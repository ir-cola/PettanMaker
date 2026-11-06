using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Boss01 の TailAttack / TailRain 着地時には跳ね上がり、咆哮時にはXZ方向へ押し出される。
/// このコンポーネントを付けたオブジェクトだけが反応する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class B1_AttackBounceObject : MonoBehaviour
{
    private enum ImpactMotion { TailLanding, Roar }

    private static readonly HashSet<B1_AttackBounceObject> Instances = new();
    private static B1_AttackBounceSettings fallbackSettings;

    [SerializeField] private B1_AttackBounceSettings settings;

    private B1_AttackBounceSettings Config
    {
        get
        {
            if (settings != null) return settings;
            if (fallbackSettings == null)
            {
                fallbackSettings = ScriptableObject.CreateInstance<B1_AttackBounceSettings>();
                fallbackSettings.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallbackSettings;
        }
    }

    private Rigidbody body;
    private bool isLaunched;
    private float flightTime;
    private ImpactMotion impactMotion;
    private int lastRushToken = int.MinValue;

    private bool startIsKinematic;
    private bool startUseGravity;
    private RigidbodyConstraints startConstraints;
    private CollisionDetectionMode startCollisionDetection;
    private RigidbodyInterpolation startInterpolation;
    private Vector3 startPosition;
    private Quaternion startRotation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        Instances.Clear();
        fallbackSettings = null;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        EnsureCollider();
        RememberStartState();
    }

    private void OnEnable() => Instances.Add(this);

    private void OnDisable()
    {
        Instances.Remove(this);
        if (isLaunched) RestoreState(forceStartPose: true);
    }

    private void OnDestroy() => Instances.Remove(this);

    /// <summary>指定した着地点の水平半径内にある登録オブジェクトを跳ね上げる。</summary>
    public static void LaunchInRadius(Vector3 impactCenter, float radius)
    {
        if (radius <= 0f) return;

        float radiusSqr = radius * radius;
        foreach (B1_AttackBounceObject target in Instances)
        {
            if (target == null || !target.isActiveAndEnabled) continue;
            if (!target.Config.Enabled || !target.Config.EnableTailLanding) continue;

            Vector3 offset = target.transform.position - impactCenter;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > radiusSqr) continue;

            target.Launch(impactCenter, Mathf.Sqrt(distanceSqr) / radius);
        }
    }

    /// <summary>咆哮中心から指定した水平半径内の登録オブジェクトをXZ方向へ押し出す。</summary>
    public static void PushFromRoar(Vector3 roarCenter, float radius)
    {
        if (radius <= 0f) return;

        float radiusSqr = radius * radius;
        foreach (B1_AttackBounceObject target in Instances)
        {
            if (target == null || !target.isActiveAndEnabled) continue;
            if (!target.Config.Enabled || !target.Config.EnableRoar) continue;

            Vector3 offset = target.transform.position - roarCenter;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > radiusSqr) continue;

            target.PushHorizontal(roarCenter, Mathf.Sqrt(distanceSqr) / radius);
        }
    }

    /// <summary>Rush中のボス周辺にある登録オブジェクトを、1回のRushにつき一度だけ跳ね上げる。</summary>
    public static void LaunchFromRush(Vector3 rushCenter, float radius, int rushToken)
    {
        if (radius <= 0f) return;

        float radiusSqr = radius * radius;
        foreach (B1_AttackBounceObject target in Instances)
        {
            if (target == null || !target.isActiveAndEnabled) continue;
            if (!target.Config.Enabled || !target.Config.EnableRush) continue;
            if (target.lastRushToken == rushToken) continue;

            Vector3 offset = target.transform.position - rushCenter;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > radiusSqr) continue;

            target.lastRushToken = rushToken;
            target.LaunchBounce(
                rushCenter,
                Mathf.Sqrt(distanceSqr) / radius,
                target.Config.RushUpwardVelocity,
                target.Config.RushOutwardVelocity,
                target.Config.RushRandomTorque);
        }
    }

    private void Launch(Vector3 impactCenter, float distance01)
    {
        LaunchBounce(
            impactCenter,
            distance01,
            Config.UpwardVelocity,
            Config.OutwardVelocity,
            Config.RandomTorque);
    }

    private void LaunchBounce(
        Vector3 impactCenter,
        float distance01,
        float upVelocity,
        float horizontalVelocity,
        float torque)
    {
        if (body == null) return;

        Vector3 outward = transform.position - impactCenter;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            Vector2 random = Random.insideUnitCircle.normalized;
            outward = new Vector3(random.x, 0f, random.y);
        }
        else
        {
            outward.Normalize();
        }

        float strength = Mathf.Lerp(1f, Config.StrengthAtRadiusEdge, Mathf.Clamp01(distance01));
        PrepareBodyForImpact();
        body.linearVelocity = (Vector3.up * upVelocity + outward * horizontalVelocity) * strength;
        body.angularVelocity = Random.onUnitSphere * torque * strength;

        flightTime = 0f;
        impactMotion = ImpactMotion.TailLanding;
        isLaunched = true;
    }

    private void PushHorizontal(Vector3 roarCenter, float distance01)
    {
        if (body == null) return;

        Vector3 outward = transform.position - roarCenter;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            Vector2 random = Random.insideUnitCircle.normalized;
            outward = new Vector3(random.x, 0f, random.y);
        }
        else
        {
            outward.Normalize();
        }

        Vector2 randomOffset = Random.insideUnitCircle * Config.RoarDirectionRandomness;
        outward = new Vector3(outward.x + randomOffset.x, 0f, outward.z + randomOffset.y).normalized;
        float strength = Mathf.Lerp(1f, Config.StrengthAtRadiusEdge, Mathf.Clamp01(distance01));

        PrepareBodyForImpact();
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezePositionY
                         | RigidbodyConstraints.FreezeRotationX
                         | RigidbodyConstraints.FreezeRotationZ;
        body.linearVelocity = outward * Config.RoarHorizontalVelocity * strength;
        body.angularVelocity = Vector3.up * Random.Range(-Config.RoarYawTorque, Config.RoarYawTorque) * strength;

        flightTime = 0f;
        impactMotion = ImpactMotion.Roar;
        isLaunched = true;
    }

    private void PrepareBodyForImpact()
    {
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.WakeUp();
    }

    private void FixedUpdate()
    {
        if (!isLaunched || body == null) return;
        if (!Config.Enabled)
        {
            RestoreState(forceStartPose: true);
            return;
        }

        flightTime += Time.fixedDeltaTime;
        if (flightTime >= Config.MaximumFlightTime)
        {
            RestoreState(forceStartPose: true);
            return;
        }

        if (impactMotion == ImpactMotion.Roar)
        {
            if (flightTime >= Config.RoarMoveDuration)
                RestoreState();
            return;
        }

        if (flightTime < Config.MinimumFlightTime || body.linearVelocity.y > 0f) return;

        bool nearGround = body.IsSleeping()
                       || body.SweepTest(Vector3.down, out _, Config.GroundProbeDistance);
        if (nearGround)
            RestoreState();
    }

    private void EnsureCollider()
    {
        if (!Config.AddBoxColliderWhenMissing || GetComponent<Collider>() != null) return;

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        box.center = meshFilter.sharedMesh.bounds.center;
        box.size = meshFilter.sharedMesh.bounds.size;
    }

    private void RememberStartState()
    {
        startIsKinematic = body.isKinematic;
        startUseGravity = body.useGravity;
        startConstraints = body.constraints;
        startCollisionDetection = body.collisionDetectionMode;
        startInterpolation = body.interpolation;
        startPosition = body.position;
        startRotation = body.rotation;
    }

    private void RestoreState(bool forceStartPose = false)
    {
        if (body == null) return;

        isLaunched = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        if (Config.RestoreStartPose || forceStartPose)
        {
            body.position = startPosition;
            body.rotation = startRotation;
        }

        if (!Config.RestoreRigidbodyState) return;

        body.collisionDetectionMode = startCollisionDetection;
        body.interpolation = startInterpolation;
        body.constraints = startConstraints;
        body.useGravity = startUseGravity;
        body.isKinematic = startIsKinematic;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        Rigidbody targetBody = GetComponent<Rigidbody>();
        targetBody.isKinematic = true;
        targetBody.useGravity = false;
        GameObjectUtility.SetStaticEditorFlags(gameObject, 0);
    }

    private void OnValidate()
    {
        if (Application.isPlaying) return;

        GameObjectUtility.SetStaticEditorFlags(gameObject, 0);
        if (settings != null) return;

        string[] guids = AssetDatabase.FindAssets("t:B1_AttackBounceSettings");
        if (guids.Length > 0)
            settings = AssetDatabase.LoadAssetAtPath<B1_AttackBounceSettings>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
    }
#endif

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawRay(transform.position, Vector3.up * Config.UpwardVelocity * 0.25f);
    }
}
