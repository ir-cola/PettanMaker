# CombatSystem

ダメージ処理・ヒットストップ・カメラシェイクを一括管理するシングルトン。  
シーンに1つ配置するだけで全攻撃処理の窓口になる。

---

## セットアップ

以下の3コンポーネントを任意の GameObject にアタッチする（同じ GameObject でも別でも可）。

| コンポーネント             | 役割                                   |
|--------------------------|----------------------------------------|
| `CombatSystem`           | ダメージ処理                           |
| `DebugOverlay`           | デバッグ表示の管理・F1 トグル          |
| `CombatSystemDebugOverlay` | Combat / Damage ログの描画           |

> `CombatSystem` は他のコンポーネントを自動生成しない。シーンに明示的に配置すること。

### DebugOverlay の Inspector

| フィールド    | 説明                              |
|-------------|-----------------------------------|
| `Visible`   | 起動時の表示状態（デフォルト: ON） |
| `Toggle Key`| 表示切り替えキー（デフォルト: F1） |

---

## Damage() の使い方

### AttackData（推奨）

`AttackData` ScriptableObject にダメージ・エフェクト設定をまとめて定義できる。

```
Assets > Create > Combat > AttackData
```

```csharp
[SerializeField] private AttackData attackData;

CombatSystem.Instance.Damage(target, attackData, this);
```

### DamageContext（コードで直接指定）

```csharp
var ctx = new DamageContext {
    targetHitstop  = 0.08f,
    sourceHitstop  = 0.04f,
    shakeDuration  = 0.15f,
    shakeIntensity = 0.10f,
};

CombatSystem.Instance.Damage(target, 20, ctx, this);
```

### 演出なしの素のダメージ

```csharp
CombatSystem.Instance.Damage(target, 10);
```

---

## API

```csharp
// AttackData を使う（effects フィールドがエフェクト設定を持つ）
void Damage(IHealth target, AttackData data, Component source = null)

// ダメージ量・演出を直指定（ctx は省略可、source も省略可）
void Damage(IHealth target, int amount, DamageContext ctx = default, Component source = null)
```

- `target` が `null` または `IsDead` のときは何もしない
- `source` は `Component` 型のため `this` を直接渡せる
- HitStop はエンティティ側に `IHitStopable` コンポーネントがある場合のみ適用。なければスキップ

---

## AttackData の構造

```csharp
public class AttackData : ScriptableObject {
    public int damage;
    public DamageContext effects;  // targetHitstop / sourceHitstop / shakeDuration / shakeIntensity
}
```

---

## onDamageDealt イベント

ダメージが成立するたびに `onDamageDealt` が発火する。  
スコア・実績・ダメージ表示など任意の処理をフックできる。

```csharp
// シグネチャ: (GameObject source, IHealth target, int amount)
CombatSystem.Instance.onDamageDealt.AddListener((src, target, amount) => {
    Debug.Log($"{src?.name} → {(target as MonoBehaviour)?.name} : {amount}");
});
```

Inspector の `On Damage Dealt` から UnityEvent として設定することもできる。

---

## HitStop との連携

`source` を渡すと攻撃側にもヒットストップが掛かる。  
`GetComponent<IHitStopable>()` でコンポーネントを取得し、見つかった場合のみ適用する。

| コンポーネント      | 用途                           |
|--------------------|-------------------------------|
| `HitStopRoot`      | 子オブジェクトへ一括伝播       |
| `CharacterHitStop` | Animator・Rigidbody を停止     |
| `WeaponHitStop`    | 武器の Animator を停止         |
| `EffectHitStop`    | ParticleSystem を停止          |
| `ProjectileHitStop`| Rigidbody を停止（速度を保存） |

詳細は [HitStop.md](HitStop.md) を参照。

---

## デバッグオーバーレイ

`DebugOverlay` が有効なとき、画面左上にリアルタイム情報を表示する。

| 表示項目      | 内容                              |
|--------------|-----------------------------------|
| Player       | CurrentHP / MaxHP                 |
| Enemies      | 全 EnemyHealth の HP・死亡状態    |
| Boss02       | B2_Health の HP・死亡状態         |
| Combat Log   | 誰が誰に何ダメージを与えたか      |
| Damage Log   | 被弾側の HP 推移                  |
