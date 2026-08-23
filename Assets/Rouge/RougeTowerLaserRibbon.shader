Shader "Rouge/TowerLaserRibbon"
{
    Properties
    {
        [HDR] _CoreColor("White-hot Core", Color) = (1.25, 1.5, 1.85, 1)
        [HDR] _BeamColor("Energy Color", Color) = (0.025, 0.46, 2.0, 1)
        [HDR] _GlowColor("Outer Glow", Color) = (0.2, 0.018, 1.2, 1)
        _CoreWidth("Core Width", Range(0.02, 0.5)) = 0.07
        _BeamWidth("Beam Width", Range(0.1, 0.9)) = 0.42
        _EdgeSoftness("Edge Softness", Range(0.02, 0.8)) = 0.50
        _FlowScale("Flow Scale", Range(2.0, 80.0)) = 32.0
        _FlowSpeed("Flow Speed", Range(-40.0, 40.0)) = 18.0
        _PulseSpeed("Pulse Speed", Range(0.0, 20.0)) = 7.0
        _SparkIntensity("Spark Intensity", Range(0.0, 4.0)) = 1.35
        _EndFade("End Fade", Range(0.001, 0.25)) = 0.025
        _Alpha("Intensity", Range(0.0, 3.0)) = 0.9
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+45"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CrystalEnergyRibbon"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _CoreColor;
            float4 _BeamColor;
            float4 _GlowColor;
            float _CoreWidth;
            float _BeamWidth;
            float _EdgeSoftness;
            float _FlowScale;
            float _FlowSpeed;
            float _PulseSpeed;
            float _SparkIntensity;
            float _EndFade;
            float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float along = saturate(input.uv.x);
                float across = abs(input.uv.y * 2.0 - 1.0);
                float time = _Time.y;

                float core = 1.0 - smoothstep(_CoreWidth,
                    min(1.0, _CoreWidth + _EdgeSoftness * 0.28), across);
                float body = 1.0 - smoothstep(_BeamWidth,
                    min(1.0, _BeamWidth + _EdgeSoftness), across);
                float outerGlow = (1.0 - smoothstep(0.28, 1.0, across)) * (1.0 - core);

                float flowPhase = along * _FlowScale - time * _FlowSpeed;
                float flow = 0.82 + pow(saturate(sin(flowPhase) * 0.5 + 0.5), 10.0) * 0.55;
                float sparkA = pow(saturate(sin(flowPhase * 1.83 + 1.7) * 0.5 + 0.5), 24.0);
                float sparkB = pow(saturate(sin(along * (_FlowScale * 0.67) -
                    time * (_FlowSpeed * 1.31) - 0.9) * 0.5 + 0.5), 30.0);
                float sparks = max(sparkA, sparkB) * (core + body * 0.55) * _SparkIntensity;
                float pulse = 0.88 + sin(time * _PulseSpeed + along * 9.0) * 0.12;
                float endFade = smoothstep(0.0, _EndFade, along) *
                    smoothstep(0.0, _EndFade, 1.0 - along);

                float3 whiteCore = _CoreColor.rgb * core * (1.35 + sparks * 0.45);
                float3 energyBody = _BeamColor.rgb * body * flow;
                float3 glow = _GlowColor.rgb * outerGlow * (0.38 + flow * 0.24);
                float3 hotPackets = lerp(_BeamColor.rgb, _CoreColor.rgb, 0.78) * sparks;
                float3 emission = (whiteCore + energyBody + glow + hotPackets) *
                    pulse * endFade * _Alpha;

                float mask = saturate(core + body * 0.82 + outerGlow * 0.42 + sparks * 0.35) * endFade;
                clip(mask - 0.012);
                return half4(emission * mask, mask);
            }
            ENDHLSL
        }
    }
}
