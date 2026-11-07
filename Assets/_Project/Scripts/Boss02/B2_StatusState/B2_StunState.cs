/**===========================================================================

 File   : [B2_StunState.cs]
 Desc   : 気絶状態のステータス状態クラス。
          土台が壊れると B2_Boss.BreakBaseAndStun() 経由でこの状態に入る。
          入口は「HPがBaseHealthRatioを下回る(B2_Health)」と
          「シャンデリア着弾(B2_Chandelier)」の2つで、どちらでも同じ流れ。
          進行中のアクションを打ち切ってダウンアニメへ移り、
          B2_Settings.StunDuration 経過したら土台なし状態へ移行する。
          スタン中に行動しないよう、B2_Boss.Update 側で
          ActionController.Update() を止めている。
 Author : Yokoyama Haruki
 Date   : 2026/05/14
 -----------------------------------------------------------------------------

 ===========================================================================**/
using UnityEngine;

namespace Boss02
{
    public class B2_StunState : IStatusState
    {
        private float elapsedTime;
        private float duration;

        public B2_StunState(B2_Boss boss) : base(boss)
        {
        }

        /// <summary>
        /// このスタンの継続時間を指定する。ChangeState(stunState) の直前に呼ぶ。
        /// (B2_Boss.Stun が必ず呼ぶので、外から直接状態遷移させないこと)
        /// 土台破壊時は大幅スタン、破壊後のシャンデリア直撃はその半分、と呼び分ける。
        /// </summary>
        public void SetDuration(float value) => duration = Mathf.Max(0f, value);

        public override void Enter()
        {
            elapsedTime = 0f;
            // 指定漏れの保険。B2_Boss.Stun 経由なら必ず入っている
            if (duration <= 0f) duration = boss.Settings.StunDuration;

            // 進行中の行動と予約を捨てる(スタン明けは土台なしの行動が抽選される)
            boss.ActionController.RequestNextAction();
            boss.ActionController.ClearActionQueue();

            boss.Animator.SetTrigger(B2_Animator.ToDown);
        }

        public override void Update()
        {
            elapsedTime += Time.deltaTime;
            if (elapsedTime < duration) return;

            // スタンが解けたら土台なし状態へ(土台破壊後の再スタンも同じ復帰先)
            boss.StateMachine.ChangeState(boss.StateMachine.offBaseState);
        }

        public override void Exit()
        {
        }
    }
}
