/**===========================================================================

 File   : スタンプ風テキスト演出 [StampTextBanner.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/5
 -----------------------------------------------------------------------------
 「シロウサギをつかまえろ！」のような一言を、1文字ずつスタンプを押すように
 ドンッと画面へ出すオーバーレイ演出。

 ・各文字: 拡大状態 (画面手前) から easeOutBack で着地 + フェードイン + 傾き戻し。
   少しめり込んで戻る戻りが「押した」感を出す (文字自体は 2D)。
 ・本体の下にズレ影レイヤーを重ねて立体感を出す。
 ・フォントは SceneLoaderAssets.font (RabbitandFullMoon SDF, Resources 経由) を使用。

 使い方: StampTextBanner.Show("シロウサギをつかまえろ！");
 ===========================================================================**/

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StampTextBanner : MonoBehaviour {

    const float CharInterval  = 0.07f;   // 文字ごとの出現間隔 (秒)
    const float CharPopTime   = 0.30f;   // 1文字の着地時間 (秒)
    const float StartScale    = 2.6f;    // 出現時の拡大率 (手前から押される感じ)
    const float StartRotation = 14f;     // 出現時の傾き (度)。文字ごとに交互
    const float DefaultHold   = 1.8f;    // 全文字表示後の保持 (秒)
    const float FadeTime      = 0.5f;    // 退場フェード (秒)

    TextMeshProUGUI _main, _shadow;
    CanvasGroup _group;
    float _elapsed;
    int _charCount;

    /** 表示中のバナー数（0 なら何も出ていない） */
    public static int ActiveCount { get; private set; }
    /** 一度でもバナーを表示したか（チュートリアル表示のトリガー用） */
    public static bool AnyShownYet { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ActiveCount = 0; AnyShownYet = false; }

    public static void Show(string message) { Show(message, DefaultHold); }

    public static void Show(string message, float holdSeconds) {
        if (string.IsNullOrEmpty(message)) return;
        var go = new GameObject("StampTextBanner");
        var banner = go.AddComponent<StampTextBanner>();
        banner.Build(message);
        banner.StartCoroutine(banner.Run(holdSeconds));
        ActiveCount++;
        AnyShownYet = true;
    }

    void OnDestroy() {
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
    }

    void Build(string message) {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4500;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;

        TMP_FontAsset font = ResolveFont();
        // 影を先に作る (描画順: 影が下、本体が上)
        _shadow = MakeText("Shadow", message, font, new Color(0.22f, 0.12f, 0.30f, 0.9f), new Vector2(7f, -8f));
        _main   = MakeText("Main",   message, font, Color.white, Vector2.zero);
        _charCount = message.Length;
    }

    TextMeshProUGUI MakeText(string name, string message, TMP_FontAsset font, Color color, Vector2 offset) {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = message;
        if (font != null) t.font = font;
        t.fontSize = 88f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.raycastTarget = false;

        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);   // 上部中央
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1800f, 200f);
        rt.anchoredPosition = new Vector2(0f, -210f) + offset;
        return t;
    }

    static TMP_FontAsset ResolveFont() {
        // ロード画面と同じ RabbitandFullMoon SDF (Resources 経由なのでビルドでも有効)
        var assets = Resources.Load<SceneLoaderAssets>("SceneLoaderAssets");
        if (assets != null && assets.font != null) return assets.font;
#if UNITY_EDITOR
        var f = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/_Project/Fonts/RabbitandFullMoon SDF.asset");
        if (f != null) return f;
#endif
        return null;   // 見つからなければ TMP 既定フォント
    }

    IEnumerator Run(float holdSeconds) {
        float revealTime = CharInterval * Mathf.Max(0, _charCount - 1) + CharPopTime;

        while (_elapsed < revealTime + holdSeconds) {
            _elapsed += Time.deltaTime;
            Animate(_main);
            Animate(_shadow);
            yield return null;
        }

        float f = 0f;
        while (f < FadeTime) {
            f += Time.deltaTime;
            _group.alpha = 1f - Mathf.Clamp01(f / FadeTime);
            Animate(_main);
            Animate(_shadow);
            yield return null;
        }
        Destroy(gameObject);
    }

    /**=======================================================================
      1文字ずつのスタンプ着地アニメーション (TMP 頂点操作)
      経過時間から各文字の進行度を求め、文字の中心まわりに拡大縮小+回転を適用。
    =======================================================================**/
    void Animate(TextMeshProUGUI text) {
        if (text == null) return;
        text.ForceMeshUpdate();
        var ti = text.textInfo;
        if (ti == null || ti.characterCount == 0) return;

        for (int c = 0; c < ti.characterCount; c++) {
            var ci = ti.characterInfo[c];
            if (!ci.isVisible) continue;

            int vi = ci.vertexIndex;
            int mi = ci.materialReferenceIndex;
            var verts = ti.meshInfo[mi].vertices;
            var cols  = ti.meshInfo[mi].colors32;

            float t = (_elapsed - c * CharInterval) / CharPopTime;
            Vector3 mid = (verts[vi] + verts[vi + 2]) * 0.5f;

            if (t <= 0f) {
                // 未出現: 中心へつぶして透明化
                for (int k = 0; k < 4; k++) { verts[vi + k] = mid; cols[vi + k].a = 0; }
                continue;
            }

            float p = Mathf.Clamp01(t);
            // easeOutBack: 大きい状態から着地し、わずかにめり込んで戻る (押した感)
            const float s = 1.70158f;
            float u = p - 1f;
            float ease = 1f + (s + 1f) * u * u * u + s * u * u;

            float scale = Mathf.LerpUnclamped(StartScale, 1f, ease);
            float rotZ  = Mathf.LerpUnclamped((c % 2 == 0) ? StartRotation : -StartRotation, 0f, ease);
            float alphaMul = Mathf.Clamp01(p * 2f);          // 前半で不透明へ

            Quaternion rot = Quaternion.Euler(0f, 0f, rotZ);
            for (int k = 0; k < 4; k++) {
                verts[vi + k] = mid + rot * ((verts[vi + k] - mid) * scale);
                cols[vi + k].a = (byte)(cols[vi + k].a * alphaMul);
            }
        }

        for (int m = 0; m < ti.meshInfo.Length; m++) {
            var mesh = ti.meshInfo[m].mesh;
            mesh.vertices = ti.meshInfo[m].vertices;
            mesh.colors32 = ti.meshInfo[m].colors32;
            text.UpdateGeometry(mesh, m);
        }
    }
}
