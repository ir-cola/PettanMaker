/**===========================================================================

 File   : [B2_MovieState.cs]
 Desc   : ムービー再生中のステータス状態クラス
 Author : Yokoyama Haruki
 Date   : 2026/05/14
 -----------------------------------------------------------------------------
 
 ===========================================================================**/
using UnityEngine;

namespace Boss02
{
    public class B2_MovieState : IStatusState
    {
        public B2_MovieState(B2_Boss boss) : base(boss) { }

        public override void Enter()
        {

        }

        public override void Update()
        {
        }

        public override void Exit()
        {

        }
    }
}