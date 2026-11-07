using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// カメラ/移動演出まわりの共有スプライン計算。ウェイポイントを Catmull-Rom で
/// 弧を描いて滑らかに繋ぐ。位置は Catmull-Rom、回転は区間 Slerp。
/// B1_EntranceIntro / B1_CameraDirector など複数のオブジェクトから使う。
/// </summary>
public static class CinemaSpline
{
    public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1)
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>媒介変数 u∈[0, count-1] でスプライン上の位置を返す。</summary>
    public static Vector3 Point(IReadOnlyList<Vector3> pts, float u)
    {
        int n = pts.Count;
        if (n == 0) return Vector3.zero;
        if (n == 1) return pts[0];
        u = Mathf.Clamp(u, 0f, n - 1);
        int i = Mathf.Min((int)u, n - 2);
        float t = u - i;
        Vector3 p0 = pts[Mathf.Max(i - 1, 0)];
        Vector3 p3 = pts[Mathf.Min(i + 2, n - 1)];
        return CatmullRom(p0, pts[i], pts[i + 1], p3, t);
    }

    /// <summary>媒介変数 u∈[0, count-1] で回転を区間 Slerp して返す。</summary>
    public static Quaternion Rot(IReadOnlyList<Quaternion> rots, float u)
    {
        int n = rots.Count;
        if (n == 0) return Quaternion.identity;
        if (n == 1) return rots[0];
        u = Mathf.Clamp(u, 0f, n - 1);
        int i = Mathf.Min((int)u, n - 2);
        float t = Mathf.SmoothStep(0f, 1f, u - i);
        return Quaternion.Slerp(rots[i], rots[i + 1], t);
    }

    /// <summary>ウェイポイント列をスプラインで密にサンプリングした点列にする(2点以下はそのまま)。</summary>
    public static List<Vector3> Smooth(IReadOnlyList<Vector3> pts, int segmentsPerSpan = 12)
    {
        var outp = new List<Vector3>();
        if (pts.Count < 3)
        {
            for (int i = 0; i < pts.Count; i++) outp.Add(pts[i]);
            return outp;
        }
        int spans = pts.Count - 1;
        for (int s = 0; s < spans; s++)
            for (int k = 0; k < segmentsPerSpan; k++)
                outp.Add(Point(pts, s + (float)k / segmentsPerSpan));
        outp.Add(pts[pts.Count - 1]);
        return outp;
    }

    /// <summary>始点から点列を辿る水平距離の総和(y は無視。0除算回避に下限あり)。</summary>
    public static float HorizontalLength(Vector3 start, IReadOnlyList<Vector3> pts)
    {
        float total = 0f;
        Vector3 prev = start; prev.y = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 b = pts[i]; b.y = 0f;
            total += Vector3.Distance(prev, b);
            prev = b;
        }
        return Mathf.Max(0.001f, total);
    }
}
