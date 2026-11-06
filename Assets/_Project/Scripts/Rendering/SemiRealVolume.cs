using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 通常プレイ向けの自然なセミリアル画作り。
/// 質感、細部、接地感、空気遠近を保ちつつ画面を整理する。
/// </summary>
[Serializable, VolumeComponentMenu("Custom/Semi Real Rendering")]
public sealed class SemiRealVolume : VolumeComponent, IPostProcessComponent
{
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);

    [Header("Material Detail")]
    public ClampedFloatParameter clarity = new(0.22f, 0f, 1f);
    public ClampedFloatParameter sharpen = new(0.16f, 0f, 1f);
    public ClampedFloatParameter surfaceSmoothing = new(0.12f, 0f, 1f);
    public ClampedFloatParameter detailRadius = new(1f, 0.5f, 3f);
    public ClampedFloatParameter highlightRolloff = new(0.3f, 0f, 1f);

    [Header("Contact & Depth")]
    public ClampedFloatParameter contactAO = new(0.28f, 0f, 1.5f);
    public ClampedFloatParameter aoRadius = new(1.35f, 0.5f, 4f);
    public ClampedFloatParameter aoBias = new(0.002f, 0.0001f, 0.02f);
    public ColorParameter aoColor = new(new Color(0.48f, 0.52f, 0.62f, 1f), true, false, true);

    [Header("Natural Color")]
    public ClampedFloatParameter exposure = new(1.02f, 0.5f, 2f);
    public ClampedFloatParameter contrast = new(1.06f, 0.7f, 1.5f);
    public ClampedFloatParameter saturation = new(1.03f, 0f, 2f);
    public ColorParameter shadowTint = new(new Color(0.91f, 0.95f, 1.02f, 1f), true, false, true);
    public ColorParameter highlightTint = new(new Color(1.03f, 1f, 0.95f, 1f), true, false, true);

    [Header("Atmosphere")]
    public ClampedFloatParameter aerialStrength = new(0.16f, 0f, 1f);
    public MinFloatParameter aerialStart = new(75f, 0f);
    public MinFloatParameter aerialEnd = new(380f, 0f);
    public ColorParameter aerialColor = new(new Color(0.66f, 0.74f, 0.86f, 1f), true, false, true);

    [Header("Lens Correction")]
    public ClampedFloatParameter lensDistortion = new(0.006f, -0.08f, 0.08f);
    public ClampedFloatParameter chromaticAberration = new(0.25f, 0f, 2f);

    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => false;
}
