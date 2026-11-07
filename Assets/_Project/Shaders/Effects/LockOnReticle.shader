Shader "Effects/LockOnReticle"
{
    // ロックオンレティクル (ビルボード四角ポリに貼る)。
    // 回転する4分割リング + 十字ティック + 中心ドットを SDF で描く。
    // ZTest Always で敵メッシュに隠れず常に見える (UI 的な読みやすさ優先)。
    // ※ ランタイム専用シェーダーは Shader.Find だけだとビルドで削除されるため、
    //   Resources/M_LockOnReticle.mat 経由で参照すること (M_DashAfterimage と同じ対策)。
    Properties
    {
        _Color ("Color", Color) = (1, 0.3, 0.45, 1)
        _SpinSpeed ("Spin Speed (rad/s)", Float) = 1.6
        _PulseSpeed ("Pulse Speed", Float) = 6.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+80" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _SpinSpeed;
            float _PulseSpeed;

            struct appdata_t { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.uv = v.uv;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 中心原点 (-1..1) + 回転
                float2 p = IN.uv * 2.0 - 1.0;
                float ang = _Time.y * _SpinSpeed;
                float ca = cos(ang), sa = sin(ang);
                float2 q = float2(p.x * ca - p.y * sa, p.x * sa + p.y * ca);

                float r = length(q);
                float theta = atan2(q.y, q.x);

                // 4分割リング: sin(2θ) が 0 になる位置 (十字方向) に切れ目
                float ring = 1.0 - smoothstep(0.025, 0.06, abs(r - 0.78));
                float arcs = smoothstep(0.22, 0.5, abs(sin(theta * 2.0)));
                float ringA = ring * arcs;

                // 十字ティック: 軸方向の内向き短線 (回転に追従)
                float axisDist = min(abs(q.x), abs(q.y));
                float ticks = (1.0 - smoothstep(0.035, 0.06, axisDist))
                            * smoothstep(0.5, 0.55, r) * (1.0 - smoothstep(0.66, 0.7, r));

                // 中心ドット
                float dotC = 1.0 - smoothstep(0.045, 0.09, r);

                float a = saturate(ringA + ticks + dotC);

                // パルス (明滅)
                float pulse = 1.0 + 0.25 * sin(_Time.y * _PulseSpeed);

                fixed4 col;
                col.rgb = _Color.rgb * pulse;
                col.a = a * _Color.a;
                return col;
            }
            ENDCG
        }
    }
}
