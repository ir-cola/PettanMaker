using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Cheshire ステージ向けの、絵本・油彩調スタイライズ設定。
/// 実際の描画は PostProcessFeature と AnimeStylize.shader が行う。
/// </summary>
[Serializable, VolumeComponentMenu("Custom/Alice Painting")]
public sealed class AlicePaintingVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("元映像に対する絵画効果のブレンド量")]
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);

    [Header("Brush")]
    [Tooltip("筆跡として色をまとめる範囲")]
    public ClampedFloatParameter brushSize = new(1.6f, 0.5f, 4f);

    [Tooltip("周辺色を混ぜる強さ")]
    public ClampedFloatParameter brushStrength = new(0.65f, 0f, 1f);

    [Tooltip("筆跡をわずかに揺らし、手描き感を出す強さ")]
    public ClampedFloatParameter wobble = new(0.35f, 0f, 2f);

    [Header("Ink & Paper")]
    [Tooltip("暗い輪郭を加える強さ")]
    public ClampedFloatParameter edgeStrength = new(0.38f, 0f, 2f);

    [Tooltip("輪郭として拾い始める明暗差")]
    public ClampedFloatParameter edgeThreshold = new(0.12f, 0.01f, 0.5f);

    [Tooltip("紙・キャンバス表面の粒子")]
    public ClampedFloatParameter paperGrain = new(0.055f, 0f, 0.25f);

    [Header("Storybook Color")]
    public ClampedIntParameter posterizeSteps = new(7, 3, 12);
    public ClampedFloatParameter saturation = new(1.18f, 0f, 2f);
    public ClampedFloatParameter contrast = new(1.08f, 0.5f, 2f);
    public ColorParameter shadowTint = new(new Color(0.72f, 0.67f, 0.94f, 1f), true, false, true);
    public ColorParameter midTint = new(new Color(0.94f, 0.91f, 1.04f, 1f), true, false, true);
    public ColorParameter highlightTint = new(new Color(1.08f, 0.96f, 0.72f, 1f), true, false, true);

    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => false;
}
