Shader "UI/LogoShine"
{
    // ロゴのスプライトを描きつつ、一定間隔で斜めの光の筋がサッと走り、
    // 通過時に一瞬きらめく "キラン" 演出を加える LOGO 専用マテリアル。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ShineColor     ("Shine Color", Color) = (1, 1, 1, 1)
        _ShineIntensity ("Shine Intensity", Range(0,3)) = 0.75
        _ShinePeriod    ("Shine Period (sec)", Float) = 3.2
        _ShineActive    ("Sweep Active (frac)", Range(0.05,1)) = 0.26
        _ShineWidth     ("Shine Width", Range(0.01,0.4)) = 0.06
        _ShineAngle     ("Shine Angle (deg)", Float) = 22
        _FlashIntensity ("Flash Intensity", Range(0,2)) = 0.20
        _AlphaGate      ("Alpha Gate", Range(0,1)) = 0.45

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; float4 worldPosition:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; fixed4 _TextureSampleAdd; float4 _ClipRect;

            fixed4 _ShineColor; float _ShineIntensity; float _ShinePeriod; float _ShineActive; float _ShineWidth; float _ShineAngle; float _FlashIntensity; float _AlphaGate;

            #define TAU 6.28318530718

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                half4 c = (tex2D(_MainTex, uv) + _TextureSampleAdd) * IN.color;

                // 斜め方向の正規化座標 [0,1]
                float a = _ShineAngle * (TAU / 360.0);
                float ca = cos(a), sa = sin(a);
                float p = (uv.x * ca + uv.y * sa) / max(ca + sa, 1e-3);

                // 周期の前半 _ShineActive の間だけ筋がサッと横切る("キラン")
                float cyc = frac(_Time.y / max(_ShinePeriod, 0.01));
                float on  = step(cyc, _ShineActive);
                float sweepT = saturate(cyc / max(_ShineActive, 1e-3));
                float center = sweepT * (1.0 + 2.0 * _ShineWidth) - _ShineWidth;
                float band = smoothstep(_ShineWidth, 0.0, abs(p - center)) * on;
                float streak = pow(band, 2.0) * _ShineIntensity;   // 鋭い細い筋

                // 通過の中央付近で一瞬きらめく
                float flash = saturate(1.0 - abs(sweepT - 0.5) * 2.0);
                flash = pow(flash, 4.0) * on * _FlashIntensity;

                // ロゴの不透明部分だけに限定(柔らかい影や縁のにじみには乗せない)
                float gate = smoothstep(_AlphaGate, _AlphaGate + 0.25, c.a);
                c.rgb += _ShineColor.rgb * (streak + flash) * gate;
                c.rgb = saturate(c.rgb);

                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
