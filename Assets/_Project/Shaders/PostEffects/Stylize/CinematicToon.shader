Shader "_Project/PostEffects/CinematicToon"
{
    Properties
    {
        _CinematicToonIntensity ("Cinematic Toon Intensity", Range(0, 1)) = 0
        _SurfaceSmoothing ("Surface Smoothing", Range(0, 1)) = 0.48
        _SmoothingRadius ("Smoothing Radius", Range(0.5, 3)) = 1.35
        _ToneBands ("Tone Bands", Range(3, 12)) = 6
        _BandSoftness ("Band Softness", Range(0.02, 0.48)) = 0.18
        _OutlineStrength ("Outline Strength", Range(0, 1.5)) = 0.52
        _DepthSensitivity ("Depth Sensitivity", Range(0.1, 5)) = 1.7
        _ColorSensitivity ("Color Sensitivity", Range(0.1, 4)) = 1.25
        _OutlineWidth ("Outline Width", Range(0.5, 3)) = 1.15
        _LocalContrast ("Local Contrast", Range(0, 0.8)) = 0.22
        _HighlightGlow ("Highlight Glow", Range(0, 1)) = 0.24
        _GlowThreshold ("Glow Threshold", Range(0.2, 1.5)) = 0.72
        _ToonSaturation ("Toon Saturation", Range(0, 2)) = 1.08
        _ToonOutlineColor ("Outline Color", Color) = (0.035, 0.045, 0.075, 1)
        _ToonShadowTint ("Shadow Tint", Color) = (0.72, 0.78, 0.96, 1)
        _ToonHighlightTint ("Highlight Tint", Color) = (1.06, 0.98, 0.88, 1)
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
                float _CinematicToonIntensity;
                float _SurfaceSmoothing;
                float _SmoothingRadius;
                float _ToneBands;
                float _BandSoftness;
                float _OutlineStrength;
                float _DepthSensitivity;
                float _ColorSensitivity;
                float _OutlineWidth;
                float _LocalContrast;
                float _HighlightGlow;
                float _GlowThreshold;
                float _ToonSaturation;
                float4 _ToonOutlineColor;
                float4 _ToonShadowTint;
                float4 _ToonHighlightTint;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv.y = 1.0 - output.uv.y;
                return output;
            }

            float Luminance(float3 color)
            {
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            float SoftBand(float value, float bands, float softness)
            {
                float scaled = saturate(value) * bands;
                float baseBand = floor(scaled);
                float fractionInBand = frac(scaled);
                float transition = smoothstep(
                    0.5 - softness,
                    0.5 + softness,
                    fractionInBand
                );
                return saturate((baseBand + transition) / bands);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
                if (_CinematicToonIntensity <= 0.0001)
                    return original;

                float2 texel = max(_BlitTexture_TexelSize.xy, float2(0.000001, 0.000001));
                float2 smoothOffset = texel * _SmoothingRadius;
                float2 outlineOffset = texel * _OutlineWidth;

                float3 center = original.rgb;
                float centerLum = Luminance(center);
                float centerDepth = EyeDepth(input.uv);

                float2 smoothOffsets[8] =
                {
                    float2( smoothOffset.x, 0),
                    float2(-smoothOffset.x, 0),
                    float2(0,  smoothOffset.y),
                    float2(0, -smoothOffset.y),
                    float2( smoothOffset.x,  smoothOffset.y),
                    float2(-smoothOffset.x,  smoothOffset.y),
                    float2( smoothOffset.x, -smoothOffset.y),
                    float2(-smoothOffset.x, -smoothOffset.y)
                };

                // 色差と深度差の両方を重みにする、小型のエッジ保持フィルタ。
                float3 smoothSum = center;
                float weightSum = 1.0;
                float3 broadAverage = center;

                [unroll]
                for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++)
                {
                    float2 sampleUV = input.uv + smoothOffsets[sampleIndex];
                    float3 sampleColor = SAMPLE_TEXTURE2D_X(
                        _BlitTexture, sampler_LinearClamp, sampleUV).rgb;
                    float sampleLum = Luminance(sampleColor);
                    float sampleDepth = EyeDepth(sampleUV);

                    float colorDifference = abs(sampleLum - centerLum);
                    float relativeDepthDifference = abs(sampleDepth - centerDepth)
                                                  / max(centerDepth, 0.25);
                    float weight = exp(-colorDifference * 10.0)
                                 * exp(-relativeDepthDifference * 42.0);

                    smoothSum += sampleColor * weight;
                    weightSum += weight;
                    broadAverage += sampleColor;
                }

                broadAverage /= 9.0;
                float3 edgePreserved = smoothSum / max(weightSum, 0.001);
                float3 surfaceColor = lerp(center, edgePreserved, _SurfaceSmoothing);

                // 元の色相を維持したまま輝度だけを柔らかなトゥーン段階へ整理する。
                float surfaceLum = max(Luminance(surfaceColor), 0.0001);
                float bandedLum = SoftBand(surfaceLum, max(_ToneBands, 3.0), _BandSoftness);
                float3 toonColor = surfaceColor * (bandedLum / surfaceLum);

                float shadowWeight = 1.0 - smoothstep(0.18, 0.58, bandedLum);
                float highlightWeight = smoothstep(0.58, 0.92, bandedLum);
                float3 gradeTint = lerp(
                    float3(1.0, 1.0, 1.0),
                    _ToonShadowTint.rgb,
                    shadowWeight * 0.38
                );
                gradeTint = lerp(gradeTint, _ToonHighlightTint.rgb, highlightWeight * 0.28);
                toonColor *= gradeTint;

                // 面を均した後に細部だけを戻し、低解像度なフィルター感を避ける。
                float3 localDetail = center - broadAverage;
                toonColor += localDetail * _LocalContrast;

                float toonLum = Luminance(toonColor);
                toonColor = lerp(
                    float3(toonLum, toonLum, toonLum),
                    toonColor,
                    _ToonSaturation
                );

                // 色輪郭と相対深度輪郭を統合。距離が変わっても線幅が暴れにくい。
                float lumLeft = Luminance(SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, input.uv - float2(outlineOffset.x, 0)).rgb);
                float lumRight = Luminance(SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, input.uv + float2(outlineOffset.x, 0)).rgb);
                float lumDown = Luminance(SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, input.uv - float2(0, outlineOffset.y)).rgb);
                float lumUp = Luminance(SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, input.uv + float2(0, outlineOffset.y)).rgb);

                float colorEdge = length(float2(lumRight - lumLeft, lumUp - lumDown))
                                * _ColorSensitivity;

                float depthLeft = EyeDepth(input.uv - float2(outlineOffset.x, 0));
                float depthRight = EyeDepth(input.uv + float2(outlineOffset.x, 0));
                float depthDown = EyeDepth(input.uv - float2(0, outlineOffset.y));
                float depthUp = EyeDepth(input.uv + float2(0, outlineOffset.y));
                float2 depthGradient = float2(depthRight - depthLeft, depthUp - depthDown)
                                     / max(centerDepth, 0.25);
                float depthEdge = length(depthGradient) * _DepthSensitivity;

                float outline = smoothstep(0.035, 0.22, max(colorEdge, depthEdge))
                              * _OutlineStrength;
                toonColor = lerp(toonColor, _ToonOutlineColor.rgb, saturate(outline));

                // 明部周辺のみを加算し、ブルームに頼り過ぎない制御されたグローにする。
                float3 glowSource = max(broadAverage - _GlowThreshold, 0.0);
                float glowMask = smoothstep(
                    _GlowThreshold,
                    _GlowThreshold + 0.35,
                    Luminance(broadAverage)
                );
                toonColor += glowSource * _ToonHighlightTint.rgb
                           * glowMask * _HighlightGlow;

                // HDR の余韻を残しつつ、過飽和だけを穏やかに圧縮。
                toonColor = max(toonColor, 0.0);
                toonColor = toonColor / (1.0 + max(toonColor - 1.0, 0.0) * 0.35);

                return half4(
                    lerp(original.rgb, toonColor, _CinematicToonIntensity),
                    original.a
                );
            }
            ENDHLSL
        }
    }
}
