Shader "Rouge/Enemy Contact Shadow"
{
    Properties
    {
        _ShadowColor("Shadow Color", Color) = (0.004, 0.012, 0.022, 0.58)
        _ShadowScale("Shadow Width / Length", Vector) = (0.82, 0.52, 0, 0)
        _ScaleMultiplier("Enemy Sprite Scale", Vector) = (1, 1, 0, 0)
        _EnemyTypeSizes("Enemy Type Sizes / Elite", Vector) = (1, 1, 1, 1)
        _GroundHeight("Ground Height", Float) = 0.105
        _InstanceDensity("Rendered Instance Density", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="AlphaTest-30"
            "RenderType"="Transparent"
        }
        Pass
        {
            Name "Enemy Contact Shadow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _PositionScaleBuffer;
            StructuredBuffer<float4> _StateBuffer;
            StructuredBuffer<int> _EnemyKindBuffer;

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor;
                float4 _ShadowScale;
                float4 _ScaleMultiplier;
                float4 _EnemyTypeSizes;
                float _GroundHeight;
                float _InstanceDensity;
            CBUFFER_END

            float4 _RougeLightDirection;
            float _RougeContactShadowStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half visibility : TEXCOORD1;
                half heightFade : TEXCOORD2;
            };

            float HashInstance(uint value)
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215.0;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionScale = _PositionScaleBuffer[input.instanceID];
                float4 state = _StateBuffer[input.instanceID];
                int rawEnemyKind = _EnemyKindBuffer[input.instanceID];
                int enemyKind = rawEnemyKind & 0x3F;

                float visualFlags = floor(max(state.w, 0.0) / 10.0 + 0.001);
                float dead = step(0.5, fmod(floor(visualFlags * 0.5), 2.0));
                float selected = step(HashInstance(input.instanceID), _InstanceDensity);
                float supportedKind = step((float)enemyKind, 2.5);
                output.visibility = selected * supportedKind * (1.0 - dead);
                output.uv = input.uv;

                if (output.visibility < 0.5)
                {
                    output.positionHCS = float4(-2.0, -2.0, -1.0, 1.0);
                    output.heightFade = 0.0;
                    return output;
                }

                float enemyTypeSize = _EnemyTypeSizes.x;
                if (enemyKind == 1) enemyTypeSize = _EnemyTypeSizes.y;
                else if (enemyKind == 2) enemyTypeSize = _EnemyTypeSizes.z;
                if ((rawEnemyKind & 0x40) != 0) enemyTypeSize *= _EnemyTypeSizes.w;

                float height = max(0.0, positionScale.y - _GroundHeight);
                float2 awayFromLight = -_RougeLightDirection.xz;
                float directionLength = max(length(awayFromLight), 0.0001);
                awayFromLight /= directionLength;
                float2 right = float2(awayFromLight.y, -awayFromLight.x);
                float2 shadowSize = max(enemyTypeSize * _ScaleMultiplier.x * _ShadowScale.xy, 0.04);
                shadowSize *= 1.0 + min(height * 0.045, 0.38);
                float2 local = input.positionOS.xy * 2.0 * shadowSize;
                float2 shadowCenter = positionScale.xz + awayFromLight * (0.13 + height * 0.32);
                float2 positionXZ = shadowCenter + right * local.x + awayFromLight * local.y;
                float3 positionWS = float3(positionXZ.x, _GroundHeight, positionXZ.y);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.heightFade = saturate(1.0 - height * 0.105);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.visibility - 0.5h);
                float2 centered = input.uv * 2.0 - 1.0;
                float radiusSquared = dot(centered, centered);
                float softEllipse = 1.0 - smoothstep(0.12, 1.0, radiusSquared);
                softEllipse *= softEllipse;
                half alpha = _ShadowColor.a * softEllipse * input.heightFade *
                    _RougeContactShadowStrength;
                clip(alpha - 0.002h);
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
