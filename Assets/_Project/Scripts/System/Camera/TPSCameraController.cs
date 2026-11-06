/**===========================================================================

 File   : カメラ管理クラス [TPSCameraController.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------
 三人称 後追いカメラ (C++ 版 player_camera.cpp の操作感を移植)。

 ■ yaw は 2 層: 入力/自動回転が targetYaw を書き、cameraYaw が指数平滑で追従。
 ■ 進行方向への自動回転 (後追いの核): 「マウス未操作 かつ 移動中」のときだけ
   ヨーを進行方向の背後へ寄せる。停止中(慣性スライド含む)は今の視点を保持。
 ■ 注視点 (lookTarget): カメラローカルの非対称ボックスで別途追従。
 ■ 距離/高さ/ピッチは状態別 (Home / Jump / Fall) に滑らかに遷移。
 ■ 演出 API: チャージズーム / パンチ / ダッシュ引き / ロックオン / スラムズーム。
 ===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class TPSCameraController : MonoBehaviour {

    public static TPSCameraController Instance { get; private set; }

    /// <summary>状態別カメラリグ (距離 / 高さ / ピッチ / 注視点高さオフセット)。</summary>
    [System.Serializable]
    public struct CamRig {
        public float distance;
        public float height;
        public float pitch;
        public float lookHeightOffset;
        public CamRig(float d, float h, float p, float l) {
            distance = d; height = h; pitch = p; lookHeightOffset = l;
        }
    }

    [Header("追従対象")]
    [SerializeField] private Transform target;

    [Header("状態別 定位置 (Unity スケール。参照値は Home13/1/15/1, Jump14/3/10/3, Fall13/2/27/2)")]
    [Tooltip("通常時")]
    [SerializeField] private CamRig homeRig = new CamRig(7.5f, 1.4f, 15f, 1.4f);
    [Tooltip("ジャンプ上昇中 (引き気味・高め・ピッチ浅め)")]
    [SerializeField] private CamRig jumpRig = new CamRig(8.0f, 2.2f, 10f, 2.5f);
    [Tooltip("落下中 (ピッチを立てて足元を見せる)")]
    [SerializeField] private CamRig fallRig = new CamRig(7.5f, 1.6f, 27f, 1.8f);

    [Header("注視点追従 (後追いラグ)")]
    [Tooltip("移動中の注視点追従ベース速度")]
    [SerializeField] private float followSpeedMoving = 11.0f;
    [Tooltip("静止中の注視点追従ベース速度")]
    [SerializeField] private float followSpeedIdle = 5.0f;
    [Tooltip("空中の注視点追従ベース速度")]
    [SerializeField] private float followSpeedAirborne = 12.0f;
    [Tooltip("カメラ前方(奥行)方向の追従倍率 (大きいほど奥行方向は素早く詰める)")]
    [SerializeField] private float followForwardMult = 2.5f;
    [Tooltip("カメラ横方向の追従倍率")]
    [SerializeField] private float followRightMult = 2.0f;
    [Tooltip("注視点 Y 追従速度 (接地 / 空中)")]
    [SerializeField] private float yFollowGround = 10.0f;
    [SerializeField] private float yFollowAir = 15.0f;

    [Header("yaw 平滑 / ピッチ制限")]
    [Tooltip("cameraYaw が targetYaw へ追従する速度 (指数平滑)")]
    [SerializeField] private float rotationSmoothing = 15.0f;
    [SerializeField] private float minPitch = -60.0f;
    [SerializeField] private float maxPitch = 80.0f;

    [Header("進行方向への自動回転 (後追い)")]
    [Tooltip("後追いの ON/OFF")]
    [SerializeField] private bool enableAutoRotate = true;
    [Tooltip("前進時(strafe比0)の自動回転速度 (度/秒)")]
    [SerializeField] private float autoRotateSpeedMin = 20.0f;
    [Tooltip("横移動時(strafe比1)の自動回転速度 (度/秒)")]
    [SerializeField] private float autoRotateSpeedMax = 80.0f;
    [Tooltip("自動回転速度のランプ(ease)速度")]
    [SerializeField] private float autoRotateEaseSpeed = 5.0f;
    [Tooltip("マウス操作後、後追い(ヨー)とピッチ復帰を止める時間 (秒)。この時間が経つと"
        + "「移動中なら」後追いが再開する。長すぎると移動再開しても後追いが復帰しないので短め(0.15)。")]
    [SerializeField] private float manualCooldownTime = 0.15f;

    [Header("移動中のピッチ復帰")]
    [Tooltip("移動中(かつマウス未操作)に、ピッチをホーム角へ戻す速度。大きいほど速い")]
    [SerializeField] private float startResetEaseSpeed = 1.0f;
    [Tooltip("状態遷移(Home/Jump/Fall)時のピッチ復帰速度")]
    [SerializeField] private float statePitchEaseSpeed = 3.0f;

    [Header("マウス操作")]
    [Tooltip("マウス1ピクセルあたりの回転量(度)。参照は0.15")]
    [SerializeField] private float mouseSensitivity = 0.15f;

    [Header("ゲームパッド視点操作")]
    [Tooltip("右スティックの横回転速度 (度/秒)")]
    [SerializeField] private float gamepadLookSpeedX = 160f;
    [Tooltip("右スティックの縦回転速度 (度/秒)")]
    [SerializeField] private float gamepadLookSpeedY = 100f;

    [Header("速度しきい値 (状態判定)")]
    [SerializeField] private float jumpVelocityThreshold = 2.0f;
    [SerializeField] private float fallVelocityThreshold = -1.0f;
    [Tooltip("接地が外れてから Fall とみなすまでの猶予 (秒)。接地レイキャストが1〜数フレーム" +
        "外れる程度のチラつきでカメラが Fall に飛ぶのを防ぐ (プレイヤーの CoyoteTime と同趣旨)。")]
    [SerializeField] private float airborneGraceTime = 0.15f;

    [Header("FOV")]
    [SerializeField] private float baseFov = 50.0f;
    [SerializeField] private float fovEaseSpeed = 8.0f;

    /// <summary>ゲーム標準の FOV。カットシーン等で TPS を止めている間に FOV を正しく保つ用
    /// (Cinemachine の CinemachineBrain 等が別 FOV を残すと、TPS 停止中のイントロで広角に化ける)。</summary>
    public float BaseFov => baseFov;

    [Header("チャージ演出 (既存互換)")]
    [Tooltip("チャージ満タン時に寄る量 (m, 正で距離が縮む)")]
    [SerializeField] private float chargeZoomMax = 0.8f;
    [SerializeField] private float chargeZoomLerpSpeed = 5f;

    [Header("チャージFOV演出")]
    [Tooltip("チャージ満タン時に FOV が広がる量 (度)。チャージ進捗に応じてゆっくり広がる")]
    [SerializeField] private float chargeFovWiden = 12f;
    [Tooltip("チャージFOVの追従速度 (小さいほどゆっくり広がる)")]
    [SerializeField] private float chargeFovEaseSpeed = 2.5f;

    [Header("ロックオン")]
    [Tooltip("ロックオン追尾の最大ヨー速度 (度/秒)")]
    [SerializeField] private float lockOnYawSpeed = 140f;
    [Tooltip("手動カメラ入力を離したあと、追尾速度が最大へ戻るランプ速度。" +
        "小さいほど「最初ゆっくり→徐々に」ロックオンへ復帰する")]
    [SerializeField] private float lockOnRampSpeed = 1.6f;

    [Header("スラムズーム (攻撃発動〜スタンプ着地までFOVで寄る)")]
    [Tooltip("スタンプ落下中に FOV を狭める量 (度)。着地で解除されゆっくり戻る")]
    [SerializeField] private float slamZoomFov = 10f;
    [Tooltip("寄りの速さ")]
    [SerializeField] private float slamZoomInSpeed = 9f;
    [Tooltip("着地後の戻りの速さ (小さいほどゆっくり戻る)")]
    [SerializeField] private float slamZoomOutSpeed = 3f;

    [Header("ダッシュ演出")]
    [Tooltip("ダッシュ中にカメラを引く量 (m, 正で距離が伸びる)")]
    [SerializeField] private float dashPullDistance = 0.9f;
    [Tooltip("引き始めの速さ (大きいほど素早く引く)")]
    [SerializeField] private float dashPullInSpeed = 9f;
    [Tooltip("戻りの速さ (小さいほどゆっくり元の距離へ戻る)")]
    [SerializeField] private float dashPullOutSpeed = 2f;

    [Header("体格スケール追従 (アリス=縮小で寄る / 巨大化で引く)")]
    [Tooltip("ON で PlayerSizeController.CurrentMultiplier に応じて距離・高さ・注視点を体格へ合わせる")]
    [SerializeField] private bool enableSizeFollow = true;
    [Tooltip("距離への効き。1=完全比例 (3倍で3倍遠く=引きすぎ)、0=距離は変えない。0.7 が自然")]
    [SerializeField, Range(0f, 1f)] private float sizeDistanceExponent = 0.7f;
    [Tooltip("距離倍率の下限 (縮小時に寄りすぎない)")]
    [SerializeField, Min(0.05f)] private float sizeDistanceMin = 0.55f;
    [Tooltip("距離倍率の上限 (巨大化時に引きすぎない)")]
    [SerializeField, Min(1f)] private float sizeDistanceMax = 2.2f;
    [Tooltip("体格変化へのカメラ追従速度。PlayerSizeController(12) より遅くしてドリーを柔らかく見せる")]
    [SerializeField] private float sizeFollowSpeed = 5f;

    [Header("衝突回避 (参照には無いオプション。既定OFF=参照と同じ)")]
    [Tooltip("ON にすると壁で寄るが、誤って寄りすぎることもあるので参照同様 既定OFF")]
    [SerializeField] private bool enableCollision = false;
    [SerializeField] private float collisionOffset = 0.2f;
    [SerializeField] private LayerMask collisionMask = ~0;

    [Header("地面めり込み防止")]
    [Tooltip("注視点→カメラ間を遮る「床」(上向き法線) にだけ当ててカメラを地表へ押し出す。" +
        "壁 (Wall タグ) は下の clampToWall が担当。Wall_Clear 等は両方とも素通り")]
    [SerializeField] private bool clampToGround = true;
    [Tooltip("地表からの浮かせ量 (m)")]
    [SerializeField] private float groundClearance = 0.3f;
    [Tooltip("これより法線が上向き (normal.y がこの値以上) のときだけ「床」とみなす")]
    [SerializeField, Range(0f, 1f)] private float groundNormalY = 0.4f;

    [Header("壁めり込み防止")]
    [Tooltip("注視点→カメラ間を遮る「壁」(Wall タグ) に当ててカメラを壁手前へ押し出す。" +
        "Wall_Clear など許可外タグは無視してカメラを通す (参照 SecondPhaseSequence の許可制と同じ)")]
    [SerializeField] private bool clampToWall = true;
    [Tooltip("壁面からの浮かせ量 (m)")]
    [SerializeField] private float wallClearance = 0.3f;

    // ===== 内部: yaw / pitch =====
    private float targetYaw;
    private float cameraYaw;
    private float targetPitch;
    private float cameraPitch;

    // ===== 内部: 状態別パラメータ (現在値=easeで目標へ追従) =====
    private enum CameraState { Home, Jump, Fall }
    private CameraState cameraState = CameraState.Home;
    private float airborneTimer;          // 接地が外れている連続時間 (猶予判定用)
    private float currentDistance;
    private float currentHeight;
    private float currentLookOffset;
    // 目標値 (状態に応じて UpdateCameraState が設定。Jump→Fall は中間ブレンドで滑らかに繋ぐ)
    private float targetDistance;
    private float targetHeight;
    private float targetLookOffset;
    private float targetPitchAbsolute;   // 現在状態のピッチ目標
    private bool  forcePitchTransition;  // 状態遷移直後のピッチ復帰中

    // 状態遷移の基準速度 (参照 player_camera.cpp: speed_opacity=0.1 を畳み込み済み)。
    // 実際の ease 速度は進捗ベースの倍率 (easeOutQuart/easeInCubic/easeOutCubic) を掛けて決まる。
    private const float DistanceMoveSpeed   = 0.15f; // 参照 1.5 * 0.1
    private const float HeightMoveSpeed     = 0.10f; // 参照 1.0 * 0.1
    private const float LookHeightMoveSpeed = 0.08f; // 参照 0.8 * 0.1
    private const float JumpFallBlend       = 0.3f;  // Jump→Fall 時に目標を中間へ寄せる割合 (急変防止)

    // ===== 内部: 注視点 =====
    private Vector3 lookTarget;
    private bool lookTargetInit;

    // ===== 内部: 自動回転 =====
    private float autoRotateCurrent;
    private float manualCooldown;
    // 縦(ピッチ)の手動エイム専用クールダウン。横の見回しでは立たない。空中の上→足元ピッチ遷移は
    // これが 0 の間つねに走るので、見回し(横)では途切れず、意図的な縦エイム中だけ手動を尊重する。
    private float pitchManualCooldown;

    // ===== 内部: PlayerMover からの通知 =====
    private Vector3 playerMoveDir;   // ワールド進行方向 (単位)
    private bool    isPlayerMoving;
    private float   stickX, stickY;  // 生入力 (camera非依存) strafe比用
    private float   playerVelocityY;
    private bool    playerGrounded = true;

    // ===== 内部: FOV / チャージ / パンチ =====
    private Camera cam;
    private float currentFov;
    private float chargeProgressTarget;
    private float chargeZoomCurrent;
    private float punchZoomCurrent;
    private float dashZoomCurrent;
    private bool  dashPullActive;

    // ===== 内部: 体格スケール追従 =====
    private PlayerSizeController sizeCtrl;   // target 上の体格スケール元 (キャッシュ)
    private Transform sizeCtrlFor;           // どの target 用に引いたか (差し替え検知)
    private float sizeMulCurrent = 1f;       // 平滑済みの体格倍率 (高さ/注視点はこれに比例)
    private float sizeDistFactor = 1f;       // 距離倍率 (pow + clamp 済み)
    private Coroutine punchRoutine;
    private float chargeFovProgress;   // チャージFOVの目標進捗 (0..1)
    private float chargeFovCurrent;    // 現在の広がり量 (度)
    private float fovPunchCurrent;     // FOVパンチの瞬間加算量 (度, 負でズームイン)
    private Coroutine fovPunchRoutine;

    // ===== 内部: ロックオン =====
    private Transform lockOnTarget;
    private float lockOnSteerCurrent;   // 現在の追尾ヨー速度 (手動優先のランプ用)

    // ===== 内部: スラムズーム =====
    private bool  slamZoomActive;
    private float slamFovCurrent;       // 負の値 = 望遠 (寄り)

    private int weaponLayerBit;
    private int playerLayerBit;

    private const float MouseEpsilon = 0.0001f;
    private const float Deg2Rad = Mathf.Deg2Rad;
    private const float Rad2Deg = Mathf.Rad2Deg;

    /// <summary>水平カメラ前方 (互換用)。</summary>
    public Vector3 FlatForward {
        get {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }
    }

    public float CameraYaw => cameraYaw;

    /// <summary>distance<=0 の(=シーン未保存の)リグを既定値へ補修。設定済みなら尊重。</summary>
    private static CamRig RepairRig(CamRig r, float d, float h, float p, float l) {
        return r.distance > 0.01f ? r : new CamRig(d, h, p, l);
    }

    private void Awake() {
        if (Instance == null) Instance = this;
    }

    private void OnDestroy() {
        if (Instance == this) Instance = null;
    }

    private void Start() {
        // コンポーネント書き換えでシーン側に CamRig が未保存(=0)の場合、距離0で至近距離に
        // なってしまうため、distance<=0 のリグは参照準拠の既定値へ補修する (設定済み値は尊重)。
        homeRig = RepairRig(homeRig, 7.5f, 1.4f, 15f, 1.4f);
        jumpRig = RepairRig(jumpRig, 8.0f, 2.2f, 10f, 2.5f);
        fallRig = RepairRig(fallRig, 7.5f, 1.6f, 27f, 1.8f);

        // baseFov が 0(以下) で直列化されていると Start で currentFov=0 になり超望遠ズーム
        // (=至近距離に見える) になる。0 は FOV として無効なので参照準拠の 50 へ補修する。
        if (baseFov < 1f) baseFov = 50f;

        cameraYaw = transform.eulerAngles.y;
        targetYaw = cameraYaw;
        cameraPitch = homeRig.pitch;
        targetPitch = cameraPitch;

        currentDistance = homeRig.distance;
        currentHeight = homeRig.height;
        currentLookOffset = homeRig.lookHeightOffset;
        targetDistance = homeRig.distance;
        targetHeight = homeRig.height;
        targetLookOffset = homeRig.lookHeightOffset;
        targetPitchAbsolute = homeRig.pitch;

        // 体格倍率は「現在値」で初期化する (1 で始めると、アリス装備で開始した時に寄りが1回滑る)
        sizeMulCurrent = ReadSizeMultiplier();
        RefreshSizeFactors();

        int weaponLayer = LayerMask.NameToLayer("Weapon");
        weaponLayerBit = weaponLayer >= 0 ? (1 << weaponLayer) : 0;
        int playerLayer = LayerMask.NameToLayer("Player");
        playerLayerBit = playerLayer >= 0 ? (1 << playerLayer) : 0;

        cam = GetComponent<Camera>();
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;
        currentFov = baseFov;
        if (cam != null) cam.fieldOfView = baseFov;

        if (target != null) lookTarget = target.position;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ===== PlayerMover からの通知 (Update で呼ばれ、この LateUpdate より前に届く) =====
    public void NotifyPlayerMovement(Vector3 worldMoveDir) {
        worldMoveDir.y = 0f;
        if (worldMoveDir.sqrMagnitude > 1e-6f) {
            playerMoveDir = worldMoveDir.normalized;
            isPlayerMoving = true;
        }
    }
    public void NotifyStickInput(float x, float y) { stickX = x; stickY = y; }
    public void NotifyPlayerVelocityY(float vy) { playerVelocityY = vy; }
    public void NotifyPlayerGrounded(bool grounded) { playerGrounded = grounded; }

    private void LateUpdate() {
        if (target == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (!lookTargetInit) { lookTarget = target.position; lookTargetInit = true; }

        // 接地猶予タイマ: 接地が外れている連続時間を計測 (1〜数フレームのレイキャストチラつきを吸収)
        if (playerGrounded) airborneTimer = 0f;
        else airborneTimer += dt;

        HandleManualInput(dt);
        UpdateCameraState();
        UpdateCameraParameters(dt);
        if (enableAutoRotate) HandleAutoRotation(dt);
        HandleLockOn(dt);   // ロックオンは自動回転の後 (対象がいる間は敵方向優先)

        // クールダウン減算は自動回転の後 (同フレームの新規入力はこの自動回転を止める)
        if (manualCooldown > 0f) manualCooldown -= dt;
        if (pitchManualCooldown > 0f) pitchManualCooldown -= dt;

        // yaw 平滑
        cameraYaw = Mathf.LerpAngle(cameraYaw, targetYaw, 1f - Mathf.Exp(-rotationSmoothing * dt));

        UpdateLookTarget(dt);
        ApplyTransform();

        // 移動フラグは毎フレームの最後にリセット。NotifyPlayerMovement が翌フレーム再付与。
        isPlayerMoving = false;
    }

    // ===== マウス入力: 手動オービット =====
    // 横(ヨー)入力と縦(ピッチ)入力を分離して扱う。これは見た目以上に重要:
    //   「上→足元」のジャンプ時ピッチ遷移は forcePitchTransition フラグが Home→Jump で
    //   立ち、それが Fall まで持ち越されて速い ease(=statePitchEaseSpeed) で駆動される。
    //   (Fall 単体の easeInCubic 由来 ease は開始 0.3 と激遅で、単体では足元角まで届かない)
    //   旧実装は「縦横どちらの動きでも」forcePitchTransition を解除していたため、ジャンプ中に
    //   ただ見回す(横入力)だけでこの駆動が消え、足元への遷移が死んでいた。
    //   参照 player_camera.cpp(L905) は force_pitch_transition を delta_y!=0 のときだけ解除する。
    //   それに合わせ、ピッチへの介入(手動エイム+force解除)は「縦入力があったときだけ」行う。
    private void HandleManualInput(float dt) {
        bool hasInput = false;

        // マウス移動量に、右スティックを同じ単位 (px相当) へ換算して合算する。
        // 以降のヨー/ピッチ処理はマウスと共通 (targetYaw += d.x * mouseSensitivity)。
        Vector2 d = Vector2.zero;
        if (Mouse.current != null) d = Mouse.current.delta.ReadValue();
        var gp = Gamepad.current;
        if (gp != null && mouseSensitivity > 0.0001f) {
            Vector2 s = gp.rightStick.ReadValue();
            if (s.sqrMagnitude > 0.01f)
                d += new Vector2(s.x * gamepadLookSpeedX, s.y * gamepadLookSpeedY) * (dt / mouseSensitivity);
        }

        {
            float adx = Mathf.Abs(d.x);
            float ady = Mathf.Abs(d.y);

            // 横(ヨー): 視点を回すだけ。ピッチ系(forcePitchTransition / pitchManualCooldown)には触れない。
            if (adx > MouseEpsilon) {
                targetYaw += d.x * mouseSensitivity;
                hasInput = true;
            }

            // 縦(ピッチ)エイム: 「縦が横以上」= 意図的な上下エイムのときだけ手動ピッチにする。
            // こうすると見回し(横優勢)に必ず混じる微小な縦ジッタではピッチを手動化せず、
            // 空中の上→足元ピッチ遷移を止めない。意図的に上下を見たときだけ手動が効く。
            if (ady > MouseEpsilon && ady >= adx) {
                targetPitch -= d.y * mouseSensitivity;   // マウス上=見上げ
                targetPitch  = Mathf.Clamp(targetPitch, minPitch, maxPitch);
                cameraPitch  = targetPitch;              // ピッチは手動時 即時
                forcePitchTransition = false;
                pitchManualCooldown = manualCooldownTime;
                hasInput = true;
            }
        }

        if (hasInput) manualCooldown = manualCooldownTime;
    }

    // ===== 状態 Home / Jump / Fall 判定 + 目標値設定 (参照 UpdateCameraState 準拠) =====
    private void UpdateCameraState() {
        CameraState prev = cameraState;
        CameraState next;
        // 接地が airborneGraceTime 以上外れて初めて「空中」とみなす。これで接地レイキャストが
        // 1〜数フレーム外れる程度のチラつきでは Fall に飛ばず、idle 時の pitch ドリフトを防ぐ。
        // Jump は vy>閾値 が明確な上昇シグナル (チラつきでは決して出ない) なので即時判定でよい。
        bool camAirborne = airborneTimer > airborneGraceTime;
        if (playerVelocityY > jumpVelocityThreshold && !playerGrounded) {
            next = CameraState.Jump;
        }
        else if (playerVelocityY < fallVelocityThreshold && camAirborne) {
            next = CameraState.Fall;
        }
        else if (camAirborne && cameraState == CameraState.Jump) {
            next = CameraState.Jump;
        }
        else if (camAirborne && cameraState == CameraState.Fall) {
            next = CameraState.Fall;
        }
        else {
            next = CameraState.Home;
        }

        if (next != prev) {
            // Jump->Fall は強制ピッチ遷移しない (現在値から滑らかに繋ぐ)
            if (!(prev == CameraState.Jump && next == CameraState.Fall)) {
                forcePitchTransition = true;
            }
        }
        cameraState = next;

        // 目標値の設定。Jump→Fall は変化が大きいので「現在値 + 30% だけ Fall 側」へ寄せ、
        // 残りはその後の進捗ベース ease で詰める (参照 blend_factor=0.3。カクつき防止の要)。
        if (prev == CameraState.Jump && next == CameraState.Fall) {
            targetDistance      = currentDistance   + (fallRig.distance         - currentDistance)   * JumpFallBlend;
            targetHeight        = currentHeight     + (fallRig.height           - currentHeight)     * JumpFallBlend;
            targetLookOffset    = currentLookOffset + (fallRig.lookHeightOffset - currentLookOffset) * JumpFallBlend;
            targetPitchAbsolute = cameraPitch       + (fallRig.pitch            - cameraPitch)       * JumpFallBlend;
        }
        else {
            CamRig rig = cameraState == CameraState.Jump ? jumpRig
                       : cameraState == CameraState.Fall ? fallRig
                       : homeRig;
            targetDistance      = rig.distance;
            targetHeight        = rig.height;
            targetLookOffset    = rig.lookHeightOffset;
            targetPitchAbsolute = rig.pitch;
        }
    }

    // 進捗 0..1: from→to 区間を cur がどこまで進んだか (参照の distance_progress 等)。
    private static float TransitionProgress(float cur, float from, float to) {
        float total = Mathf.Abs(to - from);
        return total > 0.001f ? Mathf.Clamp01(Mathf.Abs(cur - from) / total) : 1f;
    }
    private static float EaseOutQuart(float x) { float u = 1f - x; return 1f - u * u * u * u; }
    private static float EaseInCubic(float x)  { return x * x * x; }
    private static float EaseOutCubic(float x) { float u = 1f - x; return 1f - u * u * u; }

    // ===== 距離/高さ/注視点オフセット/ピッチ/FOV の遷移 (参照 UpdateCameraParameters 準拠) =====
    // 状態ごとに進捗ベースのイージング(easeOutQuart/easeInCubic/easeOutCubic)で遷移「速度」を
    // 変調する。状態切替の瞬間も速度が飛ばず (ease-in で 0 から立ち上がる)、ジャンプ遷移が滑らか。
    private void UpdateCameraParameters(float dt) {
        float distanceSpeed   = DistanceMoveSpeed;
        float heightSpeed     = HeightMoveSpeed;
        float lookHeightSpeed = LookHeightMoveSpeed;

        if (cameraState == CameraState.Jump) {
            float dEase = 0.1f + 0.9f * EaseOutQuart(TransitionProgress(currentDistance,   homeRig.distance,         jumpRig.distance));
            float hEase = 0.1f + 0.9f * EaseOutQuart(TransitionProgress(currentHeight,     homeRig.height,           jumpRig.height));
            float lEase = 0.1f + 0.9f * EaseOutQuart(TransitionProgress(currentLookOffset, homeRig.lookHeightOffset, jumpRig.lookHeightOffset));
            float sync = isPlayerMoving ? 1.5f : 3.0f;
            distanceSpeed *= dEase * sync; heightSpeed *= hEase * sync; lookHeightSpeed *= lEase * sync;
        }
        else if (cameraState == CameraState.Fall) {
            float dEase = 0.02f + 0.98f * EaseInCubic(TransitionProgress(currentDistance,   jumpRig.distance,         fallRig.distance));
            float hEase = 0.02f + 0.98f * EaseInCubic(TransitionProgress(currentHeight,     jumpRig.height,           fallRig.height));
            float lEase = 0.02f + 0.98f * EaseInCubic(TransitionProgress(currentLookOffset, jumpRig.lookHeightOffset, fallRig.lookHeightOffset));
            float sync = isPlayerMoving ? 0.5f : 0.8f;
            distanceSpeed *= dEase * sync; heightSpeed *= hEase * sync; lookHeightSpeed *= lEase * sync;
        }
        else { // Home: 復帰の進捗 (1=home到達) で easeOutCubic、参照同様 ×2.0
            float dStart = Mathf.Max(Mathf.Abs(fallRig.distance         - homeRig.distance),         Mathf.Abs(jumpRig.distance         - homeRig.distance));
            float hStart = Mathf.Max(Mathf.Abs(fallRig.height           - homeRig.height),           Mathf.Abs(jumpRig.height           - homeRig.height));
            float lStart = Mathf.Max(Mathf.Abs(fallRig.lookHeightOffset - homeRig.lookHeightOffset), Mathf.Abs(jumpRig.lookHeightOffset - homeRig.lookHeightOffset));
            float dProg = dStart > 0.001f ? Mathf.Clamp01(1f - Mathf.Abs(homeRig.distance         - currentDistance)   / dStart) : 1f;
            float hProg = hStart > 0.001f ? Mathf.Clamp01(1f - Mathf.Abs(homeRig.height           - currentHeight)     / hStart) : 1f;
            float lProg = lStart > 0.001f ? Mathf.Clamp01(1f - Mathf.Abs(homeRig.lookHeightOffset - currentLookOffset) / lStart) : 1f;
            distanceSpeed   *= (0.1f + 0.9f * EaseOutCubic(dProg)) * 2.0f;
            heightSpeed     *= (0.1f + 0.9f * EaseOutCubic(hProg)) * 2.0f;
            lookHeightSpeed *= (0.1f + 0.9f * EaseOutCubic(lProg)) * 2.0f;
        }

        // 参照同様 speed*dt*10 を時定数にした指数 ease で目標へ詰める。
        currentDistance   += (targetDistance   - currentDistance)   * (1f - Mathf.Exp(-distanceSpeed   * dt * 10f));
        currentHeight     += (targetHeight     - currentHeight)     * (1f - Mathf.Exp(-heightSpeed     * dt * 10f));
        currentLookOffset += (targetLookOffset - currentLookOffset) * (1f - Mathf.Exp(-lookHeightSpeed * dt * 10f));

        // ===== ピッチの遷移 (上→足元の核) =====
        // 空中(Jump/Fall)は「縦の手動エイム中(pitchManualCooldown>0)」だけ手動ピッチを尊重し、
        // それ以外はつねに状態ピッチ(Jump=上/Fall=足元)へ statePitchEaseSpeed で確実に寄せ続ける。
        // ★ forcePitchTransition の Jump→Fall 持ち越しに依存させない。旧実装はこの持ち越し(ease3.0)
        //   頼みで、Fall 単体の弱い ease(easeInCubic 開始0.3)では足元角まで届かなかった。よって
        //   ジャンプ中に見回す/一瞬縦に触れて forcePitchTransition が消えると「上→足元が動かない」
        //   状態になっていた。空中では入力が止めば必ず再開し、横の見回しは pitchManualCooldown を
        //   立てない(HandleManualInput)ので途切れない。
        // 地上(Home)は従来通り 状態遷移直後(forcePitchTransition)だけ寄せ、手動中(cooldown)は据え置く。
        bool airborne = cameraState == CameraState.Jump || cameraState == CameraState.Fall;
        bool shouldAutoPitch = airborne ? (pitchManualCooldown <= 0f)
                                        : (forcePitchTransition && manualCooldown <= 0f);
        if (shouldAutoPitch) {
            float pitchDiff = targetPitchAbsolute - cameraPitch;
            if (Mathf.Abs(pitchDiff) > 0.01f) {
                cameraPitch += pitchDiff * (1f - Mathf.Exp(-statePitchEaseSpeed * dt));
                targetPitch = cameraPitch;
            }
            if (forcePitchTransition && Mathf.Abs(targetPitchAbsolute - cameraPitch) < 0.5f)
                forcePitchTransition = false;
        }

        // チャージズーム
        chargeZoomCurrent = Mathf.Lerp(chargeZoomCurrent, chargeProgressTarget * chargeZoomMax,
                                       1f - Mathf.Exp(-chargeZoomLerpSpeed * dt));

        // ダッシュ引き (開始は素早く引き、終了後はゆっくり元へ戻す)
        float dashSpeed = dashPullActive ? dashPullInSpeed : dashPullOutSpeed;
        dashZoomCurrent = Mathf.Lerp(dashZoomCurrent, dashPullActive ? dashPullDistance : 0f,
                                     1f - Mathf.Exp(-dashSpeed * dt));

        // 体格スケール追従 (アリス=0.5倍で寄る / 巨大化=3倍で引く)。
        // CurrentMultiplier は PlayerSizeController 側で既に平滑 (12) されているが、着替えロード中は
        // 即時スナップする実装なので、ここでもう一段ゆるく詰めてドリーを常に滑らかにする。
        sizeMulCurrent = enableSizeFollow
                       ? Mathf.Lerp(sizeMulCurrent, ReadSizeMultiplier(), 1f - Mathf.Exp(-sizeFollowSpeed * dt))
                       : 1f;
        RefreshSizeFactors();

        // チャージFOV: 進捗に応じて「ゆっくり」広がる (専用のおそめ ease)
        chargeFovCurrent = Mathf.Lerp(chargeFovCurrent, chargeFovProgress * chargeFovWiden,
                                      1f - Mathf.Exp(-chargeFovEaseSpeed * dt));

        // スラムズーム: 発動〜着地の間 FOV で寄る (負値)。着地後はゆっくり戻す
        float slamSpeed = slamZoomActive ? slamZoomInSpeed : slamZoomOutSpeed;
        slamFovCurrent = Mathf.Lerp(slamFovCurrent, slamZoomActive ? -slamZoomFov : 0f,
                                    1f - Mathf.Exp(-slamSpeed * dt));

        // FOV (チャージ広がり+スラム寄りを含む基準へ追従。パンチは ApplyTransform で瞬間加算)
        currentFov = Mathf.Lerp(currentFov, baseFov + chargeFovCurrent + slamFovCurrent,
                                1f - Mathf.Exp(-fovEaseSpeed * dt));
    }

    /**=======================================================================
      体格スケール追従
        target はプレイヤー root そのもの (= PlayerSizeController が載っている GameObject) なので、
        追加のシリアライズ配線なしで引ける。target 差し替えにも追従できるようキャッシュキー付き。
        push (PlayerSizeController→カメラ) にしないのは、カメラがカットシーン中に disable される
        (CinemaTrack / B1_CameraDirector) 間に push 値が腐るため。pull なら復帰後の初回で読み直す。
    =======================================================================**/
    private float ReadSizeMultiplier() {
        if (target == null) return 1f;
        if (sizeCtrlFor != target) {
            sizeCtrlFor = target;
            sizeCtrl = target.GetComponent<PlayerSizeController>()
                    ?? target.GetComponentInParent<PlayerSizeController>();
        }
        return sizeCtrl != null ? sizeCtrl.CurrentMultiplier : 1f;
    }

    /// <summary>平滑済み倍率 → 距離倍率 (完全比例だと3倍巨大化で引きすぎるので pow で鈍らせて clamp)。</summary>
    private void RefreshSizeFactors() {
        sizeDistFactor = Mathf.Clamp(Mathf.Pow(Mathf.Max(0.01f, sizeMulCurrent), sizeDistanceExponent),
                                     sizeDistanceMin, Mathf.Max(sizeDistanceMin, sizeDistanceMax));
    }

    // ===== ロックオン: プレイヤー→敵の方向へヨーをレート制限で寄せる =====
    // カメラはプレイヤーの背後から敵を正面に捉える向きになる (ソフトロック)。
    // ・手動カメラ入力 (マウス/右スティック) を優先: 操作直後 (クールダウン中) は追尾しない
    // ・入力を離すと追尾速度が 0 からランプアップ → 「最初ゆっくり、徐々に」ロックオンへ戻る
    private void HandleLockOn(float dt) {
        if (lockOnTarget == null || target == null) {
            lockOnSteerCurrent = 0f;
            return;
        }

        float decayT = 1f - Mathf.Exp(-autoRotateEaseSpeed * dt);
        if (manualCooldown > 0f) {
            // 手動優先: 追尾を止め、ランプもリセット方向へ
            lockOnSteerCurrent = Mathf.Lerp(lockOnSteerCurrent, 0f, decayT);
            return;
        }

        // 手動を離したあと: ゆっくり最大速度までランプアップ
        lockOnSteerCurrent = Mathf.Lerp(lockOnSteerCurrent, lockOnYawSpeed,
                                        1f - Mathf.Exp(-lockOnRampSpeed * dt));

        Vector3 to = lockOnTarget.position - target.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.04f) return;   // ほぼ真上/直下は向き不定なのでスキップ
        float goalYaw = Mathf.Atan2(to.x, to.z) * Rad2Deg;
        targetYaw = Mathf.MoveTowardsAngle(targetYaw, goalYaw, lockOnSteerCurrent * dt);
    }

    // ===== 進行方向への自動回転 (後追いの核) =====
    // 仕様:
    //   ・マウス操作中(クールダウン中)      → 何もしない (手動優先)
    //   ・マウス未操作 かつ 移動中           → 定位置へ戻す (ヨーを進行方向の背後へ + 接地時はピッチをホームへ)
    //   ・マウス未操作 かつ 停止中           → 何もしない (今の視点を保持)
    //   ※「移動中」= 実際の入力移動のみ。慣性スライドは PlayerMover が「移動」と通知しないので
    //     isPlayerMoving=false となり、スティックを離した後・停止後は後追いしない。
    private void HandleAutoRotation(float dt) {
        float decayT = 1f - Mathf.Exp(-autoRotateEaseSpeed * dt);

        // マウス操作直後は手動優先: 後追いもピッチ復帰もしない
        if (manualCooldown > 0f) {
            autoRotateCurrent = Mathf.Lerp(autoRotateCurrent, 0f, decayT);
            return;
        }

        // 非移動 (停止・慣性スライド中) → 今の視点を保持 (戻さない)
        if (!isPlayerMoving) {
            autoRotateCurrent = Mathf.Lerp(autoRotateCurrent, 0f, decayT);
            return;
        }

        // --- 移動中 かつ マウス未操作: 接地時はピッチをホームへゆるく戻す (空中は遷移側が担当) ---
        if (cameraState == CameraState.Home) {
            float pitchDiff = targetPitchAbsolute - cameraPitch;
            if (Mathf.Abs(pitchDiff) > 0.5f) {
                cameraPitch = Mathf.Lerp(cameraPitch, targetPitchAbsolute,
                                         1f - Mathf.Exp(-startResetEaseSpeed * dt));
                targetPitch = cameraPitch;
            }
        }

        // --- ヨー後追い: 進行方向の背後へ targetYaw を寄せる ---
        Vector3 md = playerMoveDir; md.y = 0f;
        float moveLen = md.magnitude;
        if (moveLen < 0.1f) { autoRotateCurrent = Mathf.Lerp(autoRotateCurrent, 0f, decayT); return; }

        float yawRad = cameraYaw * Deg2Rad;
        Vector3 camFwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
        Vector3 mdn = md / moveLen;
        float dot = camFwd.x * mdn.x + camFwd.z * mdn.z;

        // 真後ろ移動はスキップ (180°スピン防止)
        if (dot < -0.95f) { autoRotateCurrent = Mathf.Lerp(autoRotateCurrent, 0f, decayT); return; }

        // strafe 比で回転速度を選択 (前進=ゆるい / 横=機敏)
        float fwdIn = Mathf.Abs(stickY);
        float strafeIn = Mathf.Abs(stickX);
        float strafeRatio = strafeIn / (fwdIn + strafeIn + 0.01f);
        float targetRotSpeed = autoRotateSpeedMin + (autoRotateSpeedMax - autoRotateSpeedMin) * strafeRatio;
        autoRotateCurrent = Mathf.Lerp(autoRotateCurrent, targetRotSpeed, decayT);

        // 目標 yaw とデッドゾーン
        float goalYaw = Mathf.Atan2(md.x, md.z) * Rad2Deg;
        if (Mathf.Abs(Mathf.DeltaAngle(targetYaw, goalYaw)) < 0.5f) return;

        // レート制限付きで targetYaw を寄せる
        targetYaw = Mathf.MoveTowardsAngle(targetYaw, goalYaw, autoRotateCurrent * dt);
    }

    // ===== 注視点追従 (カメラローカル 非対称ボックス) =====
    private void UpdateLookTarget(float dt) {
        Vector3 p = target.position;
        float yawRad = cameraYaw * Deg2Rad;
        float cy = Mathf.Cos(yawRad);
        float sy = Mathf.Sin(yawRad);

        float dx = p.x - lookTarget.x;
        float dz = p.z - lookTarget.z;
        float localForward = dx * sy + dz * cy;   // 奥行
        float localRight   = dx * cy - dz * sy;   // 横

        bool airborne = !playerGrounded;
        float baseFollow = airborne ? followSpeedAirborne
                         : (isPlayerMoving ? followSpeedMoving : followSpeedIdle);

        float fFactor = 1f - Mathf.Exp(-baseFollow * followForwardMult * dt);
        float rFactor = 1f - Mathf.Exp(-baseFollow * followRightMult * dt);
        float fF = localForward * fFactor;
        float rF = localRight * rFactor;

        lookTarget.x += fF * sy + rF * cy;
        lookTarget.z += fF * cy - rF * sy;

        float yFollow = airborne ? yFollowAir : yFollowGround;
        lookTarget.y += (p.y - lookTarget.y) * (1f - Mathf.Exp(-yFollow * dt));
    }

    // ===== 最終的なカメラ位置・注視・FOV =====
    private void ApplyTransform() {
        float finalPitch = Mathf.Clamp(cameraPitch, minPitch, maxPitch);
        float yawRad = cameraYaw * Deg2Rad;
        float pitchRad = finalPitch * Deg2Rad;
        float sy = Mathf.Sin(yawRad), cyaw = Mathf.Cos(yawRad);
        float sp = Mathf.Sin(pitchRad), cp = Mathf.Cos(pitchRad);

        // 有効距離 = 状態距離×体格倍率 - チャージ寄り - パンチ寄り + ダッシュ引き (下限あり)。
        // ※体格倍率は「基準距離」にだけ掛ける。合計に掛けると巨大化中に dashPull(0.9m) や
        //   chargeZoom(0.8m) の効き量まで倍化して、既存演出の手触り (加算メートル) が壊れる。
        float dist = Mathf.Max(0.5f, currentDistance * sizeDistFactor
                                     - chargeZoomCurrent - punchZoomCurrent + dashZoomCurrent);

        Vector3 offset = new Vector3(-sy * cp * dist, sp * dist, -cyaw * cp * dist);
        // 高さ/注視点は「足元からの身体の高さ」なので距離と違い完全比例させる。掛けないと
        // 縮小時は注視点がアリスの頭上へ、巨大化時は足首へズレる。
        Vector3 camPos = lookTarget + Vector3.up * (currentHeight * sizeMulCurrent) + offset;
        Vector3 lookAt = lookTarget + Vector3.up * (currentLookOffset * sizeMulCurrent);

        if (enableCollision) {
            int mask = collisionMask.value & ~weaponLayerBit & ~playerLayerBit;
            if (Physics.Linecast(lookAt, camPos, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore)) {
                camPos = hit.point + hit.normal * collisionOffset;
            }
        }

        // 地面・壁めり込み防止。注視点→カメラの線分を最初に遮る面を見て、
        // 「Ground タグ かつ 上向き法線」= 本物の床にはカメラを地表へ、
        // 「Wall タグ」= 壁にはカメラを壁手前へ押し出す (どちらも許可タグのみ反応)。
        // ※ Ground はタグ判定が無いとプロップの天面 (箱の上面等も法線は上向き) に反応してワープする。
        // ※ Wall_Clear など許可外タグは素通り (参照 SecondPhaseSequence の cameraCollisionTags と同じ許可制)。
        if (clampToGround || clampToWall) {
            int gmask = collisionMask.value & ~weaponLayerBit & ~playerLayerBit;
            if (Physics.Linecast(lookAt, camPos, out RaycastHit ghit, gmask, QueryTriggerInteraction.Ignore)) {
                if (clampToGround && ghit.normal.y >= groundNormalY && ghit.collider.CompareTag("Ground")) {
                    camPos = ghit.point + ghit.normal * groundClearance;
                }
                else if (clampToWall && ghit.collider.CompareTag("Wall")) {
                    camPos = ghit.point + ghit.normal * wallClearance;
                }
            }
        }

        transform.position = camPos;
        transform.LookAt(lookAt, Vector3.up);

        if (cam != null) cam.fieldOfView = currentFov + fovPunchCurrent;
    }

    /**=======================================================================
      チャージ進捗 (0..1)。0 で通常距離、1 で chargeZoomMax 分寄る。
    =======================================================================**/
    public void SetChargeProgress(float progress01) {
        chargeProgressTarget = Mathf.Clamp01(progress01);
    }

    /**=======================================================================
      ダッシュ中のカメラ引きを ON/OFF する (StateRoll から呼ぶ)。
      ON で dashPullDistance ぶん素早く引き、OFF でゆっくり元の距離へ戻る。
    =======================================================================**/
    public void SetDashPull(bool active) {
        dashPullActive = active;
    }

    /**=======================================================================
      チャージFOV進捗 (0..1)。進捗に応じて FOV が chargeFovWiden 度までゆっくり広がる。
      StateAttack が毎フレーム進捗を渡し、Exit で 0 に戻す。
    =======================================================================**/
    public void SetChargeFov(float progress01) {
        chargeFovProgress = Mathf.Clamp01(progress01);
    }

    /**=======================================================================
      FOVパンチ: 一瞬バッと広がって(または縮んで)戻る (非スケール時間)。
      amount : 加算量 (度)。正で広角 (チャージ完了のバッ)、負で望遠 (攻撃のズームイン)
      duration       : ピークへ寄せる時間 (秒)
      returnDuration : 元へ戻す時間 (秒)。負なら duration の半分ずつ (従来の山なり互換)。
                       寄りより長くすると「グッと寄ってゆっくり戻る」演出になる。
      連打で再発動しても現在値からピークへ滑らかに繋ぐ (値の飛び=ガクつきを出さない)。
    =======================================================================**/
    public void FovPunch(float amount = 8f, float duration = 0.3f, float returnDuration = -1f) {
        if (!isActiveAndEnabled) return;
        if (fovPunchRoutine != null) StopCoroutine(fovPunchRoutine);
        SplitPunchDurations(duration, returnDuration, out float inDur, out float outDur);
        fovPunchRoutine = StartCoroutine(FovPunchRoutine(amount, inDur, outDur));
    }

    private IEnumerator FovPunchRoutine(float amount, float inDur, float outDur) {
        float from = fovPunchCurrent;   // 直前の演出から連続的に繋ぐ
        float t = 0f;
        while (t < inDur) {
            t += Time.unscaledDeltaTime;
            fovPunchCurrent = Mathf.Lerp(from, amount, EaseOutCubic(Mathf.Clamp01(t / inDur)));
            yield return null;
        }
        t = 0f;
        while (t < outDur) {
            t += Time.unscaledDeltaTime;
            fovPunchCurrent = amount * (1f - EaseInOutSine(Mathf.Clamp01(t / outDur)));
            yield return null;
        }
        fovPunchCurrent = 0f;
        fovPunchRoutine = null;
    }

    // 寄り/戻り時間の振り分け。returnDuration 未指定 (<0) は従来の sin 山なり相当 = 半分ずつ。
    private static void SplitPunchDurations(float duration, float returnDuration,
                                            out float inDur, out float outDur) {
        if (returnDuration >= 0f) { inDur = Mathf.Max(0.01f, duration); outDur = Mathf.Max(0.01f, returnDuration); }
        else                      { inDur = Mathf.Max(0.01f, duration * 0.5f); outDur = inDur; }
    }

    private static float EaseInOutSine(float x) { return 0.5f * (1f - Mathf.Cos(Mathf.PI * x)); }

    /**=======================================================================
      ロックオン対象の設定 / 解除。対象がいる間、ヨーが敵方向へ寄り続ける。
      解除は「自分が設定した対象なら」のみ (他者のロックを誤って消さない)。
    =======================================================================**/
    public void SetLockOnTarget(Transform t) {
        lockOnTarget = t;
    }

    public void ClearLockOnTarget(Transform t = null) {
        if (t == null || lockOnTarget == t) lockOnTarget = null;
    }

    /**=======================================================================
      スラムズーム: 攻撃発動で ON (FOVで寄る) → 最後のスタンプ着地で OFF
      (ゆっくり元の画角へ戻る)。PlayerAttack が発動/着地タイミングで呼ぶ。
    =======================================================================**/
    public void SetSlamZoom(bool active) {
        slamZoomActive = active;
    }

    /**=======================================================================
      パンチズーム: 一瞬グワッと寄って戻る。
      intensity      : 寄せ量 (m, 正で距離が縮む / 負で引き)
      duration       : ピークへ寄せる時間 (秒)
      returnDuration : 元へ戻す時間 (秒)。負なら duration の半分ずつ (従来の山なり互換)。
                       寄りより長くすると「グッと寄ってゆっくり戻る」演出になる。
      連打で再発動しても現在値からピークへ滑らかに繋ぐ (値の飛び=ガクつきを出さない)。
    =======================================================================**/
    public void PunchZoom(float intensity = 0.3f, float duration = 0.3f, float returnDuration = -1f) {
        if (!isActiveAndEnabled) return;   // 無効化中 (StickerBook 等) はコルーチン起動不可
        if (punchRoutine != null) StopCoroutine(punchRoutine);
        SplitPunchDurations(duration, returnDuration, out float inDur, out float outDur);
        punchRoutine = StartCoroutine(PunchRoutine(intensity, inDur, outDur));
    }

    private IEnumerator PunchRoutine(float intensity, float inDur, float outDur) {
        float from = punchZoomCurrent;   // 直前の演出から連続的に繋ぐ
        float t = 0f;
        while (t < inDur) {
            t += Time.unscaledDeltaTime;
            punchZoomCurrent = Mathf.Lerp(from, intensity, EaseOutCubic(Mathf.Clamp01(t / inDur)));
            yield return null;
        }
        t = 0f;
        while (t < outDur) {
            t += Time.unscaledDeltaTime;
            punchZoomCurrent = intensity * (1f - EaseInOutSine(Mathf.Clamp01(t / outDur)));
            yield return null;
        }
        punchZoomCurrent = 0f;
        punchRoutine = null;
    }

    /**=======================================================================
      シネマ演出フック: ホームリグでの後追い姿勢を、指定ヨー(度)で算出する。
      イントロ演出の最終ウェイポイントをこの姿勢に合わせて着地させると、
      ResyncFromCurrentView() 併用で後追い再開時にスナップが出ない。
    =======================================================================**/
    public void GetHomeFollowPose(float yawDeg, out Vector3 pos, out Quaternion rot) {
        Vector3 focus = (target != null) ? target.position : transform.position;
        float yawRad = yawDeg * Deg2Rad;
        float pitchRad = homeRig.pitch * Deg2Rad;
        float sy = Mathf.Sin(yawRad), cyaw = Mathf.Cos(yawRad);
        float sp = Mathf.Sin(pitchRad), cp = Mathf.Cos(pitchRad);

        // 復帰後の ApplyTransform と同じ体格補正を掛ける (掛けないとアリス/巨大化のまま
        // カットシーンが明けた瞬間に距離と注視点が飛ぶ)。ここは「いまの体格」の即値で計算し、
        // ResyncFromCurrentView の再シードと一致させる。
        float mul  = enableSizeFollow ? Mathf.Max(0.01f, ReadSizeMultiplier()) : 1f;
        float dfac = enableSizeFollow
                   ? Mathf.Clamp(Mathf.Pow(mul, sizeDistanceExponent), sizeDistanceMin,
                                 Mathf.Max(sizeDistanceMin, sizeDistanceMax))
                   : 1f;
        float d = homeRig.distance * dfac;

        Vector3 offset = new Vector3(-sy * cp * d, sp * d, -cyaw * cp * d);
        pos = focus + Vector3.up * (homeRig.height * mul) + offset;
        Vector3 lookAt = focus + Vector3.up * (homeRig.lookHeightOffset * mul);
        Vector3 dir = lookAt - pos;
        rot = (dir.sqrMagnitude > 1e-6f) ? Quaternion.LookRotation(dir, Vector3.up) : transform.rotation;
    }

    /**=======================================================================
      シネマ演出フック: 外部制御 (イントロ等) で transform を直接動かした後に
      呼ぶ。内部平滑状態を「現在の transform + 追従対象」へ同期し直し、これから
      enabled=true に戻したときの最初の LateUpdate が現在姿勢を再現してスナップ
      しないようにする (Start は再実行されないため、状態が古いままなのを防ぐ)。
      GetHomeFollowPose() の姿勢へ着地してから呼ぶと継ぎ目が出ない。
    =======================================================================**/
    public void ResyncFromCurrentView() {
        cameraYaw = targetYaw = transform.eulerAngles.y;
        cameraPitch = targetPitch = homeRig.pitch;
        targetPitchAbsolute = homeRig.pitch;
        forcePitchTransition = false;
        currentDistance = targetDistance = homeRig.distance;
        currentHeight = targetHeight = homeRig.height;
        currentLookOffset = targetLookOffset = homeRig.lookHeightOffset;
        cameraState = CameraState.Home;
        airborneTimer = 0f;
        autoRotateCurrent = 0f;
        manualCooldown = 0f;
        pitchManualCooldown = 0f;
        chargeProgressTarget = 0f;
        chargeZoomCurrent = 0f;
        punchZoomCurrent = 0f;
        dashZoomCurrent = 0f;
        dashPullActive = false;
        // 体格倍率だけは 0 リセットではなく「今の体格」へ再シード。1 に戻すとアリス装備のまま
        // カットシーンから復帰した瞬間にカメラが引きへスナップする。
        sizeMulCurrent = ReadSizeMultiplier();
        RefreshSizeFactors();
        chargeFovProgress = 0f;
        chargeFovCurrent = 0f;
        fovPunchCurrent = 0f;
        lockOnTarget = null;
        lockOnSteerCurrent = 0f;
        slamZoomActive = false;
        slamFovCurrent = 0f;
        currentFov = baseFov;
        if (cam != null) cam.fieldOfView = baseFov;
        if (target != null) lookTarget = target.position;
        lookTargetInit = true;
    }
}
