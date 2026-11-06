using System;
using System.Collections.Generic;

/// <summary>
/// 敵の1つの行動を表す基底クラス（Boss01 の B1_Action 相当）。
/// Enter/Update/Exit ライフサイクルを持ち、IsFinished で完了を通知する。
/// </summary>
public abstract class EnemyAction
{
    protected readonly EnemyBase owner;
    public bool IsFinished { get; protected set; }

    private readonly List<Action> unsubList = new List<Action>();

    protected EnemyAction(EnemyBase owner)
    {
        this.owner = owner;
    }

    public abstract void Enter();
    public abstract void Update();

    public virtual void Exit()
    {
        foreach (var unsubscribe in unsubList)
            unsubscribe();

        unsubList.Clear();
    }

    protected void SubscribeAttackImpact(Action handler)
    {
        if (owner.AnimatorBridge == null)
            return;

        owner.AnimatorBridge.OnAttackImpactEvent += handler;
        unsubList.Add(() => owner.AnimatorBridge.OnAttackImpactEvent -= handler);
    }

    protected void SubscribeAttackFinished(Action handler)
    {
        if (owner.AnimatorBridge == null)
            return;

        owner.AnimatorBridge.OnAttackFinishedEvent += handler;
        unsubList.Add(() => owner.AnimatorBridge.OnAttackFinishedEvent -= handler);
    }
}
