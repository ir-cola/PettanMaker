Shader "Player/DashAfterimage"
{
    // ダッシュ残像用: 単色シルエット + スクリーンスペースの斜線ストライプ。
    // 色とフェードは SkinnedMeshTrailRenderer が MaterialPropertyBlock の
    // _Color / _BaseColor へ毎フレーム書き込む (このシェーダーは _BaseColor を使う)。
    Properties
    {
        _BaseColor ("Color", Color) = (0.7, 0.4, 1, 0.85)
        _StripeSize ("Stripe Size (px)", Float) = 22
        _StripeMin ("Stripe Min Alpha", Range(0, 1)) = 0.3
        _StripeSharp ("Stripe Sharpness", Range(1, 16)) = 4
        _ScrollSpeed ("Stripe Scroll Speed", Float) = 1.5
        _OutlineColor ("Outline Color (濃い紫)", Color) = (0.4, 0.16, 0.7, 1)
        _OutlineWidth ("Outline Width (m)", Range(0, 0.05)) = 0.014
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        // ZWrite On: 結合1枚メッシュの自己重なり (体の裏側や服の下の素体) を
        // 深度で隠し、半透明の濃度ムラを消してきれいなシルエットにする
        Cull Back Lighting Off ZWrite On ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        // 輪郭線: 法線方向に膨らませた裏面ハルを濃い紫で描く。
        // URP はタグ無しマルチパスを1パスしか描かないため、本体=SRPDefaultUnlit /
        // 輪郭=UniversalForward と別タグにして両方描画させる (URP 2パスの定石)。
        // タグ順で本体が先に描かれ深度を書くので、ハルは内側が ZTest で落ち
        // 「縁だけ」が残る = 本体の薄紫を濃い裏地で潰さない。
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            fixed4 _OutlineColor;
            float _OutlineWidth;

            struct appdata_t { float4 vertex:POSITION; float3 normal:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                // ワールド法線方向へ一定距離 (m) 押し出してから射影
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wnrm = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                wpos += wnrm * _OutlineWidth;
                OUT.vertex = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 残像本体のフェード (_BaseColor.a) に合わせて輪郭も消えていく
                return fixed4(_OutlineColor.rgb, _OutlineColor.a * _BaseColor.a);
            }
            ENDCG
        }

        Pass
        {
            Name "Default"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            float _StripeSize; float _StripeMin; float _StripeSharp; float _ScrollSpeed;

            struct appdata_t { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 spos   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.spos   = ComputeScreenPos(OUT.vertex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // スクリーンスペースの斜め45°ストライプ (ゆっくり流れる)
                float2 px = (IN.spos.xy / IN.spos.w) * _ScreenParams.xy;
                float band = (px.x + px.y) / max(_StripeSize, 1.0) - _Time.y * _ScrollSpeed;
                float stripe = pow(abs(sin(band * 3.14159265)), _StripeSharp);

                fixed4 col;
                col.rgb = _BaseColor.rgb * lerp(0.9, 1.15, stripe);   // 山をわずかに明るく (ブルーム映え)
                col.a   = _BaseColor.a * lerp(_StripeMin, 1.0, stripe);
                return col;
            }
            ENDCG
        }
    }
}
