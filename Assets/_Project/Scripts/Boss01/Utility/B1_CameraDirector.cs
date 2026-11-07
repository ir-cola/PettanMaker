using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Serialization;
using Unity.Cinemachine;

/// <summary>
/// Boss01 のカメラシーン入口。
/// Entrance Map(戦闘前のマップ演出) / Entrance / Phase2 / Defeat をそれぞれ
/// PlayableDirector(Timeline)で再生する。TPS⇔CinemachineBrain の受け渡しをここで一元管理する。
/// </summary>
[DisallowMultipleComponent]
public class B1_CameraDirector : MonoBehaviour
{
    [Header("Camera Scene Settings (ScriptableObject)")]
    [Tooltip("登場カメラ設定。Boss01/Camera Settings/Entrance のアセットを割り当てる。" +
             "登場演出(Reveal: 煙/透明/ディゾルブ)もこのアセットに集約されている。")]
    [FormerlySerializedAs("engageScene")]
    [SerializeField] private B1_CameraSceneSettings entranceScene;
    [Tooltip("Phase2(体力半分)カメラ設定。")]
    [SerializeField] private B1_CameraSceneSettings phase2Scene;
    [Tooltip("Phase2 突入時、ボスがテールアタックで Target に着地してから Phase2 カメラ演出へ移るまでの溜め(秒, unscaled)。0 で着地と同時にカメラ演出へ入る。")]
    [SerializeField] private float phase2ResetLeadTime = 0.3f;
    public float Phase2ResetLeadTime => Mathf.Max(0f, phase2ResetLeadTime);
    [Tooltip("撃破カメラ設定。")]
    [SerializeField] private B1_CameraSceneSettings defeatScene;

    [HideInInspector]
    [Tooltip("演出で動かす TPS カメラ。未設定なら Instance -> シーン内検索 -> MainCamera の順に探す")]
    [SerializeField] private TPSCameraController cameraController;
    [HideInInspector]
    [Tooltip("TPSCameraController が無い場合に直接動かすカメラ")]
    [SerializeField] private Camera targetCamera;
    [HideInInspector]
    [Tooltip("演出中に入力を凍結するプレイヤー。未設定なら実行時に検索する")]
    [SerializeField] private PlayerLifetimeScope player;

    [Header("Cinemachine")]
    [Tooltip("実際のカメラに付いている CinemachineBrain(シーンに1つだけ)。演出中だけ有効化し、" +
             "終わると無効化して TPS へ戻す。全シーン共通で必ずこの1つを指すこと。")]
    [SerializeField] private CinemachineBrain cinemachineBrain;

    [Header("Timeline (Entrance Map)")]
    [Tooltip("戦闘開始前に再生するマップ演出(中心をOrbitするなど)。B1_BossRoomTrigger から " +
             "PlayEntranceMap() で呼ぶ想定。未設定なら PlayEntranceMap は即 onComplete を呼ぶ。")]
    [SerializeField] private PlayableDirector entranceMapDirector;
    [Tooltip("Entrance Map 用の CinemachineCamera。再生中だけこのコンポーネントを enabled にし、" +
             "他シーンの vcam は disabled にする(Priority 競合を避けるため。GameObject 自体は常時 Active)")]
    [SerializeField] private CinemachineCamera entranceMapVcam;

    [Tooltip("マップ演出中のクレーン高さ(m)。X=演出の正規化時間(0〜1)、Y=Entrance Map vcam のローカルYへ加算。\n" +
             "低い位置から始めて上昇し、マップ全体を見渡す。キーを全部消すと高さは触らない。")]
    [SerializeField] private AnimationCurve entranceMapCraneCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(1f, 6f, 0f, 0f));

    [Tooltip("マップ演出中の FOV(度)。X=正規化時間。広角(70)で全体を見せ、終盤に絞って(55)視線を定めてから " +
             "Boss01_Entrance へカットでつなぐ。キーを全部消すと FOV は触らない。")]
    [SerializeField] private AnimationCurve entranceMapFovCurve = new AnimationCurve(
        new Keyframe(0f, 70f, 0f, 0f),
        new Keyframe(1f, 55f, 0f, 0f));

    [Tooltip("マップ演出中のダッチアングル=ロール角(度)。X=正規化時間。中盤に 3〜5° 傾けて緊張感を出し、" +
             "終端は 0 に戻して Boss01_Entrance へのカットを水平で受ける。キーを全部消すとロールは触らない。")]
    [SerializeField] private AnimationCurve entranceMapRollCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.45f, 4f, 0f, 0f),
        new Keyframe(1f, 0f, 0f, 0f));

    [Header("Entrance Map (Player Scary Walk)")]
    [Tooltip("マップ演出中にプレイヤーが歩き始める位置。未設定なら現在位置から EndPoint へ歩く。")]
    [SerializeField] private Transform entranceMapWalkStartPoint;
    [Tooltip("プレイヤーの歩行終点。設定するとマップ演出中に ScaryWalk で前方へ歩き、到着後は" +
             "上半身 ScaryWalk のまま下半身だけ Idle で立ち止まる。未設定なら歩かない(従来どおり)。")]
    [SerializeField] private Transform entranceMapWalkEndPoint;
    [Tooltip("歩行速度(m/秒)。ScaryWalk の歩幅に合わせて足が滑らない速度に調整する。")]
    [SerializeField, Min(0f)] private float entranceMapWalkSpeed = 1.2f;
    [Tooltip("歩行中に EndPoint 方向へ向き直る回転速度(度/秒)。")]
    [SerializeField, Min(0f)] private float entranceMapWalkTurnSpeed = 360f;

    [Header("Timeline (Entrance)")]
    [Tooltip("Entrance を再生する PlayableDirector(Timeline)。")]
    [SerializeField] private PlayableDirector entranceDirector;
    [Tooltip("Entrance Timeline 用のビート/フォグ/UI ブリッジ")]
    [SerializeField] private B1_CameraTimelineBridge entranceBridge;
    [Tooltip("Entrance 用の CinemachineCamera。再生中だけ enabled にする(GameObject 自体は常時 Active)")]
    [SerializeField] private CinemachineCamera entranceVcam;

    [Header("Timeline (Phase2)")]
    [Tooltip("Phase2 を再生する PlayableDirector(Timeline)。")]
    [SerializeField] private PlayableDirector phase2Director;
    [Tooltip("Phase2 Timeline 用のフォグ/UI ブリッジ(Phase2はビート無しなのでフォグ駆動のみ使う)")]
    [SerializeField] private B1_CameraTimelineBridge phase2Bridge;
    [Tooltip("Phase2 用の CinemachineCamera。再生中だけ enabled にする(GameObject 自体は常時 Active)")]
    [SerializeField] private CinemachineCamera phase2Vcam;

    [Header("Timeline (Defeat)")]
    [Tooltip("Defeat を再生する PlayableDirector(Timeline)。")]
    [SerializeField] private PlayableDirector defeatDirector;
    [Tooltip("Defeat Timeline 用のフォグ/UI ブリッジ(Defeatはビート無しなのでフォグ駆動のみ使う)")]
    [SerializeField] private B1_CameraTimelineBridge defeatBridge;
    [Tooltip("Defeat 用の CinemachineCamera。再生中だけ enabled にする(GameObject 自体は常時 Active)")]
    [SerializeField] private CinemachineCamera defeatVcam;

    [Header("Fixed Camera (Tail Rain)")]
    [Tooltip("テールレイン中に切り替える見下ろし固定 CinemachineCamera。マップを上空から俯瞰する構図にしておく。\n" +
             "未設定ならテールレインはカメラを切り替えずに進行する(演出だけ省略)。Timeline は使わず、この vcam へ滑らかに補間して寄せる。")]
    [SerializeField] private CinemachineCamera tailRainVcam;
    [Tooltip("TPS から見下ろし vcam へ寄せる補間時間(秒)。0 で従来どおり瞬時切替。")]
    [SerializeField] private float tailRainBlendDuration = 0.8f;
    [Tooltip("補間のイージング。既定は EaseInOut(スプライン状)。X=正規化時間, Y=進捗。")]
    [SerializeField] private AnimationCurve tailRainBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Fixed Camera (Down)")]
    [Tooltip("ダウン中(体力半分 → Phase2 移行前)に切り替える CinemachineCamera。Follow/LookAt をボスにして、" +
             "ボスが近くで見える構図にしておく。未設定なら下の疑似シネマ(ボス追従の寄り + ドリー/オービット)で演出する。")]
    [SerializeField] private CinemachineCamera downVcam;
    [Tooltip("TPS からダウン用 vcam へ寄せる補間時間(秒)。0 で瞬時切替。")]
    [SerializeField] private float downBlendDuration = 0.7f;
    [Tooltip("補間のイージング。既定は EaseInOut(スプライン状)。X=正規化時間, Y=進捗。")]
    [SerializeField] private AnimationCurve downBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("UI (Camera Scene)")]
    [Tooltip("カメラ演出(Entrance Map / Entrance / Phase2 / Defeat の Timeline 再生)中に非表示にする Canvas。" +
             "HUD などを割り当てる。演出が終わると再表示する。TailRain / Down のゲームプレイカメラでは隠さない。")]
    [SerializeField] private Canvas[] cinematicHiddenCanvases;
    [Tooltip("UI の非表示/再表示フェード時間(秒, unscaled)。0 で即時切替。")]
    [SerializeField, Min(0f)] private float cinematicUIFadeDuration = 0.15f;

    [Header("Down Camera 疑似シネマ (downVcam 未設定時)")]
    [Tooltip("downVcam を割り当てない場合、Timeline 無しでボスへ寄る疑似シネマカメラを使う。\n浮遊するボスを毎フレーム注視するため、静的な vcam より演出向き。以下はその構図パラメータ。")]
    [SerializeField] private float downShotDistance = 7f;
    [Tooltip("注視点の高さ(m)。ボスの足元(root)からのオフセット。浮遊分はボス位置に追従するので含めない。")]
    [SerializeField] private float downShotFocusHeight = 1.6f;
    [Tooltip("カメラの高さ(m)。注視点からのオフセット。負値でローアングル(見上げ)になる。")]
    [SerializeField] private float downShotHeight = 0.6f;
    [Tooltip("寄る方向のヨーオフセット(度)。0 で現在カメラのいる側からまっすぐ寄る。±で斜めに回り込む。")]
    [SerializeField] private float downShotYawOffset = 25f;
    [Tooltip("寄り中の FOV(度)。狭めるほど望遠の圧縮感が出る。")]
    [SerializeField, Range(10f, 90f)] private float downShotFov = 42f;
    [Tooltip("寄り切った後にゆっくり近づき続ける速度(m/秒)。0 で静止。")]
    [SerializeField, Min(0f)] private float downDollySpeed = 0.5f;
    [Tooltip("寄り切った後にボスの周りをゆっくり回る速度(度/秒)。0 で静止。")]
    [SerializeField] private float downOrbitSpeed = 8f;

    private Coroutine playing;
    private Coroutine fixedCamBlend;   // TailRain / Down 共用の「TPS→vcam 補間」コルーチン
    private Coroutine uiFade;          // cinematicHiddenCanvases のフェードコルーチン
    private B1_CinematicLetterbox entranceMapLetterbox;
    private bool cinematicUIHidden;
    private bool downCamActive;
    private bool playerScaryWalkActive;
    private TPSCameraController activeCameraController;
    private B1_CameraSceneSettings runtimeEntranceDefault;
    private B1_CameraSceneSettings runtimePhase2Default;
    private B1_CameraSceneSettings runtimeDefeatDefault;
    private B1_Boss bossRef;

    public bool IsPlaying => playing != null;
    public float LastRequestedDuration { get; private set; }

    /// <summary>Entrance Timeline が割り当てられているか。HPゲージを演出終了まで隠すかの判定に使う。</summary>
    public bool HasEntranceTimeline => entranceDirector != null;

    /// <summary>Entrance のカメラ設定（未割り当てならランタイム既定）。B1_Boss が登場演出設定(Reveal)を読む。</summary>
    public B1_CameraSceneSettings EntranceScene => ResolveScene(B1_CameraSceneKind.Entrance);
    /// <summary>Phase2 カメラ設定。演出後の待機時間などを戦闘状態から読む。</summary>
    public B1_Phase2CameraSettings Phase2Scene =>
        ResolveScene(B1_CameraSceneKind.Phase2) as B1_Phase2CameraSettings;

    private void Awake()
    {
        ConfigureTimelineDirector(entranceMapDirector);
        ConfigureTimelineDirector(entranceDirector);
        ConfigureTimelineDirector(phase2Director);
        ConfigureTimelineDirector(defeatDirector);
        StopAllTimelines();
        ActivateVcam(null);
        SetBrainEnabled(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ConfigureTimelineDirector(entranceMapDirector);
        ConfigureTimelineDirector(entranceDirector);
        ConfigureTimelineDirector(phase2Director);
        ConfigureTimelineDirector(defeatDirector);
    }
#endif

    /// <summary>
    /// カメラ演出中にプレイヤー入力を凍結/解除する。
    /// 凍結しないと演出中も攻撃入力が通り、ボスHPが変化して演出が途中で
    /// 別状態(Death/Phase2)に割り込まれる。撃破カメラでは呼ばない
    /// （とどめ入力が必要なため）。
    /// </summary>
    public void SetPlayerInputFrozen(bool frozen)
    {
        if (player == null) player = FindObjectOfType<PlayerLifetimeScope>();
        if (player != null) player.SetUIMode(frozen);
    }

    /// <summary>
    /// プレイヤー移動を固定カメラ基準へ切り替える/戻す。
    /// 見下ろし固定カメラ演出 (TailRain) 中は TPS を止めるため、TPS の Yaw ではなく
    /// 今映っている固定カメラ基準でスティック入力を解釈させる。
    /// </summary>
    public void SetPlayerFixedCameraMove(bool on)
    {
        if (player == null) player = FindObjectOfType<PlayerLifetimeScope>();
        if (player != null) player.SetFixedCameraMove(on);
    }

    private void SetPlayerScaryWalk(bool active)
    {
        if (player == null) player = FindFirstObjectByType<PlayerLifetimeScope>();
        if (player?.Animator == null)
        {
            playerScaryWalkActive = false;
            return;
        }

        if (active)
        {
            // 既に ScaryWalk 中なら再トリガーしない (AnyState 遷移でモーションが頭から入り直すのを防ぐ。
            // マップ演出 → Entrance と連続で呼ばれてもポーズが継がる)。
            if (playerScaryWalkActive) return;
            player.Animator.ResetTrigger(PlayerAnimator.ToMoveParam);
            player.Animator.SetTrigger(PlayerAnimator.ScaryWalkParam);
            playerScaryWalkActive = true;
        }
        else
        {
            // 歩行演出の後片付け (下半身 Idle 上書きの解除)。ScaryWalk 解除と常にセットで行う。
            entranceMapWalking = false;
            player.SetScaryWalkLowerIdle(false);
            if (!playerScaryWalkActive) return;
            player.Animator.ResetTrigger(PlayerAnimator.ScaryWalkParam);
            player.Animator.SetTrigger(PlayerAnimator.ToMoveParam);
            playerScaryWalkActive = false;
        }
    }

    // ---- Entrance Map 中のプレイヤー歩行 (StartPoint → EndPoint) ----
    // マップ演出の間、プレイヤーを ScaryWalk で EndPoint まで歩かせる。到着後は
    // ScaryWalkLowerIdle=true で下半身レイヤーだけ Idle に上書きし、上半身の ScaryWalk は維持する。
    // 入力は SetPlayerInputFrozen で凍結済み (Mover は UIMode で動かない) ので、
    // ここから CharacterController を直接駆動しても競合しない。

    private bool entranceMapWalking;
    private CharacterController entranceMapWalkCc;
    private const float EntranceMapWalkArriveDist = 0.05f; // 到着判定距離(m, 水平)

    private void BeginEntranceMapPlayerWalk()
    {
        entranceMapWalking = false;
        if (entranceMapWalkEndPoint == null) return;
        if (player == null) player = FindFirstObjectByType<PlayerLifetimeScope>();
        if (player == null) return;

        entranceMapWalkCc = player.GetComponent<CharacterController>();
        if (entranceMapWalkStartPoint != null)
        {
            // CharacterController 有効のままだと transform 直接セットが内部位置に負けるので一旦切る。
            if (entranceMapWalkCc != null) entranceMapWalkCc.enabled = false;
            player.transform.position = entranceMapWalkStartPoint.position;
            if (entranceMapWalkCc != null) entranceMapWalkCc.enabled = true;
        }

        // 歩き出しは EndPoint 方向へ即時に向けておく (演出頭から背中向きで歩かないように)。
        Vector3 dir = entranceMapWalkEndPoint.position - player.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            player.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        player.SetScaryWalkLowerIdle(false);
        SetPlayerScaryWalk(true);
        entranceMapWalking = true;
    }

    private void UpdateEntranceMapPlayerWalk()
    {
        if (!entranceMapWalking || player == null || entranceMapWalkEndPoint == null) return;

        Transform tf = player.transform;
        Vector3 to = entranceMapWalkEndPoint.position - tf.position;
        to.y = 0f;
        if (to.magnitude <= EntranceMapWalkArriveDist)
        {
            FinishEntranceMapPlayerWalk();
            return;
        }

        // Timeline(GameTime) と Animator に合わせて scaled time で歩く。巨大 dt はクランプ。
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        Vector3 dir = to.normalized;

        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float newYaw = Mathf.MoveTowardsAngle(tf.eulerAngles.y, targetYaw, entranceMapWalkTurnSpeed * dt);
        tf.rotation = Quaternion.Euler(0f, newYaw, 0f);

        float step = Mathf.Min(entranceMapWalkSpeed * dt, to.magnitude);
        Vector3 delta = dir * step + Vector3.down * (2f * dt); // 軽い下向き成分で接地を維持
        if (entranceMapWalkCc != null && entranceMapWalkCc.enabled) entranceMapWalkCc.Move(delta);
        else tf.position += dir * step;
    }

    // EndPoint 到着 (またはマップ演出終了) で歩行を止め、下半身だけ Idle に切り替える。
    private void FinishEntranceMapPlayerWalk()
    {
        if (!entranceMapWalking) return;
        entranceMapWalking = false;
        if (player != null) player.SetScaryWalkLowerIdle(true);
    }

#if UNITY_EDITOR
    // 歩行の StartPoint → EndPoint をシーンビューで確認できるように描く。
    private void OnDrawGizmosSelected()
    {
        if (entranceMapWalkEndPoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(entranceMapWalkEndPoint.position, 0.3f);
        if (entranceMapWalkStartPoint != null)
        {
            Gizmos.DrawWireSphere(entranceMapWalkStartPoint.position, 0.3f);
            Gizmos.DrawLine(entranceMapWalkStartPoint.position, entranceMapWalkEndPoint.position);
        }
    }
#endif

    // ---- カメラ演出中の UI 非表示 ----
    // Inspector で割り当てた Canvas を Timeline 演出中だけ CanvasGroup のアルファで隠す。
    // TailRain / Down はプレイヤーが操作し続けるゲームプレイ演出なので対象外(呼ばない)。

    /// <summary>カメラ演出用に UI を隠す/戻す。多重呼び出しは無視する。</summary>
    private void SetCinematicUIHidden(bool hidden)
    {
        if (cinematicUIHidden == hidden) return;
        cinematicUIHidden = hidden;
        if (cinematicHiddenCanvases == null || cinematicHiddenCanvases.Length == 0) return;

        if (uiFade != null) { StopCoroutine(uiFade); uiFade = null; }

        float target = hidden ? 0f : 1f;
        // OnDisable 経由などコルーチンを回せない状況、またはフェード無し設定なら即時反映。
        if (!isActiveAndEnabled || cinematicUIFadeDuration <= 0f)
        {
            ApplyCinematicUIAlpha(target);
            return;
        }
        uiFade = StartCoroutine(FadeCinematicUIRoutine(target));
    }

    private IEnumerator FadeCinematicUIRoutine(float target)
    {
        int count = cinematicHiddenCanvases.Length;
        var groups = new CanvasGroup[count];
        var starts = new float[count];
        for (int i = 0; i < count; i++)
        {
            groups[i] = ResolveUIGroup(cinematicHiddenCanvases[i]);
            starts[i] = groups[i] != null ? groups[i].alpha : 0f;
        }

        // 演出中は Time.timeScale が変わることがあるので unscaled で進める。
        float t = 0f;
        while (t < 1f)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 0.1f) / cinematicUIFadeDuration;
            float k = Mathf.Clamp01(t);
            for (int i = 0; i < count; i++)
                if (groups[i] != null) groups[i].alpha = Mathf.Lerp(starts[i], target, k);
            yield return null;
        }
        ApplyCinematicUIAlpha(target);
        uiFade = null;
    }

    private void ApplyCinematicUIAlpha(float alpha)
    {
        if (cinematicHiddenCanvases == null) return;
        foreach (var canvas in cinematicHiddenCanvases)
        {
            var group = ResolveUIGroup(canvas);
            if (group == null) continue;
            group.alpha = alpha;
            // 隠している間はクリックも透過させる(見えない UI が入力を食わないように)。
            bool visible = alpha > 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }

    // Canvas 直下の CanvasGroup を取得する(無ければ追加)。Canvas 全体をアルファ1発で制御するため。
    private static CanvasGroup ResolveUIGroup(Canvas canvas)
    {
        if (canvas == null) return null;
        var group = canvas.GetComponent<CanvasGroup>();
        if (group == null) group = canvas.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    /// <summary>
    /// 戦闘開始前のマップ演出(中心をOrbitするなど)を再生する。B1_BossRoomTrigger から呼ぶ想定。
    /// entranceMapDirector が未設定なら即 onComplete を呼ぶ。完了後は TPS へ戻すので、
    /// 続けて PlayEntrance 等を呼べば(同フレーム内なら)チラつき無く受け渡せる。
    /// </summary>
    public void PlayEntranceMap(System.Action onComplete)
    {
        if (entranceMapDirector == null) { onComplete?.Invoke(); return; }

        if (playing != null) Stop();
        StopLegacyCinemaTracks();
        StopTimeline(entranceDirector, entranceBridge);

        // マップ演出は B1_CinematicState (戦闘開始後) より前に走るため、ここで入力を凍結する。
        // 解除は WaitEntranceMapRoutine の末尾。直後の EnterCombat → CinematicState が同フレームで
        // 再凍結するので入力が通る隙間はない。
        SetPlayerInputFrozen(true);
        SetCinematicUIHidden(true);

        Transform camTf = ResolveCameraTransform(out var tps);
        if (camTf != null && tps != null) tps.enabled = false;
        ActivateVcam(entranceMapVcam);       // Brain を有効化する前に対象 vcam を確定させる
        SetBrainEnabled(true);

        // プレイヤーを StartPoint へ置き、ScaryWalk で EndPoint へ歩かせ始める (EndPoint 未設定なら何もしない)。
        BeginEntranceMapPlayerWalk();

        // 黒帯は Boss01 Entrance 本編ではなく、マップを周回する Entrance Orbit 中だけ表示する。
        DisposeEntranceMapLetterbox();
        entranceMapLetterbox = B1_CinematicLetterbox.Create(EntranceScene != null ? EntranceScene.Letterbox : null);

        entranceMapDirector.time = 0;
        entranceMapDirector.Play();
        playing = StartCoroutine(WaitEntranceMapRoutine(onComplete));
    }

    private IEnumerator WaitEntranceMapRoutine(System.Action onComplete)
    {
        // クレーン(高さ)・FOV・ロール(ダッチアングル)は Timeline (回転のみ) に足す形でコード側から駆動する。
        // Timeline アセットを触らず Inspector のカーブだけで軌道を立体化できる。
        Transform vcamTf   = entranceMapVcam != null ? entranceMapVcam.transform : null;
        Vector3   basePos  = vcamTf != null ? vcamTf.localPosition : Vector3.zero;
        float     baseFov  = entranceMapVcam != null ? entranceMapVcam.Lens.FieldOfView : 0f;
        float     baseDutch = entranceMapVcam != null ? entranceMapVcam.Lens.Dutch : 0f;
        float     dur      = (float)entranceMapDirector.duration;
        float     closeLead = EntranceScene != null && EntranceScene.Letterbox != null
            ? Mathf.Max(0f, EntranceScene.Letterbox.exitDuration)
            : 0f;
        bool      letterboxClosing = false;

        // DirectorWrapMode.Hold だと Timeline 終了後も state は Playing のまま変わらないため、
        // time が duration に達したか(または外部から Stop/Pause されたか)で完了を判定する。
        while (entranceMapDirector != null && entranceMapDirector.state == PlayState.Playing &&
               entranceMapDirector.time < entranceMapDirector.duration)
        {
            float timelineTime = (float)entranceMapDirector.time;
            ApplyEntranceMapCrane(vcamTf, basePos, dur > 0f ? timelineTime / dur : 1f);
            UpdateEntranceMapPlayerWalk();
            if (!letterboxClosing && dur - timelineTime <= closeLead)
            {
                entranceMapLetterbox?.HideAndDispose();
                letterboxClosing = true;
            }
            entranceMapLetterbox?.Tick(Time.unscaledDeltaTime);
            yield return null;
        }
        if (!letterboxClosing) entranceMapLetterbox?.HideAndDispose();
        while (entranceMapLetterbox != null && !entranceMapLetterbox.IsDisposed)
        {
            entranceMapLetterbox.Tick(Time.unscaledDeltaTime);
            yield return null;
        }
        entranceMapLetterbox = null;

        ApplyEntranceMapCrane(vcamTf, basePos, 1f); // 最終フレームの構図で確定してから切り替える
        // 演出が終わったら EndPoint 未到達でもその場で歩行を止め、下半身を Idle にする。
        FinishEntranceMapPlayerWalk();
        // 直後に Entrance Timeline が続かない構成なら、ScaryWalk を解いて通常ロコモーションへ戻す
        // (続く場合は PlayEntrance 側が ScaryWalk を維持し、演出終了時に解除する)。
        if (entranceDirector == null) SetPlayerScaryWalk(false);

        // Hold で生き続けている Timeline をここで明示的に Stop する(stopped イベントを発火させて
        // Bridge 側のフォグ/UI 後片付けを確実に走らせるため)。
        if (entranceMapDirector != null && entranceMapDirector.state == PlayState.Playing) entranceMapDirector.Stop();
        ActivateVcam(null);
        SetBrainEnabled(false);

        // vcam を次回再生に備えて元の構図へ戻す (Brain はもう切れているので画面には影響しない)
        if (vcamTf != null) vcamTf.localPosition = basePos;
        if (entranceMapVcam != null)
        {
            var lens = entranceMapVcam.Lens;
            lens.FieldOfView = baseFov;
            lens.Dutch = baseDutch;
            entranceMapVcam.Lens = lens;
        }

        RestoreTpsCamera();
        playing = null;
        SetPlayerInputFrozen(false); // 凍結スコープはマップ演出内で閉じる (直後の CinematicState が再凍結する)
        // 直後に PlayEntrance が続く場合は再度 hidden になる(フェード途中から引き継ぐのでチラつかない)。
        SetCinematicUIHidden(false);
        onComplete?.Invoke();
    }

    // マップ演出の正規化時間 t (0〜1) に応じて、vcam のローカル高さ・FOV・ロールをカーブで駆動する。
    private void ApplyEntranceMapCrane(Transform vcamTf, Vector3 basePos, float t)
    {
        if (vcamTf == null) return;
        t = Mathf.Clamp01(t);

        if (entranceMapCraneCurve != null && entranceMapCraneCurve.length > 0)
            vcamTf.localPosition = basePos + Vector3.up * entranceMapCraneCurve.Evaluate(t);

        bool touchFov  = entranceMapFovCurve != null && entranceMapFovCurve.length > 0;
        bool touchRoll = entranceMapRollCurve != null && entranceMapRollCurve.length > 0;
        if ((touchFov || touchRoll) && entranceMapVcam != null)
        {
            var lens = entranceMapVcam.Lens;
            if (touchFov)  lens.FieldOfView = Mathf.Clamp(entranceMapFovCurve.Evaluate(t), 1f, 179f);
            if (touchRoll) lens.Dutch = entranceMapRollCurve.Evaluate(t);
            entranceMapVcam.Lens = lens;
        }
    }

    public void PlayEntrance()
    {
        if (entranceDirector == null) return;

        LastRequestedDuration = (float)entranceDirector.duration;
        if (!isActiveAndEnabled) return;

        if (playing != null) Stop();
        StopLegacyCinemaTracks();
        StopTimeline(entranceMapDirector, null);

        SetCinematicUIHidden(true);

        Transform camTf = ResolveCameraTransform(out var tps);
        if (camTf != null && tps != null) tps.enabled = false;
        ActivateVcam(entranceVcam);          // Brain を有効化する前に対象 vcam を確定させる
        SetBrainEnabled(true);
        SetPlayerScaryWalk(true);

        entranceDirector.time = 0;
        entranceDirector.Play();
        playing = StartCoroutine(WaitEntranceTimelineRoutine());
    }

    // Reveal 完了待ちは B1_CinematicState が担当する。カメラは Timeline 終了で TPS へ戻す。
    private IEnumerator WaitEntranceTimelineRoutine()
    {
        if (bossRef == null) bossRef = GetComponentInParent<B1_Boss>();

        // DirectorWrapMode.Hold だと Timeline 終了後も state は Playing のまま変わらないため、
        // time が duration に達したか(または外部から Stop/Pause されたか)で完了を判定する。
        while (entranceDirector != null && entranceDirector.state == PlayState.Playing &&
               entranceDirector.time < entranceDirector.duration)
            yield return null;

        // Hold で生き続けている Timeline をここで明示的に Stop する(stopped イベントを発火させて
        // Bridge 側のフォグ/UI 後片付けを確実に走らせるため)。
        if (entranceDirector != null && entranceDirector.state == PlayState.Playing) entranceDirector.Stop();
        ActivateVcam(null);
        SetBrainEnabled(false);
        RestoreTpsCamera();
        SetCinematicUIHidden(false);
        SetPlayerScaryWalk(false);
        entranceBridge?.PlayHealthGaugeEntrance();
        playing = null;
    }

    public void PlayEngage() => PlayEntrance();

    public void PlayHalfHealth()
    {
        if (phase2Director == null) return;
        PlaySimpleTimeline(phase2Director, phase2Vcam, faceBossOnEnd: true);
    }

    /// <summary>
    /// Phase2 突入演出。先に resetAction(ボスのテールアタック移動)を実行し、resetComplete が
    /// true(ジャンプ着地)になるのを待ってから、phase2ResetLeadTime の溜めを挟んで Phase2 カメラ
    /// Timeline を再生する。一連を1本の playing コルーチンで動かすので、IsPlaying /
    /// LastRequestedDuration はリセット尺込みになり、B1_CinematicState は最後まで待ってから抜ける。
    /// </summary>
    public void PlayHalfHealthWithReset(System.Action resetAction, System.Func<bool> resetComplete, float resetLeadEstimate)
    {
        if (phase2Director == null) { resetAction?.Invoke(); return; }

        // 尺見積り: リセット移動(ジャンプ)の想定尺 ＋ 着地後の溜め ＋ Timeline 全体。
        // CinematicState はこの尺と「playing 完了」の両方が揃うまで待つので、実尺に近い見積りにする。
        LastRequestedDuration = Mathf.Max(0f, resetLeadEstimate) + Mathf.Max(0f, phase2ResetLeadTime) + (float)phase2Director.duration;
        if (!isActiveAndEnabled) { resetAction?.Invoke(); return; }

        if (playing != null) Stop();
        playing = StartCoroutine(Phase2ResetThenCameraRoutine(resetAction, resetComplete));
    }

    // リセット移動(ジャンプ)が何らかの理由で完了しない場合の保険上限(秒)。
    private const float Phase2ResetMaxWait = 4f;

    private IEnumerator Phase2ResetThenCameraRoutine(System.Action resetAction, System.Func<bool> resetComplete)
    {
        // リセット移動(ジャンプ)の間も演出の一部なので、UI はこの時点から隠す。
        SetCinematicUIHidden(true);

        // ① テールアタック移動を開始し、着地(resetComplete)まで待つ。カメラは現状のまま。
        //    シーンロード直後の巨大 unscaledDeltaTime で尺を食い尽くさないよう 1フレームをクランプする。
        resetAction?.Invoke();
        float t = 0f;
        while ((resetComplete == null || !resetComplete()) && t < Phase2ResetMaxWait)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            yield return null;
        }

        // ② 着地後の溜め。
        float hold = 0f;
        while (hold < phase2ResetLeadTime)
        {
            hold += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            yield return null;
        }

        // ③ Phase2 カメラ Timeline へ移る。待ち・後片付けは共通ルーチンに委譲。
        BeginSimpleTimeline(phase2Director, phase2Vcam);
        yield return WaitSimpleTimelineRoutine(phase2Director, faceBossOnEnd: true);
    }

    public void PlayDefeat()
    {
        if (defeatDirector == null) return;
        PlaySimpleTimeline(defeatDirector, defeatVcam);
    }

    // ---- テールレイン用 固定カメラ（Timeline 無し・入力凍結なし） ----
    // Entrance/Phase2/Defeat と違い、テールレインは「プレイヤーが操作したまま見下ろし視点で避ける」
    // ゲームプレイ演出。よって SetPlayerInputFrozen は呼ばず、Timeline も回さない。
    // 現在の TPS カメラ姿勢から見下ろし vcam の構図まで補間で寄せ、着いてから Brain へ主導権を渡す。
    // tailRainVcam 未設定なら何もしない。

    /// <summary>
    /// 見下ろし固定カメラへ切り替える（プレイヤー入力は凍結しない）。tailRainVcam 未設定なら false。
    /// 瞬時カットではなく、現在の TPS カメラ姿勢から vcam の構図まで tailRainBlendDuration 秒かけて補間して寄せる。
    /// </summary>
    public bool BeginTailRainCamera()
    {
        if (tailRainVcam == null) return false;
        if (playing != null) Stop();          // 進行中の Timeline があれば畳む
        StopAllTimelines();

        if (fixedCamBlend != null) StopCoroutine(fixedCamBlend);
        fixedCamBlend = StartCoroutine(BlendToVcamRoutine(tailRainVcam, tailRainBlendDuration, tailRainBlendCurve));
        return true;
    }

    // Brain を切ったまま実カメラの Transform/FOV を手で補間し、着いてから Brain へ主導権を渡す。
    // Brain off の間は vcam を UpdateCameraState で毎フレーム tick して「今の構図」を読み、動く vcam でも継ぎ目なく寄せる。
    // TailRain / Down 共用。
    private IEnumerator BlendToVcamRoutine(CinemachineCamera vcam, float duration, AnimationCurve curve)
    {
        Transform camTf = ResolveCameraTransform(out var tps);
        if (camTf == null)
        {
            // カメラ Transform が取れないときは従来どおり即時切替にフォールバック。
            ActivateVcam(vcam);
            SetBrainEnabled(true);
            fixedCamBlend = null;
            yield break;
        }

        if (tps != null) tps.enabled = false; // 補間中は TPS に触らせない（Transform を奪い合わない）
        Camera cam = camTf.GetComponent<Camera>();

        Vector3    startPos = camTf.position;
        Quaternion startRot = camTf.rotation;
        float      startFov = cam != null ? cam.fieldOfView : 0f;

        float dur = Mathf.Max(0f, duration);
        float t   = dur <= 0f ? 1f : 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / dur;
            float e = curve != null
                ? curve.Evaluate(Mathf.Clamp01(t))
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            // Brain off の間、vcam を手動 tick して目標構図（位置/回転/FOV）を得る。
            vcam.UpdateCameraState(Vector3.up, Time.unscaledDeltaTime);
            CameraState st = vcam.State;

            camTf.position = Vector3.Lerp(startPos, st.GetFinalPosition(), e);
            camTf.rotation = Quaternion.Slerp(startRot, st.GetFinalOrientation(), e);
            if (cam != null) cam.fieldOfView = Mathf.Lerp(startFov, st.Lens.FieldOfView, e);

            yield return null;
        }

        // 補間完了。実カメラは既に vcam 構図に一致しているので、ここで Brain へ渡しても継ぎ目は出ない。
        ActivateVcam(vcam);
        SetBrainEnabled(true);
        fixedCamBlend = null;
    }

    /// <summary>テールレインの固定カメラを解除して TPS へ戻す。</summary>
    public void EndTailRainCamera()
    {
        if (tailRainVcam == null) return;
        if (fixedCamBlend != null) { StopCoroutine(fixedCamBlend); fixedCamBlend = null; }
        ActivateVcam(null);
        SetBrainEnabled(false);
        RestoreTpsCamera();
    }

    // ---- ダウン用 寄りカメラ（Timeline 無し・入力凍結なし） ----
    // ダウン中はプレイヤーが自由に攻撃できるゲームプレイ演出なので、TailRain と同じく
    // SetPlayerInputFrozen は呼ばず、TPS からボスの寄り構図へイージングで寄せるだけにする。
    // downVcam が割り当てられていればその構図へ寄せ、未設定なら疑似シネマ
    // (DownCloseUpRoutine: ボス追従の寄り + スロードリー/オービット) で演出する。

    private float downCamRestoreFov = -1f; // 疑似シネマ開始時の FOV。終了時に戻す (-1 = 未使用)

    /// <summary>
    /// ダウンの寄りカメラ演出を開始する（プレイヤー入力は凍結しない）。
    /// downVcam 未設定でも疑似シネマで必ず寄る。
    /// </summary>
    public bool BeginDownCamera()
    {
        if (playing != null) Stop();          // 進行中の Timeline があれば畳む
        StopAllTimelines();

        downCamActive = true;
        SetCinematicUIHidden(true); // 寄りカメラの間は UI も隠す(解除は EndDownCamera)
        if (fixedCamBlend != null) StopCoroutine(fixedCamBlend);
        fixedCamBlend = downVcam != null
            ? StartCoroutine(BlendToVcamRoutine(downVcam, downBlendDuration, downBlendCurve))
            : StartCoroutine(DownCloseUpRoutine());
        return true;
    }

    /// <summary>ダウン用カメラを解除して TPS へ戻す。多重呼び出しは無視する。</summary>
    public void EndDownCamera()
    {
        if (!downCamActive) return;
        downCamActive = false;
        if (fixedCamBlend != null) { StopCoroutine(fixedCamBlend); fixedCamBlend = null; }
        RestoreDownCamFov();
        ActivateVcam(null);
        SetBrainEnabled(false);
        RestoreTpsCamera();
        SetCinematicUIHidden(false);
    }

    // 疑似シネマで狭めた FOV を元へ戻す（未使用なら何もしない）。
    private void RestoreDownCamFov()
    {
        if (downCamRestoreFov < 0f) return;
        Transform camTf = ResolveCameraTransform(out _);
        Camera cam = camTf != null ? camTf.GetComponent<Camera>() : null;
        if (cam != null) cam.fieldOfView = downCamRestoreFov;
        downCamRestoreFov = -1f;
    }

    // ダウンの疑似シネマ。Brain は使わず実カメラの Transform/FOV を直接駆動する。
    // ① 現在の TPS 構図からボスの寄り構図へ downBlendDuration 秒でイージング。
    //    目標構図は毎フレーム再計算するので、浮遊で上昇するボスに追従する。
    // ② 寄り切った後は EndDownCamera まで、スロードリー(接近) + オービット(回り込み)で
    //    画を止めずにシネマらしく見せ続ける。
    private IEnumerator DownCloseUpRoutine()
    {
        Transform camTf = ResolveCameraTransform(out var tps);
        if (bossRef == null) bossRef = GetComponentInParent<B1_Boss>();
        if (bossRef == null) bossRef = FindObjectOfType<B1_Boss>();
        if (camTf == null || bossRef == null) { fixedCamBlend = null; yield break; }

        if (tps != null) tps.enabled = false; // 演出中は TPS に触らせない
        Camera cam = camTf.GetComponent<Camera>();

        Vector3    startPos = camTf.position;
        Quaternion startRot = camTf.rotation;
        float      startFov = cam != null ? cam.fieldOfView : 60f;
        downCamRestoreFov   = cam != null ? startFov : -1f;

        // 寄る方向は「現在カメラのいる側」を基準にヨーオフセットを加える（反対側へ回り込んで画面が飛ぶのを防ぐ）。
        Vector3 sideDir = startPos - bossRef.transform.position;
        sideDir.y = 0f;
        if (sideDir.sqrMagnitude < 0.01f) sideDir = -bossRef.transform.forward;
        sideDir.Normalize();

        float t     = 0f;   // ブレンド正規化時間
        float held  = 0f;   // 寄り切った後の経過秒（ドリー/オービット用）
        float dur   = Mathf.Max(0.0001f, downBlendDuration);

        while (downCamActive)
        {
            // シーンロード直後の巨大 unscaledDeltaTime 対策で1フレームをクランプ（他ルーチンと同じ流儀）。
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            t += udt / dur;
            float k = Mathf.Clamp01(t);
            float e = downBlendCurve != null ? downBlendCurve.Evaluate(k) : Mathf.SmoothStep(0f, 1f, k);
            if (k >= 1f) held += udt;

            // 目標構図を毎フレーム再計算（浮遊・傾きで動くボスを注視し続ける）
            Vector3 focus = bossRef.transform.position + Vector3.up * downShotFocusHeight;
            Vector3 dir   = Quaternion.AngleAxis(downShotYawOffset + downOrbitSpeed * held, Vector3.up) * sideDir;
            float   dist  = Mathf.Max(2f, downShotDistance - downDollySpeed * held);
            Vector3 shotPos = focus + dir * dist + Vector3.up * downShotHeight;
            Quaternion shotRot = Quaternion.LookRotation(focus - shotPos, Vector3.up);

            camTf.SetPositionAndRotation(
                Vector3.Lerp(startPos, shotPos, e),
                Quaternion.Slerp(startRot, shotRot, e));
            if (cam != null) cam.fieldOfView = Mathf.Lerp(startFov, downShotFov, e);

            yield return null;
        }
        fixedCamBlend = null;
    }

    // Phase2/Defeat 用の軽量 Timeline 経路。Entrance と違い登場ディゾルブ待ちが無いので、
    // Timeline 終了 = 演出終了でよい。
    private void PlaySimpleTimeline(PlayableDirector director, CinemachineCamera vcam, bool faceBossOnEnd = false)
    {
        LastRequestedDuration = (float)director.duration;
        if (!isActiveAndEnabled) return;

        if (playing != null) Stop();
        BeginSimpleTimeline(director, vcam);
        playing = StartCoroutine(WaitSimpleTimelineRoutine(director, faceBossOnEnd));
    }

    // 対象 vcam を確定して Timeline を再生開始する（待ち・後片付けは WaitSimpleTimelineRoutine 側）。
    private void BeginSimpleTimeline(PlayableDirector director, CinemachineCamera vcam)
    {
        StopLegacyCinemaTracks();
        StopTimeline(entranceMapDirector, null);
        StopTimeline(entranceDirector, entranceBridge);

        SetCinematicUIHidden(true);

        Transform camTf = ResolveCameraTransform(out var tps);
        if (camTf != null && tps != null) tps.enabled = false;
        ActivateVcam(vcam);                  // Brain を有効化する前に対象 vcam を確定させる
        SetBrainEnabled(true);

        director.time = 0;
        director.Play();
    }

    private IEnumerator WaitSimpleTimelineRoutine(PlayableDirector director, bool faceBossOnEnd = false)
    {
        // DirectorWrapMode.Hold だと Timeline 終了後も state は Playing のまま変わらないため、
        // time が duration に達したか(または外部から Stop/Pause されたか)で完了を判定する。
        while (director != null && director.state == PlayState.Playing && director.time < director.duration)
            yield return null;

        // Hold で生き続けている Timeline をここで明示的に Stop する(stopped イベントを発火させて
        // Bridge 側のフォグ/UI 後片付けを確実に走らせるため)。
        if (director != null && director.state == PlayState.Playing) director.Stop();
        ActivateVcam(null);
        SetBrainEnabled(false);
        // TPS 復帰前にプレイヤーとカメラをボス正対の構図へ着地させる (Phase2 明けなど)。
        // RestoreTpsCamera() の ResyncFromCurrentView() がこの姿勢を再シードするので継ぎ目が出ない。
        if (faceBossOnEnd) AlignPlayerAndCameraToBoss();
        RestoreTpsCamera();
        SetCinematicUIHidden(false);
        playing = null;
    }

    /// <summary>
    /// プレイヤーをボスへ正対させ (ヨーのみ)、実カメラをその背後のホーム追従構図へ置く。
    /// シネマ明けに「プレイヤーもカメラもボスの方を向いている」状態で戦闘へ復帰させるための着地処理。
    /// Brain を無効化した後・RestoreTpsCamera() の前に呼ぶこと。
    /// </summary>
    private void AlignPlayerAndCameraToBoss()
    {
        if (bossRef == null) bossRef = GetComponentInParent<B1_Boss>();
        if (bossRef == null) bossRef = FindObjectOfType<B1_Boss>();
        if (bossRef == null) return;

        Transform playerTf = bossRef.PlayerTransform;
        if (playerTf == null) return;

        Vector3 toBoss = bossRef.transform.position - playerTf.position;
        toBoss.y = 0f;
        if (toBoss.sqrMagnitude < 0.0001f) return;
        toBoss.Normalize();

        playerTf.rotation = Quaternion.LookRotation(toBoss, Vector3.up);

        // カメラはプレイヤー背後・ボス方向ヨーのホーム構図へスナップさせる。
        Transform camTf = ResolveCameraTransform(out var tps);
        if (camTf == null || tps == null) return;
        float yaw = Mathf.Atan2(toBoss.x, toBoss.z) * Mathf.Rad2Deg;
        tps.GetHomeFollowPose(yaw, out Vector3 pos, out Quaternion rot);
        camTf.SetPositionAndRotation(pos, rot);
    }

    [ContextMenu("Play Entrance Camera")]
    private void ContextPlayEntrance() => PlayEntrance();

    [ContextMenu("Play Phase2 Camera")]
    private void ContextPlayPhase2() => PlayHalfHealth();

    [ContextMenu("Play Defeat Camera")]
    private void ContextPlayDefeat() => PlayDefeat();

    [ContextMenu("Stop Camera Preview")]
    private void ContextStopPreview() => Stop();

    public void Stop()
    {
        if (fixedCamBlend != null) { StopCoroutine(fixedCamBlend); fixedCamBlend = null; }
        downCamActive = false;
        RestoreDownCamFov();
        SetPlayerScaryWalk(false);
        DisposeEntranceMapLetterbox();
        if (playing == null && !AnyTimelinePlaying()) return;

        if (playing != null) StopCoroutine(playing);
        StopAllTimelines();
        ActivateVcam(null);
        SetBrainEnabled(false);
        RestoreTpsCamera();
        SetCinematicUIHidden(false);
        playing = null;
    }

    private bool AnyTimelinePlaying() =>
        (entranceMapDirector != null && entranceMapDirector.state == PlayState.Playing) ||
        (entranceDirector != null && entranceDirector.state == PlayState.Playing) ||
        (phase2Director != null && phase2Director.state == PlayState.Playing) ||
        (defeatDirector != null && defeatDirector.state == PlayState.Playing);

    private void StopAllTimelines()
    {
        StopLegacyCinemaTracks();
        StopTimeline(entranceMapDirector, null);
        StopTimeline(entranceDirector, entranceBridge);
        StopTimeline(phase2Director, phase2Bridge);
        StopTimeline(defeatDirector, defeatBridge);
    }

    private static void StopTimeline(PlayableDirector director, B1_CameraTimelineBridge bridge)
    {
        bridge?.ForceStop();
        if (director != null && director.state == PlayState.Playing) director.Stop();
    }

    private static void ConfigureTimelineDirector(PlayableDirector director)
    {
        if (director == null) return;
        director.playOnAwake = false;
        director.time = 0;
    }

    private void StopLegacyCinemaTracks()
    {
        var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null || behaviour.GetType().Name != "CinemaTrack") continue;
            behaviour.SendMessage("Stop", SendMessageOptions.DontRequireReceiver);
            behaviour.enabled = false;
        }
    }

    // シーンに1つだけの CinemachineBrain を有効/無効にする。演出中だけ有効化し、
    // 終わったら無効化して TPSCameraController に実カメラを返す。
    // 未割り当てなら実カメラ側から自動で探す(設定漏れでも TPS 復帰が壊れないように)。
    private void SetBrainEnabled(bool enabled)
    {
        if (cinemachineBrain == null) cinemachineBrain = ResolveBrain();
        if (cinemachineBrain != null) cinemachineBrain.enabled = enabled;
        else if (!enabled)
            Debug.LogWarning("[B1_CameraDirector] CinemachineBrain が見つからないため TPS へ戻せません。" +
                             "Inspector の Cinemachine > Cinemachine Brain に実カメラの CinemachineBrain を割り当ててください。", this);
    }

    private CinemachineBrain ResolveBrain()
    {
        if (activeCameraController != null)
        {
            var b = activeCameraController.GetComponent<CinemachineBrain>();
            if (b != null) return b;
        }
        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
        {
            var b = cam.GetComponent<CinemachineBrain>();
            if (b != null) return b;
        }
        return FindObjectOfType<CinemachineBrain>(true);
    }

    /// <summary>
    /// Entrance Timeline 終端の Signal から明示的に呼んでカメラを TPS へ戻す。
    /// time/duration の polling 判定に頼らず確実に演出を終わらせたいときに使う。
    /// (登場ディゾルブの完了は待たないので、ディゾルブが終わる位置に Signal を置くこと)
    /// </summary>
    public void ReturnToTPS()
    {
        if (playing != null) { StopCoroutine(playing); playing = null; }
        StopAllTimelines();
        ActivateVcam(null);
        SetBrainEnabled(false);
        RestoreTpsCamera();
        SetCinematicUIHidden(false);
        entranceBridge?.PlayHealthGaugeEntrance();
    }

    // 指定した vcam だけを enabled にし、他シーンの vcam はすべて disabled にする。
    // GameObject の active ではなく CinemachineCamera コンポーネントの enabled を切り替えるので、
    // 同じ GameObject に PlayableDirector や B1_CameraTimelineBridge が同居していても
    // それらを巻き込んで非アクティブ化しない(Bridge の購読やコルーチンが切れない)。
    // 複数の vcam が同時に enabled だと Brain が Priority で選んでしまうため、常に1体だけに絞る。
    private void ActivateVcam(CinemachineCamera active)
    {
        SetVcamEnabled(entranceMapVcam, active);
        SetVcamEnabled(entranceVcam, active);
        SetVcamEnabled(phase2Vcam, active);
        SetVcamEnabled(defeatVcam, active);
        SetVcamEnabled(tailRainVcam, active);
        SetVcamEnabled(downVcam, active);
    }

    private static void SetVcamEnabled(CinemachineCamera vcam, CinemachineCamera active)
    {
        if (vcam == null) return;
        bool shouldBeEnabled = vcam == active;
        if (vcam.enabled != shouldBeEnabled) vcam.enabled = shouldBeEnabled;
    }

    private B1_CameraSceneSettings ResolveScene(B1_CameraSceneKind kind)
    {
        switch (kind)
        {
            case B1_CameraSceneKind.Phase2:
                if (phase2Scene != null) return phase2Scene;
                if (runtimePhase2Default == null)
                    runtimePhase2Default = B1_CameraSceneSettings.CreateRuntimeDefault(kind);
                return runtimePhase2Default;

            case B1_CameraSceneKind.Defeat:
                if (defeatScene != null) return defeatScene;
                if (runtimeDefeatDefault == null)
                    runtimeDefeatDefault = B1_CameraSceneSettings.CreateRuntimeDefault(kind);
                return runtimeDefeatDefault;

            default:
                if (entranceScene != null) return entranceScene;
                if (runtimeEntranceDefault == null)
                    runtimeEntranceDefault = B1_CameraSceneSettings.CreateRuntimeDefault(B1_CameraSceneKind.Entrance);
                return runtimeEntranceDefault;
        }
    }

    private Transform ResolveCameraTransform(out TPSCameraController tps)
    {
        tps = cameraController;
        if (tps == null) tps = TPSCameraController.Instance;
        if (tps == null) tps = FindObjectOfType<TPSCameraController>();

        activeCameraController = tps;
        if (tps != null) return tps.transform;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        return cam != null ? cam.transform : null;
    }

    private void RestoreTpsCamera()
    {
        var tps = activeCameraController;
        if (tps == null)
        {
            tps = cameraController;
            if (tps == null) tps = TPSCameraController.Instance;
            if (tps == null) tps = FindObjectOfType<TPSCameraController>();
        }
        if (tps == null) return;

        tps.ResyncFromCurrentView();
        tps.enabled = true;
        activeCameraController = null;
    }

    private void OnDisable()
    {
        if (fixedCamBlend != null)
        {
            StopCoroutine(fixedCamBlend);
            fixedCamBlend = null;
        }
        downCamActive = false;
        RestoreDownCamFov();
        if (playing != null)
        {
            StopCoroutine(playing);
            playing = null;
        }
        StopAllTimelines();
        ActivateVcam(null);
        SetBrainEnabled(false);
        RestoreTpsCamera();
        SetCinematicUIHidden(false); // Disable 中はフェード不可なので即時再表示になる
        SetPlayerScaryWalk(false);
        DisposeEntranceMapLetterbox();
    }

    private void DisposeEntranceMapLetterbox()
    {
        if (entranceMapLetterbox == null) return;
        entranceMapLetterbox.Dispose();
        entranceMapLetterbox = null;
    }

#if UNITY_EDITOR
    // Timeline 再生中に B1_CameraTimelineBridge が生成する一時オブジェクト(フォグ/UI/衝撃波)は
    // HideFlags.DontSave のため、プレビュー中に再コンパイル(ドメインリロード)されると
    // 自己破棄の購読が切れて恒久的にシーンへ残る。リロード直前と Play 切替の境目で強制的に掃除する。
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterPreviewCleanup()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= DestroyPreviewTempObjects;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DestroyPreviewTempObjects;
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change == UnityEditor.PlayModeStateChange.ExitingEditMode ||
            change == UnityEditor.PlayModeStateChange.EnteredEditMode)
            DestroyPreviewTempObjects();
    }

    private static void DestroyPreviewTempObjects()
    {
        DestroyAllOfType<B1_CinematicOverlay>();
        DestroyAllOfType<B1_ShockwaveDistortion>();
        DestroyAllOfType<B1_CinematicFog>();
    }

    private static void DestroyAllOfType<T>() where T : Component
    {
        var found = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in found)
            if (c != null) DestroyImmediate(c.gameObject);
    }
#endif
}
