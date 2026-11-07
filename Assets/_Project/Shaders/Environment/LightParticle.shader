Shader "Environment/LightParticle"
{
    // シーンに漂う光の粒子用。柔らかい丸い光点を「加算合成」で発光させる。
    // テクスチャ不要（UV中心からの距離で放射状フォールオフを手続き生成）。
    // 色はパーティクルの頂点カラー × Tint。HDR色＋ブルームでふんわり光る。
    Properties
    {
        _TintColor ("Tint (HDR)", Color) = (1,1,1,1)
        _Core   ("Core Size", Range(0,1)) = 0.0
        _Falloff("Edge Falloff", Range(0.5,6)) = 2.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Blend SrcAlpha One            // 加算（アルファで強度を制御）
        Cull Off  Lighting Off  ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TintColor;
            float _Core, _Falloff;

            struct appdata_t { float4 vertex:POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; };
            struct v2f       { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; };

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
                float d    = length(i.texcoord - 0.5) * 2.0;          // 0=中心, 1=端
                float glow = saturate(1.0 - smoothstep(_Core, 1.0, d));
                glow = pow(glow, _Falloff);                            // 中心を明るく、外を柔らかく
                fixed4 c = i.color * _TintColor;
                c.a *= glow;
                return c;
            }
            ENDCG
        }
    }
}
