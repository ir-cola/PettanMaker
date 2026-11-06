using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// すべての攻撃・移動アクションの基底クラス。
/// AnimatorBridge のイベント購読を Exit 時に一括解除する。
/// </summary>
public abstract class B1_Action
{
    protected B1_Boss     boss;
    protected IBlackboard bb;

    public bool IsFinished { get; protected set; }

    private readonly List<Action> unsubList = new List<Action>();

    protected B1_Action(B1_Boss boss, IBlackboard bb)
    {
        this.boss = boss;
        this.bb   = bb;
    }

    public abstract void Enter();
    public abstract void Update();

    public virtual void Exit()
    {
        foreach (var u in unsubList) u();
        unsubList.Clear();

        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
    }

    // ---- AnimatorBridge イベント購読ヘルパー（Exit で自動解除） ----

    protected void SubscribeTelegraphStart(Action h)
    {
        boss.AnimatorBridge.OnTelegraphStartEvent += h;
        unsubList.Add(() => boss.AnimatorBridge.OnTelegraphStartEvent -= h);
    }

    protected void SubscribeTelegraphEnd(Action h)
    {
        boss.AnimatorBridge.OnTelegraphEndEvent += h;
        unsubList.Add(() => boss.AnimatorBridge.OnTelegraphEndEvent -= h);
    }

    protected void SubscribeHitboxActivate(Action h)
    {
        boss.AnimatorBridge.OnHitboxActivateEvent += h;
        unsubList.Add(() => boss.AnimatorBridge.OnHitboxActivateEvent -= h);
    }

    protected void SubscribeAttackStateStarted(Action h)
    {
        boss.AnimatorBridge.OnAttackStateStartedEvent += h;
        unsubList.Add(() => boss.AnimatorBridge.OnAttackStateStartedEvent -= h);
    }

    protected void SubscribeHitboxDeactivate(Action h)
    {
        boss.AnimatorBridge.OnHitboxDeactivateEvent += h;
        unsubList.Add(() => boss.AnimatorBridge.OnHitboxDeactivateEvent -= h);
    }

    protected void SubscribeAttackFinished(Action h)
    {
        boss.AnimatorBridge.OnAttackFinishedEvent += h;
        unsubList.Add(() => boss.AnimatorBridge.OnAttackFinishedEvent -= h);
    }
}

/// <summary>
/// 攻撃前に猫らしく腰を落とし、プレイヤーへ狙いを定めてから本来のActionへ繋ぐ。
/// </summary>
public sealed class B1_AimCrouchAction : B1_Action
{
    private readonly B1_Action nextAction;
    private float elapsed;
    private bool startedNext;

    public B1_AimCrouchAction(B1_Boss boss, IBlackboard bb, B1_Action nextAction)
        : base(boss, bb)
    {
        this.nextAction = nextAction;
    }

    public override void Enter()
    {
        elapsed = 0f;
        startedNext = false;
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
        boss.BeginAimCrouch();
    }

    public override void Update()
    {
        var cfg = boss.Settings.AimCrouch;
        float enter = Mathf.Max(0f, cfg.EnterDuration);
        float hold = Mathf.Max(0f, cfg.HoldDuration);
        float release = Mathf.Max(0f, cfg.ReleaseDuration);
        float total = enter + hold + release;

        elapsed += Time.deltaTime;
        boss.Locomotion.AimAtPlayer(cfg.TurnSpeedMultiplier);

        float pose;
        if (elapsed < enter)
            pose = enter > 0f ? Mathf.SmoothStep(0f, 1f, elapsed / enter) : 1f;
        else if (elapsed < enter + hold)
            pose = 1f;
        else
            pose = release > 0f
                ? 1f - Mathf.SmoothStep(0f, 1f, (elapsed - enter - hold) / release)
                : 0f;

        boss.SetAimCrouch(pose, cfg.CrouchAmount, cfg.WidthScale);

        if (startedNext || elapsed < total) return;
        startedNext = true;
        boss.ActionController.ForceInterrupt(nextAction);
    }

    public override void Exit()
    {
        boss.EndAimCrouch();
        base.Exit();
    }
}
