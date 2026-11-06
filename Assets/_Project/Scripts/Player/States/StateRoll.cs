/**===========================================================================

 File   : ロールステート [StateRoll.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 回避ロール中のアクションステート。

 ・回避方向はロール開始時点の「キャラの向き」で固定 (カメラ/入力方向は使わない)
 ・回避中は WASD 等の移動入力を受け付けない (StateGrounded 側でもブロック)
 ・回避中は SkinnedMeshTrailRenderer による残像を放出する (PlayerDodgeTrail)
 ===========================================================================**/

using UnityEngine;

public class StateRoll : IPlayerState {

    protected IPlayerContext player;
    private float rollTimer = 0f;
    private Vector3 rollDir = Vector3.forward;
    private PlayerDodgeTrail trail;

    public StateRoll(IPlayerContext player) {
        this.player = player;
    }

    public virtual void Enter() {
        Debug.Log("Roll!");
        player.Animator.SetBool(PlayerAnimator.IsRollParam, true);
        player.Sound?.PlayDodge();

        // 回避方向 = ロール開始時点のキャラの向き (以降は入力で変わらない)
        rollDir = player.Mover.transform.forward;
        rollDir.y = 0f;
        rollDir = rollDir.sqrMagnitude > 0.0001f ? rollDir.normalized : Vector3.forward;

        // 残像放出開始 (未装着ならランタイムで自動構成)
        trail = PlayerDodgeTrail.Ensure(player.Mover.gameObject);
        trail.SetEmitting(true);

        // ダッシュ中はカメラを少し引く (終了時にゆっくり戻る)
        if (TPSCameraController.Instance != null)
            TPSCameraController.Instance.SetDashPull(true);
    }

    public virtual void Update() {
        // 慣性ランプを飛ばして即バースト (snap)。終了後は残速度が自然減速する。
        // DodgeMultiplier は speedMul 経由で渡す (入力ベクトルに掛けると Move 内の
        // inputMag クランプで倍率が消えてバーストにならない)。
        player.Mover.MoveWorld(rollDir, speedMul: player.DodgeMultiplier, rotate: false, snap: true);

        // 所要時間 = 距離 ÷ 速度。DodgeMultiplier を上げても距離は変わらず速くなる
        rollTimer += Time.deltaTime;
        float rollDuration = player.DodgeDistance
                           / Mathf.Max(0.1f, player.Speed * player.DodgeMultiplier);
        if (rollTimer >= rollDuration) {
            player.StateMachine.ChangeActionState(new StatePlayerNull());
        }
    }

    public virtual void Exit() {
        player.Animator.SetBool(PlayerAnimator.IsRollParam, false);
        if (trail != null) trail.SetEmitting(false);
        if (TPSCameraController.Instance != null)
            TPSCameraController.Instance.SetDashPull(false);
    }
}
