Shader "PettanMaker/Sticker/Collection/ResinDome"
{
    Properties
    {
        _MainTex            ("Main Texture",        2D)             = "white" {}
        _Color              ("Color Tint",          Color)          = (1, 1, 1, 1)

        [Header(Lighting)]
        _ShadowColor        ("Shadow Color",         Color)          = (0.6, 0.6, 0.7, 1)
        _ShadowIntensity    ("Shadow Intensity",     Range(0, 1))    = 0.6
        _AmbientMin         ("Ambient Min",          Range(0, 1))    = 0.35

        [Header(Specular)]
        _SpecColor          ("Specular Color",       Color)          = (1, 1, 1, 1)
        _SpecIntensity      ("Specular Intensity",   Range(0, 4))    = 1.5
        _SpecPower          ("Specular Power",       Range(1, 256))  = 80.0

        [Header(Ambient Sheen)]
        _HighlightColor     ("Highlight Color",      Color)          = (1, 1, 1, 1)
        _HighlightIntensity ("Highlight Intensity",  Range(0, 3))    = 1.0
        _HighlightPower     ("Highlight Power",      Range(1, 32))   = 8.0
        _HighlightShift     ("Highlight Shift",      Range(0, 1))    = 0.35

        [Header(Rim)]
        _RimColor           ("Rim Color",           Color)          = (1, 1, 1, 1)
        _RimIntensity       ("Rim Intensity",       Range(0, 3))    = 1.0
        _RimPower           ("Rim Power",            Range(0.5, 8)) = 3.5

        [Header(Puff Dome Shape)]
        _Puffiness          ("Puffiness",            Range(0, 6))    = 2.0
        _PuffBlur           ("Puff Blur (mip)",      Range(0, 8))    = 5.0
        _PuffSampleDist     ("Puff Sample Dist (texels)", Range(1, 96)) = 20.0
        _PuffEdge           ("Puff Edge Rounding (low=round)", Range(0.2, 1)) = 0.45

        [Header(Glass Candy)]
        _RefractStrength    ("Refraction Strength",  Range(0, 0.2))  = 0.06
        _ColorDepth         ("Candy Color Depth",    Range(0, 1))    = 0.55
        _EdgeDarken         ("Edge Absorption",      Range(0, 1))    = 0.4
        _GlintIntensity     ("Second Glint",         Range(0, 3))    = 1.0

        [Header(Reflection Caustic)]
        _ReflectIntensity   ("Env Reflection",       Range(0, 2))    = 0.7
        _Dispersion         ("Chromatic Dispersion", Range(0, 1))    = 0.35
        _CausticIntensity   ("Caustic Glow",         Range(0, 2))    = 0.8

        [Header(Inner Resin)]
        _InnerDepth         ("Inner Parallax Depth", Range(0, 0.3))  = 0.12
        _InnerColor         ("Inner Liquid Tint",    Color)          = (1, 1, 1, 1)
        _InnerGlow          ("Inner Glow",           Range(0, 2))    = 0.6
        _BubbleAmount       ("Bubble Amount",        Range(0, 1))    = 0.5
        _BubbleScale        ("Bubble Scale",         Range(4, 64))   = 22.0

        [Header(Prism Sweep)]
        _SweepIntensity     ("Sweep Intensity",      Range(0, 3))    = 1.2
        _SweepPeriod        ("Sweep Period (s)",     Range(0.5, 12)) = 5.0
        _SweepWidth         ("Sweep Width",          Range(0.02, 0.5)) = 0.12
        _SweepAngle         ("Sweep Angle (deg)",    Range(0, 360))  = 35.0
        _SweepRainbow       ("Rainbow Split",        Range(0, 1))    = 0.6

        [Header(Animation)]
        _ShimmerAmount      ("Inner Shimmer Amount", Range(0, 1))    = 0.5
        _ShimmerSpeed       ("Inner Shimmer Speed",  Range(0, 3))    = 0.6

        [Header(Alpha)]
        _BaseAlpha          ("Base Alpha",           Range(0, 1))    = 0.85
        _EdgeAlphaBoost     ("Edge Alpha Boost",     Range(0, 1))    = 0.3

        [Header(Shadow Cast)]
        _AlphaCutoff        ("Shadow Alpha Cutoff",  Range(0, 1))    = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        // -------------------------------------------------------
        // Pass 1 : ShadowCaster (下地の背景モデルへ影を落とす)
        // -------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Pass2(UniversalForward)と同一レイアウトにしてSRP Batcherの互換性を保つ
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half4  _ShadowColor;
                half   _ShadowIntensity;
                half   _AmbientMin;
                half4  _SpecColor;
                half   _SpecIntensity;
                half   _SpecPower;
                half4  _HighlightColor;
                half   _HighlightIntensity;
                half   _HighlightPower;
                half   _HighlightShift;
                half4  _RimColor;
                half   _RimIntensity;
                half   _RimPower;
                half   _BaseAlpha;
                half   _EdgeAlphaBoost;
                half   _AlphaCutoff;
                half   _Puffiness;
                half   _PuffBlur;
                half   _PuffSampleDist;
                half   _PuffEdge;
                half   _RefractStrength;
                half   _ColorDepth;
                half   _EdgeDarken;
                half   _GlintIntensity;
                half   _ReflectIntensity;
                half   _Dispersion;
                half   _CausticIntensity;
                half   _InnerDepth;
                half4  _InnerColor;
                half   _InnerGlow;
                half   _BubbleAmount;
                half   _BubbleScale;
                half   _ShimmerAmount;
                half   _ShimmerSpeed;
                half   _SweepIntensity;
                half   _SweepPeriod;
                half   _SweepWidth;
                half   _SweepAngle;
                half   _SweepRainbow;
                float4 _MainTex_TexelSize;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            float4 GetShadowPositionHClip(Attributes IN)
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                float3 lightDirWS = _LightDirection;

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    lightDirWS = normalize(_LightPosition - positionWS);
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = GetShadowPositionHClip(IN);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ドームの塊ではなく模様(顔・線画)の輪郭だけを影として落とす
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                clip(alpha - _AlphaCutoff);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // Pass 2 : メインカラー描画（滑らかなライティング + 半透明レジン質感）
        // -------------------------------------------------------
        Pass
        {
            Name "ResinDome"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half4  _ShadowColor;
                half   _ShadowIntensity;
                half   _AmbientMin;
                half4  _SpecColor;
                half   _SpecIntensity;
                half   _SpecPower;
                half4  _HighlightColor;
                half   _HighlightIntensity;
                half   _HighlightPower;
                half   _HighlightShift;
                half4  _RimColor;
                half   _RimIntensity;
                half   _RimPower;
                half   _BaseAlpha;
                half   _EdgeAlphaBoost;
                half   _AlphaCutoff;
                half   _Puffiness;
                half   _PuffBlur;
                half   _PuffSampleDist;
                half   _PuffEdge;
                half   _RefractStrength;
                half   _ColorDepth;
                half   _EdgeDarken;
                half   _GlintIntensity;
                half   _ReflectIntensity;
                half   _Dispersion;
                half   _CausticIntensity;
                half   _InnerDepth;
                half4  _InnerColor;
                half   _InnerGlow;
                half   _BubbleAmount;
                half   _BubbleScale;
                half   _ShimmerAmount;
                half   _ShimmerSpeed;
                half   _SweepIntensity;
                half   _SweepPeriod;
                half   _SweepWidth;
                half   _SweepAngle;
                half   _SweepRainbow;
                float4 _MainTex_TexelSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 tangentWS   : TEXCOORD1;
                float3 bitangentWS : TEXCOORD2;
                float3 viewDirWS   : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float3 positionWS  : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs    = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS  = posInputs.positionCS;
                OUT.normalWS    = normalInputs.normalWS;
                OUT.tangentWS   = normalInputs.tangentWS;
                OUT.bitangentWS = normalInputs.bitangentWS;
                OUT.viewDirWS   = GetWorldSpaceViewDir(posInputs.positionWS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.positionWS  = posInputs.positionWS;
                return OUT;
            }

            half Resin_hash21(float2 p)
            {
                p  = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 2オクターブ相当の滑らかなバリューノイズ(光の揺らぎ用)
            half Resin_vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                half a = Resin_hash21(i);
                half b = Resin_hash21(i + float2(1, 0));
                half c = Resin_hash21(i + float2(0, 1));
                half d = Resin_hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 樹脂の中に浮かぶ気泡: セルごとにランダムな位置・大きさの泡。
            // 泡は「輪郭のリング + 左上のハイライト点」で描くと液中の気泡に見える
            half ResinBubbles(float2 uv, half scale)
            {
                float2 gv  = uv * scale;
                float2 id  = floor(gv);
                float2 f   = frac(gv);
                half   h   = Resin_hash21(id);
                float2 pos = float2(Resin_hash21(id + 3.7), Resin_hash21(id + 9.1)) * 0.5 + 0.25;
                half   r   = lerp(0.06h, 0.16h, Resin_hash21(id + 5.3));
                half   d   = length(f - pos);
                half   ring = smoothstep(r, r * 0.6h, d) - smoothstep(r * 0.55h, r * 0.2h, d);
                half   spot = smoothstep(r * 0.5h, r * 0.15h, length(f - pos + float2(-r, r) * 0.4));
                half   vis  = step(h, 0.45h);   // 泡が入るセルは半分弱だけ(まばらに)
                return saturate(ring * 0.6h + spot) * vis;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 N  = normalize(IN.normalWS);
                half3 T  = normalize(IN.tangentWS);
                half3 BT = normalize(IN.bitangentWS);
                half3 V  = normalize(IN.viewDirWS);

                float4 shadowCoord;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    shadowCoord = ComputeScreenPos(IN.positionCS);
                #else
                    shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half3 L = normalize(mainLight.direction);

                // --- ぷっくりドーム法線 ---
                // テクスチャのシルエット(アルファ)を高mipでぼかして滑らかな高さ場を作り、
                // その勾配からタンジェント空間法線を再構成。中心が盛り上がり縁で下がる丸みになる。
                float2 duv = _MainTex_TexelSize.xy * _PuffSampleDist;
                half hC = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, IN.uv, _PuffBlur).a;
                half hL = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, IN.uv - float2(duv.x, 0), _PuffBlur).a;
                half hR = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, IN.uv + float2(duv.x, 0), _PuffBlur).a;
                half hD = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, IN.uv - float2(0, duv.y), _PuffBlur).a;
                half hU = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, IN.uv + float2(0, duv.y), _PuffBlur).a;
                // 高さを球状プロファイルにリマップ: pow(<1)で縁(カバレッジが中間値)がなだらかに立ち上がり、
                // 端まで丸くロールする。_PuffEdgeを下げるほど縁がぷっくり丸くなる。
                hL = pow(saturate(hL), _PuffEdge);
                hR = pow(saturate(hR), _PuffEdge);
                hD = pow(saturate(hD), _PuffEdge);
                hU = pow(saturate(hU), _PuffEdge);
                half3 nTS = normalize(half3((hL - hR) * _Puffiness, (hD - hU) * _Puffiness, 1.0h));
                // タンジェント空間 → ワールド。以降のライティングはこの膨らんだ法線を使う
                half3 Np = normalize(nTS.x * T + nTS.y * BT + nTS.z * N);

                // 連続的なhalf-Lambert拡散(バンド無し)。シャドウマップの減衰もそのまま滑らかに反映
                half NdotL   = saturate(dot(Np, L) * 0.5h + 0.5h);
                half diffuse = max(NdotL * mainLight.shadowAttenuation, _AmbientMin);

                // --- 屈折: 膨らみ法線の傾きで絵柄を内側へ引き込む(ゼリー越しの拡大レンズ) ---
                // nTS.xy はドーム外側を向くので、逆方向にサンプル位置をずらすと中心が拡大されて見える。
                // シルエット(アルファ)は歪ませると輪郭が壊れるため元UVから取る
                // --- プリズムフラッシュスイープ: 周期的に斜めへ走る虹の帯 ---
                // 帯に垂直な射影座標 sCoord 上を、帯の中心位置 sweepPos が周期ごとに横断する。
                // 移動範囲をUV範囲より広く取ることで、帯が画面外にいる「休止時間」が自然に入る
                half sweepSin, sweepCos;
                sincos(radians(_SweepAngle), sweepSin, sweepCos);
                half sCoord   = dot(half2(IN.uv), half2(sweepCos, sweepSin));
                half sweepPos = lerp(-0.6h, 1.8h, frac(_Time.y / max(_SweepPeriod, 0.01h)));
                half split    = _SweepWidth * 0.5h * _SweepRainbow;   // RGBの帯をずらす量
                half bandR = saturate(1.0h - abs(sCoord - sweepPos + split) / _SweepWidth);
                half bandG = saturate(1.0h - abs(sCoord - sweepPos)         / _SweepWidth);
                half bandB = saturate(1.0h - abs(sCoord - sweepPos - split) / _SweepWidth);
                bandR *= bandR; bandG *= bandG; bandB *= bandB;       // 中心が鋭いプロファイル

                float2 refrUV = IN.uv - nTS.xy * _RefractStrength;
                // 色収差(分散): 屈折率がRGBでわずかに違うため、屈折の強い縁ほど虹色のフリンジが出る。
                // スイープ帯が通過中の場所は分散を強め、「光が通ると色がほどける」ようにする
                float2 disp = nTS.xy * _RefractStrength * _Dispersion * 0.25
                              * (1.0 + bandG * 2.5 * _SweepRainbow);
                half4  texCol;
                texCol.r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, refrUV + disp).r;
                texCol.g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, refrUV).g;
                texCol.b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, refrUV - disp).b;
                texCol.rgb *= _Color.rgb;
                texCol.a    = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * _Color.a;

                // --- 飴色の深み: 厚い所ほど色が濃く飽和する(透明キャンディの色の溜まり) ---
                half  thickness = pow(saturate(hC), _PuffEdge);
                half3 candy     = texCol.rgb * texCol.rgb * 1.6h;   // 2乗で彩度を上げ、明度を補償
                texCol.rgb      = lerp(texCol.rgb, candy, _ColorDepth * thickness);

                // --- 内部レジン: 視差で奥へずれる内容物の層(体積が詰まっている感) ---
                // 視点が動くと表面の像と内部の像がずれる = 「中に樹脂が入っている」一番の手がかり。
                // 厚い所ほど深くずれる(縁ではゼロ → シルエットは壊れない)
                half2  viewTS   = half2(dot(V, T), dot(V, BT));
                float2 innerUV  = IN.uv + viewTS * _InnerDepth * thickness;
                float2 innerUV2 = IN.uv + viewTS * _InnerDepth * 2.2h * thickness;
                half3  deepCol  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, innerUV2).rgb
                                  * _Color.rgb * _InnerColor.rgb;
                texCol.rgb = lerp(texCol.rgb, deepCol, 0.35h * thickness);

                // 樹脂中に浮かぶ気泡: 深さの違う2層(視差量が違うので立体的に散らばって見える)
                half bubbles = (ResinBubbles(innerUV, _BubbleScale)
                                + ResinBubbles(innerUV2 + 17.0, _BubbleScale * 0.55h) * 0.7h)
                               * _BubbleAmount * thickness;

                // 陰影部はシャドウカラーへなだらかに寄せる(擬似SSSのような柔らかい落ち影)
                half3 litColor   = texCol.rgb * mainLight.color;
                half3 shadowTint = lerp(texCol.rgb * _ShadowColor.rgb, texCol.rgb, 1.0h - _ShadowIntensity);
                half3 baseColor  = lerp(shadowTint, litColor, diffuse);

                // --- 縁の吸収: 視線が浅く入る縁ほど光路が長い → 自分の色で暗くなる ---
                // 白リムの前に暗い色の帯を入れることで、濁りのないガラスの縁になる
                half NdotV   = saturate(dot(Np, V));
                half fresnel = pow(1.0h - NdotV, _RimPower);
                baseColor = lerp(baseColor, baseColor * texCol.rgb * 0.5h, _EdgeDarken * saturate(fresnel * 1.3h));

                // Blinn-Phong: 主光源の方向に追従する鏡面ハイライト。膨らんだ法線で頂点付近に集中する
                half3 H    = normalize(L + V);
                half  spec = pow(saturate(dot(Np, H)), _SpecPower);
                // 第2グリント: 少しずれた方向の小さく硬い照り(樹脂の「キラッ」を2点にする)
                half3 H2    = normalize(H + T * 0.35h - BT * 0.25h);
                half  glint = pow(saturate(dot(Np, H2)), _SpecPower * 3.0h) * _GlintIntensity;
                half  specTerm = saturate(max(spec * _SpecIntensity, glint)) * mainLight.shadowAttenuation;
                half3 specCol  = _SpecColor.rgb * (spec * _SpecIntensity + glint) * mainLight.shadowAttenuation;

                // アンビエントシーン: 視点方向に追従する柔らかい照り返し(単一光源でも常時ツヤを残す)
                half2 spotUV  = (half2(IN.uv) - 0.5h) - viewTS * _HighlightShift;
                half  sheen   = pow(saturate(1.0h - length(spotUV)), _HighlightPower);
                half3 sheenCol = _HighlightColor.rgb * sheen * _HighlightIntensity;

                // フレネルリム: 吸収帯の外側に走る細く明るい縁のライン
                half3 rimCol = _RimColor.rgb * fresnel * _RimIntensity;

                half3 col = baseColor + specCol + sheenCol + rimCol;

                // 環境映り込み: リフレクションプローブ/スカイボックスの反射。
                // フレネル重みで正面は薄く縁ほど強く映る = 置かれた風景を拾うガラスの表面
                half3 R       = reflect(-V, Np);
                half3 envRefl = GlossyEnvironmentReflection(R, 0.15h, 1.0h);
                col += envRefl * _ReflectIntensity * lerp(0.12h, 1.0h, fresnel);

                // プリズムフラッシュ: RGBがずれた虹の帯を加算。厚い所ほど明るく=樹脂の中を通る光
                half3 sweepCol = half3(bandR, bandG, bandB) * _SweepIntensity
                                 * (0.35h + 0.65h * thickness) * texCol.a;
                col += sweepCol;

                // 気泡: 明るい輪郭として加算。陰では控えめに
                col += bubbles * half3(1, 1, 1) * (0.4h + 0.6h * diffuse);

                // 内部の光の揺らぎ: 低周波ノイズを斜めに流し、水面越しの光のように
                // 明るさが場所と時間でゆらゆら変わる係数を作る(_ShimmerAmount=0 で常に1)
                half tAnim    = _Time.y;
                half shimmerN = Resin_vnoise(IN.uv * 3.0
                                             + float2(tAnim * _ShimmerSpeed * 0.7, tAnim * _ShimmerSpeed * 0.35));
                half shimmer  = lerp(1.0h, 0.55h + 0.9h * shimmerN, _ShimmerAmount);

                // 内部散乱グロー: 光源が向こう側にある時、樹脂の中身が透けて発光する
                half backlit = pow(saturate(dot(V, -L) * 0.5h + 0.5h), 3.0h);
                col += texCol.rgb * _InnerGlow * backlit * thickness * 0.6h * shimmer;

                // コースティクス: ドームがレンズになり、光源の反対側の内壁に光が集まる「光だまり」。
                // 斜面(nTS.xy)が光と逆を向く縁で最大 → 飴の中で光が屈折して溜まって見える。
                // 揺らぎは強度(shimmer)に加え、光だまりの位置もノイズでゆっくり回して漂わせる
                half2 lightTS  = half2(dot(L, T), dot(L, BT));
                half  swayAng  = (Resin_vnoise(float2(tAnim * _ShimmerSpeed * 0.4, 7.7)) - 0.5h)
                                 * 0.9h * _ShimmerAmount;
                half  sinA, cosA;
                sincos(swayAng, sinA, cosA);
                half2 lightTSw = half2(lightTS.x * cosA - lightTS.y * sinA,
                                       lightTS.x * sinA + lightTS.y * cosA);
                half  slopeLen = saturate(length(nTS.xy));
                half  caustic  = pow(saturate(dot(normalize(nTS.xy + 0.0001h), -normalize(lightTSw + 0.0001h))), 4.0h)
                                 * slopeLen * mainLight.shadowAttenuation;
                col += texCol.rgb * mainLight.color * caustic * _CausticIntensity * shimmer;

                // 中心は下地を活かして半透明に、縁・ハイライトは不透明寄りに。
                // sheen は広範囲に出るためフル寄与させると全体が濁る → 弱めに加算
                half alpha = saturate(_BaseAlpha * texCol.a + fresnel * _EdgeAlphaBoost
                                      + specTerm + sheen * 0.35h + bubbles * 0.5h
                                      + max(max(bandR, bandG), bandB) * _SweepIntensity * 0.25h);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
