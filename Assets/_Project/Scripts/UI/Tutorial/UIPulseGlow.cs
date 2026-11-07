/**===========================================================================

 File   : UI 縁グロー(脈動) [UIPulseGlow.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/10
 -----------------------------------------------------------------------------

 対象 UI の背後にソフトグローを敷き、脈動(拡縮＋明滅)させて縁を光らせる汎用演出。
 「TAB でシール帳を開こう」演出でブックボタンの縁を光らせるのに使う。時間は unscaled。

   使い方: var g = UIPulseGlow.Attach(targetRect, color); ... g.Dismiss();

===========================================================================**/

using UnityEngine;
using UnityEngine.UI;

public class UIPulseGlow : MonoBehaviour {

    RectTransform _rt;
    Image _img;
    Color _color = new Color(1f, 0.86f, 0.35f, 1f);
    float _minA = 0.35f, _maxA = 0.95f, _minS = 1.0f, _maxS = 1.22f, _speed = 3.2f, _t = 0f;
    float _baseSize;

    /// <summary>対象の背後(直前の兄弟)にグローを差し込む。</summary>
    public static UIPulseGlow Attach(RectTransform target, Color? color = null, float sizeScale = 1.75f) {
        if (target == null) return null;
        var go = new GameObject("PulseGlow", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(target.parent, false);
        rt.SetSiblingIndex(target.GetSiblingIndex());            // 対象の直前＝背後に描く
        Vector2 sz = target.rect.size * sizeScale;
        rt.position    = target.position;
        rt.sizeDelta   = sz;
        rt.localScale  = target.localScale;
        rt.localRotation = target.localRotation;

        var img = go.AddComponent<Image>();
        img.sprite = TutorialUIGfx.SoftGlow(128);
        img.raycastTarget = false;

        var g = go.AddComponent<UIPulseGlow>();
        g._rt = rt; g._img = img;
        if (color.HasValue) g._color = color.Value;
        g._baseSize = 1f;
        img.color = g._color;
        return g;
    }

    void Update() {
        _t += Time.unscaledDeltaTime * _speed;
        float w = (Mathf.Sin(_t) * 0.5f + 0.5f);
        var c = _color; c.a = Mathf.Lerp(_minA, _maxA, w);
        if (_img) _img.color = c;
        if (_rt)  _rt.localScale = Vector3.one * Mathf.Lerp(_minS, _maxS, w);
    }

    public void SetColor(Color c) { _color = c; }
    public void Dismiss() { if (this != null) Destroy(gameObject); }
}
