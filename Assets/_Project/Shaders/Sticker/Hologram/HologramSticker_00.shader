Shader "PettanMaker/Sticker/HologramSticker_00"
{
    Properties
    {
        _MainTex            ("Main Texture",        2D)             = "white" {}
        _AlphaCutoff        ("Alpha Cutoff",        Range(0, 1))    = 0.5

        [Header(Hologram Masks)]
        _HoloMask1          ("Holo Mask 1",         2D)             = "black" {}
        _HoloMask1Intensity ("Mask 1 Intensity",    Range(0, 2))    = 1.0
        _HoloMask2          ("Holo Mask 2",         2D)             = "black" {}
        _HoloMask2Intensity ("Mask 2 Intensity",    Range(0, 2))    = 1.0

        [Header(Hologram)]
        _HoloIntensity      ("Holo Intensity",      Range(0, 4))    = 2.0
        _HoloColor1         ("Holo Color 1",        Color)          = (1, 0, 0.5, 1)
        _HoloColor2         ("Holo Color 2",        Color)          = (0, 0.5, 1, 1)
        _HoloColor3         ("Holo Color 3",        Color)          = (0.5, 1, 0, 1)
        _HoloColor4         ("Holo Color 4",        Color)          = (1, 0.8, 0, 1)
        _NoiseScale         ("Noise Scale",         Range(0.1, 20)) = 4.0
        _HueOffset          ("Hue Offset",          Range(0, 1))    = 0.0
        _CycleSpeed         ("Cycle Speed",         Range(0, 2))    = 0.3
        _ShineSpeed         ("Shine Speed",         Range(0.1, 5))  = 1.0
        _ShineIntensity     ("Shine Intensity",     Range(0, 4))    = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "TransparentCutout"
            "Queue"          = "AlphaTest"
        }

        Pass
        {
            Name "HologramSticker"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_HoloMask1);  SAMPLER(sampler_HoloMask1);
            TEXTURE2D(_HoloMask2);  SAMPLER(sampler_HoloMask2);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _HoloMask1_ST;
                float4 _HoloMask2_ST;
                float  _AlphaCutoff;
                float  _HoloMask1Intensity;
                float  _HoloMask2Intensity;
                float  _HoloIntensity;
                float4 _HoloColor1;
                float4 _HoloColor2;
                float4 _HoloColor3;
                float4 _HoloColor4;
                float  _NoiseScale;
                float  _HueOffset;
                float  _CycleSpeed;
                float  _ShineSpeed;
                float  _ShineIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 rawUV      : TEXCOORD1;
            };

            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // バイリニア補間のスムーズノイズ（0〜1を返す）
            float smoothNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 4色を0.25刻みで循環補間
            float3 fourColorGradient(float t)
            {
                t = frac(t) * 4.0;
                float3 c1 = _HoloColor1.rgb;
                float3 c2 = _HoloColor2.rgb;
                float3 c3 = _HoloColor3.rgb;
                float3 c4 = _HoloColor4.rgb;
                if      (t < 1.0) return lerp(c1, c2, t);
                else if (t < 2.0) return lerp(c2, c3, t - 1.0);
                else if (t < 3.0) return lerp(c3, c4, t - 2.0);
                else              return lerp(c4, c1, t - 3.0);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.rawUV      = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float4 baseTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(baseTex.a - _AlphaCutoff);

                float2 uv1    = TRANSFORM_TEX(IN.rawUV, _HoloMask1);
                float2 uv2    = TRANSFORM_TEX(IN.rawUV, _HoloMask2);
                float  mask1  = SAMPLE_TEXTURE2D(_HoloMask1, sampler_HoloMask1, uv1).r;
                float  mask2  = SAMPLE_TEXTURE2D(_HoloMask2, sampler_HoloMask2, uv2).r;
                float  holoStr = saturate(mask1 * _HoloMask1Intensity + mask2 * _HoloMask2Intensity);

                float  t       = _Time.y;
                float  hue     = smoothNoise(IN.uv * _NoiseScale) + _HueOffset + t * _CycleSpeed;
                float  shine   = pow(saturate(sin(t * _ShineSpeed * 3.14159)), 4.0);
                float3 holo    = fourColorGradient(hue) * (_HoloIntensity + shine * _ShineIntensity) * holoStr;

                return float4(baseTex.rgb + holo, baseTex.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
