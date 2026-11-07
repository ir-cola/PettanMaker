Shader "PettanMaker/Environment/OccludablePolka"
{
    // カメラとプレイヤーの間に挟まった壁・環境オブジェクトを「水玉がほわんほわんと
    // 膨らみながら抜けていく」見た目で透過させるトゥーン系シェーダー。
    //
    // 透過は2系統あり、両方の max を採用する:
    //   1. _OcclusionFade (0=不透明, 1=全消し) … CameraOcclusionSystem が Raycast で
    //      検出した Collider 付きオブジェクトへ MaterialPropertyBlock で毎フレーム書き込む。
    //   2. 自動シリンダー透過 … カメラ→プレイヤーを結ぶ世界空間の円柱
    //      (_OccludeLineStart/_OccludeLineEnd/_OccludeAutoRadius) に入った
    //      フラグメントを自動でフェード。Collider が無いオブジェクトにも効く。
    //
    // ・水玉はスクリーン空間の格子。fade に応じて各セルの穴が育ち、セルごとに
    //   位相の違う呼吸 (sin) で半径がゆらぐ = ほわんほわん。
    // ・Deferred + DBuffer デカール構成のため DepthOnly / DepthNormals / ShadowCaster
    //   にも同じ穴を開ける (深度に壁が残るとフォグ等が壁の形のまま残ってしまう)。
    // ・葉/草向けにアルファカットアウト・風揺れ・ワールド座標ノイズの2色
    //   バリエーションをオプションで持つ (Stylised Nature の ShaderGraph 置き換え用)。
    Properties
    {
        _MainTex   ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Toon Shading)]
        _ShadowColor     ("Shadow Color", Color) = (0.60, 0.55, 0.75, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowSmooth    ("Shadow Smooth", Range(0.001, 0.5)) = 0.08

        [Header(Occlusion Polka)]
        [Toggle] _OccludeAllow ("Occlude Allow (0=このマテリアルは透過させない)", Float) = 1
        _OcclusionFade   ("Occlusion Fade", Range(0, 1)) = 0
        _PolkaCell       ("Polka Cell Size (px)", Float) = 44
        _PolkaPulse      ("Polka Pulse Amp", Range(0, 0.3)) = 0.09
        _PolkaPulseSpeed ("Polka Pulse Speed", Float) = 3.2

        [Header(Alpha Cutout)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip (葉など)", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Wind)]
        [Toggle(_WIND_ON)] _Wind ("Wind Sway (草・葉)", Float) = 0
        _WindStrength   ("Wind Strength (m)", Range(0, 1)) = 0.08
        _WindSpeed      ("Wind Speed", Float) = 1.6
        _WindScale      ("Wind Spatial Scale", Float) = 0.35
        _WindHeightMask ("Wind Height Mask (0=全体, 1=UV上端ほど揺れる)", Range(0, 1)) = 0

        [Header(Color Variation)]
        [Toggle(_COLOR_VARIATION_ON)] _ColorVariation ("Color Variation (色ムラノイズ)", Float) = 0
        _ColorB          ("Second Color (ムラ/グラデ先端色)", Color) = (1, 1, 1, 1)
        _ColorNoiseScale ("Variation Noise Scale", Float) = 0.35

        [Header(Height Gradient)]
        [Toggle(_HEIGHT_GRADIENT_ON)] _HeightGradient ("Height Gradient (草: 根元=Base, 先端=Second)", Float) = 0

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _BaseColor;
            float4 _ColorB;
            float4 _ShadowColor;
            float  _ShadowThreshold;
            float  _ShadowSmooth;
            float  _OccludeAllow;
            float  _OcclusionFade;
            float  _PolkaCell;
            float  _PolkaPulse;
            float  _PolkaPulseSpeed;
            float  _Cutoff;
            float  _WindStrength;
            float  _WindSpeed;
            float  _WindScale;
            float  _WindHeightMask;
            float  _ColorNoiseScale;
        CBUFFER_END

        // プレイヤー周辺の透過窓 (CameraOcclusionSystem がグローバルで毎フレーム設定)
        //   _OccludeHoleCenter.xy : プレイヤーの正規化スクリーン座標 (0..1, 原点=左下)
        //   _OccludeHoleRadius    : 窓の半径 (画面高さ比)。0 なら窓制限なし (全面均一に穴あけ)
        // ※ px ではなく正規化で渡すのは URP レンダースケール (1.5倍など) 対策。
        //   WorldToScreenPoint のディスプレイ px と SV_Position の内部 RT px がズレるため、
        //   シェーダー側で実解像度 (_ScreenParams) の px へ復元してスケール非依存にする。
        float4 _OccludeHoleCenter;
        float  _OccludeHoleRadius;
        // 窓（楕円）の形状 (CameraOcclusionSystem が設定): x=横の広がり, y=縦の広がり, z=下げ量(半径比)
        float4 _OccludeHoleShape;

        // 自動シリンダー透過 (CameraOcclusionSystem がグローバルで毎フレーム設定)
        //   _OccludeLineStart : カメラ位置 (WS)
        //   _OccludeLineEnd   : プレイヤー胴体位置 (WS)
        //   _OccludeAutoRadius: 円柱半径 (m)。0 で自動透過オフ
        float3 _OccludeLineStart;
        float3 _OccludeLineEnd;
        float  _OccludeAutoRadius;
        float  _OccludeAutoStrength;

        // 草・葉の風揺れ。ワールド座標を位相に使い、隣り合う株が別々に揺れる。
        // 高さマスクは UV.y 基準 (草カードは根元=0 → 先端=1。メッシュの
        // object-space Y はスケール込みでほぼ 0 のため使えない)。
        // 全パス (影・深度含む) で同じ変位を掛けてズレを防ぐ。
        float3 ApplyWind(float3 positionWS, float2 uv)
        {
            #if defined(_WIND_ON)
            float mask  = lerp(1.0, saturate(uv.y), _WindHeightMask);
            float phase = dot(positionWS.xz, float2(0.31, 0.47)) * (_WindScale * 6.2831);
            float t     = _Time.y * _WindSpeed;
            float sway  = sin(t + phase) + 0.5 * sin(t * 1.73 + phase * 2.3);
            positionWS.x += sway * _WindStrength * mask;
            positionWS.z += cos(t * 0.87 + phase * 1.31) * 0.6 * _WindStrength * mask;
            #endif
            return positionWS;
        }

        // カメラ→プレイヤー間の円柱にどれだけ入っているか (0=外, _OccludeAutoStrength=芯)。
        // Collider 不要で全オブジェクトが対象になる自動透過の本体。
        float AutoOccludeFade(float3 positionWS)
        {
            if (_OccludeAutoRadius <= 0.001) return 0.0;

            float3 ab  = _OccludeLineEnd - _OccludeLineStart;
            float  len = max(length(ab), 1e-4);
            float3 dir = ab / len;
            float  t   = dot(positionWS - _OccludeLineStart, dir);

            // カメラ直前とプレイヤー付近 (足元の地面など) は対象外にする軸方向マスク
            float axial = smoothstep(0.05, 0.35, t) * (1.0 - smoothstep(len - 0.9, len - 0.45, t));
            if (axial <= 0.001) return 0.0;

            float radial = length(positionWS - (_OccludeLineStart + dir * t));
            float rad    = 1.0 - smoothstep(_OccludeAutoRadius * 0.55, _OccludeAutoRadius, radial);
            return _OccludeAutoStrength * axial * rad;
        }

        // スクリーン空間の水玉穴あけ。fade で穴が育ち、セルごとの位相ずれ sin でゆらぐ。
        // プレイヤーのスクリーン位置を中心とした円窓の内側ほど穴が大きく、縁でなめらかに 0 へ。
        // 窓の中心部 (プレイヤーのすぐ周り) は fade を 1 まで引き上げ、水玉を完全に
        // 消して全開にする (壁の切れ端がプレイヤーに被らないように)。
        void PolkaClip(float4 positionCS, float3 positionWS)
        {
            // _OccludeAllow=0 のマテリアル (フェンス・茂み・草など) は一切透過させない
            if (_OccludeAllow < 0.5) return;

            float occl = max(saturate(_OcclusionFade), AutoOccludeFade(positionWS));
            if (occl <= 0.001) return;

            float fade = occl;

            // プレイヤー周辺だけ開ける (窓の外は不透明のまま)。
            // 窓は縦 1.35 倍の縦長楕円で、中心をプレイヤーの実スクリーン位置に合わせる。
            // (注視点は移動・旋回でラグするためプレイヤーは画面中央からズレる。中央固定だと
            //  ズレた方向と逆に窓が取り残されて見える → プレイヤーへ追従させる。)
            if (_OccludeHoleRadius > 0.0001)
            {
                // ★ 必ず _ScaledScreenParams を使う (_ScreenParams ではない)。横ズレの真因はここ。
                //   positionCS(SV_Position) は URP レンダースケール後の RT px 空間 (renderScale1.5→2880px)。
                //   URP は _ScreenParams=スケール前の表示 px(1920)、_ScaledScreenParams=スケール後(2880)=SV_Position と同一空間
                //   に設定する (ScriptableRenderer.SetPerCameraShaderVariables)。_ScreenParams を使うと中心が
                //   normX*1920 になり、フラグメント(0..2880) に対し窓が左 2/3 へ寄る = 「左に結構ズレる」(実測確定)。
                float  radiusPx = _OccludeHoleRadius * _ScaledScreenParams.y;
                // 中心 = プレイヤーの正規化スクリーン座標 (0..1, 原点=左下) を RT px へ復元。
                float2 center;
                center.x = _OccludeHoleCenter.x * _ScaledScreenParams.x;
                center.y = _OccludeHoleCenter.y * _ScaledScreenParams.y;
                #if UNITY_UV_STARTS_AT_TOP
                center.y = _ScaledScreenParams.y - center.y;   // SV_Position は左上原点 (y+ が下)
                #endif
                center.y += radiusPx * _OccludeHoleShape.z;    // 窓の下げ量 (半径比。SV_Position は y+ が下方向)
                // 楕円の縦横は _OccludeHoleShape.xy (インスペクター holeStretchX/Y)。
                // 横>縦で横長、縦>横で縦長。OcclusionWaterBlur.shader の Frag と同じ値を使うこと。
                float2 p      = (positionCS.xy - center) / float2(_OccludeHoleShape.x, _OccludeHoleShape.y);
                float  dist01 = length(p) / radiusPx;
                fade = occl * smoothstep(1.0, 0.45, dist01);   // 中心=1 → 縁=0

                // コア領域 (楕円の内側 ~50%) は水玉を完全に消して全開。遮蔽している時だけ効く
                float core = 1.0 - smoothstep(0.32, 0.52, dist01);
                fade = saturate(max(fade, core * saturate(occl * 5.0)));
                if (fade <= 0.001) return;
            }

            float2 sp   = positionCS.xy / max(_PolkaCell, 1.0);
            float2 id   = floor(sp);
            float2 cell = frac(sp) - 0.5;

            // ほわんほわん: セル位置から位相を作り、穴の半径を呼吸させる。
            // fade=1 付近では脈動を絞り、全開ゾーンに切れ端が点滅しないようにする。
            float pulse = _PolkaPulse * sin(_Time.y * _PolkaPulseSpeed + id.x * 1.7 + id.y * 2.3);
            float hole  = fade * 0.78 + pulse * saturate(fade * 3.0) * saturate((1.0 - fade) * 4.0);
            clip(length(cell) - hole);
        }

        // アルファカットアウト (葉など)。_ALPHATEST_ON 時のみテクスチャの A で clip する。
        void AlphaClip(float2 uv)
        {
            #if defined(_ALPHATEST_ON)
            float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * _BaseColor.a;
            clip(a - _Cutoff);
            #endif
        }

        // 全パス共通の頂点入出力。深度系パスでもカットアウト用 uv と
        // シリンダー判定用 positionWS が必要なので共通化している。
        struct CommonAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
        };

        struct CommonVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
        };

        CommonVaryings CommonVert(CommonAttributes v)
        {
            CommonVaryings o;
            float3 posWS = TransformObjectToWorld(v.positionOS);
            posWS        = ApplyWind(posWS, v.uv);
            o.positionWS = posWS;
            o.positionCS = TransformWorldToHClip(posWS);
            o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
            o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
            return o;
        }
        ENDHLSL

        // -------------------------------------------------------
        // Depth Prepass
        // -------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex CommonVert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _WIND_ON

            float4 frag(CommonVaryings i) : SV_Target
            {
                AlphaClip(i.uv);
                PolkaClip(i.positionCS, i.positionWS);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // Depth + Normal Prepass (DBuffer デカール / SSAO / フォグ用)
        // -------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex CommonVert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _WIND_ON

            float4 frag(CommonVaryings i) : SV_Target
            {
                AlphaClip(i.uv);
                PolkaClip(i.positionCS, i.positionWS);
                return float4(normalize(i.normalWS), 0.0);
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // Shadow Caster (影にも同じ水玉穴を開ける)
        // -------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _WIND_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 GetShadowPositionHClip(float3 positionWS, float3 normalWS)
            {
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            CommonVaryings vert(CommonAttributes v)
            {
                CommonVaryings o;
                float3 posWS = TransformObjectToWorld(v.positionOS);
                posWS        = ApplyWind(posWS, v.uv);
                float3 nWS   = TransformObjectToWorldNormal(v.normalOS);
                o.positionWS = posWS;
                o.positionCS = GetShadowPositionHClip(posWS, nWS);
                o.normalWS   = nWS;
                o.uv         = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(CommonVaryings i) : SV_Target
            {
                AlphaClip(i.uv);
                PolkaClip(i.positionCS, i.positionWS);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------
        // メインカラー (簡易トゥーン: メインライト + 2バンド + SH アンビエント)
        // -------------------------------------------------------
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex CommonVert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _WIND_ON
            #pragma shader_feature_local _COLOR_VARIATION_ON
            #pragma shader_feature_local _HEIGHT_GRADIENT_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // DBuffer デカール (道・シール) を Forward でも受ける。
            // これが無いと道デカール等がこのシェーダーの面に一切描画されない
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

            // ワールド XZ の格子で作る値ノイズ。草の色ムラ用 (GrassShader の置き換え)。
            float ValueNoise(float2 p)
            {
                float2 id = floor(p);
                float2 f  = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(id,                    float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(id + float2(1.0, 0.0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(id + float2(0.0, 1.0), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(id + float2(1.0, 1.0), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float4 frag(CommonVaryings i) : SV_Target
            {
                AlphaClip(i.uv);
                PolkaClip(i.positionCS, i.positionWS);

                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float4 albedo = tex * _BaseColor;
                #if defined(_HEIGHT_GRADIENT_ON)
                // 草: 根元 (uv.y=0) = _BaseColor → 先端 (uv.y=1) = _ColorB
                albedo.rgb = tex.rgb * lerp(_BaseColor.rgb, _ColorB.rgb, saturate(i.uv.y));
                #elif defined(_COLOR_VARIATION_ON)
                float n = ValueNoise(i.positionWS.xz * _ColorNoiseScale);
                albedo.rgb = tex.rgb * lerp(_BaseColor.rgb, _ColorB.rgb, n);
                #endif

                // DBuffer デカール (道・シール) をアルベドに合成してからトゥーンライティング
                #if defined(_DBUFFER)
                half3 decaledAlbedo = albedo.rgb;
                ApplyDecalToBaseColor(i.positionCS, decaledAlbedo);
                albedo.rgb = decaledAlbedo;
                #endif

                float3 n3 = normalize(i.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light  light = GetMainLight(shadowCoord);

                float ndl  = saturate(dot(n3, light.direction)) * light.shadowAttenuation;
                float band = smoothstep(_ShadowThreshold - _ShadowSmooth,
                                        _ShadowThreshold + _ShadowSmooth, ndl);
                float3 lit     = lerp(_ShadowColor.rgb, float3(1, 1, 1), band) * light.color;
                float3 ambient = SampleSH(n3);

                float3 col = albedo.rgb * (lit + ambient * 0.35);
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
