using System;

/// <summary>
/// 現在のアクションを実行し、完了したら OnActionFinished を発行する。
/// </summary>
public class B1_ActionController
{
    private B1_Action currentAction;

    /// <summary>現在のアクションが終了したときに発火する。</summary>
    public event Action OnActionFinished;

    public B1_Action CurrentAction => currentAction;
    public string CurrentActionName => currentAction?.GetType().Name ?? "None";

    public void SetAction(B1_Action action)
    {
        currentAction?.Exit();
        currentAction = action;
        currentAction?.Enter();
    }

    public void Update()
    {
        if (currentAction == null) return;

        if (currentAction.IsFinished)
        {
            currentAction.Exit();
            currentAction = null;
            OnActionFinished?.Invoke();
            return;
        }

        currentAction.Update();
    }

    public void ForceInterrupt(B1_Action interruptAction)
    {
        currentAction?.Exit();
        currentAction = interruptAction;
        currentAction?.Enter();
    }
}
