using UnityEngine;
using UnityEngine.Events;

namespace Boss02
{
    public class B2_Health : MonoBehaviour, IEnemy
    {
        [Header("ボスHP(土台と共通)")]
        [Tooltip("ボスの最大HP。土台の破壊もこのHPを基準にする。ここでHPを調整する")]
        [SerializeField] private int maxHealth = 100;
        [Tooltip("HPがこの割合を下回ると土台が壊れる(0.5=HPが半分で土台破壊)。\n" +
                 "土台が壊れた後が第二形態(土台なし状態)")]
        [SerializeField] private float baseHealthRatio = 0.5f;
        [SerializeField] private Boss_HPDisplay hPUI;

        [Header("通知")]
        [Tooltip("HPが0になった瞬間に1回だけ呼ぶ。B2_Boss が購読して無限ダウンへ移行する。\n" +
                 "SkillGaugeReward 等も購読する(撃破でゲージ加算)。\n" +
                 "※ここでボスを消さないこと。とどめは B2_FinishingBlow のムービーが刺す")]
        public UnityEvent onDied;
        [Tooltip("ダメージを受けるたびに呼ぶ(現在HP, 最大HP)。被弾リアクション用")]
        public HPChangedEvent onHPChanged;

        [System.Serializable]
        public class HPChangedEvent : UnityEvent<int, int> { }

        private int currentHealth;
        private B2_Boss boss;
        bool isBaseBreaked = false;
        private bool died = false;

        public int MaxHealth => maxHealth;

        /// <summary>
        /// true の間ダメージを一切受けない。登場演出中やとどめムービー中に立てる。
        /// </summary>
        public bool Invulnerable { get; set; }

        // IHealth
        public int CurrentHP => currentHealth;
        public int MaxHP     => maxHealth;
        public bool IsDead   => currentHealth <= 0;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            currentHealth = maxHealth;
            isBaseBreaked = false;
            died = false;
            hPUI?.UpdateHealthDisplay(currentHealth, maxHealth);
            boss = GetComponentInParent<B2_Boss>();
            if (boss == null)
                Debug.LogError("B2_Health は B2_Boss と同じ(または子の)GameObject に付ける必要があります。");
            DebugOverlay.Register(GetInstanceID(), "Boss02", () => $"HP  {currentHealth} / {maxHealth}{(IsDead ? "  [DEAD]" : "")}");
        }

        private void OnDestroy() => DebugOverlay.Unregister(GetInstanceID());

        public void TakeDamage(int amount)
        {
            // HP0後も素通りさせると、死亡後に土台破壊やスタンが発火してしまう。
            // Destroy は遅延実行なので「消えるまでの間に殴られる」も起こりうる。
            if (IsDead || Invulnerable) return;

            currentHealth = Mathf.Max(0, currentHealth - amount);
            onHPChanged?.Invoke(currentHealth, maxHealth);
            hPUI?.UpdateHealthDisplay(currentHealth, maxHealth);

            if (currentHealth == 0)
            {
                // ここでは消さない。無限ダウンに入って、とどめのムービーを待つ
                if (!died)
                {
                    died = true;
                    onDied?.Invoke();
                }
                return;
            }

            if (currentHealth <= maxHealth * baseHealthRatio && !isBaseBreaked)
            {
                hPUI?.ImageColorChange(Color.red);
                isBaseBreaked = true;
                // 土台破壊 → スタン → 土台なし状態(第二形態)、という流れはシャンデリアと共通。
                // 既にシャンデリアで壊れていた場合は B2_Boss 側のガードで何も起きない。
                boss.BreakBaseAndStun();
            }
        }
    }
}
