Shader "UI/PlayerSparkle"
{
    // スプライト(キャラ)を描きつつ、シルエット上に星形のキラキラと
    // やわらかいシャイン・スイープを乗せる Player 専用マテリアル。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SparkleColor     ("Sparkle Color", Color) = (1, 1, 0.96, 1)
        _SparkleIntensity ("Sparkle Intensity", Range(0,2)) = 0.85
        _SparkleDensity   ("Sparkle Density", Float) = 9
        _SparkleSpeed     ("Sparkle Speed", Float) = 2.0
        _SparkleSparsity  ("Sparkle Sparsity", Range(0,1)) = 0.72

        _ShineColor     ("Shine Color", Color) = (1, 1, 1, 1)
        _ShineIntensity ("Shine Intensity", Range(0,2)) = 0.35
        _ShineSpeed     ("Shine Speed", Float) = 0.45
        _ShineWidth     ("Shine Width", Range(0.02,0.5)) = 0.16
        _ShineAngle     ("Shine Angle (deg)", Float) = 60

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

            fixed4 _SparkleColor; float _SparkleIntensity; float _SparkleDensity; float _SparkleSpeed; float _SparkleSparsity;
            fixed4 _ShineColor;   float _ShineIntensity;   float _ShineSpeed;     float _ShineWidth;    float _ShineAngle;

            #define TAU 6.28318530718

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

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

                // キャラのシルエット内だけに乗せるマスク
                float mask = smoothstep(0.15, 0.55, c.a);

                // --- 星形キラキラ(スプライトUV上、疎にして大きめ) ---
                float2 suv = uv * max(_SparkleDensity, 1.0);
                float2 sid = floor(suv);
                float2 sf  = frac(suv) - 0.5;
                float h  = hash21(sid + float2(3.7, 1.9));
                float on = step(_SparkleSparsity, hash21(sid + float2(8.1, 5.3)));
                float2 jit = (float2(hash21(sid + float2(1.3, 7.7)), hash21(sid + float2(6.7, 2.1))) - 0.5) * 0.6;
                float2 q = sf - jit;
                float tw = pow(sin(_Time.y * _SparkleSpeed + h * TAU) * 0.5 + 0.5, 3.0);
                float core = smoothstep(0.22, 0.0, length(q));
                float cH = smoothstep(0.03, 0.0, abs(q.y)) * smoothstep(0.42, 0.0, abs(q.x));
                float cV = smoothstep(0.03, 0.0, abs(q.x)) * smoothstep(0.42, 0.0, abs(q.y));
                float star = saturate(core * 0.7 + cH + cV);
                float sparkle = on * tw * star * _SparkleIntensity;

                // --- ゆっくり流れるシャイン・スイープ ---
                float a = _ShineAngle * (TAU / 360.0);
                float proj = uv.x * cos(a) + uv.y * sin(a);
                float sw = frac(_Time.y * _ShineSpeed);
                float center = sw * 1.4 - 0.2;
                float shine = smoothstep(_ShineWidth, 0.0, abs(proj - center)) * _ShineIntensity;

                c.rgb += (_SparkleColor.rgb * sparkle + _ShineColor.rgb * shine) * mask;
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
