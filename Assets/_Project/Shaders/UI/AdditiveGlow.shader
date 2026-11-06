/**===========================================================================

 File   : 加算グロー UIシェーダー [AdditiveGlow.shader]

 Author : Hayato Otsuki
 Date   : 2026/7/17
 -----------------------------------------------------------------------------

 UGUI Image 用の単純な加算(プリマルチプライ)グロー。スキル発動可能時の
 Q アイコンの後ろに置く光のハロー(Q_Glow)に使う。SkillReadyIndicator が
 Image.color の alpha を明滅させて発光量を制御する。
   ・Blend One One + rgb を alpha で重み付け = きれいな加算合成
   ・_MainTex は [PerRendererData] で sprite テクスチャが流し込まれる

===========================================================================**/
Shader "UI/AdditiveGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,4)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float  _Intensity;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                // プリマルチプライ加算: 明るさ = rgb * (tex.a * color.a * intensity)
                float a = tex.a * i.color.a * _Intensity;
                fixed4 col;
                col.rgb = tex.rgb * i.color.rgb * a;
                col.a = a;
                return col;
            }
            ENDCG
        }
    }
}
