Shader "_Project/PostEffects/SoftAA"
{
	Properties
	{
		_Strength ("Strength", Range(0, 2)) = 1.0
		_EdgeSensitivity ("Edge Sensitivity", Range(0.1, 4)) = 1.5
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
				float _EdgeSensitivity;
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

				float3 center = SampleColor(uv);
				float3 n = SampleColor(uv + float2(0, texel.y));
				float3 s = SampleColor(uv - float2(0, texel.y));
				float3 e = SampleColor(uv + float2(texel.x, 0));
				float3 w = SampleColor(uv - float2(texel.x, 0));

				float lCenter = Luma(center);
				float edge = abs(lCenter - Luma(n)) + abs(lCenter - Luma(s)) + abs(lCenter - Luma(e)) + abs(lCenter - Luma(w));
				edge = saturate(edge * _EdgeSensitivity);

				float3 blur = (center * 4.0 + n + s + e + w) / 8.0;
				float blend = saturate(edge * _Strength);
				float3 result = lerp(center, blur, blend);
				return half4(result, 1.0);
			}
			ENDHLSL
		}
	}
}
