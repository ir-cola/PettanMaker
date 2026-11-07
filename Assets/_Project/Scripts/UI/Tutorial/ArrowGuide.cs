/**===========================================================================

 File   : 誘導矢印 [ArrowGuide.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/10
 -----------------------------------------------------------------------------

 2つの UI（RectTransform）の間に、A から B へ「伸びる」矢印を繰り返し描く
 汎用ガイド。対象が動いても毎フレーム追従する。時間は unscaled。
 （例：アリスのハンガー → 右のプレイヤープレビュー枠 へドラッグ誘導）

===========================================================================**/

using UnityEngine;
using UnityEngine.UI;

public class ArrowGuide : MonoBehaviour {

    RectTransform _root, _arrow, _shaft, _head;
    CanvasGroup   _group;
    RectTransform _from, _to;
    Camera _cam;
    Color  _color = new Color(1f, 0.52f, 0.74f, 1f);
    float  _thickness = 30f, _headSize = 68f, _period = 1.05f, _phase = 0f, _endShorten = 95f;
    float  _tipPenetration = 52f;   // ビューポート枠に「乗せる」ためのめり込み量
    bool   _playing = true;

    public static ArrowGuide Create(Canvas parent, Color? color = null, int sortingOrder = 3100) {
        var go = new GameObject("ArrowGuide");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var ag = go.AddComponent<ArrowGuide>();
        ag._root = (RectTransform)go.transform;
        if (color.HasValue) ag._color = color.Value;
        ag.Build();
        return ag;
    }

    void Build() {
        _arrow = TutorialUIGfx.Child(_root, "Arrow");
        _arrow.anchorMin = _arrow.anchorMax = _arrow.pivot = new Vector2(0f, 0.5f);
        _group = _arrow.gameObject.AddComponent<CanvasGroup>();

        _shaft = TutorialUIGfx.Child(_arrow, "Shaft");
        _shaft.anchorMin = _shaft.anchorMax = _shaft.pivot = new Vector2(0f, 0.5f);
        var sImg = _shaft.gameObject.AddComponent<Image>();
        sImg.sprite = TutorialUIGfx.RoundedRect(32, 12); sImg.type = Image.Type.Sliced;
        sImg.color = _color; sImg.raycastTarget = false;

        _head = TutorialUIGfx.Child(_arrow, "Head");
        _head.pivot = new Vector2(0.5f, 0.5f);
        _head.anchorMin = _head.anchorMax = new Vector2(0f, 0.5f);
        _head.sizeDelta = new Vector2(_headSize, _headSize * 1.15f);
        var hImg = _head.gameObject.AddComponent<Image>();
        hImg.sprite = TutorialUIGfx.Triangle(48); hImg.color = _color; hImg.raycastTarget = false;
    }

    public void SetEndpoints(RectTransform from, RectTransform to, Camera cam = null) { _from = from; _to = to; _cam = cam; }
    public void SetColor(Color c) {
        _color = c;
        if (_shaft) _shaft.GetComponent<Image>().color = c;
        if (_head)  _head.GetComponent<Image>().color = c;
    }
    public void Play() { _playing = true; }
    public void Stop() { _playing = false; if (_arrow) _arrow.gameObject.SetActive(false); }
    public void Dismiss() { if (this != null) Destroy(gameObject); }

    void Update() { if (_playing) _phase += Time.unscaledDeltaTime / Mathf.Max(0.1f, _period); }

    void LateUpdate() {
        if (!_playing || _from == null || _to == null) { if (_arrow) _arrow.gameObject.SetActive(false); return; }
        _arrow.gameObject.SetActive(true);

        Vector2 aL = _root.InverseTransformPoint(WorldCenter(_from));   // 解像度非依存
        Vector2 bL = _root.InverseTransformPoint(WorldCenter(_to));

        Vector2 d = bL - aL;
        // プレイヤービューポート枠(_to)へ実際に入る所まで伸ばし、先端を枠の上に少し重ねる
        float len = ComputeReach(aL, bL);
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        _arrow.localPosition = new Vector3(aL.x, aL.y, 0f);   // アンカー非依存で from の位置へ正確に置く
        _arrow.localRotation = Quaternion.Euler(0, 0, ang);

        float phase = Mathf.Repeat(_phase, 1f);
        float grow  = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / 0.72f));
        float alpha = phase < 0.12f ? phase / 0.12f : (phase > 0.82f ? (1f - phase) / 0.18f : 1f);
        float tip   = len * grow;

        _shaft.sizeDelta = new Vector2(Mathf.Max(0f, tip - _headSize * 0.45f), _thickness);
        _head.anchoredPosition = new Vector2(tip, 0f);
        _group.alpha = alpha;
    }

    Vector3 WorldCenter(RectTransform rt) => rt.TransformPoint(rt.rect.center);

    // aL から _to 枠の近い辺に「入る」距離＋めり込み量を返す（先端をビューポートの上に乗せる）。
    // 交差しない構成では従来どおり中心手前 _endShorten で止める。
    float ComputeReach(Vector2 aL, Vector2 bL) {
        Vector2 d = bL - aL;
        float full = d.magnitude;
        if (full < 1e-3f) return 0f;
        Vector2 dir = d / full;

        var corners = new Vector3[4];
        _to.GetWorldCorners(corners);                       // _to の四隅（ワールド）→ root ローカル AABB
        Vector2 mn = _root.InverseTransformPoint(corners[0]);
        Vector2 mx = mn;
        for (int i = 1; i < 4; i++) {
            Vector2 p = _root.InverseTransformPoint(corners[i]);
            mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p);
        }
        float entry = RayBoxEntry(aL, dir, mn, mx);
        if (entry < 0f) return Mathf.Max(0f, full - _endShorten);   // 交差しない＝従来動作
        return Mathf.Clamp(entry + _tipPenetration, 0f, full * 0.99f);
    }

    // 半直線 o + t*dir が AABB[min,max] に入る最初の t(>=0)。入らなければ -1（スラブ法）。
    static float RayBoxEntry(Vector2 o, Vector2 dir, Vector2 min, Vector2 max) {
        float tmin = 0f, tmax = float.MaxValue;
        for (int a = 0; a < 2; a++) {
            float od = a == 0 ? dir.x : dir.y;
            float oo = a == 0 ? o.x   : o.y;
            float lo = a == 0 ? min.x : min.y;
            float hi = a == 0 ? max.x : max.y;
            if (Mathf.Abs(od) < 1e-6f) {
                if (oo < lo || oo > hi) return -1f;         // 平行かつ範囲外
            } else {
                float t1 = (lo - oo) / od, t2 = (hi - oo) / od;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tmin = Mathf.Max(tmin, t1);
                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return -1f;
            }
        }
        return tmin;                                        // aL が枠外なら入口 t、枠内なら 0
    }
}
