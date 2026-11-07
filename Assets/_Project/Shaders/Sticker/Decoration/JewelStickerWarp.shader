Shader "PettanMaker/Sticker/JewelStickerWarp"
{
    // ぷっくり宝石デコシール (ハイライト歪みバージョン)。
    // JewelSticker をベースに、ノイズのドメインワープでハイライトを大きく歪ませる:
    //   ・サークルハイライトが真円ではなく手描き風の有機的な形に潰れる
    //   ・法線にもノイズ揺らぎを加え、スペキュラ / 虹色 / リムが不均一にゆらめく
    // 均一で綺麗な円が欲しい場合は元の PettanMaker/Sticker/JewelSticker を使う。
    Properties
    {
        _MainTex            ("Main Texture",        2D)             = "white" {}
        _Color              ("Color Tint",          Color)          = (1, 1, 1, 1)
        _AlphaCutoff        ("Alpha Cutoff",        Range(0, 1))    = 0.5

        [Header(Normal Map)]
        [Normal] _BumpMap   ("Normal Map",          2D)             = "bump" {}
        _BumpScale          ("Normal Strength",     Range(0, 3))    = 1.0

        [Header(Highlight Warp)]
        _WarpStrength       ("Warp Strength",       Range(0, 0.2))  = 0.06
        _WarpScale          ("Warp Noise Scale",    Range(1, 30))   = 8.0
        _WarpNormal         ("Normal Wobble",       Range(0, 1))    = 0.3

        [Header(Shine Sweep)]
        _ShineColor         ("Shine Color",         Color)          = (1, 1, 1, 1)
        _ShineIntensity     ("Shine Intensity",     Range(0, 3))    = 1.0
        _ShineWidth         ("Shine Width",         Range(0.02, 0.5)) = 0.12
        _ShineInterval      ("Shine Interval (s)",  Range(0.5, 10)) = 3.0
        _ShineAngle         ("Shine Angle (deg)",   Range(0, 180))  = 60.0

        [Header(Lighting)]
        _ShadowColor        ("Shadow Color",        Color)          = (0.55, 0.45, 0.7, 1)
        _ShadowIntensity    ("Shadow Intensity",    Range(0, 1))    = 0.55
        _AmbientMin         ("Ambient Min",         Range(0, 1))    = 0.4

        [Header(Puff Dome Shape)]
        _Puffiness          ("Puffiness",           Range(0, 6))    = 2.5
        _PuffBlur           ("Puff Blur (mip)",     Range(0, 8))    = 5.0
        _PuffSampleDist     ("Puff Sample Dist (texels)", Range(1, 96)) = 20.0
        _PuffEdge           ("Puff Edge Rounding (low=round)", Range(0.2, 1)) = 0.4

        [Header(Gem Depth)]
        _DepthColor         ("Edge Deep Color",     Color)          = (0.35, 0.1, 0.45, 1)
        _DepthStrength      ("Edge Deepening",      Range(0, 1))    = 0.55
        _InnerGlow          ("Inner Glow (center boost)", Range(0, 1)) = 0.35

        [Header(Gem Inside)]
        _RefractStrength    ("Inner Refraction",    Range(0, 0.2))  = 0.06
        _InnerReflect       ("Transmitted Light Ring", Range(0, 3)) = 1.0
        _GemSaturation      ("Gem Color Boost",     Range(0, 1))    = 0.35

        [Header(Specular)]
        _SpecColor          ("Specular Color",      Color)          = (1, 1, 1, 1)
        _SpecIntensity      ("Specular Intensity",  Range(0, 6))    = 2.5
        _SpecPower          ("Specular Power",      Range(1, 512))  = 180.0
        _Spec2Intensity     ("Broad Gloss Intensity", Range(0, 3))  = 0.6
        _Spec2Power         ("Broad Gloss Power",   Range(1, 64))   = 14.0

        [Header(Painted Circle Highlight)]
        _CircleColor        ("Circle Color",        Color)          = (1, 1, 1, 1)
        _CircleIntensity    ("Circle Intensity",    Range(0, 2))    = 1.0
        _CircleSize         ("Circle Size",         Range(0, 0.5))  = 0.12
        _CircleSoft         ("Circle Edge Softness", Range(0.001, 0.2)) = 0.01
        _CircleOffset       ("Circle Offset (x,y)", Vector)         = (0, 0, 0, 0)
        _Circle2Size        ("Small Circle Size",   Range(0, 0.3))  = 0.05
        _Circle2Offset      ("Small Circle Offset (xy) / 3rd (zw)", Vector) = (0.25, -0.18, -0.1, -0.3)

        [Header(Painted Circle Highlight Set B)]
        _CircleBColor       ("B Circle Color",      Color)          = (1, 1, 1, 1)
        _CircleBIntensity   ("B Circle Intensity",  Range(0, 2))    = 0.0
        _CircleBSize        ("B Circle Size",       Range(0, 0.5))  = 0.08
        _CircleBOffset      ("B Circle Offset (x,y)", Vector)       = (-0.3, 0.2, 0, 0)
        _CircleB2Size       ("B Small Circle Size", Range(0, 0.3))  = 0.035
        _CircleB2Offset     ("B Small Circle Offset (xy) / 3rd (zw)", Vector) = (0.18, -0.12, -0.08, -0.22)

        [Header(Iridescence)]
        _IridIntensity      ("Iridescence Intensity", Range(0, 2))  = 0.7
        _IridScale          ("Rainbow Repeat",      Range(0.5, 8))  = 2.5
        _IridShift          ("Rainbow Phase",       Range(0, 1))    = 0.0
        _IridSaturation     ("Rainbow Saturation",  Range(0, 1))    = 0.8

        [Header(Glitter)]
        _GlitterIntensity   ("Glitter Intensity",   Range(0, 4))    = 1.5
        _GlitterScale       ("Glitter Density",     Range(10, 400)) = 120.0
        _GlitterThreshold   ("Glitter Sparsity",    Range(0.5, 0.99)) = 0.9
        _GlitterColor       ("Glitter Color",       Color)          = (1, 1, 1, 1)

        [Header(Rim)]
        _RimColor           ("Rim Color",           Color)          = (1, 0.9, 1, 1)
        _RimIntensity       ("Rim Intensity",       Range(0, 3))    = 1.2
        _RimPower           ("Rim Power",           Range(0.5, 8))  = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "TransparentCutout"
            "Queue"          = "AlphaTest"
        }

        // Pass 1 : ShadowCaster (シールの型抜き形状で影を落とす)
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

            // Pass2(UniversalForward)と同一レイアウトにして SRP Batcher の互換性を保つ
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                half4  _Color;
                half   _AlphaCutoff;
                half   _BumpScale;
                half   _WarpStrength;
                half   _WarpScale;
                half   _WarpNormal;
                half4  _ShadowColor;
                half   _ShadowIntensity;
                half   _AmbientMin;
                half   _Puffiness;
                half   _PuffBlur;
                half   _PuffSampleDist;
                half   _PuffEdge;
                half4  _DepthColor;
                half   _DepthStrength;
                half   _InnerGlow;
                half4  _SpecColor;
                half   _SpecIntensity;
                half   _SpecPower;
                half   _Spec2Intensity;
                half   _Spec2Power;
                half4  _CircleColor;
                half   _CircleIntensity;
                half   _CircleSize;
                half   _CircleSoft;
                float4 _CircleOffset;
                half   _Circle2Size;
                float4 _Circle2Offset;
                half4  _CircleBColor;
                half   _CircleBIntensity;
                half   _CircleBSize;
                float4 _CircleBOffset;
                half   _CircleB2Size;
                float4 _CircleB2Offset;
                half   _IridIntensity;
                half   _IridScale;
                half   _IridShift;
                half   _IridSaturation;
                half   _GlitterIntensity;
                half   _GlitterScale;
                half   _GlitterThreshold;
                half4  _GlitterColor;
                half4  _RimColor;
                half   _RimIntensity;
                half   _RimPower;
                half   _RefractStrength;
                half   _InnerReflect;
                half   _GemSaturation;
                half4  _ShineColor;
                half   _ShineIntensity;
                half   _ShineWidth;
                half   _ShineInterval;
                half   _ShineAngle;
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
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                clip(alpha - _AlphaCutoff);
                return 0;
            }
            ENDHLSL
        }

        // Pass 2 : メインカラー (宝石シール本体・歪みハイライト)
        Pass
        {
            Name "JewelStickerWarp"
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
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                half4  _Color;
                half   _AlphaCutoff;
                half   _BumpScale;
                half   _WarpStrength;
                half   _WarpScale;
                half   _WarpNormal;
                half4  _ShadowColor;
                half   _ShadowIntensity;
                half   _AmbientMin;
                half   _Puffiness;
                half   _PuffBlur;
                half   _PuffSampleDist;
                half   _PuffEdge;
                half4  _DepthColor;
                half   _DepthStrength;
                half   _InnerGlow;
                half4  _SpecColor;
                half   _SpecIntensity;
                half   _SpecPower;
                half   _Spec2Intensity;
                half   _Spec2Power;
                half4  _CircleColor;
                half   _CircleIntensity;
                half   _CircleSize;
                half   _CircleSoft;
                float4 _CircleOffset;
                half   _Circle2Size;
                float4 _Circle2Offset;
                half4  _CircleBColor;
                half   _CircleBIntensity;
                half   _CircleBSize;
                float4 _CircleBOffset;
                half   _CircleB2Size;
                float4 _CircleB2Offset;
                half   _IridIntensity;
                half   _IridScale;
                half   _IridShift;
                half   _IridSaturation;
                half   _GlitterIntensity;
                half   _GlitterScale;
                half   _GlitterThreshold;
                half4  _GlitterColor;
                half4  _RimColor;
                half   _RimIntensity;
                half   _RimPower;
                half   _RefractStrength;
                half   _InnerReflect;
                half   _GemSaturation;
                half4  _ShineColor;
                half   _ShineIntensity;
                half   _ShineWidth;
                half   _ShineInterval;
                half   _ShineAngle;
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

            // 2D ハッシュ (グリッター粒・ノイズの乱数)
            float2 JewelHash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // 2オクターブのバリューノイズ (ドメインワープ用)
            float JewelVNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = JewelHash22(i).x;
                float b = JewelHash22(i + float2(1, 0)).x;
                float c = JewelHash22(i + float2(0, 1)).x;
                float d = JewelHash22(i + float2(1, 1)).x;
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // UV を歪ませるワープベクトル (中心 0 の符号付き)
            float2 JewelWarp(float2 uv)
            {
                float2 p = uv * _WarpScale;
                float nx = JewelVNoise(p)         * 0.7 + JewelVNoise(p * 2.3)         * 0.3;
                float ny = JewelVNoise(p + 17.31) * 0.7 + JewelVNoise(p * 2.3 + 9.17) * 0.3;
                return (float2(nx, ny) - 0.5) * 2.0 * _WarpStrength;
            }

            // アニメ塗り風サークル 1 組 (大円 + 小円2つ) のマスクを返す。
            // 渡される uv は事前にドメインワープ済みなので、円が有機的に歪む。
            half JewelCircleSet(float2 uv, float2 sway,
                                float2 mainOff, half mainSize,
                                half smallSize, float4 smallOff, half soft)
            {
                float2 c1 = float2(0.5, 0.5) + mainOff + sway;
                half  d1  = length(uv - c1);
                half  c   = 1.0h - smoothstep(mainSize - soft, mainSize + soft, d1);
                half  d2  = length(uv - (c1 + smallOff.xy));
                c        += 1.0h - smoothstep(smallSize - soft, smallSize + soft, d2);
                // 小円その2 (さらに小さく)
                half  d3  = length(uv - (c1 + smallOff.zw));
                c        += 1.0h - smoothstep(smallSize * 0.5h - soft, smallSize * 0.5h + soft, d3);
                return saturate(c);
            }

            // コサインパレット虹色: t が 1 進むごとに色相が一周する
            half3 JewelRainbow(half t)
            {
                half3 rainbow = 0.5h + 0.5h * cos(6.28318h * (t + half3(0.0h, 0.33h, 0.67h)));
                return lerp(half3(1, 1, 1) * dot(rainbow, half3(0.33h, 0.34h, 0.33h)), rainbow, _IridSaturation);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv  = IN.uv;
                half4  tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                clip(tex.a - _AlphaCutoff);

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

                // ドメインワープ: このシェーダーの核。ハイライト系すべてを歪ませる
                float2 warp = JewelWarp(uv);

                // --- ぷっくりドーム法線 (ResinDome と同手法) ---
                // 高 mip でぼかしたアルファの勾配 → 中心が盛り上がる滑らかな法線
                float2 duv = _MainTex_TexelSize.xy * _PuffSampleDist;
                half hL = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv - float2(duv.x, 0), _PuffBlur).a;
                half hR = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv + float2(duv.x, 0), _PuffBlur).a;
                half hD = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv - float2(0, duv.y), _PuffBlur).a;
                half hU = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv + float2(0, duv.y), _PuffBlur).a;
                hL = pow(saturate(hL), _PuffEdge);
                hR = pow(saturate(hR), _PuffEdge);
                hD = pow(saturate(hD), _PuffEdge);
                hU = pow(saturate(hU), _PuffEdge);
                half3 nTS = normalize(half3((hL - hR) * _Puffiness, (hD - hU) * _Puffiness, 1.0h));

                // --- 法線の揺らぎ: ワープを法線にも加え、スペキュラ/虹色/リムを不均一に歪ませる ---
                nTS = normalize(half3(nTS.xy + half2(warp) * (_WarpNormal * 6.0h), nTS.z));

                // --- ノーマルマップ合成 ---
                // ドーム法線とタンジェント空間で XY を加算合成 (未設定時は "bump" 既定値 → 無影響)
                half3 nMap = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
                nTS = normalize(half3(nTS.xy + nMap.xy, nTS.z * nMap.z));

                half3 Np  = normalize(nTS.x * T + nTS.y * BT + nTS.z * N);

                // ドーム高さ (0=縁 / 1=頂点)。縁の色沈みと内側の透過光に使う
                half hC = pow(saturate((hL + hR + hD + hU) * 0.25h), _PuffEdge);

                // --- 内部屈折: ドーム法線の傾きで絵をずらして二重にサンプル ---
                // 中心(法線が正面向き)ではずれず、縁ほど絵が屈折して見える → ガラス玉の奥行き感
                float2 refrUV = uv - nTS.xy * _RefractStrength;
                half3  refr   = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, refrUV).rgb;
                tex.rgb = lerp(tex.rgb, refr, 0.45h);

                // --- ベースの陰影 (half-Lambert + シャドウカラー寄せ) ---
                half NdotL   = saturate(dot(Np, L) * 0.5h + 0.5h);
                half diffuse = max(NdotL * mainLight.shadowAttenuation, _AmbientMin);
                half3 litColor   = tex.rgb * _Color.rgb * mainLight.color;
                half3 shadowTint = lerp(litColor * _ShadowColor.rgb, litColor, 1.0h - _ShadowIntensity);
                half3 baseColor  = lerp(shadowTint, litColor, diffuse);

                // --- 宝石の深み: 縁ほど濃い色に沈み、中心は光が透けて明るく ---
                half edgeDepth = 1.0h - hC;
                baseColor = lerp(baseColor, baseColor * _DepthColor.rgb, edgeDepth * _DepthStrength);
                baseColor *= 1.0h + _InnerGlow * hC * diffuse;

                // --- 宝石の彩度ブースト: 色ガラスらしく色を深く濃く ---
                half lum = dot(baseColor, half3(0.299h, 0.587h, 0.114h));
                baseColor = max(lerp(half3(lum, lum, lum), baseColor, 1.0h + _GemSaturation), 0.0h);

                // --- 透過光リング: 光源の反対側の内縁が、ガラスを通った光でぼうっと明るむ ---
                half ringBand = smoothstep(0.05h, 0.25h, hC) * smoothstep(0.55h, 0.30h, hC);
                half transmit = saturate(dot(Np, -L) * 0.5h + 0.5h);
                half3 innerCol = tex.rgb * _Color.rgb * mainLight.color
                               * ringBand * transmit * transmit * _InnerReflect;

                // --- 鋭い点光 + 広いツヤ (2 段スペキュラ。法線揺らぎで形が崩れる) ---
                half3 H       = normalize(L + V);
                half  NdotH   = saturate(dot(Np, H));
                half  specHot = pow(NdotH, _SpecPower)  * _SpecIntensity;
                half  specSoft= pow(NdotH, _Spec2Power) * _Spec2Intensity;
                half3 specCol = _SpecColor.rgb * (specHot + specSoft) * mainLight.shadowAttenuation;

                // --- アニメ塗り風サークルハイライト (2 組: A / B) ---
                // ワープ済み UV に対して円を描くので、輪郭が有機的にぐにゃりと歪む
                float2 warpedUV = uv + warp;
                half2  lightTS  = half2(dot(L, T), dot(L, BT));
                half2  viewTShl = half2(dot(V, T), dot(V, BT));
                float2 sway     = (lightTS + viewTShl) * 0.06;
                half circleA = JewelCircleSet(warpedUV, sway,
                                              _CircleOffset.xy, _CircleSize,
                                              _Circle2Size, _Circle2Offset, _CircleSoft);
                half circleB = JewelCircleSet(warpedUV, sway,
                                              _CircleBOffset.xy, _CircleBSize,
                                              _CircleB2Size, _CircleB2Offset, _CircleSoft);

                // --- イリデッセンス: 視線が傾くほど虹の位相が流れる ---
                half NdotV = saturate(dot(Np, V));
                half iridT = NdotV * _IridScale + _IridShift + dot(warpedUV, half2(0.7h, 0.4h));
                half3 iridCol = JewelRainbow(iridT) * _IridIntensity;
                // 正面では控えめ・斜めと縁で強く出す (薄膜反射らしい角度依存)
                half iridMask = pow(1.0h - NdotV, 1.5h) * 0.8h + 0.2h;
                iridCol *= iridMask;

                // --- グリッター: UV セルごとの乱数法線が視線とほぼ一致した粒だけ瞬く ---
                float2 cell = floor(uv * _GlitterScale);
                float2 rnd  = JewelHash22(cell);
                // 粒ごとのランダム反射方向をタンジェント空間の視線と突き合わせる
                half  sparkle  = frac(rnd.x + dot(viewTShl, (rnd - 0.5h) * 4.0h));
                sparkle = smoothstep(_GlitterThreshold, 1.0h, sparkle);
                sparkle *= step(0.5h, rnd.y + hC);   // 縁ギリギリの粒は間引く
                half3 glitterCol = _GlitterColor.rgb * sparkle * _GlitterIntensity
                                 * lerp(half3(1, 1, 1), JewelRainbow(rnd.x * 3.0h), 0.5h);

                // --- フレネルリム (縁の輝き) ---
                half  fresnel = pow(1.0h - NdotV, _RimPower);
                half3 rimCol  = _RimColor.rgb * fresnel * _RimIntensity;

                half3 col = baseColor + innerCol + specCol + iridCol + glitterCol + rimCol;

                // サークルは加算ではなく上塗り(lerp)で乗せる。
                // 加算だと黒などの暗色が消えるため、ペンで描いたように下地色を置き換える。
                // Intensity は不透明度として働く (1で完全上書き、>1 は縁が締まる)
                col = lerp(col, _CircleColor.rgb,  saturate(circleA * _CircleIntensity));
                col = lerp(col, _CircleBColor.rgb, saturate(circleB * _CircleBIntensity));

                // --- シャインスイープ: 斜めの光の帯が一定間隔でシール表面を走り抜ける ---
                // フォイル面の反射のように全要素の上に乗せる (円ハイライトの上からも光る)。
                // ワープ済み UV を使うので帯の縁も有機的に歪む
                half  rad      = radians(_ShineAngle);
                half  sProj    = dot(warpedUV - 0.5, half2(cos(rad), sin(rad))) + 0.5h; // 帯方向への射影 (~0..1)
                half  sweepT   = frac(_Time.y / max(_ShineInterval, 0.1h));
                half  sweepPos = lerp(-_ShineWidth, 1.0h + _ShineWidth, sweepT);        // 画面外から画面外へ
                half  shine    = saturate(1.0h - abs(sProj - sweepPos) / max(_ShineWidth, 1e-3h));
                shine *= shine;                                                         // 帯の中心ほど明るく
                col += _ShineColor.rgb * shine * _ShineIntensity * (0.5h + 0.5h * hC);

                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
