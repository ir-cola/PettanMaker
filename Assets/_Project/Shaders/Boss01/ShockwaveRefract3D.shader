Shader "PettanMaker/Boss_01/ShockwaveRefract3D"
{
    // ワールド空間の3D衝撃波シェル。RoarWave3D と同じフレネル・シェルの発光に加え、
    // シェル面のビュー空間法線で背後のシーンカラーを屈折（歪み）させる。
    // 歪みは3Dジオメトリ由来なので、フルスクリーン後処理のような安っぽさが出ない。
    //  ・_Dispersion: RGB を波長別にずらして屈折させ、シェル縁にプリズムの色ズレを出す
    //  ・_SoftFade  : シーン深度でジオメトリ交差を柔らかくフェード（床/壁に刺さる硬い線を消す）
    // 描画には URP の Opaque Texture と Depth Texture が必要。
    Properties
    {
        _Progress ("Progress", Range(0, 1)) = 0
        _RingWidth ("Shell Width", Range(0.02, 0.9)) = 0.4
        _Strength ("Surface Ripple", Range(0, 1)) = 0.2
        _Refraction ("Scene Refraction", Range(0, 0.3)) = 0.06
        _Dispersion ("Chromatic Dispersion", Range(0, 1)) = 0.35
        _SoftFade ("Soft Intersection Fade (m)", Range(0.01, 3)) = 0.6
        [HDR] _RimColor ("Roar Color", Color) = (1.4, 0.85, 0.4, 1)
        _RimIntensity ("Emission", Range(0, 8)) = 2.2
        _FresnelPower ("Fresnel Power", Range(0.2, 8)) = 2.2
        _NoiseScale ("Noise Scale", Range(0.1, 20)) = 4
        _NoiseSpeed ("Noise Speed", Range(-10, 10)) = 2
        _CoreAlpha ("Core Alpha", Range(0, 1)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShockwaveRefract3D"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Progress;
                half _RingWidth;
                half _Strength;
                half _Refraction;
                half _Dispersion;
                half _SoftFade;
                half4 _RimColor;
                half _RimIntensity;
                half _FresnelPower;
                half _NoiseScale;
                half _NoiseSpeed;
                half _CoreAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half noise : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 p = input.positionOS.xyz;
                float phase = dot(p, float3(1.73, 2.31, 2.87)) * _NoiseScale
                            + _Time.y * _NoiseSpeed;
                float noise = sin(phase) * 0.55 + sin(phase * 1.91 + 1.7) * 0.3;
                p += input.normalOS * noise * _Strength * 0.12 * (1.0 - _Progress);

                VertexPositionInputs pos = GetVertexPositionInputs(p);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = normal.normalWS;
                output.noise = noise * 0.5 + 0.5;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                half3 v = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(saturate(1.0h - abs(dot(n, v))), _FresnelPower);
                half shell = smoothstep(1.0h - _RingWidth, 1.0h, fresnel);
                half ripple = lerp(0.65h, 1.35h, input.noise);
                half life = saturate(1.0h - _Progress);
                life *= life;

                // 画面端のサンプリング破綻を防ぐため UV をクランプする。
                float2 screenUV = clamp(input.screenPos.xy / input.screenPos.w,
                                        float2(0.001, 0.001), float2(0.999, 0.999));

                // シーン深度との差でジオメトリ交差を柔らかくフェード（床/壁に刺さる硬い線を消す）。
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragEye  = input.screenPos.w;
                half soft = saturate((half)((sceneEye - fragEye) / max(_SoftFade, 0.01h)));

                // シェル面のビュー空間法線で背後のシーンを横へずらして屈折させる。
                // ノイズで屈折量を揺らし、均一な「レンズ」ではなく熱気のような揺らぎにする。
                half3 nVS = mul((float3x3)UNITY_MATRIX_V, n);
                half refractMod = lerp(0.7h, 1.3h, input.noise);
                float2 offset = nVS.xy * shell * ripple * _Refraction * life * soft * refractMod;

                // 色分散: RGB を波長別に少しずつ違う量で屈折させ、縁にプリズムの色ズレを出す。
                half3 scene;
                scene.r = SampleSceneColor(screenUV + offset * (1.0h + _Dispersion)).r;
                scene.g = SampleSceneColor(screenUV + offset).g;
                scene.b = SampleSceneColor(screenUV + offset * (1.0h - _Dispersion)).b;

                half3 glow = _RimColor.rgb * _RimIntensity * shell * ripple * life;
                half alpha = saturate((shell + _CoreAlpha) * ripple * life) * _RimColor.a * soft;
                return half4(scene + glow, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
