Shader "Rouge/VFXInstanced"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 0.5, 0.1, 1)
        _ScaleMultiplier("Scale Multiplier", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _PositionScaleBuffer;
            StructuredBuffer<float4> _StateBuffer; // x,y,z = scale; w = color progress

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _ScaleMultiplier;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float progress : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 posData = _PositionScaleBuffer[input.instanceID];
                float4 state = _StateBuffer[input.instanceID];
                
                float3 scale = state.xyz * _ScaleMultiplier;
                
                float3 positionWS = float3(
                    posData.x + input.positionOS.x * scale.x,
                    posData.y + input.positionOS.y * scale.y,
                    posData.z + input.positionOS.z * scale.z
                );
                
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.progress = state.w;
                
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Basic lighting to give volume
                float3 lightDir = normalize(float3(0.45, 0.8, 0.35));
                float ndotl = saturate(dot(normalize(input.normalWS), lightDir));
                float shade = 0.5 + ndotl * 0.5;
                
                half3 col = _BaseColor.rgb * shade;
                // Fade out over progress
                float alpha = _BaseColor.a * (1.0 - input.progress);
                
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
