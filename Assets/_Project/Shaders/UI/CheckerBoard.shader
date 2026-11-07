Shader "UI/CheckerBoard"
{
    // チェス盤風のチェック柄。白(ColorA)マスにハートを配置し、ハートに
    // シールのようなホログラム(虹色のイリデッセンス+シーン)をかける。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Tiles    ("Tile Rows", Float) = 7
        _ColorA   ("Color A (white)", Color) = (0.96, 0.93, 0.87, 1)
        _ColorB   ("Color B", Color) = (0.33, 0.26, 0.42, 1)
        _LineColor("Grid Line", Color) = (0.86, 0.74, 0.45, 0.5)
        _LineWidth("Grid Width", Range(0,0.1)) = 0.015
        _Bevel    ("Square Bevel", Range(0,1)) = 0.5
        _Vignette ("Vignette", Range(0,2)) = 0.6

        _HeartScale ("Heart Scale", Range(0.3,1.2)) = 0.82
        _HeartColor ("Heart Base", Color) = (1.0, 0.92, 0.95, 1)
        _HoloSat    ("Holo Saturation", Range(0,1)) = 0.55
        _HoloSpeed  ("Holo Speed", Float) = 0.18
        _HoloScale  ("Holo Scale", Float) = 0.6
        _HeartSheen ("Heart Sheen", Range(0,1)) = 0.6

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; float4 worldPosition:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float4 _ClipRect;
            float _Tiles; fixed4 _ColorA; fixed4 _ColorB; fixed4 _LineColor; float _LineWidth; float _Bevel; float _Vignette;
            float _HeartScale; fixed4 _HeartColor; float _HoloSat; float _HoloSpeed; float _HoloScale; float _HeartSheen;

            #define TAU 6.28318530718
            float dot2(float2 v){ return dot(v,v); }
            float3 hsv2rgb(float3 c){
                float3 rgb = clamp(abs(fmod(c.x*6.0 + float3(0.0,4.0,2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
                return c.z * lerp(float3(1,1,1), rgb, c.y);
            }
            // iq heart, point down / lobes up, negative inside
            float sdHeartRaw(float2 p){
                p.x = abs(p.x);
                if (p.y + p.x > 1.0) return sqrt(dot2(p - float2(0.25,0.75))) - sqrt(2.0)/4.0;
                return sqrt(min(dot2(p - float2(0.0,1.0)), dot2(p - 0.5*max(p.x+p.y,0.0)))) * sign(p.x - p.y);
            }
            float heartSDF(float2 q){ // q ~ [-0.5,0.5]
                float s = 0.70;
                float2 p = q / s + float2(0.0, 0.55);
                return sdHeartRaw(p) * s;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 g = float2(uv.x * aspect, uv.y) * max(_Tiles, 1.0);
                float2 cell = floor(g);
                float2 f = frac(g);

                float checker = fmod(cell.x + cell.y, 2.0);
                half3 col = lerp(_ColorA.rgb, _ColorB.rgb, checker);

                // ベベル
                float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
                float bevel = smoothstep(0.0, 0.07, edge);
                col *= lerp(1.0 - 0.12 * _Bevel, 1.0, bevel);

                // 格子線
                float gl = 1.0 - smoothstep(0.0, max(_LineWidth, 1e-4), edge);
                col = lerp(col, _LineColor.rgb, gl * _LineColor.a);

                // 白(ColorA)マスにホログラムハート
                if (checker < 0.5) {
                    float2 q = (f - 0.5) / max(_HeartScale, 0.01);
                    float hd = heartSDF(q) * _HeartScale;
                    float aa = clamp(fwidth(hd), 0.001, 0.05);
                    float heartMask = 1.0 - smoothstep(-aa, aa, hd);

                    float2 pp = float2(uv.x * aspect, uv.y);
                    float hue = frac((pp.x + pp.y) * _HoloScale + _Time.y * _HoloSpeed);
                    float3 rainbow = hsv2rgb(float3(hue, _HoloSat, 1.0));
                    float3 heartCol = lerp(_HeartColor.rgb, rainbow, 0.55);
                    // シーン(箔の光沢)
                    float sw = sin((pp.x - pp.y * 0.7 + _Time.y * _HoloSpeed * 3.0) * TAU) * 0.5 + 0.5;
                    heartCol += smoothstep(0.72, 1.0, sw) * _HeartSheen;
                    // 内側の陰影で立体感
                    heartCol *= lerp(0.85, 1.08, smoothstep(0.0, -0.25, hd));

                    col = lerp(col, saturate(heartCol), heartMask);
                }

                // ビネット
                float2 d = uv - 0.5;
                float vig = saturate(1.0 - dot(d, d) * _Vignette);
                col *= vig;

                half4 outc = half4(saturate(col), 1.0) * half4(IN.color.rgb, IN.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                outc.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(outc.a - 0.001);
                #endif
                return outc;
            }
            ENDCG
        }
    }
}
