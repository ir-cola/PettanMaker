Shader "UI/HoloFoil"
{
    // 画面全体に重ねる、シール箔のようなホログラム(虹色光沢)オーバーレイ。
    // 加算合成(Blend One One)なので、虹色の光沢バンドが流れるように光る。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _HoloIntensity ("Holo Intensity", Range(0,1)) = 0.26
        _Saturation    ("Saturation", Range(0,1)) = 0.85
        _Brightness    ("Base Brightness", Range(0,1)) = 0.10
        _HueScale      ("Hue Scale", Float) = 1.4
        _HueSpeed      ("Hue Speed", Float) = 0.07
        _BandScale     ("Sheen Band Scale", Float) = 1.3
        _BandSharp     ("Sheen Sharpness", Range(1,8)) = 3.0
        _BandSpeed     ("Sheen Speed", Float) = 0.16
        _Angle         ("Sheen Angle (deg)", Float) = 35
        _Grain         ("Foil Glitter", Range(0,1)) = 0.30

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
        Blend One One                 // 加算(光沢を足す)
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

            float  _HoloIntensity;
            float  _Saturation;
            float  _Brightness;
            float  _HueScale;
            float  _HueSpeed;
            float  _BandScale;
            float  _BandSharp;
            float  _BandSpeed;
            float  _Angle;
            float  _Grain;

            #define TAU 6.28318530718

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            // HSV -> RGB (iq)
            float3 hsv2rgb(float3 c)
            {
                float3 rgb = clamp(abs(fmod(c.x * 6.0 + float3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
                return c.z * lerp(float3(1.0, 1.0, 1.0), rgb, c.y);
            }

            half4 ProceduralHolo(float2 uv)
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 p = float2(uv.x * aspect, uv.y);
                float t = _Time.y;

                // 斜め方向に沿った座標(光沢バンドと虹の進行方向)
                float a  = _Angle * (TAU / 360.0);
                float ca = cos(a), sa = sin(a);
                float dir  = p.x * ca + p.y * sa;
                float dir2 = p.x * cos(a + 1.25) + p.y * sin(a + 1.25);   // 交差する2本目

                // 流れる虹(方向と時間で色相シフト)
                float hue = frac(dir * _HueScale + t * _HueSpeed);
                float3 rainbow = hsv2rgb(float3(hue, _Saturation, 1.0));

                // 流れる光沢バンド(2本を交差させて箔っぽく)
                float b1 = sin((dir  * _BandScale        - t * _BandSpeed) * TAU) * 0.5 + 0.5;
                float b2 = sin((dir2 * _BandScale * 0.75  + t * _BandSpeed * 0.6) * TAU) * 0.5 + 0.5;
                float sheen = saturate(pow(b1, _BandSharp) * 0.8 + pow(b2, _BandSharp) * 0.6);

                // 箔の微細グリッター(細かくキラッと光る点)
                float2 gid = floor(float2(uv.x * aspect, uv.y) * 230.0);
                float gh = hash21(gid);
                float gtw = pow(sin(t * 3.0 + gh * TAU) * 0.5 + 0.5, 8.0);
                float glitter = step(0.62, gh) * gtw;

                // 合成:薄いベース虹 + 流れる光沢 + グリッター(すべて加算)
                float amount = _Brightness + sheen;
                float3 col = rainbow * amount * _HoloIntensity;
                col += hsv2rgb(float3(frac(hue + 0.35), _Saturation, 1.0)) * glitter * _Grain;

                col = saturate(col);
                float lum = max(col.r, max(col.g, col.b));
                return half4(col, lum);
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
                half4 color = ProceduralHolo(IN.texcoord);
                color.rgb *= IN.color.rgb;
                color.a   *= IN.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                half cl = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                color.rgb *= cl;   // 加算なので rgb 側にクリップを適用
                color.a   *= cl;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
