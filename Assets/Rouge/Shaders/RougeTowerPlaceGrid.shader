Shader "Rouge/Tower Place Grid"
{
    Properties
    {
        _BaseColor("Hologram Fill", Color) = (0.01,0.08,0.12,0.035)
        _LineColor("Energy Rail", Color) = (0.32,0.84,0.92,0.82)
        _CellSize("Cell Size", Float) = 8
        _LineWidth("Rail Width", Range(0.002, 0.12)) = 0.03
        _InnerRailDistance("Inner Rail Distance", Range(0.02, 0.2)) = 0.085
        _FlowSpeed("Flow Speed", Range(0, 5)) = 1.2
        [HideInInspector] _GridOrigin("Grid Origin", Vector) = (0,0,0,0)
        [HideInInspector] _UseVertexColor("Use Vertex Color", Float) = 0
        [HideInInspector] _OwnershipHighlight("Ownership Highlight", Float) = 0
        [HideInInspector] _CornerScalePulse("Corner Scale Pulse", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LineColor;
                float4 _GridOrigin;
                float _CellSize;
                float _LineWidth;
                float _InnerRailDistance;
                float _FlowSpeed;
                float _UseVertexColor;
                float _OwnershipHighlight;
                float _CornerScalePulse;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 gridPosition = (output.positionWS.xz - _GridOrigin.xy) /
                    max(_CellSize, 0.001);
                float2 cellCenter = _GridOrigin.xy +
                    (floor(gridPosition) + 0.5) * _CellSize;
                float scaleWave = 0.91 + 0.09 *
                    (0.5 + 0.5 * sin(_Time.y * max(0.1, _FlowSpeed) * 3.0));
                float2 pulsedPosition = cellCenter +
                    (output.positionWS.xz - cellCenter) * scaleWave;
                output.positionWS.xz = lerp(output.positionWS.xz, pulsedPosition,
                    step(0.5, _CornerScalePulse));
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Free buildable surfaces intentionally have no cell rails or crosses.
                // Only the corner geometry authored by the placement overlay is visible.
                float pulse = 0.98 + 0.02 * sin(_Time.y * max(0.1, _FlowSpeed) * 2.4);
                half4 freeGrid = half4(_BaseColor.rgb * pulse, _BaseColor.a);

                // Footprint geometry is inset from the real grid in C#, so this layer
                // is only a translucent status panel and never redraws/obscures lines.
                half invalidState = step(input.color.g * 2.5, input.color.r);
                float statePulse = lerp(
                    1.04 + 0.08 * sin(_Time.y * max(0.1, _FlowSpeed) * 2.8),
                    1.02,
                    invalidState);
                half3 stateRgb = input.color.rgb * statePulse;
                stateRgb = lerp(stateRgb, half3(1.0, 0.12, 0.085), invalidState);
                half stateAlpha = saturate(input.color.a * lerp(0.68, 0.74, invalidState));
                half4 footprintGrid = half4(stateRgb, stateAlpha);
                half4 gridResult = lerp(freeGrid, footprintGrid, step(0.5, _UseVertexColor));

                float ownershipWave = 0.5 + 0.5 * sin(
                    _Time.y * max(0.1, _FlowSpeed) * 3.0);
                half3 ownershipRgb = input.color.rgb * (1.03 + ownershipWave * 0.2);
                half ownershipAlpha = saturate(input.color.a * (0.7 + ownershipWave * 0.22));
                half4 ownershipGrid = half4(ownershipRgb, ownershipAlpha);
                return lerp(gridResult, ownershipGrid, step(0.5, _OwnershipHighlight));
            }
            ENDHLSL
        }
    }
}
