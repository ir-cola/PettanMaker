using UnityEngine;

/// <summary>
/// Common enemy tuning values shared by EnemyAI implementations.
/// Per-enemy scripts still own their unique ranges and action timing.
/// </summary>
[CreateAssetMenu(fileName = "EnemySettings", menuName = "Enemies/Settings")]
public class EnemySettings : ScriptableObject
{
    [Header("Health")]
    [Tooltip("最大HP。難易度別SO（EnemySettingsSet）で敵の耐久を増減できる。")]
    [Min(1)]
    public int MaxHP = 30;

    [Header("Detection")]
    public float DetectionRange = 10.0f;
    public float AttackRange = 1.5f;
    public float AttackInterval = 1.5f;

    [Header("Attack")]
    public int AttackDamage = 10;
    [Tooltip("Player の被弾アニメ種別。0=倒れ込まない、1以上=地面に倒れる。")]
    [Min(0)] public int DamageType = 1;

    [Header("被弾ヒットストップ")]
    [Tooltip("被弾時に自分の Animator を止める時間（秒・実時間）。0で無効。基準ダメージ時の値。")]
    public float HitStopDuration = 0.08f;
    [Tooltip("停止中の再生速度（重い一撃側）。0で完全停止。")]
    [Range(0f, 1f)] public float HitStopFrozenSpeed = 0f;
    [Tooltip("停止中の再生速度（軽い一撃側）。軽いヒットは少し動かして重さの差を出す。")]
    [Range(0f, 1f)] public float HitStopFrozenSpeedLight = 0.2f;

    [Header("被弾スケーリング（ダメージ量で強弱）")]
    [Tooltip("被弾ヒットストップ／ブレ／スカッシュをダメージ量でスケールさせる。")]
    public bool HitScaleByDamage = true;
    [Tooltip("HitStopDuration をちょうど1倍にする基準ダメージ。これより大きい一撃ほど強く止まる。")]
    [Min(1f)] public float HitReferenceDamage = 20f;
    [Tooltip("スケール倍率の下限・上限（x=軽い最小, y=重い最大）。")]
    public Vector2 HitScaleRange = new Vector2(0.5f, 2f);

    [Header("被弾スカッシュ＆ストレッチ")]
    [Tooltip("被弾時にモデルを攻撃方向へつぶす。false で無効。")]
    public bool HitSquashEnabled = true;
    [Tooltip("最大強度時、攻撃軸に沿って潰す倍率（<1で薄くなる）。")]
    [Range(0.3f, 1f)] public float HitSquashAlong = 0.78f;
    [Tooltip("最大強度時、攻撃軸と直交方向へ膨らむ倍率（>1で広がる）。")]
    [Range(1f, 1.6f)] public float HitSquashPerp = 1.18f;
    [Tooltip("最大強度時の縦方向倍率。")]
    [Range(0.8f, 1.4f)] public float HitSquashVertical = 1.08f;
    [Tooltip("スカッシュの復帰時間（秒・実時間）。")]
    [Min(0.01f)] public float HitSquashDuration = 0.2f;
    [Tooltip("軽い一撃でも最低これだけ変形させる強度（0..1）。")]
    [Range(0f, 1f)] public float HitSquashMinStrength = 0.35f;

    [Header("被弾ブレ")]
    [Tooltip("被弾時にモデルを小刻みに震わせる揺れ幅（ローカル単位）。0で無効。")]
    [Min(0f)] public float HitShakeAmplitude = 0.06f;
    [Tooltip("1秒あたりのブレ更新回数。大きいほど細かい「ブルブル」になる。")]
    [Min(1f)] public float HitShakeFrequency = 45f;
    [Tooltip("被弾ブレの継続時間（秒・実時間）。0でヒットストップ時間に合わせる。")]
    [Min(0f)] public float HitShakeDuration = 0f;

    [Header("被弾ノックバック")]
    [Tooltip("被弾時に攻撃元から離れる移動距離。0で無効。")]
    [Min(0f)] public float Knockback = 0.8f;
    [Tooltip("ノックバック移動にかける時間（秒）。")]
    [Min(0.01f)] public float KnockbackDuration = 0.12f;

    [Header("Alert")]
    public float AlertDuration = 0.4f;

    [Header("SE - プレイヤー発見")]
    [Tooltip("プレイヤーを初めて発見したときに再生するSE。未設定なら再生しない。")]
    public AudioClip PlayerDetectedSE;
    [Tooltip("プレイヤー発見SEの音量。")]
    [Range(0f, 1f)] public float PlayerDetectedSEVolume = 1f;

    [Header("SE - 撃破")]
    [Tooltip("撃破(死亡)時に再生するSE。未設定なら再生しない。")]
    public AudioClip DefeatedSE;
    [Tooltip("撃破SEの音量。")]
    [Range(0f, 1f)] public float DefeatedSEVolume = 1f;

    [Header("Animator Parameters")]
    public string SpeedParameter = "Speed";
    public string AlertParameter = "IsAlert";

    [Header("Locomotion")]
    [Tooltip("旋回の最大角速度（度/秒）。SmoothDamp の上限としても使う。")]
    public float TurnSpeed = 360f;
    [Tooltip("旋回のイージング時間（秒）。小さいほどキビキビ、大きいほどヌルッと減速して向く。0で従来の等速旋回。")]
    public float TurnSmoothTime = 0.12f;
    [Tooltip("旋回時に進行方向へ傾ける最大角度（度）。0で無効。飛行・突進系に付けると生き物っぽくなる。")]
    public float TurnBankAngle = 0f;

    [Header("徘徊（未発見時）")]
    public bool PreAlertWanderEnabled = true;
    public float PreAlertWanderSpeed = 1.2f;
    public float PreAlertWanderRadius = 4.0f;
    public float PreAlertWanderInterval = 2.5f;
    public float PreAlertWanderArriveDistance = 0.3f;
    public float PreAlertWanderTurnSpeed = 180f;
    [Range(5f, 90f)] public float PreAlertWanderTurnAngle = 35f;
    [Range(0.1f, 1f)] public float PreAlertWanderMinStepRatio = 0.55f;

    [Header("Debug — マスター")]
    [Tooltip("OFF にするとデバッグ表示をすべて無効化する。")]
    public bool DebugEnabled = false;

    [Header("Debug — 表示項目")]
    [Tooltip("検知範囲・攻撃範囲のリングを Scene ビューに常時表示する（選択なしでも見える）。")]
    public bool ShowRangeRings = true;
    [Tooltip("アクション中の攻撃ヒット判定を Gizmos 表示する（IDebugDrawable を実装したアクションのみ）。")]
    public bool ShowHitboxes = true;
    [Tooltip("DebugOverlay に状態・アクション・モーション・距離を表示する。")]
    public bool ShowHUD = true;
}
