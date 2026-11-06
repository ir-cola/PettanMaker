/**===========================================================================

 File   : 空中ステート [StateAirborne.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 ジャンプ・落下中のロコモーションステート。
 ===========================================================================**/

using UnityEngine;

public class StateAirborne : IPlayerState {

    protected IPlayerContext player;
    private bool jumpStartFired = false;

    public StateAirborne(IPlayerContext player) {
        this.player = player;
    }

    public virtual void Enter() {
        player.Animator.SetBool(PlayerAnimator.IsLandParam, false);
        jumpStartFired = false;
    }

    public virtual void Update() {
        // ノックバック中/回避中は移動入力を受け付けない
        // (ノックバックの押し出しは PlayerHitReaction、回避は StateRoll が固定方向で駆動)
        bool rolling = player.StateMachine.IsInActionState<StateRoll>();
        bool knockedBack = player.Health != null && player.Health.IsInKnockback;
        if (knockedBack) player.Mover.Move(Vector2.zero);
        else if (!rolling) player.Mover.Move(player.Input.Move);
        player.Mover.ApplyGravity();

        // 縦速度を Animator に公開 (宣言済み VerticalSpeed パラメータ / 将来の上下ブレンド用)
        player.Animator.SetFloat(PlayerAnimator.VerticalSpeedParam, player.Mover.VerticalVelocity);

        // 上昇中は Jump モーションを継続し、頂点を越えて降下に転じた瞬間に Fall へ遷移させる。
        // 離陸直後ではなく「降下開始」で JumpStart を送ることで上昇モーションを見せる。
        // 段差落下のように最初から降下している場合は最初のフレームで即 Fall へ移る。
        if (!jumpStartFired && player.Mover.VerticalVelocity < 0f) {
            player.Animator.SetTrigger(PlayerAnimator.JumpStartParam);
            jumpStartFired = true;
        }

        if (!rolling && !knockedBack && player.Input.IsDodgePress) {
            player.StateMachine.ChangeActionState(new StateRoll(player));
            return;
        }

        // 上昇中は着地させない (接地判定を広げてもジャンプの離陸が潰れないように)
        if (player.Mover.IsGrounded && player.Mover.VerticalVelocity <= 0.01f) {
            player.Sound?.PlayLand();
            player.StateMachine.ChangeLocomotionState(new StateGrounded(player));
        }
    }

    public virtual void Exit() { }
}
