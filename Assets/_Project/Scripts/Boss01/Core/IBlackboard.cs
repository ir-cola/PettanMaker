/// <summary>
/// ブラックボードの読み取り専用インターフェース。
/// KnowledgeSource 以外のクラスはこの型で受け取る。
/// 書き込みは B1_Blackboard のメソッド経由のみ許可する。
/// </summary>
public interface IBlackboard
{
    float PlayerDistance   { get; } // プレイヤーとの距離（m）
    float PlayerAngle      { get; } // ボス正面からプレイヤーへの水平角度（度）
    bool  IsPlayerAirborne { get; } // プレイヤーが空中にいるか
    float HpRatio          { get; } // 現在HPの最大HP比（0〜1）
    int   CurrentPhase     { get; } // 現在のフェーズ（1 or 2）
    bool            IsEnraged        { get; } // 激昂状態か（Phase2 移行時に true）
    ActionGroupType LastActionGroup  { get; } // 直前の行動グループ
    int             SameGroupCount   { get; } // 同じグループを連続で選んだ回数

    // type が直近の履歴で何ステップ前に選ばれたか（1 = 直前、2 = 2つ前 …）。
    // 直近履歴に無ければ int.MaxValue。行動の連続選択を抑制する重み補正に使う。
    int StepsSinceLastUse(ActionGroupType type);
}
