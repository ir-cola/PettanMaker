using System;
using UnityEngine;

/// <summary>
/// 戦闘アクション（突進発射 / テール着地キック / ひっかき3段目）で出す
/// ワールド空間の3D衝撃波1波ぶんの設定。<see cref="B1_ShockwaveDistortion.Params"/>（use3D=true）へ
/// 変換して Spawn する。既定は床に沿って潰したリング状の発光シェル（RoarWave3D シェーダ）。
/// スクリーン空間の後処理ではなく、シーン内で立体的に膨張するので安っぽくならない。
/// Roar 衝撃波（<see cref="B1_CameraSceneSettings.ShockwaveSettings"/>）と同じ 3D 波を、
/// 戦闘テンポに合わせて素早く小さく出すための軽量プリセット。
/// </summary>
[Serializable]
public class B1_CombatShockwaveSettings
{
    [Tooltip("OFF でこの衝撃波を無効化する。")]
    public bool enabled = true;

    [Header("表示位置")]
    [Tooltip("発生地点の基準（ボス、または対象）からのローカルオフセット。x=右, y=上, z=前。\n基準の向きに追従して回転する。")]
    public Vector3 positionOffset = new Vector3(0f, 1.5f, 0f);

    [Header("寿命 / サイズ")]
    [Tooltip("1波の寿命(秒)。小さいほど素早い。")]
    public float duration = 0.5f;
    [Tooltip("開始スケール。")]
    public float startScale = 1.2f;
    [Tooltip("最大スケール。Roar は 18。戦闘は 9〜13 くらいが見やすい。")]
    public float endScale = 11f;
    [Tooltip("軸別倍率。Y を小さくすると床に沿ったリング状の衝撃波になる（潰しすぎると薄く見えにくい）。")]
    public Vector3 scale3D = new Vector3(1f, 0.55f, 1f);

    [Header("見た目")]
    [Tooltip("シェル幅。")]
    [Range(0.02f, 0.9f)] public float ringWidth = 0.4f;
    [Tooltip("表面のゆらぎ量。")]
    [Range(0f, 1f)] public float strength = 0.2f;
    [Tooltip("背後のシーンを歪ませる屈折量。0 で発光のみ。0.04〜0.10 で明確に歪む。")]
    [Range(0f, 0.3f)] public float refraction = 0.06f;
    [Tooltip("発光色（HDR）。")]
    [ColorUsage(true, true)] public Color rimColor = new Color(1.4f, 0.85f, 0.4f, 1f);
    [Tooltip("発光強度。")]
    public float rimIntensity = 2.2f;

    [Tooltip("3D衝撃波 Material。未設定なら RoarWave3D シェーダから自動生成する。\nビルドで確実に出すなら RoarWave3D マテリアルを割り当てておくこと。")]
    public Material material;

    /// <summary>基準transformのローカル軸に沿って positionOffset を適用したワールド座標を返す。</summary>
    public Vector3 ResolvePosition(Transform origin)
    {
        if (origin == null) return positionOffset;
        return origin.position + origin.TransformVector(positionOffset);
    }

    /// <summary>1波ぶんの見た目パラメータへ変換する（常にワールド3D）。</summary>
    public B1_ShockwaveDistortion.Params ToParams() => new B1_ShockwaveDistortion.Params
    {
        duration     = duration,
        strength     = strength,
        startScale   = startScale,
        endScale     = endScale,
        ringWidth    = ringWidth,
        rimColor     = rimColor,
        rimIntensity = rimIntensity,
        use3D        = true,
        material     = material,
        scale3D      = scale3D,
        refraction   = refraction,
    };
}
