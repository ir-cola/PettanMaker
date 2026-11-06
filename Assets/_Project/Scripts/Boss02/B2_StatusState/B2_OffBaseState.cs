/**===========================================================================

 File   : [B2_OffBaseState.cs]

 Author : Yokoyama Haruki
 Date   : 2026/05/14
 -----------------------------------------------------------------------------
 
 ===========================================================================**/
using UnityEngine;

namespace Boss02
{
    public class B2_OffBaseState : IStatusState
    {
        public B2_OffBaseState(B2_Boss boss) : base(boss)
        {
        }

        public override void Enter()
        {
            // 土台あり用オブジェクトの非アクティブ化とボスの接地。
            // シャンデリア経由(BreakBaseAndStun)で既に壊れている場合は二度目の呼び出しになるが、
            // OffBaseTransform がボスに追従するため位置補正は差分0になり、二重に落ちることはない。
            boss.BreakBase();

            // 土台無し状態に入る際に、アクションキューをクリアしておく（土台無し状態の行動が次に抽選される）
            boss.ActionController.ClearActionQueue();

            // ここが第二形態への変身の瞬間。変身SE + BGM切替
            boss.PlayPhase2Presentation();
        }

        public override void Update()
        {
        }

        public override void Exit()
        {

        }
    }
}