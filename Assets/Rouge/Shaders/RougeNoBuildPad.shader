Shader "Rouge/No-Build Pad"
{
    Properties
    {
        _BaseColor("Neutral Base", Color) = (0.25, 0.27, 0.29, 1)
        _PanelColor("Inset Panel", Color) = (0.14, 0.15, 0.16, 1)
        _MarkColor("No Build Mark", Color) = (0.46, 0.48, 0.50, 1)
        _CellSize("Terrain Cell Size", Float) = 8
        _GridOrigin("Grid Origin", Vector) = (0, 0, 0, 0)
        _FrameWidth("Frame Width", Range(0.005, 0.12)) = 0.026
        _MarkStrength("Mark Strength", Range(0, 1)) = 0.72
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Name "Forward No Build Pad"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                float4 _MarkColor;
                float4 _GridOrigin;
                float _CellSize;
                float _FrameWidth;
                float _MarkStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float Band(float value, float halfWidth, float feather)
            {
                return 1.0 - smoothstep(halfWidth, halfWidth + feather,
                    abs(value));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float safeCellSize = max(_CellSize, 0.001);
                float2 gridPosition =
                    (input.positionWS.xz - _GridOrigin.xy) / safeCellSize;
                float2 uv = frac(gridPosition);
                float2 footprint = abs(ddx(gridPosition)) +
                                   abs(ddy(gridPosition));
                float feather = max(max(footprint.x, footprint.y), 0.0015);
                float2 edgeDistance = min(uv, 1.0 - uv);
                float borderDistance = min(edgeDistance.x, edgeDistance.y);
                float topFace = smoothstep(0.45, 0.82, input.normalWS.y);

                float outerFrame = 1.0 - smoothstep(_FrameWidth,
                    _FrameWidth + feather, borderDistance);
                float insetFrame = 1.0 - smoothstep(0.012, 0.026 + feather,
                    abs(borderDistance - 0.105));
                float innerPanel = smoothstep(0.12, 0.15 + feather,
                    borderDistance);

                // A restrained diagonal cross reads as "not a deployment node"
                // from any camera angle without introducing another icon texture.
                float2 centered = uv - 0.5;
                float crossA = Band(centered.x - centered.y, 0.028, feather);
                float crossB = Band(centered.x + centered.y, 0.028, feather);
                float crossBounds = 1.0 - smoothstep(0.31, 0.39,
                    max(abs(centered.x), abs(centered.y)));
                float noBuildMark = saturate(crossA + crossB) * crossBounds;

                float2 microCell = floor(uv * 8.0);
                float checker = fmod(microCell.x + microCell.y, 2.0);
                float brushed = 0.5 + 0.5 * sin(
                    input.positionWS.x * 5.7 + input.positionWS.z * 0.31);
                half3 panel = lerp(_PanelColor.rgb, _BaseColor.rgb, 0.46);
                panel *= 0.965 + checker * 0.018 + brushed * 0.018;

                half3 color = lerp(_BaseColor.rgb, panel, innerPanel);
                color = lerp(color, _PanelColor.rgb, outerFrame * 0.78);
                color = lerp(color, _MarkColor.rgb, insetFrame * 0.32);
                color = lerp(color, _MarkColor.rgb,
                    noBuildMark * _MarkStrength);
                color *= lerp(0.55, 1.0, topFace);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
