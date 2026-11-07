Shader "_Project/PostEffects/SemiReal"
{
    Properties
    {
        _SemiRealIntensity ("Semi Real Intensity", Range(0, 1)) = 0
        _Clarity ("Clarity", Range(0, 1)) = 0.22
        _SemiRealSharpen ("Sharpen", Range(0, 1)) = 0.16
        _SurfaceSmoothing ("Surface Smoothing", Range(0, 1)) = 0.12
        _DetailRadius ("Detail Radius", Range(0.5, 3)) = 1
        _HighlightRolloff ("Highlight Rolloff", Range(0, 1)) = 0.3
        _ContactAO ("Contact AO", Range(0, 1.5)) = 0.28
        _AORadius ("AO Radius", Range(0.5, 4)) = 1.35
        _AOBias ("AO Bias", Range(0.0001, 0.02)) = 0.002
        _AOColor ("AO Color", Color) = (0.48, 0.52, 0.62, 1)
        _NaturalExposure ("Exposure", Range(0.5, 2)) = 1.02
        _NaturalContrast ("Contrast", Range(0.7, 1.5)) = 1.06
        _NaturalSaturation ("Saturation", Range(0, 2)) = 1.03
        _NaturalShadowTint ("Shadow Tint", Color) = (0.91, 0.95, 1.02, 1)
        _NaturalHighlightTint ("Highlight Tint", Color) = (1.03, 1, 0.95, 1)
        _AerialStrength ("Aerial Strength", Range(0, 1)) = 0.16
        _AerialStart ("Aerial Start", Float) = 75
        _AerialEnd ("Aerial End", Float) = 380
        _AerialColor ("Aerial Color", Color) = (0.66, 0.74, 0.86, 1)
        _NaturalLensDistortion ("Lens Distortion", Range(-0.08, 0.08)) = 0.006
        _NaturalChromaticAberration ("Chromatic Aberration", Range(0, 2)) = 0.25
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
            float4 _BlitTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _SemiRealIntensity, _Clarity, _SemiRealSharpen, _SurfaceSmoothing;
                float _DetailRadius, _HighlightRolloff;
                float _ContactAO, _AORadius, _AOBias;
                float4 _AOColor;
                float _NaturalExposure, _NaturalContrast, _NaturalSaturation;
                float4 _NaturalShadowTint, _NaturalHighlightTint;
                float _AerialStrength, _AerialStart, _AerialEnd;
                float4 _AerialColor;
                float _NaturalLensDistortion, _NaturalChromaticAberration;
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
            float EyeDepth(float2 uv) { return LinearEyeDepth(SampleSceneDepth(saturate(uv)), _ZBufferParams); }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
                if (_SemiRealIntensity <= 0.0001) return original;

                float2 centered = input.uv - 0.5;
                float radius2 = dot(centered, centered);
                float2 uv = saturate(0.5 + centered * (1.0 + radius2 * _NaturalLensDistortion));
                float2 texel = max(_BlitTexture_TexelSize.xy, float2(0.000001, 0.000001));
                float2 offset = texel * _DetailRadius;

                float2 radial = normalize(centered + float2(0.0001, 0.0001));
                float2 chroma = radial * texel * _NaturalChromaticAberration * smoothstep(0.12, 0.72, length(centered));
                float3 center;
                center.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + chroma).r;
                center.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                center.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - chroma).b;

                float3 n = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, offset.y)).rgb;
                float3 s = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, offset.y)).rgb;
                float3 e = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(offset.x, 0)).rgb;
                float3 w = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(offset.x, 0)).rgb;
                float3 average = (center * 2.0 + n + s + e + w) / 6.0;

                float localDifference = abs(Luminance(center) - Luminance(average));
                float flatMask = 1.0 - smoothstep(0.03, 0.22, localDifference);
                float3 color = lerp(center, average, _SurfaceSmoothing * flatMask);
                float3 detail = center - average;
                color += detail * (_Clarity + _SemiRealSharpen * smoothstep(0.015, 0.18, abs(detail)));

                float rawDepth = SampleSceneDepth(uv);
                float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
                #if UNITY_REVERSED_Z
                    float geometryMask = rawDepth > 0.000001 ? 1.0 : 0.0;
                #else
                    float geometryMask = rawDepth < 0.999999 ? 1.0 : 0.0;
                #endif
                float2 aoOffset = texel * _AORadius * 2.0;
                float ao = 0.0;
                ao += saturate((depth - EyeDepth(uv + float2(aoOffset.x, 0)) - _AOBias) / max(_AORadius, 0.001));
                ao += saturate((depth - EyeDepth(uv - float2(aoOffset.x, 0)) - _AOBias) / max(_AORadius, 0.001));
                ao += saturate((depth - EyeDepth(uv + float2(0, aoOffset.y)) - _AOBias) / max(_AORadius, 0.001));
                ao += saturate((depth - EyeDepth(uv - float2(0, aoOffset.y)) - _AOBias) / max(_AORadius, 0.001));
                ao = saturate(ao * 0.25) * _ContactAO * geometryMask;
                color *= lerp(float3(1, 1, 1), _AOColor.rgb, ao);

                color *= _NaturalExposure;
                color = ((color - 0.5) * _NaturalContrast) + 0.5;
                float lum = Luminance(color);
                color = lerp(float3(lum, lum, lum), color, _NaturalSaturation);
                float shadowWeight = 1.0 - smoothstep(0.12, 0.48, lum);
                float highlightWeight = smoothstep(0.55, 0.92, lum);
                color *= lerp(float3(1, 1, 1), _NaturalShadowTint.rgb, shadowWeight * 0.22);
                color *= lerp(float3(1, 1, 1), _NaturalHighlightTint.rgb, highlightWeight * 0.16);

                // 鏡面ハイライトの色を残しながら白飛びだけを圧縮する。
                float3 overbright = max(color - 1.0, 0.0);
                color -= overbright * (_HighlightRolloff * overbright / (1.0 + overbright));

                float aerial = saturate((depth - _AerialStart) / max(_AerialEnd - _AerialStart, 0.001));
                aerial = aerial * aerial * _AerialStrength * geometryMask;
                float aerialLum = Luminance(color);
                color = lerp(color, lerp(float3(aerialLum, aerialLum, aerialLum), _AerialColor.rgb, 0.72), aerial);

                return half4(lerp(original.rgb, max(color, 0.0), _SemiRealIntensity), original.a);
            }
            ENDHLSL
        }
    }
}
