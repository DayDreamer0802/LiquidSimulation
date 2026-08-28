Shader "Rouge/Tower Upgrade VFX"
{
    Properties
    {
        [HDR] _PrimaryColor("Primary Cyan", Color) = (0.125, 0.875, 1.0, 1.0)
        [HDR] _SecondaryColor("Commit Gold", Color) = (1.0, 0.67, 0.20, 1.0)
        [HDR] _AccentColor("Branch Purple", Color) = (0.72, 0.36, 1.0, 1.0)
        _Progress("Progress", Range(0, 1)) = 0
        [Enum(GroundCircuit, 0, EnergyColumn, 1, Shockwave, 2, SparkCard, 3)] _Mode("Mode", Float) = 0
        _Intensity("Intensity", Range(0, 8)) = 2.2
        _Opacity("Opacity", Range(0, 1)) = 1
        _Softness("Edge Softness", Range(0.25, 3)) = 1
        _Seed("Variation Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+30"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "TowerUpgradeVFX"
            Tags { "LightMode" = "UniversalForward" }

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
                float4 _PrimaryColor;
                float4 _SecondaryColor;
                float4 _AccentColor;
                float _Progress;
                float _Mode;
                float _Intensity;
                float _Opacity;
                float _Softness;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct EffectSample
            {
                float3 color;
                float alpha;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float2 Rotate2D(float2 value, float angle)
            {
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                return float2(
                    cosine * value.x - sine * value.y,
                    sine * value.x + cosine * value.y);
            }

            float Hash11(float value)
            {
                value = frac(value * 0.1031);
                value *= value + 33.33;
                value *= value + value;
                return frac(value);
            }

            float Window(float value, float fadeInStart, float fadeInEnd,
                float fadeOutStart, float fadeOutEnd)
            {
                return smoothstep(fadeInStart, fadeInEnd, value) *
                    (1.0 - smoothstep(fadeOutStart, fadeOutEnd, value));
            }

            float Band(float value, float center, float halfWidth, float softness)
            {
                float antiAlias = max(fwidth(value) * softness, 0.0015);
                return 1.0 - smoothstep(halfWidth, halfWidth + antiAlias,
                    abs(value - center));
            }

            float Fill(float signedDistance, float softness)
            {
                float antiAlias = max(fwidth(signedDistance) * softness, 0.0015);
                return 1.0 - smoothstep(0.0, antiAlias, signedDistance);
            }

            float HexDistance(float2 value)
            {
                value = abs(value);
                return max(value.y, dot(value, float2(0.8660254, 0.5)));
            }

            EffectSample GroundCircuit(float2 uv, float progress)
            {
                EffectSample result;
                float phase = smoothstep(0.0, 0.42, progress);
                float active = Window(progress, 0.0, 0.055, 0.38, 0.50);
                float commit = 1.0 - smoothstep(0.0, 0.065, abs(progress - 0.46));
                float2 rotated = Rotate2D(uv, phase * -1.15 - _Seed * 0.071);
                float radius = length(uv);
                float angle = atan2(rotated.y, rotated.x) / 6.2831853 + 0.5;

                // The lock rails converge onto the tower before the visual commit.
                float lockRadius = lerp(0.94, 0.55, phase);
                float hexDistance = HexDistance(rotated);
                float outerRail = Band(hexDistance, lockRadius, 0.020, _Softness);
                float innerRail = Band(hexDistance, lockRadius - 0.095, 0.010, _Softness);

                // Broken rails and a clockwise packet prevent the glyph reading as a
                // generic selection circle.
                float segmentPhase = frac(angle * 6.0 - phase * 2.4 + _Seed * 0.13);
                float brokenSegments = smoothstep(0.08, 0.16, segmentPhase) *
                    (1.0 - smoothstep(0.72, 0.84, segmentPhase));
                float energyPacket = 1.0 - smoothstep(0.0, 0.085,
                    abs(segmentPhase - 0.34));
                outerRail *= 0.26 + brokenSegments * 0.74;
                innerRail *= 0.20 + brokenSegments * 0.48 + energyPacket * 0.92;

                float spokeAngle = abs(frac(angle * 6.0 + 0.5) - 0.5);
                float spokes = (1.0 - smoothstep(0.035, 0.075, spokeAngle)) *
                    smoothstep(0.19, 0.28, radius) *
                    (1.0 - smoothstep(lockRadius - 0.14, lockRadius - 0.05, radius));
                float coreRail = Band(radius, 0.255, 0.011, _Softness) *
                    (0.25 + brokenSegments * 0.75);
                float node = Band(radius, lockRadius - 0.035, 0.018, _Softness) *
                    (1.0 - smoothstep(0.045, 0.095, spokeAngle));

                float cyanShape = outerRail * 0.82 + innerRail * 0.62 +
                    spokes * 0.42 + coreRail * 0.44;
                float hotShape = energyPacket * innerRail * 1.35 + node * 0.72 +
                    commit * (coreRail + innerRail) * 1.4;
                float purpleTrim = innerRail * (1.0 - energyPacket) * 0.18;

                result.color = _PrimaryColor.rgb * cyanShape +
                    _SecondaryColor.rgb * hotShape +
                    _AccentColor.rgb * purpleTrim;
                result.alpha = saturate(cyanShape * 0.78 + hotShape + purpleTrim) *
                    max(active, commit * 0.72);
                return result;
            }

            EffectSample EnergyColumn(float2 uv, float progress)
            {
                EffectSample result;
                float active = Window(progress, 0.10, 0.18, 0.52, 0.66);
                float phase = smoothstep(0.12, 0.62, progress);
                float commit = 1.0 - smoothstep(0.0, 0.07, abs(progress - 0.46));
                float y01 = uv.y * 0.5 + 0.5;
                float absoluteX = abs(uv.x);

                // Crossed quads form one soft, readable light pillar. Keep the core
                // translucent so the tower art remains legible through the beam.
                float verticalFade = smoothstep(0.0, 0.06, y01) *
                    (1.0 - smoothstep(0.94, 1.0, y01));
                float revealed = 1.0 - smoothstep(phase, phase + 0.035, y01);
                float scanHead = Band(y01, phase, 0.012, _Softness);

                float innerRail = Band(absoluteX, 0.28, 0.012, _Softness);
                float outerRail = Band(absoluteX, 0.54, 0.016, _Softness);
                float softField = pow(saturate(1.0 - absoluteX / 0.68), 2.2);
                float softCore = pow(saturate(1.0 - absoluteX / 0.24), 2.8);

                float packetPhase = frac(y01 * 8.0 - phase * 5.0 + _Seed * 0.17);
                float packets = smoothstep(0.08, 0.18, packetPhase) *
                    (1.0 - smoothstep(0.48, 0.66, packetPhase));
                packets *= Band(absoluteX, 0.42, 0.075, _Softness);

                float rails = (innerRail * 0.9 + outerRail * 0.62) * revealed;
                float field = softField * revealed * (0.075 + softCore * 0.13 +
                    packets * 0.07);
                float head = scanHead *
                    (1.0 - smoothstep(0.54, 0.82, absoluteX));
                float goldHead = head * (0.25 + commit * 1.45);
                float purplePackets = packets * revealed * 0.12;

                result.color = _PrimaryColor.rgb * (rails + field + head * 0.72) +
                    _SecondaryColor.rgb * goldHead +
                    _AccentColor.rgb * purplePackets;
                result.alpha = saturate(rails * 0.74 + field + head +
                    goldHead * 0.75 + purplePackets) * active * verticalFade;
                return result;
            }

            EffectSample Shockwave(float2 uv, float progress)
            {
                EffectSample result;
                float active = Window(progress, 0.42, 0.47, 0.76, 0.84);
                float phase = saturate((progress - 0.43) / 0.39);
                float radius = lerp(0.18, 1.08, phase);
                float width = lerp(0.060, 0.014, phase);
                float radial = length(uv);
                float ring = Band(radial, radius, width, _Softness);
                float echoRing = Band(radial, radius * 0.76, width * 0.42,
                    _Softness) * (1.0 - phase) * 0.48;

                float angle = atan2(uv.y, uv.x) / 6.2831853 + 0.5;
                float segmentPhase = frac(angle * 12.0 + _Seed * 0.07);
                float segments = smoothstep(0.035, 0.10, segmentPhase) *
                    (1.0 - smoothstep(0.80, 0.92, segmentPhase));
                float whiteCore = 1.0 - smoothstep(0.0, 0.075, progress - 0.43);
                float goldAmount = smoothstep(0.43, 0.53, progress);

                float shape = ring * (0.50 + segments * 0.50) + echoRing;
                float3 waveColor = lerp(_PrimaryColor.rgb, _SecondaryColor.rgb,
                    goldAmount);
                waveColor = lerp(waveColor, 1.0.xxx * 1.35, whiteCore);
                result.color = waveColor * shape +
                    _AccentColor.rgb * echoRing * 0.18;
                result.alpha = saturate(shape * (1.15 - phase * 0.24)) * active;
                return result;
            }

            EffectSample SparkCard(float2 uv, float progress)
            {
                EffectSample result;
                float active = Window(progress, 0.44, 0.50, 0.84, 0.96);
                float phase = saturate((progress - 0.45) / 0.50);
                float randomA = Hash11(_Seed + 1.71);
                float randomB = Hash11(_Seed + 8.43);
                float angle = lerp(-0.62, 0.62, randomA) +
                    sin((_Seed + 0.37) * 2.17) * 0.18;
                float2 sparkUv = Rotate2D(uv, angle);

                // Each quad is a textureless light spindle. The seed varies its length,
                // tilt and tiny satellite mote so a small radial burst stays organic.
                sparkUv.x -= lerp(-0.18, 0.25, phase);
                float lengthScale = lerp(0.48, 0.76, randomB) *
                    lerp(1.0, 0.56, phase);
                float widthScale = lerp(0.095, 0.16, randomA) *
                    lerp(1.0, 0.64, phase);
                float diamondDistance = abs(sparkUv.x) / max(lengthScale, 0.01) +
                    abs(sparkUv.y) / max(widthScale, 0.01) - 1.0;
                float spindle = Fill(diamondDistance, _Softness);

                float coreDistance = abs(sparkUv.x) / max(lengthScale * 0.68, 0.01) +
                    abs(sparkUv.y) / max(widthScale * 0.26, 0.01) - 1.0;
                float core = Fill(coreDistance, _Softness);
                float tail = Band(sparkUv.y, 0.0, widthScale * 0.18,
                    _Softness) * smoothstep(-lengthScale, -lengthScale * 0.12,
                    sparkUv.x) * (1.0 - smoothstep(-0.06, 0.12, sparkUv.x));

                float2 motePosition = float2(-0.44 + randomA * 0.18,
                    (randomB - 0.5) * 0.48);
                float moteDistance = length(sparkUv - motePosition) -
                    lerp(0.025, 0.052, randomB);
                float mote = Fill(moteDistance, _Softness) * (1.0 - phase);
                float hot = core * (1.0 - smoothstep(0.45, 0.86, phase));

                result.color = _SecondaryColor.rgb * (spindle * 0.82 + hot * 0.75) +
                    _PrimaryColor.rgb * (tail * 0.72 + mote) +
                    _AccentColor.rgb * spindle * 0.10;
                result.alpha = saturate(spindle * 0.88 + core + tail * 0.48 + mote) *
                    active;
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * 2.0 - 1.0;
                float progress = saturate(_Progress);
                EffectSample effect;

                if (_Mode < 0.5)
                    effect = GroundCircuit(uv, progress);
                else if (_Mode < 1.5)
                    effect = EnergyColumn(uv, progress);
                else if (_Mode < 2.5)
                    effect = Shockwave(uv, progress);
                else
                    effect = SparkCard(uv, progress);

                float alpha = saturate(effect.alpha * _Opacity);
                clip(alpha - 0.001);
                return half4(effect.color * max(_Intensity, 0.0), alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
