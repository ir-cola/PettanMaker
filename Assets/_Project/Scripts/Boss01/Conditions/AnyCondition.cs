using System.Linq;

// OR 合成。内側の条件のどれか1つが true なら true を返す
public class AnyCondition : Condition
{
    private readonly Condition[] inner;

    public AnyCondition(params Condition[] conditions)
    {
        inner = conditions;
    }

    public override bool Evaluate(IBlackboard bb) => inner.Any(c => c.Evaluate(bb));
}
