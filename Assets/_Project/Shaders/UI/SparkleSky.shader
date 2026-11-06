Shader "UI/SparkleSky"
{
    // 夕焼け空(オレンジ〜ピンク/マゼンタのグラデ＋中央の発光)に、
    // 白い三角の紙吹雪キラキラと、黄色い4芒星(縁取り付き)のキラキラを乗せる。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // ---- グラデーション ----
        _TopColor    ("Top (sky)", Color)    = (1.00, 0.62, 0.42, 1)
        _MidColor    ("Mid", Color)          = (0.98, 0.36, 0.52, 1)
        _BottomColor ("Bottom", Color)       = (0.86, 0.26, 0.50, 1)
        _MidPoint    ("Mid Point", Range(0,1)) = 0.5
        _GlowColor   ("Center Glow", Color)  = (1.0, 0.32, 0.55, 0.55)
        _GlowCenter  ("Glow Center", Vector) = (0.5, 0.45, 0, 0)
        _GlowSize    ("Glow Size", Float)    = 0.13

        // ---- 白い紙吹雪キラキラ ----
        _SparkleColor    ("Sparkle Color", Color) = (1, 0.95, 0.96, 1)
        _SparkleDensity  ("Sparkle Density", Float) = 10
        _SparkleSpeed    ("Sparkle Speed", Float) = 0.6
        _SparkleSize     ("Sparkle Size", Range(0.05,0.45)) = 0.16
        _SparkleIntensity("Sparkle Intensity", Range(0,1)) = 0.85

        // ---- 黄色い星キラキラ ----
        _StarColor    ("Star Color", Color)   = (1.0, 0.84, 0.18, 1)
        _StarOutline  ("Star Outline", Color) = (0.20, 0.10, 0.02, 1)
        _StarDensity  ("Star Density", Float) = 5
        _StarSpeed    ("Star Twinkle Speed", Float) = 1.4
        _StarSize     ("Star Size", Range(0.05,0.5)) = 0.22
        _StarOutlineW ("Star Outline Width", Range(0,0.3)) = 0.10
        _StarIntensity("Star Amount", Range(0,1)) = 0.9

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
            fixed4 _TopColor, _MidColor, _BottomColor; float _MidPoint; fixed4 _GlowColor; float4 _GlowCenter; float _GlowSize;
            fixed4 _SparkleColor; float _SparkleDensity, _SparkleSpeed, _SparkleSize, _SparkleIntensity;
            fixed4 _StarColor, _StarOutline; float _StarDensity, _StarSpeed, _StarSize, _StarOutlineW, _StarIntensity;

            #define TAU 6.28318530718
            float  hash21(float2 p){ p=frac(p*float2(123.34,345.45)); p+=dot(p,p+34.345); return frac(p.x*p.y); }
            float2 hash22(float2 p){ float n=hash21(p); return float2(n, hash21(p+n+11.7)); }
            float2 rot(float2 p, float a){ float s=sin(a),c=cos(a); return float2(p.x*c-p.y*s, p.x*s+p.y*c); }

            // iq 正三角形SDF(負=内側)
            float sdTri(float2 p, float r){
                const float k = 1.7320508;
                p.x = abs(p.x) - r;
                p.y = p.y + r/k;
                if (p.x + k*p.y > 0.0) p = float2(p.x - k*p.y, -k*p.x - p.y)/2.0;
                p.x -= clamp(p.x, -2.0*r, 0.0);
                return -length(p)*sign(p.y);
            }
            // 4芒星(アステロイド)SDF風(負=内側)
            float sdStar4(float2 p, float r){
                p = abs(p);
                float v = pow(p.x, 0.6667) + pow(p.y, 0.6667);
                return v - pow(max(r,1e-4), 0.6667);
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
                float t = _Time.y;

                // ===== グラデーション =====
                float3 col;
                if (uv.y < _MidPoint) col = lerp(_BottomColor.rgb, _MidColor.rgb, uv.y / max(_MidPoint,1e-4));
                else                  col = lerp(_MidColor.rgb, _TopColor.rgb, (uv.y - _MidPoint) / max(1.0 - _MidPoint,1e-4));
                float2 gc = uv - _GlowCenter.xy; gc.x *= aspect;
                float glow = exp(-dot(gc, gc) / max(_GlowSize, 1e-4));
                col = lerp(col, _GlowColor.rgb, saturate(glow) * _GlowColor.a);

                // ===== 白い紙吹雪キラキラ(2レイヤー) =====
                float conf = 0.0;
                for (int L = 0; L < 2; L++) {
                    float sc = _SparkleDensity * (1.0 + 0.6 * L);
                    float2 p = float2(uv.x * aspect, uv.y) * sc;
                    p += float2(0.0, t * _SparkleSpeed * (0.6 + 0.4 * L)); // ゆっくり流れる
                    float2 cell = floor(p);
                    float2 f = frac(p) - 0.5;
                    float present = step(0.55, hash21(cell + 13.1 * (L + 1)));
                    float2 jit = (hash22(cell + 5.0 * L) - 0.5) * 0.7;
                    float ang = hash21(cell + 2.3) * TAU + t * 0.5;
                    float sz = _SparkleSize * (0.6 + 0.7 * hash21(cell + 9.1));
                    float d = sdTri(rot(f - jit, ang), sz);
                    float aa = fwidth(d) + 1e-4;
                    float shape = 1.0 - smoothstep(-aa, aa, d);
                    float tw = pow(sin(t * _SparkleSpeed * 3.0 + hash21(cell) * TAU) * 0.5 + 0.5, 2.0);
                    conf = max(conf, present * shape * tw);
                }
                col = lerp(col, _SparkleColor.rgb, conf * _SparkleIntensity);

                // ===== 黄色い星キラキラ =====
                float starFill = 0.0, starEdge = 0.0;
                float2 sp = float2(uv.x * aspect, uv.y) * _StarDensity;
                float2 scell = floor(sp);
                float2 sf = frac(sp) - 0.5;
                float spr = step(0.62, hash21(scell + 7.7));
                float2 sjit = (hash22(scell + 3.0) - 0.5) * 0.66;
                float sang = hash21(scell + 1.9) * TAU;
                float twk = pow(sin(t * _StarSpeed + hash21(scell) * TAU) * 0.5 + 0.5, 1.5); // またたき
                float ssz = _StarSize * (0.55 + 0.7 * hash21(scell + 4.4)) * (0.5 + 0.5 * twk);
                float sd = sdStar4(rot(sf - sjit, sang), ssz);
                float saa = fwidth(sd) + 1e-4;
                float fillM = 1.0 - smoothstep(0.0, saa, sd);
                float outM  = 1.0 - smoothstep(0.0, saa, sd - _StarOutlineW * ssz);
                starFill = spr * fillM * _StarIntensity;
                starEdge = spr * saturate(outM - fillM) * _StarIntensity;
                col = lerp(col, _StarOutline.rgb, starEdge);
                col = lerp(col, _StarColor.rgb, starFill);

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
