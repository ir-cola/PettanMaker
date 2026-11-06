public enum ActionGroupType
{
    None,
    Idle,            // その場で待機
    Walk,            // 歩き接近
    Run,             // 走り接近
    BackStep,        // 後退（攻撃直後に距離を取る）
    IdleApproach,    // フォールバック用（歩き接近）
    Scratch,         // ひっかき
    RushCharge,      // 突進タメ
    TailAttack,      // テールアタック（ジャンプ範囲攻撃）
    TailRain,        // テールレイン（見下ろし固定カメラ＋連続叩きつけの回避演出）
    Teleport,        // 瞬間移動
    TeleportScratch, // 瞬間移動 + ひっかき
    InvisibleCharge, // 透明化 + 突進
    Evade,           // 逃げ（透明化）
    Roar,            // 咆哮（Phase2 移行演出）
}

/// <summary>
/// B1_AIController が管理する行動グループのエントリ。
/// enableConditions はすべて AND 評価。
/// </summary>
public struct ActionGroupEntry
{
    public ActionGroupType groupType;
    public float           baseWeight;
    public Condition[]     enableConditions;
    // x=距離(m), y=重み倍率。null なら常に 1.0 倍
    public UnityEngine.AnimationCurve distanceCurve;
    // 選択後に再び候補になるまでの最低秒数（0 = 制限なし）
    public float cooldown;
    // true のとき、直近に選ばれた行動ほど重みを下げる「履歴補正」の対象にする。
    // 攻撃の連続選択を防ぐため、攻撃系グループにのみ立てる（移動・待機は対象外）。
    public bool applyRepeatPenalty;
}
