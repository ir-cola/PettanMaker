/// <summary>
/// ボスエリア進入前の待機状態。
/// 外部から EnterCombat() が呼ばれると Phase1State へ遷移する。
/// </summary>
public class B1_IdleState : IStatusState
{
    private readonly B1_Boss boss;

    public B1_IdleState(B1_Boss boss)
    {
        this.boss = boss;
    }

    public string StateName => "Idle";

    public void Enter()
    {
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
    }

    public void Update() { }

    public void Exit()
    {
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Chase);
    }
}
