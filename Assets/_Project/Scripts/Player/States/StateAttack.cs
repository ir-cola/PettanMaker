/**===========================================================================

 File   : 攻撃ステート (タップ/長押しの振り分け) [StateAttack.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29  (2026/7/14 タップ=3段コンボ / 長押し=チャージ に改修)
 -----------------------------------------------------------------------------
 攻撃ボタンを押した瞬間このステートに入り、離し方で分岐する:
   ・TapThreshold 未満で離す  = タップ → StateComboAttack (3段コンボ)
   ・TapThreshold 以上 押し続け = 長押し → チャージ (据え置き。離した chargeTime で
                                  Charge1/2/3 を発動。スタンプは斜め軌道で突入)
 チャージ視覚 (エフェクト/ロックオン/落下予想サークル/Chargeモーション) は
 「長押し」と確定 (TapThreshold 超え) してから開始する。こうしないとコンボの
 素早いタップ毎にロックオン等がチラつく。
 ===========================================================================**/

using UnityEngine;

public class StateAttack : IPlayerState {

    protected IPlayerContext player;
    private float chargeTime;
    private bool  chargeStarted;      // 長押し確定でチャージ視覚を開始したか
    private bool  midChargeReached;
    private bool  fullChargeReached;
    private PlayerLockOn lockOn;
    private AttackLandingIndicator indicator;
    private PlayerClothSkills.SizeMode aliceMode;   // 入場時の体格モード (アリス小/巨大/通常)

    public StateAttack(IPlayerContext player) {
        this.player = player;
    }

    public virtual void Enter() {
        chargeTime        = 0f;
        chargeStarted     = false;
        midChargeReached  = false;
        fullChargeReached = false;
        aliceMode = player.ClothSkills != null ? player.ClothSkills.Mode : PlayerClothSkills.SizeMode.None;
        // ※ 視覚はまだ開始しない (タップかもしれない)。BeginCharge() で遅延開始。
    }

    public virtual void Update() {
        // アリス(小): コンボもチャージも不可。攻撃入力で「ちびスタンプ」を上から下へ
        // ぺちっと召喚するだけ (非ダメージ = 敵は倒せない)。押し方に関係なく即発動して抜ける。
        if (aliceMode == PlayerClothSkills.SizeMode.Small) {
            player.Attack.ChibiStampBegin();
            // ちびでも攻撃モーションを再生する (コンボ1段目のスイングを流用)。
            player.Animator.SetInteger(PlayerAnimator.ComboIndexParam, 0);
            player.Animator.SetTrigger(PlayerAnimator.ComboParam);
            player.StateMachine.ChangeActionState(new StatePlayerNull());
            return;
        }

        chargeTime += Time.deltaTime;

        if (player.Input.IsAttackHold) {
            // 押しっぱなし: TapThreshold を超えたら「長押し=チャージ」確定
            // (アリス巨大時もここでチャージ開始。タップでコンボへは落ちない)。
            if (!chargeStarted && chargeTime >= player.Attack.TapThreshold) BeginCharge();
            if (chargeStarted) UpdateCharge();
            return;
        }

        // 離した
        if (aliceMode == PlayerClothSkills.SizeMode.Giant) {
            FireGiantChargeAndExit();  // 溜まっていれば巨大スタンプ / 未達なら Idle (コンボ無し)
            return;
        }
        if (!chargeStarted) FireComboAndExit();   // 素早いタップ → コンボ
        else                FireChargeAndExit();  // 溜めていた → チャージ発動
    }

    public virtual void Exit() {
        if (!chargeStarted) return;               // タップだけなら片付け不要
        player.Attack.StopChargeEffect();
        player.Animator.SetInteger(PlayerAnimator.ChargeLvParam, 0);
        TPSCameraController.Instance?.SetChargeProgress(0f);
        TPSCameraController.Instance?.SetChargeFov(0f);
        lockOn?.SetActive(false);
        // ※ 予想サークルはここでは消さない (発動時はスタンプ着地で PlayerAttack が TryHide)。
    }

    /// <summary>長押し確定: チャージ視覚を開始する。</summary>
    private void BeginCharge() {
        chargeStarted = true;
        player.Attack.StartChargeEffect();

        lockOn = PlayerLockOn.Ensure(player.Attack.gameObject);
        lockOn.SetActive(true);

        indicator = AttackLandingIndicator.Ensure();
        indicator.Show();

        // チャージ開始時に前回攻撃の残留を掃除する。ChargeLv は攻撃発動後 2 のまま残り、
        // Attack トリガーも「解放時に別遷移(ToMove 等)へ先取りされて未消費のまま持ち越し」に
        // なることがある。その状態で Charge ステートへ入ると `Attack && ChargeLv==2` が即成立し、
        // チャージ姿勢を飛ばして Attack_Charge2 が暴発する (特にチャージ専用のアリス巨大化で頻発)。
        player.Animator.ResetTrigger(PlayerAnimator.AttackParam);
        player.Animator.SetInteger(PlayerAnimator.ChargeLvParam, 0);
        player.Animator.SetTrigger(PlayerAnimator.ChargeParam);
    }

    /// <summary>チャージ中のカメラ演出・段階エフェクト (従来の Update 内容)。</summary>
    private void UpdateCharge() {
        // アリス巨大化は専用のカメラ挙動へ (連続FOVズームを使わず、溜め完了で1回だけ拡縮ズーム)。
        if (aliceMode == PlayerClothSkills.SizeMode.Giant) { UpdateGiantCharge(); return; }

        float progress = Mathf.Clamp01(chargeTime / player.Attack.MaxChargeTime);
        TPSCameraController.Instance?.SetChargeProgress(progress);
        TPSCameraController.Instance?.SetChargeFov(progress);

        if (indicator != null && player.Attack.TryPredictLanding(out Vector3 landPoint)) {
            indicator.SetPoint(landPoint, player.Attack.AttackRadius);
        }

        if (!midChargeReached && chargeTime >= player.Attack.ReleaseThresholds[1]) {
            player.Attack.PlayChargeMidEffect();
            TPSCameraController.Instance?.PunchZoom(intensity: 0.2f, duration: 0.25f);
            midChargeReached = true;
        }

        if (!fullChargeReached && chargeTime >= player.Attack.ReleaseThresholds[2]) {
            player.Attack.PlayChargeCompleteEffect();
            TPSCameraController.Instance?.PunchZoom(intensity: 0.35f, duration: 0.30f);
            TPSCameraController.Instance?.FovPunch(amount: 10f, duration: 0.35f);
            fullChargeReached = true;
        }
    }

    /// <summary>アリス巨大化のチャージ中カメラ。通常の連続FOVズーム(SetChargeFov)は使わない
    /// (巨大時はカメラが引けていて「通常時のまま」に見えるため)。溜め完了(th[2])で
    /// 1回だけ拡縮ズーム(パンチ)を出して「溜まった=撃てる」合図にする。</summary>
    private void UpdateGiantCharge() {
        if (indicator != null && player.Attack.TryPredictLanding(out Vector3 landPoint)) {
            indicator.SetPoint(landPoint, player.Attack.AttackRadius);
        }

        if (!fullChargeReached && chargeTime >= player.Attack.ReleaseThresholds[2]) {
            player.Attack.PlayChargeCompleteEffect();
            var cam = TPSCameraController.Instance;
            cam?.PunchZoom(player.Attack.GiantChargeZoomIn, 0.16f, 0.5f);   // グッと寄って戻る = 拡縮ズーム
            cam?.FovPunch(player.Attack.GiantChargeFovPunch, 0.16f, 0.5f);
            fullChargeReached = true;
        }
    }

    /// <summary>タップ発動: 3段コンボステートへ (1段目は入場と同時に発動)。クールタイム中は不発。</summary>
    private void FireComboAndExit() {
        if (!player.Attack.ComboReady) {   // コンボのクールタイム中は発動しない
            player.StateMachine.ChangeActionState(new StatePlayerNull());
            return;
        }
        player.StateMachine.ChangeActionState(new StateComboAttack(player));
    }

    /// <summary>チャージ発動: 最初のチャージ(th[1])が溜まっていなければ攻撃を出さない。
    /// 溜まっていれば 2段(部分) / 3段(満タン) を発動。1段目は廃止。</summary>
    private void FireChargeAndExit() {
        var th = player.Attack.ReleaseThresholds;

        // 中途半端な長押し = 最初のチャージ未達で離した → 攻撃は生成しない (溜め不足キャンセル)
        if (chargeTime < th[1]) {
            AttackLandingIndicator.TryHide();   // 予想サークルを片付ける
            // Charge モーションから Idle/Locomotion へ確実に戻す。これをしないと Charge ステートは
            // Attack_ChargeN への遷移しか出口が無く、キャンセル時に Charge ポーズで固まる
            // (「完了直前で離すと固まる」バグの原因)。ToMove で Charge→Locomotion を張って抜ける。
            player.Animator.ResetTrigger(PlayerAnimator.ChargeParam);
            player.Animator.SetTrigger(PlayerAnimator.ToMoveParam);
            player.StateMachine.ChangeActionState(new StatePlayerNull());
            return;
        }

        // 発動は「巨大スタンプ」1発に統一する (旧: 左右からのV字2本)。
        // 溜め段階 (部分 th[1] / 満タン th[2]) に関わらず単発の巨大スタンプを叩き落とす。
        // 3段階目は「溜め完了の合図」(UpdateCharge の PlayChargeCompleteEffect / カメラ演出) として残す。
        // モーションは通常チャージと共用 (ChargeLv=Release_Charge2 で Attack_Charge2 に揃える)。
        player.Attack.GiantStampBegin();

        // 先にチャージステートを抜ける (Exit が ChargeLv を 0 に戻すため、発動用の
        // Attack / ChargeLv は必ず Exit の後に設定する。逆順だと AnyState 遷移条件が
        // 成立せず攻撃モーションに入れない)。
        player.StateMachine.ChangeActionState(new StatePlayerNull());
        player.Animator.SetTrigger(PlayerAnimator.AttackParam);
        player.Animator.SetInteger(PlayerAnimator.ChargeLvParam, (int)PlayerAttack.AttackType.Release_Charge2);
    }

    /// <summary>アリス巨大化の発動: チャージが「溜め完了(th[2])」まで達していれば「巨大スタンプ」を1発。
    /// 溜め切っていない (早離し / タップ) なら攻撃を出さず Idle へ戻す (コンボは出さない)。</summary>
    private void FireGiantChargeAndExit() {
        var th = player.Attack.ReleaseThresholds;

        // 溜め完了(th[2])未満で離した (またはチャージ未開始のタップ) = キャンセルして Idle へ。
        // Charge ポーズで固まらないよう ToMove を張って抜ける (FireChargeAndExit と同様)。
        if (!chargeStarted || chargeTime < th[2]) {
            AttackLandingIndicator.TryHide();
            player.Animator.ResetTrigger(PlayerAnimator.ChargeParam);
            player.Animator.SetTrigger(PlayerAnimator.ToMoveParam);
            player.StateMachine.ChangeActionState(new StatePlayerNull());
            return;
        }

        // 溜まった → 巨大スタンプ (多段ヒット) を発動。モーションは通常チャージと共用。
        player.Attack.GiantStampBegin();
        player.StateMachine.ChangeActionState(new StatePlayerNull());
        player.Animator.SetTrigger(PlayerAnimator.AttackParam);
        player.Animator.SetInteger(PlayerAnimator.ChargeLvParam, (int)PlayerAttack.AttackType.Release_Charge2);
    }
}
