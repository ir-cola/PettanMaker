# IHealth / ヘルスシステム

HP の参照・ダメージ・死亡判定を統一するインターフェース群。

---

## 構成

```
IHealth          ← 共通インターフェース
 ├─ PlayerHealth ← プレイヤー（回復・最大HP増加付き）
 ├─ EnemyHealth  ← 敵（死亡イベント付き）
 └─ B2_Health    ← Boss02（float ベース）
```

---

## IHealth インターフェース

```csharp
public interface IHealth {
    int CurrentHP { get; }
    int MaxHP     { get; }
    bool IsDead   { get; }
    void TakeDamage(int amount);
}
```

---

## 各実装の特徴

### PlayerHealth

| メンバー           | 説明                         |
|-------------------|------------------------------|
| `CurrentHP`       | 現在 HP                      |
| `MaxHP`           | 最大 HP（デフォルト 5）       |
| `IsDead`          | `CurrentHP <= 0`             |
| `TakeDamage(int)` | HP 減少・`onHPChanged` 発火  |
| `Heal(int)`       | HP 回復（MaxHP を超えない）   |
| `IncreaseMaxHP(int)` | 最大 HP 拡張               |
| `onHPChanged`     | HP 変化時イベント `(int hp)`  |

### EnemyHealth

| メンバー           | 説明                              |
|-------------------|-----------------------------------|
| `CurrentHP`       | 現在 HP                           |
| `MaxHP`           | 最大 HP（デフォルト 30）           |
| `IsDead`          | 死亡フラグ                        |
| `TakeDamage(int)` | HP 減少・死亡時に `Die()` を呼ぶ  |
| `onHPChanged`     | HP 変化時イベント `(int hp)`       |
| `onDied`          | 死亡時イベント                    |

### B2_Health

| メンバー              | 説明                                   |
|----------------------|----------------------------------------|
| `CurrentHP` / `MaxHP`| float → int 変換で `IHealth` を満たす  |
| `IsDead`             | `currentHealth <= 0`                   |
| `TakeDamage(int)`    | `TakeDamage(float)` に委譲             |
| `TakeDamage(float)`  | ベースブレイク判定・HP バー更新あり    |

---

## 使い方

### ダメージを与える（CombatSystem 推奨）

```csharp
// IHealth を取得できれば種類を問わず同じ呼び出し
IHealth target = col.GetComponentInParent<IHealth>();
CombatSystem.Instance.Damage(target, 10, gameObject, ctx);
```

### HP を参照する

```csharp
IHealth health = GetComponent<IHealth>();
float ratio = (float)health.CurrentHP / health.MaxHP;
if (health.IsDead) { /* 死亡処理 */ }
```

### 死亡・HP 変化イベントを購読する

```csharp
// EnemyHealth
enemyHealth.onDied.AddListener(() => Debug.Log("死亡"));
enemyHealth.onHPChanged.AddListener(hp => hpBar.SetValue(hp));

// PlayerHealth
playerHealth.onHPChanged.AddListener(hp => hpUI.UpdateHP(hp));
```

### プレイヤーを回復・強化する

```csharp
PlayerHealth player = GetComponent<PlayerHealth>();
player.Heal(2);
player.IncreaseMaxHP(1);
```

---

## 注意

- ダメージは必ず `CombatSystem.Damage()` 経由で呼ぶ。直接 `TakeDamage()` を呼ぶとヒットストップ・カメラシェイクが発生しない。
- `EnemyHealth` は死亡後に `TakeDamage()` を呼んでも無視する（`isDead` ガード済み）。
- `B2_Health` は内部で `float` を使うため、`CurrentHP` / `MaxHP` は切り捨て値になる。
