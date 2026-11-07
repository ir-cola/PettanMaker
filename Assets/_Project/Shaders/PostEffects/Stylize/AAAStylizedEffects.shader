Shader "_Project/PostEffects/AAAStylizedEffects"
{
    Properties
    {
        _AAAEffectsIntensity ("Effects Intensity", Range(0, 1)) = 0
        _HalftoneStrength ("Halftone Strength", Range(0, 1)) = 0.12
        _HalftoneScale ("Halftone Scale", Range(2, 16)) = 5.5
        _HalftoneColor ("Halftone Color", Color) = (0.055, 0.07, 0.13, 1)
        _WatercolorStrength ("Watercolor Strength", Range(0, 1)) = 0.2
        _BleedRadius ("Bleed Radius", Range(0.5, 4)) = 1.4
        _PigmentVariation ("Pigment Variation", Range(0, 0.2)) = 0.035
        _HeatHazeStrength ("Heat Haze Strength", Range(0, 1)) = 0.08
        _HeatHazeScale ("Heat Haze Scale", Range(1, 20)) = 7
        _HeatHazeSpeed ("Heat Haze Speed", Range(0, 2)) = 0.35
        _HeatHazeMask ("Heat Haze Mask", Range(0, 1)) = 0.35
        _LookupTex ("Lookup LUT (2D Strip)", 2D) = "white" {}
        _UseLookupTex ("Use Lookup Texture", Float) = 0
        _LookupStrength ("Lookup Strength", Range(0, 1)) = 0.3
        _LookupSize ("Lookup Size", Range(8, 64)) = 32
        _LookupShadow ("Lookup Shadow", Color) = (0.75, 0.82, 1.05, 1)
        _LookupMid ("Lookup Mid", Color) = (0.98, 0.96, 1.02, 1)
        _LookupHighlight ("Lookup Highlight", Color) = (1.08, 0.96, 0.82, 1)
        _ContactShadowStrength ("Contact Shadow Strength", Range(0, 1.5)) = 0.34
        _ContactShadowRadius ("Contact Shadow Radius", Range(0.5, 4)) = 1.5
        _ContactShadowBias ("Contact Shadow Bias", Range(0.0001, 0.02)) = 0.002
        _ContactShadowColor ("Contact Shadow Color", Color) = (0.45, 0.5, 0.7, 1)
        _BlurStrength ("Blur Strength", Range(0, 1)) = 0.1
        _BlurRadius ("Blur Radius", Range(0.5, 4)) = 1.25
        _KuwaharaStrength ("Kuwahara Strength", Range(0, 1)) = 0.22
        _KuwaharaRadius ("Kuwahara Radius", Range(0.75, 4)) = 1.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "PostEffect"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_BlitTexture);
            TEXTURE2D(_LookupTex);
            float4 _BlitTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _AAAEffectsIntensity, _HalftoneStrength, _HalftoneScale;
                float4 _HalftoneColor;
                float _WatercolorStrength, _BleedRadius, _PigmentVariation;
                float _HeatHazeStrength, _HeatHazeScale, _HeatHazeSpeed, _HeatHazeMask;
                float _UseLookupTex, _LookupStrength, _LookupSize;
                float4 _LookupShadow, _LookupMid, _LookupHighlight;
                float _ContactShadowStrength, _ContactShadowRadius, _ContactShadowBias;
                float4 _ContactShadowColor;
                float _BlurStrength, _BlurRadius;
                float _KuwaharaStrength, _KuwaharaRadius;
            CBUFFER_END

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv.y = 1.0 - output.uv.y;
                return output;
            }

            float Luminance(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(saturate(uv)), _ZBufferParams);
            }
            float3 SampleLut(float3 color)
            {
                float size = max(_LookupSize, 8.0);
                float3 c = saturate(color) * (size - 1.0);
                float low = floor(c.b);
                float high = min(low + 1.0, size - 1.0);
                float2 uvLow = float2((c.r + low * size + 0.5) / (size * size), (c.g + 0.5) / size);
                float2 uvHigh = float2((c.r + high * size + 0.5) / (size * size), uvLow.y);
                float3 a = SAMPLE_TEXTURE2D(_LookupTex, sampler_LinearClamp, uvLow).rgb;
                float3 b = SAMPLE_TEXTURE2D(_LookupTex, sampler_LinearClamp, uvHigh).rgb;
                return lerp(a, b, frac(c.b));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
                if (_AAAEffectsIntensity <= 0.0001) return original;

                float2 texel = max(_BlitTexture_TexelSize.xy, float2(0.000001, 0.000001));
                float2 pixel = input.uv / texel;
                float sourceLum = Luminance(original.rgb);

                float time = _Time.y * _HeatHazeSpeed;
                float2 wave = float2(
                    sin(input.uv.y * _HeatHazeScale * 9.0 + time * 2.1) +
                    sin(input.uv.x * _HeatHazeScale * 5.0 - time * 1.3),
                    cos(input.uv.x * _HeatHazeScale * 8.0 + time * 1.7) +
                    cos(input.uv.y * _HeatHazeScale * 4.0 - time)
                );
                float brightMask = smoothstep(0.58, 1.1, sourceLum);
                float lowerMask = smoothstep(0.35, 1.0, 1.0 - input.uv.y);
                float hazeMask = lerp(brightMask, max(brightMask, lowerMask), _HeatHazeMask);
                float2 baseUV = saturate(input.uv + wave * texel * _HeatHazeStrength * hazeMask * 1.8);

                float3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV).rgb;
                float centerLum = Luminance(center);
                float2 blurOffset = texel * _BlurRadius;
                float3 n = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + float2(0, blurOffset.y)).rgb;
                float3 s = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV - float2(0, blurOffset.y)).rgb;
                float3 e = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + float2(blurOffset.x, 0)).rgb;
                float3 w = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV - float2(blurOffset.x, 0)).rgb;
                float3 soft = (center * 2.0 + n + s + e + w) / 6.0;
                float difference = abs(Luminance(n) - centerLum) + abs(Luminance(s) - centerLum)
                                 + abs(Luminance(e) - centerLum) + abs(Luminance(w) - centerLum);
                float flatMask = 1.0 - smoothstep(0.08, 0.42, difference);
                float3 color = lerp(center, soft, _BlurStrength * flatMask);

                // 4象限のうち輝度分散が最も小さい領域を採用する簡易Kuwahara。
                // 強度0では追加サンプルを実行しない。
                if (_KuwaharaStrength > 0.0001)
                {
                    float2 k = texel * _KuwaharaRadius;
                    float3 kn = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + float2(0, k.y)).rgb;
                    float3 ks = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV - float2(0, k.y)).rgb;
                    float3 ke = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + float2(k.x, 0)).rgb;
                    float3 kw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV - float2(k.x, 0)).rgb;
                    float3 kne = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + k).rgb;
                    float3 knw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + float2(-k.x, k.y)).rgb;
                    float3 kse = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + float2(k.x, -k.y)).rgb;
                    float3 ksw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV - k).rgb;

                    float4 q0Lum = float4(centerLum, Luminance(kn), Luminance(ke), Luminance(kne));
                    float4 q1Lum = float4(centerLum, Luminance(kn), Luminance(kw), Luminance(knw));
                    float4 q2Lum = float4(centerLum, Luminance(ks), Luminance(ke), Luminance(kse));
                    float4 q3Lum = float4(centerLum, Luminance(ks), Luminance(kw), Luminance(ksw));
                    float4 quarter = float4(0.25, 0.25, 0.25, 0.25);
                    float mean0 = dot(q0Lum, quarter);
                    float mean1 = dot(q1Lum, quarter);
                    float mean2 = dot(q2Lum, quarter);
                    float mean3 = dot(q3Lum, quarter);
                    float variance0 = dot((q0Lum - mean0) * (q0Lum - mean0), quarter);
                    float variance1 = dot((q1Lum - mean1) * (q1Lum - mean1), quarter);
                    float variance2 = dot((q2Lum - mean2) * (q2Lum - mean2), quarter);
                    float variance3 = dot((q3Lum - mean3) * (q3Lum - mean3), quarter);

                    float3 selectedMean = (center + kn + ke + kne) * 0.25;
                    float selectedVariance = variance0;
                    if (variance1 < selectedVariance)
                    {
                        selectedVariance = variance1;
                        selectedMean = (center + kn + kw + knw) * 0.25;
                    }
                    if (variance2 < selectedVariance)
                    {
                        selectedVariance = variance2;
                        selectedMean = (center + ks + ke + kse) * 0.25;
                    }
                    if (variance3 < selectedVariance)
                        selectedMean = (center + ks + kw + ksw) * 0.25;

                    // 強い輪郭付近ではブレンドを少し抑え、線の鮮明さを保つ。
                    float edgeProtection = 1.0 - smoothstep(0.12, 0.48, difference);
                    color = lerp(color, selectedMean, _KuwaharaStrength * (0.35 + edgeProtection * 0.65));
                }

                float2 bleedOffset = texel * _BleedRadius;
                float3 bleedA = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV + float2(bleedOffset.x, bleedOffset.y * 0.45)).rgb;
                float3 bleedB = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, baseUV - float2(bleedOffset.x, bleedOffset.y * 0.45)).rgb;
                float bleedMask = smoothstep(0.025, 0.3, abs(Luminance(bleedA) - Luminance(bleedB)));
                color = lerp(color, (bleedA + bleedB + soft) / 3.0, bleedMask * _WatercolorStrength * 0.55);
                color += (Hash21(floor(pixel / 3.0)) - 0.5) * _PigmentVariation * (0.45 + centerLum * 0.55);

                float centerDepth = EyeDepth(baseUV);
                float2 aoOffset = texel * _ContactShadowRadius * 2.0;
                float ao = 0.0;
                ao += saturate((centerDepth - EyeDepth(baseUV + float2(0, aoOffset.y)) - _ContactShadowBias) / max(_ContactShadowRadius, 0.001));
                ao += saturate((centerDepth - EyeDepth(baseUV - float2(0, aoOffset.y)) - _ContactShadowBias) / max(_ContactShadowRadius, 0.001));
                ao += saturate((centerDepth - EyeDepth(baseUV + float2(aoOffset.x, 0)) - _ContactShadowBias) / max(_ContactShadowRadius, 0.001));
                ao += saturate((centerDepth - EyeDepth(baseUV - float2(aoOffset.x, 0)) - _ContactShadowBias) / max(_ContactShadowRadius, 0.001));
                ao = saturate(ao * 0.25) * _ContactShadowStrength;
                color *= lerp(float3(1, 1, 1), _ContactShadowColor.rgb, ao);

                float2 dotCell = frac(pixel / max(_HalftoneScale, 2.0)) - 0.5;
                float shadowMask = 1.0 - smoothstep(0.18, 0.68, Luminance(color));
                float dotRadius = lerp(0.12, 0.46, shadowMask);
                float dotMask = 1.0 - smoothstep(dotRadius - 0.055, dotRadius + 0.055, length(dotCell));
                color = lerp(color, _HalftoneColor.rgb, dotMask * shadowMask * _HalftoneStrength);

                float lum = Luminance(color);
                float shadowWeight = 1.0 - smoothstep(0.15, 0.48, lum);
                float highlightWeight = smoothstep(0.52, 0.9, lum);
                float midWeight = saturate(1.0 - shadowWeight - highlightWeight);
                float3 lookup = color * (_LookupShadow.rgb * shadowWeight +
                    _LookupMid.rgb * midWeight + _LookupHighlight.rgb * highlightWeight);
                if (_UseLookupTex > 0.5) lookup = SampleLut(color);
                color = lerp(color, lookup, _LookupStrength);

                return half4(lerp(original.rgb, max(color, 0.0), _AAAEffectsIntensity), original.a);
            }
            ENDHLSL
        }
    }
}
