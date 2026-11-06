Shader "PettanMaker/Boss_01/ScreenColorDrain"
{
    // Roar 直前の「色を一瞬抜く」演出。カメラ前面のフルスクリーン Quad に貼り、
    // _Amount(0→1) で背後のシーンカラーを彩度ダウン＋わずかな減光/ティントへ差し替える。
    // 実体の色は持たず、SampleSceneColor を desaturate して alpha=_Amount で合成する。
    Properties
    {
        _Amount ("Amount", Range(0, 1)) = 0                 // 0=通常, 1=最大色抜き
        _MaxDesaturation ("Max Desaturation", Range(0, 1)) = 1
        _Darken ("Darken", Range(0, 1)) = 0.12
        _Tint ("Tint", Color) = (0.75, 0.72, 0.85, 1)
        _TintAmount ("Tint Amount", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ScreenColorDrain"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Amount;
                half _MaxDesaturation;
                half _Darken;
                half4 _Tint;
                half _TintAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                half3 scene = SampleSceneColor(screenUV);

                // 輝度へ寄せて彩度を落とす。さらに軽くティスト/減光を重ねる。
                half luma = dot(scene, half3(0.299h, 0.587h, 0.114h));
                half3 desat = lerp(scene, (half3)luma, _MaxDesaturation);
                desat = lerp(desat, desat * _Tint.rgb, _TintAmount);
                desat *= (1.0h - _Darken);

                // alpha=_Amount で blend が scene→desat を線形補間する（通常時 alpha=0＝素通り）。
                return half4(desat, saturate(_Amount));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
