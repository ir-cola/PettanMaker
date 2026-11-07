/**===========================================================================

 File   : ステージ入場シネマ演出 [StageIntroSequence.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/03
 -----------------------------------------------------------------------------
 ステージ入場時の導入演出を統括する司令塔。
 カメラを数カットで見せる → 地面のシールを剥がす → Enemy02 を召喚 →
 通常の後追いカメラへ戻して操作を返す、までを一連で再生する。
 演出中はプレイヤー入力を凍結し接地位置へピン留め。演出中に入力すると
 右下に案内を出し Enter でスキップできる (シール剥がし→召喚は省略しない)。
 ===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class StageIntroSequence : MonoBehaviour {

    [Header("参照 (未設定は実行時に自動取得)")]
    [SerializeField] private PlayerLifetimeScope player;
    [SerializeField] private TPSCameraController cameraController;
    [Tooltip("地面のシール (WhiteBunnySticker)。DecalProjector を持つ想定")]
    [SerializeField] private Transform stickerTarget;

    [Header("召喚")]
    [Tooltip("召喚する Enemy02 プレハブ")]
    [SerializeField] private GameObject enemyPrefab;
    [Tooltip("召喚後、演出終了時に AI を有効化して実際の敵にする (逃走ルートが無い場合)")]
    [SerializeField] private bool activateEnemyAfterIntro = true;

    [Header("白兎の逃走ルート")]
    [Tooltip("逃走ルートのルート。子 (順番) = 通過ポイント。未設定なら 'EscapeRoute' を探す。" +
        "見つからなければ従来通り通常 AI を有効化する。")]
    [SerializeField] private Transform escapeRouteRoot;
    [Tooltip("逃走の移動速度。参考: プレイヤーの Speed は 6")]
    [SerializeField] private float escapeRunSpeed = 3.5f;
    [Tooltip("プレイヤーが近づいたときの加速倍率")]
    [SerializeField] private float escapeBoostMultiplier = 1.6f;
    [Tooltip("この距離までプレイヤーが近づくと加速")]
    [SerializeField] private float escapeBoostRadius = 6f;
    [Tooltip("ポイント間ジャンプの高さ (放物線の頂点。段差を飛び越えられる高さにする)")]
    [SerializeField] private float escapeJumpHeight = 1.5f;
    [Tooltip("終点後に駆け回る広場の範囲。'EscapeRoom' という名前のコライダー付きオブジェクトを自動検出。" +
        "範囲定義用なので IsTrigger 推奨 (solid なら実行時にトリガーへ切替)")]
    [SerializeField] private Collider escapeAreaCollider;
    [Tooltip("EscapeRoom が無いときの代替: 終点の先を中心にこの半径の円内を駆け回る")]
    [SerializeField] private float escapeRoamRadius = 10f;
    [Tooltip("true でルートのジャンプを周回し続ける (広場駆け回りへは移行しない)")]
    [SerializeField] private bool escapeLoop = false;

    [Header("召喚時の一言")]
    [Tooltip("白兎召喚時に1文字ずつスタンプ風に表示する文言。空なら表示しない")]
    [SerializeField] private string summonCalloutText = "シロウサギをつかまえろ！";

    [Header("剥がしシール")]
    [Tooltip("World/StickerPeel を使うマテリアル。未設定なら実行時に生成する")]
    [SerializeField] private Material peelMaterial;
    [Tooltip("シールの絵。未設定ならデカールの Base_Map を使う")]
    [SerializeField] private Texture stickerTexture;
    [Tooltip("シール板ポリのサイズ (m)。デカールに合わせる (3x3)")]
    [SerializeField] private float stickerSize = 3f;
    [Tooltip("地面からの浮かせ量 (z-fight 回避)")]
    [SerializeField] private float stickerLift = 0.02f;
    [SerializeField] private float peelDuration = 1.2f;
    [Tooltip("剥がれ切った後、ひらんっと舞ってディゾルブで消えるまでの時間 (秒)")]
    [SerializeField] private float peelVanishDuration = 0.9f;
    [SerializeField] private float peelDirX = 1f;
    [SerializeField] private float peelDirY = 1f;
    [SerializeField] private bool peelFlipY = false;

    [Header("カメラパス (ショット制: 番号ごとにカット、ショット内は移動)")]
    [Tooltip("ショットのルート。子=各ショット(番号順)。ショットの子=そのショット内の移動ポイント(順番)。" +
        "ショット直下が葉(子なし)なら静止ショット。ショット間はカットで切替。" +
        "未設定なら 'CamWaypoints' を探し、無ければ自動生成する。")]
    [SerializeField] private Transform waypointRoot;
    [Tooltip("ショット内をポイント間で移動する時間 (秒)")]
    [SerializeField] private float shotMoveTime = 3.0f;
    [Tooltip("各ショットを見せる静止時間 (秒)。ショット末で待ってから次へカット")]
    [SerializeField] private float shotHold = 1.2f;
    [Tooltip("最後のショット(シール寄り)で剥がす前に待つ時間")]
    [SerializeField] private float settleHold = 0.3f;
    [Tooltip("召喚後、敵を見せる時間")]
    [SerializeField] private float admireHold = 1.0f;
    [Tooltip("演出終了時、通常追従へ戻る移動時間")]
    [SerializeField] private float finalMoveTime = 1.4f;

    [Header("起動")]
    [SerializeField] private bool playOnStart = true;
    [Tooltip("ステージシーンを直接 Play したときにロード画面を出す")]
    [SerializeField] private bool showLoadingOnColdBoot = true;
    [SerializeField] private float coldBootMinShow = 0.8f;
    [Tooltip("何か入力で右下にスキップ案内を表示し、Enter で演出をスキップできるようにする")]
    [SerializeField] private bool allowSkip = true;

    [Header("スキップ案内")]
    [Tooltip("スキップ案内のフォント。未設定ならエディタ実行時は RabbitandFullMoon SDF を自動ロード")]
    [SerializeField] private TMP_FontAsset skipPromptFont;
    [SerializeField] private string skipPromptText = "Enterでスキップ";
    [SerializeField] private float skipPromptFontSize = 40f;

    // ---- 内部 ----
    /** イントロ演出が進行中か（チュートリアル表示などの抑制用） */
    public static bool InProgress { get; private set; }

    private struct Pose { public Vector3 pos; public Quaternion rot; }

    private DecalProjector _stickerDecal;
    private Renderer _stickerRenderer;   // シールが板ポリ(スプライト)の場合の自前レンダラ
    private Transform _camTf;

    private List<List<Pose>> _shots;     // ショット毎のポイント列。ショット間はカット、ショット内は移動
    private Pose _finalPose;

    private GameObject _peelQuad;
    private Material _quadMatInstance;
    private GameObject _enemyInstance;
    private Vector3 _enemyOriginalScale = Vector3.one;
    private readonly List<GameObject> _sceneEnemies = new();   // 演出中に凍結したシーン配置の敵

    private bool _setupDone;
    private bool _revealed;
    private bool _beginImmediately;
    private bool _skipRequested;
    private bool _promptShown;           // スキップ案内を表示済みか (表示後の Enter で確定)
    private bool _finalizing;
    private Coroutine _intro;
    private GameObject _skipUI;          // 実行時生成のスキップ案内 Canvas
    private bool _pinPlayer;             // 演出中プレイヤーを接地位置へ固定
    private Vector3 _frozenPlayerPos;
    private bool _lockFirstShot;         // ロード画面〜イントロ開始までカメラを最初のショットへ固定(プレイ画角のチラ見え防止)
    private Pose _firstShotPose;
    private bool _cameraPlaced;          // カメラを最初のショットへ配置済みか(Awake で先行実行)

    // ======================================================================
    void Awake() {
        InProgress = playOnStart;   // 演出終了 (RestoreControl) までチュートリアル等を抑制

        // 入力を「演出開始より前(Awake)」で即ロックする。
        // 従来は FreezePlayer(true) が演出コルーチン内 (接地記録時) まで走らず、
        // ロード剥がし〜演出開始の隙にプレイヤーが移動/攻撃できた (#12/#16)。
        if (playOnStart) {
            if (player == null) player = FindObjectOfType<PlayerLifetimeScope>();
            if (player != null) player.SetUIMode(true);
        }

        // 【重要】カメラを最初のショットへ「ロード画面が動く前(Awake)」に配置する。
        //   従来はカメラ配置(EnsureSetup)が Start / OnLoadRevealed で走っていたため、
        //   ロード剥がしの瞬間はまだ既定(プレイ)画角で、剥がした直後にイントロ初期位置へ
        //   ワープして見えた。ここで先に置いておけば、ロード中〜剥がし=常に初期ショット。
        if (playOnStart) PlaceCameraFirstShot();

        // ロード画面が出る前(Awake)に HUD を隠す。従来は Start の EnsureSetup で隠していたため、
        // ロード画面が覆う前の 1 フレームだけ HUD が見えてしまう隙があった。ここで先に隠す。
        if (playOnStart) HUDIntroReveal.HideForIntro();

        if (!SceneLoader.IsActive) {
            // 遷移を経ていない = ステージを直接 Play。
            if (showLoadingOnColdBoot)
                SceneLoader.CoverAndReveal(coldBootMinShow, OnLoadRevealed);
            else
                _beginImmediately = true;   // ロード画面無しで即開始 (Start 後に1フレーム置いて)
        }
        else {
            // 遷移経由: ロード画面の剥がし完了を起点に開始。
            SceneLoader.OnComplete += OnLoadRevealed;
        }
    }

    void OnDestroy() {
        InProgress = false;
        SceneLoader.OnComplete -= OnLoadRevealed;
        if (_quadMatInstance != null) Destroy(_quadMatInstance);
        if (_skipUI != null) Destroy(_skipUI);
    }

    void Start() {
        EnsureSetup();
        if (_beginImmediately) StartCoroutine(BeginNextFrame());
    }

    IEnumerator BeginNextFrame() {
        yield return null;      // カメラを初期カットへ置いてから開始
        OnLoadRevealed();
    }

    // カメラだけを最初のショットへ即セットする軽量版。ロード画面が覆う/剥がす前(Awake)に
    // 呼び、ロード中〜剥がしの間ずっとイントロ初期画角を映す。プレイヤー凍結など重い処理は
    // EnsureSetup 側に残す(Awake では他オブジェクト未初期化の恐れがあるため最小限に留める)。冪等。
    void PlaceCameraFirstShot() {
        if (_cameraPlaced) return;
        _cameraPlaced = true;

        ResolveRefs();
        SuspendCamera();               // 追従カメラを止めてこちらで画角を固定
        BuildShots();
        if (_shots != null && _shots.Count > 0 && _shots[0].Count > 0) {
            _firstShotPose = _shots[0][0];
            ApplyPose(_firstShotPose);   // ロード中からこの絵。剥がし後もそのまま(ワープしない)
            _lockFirstShot = true;       // イントロ開始までこの画角へ固定
        }
    }

    // 演出前の待機状態を作る (ロード画面に覆われている間に済ませておく)。冪等。
    void EnsureSetup() {
        if (_setupDone) return;
        _setupDone = true;

        ResolveRefs();
        if (!playOnStart) return;

        // イントロ中は HUD を隠す。バナー(シロウサギをつかまえろ)消滅後に
        // HUD がシールのように貼り込まれ、最後に HP ハートがパラララと並ぶ。
        HUDIntroReveal.HideForIntro();

        SnapPlayerToGround();
        FreezePlayer(true);        // 接地させた位置を凍結位置として記録する (順序注意)
        FreezeSceneEnemies(true);
        PlaceCameraFirstShot();    // カメラ配置(Awake で済んでいれば冪等に素通り)
    }

    void OnLoadRevealed() {
        SceneLoader.OnComplete -= OnLoadRevealed;
        if (_revealed) return;
        _revealed = true;

        EnsureSetup();
        if (!playOnStart) { RestoreControl(); return; }
        _intro = StartCoroutine(RunIntro());
    }

    // ======================================================================
    //  メインシーケンス
    // ======================================================================
    IEnumerator RunIntro() {
        try {
        _lockFirstShot = false;   // ここからカメラ演出がカメラを制御する(固定解除)

        // ショットを番号順に見せる。ショットの先頭へ「カット」→ ショット内は移動 → 静止 → 次へカット。
        // 最後のショット(シール寄り)で剥がし→召喚を行う。
        for (int s = 0; s < _shots.Count; s++) {
            var shot = _shots[s];
            if (shot.Count == 0) continue;
            bool isLast = (s == _shots.Count - 1);

            ApplyPose(shot[0]);                         // カット切替 (瞬間)
            for (int k = 1; k < shot.Count; k++) {      // ショット内はなめらかに移動
                yield return MoveTo(shot[k], shotMoveTime);
                if (_skipRequested) { yield return FastForward(); yield break; }
            }

            if (isLast) break;
            yield return HoldOrSkip(shotHold);          // 見せて待つ → 次ショットへカット
            if (_skipRequested) { yield return FastForward(); yield break; }
        }

        // 最後のショットで少し待ってからシールを剥がす
        yield return HoldOrSkip(settleHold);
        if (_skipRequested) { yield return FastForward(); yield break; }

        yield return PeelSticker(peelDuration);
        if (_skipRequested) { yield return FastForward(); yield break; }

        // Enemy02 召喚 (ポップ登場)
        SummonEnemy();
        yield return HoldOrSkip(admireHold);

        // プレイヤー背後の後追い姿勢へなめらかに戻して復帰 (ゲーム開始はカットでなく滑らかに)
        _finalizing = true;
        _finalPose = ComputeFinalPose();
        yield return MoveTo(_finalPose, finalMoveTime);
        FinishIntro();
        }
        finally {
            // 例外・中断・シーン遷移などで途中終了しても、プレイヤー固定とカメラ停止を必ず解除する。
            // 正常完了/スキップ時は FinishIntro→RestoreControl 済みで InProgress=false なので何もしない。
            if (InProgress) {
                if (_skipUI != null) { Destroy(_skipUI); _skipUI = null; }
                RestoreControl();
                _intro = null;
            }
        }
    }

    // スキップ時: カメラ回しだけ飛ばし、シール剥がし → Enemy02(白兎) スポーンは必ず見せる
    IEnumerator FastForward() {
        _finalizing = true;             // 以降は CheckSkip が効かない = スポーン演出はスキップ不可
        HideSkipPrompt();

        if (_enemyInstance == null) {
            // シール寄りショットへカットし、剥がし → ポップ登場は通常と同じ速度で再生
            if (_shots != null && _shots.Count > 0) {
                var last = _shots[_shots.Count - 1];
                if (last.Count > 0) ApplyPose(last[last.Count - 1]);
            }
            if (!_peelDone) yield return PeelSticker(peelDuration);   // 消滅済みなら再生しない
            SummonEnemy();
            yield return HoldOrSkip(admireHold);   // ポップ登場を見せる (_finalizing 中はただの待ち)
        }

        _finalPose = ComputeFinalPose();
        yield return MoveTo(_finalPose, 0.35f);
        FinishIntro();
    }

    void FinishIntro() {
        HideSticker();
        if (_skipUI != null) { Destroy(_skipUI); _skipUI = null; }
        if (cameraController != null) {
            cameraController.ResyncFromCurrentView();
            cameraController.enabled = true;
        }
        if (_enemyInstance != null) {
            if (escapeRouteRoot != null && escapeRouteRoot.childCount > 0) {
                // 白兎は決められたルートをジャンプで渡り、終点後は広場を駆け回って逃げる
                var esc = _enemyInstance.AddComponent<Enemy02_RouteEscape>();
                esc.Init(escapeRouteRoot, player != null ? player.transform : null,
                         escapeRunSpeed, escapeBoostMultiplier, escapeBoostRadius,
                         escapeLoop, escapeJumpHeight, escapeRoamRadius, escapeAreaCollider);
            }
            else if (activateEnemyAfterIntro) {
                SetEnemyAI(_enemyInstance, true);
            }
        }
        RestoreControl();
        _intro = null;
    }

    void RestoreControl() {
        InProgress = false;
        FreezePlayer(false);
        FreezeSceneEnemies(false);
        if (cameraController != null && !cameraController.enabled) cameraController.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ======================================================================
    //  カメラ
    // ======================================================================
    void SuspendCamera() {
        if (cameraController != null) cameraController.enabled = false;
        EnforceGameFov();   // TPS 停止中に Cinemachine 等が残した FOV(例:ボスvcamの90)を戻す
    }

    void ApplyPose(Pose p) {
        if (_camTf == null) return;
        _camTf.position = p.pos;
        _camTf.rotation = p.rot;
        EnforceGameFov();
    }

    // TPS を止めているイントロ中、カメラFOVをゲーム標準へ強制する。
    // Boss01_Stage が持ち込む Cinemachine の CinemachineBrain が Main Camera の FOV を
    // ボス vcam のレンズ(90)へ書き換えて残すため、TPS 停止中のショットが広角に化けて
    // 「ポイントより遠く/ずれて見える」症状になる。ここで毎カット baseFov(=50) へ戻す。
    void EnforceGameFov() {
        if (_camTf == null) return;
        var c = _camTf.GetComponent<Camera>();
        if (c != null && cameraController != null) c.fieldOfView = cameraController.BaseFov;
    }

    IEnumerator MoveTo(Pose target, float moveTime) {
        if (_camTf == null) yield break;
        Vector3 p0 = _camTf.position;
        Quaternion r0 = _camTf.rotation;
        float dur = Mathf.Max(0.01f, moveTime);
        float t = 0f;
        while (t < dur) {
            if (CheckSkip()) yield break;
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / dur);
            float e = x * x * (3f - 2f * x);              // smoothstep (両端イーズ)
            _camTf.position = Vector3.LerpUnclamped(p0, target.pos, e);
            _camTf.rotation = Quaternion.SlerpUnclamped(r0, target.rot, e);
            yield return null;
        }
        _camTf.position = target.pos;
        _camTf.rotation = target.rot;
    }

    IEnumerator HoldOrSkip(float sec) {
        float t = 0f;
        while (t < sec) {
            if (CheckSkip()) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    Pose ComputeFinalPose() {
        Pose p = default;
        float yaw = (player != null) ? player.transform.eulerAngles.y : 0f;
        if (cameraController != null) {
            cameraController.GetHomeFollowPose(yaw, out var pos, out var rot);
            p.pos = pos; p.rot = rot;
        }
        else if (_camTf != null) {
            p.pos = _camTf.position; p.rot = _camTf.rotation;
        }
        return p;
    }

    // ショットを構築する。waypointRoot(既定は "CamWaypoints") の直下の子=各ショット。
    // ・ショットに子があれば、その子列をショット内の移動ポイント(順番)として使う
    // ・ショットが葉(子なし)なら、それ自身を静止ショットとして使う
    // 何も無ければ自動生成にフォールバック。
    void BuildShots() {
        if (_shots != null) return;
        _shots = new List<List<Pose>>();

        Transform root = ResolveWaypointRoot();
        if (root != null) {
            for (int i = 0; i < root.childCount; i++) {
                var shotT = root.GetChild(i);
                if (shotT == null || !shotT.gameObject.activeInHierarchy) continue;
                var poses = new List<Pose>();
                if (shotT.childCount > 0) {
                    for (int j = 0; j < shotT.childCount; j++) {
                        var pt = shotT.GetChild(j);
                        if (pt != null && pt.gameObject.activeInHierarchy) poses.Add(PoseFrom(pt));
                    }
                }
                else {
                    poses.Add(PoseFrom(shotT));
                }
                if (poses.Count > 0) _shots.Add(poses);
            }
        }

        if (_shots.Count == 0) BuildDefaultShots();
    }

    Transform ResolveWaypointRoot() {
        if (waypointRoot != null) return waypointRoot;
        var go = GameObject.Find("CamWaypoints");
        return go != null ? go.transform : null;
    }

    static Pose PoseFrom(Transform t) {
        Pose p; p.pos = t.position; p.rot = t.rotation; return p;
    }

    // 自動フォールバック: シール周りを数ショットのカットで見せて寄る。
    void BuildDefaultShots() {
        Vector3 S = (stickerTarget != null) ? stickerTarget.position : transform.position;
        Vector3 f = S + Vector3.up * 0.8f;
        _shots.Add(new List<Pose> { OrbitPose(f, 210f, 16f, 4f), OrbitPose(f, 205f, 15f, 13f) }); // クレーンアップ
        _shots.Add(new List<Pose> { OrbitPose(f, 110f, 15f, 9f), OrbitPose(f, 55f, 13f, 8f) });   // 横流し
        _shots.Add(new List<Pose> { OrbitPose(f, 15f, 12f, 9f) });                                // 正面
        Vector3 sp = S + new Vector3(0f, 6.5f, -3.2f);                                            // シール寄り(剥がし)
        _shots.Add(new List<Pose> { new Pose { pos = sp, rot = Quaternion.LookRotation((S - sp).normalized, Vector3.up) } });
    }

    Pose OrbitPose(Vector3 focus, float angleDeg, float radius, float height) {
        Vector3 center = (stickerTarget != null) ? stickerTarget.position : transform.position;
        float a = angleDeg * Mathf.Deg2Rad;
        Vector3 pos = center + new Vector3(Mathf.Sin(a) * radius, height, Mathf.Cos(a) * radius);
        Vector3 dir = focus - pos;
        Pose p; p.pos = pos;
        p.rot = (dir.sqrMagnitude > 1e-6f) ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
        return p;
    }

    // ======================================================================
    //  剥がし
    // ======================================================================
    private bool _peelDone;    // 剥がし+消滅まで完了したか (スキップ時の再生し直し判定)

    IEnumerator PeelSticker(float duration) {
        Material mat = ResolvePeelMaterial();
        if (mat != null) {
            StickerPeelFX.PrepareMaterial(mat);     // _Peel=0 + 上方向カール半径
            if (mat.HasProperty("_DirX")) mat.SetFloat("_DirX", peelDirX);
            if (mat.HasProperty("_DirY")) mat.SetFloat("_DirY", peelDirY);
            if (mat.HasProperty("_FlipY")) mat.SetFloat("_FlipY", peelFlipY ? 1f : 0f);
        }

        float t = 0f;
        while (t < duration) {
            if (CheckSkip()) yield break;
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / duration);
            float p = x * Mathf.Sqrt(x);        // イーズイン x^1.5 (早めに動き出しつつ最後にペラッ)
            if (mat != null) mat.SetFloat("_Peel", p);
            yield return null;
        }
        if (mat != null) mat.SetFloat("_Peel", 1f);
        _peelDone = true;

        // ひらんっと舞ってディゾルブで消える演出は「裏で」再生する。
        // これを待たずに呼び出し元が即 SummonEnemy するので、白兎の登場が早い
        // (シールが舞い上がる下から白兎がポップする見た目になる)。
        Transform peelTf = (_stickerRenderer != null) ? _stickerRenderer.transform
                         : (_peelQuad != null) ? _peelQuad.transform : null;
        StartCoroutine(VanishSticker(peelTf, mat));
    }

    // 剥がし方向へ飛ばしつつディゾルブ → 消え切ったら描画も止める (PeelSticker の後追い演出)
    IEnumerator VanishSticker(Transform peelTf, Material mat) {
        yield return StickerPeelFX.FlyOffAndDissolve(peelTf, mat, peelVanishDuration, CheckSkip);
        HideSticker();
    }

    // シールを剥がすための _Peel マテリアルを得る。シール自身が板ポリ(World/StickerPeel)なら
    // そのマテリアルインスタンスを使う。デカールしか無ければ従来通りデカールを消して板ポリを生成。
    Material ResolvePeelMaterial() {
        if (_stickerRenderer != null && _stickerRenderer.sharedMaterial != null
            && _stickerRenderer.sharedMaterial.HasProperty("_Peel")) {
            // 3D カールは頂点変形なので、組込み Quad (4頂点) なら分割グリッドへ差し替える
            var mf = _stickerRenderer.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount <= 8)
                mf.sharedMesh = StickerQuadMesh.Grid;
            return _stickerRenderer.material;   // インスタンス化 (共有アセットは汚さない)
        }
        if (_stickerDecal != null) _stickerDecal.enabled = false;
        EnsurePeelQuad();
        return _quadMatInstance;
    }

    // シールの見た目を完全に消す (自前レンダラ / デカール / 生成した板ポリ すべて対応)。
    void HideSticker() {
        if (_stickerRenderer != null) _stickerRenderer.enabled = false;
        if (_stickerDecal != null) _stickerDecal.enabled = false;
        if (_peelQuad != null) _peelQuad.SetActive(false);
    }

    void EnsurePeelQuad() {
        if (_peelQuad != null) { _peelQuad.SetActive(true); return; }

        _peelQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _peelQuad.name = "StageIntro_PeelQuad";
        var col = _peelQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);
        // 3D カール (頂点変形) 用に分割グリッドへ差し替え
        _peelQuad.GetComponent<MeshFilter>().sharedMesh = StickerQuadMesh.Grid;

        Transform tf = _peelQuad.transform;
        Vector3 basePos = (stickerTarget != null) ? stickerTarget.position : transform.position;
        tf.position = basePos + Vector3.up * stickerLift;
        tf.rotation = Quaternion.Euler(90f, 0f, 0f);          // 地面に寝かせる (デカールと同じ向き)
        tf.localScale = new Vector3(stickerSize, stickerSize, 1f);

        var mr = _peelQuad.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _quadMatInstance = BuildPeelMaterial();
        mr.sharedMaterial = _quadMatInstance;
    }

    Material BuildPeelMaterial() {
        Texture tex = stickerTexture;
        if (tex == null && _stickerDecal != null && _stickerDecal.material != null) {
            var m = _stickerDecal.material;
            if (m.HasProperty("Base_Map")) tex = m.GetTexture("Base_Map");
            else if (m.HasProperty("_BaseMap")) tex = m.GetTexture("_BaseMap");
            else if (m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
        }

        Material mat;
        if (peelMaterial != null) mat = new Material(peelMaterial);
        else {
            var sh = Shader.Find("Sticker/World");
            mat = (sh != null) ? new Material(sh) : new Material(Shader.Find("Sprites/Default"));
        }
        if (tex != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_Peel")) mat.SetFloat("_Peel", 0f);
        if (mat.HasProperty("_DirX")) mat.SetFloat("_DirX", peelDirX);
        if (mat.HasProperty("_DirY")) mat.SetFloat("_DirY", peelDirY);
        if (mat.HasProperty("_FlipY")) mat.SetFloat("_FlipY", peelFlipY ? 1f : 0f);
        return mat;
    }

    // ======================================================================
    //  召喚
    // ======================================================================
    void SummonEnemy() {
        if (_enemyInstance != null) return;
        if (enemyPrefab == null) {
            Debug.LogWarning("[StageIntroSequence] enemyPrefab が未設定です。召喚をスキップします。");
            return;
        }

        Vector3 pos = (stickerTarget != null) ? stickerTarget.position : transform.position;
        if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;

        Quaternion rot = Quaternion.identity;
        if (player != null) {                       // プレイヤーの方を向いて出す
            Vector3 to = player.transform.position - pos; to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(to, Vector3.up);
        }

        _enemyInstance = Instantiate(enemyPrefab, pos, rot);
        _enemyOriginalScale = _enemyInstance.transform.localScale;
        SetEnemyAI(_enemyInstance, false);          // 演出中は AI を止めてアイドルポーズを保持

        // ポップ登場
        _enemyInstance.transform.localScale = Vector3.zero;
        StartCoroutine(PopScale(_enemyInstance.transform, _enemyOriginalScale, 0.35f));

        // 「シロウサギをつかまえろ！」を1文字ずつスタンプ風に表示
        if (!string.IsNullOrEmpty(summonCalloutText)) StampTextBanner.Show(summonCalloutText);
    }

    IEnumerator PopScale(Transform tf, Vector3 target, float dur) {
        float t = 0f;
        while (t < dur && tf != null) {
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / dur);
            // easeOutBack (少しオーバーシュートする "ポン" 感)
            float s = 1.70158f;
            float u = x - 1f;
            float e = 1f + (s + 1f) * u * u * u + s * u * u;
            tf.localScale = target * e;
            yield return null;
        }
        if (tf != null) tf.localScale = target;
    }

    // NavMeshAgent と挙動スクリプト(EnemyBase 派生)だけを切替。Health/Death 等は触らない。
    void SetEnemyAI(GameObject go, bool on) {
        if (go == null) return;
        var nav = go.GetComponent<NavMeshAgent>();
        if (nav != null) nav.enabled = on;
        foreach (var mb in go.GetComponents<MonoBehaviour>()) {
            if (mb != null && mb is EnemyBase) mb.enabled = on;
        }
    }

    // シーンに直接配置された敵の AI を演出中だけ止める。
    // 止めないと、操作を凍結されたプレイヤーがムービー中に追跡・攻撃される。
    // (SummonEnemy で出す _enemyInstance は凍結後に生成されるためこのリストには入らない)
    void FreezeSceneEnemies(bool freeze) {
        if (freeze) {
            _sceneEnemies.Clear();
            foreach (var enemy in FindObjectsOfType<EnemyBase>()) {
                _sceneEnemies.Add(enemy.gameObject);
                SetEnemyAI(enemy.gameObject, false);
            }
        }
        else {
            foreach (var go in _sceneEnemies) SetEnemyAI(go, true);
            _sceneEnemies.Clear();
        }
    }

    // ======================================================================
    //  参照解決 / プレイヤー凍結 / スキップ
    // ======================================================================
    void ResolveRefs() {
        if (player == null) player = FindObjectOfType<PlayerLifetimeScope>();
        if (cameraController == null) cameraController = TPSCameraController.Instance;
        if (cameraController == null) cameraController = FindObjectOfType<TPSCameraController>();
        if (stickerTarget == null) {
            var go = GameObject.Find("WhiteBunnySticker");
            if (go != null) stickerTarget = go.transform;
        }
        if (stickerTarget != null) {
            _stickerDecal = stickerTarget.GetComponent<DecalProjector>();
            _stickerRenderer = stickerTarget.GetComponent<Renderer>();
        }
        if (escapeRouteRoot == null) {
            var er = GameObject.Find("EscapeRoute");
            if (er != null) escapeRouteRoot = er.transform;
        }
        if (escapeAreaCollider == null) {
            var room = GameObject.Find("EscapeRoom");
            if (room != null) escapeAreaCollider = room.GetComponent<Collider>();
        }
        if (cameraController != null) _camTf = cameraController.transform;
        else if (Camera.main != null) _camTf = Camera.main.transform;
    }

    void FreezePlayer(bool freeze) {
        if (player != null) player.SetUIMode(freeze);
        // ※ applyRootMotion はここで切り替えない。Generic リグで false にすると
        //   ルートモーション由来のポーズ補正が外れ、モデルが腰の高さぶん宙に浮く。
        //   位置ずれ防止は LateUpdate のピン留めが担う。
        _pinPlayer = freeze && player != null;
        if (_pinPlayer) _frozenPlayerPos = player.transform.position;
    }

    // 演出中はプレイヤーを接地位置へ固定する (アニメーションや外力での位置ずれ防止)。
    // Animator の適用後 (LateUpdate) に上書きすることで確実に効かせる。
    void LateUpdate() {
        if (_pinPlayer && player != null) player.transform.position = _frozenPlayerPos;
        if (_lockFirstShot) ApplyPose(_firstShotPose);   // イントロ開始までカメラを最初のショットへ固定
    }

    // UIMode 中は PlayerMover.ApplyGravity が早期 return して重力が止まるため、
    // シーン上で地面より高い位置に置かれたプレイヤーは演出の間ずっと宙に浮き、
    // 操作復帰の瞬間に地面へワープして見える。凍結したタイミングで接地させておく。
    void SnapPlayerToGround() {
        if (player == null) return;

        int mask = ~0;
        int playerLayer = LayerMask.NameToLayer("Player");
        int weaponLayer = LayerMask.NameToLayer("Weapon");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        if (weaponLayer >= 0) mask &= ~(1 << weaponLayer);

        Transform tf = player.transform;
        if (!Physics.Raycast(tf.position + Vector3.up * 0.5f, Vector3.down,
                out RaycastHit hit, 100f, mask, QueryTriggerInteraction.Ignore)) return;

        // CharacterController のカプセル底面が地面に乗る高さへ (skinWidth ぶん浮かせる)
        var cc = player.GetComponent<CharacterController>();
        float footOffset = cc != null ? cc.center.y - cc.height * 0.5f - cc.skinWidth : 0f;
        bool ccWasEnabled = cc != null && cc.enabled;
        if (ccWasEnabled) cc.enabled = false;
        Vector3 pos = tf.position;
        pos.y = hit.point.y - footOffset;
        tf.position = pos;
        if (ccWasEnabled) cc.enabled = true;
    }

    // 2段階スキップ:
    //   1. 演出中に何か入力 → 右下に「Enterでスキップ」案内を表示
    //   2. 案内表示中に Enter (パッドは Start) → スキップ確定
    bool CheckSkip() {
        if (_finalizing) return false;
        if (!allowSkip) return false;
        if (_skipRequested) return true;

        if (!_promptShown) {
            if (AnyIntroInput()) ShowSkipPrompt();
            return false;   // 最初の入力では案内を出すだけでスキップしない
        }
        if (SkipConfirmInput()) _skipRequested = true;
        return _skipRequested;
    }

    bool AnyIntroInput() {
        var k = Keyboard.current;
        if (k != null && (k.anyKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame)) return true;
        var g = Gamepad.current;
        if (g != null && (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame)) return true;
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) return true;
        return false;
    }

    // スキップ確定入力 (Enter。ゲームパッドは Start を Enter 相当として扱う)
    bool SkipConfirmInput() {
        var k = Keyboard.current;
        if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) return true;
        var g = Gamepad.current;
        if (g != null && g.startButton.wasPressedThisFrame) return true;
        return false;
    }

    // ======================================================================
    //  スキップ案内 UI (実行時生成: シーンへの配置不要)
    // ======================================================================
    void ShowSkipPrompt() {
        _promptShown = true;
        if (_skipUI != null) { _skipUI.SetActive(true); return; }

        _skipUI = new GameObject("StageIntro_SkipPrompt");
        var canvas = _skipUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var scaler = _skipUI.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textGo = new GameObject("SkipText");
        textGo.transform.SetParent(_skipUI.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = skipPromptText;
        text.fontSize = skipPromptFontSize;
        text.alignment = TextAlignmentOptions.BottomRight;
        text.color = new Color(1f, 1f, 1f, 0.92f);
        text.raycastTarget = false;
        TMP_FontAsset font = ResolveSkipFont();
        if (font != null) text.font = font;

        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);   // 右下アンカー
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(640f, 80f);
        rt.anchoredPosition = new Vector2(-48f, 36f);
    }

    void HideSkipPrompt() {
        if (_skipUI != null) _skipUI.SetActive(false);
    }

    TMP_FontAsset ResolveSkipFont() {
        if (skipPromptFont != null) return skipPromptFont;
#if UNITY_EDITOR
        skipPromptFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/_Project/Fonts/RabbitandFullMoon SDF.asset");
        if (skipPromptFont != null) return skipPromptFont;
#endif
        // ビルドでは Inspector 未設定時、読み込み済みアセットから名前一致で探す
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) {
            if (f != null && f.name.StartsWith("RabbitandFullMoon")) { skipPromptFont = f; return f; }
        }
        return null;    // 見つからなければ TMP 既定フォント
    }

    // シーンビューでカメラパスを可視化。ショット毎に色分けし、ショット内の移動は線で結ぶ。
    // ショット間はカット(線を引かない)。黄色い線=各ポイントのカメラの向き。
    // ポイントを選んで動かす/回すだけでイントロのカメラを調整できる。
    // 白兎の逃走ルート (EscapeRoute の子) は緑の球+線で描く。
    void OnDrawGizmos() {
        DrawEscapeRouteGizmo();

        Transform root = ResolveWaypointRoot();
        if (root == null) return;
        for (int s = 0; s < root.childCount; s++) {
            var shotT = root.GetChild(s);
            if (shotT == null) continue;
            Color c = ShotColor(s);
            Transform prev = null;
            if (shotT.childCount > 0) {
                for (int j = 0; j < shotT.childCount; j++) {
                    var pt = shotT.GetChild(j);
                    if (pt == null) continue;
                    DrawWaypointGizmo(pt, c);
                    if (prev != null) { Gizmos.color = c; Gizmos.DrawLine(prev.position, pt.position); }
                    prev = pt;
                }
            }
            else {
                DrawWaypointGizmo(shotT, c);   // 葉 = 静止ショット
            }
        }
    }

    void DrawWaypointGizmo(Transform t, Color c) {
        Gizmos.color = c;
        Gizmos.DrawWireSphere(t.position, 0.4f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(t.position, t.forward * 2.5f);   // カメラの向き
    }

    void DrawEscapeRouteGizmo() {
        Transform root = escapeRouteRoot;
        if (root == null) {
            var go = GameObject.Find("EscapeRoute");
            if (go != null) root = go.transform;
        }
        if (root != null) {
            Gizmos.color = new Color(0.3f, 1f, 0.3f);
            Transform prev = null;
            for (int i = 0; i < root.childCount; i++) {
                var pt = root.GetChild(i);
                if (pt == null) continue;
                Gizmos.DrawWireSphere(pt.position, 0.35f);
                if (prev != null) Gizmos.DrawLine(prev.position, pt.position);
                prev = pt;
            }
        }

        // 駆け回りエリア (EscapeRoom) の範囲を緑の箱で表示
        Collider area = escapeAreaCollider;
        if (area == null) {
            var room = GameObject.Find("EscapeRoom");
            if (room != null) area = room.GetComponent<Collider>();
        }
        if (area != null) {
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            Gizmos.DrawWireCube(area.bounds.center, area.bounds.size);
        }
    }

    static Color ShotColor(int s) {
        switch (s % 6) {
            case 0: return new Color(0.3f, 0.9f, 1f);
            case 1: return new Color(1f, 0.6f, 0.2f);
            case 2: return new Color(0.5f, 1f, 0.4f);
            case 3: return new Color(1f, 0.4f, 0.7f);
            case 4: return new Color(0.8f, 0.7f, 1f);
            default: return new Color(1f, 1f, 0.4f);
        }
    }
}
