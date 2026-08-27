using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class RougeTowerConstructionEffect : MonoBehaviour
{
    private const float Duration = 0.74f;
    private const float RevealEnd = 0.56f;
    private const float OverlayFadeStart = 0.42f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
    private static readonly int DissolveProgressId = Shader.PropertyToID("_DissolveProgress");
    private static readonly int LifecycleAlphaId = Shader.PropertyToID("_LifecycleAlpha");
    private static Material s_hologramMaterial;

    private readonly List<SpriteState> _spriteStates = new List<SpriteState>(4);
    private readonly List<MeshState> _meshStates = new List<MeshState>(2);
    private readonly List<Renderer> _hologramRenderers = new List<Renderer>(6);
    private readonly List<GameObject> _temporaryObjects = new List<GameObject>(8);

    private MaterialPropertyBlock _propertyBlock;
    private Transform _outerFrameTransform;
    private Transform _innerFrameTransform;
    private LineRenderer _outerFrame;
    private LineRenderer _innerFrame;
    private Color _baseColor;
    private Color _accentColor;
    private float _elapsed;
    private bool _finished;

    private struct SpriteState
    {
        public SpriteRenderer Renderer;
        public Color Color;
    }

    private struct MeshState
    {
        public Renderer Renderer;
        public bool Enabled;
    }

    public static void Play(RougeDefenseTower tower, Shader hologramShader, float cellSize)
    {
        if (tower == null || !tower.gameObject.activeInHierarchy) return;
        RougeTowerConstructionEffect existing =
            tower.GetComponent<RougeTowerConstructionEffect>();
        if (existing != null) return;

        RougeTowerConstructionEffect effect =
            tower.gameObject.AddComponent<RougeTowerConstructionEffect>();
        effect.Initialize(tower, hologramShader, Mathf.Max(0.5f, cellSize));
    }

    private void Initialize(RougeDefenseTower tower, Shader hologramShader, float cellSize)
    {
        _propertyBlock = new MaterialPropertyBlock();
        ResolveConstructionColors(tower.TowerType, out _baseColor, out _accentColor);
        CreateRotatingFrames(cellSize);

        if (RougeVisualQualityManager.ActiveTier == RougeVisualQualityTier.Low) return;
        Material hologramMaterial = GetHologramMaterial(hologramShader);
        if (hologramMaterial == null) return;

        SpriteRenderer[] sprites = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < sprites.Length; i++)
        {
            SpriteRenderer source = sprites[i];
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy ||
                source.sprite == null) continue;

            _spriteStates.Add(new SpriteState
            {
                Renderer = source,
                Color = source.color
            });
            Color hidden = source.color;
            hidden.a = 0f;
            source.color = hidden;
            CreateSpriteHologram(source, hologramMaterial);
        }

        MeshRenderer[] meshes = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            MeshRenderer source = meshes[i];
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy ||
                source.GetComponent<MeshFilter>() == null) continue;
            _meshStates.Add(new MeshState
            {
                Renderer = source,
                Enabled = source.enabled
            });
            source.enabled = false;
            CreateMeshHologram(source, hologramMaterial);
        }
    }

    private static Material GetHologramMaterial(Shader shader)
    {
        if (s_hologramMaterial != null) return s_hologramMaterial;
        if (shader == null) shader = Shader.Find("Rouge/Hologram");
        if (shader == null) return null;

        s_hologramMaterial = new Material(shader)
        {
            name = "Shared Tower Construction Lattice",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 3020,
            enableInstancing = true
        };
        s_hologramMaterial.SetFloat("_Alpha", 0.68f);
        s_hologramMaterial.SetFloat("_ScanlineDensity", 22f);
        s_hologramMaterial.SetFloat("_ScanlineSpeed", 2.8f);
        s_hologramMaterial.SetFloat("_FresnelPower", 2.15f);
        s_hologramMaterial.SetFloat("_GlowStrength", 1.72f);
        s_hologramMaterial.SetFloat("_NoiseStrength", 0.08f);
        s_hologramMaterial.SetFloat("_GridDensity", 11f);
        s_hologramMaterial.SetFloat("_DissolveEdgeWidth", 0.12f);
        s_hologramMaterial.SetFloat("_DissolveGlow", 1.65f);
        return s_hologramMaterial;
    }

    private void CreateSpriteHologram(SpriteRenderer source, Material material)
    {
        GameObject overlayObject = new GameObject("Construction Lattice Sprite");
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
        overlay.sortingOrder = source.sortingOrder + 20;
        overlay.sharedMaterial = material;
        overlay.color = Color.white;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.lightProbeUsage = LightProbeUsage.Off;
        overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _hologramRenderers.Add(overlay);
        _temporaryObjects.Add(overlayObject);
    }

    private void CreateMeshHologram(MeshRenderer source, Material material)
    {
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null) return;
        GameObject overlayObject = new GameObject("Construction Lattice Mesh");
        overlayObject.transform.SetParent(source.transform, false);
        MeshFilter overlayFilter = overlayObject.AddComponent<MeshFilter>();
        overlayFilter.sharedMesh = sourceFilter.sharedMesh;
        MeshRenderer overlay = overlayObject.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = material;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.lightProbeUsage = LightProbeUsage.Off;
        overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _hologramRenderers.Add(overlay);
        _temporaryObjects.Add(overlayObject);
    }

    private void CreateRotatingFrames(float cellSize)
    {
        _outerFrame = CreateFrame("Construction Field Outer", cellSize * 0.38f,
            Mathf.Clamp(cellSize * 0.010f, 0.045f, 0.085f), out _outerFrameTransform);
        if (RougeVisualQualityManager.ActiveTier != RougeVisualQualityTier.Low)
        {
            _innerFrame = CreateFrame("Construction Field Inner", cellSize * 0.28f,
                Mathf.Clamp(cellSize * 0.0065f, 0.032f, 0.060f), out _innerFrameTransform);
        }
    }

    private LineRenderer CreateFrame(string objectName, float halfExtent, float width,
        out Transform frameTransform)
    {
        LineRenderer line = TowerDefenseVisuals.CreateBeamRenderer(objectName, transform, width);
        frameTransform = line.transform;
        frameTransform.localPosition = new Vector3(0f, 0.18f, 0f);
        frameTransform.localRotation = Quaternion.identity;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 4;
        line.numCornerVertices = 1;
        line.SetPosition(0, new Vector3(-halfExtent, 0f, -halfExtent));
        line.SetPosition(1, new Vector3(-halfExtent, 0f, halfExtent));
        line.SetPosition(2, new Vector3(halfExtent, 0f, halfExtent));
        line.SetPosition(3, new Vector3(halfExtent, 0f, -halfExtent));
        _temporaryObjects.Add(line.gameObject);
        return line;
    }

    private void Update()
    {
        if (_finished) return;
        _elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_elapsed / Duration);
        float easedRotation = 1f - Mathf.Pow(1f - progress, 3f);
        float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 1f, progress));
        float frameAlpha = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0.54f, 1f, progress));

        if (_outerFrameTransform != null)
        {
            _outerFrameTransform.localRotation = Quaternion.Euler(0f,
                easedRotation * 540f, 0f);
            _outerFrameTransform.localScale = Vector3.one * Mathf.Lerp(1.06f, 0.78f, settle);
        }
        if (_innerFrameTransform != null)
        {
            _innerFrameTransform.localRotation = Quaternion.Euler(0f,
                -easedRotation * 360f + 45f, 0f);
            _innerFrameTransform.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, settle);
        }
        SetFrameColor(_outerFrame, _accentColor, frameAlpha * 0.76f);
        SetFrameColor(_innerFrame, Color.Lerp(_baseColor, Color.white, 0.38f),
            frameAlpha * 0.48f);

        float reveal = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(progress / RevealEnd));
        float overlayAlpha = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(OverlayFadeStart, 1f, progress));
        UpdateHolograms(reveal, overlayAlpha);

        float towerAlpha = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0.12f, 0.68f, progress));
        UpdateSourceVisibility(towerAlpha, progress >= 0.30f);

        if (progress >= 1f) FinishImmediately();
    }

    private void UpdateHolograms(float reveal, float alpha)
    {
        if (_hologramRenderers.Count == 0) return;
        _propertyBlock.Clear();
        _propertyBlock.SetColor(BaseColorId, Color.Lerp(_baseColor, _accentColor, 0.34f));
        _propertyBlock.SetColor(AccentColorId, _accentColor);
        _propertyBlock.SetFloat(DissolveProgressId, reveal);
        _propertyBlock.SetFloat(LifecycleAlphaId, alpha);
        for (int i = 0; i < _hologramRenderers.Count; i++)
        {
            Renderer renderer = _hologramRenderers[i];
            if (renderer != null) renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void UpdateSourceVisibility(float alpha, bool showMeshes)
    {
        for (int i = 0; i < _spriteStates.Count; i++)
        {
            SpriteState state = _spriteStates[i];
            if (state.Renderer == null) continue;
            Color color = state.Color;
            color.a *= alpha;
            state.Renderer.color = color;
        }
        if (!showMeshes) return;
        for (int i = 0; i < _meshStates.Count; i++)
        {
            MeshState state = _meshStates[i];
            if (state.Renderer != null) state.Renderer.enabled = state.Enabled;
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

    private void FinishImmediately()
    {
        if (_finished) return;
        _finished = true;
        for (int i = 0; i < _spriteStates.Count; i++)
        {
            SpriteState state = _spriteStates[i];
            if (state.Renderer != null) state.Renderer.color = state.Color;
        }
        for (int i = 0; i < _meshStates.Count; i++)
        {
            MeshState state = _meshStates[i];
            if (state.Renderer != null) state.Renderer.enabled = state.Enabled;
        }
        for (int i = 0; i < _temporaryObjects.Count; i++)
        {
            if (_temporaryObjects[i] != null) Destroy(_temporaryObjects[i]);
        }
        _temporaryObjects.Clear();
        Destroy(this);
    }

    private void OnDisable()
    {
        if (!_finished) FinishImmediately();
    }

    private static void ResolveConstructionColors(RougeTowerType type,
        out Color baseColor, out Color accentColor)
    {
        switch (type)
        {
            case RougeTowerType.MachineGun:
                baseColor = new Color(0.42f, 0.30f, 0.02f, 1f);
                accentColor = new Color(1.18f, 0.88f, 0.18f, 1f);
                break;
            case RougeTowerType.Cannon:
            case RougeTowerType.Flame:
                baseColor = new Color(0.48f, 0.10f, 0.025f, 1f);
                accentColor = new Color(1.30f, 0.42f, 0.08f, 1f);
                break;
            case RougeTowerType.Laser:
                baseColor = new Color(0.025f, 0.38f, 0.24f, 1f);
                accentColor = new Color(0.18f, 1.16f, 0.70f, 1f);
                break;
            case RougeTowerType.PiercingLaser:
                baseColor = new Color(0.31f, 0.035f, 0.42f, 1f);
                accentColor = new Color(0.94f, 0.22f, 1.28f, 1f);
                break;
            case RougeTowerType.ReinforcementTower:
                baseColor = new Color(0.40f, 0.24f, 0.025f, 1f);
                accentColor = new Color(1.28f, 0.72f, 0.10f, 1f);
                break;
            case RougeTowerType.ChargeTower:
                baseColor = new Color(0.03f, 0.36f, 0.44f, 1f);
                accentColor = new Color(0.20f, 1.14f, 1.25f, 1f);
                break;
            default:
                baseColor = new Color(0.035f, 0.30f, 0.52f, 1f);
                accentColor = new Color(0.24f, 0.92f, 1.30f, 1f);
                break;
        }
    }
}

public partial class RougeGameManager
{
    private void PlayTowerConstructionEffect(RougeDefenseTower tower)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        float cellSize = map != null ? map.CellSize : 8f;
        RougeTowerConstructionEffect.Play(tower, hologramShader, cellSize);
    }
}
