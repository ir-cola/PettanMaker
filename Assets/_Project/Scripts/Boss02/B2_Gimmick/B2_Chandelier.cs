/*=============================================================================

 File   : [B2_Chandelier.cs]
 Desc   : 天井から落下するシャンデリアギミック。
          Drop() を呼ぶと落下を開始し、着弾で onImpact を発火する。
          駆動は Rigidbody(重力)。待機中は isKinematic で吊るしておき、
          Drop() で解除して自然落下させる。Rigidbody が無い場合は
          fallbackFallSpeed による直線落下にフォールバックする。

          着弾判定は OnCollisionEnter。impactLayers で拾う相手を絞る
          (既定は全レイヤー)。万一何にも当たらなかった場合の保険として
          maxFallTime 経過で強制的に着弾扱いにする。

          使用後は ResetToCeilingRoutine() で復帰する:
            着弾したまま少し見せる → 落ちた本体をフェードアウト →
            開始時の天井位置へ吊り直す → フェードイン → 再び Drop() 可能
          フェードは CameraOcclusionSystem と同じ作法で、Toon 系シェーダーの
          _OcclusionFade (0=不透明, 1=完全透過) を MaterialPropertyBlock 経由で
          Renderer 単位に書き込む(マテリアルは共有のまま)。
          → シャンデリアのマテリアルが _OcclusionFade を持たないシェーダーだと
            フェードは効かず、パッと消えて出る見た目になる。

          配線例(Inspector):
            onImpact → B2_Boss.HitByChandelier()
              土台が無事なら「土台破壊 + 大幅スタン」、
              破壊済みなら「ダメージ + 大幅スタンの半分」になる(判定はボス側)。
          (Drop() と復帰は B2_ChandelierGimmick が自動で呼ぶ)

 ------------------------------------------------------------------------------

 Date   : 2026/07/15
 Author : akatu

===============================================================================*/

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Boss02
{
    /// <summary>スイッチ全作動で Drop() され、着弾で onImpact を発火するシャンデリア。</summary>
    public class B2_Chandelier : MonoBehaviour
    {
        // CameraOcclusionSystem と同じディザ透過プロパティ(0=不透明, 1=完全透過)
        private static readonly int OcclusionFadeId = Shader.PropertyToID("_OcclusionFade");

        [Header("落下")]
        [Tooltip("落下に使う Rigidbody。未設定なら自身から取得。無ければ直線落下する")]
        [SerializeField] private Rigidbody body;
        [Tooltip("Rigidbody が無いときの落下速度(m/s)")]
        [SerializeField] private float fallbackFallSpeed = 12f;
        [Tooltip("何にも当たらなかった場合に強制着弾するまでの時間(秒)。保険")]
        [SerializeField] private float maxFallTime = 5f;

        [Header("着弾判定")]
        [Tooltip("着弾とみなすレイヤー。既定は全部")]
        [SerializeField] private LayerMask impactLayers = ~0;

        [Header("復帰(フェード)")]
        [Tooltip("着弾してから消え始めるまでの時間(秒)。落ちた姿を見せる間")]
        [SerializeField] private float resetDelay = 1.5f;
        [Tooltip("落ちた本体が消えるまでの時間(秒)")]
        [SerializeField] private float fadeOutDuration = 0.6f;
        [Tooltip("天井に吊り直してから現れるまでの時間(秒)")]
        [SerializeField] private float fadeInDuration = 0.8f;
        [Tooltip("フェード対象の Renderer。未設定なら子から自動取得")]
        [SerializeField] private Renderer[] fadeRenderers;

        [Header("演出")]
        [SerializeField] private AudioSource seSource;
        [Tooltip("落下開始時のSE (Boss02_Chandelier_Fall)")]
        [SerializeField] private AudioClip fallSe;
        [Tooltip("着弾時のSE (Boss02_Chandelier_Break)")]
        [SerializeField] private AudioClip breakSe;
        [Tooltip("着弾時に生成するエフェクト")]
        [SerializeField] private GameObject breakVfxPrefab;
        [Tooltip("着弾後に切り替える壊れた見た目。未設定なら見た目はそのまま")]
        [SerializeField] private GameObject intactVisual;
        [SerializeField] private GameObject brokenVisual;

        [Header("イベント")]
        [Tooltip("着弾した瞬間に呼ぶ。B2_Boss.HitByChandelier() をここへ配線する")]
        public UnityEvent onImpact;
        [Tooltip("天井に吊り直して再び落とせるようになった瞬間に呼ぶ")]
        public UnityEvent onReady;

        private bool falling;
        private bool impacted;
        private float fallElapsed;

        private Vector3 ceilingPosition;   // 開始時の位置(＝天井の吊り位置)
        private Quaternion ceilingRotation;
        private MaterialPropertyBlock mpb;

        public bool IsFalling => falling;
        public bool HasImpacted => impacted;
        /// <summary>今 Drop() を受け付けられるか</summary>
        public bool IsReady => !falling && !impacted;

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (fadeRenderers == null || fadeRenderers.Length == 0)
                fadeRenderers = GetComponentsInChildren<Renderer>(true);

            // 開始時の姿勢を天井の定位置として覚えておく(復帰先)
            ceilingPosition = transform.position;
            ceilingRotation = transform.rotation;

            Suspend();
            SetVisual(false);
            SetFade(0f);
            WarnIfFadeUnsupported();
        }

        // _OcclusionFade を持たないシェーダーだとフェードが無言で効かないので、気づけるようにする
        private void WarnIfFadeUnsupported()
        {
            foreach (Renderer r in fadeRenderers)
            {
                if (r == null || r.sharedMaterial == null) continue;
                if (r.sharedMaterial.HasProperty(OcclusionFadeId)) return; // 1つでも対応していれば良しとする
            }

            Debug.LogWarning(
                $"[B2_Chandelier] '{name}' のマテリアルが _OcclusionFade を持たないため、フェードが効きません" +
                "(パッと消えて出る見た目になります)。シェーダーを PettanMaker/Environment/OccludablePolka " +
                "などの _OcclusionFade 対応シェーダーに変更してください。", this);
        }

        /// <summary>落下を開始する。全スイッチ作動時に B2_ChandelierGimmick から呼ばれる。</summary>
        public void Drop()
        {
            if (falling || impacted) return;

            falling = true;
            fallElapsed = 0f;
            PlaySe(fallSe);

            if (body != null)
            {
                body.isKinematic = false;
                body.useGravity = true;
            }
        }

        private void Update()
        {
            if (!falling || impacted) return;

            fallElapsed += Time.deltaTime;

            // Rigidbody が無い構成では自前で落とす
            if (body == null)
                transform.position += Vector3.down * (fallbackFallSpeed * Time.deltaTime);

            // 何にも当たらなかったときの保険
            if (maxFallTime > 0f && fallElapsed >= maxFallTime) Impact();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!falling || impacted) return;
            // impactLayers に含まれない相手(空中の小物など)では割れない
            if ((impactLayers.value & (1 << collision.gameObject.layer)) == 0) return;
            Impact();
        }

        /// <summary>着弾処理。SE/エフェクト/見た目を切り替えて onImpact を発火する。</summary>
        public void Impact()
        {
            if (impacted) return;
            impacted = true;
            falling = false;

            if (body != null) Suspend(); // 着弾後に転がらないよう固定

            PlaySe(breakSe);
            if (breakVfxPrefab != null) Instantiate(breakVfxPrefab, transform.position, Quaternion.identity);
            SetVisual(true);

            onImpact?.Invoke();
        }

        /// <summary>
        /// 落ちた本体をフェードで消し、天井の定位置へ吊り直してフェードで戻す。
        /// 完了すると再び Drop() できる。B2_ChandelierGimmick が着弾後に回す。
        /// </summary>
        public IEnumerator ResetToCeilingRoutine()
        {
            // 落ちて壊れた姿を少し見せる
            if (resetDelay > 0f) yield return new WaitForSeconds(resetDelay);

            yield return FadeRoutine(0f, 1f, fadeOutDuration);

            // 透明なうちに天井へ吊り直して状態を戻す
            transform.SetPositionAndRotation(ceilingPosition, ceilingRotation);
            Suspend();
            falling = false;
            impacted = false;
            fallElapsed = 0f;
            SetVisual(false);

            yield return FadeRoutine(1f, 0f, fadeInDuration);

            onReady?.Invoke();
        }

        // 吊るした状態(物理停止)にする
        private void Suspend()
        {
            if (body == null) return;

            // 速度は kinematic にする前に落とす。kinematic な Rigidbody への
            // 速度書き込みは警告になるため。
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
        }

        private IEnumerator FadeRoutine(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                SetFade(to);
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                SetFade(Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            SetFade(to);
        }

        // 0=不透明, 1=完全透過。マテリアルは共有のまま Renderer 単位で書き込む
        private void SetFade(float value)
        {
            if (fadeRenderers == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            foreach (Renderer r in fadeRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(OcclusionFadeId, value);
                r.SetPropertyBlock(mpb);
            }
        }

        private void SetVisual(bool broken)
        {
            if (intactVisual != null) intactVisual.SetActive(!broken);
            if (brokenVisual != null) brokenVisual.SetActive(broken);
        }

        private void PlaySe(AudioClip clip)
        {
            if (seSource != null && clip != null) seSource.PlayOneShot(clip);
        }
    }
}
