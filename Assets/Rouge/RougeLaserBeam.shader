Shader "Rouge/LaserBeam"
{
    Properties
    {
        [HDR] _BeamColor("Beam Core Color", Color) = (0.25, 0.95, 1.0, 1)
        [HDR] _EdgeColor("Beam Edge Color", Color) = (0.06, 0.45, 1.0, 1)
        _CoreRadius("Core Radius", Range(0.05, 0.9)) = 0.28
        _EdgeSoftness("Edge Softness", Range(0.02, 0.5)) = 0.14
        _PulseSpeed("Pulse Speed", Range(0.0, 20.0)) = 8.0
        _ScrollSpeed("Scroll Speed", Range(0.0, 40.0)) = 16.0
        _NoiseStrength("Noise Strength", Range(0.0, 1.0)) = 0.35
        _HelixTightness("Helix Tightness", Range(2.0, 64.0)) = 24.0
        _HelixWidth("Helix Width", Range(0.02, 0.5)) = 0.12
        _HelixIntensity("Helix Intensity", Range(0.0, 3.0)) = 1.35
        _HelixSpinSpeed("Helix Spin Speed", Range(0.0, 40.0)) = 13.0
        _HelixSecondaryOffset("Helix Secondary Offset", Range(0.0, 3.14159)) = 1.5708
        _HelixCutout("Helix Cutout", Range(0.0, 1.0)) = 0.82
        _FresnelPower("Fresnel Power", Range(0.5, 8.0)) = 2.4
        _FresnelStrength("Fresnel Strength", Range(0.0, 3.0)) = 1.2
        _Alpha("Global Alpha", Range(0.0, 2.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+30" "RenderType" = "Transparent" }
        Blend SrcAlpha One
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
            float4 _BeamColor;
            float4 _EdgeColor;
            float _CoreRadius;
            float _EdgeSoftness;
            float _PulseSpeed;
            float _ScrollSpeed;
            float _NoiseStrength;
            float _HelixTightness;
            float _HelixWidth;
            float _HelixIntensity;
            float _HelixSpinSpeed;
            float _HelixSecondaryOffset;
            float _HelixCutout;
            float _FresnelPower;
            float _FresnelStrength;
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
                float3 localPos : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.localPos = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower) * _FresnelStrength;

                float radial = saturate(length(input.localPos.xz) / 0.5);
                float axis01 = saturate(input.localPos.y * 0.5 + 0.5);
                float angle = atan2(input.localPos.z, input.localPos.x);

                float inner = 1.0 - smoothstep(_CoreRadius, _CoreRadius + _EdgeSoftness, radial);
                float edge = smoothstep(_CoreRadius - _EdgeSoftness, _CoreRadius + _EdgeSoftness * 1.6, radial);

                float axisPulse = 0.7 + 0.3 * sin(axis01 * 32.0 - _Time.y * _ScrollSpeed);
                float globalPulse = 0.85 + 0.15 * sin(_Time.y * _PulseSpeed);
                float shimmer =
                    sin(axis01 * 54.0 - _Time.y * (_ScrollSpeed * 1.35) + radial * 9.0) *
                    cos(axis01 * 23.0 + _Time.y * (_ScrollSpeed * 0.65) - radial * 13.0);
                shimmer = shimmer * 0.5 + 0.5;
                float noise = lerp(1.0, 0.7 + shimmer * 0.6, saturate(_NoiseStrength));

                float helixPhase = angle + axis01 * _HelixTightness - _Time.y * _HelixSpinSpeed;
                float helixA = 1.0 - smoothstep(0.0, _HelixWidth, abs(sin(helixPhase)));
                float helixB = 1.0 - smoothstep(0.0, _HelixWidth, abs(sin(helixPhase + _HelixSecondaryOffset)));
                float helixMask = saturate(max(helixA, helixB));
                float helixEnvelope = smoothstep(0.1, 0.95, radial) * (1.0 - smoothstep(0.82, 1.0, radial));
                float helixPulse = 0.7 + 0.3 * sin(axis01 * 22.0 - _Time.y * (_ScrollSpeed * 1.25));
                float helixEnergy = helixMask * helixEnvelope * helixPulse * _HelixIntensity;
                float cutoutSupport = lerp(1.0, helixMask, saturate(_HelixCutout));
                float shellMask = edge * cutoutSupport;
                float cutoutMask = saturate(inner * 0.95 + shellMask * 1.2 + helixMask * 0.12 + fresnel * 0.16);
                clip(cutoutMask - 0.03);

                float3 coreColor = _BeamColor.rgb * (inner * axisPulse * globalPulse * noise);
                float3 edgeColor = _EdgeColor.rgb * (shellMask * (0.45 + fresnel) * (0.75 + axisPulse * 0.25));
                float3 helixColor = lerp(_BeamColor.rgb, _EdgeColor.rgb, 0.65) * helixEnergy;
                float3 color = coreColor + edgeColor + helixColor;

                float alpha = saturate((inner * 0.9 + shellMask * 0.65 + helixMask * 0.25 + fresnel * 0.35) * _Alpha * globalPulse);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
