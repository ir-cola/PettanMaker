/*=============================================================================

 File   : [B2_BossHPBarReveal.cs]
 Desc   : ボスHPバーの出し方を制御する。BossHP オブジェクトにアタッチする。

          開始時は隠しておき、Reveal() でフェードインする。
          Reveal() は B2_Boss.OnIntroComplete が呼ぶので、
          「登場カットが終わる → HUDがシールで貼られる → HPバーがフェードで出る」
          の順になる。

          ■ なぜ HUD の非表示に乗れないのか
            HUDIntroReveal は "Canvas/HUD" 配下だけを隠す。BossHP は
            Canvas 直下(HUD の外)にあるため対象外で、放っておくと
            カットシーン中から出っぱなしになる。かといって HUD の子にすると
            シール貼り演出の対象に含まれてしまい、バーが札として飛んでくる。
            そこで HPバーだけ独立してフェードさせる。

 ------------------------------------------------------------------------------

 Date   : 2026/07/16
 Author : akatu

===============================================================================*/

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Boss02
{
    /// <summary>ボスHPバーを開始時に隠し、Reveal() でフェードインさせる。</summary>
    public class B2_BossHPBarReveal : MonoBehaviour
    {
        [Header("表示")]
        [Tooltip("ONなら開始時に隠しておく。OFFなら最初から出っぱなし(演出無し)")]
        [SerializeField] private bool hideOnAwake = true;
        [Tooltip("Reveal() が呼ばれてからフェードを始めるまでの待ち(秒)。\n" +
                 "HUDのシール貼り演出(約1.7秒)が終わるのを待つための値")]
        [SerializeField] private float delay = 1.8f;
        [Tooltip("フェードインにかける時間(秒)")]
        [SerializeField] private float fadeDuration = 0.5f;

        [Header("通知")]
        [Tooltip("delay を消化してフェードが始まる瞬間に呼ばれる。\n" +
                 "ボスBGMを『HPバーが出るのと同時』に鳴らすための同期点。\n" +
                 "Reveal() が呼ばれた時点ではまだ待ちがあるので、そこでは早すぎる")]
        public UnityEvent onRevealBegan;

        private CanvasGroup group;
        private Coroutine co;

        private void Awake()
        {
            EnsureGroup();
            if (hideOnAwake) group.alpha = 0f;
        }

        /// <summary>HPバーをフェードで出す。B2_Boss.OnIntroComplete から呼ばれる。</summary>
        [ContextMenu("HPバーを出す")]
        public void Reveal()
        {
            EnsureGroup();
            if (co != null) StopCoroutine(co);
            co = StartCoroutine(RevealRoutine());
        }

        /// <summary>フェードせず即座に隠す(やり直し用)。</summary>
        public void HideInstant()
        {
            EnsureGroup();
            if (co != null) { StopCoroutine(co); co = null; }
            group.alpha = 0f;
        }

        private IEnumerator RevealRoutine()
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            // 待ちが明けてフェードが始まる = 見た目上バーが出始める瞬間。ここで通知する。
            onRevealBegan?.Invoke();

            float dur = Mathf.Max(0.01f, fadeDuration);
            float from = group.alpha;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, 1f, Mathf.Clamp01(t / dur));
                yield return null;
            }
            group.alpha = 1f;
            co = null;
        }

        private void EnsureGroup()
        {
            if (group != null) return;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }
    }
}
