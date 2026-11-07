/**===========================================================================

 File   : プレイヤーHPコンポーネント [PlayerHealth.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 プレイヤーのHP管理 (IHealth 実装)。ダメージ・回復・最大HP増加を扱い、
 HP変化を onHPChanged で UI に通知する。
 ===========================================================================**/

using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

public class PlayerHealth : MonoBehaviour, IHealth {

    [Header("設定アセット")]
    [Tooltip("HP・被弾設定 (ScriptableObject)。最大HP/無敵時間/ノックバックはこのアセットで調整する。未アサイン時はコード既定値で動作")]
    [SerializeField] private PlayerHealthSettings settings;

    [Header("被弾ヒットストップ")]
    [Tooltip("null なら自動取得/追加する。CombatSystem が DamageContext.targetHitstop で Freeze を呼ぶ。")]
    [SerializeField] private AnimationHitStop hitStop;

    [Header("デバッグ")]
    [Tooltip("デバッグ用無敵モード。ON の間はダメージを一切受けない。エディタ/開発ビルドでは F1 キーでも切り替え可")]
    [SerializeField] private bool debugInvincible = false;

    [Header("ゲームオーバーBGM")]
    [Tooltip("HPが0になったとき BGMManager.SetOverride で流すBGM。未設定なら切り替えない。")]
    [SerializeField] private AudioClip gameOverBgm;

    [Header("被弾SE")]
    [Tooltip("この値以上のダメージで「重」ダメージ音(Damage[1])、未満で「軽」(Damage[0])を鳴らす。")]
    [SerializeField] private int heavyDamageThreshold = 2;

    // チューニング値は PlayerHealthSettings アセットで調整する (Awake でアセット値に上書き)。
    // 以下はアセット未アサイン時のコード既定値。
    private int maxHP = 5;
    private float invincibleDuration = 1.0f;   // 被弾後の無敵時間 (秒)。PlayerHitReaction がモデルを点滅させる

    private int currentHP;
    private float invincibleUntil = -1f;
    private PlayerHitReaction hitReaction;
    private bool deathNotified;

    // HP変化通知 (UI連動用)
    public UnityEvent<int> onHPChanged;
    public UnityEvent onDied = new();

    public int CurrentHP => currentHP;
    public int MaxHP => maxHP;
    public PlayerHealthSettings Settings => settings;
    public bool IsDead => currentHP <= 0;
    // ノックバック(押し出し＋復帰待ち)中か。ロコモーションステートが移動入力をロックするために参照。
    public bool IsInKnockback => hitReaction != null && hitReaction.IsKnockingBack;
    // 被弾後の無敵中か。CombatSystem が被弾処理全体 (ダメージ/フラッシュ/ヒットストップ) をスキップする。
    // 固定の無敵時間に加え、ノックバックから復帰しきる (Locomotion へ戻る) までは無敵を継続する。
    // デバッグ無敵はここに含めない: HP減少のみ無効で、被弾リアクション (ノックバック/点滅) は通常通り再生する。
    public bool IsHitInvincible => Time.time < invincibleUntil || IsInKnockback;
    // デバッグ無敵 (Inspector / F1 トグル) 込みの無敵判定。ダメージ適用可否の最終保険用。
    public bool IsInvincible => debugInvincible || IsHitInvincible;

    private void Awake() {
        // 設定アセットの値を反映する (未アサイン時はコード既定値のまま動作)。
        if (settings != null) {
            maxHP              = settings.MaxHP;
            invincibleDuration = settings.InvincibleDuration;
        } else {
            Debug.LogWarning("[PlayerHealth] PlayerHealthSettings が未アサインです。コード既定値で動作します。", this);
        }

        currentHP = maxHP;
        if (hitStop == null)
            hitStop = GetComponent<AnimationHitStop>() ?? gameObject.AddComponent<AnimationHitStop>();
        // 被弾リアクション (ノックバック + 無敵中点滅) を自動構成
        hitReaction = GetComponent<PlayerHitReaction>();
        if (hitReaction == null)
            hitReaction = gameObject.AddComponent<PlayerHitReaction>();
    }

    private void Start() {
        // 初期HPをUIに通知
        onHPChanged?.Invoke(currentHP);
        DebugOverlay.Register(GetInstanceID(), "Player", () => $"HP  {currentHP} / {maxHP}{(debugInvincible ? "  [無敵]" : "")}");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void Update() {
        // デバッグ用: F1 で無敵モードをトグル (エディタ/開発ビルドのみ)
        if (Keyboard.current?.f1Key.wasPressedThisFrame ?? false) {
            debugInvincible = !debugInvincible;
            Debug.Log($"[PlayerHealth] デバッグ無敵: {(debugInvincible ? "ON" : "OFF")}", this);
        }
    }
#endif

    private void OnDestroy() => DebugOverlay.Unregister(GetInstanceID());

    public void TakeDamage(int amount) {
        // CombatSystem を通さない直接呼び出しに対する保険 (通常は CombatSystem 側で弾かれる)
        if (IsDead || IsHitInvincible) return;

        // デバッグ無敵中: HPは減らさず、被弾リアクション用の無敵時間 (点滅・連続被弾防止) だけ開始する
        if (debugInvincible) {
            GetComponent<Player_SoundManager>()?.PlayDamage(amount >= heavyDamageThreshold);
            invincibleUntil = Time.time + invincibleDuration;
            return;
        }

        int previousHP = currentHP;
        currentHP = Mathf.Max(0, currentHP - amount);
        onHPChanged?.Invoke(currentHP);

        if (currentHP < previousHP) {
            GetComponent<Player_SoundManager>()?.PlayDamage(amount >= heavyDamageThreshold);
            if (currentHP > 0)
                invincibleUntil = Time.time + invincibleDuration;
        }

        if (currentHP <= 0 && !deathNotified) {
            deathNotified = true;
            Debug.Log("Player Dead");
            onDied?.Invoke();
            if (gameOverBgm != null) BGMManager.SetOverride(gameOverBgm);
        }
    }

    /// <summary>
    /// 無敵終了時刻を「今から seconds 秒後」まで延長する (すでにそれより長ければ変えない)。
    /// ノックバック復帰直後に猶予ゼロで連続被弾しないよう、PlayerHitReaction が復帰時に呼ぶ。
    /// </summary>
    public void ExtendInvincibility(float seconds) {
        if (seconds <= 0f || IsDead) return;
        invincibleUntil = Mathf.Max(invincibleUntil, Time.time + seconds);
    }

    public void Heal(int amount) {
        if (IsDead) return;

        int previousHP = currentHP;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        onHPChanged?.Invoke(currentHP);

        if (currentHP > previousHP)
            GetComponent<Player_SoundManager>()?.PlayHeal();
    }

    public void IncreaseMaxHP(int amount) {
        if (amount <= 0) return;
        bool wasDead = IsDead;
        maxHP += amount;
        if (!wasDead)
            currentHP = Mathf.Min(maxHP, currentHP + amount);
        onHPChanged?.Invoke(currentHP);
    }
}
