Shader "_Project/PostEffects/Vignette"
{
	Properties
	{
		_Intensity ("Intensity", Range(0, 1)) = 0.18
		_Smoothness ("Smoothness", Range(0, 1)) = 0.7
		_Roundness ("Roundness", Range(0.1, 2)) = 1.25
		_Ellipse ("Ellipse Scale", Vector) = (1, 0.8, 0, 0)
		_Color ("Color", Color) = (0, 0, 0, 1)
		_Center ("Center", Vector) = (0.5, 0.5, 0, 0)
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
				float _Smoothness;
				float _Roundness;
				float4 _Ellipse;
				float4 _Color;
				float4 _Center;
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

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);

				float2 d = input.uv - _Center.xy;
				d.x *= _ScreenParams.x / _ScreenParams.y;
				d = abs(d / max(_Ellipse.xy, 0.0001));
				float v = pow(pow(d.x, _Roundness) + pow(d.y, _Roundness), 1.0 / _Roundness);

				float m = smoothstep(_Smoothness, 1.0, v);
				float a = saturate(m * _Intensity);
				float3 c = lerp(src.rgb, src.rgb * _Color.rgb, a);
				return half4(c, src.a);
			}
			ENDHLSL
		}
	}
}
