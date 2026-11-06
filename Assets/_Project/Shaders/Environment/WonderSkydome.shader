Shader "Hidden/Custom/URP/WonderSkydome"
{
    Properties
    {
        _ColorA ("Check Color A", Color) = (0.1, 0.0, 0.2, 1)
        _ColorB ("Check Color B", Color) = (0.6, 0.0, 0.4, 1)
        _HorizonColor ("Horizon Color", Color) = (1.0, 0.3, 0.6, 1)
        _CheckScale ("Check Scale", Float) = 8.0
        _DistortStrength ("Distort Strength", Float) = 0.15
        _DistortSpeed ("Distort Speed", Float) = 0.4
        _WarpScale ("Warp Scale", Float) = 3.0

        [Header(Tile 3D (Bevel))]
        _BevelWidth    ("Bevel Width",       Range(0.01, 0.49)) = 0.12
        _BevelHeight   ("Bevel Height",      Range(0.0, 2.0))   = 0.6
        _GrooveDepth   ("Groove Depth",      Range(0.0, 1.0))   = 0.35
        _SpecPower     ("Specular Power",    Range(1, 256))     = 60.0
        _SpecIntensity ("Specular Intensity",Range(0, 4))       = 0.6
        _Ambient       ("Ambient",           Range(0, 1))       = 0.6

        [Header(Toon)]
        _ToonThreshold ("Toon Threshold",    Range(0, 1))       = 0.5
        _ToonSoftness  ("Toon Softness",     Range(0.001, 0.3)) = 0.03

        [Header(Card Suits)]
        [NoScaleOffset] _SuitAtlas ("Suit Atlas (2x2: Heart/Diamond/Club/Spade)", 2D) = "black" {}
        _SuitColor ("Suit Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _SuitGridScale ("Suit Grid Scale", Float) = 6.0
        _SuitSize ("Suit Size", Range(0.01, 0.6)) = 0.2
        _SuitSpeed ("Suit Float Speed", Float) = 0.25
        _SuitOpacity ("Suit Opacity", Range(0.0, 1.0)) = 0.9

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
            Name "WonderlandSkyDome"

            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
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

            TEXTURE2D(_SuitAtlas); SAMPLER(sampler_SuitAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorB;
                float4 _HorizonColor;
                float  _CheckScale;
                float  _DistortStrength;
                float  _DistortSpeed;
                float  _WarpScale;

                float4 _SuitColor;
                float  _SuitGridScale;
                float  _SuitSize;
                float  _SuitSpeed;
                float  _SuitOpacity;

                float  _BevelWidth;
                float  _BevelHeight;
                float  _GrooveDepth;
                float  _SpecPower;
                float  _SpecIntensity;
                float  _Ambient;
                float  _ToonThreshold;
                float  _ToonSoftness;

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
                float a = hash2(i + float2(0,0)).x;
                float b = hash2(i + float2(1,0)).x;
                float c = hash2(i + float2(0,1)).x;
                float d = hash2(i + float2(1,1)).x;
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v   += amp * noise(p);
                    p   *= 2.1;
                    amp *= 0.5;
                }
                return v;
            }

            // 2x2アトラスから該当スートをサンプリング
            // 配置: 左上=Heart(0), 右上=Diamond(1), 左下=Club(2), 右下=Spade(3)
            float4 sampleSuit(float2 texUV, int suitIdx)
            {
                float2 tileOffset;
                tileOffset.x = (suitIdx == 1 || suitIdx == 3) ? 0.5 : 0.0;
                tileOffset.y = (suitIdx == 2 || suitIdx == 3) ? 0.5 : 0.0;
                float2 atlasUV = texUV * 0.5 + tileOffset;
                return SAMPLE_TEXTURE2D(_SuitAtlas, sampler_SuitAtlas, atlasUV);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS   = _WorldSpaceCameraPos + IN.positionOS.xyz;
                float4 posCS   = TransformWorldToHClip(posWS);
                posCS.z        = posCS.w; // 深度を最遠値に固定（他オブジェクトより必ず後ろ）
                OUT.positionCS = posCS;
                OUT.worldDir   = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.worldDir);

                // 歪み込み UV（3Dノイズでシームレス：球の継ぎ目でタイルが合う）
                // uv / distort はスート散布へ流用
                float2 uv, distort;
                float2 distortedUV = SkydomeWonderDistortedUV(
                    dir, _WarpScale, _DistortStrength, _DistortSpeed, uv, distort);

                // チェッカーボード（横は偶数セルに丸めて経線で色が連続）
                float sc = SkydomeEvenScale(_CheckScale);
                float2 s       = distortedUV * sc;
                float2 checker = floor(s);
                float2 cellUV  = frac(s);
                float check = fmod(checker.x + checker.y, 2.0);
                float4 col = lerp(_ColorA, _ColorB, check); // 地平線グラデーションは削除

                // ---- 立体的なタイル（ベベル法線＋溝＋ライティング）----
                // Checker.shader と同じ考え方：セル端でベベル法線を傾け、溝を暗く、太陽で陰影
                float2 distEdge   = min(cellUV, 1.0 - cellUV);
                float  grooveMask = saturate(min(distEdge.x, distEdge.y) / _BevelWidth);
                float  groove     = lerp(1.0 - _GrooveDepth, 1.0, grooveMask);

                float2 bevel  = saturate(distEdge / _BevelWidth);
                float2 bevelN = (1.0 - bevel) * sign(cellUV - 0.5) * _BevelHeight;

                // タイル接空間で計算（平面=(0,0,1)、ベベルで法線が傾く）。
                // DirectionalLight を参照せず接空間の固定ライトを使うので、
                // 全天のどのタイルも同じ陰影＝均一な明るさになる。
                float3 nT = normalize(float3(bevelN, 1.0));            // 接空間の法線
                float3 Lt = normalize(float3(-0.35, 0.45, 0.82));     // 固定ライト（左上やや手前）
                float3 Vt = float3(0.0, 0.0, 1.0);                    // 視線＝タイル正面
                float3 Ht = normalize(Lt + Vt);

                float  NdotL = dot(nT, Lt);
                float  NdotH = saturate(dot(nT, Ht));

                // トゥーン拡散：面は明るく、傾いた縁がハードに陰影
                float  litStep     = smoothstep(_ToonThreshold - _ToonSoftness,
                                                _ToonThreshold + _ToonSoftness, NdotL * 0.5 + 0.5);
                float  lightAmount = lerp(_Ambient, 1.0, litStep);
                float3 diffuse     = col.rgb * lightAmount * groove;

                // トゥーンスペキュラ：平面には乗らず、光を向いたベベル縁だけに乗る
                float  specRaw  = pow(NdotH, _SpecPower);
                float  specStep = smoothstep(0.5 - _ToonSoftness, 0.5 + _ToonSoftness, specRaw);
                float3 specular = specStep * _SpecIntensity * grooveMask;

                col.rgb = diffuse + specular;

                // ---- スート散布 ----
                float2 suitUV = uv + _DistortStrength * 0.3 * (distort - 0.5);

                // セルIDを算出（上方向に流れる）
                float2 suitGrid = suitUV * _SuitGridScale;
                float2 cellID0  = floor(suitGrid);
                float2 rndSpeed = hash2(cellID0 + float2(3.7, 8.1));
                float  speed    = _SuitSpeed * (0.6 + rndSpeed.x * 0.8);
                suitGrid.y += _Time.y * speed;

                float2 cellID  = floor(suitGrid);
                float2 fracUV  = frac(suitGrid);

                // セルごとにランダムなオフセット・サイズ・スート種類
                float2 rnd     = hash2(cellID);
                float2 offset  = (rnd - 0.5) * 0.4;
                float  sizeMult = 0.6 + rnd.y * 0.4;
                int    suitIdx  = (int)clamp(floor(hash2(cellID + float2(1.3, 5.7)).x * 4.0), 0, 3);

                // セルローカル座標をテクスチャUVに変換
                float2 localUV = fracUV - 0.5 - offset;
                float2 texUV   = localUV / (_SuitSize * sizeMult) + 0.5;

                // セル内の有効範囲外はスキップ
                float inCell = step(0.0, texUV.x) * step(texUV.x, 1.0)
                             * step(0.0, texUV.y) * step(texUV.y, 1.0);

                float4 suitSample = sampleSuit(texUV, suitIdx);

                // テクスチャのアルファをマスクに使用
                float suitMask = suitSample.a * inCell;

                // 上半球フェード
                float skyMask = saturate(dir.y * 2.0 + 0.4);
                suitMask *= skyMask;

                // 点滅
                float blink = 0.75 + 0.25 * sin(_Time.y * (1.5 + rndSpeed.y * 2.0) + rnd.x * 6.28);
                suitMask *= blink;

                col = lerp(col, float4(suitSample.rgb * _SuitColor.rgb, 1.0), suitMask * _SuitOpacity);

                // ディゾルブ・クロスフェード：溶け際に燃えるウェーブフロントを合成
                return SkydomeDissolveComposite(col.rgb, IN.worldDir, IN.positionCS.xy, _Dissolve);
            }
            ENDHLSL
        }
    }
}
