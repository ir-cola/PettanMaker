/**===========================================================================

 File   : プレイヤーステートマシン [PlayerStateMachine.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 2 スロット (Locomotion / Action) を並行に保持・更新するステートマシン。
   - Locomotion : 接地/空中 (StateGrounded ↔ StateAirborne)
   - Action     : 攻撃/回避など一時アクション (StateAttack / StateRoll / StatePlayerNull)
 ===========================================================================**/

using UnityEngine;

public class PlayerStateMachine {

    private IPlayerState currentLocomotionState;
    private IPlayerState currentActionState;

    public PlayerStateMachine() { }

    public void ChangeLocomotionState(IPlayerState newState) {
        currentLocomotionState?.Exit();
        currentLocomotionState = newState;
        currentLocomotionState?.Enter();
    }

    public void ChangeActionState(IPlayerState newState) {
        currentActionState?.Exit();
        currentActionState = newState;
        currentActionState?.Enter();
    }

    public void Update() {
        currentActionState?.Update();
        currentLocomotionState?.Update();
    }

    public bool IsInLocomotionState<T>() where T : IPlayerState => currentLocomotionState is T;
    public bool IsInActionState<T>()     where T : IPlayerState => currentActionState is T;
}
