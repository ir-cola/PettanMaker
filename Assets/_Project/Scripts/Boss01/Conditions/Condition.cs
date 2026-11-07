/// <summary>
/// 行動グループの選択条件の抽象基底クラス。
/// enableConditions に並べて AND 評価、OR が必要なら AnyCondition で束ねる。
/// </summary>
public abstract class Condition
{
    public abstract bool Evaluate(IBlackboard bb);
}
