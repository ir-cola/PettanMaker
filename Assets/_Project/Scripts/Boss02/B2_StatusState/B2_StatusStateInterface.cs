/**===========================================================================

 File   : [B2_StatusStateInterface.cs]
 Desc   : Boss02のステータス状態のインターフェースクラス.Idleも現状使い道がないがここに実装
 Author : Yokoyama Haruki
 Date   : 2026/05/14
 -----------------------------------------------------------------------------
 
 ===========================================================================**/
using UnityEngine;

namespace Boss02
{
    public abstract class IStatusState
    {
        protected B2_Boss boss;
        public IStatusState(B2_Boss boss)
        {
            this.boss = boss;
        }

        public virtual void Enter() { }
        public virtual void Update() { }
        public virtual void Exit() { }
    }

    public class B2_IdleState : IStatusState
    {

        public B2_IdleState(B2_Boss boss) : base(boss) { }

        public override void Enter()
        {
        }
        public override void Update()
        {
            // Idle behavior
        }
        public override void Exit()
        {
        }
    }
}