using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class RougeTowerDefenseMapEditor : EditorWindow
{
    private enum PaintTool
    {
        Tile,
        EnemySpawn,
        MainTower,
        BossSpawn,
        Erase
    }

    private enum UpperDragKind
    {
        None,
        Enemy,
        MainTower,
        BossSpawn
    }

    private RougeTowerDefenseMap _map;
    private SerializedObject _serializedMap;
    private RougeTowerDefenseMapLoader _loader;
    private PaintTool _tool;
    private int _tileIndex = 1;
    private float _pixelSize = 8f;
    private Vector2 _settingsScroll;
    private bool _painting;
    private Vector2Int _lastPaintCell = new Vector2Int(int.MinValue, int.MinValue);
    private Vector2Int _hoverCell = new Vector2Int(-1, -1);
    private UpperDragKind _upperDragKind;
    private Vector2Int _upperDragSource = new Vector2Int(-1, -1);
    private int _pendingWidth = 32;
    private int _pendingHeight = 32;
    private float _pendingCellSize = 8f;

    [MenuItem("Rouge/Tower Defense/Map Painter")]
    public static void Open()
    {
        RougeTowerDefenseMapEditor window = GetWindow<RougeTowerDefenseMapEditor>();
        window.titleContent = new GUIContent("TD Map Painter");
        window.minSize = new Vector2(900f, 640f);
        window.Show();
    }

    private void OnEnable()
    {
        RougeTowerDefenseMap selected = Selection.activeObject as RougeTowerDefenseMap;
        if (selected != null) SetMap(selected);
    }

    private void OnSelectionChange()
    {
        if (Selection.activeObject is RougeTowerDefenseMap selected) SetMap(selected);
        Repaint();
    }

    private void OnGUI()
    {
        DrawTopBar();
        if (_map == null)
        {
            EditorGUILayout.HelpBox("Create or select a Tower Defense Map asset.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawSettingsPanel();
            GUILayout.Space(8f);
            DrawMapPanel();
        }
    }

    private void DrawTopBar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUI.BeginChangeCheck();
            RougeTowerDefenseMap selected = (RougeTowerDefenseMap)EditorGUILayout.ObjectField(
                _map, typeof(RougeTowerDefenseMap), false, GUILayout.Width(260f));
            if (EditorGUI.EndChangeCheck()) SetMap(selected);
            if (GUILayout.Button("New Map", EditorStyles.toolbarButton, GUILayout.Width(75f))) CreateMapAsset();
            using (new EditorGUI.DisabledScope(_map == null))
            {
                if (GUILayout.Button("Add/Update Scene Loader", EditorStyles.toolbarButton, GUILayout.Width(160f)))
                    AddOrUpdateLoader();
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(55f)))
                {
                    EditorUtility.SetDirty(_map);
                    AssetDatabase.SaveAssets();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Standalone 2D editor — Scene painting is not used", EditorStyles.miniLabel);
        }
    }

    private void DrawSettingsPanel()
    {
        if (_serializedMap == null || _serializedMap.targetObject != _map)
            _serializedMap = new SerializedObject(_map);
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(310f)))
        {
            _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll);
            EditorGUILayout.LabelField("Map Size", EditorStyles.boldLabel);
            _pendingWidth = EditorGUILayout.IntSlider("Width", _pendingWidth, 1, RougeTowerDefenseMap.MaxMapCells);
            _pendingHeight = EditorGUILayout.IntSlider("Height", _pendingHeight, 1, RougeTowerDefenseMap.MaxMapCells);
            _pendingCellSize = EditorGUILayout.FloatField("World Cell Size", _pendingCellSize);
            using (new EditorGUI.DisabledScope(
                       _pendingWidth == _map.Width && _pendingHeight == _map.Height &&
                       Mathf.Approximately(_pendingCellSize, _map.CellSize)))
            {
                if (GUILayout.Button("Apply Size (keep existing cells)"))
                {
                    Undo.RecordObject(_map, "Resize Tower Defense Map");
                    _map.ResizeGrid(_pendingWidth, _pendingHeight, _pendingCellSize, true);
                    EditorUtility.SetDirty(_map);
                }
            }
            EditorGUILayout.HelpBox(
                $"World size: {_map.Width * _map.CellSize:0.#} × {_map.Height * _map.CellSize:0.#}\n" +
                $"Each terrain cell contains {RougeTowerDefenseMap.MicroCellsPerTile} × {RougeTowerDefenseMap.MicroCellsPerTile} micro cells.\n" +
                "The map stays centered at world (0,0).",
                MessageType.None);

            DrawLevelCameraSettings();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            _tool = (PaintTool)GUILayout.Toolbar((int)_tool,
                new[] { "Tile", "Enemy", "Main", "Boss", "Erase" });
            _pixelSize = EditorGUILayout.Slider("Canvas Cell Pixels", _pixelSize, 5f, 20f);

            if (_tool == PaintTool.Tile)
                EditorGUILayout.HelpBox("Left: paint base tile. Right: erase base tile and its upper object. The main tower tile is protected.", MessageType.None);
            else if (_tool == PaintTool.EnemySpawn)
                EditorGUILayout.HelpBox("Click an empty walkable tile to create an enemy spawn. Drag an existing numbered marker to move it. Right-click removes only the upper marker.", MessageType.None);
            else
                EditorGUILayout.HelpBox("Upper layer only: objects require a walkable base tile and cannot overlap. Right-click keeps the base tile. Main tower cannot be deleted.", MessageType.None);

            if (_tool == PaintTool.Tile)
            {
                EditorGUILayout.Space(3f);
                for (int i = 1; i < _map.TileDefinitions.Count; i++)
                {
                    RougeTowerDefenseMap.TileDefinition definition = _map.TileDefinitions[i];
                    Rect row = EditorGUILayout.GetControlRect(false, 24f);
                    Rect swatch = new Rect(row.x + 3f, row.y + 3f, 18f, 18f);
                    EditorGUI.DrawRect(swatch, definition.editorColor);
                    if (GUI.Toggle(new Rect(row.x + 25f, row.y, row.width - 25f, row.height),
                            _tileIndex == i, $"{i}: {definition.name}", "Button")) _tileIndex = i;
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Asset Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "AutoTile index uses N/E/S/W bits: North 1, East 2, South 4, West 8. " +
                "Examples: 0 isolated, 3 north+east, 15 surrounded. Missing variants use the base Prefab.",
                MessageType.None);
            _serializedMap.Update();
            EditorGUILayout.PropertyField(_serializedMap.FindProperty("tileDefinitions"), true);
            EditorGUILayout.PropertyField(_serializedMap.FindProperty("enemySpawns"), true);
            EditorGUILayout.PropertyField(_serializedMap.FindProperty("mainTowerPrefab"));
            EditorGUILayout.PropertyField(_serializedMap.FindProperty("bossPrefab"));
            if (_serializedMap.ApplyModifiedProperties()) EditorUtility.SetDirty(_map);
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawLevelCameraSettings()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Level Camera Clamp / Zoom", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("configureCameraBounds"),
            new GUIContent("Enable Camera Clamp"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("cameraBoundsCenter"),
            new GUIContent("Clamp Center (World X/Z)"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("cameraBoundsSize"),
            new GUIContent("Clamp Size (Width/Height)"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("minimumCameraZoom"),
            new GUIContent("Minimum Zoom"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("maximumCameraZoom"),
            new GUIContent("Maximum Zoom"));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_map, "Edit Level Camera Clamp");
            _serializedMap.ApplyModifiedProperties();
            EditorUtility.SetDirty(_map);
            ResolveSceneLoader();
            if (_loader != null)
            {
                _loader.ApplyCameraSettingsToExistingBounds();
                EditorSceneManager.MarkSceneDirty(_loader.gameObject.scene);
            }
            SceneView.RepaintAll();
        }
        else
        {
            _serializedMap.ApplyModifiedProperties();
        }
        EditorGUILayout.HelpBox(
            "Saved in the selected Map asset, so every level has independent values. " +
            "A scene Loader using this Map applies them at runtime.",
            MessageType.None);
    }

    private void DrawMapPanel()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            string hover = _map.Contains(_hoverCell)
                ? $"Cell {_hoverCell.x}, {_hoverCell.y}  |  World {FormatWorld(_hoverCell)}"
                : "Left drag: paint    Right drag: erase";
            EditorGUILayout.LabelField(hover, EditorStyles.boldLabel);

            float width = _map.Width * _pixelSize;
            float height = _map.Height * _pixelSize;
            Rect scrollArea = GUILayoutUtility.GetRect(
                Mathf.Min(width + 20f, position.width - 340f),
                Mathf.Min(height + 20f, position.height - 80f),
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.BeginGroup(scrollArea, GUI.skin.box);
            Rect canvas = new Rect(8f, 8f, width, height);
            DrawCanvas(canvas);
            HandleCanvasInput(canvas);
            GUI.EndGroup();
        }
    }

    private void DrawCanvas(Rect canvas)
    {
        EditorGUI.DrawRect(canvas, new Color(0.075f, 0.085f, 0.1f, 1f));
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                int tileIndex = _map.GetTile(new Vector2Int(x, y));
                RougeTowerDefenseMap.TileDefinition definition = _map.GetDefinition(tileIndex);
                if (tileIndex == 0 || definition == null) continue;
                EditorGUI.DrawRect(CellRect(canvas, new Vector2Int(x, y)), definition.editorColor);
            }
        }

        Color gridColor = _pixelSize >= 7f
            ? new Color(0.38f, 0.42f, 0.48f, 0.42f)
            : new Color(0.38f, 0.42f, 0.48f, 0.2f);
        Handles.BeginGUI();
        Handles.color = gridColor;
        for (int x = 0; x <= _map.Width; x++)
        {
            float px = canvas.x + x * _pixelSize;
            Handles.DrawLine(new Vector3(px, canvas.y), new Vector3(px, canvas.yMax));
        }
        for (int y = 0; y <= _map.Height; y++)
        {
            float py = canvas.y + y * _pixelSize;
            Handles.DrawLine(new Vector3(canvas.x, py), new Vector3(canvas.xMax, py));
        }
        Handles.EndGUI();

        for (int i = 0; i < _map.EnemySpawns.Count; i++)
        {
            Vector2Int cell = _map.EnemySpawns[i].cell;
            DrawMarker(canvas, cell, (i + 1).ToString(), new Color(1f, 0.25f, 0.12f));
        }
        if (_map.HasMainTower) DrawMarker(canvas, _map.MainTowerCell, "T", new Color(0.1f, 0.78f, 1f));
        if (_map.HasBossSpawn) DrawMarker(canvas, _map.BossSpawnCell, "B", new Color(0.9f, 0.15f, 1f));

        if (_upperDragKind != UpperDragKind.None)
        {
            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(CellRect(canvas, _upperDragSource),
                new Color(1f, 0.85f, 0.1f, 0.2f), Color.yellow);
            if (_map.Contains(_hoverCell))
            {
                bool validDrop = _hoverCell == _upperDragSource ||
                                 (_map.IsGround(_hoverCell) && !_map.HasUpperObject(_hoverCell));
                Handles.DrawSolidRectangleWithOutline(CellRect(canvas, _hoverCell),
                    new Color(1f, 1f, 1f, 0.12f), validDrop ? Color.green : Color.red);
            }
            Handles.EndGUI();
        }

        if (_map.Contains(_hoverCell))
        {
            Rect hover = CellRect(canvas, _hoverCell);
            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(hover, new Color(1f, 1f, 1f, 0.13f), Color.white);
            Handles.EndGUI();
        }
    }

    private void DrawMarker(Rect canvas, Vector2Int cell, string label, Color color)
    {
        Rect rect = CellRect(canvas, cell);
        EditorGUI.DrawRect(rect, color);
        if (_pixelSize >= 7f)
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                fontSize = Mathf.Clamp(Mathf.RoundToInt(_pixelSize * 0.7f), 7, 13)
            };
            GUI.Label(rect, label, style);
        }
    }

    private void HandleCanvasInput(Rect canvas)
    {
        Event evt = Event.current;
        bool inside = canvas.Contains(evt.mousePosition);
        if (inside)
        {
            _hoverCell = MouseToCell(canvas, evt.mousePosition);
            Repaint();
        }
        else if (evt.type == EventType.MouseMove)
        {
            _hoverCell = new Vector2Int(-1, -1);
        }

        if (evt.type == EventType.MouseDown && inside && (evt.button == 0 || evt.button == 1))
        {
            if (evt.button == 0 && IsUpperPlacementTool())
            {
                BeginUpperInteraction(_hoverCell);
                evt.Use();
                return;
            }
            _painting = true;
            _lastPaintCell = new Vector2Int(int.MinValue, int.MinValue);
            Undo.RecordObject(_map, evt.button == 1 ? "Erase Map" : "Paint Map");
            PaintLine(_hoverCell, evt.button == 1);
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && _upperDragKind != UpperDragKind.None)
        {
            if (inside) UpdateUpperDrag(_hoverCell);
            evt.Use();
            Repaint();
        }
        else if ((evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp) &&
                 _upperDragKind != UpperDragKind.None)
        {
            CompleteUpperDrag(inside ? _hoverCell : _upperDragSource);
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && _painting && (evt.button == 0 || evt.button == 1))
        {
            if (inside) PaintLine(_hoverCell, evt.button == 1);
            evt.Use();
        }
        else if ((evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp) && _painting)
        {
            _painting = false;
            _lastPaintCell = new Vector2Int(int.MinValue, int.MinValue);
            EditorUtility.SetDirty(_map);
            evt.Use();
        }
    }

    private void PaintLine(Vector2Int destination, bool forceErase)
    {
        if (!_map.Contains(destination)) return;
        if (!_map.Contains(_lastPaintCell))
        {
            PaintCell(destination, forceErase);
            _lastPaintCell = destination;
            return;
        }

        int x0 = _lastPaintCell.x;
        int y0 = _lastPaintCell.y;
        int x1 = destination.x;
        int y1 = destination.y;
        int dx = Mathf.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            PaintCell(new Vector2Int(x0, y0), forceErase);
            if (x0 == x1 && y0 == y1) break;
            int twice = error * 2;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
        _lastPaintCell = destination;
        EditorUtility.SetDirty(_map);
    }

    private void PaintCell(Vector2Int cell, bool forceErase)
    {
        if (forceErase)
        {
            if (_tool == PaintTool.Tile) _map.EraseBaseTile(cell);
            else _map.RemoveUpperObjectAt(cell);
            return;
        }
        if (_tool == PaintTool.Erase)
        {
            _map.RemoveUpperObjectAt(cell);
            return;
        }
        switch (_tool)
        {
            case PaintTool.Tile:
                _map.PaintBaseTile(cell, _tileIndex);
                break;
        }
    }

    private bool IsUpperPlacementTool()
    {
        return _tool == PaintTool.EnemySpawn || _tool == PaintTool.MainTower || _tool == PaintTool.BossSpawn;
    }

    private void BeginUpperInteraction(Vector2Int cell)
    {
        if (!_map.Contains(cell)) return;
        switch (_tool)
        {
            case PaintTool.EnemySpawn:
                if (_map.FindEnemySpawn(cell) != null)
                {
                    Undo.RecordObject(_map, "Move Enemy Spawn");
                    _upperDragKind = UpperDragKind.Enemy;
                    _upperDragSource = cell;
                }
                else if (_map.IsGround(cell) && !_map.HasUpperObject(cell))
                {
                    Undo.RecordObject(_map, "Create Enemy Spawn");
                    _map.AddEnemySpawn(cell);
                }
                break;
            case PaintTool.MainTower:
                if (_map.HasMainTower && _map.MainTowerCell == cell)
                {
                    Undo.RecordObject(_map, "Move Main Tower");
                    _upperDragKind = UpperDragKind.MainTower;
                    _upperDragSource = cell;
                }
                else if (!_map.HasMainTower && _map.IsGround(cell) && !_map.HasUpperObject(cell))
                {
                    Undo.RecordObject(_map, "Create Main Tower");
                    _map.PlaceMainTower(cell);
                }
                break;
            case PaintTool.BossSpawn:
                if (_map.HasBossSpawn && _map.BossSpawnCell == cell)
                {
                    Undo.RecordObject(_map, "Move Boss Spawn");
                    _upperDragKind = UpperDragKind.BossSpawn;
                    _upperDragSource = cell;
                }
                else if (!_map.HasBossSpawn && _map.IsGround(cell) && !_map.HasUpperObject(cell))
                {
                    Undo.RecordObject(_map, "Create Boss Spawn");
                    _map.PlaceBossSpawn(cell);
                }
                break;
        }
        EditorUtility.SetDirty(_map);
        Repaint();
    }

    private void CompleteUpperDrag(Vector2Int destination)
    {
        if (_upperDragKind == UpperDragKind.None) return;
        UpdateUpperDrag(destination);
        _upperDragKind = UpperDragKind.None;
        _upperDragSource = new Vector2Int(-1, -1);
        Repaint();
    }

    private void UpdateUpperDrag(Vector2Int destination)
    {
        if (_upperDragKind == UpperDragKind.None || destination == _upperDragSource) return;
        if (!_map.IsGround(destination) || _map.HasUpperObject(destination)) return;
        bool moved = false;
        switch (_upperDragKind)
        {
            case UpperDragKind.Enemy:
                moved = _map.MoveEnemySpawn(_upperDragSource, destination);
                break;
            case UpperDragKind.MainTower:
                moved = _map.PlaceMainTower(destination);
                break;
            case UpperDragKind.BossSpawn:
                moved = _map.PlaceBossSpawn(destination);
                break;
        }
        if (moved)
        {
            _upperDragSource = destination;
            EditorUtility.SetDirty(_map);
        }
    }

    private Rect CellRect(Rect canvas, Vector2Int cell)
    {
        return new Rect(
            canvas.x + cell.x * _pixelSize,
            canvas.y + (_map.Height - 1 - cell.y) * _pixelSize,
            _pixelSize, _pixelSize);
    }

    private Vector2Int MouseToCell(Rect canvas, Vector2 mouse)
    {
        int x = Mathf.FloorToInt((mouse.x - canvas.x) / _pixelSize);
        int topY = Mathf.FloorToInt((mouse.y - canvas.y) / _pixelSize);
        return new Vector2Int(x, _map.Height - 1 - topY);
    }

    private string FormatWorld(Vector2Int cell)
    {
        Vector3 world = _map.CellCenter(cell);
        return $"({world.x:0.#}, {world.z:0.#})";
    }

    private void SetMap(RougeTowerDefenseMap map)
    {
        _map = map;
        _upperDragKind = UpperDragKind.None;
        _upperDragSource = new Vector2Int(-1, -1);
        _serializedMap = map != null ? new SerializedObject(map) : null;
        ResolveSceneLoader();
        if (map != null)
        {
            _pendingWidth = map.Width;
            _pendingHeight = map.Height;
            _pendingCellSize = map.CellSize;
            _tileIndex = Mathf.Clamp(_tileIndex, 1, Mathf.Max(1, map.TileDefinitions.Count - 1));
        }
    }

    private void ResolveSceneLoader()
    {
        _loader = null;
        RougeTowerDefenseMapLoader[] loaders = Object.FindObjectsByType<RougeTowerDefenseMapLoader>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < loaders.Length; i++)
        {
            SerializedObject candidate = new SerializedObject(loaders[i]);
            if (candidate.FindProperty("map").objectReferenceValue != _map) continue;
            _loader = loaders[i];
            return;
        }
    }

    private void CreateMapAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Tower Defense Map", "TowerDefenseMap", "asset", "Choose the map asset location.");
        if (string.IsNullOrEmpty(path)) return;
        RougeTowerDefenseMap created = CreateInstance<RougeTowerDefenseMap>();
        created.InitializeDefaults();
        AssetDatabase.CreateAsset(created, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = created;
        SetMap(created);
    }

    private void AddOrUpdateLoader()
    {
        RougeTowerDefenseMapLoader loader = Object.FindFirstObjectByType<RougeTowerDefenseMapLoader>();
        if (loader == null)
        {
            GameObject go = new GameObject("Tower Defense Map Loader");
            Undo.RegisterCreatedObjectUndo(go, "Create Tower Defense Map Loader");
            loader = Undo.AddComponent<RougeTowerDefenseMapLoader>(go);
        }
        SerializedObject serializedLoader = new SerializedObject(loader);
        serializedLoader.FindProperty("map").objectReferenceValue = _map;
        serializedLoader.ApplyModifiedProperties();
        _loader = loader;
        EditorUtility.SetDirty(loader);
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
        Selection.activeGameObject = loader.gameObject;
    }
}
