Shader "PettanMaker/Boss_01/TelegraphLine"
{
    // Boss01 の突進予告ライン。地面に寝かせた帯状ポリ（U=横幅 0..1 / V=長さ 0=手前(ボス) 1=先端）へ貼る。
    // TelegraphCircle と同系の見た目（プレイヤー武器デカール Effects/AttackLandingCircle 準拠）：
    //   ・中: チェック柄。2トーンが位相差で明滅し、突進方向（先端側）へ流れる
    //   ・縁: 左右の側線と先端キャップが sin 揺らぎでゆらゆら動く輪郭線
    //   ・横幅の中心は透明で、左右の縁へ向かって濃くなるエンベロープ
    //   ・手前（ボスの足元側）はフェードインして自然につながる
    // チェックのセルは _Aspect（長さ/横幅。スクリプトが毎フレーム更新）で正方形を保つ。
    // 色は _EdgeColor 1色が基準（TelegraphCircle と同じ作り）。_Fade はスクリプトの出現フェード用。
    // ※ ランタイム専用のため Shader.Find だけの参照だとビルドで剥がされることがある。
    //   ビルドで確実に出す場合は本シェーダのマテリアルを config の TelegraphMaterial へ割り当てること。
    Properties
    {
        [Header(Colors)]
        _EdgeColor ("基準色（赤）", Color) = (1, 0.12, 0.08, 0.9)
        _HotMix    ("輪郭線の白寄せ (0=基準色のまま)", Range(0, 1)) = 0.6
        _GradientPower ("中心→縁の濃度勾配 (小=中央から濃い / 大=縁だけ濃い)", Range(0.25, 6)) = 1.8

        [Header(Checker)]
        _CheckerTiles  ("チェックのタイル数（横幅あたり）", Float) = 3
        _ScrollSpeed   ("チェックの流れ速度 (セル/秒。＋で先端方向へ)", Float) = 1.5
        _ShadeSpeed    ("濃淡アニメ速度", Float) = 2.2
        _CheckerToneA  ("トーンA 明度", Range(0, 2)) = 1.0
        _CheckerToneB  ("トーンB 明度", Range(0, 2)) = 0.6
        _CheckerAlphaA ("トーンA 不透明度", Range(0, 1)) = 0.30
        _CheckerAlphaB ("トーンB 不透明度", Range(0, 1)) = 0.45

        [Header(Wobble Edge)]
        _EdgeInset   ("輪郭線の位置（横幅に対する割合）", Range(0.01, 0.3)) = 0.08
        _EdgeWidth   ("輪郭線の太さ（横幅に対する割合）", Range(0.01, 0.15)) = 0.04
        _WobbleAmp   ("輪郭の揺らぎ幅", Range(0, 0.15)) = 0.03
        _WobbleFreq  ("輪郭の揺らぎ周波数", Float) = 4
        _WobbleSpeed ("輪郭の揺らぎ速度", Float) = 2.6

        [Header(Animation)]
        _StartFade   ("手前のフェード区間 (V 0..1)", Range(0, 0.5)) = 0.12
        _PulseSpeed  ("明滅速度 (Hz)", Float) = 2.2
        _PulseAmount ("明滅の深さ", Range(0, 1)) = 0.2

        [Header(Charge Wipe)]
        _SweepStrength ("チャージアップの強さ (0=無効)", Range(0, 1)) = 1
        _SweepDimming  ("未チャージ部分の暗さ (小=はっきり差が出る)", Range(0, 1)) = 0.3
        _SweepLeadGlow ("チャージ先端の光る帯の太さ (V 単位)", Range(0.01, 0.3)) = 0.08

        [Header(Launch Flash)]
        _FlashColor ("発射フラッシュ色", Color) = (1, 1, 1, 1)

        [Header(Sticker Peel)]
        _Peel ("Peel (0=地面に密着, 1=めくれ上がり。スクリプト制御)", Range(0, 1)) = 0
        _CurlRadius ("Curl Radius (板ローカル)", Range(0.02, 0.35)) = 0.24
        _DirX ("Peel Dir X", Float) = 0
        _DirY ("Peel Dir Y", Float) = 1
        _HideUnpeeled ("Hide Unpeeled (貼り付け中の未接着を隠す。1=有効)", Range(0, 1)) = 0

        [Header(Runtime)]
        _Aspect  ("長さ/横幅（スクリプト制御）", Float) = 4
        _Urgency ("タメ進行度 (0=タメ開始, 1=発射直前。スクリプト制御)", Range(0, 1)) = 1
        _Flash   ("発射フラッシュ量 (スクリプト制御。0=無)", Range(0, 1)) = 0
        _Fade    ("Fade (0=非表示, 1=表示。スクリプト制御)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+5"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "TelegraphLine"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Sticker/StickerCore.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColor;
                half  _HotMix;
                half  _GradientPower;
                float _CheckerTiles;
                float _ScrollSpeed;
                float _ShadeSpeed;
                half  _CheckerToneA;
                half  _CheckerToneB;
                half  _CheckerAlphaA;
                half  _CheckerAlphaB;
                float _EdgeInset;
                float _EdgeWidth;
                float _WobbleAmp;
                float _WobbleFreq;
                float _WobbleSpeed;
                float _StartFade;
                float _PulseSpeed;
                half  _PulseAmount;
                half  _SweepStrength;
                half  _SweepDimming;
                half  _SweepLeadGlow;
                half4 _FlashColor;
                float _Peel;
                float _CurlRadius;
                float _DirX;
                float _DirY;
                float _HideUnpeeled;
                float _Aspect;
                half  _Urgency;
                half  _Flash;
                half  _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // シールの円筒ページカール（分割グリッド前提）。
                // _Peel 1→0 で地面へ密着（貼る）、0→1 でめくれ上がる（剥がす）。
                // 板は XY 平面（±0.5）・表 −Z。長さ方向（板 Y）に沿って剥がす（_Dir=(0,1)）。
                float2 dir = normalize(float2(_DirX, _DirY) + 1e-5);
                float2 xyOut; float lift; float curl;
                Sticker_CurlVertex(IN.positionOS.xy, dir, _Peel, _CurlRadius, xyOut, lift, curl);
                float3 posOS = float3(xyOut, -lift);

                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float u  = IN.uv.x;            // 横幅 0..1（0.5 が中心線）
                float v  = IN.uv.y;            // 長さ 0=手前(ボス) 1=先端
                float vw = v * _Aspect;        // 長さ方向を「横幅単位」に換算（太さ・周波数を等方に保つ）
                float t  = _Time.y;

                // 輪郭線の色：基準色を白へ寄せて作る（config の1色で全体が決まる）
                half3 hot = lerp(_EdgeColor.rgb, half3(1.0h, 1.0h, 1.0h), _HotMix);

                // --- 左右の側線（ゆらぐ輪郭。TelegraphCircle と同じ2重 sin） ---
                float d = min(u, 1.0 - u);     // 側端からの距離 0..0.5
                float wob = _WobbleAmp * sin(vw * _WobbleFreq + t * _WobbleSpeed)
                          + _WobbleAmp * 0.6 * sin(vw * (_WobbleFreq * 0.53) - t * (_WobbleSpeed * 1.7) + 1.7);
                float b = _EdgeInset + wob;    // 側線の中心位置（側端からの距離）
                half sideBand = 1.0h - (half)smoothstep(_EdgeWidth * 0.3, _EdgeWidth, abs(d - b));
                half insideU  = (half)smoothstep(b - 0.02, b + 0.015, d); // 側線より内側だけフィル

                // --- 先端キャップ（横方向にゆらぐ端線） ---
                float de = (1.0 - v) * _Aspect; // 先端からの距離（横幅単位）
                float wobE = _WobbleAmp * sin(u * _WobbleFreq * 2.0 - t * _WobbleSpeed + 0.8);
                float bE = _EdgeInset + wobE;
                half capBand  = 1.0h - (half)smoothstep(_EdgeWidth * 0.3, _EdgeWidth, abs(de - bE));
                half insideV  = (half)smoothstep(bE - 0.02, bE + 0.015, de); // キャップより手前だけフィル

                // --- チェック柄（正方形セル。先端方向へ流れる） ---
                float2 cuv = float2(u * _CheckerTiles, vw * _CheckerTiles - t * _ScrollSpeed);
                half ch = (half)abs(fmod(floor(cuv.x) + floor(cuv.y), 2.0));

                // 2トーンが位相差で明滅する濃淡アニメ（プレイヤーデカール準拠）
                half s = 0.5h + 0.5h * (half)sin(t * _ShadeSpeed + ch * 3.14159);
                half tone  = lerp(_CheckerToneA,  _CheckerToneB,  ch) * lerp(0.8h,  1.3h,  s);
                half fillA = lerp(_CheckerAlphaA, _CheckerAlphaB, ch) * lerp(0.75h, 1.15h, s);

                half3 col = _EdgeColor.rgb * tone;

                // --- 横幅の中心は透明 → 左右の縁へ向かって濃くなるエンベロープ ---
                half g = (half)pow(saturate(1.0 - d * 2.0), _GradientPower);
                half alpha = fillA * g * _EdgeColor.a * insideU * insideV;

                // --- ゆらぐ輪郭線（側線＋先端キャップ） ---
                half band = max(sideBand, capBand);
                col   = lerp(col, hot, band);
                alpha = max(alpha, band * _EdgeColor.a);

                // --- 手前（ボスの足元側）はフェードイン ---
                alpha *= (half)smoothstep(0.0, max(_StartFade, 0.001), v);

                // --- チャージアップ：タメが進むと手前(v=0)から先端(v=1)へ塗りが伸びる ---
                // v が _Urgency 未満の区間が「チャージ済み」。未チャージ側（先端寄り）は暗く沈む。
                half charged   = (half)step(v, _Urgency);            // 1=チャージ済み
                half chargeMul = lerp(_SweepDimming, 1.0h, charged); // 未チャージ側だけ暗くする
                alpha *= lerp(1.0h, chargeMul, _SweepStrength);      // _SweepStrength=0 で無効

                // チャージ先端に光る帯（進行中の切っ先＝どこまで満ちたかが見える）
                half lead = (half)(smoothstep(_Urgency - _SweepLeadGlow, _Urgency, v) * step(v, _Urgency));
                lead *= _SweepStrength * insideU * (1.0h - _Flash);  // 発射フラッシュ中は先端表現を隠す
                lead *= (half)saturate((1.0 - _Urgency) * 8.0);      // 満タン時は先端の帯を消す（先端の残りを防ぐ）
                col   = lerp(col, hot, lead);
                alpha = max(alpha, lead * _EdgeColor.a);

                // --- 明滅パルス × スクリプトフェード ---
                half pulse = lerp(1.0h - _PulseAmount, 1.0h,
                                  0.5h + 0.5h * (half)sin(t * _PulseSpeed * 6.28318));
                alpha *= pulse * _Fade;

                // --- 発射フラッシュ：チャージ満タン＝突進開始の瞬間にライン全体が白く弾ける ---
                half fillMask = insideU * insideV; // 帯の内側
                col   = lerp(col, _FlashColor.rgb, _Flash);
                alpha = max(alpha, _Flash * _FlashColor.a * fillMask * _Fade);

                // --- シール貼り付けの進行：未接着（クリースより手前）を隠し、接着済み領域が時間で広がる ---
                //     出現中だけ有効化（_HideUnpeeled=1）。長さ方向（板 Y＝手前→先端）に貼られていく。
                if (_HideUnpeeled > 0.5)
                {
                    float2 dirp    = normalize(float2(_DirX, _DirY) + 1e-5);
                    float  halfSpan = 0.5 * (abs(dirp.x) + abs(dirp.y));
                    float  span     = 2.0 * halfSpan;
                    float  Rc       = max(_CurlRadius, 1e-3);
                    float  crease   = -halfSpan + _Peel * (span + STICKER_PI * Rc);
                    float  sfrag    = dot(IN.uv - 0.5, dirp);        // 頂点カールと同じ射影
                    float  aa       = fwidth(sfrag) * 1.5 + 1e-4;
                    float  adh      = smoothstep(crease - aa, crease + aa, sfrag); // 1=接着済み
                    alpha *= adh;

                    float front = saturate(1.0 - abs(sfrag - crease) / (aa + 0.05));
                    col   = lerp(col, hot, front * 0.5 * adh);
                    alpha = max(alpha, front * 0.5h * _EdgeColor.a * adh);
                }

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
