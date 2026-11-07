/// <summary>
/// ボスが短時間その場で待機するアクション。
/// </summary>
public class B1_IdleAction : B1_Action
{
    private readonly float overrideDuration; // 0 以下なら Settings.Idle.Duration を使う
    private float timer;

    public B1_IdleAction(B1_Boss boss, IBlackboard bb, float duration = -1f) : base(boss, bb)
    {
        overrideDuration = duration;
    }

    public override void Enter()
    {
        timer = overrideDuration > 0f ? overrideDuration : boss.Settings.Idle.Duration;
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
    }

    public override void Update()
    {
        timer -= UnityEngine.Time.deltaTime;
        if (timer <= 0f)
            IsFinished = true;
    }
}
