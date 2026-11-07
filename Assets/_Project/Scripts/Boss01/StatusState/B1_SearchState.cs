/// <summary>
/// Player死亡後の非戦闘状態。攻撃を中断し、周囲を歩いて探索し続ける。
/// </summary>
public sealed class B1_SearchState : IStatusState
{
    private readonly B1_Boss boss;
    private readonly B1_StateMachine stateMachine;

    public B1_SearchState(B1_Boss boss, B1_StateMachine stateMachine)
    {
        this.boss = boss;
        this.stateMachine = stateMachine;
    }

    public string StateName => "Search";

    public void Enter()
    {
        boss.ActionController.SetAction(null);
        boss.EnableHitbox(false);
        boss.EnableTailHitbox(false);
        boss.AnimatorBridge.ResumeAnimator();
        boss.AnimatorBridge.PlayLocomotion();

        // TailAttackの途中でPlayerを倒すとルートに前後の傾きが残るため、
        // 現在の向き(Yaw)だけ維持して直立へ戻す。
        float yaw = boss.transform.eulerAngles.y;
        boss.transform.rotation = UnityEngine.Quaternion.Euler(0f, yaw, 0f);

        boss.Locomotion.FacingLocked = false;
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Search);
    }

    public void Update()
    {
        if (boss.Health != null && boss.Health.IsDead)
            stateMachine.ChangeState(stateMachine.DeathState);
    }

    public void Exit()
    {
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
    }
}
