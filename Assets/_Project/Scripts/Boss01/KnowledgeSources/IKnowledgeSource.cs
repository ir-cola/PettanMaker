/// <summary>
/// ブラックボードへの書き込みを担うナレッジソースのインターフェース。
/// B1_Boss が評価順にリストで管理し、毎フレーム Evaluate を呼ぶ。
/// </summary>
public interface IKnowledgeSource
{
    // 毎フレーム呼ばれる。BBへの書き込みはここで行う
    void Evaluate(B1_Blackboard bb);
}
