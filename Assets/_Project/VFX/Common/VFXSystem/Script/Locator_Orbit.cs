using UnityEngine;

public class OrbitLocator : MonoBehaviour
{
    public enum OrbitPlane
    {
        XZ,
        XY,
        YZ
    }

    [Header("中心")]
    [SerializeField] private Transform center;

    [Header("軌道")]
    [SerializeField] private OrbitPlane orbitPlane = OrbitPlane.XZ;
    [SerializeField] private float radiusA = 0.7f;
    [SerializeField] private float radiusB = 0.25f;
    [SerializeField] private float heightOffset = 0.0f;

    [Tooltip("1秒あたりの角速度。大きいほど速く回る")]
    [SerializeField] private float speed = 2.0f;

    [Header("軌道の傾き")]
    [SerializeField] private Vector3 eulerTilt = Vector3.zero;

    [Header("開始角度")]
    [Range(0f, 360f)]
    [SerializeField] private float startAngle = 0f;

    [Header("収束設定")]
    [Tooltip("BeginAttract() が呼ばれてから、何周待って収束を始めるか")]
    [SerializeField] private float loopsBeforeAttractAfterTrigger = 0.0f;

    [Tooltip("収束に何周使うか。1なら1周かけて中心へ寄る")]
    [SerializeField] private float attractLoopDuration = 1.0f;

    [Tooltip("数値が大きいほど、後半まで外側に残る")]
    [SerializeField] private float attractPower = 1.6f;

    [Header("収束後")]
    [Tooltip("収束完了後、完全にCenter位置へ固定する")]
    [SerializeField] private bool lockToCenterAfterConverged = true;

    public bool IsAttracting { get; private set; }
    public bool IsConverged { get; private set; }

    public Transform Center
    {
        get => center;
        set => center = value;
    }

    private float angleRad;
    private float startAngleRad;
    private float currentLoop;
    private float triggerLoop;
    private float radiusRate = 1f;

    private void OnEnable()
    {
        ResetOrbit();
    }

    private void Update()
    {
        if (center == null)
        {
            return;
        }

        UpdateOrbit();
    }

    private void UpdateOrbit()
    {
        angleRad += Time.deltaTime * speed;

        float traveledRad = angleRad - startAngleRad;
        currentLoop = traveledRad / (Mathf.PI * 2f);

        radiusRate = CalculateRadiusRate();

        Vector3 localPos = CalculateLocalPosition(angleRad, radiusRate);

        Quaternion tiltRot = Quaternion.Euler(eulerTilt);
        transform.position = center.position + tiltRot * localPos;
    }

    private float CalculateRadiusRate()
    {
        if (!IsAttracting)
        {
            return 1f;
        }

        float attractStartLoop = triggerLoop + loopsBeforeAttractAfterTrigger;

        float attractT = Mathf.Clamp01(
            (currentLoop - attractStartLoop) / Mathf.Max(0.0001f, attractLoopDuration)
        );

        float smoothT = SmootherStep(attractT);
        smoothT = Mathf.Pow(smoothT, attractPower);

        float result = Mathf.Lerp(1f, 0f, smoothT);

        if (attractT >= 1f)
        {
            IsConverged = true;

            if (lockToCenterAfterConverged)
            {
                result = 0f;
            }
        }

        return result;
    }

    private Vector3 CalculateLocalPosition(float angle, float rate)
    {
        float a = Mathf.Cos(angle) * radiusA * rate;
        float b = Mathf.Sin(angle) * radiusB * rate;
        float h = heightOffset * rate;

        switch (orbitPlane)
        {
            case OrbitPlane.XY:
                return new Vector3(a, b, h);

            case OrbitPlane.YZ:
                return new Vector3(h, a, b);

            case OrbitPlane.XZ:
            default:
                return new Vector3(a, h, b);
        }
    }

    public void BeginAttract()
    {
        if (IsAttracting)
        {
            return;
        }

        IsAttracting = true;
        IsConverged = false;
        triggerLoop = currentLoop;
    }

    public void ResetOrbit()
    {
        startAngleRad = startAngle * Mathf.Deg2Rad;
        angleRad = startAngleRad;

        currentLoop = 0f;
        triggerLoop = 0f;
        radiusRate = 1f;

        IsAttracting = false;
        IsConverged = false;

        if (center != null)
        {
            Vector3 localPos = CalculateLocalPosition(angleRad, radiusRate);
            Quaternion tiltRot = Quaternion.Euler(eulerTilt);
            transform.position = center.position + tiltRot * localPos;
        }
    }

    public void ForceConverge()
    {
        IsAttracting = true;
        IsConverged = true;
        radiusRate = 0f;

        if (center != null)
        {
            transform.position = center.position;
        }
    }

    public void SetStartAngle(float angle)
    {
        startAngle = angle;
        ResetOrbit();
    }

    private float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        radiusA = Mathf.Max(0f, radiusA);
        radiusB = Mathf.Max(0f, radiusB);
        attractLoopDuration = Mathf.Max(0.0001f, attractLoopDuration);
        attractPower = Mathf.Max(0.01f, attractPower);
    }
#endif
}