Shader "_Project/PostEffects/OcclusionWaterBlur"
{
	// カメラオクルージョン透過 (OccludablePolka) の水玉と周囲を「水面越し」に見せる
	// ポストエフェクト。遮蔽窓の内側で、カメラ→プレイヤーの円柱
	// (_OccludeLineStart/_OccludeLineEnd/_OccludeAutoRadius, CameraOcclusionSystem が
	// 毎フレーム設定) の近くにある「プレイヤーより手前の」ピクセルとその周囲だけを:
	//   1. 同心円の波紋で屈折 (UV を揺らす)
	//   2. ポアソン12タップでぼかす (すりガラス)
	//   3. 虹色のシマーとほのかな水色ティント
	//   4. 波頭のスパークル
	// で装飾する。円柱に何も入っていなければ完全に素通し (常時ボケは出ない)。
	Properties
	{
		_Strength       ("Effect Strength", Range(0, 1)) = 1.0
		_BlurRadius     ("Blur Radius (px)", Range(0, 32)) = 12
		_RippleScale    ("Ripple Wavelength (px)", Float) = 90
		_RippleSpeed    ("Ripple Speed", Float) = 2.0
		_RippleStrength ("Ripple Distort (px)", Range(0, 24)) = 7
		_TintColor      ("Water Tint", Color) = (0.78, 0.93, 1.0, 1)
		_TintAmount     ("Tint Amount", Range(0, 1)) = 0.22
		_IridAmount     ("Iridescence", Range(0, 1)) = 0.25
		_SparkleStrength("Sparkle", Range(0, 2)) = 0.5
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		Pass
		{
			Name "OcclusionWaterBlur"
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
				float  _Strength;
				float  _BlurRadius;
				float  _RippleScale;
				float  _RippleSpeed;
				float  _RippleStrength;
				float4 _TintColor;
				float  _TintAmount;
				float  _IridAmount;
				float  _SparkleStrength;
			CBUFFER_END

			// CameraOcclusionSystem が設定するグローバル (OccludablePolka と共有)
			float4 _OccludeHoleCenter;   // プレイヤーの正規化スクリーン座標 (0..1, 原点=左下)
			float  _OccludeHoleRadius;   // 窓半径 (画面高さ比)。0 なら無効
			float4 _OccludeHoleShape;    // 窓（楕円）形状: x=横の広がり, y=縦の広がり, z=下げ量(半径比)
			float3 _OccludeLineStart;    // カメラ位置 (WS)
			float3 _OccludeLineEnd;      // プレイヤー胴体位置 (WS)
			float  _OccludeAutoRadius;   // 円柱半径 (m)。0 なら無効

			struct Attributes { uint vertexID : SV_VertexID; };
			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0;
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

			// x = カメラ→プレイヤー円柱への所属度 (0=無関係, 1=完全に遮蔽位置)。
			//     OccludablePolka の AutoOccludeFade と同じ形で、少し外側 (×1.35) まで
			//     含めることで「穴の周囲」もぼかし対象にする。
			// y = 「視線の奥 (プレイヤー付近かそれ以遠)」を見ているか。
			//     水玉で実際に抜けているかの証拠判定に使う。
			float2 CylProbe(float3 positionWS)
			{
				float3 ab  = _OccludeLineEnd - _OccludeLineStart;
				float  len = max(length(ab), 1e-4);
				float3 dir = ab / len;
				float  t   = dot(positionWS - _OccludeLineStart, dir);

				float through = smoothstep(len - 0.9, len - 0.4, t);

				float axial = smoothstep(0.05, 0.4, t) * (1.0 - smoothstep(len - 0.8, len - 0.35, t));
				float radial = length(positionWS - (_OccludeLineStart + dir * t));
				float mask = axial * (1.0 - smoothstep(_OccludeAutoRadius * 0.8, _OccludeAutoRadius * 1.35, radial));
				return float2(mask, through);
			}

			// uv の深度からワールド位置を復元して CylProbe する
			float2 CylProbeUV(float2 uv)
			{
				float raw = SampleSceneDepth(uv);
				return CylProbe(ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP));
			}

			// ポアソン風 12 タップ (内リング4 + 外リング8)
			static const float2 kTaps[12] =
			{
				float2( 0.354,  0.354), float2(-0.354,  0.354), float2(-0.354, -0.354), float2( 0.354, -0.354),
				float2( 1.0,    0.0  ), float2( 0.707,  0.707), float2( 0.0,    1.0  ), float2(-0.707,  0.707),
				float2(-1.0,    0.0  ), float2(-0.707, -0.707), float2( 0.0,   -1.0  ), float2( 0.707, -0.707)
			};

			half4 Frag(Varyings input) : SV_Target
			{
				float3 src = SampleColor(input.uv);

				// オクルージョンシステム停止中は素通し
				if (_OccludeAutoRadius <= 0.001 || _OccludeHoleRadius <= 0.0001)
					return half4(src, 1.0);

				// 遮蔽窓マスク (OccludablePolka と同じ縦長楕円)。
				// 窓の中心はプレイヤーの実スクリーン位置へ合わせる (PolkaClip と揃える)。
				// 楕円倍率 1.35 は OccludablePolka.shader の PolkaClip と必ず揃えること。
				float  radiusPx = _OccludeHoleRadius * _ScreenParams.y;
				float2 px     = input.uv * _ScreenParams.xy;                    // Vert の 1-uv.y 反転で左上原点 (y+ が下)
				// 中心 = 正規化スクリーン座標 (0..1, 原点=左下) を px 復元。y は px と揃えて反転。
				float2 center;
				center.x = _OccludeHoleCenter.x * _ScreenParams.x;
				center.y = (1.0 - _OccludeHoleCenter.y) * _ScreenParams.y;
				center.y += radiusPx * _OccludeHoleShape.z;                     // 窓の下げ量 (PolkaClip と揃える)
				// 楕円の縦横は _OccludeHoleShape.xy (OccludablePolka.shader の PolkaClip と同値)
				float2 toC  = (px - center) / float2(_OccludeHoleShape.x, _OccludeHoleShape.y);
				float  dpx  = length(toC);
				float  window = smoothstep(1.0, 0.55, dpx / (radiusPx * 1.15));
				if (window <= 0.003)
					return half4(src, 1.0);

				// 同心円の波紋で UV を屈折させる (中心ほど強い)
				float2 dirC   = toC / max(dpx, 1e-3);
				float  phase  = dpx / max(_RippleScale, 1.0) * 6.2831 - _Time.y * _RippleSpeed;
				float  ring   = sin(phase);
				float  ring2  = sin(dpx / max(_RippleScale * 0.53, 1.0) * 6.2831
				                    - _Time.y * _RippleSpeed * 1.37
				                    + atan2(toC.y, toC.x) * 3.0);
				float2 texel  = 1.0 / _ScreenParams.xy;
				float2 uvBase = input.uv + dirC * (ring * 0.75 + ring2 * 0.45) * _RippleStrength * window * texel;

				// 円柱近傍の遮蔽ピクセルを収集しながらぼかす
				float  radiusUV = _BlurRadius * window;
				float3 sum      = 0.0;
				float  coverage = 0.0;

				float2 cpCenter = CylProbeUV(uvBase);
				sum      += SampleColor(uvBase);
				coverage += cpCenter.x;

				[unroll]
				for (int i = 0; i < 12; i++)
				{
					float2 tuv = uvBase + kTaps[i] * radiusUV * texel;
					coverage  += CylProbeUV(tuv).x;
					sum       += SampleColor(tuv);
				}

				float3 blurred = sum / 13.0;
				coverage       = coverage / 13.0;

				// 広域プローブ: 開口部の周囲に「円柱内の遮蔽ピクセル」があり、かつ
				// 自ピクセルとの中間点で「奥が見えている」ものだけを有効票とする。
				// 水玉で実際に抜けている壁 (許可マテリアル) の周囲だけが票を集め、
				// _OccludeAllow=0 のベタ壁 (フェンス等) は中間点も壁のままなので
				// 票が入らず、ぼかしもフィルムも発火しない。
				float validProbe = 0.0;
				[unroll]
				for (int j = 0; j < 6; j++)
				{
					float  a    = j * 1.0472;               // 60度刻み
					float2 dir2 = float2(cos(a), sin(a));
					float2 puv1 = input.uv + dir2 * (radiusPx * 0.55) * texel;
					float2 puv2 = input.uv - dir2 * (radiusPx * 0.80) * texel;
					validProbe += CylProbeUV(puv1).x * CylProbeUV((input.uv + puv1) * 0.5).y;
					validProbe += CylProbeUV(puv2).x * CylProbeUV((input.uv + puv2) * 0.5).y;
				}
				// 「実際に抜けている」証拠。ベタ壁なら 0
				float evidence = saturate(validProbe / 12.0 * 4.0);

				float blurMask = saturate(window * coverage * 1.6) * evidence * _Strength;
				float film     = window * evidence * max(cpCenter.y, saturate(coverage * 1.6))
				               * _TintAmount * _Strength;
				if (blurMask <= 0.003 && film <= 0.003)
					return half4(src, 1.0);

				// 1) ぼかし: 水玉とその周囲 (すりガラス)
				float3 result = lerp(src, blurred, blurMask);

				// 2) 色フィルム: 開口部全体にうっすら色を乗せ「透かして見ている」感を出す
				float3 glass = result * lerp(float3(1.0, 1.0, 1.0), _TintColor.rgb, 0.85)
				             + _TintColor.rgb * 0.08;
				result = lerp(result, glass, saturate(film));

				// 3) オプション装飾 (現在は 0 で無効): 虹色シマーと波頭スパークル
				if (_IridAmount > 0.001 || _SparkleStrength > 0.001)
				{
					float3 irid = 0.5 + 0.5 * cos(phase * 0.5 + float3(0.0, 2.094, 4.188));
					result = lerp(result, result * (0.85 + 0.35 * irid), _IridAmount * blurMask);
					float crest = pow(saturate(ring * 0.5 + 0.5), 12.0) * _SparkleStrength;
					result += crest * blurMask * _TintColor.rgb;
				}

				return half4(result, 1.0);
			}
			ENDHLSL
		}
	}
}
