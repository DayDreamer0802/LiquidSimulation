Shader "Rouge/Tech Arena Frame"
{
    Properties
    {
        _BaseColor("Armor Base", Color) = (0.035, 0.075, 0.11, 1)
        _PanelColor("Raised Panel", Color) = (0.07, 0.14, 0.19, 1)
        [HDR] _AccentColor("Energy Accent", Color) = (0.08, 0.82, 1.2, 1)
        _PanelSize("Panel Size", Float) = 8
        _EmissionStrength("Emission Strength", Range(0, 4)) = 1.65
        [Toggle] _AccentOnly("Accent Only", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry-20"
            "RenderType"="Opaque"
        }

        Pass
        {
            Name "Tech Frame"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PanelColor;
                float4 _AccentColor;
                float _PanelSize;
                float _EmissionStrength;
                float _AccentOnly;
            CBUFFER_END

            float _RougeLightingStrength;
            float _RougeTechDetailStrength;
            float _RougeTechAnimationStrength;
            float4 _RougeLightDirection;
            float4 _RougeLightColor;

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirection = SafeNormalize(_WorldSpaceCameraPos - input.positionWS);
                float3 lightDirection = SafeNormalize(_RougeLightDirection.xyz);
                float topFace = smoothstep(0.42, 0.82, normalWS.y);
                float sideFace = 1.0 - topFace;
                float detailQuality = saturate(_RougeTechDetailStrength);

                // World-locked seams give every intersecting rail exactly the same
                // surface result, so convex and concave contour joins stay stable.
                float2 segmentUv = frac((input.positionWS.xz + _PanelSize * 0.17) /
                    max(_PanelSize, 0.1));
                float2 segmentDistance = min(segmentUv, 1.0 - segmentUv);
                float segmentSeam = 1.0 - smoothstep(0.018,
                    0.018 + max(max(fwidth(segmentUv.x), fwidth(segmentUv.y)), 0.002),
                    min(segmentDistance.x, segmentDistance.y));

                float brushed = sin(input.positionWS.x * 1.17 + input.positionWS.z * 0.73) *
                    sin(input.positionWS.x * 0.31 - input.positionWS.z * 0.57) * 0.5 + 0.5;

                float diffuse = saturate(dot(normalWS, lightDirection));
                float3 halfDirection = SafeNormalize(lightDirection + viewDirection);
                float specular = pow(saturate(dot(normalWS, halfDirection)), 34.0);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirection)), 3.0);
                float lightEnergy = lerp(0.82, 1.10, diffuse);
                lightEnergy = lerp(1.0, lightEnergy, saturate(_RougeLightingStrength));

                half sunPeak = max(max(_RougeLightColor.r, _RougeLightColor.g),
                    max(_RougeLightColor.b, 0.001h));
                half3 sunTint = _RougeLightColor.rgb / sunPeak;
                half3 metal = lerp(_BaseColor.rgb, _PanelColor.rgb,
                    0.16 + topFace * 0.21 + brushed * 0.035 * detailQuality);
                metal *= lightEnergy;
                metal *= lerp(1.0h.xxx, sunTint,
                    (half)(diffuse * _RougeLightingStrength * 0.22));
                metal *= lerp(0.64, 1.0, topFace);
                metal = lerp(metal, _BaseColor.rgb * 0.42,
                    segmentSeam * (0.45 + sideFace * 0.2));
                metal += sunTint * specular * (0.10 + detailQuality * 0.14);
                metal += _AccentColor.rgb * fresnel * sideFace * 0.025;

                float accentDash = 0.90 + 0.10 * sin(
                    (input.positionWS.x + input.positionWS.z) * 0.155 -
                    _Time.y * 0.75 * _RougeTechAnimationStrength);
                half3 accentSurface = _AccentColor.rgb *
                    (0.70 + _EmissionStrength * 0.42) * accentDash;
                half3 color = lerp(metal, accentSurface, saturate(_AccentOnly));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
