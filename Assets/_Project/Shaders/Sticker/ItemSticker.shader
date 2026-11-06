Shader "PettanMaker/Sticker/ItemSticker"
{
    // アイテム用ぷっくりシール(マシュマロソール)。
    // ウレタン素材のような厚み・やわらかさを、平面メッシュのままフラグメントで擬似的に表現する:
    //   1. Puff   : アルファのエッジ勾配から法線を傾け、縁が丸く盛り上がって見える陰影を作る
    //   2. Dome   : シール全体をゆるく膨らませた法線(中央が手前に膨らむ)
    //   3. Gloss  : 表面のビニールフィルム的な広いスペキュラ + 視点追従スポットハイライト
    //   4. Border : アルファ膨張で検出した白フチ(フチもぷっくり陰影に乗る)
    //   5. Soft   : ラップディフューズ + 縁のフレネルで、光がまわり込むマシュマロ的やわらかさ
    // メッシュは組込み Quad / Sprite でOK(頂点変形なし)。
    Properties
    {
        _MainTex            ("Main Texture",        2D)             = "white" {}
        _Color              ("Color Tint",          Color)          = (1, 1, 1, 1)
        _AlphaCutoff        ("Alpha Cutoff",        Range(0, 1))    = 0.5
        _FillColor          ("Transparent Fill",    Color)          = (1, 1, 1, 1)

        [Header(Border)]
        _BorderColor        ("Border Color",        Color)          = (1, 1, 1, 1)
        _BorderPixels       ("Border Width (px)",   Range(0, 32))   = 6.0

        [Header(Puff)]
        _PuffPixels         ("Puff Edge Width (px)",Range(1, 64))   = 26.0
        _PuffStrength       ("Puff Strength",       Range(0, 4))    = 2.4
        _DomeStrength       ("Dome Strength",       Range(0, 2))    = 0.35

        [Header(Breathe)]
        _BreatheAmount      ("Breathe Amount",      Range(0, 1))    = 0.35
        _BreatheSpeed       ("Breathe Speed",       Range(0, 10))   = 2.2
        _WobbleAmount       ("Wobble Amount",       Range(0, 0.1))  = 0.02

        [Header(Marshmallow)]
        _Milkiness          ("Milkiness (白浮き)",   Range(0, 1))    = 0.35
        _ContactShadow      ("Contact Shadow",      Range(0, 1))    = 0.55

        [Header(Gloss)]
        _GlossColor         ("Gloss Color",         Color)          = (1, 1, 1, 1)
        _GlossIntensity     ("Gloss Intensity",     Range(0, 3))    = 0.45
        _GlossPower         ("Gloss Power",         Range(1, 128))  = 9.0
        _HighlightIntensity ("Spot Intensity",      Range(0, 3))    = 0.4
        _HighlightPower     ("Spot Power",          Range(1, 32))   = 4.0
        _HighlightShift     ("Spot Shift",          Range(0, 1))    = 0.3
        _RimIntensity       ("Rim Intensity",       Range(0, 2))    = 0.7
        _RimPower           ("Rim Power",           Range(0.5, 8))  = 2.2

        [Header(Lighting)]
        _AmbientMin         ("Ambient Min",         Range(0, 1))    = 0.7
        _ShadowStrength     ("Shadow Strength",     Range(0, 1))    = 0.2
        _PuffShade          ("Puff Shade",          Range(0, 1))    = 0.45
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }
        Pass
        {
            Name "ItemSticker"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
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
                float4 _MainTex_TexelSize;
                half4  _Color;
                half   _AlphaCutoff;
                half4  _FillColor;
                half4  _BorderColor;
                half   _BorderPixels;
                half   _PuffPixels;
                half   _PuffStrength;
                half   _DomeStrength;
                half   _BreatheAmount;
                half   _BreatheSpeed;
                half   _WobbleAmount;
                half   _Milkiness;
                half   _ContactShadow;
                half4  _GlossColor;
                half   _GlossIntensity;
                half   _GlossPower;
                half   _HighlightIntensity;
                half   _HighlightPower;
                half   _HighlightShift;
                half   _RimIntensity;
                half   _RimPower;
                half   _AmbientMin;
                half   _ShadowStrength;
                half   _PuffShade;
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
                // ぷにぷに呼吸: 板をわずかに squash & stretch (X が伸びる時 Y が縮む)。
                // 中心原点の Quad 前提。フラグメント側の脈動と同じ位相
                half wob = sin(_Time.y * _BreatheSpeed) * _WobbleAmount;
                IN.positionOS.xy *= float2(1.0 + wob, 1.0 - wob);
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

            // 8方向のアルファをサンプリングし、最小値(膨張)・勾配(エッジ法線用)・平均(ぼかし)をまとめて返す
            void SampleAlphaRing(float2 uv, float2 radius, out half minA, out half2 grad, out half avgA)
            {
                const float2 dirs[8] =
                {
                    float2( 1,  0), float2(-1,  0), float2( 0,  1), float2( 0, -1),
                    float2( 0.7071,  0.7071), float2(-0.7071,  0.7071),
                    float2( 0.7071, -0.7071), float2(-0.7071, -0.7071)
                };
                minA = 1.0h;
                grad = half2(0, 0);
                avgA = 0.0h;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + dirs[i] * radius).a;
                    minA  = min(minA, a);
                    grad += half2(dirs[i]) * a;   // アルファの高い側(=シール内側)を向く
                    avgA += a;
                }
                grad *= 0.25h;
                avgA *= 0.125h;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv  = IN.uv;
                half4  tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                // 透過部はクリップせず _FillColor(白)で埋める → 板全体がウレタンシートに見える
                half   fill = smoothstep(_AlphaCutoff - 0.05h, _AlphaCutoff + 0.05h, tex.a);

                // ---- アルファ膨張: 白フチ検出リングとぷっくり勾配リングの2重サンプル ----
                half  borderMin; half2 borderGrad; half borderAvg;
                SampleAlphaRing(uv, _MainTex_TexelSize.xy * _BorderPixels, borderMin, borderGrad, borderAvg);
                half  puffMin;   half2 puffGrad;   half puffAvg;
                SampleAlphaRing(uv, _MainTex_TexelSize.xy * _PuffPixels, puffMin, puffGrad, puffAvg);

                bool isBorder = (borderMin < _AlphaCutoff);

                // ---- Puff 法線: 枕のように肩が丸い盛り上がり + 全体をゆるく膨らませるドーム ----
                // puffGrad はシール内側を向くので、盛り上がりの斜面法線は逆向き(外側)。
                // shoulder(ぼかしアルファの中腹で最大)を掛けると、斜面が縁の中ほどに集中し
                // 頂上と裾が平らな「マシュマロの肩」プロファイルになる
                // ぷにぷに呼吸: 盛り上がりの強さを sin で脈動させる(吸うと膨らみ、吐くとしぼむ)
                half  breathe  = 1.0h + _BreatheAmount * 0.5h * sin(_Time.y * _BreatheSpeed);
                half  shoulder = puffAvg * (1.0h - puffAvg) * 4.0h;
                half2 slopeTS  = -puffGrad * _PuffStrength * breathe * (0.3h + shoulder);
                half2 domeTS   = half2(uv - 0.5) * -2.0h * _DomeStrength * breathe; // 中央が手前に膨らむ
                half3 nTS      = normalize(half3(slopeTS + domeTS, 1.0h));

                half3 T  = normalize(IN.tangentWS);
                half3 BT = normalize(IN.bitangentWS);
                half3 Ng = normalize(IN.normalWS);
                half3 N  = normalize(T * nTS.x + BT * nTS.y + Ng * nTS.z);
                half3 V  = normalize(IN.viewDirWS);

                float4 shadowCoord;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    shadowCoord = ComputeScreenPos(IN.positionCS);
                #else
                    shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half3 L = normalize(mainLight.direction);

                // ---- ラップディフューズ: 光がまわり込むウレタンのやわらかさ ----
                half NdotL   = dot(N, L) * 0.5h + 0.5h;
                half diffuse = max(NdotL * NdotL, _AmbientMin);   // 2乗でなだらかな階調に
                diffuse      = lerp(1.0h, diffuse, _ShadowStrength);
                diffuse     *= lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);

                // 盛り上がりの影側(斜面が光と逆を向く所)をひと押し暗く → 厚みが出る
                half2 lightTS  = half2(dot(L, T), dot(L, BT));
                half  slope     = saturate(length(slopeTS));
                half  slopeDark = saturate(-dot(slopeTS, lightTS) * 0.5h + 0.5h);
                diffuse *= 1.0h - _PuffShade * 0.4h * slope * slopeDark;

                half3 inkColor = isBorder ? _BorderColor.rgb : tex.rgb * _Color.rgb;
                // ミルキー白浮き: ウレタンの白い素材が透けたような、粉っぽいパステル調に持ち上げる
                inkColor = lerp(inkColor, half3(1, 1, 1), _Milkiness * 0.6h);
                // 盛り上がりの頂上ほど白く霞む → 厚みの中を光が散乱している感じ
                inkColor += _Milkiness * 0.25h * saturate(puffAvg - 0.5h) * 2.0h * nTS.z;
                half3 albedo = lerp(_FillColor.rgb, inkColor, fill);

                // ---- 接地影: ぷっくりした絵柄が台紙(白地)に落とすやわらかいドロップシャドウ ----
                // 光の方向にずらした位置のぼかしアルファ = その場所を覆う絵柄の影
                float2 dropUV   = uv + lightTS * _MainTex_TexelSize.xy * _PuffPixels * 0.9;
                half  dropMin; half2 dropGrad; half dropAvg;
                SampleAlphaRing(dropUV, _MainTex_TexelSize.xy * _PuffPixels * 0.6, dropMin, dropGrad, dropAvg);
                half  drop = dropAvg * (1.0h - fill) * _ContactShadow;
                albedo *= 1.0h - drop * half3(0.35h, 0.32h, 0.25h); // わずかに暖色寄りの影

                half3 col = albedo * diffuse * mainLight.color;

                // ---- ビニールフィルムの光沢: 広い Blinn-Phong スペキュラ ----
                half3 H    = normalize(L + V);
                half  spec = pow(saturate(dot(N, H)), _GlossPower);
                col += _GlossColor.rgb * spec * _GlossIntensity;

                // ---- 視点追従スポット: タンジェント空間ビュー方向でUVスポットを移動 ----
                half2 viewTS   = half2(dot(V, T), dot(V, BT));
                half2 spotUV   = (half2(uv) - 0.5h) - viewTS * _HighlightShift;
                half  spot     = pow(saturate(1.0h - length(spotUV)), _HighlightPower);
                col += _GlossColor.rgb * spot * _HighlightIntensity;

                // ---- 縁のフレネル: 素材色がにじむやわらかい縁あかり(擬似サブサーフェス) ----
                // 白のスペキュラではなくアルベド寄りの色で足すことで、
                // 厚みの中で光が散っているマシュマロ的なぼんやりした発光になる
                half  fresnel = pow(1.0h - saturate(dot(N, V)), _RimPower);
                half3 rimCol  = lerp(albedo * 1.3h, _GlossColor.rgb, 0.35h);
                col += rimCol * fresnel * _RimIntensity * saturate(slope + 0.3h);

                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        // ぷっくりシールらしい柔らかい接地感のため影は落とす
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                half4  _Color;
                half   _AlphaCutoff;
                half4  _FillColor;
                half4  _BorderColor;
                half   _BorderPixels;
                half   _PuffPixels;
                half   _PuffStrength;
                half   _DomeStrength;
                half   _BreatheAmount;
                half   _BreatheSpeed;
                half   _WobbleAmount;
                half   _Milkiness;
                half   _ContactShadow;
                half4  _GlossColor;
                half   _GlossIntensity;
                half   _GlossPower;
                half   _HighlightIntensity;
                half   _HighlightPower;
                half   _HighlightShift;
                half   _RimIntensity;
                half   _RimPower;
                half   _AmbientMin;
                half   _ShadowStrength;
                half   _PuffShade;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings shadowVert(Attributes IN)
            {
                Varyings OUT;
                // メインパスのぷにぷに呼吸と同位相で影も伸び縮みさせる
                half wob = sin(_Time.y * _BreatheSpeed) * _WobbleAmount;
                IN.positionOS.xy *= float2(1.0 + wob, 1.0 - wob);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 shadowFrag(Varyings IN) : SV_Target
            {
                // 透過部も白で埋まる全面不透明シートなので、板全体で影を落とす
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
