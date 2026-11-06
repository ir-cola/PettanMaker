/**===========================================================================

 File   : チュートリアルUI 手続き生成グラフィック [TutorialUIGfx.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/10
 -----------------------------------------------------------------------------

 吹き出し・矢印・グロー等に使う簡単なスプライトを実行時ベイクして提供する
 共通ユーティリティ。アセット依存なし＝ビルドで消えない。生成物はキャッシュ。

===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

public static class TutorialUIGfx {

    static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

    static Sprite Cached(string key, System.Func<Sprite> make) {
        if (_cache.TryGetValue(key, out var s) && s != null) return s;
        s = make();
        _cache[key] = s;
        return s;
    }

    /// <summary>白い角丸矩形(9スライス対応)。吹き出し・パネル背景に。</summary>
    public static Sprite RoundedRect(int size = 48, int radius = 14) {
        return Cached($"rr_{size}_{radius}", () => {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            float half = size * 0.5f, r = radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    float px_ = x + 0.5f - half, py_ = y + 0.5f - half;
                    float qx = Mathf.Abs(px_) - (half - r), qy = Mathf.Abs(py_) - (half - r);
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float sd = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - sd);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                                 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        });
    }

    /// <summary>放射状ソフトグロー(中心=不透明→縁=透明)。縁を光らせる用途。</summary>
    public static Sprite SoftGlow(int size = 128) {
        return Cached($"glow_{size}", () => {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a;                                   // 中心を強く
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        });
    }

    /// <summary>+X 方向を向いた三角形(矢印の頭)。</summary>
    public static Sprite Triangle(int size = 48) {
        return Cached($"tri_{size}", () => {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    float u = x / (float)(size - 1);             // 0..1 (先端=1)
                    float v = y / (float)(size - 1);             // 0..1
                    float halfSpan = 0.5f * (1f - u);            // 先端で0
                    float dist = Mathf.Abs(v - 0.5f) - halfSpan; // <0 で内側
                    float a = Mathf.Clamp01(-dist * (size * 0.5f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        });
    }

    /// <summary>子として RectTransform を作る簡易ヘルパー。</summary>
    public static RectTransform Child(Transform parent, string name) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
