Shader "Rouge/Contact Shadow"
{
    Properties
    {
        _ShadowColor("Shadow Color", Color) = (0.004, 0.012, 0.022, 0.62)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="AlphaTest-30"
            "RenderType"="Transparent"
        }
        Pass
        {
            Name "Contact Shadow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor;
            CBUFFER_END

            float _RougeContactShadowStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                float radiusSquared = dot(centered, centered);
                float softEllipse = 1.0 - smoothstep(0.08, 1.0, radiusSquared);
                softEllipse *= softEllipse;
                half alpha = _ShadowColor.a * softEllipse * _RougeContactShadowStrength;
                clip(alpha - 0.002h);
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
