Shader "Custom/Toon2"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Normal)]
        [NoScaleOffset] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0, 2)) = 1

        [Header(Toon Shading)]
        _ShadeColor ("Shade Color", Color) = (0.72, 0.72, 0.82, 1)
        _ShadowStep ("Shadow Step", Range(0, 1)) = 0.45
        _ShadowFeather ("Shadow Feather", Range(0.001, 0.25)) = 0.04
        _IndirectIntensity ("Indirect Intensity", Range(0, 1)) = 0.18

        [Header(Fresnel Shadow)]
        _FresnelShadowColor ("Fresnel Shadow Color", Color) = (0.45, 0.46, 0.62, 1)
        _FresnelShadowPower ("Fresnel Shadow Power", Range(0.5, 8)) = 2.2
        _FresnelShadowIntensity ("Fresnel Shadow Intensity", Range(0, 1)) = 0.45
        _FresnelShadowFeather ("Fresnel Shadow Feather", Range(0.001, 0.5)) = 0.18

        [Header(Rim)]
        _RimColor ("Rim Color", Color) = (1, 0.92, 0.75, 1)
        _RimPower ("Rim Power", Range(0.5, 10)) = 4
        _RimIntensity ("Rim Intensity", Range(0, 2)) = 0.35

        [Header(Toon Cast Shadow)]
        _ToonCastShadow ("Toon Cast Shadow", Range(0, 1)) = 1
        _CastShadowStep ("Cast Shadow Step", Range(0, 1)) = 0.45
        _CastShadowFeather ("Cast Shadow Feather", Range(0.001, 0.25)) = 0.04

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.03, 0.025, 0.04, 1)
        _OutlineWidth ("Outline Width", Range(0, 10)) = 1.5
        _OutlineDistMin ("Outline Distance Min", Range(0, 100)) = 0.5
        _OutlineDistMax ("Outline Distance Max", Range(0, 100)) = 30
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _NormalScale;
                half4 _ShadeColor;
                half _ShadowStep;
                half _ShadowFeather;
                half _IndirectIntensity;
                half4 _FresnelShadowColor;
                half _FresnelShadowPower;
                half _FresnelShadowIntensity;
                half _FresnelShadowFeather;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _ToonCastShadow;
                half _CastShadowStep;
                half _CastShadowFeather;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineDistMin;
                half _OutlineDistMax;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fogCoord : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half3 GetNormalWS(Varyings input)
            {
                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half tangentSign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(normalWS, tangentWS) * tangentSign;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalScale);
                return normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                half3 normalWS = GetNormalWS(input);
                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half lightTerm = ndotl * mainLight.shadowAttenuation;
                half toonMask = smoothstep(_ShadowStep - _ShadowFeather, _ShadowStep + _ShadowFeather, lightTerm);

                half3 indirect = SampleSH(normalWS) * _IndirectIntensity;
                half3 litColor = albedo.rgb * mainLight.color;
                half3 shadeColor = albedo.rgb * _ShadeColor.rgb;
                half3 color = lerp(shadeColor, litColor, toonMask) + albedo.rgb * indirect;

                half fresnel = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _FresnelShadowPower);
                half shadowArea = 1.0 - toonMask;
                half fresnelShadow = smoothstep(0.0, _FresnelShadowFeather, fresnel) * shadowArea;
                color = lerp(color, albedo.rgb * _FresnelShadowColor.rgb, fresnelShadow * _FresnelShadowIntensity * _FresnelShadowColor.a);

                half rim = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _RimPower);
                color += _RimColor.rgb * rim * _RimIntensity * _RimColor.a;
                color = MixFog(color, input.fogCoord);

                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _NormalScale;
                half4 _ShadeColor;
                half _ShadowStep;
                half _ShadowFeather;
                half _IndirectIntensity;
                half4 _FresnelShadowColor;
                half _FresnelShadowPower;
                half _FresnelShadowIntensity;
                half _FresnelShadowFeather;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _ToonCastShadow;
                half _CastShadowStep;
                half _CastShadowFeather;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineDistMin;
                half _OutlineDistMax;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(positionWS);

                float viewDistance = max(positionCS.w, 0.001);
                float distanceFade = 1.0 - smoothstep(_OutlineDistMin, _OutlineDistMax, viewDistance);
                float width = _OutlineWidth * distanceFade * (2.0 / _ScreenParams.y) * viewDistance / abs(UNITY_MATRIX_P._m11);
                positionWS += normalWS * width;

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            float3 _LightDirection;
            float3 _LightPosition;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _NormalScale;
                half4 _ShadeColor;
                half _ShadowStep;
                half _ShadowFeather;
                half _IndirectIntensity;
                half4 _FresnelShadowColor;
                half _FresnelShadowPower;
                half _FresnelShadowIntensity;
                half _FresnelShadowFeather;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _ToonCastShadow;
                half _CastShadowStep;
                half _CastShadowFeather;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineDistMin;
                half _OutlineDistMax;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            };

            float3 GetLightDirectionWS(float3 positionWS)
            {
                float3 lightDirectionWS = _LightDirection;

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    lightDirectionWS = normalize(_LightPosition - positionWS);
                #endif

                return lightDirectionWS;
            }

            float4 GetShadowPositionHClip(float3 positionWS, float3 normalWS)
            {
                float3 lightDirectionWS = GetLightDirectionWS(positionWS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = GetShadowPositionHClip(output.positionWS, output.normalWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                half3 normalWS = normalize(input.normalWS);
                half3 lightDirectionWS = normalize(GetLightDirectionWS(input.positionWS));
                half ndotl = saturate(dot(normalWS, lightDirectionWS));
                half toonShadow = 1.0 - smoothstep(_CastShadowStep - _CastShadowFeather, _CastShadowStep + _CastShadowFeather, ndotl);
                clip(lerp(1.0, toonShadow, _ToonCastShadow) - 0.001);

                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _NormalScale;
                half4 _ShadeColor;
                half _ShadowStep;
                half _ShadowFeather;
                half _IndirectIntensity;
                half4 _FresnelShadowColor;
                half _FresnelShadowPower;
                half _FresnelShadowIntensity;
                half _FresnelShadowFeather;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _ToonCastShadow;
                half _CastShadowStep;
                half _CastShadowFeather;
                half4 _OutlineColor;
                half _OutlineWidth;
                half _OutlineDistMin;
                half _OutlineDistMax;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
