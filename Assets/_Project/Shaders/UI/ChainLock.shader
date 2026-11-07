Shader "UI/ChainLock"
{
    // ロック演出。縦長の本の上にチェーンを×(クロス)に渡し、中央に南京錠を描く。
    // 金属グレーで、未開放の本に重ねる。背景は透明。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ChainColor ("Chain Color", Color) = (0.80, 0.82, 0.86, 1)
        _ChainDark  ("Chain Shadow", Color) = (0.28, 0.29, 0.33, 1)
        _ChainWidth ("Chain Width", Range(0.02,0.2)) = 0.085
        _LinkCount  ("Chain Links", Float) = 16
        _LockColor  ("Lock Color", Color) = (0.80, 0.68, 0.32, 1)
        _LockSize   ("Lock Size", Range(0.1,0.7)) = 0.42
        _AspectWH   ("Element Aspect (w/h)", Float) = 0.28

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
            fixed4 _ChainColor; fixed4 _ChainDark; float _ChainWidth; float _LinkCount;
            fixed4 _LockColor; float _LockSize; float _AspectWH;

            #define TAU 6.28318530718

            // 1本のチェーン。p は幅=1単位の等方座標。戻り: x=被覆, y=金属シェード(0..1)
            float2 chain(float2 p, float2 dir, float halfLen)
            {
                float2 n = float2(-dir.y, dir.x);
                float along = dot(p, dir);
                float perp  = dot(p, n);
                float ends  = step(abs(along), halfLen);
                float prof  = 1.0 - smoothstep(_ChainWidth * 0.55, _ChainWidth, abs(perp));
                float seg   = along * _LinkCount / max(halfLen * 2.0, 1e-3);
                float link  = sin(seg * TAU) * 0.5 + 0.5;            // リンクの明暗
                float cross = 1.0 - saturate(abs(perp) / _ChainWidth); // 丸い断面
                float shade = saturate(0.35 + 0.65 * cross) * lerp(0.65, 1.0, link);
                return float2(prof * ends, shade);
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
                // 幅=1単位の等方座標(縦長なので y を 1/aspect 倍に伸ばす)
                float invA = 1.0 / max(_AspectWH, 0.01);
                float hy = 0.5 * invA;                       // 半分の高さ(幅単位)
                float2 p = float2(IN.texcoord.x - 0.5, (IN.texcoord.y - 0.5) * invA);

                // ×字の2本(四隅を結ぶ)
                float2 corner  = float2(0.5, hy);
                float  halfLen = length(corner);
                float2 c1 = chain(p, corner / halfLen, halfLen);
                float2 c2 = chain(p, float2(corner.x, -corner.y) / halfLen, halfLen);
                float cover = max(c1.x, c2.x);
                float shade = (c1.x >= c2.x) ? c1.y : c2.y;
                half3 col = lerp(_ChainDark.rgb, _ChainColor.rgb, shade);
                float a = saturate(cover);

                // 中央の南京錠
                float2 lp = p / max(_LockSize, 0.01);
                float2 bd = abs(lp - float2(0.0, -0.15)) - float2(0.85, 0.65);
                float body = min(max(bd.x, bd.y), 0.0) + length(max(bd, 0.0)) - 0.15;
                float2 sp = lp - float2(0.0, 0.78);
                float shackle = max(abs(length(sp) - 0.62) - 0.13, -sp.y);
                float lockD = min(body, shackle);
                float lockMask = 1.0 - smoothstep(0.0, 0.05, lockD);
                float key = min(length(lp - float2(0.0, -0.05)) - 0.14,
                                max(abs(lp.x) - 0.06, abs(lp.y + 0.40) - 0.28));
                float keyMask = 1.0 - smoothstep(0.0, 0.04, key);
                half3 lockCol = _LockColor.rgb * lerp(0.7, 1.12, smoothstep(0.3, -0.4, lockD));
                lockCol = lerp(lockCol, _ChainDark.rgb * 0.6, keyMask);

                col = lerp(col, lockCol, lockMask);
                a = max(a, lockMask);

                half4 outc = half4(saturate(col), a * IN.color.a);
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
