Shader "Rouge/Tower Placement Pad"
{
    Properties
    {
        _BaseColor("Metal Base", Color) = (0.07, 0.13, 0.18, 1)
        [HDR] _AccentColor("Energy Accent", Color) = (0.08, 0.82, 1, 1)
        [NoScaleOffset] _PlaceIcon("Center Icon", 2D) = "white" {}
        [Toggle] _UsePlaceIcon("Use Center Icon", Float) = 0
        _PlaceIconScale("Center Icon Scale", Range(0.2, 0.9)) = 0.58
        _IconBreathStrength("Icon Breath Strength", Range(0, 0.25)) = 0.14
        _IconBreathScale("Icon Breath Scale", Range(0, 0.08)) = 0.025
        _CellSize("Terrain Cell Size", Float) = 8
        _GridOrigin("Grid Origin", Vector) = (0, 0, 0, 0)
        _FrameWidth("Frame Width", Range(0.005, 0.15)) = 0.035
        _GlowStrength("Glow Strength", Range(0, 4)) = 1.8
        _PulseSpeed("Pulse Speed", Range(0, 4)) = 0.35
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Name "Forward Tech Pad"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_PlaceIcon);
            SAMPLER(sampler_PlaceIcon);

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
                float4 _AccentColor;
                float4 _GridOrigin;
                float _UsePlaceIcon;
                float _PlaceIconScale;
                float _IconBreathStrength;
                float _IconBreathScale;
                float _CellSize;
                float _FrameWidth;
                float _GlowStrength;
                float _PulseSpeed;
            CBUFFER_END

            float _RougeVisualQuality;
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
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float Band(float value, float center, float halfWidth, float feather)
            {
                return 1.0 - smoothstep(halfWidth, halfWidth + feather, abs(value - center));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float safeCellSize = max(_CellSize, 0.001);
                float2 gridPosition = (input.positionWS.xz - _GridOrigin.xy) / safeCellSize;
                float2 footprint = abs(ddx(gridPosition)) + abs(ddy(gridPosition));
                float2 uv = frac(gridPosition);
                float2 edgeDistance = min(uv, 1.0 - uv);
                float feather = max(max(footprint.x, footprint.y), 0.0015);
                float topFace = smoothstep(0.45, 0.8, input.normalWS.y);

                // The old frac(floor + floor) expression was always zero. Cell
                // parity restores a subtle machined checker without a texture.
                float2 checkerCell = floor(uv * 8.0);
                float checker = (fmod(checkerCell.x + checkerCell.y, 2.0) * 2.0 - 1.0) * 0.004;
                float centerShade = saturate(length(uv - 0.5) * 1.15);
                half3 metal = _BaseColor.rgb * (0.94 + checker + centerShade * 0.18);
                metal = lerp(metal * 0.72, metal, topFace);

                // Outer energy rail and a dimmer inset rail make the pad read as machinery.
                float outerFrame = max(
                    1.0 - smoothstep(_FrameWidth, _FrameWidth + feather, edgeDistance.x),
                    1.0 - smoothstep(_FrameWidth, _FrameWidth + feather, edgeDistance.y));
                float insetFrame = max(Band(edgeDistance.x, 0.105, _FrameWidth * 0.45, feather),
                                       Band(edgeDistance.y, 0.105, _FrameWidth * 0.45, feather));
                float detailQuality = saturate(_RougeTechDetailStrength);
                float haloWidth = lerp(0.035, 0.060, detailQuality);
                float borderDistance = min(edgeDistance.x, edgeDistance.y);
                float outerGlowMask = 1.0 - smoothstep(_FrameWidth,
                    _FrameWidth + haloWidth + feather, borderDistance);
                float insetGlowMask = max(
                    Band(edgeDistance.x, 0.105,
                        _FrameWidth * 0.45 + haloWidth * 0.42, feather),
                    Band(edgeDistance.y, 0.105,
                        _FrameWidth * 0.45 + haloWidth * 0.42, feather));

                // Corner power nodes and short circuit traces break up the flat square silhouette.
                float2 cornerDistance = min(uv, 1.0 - uv);
                float cornerNode = 1.0 - smoothstep(0.045, 0.075 + feather,
                    length(cornerDistance - float2(0.115, 0.115)));
                float cornerHalo = 1.0 - smoothstep(0.045,
                    0.075 + haloWidth + feather,
                    length(cornerDistance - float2(0.115, 0.115)));
                float traceMask = step(0.11, edgeDistance.y) * step(edgeDistance.y, 0.16) *
                                  step(0.18, uv.x) * step(uv.x, 0.38);
                traceMask += step(0.11, edgeDistance.x) * step(edgeDistance.x, 0.16) *
                             step(0.62, uv.y) * step(uv.y, 0.82);
                float traceDash = smoothstep(0.35, 0.65,
                    sin((uv.x + uv.y) * 72.0) * 0.5 + 0.5) * saturate(traceMask);

                float bevelX = Band(edgeDistance.x, 0.095, 0.025, feather);
                float bevelY = Band(edgeDistance.y, 0.095, 0.025, feather);
                float wellX = Band(edgeDistance.x, 0.155, 0.020, feather);
                float wellY = Band(edgeDistance.y, 0.155, 0.020, feather);
                float sideX = lerp(-1.0, 1.0, step(0.5, uv.x));
                float sideZ = lerp(-1.0, 1.0, step(0.5, uv.y));
                float slopeX = (bevelX * 0.52 - wellX * 0.18) * sideX;
                float slopeZ = (bevelY * 0.52 - wellY * 0.18) * sideZ;
                float3 fakeNormal = normalize(float3(slopeX, 1.0, slopeZ));
                float directional = saturate(dot(fakeNormal,
                    normalize(_RougeLightDirection.xyz)));
                float sunlight = smoothstep(0.22, 0.88, directional);
                float3 viewDirection = SafeNormalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfDirection = SafeNormalize(
                    normalize(_RougeLightDirection.xyz) + viewDirection);
                float padSpecular = pow(saturate(dot(fakeNormal, halfDirection)), 34.0);
                float fakeLighting = lerp(1.0, lerp(0.80, 1.12, sunlight),
                    _RougeLightingStrength * topFace);
                half realtimeShadow = 1.0h;
                if (_RougeVisualQuality > 0.5)
                {
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                    realtimeShadow = mainLight.shadowAttenuation;
                }
                float shadowLighting = lerp(1.0, 0.68 + realtimeShadow * 0.32,
                    _RougeLightingStrength * topFace);
                half sunPeak = max(max(_RougeLightColor.r, _RougeLightColor.g),
                    max(_RougeLightColor.b, 0.001h));
                half3 normalizedSun = _RougeLightColor.rgb / sunPeak;
                half3 lightTint = lerp(half3(0.88h, 0.94h, 1.04h),
                    normalizedSun, sunlight);
                lightTint = lerp(1.0h.xxx, lightTint,
                    (half)(_RougeLightingStrength * 0.24 * topFace));
                lightTint *= lerp(0.96h, clamp(sunPeak, 0.82h, 1.20h),
                    (half)(sunlight * _RougeLightingStrength * topFace));
                float cavity = saturate(outerFrame * 0.22 + insetFrame * 0.42 + cornerNode * 0.18);
                float wellInterior = smoothstep(0.13, 0.19,
                    min(edgeDistance.x, edgeDistance.y));
                metal *= fakeLighting * shadowLighting * lightTint *
                    (1.0 - cavity * (0.12 + _RougeLightingStrength * 0.12));
                metal *= 1.0 - wellInterior * 0.045 * topFace;
                half3 padSpecularTint = lerp(_BaseColor.rgb, _AccentColor.rgb, 0.08);
                metal += padSpecularTint * padSpecular * shadowLighting *
                    (0.12 + _RougeTechDetailStrength * 0.10) *
                    _RougeLightingStrength * topFace;

                float cellSeed = frac(sin(dot(floor(gridPosition), float2(12.9898, 78.233))) * 43758.5453);
                float breathWave = sin(_Time.y * _PulseSpeed * 6.28318 +
                    cellSeed * 6.28318) * _RougeTechAnimationStrength;
                float pulse = 0.76 + 0.24 * breathWave;
                float qualityDetail = 0.48 + _RougeTechDetailStrength * 0.52;
                float energy = saturate(outerFrame + insetFrame * 0.48 +
                    cornerNode * qualityDetail + traceDash * 0.55 * qualityDetail) * topFace;
                float coreGain = 1.20 + _GlowStrength * pulse *
                    (0.90 + detailQuality * 0.25);
                half3 color = metal + _AccentColor.rgb * energy * coreGain;
                // A broad, low-energy analytic halo gives HDR bloom something to
                // integrate without turning the crisp rail itself into a white strip.
                float railHalo = saturate(
                    (outerGlowMask - outerFrame) * 0.85 +
                    (insetGlowMask - insetFrame) * 0.55 +
                    (cornerHalo - cornerNode) * 0.40);
                railHalo *= lerp(0.58, 1.0,
                    saturate(1.0 - feather * 1.5));
                float haloGain = 0.10 + _GlowStrength * 0.10;
                color += _AccentColor.rgb * railHalo * haloGain *
                    qualityDetail * topFace;

                // A configured white-alpha icon replaces the original center reactor.
                // Keeping this as a material switch preserves the old appearance for
                // every tile definition that has no texture assigned.
                float reactor = 1.0 - smoothstep(0.0, 0.28, length(uv - 0.5));
                float usePlaceIcon = step(0.5, _UsePlaceIcon);
                float breathingIconScale = _PlaceIconScale *
                    (1.0 + breathWave * _IconBreathScale);
                float2 iconUv = (uv - 0.5) / max(breathingIconScale, 0.001) + 0.5;
                float2 iconLowerBound = step(0.0, iconUv);
                float2 iconUpperBound = step(iconUv, 1.0);
                float iconInside = iconLowerBound.x * iconLowerBound.y *
                                   iconUpperBound.x * iconUpperBound.y;
                // Four small footprint-aware samples keep angled/minified icons as
                // smooth as the shader-drawn shapes without producing a wide blur.
                float2 iconSampleOffset = max(fwidth(iconUv) * 0.28,
                    float2(0.0004, 0.0004));
                half iconAlpha = (
                    SAMPLE_TEXTURE2D(_PlaceIcon, sampler_PlaceIcon,
                        saturate(iconUv + float2(-iconSampleOffset.x, -iconSampleOffset.y))).a +
                    SAMPLE_TEXTURE2D(_PlaceIcon, sampler_PlaceIcon,
                        saturate(iconUv + float2( iconSampleOffset.x, -iconSampleOffset.y))).a +
                    SAMPLE_TEXTURE2D(_PlaceIcon, sampler_PlaceIcon,
                        saturate(iconUv + float2(-iconSampleOffset.x,  iconSampleOffset.y))).a +
                    SAMPLE_TEXTURE2D(_PlaceIcon, sampler_PlaceIcon,
                        saturate(iconUv + float2( iconSampleOffset.x,  iconSampleOffset.y))).a
                ) * 0.25 * iconInside;
                float breath01 = breathWave * 0.5 + 0.5;
                float iconBreath = 1.0 + breathWave * _IconBreathStrength;
                float centerBreath = 0.76 + breathWave * 0.24;
                // Shift gently between a slightly richer version of the configured
                // accent and the accent itself. This reads as energy changing color
                // without introducing white highlights or a blurred outer edge.
                half3 richerIconColor = _AccentColor.rgb * _AccentColor.rgb;
                half3 breathingIconColor = lerp(richerIconColor, _AccentColor.rgb,
                    0.82 + breath01 * 0.18);
                float centerAura = 1.0 - smoothstep(0.10, 0.34, length(uv - 0.5));
                color += _AccentColor.rgb * centerAura *
                    (0.035 + _GlowStrength * 0.015) * centerBreath * topFace;
                color += _AccentColor.rgb * reactor * 0.11 * centerBreath * topFace *
                         (1.0 - usePlaceIcon);
                color += breathingIconColor * iconAlpha *
                         (0.95 + _GlowStrength * 0.38) * iconBreath *
                         topFace * usePlaceIcon;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
