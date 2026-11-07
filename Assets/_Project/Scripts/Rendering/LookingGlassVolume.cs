using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 「鏡の国」をイメージした、夢のような屈折とプリズム色のポストプロセス。
/// 実際の描画は PostProcessFeature と LookingGlassDream.shader が行う。
/// </summary>
[Serializable, VolumeComponentMenu("Custom/Looking Glass Dream")]
public sealed class LookingGlassVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("元映像に対する効果のブレンド量")]
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);

    [Header("Looking Glass")]
    [Tooltip("鏡面越しに見たような、画面中心からの屈折")]
    public ClampedFloatParameter refraction = new(0.018f, 0f, 0.08f);

    [Tooltip("画面中心を軸にした、ゆるやかな渦")]
    public ClampedFloatParameter swirl = new(0.035f, -0.15f, 0.15f);

    [Tooltip("屈折模様が呼吸する速度。0 で静止")]
    public ClampedFloatParameter driftSpeed = new(0.12f, 0f, 1f);

    [Tooltip("画面端に現れる反転像の残響")]
    public ClampedFloatParameter reflectionEcho = new(0.07f, 0f, 0.3f);

    [Header("Dream Color")]
    [Tooltip("赤・青のプリズム分離幅（ピクセル）")]
    public ClampedFloatParameter chromaticShift = new(1.8f, 0f, 6f);

    [Tooltip("明るい部分を夢のように柔らかくする")]
    public ClampedFloatParameter softFocus = new(0.45f, 0f, 1f);

    [Tooltip("虹色の光が揺らぐ強さ")]
    public ClampedFloatParameter prismStrength = new(0.18f, 0f, 0.6f);

    [Tooltip("周辺を暗くし、鏡の奥を覗くような視線誘導を作る")]
    public ClampedFloatParameter vignette = new(0.3f, 0f, 1f);

    public ColorParameter coolTint = new(new Color(0.70f, 0.92f, 1.08f, 1f), true, false, true);
    public ColorParameter warmTint = new(new Color(1.08f, 0.72f, 0.94f, 1f), true, false, true);

    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => false;
}
