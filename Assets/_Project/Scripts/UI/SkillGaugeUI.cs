/**===========================================================================

 File   : スキルゲージ UI 反映 [SkillGaugeUI.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/17
 -----------------------------------------------------------------------------
 HUD の StickerGauge(虹色ゲージ)Image に付ける。SkillGauge.Active の値を
 マテリアル(UI/RainbowGauge)の _Fill へ流し込み、左→右に溜まる表示にする。
 アトラス sub-sprite の texcoord を 0..1 に正規化するための _UVRect も設定する。
 共有マテリアルを汚さないよう実行時にインスタンス化して使う。
 ===========================================================================**/

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class SkillGaugeUI : MonoBehaviour {

    private static readonly int FillId   = Shader.PropertyToID("_Fill");
    private static readonly int UVRectId = Shader.PropertyToID("_UVRect");

    [SerializeField] private Image image;
    [Tooltip("表示追従のスムージング速度(大きいほど素早く目標へ)")]
    [SerializeField] private float lerpSpeed = 10f;

    private Material _mat;
    private Sprite   _appliedSprite;
    private float    _shown = 0f;

    private void Awake() {
        if (image == null) image = GetComponent<Image>();
        // マテリアルをインスタンス化(アセットを汚さない)
        if (image != null && image.material != null) {
            _mat = Instantiate(image.material);
            _mat.name = image.material.name + " (Instance)";
            image.material = _mat;
        }
    }

    private void OnDestroy() {
        // 実行時に Instantiate したマテリアルは明示的に破棄する（毎シーンのリークを防ぐ）
        if (_mat != null) { Destroy(_mat); _mat = null; }
    }

    private void LateUpdate() {
        if (_mat == null || image == null) return;

        ApplyUVRect();

        SkillGauge g = SkillGauge.Active;
        float target = g != null ? g.Value01 : 0f;

        if (target <= 0.0001f) {
            _shown = 0f;                                   // 空(リセット/減少完了)は即座に空表示
        }
        else if (lerpSpeed > 0f) {
            _shown = Mathf.Lerp(_shown, target, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime));
        }
        else {
            _shown = target;
        }

        _mat.SetFloat(FillId, _shown);
    }

    // スプライトのアトラス UV 矩形を 0..1 正規化用にシェーダーへ渡す(スプライト差し替え時のみ)。
    private void ApplyUVRect() {
        Sprite sp = image.sprite;
        if (sp == null || sp == _appliedSprite) return;
        Texture tex = sp.texture;
        if (tex == null) return;
        Rect r = sp.textureRect;
        _mat.SetVector(UVRectId, new Vector4(
            r.xMin / tex.width, r.yMin / tex.height,
            r.xMax / tex.width, r.yMax / tex.height));
        _appliedSprite = sp;
    }
}
