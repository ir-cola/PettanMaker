Shader "Hidden/Custom/URP/Skydome2"
{
    // =========================================================================
    // URP 対応スカイドームシェーダー (アニメ調)
    //
    // 機能:
    //   - Directional Light の方向を太陽として使用（太陽ディスク・コロナ・光芒）
    //   - 2色を指定方向に繰り返すグラデーション空 + 水平線カラー
    //   - 三角形きらきらエフェクト（常時表示・等角投影で均一サイズ）
    //   - ACES トーンマッピング
    // =========================================================================

    Properties
    {
        // ── 太陽 ──────────────────────────────────────────────────────────────
        [Header(Sun)]
        _SunIntensity ("Sun Intensity",  Float)      = 5.0   // 太陽ディスクの明るさ
        _SunRayLength ("Sun Ray Length", Range(0,1)) = 0.5   // 光芒の長さ (0=短い, 1=長い)
        _Exposure     ("Exposure",       Float)      = 1.0   // 全体の露出補正

        // ── 大気散乱 ──────────────────────────────────────────────────────────
        [Header(Atmosphere)]
        _MieG ("Mie Anisotropy (g)", Range(0,1)) = 0.76     // Mie 散乱の指向性 (大=太陽周りに集中)

        // ── 空の色 ────────────────────────────────────────────────────────────
        [Header(Sky Colors)]
        _SkyColor0    ("Sky Color 0",   Color)        = (0.18, 0.05, 0.44, 1) // グラデーション色A
        _SkyColor1    ("Sky Color 1",   Color)        = (0.98, 0.44, 0.07, 1) // グラデーション色B
        _HorizonColor ("Horizon Color", Color)        = (1.00, 0.60, 0.20, 1) // 水平線付近の色
        _HorizonWidth ("Horizon Width", Range(1, 30)) = 10.0                  // 水平線帯の幅 (大=狭い)
        _BlendAngle   ("Blend Angle (deg, 0=right 90=up 315=top-left to bottom-right)", Range(0, 360)) = 315.0 // グラデーションの方向 (度)
        _RepeatCount  ("Repeat Count",  Range(1, 20)) = 3                     // 2色の繰り返し回数

        // ── 三角形きらきら ────────────────────────────────────────────────────
        [Header(Triangle Sparkle)]
        _TriangleGridDensity ("Grid Density",  Range(1, 20))     = 4    // グリッド密度 (小=大きくまばら)
        _TriangleSize        ("Triangle Size", Range(0.02, 1.0)) = 0.35 // 三角形の大きさ
        _TriangleBrightness  ("Brightness",    Range(0, 5))      = 2.5  // 輝度
        _TriangleAmount      ("Amount",        Range(0, 1))      = 0.80 // セルに三角形が出る確率

        // ディザ・クロスフェード用（SkydomeBlender が制御）
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
            Name "Sky"
            HLSLPROGRAM

            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SkydomeDissolve.hlsl"

            // ─────────────────────────────────────────────────────────────────
            // Structs
            // ─────────────────────────────────────────────────────────────────

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 rayDir      : TEXCOORD0; // ワールド空間の視線方向
            };

            // ─────────────────────────────────────────────────────────────────
            // Uniforms
            // ─────────────────────────────────────────────────────────────────

            float  _SunIntensity;
            float  _SunRayLength;
            float  _Exposure;
            float  _MieG;
            float4 _SkyColor0;
            float4 _SkyColor1;
            float4 _HorizonColor;
            float  _HorizonWidth;
            float  _BlendAngle;
            float  _RepeatCount;
            float  _TriangleGridDensity;
            float  _TriangleSize;
            float  _TriangleBrightness;
            float  _TriangleAmount;
            float  _Dissolve;

            // 太陽の色 (仰角によって補間)
            static const float3 SUN_COLOR_HIGH    = float3(1.000, 0.920, 0.680); // 高仰角: 黄金色
            static const float3 SUN_COLOR_HORIZON = float3(1.000, 0.500, 0.080); // 地平線: 橙赤

            // ─────────────────────────────────────────────────────────────────
            // Hash
            // ─────────────────────────────────────────────────────────────────

            // グリッドセル座標から疑似乱数 [0,1] を生成
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            // ─────────────────────────────────────────────────────────────────
            // Mie 大気散乱
            // ─────────────────────────────────────────────────────────────────

            // 太陽周辺のグロー計算 (g が大きいほど前方散乱が強い)
            float MiePhase(float cosTheta, float g)
            {
                float g2    = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosTheta;
                return (1.0 - g2) / (4.0 * PI * pow(max(denom, 0.0001), 1.5));
            }

            // ─────────────────────────────────────────────────────────────────
            // 太陽 (ディスク + コロナ + 光芒 8本)
            // ─────────────────────────────────────────────────────────────────

            // sunAbove: 太陽が地平線以上のとき 1、以下のとき 0 (地平線下で太陽を隠す)
            float3 SunContribution(float3 rayDir, float3 sunDir, float3 sunColor, float sunAbove)
            {
                float cosTheta = dot(rayDir, sunDir);
                float3 result  = 0;

                // 広域グロー (太陽から離れた淡い輝き)
                float wideGlow = pow(max(cosTheta - 0.85, 0.0) / 0.15, 4.0);
                result += sunColor * float3(1.0, 0.75, 0.45) * wideGlow * 0.14;

                // 中域グロー (コロナ外縁)
                float midGlow = pow(max(cosTheta - 0.990, 0.0) / 0.010, 3.0);
                result += sunColor * float3(1.0, 0.88, 0.60) * midGlow * 0.70;

                // 内側グロー (コロナ中心)
                float innerGlow = pow(max(cosTheta - 0.9990, 0.0) / 0.0009, 2.5);
                result += sunColor * innerGlow * 1.3;

                // 太陽ディスク (周縁減光 limb darkening 付き)
                float diskT = saturate((cosTheta - 0.9994) / 0.0005);
                float limb  = lerp(0.60, 1.0, sqrt(diskT));
                result     += sunColor * limb * step(0.9994, cosTheta) * _SunIntensity;

                // 光芒 8本: 太陽を中心とした接平面上でランダム幅・長さの筋を加算
                float3 up    = abs(sunDir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 right = normalize(cross(sunDir, up));
                float3 upDir = normalize(cross(right, sunDir));
                float3 perp  = rayDir - sunDir * cosTheta; // 太陽方向を除いた成分
                float  px    = dot(perp, right);
                float  py    = dot(perp, upDir);
                float  r     = length(float2(px, py));

                float rays = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a   = PI * float(i) * 0.25 + 0.31;              // 光芒の角度 (45度刻み+オフセット)
                    float d   = abs(px * cos(a) + py * sin(a));            // 光芒軸からの距離
                    float w   = (0.004 + 0.004 * Hash(float2(float(i), 3.7)));         // 光芒の幅 (ランダム)
                    float len = (0.4   + 0.6   * Hash(float2(float(i) * 1.7, 5.3)));   // 光芒の長さ (ランダム)
                    rays += exp(-d * d / (w * w)) * len;
                }
                rays = saturate(rays * 0.3);
                float outerR  = lerp(0.07, 0.25, _SunRayLength);
                float rayFade = smoothstep(0.008, 0.022, r)               // ディスク直近をフェードアウト
                              * smoothstep(outerR, outerR * 0.3, r);      // 外縁をフェードアウト
                result += sunColor * float3(1.0, 0.85, 0.60) * rays * rayFade * 1.0;

                return result * sunAbove;
            }

            // ─────────────────────────────────────────────────────────────────
            // 空の色 (2色の斜め繰り返しグラデーション + 水平線カラー)
            // ─────────────────────────────────────────────────────────────────

            float3 SkyColor(float3 rayDir)
            {
                // _BlendAngle (度) をラジアンに変換してブレンド方向ベクトルを作る
                // 例: 0°=右(+X), 90°=上(+Y), 315°=左上→右下
                float  rad      = _BlendAngle * (3.14159265 / 180.0);
                float2 blendDir = float2(cos(rad), sin(rad));

                // rayDir の XY 成分をブレンド軸に投影し、位相 [0,1] を繰り返す
                // frac で折り返すことで縞模様を作る
                float proj  = dot(float2(rayDir.x, rayDir.y), blendDir);
                float phase = frac(proj * _RepeatCount * 0.5 + 0.5);

                // 三角波 (0→1→0→...) で Color0 と Color1 を滑らかに往復
                float t  = 1.0 - abs(phase * 2.0 - 1.0);
                float sm = t * t * (3.0 - 2.0 * t); // smoothstep 相当の S カーブ
                float3 sky = lerp(_SkyColor0.rgb, _SkyColor1.rgb, sm);

                // 水平線 (rayDir.y ≈ 0) に _HorizonColor を重ねる
                // exp により y=0 が最大、離れるほど指数的に減衰
                float horizonBand = exp(-abs(rayDir.y) * _HorizonWidth);
                sky = lerp(sky, _HorizonColor.rgb, horizonBand);

                return sky;
            }

            // ─────────────────────────────────────────────────────────────────
            // 正三角形の符号付き距離関数 (負=内側, 正=外側)
            // r: 外接円半径に相当するサイズ
            // ─────────────────────────────────────────────────────────────────

            float TriangleSDF(float2 p, float r)
            {
                const float k = 1.73205; // sqrt(3)
                p.x = abs(p.x) - r;
                p.y = p.y + r / k;
                if (p.x + k * p.y > 0.0)
                    p = float2(p.x - k * p.y, -k * p.x - p.y) * 0.5;
                p.x -= clamp(p.x, -2.0 * r, 0.0);
                return -length(p) * sign(p.y);
            }

            // ─────────────────────────────────────────────────────────────────
            // ACES トーンマッピング
            // ─────────────────────────────────────────────────────────────────

            float3 ACES(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            // ─────────────────────────────────────────────────────────────────
            // Vertex シェーダー
            // ─────────────────────────────────────────────────────────────────

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // カメラ位置を中心に固定して無限遠扱いにする
                float3 posWS    = _WorldSpaceCameraPos + IN.positionOS.xyz;
                float4 posCS    = TransformWorldToHClip(posWS);
                posCS.z         = posCS.w; // 深度を最遠値に固定 (スカイドームが他の物体より後ろになる)
                OUT.positionHCS = posCS;
                OUT.rayDir      = normalize(IN.positionOS.xyz);
                return OUT;
            }

            // ─────────────────────────────────────────────────────────────────
            // Fragment シェーダー
            // ─────────────────────────────────────────────────────────────────

            half4 frag(Varyings IN) : SV_Target
            {
                float3 rayDir = normalize(IN.rayDir);

                // シーンの Directional Light を太陽として使用
                Light  mainLight = GetMainLight();
                float3 sunDir    = normalize(mainLight.direction);

                float elevation = sunDir.y;
                float dayT      = saturate(elevation * 5.0);  // 太陽が地平線より上: 0→1
                float sunAbove  = saturate(elevation * 8.0);  // 太陽ディスク表示フラグ
                float cosTheta  = dot(rayDir, sunDir);        // 視線と太陽方向のなす角

                // 空のベースカラー
                float3 sky = SkyColor(rayDir);

                // Mie 大気散乱: 太陽周囲にグロー色を加算
                float mie = MiePhase(cosTheta, _MieG) * 0.02;
                float3 mieColor = lerp(float3(1.0, 0.65, 0.25), float3(1.0, 0.88, 0.65), dayT);
                sky += mieColor * mie * dayT;

                // 太陽 (地平線以上のとき表示)
                float3 sunColor = lerp(SUN_COLOR_HORIZON, SUN_COLOR_HIGH, dayT);
                sky += SunContribution(rayDir, sunDir, sunColor, sunAbove);

                // 三角形きらきらエフェクト (上半球のみ)
                if (rayDir.y > 0.01)
                {
                    // 等角投影 (経度 phi, 緯度 theta) でグリッドを張る
                    // 透視投影と違い仰角によらずセルの角度サイズが一定 → 三角形が均一サイズ
                    float phi   = atan2(rayDir.z, rayDir.x) * (1.0 / (2.0 * PI)); // 経度: -0.5〜0.5
                    float theta = asin(clamp(rayDir.y, -1.0, 1.0)) * (2.0 / PI);  // 緯度: 0〜1 (上半球)
                    float2 triGrid = float2(phi, theta) * _TriangleGridDensity;
                    float2 triCell = floor(triGrid);
                    float2 cellUV  = frac(triGrid) - 0.5; // セル内座標 [-0.5, 0.5]

                    // セルごとの基本乱数 (存在判定・点滅・色に使用)
                    float triRand = Hash(triCell);

                    // _TriangleAmount の確率でセルに三角形を配置
                    float hasTri = step(1.0 - _TriangleAmount, triRand);

                    // セル内でランダムにジッター (格子状に見えないようにする)
                    float2 jitter  = float2(Hash(triCell + 17.3), Hash(triCell + 91.7)) - 0.5;
                    float2 localUV = cellUV - jitter * 0.35;

                    // 角度は存在判定と独立した別の乱数で決める (回転が均一分布になる)
                    float angle = Hash(triCell + float2(53.1, 79.3)) * 6.28318;
                    float ca = cos(angle), sa = sin(angle);
                    float2 rotUV = float2(localUV.x * ca - localUV.y * sa,
                                         localUV.x * sa + localUV.y * ca);

                    // 三角形 SDF でマスクを作る (エッジを smoothstep でアンチエイリアス)
                    float triDist = TriangleSDF(rotUV, _TriangleSize * 0.5);
                    float triMask = 1.0 - smoothstep(-0.004, 0.004, triDist);

                    // 点滅: 速さ・位相ともにセルごとにランダム
                    float twinkle = 0.55 + 0.45 * sin(_Time.y * (2.5 + triRand * 8.0) + triRand * 70.0);

                    // 三角形の色: 暖色 (オレンジ白) 〜 寒色 (薄紫白) をランダム選択
                    float3 triColor = lerp(
                        float3(1.00, 0.88, 0.70),
                        float3(0.88, 0.80, 1.00),
                        frac(triRand * 3.7)
                    );

                    sky += triColor * triMask * hasTri * twinkle * _TriangleBrightness;
                }

                // ACES トーンマッピング + 露出補正
                sky = ACES(sky * _Exposure);

                // ディゾルブ・クロスフェード：溶け際に燃えるウェーブフロントを合成
                return SkydomeDissolveComposite(sky, IN.rayDir, IN.positionHCS.xy, _Dissolve);
            }

            ENDHLSL
        }
    }
}
