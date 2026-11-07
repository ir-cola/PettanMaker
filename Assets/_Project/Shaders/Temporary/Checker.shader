Shader "Custom/Temporary/Checker"
{
    Properties
    {
        [Header(Colors)]
        _ColorA         ("Color A",             Color)          = (0, 0, 0, 1)
        _ColorB         ("Color B",             Color)          = (1, 1, 1, 1)

        [Header(Pattern)]
        _TilingX        ("Tiling X",            Float)          = 4.0
        _TilingY        ("Tiling Y",            Float)          = 4.0

        [Header(UV Offset)]
        _OffsetX        ("Offset X",            Float)          = 0.0
        _OffsetY        ("Offset Y",            Float)          = 0.0

        [Header(Bevel)]
        _BevelWidth     ("Bevel Width",         Range(0.01, 0.49)) = 0.08
        _BevelHeight    ("Bevel Height",        Range(0.0, 2.0))   = 0.0
        _GrooveDepth    ("Groove Depth",        Range(0.0, 1.0))   = 0.0

        [Header(Lighting)]
        _SpecPower      ("Specular Power",      Range(1, 256))  = 60.0
        _SpecIntensity  ("Specular Intensity",  Range(0, 4))    = 0.0
        _Ambient        ("Ambient",             Range(0, 1))    = 0.5

        [Header(Blend)]
        _Opacity        ("Opacity",             Range(0, 1))    = 1.0
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
            Name "Checker"
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
                float4 _ColorA;
                float4 _ColorB;
                float  _TilingX;
                float  _TilingY;
                float  _OffsetX;
                float  _OffsetY;
                float  _BevelWidth;
                float  _BevelHeight;
                float  _GrooveDepth;
                float  _SpecPower;
                float  _SpecIntensity;
                float  _Ambient;
                float  _Opacity;
            CBUFFER_END

            float2 bevelNormal(float2 cellUV, float bevelWidth)
            {
                float2 distEdge = min(cellUV, 1.0 - cellUV);
                float2 bevel    = saturate(distEdge / bevelWidth);
                return (1.0 - bevel) * sign(cellUV - 0.5) * _BevelHeight;
            }

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

            float4 frag(PS_INPUT input) : SV_Target
            {
                float2 tiledUV = input.uv * float2(_TilingX, _TilingY) + float2(_OffsetX, _OffsetY);
                float2 cellID  = floor(tiledUV);
                float2 cellUV  = frac(tiledUV);

                float  checker = fmod(cellID.x + cellID.y, 2.0);
                float4 baseCol = lerp(_ColorA, _ColorB, checker);

                // グルーブマスク（溝の暗化）
                float2 distEdge  = min(cellUV, 1.0 - cellUV);
                float  grooveMask = saturate(min(distEdge.x, distEdge.y) / _BevelWidth);
                float  groove     = lerp(1.0 - _GrooveDepth, 1.0, grooveMask);

                // ベベル法線
                float2 bevelN   = bevelNormal(cellUV, _BevelWidth);
                float3 N        = normalize(input.normalWS);
                float3 up       = abs(N.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 tangent  = normalize(cross(up, N));
                float3 bitan    = cross(N, tangent);
                float3 perturbedN = normalize(N + tangent * bevelN.x + bitan * bevelN.y);

                // ライティング
                float3 V     = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                Light  light = GetMainLight();
                float3 L     = normalize(light.direction);
                float3 H     = normalize(L + V);

                float  NdotL = saturate(dot(perturbedN, L));
                float  NdotH = saturate(dot(perturbedN, H));

                float3 diffuse  = baseCol.rgb * (NdotL + _Ambient) * groove;
                float3 specular = pow(NdotH, _SpecPower) * _SpecIntensity * grooveMask * light.color;

                float4 col = float4(diffuse + specular, baseCol.a * _Opacity);
                return col;
            }
            ENDHLSL
        }
    }
}
