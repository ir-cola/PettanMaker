/**===========================================================================

 File   : 汎用シールFXドライバ [StickerFX.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/10
 -----------------------------------------------------------------------------

 Sticker/UI ・ Sticker/World シェーダーを駆動する共通ドライバ。
 「貼る(Apply)」「剥がす(Peel)」をコード側から汎用的に呼べるよう集約する。

   ・ApplyUI(Graphic) : 任意の UGUI Image/RawImage を「シールを貼る」演出で出す
                        （角の折り返しが密着 ＋ 接着シマー ＋ 一瞬のポップ拡大）。
                        HUD・シール帳・メニューなど UI 全般で再利用できる。
   ・共有 PropertyID  : _Peel/_Dir/_Curl/_Dissolve/_Shimmer… を一元管理。
   ・World 系の「剥がして飛ばす」は従来どおり StickerPeelFX を使う
     （同じプロパティ名なので Sticker/World でそのまま動く）。

===========================================================================**/

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public static class StickerFX {

    // ---- 共有プロパティ ID ----
    public static readonly int PeelId         = Shader.PropertyToID("_Peel");
    public static readonly int DirXId         = Shader.PropertyToID("_DirX");
    public static readonly int DirYId         = Shader.PropertyToID("_DirY");
    public static readonly int FlipYId        = Shader.PropertyToID("_FlipY");
    public static readonly int UVRectId       = Shader.PropertyToID("_UVRect");
    public static readonly int CurlRadiusId   = Shader.PropertyToID("_CurlRadius");
    public static readonly int DissolveId     = Shader.PropertyToID("_Dissolve");
    public static readonly int ShimmerId      = Shader.PropertyToID("_Shimmer");
    public static readonly int ShimmerColorId = Shader.PropertyToID("_ShimmerColor");
    public static readonly int ShimmerWidthId = Shader.PropertyToID("_ShimmerWidth");
    public static readonly int WrinkleId      = Shader.PropertyToID("_Wrinkle");
    public static readonly int ShimmerPhaseId = Shader.PropertyToID("_ShimmerPhase");

    public const string UIShaderName          = "Sticker/UI";
    public const string WorldShaderName        = "Sticker/World";
    public const string ApplyUIMaterialResource = "M_StickerApplyUI";   // Resources 内(任意)

    /**=======================================================================
      UI 貼り付け演出のパラメータ
    =======================================================================**/
    public class UIApplyOptions {
        public float   duration     = 0.55f;
        public float   startPeel    = 0.85f;                 // めくれ量(開始)。1に近いほど大きく浮いた状態から貼る
        public Vector2 dir          = new Vector2(-1f, 1f);  // 貼り付け方向(この角から密着していく)
        public float   popScale     = 1.18f;                 // 貼り付く瞬間の一瞬の拡大
        public float   startScale   = 0.55f;                 // 出現時の縮み
        public float   settleTilt   = 7f;                    // 貼り付き時に揺れて戻る微傾き(度)
        public float   shimmer      = 1.1f;                  // 接着シマー量
        public Color   shimmerColor = new Color(1f, 1f, 1f, 1f);
        public bool    useUnscaledTime = true;               // 世界停止(timeScale=0)中でも動くように
        public bool    keepMaterial  = false;                // 完了後もシールマテリアルを残すか(通常は戻す)
    }

    /**=======================================================================
      UGUI の Graphic をシールのように貼り付ける。
      Material はインスタンス化して割り当て、完了時に既定へ戻す(keepMaterial=false)。
    =======================================================================**/
    public static IEnumerator ApplyUI(Graphic g, UIApplyOptions o = null) {
        if (g == null) yield break;
        o = o ?? new UIApplyOptions();

        Material baseMat = g.material;                       // 元マテリアル退避
        Material mat = NewApplyUIMaterial();
        if (mat == null) yield break;

        // Image のスプライトがアトラス内でも折り目計算が 0-1 になるよう UV 矩形を設定
        Vector4 uvRect = new Vector4(0, 0, 1, 1);
        Image img = g as Image;
        if (img != null && img.sprite != null && img.sprite.texture != null) {
            Rect tr = img.sprite.textureRect;
            Texture tx = img.sprite.texture;
            uvRect = new Vector4(tr.x / tx.width, tr.y / tx.height, tr.width / tx.width, tr.height / tx.height);
        }
        mat.SetVector(UVRectId, uvRect);
        mat.SetFloat(DirXId, o.dir.x);
        mat.SetFloat(DirYId, o.dir.y);
        mat.SetColor(ShimmerColorId, o.shimmerColor);
        mat.SetFloat(PeelId, 0f);                            // 折り(fold)は使わない＝メッシュ非依存で安定(バグ回避)
        mat.SetFloat(ShimmerId, o.shimmer);
        mat.SetFloat(ShimmerPhaseId, 0f);
        g.material = mat;

        RectTransform rt = g.rectTransform;
        Vector3    baseScale = rt.localScale;
        Quaternion baseRot   = rt.localRotation;
        rt.localScale = baseScale * o.startScale;

        float t = 0f;
        while (t < o.duration) {
            t += o.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float p = Mathf.Clamp01(t / Mathf.Max(0.001f, o.duration));

            mat.SetFloat(ShimmerPhaseId, p);                                             // 光帯が端から端へ掃引
            rt.localScale = baseScale * Mathf.Lerp(o.startScale, 1f, EaseOutBack(p));    // バイン：overshootで弾む
            rt.localRotation = baseRot * Quaternion.Euler(0, 0, o.settleTilt * (1f - p) * Mathf.Sin(p * Mathf.PI * 3f));
            yield return null;
        }

        mat.SetFloat(ShimmerId, 0f);
        rt.localScale = baseScale;
        rt.localRotation = baseRot;
        if (!o.keepMaterial) g.material = baseMat;                                        // 既定マテリアルへ戻す
    }

    /// <summary>Sticker/UI(型抜き・ストレート)マテリアルを生成する。Resources にあればそれを複製。</summary>
    public static Material NewApplyUIMaterial() {
        Material src = Resources.Load<Material>(ApplyUIMaterialResource);
        if (src != null) return new Material(src);
        Shader sh = Shader.Find(UIShaderName);
        if (sh == null) { Debug.LogWarning("[StickerFX] Sticker/UI シェーダーが見つかりません"); return null; }
        Material m = new Material(sh);
        m.EnableKeyword("_STICKER_CUTOUT");   // スプライトの α を尊重(型抜き)・ストレート合成
        m.SetVector(UVRectId, new Vector4(0, 0, 1, 1));
        m.SetColor(ShimmerColorId, Color.white);
        m.SetFloat(ShimmerWidthId, 0.16f);
        return m;
    }

    /// <summary>World シール剥がし用のマテリアル準備(_Peel リセット + カール半径)。StickerPeelFX と同等。</summary>
    public static void PrepareWorld(Material mat, float curlRadius = 0.24f) {
        if (mat == null) return;
        if (mat.HasProperty(PeelId))       mat.SetFloat(PeelId, 0f);
        if (mat.HasProperty(CurlRadiusId)) mat.SetFloat(CurlRadiusId, curlRadius);
    }

    static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
    static float EaseOutBack(float x) {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}
