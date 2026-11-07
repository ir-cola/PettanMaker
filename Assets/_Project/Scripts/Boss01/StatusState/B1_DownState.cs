using UnityEngine;

/// <summary>
/// 体力半分で Phase2 へ移行する前に挟むダウン状態。3段階で進行する。
///
/// ① Showcase : Down モーション + Y軸浮遊 + X軸-90度（上向き）へイージング。TPS からボスの寄り構図へ
///               イージング遷移したカメラで一定時間（ShowcaseDuration）見せる。
///               突入の瞬間の強い画面フラッシュは B1_PostProcessFX が OnStateChanged で発火。
/// ② ToIdle   : Down → Down_Idle へブレンド遷移しながら、突入時に上げた Y座標と X軸の傾きを
///               同じ尺（IdleBlendDuration）のイージングで戻して着地する。カメラは TPS へ復帰。
/// ③ Idle     : Down_Idle モーションで無防備。VFX_Down（VFX_Enemy04_Down）を付けて
///               Duration の間プレイヤーが自由に攻撃できる（入力凍結なし・無敵なし）。
///               経過したら Phase2 移行演出（既存の EnterPhase2）へ進む。
/// </summary>
public class B1_DownState : IStatusState
{
    private enum Phase { Showcase, ToIdle, Idle }

    private readonly B1_Boss         boss;
    private readonly IBlackboard     bb;
    private readonly B1_StateMachine stateMachine;

    private Phase phase;
    private float timer;
    private bool  downVFXEmissionStopped;
    private float downVFXStopLeadTime;

    // X軸の傾き（上向き）。ボス本体の rotation を直接回す。
    // Hover 中は Locomotion がプレイヤーへの自動回転を行わないため、ここで書いた回転は保持される。
    private Quaternion baseRotation;   // ダウン突入時の向き（ヨー）。傾きはこれに加算し、戻り先もこれ。
    private float      tiltElapsed;    // 傾きイージングの経過秒（Showcase=適用 / ToIdle=戻し で使い回す）

    public B1_DownState(B1_Boss boss, IBlackboard bb, B1_StateMachine stateMachine)
    {
        this.boss         = boss;
        this.bb           = bb;
        this.stateMachine = stateMachine;
    }

    public string StateName => "Down";

    private DownStateConfig Config =>
        boss.Settings != null ? boss.Settings.Down : null;

    public void Enter()
    {
        var cfg = Config;
        phase = Phase.Showcase;
        timer = cfg != null ? cfg.ShowcaseDuration : 2f;

        // 行動を中断して固める（ヒットボックスはアクションの Exit で閉じるが保険で明示）。
        boss.ActionController.SetAction(null);
        boss.EnableHitbox(false);
        boss.EnableTailHitbox(false);

        // Down モーション + 浮遊開始
        boss.AnimatorBridge.PlayDown();
        boss.Locomotion.BeginHover(
            cfg != null ? cfg.RiseHeight : 1.2f,
            cfg != null ? cfg.RiseDuration : 0.6f);
        boss.Effects.StartDownShake(
            cfg != null ? cfg.ShakeAmplitude : 0.06f,
            cfg != null ? cfg.ShakeFrequency : 35f);
        boss.Effects.StartDownMaterialFlash(
            cfg != null ? cfg.MaterialFlashInterval : 0.125f,
            cfg != null ? cfg.MaterialFlashTotalDuration : 2f,
            cfg != null ? cfg.MaterialFlashAmount : 0.85f,
            cfg != null ? cfg.MaterialFlashWhite : Color.white,
            cfg != null ? cfg.MaterialFlashRed : Color.red);

        // X軸の傾き：突入の瞬間から TiltStartAngleX（-60=既に上向き気味）でスナップ。
        // スナップは同フレームに発火する突入フラッシュ（B1_PostProcessFX）が隠す。
        // 以降 Update で RiseDuration かけて TiltAngleX（-90）までイージングする。
        baseRotation = boss.transform.rotation;
        tiltElapsed  = 0f;
        ApplyTiltAngle(TiltStartAngle);

        // 演出：寄りカメラ（突入の画面フラッシュは B1_PostProcessFX 側）
        boss.CameraDirector?.BeginDownCamera();
    }

    public void Update()
    {
        // ダウン中も無敵にしないため、殴り切られたら死亡へ。
        if (bb.HpRatio <= 0f)
        {
            stateMachine.ChangeState(stateMachine.DeathState);
            return;
        }

        switch (phase)
        {
            case Phase.Showcase:
                // ExitTime などで Down モーションから抜けてしまった場合に戻す。
                boss.AnimatorBridge.HoldDown();
                // X軸を TiltStartAngleX（-60）→ TiltAngleX（-90=上向き）へ、浮き上がりと同じ尺でイージング。
                tiltElapsed += Time.deltaTime;
                ApplyTiltAngle(Mathf.Lerp(TiltStartAngle, TiltAngle,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tiltElapsed / RiseDuration))));
                timer -= Time.deltaTime;
                if (timer <= 0f) BeginToIdle();
                break;

            case Phase.ToIdle:
                // Down_Idle へのブレンドと同じ尺で、X軸の傾きを 0 へ戻しながら降下中。着地したら攻撃チャンスへ。
                tiltElapsed += Time.deltaTime;
                ApplyTiltAngle(Mathf.Lerp(TiltAngle, 0f,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tiltElapsed / IdleBlendDuration))));
                if (boss.Locomotion.IsHoverLanded) BeginIdle();
                break;

            case Phase.Idle:
                boss.AnimatorBridge.HoldDownIdle();
                timer -= Time.deltaTime;
                if (!downVFXEmissionStopped && timer <= downVFXStopLeadTime)
                {
                    downVFXEmissionStopped = true;
                    boss.Effects.StopDownVFX();
                }
                if (timer <= 0f) stateMachine.EnterPhase2();
                break;
        }
    }

    // ② カメラを TPS へ返し、Down_Idle へブレンドしながら浮遊と X軸の傾きを解いて降下する。
    private void BeginToIdle()
    {
        phase       = Phase.ToIdle;
        tiltElapsed = 0f;
        float blend = IdleBlendDuration;
        boss.CameraDirector?.EndDownCamera();
        boss.Effects.StopDownShake();
        boss.Effects.StopDownMaterialFlash();
        boss.AnimatorBridge.PlayDownIdle(blend);
        boss.Locomotion.EndHover(blend);
    }

    // ③ 着地完了。傾きを確実に 0 へ戻し、VFX_Down を付けて攻撃チャンスの計時を始める。
    private void BeginIdle()
    {
        phase = Phase.Idle;
        timer = Config != null ? Config.Duration : 6f;
        downVFXEmissionStopped = false;
        ApplyTiltAngle(0f);
        boss.Effects.PlayDownVFX();
        downVFXStopLeadTime = CalculateDownVFXStopLeadTime();
    }

    public void Exit()
    {
        // Death への割り込みなど、どの抜け方でも演出を確実に畳む。
        boss.Effects.StopDownVFX();
        boss.Effects.StopDownShake();
        boss.Effects.StopDownMaterialFlash();
        boss.CameraDirector?.EndDownCamera();

        // 空中のまま抜けた場合は即着地させ、傾きも元の向きへ戻す。
        if (!boss.Locomotion.IsHoverLanded)
            boss.Locomotion.CancelHover();
        boss.transform.rotation = baseRotation;
    }

    // ダウン突入時の向き（ヨー）を基準に X軸回転（度）を適用する。
    private void ApplyTiltAngle(float degrees)
    {
        boss.transform.rotation = baseRotation * Quaternion.Euler(degrees, 0f, 0f);
    }

    private float TiltStartAngle    => Config != null ? Config.TiltStartAngleX : -60f;
    private float TiltAngle         => Config != null ? Config.TiltAngleX : -90f;
    private float RiseDuration      => Config != null ? Config.RiseDuration : 0.6f;
    private float IdleBlendDuration => Config != null ? Config.IdleBlendDuration : 0.5f;

    // VFX の最長寿命から Phase2 のリセット移動＋溜め時間を引き、カメラ Timeline 開始時に
    // 最後の粒子が自然に消え切るタイミングを逆算する。
    private float CalculateDownVFXStopLeadTime()
    {
        float particleLifetime = boss.Effects != null
            ? boss.Effects.DownVFXMaxParticleLifetime
            : 0f;
        float cameraLead = boss.Phase2ResetLeadEstimate;
        if (boss.CameraDirector != null)
            cameraLead += boss.CameraDirector.Phase2ResetLeadTime;
        float margin = Config != null ? Config.VFXCleanupMargin : 0.15f;
        return Mathf.Max(0f, particleLifetime - cameraLead + margin);
    }
}
