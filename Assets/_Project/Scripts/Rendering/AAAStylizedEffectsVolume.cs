using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable, VolumeComponentMenu("Custom/AAA Stylized Effects")]
public sealed class AAAStylizedEffectsVolume : VolumeComponent, IPostProcessComponent
{
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);

    [Header("Halftone / Dot Pattern")]
    public ClampedFloatParameter halftoneStrength = new(0.12f, 0f, 1f);
    public ClampedFloatParameter halftoneScale = new(5.5f, 2f, 16f);
    public ColorParameter halftoneColor = new(new Color(0.055f, 0.07f, 0.13f, 1f), true, false, true);

    [Header("Watercolor Bleeding")]
    public ClampedFloatParameter watercolorStrength = new(0.2f, 0f, 1f);
    public ClampedFloatParameter bleedRadius = new(1.4f, 0.5f, 4f);
    public ClampedFloatParameter pigmentVariation = new(0.035f, 0f, 0.2f);

    [Header("Heat Haze / Refraction")]
    public ClampedFloatParameter heatHazeStrength = new(0.08f, 0f, 1f);
    public ClampedFloatParameter heatHazeScale = new(7f, 1f, 20f);
    public ClampedFloatParameter heatHazeSpeed = new(0.35f, 0f, 2f);
    public ClampedFloatParameter heatHazeMask = new(0.35f, 0f, 1f);

    [Header("Color Lookup / LUT")]
    [Tooltip("任意の2D Strip LUT。未設定時は下の3色によるLookupを使用")]
    public TextureParameter lookupTexture = new(null);
    public ClampedFloatParameter lookupStrength = new(0.3f, 0f, 1f);
    public ClampedIntParameter lookupSize = new(32, 8, 64);
    public ColorParameter lookupShadow = new(new Color(0.75f, 0.82f, 1.05f, 1f), true, false, true);
    public ColorParameter lookupMid = new(new Color(0.98f, 0.96f, 1.02f, 1f), true, false, true);
    public ColorParameter lookupHighlight = new(new Color(1.08f, 0.96f, 0.82f, 1f), true, false, true);

    [Header("Contact Shadow / SSAO")]
    public ClampedFloatParameter contactShadowStrength = new(0.34f, 0f, 1.5f);
    public ClampedFloatParameter contactShadowRadius = new(1.5f, 0.5f, 4f);
    public ClampedFloatParameter contactShadowBias = new(0.002f, 0.0001f, 0.02f);
    public ColorParameter contactShadowColor = new(new Color(0.45f, 0.5f, 0.7f, 1f), true, false, true);

    [Header("Cinematic Blur")]
    [Tooltip("低コントラスト領域を中心に、輪郭を保ってぼかす")]
    public ClampedFloatParameter blurStrength = new(0.1f, 0f, 1f);
    public ClampedFloatParameter blurRadius = new(1.25f, 0.5f, 4f);

    [Header("Kuwahara Filter")]
    [Tooltip("最も色分散の小さい領域を選び、輪郭を残したまま面を絵画的に整理する")]
    public ClampedFloatParameter kuwaharaStrength = new(0.22f, 0f, 1f);
    public ClampedFloatParameter kuwaharaRadius = new(1.6f, 0.75f, 4f);

    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => false;
}
