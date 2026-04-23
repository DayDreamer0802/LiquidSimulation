Shader "Rouge/CosmicFloor"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.02, 0.05, 0.12, 1)
        _NebulaColorA("Nebula Color A", Color) = (0.12, 0.28, 0.62, 1)
        _NebulaColorB("Nebula Color B", Color) = (0.38, 0.58, 1.0, 1)
        _GridColor("Grid Color", Color) = (0.22, 0.62, 1.0, 1)
        _AccentColor("Accent Color", Color) = (0.72, 0.9, 1.0, 1)
        _GridScale("Grid Scale", Float) = 1.6
        _MajorGridEvery("Major Grid Every", Float) = 6
        _LineWidth("Line Width", Range(0.001, 0.08)) = 0.02
        _MajorLineWidth("Major Line Width", Range(0.001, 0.12)) = 0.04
        _NebulaScale("Nebula Scale", Float) = 0.08
        _NebulaStrength("Nebula Strength", Range(0, 2)) = 0.38
        _NebulaSpeed("Nebula Speed", Float) = 0.08
        _StarDensity("Star Density", Float) = 10
        _StarBrightness("Star Brightness", Range(0, 4)) = 0.6
        _ScanStrength("Scan Strength", Range(0, 2)) = 0.55
        _CenterGlow("Center Glow", Range(0, 4)) = 0.75
        _VignetteStrength("Vignette Strength", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "Forward"
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
            float4 _NebulaColorA;
            float4 _NebulaColorB;
            float4 _GridColor;
            float4 _AccentColor;
            float _GridScale;
            float _MajorGridEvery;
            float _LineWidth;
            float _MajorLineWidth;
            float _NebulaScale;
            float _NebulaStrength;
            float _NebulaSpeed;
            float _StarDensity;
            float _StarBrightness;
            float _ScanStrength;
            float _CenterGlow;
            float _VignetteStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float2 smooth = local * local * (3.0 - 2.0 * local);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));

                return lerp(lerp(a, b, smooth.x), lerp(c, d, smooth.x), smooth.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                value += amplitude * Noise(p); p = p * 2.02 + 13.1; amplitude *= 0.5;
                value += amplitude * Noise(p); p = p * 2.03 + 7.7; amplitude *= 0.5;
                value += amplitude * Noise(p); p = p * 2.01 + 19.3; amplitude *= 0.5;
                value += amplitude * Noise(p);

                return value;
            }

            float Ring(float distanceToCenter, float radius, float width)
            {
                float d = abs(distanceToCenter - radius);
                return 1.0 - smoothstep(width, width * 2.2, d);
            }

            float GridLine(float2 uv, float width)
            {
                float2 grid = abs(frac(uv) - 0.5) / max(fwidth(uv), 1e-4);
                float dist = min(grid.x, grid.y);
                return 1.0 - saturate((dist - width) / max(width, 1e-4));
            }

            float StarField(float2 uv)
            {
                float2 scaled = uv * _StarDensity;
                float2 cell = floor(scaled);
                float2 local = frac(scaled) - 0.5;
                float2 jitter = float2(Hash21(cell), Hash21(cell + 19.7)) - 0.5;
                float radius = length(local - jitter * 0.38);
                float gate = step(0.985, Hash21(cell + 3.17));
                float star = gate * pow(saturate(1.0 - radius * 5.0), 14.0);
                return star * (0.45 + Hash21(cell + 8.1));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(positionInputs.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 worldXZ = input.positionWS.xz * _GridScale;
                float time = _Time.y;

                float2 nebulaUvA = input.positionWS.xz * _NebulaScale + float2(time * _NebulaSpeed, -time * _NebulaSpeed * 0.35);
                float2 nebulaUvB = input.positionWS.zx * (_NebulaScale * 1.7) + float2(-time * _NebulaSpeed * 0.5, time * _NebulaSpeed * 0.22);
                float nebulaA = Fbm(nebulaUvA);
                float nebulaB = Fbm(nebulaUvB);
                float nebulaField = saturate((nebulaA * 0.75 + nebulaB * 0.45 - 0.42) * 1.9 + 0.5);
                float nebulaVein = saturate((nebulaA - nebulaB) * 0.65 + 0.45);
                float nebulaMask = saturate(pow(nebulaField, 2.6) * 0.8 + pow(nebulaVein, 4.0) * 0.12);

                float minorGrid = GridLine(worldXZ, _LineWidth);
                float2 majorUv = worldXZ / max(_MajorGridEvery, 1.0);
                float majorGrid = GridLine(majorUv, _MajorLineWidth);
                float macroGrid = GridLine(input.positionWS.xz * 0.055, 0.045);

                float stars = StarField(input.positionWS.xz * 0.22 + float2(time * 0.01, 0.0));
                stars += StarField(input.positionWS.xz * 0.37 - float2(time * 0.015, time * 0.01)) * 0.65;
                stars *= 0.45 + smoothstep(25.0, 90.0, length(input.positionWS.xz));

                float radial = length(input.positionWS.xz);
                float centerDisc = exp(-radial * 0.18) * _CenterGlow;
                float centerRing = Ring(radial, 6.0, 0.18) * 0.45 * _CenterGlow;
                float outerRing = Ring(radial, 10.5, 0.22) * 0.22 * _CenterGlow;
                float axisX = 1.0 - smoothstep(0.015, 0.09, abs(input.positionWS.x));
                float axisZ = 1.0 - smoothstep(0.015, 0.09, abs(input.positionWS.z));
                float angle = atan2(input.positionWS.z, input.positionWS.x);
                float spokeMask = pow(saturate(cos(angle * 4.0) * 0.5 + 0.5), 18.0);
                float spokes = spokeMask * smoothstep(1.4, 3.5, radial) * (1.0 - smoothstep(9.5, 12.0, radial)) * 0.22;

                float beamX = pow(saturate(sin(input.positionWS.x * 0.085 + time * 0.22) * 0.5 + 0.5), 14.0);
                float beamZ = pow(saturate(cos(input.positionWS.z * 0.075 - time * 0.18) * 0.5 + 0.5), 16.0);
                float beams = (beamX * 0.16 + beamZ * 0.12) * _ScanStrength;

                float fresnel = pow(1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS))), 2.8);
                float vignette = saturate(1.0 - radial * 0.009 * _VignetteStrength);

                float3 nebulaColor = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, saturate(nebulaB * 1.2));
                float3 color = _BaseColor.rgb;
                color += nebulaColor * nebulaMask * _NebulaStrength;
                color += _GridColor.rgb * (minorGrid * 0.42 + majorGrid * 1.25 + macroGrid * 0.1);
                color += _AccentColor.rgb * (stars * _StarBrightness + centerDisc + centerRing + outerRing + (axisX + axisZ) * 0.18 + spokes + beams + fresnel * 0.16);
                color *= vignette;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}