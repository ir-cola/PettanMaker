#ifndef TOON_COMMON_INCLUDED
#define TOON_COMMON_INCLUDED

// Custom/Toon, Custom/B1_Toon, Custom/FaceToon が共有するトゥーン表現の実装。
// ライティング（環境光・追加ライト・スペキュラ）は ToonLighting.hlsl 側。
// こちらは Lighting.hlsl を必要としないため、DepthOnly など軽量パスからも include できる。
//
// 呼び出し側は先に *_Input.hlsl で UnityPerMaterial を宣言しておくこと。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ---------------------------------------------------------------
// カメラオクルージョン用スクリーンディザ
// ---------------------------------------------------------------
void ToonDitherClip(float2 screenPx, float fade)
{
    clip(frac(52.9829189 * frac(dot(screenPx, float2(0.06711056, 0.00583715)))) - fade);
}

// ---------------------------------------------------------------
// トゥーンランプ（影 → 中間 → 明部 の3階調）
// ---------------------------------------------------------------
struct ToonRampParams
{
    float3 shadowMul;   // 影部でアルベドに掛かる係数
    float3 midMul;      // 中間調でアルベドに掛かる係数
    float2 shadowBand;  // x=閾値 y=幅（NdotL に対して）
    float2 midBand;     // x=閾値 y=幅（NdotL に対して）
    float2 attenBand;   // x=閾値 y=幅（shadowAttenuation に対して）
};

// リアルタイム影の減衰を NdotL とは独立に階調化する。
// NdotL に掛け算すると、強く照らされた面ほど影の判定が甘くなり半影が消える。
float ToonAtten(float shadowAttenuation, float2 attenBand)
{
    return smoothstep(attenBand.x - attenBand.y, attenBand.x + attenBand.y, shadowAttenuation);
}

// 0=影 / 1=中間 / 2=明部 の連続バンド値を返す。
float ToonBandLevel(float NdotL, float shadowAttenuation, ToonRampParams p)
{
    float lit = smoothstep(p.shadowBand.x - p.shadowBand.y, p.shadowBand.x + p.shadowBand.y, NdotL);
    float mid = smoothstep(p.midBand.x    - p.midBand.y,    p.midBand.x    + p.midBand.y,    NdotL);
    return (lit + mid) * ToonAtten(shadowAttenuation, p.attenBand);
}

// バンド値 [0,2] をアルベド乗算係数に変換する。
float3 ToonRamp(float level, ToonRampParams p)
{
    return lerp(lerp(p.shadowMul, p.midMul, saturate(level)), 1.0, saturate(level - 1.0));
}

// 2階調版（FaceToon 用）。illuminated は [0,1]。
float3 ToonRamp2(float illuminated, float3 shadowMul)
{
    return lerp(shadowMul, 1.0, saturate(illuminated));
}

// _ShadowColor と _ShadowIntensity から影部の乗算係数を組み立てる。
// _ShadowIntensity が 1 に近いほど影は元の色に戻る（＝影が薄くなる）。
float3 ToonShadowMul(float3 shadowColor, float shadowIntensity)
{
    return lerp(shadowColor, 1.0, shadowIntensity);
}

// 中間調の乗算係数。midColor == shadowColor のとき、旧実装の
// lerp(shadowedColor, litColor, blend) と完全に一致する。
float3 ToonMidMul(float3 midColor, float shadowIntensity, float blend)
{
    return lerp(midColor, 1.0, lerp(shadowIntensity, 1.0, blend));
}

// ---------------------------------------------------------------
// ハッチング
// ---------------------------------------------------------------

// オブジェクト原点をアンカーにしたスクリーン空間 UV。
// アスペクト補正しないと画面比によって線の角度と間隔が変わる。
float2 ToonHatchUV(float3 positionWS, float density)
{
    float3 pivotWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float4 fragCS  = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
    float4 pivotCS = mul(UNITY_MATRIX_VP, float4(pivotWS, 1.0));

    float2 hp = fragCS.xy / fragCS.w - pivotCS.xy / pivotCS.w;
    hp.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
    return hp * density;
}

float ToonHatchPattern(float2 p, float lineWidth, float angle, float sharpness)
{
    float s, c;
    sincos(angle * PI, s, c);
    float u = dot(p, float2(-s, c));
    float d = abs(frac(u) - 0.5);

    // 1px 未満まで細くなった線がちらつかないよう、画面上の変化量で下限を決める。
    // 微分は frac を挟まない u から取る（d だと折り返しの1px だけ線が太る）。
    float blur = max((1.0 - sharpness) * 0.05, max(fwidth(u), 1e-5));
    return smoothstep(lineWidth + blur, lineWidth - blur, d);
}

// 影の強さに応じてハッチングをフェードイン／フェードアウトさせる。
float ToonHatch(float2 p, float shadowStrength, float width, float angle, float sharpness, float2 shadowRange)
{
    float h = ToonHatchPattern(p, width * 0.5, angle, sharpness);
    float mask = smoothstep(shadowRange.x - 0.1, shadowRange.x + 0.1, shadowStrength)
               * smoothstep(shadowRange.y + 0.1, shadowRange.y - 0.1, shadowStrength);
    return saturate(h * mask);
}

float3 ToonApplyHatch(float3 color, float hatchValue, float shadowStrength,
                      float3 hatchColor, float hatchAlpha, float intensity)
{
    float mask = smoothstep(0.1, 0.9, shadowStrength);
    float e = hatchValue * mask * intensity * hatchAlpha;
    color = lerp(color, color * hatchColor, e);
    return color * lerp(1.0, 0.85, e * 0.5);
}

// ---------------------------------------------------------------
// アウトライン（背面押し出し）の頂点オフセット量
// ---------------------------------------------------------------
float ToonOutlineOffset(float4 positionCS, float width, float distMin, float distMax)
{
    float distFade = 1.0 - smoothstep(distMin, distMax, positionCS.w);
    return width * distFade * 2.0 * (1.0 / _ScreenParams.y) * positionCS.w / abs(UNITY_MATRIX_P._m11);
}

#endif // TOON_COMMON_INCLUDED
