Shader "Rouge/Map Startup Crystal"
{
    Properties
    {
        [HDR] _CrystalColor("Crystal Color", Color) = (0.06, 1.15, 1.8, 1)
        _Progress("Reveal Progress", Range(0, 1)) = 0
        _OverallFade("Overall Fade", Range(0, 1)) = 1
        _EdgeFlash("Edge Flash", Range(0, 1)) = 0
        _EdgeMode("Edge Mode", Float) = 0
        _RevealWindow("Reveal Window", Range(0.01, 1)) = 0.24
        _UseVertexRevealData("Use Vertex Reveal Data", Float) = 1
        _UseMainTextureAlpha("Use Main Texture Alpha", Float) = 0
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent+80"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "Map Startup Crystal"
            // This is an unlit forward-only effect. UniversalForwardOnly keeps it
            // valid for Forward, Forward+ and Deferred URP renderer configurations.
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            // Do not show Unity's async-compilation placeholder during the short
            // one-shot reveal. The C# side waits out this compile frame before its
            // animation clock starts.
            #pragma editor_sync_compilation
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 textureUv : TEXCOORD1;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 revealData : TEXCOORD1;
                float2 textureUv : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _CrystalColor;
                float _Progress;
                float _OverallFade;
                float _EdgeFlash;
                float _EdgeMode;
                float _RevealWindow;
                float _UseVertexRevealData;
                float _UseMainTextureAlpha;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.revealData = input.color.rg;
                output.textureUv = input.textureUv;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_EdgeMode > 0.5)
                {
                    float flow = 0.72 + 0.28 * sin((input.uv.x + input.uv.y) * 28.0);
                    float edgeAlpha = saturate(_EdgeFlash) * flow;
                    half3 edgeColor = _CrystalColor.rgb * (1.8 + _EdgeFlash * 3.5);
                    return half4(edgeColor, edgeAlpha);
                }

                float vertexRevealData = saturate(_UseVertexRevealData);
                float delay = input.revealData.x * vertexRevealData;
                float variation = lerp(0.47, input.revealData.y, vertexRevealData);
                float localProgress = saturate(
                    (_Progress - delay) / max(_RevealWindow, 0.001));
                clip(localProgress - 0.001);

                float textureAlpha = lerp(1.0,
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.textureUv).a,
                    saturate(_UseMainTextureAlpha));
                clip(textureAlpha - 0.002);

                float2 patternUv = input.uv * 7.0;
                float2 cell = floor(patternUv);
                float2 local = frac(patternUv);
                float2 edgeDistance = min(local, 1.0 - local);
                float lattice = 1.0 - smoothstep(0.035, 0.115,
                    min(edgeDistance.x, edgeDistance.y));
                float diagonalA = abs(frac((patternUv.x + patternUv.y) * 0.5) - 0.5);
                float diagonalB = abs(frac((patternUv.x - patternUv.y) * 0.5) - 0.5);
                float facets = 1.0 - smoothstep(0.025, 0.085,
                    min(diagonalA, diagonalB));
                float noise = Hash21(cell + variation * 37.19);
                float crystalFill = smoothstep(noise - 0.18, noise + 0.10, localProgress);
                float scan = 1.0 - smoothstep(0.025, 0.13,
                    abs(localProgress - noise));
                float rimDistance = min(min(input.uv.x, 1.0 - input.uv.x),
                    min(input.uv.y, 1.0 - input.uv.y));
                float tileRim = 1.0 - smoothstep(0.012, 0.065, rimDistance);
                float fade = saturate(1.0 - localProgress) * _OverallFade;
                float alpha = saturate((lattice * 0.56 + facets * 0.24 +
                    scan * 1.25 + tileRim * 0.42) * crystalFill * fade *
                    textureAlpha);
                half3 color = _CrystalColor.rgb *
                    (0.72 + variation * 0.32 + scan * 2.25 + tileRim * 0.45);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
