/*
 File   : プレイヤー攻撃設定アセット [PlayerAttackSettings.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/18
 攻撃 (コンボ / チャージ / アリス) のチューニング値を ScriptableObject として資産化したもの。
 既定値は Cheshire シーンの Player (PlayerAttack) の調整済み値。
 PlayerAttack にアサインすると Awake でシーン側の値を上書きする。
 ※ プレハブ / Transform / LayerMask 等のシーン参照はアセット化対象外 (シーン側で設定)。
*/
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerAttackSettings", menuName = "Player/Attack Settings")]
public class PlayerAttackSettings : ScriptableObject {

    [Header("攻撃設定 (共有)")]
    [Tooltip("チャージ叩きつけのダメージ")]
    public int AttackDamage = 20;
    [Tooltip("叩きつけの効果半径 (予想サークル表示にも使う)")]
    public float AttackRadius = 2.0f;

    [Header("ヒット演出 (エネミー命中時のみ)")]
    [Tooltip("命中時のグローバルヒットストップ時間 (秒)。0で無効")]
    [Min(0f)] public float HitGlobalHitstop = 0.09f;
    [Tooltip("命中時ヒットストップ中の timeScale (小さいほど強く止まる)")]
    [Range(0.01f, 0.5f)] public float HitGlobalHitstopScale = 0.06f;
    public float HitShakeDuration = 0.22f;
    public float HitShakeIntensity = 0.28f;

    [Header("着地スカッシュ設定")]
    public float ImpactSquashScaleXZ = 1.3f;
    public float ImpactSquashScaleY = 0.6f;
    public float ImpactSquashDuration = 0.2f;

    [Header("演出設定 (空振りシェイク / 消滅)")]
    public float ShakeDuration = 0.15f;
    public float ShakeIntensity = 0.10f;
    public float HitstopDurationEnemy = 0.08f;
    public float HitstopDurationSelf = 0.04f;
    [Tooltip("着地後に武器を消去するまでの時間 (前半据え置き→後半ディゾルブ)")]
    public float DestroyDelay = 0.5f;

    [Header("コンボ (タップ攻撃 = 3段)")]
    [Tooltip("これ未満で攻撃ボタンを離す=タップ=コンボ。以上=長押し=チャージ (秒)")]
    public float TapThreshold = 0.15f;
    [Tooltip("コンボの持続 / 次段受付時間 (Duration, 秒)。この時間内に押すと次段へ繋がる。過ぎるとコンボ終了")]
    public float ComboWindow = 0.6f;
    [Tooltip("コンボ各段の最短間隔 (秒)。連打してもこの間隔より速く次段へ進まない (超高速コンボ防止)")]
    [Min(0f)] public float ComboMinInterval = 0.3f;
    [Tooltip("コンボ終了後、次のコンボを始められるまでのクールタイム (秒)")]
    [Min(0f)] public float ComboCooldown = 0f;
    [Tooltip("コンボ各段のダメージ (要素0=1段目 / 1=2段目 / 2=3段目...)。段数より要素が少ない場合は末尾の値を使う")]
    public int[] ComboStageDamages = { 15, 15, 20 };
    [Tooltip("コンボ各段の全体ヒットストップ時間 (秒。要素0=1段目...)。0で無効、要素が足りなければ末尾の値を使う")]
    public float[] ComboStageHitstops = { 0.09f, 0.09f, 0.12f };
    [Tooltip("斬撃(スタンプ)の飛行時間。短いほど『スパッ』と速い (秒)")]
    [Min(0.02f)] public float ComboFlightTime = 0.12f;
    [Tooltip("斬撃の当たり判定の太さ (掃引カプセル半径 m)")]
    public float SlashHitRadius = 1.6f;

    [Header("コンボ 段ごとのスタンプ生成タイミング (発動から何秒後に飛ばすか)")]
    [Tooltip("1段目のスタンプ生成ディレイ(秒)")]
    [Min(0f)] public float Combo1SpawnDelay = 0f;
    [Tooltip("2段目のスタンプ生成ディレイ(秒)")]
    [Min(0f)] public float Combo2SpawnDelay = 0f;
    [Tooltip("3段目のスタンプ生成ディレイ(秒)")]
    [Min(0f)] public float Combo3SpawnDelay = 0.3f;

    [Header("コンボの前進ステップ (一歩踏み出す)")]
    [Tooltip("1段あたりの前進距離 (m)")]
    public float ComboStepDistance = 1.2f;
    [Tooltip("前進にかける時間 (秒)")]
    [Min(0.01f)] public float ComboStepTime = 0.18f;

    [Header("コンボ ロックオン (近くの敵へ自動ロック)")]
    [Tooltip("コンボ開始時にこの半径内の最近接エネミーへロックオン (m)。0以下で無効")]
    public float ComboLockRadius = 7f;
    [Tooltip("攻撃入力がこの秒数あくとロックオンが消える (秒)")]
    [Min(0.1f)] public float ComboLockLinger = 1.2f;
    [Tooltip("ロックオン中の前進ステップ距離倍率 (1段/2段/3段目)。段階的に伸ばして早く敵へ届く")]
    public float[] ComboLockStepMul = { 1.0f, 1.6f, 2.2f };
    [Tooltip("ロックオン中、敵のこの距離手前でステップを頭打ちにする (すり抜け・めり込み防止 m)")]
    [Min(0f)] public float ComboLockStopRange = 1.2f;

    [Header("コンボ カメラ演出 (発動準備で引き → 攻撃開始でズーム)")]
    [Tooltip("発動準備(生成ディレイ中)のカメラ引き量 (m)。0で無効")]
    public float ComboWindupPull = 0.6f;
    [Tooltip("攻撃開始(スタンプ生成)のカメラ寄せ量 (m)。大きすぎると酔うので控えめに")]
    public float ComboZoomIn = 1f;
    [Tooltip("攻撃開始のFOV望遠 (度)。寄る感じを強める")]
    public float ComboZoomFov = 6f;
    [Tooltip("ズーム(寄り)にかける時間 (秒)")]
    [Min(0.02f)] public float ComboZoomDur = 0.18f;
    [Tooltip("ズームから元の画角へ戻す時間 (秒)。寄りより長め=ゆっくりめのイージングで戻る")]
    [Min(0.02f)] public float ComboZoomReturn = 0.55f;

    [Header("チャージ: 召喚/着地予測オフセット")]
    [Tooltip("前方オフセット (m)")]
    public float ForwardOffset = 2.6f;
    [Tooltip("高さオフセット (m)")]
    public float HeightOffset = 3.0f;
    [Tooltip("横オフセット (フォールバック配置 m)")]
    public float SideOffset = 1.0f;
    [Tooltip("武器同士の間隔 (フォールバック配置 m)")]
    public float BetweenWeaponsOffset = 0.5f;

    [Header("チャージ設定")]
    public float MaxChargeTime = 1.6f;
    public float[] ReleaseThresholds = { 0f, 0.7f, 1.6f };
    [Tooltip("Charge1 連打防止用クールタイム (秒)。Charge2/3 は対象外")]
    public float Charge1Cooldown = 0.6f;

    [Header("叩きつけ設定")]
    [Tooltip("最大落下速度 (弧不成立時のフォールバック用)")]
    public float SlamSpeed = 15.0f;
    [Tooltip("落下加速度 (同上)")]
    public float SlamGravity = 100f;
    [Tooltip("スポーン演出 + 叩きつけ前の待機 (秒)")]
    public float SlamWaitTime = 0.15f;

    [Header("弧軌道スラム設定 (全スタンプが落下予想点1点へ収束)")]
    [Tooltip("同時召喚の時差 (秒)。0で完全同時 (V字収束は同時着弾が気持ちいい)")]
    [Min(0f)] public float ArcStagger = 0.05f;
    [Tooltip("弧の飛行時間 (秒)。t^2 イージングで終端ほど速く『ズドンッ』。短いほど斜めスパッ")]
    [Min(0.05f)] public float ArcFlightTime = 0.24f;
    [Tooltip("V字収束の横広がり (m)。Charge2/3 でスタンプが左右から飛んでくる距離")]
    public float ConvergeSpread = 2.4f;
    [Tooltip("V字収束の開始高さ (m)")]
    public float ConvergeHeight = 3.0f;
    [Tooltip("着地点を各スタンプの進入方向側へ寄せる量 (m)。中心を通り越す(貫通)のを防ぐ")]
    [Min(0f)] public float ConvergeJitter = 0.4f;
    [Tooltip("V字軌道の膨らみ (m)。小さいほど直線的な斜め軌道 (スパッ)、大きいほど弧")]
    public float ArcRise = 0.25f;

    [Header("アリス: ちびスタンプ (小さいとき・非ダメージ)")]
    [Tooltip("スタンプ倍率 (weaponPrefab 基準)")]
    public float ChibiScaleMul = 0.4f;
    [Tooltip("召喚する高さ (m)")]
    public float ChibiHeight = 3.5f;
    [Tooltip("前方オフセット (m)")]
    public float ChibiForward = 1.0f;
    [Tooltip("出現ポップの時間 (s)")]
    [Min(0f)] public float ChibiAppearTime = 0.1f;
    [Tooltip("最大落下速度 (m/s)")]
    public float ChibiFallSpeed = 16f;
    [Tooltip("落下加速度 (m/s^2)。大きいほど鋭く『ぺちっ』")]
    public float ChibiFallGravity = 120f;

    [Header("アリス: 巨大スタンプ (巨大化チャージ・多段ヒット)")]
    [Tooltip("スタンプ倍率 (weaponPrefab 基準)")]
    public float GiantScaleMul = 2.6f;
    [Tooltip("召喚する高さ (m)")]
    public float GiantHeight = 7f;
    [Tooltip("前方オフセット (m)")]
    public float GiantForward = 1.5f;
    [Tooltip("出現ポップの時間 (s)")]
    [Min(0f)] public float GiantAppearTime = 0.16f;
    [Tooltip("叩き落としの時間 (s)。短いほど鋭い『ズドン』")]
    [Min(0.05f)] public float GiantSlamTime = 0.22f;
    [Tooltip("着地の効果半径 (m)")]
    public float GiantRadius = 4.5f;
    [Tooltip("1ヒットぶんのダメージ (2発目以降)")]
    public int GiantDamagePerHit = 25;
    [Tooltip("多段ヒットの1発目だけのダメージ (チャージ/巨大スタンプの初撃を強くする)。0以下で GiantDamagePerHit を使う")]
    public int GiantFirstHitDamage = 40;
    [Tooltip("多段ヒットの回数")]
    [Min(1)] public int GiantHitCount = 4;
    [Tooltip("多段ヒットの間隔 (s)")]
    [Min(0.01f)] public float GiantHitInterval = 0.09f;
    [Tooltip("チャージ/巨大スタンプ着地の全体ヒットストップ時間 (秒。最後の一撃で1回)。0で無効")]
    [Min(0f)] public float GiantHitstop = 0.12f;

    [Header("アリス: 巨大化チャージ カメラ (溜め完了時に1回)")]
    [Tooltip("溜め完了時のパンチズーム量 (m。正でグッと寄る→戻る)。巨大時はカメラが引けているので大きめ")]
    public float GiantChargeZoomIn = 2.2f;
    [Tooltip("溜め完了時の FOV パンチ (度。負で望遠=寄る、正で広角)")]
    public float GiantChargeFovPunch = -7f;
}
