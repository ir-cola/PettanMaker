Shader "PettanMaker/Common/ToonTrail"
{
    // TrailRenderer 用。colorGradient がそのまま vertex color に乗る。
    // 幅方向(V)の中心を明るく・端をトゥーン的に階段状の影に落とし、
    // Toon.shader のシェーディングを簡易に再現する。
    Properties
    {
        [Header(Core)]
        _MainTex        ("Trail Texture (RGB=Tint, A=Mask)", 2D) = "white" {}
        _TintColor      ("Tint Color",   Color)          = (1,1,1,1)
        _Brightness     ("Brightness",   Range(0.5, 10))  = 1.5

        [Header(Shape)]
        _EdgeSoftness   ("Edge Softness (V幅方向)", Range(0.001, 1)) = 0.35
        _HeadFade       ("Head Fade (U先端フェード, 0=無効)", Range(0, 1)) = 0.0

        [Header(Toon Banding)]
        [Toggle(_TOON_BANDING)] _ToonBanding ("Toon Step Banding", Float) = 1
        _BandSteps      ("Band Steps",     Range(1, 8)) = 3
        _ShadowColor    ("Shadow Tint",    Color)       = (0.55,0.55,0.75,1)
        _ShadowStrength ("Shadow Strength",Range(0,1))  = 0.5

        [Header(Rim)]
        _RimColor       ("Rim Color",      Color)         = (1,1,1,1)
        _RimIntensity   ("Rim Intensity",  Range(0,3))    = 0.6

        [Header(Blend)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 6 // OneMinusSrcAlpha
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
            Name "ToonTrail"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _TOON_BANDING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _TintColor;
                half   _Brightness;
                half   _EdgeSoftness;
                half   _HeadFade;
                half   _BandSteps;
                half4  _ShadowColor;
                half   _ShadowStrength;
                half4  _RimColor;
                half   _RimIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;  // U=長さ方向, V=幅方向(0〜1, 0.5が中心)
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
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv * _MainTex_ST.xy + _MainTex_ST.zw);

                // 幅方向(V): 中心=1, 端=0 の疑似ライティング値として使う
                half vCentered = abs(IN.uv.y - 0.5h) * 2.0h; // 0=中心, 1=端
                half lit       = 1.0h - vCentered;
                half edgeFade  = 1.0h - smoothstep(1.0h - _EdgeSoftness, 1.0h, vCentered);

                // 先端(生成されたばかりの部分)のフェードイン。0なら無効。
                half headFade = _HeadFade > 0.0001h ? smoothstep(0.0h, _HeadFade, IN.uv.x) : 1.0h;

                half4 vcol  = IN.color * _TintColor * tex;
                half  alpha = vcol.a * edgeFade * headFade;
                half3 baseColor = vcol.rgb * _Brightness;

                #if defined(_TOON_BANDING)
                    // lit を階段状に量子化してトゥーンのバンド影を疑似再現
                    half banded = floor(lit * _BandSteps) / max(_BandSteps - 1.0h, 1.0h);
                    half shade  = saturate(banded + (1.0h - _ShadowStrength));
                    baseColor = lerp(baseColor * _ShadowColor.rgb, baseColor, shade);
                #endif

                // 中心に沿って走るリムライト
                half rim = pow(lit, 2.0h) * _RimIntensity;
                baseColor += _RimColor.rgb * rim * alpha;

                return half4(baseColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
