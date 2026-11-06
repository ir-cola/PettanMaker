/**===========================================================================

 File   : プレイヤー攻撃コンポーネント (共有コア) [PlayerAttack.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29  (2026/7/14 肥大化対策で partial 3分割)
 -----------------------------------------------------------------------------
 武器（スタンプ）を生成して敵に当てる攻撃コンポーネント。肥大化回避のため
 partial で3ファイルに分割:
   PlayerAttack.cs        : 共有コア (スタンプ状態/命中処理/スカッシュ/消滅/フック)
   PlayerAttack.Combo.cs  : タップ3段コンボ (斜め斬撃 + 前進ステップ)
   PlayerAttack.Charge.cs : 長押しチャージ (弧軌道の叩きつけ + 着地予測)
 このファイルは両方が使う共有機構だけを持つ。
 ===========================================================================**/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class PlayerAttack : MonoBehaviour {

    public enum AttackType {
        Charge = 0,
        Release_Charge1,
        Release_Charge2,
        Release_Charge3
    }

    /**=======================================================================
      エフェクト連携用フック (static イベントを購読するだけでFXを差し込める)。
        OnStampSpawned : スタンプ生成の瞬間 / OnStampAppear : 出現演出開始 / OnStampLanded : 着地
      ※ static なので購読解除を忘れないこと。
    =======================================================================**/
    public struct StampEvent {
        public GameObject weapon;     // 対象スタンプの GameObject
        public Vector3 position;      // 発生 / 着地のワールド座標
        public Quaternion rotation;   // スタンプの飛翔方向 (生成時) / identity (着地時)
        public AttackType attackType; // どのチャージ段階か
        public int index;      // 同時生成の何個目か (0始まり)
        public int totalCount; // 今回生成する総数
        public int hitCount;   // 着地時のみ: この一撃で命中したエネミー数 (0=空振り)
    }

    /// <summary>エネミー1体への命中 (ヒットVFX/フラッシュ用)。</summary>
    public struct HitEvent {
        public Vector3 position;   // 命中位置 (対象コライダーのバウンズ中心)
        public int damage;
    }

    public static event System.Action<StampEvent> OnStampSpawned; // 生成時 (Instantiate 直後)
    public static event System.Action<StampEvent> OnStampAppear;  // 出現演出開始時
    public static event System.Action<StampEvent> OnStampLanded;  // 着地時
    public static event System.Action<HitEvent>   OnEnemyHit;     // エネミー1体命中ごと

    [Header("設定アセット")]
    [Tooltip("攻撃設定 (ScriptableObject)。ダメージ/コンボ/チャージ/アリス等のチューニング値はこのアセットで調整する。未アサイン時はコード既定値で動作")]
    [SerializeField] private PlayerAttackSettings settings;

    [Header("シーン参照 (アセット化対象外)")]
    [SerializeField] private GameObject weaponPrefab;           // 武器プレハブ
    [SerializeField] private LayerMask enemyLayer;

    // チューニング値は PlayerAttackSettings アセットで調整する (Awake でアセット値に上書き)。
    // 以下はアセット未アサイン時のコード既定値 (= Cheshire シーンの調整値)。
    private int attackDamage = 20;              // チャージ叩きつけのダメージ
    private float attackRadius = 2.0f;          // 叩きつけの効果半径 (予想サークル表示にも使う)
    // ヒット演出 (エネミー命中時のみ)
    private float hitGlobalHitstop = 0.09f;     // 命中時のグローバルヒットストップ時間 (秒)。0で無効
    private float hitGlobalHitstopScale = 0.06f;// 命中時ヒットストップ中の timeScale
    private float hitShakeDuration = 0.22f;
    private float hitShakeIntensity = 0.28f;
    // 着地スカッシュ
    private float impactSquashScaleXZ = 1.3f;
    private float impactSquashScaleY = 0.6f;
    private float impactSquashDuration = 0.2f;
    // 演出 (空振りシェイク / 消滅)
    private float shakeDuration = 0.15f;
    private float shakeIntensity = 0.10f;
    private float hitstopDurationEnemy = 0.08f;
    private float hitstopDurationSelf = 0.04f;
    private float destroyDelay = 0.5f;          // 着地後に武器を消去するまでの時間

    // ===== 共有状態 =====
    private bool isUIMode = false;
    private int activeWeaponCount = 0;      // スタンプが動作中の数 (コンボ/チャージ共通)
    private float lastImpactFxTime = -10f;  // 同フレーム多重着地のシェイク重複防止
    private int bonusAttackCount = 0;       // アイテム効果による追加スタンプ数
    private CharacterController characterController; // コンボ前進ステップ用 (root の CC)
    private PlayerAttackVFX attackVfx;      // 納品VFXパッケージの配線 (Awake で装着)
    private Player_SoundManager sound;

    // ===== 公開 API =====
    public float AttackRadius => attackRadius;
    public bool IsAttacking => activeWeaponCount > 0; // 召喚〜消え始めまで true
    public int BonusAttackCount => bonusAttackCount;
    public void AddBonusAttackCount(int delta) => bonusAttackCount = Mathf.Max(0, bonusAttackCount + delta);
    public void SetUIMode(bool uiMode) => isUIMode = uiMode;

    /// <summary>設定アセット (PlayerAttackSettings) の値をシーン上のフィールドへ反映する。
    /// プレハブ / Transform / LayerMask 等のシーン参照は対象外。</summary>
    private void ApplySettings() {
        attackDamage          = settings.AttackDamage;
        attackRadius          = settings.AttackRadius;
        hitGlobalHitstop      = settings.HitGlobalHitstop;
        hitGlobalHitstopScale = settings.HitGlobalHitstopScale;
        hitShakeDuration      = settings.HitShakeDuration;
        hitShakeIntensity     = settings.HitShakeIntensity;
        impactSquashScaleXZ   = settings.ImpactSquashScaleXZ;
        impactSquashScaleY    = settings.ImpactSquashScaleY;
        impactSquashDuration  = settings.ImpactSquashDuration;
        shakeDuration         = settings.ShakeDuration;
        shakeIntensity        = settings.ShakeIntensity;
        hitstopDurationEnemy  = settings.HitstopDurationEnemy;
        hitstopDurationSelf   = settings.HitstopDurationSelf;
        destroyDelay          = settings.DestroyDelay;
        // コンボ (PlayerAttack.Combo.cs)
        tapThreshold       = settings.TapThreshold;
        comboWindow        = settings.ComboWindow;
        comboMinInterval   = settings.ComboMinInterval;
        comboCooldown      = settings.ComboCooldown;
        comboStageDamages  = settings.ComboStageDamages;
        comboStageHitstops = settings.ComboStageHitstops;
        comboFlightTime    = settings.ComboFlightTime;
        slashHitRadius     = settings.SlashHitRadius;
        combo1SpawnDelay   = settings.Combo1SpawnDelay;
        combo2SpawnDelay   = settings.Combo2SpawnDelay;
        combo3SpawnDelay   = settings.Combo3SpawnDelay;
        comboStepDistance  = settings.ComboStepDistance;
        comboStepTime      = settings.ComboStepTime;
        comboLockRadius    = settings.ComboLockRadius;
        comboLockLinger    = settings.ComboLockLinger;
        comboLockStepMul   = settings.ComboLockStepMul;
        comboLockStopRange = settings.ComboLockStopRange;
        comboWindupPull    = settings.ComboWindupPull;
        comboZoomIn        = settings.ComboZoomIn;
        comboZoomFov       = settings.ComboZoomFov;
        comboZoomDur       = settings.ComboZoomDur;
        comboZoomReturn    = settings.ComboZoomReturn;
        // チャージ (PlayerAttack.Charge.cs)
        forwardOffset        = settings.ForwardOffset;
        heightOffset         = settings.HeightOffset;
        sideOffset           = settings.SideOffset;
        betweenWeaponsOffset = settings.BetweenWeaponsOffset;
        maxChargeTime        = settings.MaxChargeTime;
        // ReleaseThresholds は [tap, mid, full] の3段前提で StateAttack が [1]/[2] を参照する。
        // 設定アセットが3要素未満でも IndexOutOfRange にならないよう長さ3を保証する
        // （正しく設定された値はそのまま使い、不足分のみ既定値で補う）。
        {
            var src = settings.ReleaseThresholds;
            if (src != null && src.Length >= 3) {
                releaseThresholds = src;
            } else {
                var def = new float[] { 0f, 0.7f, 1.6f };
                releaseThresholds = new float[3];
                for (int i = 0; i < 3; i++)
                    releaseThresholds[i] = (src != null && i < src.Length) ? src[i] : def[i];
                Debug.LogWarning("PlayerAttackSettings.ReleaseThresholds の要素数が3未満のため既定値で補完しました", settings);
            }
        }
        charge1Cooldown      = settings.Charge1Cooldown;
        slamSpeed            = settings.SlamSpeed;
        slamGravity          = settings.SlamGravity;
        slamWaitTime         = settings.SlamWaitTime;
        arcStagger           = settings.ArcStagger;
        arcFlightTime        = settings.ArcFlightTime;
        convergeSpread       = settings.ConvergeSpread;
        convergeHeight       = settings.ConvergeHeight;
        convergeJitter       = settings.ConvergeJitter;
        arcRise              = settings.ArcRise;
        // アリス (PlayerAttack.Alice.cs)
        chibiScaleMul       = settings.ChibiScaleMul;
        chibiHeight         = settings.ChibiHeight;
        chibiForward        = settings.ChibiForward;
        chibiAppearTime     = settings.ChibiAppearTime;
        chibiFallSpeed      = settings.ChibiFallSpeed;
        chibiFallGravity    = settings.ChibiFallGravity;
        giantScaleMul       = settings.GiantScaleMul;
        giantHeight         = settings.GiantHeight;
        giantForward        = settings.GiantForward;
        giantAppearTime     = settings.GiantAppearTime;
        giantSlamTime       = settings.GiantSlamTime;
        giantRadius         = settings.GiantRadius;
        giantDamagePerHit   = settings.GiantDamagePerHit;
        giantFirstHitDamage = settings.GiantFirstHitDamage;
        giantHitCount       = settings.GiantHitCount;
        giantHitInterval    = settings.GiantHitInterval;
        giantHitstop        = settings.GiantHitstop;
        giantChargeZoomIn   = settings.GiantChargeZoomIn;
        giantChargeFovPunch = settings.GiantChargeFovPunch;
    }

    private void Awake() {
        // 設定アセットの値を反映する (未アサイン時はコード既定値のまま動作)。
        if (settings != null) ApplySettings();
        else Debug.LogWarning("[PlayerAttack] PlayerAttackSettings が未アサインです。コード既定値で動作します。", this);

        characterController = GetComponent<CharacterController>();
        sound = GetComponent<Player_SoundManager>();
        attackVfx = PlayerAttackVFX.Ensure(gameObject);   // 納品VFX (チャージ/ポータル/着弾/ヒット/走り煙)
        InitChargeEffects();   // PlayerAttack.Charge.cs (パッケージがあれば簡易デフォルトは作らない)
    }

    // スタンプ1本ぶんの「消え始めた」処理。最後の1本で予想サークルとスラムズームを解除。
    private void ReleaseWeaponSlot() {
        activeWeaponCount = Mathf.Max(0, activeWeaponCount - 1);
        if (activeWeaponCount == 0) {
            AttackLandingIndicator.TryHide();
            TPSCameraController.Instance?.SetSlamZoom(false);   // 着地後ゆっくり画角が戻る
        }
    }

    /**=======================================================================
      命中処理の共通部: 検出コライダーから IEnemy を拾ってダメージ + ヒット演出。
      叩きつけ着地 (OverlapSphere) とコンボ斬撃 (OverlapCapsule) の両方から呼ぶ。
      戻り値 = 命中したエネミー数 (着弾イベントの hitCount / 空振り判定に使う)。
    =======================================================================**/
    private int ApplyDamageToEnemies(Collider[] hitColliders, Vector3 fxPos, int damage,
                                     bool allowGlobalHitstop = true, float globalHitstop = -1f) {
        var ctx = new DamageContext {
            targetHitstop = hitstopDurationEnemy,
            sourceHitstop = hitstopDurationSelf
        };

        HashSet<IHealth> damaged = new HashSet<IHealth>();
        foreach (Collider col in hitColliders) {
            IEnemy enemy = col.GetComponentInParent<IEnemy>();
            if (enemy == null) continue;
            if (!damaged.Add(enemy)) continue;

            if (enemy.IsDead) {
                (enemy as MonoBehaviour)?.GetComponent<B1_Death>()?.TryFinishingBlow();
                // Boss02(女王): 無限ダウン中に近づいて攻撃するとシーリングスタンプのとどめが入る
                (enemy as MonoBehaviour)?.GetComponent<Boss02.B2_FinishingBlow>()?.TryFinishingBlow();
                continue;
            }
            CombatSystem.Instance.Damage(enemy, damage, ctx, this);
            // 命中1体ごとのヒットVFXフック (対象の体の中心で弾ける)
            OnEnemyHit?.Invoke(new HitEvent { position = col.bounds.center, damage = damage });
        }

        // 命中演出: エネミー命中時は全体ヒットストップ + 強シェイク、空振りは軽いシェイク。
        // 同フレーム多重命中でも 0.1 秒内は1回にまとめる (重ねると振幅が倍々で酔う)。
        bool fxOk = Time.unscaledTime - lastImpactFxTime >= 0.1f;
        if (damaged.Count > 0) {
            sound?.PlayAttackHit();
            // 多段ヒット(巨大スタンプ)は毎ヒットで全体停止すると停止が重なりガタつくため、
            // 呼び出し側が最後の一撃だけ true を渡す等で制御できるようにしている。
            // globalHitstop >= 0 なら攻撃ごとの指定値、負なら共有既定 (hitGlobalHitstop) を使う。
            float gh = globalHitstop >= 0f ? globalHitstop : hitGlobalHitstop;
            if (allowGlobalHitstop && gh > 0f) GlobalHitStop.Play(gh, hitGlobalHitstopScale);
            if (fxOk) CameraShake.Play(hitShakeDuration, hitShakeIntensity);
            lastImpactFxTime = Time.unscaledTime;
        } else if (fxOk) {
            CameraShake.Play(shakeDuration, shakeIntensity);
            lastImpactFxTime = Time.unscaledTime;
        }

        return damaged.Count;
    }

    /**=======================================================================
      着地スカッシュ: 縦に潰れて元サイズに戻る (コンボ/チャージ共通)
    =======================================================================**/
    private IEnumerator ImpactSquash(GameObject weapon, Vector3 baseScale) {
        if (weapon == null || impactSquashDuration <= 0f) yield break;

        Transform tf = weapon.transform;
        Vector3 squashed = new Vector3(
            baseScale.x * impactSquashScaleXZ,
            baseScale.y * impactSquashScaleY,
            baseScale.z * impactSquashScaleXZ);
        tf.localScale = squashed;

        float t = 0f;
        while (t < impactSquashDuration && weapon != null) {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / impactSquashDuration);
            float ease = 1f - Mathf.Pow(1f - p, 3f);
            tf.localScale = Vector3.Lerp(squashed, baseScale, ease);
            yield return null;
        }
        if (weapon != null) tf.localScale = baseScale;
    }

    /**=======================================================================
      着地後のスタンプをディゾルブで消す (コンボ/チャージ共通)。
      destroyDelay 前半は据え置き → 後半で Effects/StampDissolve へ差し替えて溶かす。
      マテリアルは Resources/M_StampDissolve 経由 (ビルド削除対策)。
    =======================================================================**/
    private IEnumerator DissolveAndDestroy(GameObject weapon) {
        float holdTime = destroyDelay * 0.5f;
        float dissolveTime = Mathf.Max(0.1f, destroyDelay - holdTime);

        float t = 0f;
        while (t < holdTime && weapon != null) { t += Time.deltaTime; yield return null; }
        if (weapon == null) yield break;

        Material baseMat = Resources.Load<Material>("M_StampDissolve");
        Shader sh = (baseMat != null) ? null : Shader.Find("Effects/StampDissolve");
        var mats = new List<Material>();
        if (baseMat != null || sh != null) {
            foreach (var r in weapon.GetComponentsInChildren<Renderer>()) {
                var src = r.sharedMaterial;
                Material m = (baseMat != null) ? new Material(baseMat) : new Material(sh);
                if (src != null) {
                    Texture tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap")
                                : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                    if (tex != null) m.SetTexture("_MainTex", tex);
                    if (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                    else if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.GetColor("_Color"));
                }
                r.sharedMaterial = m;
                mats.Add(m);
            }
        }

        t = 0f;
        while (t < dissolveTime && weapon != null) {
            t += Time.deltaTime;
            float d = Mathf.Clamp01(t / dissolveTime);
            for (int i = 0; i < mats.Count; i++) mats[i].SetFloat("_Dissolve", d);
            yield return null;
        }

        if (weapon != null) Destroy(weapon);
        for (int i = 0; i < mats.Count; i++) if (mats[i] != null) Destroy(mats[i]);
    }
}
