/// <summary>
/// 現在の EnemyAction を管理するコントローラー（Boss01 の B1_ActionController 相当）。
/// SetAction で切り替え、Update で毎フレーム進行し、IsFinished で自動終了する。
/// </summary>
public class EnemyActionController
{
    private EnemyAction current;

    public bool HasAction => current != null;
    public EnemyAction CurrentAction => current;
    public string CurrentActionName => current?.GetType().Name ?? "-";

    public void SetAction(EnemyAction action)
    {
        current?.Exit();
        current = action;
        current?.Enter();
    }

    public void Update()
    {
        if (current == null) return;
        if (current.IsFinished) { current.Exit(); current = null; return; }
        current.Update();
    }

    public void Abort()
    {
        current?.Exit();
        current = null;
    }
}
