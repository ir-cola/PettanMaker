Shader "_Project/PostEffects/AerialPerspective"
{
	Properties
	{
		_HazeColor ("Haze Color", Color) = (0.72, 0.82, 0.95, 1)
		_HazeStart ("Haze Start", Float) = 25
		_HazeEnd ("Haze End", Float) = 180
		_HazeStrength ("Haze Strength", Range(0, 2)) = 0.65
		_HorizonHeight ("Horizon Height", Float) = 6
		_HorizonSoftness ("Horizon Softness", Range(0.1, 32)) = 8
		_DistanceDesat ("Distance Desaturation", Range(0, 1)) = 0.25
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
				float4 _HazeColor;
				float _HazeStart;
				float _HazeEnd;
				float _HazeStrength;
				float _HorizonHeight;
				float _HorizonSoftness;
				float _DistanceDesat;
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

			float Luma(float3 c)
			{
				return dot(c, float3(0.299, 0.587, 0.114));
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				float rawDepth = SampleSceneDepth(input.uv);
				// 深度未書き込み (空など) はヘイズ対象外。逆Zでは「未書き込み=0」(Fog.shader と同じ修正)
				#if UNITY_REVERSED_Z
				if (rawDepth <= 0.000001)
				{
					return src;
				}
				#else
				if (rawDepth >= 0.999999)
				{
					return src;
				}
				#endif

				float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
				float distanceHaze = saturate((depth - _HazeStart) / max(_HazeEnd - _HazeStart, 1e-4));
				distanceHaze = pow(distanceHaze, 1.15) * _HazeStrength;

				float3 worldPos = ComputeWorldSpacePosition(input.uv, rawDepth, UNITY_MATRIX_I_VP);
				float horizonMask = saturate(1.0 - abs(worldPos.y - _HorizonHeight) / max(_HorizonSoftness, 1e-4));
				horizonMask = smoothstep(0.0, 1.0, horizonMask);

				float haze = saturate(distanceHaze * (0.75 + horizonMask * 0.5));
				float3 desatColor = lerp(src.rgb, Luma(src.rgb).xxx, _DistanceDesat * haze);
				float3 result = lerp(desatColor, _HazeColor.rgb, haze);
				return half4(result, src.a);
			}
			ENDHLSL
		}
	}
}
