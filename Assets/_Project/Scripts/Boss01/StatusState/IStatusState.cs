// ボスの大状態（Phase・Stun・Roar・Death）のインターフェース
public interface IStatusState
{
    string StateName { get; }
    void Enter();
    void Update();
    void Exit();
}
