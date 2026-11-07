// 現在のフェーズが Phase2 か
public class Phase2Condition : Condition
{
    public override bool Evaluate(IBlackboard bb) => bb.CurrentPhase >= 2;
}
