using UnityEngine;

/// <summary>
/// テールアタック前のタメフェーズ。
/// ChargeDuration 秒後に B1_TailAttackAction へ連鎖する。
/// </summary>
public class B1_TailChargeAction : B1_Action
{
    private float elapsed;
    private float _duration; // 実効タメ時間（怒りモード中は AttackSpeedMult で短縮）

    public B1_TailChargeAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        elapsed = 0f;
        // 怒りモード（激昂）中はタメ時間を短縮して攻撃テンポを上げる。
        _duration = boss.Settings.TailAttack.ChargeDuration / boss.AttackSpeedMult;
        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled)
            BGMManager.SetVolumeScale(bgm.AnticipationVolume, bgm.AnticipationFadeDuration);

        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
        boss.PlayTailChargeSound();
        boss.TailChargeVFX?.Play();
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;
        float t = _duration > 0f ? Mathf.Clamp01(elapsed / _duration) : 1f;
        boss.TailChargeVFX?.UpdateProgress(t);
        if (elapsed < _duration) return;

        boss.TailChargeVFX?.Stop();
        boss.ActionController.ForceInterrupt(new B1_TailAttackAction(boss, bb));
    }

    public override void Exit()
    {
        boss.StopTailChargeSound();
        boss.TailChargeVFX?.Stop();
        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled) BGMManager.ResetVolumeScale(bgm.ReleaseDuration);
        base.Exit();
    }
}
