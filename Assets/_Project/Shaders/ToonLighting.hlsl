#ifndef TOON_LIGHTING_INCLUDED
#define TOON_LIGHTING_INCLUDED

// Custom/Toon 系シェーダーのライティング。カラーパスからのみ include する
// （Lighting.hlsl を引き込むのでコンパイル時間が重い）。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "ToonCommon.hlsl"

// ---------------------------------------------------------------
// 環境光
// ---------------------------------------------------------------
// EVALUATE_SH_* キーワードを宣言していないため、SampleSHPixel は常に
// ピクセル単位で Ambient Probe を評価する。AO はここにだけ掛ける
// （AO をアルベドに直接掛けると、直接光まで暗くなってしまう）。
float3 ToonAmbient(float3 normalWS, float strength, float ao)
{
    return SampleSHPixel(half3(0.0, 0.0, 0.0), normalWS) * strength * ao;
}

// ---------------------------------------------------------------
// 追加ライト（ポイント / スポット / 追加ディレクショナル）
// ---------------------------------------------------------------
// 追加ライトは影色を持ち込まない。ライトごとに shadowMul を足すと、
// 光源が増えるほど影が明るくなってしまうため、明部の寄与だけを加算する。
float3 ToonPunctual(Light light, float3 N, ToonRampParams rp)
{
    float level   = ToonBandLevel(dot(N, light.direction), light.shadowAttenuation, rp);
    float diffuse = saturate(level * 0.5);
    float atten   = saturate(light.distanceAttenuation);

    return diffuse * light.color * atten;
}

float3 ToonAdditionalLights(float3 positionWS, float4 positionCS, float3 N, ToonRampParams rp)
{
    float3 acc = 0.0;

#ifdef _ADDITIONAL_LIGHTS
    // LIGHT_LOOP_BEGIN は inputData.positionWS / normalizedScreenSpaceUV を参照する
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

    // ライトマップを使わないので shadowMask は素通し。
    // shadowMask を渡す3引数版でないと shadowAttenuation が 1 固定になり、
    // _ADDITIONAL_LIGHT_SHADOWS が効かない。
    half4 shadowMask = half4(1.0, 1.0, 1.0, 1.0);

    uint lightCount = GetAdditionalLightsCount();

#if USE_CLUSTER_LIGHT_LOOP
    // Forward+ ではクラスタに追加ディレクショナルライトが入らないので別ループで回す
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        acc += ToonPunctual(GetAdditionalLight(lightIndex, positionWS, shadowMask), N, rp);
    }
#endif

    LIGHT_LOOP_BEGIN(lightCount)
        acc += ToonPunctual(GetAdditionalLight(lightIndex, positionWS, shadowMask), N, rp);
    LIGHT_LOOP_END
#endif

    return acc;
}

// ---------------------------------------------------------------
// メインライトの取得
// ---------------------------------------------------------------
// フラグメントの positionCS は既にピクセル座標なので、スクリーンスペースシャドウでは
// ComputeScreenPos ではなく GetNormalizedScreenSpaceUV を使う必要がある。
Light ToonGetMainLight(float3 positionWS, float4 positionCS)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = float4(GetNormalizedScreenSpaceUV(positionCS), 0.0, 1.0);
#else
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
#endif
    return GetMainLight(shadowCoord);
}

// ---------------------------------------------------------------
// リムライト
// ---------------------------------------------------------------
// shadowMask=0 で影に入ると消え、1 で影の中でも光る。
float3 ToonRim(float3 N, float3 V, float illuminated, float3 color, float power, float intensity, float shadowMask)
{
    float rim = pow(1.0 - saturate(dot(N, V)), power);
    rim *= saturate(illuminated + shadowMask);
    return color * (rim * intensity);
}

#endif // TOON_LIGHTING_INCLUDED
