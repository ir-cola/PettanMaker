Shader "_Project/PostEffects/GodRays"
{
	Properties
	{
		_LightScreenPos ("Light Screen Pos", Vector) = (0.5, 0.75, 0, 0)
		_OcclusionRadius ("Occlusion Sample Radius", Range(0.001, 0.02)) = 0.005
		_Intensity ("Intensity", Range(0, 5)) = 0.9
		_Decay ("Decay", Range(0.8, 1)) = 0.96
		_Density ("Density", Range(0.5, 2)) = 1.0
		_Weight ("Weight", Range(0, 2)) = 0.6
		_Tint ("Tint", Color) = (1, 0.95, 0.8, 1)
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		Pass
		{
			Name "PostEffect"
			ZWrite Off
			ZTest Always
			Cull Off
			Blend One Zero

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D_X(_BlitTexture);
			TEXTURE2D_X(_CameraDepthTexture);

			CBUFFER_START(UnityPerMaterial)
				float4 _LightScreenPos;
				float _Intensity;
				float _Decay;
				float _Density;
				float _Weight;
				float4 _Tint;
				float _OcclusionRadius;
			CBUFFER_END

			struct Attributes
			{
				uint vertexID : SV_VertexID;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
				output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
				output.uv.y = 1.0 - output.uv.y;
				return output;
			}

			float3 SampleColor(float2 uv)
			{
				return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float2 uv = input.uv;
				float2 lightPos = _LightScreenPos.xy;
				float lightVisible = step(0.0, _LightScreenPos.z) * step(0.0, _LightScreenPos.w);
				float2 delta = (uv - lightPos) * _Density / 12.0;

				// 深度バッファでオクルージョン判定（LensFlareと同じ方式）
				// 変数名は sampleUV と衝突しないよう occUV を使用
				static const float2 occOffsets[5] = {
					float2( 0,  0),
					float2( 1,  1), float2(-1,  1),
					float2( 1, -1), float2(-1, -1)
				};
				float occlusionSum = 0.0;
				[unroll]
				for (int oi = 0; oi < 5; oi++)
				{
					float2 occUV = saturate(lightPos + occOffsets[oi] * _OcclusionRadius);
					float d = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_LinearClamp, occUV).r;
					// Reversed-Z: 0=far(sky)  /  Standard-Z: 1=far(sky)
					#if UNITY_REVERSED_Z
						occlusionSum += step(d, 0.0001);
					#else
						occlusionSum += step(0.9999, d);
					#endif
				}
				float occlusionMask = occlusionSum / 5.0;

				float3 src = SampleColor(uv);
				float3 rays = 0.0;
				float illuminationDecay = 1.0;
				float2 sampleUV = uv;

				[loop]
				for (int i = 0; i < 12; i++)
				{
					sampleUV -= delta;
					float3 sampleCol = SampleColor(saturate(sampleUV));
					sampleCol *= illuminationDecay * _Weight;
					rays += sampleCol;
					illuminationDecay *= _Decay;
				}

				float lightMask = saturate(1.0 - distance(uv, lightPos) * 2.0);
				float3 result = src + rays * _Intensity * lightMask * lightVisible * occlusionMask * _Tint.rgb;
				return half4(result, 1.0);
			}
			ENDHLSL
		}
	}
}
