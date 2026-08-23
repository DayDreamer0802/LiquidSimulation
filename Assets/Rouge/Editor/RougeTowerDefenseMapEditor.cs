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
        BossSpawn
    }

    private enum UpperDragKind
    {
        None,
        Enemy,
        MainTower,
        BossSpawn
    }

    private RougeTowerDefenseMap _mapAsset;
    private RougeTowerDefenseMap _map;
    private SerializedObject _serializedMap;
    private RougeTowerDefenseTilePalette _tilePalette;
    private SerializedObject _serializedTilePalette;
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
    private int _paintUndoGroup = -1;
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
    private string _savedDraftJson = string.Empty;
    private string _saveStatus = string.Empty;

    [MenuItem("Rouge/塔防/地图编辑器")]
    public static void Open()
    {
        RougeTowerDefenseMapEditor window = GetWindow<RougeTowerDefenseMapEditor>();
        window.titleContent = new GUIContent("塔防地图编辑器");
        window.minSize = new Vector2(900f, 640f);
        window.Show();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        saveChangesMessage = "地图还有未保存的修改。要保存到关卡 Asset 吗？";
        ResolveSharedTilePalette();
        RougeTowerDefenseMap selected = Selection.activeObject as RougeTowerDefenseMap;
        if (selected != null) SetMap(selected);
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        DestroyWorkingCopy();
    }

    public override void SaveChanges()
    {
        SaveMap();
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        ReloadWorkingCopy();
        base.DiscardChanges();
    }

    private void OnSelectionChange()
    {
        if (Selection.activeObject is RougeTowerDefenseMap selected && selected != _mapAsset)
            TrySetMap(selected);
        Repaint();
    }

    private void OnGUI()
    {
        HandleSaveShortcut();
        if (_tilePalette == null) ResolveSharedTilePalette();
        DrawTopBar();
        if (_map == null)
        {
            EditorGUILayout.HelpBox("请新建或选择一个塔防地图资源。", MessageType.Info);
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
                _mapAsset, typeof(RougeTowerDefenseMap), false, GUILayout.Width(260f));
            if (EditorGUI.EndChangeCheck()) TrySetMap(selected);
            if (GUILayout.Button("新建地图", EditorStyles.toolbarButton, GUILayout.Width(75f))) CreateMapAsset();
            using (new EditorGUI.DisabledScope(_mapAsset == null))
            {
                if (GUILayout.Button("添加/更新场景加载器", EditorStyles.toolbarButton, GUILayout.Width(160f)))
                    AddOrUpdateLoader();
                Color previousBackground = GUI.backgroundColor;
                if (hasUnsavedChanges) GUI.backgroundColor = new Color(1f, 0.68f, 0.18f);
                if (GUILayout.Button(hasUnsavedChanges ? "保存 *" : "保存", EditorStyles.toolbarButton,
                        GUILayout.Width(62f))) SaveMap();
                GUI.backgroundColor = previousBackground;
                using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
                {
                    if (GUILayout.Button("放弃修改", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                        ReloadWorkingCopy();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(hasUnsavedChanges ? "未保存：仅存在于当前草稿" : "已保存",
                EditorStyles.miniLabel);
        }
        if (!string.IsNullOrEmpty(_saveStatus))
            EditorGUILayout.HelpBox(_saveStatus, hasUnsavedChanges ? MessageType.Warning : MessageType.Info);
    }

    private void DrawSettingsPanel()
    {
        if (_serializedMap == null || _serializedMap.targetObject != _map)
            _serializedMap = new SerializedObject(_map);
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(360f)))
        {
            _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll);
            EditorGUILayout.LabelField("图层 / 画笔", EditorStyles.boldLabel);
            PaintTool nextTool = (PaintTool)GUILayout.Toolbar((int)_tool,
                new[] { "地图", "敌人", "主塔", "Boss" }, GUILayout.Height(28f));
            if (nextTool != _tool)
            {
                _tool = nextTool;
                _settingsScroll = Vector2.zero;
                GUI.FocusControl(null);
            }
            _pixelSize = EditorGUILayout.Slider("画布格子像素", _pixelSize, 15f, 30f);

            if (_tool == PaintTool.Tile)
                EditorGUILayout.HelpBox("左键：绘制地形。右键：删除地形及其上层对象。主塔所在格受保护。", MessageType.None);
            else if (_tool == PaintTool.EnemySpawn)
                EditorGUILayout.HelpBox("点击空的可行走地块创建敌人出生点。拖动已有编号标记可移动；右键只删除上层标记。", MessageType.None);
            else
                EditorGUILayout.HelpBox("只编辑上层：对象必须放在可行走地块上且不能重叠。右键会保留底层地形；主塔不能删除。", MessageType.None);

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
        EditorGUILayout.LabelField("地图尺寸", EditorStyles.boldLabel);
        _pendingWidth = EditorGUILayout.IntSlider("宽度", _pendingWidth, 1, RougeTowerDefenseMap.MaxMapCells);
        _pendingHeight = EditorGUILayout.IntSlider("高度", _pendingHeight, 1, RougeTowerDefenseMap.MaxMapCells);
        _pendingCellSize = EditorGUILayout.FloatField("世界格子尺寸", _pendingCellSize);
        using (new EditorGUI.DisabledScope(
                   _pendingWidth == _map.Width && _pendingHeight == _map.Height &&
                   Mathf.Approximately(_pendingCellSize, _map.CellSize)))
        {
            if (GUILayout.Button("应用尺寸（保留已有格子）"))
            {
                Undo.RegisterCompleteObjectUndo(_map, "Resize Tower Defense Map");
                _map.ResizeGrid(_pendingWidth, _pendingHeight, _pendingCellSize, true);
                MarkMapChanged();
            }
        }
        EditorGUILayout.HelpBox(
            $"世界尺寸：{_map.Width * _map.CellSize:0.#} × {_map.Height * _map.CellSize:0.#}\n" +
            $"每个地形格包含 {RougeTowerDefenseMap.MicroCellsPerTile} × {RougeTowerDefenseMap.MicroCellsPerTile} 个微格。\n" +
            "地图中心始终位于世界坐标 (0,0)。",
            MessageType.None);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("共享地块调色板", EditorStyles.boldLabel);
        if (_tilePalette == null)
        {
            EditorGUILayout.HelpBox(
                "找不到 Resources/Config/TowerDefenseTilePalette。地图会暂时使用自身的旧版地块定义。",
                MessageType.Error);
            return;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(_tilePalette,
                    typeof(RougeTowerDefenseTilePalette), false);
            if (GUILayout.Button("定位", GUILayout.Width(52f)))
                Selection.activeObject = _tilePalette;
        }
        EditorGUILayout.HelpBox(
            "此调色板由所有塔防地图共同使用。地块索引、预制体、颜色或效果的修改会立即保存，并同步影响所有地图。\n" +
            "地图保存的是地块整数索引：已有条目请勿重排或删除，新地块应追加到列表末尾。",
            MessageType.Info);
        for (int i = 1; i < _map.TileDefinitions.Count; i++)
        {
            RougeTowerDefenseMap.TileDefinition definition = _map.TileDefinitions[i];
            Rect row = EditorGUILayout.GetControlRect(false, 24f);
            Rect swatch = new Rect(row.x + 3f, row.y + 3f, 18f, 18f);
            EditorGUI.DrawRect(swatch, definition.editorColor);
            string effectSuffix = definition.towerPlace &&
                                  definition.towerPlaceEffect != RougeTowerPlaceEffect.None
                ? $"  [效果 {(int)definition.towerPlaceEffect}]"
                : string.Empty;
            if (GUI.Toggle(new Rect(row.x + 25f, row.y, row.width - 25f, row.height),
                    _tileIndex == i, $"{i}: {definition.name}{effectSuffix}", "Button")) _tileIndex = i;
        }

        if (_tileIndex > 0 && _tileIndex < _map.TileDefinitions.Count &&
            _map.TileDefinitions[_tileIndex].towerPlace)
        {
            RougeTowerDefenseMap.TileDefinition selectedDefinition = _map.TileDefinitions[_tileIndex];
            EditorGUI.BeginChangeCheck();
            RougeTowerPlaceEffect selectedEffect = (RougeTowerPlaceEffect)EditorGUILayout.EnumPopup(
                "所选塔楼格效果", selectedDefinition.towerPlaceEffect);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(_tilePalette, "Change Shared Tower Grid Effect");
                selectedDefinition.towerPlaceEffect = selectedEffect;
                SaveSharedTilePalette();
            }
            EditorGUI.BeginChangeCheck();
            Texture2D selectedIcon = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("所选地图格图标",
                    "可选的纯白透明图标；未配置时继续显示原来的中心圆圈。"),
                selectedDefinition.towerPlaceIcon, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(_tilePalette,
                    "Change Shared Tower Grid Icon");
                selectedDefinition.towerPlaceIcon = selectedIcon;
                SaveSharedTilePalette();
            }
            EditorGUILayout.HelpBox(
                RougeTowerPlaceEffectRules.GetDisplayName(selectedDefinition.towerPlaceEffect) + "\n" +
                RougeTowerPlaceEffectRules.GetDescription(selectedDefinition.towerPlaceEffect) + "\n" +
                (selectedDefinition.towerPlaceIcon != null
                    ? "中心图标：已配置（运行时使用地块效果颜色着色）"
                    : "中心图标：未配置（使用原中心圆圈）"),
                MessageType.None);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("地块定义", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "自动地块索引使用北/东/南/西位：北 1、东 2、南 4、西 8。缺少的变体会使用基础预制体。" +
            "塔楼格效果按地块定义配置；一座塔只读取其中心点下方的一个地形格。", MessageType.None);
        _serializedTilePalette.Update();
        EditorGUILayout.PropertyField(
            _serializedTilePalette.FindProperty("tileDefinitions"), true);
        if (_serializedTilePalette.ApplyModifiedProperties())
        {
            _tileIndex = Mathf.Clamp(_tileIndex, 1,
                Mathf.Max(1, _map.TileDefinitions.Count - 1));
            SaveSharedTilePalette();
        }
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
                "存在无限波次的出生点时，无法完成“消灭全部敌人”。请为所有出生点启用波次限制。",
                MessageType.Warning);
        }
        ApplyMapPropertyChanges("Edit Enemy Spawn Settings");
    }

    private void DrawMainTowerLayerSettings()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("主塔", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("mainTowerPrefab"),
            new GUIContent("主塔预制体"));
        ApplyMapPropertyChanges("Edit Main Tower Settings");

        // Level-wide gameplay rules live on the Main tab so the Map tab stays focused on terrain.
        DrawLevelRulesSettings();
        DrawLevelCameraSettings();
    }

    private void DrawBossLayerSettings()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Boss 图层", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("bossPrefab"),
            new GUIContent("Boss 预制体"));
        EditorGUILayout.Space(5f);
        DrawBossEncounters(_serializedMap.FindProperty("bossEncounters"));
        if (!ContainsVictoryCondition(_serializedMap.FindProperty("victoryConditions"),
                RougeLevelVictoryConditionType.KillBoss) &&
            HasVictoryBossEncounter(_serializedMap.FindProperty("bossEncounters")))
        {
            EditorGUILayout.HelpBox(
                "某个 Boss 启用了“击败后胜利”，但本关没有“击杀 Boss”胜利条件，因此击败它不会获胜。",
                MessageType.Warning);
        }
        ApplyMapPropertyChanges("Edit Boss Layer Settings");
    }

    private void ApplyMapPropertyChanges(string undoName)
    {
        bool guiChanged = EditorGUI.EndChangeCheck();
        if (guiChanged) Undo.SetCurrentGroupName(undoName);
        if (_serializedMap.ApplyModifiedProperties()) MarkMapChanged();
    }

    private void DrawEnemySpawns(SerializedProperty spawns)
    {
        int dragControlId = GUIUtility.GetControlID("EnemySpawnReorder".GetHashCode(), FocusType.Passive);
        Event currentEvent = Event.current;
        if (_draggedEnemySpawnIndex >= 0 && GUIUtility.hotControl == dragControlId &&
            currentEvent.type == EventType.MouseDrag)
            _enemySpawnDropIndex = 0;
        EditorGUILayout.LabelField("敌人出生点", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "编号与地图上的红色标记对应。拖动左侧把手可以排序，地图标记编号会随列表更新。",
            MessageType.Info);
        if (spawns != null && _expandedEnemySpawnIndex >= spawns.arraySize)
            _expandedEnemySpawnIndex = -1;
        if (spawns == null || spawns.arraySize == 0)
        {
            EditorGUILayout.HelpBox("当前没有敌人出生点。点击可行走地图格即可添加。",
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
                GUI.Label(dragRect, new GUIContent("≡", "拖动排序并更新地图标记编号"),
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
                    $"#{i + 1}   格子 ({cellValue.x}, {cellValue.y})   {typeName}", true,
                    EditorStyles.foldoutHeader);
                if (nextExpanded != expanded)
                    _expandedEnemySpawnIndex = nextExpanded ? i : -1;
                if (GUI.Button(removeRect, new GUIContent("×", "删除这个出生点")) &&
                    EditorUtility.DisplayDialog("删除敌人出生点",
                        $"确定删除格子 ({cellValue.x}, {cellValue.y}) 上的出生点 #{i + 1} 吗？\n\n" +
                        "后续地图标记编号会前移，也可以按 Ctrl+Z 撤销。",
                        "删除", "取消"))
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
                EditorGUILayout.LabelField("格子", $"({cellValue.x}, {cellValue.y})");
                SerializedProperty spawnCount = spawn.FindPropertyRelative("spawnCount");
                spawnCount.intValue = Mathf.Clamp(
                    EditorGUILayout.IntField("每波敌人数", spawnCount.intValue), 1, 64);
                SerializedProperty spawnInterval = spawn.FindPropertyRelative("spawnInterval");
                spawnInterval.floatValue = Mathf.Max(0.1f,
                    EditorGUILayout.FloatField("生成间隔", spawnInterval.floatValue));
                SerializedProperty startDelay = spawn.FindPropertyRelative("startDelay");
                startDelay.floatValue = Mathf.Max(0f,
                    EditorGUILayout.FloatField("开始延迟", startDelay.floatValue));
                EditorGUILayout.PropertyField(enemyType, new GUIContent("敌人类型"));
                SerializedProperty limitWaves = spawn.FindPropertyRelative("limitWaveCount");
                EditorGUILayout.PropertyField(limitWaves, new GUIContent("限制波次数"));
                if (limitWaves.boolValue)
                {
                    SerializedProperty maximumWaves = spawn.FindPropertyRelative("maximumWaves");
                    maximumWaves.intValue = Mathf.Max(1,
                        EditorGUILayout.IntField("最大波次数", maximumWaves.intValue));
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
        EditorGUILayout.LabelField("关卡规则", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();

        DrawVictoryConditions(_serializedMap.FindProperty("victoryConditions"));

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("关卡经济 / 倍率", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("startingGold"), new GUIContent("初始金币"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("enemyHealthMultiplier"), new GUIContent("敌人生命倍率"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("enemyMoveSpeedMultiplier"), new GUIContent("敌人移速倍率"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("towerGoldCostMultiplier"), new GUIContent("塔楼金币消耗倍率"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("towerDamageMultiplier"), new GUIContent("塔楼伤害倍率"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("towerAttackSpeedMultiplier"), new GUIContent("塔楼攻速倍率"));

        EditorGUILayout.Space(5f);
        DrawDisabledTowerIds(_serializedMap.FindProperty("disabledTowerTypeIds"));

        bool guiChanged = EditorGUI.EndChangeCheck();
        if (guiChanged) Undo.SetCurrentGroupName("Edit Tower Defense Level Rules");
        if (_serializedMap.ApplyModifiedProperties()) MarkMapChanged();
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
        EditorGUILayout.LabelField("胜利条件（任意一个 / 或）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("只要满足任意一个已配置条件，本关立即胜利。", MessageType.Info);
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
                        new GUIContent("击杀敌人数"));
                }
                else if (conditionType == RougeLevelVictoryConditionType.SurviveSeconds)
                {
                    EditorGUILayout.PropertyField(condition.FindPropertyRelative("targetSeconds"),
                        new GUIContent("生存秒数"));
                }
                else if (conditionType == RougeLevelVictoryConditionType.EarnGold)
                {
                    EditorGUILayout.PropertyField(condition.FindPropertyRelative("targetAmount"),
                        new GUIContent("累计获得金币"));
                }
            }
        }
        if (GUILayout.Button("添加胜利条件"))
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
            EditorGUILayout.HelpBox("尚未配置胜利条件，本关将无法完成。", MessageType.Warning);
    }

    private static void DrawDisabledTowerIds(SerializedProperty disabledIds)
    {
        EditorGUILayout.LabelField("禁用塔楼", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "已知整数 ID 会显示为塔楼类型。底层仍保存为 List<int>，以后可以直接输入 Mod 塔楼 ID。",
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
                    id.intValue = EditorGUILayout.IntField($"Mod 塔楼 ID {i + 1}", id.intValue);
                }
                if (GUILayout.Button("−", GUILayout.Width(24f)))
                {
                    disabledIds.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }
        if (GUILayout.Button("添加禁用塔楼"))
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
        EditorGUILayout.LabelField("Boss 出场计划", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Boss 使用地图上的 Boss 出生标记。多个 Boss 会按顺序生成；前一个仍存活时，后一个会等待。" +
            "“击败后胜利”还需要配置“击杀 Boss”胜利条件。",
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
                    new GUIContent("出场分钟"));
                EditorGUILayout.PropertyField(encounter.FindPropertyRelative("defeatGrantsVictory"),
                    new GUIContent("击败后胜利"));
            }
        }
        if (GUILayout.Button("添加 Boss"))
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
            labelList.Add("霸主（ID 0）");
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
        EditorGUILayout.LabelField("关卡镜头边界 / 缩放", EditorStyles.boldLabel);
        _serializedMap.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("configureCameraBounds"),
            new GUIContent("启用镜头边界"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("cameraBoundsCenter"),
            new GUIContent("边界中心（世界 X/Z）"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("cameraBoundsSize"),
            new GUIContent("边界尺寸（宽/高）"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("minimumCameraZoom"),
            new GUIContent("最小缩放"));
        EditorGUILayout.PropertyField(_serializedMap.FindProperty("maximumCameraZoom"),
            new GUIContent("最大缩放"));
        bool guiChanged = EditorGUI.EndChangeCheck();
        if (guiChanged) Undo.SetCurrentGroupName("Edit Level Camera Clamp");
        if (_serializedMap.ApplyModifiedProperties()) MarkMapChanged();
        EditorGUILayout.HelpBox(
            "这些数值保存在当前地图资源中，因此每个关卡互相独立。使用该地图的场景加载器会在运行时应用它们。",
            MessageType.None);
    }

    private void DrawMapPanel()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            string hover = _map.Contains(_hoverCell)
                ? $"格子 {_hoverCell.x}, {_hoverCell.y}  |  世界坐标 {FormatWorld(_hoverCell)}"
                : "左键拖动：绘制    右键拖动：擦除";
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
            Undo.IncrementCurrentGroup();
            _paintUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(evt.button == 1 ? "Erase Map Stroke" : "Paint Map Stroke");
            Undo.RegisterCompleteObjectUndo(_map,
                evt.button == 1 ? "Erase Map Stroke" : "Paint Map Stroke");
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
            if (_paintUndoGroup >= 0) Undo.CollapseUndoOperations(_paintUndoGroup);
            _paintUndoGroup = -1;
            MarkMapChanged();
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
        MarkMapChanged();
    }

    private void PaintCell(Vector2Int cell, bool forceErase)
    {
        if (forceErase)
        {
            if (_tool == PaintTool.Tile) _map.EraseBaseTile(cell);
            else _map.RemoveUpperObjectAt(cell);
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
                    Undo.RegisterCompleteObjectUndo(_map, "Move Enemy Spawn");
                    _upperDragKind = UpperDragKind.Enemy;
                    _upperDragSource = cell;
                }
                else if (_map.IsGround(cell) && !_map.HasUpperObject(cell))
                {
                    Undo.RegisterCompleteObjectUndo(_map, "Create Enemy Spawn");
                    if (_map.AddEnemySpawn(cell))
                    {
                        SelectEnemySpawn(_map.EnemySpawns.Count - 1);
                        MarkMapChanged();
                    }
                }
                break;
            case PaintTool.MainTower:
                if (_map.HasMainTower && _map.MainTowerCell == cell)
                {
                    Undo.RegisterCompleteObjectUndo(_map, "Move Main Tower");
                    _upperDragKind = UpperDragKind.MainTower;
                    _upperDragSource = cell;
                }
                else if (!_map.HasMainTower && _map.IsGround(cell) && !_map.HasUpperObject(cell))
                {
                    Undo.RegisterCompleteObjectUndo(_map, "Create Main Tower");
                    if (_map.PlaceMainTower(cell)) MarkMapChanged();
                }
                break;
            case PaintTool.BossSpawn:
                if (_map.HasBossSpawn && _map.BossSpawnCell == cell)
                {
                    Undo.RegisterCompleteObjectUndo(_map, "Move Boss Spawn");
                    _upperDragKind = UpperDragKind.BossSpawn;
                    _upperDragSource = cell;
                }
                else if (!_map.HasBossSpawn && _map.IsGround(cell) && !_map.HasUpperObject(cell))
                {
                    Undo.RegisterCompleteObjectUndo(_map, "Create Boss Spawn");
                    if (_map.PlaceBossSpawn(cell)) MarkMapChanged();
                }
                break;
        }
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
            MarkMapChanged();
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

    private void HandleSaveShortcut()
    {
        Event current = Event.current;
        if (current == null || current.type != EventType.KeyDown || current.keyCode != KeyCode.S ||
            (!current.control && !current.command)) return;
        SaveMap();
        current.Use();
    }

    private void OnUndoRedoPerformed()
    {
        bool paletteChanged = _tilePalette != null && EditorUtility.IsDirty(_tilePalette);
        if (paletteChanged)
        {
            AssetDatabase.SaveAssetIfDirty(_tilePalette);
            _serializedTilePalette?.UpdateIfRequiredOrScript();
        }
        if (_map == null)
        {
            Repaint();
            return;
        }
        _serializedMap?.UpdateIfRequiredOrScript();
        UpdateUnsavedState();
        _saveStatus = hasUnsavedChanges
            ? "已撤销/重做到草稿；点击“保存”才会写入关卡 Asset。"
            : paletteChanged
                ? "共享地块调色板已撤销/重做并保存；所有地图同步生效。"
                : "草稿已回到上次保存的状态。";
        Repaint();
    }

    private void MarkMapChanged()
    {
        if (_map == null) return;
        EditorUtility.SetDirty(_map);
        UpdateUnsavedState();
        if (hasUnsavedChanges)
            _saveStatus = "修改只保存在编辑器草稿中，尚未写入关卡 Asset。";
        Repaint();
    }

    private void UpdateUnsavedState()
    {
        hasUnsavedChanges = _map != null && GetDraftJson(_map) != _savedDraftJson;
    }

    private static string GetDraftJson(RougeTowerDefenseMap map)
    {
        return map != null ? EditorJsonUtility.ToJson(map, false) : string.Empty;
    }

    private void SaveMap()
    {
        if (_mapAsset == null || _map == null) return;
        _serializedMap?.ApplyModifiedProperties();
        string assetName = _mapAsset.name;
        EditorUtility.CopySerialized(_map, _mapAsset);
        _mapAsset.name = assetName;
        EditorUtility.SetDirty(_mapAsset);
        AssetDatabase.SaveAssetIfDirty(_mapAsset);
        if (_tilePalette != null) AssetDatabase.SaveAssetIfDirty(_tilePalette);
        _savedDraftJson = GetDraftJson(_map);
        hasUnsavedChanges = false;
        _saveStatus = "已手动保存到 " + AssetDatabase.GetAssetPath(_mapAsset);
        Repaint();
    }

    private bool ConfirmCanReplaceDraft()
    {
        if (!hasUnsavedChanges) return true;
        int choice = EditorUtility.DisplayDialogComplex(
            "地图尚未保存",
            "当前修改只存在于草稿中。切换地图后将无法恢复。",
            "保存并继续", "取消", "放弃修改");
        if (choice == 1) return false;
        if (choice == 0) SaveMap();
        return choice == 2 || !hasUnsavedChanges;
    }

    private void ReloadWorkingCopy()
    {
        RougeTowerDefenseMap asset = _mapAsset;
        SetMap(asset);
        _saveStatus = asset != null ? "已放弃草稿修改，并从关卡 Asset 重新载入。" : string.Empty;
    }

    private bool TrySetMap(RougeTowerDefenseMap map)
    {
        if (map == _mapAsset) return true;
        if (!ConfirmCanReplaceDraft()) return false;
        SetMap(map);
        return true;
    }

    private void SetMap(RougeTowerDefenseMap map)
    {
        DestroyWorkingCopy();
        _mapAsset = map;
        _map = null;
        _upperDragKind = UpperDragKind.None;
        _upperDragSource = new Vector2Int(-1, -1);
        _expandedEnemySpawnIndex = -1;
        _scrollToEnemySpawnIndex = -1;
        _draggedEnemySpawnIndex = -1;
        _enemySpawnDropIndex = -1;
        _settingsScroll = Vector2.zero;
        if (map != null)
        {
            _map = CreateInstance<RougeTowerDefenseMap>();
            EditorUtility.CopySerialized(map, _map);
            _map.name = map.name + " (Editing Draft)";
            _map.hideFlags = HideFlags.HideInHierarchy;
        }
        _serializedMap = _map != null ? new SerializedObject(_map) : null;
        _savedDraftJson = GetDraftJson(_map);
        hasUnsavedChanges = false;
        _saveStatus = map != null ? "已载入草稿；修改不会自动写入关卡 Asset。" : string.Empty;
        ResolveSceneLoader();
        if (_map != null)
        {
            _pendingWidth = _map.Width;
            _pendingHeight = _map.Height;
            _pendingCellSize = _map.CellSize;
            _tileIndex = Mathf.Clamp(_tileIndex, 1, Mathf.Max(1, _map.TileDefinitions.Count - 1));
        }
    }

    private void DestroyWorkingCopy()
    {
        if (_map == null) return;
        Undo.ClearUndo(_map);
        DestroyImmediate(_map);
        _map = null;
        _serializedMap = null;
    }

    private void ResolveSceneLoader()
    {
        _loader = null;
        RougeTowerDefenseMapLoader[] loaders = UnityEngine.Object.FindObjectsByType<RougeTowerDefenseMapLoader>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < loaders.Length; i++)
        {
            SerializedObject candidate = new SerializedObject(loaders[i]);
            if (candidate.FindProperty("map").objectReferenceValue != _mapAsset) continue;
            _loader = loaders[i];
            return;
        }
    }

    private void ResolveSharedTilePalette()
    {
        _tilePalette = RougeTowerDefenseTilePalette.Shared;
        if (_tilePalette == null)
        {
            _tilePalette = AssetDatabase.LoadAssetAtPath<RougeTowerDefenseTilePalette>(
                "Assets/Resources/Config/TowerDefenseTilePalette.asset");
        }
        _serializedTilePalette = _tilePalette != null
            ? new SerializedObject(_tilePalette)
            : null;
    }

    private void SaveSharedTilePalette()
    {
        if (_tilePalette == null) return;
        EditorUtility.SetDirty(_tilePalette);
        AssetDatabase.SaveAssetIfDirty(_tilePalette);
        _serializedTilePalette?.UpdateIfRequiredOrScript();
        _saveStatus = "共享地块调色板已保存；所有地图将使用相同定义。";
        Repaint();
    }

    private void CreateMapAsset()
    {
        if (!ConfirmCanReplaceDraft()) return;
        string path = EditorUtility.SaveFilePanelInProject(
            "创建塔防地图", "TowerDefenseMap", "asset", "请选择地图资源的保存位置。" );
        if (string.IsNullOrEmpty(path)) return;
        RougeTowerDefenseMap created = CreateInstance<RougeTowerDefenseMap>();
        created.InitializeDefaults();
        AssetDatabase.CreateAsset(created, path);
        AssetDatabase.SaveAssetIfDirty(created);
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
        serializedLoader.FindProperty("map").objectReferenceValue = _mapAsset;
        serializedLoader.ApplyModifiedProperties();
        _loader = loader;
        EditorUtility.SetDirty(loader);
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
        Selection.activeGameObject = loader.gameObject;
    }
}
