using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// AAA タイトルを意識した、画面空間の面整理・深度輪郭・シネマティック階調設定。
/// 実際の描画は PostProcessFeature と CinematicToon.shader が行う。
/// </summary>
[Serializable, VolumeComponentMenu("Custom/Cinematic AAA Toon")]
public sealed class CinematicToonVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("元映像に対するシネマティックトゥーン効果のブレンド量")]
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);

    [Header("Surface Simplification")]
    [Tooltip("輪郭を保ちながら面の細かなノイズを整理する強さ")]
    public ClampedFloatParameter surfaceSmoothing = new(0.48f, 0f, 1f);

    [Tooltip("面を整理するサンプル範囲（ピクセル）")]
    public ClampedFloatParameter smoothingRadius = new(1.35f, 0.5f, 3f);

    [Tooltip("陰影をトゥーン階調として整理する段数")]
    public ClampedIntParameter toneBands = new(6, 3, 12);

    [Tooltip("階調境界の柔らかさ")]
    public ClampedFloatParameter bandSoftness = new(0.18f, 0.02f, 0.48f);

    [Header("Cinematic Ink")]
    [Tooltip("色差と深度差から生成する輪郭の強さ")]
    public ClampedFloatParameter outlineStrength = new(0.52f, 0f, 1.5f);

    [Tooltip("深度輪郭の検出感度")]
    public ClampedFloatParameter depthSensitivity = new(1.7f, 0.1f, 5f);

    [Tooltip("色・明暗輪郭の検出感度")]
    public ClampedFloatParameter colorSensitivity = new(1.25f, 0.1f, 4f);

    [Tooltip("画面解像度に対する輪郭幅（ピクセル）")]
    public ClampedFloatParameter outlineWidth = new(1.15f, 0.5f, 3f);

    [Header("Cinematic Grade")]
    [Tooltip("面の立体感を戻す局所コントラスト")]
    public ClampedFloatParameter localContrast = new(0.22f, 0f, 0.8f);

    [Tooltip("明部だけに加える柔らかなグロー")]
    public ClampedFloatParameter highlightGlow = new(0.24f, 0f, 1f);

    [Tooltip("グローを開始する輝度")]
    public ClampedFloatParameter glowThreshold = new(0.72f, 0.2f, 1.5f);

    [Tooltip("最終カラーの彩度")]
    public ClampedFloatParameter saturation = new(1.08f, 0f, 2f);

    public ColorParameter outlineColor = new(new Color(0.035f, 0.045f, 0.075f, 1f), true, false, true);
    public ColorParameter shadowTint = new(new Color(0.72f, 0.78f, 0.96f, 1f), true, false, true);
    public ColorParameter highlightTint = new(new Color(1.06f, 0.98f, 0.88f, 1f), true, false, true);

    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => false;
}
