/**===========================================================================

 File   : 空ステート [StatePlayerNull.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 何もしないステート。各スロットの初期値・終了後の戻り先として使用。
 ===========================================================================**/

public class StatePlayerNull : IPlayerState {
    public virtual void Enter() { }
    public virtual void Update() { }
    public virtual void Exit() { }
}
