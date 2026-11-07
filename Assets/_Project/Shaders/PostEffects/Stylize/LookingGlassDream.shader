Shader "_Project/PostEffects/LookingGlassDream"
{
    Properties
    {
        _LookingGlassIntensity ("Looking Glass Intensity", Range(0, 1)) = 0
        _Refraction ("Refraction", Range(0, 0.08)) = 0.018
        _Swirl ("Swirl", Range(-0.15, 0.15)) = 0.035
        _DriftSpeed ("Drift Speed", Range(0, 1)) = 0.12
        _ReflectionEcho ("Reflection Echo", Range(0, 0.3)) = 0.07
        _ChromaticShift ("Chromatic Shift", Range(0, 6)) = 1.8
        _SoftFocus ("Soft Focus", Range(0, 1)) = 0.45
        _PrismStrength ("Prism Strength", Range(0, 0.6)) = 0.18
        _DreamVignette ("Dream Vignette", Range(0, 1)) = 0.3
        _CoolTint ("Cool Tint", Color) = (0.70, 0.92, 1.08, 1)
        _WarmTint ("Warm Tint", Color) = (1.08, 0.72, 0.94, 1)
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

            TEXTURE2D_X(_BlitTexture);
            float4 _BlitTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _LookingGlassIntensity;
                float _Refraction;
                float _Swirl;
                float _DriftSpeed;
                float _ReflectionEcho;
                float _ChromaticShift;
                float _SoftFocus;
                float _PrismStrength;
                float _DreamVignette;
                float4 _CoolTint;
                float4 _WarmTint;
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

            float2 Rotate(float2 value, float angle)
            {
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                return float2(
                    value.x * cosine - value.y * sine,
                    value.x * sine + value.y * cosine
                );
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
                if (_LookingGlassIntensity <= 0.0001)
                    return original;

                float aspect = _BlitTexture_TexelSize.z / max(_BlitTexture_TexelSize.w, 1.0);
                float2 centered = input.uv - 0.5;
                centered.x *= aspect;

                float radius = length(centered);
                float radius2 = radius * radius;
                float time = _Time.y * _DriftSpeed;

                // ガラス表面が呼吸するような同心円状の屈折と、中心付近の緩い渦。
                float ripple = sin(radius * 31.0 - time * 1.7)
                             + 0.45 * sin(centered.x * 19.0 + centered.y * 13.0 + time);
                float angle = _Swirl * (1.0 - saturate(radius * 1.4)) * 8.0
                            + ripple * _Refraction * 0.22;
                float2 warped = Rotate(centered, angle);
                warped *= 1.0 + _Refraction * radius2 * 9.0;
                warped += normalize(centered + float2(0.0001, 0.0001))
                        * ripple * _Refraction * (0.18 + radius * 0.5);
                warped.x /= aspect;
                float2 dreamUV = warped + 0.5;

                // プリズム色は中心から放射状にずらし、一般的な横方向の色収差と差別化する。
                float2 radialDirection = normalize(warped + float2(0.0001, 0.0001));
                radialDirection.x /= aspect;
                float edgeMask = smoothstep(0.08, 0.72, radius);
                float2 chromaOffset = radialDirection * _BlitTexture_TexelSize.xy
                                    * _ChromaticShift * (0.35 + edgeMask);

                float3 dreamColor;
                dreamColor.r = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, dreamUV + chromaOffset).r;
                dreamColor.g = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, dreamUV).g;
                dreamColor.b = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, dreamUV - chromaOffset).b;

                // 少数サンプルの対角ブラー。明部だけを強く混ぜて柔らかな夢の光にする。
                float2 blurStep = _BlitTexture_TexelSize.xy * (1.5 + _SoftFocus * 3.5);
                float3 blurColor =
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, dreamUV + blurStep).rgb +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, dreamUV - blurStep).rgb +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, dreamUV + float2(blurStep.x, -blurStep.y)).rgb +
                    SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, dreamUV + float2(-blurStep.x, blurStep.y)).rgb;
                blurColor *= 0.25;
                float highlightMask = smoothstep(0.42, 1.05, Luminance(blurColor));
                dreamColor = lerp(dreamColor, blurColor, _SoftFocus * (0.18 + highlightMask * 0.5));

                // 鏡の端にだけ反転像を薄く混ぜ、現実と鏡像の境界を曖昧にする。
                float2 mirrorUV = float2(1.0 - dreamUV.x, dreamUV.y);
                float3 mirrorColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, mirrorUV).rgb;
                float echoMask = smoothstep(0.42, 0.78, radius);
                dreamColor = lerp(dreamColor, mirrorColor, echoMask * _ReflectionEcho);

                // シアンとマゼンタが入れ替わる、ステンドグラス風の虹色の揺らぎ。
                float prismWave = sin(radius * 24.0 - time * 1.3 + atan2(centered.y, centered.x) * 3.0);
                float prismBlend = prismWave * 0.5 + 0.5;
                float3 prismTint = lerp(_CoolTint.rgb, _WarmTint.rgb, prismBlend);
                float colorMask = _PrismStrength * (0.25 + edgeMask * 0.75)
                                * (0.4 + highlightMask * 0.6);
                dreamColor = lerp(dreamColor, dreamColor * prismTint, colorMask);

                float vignette = 1.0 - smoothstep(0.32, 0.82, radius) * _DreamVignette;
                dreamColor *= vignette;

                return half4(
                    lerp(original.rgb, saturate(dreamColor), _LookingGlassIntensity),
                    original.a
                );
            }
            ENDHLSL
        }
    }
}
