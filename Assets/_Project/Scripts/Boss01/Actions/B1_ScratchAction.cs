using UnityEngine;

/// <summary>
/// 3連ひっかき攻撃。Scratch1→2→3 の順に再生し、各ヒットで当たり判定を制御する。
/// </summary>
public class B1_ScratchAction : B1_Action
{
    private static readonly string[] Clips = { "Scratch01", "Scratch02", "Scratch03" };
    private int   currentHit;
    private int   totalHits;
    private float _advanceTimer;
    private float _advanceSpeed;
    private bool  _playNextHit;

    public B1_ScratchAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        currentHit = 0;
        totalHits  = WeightedHitCount(boss.Settings.Scratch.HitCountWeights);
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Strafe);

        int[] dmgPerHit = boss.Settings.Scratch.DamagePerHit;
        DamageContext[] fxPerHit = boss.Settings.Scratch.EffectsPerHit;
        SubscribeAttackStateStarted(BeginAdvance);
        SubscribeHitboxActivate(() =>
        {
            int dmg = (dmgPerHit != null && currentHit < dmgPerHit.Length) ? dmgPerHit[currentHit] : 1;
            DamageContext ctx = (fxPerHit != null && currentHit < fxPerHit.Length) ? fxPerHit[currentHit] : default;
            ctx = ctx.WithPlayerDamageType(boss.Settings.Scratch.DamageType);
            boss.EnableHitbox(true, dmg, ctx);
            // 3段目（踏み込みの大振り）のヒットに合わせて画面衝撃波を出す。表示位置は SO の positionOffset で調整。
            if (currentHit == 2)
                boss.PlayShockwave(B1_EffectManager.CombatShockwave.ScratchFinisher);
        });
        SubscribeHitboxDeactivate(() => boss.EnableHitbox(false));
        SubscribeAttackFinished(OnHitFinished);

        StepAndPlay(currentHit);
    }

    private void OnHitFinished()
    {
        boss.EnableHitbox(false);
        currentHit++;
        if (currentHit < totalHits)
            _playNextHit = true;
        else
            IsFinished = true;
    }

    private void StepAndPlay(int hitIndex)
    {
        _advanceTimer = 0f;
        boss.PlaySlashVFX();   // ひっかき：各振りごとに spawn
        boss.PlayScratchSound(hitIndex); // 1→2→3段目でピッチと重さを変える
        boss.AnimatorBridge.PlayAttack(Clips[hitIndex]);
    }

    private void BeginAdvance()
    {
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);

        float[] advances = boss.Settings.Scratch.AdvancePerHit;
        float duration = boss.Settings.Scratch.AdvanceDuration;
        if (advances == null || currentHit >= advances.Length || duration <= 0f)
            return;

        _advanceSpeed = advances[currentHit] / duration;
        _advanceTimer = duration;
    }

    public override void Update()
    {
        // Animator の OnStateExit 中に次ステートへ切り替えると、
        // 元の Locomotion 遷移に上書きされるため次フレームで再生する。
        if (_playNextHit)
        {
            _playNextHit = false;
            StepAndPlay(currentHit);
        }

        if (_advanceTimer > 0f)
        {
            float stepTime = Mathf.Min(Time.deltaTime, _advanceTimer);
            boss.Locomotion.StepForward(_advanceSpeed * stepTime);
            _advanceTimer -= stepTime;
        }
    }

    private static int WeightedHitCount(float[] weights)
    {
        float total = 0f;
        foreach (var w in weights) total += Mathf.Max(0f, w);
        float roll = UnityEngine.Random.Range(0f, total);
        float accum = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            accum += Mathf.Max(0f, weights[i]);
            if (roll <= accum) return i + 1;
        }
        return weights.Length;
    }

    public override void Exit()
    {
        _playNextHit = false;
        _advanceTimer = 0f;
        boss.EnableHitbox(false);
        boss.AnimatorBridge.ResumeAnimator();
        base.Exit();
    }
}
