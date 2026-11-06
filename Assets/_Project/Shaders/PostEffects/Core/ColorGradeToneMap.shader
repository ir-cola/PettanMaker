Shader "_Project/PostEffects/ColorGradeToneMap"
{
	Properties
	{
		_Exposure ("Exposure", Range(0.1, 2)) = 1.05
		_Contrast ("Contrast", Range(0.5, 2)) = 1.10
		_Saturation ("Saturation", Range(0, 2)) = 1.12
		_Warmth ("Warmth", Range(-1, 1)) = 0.03
		_BlackLift ("Black Lift", Range(0, 0.2)) = 0.015
		_WhiteClip ("White Clip", Range(0, 0.4)) = 0.06
		_ShadowTint ("Shadow Tint", Color) = (0.97, 0.99, 1.02, 1)
		_MidTint ("Mid Tint", Color) = (1.0, 1.0, 1.0, 1)
		_HighlightTint ("Highlight Tint", Color) = (1.03, 1.0, 0.98, 1)
		_LutTex ("LUT (2D Strip)", 2D) = "white" {}
		_LutStrength ("LUT Strength", Range(0, 1)) = 0
		_LutSize ("LUT Size", Range(8, 64)) = 32
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
			TEXTURE2D(_LutTex);

			CBUFFER_START(UnityPerMaterial)
				float _Exposure;
				float _Contrast;
				float _Saturation;
				float _Warmth;
				float _BlackLift;
				float _WhiteClip;
				float4 _ShadowTint;
				float4 _MidTint;
				float4 _HighlightTint;
				float _LutStrength;
				float _LutSize;
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

			float Luma(float3 c)
			{
				return dot(c, float3(0.299, 0.587, 0.114));
			}

			float3 SampleLut2DStrip(float3 color)
			{
				float size = max(_LutSize, 8.0);
				float invSize = 1.0 / size;
				float slices = size;
				float maxSlice = slices - 1.0;

				float3 c = saturate(color) * maxSlice;
				float sliceLow = floor(c.b);
				float sliceHigh = min(sliceLow + 1.0, maxSlice);
				float sliceFrac = frac(c.b);

				float2 uvLow;
				uvLow.x = (c.r + sliceLow * size + 0.5) / (size * size);
				uvLow.y = (c.g + 0.5) / size;

				float2 uvHigh;
				uvHigh.x = (c.r + sliceHigh * size + 0.5) / (size * size);
				uvHigh.y = uvLow.y;

				float3 lowCol = SAMPLE_TEXTURE2D(_LutTex, sampler_LinearClamp, uvLow).rgb;
				float3 highCol = SAMPLE_TEXTURE2D(_LutTex, sampler_LinearClamp, uvHigh).rgb;
				return lerp(lowCol, highCol, sliceFrac);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				float3 c = src.rgb * _Exposure;

				c = max(c, _BlackLift.xxx);
				c = c / (1.0 + _WhiteClip * c);
				c = ((c - 0.5) * _Contrast) + 0.5;

				float l = Luma(c);
				c = lerp(l.xxx, c, _Saturation);

				float3 warm = float3(1.0 + _Warmth * 0.08, 1.0, 1.0 - _Warmth * 0.08);
				c *= warm;

				float shadowW = 1.0 - smoothstep(0.15, 0.45, l);
				float highlightW = smoothstep(0.55, 0.9, l);
				float midW = saturate(1.0 - shadowW - highlightW);

				float3 tint = shadowW * _ShadowTint.rgb + midW * _MidTint.rgb + highlightW * _HighlightTint.rgb;
				c = saturate(c * tint);

				float3 lutColor = SampleLut2DStrip(c);
				c = lerp(c, lutColor, saturate(_LutStrength));

				return half4(c, src.a);
			}
			ENDHLSL
		}
	}
}
