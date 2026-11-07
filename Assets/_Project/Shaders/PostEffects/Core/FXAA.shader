Shader "_Project/PostEffects/FXAA"
{
	Properties
	{
		_EdgeThresholdMin ("Edge Threshold Min", Range(0.01, 0.2)) = 0.0312
		_EdgeThresholdMax ("Edge Threshold Max", Range(0.03, 0.5)) = 0.125
		_SubpixelBlend ("Subpixel Blend", Range(0, 1)) = 0.75
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
				float _EdgeThresholdMin;
				float _EdgeThresholdMax;
				float _SubpixelBlend;
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

				float3 rgbM = SampleColor(uv);
				float3 rgbN = SampleColor(uv + float2(0.0, texel.y));
				float3 rgbS = SampleColor(uv - float2(0.0, texel.y));
				float3 rgbE = SampleColor(uv + float2(texel.x, 0.0));
				float3 rgbW = SampleColor(uv - float2(texel.x, 0.0));

				float lumaM = Luma(rgbM);
				float lumaN = Luma(rgbN);
				float lumaS = Luma(rgbS);
				float lumaE = Luma(rgbE);
				float lumaW = Luma(rgbW);

				float lumaMin = min(lumaM, min(min(lumaN, lumaS), min(lumaE, lumaW)));
				float lumaMax = max(lumaM, max(max(lumaN, lumaS), max(lumaE, lumaW)));
				float lumaRange = lumaMax - lumaMin;

				if (lumaRange < max(_EdgeThresholdMin, lumaMax * _EdgeThresholdMax))
				{
					return half4(rgbM, 1.0);
				}

				float2 dir;
				dir.x = -((lumaN + lumaS) - 2.0 * lumaM);
				dir.y = ((lumaE + lumaW) - 2.0 * lumaM);
				dir = normalize(dir + 1e-6) * texel;

				float3 rgbA = 0.5 * (
					SampleColor(saturate(uv + dir * (1.0 / 3.0 - 0.5))) +
					SampleColor(saturate(uv + dir * (2.0 / 3.0 - 0.5)))
				);

				float3 rgbB = rgbA * 0.5 + 0.25 * (
					SampleColor(saturate(uv + dir * -0.5)) +
					SampleColor(saturate(uv + dir * 0.5))
				);

				float lumaB = Luma(rgbB);
				float3 aa = (lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB;

				float3 result = lerp(rgbM, aa, _SubpixelBlend);
				return half4(result, 1.0);
			}
			ENDHLSL
		}
	}
}
