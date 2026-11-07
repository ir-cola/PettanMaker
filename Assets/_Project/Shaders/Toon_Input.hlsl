#ifndef TOON_INPUT_INCLUDED
#define TOON_INPUT_INCLUDED

// Custom/Toon のマテリアルプロパティ。SubShader の HLSLINCLUDE から
// 全パスに取り込む。UnityPerMaterial を1箇所にまとめることで、パス間で
// 宣言順がずれて SRP Batcher が黙って無効化される事故を防ぐ。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
TEXTURE2D(_FaceTex);        SAMPLER(sampler_FaceTex);
TEXTURE2D(_FaceNextTex);    SAMPLER(sampler_FaceNextTex);
TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);
TEXTURE2D(_AOMap);          SAMPLER(sampler_AOMap);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _FaceTex_ST;
    float4 _FaceNextTex_ST;
    float4 _BaseColor;
    float  _NormalScale;

    float4 _FaceColor;
    float  _FaceBlend;
    float  _FaceTransition;

    float  _AOIntensity;

    float  _ShadowThreshold;
    float  _ShadowSmooth;
    float4 _ShadowColor;
    float  _ShadowIntensity;
    float  _ShadowAttenThreshold;
    float  _ShadowAttenSmooth;

    float  _MidToneThreshold;
    float  _MidToneSmooth;
    float  _MidToneBlend;
    float4 _MidToneColor;

    float  _AmbientStrength;
    float  _AdditionalLightsIntensity;

    float4 _HatchColor;
    float  _HatchIntensity;
    float  _HatchDensity;
    float  _HatchWidth;
    float  _HatchAngle1;
    float  _HatchSharpness;
    float  _HatchShadowMin;
    float  _HatchShadowMax;

    float4 _RimColor;
    float  _RimPower;
    float  _RimIntensity;
    float  _RimShadowMask;

    float4 _FlashColor;
    float  _FlashAmount;

    float4 _OutlineColor;
    float  _OutlineWidth;
    float  _OutlineDistMin;
    float  _OutlineDistMax;

    float  _OcclusionFade;
CBUFFER_END

#endif // TOON_INPUT_INCLUDED
