/// <summary>
/// ボスが走ってプレイヤーに近づくアクション。
/// 攻撃射程内に入るか最大時間が来たら終了する。
/// </summary>
public class B1_RunAction : B1_Action
{
    private float timer;

    public B1_RunAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        timer = boss.Settings.Run.MaxDuration;
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Run);
    }

    public override void Update()
    {
        timer -= UnityEngine.Time.deltaTime;
        if (timer <= 0f || bb.PlayerDistance <= boss.Settings.Run.StopRange)
            IsFinished = true;
    }
}
