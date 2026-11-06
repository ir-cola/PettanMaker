using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RollingDustEmitter : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private ParticleSystem dustParticle;
    [SerializeField] private Transform dustFollowTarget;

    [Header("地面判定")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField] private float raycastDistance = 2.0f;
    [SerializeField] private float groundOffset = 0.03f;

    [Header("移動判定")]
    [SerializeField] private float minMoveSpeed = 0.2f;
    [SerializeField] private float minAngularSpeed = 0.5f;

    [Header("発生量")]
    [SerializeField] private int minEmitCount = 1;
    [SerializeField] private int maxEmitCount = 6;
    [SerializeField] private float emitInterval = 0.03f;
    [SerializeField] private float speedForMaxEmission = 6.0f;

    [Header("位置調整")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    private Rigidbody rb;
    private float emitTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (dustFollowTarget == null)
        {
            dustFollowTarget = transform;
        }

        if (dustParticle != null)
        {
            var emission = dustParticle.emission;
            emission.rateOverTime = 0f;

            var main = dustParticle.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }
    }

    private void Update()
    {
        if (dustParticle == null || rb == null)
        {
            return;
        }

        bool isGrounded = TryGetGroundPoint(out Vector3 groundPoint, out Vector3 groundNormal);
        bool isMoving = IsObjectMoving();

        if (!isGrounded || !isMoving)
        {
            return;
        }

        emitTimer -= Time.deltaTime;

        if (emitTimer > 0f)
        {
            return;
        }

        emitTimer = emitInterval;

        Vector3 emitPosition = groundPoint + groundNormal * groundOffset + positionOffset;

        MoveParticleSystemTo(emitPosition, groundNormal);

        int emitCount = GetEmitCountBySpeed();

        dustParticle.Emit(emitCount);
    }

    private bool TryGetGroundPoint(out Vector3 point, out Vector3 normal)
    {
        Vector3 rayOrigin = dustFollowTarget.position + Vector3.up * 0.2f;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                raycastDistance,
                groundLayer,
                QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        point = Vector3.zero;
        normal = Vector3.up;
        return false;
    }

    private bool IsObjectMoving()
    {
        float moveSpeed = rb.linearVelocity.magnitude;
        float angularSpeed = rb.angularVelocity.magnitude;

        return moveSpeed >= minMoveSpeed || angularSpeed >= minAngularSpeed;
    }

    private int GetEmitCountBySpeed()
    {
        float speed = Mathf.Max(rb.linearVelocity.magnitude, rb.angularVelocity.magnitude);
        float t = Mathf.Clamp01(speed / speedForMaxEmission);

        return Mathf.RoundToInt(Mathf.Lerp(minEmitCount, maxEmitCount, t));
    }

    private void MoveParticleSystemTo(Vector3 position, Vector3 groundNormal)
    {
        dustParticle.transform.position = position;

        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, groundNormal);
        dustParticle.transform.rotation = rotation;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform target = dustFollowTarget != null ? dustFollowTarget : transform;
        Vector3 rayOrigin = target.position + Vector3.up * 0.2f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.down * raycastDistance);
    }
#endif
}