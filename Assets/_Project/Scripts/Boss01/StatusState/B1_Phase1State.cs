using UnityEngine;

/// <summary>
/// Phase1 の通常戦闘状態。Phase・HP を監視して各状態へ遷移する。
/// 登場演出直後は一定時間 Idle で待ち、いきなりプレイヤーへ攻撃しないようにする。
/// </summary>
public class B1_Phase1State : IStatusState
{
    private readonly B1_Boss         boss;
    private readonly IBlackboard     bb;
    private readonly B1_StateMachine stateMachine;

    private float graceTimer;   // 戦闘開始の攻撃猶予（Idle）残り秒
    private bool  combatStarted;

    public B1_Phase1State(B1_Boss boss, IBlackboard bb, B1_StateMachine stateMachine)
    {
        this.boss         = boss;
        this.bb           = bb;
        this.stateMachine = stateMachine;
    }

    public string StateName => "Phase1";

    public void Enter()
    {
        // 戦闘開始の猶予：一定時間はアクションを持たせず（Idle で）待つ。
        graceTimer    = Mathf.Max(0f, boss.CombatStartIdleDuration);
        combatStarted = false;
        boss.ActionController.SetAction(null);   // 攻撃を選ばず待機
        if (graceTimer <= 0f) StartCombat();
    }

    public void Update()
    {
        if (bb.HpRatio <= 0f)
        {
            stateMachine.ChangeState(stateMachine.DeathState);
            return;
        }

        if (bb.CurrentPhase >= 2)
        {
            // Phase2 突入演出の前に、まずダウン（プレイヤーの攻撃チャンス）を挟む。
            stateMachine.EnterDown();
            return;
        }

        // 猶予中は攻撃を始めない（Idle）。経過したら最初のアクションへ。
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
