Shader "Rouge/UI Disconnect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Glitch ("Glitch", Range(0,1)) = 0
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Disconnect"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _Glitch;
            float _Dissolve;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float timeSlice = floor(_Time.y * lerp(12.0, 42.0, _Glitch));
                float band = floor(input.texcoord.y * 42.0);
                float sliceNoise = Hash21(float2(band, timeSlice));
                float activeSlice = step(lerp(0.97, 0.58, _Glitch), sliceNoise);
                float horizontalOffset = (sliceNoise - 0.5) * 0.075 *
                                         _Glitch * activeSlice;
                float2 uv = input.texcoord + float2(horizontalOffset, 0.0);
                float chroma = 0.0065 * _Glitch * activeSlice;
                fixed4 center = tex2D(_MainTex, uv);
                fixed red = tex2D(_MainTex, uv + float2(chroma, 0)).r;
                fixed blue = tex2D(_MainTex, uv - float2(chroma, 0)).b;
                fixed4 color = fixed4(red, center.g, blue, center.a) * input.color;

                float scan = 0.82 + 0.18 * sin(input.texcoord.y * 920.0 +
                                               _Time.y * 38.0);
                color.rgb *= lerp(1.0, scan, _Glitch);
                float staticNoise = Hash21(floor(input.texcoord *
                    float2(180.0, 260.0)) + timeSlice);
                color.rgb += (staticNoise - 0.5) * 0.22 * _Glitch;

                float dissolveNoise = Hash21(floor(input.texcoord *
                    float2(160.0, 220.0)) + float2(timeSlice * 0.07, 0));
                float edge = smoothstep(_Dissolve - 0.08, _Dissolve + 0.02,
                                        dissolveNoise);
                color.rgb += fixed3(0.0, 0.75, 1.0) *
                             (1.0 - edge) * edge * 1.8;
                color.a *= edge;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
