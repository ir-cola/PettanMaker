Shader "PettanMaker/VFX/AdditiveTrail"
{
    // TrailRenderer 用の最小加算シェーダー。テクスチャ不要・頂点カラー（＝Trail のグラデ）対応。
    // 縁に向かって細く・透明になる帯を加算合成でブルームに乗せる。StunStar と同系統の見た目。
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (1.0, 0.85, 0.2, 1)
        _Brightness ("Brightness", Range(0.5, 8)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+19"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "AdditiveTrail"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One   // 加算合成
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half  _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;   // Trail のカラーグラデ（rgb×a=先端フェード）
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 col = _Tint.rgb * IN.color.rgb * _Brightness;
                half alpha = _Tint.a * IN.color.a;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
