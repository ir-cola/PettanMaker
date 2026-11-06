/**===========================================================================

 File   : 虹色ゲージ UIシェーダー [RainbowGauge.shader]

 Author : Hayato Otsuki
 Date   : 2026/7/17
 -----------------------------------------------------------------------------

 HUD の StickerGauge (虹色ゲージ) 用。既存の虹ゲージスプライトの「形」だけを
 アルファマスクとして使い、流れる虹グラデーションをシェーダーで生成する。
   ・_Fill        : 溜まり量 0..1。左→右に見えていく (未充填は透明=BGが見える)
   ・_UVRect      : このスプライトのアトラス内 UV 矩形 (SkillGaugeUI が毎フレーム設定)。
                    アトラス sub-sprite の texcoord を 0..1 に正規化するために必須。
   ・_Speed       : 虹スクロール速度 / _HueScale : バー幅あたりの色相周回数
   ・_EdgeGlow    : 充填先端 (ウェーブフロント) の光る帯
 UGUI Image.material に割り当てて使用。_MainTex は [PerRendererData] で
 CanvasRenderer が sprite のアトラステクスチャを流し込む。

===========================================================================**/
Shader "UI/RainbowGauge"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fill ("Fill (0..1)", Range(0,1)) = 1
        _UVRect ("Sprite UV Rect (script)", Vector) = (0,0,1,1)
        _Speed ("Hue Scroll Speed", Float) = 0.35
        _HueScale ("Hue Cycles Across Bar", Float) = 1.15
        _Saturation ("Saturation", Range(0,1)) = 0.9
        _Brightness ("Brightness", Range(0,2)) = 1.05
        _VShade ("Vertical Shade", Range(0,1)) = 0.28
        _EdgeSoftness ("Fill Edge Softness", Range(0.0005,0.2)) = 0.02
        _EdgeGlow ("Fill Edge Glow", Range(0,3)) = 1.1
        _Shine ("Shine Sweep", Range(0,2)) = 0.4

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
            float  _Fill;
            float4 _UVRect;
            float  _Speed, _HueScale, _Saturation, _Brightness, _VShade;
            float  _EdgeSoftness, _EdgeGlow, _Shine;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = v.texcoord;
                return o;
            }

            // 色相(0..1)→RGB (彩度1)。分岐なしの近似。
            float3 Hue2RGB(float h)
            {
                h = frac(h);
                float r = abs(h * 6.0 - 3.0) - 1.0;
                float g = 2.0 - abs(h * 6.0 - 2.0);
                float b = 2.0 - abs(h * 6.0 - 4.0);
                return saturate(float3(r, g, b));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 形はスプライトのアルファ (生のアトラス UV でサンプル)
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                float shape = tex.a;

                // アトラス sub-sprite の texcoord を 0..1 に正規化
                float2 span = max(_UVRect.zw - _UVRect.xy, float2(1e-5, 1e-5));
                float2 nuv = saturate((i.texcoord - _UVRect.xy) / span);

                // 流れる虹
                float hue = nuv.x * _HueScale - _Time.y * _Speed;
                float3 rgb = lerp(float3(1,1,1), Hue2RGB(hue), _Saturation) * _Brightness;

                // 上下の陰影で少し立体感
                rgb *= 1.0 - _VShade * (abs(nuv.y - 0.5) * 2.0);

                // 流れるハイライト (シャイン)
                float shine = pow(saturate(sin(nuv.x * 3.0 - _Time.y * 1.3)), 8.0) * _Shine;
                rgb += shine;

                // 左→右の充填 (ソフトエッジ) + 先端の光る帯
                float e = max(_EdgeSoftness, 1e-4);
                float fillMask = smoothstep(_Fill + e, _Fill - e, nuv.x);   // nuv.x < _Fill で 1
                float wave = smoothstep(_Fill - e * 2.5, _Fill, nuv.x) * step(nuv.x, _Fill);
                rgb += wave * _EdgeGlow * step(0.0001, _Fill);

                fixed4 col;
                col.rgb = rgb * i.color.rgb;
                col.a = shape * i.color.a * fillMask;
                return col;
            }
            ENDCG
        }
    }
}
