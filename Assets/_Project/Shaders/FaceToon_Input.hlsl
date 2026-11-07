#ifndef FACE_TOON_INPUT_INCLUDED
#define FACE_TOON_INPUT_INCLUDED

// Custom/FaceToon のマテリアルプロパティ。SubShader の HLSLINCLUDE から全パスに取り込む。
//
// 注意: _FaceForward / _FaceRight / _FaceUp は ToonFaceLightController が
// MaterialPropertyBlock 経由で毎フレーム設定するため、顔のレンダラーだけは
// SRP Batcher の対象外になる。

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

    float  _FaceShadowBias;
    float  _FaceMinLight;
    float  _FaceNormalFlatten;
    float  _FaceShadowReceive;

    float4 _FaceLightDir;
    float  _FaceLightFollow;
    float4 _FaceForward;
    float4 _FaceRight;
    float4 _FaceUp;

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

#endif // FACE_TOON_INPUT_INCLUDED
