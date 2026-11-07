Shader "UI/SpotlightMask"
{
    // 画面全体を暗転させつつ、指定した最大8個の角丸矩形「穴」だけを明るく抜く。
    // 穴の位置/大きさは SpotlightOverlay が毎フレーム画面UVで更新する（移動対象に追従）。
    // 演出中に特定UI（吹き出し・ボタン・ハンガー・プレビュー枠）だけを見せるのに使う汎用マスク。
    Properties
    {
        [PerRendererData] _MainTex ("Tex", 2D) = "white" {}
        _Color ("Dim Color", Color) = (0,0,0,1)
        _DimAlpha ("Dim Alpha", Range(0,1)) = 0.72
        _HoleCount ("Hole Count", Float) = 0
        _Aspect ("Screen Aspect", Float) = 1.777
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex;
            fixed4 _Color; float _DimAlpha; float _HoleCount; float _Aspect;
            float4 _Holes[8];       // xy=中心UV, zw=半サイズUV
            float4 _HoleParams[8];  // x=角丸半径(UV), y=ソフトネス(UV)

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex   = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color    = v.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;                 // 0..1 全画面
                float hole = 0.0;
                for (int i = 0; i < 8; i++)
                {
                    if ((float)i >= _HoleCount) break;
                    float2 c = _Holes[i].xy;
                    float2 h = _Holes[i].zw;
                    float  r = _HoleParams[i].x;
                    float  soft = max(_HoleParams[i].y, 1e-4);
                    // 角丸矩形 SDF (X をアスペクト補正して円い角に)
                    float2 P = float2(uv.x * _Aspect, uv.y);
                    float2 C = float2(c.x  * _Aspect, c.y);
                    float2 H = float2(h.x  * _Aspect, h.y);
                    float2 d = abs(P - C) - (H - r);
                    float sd = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
                    float inside = 1.0 - smoothstep(-soft, soft, sd);   // 1=穴の中
                    hole = max(hole, inside);
                }
                float a = _DimAlpha * (1.0 - hole);
                return fixed4(_Color.rgb, a) * IN.color;
            }
            ENDCG
        }
    }
}
