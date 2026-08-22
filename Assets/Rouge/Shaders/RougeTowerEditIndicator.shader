Shader "Rouge/Tower Edit Indicator"
{
    Properties
    {
        _TintColor("Tint", Color) = (1, 0.38, 0.035, 1)
        _Mode("Mode", Float) = 0
        _PulseSpeed("Pulse Speed", Float) = 1
        _RotationSpeed("Rotation Speed", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _Mode;
                float _PulseSpeed;
                float _RotationSpeed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float2 Rotate(float2 value, float angle)
            {
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                return float2(cosine * value.x - sine * value.y,
                    sine * value.x + cosine * value.y);
            }

            float Band(float value, float center, float halfWidth, float feather)
            {
                return 1.0 - smoothstep(halfWidth, halfWidth + feather, abs(value - center));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // "point" is a D3D11 HLSL reserved token, so keep this identifier explicit.
                float2 centeredUv = input.uv * 2.0 - 1.0;
                float feather = max(fwidth(centeredUv.x) + fwidth(centeredUv.y), 0.003);
                float time = _Time.y * max(0.05, _PulseSpeed);
                float pulse = 0.88 + 0.22 * sin(time * 3.4);

                // Selection: four corner brackets, a rotating diamond locator and a
                // faint scan sweep. It reads as a targeting UI rather than a circle.
                float2 absolutePoint = abs(centeredUv);
                float cornerVertical = Band(absolutePoint.x, 0.76, 0.022, feather) *
                    smoothstep(0.4, 0.48, absolutePoint.y) * step(absolutePoint.y, 0.83);
                float cornerHorizontal = Band(absolutePoint.y, 0.76, 0.022, feather) *
                    smoothstep(0.4, 0.48, absolutePoint.x) * step(absolutePoint.x, 0.83);
                float cornerBrackets = saturate(cornerVertical + cornerHorizontal);

                float2 selectedRotated = Rotate(centeredUv, time * _RotationSpeed * 0.48);
                float diamondDistance = abs(selectedRotated.x) + abs(selectedRotated.y);
                float diamondRail = Band(diamondDistance, 0.78, 0.018, feather);
                float diamondSegments = smoothstep(0.25, 0.42,
                    max(abs(selectedRotated.x), abs(selectedRotated.y)));
                diamondRail *= diamondSegments;
                float scanPosition = lerp(-0.52, 0.52, frac(time * 0.18));
                float scan = Band(centeredUv.y, scanPosition, 0.008, feather * 1.5) *
                    (1.0 - smoothstep(0.42, 0.66, abs(centeredUv.x)));
                float selectedAlpha = saturate(cornerBrackets * 0.96 +
                    diamondRail * 0.62 + scan * 0.24) * pulse;

                // Upgrade-ready: two broken rotating hex rails and six travelling
                // energy segments. No continuous green circle is drawn.
                float2 upgradeRotated = Rotate(centeredUv, -time * _RotationSpeed * 0.34);
                float2 upgradeAbs = abs(upgradeRotated);
                float outerHexDistance = max(upgradeAbs.y,
                    dot(upgradeAbs, float2(0.8660254, 0.5)));
                float angle = atan2(upgradeRotated.y, upgradeRotated.x) / 6.2831853 + 0.5;
                float segmentPhase = frac(angle * 6.0 + time * 0.24);
                float segmentMask = smoothstep(0.12, 0.22, segmentPhase) *
                    (1.0 - smoothstep(0.7, 0.82, segmentPhase));
                float outerHex = Band(outerHexDistance, 0.69, 0.026, feather) * segmentMask;
                float innerHex = Band(outerHexDistance, 0.52, 0.016, feather) *
                    smoothstep(0.42, 0.68, segmentPhase) *
                    (1.0 - smoothstep(0.84, 0.94, segmentPhase));
                float node = 1.0 - smoothstep(0.025, 0.075 + feather,
                    length(upgradeRotated - normalize(upgradeRotated +
                        float2(0.0001, 0.0001)) * 0.61));
                float angularNode = 1.0 - smoothstep(0.07, 0.16,
                    abs(frac(angle * 6.0) - 0.5));
                node *= angularNode;
                float upgradeAlpha = saturate(outerHex * 0.92 + innerHex * 0.52 + node * 0.34) * pulse;

                float upgradeMode = step(0.5, _Mode);
                float alpha = lerp(selectedAlpha, upgradeAlpha, upgradeMode) * _TintColor.a;
                half3 glowColor = _TintColor.rgb * (1.12 + alpha * 0.98);
                return half4(glowColor, alpha);
            }
            ENDHLSL
        }
    }
}
