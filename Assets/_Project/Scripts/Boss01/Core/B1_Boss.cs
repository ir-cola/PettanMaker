using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ボスのエントリポイント。全コンポーネントを初期化し、毎フレームの処理順を管理する。
/// KS → StateMachine → ActionController の順で Update する。
/// VFX は B1_EffectManager、HP は B1_Health に委譲する。
/// </summary>
public class B1_Boss : MonoBehaviour
{
    [SerializeField] private B1_Settings       settings;
    [Tooltip("割り当てると DifficultyManager.Current に応じて Easy/Normal/Hard の設定を自動選択する。")]
    [SerializeField] private B1_SettingsSet    settingsSet;
    [SerializeField] private Transform         playerTransform;
    [SerializeField] private B1_AnimatorBridge animatorBridge;
    [SerializeField] private Collider          attackHitbox;
    [SerializeField] private Collider          tailHitbox;
    [SerializeField] private Collider[]        bodyColliders;

    [Tooltip("Phase2 突入時にボスをこの Target の位置・向きへ戻す。未設定なら位置リセットはスキップ。")]
    [SerializeField] private Transform         phase2ResetTarget;

    [Tooltip("円形マップの中心。BackStep がマップ境界を考慮して着地点を決めるのに使う。\n未設定なら境界を考慮しない (従来の単純後退)。")]
    [SerializeField] private Transform         mapCenter;
    /// <summary>円形マップの中心 (境界考慮用)。未設定なら null。</summary>
    public Transform MapCenter => mapCenter;

    [Tooltip("TailRain(テールレイン)の着弾候補地点。マップ内に数個(目安7個)置き、ここからランダムに選んで叩きつける。\n未設定ならプレイヤー周辺を狙うフォールバック動作になる。")]
    [SerializeField] private Transform[]       tailRainTargets;

    /// <summary>TailRain の着弾候補地点。B1_TailRainAction がランダム抽選に使う。未設定/空なら null 相当。</summary>
    public Transform[] TailRainTargets => tailRainTargets;

    // 登場演出の設定は B1_CameraDirector の Entrance ScriptableObject(RevealSettings) に集約。
    // ここからは読むだけ（値をシーンに散らばせない）。
    private B1_CameraSceneSettings.RevealSettings Reveal =>
        CameraDirector != null ? CameraDirector.EntranceScene?.Reveal : null;

    /// <summary>戦闘開始時に攻撃せず待機する秒数（Phase1 の初動猶予）。</summary>
    public float CombatStartIdleDuration => Reveal != null ? Reveal.sequence.combatStartIdle : 0f;

    /// <summary>Phase2 Cinema終了後に攻撃せず待機する秒数。</summary>
    public float Phase2PostCinemaIdleDuration =>
        CameraDirector != null && CameraDirector.Phase2Scene != null
            ? CameraDirector.Phase2Scene.PostCinemaIdleDuration
            : 0f;

    /// <summary>登場出現(煙〜ディゾルブ)を再生中か。CinematicState が戦闘開始を待つのに使う。</summary>
    public bool IsEntranceRevealing => Effects != null && Effects.IsEntranceRevealing;

    // --- 公開プロパティ ---
    public B1_Settings         Settings         => settings;
    public B1_AnimatorBridge   AnimatorBridge   => animatorBridge;
    public Transform           PlayerTransform  => playerTransform;
    public IBlackboard         Blackboard       => bb;

    public B1_StateMachine     StateMachine     { get; private set; }
    public B1_Locomotion       Locomotion       { get; private set; }
    public B1_ActionController ActionController { get; private set; }
    public B1_AIController     AIController     { get; private set; }
    public B1_EffectManager    Effects          { get; private set; }
    public B1_SoundManager     Sound            { get; private set; }
    public B1_Health           Health           { get; private set; }
    public B1_CameraDirector   CameraDirector   { get; private set; }
    public AnimationHitStop    HitStop          { get; private set; }
    public B1_HitReaction      HitReaction      { get; private set; }

    // --- VFX ファサード（アクション側の呼び出し口を維持する） ---
    public B1_ChargeVFX ChargeVFX                                                 => Effects.ChargeVFX;
    public B1_ChargeVFX TailChargeVFX                                             => Effects.TailChargeVFX;
    public void PlayTailAttack()                                                  => AnimatorBridge.PlayTailAttack();

    /// <summary>
    /// Phase2 突入演出の開始。TailAttackJump モーションで phase2ResetTarget の位置へ飛び
    /// （NavMeshAgent を無効化して移動するので Target が NavMesh 外でも確実に移動できる）、
    /// 着地＝衝撃波の瞬間に TailAttackLand を再生してその場に降り立つ。
    /// 着地済みかは <see cref="HasPhase2ResetLanded"/> が示し、B1_Phase2State.Enter は
    /// 未着地（Target 未設定でしっぽ立ち凍結）の場合のみ改めて TailAttackLand を再生する。
    /// 完了は <see cref="IsPhase2ResetComplete"/> で待つ。
    /// </summary>
    public void BeginPhase2Reset()
    {
        hasPhase2ResetLanded = false;
        if (phase2ResetTarget == null)
        {
            // 移動なし：その場でプレイヤーへ正対してしっぽ立ちへ入り静止する。
            FacePlayerImmediate();
            AnimatorBridge.PlayTailAttackImmediate();
            AnimatorBridge.PauseAnimator();
            return;
        }

        var cfg = settings != null ? settings.TailAttack : null;
        float height = cfg != null ? cfg.JumpHeight : 4f;
        AnimatorBridge.PlayTailAttackJumpDirect();
        Locomotion.BeginJump(phase2ResetTarget.position, Phase2ResetJumpDuration, height);

        if (phase2ResetLanding != null) StopCoroutine(phase2ResetLanding);
        phase2ResetLanding = StartCoroutine(Phase2ResetLandingRoutine());
    }

    private Coroutine phase2ResetLanding;
    private bool      hasPhase2ResetLanded;

    /// <summary>Phase2 突入演出中に TailAttackLand まで再生済みか。Phase2State が二重着地を避けるのに使う。</summary>
    public bool HasPhase2ResetLanded => hasPhase2ResetLanded;

    // 着地(ジャンプ完了)を待ち、衝撃波 → TailAttackLand再生 を同フレームで行う。
    // カメラ演出中は Time.timeScale に依存しないよう unscaled で待つ（CameraDirector と同じ流儀）。
    private System.Collections.IEnumerator Phase2ResetLandingRoutine()
    {
        const float maxWait = 4f; // ジャンプが完了しない場合の保険（CameraDirector.Phase2ResetMaxWait と同値）
        float t = 0f;
        while (!Locomotion.IsJumpComplete && t < maxWait)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            yield return null;
        }
        phase2ResetLanding = null;

        // 死亡演出や演出中断へ割り込まれていたら、Animator を凍結させない。
        if (Health != null && Health.IsDead) yield break;
        if (StateMachine != null && !StateMachine.IsCinematic) yield break;

        FacePlayerImmediate();
        PlayShockwaveAt(B1_EffectManager.CombatShockwave.TailLand, transform.position);
        AnimatorBridge.PlayTailAttackLandDirect();
        PlayLandingSquash();
        hasPhase2ResetLanded = true;
    }

    /// <summary>水平方向のみプレイヤーへ即時に正対する（Phase2 リセット着地時など、演出でスナップさせたい場面用）。</summary>
    private void FacePlayerImmediate()
    {
        if (playerTransform == null) return;
        Vector3 dir = playerTransform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    /// <summary>Phase2 リセットのジャンプ移動が着地したか。Target 未設定なら常に完了扱い。</summary>
    public bool IsPhase2ResetComplete => phase2ResetTarget == null || Locomotion.IsJumpComplete;

    /// <summary>Phase2 リセットのジャンプにかかる想定尺(秒)。カメラ側の尺見積りに使う。Target 未設定なら 0。</summary>
    public float Phase2ResetLeadEstimate => phase2ResetTarget == null ? 0f : Phase2ResetJumpDuration;

    private float Phase2ResetJumpDuration =>
        settings != null ? settings.TailAttack.JumpDuration : 0.7f;

    public void PlayEyeRushVFX()                                                  => Effects.PlayEyeRushVFX();
    public void StopEyeRushVFX()                                                  => Effects.StopEyeRushVFX();
    public void PlayRushWindVFX(Vector3 dir)                                      => Effects.PlayRushWindVFX(dir);
    public void StopRushWindVFX()                                                 => Effects.StopRushWindVFX();
    public void PlayLandingImpact(Vector3 pos)                                    => Effects.PlayLandingImpact(pos);
    public void PlayShockwave(B1_EffectManager.CombatShockwave kind, Transform origin = null)
        => Effects.PlayShockwave(kind, origin != null ? origin : transform);
    public void PlayShockwaveAt(B1_EffectManager.CombatShockwave kind, Vector3 position)
        => Effects.PlayShockwaveAt(kind, position);
    public B1_CombatShockwaveSettings WavePassShockwave                           => Effects.WavePassShockwave;
    public ManaBurstVFXSettings ManaBurstFX                                       => Effects.ManaBurst;

    /// <summary>激昂（怒りモード）中の攻撃速度倍率。モーション再生速度とタメ時間の短縮に使う。非激昂時は 1。</summary>
    public float AttackSpeedMult =>
        bb != null && bb.IsEnraged && settings != null
            ? Mathf.Max(0.01f, settings.EnragedAttackSpeedMult)
            : 1f;
    public void PlayLandingSquash()                                               => Effects.PlayLandingSquash();
    public void StartScalePulse(float duration, float peakScale, float frequencyStart, float frequencyEnd,
                                float crouchAmount, float crouchDuration)
        => Effects.StartScalePulse(duration, peakScale, frequencyStart, frequencyEnd, crouchAmount, crouchDuration);
    public void StopScalePulse()                                                  => Effects.StopScalePulse();
    public void BeginAimCrouch()                                                  => Effects.BeginAimCrouch();
    public void SetAimCrouch(float amount, float crouchAmount, float widthScale)   => Effects.SetAimCrouch(amount, crouchAmount, widthScale);
    public void EndAimCrouch()                                                    => Effects.EndAimCrouch();
    /// <summary>Phase2 カメラ Timeline の Signal から怒りモードの見た目を適用する。</summary>
    public void ActivateEnragedVisual()                                           => Effects.SetEnraged(true);
    public void BlendRushMaterialToTransparent(float duration)
    {
        Effects.BlendRushMaterialToTransparent(duration);
        Sound.PlayTransparent();
    }
    public void BlendRushMaterialToNormal(float duration)
    {
        Effects.BlendRushMaterialToNormal(duration);
        Sound.PlayTeleport();
    }
    public void CompleteRushMaterialBlendToNormal()                               => Effects.CompleteRushMaterialBlendToNormal();
    public void SetInvisible(bool invisible)
    {
        Effects.SetInvisible(invisible);
        if (invisible) Sound.PlayTransparent();
    }
    public void StartTrail()                                                      => Effects.StartTrail();
    public void StopTrail()                                                       => Effects.StopTrail();

    // --- SE ファサード（アクション側の呼び出し口） ---
    public SoundHandle PlayEntranceSound()   => Sound.PlayEntrance();
    public SoundHandle PlayDefeatedSound()   => Sound.PlayDefeated();
    public SoundHandle PlayRushChargeSound() => Sound.PlayRushCharge();
    public SoundHandle PlayRushSound()       => Sound.PlayRush();
    public SoundHandle PlayScratchSound(int hitIndex) => Sound.PlayScratch(hitIndex);
    public SoundHandle PlayAttackHitSound()  => Sound.PlayAttackHit();
    public SoundHandle PlayTeleportSound()   => Sound.PlayTeleport();
    public SoundHandle PlayTailChargeSound() => Sound.PlayTailCharge();
    public void StopTailChargeSound() => Sound.StopTailCharge();
    public SoundHandle PlayTailJumpSound()   => Sound.PlayTailJump();
    public SoundHandle PlayTailAttackSound() => Sound.PlayTailAttack();
    public SoundHandle PlayTailAttackImpactSound(Vector3 position) => Sound.PlayTailAttackImpact(position);
    public SoundHandle PlayTailLandSound()   => Sound.PlayTailLand();
    public SoundHandle PlayBackStepLandSound() => Sound.PlayBackStepLand();
    public SoundHandle PlayTailRainImpactSound(Vector3 position, bool finisher)
        => Sound.PlayTailRainImpact(position, finisher);
    public SoundHandle PlayManaBurstSound(Vector3 position, int group, bool finisher)
        => Sound.PlayManaBurst(position, group, finisher);
    public SoundHandle PlayWalkStepSound()   => Sound.PlayWalkStep();

    // --- オーサリング VFX プレハブ ---
    public void PlaySlashVFX()                                                    => Effects.PlaySlashVFX();
    public void PlayDashStarVFX()                                                 => Effects.PlayDashStarVFX();
    public void PlayDashSmokeVFX()                                                => Effects.PlayDashSmokeVFX();
    public void StopDashSmokeVFX()                                                => Effects.StopDashSmokeVFX();
    public void PlayExplosionVFX(Vector3 pos)                                     => Effects.PlayExplosionVFX(pos);

    // --- 攻撃判定 ---
    public void EnableHitbox(bool enable, int damage = 1, DamageContext ctx = default)
    {
        if (attackHitbox == null) return;
        if (enable && attackHitbox.TryGetComponent(out B1_AttackHitbox hitbox))
            hitbox.BeginAttack(damage, ctx);
        attackHitbox.enabled = enable;
    }

    public void EnableTailHitbox(bool enable, int damage = 1, DamageContext ctx = default)
    {
        if (tailHitbox == null) return;
        if (enable && tailHitbox.TryGetComponent(out B1_AttackHitbox hitbox))
            hitbox.BeginAttack(damage, ctx);
        tailHitbox.enabled = enable;
    }

    public void SetIgnorePlayerCollision(bool ignore)
    {
        if (playerTransform == null) return;
        var playerCols = playerTransform.GetComponentsInChildren<Collider>(true);
        var bossCols   = GetComponentsInChildren<Collider>(true);
        foreach (var bc in bossCols)
            foreach (var pc in playerCols)
                if (bc != null && pc != null)
                    Physics.IgnoreCollision(bc, pc, ignore);
    }

    private B1_Blackboard          bb;
    private List<IKnowledgeSource> knowledgeSources;
    private bool                   inRecovery;   // 攻撃直後の硬直（反撃猶予）中フラグ
    private CombatSystem           combat;       // onDamageDealt 購読解除用に保持
    private IHealth                playerHealth;

    private void Awake()
    {
        // 難易度別 SO が割り当てられていれば現在の難易度で解決して settings を差し替える。
        if (settingsSet != null)
        {
            var resolved = settingsSet.Resolve(DifficultyManager.Current);
            if (resolved != null) settings = resolved;
        }

        if (settings == null)
        {
            Debug.LogError("[B1_Boss] B1_Settings または B1_SettingsSet を割り当ててください。HP を含むボス設定は ScriptableObject から読み込みます。", this);
            enabled = false;
            return;
        }

        Effects = GetComponent<B1_EffectManager>() ?? gameObject.AddComponent<B1_EffectManager>();
        Sound   = GetComponent<B1_SoundManager>()   ?? gameObject.AddComponent<B1_SoundManager>();
        Health  = GetComponent<B1_Health>()         ?? gameObject.AddComponent<B1_Health>();
        CameraDirector = GetComponent<B1_CameraDirector>()
                      ?? GetComponentInChildren<B1_CameraDirector>(true)
                      ?? gameObject.AddComponent<B1_CameraDirector>();

        ResolveReferences();

        Effects.Initialize();
        Sound.Initialize(this);
        // HP は settings（難易度解決済み）から初期化する。
        Health.Initialize(settings.MaxHP);
        // クリティカルヒット: 抽選は B1_Health、演出（強ヒットストップ/点滅/シェイク）はここで発火する。
        if (settings.Critical != null && settings.Critical.Enabled)
        {
            Health.ConfigureCritical(settings.Critical.Chance, settings.Critical.DamageMultiplier);
            Health.onCriticalHit.AddListener(OnCriticalHit);
        }

        // Entrance 演出があるボスは、HPゲージ(上部ラベル含む)をシネマ終了後のシール貼付で
        // 初出しするため、生成直後から隠しておく。演出が中断/スキップされた場合は
        // Bridge の RevealImmediately か被弾時の保険で即時表示に戻る。
        if (CameraDirector != null && CameraDirector.HasEntranceTimeline)
            Health.GetComponent<B1_HealthUI>()?.PrepareStickerEntrance();

        // 被弾ヒットストップ（bridge が持つ Animator を共有して一瞬止める）
        HitStop = GetComponent<AnimationHitStop>() ?? gameObject.AddComponent<AnimationHitStop>();
        var bossAnimator = animatorBridge != null ? animatorBridge.Animator : null;
        if (bossAnimator != null) HitStop.SetAnimator(bossAnimator);
        if (settings != null) HitStop.Configure(settings.HitStopDuration, settings.HitStopFrozenSpeed);
        Health.onHPChanged.AddListener(OnDamagedHitStop);

        // 被弾時にヒット方向へ肉体を仰け反らせる（モデルルートを傾ける）。
        HitReaction = GetComponent<B1_HitReaction>() ?? gameObject.AddComponent<B1_HitReaction>();
        HitReaction.Configure(bossAnimator != null ? bossAnimator.transform : null);
        combat = CombatSystem.Instance;
        combat.onDamageDealt.AddListener(OnBossDamaged);

        bb = new B1_Blackboard();
        knowledgeSources = new List<IKnowledgeSource>
        {
            new B1_HpKS(Health),
            new B1_PerceptionKS(transform, playerTransform),
            new B1_PhaseKS(),
        };

        StateMachine     = new B1_StateMachine(this, bb);
        Locomotion       = GetComponent<B1_Locomotion>() ?? gameObject.AddComponent<B1_Locomotion>();
        Locomotion.Initialize(bb, playerTransform, settings, animatorBridge);
        ActionController = new B1_ActionController();
        AIController     = new B1_AIController(this, bb);

        var historyKS = new B1_HistoryKS(AIController);
        knowledgeSources.Add(historyKS);

        ActionController.OnActionFinished += () =>
        {
            if (!IsCombatActionState()) return;

            // 攻撃直後は一度だけ硬直（Idle）を挟み、「避けたら殴れる」反撃猶予を作る。
            // 硬直中は SelectNextAction を呼ばないため LastActionGroup は攻撃のまま。
            // inRecovery フラグで二重硬直を防ぎ、硬直明けに次行動を抽選する。
            if (!inRecovery)
            {
                float recovery = AIController.RecoveryFor(bb.LastActionGroup);
                if (recovery > 0f)
                {
                    inRecovery = true;
                    ActionController.SetAction(new B1_IdleAction(this, bb, recovery));
                    return;
                }
            }
            inRecovery = false;

            var next = AIController.SelectNextAction();
            ActionController.SetAction(next);
        };
    }

    /// <summary>被弾時に自分の Animator を一瞬止めて手応えを出す。</summary>
    private void OnDamagedHitStop(int currentHP)
    {
        HitStop?.HitStop();
    }

    /// <summary>
    /// クリティカルヒット演出（B1_Health.onCriticalHit から発火）。
    /// 通常被弾より強いヒットストップ（ボス Animator + 全体 timeScale）・スナップズーム・
    /// 強めのカメラシェイク・ボスマテリアルの点滅をまとめて再生する。
    /// 画面フラッシュは B1_PostProcessFX 側が同イベントを購読して焚く。
    /// </summary>
    private void OnCriticalHit(int damage)
    {
        var cfg = settings.Critical;
        if (cfg == null) return;

        if (cfg.BossHitStopDuration > 0f)
            HitStop?.Freeze(cfg.BossHitStopDuration);
        if (cfg.GlobalHitStopDuration > 0f)
            GlobalHitStop.Play(cfg.GlobalHitStopDuration, cfg.GlobalHitStopScale);
        if (cfg.SnapZoomFov > 0f)
            CameraSnapZoom.Play(cfg.SnapZoomFov, cfg.SnapZoomInDuration,
                                cfg.SnapZoomHoldDuration, cfg.SnapZoomOutDuration);
        if (cfg.ShakeDuration > 0f && cfg.ShakeIntensity > 0f)
            CameraShake.Play(cfg.ShakeDuration, cfg.ShakeIntensity, cfg.ShakeFrequency);
        if (cfg.BlinkInterval > 0f)
            Effects?.PlayCriticalBlink(cfg.BlinkInterval, cfg.BlinkCount, cfg.BlinkAmount, cfg.BlinkColor);
        Sound?.PlayCriticalHit();
    }

    /// <summary>被弾時にヒット方向へ肉体を仰け反らせる（攻撃元→ボスの向きへ倒す）。</summary>
    private void OnBossDamaged(GameObject src, IHealth target, int amount)
    {
        if (!ReferenceEquals(target, Health)) return;

        Vector3 from = src != null ? src.transform.position
                     : playerTransform != null ? playerTransform.position
                     : transform.position - transform.forward;
        HitReaction?.React(transform.position - from);
    }

    private void OnDestroy()
    {
        if (combat != null) combat.onDamageDealt.RemoveListener(OnBossDamaged);
    }

    private void ResolveReferences()
    {
        if (playerTransform == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }
        if (playerTransform != null)
            playerHealth = playerTransform.GetComponent<IHealth>();
        if (animatorBridge == null)
            animatorBridge = GetComponentInChildren<B1_AnimatorBridge>(true)
                          ?? gameObject.AddComponent<B1_AnimatorBridge>();
        if (attackHitbox != null)
        {
            var hitbox = attackHitbox.GetComponent<B1_AttackHitbox>()
                      ?? attackHitbox.gameObject.AddComponent<B1_AttackHitbox>();
            hitbox.Initialize(this);
            attackHitbox.enabled = false;
        }
        if (tailHitbox != null)
        {
            var hitbox = tailHitbox.GetComponent<B1_AttackHitbox>()
                      ?? tailHitbox.gameObject.AddComponent<B1_AttackHitbox>();
            hitbox.Initialize(this);
            tailHitbox.enabled = false;
        }
    }

    private void Start()
    {
        // エントランスで「煙の中から出現」させる場合、出現ビートまで完全に隠しておく。
        var r = Reveal;
        if (r != null && r.useDissolveReveal) Effects.SetHidden(true);
        StateMachine.Start();
    }

    /// <summary>隠し状態から出現させる（B1_CameraDirector の appear ビートから呼ぶ）。</summary>
    public void RevealFromHidden()
    {
        var r = Reveal;
        if (r != null && r.useDissolveReveal)
        {
            // 煙 → 完全不可視 → 透明(クローク)フェードイン → B1_Toon へ相補ディゾルブ
            Effects.PlayEntranceReveal(r);
            return;
        }
        // ディゾルブ登場を使わない場合は単純に表示するだけ。
        Effects.SetHidden(false);
    }

    private void Update()
    {
        foreach (var ks in knowledgeSources)
            ks.Evaluate(bb);

        // Player死亡後は進行中の攻撃を中断し、非戦闘の歩行探索へ移る。
        if (Health != null && !Health.IsDead && playerHealth != null && playerHealth.IsDead)
            StateMachine.EnterSearch();

        SyncEnragedState();
        StateMachine.Update();
        if (IsCombatActionState())
            ActionController.Update();
    }

    /// <summary>
    /// 激昂（怒りモード）の毎フレーム反映。
    /// 攻撃速度倍率を AnimatorBridge へ流し込む。
    /// 見た目の切り替えは Phase2 Timeline の Signal が行い、死亡中だけここで解除する。
    /// </summary>
    private void SyncEnragedState()
    {
        bool dead = Health != null && Health.IsDead;
        AnimatorBridge.SetSpeedMultiplier(dead ? 1f : AttackSpeedMult);
        if (dead) Effects.SetEnraged(false);
    }

    private bool IsCombatActionState()
    {
        string state = StateMachine?.CurrentStateName;
        return state == "Phase1" || state == "Phase2";
    }
}
