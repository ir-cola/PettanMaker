Shader "_Project/PostEffects/ScreenSpaceAO"
{
	Properties
	{
		_Intensity ("Intensity", Range(0, 2)) = 1.15
		_Radius ("Radius", Range(0.5, 8)) = 4.0
		_Threshold ("Threshold", Range(0.0001, 0.05)) = 0.003
		_Darkness ("Darkness", Range(0, 2)) = 0.7
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
				float _Radius;
				float _Threshold;
				float _Darkness;
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

			float SampleAO(float2 uv, float centerDepth, float2 offset)
			{
				float sampleDepth = LinearEyeDepth(SampleSceneDepth(uv + offset), _ZBufferParams);
				float relative = (centerDepth - sampleDepth) / max(centerDepth, 0.001);
				return saturate((relative - _Threshold) * _Radius);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				float centerDepth = LinearEyeDepth(SampleSceneDepth(input.uv), _ZBufferParams);
				float2 texel = _ScreenParams.zw * _Radius;

				float ao = 0.0;
				ao += SampleAO(input.uv, centerDepth, float2(texel.x, 0));
				ao += SampleAO(input.uv, centerDepth, float2(-texel.x, 0));
				ao += SampleAO(input.uv, centerDepth, float2(0, texel.y));
				ao += SampleAO(input.uv, centerDepth, float2(0, -texel.y));
				ao += SampleAO(input.uv, centerDepth, float2(texel.x, texel.y));
				ao += SampleAO(input.uv, centerDepth, float2(-texel.x, texel.y));
				ao += SampleAO(input.uv, centerDepth, float2(texel.x, -texel.y));
				ao += SampleAO(input.uv, centerDepth, float2(-texel.x, -texel.y));
				ao = saturate(ao / 8.0 * _Intensity);

				float shade = saturate(1.0 - ao * _Darkness);
				float3 c = src.rgb * shade;
				return half4(c, src.a);
			}
			ENDHLSL
		}
	}
}
