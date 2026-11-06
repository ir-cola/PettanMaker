Shader "PettanMaker/Boss_01/RushTrail"
{
    // TrailRenderer の colorGradient がそのまま vertex color に乗る。
    // 加算合成でブルームに乗りやすくする。
    Properties
    {
        [Header(Core)]
        _TintColor      ("Tint Color",       Color)        = (1.0, 0.15, 0.05, 1)
        _Brightness     ("Brightness",       Range(1, 20)) = 6.0

        [Header(Shape)]
        _EdgeSoftness   ("Edge Softness",    Range(0.01, 1)) = 0.35
        // 0=中心だけ光る  1=均一
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+10"
            "RenderType"     = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RushTrail"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One      // 加算合成
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TintColor;
                half  _Brightness;
                half  _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;  // U=長さ方向, V=幅方向(0〜1)
                half4  color      : COLOR;       // TrailRenderer の colorGradient
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                OUT.color      = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // V=0.5 が中心。そこを最大輝度、端に向かって落とす。
                half vCentered = abs(IN.uv.y - 0.5h) * 2.0h;   // 0=中心, 1=端
                half edgeFade  = 1.0h - smoothstep(1.0h - _EdgeSoftness, 1.0h, vCentered);

                half4 vcol  = IN.color * _TintColor;
                half3 color = vcol.rgb * _Brightness * edgeFade;
                half  alpha = vcol.a   * edgeFade;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
