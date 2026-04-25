Shader "Rouge/AOERing"
{
    Properties
    {
        _Color("Color", Color) = (1, 0.6, 0.0, 0.8)
        _InnerRadiusRatio("Inner Radius Ratio", Range(0.5, 0.99)) = 0.75
        _GlowIntensity("Glow Intensity", Float) = 3.0
        _AlphaMultiplier("Alpha Multiplier", Range(0, 1)) = 1.0
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
            float _AlphaMultiplier;
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
                float distFromAxis = length(input.localPos.xz);
                float normalizedDist = distFromAxis / 0.5;

                if (normalizedDist < _InnerRadiusRatio)
                    discard;

                float ringCenter = (_InnerRadiusRatio + 1.0) * 0.5;
                float ringHalfWidth = (1.0 - _InnerRadiusRatio) * 0.5;
                float edgeFactor = 1.0 - abs(normalizedDist - ringCenter) / max(ringHalfWidth, 0.001);
                edgeFactor = saturate(edgeFactor);

                float heightFactor = 1.0 - saturate((input.localPos.y + 1.0) * 0.42);
                float rim = pow(saturate(1.0 - abs(dot(normalize(input.normalWS), float3(0.0, 1.0, 0.0)))), 1.2);
                float glow = lerp(0.72, 1.0 + _GlowIntensity * 0.24, pow(edgeFactor, 1.6));

                half3 col = _Color.rgb * glow * (0.88 + rim * 0.12);
                float alpha = _Color.a * _AlphaMultiplier * heightFactor * pow(edgeFactor, 1.45);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
