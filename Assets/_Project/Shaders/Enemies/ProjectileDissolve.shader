Shader "Enemy/ProjectileDissolve"
{
    // 敵投擲物の消滅ディゾルブ。着地バウンド後、元マテリアルの色/テクスチャを引き継いで
    // バリューノイズのまだらで欠けていき、境界が発光して消える。
    // Effects/StampDissolve と同系のノイズ/縁色で演出の統一感を持たせている。
    // スクリプト(Enemy01_Projectile)が _Dissolve を 0→1 に動かして消す。
    Properties
    {
        _MainTex    ("Texture", 2D)                    = "white" {}
        _BaseColor  ("Tint", Color)                    = (1, 1, 1, 1)
        _Dissolve   ("Dissolve (0=表示, 1=消滅)", Range(0, 1)) = 0
        _NoiseScale ("Noise Scale", Float)             = 9
        [HDR] _EdgeColor ("Dissolve Edge", Color)      = (1.0, 0.55, 0.15, 1)
        _EdgeWidth  ("Edge Width", Range(0.01, 0.4))   = 0.14
    }

    SubShader
    {
        Tags { "Queue"="Geometry+2" "IgnoreProjector"="True" "RenderType"="Opaque" }
        Cull Off Lighting Off ZWrite On ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _BaseColor;
            float  _Dissolve;
            float  _NoiseScale;
            fixed4 _EdgeColor;
            float  _EdgeWidth;

            struct appdata_t { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float3 wpos   : TEXCOORD1;
                float3 wnrm   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.uv = TRANSFORM_TEX(v.uv, _MainTex);
                OUT.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                OUT.wnrm = UnityObjectToWorldNormal(v.normal);
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
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

            fixed4 frag(v2f IN) : SV_Target
            {
                // 簡易ライティング（固定方向の半ランバート。短いディゾルブ中の立体感が残れば十分）
                float3 l = normalize(float3(0.4, 0.8, 0.3));
                float ndl = saturate(dot(normalize(IN.wnrm), l)) * 0.5 + 0.5;

                fixed4 col = tex2D(_MainTex, IN.uv) * _BaseColor;
                col.rgb *= lerp(0.75, 1.05, ndl);

                // ワールド位置ベースのノイズで欠けさせる（UVが無い/歪むメッシュでも安定）
                float n = valueNoise(IN.wpos.xz * _NoiseScale + IN.wpos.y * 3.7);
                float th = _Dissolve * (1.0 + _EdgeWidth * 2.0) - _EdgeWidth;
                clip(n - th);                                   // 欠けた所は完全に消す（ZWrite On）

                // 溶けている縁を発光させる
                float edge = 1.0 - smoothstep(th, th + _EdgeWidth, n);
                col.rgb = lerp(col.rgb, _EdgeColor.rgb * 1.6, edge);
                return col;
            }
            ENDCG
        }
    }
}
