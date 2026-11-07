/*=============================================================================

 File   : [B2_BossNameDisplay.cs]
 Desc   : 女王のボス名表示。用意されたロゴ画像(Logo_Queen)を、チェシャ猫の
          名前演出(B1_CinematicOverlay)と同じ見せ方(ポップイン+フェード)で
          画面へ重ねる。

          以前は B1_CinematicOverlay(TMPテキスト)を使っていたが、
          ロゴ画像が用意されたため画像表示へ置き換えた。
          画像のインポートが Default(非Sprite)のままでも使えるよう、
          Image ではなく RawImage で描く。アスペクト比はテクスチャの
          実寸から計算して維持する。

          Show()             : ロゴを表示(同時に上下の黒帯を閉じる)
          FadeOutAndDispose(): ロゴをフェードで消す

          配線(変更なし):
            Shot4_Reveal(全体を映す引き)の onBegin → Show()
            B2_QueenIntroCamera が titleHideProgress 到達で FadeOutAndDispose()

 ------------------------------------------------------------------------------

 Date   : 2026/07/16
 Author : akatu

===============================================================================*/

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Boss02
{
    /// <summary>ボス名ロゴ画像を表示する(ポップイン+フェード、チェシャ猫の演出準拠)。</summary>
    public class B2_BossNameDisplay : MonoBehaviour
    {
        [Header("ロゴ画像")]
        [Tooltip("ボス名のロゴ(Logo_Queen)。インポートが Default のままでも表示できる")]
        [SerializeField] private Texture logoTexture;
        [Tooltip("ロゴの横幅(1920x1080基準のピクセル)。高さは画像の縦横比から自動")]
        [SerializeField] private float logoWidth = 700f;

        [Header("位置")]
        [Tooltip("画面上の中心位置(ビューポート 0..1)。(0.5, 0.18)=下寄り中央")]
        [SerializeField] private Vector2 anchor = new Vector2(0.5f, 0.18f);
        [Tooltip("ピクセル単位の追加オフセット(x=右, y=上)。1920x1080基準")]
        [SerializeField] private Vector2 pixelOffset = Vector2.zero;

        [Header("ポップイン(チェシャ猫と同じ演出)")]
        [Tooltip("表示時に大きめから弾んで出る")]
        [SerializeField] private bool popIn = true;
        [Tooltip("ポップイン開始スケール(オーバーシュート量)")]
        [SerializeField] private float popInScale = 1.35f;
        [Tooltip("ポップインに掛ける時間(秒)")]
        [SerializeField] private float popInDuration = 0.35f;

        [Header("フェード")]
        [SerializeField] private float fadeInTime = 0.25f;
        [SerializeField] private float fadeOutTime = 0.4f;
        [Tooltip("Show() と同時に上下の黒帯(B2_Letterbox)を閉じる")]
        [SerializeField] private bool hideLetterboxOnShow = true;

        [Header("描画順")]
        [SerializeField] private int sortingOrder = 30000;

        private GameObject canvasGo;
        private RawImage image;
        private RectTransform imageRect;
        private Coroutine animCo;

        /// <summary>ロゴを表示する。カメラが全体へ引くショットの onBegin から呼ぶ。</summary>
        public void Show()
        {
            if (canvasGo != null) return;

            // 「枠を消すのと同時に名前を出す」の枠側。
            // (帯はシーン側の B2_Letterbox が持つ。プレハブから参照できないため型で探す)
            if (hideLetterboxOnShow)
            {
                B2_Letterbox letterbox = FindObjectOfType<B2_Letterbox>();
                if (letterbox != null) letterbox.Hide();
            }

            if (logoTexture == null)
            {
                Debug.LogWarning("[B2_BossNameDisplay] Logo Texture が未設定です。名前は出ません。", this);
                return;
            }

            Build();
            if (animCo != null) StopCoroutine(animCo);
            animCo = StartCoroutine(ShowRoutine());
        }

        /// <summary>ロゴをフェードで消す。B2_QueenIntroCamera が titleHideProgress で呼ぶ。</summary>
        public void FadeOutAndDispose()
        {
            if (canvasGo == null) return;
            if (animCo != null) StopCoroutine(animCo);
            animCo = StartCoroutine(FadeOutRoutine());
        }

        private void Build()
        {
            canvasGo = new GameObject("B2_BossNameLogo");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>().enabled = false;

            var imgGo = new GameObject("Logo", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imgGo.transform.SetParent(canvasGo.transform, false);

            imageRect = (RectTransform)imgGo.transform;
            imageRect.anchorMin = imageRect.anchorMax = anchor;
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            // 高さは画像の実寸から縦横比を守って算出(RawImage には preserveAspect が無い)
            float aspect = logoTexture.width > 0 ? (float)logoTexture.height / logoTexture.width : 0.5625f;
            imageRect.sizeDelta = new Vector2(logoWidth, logoWidth * aspect);
            imageRect.anchoredPosition = pixelOffset;

            image = imgGo.GetComponent<RawImage>();
            image.texture = logoTexture;
            image.raycastTarget = false;
            SetAlpha(0f);
        }

        // フェードイン + ポップイン(大きめから弾んで定位置へ)
        private IEnumerator ShowRoutine()
        {
            float dur = Mathf.Max(fadeInTime, popIn ? popInDuration : 0f, 0.01f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;

                float ka = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeInTime));
                SetAlpha(ka);

                if (popIn && imageRect != null)
                {
                    float kp = Mathf.Clamp01(t / Mathf.Max(0.01f, popInDuration));
                    // easeOutBack 風: 行き過ぎてから戻る「ポンッ」
                    float e = 1f + (kp - 1f) * (kp - 1f) * (kp - 1f) * (1.70158f + 1f) + (kp - 1f) * (kp - 1f) * 1.70158f;
                    imageRect.localScale = Vector3.one * Mathf.LerpUnclamped(popInScale, 1f, e);
                }
                yield return null;
            }
            SetAlpha(1f);
            if (imageRect != null) imageRect.localScale = Vector3.one;
        }

        private IEnumerator FadeOutRoutine()
        {
            float dur = Mathf.Max(0.01f, fadeOutTime);
            float t = 0f;
            float from = image != null ? image.color.a : 1f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, 0f, Mathf.Clamp01(t / dur)));
                yield return null;
            }
            Dispose();
        }

        private void SetAlpha(float a)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = a;
            image.color = c;
        }

        private void Dispose()
        {
            if (canvasGo != null) { Destroy(canvasGo); canvasGo = null; }
            image = null;
            imageRect = null;
        }

        // 演出中に無効化されても表示を残さない
        private void OnDisable() => Dispose();
    }
}
