Shader "Rouge/Hologram"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.15, 0.85, 1.0, 1.0)
        _AccentColor("Accent Color", Color) = (0.95, 1.0, 1.0, 1.0)
        _Alpha("Alpha", Range(0, 1)) = 0.7
        _ScanlineDensity("Scanline Density", Range(4, 80)) = 18
        _ScanlineSpeed("Scanline Speed", Range(0, 10)) = 2.2
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 2.4
        _GlowStrength("Glow Strength", Range(0, 8)) = 2.2
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.16
        _DissolveProgress("Dissolve Progress", Range(0, 1)) = 1
        _GridDensity("Grid Density", Range(2, 40)) = 9
        _DissolveEdgeWidth("Dissolve Edge Width", Range(0.01, 0.45)) = 0.14
        _DissolveGlow("Dissolve Glow", Range(0, 4)) = 1.45
        [HideInInspector] _LifecycleAlpha("Lifecycle Alpha", Range(0, 1)) = 1
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _AccentColor;
            float _Alpha;
            float _ScanlineDensity;
            float _ScanlineSpeed;
            float _FresnelPower;
            float _GlowStrength;
            float _NoiseStrength;
            float _DissolveProgress;
            float _GridDensity;
            float _DissolveEdgeWidth;
            float _DissolveGlow;
            float _LifecycleAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 color : COLOR;
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
                output.color = input.color;
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 authoredShape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(authoredShape.a * input.color.a - 0.02h);
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = SafeNormalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float fresnel = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _FresnelPower);

                float scanlinePhase = input.positionOS.y * _ScanlineDensity - _Time.y * _ScanlineSpeed;
                float scanlines = 0.55 + 0.45 * sin(scanlinePhase * 6.28318);
                float2 noiseUv = input.positionOS.xz * 1.7 + _Time.y * 0.35;
                float noise = lerp(1.0, Hash21(noiseUv), _NoiseStrength);
                float rimPulse = 0.72 + 0.28 * sin((_Time.y + input.positionOS.y) * 4.1);

                float2 gridUv = input.uv * float2(_GridDensity, max(2.0, _GridDensity * 0.35));
                float2 gridFrac = abs(frac(gridUv) - 0.5);
                float latticeX = 1.0 - saturate(gridFrac.x / 0.16);
                float latticeY = 1.0 - saturate(gridFrac.y / 0.16);
                float lattice = saturate(max(latticeX, latticeY));
                lattice = pow(lattice, 2.8);
                float2 cellId = floor(gridUv);
                float cellNoise = Hash21(cellId + float2(floor(abs(input.positionOS.y) * 3.0), floor(length(input.positionOS.xz) * 2.0)));
                float edgeWidth = max(_DissolveEdgeWidth, 0.001);
                float dissolve = smoothstep(cellNoise - edgeWidth, cellNoise + edgeWidth, saturate(_DissolveProgress));
                float dissolveEdge = 1.0 - saturate(abs(saturate(_DissolveProgress) - cellNoise) / edgeWidth);
                clip(dissolve - 0.02);

                float glow = (0.35 + fresnel * _GlowStrength) * scanlines * rimPulse * noise;
                float3 color = lerp(_BaseColor.rgb, _AccentColor.rgb, saturate(fresnel * 0.85 + scanlines * 0.25));
                color += _AccentColor.rgb * (lattice * 0.32 + dissolveEdge * _DissolveGlow * (0.45 + lattice * 0.55));
                color *= glow * lerp(1.0h.xxx, authoredShape.rgb, 0.28h);

                float alpha = _Alpha * saturate(0.25 + fresnel * 0.85) * saturate(0.75 + scanlines * 0.25);
                alpha *= dissolve * saturate(0.82 + lattice * 0.18) *
                    authoredShape.a * input.color.a * _LifecycleAlpha;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
