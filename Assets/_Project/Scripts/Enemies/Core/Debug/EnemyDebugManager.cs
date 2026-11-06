using UnityEngine;

[RequireComponent(typeof(EnemyBase))]
public class EnemyDebugManager : MonoBehaviour
{
    private EnemyBase           ai;
    private EnemyHitboxWires    hitboxWires;
    private EnemyAttackHitbox[] hitboxes;

    private void Awake()
    {
        ai = GetComponent<EnemyBase>();
        var hwGO = new GameObject("DebugHitboxWires");
        hwGO.transform.SetParent(transform, false);
        hitboxWires = hwGO.AddComponent<EnemyHitboxWires>();
    }

    private void Start()
    {
        hitboxes = GetComponentsInChildren<EnemyAttackHitbox>(true);
        hitboxWires.Setup(hitboxes);

        var s = ai.Settings;
        if (s != null && s.DebugEnabled && s.ShowHUD)
            DebugOverlay.Register(GetInstanceID(), $"{gameObject.name} [Enemy]", BuildHUD);
    }

    private void Update()
    {
        var s = ai?.Settings;
        if (s == null || !s.DebugEnabled) { hitboxWires.gameObject.SetActive(false); return; }
        hitboxWires.gameObject.SetActive(s.ShowHitboxes);
        if (s.ShowHitboxes) hitboxWires.Tick();
    }

    private void OnDestroy() => DebugOverlay.Unregister(GetInstanceID());

    private string BuildHUD()
    {
        if (ai == null) return "-";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"State  {ai.CurrentStateName}");
        sb.AppendLine($"Action {ai.ActionController.CurrentActionName}");
        string motion = ai.DebugMotionName;
        if (motion != "-") sb.AppendLine($"Motion {motion}");
        float dist = ai.PlayerDistance;
        if (dist >= 0f) sb.Append($"Dist   {dist:F1} m");
        return sb.ToString().TrimEnd();
    }

    private void OnDrawGizmos()
    {
        if (ai == null) return;
        var s = ai.Settings;
        if (s == null || !s.DebugEnabled || !s.ShowHitboxes) return;
        (ai.ActionController?.CurrentAction as IDebugDrawable)?.DrawDebugGizmos();
        DrawHitboxGizmos();
    }

    private void DrawHitboxGizmos()
    {
        if (hitboxes == null) return;
        foreach (var hb in hitboxes)
        {
            var col = hb?.HitboxCollider;
            if (col == null) continue;
            bool active = col.enabled && col.gameObject.activeInHierarchy;
            Gizmos.color  = active ? new Color(1f, 0f, 0f, 0.4f) : new Color(0.5f, 0.5f, 0.5f, 0.2f);
            Gizmos.matrix = col.transform.localToWorldMatrix;
            if      (col is BoxCollider box)     Gizmos.DrawWireCube(box.center, box.size);
            else if (col is SphereCollider sph)  Gizmos.DrawWireSphere(sph.center, sph.radius);
            else if (col is CapsuleCollider cap) Gizmos.DrawWireSphere(cap.center, cap.radius);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
