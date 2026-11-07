Shader "_Project/PostEffects/Sharpen"
{
	Properties
	{
		_Strength ("Strength", Range(0, 3)) = 0.55
		_Threshold ("Threshold", Range(0, 1)) = 0.12
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
				float _Strength;
				float _Threshold;
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

			half4 Frag(Varyings input) : SV_Target
			{
				float2 texel = _ScreenParams.zw;
				float3 center = SampleColor(input.uv);

				float3 north = SampleColor(input.uv + float2(0.0, texel.y));
				float3 south = SampleColor(input.uv + float2(0.0, -texel.y));
				float3 east  = SampleColor(input.uv + float2(texel.x, 0.0));
				float3 west  = SampleColor(input.uv + float2(-texel.x, 0.0));
				float3 ne = SampleColor(input.uv + float2(texel.x, texel.y));
				float3 nw = SampleColor(input.uv + float2(-texel.x, texel.y));
				float3 se = SampleColor(input.uv + float2(texel.x, -texel.y));
				float3 sw = SampleColor(input.uv + float2(-texel.x, -texel.y));

				float3 blur = (north + south + east + west + ne + nw + se + sw) * 0.125;
				float3 highPass = center - blur;

				float luma = dot(center, float3(0.299, 0.587, 0.114));
				float mask = saturate((luma - _Threshold) * 2.0);
				float3 result = center + highPass * _Strength * mask;
				return half4(saturate(result), 1.0);
			}
			ENDHLSL
		}
	}
}
