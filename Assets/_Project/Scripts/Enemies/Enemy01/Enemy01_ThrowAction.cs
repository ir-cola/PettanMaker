using UnityEngine;

/// <summary>
/// Enemy01 の投擲攻撃シーケンス（Enemy01.AttackSequence の coroutine 版を置き換え）。
/// Animator は Finding → Attack の順に再生し、投擲タイミングだけコードで制御する。
/// </summary>
public class Enemy01_ThrowAction : EnemyAction
{
    private enum Phase { Finding, Windup, Recovery }

    private readonly Enemy01 enemy;
    private readonly float findingDuration;
    private readonly float windupDuration;
    private readonly float throwRecovery;
    private readonly float attackCooldown;
    private readonly string triggerFinding;
    private readonly string triggerAttack0;
    private readonly string boolIsAttacking;

    // Attack アニメのフレーム基準タイミング
    private readonly float attackFPS;
    private readonly int carryEndFrame;
    private readonly int throwFrame;
    private const string AttackStateName = "Attack";

    private Phase phase;
    private float timer;
    private bool hasEnteredAttackState;
    private float attackAnimationWaitTimer;
    private Enemy01_Projectile activeProjectile;   // 保持中の投擲物（フレーム進捗で駆動）

    public Enemy01_ThrowAction(
        Enemy01 enemy,
        float findingDuration, float windupDuration, float throwRecovery, float attackCooldown,
        string triggerFinding, string triggerAttack0, string boolIsAttacking,
        float attackFPS, int carryEndFrame, int throwFrame)
        : base(enemy)
    {
        this.enemy = enemy;
        this.findingDuration = findingDuration;
        this.windupDuration = windupDuration;
        this.throwRecovery = throwRecovery;
        this.attackCooldown = attackCooldown;
        this.triggerFinding = triggerFinding;
        this.triggerAttack0 = triggerAttack0;
        this.boolIsAttacking = boolIsAttacking;
        this.attackFPS = Mathf.Max(1f, attackFPS);
        this.carryEndFrame = carryEndFrame;
        this.throwFrame = throwFrame;
    }

    public override void Enter()
    {
        phase = Phase.Finding;
        timer = findingDuration;
        activeProjectile = null;
        owner.Agent.ResetPath();
        enemy.CurrentMotion = Enemy01.MotionState.Finding;
        owner.AnimatorBridge?.SetBool(boolIsAttacking, false);
        owner.AnimatorBridge?.SetTrigger(triggerFinding);
    }

    public override void Update()
    {
        owner.FacePlayer();
        TrackAttackState();
        DriveProjectile();          // Attack フレーム進捗に合わせて運搬/投擲
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        AdvancePhase();
    }

    public override void Exit()
    {
        base.Exit();
        owner.AnimatorBridge?.SetBool(boolIsAttacking, false);
        owner.SetCooldown(attackCooldown);
        enemy.OnAttackFinished();
    }

    private void AdvancePhase()
    {
        switch (phase)
        {
            case Phase.Finding:
                phase = Phase.Windup;
                timer = windupDuration;
                hasEnteredAttackState = false;
                attackAnimationWaitTimer = 0f;
                enemy.CurrentMotion = Enemy01.MotionState.Attack_0;
                owner.AnimatorBridge?.SetBool(boolIsAttacking, true);
                owner.AnimatorBridge?.SetTrigger(triggerAttack0);
                // 投擲物をバッグ地点に生成して保持。実際の運搬/投擲は Attack アニメの
                // フレーム進捗に合わせて DriveProjectile() が駆動する。
                activeProjectile = enemy.SpawnThrowProjectile();
                break;

            case Phase.Windup:
                phase = Phase.Recovery;
                timer = throwRecovery;
                enemy.CurrentMotion = Enemy01.MotionState.Attack_1;
                break;

            case Phase.Recovery:
                if (!IsAttackAnimationComplete())
                    return;

                IsFinished = true;
                break;
        }
    }

    // Attack アニメのフレーム進捗で投擲物を運搬→投擲する。
    // Frame0→CarryEndFrame で バッグ→耳、ThrowFrame で放物線投擲。
    private void DriveProjectile()
    {
        if (activeProjectile == null) return;

        float frame = ComputeAttackFrame();
        float k = carryEndFrame > 0 ? frame / carryEndFrame : 1f;
        activeProjectile.UpdateCarry(k);

        if (frame >= throwFrame)
        {
            activeProjectile.Release();
            activeProjectile = null;
        }
    }

    // 再生中の Attack ステートの経過フレームを返す。Attack がまだ始まっていなければ 0
    // （＝バッグ地点で待機）。Animator が無い場合は即投擲できるよう throwFrame を返す。
    private float ComputeAttackFrame()
    {
        Animator animator = owner.EnemyAnimator;
        if (animator == null) return throwFrame;

        AnimatorStateInfo info;
        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (!next.IsName(AttackStateName)) return 0f;   // まだ Attack へ入っていない
            info = next;
        }
        else
        {
            info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(AttackStateName)) return 0f;
        }

        // normalizedTime(0..1) × ステート長(秒) = 経過秒 → ×FPS で経過フレーム。
        return info.normalizedTime * info.length * attackFPS;
    }

    private bool IsAttackAnimationComplete()
    {
        attackAnimationWaitTimer += Time.deltaTime;

        TrackAttackState();

        if (owner.EnemyAnimator == null)
            return true;

        if (owner.EnemyAnimator.IsInTransition(0))
            return false;

        AnimatorStateInfo state = owner.EnemyAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName("Attack"))
            return state.normalizedTime >= 1f;

        if (hasEnteredAttackState)
            return true;

        return attackAnimationWaitTimer >= 2f;
    }

    private void TrackAttackState()
    {
        Animator animator = owner.EnemyAnimator;
        if (animator == null)
            return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName("Attack"))
        {
            hasEnteredAttackState = true;
        }
    }
}
