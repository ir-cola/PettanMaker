/**===========================================================================

 File   : プレイヤー設定値 契約 [IPlayerConfig.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/29
 -----------------------------------------------------------------------------
 移動 / 回避 / ジャンプ等のチューニング値への読み取り契約。
 具象 PlayerLifetimeScope への直接依存を避けるための窓口。
 ===========================================================================**/

public interface IPlayerConfig {
    float Speed              { get; }
    float Gravity            { get; }
    float RotationSpeed      { get; }
    float JumpHeight         { get; }
    float DodgeDistance      { get; }   // 回避の移動距離 (m)。速度を変えても距離は不変
    float DodgeMultiplier    { get; }   // 回避の速度倍率 (Speed × この値)。所要時間 = 距離 ÷ 速度
    float DodgeCooldown      { get; }   // ダッシュ(回避)のクールタイム (秒)。回避後この時間は再回避不可
    float GroundCheckDistance{ get; }
    float BufferTime         { get; }
    float SpeedMultiplier    { get; }

    // 移動フィール (PlayerMover の定数を Inspector 調整可能にしたもの)
    float MoveDrag           { get; }   // 滑り出しの鋭さ (初期加速 = Speed × Drag)
    float WalkFloor          { get; }   // 最小入力時の速度倍率 (歩き)
    float InertiaDecay       { get; }   // 入力を離した後の慣性スライド減衰
    float QuickTurnSpeed     { get; }   // 向き直り速度 (>90°) 度/秒
    float MediumTurnSpeed    { get; }   // 向き直り速度 (45〜90°) 度/秒
    float RotEaseSpeed       { get; }   // 向き直り指数イーズ (<45°)

    // 攻撃フィール (StateGrounded の定数を Inspector 調整可能にしたもの)
    float AttackRecoveryTime { get; }   // 攻撃発動後の移動ロック追加時間 (秒)
    float ChargeMoveSpeedMul { get; }   // チャージ中の移動速度倍率

    void SetSpeedMultiplier(float m);
}
