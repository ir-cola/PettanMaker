/**===========================================================================

 File   : スプライト光アウトライン補助 [SpriteGlowOutline.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 UI/SpriteGlowOutline シェーダーを使う SpriteRenderer に付ける補助。
 スプライトのアトラス内 UV 範囲 (_UVRect) を MaterialPropertyBlock で
 シェーダーへ渡し、アウトラインのサンプルが隣のスプライトへ漏れないようにする。

===========================================================================**/

using UnityEngine;

/**===========================================================================
  スプライト光アウトライン補助
===========================================================================**/
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteGlowOutline : MonoBehaviour {

    static readonly int UVRectId = Shader.PropertyToID("_UVRect");

    private SpriteRenderer _renderer;
    private Sprite _appliedSprite;

    void OnEnable() {
        _renderer = GetComponent<SpriteRenderer>();
        Apply();
    }

    void LateUpdate() {
        // 着せ替え等でスプライトが差し替わったときだけ更新
        if (_renderer != null && _renderer.sprite != _appliedSprite) Apply();
    }

    void Apply() {
        if (_renderer == null || _renderer.sprite == null) return;
        Sprite sp = _renderer.sprite;
        Texture2D tex = sp.texture;
        if (tex == null) return;

        Rect r = sp.textureRect;
        var mpb = new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(mpb);
        mpb.SetVector(UVRectId, new Vector4(
            r.xMin / tex.width, r.yMin / tex.height,
            r.xMax / tex.width, r.yMax / tex.height));
        _renderer.SetPropertyBlock(mpb);
        _appliedSprite = sp;
    }
}
