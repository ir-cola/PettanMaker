Shader "_Project/PostEffects/Fog"
{
	Properties
	{
		_FogColor ("Fog Color", Color) = (0.72, 0.78, 0.86, 1)
		_FogStart ("Fog Start", Float) = 10
		_FogEnd ("Fog End", Float) = 40
		_FogStrength ("Fog Strength", Range(0, 2)) = 1
		_HeightFogBase ("Height Fog Base", Float) = 0
		_HeightFogFalloff ("Height Fog Falloff", Range(0.001, 2)) = 0.08
		_HeightFogStrength ("Height Fog Strength", Range(0, 2)) = 0.7
		_VolumetricScattering ("Volumetric Scattering", Range(0, 2)) = 0.6
		_VolumetricAnisotropy ("Volumetric Anisotropy", Range(0, 0.95)) = 0.55
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
				float4 _FogColor;
				float _FogStart;
				float _FogEnd;
				float _FogStrength;
				float _HeightFogBase;
				float _HeightFogFalloff;
				float _HeightFogStrength;
				float _VolumetricScattering;
				float _VolumetricAnisotropy;
				float4 _LightDirection;
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

			float MiePhase(float cosTheta, float g)
			{
				float g2 = g * g;
				float denom = 1.0 + g2 - 2.0 * g * cosTheta;
				return (1.0 - g2) / max(4.0 * PI * pow(max(denom, 0.0001), 1.5), 0.0001);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.uv);

				float rawDepth = SampleSceneDepth(input.uv);
				// 深度が書かれていないピクセル (空ドーム/アウトライン等) はフォグ対象外。
				// 逆Zプラットフォーム (D3D/Metal/Vulkan) では「未書き込み=0」なので
				// 0.999999 との比較では空を除外できず、空が全面フォグ色に塗られていた。
				#if UNITY_REVERSED_Z
				if (rawDepth <= 0.000001)
				{
					return src;
				}
				#else
				if (rawDepth >= 0.999999)
				{
					return src;
				}
				#endif

				float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
				float baseFog = saturate((depth - _FogStart) / max(_FogEnd - _FogStart, 0.0001));
				baseFog = pow(baseFog, 1.2) * _FogStrength;

				float3 worldPos = ComputeWorldSpacePosition(input.uv, rawDepth, UNITY_MATRIX_I_VP);
				float heightFactor = saturate(exp(-max(worldPos.y - _HeightFogBase, 0.0) * _HeightFogFalloff));
				float heightFog = heightFactor * (1.0 - exp(-depth * 0.02)) * _HeightFogStrength;

				float3 viewDir = normalize(worldPos - _WorldSpaceCameraPos);
				float3 lightDir = normalize(_LightDirection.xyz);
				float sunForward = saturate(dot(-viewDir, lightDir));
				float mie = MiePhase(sunForward, saturate(_VolumetricAnisotropy));
				float volumetric = saturate(mie * _VolumetricScattering * baseFog * 0.35);

				float fogFactor = saturate(baseFog + heightFog + volumetric);
				float3 c = lerp(src.rgb, _FogColor.rgb, fogFactor);
				return half4(c, src.a);
			}
			ENDHLSL
		}
	}
}
