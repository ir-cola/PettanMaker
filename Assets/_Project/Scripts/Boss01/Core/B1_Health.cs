using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Boss01 固有の HP コンポーネント。IHealth を直接実装する。
/// EnemyHealth / EnemyDeath に依存しない。
/// </summary>
public class B1_Health : MonoBehaviour, IEnemy
{
    private int maxHP;

    [Header("HP バー UI")]
    [FormerlySerializedAs("healthBackgroundTexture")]
    [SerializeField] private Sprite healthBackgroundSprite;
    [FormerlySerializedAs("healthFillTexture")]
    [SerializeField] private Sprite healthFillSprite;
    [SerializeField] private float barWidth  = 400f;
    [SerializeField] private float barHeight = 30f;
    [SerializeField] private float marginTop = 20f;

    [Header("HP ダメージ残像")]
    [Tooltip("被弾範囲を薄色で残してから、完全に削れ始めるまでの時間（秒）。")]
    [SerializeField, Min(0f)] private float damageTrailDelay = 0.25f;
    [Tooltip("薄色の残像ゲージが完全に削れるまでの時間（秒）。")]
    [SerializeField, Min(0.01f)] private float damageTrailDuration = 0.5f;
    [Tooltip("削られた範囲に一時表示する薄色。Alphaを下げるほど薄く見える。")]
    [SerializeField] private Color damageTrailColor = new(1f, 1f, 1f, 0.45f);

    [Header("HP バー上部テキスト")]
    [SerializeField] private Font healthLabelFont;
    [SerializeField] private string healthLabelText = "BOSS";
    [SerializeField, Min(1)] private int healthLabelFontSize = 32;
    [SerializeField] private Color healthLabelColor = Color.white;
    [SerializeField] private FontStyle healthLabelFontStyle = FontStyle.Bold;
    [SerializeField, Min(0f)] private float healthLabelSpacing = 8f;

    private int  currentHP;
    private bool isDead;
    private bool invulnerable;

    private B1_HealthUI healthUI;

    // IHealth
    public int  CurrentHP => currentHP;
    public int  MaxHP     => maxHP;
    public bool IsDead    => isDead;

    // B1_HpKS が参照
    public float HpRatio => maxHP > 0 ? (float)currentHP / maxHP : 0f;

    /// <summary>カメラ演出中など、ダメージを一時的に無効化したいときに使う。</summary>
    public bool Invulnerable { get => invulnerable; set => invulnerable = value; }

    public UnityEvent<int> onHPChanged = new();
    public UnityEvent      onDied      = new();
    /// <summary>クリティカルヒット発生時（引数 = 倍率適用後の最終ダメージ）。演出側が購読する。</summary>
    public UnityEvent<int> onCriticalHit = new();

    // クリティカル設定（B1_Boss が Settings から注入する。未設定なら発生しない）
    private float critChance;
    private float critMultiplier = 1f;

    /// <summary>クリティカルの確率・倍率を設定する（B1_Settings.Critical 由来）。</summary>
    public void ConfigureCritical(float chance, float multiplier)
    {
        critChance     = Mathf.Clamp01(chance);
        critMultiplier = Mathf.Max(1f, multiplier);
    }

    /// <summary>難易度解決済みの B1_Settings.MaxHP で初期化する。</summary>
    public void Initialize(int newMaxHP)
    {
        maxHP = Mathf.Max(1, newMaxHP);
        currentHP    = maxHP;
        isDead       = false;
        EnsureHealthUI();
    }

    public void TakeDamage(int amount)
    {
        if (isDead || invulnerable) return;

        // クリティカル抽選。倍率を掛けた最終ダメージで onCriticalHit を先に発火し、
        // 演出側（強ヒットストップ等）が通常被弾の短いヒットストップより先に走れるようにする。
        if (critChance > 0f && amount > 0 && Random.value < critChance)
        {
            amount = Mathf.CeilToInt(amount * critMultiplier);
            onCriticalHit?.Invoke(amount);
        }

        currentHP    = Mathf.Max(0, currentHP - amount);
        onHPChanged?.Invoke(currentHP);
        if (currentHP <= 0) Die();
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;
        onDied?.Invoke();
        Debug.Log("[B1_Health] 死亡！");
    }

    private void EnsureHealthUI()
    {
        if (healthUI == null) healthUI = GetComponent<B1_HealthUI>();
        if (healthUI == null) healthUI = gameObject.AddComponent<B1_HealthUI>();
        healthUI.Configure(
            this,
            healthBackgroundSprite,
            healthFillSprite,
            new Vector2(barWidth, barHeight),
            marginTop,
            damageTrailDelay,
            damageTrailDuration,
            damageTrailColor,
            healthLabelFont,
            healthLabelText,
            healthLabelFontSize,
            healthLabelColor,
            healthLabelFontStyle,
            healthLabelSpacing);
    }
}
