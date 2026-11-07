using UnityEngine;

/// <summary>
/// エネミーごとの Aim レティクル (ロックオンビルボード) 表示位置。
/// エネミー本体 (IEnemy を持つ GameObject) かその子に付け、Point に位置基準の
/// Transform を割り当てる。未割り当てなら自身の Transform を基準にする。
/// PlayerLockOn がロック時にこのコンポーネントを探し、あれば Point の位置へ
/// レティクルを表示する (無ければ従来どおりコライダーのバウンズ中心)。
/// </summary>
public class EnemyAimAnchor : MonoBehaviour {
    [Tooltip("Aim レティクルを表示する位置の基準。空の子オブジェクトを敵の狙いたい高さに置いて割り当てる。未設定ならこのコンポーネント自身の位置。")]
    [SerializeField] private Transform point;

    /// <summary>レティクルを表示するワールド座標 (毎フレーム評価。動く骨に付けても追従する)。</summary>
    public Vector3 Position => point != null ? point.position : transform.position;

    private void OnDrawGizmosSelected() {
        Gizmos.color = new Color(1f, 0.3f, 0.45f, 0.9f);
        Gizmos.DrawWireSphere(Position, 0.25f);
    }
}
