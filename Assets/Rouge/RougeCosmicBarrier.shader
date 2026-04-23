Shader "Custom/PremiumQuantumCube_URP"
{
    Properties
    {
        [Header(Glass Coating)]
        [HDR] _EdgeColor1("Edge Color 1 (边缘霓虹色1)", Color) = (0.0, 1.0, 0.8, 1)
        [HDR] _EdgeColor2("Edge Color 2 (边缘霓虹色2)", Color) = (0.8, 0.2, 1.0, 1)
        _FresnelPower("Fresnel Power (边缘锐度)", Range(0.1, 8.0)) = 3.0

        [Header(Inner Quantum Core)]
        [HDR] _CoreColor("Core Color (核心能量色)", Color) = (0.0, 0.4, 1.0, 1)
        _CoreScale("Core Scale (核心晶格密度)", Float) = 20.0
        _CoreDepth("Core Parallax Depth (视差深度)", Range(0, 2)) = 0.5
        _AnimSpeed("Animation Speed (流转速度)", Float) = 2.0
    }

    SubShader
    {
        // 渲染队列设置为透明，使用 Additive (叠加) 混合模式制造全息发光感
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        
        Blend One One // 核心秘诀：叠加混合模式
        ZWrite Off    // 关闭深度写入
        Cull Off      // 核心秘诀：关闭剔除！让魔方的背面也被渲染出来

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColor1;
                half4 _EdgeColor2;
                half4 _CoreColor;
                float _FresnelPower;
                float _CoreScale;
                float _CoreDepth;
                float _AnimSpeed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 视线向量 (从摄像机指向像素)
                float3 viewDirWS = normalize(input.positionWS - GetCameraPositionWS());
                float3 normalWS = normalize(input.normalWS);
                
                // 菲涅尔 (N dot V)
                float NdotV = saturate(dot(normalWS, -viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);

                // ==========================================
                // 高级感来源一：虹彩镀膜 (Iridescent Coating)
                // ==========================================
                // 根据观察角度在两种颜色之间平滑过渡，模拟高级镜头或钛金属的表面色散
                float3 edgeColor = lerp(_EdgeColor1.rgb, _EdgeColor2.rgb, sin(NdotV * 3.14) * 0.5 + 0.5);
                float3 glassGlow = edgeColor * fresnel;

                // ==========================================
                // 高级感来源二：内部视差体积 (Internal Parallax)
                // ==========================================
                // 将采样坐标沿着视线方向往模型内部推，制造“正方体内部有东西”的纵深错觉
                float3 innerPosWS = input.positionWS + viewDirWS * _CoreDepth;
                
                // 基于偏移后的世界坐标，生成一个三维立体的动态能量晶格
                float3 grid = sin(innerPosWS * _CoreScale + _Time.y * _AnimSpeed);
                
                // 提取 X, Y, Z 三个方向晶格的交叉点作为发光的“量子节点”
                float coreNoise = saturate(grid.x * grid.y * grid.z);
                coreNoise = pow(coreNoise, 12.0); // 极高次幂让能量点变得非常小且锐利

                // 核心亮度计算：越靠近边缘越淡（被玻璃外壳遮挡）
                float3 coreGlow = _CoreColor.rgb * coreNoise * (1.0 - fresnel) * 2.0;

                // ==========================================
                // 高级感来源三：双面渲染叠加 (Cull Off + Blend One One)
                // ==========================================
                // 因为我们在 SubShader 开启了 Cull Off，正方体的背面也会被渲染并叠加到正面。
                // 配合视差偏移，正面和背面的量子节点会产生相对位移，立刻产生极强的裸眼3D感！

                float3 finalColor = glassGlow + coreGlow;

                return half4(finalColor, 1.0); // Additive 模式下 Alpha 不起作用
            }
            ENDHLSL
        }
    }
}