using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ボスのエフェクト全般パラメータをまとめた ScriptableObject。
/// B1_EffectManager にアサインすることで各 VFX コンポーネントに一括適用される。
/// 未アサインの場合は各 VFX コンポーネントの Inspector デフォルト値が使われる。
/// </summary>
[CreateAssetMenu(fileName = "B1_EffectSettings", menuName = "Boss01/EffectSettings")]
public class B1_EffectSettings : ScriptableObject
{
    [Header("チャージ収束 VFX（Rush）")]
    public ChargeVFXSettings  Charge   = new();

    [Header("テールチャージ収束 VFX")]
    public ChargeVFXSettings TailCharge = new()
    {
        ColorOuter      = new Color(0.35f, 0.1f,  1f,   1f),
        ColorInner      = new Color(0.7f,  0.5f,  1f,   1f),
        EmitRadiusStart = 1.8f,
        EmitRadiusEnd   = 0.5f,
        EmitRateStart   = 18f,
        EmitRateEnd     = 100f,
    };

    [Header("着地衝撃波 VFX（TailAttack）")]
    public LandingImpactVFXSettings LandingImpact = new();

    [Header("風切り VFX（Rush）")]
    public RushWindVFXSettings RushWind = new();

    [Header("目トレイル VFX（Rush）")]
    public EyeRushVFXSettings  EyeRush  = new();

    // ────────────────────────────────────────────
    // 戦闘の3D衝撃波（B1_ShockwaveDistortion/ShockwaveRefract3D をアクションから発生）
    // ────────────────────────────────────────────

    [Header("着地の Squash & Stretch（TailAttack）")]
    public LandingSquashSettings LandingSquash = new();

    [Header("戦闘の3D衝撃波：突進発射（基準=ボス）")]
    public B1_CombatShockwaveSettings ChargeLaunchShockwave = new()
    {
        positionOffset = new Vector3(0f, 1.6f, 0f),
        duration = 0.55f, startScale = 1.5f, endScale = 13f, ringWidth = 0.32f,
        scale3D = new Vector3(1f, 0.55f, 1f),
        rimColor = new Color(1.6f, 0.9f, 0.35f, 1f), rimIntensity = 2.6f,
    };

    [Header("戦闘の3D衝撃波：テール着地（基準=ボス）")]
    [FormerlySerializedAs("TailJumpShockwave")]
    public B1_CombatShockwaveSettings TailLandShockwave = new()
    {
        positionOffset = new Vector3(0f, 1.0f, 0f),
        duration = 0.5f, startScale = 1f, endScale = 9f, ringWidth = 0.3f,
        scale3D = new Vector3(1f, 0.45f, 1f), rimIntensity = 2.2f,
    };

    [Header("戦闘の3D衝撃波：ひっかき3段目（基準=ボス。z=前方オフセットで爪の先へ）")]
    public B1_CombatShockwaveSettings ScratchFinisherShockwave = new()
    {
        positionOffset = new Vector3(0f, 1.4f, 1.5f),
        duration = 0.5f, startScale = 1.2f, endScale = 11f, ringWidth = 0.32f,
        scale3D = new Vector3(1f, 0.6f, 1f),
        rimColor = new Color(1.6f, 0.4f, 0.4f, 1f), rimIntensity = 2.4f,
    };

    [Header("戦闘の3D衝撃波：ダメージ波がプレイヤーを通過（基準=プレイヤー）")]
    public B1_CombatShockwaveSettings WavePassShockwave = new()
    {
        positionOffset = new Vector3(0f, 1.0f, 0f),
        duration = 0.45f, startScale = 1.2f, endScale = 8f, ringWidth = 0.3f,
        scale3D = new Vector3(1f, 0.5f, 1f),
        rimColor = new Color(1.4f, 0.75f, 0.6f, 1f), rimIntensity = 2.2f,
    };

    [Header("魔力地面爆発 VFX（TailAttack マナバーストクロス）")]
    public ManaBurstVFXSettings ManaBurst = new();

    // 攻撃の「瞬間」だけ周囲を照らすフラッシュ光。持続光は目が慣れて意味を失う＋
    // URPの追加ライト上限に食い込むため、必ず短時間の単発にする。
    // 色は攻撃カテゴリの言語に合わせる：物理=赤/オレンジ、魔力=紫。
    [Header("攻撃フラッシュ光：ひっかき3段目（爪先の赤）")]
    public ImpactFlashSettings ScratchFinisherFlash = new()
    {
        Color = new Color(1f, 0.35f, 0.3f), Intensity = 7f, Range = 11f, Duration = 0.2f,
    };

    [Header("攻撃フラッシュ光：突進発射（オレンジ）")]
    public ImpactFlashSettings ChargeLaunchFlash = new()
    {
        Color = new Color(1f, 0.6f, 0.25f), Intensity = 6f, Range = 12f, Duration = 0.18f,
    };

    [Header("攻撃フラッシュ光：テール着地（既定OFF：着地爆発VFXが同じ瞬間に光るため二重になる）")]
    public ImpactFlashSettings TailLandFlash = new()
    {
        Enabled = false,
        Color = new Color(1f, 0.7f, 0.35f), Intensity = 5f, Range = 12f, Duration = 0.2f,
    };
}

/// <summary>
/// 攻撃の瞬間に焚く単発フラッシュ光の設定。B1_ImpactLight（自壊するポイントライト・影なし）を使う。
/// </summary>
[Serializable]
public class ImpactFlashSettings
{
    [Tooltip("OFF でこの攻撃のフラッシュ光を出さない。")]
    public bool Enabled = true;

    [Tooltip("光の色。物理攻撃=赤/オレンジ、魔力技=紫、と攻撃カテゴリで揃えると光った瞬間に技種が伝わる。")]
    public Color Color = new Color(1f, 0.6f, 0.3f);

    [Tooltip("光の強さ。")]
    public float Intensity = 6f;

    [Tooltip("光の届く範囲（m）。")]
    public float Range = 12f;

    [Tooltip("点灯からフェード消灯までの時間（秒）。0.15〜0.3 の短時間を推奨（瞬間だから効く）。")]
    public float Duration = 0.2f;

    /// <summary>設定に従いフラッシュ光を1発出す（無効なら何もしない）。</summary>
    public void Play(Vector3 position)
    {
        if (!Enabled) return;
        B1_ImpactLight.Spawn(position, Color, Intensity, Range, Duration);
    }
}

// ────────────────────────────────────────────
// 各 VFX の設定クラス
// ────────────────────────────────────────────

[Serializable]
public class ChargeVFXSettings
{
    [Header("収束半径")]
    [Tooltip("チャージ開始時のパーティクル発生半径（m）。")]
    public float EmitRadiusStart = 2f;
    [Tooltip("チャージ完了時に絞り込む半径（m）。")]
    public float EmitRadiusEnd   = 0.6f;

    [Header("速度")]
    [Tooltip("チャージ開始時の引き込み速度（m/s）。")]
    public float InwardSpeedStart = 4f;
    [Tooltip("チャージ完了時の引き込み速度（m/s）。")]
    public float InwardSpeedEnd   = 20f;

    [Header("放出レート")]
    public float EmitRateStart = 20f;
    public float EmitRateEnd   = 120f;

    [Header("色")]
    public Color ColorOuter = new Color(1f, 0.45f, 0.05f, 1f);
    public Color ColorInner = new Color(1f, 0.95f, 0.35f, 1f);

    [Header("バースト（突進移行時）")]
    public int   BurstCount    = 120;
    public float BurstSpeedMin = 10f;
    public float BurstSpeedMax = 24f;

    [Header("マテリアル")]
    [Tooltip("null の場合は VFX コンポーネントの Inspector 値を使用する。")]
    public Material ParticleMaterial;
}

[Serializable]
public class RushWindVFXSettings
{
    [Header("放出形状（ボス前面断面）")]
    public float EmitWidth  = 1.4f;
    public float EmitHeight = 1.8f;
    public float EmitDepth  = 0.2f;

    [Header("速度")]
    public float SpeedMin = 14f;
    public float SpeedMax = 26f;

    [Header("放出レート")]
    public float EmitRate = 120f;

    [Header("寿命")]
    public float LifetimeMin = 0.10f;
    public float LifetimeMax = 0.22f;

    [Header("ストレッチ")]
    [Tooltip("大きいほど速度方向に長く引き伸ばされる。")]
    public float VelocityScale = 0.12f;

    [Range(0f, 1f)]
    [Tooltip("0=直線、1=全方向。広げると霧散感が出る。")]
    public float SpreadAmount = 0.15f;

    [Header("色")]
    public Color ColorHead = new Color(1.0f,  1.0f,  1.0f,  1.0f);
    public Color ColorMid  = new Color(0.55f, 0.85f, 1.0f,  0.8f);

    [Header("マテリアル")]
    [Tooltip("null の場合は VFX コンポーネントの Inspector 値を使用する。")]
    public Material ParticleMaterial;
}

[Serializable]
public class EyeRushVFXSettings
{
    [Header("Trail")]
    [Tooltip("トレイルが残る時間（秒）。")]
    public float TrailTime       = 0.25f;
    public float TrailWidthStart = 0.08f;
    public float TrailWidthEnd   = 0.0f;
    public Color TrailColorHead  = new Color(1f, 0.15f, 0.05f, 1f);
    public Color TrailColorTail  = new Color(1f, 0.35f, 0.1f,  0f);

    [Header("マテリアル")]
    [Tooltip("目メッシュの Rush 中マテリアル。null の場合は Inspector 値を使用する。")]
    public Material RushMaterial;
    [Tooltip("Trail マテリアル。null の場合は Inspector 値を使用する。")]
    public Material TrailMaterial;
}

[Serializable]
public class LandingSquashSettings
{
    [Tooltip("OFF で Squash & Stretch を無効化する。")]
    public bool Enabled = true;

    [Tooltip("着地の瞬間の潰れスケール（基準スケールへの倍率）。Y を潰して XZ を広げる。")]
    public Vector3 SquashScale = new Vector3(1.3f, 0.6f, 1.3f);

    [Tooltip("潰れ→弾んで基準スケールへ収束するまでの時間（秒）。")]
    public float Duration = 0.25f;

    [Tooltip("減衰振動の半周期数。3 で 潰れ→伸び→小さく潰れ→収束（弾む見た目）。1 で単純に戻るだけ。")]
    [Range(1f, 6f)] public float ReboundCycles = 3f;
}

[Serializable]
public class ManaBurstVFXSettings
{
    [Header("爆発本体")]
    [Tooltip("地面爆発エフェクトのプレハブ（Kenney素材のParticleSystem等）。\nnull なら EffectManager の ExplosionVFX（着地爆発と同じもの）で代用する。")]
    public GameObject BurstVFXPrefab;

    [Tooltip("爆発時のフラッシュ光の色。チェシャ猫の魔力＝TailCharge と同系の紫を推奨。")]
    public Color BurstColor = new Color(0.7f, 0.5f, 1f, 1f);

    [Header("爆発跡")]
    [Tooltip("爆発跡デカールのマテリアル。PettanMaker/Boss_01/GroundScar シェーダ推奨\n（残り火の冷却 _Cool と消滅 _Fade をスクリプトが流す。ノーマルマップ込みで Builder が自動生成できる）。\n専用シェーダでないマテリアルでも色フェードで動く。null なら跡を残さない。")]
    public Material ScarMaterial;

    [Tooltip("爆発跡が消えるまでの時間（秒）。")]
    public float ScarDuration = 4f;

    [Header("落雷＋空間の裂け目")]
    [Tooltip("各着弾で空から紫の魔力の雷を落とすか。既存の地面爆発はそのままに縦の一撃を足す。")]
    public bool EnableManaBolt = true;

    [Tooltip("雷本体のマテリアル（Kenney spark ＋ 加算発光推奨。M_ManaBolt_Strike）。\nnull なら Sprites/Default で色だけの簡易フォールバック。")]
    public Material BoltMaterial;

    [Tooltip("着弾後に残る『歪んだ空間の裂け目』のマテリアル（URP Particles の Distortion 推奨。M_ManaBolt_Rift）。\nnull なら暗い紫の残り裂けフォールバック。屈折には URP の Opaque Texture が必要。")]
    public Material RiftMaterial;

    [Tooltip("雷・裂け目の色（頂点カラー）。チェシャ猫の魔力＝紫を推奨。")]
    public Color BoltColor = new Color(0.7f, 0.5f, 1f, 1f);

    [Tooltip("雷の落下高さ（m）。上空どのくらいから落ちるか。実際にはここに AoE 半径が加算される。")]
    public float BoltHeight = 14f;

    [Tooltip("雷の太さ ＝ AoE半径に対する倍率。太さ(m) = この値 × 爆発の半径。\n大きい爆発ほど太い雷になる。見やすさ優先なら 0.5〜0.8 くらい。")]
    public float BoltWidth = 0.6f;

    [Tooltip("雷の閃光が残る時間（秒）。短いほど一撃らしい。")]
    public float BoltStrikeDuration = 0.12f;

    [Tooltip("着弾後の裂け目が残る時間（秒）。")]
    public float RiftDuration = 1.2f;

    [Tooltip("フィニッシュの雷を何倍太く・高くするか。道中は細く、最後だけ極太に。")]
    public float FinisherBoltScale = 1.8f;

    [Tooltip("雷の着弾点の高さ補正（m）。爆発中心（AoeOffset）が地面より高くて雷が浮くときに、\n負の値で着弾点を地面まで下げる。0＝爆発中心と同じ高さ。")]
    public float BoltImpactYOffset = 0f;

    [Header("着弾スパーク球")]
    [Tooltip("着弾点で半透明球を膨らませ、球面の法線方向へ Spark を撒くか。")]
    public bool EnableImpactSparks = true;

    [Tooltip("半透明球のマテリアル。null なら URP Unlit の半透明フォールバック。")]
    public Material ImpactSphereMaterial;

    [Tooltip("Spark パーティクルのマテリアル（Kenney spark ＋加算推奨。M_ManaBolt_Strike 等を流用可）。\nnull なら Sprites/Default フォールバック。")]
    public Material ImpactSparkMaterial;

    [Tooltip("球・スパークの色。チェシャ猫の魔力＝紫を推奨。")]
    public Color ImpactSparkColor = new Color(0.7f, 0.5f, 1f, 1f);

    [Tooltip("球の半径 ＝ AoE半径に対する倍率。半径(m) = この値 × 爆発の半径。")]
    public float ImpactSphereRadiusScale = 1f;

    [Tooltip("球が膨らんで消えるまでの時間（秒）。")]
    public float ImpactSphereDuration = 0.35f;

    [Tooltip("スパークの数。")]
    public int ImpactSparkCount = 24;

    [Tooltip("スパークの飛び出し速度（m/s）。")]
    public float ImpactSparkSpeed = 8f;

    [Tooltip("スパーク1粒の大きさ。")]
    public float ImpactSparkSize = 0.4f;

    [Tooltip("スパークの寿命（秒）。")]
    public float ImpactSparkDuration = 0.4f;
}

[Serializable]
public class LandingImpactVFXSettings
{
    [Header("衝撃波リング")]
    public int   RingCount    = 80;
    public float RingSpeed    = 10f;
    public float RingLifetime = 0.55f;
    public Color RingColorHead = new Color(1f, 0.85f, 0.3f, 1f);
    public Color RingColorTail = new Color(1f, 0.45f, 0.1f, 0f);

    [Header("土煙")]
    public int   DustCount   = 55;
    public float DustSpeedMin = 2.5f;
    public float DustSpeedMax = 9f;
    public Color DustColor   = new Color(0.72f, 0.57f, 0.32f, 0.85f);

    [Header("マテリアル")]
    [Tooltip("リング・土煙共通マテリアル。null の場合は VFX コンポーネントの Inspector 値を使用する。")]
    public Material ParticleMaterial;
}
