using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// テールアタック（必殺技）。
/// TailAttackJumpで上昇して地面へ戻る → TailAttack開始と衝撃波
/// → AnimationStop → ループ → 座標を動かさずTailAttackLandへ遷移する。
/// </summary>
public class B1_TailAttackAction : B1_Action
{
    private enum Phase
    {
        Rising,
        ImpactDelay,
        AnimationStop,
        Looping,
        Landing,
        Recovery,
    }

    private TailAttackActionConfig cfg;
    private float _speedMult = 1f; // 怒りモード（激昂）中の攻撃速度倍率。タイマー類を短縮して高速化する
    private Phase   _phase;
    private float   _phaseTimer;
    private bool    _hasLanded;
    private bool    _impactTriggered;
    private float   _jumpElapsed;
    private Vector3 _landingTarget;
    private B1_TelegraphCircle _telegraph;
    private B1_ManaBurstCross  _manaBurst;
    private IHealth            _telegraphDamagedTarget;
    private bool               _vanished; // マナバースト中の透明化(チェシャ猫の消失)中か

    public bool HasLanded => _hasLanded;
    public Vector3 DamageCenter => _hasLanded
        ? boss.transform.TransformPoint(cfg.AoeOffset)
        : _landingTarget + boss.transform.TransformVector(cfg.AoeOffset);
    public float DamageRadius => cfg.AoeRadius;

    public B1_TailAttackAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        cfg              = boss.Settings.TailAttack;
        _speedMult       = boss.AttackSpeedMult;
        _phase           = Phase.Rising;
        _phaseTimer      = 0f;
        _hasLanded       = false;
        _impactTriggered = false;
        _telegraphDamagedTarget = null;

        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled) BGMManager.ResetVolumeScale(bgm.ReleaseDuration);

        _landingTarget = boss.PlayerTransform != null
            ? boss.PlayerTransform.position
            : boss.transform.position;

        // 着地地点の予告サークル（中心透明→外へ赤のグラデーション円）。着地で消す。
        // 薄い状態から始まり、着地が近づくにつれ赤が濃くなる（値差は Settings で調整）。
        _jumpElapsed = 0f;
        if (cfg.UseTelegraph)
        {
            _telegraph = B1_TelegraphCircle.Spawn(
                DamageCenter, cfg.TelegraphRadius, cfg.TelegraphColor, cfg.TelegraphMaterial);
            _telegraph.SetUrgencyStyle(cfg.TelegraphFarAlpha, cfg.TelegraphPulseBoost);
            _telegraph.SetUrgency(0f);
        }

        boss.TailChargeVFX?.Burst();
        // TailAttackJumpのアニメーションと放物線移動を同じフレームで開始する。
        boss.AnimatorBridge.PlayTailAttackBeforeJump();
        boss.AnimatorBridge.SetAnimatorSpeed(cfg.AnticipationSpeed);
        boss.PlayTailJumpSound();
        // TailAttackJump中だけYを変化させる。BeginJumpは放物線の終端で
        // landingTarget.yへ戻るため、TailAttack開始時にはすでに地面へ戻っている。
        boss.Locomotion.BeginJump(_landingTarget, cfg.JumpDuration / _speedMult, cfg.JumpHeight);
        boss.StartTrail();
    }

    public override void Update()
    {
        if (!_impactTriggered && _telegraph != null)
        {
            _jumpElapsed += Time.deltaTime;
            float impactTime = (cfg.JumpDuration + cfg.ImpactDelay) / _speedMult;
            _telegraph.SetUrgency(impactTime > 0f
                ? Mathf.Clamp01(_jumpElapsed / impactTime)
                : 1f);
        }

        switch (_phase)
        {
            case Phase.Rising:
                if (boss.Locomotion.IsJumpComplete)
                    BeginTailAttack();
                break;

            case Phase.ImpactDelay:
                _phaseTimer -= Time.deltaTime;
                if (_phaseTimer <= 0f)
                    TriggerImpactAndStop();
                break;

            case Phase.AnimationStop:
                _phaseTimer -= Time.deltaTime;
                if (_phaseTimer <= 0f)
                    BeginLoop();
                break;

            case Phase.Looping:
                _phaseTimer -= Time.deltaTime;
                if (_phaseTimer <= 0f)
                    BeginLanding();
                break;

            case Phase.Landing:
                _phaseTimer -= Time.deltaTime;
                if (_phaseTimer <= 0f)
                    CompleteLanding();
                break;

            case Phase.Recovery:
                _phaseTimer -= Time.deltaTime;
                if (_phaseTimer <= 0f)
                    IsFinished = true;
                break;
        }
    }

    private void BeginTailAttack()
    {
        boss.PlayTailAttackSound();
        boss.PlayTailAttack();
        boss.AnimatorBridge.SetAnimatorSpeed(cfg.SwingSpeed);
        _phase = Phase.ImpactDelay;
        _phaseTimer = Mathf.Max(0f, cfg.ImpactDelay) / _speedMult;
    }

    private void TriggerImpactAndStop()
    {
        _impactTriggered = true;
        boss.PlayTailAttackImpactSound(DamageCenter);
        DetonateTelegraph();
        boss.PlayShockwaveAt(B1_EffectManager.CombatShockwave.TailLand, DamageCenter);
        // 叩きつけ地点から押し出される指向性インパルス。プレイヤーの立ち位置によって
        // 揺れの方向が変わり、衝撃の出どころが伝わる。
        CameraShake.PlayImpulse(DamageCenter, cfg.LandingShakeDuration, cfg.LandingShakeIntensity, cfg.LandingShakeFrequency);
        ApplyTelegraphDamage();
        ApplyLandingDamage();
        SpawnManaBurstCross();

        // TailAttack はループモーションなので speed 0 で静止させず、SwingSpeed から
        // 等速へ戻してしっぽ立ちのままループ再生させる（終了は BeginLanding の TailAttackLand）。
        boss.AnimatorBridge.ResumeAnimator();
        _phase = Phase.AnimationStop;
        _phaseTimer = Mathf.Max(0f, cfg.AnimationStopDuration) / _speedMult;
        if (_phaseTimer <= 0f)
            BeginLoop();
    }

    private void BeginLoop()
    {
        boss.AnimatorBridge.ResumeAnimator();
        _phase = Phase.Looping;
        _phaseTimer = Mathf.Max(0f, cfg.TailAttackLoopDuration) / _speedMult;
        if (_phaseTimer <= 0f)
            BeginLanding();
    }

    private void BeginLanding()
    {
        // 消えていたチェシャ猫はここで再出現する(最後の爆発と入れ替わりに姿を見せる)。
        if (_vanished)
        {
            boss.BlendRushMaterialToNormal(cfg.ReappearFadeDuration);
            _vanished = false;
        }
        boss.AnimatorBridge.PlayTailAttackLand();
        // しっぽが持ち上がる着地フェーズに入るのでヒットボックスを閉じる。
        boss.EnableTailHitbox(false);
        _phase = Phase.Landing;
        _phaseTimer = Mathf.Max(0.05f, cfg.TailAttackLandDuration / _speedMult);
    }

    private void CompleteLanding()
    {
        _hasLanded = true;
        _landingTarget = boss.transform.position;
        boss.StopTrail();
        boss.PlayLandingSquash();
        _phase = Phase.Recovery;
        _phaseTimer = Mathf.Max(0f, cfg.LandingDelay) / _speedMult;
        if (_phaseTimer <= 0f)
            IsFinished = true;
    }

    private void ApplyLandingDamage()
    {
        boss.PlayExplosionVFX(DamageCenter); // しっぽ叩きつけの着地爆発
        B1_AttackBounceObject.LaunchInRadius(DamageCenter, cfg.AoeRadius);

        if (cfg.UseDamageWave)
        {
            // ドーナッツ状に外へ広がるダメージの波。波面が通過した対象に一度だけ当たり、
            // 中心の穴(WaveInnerRadius)にいる対象は当たらない。以降は波が自己管理・自壊する。
            B1_DamageWave.Spawn(DamageCenter, new B1_DamageWave.Params
            {
                duration    = cfg.WaveDuration,
                innerRadius = cfg.WaveInnerRadius,
                maxRadius   = cfg.AoeRadius,
                thickness   = cfg.WaveThickness,
                damage      = cfg.Damage,
                effects     = cfg.Effects.WithPlayerDamageType(cfg.DamageType),
                alreadyDamagedTarget = _telegraphDamagedTarget,
                color       = cfg.WaveColor,
                material    = cfg.WaveMaterial,
                spikeDepth  = cfg.WaveSpikeDepth,
                height          = cfg.JumpDodgeHeight, // 山なりに立ち上げて「跳べば越えられる壁」に見せる
                jumpDodgeHeight = cfg.JumpDodgeHeight, // 見た目の高さと回避判定の高さを一致させる
                // 波面がプレイヤーを通過したら3D衝撃波を出す（設定は B1_EffectSettings 由来）。
                screenDistortTarget = (cfg.ScreenDistortOnWave && boss.WavePassShockwave.enabled) ? boss.PlayerTransform : null,
                screenDistortOnPass = cfg.ScreenDistortOnWave && boss.WavePassShockwave.enabled,
                screenDistortParams = boss.WavePassShockwave.ToParams(),
                screenDistortOffset = boss.WavePassShockwave.positionOffset,
            }, boss.Health, boss);
            return;
        }

        ApplyInstantAoe();
    }

    // TailAttackHitBox の形状ではなく、地面に表示した予告円と同じ水平範囲で直接ヒットを判定する。
    private void ApplyTelegraphDamage()
    {
        if (!cfg.UseTelegraph || boss.PlayerTransform == null) return;

        float radius = Mathf.Max(0f, cfg.TelegraphRadius);
        Vector3 toPlayer = boss.PlayerTransform.position - DamageCenter;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > radius * radius) return;

        IHealth health = boss.PlayerTransform.GetComponent<IHealth>()
                      ?? boss.PlayerTransform.GetComponentInParent<IHealth>();
        if (health == null || health.IsDead) return;

        _telegraphDamagedTarget = health;
        CombatSystem.Instance.Damage(
            health,
            cfg.Damage,
            cfg.Effects.WithPlayerDamageType(cfg.DamageType),
            boss);
        boss.PlayAttackHitSound();
    }

    // 着地衝撃波の直後、着地点から十字方向へ導火線式の魔力地面爆発「マナバーストクロス」を仕込む。
    // 同時にボスを透明化(チェシャ猫の消失)し、爆発が走っている間は姿を消す。
    // しっぽ立ちループ中に予告→中心から外へ順に地面が爆ぜる。追加モーションは不要で、
    // B1_DamageWave と同じく Spawn 後は自己管理・自壊する。
    private void SpawnManaBurstCross()
    {
        if (!cfg.UseBurstCross) return;

        boss.TailChargeVFX?.Burst(); // 魔力が地面へ流れ込む予兆(既存のチャージVFXを流用)

        // 見た目はB1_EffectSettings、音はB1_SoundSettingsへ分離している。
        ManaBurstVFXSettings fx = boss.ManaBurstFX;

        var pattern = SelectBurstPattern();

        var p = new B1_ManaBurstCross.Params
        {
            pattern         = pattern,
            armCount        = cfg.BurstArmCount,
            armAngleOffset  = cfg.BurstAngleOffset,
            circlesPerArm   = cfg.BurstCirclesPerArm,
            startDistance   = cfg.BurstStartDistance,
            circleSpacing   = cfg.BurstCircleSpacing,
            circleRadius    = cfg.BurstCircleRadius,
            radiusGrowth    = cfg.BurstRadiusGrowth,
            spiralCircleCount = cfg.SpiralCircleCount,
            spiralTurns       = cfg.SpiralTurns,

            chargeDuration  = cfg.BurstChargeDuration,
            burstInterval   = cfg.BurstInterval,
            intervalDecay   = cfg.BurstIntervalDecay,

            damage          = cfg.BurstDamage,
            effects         = cfg.BurstEffects.WithPlayerDamageType(cfg.BurstDamageType),

            telegraphColor      = cfg.BurstTelegraphColor,
            telegraphMaterial   = cfg.TelegraphMaterial,
            telegraphFarAlpha   = cfg.TelegraphFarAlpha,
            telegraphPulseBoost = cfg.TelegraphPulseBoost,

            burstVFXPrefab = fx.BurstVFXPrefab,
            fallbackBurst  = boss.PlayExplosionVFX, // プレハブ未設定時は着地爆発と同じVFXで代用
            burstColor     = fx.BurstColor,
            scarMaterial   = fx.ScarMaterial,
            scarDuration   = fx.ScarDuration,

            // 落雷＋空間の裂け目(既存演出への純追加)
            manaBolt           = fx.EnableManaBolt,
            boltMaterial       = fx.BoltMaterial,
            riftMaterial       = fx.RiftMaterial,
            boltColor          = fx.BoltColor,
            boltHeight         = fx.BoltHeight,
            boltWidth          = fx.BoltWidth,
            boltStrikeDuration = fx.BoltStrikeDuration,
            riftDuration       = fx.RiftDuration,
            finisherBoltScale  = fx.FinisherBoltScale,
            boltImpactYOffset  = fx.BoltImpactYOffset,

            // 着弾スパーク球。impactParams.radius には「AoE半径に対する倍率」を入れておく。
            impactSparks = fx.EnableImpactSparks,
            impactParams = new B1_ManaBoltImpact.Params
            {
                sphereMaterial = fx.ImpactSphereMaterial,
                sparkMaterial  = fx.ImpactSparkMaterial,
                color          = fx.ImpactSparkColor,
                radius         = fx.ImpactSphereRadiusScale,
                sphereDuration = fx.ImpactSphereDuration,
                sparkCount     = fx.ImpactSparkCount,
                sparkSpeed     = fx.ImpactSparkSpeed,
                sparkSize      = fx.ImpactSparkSize,
                sparkDuration  = fx.ImpactSparkDuration,
            },

            strikeShakeDuration      = cfg.BurstShakeDuration,
            strikeShakeIntensity     = cfg.BurstShakeIntensity,
            strikeShakeFrequency     = cfg.BurstShakeFrequency,
            finisherShakeMultiplier  = cfg.BurstFinisherShakeMultiplier,
            hitStopDuration          = cfg.BurstHitStopDuration,

            // 画面歪みは既存の WavePassShockwave 設定を流用し、フィニッシュ爆発のみ。
            finisherDistortTarget = (cfg.BurstFinisherDistort && boss.WavePassShockwave.enabled)
                ? boss.PlayerTransform : null,
            finisherDistortParams = boss.WavePassShockwave.ToParams(),
            finisherDistortOffset = boss.WavePassShockwave.positionOffset,

            playBurstSound = (position, group, finisher) =>
                boss.PlayManaBurstSound(position, group, finisher),
        };

        _manaBurst = B1_ManaBurstCross.Spawn(
            DamageCenter, boss.transform.eulerAngles.y, p, boss.Health, boss);

        // チェシャ猫の消失。姿は消えるが判定・AIはそのまま(見た目だけの透明化)。
        if (cfg.VanishDuringBurst)
        {
            boss.BlendRushMaterialToTransparent(cfg.VanishFadeDuration);
            _vanished = true;
        }
    }

    // RandomizeBurstPattern 時の抽選候補。怒りモードは回転系(渦巻き/風車)に限定して連打感を出す。
    private static readonly B1_ManaBurstCross.Pattern[] s_allBurstPatterns =
        (B1_ManaBurstCross.Pattern[])System.Enum.GetValues(typeof(B1_ManaBurstCross.Pattern));
    private static readonly B1_ManaBurstCross.Pattern[] s_enragedBurstPatterns =
    {
        B1_ManaBurstCross.Pattern.Spiral,
        B1_ManaBurstCross.Pattern.Pinwheel,
    };

    // 直前に出したパターン。ランダム抽選で同じ絵が2連続すると単調なので避ける。
    private static B1_ManaBurstCross.Pattern s_lastBurstPattern = (B1_ManaBurstCross.Pattern)(-1);

    private B1_ManaBurstCross.Pattern SelectBurstPattern()
    {
        bool enragedSpin = bb.IsEnraged && cfg.EnragedUseSpiral;

        if (!cfg.RandomizeBurstPattern)
            return enragedSpin ? B1_ManaBurstCross.Pattern.Spiral : cfg.BurstPattern;

        var pool = enragedSpin ? s_enragedBurstPatterns : s_allBurstPatterns;
        var pick = pool[Random.Range(0, pool.Length)];
        if (pick == s_lastBurstPattern && pool.Length > 1)
            pick = pool[(System.Array.IndexOf(pool, pick) + 1) % pool.Length];

        s_lastBurstPattern = pick;
        return pick;
    }

    // 従来の即時全範囲ダメージ（UseDamageWave = false 用のフォールバック）。
    private void ApplyInstantAoe()
    {
        Collider[] hits = Physics.OverlapSphere(
            DamageCenter,
            cfg.AoeRadius,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        var damagedTargets = new HashSet<IHealth>();
        if (_telegraphDamagedTarget != null)
            damagedTargets.Add(_telegraphDamagedTarget);
        foreach (Collider hit in hits)
        {
            IHealth health = hit.GetComponent<IHealth>() ?? hit.GetComponentInParent<IHealth>();
            if (health == null || health.IsDead) continue;
            // ボス自身（AoE の中心にいる）は巻き込まない＝自傷を防ぐ
            if (ReferenceEquals(health, boss.Health)) continue;
            if (!damagedTargets.Add(health)) continue;

            CombatSystem.Instance.Damage(
                health,
                cfg.Damage,
                cfg.Effects.WithPlayerDamageType(cfg.DamageType),
                boss);
        }
    }

    // 着地：完了フラッシュを再生させて自壊に任せる（以後この参照からは触らない）。
    private void DetonateTelegraph()
    {
        if (_telegraph != null)
        {
            _telegraph.Detonate();
            _telegraph = null;
        }
    }

    // 途中割り込み：予告はシールを剥がすように消す（自壊まで自走する）。
    private void DestroyTelegraph()
    {
        if (_telegraph != null)
        {
            _telegraph.Dismiss();
            _telegraph = null;
        }
    }

    public override void Exit()
    {
        DestroyTelegraph(); // 着地前に割り込まれた場合だけ残っている。着地済みなら null なので何もしない

        // 途中割り込み(死亡・ダウン等)なら、まだ爆ぜていない爆発を予告ごと消す。
        // 通常完了(IsFinished)や爆発開始後は自走に任せる(B1_DamageWaveと同じ扱い)。
        if (!IsFinished)
            _manaBurst?.CancelIfCharging();
        _manaBurst = null;

        // 透明化したまま抜けないよう、割り込み時は即座に元の姿へ戻す(Rushと同じ復帰API)。
        if (_vanished)
        {
            boss.CompleteRushMaterialBlendToNormal();
            _vanished = false;
        }
        boss.StopTrail();
        boss.EnableTailHitbox(false);
        boss.AnimatorBridge.ResumeAnimator(); // アニメ速度を戻す
        base.Exit();
    }
}
