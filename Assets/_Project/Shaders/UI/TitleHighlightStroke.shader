// ============================================================================
//  TitleHighlightStroke.shader
//  Author : Hayato Otsuki
//  Date   : 2026/6/29
// ----------------------------------------------------------------------------
//  タイトルのカーソル・ハイライトをフラグメントで「プロシージャル生成」する UI シェーダー。
//
//  - クワッド(UV 0..1, x=左→右)に対し、横棒(平行筆跡)を SDF 的に塗る。
//  - _Fill (0..1): ローラーが左から右へ転がって塗り進めた位置。x<=_Fill だけ描く。
//  - テーパー: ローラー側(右/先端)ほど細い。_WideHalf(左) → _ThinHalf(ローラー)。
//  - _Skew: x に応じて y をずらすシアー(平行四辺形風の傾き)。
//  - 色は頂点カラー(Graphic.color)。URP の Screen-Space UI でもそのまま動く CG パス。
// ============================================================================
Shader "UI/TitleHighlightStroke"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color      ("Tint", Color) = (1,1,1,1)
        _Fill       ("Fill (roller progress)", Range(0,1)) = 1
        _WideHalf   ("Wide Half Height (left)", Range(0,0.5)) = 0.46
        _ThinHalf   ("Thin Half Height (roller/right point)", Range(0,0.5)) = 0.02
        _Softness   ("Edge Softness", Range(0.0001,0.2)) = 0.03
        _TipSoftness("Leading Tip Softness", Range(0.0001,0.3)) = 0.06
        _Skew       ("Skew (shear)", Range(-0.6,0.6)) = 0.12
        _TailSlant  ("Parallelogram slant (L/R edges)", Range(-1.5,1.5)) = 0.3
        _ShadowColor  ("Shadow/Mask Color", Color) = (0,0,0,1)
        _ShadowOffset ("Shadow Offset (uv)", Vector) = (0.012, -0.07, 0, 0)

        // --- 標準 UI ステンシル/マスク プロパティ ---
        _StencilComp      ("Stencil Comparison", Float) = 8
        _Stencil          ("Stencil ID", Float) = 0
        _StencilOp        ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask", Float) = 255
        _ColorMask        ("Color Mask", Float) = 15
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
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float  _Fill;
            float  _WideHalf;
            float  _ThinHalf;
            float  _Softness;
            float  _TipSoftness;
            float  _Skew;
            float  _TailSlant;
            fixed4 _ShadowColor;
            float4 _ShadowOffset;
            float4 _ClipRect;

            // 平行四辺形の筆跡シェイプのアルファ (テーパー/塗り進み/平行四辺形)。
            // ズレ影レイヤー(重ね影)用に座標をずらして2回評価できるよう関数化。
            float ShapeAlpha(float2 p)
            {
                float y0 = p.y;
                float uy = p.y + (p.x - 0.5) * _Skew;
                float halfH = lerp(_WideHalf, _ThinHalf, p.x);
                float maxHalf = max(0.5 - 0.01 - 0.5 * abs(_Skew), 0.0);
                halfH = min(halfH, maxHalf);
                float dy = abs(uy - 0.5);
                float fy = fwidth(p.y);
                float bar = 1.0 - smoothstep(halfH - fy, halfH + fy, dy);
                float inset  = 0.5 * abs(_TailSlant);
                float slant  = _TailSlant * (y0 - 0.5);
                float leftX  = inset + slant;
                float rightX = lerp(inset, 1.0 - inset, _Fill) + slant;
                float fx = fwidth(p.x);
                float aLeft  = smoothstep(leftX  - fx, leftX  + fx, p.x);
                float aRight = 1.0 - smoothstep(rightX - fx, rightX + fx, p.x);
                return saturate(bar * aLeft * aRight);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // ピンク本体と、UVをずらした「重ね影(マスク)」を1パスで合成。
                float aPink   = ShapeAlpha(uv);
                float aShadow = ShapeAlpha(uv - _ShadowOffset.xy);
                float a = max(aPink, aShadow);
                float k = (a > 1e-5) ? saturate(aPink / a) : 0.0;   // ピンクが上、影は下にはみ出す

                fixed4 col;
                col.rgb = lerp(_ShadowColor.rgb, IN.color.rgb, k);
                col.a   = a * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
