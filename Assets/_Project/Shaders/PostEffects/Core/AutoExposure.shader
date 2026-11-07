Shader "_Project/PostEffects/AutoExposure"
{
	Properties
	{
		_KeyValue ("Key Value", Range(0.05, 1.5)) = 0.18
		_Compensation ("Compensation", Range(-2, 2)) = 0
		_MinExposure ("Min Exposure", Range(0.1, 2)) = 0.4
		_MaxExposure ("Max Exposure", Range(0.5, 8)) = 2.2
		_Response ("Response", Range(0.2, 3)) = 1.0
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

			CBUFFER_START(UnityPerMaterial)
				float _KeyValue;
				float _Compensation;
				float _MinExposure;
				float _MaxExposure;
				float _Response;
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
				return dot(c, float3(0.2126, 0.7152, 0.0722));
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float3 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv).rgb;

				float2 texel = _ScaledScreenParams.zw - 1.0;
				float sumLogLum = 0.0;
				float count = 0.0;

				[unroll]
				for (int y = -1; y <= 1; y++)
				{
					[unroll]
					for (int x = -1; x <= 1; x++)
					{
						float2 uv = saturate(input.uv + float2(x, y) * texel * 3.0);
						float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
						float lum = max(Luma(c), 1e-4);
						sumLogLum += log2(lum);
						count += 1.0;
					}
				}

				float avgLum = exp2(sumLogLum / max(count, 1.0));
				float targetExposure = (_KeyValue / max(avgLum, 1e-4)) * exp2(_Compensation);
				targetExposure = clamp(targetExposure, _MinExposure, _MaxExposure);

				float adaptedExposure = lerp(1.0, targetExposure, saturate(_Response));
				float3 result = src * adaptedExposure;
				return half4(result, 1.0);
			}
			ENDHLSL
		}
	}
}
