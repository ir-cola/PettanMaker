Shader "UI/BookSpine"
{
    // 本の背表紙。円柱状の陰影・上下の金帯・中央を挟む金の枠線・布の縦目。
    // 縦書きタイトルを上に重ねる前提(中央は素地のまま)。背色は Image の色で個別指定。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _BandColor ("Band/Frame Color (gold)", Color) = (0.85, 0.70, 0.38, 1)
        _BandH     ("Band Height", Range(0,0.2)) = 0.07
        _FrameX    ("Frame Half Width", Range(0,0.5)) = 0.30
        _Round     ("Spine Roundness", Range(0,1)) = 0.55
        _EdgeHi    ("Edge Highlight", Range(0,1)) = 0.35

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

            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float4 _ClipRect;
            fixed4 _BandColor; float _BandH; float _FrameX; float _Round; float _EdgeHi;

            float hash21(float2 p){ p = frac(p*float2(123.34,345.45)); p += dot(p,p+34.345); return frac(p.x*p.y); }

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

                // 円柱状の陰影
                float cyl = 1.0 - pow(abs(uv.x - 0.5) * 2.0, 2.0) * (0.2 + 0.6 * _Round);
                half3 col = IN.color.rgb * cyl;

                // 布の細い縦目(控えめ)
                col += (hash21(floor(float2(uv.x*16.0, 0.0))) - 0.5) * 0.05
                     + (hash21(floor(uv*float2(20.0,60.0))) - 0.5) * 0.012;

                // 上下の金帯(細い境界線付き)
                float topB = smoothstep(_BandH + 0.012, _BandH, uv.y);
                float botB = smoothstep(1.0 - _BandH - 0.012, 1.0 - _BandH, uv.y);
                col = lerp(col, _BandColor.rgb * cyl, saturate(topB + botB));

                // 中央タイトルを挟む金の縦枠線(2本)
                float fx = abs(uv.x - 0.5);
                float frame = (smoothstep(_FrameX + 0.012, _FrameX + 0.004, fx) - smoothstep(_FrameX + 0.004, _FrameX - 0.004, fx));
                // 枠線は帯の間(縦方向)だけ
                float withinV = smoothstep(_BandH + 0.02, _BandH + 0.05, uv.y) * smoothstep(1.0 - _BandH - 0.02, 1.0 - _BandH - 0.05, uv.y);
                col = lerp(col, _BandColor.rgb, saturate(frame) * withinV);

                // 左端ハイライト・右端の影
                col += smoothstep(0.10, 0.0, uv.x) * _EdgeHi;
                col *= lerp(0.82, 1.0, smoothstep(1.0, 0.85, uv.x));

                half4 outc = half4(saturate(col), IN.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                outc.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(outc.a - 0.001);
                #endif
                return outc;
            }
            ENDCG
        }
    }
}
