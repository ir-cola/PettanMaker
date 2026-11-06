/*=============================================================================

 File   : [B2_ScreenFade.cs]
 Desc   : 画面全体の暗転。カットシーンの繋ぎに使う。
          FadeToBlack() で暗くし、FadeFromBlack() で戻す。
          全画面の板はランタイムに生成する(初回まで作らない)。

          使い所:
            プレイヤー着地 → 暗転 → カメラを演出開始位置へ飛ばす → 明転
          暗転中にカメラを飛ばせば、ワープが見えない。

          B2_Letterbox と同じ作りだが役割が別なので分けてある
          (帯は演出中ずっと出ているもの、暗転は繋ぎの一瞬だけのもの)。

 ------------------------------------------------------------------------------

 Date   : 2026/07/16
 Author : akatu

===============================================================================*/

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Boss02
{
    /// <summary>画面全体を暗転させる。コルーチンで待てる。</summary>
    public class B2_ScreenFade : MonoBehaviour
    {
        [Header("見た目")]
        [Tooltip("暗転の色")]
        [SerializeField] private Color fadeColor = Color.black;
        [Tooltip("描画順。レターボックスより手前に出す")]
        [SerializeField] private int sortingOrder = 31000;

        // 1フレームで進めるフェードの上限(秒)。
        // Play直後やシーン初期化の直後は unscaledDeltaTime が1秒を超えることがあり、
        // そのまま足すとフェードが1〜2フレームで完走して「一瞬暗くてパッと明るくなる」
        // 画になってしまう。カクついた分は進めず、その先のフレームで見せる。
        private const float MaxStepPerFrame = 0.05f;

        private Image image;
        private float current;   // 0=透明, 1=暗転
        private bool built;

        /// <summary>完全に暗転しているか</summary>
        public bool IsBlack => current >= 1f;

        /// <summary>フェードの色。暗転用(黒)と白転用(白)を見分けるために公開している</summary>
        public Color FadeColor => fadeColor;

        /// <summary>暗転する。yield return で待てる。</summary>
        public IEnumerator FadeToBlack(float duration) => FadeRoutine(1f, duration);

        /// <summary>明転する。yield return で待てる。</summary>
        public IEnumerator FadeFromBlack(float duration) => FadeRoutine(0f, duration);

        /// <summary>フェードせず即座に設定する(0=透明, 1=暗転)。</summary>
        public void SetInstant(float value)
        {
            EnsureBuilt();
            current = Mathf.Clamp01(value);
            Apply();
        }

        private IEnumerator FadeRoutine(float target, float duration)
        {
            EnsureBuilt();

            if (duration <= 0f)
            {
                current = target;
                Apply();
                yield break;
            }

            float from = current;
            float t = 0f;
            while (t < duration)
            {
                // 演出中に timeScale を落としても進むよう unscaled で回す。
                // カクついたフレームで飛ばないよう1フレームの進行量に上限を掛ける
                t += Mathf.Min(Time.unscaledDeltaTime, MaxStepPerFrame);
                current = Mathf.Lerp(from, target, Mathf.Clamp01(t / duration));
                Apply();
                yield return null;
            }
            current = target;
            Apply();
        }

        private void Apply()
        {
            if (image == null) return;
            Color c = fadeColor;
            c.a = current;
            image.color = c;
            // 完全に透明な間はレイキャストを通す(UI操作を邪魔しない)
            image.raycastTarget = false;
            image.enabled = current > 0.001f;
        }

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            var go = new GameObject("B2_ScreenFadeCanvas");
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>().enabled = false;

            var imgGo = new GameObject("Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imgGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            image = imgGo.GetComponent<Image>();
            Apply();
        }
    }
}
