# HitStop システム

## 構成

```
IHitStopable              ← Freeze / StopFreeze インターフェース
HitStopBase               ← コルーチン管理・IsFrozen（抽象基底）
 ├─ RigidbodyHitStop      ← Rigidbody 停止（単体でも使用可）
 │    └─ CharacterHitStop ← + Animator 停止
 ├─ WeaponHitStop         ← Animator のみ
 └─ EffectHitStop         ← ParticleSystem 停止
HitStopRoot               ← 配下への一括伝播
```

---

## 基本的な使い方

### シンプルなキャラクター停止

`CharacterHitStop` をアタッチするだけ。Animator と Rigidbody を自動検出して停止する。

```
Enemy
 └─ CharacterHitStop
```

```csharp
GetComponent<CharacterHitStop>().Freeze(0.08f);
```

### CombatSystem 経由（推奨）

`CombatSystem.Damage()` を呼ぶと `DamageContext` の値で自動的に停止する。  
エンティティに `IHitStopable` コンポーネントがある場合のみ適用し、なければスキップする。

```csharp
// DamageContext を直接指定
var ctx = new DamageContext { targetHitstop = 0.08f, sourceHitstop = 0.04f };
CombatSystem.Instance.Damage(target, damage, ctx, this);

// AttackData (ScriptableObject) を使う場合
CombatSystem.Instance.Damage(target, attackData, this);
```

---

## 複数対象をまとめて停止する（HitStopRoot）

エフェクト・武器・本体をまとめて同時停止したい場合は `HitStopRoot` を使う。

### Inspector 設定

```
Enemy
 ├─ HitStopRoot          ← ここに各コンポーネントをアサイン
 ├─ CharacterHitStop
 ├─ WeaponRoot
 │    └─ WeaponHitStop
 └─ AuraEffect
      └─ EffectHitStop
```

`HitStopRoot` の `Targets` リストに `CharacterHitStop`・`WeaponHitStop`・`EffectHitStop` をアサインする。  
`IHitStopable` を実装していないコンポーネントをアサインすると Console に警告が出る。

```csharp
GetComponent<HitStopRoot>().Freeze(0.1f);
// → 登録されたすべての IHitStopable に伝播する
```

### Projectile の動的登録

Projectile が敵に刺さったタイミングで登録し、敵と一緒に停止させる。

```csharp
// 命中時
enemyHitStopRoot.Register(GetComponent<RigidbodyHitStop>());

// 離脱・破棄時
enemyHitStopRoot.Unregister(GetComponent<RigidbodyHitStop>());
```

---

## コンポーネントの選び方

| コンポーネント      | 用途                                        |
|--------------------|---------------------------------------------|
| `CharacterHitStop` | Animator + Rigidbody を持つキャラクター全般 |
| `RigidbodyHitStop` | Rigidbody のみ（プロジェクタイルなど）      |
| `WeaponHitStop`    | Animator のみ（武器オブジェクト）           |
| `EffectHitStop`    | ParticleSystem を持つエフェクト             |

---

## カスタム実装

`HitStopBase` を継承して `OnFreeze` / `OnRestore` を実装するだけで追加できる。

```csharp
public class MyHitStop : HitStopBase {
    protected override void OnFreeze()  { /* 停止処理 */ }
    protected override void OnRestore() { /* 復帰処理 */ }
}
```

`IsFrozen` は `HitStopBase` が管理しているため実装不要。

---

## AttackData（ScriptableObject）

`Assets > Create > Combat > AttackData` で作成。  
`effects` フィールドに `DamageContext` がまとめて入っている。

| フィールド                  | 説明                  |
|----------------------------|-----------------------|
| `damage`                   | ダメージ量            |
| `effects.sourceHitstop`    | 攻撃側の停止時間（秒）|
| `effects.targetHitstop`    | 被弾側の停止時間（秒）|
| `effects.shakeDuration`    | カメラシェイク時間    |
| `effects.shakeIntensity`   | カメラシェイク強度    |

典型値の目安：`targetHitstop = 0.08`、`sourceHitstop = 0.04`

---

## 注意

- `Freeze()` は**残り時間が長い方を優先**する。短い呼び出しで凍結が中断されない。
- GameObject が `OnDisable` されると自動的に `StopFreeze()` が呼ばれる。
- Freeze 前から Kinematic だった Rigidbody はそのまま維持される（意図しない状態変化を防ぐ）。
- `HitStopRoot` は `Awake` 時に `Targets` リストを読み込む。動的追加は `Register()` を使う。
