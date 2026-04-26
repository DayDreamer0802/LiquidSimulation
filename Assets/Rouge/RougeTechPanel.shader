Shader "Rouge/TechPanel"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.12, 0.62, 0.82, 1)
        _EdgeColor("Edge Color", Color) = (0.9, 0.98, 1.0, 1)
        _Alpha("Alpha", Range(0, 1)) = 0.56
        _LineDensity("Line Density", Range(4, 48)) = 16
        _SweepSpeed("Sweep Speed", Range(0, 6)) = 1.5
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 2.1
        _GlowStrength("Glow Strength", Range(0, 4)) = 1.2
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }
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
            float4 _BaseColor;
            float4 _EdgeColor;
            float _Alpha;
            float _LineDensity;
            float _SweepSpeed;
            float _FresnelPower;
            float _GlowStrength;
            float _NoiseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 34.45);
                return frac(p.x * p.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = SafeNormalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float fresnel = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _FresnelPower);

                float2 uv = input.uv;
                float2 denseUv = uv * float2(_LineDensity, max(4.0, _LineDensity * 0.32));
                float2 fracUv = abs(frac(denseUv) - 0.5);
                float verticalLine = 1.0 - saturate(fracUv.x / 0.09);
                float horizontalLine = 1.0 - saturate(fracUv.y / 0.14);
                float circuit = saturate(max(verticalLine, horizontalLine * 0.85));
                circuit = pow(circuit, 1.8);

                float sweepCenter = frac(_Time.y * _SweepSpeed * 0.12 + uv.y * 0.75 + uv.x * 0.15);
                float sweep = smoothstep(0.08, 0.48, sweepCenter) * (1.0 - smoothstep(0.52, 0.92, sweepCenter));
                float noise = lerp(1.0, 0.82 + Hash21(floor(denseUv) + floor(_Time.y * 3.0)) * 0.28, _NoiseStrength);
                float panelPulse = 0.88 + 0.12 * sin((_Time.y + input.positionOS.y) * 3.4);

                float edgeMask = saturate(1.0 - abs(uv.x * 2.0 - 1.0));
                edgeMask = pow(1.0 - edgeMask, 1.8);
                float3 color = lerp(_BaseColor.rgb, _EdgeColor.rgb, saturate(fresnel * 0.6 + sweep * 0.35 + edgeMask * 0.4));
                color *= (0.45 + fresnel * 0.35 + sweep * 0.22) * panelPulse * noise;
                color += _EdgeColor.rgb * circuit * (0.18 + 0.32 * sweep) * _GlowStrength;
                color += _EdgeColor.rgb * edgeMask * 0.18 * _GlowStrength;

                float alpha = _Alpha * saturate(0.34 + fresnel * 0.32 + circuit * 0.24 + sweep * 0.22);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
