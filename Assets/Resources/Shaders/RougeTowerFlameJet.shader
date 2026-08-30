Shader "Rouge/TowerFlameJet"
{
    Properties
    {
        [HDR] _CoreColor("White-hot Core", Color) = (2.8, 2.35, 0.88, 1)
        [HDR] _FlameColor("Flame Body", Color) = (2.25, 0.42, 0.035, 1)
        [HDR] _EdgeColor("Ember Edge", Color) = (0.82, 0.025, 0.004, 1)
        _FlowSpeed("Flow Speed", Range(0.1, 10.0)) = 3.8
        _Turbulence("Turbulence", Range(0.0, 0.7)) = 0.32
        _SparkIntensity("Spark Intensity", Range(0.0, 3.0)) = 1.25
        _Intensity("Intensity", Range(0.0, 3.0)) = 1.05
        [PerRendererData] _Phase("Ribbon Phase", Float) = 0
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

        Pass
        {
            Name "ProceduralFlameRibbon"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _FlameColor;
                float4 _EdgeColor;
                float _FlowSpeed;
                float _Turbulence;
                float _SparkIntensity;
                float _Intensity;
                float _Phase;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 local = frac(value);
                local = local * local * (3.0 - 2.0 * local);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
            }

            float FlameNoise(float2 value)
            {
                float noise = ValueNoise(value) * 0.68;
                noise += ValueNoise(value * 2.07 + 17.13) * 0.32;
                return noise;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float along = saturate(input.uv.x);
                float across = input.uv.y * 2.0 - 1.0;
                float time = _Time.y * _FlowSpeed + _Phase;

                float broadNoise = FlameNoise(float2(
                    along * 5.8 - time * 0.92,
                    across * 1.75 + _Phase * 0.17));
                float detailNoise = ValueNoise(float2(
                    along * 11.2 - time * 1.63 + 5.7,
                    across * 3.4 - _Phase * 0.11));
                float warp = (broadNoise - 0.5) * _Turbulence *
                    (0.28 + along * 0.72);
                float distanceFromCore = abs(across + warp);

                float lickingEdge = 0.76 + (detailNoise - 0.5) * 0.38 +
                    sin(along * 31.0 - time * 4.1 + _Phase) * 0.055;
                float outer = 1.0 - smoothstep(lickingEdge - 0.17,
                    lickingEdge + 0.06, distanceFromCore);
                float body = 1.0 - smoothstep(0.24, 0.63,
                    distanceFromCore + (detailNoise - 0.5) * 0.11);
                float core = 1.0 - smoothstep(0.055, 0.235,
                    distanceFromCore + (broadNoise - 0.5) * 0.07);

                // Cut the wide telegraph ribbon into several licking strands. The
                // geometry still covers the gameplay cone, but it no longer reads as
                // a single translucent triangle.
                float tonguePhase = across * 7.4 + broadNoise * 2.2 +
                    sin(along * 17.0 - time * 2.1) * 0.52;
                float strand = smoothstep(0.06, 0.43,
                    abs(sin(tonguePhase)));
                float strandBlend = smoothstep(0.20, 0.88, along);
                outer *= lerp(1.0, 0.56 + strand * 0.44, strandBlend);
                body *= lerp(1.0, 0.70 + strand * 0.30, strandBlend);

                float tailBreakup = saturate((1.0 - along) * 5.5 +
                    (detailNoise - 0.43) * 1.75);
                float muzzle = smoothstep(0.0, 0.035, along + 0.014);
                float packets = pow(saturate(sin(along * 38.0 - time * 8.5 +
                    detailNoise * 6.0) * 0.5 + 0.5), 16.0);
                packets *= saturate(body + core) * _SparkIntensity;
                float edgeSparks = pow(saturate(detailNoise - 0.66) / 0.34,
                    5.0) * outer * (0.2 + along * 0.8) * _SparkIntensity;

                float mask = saturate(outer * 0.72 + body * 0.55 +
                    core * 0.85 + packets * 0.42 + edgeSparks * 0.34);
                mask *= tailBreakup * muzzle * saturate(input.color.a);
                clip(mask - 0.006);

                float pulse = 0.91 + sin(time * 2.7 - along * 16.0) * 0.09;
                float3 emission = _EdgeColor.rgb * outer * 0.52;
                emission += _FlameColor.rgb * body *
                    (0.78 + broadNoise * 0.52);
                emission += _CoreColor.rgb * core *
                    (1.15 + packets * 0.78);
                emission += lerp(_FlameColor.rgb, _CoreColor.rgb, 0.72) *
                    (packets + edgeSparks * 0.55);
                emission *= pulse * _Intensity;

                return half4(emission, saturate(mask * _Intensity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
