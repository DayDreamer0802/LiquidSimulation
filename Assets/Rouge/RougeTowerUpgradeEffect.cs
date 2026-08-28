using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class RougeTowerUpgradeEffect : MonoBehaviour
{
    private const float Duration = 1.08f;

    private static readonly int PrimaryColorId = Shader.PropertyToID("_PrimaryColor");
    private static readonly int SecondaryColorId = Shader.PropertyToID("_SecondaryColor");
    private static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private static readonly int HologramBaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int HologramAccentColorId = Shader.PropertyToID("_AccentColor");
    private static readonly int HologramDissolveProgressId =
        Shader.PropertyToID("_DissolveProgress");
    private static readonly int HologramLifecycleAlphaId =
        Shader.PropertyToID("_LifecycleAlpha");

    private static Material s_upgradeMaterial;
    private static Mesh s_quadMesh;

    private readonly List<OverlayLayer> _overlayLayers = new List<OverlayLayer>(18);
    private readonly List<Renderer> _hologramRenderers = new List<Renderer>(6);
    private readonly List<GameObject> _temporaryObjects = new List<GameObject>(24);

    private MaterialPropertyBlock _propertyBlock;
    private Transform _billboardTransform;
    private Vector3 _billboardBaseScale;
    private Vector3 _billboardBasePosition;
    private Transform _outerLockTransform;
    private Transform _innerLockTransform;
    private LineRenderer _outerLock;
    private LineRenderer _innerLock;
    private Color _primaryColor;
    private Color _secondaryColor;
    private Color _accentColor;
    private float _cellSize;
    private float _elapsed;
    private bool _billboardCaptured;
    private bool _finished;

    private sealed class OverlayLayer
    {
        public Renderer Renderer;
        public float Mode;
        public float Seed;
        public float Intensity;
        public float Opacity;
        public float Softness;
    }

    public static void Play(RougeDefenseTower tower, Shader upgradeShader,
        Shader hologramShader, float cellSize)
    {
        if (tower == null || !tower.gameObject.activeInHierarchy) return;

        // Construction temporarily owns renderer visibility. Finish it before the
        // overlay-only upgrade effect captures the visible tower presentation.
        RougeTowerConstructionEffect construction =
            tower.GetComponent<RougeTowerConstructionEffect>();
        if (construction != null && construction.enabled) construction.enabled = false;

        RougeTowerUpgradeEffect effect = tower.GetComponent<RougeTowerUpgradeEffect>();
        if (effect == null) effect = tower.gameObject.AddComponent<RougeTowerUpgradeEffect>();
        effect.Restart(tower, upgradeShader, hologramShader, Mathf.Max(0.5f, cellSize));
    }

    private void Restart(RougeDefenseTower tower, Shader upgradeShader,
        Shader hologramShader, float cellSize)
    {
        RestoreBillboard();
        ClearTemporaryObjects();

        _finished = false;
        _elapsed = 0f;
        _cellSize = cellSize;
        _propertyBlock ??= new MaterialPropertyBlock();
        ResolveUpgradeColors(tower, out _primaryColor, out _secondaryColor,
            out _accentColor);
        CaptureBillboard(tower);
        CreateLockFrames();

        Material upgradeMaterial = GetUpgradeMaterial(upgradeShader);
        if (upgradeMaterial != null)
        {
            CreateGroundLayers(upgradeMaterial);
            if (RougeVisualQualityManager.ActiveTier != RougeVisualQualityTier.Low)
                CreateEnergyColumns(upgradeMaterial);
        }

        if (RougeVisualQualityManager.ActiveTier != RougeVisualQualityTier.Low)
        {
            Material hologramMaterial =
                RougeTowerConstructionEffect.GetHologramMaterial(hologramShader);
            if (hologramMaterial != null) CreateHologramOverlays(hologramMaterial);
        }

        ApplyVisualState(0f);
    }

    private static Material GetUpgradeMaterial(Shader shader)
    {
        if (shader == null) shader = Shader.Find("Rouge/Tower Upgrade VFX");
        if (shader == null) return null;
        if (s_upgradeMaterial != null && s_upgradeMaterial.shader == shader)
            return s_upgradeMaterial;

        if (s_upgradeMaterial != null)
        {
            if (Application.isPlaying) Destroy(s_upgradeMaterial);
            else DestroyImmediate(s_upgradeMaterial);
        }
        s_upgradeMaterial = new Material(shader)
        {
            name = "Shared Tower Upgrade VFX",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 3030
        };
        return s_upgradeMaterial;
    }

    private static Mesh GetQuadMesh()
    {
        if (s_quadMesh != null) return s_quadMesh;
        s_quadMesh = new Mesh
        {
            name = "Rouge Upgrade VFX Quad",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            triangles = new[] { 0, 2, 1, 0, 3, 2 }
        };
        s_quadMesh.RecalculateBounds();
        return s_quadMesh;
    }

    private void CaptureBillboard(RougeDefenseTower tower)
    {
        RougeBillboard billboard = tower.GetComponentInChildren<RougeBillboard>(true);
        if (billboard == null) return;
        _billboardTransform = billboard.transform;
        _billboardBaseScale = _billboardTransform.localScale;
        _billboardBasePosition = _billboardTransform.localPosition;
        _billboardCaptured = true;
    }

    private void CreateGroundLayers(Material material)
    {
        CreateOverlayQuad("Upgrade Ground Circuit", transform,
            new Vector3(0f, 0.29f, 0f), Quaternion.Euler(90f, 0f, 0f),
            new Vector3(_cellSize * 1.05f, _cellSize * 1.05f, 1f), material,
            0f, 1.17f, 1.85f, 0.94f, 1f);
        CreateOverlayQuad("Upgrade Commit Shockwave", transform,
            new Vector3(0f, 0.315f, 0f), Quaternion.Euler(90f, 0f, 0f),
            new Vector3(_cellSize * 1.42f, _cellSize * 1.42f, 1f), material,
            2f, 3.43f, 2.35f, 0.92f, 0.82f);

        if (RougeVisualQualityManager.ActiveTier == RougeVisualQualityTier.High)
        {
            CreateOverlayQuad("Upgrade Echo Shockwave", transform,
                new Vector3(0f, 0.325f, 0f), Quaternion.Euler(90f, 17f, 0f),
                new Vector3(_cellSize * 1.16f, _cellSize * 1.16f, 1f), material,
                2f, 8.91f, 1.45f, 0.58f, 1.05f);
        }
    }

    private void CreateEnergyColumns(Material material)
    {
        int count = RougeVisualQualityManager.ActiveTier == RougeVisualQualityTier.High
            ? 3
            : 2;
        for (int i = 0; i < count; i++)
        {
            float angle = 20f + i * (180f / count);
            CreateOverlayQuad("Upgrade Light Beam " + (i + 1), transform,
                new Vector3(0f, _cellSize * 0.47f, 0f),
                Quaternion.Euler(0f, angle, 0f),
                new Vector3(_cellSize * 0.62f, _cellSize * 1.08f, 1f), material,
                1f, 11.3f + i * 4.71f, 1.34f, 0.54f, 1.34f);
        }
    }

    private void CreateOverlayQuad(string objectName, Transform parent,
        Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
        Material material, float mode, float seed, float intensity, float opacity,
        float softness)
    {
        GameObject layerObject = new GameObject(objectName);
        layerObject.transform.SetParent(parent, false);
        layerObject.transform.localPosition = localPosition;
        layerObject.transform.localRotation = localRotation;
        layerObject.transform.localScale = localScale;
        MeshFilter filter = layerObject.AddComponent<MeshFilter>();
        filter.sharedMesh = GetQuadMesh();
        MeshRenderer renderer = layerObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sortingOrder = 32000;
        _overlayLayers.Add(new OverlayLayer
        {
            Renderer = renderer,
            Mode = mode,
            Seed = seed,
            Intensity = intensity,
            Opacity = opacity,
            Softness = softness
        });
        _temporaryObjects.Add(layerObject);
    }

    private void CreateLockFrames()
    {
        _outerLock = CreateHexFrame("Upgrade Outer Lock", _cellSize * 0.39f,
            Mathf.Clamp(_cellSize * 0.010f, 0.045f, 0.085f),
            out _outerLockTransform);
        if (RougeVisualQualityManager.ActiveTier != RougeVisualQualityTier.Low)
        {
            _innerLock = CreateHexFrame("Upgrade Inner Lock", _cellSize * 0.27f,
                Mathf.Clamp(_cellSize * 0.0065f, 0.032f, 0.060f),
                out _innerLockTransform);
        }
    }

    private LineRenderer CreateHexFrame(string objectName, float radius, float width,
        out Transform frameTransform)
    {
        LineRenderer line = TowerDefenseVisuals.CreateBeamRenderer(objectName,
            transform, width);
        frameTransform = line.transform;
        frameTransform.localPosition = new Vector3(0f, 0.335f, 0f);
        frameTransform.localRotation = Quaternion.identity;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 6;
        line.numCornerVertices = 2;
        for (int i = 0; i < 6; i++)
        {
            float angle = (30f + i * 60f) * Mathf.Deg2Rad;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f,
                Mathf.Sin(angle) * radius));
        }
        _temporaryObjects.Add(line.gameObject);
        return line;
    }

    private void CreateHologramOverlays(Material material)
    {
        if (!_billboardCaptured || _billboardTransform == null) return;
        SpriteRenderer[] sprites =
            _billboardTransform.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < sprites.Length; i++)
        {
            SpriteRenderer source = sprites[i];
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy ||
                source.sprite == null) continue;

            GameObject overlayObject = new GameObject("Upgrade Tower Hologram");
            overlayObject.transform.SetParent(source.transform, false);
            SpriteRenderer overlay = overlayObject.AddComponent<SpriteRenderer>();
            overlay.sprite = source.sprite;
            overlay.drawMode = source.drawMode;
            overlay.size = source.size;
            overlay.tileMode = source.tileMode;
            overlay.flipX = source.flipX;
            overlay.flipY = source.flipY;
            overlay.maskInteraction = source.maskInteraction;
            overlay.spriteSortPoint = source.spriteSortPoint;
            overlay.sortingLayerID = source.sortingLayerID;
            overlay.sortingOrder = source.sortingOrder + 30;
            overlay.sharedMaterial = material;
            overlay.color = Color.white;
            overlay.shadowCastingMode = ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.lightProbeUsage = LightProbeUsage.Off;
            overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _hologramRenderers.Add(overlay);
            _temporaryObjects.Add(overlayObject);
        }
    }

    private void Update()
    {
        if (_finished) return;
        _elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_elapsed / Duration);
        ApplyVisualState(progress);
        if (progress >= 1f) FinishImmediately();
    }

    private void ApplyVisualState(float progress)
    {
        ApplyBillboardMotion(progress);
        ApplyLockFrames(progress);
        ApplyOverlayProperties(progress);
        ApplyHologramProperties(progress);
    }

    private void ApplyBillboardMotion(float progress)
    {
        if (!_billboardCaptured || _billboardTransform == null) return;
        float scale;
        if (progress < 0.16f)
            scale = Mathf.Lerp(1f, 0.92f, Smooth01(progress / 0.16f));
        else if (progress < 0.42f)
            scale = Mathf.Lerp(0.92f, 0.84f, Smooth01((progress - 0.16f) / 0.26f));
        else if (progress < 0.53f)
            scale = Mathf.Lerp(0.84f, 1.16f, BackOut((progress - 0.42f) / 0.11f));
        else if (progress < 0.72f)
            scale = Mathf.Lerp(1.16f, 0.975f, Smooth01((progress - 0.53f) / 0.19f));
        else if (progress < 0.88f)
            scale = Mathf.Lerp(0.975f, 1.035f, Smooth01((progress - 0.72f) / 0.16f));
        else
            scale = Mathf.Lerp(1.035f, 1f, Smooth01((progress - 0.88f) / 0.12f));

        float lift = Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(0.38f, 0.68f,
            progress)) * Mathf.PI) * _cellSize * 0.035f;
        _billboardTransform.localScale = _billboardBaseScale * scale;
        _billboardTransform.localPosition = _billboardBasePosition + Vector3.up * lift;
    }

    private void ApplyLockFrames(float progress)
    {
        float lockPhase = Smooth01(Mathf.Clamp01(progress / 0.42f));
        float release = Smooth01(Mathf.InverseLerp(0.44f, 0.80f, progress));
        float alpha = (1f - Smooth01(Mathf.InverseLerp(0.50f, 0.88f, progress))) *
            Smooth01(Mathf.Clamp01(progress / 0.055f));

        if (_outerLockTransform != null)
        {
            _outerLockTransform.localRotation = Quaternion.Euler(0f,
                Mathf.Lerp(-34f, 164f, lockPhase) + release * 72f, 0f);
            _outerLockTransform.localScale = Vector3.one *
                Mathf.Lerp(1.34f, 0.74f, lockPhase) * Mathf.Lerp(1f, 1.58f, release);
        }
        if (_innerLockTransform != null)
        {
            _innerLockTransform.localRotation = Quaternion.Euler(0f,
                Mathf.Lerp(46f, -132f, lockPhase) - release * 54f, 0f);
            _innerLockTransform.localScale = Vector3.one *
                Mathf.Lerp(0.82f, 1.04f, lockPhase) * Mathf.Lerp(1f, 1.32f, release);
        }
        SetFrameColor(_outerLock, _secondaryColor, alpha * 0.88f);
        SetFrameColor(_innerLock, Color.Lerp(_primaryColor, Color.white, 0.24f),
            alpha * 0.56f);
    }

    private void ApplyOverlayProperties(float progress)
    {
        for (int i = 0; i < _overlayLayers.Count; i++)
        {
            OverlayLayer layer = _overlayLayers[i];
            if (layer.Renderer == null) continue;
            _propertyBlock.Clear();
            _propertyBlock.SetColor(PrimaryColorId, _primaryColor);
            _propertyBlock.SetColor(SecondaryColorId, _secondaryColor);
            _propertyBlock.SetColor(AccentColorId, _accentColor);
            _propertyBlock.SetFloat(ProgressId, progress);
            _propertyBlock.SetFloat(ModeId, layer.Mode);
            _propertyBlock.SetFloat(IntensityId, layer.Intensity);
            _propertyBlock.SetFloat(OpacityId, layer.Opacity);
            _propertyBlock.SetFloat(SoftnessId, layer.Softness);
            _propertyBlock.SetFloat(SeedId, layer.Seed);
            layer.Renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void ApplyHologramProperties(float progress)
    {
        if (_hologramRenderers.Count == 0) return;
        float reveal = Smooth01(Mathf.InverseLerp(0.08f, 0.39f, progress));
        float fadeIn = Smooth01(Mathf.InverseLerp(0.08f, 0.19f, progress));
        float fadeOut = 1f - Smooth01(Mathf.InverseLerp(0.55f, 0.84f, progress));
        float commitFlash = 1f - Smooth01(Mathf.Abs(progress - 0.46f) / 0.075f);
        float alpha = Mathf.Clamp01(fadeIn * fadeOut * (0.42f + commitFlash * 0.58f));

        _propertyBlock.Clear();
        _propertyBlock.SetColor(HologramBaseColorId,
            Color.Lerp(_primaryColor, _secondaryColor, 0.30f));
        _propertyBlock.SetColor(HologramAccentColorId, _secondaryColor);
        _propertyBlock.SetFloat(HologramDissolveProgressId, reveal);
        _propertyBlock.SetFloat(HologramLifecycleAlphaId, alpha);
        for (int i = 0; i < _hologramRenderers.Count; i++)
        {
            Renderer renderer = _hologramRenderers[i];
            if (renderer != null) renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private static void ResolveUpgradeColors(RougeDefenseTower tower,
        out Color primary, out Color secondary, out Color accent)
    {
        Color towerColor = TowerDefenseVisuals.GetTowerColor(tower.TowerType);
        Color techCyan = new Color(0.12f, 0.94f, 1.30f, 1f);
        primary = Color.Lerp(techCyan, towerColor, 0.34f);
        primary.a = 1f;

        bool routeA = tower.UsesIceFreeze || tower.UsesMachineGunCritical ||
                      tower.UsesCannonInnerBlast || tower.UsesLaserArmorBreak;
        bool routeB = tower.UsesIceVulnerability || tower.UsesMachineGunFragments ||
                      tower.UsesPersistentCannonShell || tower.UsesLaserRefraction;
        Color gold = new Color(1.36f, 0.74f, 0.13f, 1f);
        Color purple = new Color(0.96f, 0.30f, 1.38f, 1f);
        if (routeB)
        {
            secondary = purple;
            accent = new Color(0.66f, 0.19f, 1.18f, 1f);
        }
        else if (routeA)
        {
            secondary = gold;
            accent = new Color(1.18f, 0.46f, 0.08f, 1f);
        }
        else
        {
            secondary = gold;
            accent = primary;
        }
    }

    private static void SetFrameColor(LineRenderer frame, Color color, float alpha)
    {
        if (frame == null) return;
        color.a = Mathf.Clamp01(alpha);
        frame.startColor = color;
        frame.endColor = color;
        frame.enabled = color.a > 0.002f;
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float BackOut(float value)
    {
        value = Mathf.Clamp01(value) - 1f;
        const float overshoot = 1.25f;
        return value * value * ((overshoot + 1f) * value + overshoot) + 1f;
    }

    private void RestoreBillboard()
    {
        if (_billboardCaptured && _billboardTransform != null)
        {
            _billboardTransform.localScale = _billboardBaseScale;
            _billboardTransform.localPosition = _billboardBasePosition;
        }
        _billboardTransform = null;
        _billboardCaptured = false;
    }

    private void ClearTemporaryObjects()
    {
        for (int i = 0; i < _temporaryObjects.Count; i++)
        {
            GameObject temporary = _temporaryObjects[i];
            if (temporary == null) continue;
            temporary.SetActive(false);
            Destroy(temporary);
        }
        _temporaryObjects.Clear();
        _overlayLayers.Clear();
        _hologramRenderers.Clear();
        _outerLockTransform = null;
        _innerLockTransform = null;
        _outerLock = null;
        _innerLock = null;
    }

    private void FinishImmediately()
    {
        if (_finished) return;
        _finished = true;
        RestoreBillboard();
        ClearTemporaryObjects();
        Destroy(this);
    }

    private void OnDisable()
    {
        if (_finished) return;
        _finished = true;
        RestoreBillboard();
        ClearTemporaryObjects();
    }

    private void OnDestroy()
    {
        RestoreBillboard();
    }
}

public partial class RougeGameManager
{
    private void PlayTowerUpgradeFeedback(RougeDefenseTower tower)
    {
        if (tower == null) return;
        tower.PlayUpgradeSound();
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        float cellSize = map != null ? map.CellSize : 8f;
        RougeTowerUpgradeEffect.Play(tower, towerUpgradeVfxShader, hologramShader,
            cellSize);
    }
}
