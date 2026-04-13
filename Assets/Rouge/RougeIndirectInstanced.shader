Shader "Rouge/IndirectInstancedURP"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.88, 0.18, 0.18, 1)
        _ScaleMultiplier("Scale Multiplier", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _PositionScaleBuffer;
            StructuredBuffer<float4> _StateBuffer;

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
                float flash : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionScale = _PositionScaleBuffer[input.instanceID];
                float4 state = _StateBuffer[input.instanceID];
                float scale = max(state.y * _ScaleMultiplier, 0.0001);
                
                float3 positionWS = float3(
                    positionScale.x + input.positionOS.x * scale,
                    positionScale.y + input.positionOS.y * scale,
                    positionScale.z + input.positionOS.z * scale
                );
                
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.flash = state.w;
                
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 lightDir = normalize(float3(0.45, 0.8, 0.35));
                float ndotl = saturate(dot(normalize(input.normalWS), lightDir));
                float shade = 0.25 + ndotl * 0.75;
                
                half3 col = _BaseColor.rgb * shade;
                col = lerp(col, half3(1.0, 1.0, 1.0), saturate(input.flash));
                
                return half4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
