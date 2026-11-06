/**===========================================================================

 File   : 角丸＋アウトライン UIシェーダー [RoundedOutline.shader]

 Author : Hayato Otsuki
 Date   : 2026/7/9
 -----------------------------------------------------------------------------

 RawImage/Image 用。_MainTex（プレイヤー描画RTなど）を角丸矩形にクリップし、
 縁に白アウトラインを描く。チュートリアルビルボード（角丸＋白縁）と同系の見た目。
   ・_Size          : パネルのピクセルサイズ(w,h)。角丸を真円に保つため実サイズで計算
   ・_Radius        : 角丸半径(px)
   ・_OutlineWidth  : アウトライン太さ(px)
   ・_OutlineColor  : アウトライン色（既定 白）
 角丸のSDFで内外をアンチエイリアス。ScreenSpaceOverlay Canvas で使用。

===========================================================================**/
Shader "UI/RoundedOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Size ("Panel Size (px)", Vector) = (560,660,0,0)
        _Radius ("Corner Radius (px)", Float) = 40
        _OutlineWidth ("Outline Width (px)", Float) = 6
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _Size;
            float  _Radius;
            float  _OutlineWidth;
            fixed4 _OutlineColor;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = v.texcoord;
                return o;
            }

            // 角丸矩形の符号付き距離（px空間）。d<0 が内側。
            float RoundedBoxSDF(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - (halfSize - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord) * i.color;

                float2 half = _Size.xy * 0.5;
                float2 p = (i.texcoord - 0.5) * _Size.xy;
                float r = min(_Radius, min(half.x, half.y));
                float d = RoundedBoxSDF(p, half, r);

                float aa = max(fwidth(d), 1e-4);
                float edge = 1.0 - smoothstep(-aa, aa, d);                 // 角丸クリップ
                float outline = smoothstep(-_OutlineWidth - aa, -_OutlineWidth + aa, d); // 縁の帯

                fixed4 col = tex;
                col.rgb = lerp(col.rgb, _OutlineColor.rgb, outline * _OutlineColor.a);
                col.a = max(tex.a, outline * _OutlineColor.a) * edge;
                return col;
            }
            ENDCG
        }
    }
}
