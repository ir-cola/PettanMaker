Shader "PettanMaker/Boss_01/ManaDome"
{
    // 落雷の着弾後に残る「電気ドーム」。Sparkテクスチャを2層でスクロール＋回転させ、
    // フレネルで中心を抜いて縁を光らせる。ソリッドな塗りつぶしにならず、
    // 雷のあとの残り電気が半球の表面を這って回っているように見せる。加算発光。
    Properties
    {
        _SparkTex ("Spark (grayscale)", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (2.4, 0.8, 4.0, 1)
        _Emission ("Emission", Range(0, 12)) = 4
        _Tiling ("Tiling (u,v)", Vector) = (2, 1, 0, 0)
        _RotateSpeed ("Rotate Speed (回転)", Range(-4, 4)) = 0.6
        _CrawlSpeed ("Crawl Speed (這い上がり)", Range(-4, 4)) = 0.25
        _Layer2Scale ("Layer2 Scale", Range(0.5, 3)) = 1.4
        _FresnelPower ("Fresnel Power", Range(0.2, 8)) = 2.2
        _RimBoost ("Rim Boost", Range(0, 4)) = 1.5
        _CoreAlpha ("Core Alpha (中心の抜け)", Range(0, 1)) = 0.06
        _Alpha ("Overall Alpha", Range(0, 3)) = 1
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
            Name "ManaDome"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One   // 加算発光
            Cull Off             // 半球なので内外どちらからも見える
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SparkTex);
            SAMPLER(sampler_SparkTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _SparkTex_ST;
                half4 _Color;
                half _Emission;
                float4 _Tiling;
                half _RotateSpeed;
                half _CrawlSpeed;
                half _Layer2Scale;
                half _FresnelPower;
                half _RimBoost;
                half _CoreAlpha;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            // UV を中心まわりに角度 a で回す。
            float2 RotateUV(float2 uv, float a)
            {
                float s = sin(a);
                float c = cos(a);
                uv -= 0.5;
                uv = float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
                return uv + 0.5;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = normal.normalWS;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y;

                // レイヤー1：横（＝ドームの周方向）へスクロール＝電気が回る、縦へ這い上がる。
                float2 uv1 = input.uv * _Tiling.xy + float2(t * _RotateSpeed, t * _CrawlSpeed);
                half s1 = SAMPLE_TEXTURE2D(_SparkTex, sampler_SparkTex, uv1).r;

                // レイヤー2：回転＋逆向きスクロールで重ね、タイリングの規則性を隠して蠢かせる。
                float2 uv2 = RotateUV(input.uv, t * _RotateSpeed * 0.5) * _Tiling.xy * _Layer2Scale
                           + float2(-t * _RotateSpeed * 0.7, t * _CrawlSpeed * 1.6);
                half s2 = SAMPLE_TEXTURE2D(_SparkTex, sampler_SparkTex, uv2).r;

                half spark = saturate(s1 + s2);

                // フレネル：縁ほど濃く、中心は _CoreAlpha ぶんだけ薄く抜く（ソリッドにしない）。
                half3 n = normalize(input.normalWS);
                half3 v = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(saturate(1.0h - abs(dot(n, v))), _FresnelPower);
                half rim = saturate(fresnel * _RimBoost + _CoreAlpha);

                half alpha = saturate(spark * rim) * _Alpha * _Color.a;
                half3 color = _Color.rgb * _Emission * spark * rim;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
