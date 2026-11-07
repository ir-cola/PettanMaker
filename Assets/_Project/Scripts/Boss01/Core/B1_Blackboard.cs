/// <summary>
/// IBlackboard の実体。書き込みはメソッド経由のみ許可する。
/// KnowledgeSource 以外のクラスは IBlackboard 型で受け取ること。
/// </summary>
public class B1_Blackboard : IBlackboard
{
    // ---- IBlackboard 実装（読み取り専用プロパティ） ----

    public float PlayerDistance   { get; private set; } // プレイヤーとの距離（m）
    public float PlayerAngle      { get; private set; } // ボス正面からプレイヤーへの水平角度（度）
    public bool  IsPlayerAirborne { get; private set; } // プレイヤーが空中にいるか
    public float HpRatio          { get; private set; } // 現在HPの最大HP比（0〜1）
    public int   CurrentPhase     { get; private set; } // 現在のフェーズ（1 or 2）
    public bool            IsEnraged        { get; private set; } // 激昂状態か（Phase2 移行時に true）
    public ActionGroupType LastActionGroup  { get; private set; } // 直前の行動グループ
    public int             SameGroupCount   { get; private set; } // 同じグループを連続で選んだ回数

    // 直近に選ばれた行動グループ（0 番目 = 直前）。履歴補正の照会に使う。
    private ActionGroupType[] recentGroups = System.Array.Empty<ActionGroupType>();

    public int StepsSinceLastUse(ActionGroupType type)
    {
        for (int i = 0; i < recentGroups.Length; i++)
            if (recentGroups[i] == type) return i + 1;
        return int.MaxValue;
    }

    // ---- 書き込みメソッド（KnowledgeSource のみ呼ぶ） ----

    // プレイヤー知覚情報を更新する
    public void SetPerception(float distance, float angle, bool isAirborne)
    {
        PlayerDistance   = distance;
        PlayerAngle      = angle;
        IsPlayerAirborne = isAirborne;
    }

    // HP比率を更新する
    public void SetHpRatio(float ratio)
    {
        HpRatio = ratio;
    }

    // フェーズと激昂状態を更新する
    public void SetPhase(int phase, bool isEnraged)
    {
        CurrentPhase = phase;
        IsEnraged    = isEnraged;
    }

    // 行動履歴を更新する（B1_HistoryKS のみ呼ぶ）
    public void SetActionHistory(ActionGroupType last, int sameCount, ActionGroupType[] recent)
    {
        LastActionGroup = last;
        SameGroupCount  = sameCount;
        recentGroups    = recent ?? System.Array.Empty<ActionGroupType>();
    }
}
