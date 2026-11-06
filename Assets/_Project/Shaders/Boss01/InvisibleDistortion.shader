Shader "PettanMaker/Boss_01/InvisibleDistortion"
{
    Properties
    {
        [Header(Cloak)]
        _Alpha ("Alpha", Range(0, 1)) = 0.18
        _DistortionOpacity ("Distortion Opacity", Range(0, 1)) = 0.75
        _DistortionStrength ("Distortion Strength", Range(0, 1)) = 0.025
        _DistortionScale ("Distortion Scale", Range(0.1, 20)) = 5
        _DistortionSpeed ("Distortion Speed", Range(0, 5)) = 1.2
        _NoiseTex ("Noise Texture", 2D) = "bump" {}
        _NoiseTexBlend ("Noise Texture Blend", Range(0, 1)) = 0

        [Header(Screen Edge Wobble)]
        _EdgeWobbleStrength ("Edge Wobble Strength", Range(0, 1)) = 0.35
        _EdgeWobbleScale ("Edge Wobble Scale", Range(1, 200)) = 55
        _EdgeWobbleSpeed ("Edge Wobble Speed", Range(0, 5)) = 1.4
        _EdgeWobbleDistortion ("Edge Wobble Distortion", Range(0, 1)) = 0.018

        [Header(Edge)]
        _RimColor ("Rim Color", Color) = (0.25, 0.75, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimIntensity ("Rim Intensity", Range(0, 5)) = 1.3

        [Header(Surface)]
        _Tint ("Tint", Color) = (0.55, 0.9, 1, 1)
        _NormalStrength ("Normal Distortion", Range(0, 2)) = 0.6

        [Header(Dissolve Noise like SkydomeBlendSystem_Noise)]
        _Dissolve            ("Dissolve (0=visible 1=gone)", Range(0,1)) = 0
        [HDR] _DissolveEdgeColor  ("Dissolve Edge Color (flame body)", Color)  = (1.6, 0.5, 1.2, 1)
        [HDR] _DissolveEmberColor ("Dissolve Ember Color (hot core)",  Color)  = (2.4, 1.6, 0.6, 1)
        _DissolveEdge        ("Dissolve Edge Width", Range(0,0.5))       = 0.12
        _DissolveScale       ("Dissolve Noise Scale", Range(0.5,8))      = 3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InvisibleDistortion"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            // B1_Toon と1文字違わぬ同じノイズ場を使う。ここが別実装だと相補クリップがずれ、
            // クロークと実ボスが同時に見える／隙間が空く。
            #include "../B1_Dissolve.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half _Alpha;
                half _DistortionOpacity;
                half _DistortionStrength;
                half _DistortionScale;
                half _DistortionSpeed;
                half _NoiseTexBlend;
                half _EdgeWobbleStrength;
                half _EdgeWobbleScale;
                half _EdgeWobbleSpeed;
                half _EdgeWobbleDistortion;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half4 _Tint;
                half _NormalStrength;
                // B1_Toon 側は float。half にすると溶け際の境界が実ボスとずれるので合わせる。
                float _Dissolve;
                half4 _DissolveEdgeColor;
                half4 _DissolveEmberColor;
                float _DissolveEdge;
                float _DissolveScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 uv)
            {
                float2 id = floor(uv);
                float2 f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = Hash21(id);
                float b = Hash21(id + float2(1.0, 0.0));
                float c = Hash21(id + float2(0.0, 1.0));
                float d = Hash21(id + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float2 ProceduralFlow(float2 uv, float time)
            {
                float n1 = ValueNoise(uv + float2(time * 0.37, time * 0.19));
                float n2 = ValueNoise(uv * 1.71 + float2(-time * 0.23, time * 0.31));
                float n3 = ValueNoise(uv * 0.63 + float2(time * 0.11, -time * 0.29));
                float angle = (n1 + n2 * 0.5 + n3 * 0.25) * 12.56637061436;

                return float2(cos(angle), sin(angle));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _NoiseTex);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.positionWS = positionInputs.positionWS;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // SkydomeBlendSystem_Noise と同じ有機ノイズで溶かす（クローク → 実マテリアルのクロスフェード）
                float dissolveField = B1_DissolveField(input.positionWS, input.positionCS.xy, _DissolveScale);
                float dissolveEdge  = _DissolveEdge > 0.0 ? _DissolveEdge : 0.12;
                float dissolveFront = _Dissolve * (1.0 + dissolveEdge * 2.0); // ハロー分まで溶かし切る
                if (_Dissolve > 0.0001 && dissolveField < dissolveFront - dissolveEdge) clip(-1);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                float time = _Time.y * _DistortionSpeed;
                float2 noiseUV1 = input.uv * _DistortionScale + float2(time * 0.13, time * 0.21);
                float2 noiseUV2 = input.uv * (_DistortionScale * 1.73) + float2(-time * 0.19, time * 0.11);

                half2 noise1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV1).rg * 2.0h - 1.0h;
                half2 noise2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV2).rg * 2.0h - 1.0h;
                half2 textureFlow = normalize(noise1 + noise2 + 0.001h);
                float2 proceduralFlow = ProceduralFlow(input.uv * _DistortionScale, time);
                half2 flow = normalize(lerp(proceduralFlow, textureFlow, _NoiseTexBlend) + 0.001h);

                float edgeTime = _Time.y * _EdgeWobbleSpeed;
                float2 edgeUV = screenUV * _EdgeWobbleScale;
                float edgeNoiseA = ValueNoise(edgeUV + float2(edgeTime * 0.41, edgeTime * 0.23));
                float edgeNoiseB = ValueNoise(edgeUV * 1.67 + float2(-edgeTime * 0.19, edgeTime * 0.37));
                half edgeWobble = ((edgeNoiseA + edgeNoiseB * 0.5h) / 1.5h - 0.5h) * 2.0h;
                half baseFresnel = pow(saturate(1.0h - dot(normalWS, viewDirWS)), _RimPower);
                half fresnel = saturate(baseFresnel + edgeWobble * _EdgeWobbleStrength * saturate(baseFresnel * 2.0h));
                half distortionMask = lerp(0.35h, 1.0h, fresnel);
                float2 normalOffset = normalWS.xy * (_DistortionStrength * _NormalStrength);
                float2 edgeOffset = ProceduralFlow(edgeUV * 0.25h, edgeTime) * _EdgeWobbleDistortion * saturate(baseFresnel * 2.0h);
                float2 distortionUV = screenUV + flow * _DistortionStrength * distortionMask + normalOffset + edgeOffset;

                half3 sceneColor = SampleSceneColor(distortionUV);
                half3 rim = _RimColor.rgb * fresnel * _RimIntensity;
                half3 color = sceneColor * lerp(1.0h, _Tint.rgb, saturate(_Alpha * 0.55h)) + rim;
                half edgeAlpha = saturate(fresnel + edgeWobble * _EdgeWobbleStrength);
                half alpha = saturate(max(_Alpha + edgeAlpha * 0.22h, _DistortionOpacity * distortionMask));

                // 溶け際の「燃えるウェーブフロント」（SkydomeBlendSystem_Noise と同じ：白熱コア/炎本体/ハロー/火の粉）
                // HDR 加算 → Bloom で豪華に光る。
                if (_Dissolve > 0.0001)
                {
                    float dd = dissolveField - (dissolveFront - dissolveEdge); // 溶け際(front)からの距離
                    float core = saturate(1.0 - dd / (dissolveEdge * 0.6));    // 白熱コア（細い）
                    float body = saturate(1.0 - dd / (dissolveEdge * 1.2));    // 炎本体
                    float halo = saturate(1.0 - dd / (dissolveEdge * 2.4));    // 外側ハロー

                    half3 cMain = _DissolveEdgeColor.a  > 0.0h ? _DissolveEdgeColor.rgb  : half3(1.6h, 0.35h, 1.2h);
                    half3 cHot  = _DissolveEmberColor.a > 0.0h ? _DissolveEmberColor.rgb : half3(2.4h, 1.6h, 0.6h);

                    // 火の粉（エンバー）：高周波ノイズをしきい値化して front 帯だけに散らし時間で瞬かせる
                    float escale = (_DissolveScale > 0.0 ? _DissolveScale : 3.0) * 2.0;
                    float en = B1_DisFbm(input.positionWS.xz * escale + _Time.y * 0.3);
                    float ember = smoothstep(0.62, 0.72, en) * body;
                    ember *= 0.55 + 0.45 * sin(_Time.y * 8.0 + en * 30.0);

                    half3 glow = cMain * (body * body) * 1.6h
                               + cHot  * (core * core) * 3.5h
                               + cHot  * ember * 5.0h
                               + cMain * (halo * halo) * 0.35h;

                    color += glow;
                    alpha = saturate(alpha + max(core, ember)); // 溶け際は不透明寄りにして炎を見せる
                }

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
