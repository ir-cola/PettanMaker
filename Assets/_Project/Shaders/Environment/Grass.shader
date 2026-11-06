// =========================================================================
// URP 対応 草インスタンシングシェーダー
//
// 機能概要:
//   - DrawMeshInstancedIndirect による GPU インスタンシング描画
//   - クロスクワッドメッシュ（2枚の板ポリを十字に組み合わせた草の形）
//   - GrassInstanceData バッファから位置・姿勢・スケールを受け取る
//   - 風アニメーション（根元は固定、先端のみ揺れる）
//   - 踏み倒し暗化（DarknessBuffer により草が暗くなる）
//   - ACES ではなく単純なライティング + フォグ
// =========================================================================
Shader "Custom/URP/Grass"
{
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}          // 草のアルファマスク用テクスチャ
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.35       // アルファ切り抜き閾値
        _BottomColor ("Bottom Color", Color) = (0.08, 0.26, 0.06, 1) // 根元の色
        _TopColor ("Top Color", Color) = (0.45, 0.9, 0.22, 1)        // 先端の色
        _WindStrength ("Wind Strength", Range(0, 0.5)) = 0.08   // 風の揺れ幅
        _WindSpeed ("Wind Speed", Range(0, 10)) = 2.0            // 風の速さ
        _WindFrequency ("Wind Frequency", Range(0, 10)) = 2.0   // 風の空間周波数
        _WindDirection ("Wind Direction", Vector) = (1, 0, 0, 0) // 風の向き（XZ平面）
        _Ambient ("Ambient", Range(0, 1)) = 0.45                // 環境光の最低輝度
        _DarknessStrength ("Darkness Strength", Range(0, 1)) = 0.4 // 踏み倒し時の暗化強度
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"           // アルファテスト描画キュー
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off    // 草は両面描画（裏面カリングなし）
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                uint   instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
                nointerpolation float darkness : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // UnityPerMaterial: マテリアルごとのプロパティを1つの定数バッファにまとめる
            // → GPU 側でバッファ切り替えを最小化しパフォーマンスを向上させる
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float  _AlphaClip;
                float4 _BottomColor;
                float4 _TopColor;
                float  _WindStrength;
                float  _WindSpeed;
                float  _WindFrequency;
                float4 _WindDirection;
                float  _Ambient;
                float  _DarknessStrength;
            CBUFFER_END

            // コンパクトなインスタンスデータ (36B/blade)
            struct GrassInstance
            {
                float4 posAndWidth; // xyz=position, w=width
                float4 quaternion;
                float  height;
            };

            StructuredBuffer<GrassInstance> _InstanceData;
            StructuredBuffer<float>         _DarknessBuffer;

            // クォータニオン → 回転行列
            float3x3 QuatToMatrix(float4 q)
            {
                float x = q.x, y = q.y, z = q.z, w = q.w;
                return float3x3(
                    1 - 2*(y*y + z*z),  2*(x*y - w*z),      2*(x*z + w*y),
                    2*(x*y + w*z),      1 - 2*(x*x + z*z),  2*(y*z - w*x),
                    2*(x*z - w*y),      2*(y*z + w*x),      1 - 2*(x*x + y*y)
                );
            }

            Varyings vert(Attributes input)
            {
                GrassInstance inst = _InstanceData[input.instanceID];
                float3 pos    = inst.posAndWidth.xyz;
                float  width  = inst.posAndWidth.w;
                float  height = inst.height;

                float3x3 rot = QuatToMatrix(inst.quaternion);

                // スケール適用後に回転してワールド座標へ
                float3 scaledOS   = float3(input.positionOS.x * width,
                                           input.positionOS.y * height,
                                           input.positionOS.z * width);
                float3 positionWS = mul(rot, scaledOS) + pos;

                // 風アニメーション: UV.y で根元（0）は揺れず先端（1）だけ揺れる
                float2 windDir   = normalize(_WindDirection.xz);
                float  windPhase = dot(positionWS.xz, windDir) * _WindFrequency + _Time.y * _WindSpeed;
                positionWS.xz   += windDir * sin(windPhase) * _WindStrength * input.uv.y;

                // 法線変換: 非均一スケール時は R * S^{-1} * n
                float3 invScale = float3(1.0 / width, 1.0 / height, 1.0 / width);
                float3 normalWS = normalize(mul(rot, input.normalOS * invScale));

                Varyings output;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.normalWS    = normalWS;
                output.uv          = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor   = ComputeFogFactor(output.positionHCS.z);
                output.darkness    = _DarknessBuffer[input.instanceID];
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // テクスチャサンプル → グレースケール輝度をアルファマスクとして使用
                // BT.601 輝度係数でテクスチャのアルファ形状を取り出す
                half4 tex  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half  mask = dot(tex.rgb, half3(0.299h, 0.587h, 0.114h)) * tex.a;
                clip(mask - _AlphaClip); // マスク値が閾値未満なら描画しない（アルファクリップ）

                // ライティング: N・L が _Ambient を下回らないように clamp することで
                // 日陰でも草が真っ暗にならない最低輝度を保証する
                half3 normalWS  = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half  ndotl     = saturate(dot(normalWS, mainLight.direction));
                half  lighting  = max(ndotl, _Ambient);

                // 根元（UV.y=0）→先端（UV.y=1）へ色をグラデーション
                half3 color = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(input.uv.y));
                color *= mainLight.color * lighting;
                // 踏み倒し量（darkness）に応じて草を暗化する
                color *= 1.0h - (half)input.darkness * _DarknessStrength;
                color  = MixFog(color, input.fogFactor); // シーンのフォグを合成

                return half4(color, mask);
            }
            ENDHLSL
        }
    }
}
