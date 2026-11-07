/*=============================================================================

 File   : [B2_StateMachine.cs]
 Desc   : 
 ------------------------------------------------------------------------------

 Date   : 2026/05/13
 Author : Yokoyama Haruki
 
===============================================================================*/

using UnityEngine;

namespace Boss02
{


    public class B2_StateMachine
    {
        private B2_Boss boss;
        private IStatusState currentState; 

        public B2_IdleState idleState { get; private set; }
        public B2_MovieState movieState { get; private set; }
        public B2_OnBaseState onBaseState { get; private set; }
        public B2_OffBaseState offBaseState { get; private set; }
        public B2_StunState stunState { get; private set; }
        public B2_DownedState downedState { get; private set; }

        public B2_StateMachine(B2_Boss boss)
        {
            this.boss = boss;
            idleState = new B2_IdleState(boss);
            movieState = new B2_MovieState(boss);
            onBaseState = new B2_OnBaseState(boss);
            offBaseState = new B2_OffBaseState(boss);
            stunState = new B2_StunState(boss);
            downedState = new B2_DownedState(boss);

            currentState = onBaseState;
            currentState.Enter();
        }

        public void Update()
        {
            currentState?.Update();
        }

        public void ChangeState(IStatusState newState)
        {
            currentState?.Exit();
            currentState = newState;
            currentState?.Enter();
        }

        public bool IsInState<T>() where T : IStatusState
        {
            return currentState is T;
        }
    }
}