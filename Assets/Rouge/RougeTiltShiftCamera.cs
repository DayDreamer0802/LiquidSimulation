using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(1100)]
[RequireComponent(typeof(Camera))]
public sealed class RougeTiltShiftCamera : MonoBehaviour
{
    private const float WorldFocusViewportEdgeClamp = 0.08f;
    private static readonly int TiltShiftParamsId = Shader.PropertyToID("_RougeTiltShiftParams");
    private static readonly int TiltShiftTransitionsId =
        Shader.PropertyToID("_RougeTiltShiftTransitions");
    private static readonly int TiltShiftBlurRadiusId = Shader.PropertyToID("_RougeTiltShiftBlurRadius");
    private static readonly int TiltShiftColorId = Shader.PropertyToID("_RougeTiltShiftColor");
    private static readonly int TiltShiftUiTopId = Shader.PropertyToID("_RougeTiltShiftUiTop");

    private static RougeTiltShiftCamera s_activeInstance;
    private static float s_uiTopNormalized = -1f;
    private static float s_userBlurNormalized = float.NaN;
    private static float s_userClearWidthNormalized = 0.5f;
    private Camera _camera;
    private bool _useWorldFocusPoint;
    private Vector3 _worldFocusPoint;

    [SerializeField] private bool effectEnabled;

    [Header("Focus (visible game area)")]
    [SerializeField, Range(0.2f, 0.8f)] private float focusCenterY = 0.5f;

    [Header("Upper Blur")]
    [FormerlySerializedAs("clearBandHalfHeight")]
    [SerializeField, Range(0.02f, 0.48f)] private float upperClearRange = 0.27f;
    [FormerlySerializedAs("transitionWidth")]
    [SerializeField, Range(0.02f, 0.48f)] private float upperTransitionWidth = 0.19f;
    [SerializeField, Range(0f, 1f)] private float upperBlurStrength = 0.86f;

    [Header("Lower Blur")]
    [Tooltip("Treat the command dock top as the bottom of the visible game area.")]
    [SerializeField] private bool anchorLowerBlurToUi = true;
    [SerializeField, Range(0.02f, 0.48f)] private float lowerClearRange = 0.25f;
    [SerializeField, Range(0.02f, 0.48f)] private float lowerTransitionWidth = 0.20f;
    [Tooltip("Normalized screen-space offset from the detected command dock top.")]
    [SerializeField, Range(-0.12f, 0.2f)] private float lowerUiEdgeOffset;
    [SerializeField, Range(0f, 1f)] private float lowerBlurStrength = 0.95f;

    [Header("Shared Blur")]
    [SerializeField, Range(1f, 26f)] private float blurRadius = 4.5f;

    [Header("Miniature Look")]
    [SerializeField, Range(0.5f, 1.5f)] private float contrast = 1.01f;
    [SerializeField, Range(0f, 2f)] private float saturation = 1f;

    public static bool IsEffectActive { get; private set; }
    public static bool IsColorPassActive { get; private set; }
    public bool EffectEnabled => effectEnabled;

    public static float BlurRadiusToNormalized(float radius)
    {
        return Mathf.InverseLerp(1f, 26f, Mathf.Clamp(radius, 1f, 26f));
    }

    public static void SetUserAdjustments(bool customBlurEnabled,
        float blurNormalized, float clearWidthNormalized)
    {
        s_userBlurNormalized = customBlurEnabled
            ? Mathf.Clamp01(blurNormalized)
            : float.NaN;
        s_userClearWidthNormalized = Mathf.Clamp01(clearWidthNormalized);
        if (s_activeInstance != null && s_activeInstance.isActiveAndEnabled)
            s_activeInstance.ApplyShaderSettings();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeUserAdjustments()
    {
        s_userBlurNormalized = float.NaN;
        s_userClearWidthNormalized = 0.5f;
    }

    public RougeTiltShiftSettings CaptureSettings()
    {
        return new RougeTiltShiftSettings
        {
            focusCenterY = focusCenterY,
            upperClearRange = upperClearRange,
            upperTransitionWidth = upperTransitionWidth,
            upperBlurStrength = upperBlurStrength,
            anchorLowerBlurToUi = anchorLowerBlurToUi,
            lowerClearRange = lowerClearRange,
            lowerTransitionWidth = lowerTransitionWidth,
            lowerUiEdgeOffset = lowerUiEdgeOffset,
            lowerBlurStrength = lowerBlurStrength,
            blurRadius = blurRadius,
            contrast = contrast,
            saturation = saturation
        }.Sanitized();
    }

    public void ApplySettings(RougeTiltShiftSettings settings)
    {
        settings = settings.Sanitized();
        focusCenterY = settings.focusCenterY;
        upperClearRange = settings.upperClearRange;
        upperTransitionWidth = settings.upperTransitionWidth;
        upperBlurStrength = settings.upperBlurStrength;
        anchorLowerBlurToUi = settings.anchorLowerBlurToUi;
        lowerClearRange = settings.lowerClearRange;
        lowerTransitionWidth = settings.lowerTransitionWidth;
        lowerUiEdgeOffset = settings.lowerUiEdgeOffset;
        lowerBlurStrength = settings.lowerBlurStrength;
        blurRadius = settings.blurRadius;
        contrast = settings.contrast;
        saturation = settings.saturation;
        ApplyShaderSettings();
    }

    public void SetEffectEnabled(bool enabled)
    {
        effectEnabled = enabled;
        ApplyShaderSettings();
    }

    public void SetWorldFocusPoint(Vector3 worldPoint)
    {
        _worldFocusPoint = worldPoint;
        _useWorldFocusPoint = true;
        ApplyShaderSettings();
    }

    public void ClearWorldFocusPoint()
    {
        _useWorldFocusPoint = false;
        ApplyShaderSettings();
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        s_activeInstance = this;
        ApplyShaderSettings();
    }

    private void LateUpdate()
    {
        if (effectEnabled) ApplyShaderSettings();
    }

    private void OnDisable()
    {
        if (s_activeInstance == this) s_activeInstance = null;
        effectEnabled = false;
        IsColorPassActive = false;
        ApplyShaderSettings();
    }

    private void OnValidate()
    {
        upperClearRange = Mathf.Clamp(upperClearRange, 0.02f, 0.48f);
        lowerClearRange = Mathf.Clamp(lowerClearRange, 0.02f, 0.48f);
        upperTransitionWidth = Mathf.Clamp(upperTransitionWidth, 0.02f, 0.48f);
        lowerTransitionWidth = Mathf.Clamp(lowerTransitionWidth, 0.02f, 0.48f);
        upperBlurStrength = Mathf.Clamp01(upperBlurStrength);
        lowerBlurStrength = Mathf.Clamp01(lowerBlurStrength);
        lowerUiEdgeOffset = Mathf.Clamp(lowerUiEdgeOffset, -0.12f, 0.2f);
        blurRadius = Mathf.Clamp(blurRadius, 1f, 26f);
        if (isActiveAndEnabled) ApplyShaderSettings();
    }

    private void ApplyShaderSettings()
    {
        // The inexpensive final color pass stays enabled for every camera mode.
        // Only the two Gaussian blur passes depend on the F2 tilt-shift mode.
        IsColorPassActive = isActiveAndEnabled;
        IsEffectActive = isActiveAndEnabled && effectEnabled;
        float clearWidthScale = s_userClearWidthNormalized * 2f;
        float effectiveUpperClearRange = Mathf.Clamp(
            upperClearRange * clearWidthScale, 0f, 0.96f);
        float effectiveLowerClearRange = Mathf.Clamp(
            lowerClearRange * clearWidthScale, 0f, 0.96f);
        float effectiveBlurRadius = float.IsNaN(s_userBlurNormalized)
            ? blurRadius
            : Mathf.Lerp(1f, 26f, s_userBlurNormalized);
        Shader.SetGlobalVector(TiltShiftParamsId, new Vector4(
            IsEffectActive ? 1f : 0f,
            ResolveFocusCenterY(),
            effectiveUpperClearRange,
            effectiveLowerClearRange));
        Shader.SetGlobalVector(TiltShiftTransitionsId, new Vector4(
            upperTransitionWidth,
            lowerTransitionWidth,
            upperBlurStrength,
            lowerBlurStrength));
        Shader.SetGlobalFloat(TiltShiftBlurRadiusId, effectiveBlurRadius);
        float visibleGameBottom = anchorLowerBlurToUi && s_uiTopNormalized >= 0f
            ? Mathf.Clamp01(s_uiTopNormalized + lowerUiEdgeOffset)
            : -1f;
        Shader.SetGlobalFloat(TiltShiftUiTopId, visibleGameBottom);
        Shader.SetGlobalVector(TiltShiftColorId, new Vector4(contrast, saturation, 0f, 0f));
    }

    private float ResolveFocusCenterY()
    {
        if (!_useWorldFocusPoint) return focusCenterY;
        if (_camera == null) _camera = GetComponent<Camera>();
        if (_camera == null) return focusCenterY;

        Vector3 viewportPoint = _camera.WorldToViewportPoint(_worldFocusPoint);
        if (viewportPoint.z <= 0.001f) return focusCenterY;
        return Mathf.Clamp(viewportPoint.y,
            WorldFocusViewportEdgeClamp, 1f - WorldFocusViewportEdgeClamp);
    }

    public static void SetUiTopNormalized(float normalizedY)
    {
        float clamped = Mathf.Clamp01(normalizedY);
        if (Mathf.Abs(s_uiTopNormalized - clamped) <= 0.0005f) return;
        s_uiTopNormalized = clamped;
        if (s_activeInstance != null && s_activeInstance.isActiveAndEnabled)
            s_activeInstance.ApplyShaderSettings();
    }

    public static void ClearUiTopBoundary()
    {
        s_uiTopNormalized = -1f;
        if (s_activeInstance != null && s_activeInstance.isActiveAndEnabled)
            s_activeInstance.ApplyShaderSettings();
    }
}
