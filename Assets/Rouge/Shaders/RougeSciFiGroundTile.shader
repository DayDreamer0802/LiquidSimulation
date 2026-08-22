Shader "Rouge/Sci-Fi Ground Tile"
{
    Properties
    {
        _BaseColor("Base Metal", Color) = (0.16, 0.24, 0.3, 1)
        _PanelColor("Panel Tint", Color) = (0.12, 0.2, 0.27, 1)
        _AccentColor("Circuit Accent", Color) = (0.08, 0.48, 0.58, 1)
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
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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

            Varyings Vert(Attributes input)
            {
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
                float2 localUv = frac(gridPosition);
                float2 tileId = floor(gridPosition);
                float2 edgeDistance = min(localUv, 1.0 - localUv);
                float feather = max(fwidth(localUv.x) + fwidth(localUv.y), 0.0015);
                float topFace = smoothstep(0.45, 0.8, input.normalWS.y);

                float variation = Hash21(tileId) - 0.5;
                float finePattern = (frac(floor(localUv.x * 12.0) +
                    floor(localUv.y * 12.0)) - 0.5) * 0.014;
                half3 baseMetal = lerp(_BaseColor.rgb, _PanelColor.rgb,
                    0.18 + variation * 0.08);
                baseMetal *= 1.0 + finePattern;

                // Strong outer seam plus a narrow bevel makes adjacent terrain cells
                // read as assembled metal plates without looking like a build grid.
                float outerSeam = max(
                    1.0 - smoothstep(_SeamWidth, _SeamWidth + feather, edgeDistance.x),
                    1.0 - smoothstep(_SeamWidth, _SeamWidth + feather, edgeDistance.y));
                float bevel = max(Band(edgeDistance.x, 0.045, 0.009, feather),
                                  Band(edgeDistance.y, 0.045, 0.009, feather));

                // Four large sub-panels, interrupted around the center so the floor
                // stays readable instead of becoming another dense square grid.
                float centerVertical = Band(localUv.x, 0.5, 0.008, feather) *
                    step(0.16, localUv.y) * step(localUv.y, 0.84);
                float centerHorizontal = Band(localUv.y, 0.5, 0.008, feather) *
                    step(0.16, localUv.x) * step(localUv.x, 0.84);
                float centerGap = 1.0 - smoothstep(0.09, 0.14, length(localUv - 0.5));
                float panelSeam = saturate(centerVertical + centerHorizontal) *
                    (1.0 - centerGap);

                // Four recessed bolts and a few asymmetric circuit traces sell the
                // sci-fi panel construction while remaining deliberately subtle.
                float2 cornerUv = min(localUv, 1.0 - localUv);
                float boltDistance = length(cornerUv - float2(0.105, 0.105));
                float boltOuter = 1.0 - smoothstep(0.034, 0.052 + feather, boltDistance);
                float boltCore = 1.0 - smoothstep(0.012, 0.023 + feather, boltDistance);
                float traceA = Band(localUv.y, 0.22, 0.008, feather) *
                    step(0.18, localUv.x) * step(localUv.x, 0.34);
                float traceB = Band(localUv.x, 0.78, 0.008, feather) *
                    step(0.61, localUv.y) * step(localUv.y, 0.78);
                float traceBreaks = smoothstep(0.15, 0.75,
                    sin((localUv.x + localUv.y) * 58.0) * 0.5 + 0.5);
                float circuitTrace = (traceA + traceB) * traceBreaks;

                half3 color = baseMetal;
                color = lerp(color, _PanelColor.rgb * 0.42, outerSeam * 0.86 * topFace);
                color += _BaseColor.rgb * bevel * 0.18 * topFace;
                color = lerp(color, _PanelColor.rgb * 0.62,
                    panelSeam * 0.42 * _DetailStrength * topFace);
                color = lerp(color, _PanelColor.rgb * 0.3, boltOuter * 0.82 * topFace);
                color += _AccentColor.rgb * boltCore * 0.28 * _DetailStrength * topFace;
                color += _AccentColor.rgb * circuitTrace * 0.2 * _DetailStrength * topFace;
                color = lerp(color * 0.7, color, topFace);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
