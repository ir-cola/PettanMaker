/**===========================================================================

 File   : 3段コンボ攻撃ステート [StateComboAttack.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/14
 -----------------------------------------------------------------------------
 タップ攻撃の3段コンボ (Chain_Attack_0 → Chain_Attack_1 → Attack_1)。
   ・入場と同時に1段目を発動 (StateAttack がタップと判定して遷移してくる)
   ・各段の受付猶予 (ComboWindow) 内に攻撃ボタンを押すと次段へ繋ぐ
   ・**各段の最短間隔 (ComboMinInterval) を設け、連打しても超高速で進まない**。
     間隔前に押した入力はバッファし、間隔経過時に発動する (レスポンス確保)。
   ・押さなければその段で終了 → StatePlayerNull へ戻る (通常状態へ)
 1段ごとに: 狙う方向へ即アイム → コンボモーション → 斜め斬撃スポーン
            + 前方へ一歩ステップ (PlayerAttack.DoComboHit)。
 ロックオン: 開始時に近く (ComboLockRadius) の最近接エネミーへ自動ロックし、
 各段はその敵の方を向いて撃つ (未ロック時はカメラ正面)。ロックは攻撃入力が
 ComboLockLinger 秒あくと自動で消える (PlayerLockOn 側のタイマー) ので、
 コンボ終了後もその猶予内に再タップすればロックが継続する。
 移動ロックは StateGrounded 側がこのステート中に掛ける (前進はランジが担当)。
 ===========================================================================**/

using UnityEngine;

public class StateComboAttack : IPlayerState {

    private readonly IPlayerContext player;
    private int   comboIndex;      // 0,1,2
    private float windowTimer;     // 次段の受付残り秒 (ComboWindow)
    private float sinceHit;        // 直近ヒットからの経過秒 (最短間隔判定)
    private bool  buffered;        // 最短間隔前に押された入力をバッファ
    private PlayerLockOn lockOn;   // 近接敵への自動ロック (解除は保持時間切れで自動)

    private const int MaxIndex = 2;   // Chain_0 / Chain_1 / Attack_1 の3段 (0..2)

    public StateComboAttack(IPlayerContext player) {
        this.player = player;
    }

    public void Enter() {
        // 近くの最近接エネミーへ自動ロックオン。攻撃入力が ComboLockLinger 秒あくと
        // 自動解除されるため、ここでは SetActive(false) を呼ぶ側は無い (自己完結)。
        if (player.Attack.ComboLockRadius > 0f) {
            lockOn = PlayerLockOn.Ensure(player.Attack.gameObject);
            lockOn.SetActive(true, player.Attack.ComboLockLinger, player.Attack.ComboLockRadius);
        }
        comboIndex = 0;
        PlayHit(0);
    }

    public void Update() {
        windowTimer -= Time.deltaTime;
        sinceHit    += Time.deltaTime;

        // 入力はいつでもバッファ (最短間隔前でも受け付けて溜める → 押し損ねを防ぎレスポンス確保)
        if (player.Input.IsAttackPress) buffered = true;

        // 最短間隔を過ぎ、バッファがあれば次段へ。連打してもこの間隔より速くは進まない。
        if (comboIndex < MaxIndex && buffered && sinceHit >= player.Attack.ComboMinInterval) {
            comboIndex++;
            PlayHit(comboIndex);
            return;
        }
        if (comboIndex >= MaxIndex) buffered = false;   // 最終段はこれ以上進めない=バッファ破棄

        // 猶予切れ (かつ保留入力なし) = コンボ終了。通常状態へ戻す。
        // バッファ入力が残っていれば最短間隔まで待って発動させてから終了する。
        if (windowTimer <= 0f && !buffered) {
            player.StateMachine.ChangeActionState(new StatePlayerNull());
        }
    }

    public void Exit() {
        player.Attack.NotifyComboEnded();   // クールタイム開始 (次のコンボはこの時点から comboCooldown 後)
    }

    private void PlayHit(int index) {
        lockOn?.RefreshHold();                                               // 攻撃入力あり = 解除タイマー巻き戻し
        // 撃つ方向: ロックオン中はその敵へ、未ロックはカメラ正面へ即アイム
        if (lockOn != null && lockOn.HasTarget) player.Mover.FacePointInstant(lockOn.Target.position);
        else                                    player.Mover.FaceCameraInstant();
        player.Animator.SetInteger(PlayerAnimator.ComboIndexParam, index);
        player.Animator.SetTrigger(PlayerAnimator.ComboParam);
        player.Attack.DoComboHit(index);                                     // 斬撃スポーン + 前進ステップ + ダメージ
        windowTimer = player.Attack.ComboWindow;
        sinceHit    = 0f;      // このヒットから最短間隔を計り直す
        buffered    = false;   // 消費
    }
}
