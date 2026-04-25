Shader "Rouge/Hologram"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.15, 0.85, 1.0, 1.0)
        _AccentColor("Accent Color", Color) = (0.95, 1.0, 1.0, 1.0)
        _Alpha("Alpha", Range(0, 1)) = 0.7
        _ScanlineDensity("Scanline Density", Range(4, 80)) = 18
        _ScanlineSpeed("Scanline Speed", Range(0, 10)) = 2.2
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 2.4
        _GlowStrength("Glow Strength", Range(0, 8)) = 2.2
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.16
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "RenderType" = "Transparent" }
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
            float4 _BaseColor;
            float4 _AccentColor;
            float _Alpha;
            float _ScanlineDensity;
            float _ScanlineSpeed;
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
                float3 positionOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.uv = input.uv;
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = SafeNormalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float fresnel = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _FresnelPower);

                float scanlinePhase = input.positionOS.y * _ScanlineDensity - _Time.y * _ScanlineSpeed;
                float scanlines = 0.55 + 0.45 * sin(scanlinePhase * 6.28318);
                float2 noiseUv = input.positionOS.xz * 1.7 + _Time.y * 0.35;
                float noise = lerp(1.0, Hash21(noiseUv), _NoiseStrength);
                float rimPulse = 0.72 + 0.28 * sin((_Time.y + input.positionOS.y) * 4.1);

                float glow = (0.35 + fresnel * _GlowStrength) * scanlines * rimPulse * noise;
                float3 color = lerp(_BaseColor.rgb, _AccentColor.rgb, saturate(fresnel * 0.85 + scanlines * 0.25));
                color *= glow;

                float alpha = _Alpha * saturate(0.25 + fresnel * 0.85) * saturate(0.75 + scanlines * 0.25);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}