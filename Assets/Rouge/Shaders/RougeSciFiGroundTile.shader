Shader "Rouge/Sci-Fi Ground Tile"
{
    Properties
    {
        _BaseColor("Base Metal", Color) = (0.16, 0.24, 0.3, 1)
        _PanelColor("Panel Tint", Color) = (0.12, 0.2, 0.27, 1)
        [HDR] _AccentColor("Circuit Accent", Color) = (0.08, 0.48, 0.58, 1)
        _CellSize("Terrain Cell Size", Float) = 8
        _GridOrigin("Grid Origin", Vector) = (0, 0, 0, 0)
        _SeamWidth("Seam Width", Range(0.004, 0.08)) = 0.018
        _DetailStrength("Detail Strength", Range(0, 1)) = 0.42
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Name "Forward Tech Ground"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PanelColor;
                float4 _AccentColor;
                float4 _GridOrigin;
                float _CellSize;
                float _SeamWidth;
                float _DetailStrength;
            CBUFFER_END

            float _RougeVisualQuality;
            float _RougeLightingStrength;
            float _RougeTechDetailStrength;
            float4 _RougeLightDirection;
            float4 _RougeLightColor;

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float Band(float value, float center, float halfWidth, float feather)
            {
                return 1.0 - smoothstep(halfWidth, halfWidth + feather, abs(value - center));
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float safeCellSize = max(_CellSize, 0.001);
                float2 gridPosition = (input.positionWS.xz - _GridOrigin.xy) / safeCellSize;
                float2 footprint = abs(ddx(gridPosition)) + abs(ddy(gridPosition));
                float2 localUv = frac(gridPosition);
                float2 tileId = floor(gridPosition);
                float2 edgeDistance = min(localUv, 1.0 - localUv);
                float feather = max(max(footprint.x, footprint.y), 0.0015);
                float topFace = smoothstep(0.45, 0.8, input.normalWS.y);
                float materialDetail = saturate(_DetailStrength *
                    lerp(0.65, 1.0, saturate(_RougeTechDetailStrength)));

                float variation = Hash21(tileId) - 0.5;
                float macroVariation = Hash21(floor(tileId * 0.25) + 19.7) - 0.5;
                half3 baseMetal = lerp(_BaseColor.rgb, _PanelColor.rgb,
                    0.17 + variation * 0.032);
                baseMetal *= 1.0 + macroVariation * 0.016;

                float outerSeam = max(
                    1.0 - smoothstep(_SeamWidth, _SeamWidth + feather, edgeDistance.x),
                    1.0 - smoothstep(_SeamWidth, _SeamWidth + feather, edgeDistance.y));
                float bevelX = Band(edgeDistance.x, 0.045, 0.011, feather);
                float bevelY = Band(edgeDistance.y, 0.045, 0.011, feather);
                float bevel = max(bevelX, bevelY);
                float insetRimX = Band(edgeDistance.x, 0.105, 0.010, feather);
                float insetRimY = Band(edgeDistance.y, 0.105, 0.010, feather);
                float insetRim = max(insetRimX, insetRimY);
                float innerPanel = smoothstep(0.112 - feather, 0.112 + feather,
                    edgeDistance.x) * smoothstep(0.112 - feather, 0.112 + feather,
                    edgeDistance.y);

                float centerVertical = Band(localUv.x, 0.5, 0.008, feather) *
                    step(0.16, localUv.y) * step(localUv.y, 0.84);
                float centerHorizontal = Band(localUv.y, 0.5, 0.008, feather) *
                    step(0.16, localUv.x) * step(localUv.x, 0.84);
                float centerGap = 1.0 - smoothstep(0.09, 0.14, length(localUv - 0.5));
                float panelSeam = saturate(centerVertical + centerHorizontal) * (1.0 - centerGap);

                float2 cornerUv = min(localUv, 1.0 - localUv);
                float boltDistance = length(cornerUv - float2(0.105, 0.105));
                float boltCore = 1.0 - smoothstep(0.012, 0.023 + feather, boltDistance);
                float traceA = Band(localUv.y, 0.22, 0.008, feather) *
                    step(0.18, localUv.x) * step(localUv.x, 0.34);
                float traceB = Band(localUv.x, 0.78, 0.008, feather) *
                    step(0.61, localUv.y) * step(localUv.y, 0.78);
                float traceHaloA = Band(localUv.y, 0.22, 0.028, feather) *
                    step(0.16, localUv.x) * step(localUv.x, 0.36);
                float traceHaloB = Band(localUv.x, 0.78, 0.028, feather) *
                    step(0.59, localUv.y) * step(localUv.y, 0.80);
                // A few precise inlays are enough to imply circuitry. Keeping them
                // sparse and continuous avoids the dirty, broken-line look at distance.
                float circuitCell = step(0.88, Hash21(tileId + 31.7));
                float circuitTrace = (traceA + traceB) * circuitCell;
                float circuitHalo = saturate(traceHaloA + traceHaloB) * circuitCell;
                float boltHalo = 1.0 - smoothstep(0.022, 0.065 + feather, boltDistance);

                // A cheap analytic bevel normal makes a flat cube top react to the
                // scene's directional light without a normal map or extra texture read.
                float sideX = lerp(-1.0, 1.0, step(0.5, localUv.x));
                float sideZ = lerp(-1.0, 1.0, step(0.5, localUv.y));
                float slopeX = (bevelX * 0.45 - insetRimX * 0.18) * sideX;
                float slopeZ = (bevelY * 0.45 - insetRimY * 0.18) * sideZ;
                float3 fakeNormal = normalize(float3(slopeX, 1.0, slopeZ));
                float3 lightDirection = normalize(_RougeLightDirection.xyz);
                float directional = saturate(dot(fakeNormal, lightDirection));
                float sunlight = smoothstep(0.24, 0.88, directional);
                float3 viewDirection = SafeNormalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfDirection = SafeNormalize(lightDirection + viewDirection);
                float metalSpecular = pow(saturate(dot(fakeNormal, halfDirection)), 38.0);
                float grazing = pow(1.0 - saturate(dot(fakeNormal, viewDirection)), 3.0);
                float fakeLighting = lerp(0.82, 1.10, sunlight);
                fakeLighting = lerp(1.0, fakeLighting, _RougeLightingStrength * topFace);

                half realtimeShadow = 1.0h;
                if (_RougeVisualQuality > 0.5)
                {
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                    realtimeShadow = mainLight.shadowAttenuation;
                }
                float shadowLighting = lerp(1.0, 0.70 + realtimeShadow * 0.30,
                    _RougeLightingStrength * topFace);
                half sunPeak = max(max(_RougeLightColor.r, _RougeLightColor.g),
                    max(_RougeLightColor.b, 0.001h));
                half3 normalizedSun = _RougeLightColor.rgb / sunPeak;
                half3 coolAmbient = half3(0.88h, 0.94h, 1.04h);
                half3 lightTint = lerp(coolAmbient, normalizedSun, sunlight);
                lightTint = lerp(1.0h.xxx, lightTint,
                    (half)(_RougeLightingStrength * 0.28 * topFace));
                half directEnergy = lerp(0.96h, clamp(sunPeak, 0.82h, 1.20h),
                    (half)(sunlight * _RougeLightingStrength * topFace));
                lightTint *= directEnergy;
                float cavity = saturate(outerSeam * 0.35 + panelSeam * 0.10);
                float wellInterior = smoothstep(0.085, 0.135,
                    min(edgeDistance.x, edgeDistance.y));
                float brushedPhase = input.positionWS.x * 7.13 +
                    input.positionWS.z * 0.37;
                float brushedVisibility = saturate(1.0 - fwidth(brushedPhase) * 0.5);
                float brushedMetal = 0.5 + sin(brushedPhase) * 0.5 * brushedVisibility;

                half3 color = baseMetal * fakeLighting * shadowLighting * lightTint;
                color *= 0.986 + brushedMetal * 0.028 * materialDetail * topFace;
                color *= 1.0 - cavity * (0.12 + _RougeLightingStrength * 0.06) * topFace;
                color = lerp(color, _PanelColor.rgb * 0.72, outerSeam * 0.64 * topFace);
                color += _BaseColor.rgb * bevel * (0.10 + directional * 0.08) * topFace;
                color *= 1.0 - wellInterior *
                    (0.025 + materialDetail * 0.035) * topFace;
                color = lerp(color, _PanelColor.rgb * 0.91,
                    innerPanel * 0.065 * materialDetail * topFace);
                color += normalizedSun * insetRim *
                    (0.025 + directional * 0.045) * materialDetail * topFace;
                color = lerp(color, _PanelColor.rgb * 0.76,
                    panelSeam * 0.24 * materialDetail * topFace);
                color = lerp(color, _PanelColor.rgb * 0.78, boltCore * 0.24 * topFace);
                half3 specularTint = lerp(_BaseColor.rgb, _AccentColor.rgb, 0.10);
                color += specularTint * metalSpecular * directional * shadowLighting *
                    (0.16 + materialDetail * 0.14) *
                    _RougeLightingStrength * topFace;
                color += _BaseColor.rgb * grazing * bevel * 0.07 * topFace;
                color += _AccentColor.rgb * boltHalo * 0.025 * materialDetail * topFace;
                color += _AccentColor.rgb * boltCore * 0.15 * materialDetail * topFace;
                color += _AccentColor.rgb * circuitHalo * 0.022 * materialDetail * topFace;
                color += _AccentColor.rgb * circuitTrace * 0.16 * materialDetail * topFace;

                color = lerp(color * 0.72, color, topFace);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
