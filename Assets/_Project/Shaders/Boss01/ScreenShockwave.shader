Shader "PettanMaker/Boss_01/ScreenShockwave"
{
    // 出現時の画面衝撃波。カメラ向きの Quad に貼り、_Progress で外へ広がる
    // リング状に背後のシーンカラーを歪ませる。実体の色は持たず歪みとリム光のみ。
    Properties
    {
        _Progress ("Progress", Range(0, 1)) = 0
        _RingWidth ("Ring Width", Range(0.02, 0.6)) = 0.16
        _Strength ("Distortion Strength", Range(0, 0.2)) = 0.035
        _RimColor ("Rim Color", Color) = (0.82, 0.6, 1, 1)
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 0.6
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
            Name "ScreenShockwave"
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
                half _Progress;
                half _RingWidth;
                half _Strength;
                half4 _RimColor;
                half _RimIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Quad UV 中心を原点に、正規化半径 r(0=中心, 1=端)を作る。
                float2 c = input.uv - 0.5;
                float r = length(c) * 2.0;

                // _Progress の位置に幅 _RingWidth のリングを立てる。
                half ring = 1.0h - saturate(abs(r - _Progress) / max(_RingWidth, 1e-3h));
                ring = smoothstep(0.0h, 1.0h, ring);

                // 進行に伴い全体を減衰させ、終端で消える。
                half fade = saturate(1.0h - _Progress);

                float2 dir = (r > 1e-4) ? (c / (r * 0.5)) : float2(0.0, 0.0);
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 offset = dir * ring * _Strength * fade;

                half3 scene = SampleSceneColor(screenUV + offset);
                half3 rim = _RimColor.rgb * ring * _RimIntensity * fade;
                half alpha = saturate(ring * fade);

                return half4(scene + rim, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
