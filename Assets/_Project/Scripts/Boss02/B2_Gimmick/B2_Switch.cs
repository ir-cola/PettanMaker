/*=============================================================================

 File   : [B2_Switch.cs]
 Desc   : プレイヤーの攻撃で作動するスイッチギミック。
          IEnemy を実装することで、PlayerAttack の OverlapSphere +
          GetComponentInParent<IEnemy>() にそのまま拾われ、殴られると
          TakeDamage が呼ばれる(敵と同じ経路なのでヒットストップ等も効く)。
          HP が 0 になった時点で onActivated を発火する。
            - maxHealth = 1 (既定) なら 1 発で作動
            - 複数発必要にしたいなら maxHealth を増やす
          作動後は IsDead = true になるため、PlayerAttack 側で以降の攻撃は
          スキップされ、二重発火しない。

          配線例(Inspector):
            onActivated → B2_Chandelier.Drop()

 ------------------------------------------------------------------------------

 Date   : 2026/07/15
 Author : akatu

===============================================================================*/

using UnityEngine;
using UnityEngine.Events;

namespace Boss02
{
    /// <summary>プレイヤーの攻撃で作動し、onActivated を発火するスイッチ。</summary>
    public class B2_Switch : MonoBehaviour, IEnemy
    {
        [Header("作動条件")]
        [Tooltip("作動までに必要な体力。1 なら一撃で作動する")]
        [SerializeField] private int maxHealth = 1;

        [Header("演出")]
        [SerializeField] private AudioSource seSource;
        [Tooltip("殴られた(まだ作動しない)ときのSE")]
        [SerializeField] private AudioClip hitSe;
        [Tooltip("作動した瞬間のSE")]
        [SerializeField] private AudioClip activateSe;
        [Tooltip("作動時に生成するエフェクト")]
        [SerializeField] private GameObject activateVfxPrefab;
        [Tooltip("作動後に切り替える見た目(押し込まれた状態など)。未設定なら何もしない")]
        [SerializeField] private GameObject idleVisual;
        [SerializeField] private GameObject activatedVisual;

        [Header("イベント")]
        [Tooltip("作動した瞬間に呼ぶ。シャンデリアの Drop() をここへ配線する")]
        public UnityEvent onActivated;

        private int currentHealth;
        private bool activated;
        private bool initialized;

        // IHealth
        public int CurrentHP => currentHealth;
        public int MaxHP => maxHealth;
        // 作動済み = これ以上殴っても反応しない。PlayerAttack は IsDead を先に見てスキップする。
        // HP ではなくフラグで判定する: 出現前(非アクティブ)は Awake が走っておらず
        // currentHealth が 0 のままなので、HP 判定だと未作動なのに IsDead になってしまう。
        public bool IsDead => activated;

        /// <summary>作動済みか(IsDead と同義。読みやすさ用)</summary>
        public bool IsActivated => activated;

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            currentHealth = Mathf.Max(1, maxHealth);
            SetVisual(false);
        }

        /// <summary>プレイヤーの攻撃から呼ばれる。HP が尽きた瞬間に作動する。</summary>
        public void TakeDamage(int amount)
        {
            if (activated) return;
            Initialize();

            currentHealth = Mathf.Max(0, currentHealth - Mathf.Max(1, amount));

            if (currentHealth > 0)
            {
                PlaySe(hitSe);
                return;
            }

            Activate();
        }

        /// <summary>体力に関係なく即作動させる(デバッグ/他ギミックからの連動用)。</summary>
        [ContextMenu("強制作動")]
        public void Activate()
        {
            if (activated) return;
            Initialize();
            activated = true;
            currentHealth = 0;
            PlaySe(activateSe);
            if (activateVfxPrefab != null) Instantiate(activateVfxPrefab, transform.position, transform.rotation);
            SetVisual(true);
            onActivated?.Invoke();
        }

        /// <summary>
        /// 未作動の状態に戻す。B2_ChandelierGimmick が次のクールタイムに入るときに呼ぶ。
        /// </summary>
        public void ResetSwitch()
        {
            initialized = true;
            activated = false;
            currentHealth = Mathf.Max(1, maxHealth);
            SetVisual(false);
        }

        private void SetVisual(bool activated)
        {
            if (idleVisual != null) idleVisual.SetActive(!activated);
            if (activatedVisual != null) activatedVisual.SetActive(activated);
        }

        private void PlaySe(AudioClip clip)
        {
            if (seSource != null && clip != null) seSource.PlayOneShot(clip);
        }
    }
}
