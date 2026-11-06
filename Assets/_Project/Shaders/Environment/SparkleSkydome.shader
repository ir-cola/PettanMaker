Shader "Custom/URP/SparkleSkydome"
{
    // 夕焼け〜トワイライトのスカイドーム。
    // 上=オレンジ / 下=濃い紫・青 のグラデに、オーロラ・白い紙吹雪・
    // 黄色いキラキラ(クリスタル と 五芒星)を重ねる。
    Properties
    {
        _ZenithColor  ("Zenith (top, orange)", Color) = (1.00, 0.60, 0.32, 1)
        _MidColor     ("Mid (pink)", Color)           = (0.88, 0.30, 0.56, 1)
        _HorizonColor ("Horizon (bottom, purple/blue)", Color) = (0.18, 0.13, 0.40, 1)
        _MidPoint     ("Mid Point (unused)", Range(0,1)) = 0.46
        _GradientScale("Gradient Scale", Float) = 2.0
        _PurpleRange  ("Purple Range (bottom)", Range(0,0.6)) = 0.16

        [Header(Aurora)]
        _AuroraColorA  ("Aurora Low (green)", Color)  = (0.20, 0.95, 0.55, 1)
        _AuroraColorB  ("Aurora High (purple)", Color)= (0.55, 0.35, 0.95, 1)
        _AuroraIntensity("Aurora Intensity", Range(0,2)) = 0.6
        _AuroraHeight  ("Aurora Height", Range(0,1)) = 0.55
        _AuroraThickness("Aurora Thickness", Range(0.05,0.6)) = 0.22
        _AuroraWave    ("Aurora Wave", Range(0,0.6)) = 0.18
        _AuroraScale   ("Aurora Flow Scale", Float) = 2.2
        _AuroraStreak  ("Aurora Streaks", Float) = 46

        [Header(White Confetti)]
        _SparkleColor    ("Sparkle Color", Color) = (1, 0.95, 0.96, 1)
        _SparkleDensity  ("Sparkle Density", Float) = 20
        _SparkleSpeed    ("Sparkle Speed", Float) = 0.25
        _SparkleSize     ("Sparkle Size", Range(0.02,0.45)) = 0.085
        _SparkleIntensity("Sparkle Intensity", Range(0,1)) = 0.85

        [Header(Yellow Sparkles (Crystal and Pentagram))]
        _StarColor    ("Star Color", Color)   = (1.0, 0.84, 0.18, 1)
        _StarColor2   ("Crystal Color", Color)= (1.0, 0.92, 0.55, 1)
        _StarOutline  ("Outline", Color)      = (0.20, 0.10, 0.02, 1)
        _StarDensity  ("Star Density", Float) = 13
        _StarSpeed    ("Twinkle Speed", Float) = 1.3
        _StarSize     ("Star Size", Range(0.02,0.5)) = 0.10
        _StarOutlineW ("Outline Width", Range(0,0.4)) = 0.16
        _StarIntensity("Star Amount", Range(0,1)) = 0.95

        _SparkleEmission("Sparkle Glow (HDR)", Range(0,8)) = 2.4
        _StarEmission   ("Star Glow (HDR)",    Range(0,8)) = 3.0

        // ディザ・クロスフェード用（SkydomeBlender が制御）
        [HideInInspector] _Dissolve ("Dissolve", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Opaque" }
        Cull Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "SparkleSkyDome"
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkydomeDissolve.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 worldDir : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor; float4 _MidColor; float4 _HorizonColor; float _MidPoint; float _GradientScale; float _PurpleRange;
                float4 _AuroraColorA; float4 _AuroraColorB; float _AuroraIntensity; float _AuroraHeight; float _AuroraThickness; float _AuroraWave; float _AuroraScale; float _AuroraStreak;
                float4 _SparkleColor; float _SparkleDensity; float _SparkleSpeed; float _SparkleSize; float _SparkleIntensity; float _SparkleEmission;
                float4 _StarColor; float4 _StarColor2; float4 _StarOutline; float _StarDensity; float _StarSpeed; float _StarSize; float _StarOutlineW; float _StarIntensity; float _StarEmission;
                float _Dissolve;
            CBUFFER_END

            #define TAU 6.28318530718
            float  hash21(float2 p){ p=frac(p*float2(123.34,345.45)); p+=dot(p,p+34.345); return frac(p.x*p.y); }
            float2 hash22(float2 p){ float n=hash21(p); return float2(n, hash21(p+n+11.7)); }
            float2 rot2(float2 p, float a){ float s=sin(a),c=cos(a); return float2(p.x*c-p.y*s, p.x*s+p.y*c); }
            float sdTri(float2 p, float r){ const float k=1.7320508; p.x=abs(p.x)-r; p.y=p.y+r/k; if(p.x+k*p.y>0.0) p=float2(p.x-k*p.y,-k*p.x-p.y)*0.5; p.x-=clamp(p.x,-2.0*r,0.0); return -length(p)*sign(p.y); }

            // iq 五芒星SDF(負=内側)
            float sdStar5(float2 p, float r, float rf){
                const float2 k1 = float2(0.809016994375, -0.587785252292);
                const float2 k2 = float2(-k1.x, k1.y);
                p.x = abs(p.x);
                p -= 2.0*max(dot(k1,p),0.0)*k1;
                p -= 2.0*max(dot(k2,p),0.0)*k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf*float2(-k1.y,k1.x) - float2(0.0,1.0);
                float h = clamp(dot(p,ba)/dot(ba,ba), 0.0, r);
                return length(p-ba*h)*sign(p.y*ba.x - p.x*ba.y);
            }
            // 六角形SDF→縦長クリスタル
            float sdHex(float2 p, float r){
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p);
                p -= 2.0*min(dot(k.xy,p),0.0)*k.xy;
                p -= float2(clamp(p.x, -k.z*r, k.z*r), r);
                return length(p)*sign(p.y);
            }
            float sdCrystal(float2 p, float r){ p.x *= 1.9; p.y *= 0.82; return sdHex(p, r); }

            float vnoise(float2 p){
                float2 i=floor(p), f=frac(p); float2 u=f*f*(3.0-2.0*f);
                float a=hash21(i), b=hash21(i+float2(1,0)), c=hash21(i+float2(0,1)), d=hash21(i+float2(1,1));
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float fbm(float2 p){ float v=0.0, a=0.5; for(int i=0;i<4;i++){ v+=a*vnoise(p); p*=2.1; a*=0.5; } return v; }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = _WorldSpaceCameraPos + IN.positionOS.xyz;
                float4 posCS = TransformWorldToHClip(posWS);
                posCS.z = posCS.w;
                OUT.positionCS = posCS;
                OUT.worldDir = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.worldDir);
                float t = _Time.y;

                float phi   = atan2(dir.z, dir.x);
                float theta = acos(clamp(dir.y, -1.0, 1.0));
                float2 uv = float2(phi / TAU + 0.5, theta / 3.14159265);

                // ---- グラデ(下=紫/青 → 中=ピンク → 上=オレンジ)。境目に線が出ないよう
                //      重なり合う smoothstep で3色を連続的に混ぜ、さらにディザでバンディング除去 ----
                // メインは ピンク(下)→オレンジ(上) の1本の滑らかなグラデ(中間に帯=線が出ない)
                float up = saturate(dir.y * _GradientScale);
                float3 col = lerp(_MidColor.rgb, _ZenithColor.rgb, up);
                // 青紫は地平線(dir.y=0)から下=下半球側へ。_PurpleRange の深さで満ちる(=下半球の何割か)
                col = lerp(col, _HorizonColor.rgb, smoothstep(0.02, -_PurpleRange, dir.y));
                // ディザでバンディング(段差線)を除去
                col += (hash21(uv * 1024.0 + frac(t)) - 0.5) * (2.5 / 255.0);

                float skyMask = saturate(dir.y * 3.0 + 0.55);
                float2 buv = float2(uv.x * 2.0, uv.y);

                // ---- オーロラ ----
                float3 acol = 0.0;
                [unroll]
                for (int A = 0; A < 3; A++)
                {
                    float fa = (float)A;
                    float nx = uv.x * _AuroraScale + t * (0.04 + 0.015 * fa);
                    float n = fbm(float2(nx, fa * 7.0 + t * 0.08));
                    float center = _AuroraHeight + (n - 0.5) * _AuroraWave - fa * 0.05;
                    float dd = (dir.y - center) / max(_AuroraThickness, 1e-3);
                    float band = exp(-dd * dd);
                    float streak = 0.55 + 0.45 * sin(uv.x * _AuroraStreak + t * 0.5 + fa * 2.0 + n * 6.0);
                    float inten = band * streak * (0.7 - 0.18 * fa);
                    float vmix = saturate((dir.y - center) / (_AuroraThickness * 2.0) + 0.5);
                    acol += lerp(_AuroraColorA.rgb, _AuroraColorB.rgb, vmix) * inten;
                }
                col += acol * _AuroraIntensity * saturate(dir.y * 2.2);

                // ---- 白い紙吹雪キラキラ(小さめ・2レイヤー) ----
                float conf = 0.0;
                [unroll]
                for (int L = 0; L < 2; L++)
                {
                    float sc = _SparkleDensity * (1.0 + 0.6 * L);
                    float2 p = buv * sc + float2(0.0, t * _SparkleSpeed * (0.6 + 0.4 * L));
                    float2 cell = floor(p);
                    float2 f = frac(p) - 0.5;
                    float present = step(0.55, hash21(cell + 13.1 * (L + 1)));
                    float2 jit = (hash22(cell + 5.0 * L) - 0.5) * 0.7;
                    float ang = hash21(cell + 2.3) * TAU + t * 0.4;
                    float sz = _SparkleSize * (0.6 + 0.7 * hash21(cell + 9.1));
                    float d = sdTri(rot2(f - jit, ang), sz);
                    float aa = fwidth(d) + 1e-4;
                    float shape = 1.0 - smoothstep(-aa, aa, d);
                    float tw = pow(sin(t * _SparkleSpeed * 4.0 + hash21(cell) * TAU) * 0.5 + 0.5, 2.0);
                    conf = max(conf, present * shape * tw);
                }
                float confA = conf * _SparkleIntensity * skyMask;
                col = lerp(col, _SparkleColor.rgb, confA);

                // ---- 黄色いキラキラ: セルごとに クリスタル / 五芒星 ----
                float2 spx = buv * _StarDensity;
                float2 scell = floor(spx);
                float2 sf = frac(spx) - 0.5;
                float spr = step(0.62, hash21(scell + 7.7));
                float2 sjit = (hash22(scell + 3.0) - 0.5) * 0.66;
                float sang = hash21(scell + 1.9) * TAU;
                float twk = pow(sin(t * _StarSpeed + hash21(scell) * TAU) * 0.5 + 0.5, 1.5);
                float ssz = _StarSize * (0.55 + 0.7 * hash21(scell + 4.4)) * (0.5 + 0.5 * twk);
                float2 slp = rot2(sf - sjit, sang);
                float kind = hash21(scell + 6.6);                       // <0.5=五芒星, それ以外=クリスタル
                float sd = (kind < 0.5) ? sdStar5(slp, ssz, 0.45) : sdCrystal(slp, ssz);
                float3 fillCol = (kind < 0.5) ? _StarColor.rgb : _StarColor2.rgb;
                float saa = fwidth(sd) + 1e-4;
                float fillM = 1.0 - smoothstep(0.0, saa, sd);
                float outM  = 1.0 - smoothstep(0.0, saa, sd - _StarOutlineW * ssz);
                float starFill = spr * fillM * _StarIntensity * skyMask;
                float starEdge = spr * saturate(outM - fillM) * _StarIntensity * skyMask;
                col = lerp(col, _StarOutline.rgb, starEdge);
                col = lerp(col, fillCol, starFill);

                // 空のベースは[0,1]にクランプ（Bloomで白飛びさせない）。発光はこの後だけHDRで加算。
                col = saturate(col);
                col += _SparkleColor.rgb * confA * _SparkleEmission;            // 白キラがHDR発光 → Bloom
                col += fillCol * starFill * _StarEmission * (0.6 + 0.8 * twk);  // 星がHDRで瞬く → Bloom

                // ディゾルブ・クロスフェード：溶け際に燃えるウェーブフロントを合成
                return SkydomeDissolveComposite(col, IN.worldDir, IN.positionCS.xy, _Dissolve);
            }
            ENDHLSL
        }
    }
}
