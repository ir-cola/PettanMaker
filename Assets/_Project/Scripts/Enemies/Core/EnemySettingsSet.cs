using UnityEngine;

/// <summary>
/// 難易度別の EnemySettings をまとめる SO（難易度別フルSO方式）。
/// 各敵はこの Set を1つ持ち、実行時に DifficultyManager.Current で
/// Easy/Normal/Hard のどの EnemySettings を使うかを解決する。
///
/// HP・与ダメージ(AttackDamage)・攻撃頻度(AttackInterval/各サブSOの Cooldown 等)は
/// すべて EnemySettings 内にあるため、難易度ごとに値を変えた SO を差し替えるだけで
/// これらがまとめてスケールされる。
///
/// 各スロットには対応する敵型の派生 SO（例：Enemy01Settings）を割り当てること。
/// </summary>
[CreateAssetMenu(fileName = "EnemySettingsSet", menuName = "Enemies/Settings Set (Difficulty)")]
public class EnemySettingsSet : ScriptableObject
{
    [SerializeField] private EnemySettings easy;
    [SerializeField] private EnemySettings normal;
    [SerializeField] private EnemySettings hard;

    /// <summary>難易度に対応する EnemySettings を返す（未設定なら normal にフォールバック）。</summary>
    public EnemySettings Resolve(Difficulty difficulty)
    {
        EnemySettings selected = difficulty switch
        {
            Difficulty.Easy => easy,
            Difficulty.Hard => hard,
            _ => normal,
        };
        return selected != null ? selected : normal;
    }
}
