using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 古い児童書の挿絵、銅版画、切り絵をイメージしたポストプロセス設定。
/// 実際の描画は PostProcessFeature と StorybookPrint.shader が行う。
/// </summary>
[Serializable, VolumeComponentMenu("Custom/Storybook Print")]
public sealed class StorybookPrintVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("元映像に対する古い挿絵効果のブレンド量")]
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);

    [Header("Printed Illustration")]
    [Tooltip("明暗を何段階のインク色に分けるか")]
    public ClampedIntParameter toneSteps = new(6, 3, 12);

    [Tooltip("元映像の色を残す量")]
    public ClampedFloatParameter colorRetention = new(0.42f, 0f, 1f);

    [Tooltip("版画の輪郭線の濃さ")]
    public ClampedFloatParameter inkStrength = new(0.62f, 0f, 1.5f);

    [Tooltip("輪郭として拾い始める明暗差")]
    public ClampedFloatParameter edgeThreshold = new(0.105f, 0.01f, 0.5f);

    [Tooltip("暗部に入る斜線ハッチングの濃さ")]
    public ClampedFloatParameter hatchStrength = new(0.34f, 0f, 1f);

    [Tooltip("印刷版のわずかな色ずれ（ピクセル）")]
    public ClampedFloatParameter printOffset = new(0.7f, 0f, 3f);

    [Header("Aged Paper")]
    [Tooltip("紙の繊維や印刷ムラ")]
    public ClampedFloatParameter paperGrain = new(0.07f, 0f, 0.25f);

    [Tooltip("赤い物をトランプのクリムゾン色として強調する量")]
    public ClampedFloatParameter crimsonAccent = new(0.55f, 0f, 1f);

    public ColorParameter inkColor = new(new Color(0.095f, 0.07f, 0.12f, 1f), true, false, true);
    public ColorParameter paperColor = new(new Color(1.03f, 0.91f, 0.72f, 1f), true, false, true);
    public ColorParameter shadowColor = new(new Color(0.28f, 0.31f, 0.42f, 1f), true, false, true);
    public ColorParameter crimsonColor = new(new Color(0.72f, 0.055f, 0.095f, 1f), true, false, true);

    public bool IsActive() => intensity.value > 0f;
    public bool IsTileCompatible() => false;
}
