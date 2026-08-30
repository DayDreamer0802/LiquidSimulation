using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class RougeTowerDefenseMapLoader
{
    private const float VictoryRecallMaximumFrameDelta = 1f / 30f;
    private const float VictoryRecallTileWindow = 0.34f;
    private static readonly WaitForEndOfFrame VictoryRecallEndOfFrame =
        new WaitForEndOfFrame();

    private sealed class VictoryRecallRendererState
    {
        public Renderer renderer;
        public bool enabled;
        public bool hasSpriteColor;
        public Color spriteColor;
    }

    private sealed class VictoryRecallTileState
    {
        public Vector2Int cell;
        public Transform root;
        public Vector3 originalLocalPosition;
        public Vector3 originalLocalScale;
        public Vector3 anchorLocalPosition;
        public float normalizedDistance;
        public float top;
        public readonly List<VictoryRecallRendererState> renderers =
            new List<VictoryRecallRendererState>();
    }

    private sealed class VictoryRecallMainTowerState
    {
        public RougeMainTower tower;
        public Vector3 originalLocalPosition;
        public Vector3 originalLocalScale;
        public readonly List<VictoryRecallRendererState> renderers =
            new List<VictoryRecallRendererState>();
    }

    private sealed class VictoryRecallState
    {
        public GameObject root;
        public Material tileMaterial;
        public Material mainTowerMaterial;
        public Mesh tileMesh;
        public readonly List<Mesh> ownedMeshes = new List<Mesh>();
        public readonly List<Material> ownedMaterials = new List<Material>();
        public readonly List<VictoryRecallTileState> tiles =
            new List<VictoryRecallTileState>();
        public readonly List<VictoryRecallRendererState> presentationRenderers =
            new List<VictoryRecallRendererState>();
        public VictoryRecallMainTowerState mainTower;
        public bool committed;
        public bool cleaned;
    }

    private VictoryRecallState _activeVictoryRecall;

    public bool CanPlayVictoryRecall => Application.isPlaying && map != null &&
        _runtimeRoot != null && _tileVisuals.Count > 0;

    /// <summary>
    /// Collapses the board from its far edge toward <paramref name="anchor"/>, then
    /// crystalizes the main tower away. The animation uses unscaled time and leaves
    /// the runtime arena hidden on successful completion.
    /// </summary>
    public IEnumerator PlayVictoryRecallToAnchor(Vector3 anchor,
        RougeMainTower mainTower, float tileDuration = 2.1f,
        float mainTowerDuration = 0.85f)
    {
        CancelVictoryRecall();
        if (!Application.isPlaying || map == null || _runtimeRoot == null ||
            _tileVisuals.Count == 0)
            yield break;

        var state = new VictoryRecallState();
        _activeVictoryRecall = state;
        try
        {
            CaptureVictoryRecallTiles(state, anchor);
            CaptureVictoryRecallPresentation(state);
            CaptureVictoryRecallMainTower(state, mainTower);
            bool hasCrystalVisual = BuildVictoryRecallVisuals(state);

            if (hasCrystalVisual)
            {
                WarmVictoryRecallMaterials(state);
                yield return VictoryRecallEndOfFrame;
                yield return null;
                yield return null;
            }

            if (!IsVictoryRecallActive(state)) yield break;

            float duration = Mathf.Max(0.05f, tileDuration);
            float elapsed = 0f;
            while (elapsed < duration && IsVictoryRecallActive(state))
            {
                elapsed = Mathf.Min(duration,
                    elapsed + GetVictoryRecallFrameDelta());
                float progress = Mathf.Clamp01(elapsed / duration);
                UpdateVictoryRecallTiles(state, progress);
                if (state.tileMaterial != null)
                    state.tileMaterial.SetFloat(StartupProgressId, 1f - progress);
                yield return null;
            }

            if (!IsVictoryRecallActive(state)) yield break;
            UpdateVictoryRecallTiles(state, 1f);
            if (state.tileMaterial != null)
                state.tileMaterial.SetFloat(StartupProgressId, 0f);
            HideVictoryRecallPresentation(state);

            if (state.mainTower != null && state.mainTower.tower != null)
            {
                duration = Mathf.Max(0.05f, mainTowerDuration);
                elapsed = 0f;
                while (elapsed < duration && IsVictoryRecallActive(state))
                {
                    elapsed = Mathf.Min(duration,
                        elapsed + GetVictoryRecallFrameDelta());
                    float progress = Mathf.Clamp01(elapsed / duration);
                    UpdateVictoryRecallMainTower(state.mainTower, progress);
                    if (state.mainTowerMaterial != null)
                        state.mainTowerMaterial.SetFloat(
                            StartupProgressId, 1f - progress);
                    yield return null;
                }

                if (!IsVictoryRecallActive(state)) yield break;
                UpdateVictoryRecallMainTower(state.mainTower, 1f);
                if (state.mainTowerMaterial != null)
                    state.mainTowerMaterial.SetFloat(StartupProgressId, 0f);
            }

            // Commit only after both phases are fully invisible. Cleanup below must
            // not restore captured visuals, and the arena root stays inactive so a
            // later presentation/scene transition cannot flash the board back on.
            state.committed = true;
            if (_runtimeRoot != null) _runtimeRoot.SetActive(false);
        }
        finally
        {
            if (_activeVictoryRecall == state)
            {
                bool restoreVisuals = !state.committed && _runtimeRoot != null;
                CleanupVictoryRecall(state, restoreVisuals);
                _activeVictoryRecall = null;
            }
            else
                CleanupVictoryRecall(state, false);
        }
    }

    /// <summary>
    /// Stops an in-progress recall. By default the board and main tower are restored
    /// to their captured state; scene teardown can request resource cleanup only.
    /// </summary>
    public void CancelVictoryRecall(bool restoreVisuals = true)
    {
        VictoryRecallState state = _activeVictoryRecall;
        if (state == null) return;
        _activeVictoryRecall = null;
        CleanupVictoryRecall(state, restoreVisuals && !state.committed);
    }

    private void OnDestroy()
    {
        VictoryRecallState state = _activeVictoryRecall;
        _activeVictoryRecall = null;
        CleanupVictoryRecall(state, false);
    }

    private bool IsVictoryRecallActive(VictoryRecallState state)
    {
        return state != null && !state.cleaned &&
               _activeVictoryRecall == state && _runtimeRoot != null;
    }

    private void CaptureVictoryRecallTiles(VictoryRecallState state,
        Vector3 anchor)
    {
        float maximumDistance = 0f;
        foreach (KeyValuePair<Vector2Int, TileVisualState> pair in _tileVisuals)
        {
            TileVisualState visual = pair.Value;
            if (visual == null || visual.root == null) continue;

            Transform tileRoot = visual.root;
            Vector3 worldPosition = tileRoot.position;
            var tile = new VictoryRecallTileState
            {
                cell = pair.Key,
                root = tileRoot,
                originalLocalPosition = tileRoot.localPosition,
                originalLocalScale = tileRoot.localScale,
                anchorLocalPosition = tileRoot.parent != null
                    ? tileRoot.parent.InverseTransformPoint(anchor)
                    : anchor,
                normalizedDistance = Vector2.Distance(
                    new Vector2(worldPosition.x, worldPosition.z),
                    new Vector2(anchor.x, anchor.z)),
                top = ResolveStartupTileTop(visual)
            };
            CaptureVictoryRendererStates(visual.renderers, tile.renderers);
            maximumDistance = Mathf.Max(maximumDistance, tile.normalizedDistance);
            state.tiles.Add(tile);
        }

        maximumDistance = Mathf.Max(0.0001f, maximumDistance);
        for (int i = 0; i < state.tiles.Count; i++)
            state.tiles[i].normalizedDistance =
                Mathf.Clamp01(state.tiles[i].normalizedDistance / maximumDistance);
    }

    private void CaptureVictoryRecallPresentation(VictoryRecallState state)
    {
        Transform presentation = _runtimeRoot != null
            ? _runtimeRoot.transform.Find("Arena Presentation")
            : null;
        if (presentation == null) return;
        CaptureVictoryRendererStates(
            presentation.GetComponentsInChildren<Renderer>(true),
            state.presentationRenderers);
    }

    private static void CaptureVictoryRendererStates(Renderer[] renderers,
        List<VictoryRecallRendererState> destination)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            var captured = new VictoryRecallRendererState
            {
                renderer = renderer,
                enabled = renderer.enabled
            };
            if (renderer is SpriteRenderer spriteRenderer)
            {
                captured.hasSpriteColor = true;
                captured.spriteColor = spriteRenderer.color;
            }
            destination.Add(captured);
        }
    }

    private static void CaptureVictoryRecallMainTower(VictoryRecallState state,
        RougeMainTower mainTower)
    {
        if (mainTower == null) return;
        Transform towerTransform = mainTower.transform;
        var captured = new VictoryRecallMainTowerState
        {
            tower = mainTower,
            originalLocalPosition = towerTransform.localPosition,
            originalLocalScale = towerTransform.localScale
        };
        CaptureVictoryRendererStates(
            mainTower.GetComponentsInChildren<Renderer>(true),
            captured.renderers);
        state.mainTower = captured;
    }

    private bool BuildVictoryRecallVisuals(VictoryRecallState state)
    {
        Shader shader = _cachedStartupRevealShader;
        if (shader == null)
        {
            shader = Resources.Load<Shader>(StartupRevealShaderResource);
            _cachedStartupRevealShader = shader;
        }
        if (shader == null) shader = Shader.Find("Rouge/Map Startup Crystal");
        if (shader == null || !shader.isSupported) return false;
        _cachedStartupRevealShader = shader;

        state.root = new GameObject("Map Victory Crystal Recall");
        state.root.transform.SetParent(_runtimeRoot.transform, false);
        state.root.layer = gameObject.layer;

        state.tileMaterial = CreateVictoryRecallMaterial(
            state, shader, "Runtime Map Victory Crystal Recall");
        ConfigureVictoryRecallMaterial(state.tileMaterial,
            startupCrystalColor * 1.08f, VictoryRecallTileWindow, true);
        state.tileMesh = BuildVictoryRecallTileMesh(state);
        if (state.tileMesh != null)
        {
            state.ownedMeshes.Add(state.tileMesh);
            CreateVictoryRecallMeshObject(state, "Recall Crystal Cells",
                state.tileMesh, state.tileMaterial, null);
        }

        if (state.mainTower != null)
        {
            state.mainTowerMaterial = CreateVictoryRecallMaterial(
                state, shader, "Runtime Main Tower Victory Crystal Recall");
            ConfigureVictoryRecallMaterial(state.mainTowerMaterial,
                startupCrystalColor * 1.2f, 1f, false);
            BuildVictoryRecallMainTowerProxies(state);
        }

        return state.tileMesh != null || state.mainTowerMaterial != null;
    }

    private Material CreateVictoryRecallMaterial(VictoryRecallState state,
        Shader shader, string materialName)
    {
        var material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave,
            enableInstancing = false
        };
        state.ownedMaterials.Add(material);
        _runtimeMaterials.Add(material);
        return material;
    }

    private static void ConfigureVictoryRecallMaterial(Material material,
        Color crystalColor, float revealWindow, bool useVertexRevealData)
    {
        if (material == null) return;
        material.SetColor(StartupCrystalColorId, crystalColor);
        material.SetFloat(StartupProgressId, 1f);
        material.SetFloat(StartupOverallFadeId, 1f);
        material.SetFloat(StartupEdgeFlashId, 0f);
        material.SetFloat(StartupEdgeModeId, 0f);
        material.SetFloat(StartupRevealWindowId, revealWindow);
        material.SetFloat(StartupUseVertexRevealDataId,
            useVertexRevealData ? 1f : 0f);
        material.SetFloat(StartupUseMainTextureAlphaId, 0f);
    }

    private Mesh BuildVictoryRecallTileMesh(VictoryRecallState state)
    {
        if (state.tiles.Count == 0 || map == null) return null;
        var vertices = new List<Vector3>(state.tiles.Count * 4);
        var uvs = new List<Vector2>(state.tiles.Count * 4);
        var colors = new List<Color>(state.tiles.Count * 4);
        var triangles = new List<int>(state.tiles.Count * 6);
        for (int tileIndex = 0; tileIndex < state.tiles.Count; tileIndex++)
        {
            VictoryRecallTileState tile = state.tiles[tileIndex];
            Vector2Int cell = tile.cell;
            float top = tile.top + 0.035f;
            float x0 = map.Origin.x + cell.x * map.CellSize;
            float x1 = x0 + map.CellSize;
            float z0 = map.Origin.y + cell.y * map.CellSize;
            float z1 = z0 + map.CellSize;
            float variation = HashCell(cell);
            float delay = Mathf.Clamp01(0.04f +
                tile.normalizedDistance * 0.58f + variation * 0.025f);
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

        return CreateVictoryRecallMesh("Map Victory Crystal Recall",
            vertices, uvs, null, colors, triangles);
    }

    private void BuildVictoryRecallMainTowerProxies(VictoryRecallState state)
    {
        VictoryRecallMainTowerState tower = state.mainTower;
        if (tower == null || tower.tower == null ||
            state.root == null || state.mainTowerMaterial == null)
            return;

        for (int i = 0; i < tower.renderers.Count; i++)
        {
            VictoryRecallRendererState captured = tower.renderers[i];
            Renderer source = captured.renderer;
            if (source == null || !captured.enabled ||
                !source.gameObject.activeInHierarchy)
                continue;

            if (source is SpriteRenderer spriteRenderer)
            {
                CreateVictoryRecallSpriteProxy(state, spriteRenderer);
                continue;
            }

            if (source is MeshRenderer meshRenderer)
            {
                MeshFilter sourceFilter = meshRenderer.GetComponent<MeshFilter>();
                if (sourceFilter != null && sourceFilter.sharedMesh != null)
                    CreateVictoryRecallMeshObject(state,
                        $"Main Tower Recall - {source.name}",
                        sourceFilter.sharedMesh, state.mainTowerMaterial, source);
                continue;
            }

            if (source is SkinnedMeshRenderer skinnedRenderer)
            {
                var bakedMesh = new Mesh
                {
                    name = $"{source.name} Victory Recall Mesh",
                    hideFlags = HideFlags.DontSave
                };
                skinnedRenderer.BakeMesh(bakedMesh);
                if (bakedMesh.vertexCount > 0)
                {
                    state.ownedMeshes.Add(bakedMesh);
                    CreateVictoryRecallMeshObject(state,
                        $"Main Tower Recall - {source.name}",
                        bakedMesh, state.mainTowerMaterial, source);
                }
                else
                    DestroyVictoryRecallObject(bakedMesh);
            }
        }
    }

    private void CreateVictoryRecallSpriteProxy(VictoryRecallState state,
        SpriteRenderer source)
    {
        Sprite sprite = source != null ? source.sprite : null;
        if (sprite == null) return;
        Vector2[] spriteVertices = sprite.vertices;
        Vector2[] spriteTextureUvs = sprite.uv;
        ushort[] spriteTriangles = sprite.triangles;
        if (spriteVertices == null || spriteVertices.Length == 0 ||
            spriteTextureUvs == null || spriteTextureUvs.Length != spriteVertices.Length ||
            spriteTriangles == null || spriteTriangles.Length == 0)
            return;

        Bounds bounds = sprite.bounds;
        Vector2 drawScale = Vector2.one;
        if (source.drawMode != SpriteDrawMode.Simple)
        {
            drawScale.x = source.size.x / Mathf.Max(0.0001f, bounds.size.x);
            drawScale.y = source.size.y / Mathf.Max(0.0001f, bounds.size.y);
        }
        Vector2 center = Vector2.Scale(bounds.center, drawScale);
        Vector2 size = new Vector2(
            Mathf.Max(0.0001f, bounds.size.x * drawScale.x),
            Mathf.Max(0.0001f, bounds.size.y * drawScale.y));
        var vertices = new List<Vector3>(spriteVertices.Length);
        var patternUvs = new List<Vector2>(spriteVertices.Length);
        var textureUvs = new List<Vector2>(spriteVertices.Length);
        var colors = new List<Color>(spriteVertices.Length);
        for (int i = 0; i < spriteVertices.Length; i++)
        {
            Vector2 vertex = Vector2.Scale(spriteVertices[i], drawScale);
            if (source.flipX) vertex.x = center.x * 2f - vertex.x;
            if (source.flipY) vertex.y = center.y * 2f - vertex.y;
            vertices.Add(new Vector3(vertex.x, vertex.y, 0f));
            patternUvs.Add(new Vector2(
                (vertex.x - center.x) / size.x + 0.5f,
                (vertex.y - center.y) / size.y + 0.5f));
            textureUvs.Add(spriteTextureUvs[i]);
            colors.Add(Color.white);
        }
        var triangles = new List<int>(spriteTriangles.Length);
        for (int i = 0; i < spriteTriangles.Length; i++)
            triangles.Add(spriteTriangles[i]);

        Mesh mesh = CreateVictoryRecallMesh(
            $"{source.name} Victory Recall Sprite Mesh",
            vertices, patternUvs, textureUvs, colors, triangles);
        if (mesh == null) return;
        state.ownedMeshes.Add(mesh);
        MeshRenderer proxy = CreateVictoryRecallMeshObject(state,
            $"Main Tower Recall - {source.name}", mesh,
            state.mainTowerMaterial, source);
        if (proxy == null) return;
        var properties = new MaterialPropertyBlock();
        properties.SetTexture(StartupMainTextureId, sprite.texture);
        properties.SetFloat(StartupUseMainTextureAlphaId, 1f);
        proxy.SetPropertyBlock(properties);
    }

    private MeshRenderer CreateVictoryRecallMeshObject(VictoryRecallState state,
        string objectName, Mesh mesh, Material material, Renderer sourceRenderer)
    {
        if (state.root == null || mesh == null || mesh.vertexCount == 0 ||
            material == null)
            return null;

        GameObject proxyObject = new GameObject(
            objectName, typeof(MeshFilter), typeof(MeshRenderer));
        proxyObject.transform.SetParent(state.root.transform, false);
        proxyObject.layer = sourceRenderer != null
            ? sourceRenderer.gameObject.layer
            : gameObject.layer;
        if (sourceRenderer != null)
            CopyStartupWorldTransform(proxyObject.transform,
                sourceRenderer.transform);
        proxyObject.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer proxy = proxyObject.GetComponent<MeshRenderer>();
        int materialCount = Mathf.Max(1, mesh.subMeshCount);
        var materials = new Material[materialCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        proxy.sharedMaterials = materials;
        proxy.enabled = true;
        proxy.allowOcclusionWhenDynamic = false;
        proxy.shadowCastingMode = ShadowCastingMode.Off;
        proxy.receiveShadows = false;
        if (sourceRenderer != null)
        {
            proxy.sortingLayerID = sourceRenderer.sortingLayerID;
            proxy.sortingOrder = sourceRenderer.sortingOrder + 250;
        }
        else
            proxy.sortingOrder = 250;
        return proxy;
    }

    private static Mesh CreateVictoryRecallMesh(string meshName,
        List<Vector3> vertices, List<Vector2> patternUvs,
        List<Vector2> textureUvs, List<Color> colors, List<int> triangles)
    {
        if (vertices == null || vertices.Count == 0) return null;
        var mesh = new Mesh { name = meshName, hideFlags = HideFlags.DontSave };
        if (vertices.Count > ushort.MaxValue)
            mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, patternUvs);
        if (textureUvs != null && textureUvs.Count == vertices.Count)
            mesh.SetUVs(1, textureUvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void UpdateVictoryRecallTiles(VictoryRecallState state,
        float progress)
    {
        float normalized = Mathf.Clamp01(progress);
        for (int i = 0; i < state.tiles.Count; i++)
        {
            VictoryRecallTileState tile = state.tiles[i];
            if (tile.root == null) continue;
            float start = (1f - tile.normalizedDistance) * 0.52f;
            float local = Mathf.Clamp01((normalized - start) / 0.48f);
            float smooth = local * local * (3f - 2f * local);
            float remaining = 1f - smooth;
            Vector3 anchorPosition = tile.anchorLocalPosition;
            anchorPosition.y = tile.originalLocalPosition.y;
            tile.root.localPosition = Vector3.Lerp(
                tile.originalLocalPosition, anchorPosition, smooth * 0.16f) +
                Vector3.down * (smooth * 0.55f);
            tile.root.localScale = tile.originalLocalScale * remaining;
            SetVictoryRecallRendererProgress(tile.renderers, remaining,
                local < 0.999f);
        }
    }

    private static void UpdateVictoryRecallMainTower(
        VictoryRecallMainTowerState state, float progress)
    {
        if (state == null || state.tower == null) return;
        float normalized = Mathf.Clamp01(progress);
        float smooth = normalized * normalized * (3f - 2f * normalized);
        Transform towerTransform = state.tower.transform;
        towerTransform.localPosition = state.originalLocalPosition +
                                       Vector3.up * (smooth * 0.16f);
        towerTransform.localScale = state.originalLocalScale *
                                    Mathf.Lerp(1f, 0.9f, smooth);
        for (int i = 0; i < state.renderers.Count; i++)
        {
            VictoryRecallRendererState captured = state.renderers[i];
            Renderer renderer = captured.renderer;
            if (renderer == null) continue;
            if (!captured.enabled)
            {
                renderer.enabled = false;
                continue;
            }
            if (captured.hasSpriteColor && renderer is SpriteRenderer sprite)
            {
                Color color = captured.spriteColor;
                color.a *= 1f - smooth;
                sprite.color = color;
                renderer.enabled = normalized < 0.999f;
            }
            else
                renderer.enabled = normalized < 0.56f;
        }
    }

    private static void SetVictoryRecallRendererProgress(
        List<VictoryRecallRendererState> renderers, float alpha,
        bool visible)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            VictoryRecallRendererState captured = renderers[i];
            Renderer renderer = captured.renderer;
            if (renderer == null) continue;
            renderer.enabled = captured.enabled && visible;
            if (captured.hasSpriteColor && renderer is SpriteRenderer sprite)
            {
                Color color = captured.spriteColor;
                color.a *= Mathf.Clamp01(alpha);
                sprite.color = color;
            }
        }
    }

    private static void HideVictoryRecallPresentation(VictoryRecallState state)
    {
        for (int i = 0; i < state.presentationRenderers.Count; i++)
        {
            Renderer renderer = state.presentationRenderers[i].renderer;
            if (renderer != null) renderer.enabled = false;
        }
    }

    private static float GetVictoryRecallFrameDelta()
    {
        float delta = Time.unscaledDeltaTime;
        if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta))
            return 0f;
        return Mathf.Min(delta, VictoryRecallMaximumFrameDelta);
    }

    private static void WarmVictoryRecallMaterials(VictoryRecallState state)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        if (state.tileMaterial != null) state.tileMaterial.SetPass(0);
        if (state.mainTowerMaterial != null) state.mainTowerMaterial.SetPass(0);
    }

    private void CleanupVictoryRecall(VictoryRecallState state,
        bool restoreVisuals)
    {
        if (state == null || state.cleaned) return;
        state.cleaned = true;
        if (restoreVisuals)
        {
            RestoreVictoryRecallTiles(state);
            RestoreVictoryRecallRenderers(state.presentationRenderers);
            RestoreVictoryRecallMainTower(state.mainTower);
        }

        DestroyVictoryRecallObject(state.root);
        for (int i = 0; i < state.ownedMeshes.Count; i++)
            DestroyVictoryRecallObject(state.ownedMeshes[i]);
        for (int i = 0; i < state.ownedMaterials.Count; i++)
        {
            Material material = state.ownedMaterials[i];
            _runtimeMaterials.Remove(material);
            DestroyVictoryRecallObject(material);
        }
        state.ownedMeshes.Clear();
        state.ownedMaterials.Clear();
        state.root = null;
        state.tileMesh = null;
        state.tileMaterial = null;
        state.mainTowerMaterial = null;
    }

    private static void RestoreVictoryRecallTiles(VictoryRecallState state)
    {
        for (int i = 0; i < state.tiles.Count; i++)
        {
            VictoryRecallTileState tile = state.tiles[i];
            if (tile.root != null)
            {
                tile.root.localPosition = tile.originalLocalPosition;
                tile.root.localScale = tile.originalLocalScale;
            }
            RestoreVictoryRecallRenderers(tile.renderers);
        }
    }

    private static void RestoreVictoryRecallMainTower(
        VictoryRecallMainTowerState state)
    {
        if (state == null) return;
        if (state.tower != null)
        {
            state.tower.transform.localPosition = state.originalLocalPosition;
            state.tower.transform.localScale = state.originalLocalScale;
        }
        RestoreVictoryRecallRenderers(state.renderers);
    }

    private static void RestoreVictoryRecallRenderers(
        List<VictoryRecallRendererState> renderers)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            VictoryRecallRendererState captured = renderers[i];
            Renderer renderer = captured.renderer;
            if (renderer == null) continue;
            if (captured.hasSpriteColor && renderer is SpriteRenderer sprite)
                sprite.color = captured.spriteColor;
            renderer.enabled = captured.enabled;
        }
    }

    private void DestroyVictoryRecallObject(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
