Shader "_Project/PostEffects/SSAO"
{
	Properties
	{
		_Intensity ("Intensity", Range(0, 2)) = 0.6
		_Radius ("Radius", Range(0.2, 3)) = 1.2
		_Bias ("Bias", Range(0.0001, 0.02)) = 0.002
		_Falloff ("Falloff", Range(0.1, 4)) = 1.5
		_AOColor ("AO Color", Color) = (0.92, 0.94, 0.98, 1)
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
				float _Bias;
				float _Falloff;
				float4 _AOColor;
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

			float3 WorldPos(float2 uv, float rawDepth)
			{
				return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				float rawDepth = SampleSceneDepth(input.uv);
				if (rawDepth >= 0.999999)
				{
					return src;
				}

				float3 centerPos = WorldPos(input.uv, rawDepth);
				float centerDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
				float2 texel = _ScaledScreenParams.zw - 1.0;

				const float2 kDirs[8] =
				{
					float2(1,0), float2(-1,0), float2(0,1), float2(0,-1),
					float2(0.707,0.707), float2(-0.707,0.707), float2(0.707,-0.707), float2(-0.707,-0.707)
				};

				float occ = 0.0;
				[unroll]
				for (int i = 0; i < 8; i++)
				{
					float2 uv = saturate(input.uv + kDirs[i] * texel * _Radius * 2.0);
					float sRawDepth = SampleSceneDepth(uv);
					if (sRawDepth >= 0.999999)
					{
						continue;
					}

					float sDepth = LinearEyeDepth(sRawDepth, _ZBufferParams);
					float depthDelta = sDepth - centerDepth;
					float blocker = saturate((-depthDelta - _Bias) / max(_Radius, 1e-4));
					float atten = exp(-abs(depthDelta) * _Falloff);
					occ += blocker * atten;
				}

				occ = saturate(occ / 8.0) * _Intensity;
				float3 aoTint = lerp(1.0.xxx, _AOColor.rgb, occ);
				float3 result = src.rgb * aoTint;
				return half4(result, src.a);
			}
			ENDHLSL
		}
	}
}
