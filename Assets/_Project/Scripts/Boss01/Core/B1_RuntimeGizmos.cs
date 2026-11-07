using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Game ビューでも見えるデバッグ描画ユーティリティ。
/// B1_Boss と同じ GameObject にアタッチする。
/// Built-in: OnRenderObject / URP・HDRP: RenderPipelineManager.endCameraRendering で描画。
/// </summary>
public class B1_RuntimeGizmos : MonoBehaviour
{
    private static readonly List<(Vector3 from, Vector3 to, Color color)> lines = new();

    private Material mat;

    // ---- 塗りつぶし円（同心円＋放射線） ----

    public static void DrawFilledCircle(Vector3 center, float radius, Color color, float fillT, int rings = 6, int segments = 32)
    {
        Color inner = color;
        inner.a = color.a * 0.4f;

        DrawCircle(center, radius, color, segments);

        int ringCount = Mathf.RoundToInt(rings * fillT);
        for (int r = 1; r <= ringCount; r++)
        {
            float t = (float)r / rings;
            DrawCircle(center, radius * t, Color.Lerp(inner, color, t), segments);
        }

        int rayCount = Mathf.RoundToInt(16 * fillT);
        float rayStep = 360f / Mathf.Max(1, rayCount);
        for (int i = 0; i < rayCount; i++)
        {
            float   angle = Mathf.Deg2Rad * rayStep * i;
            Vector3 dir   = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            lines.Add((center, center + dir * radius, inner));
        }
    }

    // ---- 塗りつぶし扇形 ----

    public static void DrawFilledArc(Vector3 apex, Vector3 forward, float arcAngle, float range, Color color, float fillT, int segments = 32)
    {
        Color inner = color;
        inner.a = color.a * 0.35f;

        DrawArc(apex, forward, arcAngle, range, color);

        float halfAngle = arcAngle * 0.5f;
        int   lineCount = Mathf.RoundToInt(segments * fillT);
        float step      = arcAngle / segments;
        for (int i = 0; i <= lineCount; i++)
        {
            float   angle = -halfAngle + step * i;
            Vector3 dir   = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            lines.Add((apex, apex + dir * range, inner));
        }
    }

    // ---- オーラ（パルス状の拡散リング） ----

    public static void DrawAuraRings(Vector3 center, float maxRadius, Color color, float fillT, int ringCount = 3)
    {
        for (int i = 0; i < ringCount; i++)
        {
            float offset = (float)i / ringCount;
            float t      = Mathf.Repeat(fillT + offset, 1f);
            float r      = maxRadius * t;
            Color c      = color;
            c.a = color.a * (1f - t);
            DrawCircle(center, r, c, 24);
        }
    }

    // ---- プリミティブ ----

    public static void DrawLine(Vector3 from, Vector3 to, Color color)
        => lines.Add((from, to, color));

    public static void DrawCircle(Vector3 center, float radius, Color color, int segments = 32)
    {
        float step = 360f / segments;
        for (int i = 0; i < segments; i++)
        {
            float   a0 = Mathf.Deg2Rad * step * i;
            float   a1 = Mathf.Deg2Rad * step * (i + 1);
            Vector3 p0 = center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            lines.Add((p0, p1, color));
        }
    }

    public static void DrawArc(Vector3 apex, Vector3 forward, float arcAngle, float range, Color color, int segments = 24)
    {
        float   halfAngle = arcAngle * 0.5f;
        float   step      = arcAngle / segments;
        Vector3 prev      = apex + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * range;

        for (int i = 1; i <= segments; i++)
        {
            Vector3 curr = apex + Quaternion.AngleAxis(-halfAngle + step * i, Vector3.up) * forward * range;
            lines.Add((prev, curr, color));
            prev = curr;
        }
        lines.Add((apex, apex + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * range, color));
        lines.Add((apex, apex + Quaternion.AngleAxis( halfAngle, Vector3.up) * forward * range, color));
    }

    // ---- Unity ライフサイクル ----

    private void Awake()
    {
        mat = new Material(Shader.Find("Hidden/Internal-Colored"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)CompareFunction.Always);
    }

    private void OnEnable()
    {
        // URP / HDRP 対応
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    // URP/HDRP: カメラレンダリング終了後
    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        Flush(cam);
    }

    // Built-in パイプライン: 全カメラ描画後
    private void OnRenderObject()
    {
        Flush(null);
    }

    private void Flush(Camera cam)
    {
        if (lines.Count == 0) return;

        mat.SetPass(0);
        GL.PushMatrix();
        if (cam != null)
        {
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
        }
        GL.Begin(GL.LINES);
        foreach (var (from, to, color) in lines)
        {
            GL.Color(color);
            GL.Vertex(from);
            GL.Vertex(to);
        }
        GL.End();
        GL.PopMatrix();
        lines.Clear();
    }
}
