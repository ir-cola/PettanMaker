/**===========================================================================

 File   : [B2_DownedState.cs]
 Desc   : HPを削り切った後の「無限ダウン」状態。
          倒れたまま何もせず、とどめ(B2_FinishingBlow のシーリングスタンプ
          ムービー)が刺されるのを待ち続ける。

          ■ スタン(B2_StunState)との違い
            スタンは時間で明けて土台なし状態へ移行するが、こちらは明けない。
            Update で何もしないのが仕様。

          ■ ここで消さない理由
            プレイヤーが近づいて攻撃するまでボスは画面に残っている必要がある。
            消すのはとどめムービー側(B2_FinishingBlow)。

 Author : akatu
 Date   : 2026/07/17
 -----------------------------------------------------------------------------

 ===========================================================================**/
using UnityEngine;

namespace Boss02
{
    public class B2_DownedState : IStatusState
    {
        public B2_DownedState(B2_Boss boss) : base(boss)
        {
        }

        public override void Enter()
        {
            // 進行中の攻撃と予約を捨てる。
            // (B2_Boss.Update 側でも ActionController は止めるが、
            //  Exit を走らせて後始末させるためここでも打ち切る)
            boss.ActionController.RequestNextAction();
            boss.ActionController.ClearActionQueue();

            // 倒れる。スタンと同じダウンアニメを使う(専用の死亡アニメは用意されていない)
            boss.Animator.SetTrigger(B2_Animator.ToDown);
        }

        public override void Update()
        {
            // 何もしない。とどめが来るまで倒れ続ける
        }

        public override void Exit()
        {
        }
    }
}
