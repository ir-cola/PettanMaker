Shader "Custom/B1_EntranceSmoke"
{
    // 登場用の紫煙パーティクル（チェシャ猫のように煙から出てくる用）。
    // ParticleSystem の頂点カラー（startColor / colorOverLifetime）× テクスチャ × Tint で発色。
    // 半透明・ソフトエッジ。カメラ手前ソフトパーティクル（深度フェード）付き。
    Properties
    {
        _MainTex   ("Smoke Texture", 2D)          = "white" {}
        [HDR] _Tint ("Tint", Color)               = (0.55, 0.2, 0.95, 1)
        _AlphaBoost ("Alpha Boost", Range(0, 4))  = 1.2
        _Softness   ("Edge Softness", Range(0, 1))= 0.35
        _SoftParticle ("Soft Particle Depth", Range(0, 4)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Tint;
                half   _AlphaBoost;
                half   _Softness;
                half   _SoftParticle;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;   // パーティクルの頂点カラー
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv         = TRANSFORM_TEX(input.uv, _MainTex);
                o.color      = input.color;
                o.screenPos  = ComputeScreenPos(p.positionCS);
                o.fogFactor  = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 c   = tex * _Tint * i.color;

                // アルファを持ち上げてソフトに整える
                half a = saturate(c.a * _AlphaBoost);
                a *= smoothstep(0.0h, _Softness + 0.001h, a);

                // ソフトパーティクル：不透明面との交差をやわらげて板っぽさを消す
                #if defined(_SOFT_PARTICLE_ON) || 1
                float2 suv   = i.screenPos.xy / max(i.screenPos.w, 1e-4);
                float sceneD = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float partD  = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float soft   = saturate((sceneD - partD) / max(_SoftParticle, 1e-3));
                a *= soft;
                #endif

                c.rgb = MixFog(c.rgb, i.fogFactor);
                return half4(c.rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
