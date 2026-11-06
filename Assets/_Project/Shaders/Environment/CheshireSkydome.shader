Shader "Custom/URP/CheshireSkydome"
{
    // Boss01（チェシャ猫）戦用スカイドーム
    //
    // 機能概要:
    //   - 藍 → 紫 → ラベンダーの縦グラデーション（ボス部屋のフォグ色に馴染む）
    //   - 三日月（円SDFの差分。チェシャ猫の笑みを暗喩する傾き）+ トゥーンハロ
    //   - トゥーン夜雲（FBM 1層 + ディテールノイズ。月側にシルバーライニング）
    //   - 瞬く星（Kenny star テクスチャ4種をセル散布）
    //   - 宙に浮かぶ窓（Kenny window テクスチャ4種。ゆっくり現れては消える）
    //   - ACES トーンマッピング
    //   - SkydomeBlendSystem による _Dissolve クロスフェード対応

    Properties
    {
        [Header(Sky Gradient)]
        _ZenithColor  ("Zenith Color (top)",     Color) = (0.045, 0.025, 0.140, 1)
        _MidColor     ("Mid Color",              Color) = (0.240, 0.120, 0.420, 1)
        _HorizonColor ("Horizon Color (bottom)", Color) = (0.720, 0.550, 0.950, 1)
        _MidPoint     ("Mid Height", Range(0.05, 0.9)) = 0.35
        _Exposure     ("Exposure", Float) = 1.0

        [Header(Crescent Moon)]
        _MoonDir       ("Moon Direction (xyz)", Vector) = (0.35, 0.50, 0.60, 0)
        _MoonSize      ("Moon Size",       Range(0.01, 0.30)) = 0.09
        _MoonColor     ("Moon Color",      Color) = (1.00, 0.93, 0.70, 1)
        _MoonEdgeColor ("Moon Edge Color", Color) = (0.95, 0.70, 0.45, 1)
        _CrescentBite  ("Crescent Bite (0=full moon)", Range(0, 1)) = 0.55
        _CrescentAngle ("Crescent Angle (deg)", Range(0, 360)) = 130
        _MoonGlow      ("Moon Glow Intensity", Range(0, 4)) = 1.2
        _MoonGlowColor ("Moon Glow Color", Color) = (0.80, 0.60, 1.00, 1)
        [NoScaleOffset] _MoonHaloTex ("Moon Halo Tex (light_01)", 2D) = "black" {}
        _MoonHaloScale ("Moon Halo Scale", Range(2, 16)) = 7.0

        [Header(Clouds)]
        _CloudCoverage    ("Cloud Coverage",  Range(0, 1)) = 0.45
        _CloudDensity     ("Cloud Density",   Range(0, 1)) = 0.85
        _CloudScale       ("Cloud Scale",     Range(0.5, 6)) = 1.0
        _CloudSpeed       ("Cloud Speed",     Float) = 0.6
        _CloudSharpness   ("Cloud Sharpness", Range(0, 1)) = 0.6
        _CloudLitColor    ("Cloud Lit Color",    Color) = (0.62, 0.50, 0.88, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.14, 0.09, 0.28, 1)

        [Header(Stars (Kenny star))]
        [NoScaleOffset] _StarTexA ("Star Tex A", 2D) = "black" {}
        [NoScaleOffset] _StarTexB ("Star Tex B", 2D) = "black" {}
        [NoScaleOffset] _StarTexC ("Star Tex C", 2D) = "black" {}
        [NoScaleOffset] _StarTexD ("Star Tex D", 2D) = "black" {}
        _StarGridScale ("Star Grid Scale", Range(4, 32))   = 14
        _StarAmount    ("Star Amount",     Range(0, 1))    = 0.55
        _StarSize      ("Star Size",       Range(0.05, 1)) = 0.35
        _StarColor     ("Star Color",      Color) = (1.0, 0.95, 0.85, 1)
        _StarIntensity ("Star Intensity (HDR)", Range(0, 6)) = 2.0
        _TwinkleSpeed  ("Twinkle Speed", Float) = 1.4

        [Header(Floating Windows (Kenny window))]
        [NoScaleOffset] _WindowTexA ("Window Tex A", 2D) = "black" {}
        [NoScaleOffset] _WindowTexB ("Window Tex B", 2D) = "black" {}
        [NoScaleOffset] _WindowTexC ("Window Tex C", 2D) = "black" {}
        [NoScaleOffset] _WindowTexD ("Window Tex D", 2D) = "black" {}
        _WindowGridScale  ("Window Grid Scale", Range(2, 12))   = 5
        _WindowAmount     ("Window Amount",     Range(0, 1))    = 0.25
        _WindowSize       ("Window Size",       Range(0.05, 1)) = 0.30
        _WindowColor      ("Window Color",      Color) = (1.00, 0.82, 0.45, 1)
        _WindowIntensity  ("Window Intensity (HDR)", Range(0, 6)) = 1.6
        _WindowDriftSpeed ("Window Drift Speed", Float) = 0.015
        _WindowCycleSpeed ("Window Appear Cycle Speed", Float) = 0.25

        // ディザ・クロスフェード用（SkydomeBlendSystem が制御）
        [HideInInspector] _Dissolve ("Dissolve", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Background"
            "RenderType"     = "Opaque"
        }

        // 球の内側を描画するため前面カリング、深度書き込みなし
        Cull  Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "CheshireSkyDome"
            HLSLPROGRAM

            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkydomeDissolve.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldDir   : TEXCOORD0;
            };

            TEXTURE2D(_MoonHaloTex);
            TEXTURE2D(_StarTexA);
            TEXTURE2D(_StarTexB);
            TEXTURE2D(_StarTexC);
            TEXTURE2D(_StarTexD);
            TEXTURE2D(_WindowTexA);
            TEXTURE2D(_WindowTexB);
            TEXTURE2D(_WindowTexC);
            TEXTURE2D(_WindowTexD);
            // 全テクスチャで共有するインラインサンプラー（クランプで縁のにじみ防止）
            SAMPLER(sampler_linear_clamp);

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _MidColor;
                float4 _HorizonColor;
                float  _MidPoint;
                float  _Exposure;
                float4 _MoonDir;
                float  _MoonSize;
                float4 _MoonColor;
                float4 _MoonEdgeColor;
                float  _CrescentBite;
                float  _CrescentAngle;
                float  _MoonGlow;
                float4 _MoonGlowColor;
                float  _MoonHaloScale;
                float  _CloudCoverage;
                float  _CloudDensity;
                float  _CloudScale;
                float  _CloudSpeed;
                float  _CloudSharpness;
                float4 _CloudLitColor;
                float4 _CloudShadowColor;
                float  _StarGridScale;
                float  _StarAmount;
                float  _StarSize;
                float4 _StarColor;
                float  _StarIntensity;
                float  _TwinkleSpeed;
                float  _WindowGridScale;
                float  _WindowAmount;
                float  _WindowSize;
                float4 _WindowColor;
                float  _WindowIntensity;
                float  _WindowDriftSpeed;
                float  _WindowCycleSpeed;
                float  _Dissolve;
            CBUFFER_END


            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash2(i + float2(0, 0)).x;
                float b = hash2(i + float2(1, 0)).x;
                float c = hash2(i + float2(0, 1)).x;
                float d = hash2(i + float2(1, 1)).x;
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v   = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v   += amp * noise(p);
                    p   *= 2.1;
                    amp *= 0.5;
                }
                return v;
            }

            // 4枚のスプライトを常にサンプルして idx で選択
            // （分岐サンプリングによる微分不定を避けるため全枚サンプル）
            float SampleStarMask(float2 uv, int idx)
            {
                float4 s;
                s.x = SAMPLE_TEXTURE2D(_StarTexA, sampler_linear_clamp, uv).a;
                s.y = SAMPLE_TEXTURE2D(_StarTexB, sampler_linear_clamp, uv).a;
                s.z = SAMPLE_TEXTURE2D(_StarTexC, sampler_linear_clamp, uv).a;
                s.w = SAMPLE_TEXTURE2D(_StarTexD, sampler_linear_clamp, uv).a;
                float4 w = float4(idx == 0, idx == 1, idx == 2, idx == 3);
                return dot(s, w);
            }

            float SampleWindowMask(float2 uv, int idx)
            {
                float4 s;
                s.x = SAMPLE_TEXTURE2D(_WindowTexA, sampler_linear_clamp, uv).a;
                s.y = SAMPLE_TEXTURE2D(_WindowTexB, sampler_linear_clamp, uv).a;
                s.z = SAMPLE_TEXTURE2D(_WindowTexC, sampler_linear_clamp, uv).a;
                s.w = SAMPLE_TEXTURE2D(_WindowTexD, sampler_linear_clamp, uv).a;
                float4 w = float4(idx == 0, idx == 1, idx == 2, idx == 3);
                return dot(s, w);
            }

            // 球面方向 → 経緯度 UV（WonderSkydome のスート散布と同方式）
            float2 SphereUV(float3 dir)
            {
                float phi   = atan2(dir.z, dir.x);
                float theta = acos(clamp(dir.y, -1.0, 1.0));
                return float2(phi / (2.0 * PI) + 0.5, theta / PI);
            }

            // ACES フィルミック トーンマッピング
            float3 ACES(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }


            // ---- 三日月 ----
            // moonMask: 月ディスク近傍で 0 になるマスク（星・窓が月に重ならないように）
            // cloudSilver 用に月方向も out で返す
            float3 CrescentMoon(float3 dir, float3 sky, float3 mdir, out float moonMask)
            {
                float cosT = dot(dir, mdir);
                moonMask = 1.0;
                if (cosT <= 0.0) return sky; // 月の反対側は何もしない

                // 月中心の接平面 2D 座標（r ≒ 月中心からの角距離）
                float3 up    = abs(mdir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 right = normalize(cross(mdir, up));
                float3 upDir = cross(right, mdir);
                float3 perp  = dir - mdir * cosT;
                float2 mp    = float2(dot(perp, right), dot(perp, upDir));
                float  r     = length(mp);

                // 本体円 − 欠け円 = 三日月。_CrescentAngle で笑みの傾きを決める
                float aa   = _MoonSize * 0.03; // エッジのアンチエイリアス幅
                float disk = 1.0 - smoothstep(_MoonSize - aa, _MoonSize + aa, r);

                float  ca         = radians(_CrescentAngle);
                float2 biteCenter = float2(cos(ca), sin(ca)) * _MoonSize * _CrescentBite * 1.1;
                float  biteR      = _MoonSize * 0.92;
                float  rb         = length(mp - biteCenter);
                float  bite       = 1.0 - smoothstep(biteR - aa, biteR + aa, rb);

                float crescent = saturate(disk - bite);

                // 欠け際に暖色のエッジを一段入れるトゥーン塗り分け
                float  edgeBand  = 1.0 - smoothstep(biteR, biteR * 1.35, rb);
                float3 moonFace  = lerp(_MoonColor.rgb, _MoonEdgeColor.rgb, edgeBand * 0.8);

                // 月の周囲に加算グロー（プロシージャル）+ ハロテクスチャ（light_01）
                float glow = pow(saturate(1.0 - r / (_MoonSize * 3.5)), 3.0);

                float2 haloUV  = mp / (_MoonSize * _MoonHaloScale) + 0.5;
                float  inHalo  = step(0.0, haloUV.x) * step(haloUV.x, 1.0)
                               * step(0.0, haloUV.y) * step(haloUV.y, 1.0);
                float  halo    = SAMPLE_TEXTURE2D(_MoonHaloTex, sampler_linear_clamp, haloUV).a * inHalo;

                sky += _MoonGlowColor.rgb * (glow * 0.6 + halo * 0.5) * _MoonGlow * (1.0 - crescent);
                sky  = lerp(sky, moonFace, crescent);

                moonMask = smoothstep(_MoonSize * 1.1, _MoonSize * 1.8, r);
                return sky;
            }


            // ---- トゥーン夜雲 ----  color(rgb) + density(a)
            float4 SampleCloud(float3 dir, float3 mdir)
            {
                if (dir.y <= 0.0) return 0;

                float skyMask = smoothstep(0.12, 0.32, dir.y);       // 地平線際はフェードイン
                float topFade = 1.0 - smoothstep(0.80, 1.0, dir.y);  // 真上はフェードアウト
                float t       = _Time.y * _CloudSpeed * 0.01;

                // ワープはベース UV（時間なし）から計算（時間込みだとワープ自体が動いてガタつく）
                float2 base = dir.xz / dir.y * (0.30 * _CloudScale);
                float2 warp = float2(fbm(base + 13.1), fbm(base + 37.7)) * 0.30;
                float2 uv   = base + warp + float2(t, t * 0.6);

                float shape  = fbm(uv);
                float detail = fbm(uv * 2.3 + float2(5.2, 1.3));

                float thresh = lerp(0.60, 0.42, _CloudCoverage);
                float d = smoothstep(thresh, thresh + 0.05, shape);
                d *= skyMask * topFade * _CloudDensity;
                if (d <= 0.001) return 0;

                // トゥーン2値ライティング：上向き + ディテールノイズで明暗を割る
                float litRaw    = (dir.y - 0.25 + (detail - 0.5) * 0.5) * 4.0;
                float edgeWidth = lerp(0.40, 0.06, _CloudSharpness);
                float litFace   = smoothstep(0.5 - edgeWidth * 0.5, 0.5 + edgeWidth * 0.5, saturate(litRaw));

                float3 col = lerp(_CloudShadowColor.rgb, _CloudLitColor.rgb, litFace);

                // 月側の雲エッジに月色のシルバーライニング
                float silver = pow(saturate(dot(dir, mdir)), 6.0) * (1.0 - d) * d * 4.0;
                col += _MoonColor.rgb * silver * 0.35;

                // 明暗境界をわずかに暗くして輪郭を強調
                float edge = 1.0 - smoothstep(0.0, 0.10, abs(saturate(litRaw) - 0.5));
                col *= 1.0 - edge * 0.25;

                return float4(col, d);
            }


            // ---- 瞬く星（Kenny star_xx セル散布）----
            float3 SampleStars(float3 dir)
            {
                float skyMask = smoothstep(0.02, 0.25, dir.y); // 地平線の下・際は出さない
                if (skyMask <= 0.0) return 0;

                float2 g    = SphereUV(dir) * float2(_StarGridScale * 2.0, _StarGridScale);
                float2 cell = floor(g);
                float2 f    = frac(g);

                float2 rnd = hash2(cell);
                float  has = step(rnd.x, _StarAmount); // セルに星が出る確率
                if (has <= 0.0) return 0;

                float2 offset   = (hash2(cell + float2(7.3, 2.9)) - 0.5) * 0.5;
                float  sizeMult = 0.5 + rnd.y * 0.7;
                int    idx      = (int)clamp(floor(hash2(cell + float2(1.3, 5.7)).x * 4.0), 0, 3);

                float2 uv = (f - 0.5 - offset) / (_StarSize * sizeMult * 0.5) + 0.5;
                float inCell = step(0.0, uv.x) * step(uv.x, 1.0)
                             * step(0.0, uv.y) * step(uv.y, 1.0);

                float mask = SampleStarMask(uv, idx) * inCell;

                // 瞬き：セルごとに位相・速度を散らす
                float twinkle = 0.55 + 0.45 * sin(_Time.y * _TwinkleSpeed * (0.5 + rnd.y) + rnd.x * 6.28);

                return _StarColor.rgb * _StarIntensity * mask * twinkle * skyMask;
            }


            // ---- 宙に浮かぶ窓（Kenny window_xx セル散布）----
            // ゆっくり現れては消える = チェシャ猫の神出鬼没
            float3 SampleWindows(float3 dir)
            {
                float skyMask = smoothstep(0.05, 0.20, dir.y);
                if (skyMask <= 0.0) return 0;

                float2 g = SphereUV(dir) * float2(_WindowGridScale * 2.0, _WindowGridScale);
                g.y += _Time.y * _WindowDriftSpeed; // ごくゆっくり漂う
                float2 cell = floor(g);
                float2 f    = frac(g);

                float2 rnd = hash2(cell);
                float  has = step(rnd.x, _WindowAmount);
                if (has <= 0.0) return 0;

                float2 offset   = (hash2(cell + float2(4.1, 9.7)) - 0.5) * 0.4;
                float  sizeMult = 0.6 + rnd.y * 0.5;
                int    idx      = (int)clamp(floor(hash2(cell + float2(2.7, 6.1)).x * 4.0), 0, 3);

                // セルごとにランダム回転
                float  ang = hash2(cell + float2(8.9, 3.3)).x * 6.2831853;
                float  cs  = cos(ang);
                float  sn  = sin(ang);
                float2 lp  = f - 0.5 - offset;
                lp = float2(lp.x * cs - lp.y * sn, lp.x * sn + lp.y * cs);

                float2 uv = lp / (_WindowSize * sizeMult) + 0.5;
                float inCell = step(0.0, uv.x) * step(uv.x, 1.0)
                             * step(0.0, uv.y) * step(uv.y, 1.0);

                float mask = SampleWindowMask(uv, idx) * inCell;

                // フェードイン / アウトの出現サイクル（セルごとに位相・周期を散らす）
                float cyc    = 0.5 + 0.5 * sin(_Time.y * _WindowCycleSpeed * (0.7 + rnd.y * 0.6) + rnd.x * 6.28);
                float appear = smoothstep(0.30, 0.62, cyc);

                return _WindowColor.rgb * _WindowIntensity * mask * appear * skyMask;
            }


            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // カメラ追従 + 深度最遠固定（他オブジェクトより必ず後ろ）
                float3 posWS   = _WorldSpaceCameraPos + IN.positionOS.xyz;
                float4 posCS   = TransformWorldToHClip(posWS);
                posCS.z        = posCS.w;
                OUT.positionCS = posCS;
                OUT.worldDir   = IN.positionOS.xyz;
                return OUT;
            }


            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir  = normalize(IN.worldDir);
                float3 mdir = normalize(_MoonDir.xyz);

                // --- ベースの縦グラデーション（藍 → 紫 → ラベンダー） ---
                float h = saturate(dir.y * 0.5 + 0.5);              // 0=真下, 0.5=地平線, 1=天頂
                float hUp = saturate((h - 0.5) * 2.0);              // 地平線から上の高さ [0,1]
                float3 sky;
                if (hUp < _MidPoint)
                    sky = lerp(_HorizonColor.rgb, _MidColor.rgb, smoothstep(0.0, _MidPoint, hUp));
                else
                    sky = lerp(_MidColor.rgb, _ZenithColor.rgb, smoothstep(_MidPoint, 1.0, hUp));

                // --- 三日月（ディスク + グロー + ハロ） ---
                float moonMask;
                sky = CrescentMoon(dir, sky, mdir, moonMask);

                // --- トゥーン夜雲（月より手前） ---
                float4 cloud = SampleCloud(dir, mdir);
                sky = lerp(sky, cloud.rgb, cloud.a);

                sky = ACES(sky * _Exposure);

                // --- 星・窓は ACES 後に加算（HDR 値を保って Bloom で光らせる） ---
                float occlusion = (1.0 - cloud.a) * moonMask; // 雲の裏・月の上には出さない
                sky += SampleStars(dir)   * occlusion;
                sky += SampleWindows(dir) * occlusion;

                // ディゾルブ・クロスフェード：溶け際に燃えるウェーブフロントを合成
                return SkydomeDissolveComposite(sky, IN.worldDir, IN.positionCS.xy, _Dissolve);
            }

            ENDHLSL
        }
    }
}
