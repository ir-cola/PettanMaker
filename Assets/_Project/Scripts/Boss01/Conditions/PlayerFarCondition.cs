// プレイヤーが指定距離より遠くにいるか
public class PlayerFarCondition : Condition
{
    private readonly float threshold;

    public PlayerFarCondition(float threshold)
    {
        this.threshold = threshold;
    }

    public override bool Evaluate(IBlackboard bb) => bb.PlayerDistance > threshold;
}
