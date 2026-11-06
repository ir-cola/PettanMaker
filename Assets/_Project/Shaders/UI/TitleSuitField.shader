Shader "UI/TitleSuitField"
{
    Properties
    {
        // UGUI 必須(手続き生成のためサンプルはしないが、プロパティの存在が必要)
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // ---- タイリング / 回転 ----
        _Tiles            ("Tile Rows", Float) = 6
        _SpinSpeed        ("Spin Speed", Float) = 0.9
        _SpinWave         ("Spin Wave (per cell)", Float) = 0.45
        _SpinPhaseRandom  ("Spin Phase Random", Range(0,1)) = 0

        // ---- スートの色 / 合成 ----
        [Toggle] _ClassicColors ("Classic Red/Black", Float) = 0
        _SuitColor  ("Suit Color (Mono)", Color) = (1.0, 0.93, 0.78, 1)
        _RedColor   ("Classic Red", Color)       = (0.82, 0.10, 0.16, 1)
        _BlackColor ("Classic Black", Color)     = (0.09, 0.09, 0.12, 1)
        _SuitAlpha  ("Suit Alpha", Range(0,1)) = 0.5
        _GlowStrength ("Inner Glow", Range(0,1)) = 0.35
        _BGColor    ("Background (between suits)", Color) = (0,0,0,0)

        // ---- キラキラ(控えめ) ----
        _SparkleColor     ("Sparkle Color", Color) = (1.0, 0.90, 0.60, 1)
        _SparkleIntensity ("Sparkle Intensity", Range(0,1)) = 0.35
        _SparkleDensity   ("Sparkle Density", Float) = 14
        _SparkleSpeed     ("Sparkle Speed", Float) = 3.0

        // ---- 面のグリント(控えめ) ----
        _GlintColor     ("Glint Color", Color) = (1.0, 0.95, 0.70, 1)
        _GlintIntensity ("Glint Intensity", Range(0,1)) = 0.25
        _GlintSpeed     ("Glint Speed", Float) = 1.5

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

            // _MainTex はサンプルしない(完全手続き生成)。フォント/テキスト用の
            // _TextureSampleAdd 経路は意図的に省略している。
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float4 _ClipRect;

            float  _Tiles;
            float  _SpinSpeed;
            float  _SpinWave;
            float  _SpinPhaseRandom;
            float  _ClassicColors;
            fixed4 _SuitColor;
            fixed4 _RedColor;
            fixed4 _BlackColor;
            float  _SuitAlpha;
            float  _GlowStrength;
            fixed4 _BGColor;
            fixed4 _SparkleColor;
            float  _SparkleIntensity;
            float  _SparkleDensity;
            float  _SparkleSpeed;
            fixed4 _GlintColor;
            float  _GlintIntensity;
            float  _GlintSpeed;

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

            #define TAU 6.28318530718

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float dot2(float2 v) { return dot(v, v); }

            // iq heart SDF: 先端は下(y=0)、ふくらみは上(y~1)、negative = 内側
            float sdHeartRaw(float2 p)
            {
                p.x = abs(p.x);
                if (p.y + p.x > 1.0)
                    return sqrt(dot2(p - float2(0.25, 0.75))) - sqrt(2.0) / 4.0;
                return sqrt(min(dot2(p - float2(0.0, 1.0)),
                                dot2(p - 0.5 * max(p.x + p.y, 0.0)))) * sign(p.x - p.y);
            }

            // iq exact triangle SDF (negative = 内側)
            float sdTriangle(float2 p, float2 p0, float2 p1, float2 p2)
            {
                float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
                float2 v0 = p - p0,  v1 = p - p1,  v2 = p - p2;
                float2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
                float2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
                float2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
                float s = sign(e0.x * e2.y - e0.y * e2.x);
                float2 d = min(min(float2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                                   float2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                                   float2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
                return -sqrt(d.x) * sign(d.y);
            }

            float sdCircle(float2 p, float2 c, float r) { return length(p - c) - r; }

            // ---- 各スート(q はセルローカル[-0.5,0.5]、~±0.45 に収まる) ----
            float suitHeart(float2 q)
            {
                float s = 0.70;
                float2 p = q / s + float2(0.0, 0.55);
                return sdHeartRaw(p) * s;     // ハート(ふくらみ上・先端下)
            }
            float suitSpade(float2 q)
            {
                float body = suitHeart(float2(q.x, -q.y));  // 上下反転=先端上
                float stem = sdTriangle(q, float2(0.0, -0.28), float2(-0.13, -0.47), float2(0.13, -0.47));
                return min(body, stem);
            }
            float suitDiamond(float2 q)
            {
                const float k = 1.35;
                return (abs(q.x) * k + abs(q.y) - 0.42) / sqrt(k * k + 1.0); // 正規化(AA均一化)
            }
            float suitClub(float2 q)
            {
                float r   = 0.20;
                float top = sdCircle(q, float2( 0.00,  0.18), r);
                float bl  = sdCircle(q, float2(-0.19, -0.05), r);
                float br  = sdCircle(q, float2( 0.19, -0.05), r);
                float lobes = min(top, min(bl, br));
                float stem  = sdTriangle(q, float2(0.0, -0.02), float2(-0.12, -0.42), float2(0.12, -0.42));
                return min(lobes, stem);
            }

            // 0=スペード 1=ハート 2=クラブ 3=ダイヤ
            float suitSDF(float idx, float2 p)
            {
                if (idx < 0.5)      return suitSpade(p);
                else if (idx < 1.5) return suitHeart(p);
                else if (idx < 2.5) return suitClub(p);
                else                return suitDiamond(p);
            }

            half4 ProceduralSuits(float2 uv)
            {
                // 画面に対して正方形セルになるようアスペクト補正(全画面オーバーレイ前提。
                // 部分矩形に使う場合は _ScreenParams ではなく矩形サイズで割る必要がある)
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float rows = max(_Tiles, 1.0);
                float cols = rows * aspect;

                float2 cell   = float2(uv.x * cols, uv.y * rows);
                float2 cellId = floor(cell);
                float2 local  = frac(cell) - 0.5;            // [-0.5,0.5]

                // --- 2x2 ブロックで4種スートを必ず含める ---
                float2 par = fmod(abs(cellId), 2.0);         // 0 or 1(負UVでも安全)
                float idx  = par.x + par.y * 2.0;            // 0..3

                // --- Y軸回転(擬似3D:水平方向に潰す) ---
                // 位相はセル位置で揃った対角ウェーブ。_SpinWave=0 で全カード完全同期、
                // _SpinPhaseRandom>0 でランダム性を足せる(既定は規則的に揃う)。
                float phase = (cellId.x + cellId.y) * _SpinWave
                            + hash21(cellId) * TAU * _SpinPhaseRandom;
                float angle = _Time.y * _SpinSpeed + phase;
                float c      = cos(angle);
                float csign  = (c < 0.0) ? -1.0 : 1.0;
                float cguard = csign * max(abs(c), 0.06);    // 0除算回避・符号維持
                float cardX  = (local.x / cguard) * 0.99;    // 隣接カードの隙間を防ぐ微小インセット

                // --- カード端マスク(クランプfwidthでAA) ---
                float caa = clamp(fwidth(cardX), 0.001, 0.05);
                float cardMask = 1.0 - smoothstep(0.5 - caa, 0.5 + caa, abs(cardX));

                // --- スートSDF(表=塗り / 裏=輪郭) ---
                float2 sp = float2(cardX, local.y);
                float d = suitSDF(idx, sp);
                float aa = clamp(fwidth(d), 0.001, 0.05);
                float suitFill = 1.0 - smoothstep(-aa, aa, d);                 // 表面:塗り
                float suitRing = 1.0 - smoothstep(-aa, aa, abs(d) - 0.030);    // 裏面:輪郭(浮き彫り風)

                float faceSel = step(0.0, c);                 // 1=表, 0=裏
                float inkMask = lerp(suitRing, suitFill, faceSel) * cardMask;

                // --- 色 ---
                float redSel = step(0.5, fmod(idx, 2.0));      // ハート(1)・ダイヤ(3)=赤
                float3 classicCol = lerp(_BlackColor.rgb, _RedColor.rgb, redSel);
                float3 baseRGB = lerp(_SuitColor.rgb, classicCol, saturate(_ClassicColors));

                // 面の陰影:エッジオンで暗く、裏面はさらに少し暗く(立体感)
                float shade = lerp(0.62, 1.0, abs(c)) * lerp(0.82, 1.0, faceSel);
                float3 inkRGB = baseRGB * shade;

                // 内側エッジの淡い発光(エッチドグラス風、白飛びは最後にクランプ)
                float rim = 1.0 - smoothstep(0.0, 0.09, -d);
                inkRGB *= (1.0 + _GlowStrength * rim * faceSel);

                // カードごとの控えめなグリント(画面一括ではなくカードローカル)
                float gph = (cardX + local.y) * 1.5 + _Time.y * _GlintSpeed + hash21(cellId) * TAU;
                float gband = smoothstep(0.86, 1.0, sin(gph) * 0.5 + 0.5);
                inkRGB += _GlintColor.rgb * (gband * _GlintIntensity * abs(c) * faceSel);

                float inkA = inkMask * _SuitAlpha;

                // --- 背景(スート間)へ重ねる(ストレートアルファ) ---
                float4 bg = _BGColor;
                float outA = inkA + bg.a * (1.0 - inkA);
                float3 outRGB = (inkRGB * inkA + bg.rgb * bg.a * (1.0 - inkA)) / max(outA, 1e-4);
                half4 col = half4(outRGB, outA);

                // --- 控えめなキラキラ(スート面に限定) ---
                float2 sUv = float2(uv.x * aspect, uv.y) * max(_SparkleDensity, 1.0);
                float2 sId = floor(sUv);
                float2 sLoc = frac(sUv) - 0.5;
                float sh = hash21(sId);
                float sparkOn = step(0.68, hash21(sId + float2(11.3, 4.7)));   // 約32%のセルのみ
                float2 jit = (float2(hash21(sId + float2(1.7, 9.2)), hash21(sId + float2(5.3, 2.1))) - 0.5) * 0.7;
                float2 q = sLoc - jit;
                float tw = pow(sin(_Time.y * _SparkleSpeed + sh * TAU) * 0.5 + 0.5, 4.0); // 鋭く疎な明滅
                float star   = smoothstep(0.16, 0.0, length(q));
                float crossH = smoothstep(0.018, 0.0, abs(q.y)) * smoothstep(0.30, 0.0, abs(q.x));
                float crossV = smoothstep(0.018, 0.0, abs(q.x)) * smoothstep(0.30, 0.0, abs(q.y));
                float shape  = saturate(star + (crossH + crossV) * 0.6);
                float gate   = cardMask * (0.15 + 0.85 * suitFill);  // スート面中心に限定(控えめ)
                float sparkleAmt = sparkOn * tw * shape * _SparkleIntensity * gate;
                col.rgb += _SparkleColor.rgb * sparkleAmt;
                col.a = saturate(col.a + sparkleAmt * _SparkleColor.a);

                col.rgb = saturate(col.rgb);  // 加算成分の白飛び抑制
                return col;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = ProceduralSuits(IN.texcoord);
                color *= IN.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
