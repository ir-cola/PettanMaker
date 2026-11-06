Shader "PettanMaker/Boss_01/GroundScar"
{
    // マナバーストクロス（Boss01 TailAttack後の十字地面爆発）の爆発跡デカール。
    // 地面に寝かせた四角ポリ（UV 0..1）へ貼る前提。
    //   ・_BaseMap のアルファ＝跡の形。RGB は _BaseColor で焦げ色に落とす
    //   ・_BumpMap（テクスチャから B1_ManaBurstVFXBuilder がプロシージャル生成）で
    //     メインライトに反応する凹凸＝「地面が抉れた」立体感を出す
    //   ・跡の濃い部分が残り火のように紫（魔力）で明滅し、_Cool で冷えて消える
    //   ・_Fade はスクリプトから消滅フェードに使う（B1_GroundScarDecal が制御）
    // ※ ランタイム専用のため Shader.Find だけの参照だとビルドで剥がされることがある。
    //   ビルドで確実に出す場合は本シェーダのマテリアルを EffectSettings の ScarMaterial へ割り当てること。
    Properties
    {
        [Header(Maps)]
        _BaseMap ("跡テクスチャ (A=形)", 2D) = "white" {}
        [Normal] _BumpMap ("ノーマルマップ (Builderが自動生成)", 2D) = "bump" {}
        _NormalStrength ("凹凸の強さ", Range(0, 3)) = 1.2

        [Header(Colors)]
        _BaseColor ("焦げの色 (A=全体の不透明度)", Color) = (0.09, 0.07, 0.13, 0.9)
        [HDR] _EmissionColor ("残り火の色（チェシャ猫の魔力＝紫）", Color) = (0.75, 0.35, 1.6, 1)
        _EmberFlickerSpeed ("残り火の明滅速度", Float) = 6
        _EmberSharpness ("残り火の絞り (大=跡の芯だけ光る)", Range(1, 6)) = 2.5

        [Header(Runtime)]
        _Cool ("冷却度 (0=灼熱, 1=冷え切り。スクリプト制御)", Range(0, 1)) = 0
        _Fade ("Fade (1=表示, 0=消滅。スクリプト制御)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+2"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GroundScar"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -1, -1   // 地面ポリとの Z ファイト防止

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _NormalStrength;
                half4  _EmissionColor;
                float  _EmberFlickerSpeed;
                half   _EmberSharpness;
                half   _Cool;
                half   _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half3  normalWS    : TEXCOORD1;
                half3  tangentWS   : TEXCOORD2;
                half3  bitangentWS : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);

                VertexNormalInputs n = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.normalWS    = n.normalWS;
                OUT.tangentWS   = n.tangentWS;
                OUT.bitangentWS = n.bitangentWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half  shape = tex.a; // アルファ＝跡の形・濃さ
                half  alpha = shape * _BaseColor.a * _Fade;
                clip(alpha - 0.003);

                // プロシージャル生成ノーマルで抉れの立体感（メインライト + 環境光の簡易ライティング）
                half3 nTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _NormalStrength);
                half3x3 tbn = half3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                half3 nWS = normalize(mul(nTS, tbn));

                Light mainLight = GetMainLight();
                half  ndl     = saturate(dot(nWS, mainLight.direction));
                half3 ambient = SampleSH(nWS);
                half3 albedo  = tex.rgb * _BaseColor.rgb;
                half3 lit     = albedo * (mainLight.color * ndl + ambient);

                // 残り火：跡の芯（アルファが濃い所）だけが魔力色で明滅し、_Cool で冷える。
                // UVごとに位相をずらして「くすぶり」のムラを出す。
                half  ember   = pow(shape, _EmberSharpness);
                half  flicker = 0.7 + 0.3 * sin(_Time.y * _EmberFlickerSpeed
                                  + IN.uv.x * 37.0 + IN.uv.y * 53.0);
                half3 glow    = _EmissionColor.rgb * ember * flicker * (1.0h - _Cool);

                return half4(lit + glow, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
