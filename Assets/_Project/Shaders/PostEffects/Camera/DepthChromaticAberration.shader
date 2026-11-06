Shader "_Project/PostEffects/DepthChromaticAberration"
{
	Properties
	{
		_Intensity ("Intensity", Range(0, 2)) = 1
		_ChromaticStrength ("Chromatic Strength", Range(0, 5)) = 1
		_DepthStart ("Depth Start", Float) = 2
		_DepthEnd ("Depth End", Float) = 25
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
				float _Intensity;
				float _ChromaticStrength;
				float _DepthStart;
				float _DepthEnd;
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
				float rawDepth = SampleSceneDepth(input.uv);
				float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
				float depthMask = saturate((eyeDepth - _DepthStart) / max(_DepthEnd - _DepthStart, 0.0001));

				float2 offset = float2(_ChromaticStrength * _Intensity * depthMask, 0.0) * _ScreenSize.zw;

				half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv + offset).r;
				half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv).g;
				half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv - offset).b;
				half a = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv).a;

				return half4(r, g, b, a);
			}
			ENDHLSL
		}
	}
}
