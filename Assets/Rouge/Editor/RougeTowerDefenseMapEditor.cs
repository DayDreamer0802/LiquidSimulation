using System;
using System.Collections.Generic;
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
    private float _pixelSize = 25f;
    private Vector2 _settingsScroll;
    private int _expandedEnemySpawnIndex = -1;
    private int _scrollToEnemySpawnIndex = -1;
    private int _draggedEnemySpawnIndex = -1;
    private int _enemySpawnDropIndex = -1;
    private bool _painting;
    private Vector2Int _lastPaintCell = new Vector2Int(int.MinValue, int.MinValue);
    private Vector2Int _hoverCell = new Vector2Int(-1, -1);
    private UpperDragKind _upperDragKind;
    private Vector2Int _upperDragSource = new Vector2Int(-1, -1);
    private int _pendingWidth = 48;
    private int _pendingHeight = 48;
    private float _pendingCellSize = 8f;
    private static int s_bossOptionsJsonHash = int.MinValue;
    private static int[] s_bossOptionIds;
    private static string[] s_bossOptionLabels;

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
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(360f)))
        {
            _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll);
            EditorGUILayout.LabelField("Layer / Brush", EditorStyles.boldLabel);
            PaintTool nextTool = (PaintTool)GUILayout.Toolbar((int)_tool,
                new[] { "Map", "Enemy", "Main", "Boss", "Erase" }, GUILayout.Height(28f));
            if (nextTool != _tool)
            {
                _tool = nextTool;
                _settingsScroll = Vector2.zero;
                GUI.FocusControl(null);
            }
            _pixelSize = EditorGUILayout.Slider("Canvas Cell Pixels", _pixelSize, 15f, 30f);

            if (_tool == PaintTool.Tile)
                EditorGUILayout.HelpBox("Left: paint base tile. Right: erase base tile and its upper object. The main tower tile is protected.", MessageType.None);
            else if (_tool == PaintTool.EnemySpawn)
                EditorGUILayout.HelpBox("Click an empty walkable tile to create an enemy spawn. Drag an existing numbered marker to move it. Right-click removes only the upper marker.", MessageType.None);
            else
                EditorGUILayout.HelpBox("Upper layer only: objects require a walkable base tile and cannot overlap. Right-click keeps the base tile. Main tower cannot be deleted.", MessageType.None);

            switch (_tool)
            {
                case PaintTool.Tile:
                    DrawMapLayerSettings();
                    break;
                case PaintTool.EnemySpawn:
                    DrawEnemyLayerSettings();
                    break;
                case PaintTool.MainTower:
                    DrawMainTowerLayerSettings();
                    break;
                case PaintTool.BossSpawn:
                    DrawBossLayerSettings();
                    break;
            }
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawMapLayerSettings()
    {
        EditorGUILayout.Space(6f);
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

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Tile Palette", EditorStyles.boldLabel);
        for (int i = 1; i < _map.TileDefinitions.Count; i++)
        {
            RougeTowerDefenseMap.TileDefinition definition = _map.TileDefinitions[i];
            Rect row = EditorGUILayout.GetControlRect(false, 24f);
            Rect swatch = new Rect(row.x + 3f, row.y + 3f, 18f, 18f);
            EditorGUI.DrawRect(swatch, definition.editorColor);
            if (GUI.Toggle(new Rect(row.x + 25f, row.y, row.width - 25f, row.height),
                    _tileIndex == i, $"{i}: {definition.name}", "Button")) _tileIndex = i;
        }

        DrawLevelRulesSettings();
        DrawLevelCameraSettings();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Tile Definitions", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "AutoTile index uses N/E/S/W bits: North 1, East 2, South 4, West 8. " +
            "Missing variants use the base Prefab.", MessageType.None);
        _serializedMap.Update();
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("tileDefinitions"), true);
        if (_serializedMap.ApplyModifiedProperties()) EditorUtility.SetDirty(_map);
    }

    private void DrawEnemyLayerSettings()
    {
        EditorGUILayout.Space(6f);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();
        DrawEnemySpawns(_serializedMap.FindProperty("enemySpawns"));
        SerializedProperty victoryConditions = _serializedMap.FindProperty("victoryConditions");
        if (ContainsVictoryCondition(victoryConditions, RougeLevelVictoryConditionType.KillAllEnemies) &&
            HasUnlimitedEnemySpawner(_serializedMap.FindProperty("enemySpawns")))
        {
            EditorGUILayout.HelpBox(
                "Kill All Enemies cannot complete while any spawn point has unlimited waves. Enable Maximum Waves for every spawn point.",
                MessageType.Warning);
        }
        ApplyMapPropertyChanges("Edit Enemy Spawn Settings");
    }

    private void DrawMainTowerLayerSettings()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Main Tower", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("mainTowerPrefab"),
            new GUIContent("Main Tower Prefab"));
        ApplyMapPropertyChanges("Edit Main Tower Settings");
    }

    private void DrawBossLayerSettings()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Boss Layer", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("bossPrefab"),
            new GUIContent("Boss Prefab"));
        EditorGUILayout.Space(5f);
        DrawBossEncounters(_serializedMap.FindProperty("bossEncounters"));
        if (!ContainsVictoryCondition(_serializedMap.FindProperty("victoryConditions"),
                RougeLevelVictoryConditionType.KillBoss) &&
            HasVictoryBossEncounter(_serializedMap.FindProperty("bossEncounters")))
        {
            EditorGUILayout.HelpBox(
                "A Boss has Victory On Defeat enabled, but this level has no Kill Boss victory condition; defeating it will not win.",
                MessageType.Warning);
        }
        ApplyMapPropertyChanges("Edit Boss Layer Settings");
    }

    private void ApplyMapPropertyChanges(string undoName)
    {
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_map, undoName);
            _serializedMap.ApplyModifiedProperties();
            EditorUtility.SetDirty(_map);
        }
        else
        {
            _serializedMap.ApplyModifiedProperties();
        }
    }

    private void DrawEnemySpawns(SerializedProperty spawns)
    {
        int dragControlId = GUIUtility.GetControlID("EnemySpawnReorder".GetHashCode(), FocusType.Passive);
        Event currentEvent = Event.current;
        if (_draggedEnemySpawnIndex >= 0 && GUIUtility.hotControl == dragControlId &&
            currentEvent.type == EventType.MouseDrag)
            _enemySpawnDropIndex = 0;
        EditorGUILayout.LabelField("Enemy Spawn Points", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Numbers match the red markers on the map. Drag the left handle to reorder; map IDs update with the list.",
            MessageType.Info);
        if (spawns != null && _expandedEnemySpawnIndex >= spawns.arraySize)
            _expandedEnemySpawnIndex = -1;
        if (spawns == null || spawns.arraySize == 0)
        {
            EditorGUILayout.HelpBox("No enemy spawn points. Click a walkable map cell to add one.",
                MessageType.None);
            return;
        }

        for (int i = 0; i < spawns.arraySize; i++)
        {
            SerializedProperty spawn = spawns.GetArrayElementAtIndex(i);
            SerializedProperty cell = spawn.FindPropertyRelative("cell");
            SerializedProperty enemyType = spawn.FindPropertyRelative("enemyType");
            Vector2Int cellValue = cell.vector2IntValue;
            string typeName = enemyType.enumValueIndex >= 0 &&
                              enemyType.enumValueIndex < enemyType.enumDisplayNames.Length
                ? enemyType.enumDisplayNames[enemyType.enumValueIndex]
                : enemyType.intValue.ToString();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect headerRect = EditorGUILayout.GetControlRect(false, 22f);
                Rect dragRect = new Rect(headerRect.x, headerRect.y, 20f, headerRect.height);
                Rect foldoutRect = new Rect(headerRect.x + 20f, headerRect.y,
                    Mathf.Max(20f, headerRect.width - 48f), headerRect.height);
                Rect removeRect = new Rect(headerRect.xMax - 24f, headerRect.y, 24f, headerRect.height);
                if (_draggedEnemySpawnIndex >= 0 && _enemySpawnDropIndex == i &&
                    currentEvent.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(headerRect, new Color(0.12f, 0.55f, 1f, 0.22f));
                }
                GUI.Label(dragRect, new GUIContent("≡", "Drag to reorder and change map marker IDs"),
                    EditorStyles.centeredGreyMiniLabel);
                EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.Pan);
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 &&
                    dragRect.Contains(currentEvent.mousePosition))
                {
                    _draggedEnemySpawnIndex = i;
                    _enemySpawnDropIndex = i;
                    GUIUtility.hotControl = dragControlId;
                    currentEvent.Use();
                }
                if (_draggedEnemySpawnIndex >= 0 && GUIUtility.hotControl == dragControlId &&
                    currentEvent.type == EventType.MouseDrag &&
                    currentEvent.mousePosition.y >= headerRect.center.y)
                {
                    _enemySpawnDropIndex = i;
                }
                bool expanded = _expandedEnemySpawnIndex == i;
                bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded,
                    $"#{i + 1}   Cell ({cellValue.x}, {cellValue.y})   {typeName}", true,
                    EditorStyles.foldoutHeader);
                if (nextExpanded != expanded)
                    _expandedEnemySpawnIndex = nextExpanded ? i : -1;
                if (GUI.Button(removeRect, new GUIContent("×", "Delete this spawn point")) &&
                    EditorUtility.DisplayDialog("Delete Enemy Spawn Point",
                        $"Delete spawn #{i + 1} at Cell ({cellValue.x}, {cellValue.y})?\n\n" +
                        "Map marker IDs after it will move forward. You can also use Ctrl+Z to undo.",
                        "Delete", "Cancel"))
                {
                    spawns.DeleteArrayElementAtIndex(i);
                    if (_expandedEnemySpawnIndex == i) _expandedEnemySpawnIndex = -1;
                    else if (_expandedEnemySpawnIndex > i) _expandedEnemySpawnIndex--;
                    if (_scrollToEnemySpawnIndex == i) _scrollToEnemySpawnIndex = -1;
                    else if (_scrollToEnemySpawnIndex > i) _scrollToEnemySpawnIndex--;
                    break;
                }

                if (_scrollToEnemySpawnIndex == i && Event.current.type == EventType.Repaint)
                {
                    _settingsScroll.y = Mathf.Max(0f, headerRect.y - 72f);
                    _scrollToEnemySpawnIndex = -1;
                    Repaint();
                }
                if (_expandedEnemySpawnIndex != i) continue;

                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 125f;
                EditorGUILayout.LabelField("Cell", $"({cellValue.x}, {cellValue.y})");
                SerializedProperty spawnCount = spawn.FindPropertyRelative("spawnCount");
                spawnCount.intValue = Mathf.Clamp(
                    EditorGUILayout.IntField("Enemies per Wave", spawnCount.intValue), 1, 64);
                SerializedProperty spawnInterval = spawn.FindPropertyRelative("spawnInterval");
                spawnInterval.floatValue = Mathf.Max(0.1f,
                    EditorGUILayout.FloatField("Spawn Interval", spawnInterval.floatValue));
                SerializedProperty startDelay = spawn.FindPropertyRelative("startDelay");
                startDelay.floatValue = Mathf.Max(0f,
                    EditorGUILayout.FloatField("Start Delay", startDelay.floatValue));
                EditorGUILayout.PropertyField(enemyType, new GUIContent("Enemy Type"));
                SerializedProperty limitWaves = spawn.FindPropertyRelative("limitWaveCount");
                EditorGUILayout.PropertyField(limitWaves, new GUIContent("Limit Waves"));
                if (limitWaves.boolValue)
                {
                    SerializedProperty maximumWaves = spawn.FindPropertyRelative("maximumWaves");
                    maximumWaves.intValue = Mathf.Max(1,
                        EditorGUILayout.IntField("Maximum Waves", maximumWaves.intValue));
                }
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        if (_draggedEnemySpawnIndex >= 0 && GUIUtility.hotControl == dragControlId)
        {
            if (currentEvent.type == EventType.MouseDrag)
            {
                currentEvent.Use();
                Repaint();
            }
            else if (currentEvent.rawType == EventType.MouseUp)
            {
                int sourceIndex = _draggedEnemySpawnIndex;
                int destinationIndex = Mathf.Clamp(_enemySpawnDropIndex, 0, spawns.arraySize - 1);
                _draggedEnemySpawnIndex = -1;
                _enemySpawnDropIndex = -1;
                GUIUtility.hotControl = 0;
                if (sourceIndex != destinationIndex)
                    MoveEnemySpawn(spawns, sourceIndex, destinationIndex);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                _draggedEnemySpawnIndex = -1;
                _enemySpawnDropIndex = -1;
                GUIUtility.hotControl = 0;
                currentEvent.Use();
                Repaint();
            }
        }
    }

    private void MoveEnemySpawn(SerializedProperty spawns, int sourceIndex, int destinationIndex)
    {
        if (spawns == null || sourceIndex < 0 || destinationIndex < 0 ||
            sourceIndex >= spawns.arraySize || destinationIndex >= spawns.arraySize ||
            sourceIndex == destinationIndex) return;
        spawns.MoveArrayElement(sourceIndex, destinationIndex);
        _expandedEnemySpawnIndex = RemapMovedIndex(_expandedEnemySpawnIndex,
            sourceIndex, destinationIndex);
        _scrollToEnemySpawnIndex = RemapMovedIndex(_scrollToEnemySpawnIndex,
            sourceIndex, destinationIndex);
        Repaint();
    }

    private static int RemapMovedIndex(int trackedIndex, int sourceIndex, int destinationIndex)
    {
        if (trackedIndex == sourceIndex) return destinationIndex;
        if (sourceIndex < destinationIndex && trackedIndex > sourceIndex &&
            trackedIndex <= destinationIndex) return trackedIndex - 1;
        if (sourceIndex > destinationIndex && trackedIndex >= destinationIndex &&
            trackedIndex < sourceIndex) return trackedIndex + 1;
        return trackedIndex;
    }

    private void DrawLevelRulesSettings()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Level Rules", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();

        DrawVictoryConditions(_serializedMap.FindProperty("victoryConditions"));

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("Level Economy / Multipliers", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("startingGold"), new GUIContent("Starting Gold"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("enemyHealthMultiplier"), new GUIContent("Enemy Health ×"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("enemyMoveSpeedMultiplier"), new GUIContent("Enemy Move Speed ×"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("towerGoldCostMultiplier"), new GUIContent("Tower Gold Cost ×"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("towerDamageMultiplier"), new GUIContent("Tower Damage ×"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("towerAttackSpeedMultiplier"), new GUIContent("Tower Attack Speed ×"));

        EditorGUILayout.Space(5f);
        DrawDisabledTowerIds(_serializedMap.FindProperty("disabledTowerTypeIds"));

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_map, "Edit Tower Defense Level Rules");
            _serializedMap.ApplyModifiedProperties();
            EditorUtility.SetDirty(_map);
        }
        else
        {
            _serializedMap.ApplyModifiedProperties();
        }
    }

    private static bool ContainsVictoryCondition(SerializedProperty conditions,
        RougeLevelVictoryConditionType expected)
    {
        for (int i = 0; conditions != null && i < conditions.arraySize; i++)
        {
            SerializedProperty type = conditions.GetArrayElementAtIndex(i).FindPropertyRelative("type");
            if (type != null && type.enumValueIndex == (int)expected) return true;
        }
        return false;
    }

    private static bool HasUnlimitedEnemySpawner(SerializedProperty spawners)
    {
        for (int i = 0; spawners != null && i < spawners.arraySize; i++)
        {
            SerializedProperty limited = spawners.GetArrayElementAtIndex(i)
                .FindPropertyRelative("limitWaveCount");
            if (limited == null || !limited.boolValue) return true;
        }
        return false;
    }

    private static bool HasVictoryBossEncounter(SerializedProperty encounters)
    {
        for (int i = 0; encounters != null && i < encounters.arraySize; i++)
        {
            SerializedProperty grantsVictory = encounters.GetArrayElementAtIndex(i)
                .FindPropertyRelative("defeatGrantsVictory");
            if (grantsVictory != null && grantsVictory.boolValue) return true;
        }
        return false;
    }

    private static void DrawVictoryConditions(SerializedProperty conditions)
    {
        EditorGUILayout.LabelField("Victory Conditions (ANY / OR)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("The level wins as soon as any configured condition is satisfied.", MessageType.Info);
        for (int i = 0; i < conditions.arraySize; i++)
        {
            SerializedProperty condition = conditions.GetArrayElementAtIndex(i);
            SerializedProperty type = condition.FindPropertyRelative("type");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(type, GUIContent.none);
                    if (GUILayout.Button("−", GUILayout.Width(24f)))
                    {
                        conditions.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
                RougeLevelVictoryConditionType conditionType =
                    (RougeLevelVictoryConditionType)type.enumValueIndex;
                if (conditionType == RougeLevelVictoryConditionType.KillEnemies)
                {
                    EditorGUILayout.PropertyField(condition.FindPropertyRelative("targetAmount"),
                        new GUIContent("Enemy Kills"));
                }
                else if (conditionType == RougeLevelVictoryConditionType.SurviveSeconds)
                {
                    EditorGUILayout.PropertyField(condition.FindPropertyRelative("targetSeconds"),
                        new GUIContent("Survival Seconds"));
                }
                else if (conditionType == RougeLevelVictoryConditionType.EarnGold)
                {
                    EditorGUILayout.PropertyField(condition.FindPropertyRelative("targetAmount"),
                        new GUIContent("Earned Gold"));
                }
            }
        }
        if (GUILayout.Button("Add Victory Condition"))
        {
            int index = conditions.arraySize;
            conditions.InsertArrayElementAtIndex(index);
            SerializedProperty added = conditions.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("type").enumValueIndex =
                (int)RougeLevelVictoryConditionType.KillEnemies;
            added.FindPropertyRelative("targetAmount").intValue = 100;
            added.FindPropertyRelative("targetSeconds").floatValue = 300f;
        }
        if (conditions.arraySize == 0)
            EditorGUILayout.HelpBox("No victory condition is configured; this level cannot be completed.", MessageType.Warning);
    }

    private static void DrawDisabledTowerIds(SerializedProperty disabledIds)
    {
        EditorGUILayout.LabelField("Disabled Towers", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Known integer IDs are shown as tower enums. The serialized list remains List<int> so future mod tower IDs can be entered directly.",
            MessageType.None);
        for (int i = 0; i < disabledIds.arraySize; i++)
        {
            SerializedProperty id = disabledIds.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool known = Enum.IsDefined(typeof(RougeTowerType), id.intValue);
                if (known)
                {
                    RougeTowerType selected = (RougeTowerType)id.intValue;
                    id.intValue = (int)(RougeTowerType)EditorGUILayout.EnumPopup(selected);
                }
                else
                {
                    id.intValue = EditorGUILayout.IntField($"Mod Tower ID {i + 1}", id.intValue);
                }
                if (GUILayout.Button("−", GUILayout.Width(24f)))
                {
                    disabledIds.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }
        if (GUILayout.Button("Add Disabled Tower"))
        {
            int index = disabledIds.arraySize;
            disabledIds.InsertArrayElementAtIndex(index);
            disabledIds.GetArrayElementAtIndex(index).intValue = FindFirstUnusedTowerId(disabledIds, index);
        }
    }

    private static int FindFirstUnusedTowerId(SerializedProperty ids, int excludedIndex)
    {
        foreach (RougeTowerType type in Enum.GetValues(typeof(RougeTowerType)))
        {
            int candidate = (int)type;
            bool used = false;
            for (int i = 0; i < ids.arraySize; i++)
            {
                if (i == excludedIndex) continue;
                if (ids.GetArrayElementAtIndex(i).intValue != candidate) continue;
                used = true;
                break;
            }
            if (!used) return candidate;
        }
        int nextModId = 0;
        foreach (RougeTowerType type in Enum.GetValues(typeof(RougeTowerType)))
            nextModId = Mathf.Max(nextModId, (int)type + 1);
        return nextModId;
    }

    private static void DrawBossEncounters(SerializedProperty encounters)
    {
        EditorGUILayout.LabelField("Boss Schedule", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Bosses use the map's Boss spawn marker. Multiple entries spawn sequentially; a later Boss waits if the previous one is still alive. Victory On Defeat also requires a Kill Boss victory condition.",
            MessageType.Info);
        GetBossEditorOptions(out int[] bossIds, out string[] bossLabels);
        for (int i = 0; i < encounters.arraySize; i++)
        {
            SerializedProperty encounter = encounters.GetArrayElementAtIndex(i);
            SerializedProperty bossId = encounter.FindPropertyRelative("bossId");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int currentOption = Array.IndexOf(bossIds, bossId.intValue);
                    if (currentOption >= 0)
                    {
                        currentOption = EditorGUILayout.Popup($"Boss {i + 1}", currentOption, bossLabels);
                        bossId.intValue = bossIds[Mathf.Clamp(currentOption, 0, bossIds.Length - 1)];
                    }
                    else
                    {
                        bossId.intValue = EditorGUILayout.IntField($"Boss {i + 1} ID", bossId.intValue);
                    }
                    if (GUILayout.Button("−", GUILayout.Width(24f)))
                    {
                        encounters.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
                EditorGUILayout.PropertyField(encounter.FindPropertyRelative("spawnMinute"),
                    new GUIContent("Spawn At Minute"));
                EditorGUILayout.PropertyField(encounter.FindPropertyRelative("defeatGrantsVictory"),
                    new GUIContent("Victory On Defeat"));
            }
        }
        if (GUILayout.Button("Add Boss Encounter"))
        {
            int index = encounters.arraySize;
            encounters.InsertArrayElementAtIndex(index);
            SerializedProperty added = encounters.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("bossId").intValue = bossIds.Length > 0 ? bossIds[0] : 0;
            added.FindPropertyRelative("spawnMinute").floatValue = 15f;
            added.FindPropertyRelative("defeatGrantsVictory").boolValue = false;
        }
    }

    private static void GetBossEditorOptions(out int[] ids, out string[] labels)
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(RougeTowerDefenseBalanceJson.AssetPath);
        int jsonHash = json != null && json.text != null ? json.text.GetHashCode() : 0;
        if (s_bossOptionIds != null && s_bossOptionLabels != null && s_bossOptionsJsonHash == jsonHash)
        {
            ids = s_bossOptionIds;
            labels = s_bossOptionLabels;
            return;
        }
        var idList = new List<int>();
        var labelList = new List<string>();
        if (json != null && !string.IsNullOrWhiteSpace(json.text))
        {
            try
            {
                RougeTowerDefenseBalanceJsonData data =
                    JsonUtility.FromJson<RougeTowerDefenseBalanceJsonData>(json.text);
                data?.EnsureDefaults();
                if (data?.bossBalances != null)
                {
                    for (int i = 0; i < data.bossBalances.Count; i++)
                    {
                        RougeBossBalanceConfig boss = data.bossBalances[i];
                        if (boss == null || idList.Contains(boss.bossId)) continue;
                        idList.Add(boss.bossId);
                        labelList.Add($"{boss.displayName} (ID {boss.bossId})");
                    }
                }
            }
            catch (Exception)
            {
                // The Balance window reports malformed JSON in detail. Keep the Map Painter usable.
            }
        }
        if (idList.Count == 0)
        {
            idList.Add(0);
            labelList.Add("Overlord (ID 0)");
        }
        s_bossOptionsJsonHash = jsonHash;
        s_bossOptionIds = idList.ToArray();
        s_bossOptionLabels = labelList.ToArray();
        ids = s_bossOptionIds;
        labels = s_bossOptionLabels;
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
                    SelectEnemySpawnAt(cell);
                    Undo.RecordObject(_map, "Move Enemy Spawn");
                    _upperDragKind = UpperDragKind.Enemy;
                    _upperDragSource = cell;
                }
                else if (_map.IsGround(cell) && !_map.HasUpperObject(cell))
                {
                    Undo.RecordObject(_map, "Create Enemy Spawn");
                    if (_map.AddEnemySpawn(cell))
                    {
                        SelectEnemySpawn(_map.EnemySpawns.Count - 1);
                        EditorUtility.SetDirty(_map);
                    }
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

    private void SelectEnemySpawnAt(Vector2Int cell)
    {
        for (int i = 0; i < _map.EnemySpawns.Count; i++)
        {
            if (_map.EnemySpawns[i].cell != cell) continue;
            SelectEnemySpawn(i);
            return;
        }
    }

    private void SelectEnemySpawn(int index)
    {
        _expandedEnemySpawnIndex = index;
        _scrollToEnemySpawnIndex = index;
        Repaint();
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
        _expandedEnemySpawnIndex = -1;
        _scrollToEnemySpawnIndex = -1;
        _draggedEnemySpawnIndex = -1;
        _enemySpawnDropIndex = -1;
        _settingsScroll = Vector2.zero;
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
        RougeTowerDefenseMapLoader[] loaders = UnityEngine.Object.FindObjectsByType<RougeTowerDefenseMapLoader>(
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
        RougeTowerDefenseMapLoader loader = UnityEngine.Object.FindFirstObjectByType<RougeTowerDefenseMapLoader>();
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
