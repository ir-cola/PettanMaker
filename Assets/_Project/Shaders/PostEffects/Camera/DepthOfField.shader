Shader "_Project/PostEffects/DepthOfField"
{
	Properties
	{
		_FocusDistance ("Focus Distance", Float) = 8
		_FocusRange ("Focus Range", Range(0.1, 20)) = 3
		_MaxBlur ("Max Blur", Range(0, 8)) = 2.2
		_NearBlurStrength ("Near Blur Strength", Range(0, 2)) = 1.1
		_FarBlurStrength ("Far Blur Strength", Range(0, 2)) = 1.1
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
				float _FocusDistance;
				float _FocusRange;
				float _MaxBlur;
				float _NearBlurStrength;
				float _FarBlurStrength;
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

			float3 SampleTap(float2 uv)
			{
				return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float rawDepth = SampleSceneDepth(input.uv);
				float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

				float nearDiff = saturate((_FocusDistance - eyeDepth) / max(_FocusRange, 0.0001));
				float farDiff = saturate((eyeDepth - _FocusDistance) / max(_FocusRange, 0.0001));
				float blurFactor = saturate(nearDiff * _NearBlurStrength + farDiff * _FarBlurStrength);
				blurFactor = blurFactor * blurFactor;

				float radius = _MaxBlur * blurFactor;
				float2 texel = _ScreenParams.zw * radius;

				float3 sum = 0.0;
				sum += SampleTap(input.uv) * 0.20;
				sum += SampleTap(input.uv + float2(texel.x, 0.0)) * 0.12;
				sum += SampleTap(input.uv + float2(-texel.x, 0.0)) * 0.12;
				sum += SampleTap(input.uv + float2(0.0, texel.y)) * 0.12;
				sum += SampleTap(input.uv + float2(0.0, -texel.y)) * 0.12;
				sum += SampleTap(input.uv + float2(texel.x, texel.y)) * 0.11;
				sum += SampleTap(input.uv + float2(-texel.x, texel.y)) * 0.11;
				sum += SampleTap(input.uv + float2(texel.x, -texel.y)) * 0.10;
				sum += SampleTap(input.uv + float2(-texel.x, -texel.y)) * 0.10;

				float3 sharp = SampleTap(input.uv);
				float3 finalColor = lerp(sharp, sum, blurFactor);
				return half4(finalColor, 1.0);
			}
			ENDHLSL
		}
	}
}
