/// <summary>
/// ボスの大状態（Idle・Cinematic・Phase1・Down・Phase2・Search・Death）を管理するステートマシン。
/// </summary>
public class B1_StateMachine
{
    private readonly B1_Boss boss;

    public B1_IdleState      IdleState      { get; private set; }
    public B1_CinematicState CinematicState { get; private set; }
    public B1_Phase1State    Phase1State    { get; private set; }
    public B1_DownState      DownState      { get; private set; }
    public B1_Phase2State    Phase2State    { get; private set; }
    public B1_SearchState    SearchState    { get; private set; }
    public B1_DeathState     DeathState     { get; private set; }

    private IStatusState currentState;

    /// <summary>状態が切り替わった直後 (Enter 呼び出し後) に発火する。演出系のフックポイント。</summary>
    public event System.Action<IStatusState> OnStateChanged;

    public string CurrentStateName => currentState?.StateName ?? "None";
    public bool IsCinematic => currentState == CinematicState;
    public bool IsDeath => currentState == DeathState;

    public B1_StateMachine(B1_Boss boss, IBlackboard bb)
    {
        this.boss = boss;

        IdleState      = new B1_IdleState(boss);
        CinematicState = new B1_CinematicState(boss, this);
        Phase1State    = new B1_Phase1State(boss, bb, this);
        DownState      = new B1_DownState(boss, bb, this);
        Phase2State    = new B1_Phase2State(boss, bb, this);
        SearchState    = new B1_SearchState(boss, this);
        DeathState     = new B1_DeathState(boss);
    }

    public void Start() => ChangeState(IdleState);

    public void Update() => currentState?.Update();

    public void ChangeState(IStatusState next)
    {
        if (next == null || currentState == next) return;
        currentState?.Exit();
        currentState = next;
        currentState.Enter();
        OnStateChanged?.Invoke(currentState);
    }

    // BossRoomTrigger から呼ぶ
    public void EnterCombat()
    {
        if (IsCinematic) return;
        CinematicState.Configure(() => boss.CameraDirector?.PlayEngage(), Phase1State);
        ChangeState(CinematicState);
    }

    // Phase1 から体力半分で呼ぶ。Phase2 突入演出の前にダウン（攻撃チャンス）を挟む。
    // ダウンが終わると B1_DownState が EnterPhase2() を呼んで既存の Phase2 突入演出へ進む。
    public void EnterDown()
    {
        if (IsCinematic || currentState == DownState || currentState == Phase2State) return;
        ChangeState(DownState);
    }

    public void EnterPhase2()
    {
        if (IsCinematic || currentState == Phase2State) return;
        // Phase2 突入：先にボスを Target 位置へリセット（テールアタックモーション付き）を見せてから、
        // Phase2 カメラ演出へ移る。順番付け（リセット→カメラ）は CameraDirector 側が担当する。
        CinematicState.Configure(() =>
        {
            if (boss.CameraDirector != null)
                boss.CameraDirector.PlayHalfHealthWithReset(
                    boss.BeginPhase2Reset, () => boss.IsPhase2ResetComplete, boss.Phase2ResetLeadEstimate);
            else
                boss.BeginPhase2Reset();
        }, Phase2State);
        ChangeState(CinematicState);
    }

    public void EnterSearch()
    {
        if (currentState == SearchState || currentState == DeathState) return;
        ChangeState(SearchState);
    }
}
