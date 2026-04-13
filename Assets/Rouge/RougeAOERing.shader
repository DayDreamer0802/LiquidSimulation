Shader "Rouge/AOERing"
{
    Properties
    {
        _Color("Color", Color) = (1, 0.6, 0.0, 0.8)
        _InnerRadiusRatio("Inner Radius Ratio", Range(0.5, 0.99)) = 0.75
        _GlowIntensity("Glow Intensity", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _InnerRadiusRatio;
            float _GlowIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.localPos = input.positionOS.xyz;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Unity cylinder: radius 0.5 in XZ, height -1 to +1 in Y
                float distFromAxis = length(input.localPos.xz);
                float normalizedDist = distFromAxis / 0.5;

                // Discard inner hollow part to create ring
                if (normalizedDist < _InnerRadiusRatio)
                    discard;

                // Ring edge glow: brighter near inner edge
                float ringCenter = (_InnerRadiusRatio + 1.0) * 0.5;
                float ringHalfWidth = (1.0 - _InnerRadiusRatio) * 0.5;
                float edgeFactor = 1.0 - abs(normalizedDist - ringCenter) / max(ringHalfWidth, 0.001);
                edgeFactor = saturate(edgeFactor);

                // Height fade: bright at base, fade at top
                float heightFactor = 1.0 - saturate(input.localPos.y * 0.8 + 0.3);

                // Animated energy lines on the ring surface
                float energy = sin(normalizedDist * 40.0 + _Time.y * 8.0) * 0.3 + 0.7;
                float verticalWaves = sin(input.localPos.y * 12.0 - _Time.y * 6.0) * 0.2 + 0.8;

                half3 col = _Color.rgb * (1.0 + edgeFactor * _GlowIntensity) * energy * verticalWaves;
                float alpha = _Color.a * heightFactor * pow(edgeFactor, 0.5);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
