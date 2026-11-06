Shader "Custom/Hologram"
{
    Properties
    {
        _MainTex            ("Card Texture",            2D)             = "white" {}
        _AlphaCutoff        ("Alpha Cutoff",            Range(0, 1))    = 0.01

        [Header(Diffraction Grating)]
        _GratingPitch       ("Grating Pitch (nm)",      Range(300, 1500)) = 550.0
        _GratingScale       ("Region Scale",            Range(1, 20))   = 6.0
        _HoloIntensity      ("Holo Intensity",          Range(0, 4))    = 2.5
        _BandWidth          ("Visible Band Width",      Range(0.05, 0.5)) = 0.2

        [Header(Micro Glitter)]
        _GlitterScale       ("Glitter Scale",           Range(10, 200)) = 80.0
        _GlitterIntensity   ("Glitter Intensity",       Range(0, 4))    = 1.5
        _GlitterSharpness   ("Glitter Sharpness",       Range(1, 64))   = 20.0

        [Header(Glitter Wave)]
        _GlitterInterval    ("Glitter Interval (sec)",  Range(0.2, 5))  = 1.8
        _GlitterDuration    ("Glitter Duration",        Range(0.01, 1)) = 0.3
        _WaveIntensity      ("Wave Intensity",          Range(0, 3))    = 1.5
        _GlitterSweepWidth  ("Sweep Width",             Range(0, 1))    = 0.4

        [Header(Surface)]
        _RimPower           ("Rim Power",               Range(0.5, 8))  = 3.0
        _RimIntensity       ("Rim Intensity",           Range(0, 2))    = 0.6
        _SpecPower          ("Specular Power",          Range(1, 256))  = 120.0
        _SpecIntensity      ("Specular Intensity",      Range(0, 3))    = 1.2
        _MetallicBase       ("Metallic Base",           Range(0, 1))    = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "TransparentCutout"
            "Queue"          = "AlphaTest"
        }

        Pass
        {
            Name "HologramCard"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ------------------------------------------------
            // Structs
            // ------------------------------------------------
            struct VS_INPUT
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct PS_INPUT
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            // ------------------------------------------------
            // Uniforms
            // ------------------------------------------------
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _AlphaCutoff;
                float  _GratingPitch;
                float  _GratingScale;
                float  _HoloIntensity;
                float  _BandWidth;
                float  _GlitterScale;
                float  _GlitterIntensity;
                float  _GlitterSharpness;
                float  _GlitterInterval;
                float  _GlitterDuration;
                float  _WaveIntensity;
                float  _GlitterSweepWidth;
                float  _RimPower;
                float  _RimIntensity;
                float  _SpecPower;
                float  _SpecIntensity;
                float  _MetallicBase;
            CBUFFER_END

            // ------------------------------------------------
            // Helpers
            // ------------------------------------------------
            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float3 spectralRGB(float t)
            {
                float hue = lerp(0.78, 0.0, saturate(t));
                float3 p  = abs(frac(hue.xxx + float3(0.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
                return saturate(p - 1.0);
            }

            void voronoi(float2 p, float cellScale,
                         out float2 id0, out float2 id1,
                         out float  dist0, out float dist1)
            {
                float2 base = floor(p * cellScale);
                dist0 = 1e9; dist1 = 1e9;
                id0 = id1 = float2(0, 0);
                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        float2 nid  = base + float2(x, y);
                        float2 seed = nid + float2(hash21(nid), hash21(nid + float2(0.0, 7.3))) * 0.7 + 0.15;
                        float  d    = length(p * cellScale - seed);
                        if (d < dist0) { dist1 = dist0; id1 = id0; dist0 = d; id0 = nid; }
                        else if (d < dist1) { dist1 = d; id1 = nid; }
                    }
                }
            }

            float2 diffractionLambda(float2 Vt, float cellRnd, float pitchScale)
            {
                float  angle      = cellRnd * 3.14159 * 2.0;
                float2 gratingDir = float2(cos(angle), sin(angle));
                float  sinTheta   = dot(Vt, gratingDir);
                float  lambda_nm  = _GratingPitch * pitchScale * sinTheta;
                float  lambda01   = (lambda_nm - 380.0) / 320.0;
                float  inBand     = smoothstep(0.0, _BandWidth, lambda01)
                                  * smoothstep(1.0, 1.0 - _BandWidth, lambda01);
                inBand = inBand * inBand;
                return float2(lambda01, inBand);
            }

            // ------------------------------------------------
            // Vertex
            // ------------------------------------------------
            PS_INPUT vert(VS_INPUT input)
            {
                PS_INPUT o;
                float3 posWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.uv         = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                o.positionWS = posWS;
                return o;
            }

            // ------------------------------------------------
            // Fragment
            // ------------------------------------------------
            float4 frag(PS_INPUT input) : SV_Target
            {
                float3 N  = normalize(input.normalWS);
                float3 V  = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float2 uv = input.uv;
                float  t  = _Time.y;

                float NdotV = saturate(dot(N, V));

                float4 baseTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                clip(baseTex.a - _AlphaCutoff);
                float mask = baseTex.a;

                float3 up      = abs(N.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 tangent = normalize(cross(up, N));
                float3 bitan   = cross(N, tangent);
                float2 Vt      = float2(dot(V, tangent), dot(V, bitan));

                float2 id0, id1;
                float  gd0, gd1;
                voronoi(uv, _GratingScale, id0, id1, gd0, gd1);

                float cellRnd = hash21(id0 + id1 * float2(3.7, 7.1));
                float2 diff   = diffractionLambda(Vt, cellRnd, 1.0);

                float camHash    = frac(dot(floor(V * 5.0), float3(127.1, 311.7, 74.7)));
                float activeMask = step(0.5, frac(cellRnd + camHash));

                float edgeDist  = gd1 - gd0;
                float edgeGlint = smoothstep(0.0, 0.06, edgeDist)
                                * (1.0 - smoothstep(0.06, 0.18, edgeDist))
                                * NdotV;

                float3 diffractionCol  = spectralRGB(diff.x) * diff.y * activeMask * _HoloIntensity;
                diffractionCol += spectralRGB(diff.x) * edgeGlint * _HoloIntensity * 0.3;

                float2 gid0, gid1;
                float  ggd0, ggd1;
                voronoi(uv, _GlitterScale, gid0, gid1, ggd0, ggd1);

                float  gCellRnd    = hash21(gid0 + gid1 * float2(5.1, 2.9));
                float2 gDiff       = diffractionLambda(Vt, gCellRnd, 0.65);
                float  gSpec       = pow(gDiff.y, _GlitterSharpness) * NdotV;
                float3 microGlitter = spectralRGB(gDiff.x) * gSpec * _GlitterIntensity;

                float  metallic  = smoothstep(0.2, 0.8, NdotV);
                float  baseLum   = _MetallicBase + metallic * (1.0 - _MetallicBase);
                float3 baseColor = float3(baseLum, baseLum, baseLum);

                float  fresnel  = pow(1.0 - NdotV, _RimPower) * _RimIntensity;
                float3 rimCol   = diffractionCol * fresnel;

                float  spec    = pow(NdotV, _SpecPower);
                float3 specCol = lerp(float3(1, 1, 1), diffractionCol, 0.4) * spec * _SpecIntensity;

                float lightFactor  = NdotV * (0.3 + 0.7 * pow(NdotV, _SpecPower * 0.3));
                float cycleT       = fmod(t, _GlitterInterval) / _GlitterInterval;
                float waveFired    = step(cycleT, _GlitterDuration / _GlitterInterval);
                float sweepPos     = cycleT / (_GlitterDuration / _GlitterInterval);
                float sweepLine    = (uv.x + uv.y) * 0.5;
                float sweepMask    = pow(1.0 - saturate(abs(sweepLine - sweepPos) / _GlitterSweepWidth), 2.0);
                float3 waveGlitter = diffractionCol * sweepMask * waveFired * _WaveIntensity * lightFactor;

                float3 holoLayer = diffractionCol + microGlitter;
                float3 col       = baseTex.rgb * (baseColor + holoLayer);
                col += rimCol      * mask;
                col += specCol     * mask;
                col += waveGlitter * mask;

                return float4(col, mask);
            }
            ENDHLSL
        }
    }
}
