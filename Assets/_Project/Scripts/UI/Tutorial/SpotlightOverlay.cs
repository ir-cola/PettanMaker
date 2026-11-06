/**===========================================================================

 File   : スポットライト暗転オーバーレイ [SpotlightOverlay.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/10
 -----------------------------------------------------------------------------

 画面全体を暗転させ、指定 RectTransform（最大8個）の位置だけ角丸の穴で
 明るく抜く汎用オーバーレイ。対象が動いても毎フレーム追従する。
 UI/SpotlightMask シェーダーで穴を描く。クリックは塞がない（視覚のみ）。

   ・穴の下（sortingOrder が小さい Canvas）の UI が穴から覗く
   ・常に明るく見せたい UI（吹き出し等）はこの sortingOrder より上に置く

===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SpotlightOverlay : MonoBehaviour {

    public struct Target {
        public RectTransform rt;
        public float padding;       // 穴の余白(px)
        public float cornerRadius;  // 角丸(px)
        public float softness;      // 縁のぼかし(px)
        public Camera cam;          // 対象Canvasのカメラ(Overlayなら null)
        public float circleRadius;  // >0 なら rt 中心の「円」穴(px)。角丸矩形の代わり(アイリス演出用)

        public Target(RectTransform rt, float padding = 24f, float cornerRadius = 28f, float softness = 26f, Camera cam = null) {
            this.rt = rt; this.padding = padding; this.cornerRadius = cornerRadius; this.softness = softness; this.cam = cam; this.circleRadius = 0f;
        }
    }

    Canvas   _canvas;
    RawImage _image;      // Image(スプライト無し)は描画されないため RawImage を使う
    Material _mat;
    readonly List<Target> _targets = new List<Target>();
    readonly Vector4[] _holes      = new Vector4[8];
    readonly Vector4[] _holeParams = new Vector4[8];
    float _dimAlpha    = 0f;
    float _targetAlpha = 0.72f;
    Color _dimColor    = Color.black;

    static readonly int HoleCountId  = Shader.PropertyToID("_HoleCount");
    static readonly int HolesId      = Shader.PropertyToID("_Holes");
    static readonly int HoleParamsId = Shader.PropertyToID("_HoleParams");
    static readonly int AspectId     = Shader.PropertyToID("_Aspect");
    static readonly int DimAlphaId   = Shader.PropertyToID("_DimAlpha");
    static readonly int DimColorId   = Shader.PropertyToID("_Color");

    public static SpotlightOverlay Create(int sortingOrder = 3000, float dimAlpha = 0.74f, Color? dimColor = null) {
        var go = new GameObject("SpotlightOverlay");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        go.AddComponent<GraphicRaycaster>();
        var so = go.AddComponent<SpotlightOverlay>();
        so._canvas      = canvas;
        so._targetAlpha = dimAlpha;
        so._dimColor    = dimColor ?? Color.black;
        so.Build();
        return so;
    }

    void Build() {
        var imgRT = TutorialUIGfx.Child(transform, "Dim");
        imgRT.anchorMin = Vector2.zero; imgRT.anchorMax = Vector2.one;
        imgRT.offsetMin = Vector2.zero; imgRT.offsetMax = Vector2.zero;
        _image = imgRT.gameObject.AddComponent<RawImage>();
        _image.raycastTarget = false;                       // クリックは塞がない(視覚のみ)
        var sh = Shader.Find("UI/SpotlightMask");
        _mat = new Material(sh);
        _image.material = _mat;
        _mat.SetColor(DimColorId, _dimColor);
        _mat.SetFloat(DimAlphaId, 0f);
    }

    public void SetTargets(params Target[] targets) {
        _targets.Clear();
        if (targets != null) _targets.AddRange(targets);
    }
    public void SetTargets(IEnumerable<Target> targets) {
        _targets.Clear();
        if (targets != null) _targets.AddRange(targets);
    }
    public void ClearTargets() { _targets.Clear(); }

    void LateUpdate() {
        if (_mat == null || _image == null) return;
        // 暗転板のローカル(=CanvasScaler の参照解像度)空間で穴を計算する＝解像度非依存。
        RectTransform dimRT = _image.rectTransform;
        Rect dr = dimRT.rect;
        float w = Mathf.Max(1f, dr.width), h = Mathf.Max(1f, dr.height);
        _mat.SetFloat(AspectId, w / h);

        int n = Mathf.Min(_targets.Count, 8);
        for (int i = 0; i < n; i++) {
            var t = _targets[i];
            if (t.rt == null || !t.rt.gameObject.activeInHierarchy) { _holes[i] = Vector4.zero; _holeParams[i] = Vector4.zero; continue; }
            var c = new Vector3[4];
            t.rt.GetWorldCorners(c);
            if (t.circleRadius > 0f) {
                // 円穴：rt 中心＋半径 R(px)。cornerRadius=R/h・halfSize=(R/w,R/h) で SDF が円になる
                Vector2 cen = dimRT.InverseTransformPoint((c[0] + c[2]) * 0.5f);
                float R = t.circleRadius;
                _holes[i]      = new Vector4((cen.x - dr.x) / w, (cen.y - dr.y) / h, R / w, R / h);
                _holeParams[i] = new Vector4(R / h, Mathf.Max(t.softness, 1f) / h, 0, 0);
            } else {
                Vector2 a = dimRT.InverseTransformPoint(c[0]);   // ワールド角 → 暗転板ローカル
                Vector2 b = dimRT.InverseTransformPoint(c[2]);
                Vector2 mn = Vector2.Min(a, b) - new Vector2(t.padding, t.padding);
                Vector2 mx = Vector2.Max(a, b) + new Vector2(t.padding, t.padding);
                Vector2 centerUV = new Vector2(((mn.x + mx.x) * 0.5f - dr.x) / w, ((mn.y + mx.y) * 0.5f - dr.y) / h);
                Vector2 halfUV   = new Vector2((mx.x - mn.x) * 0.5f / w, (mx.y - mn.y) * 0.5f / h);
                _holes[i]      = new Vector4(centerUV.x, centerUV.y, halfUV.x, halfUV.y);
                _holeParams[i] = new Vector4(t.cornerRadius / h, Mathf.Max(t.softness, 1f) / h, 0, 0);
            }
        }
        _mat.SetFloat(HoleCountId, n);
        _mat.SetVectorArray(HolesId, _holes);
        _mat.SetVectorArray(HoleParamsId, _holeParams);
        _mat.SetFloat(DimAlphaId, _dimAlpha);
    }

    public IEnumerator FadeIn(float dur = 0.35f)  { yield return Fade(_targetAlpha, dur); }
    public IEnumerator FadeOut(float dur = 0.3f)  { yield return Fade(0f, dur); }
    IEnumerator Fade(float to, float dur) {
        float from = _dimAlpha, t = 0f;
        while (t < dur) { t += Time.unscaledDeltaTime; _dimAlpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)); yield return null; }
        _dimAlpha = to;
    }
    public void SetAlphaImmediate(float a) { _dimAlpha = a; }
    public float TargetAlpha { get => _targetAlpha; set => _targetAlpha = value; }
    public void Dismiss() { if (this != null) Destroy(gameObject); }
}
