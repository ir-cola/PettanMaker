Shader "_Project/PostEffects/Bloom"
{
	Properties
	{
		_Threshold ("Threshold", Range(0, 5)) = 1.0
		_Knee ("Knee", Range(0, 1)) = 0.4
		_Intensity ("Intensity", Range(0, 5)) = 1.15
		_Radius ("Radius", Range(0.5, 4)) = 1.7
		_Tint ("Tint", Color) = (1, 1, 1, 1)
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
				float _Threshold;
				float _Knee;
				float _Intensity;
				float _Radius;
				float4 _Tint;
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

			float3 SampleBlur(float2 uv)
			{
				float2 texel = _ScreenParams.zw * _Radius;
				float3 sum = 0;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, 0)).rgb;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0)).rgb;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, -texel.y)).rgb;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, texel.y)).rgb;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 2.0;
				return sum / 6.0;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				float lum = dot(src.rgb, float3(0.299, 0.587, 0.114));
				float bloomMask = saturate((lum - _Threshold) / max(_Knee, 0.0001));
				bloomMask = bloomMask * bloomMask;

				float3 blur = SampleBlur(input.uv) * _Tint.rgb;
				float3 c = src.rgb + blur * bloomMask * _Intensity;
				return half4(c, src.a);
			}
			ENDHLSL
		}
	}
}
