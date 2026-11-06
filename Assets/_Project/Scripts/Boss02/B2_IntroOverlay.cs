/*=============================================================================

 File   : [B2_IntroOverlay.cs]
 Desc   : 女王(Boss02)登場演出中に前面へ重ねる UI。
          上下のレターボックス帯とボス名テキスト(TMP)を実行時に生成する。
          駆動は呼び出し側(B2_QueenIntroCamera)が SetBars / SetTitleAlpha を
          毎フレーム渡す受動スタイル。演出専用の一時オブジェクトで、
          完了時に Dispose で自壊する。
 ------------------------------------------------------------------------------

 Date   : 2026/07/10
 Author : akatu

===============================================================================*/

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Boss02
{
    /// <summary>女王登場中のレターボックス + ボス名オーバーレイ(ランタイム生成)。</summary>
    public sealed class B2_IntroOverlay : MonoBehaviour
    {
        private RectTransform topBar;
        private RectTransform bottomBar;
        private CanvasGroup titleGroup;
        private float barTargetHeight;   // 帯1本の最大高さ(px, 参照解像度基準)

        /// <summary>
        /// オーバーレイを生成する。bossName が空でレターボックスも不要なら null を返す。
        /// </summary>
        /// <param name="bossName">表示するボス名(空ならテキスト無し)</param>
        /// <param name="font">TMP フォント(未設定なら既定フォント)</param>
        /// <param name="fontSize">フォントサイズ</param>
        /// <param name="titleColor">文字色</param>
        /// <param name="outlineColor">アウトライン色</param>
        /// <param name="barColor">レターボックス帯の色</param>
        /// <param name="barHeightFraction">帯1本の高さ(画面高に対する割合 0-0.5)</param>
        public static B2_IntroOverlay Create(string bossName, TMP_FontAsset font, float fontSize,
            Color titleColor, Color outlineColor, Color barColor, float barHeightFraction)
        {
            bool wantBars  = barHeightFraction > 0.0001f;
            bool wantTitle = !string.IsNullOrEmpty(bossName);
            if (!wantBars && !wantTitle) return null;

            var go = new GameObject("B2_IntroOverlay") { hideFlags = HideFlags.DontSave };
            var self = go.AddComponent<B2_IntroOverlay>();
            self.Build(bossName, font, fontSize, titleColor, outlineColor, barColor,
                       wantBars ? Mathf.Clamp(barHeightFraction, 0f, 0.5f) : 0f, wantTitle);
            return self;
        }

        private void Build(string bossName, TMP_FontAsset font, float fontSize, Color titleColor,
            Color outlineColor, Color barColor, float barHeightFraction, bool wantTitle)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000; // シーン UI より前

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // 高さ基準(レターボックスは縦の割合が正義)
            gameObject.AddComponent<GraphicRaycaster>().enabled = false;

            barTargetHeight = 1080f * barHeightFraction;
            if (barTargetHeight > 0f)
            {
                topBar    = BuildBar("LetterboxTop", new Vector2(0.5f, 1f), barColor);
                bottomBar = BuildBar("LetterboxBottom", new Vector2(0.5f, 0f), barColor);
                SetBars(0f); // 初期は畳んでおく
            }

            if (wantTitle) BuildTitle(bossName, font, fontSize, titleColor, outlineColor);
        }

        // 画面幅いっぱいの黒帯。anchor は上端(0.5,1) or 下端(0.5,0)、pivot も同じにして端から伸ばす。
        private RectTransform BuildBar(string name, Vector2 edge, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, edge.y);
            rt.anchorMax = new Vector2(1f, edge.y);
            rt.pivot = edge;
            rt.sizeDelta = new Vector2(0f, 0f);
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private void BuildTitle(string bossName, TMP_FontAsset font, float fontSize, Color titleColor, Color outlineColor)
        {
            var groupGo = new GameObject("Title", typeof(RectTransform), typeof(CanvasGroup));
            groupGo.transform.SetParent(transform, false);
            var grt = (RectTransform)groupGo.transform;
            grt.anchorMin = new Vector2(0f, 0f);
            grt.anchorMax = new Vector2(1f, 0.28f); // 画面下寄り中央(ボス名の定番位置)
            grt.offsetMin = grt.offsetMax = Vector2.zero;

            titleGroup = groupGo.GetComponent<CanvasGroup>();
            titleGroup.alpha = 0f;
            titleGroup.interactable = false;
            titleGroup.blocksRaycasts = false;

            var tmpGo = new GameObject("BossName", typeof(RectTransform));
            tmpGo.transform.SetParent(groupGo.transform, false);
            var trt = (RectTransform)tmpGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;

            var tmp = tmpGo.AddComponent<TextMeshProUGUI>();
            tmp.text = bossName;
            if (font != null) tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = titleColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;

            var mat = tmp.fontMaterial; // アクセスした時点でインスタンス化される
            if (mat != null)
            {
                mat.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            }
        }

        /// <summary>レターボックスの開き具合を 0(閉)→1(全開) で設定する。</summary>
        public void SetBars(float t)
        {
            float h = barTargetHeight * Mathf.Clamp01(t);
            if (topBar != null)    topBar.sizeDelta = new Vector2(0f, h);
            if (bottomBar != null) bottomBar.sizeDelta = new Vector2(0f, h);
        }

        /// <summary>ボス名の表示アルファを 0-1 で設定する。</summary>
        public void SetTitleAlpha(float a)
        {
            if (titleGroup != null) titleGroup.alpha = Mathf.Clamp01(a);
        }

        /// <summary>破棄する。</summary>
        public void Dispose()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
