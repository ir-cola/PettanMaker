Shader "PettanMaker/Boss_01/CheshireShockwave"
{
    // チェシャ猫ボスの衝撃波リング。B1_DamageWave がプロシージャル生成する
    // 山なり＆ギザギザのドーナッツ帯メッシュ（U=円周 0..1 / V=半径 0=後端・1=先端(波面) / uv2.x=山の高さ 0..1）へ貼る前提。
    // 頂点カラー（rgb=波色 / a=帯フェード×寿命フェードの包絡線）を強度エンベロープとして使い、
    //   ・先端リムの発光（ゆらぎ付き）
    //   ・角度スクロールするハーレクイン(菱形)模様 … 不思議の国/チェシャ猫のモチーフ
    //   ・気まぐれに明滅するスパークル（チェシャ猫の消えかけの光）
    //   ・立体の高さ(uv2)によるボリューム陰影と頂点(稜線)の発光
    // をプリマルチプライド合成（発光＝加算的、縁取り＝背景を暗く遮蔽）で描く。
    // 加算のみだと明るい床・ブルームの上で波が消えて視認できないため、
    // 本体の遮蔽と波面直後の暗い縁取りで「どの背景でも読める」ようにしている。
    // ランタイム生成のため Shader.Find で参照されうる。
    Properties
    {
        [Header(Colors)]
        _RimColor     ("Rim 先端発光", Color)   = (1.0, 0.30, 0.92, 1)
        _FillColorA   ("Harlequin A", Color)     = (0.62, 0.20, 1.0, 1)
        _FillColorB   ("Harlequin B", Color)     = (0.28, 0.07, 0.60, 1)
        _SparkleColor ("Sparkle", Color)          = (1.0, 0.90, 1.0, 1)
        _Brightness   ("Brightness", Range(0.5, 12)) = 3.5
        _VertexTint   ("Vertex Color Tint", Range(0, 1)) = 0.5

        [Header(Rim Front)]
        _RimWidth ("Rim Width (先端の厚み)", Range(0.02, 1)) = 0.4
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.0

        [Header(Readability)]
        _EdgeColor      ("波面エッジライン色", Color) = (1.0, 0.95, 1.0, 1)
        _EdgeWidth      ("波面エッジラインの細さ", Range(0.01, 0.3)) = 0.07
        _EdgePulseSpeed ("エッジの明滅速度 (0=なし)", Float) = 10
        _BodyOpacity    ("本体の遮蔽（明るい床対策）", Range(0, 1)) = 0.35
        _OutlineStrength("波面直後の暗い縁取り", Range(0, 1)) = 0.55
        _OutlineWidth   ("縁取りの幅", Range(0.02, 0.6)) = 0.22

        [Header(Harlequin)]
        _CheckerU        ("Diamonds Around", Float) = 18
        _CheckerV        ("Diamonds Radial", Float) = 2
        _ScrollSpeed     ("Scroll Speed", Float) = 0.25
        _PatternContrast ("Pattern Contrast", Range(0, 1)) = 0.7

        [Header(Front Wobble)]
        _WobbleAmp   ("Wobble Amp", Range(0, 0.4)) = 0.1
        _WobbleFreq  ("Wobble Freq", Float) = 10
        _WobbleSpeed ("Wobble Speed", Float) = 5

        [Header(Sparkle)]
        _SparkleDensity ("Sparkle Density", Float) = 40
        _SparkleSpeed   ("Sparkle Speed", Float) = 1.2
        _SparkleAmount  ("Sparkle Amount", Range(0, 4)) = 1.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+10"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CheshireShockwave"
            Tags { "LightMode" = "UniversalForward" }

            // プリマルチプライド合成: 出力rgb=発光(そのまま加算)、出力a=背景の遮蔽量。
            // a=0 の部分は純加算(グロー)、a>0 の部分は背景を暗くしてから発光を乗せる。
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half4 _FillColorA;
                half4 _FillColorB;
                half4 _SparkleColor;
                half  _Brightness;
                half  _VertexTint;
                half  _RimWidth;
                half  _RimPower;
                half4 _EdgeColor;
                half  _EdgeWidth;
                float _EdgePulseSpeed;
                half  _BodyOpacity;
                half  _OutlineStrength;
                half  _OutlineWidth;
                float _CheckerU;
                float _CheckerV;
                float _ScrollSpeed;
                half  _PatternContrast;
                half  _WobbleAmp;
                float _WobbleFreq;
                float _WobbleSpeed;
                float _SparkleDensity;
                float _SparkleSpeed;
                half  _SparkleAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0; // U=円周, V=半径(0=後端,1=先端)
                float2 uvHeight   : TEXCOORD1; // x=正規化した山の高さ(0=接地,1=頂点)
                half4  color      : COLOR;      // rgb=波色, a=帯×寿命エンベロープ
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   height     : TEXCOORD1;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                OUT.height     = (half)IN.uvHeight.x;
                OUT.color      = IN.color;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float u = IN.uv.x;   // 円周 0..1
                float v = IN.uv.y;   // 半径 0=後端 1=先端(波面)
                float t = _Time.y;

                // --- 先端リム: v→1 を発光させ、角度＋時間でゆらして生き物っぽくする ---
                float wob = _WobbleAmp * sin(u * 6.28318 * _WobbleFreq + t * _WobbleSpeed);
                float vv  = saturate(v + wob);
                half rimBand = smoothstep(1.0h - _RimWidth, 1.0h, (half)vv);
                half rim     = pow(rimBand, _RimPower);

                // --- ハーレクイン(菱形)模様: 45度回転チェッカーを円周方向へスクロール ---
                // 波面付近(v→1)は模様を薄めて、エッジライン/リムが模様に埋もれないようにする。
                float su = u * _CheckerU + t * _ScrollSpeed;
                float sv = v * _CheckerV;
                half ch  = (half)fmod(floor(su + sv) + floor(su - sv), 2.0);
                half3 fillCol = lerp(_FillColorA.rgb, _FillColorB.rgb, ch);
                half3 flat    = (_FillColorA.rgb + _FillColorB.rgb) * 0.5h;
                half patternVis = (half)smoothstep(0.95, 0.7, v); // 先端に近づくほど模様を消す
                fillCol = lerp(flat, fillCol, _PatternContrast * patternVis);

                // 先端ほど明るい本体グロー
                half3 col = fillCol * _Brightness * lerp(0.35h, 1.0h, (half)v);

                // 先端リムの発光を上乗せ
                col += _RimColor.rgb * rim * _Brightness;

                // --- 波面エッジライン: 帯の最先端(v≈1)＝実際の当たり判定位置に細い高輝度ライン ---
                // 「ここに触れたら当たる」を最優先で読ませる。軽く明滅させて危険を示す。
                half edge = smoothstep(1.0h - _EdgeWidth, 1.0h, (half)vv);
                half pulse = _EdgePulseSpeed > 0.0
                    ? 0.8h + 0.2h * (half)sin(t * _EdgePulseSpeed)
                    : 1.0h;
                col += _EdgeColor.rgb * edge * _Brightness * 1.2h * pulse;

                // --- スパークル: 一部セルだけが気まぐれに明滅する（波面付近では抑えて視認性を優先） ---
                float2 sc   = float2(floor(u * _SparkleDensity), floor(v * 3.0));
                float  cell = hash21(sc);
                float  onOff = step(0.6, cell); // 約40%のセルが光る
                float  phase = frac(t * _SparkleSpeed + cell);
                half   tw    = (half)pow(saturate(1.0 - abs(phase - 0.5) * 2.0), 8.0);
                col += _SparkleColor.rgb * (onOff * tw * _SparkleAmount * patternVis);

                // --- 立体(山なり)の陰影: 頂点(height→1)ほど明るく、稜線に発光を足して壁の高さを読ませる ---
                half h = saturate(IN.height);
                col *= lerp(0.6h, 1.25h, h);                          // 高さでボリューム陰影
                half crest = smoothstep(0.6h, 1.0h, h);
                col += _RimColor.rgb * crest * _Brightness * 0.5h;    // 頂点の稜線ライン＝「ここまで跳べ」の目安

                // 頂点カラー(波色)へ色味を寄せる
                col *= lerp((half3)1.0h, IN.color.rgb, _VertexTint);

                // --- 遮蔽（明るい床対策）---
                // 本体はうっすら、波面直後は暗い縁取りで背景を沈め、加算グローが白飛びした床の上でも輪郭が読めるようにする。
                half outline = (half)smoothstep(1.0 - _RimWidth - _OutlineWidth, 1.0 - _RimWidth * 0.5, vv) * (1.0h - rim);
                half body    = 0.4h + 0.6h * (half)v; // 後端は薄く、波面へ向けて濃く
                half occl    = saturate(_BodyOpacity * body + _OutlineStrength * outline + edge * 0.6h);

                // 帯フェード×寿命フェードの包絡線 = 頂点アルファ。
                // プリマルチプライド合成: rgb=発光×包絡線、a=遮蔽×包絡線。
                half env = saturate(IN.color.a);
                return half4(col * env, occl * env);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
