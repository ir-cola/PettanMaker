Shader "UI/SpriteGlowOutline"
{
    // チュートリアルビルボードのキーアイコン用:
    // スプライトのアルファを8方向に膨張サンプリングし、形に沿った光るアウトラインを付ける。
    // ゆっくり明滅 (パルス) して「光っている」感を出す。ブルームで発光が乗る前提で少し明るめ。
    //
    // ※ アトラス内の隣スプライトを拾わないよう、サンプル UV を _UVRect 内へクランプする。
    //    _UVRect は SpriteGlowOutline.cs が MaterialPropertyBlock で書き込む。
    // ※ スプライトの MeshType は FullRect にすること (Tight だと外側の枠が描画されない)。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (0.72, 0.4, 1, 1)
        _OutlineWidthPx ("Outline Width (px)", Range(0, 16)) = 6
        _GlowStrength ("Glow Strength", Range(0, 3)) = 1.5
        _PulseSpeed ("Pulse Speed", Float) = 2.4
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.3
        _UVRect ("UV Rect (script)", Vector) = (0, 0, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest LEqual
        Blend One OneMinusSrcAlpha      // 前乗算 (本体とグローをきれいに合成)
        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_TexelSize; fixed4 _Color;
            fixed4 _OutlineColor; float _OutlineWidthPx; float _GlowStrength;
            float _PulseSpeed; float _PulseAmount; float4 _UVRect;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.uv     = v.uv;
                OUT.color  = v.color * _Color;
                return OUT;
            }

            float SampleA(float2 uv, float2 offs)
            {
                float2 p = clamp(uv + offs, _UVRect.xy, _UVRect.zw);   // 隣スプライトへ漏れない
                return tex2D(_MainTex, p).a;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex  = tex2D(_MainTex, IN.uv);
                fixed4 base = tex * IN.color;

                // 8方向のアルファ膨張 → 自分のアルファとの差 = 形のふち
                float2 t = _MainTex_TexelSize.xy * _OutlineWidthPx;
                float maxA = 0.0;
                maxA = max(maxA, SampleA(IN.uv, float2( t.x, 0)));
                maxA = max(maxA, SampleA(IN.uv, float2(-t.x, 0)));
                maxA = max(maxA, SampleA(IN.uv, float2(0,  t.y)));
                maxA = max(maxA, SampleA(IN.uv, float2(0, -t.y)));
                maxA = max(maxA, SampleA(IN.uv, float2( t.x,  t.y) * 0.7071));
                maxA = max(maxA, SampleA(IN.uv, float2(-t.x,  t.y) * 0.7071));
                maxA = max(maxA, SampleA(IN.uv, float2( t.x, -t.y) * 0.7071));
                maxA = max(maxA, SampleA(IN.uv, float2(-t.x, -t.y) * 0.7071));

                float outline = saturate((maxA - tex.a) * 1.6);

                // アイコン内の文字などの「くり抜き穴」は、遠めの8方向すべてが不透明
                // (=閉じた穴) になる。その場合はグローを消し、外周だけを光らせる。
                float2 f = t * 3.0;
                float minFar = 1.0;
                minFar = min(minFar, SampleA(IN.uv, float2( f.x, 0)));
                minFar = min(minFar, SampleA(IN.uv, float2(-f.x, 0)));
                minFar = min(minFar, SampleA(IN.uv, float2(0,  f.y)));
                minFar = min(minFar, SampleA(IN.uv, float2(0, -f.y)));
                minFar = min(minFar, SampleA(IN.uv, float2( f.x,  f.y) * 0.7071));
                minFar = min(minFar, SampleA(IN.uv, float2(-f.x,  f.y) * 0.7071));
                minFar = min(minFar, SampleA(IN.uv, float2( f.x, -f.y) * 0.7071));
                minFar = min(minFar, SampleA(IN.uv, float2(-f.x, -f.y) * 0.7071));
                outline *= 1.0 - minFar;

                float pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed);
                fixed3 glowRGB = _OutlineColor.rgb * (_GlowStrength * pulse);
                float glowA = outline * _OutlineColor.a * IN.color.a * (1.0 - tex.a);

                fixed4 col;
                col.rgb = base.rgb * base.a + glowRGB * glowA;
                col.a   = base.a + glowA;
                return col;
            }
            ENDCG
        }
    }
}
