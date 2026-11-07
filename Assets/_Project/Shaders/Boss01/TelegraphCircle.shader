Shader "PettanMaker/Boss_01/TelegraphCircle"
{
    // Boss01 の攻撃着弾予告サークル。地面に寝かせた四角ポリ（UV 0..1）へ貼る前提。
    // プレイヤー武器の着弾デカール（Effects/AttackLandingCircle）と同系の見た目：
    //   ・中: チェック柄。2トーンが位相差で明滅する濃淡アニメ＋中心回りにゆっくり回転
    //   ・縁: 角度依存の sin 揺らぎでゆらゆら動く輪郭線
    //   ・中心は透明で、外周へ向かって濃くなる放射エンベロープ（着弾予告の危険度勾配）
    // 色は _EdgeColor 1色が基準。チェックの2トーンは明度・不透明度の倍率で作り、
    // 輪郭線は _HotMix ぶん白へ寄せる。config の TelegraphColor だけで全体の色味が決まる。
    // B1_TelegraphCircle がランタイム生成する。_Fade はスクリプトから出現フェードに使う。
    // ※ ランタイム専用のため Shader.Find だけの参照だとビルドで剥がされることがある。
    //   ビルドで確実に出す場合は本シェーダのマテリアルを config の TelegraphMaterial へ割り当てること。
    Properties
    {
        [Header(Colors)]
        _EdgeColor ("基準色（赤）", Color) = (1, 0.12, 0.08, 0.9)
        _HotMix    ("輪郭線の白寄せ (0=基準色のまま)", Range(0, 1)) = 0.6
        _GradientPower ("中心→外縁の濃度勾配 (小=内側から濃い / 大=外縁だけ濃い)", Range(0.25, 6)) = 1.8

        [Header(Checker)]
        _CheckerTiles    ("チェックのタイル数", Float) = 7
        _CheckerRotSpeed ("チェックの回転速度 (周/秒)", Float) = 0.03
        _ShadeSpeed      ("濃淡アニメ速度", Float) = 2.2
        _CheckerToneA    ("トーンA 明度", Range(0, 2)) = 1.0
        _CheckerToneB    ("トーンB 明度", Range(0, 2)) = 0.6
        _CheckerAlphaA   ("トーンA 不透明度", Range(0, 1)) = 0.30
        _CheckerAlphaB   ("トーンB 不透明度", Range(0, 1)) = 0.45

        [Header(Wobble Edge)]
        _EdgeRadius  ("輪郭の基準半径 (0..1)", Range(0.5, 0.98)) = 0.92
        _EdgeWidth   ("輪郭線の太さ", Range(0.01, 0.15)) = 0.05
        _WobbleAmp   ("輪郭の揺らぎ幅", Range(0, 0.15)) = 0.04
        _WobbleFreq  ("輪郭の揺らぎ周波数", Float) = 9
        _WobbleSpeed ("輪郭の揺らぎ速度", Float) = 2.6

        [Header(Animation)]
        _PulseSpeed  ("明滅速度 (Hz)", Float) = 2.2
        _PulseAmount ("明滅の深さ", Range(0, 1)) = 0.2

        [Header(Urgency)]
        _UrgencyMinAlpha   ("接近度0のときの薄さ（不透明度倍率）", Range(0, 1)) = 0.25
        _UrgencyPulseBoost ("接近度1で明滅を何倍速にするか", Range(1, 4)) = 2

        [Header(Fill Countdown)]
        _SweepStrength ("時計ワイプの強さ (0=無効)", Range(0, 1)) = 1
        _SweepDimming  ("未チャージ部分の暗さ (小=はっきり差が出る)", Range(0, 1)) = 0.3
        _SweepLeadGlow ("ワイプ先端の光る帯の太さ", Range(0.01, 0.3)) = 0.06
        _SweepStart    ("ワイプ開始角 (0=12時, 0.25=3時…)", Range(0, 1)) = 0

        [Header(Completion Flash)]
        _FlashColor ("完了フラッシュ色", Color) = (1, 1, 1, 1)

        [Header(Sticker Peel)]
        _Peel ("Peel (0=地面に密着, 1=めくれ上がり。スクリプト制御)", Range(0, 1)) = 0
        _CurlRadius ("Curl Radius (板ローカル)", Range(0.02, 0.35)) = 0.24
        _DirX ("Peel Dir X", Float) = 1
        _DirY ("Peel Dir Y", Float) = 1
        _HideUnpeeled ("Hide Unpeeled (貼り付け中の未接着を隠す。1=有効)", Range(0, 1)) = 0

        [Header(Runtime)]
        _Urgency ("着弾接近度 (0=まだ先, 1=直前。スクリプト制御)", Range(0, 1)) = 1
        _Flash   ("完了フラッシュ量 (スクリプト制御。0=無)", Range(0, 1)) = 0
        _Fade ("Fade (0=非表示, 1=表示。スクリプト制御)", Range(0, 1)) = 1
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
            Name "TelegraphCircle"
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
                float _CheckerRotSpeed;
                float _ShadeSpeed;
                half  _CheckerToneA;
                half  _CheckerToneB;
                half  _CheckerAlphaA;
                half  _CheckerAlphaB;
                float _EdgeRadius;
                float _EdgeWidth;
                float _WobbleAmp;
                float _WobbleFreq;
                float _WobbleSpeed;
                float _PulseSpeed;
                half  _PulseAmount;
                half  _UrgencyMinAlpha;
                half  _UrgencyPulseBoost;
                half  _SweepStrength;
                half  _SweepDimming;
                half  _SweepLeadGlow;
                float _SweepStart;
                half4 _FlashColor;
                float _Peel;
                float _CurlRadius;
                float _DirX;
                float _DirY;
                float _HideUnpeeled;
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
                // 板は XY 平面（±0.5）・表 −Z。Euler(90,0,0) で寝かせて使う。
                float2 dir = normalize(float2(_DirX, _DirY) + 1e-5);
                float2 xyOut; float lift; float curl;
                Sticker_CurlVertex(IN.positionOS.xy, dir, _Peel, _CurlRadius, xyOut, lift, curl);
                float3 posOS = float3(xyOut, -lift); // 表 −Z 側へ持ち上げ

                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;   // 中心 (0,0)、外周 r=1
                float r     = length(p);
                float theta = atan2(p.y, p.x);
                float t     = _Time.y;

                // 輪郭線の色：基準色を白へ寄せて作る（config の1色で全体が決まる）
                half3 hot = lerp(_EdgeColor.rgb, half3(1.0h, 1.0h, 1.0h), _HotMix);

                // --- ゆらぐ輪郭半径（プレイヤーデカールと同じ2重 sin 揺らぎ） ---
                float edgeR = _EdgeRadius
                    + _WobbleAmp * sin(theta * _WobbleFreq + t * _WobbleSpeed)
                    + _WobbleAmp * 0.6 * sin(theta * (_WobbleFreq * 0.53) - t * (_WobbleSpeed * 1.7) + 1.7);

                half inside   = 1.0h - (half)smoothstep(edgeR - 0.02, edgeR + 0.015, r);
                half ringBand = 1.0h - (half)smoothstep(_EdgeWidth * 0.3, _EdgeWidth, abs(r - edgeR));

                // --- チェック柄（中心回りにゆっくり回転） ---
                float ca = t * _CheckerRotSpeed * 6.28318;
                float2 rp;
                rp.x = p.x * cos(ca) - p.y * sin(ca);
                rp.y = p.x * sin(ca) + p.y * cos(ca);
                float2 cuv = (rp * 0.5 + 0.5) * _CheckerTiles;
                half ch = (half)abs(fmod(floor(cuv.x) + floor(cuv.y), 2.0));

                // 2トーンが位相差で明滅する濃淡アニメ（プレイヤーデカール準拠）
                half s = 0.5h + 0.5h * (half)sin(t * _ShadeSpeed + ch * 3.14159);
                half tone  = lerp(_CheckerToneA,  _CheckerToneB,  ch) * lerp(0.8h,  1.3h,  s);
                half fillA = lerp(_CheckerAlphaA, _CheckerAlphaB, ch) * lerp(0.75h, 1.15h, s);

                half3 col = _EdgeColor.rgb * tone;

                // --- 中心は透明 → ゆらぐ輪郭へ向かって濃くなる放射エンベロープ ---
                half g = (half)pow(saturate(r / max(edgeR, 0.001)), _GradientPower);
                half alpha = fillA * g * _EdgeColor.a * inside;

                // --- ゆらぐ輪郭線 ---
                col   = lerp(col, hot, ringBand);
                alpha = max(alpha, ringBand * _EdgeColor.a);

                // --- 放射フィルの時計ワイプ（接近度が進むと 12 時から時計回りにチャージされる） ---
                // a=その画素の時計回り角 0..1（0=開始角）。接近度 _Urgency 未満のセクターが「チャージ済み」。
                float a = frac(atan2(p.x, p.y) / 6.28318 - _SweepStart); // atan2(x,y): 0=上, 時計回りに増加
                half charged = (half)step(a, _Urgency);                   // 1=チャージ済み, 0=未チャージ
                half chargeMul = lerp(_SweepDimming, 1.0h, charged);      // 未チャージ側だけ暗くする
                alpha *= lerp(1.0h, chargeMul, _SweepStrength);           // _SweepStrength=0 でワイプ無効

                // ワイプ先端に光る帯（進行中の切っ先を明るく＝時計の針が見える）
                half lead = (half)(smoothstep(_Urgency - _SweepLeadGlow, _Urgency, a) * step(a, _Urgency));
                lead *= _SweepStrength * inside * (1.0h - _Flash); // 完了フラッシュ中は先端表現を隠す
                lead *= (half)saturate((1.0 - _Urgency) * 8.0);    // 満タン時は先端の帯を消す（12時の残りを防ぐ）
                col   = lerp(col, hot, lead);
                alpha = max(alpha, lead * _EdgeColor.a);

                // --- 着弾接近度：まだ先の予告は薄く、着弾が近づくほど赤が濃く・明滅が速くなる ---
                alpha *= lerp(_UrgencyMinAlpha, 1.0h, _Urgency);
                float pulseHz = _PulseSpeed * lerp(1.0, (float)_UrgencyPulseBoost, (float)_Urgency);

                // --- 明滅パルス × スクリプトフェード ---
                half pulse = lerp(1.0h - _PulseAmount, 1.0h,
                                  0.5h + 0.5h * (half)sin(t * pulseHz * 6.28318));
                alpha *= pulse * _Fade;

                // --- 完了フラッシュ：チャージ満タン＝着弾の瞬間に円全体が白く弾ける ---
                // 円内を _FlashColor で満たし、フェードは無視して確実に見せる（弾けてから消える）。
                col   = lerp(col, _FlashColor.rgb, _Flash);
                alpha = max(alpha, _Flash * _FlashColor.a * inside * _Fade);

                // --- シール貼り付けの進行：未接着（クリースより手前）を隠し、接着済み領域が時間で広がる ---
                //     出現中だけ有効化（_HideUnpeeled=1）。_Peel 1→0 に合わせて貼られていく境界が動く。
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

                    // 接着フロント（貼り付いていく境界）に光る帯
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
