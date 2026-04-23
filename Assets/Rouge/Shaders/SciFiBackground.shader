Shader "Rouge/CosmicFloor"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.03, 0.03, 0.06, 1)
        _NebulaColor("Nebula Color", Color) = (0.20, 0.10, 0.30, 1)
        _HighlightColor("Highlight Color", Color) = (0.24, 0.44, 0.82, 1)
        _StarColor("Star Color", Color) = (1.0, 0.97, 1.0, 1)
        _Tiling("Tiling", Float) = 0.08
        _NebulaScale("Nebula Scale", Float) = 1.25
        _StarDensity("Star Density", Range(0.0, 1.0)) = 0.16
        _StarSize("Star Size", Range(4.0, 28.0)) = 14.0
        _FlowSpeed("Flow Speed", Float) = 0.08
        _EmissionStrength("Emission Strength", Range(0.0, 2.0)) = 0.55
        _SheenStrength("Sheen Strength", Range(0.0, 2.0)) = 0.35
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _NebulaColor;
            float4 _HighlightColor;
            float4 _StarColor;
            float _Tiling;
            float _NebulaScale;
            float _StarDensity;
            float _StarSize;
            float _FlowSpeed;
            float _EmissionStrength;
            float _SheenStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float Noise2D(float2 p)
            {
                float2 cell = floor(p);
                float2 fracPart = frac(p);
                float2 smoothFrac = fracPart * fracPart * (3.0 - 2.0 * fracPart);

                float a = Hash12(cell);
                float b = Hash12(cell + float2(1.0, 0.0));
                float c = Hash12(cell + float2(0.0, 1.0));
                float d = Hash12(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, smoothFrac.x), lerp(c, d, smoothFrac.x), smoothFrac.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                value += Noise2D(p) * amplitude;
                p = p * 2.02 + 17.13;
                amplitude *= 0.5;
                value += Noise2D(p) * amplitude;
                p = p * 2.11 + 11.71;
                amplitude *= 0.5;
                value += Noise2D(p) * amplitude;
                p = p * 2.37 + 5.19;
                amplitude *= 0.5;
                value += Noise2D(p) * amplitude;
                return value;
            }

            float StarLayer(float2 uv, float density, float size, float time)
            {
                float2 scaled = uv * size;
                float2 cell = floor(scaled);
                float2 local = frac(scaled) - 0.5;
                float seed = Hash12(cell);
                float enabled = step(1.0 - density, seed);
                float2 offset = (Hash22(cell) - 0.5) * 0.62;
                float dist = length(local - offset);
                float twinkle = 0.72 + 0.28 * sin(time * (1.2 + seed * 2.8) + seed * 6.2831853);
                return enabled * twinkle * smoothstep(0.12, 0.0, dist);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionWS.xz * _Tiling;
                float time = _Time.y * _FlowSpeed;
                float2 driftUv = uv + float2(time * 0.75, -time * 0.45);

                float nebula = Fbm(driftUv * _NebulaScale);
                float nebulaDetail = Fbm(driftUv * (_NebulaScale * 2.2) + 19.7);
                float swirl = 0.5 + 0.5 * sin((uv.x + uv.y) * 2.3 + nebula * 5.2 - time * 3.0);

                float stars = StarLayer(uv * 0.9 + float2(time * 0.12, 0.0), _StarDensity, _StarSize, _Time.y);
                stars += StarLayer(uv * 1.7 - float2(0.0, time * 0.08), _StarDensity * 0.45, _StarSize * 1.8, _Time.y * 0.8) * 0.6;

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float3 lightDir = normalize(float3(0.35, 0.9, 0.25));
                float ndotl = saturate(dot(normalWS, lightDir));
                float shade = 0.35 + ndotl * 0.45;
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 4.0);
                float ring = 0.5 + 0.5 * sin(length(uv) * 5.0 - time * 2.0 + nebulaDetail * 3.0);

                float3 nebulaTint = lerp(_NebulaColor.rgb * 0.75, _HighlightColor.rgb, saturate(nebulaDetail * 0.7 + swirl * 0.3));
                float3 color = _BaseColor.rgb;
                color += nebulaTint * pow(saturate(nebula), 1.7) * 0.72;
                color += _HighlightColor.rgb * saturate(nebulaDetail - 0.42) * 0.18;
                color += _HighlightColor.rgb * smoothstep(0.82, 1.0, ring) * 0.05;
                color += _StarColor.rgb * stars * (0.85 + _EmissionStrength * 0.45);
                color = color * shade + nebulaTint * fresnel * _SheenStrength * 0.22;
                color += color * _EmissionStrength * 0.08;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}