using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class RougeTowerDefenseMapLoader
{
    private const string StartupRevealShaderResource = "Shaders/RougeMapStartupCrystal";
    private const string StartupBackdropObjectName = "Subtle Circuit Backdrop";
    // A long first frame (most commonly the Editor compiling the shader) must not
    // consume the reveal. At 30 fps or better this does not alter the timing; on a
    // hitchy frame the animation deliberately slows down instead of skipping.
    private const float StartupRevealMaximumFrameDelta = 1f / 30f;

    private static readonly int StartupProgressId = Shader.PropertyToID("_Progress");
    private static readonly int StartupOverallFadeId = Shader.PropertyToID("_OverallFade");
    private static readonly int StartupEdgeFlashId = Shader.PropertyToID("_EdgeFlash");
    private static readonly int StartupEdgeModeId = Shader.PropertyToID("_EdgeMode");
    private static readonly int StartupCrystalColorId = Shader.PropertyToID("_CrystalColor");
    private static readonly int StartupRevealWindowId = Shader.PropertyToID("_RevealWindow");
    private static readonly int StartupUseVertexRevealDataId =
        Shader.PropertyToID("_UseVertexRevealData");
    private static readonly int StartupUseMainTextureAlphaId =
        Shader.PropertyToID("_UseMainTextureAlpha");
    private static readonly int StartupMainTextureId = Shader.PropertyToID("_MainTex");
    private static readonly WaitForEndOfFrame StartupRevealEndOfFrame = new WaitForEndOfFrame();
    private static Shader _cachedStartupRevealShader;

    [Header("Startup Map Reveal")]
    [SerializeField] private bool playStartupMapReveal = true;
    [SerializeField, Range(0.4f, 4f)] private float startupRevealDuration = 1.65f;
    [SerializeField, Range(0.1f, 1.5f)] private float startupEdgeFlashDuration = 0.42f;
    [SerializeField, Range(0.2f, 2f)] private float startupMainTowerRevealDuration = 0.72f;
    [SerializeField, Range(0f, 1f)] private float startupEmptyZoneHoldDuration = 0.32f;
    [SerializeField, Range(0f, 1f)] private float startupAnchorHoldDuration = 0.24f;
    [SerializeField, Range(0f, 1f)] private float startupReadyHoldDuration = 0.18f;
    [SerializeField, ColorUsage(true, true)] private Color startupCrystalColor =
        new Color(0.06f, 1.15f, 1.8f, 1f);

    private GameObject _startupRevealRoot;
    private Mesh _startupRevealMesh;
    private Mesh _startupBoundaryMesh;
    private Material _startupRevealMaterial;
    private Material _startupBoundaryMaterial;
    private Material _startupMainTowerRevealMaterial;
    private Material _startupBackdropMaterial;
    private Color _startupBackdropGridColor;
    private Color _startupBackdropAccentColor;
    private float _startupBackdropLineIntensity;
    private readonly List<Mesh> _startupMainTowerRevealMeshes = new List<Mesh>();
    private readonly Dictionary<Vector2Int, float> _startupTileTopByCell =
        new Dictionary<Vector2Int, float>();
    private readonly Dictionary<Renderer, bool> _startupTileRendererStates =
        new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, bool> _startupPresentationRendererStates =
        new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, bool> _startupMainTowerRendererStates =
        new Dictionary<Renderer, bool>();
    private readonly Dictionary<SpriteRenderer, Color> _startupMainTowerSpriteColors =
        new Dictionary<SpriteRenderer, Color>();
    private RougeMainTower _startupMainTower;
    private Vector3 _startupMainTowerOriginalLocalPosition;
    private Vector3 _startupMainTowerOriginalLocalScale;
    private GameObject _startupPrimedRuntimeRoot;
    private bool _startupRevealPrimed;

    public bool StartupRevealEnabled => playStartupMapReveal;
    public bool StartupMainTowerHidden =>
        _startupRevealPrimed && _startupMainTower != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PreloadStartupRevealShader()
    {
        // Loading the resource before the scene is constructed moves resource lookup
        // out of the first visible reveal frame. The concrete graphics variant is
        // warmed once a material exists below.
        _cachedStartupRevealShader = Resources.Load<Shader>(StartupRevealShaderResource);
    }

    internal void PrimeStartupRevealHiddenState()
    {
        if (!Application.isPlaying || !playStartupMapReveal || map == null ||
            _runtimeRoot == null || _tileVisuals.Count == 0)
            return;

        if (_startupRevealPrimed && _startupPrimedRuntimeRoot == _runtimeRoot) return;

        ClearStartupRevealVisuals();
        _startupPrimedRuntimeRoot = _runtimeRoot;

        // Cache bounds before disabling any renderer. Some prefab renderers report
        // unusable bounds after their hierarchy is collapsed for the reveal.
        foreach (KeyValuePair<Vector2Int, TileVisualState> pair in _tileVisuals)
            _startupTileTopByCell[pair.Key] = ResolveStartupTileTop(pair.Value);

        CaptureAndHideStartupMapVisuals();
        CaptureAndHideStartupMainTower(FindFirstObjectByType<RougeMainTower>());
        _startupRevealPrimed = true;
    }

    internal void CancelStartupReveal()
    {
        ClearStartupRevealVisuals();
    }

    public IEnumerator PlayStartupReveal(RougeMainTower mainTower)
    {
        if (!Application.isPlaying || !playStartupMapReveal || map == null ||
            _runtimeRoot == null || _tileVisuals.Count == 0)
        {
            ClearStartupRevealVisuals();
            yield break;
        }

        if (!_startupRevealPrimed || _startupPrimedRuntimeRoot != _runtimeRoot)
            PrimeStartupRevealHiddenState();
        EnsureStartupMainTower(mainTower);
        DestroyStartupRevealEffectObjects();

        bool hasCrystalVisual = BuildStartupRevealVisuals();
        UpdateStartupTileTransforms(0f);
        bool hasMainTowerCrystal = BuildStartupMainTowerRevealVisuals();
        UpdateStartupMainTowerTransform(0f);

        // Material.SetPass requests the graphics variant up front. Waiting for the
        // end of the frame then guarantees that the mesh/material has gone through
        // the render pipeline at least once before animation time starts. The next
        // Update is intentionally discarded because its delta contains any compile
        // or GPU upload stall from that first render.
        if (hasCrystalVisual || hasMainTowerCrystal)
            WarmStartupRevealMaterials();
        yield return StartupRevealEndOfFrame;
        yield return null;
        yield return null;

        if (_runtimeRoot == null)
        {
            ClearStartupRevealVisuals();
            yield break;
        }

        // 1. Empty anchor zone. The circuit backdrop is the only visible surface;
        // the base, board, pads and arena frame are all still captured and hidden.
        float duration = Mathf.Max(0f, startupEmptyZoneHoldDuration);
        float elapsed = 0f;
        while (elapsed < duration && _runtimeRoot != null)
        {
            elapsed = Mathf.Min(duration, elapsed + GetStartupRevealFrameDelta());
            yield return null;
        }

        // 2. The anchor vehicle is phase-transmitted and establishes the only
        // physical point that exists before the adjutant's battlefield model.
        float towerDuration = _startupMainTower != null
            ? Mathf.Max(0.2f, startupMainTowerRevealDuration)
            : 0f;
        elapsed = 0f;
        while (elapsed < towerDuration && _runtimeRoot != null)
        {
            elapsed = Mathf.Min(towerDuration,
                elapsed + GetStartupRevealFrameDelta());
            float towerProgress = Mathf.Clamp01(elapsed / towerDuration);
            UpdateStartupMainTowerTransform(towerProgress);
            if (_startupMainTowerRevealMaterial != null)
                _startupMainTowerRevealMaterial.SetFloat(
                    StartupProgressId, towerProgress);
            yield return null;
        }
        UpdateStartupMainTowerTransform(1f);
        RestoreStartupMainTowerVisuals();

        // A short readable beat separates physical anchoring from AI scanning.
        duration = Mathf.Max(0f, startupAnchorHoldDuration);
        elapsed = 0f;
        while (elapsed < duration && _runtimeRoot != null)
        {
            elapsed = Mathf.Min(duration, elapsed + GetStartupRevealFrameDelta());
            yield return null;
        }

        // 3. The adjutant scans outward from the anchor and resolves the tactical
        // grid. The reveal origin follows MainTowerCell on asymmetric maps too.
        duration = Mathf.Max(0.1f, startupRevealDuration);
        elapsed = 0f;
        while (elapsed < duration && _runtimeRoot != null)
        {
            elapsed = Mathf.Min(duration, elapsed + GetStartupRevealFrameDelta());
            float progress = Mathf.Clamp01(elapsed / duration);
            UpdateStartupTileTransforms(progress);
            SetStartupBackdropTacticalVisibility(Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.03f, 0.88f, progress)));
            if (_startupRevealMaterial != null)
                _startupRevealMaterial.SetFloat(StartupProgressId, progress);
            yield return null;
        }

        // 4. The reconstructed board becomes authoritative, then the perimeter
        // seal flashes once to confirm that the anchor zone is combat-ready.
        RestoreStartupMapVisuals();
        if (_startupRevealMaterial != null)
            _startupRevealMaterial.SetFloat(StartupProgressId, 1f);

        float flashDuration = Mathf.Max(0.05f, startupEdgeFlashDuration);
        elapsed = 0f;
        while (elapsed < flashDuration && _runtimeRoot != null)
        {
            elapsed = Mathf.Min(flashDuration,
                elapsed + GetStartupRevealFrameDelta());
            float flashProgress = Mathf.Clamp01(elapsed / flashDuration);
            float envelope = Mathf.Sin(flashProgress * Mathf.PI);
            float flicker = Mathf.Lerp(0.62f, 1f,
                0.5f + 0.5f * Mathf.Sin(flashProgress * Mathf.PI * 7f));
            float flash = envelope * flicker;
            if (_startupBoundaryMaterial != null)
                _startupBoundaryMaterial.SetFloat(StartupEdgeFlashId, flash);
            if (_startupRevealMaterial != null)
                _startupRevealMaterial.SetFloat(
                    StartupOverallFadeId, 1f - flashProgress);
            yield return null;
        }

        duration = Mathf.Max(0f, startupReadyHoldDuration);
        elapsed = 0f;
        while (elapsed < duration && _runtimeRoot != null)
        {
            elapsed = Mathf.Min(duration, elapsed + GetStartupRevealFrameDelta());
            yield return null;
        }

        _startupRevealPrimed = false;
        _startupPrimedRuntimeRoot = null;
        _startupTileTopByCell.Clear();
        ClearStartupRevealVisuals(false);
    }

    private bool BuildStartupRevealVisuals()
    {
        Shader shader = _cachedStartupRevealShader;
        if (shader == null)
        {
            shader = Resources.Load<Shader>(StartupRevealShaderResource);
            _cachedStartupRevealShader = shader;
        }
        if (shader == null) shader = Shader.Find("Rouge/Map Startup Crystal");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning(
                "Map startup crystal shader is missing or unsupported. " +
                "The tile reveal will continue without the crystal overlay.", this);
            return false;
        }

        _cachedStartupRevealShader = shader;

        _startupRevealRoot = new GameObject("Map Startup Crystal Reveal");
        _startupRevealRoot.transform.SetParent(_runtimeRoot.transform, false);

        _startupRevealMaterial = new Material(shader)
        {
            name = "Runtime Map Startup Crystal",
            hideFlags = HideFlags.DontSave,
            enableInstancing = true
        };
        _startupRevealMaterial.SetColor(StartupCrystalColorId, startupCrystalColor);
        _startupRevealMaterial.SetFloat(StartupProgressId, 0f);
        _startupRevealMaterial.SetFloat(StartupOverallFadeId, 1f);
        _startupRevealMaterial.SetFloat(StartupEdgeModeId, 0f);
        _startupRevealMaterial.SetFloat(StartupRevealWindowId, 0.24f);
        _startupRevealMaterial.SetFloat(StartupUseVertexRevealDataId, 1f);
        _startupRevealMaterial.SetFloat(StartupUseMainTextureAlphaId, 0f);

        _startupBoundaryMaterial = new Material(_startupRevealMaterial)
        {
            name = "Runtime Map Startup Edge Flash",
            hideFlags = HideFlags.DontSave
        };
        _startupBoundaryMaterial.SetFloat(StartupEdgeModeId, 1f);
        _startupBoundaryMaterial.SetFloat(StartupEdgeFlashId, 0f);

        _startupRevealMesh = BuildStartupTileMesh();
        _startupBoundaryMesh = BuildStartupBoundaryMesh();
        MeshRenderer cells = CreateStartupMeshObject(
            "Crystal Cells", _startupRevealMesh, _startupRevealMaterial);
        CreateStartupMeshObject(
            "Boundary Flash", _startupBoundaryMesh, _startupBoundaryMaterial);
        if (cells == null)
        {
            Debug.LogWarning(
                "Map startup crystal mesh could not be created. " +
                "The tile reveal will continue without the crystal overlay.", this);
            return false;
        }

        return true;
    }

    private void WarmStartupRevealMaterials()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            return;

        bool ready = _startupRevealMaterial == null || _startupRevealMaterial.SetPass(0);
        ready &= _startupMainTowerRevealMaterial == null ||
                 _startupMainTowerRevealMaterial.SetPass(0);
        if (!ready)
        {
            Debug.LogWarning(
                "Map startup crystal shader pass could not be prepared. " +
                "The tile reveal will still continue.", this);
        }
    }

    private bool BuildStartupMainTowerRevealVisuals()
    {
        if (_startupMainTower == null || _startupRevealRoot == null ||
            _startupRevealMaterial == null)
            return false;

        _startupMainTowerRevealMaterial = new Material(_startupRevealMaterial)
        {
            name = "Runtime Main Tower Startup Crystal",
            hideFlags = HideFlags.DontSave,
            enableInstancing = false
        };
        _startupMainTowerRevealMaterial.SetColor(
            StartupCrystalColorId, startupCrystalColor * 1.18f);
        _startupMainTowerRevealMaterial.SetFloat(StartupProgressId, 0f);
        _startupMainTowerRevealMaterial.SetFloat(StartupOverallFadeId, 1f);
        _startupMainTowerRevealMaterial.SetFloat(StartupEdgeModeId, 0f);
        _startupMainTowerRevealMaterial.SetFloat(StartupRevealWindowId, 1f);
        _startupMainTowerRevealMaterial.SetFloat(StartupUseVertexRevealDataId, 0f);
        _startupMainTowerRevealMaterial.SetFloat(StartupUseMainTextureAlphaId, 0f);

        int createdCount = 0;
        foreach (KeyValuePair<Renderer, bool> pair in _startupMainTowerRendererStates)
        {
            Renderer source = pair.Key;
            if (source == null || !pair.Value || !source.gameObject.activeInHierarchy) continue;
            if (source is SpriteRenderer spriteRenderer)
            {
                if (CreateStartupMainTowerSpriteProxy(spriteRenderer)) createdCount++;
                continue;
            }

            if (source is MeshRenderer meshRenderer)
            {
                MeshFilter sourceFilter = meshRenderer.GetComponent<MeshFilter>();
                if (sourceFilter != null && sourceFilter.sharedMesh != null &&
                    CreateStartupMainTowerMeshProxy(
                        source.transform, sourceFilter.sharedMesh, source))
                    createdCount++;
                continue;
            }

            if (source is SkinnedMeshRenderer skinnedRenderer)
            {
                Mesh bakedMesh = new Mesh
                {
                    name = $"{source.name} Startup Crystal Mesh",
                    hideFlags = HideFlags.DontSave
                };
                skinnedRenderer.BakeMesh(bakedMesh);
                if (bakedMesh.vertexCount > 0 && CreateStartupMainTowerMeshProxy(
                        source.transform, bakedMesh, source))
                {
                    _startupMainTowerRevealMeshes.Add(bakedMesh);
                    createdCount++;
                }
                else
                    DestroyStartupObject(bakedMesh);
            }
        }

        if (createdCount > 0) return true;
        Debug.LogWarning(
            "Main tower has no supported visible Renderer for the startup crystal overlay. " +
            "Its transform reveal will still play.", _startupMainTower);
        return false;
    }

    private bool CreateStartupMainTowerSpriteProxy(SpriteRenderer source)
    {
        Sprite sprite = source != null ? source.sprite : null;
        if (sprite == null) return false;

        Vector2[] spriteVertices = sprite.vertices;
        Vector2[] spriteTextureUvs = sprite.uv;
        ushort[] spriteTriangles = sprite.triangles;
        if (spriteVertices == null || spriteVertices.Length == 0 ||
            spriteTextureUvs == null || spriteTextureUvs.Length != spriteVertices.Length ||
            spriteTriangles == null || spriteTriangles.Length == 0)
            return false;

        Bounds spriteBounds = sprite.bounds;
        Vector2 drawScale = Vector2.one;
        if (source.drawMode != SpriteDrawMode.Simple)
        {
            drawScale.x = source.size.x / Mathf.Max(0.0001f, spriteBounds.size.x);
            drawScale.y = source.size.y / Mathf.Max(0.0001f, spriteBounds.size.y);
        }

        Vector2 scaledCenter = new Vector2(
            spriteBounds.center.x * drawScale.x,
            spriteBounds.center.y * drawScale.y);
        Vector2 scaledSize = new Vector2(
            Mathf.Max(0.0001f, spriteBounds.size.x * drawScale.x),
            Mathf.Max(0.0001f, spriteBounds.size.y * drawScale.y));
        var vertices = new List<Vector3>(spriteVertices.Length);
        var patternUvs = new List<Vector2>(spriteVertices.Length);
        var textureUvs = new List<Vector2>(spriteTextureUvs.Length);
        var colors = new List<Color>(spriteVertices.Length);
        for (int i = 0; i < spriteVertices.Length; i++)
        {
            Vector2 vertex = Vector2.Scale(spriteVertices[i], drawScale);
            if (source.flipX) vertex.x = scaledCenter.x * 2f - vertex.x;
            if (source.flipY) vertex.y = scaledCenter.y * 2f - vertex.y;
            vertices.Add(new Vector3(vertex.x, vertex.y, 0f));
            patternUvs.Add(new Vector2(
                (vertex.x - scaledCenter.x) / scaledSize.x + 0.5f,
                (vertex.y - scaledCenter.y) / scaledSize.y + 0.5f));
            textureUvs.Add(spriteTextureUvs[i]);
            colors.Add(Color.white);
        }

        var triangles = new List<int>(spriteTriangles.Length);
        for (int i = 0; i < spriteTriangles.Length; i++)
            triangles.Add(spriteTriangles[i]);

        Mesh mesh = new Mesh
        {
            name = $"{source.name} Startup Crystal Sprite Mesh",
            hideFlags = HideFlags.DontSave
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, patternUvs);
        mesh.SetUVs(1, textureUvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();

        MeshRenderer proxy = CreateStartupMainTowerMeshProxyRenderer(
            source.transform, mesh, source);
        if (proxy == null)
        {
            DestroyStartupObject(mesh);
            return false;
        }

        var properties = new MaterialPropertyBlock();
        properties.SetTexture(StartupMainTextureId, sprite.texture);
        properties.SetFloat(StartupUseMainTextureAlphaId, 1f);
        proxy.SetPropertyBlock(properties);
        _startupMainTowerRevealMeshes.Add(mesh);
        return true;
    }

    private bool CreateStartupMainTowerMeshProxy(
        Transform sourceTransform, Mesh mesh, Renderer sourceRenderer)
    {
        return CreateStartupMainTowerMeshProxyRenderer(
            sourceTransform, mesh, sourceRenderer) != null;
    }

    private MeshRenderer CreateStartupMainTowerMeshProxyRenderer(
        Transform sourceTransform, Mesh mesh, Renderer sourceRenderer)
    {
        if (_startupRevealRoot == null || _startupMainTowerRevealMaterial == null ||
            sourceTransform == null || mesh == null || mesh.vertexCount == 0)
            return null;

        GameObject proxyObject = new GameObject(
            $"Main Tower Crystal - {sourceTransform.name}",
            typeof(MeshFilter), typeof(MeshRenderer));
        proxyObject.transform.SetParent(_startupRevealRoot.transform, false);
        proxyObject.layer = sourceTransform.gameObject.layer;
        CopyStartupWorldTransform(proxyObject.transform, sourceTransform);
        proxyObject.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer proxy = proxyObject.GetComponent<MeshRenderer>();
        proxy.sharedMaterial = _startupMainTowerRevealMaterial;
        proxy.enabled = true;
        proxy.allowOcclusionWhenDynamic = false;
        proxy.shadowCastingMode = ShadowCastingMode.Off;
        proxy.receiveShadows = false;
        if (sourceRenderer != null)
        {
            proxy.sortingLayerID = sourceRenderer.sortingLayerID;
            proxy.sortingOrder = sourceRenderer.sortingOrder + 250;
        }
        return proxy;
    }

    private static void CopyStartupWorldTransform(Transform target, Transform source)
    {
        target.SetPositionAndRotation(source.position, source.rotation);
        Vector3 sourceScale = source.lossyScale;
        Vector3 parentScale = target.parent != null
            ? target.parent.lossyScale
            : Vector3.one;
        target.localScale = new Vector3(
            DivideStartupScale(sourceScale.x, parentScale.x),
            DivideStartupScale(sourceScale.y, parentScale.y),
            DivideStartupScale(sourceScale.z, parentScale.z));
    }

    private static float DivideStartupScale(float value, float divisor)
    {
        return Mathf.Abs(divisor) > 0.00001f ? value / divisor : value;
    }

    private static float GetStartupRevealFrameDelta()
    {
        float delta = Time.unscaledDeltaTime;
        if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta)) return 0f;
        return Mathf.Min(delta, StartupRevealMaximumFrameDelta);
    }

    private Mesh BuildStartupTileMesh()
    {
        var vertices = new List<Vector3>(_tileVisuals.Count * 4);
        var uvs = new List<Vector2>(_tileVisuals.Count * 4);
        var colors = new List<Color>(_tileVisuals.Count * 4);
        var triangles = new List<int>(_tileVisuals.Count * 6);
        foreach (KeyValuePair<Vector2Int, TileVisualState> pair in _tileVisuals)
        {
            Vector2Int cell = pair.Key;
            TileVisualState visual = pair.Value;
            float top = (_startupTileTopByCell.TryGetValue(cell, out float cachedTop)
                ? cachedTop
                : ResolveStartupTileTop(visual)) + 0.035f;
            float x0 = map.Origin.x + cell.x * map.CellSize;
            float x1 = x0 + map.CellSize;
            float z0 = map.Origin.y + cell.y * map.CellSize;
            float z1 = z0 + map.CellSize;
            float delay = ResolveStartupRevealDelay(cell);
            float variation = HashCell(cell);
            int start = vertices.Count;
            vertices.Add(new Vector3(x0, top, z0));
            vertices.Add(new Vector3(x0, top, z1));
            vertices.Add(new Vector3(x1, top, z1));
            vertices.Add(new Vector3(x1, top, z0));
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 0f));
            Color data = new Color(delay, variation, 0f, 1f);
            colors.Add(data);
            colors.Add(data);
            colors.Add(data);
            colors.Add(data);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
        return CreateStartupMesh("Map Startup Crystal Cells", vertices, uvs, colors, triangles);
    }

    private Mesh BuildStartupBoundaryMesh()
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        float width = Mathf.Clamp(map.CellSize * 0.065f, 0.08f, 0.7f);
        foreach (KeyValuePair<Vector2Int, TileVisualState> pair in _tileVisuals)
        {
            Vector2Int cell = pair.Key;
            float top = (_startupTileTopByCell.TryGetValue(cell, out float cachedTop)
                ? cachedTop
                : ResolveStartupTileTop(pair.Value)) + 0.075f;
            float x0 = map.Origin.x + cell.x * map.CellSize;
            float x1 = x0 + map.CellSize;
            float z0 = map.Origin.y + cell.y * map.CellSize;
            float z1 = z0 + map.CellSize;
            if (!HasStartupTile(cell + Vector2Int.left))
                AddStartupQuad(vertices, uvs, colors, triangles,
                    new Vector3(x0, top, z0), new Vector3(x0 + width, top, z0),
                    new Vector3(x0 + width, top, z1), new Vector3(x0, top, z1));
            if (!HasStartupTile(cell + Vector2Int.right))
                AddStartupQuad(vertices, uvs, colors, triangles,
                    new Vector3(x1 - width, top, z0), new Vector3(x1, top, z0),
                    new Vector3(x1, top, z1), new Vector3(x1 - width, top, z1));
            if (!HasStartupTile(cell + Vector2Int.down))
                AddStartupQuad(vertices, uvs, colors, triangles,
                    new Vector3(x0, top, z0), new Vector3(x0, top, z0 + width),
                    new Vector3(x1, top, z0 + width), new Vector3(x1, top, z0));
            if (!HasStartupTile(cell + Vector2Int.up))
                AddStartupQuad(vertices, uvs, colors, triangles,
                    new Vector3(x0, top, z1 - width), new Vector3(x0, top, z1),
                    new Vector3(x1, top, z1), new Vector3(x1, top, z1 - width));
        }
        return CreateStartupMesh("Map Startup Boundary", vertices, uvs, colors, triangles);
    }

    private void UpdateStartupTileTransforms(float progress)
    {
        foreach (KeyValuePair<Vector2Int, TileVisualState> pair in _tileVisuals)
        {
            TileVisualState visual = pair.Value;
            if (visual == null || visual.root == null) continue;
            float delay = ResolveStartupRevealDelay(pair.Key);
            float local = Mathf.Clamp01((progress - delay) / 0.24f);
            float smooth = local * local * (3f - 2f * local);
            float overshoot = Mathf.Sin(local * Mathf.PI) * (1f - local) * 0.12f;
            float scale = smooth + overshoot;
            visual.root.localScale = visual.originalLocalScale * scale;
            visual.root.localPosition = visual.originalLocalPosition +
                                        Vector3.down * ((1f - smooth) * 0.75f);
            SetStartupTileRenderersVisible(visual, local > 0.001f);
        }
    }

    private void CaptureAndHideStartupMapVisuals()
    {
        _startupTileRendererStates.Clear();
        foreach (TileVisualState visual in _tileVisuals.Values)
        {
            if (visual?.renderers == null) continue;
            for (int i = 0; i < visual.renderers.Length; i++)
                CaptureAndHideRenderer(visual.renderers[i], _startupTileRendererStates);
        }

        _startupPresentationRendererStates.Clear();
        Transform presentation = _runtimeRoot != null
            ? _runtimeRoot.transform.Find("Arena Presentation")
            : null;
        if (presentation == null) return;
        Renderer[] presentationRenderers =
            presentation.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < presentationRenderers.Length; i++)
        {
            // This is the very large technology-grid plane beneath the arena, not
            // part of the board being reconstructed. It must stay visible from the
            // first rendered frame while Foundation/Armor/Energy pieces stay hidden.
            Renderer renderer = presentationRenderers[i];
            if (renderer != null && renderer.transform.parent == presentation &&
                renderer.transform.name == StartupBackdropObjectName)
            {
                CaptureAndHideStartupBackdropTacticalLayer(renderer);
                continue;
            }
            CaptureAndHideRenderer(
                renderer, _startupPresentationRendererStates);
        }
    }

    private static void CaptureAndHideRenderer(
        Renderer renderer, Dictionary<Renderer, bool> states)
    {
        if (renderer == null || states.ContainsKey(renderer)) return;
        states.Add(renderer, renderer.enabled);
        renderer.enabled = false;
    }

    private void SetStartupTileRenderersVisible(TileVisualState visual, bool visible)
    {
        if (visual?.renderers == null) return;
        for (int i = 0; i < visual.renderers.Length; i++)
        {
            Renderer renderer = visual.renderers[i];
            if (renderer == null ||
                !_startupTileRendererStates.TryGetValue(renderer, out bool originallyEnabled))
                continue;
            renderer.enabled = visible && originallyEnabled;
        }
    }

    private void RestoreStartupMapVisuals()
    {
        SetStartupBackdropTacticalVisibility(1f);
        _startupBackdropMaterial = null;
        RestoreStartupTileTransforms();
        RestoreRendererStates(_startupTileRendererStates);
        RestoreRendererStates(_startupPresentationRendererStates);
    }

    private void CaptureAndHideStartupBackdropTacticalLayer(Renderer renderer)
    {
        _startupBackdropMaterial = renderer != null
            ? renderer.sharedMaterial
            : null;
        if (_startupBackdropMaterial == null) return;
        _startupBackdropGridColor = _startupBackdropMaterial.HasProperty("_GridColor")
            ? _startupBackdropMaterial.GetColor("_GridColor")
            : Color.black;
        _startupBackdropAccentColor = _startupBackdropMaterial.HasProperty("_AccentColor")
            ? _startupBackdropMaterial.GetColor("_AccentColor")
            : Color.black;
        _startupBackdropLineIntensity = _startupBackdropMaterial.HasProperty("_LineIntensity")
            ? _startupBackdropMaterial.GetFloat("_LineIntensity")
            : 0f;
        SetStartupBackdropTacticalVisibility(0f);
    }

    private void SetStartupBackdropTacticalVisibility(float value)
    {
        if (_startupBackdropMaterial == null) return;
        float visibility = Mathf.Clamp01(value);
        if (_startupBackdropMaterial.HasProperty("_GridColor"))
            _startupBackdropMaterial.SetColor("_GridColor",
                Color.Lerp(Color.black, _startupBackdropGridColor, visibility));
        if (_startupBackdropMaterial.HasProperty("_AccentColor"))
            _startupBackdropMaterial.SetColor("_AccentColor",
                Color.Lerp(Color.black, _startupBackdropAccentColor, visibility));
        if (_startupBackdropMaterial.HasProperty("_LineIntensity"))
            _startupBackdropMaterial.SetFloat("_LineIntensity",
                _startupBackdropLineIntensity * visibility);
    }

    private static void RestoreRendererStates(Dictionary<Renderer, bool> states)
    {
        foreach (KeyValuePair<Renderer, bool> pair in states)
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        states.Clear();
    }

    private void RestoreStartupTileTransforms()
    {
        foreach (TileVisualState visual in _tileVisuals.Values)
        {
            if (visual == null || visual.root == null) continue;
            visual.root.localPosition = visual.originalLocalPosition;
            visual.root.localScale = visual.originalLocalScale;
        }
    }

    private void CaptureAndHideStartupMainTower(RougeMainTower tower)
    {
        RestoreStartupMainTowerVisuals();
        if (tower == null) return;

        _startupMainTower = tower;
        Transform towerTransform = tower.transform;
        _startupMainTowerOriginalLocalPosition = towerTransform.localPosition;
        _startupMainTowerOriginalLocalScale = towerTransform.localScale;
        Renderer[] renderers = tower.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            _startupMainTowerRendererStates[renderer] = renderer.enabled;
            if (renderer is SpriteRenderer spriteRenderer)
                _startupMainTowerSpriteColors[spriteRenderer] = spriteRenderer.color;
            renderer.enabled = false;
        }
    }

    private void EnsureStartupMainTower(RougeMainTower tower)
    {
        if (tower == null || tower == _startupMainTower) return;
        CaptureAndHideStartupMainTower(tower);
    }

    private void UpdateStartupMainTowerTransform(float progress)
    {
        if (_startupMainTower == null) return;
        Transform towerTransform = _startupMainTower.transform;
        float normalized = Mathf.Clamp01(progress);
        float smooth = normalized * normalized * (3f - 2f * normalized);
        float overshoot = Mathf.Sin(normalized * Mathf.PI) *
                          (1f - normalized) * 0.045f;
        float phaseScale = Mathf.Lerp(0.9f, 1f, smooth) + overshoot;
        towerTransform.localScale = _startupMainTowerOriginalLocalScale *
                                    phaseScale;
        towerTransform.localPosition = _startupMainTowerOriginalLocalPosition +
                                       Vector3.up * ((1f - smooth) * 0.16f);

        foreach (KeyValuePair<Renderer, bool> pair in _startupMainTowerRendererStates)
        {
            Renderer renderer = pair.Key;
            if (renderer == null) continue;
            if (!pair.Value)
            {
                renderer.enabled = false;
                continue;
            }

            if (renderer is SpriteRenderer spriteRenderer &&
                _startupMainTowerSpriteColors.TryGetValue(
                    spriteRenderer, out Color originalColor))
            {
                originalColor.a *= smooth;
                spriteRenderer.color = originalColor;
                renderer.enabled = normalized > 0.001f;
            }
            else
                renderer.enabled = normalized >= 0.52f;
        }
    }

    private void RestoreStartupMainTowerVisuals()
    {
        if (_startupMainTower != null)
        {
            Transform towerTransform = _startupMainTower.transform;
            towerTransform.localPosition = _startupMainTowerOriginalLocalPosition;
            towerTransform.localScale = _startupMainTowerOriginalLocalScale;
        }

        foreach (KeyValuePair<SpriteRenderer, Color> pair in
                 _startupMainTowerSpriteColors)
            if (pair.Key != null)
                pair.Key.color = pair.Value;
        _startupMainTowerSpriteColors.Clear();
        RestoreRendererStates(_startupMainTowerRendererStates);
        _startupMainTower = null;
    }

    private void ClearStartupRevealVisuals(bool restoreVisuals = true)
    {
        if (restoreVisuals)
        {
            RestoreStartupMapVisuals();
            RestoreStartupMainTowerVisuals();
            _startupRevealPrimed = false;
            _startupPrimedRuntimeRoot = null;
            _startupTileTopByCell.Clear();
        }
        DestroyStartupRevealEffectObjects();
    }

    private void DestroyStartupRevealEffectObjects()
    {
        DestroyStartupObject(_startupRevealRoot);
        DestroyStartupObject(_startupRevealMesh);
        DestroyStartupObject(_startupBoundaryMesh);
        DestroyStartupObject(_startupRevealMaterial);
        DestroyStartupObject(_startupBoundaryMaterial);
        DestroyStartupObject(_startupMainTowerRevealMaterial);
        for (int i = 0; i < _startupMainTowerRevealMeshes.Count; i++)
            DestroyStartupObject(_startupMainTowerRevealMeshes[i]);
        _startupMainTowerRevealMeshes.Clear();
        _startupRevealRoot = null;
        _startupRevealMesh = null;
        _startupBoundaryMesh = null;
        _startupRevealMaterial = null;
        _startupBoundaryMaterial = null;
        _startupMainTowerRevealMaterial = null;
    }

    private MeshRenderer CreateStartupMeshObject(string objectName, Mesh mesh, Material material)
    {
        if (_startupRevealRoot == null || mesh == null || mesh.vertexCount == 0 || material == null)
            return null;
        GameObject child = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        child.transform.SetParent(_startupRevealRoot.transform, false);
        child.layer = gameObject.layer;
        child.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = child.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.enabled = true;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.sortingOrder = 250;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private Mesh CreateStartupMesh(string meshName, List<Vector3> vertices,
        List<Vector2> uvs, List<Color> colors, List<int> triangles)
    {
        Mesh mesh = new Mesh { name = meshName, hideFlags = HideFlags.DontSave };
        if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddStartupQuad(List<Vector3> vertices, List<Vector2> uvs,
        List<Color> colors, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));
        colors.Add(Color.white);
        colors.Add(Color.white);
        colors.Add(Color.white);
        colors.Add(Color.white);
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private float ResolveStartupTileTop(TileVisualState visual)
    {
        float top = visual != null && visual.root != null
            ? visual.root.position.y
            : 0f;
        if (visual?.renderers == null) return top;
        for (int i = 0; i < visual.renderers.Length; i++)
            if (visual.renderers[i] != null)
                top = Mathf.Max(top, visual.renderers[i].bounds.max.y);
        return top;
    }

    private float ResolveStartupRevealDelay(Vector2Int cell)
    {
        Vector2 source = map.HasMainTower
            ? new Vector2(map.MainTowerCell.x, map.MainTowerCell.y)
            : new Vector2((map.Width - 1) * 0.5f, (map.Height - 1) * 0.5f);
        float maxX = Mathf.Max(source.x, map.Width - 1f - source.x);
        float maxY = Mathf.Max(source.y, map.Height - 1f - source.y);
        float maxDistance = Mathf.Max(1f,
            Mathf.Sqrt(maxX * maxX + maxY * maxY));
        float radial = Mathf.Clamp01(Vector2.Distance(cell, source) / maxDistance);
        return 0.04f + radial * 0.56f + HashCell(cell) * 0.08f;
    }

    private bool HasStartupTile(Vector2Int cell)
    {
        return _tileVisuals.ContainsKey(cell);
    }

    private static float HashCell(Vector2Int cell)
    {
        uint value = (uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663);
        value ^= value >> 13;
        value *= 1274126177u;
        return (value & 0x00ffffffu) / 16777215f;
    }

    private void DestroyStartupObject(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
