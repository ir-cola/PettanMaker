using System;
using UnityEngine;

/// <summary>
/// カメラ演出中のボス専用状態。
/// ボスの移動を停止し現在アクションを中断して「固め」、指定のカメラカット
/// (cameraCut) を再生する。カメラ演出が終わったら next 状態へ遷移する。
///
/// 接敵時(→Phase1) と 体力半分=Phase2 突入時(→Phase2) に使う。
/// 撃破時は死亡モーションとカメラを同時に見せたいので DeathState 側が担当する。
/// </summary>
public class B1_CinematicState : IStatusState
{
    private readonly B1_Boss         boss;
    private readonly B1_StateMachine stateMachine;

    private Action       cameraCut;
    private IStatusState next;
    private float        elapsed;
    private float        minWait;
    private float        maxWait;

    // カメラ演出が何らかの理由で終わらない場合の保険 (秒, unscaled)
    private const float FallbackMaxWait = 8f;

    // 1フレームで加算する経過時間の上限 (秒)。ロードのもたつきで尺を食い尽くさないため。
    private const float MaxFrameStep = 0.1f;

    public B1_CinematicState(B1_Boss boss, B1_StateMachine stateMachine)
    {
        this.boss         = boss;
        this.stateMachine = stateMachine;
    }

    public string StateName => "Cinematic";

    /// <summary>再生するカメラカットと、演出後に遷移する状態を設定する。</summary>
    public void Configure(Action cameraCut, IStatusState next)
    {
        this.cameraCut = cameraCut;
        this.next      = next;
    }

    public void Enter()
    {
        elapsed = 0f;
        // 演出を固める。プレイヤー入力を止めないと攻撃入力が通り続け、ボスHPが
        // 変化して HP=0 で DeathState、HP<50% で Phase2 へと途中で割り込まれる。
        boss.Locomotion.SetMode(B1_Locomotion.MoveMode.Stop);
        boss.ActionController.SetAction(null);
        boss.CameraDirector?.SetPlayerInputFrozen(true);          // 根本原因：プレイヤー入力を凍結
        if (boss.Health != null) boss.Health.Invulnerable = true; // 発射済み攻撃が着弾しても無効化する保険
        // カメラカット開始
        cameraCut?.Invoke();

        minWait = boss.CameraDirector != null
            ? boss.CameraDirector.LastRequestedDuration
            : 0f;
        maxWait = Mathf.Max(FallbackMaxWait, minWait + 0.5f);
    }

    public void Update()
    {
        if (boss.Blackboard.HpRatio <= 0f)
        {
            boss.CameraDirector?.Stop();
            stateMachine.ChangeState(stateMachine.DeathState);
            return;
        }

        // 1フレームの経過量をクランプする。シーンロード直後は unscaledDeltaTime が
        // 1〜2秒の巨大値になり（maximumDeltaTime で頭打ちされない）、放置すると数フレームで
        // 尺を食い尽くして演出が一瞬で終わる。カメラ側 PlayRoutine も同じクランプが要る。
        elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep);

        // Duration 分は必ず待ち、終了後にカメラ停止 or 保険時間超過で復帰
        bool durationElapsed = elapsed >= minWait;
        bool directorFinished = boss.CameraDirector == null
                             || !boss.CameraDirector.IsPlaying;
        bool timedOut = elapsed >= maxWait;
        bool finished = boss.CameraDirector == null
                     || (durationElapsed && directorFinished)
                     || timedOut;
        // 依存関係: 登場出現(煙〜ディゾルブ)が終わるまでは戦闘(next)へ進まない。
        // ＝ RevealSequence の ④ 完了まで待ち、⑤ 戦闘Idle は Phase1 側で消化する。
        if (boss.IsEntranceRevealing) finished = false;
        if (finished)
        {
            if (timedOut) boss.CameraDirector?.Stop();
            stateMachine.ChangeState(next ?? stateMachine.Phase1State);
        }
    }

    public void Exit()
    {
        // 演出終了でプレイヤー入力と無敵を解除（next 復帰でも Death 抜けでも必ず通る）
        boss.CameraDirector?.SetPlayerInputFrozen(false);
        if (boss.Health != null) boss.Health.Invulnerable = false;
        // Phase2 リセット（Target 未設定時）のしっぽ立ち AnimatorStop が残らないよう必ず解除する
        // （Death へ抜けた場合に死亡モーションが凍結するのを防ぐ。未着地なら Phase2 側が Enter で着地させる）。
        boss.AnimatorBridge?.ResumeAnimator();
    }
}
