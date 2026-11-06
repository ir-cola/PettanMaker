Shader "PettanMaker/VFX/StunStar"
{
    // スタン（目を回した）演出の「ぐるぐる星」。テクスチャ不要のプロシージャル5角星。
    // ・ビルボード（常にカメラを向く）… 頭上を公転しても常に正面から星に見える
    // ・IQ の 5-star SDF で塗り＋縁発光をアンチエイリアス付きで描く
    // ・自己スピン（_Spin）でチカチカ回る
    // 加算合成でブルームに乗せる想定。Enemy04StunStars がランタイム生成／Shader.Find で参照する。
    Properties
    {
        [Header(Colors)]
        _Color      ("Star Color", Color)          = (1.0, 0.85, 0.2, 1)
        _EdgeColor  ("Edge Glow", Color)           = (1.0, 0.45, 0.05, 1)
        _Brightness ("Brightness", Range(0.5, 8))  = 2.5

        [Header(Shape)]
        _Radius     ("Star Radius", Range(0.3, 1.0))   = 0.82
        _InnerRatio ("Inner Ratio (くびれ)", Range(0.1, 0.9)) = 0.45
        _EdgeWidth  ("Edge Glow Width", Range(0.01, 0.4)) = 0.12
        _Softness   ("Edge Softness (AA)", Range(0.001, 0.2)) = 0.02

        [Header(Motion)]
        _Spin       ("Self Spin Speed", Float) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+20"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "StunStar"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One   // 加算合成（ブルーム向け）
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                half  _Brightness;
                half  _Radius;
                half  _InnerRatio;
                half  _EdgeWidth;
                half  _Softness;
                float _Spin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;   // 頂点アルファ = 全体の強度エンベロープ（フェード用）
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

                // --- ビルボード ---
                // オブジェクト原点をビュー空間に置き、頂点のローカルXYをビュー平面上へ展開する。
                // これでメッシュの向きに関係なく常にカメラ正面を向く。
                float3 centerWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 centerVS = TransformWorldToView(centerWS);

                // ローカルスケール（行列の各軸の長さ）を反映してサイズを保つ。
                float sx = length(GetObjectToWorldMatrix()._m00_m10_m20);
                float sy = length(GetObjectToWorldMatrix()._m01_m11_m21);

                float3 posVS = centerVS + float3(IN.positionOS.x * sx, IN.positionOS.y * sy, 0.0);
                OUT.positionCS = mul(UNITY_MATRIX_P, float4(posVS, 1.0));
                OUT.uv    = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            // IQ の 5角星 符号付き距離（内側が負）。r=外径, rf=くびれ比。
            float sdStar5(float2 p, float r, float rf)
            {
                const float2 k1 = float2(0.809016994375, -0.587785252292);
                const float2 k2 = float2(-k1.x, k1.y);
                p.x = abs(p.x);
                p -= 2.0 * max(dot(k1, p), 0.0) * k1;
                p -= 2.0 * max(dot(k2, p), 0.0) * k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0.0, 1.0);
                float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
                return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // UV(0..1) → 中心原点(-1..1)
                float2 p = (IN.uv - 0.5) * 2.0;

                // 自己スピン
                float a = _Spin * _Time.y;
                float s = sin(a), c = cos(a);
                p = float2(c * p.x - s * p.y, s * p.x + c * p.y);

                float d = sdStar5(p, _Radius, _InnerRatio);   // <0 = 星の内側

                // 塗り（内側）: 縁でアンチエイリアス
                half fill = smoothstep(_Softness, -_Softness, (half)d);
                // 縁発光: d≈0 付近を明るく
                half edge = 1.0h - smoothstep(0.0h, _EdgeWidth, (half)abs(d));

                half3 col = _Color.rgb * fill * _Brightness;
                col += _EdgeColor.rgb * edge * _Brightness;

                half alpha = saturate(max(fill, edge)) * IN.color.a * _Color.a;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
