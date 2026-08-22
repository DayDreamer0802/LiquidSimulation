Shader "Rouge/Tower Placement Pad"
{
    Properties
    {
        _BaseColor("Metal Base", Color) = (0.07, 0.13, 0.18, 1)
        _AccentColor("Energy Accent", Color) = (0.08, 0.82, 1, 1)
        _CellSize("Terrain Cell Size", Float) = 8
        _GridOrigin("Grid Origin", Vector) = (0, 0, 0, 0)
        _FrameWidth("Frame Width", Range(0.005, 0.15)) = 0.035
        _GlowStrength("Glow Strength", Range(0, 4)) = 1.8
        _PulseSpeed("Pulse Speed", Range(0, 4)) = 0.8
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
                float4 _AccentColor;
                float4 _GridOrigin;
                float _CellSize;
                float _FrameWidth;
                float _GlowStrength;
                float _PulseSpeed;
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

            half4 Frag(Varyings input) : SV_Target
            {
                float safeCellSize = max(_CellSize, 0.001);
                float2 gridPosition = (input.positionWS.xz - _GridOrigin.xy) / safeCellSize;
                float2 uv = frac(gridPosition);
                float2 edgeDistance = min(uv, 1.0 - uv);
                float feather = max(fwidth(uv.x) + fwidth(uv.y), 0.0015);
                float topFace = smoothstep(0.45, 0.8, input.normalWS.y);

                // Recessed metal panel with a subtle brushed checker pattern.
                float checker = frac(floor(uv.x * 8.0) + floor(uv.y * 8.0)) * 0.012;
                float centerShade = saturate(length(uv - 0.5) * 1.15);
                half3 metal = _BaseColor.rgb * (0.94 + checker + centerShade * 0.18);
                metal = lerp(metal * 0.72, metal, topFace);

                // Outer energy rail and a dimmer inset rail make the pad read as machinery.
                float outerFrame = max(
                    1.0 - smoothstep(_FrameWidth, _FrameWidth + feather, edgeDistance.x),
                    1.0 - smoothstep(_FrameWidth, _FrameWidth + feather, edgeDistance.y));
                float insetFrame = max(Band(edgeDistance.x, 0.105, _FrameWidth * 0.45, feather),
                                       Band(edgeDistance.y, 0.105, _FrameWidth * 0.45, feather));

                // Corner power nodes and short circuit traces break up the flat square silhouette.
                float2 cornerDistance = min(uv, 1.0 - uv);
                float cornerNode = 1.0 - smoothstep(0.045, 0.075 + feather,
                    length(cornerDistance - float2(0.115, 0.115)));
                float traceMask = step(0.11, edgeDistance.y) * step(edgeDistance.y, 0.16) *
                                  step(0.18, uv.x) * step(uv.x, 0.38);
                traceMask += step(0.11, edgeDistance.x) * step(edgeDistance.x, 0.16) *
                             step(0.62, uv.y) * step(uv.y, 0.82);
                float traceDash = smoothstep(0.35, 0.65,
                    sin((uv.x + uv.y) * 72.0) * 0.5 + 0.5) * saturate(traceMask);

                float cellSeed = frac(sin(dot(floor(gridPosition), float2(12.9898, 78.233))) * 43758.5453);
                float pulse = 0.76 + 0.24 * sin(_Time.y * _PulseSpeed * 6.28318 + cellSeed * 6.28318);
                float energy = saturate(outerFrame + insetFrame * 0.48 + cornerNode + traceDash * 0.55) * topFace;
                half3 accent = _AccentColor.rgb * (0.82 + _GlowStrength * pulse);
                half3 color = metal + accent * energy;

                // A faint central reactor glow keeps large pads from looking empty.
                float reactor = 1.0 - smoothstep(0.0, 0.28, length(uv - 0.5));
                color += _AccentColor.rgb * reactor * 0.11 * pulse * topFace;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
