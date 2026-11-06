/// <summary>
/// HPをブラックボードに書き込む。
/// B1_PhaseKS が依存するため必ず先に評価すること。
/// </summary>
public class B1_HpKS : IKnowledgeSource
{
    private readonly B1_Health health;

    public B1_HpKS(B1_Health health)
    {
        this.health = health;
    }

    public void Evaluate(B1_Blackboard bb)
    {
        bb.SetHpRatio(health.HpRatio);
    }
}
