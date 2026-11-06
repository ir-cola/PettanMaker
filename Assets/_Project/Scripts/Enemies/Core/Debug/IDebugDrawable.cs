/// <summary>
/// EnemyAction に実装することで、攻撃判定などをデバッグ表示できるインターフェース。
/// EnemyDebugManager が OnDrawGizmos / OnDrawGizmosSelected でこれを呼ぶ。
/// 新しい攻撃アクションを追加したときに実装するだけでデバッグ可視化が有効になる。
/// </summary>
public interface IDebugDrawable
{
    void DrawDebugGizmos();
}
