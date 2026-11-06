using UnityEngine;

/// <summary>
/// Phase2 の戦闘状態。Cinema終了直後は設定時間だけ待機してから最初の行動を始める。
/// </summary>
public class B1_Phase2State : IStatusState
{
    private readonly B1_Boss         boss;
    private readonly IBlackboard     bb;
    private readonly B1_StateMachine stateMachine;
    private float graceTimer;
    private bool combatStarted;

    public B1_Phase2State(B1_Boss boss, IBlackboard bb, B1_StateMachine stateMachine)
    {
        this.boss         = boss;
        this.bb           = bb;
        this.stateMachine = stateMachine;
    }

    public string StateName => "Phase2";

    public void Enter()
    {
        // 通常は Phase2 Timeline の EnrageMaterial Signal で切り替え済み。
        // Timeline 未設定・中断時も Phase2 戦闘では必ず怒り用の見た目になるよう補完する。
        boss.ActivateEnragedVisual();

        // 通常は Phase2 突入演出中に TailAttackJump → TailAttackLand で着地済み。
        // phase2ResetTarget 未設定時のみしっぽ立ちのまま AnimatorStop されているので、
        // 演出明けに解除して TailAttackLand で通常姿勢へ降りる。
        boss.AnimatorBridge.ResumeAnimator();
        if (!boss.HasPhase2ResetLanded)
            boss.AnimatorBridge.PlayTailAttackLand();

        graceTimer = Mathf.Max(0f, boss.Phase2PostCinemaIdleDuration);
        combatStarted = false;
        boss.ActionController.SetAction(null);
        if (graceTimer <= 0f) StartCombat();
    }

    public void Update()
    {
        if (bb.HpRatio <= 0f)
        {
            stateMachine.ChangeState(stateMachine.DeathState);
            return;
        }

        if (!combatStarted)
        {
            graceTimer -= Time.deltaTime;
            if (graceTimer <= 0f) StartCombat();
        }
    }

    private void StartCombat()
    {
        combatStarted = true;
        var first = boss.AIController.SelectNextAction();
        boss.ActionController.SetAction(first);
    }

    public void Exit() { }
}
