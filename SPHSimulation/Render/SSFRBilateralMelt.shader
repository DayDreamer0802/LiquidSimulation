Shader "Custom/SSFR_FinalWater"
{
    Properties
    {
        [MainTexture] _FluidDepthTex("Fluid Depth RT", 2D) = "black" {}
        
        [Header(Water Colors)]
        _WaterColor("深水颜色 (Deep Color)", Color) = (0.0, 0.35, 0.65, 0.9)
        _FoamColor("浅水/边缘 (Shallow Color)", Color) = (0.6, 0.9, 1.0, 1.0)
        
        [Header(Fluid Properties)]
        _FluidMaxDepth("流体最大深度范围", Float) = 150.0 // 提取原来硬编码的 150.0
        _BlurPixels("融合半径 (Blur Radius)", Range(1.0, 50.0)) = 15.0 
        _MeltDistance("融合深度阈值 (Melt Dist)", Range(0.01, 2.0)) = 0.5
        
        [Header(Optical Properties)]
        _Absorption("水体吸收率(越小越透明)", Range(0.01, 2.0)) = 0.3
        _Refraction("折射扭曲强度", Range(0.0, 0.2)) = 0.05
        
        [Header(Surface Details)]
        _NormalStrength("宏观法线强度(立体感)", Range(0.1, 3.0)) = 1.0 // 替换原来的 Flatten
        _SpecularPower("高光集中度", Range(10.0, 500.0)) = 250.0
        _SpecularIntensity("高光亮度", Range(0.0, 5.0)) = 1.5
        
        [Header(Detail Ripples)]
        [NoScaleOffset] _DetailNormalMap("水面波纹法线(推荐开启)", 2D) = "bump" {}
        _RippleScale("波纹缩放", Range(0.1, 20.0)) = 5.0
        _RippleSpeed("波纹流动速度", Vector) = (0.1, 0.1, 0, 0)
        _RippleStrength("波纹强度", Range(0.0, 1.0)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        LOD 100
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "SSFR_Water"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FluidDepthTex);
            SAMPLER(sampler_FluidDepthTex);
            
            TEXTURE2D(_DetailNormalMap);
            SAMPLER(sampler_DetailNormalMap);

            float4 _WaterColor;
            float4 _FoamColor;
            float _FluidMaxDepth;
            float _BlurPixels;
            float _MeltDistance;
            float _Absorption;
            float _NormalStrength;
            float _SpecularPower;
            float _SpecularIntensity;
            float _Refraction;
            
            float _RippleScale;
            float2 _RippleSpeed;
            float _RippleStrength;

            // 完美的相机坐标重建函数
            float3 ReconstructViewPos(float2 uv, float linearZ)
            {
                float2 ndc = uv * 2.0 - 1.0;
                float3 viewPos;
                viewPos.z = linearZ;
                // 利用相机投影矩阵精确还原 X 和 Y，无视相机旋转和FOV变化
                viewPos.x = ndc.x * viewPos.z / unity_CameraProjection._m00;
                viewPos.y = ndc.y * viewPos.z / unity_CameraProjection._m11;
                return viewPos;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float centerDepth = SAMPLE_TEXTURE2D(_FluidDepthTex, sampler_FluidDepthTex, uv).r;
                float3 backgroundScene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // 如果没有流体，直接返回背景
                if (centerDepth <= 0.01) return float4(backgroundScene, 1.0);

                // =========================================================
                // 1. 真正的高斯双边滤波 (Bilateral Filter)
                // =========================================================
                float sum = 0.0;
                float totalWeight = 0.0;
                float2 texelSize = 1.0 / _ScreenParams.xy;
                
                float blurRadius = _BlurPixels;
                float sigmaSpatial = blurRadius * 0.5;
                float sigmaRange = _MeltDistance;

                for (int x = -2; x <= 2; x++)
                {
                    for (int y = -2; y <= 2; y++)
                    {
                        float2 offset = float2(x, y) * texelSize * (blurRadius / 2.0); 
                        float sampleDepth = SAMPLE_TEXTURE2D(_FluidDepthTex, sampler_FluidDepthTex, uv + offset).r;

                        if (sampleDepth > 0.01)
                        {
                            float depthDiff = centerDepth - sampleDepth;
                            
                            // 高斯空间权重 (距离中心越近权重越大)
                            float spatialWeight = exp(-(x*x + y*y) / (2.0 * sigmaSpatial * sigmaSpatial));
                            // 高斯深度权重 (深度差异越小权重越大，保护流体边缘)
                            float rangeWeight = exp(-(depthDiff*depthDiff) / (2.0 * sigmaRange * sigmaRange));
                            
                            float weight = spatialWeight * rangeWeight;
                            
                            sum += sampleDepth * weight;
                            totalWeight += weight;
                        }
                    }
                }

                float smoothDepth = (totalWeight > 0.0001) ? (sum / totalWeight) : centerDepth;
                float fluidLinearZ = smoothDepth * _FluidMaxDepth;

                // =========================================================
                // 2. 场景深度剔除测试
                float sceneRawDepth = SampleSceneDepth(uv);
                float sceneLinearZ = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                if (sceneLinearZ < fluidLinearZ - 0.2) 
                {
                    return float4(backgroundScene, 1.0);
                }

                // =========================================================
                // 3. 完美的观察空间位置与法线重建
                float3 viewPos = ReconstructViewPos(uv, fluidLinearZ);
                
                float3 dx = ddx(viewPos);
                float3 dy = ddy(viewPos);
                float3 normal = normalize(cross(dx, dy));
                
                // 修正法线朝向（确保朝向相机）
                if (normal.z < 0.0) normal = -normal;
                
                // 恢复流体立体感
                normal.xy *= _NormalStrength; 
                normal = normalize(normal);

                // =========================================================
                // 4. 叠加水面波纹细节 (高频法线)
                float2 rippleUV = uv * _RippleScale + _Time.y * _RippleSpeed;
                float3 detailNormal = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, rippleUV));
                
                // 混合宏观法线与微观法线
                normal = normalize(normal + detailNormal * _RippleStrength);

                // =========================================================
                // 5. 光学与着色计算
                float2 refractedUV = uv + normal.xy * _Refraction;
                float3 refractedScene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, refractedUV).rgb;

                float waterThickness = max(0.0, sceneLinearZ - fluidLinearZ);
                float transmission = exp(-waterThickness * _Absorption);

                // 修正视线向量：从表面指向相机原点 (0,0,0)
                float3 viewDir = normalize(-viewPos);
                float3 lightDir = normalize(float3(0.5, 1.0, -0.5)); 
                float3 halfVector = normalize(lightDir + viewDir);

                float ndotl = saturate(dot(normal, lightDir) * 0.5 + 0.5); // 半伯特光照，让暗部不死黑
                
                // 改进的 Schlick 菲涅尔近似
                float fresnelBase = 1.0 - max(0.0, dot(normal, viewDir));
                float fresnel = pow(fresnelBase, 4.0);
                
                float specular = pow(max(0.0, dot(normal, halfVector)), _SpecularPower) * _SpecularIntensity;

                float3 waterAlbedo = lerp(_WaterColor.rgb, _FoamColor.rgb, fresnel) * ndotl;
                float3 finalWaterColor = lerp(waterAlbedo, refractedScene, transmission);

                return float4(finalWaterColor + specular.xxx, 1.0);
            }
            ENDHLSL
        }
    }
}