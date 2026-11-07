using UnityEngine;

/// <summary>
/// タメ演出フェーズ。Settings.RushCharge.Duration 秒待機後 B1_RushAction へ連鎖する。
/// 突進の実体は B1_RushAction が担う。
/// タメ中は突進経路の予告ライン（B1_TelegraphLine）を表示し、プレイヤーを狙い続けて追従させる。
/// </summary>
public class B1_ChargeAction : B1_Action
{
    private float elapsed;
    private float _duration; // 実効タメ時間（怒りモード中は AttackSpeedMult で短縮）
    private bool startedRush;
    private B1_TelegraphLine _telegraph;

    public B1_ChargeAction(B1_Boss boss, IBlackboard bb) : base(boss, bb) { }

    public override void Enter()
    {
        elapsed = 0f;
        startedRush = false;
        // 怒りモード（激昂）中はタメ時間を短縮して攻撃テンポを上げる。
        _duration = boss.Settings.RushCharge.Duration / boss.AttackSpeedMult;
        var bgm = boss.Settings.BgmAccent;
        if (bgm != null && bgm.Enabled)
            BGMManager.SetVolumeScale(bgm.AnticipationVolume, bgm.AnticipationFadeDuration);

        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Strafe); // タメ中に横移動でプレッシャー
        boss.AnimatorBridge.PlayAttack("Charge");
        boss.PlayRushChargeSound();
        boss.BlendRushMaterialToTransparent(_duration);
        boss.StartScalePulse(_duration,
                             boss.Settings.RushCharge.ScalePeak,
                             boss.Settings.RushCharge.ScaleFrequency,
                             boss.Settings.RushCharge.ScaleFrequencyEnd,
                             boss.Settings.RushCharge.CrouchAmount,
                             boss.Settings.RushCharge.CrouchDuration);

        // 突進経路の予告ライン（チェック柄・中心透明）。タメ中はプレイヤーを狙い続ける。
        var rush = boss.Settings.Rush;
        if (rush.UseTelegraph && boss.PlayerTransform != null)
            _telegraph = B1_TelegraphLine.Spawn(
                boss.transform.position, RushEndPoint(),
                rush.TelegraphWidth, rush.TelegraphColor, rush.TelegraphMaterial);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        // ボスが横移動し、プレイヤーも動くので、予告ラインは毎フレーム経路を追従させる。
        // タメ進行度をチャージアップとして反映（手前→先端へ塗りが伸び、満タン＝発射直前）。
        if (_telegraph != null && boss.PlayerTransform != null)
        {
            _telegraph.SetLine(boss.transform.position, RushEndPoint());
            _telegraph.SetUrgency(_duration > 0f ? Mathf.Clamp01(elapsed / _duration) : 1f);
        }

        if (elapsed < _duration) return;

        boss.PlayDashStarVFX(); // タメ完了：走り出す直前に目の前へ
        startedRush = true;
        DetonateTelegraph();    // 突進開始の瞬間に発射フラッシュ（→自壊）
        // 発射の瞬間に3D衝撃波を出す（予兆→爆発の「爆発」を強調）。表示位置は B1_EffectSettings の positionOffset で調整。
        boss.PlayShockwave(B1_EffectManager.CombatShockwave.ChargeLaunch);
        boss.ActionController.ForceInterrupt(new B1_RushAction(boss, bb));
    }

    // B1_RushAction.Enter と同等の突進到達点（プレイヤー位置＋オーバーシュート）。表示用に水平面で計算する
    private Vector3 RushEndPoint()
    {
        var cfg = boss.Settings.Rush;
        Vector3 dir = boss.PlayerTransform.position - boss.transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : boss.transform.forward;
        return boss.PlayerTransform.position + dir * cfg.OvershootDistance;
    }

    // 発射：フラッシュを再生させて自壊に任せる（以後この参照からは触らない）。
    private void DetonateTelegraph()
    {
        if (_telegraph != null)
        {
            _telegraph.Detonate();
            _telegraph = null;
        }
    }

    // 途中割り込み：予告はシールを剥がすように消す（自壊まで自走する）。
    private void DestroyTelegraph()
    {
        if (_telegraph != null)
        {
            _telegraph.Dismiss();
            _telegraph = null;
        }
    }

    public override void Exit()
    {
        DestroyTelegraph(); // 発射前に割り込まれた場合だけ残っている。発射済みなら null なので何もしない
        boss.StopScalePulse();
        if (!startedRush) boss.CompleteRushMaterialBlendToNormal();
        if (!startedRush)
        {
            var bgm = boss.Settings.BgmAccent;
            if (bgm != null && bgm.Enabled) BGMManager.ResetVolumeScale(bgm.ReleaseDuration);
        }
        base.Exit();
    }
}
