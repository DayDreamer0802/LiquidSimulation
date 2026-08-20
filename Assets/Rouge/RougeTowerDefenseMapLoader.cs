using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Rouge/Tower Defense Map Loader")]
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class RougeTowerDefenseMapLoader : MonoBehaviour
{
    public static RougeTowerDefenseMapLoader Active { get; private set; }
    public static RougeTowerDefenseMap ActiveMap => Active != null ? Active.map : null;

    [SerializeField] private RougeTowerDefenseMap map;
    [SerializeField] private bool loadOnEnable = true;
    [SerializeField] private bool clearOnDisable = true;

    private GameObject _runtimeRoot;
    private readonly List<Material> _runtimeMaterials = new List<Material>();
    private readonly List<Renderer> _towerPlaceGridRenderers = new List<Renderer>();
    private Material _towerPlaceGridMaterial;
    private Material _towerFootprintGridMaterial;
    private readonly Dictionary<RougeDefenseTower, TowerFootprintGridOverlay> _placedTowerGridOverlays =
        new Dictionary<RougeDefenseTower, TowerFootprintGridOverlay>();
    private readonly HashSet<RougeDefenseTower> _currentPlacedTowers = new HashSet<RougeDefenseTower>();
    private readonly HashSet<Vector2Int> _occupiedTowerGridCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _previewTowerGridCells = new HashSet<Vector2Int>();
    private readonly List<RougeDefenseTower> _stalePlacedTowerKeys = new List<RougeDefenseTower>();
    private TowerFootprintGridOverlay _previewTowerGridOverlay;

    private sealed class TowerFootprintGridOverlay
    {
        public GameObject root;
        public Mesh mesh;
        public MeshRenderer renderer;
        public Vector2Int anchor = new Vector2Int(int.MinValue, int.MinValue);
        public Vector2Int size;
        public int colorStateHash = int.MinValue;
    }

    public RougeTowerDefenseMap Map => map;

    private void OnEnable()
    {
        Active = this;
        if (Application.isPlaying && loadOnEnable) LoadMap();
    }

    private void OnDisable()
    {
        if (Application.isPlaying && clearOnDisable) ClearMap();
        if (Active == this) Active = null;
    }

    [ContextMenu("Load Map")]
    public void LoadMap()
    {
        ClearMap();
        if (map == null)
        {
            Debug.LogError("Tower Defense Map Loader has no map asset.", this);
            return;
        }

        if (Application.isPlaying)
        {
            RougeGameManager manager = FindFirstObjectByType<RougeGameManager>();
            if (manager != null)
                manager.ConfigureArenaFromMap(map.Width * map.CellSize, map.Height * map.CellSize);
        }

        _runtimeRoot = new GameObject($"Runtime Map - {map.name}");
        if (!Application.isPlaying) _runtimeRoot.hideFlags = HideFlags.DontSaveInEditor;
        _runtimeRoot.transform.SetParent(transform, false);
        BuildTileVisuals();
        BuildMergedSurfaces(false);
        BuildMergedSurfaces(true);
        BuildMapObjects();
    }

    [ContextMenu("Clear Map")]
    public void ClearMap()
    {
        ClearTowerFootprintGridOverlays();
        if (_runtimeRoot != null)
        {
            if (Application.isPlaying) Destroy(_runtimeRoot);
            else DestroyImmediate(_runtimeRoot);
            _runtimeRoot = null;
        }
        for (int i = 0; i < _runtimeMaterials.Count; i++)
        {
            if (_runtimeMaterials[i] == null) continue;
            if (Application.isPlaying) Destroy(_runtimeMaterials[i]);
            else DestroyImmediate(_runtimeMaterials[i]);
        }
        _runtimeMaterials.Clear();
        _towerPlaceGridRenderers.Clear();
        _towerPlaceGridMaterial = null;
        _towerFootprintGridMaterial = null;
    }

    private void BuildTileVisuals()
    {
        Transform visualRoot = CreateRoot("Visuals");
        var fallbackMaterials = new Dictionary<int, Material>();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                int tileIndex = map.GetTile(cell);
                if (tileIndex == 0) continue;
                RougeTowerDefenseMap.TileDefinition definition = map.GetDefinition(tileIndex);
                if (definition == null) continue;

                GameObject instance;
                GameObject resolvedPrefab = map.ResolveTilePrefab(cell, tileIndex);
                if (resolvedPrefab != null)
                {
                    instance = Instantiate(resolvedPrefab, map.CellCenter(cell, definition.yOffset),
                        Quaternion.Euler(definition.prefabEulerAngles), visualRoot);
                }
                else
                {
                    instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    instance.transform.SetParent(visualRoot, false);
                    float visualHeight = Mathf.Max(0.02f, definition.fallbackHeight);
                    instance.transform.position = map.CellCenter(cell, definition.yOffset + visualHeight * 0.5f);
                    instance.transform.localScale = new Vector3(map.CellSize, visualHeight, map.CellSize);
                    if (!fallbackMaterials.TryGetValue(tileIndex, out Material material))
                    {
                        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                        material = new Material(shader) { name = $"Runtime {definition.name}" };
                        material.color = definition.editorColor;
                        fallbackMaterials.Add(tileIndex, material);
                        _runtimeMaterials.Add(material);
                    }
                    instance.GetComponent<Renderer>().sharedMaterial = material;
                }
                instance.name = $"{definition.name} [{x},{y}]";
                Collider[] visualColliders = instance.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < visualColliders.Length; i++) visualColliders[i].enabled = false;
            }
        }
    }

    private void BuildMergedSurfaces(bool towerPlace)
    {
        int layer = towerPlace ? LayerMask.NameToLayer("TowerPlace") : gameObject.layer;
        if (towerPlace && layer < 0)
        {
            Debug.LogError("Map contains Tower Place tiles, but the TowerPlace layer does not exist.", this);
            return;
        }

        bool[,] used = new bool[map.Width, map.Height];
        Transform root = CreateRoot(towerPlace ? "Tower Places" : "Navigation Obstacles");
        int maxSpan = Mathf.Max(1, Mathf.FloorToInt(80f / map.CellSize));
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (used[x, y]) continue;
                int tileIndex = map.GetTile(new Vector2Int(x, y));
                RougeTowerDefenseMap.TileDefinition definition = map.GetDefinition(tileIndex);
                bool matches = definition != null &&
                               (towerPlace ? definition.towerPlace : definition.blocksNavigation || definition.towerPlace);
                if (!matches) continue;

                int rectWidth = 1;
                while (rectWidth < maxSpan && x + rectWidth < map.Width && !used[x + rectWidth, y] &&
                       TileMatches(x + rectWidth, y, tileIndex, towerPlace)) rectWidth++;
                int rectHeight = 1;
                bool canGrow = true;
                while (rectHeight < maxSpan && y + rectHeight < map.Height && canGrow)
                {
                    for (int xx = x; xx < x + rectWidth; xx++)
                    {
                        if (used[xx, y + rectHeight] || !TileMatches(xx, y + rectHeight, tileIndex, towerPlace))
                        {
                            canGrow = false;
                            break;
                        }
                    }
                    if (canGrow) rectHeight++;
                }
                for (int yy = y; yy < y + rectHeight; yy++)
                    for (int xx = x; xx < x + rectWidth; xx++) used[xx, yy] = true;

                float colliderHeight = towerPlace ? 0.1f : Mathf.Max(0.2f, definition.fallbackHeight);
                GameObject surface = new GameObject(towerPlace
                    ? $"towerPlace [{x},{y}] {rectWidth}x{rectHeight}"
                    : $"Map Obstacle [{x},{y}] {rectWidth}x{rectHeight}");
                surface.layer = layer;
                surface.transform.SetParent(root, false);
                Vector3 first = map.CellCenter(new Vector2Int(x, y));
                surface.transform.position = new Vector3(
                    first.x + (rectWidth - 1) * map.CellSize * 0.5f,
                    definition.yOffset + (towerPlace ? 0f : colliderHeight * 0.5f),
                    first.z + (rectHeight - 1) * map.CellSize * 0.5f);
                if (!towerPlace)
                {
                    BoxCollider box = surface.AddComponent<BoxCollider>();
                    box.size = new Vector3(rectWidth * map.CellSize, colliderHeight, rectHeight * map.CellSize);
                }
                surface.AddComponent<RougeMapSurface>().SetBlocksNavigation(!towerPlace);
                if (towerPlace) CreateTowerPlaceGridOverlay(surface.transform, rectWidth, rectHeight,
                    Mathf.Max(0.02f, definition.fallbackHeight));
            }
        }
    }

    private void CreateTowerPlaceGridOverlay(Transform parent, int widthInCells, int heightInCells,
        float surfaceHeight)
    {
        GameObject overlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
        overlay.name = "Tower Place Grid Overlay";
        overlay.transform.SetParent(parent, false);
        // Tile visuals sit on top of their fallback height. Keep the overlay above
        // that surface so the depth buffer cannot hide the placement grid.
        overlay.transform.localPosition = new Vector3(0f, surfaceHeight + 0.015f, 0f);
        overlay.transform.localScale = new Vector3(
            widthInCells * map.CellSize, 0.02f, heightInCells * map.CellSize);
        Collider overlayCollider = overlay.GetComponent<Collider>();
        if (overlayCollider != null) overlayCollider.enabled = false;
        MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetTowerPlaceGridMaterial();
        renderer.enabled = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        _towerPlaceGridRenderers.Add(renderer);
    }

    private Material GetTowerPlaceGridMaterial()
    {
        if (_towerPlaceGridMaterial != null) return _towerPlaceGridMaterial;
        Shader shader = Shader.Find("Rouge/Tower Place Grid");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        _towerPlaceGridMaterial = new Material(shader) { name = "Runtime Tower Place Grid" };
        if (_towerPlaceGridMaterial.HasProperty("_CellSize"))
            _towerPlaceGridMaterial.SetFloat("_CellSize", map.MicroCellSize);
        if (_towerPlaceGridMaterial.HasProperty("_GridOrigin"))
            _towerPlaceGridMaterial.SetVector("_GridOrigin", new Vector4(map.Origin.x, map.Origin.y, 0f, 0f));
        _runtimeMaterials.Add(_towerPlaceGridMaterial);
        return _towerPlaceGridMaterial;
    }

    public void SetTowerPlaceGridState(bool visible, IReadOnlyList<RougeDefenseTower> towers,
        RougeDefenseTower previewTower)
    {
        for (int i = _towerPlaceGridRenderers.Count - 1; i >= 0; i--)
        {
            Renderer renderer = _towerPlaceGridRenderers[i];
            if (renderer == null)
            {
                _towerPlaceGridRenderers.RemoveAt(i);
                continue;
            }
            renderer.enabled = visible;
        }

        if (map == null || _runtimeRoot == null) return;
        SyncTowerFootprintGridOverlays(visible, towers, previewTower);
    }

    private void SyncTowerFootprintGridOverlays(bool visible,
        IReadOnlyList<RougeDefenseTower> towers, RougeDefenseTower previewTower)
    {
        _currentPlacedTowers.Clear();
        _occupiedTowerGridCells.Clear();
        _previewTowerGridCells.Clear();
        int occupiedStateHash = 17;
        if (towers != null)
        {
            for (int i = 0; i < towers.Count; i++)
            {
                RougeDefenseTower tower = towers[i];
                if (tower == null) continue;
                _currentPlacedTowers.Add(tower);
                Vector2Int size = tower.FootprintCells;
                Vector2Int anchor = map.WorldToMicroFootprintAnchor(tower.transform.position, size);
                occupiedStateHash = CombineFootprintStateHash(occupiedStateHash, tower, anchor, size);
                for (int y = 0; y < size.y; y++)
                    for (int x = 0; x < size.x; x++)
                        _occupiedTowerGridCells.Add(anchor + new Vector2Int(x, y));

                TowerFootprintGridOverlay overlay = GetOrCreatePlacedTowerGridOverlay(tower);
                UpdateTowerFootprintGridOverlay(overlay, anchor, size, true, 1);
                overlay.renderer.enabled = visible;
            }
        }

        _stalePlacedTowerKeys.Clear();
        foreach (KeyValuePair<RougeDefenseTower, TowerFootprintGridOverlay> pair in _placedTowerGridOverlays)
            if (pair.Key == null || !_currentPlacedTowers.Contains(pair.Key))
                _stalePlacedTowerKeys.Add(pair.Key);
        for (int i = 0; i < _stalePlacedTowerKeys.Count; i++)
        {
            RougeDefenseTower key = _stalePlacedTowerKeys[i];
            if (_placedTowerGridOverlays.TryGetValue(key, out TowerFootprintGridOverlay stale))
                DestroyTowerFootprintGridOverlay(stale);
            _placedTowerGridOverlays.Remove(key);
        }

        bool previewIsPlaced = previewTower != null && _currentPlacedTowers.Contains(previewTower);
        bool previewActive = visible && !previewIsPlaced && previewTower != null &&
            previewTower.gameObject.activeInHierarchy;
        if (previewActive)
        {
            if (_previewTowerGridOverlay == null)
                _previewTowerGridOverlay = CreateTowerFootprintGridOverlay("Tower Preview Footprint Grid");
            Vector2Int previewSize = previewTower.FootprintCells;
            Vector2Int previewAnchor = map.WorldToMicroFootprintAnchor(
                previewTower.transform.position, previewSize);
            for (int y = 0; y < previewSize.y; y++)
            {
                for (int x = 0; x < previewSize.x; x++)
                {
                    Vector2Int cell = previewAnchor + new Vector2Int(x, y);
                    if (!_occupiedTowerGridCells.Contains(cell))
                        _previewTowerGridCells.Add(cell);
                }
            }
            UpdateTowerFootprintGridOverlay(_previewTowerGridOverlay, previewAnchor,
                previewSize, false, occupiedStateHash);
            _previewTowerGridOverlay.renderer.enabled = true;
        }
        else if (_previewTowerGridOverlay != null)
        {
            _previewTowerGridOverlay.renderer.enabled = false;
        }
    }

    private static int CombineFootprintStateHash(int hash, RougeDefenseTower tower,
        Vector2Int anchor, Vector2Int size)
    {
        unchecked
        {
            hash = hash * 31 + tower.GetInstanceID();
            hash = hash * 31 + anchor.x;
            hash = hash * 31 + anchor.y;
            hash = hash * 31 + size.x;
            hash = hash * 31 + size.y;
            return hash;
        }
    }

    private TowerFootprintGridOverlay GetOrCreatePlacedTowerGridOverlay(RougeDefenseTower tower)
    {
        if (_placedTowerGridOverlays.TryGetValue(tower, out TowerFootprintGridOverlay overlay))
            return overlay;
        overlay = CreateTowerFootprintGridOverlay($"Placed Tower Footprint Grid - {tower.name}");
        _placedTowerGridOverlays.Add(tower, overlay);
        return overlay;
    }

    private TowerFootprintGridOverlay CreateTowerFootprintGridOverlay(string overlayName)
    {
        GameObject root = new GameObject(overlayName);
        root.transform.SetParent(_runtimeRoot.transform, false);
        Mesh mesh = new Mesh { name = overlayName + " Mesh" };
        mesh.MarkDynamic();
        MeshFilter filter = root.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetTowerFootprintGridMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return new TowerFootprintGridOverlay
        {
            root = root,
            mesh = mesh,
            renderer = renderer
        };
    }

    private Material GetTowerFootprintGridMaterial()
    {
        if (_towerFootprintGridMaterial != null) return _towerFootprintGridMaterial;
        Shader shader = Shader.Find("Rouge/Tower Place Grid") ?? Shader.Find("Sprites/Default");
        _towerFootprintGridMaterial = new Material(shader) { name = "Runtime Tower Footprint Grid" };
        if (_towerFootprintGridMaterial.HasProperty("_UseVertexColor"))
            _towerFootprintGridMaterial.SetFloat("_UseVertexColor", 1f);
        if (_towerFootprintGridMaterial.HasProperty("_CellSize"))
            _towerFootprintGridMaterial.SetFloat("_CellSize", map.MicroCellSize);
        if (_towerFootprintGridMaterial.HasProperty("_GridOrigin"))
            _towerFootprintGridMaterial.SetVector("_GridOrigin",
                new Vector4(map.Origin.x, map.Origin.y, 0f, 0f));
        _runtimeMaterials.Add(_towerFootprintGridMaterial);
        return _towerFootprintGridMaterial;
    }

    private void UpdateTowerFootprintGridOverlay(TowerFootprintGridOverlay overlay,
        Vector2Int anchor, Vector2Int size, bool allRed, int colorStateHash)
    {
        float y = GetTowerFootprintGridHeight(anchor, size);
        overlay.root.transform.position = map.MicroFootprintCenter(anchor, size, y);
        overlay.root.transform.rotation = Quaternion.identity;
        overlay.root.transform.localScale = Vector3.one;
        if (overlay.anchor == anchor && overlay.size == size &&
            overlay.colorStateHash == colorStateHash) return;

        overlay.anchor = anchor;
        overlay.size = size;
        overlay.colorStateHash = colorStateHash;
        BuildTowerFootprintGridMesh(overlay.mesh, anchor, size, allRed);
    }

    private void BuildTowerFootprintGridMesh(Mesh mesh, Vector2Int anchor,
        Vector2Int size, bool allRed)
    {
        int width = Mathf.Clamp(size.x, 1, 16);
        int height = Mathf.Clamp(size.y, 1, 16);
        int cellCount = width * height;
        Vector3[] vertices = new Vector3[cellCount * 4];
        Color32[] colors = new Color32[cellCount * 4];
        int[] triangles = new int[cellCount * 6];
        float cellSize = map.MicroCellSize;
        float originX = width * cellSize * -0.5f;
        float originZ = height * cellSize * -0.5f;
        Color32 red = new Color32(255, 18, 12, 150);
        Color32 green = new Color32(24, 255, 62, 135);

        int cellIndex = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++, cellIndex++)
            {
                int vertex = cellIndex * 4;
                int triangle = cellIndex * 6;
                float x0 = originX + x * cellSize;
                float x1 = x0 + cellSize;
                float z0 = originZ + y * cellSize;
                float z1 = z0 + cellSize;
                vertices[vertex] = new Vector3(x0, 0f, z0);
                vertices[vertex + 1] = new Vector3(x0, 0f, z1);
                vertices[vertex + 2] = new Vector3(x1, 0f, z1);
                vertices[vertex + 3] = new Vector3(x1, 0f, z0);

                Vector2Int gridCell = anchor + new Vector2Int(x, y);
                bool occupied = allRed || _occupiedTowerGridCells.Contains(gridCell);
                Color32 color = occupied ? red :
                    _previewTowerGridCells.Contains(gridCell) ? green : new Color32(0, 0, 0, 0);
                colors[vertex] = color;
                colors[vertex + 1] = color;
                colors[vertex + 2] = color;
                colors[vertex + 3] = color;

                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    private float GetTowerFootprintGridHeight(Vector2Int anchor, Vector2Int size)
    {
        float minX = map.Origin.x + anchor.x * map.MicroCellSize;
        float minZ = map.Origin.y + anchor.y * map.MicroCellSize;
        float maxX = minX + size.x * map.MicroCellSize;
        float maxZ = minZ + size.y * map.MicroCellSize;
        float height = 0.04f;
        for (int i = 0; i < _towerPlaceGridRenderers.Count; i++)
        {
            Renderer renderer = _towerPlaceGridRenderers[i];
            if (renderer == null) continue;
            Bounds bounds = renderer.bounds;
            bool overlaps = minX < bounds.max.x && maxX > bounds.min.x &&
                minZ < bounds.max.z && maxZ > bounds.min.z;
            if (overlaps) height = Mathf.Max(height, bounds.max.y + 0.008f);
        }
        return height;
    }

    private void ClearTowerFootprintGridOverlays()
    {
        foreach (TowerFootprintGridOverlay overlay in _placedTowerGridOverlays.Values)
            DestroyTowerFootprintGridOverlay(overlay);
        _placedTowerGridOverlays.Clear();
        DestroyTowerFootprintGridOverlay(_previewTowerGridOverlay);
        _previewTowerGridOverlay = null;
        _currentPlacedTowers.Clear();
        _occupiedTowerGridCells.Clear();
        _previewTowerGridCells.Clear();
        _stalePlacedTowerKeys.Clear();
    }

    private void DestroyTowerFootprintGridOverlay(TowerFootprintGridOverlay overlay)
    {
        if (overlay == null) return;
        if (overlay.mesh != null)
        {
            if (Application.isPlaying) Destroy(overlay.mesh);
            else DestroyImmediate(overlay.mesh);
        }
        if (overlay.root != null)
        {
            if (Application.isPlaying) Destroy(overlay.root);
            else DestroyImmediate(overlay.root);
        }
    }

    private bool TileMatches(int x, int y, int tileIndex, bool towerPlace)
    {
        if (map.GetTile(new Vector2Int(x, y)) != tileIndex) return false;
        RougeTowerDefenseMap.TileDefinition definition = map.GetDefinition(tileIndex);
        return definition != null &&
               (towerPlace ? definition.towerPlace : definition.blocksNavigation || definition.towerPlace);
    }

    private void BuildMapObjects()
    {
        Transform objectRoot = CreateRoot("Map Objects");
        objectRoot.gameObject.AddComponent<RougeRuntimeMapObject>();
        RougeCameraFollow levelCameraFollow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (levelCameraFollow != null)
        {
            levelCameraFollow.SetZoomLimits(map.MinimumCameraZoom, map.MaximumCameraZoom);
            levelCameraFollow.SetMovementClampEnabled(map.ConfigureCameraBounds);
        }
        for (int i = 0; i < map.EnemySpawns.Count; i++)
        {
            RougeTowerDefenseMap.EnemySpawn source = map.EnemySpawns[i];
            GameObject go = new GameObject($"Enemy Spawn [{source.cell.x},{source.cell.y}]");
            go.transform.SetParent(objectRoot, false);
            go.transform.position = map.CellCenter(source.cell, 0.25f);
            RougeEnemySpawnPoint spawn = go.AddComponent<RougeEnemySpawnPoint>();
            spawn.ConfigureFromMap(source.spawnCount, source.spawnInterval, source.startDelay,
                map.CellSize, source.enemyType);
        }

        if (map.HasMainTower)
        {
            RougeMainTower tower = FindFirstObjectByType<RougeMainTower>();
            if (tower == null)
            {
                GameObject go = map.MainTowerPrefab != null
                    ? Instantiate(map.MainTowerPrefab, objectRoot)
                    : new GameObject("Main Tower");
                go.transform.SetParent(objectRoot, true);
                tower = go.GetComponent<RougeMainTower>() ?? go.AddComponent<RougeMainTower>();
            }
            tower.transform.position = map.CellCenter(map.MainTowerCell, 0.25f);
        }

        if (map.HasBossSpawn)
        {
            RougeBossSpawnPoint boss = FindFirstObjectByType<RougeBossSpawnPoint>();
            if (boss == null)
            {
                GameObject go = map.BossPrefab != null
                    ? Instantiate(map.BossPrefab, objectRoot)
                    : new GameObject("Boss Spawn Point");
                go.transform.SetParent(objectRoot, true);
                boss = go.GetComponent<RougeBossSpawnPoint>() ?? go.AddComponent<RougeBossSpawnPoint>();
            }
            boss.transform.position = map.CellCenter(map.BossSpawnCell, 0.25f);
        }

        if (map.ConfigureCameraBounds)
        {
            RougeCameraBounds bounds = FindFirstObjectByType<RougeCameraBounds>();
            GameObject go;
            if (bounds == null)
            {
                go = new GameObject("Camera Movement Bounds");
                go.transform.SetParent(objectRoot, false);
                bounds = go.AddComponent<RougeCameraBounds>();
                go.AddComponent<RougeMapSurface>().SetBlocksNavigation(false);
            }
            else
            {
                go = bounds.gameObject;
            }
            ApplyCameraBounds(bounds);
            RougeMapSurface surface = go.GetComponent<RougeMapSurface>() ?? go.AddComponent<RougeMapSurface>();
            surface.SetBlocksNavigation(false);
            if (levelCameraFollow != null)
            {
                levelCameraFollow.SetMovementBounds(bounds);
            }
        }
    }

    public void ApplyCameraSettingsToExistingBounds()
    {
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (map == null) return;
        if (follow != null) follow.SetZoomLimits(map.MinimumCameraZoom, map.MaximumCameraZoom);
        if (follow != null) follow.SetMovementClampEnabled(map.ConfigureCameraBounds);
        if (!map.ConfigureCameraBounds) return;

        RougeCameraBounds bounds = FindFirstObjectByType<RougeCameraBounds>();
        if (bounds == null) return;
        ApplyCameraBounds(bounds);
        if (follow != null) follow.SetMovementBounds(bounds);
    }

    private void ApplyCameraBounds(RougeCameraBounds bounds)
    {
        GameObject go = bounds.gameObject;
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        BoxCollider box = bounds.GetComponent<BoxCollider>();
        box.isTrigger = true;
        Vector3 worldScale = go.transform.lossyScale;
        box.center = new Vector3(
            map.CameraBoundsCenter.x / Mathf.Max(0.0001f, worldScale.x), 0f,
            map.CameraBoundsCenter.y / Mathf.Max(0.0001f, worldScale.z));
        box.size = new Vector3(
            Mathf.Max(1f, map.CameraBoundsSize.x) / Mathf.Max(0.0001f, Mathf.Abs(worldScale.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(worldScale.y)),
            Mathf.Max(1f, map.CameraBoundsSize.y) / Mathf.Max(0.0001f, Mathf.Abs(worldScale.z)));
    }

    private Transform CreateRoot(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(_runtimeRoot.transform, false);
        return go.transform;
    }
}

[DisallowMultipleComponent]
public sealed class RougeMapSurface : MonoBehaviour
{
    [SerializeField] private bool blocksNavigation;
    public bool BlocksNavigation => blocksNavigation;
    public void SetBlocksNavigation(bool value) => blocksNavigation = value;
}

public sealed class RougeRuntimeMapObject : MonoBehaviour
{
}
