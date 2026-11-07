// プレイヤーが指定距離以内にいるか
public class PlayerNearCondition : Condition
{
    private readonly float threshold;

    public PlayerNearCondition(float threshold)
    {
        this.threshold = threshold;
    }

    public override bool Evaluate(IBlackboard bb) => bb.PlayerDistance <= threshold;
}
