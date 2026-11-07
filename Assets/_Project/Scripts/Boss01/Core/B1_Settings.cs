using UnityEngine;

/// <summary>
/// Boss01 の全パラメータをまとめた ScriptableObject。
/// Inspector から値を調整して挙動をチューニングする。
///
/// 【距離の関係図】
///
///   0m ──── Scratch.AttackRange ──── Walk.ActivateRange ──── Charge.MinRange
///                  ↑                        ↑                      ↑
///               爪攻撃が届く             Walkが起動する         Chargeが起動する
///
///   Charge.MinRange ─── Charge中間(自動計算) ─── Charge.MaxRange ─── Run.ActivateRange
///                              ↑                        ↑                   ↑
///                         突進の選ばれやすさピーク     Chargeの限界距離    Runが起動する
///
/// 【激昂（Enraged）について】
///   Phase2 移行時に IsEnraged = true になる。
///   移動速度が EnragedSpeedMult 倍になり、全体的に速く激しくなる。
/// </summary>
[CreateAssetMenu(fileName = "B1_Settings", menuName = "Boss01/Settings")]
public class B1_Settings : ScriptableObject
{
    [Header("Health")]
    [Tooltip("ボスの最大HP。難易度別SO（B1_SettingsSet）で耐久を増減できる。")]
    [Min(1)]
    public int MaxHP = 250;

    [Header("被弾ヒットストップ")]
    [Tooltip("被弾時にボスの Animator を止める時間（秒・実時間）。0で無効。")]
    public float HitStopDuration = 0.08f;
    [Tooltip("停止中の再生速度。0で完全停止。")]
    [Range(0f, 1f)] public float HitStopFrozenSpeed = 0f;

    [Header("クリティカルヒット — 被弾時に低確率で発生")]
    [Tooltip("プレイヤーの攻撃がクリティカルになる確率・倍率と、発生時の演出（画面フラッシュ/マテリアル点滅/強ヒットストップ）。")]
    public CriticalHitConfig Critical = new();

    [Header("移動 — 共通")]
    [Tooltip("Chase（追跡）・Strafe（横移動）モードの基準速度（m/s）。\nWalk.Speed より大きい値にすること。")]
    public float ChaseSpeed = 5f;

    [Tooltip("激昂（Phase2）中の速度倍率。1.5 なら全移動速度が1.5倍になる。\n1.0 で通常と同じ、2.0 で2倍速。")]
    public float EnragedSpeedMult = 1.5f;

    [Tooltip("激昂（Phase2）中の攻撃速度倍率。攻撃モーションの再生速度と、タメ・叩きつけ等の各種待ち時間に掛かる。\n1.0 で通常と同じ、1.2 で2割速い。上げすぎると回避猶予が消えるので 1.1〜1.3 が目安。")]
    public float EnragedAttackSpeedMult = 1.2f;

    [Tooltip("プレイヤーへの旋回速度（度/秒）。小さいほどゆっくり向き直る。\n目安：150〜300。")]
    public float TurnSpeed = 200f;

    [Tooltip("この角度（度）を超えると弧を描いてターンする。\n小さくすると常に大回りになる。大きくすると直進が多くなる。目安：30〜60。")]
    public float TurnArcThreshold = 45f;

    [Tooltip("ターン時の弧の最大横オフセット（m）。大きいほど大回りになる。\n0 にすると弧なしで直進ターンになる。")]
    public float TurnArcStrength = 3f;

    [Tooltip("NavMeshAgent の加速度。大きいほど素早く最高速に達する。\n小さいと加速が緩やか。目安：6〜12。")]
    public float NavAcceleration = 8f;

    [Tooltip("NavMesh の端からボス中心まで確保する最小距離（m）。\nボス本体が端からはみ出す場合に大きくする。0 で補正を無効化。")]
    [Min(0f)] public float NavMeshEdgeMargin = 1f;

    [Tooltip("移動アニメーション速度パラメータの補間係数。\n大きいほどアニメが素早く切り替わる。小さいと滑らかに変化する。目安：8〜15。")]
    public float AnimSpeedSmoothing = 10f;

    [Tooltip("この角度（度）以上プレイヤーへ向きがずれると Turn_L / Turn_R の振り向きアニメを再生する。\nクリップが90度なので目安：60〜90。小さすぎると頻繁に発火する。")]
    public float TurnAnimAngle = 70f;

    [Tooltip("ターンアニメを再生してから次に再生できるまでの最短間隔（秒）。\nクリップの長さ程度にすると多重発火しない。目安：0.4〜0.8。")]
    public float TurnAnimCooldown = 0.6f;

    [Header("攻撃前 — 猫の狙い構え")]
    public AimCrouchActionConfig AimCrouch = new();

    [Header("BGM — 攻撃同期アクセント")]
    public BgmActionAccentConfig BgmAccent = new();

    [Header("Idle — その場で待機")]
    public IdleActionConfig Idle = new();

    [Header("Walk — 歩いて接近")]
    public WalkActionConfig Walk = new();

    [Header("Run — 走って接近")]
    public RunActionConfig Run = new();

    [Header("BackStep — 攻撃直後に後退して距離を取る")]
    public BackStepActionConfig BackStep = new();

    [Header("Scratch — ひっかき攻撃")]
    public ScratchActionConfig Scratch = new();

    [Header("RushCharge / Rush — タメ + 突進")]
    [Tooltip("突進前のタメ演出フェーズ。タメが終わると Rush へ移行する。")]
    public ChargeActionConfig RushCharge = new();

    [Tooltip("タメ後の実際の突進フェーズ。Speed と OvershootDistance で突進の鋭さを調整する。")]
    public RushActionConfig Rush = new();

    [Header("TailAttack — ジャンプ範囲攻撃")]
    public TailAttackActionConfig TailAttack = new();

    [Header("TailRain — 見下ろし固定カメラ＋連続叩きつけ回避")]
    public TailRainActionConfig TailRain = new();

    [Header("Down — 体力半分でダウン（Phase2移行前の攻撃チャンス）")]
    public DownStateConfig Down = new();

    [Header("Debug — マスター")]
    [Tooltip("OFF にするとデバッグ表示をすべて無効化する。")]
    public bool DebugEnabled = false;

    [Header("Debug — 表示項目")]
    [Tooltip("各攻撃レンジ円を Game/Scene ビューに GL 表示する。")]
    public bool ShowRangeRings = true;
    [Tooltip("ヒットボックスを常時 GL 表示する（非アクティブ=灰、アクティブ=赤）。")]
    public bool ShowHitboxes = true;
    [Tooltip("テールアタックの着弾範囲を GL 表示する。")]
    public bool ShowTailAttack = true;
    [Tooltip("デバッグ HUD パネル（状態・距離・HP バー）を画面に表示する。")]
    public bool ShowHUD = true;
}
