/**===========================================================================

 File   : ステージクリア出口ゲート [StageClearGate.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/18
 -----------------------------------------------------------------------------
 SecondPhase の全ウェーブ (Phase1〜3) を殲滅すると:
   1. カメラを奪って OutGate へ寄せる (ショーケース演出)
   2. OutGate を「シールを壁に貼る」アニメ(_Peel 1→0 + 接着シマー)で出現させる
   3. カメラをプレイヤーへ返し、F プロンプト (TutorialBillboard) を表示
   4. プレイヤーがゲートに近づいて F (パッドは RT) を押すと
      黒フェードアウト → OutGate_1/P0 へワープ → フェードインで「外に出る」

   OutGate    … 出口判定＋シール出現させる本体 (開始時は隠す)
   OutGate_1  … 飾り。子 P0 がワープ先。開始時から表示のまま (触らない)

 ■ 貼り付けは Sticker/World の頂点カール(_Peel)を使う。カールには分割メッシュが
   必要なので、SpriteRenderer では曲がらない → StickerQuadMesh.Grid の板ポリを
   一時生成して貼り、完了後に実 SpriteRenderer へ差し替える (SecondPhaseSequence と同流儀)。
   壁向きシールなので回転は OutGate と同じ(front=-Z がプレイヤー側)。

 ■ すべての演出は Time.unscaledXxx で駆動する。
   撃破の瞬間 GlobalHitStop が Time.timeScale を落とすため、スケール時間だと
   貼り付けが途中で止まって見える (= 以前のバグ)。unscaled なら影響を受けない。
 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class StageClearGate : MonoBehaviour {

    [Header("参照")]
    [Tooltip("クリア通知元。未設定ならシーンから自動取得")]
    [SerializeField] private SecondPhaseSequence sequence;
    [Tooltip("シール出現させる出口ゲート (OutGate) の SpriteRenderer")]
    [SerializeField] private SpriteRenderer revealGate;
    [Tooltip("F プロンプトのルート (TutorialBillboard を持つオブジェクト)")]
    [SerializeField] private GameObject fPrompt;
    [Tooltip("ワープ先 (OutGate_1/P0)")]
    [SerializeField] private Transform warpTarget;
    [Tooltip("プレイヤー。未設定なら Tag=Player を自動取得")]
    [SerializeField] private Transform player;

    [Header("シール貼り付け演出")]
    [Tooltip("(カメラが着いてから) 貼り付け開始までの間 (秒)")]
    [SerializeField] private float revealDelay = 0.3f;
    [Tooltip("シールが壁に貼り付く (_Peel 1→0) アニメの秒数")]
    [SerializeField] private float pasteDuration = 0.9f;
    [Tooltip("貼り付き方向 (この角/辺から密着していく)。板ローカル XY")]
    [SerializeField] private Vector2 pasteDir = new Vector2(0.7f, 1f);
    [Tooltip("カール半径 (貼り付く前の浮き上がり量)")]
    [SerializeField] private float curlRadius = 0.24f;
    [Tooltip("接着シマー(光沢の掃引)の強さ")]
    [SerializeField] private float pasteShimmer = 1.1f;
    [Tooltip("シール絵の上下反転 (逆さまに見えたら ON)")]
    [SerializeField] private bool flipY = false;

    [Header("出現時のカメラ演出")]
    [Tooltip("クリア時にカメラを OutGate へ寄せるショーケースを行う")]
    [SerializeField] private bool showcaseCamera = true;
    [Tooltip("ゲートから見たカメラ方向 (プレイヤー側=-Z、少し上から)。正規化して使用")]
    [SerializeField] private Vector3 showcaseFromDir = new Vector3(0f, 0.14f, -1f);
    [Tooltip("ゲートからカメラまでの距離")]
    [SerializeField] private float showcaseDistance = 7f;
    [Tooltip("注視点をゲート中心から上下にずらす量")]
    [SerializeField] private float showcaseLookHeight = 0.3f;
    [Tooltip("カメラがゲートへ寄る時間")]
    [SerializeField] private float cameraMoveTime = 0.7f;
    [Tooltip("出現後ゲートを見せておく余韻 (秒)")]
    [SerializeField] private float showcaseHold = 0.7f;
    [Tooltip("カメラをプレイヤーへ戻す時間")]
    [SerializeField] private float cameraReturnTime = 0.6f;

    [Header("インタラクト")]
    [Tooltip("この距離以内でゲートに近づくと F で外に出られる")]
    [SerializeField] private float interactRange = 7f;

    [Header("フェード")]
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float holdBlack = 0.25f;
    [SerializeField] private float fadeInDuration = 0.6f;
    [SerializeField] private Color fadeColor = Color.black;

    // ---- 内部 ----
    private bool _armed;
    private bool _exiting;
    private GameObject _pasteQuad;
    private Material _quadMat;
    private PlayerLifetimeScope _playerScope;
    private CanvasGroup _fadeGroup;

    // カメラ奪取用
    private TPSCameraController _cam;
    private Transform _camTf;
    private Camera _camComponent;
    private Behaviour _brain;

    private void Awake() {
        if (sequence == null) sequence = FindObjectOfType<SecondPhaseSequence>();
        if (player == null) {
            var p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null) _playerScope = player.GetComponent<PlayerLifetimeScope>();

        // 開始状態: 出口ゲートと F を隠す (OutGate_1 飾りは触らない)
        SetupGateHidden();
        if (fPrompt != null) fPrompt.SetActive(false);
    }

    private void OnEnable() {
        if (sequence != null) sequence.AllCleared += HandleCleared;
    }

    private void OnDisable() {
        if (sequence != null) sequence.AllCleared -= HandleCleared;
    }

    private void Start() {
        // 購読前に既にクリア済みだった場合の保険
        if (!_armed && sequence != null && sequence.IsAllCleared) HandleCleared();
    }

    /// <summary>出口ゲート(実スプライト)は開始時に隠すだけ。貼り付けは板ポリで行う。</summary>
    private void SetupGateHidden() {
        if (revealGate != null) revealGate.enabled = false;
    }

    private void HandleCleared() {
        if (_armed) return;
        _armed = true;
        StartCoroutine(RevealRoutine());
    }

    /// <summary>カメラをゲートへ寄せ → ディゾルブ出現 → カメラを戻し → F プロンプト表示。</summary>
    private IEnumerator RevealRoutine() {
        ResolveCameraRefs();
        bool doCam = showcaseCamera && _camTf != null && revealGate != null;

        if (doCam) {
            TakeCamera();
            GetGatePose(out Vector3 cp, out Quaternion cr);
            yield return MoveCameraUnscaled(cp, cr, cameraMoveTime);
        }

        if (revealDelay > 0f) yield return WaitUnscaled(revealDelay);

        yield return PasteOn();

        if (doCam) {
            if (showcaseHold > 0f) yield return WaitUnscaled(showcaseHold);
            yield return ReleaseCamera();
        }

        if (fPrompt != null) fPrompt.SetActive(true);
    }

    /// <summary>OutGate に合わせた分割板ポリ(壁向きシール)を _Peel=1(めくれた状態)で生成。</summary>
    private void BuildPasteQuad() {
        if (revealGate == null || revealGate.sprite == null) return;

        _pasteQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _pasteQuad.name = "OutGate_PasteSticker";
        var col = _pasteQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _pasteQuad.GetComponent<MeshFilter>().sharedMesh = StickerQuadMesh.Grid;   // カール用分割メッシュ

        Transform gt = revealGate.transform;
        Transform qt = _pasteQuad.transform;
        qt.position = gt.position;
        qt.rotation = gt.rotation;                          // OutGate と同じ壁向き (front=-Z がプレイヤー側)
        Vector3 ls = gt.lossyScale;
        Vector3 sb = revealGate.sprite.bounds.size;         // 板ローカル単位 (PPU 換算済)
        qt.localScale = new Vector3(Mathf.Abs(sb.x * ls.x), Mathf.Abs(sb.y * ls.y), 1f);

        var mr = _pasteQuad.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var sh = Shader.Find("Sticker/World");
        _quadMat = (sh != null) ? new Material(sh) : new Material(Shader.Find("Sprites/Default"));
        _quadMat.SetTexture("_MainTex", revealGate.sprite.texture);
        if (_quadMat.HasProperty("_Dissolve"))     _quadMat.SetFloat("_Dissolve", -0.2f);   // 溶けさせない = 常時表示
        if (_quadMat.HasProperty("_CurlRadius"))   _quadMat.SetFloat("_CurlRadius", curlRadius);
        if (_quadMat.HasProperty("_DirX"))         _quadMat.SetFloat("_DirX", pasteDir.x);
        if (_quadMat.HasProperty("_DirY"))         _quadMat.SetFloat("_DirY", pasteDir.y);
        if (_quadMat.HasProperty("_FlipY"))        _quadMat.SetFloat("_FlipY", flipY ? 1f : 0f);
        if (_quadMat.HasProperty("_ShimmerColor")) _quadMat.SetColor("_ShimmerColor", Color.white);
        if (_quadMat.HasProperty("_ShimmerWidth")) _quadMat.SetFloat("_ShimmerWidth", 0.16f);
        if (_quadMat.HasProperty("_Peel"))         _quadMat.SetFloat("_Peel", 1f);           // めくれ切った状態から
        if (_quadMat.HasProperty("_Shimmer"))      _quadMat.SetFloat("_Shimmer", pasteShimmer);
        mr.sharedMaterial = _quadMat;
    }

    /// <summary>_Peel 1→0 でシールを壁へ貼り付け + シマー掃引 (unscaled)。完了後 実スプライトへ差し替え。</summary>
    private IEnumerator PasteOn() {
        BuildPasteQuad();
        if (_quadMat == null || !_quadMat.HasProperty("_Peel")) {
            if (revealGate != null) revealGate.enabled = true;
            yield break;
        }
        float dur = Mathf.Max(0.01f, pasteDuration);
        float t = 0f;
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / dur);
            float e = 1f - Mathf.Pow(1f - x, 3f);                       // ease-out cubic
            _quadMat.SetFloat("_Peel", 1f - e);                         // 1→0 = 壁へ貼り付く
            if (_quadMat.HasProperty("_Shimmer"))
                _quadMat.SetFloat("_Shimmer", pasteShimmer * (1f - x)); // 終盤にシマー収束
            yield return null;
        }
        _quadMat.SetFloat("_Peel", 0f);
        if (_quadMat.HasProperty("_Shimmer")) _quadMat.SetFloat("_Shimmer", 0f);

        // 実ゲート(スプライト)へ差し替え、板ポリは破棄
        if (revealGate != null) revealGate.enabled = true;
        if (_pasteQuad != null) Destroy(_pasteQuad);
        if (_quadMat != null) { Destroy(_quadMat); _quadMat = null; }   // 生成マテリアルも解放
    }

    private void OnDestroy() {
        // 演出途中でシーン遷移等によりゲートが破棄されても生成物を残さない
        if (_quadMat != null) { Destroy(_quadMat); _quadMat = null; }
        if (_pasteQuad != null) { Destroy(_pasteQuad); _pasteQuad = null; }
    }

    // ======================================================================
    //  カメラ奪取 / ゲート寄せ / 返却  (SecondPhaseSequence と同じ流儀)
    // ======================================================================
    private void ResolveCameraRefs() {
        _cam = TPSCameraController.Instance;
        _camTf = _cam != null ? _cam.transform : (Camera.main != null ? Camera.main.transform : null);
        _camComponent = _camTf != null ? _camTf.GetComponent<Camera>() : null;
        _brain = FindBrain(_camTf);
    }

    private Behaviour FindBrain(Transform camTf) {
        if (camTf == null) return null;
        foreach (var c in camTf.GetComponents<Component>())
            if (c != null && c.GetType().Name == "CinemachineBrain") return c as Behaviour;
        return null;
    }

    private void TakeCamera() {
        if (_brain != null) _brain.enabled = false;   // Cinemachine の上書きを止める
        if (_cam != null) _cam.enabled = false;        // 後追いカメラを停止して直接駆動
        ForceGameFov();
        if (_playerScope != null) _playerScope.SetUIMode(true);
    }

    private IEnumerator ReleaseCamera() {
        if (_camTf != null && _cam != null && player != null) {
            _cam.GetHomeFollowPose(player.eulerAngles.y, out Vector3 hp, out Quaternion hr);
            yield return MoveCameraUnscaled(hp, hr, cameraReturnTime);
        }
        if (_cam != null) { _cam.ResyncFromCurrentView(); _cam.enabled = true; }
        if (_brain != null) _brain.enabled = true;
        if (_playerScope != null) _playerScope.SetUIMode(false);
    }

    private void ForceGameFov() {
        if (_camComponent != null && _cam != null) _camComponent.fieldOfView = _cam.BaseFov;
    }

    /// <summary>ゲートを正面から見るカメラ姿勢。</summary>
    private void GetGatePose(out Vector3 pos, out Quaternion rot) {
        Vector3 gate = revealGate.transform.position;
        Vector3 dir = showcaseFromDir.sqrMagnitude > 0.0001f ? showcaseFromDir.normalized : Vector3.back;
        pos = gate + dir * showcaseDistance;
        Vector3 lookAt = gate + Vector3.up * showcaseLookHeight;
        rot = Quaternion.LookRotation(lookAt - pos, Vector3.up);
    }

    private IEnumerator MoveCameraUnscaled(Vector3 pos, Quaternion rot, float moveTime) {
        if (_camTf == null) yield break;
        Vector3 p0 = _camTf.position;
        Quaternion r0 = _camTf.rotation;
        float dur = Mathf.Max(0.01f, moveTime);
        float t = 0f;
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(t / dur);
            float e = x * x * (3f - 2f * x);   // smoothstep
            _camTf.SetPositionAndRotation(
                Vector3.LerpUnclamped(p0, pos, e),
                Quaternion.SlerpUnclamped(r0, rot, e));
            ForceGameFov();
            yield return null;
        }
        _camTf.SetPositionAndRotation(pos, rot);
        ForceGameFov();
    }

    // ======================================================================
    //  インタラクト → 退場
    // ======================================================================
    private void Update() {
        if (!_armed || _exiting) return;
        if (fPrompt == null || !fPrompt.activeSelf) return;   // 出現演出が済むまで不可
        if (player == null || revealGate == null || warpTarget == null) return;

        if (Vector3.Distance(player.position, revealGate.transform.position) > interactRange) return;

        bool press = (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
                  || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);
        if (press) StartCoroutine(ExitRoutine());
    }

    /// <summary>暗転 → ワープ → 復帰。</summary>
    private IEnumerator ExitRoutine() {
        _exiting = true;
        if (fPrompt != null) fPrompt.SetActive(false);
        if (_playerScope != null) _playerScope.SetUIMode(true);
        EnsureFadeOverlay();

        yield return Fade(0f, 1f, fadeOutDuration);          // 暗転
        WarpPlayer();
        if (holdBlack > 0f) yield return new WaitForSecondsRealtime(holdBlack);
        yield return Fade(1f, 0f, fadeInDuration);           // 復帰

        if (_playerScope != null) _playerScope.SetUIMode(false);
        _exiting = false;
    }

    /// <summary>CharacterController を一時無効化してワープ先へ瞬間移動。</summary>
    private void WarpPlayer() {
        if (player == null || warpTarget == null) return;
        var cc = player.GetComponent<CharacterController>();
        var rb = player.GetComponent<Rigidbody>();
        bool ccWas = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;               // CC が瞬間移動を打ち消すのを防ぐ
        player.position = warpTarget.position;
        player.rotation = Quaternion.Euler(0f, warpTarget.eulerAngles.y, 0f);
        if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (cc != null) cc.enabled = ccWas;
    }

    // ======================================================================
    //  フェード / ユーティリティ
    // ======================================================================
    private IEnumerator Fade(float from, float to, float dur) {
        if (_fadeGroup == null) yield break;
        _fadeGroup.alpha = from;
        if (dur <= 0f) { _fadeGroup.alpha = to; yield break; }
        float t = 0f;
        while (t < dur) {
            t += Time.unscaledDeltaTime;
            _fadeGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            yield return null;
        }
        _fadeGroup.alpha = to;
    }

    private IEnumerator WaitUnscaled(float seconds) {
        float t = 0f;
        while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    /// <summary>フルスクリーン黒フェード用オーバーレイを一度だけ生成。</summary>
    private void EnsureFadeOverlay() {
        if (_fadeGroup != null) return;
        var go = new GameObject("StageClearGate_Fade");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;                      // 最前面
        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;
        var imgGo = new GameObject("Black");
        imgGo.transform.SetParent(go.transform, false);
        var img = imgGo.AddComponent<Image>();
        img.color = fadeColor;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _fadeGroup = cg;
    }

    // ---- エディタ確認用 (Play 中に右クリックメニューから) ----
    [ContextMenu("DEBUG_ForceReveal")]
    private void DebugForceReveal() { if (Application.isPlaying) HandleCleared(); }
    [ContextMenu("DEBUG_ForceExit")]
    private void DebugForceExit() { if (Application.isPlaying && !_exiting) StartCoroutine(ExitRoutine()); }
}
