Shader "Rouge/EnemyBillboard"
{
    Properties
    {
        _MainTex("Enemy Sprite", 2D) = "white" {}
        _EnemySheet0("Enemy Sheet 0", 2D) = "white" {}
        _EnemySheet1("Enemy Sheet 1", 2D) = "white" {}
        _EnemySheet2("Enemy Sheet 2", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _ScaleMultiplier("Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _PositionScaleBuffer;
            StructuredBuffer<float4> _StateBuffer;
            StructuredBuffer<float4> _VelocityBuffer;
            StructuredBuffer<int> _EnemyKindBuffer;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_EnemySheet0);
            SAMPLER(sampler_EnemySheet0);
            TEXTURE2D(_EnemySheet1);
            SAMPLER(sampler_EnemySheet1);
            TEXTURE2D(_EnemySheet2);
            SAMPLER(sampler_EnemySheet2);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _ScaleMultiplier;
            CBUFFER_END

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
                float flash : TEXCOORD1;
                float dead : TEXCOORD2;
                float curse : TEXCOORD3;
                float slow : TEXCOORD4;
                nointerpolation float enemyKind : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionScale = _PositionScaleBuffer[input.instanceID];
                float4 state = _StateBuffer[input.instanceID];
                float4 velocity = _VelocityBuffer[input.instanceID];
                float scale = max(state.y * _ScaleMultiplier * 2.15, 0.001);
                float3 cameraRight = normalize(UNITY_MATRIX_I_V[0].xyz);
                float3 cameraUp = normalize(UNITY_MATRIX_I_V[1].xyz);
                float3 center = positionScale.xyz + float3(0, scale * 0.72, 0);
                float3 positionWS = center
                    + cameraRight * (input.positionOS.x * scale * 2.0)
                    + cameraUp * (input.positionOS.y * scale * 2.0);

                float visualFlags = floor(max(state.w, 0.0) / 10.0 + 0.001);
                output.positionHCS = TransformWorldToHClip(positionWS);
                float dead = step(0.5, fmod(floor(visualFlags * 0.5), 2.0));
                float movementFrame = fmod(floor(_Time.y * 9.0 + fmod(input.instanceID * 0.618, 4.0)), 4.0);
                float deathFrame = lerp(5.0, 4.0, step(0.5, frac(max(state.w, 0.0))));
                float frame = lerp(movementFrame, deathFrame, dead);
                float2 frameUv = input.uv;
                if (velocity.x < -0.01) frameUv.x = 1.0 - frameUv.x;
                float column = fmod(frame, 3.0);
                float topRow = floor(frame / 3.0);
                frameUv.x = (frameUv.x + column) / 3.0;
                frameUv.y = (frameUv.y + (1.0 - topRow)) / 2.0;
                output.uv = frameUv;
                output.flash = frac(max(state.w, 0.0));
                output.curse = step(0.5, fmod(visualFlags, 2.0));
                output.dead = dead;
                output.slow = step(0.5, fmod(floor(visualFlags * 0.125), 2.0));
                output.enemyKind = _EnemyKindBuffer[input.instanceID];
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Kind 3 is the separately animated Boss billboard.
                clip(2.5 - input.enemyKind);
                half4 color;
                if (input.enemyKind < 0.5)
                    color = SAMPLE_TEXTURE2D(_EnemySheet0, sampler_EnemySheet0, input.uv);
                else if (input.enemyKind < 1.5)
                    color = SAMPLE_TEXTURE2D(_EnemySheet1, sampler_EnemySheet1, input.uv);
                else
                    color = SAMPLE_TEXTURE2D(_EnemySheet2, sampler_EnemySheet2, input.uv);
                color *= _BaseColor;
                clip(color.a - 0.08);
                half luminance = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
                color.rgb = lerp(color.rgb, luminance.xxx * half3(0.42, 0.48, 0.6), input.curse);
                color.rgb = lerp(color.rgb, luminance.xxx * 0.45, input.dead);
                color.rgb = lerp(color.rgb,
                    color.rgb * half3(0.55, 0.78, 1.35) + half3(0.02, 0.08, 0.2),
                    input.slow * 0.48);
                color.rgb = lerp(color.rgb, half3(1, 1, 1), saturate(input.flash) * 0.8);
                return color;
            }
            ENDHLSL
        }
    }
}
