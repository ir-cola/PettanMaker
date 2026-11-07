Shader "Skybox/SunsetGradient"
{
    // コンセプトアートの夕焼け空: 地平線(コーラル)→中(ホットピンク)→天頂(マゼンタ)のグラデ。
    Properties
    {
        _TopColor     ("Top (zenith)", Color)  = (0.80, 0.22, 0.48, 1)
        _MidColor     ("Mid", Color)           = (0.94, 0.36, 0.52, 1)
        _HorizonColor ("Horizon", Color)       = (0.98, 0.60, 0.42, 1)
        _MidPoint     ("Mid Point", Range(0,1)) = 0.35
        _HorizonSharp ("Horizon Sharpness", Range(0.2,4)) = 1.2
        _Intensity    ("Intensity", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex : POSITION; float3 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float3 dir : TEXCOORD0; };
            half4 _TopColor, _MidColor, _HorizonColor; float _MidPoint, _HorizonSharp, _Intensity;

            v2f vert(appdata_t v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }

            half4 frag(v2f i) : SV_Target
            {
                float y = normalize(i.dir).y;       // -1..1
                float t = saturate(y);              // 0 地平線 -> 1 天頂
                float th = pow(t, _HorizonSharp);
                half3 col;
                if (th < _MidPoint) col = lerp(_HorizonColor.rgb, _MidColor.rgb, th / max(_MidPoint, 1e-4));
                else                col = lerp(_MidColor.rgb, _TopColor.rgb, (th - _MidPoint) / max(1.0 - _MidPoint, 1e-4));
                col = lerp(_HorizonColor.rgb, col, step(0.0, y)); // 地平線より下
                return half4(col * _Intensity, 1);
            }
            ENDCG
        }
    }
}
