using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum RougeVisualQualityTier
{
    Low = 0,
    Medium = 1,
    High = 2
}

[ExecuteAlways]
[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
[AddComponentMenu("Rouge/Visual Quality Manager")]
public sealed class RougeVisualQualityManager : MonoBehaviour
{
    private const string PreferenceKey = "Rouge.Graphics.VisualQualityTier";

    private static readonly int VisualQualityId = Shader.PropertyToID("_RougeVisualQuality");
    private static readonly int LightingStrengthId = Shader.PropertyToID("_RougeLightingStrength");
    private static readonly int TechDetailStrengthId = Shader.PropertyToID("_RougeTechDetailStrength");
    private static readonly int TechAnimationStrengthId = Shader.PropertyToID("_RougeTechAnimationStrength");
    private static readonly int ContactShadowStrengthId = Shader.PropertyToID("_RougeContactShadowStrength");
    private static readonly int SpriteLightingStrengthId = Shader.PropertyToID("_RougeSpriteLightingStrength");
    private static readonly int LightDirectionId = Shader.PropertyToID("_RougeLightDirection");
    private static readonly int LightColorId = Shader.PropertyToID("_RougeLightColor");

    [SerializeField] private RougeVisualQualityTier defaultTier = RougeVisualQualityTier.Medium;
    [SerializeField] private bool rememberSelection = true;
    [SerializeField] private Light directionalLight;
    [SerializeField] private ScriptableRendererData rendererData;

    private Camera _camera;
    private UniversalAdditionalCameraData _cameraData;
    private UniversalRenderPipelineAsset _pipelineAsset;
    private ScreenSpaceAmbientOcclusion _ambientOcclusion;
    private GameObject _runtimeVolumeObject;
    private VolumeProfile _runtimeVolumeProfile;
    private Bloom _bloom;
    private ColorAdjustments _colorAdjustments;
    private Tonemapping _tonemapping;
    private Vignette _vignette;
    private float _nextEnvironmentRefresh;
    private bool _runtimeStateCaptured;
    private bool _initialPostProcessing;
    private bool _initialRenderShadows;
    private AntialiasingMode _initialAntialiasing;
    private AntialiasingQuality _initialAntialiasingQuality;
    private CameraClearFlags _initialCameraClearFlags;
    private Color _initialCameraBackground;
    private LightShadows _initialLightShadows;
    private bool _initialAmbientOcclusion;
    private float _initialShadowDistance;
    private int _initialShadowResolution;
    private int _initialShadowCascades;
    private float _initialAmbientIntensity;
    private float _initialReflectionIntensity;

    private static RougeVisualQualityManager _instance;
    private static RougeVisualQualityTier _activeTier = RougeVisualQualityTier.Medium;
    private static Vector2 _shadowDirection = new Vector2(0.45f, 0.3f).normalized;

    public static RougeVisualQualityTier ActiveTier => _activeTier;
    // A single instanced batch for the player and towers is inexpensive and is
    // important for keeping billboard art grounded, even in the performance tier.
    public static bool StaticContactShadowsEnabled => true;
    public static bool EnemyContactShadowsEnabled => _activeTier != RougeVisualQualityTier.Low;
    public static float EnemyShadowDensity => _activeTier switch
    {
        RougeVisualQualityTier.Low => 0f,
        RougeVisualQualityTier.Medium => 0.55f,
        _ => 1f
    };
    public static int TiltShiftDownsample => _activeTier switch
    {
        RougeVisualQualityTier.Low => 4,
        RougeVisualQualityTier.Medium => 2,
        _ => 1
    };

    public static int ResolveTiltShiftDownsample(int width, int height)
    {
        int downsample = TiltShiftDownsample;
        long pixels = (long)Mathf.Max(1, width) * Mathf.Max(1, height);
        // A full-resolution five-tap horizontal + vertical blur is needlessly
        // expensive at 1440p/4K. Preserve the enhanced tier at 1080p, but cap the
        // blur buffer resolution as output size grows; the composite remains full-res.
        if (pixels >= 7_000_000L) return Mathf.Max(downsample, 4);
        if (pixels >= 3_000_000L) return Mathf.Max(downsample, 2);
        return downsample;
    }
    public static Vector2 ShadowDirection => _shadowDirection;
    public static string ActiveTierLabel => GetTierLabel(_activeTier);

    private void OnEnable()
    {
        _instance = this;
        ResolveSceneReferences();
        RefreshEnvironmentGlobals();

        if (!Application.isPlaying)
        {
            ApplyShaderGlobals(defaultTier);
            return;
        }

        CaptureRuntimeState();
        CreateRuntimeVolume();
        int storedTier = rememberSelection
            ? PlayerPrefs.GetInt(PreferenceKey, (int)defaultTier)
            : (int)defaultTier;
        ApplyTier((RougeVisualQualityTier)Mathf.Clamp(storedTier, 0, 2), false);
    }

    private void OnDisable()
    {
        if (_instance == this) _instance = null;
        if (_runtimeStateCaptured) RestoreRuntimeState();
        if (_runtimeVolumeObject != null || _runtimeVolumeProfile != null)
            DestroyRuntimeVolume();
    }

    private void OnValidate()
    {
        defaultTier = (RougeVisualQualityTier)Mathf.Clamp((int)defaultTier, 0, 2);
        if (!Application.isPlaying && isActiveAndEnabled)
        {
            ResolveSceneReferences();
            RefreshEnvironmentGlobals();
            ApplyShaderGlobals(defaultTier);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        if (Input.GetKeyDown(KeyCode.F5)) CycleActiveTier();
        if (Time.unscaledTime < _nextEnvironmentRefresh) return;
        _nextEnvironmentRefresh = Time.unscaledTime + 0.75f;
        RefreshEnvironmentGlobals();
    }

    public static void CycleActiveTier()
    {
        if (_instance == null) return;
        RougeVisualQualityTier next = (RougeVisualQualityTier)(((int)_activeTier + 1) % 3);
        _instance.ApplyTier(next, true);
    }

    public static void SetActiveTier(RougeVisualQualityTier tier)
    {
        tier = (RougeVisualQualityTier)Mathf.Clamp((int)tier, 0, 2);
        if (_instance != null)
        {
            _instance.ApplyTier(tier, true);
            return;
        }
        _activeTier = tier;
        PlayerPrefs.SetInt(PreferenceKey, (int)tier);
    }

    public static string GetTierLabel(RougeVisualQualityTier tier)
    {
        return tier switch
        {
            RougeVisualQualityTier.Low => "性能",
            RougeVisualQualityTier.Medium => "均衡",
            _ => "增强"
        };
    }

    private void ApplyTier(RougeVisualQualityTier tier, bool persist)
    {
        _activeTier = (RougeVisualQualityTier)Mathf.Clamp((int)tier, 0, 2);
        ApplyShaderGlobals(_activeTier);
        ApplyEnvironmentQuality(_activeTier);
        ApplyCameraQuality(_activeTier);
        ApplyShadowQuality(_activeTier);
        ApplyAmbientOcclusion(_activeTier);
        ApplyPostProcessing(_activeTier);

        if (persist && rememberSelection)
        {
            PlayerPrefs.SetInt(PreferenceKey, (int)_activeTier);
            PlayerPrefs.Save();
        }
    }

    private static void ApplyShaderGlobals(RougeVisualQualityTier tier)
    {
        float lighting;
        float detail;
        float animation;
        float shadow;
        float spriteLighting;
        switch (tier)
        {
            case RougeVisualQualityTier.Low:
                lighting = 0.68f;
                detail = 0.42f;
                animation = 0f;
                shadow = 0.34f;
                spriteLighting = 0.38f;
                break;
            case RougeVisualQualityTier.High:
                lighting = 1f;
                detail = 1f;
                animation = 1f;
                shadow = 1f;
                spriteLighting = 0.72f;
                break;
            default:
                lighting = 0.84f;
                detail = 0.74f;
                animation = 0.65f;
                shadow = 0.68f;
                spriteLighting = 0.54f;
                break;
        }

        Shader.SetGlobalFloat(VisualQualityId, (float)tier);
        Shader.SetGlobalFloat(LightingStrengthId, lighting);
        Shader.SetGlobalFloat(TechDetailStrengthId, detail);
        Shader.SetGlobalFloat(TechAnimationStrengthId, animation);
        Shader.SetGlobalFloat(ContactShadowStrengthId, shadow);
        Shader.SetGlobalFloat(SpriteLightingStrengthId, spriteLighting);
    }

    private void ResolveSceneReferences()
    {
        if (_camera == null) _camera = GetComponent<Camera>();
        if (_camera == null) _camera = RougeCameraFollow.ResolveCamera();
        if (_camera != null && _cameraData == null)
            _cameraData = _camera.GetComponent<UniversalAdditionalCameraData>();

        if (directionalLight == null)
        {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
                directionalLight = RenderSettings.sun;
            else
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] == null || lights[i].type != LightType.Directional) continue;
                    directionalLight = lights[i];
                    break;
                }
            }
        }

        _pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        ResolveAmbientOcclusionFeature();
    }

    private void ResolveAmbientOcclusionFeature()
    {
        if (_ambientOcclusion != null || rendererData == null) return;
        for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
        {
            if (rendererData.rendererFeatures[i] is not ScreenSpaceAmbientOcclusion ambientOcclusion) continue;
            _ambientOcclusion = ambientOcclusion;
            break;
        }
    }

    private void RefreshEnvironmentGlobals()
    {
        ResolveSceneReferences();
        Vector3 directionToLight = directionalLight != null
            ? -directionalLight.transform.forward
            : new Vector3(0.45f, 0.78f, 0.43f).normalized;
        directionToLight.Normalize();
        Shader.SetGlobalVector(LightDirectionId,
            new Vector4(directionToLight.x, directionToLight.y, directionToLight.z, 0f));
        Color lightColor = directionalLight != null
            ? directionalLight.color.linear * Mathf.Clamp(directionalLight.intensity, 0f, 1.1f)
            : new Color(1f, 0.92f, 0.78f, 1f);
        Shader.SetGlobalVector(LightColorId,
            new Vector4(lightColor.r, lightColor.g, lightColor.b, 1f));

        Vector2 awayFromLight = new Vector2(-directionToLight.x, -directionToLight.z);
        _shadowDirection = awayFromLight.sqrMagnitude > 0.0001f
            ? awayFromLight.normalized
            : new Vector2(0.45f, 0.3f).normalized;
    }

    private void CaptureRuntimeState()
    {
        if (_runtimeStateCaptured) return;
        _runtimeStateCaptured = true;
        if (_cameraData != null)
        {
            _initialPostProcessing = _cameraData.renderPostProcessing;
            _initialRenderShadows = _cameraData.renderShadows;
            _initialAntialiasing = _cameraData.antialiasing;
            _initialAntialiasingQuality = _cameraData.antialiasingQuality;
        }
        if (_camera != null)
        {
            _initialCameraClearFlags = _camera.clearFlags;
            _initialCameraBackground = _camera.backgroundColor;
        }
        if (directionalLight != null) _initialLightShadows = directionalLight.shadows;
        if (_ambientOcclusion != null) _initialAmbientOcclusion = _ambientOcclusion.isActive;
        if (_pipelineAsset != null)
        {
            _initialShadowDistance = _pipelineAsset.shadowDistance;
            _initialShadowResolution = _pipelineAsset.mainLightShadowmapResolution;
            _initialShadowCascades = _pipelineAsset.shadowCascadeCount;
        }
        _initialAmbientIntensity = RenderSettings.ambientIntensity;
        _initialReflectionIntensity = RenderSettings.reflectionIntensity;
    }

    private void RestoreRuntimeState()
    {
        if (!_runtimeStateCaptured) return;
        if (_cameraData != null)
        {
            _cameraData.renderPostProcessing = _initialPostProcessing;
            _cameraData.renderShadows = _initialRenderShadows;
            _cameraData.antialiasing = _initialAntialiasing;
            _cameraData.antialiasingQuality = _initialAntialiasingQuality;
        }
        if (_camera != null)
        {
            _camera.clearFlags = _initialCameraClearFlags;
            _camera.backgroundColor = _initialCameraBackground;
        }
        if (directionalLight != null) directionalLight.shadows = _initialLightShadows;
        if (_ambientOcclusion != null && rendererData != null)
        {
            _ambientOcclusion.SetActive(_initialAmbientOcclusion);
            rendererData.SetDirty();
        }
        if (_pipelineAsset != null)
        {
            _pipelineAsset.shadowDistance = _initialShadowDistance;
            _pipelineAsset.mainLightShadowmapResolution = _initialShadowResolution;
            _pipelineAsset.shadowCascadeCount = _initialShadowCascades;
        }
        RenderSettings.ambientIntensity = _initialAmbientIntensity;
        RenderSettings.reflectionIntensity = _initialReflectionIntensity;
        _runtimeStateCaptured = false;
    }

    private static void ApplyEnvironmentQuality(RougeVisualQualityTier tier)
    {
        // The low tier has no real-time shadows or tonemapping, so it needs a
        // little more neutral fill instead of inheriting the moodier high-tier
        // environment and looking as if a grey filter was placed over the map.
        RenderSettings.ambientIntensity = tier switch
        {
            RougeVisualQualityTier.Low => 0.90f,
            RougeVisualQualityTier.Medium => 0.78f,
            _ => 0.72f
        };
        RenderSettings.reflectionIntensity = tier switch
        {
            RougeVisualQualityTier.Low => 0.86f,
            RougeVisualQualityTier.Medium => 0.80f,
            _ => 0.76f
        };
    }

    private void ApplyCameraQuality(RougeVisualQualityTier tier)
    {
        if (_camera != null)
        {
            // The built-in procedural sky's lower hemisphere was the brown-grey
            // field visible around the board. A stable cool void costs nothing
            // and keeps every camera mode in the same sci-fi palette.
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.018f, 0.028f, 0.045f, 1f);
        }
        if (_cameraData == null) return;
        _cameraData.renderShadows = tier != RougeVisualQualityTier.Low;
        // Keep the single combined color/tonemap pass on Low. Expensive bloom,
        // AO, anti-aliasing and dynamic shadows are still disabled there, while
        // the board no longer loses its entire color baseline.
        _cameraData.renderPostProcessing = true;
        _cameraData.antialiasing = tier switch
        {
            RougeVisualQualityTier.Low => AntialiasingMode.None,
            RougeVisualQualityTier.Medium => AntialiasingMode.FastApproximateAntialiasing,
            _ => AntialiasingMode.SubpixelMorphologicalAntiAliasing
        };
        _cameraData.antialiasingQuality = tier == RougeVisualQualityTier.High
            ? AntialiasingQuality.Medium
            : AntialiasingQuality.Low;
    }

    private void ApplyShadowQuality(RougeVisualQualityTier tier)
    {
        if (directionalLight != null)
        {
            directionalLight.shadows = tier switch
            {
                RougeVisualQualityTier.Low => LightShadows.None,
                RougeVisualQualityTier.Medium => LightShadows.Hard,
                _ => LightShadows.Soft
            };
        }
        if (_pipelineAsset == null) return;
        _pipelineAsset.shadowDistance = tier switch
        {
            RougeVisualQualityTier.Low => 0f,
            RougeVisualQualityTier.Medium => 42f,
            _ => 68f
        };
        _pipelineAsset.mainLightShadowmapResolution = tier == RougeVisualQualityTier.High ? 2048 : 1024;
        _pipelineAsset.shadowCascadeCount = tier == RougeVisualQualityTier.High ? 2 : 1;
    }

    private void ApplyAmbientOcclusion(RougeVisualQualityTier tier)
    {
        if (_ambientOcclusion == null || rendererData == null) return;
        bool shouldBeActive = tier == RougeVisualQualityTier.High;
        if (_ambientOcclusion.isActive == shouldBeActive) return;
        _ambientOcclusion.SetActive(shouldBeActive);
        rendererData.SetDirty();
    }

    private void CreateRuntimeVolume()
    {
        if (_runtimeVolumeObject != null) return;
        _runtimeVolumeObject = new GameObject("Rouge Runtime Lighting Volume")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        Volume volume = _runtimeVolumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 80f;
        _runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _runtimeVolumeProfile.hideFlags = HideFlags.HideAndDontSave;
        volume.profile = _runtimeVolumeProfile;
        _bloom = _runtimeVolumeProfile.Add<Bloom>(true);
        _colorAdjustments = _runtimeVolumeProfile.Add<ColorAdjustments>(true);
        _tonemapping = _runtimeVolumeProfile.Add<Tonemapping>(true);
        _vignette = _runtimeVolumeProfile.Add<Vignette>(true);
    }

    private void DestroyRuntimeVolume()
    {
        if (Application.isPlaying)
        {
            if (_runtimeVolumeObject != null) Destroy(_runtimeVolumeObject);
            if (_runtimeVolumeProfile != null) Destroy(_runtimeVolumeProfile);
        }
        else
        {
            if (_runtimeVolumeObject != null) DestroyImmediate(_runtimeVolumeObject);
            if (_runtimeVolumeProfile != null) DestroyImmediate(_runtimeVolumeProfile);
        }
        _runtimeVolumeObject = null;
        _runtimeVolumeProfile = null;
        _bloom = null;
        _colorAdjustments = null;
        _tonemapping = null;
        _vignette = null;
    }

    private void ApplyPostProcessing(RougeVisualQualityTier tier)
    {
        if (_bloom == null || _colorAdjustments == null ||
            _tonemapping == null || _vignette == null) return;
        bool low = tier == RougeVisualQualityTier.Low;
        bool high = tier == RougeVisualQualityTier.High;
        bool enhanced = !low;
        _bloom.active = enhanced;
        _bloom.threshold.Override(high ? 0.90f : 1.05f);
        _bloom.intensity.Override(high ? 0.16f : 0.08f);
        _bloom.scatter.Override(high ? 0.48f : 0.40f);
        _bloom.highQualityFiltering.Override(high);
        _bloom.downscale.Override(high ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter);
        _bloom.maxIterations.Override(high ? 5 : 3);

        _colorAdjustments.active = true;
        _colorAdjustments.postExposure.Override(low ? 0.08f : high ? 0.02f : 0.06f);
        _colorAdjustments.contrast.Override(low ? 6f : high ? 10f : 8f);
        _colorAdjustments.saturation.Override(low ? 4f : high ? 4f : 3f);

        // One shared filmic baseline keeps normal and F2 views visually consistent.
        // Tilt shift now contributes blur only instead of stacking a second grade.
        _tonemapping.active = true;
        _tonemapping.mode.Override(TonemappingMode.Neutral);

        _vignette.active = enhanced;
        _vignette.intensity.Override(high ? 0.08f : 0.045f);
        _vignette.smoothness.Override(0.34f);
    }
}
