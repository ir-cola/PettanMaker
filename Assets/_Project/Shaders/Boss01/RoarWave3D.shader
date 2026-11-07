Shader "PettanMaker/Boss_01/RoarWave3D"
{
    Properties
    {
        _Progress ("Progress", Range(0, 1)) = 0
        _RingWidth ("Shell Width", Range(0.02, 0.9)) = 0.4
        _Strength ("Surface Distortion", Range(0, 1)) = 0.2
        [HDR] _RimColor ("Roar Color", Color) = (1.2, 0.25, 1.8, 1)
        _RimIntensity ("Emission", Range(0, 8)) = 2
        _FresnelPower ("Fresnel Power", Range(0.2, 8)) = 2.2
        _NoiseScale ("Noise Scale", Range(0.1, 20)) = 4
        _NoiseSpeed ("Noise Speed", Range(-10, 10)) = 2
        _CoreAlpha ("Core Alpha", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RoarWave3D"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            Cull Back
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Progress;
                half _RingWidth;
                half _Strength;
                half4 _RimColor;
                half _RimIntensity;
                half _FresnelPower;
                half _NoiseScale;
                half _NoiseSpeed;
                half _CoreAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half noise : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 p = input.positionOS.xyz;
                float phase = dot(p, float3(1.73, 2.31, 2.87)) * _NoiseScale
                            + _Time.y * _NoiseSpeed;
                float noise = sin(phase) * 0.55 + sin(phase * 1.91 + 1.7) * 0.3;
                p += input.normalOS * noise * _Strength * 0.12 * (1.0 - _Progress);

                VertexPositionInputs pos = GetVertexPositionInputs(p);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = normal.normalWS;
                output.noise = noise * 0.5 + 0.5;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                half3 v = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(saturate(1.0h - abs(dot(n, v))), _FresnelPower);
                half shell = smoothstep(1.0h - _RingWidth, 1.0h, fresnel);
                half ripple = lerp(0.65h, 1.35h, input.noise);
                half life = saturate(1.0h - _Progress);
                life *= life;

                half alpha = saturate((shell + _CoreAlpha) * ripple * life);
                half3 color = _RimColor.rgb * _RimIntensity * ripple * life;
                return half4(color, alpha * _RimColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
