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
    private readonly HashSet<Vector2Int> _bluePlacedTowerGridCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _greenValidTowerGridCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _redInvalidTowerGridCells = new HashSet<Vector2Int>();
    private TowerFootprintGridOverlay _towerFootprintGridOverlay;

    private sealed class TowerFootprintGridOverlay
    {
        public GameObject root;
        public Mesh mesh;
        public MeshRenderer renderer;
        public Vector2Int anchor = new Vector2Int(int.MinValue, int.MinValue);
        public Vector2Int size;
        public readonly HashSet<Vector2Int> blueCells = new HashSet<Vector2Int>();
        public readonly HashSet<Vector2Int> greenCells = new HashSet<Vector2Int>();
        public readonly HashSet<Vector2Int> redCells = new HashSet<Vector2Int>();
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
                        material = CreateFallbackTileMaterial(definition);
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

    private Material CreateFallbackTileMaterial(RougeTowerDefenseMap.TileDefinition definition)
    {
        if (definition.towerPlace)
        {
            Shader padShader = Shader.Find("Rouge/Tower Placement Pad");
            if (padShader != null)
            {
                Material padMaterial = new Material(padShader)
                {
                    name = $"Runtime Sci-Fi Placement Pad - {definition.name}"
                };
                Color.RGBToHSV(definition.editorColor, out float hue, out float saturation,
                    out float value);
                Color accent = Color.HSVToRGB(hue, Mathf.Max(0.62f, saturation),
                    Mathf.Max(0.78f, value));
                accent.a = 1f;
                padMaterial.SetColor("_AccentColor", accent);
                padMaterial.SetColor("_BaseColor", new Color(0.07f, 0.135f, 0.19f, 1f));
                padMaterial.SetFloat("_CellSize", map.CellSize);
                padMaterial.SetVector("_GridOrigin",
                    new Vector4(map.Origin.x, map.Origin.y, 0f, 0f));
                padMaterial.SetFloat("_FrameWidth", 0.028f);
                padMaterial.SetFloat("_GlowStrength", 1.05f);
                padMaterial.SetFloat("_PulseSpeed", 0.7f);
                padMaterial.enableInstancing = true;
                return padMaterial;
            }
        }

        if (!definition.blocksNavigation && !definition.towerPlace)
        {
            Shader groundShader = Shader.Find("Rouge/Sci-Fi Ground Tile");
            if (groundShader != null)
            {
                Material groundMaterial = new Material(groundShader)
                {
                    name = $"Runtime Sci-Fi Ground - {definition.name}"
                };
                Color source = definition.editorColor;
                source.a = 1f;
                Color baseColor = Color.Lerp(new Color(0.13f, 0.21f, 0.27f, 1f), source, 0.58f);
                Color panelColor = Color.Lerp(baseColor * 0.72f,
                    new Color(0.08f, 0.15f, 0.21f, 1f), 0.42f);
                panelColor.a = 1f;
                Color accentColor = Color.Lerp(baseColor,
                    new Color(0.05f, 0.62f, 0.72f, 1f), 0.36f);
                accentColor.a = 1f;
                groundMaterial.SetColor("_BaseColor", baseColor);
                groundMaterial.SetColor("_PanelColor", panelColor);
                groundMaterial.SetColor("_AccentColor", accentColor);
                groundMaterial.SetFloat("_CellSize", map.CellSize);
                groundMaterial.SetVector("_GridOrigin",
                    new Vector4(map.Origin.x, map.Origin.y, 0f, 0f));
                groundMaterial.SetFloat("_SeamWidth", 0.018f);
                groundMaterial.SetFloat("_DetailStrength", 0.42f);
                groundMaterial.enableInstancing = true;
                return groundMaterial;
            }
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { name = $"Runtime {definition.name}" };
        Color fallbackColor = definition.towerPlace
            ? Color.Lerp(new Color(0.07f, 0.135f, 0.19f, 1f), definition.editorColor, 0.28f)
            : definition.editorColor;
        material.color = fallbackColor;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", fallbackColor);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", definition.towerPlace ? 0.72f : 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", definition.towerPlace ? 0.68f : 0.2f);
        material.enableInstancing = true;
        return material;
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
        if (_towerPlaceGridMaterial.HasProperty("_BaseColor"))
            _towerPlaceGridMaterial.SetColor("_BaseColor", new Color(0.008f, 0.07f, 0.11f, 0.035f));
        if (_towerPlaceGridMaterial.HasProperty("_LineColor"))
            _towerPlaceGridMaterial.SetColor("_LineColor", new Color(0.32f, 0.84f, 0.92f, 0.82f));
        if (_towerPlaceGridMaterial.HasProperty("_CellSize"))
            _towerPlaceGridMaterial.SetFloat("_CellSize", map.MicroCellSize);
        if (_towerPlaceGridMaterial.HasProperty("_LineWidth"))
            _towerPlaceGridMaterial.SetFloat("_LineWidth", 0.03f);
        if (_towerPlaceGridMaterial.HasProperty("_InnerRailDistance"))
            _towerPlaceGridMaterial.SetFloat("_InnerRailDistance", 0.085f);
        if (_towerPlaceGridMaterial.HasProperty("_FlowSpeed"))
            _towerPlaceGridMaterial.SetFloat("_FlowSpeed", 0.85f);
        if (_towerPlaceGridMaterial.HasProperty("_GridOrigin"))
            _towerPlaceGridMaterial.SetVector("_GridOrigin", new Vector4(map.Origin.x, map.Origin.y, 0f, 0f));
        _runtimeMaterials.Add(_towerPlaceGridMaterial);
        return _towerPlaceGridMaterial;
    }

    public void SetTowerPlaceGridState(bool visible, IReadOnlyList<RougeDefenseTower> towers,
        RougeDefenseTower previewTower, IReadOnlyList<bool> previewCellValidity, bool previewValid)
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
        SyncTowerFootprintGridOverlays(visible, towers, previewTower, previewCellValidity, previewValid);
    }

    private void SyncTowerFootprintGridOverlays(bool visible,
        IReadOnlyList<RougeDefenseTower> towers, RougeDefenseTower previewTower,
        IReadOnlyList<bool> previewCellValidity, bool previewValid)
    {
        _bluePlacedTowerGridCells.Clear();
        _greenValidTowerGridCells.Clear();
        _redInvalidTowerGridCells.Clear();
        bool previewIsPlaced = false;
        if (towers != null)
        {
            for (int i = 0; i < towers.Count; i++)
            {
                RougeDefenseTower tower = towers[i];
                if (tower == null) continue;
                if (tower == previewTower) previewIsPlaced = true;
                Vector2Int size = tower.FootprintCells;
                Vector2Int anchor = map.WorldToMicroFootprintAnchor(tower.transform.position, size);
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        Vector2Int cell = anchor + new Vector2Int(x, y);
                        // The occupied overlay is a state of the white build grid,
                        // so it must never render beyond that grid's actual cells.
                        if (map.IsTowerPlaceMicroCell(cell))
                            _bluePlacedTowerGridCells.Add(cell);
                    }
                }
            }
        }

        bool previewActive = visible && !previewIsPlaced && previewTower != null &&
            previewTower.gameObject.activeInHierarchy;
        if (previewActive)
        {
            Vector2Int previewSize = previewTower.FootprintCells;
            Vector2Int previewAnchor = map.WorldToMicroFootprintAnchor(
                previewTower.transform.position, previewSize);
            int expectedValidityCount = previewSize.x * previewSize.y;
            bool hasExplicitValidity = previewCellValidity != null &&
                previewCellValidity.Count == expectedValidityCount;
            bool previewTouchesTowerPlace = false;
            for (int y = 0; y < previewSize.y; y++)
            {
                for (int x = 0; x < previewSize.x; x++)
                {
                    Vector2Int cell = previewAnchor + new Vector2Int(x, y);
                    bool isTowerPlaceCell = map.IsTowerPlaceMicroCell(cell);
                    previewTouchesTowerPlace |= isTowerPlaceCell;
                    int validityIndex = y * previewSize.x + x;
                    bool cellIsValid = previewValid && (hasExplicitValidity
                        ? previewCellValidity[validityIndex]
                        : isTowerPlaceCell && !_bluePlacedTowerGridCells.Contains(cell));
                    if (cellIsValid && !_bluePlacedTowerGridCells.Contains(cell))
                    {
                        _greenValidTowerGridCells.Add(cell);
                    }
                    else
                    {
                        _redInvalidTowerGridCells.Add(cell);
                    }
                }
            }

            // Keep out-of-bounds cells visible in red while any part of the
            // footprint still touches the white build grid. Hide the footprint once
            // it has moved completely outside the buildable area.
            if (!previewTouchesTowerPlace)
            {
                _greenValidTowerGridCells.Clear();
                _redInvalidTowerGridCells.Clear();
            }
        }

        // A placed footprint remains in the persistent blue layer. Invalid preview
        // cells are allowed to overlap it and are appended after blue in the mesh,
        // producing a temporary red overlay without mutating the occupied state.
        _greenValidTowerGridCells.ExceptWith(_bluePlacedTowerGridCells);
        _greenValidTowerGridCells.ExceptWith(_redInvalidTowerGridCells);

        int renderedCellCount = _bluePlacedTowerGridCells.Count + _greenValidTowerGridCells.Count +
            _redInvalidTowerGridCells.Count;
        if (visible && renderedCellCount > 0)
        {
            if (_towerFootprintGridOverlay == null)
                _towerFootprintGridOverlay = CreateTowerFootprintGridOverlay("Tower Footprint Grid States");
            UpdateTowerGridStateOverlay(_towerFootprintGridOverlay);
            _towerFootprintGridOverlay.renderer.enabled = true;
        }
        else if (_towerFootprintGridOverlay != null)
            _towerFootprintGridOverlay.renderer.enabled = false;
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
        if (_towerFootprintGridMaterial.HasProperty("_BaseColor"))
            _towerFootprintGridMaterial.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
        if (_towerFootprintGridMaterial.HasProperty("_LineColor"))
            _towerFootprintGridMaterial.SetColor("_LineColor", new Color(0.55f, 0.96f, 1f, 1f));
        if (_towerFootprintGridMaterial.HasProperty("_CellSize"))
            _towerFootprintGridMaterial.SetFloat("_CellSize", map.MicroCellSize);
        if (_towerFootprintGridMaterial.HasProperty("_LineWidth"))
            _towerFootprintGridMaterial.SetFloat("_LineWidth", 0.022f);
        if (_towerFootprintGridMaterial.HasProperty("_InnerRailDistance"))
            _towerFootprintGridMaterial.SetFloat("_InnerRailDistance", 0.085f);
        if (_towerFootprintGridMaterial.HasProperty("_FlowSpeed"))
            _towerFootprintGridMaterial.SetFloat("_FlowSpeed", 0.9f);
        if (_towerFootprintGridMaterial.HasProperty("_GridOrigin"))
            _towerFootprintGridMaterial.SetVector("_GridOrigin",
                new Vector4(map.Origin.x, map.Origin.y, 0f, 0f));
        _runtimeMaterials.Add(_towerFootprintGridMaterial);
        return _towerFootprintGridMaterial;
    }

    private void UpdateTowerGridStateOverlay(TowerFootprintGridOverlay overlay)
    {
        Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
        Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);
        IncludeTowerCellSetBounds(_bluePlacedTowerGridCells, ref min, ref max);
        IncludeTowerCellSetBounds(_greenValidTowerGridCells, ref min, ref max);
        IncludeTowerCellSetBounds(_redInvalidTowerGridCells, ref min, ref max);
        Vector2Int size = max - min + Vector2Int.one;
        float y = GetTowerFootprintGridHeight(min, size);
        overlay.root.transform.position = map.MicroFootprintCenter(min, size, y);
        overlay.root.transform.rotation = Quaternion.identity;
        overlay.root.transform.localScale = Vector3.one;
        bool cellsUnchanged = overlay.blueCells.SetEquals(_bluePlacedTowerGridCells) &&
            overlay.greenCells.SetEquals(_greenValidTowerGridCells) &&
            overlay.redCells.SetEquals(_redInvalidTowerGridCells);
        if (overlay.anchor == min && overlay.size == size && cellsUnchanged) return;

        overlay.anchor = min;
        overlay.size = size;
        CopyTowerCellSet(_bluePlacedTowerGridCells, overlay.blueCells);
        CopyTowerCellSet(_greenValidTowerGridCells, overlay.greenCells);
        CopyTowerCellSet(_redInvalidTowerGridCells, overlay.redCells);
        BuildTowerGridStateMesh(overlay.mesh, min, size);
    }

    private static void CopyTowerCellSet(HashSet<Vector2Int> source, HashSet<Vector2Int> destination)
    {
        destination.Clear();
        destination.UnionWith(source);
    }

    private static void IncludeTowerCellSetBounds(HashSet<Vector2Int> cells,
        ref Vector2Int min, ref Vector2Int max)
    {
        foreach (Vector2Int cell in cells)
        {
            min = Vector2Int.Min(min, cell);
            max = Vector2Int.Max(max, cell);
        }
    }

    private void BuildTowerGridStateMesh(Mesh mesh, Vector2Int anchor, Vector2Int boundsSize)
    {
        int cellCount = _bluePlacedTowerGridCells.Count + _greenValidTowerGridCells.Count +
            _redInvalidTowerGridCells.Count;
        Vector3[] vertices = new Vector3[cellCount * 4];
        Color32[] colors = new Color32[cellCount * 4];
        int[] triangles = new int[cellCount * 6];
        float cellSize = map.MicroCellSize;
        float originX = boundsSize.x * cellSize * -0.5f;
        float originZ = boundsSize.y * cellSize * -0.5f;

        int cellIndex = 0;
        AppendTowerCellSet(_bluePlacedTowerGridCells, new Color32(38, 150, 255, 220), anchor,
            cellSize, originX, originZ, vertices, colors, triangles, ref cellIndex);
        AppendTowerCellSet(_greenValidTowerGridCells, new Color32(30, 255, 76, 210), anchor,
            cellSize, originX, originZ, vertices, colors, triangles, ref cellIndex);
        AppendTowerCellSet(_redInvalidTowerGridCells, new Color32(255, 58, 46, 226), anchor,
            cellSize, originX, originZ, vertices, colors, triangles, ref cellIndex);

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    private static void AppendTowerCellSet(HashSet<Vector2Int> cells, Color32 color,
        Vector2Int anchor, float cellSize, float originX, float originZ,
        Vector3[] vertices, Color32[] colors, int[] triangles, ref int cellIndex)
    {
        foreach (Vector2Int cell in cells)
        {
            int x = cell.x - anchor.x;
            int y = cell.y - anchor.y;
            int vertex = cellIndex * 4;
            int triangle = cellIndex * 6;
            float inset = cellSize * 0.11f;
            float x0 = originX + x * cellSize + inset;
            float x1 = originX + (x + 1) * cellSize - inset;
            float z0 = originZ + y * cellSize + inset;
            float z1 = originZ + (y + 1) * cellSize - inset;
            vertices[vertex] = new Vector3(x0, 0f, z0);
            vertices[vertex + 1] = new Vector3(x0, 0f, z1);
            vertices[vertex + 2] = new Vector3(x1, 0f, z1);
            vertices[vertex + 3] = new Vector3(x1, 0f, z0);
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
            cellIndex++;
        }
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
        DestroyTowerFootprintGridOverlay(_towerFootprintGridOverlay);
        _towerFootprintGridOverlay = null;
        _bluePlacedTowerGridCells.Clear();
        _greenValidTowerGridCells.Clear();
        _redInvalidTowerGridCells.Clear();
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
                map.CellSize, source.enemyType, source.limitWaveCount, source.maximumWaves);
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
