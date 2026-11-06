/**===========================================================================

 File   : 接地ステート [StateGrounded.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 地面に立っている時のロコモーションステート。移動・ジャンプ・回避・攻撃トリガー担当。
 移動可否: チャージ中はカメラ方向へアイムしつつ低速移動 (ジャンプ/回避ブロック)、
 攻撃発動〜硬直中は移動不可、通常は移動 + 回避 / ジャンプ / 攻撃トリガー。
 Update() は責務別メソッドに分割: ロック判定 → 移動/アクション → レイヤー合成 → 移動アニメ → 接地遷移。
 ===========================================================================**/

using UnityEngine;

public class StateGrounded : IPlayerState {

    protected IPlayerContext player;
    private bool isJump = false;

    // 接地判定が一瞬チラついても即落下扱いしないための猶予 (コヨーテタイム)
    private float ungroundedTimer = 0f;
    private const float CoyoteTime = 0.15f;

    // 移動アニメ(Move)の前フレーム値。加速中か減速中かを判定して damp を切り替える。
    private float prevPlanarSpeed = 0f;
    private const float MoveAccelDamp = 0.02f; // 加速: ほぼラグ無しで実速度に密着 (足の滑り防止)
    private const float MoveDecelDamp = 0.10f; // 減速: 慣性スライドへぬるっと戻す

    // 攻撃発動後の硬直タイマ。硬直時間・チャージ中の移動速度倍率は
    // IPlayerConfig (PlayerLifetimeScope の Inspector) で調整する。
    private float attackLockTimer = 0f;

    // ダッシュ(回避)クールタイム。StateGrounded は回避では作り直されない(接地継続)ため、
    // 回避をまたいで保持される。着地(空中→接地)で作り直された時だけリセット。
    private float lastDodgeTime = -10f;

    // 下半身レイヤー (Layer1)。チャージ中だけ重み1で脚を Locomotion(Walk) に上書きし、
    // 上半身は Layer0 の Charge ポーズを保つ (上半身チャージ / 下半身Walk)。
    private const int   LowerBodyLayer = 1;
    private const float LayerBlendTime = 0.12f; // レイヤー重みのフェード時間(秒)

    // チャージ中の下半身再生速度。後退時は -1 にして Walk を逆再生 (前進と同じ歩容で後ろへ)。
    private float lowerSpeedCurrent = 1f;
    private const float LowerSpeedBlendTime = 0.12f;

    public StateGrounded(IPlayerContext player) {
        this.player = player;
    }

    public virtual void Enter() {
        player.Animator.SetBool(PlayerAnimator.IsLandParam, true);
        // 着地時に未消費の JumpStart が残っていると、復帰直後に Locomotion -> Fall が
        // 一瞬発火して Fall がチラつく (空中で Dodge/Attack した / フワッと頂点超えした等)。
        // 接地した時点でクリアしておく。
        player.Animator.ResetTrigger(PlayerAnimator.JumpStartParam);
    }

    public virtual void Update() {
        // Land ステートは廃止。接地復帰は IsLand=true による Fall -> Locomotion 遷移が担う。
        bool charging = player.StateMachine.IsInActionState<StateAttack>();
        bool attackLocked = UpdateAttackLock();

        // 回避でアクションステートへ遷移した場合は以降をスキップ。
        if (!HandleMovementAndActions(charging, attackLocked)) return;

        player.Mover.ApplyGravity();
        UpdateLowerBodyLayer(charging);
        UpdateMoveAnimation();
        UpdateGroundedTransition();
    }

    public virtual void Exit() { }

    /// <summary>攻撃発動後の硬直タイマを進め、移動ロック中かを返す。</summary>
    private bool UpdateAttackLock() {
        bool attackActive = player.Attack.IsAttacking;
        if (attackActive) attackLockTimer = player.AttackRecoveryTime;
        else if (attackLockTimer > 0f) attackLockTimer -= Time.deltaTime;
        return attackActive || attackLockTimer > 0f;
    }

    /// <summary>
    /// 移動とアクション入力 (回避/ジャンプ/攻撃) を処理する。
    /// 回避でステート遷移した場合は false (= Update を中断) を返す。
    /// </summary>
    private bool HandleMovementAndActions(bool charging, bool attackLocked) {
        // ノックバック中: 移動・アクション入力を受け付けない (押し出しは PlayerHitReaction が駆動)。
        // ここで Move を呼ぶと WASD がノックバック方向を打ち消して自由移動できてしまう。
        // Move(zero) で慣性を止めつつ、後続の重力/アニメ同期/接地遷移は継続する。
        if (player.Health != null && player.Health.IsInKnockback) {
            player.Mover.Move(Vector2.zero);
            return true;
        }

        // 回避中: 移動・アクション入力を受け付けない (移動は StateRoll が固定方向で駆動)。
        // ここで Move を呼ぶと WASD がロール方向を曲げてしまう。重力/アニメ同期/接地遷移
        // (呼び出し元の後続処理) は継続する。
        if (player.StateMachine.IsInActionState<StateRoll>()) return true;

        // コンボ中は移動を止める (前進はコンボのランジが担当)。ここで早期 return して、
        // 連打入力は StateComboAttack だけが拾わせる (StateGrounded が新たな StateAttack を
        // 発火して二重に処理しないように)。
        if (player.StateMachine.IsInActionState<StateComboAttack>()) {
            player.Mover.Move(Vector2.zero);
            return true;
        }

        Vector2 moveInput = isJump ? player.Input.Move * 0.5f : player.Input.Move;

        if (charging) {
            // カメラ方向へアイムを維持しつつゆっくり移動 (向きはカメラ固定)。
            // notifyCameraTrail:false でカメラの後追い自動旋回を止める (左右移動でカメラが勝手に向かない)。
            player.Mover.FaceCamera();
            player.Mover.Move(moveInput, player.ChargeMoveSpeedMul, rotate: false, notifyCameraTrail: false);
            return true;
        }

        if (attackLocked) {
            // 発動〜硬直中は移動不可 (慣性で滑らかに停止)。
            player.Mover.Move(Vector2.zero);
            return true;
        }

        player.Mover.Move(moveInput);

        // ScaryWalk 解除漏れフォールバック: 操作が返った後に WASD 移動したら、上半身 (Base Layer)
        // に残った ScaryWalk を通常ロコモーションへ戻す。ScaryWalk の出口は ToMove トリガーのみ
        // なので、演出側 (B1_CameraDirector) が SetPlayerScaryWalk(false) を通らない経路で
        // 操作が返ると、下半身は LowerBody レイヤーで歩くのに頭 (上半身) だけ ScaryWalk が残る。
        if (moveInput.sqrMagnitude > 0.01f) ReleaseScaryWalkIfLingering();

        // 回避 (ダッシュ)。クールタイム中は不可。
        if (player.Input.IsDodgePress && Time.time - lastDodgeTime >= player.DodgeCooldown) {
            lastDodgeTime = Time.time;
            player.StateMachine.ChangeActionState(new StateRoll(player));
            return false;
        }
        // ジャンプ
        if (player.Input.ConsumeJump()) {
            player.Animator.SetTrigger(PlayerAnimator.JumpParam);
            player.Mover.Jump();
            player.Sound?.PlayJump();
            isJump = true;
        }
        // 攻撃トリガー (タップ=コンボ / 長押し=チャージ の振り分けは StateAttack が担当)。
        // ※ Charge1 クールダウン(CanAttack)ではゲートしない。コンボの連打を妨げないため
        //   (IsAttackPress は UI/メニュー表示中は既に false)。
        if (player.Input.IsAttackPress) {
            player.StateMachine.ChangeActionState(new StateAttack(player));
        }
        return true;
    }

    /// <summary>
    /// Base Layer に残留した ScaryWalk を解除する (移動入力があった時のみ呼ばれる)。
    /// 下半身 Idle 上書きも解いて Layer1 の重みを 0 へフェードさせる。
    /// </summary>
    private void ReleaseScaryWalkIfLingering() {
        // 遷移中はカレントステートがまだ ScaryWalk を返すため、二重発火しないよう除外する。
        Animator uAnim = player.Animator.UnityAnimator;
        bool inScaryWalk = uAnim != null && !uAnim.IsInTransition(0)
                        && uAnim.GetCurrentAnimatorStateInfo(0).IsName("ScaryWalk");
        if (!inScaryWalk && !player.ScaryWalkLowerIdle) return;

        player.SetScaryWalkLowerIdle(false);
        player.Animator.ResetTrigger(PlayerAnimator.ScaryWalkParam); // 未消費の再突入トリガーを掃除
        // ToMove は他ステート (ノックバック復帰等) の出口にも使われるため、実際に
        // ScaryWalk に居るときだけ撃つ (残留トリガーによる誤遷移を避ける)。
        if (inScaryWalk) player.Animator.SetTrigger(PlayerAnimator.ToMoveParam);
    }

    /// <summary>上半身=Charge(Layer0) / 下半身=Walk(Layer1) の合成。チャージ中のみ下半身レイヤーを効かせる。</summary>
    private void UpdateLowerBodyLayer(bool charging) {
        Animator uAnim = player.Animator.UnityAnimator;
        if (uAnim == null || uAnim.layerCount <= LowerBodyLayer) return;
        // ScaryWalkLowerIdle: マップ演出の EndPoint 到着後、上半身は Layer0 の ScaryWalk を
        // 保ったまま下半身だけ LowerLocomotion(Move=0 で Idle) に上書きする (B1_CameraDirector が制御)。
        bool lowerIdle = player.ScaryWalkLowerIdle;
        float wTarget = (charging || lowerIdle) ? 1f : 0f;
        float w = Mathf.MoveTowards(uAnim.GetLayerWeight(LowerBodyLayer), wTarget,
                                    Time.deltaTime / LayerBlendTime);
        uAnim.SetLayerWeight(LowerBodyLayer, w);

        // チャージ中の下半身 Locomotion 再生速度:
        //   後退 → 逆再生 (sign=-1。前進W と同じ歩容で後ろへ)。
        //   横移動ほど PlayerMotionSpeeds.ChargeMove (既定1.5) へ寄せて少し速く (前進は等倍)。
        Vector3 camFwd = TPSCameraController.Instance != null ? TPSCameraController.Instance.FlatForward : Vector3.zero;
        Vector3 mwd = player.Mover.MoveWorldDir;
        float fwdDot = Vector3.Dot(mwd, camFwd);                  // 前=+ / 後=−
        Vector3 camRight = new Vector3(camFwd.z, 0f, -camFwd.x);  // カメラ右 (PlayerMover と同式)
        float lateral = Mathf.Abs(Vector3.Dot(mwd, camRight));    // 0=前後 / 1=真横
        float chargeMove = player.MotionSpeeds != null ? player.MotionSpeeds.ChargeMove : 1f;
        float mag = Mathf.Lerp(1f, chargeMove, lateral);          // 横ほど速く
        float sign = (fwdDot < -0.1f) ? -1f : 1f;                 // 後退は逆再生
        float lowerSpeedTarget = charging ? sign * mag : 1f;
        lowerSpeedCurrent = Mathf.MoveTowards(lowerSpeedCurrent, lowerSpeedTarget, Time.deltaTime / LowerSpeedBlendTime);
        uAnim.SetFloat(PlayerAnimator.LowerSpeedParam, lowerSpeedCurrent);
    }

    /// <summary>移動アニメ (Move param) を実速度へ同期。加速は密着 / 減速はスムージング (足の滑り防止)。</summary>
    private void UpdateMoveAnimation() {
        float planarSpeed = player.Mover.PlanarSpeed01;
        float moveDamp = planarSpeed >= prevPlanarSpeed ? MoveAccelDamp : MoveDecelDamp;
        player.Animator.SetFloat(PlayerAnimator.MoveParam, planarSpeed, moveDamp, Time.deltaTime);
        prevPlanarSpeed = planarSpeed;
    }

    /// <summary>接地が猶予を超えて外れた / ジャンプ離陸したら空中ステートへ遷移。</summary>
    private void UpdateGroundedTransition() {
        if (player.Mover.IsGrounded) {
            ungroundedTimer = 0f;
            return;
        }
        ungroundedTimer += Time.deltaTime;
        if (isJump || ungroundedTimer > CoyoteTime) {
            // JumpStart (Jump/Loco -> Fall) は降下に転じた時点で StateAirborne が送る。
            player.StateMachine.ChangeLocomotionState(new StateAirborne(player));
        }
    }
}
