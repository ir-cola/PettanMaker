using UnityEngine;

/// <summary>
/// 突進フェーズ。B1_ChargeAction のテレグラフ後に ForceInterrupt で呼ばれる。
/// 即座にヒットボックスをONにしてプレイヤー位置へ突進し、到達で終了する。
/// </summary>
public class B1_RushAction : B1_Action
{
    private static int nextRushToken;

    private RushActionConfig cfg;
    private int rushToken;

    public B1_RushAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        cfg = boss.Settings.Rush;
        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled) BGMManager.ResetVolumeScale(bgm.ReleaseDuration);

        rushToken = unchecked(++nextRushToken);
        Vector3 dir    = (boss.PlayerTransform.position - boss.transform.position).normalized;
        Vector3 target = boss.PlayerTransform.position + dir * cfg.OvershootDistance;

        boss.AnimatorBridge.PlayRush();
        boss.PlayRushSound();
        boss.Locomotion.BeginCharge(target, cfg.Speed);
        float restoreDuration = Vector3.Distance(boss.transform.position, target) / Mathf.Max(0.01f, cfg.Speed);
        boss.BlendRushMaterialToNormal(restoreDuration);
        boss.EnableHitbox(
            true,
            cfg.Damage,
            cfg.Effects.WithPlayerDamageType(cfg.DamageType));
        boss.PlayEyeRushVFX();
        boss.PlayDashSmokeVFX(); // 突進時の土煙
        boss.StartTrail();
    }

    public override void Update()
    {
        B1_AttackBounceObject.LaunchFromRush(boss.transform.position, cfg.BounceRadius, rushToken);

        if (boss.Locomotion.IsHopComplete || boss.Locomotion.IsWallHit)
        {
            boss.EnableHitbox(false);
            boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
            IsFinished = true;
        }
    }

    public override void Exit()
    {
        boss.StopEyeRushVFX();
        boss.StopDashSmokeVFX();
        boss.StopTrail();
        boss.CompleteRushMaterialBlendToNormal();
        boss.EnableHitbox(false);
        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled) BGMManager.ResetVolumeScale(bgm.ReleaseDuration);
        base.Exit();
    }
}
