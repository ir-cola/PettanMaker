// Queen_Chasle 厄災の渦(ハイラル城風)用ディゾルブシェーダー。
// 城を囲む筒メッシュに貼り、ノイズ(Kenneyのsmoke)をUVスクロールさせて
// ディゾルブの模様が城の周りを回転しながら流れるように見せる。
// ノイズを段階量子化(_Steps)してToon調のバンディングにしている。
Shader "PettanMaker/QC_MaliceSwirlDissolve"
{
    Properties
    {
        _NoiseMap("Noise Map (smoke)", 2D) = "white" {}
        _BodyColor("Body Color", Color) = (0.10, 0.03, 0.18, 0.9)
        [HDR] _EdgeColor("Edge Color (HDR)", Color) = (2.0, 0.36, 1.1, 1)
        _Dissolve("Dissolve Threshold", Range(0, 1)) = 0.45
        _EdgeWidth("Edge Width", Range(0.01, 0.6)) = 0.25
        _Steps("Toon Steps", Range(1, 8)) = 4
        // x: 横スクロール(=回転) y: 縦スクロール(=立ち昇り)
        _ScrollSpeed("Scroll Speed (x=rotate, y=rise)", Vector) = (0.12, 0.05, 0, 0)
        _Tiling("Noise Tiling", Vector) = (3, 1, 0, 0)
        _BottomFade("Bottom Fade", Range(0.01, 1)) = 0.15
        _TopFade("Top Fade", Range(0.01, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseMap_ST;
                half4 _BodyColor;
                half4 _EdgeColor;
                float _Dissolve;
                float _EdgeWidth;
                float _Steps;
                float4 _ScrollSpeed;
                float4 _Tiling;
                float _BottomFade;
                float _TopFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;   // スクロール済みUV
                float2 uvRaw : TEXCOORD1; // 元UV(縦フェード用)
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uvRaw = IN.uv;
                OUT.uv = IN.uv * _Tiling.xy + _Time.y * _ScrollSpeed.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 2枚のノイズを速度・スケール違いで重ねてうねりを作る
                float n1 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, IN.uv).a;
                float2 uv2 = IN.uv * 1.7 + float2(-_Time.y * _ScrollSpeed.x * 0.6,
                                                   _Time.y * _ScrollSpeed.y * 1.3);
                float n2 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uv2).a;
                float n = n1 * 0.65 + n2 * 0.35;

                // Toon調:ノイズを段階量子化してバンディングさせる
                float steps = max(_Steps, 1.0);
                n = floor(n * steps) / steps;

                // ディゾルブ:しきい値未満は消し、しきい値直上の帯をHDR発光エッジに
                float mask = step(_Dissolve, n);
                float edge = mask * (1.0 - step(_Dissolve + _EdgeWidth, n));
                half3 color = lerp(_BodyColor.rgb, _EdgeColor.rgb, edge);

                // 上下端をフェードして筒の切れ目を隠す
                float fade = saturate(IN.uvRaw.y / _BottomFade)
                           * saturate((1.0 - IN.uvRaw.y) / _TopFade);
                half alpha = mask * _BodyColor.a * fade;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
