/**===========================================================================

 File   : プレイヤー移動コンポーネント [PlayerMover.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 CharacterController を使った移動・ジャンプ・接地判定。
 PlayerLifetimeScope.Awake() で AddComponent される（プレハブ直付けではない）。
 移動feel は C++ 参照実装 (player_state.cpp) 準拠: カメラ相対入力 →
 ドラッグ積分による加速 + 3段回転 + 入力を離すと慣性スライドで停止。
 後追いカメラへ毎フレーム 入力/移動方向/接地状態 を通知して連動させる。
 ===========================================================================**/

using UnityEngine;
using VContainer;

public class PlayerMover : MonoBehaviour, IPlayerMover {

    private CharacterController characterController;
    private Camera mainCamera;
    private IPlayerConfig config;
    private Vector3 velocity;           // 鉛直方向 (y) のみ使用: 重力 / ジャンプ
    private Vector3 planarVelocity;     // 水平方向 (xz) の慣性速度
    private bool isUIMode = false;
    // 固定カメラ移動モード: true の間、移動の基準を TPS 後追いカメラの Yaw ではなく
    // 実際に描画しているカメラ (Camera.main) の Yaw にする。TailRain 等で TPS を止めて
    // 見下ろし固定カメラへ切り替える演出中、スティック入力を「今映っている画面」基準に合わせるため。
    // (TPS を切ると CameraYaw が停止した値のまま残り、固定カメラの向きとズレるのを回避する)
    private bool fixedCameraMove = false;
    private int groundRaycastMask = ~0; // Weapon/Player レイヤーは Awake で除外

    // ===== 移動feel 定数 (参照 player_state.cpp の無次元値を 1:1 移植) =====
    // ※ Drag / WalkFloor / 回転速度 / スライド減衰は Inspector 調整用に
    //   IPlayerConfig (PlayerLifetimeScope) へ移動した。既定値は従来の定数と同一。
    private const float MoveThreshold      = 0.1f;   // これ未満の入力は移動とみなさない
    private const float SpeedDeadzone      = 0.15f;  // 速度倍率カーブのデッドゾーン
    private const float InertiaStopSpeed   = 0.5f;   // これ未満で完全停止 (参照 m_inertia_threshold=0.5)

    public bool IsGrounded { get; private set; }
    public float VerticalVelocity => velocity.y;

    // 直近の目標移動方向 (ワールド・正規化、入力なしは zero)。チャージ中の腰ひねり等が参照。
    public Vector3 MoveWorldDir { get; private set; }

    // 現在の水平移動速度を 0..1 で公開 (アニメ Move 同期用。足の滑り防止)
    public float PlanarSpeed01 =>
        (config != null && config.Speed > 0.001f)
            ? Mathf.Clamp01(new Vector2(planarVelocity.x, planarVelocity.z).magnitude / config.Speed)
            : 0f;

    /// <summary>VContainer メソッド注入: 設定値 (IPlayerConfig) を受け取る。</summary>
    [Inject]
    public void Construct(IPlayerConfig config) {
        this.config = config;
    }

    private void Awake() {
        characterController = GetComponent<CharacterController>();
        mainCamera = Camera.main;

        // 地面判定 Raycast の対象から Player と Weapon を除外
        int playerLayer = LayerMask.NameToLayer("Player");
        int weaponLayer = LayerMask.NameToLayer("Weapon");
        if (playerLayer >= 0) groundRaycastMask &= ~(1 << playerLayer);
        if (weaponLayer >= 0) groundRaycastMask &= ~(1 << weaponLayer);
    }

    /**=======================================================================
      接地判定 (Ground タグ付きのみ地面とみなす)
      カプセル半径ぶんの SphereCast で判定する。中心 1 本の Raycast だと
      小さいブロック (Props/Jumping 等) の縁に立ったとき中心がはみ出して
      外れ、Idle/Fall が暴れるため、CharacterController が物理的に
      立てている場所は必ず接地扱いになるようにする。
    =======================================================================**/
    public void CheckGround() {
        IsGrounded = false;

        // CharacterController はカプセル半径 + skinWidth ぶん縁にはみ出して立てるので、
        // 判定球はカプセルよりわずかに太くする (径が細いと縁で立てているのに落下扱いになる)
        float radius = characterController != null
                     ? characterController.radius + 0.05f
                     : 0.25f;
        Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);
        float   dist   = config.GroundCheckDistance + 0.05f;

        if (Physics.SphereCast(
            origin,
            radius,
            Vector3.down,
            out RaycastHit hit,
            dist,
            groundRaycastMask,
            QueryTriggerInteraction.Ignore)) {
            // 側面 (壁, normal.y≈0) を地面と誤認しない範囲で、ブロックの縁の
            // 角接触 (normal が寝る) も接地として認める
            IsGrounded = hit.collider.CompareTag("Ground") && hit.normal.y > 0.25f;
        }

        if (!IsGrounded) {
            // フォールバック: ブロックの角に乗った「パーチ姿勢」はカプセル底の球が
            // 角に乗り上げ、足元が最大 radius+skin (約0.28m) 浮くため球の掃引では
            // 届かない。足元の正方形フットプリントを長めに下へ掃引して拾う
            // (箱の底面は横端までフラットなので角接触でも上面法線が取れる)。
            Vector3 boxHalf   = new Vector3(radius + 0.02f, 0.05f, radius + 0.02f);
            Vector3 boxCenter = transform.position + Vector3.up * 0.15f;
            if (Physics.BoxCast(
                boxCenter, boxHalf, Vector3.down, out RaycastHit boxHit,
                Quaternion.identity,
                config.GroundCheckDistance + 0.35f,
                groundRaycastMask,
                QueryTriggerInteraction.Ignore)) {
                IsGrounded = boxHit.collider.CompareTag("Ground") && boxHit.normal.y > 0.25f;
            }
        }

        if (IsGrounded && velocity.y < 0) velocity.y = -2f;

        Debug.DrawRay(origin, Vector3.down * (dist + radius),
            IsGrounded ? Color.green : Color.red);
    }

    /**=======================================================================
      移動 (参照 player_state.cpp 準拠: ドラッグ積分 + 3段回転 + 慣性スライド)
      speedMul : 移動速度倍率 (回避バーストなど)
      rotate   : true で入力方向へ自動回転、false で並進のみ (チャージ中は false)
      snap     : true で目標速度へ即時セット (回避バーストの滑り出しを飛ばす)
      notifyCameraTrail : false でカメラへ「移動中」を通知しない (= 後追い自動旋回を止める。
                          チャージ中の左右移動でカメラが勝手に向かないようにする用)
    =======================================================================**/
    public void Move(Vector2 input, float speedMul = 1.0f, bool rotate = true, bool snap = false,
                     bool notifyCameraTrail = true) {
        TPSCameraController cam = TPSCameraController.Instance;
        float dt = Time.deltaTime;

        if (isUIMode) {
            planarVelocity = Vector3.zero;
            MoveWorldDir = Vector3.zero;
            NotifyCamera(cam, input, false, Vector3.zero);
            return;
        }

        // --- カメラ相対 → ワールド移動方向 (参照 CalculateCameraRelativeMove と同一) ---
        float camYaw = ResolveCameraYaw(cam);
        float yawRad = camYaw * Mathf.Deg2Rad;
        Vector3 camForward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
        Vector3 camRight   = new Vector3(camForward.z, 0f, -camForward.x);   // (cos, 0, -sin)
        Vector3 worldMove  = camRight * input.x + camForward * input.y;

        float inputMag = Mathf.Min(input.magnitude, 1f);
        bool  hasInput = worldMove.sqrMagnitude >= MoveThreshold * MoveThreshold && inputMag >= MoveThreshold;
        Vector3 desiredDir = hasInput ? worldMove.normalized : Vector3.zero;
        MoveWorldDir = desiredDir;

        Vector3 notifyDir = Vector3.zero;
        bool    notifyMoving = false;

        if (hasInput) {
            float desiredSpeed = config.Speed * speedMul * SpeedMultiplier(inputMag);

            if (snap) {
                planarVelocity = desiredDir * desiredSpeed;
            }
            else {
                // ドラッグ積分: 力 = 目標速度 * Drag。定常で v→目標速度、時定数 1/Drag。
                float drag = config.MoveDrag;
                float accel = desiredSpeed * drag;
                planarVelocity += desiredDir * (accel * dt);
                planarVelocity *= 1f / (1f + drag * dt);
            }

            if (rotate) RotateTowards(desiredDir, dt);

            // notifyCameraTrail=false (チャージ中) はカメラへ移動通知しない → 後追い自動旋回を抑制。
            notifyDir = desiredDir; notifyMoving = notifyCameraTrail;
        }
        else {
            // 入力なし: 慣性スライド → 停止。
            // ※ スライド中はカメラに「移動中」と通知しない (notifyMoving=false のまま)。
            //   こうしないとスティックを離した後もカメラが後追いして定位置に戻ってしまう。
            //   (注視点追従は target.position を毎フレーム追うので、通知無しでもスライドには付いていく)
            float speed = planarVelocity.magnitude;
            if (speed > InertiaStopSpeed) {
                planarVelocity *= Mathf.Exp(-config.InertiaDecay * dt);
            }
            else {
                planarVelocity = Vector3.zero;
            }
        }

        characterController.Move(planarVelocity * dt);

        NotifyCamera(cam, input, notifyMoving, notifyDir);
    }

    /**=======================================================================
      ワールド方向指定の移動 (回避など「キャラの向き基準」で動かす用)。
      カメラ相対入力へ逆変換して Move() に委譲する (加速・慣性の feel は共通)。
    =======================================================================**/
    public void MoveWorld(Vector3 worldDir, float speedMul = 1.0f, bool rotate = true, bool snap = false,
                          bool notifyCameraTrail = true) {
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude < 0.0001f) {
            Move(Vector2.zero, speedMul, rotate, snap, notifyCameraTrail);
            return;
        }
        worldDir.Normalize();

        TPSCameraController cam = TPSCameraController.Instance;
        float camYaw = ResolveCameraYaw(cam);
        float yawRad = camYaw * Mathf.Deg2Rad;
        Vector3 camForward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
        Vector3 camRight   = new Vector3(camForward.z, 0f, -camForward.x);
        Vector2 input = new Vector2(Vector3.Dot(worldDir, camRight), Vector3.Dot(worldDir, camForward));
        Move(input, speedMul, rotate, snap, notifyCameraTrail);
    }

    /// <summary>向き回転 3 段 (参照既定: >90°=720, >45°=540, それ未満=指数ease8)。</summary>
    private void RotateTowards(Vector3 dir, float dt) {
        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float curYaw = transform.eulerAngles.y;
        float diff = Mathf.Abs(Mathf.DeltaAngle(curYaw, targetYaw));

        float newYaw;
        if (diff > 90f)      newYaw = Mathf.MoveTowardsAngle(curYaw, targetYaw, config.QuickTurnSpeed * dt);
        else if (diff > 45f) newYaw = Mathf.MoveTowardsAngle(curYaw, targetYaw, config.MediumTurnSpeed * dt);
        else                 newYaw = Mathf.LerpAngle(curYaw, targetYaw, 1f - Mathf.Exp(-config.RotEaseSpeed * dt));

        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }

    /// <summary>入力量(0..1) → 速度倍率(WalkFloor..1.0) 線形ランプ。</summary>
    private float SpeedMultiplier(float inputMag) {
        float walkFloor = config.WalkFloor;
        if (inputMag <= SpeedDeadzone) return walkFloor;
        float n = Mathf.Clamp01((inputMag - SpeedDeadzone) / (1f - SpeedDeadzone));
        return walkFloor + (1f - walkFloor) * n;
    }

    /// <summary>後追いカメラへ毎フレーム状態を通知。</summary>
    private void NotifyCamera(TPSCameraController cam, Vector2 input, bool moving, Vector3 worldDir) {
        if (cam == null) return;
        cam.NotifyStickInput(input.x, input.y);
        cam.NotifyPlayerVelocityY(IsGrounded ? 0f : velocity.y);
        cam.NotifyPlayerGrounded(IsGrounded);
        if (moving) cam.NotifyPlayerMovement(worldDir);
    }

    /**=======================================================================
      カメラ正面方向へ滑らかに向く (チャージ中の継続追従用)
    =======================================================================**/
    public void FaceCamera() {
        if (mainCamera == null) return;
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return;
        Quaternion targetRotation = Quaternion.LookRotation(forward.normalized);
        float rotT = 1f - Mathf.Exp(-config.RotationSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotT);
    }

    public void Jump() {
        if (IsGrounded) {
            // ジャンプ初速 v = sqrt(2 * |g| * h)
            velocity.y = Mathf.Sqrt(config.JumpHeight * -2f * config.Gravity);
        }
    }

    public void ApplyGravity() {
        if (isUIMode) return;
        if (!IsGrounded) velocity.y += config.Gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }

    /**=======================================================================
      カメラ正面方向へ即座に向く（攻撃開始時などに使用）
    =======================================================================**/
    public void FaceCameraInstant() {
        if (mainCamera == null) return;
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(forward.normalized);
    }

    /**=======================================================================
      指定ワールド地点の方向へ即座に向く（コンボのロックオン攻撃で敵へ向く）
    =======================================================================**/
    public void FacePointInstant(Vector3 worldPoint) {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    /**=======================================================================
      移動基準の Yaw を決める。通常は TPS 後追いカメラの CameraYaw。
      固定カメラ移動モード中は、実際に描画しているカメラ (Camera.main) の Yaw を
      使う (TailRain の見下ろし固定カメラ中に、画面基準で動かすため)。
    =======================================================================**/
    private float ResolveCameraYaw(TPSCameraController cam) {
        if (!fixedCameraMove && cam != null) return cam.CameraYaw;

        if (mainCamera == null) mainCamera = Camera.main; // 破棄/再取得の保険
        if (mainCamera != null) return mainCamera.transform.eulerAngles.y;

        return cam != null ? cam.CameraYaw : 0f;
    }

    /// <summary>固定カメラ移動モードの ON/OFF。ON の間、移動基準を Camera.main の向きに切り替える。</summary>
    public void SetFixedCameraMove(bool on) {
        fixedCameraMove = on;
    }

    public void SetUIMode(bool uiMode) {
        isUIMode = uiMode;
        if (uiMode) {
            velocity = Vector3.zero;
            planarVelocity = Vector3.zero;
        }
    }
}
