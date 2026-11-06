using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root(子=通過点)から作るウェイポイントパス。各点の「位置」と「回転」を持ち、
/// t01∈[0,1] で全体を弧を描いて辿る(位置=スプライン / 回転=区間 Slerp)。
/// カメラ経路・移動経路など、いろんなオブジェクトで共有して使う。
///
/// 使い方:
///   var path = CinemaPath.FromRoot(cameraRoot);
///   if (path.IsValid) { cam.position = path.SamplePosition(t); cam.rotation = path.SampleRotation(t); }
/// </summary>
public sealed class CinemaPath
{
    public readonly List<Vector3> Points = new();
    public readonly List<Quaternion> Rotations = new();

    public int Count => Points.Count;
    public bool IsValid => Points.Count > 0;

    /// <summary>Root の(アクティブな)子から順に位置・回転を集めてパスを作る。</summary>
    public static CinemaPath FromRoot(Transform root)
    {
        var path = new CinemaPath();
        if (root == null) return path;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c == null || !c.gameObject.activeInHierarchy) continue;
            path.Points.Add(c.position);
            path.Rotations.Add(c.rotation);
        }
        return path;
    }

    private float U(float t01) => Mathf.Clamp01(t01) * Mathf.Max(1, Count - 1);

    public Vector3 SamplePosition(float t01)
        => Count == 1 ? Points[0] : CinemaSpline.Point(Points, U(t01));

    public Quaternion SampleRotation(float t01)
        => Count == 0 ? Quaternion.identity : CinemaSpline.Rot(Rotations, U(t01));

    /// <summary>t01 に対応するカット(スナップ)先のインデックス。各点を均等時間で切り替える。</summary>
    public int CutIndex(float t01)
        => Count == 0 ? 0 : Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(t01) * Count), 0, Count - 1);

    /// <summary>cut=true なら補間せず現在のカット点へスナップ(線がつながらない切替)。</summary>
    public Vector3 SamplePosition(float t01, bool cut)
        => cut ? (Count > 0 ? Points[CutIndex(t01)] : Vector3.zero) : SamplePosition(t01);

    public Quaternion SampleRotation(float t01, bool cut)
        => cut ? (Count > 0 ? Rotations[CutIndex(t01)] : Quaternion.identity) : SampleRotation(t01);

    /// <summary>移動用に密なスプライン点列(位置のみ)を返す。</summary>
    public List<Vector3> SmoothedPoints(int segmentsPerSpan = 12)
        => CinemaSpline.Smooth(Points, segmentsPerSpan);

    // ======================================================================
    //  ギズモ (Scene 可視化) — OnDrawGizmos から呼ぶ
    // ======================================================================

    /// <summary>スプライン曲線を単色で描く。</summary>
    public void DrawSpline(Color color)
    {
        if (Count < 2) return;
        Gizmos.color = color;
        Vector3 prev = Points[0];
        int spans = Count - 1, steps = 14;
        for (int s = 0; s < spans; s++)
            for (int k = 1; k <= steps; k++)
            {
                Vector3 cur = CinemaSpline.Point(Points, s + (float)k / steps);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
    }

    /// <summary>スプライン曲線を start→end のグラデーションで描く(順番が分かる)。</summary>
    public void DrawSplineGradient(Color start, Color end)
    {
        if (Count < 2) return;
        int spans = Count - 1, steps = 14, total = spans * steps, c = 0;
        Vector3 prev = Points[0];
        for (int s = 0; s < spans; s++)
            for (int k = 1; k <= steps; k++)
            {
                Vector3 cur = CinemaSpline.Point(Points, s + (float)k / steps);
                Gizmos.color = Color.Lerp(start, end, total > 0 ? (float)(++c) / total : 0f);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
    }

    /// <summary>各点に視界フラスタム(点の回転で向く)+ start→end グラデ + 番号を描く。</summary>
    public void DrawCameraMarkers(string labelPrefix, float fov = 32f, float range = 1.3f)
    {
        for (int i = 0; i < Count; i++)
        {
            float t = Count > 1 ? (float)i / (Count - 1) : 0f;
            Color col = Color.Lerp(new Color(0.3f, 1f, 0.4f), new Color(1f, 0.35f, 0.35f), t);

            Gizmos.color = col;
            Gizmos.matrix = Matrix4x4.TRS(Points[i], Rotations[i], Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, fov, range, 0f, 1.6f);
            Gizmos.matrix = Matrix4x4.identity;

            Gizmos.color = col;
            Gizmos.DrawWireSphere(Points[i], 0.16f);
#if UNITY_EDITOR
            UnityEditor.Handles.color = col;
            UnityEditor.Handles.Label(Points[i] + Vector3.up * 0.28f, $"{labelPrefix}{i}");
#endif
        }
    }

    /// <summary>
    /// デバッグ一括描画: スプライン(start→end グラデ)+ 各点の球・番号・「見ている方向」矢印
    /// (点の回転 forward)+ 任意でカメラフラスタム。順番と向きが一目で分かる。
    /// </summary>
    public void DrawDebug(string labelPrefix, Color startColor, Color endColor, bool frustums,
                          float lookLength = 2f, bool connected = true, Vector3? lookAt = null)
    {
        if (connected) DrawSplineGradient(startColor, endColor); // カット時は線を繋がない
        for (int i = 0; i < Count; i++)
        {
            float t = Count > 1 ? (float)i / (Count - 1) : 0f;
            Color col = Color.Lerp(startColor, endColor, t);

            Gizmos.color = col;
            Gizmos.DrawWireSphere(Points[i], 0.16f);

            // 見ている方向: LookAt があればそこへ、無ければ点の回転 forward。
            Vector3 look = lookAt.HasValue ? (lookAt.Value - Points[i]) : (Rotations[i] * Vector3.forward);
            Quaternion rot = look.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(look.normalized, Vector3.up) : Rotations[i];

            DrawArrow(Points[i], Points[i] + look.normalized * lookLength, col);
            if (lookAt.HasValue)
            {
                Gizmos.color = new Color(col.r, col.g, col.b, 0.35f);
                Gizmos.DrawLine(Points[i], lookAt.Value); // LookAt へ結ぶ
            }

            if (frustums)
            {
                Gizmos.color = col;
                Gizmos.matrix = Matrix4x4.TRS(Points[i], rot, Vector3.one);
                Gizmos.DrawFrustum(Vector3.zero, 30f, 1.1f, 0f, 1.6f);
                Gizmos.matrix = Matrix4x4.identity;
            }
#if UNITY_EDITOR
            UnityEditor.Handles.color = col;
            UnityEditor.Handles.Label(Points[i] + Vector3.up * 0.3f, $"{labelPrefix}{i}");
#endif
        }
    }

    /// <summary>矢印(線 + 矢じり)を描く。</summary>
    public static void DrawArrow(Vector3 from, Vector3 to, Color color, float head = 0.25f)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(from, to);
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 1e-4f) return;
        dir /= len;
        Vector3 up = Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up;
        Vector3 right = Vector3.Cross(up, dir).normalized;
        float h = Mathf.Min(head, len * 0.5f);
        Gizmos.DrawLine(to, to - dir * h + right * h * 0.5f);
        Gizmos.DrawLine(to, to - dir * h - right * h * 0.5f);
    }

    /// <summary>各点に球 + 向き矢印(点の回転)+ 番号を描く(移動経路向け)。</summary>
    public void DrawPathMarkers(Color color, string labelPrefix = null, float sphere = 0.32f, float arrow = 1.2f)
    {
        Gizmos.color = color;
        for (int i = 0; i < Count; i++) Gizmos.DrawWireSphere(Points[i], sphere);
        Gizmos.color = new Color(color.r, color.g, color.b, 0.7f);
        for (int i = 0; i < Count; i++)
            Gizmos.DrawRay(Points[i], Rotations[i] * Vector3.forward * arrow);
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(labelPrefix))
        {
            UnityEditor.Handles.color = color;
            for (int i = 0; i < Count; i++)
                UnityEditor.Handles.Label(Points[i] + Vector3.up * 0.28f, $"{labelPrefix}{i}");
        }
#endif
    }
}
