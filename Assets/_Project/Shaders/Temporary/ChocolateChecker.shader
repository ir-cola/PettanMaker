Shader "Custom/Temporary/ChocolateChecker"
{
    Properties
    {
        [Header(Colors)]
        _ColorBase      ("Base Color",          Color)          = (0.22, 0.11, 0.04, 1)
        _ColorHighlight ("Highlight Color",     Color)          = (0.45, 0.25, 0.10, 1)

        [Header(Pattern)]
        _TilingX        ("Tiling X",            Float)          = 4.0
        _TilingY        ("Tiling Y",            Float)          = 4.0

        [Header(Bevel)]
        _BevelWidth     ("Bevel Width",         Range(0.01, 0.49)) = 0.08
        _BevelHeight    ("Bevel Height",        Range(0.0, 2.0))   = 0.8
        _GrooveDepth    ("Groove Depth",        Range(0.0, 1.0))   = 0.6

        [Header(Lighting)]
        _SpecPower      ("Specular Power",      Range(1, 256))  = 80.0
        _SpecIntensity  ("Specular Intensity",  Range(0, 4))    = 1.5
        _RimPower       ("Rim Power",           Range(1, 8))    = 3.0
        _RimIntensity   ("Rim Intensity",       Range(0, 2))    = 0.4
        _Ambient        ("Ambient",             Range(0, 1))    = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "ChocolateChecker"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct VS_INPUT
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct PS_INPUT
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorBase;
                float4 _ColorHighlight;
                float  _TilingX;
                float  _TilingY;
                float  _BevelWidth;
                float  _BevelHeight;
                float  _GrooveDepth;
                float  _SpecPower;
                float  _SpecIntensity;
                float  _RimPower;
                float  _RimIntensity;
                float  _Ambient;
            CBUFFER_END

            PS_INPUT vert(VS_INPUT input)
            {
                PS_INPUT o;
                float3 posWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.uv         = input.uv;
                o.positionWS = posWS;
                return o;
            }

            // ベベル傾斜からフェイクノーマルを生成
            // cellUV: セル内の0-1 UV, bevelWidth: ベベル幅
            // 戻り値: xy=接線空間の法線オフセット
            float2 bevelNormal(float2 cellUV, float bevelWidth)
            {
                float2 distEdge = min(cellUV, 1.0 - cellUV); // 各辺からの距離
                float2 bevel    = saturate(distEdge / bevelWidth);
                // 傾斜: 0(端)→1(中央)、勾配が法線の横成分になる
                float2 slope = (1.0 - bevel) * sign(cellUV - 0.5);
                return slope * _BevelHeight;
            }

            float4 frag(PS_INPUT input) : SV_Target
            {
                float2 tiledUV  = input.uv * float2(_TilingX, _TilingY);
                float2 cellID   = floor(tiledUV);
                float2 cellUV   = frac(tiledUV);

                // チェッカー色
                float  checker  = fmod(cellID.x + cellID.y, 2.0);
                float4 baseCol  = lerp(_ColorBase, _ColorHighlight, checker * 0.25);

                // ベベル法線（接線空間）
                float2 bevelN   = bevelNormal(cellUV, _BevelWidth);

                // メッシュ法線をベースに接線空間を構築
                float3 N        = normalize(input.normalWS);
                float3 up       = abs(N.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 tangent  = normalize(cross(up, N));
                float3 bitan    = cross(N, tangent);

                // ベベルオフセットをワールド空間に加算して法線を傾ける
                float3 perturbedN = normalize(N + tangent * bevelN.x + bitan * bevelN.y);

                // グルーブ（溝）の暗化
                float2 distEdge   = min(cellUV, 1.0 - cellUV);
                float  grooveMask = saturate(min(distEdge.x, distEdge.y) / _BevelWidth);
                float  groove     = lerp(1.0 - _GrooveDepth, 1.0, grooveMask);

                // ライティング
                float3 V     = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                Light  light = GetMainLight();
                float3 L     = normalize(light.direction);
                float3 H     = normalize(L + V);

                float  NdotL = saturate(dot(perturbedN, L));
                float  NdotH = saturate(dot(perturbedN, H));
                float  NdotV = saturate(dot(perturbedN, V));

                float3 diffuse  = baseCol.rgb * (NdotL + _Ambient) * groove;
                float3 specular = pow(NdotH, _SpecPower) * _SpecIntensity * light.color;
                float3 rim      = pow(1.0 - NdotV, _RimPower) * _RimIntensity * baseCol.rgb;

                float3 col = diffuse + specular * grooveMask + rim * grooveMask;
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
