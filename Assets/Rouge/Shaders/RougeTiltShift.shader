Shader "Hidden/Rouge/TiltShift"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _RougeTiltShiftParams;
        float4 _RougeTiltShiftTransitions;
        float _RougeTiltShiftBlurRadius;
        float _RougeTiltShiftVerticalScale;
        float _RougeTiltShiftUiTop;
        float4 _RougeTiltShiftColor;

        TEXTURE2D_X(_RougeTiltShiftBlurTexture);

        half4 SampleSource(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        }

        half4 SampleGaussian(float2 uv, float2 direction)
        {
            // Five bilinear samples reproduce a normalized nine-tap Gaussian kernel.
            // blurRadius is the distance, in pixels, of the outermost tap.
            float2 stepUv = _BlitTexture_TexelSize.xy * direction
                * (_RougeTiltShiftBlurRadius / 3.23076923);

            half4 color = SampleSource(uv) * 0.22702703h;
            color += SampleSource(uv + stepUv * 1.38461538) * 0.31621622h;
            color += SampleSource(uv - stepUv * 1.38461538) * 0.31621622h;
            color += SampleSource(uv + stepUv * 3.23076923) * 0.07027027h;
            color += SampleSource(uv - stepUv * 3.23076923) * 0.07027027h;
            return color;
        }

        half4 FragBlurHorizontal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
            // This pass samples the full-resolution camera source while writing
            // into the smaller blur target, so its texel step is already correct.
            return SampleGaussian(uv, float2(1.0, 0.0));
        }

        half4 FragBlurVertical(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
            // Match the vertical radius to the selected full/half/quarter-size
            // blur buffer used by the current visual quality tier.
            return SampleGaussian(uv, float2(0.0, _RougeTiltShiftVerticalScale));
        }

        half4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
            half4 source = SampleSource(uv);
            half3 color = source.rgb;
            // This uniform branch avoids touching an unbound blur texture in the
            // default/free/top-down modes, where this pass only applies the 1.01 grade.
            UNITY_BRANCH
            if (_RougeTiltShiftParams.x > 0.5)
            {
                half3 blurred = SAMPLE_TEXTURE2D_X(
                    _RougeTiltShiftBlurTexture, sampler_LinearClamp, uv).rgb;
                // Normalize against the part of the screen that still shows the game.
                // The dock edge becomes gameY=0, so the lower transition remains
                // visible above the UI instead of being hidden behind it.
                float visibleGameBottom = _RougeTiltShiftUiTop >= 0.0
                    ? _RougeTiltShiftUiTop
                    : 0.0;
                float gameY = saturate((uv.y - visibleGameBottom) /
                    max(0.0001, 1.0 - visibleGameBottom));
                float upperDistance = max(0.0,
                    gameY - _RougeTiltShiftParams.y);
                float lowerDistance = max(0.0,
                    _RougeTiltShiftParams.y - gameY);
                float upperBlur = smoothstep(_RougeTiltShiftParams.z,
                    _RougeTiltShiftParams.z + _RougeTiltShiftTransitions.x,
                    upperDistance) * _RougeTiltShiftTransitions.z;
                float lowerBlur = smoothstep(_RougeTiltShiftParams.w,
                    _RougeTiltShiftParams.w + _RougeTiltShiftTransitions.y,
                    lowerDistance) * _RougeTiltShiftTransitions.w;
                float blurMask = saturate(max(lowerBlur, upperBlur));
                color = lerp(source.rgb, blurred, blurMask);
            }
            half luminance = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
            color = lerp(luminance.xxx, color, _RougeTiltShiftColor.y);
            color = (color - 0.5h) * _RougeTiltShiftColor.x + 0.5h;
            return half4(color, source.a);
        }
        ENDHLSL

        Pass
        {
            Name "Gaussian Horizontal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "Gaussian Vertical"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "Tilt Shift Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
