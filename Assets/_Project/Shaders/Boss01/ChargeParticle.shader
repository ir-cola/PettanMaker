Shader "PettanMaker/Boss_01/ChargeParticle"
{
    Properties
    {
        _MainTex   ("Particle Texture",   2D)          = "white" {}
        _BaseColor ("Base Color",         Color)       = (1,1,1,1)
        _Intensity ("Emission Intensity", Range(0,10)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+100"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "ChargeParticle"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull   Off
            ZWrite Off
            ZTest  LEqual
            // 加算合成: アルファをRGBに畳み込むことでテクスチャのエッジが柔らかくなる
            Blend  One One

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _BaseColor;
                half   _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;      // ParticleSystem の Color over Lifetime
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color * _BaseColor;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                // アルファをRGBに乗算 → Blend One One でもエッジがフェードする
                half4 col;
                col.rgb = tex.rgb * tex.a * IN.color.rgb * IN.color.a * _Intensity;
                col.a   = 0;
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
