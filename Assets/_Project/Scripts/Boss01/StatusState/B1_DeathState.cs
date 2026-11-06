/// <summary>
/// 死亡状態。デスアニメを再生する。
/// この状態からは復帰しない。
/// </summary>
public class B1_DeathState : IStatusState
{
    private readonly B1_Boss boss;

    public B1_DeathState(B1_Boss boss)
    {
        this.boss = boss;
    }

    public string StateName => "Death";

    public void Enter()
    {
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
        boss.Locomotion.FacingLocked = true; // 死亡後にプレイヤーへ振り向かないよう回転追従を止める
        boss.ActionController.SetAction(null);
        boss.AnimatorBridge.PlayDeath();
        boss.CameraDirector?.PlayDefeat();
    }

    public void Update() { }
    public void Exit()   { }
}
