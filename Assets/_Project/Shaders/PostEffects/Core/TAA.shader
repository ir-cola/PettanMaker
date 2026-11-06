Shader "_Project/PostEffects/TAA"
{
	Properties
	{
		_Blend ("Temporal Blend", Range(0, 0.98)) = 0.88
		_MotionRejection ("Motion Rejection", Range(0, 1)) = 0.45
		_Sharpness ("Sharpness", Range(0, 2)) = 0.35
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
				float _Blend;
				float _MotionRejection;
				float _Sharpness;
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

			float3 SampleColor(float2 uv)
			{
				return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float2 uv = input.uv;
				float rawDepth = SampleSceneDepth(uv);
				float3 current = SampleColor(uv);

				if (rawDepth >= 0.999999)
				{
					return half4(current, 1.0);
				}

				float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
				float4 previousClip = mul(_PostProcessPreviousViewProjection, float4(worldPos, 1.0));
				float2 previousUv = previousClip.xy / max(previousClip.w, 1e-5) * 0.5 + 0.5;
				previousUv.y = 1.0 - previousUv.y;

				float3 historyApprox = SampleColor(previousUv);

				float2 motion = uv - previousUv;
				float motionLen = length(motion) * 120.0;
				float motionReject = saturate(motionLen * _MotionRejection);
				float temporalWeight = saturate(_Blend * (1.0 - motionReject));

				float3 temporal = lerp(current, historyApprox, temporalWeight);

				float2 texel = _ScaledScreenParams.zw - 1.0;
				float3 neighborhood =
					SampleColor(uv + float2(texel.x, 0)) +
					SampleColor(uv - float2(texel.x, 0)) +
					SampleColor(uv + float2(0, texel.y)) +
					SampleColor(uv - float2(0, texel.y));
				neighborhood *= 0.25;

				float3 sharpened = temporal + (temporal - neighborhood) * _Sharpness;
				return half4(saturate(sharpened), 1.0);
			}
			ENDHLSL
		}
	}
}
