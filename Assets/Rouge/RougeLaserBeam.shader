Shader "Rouge/LaserBeam"
{
    Properties
    {
        [HDR] _CoreColor("White-hot Core", Color) = (2.8, 3.2, 3.5, 1)
        [HDR] _BeamColor("Beam Color", Color) = (0.08, 1.25, 2.8, 1)
        [HDR] _GlowColor("Outer Glow", Color) = (0.12, 0.25, 2.2, 1)
        _CoreRadius("Core Radius", Range(0.03, 0.65)) = 0.18
        _BeamRadius("Beam Radius", Range(0.1, 0.9)) = 0.52
        _GlowSoftness("Glow Softness", Range(0.02, 0.7)) = 0.28
        _FlowScale("Flow Scale", Range(2.0, 80.0)) = 28.0
        _FlowSpeed("Flow Speed", Range(-40.0, 40.0)) = 18.0
        _RibbonScale("Ribbon Scale", Range(1.0, 32.0)) = 11.0
        _RibbonSpeed("Ribbon Speed", Range(-30.0, 30.0)) = 9.0
        _RibbonIntensity("Ribbon Intensity", Range(0.0, 4.0)) = 1.45
        _NoiseStrength("Energy Turbulence", Range(0.0, 1.0)) = 0.24
        _PulseSpeed("Pulse Speed", Range(0.0, 20.0)) = 6.0
        _FresnelPower("Fresnel Power", Range(0.5, 8.0)) = 2.2
        _FresnelStrength("Fresnel Strength", Range(0.0, 4.0)) = 1.25
        _EndFade("End Fade", Range(0.001, 0.35)) = 0.08
        _Alpha("Global Intensity", Range(0.0, 3.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+40"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "EnergyBeam"
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
            float _CoreRadius;
            float _BeamRadius;
            float _GlowSoftness;
            float _FlowScale;
            float _FlowSpeed;
            float _RibbonScale;
            float _RibbonSpeed;
            float _RibbonIntensity;
            float _NoiseStrength;
            float _PulseSpeed;
            float _FresnelPower;
            float _FresnelStrength;
            float _EndFade;
            float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float EnergyNoise(float2 p)
            {
                float a = sin(dot(p, float2(12.9898, 78.233)));
                float b = cos(dot(p.yx, float2(39.3468, 11.1351)));
                return saturate((a * b) * 0.5 + 0.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewWS))), _FresnelPower);

                // A cylinder only contains its outer shell. Reconstruct a camera-facing
                // cross-section coordinate so the shell still renders a bright center
                // and two soft halo layers instead of a uniformly colored tube.
                float3 axisWS = normalize(TransformObjectToWorldDir(float3(0.0, 1.0, 0.0)));
                float3 sideNormalRaw = normalWS - axisWS * dot(normalWS, axisWS);
                float3 sideViewRaw = viewWS - axisWS * dot(viewWS, axisWS);
                float3 sideNormal = sideNormalRaw * rsqrt(max(dot(sideNormalRaw, sideNormalRaw), 0.0001));
                float3 sideView = sideViewRaw * rsqrt(max(dot(sideViewRaw, sideViewRaw), 0.0001));
                float facing = saturate(abs(dot(sideNormal, sideView)));
                float radial = sqrt(saturate(1.0 - facing * facing));
                float axis = saturate(input.positionOS.y * 0.5 + 0.5);
                float angle = atan2(input.positionOS.z, input.positionOS.x);
                float time = _Time.y;

                float core = 1.0 - smoothstep(_CoreRadius, _CoreRadius + 0.16, radial);
                float body = 1.0 - smoothstep(_BeamRadius, _BeamRadius + _GlowSoftness, radial);
                float innerHalo = (1.0 - smoothstep(_CoreRadius, _BeamRadius, radial)) * (1.0 - core);
                float outerHalo = body * smoothstep(_CoreRadius * 0.7, 1.0, radial);

                float flowPhase = axis * _FlowScale - time * _FlowSpeed;
                float fastFlow = pow(saturate(sin(flowPhase) * 0.5 + 0.5), 7.0);
                float turbulence = EnergyNoise(float2(axis * 17.0 - time * 2.0, angle * 1.7 + time));
                float flow = lerp(0.78 + fastFlow * 0.42, 0.62 + fastFlow * 0.55 + turbulence * 0.28,
                    saturate(_NoiseStrength));

                float ribbonPhaseA = angle * 2.0 + axis * _RibbonScale - time * _RibbonSpeed;
                float ribbonPhaseB = angle * 2.0 - axis * (_RibbonScale * 0.83) + time * (_RibbonSpeed * 1.17);
                float ribbonA = pow(saturate(sin(ribbonPhaseA) * 0.5 + 0.5), 14.0);
                float ribbonB = pow(saturate(sin(ribbonPhaseB) * 0.5 + 0.5), 18.0);
                float ribbonEnvelope = smoothstep(_CoreRadius, 0.48, radial) * (1.0 - smoothstep(0.6, 1.0, radial));
                float ribbons = max(ribbonA, ribbonB) * ribbonEnvelope * _RibbonIntensity;

                float pulse = 0.88 + sin(time * _PulseSpeed + axis * 8.0) * 0.12;
                float endFade = smoothstep(0.0, _EndFade, axis) * smoothstep(0.0, _EndFade, 1.0 - axis);

                float3 whiteCore = _CoreColor.rgb * core * (1.15 + fastFlow * 0.5);
                float3 beam = _BeamColor.rgb * (innerHalo * 1.15 + body * 0.42) * flow;
                float3 glow = _GlowColor.rgb * outerHalo * (0.42 + fresnel * _FresnelStrength);
                float3 ribbonColor = lerp(_CoreColor.rgb, _BeamColor.rgb, 0.72) * ribbons;
                float3 emission = (whiteCore + beam + glow + ribbonColor) * pulse * endFade * _Alpha;

                float mask = saturate(core + body * 0.82 + ribbons * 0.55 + fresnel * body * 0.35) * endFade;
                clip(mask - 0.015);
                return half4(emission * mask, mask);
            }
            ENDHLSL
        }
    }
}
