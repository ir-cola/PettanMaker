using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Boss01 の戦闘イベントと連動する動的ポストプロセス。
/// B1_PostProcess のベース Volume の上に、weight 0 の高優先度 Volume を複数構築し、
/// イベントに応じて weight を動かすことで画面演出を行う。
///
/// 常時レイヤー (FadeLayer: SmoothStep イージング付き ON/OFF フェード)
/// ・フェーズ2   : コントラスト/Bloom 強化 + 低周波の「呼吸」揺らぎ (StateMachine.OnStateChanged)
/// ・Enraged     : 赤みフィルタ + FilmGrain 増量で激昂を表現 (Blackboard.IsEnraged)
/// ・予兆中      : Vignette をじわっと強めて緊張感。攻撃種別で色分け (Rush=赤 / Tail=金 / 他=紫)
/// ・Rush 突進中 : 樽型歪み + 色収差 + Vignette で疑似モーションブラー
/// ・プレイヤー低HP: 脱色 + 赤 Vignette (HP割合がしきい値以下の間)
///
/// 単発レイヤー (PulseLayer: attack→hold→release エンベロープ、unscaled 時間で駆動)
/// ・被ダメージ  : 色収差 + 赤 Vignette を一瞬パルス (PlayerHealth.onHPChanged の減少検知)
/// ・Roar        : レンズ歪み + Bloom 爆発 + カメラシェイク
/// ・フェーズ2突入: 白フラッシュ + 彩度抜きで「切り替わった」ことを見せる
/// ・撃破        : 白フラッシュ + 完全脱色からゆっくり色が戻り、全戦闘演出をベースの絵へ返す
///
/// 参照はシーンから自動解決するので、B1_PostProcess と同じ GameObject に置くだけで動く。
/// </summary>
[DisallowMultipleComponent]
public class B1_PostProcessFX : MonoBehaviour
{
    // ========================================================================
    // Inspector
    // ========================================================================

    [Header("被ダメージパルス")]
    [SerializeField] private bool  damagePulse = true;
    [Tooltip("パルスが減衰しきるまでの秒数。")]
    [SerializeField] private float damagePulseDuration = 0.35f;
    [SerializeField, Range(0f, 1f)] private float damageChromatic = 0.6f;
    [SerializeField, Range(0f, 1f)] private float damageVignetteIntensity = 0.45f;
    [SerializeField] private Color damageVignetteColor = new Color(0.45f, 0.02f, 0.02f, 1f);

    [Header("フェーズ2")]
    [SerializeField] private bool  phase2Look = true;
    [Tooltip("フェーズ2の絵へ切り替わるフェード秒数。")]
    [SerializeField] private float phase2FadeDuration = 1.5f;
    [Tooltip("上げすぎると暗部が沈んで画面が暗く見える。15 前後まで。")]
    [SerializeField, Range(-100f, 100f)] private float phase2Contrast = 15f;
    [SerializeField, Range(-100f, 100f)] private float phase2Saturation = 5f;
    [Tooltip("フェーズ2で画面全体に乗せる色。紫寄りで妖しさを強調。平均輝度を落とさないよう 1 以上を基準にする。")]
    [SerializeField] private Color phase2ColorFilter = new Color(1.03f, 0.97f, 1.08f, 1f);
    [SerializeField] private float phase2BloomIntensity = 1.2f;

    [Header("フェーズ2: 呼吸揺らぎ")]
    [Tooltip("Bloom と ColorFilter を低周波の sin で微揺らしして、常時演出が静止画に見えるのを防ぐ。")]
    [SerializeField] private bool  phase2Breath = true;
    [Tooltip("呼吸の周波数 (Hz)。0.3〜0.5 で「ゆっくり脈打つ」感じになる。")]
    [SerializeField, Range(0.05f, 2f)] private float phase2BreathFrequency = 0.4f;
    [Tooltip("揺らぎの振幅 (±割合)。0.1 = ±10%。上げすぎると点滅に見える。")]
    [SerializeField, Range(0f, 0.3f)] private float phase2BreathAmount = 0.08f;

    [Header("フェーズ2: 突入フラッシュ")]
    [Tooltip("Phase2 ステートへ入った瞬間に一発、白フラッシュ + 彩度抜きで切り替わりを見せる。")]
    [SerializeField] private bool  phase2EntryFlash = true;
    [SerializeField, Range(0f, 2f)]     private float phase2FlashExposure = 0.5f;
    [SerializeField, Range(-100f, 0f)]  private float phase2FlashSaturation = -60f;
    [SerializeField] private float phase2FlashAttack  = 0.06f;
    [SerializeField] private float phase2FlashHold    = 0.10f;
    [SerializeField] private float phase2FlashRelease = 0.8f;

    [Header("ダウン: 突入フラッシュ")]
    [Tooltip("Down ステート（体力半分 → Phase2 移行前）へ入った瞬間、寄りカメラへの遷移に合わせて強い白フラッシュを焚く。")]
    [SerializeField] private bool  downEntryFlash = true;
    [Tooltip("Phase2 突入フラッシュより強め (露出)。")]
    [SerializeField, Range(0f, 3f)]     private float downFlashExposure = 1.2f;
    [SerializeField, Range(-100f, 0f)]  private float downFlashSaturation = -40f;
    [Tooltip("Down 突入フラッシュが立ち上がってから消えるまでの合計秒数。")]
    [SerializeField, Min(0f)] private float downFlashDuration = 0.86f;

    [Header("クリティカルヒット: 画面フラッシュ")]
    [Tooltip("ボスへのクリティカルヒットの瞬間、白フラッシュ + 色収差を一発焚いて「会心の一撃」を見せる。")]
    [SerializeField] private bool  critFlash = true;
    [Tooltip("フラッシュの露出。Down フラッシュ (1.2) より控えめ、被ダメパルスより強く。")]
    [SerializeField, Range(0f, 3f)]    private float critFlashExposure = 0.8f;
    [Tooltip("フラッシュ中の彩度。少し抜くと閃光感が出る。")]
    [SerializeField, Range(-100f, 0f)] private float critFlashSaturation = -30f;
    [SerializeField, Range(0f, 1f)]    private float critFlashChromatic = 0.7f;
    [SerializeField] private float critFlashAttack  = 0.02f;
    [SerializeField] private float critFlashHold    = 0.08f;
    [SerializeField] private float critFlashRelease = 0.4f;

    [Header("予兆 (テレグラフ)")]
    [SerializeField] private bool  telegraphTension = true;
    [Tooltip("予兆 Vignette の立ち上がり秒数。解除は倍速で戻る。")]
    [SerializeField] private float tensionFadeDuration = 0.4f;
    [SerializeField, Range(0f, 1f)] private float tensionVignetteIntensity = 0.32f;
    [Tooltip("攻撃種別が特定できないときの基本色 (紫)。")]
    [SerializeField] private Color tensionVignetteColor = new Color(0.08f, 0.02f, 0.12f, 1f);
    [Tooltip("突進 (RushCharge) の予兆色。赤系で「来る」感を出す。")]
    [SerializeField] private Color tensionColorRush = new Color(0.30f, 0.03f, 0.06f, 1f);
    [Tooltip("テール攻撃 (TailAttack / TailRain) の予兆色。金系。")]
    [SerializeField] private Color tensionColorTail = new Color(0.34f, 0.24f, 0.03f, 1f);

    [Header("Rush (突進) 速度感")]
    [Tooltip("突進中だけ弱い樽型歪み + 色収差 + Vignette を乗せて疑似モーションブラー感を出す。")]
    [SerializeField] private bool  rushSpeedLook = true;
    [SerializeField] private float rushFadeIn  = 0.15f;
    [SerializeField] private float rushFadeOut = 0.30f;
    [Tooltip("樽型レンズ歪み。Roar (-0.35) より控えめに。")]
    [SerializeField, Range(-1f, 0f)] private float rushLensDistortion = -0.15f;
    [SerializeField, Range(0f, 1f)]  private float rushChromatic = 0.25f;
    [SerializeField, Range(0f, 1f)]  private float rushVignetteIntensity = 0.22f;
    [SerializeField] private Color rushVignetteColor = new Color(0.02f, 0.02f, 0.06f, 1f);

    [Header("Enraged (激昂)")]
    [Tooltip("Blackboard.IsEnraged の間、赤みフィルタ + FilmGrain を薄く乗せて AI の状態を画面から読めるようにする。")]
    [SerializeField] private bool  enragedLook = true;
    [SerializeField] private float enragedFadeDuration = 1.0f;
    [Tooltip("平均輝度を落とさないよう R だけ 1 以上に上げる。")]
    [SerializeField] private Color enragedColorFilter = new Color(1.06f, 0.96f, 0.96f, 1f);
    [Tooltip("ベース (B1_PostProcess: 0.08) より少し強いグレイン。")]
    [SerializeField, Range(0f, 1f)] private float enragedFilmGrain = 0.14f;

    [Header("プレイヤー低HP")]
    [SerializeField] private bool  lowHpLook = true;
    [Tooltip("この割合以下で低HP演出を開始する。")]
    [SerializeField, Range(0f, 1f)] private float lowHpRatio = 0.25f;
    [SerializeField] private float lowHpFadeDuration = 0.8f;
    [SerializeField, Range(-100f, 0f)] private float lowHpSaturation = -40f;
    [SerializeField, Range(0f, 1f)] private float lowHpVignetteIntensity = 0.35f;
    [SerializeField] private Color lowHpVignetteColor = new Color(0.35f, 0.02f, 0.02f, 1f);

    [Header("Roar (咆哮)")]
    [Tooltip("Roar の瞬間に画面全体を歪ませて咆哮の圧を出す。登場演出とフェーズ2の Roar で発火。")]
    [SerializeField] private bool  roarPulse = true;
    [Tooltip("立ち上がり秒数。短いほど「ドン」と来る。")]
    [SerializeField] private float roarAttack = 0.08f;
    [SerializeField] private float roarHold = 0.5f;
    [SerializeField] private float roarRelease = 0.9f;
    [Tooltip("樽型レンズ歪み。負値で画面が外側へ膨らみ、咆哮の圧力波に見える。")]
    [SerializeField, Range(-1f, 0f)] private float roarLensDistortion = -0.35f;
    [SerializeField, Range(0f, 1f)] private float roarChromatic = 0.5f;
    [Tooltip("Roar 中の Bloom 強度。ベース(0.8)より大きく上げて発光を爆発させる。")]
    [SerializeField] private float roarBloomIntensity = 1.8f;
    [SerializeField, Range(0f, 1f)] private float roarVignetteIntensity = 0.4f;
    [SerializeField] private Color roarVignetteColor = new Color(0.12f, 0.02f, 0.18f, 1f);
    [Tooltip("Roar 時のカメラシェイク秒数。0 で無効。")]
    [SerializeField] private float roarShakeDuration = 0.6f;
    [SerializeField] private float roarShakeIntensity = 0.35f;
    [SerializeField] private float roarShakeFrequency = 22f;

    [Header("撃破")]
    [Tooltip("ボス撃破後、すべての戦闘演出をベースの絵へ戻すフェード秒数。")]
    [SerializeField] private float returnToNormalDuration = 1.2f;
    [Tooltip("撃破の瞬間に白フラッシュ + 完全脱色を入れ、release でゆっくり色を戻して勝利を締める。")]
    [SerializeField] private bool  defeatFlash = true;
    [SerializeField, Range(0f, 2f)] private float defeatFlashExposure = 0.45f;
    [SerializeField] private float defeatFlashAttack  = 0.05f;
    [SerializeField] private float defeatFlashHold    = 0.20f;
    [SerializeField] private float defeatFlashRelease = 1.8f;

    // ========================================================================
    // レイヤー駆動の共通部品
    // ========================================================================

    /// <summary>
    /// ON/OFF ターゲットへ向けてフェードする常時レイヤー。
    /// 内部は線形進行度で保持し、weight へは SmoothStep をかけて機械的な線形フェードを避ける。
    /// </summary>
    private sealed class FadeLayer
    {
        private readonly Volume volume;
        private float linear; // 0..1 の線形進行度

        public FadeLayer(Volume volume) => this.volume = volume;

        /// <summary>イージング適用後の現在 weight (0..1)。</summary>
        public float Progress => volume != null ? volume.weight : 0f;

        public void Update(bool on, float duration, float dt)
        {
            if (volume == null) return;
            float speed = duration > 0f ? 1f / duration : float.MaxValue;
            linear = Mathf.MoveTowards(linear, on ? 1f : 0f, speed * dt);
            volume.weight = Mathf.SmoothStep(0f, 1f, linear);
        }
    }

    /// <summary>
    /// attack → hold → release のエンベロープで weight を駆動する単発パルスレイヤー。
    /// Fire() で発火し、エンベロープを走りきったら自動で消灯する。
    /// </summary>
    private sealed class PulseLayer
    {
        private readonly Volume volume;
        private readonly float attack, hold, release;
        private float timer = -1f; // 負値で停止中

        public PulseLayer(Volume volume, float attack, float hold, float release)
        {
            this.volume  = volume;
            this.attack  = Mathf.Max(0f, attack);
            this.hold    = Mathf.Max(0f, hold);
            this.release = Mathf.Max(0f, release);
        }

        public void Fire() => timer = 0f;

        public void Kill()
        {
            timer = -1f;
            if (volume != null) volume.weight = 0f;
        }

        public void Update(float dt)
        {
            if (timer < 0f || volume == null) return;
            timer += dt;
            volume.weight = Evaluate(timer);
            if (timer >= attack + hold + release) Kill();
        }

        // attack で 0→1、hold で 1 を維持、release で 1→0 (いずれも smoothstep)。
        private float Evaluate(float t)
        {
            if (t < attack) return attack > 0f ? Mathf.SmoothStep(0f, 1f, t / attack) : 1f;
            t -= attack;
            if (t < hold) return 1f;
            t -= hold;
            return release > 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / release)) : 0f;
        }
    }

    // ========================================================================
    // 内部状態
    // ========================================================================

    private const float ResolveInterval = 0.5f; // 参照解決リトライの間隔 (秒)

    // 常時レイヤー
    private FadeLayer phase2Layer;
    private FadeLayer tensionLayer;
    private FadeLayer lowHpLayer;
    private FadeLayer rushLayer;
    private FadeLayer enragedLayer;

    // 単発レイヤー
    private PulseLayer damageLayer;
    private PulseLayer roarLayer;
    private PulseLayer phase2FlashLayer;
    private PulseLayer downFlashLayer;
    private PulseLayer critFlashLayer;
    private PulseLayer defeatFlashLayer;

    // 呼吸揺らぎ・予兆色分けで毎フレーム触るオーバーライドへの参照
    private Bloom            phase2Bloom;
    private ColorAdjustments phase2ColorAdj;
    private Vignette         tensionVignette;

    private readonly List<Volume> createdVolumes = new();

    private B1_Boss         boss;
    private B1_StateMachine stateMachine;
    private PlayerHealth    playerHealth;
    private int             lastPlayerHP = -1;
    private float           nextResolveTime;
    private bool            tensionActive;
    private bool            inPhase2;
    private bool            phase2FlashFired; // Timeline Signal とステート遷移の二重発火防止
    private bool            bossDefeated;
    private ActionGroupType lastSelectedGroup = ActionGroupType.Idle;

    // ========================================================================
    // ライフサイクル
    // ========================================================================

    private void Start()
    {
        // 参照解決。ボスは登場演出でシーンに後から Instantiate される可能性があるため
        // 見つからなければ Update 内で一定間隔でリトライする。
        ResolveReferences();

        if (damagePulse)      damageLayer      = new PulseLayer(BuildDamageVolume(), 0f, 0f, damagePulseDuration);
        if (phase2Look)       phase2Layer      = new FadeLayer(BuildPhase2Volume());
        if (phase2EntryFlash) phase2FlashLayer = new PulseLayer(BuildPhase2FlashVolume(),
                                                     phase2FlashAttack, phase2FlashHold, phase2FlashRelease);
        if (downEntryFlash)
        {
            // 従来の見た目に近い比率（Attack 5% / Hold 14% / Release 81%）を保ったまま、
            // Inspector の Duration ひとつでフラッシュ全体の長さを調整する。
            float duration = Mathf.Max(0f, downFlashDuration);
            downFlashLayer = new PulseLayer(BuildDownFlashVolume(),
                duration * 0.05f, duration * 0.14f, duration * 0.81f);
        }
        if (critFlash)        critFlashLayer   = new PulseLayer(BuildCritFlashVolume(),
                                                     critFlashAttack, critFlashHold, critFlashRelease);
        if (telegraphTension) tensionLayer     = new FadeLayer(BuildTensionVolume());
        if (rushSpeedLook)    rushLayer        = new FadeLayer(BuildRushVolume());
        if (enragedLook)      enragedLayer     = new FadeLayer(BuildEnragedVolume());
        if (lowHpLook)        lowHpLayer       = new FadeLayer(BuildLowHpVolume());
        if (roarPulse)        roarLayer        = new PulseLayer(BuildRoarVolume(), roarAttack, roarHold, roarRelease);
        if (defeatFlash)      defeatFlashLayer = new PulseLayer(BuildDefeatFlashVolume(),
                                                     defeatFlashAttack, defeatFlashHold, defeatFlashRelease);
    }

    /// <summary>
    /// Roar 演出を1回発火する。B1_CameraTimelineBridge.OnRoar (Timeline Signal) や
    /// AIController の Roar アクションから呼ぶ。
    /// </summary>
    public void TriggerRoarPulse() => TriggerRoarPulse(withShake: true);

    /// <summary>
    /// withShake=false でポストプロセスのみ発火する。呼び出し側が
    /// B1_CameraSceneSettings.RoarShake でシェイクを別管理する場合に使う。
    /// </summary>
    public void TriggerRoarPulse(bool withShake)
    {
        if (roarLayer == null || bossDefeated) return;
        roarLayer.Fire();
        if (withShake && roarShakeDuration > 0f)
        {
            // ボス位置が取れれば咆哮の発生源から押し出される指向性インパルスにする。
            if (boss != null)
                CameraShake.PlayImpulse(boss.transform.position, roarShakeDuration, roarShakeIntensity, roarShakeFrequency);
            else
                CameraShake.Play(roarShakeDuration, roarShakeIntensity, roarShakeFrequency);
        }
    }

    /// <summary>
    /// Phase2 移行のポストプロセス（突入フラッシュ + 常時フェーズ2ルック）を発火する。
    /// Phase2 カメラ Timeline の EnrageMaterial Signal（目が赤くなる瞬間）から
    /// B1_CameraTimelineBridge.OnEnrageMaterial 経由で呼ぶ。
    /// </summary>
    public void TriggerPhase2Transition()
    {
        if (bossDefeated) return;
        if (!phase2FlashFired)
        {
            phase2FlashFired = true;
            phase2FlashLayer?.Fire();
        }
        inPhase2 = true; // 常時フェーズ2ルックもこの瞬間からフェードインを始める
    }

    private void ResolveReferences()
    {
        if (boss == null)
        {
            boss = FindFirstObjectByType<B1_Boss>();
            if (boss != null)
            {
                if (boss.AnimatorBridge != null && telegraphTension)
                {
                    boss.AnimatorBridge.OnTelegraphStartEvent += OnTelegraphStart;
                    boss.AnimatorBridge.OnTelegraphEndEvent   += OnTelegraphEnd;
                }
                if (boss.Health != null)
                {
                    boss.Health.onDied.AddListener(OnBossDied);
                    if (critFlash)
                        boss.Health.onCriticalHit.AddListener(OnCriticalHit);
                }
                if (boss.AIController != null)
                {
                    if (roarPulse)
                        boss.AIController.OnRoarActionStarted += TriggerRoarPulse;
                    // 予兆 Vignette の色分けに使う「直近に選ばれた行動グループ」
                    boss.AIController.OnActionGroupSelected += OnActionGroupSelected;
                }
                if (boss.StateMachine != null)
                {
                    stateMachine = boss.StateMachine;
                    stateMachine.OnStateChanged += OnBossStateChanged;
                    // 途中参加でイベントを取り逃した場合の初期同期 (突入フラッシュは焚かない)
                    inPhase2 = stateMachine.CurrentStateName == "Phase2";
                    if (stateMachine.CurrentStateName == "Death") bossDefeated = true;
                }
            }
        }

        if (playerHealth == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.TryGetComponent(out PlayerHealth hp))
            {
                playerHealth = hp;
                lastPlayerHP = hp.CurrentHP;
                playerHealth.onHPChanged.AddListener(OnPlayerHPChanged);
            }
        }
    }

    private void Update()
    {
        if ((boss == null || playerHealth == null) && Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + ResolveInterval;
            ResolveReferences();
        }

        float dt = Time.deltaTime;

        // ボス撃破後: 単発パルスを消灯し、常時レイヤーをフェードアウトしてベースの絵へ戻す。
        // 撃破フラッシュだけはエンベロープを走らせ、脱色からゆっくり色が戻る絵を作る。
        if (bossDefeated)
        {
            damageLayer?.Kill();
            roarLayer?.Kill();
            phase2FlashLayer?.Kill();
            downFlashLayer?.Kill();
            critFlashLayer?.Kill();
            defeatFlashLayer?.Update(Time.unscaledDeltaTime);

            phase2Layer?.Update(false,  returnToNormalDuration, dt);
            tensionLayer?.Update(false, returnToNormalDuration, dt);
            rushLayer?.Update(false,    returnToNormalDuration, dt);
            enragedLayer?.Update(false, returnToNormalDuration, dt);
            lowHpLayer?.Update(false,   returnToNormalDuration, dt);
            return;
        }

        // ---- 単発パルス (ヒットストップやカットシーン中も戻るよう unscaled で進める) ----
        float udt = Time.unscaledDeltaTime;
        damageLayer?.Update(udt);
        roarLayer?.Update(udt);
        phase2FlashLayer?.Update(udt);
        downFlashLayer?.Update(udt);
        critFlashLayer?.Update(udt);

        // ---- 常時レイヤー ----
        phase2Layer?.Update(inPhase2, phase2FadeDuration, dt);
        UpdatePhase2Breath();

        // 予兆: Telegraph イベントで ON/OFF。解除は倍速で戻す。
        tensionLayer?.Update(tensionActive, tensionActive ? tensionFadeDuration : tensionFadeDuration * 0.5f, dt);

        if (rushLayer != null)
        {
            bool rushing = boss != null && boss.ActionController?.CurrentAction is B1_RushAction;
            rushLayer.Update(rushing, rushing ? rushFadeIn : rushFadeOut, dt);
        }

        if (enragedLayer != null)
        {
            // IsEnraged は HP半分（Down 中）で先に true になるため、Phase2 カメラ演出の
            // EnrageMaterial Signal（目が赤くなる瞬間）で立つ inPhase2 が来るまで発動を待つ。
            bool enraged = inPhase2 && boss != null && boss.Blackboard != null && boss.Blackboard.IsEnraged;
            enragedLayer.Update(enraged, enragedFadeDuration, dt);
        }

        if (lowHpLayer != null)
        {
            bool low = playerHealth != null && !playerHealth.IsDead
                    && playerHealth.MaxHP > 0
                    && (float)playerHealth.CurrentHP / playerHealth.MaxHP <= lowHpRatio;
            lowHpLayer.Update(low, lowHpFadeDuration, dt);
        }
    }

    // Bloom 強度と ColorFilter を低周波 sin で微揺らしして「呼吸」させる。
    // 2つの位相をずらして機械的な同期に見えないようにする。
    private void UpdatePhase2Breath()
    {
        if (!phase2Breath || phase2Layer == null || phase2Layer.Progress <= 0f) return;

        float w = Time.time * phase2BreathFrequency * Mathf.PI * 2f;
        if (phase2Bloom != null)
        {
            float s = Mathf.Sin(w);
            phase2Bloom.intensity.Override(Mathf.Max(0f, phase2BloomIntensity * (1f + phase2BreathAmount * s)));
        }
        if (phase2ColorAdj != null)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(w + 1.7f);
            phase2ColorAdj.colorFilter.Override(
                Color.Lerp(phase2ColorFilter, phase2ColorFilter * (1f + phase2BreathAmount), k));
        }
    }

    // ========================================================================
    // イベントハンドラ
    // ========================================================================

    private void OnPlayerHPChanged(int currentHP)
    {
        // 回復や最大HP増加でも呼ばれるため、減少したときだけパルスを発火する。
        if (lastPlayerHP >= 0 && currentHP < lastPlayerHP && !bossDefeated)
            damageLayer?.Fire();
        lastPlayerHP = currentHP;
    }

    private void OnBossStateChanged(IStatusState state)
    {
        if (stateMachine == null) return;

        // ダウン突入の瞬間、寄りカメラへの遷移に合わせて強フラッシュを焚く。
        if (state == stateMachine.DownState && !bossDefeated)
            downFlashLayer?.Fire();

        bool nowPhase2 = state == stateMachine.Phase2State;
        // 通常は Phase2 Timeline の EnrageMaterial Signal（目が赤くなる瞬間）で
        // TriggerPhase2Transition 済み。Timeline 未設定・中断時のみここで焚く保険。
        if (nowPhase2 && !inPhase2 && !bossDefeated && !phase2FlashFired)
        {
            phase2FlashFired = true;
            phase2FlashLayer?.Fire();
        }
        inPhase2 = nowPhase2 || (inPhase2 && phase2FlashFired);

        if (state == stateMachine.DeathState)
            OnBossDefeated();
    }

    private void OnActionGroupSelected(ActionGroupType group) => lastSelectedGroup = group;

    private void OnTelegraphStart()
    {
        tensionActive = true;
        // 直近に選ばれた行動グループから予兆色を決め、プレイヤーが色で攻撃を先読みできるようにする
        tensionVignette?.color.Override(TensionColorFor(lastSelectedGroup));
    }

    private void OnTelegraphEnd() => tensionActive = false;

    /// <summary>ボスへのクリティカルヒット（B1_Health.onCriticalHit）。白フラッシュを一発焚く。</summary>
    private void OnCriticalHit(int damage)
    {
        if (!bossDefeated) critFlashLayer?.Fire();
    }

    private void OnBossDied() => OnBossDefeated();

    private void OnBossDefeated()
    {
        if (bossDefeated) return;
        bossDefeated = true;
        defeatFlashLayer?.Fire();
    }

    private Color TensionColorFor(ActionGroupType group) => group switch
    {
        ActionGroupType.RushCharge => tensionColorRush,
        ActionGroupType.TailAttack => tensionColorTail,
        ActionGroupType.TailRain   => tensionColorTail,
        _                          => tensionVignetteColor,
    };

    // ========================================================================
    // Volume 構築 (すべて weight 0 で開始し、Update で動かす)
    // 優先度: Enraged(12) < 予兆(15) < Rush(18) < Phase2(20) < 低HP(25)
    //         < 被ダメ(30) < Roar(35) < Phase2フラッシュ(40) < ダウンフラッシュ(45)
    //         < クリティカルフラッシュ(48) < 撃破フラッシュ(50)
    // ========================================================================

    private Volume BuildDamageVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var cab = profile.Add<ChromaticAberration>(true);
        cab.intensity.Override(damageChromatic);
        var v = profile.Add<Vignette>(true);
        v.intensity.Override(damageVignetteIntensity);
        v.smoothness.Override(0.6f);
        v.color.Override(damageVignetteColor);
        return CreateVolume(profile, 30f);
    }

    private Volume BuildPhase2Volume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        phase2ColorAdj = profile.Add<ColorAdjustments>(true);
        phase2ColorAdj.contrast.Override(phase2Contrast);
        phase2ColorAdj.saturation.Override(phase2Saturation);
        phase2ColorAdj.colorFilter.Override(phase2ColorFilter);
        phase2Bloom = profile.Add<Bloom>(true);
        phase2Bloom.intensity.Override(Mathf.Max(0f, phase2BloomIntensity));
        return CreateVolume(profile, 20f);
    }

    private Volume BuildPhase2FlashVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.Override(phase2FlashExposure);
        ca.saturation.Override(phase2FlashSaturation);
        return CreateVolume(profile, 40f);
    }

    private Volume BuildDownFlashVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.Override(downFlashExposure);
        ca.saturation.Override(downFlashSaturation);
        var b = profile.Add<Bloom>(true);
        b.intensity.Override(2.2f);   // 露出と合わせて「バチッ」と光らせる
        b.threshold.Override(0.6f);
        return CreateVolume(profile, 45f);
    }

    private Volume BuildCritFlashVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.Override(critFlashExposure);
        ca.saturation.Override(critFlashSaturation);
        var cab = profile.Add<ChromaticAberration>(true);
        cab.intensity.Override(critFlashChromatic);
        return CreateVolume(profile, 48f);
    }

    private Volume BuildTensionVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        tensionVignette = profile.Add<Vignette>(true);
        tensionVignette.intensity.Override(tensionVignetteIntensity);
        tensionVignette.smoothness.Override(0.55f);
        tensionVignette.color.Override(tensionVignetteColor);
        return CreateVolume(profile, 15f);
    }

    private Volume BuildRushVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ld = profile.Add<LensDistortion>(true);
        ld.intensity.Override(rushLensDistortion);
        ld.scale.Override(1.03f); // 歪みで画面端が見切れないよう少し拡大
        var cab = profile.Add<ChromaticAberration>(true);
        cab.intensity.Override(rushChromatic);
        var v = profile.Add<Vignette>(true);
        v.intensity.Override(rushVignetteIntensity);
        v.smoothness.Override(0.55f);
        v.color.Override(rushVignetteColor);
        return CreateVolume(profile, 18f);
    }

    private Volume BuildEnragedVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = profile.Add<ColorAdjustments>(true);
        ca.colorFilter.Override(enragedColorFilter);
        var fg = profile.Add<FilmGrain>(true);
        fg.type.Override(FilmGrainLookup.Thin1);
        fg.intensity.Override(enragedFilmGrain);
        return CreateVolume(profile, 12f);
    }

    private Volume BuildLowHpVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = profile.Add<ColorAdjustments>(true);
        ca.saturation.Override(lowHpSaturation);
        var v = profile.Add<Vignette>(true);
        v.intensity.Override(lowHpVignetteIntensity);
        v.smoothness.Override(0.7f);
        v.color.Override(lowHpVignetteColor);
        return CreateVolume(profile, 25f);
    }

    private Volume BuildRoarVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ld = profile.Add<LensDistortion>(true);
        ld.intensity.Override(roarLensDistortion);
        ld.scale.Override(1.05f); // 歪みで画面端が見切れないよう少し拡大
        var cab = profile.Add<ChromaticAberration>(true);
        cab.intensity.Override(roarChromatic);
        var b = profile.Add<Bloom>(true);
        b.intensity.Override(Mathf.Max(0f, roarBloomIntensity));
        b.threshold.Override(0.7f); // しきい値も下げて発光を全体に広げる
        var v = profile.Add<Vignette>(true);
        v.intensity.Override(roarVignetteIntensity);
        v.smoothness.Override(0.5f);
        v.color.Override(roarVignetteColor);
        return CreateVolume(profile, 35f);
    }

    private Volume BuildDefeatFlashVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = profile.Add<ColorAdjustments>(true);
        ca.postExposure.Override(defeatFlashExposure);
        ca.saturation.Override(-100f); // 完全脱色。release で weight が抜けるほど色が戻る
        return CreateVolume(profile, 50f);
    }

    private Volume CreateVolume(VolumeProfile profile, float priority)
    {
        var vol = gameObject.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = priority;
        vol.weight   = 0f;
        vol.profile  = profile;
        createdVolumes.Add(vol);
        return vol;
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.onHPChanged.RemoveListener(OnPlayerHPChanged);
        if (boss != null)
        {
            if (boss.AnimatorBridge != null)
            {
                boss.AnimatorBridge.OnTelegraphStartEvent -= OnTelegraphStart;
                boss.AnimatorBridge.OnTelegraphEndEvent   -= OnTelegraphEnd;
            }
            if (boss.Health != null)
            {
                boss.Health.onDied.RemoveListener(OnBossDied);
                boss.Health.onCriticalHit.RemoveListener(OnCriticalHit);
            }
            if (boss.AIController != null)
            {
                boss.AIController.OnRoarActionStarted   -= TriggerRoarPulse;
                boss.AIController.OnActionGroupSelected -= OnActionGroupSelected;
            }
        }
        if (stateMachine != null) stateMachine.OnStateChanged -= OnBossStateChanged;

        foreach (var vol in createdVolumes)
            if (vol != null && vol.profile != null) Destroy(vol.profile);
    }
}
