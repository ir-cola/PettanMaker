Shader "Custom/URP/Skydome"
{
    // =========================================================================
    // URP 対応スカイドームシェーダー
    //
    // 機能概要:
    //   - シーンの Directional Light 方向を太陽として使用
    //   - 時刻（太陽仰角）に応じた空の色を自動計算（昼・夕焼け・夜）
    //   - 太陽ディスク + コロナ + サンレイ
    //   - トゥーン2層雲（低層・高層）+ セルフシャドウ + リムライト
    //   - ACES トーンマッピング
    // =========================================================================

    // ─────────────────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────────────────
    Properties
    {
        [Header(Sun)]
        _SunIntensity ("Sun Intensity",  Float)        = 5.0   // 太陽ディスクの輝度
        _SunRayLength ("Sun Ray Length", Range(0,1))   = 0.5   // 光芒の長さ (0=短, 1=長)
        _Exposure     ("Exposure",       Float)        = 1.0   // 露出補正 (ACES 前に全体に掛ける)

        [Header(Atmosphere)]
        _MieG ("Mie Anisotropy (g)", Range(0,1)) = 0.76       // 前方散乱の強さ (太陽周辺グローの広がり)

        [Header(Clouds)]
        _CloudDensity   ("Cloud Density",    Range(0,2))    = 1.0  // 雲の不透明度
        _CloudCoverage  ("Cloud Coverage",   Range(0,1))    = 0.5  // 雲量 (0=疎ら, 1=空を覆う)
        _LowCloudScale  ("Low Cloud Scale",  Range(0.5,10)) = 1.0  // 低層雲のスケール (大きいほど雲が大きい)
        _HighCloudScale ("High Cloud Scale", Range(0.5,10)) = 1.0  // 高層雲のスケール
        _CloudSharpness ("Cloud Sharpness",  Range(0,1))    = 0.5  // 明暗境界の鋭さ (0=柔らか, 1=ハードトゥーン)
        _CloudShadowStr ("Shadow Strength",  Range(0,1))    = 0.5  // セルフシャドウの濃さ
        _CloudSpeed     ("Cloud Speed",      Float)         = 0.01 // 雲の流れる速さ

        [Header(Sky Colors Night)]
        _SkyNightZenith  ("Night Zenith",  Color) = (0.005, 0.010, 0.040, 1)
        _SkyNightHorizon ("Night Horizon", Color) = (0.010, 0.018, 0.055, 1)

        [Header(Sky Colors Day)]
        _SkyDayZenith    ("Day Zenith",    Color) = (0.080, 0.220, 0.720, 1)
        _SkyDayHorizon   ("Day Horizon",   Color) = (0.320, 0.600, 0.920, 1)

        [Header(Sky Colors Sunset)]
        _SkySunsetHorizon ("Sunset Horizon", Color) = (0.980, 0.360, 0.040, 1)
        _SkySunsetZenith  ("Sunset Zenith",  Color) = (0.200, 0.100, 0.200, 1)

        [Header(Sun Colors)]
        _SunColorHigh    ("Sun Color (High)",    Color) = (1.000, 0.950, 0.800, 1)
        _SunColorHorizon ("Sun Color (Horizon)", Color) = (1.000, 0.500, 0.080, 1)

        // ディザ・クロスフェード用（SkydomeBlender が制御）
        [HideInInspector] _Dissolve ("Dissolve", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Background"  // 他のオブジェクトより先に描画
            "RenderType"     = "Opaque"
        }

        Cull  Front   // スカイドーム球の内側を描画するため表面をカリング
        ZWrite Off
        ZTest Always  // DrawMeshInstanced が先に深度を書いても常に描画する

        // ─────────────────────────────────────────────────────────────────
        // Sky Pass
        // ─────────────────────────────────────────────────────────────────
        Pass
        {
            Name "Sky"
            HLSLPROGRAM

            #pragma vertex   vert
            #pragma fragment frag

            // GetMainLight() を使うため Lighting.hlsl を include (Core.hlsl も内包)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SkydomeDissolve.hlsl"

            // ── Structs ───────────────────────────────────────────────────

            // 頂点シェーダへの入力
            struct Attributes
            {
                float4 positionOS : POSITION; // オブジェクト空間の頂点座標
            };

            // 頂点シェーダ → フラグメントシェーダへの出力
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // クリップ空間座標
                float3 rayDir      : TEXCOORD0;   // カメラからの視線方向 (正規化済み)
            };

            // ── Uniforms ──────────────────────────────────────────────────

            float _SunIntensity;
            float _SunRayLength;
            float _Exposure;
            float _MieG;
            float _CloudDensity;
            float _CloudCoverage;
            float _LowCloudScale;
            float _HighCloudScale;
            float _CloudSharpness;
            float _CloudShadowStr;
            float _CloudSpeed;
            float4 _SkyNightZenith;
            float4 _SkyNightHorizon;
            float4 _SkyDayZenith;
            float4 _SkyDayHorizon;
            float4 _SkySunsetHorizon;
            float4 _SkySunsetZenith;
            float4 _SunColorHigh;
            float4 _SunColorHorizon;
            float  _Dissolve;

            // ── Noise ─────────────────────────────────────────────────────

            // 2D 疑似乱数 [0, 1]
            // sin × 大きな定数 の小数部を取ることで格子状パターンを避ける
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            // バイリニア補間によるスムーズなノイズ [0, 1]
            // u = f*f*(3-2f) で Hermite 補間 → 格子境界の連続性を保証
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);       // セル整数座標
                float2 f = frac(p);        // セル内小数座標
                float2 u = f * f * (3.0 - 2.0 * f); // スムーズステップ

                return lerp(
                    lerp(Hash(i),                Hash(i + float2(1, 0)), u.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x),
                    u.y
                );
            }

            // Fractal Brownian Motion: ValueNoise を倍音重ねで合成
            // 5オクターブ、振幅を半分ずつ減らして細かいディテールを加える
            float FBM(float2 p)
            {
                float v = 0.0; // 累積値
                float a = 0.5; // 現在の振幅

                for (int i = 0; i < 5; i++)
                {
                    v += ValueNoise(p) * a;
                    p *= 2.0; // 周波数を2倍 (オクターブ上げ)
                    a *= 0.5; // 振幅を半減
                }

                return v;
            }

            // ── Scattering ────────────────────────────────────────────────

            // Henyey-Greenstein 位相関数 (Mie 散乱の近似)
            // cosTheta: 視線と光源のなす角のコサイン
            // g: 異方性パラメータ (0=等方, 1=強い前方散乱)
            // 太陽の近くで大きな値 → 空に白いグロー
            float MiePhase(float cosTheta, float g)
            {
                float g2    = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosTheta;
                return (1.0 - g2) / (4.0 * PI * pow(max(denom, 0.0001), 1.5));
            }

            // ── Sun (disk + corona + rays) ────────────────────────────────

            // 太陽の視覚表現を合成して返す
            // sunAbove: 太陽が地平線以下のときフェードアウトさせるマスク
            float3 SunContribution(float3 rayDir, float3 sunDir, float3 sunColor, float sunAbove)
            {
                float cosTheta = dot(rayDir, sunDir); // 視線と太陽のなす角
                float3 result  = 0;

                // 広域グロー: 太陽から約15° 以内に広がる暖色の輝き
                float wideGlow = pow(max(cosTheta - 0.85, 0.0) / 0.15, 4.0);
                result += sunColor * float3(1.0, 0.80, 0.50) * wideGlow * 0.12;

                // 中間コロナ: ディスクから約1° の黄金色の輝き
                float midGlow = pow(max(cosTheta - 0.990, 0.0) / 0.010, 3.0);
                result += sunColor * float3(1.0, 0.90, 0.65) * midGlow * 0.60;

                // タイトコロナ: ディスク直外の鋭い輝き
                float innerGlow = pow(max(cosTheta - 0.9990, 0.0) / 0.0009, 2.5);
                result += sunColor * innerGlow * 1.2;

                // 太陽ディスク: リム減光付き (中心が明るく縁がやや暗い)
                // diskT=1 が中心、0 が縁。sqrt で緩やかなグラデーション
                float diskT = saturate((cosTheta - 0.9994) / 0.0005);
                float limb  = lerp(0.55, 1.0, sqrt(diskT));
                result     += sunColor * limb * step(0.9994, cosTheta) * _SunIntensity;

                // 光芒 (8本): 太陽を中心とした局所2D座標でガウス関数の線を描く
                // right/upDir で太陽垂直面の2D基底を作成
                float3 up    = abs(sunDir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 right = normalize(cross(sunDir, up));
                float3 upDir = normalize(cross(right, sunDir));
                float3 perp  = rayDir - sunDir * cosTheta; // 太陽方向を引いた垂直成分
                float  px    = dot(perp, right);
                float  py    = dot(perp, upDir);
                float  r     = length(float2(px, py)); // 太陽中心からの2D距離

                float rays = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a   = PI * float(i) * 0.25 + 0.31;       // 各レイの角度 (等間隔+オフセット)
                    float d   = abs(px * cos(a) + py * sin(a));     // レイ直線からの距離
                    float w   = (0.004 + 0.004 * Hash(float2(float(i), 3.7)));   // レイの太さ (ランダム)
                    float len = (0.4   + 0.6   * Hash(float2(float(i) * 1.7, 5.3))); // レイの強度 (ランダム)
                    rays += exp(-d * d / (w * w)) * len; // ガウス関数で線を描く
                }
                rays = saturate(rays * 0.3);
                float outerR  = lerp(0.07, 0.25, _SunRayLength);                       // レイの外側減衰距離
                float rayFade = smoothstep(0.008, 0.022, r)                             // ディスク直近は消す
                              * smoothstep(outerR, outerR * 0.3, r);                   // 外側でフェードアウト
                result += sunColor * float3(1.0, 0.88, 0.65) * rays * rayFade * smoothstep(0.0, 0.2, cosTheta) * 0.9;

                return result * sunAbove; // 地平線以下では消える
            }

            // ── Sky Color ─────────────────────────────────────────────────

            // 太陽仰角から空の色を自動計算
            // dayT=1 が真昼、sunsetT=1 が日の出/日の入り、両方0 が夜
            float3 SkyColor(float3 rayDir, float3 sunDir, float dayT, float sunsetT)
            {
                // 天頂色と地平線色を夜→昼でブレンド
                float3 zenith  = lerp(_SkyNightZenith.rgb,  _SkyDayZenith.rgb,  dayT);
                float3 horizon = lerp(_SkyNightHorizon.rgb, _SkyDayHorizon.rgb, dayT);

                // 夕焼け・朝焼けの色を上乗せ
                zenith  = lerp(zenith,  _SkySunsetZenith.rgb,  sunsetT * 0.5);
                horizon = lerp(horizon, _SkySunsetHorizon.rgb, sunsetT);

                // 天頂→地平線グラデーション
                // pow(1-y, 5) で地平線付近を広げ、空の上部が素早く青くなる
                float hBlend = pow(saturate(1.0 - rayDir.y), 5.0);
                float3 sky   = lerp(zenith, horizon, hBlend);

                // 夕焼け時に太陽の水平方向に橙色グローを加算
                // sunDir の Y 成分を 0 にして水平方向の dot を取る
                float sunGlow = pow(saturate(dot(rayDir, float3(sunDir.x, 0, sunDir.z))), 6.0);
                sky += _SkySunsetHorizon.rgb * sunsetT * sunGlow * 0.4;

                return sky;
            }

            // ── Clouds ────────────────────────────────────────────────────

            // 1レイヤー分のトゥーンライティングを計算して雲の色を返す
            // shape    : FBM による雲の基本形状値
            // detail   : 影境界を凸凹させる細部ノイズ
            // density  : 雲の密度 (エッジリムライトの計算に使用)
            // selfShadow: 太陽方向にずらしてサンプルした FBM (自己遮蔽の近似)
            float3 CloudLighting(
                float shape, float detail, float density,
                float3 rayDir, float3 sunDir,
                float selfShadow, float dayT, float sunsetT)
            {
                // ── 明暗境界の計算 ────────────────────────────────────────
                // rayDir.y が高い (上を向いている) ほど雲の上面 (明)
                // sunDir.y が高い (太陽が高い) ほど影が少ない
                // detail ノイズで境界を凸凹させ絵画的に
                float shadowNoise  = detail * 0.5 - 0.05;
                float shadowContrib = selfShadow * _CloudShadowStr * 0.5; // セルフシャドウ寄与
                float litRaw = (rayDir.y - 0.30 + shadowNoise + sunDir.y * 0.18 - shadowContrib) * 6.0;

                // _CloudSharpness=0 → 幅0.4 (ふわっとソフト)
                // _CloudSharpness=1 → 幅0.04 (ほぼ step のハードトゥーン)
                float edgeWidth = lerp(0.40, 0.04, _CloudSharpness);
                float litFace   = smoothstep(0.5 - edgeWidth * 0.5, 0.5 + edgeWidth * 0.5, saturate(litRaw));

                // 境界付近を少し暗くして輪郭を強調
                float edge = 1.0 - smoothstep(0.0, 0.08, abs(saturate(litRaw) - 0.5));

                // エッジリムライト: 密度が薄い縁 (density≈0.5) に太陽光が透過する効果
                // (1-d)*d*4 はdensity=0.5 でピークを持つベル曲線
                float rim      = (1.0 - density) * density * 4.0;
                float rimLight = pow(saturate(dot(rayDir, sunDir)), 5.0) * rim * dayT;

                // ── カラーパレット (時刻対応) ─────────────────────────────
                float3 litDay    = float3(1.00, 1.00, 1.00); // 昼: 明面=純白
                float3 shadowDay = float3(0.30, 0.32, 0.38); // 昼: 影面=濃グレー
                float3 litSet    = float3(1.10, 0.72, 0.45); // 夕: 明面=橙白
                float3 shadowSet = float3(0.45, 0.22, 0.35); // 夕: 影面=紫
                float3 litNight    = float3(0.10, 0.13, 0.22); // 夜: 明面=暗青
                float3 shadowNight = float3(0.03, 0.04, 0.08); // 夜: 影面=ほぼ黒

                // 夜→昼→夕焼けの順でブレンド
                float3 litColor    = lerp(lerp(litNight,    litDay,    dayT), litSet,    sunsetT * 0.8);
                float3 shadowColor = lerp(lerp(shadowNight, shadowDay, dayT), shadowSet, sunsetT * 0.8);

                // シルバーライニング (太陽側の雲エッジに暖色ハイライト) + リムライト
                float silver = pow(saturate(dot(rayDir, sunDir)), 10.0) * dayT;
                litColor += float3(0.30, 0.22, 0.08) * (silver + rimLight);

                float3 color = lerp(shadowColor, litColor, litFace);
                return color * (1.0 - edge * 0.30); // 境界暗線を適用
            }

            // 雲をサンプルして color(rgb) + density(a) を返す
            // 2層構造: Layer1=低層メイン雲, Layer2=高層薄雲
            float4 SampleCloud(float3 rayDir, float3 sunDir, float dayT, float sunsetT)
            {
                if (rayDir.y <= 0.0) return 0; // 地平線以下は不要

                float t = _Time.y * _CloudSpeed; // 時間オフセット (_CloudSpeed でスケール)

                // 雲の出現範囲マスク
                float skyMask = smoothstep(0.20, 0.40, rayDir.y); // 仰角 20°〜40° でフェードイン
                float topFade = 1.0 - smoothstep(0.75, 1.0, rayDir.y); // 真上付近でフェードアウト

                // ═══ Layer 1 : 低層メイン雲 ═══════════════════════════════
                // ワープをベース UV (時間なし) から計算する理由:
                //   時間オフセット後の UV でワープすると、ワープ自体が時間で動いてガタつくため
                float2 base1 = rayDir.xz / rayDir.y * (0.28 * _LowCloudScale); // 平面投影UV
                float2 warp1 = float2(FBM(base1 + 13.1), FBM(base1 + 37.7)) * 0.30; // ドメインワープ
                float2 uv1   = base1 + warp1 + float2(t * 0.012, t * 0.007);          // 時間で流す

                float shape1  = FBM(uv1);                              // 雲の形状
                float detail1 = FBM(uv1 * 2.1 + float2(5.2, 1.3));   // 影境界の細部ノイズ
                float shadow1 = FBM(uv1 + sunDir.xz * 0.09);          // セルフシャドウ用: 太陽方向にずらしサンプル

                // Coverage で閾値をシフト → 雲量を制御
                float thresh1 = lerp(0.56, 0.40, _CloudCoverage);
                float d1 = smoothstep(thresh1, thresh1 + 0.04, shape1); // ほぼハードエッジ
                d1 *= skyMask * topFade * _CloudDensity;

                // ═══ Layer 2 : 高層薄雲 ════════════════════════════════════
                // スケールを小さく (0.15) することで遠く高い位置に見える
                float2 base2 = rayDir.xz / rayDir.y * (0.15 * _HighCloudScale);
                float2 warp2 = float2(FBM(base2 + 5.3), FBM(base2 + 17.9)) * 0.20;
                float2 uv2   = base2 + warp2 + float2(t * 0.007, t * 0.004); // Layer1 より遅く流す

                float shape2  = FBM(uv2 * 0.9 + float2(2.1, 3.7));
                float detail2 = FBM(uv2 * 1.8 + float2(8.3, 2.9));
                float shadow2 = FBM(uv2 + sunDir.xz * 0.07);

                float thresh2 = lerp(0.58, 0.42, _CloudCoverage);
                float d2 = smoothstep(thresh2, thresh2 + 0.04, shape2) * 0.65; // Layer1 より薄め
                d2 *= skyMask * topFade * _CloudDensity;

                if (max(d1, d2) <= 0.001) return 0; // 両方ゼロなら早期リターン

                // ═══ ライティング ══════════════════════════════════════════
                float3 col1 = CloudLighting(shape1, detail1, d1, rayDir, sunDir, shadow1, dayT, sunsetT);
                float3 col2 = CloudLighting(shape2, detail2, d2, rayDir, sunDir, shadow2, dayT, sunsetT);

                // 高層雲は少し白っぽく (薄い巻雲の透明感)
                col2 = lerp(col2, float3(0.90, 0.92, 0.96), 0.25 * dayT);

                // 前後合成: Layer1 (低層・手前) が Layer2 (高層・奥) を隠す
                float3 color = lerp(col2, col1, d1);
                float  alpha = saturate(d1 + d2 * (1.0 - d1)); // アルファ合成

                return float4(color, alpha);
            }

            // ── Tonemapping ───────────────────────────────────────────────

            // ACES フィルミック トーンマッピング
            // HDR の空の色を [0,1] の表示可能な範囲に圧縮する
            // 明部を自然に潰しつつ暗部のコントラストを維持
            float3 ACES(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            // ── Vertex ────────────────────────────────────────────────────

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // スカイドームをカメラ位置に追従させる
                // TransformObjectToWorld を使わず positionOS をそのままオフセットすることで
                // オブジェクトのワールド座標に依存しない → カメラがどこにいても正しく見える
                float3 posWS    = _WorldSpaceCameraPos + IN.positionOS.xyz;
                float4 posCS    = TransformWorldToHClip(posWS);
                posCS.z         = posCS.w; // z/w = 1.0 → 常に深度最遠 (他オブジェクトの後ろに描画)
                OUT.positionHCS = posCS;
                OUT.rayDir      = normalize(IN.positionOS.xyz); // オブジェクト空間方向 = 視線方向
                return OUT;
            }

            // ── Fragment ──────────────────────────────────────────────────

            half4 frag(Varyings IN) : SV_Target
            {
                float3 rayDir = normalize(IN.rayDir); // 補間後の視線方向を再正規化

                // シーンの Directional Light から太陽方向を取得
                Light  mainLight = GetMainLight();
                float3 sunDir    = normalize(mainLight.direction); // ライトへの方向 (上向き=昼)

                // 時刻ファクターを仰角から計算
                float elevation = sunDir.y;                            // -1=夜, 0=地平線, 1=真昼
                float lightOn   = saturate(max(max(mainLight.color.r, mainLight.color.g), mainLight.color.b));
                float sunVisible = smoothstep(-0.02, 0.02, elevation) * lightOn;
                float dayT      = saturate(elevation * 5.0) * lightOn; // 昼フェード (仰角11°以上で1)
                float sunsetT   = saturate(1.0 - abs(elevation) * 5.0) * sunVisible; // 夕焼けフェード (地平線でピーク)
                float sunAbove  = saturate(elevation * 8.0) * sunVisible; // 太陽が地平線以上のマスク
                float cosTheta  = dot(rayDir, sunDir);                  // 視線と太陽のなす角

                // ── 空の色 ────────────────────────────────────────────────
                float3 sky = SkyColor(rayDir, sunDir, dayT, sunsetT);

                // ── Mie 大気散乱 ──────────────────────────────────────────
                // 太陽周辺の広域グロー (昼のみ有効)
                float mie = MiePhase(cosTheta, _MieG) * 0.02;
                float3 mieColor = lerp(float3(1.0, 0.7, 0.3), float3(1.0, 0.9, 0.7), dayT);
                sky += mieColor * mie * dayT;

                // ── 太陽 (ディスク + コロナ + 光芒) ──────────────────────
                // 仰角に応じて橙→白にシフト
                float3 sunColor = lerp(_SunColorHorizon.rgb, _SunColorHigh.rgb, dayT);
                sky += SunContribution(rayDir, sunDir, sunColor, sunAbove);

                // ── 雲 ────────────────────────────────────────────────────
                // cloud.rgb=色, cloud.a=密度 でアルファ合成
                float4 cloud = SampleCloud(rayDir, sunDir, dayT, sunsetT);
                sky = lerp(sky, cloud.rgb, cloud.a);

                // ── トーンマッピング + 露出 ───────────────────────────────
                sky = ACES(sky * _Exposure);

                // ディゾルブ・クロスフェード：溶けた部分は破棄し、溶け際に燃えるウェーブフロントを合成
                return SkydomeDissolveComposite(sky, IN.rayDir, IN.positionHCS.xy, _Dissolve);
            }

            ENDHLSL
        }
    }
}
