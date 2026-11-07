Shader "_Project/PostEffects/AnimeStylize"
{
	Properties
	{
		_AlicePaintingIntensity ("Alice Painting Intensity", Range(0, 1)) = 0
		_BrushSize ("Brush Size", Range(0.5, 4)) = 1.6
		_BrushStrength ("Brush Strength", Range(0, 1)) = 0.65
		_Wobble ("Hand Painted Wobble", Range(0, 2)) = 0.35
		_EdgeStrength ("Ink Edge Strength", Range(0, 2)) = 0.38
		_EdgeThreshold ("Ink Edge Threshold", Range(0.01, 0.5)) = 0.12
		_PaperGrain ("Paper Grain", Range(0, 0.25)) = 0.055
		_PosterizeSteps ("Posterize Steps", Range(2, 10)) = 5
		_PosterizeSoftness ("Posterize Softness", Range(0, 0.49)) = 0.16
		_Saturation ("Saturation", Range(0, 2)) = 1.14
		_Vibrance ("Vibrance", Range(0, 2)) = 1.10
		_Contrast ("Contrast", Range(0.5, 2)) = 1.08
		_Gamma ("Gamma", Range(0.7, 1.5)) = 0.98
		_BlackLift ("Black Lift", Range(0, 0.2)) = 0.025
		_WhiteSoftClip ("White Soft Clip", Range(0, 0.4)) = 0.08
		_TonePivot ("Tone Pivot", Range(0, 1)) = 0.52
		_RampSharpness ("Ramp Sharpness", Range(0.5, 3)) = 1.50
		_ShadowTint ("Shadow Tint", Color) = (0.91, 0.95, 1.03, 1)
		_MidTint ("Mid Tint", Color) = (1.02, 1.00, 0.98, 1)
		_HighlightTint ("Highlight Tint", Color) = (1.06, 1.03, 0.97, 1)
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
			float4 _BlitTexture_TexelSize;

			CBUFFER_START(UnityPerMaterial)
				float _AlicePaintingIntensity;
				float _BrushSize;
				float _BrushStrength;
				float _Wobble;
				float _EdgeStrength;
				float _EdgeThreshold;
				float _PaperGrain;
				float _PosterizeSteps;
				float _PosterizeSoftness;
				float _Saturation;
				float _Vibrance;
				float _Contrast;
				float _Gamma;
				float _BlackLift;
				float _WhiteSoftClip;
				float _TonePivot;
				float _RampSharpness;
				float4 _ShadowTint;
				float4 _MidTint;
				float4 _HighlightTint;
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

			float GetLuminance(float3 c)
			{
				return dot(c, float3(0.299, 0.587, 0.114));
			}

			float3 ApplySoftPosterize(float3 c, float steps, float softness)
			{
				float3 scaled = saturate(c) * steps;
				float3 baseValue = floor(scaled);
				float3 f = scaled - baseValue;
				float lo = 0.5 - softness;
				float hi = 0.5 + softness;
				float3 s = smoothstep(lo, hi, f);
				return (baseValue + s) / steps;
			}

			float Hash21(float2 p)
			{
				p = frac(p * float2(123.34, 456.21));
				p += dot(p, p + 45.32);
				return frac(p.x * p.y);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);
				if (_AlicePaintingIntensity <= 0.0001)
					return original;

				float2 pixel = max(_BlitTexture_TexelSize.xy, float2(0.000001, 0.000001));
				float2 screenPos = input.uv / pixel;
				float2 handOffset = float2(
					sin(screenPos.y * 0.071 + sin(screenPos.x * 0.017)),
					cos(screenPos.x * 0.063 + cos(screenPos.y * 0.019))
				) * pixel * _Wobble;
				float2 uv = input.uv + handOffset;
				float2 d = pixel * _BrushSize;

				float3 c  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
				float3 n  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, d.y)).rgb;
				float3 s  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, d.y)).rgb;
				float3 e  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(d.x, 0)).rgb;
				float3 w  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(d.x, 0)).rgb;
				float3 ne = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d).rgb;
				float3 nw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-d.x, d.y)).rgb;
				float3 se = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(d.x, -d.y)).rgb;
				float3 sw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - d).rgb;

				// 明暗差の大きい色を混ぜすぎない、簡易的なエッジ保持ブラシ。
				float centerLum = GetLuminance(c);
				float4 cardLum = float4(GetLuminance(n), GetLuminance(s), GetLuminance(e), GetLuminance(w));
				float4 diagLum = float4(GetLuminance(ne), GetLuminance(nw), GetLuminance(se), GetLuminance(sw));
				float4 cardWeight = exp(-abs(cardLum - centerLum) * 7.0);
				float4 diagWeight = exp(-abs(diagLum - centerLum) * 7.0) * 0.7;
				float weightSum = 1.0 + dot(cardWeight, float4(1.0, 1.0, 1.0, 1.0))
				                      + dot(diagWeight, float4(1.0, 1.0, 1.0, 1.0));
				float3 brushed = (
					c +
					n * cardWeight.x + s * cardWeight.y + e * cardWeight.z + w * cardWeight.w +
					ne * diagWeight.x + nw * diagWeight.y + se * diagWeight.z + sw * diagWeight.w
				) / max(weightSum, 0.001);
				float3 color = lerp(c, brushed, _BrushStrength);

				float lum = GetLuminance(color);
				float maxC = max(color.r, max(color.g, color.b));
				float minC = min(color.r, min(color.g, color.b));
				float chroma = maxC - minC;

				color = lerp(lum.xxx, color, _Saturation);
				color = lerp(color, color * (1.0 + chroma), saturate(_Vibrance - 1.0));
				color = ((color - 0.5) * _Contrast) + 0.5;
				color = pow(saturate(color), 1.0 / max(_Gamma, 0.001));
				color = max(color, _BlackLift.xxx);
				color = color / (1.0 + _WhiteSoftClip * color);
				color = ApplySoftPosterize(color, max(_PosterizeSteps, 2.0), _PosterizeSoftness);

				float tone = pow(saturate(GetLuminance(color)), _RampSharpness);
				float shadowW = 1.0 - smoothstep(_TonePivot - 0.35, _TonePivot - 0.02, tone);
				float highlightW = smoothstep(_TonePivot + 0.02, _TonePivot + 0.35, tone);
				float midW = saturate(1.0 - shadowW - highlightW);

				float3 tint = shadowW * _ShadowTint.rgb + midW * _MidTint.rgb + highlightW * _HighlightTint.rgb;
				float3 finalColor = color * tint;

				// Sobel 輪郭を墨線として重ねる。
				float edgeX = (diagLum.x + 2.0 * cardLum.z + diagLum.z)
				            - (diagLum.y + 2.0 * cardLum.w + diagLum.w);
				float edgeY = (diagLum.y + 2.0 * cardLum.x + diagLum.x)
				            - (diagLum.w + 2.0 * cardLum.y + diagLum.z);
				float edge = length(float2(edgeX, edgeY));
				edge = smoothstep(_EdgeThreshold, _EdgeThreshold + 0.22, edge) * _EdgeStrength;
				finalColor *= 1.0 - saturate(edge) * 0.72;

				// 画面に固定された細粒と、大きめの繊維ムラを合わせて紙の質感を作る。
				float fineGrain = Hash21(floor(screenPos)) - 0.5;
				float coarseGrain = Hash21(floor(screenPos / 5.0)) - 0.5;
				float grain = fineGrain * 0.65 + coarseGrain * 0.35;
				finalColor += grain * _PaperGrain * (0.45 + 0.55 * GetLuminance(finalColor));

				finalColor = saturate(finalColor);
				return half4(lerp(original.rgb, finalColor, _AlicePaintingIntensity), original.a);
			}
			ENDHLSL
		}
	}
}
