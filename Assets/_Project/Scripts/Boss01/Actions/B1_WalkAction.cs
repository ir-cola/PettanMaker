/// <summary>
/// ボスが歩いてプレイヤーに近づくアクション。
/// 攻撃射程内に入るか最大時間が来たら終了する。
/// </summary>
public class B1_WalkAction : B1_Action
{
    private float timer;

    public B1_WalkAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        timer = boss.Settings.Walk.MaxDuration;
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Walk);
    }

    public override void Update()
    {
        timer -= UnityEngine.Time.deltaTime;
        if (timer <= 0f || bb.PlayerDistance <= boss.Settings.Walk.StopRange)
            IsFinished = true;
    }
}
