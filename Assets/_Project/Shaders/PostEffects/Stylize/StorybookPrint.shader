Shader "_Project/PostEffects/StorybookPrint"
{
    Properties
    {
        _StorybookIntensity ("Storybook Intensity", Range(0, 1)) = 0
        _ToneSteps ("Tone Steps", Range(3, 12)) = 6
        _ColorRetention ("Color Retention", Range(0, 1)) = 0.42
        _InkStrength ("Ink Strength", Range(0, 1.5)) = 0.62
        _EdgeThreshold ("Edge Threshold", Range(0.01, 0.5)) = 0.105
        _HatchStrength ("Hatch Strength", Range(0, 1)) = 0.34
        _PrintOffset ("Print Offset", Range(0, 3)) = 0.7
        _PaperGrain ("Paper Grain", Range(0, 0.25)) = 0.07
        _CrimsonAccent ("Crimson Accent", Range(0, 1)) = 0.55
        _InkColor ("Ink Color", Color) = (0.095, 0.07, 0.12, 1)
        _PaperColor ("Paper Color", Color) = (1.03, 0.91, 0.72, 1)
        _ShadowColor ("Shadow Color", Color) = (0.28, 0.31, 0.42, 1)
        _CrimsonColor ("Crimson Color", Color) = (0.72, 0.055, 0.095, 1)
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
                float _StorybookIntensity;
                float _ToneSteps;
                float _ColorRetention;
                float _InkStrength;
                float _EdgeThreshold;
                float _HatchStrength;
                float _PrintOffset;
                float _PaperGrain;
                float _CrimsonAccent;
                float4 _InkColor;
                float4 _PaperColor;
                float4 _ShadowColor;
                float4 _CrimsonColor;
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

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
                if (_StorybookIntensity <= 0.0001)
                    return original;

                float2 texel = max(_BlitTexture_TexelSize.xy, float2(0.000001, 0.000001));
                float2 pixelPosition = input.uv / texel;

                float3 center = original.rgb;
                float3 north = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv + float2(0, texel.y)).rgb;
                float3 south = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv - float2(0, texel.y)).rgb;
                float3 east = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv + float2(texel.x, 0)).rgb;
                float3 west = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv - float2(texel.x, 0)).rgb;
                float3 northEast = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv + texel).rgb;
                float3 northWest = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv + float2(-texel.x, texel.y)).rgb;
                float3 southEast = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv + float2(texel.x, -texel.y)).rgb;
                float3 southWest = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv - texel).rgb;

                float centerLum = Luminance(center);
                float northLum = Luminance(north);
                float southLum = Luminance(south);
                float eastLum = Luminance(east);
                float westLum = Luminance(west);
                float northEastLum = Luminance(northEast);
                float northWestLum = Luminance(northWest);
                float southEastLum = Luminance(southEast);
                float southWestLum = Luminance(southWest);

                float edgeX = northEastLum + 2.0 * eastLum + southEastLum
                            - northWestLum - 2.0 * westLum - southWestLum;
                float edgeY = northWestLum + 2.0 * northLum + northEastLum
                            - southWestLum - 2.0 * southLum - southEastLum;
                float edge = smoothstep(
                    _EdgeThreshold,
                    _EdgeThreshold + 0.24,
                    length(float2(edgeX, edgeY))
                );

                // 版画らしく階調を均等なインク段階に分ける。
                float steps = max(_ToneSteps, 3.0);
                float quantizedLum = floor(saturate(centerLum) * steps + 0.5) / steps;
                float3 limitedPalette = lerp(_ShadowColor.rgb, _PaperColor.rgb, quantizedLum);

                // 元映像の色相を少しだけ残し、キャラクターの識別性を保つ。
                float3 normalizedColor = center / max(centerLum, 0.08);
                float3 retainedColor = limitedPalette * lerp(
                    float3(1.0, 1.0, 1.0),
                    normalizedColor,
                    _ColorRetention
                );
                float3 printColor = retainedColor;

                // 赤い領域だけをトランプのクリムゾン印刷として強調する。
                float redDominance = center.r - max(center.g, center.b);
                float saturationMask = max(center.r, max(center.g, center.b))
                                     - min(center.r, min(center.g, center.b));
                float crimsonMask = smoothstep(0.04, 0.28, redDominance)
                                  * smoothstep(0.08, 0.4, saturationMask)
                                  * _CrimsonAccent;
                printColor = lerp(printColor, _CrimsonColor.rgb * (0.65 + quantizedLum * 0.5), crimsonMask);

                // アナログ印刷の版ずれを、赤版と青版の微細なズレとして加える。
                float2 registrationOffset = texel * _PrintOffset;
                float shiftedRed = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, input.uv + registrationOffset).r;
                float shiftedBlue = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, input.uv - registrationOffset).b;
                float3 misregistration = float3(shiftedRed, center.g, shiftedBlue);
                printColor = lerp(printColor, printColor * (0.82 + misregistration * 0.22), 0.55);

                // 暗さに応じて斜線を一本、さらに暗い場所には交差線を追加する。
                float shadowMask = 1.0 - smoothstep(0.18, 0.72, centerLum);
                float deepShadowMask = 1.0 - smoothstep(0.08, 0.42, centerLum);
                float hatchA = 1.0 - smoothstep(0.12, 0.38, abs(frac((pixelPosition.x + pixelPosition.y) / 6.0) - 0.5));
                float hatchB = 1.0 - smoothstep(0.1, 0.35, abs(frac((pixelPosition.x - pixelPosition.y) / 8.0) - 0.5));
                float hatch = saturate(hatchA * shadowMask + hatchB * deepShadowMask);
                printColor = lerp(printColor, _InkColor.rgb, hatch * _HatchStrength);

                float inkMask = saturate(edge * _InkStrength);
                printColor = lerp(printColor, _InkColor.rgb, inkMask);

                // 紙の繊維とインク濃度のムラ。
                float fineNoise = Hash21(floor(pixelPosition)) - 0.5;
                float fiberNoise = Hash21(floor(pixelPosition / float2(3.0, 11.0))) - 0.5;
                float grain = fineNoise * 0.65 + fiberNoise * 0.35;
                printColor += grain * _PaperGrain * (0.5 + quantizedLum * 0.5);

                // 古い本の紙端のような、ごく薄い褐色の周辺落ち。
                float2 centeredUV = input.uv * 2.0 - 1.0;
                float border = smoothstep(0.58, 1.18, dot(centeredUV, centeredUV));
                printColor *= lerp(1.0, 0.84, border);

                return half4(
                    lerp(original.rgb, saturate(printColor), _StorybookIntensity),
                    original.a
                );
            }
            ENDHLSL
        }
    }
}
