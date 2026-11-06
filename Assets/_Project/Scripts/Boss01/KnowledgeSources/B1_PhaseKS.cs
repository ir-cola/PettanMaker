/// <summary>
/// HpRatio を元にフェーズと激昂状態を書き込む。
/// B1_HpKS より後に評価すること（HpRatio への依存あり）。
/// </summary>
public class B1_PhaseKS : IKnowledgeSource
{
    private const float Phase2Threshold = 0.5f; // HP50%以下でPhase2

    public void Evaluate(B1_Blackboard bb)
    {
        int  phase     = bb.HpRatio <= Phase2Threshold ? 2 : 1;
        bool isEnraged = phase == 2;
        bb.SetPhase(phase, isEnraged);
    }
}
