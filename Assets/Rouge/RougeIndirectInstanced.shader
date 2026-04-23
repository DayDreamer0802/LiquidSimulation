Shader "Custom/EnhancedIndirectInstancedURP"
{
    Properties
    {
        [Header(Base Material)]
        _BaseColor("Base Color", Color) = (0.88, 0.18, 0.18, 1)
        _ScaleMultiplier("Scale Multiplier", Float) = 1
        _TopTint("Top Tint", Color) = (1.08, 1.00, 0.92, 1)
        _BottomTint("Bottom Tint", Color) = (0.62, 0.74, 1.05, 1)
        _RimColor("Rim Color", Color) = (0.84, 0.94, 1.0, 1)
        _FlashColor("Flash Color", Color) = (1.0, 0.95, 0.82, 1)
        _VariationStrength("Variation Strength", Range(0.0, 0.4)) = 0.12
        _BreakupScale("Breakup Scale", Range(0.5, 8.0)) = 3.0
        _BreakupStrength("Breakup Strength", Range(0.0, 0.35)) = 0.10
        _RimStrength("Rim Strength", Range(0.0, 2.0)) = 0.7

        [Header(Fresnel Shield FX)]
        [HDR] _ShieldColor("Shield Glow Color", Color) = (0.0, 1.0, 1.0, 1) // 默认青色盾光
        _FresnelPower("Fresnel Power", Range(0.1, 8.0)) = 3.0 // 边缘光锐度，越大边缘越窄
    }

    SubShader
    {
        // 渲染队列设置为不透明，URP 专用
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" } // 设置 LightMode 为前向光照

            Cull Back // 开启背面剔除
            ZWrite On   // 开启深度写入

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing // 必须，启用实例化
            
            // 💡 关键：启用主光源和阴影支持，让成千上万个单位可以接收阴影
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl" // 💡 关键：引入 URP 光照库

            StructuredBuffer<float4> _PositionScaleBuffer;
            StructuredBuffer<float4> _StateBuffer;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopTint;
                float4 _BottomTint;
                float4 _RimColor;
                float4 _FlashColor;
                half4 _ShieldColor; // 用于视差/受击的盾光颜色
                float _ScaleMultiplier;
                float _VariationStrength;
                float _BreakupScale;
                float _BreakupStrength;
                float _RimStrength;
                float _FresnelPower; // 菲涅尔锐度参数
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
                float3 positionWS : TEXCOORD0; // 💡 关键：需要世界空间位置来计算阴影和菲涅尔
                float3 normalWS : TEXCOORD1;
                float flash : TEXCOORD2;
                float curse : TEXCOORD3;
                float localY : TEXCOORD4;
                float variation : TEXCOORD5;
            };

            float Hash01(uint value)
            {
                value ^= 2747636419u;
                value *= 2654435769u;
                value ^= value >> 16;
                value *= 2654435769u;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215.0;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionScale = _PositionScaleBuffer[input.instanceID];
                float4 state = _StateBuffer[input.instanceID];
                float visualFlags = floor(max(state.w, 0.0) / 10.0 + 0.001);
                float scale = max(state.y * _ScaleMultiplier, 0.0001);

                // 完全保留你的顶点坐标计算逻辑
                float3 positionWS = float3(
                    positionScale.x + input.positionOS.x * scale,
                    positionScale.y + input.positionOS.y * scale,
                    positionScale.z + input.positionOS.z * scale
                );

                output.positionHCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS; // 传递世界位置
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.flash = frac(max(state.w, 0.0));
                output.curse = step(0.5, fmod(visualFlags, 2.0));
                output.localY = input.positionOS.y;
                output.variation = Hash01(input.instanceID + 1u);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndotl = dot(normalWS, mainLight.direction);
                float wrapDiffuse = saturate(ndotl * 0.65 + 0.35);
                float shade = 0.18 + wrapDiffuse * 0.82 * mainLight.shadowAttenuation;

                float NdotV = saturate(dot(normalWS, viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);

                float height01 = saturate(input.localY * 0.5 + 0.5);
                float3 verticalTint = lerp(_BottomTint.rgb, _TopTint.rgb, height01);

                float valueJitter = lerp(1.0 - _VariationStrength, 1.0 + _VariationStrength, input.variation);

                float breakup =
                    sin(input.positionWS.x * _BreakupScale + input.variation * 6.28318) *
                    sin(input.positionWS.z * (_BreakupScale * 0.83) - input.variation * 4.123);
                breakup = breakup * 0.5 + 0.5;
                float breakupMul = lerp(1.0 - _BreakupStrength, 1.0 + _BreakupStrength, breakup);

                half3 col = _BaseColor.rgb * verticalTint;
                col *= valueJitter;
                col *= shade;
                col *= breakupMul;

                col += _RimColor.rgb * fresnel * _RimStrength;

                half luminance = dot(col, half3(0.2126, 0.7152, 0.0722));
                half3 curseCol = luminance.xxx * 0.32 + _ShieldColor.rgb * fresnel * 0.28;
                col = lerp(col, curseCol, input.curse);

                float flashAmt = saturate(input.flash);
                col += _FlashColor.rgb * flashAmt * (0.35 + fresnel * 1.1);

                return half4(saturate(col), _BaseColor.a);
            }
            ENDHLSL
        }
    }
}