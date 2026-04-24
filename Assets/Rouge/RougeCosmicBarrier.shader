Shader "Rouge/CosmicBarrier"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.04, 0.09, 0.16, 0.78)
        _NebulaColorA("Nebula Color A", Color) = (0.16, 0.38, 0.74, 1)
        _NebulaColorB("Nebula Color B", Color) = (0.42, 0.78, 1.0, 1)
        _RimColor("Rim Color", Color) = (0.92, 0.98, 1.0, 1)
        _LineColor("Line Color", Color) = (0.56, 0.86, 1.0, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.72
        _RimPower("Rim Power", Range(0.5, 8)) = 2.7
        _NoiseScale("Noise Scale", Float) = 0.18
        _FlowSpeed("Flow Speed", Float) = 0.14
        _StripeScale("Stripe Scale", Float) = 4.4
        _StripeStrength("Stripe Strength", Range(0, 2)) = 0.7
        _IntersectionGlow("Intersection Glow", Range(0, 2)) = 0.38
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _NebulaColorA;
            float4 _NebulaColorB;
            float4 _RimColor;
            float4 _LineColor;
            float _Opacity;
            float _RimPower;
            float _NoiseScale;
            float _FlowSpeed;
            float _StripeScale;
            float _StripeStrength;
            float _IntersectionGlow;
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
                float3 viewDirWS : TEXCOORD2;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float2 smooth = local * local * (3.0 - 2.0 * local);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, smooth.x), lerp(c, d, smooth.x), smooth.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                value += amplitude * Noise(p); p = p * 2.03 + 11.4; amplitude *= 0.5;
                value += amplitude * Noise(p); p = p * 2.01 + 7.2; amplitude *= 0.5;
                value += amplitude * Noise(p); p = p * 2.02 + 17.9; amplitude *= 0.5;
                value += amplitude * Noise(p);
                return value;
            }

            float TriplanarField(float3 positionWS, float3 normalWS, float scale, float timeOffset)
            {
                float3 blend = pow(abs(normalWS), 4.0);
                blend /= max(dot(blend, 1.0), 1e-4);

                float x = Fbm(positionWS.yz * scale + timeOffset);
                float y = Fbm(positionWS.xz * scale - timeOffset * 0.7);
                float z = Fbm(positionWS.xy * scale + timeOffset * 1.2);
                return x * blend.x + y * blend.y + z * blend.z;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalize(normalInputs.normalWS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(positionInputs.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y * _FlowSpeed;
                float nebula = TriplanarField(input.positionWS, input.normalWS, _NoiseScale, time);
                float nebula2 = TriplanarField(input.positionWS.zxy + 13.7, input.normalWS, _NoiseScale * 1.9, -time * 0.6);
                float nebulaMask = saturate((nebula * 0.85 + nebula2 * 0.45 - 0.38) * 1.9 + 0.2);

                float stripe = 1.0 - smoothstep(0.16, 0.5, abs(frac(input.positionWS.y * _StripeScale + nebula * 0.45 + time * 0.4) - 0.5) / max(fwidth(input.positionWS.y * _StripeScale), 1e-4));
                float crossX = 1.0 - smoothstep(0.22, 0.95, abs(frac(input.positionWS.x * 0.55 + nebula2 * 0.2) - 0.5) / max(fwidth(input.positionWS.x * 0.55), 1e-4));
                float crossZ = 1.0 - smoothstep(0.22, 0.95, abs(frac(input.positionWS.z * 0.55 - nebula * 0.2) - 0.5) / max(fwidth(input.positionWS.z * 0.55), 1e-4));
                float gridGlow = max(crossX, crossZ) * _IntersectionGlow;

                float fresnel = pow(1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS))), _RimPower);
                float3 nebulaColor = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, saturate(nebula2 * 1.15));

                float3 color = _BaseColor.rgb;
                color += nebulaColor * nebulaMask * 0.95;
                color += _LineColor.rgb * (stripe * _StripeStrength + gridGlow);
                color += _RimColor.rgb * (fresnel * 1.35 + nebulaMask * 0.14);

                float alpha = saturate(_Opacity * (0.48 + fresnel * 0.52 + stripe * 0.18 + nebulaMask * 0.16));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}