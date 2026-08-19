Shader "Rouge/EnemyBillboard"
{
    Properties
    {
        _MainTex("Enemy Sprite", 2D) = "white" {}
        _EnemySheet0("Enemy Sheet 0", 2D) = "white" {}
        _EnemySheet1("Enemy Sheet 1", 2D) = "white" {}
        _EnemySheet2("Enemy Sheet 2", 2D) = "white" {}
        [HideInInspector] _EnemySheetAnimation0("Enemy Sheet Animation 0", Vector) = (3,2,9,0)
        [HideInInspector] _EnemySheetAnimation1("Enemy Sheet Animation 1", Vector) = (3,2,9,0)
        [HideInInspector] _EnemySheetAnimation2("Enemy Sheet Animation 2", Vector) = (3,2,9,0)
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
                float4 _EnemySheetAnimation0;
                float4 _EnemySheetAnimation1;
                float4 _EnemySheetAnimation2;
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
                int enemyKind = _EnemyKindBuffer[input.instanceID];
                float4 animation = _EnemySheetAnimation0;
                if (enemyKind == 1) animation = _EnemySheetAnimation1;
                else if (enemyKind == 2) animation = _EnemySheetAnimation2;

                int atlasColumns = max(1, (int)floor(animation.x + 0.5));
                int atlasRows = max(1, (int)floor(animation.y + 0.5));
                float animationFps = max(0.01, animation.z);
                int totalFrameCount = atlasColumns * atlasRows;

                int deathFrameCount = clamp((int)floor(animation.w + 0.5), 0, totalFrameCount - 1);
                // The final configured cells are reserved for death. Every
                // living enemy loops only through the preceding cells.
                int movementFrameCount = max(1, totalFrameCount - deathFrameCount);
                uint phaseHash = input.instanceID * 1664525u + 1013904223u;
                int instancePhase = (int)(phaseHash % (uint)movementFrameCount);
                int movementFrame = (((int)floor(_Time.y * animationFps)) + instancePhase) % movementFrameCount;
                int firstDeathFrame = min(movementFrameCount, totalFrameCount - 1);
                int finalDeathFrame = max(firstDeathFrame, totalFrameCount - 1);
                int hitFrame = firstDeathFrame;
                int deathFrame = (frac(max(state.w, 0.0)) >= 0.5) ? hitFrame : finalDeathFrame;
                int frame = dead > 0.5 ? deathFrame : movementFrame;
                float2 frameUv = input.uv;
                if (velocity.x < -0.01) frameUv.x = 1.0 - frameUv.x;
                int column = frame % atlasColumns;
                int topRow = frame / atlasColumns;
                frameUv.x = (frameUv.x + column) / (float)atlasColumns;
                frameUv.y = (frameUv.y + (atlasRows - 1 - topRow)) / (float)atlasRows;
                output.uv = frameUv;
                output.flash = frac(max(state.w, 0.0));
                output.curse = step(0.5, fmod(visualFlags, 2.0));
                output.dead = dead;
                output.slow = step(0.5, fmod(floor(visualFlags * 0.125), 2.0));
                output.enemyKind = enemyKind;
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
