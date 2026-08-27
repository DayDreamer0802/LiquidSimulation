Shader "Rouge/Tech Arena Backdrop"
{
    Properties
    {
        _BaseColor("Inner Field", Color) = (0.025, 0.06, 0.095, 1)
        _OuterColor("Outer Field", Color) = (0.009, 0.023, 0.042, 1)
        _GridColor("Circuit Grid", Color) = (0.075, 0.42, 0.58, 1)
        [HDR] _AccentColor("Energy Accent", Color) = (0.08, 0.82, 1.2, 1)
        _ArenaCenter("Arena Center", Vector) = (0, 0, 0, 0)
        _BackdropHalfSize("Backdrop Half Size", Float) = 1024
        _GridSize("Grid Size", Float) = 8
        _LineIntensity("Line Intensity", Range(0, 2)) = 0.72
        _AnimationSpeed("Animation Speed", Range(0, 1)) = 0.16
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry-100"
            "RenderType"="Opaque"
        }

        Pass
        {
            Name "Tech Backdrop"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _OuterColor;
                float4 _GridColor;
                float4 _AccentColor;
                float4 _ArenaCenter;
                float _BackdropHalfSize;
                float _GridSize;
                float _LineIntensity;
                float _AnimationSpeed;
            CBUFFER_END

            float _RougeTechDetailStrength;
            float _RougeTechAnimationStrength;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float GridLine(float2 uv, float normalizedWidth)
            {
                float2 cell = frac(uv);
                float2 distanceToLine = min(cell, 1.0 - cell);
                float2 feather = max(fwidth(uv), float2(0.0005, 0.0005));
                float2 lineMask = 1.0 - smoothstep(normalizedWidth, normalizedWidth + feather,
                    distanceToLine);
                return max(lineMask.x, lineMask.y);
            }

            float Band(float value, float center, float halfWidth)
            {
                float feather = max(fwidth(value) * 1.3, 0.002);
                return 1.0 - smoothstep(halfWidth, halfWidth + feather, abs(value - center));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 arenaOffset = input.positionWS.xz - _ArenaCenter.xy;
                float radialDistance = length(arenaOffset);
                float maxAxisDistance = max(abs(arenaOffset.x), abs(arenaOffset.y));
                float safeGridSize = max(_GridSize, 0.1);
                float detailQuality = saturate(_RougeTechDetailStrength);
                float animationQuality = saturate(_RougeTechAnimationStrength);

                float innerField = exp2(-radialDistance * 0.0045);
                half3 color = lerp(_OuterColor.rgb, _BaseColor.rgb,
                    0.34 + innerField * 0.66);

                // Keep the field alive all the way to the camera clear color, while
                // progressively removing fine lines before they can shimmer at distance.
                float backdropFade = 1.0 - smoothstep(
                    _BackdropHalfSize * 0.72, _BackdropHalfSize * 0.98, maxAxisDistance);
                float detailFade = 1.0 - smoothstep(190.0, 680.0, radialDistance);

                float2 minorUv = arenaOffset / safeGridSize;
                float minorGrid = GridLine(minorUv, 0.010) * detailFade;
                float majorGrid = GridLine(minorUv * 0.25, 0.014);
                float2 diagonalUv = float2(arenaOffset.x + arenaOffset.y,
                    arenaOffset.x - arenaOffset.y) /
                    (safeGridSize * 8.0);
                float diagonalGrid = GridLine(diagonalUv, 0.008) * detailFade;

                float2 circuitUv = arenaOffset / (safeGridSize * 2.0);
                float2 circuitCell = floor(circuitUv);
                float2 circuitLocal = frac(circuitUv);
                float cellSeed = Hash21(circuitCell + 17.13);
                float orientation = step(0.5, Hash21(circuitCell + 4.71));
                float lane = lerp(0.28, 0.72, step(0.5, Hash21(circuitCell + 9.37)));
                float horizontalTrace = Band(circuitLocal.y, lane, 0.010) *
                    step(0.12, circuitLocal.x) * step(circuitLocal.x, 0.88);
                float verticalTrace = Band(circuitLocal.x, lane, 0.010) *
                    step(0.12, circuitLocal.y) * step(circuitLocal.y, 0.88);
                float traceGate = step(0.74, cellSeed);
                float circuitTrace = lerp(horizontalTrace, verticalTrace, orientation) * traceGate;

                float2 nodeCenter = lerp(float2(0.5, lane), float2(lane, 0.5), orientation);
                float nodeDistance = length(circuitLocal - nodeCenter);
                float nodeFeather = max(fwidth(nodeDistance), 0.002);
                float circuitNode = (1.0 - smoothstep(0.035, 0.035 + nodeFeather,
                    nodeDistance)) * traceGate;

                float travel = lerp(circuitLocal.x, circuitLocal.y, orientation);
                float packetPosition = frac(cellSeed + _Time.y * _AnimationSpeed * 0.18 *
                    animationQuality);
                float packetDistance = abs(frac(travel - packetPosition + 0.5) - 0.5);
                float dataPacket = (1.0 - smoothstep(0.025, 0.09, packetDistance)) *
                    circuitTrace * animationQuality;

                float scanCoordinate = arenaOffset.x + arenaOffset.y * 0.55;
                float scanPhase = frac(scanCoordinate / 260.0 -
                    _Time.y * _AnimationSpeed * 0.025 * animationQuality);
                float scanDistance = abs(scanPhase - 0.5);
                float scan = (1.0 - smoothstep(0.015, 0.09, scanDistance)) *
                    detailFade;

                half3 gridEnergy = _GridColor.rgb * _LineIntensity;
                color += gridEnergy * (minorGrid * (0.045 + detailQuality * 0.025) +
                    majorGrid * 0.14 + diagonalGrid * detailQuality * 0.045);
                color += _GridColor.rgb * circuitTrace * detailFade *
                    detailQuality * 0.16;
                color += _AccentColor.rgb * circuitNode * detailFade *
                    detailQuality * 0.16;
                color += _AccentColor.rgb * dataPacket * detailFade * 0.22;

                color += _GridColor.rgb * scan * 0.055 * detailQuality;

                color = lerp(_OuterColor.rgb, color, backdropFade);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
