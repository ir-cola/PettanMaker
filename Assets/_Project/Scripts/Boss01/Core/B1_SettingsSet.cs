using UnityEngine;

/// <summary>
/// 難易度別の B1_Settings をまとめる SO（難易度別フルSO方式）。
/// Boss01 はこの Set を持ち、実行時に DifficultyManager.Current で
/// Easy/Normal/Hard のどの B1_Settings を使うかを解決する。
/// </summary>
[CreateAssetMenu(fileName = "B1_SettingsSet", menuName = "Boss01/Settings Set (Difficulty)")]
public class B1_SettingsSet : ScriptableObject
{
    [SerializeField] private B1_Settings easy;
    [SerializeField] private B1_Settings normal;
    [SerializeField] private B1_Settings hard;

    /// <summary>難易度に対応する B1_Settings を返す（未設定なら normal にフォールバック）。</summary>
    public B1_Settings Resolve(Difficulty difficulty)
    {
        B1_Settings selected = difficulty switch
        {
            Difficulty.Easy => easy,
            Difficulty.Hard => hard,
            _ => normal,
        };
        return selected != null ? selected : normal;
    }
}
