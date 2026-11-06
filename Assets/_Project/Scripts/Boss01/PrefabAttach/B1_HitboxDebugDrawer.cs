using UnityEngine;

/// <summary>
/// Boss01の有効な攻撃HitboxをGame/Sceneビューへ描画する。
/// B1_DebugManagerが生成・更新を管理する。
/// </summary>
public sealed class B1_HitboxDebugDrawer
{
    private static readonly Color FillColor    = new(1f, 0f, 0f, 0.3f);
    private static readonly Color WireColor    = Color.red;
    private static readonly Color InactiveColor = new(0.5f, 0.5f, 0.5f, 0.4f);

    public void DrawRuntime(B1_AttackHitbox[] hitboxes)
    {
        if (hitboxes == null) return;

        foreach (B1_AttackHitbox hitbox in hitboxes)
        {
            Collider col = hitbox != null ? hitbox.HitboxCollider : null;
            if (col == null) continue;

            bool active = col.enabled && col.gameObject.activeInHierarchy;
            Color color = active ? WireColor : InactiveColor;

            if (col is BoxCollider box)
                DrawRuntimeBox(box, color);
            else if (col is SphereCollider sphere)
                DrawRuntimeSphere(sphere, color);
            else if (col is CapsuleCollider capsule)
                DrawRuntimeSphere(capsule.transform, capsule.center, capsule.radius, color);
        }
    }

    public void DrawGizmos(B1_AttackHitbox[] hitboxes)
    {
        if (hitboxes == null) return;

        foreach (B1_AttackHitbox hitbox in hitboxes)
        {
            Collider col = hitbox != null ? hitbox.HitboxCollider : null;
            if (col == null) continue;

            bool active = col.enabled && col.gameObject.activeInHierarchy;
            Color wire = active ? WireColor : InactiveColor;
            Color fill = active ? FillColor : new Color(0.5f, 0.5f, 0.5f, 0.15f);

            Gizmos.matrix = col.transform.localToWorldMatrix;
            Gizmos.color = fill;
            if (col is BoxCollider box)
                Gizmos.DrawCube(box.center, box.size);
            else if (col is SphereCollider sphere)
                Gizmos.DrawSphere(sphere.center, sphere.radius);
            else if (col is CapsuleCollider capsule)
                Gizmos.DrawSphere(capsule.center, capsule.radius);

            Gizmos.color = wire;
            if (col is BoxCollider wireBox)
                Gizmos.DrawWireCube(wireBox.center, wireBox.size);
            else if (col is SphereCollider wireSphere)
                Gizmos.DrawWireSphere(wireSphere.center, wireSphere.radius);
            else if (col is CapsuleCollider wireCapsule)
                Gizmos.DrawWireSphere(wireCapsule.center, wireCapsule.radius);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }

    private static void DrawRuntimeBox(BoxCollider box, Color color)
    {
        Transform tr = box.transform;
        Vector3 h = box.size * 0.5f;
        Vector3[] p =
        {
            tr.TransformPoint(box.center + new Vector3(-h.x, -h.y, -h.z)),
            tr.TransformPoint(box.center + new Vector3( h.x, -h.y, -h.z)),
            tr.TransformPoint(box.center + new Vector3( h.x, -h.y,  h.z)),
            tr.TransformPoint(box.center + new Vector3(-h.x, -h.y,  h.z)),
            tr.TransformPoint(box.center + new Vector3(-h.x,  h.y, -h.z)),
            tr.TransformPoint(box.center + new Vector3( h.x,  h.y, -h.z)),
            tr.TransformPoint(box.center + new Vector3( h.x,  h.y,  h.z)),
            tr.TransformPoint(box.center + new Vector3(-h.x,  h.y,  h.z)),
        };

        Line(p[0], p[1], color); Line(p[1], p[2], color); Line(p[2], p[3], color); Line(p[3], p[0], color);
        Line(p[4], p[5], color); Line(p[5], p[6], color); Line(p[6], p[7], color); Line(p[7], p[4], color);
        Line(p[0], p[4], color); Line(p[1], p[5], color); Line(p[2], p[6], color); Line(p[3], p[7], color);
    }

    private static void DrawRuntimeSphere(SphereCollider sphere, Color color)
        => DrawRuntimeSphere(sphere.transform, sphere.center, sphere.radius, color);

    private static void DrawRuntimeSphere(Transform tr, Vector3 center, float radius, Color color)
    {
        const int segments = 24;
        for (int axis = 0; axis < 3; axis++)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Line(
                    tr.TransformPoint(center + CirclePoint(axis, a0) * radius),
                    tr.TransformPoint(center + CirclePoint(axis, a1) * radius),
                    color);
            }
        }
    }

    private static Vector3 CirclePoint(int axis, float angle) => axis switch
    {
        0 => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f),
        1 => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
        _ => new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)),
    };

    private static void Line(Vector3 from, Vector3 to, Color color)
        => B1_RuntimeGizmos.DrawLine(from, to, color);
}
