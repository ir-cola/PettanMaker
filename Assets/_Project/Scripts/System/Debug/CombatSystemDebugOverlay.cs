/**===========================================================================

 File   : 戦闘デバッグオーバーレイ [CombatSystemDebugOverlay.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 CombatSystem のダメージイベントを購読し、直近の戦闘/ダメージ履歴を
 DebugOverlay に登録して画面表示するデバッグ用コンポーネント。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

public class CombatSystemDebugOverlay : MonoBehaviour {
    [SerializeField] private int maxLog = 10;

    private readonly List<string> combatLog = new();
    private readonly List<string> damageLog = new();

    // フィールド初期化でユニークキーを確保（Awake 不要）
    private static int nextKey = int.MinValue;
    private readonly int combatLogKey = nextKey++;
    private readonly int damageLogKey = nextKey++;

    private void Start() {
        // Start は全 Awake 完了後に呼ばれるため Instance が確実に存在する
        DebugOverlay.Register(combatLogKey, "Combat Log",
            () => combatLog.Count > 0 ? string.Join("\n", combatLog) : "(no events)");
        DebugOverlay.Register(damageLogKey, "Damage Log",
            () => damageLog.Count > 0 ? string.Join("\n", damageLog) : "(no events)");

        if (CombatSystem.Instance != null)
            CombatSystem.Instance.onDamageDealt.AddListener(OnHit);
    }

    private void OnDestroy() {
        DebugOverlay.Unregister(combatLogKey);
        DebugOverlay.Unregister(damageLogKey);

        if (CombatSystem.Instance != null)
            CombatSystem.Instance.onDamageDealt.RemoveListener(OnHit);
    }

    private void OnHit(GameObject source, IHealth target, int amount) {
        string from = source != null ? source.name : "?";
        string to   = target is MonoBehaviour mb ? mb.gameObject.name : "?";

        combatLog.Add($"{from}  →  {to}  -{amount}");
        damageLog.Add($"{to}  -{amount}  ({target.CurrentHP}/{target.MaxHP})");

        if (combatLog.Count > maxLog) combatLog.RemoveAt(0);
        if (damageLog.Count > maxLog) damageLog.RemoveAt(0);
    }
}
