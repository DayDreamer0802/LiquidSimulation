using UnityEngine;

[DefaultExecutionOrder(1100)]
[RequireComponent(typeof(Camera))]
public sealed class RougeTiltShiftCamera : MonoBehaviour
{
    private static readonly int TiltShiftParamsId = Shader.PropertyToID("_RougeTiltShiftParams");
    private static readonly int TiltShiftBlurRadiusId = Shader.PropertyToID("_RougeTiltShiftBlurRadius");
    private static readonly int TiltShiftColorId = Shader.PropertyToID("_RougeTiltShiftColor");

    [SerializeField] private bool effectEnabled;

    [Header("Screen-space Focus Band")]
    [SerializeField, Range(0.2f, 0.8f)] private float focusCenterY = 0.5f;
    [SerializeField, Range(0.02f, 0.4f)] private float clearBandHalfHeight = 0.16f;
    [SerializeField, Range(0.02f, 0.4f)] private float transitionWidth = 0.20f;
    [SerializeField, Range(1f, 24f)] private float blurRadius = 5.5f;

    [Header("Miniature Look")]
    [SerializeField, Range(0.5f, 1.5f)] private float contrast = 1.01f;
    [SerializeField, Range(0f, 2f)] private float saturation = 1f;

    public static bool IsEffectActive { get; private set; }
    public static bool IsColorPassActive { get; private set; }
    public bool EffectEnabled => effectEnabled;

    public void SetEffectEnabled(bool enabled)
    {
        effectEnabled = enabled;
        ApplyShaderSettings();
    }

    private void OnEnable()
    {
        ApplyShaderSettings();
    }

    private void LateUpdate()
    {
        if (effectEnabled) ApplyShaderSettings();
    }

    private void OnDisable()
    {
        effectEnabled = false;
        IsColorPassActive = false;
        ApplyShaderSettings();
    }

    private void OnValidate()
    {
        clearBandHalfHeight = Mathf.Clamp(clearBandHalfHeight, 0.02f, 0.4f);
        transitionWidth = Mathf.Clamp(transitionWidth, 0.02f, 0.4f);
        blurRadius = Mathf.Clamp(blurRadius, 1f, 24f);
        if (isActiveAndEnabled) ApplyShaderSettings();
    }

    private void ApplyShaderSettings()
    {
        // The inexpensive final color pass stays enabled for every camera mode.
        // Only the two Gaussian blur passes depend on the F2 tilt-shift mode.
        IsColorPassActive = isActiveAndEnabled;
        IsEffectActive = isActiveAndEnabled && effectEnabled;
        Shader.SetGlobalVector(TiltShiftParamsId, new Vector4(
            IsEffectActive ? 1f : 0f,
            focusCenterY,
            clearBandHalfHeight,
            transitionWidth));
        Shader.SetGlobalFloat(TiltShiftBlurRadiusId, blurRadius);
        Shader.SetGlobalVector(TiltShiftColorId, new Vector4(contrast, saturation, 0f, 0f));
    }
}
