/// <summary>
/// 死亡済みの敵に攻撃が当たったときの通知を受け取るインターフェース。
/// Boss など死後に追加演出が必要なオブジェクトが実装する。
/// </summary>
public interface IFinishHitReceiver
{
    void OnFinishHit();
}
