Shader "_Project/PostEffects/LensFlare"
{
	Properties
	{
		_Threshold ("Threshold", Range(0, 5)) = 1.2
		_Intensity ("Intensity", Range(0, 5)) = 0.9
		_HaloSize ("Halo Size", Range(0.2, 4)) = 1.4
		_GhostCount ("Ghost Count", Range(0, 6)) = 4
		_GhostSpacing ("Ghost Spacing", Range(0.2, 1.8)) = 1.0
		_ChromaticAberration ("Chromatic Aberration", Range(0, 0.02)) = 0.004
		_StreakIntensity ("Streak Intensity", Range(0, 2)) = 0.45
		_SunRayIntensity ("Sun Ray Intensity", Range(0, 3)) = 1.0
		_SunRayCount ("Sun Ray Count", Range(4, 24)) = 10
		_SunRaySharpness ("Sun Ray Sharpness", Range(1, 12)) = 6
		_EdgeFade ("Edge Fade", Range(0.5, 8)) = 2.5
		_RainbowStrength ("Rainbow Strength", Range(0, 2)) = 1.0
		_LightScreenPos ("Light Screen Pos", Vector) = (0.5, 0.5, 0, 0)
		_OcclusionRadius ("Occlusion Sample Radius", Range(0.001, 0.02)) = 0.005
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
			TEXTURE2D_X(_CameraDepthTexture);

			CBUFFER_START(UnityPerMaterial)
				float _Threshold;
				float _Intensity;
				float _HaloSize;
				float _GhostCount;
				float _GhostSpacing;
				float _ChromaticAberration;
				float _StreakIntensity;
				float _SunRayIntensity;
				float _SunRayCount;
				float _SunRaySharpness;
				float _EdgeFade;
				float _RainbowStrength;
				float4 _LightScreenPos;
				float _OcclusionRadius;
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

			float3 Spectral(float t)
			{
				t = frac(t);
				float r = saturate(1.0 - abs(t * 6.0 - 3.0));
				float g = saturate(1.0 - abs(t * 6.0 - 2.0));
				float b = saturate(1.0 - abs(t * 6.0 - 1.0));
				return normalize(float3(r, g, b) + 1e-4);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float2 center = _LightScreenPos.xy;
				float2 toLight = center - input.uv;
				float distToLight = length(toLight);
				float2 radialDir = toLight / max(distToLight, 1e-4);

				float3 src = SampleColor(input.uv);
				float3 lightSample = SampleColor(saturate(center));
				float lightLum = dot(lightSample, float3(0.299, 0.587, 0.114));

				float screenMask = step(0.0, _LightScreenPos.z) * step(0.0, _LightScreenPos.w);
				float thresholdMask = saturate((lightLum - _Threshold) / max(_Threshold, 0.0001));

				// 深度バッファでオクルージョン判定（Directional Light は無限遠 = far plane = 深度0）
				// 周囲5点をサンプリングして端に引っかかったときのカクつきを和らげる
				static const float2 occOffsets[5] = {
					float2( 0,  0),
					float2( 1,  1), float2(-1,  1),
					float2( 1, -1), float2(-1, -1)
				};
				float occlusionSum = 0.0;
				[unroll]
				for (int oi = 0; oi < 5; oi++)
				{
					float2 sampleUV = saturate(center + occOffsets[oi] * _OcclusionRadius);
					float d = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_LinearClamp, sampleUV).r;
					// Reversed-Z: 0=far(sky)  /  Standard-Z: 1=far(sky)
					#if UNITY_REVERSED_Z
						occlusionSum += step(d, 0.0001);
					#else
						occlusionSum += step(0.9999, d);
					#endif
				}
				float occlusionMask = occlusionSum / 5.0;

				float centerDist = distance(center, float2(0.5, 0.5));
				float edgeMask = pow(saturate(1.0 - centerDist * 1.6), _EdgeFade);
				float flareMask = screenMask * edgeMask;

				float haloCore = saturate(1.0 - distToLight / max(_HaloSize, 0.001));
				haloCore *= haloCore;
				float haloRing = exp(-abs(distToLight - _HaloSize * 0.22) * 22.0);
				float halo = haloCore + haloRing * 0.6;

				float3 flare = 0.0;
				int ghostCount = (int)round(clamp(_GhostCount, 0.0, 6.0));
				[loop]
				for (int i = 0; i < ghostCount; i++)
				{
					float t = ((float)i + 1.0) / (ghostCount + 1.0);
					float2 ghostPos = lerp(center, 1.0 - center, t * _GhostSpacing);
					float ghostDist = distance(input.uv, ghostPos);
					float ghostShape = exp(-ghostDist * ghostDist * (90.0 + t * 120.0));

					float2 caOffset = radialDir * (_ChromaticAberration * (0.5 + t));
					float3 ghost;
					ghost.r = SampleColor(saturate(ghostPos + caOffset)).r;
					ghost.g = SampleColor(saturate(ghostPos)).g;
					ghost.b = SampleColor(saturate(ghostPos - caOffset)).b;

					float3 spectral = Spectral(t * 0.9 + distToLight * 0.35);
					float3 spectralTint = lerp(1.0.xxx, spectral, saturate(_RainbowStrength));
					float ghostLum = dot(ghost, float3(0.299, 0.587, 0.114));
					flare += ghost * ghostLum * ghostShape * spectralTint * (1.0 - t * 0.7);
				}

				float anamorphic = exp(-abs(toLight.y) * 40.0) * exp(-distToLight * 3.5);
				float streak = anamorphic * _StreakIntensity;

				float angle = atan2(toLight.y, toLight.x);
				float normalizedAngle = angle / (2.0 * PI);
				float rayPattern = abs(cos(normalizedAngle * _SunRayCount * 2.0 * PI));
				rayPattern = pow(rayPattern, _SunRaySharpness);
				float rayRadialFade = exp(-distToLight * 7.0) * saturate(1.0 - distToLight / max(_HaloSize * 1.3, 0.001));
				float sunRays = rayPattern * rayRadialFade * _SunRayIntensity;

				float3 haloSpectral = Spectral(distToLight * 2.8 + centerDist * 0.7);
				float3 streakSpectral = Spectral(input.uv.x * 0.8 + center.x * 0.6);
				float3 raySpectral = Spectral(normalizedAngle * 0.35 + distToLight * 1.8);
				float3 haloTint = lerp(1.0.xxx, haloSpectral, saturate(_RainbowStrength));
				float3 streakTint = lerp(1.0.xxx, streakSpectral, saturate(_RainbowStrength));
				float3 rayTint = lerp(1.0.xxx, raySpectral, saturate(_RainbowStrength));
				float3 flareColor = flare + halo.xxx * 0.65 * haloTint + streak.xxx * streakTint + sunRays.xxx * rayTint;
				float finalMask = flareMask * thresholdMask * occlusionMask;
				float3 result = src + flareColor * finalMask * _Intensity;
				return half4(result, 1.0);
			}
			ENDHLSL
		}
	}
}
