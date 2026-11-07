Shader "Custom/Toon"
{
    Properties
    {
        _MainTex         ("Main Texture",    2D)           = "white" {}
        _BaseColor       ("Base Color",      Color)        = (1,1,1,1)
        _NormalMap       ("Normal Map",      2D)           = "bump" {}
        _NormalScale     ("Normal Scale",    Range(0,2))   = 1

        [Header(Ambient Occlusion)]
        // 環境光にのみ掛かる（直接光は AO で暗くしない）。
        _AOMap       ("AO Map",       2D)          = "white" {}
        _AOIntensity ("AO Intensity", Range(0,1))  = 1

        [Header(Face Overlay)]
        _FaceTex         ("Face Texture",      2D)          = "black" {}
        _FaceNextTex     ("Face Next Texture", 2D)          = "black" {}
        _FaceColor       ("Face Color",        Color)       = (1,1,1,1)
        _FaceBlend       ("Face Blend",        Range(0,1))  = 0
        _FaceTransition  ("Face Transition",   Range(0,1))  = 0

        [Header(Shading)]
        _ShadowThreshold ("Shadow Threshold", Range(0,1))   = 0.15
        _ShadowSmooth    ("Shadow Smooth",    Range(0,0.1)) = 0.02
        _ShadowColor     ("Shadow Color",     Color)        = (0.85,0.85,0.9,1)
        _ShadowIntensity ("Shadow Intensity", Range(0,1))   = 0.85
        // リアルタイム影の減衰は NdotL とは別に階調化する。
        _ShadowAttenThreshold ("Shadow Atten Threshold", Range(0,1))     = 0.1
        _ShadowAttenSmooth    ("Shadow Atten Smooth",    Range(0.001,0.5)) = 0.1

        [Header(MidTone)]
        _MidToneThreshold ("Mid-Tone Threshold", Range(0,1))   = 0.7
        _MidToneSmooth    ("Mid-Tone Smooth",    Range(0,0.2)) = 0.02
        _MidToneBlend     ("Mid-Tone Blend",     Range(0,1))   = 0.6
        _MidToneColor     ("Mid-Tone Color",     Color)        = (0.85,0.85,0.9,1)

        [Header(Indirect and Additional Lights)]
        _AmbientStrength           ("Ambient Strength",  Range(0,2)) = 1
        _AdditionalLightsIntensity ("Additional Lights", Range(0,2)) = 1

        [Header(Hatch Primary)]
        _HatchColor      ("Hatch Color",      Color)         = (0,0,0,1)
        _HatchIntensity  ("Hatch Intensity",  Range(0,1))    = 0.8
        _HatchDensity    ("Hatch Density",    Range(1,300))  = 25
        _HatchWidth      ("Hatch Width",      Range(0.01,1)) = 0.4
        _HatchAngle1     ("Hatch Angle 1",    Range(0,1))    = 0.2
        _HatchSharpness  ("Hatch Sharpness",  Range(0,1))    = 0.92
        _HatchShadowMin  ("Hatch Shadow Min", Range(0,1))    = 0.1
        _HatchShadowMax  ("Hatch Shadow Max", Range(0,1))    = 0.95

        [Header(Rim Light)]
        _RimColor       ("Rim Color",       Color)      = (1,0.9,0.7,1)
        _RimPower       ("Rim Power",       Range(1,10))= 4
        _RimIntensity   ("Rim Intensity",   Range(0,2)) = 0.8
        // 0=影で消える / 1=影の中でも光る
        _RimShadowMask  ("Rim Shadow Mask", Range(0,1)) = 0.5

        [Header(Camera Occlusion)]
        _OcclusionFade   ("Occlusion Fade",   Range(0,1)) = 0

        [Header(Toon Flash)]
        [HDR] _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount (0=Off 1=Full)", Range(0,1)) = 0

        [Header(Outline)]
        _OutlineColor            ("Outline Color",     Color)         = (0,0,0,1)
        _OutlineWidth            ("Outline Width",     Range(0,10))   = 2.0
        _OutlineDistMin          ("Outline Dist Min",  Range(0,100))  = 0.5
        _OutlineDistMax          ("Outline Dist Max",  Range(0,100))  = 20
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry+1"
        }

        HLSLINCLUDE
        #include "Toon_Input.hlsl"
        ENDHLSL

        // -------------------------------------------------------
        // Pass 1 : Depth Prepass
        // -------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest  LEqual
            Cull   Back
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag

            #include "ToonCommon.hlsl"

            struct VSIn  { float3 positionOS : POSITION; };
            struct PSIn  { float4 positionCS : SV_POSITION; };

            PSIn vert(VSIn v)
            {
                PSIn o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                return o;
            }

            float4 frag(PSIn i) : SV_Target
            {
                // カメラオクルージョン用ディザクリップ（穴からデプスを書かず背後を見せる）
                ToonDitherClip(i.positionCS.xy, _OcclusionFade);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // Pass 1b : Depth+Normal Prepass
        //   SSAO やデカールが参照する _CameraNormalsTexture を書く。
        //   このパスが無いとプリパスに描かれず「深度なし」扱いになり、
        //   ポストのフォグ (Fog.shader) が背後の距離で全身を塗り潰す。
        // -------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest  LEqual
            Cull   Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag

            #include "ToonCommon.hlsl"

            struct VSIn { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct PSIn { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            PSIn vert(VSIn v)
            {
                PSIn o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float4 frag(PSIn i) : SV_Target
            {
                ToonDitherClip(i.positionCS.xy, _OcclusionFade);
                return float4(normalize(i.normalWS), 0.0);
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // Pass 2 : シャドウマップ描画
        // -------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "ToonCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct VSIn { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct PSIn { float4 positionCS : SV_POSITION; };

            float4 GetShadowPositionHClip(VSIn v)
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 lightDirectionWS = _LightDirection;

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    lightDirectionWS = normalize(_LightPosition - positionWS);
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            PSIn vert(VSIn v)
            {
                PSIn o;
                o.positionCS = GetShadowPositionHClip(v);
                return o;
            }

            float4 frag(PSIn i) : SV_Target
            {
                ToonDitherClip(i.positionCS.xy, _OcclusionFade);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // Pass 3 : メインカラー描画（3階調トゥーン + ハッチング）
        // -------------------------------------------------------
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest  LEqual
            Cull   Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "ToonLighting.hlsl"

            struct VSIn
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct PSIn
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
            };

            PSIn vert(VSIn v)
            {
                PSIn o;
                float3 posWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.positionWS = posWS;
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS  = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w);
                o.uv         = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            float4 frag(PSIn i) : SV_Target
            {
                ToonDitherClip(i.positionCS.xy, _OcclusionFade);

                float3 vertexNormalWS = normalize(i.normalWS);
                float3 tangentWS      = normalize(i.tangentWS.xyz);
                float3 bitangentWS    = cross(vertexNormalWS, tangentWS) * i.tangentWS.w;
                float3x3 TBN = float3x3(tangentWS, bitangentWS, vertexNormalWS);

                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalScale);
                float3 N = normalize(mul(normalTS, TBN));
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);

                Light mainLight = ToonGetMainLight(i.positionWS, i.positionCS);
                float3 L = mainLight.direction;

                ToonRampParams rp;
                rp.shadowMul  = ToonShadowMul(_ShadowColor.rgb, _ShadowIntensity);
                rp.midMul     = ToonMidMul(_MidToneColor.rgb, _ShadowIntensity, _MidToneBlend);
                rp.shadowBand = float2(_ShadowThreshold, _ShadowSmooth);
                rp.midBand    = float2(_MidToneThreshold, _MidToneSmooth);
                rp.attenBand  = float2(_ShadowAttenThreshold, _ShadowAttenSmooth);

                // アルベド
                float4 tex      = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _BaseColor;
                float4 face     = SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, i.uv * _FaceTex_ST.xy + _FaceTex_ST.zw);
                float4 faceNext = SAMPLE_TEXTURE2D(_FaceNextTex, sampler_FaceNextTex, i.uv * _FaceNextTex_ST.xy + _FaceNextTex_ST.zw);
                face = lerp(face, faceNext, _FaceTransition) * _FaceColor;
                tex.rgb = lerp(tex.rgb, face.rgb, saturate(face.a * _FaceBlend));

                float ao = lerp(1.0, SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, i.uv).r, _AOIntensity);

                float level       = ToonBandLevel(dot(N, L), mainLight.shadowAttenuation, rp);
                float illuminated = saturate(level);

                float3 lighting = ToonRamp(level, rp) * mainLight.color * saturate(mainLight.distanceAttenuation);
                lighting += ToonAmbient(N, _AmbientStrength, ao);
                lighting += ToonAdditionalLights(i.positionWS, i.positionCS, N, rp) * _AdditionalLightsIntensity;

                float3 final = tex.rgb * lighting;

                // ハッチング
                float shadowStrength = 1.0 - illuminated;
                float hatchValue = ToonHatch(ToonHatchUV(i.positionWS, _HatchDensity), shadowStrength,
                                             _HatchWidth, _HatchAngle1, _HatchSharpness,
                                             float2(_HatchShadowMin, _HatchShadowMax));
                final = ToonApplyHatch(final, hatchValue, shadowStrength, _HatchColor.rgb, _HatchColor.a, _HatchIntensity);

                final += ToonRim(N, V, illuminated, _RimColor.rgb, _RimPower, _RimIntensity, _RimShadowMask);

                final = lerp(final, _FlashColor.rgb, saturate(_FlashAmount) * _FlashColor.a);

                return float4(final, tex.a);
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // Pass 4 : アウトライン（背面押し出し / 壁で正しく消える）
        // -------------------------------------------------------
        Pass
        {
            Name "OutlineBackground"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZWrite Off
            ZTest  LEqual
            Cull   Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   vert
            #pragma fragment frag

            #include "ToonCommon.hlsl"

            struct VSIn { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct PSIn { float4 positionCS : SV_POSITION; };

            PSIn vert(VSIn v)
            {
                PSIn o;
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);
                float3 posWS = TransformObjectToWorld(v.positionOS);
                float4 posCS = TransformWorldToHClip(posWS);
                posWS += nWS * ToonOutlineOffset(posCS, _OutlineWidth, _OutlineDistMin, _OutlineDistMax);
                o.positionCS = TransformWorldToHClip(posWS);
                return o;
            }

            float4 frag(PSIn i) : SV_Target
            {
                ToonDitherClip(i.positionCS.xy, _OcclusionFade);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
