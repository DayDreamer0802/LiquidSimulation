Shader "Rouge/EnemyBillboard"
{
    Properties
    {
        _MainTex("Enemy Sprite", 2D) = "white" {}
        _EnemySheet0("Enemy Sheet 0", 2D) = "white" {}
        _EnemySheet1("Enemy Sheet 1", 2D) = "white" {}
        _EnemySheet2("Enemy Sheet 2", 2D) = "white" {}
        _FrozenOverlay("Frozen Overlay", 2D) = "black" {}
        [HideInInspector] _EnemySheetAnimation0("Enemy Sheet Animation 0", Vector) = (3,2,9,0)
        [HideInInspector] _EnemySheetAnimation1("Enemy Sheet Animation 1", Vector) = (3,2,9,0)
        [HideInInspector] _EnemySheetAnimation2("Enemy Sheet Animation 2", Vector) = (3,2,9,0)
        [HideInInspector] _EnemyTypeSizes("Enemy Type Sizes / Elite Multiplier", Vector) = (1,1,1,1)
        [HideInInspector] _RenderHeight("Render Height", Float) = 0
        _BaseColor("Tint", Color) = (1,1,1,1)
        _ScaleMultiplier("Sprite Width / Height", Vector) = (1,1,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        // Each billboard receives one constant depth from its feet in Vert. This
        // restores bottom-to-top crowd ordering without intersecting quad planes.
        ZTest LEqual
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
            TEXTURE2D(_FrozenOverlay);
            SAMPLER(sampler_FrozenOverlay);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _EnemySheetAnimation0;
                float4 _EnemySheetAnimation1;
                float4 _EnemySheetAnimation2;
                float4 _EnemyTypeSizes;
                float4 _BaseColor;
                float4 _ScaleMultiplier;
                float _RenderHeight;
            CBUFFER_END

            float _RougeSpriteLightingStrength;
            float4 _RougeLightDirection;
            float4 _RougeLightColor;

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
                float airborne : TEXCOORD6;
                float2 overlayUv : TEXCOORD7;
                float frozen : TEXCOORD8;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionScale = _PositionScaleBuffer[input.instanceID];
                float4 state = _StateBuffer[input.instanceID];
                float4 velocity = _VelocityBuffer[input.instanceID];
                // Visual size is configured per archetype in tower_defense_balance.json.
                // state.y remains the gameplay/navigation radius and must not replace it.
                int rawEnemyKind = _EnemyKindBuffer[input.instanceID];
                int enemyKind = rawEnemyKind & 0x3F;
                float enemyTypeSize = _EnemyTypeSizes.x;
                if (enemyKind == 1) enemyTypeSize = _EnemyTypeSizes.y;
                else if (enemyKind == 2) enemyTypeSize = _EnemyTypeSizes.z;
                if ((rawEnemyKind & 0x40) != 0) enemyTypeSize *= _EnemyTypeSizes.w;
                float2 spriteScale = max(enemyTypeSize * _ScaleMultiplier.xy, 0.001);
                float3 cameraRight = normalize(UNITY_MATRIX_I_V[0].xyz);
                float3 cameraUp = normalize(UNITY_MATRIX_I_V[1].xyz);
                // Anchor the visible feet to the simulation position. The source
                // sheets contain a small transparent margin below the character.
                float3 center = positionScale.xyz + float3(0, spriteScale.y * 0.90, 0);
                float3 positionWS = center
                    + cameraRight * (input.positionOS.x * spriteScale.x * 2.0)
                    + cameraUp * (input.positionOS.y * spriteScale.y * 2.0);

                float visualFlags = floor(max(state.w, 0.0) / 10.0 + 0.001);
                float slowed = step(0.5, fmod(floor(visualFlags * 0.125), 2.0));
                float frozen = step(0.5, fmod(floor(visualFlags * 0.015625), 2.0));
                float4 positionHCS = TransformWorldToHClip(positionWS);
                float4 feetHCS = TransformWorldToHClip(positionScale.xyz);
                // A camera-facing quad normally has a depth gradient across its plane,
                // so overlapping enemies can intersect and flicker. Give every pixel
                // of one enemy the depth of its feet instead. The enemy whose feet are
                // lower/closer on screen then consistently covers the one above it.
                float feetW = abs(feetHCS.w) > 0.00001 ? feetHCS.w : 0.00001;
                float feetDepth = feetHCS.z / feetW;
                uint depthHash = input.instanceID * 747796405u + 2891336453u;
                depthHash ^= depthHash >> 16;
                float stableDepthBias = (depthHash & 0x0000FFFFu) * (1.0e-6 / 65535.0);
                // Keep the feet just in front of the floor while applying the same
                // base bias to every enemy. The tiny stable per-instance component breaks
                // exact depth ties in piles without visibly changing bottom-to-top order.
                #if UNITY_REVERSED_Z
                    feetDepth += 0.00002 + stableDepthBias;
                #else
                    feetDepth -= 0.00002 + stableDepthBias;
                #endif
                positionHCS.z = saturate(feetDepth) * positionHCS.w;
                output.positionHCS = positionHCS;
                float dead = step(0.5, fmod(floor(visualFlags * 0.5), 2.0));
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
                // Translation is already reduced by the simulation. Slowing the walk cycle
                // as well makes the effect readable inside a dense crowd.
                float movementAnimationSpeed = lerp(lerp(1.0, 0.42, slowed), 0.0, frozen);
                int movementFrame = (((int)floor(_Time.y * animationFps * movementAnimationSpeed)) + instancePhase) % movementFrameCount;
                int firstDeathFrame = min(movementFrameCount, totalFrameCount - 1);
                int finalDeathFrame = max(firstDeathFrame, totalFrameCount - 1);
                int hitFrame = firstDeathFrame;
                int deathFrame = (frac(max(state.w, 0.0)) >= 0.5) ? hitFrame : finalDeathFrame;
                int frame = dead > 0.5 ? deathFrame : movementFrame;
                float2 frameUv = input.uv;
                float facingLeft = step(0.5, fmod(floor(visualFlags * 0.0625), 2.0));
                float facingValid = step(0.5, fmod(floor(visualFlags * 0.03125), 2.0));
                float legacyFacingLeft = step(velocity.x, -0.01);
                if (lerp(legacyFacingLeft, facingLeft, facingValid) > 0.5)
                    frameUv.x = 1.0 - frameUv.x;
                int column = frame % atlasColumns;
                int topRow = frame / atlasColumns;
                frameUv.x = (frameUv.x + column) / (float)atlasColumns;
                frameUv.y = (frameUv.y + (atlasRows - 1 - topRow)) / (float)atlasRows;
                output.uv = frameUv;
                output.overlayUv = input.uv;
                output.flash = frac(max(state.w, 0.0));
                output.curse = step(0.5, fmod(visualFlags, 2.0));
                output.dead = dead;
                output.slow = slowed;
                output.frozen = frozen;
                output.enemyKind = enemyKind;
                float launchBuffered = step(0.5, fmod(floor(visualFlags * 0.25), 2.0));
                float aboveGround = step(_RenderHeight + 0.05, positionScale.y);
                float rising = step(0.05, velocity.y);
                float launchMarked = step(2.5, velocity.w);
                output.airborne = max(max(aboveGround, rising), max(launchMarked, launchBuffered));
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
                half4 frozenOverlay = SAMPLE_TEXTURE2D(_FrozenOverlay,
                    sampler_FrozenOverlay, input.overlayUv);
                frozenOverlay.a *= input.frozen;
                clip(max(color.a, frozenOverlay.a) - 0.08);
                half luminance = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
                color.rgb = lerp(color.rgb, luminance.xxx * half3(0.42, 0.48, 0.6), input.curse);
                color.rgb = lerp(color.rgb, luminance.xxx * 0.45, input.dead);
                color.rgb = lerp(color.rgb,
                    color.rgb * half3(0.55, 0.78, 1.35) + half3(0.02, 0.08, 0.2),
                    input.slow * 0.68);
                color.rgb = lerp(color.rgb, frozenOverlay.rgb,
                    saturate(frozenOverlay.a * 0.92h));
                color.a = max(color.a, frozenOverlay.a);
                // Keep airborne units visibly desaturated, but retain enough texture
                // contrast to read as light grey instead of a flat white silhouette.
                half airborneLuminance = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
                half airborneGrey = lerp(airborneLuminance, 0.68h, 0.72h);
                color.rgb = lerp(color.rgb, airborneGrey.xxx, input.airborne * 0.92h);
                // Project the global sun onto the billboard so the crowd shares the
                // same warm-lit / cool-shadow direction as the world without paying
                // for thousands of alpha-cutout shadow casters.
                float3 cameraRight = normalize(UNITY_MATRIX_I_V[0].xyz);
                float3 cameraUp = normalize(UNITY_MATRIX_I_V[1].xyz);
                float2 projectedLight = normalize(float2(
                    dot(cameraRight, _RougeLightDirection.xyz),
                    dot(cameraUp, _RougeLightDirection.xyz)) + float2(0.0001, 0.0001));
                float2 spritePosition = (input.overlayUv - 0.5) * float2(1.2, 0.72);
                half sunSide = saturate(0.5h + (half)dot(spritePosition, projectedLight));
                half verticalLight = lerp(0.88h, 1.04h,
                    smoothstep(0.04h, 0.96h, input.overlayUv.y));
                half sunPeak = max(max(_RougeLightColor.r, _RougeLightColor.g),
                    max(_RougeLightColor.b, 0.001h));
                half3 normalizedSun = _RougeLightColor.rgb / sunPeak;
                half3 sunTint = lerp(half3(0.90h, 0.96h, 1.04h),
                    normalizedSun, sunSide);
                half3 directionalSprite = color.rgb * verticalLight *
                    lerp(0.86h, 1.08h, sunSide) * lerp(1.0h.xxx, sunTint, 0.22h);
                color.rgb = lerp(color.rgb, directionalSprite,
                    (half)_RougeSpriteLightingStrength);
                // A launch starts with a long hit timer; suppress that white flash while
                // airborne so it cannot wash the grey state back to pure white.
                half hitAmount = saturate(input.flash) * (1.0h - input.airborne);
                color.rgb = lerp(color.rgb, half3(1, 1, 1), hitAmount * 0.8h);
                return color;
            }
            ENDHLSL
        }
    }
}
