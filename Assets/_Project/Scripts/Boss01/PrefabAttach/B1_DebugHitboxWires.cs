using System;
using UnityEngine;

/// <summary>
/// B1_AttackHitbox のコライダーを LineRenderer でワイヤーフレーム表示する。
/// 非アクティブ=灰色、アクティブ=赤。Game / Scene 両ビューで常時見える。
/// </summary>
public class B1_DebugHitboxWires : MonoBehaviour
{
    private static readonly Color ActiveColor   = Color.red;
    private static readonly Color InactiveColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);

    private struct Entry
    {
        public B1_AttackHitbox hitbox;
        public LineRenderer[]  lines;
    }

    private Entry[]  entries;
    private Material lineMat;

    private void Awake()
    {
        lineMat = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnDestroy() => Destroy(lineMat);

    public void Setup(B1_AttackHitbox[] hitboxes)
    {
        // 再セットアップ時は古い GO を破棄
        if (entries != null)
            foreach (var e in entries)
                foreach (var lr in e.lines)
                    if (lr != null) Destroy(lr.gameObject);

        entries = new Entry[hitboxes.Length];
        for (int i = 0; i < hitboxes.Length; i++)
            entries[i] = new Entry { hitbox = hitboxes[i], lines = BuildLines(hitboxes[i]) };
    }

    public void Tick()
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (e.hitbox == null) continue;
            var col = e.hitbox.HitboxCollider;
            if (col == null) continue;
            bool active = col.enabled && col.gameObject.activeInHierarchy;
            Refresh(e.lines, col, active ? ActiveColor : InactiveColor);
        }
    }

    // ---- 生成 ----

    private LineRenderer[] BuildLines(B1_AttackHitbox hitbox)
    {
        var col = hitbox?.HitboxCollider;
        if (col is BoxCollider box)         return BuildBox(box);
        if (col is SphereCollider sph)      return BuildSphere(sph.transform, sph.center, sph.radius);
        if (col is CapsuleCollider cap)     return BuildSphere(cap.transform, cap.center, cap.radius);
        return Array.Empty<LineRenderer>();
    }

    private LineRenderer[] BuildBox(BoxCollider box)
    {
        // 底面ループ + 上面ループ + 縦4辺 = 6本
        var lrs = new LineRenderer[6];
        for (int i = 0; i < 6; i++)
        {
            lrs[i] = MakeLR(i < 2 ? 5 : 2);
        }
        return lrs;
    }

    private LineRenderer[] BuildSphere(Transform tr, Vector3 center, float radius)
    {
        const int seg = 32;
        var lrs = new LineRenderer[3]; // XY / XZ / YZ
        for (int i = 0; i < 3; i++)
            lrs[i] = MakeLR(seg + 1);
        return lrs;
    }

    private LineRenderer MakeLR(int pointCount)
    {
        var go = new GameObject("Wire");
        go.transform.SetParent(transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace      = true;
        lr.positionCount      = pointCount;
        lr.startWidth         = 0.025f;
        lr.endWidth           = 0.025f;
        lr.material           = lineMat;
        lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows     = false;
        lr.generateLightingData = false;
        return lr;
    }

    // ---- 更新 ----

    private static void Refresh(LineRenderer[] lines, Collider col, Color color)
    {
        if (lines == null || lines.Length == 0) return;
        foreach (var lr in lines)
            if (lr != null) { lr.startColor = color; lr.endColor = color; }

        if      (col is BoxCollider box)    RefreshBox(lines, box);
        else if (col is SphereCollider sph) RefreshSphere(lines, sph.transform, sph.center, sph.radius);
        else if (col is CapsuleCollider cap) RefreshSphere(lines, cap.transform, cap.center, cap.radius);
    }

    private static void RefreshBox(LineRenderer[] lr, BoxCollider box)
    {
        var tr = box.transform;
        var c  = box.center;
        var h  = box.size * 0.5f;
        var p  = new Vector3[8];
        p[0] = tr.TransformPoint(c + new Vector3(-h.x, -h.y, -h.z));
        p[1] = tr.TransformPoint(c + new Vector3( h.x, -h.y, -h.z));
        p[2] = tr.TransformPoint(c + new Vector3( h.x, -h.y,  h.z));
        p[3] = tr.TransformPoint(c + new Vector3(-h.x, -h.y,  h.z));
        p[4] = tr.TransformPoint(c + new Vector3(-h.x,  h.y, -h.z));
        p[5] = tr.TransformPoint(c + new Vector3( h.x,  h.y, -h.z));
        p[6] = tr.TransformPoint(c + new Vector3( h.x,  h.y,  h.z));
        p[7] = tr.TransformPoint(c + new Vector3(-h.x,  h.y,  h.z));

        lr[0].SetPositions(new[] { p[0], p[1], p[2], p[3], p[0] }); // 底面
        lr[1].SetPositions(new[] { p[4], p[5], p[6], p[7], p[4] }); // 上面
        lr[2].SetPositions(new[] { p[0], p[4] });
        lr[3].SetPositions(new[] { p[1], p[5] });
        lr[4].SetPositions(new[] { p[2], p[6] });
        lr[5].SetPositions(new[] { p[3], p[7] });
    }

    private static void RefreshSphere(LineRenderer[] lr, Transform tr, Vector3 center, float radius)
    {
        const int seg = 32;
        for (int axis = 0; axis < 3; axis++)
        {
            var pts = new Vector3[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                pts[i] = tr.TransformPoint(center + CirclePoint(axis, a) * radius);
            }
            lr[axis].SetPositions(pts);
        }
    }

    private static Vector3 CirclePoint(int axis, float angle) => axis switch
    {
        0 => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f),
        1 => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
        _ => new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle)),
    };
}
