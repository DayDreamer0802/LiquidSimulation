Shader "Rouge/DimensionalSlash"
{
    Properties
    {
        [HDR] _CoreColor("Core", Color) = (0.72, 0.92, 1.0, 1)
        [HDR] _EdgeColor("Edge", Color) = (0.58, 0.12, 1.0, 1)
        _ScrollSpeed("Sweep Speed", Float) = 14
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _EdgeColor;
                float _ScrollSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float across = abs(input.uv.y * 2.0 - 1.0);
                float core = 1.0 - smoothstep(0.08, 0.32, across);
                float edge = 1.0 - smoothstep(0.35, 1.0, across);
                float head = frac(input.uv.x * 1.35 - _Time.y * _ScrollSpeed);
                float sweep = smoothstep(0.0, 0.12, head) * (1.0 - smoothstep(0.2, 0.55, head));
                float taper = smoothstep(0.0, 0.035, input.uv.x) * smoothstep(0.0, 0.035, 1.0 - input.uv.x);
                float3 rgb = _EdgeColor.rgb * edge * 0.75 + _CoreColor.rgb * core * (1.3 + sweep * 2.2);
                float alpha = saturate((edge * 0.45 + core + sweep * edge) * taper) * input.color.a;
                return half4(rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
