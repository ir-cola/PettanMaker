/*=============================================================================

 File   : [B2_Letterbox.cs]
 Desc   : 画面の上下に黒帯(レターボックス)を出す単独コンポーネント。
          Show() で開き、Hide() で閉じる。帯はランタイムに生成する。

          ■ なぜ B2_QueenIntroCamera から独立させたか
            B2_QueenIntroCamera も内部に帯を持っているが、それは自分の
            トラックが始まった瞬間(OnBegin)にしか出せない。カットシーンの
            途中(2カット目)から帯を出したい場合、そのトラックはまだ
            再生されていないので間に合わない。
            そこで帯だけを切り出し、任意のトラックの onBegin から
            Show() を呼べるようにした。
            ※ B2_QueenIntroCamera 側の letterboxHeight は 0 にして
              帯が二重に出ないようにすること(タイトル表示はそのまま使える)

          配線例(Inspector):
            Shot2_Approach の onBegin → B2_Letterbox.Show()
          閉じるのは B2_Boss.OnIntroComplete() が自動で行う
          (プレハブからシーンのオブジェクトは参照できないため、
           あちらは FindObjectOfType で探して Hide() を呼ぶ)

 ------------------------------------------------------------------------------

 Date   : 2026/07/16
 Author : akatu

===============================================================================*/

using UnityEngine;
using UnityEngine.UI;

namespace Boss02
{
    /// <summary>画面上下の黒帯。Show()/Hide() で開閉する。</summary>
    public class B2_Letterbox : MonoBehaviour
    {
        [Header("見た目")]
        [Tooltip("帯の色")]
        [SerializeField] private Color barColor = Color.black;
        [Tooltip("帯1本の高さ(画面高に対する割合 0-0.5)")]
        [SerializeField, Range(0f, 0.5f)] private float barHeight = 0.12f;
        [Tooltip("開閉にかかる時間(秒)")]
        [SerializeField] private float fadeDuration = 0.4f;
        [Tooltip("描画順。シーンのUIより手前に出したいので大きめにする")]
        [SerializeField] private int sortingOrder = 29000;

        private RectTransform topBar;
        private RectTransform bottomBar;
        private float current;   // 今の開き具合 0-1
        private float target;    // 目標の開き具合 0-1
        private bool built;

        /// <summary>帯が開いているか(開き途中も含む)</summary>
        public bool IsOpen => target > 0f;

        /// <summary>帯を開く。任意の CinemaTrack の onBegin から呼ぶ。</summary>
        [ContextMenu("帯を出す")]
        public void Show()
        {
            EnsureBuilt();
            target = 1f;
        }

        /// <summary>帯を閉じる。演出完了時に呼ぶ。</summary>
        [ContextMenu("帯を閉じる")]
        public void Hide() => target = 0f;

        /// <summary>フェードせず即座に閉じる。</summary>
        public void HideInstant()
        {
            target = 0f;
            current = 0f;
            Apply();
        }

        private void Update()
        {
            if (!built) return;
            if (Mathf.Approximately(current, target)) return;

            // 演出中は Time.timeScale を落とすこともあるので unscaled で動かす
            current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeDuration));
            Apply();
        }

        private void Apply()
        {
            float h = 1080f * barHeight * current;   // 参照解像度1080基準
            if (topBar != null)    topBar.sizeDelta    = new Vector2(0f, h);
            if (bottomBar != null) bottomBar.sizeDelta = new Vector2(0f, h);
        }

        // 初回 Show() まで Canvas を作らない(使わないシーンで無駄なUIを増やさない)
        private void EnsureBuilt()
        {
            if (built) return;
            built = true;

            var go = new GameObject("B2_LetterboxCanvas");
            go.transform.SetParent(transform, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // 高さ基準(帯は縦の割合が正義)
            go.AddComponent<GraphicRaycaster>().enabled = false;

            topBar    = BuildBar(go.transform, "LetterboxTop", new Vector2(0.5f, 1f));
            bottomBar = BuildBar(go.transform, "LetterboxBottom", new Vector2(0.5f, 0f));
            current = 0f;
            Apply();
        }

        // 画面幅いっぱいの帯。anchor/pivot を上端 or 下端に置いて端から伸ばす
        private RectTransform BuildBar(Transform parent, string name, Vector2 edge)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, edge.y);
            rt.anchorMax = new Vector2(1f, edge.y);
            rt.pivot = edge;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = barColor;
            img.raycastTarget = false;
            return rt;
        }
    }
}
