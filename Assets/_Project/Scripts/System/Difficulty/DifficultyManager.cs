using UnityEngine;

/// <summary>
/// 現在の難易度をゲーム全体で保持するグローバル状態。
/// シーンへの配置は不要（静的クラス）。PlayerPrefs に保存するので
/// シーン遷移・再起動をまたいで維持される。
///
/// 難易度別フルSO方式：各敵は EnemySettingsSet を持ち、DifficultyManager.Current で
/// Easy/Normal/Hard の EnemySettings を選ぶ。値のスケーリングは行わず、
/// あらかじめ難易度ごとにチューニングした SO をそのまま使う。
///
/// 使い方：
///   タイトル等で難易度選択時に  DifficultyManager.Current = Difficulty.Hard;
///   敵/ボスは生成時に自動で該当 SO を解決する。
/// </summary>
public static class DifficultyManager
{
    private const string PrefsKey = "PettanMaker.Difficulty";

    private static bool loaded;
    private static Difficulty current = Difficulty.Normal;

    /// <summary>難易度が変わったときに通知（UI 更新などに使う）。</summary>
    public static event System.Action<Difficulty> OnChanged;

    // Enter Play Mode (Domain Reload 無効) でも静的状態を必ず初期化する。
    // 無効化しないと前回プレイの OnChanged 購読者(破棄済み)が残り、次回 Current 設定時に
    // 破棄済みオブジェクトへ通知が飛んで MissingReferenceException になる。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = false;
        current = Difficulty.Normal;
        OnChanged = null;
    }

    public static Difficulty Current
    {
        get
        {
            if (!loaded)
            {
                current = (Difficulty)PlayerPrefs.GetInt(PrefsKey, (int)Difficulty.Normal);
                loaded = true;
            }
            return current;
        }
        set
        {
            if (loaded && current == value)
                return;

            current = value;
            loaded = true;
            PlayerPrefs.SetInt(PrefsKey, (int)value);
            PlayerPrefs.Save();
            OnChanged?.Invoke(value);
        }
    }
}
