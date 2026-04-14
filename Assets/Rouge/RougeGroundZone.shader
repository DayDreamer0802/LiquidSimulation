Shader "Rouge/GroundZone"
{
    Properties
    {
        _Color("Color", Color) = (0.3, 0.8, 1.0, 0.7)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 localPos : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.localPos = input.positionOS.xyz;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float radial = saturate(length(input.localPos.xz) / 0.5);
                float edge = smoothstep(0.45, 0.95, radial);
                float core = 1.0 - smoothstep(0.0, 0.72, radial);
                float pulse = 0.84 + 0.16 * sin(_Time.y * 6.0 + radial * 16.0);
                float alpha = _Color.a * saturate(core * 0.65 + edge * 0.95) * pulse;
                half3 color = _Color.rgb * (0.7 + edge * 0.9 + core * 0.25);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}