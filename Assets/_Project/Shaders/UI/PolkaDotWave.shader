Shader "UI/PolkaDotWave"
{
    // ロード画面の黄色部分に重ねる水玉模様（UI用・背景は透明、ドットのみ描画）。
    // ドット全体を _ScrollXY でゆっくり流しつつ、色・大きさ・透明度を波打たせる。
    // 時間は _ManualTime (スクリプトが Time.unscaledTime を毎フレーム流し込む) を優先。
    // _Time.y はロード中のフレーム落ちで maximumDeltaTime にクランプされてカクつくため。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _DotColor  ("Dot Color A", Color) = (0.49, 0.64, 0.83, 0.45)
        _DotColorB ("Dot Color B", Color) = (0.90, 0.62, 0.74, 0.45)
        _Density   ("Density", Float) = 9
        _DotRadius ("Dot Radius (base)", Range(0,0.5)) = 0.30
        _SizeAmp   ("Size Pulse", Range(0,0.5)) = 0.10
        _AlphaMin  ("Min Alpha Mul", Range(0,1)) = 0.35
        _WaveFreq  ("Wave Frequency", Float) = 4.0
        _WaveSpeed ("Wave Speed", Float) = 1.6
        _Aspect    ("Aspect (w/h)", Float) = 1.777
        _ScrollXY   ("Scroll Speed (UV/s)", Vector) = (0.025, -0.018, 0, 0)
        _ManualTime ("Manual Time (<0 = use _Time)", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off  Lighting Off  ZWrite Off  ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; };
            struct v2f       { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; };

            fixed4 _DotColor, _DotColorB;
            float _Density, _DotRadius, _SizeAmp, _AlphaMin, _WaveFreq, _WaveSpeed, _Aspect, _ManualTime;
            float4 _ScrollXY;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.color    = v.color;
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float tt = (_ManualTime >= 0.0) ? _ManualTime : _Time.y;

                float2 uv = i.texcoord;
                uv.x *= _Aspect;                 // ドットを丸く保つ
                uv += _ScrollXY.xy * tt;         // 水玉全体をゆっくり流す

                // グリッド上の各ドット
                float2 g    = uv * _Density;
                float2 id   = floor(g);          // ドットの格子座標
                float2 cell = frac(g) - 0.5;     // セル中心からのオフセット
                float  d    = length(cell);

                // ドットの格子位置から位相を作る → 波が斜めに伝播する
                float phase = (id.x + id.y) * _WaveFreq * 0.3 + tt * _WaveSpeed;
                float w     = 0.5 + 0.5 * sin(phase);   // 0..1

                // 大きさを波打たせる（半径をドットごとに増減）
                float radius = max(0.02, _DotRadius + _SizeAmp * sin(phase));
                float aa     = fwidth(d);
                float dotM   = 1.0 - smoothstep(radius - aa, radius + aa, d);

                // 色を A↔B で、透明度も波打たせる
                fixed4 col     = lerp(_DotColor, _DotColorB, w);
                float  alphaMul = lerp(_AlphaMin, 1.0, w);
                col.a *= dotM * alphaMul * i.color.a;
                return col;
            }
            ENDCG
        }
    }
}
