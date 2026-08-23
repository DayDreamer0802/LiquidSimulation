using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class RougeTowerDefenseBalanceEditor : EditorWindow
{
    private enum BalanceTab
    {
        Towers,
        Enemies,
        Bosses
    }

    private static readonly string[] TabLabels = { "Towers / Levels", "Enemies", "Bosses" };
    private static readonly Dictionary<string, Texture2D> ResourceTextureCache =
        new Dictionary<string, Texture2D>();

    private RougeTowerDefenseBalanceProfile _profile;
    private SerializedObject _serializedProfile;
    [SerializeField] private BalanceTab _selectedTab;
    [SerializeField] private Vector2 _towerScroll;
    [SerializeField] private Vector2 _enemyScroll;
    [SerializeField] private Vector2 _bossScroll;
    [SerializeField] private int _selectedTowerIndex;
    private string _status;
    private bool _hasUnsavedChanges;
    private int _enemyPreviewLevel = 1;
    private int _enemyPreviewHash = int.MinValue;
    private string _enemyPreviewSummary = string.Empty;
    private string _enemyPreviewTable = string.Empty;

    [MenuItem("Rouge/Tower Defense/Balance")]
    internal static void Open()
    {
        RougeTowerDefenseBalanceEditor window = GetWindow<RougeTowerDefenseBalanceEditor>();
        window.titleContent = new GUIContent("TD Balance");
        window.minSize = new Vector2(900f, 520f);
        window.Show();
    }

    [UnityEditor.Callbacks.OnOpenAsset(1)]
    private static bool OpenBalanceJson(int instanceId, int line)
    {
        Object asset = EditorUtility.InstanceIDToObject(instanceId);
        if (asset == null || AssetDatabase.GetAssetPath(asset) != RougeTowerDefenseBalanceJson.AssetPath) return false;
        Open();
        return true;
    }

    private void OnEnable()
    {
        CreateProfile();
        LoadJson(false);
    }

    private void OnDisable()
    {
        if (_profile != null) DestroyImmediate(_profile);
        _profile = null;
        _serializedProfile = null;
    }

    private void CreateProfile()
    {
        if (_profile != null) DestroyImmediate(_profile);
        _profile = CreateInstance<RougeTowerDefenseBalanceProfile>();
        // The window owns and destroys this temporary object. DontSaveInEditor makes
        // Unity's dynamic IMGUI font atlas hit an internal persistence assertion.
        _profile.hideFlags = HideFlags.HideInHierarchy;
        _profile.EnsureDefaults();
        _serializedProfile = new SerializedObject(_profile);
        InvalidatePreviewCaches();
    }

    private void OnGUI()
    {
        if (_profile == null || _serializedProfile == null) CreateProfile();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Tower Defense Balance", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Edit values below, then click Save JSON. The game loads this file when Play starts.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reload JSON", GUILayout.Height(32f))) LoadJson(true);
            GUI.backgroundColor = _hasUnsavedChanges ? new Color(1f, 0.72f, 0.18f) : new Color(0.35f, 0.9f, 0.5f);
            if (GUILayout.Button("Save JSON", GUILayout.Height(32f))) ExportJson();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Restore Defaults", GUILayout.Height(32f))) ResetDefaults();
            if (GUILayout.Button("Locate JSON", GUILayout.Height(32f))) PingJson();
        }
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Runtime path", RougeTowerDefenseBalanceJson.AssetPath);
        if (_hasUnsavedChanges) EditorGUILayout.HelpBox("Unsaved changes.", MessageType.Warning);
        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);

        EditorGUILayout.Space(6f);
        BalanceTab nextTab = (BalanceTab)GUILayout.Toolbar((int)_selectedTab, TabLabels,
            GUILayout.Height(30f));
        if (nextTab != _selectedTab)
        {
            _selectedTab = nextTab;
            GUI.FocusControl(null);
        }

        _serializedProfile.Update();
        EditorGUI.BeginChangeCheck();
        if (_selectedTab == BalanceTab.Towers)
        {
            DrawSelectedTab();
        }
        else
        {
            Vector2 scroll = GetSelectedScroll();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawSelectedTab();
            EditorGUILayout.EndScrollView();
            SetSelectedScroll(scroll);
        }
        if (EditorGUI.EndChangeCheck())
        {
            _serializedProfile.ApplyModifiedProperties();
            _hasUnsavedChanges = true;
            _status = "Values changed. Click Save JSON to update the runtime configuration.";
        }
        else
        {
            _serializedProfile.ApplyModifiedProperties();
        }
    }

    private void DrawSelectedTab()
    {
        EditorGUILayout.Space(8f);
        switch (_selectedTab)
        {
            case BalanceTab.Towers:
                DrawTowerBalance(_serializedProfile.FindProperty("towerBalance"));
                break;
            case BalanceTab.Enemies:
                DrawEnemyBalance(_serializedProfile.FindProperty("enemyBalance"));
                break;
            case BalanceTab.Bosses:
                DrawBossBalances(_serializedProfile.FindProperty("bossBalances"));
                break;
        }
    }

    private Vector2 GetSelectedScroll()
    {
        switch (_selectedTab)
        {
            case BalanceTab.Enemies: return _enemyScroll;
            case BalanceTab.Bosses: return _bossScroll;
            default: return _towerScroll;
        }
    }

    private void SetSelectedScroll(Vector2 value)
    {
        switch (_selectedTab)
        {
            case BalanceTab.Enemies:
                _enemyScroll = value;
                break;
            case BalanceTab.Bosses:
                _bossScroll = value;
                break;
            default:
                _towerScroll = value;
                break;
        }
    }

    private static void DrawBossBalances(SerializedProperty bosses)
    {
        EditorGUILayout.HelpBox(
            "Boss IDs are stable integers used by level schedules. Keep IDs unique; level spawn time and victory behavior are configured in the Map Painter.",
            MessageType.Info);
        for (int i = 0; i < bosses.arraySize; i++)
        {
            SerializedProperty boss = bosses.GetArrayElementAtIndex(i);
            SerializedProperty id = boss.FindPropertyRelative("bossId");
            SerializedProperty displayName = boss.FindPropertyRelative("displayName");
            string title = $"Boss {id.intValue}: {displayName.stringValue}";
            boss.isExpanded = EditorGUILayout.Foldout(boss.isExpanded, title, true, EditorStyles.foldoutHeader);
            if (!boss.isExpanded) continue;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(id, new GUIContent("Boss ID (integer)"));
                EditorGUILayout.PropertyField(displayName);
                EditorGUILayout.PropertyField(boss.FindPropertyRelative("targetTravelTimeSeconds"),
                    new GUIContent("Target Travel Time (seconds)"));
                DrawRemainingBossFields(boss, "bossId", "displayName", "spawnTimeSeconds",
                    "targetArrivalTimeSeconds", "targetTravelTimeSeconds");
                if (GUILayout.Button("Remove Boss"))
                {
                    bosses.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            EditorGUILayout.Space(4f);
        }
        if (GUILayout.Button("Add Boss Configuration"))
        {
            int index = bosses.arraySize;
            bosses.InsertArrayElementAtIndex(index);
            SerializedProperty added = bosses.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("bossId").intValue = GetNextBossId(bosses, index);
            added.FindPropertyRelative("displayName").stringValue = $"Boss {added.FindPropertyRelative("bossId").intValue}";
            added.isExpanded = true;
        }
    }

    private static void DrawRemainingBossFields(SerializedProperty boss, params string[] excluded)
    {
        SerializedProperty iterator = boss.Copy();
        SerializedProperty end = iterator.GetEndProperty();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
        {
            enterChildren = false;
            bool skip = false;
            for (int i = 0; i < excluded.Length; i++)
            {
                if (iterator.name != excluded[i]) continue;
                skip = true;
                break;
            }
            if (!skip) EditorGUILayout.PropertyField(iterator, true);
        }
    }

    private static int GetNextBossId(SerializedProperty bosses, int excludedIndex)
    {
        int candidate = 0;
        while (true)
        {
            bool used = false;
            for (int i = 0; i < bosses.arraySize; i++)
            {
                if (i == excludedIndex) continue;
                if (bosses.GetArrayElementAtIndex(i).FindPropertyRelative("bossId").intValue != candidate) continue;
                used = true;
                break;
            }
            if (!used) return candidate;
            candidate++;
        }
    }

    private void DrawTowerBalance(SerializedProperty balance)
    {
        SerializedProperty towers = balance.FindPropertyRelative("towers");
        if (towers == null || towers.arraySize == 0)
        {
            EditorGUILayout.HelpBox("No tower configurations are available.", MessageType.Warning);
            return;
        }

        _selectedTowerIndex = Mathf.Clamp(_selectedTowerIndex, 0, towers.arraySize - 1);
        using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
        {
            DrawTowerSelector(towers);
            _towerScroll = EditorGUILayout.BeginScrollView(_towerScroll, true, true,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawSelectedTowerConfiguration(balance, towers.GetArrayElementAtIndex(_selectedTowerIndex));
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawTowerSelector(SerializedProperty towers)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,
            GUILayout.Width(180f), GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Tower", EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);
            for (int i = 0; i < towers.arraySize; i++)
            {
                SerializedProperty tower = towers.GetArrayElementAtIndex(i);
                SerializedProperty typeProperty = tower.FindPropertyRelative("towerType");
                RougeTowerType type = (RougeTowerType)typeProperty.enumValueIndex;
                bool selected = i == _selectedTowerIndex;
                Color previousBackground = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.35f, 0.72f, 1f);
                if (GUILayout.Button(ObjectNames.NicifyVariableName(type.ToString()),
                    selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                    GUILayout.Height(34f)))
                {
                    _selectedTowerIndex = i;
                    _towerScroll = Vector2.zero;
                    GUI.FocusControl(null);
                    GUI.changed = false;
                }
                GUI.backgroundColor = previousBackground;
            }
            GUILayout.FlexibleSpace();
        }
    }

    private static void DrawSelectedTowerConfiguration(SerializedProperty balance,
        SerializedProperty tower)
    {
        SerializedProperty typeProperty = tower.FindPropertyRelative("towerType");
        RougeTowerType type = (RougeTowerType)typeProperty.enumValueIndex;
        string towerName = ObjectNames.NicifyVariableName(type.ToString());

        EditorGUILayout.LabelField(towerName + " Configuration", EditorStyles.largeLabel);
        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(700f)))
        {
            EditorGUILayout.LabelField("Global", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(balance.FindPropertyRelative("sellRefundMultiplier"),
                new GUIContent("Sell Refund %"));
        }
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(700f)))
        {
            EditorGUILayout.LabelField("Tower Settings", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(typeProperty, new GUIContent("Tower Type"));
            EditorGUILayout.PropertyField(tower.FindPropertyRelative("placementRadius"),
                new GUIContent("Placement Radius"));
            EditorGUILayout.PropertyField(tower.FindPropertyRelative("footprintWidth"),
                new GUIContent("Footprint Width (Micro Cells)"));
            EditorGUILayout.PropertyField(tower.FindPropertyRelative("footprintHeight"),
                new GUIContent("Footprint Height (Micro Cells)"));
        }

        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("Level Parameters", EditorStyles.boldLabel);
        SerializedProperty levels = tower.FindPropertyRelative("levels");
        DrawLevelTableHeader(levels.arraySize);
        DrawTowerLevelRows(levels, type);
        EditorGUILayout.Space(10f);
    }

    private static void DrawLevelTableHeader(int levelCount)
    {
        GUIStyle centeredHeader = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.MinWidth(700f)))
        {
            GUILayout.Label("Parameter", EditorStyles.miniBoldLabel, GUILayout.Width(210f));
            for (int i = 0; i < levelCount; i++)
                GUILayout.Label($"Lv {i + 1}", centeredHeader, GUILayout.Width(96f));
        }
    }

    private static void DrawTowerLevelRows(SerializedProperty levels, RougeTowerType type)
    {
        DrawLevelRow(levels, "goldCost", "Gold Cost");
        DrawLevelRow(levels, "damage", type == RougeTowerType.OrbitSphere ? "Damage / Tick" : "Damage");
        DrawLevelRow(levels, "attackInterval", type == RougeTowerType.OrbitSphere
            ? "Cooldown After Return" : "Attack Interval");
        DrawLevelRow(levels, "attackRange", type == RougeTowerType.OrbitSphere
            ? "Maximum Orbit Distance" : "Attack Range");
        switch (type)
        {
            case RougeTowerType.Ice:
                DrawLevelRow(levels, "effectPercent", "Slow %");
                DrawLevelRow(levels, "effectDuration", "Slow Duration");
                break;
            case RougeTowerType.MachineGun:
                DrawLevelRow(levels, "targetCount", "Pellet Count");
                break;
            case RougeTowerType.Laser:
                DrawLevelRow(levels, "targetCount", "Target Count");
                break;
            case RougeTowerType.Cannon:
                DrawLevelRow(levels, "projectileCount", "Shell Count");
                DrawLevelRow(levels, "aoeRadius", "Explosion Radius");
                break;
            case RougeTowerType.Flame:
                DrawLevelRow(levels, "projectileCount", "Fireball Count");
                DrawLevelRow(levels, "aoeRadius", "Fire Radius");
                DrawLevelRow(levels, "effectDuration", "Fire Duration");
                DrawLevelRow(levels, "tickInterval", "Damage Interval");
                break;
            case RougeTowerType.OrbitSphere:
                DrawLevelRow(levels, "projectileCount", "Sphere Count");
                DrawLevelRow(levels, "orbitSphereRadius", "Sphere Radius");
                DrawLevelRow(levels, "orbitRadialSpeed", "Radial Move Speed");
                DrawLevelRow(levels, "orbitAngularSpeed", "Rotation Speed (deg/s)");
                DrawLevelRow(levels, "orbitOuterHoldDuration", "Outer Hold Duration (seconds)");
                DrawLevelRow(levels, "tickInterval", "Damage Interval");
                break;
            case RougeTowerType.RocketBarrage:
                DrawLevelRow(levels, "projectileCount", "Missiles / Salvo");
                DrawLevelRow(levels, "aoeRadius", "Impact AOE Radius");
                DrawLevelRow(levels, "projectileInterval", "Salvo Shot Interval");
                DrawLevelRow(levels, "projectileFlightDuration", "Missile Flight Duration");
                DrawLevelRow(levels, "brownianStrength", "Brownian Drift Strength");
                break;
        }
    }

    private static void DrawLevelRow(SerializedProperty levels, string propertyName, string label)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox, GUILayout.MinWidth(700f)))
        {
            GUILayout.Label(label, GUILayout.Width(202f));
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty property = levels.GetArrayElementAtIndex(i)
                    .FindPropertyRelative(propertyName);
                if (property != null)
                    EditorGUILayout.PropertyField(property, GUIContent.none, GUILayout.Width(96f));
                else
                    GUILayout.Space(100f);
            }
        }
    }

    private void DrawEnemyBalance(SerializedProperty balance)
    {
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("growthInterval"),
            new GUIContent("Enemy Level Interval (seconds)"));
        SerializedProperty healthCurve = balance.FindPropertyRelative("healthMultiplierByLevel");
        SerializedProperty speedCurve = balance.FindPropertyRelative("speedMultiplierByLevel");
        SerializedProperty spawnSpeedCurve = balance.FindPropertyRelative("spawnSpeedMultiplierByLevel");
        SerializedProperty elitePermilleCurve = balance.FindPropertyRelative("eliteChancePermilleByLevel");
        DrawEnemyMultiplierCurve(healthCurve, "HP Multiplier by Level",
            288f);
        DrawEnemyMultiplierCurve(speedCurve, "Speed Multiplier by Level",
            2f);
        DrawEnemyMultiplierCurve(spawnSpeedCurve, "Spawn Speed Multiplier by Level", 3f);
        DrawEnemyMultiplierCurve(elitePermilleCurve, "Elite Chance Permille by Level", 5f);
        DrawEnemyLevelPreview(balance.FindPropertyRelative("growthInterval"), healthCurve, speedCurve,
            spawnSpeedCurve, elitePermilleCurve);
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("eliteHealthMultiplier"));
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("eliteSpeedMultiplier"));
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("eliteSizeMultiplier"));

        SerializedProperty types = balance.FindPropertyRelative("enemyTypes");
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Enemy Types (available to spawn sequences)", EditorStyles.boldLabel);
        for (int i = 0; i < types.arraySize; i++)
        {
            SerializedProperty enemy = types.GetArrayElementAtIndex(i);
            SerializedProperty name = enemy.FindPropertyRelative("displayName");
            enemy.isExpanded = EditorGUILayout.Foldout(enemy.isExpanded,
                $"Type {i}: {name.stringValue}", true, EditorStyles.foldoutHeader);
            if (!enemy.isExpanded) continue;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(name);
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("killGold"),
                    new GUIContent("Kill Gold"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("eliteKillGold"),
                    new GUIContent("Elite Kill Gold"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("baseHealth"), new GUIContent("Health"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("healthGrowthMultiplier"),
                    new GUIContent("HP Growth Multiplier",
                        "1 uses the global HP curve unchanged. Values above 1 grow faster; level-1 base health stays unchanged."));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("baseSpeed"), new GUIContent("Move Speed"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("size"), new GUIContent("Size"));
                DrawResourceTextureField(enemy.FindPropertyRelative("spriteResourcePath"), "Enemy Sprite Sheet");
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("spriteSheetColumns"), new GUIContent("Sprite Columns"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("spriteSheetRows"), new GUIContent("Sprite Rows"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("spriteAnimationFps"), new GUIContent("Animation FPS"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("spriteDeathFrameCount"), new GUIContent("Death Animation Frames"));
                EditorGUILayout.HelpBox("The final configured cells are reserved for death; all preceding cells loop as movement.", MessageType.Info);
            }
        }
    }

    private static void DrawEnemyMultiplierCurve(SerializedProperty curveProperty, string label,
        float fallbackMaximum)
    {
        if (curveProperty == null) return;
        AnimationCurve curve = curveProperty.animationCurveValue;
        if (curve == null || curve.length == 0)
            curve = AnimationCurve.Linear(1f, 1f, RougeEnemyBalanceConfig.MaximumEnemyLevel, fallbackMaximum);

        EditorGUILayout.Space(3f);
        float level100Value = Mathf.Max(0.01f,
            EditorGUILayout.FloatField(label + " - Level 100", curve.Evaluate(
                RougeEnemyBalanceConfig.MaximumEnemyLevel)));
        if (!Mathf.Approximately(level100Value,
                curve.Evaluate(RougeEnemyBalanceConfig.MaximumEnemyLevel)))
        {
            curve = SetCurveKeyValue(curve, RougeEnemyBalanceConfig.MaximumEnemyLevel, level100Value);
            curveProperty.animationCurveValue = curve;
        }
        EditorGUI.BeginChangeCheck();
        AnimationCurve edited = EditorGUILayout.CurveField(new GUIContent(label), curve,
            GUILayout.Height(72f));
        if (EditorGUI.EndChangeCheck()) curveProperty.animationCurveValue = edited;
    }

    private static AnimationCurve SetCurveKeyValue(AnimationCurve curve, float level, float value)
    {
        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            if (Mathf.Abs(keys[i].time - level) > 0.001f) continue;
            Keyframe key = keys[i];
            key.value = value;
            curve.MoveKey(i, key);
            return curve;
        }
        curve.AddKey(new Keyframe(level, value));
        return curve;
    }

    private void DrawEnemyLevelPreview(SerializedProperty intervalProperty,
        SerializedProperty healthCurveProperty, SerializedProperty speedCurveProperty,
        SerializedProperty spawnSpeedCurveProperty, SerializedProperty elitePermilleCurveProperty)
    {
        if (intervalProperty == null || healthCurveProperty == null || speedCurveProperty == null ||
            spawnSpeedCurveProperty == null || elitePermilleCurveProperty == null) return;
        _enemyPreviewLevel = EditorGUILayout.IntSlider("Preview Enemy Level", _enemyPreviewLevel, 1,
            RougeEnemyBalanceConfig.MaximumEnemyLevel);
        AnimationCurve healthCurve = healthCurveProperty.animationCurveValue;
        AnimationCurve speedCurve = speedCurveProperty.animationCurveValue;
        AnimationCurve spawnSpeedCurve = spawnSpeedCurveProperty.animationCurveValue;
        AnimationCurve elitePermilleCurve = elitePermilleCurveProperty.animationCurveValue;
        float interval = Mathf.Max(1f, intervalProperty.floatValue);
        int previewHash = ComputeEnemyPreviewHash(interval, healthCurveProperty, speedCurveProperty,
            spawnSpeedCurveProperty, elitePermilleCurveProperty);
        if (previewHash != _enemyPreviewHash)
        {
            RebuildEnemyPreview(interval, healthCurve, speedCurve, spawnSpeedCurve,
                elitePermilleCurve);
            _enemyPreviewHash = previewHash;
        }
        EditorGUILayout.HelpBox(_enemyPreviewSummary, MessageType.Info);
        EditorGUILayout.LabelField("Level multiplier preview", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox(_enemyPreviewTable, MessageType.None);
    }

    private int ComputeEnemyPreviewHash(float interval, SerializedProperty healthCurve,
        SerializedProperty speedCurve, SerializedProperty spawnSpeedCurve,
        SerializedProperty elitePermilleCurve)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + _enemyPreviewLevel;
            hash = hash * 31 + interval.GetHashCode();
            hash = hash * 31 + healthCurve.contentHash.GetHashCode();
            hash = hash * 31 + speedCurve.contentHash.GetHashCode();
            hash = hash * 31 + spawnSpeedCurve.contentHash.GetHashCode();
            hash = hash * 31 + elitePermilleCurve.contentHash.GetHashCode();
            return hash;
        }
    }

    private void RebuildEnemyPreview(float interval, AnimationCurve healthCurve,
        AnimationCurve speedCurve, AnimationCurve spawnSpeedCurve, AnimationCurve elitePermilleCurve)
    {
        float reachedSeconds = (_enemyPreviewLevel - 1) * interval;
        float healthMultiplier = Mathf.Max(0.01f, healthCurve.Evaluate(_enemyPreviewLevel));
        float speedMultiplier = Mathf.Max(0.01f, speedCurve.Evaluate(_enemyPreviewLevel));
        float spawnSpeedMultiplier = Mathf.Max(0.01f, spawnSpeedCurve.Evaluate(_enemyPreviewLevel));
        float elitePermille = Mathf.Max(0f, elitePermilleCurve.Evaluate(_enemyPreviewLevel));
        _enemyPreviewSummary =
            $"Level {_enemyPreviewLevel}  |  reached at {FormatEnemyLevelTime(reachedSeconds)}  |  " +
            $"HP x{healthMultiplier:0.##}  |  Move x{speedMultiplier:0.###}  |  " +
            $"Spawn x{spawnSpeedMultiplier:0.###}  |  Elite {elitePermille:0.###}‰";

        StringBuilder preview = new StringBuilder(512);
        for (int level = 1; level <= RougeEnemyBalanceConfig.MaximumEnemyLevel;
             level += level == 1 ? 9 : 10)
        {
            if (preview.Length > 0) preview.AppendLine();
            float hp = Mathf.Max(0.01f, healthCurve.Evaluate(level));
            float speed = Mathf.Max(0.01f, speedCurve.Evaluate(level));
            float spawnSpeed = Mathf.Max(0.01f, spawnSpeedCurve.Evaluate(level));
            float eliteAtLevel = Mathf.Max(0f, elitePermilleCurve.Evaluate(level));
            float levelSeconds = (level - 1) * interval;
            preview.Append($"Lv {level,3}    {FormatEnemyLevelTime(levelSeconds),16}    " +
                $"HP x{hp,7:0.##}    Move x{speed,6:0.###}    " +
                $"Spawn x{spawnSpeed,6:0.###}    Elite {eliteAtLevel,6:0.###}‰");
        }
        _enemyPreviewTable = preview.ToString();
    }

    private static string FormatEnemyLevelTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int minutes = totalSeconds / 60;
        int secondPart = totalSeconds % 60;
        return $"{totalSeconds}s / {minutes:00}:{secondPart:00}";
    }

    private static void DrawResourceTextureField(SerializedProperty pathProperty, string label)
    {
        string previousPath = pathProperty.stringValue;
        Texture2D current = FindResourceTexture(pathProperty.stringValue);
        Texture2D next = (Texture2D)EditorGUILayout.ObjectField(label, current, typeof(Texture2D), false);
        if (next == current) return;
        if (next == null)
        {
            ResourceTextureCache.Remove(previousPath);
            pathProperty.stringValue = string.Empty;
            return;
        }
        string assetPath = AssetDatabase.GetAssetPath(next).Replace('\\', '/');
        const string resourcesMarker = "/Resources/";
        int marker = assetPath.IndexOf(resourcesMarker, System.StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            EditorUtility.DisplayDialog("Texture must be in Resources",
                "Move the texture under an Assets/.../Resources folder before assigning it.", "OK");
            return;
        }
        string resourcePath = assetPath.Substring(marker + resourcesMarker.Length);
        pathProperty.stringValue = resourcePath.Substring(0,
            resourcePath.Length - System.IO.Path.GetExtension(resourcePath).Length);
        ResourceTextureCache[pathProperty.stringValue] = next;
    }

    private static Texture2D FindResourceTexture(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;
        if (ResourceTextureCache.TryGetValue(resourcePath, out Texture2D cached)) return cached;
        Texture2D loaded = Resources.Load<Texture2D>(resourcePath);
        ResourceTextureCache[resourcePath] = loaded;
        return loaded;
    }

    private void InvalidatePreviewCaches()
    {
        _enemyPreviewHash = int.MinValue;
        _enemyPreviewSummary = string.Empty;
        _enemyPreviewTable = string.Empty;
        ResourceTextureCache.Clear();
    }

    private void OnProjectChange()
    {
        ResourceTextureCache.Clear();
        Repaint();
    }

    private void LoadJson(bool reportResult)
    {
        TextAsset jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(RougeTowerDefenseBalanceJson.AssetPath);
        if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text))
        {
            _profile.EnsureDefaults();
            _serializedProfile = new SerializedObject(_profile);
            if (reportResult) _status = "JSON not found. Code defaults are shown; click Save JSON to create it.";
            Repaint();
            return;
        }

        try
        {
            RougeTowerDefenseBalanceJsonData data = JsonUtility.FromJson<RougeTowerDefenseBalanceJsonData>(jsonAsset.text);
            _profile.Apply(data);
            _serializedProfile = new SerializedObject(_profile);
            InvalidatePreviewCaches();
            _status = "Loaded: " + RougeTowerDefenseBalanceJson.AssetPath;
            _hasUnsavedChanges = false;
        }
        catch (System.Exception exception)
        {
            _status = "Load failed: " + exception.Message;
        }
        Repaint();
    }

    private void ExportJson()
    {
        _serializedProfile.ApplyModifiedProperties();
        _profile.EnsureDefaults();
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string absolutePath = Path.Combine(projectRoot, RougeTowerDefenseBalanceJson.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        string directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string json = JsonUtility.ToJson(_profile.ToJsonData(), true);
        File.WriteAllText(absolutePath, json, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(RougeTowerDefenseBalanceJson.AssetPath, ImportAssetOptions.ForceUpdate);
        _hasUnsavedChanges = false;
        _status = "Saved. The file will be loaded the next time Play starts.";
        Repaint();
    }

    private void ResetDefaults()
    {
        CreateProfile();
        _status = "Code defaults restored in the editor; JSON has not been overwritten yet.";
        _hasUnsavedChanges = true;
        Repaint();
    }

    private void PingJson()
    {
        Object jsonAsset = AssetDatabase.LoadAssetAtPath<Object>(RougeTowerDefenseBalanceJson.AssetPath);
        if (jsonAsset != null)
        {
            EditorGUIUtility.PingObject(jsonAsset);
            _status = "Located in Project. Continue editing values in the TD Balance window.";
        }
        else
        {
            _status = "JSON has not been created yet.";
        }
    }

}
