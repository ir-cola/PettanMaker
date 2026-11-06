Shader "_Project/PostEffects/LensDirtBloom"
{
	Properties
	{
		_Threshold ("Threshold", Range(0, 5)) = 1.1
		_Knee ("Knee", Range(0, 1)) = 0.4
		_Intensity ("Intensity", Range(0, 5)) = 1.35
		_Radius ("Radius", Range(0.5, 4)) = 1.6
		_DirtStrength ("Dirt Strength", Range(0, 2)) = 0.45
		_DirtScale ("Dirt Scale", Range(0.5, 8)) = 2.4
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
				float _DirtStrength;
				float _DirtScale;
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

			float Hash21(float2 p)
			{
				p = frac(p * float2(123.34, 456.21));
				p += dot(p, p + 45.32);
				return frac(p.x * p.y);
			}

			float3 SampleBlur(float2 uv)
			{
				float2 texel = _ScreenParams.zw * _Radius;
				float3 sum = 0;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.28;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0)).rgb * 0.12;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, 0)).rgb * 0.12;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, texel.y)).rgb * 0.12;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, -texel.y)).rgb * 0.12;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, texel.y)).rgb * 0.08;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, texel.y)).rgb * 0.08;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, -texel.y)).rgb * 0.08;
				sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, -texel.y)).rgb * 0.08;
				return sum;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				float lum = dot(src.rgb, float3(0.299, 0.587, 0.114));
				float bloomMask = saturate((lum - _Threshold) / max(_Knee, 0.0001));
				bloomMask = bloomMask * bloomMask;

				float3 blur = SampleBlur(input.uv) * _Tint.rgb;
				float dirt = 0.7 + 0.3 * Hash21(input.uv * _ScreenParams.xy * _DirtScale + _Time.y);
				dirt = lerp(1.0, dirt, saturate(_DirtStrength));

				float3 c = src.rgb + blur * bloomMask * _Intensity * dirt;
				return half4(c, src.a);
			}
			ENDHLSL
		}
	}
}
