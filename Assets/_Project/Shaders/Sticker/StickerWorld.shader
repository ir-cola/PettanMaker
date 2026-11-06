Shader "Sticker/World"
{
    // 地面に寝かせた板ポリシールを、指定方向へ立体的に剥がす(Peel)/貼る(Apply)。
    // 頂点シェーダーで本物の円筒ページカールに変形する(StickerCore.Sticker_CurlVertex)。
    //   ・_Peel 0→1 で剥がれ上がり、1→0 で地面へ貼り付く。
    //   ・_Dissolve で剥がれ切ったシールを溶かして消す(飛ばし演出は StickerPeelFX)。
    //   ・_Shimmer で貼り付け時の接着シマーを表面に走らせる。
    // ※ 頂点変形なので分割メッシュが必要 (StickerQuadMesh.Grid)。組込み Quad(4頂点) では曲がらない。
    //    メッシュは XY 平面 [-0.5,0.5]・表=−Z 前提 (Euler(90,0,0) で地面に寝る)。
    // 旧 World/StickerPeel を統合(名前・プロパティは後方互換)。
    Properties
    {
        _MainTex ("Sticker Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Peel ("Peel", Range(0,1)) = 0
        _Dissolve ("Dissolve (剥がれた後の消滅)", Range(-0.2, 1.2)) = -0.2
        _CurlRadius ("Curl Radius (板ローカル)", Range(0.02, 0.35)) = 0.12
        _ShadowWidth ("Shadow Width (未使用/互換)", Range(0.0,0.5)) = 0.16
        _ShadowStrength ("Curl Shade Strength", Range(0,1)) = 0.5
        _BackColor ("Sticker Back Color", Color) = (0.95, 0.93, 0.86, 1)
        _BackImageMix ("Back Image Bleed", Range(0,1)) = 0.30
        _BackDark ("Back Darkness", Range(0.3,1)) = 0.72
        _RimColor ("Free Edge Highlight", Color) = (1, 1, 1, 1)
        _DirX ("Direction X", Float) = 1
        _DirY ("Direction Y", Float) = 1
        [Toggle] _FlipY ("Flip Y", Float) = 0

        [Header(Apply)]
        _Shimmer ("Adhesive Shimmer", Range(0,2)) = 0
        _ShimmerColor ("Shimmer Color", Color) = (1,1,1,1)
        _ShimmerWidth ("Shimmer Width", Range(0.02,0.6)) = 0.16
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off Lighting Off ZWrite On ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "StickerCore.hlsl"

            struct appdata_t { float4 vertex:POSITION; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float  curl     : TEXCOORD1;   // 0=平ら 〜 1=カールの立ち上がり (陰影用)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
            float _Peel; float _Dissolve; float _CurlRadius; float _ShadowWidth; float _ShadowStrength;
            fixed4 _BackColor; float _BackImageMix; float _BackDark; fixed4 _RimColor;
            float _DirX; float _DirY; float _FlipY;
            float _Shimmer; fixed4 _ShimmerColor; float _ShimmerWidth;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float2 dir = normalize(float2(_DirX, _DirY) + 1e-5);
                float2 xyOut; float lift; float curl;
                Sticker_CurlVertex(v.vertex.xy, dir, _Peel, _CurlRadius, xyOut, lift, curl);
                v.vertex.xy = xyOut;
                v.vertex.z  = -lift;              // 板の表は −Z = 上方向へ持ち上げ

                OUT.vertex   = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.curl     = curl;
                return OUT;
            }

            fixed4 frag(v2f IN, fixed facing : VFACE) : SV_Target
            {
                float2 sampUV = float2(IN.texcoord.x, lerp(IN.texcoord.y, 1.0 - IN.texcoord.y, _FlipY));
                half4 tex = tex2D(_MainTex, sampUV);

                half3 col;
                if (facing > 0)
                {
                    // 表: シール絵。カールの立ち上がりに陰影 ＋ 接着シマー
                    col = tex.rgb * _Color.rgb;
                    col *= 1.0 - _ShadowStrength * 0.45 * IN.curl;
                    float span; float sN = Sticker_ProjectSN(IN.texcoord, normalize(float2(_DirX, _DirY) + 1e-5), span);
                    col += _ShimmerColor.rgb * (_Shimmer * Sticker_Shimmer(sN, _Peel, _ShimmerWidth));
                }
                else
                {
                    // 裏: 裏紙色 + 絵のにじみ。カールの山を縁色で明るく
                    col = lerp(_BackColor.rgb, tex.rgb, _BackImageMix) * _BackDark;
                    col = lerp(col, _RimColor.rgb, IN.curl * 0.3);
                }

                half a = tex.a * _Color.a;

                // ディゾルブ: ノイズしきい値で穴が広がりながら消える。境界は縁色で光らせる
                float n = 0.65 * Sticker_vnoise(IN.texcoord * 7.0) + 0.35 * Sticker_vnoise(IN.texcoord * 19.0);
                a *= smoothstep(_Dissolve - 0.08, _Dissolve + 0.08, n);
                float edge = saturate(1.0 - abs(n - _Dissolve) / 0.14);
                col = lerp(col, _RimColor.rgb * 1.5, edge);

                clip(a - 0.05);                    // 透明部が ZWrite で奥を隠さないように
                return half4(col, a);
            }
            ENDCG
        }
    }
}
