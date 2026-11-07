Shader "_Project/PostEffects/MotionBlur"
{
	Properties
	{
		_Intensity ("Intensity", Range(0, 2)) = 0.45
		_Samples ("Samples", Range(2, 16)) = 10
		_DepthScale ("Depth Scale", Range(0, 5)) = 0.85
		_ClampMinDepth ("Clamp Min Depth", Float) = 0.0
		_ClampMaxDepth ("Clamp Max Depth", Float) = 1000.0
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
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			TEXTURE2D_X(_BlitTexture);

			CBUFFER_START(UnityPerMaterial)
				float _Intensity;
				float _Samples;
				float _DepthScale;
				float _ClampMinDepth;
				float _ClampMaxDepth;
			CBUFFER_END

			float4x4 _PostProcessPreviousViewProjection;

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

			float2 ReprojectToPreviousUV(float2 uv, float depth)
			{
				float4 clip = float4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
				float4 world = mul(unity_MatrixInvVP, clip);
				world.xyz /= max(world.w, 0.0001);
				float4 prevClip = mul(_PostProcessPreviousViewProjection, float4(world.xyz, 1.0));
				float2 prevUV = prevClip.xy / max(prevClip.w, 0.0001) * 0.5 + 0.5;
				return prevUV;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float depth = SampleSceneDepth(input.uv);
				float eyeDepth = LinearEyeDepth(depth, _ZBufferParams);
				if (eyeDepth < _ClampMinDepth || eyeDepth > _ClampMaxDepth)
				{
					return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				}

				float2 prevUV = ReprojectToPreviousUV(input.uv, depth);
				float2 motion = input.uv - prevUV;
				motion *= _Intensity * saturate(1.0 / max(eyeDepth * _DepthScale, 0.001));

				int sampleCount = (int)round(clamp(_Samples, 2.0, 16.0));
				float3 sum = 0.0;
				float totalWeight = 0.0;

				[loop]
				for (int i = 0; i < sampleCount; i++)
				{
					float t = (sampleCount <= 1) ? 0.0 : (float)i / (sampleCount - 1);
					float2 suv = input.uv - motion * (t - 0.5) * 2.0;
					float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, suv).rgb;
					float w = 1.0 - abs(t - 0.5) * 2.0;
					sum += c * w;
					totalWeight += w;
				}

				float3 blurred = sum / max(totalWeight, 0.0001);
				float3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv).rgb;
				float3 finalColor = lerp(center, blurred, saturate(length(motion) * 120.0));
				return half4(finalColor, 1.0);
			}
			ENDHLSL
		}
	}
}
