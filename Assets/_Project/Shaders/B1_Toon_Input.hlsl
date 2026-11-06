#ifndef B1_TOON_INPUT_INCLUDED
#define B1_TOON_INPUT_INCLUDED

// Custom/B1_Toon のマテリアルプロパティ。Custom/Toon と同じ内容に
// ディゾルブ関連を足したもの。SubShader の HLSLINCLUDE から全パスに取り込む。

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

    float4 _OutlineColor;
    float  _OutlineWidth;
    float  _OutlineDistMin;
    float  _OutlineDistMax;

    float  _OcclusionFade;

    float4 _FlashColor;
    float  _FlashAmount;

    float  _Dissolve;
    float4 _DissolveEdgeColor;
    float4 _DissolveEmberColor;
    float  _DissolveEdge;
    float  _DissolveScale;
    float  _DissolveInvert;
CBUFFER_END

#endif // B1_TOON_INPUT_INCLUDED
