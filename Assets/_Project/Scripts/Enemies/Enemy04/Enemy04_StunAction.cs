using UnityEngine;

public class Enemy04_StunAction : EnemyAction
{
    private enum Phase { Stunned, RecoverShake }

    private readonly Enemy04 enemy;
    private readonly float stunDuration;
    private readonly float recoverShakeDuration;
    private readonly string paramIsDown;

    private Phase phase;
    private float timer;

    public Enemy04_StunAction(Enemy04 enemy, float stunDuration, float recoverShakeDuration, string paramIsDown)
        : base(enemy)
    {
        this.enemy                = enemy;
        this.stunDuration         = stunDuration;
        this.recoverShakeDuration = recoverShakeDuration;
        this.paramIsDown          = paramIsDown;
    }

    public override void Enter()
    {
        phase = Phase.Stunned;
        timer = stunDuration;
        owner.Agent.ResetPath();
        enemy.CurrentMotion = Enemy04.MotionState.Down_01;
        owner.AnimatorBridge?.SetBool(paramIsDown, true);
        enemy.OnAttackStunned();
    }

    public override void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        switch (phase)
        {
            case Phase.Stunned:
                phase = Phase.RecoverShake;
                timer = recoverShakeDuration;
                enemy.CurrentMotion = Enemy04.MotionState.Down_Recover;
                enemy.OnDownRecoverStarted();
                break;

            case Phase.RecoverShake:
                IsFinished = true;
                break;
        }
    }

    public override void Exit()
    {
        base.Exit();
        owner.AnimatorBridge?.SetBool(paramIsDown, false);
        if (IsFinished)
            enemy.OnChargeActionFinished();
    }
}
