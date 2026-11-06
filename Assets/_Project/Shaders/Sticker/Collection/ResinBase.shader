Shader "PettanMaker/Sticker/Collection/ResinBase"
{
    Properties
    {
        _MainTex            ("Main Texture",        2D)             = "white" {}
        _Color              ("Color Tint",          Color)          = (1, 1, 1, 1)
        _AlphaCutoff        ("Alpha Cutoff",        Range(0, 1))    = 0.5

        [Header(Border)]
        _BorderColor        ("Border Color",        Color)          = (1, 1, 1, 1)
        _BorderPixels       ("Border Width (px)",   Range(0, 16))   = 3.0

        [Header(Highlight)]
        _HighlightColor     ("Highlight Color",     Color)          = (1, 1, 1, 1)
        _HighlightIntensity ("Highlight Intensity", Range(0, 3))    = 1.2
        _HighlightPower     ("Highlight Power",     Range(1, 32))   = 6.0
        _HighlightShift     ("Highlight Shift",     Range(0, 1))    = 0.35
        _RimIntensity       ("Rim Intensity",       Range(0, 2))    = 0.5
        _RimPower           ("Rim Power",           Range(0.5, 8))  = 3.0

        [Header(Lighting)]
        _AmbientMin         ("Ambient Min",         Range(0, 1))    = 0.5
        _ShadowStrength     ("Shadow Strength",     Range(0, 1))    = 0.25

        [Header(Dome Face Cast Shadow)]
        [NoScaleOffset] _FaceTex ("Dome Face Tex (cast shadow src)", 2D) = "black" {}
        _FaceShadowColor    ("Face Shadow Color",    Color)          = (0.2, 0.2, 0.25, 1)
        _FaceShadowTexTint  ("Face Shadow Tex Tint (0 color / 1 tex)", Range(0, 1)) = 1.0
        _FaceShadowStrength ("Face Shadow Strength", Range(0, 1))    = 0.55
        _FaceShadowOffset   ("Face Shadow Offset UV", Vector)        = (0.012, -0.012, 0, 0)
        _FaceShadowCutoff   ("Face Shadow Cutoff",   Range(0, 1))    = 0.15
        _FaceShadowSoftness ("Face Shadow Softness (texels)", Range(0, 12)) = 3.0
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
            Name "ResinBase"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_FaceTex);
            SAMPLER(sampler_FaceTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _FaceTex_TexelSize;
                half4  _Color;
                half   _AlphaCutoff;
                half4  _BorderColor;
                half   _BorderPixels;
                half4  _HighlightColor;
                half   _HighlightIntensity;
                half   _HighlightPower;
                half   _HighlightShift;
                half   _RimIntensity;
                half   _RimPower;
                half   _AmbientMin;
                half   _ShadowStrength;
                half4  _FaceShadowColor;
                half   _FaceShadowTexTint;
                half   _FaceShadowStrength;
                float4 _FaceShadowOffset;
                half   _FaceShadowCutoff;
                half   _FaceShadowSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 tangentWS   : TEXCOORD1;
                float3 bitangentWS : TEXCOORD2;
                float3 viewDirWS   : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float3 positionWS  : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs   = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS  = posInputs.positionCS;
                OUT.normalWS    = normalInputs.normalWS;
                OUT.tangentWS   = normalInputs.tangentWS;
                OUT.bitangentWS = normalInputs.bitangentWS;
                OUT.viewDirWS   = GetWorldSpaceViewDir(posInputs.positionWS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.positionWS  = posInputs.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv  = IN.uv;
                half4  tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                clip(tex.a - _AlphaCutoff);

                // Border: 8方向のアルファ膨張でシール輪郭を検出
                float2 ts = _MainTex_TexelSize.xy * _BorderPixels;
                half a0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( ts.x,    0  )).a;
                half a1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-ts.x,    0  )).a;
                half a2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(   0,  ts.y  )).a;
                half a3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(   0, -ts.y  )).a;
                half a4 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( ts.x,  ts.y )).a;
                half a5 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-ts.x,  ts.y )).a;
                half a6 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( ts.x, -ts.y )).a;
                half a7 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-ts.x, -ts.y )).a;
                half minNeighbor = min(min(min(a0, a1), min(a2, a3)), min(min(a4, a5), min(a6, a7)));
                bool isBorder = (minNeighbor < _AlphaCutoff);

                // Lighting (half-Lambert + ambient floor)
                half3 N    = normalize(IN.normalWS);
                half3 T    = normalize(IN.tangentWS);
                half3 BT   = normalize(IN.bitangentWS);
                half3 V    = normalize(IN.viewDirWS);
                half NdotV = saturate(dot(N, V));

                // Domeが落とす模様のシャドウマップをそのまま受け取り、下地に立体感を出す
                float4 shadowCoord;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    shadowCoord = ComputeScreenPos(IN.positionCS);
                #else
                    shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half3 L      = normalize(mainLight.direction);
                half NdotL   = dot(N, L) * 0.5h + 0.5h;
                half diffuse = max(NdotL, _AmbientMin);
                diffuse      = lerp(1.0h, diffuse, _ShadowStrength);
                diffuse      *= lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);

                half3 baseColor = tex.rgb * _Color.rgb * diffuse * mainLight.color;

                // Domeの顔テクスチャをオフセットサンプリングした擬似ドロップシャドウ。
                // シャドウマップは小物ステッカーの微細な模様を解像できないため、下地側に焼き込む。
                // DomeとBaseは同一UVレイアウト(同一FBX)なのでBaseのUVでDome模様を引ける。
                float2 faceUV = uv + _FaceShadowOffset.xy;
                float2 fs     = _FaceTex_TexelSize.xy * _FaceShadowSoftness;
                half4 c0 = SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, faceUV);
                half s1 = SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, faceUV + float2( fs.x,  fs.y)).a;
                half s2 = SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, faceUV + float2(-fs.x,  fs.y)).a;
                half s3 = SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, faceUV + float2( fs.x, -fs.y)).a;
                half s4 = SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, faceUV + float2(-fs.x, -fs.y)).a;
                half faceAlpha  = (c0.a + s1 + s2 + s3 + s4) * 0.2h;
                half faceShadow = smoothstep(_FaceShadowCutoff, saturate(_FaceShadowCutoff + 0.15h), faceAlpha) * _FaceShadowStrength;
                // 影色: 固定色 _FaceShadowColor と Dome模様のテクスチャ色を _FaceShadowTexTint でブレンド
                // (色付きレジンが同色の影を落とすステンドグラス的表現)
                half3 shadowMul = lerp(_FaceShadowColor.rgb, c0.rgb * _FaceShadowColor.rgb, _FaceShadowTexTint);
                baseColor = lerp(baseColor, baseColor * shadowMul, faceShadow);

                // UV空間スポットハイライト: ビュー方向をタンジェント空間に投影してUVオフセット
                // 視点が傾くにつれてスポットが移動する → フラット面でも光沢が見える
                half2 viewTS   = half2(dot(V, T), dot(V, BT));
                half2 spotUV   = (half2(uv) - 0.5h) - viewTS * _HighlightShift;
                half  spotDist = length(spotUV);
                half  spot     = pow(saturate(1.0h - spotDist), _HighlightPower);
                half3 spotCol  = _HighlightColor.rgb * spot * _HighlightIntensity;

                // フレネルリム (縁の輝き)
                half  fresnel = pow(1.0h - NdotV, _RimPower);
                half3 rimCol  = _HighlightColor.rgb * fresnel * _RimIntensity;

                half3 col = isBorder ? _BorderColor.rgb : (baseColor + spotCol + rimCol);
                return half4(col, tex.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
