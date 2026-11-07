/**===========================================================================

 File   : 高さ/遠景フォグ (Volume) [HeightFogVolume.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/02
 -----------------------------------------------------------------------------

   HeightFog と遠景(距離)フォグを URP の Volume で一元管理するための
   VolumeComponent。Global Volume の Profile (Stage1_PostProcess) に
   Bloom / Vignette などと並べて追加し、ここ一か所で調整する。
   実際の描画は PostProcessFeature が Fog マテリアル(M_HeightFog)へ
   この値を流し込んでブリットする。

 ===========================================================================**/
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable, VolumeComponentMenu("Custom/Height Fog")]
public class HeightFogVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("フォグ色")]
    public ColorParameter fogColor = new ColorParameter(new Color(0.80f, 0.72f, 0.88f, 1f), true, false, true);

    [Header("Height Fog (高さ)")]
    [Tooltip("この高さより下が霧になる。下げるほど霧が下に沈む")]
    public FloatParameter heightBase = new FloatParameter(-8f);
    [Tooltip("高さ方向の立ち上がりの急さ")]
    public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(0.10f, 0.001f, 1f);
    [Tooltip("高さフォグの濃さ。既定 0 = 効果なし（Volume を置いたシーンだけ上書きで有効化）")]
    public ClampedFloatParameter heightStrength = new ClampedFloatParameter(0f, 0f, 3f);

    [Header("Distance Fog (遠景)")]
    [Tooltip("遠景フォグが始まる距離")]
    public MinFloatParameter distanceStart = new MinFloatParameter(20f, 0f);
    [Tooltip("遠景フォグが最大になる距離")]
    public MinFloatParameter distanceEnd = new MinFloatParameter(85f, 0f);
    [Tooltip("遠景フォグの濃さ。既定 0 = 効果なし（Volume を置いたシーンだけ上書きで有効化）")]
    public ClampedFloatParameter distanceStrength = new ClampedFloatParameter(0f, 0f, 2f);

    [Header("Volumetric (光の散乱)")]
    public ClampedFloatParameter volumetricScattering = new ClampedFloatParameter(0.35f, 0f, 2f);
    public ClampedFloatParameter volumetricAnisotropy = new ClampedFloatParameter(0.55f, 0f, 0.95f);

    // enabled フラグには依存させない（握り込みで効かなくなる事故を防ぐ）。
    // 全体OFFにしたい時は Profile 上でこのオーバーライド自体のチェックを外すか、強度を 0 にする。
    public bool IsActive() => heightStrength.value > 0f || distanceStrength.value > 0f;
    public bool IsTileCompatible() => false;
}
