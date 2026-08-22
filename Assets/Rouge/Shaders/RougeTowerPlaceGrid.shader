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
        [HideInInspector] _UseVertexColor("Use Vertex Color", Float) = 0
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
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 gridPosition = (input.positionWS.xz - _GridOrigin.xy) /
                    max(_CellSize, 0.001);
                float2 cell = frac(gridPosition);
                float2 edgeDistance = min(cell, 1.0 - cell);
                float2 antialiasWidth = max(fwidth(cell) * 1.35, 0.0015);
                float2 edgeLines = 1.0 - smoothstep(_LineWidth,
                    _LineWidth + antialiasWidth, edgeDistance);
                float gridLine = max(edgeLines.x, edgeLines.y);
                float2 haloLines = 1.0 - smoothstep(_LineWidth * 2.45,
                    _LineWidth * 2.45 + antialiasWidth, edgeDistance);
                float gridHalo = max(haloLines.x, haloLines.y);

                // One clean holographic rail is more readable than several decorative
                // rails at gameplay zoom. A narrow dark halo keeps it readable over
                // both bright pads and detailed tower sprites.
                float pulse = 0.98 + 0.02 * sin(_Time.y * max(0.1, _FlowSpeed) * 2.4);
                half3 haloColor = half3(0.005, 0.025, 0.045);
                half3 freeRgb = lerp(_BaseColor.rgb, haloColor, gridHalo * 0.9);
                freeRgb = lerp(freeRgb, _LineColor.rgb * pulse, gridLine);
                half freeAlpha = saturate(_BaseColor.a +
                    (gridHalo * 0.6 + gridLine * 0.35) * _LineColor.a);
                half4 freeGrid = half4(freeRgb, freeAlpha);

                // Footprint geometry is inset from the real grid in C#, so this layer
                // is only a translucent status panel and never redraws/obscures lines.
                float statePulse = 1.04 + 0.08 * sin(_Time.y * max(0.1, _FlowSpeed) * 2.8);
                half3 stateRgb = input.color.rgb * statePulse;
                half stateAlpha = saturate(input.color.a * 0.56);
                half4 footprintGrid = half4(stateRgb, stateAlpha);
                return lerp(freeGrid, footprintGrid, step(0.5, _UseVertexColor));
            }
            ENDHLSL
        }
    }
}
