/**===========================================================================

 File   : プレイヤーステート文脈 契約 [IPlayerContext.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/29
 -----------------------------------------------------------------------------
 各ステート / ティックが依存する「プレイヤーの構成要素 + 設定値」への窓口。
 states を具象 PlayerLifetimeScope から切り離し、差し替え / テストを容易にする。
 ===========================================================================**/

public interface IPlayerContext : IPlayerConfig {
    PlayerInputHandler Input        { get; }
    PlayerStateMachine StateMachine { get; }
    PlayerMover        Mover        { get; }
    PlayerAnimator     Animator     { get; }
    PlayerHealth       Health       { get; }
    PlayerAttack       Attack       { get; }
    PlayerMotionSpeeds MotionSpeeds { get; }
    PlayerClothSkills  ClothSkills  { get; }   // アリス等のサイズ別攻撃/速度分岐に使う (無い場合 null)
    Player_SoundManager Sound       { get; }

    /// <summary>ScaryWalk 中に下半身レイヤーだけ Idle に上書きするフラグ (B1_CameraDirector のマップ演出が制御)。</summary>
    bool ScaryWalkLowerIdle { get; }

    /// <summary>ScaryWalk の下半身 Idle 上書きの ON/OFF (演出側の制御と、StateGrounded の解除漏れフォールバックが使う)。</summary>
    void SetScaryWalkLowerIdle(bool on);
}
