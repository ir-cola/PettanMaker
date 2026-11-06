Shader "_Project/PostEffects/FilmGrain"
{
	Properties
	{
		_Intensity ("Intensity", Range(0, 1)) = 0.05
		_Scale ("Scale", Range(0.5, 4)) = 1.1
		_Contrast ("Contrast", Range(0, 2)) = 0.25
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
				float _Intensity;
				float _Scale;
				float _Contrast;
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

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				float n = Hash21(input.uv * _ScreenParams.xy * _Scale + _Time.y);
				n = (n - 0.5) * _Intensity;
				float3 c = src.rgb + n.xxx;
				c = ((c - 0.5) * (1.0 + _Contrast)) + 0.5;
				return half4(saturate(c), src.a);
			}
			ENDHLSL
		}
	}
}
