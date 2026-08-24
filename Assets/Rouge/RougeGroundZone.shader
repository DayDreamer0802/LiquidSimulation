Shader "Rouge/GroundZone"
{
    Properties
    {
        _Color("Color", Color) = (0.3, 0.8, 1.0, 0.7)
        _SecondaryColor("Secondary Color", Color) = (1, 1, 1, 0.35)
        [HDR] _HotColor("Fire Hot Color", Color) = (1.0, 0.75, 0.12, 1)
        _ZoneType("Zone Type", Float) = 0
        _NoiseScale("Noise Scale", Float) = 1
        _EdgeIrregularity("Edge Irregularity", Float) = 0.15
        _PulseSpeed("Pulse Speed", Float) = 2
        _FlowSpeed("Flow Speed", Float) = 1
        _EmissionStrength("Emission Strength", Float) = 1
        _CoreStrength("Core Strength", Float) = 1
        _RimStrength("Rim Strength", Float) = 1
        _TimeOffset("Instance Time Offset", Float) = 0
        _LifeAlpha("Instance Life Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            float4 _Color;
            float4 _SecondaryColor;
            float4 _HotColor;
            float _ZoneType;
            float _NoiseScale;
            float _EdgeIrregularity;
            float _PulseSpeed;
            float _FlowSpeed;
            float _EmissionStrength;
            float _CoreStrength;
            float _RimStrength;
            float _TimeOffset;
            float _LifeAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 localPos : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.localPos = input.positionOS.xyz;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.localPos.xz / 0.5;
                float radial = length(uv);
                float angle = atan2(uv.y, uv.x);
                float time = _Time.y + _TimeOffset;

                float flowNoise =
                    sin(uv.x * (6.0 + _NoiseScale * 3.0) + time * (_FlowSpeed * 1.15) + angle * 1.7) +
                    cos(uv.y * (7.5 + _NoiseScale * 2.0) - time * (_FlowSpeed * 0.9) - angle * 2.3) +
                    sin((uv.x + uv.y) * (4.5 + _NoiseScale * 2.6) + time * (_FlowSpeed * 1.45));
                flowNoise *= 0.3333333;

                float edgeNoise =
                    sin(angle * (5.0 + _NoiseScale * 2.5) + time * (_FlowSpeed * 0.35)) +
                    cos(angle * (8.0 + _NoiseScale * 4.0) - time * (_FlowSpeed * 0.22) + flowNoise * 1.8);
                edgeNoise *= 0.5;

                float irregularRadius = 1.0 + edgeNoise * _EdgeIrregularity;
                float bodyMask = 1.0 - smoothstep(irregularRadius - 0.18, irregularRadius + 0.02, radial);
                float rimMask = smoothstep(irregularRadius - 0.16, irregularRadius - 0.03, radial) - smoothstep(irregularRadius - 0.03, irregularRadius + 0.05, radial);
                float coreMask = 1.0 - smoothstep(0.0, irregularRadius * 0.8, radial);
                float pulse = 0.85 + 0.15 * sin(time * _PulseSpeed + flowNoise * 2.2 + radial * 8.0);

                float3 color = _Color.rgb;
                float alpha = 0.0;

                if (_ZoneType < 0.5)
                {
                    float ooze = saturate(bodyMask * (0.82 + flowNoise * 0.28 + coreMask * 0.16));
                    float slimeVein = smoothstep(0.12, 0.95, flowNoise * 0.5 + 0.5);
                    color = lerp(_SecondaryColor.rgb, _Color.rgb, slimeVein);
                    color += _Color.rgb * coreMask * 0.25;
                    alpha = _Color.a * ooze * pulse * (0.78 + rimMask * _RimStrength * 0.4);
                }
                else if (_ZoneType < 1.5)
                {
                    float crystal = abs(sin(uv.x * (10.0 + _NoiseScale * 5.0) + time * (_FlowSpeed * 0.35)) * cos(uv.y * (9.0 + _NoiseScale * 4.0) - time * (_FlowSpeed * 0.25)));
                    float frost = saturate(bodyMask * (_CoreStrength * 0.72 + crystal * 0.55 + rimMask * _RimStrength * 0.35));
                    color = lerp(_Color.rgb, _SecondaryColor.rgb, saturate(crystal * 0.9 + rimMask * 0.45));
                    color += _SecondaryColor.rgb * coreMask * 0.18;
                    alpha = _Color.a * frost * (0.88 + pulse * 0.22);
                }
                else
                {
                    float embers = smoothstep(-0.18, 0.95, flowNoise + coreMask * 0.55);
                    float tongues = saturate(sin((uv.x - uv.y) * (7.0 + _NoiseScale * 4.0) - time * (_FlowSpeed * 2.2)) * 0.5 + 0.5);
                    float heat = saturate(coreMask * _CoreStrength + embers * 0.55 +
                        tongues * 0.22);
                    color = lerp(_SecondaryColor.rgb, _Color.rgb, heat);
                    color += _Color.rgb * rimMask * _RimStrength * 0.22;
                    alpha = _Color.a * bodyMask * (0.72 + heat * 0.42) * pulse;

                    // Zone type 3 is reserved for tower fire. Keep the established
                    // type-2 tactical burn patch unchanged while giving persistent
                    // tower AOE a hotter core and moving ember cells.
                    if (_ZoneType >= 2.5)
                    {
                        // Persistent tower fire reads more clearly as a restrained hazard
                        // perimeter than as an opaque pool. Two rails preserve the damage
                        // radius while the transparent centre keeps enemies and build pads
                        // legible when several zones overlap.
                        float outerRail =
                            smoothstep(irregularRadius - 0.15, irregularRadius - 0.09, radial) *
                            (1.0 - smoothstep(irregularRadius - 0.035, irregularRadius + 0.018, radial));
                        float innerRail =
                            smoothstep(irregularRadius - 0.29, irregularRadius - 0.245, radial) *
                            (1.0 - smoothstep(irregularRadius - 0.19, irregularRadius - 0.145, radial));
                        float interior = bodyMask * (1.0 - outerRail) * (1.0 - innerRail);

                        float sweep = sin(angle * 2.0 - time * _FlowSpeed * 1.8 +
                            flowNoise * 0.45) * 0.5 + 0.5;
                        float hotSweep = outerRail * smoothstep(0.72, 0.98, sweep);
                        float lowHeat = interior * saturate(0.46 + flowNoise * 0.32);

                        color = lerp(_SecondaryColor.rgb, _Color.rgb,
                            saturate(outerRail + innerRail * 0.72 + lowHeat * 0.22));
                        color = lerp(color, _HotColor.rgb,
                            saturate(hotSweep * 0.72));
                        color += _Color.rgb * outerRail * _RimStrength * 0.12;

                        alpha = _Color.a * pulse *
                            (outerRail * 0.68 + innerRail * 0.22 +
                             lowHeat * 0.075 + hotSweep * 0.14);
                    }
                }

                color *= (0.85 + _EmissionStrength * 0.35 + rimMask * _RimStrength * 0.2);
                return half4(color, saturate(alpha * _LifeAlpha));
            }
            ENDHLSL
        }
    }
}
