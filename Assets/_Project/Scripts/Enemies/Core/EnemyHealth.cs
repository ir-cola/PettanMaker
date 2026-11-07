/**===========================================================================

 File   : 敵HPクラス [EnemyHealth.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------

 ===========================================================================**/

using UnityEngine;
using UnityEngine.Events;

/**===========================================================================
  敵HPクラス
===========================================================================**/
public class EnemyHealth : MonoBehaviour, IEnemy {

    private int maxHP;

    // 内部変数
    private int currentHP;
    private bool isDead = false;

    // HP変化イベント
    public UnityEvent<int> onHPChanged;
    public UnityEvent onDied;

    /**=======================================================================
      EnemyBase から難易度解決後の最大HPで初期化する。
    =======================================================================**/
    public void Initialize(int newMaxHP) {
        maxHP = Mathf.Max(1, newMaxHP);
        currentHP = maxHP;
        isDead = false;
    }

    void Start() {
        DebugOverlay.Register(GetInstanceID(), gameObject.name, () => $"HP  {currentHP} / {maxHP}{(isDead ? "  [DEAD]" : "")}");
    }

    private void OnDestroy() => DebugOverlay.Unregister(GetInstanceID());

    /**=======================================================================
      ダメージを受ける
    =======================================================================**/
    public void TakeDamage(int amount) {
        if (isDead) return;

        currentHP = Mathf.Max(0, currentHP - amount);
        onHPChanged?.Invoke(currentHP);

        Debug.Log($"[{gameObject.name}] HP: {currentHP}/{maxHP}");

        if (currentHP <= 0) {
            Die();
        }
    }

    /**=======================================================================
      死亡処理
    =======================================================================**/
    private void Die() {
        if (isDead) return;
        isDead = true;

        onDied?.Invoke();
        Debug.Log($"[{gameObject.name}] 死亡！");
    }

    public int CurrentHP => currentHP;
    public int MaxHP => maxHP;
    public bool IsDead => isDead;
}
