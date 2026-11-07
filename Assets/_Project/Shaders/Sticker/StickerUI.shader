Shader "Sticker/UI"
{
    // 汎用 UGUI シールシェーダー。1枚の絵(_MainTex)を「シール」に見立て、
    // 指定方向へ角を折り返す剥がし(Peel)／逆再生で貼り付け(Apply)を行う。
    //   ・_Peel 1→0 で貼り付き。_DirX/_DirY で最初に密着する角を決める。
    //   ・Apply 追加要素: 接着シマー(_Shimmer) と シワ(_Wrinkle)。
    //   ・_STICKER_CUTOUT : テクスチャ α を尊重(型抜きシール)。OFF=全面不透明(画面キャプチャ)。
    //   ・_STICKER_PREMUL : プリマルチプライド出力(透明RTへ焼いた UI を貼る時)。
    //   ・Blend は _SrcBlend/_DstBlend で切替(既定=ストレート SrcAlpha,OneMinusSrcAlpha)。
    //   ・_UVRect : 大きな RT の一部だけを札にする時のサブ矩形。
    // 旧 UI/StickerPeel(全面/ストレート) と UI/StickerPasteHUD(型抜き/プリマル) を統合。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite/Screen Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Peel ("Peel (1=浮き, 0=貼り付き)", Range(0,1)) = 0
        _ShadowWidth ("Shadow Width", Range(0.0,0.5)) = 0.16
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.5
        _BackColor ("Sticker Back Color", Color) = (0.95, 0.93, 0.86, 1)
        _BackImageMix ("Back Image Bleed", Range(0,1)) = 0.30
        _BackDark ("Back Darkness", Range(0.3,1)) = 0.72
        _RimColor ("Free Edge Highlight", Color) = (1, 1, 1, 1)
        _DirX ("Direction X", Float) = 1
        _DirY ("Direction Y", Float) = 1
        [Toggle] _FlipY ("Flip Y (capture)", Float) = 0
        _UVRect ("UV Sub Rect (x,y,w,h)", Vector) = (0, 0, 1, 1)

        [Header(Apply)]
        _Shimmer ("Adhesive Shimmer", Range(0,2)) = 0
        _ShimmerColor ("Shimmer Color", Color) = (1,1,1,1)
        _ShimmerWidth ("Shimmer Width", Range(0.02,0.6)) = 0.16
        _ShimmerPhase ("Shimmer Phase (sweep 0..1)", Range(0,1)) = 0
        _Wrinkle ("Wrinkle", Range(0,1)) = 0
        _WrinkleScale ("Wrinkle Scale", Range(2,40)) = 14

        [Header(Mode)]
        [Toggle(_STICKER_CUTOUT)] _Cutout ("Cutout (honor texture alpha)", Float) = 0
        [Toggle(_STICKER_PREMUL)] _Premultiply ("Premultiplied output", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10

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
        Blend [_SrcBlend] [_DstBlend]
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
            #include "StickerCore.hlsl"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ _STICKER_CUTOUT
            #pragma multi_compile_local _ _STICKER_PREMUL

            struct appdata_t { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; float4 worldPosition:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float4 _ClipRect;
            float _Peel; float _ShadowWidth; float _ShadowStrength;
            fixed4 _BackColor; float _BackImageMix; float _BackDark; fixed4 _RimColor;
            float _DirX; float _DirY; float _FlipY; float4 _UVRect;
            float _Shimmer; fixed4 _ShimmerColor; float _ShimmerWidth; float _ShimmerPhase; float _Wrinkle; float _WrinkleScale;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex   = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color    = v.color * _Color;
                return OUT;
            }

            // 札ローカル(0-1) → 実テクスチャ UV (サブ矩形 + FlipY)
            float2 QuadToTex(float2 quadUV)
            {
                float2 t = _UVRect.xy + quadUV * _UVRect.zw;
                t.y = lerp(t.y, 1.0 - t.y, _FlipY);
                return t;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 札ローカル(0-1)。RawImage の uvRect でサブ矩形が来るため正規化する
                float2 uv = (IN.texcoord - _UVRect.xy) / max(_UVRect.zw, float2(1e-4, 1e-4));

                float2 dir = normalize(float2(_DirX, _DirY) + 1e-5);
                StickerFold f = Sticker_ComputeFold(uv, dir, _Peel);

                // 表面 / 裏面サンプル
                half4 flatTex = tex2D(_MainTex, QuadToTex(uv));
                float2 foldUV = Sticker_FoldUV(f, uv);
                half4 backTex = tex2D(_MainTex, QuadToTex(saturate(foldUV)));

                #ifdef _STICKER_CUTOUT
                    float inQuad = step(0.0, foldUV.x) * step(foldUV.x, 1.0)
                                 * step(0.0, foldUV.y) * step(foldUV.y, 1.0);
                    float coverageFlat = flatTex.a;
                    float backA = backTex.a * inQuad;
                    #ifdef _STICKER_PREMUL
                        half3 backImg = backTex.rgb / max(backTex.a, 1e-3);
                    #else
                        half3 backImg = backTex.rgb;
                    #endif
                #else
                    float coverageFlat = 1.0;
                    float backA = 1.0;
                    half3 backImg = backTex.rgb;
                #endif

                half3 flapCol = Sticker_FlapColor(f, backImg, _BackColor.rgb, _BackImageMix, _BackDark, _RimColor.rgb);

                // 表面色 ＋ 接着シマー ＋ シワ (Apply 用。既定値では無影響)
                half3 flatCol = flatTex.rgb;
                float shim = _Shimmer * Sticker_Shimmer(f.sN, _ShimmerPhase, _ShimmerWidth) * f.mFlat;
                flatCol += _ShimmerColor.rgb * shim;
                flatCol *= 1.0 + _Wrinkle * Sticker_Wrinkle(uv, _WrinkleScale) * (1.0 - f.mFlat);

                // 影
                float shadow  = smoothstep(f.c - _ShadowWidth, f.c, f.sN) * f.mRevealed;
                float shadowA = shadow * _ShadowStrength * coverageFlat;

                half4 col;
                #ifdef _STICKER_PREMUL
                    half3 rgb = flatCol * coverageFlat * f.mFlat + flapCol * backA * f.mFlap;
                    half  a   = saturate(coverageFlat * f.mFlat + backA * f.mFlap + shadowA);
                    col = half4(rgb * IN.color.rgb, a) * IN.color.a;
                #else
                    half3 rgb = saturate(flatCol * f.mFlat + flapCol * f.mFlap);
                    half  a   = saturate(coverageFlat * f.mFlat + backA * f.mFlap + shadowA);
                    col = half4(rgb * IN.color.rgb, a * IN.color.a);
                #endif

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
