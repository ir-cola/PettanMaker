// 激昂状態か（Phase2 移行時に true）
public class EnragedCondition : Condition
{
    public override bool Evaluate(IBlackboard bb) => bb.IsEnraged;
}
