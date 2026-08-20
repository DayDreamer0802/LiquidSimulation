Shader "Rouge/Tower Place Grid"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0,0,0,0)
        _LineColor("Free Grid Color", Color) = (1,1,1,0.92)
        _CellSize("Cell Size", Float) = 8
        _LineWidth("Line Width", Range(0.002, 0.12)) = 0.045
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
                float2 cell = frac((input.positionWS.xz - _GridOrigin.xy) / max(_CellSize, 0.001));
                float2 edgeDistance = min(cell, 1.0 - cell);
                float2 antialiasWidth = max(fwidth(cell) * 1.25, 0.002);
                float2 edgeLines = 1.0 - smoothstep(_LineWidth,
                    _LineWidth + antialiasWidth, edgeDistance);
                float gridLine = max(edgeLines.x, edgeLines.y);
                half4 whiteGrid = lerp(_BaseColor, _LineColor, gridLine);
                half4 footprintGrid = lerp(input.color,
                    half4(input.color.rgb, 1.0), gridLine);
                return lerp(whiteGrid, footprintGrid, step(0.5, _UseVertexColor));
            }
            ENDHLSL
        }
    }
}
