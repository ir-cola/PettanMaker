using UnityEngine;

/**===========================================================================

 File   : 草踏み倒しエミッター [GrassFootprintEmitter.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 キャラクターなどにアタッチして、足元の草を踏み倒すコンポーネント。
 一定間隔で真下へレイキャストを飛ばし、ヒットした GrassRenderer に曲げを伝える。
 ===========================================================================**/
[DisallowMultipleComponent]
public sealed class GrassFootprintEmitter : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField, Min(0.05f)] float radius = 0.75f;          // 草を踏み倒す半径
    [SerializeField, Min(0.01f)] float interval = 0.08f;        // 踏み込み判定の実行間隔（秒）
    [SerializeField, Min(0.001f)] float minMoveDistance = 0.02f; // これ未満の移動では方向を更新しない

    [Header("Ground Check")]
    [SerializeField, Min(0.01f)] float raycastHeight = 1.5f;    // レイの発射高さ（自身のY座標からの上方オフセット）
    [SerializeField, Min(0.01f)] float raycastDistance = 3f;    // レイの最大飛距離
    [SerializeField] QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    float _nextEmitTime;
    Vector3 _lastPosition;
    bool _hasLastPosition;

    void Reset()
    {
        groundLayer = LayerMask.GetMask("Ground");
    }

    void OnEnable()
    {
        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");

        _lastPosition = transform.position;
        _hasLastPosition = true;
    }

    void OnValidate()
    {
        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");
    }

    void LateUpdate()
    {
        // interval 秒ごとに1回だけ実行
        if (!Application.isPlaying || Time.time < _nextEmitTime)
            return;

        _nextEmitTime = Time.time + interval;
        TryBendGrass();
    }

    // 足元の草を踏み倒す。踏み倒せた場合 true を返す。
    public bool TryBendGrass()
    {
        Vector3 moveDirection = GetMoveDirection();
        Vector3 origin = transform.position + Vector3.up * raycastHeight;
        int mask = groundLayer.value != 0 ? groundLayer.value : LayerMask.GetMask("Ground");
        float distance = raycastHeight + raycastDistance;

        // まず Ground レイヤーのみを対象にレイキャスト（高速パス）
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, mask, triggerInteraction))
        {
            GrassRenderer grass = hit.collider.GetComponentInParent<GrassRenderer>();
            if (grass != null)
            {
                grass.BendGrass(hit.point, radius, moveDirection);
                return true;
            }
        }

        // Ground レイヤー指定で草が見つからなかった場合、全レイヤーを対象に再探索
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, ~0, triggerInteraction);
        float nearestDistance = float.PositiveInfinity;
        GrassRenderer nearestGrass = null;
        Vector3 nearestPoint = default;

        for (int i = 0; i < hits.Length; i++)
        {
            GrassRenderer grass = hits[i].collider.GetComponentInParent<GrassRenderer>();
            if (grass == null || hits[i].distance >= nearestDistance)
                continue;

            nearestGrass = grass;
            nearestPoint = hits[i].point;
            nearestDistance = hits[i].distance;
        }

        if (nearestGrass == null)
            return false;

        nearestGrass.BendGrass(nearestPoint, radius, moveDirection);
        return true;
    }

    // 直前フレームからの移動方向を返す。
    // 移動量が minMoveDistance 未満の場合は transform.forward を使う。
    Vector3 GetMoveDirection()
    {
        Vector3 currentPosition = transform.position;
        Vector3 delta = currentPosition - _lastPosition;
        delta.y = 0f;

        _lastPosition = currentPosition;
        _hasLastPosition = true;

        if (_hasLastPosition && delta.sqrMagnitude >= minMoveDistance * minMoveDistance)
            return delta.normalized;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 0.2f, 0.25f);
        Gizmos.DrawSphere(transform.position, radius);
    }
}
