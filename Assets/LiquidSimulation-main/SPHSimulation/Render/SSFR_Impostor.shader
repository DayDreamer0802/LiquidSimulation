Shader "Custom/FluidFlatQuadDepth"
{
    SubShader
    {
        // 依然保持 Opaque，显卡的 Early-Z 依然在疯狂为你省性能！
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        
        Pass
        {
            Name "FluidDepthOnly"
            ZWrite On
            ZTest Less
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ParticleData { float4 posRad; };
            StructuredBuffer<ParticleData> _PosRadBuffer;

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float viewDistance : TEXCOORD1; // 到 Quad 平面的距离
                float radius : TEXCOORD1_1;     // 传下半径
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                ParticleData p = _PosRadBuffer[input.instanceID];
                float3 centerWS = p.posRad.xyz;
                float radius = p.posRad.w;
                output.radius = radius;

                // 面向摄像机展开 Quad
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float2 quadXY = input.uv * 2.0 - 1.0;
                float3 posWS = centerWS + (right * quadXY.x + up * quadXY.y) * radius;
                
                output.positionCS = TransformWorldToHClip(posWS);
                output.uv = quadXY;
                
                // 获取距离摄像机的线性距离（米）
                float3 viewPos = TransformWorldToView(posWS);
                output.viewDistance = -viewPos.z; 
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 切成圆
                float r2 = dot(input.uv, input.uv);
                if (r2 > 1.0) discard; 
                
                // 【绝杀】：用纯数学算出 3D 球面的凸起高度！
                float zBump = sqrt(1.0 - r2); 
                
                // 真实的球面深度 = 扁平平面的深度 - 凸向摄像机的距离
                float trueSphereDist = input.viewDistance - (zBump * input.radius);
                
                // 把带有完美 3D 弧度的深度喂给你的 SSFR！
                float normalizedDepth = saturate(trueSphereDist / 150.0);
                return float4(normalizedDepth, 0, 0, 1.0);
            }
            ENDHLSL
        }
    }
}