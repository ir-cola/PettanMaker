Shader "PettanMaker/Boss_01/EyeRushGlow"
{
    Properties
    {
        [Header(Base)]
        _BaseColor      ("Base Color",       Color)        = (0.08, 0.0, 0.0, 1)

        [Header(Emission)]
        _EmissionColor  ("Emission Color",   Color)        = (1.0, 0.05, 0.02, 1)
        _EmissionMin    ("Emission Min",     Range(0, 20)) = 2.0
        _EmissionMax    ("Emission Max",     Range(0, 20)) = 6.0
        _PulseFrequency ("Pulse Frequency",  Range(0, 10)) = 4.0

        [Header(Rim)]
        _RimColor       ("Rim Color",        Color)        = (1.0, 0.15, 0.05, 1)
        _RimPower       ("Rim Power",        Range(0.5, 8)) = 2.0
        _RimIntensity   ("Rim Intensity",    Range(0, 10)) = 4.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
            "RenderType"     = "Opaque"
        }

        Pass
        {
            Name "EyeRushGlow"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                half  _EmissionMin;
                half  _EmissionMax;
                half  _PulseFrequency;
                half4 _RimColor;
                half  _RimPower;
                half  _RimIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs   = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normalInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.normalWS   = normalInputs.normalWS;
                OUT.viewDirWS  = GetWorldSpaceViewDir(posInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 N = normalize(IN.normalWS);
                half3 V = normalize(IN.viewDirWS);

                // 脈動するエミッション強度
                half pulse   = sin(_Time.y * _PulseFrequency) * 0.5h + 0.5h;
                half emitInt = lerp(_EmissionMin, _EmissionMax, pulse * pulse);

                half3 emission = _EmissionColor.rgb * emitInt;

                // フレネルリム
                half fresnel = pow(saturate(1.0h - dot(N, V)), _RimPower);
                half3 rim    = _RimColor.rgb * fresnel * _RimIntensity;

                half3 color = _BaseColor.rgb + emission + rim;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
