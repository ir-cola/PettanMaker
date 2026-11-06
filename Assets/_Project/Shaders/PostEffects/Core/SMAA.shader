Shader "_Project/PostEffects/SMAA"
{
	Properties
	{
		_EdgeThreshold ("Edge Threshold", Range(0.02, 0.3)) = 0.08
		_BlendStrength ("Blend Strength", Range(0, 1.5)) = 1.0
		_MaxSearchSteps ("Max Search Steps", Range(2, 16)) = 8
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
				float _EdgeThreshold;
				float _BlendStrength;
				float _MaxSearchSteps;
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

			float Luma(float3 c)
			{
				return dot(c, float3(0.299, 0.587, 0.114));
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float2 texel = _ScaledScreenParams.zw - 1.0;
				float2 uv = input.uv;
				float3 c = SampleColor(uv);
				float l = Luma(c);

				float lN = Luma(SampleColor(uv + float2(0, texel.y)));
				float lS = Luma(SampleColor(uv - float2(0, texel.y)));
				float lE = Luma(SampleColor(uv + float2(texel.x, 0)));
				float lW = Luma(SampleColor(uv - float2(texel.x, 0)));

				float edgeH = abs(lE - lW);
				float edgeV = abs(lN - lS);
				float edge = max(edgeH, edgeV);

				if (edge < _EdgeThreshold)
				{
					return half4(c, 1.0);
				}

				float2 dir = edgeH > edgeV ? float2(0, texel.y) : float2(texel.x, 0);
				float3 accum = c;
				float weight = 1.0;
				int steps = (int)round(clamp(_MaxSearchSteps, 2.0, 16.0));

				[loop]
				for (int i = 1; i <= steps; i++)
				{
					float t = (float)i / (steps + 1.0);
					float3 cp = SampleColor(saturate(uv + dir * i));
					float3 cn = SampleColor(saturate(uv - dir * i));
					float w = exp(-t * 2.2);
					accum += (cp + cn) * w;
					weight += 2.0 * w;
				}

				float3 smoothColor = accum / max(weight, 1e-4);
				float blend = saturate(edge * 4.0) * _BlendStrength;
				float3 result = lerp(c, smoothColor, blend);
				return half4(result, 1.0);
			}
			ENDHLSL
		}
	}
}
