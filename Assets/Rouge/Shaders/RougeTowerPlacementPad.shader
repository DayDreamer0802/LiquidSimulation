Shader "Rouge/Tower Placement Pad"
{
    Properties
    {
        _BaseColor("Metal Base", Color) = (0.07, 0.13, 0.18, 1)
        _AccentColor("Energy Accent", Color) = (0.08, 0.82, 1, 1)
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
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PlaceIcon);
            SAMPLER(sampler_PlaceIcon);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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

            Varyings Vert(Attributes input)
            {
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
                float2 uv = frac(gridPosition);
                float2 edgeDistance = min(uv, 1.0 - uv);
                float feather = max(fwidth(uv.x) + fwidth(uv.y), 0.0015);
                float topFace = smoothstep(0.45, 0.8, input.normalWS.y);

                // Recessed metal panel with a subtle brushed checker pattern.
                float checker = frac(floor(uv.x * 8.0) + floor(uv.y * 8.0)) * 0.012;
                float centerShade = saturate(length(uv - 0.5) * 1.15);
                half3 metal = _BaseColor.rgb * (0.94 + checker + centerShade * 0.18);
                metal = lerp(metal * 0.72, metal, topFace);

                // Outer energy rail and a dimmer inset rail make the pad read as machinery.
                float outerFrame = max(
                    1.0 - smoothstep(_FrameWidth, _FrameWidth + feather, edgeDistance.x),
                    1.0 - smoothstep(_FrameWidth, _FrameWidth + feather, edgeDistance.y));
                float insetFrame = max(Band(edgeDistance.x, 0.105, _FrameWidth * 0.45, feather),
                                       Band(edgeDistance.y, 0.105, _FrameWidth * 0.45, feather));

                // Corner power nodes and short circuit traces break up the flat square silhouette.
                float2 cornerDistance = min(uv, 1.0 - uv);
                float cornerNode = 1.0 - smoothstep(0.045, 0.075 + feather,
                    length(cornerDistance - float2(0.115, 0.115)));
                float traceMask = step(0.11, edgeDistance.y) * step(edgeDistance.y, 0.16) *
                                  step(0.18, uv.x) * step(uv.x, 0.38);
                traceMask += step(0.11, edgeDistance.x) * step(edgeDistance.x, 0.16) *
                             step(0.62, uv.y) * step(uv.y, 0.82);
                float traceDash = smoothstep(0.35, 0.65,
                    sin((uv.x + uv.y) * 72.0) * 0.5 + 0.5) * saturate(traceMask);

                float cellSeed = frac(sin(dot(floor(gridPosition), float2(12.9898, 78.233))) * 43758.5453);
                float breathWave = sin(_Time.y * _PulseSpeed * 6.28318 +
                    cellSeed * 6.28318);
                float pulse = 0.76 + 0.24 * breathWave;
                float energy = saturate(outerFrame + insetFrame * 0.48 + cornerNode + traceDash * 0.55) * topFace;
                half3 accent = _AccentColor.rgb * (0.82 + _GlowStrength * pulse);
                half3 color = metal + accent * energy;

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
                color += _AccentColor.rgb * reactor * 0.11 * centerBreath * topFace *
                         (1.0 - usePlaceIcon);
                color += breathingIconColor * iconAlpha * 0.88 * iconBreath *
                         topFace * usePlaceIcon;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
