Shader "Effects/AttackLandingCircle"
{
    // 攻撃の落下予想位置サークル (地面に寝かせた四角ポリに貼る)。
    //  ・縁: 角度依存の sin 揺らぎでゆらゆら動く輪郭線
    //  ・中: チェック柄。2トーンが位相差で明滅する濃淡アニメ
    //  ・出現/消滅: バリューノイズしきい値のディゾルブ (_Dissolve 0=表示 1=消滅)
    // ※ ランタイム専用シェーダーは Shader.Find だけだとビルドで削除されるため、
    //   Resources/M_AttackLandingCircle.mat 経由で参照すること。
    Properties
    {
        _FillColor ("Fill 薄 (紫)", Color) = (0.58, 0.32, 0.95, 0.30)
        _FillColor2 ("Fill 濃 (チェック片側)", Color) = (0.36, 0.15, 0.72, 0.42)
        _EdgeColor ("Edge (輪郭線)", Color) = (0.82, 0.52, 1.0, 0.95)
        _CheckerTiles ("Checker Tiles", Float) = 7
        _WobbleAmp ("Edge Wobble Amp", Range(0, 0.15)) = 0.045
        _WobbleFreq ("Edge Wobble Freq", Float) = 9
        _WobbleSpeed ("Edge Wobble Speed", Float) = 2.6
        _ShadeSpeed ("Checker Shade Anim Speed", Float) = 2.2
        _Dissolve ("Dissolve (0=表示, 1=消滅)", Range(0, 1)) = 0
        _DissolveEdgeColor ("Dissolve Edge", Color) = (1.0, 0.75, 1.0, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off Lighting Off ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _FillColor;
            fixed4 _FillColor2;
            fixed4 _EdgeColor;
            float _CheckerTiles;
            float _WobbleAmp;
            float _WobbleFreq;
            float _WobbleSpeed;
            float _ShadeSpeed;
            float _Dissolve;
            fixed4 _DissolveEdgeColor;

            struct appdata_t { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.uv = v.uv;
                return OUT;
            }

            // ハッシュ + バリューノイズ (ディゾルブのまだら用)
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                float r = length(p);
                float theta = atan2(p.y, p.x);

                // ===== 縁のゆらゆら: 半径を角度+時間で揺らす (2波を重ねて有機的に) =====
                float edgeR = 0.90
                    + _WobbleAmp * sin(theta * _WobbleFreq + _Time.y * _WobbleSpeed)
                    + _WobbleAmp * 0.6 * sin(theta * (_WobbleFreq * 0.53) - _Time.y * (_WobbleSpeed * 1.7) + 1.7);

                float inside = 1.0 - smoothstep(edgeR - 0.02, edgeR + 0.015, r);
                float ringBand = 1.0 - smoothstep(0.012, 0.05, abs(r - edgeR));

                // ===== チェック柄 + 濃淡アニメ (2トーンが位相差で明滅) =====
                float2 cuv = IN.uv * _CheckerTiles;
                float ch = fmod(floor(cuv.x) + floor(cuv.y), 2.0);
                float s = 0.5 + 0.5 * sin(_Time.y * _ShadeSpeed + ch * 3.14159);
                fixed4 fill = lerp(_FillColor, _FillColor2, ch);
                fill.rgb *= lerp(0.8, 1.3, s);
                fill.a *= lerp(0.75, 1.15, s);

                fixed4 col;
                col.rgb = lerp(fill.rgb, _EdgeColor.rgb, ringBand);
                col.a = max(fill.a * inside, _EdgeColor.a * ringBand);

                // ===== ディゾルブ: ノイズしきい値。境界を明るい縁色で光らせる =====
                float n = valueNoise(IN.uv * 14.0);
                float th = _Dissolve * 1.25 - 0.1;
                float vis = smoothstep(th, th + 0.08, n);
                col.rgb = lerp(_DissolveEdgeColor.rgb, col.rgb, smoothstep(th + 0.02, th + 0.18, n));
                col.a *= vis;

                return col;
            }
            ENDCG
        }
    }
}
