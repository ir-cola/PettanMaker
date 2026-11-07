using System.Collections.Generic;

/// <summary>
/// B1_AIController.OnActionGroupSelected を購読して行動履歴を管理する。
/// 直前グループと連続回数に加え、直近 HistorySize 件のグループ列（新しい順）を保持し、
/// AIController の重み補正（同じ攻撃の連続選択を抑制）に使わせる。
/// Evaluate では BB への書き込みのみ行う（蓄積はイベント駆動）。
/// </summary>
public class B1_HistoryKS : IKnowledgeSource
{
    // 直近何件のグループまで履歴補正の対象にするか。
    private const int HistorySize = 3;

    private ActionGroupType lastGroup = ActionGroupType.None;
    private int             sameCount = 0;
    private readonly List<ActionGroupType> recent = new(); // 0 番目 = 直前（新しい順）

    public B1_HistoryKS(B1_AIController aiController)
    {
        aiController.OnActionGroupSelected += OnSelected;
    }

    private void OnSelected(ActionGroupType type)
    {
        if (type == lastGroup)
            sameCount++;
        else
        {
            lastGroup = type;
            sameCount = 1;
        }

        recent.Insert(0, type);
        if (recent.Count > HistorySize)
            recent.RemoveAt(recent.Count - 1);
    }

    public void Evaluate(B1_Blackboard bb)
    {
        bb.SetActionHistory(lastGroup, sameCount, recent.ToArray());
    }
}
