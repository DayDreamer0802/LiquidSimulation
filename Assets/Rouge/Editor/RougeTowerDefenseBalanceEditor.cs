using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class RougeTowerDefenseBalanceEditor : EditorWindow
{
    private RougeTowerDefenseBalanceProfile _profile;
    private SerializedObject _serializedProfile;
    private Vector2 _scroll;
    private string _status;
    private bool _hasUnsavedChanges;

    [MenuItem("Tools/Rouge/Tower Defense Balance")]
    internal static void Open()
    {
        RougeTowerDefenseBalanceEditor window = GetWindow<RougeTowerDefenseBalanceEditor>();
        window.titleContent = new GUIContent("TD Balance");
        window.minSize = new Vector2(620f, 520f);
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
        if (GUILayout.Button("Edit Camera Bounds (cyan Scene rectangle)", GUILayout.Height(28f))) SelectCameraBounds();

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Runtime path", RougeTowerDefenseBalanceJson.AssetPath);
        if (_hasUnsavedChanges) EditorGUILayout.HelpBox("Unsaved changes.", MessageType.Warning);
        if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);

        _serializedProfile.Update();
        EditorGUI.BeginChangeCheck();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawSection("TOWERS / LEVELS", "towerBalance");
        DrawSection("ENEMIES", "enemyBalance");
        DrawSection("BOSS", "bossBalance");
        DrawSection("TACTICAL SKILLS (4 slots, 3 enabled)", "tacticalSkillBalance");
        EditorGUILayout.EndScrollView();
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

    private void DrawSection(string title, string propertyName)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        SerializedProperty property = _serializedProfile.FindProperty(propertyName);
        if (property != null)
        {
            if (propertyName == "towerBalance")
            {
                DrawTowerBalance(property);
                return;
            }
            if (propertyName == "enemyBalance")
            {
                DrawEnemyBalance(property);
                return;
            }
            property.isExpanded = true;
            EditorGUILayout.PropertyField(property, true);
        }
    }

    private static void DrawTowerBalance(SerializedProperty balance)
    {
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("sellRefundMultiplier"),
            new GUIContent("Sell Refund %"));
        SerializedProperty towers = balance.FindPropertyRelative("towers");
        for (int i = 0; i < towers.arraySize; i++)
        {
            SerializedProperty tower = towers.GetArrayElementAtIndex(i);
            SerializedProperty typeProperty = tower.FindPropertyRelative("towerType");
            RougeTowerType type = (RougeTowerType)typeProperty.enumValueIndex;
            tower.isExpanded = EditorGUILayout.Foldout(tower.isExpanded,
                ObjectNames.NicifyVariableName(type.ToString()), true, EditorStyles.foldoutHeader);
            if (!tower.isExpanded) continue;
            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(typeProperty);
                EditorGUILayout.PropertyField(tower.FindPropertyRelative("placementRadius"));
                EditorGUILayout.PropertyField(tower.FindPropertyRelative("purchaseCost"));
                SerializedProperty levels = tower.FindPropertyRelative("levels");
                for (int levelIndex = 0; levelIndex < levels.arraySize; levelIndex++)
                {
                    SerializedProperty level = levels.GetArrayElementAtIndex(levelIndex);
                    level.isExpanded = EditorGUILayout.Foldout(level.isExpanded,
                        $"Level {levelIndex + 1}", true);
                    if (!level.isExpanded) continue;
                    using (new EditorGUI.IndentLevelScope()) DrawTowerLevel(level, type);
                }
            }
            EditorGUILayout.Space(4f);
        }
    }

    private static void DrawTowerLevel(SerializedProperty level, RougeTowerType type)
    {
        DrawLevelField(level, "damage", type == RougeTowerType.OrbitSphere ? "Damage / Tick" : "Damage");
        DrawLevelField(level, "attackInterval", type == RougeTowerType.OrbitSphere
            ? "Cooldown After Return" : "Attack Interval");
        DrawLevelField(level, "attackRange", type == RougeTowerType.OrbitSphere
            ? "Maximum Orbit Distance" : "Attack Range");
        switch (type)
        {
            case RougeTowerType.Ice:
                DrawLevelField(level, "effectPercent", "Slow %");
                DrawLevelField(level, "effectDuration", "Slow Duration");
                break;
            case RougeTowerType.MachineGun:
            case RougeTowerType.Laser:
                DrawLevelField(level, "targetCount", "Target Count");
                break;
            case RougeTowerType.Cannon:
                DrawLevelField(level, "projectileCount", "Projectile Count");
                DrawLevelField(level, "aoeRadius", "Explosion Radius");
                break;
            case RougeTowerType.Flame:
                DrawLevelField(level, "aoeRadius", "Fire Radius");
                DrawLevelField(level, "effectDuration", "Fire Duration");
                DrawLevelField(level, "tickInterval", "Damage Interval");
                break;
            case RougeTowerType.OrbitSphere:
                DrawLevelField(level, "projectileCount", "Sphere Count");
                DrawLevelField(level, "orbitSphereRadius", "Sphere Radius");
                DrawLevelField(level, "orbitRadialSpeed", "Radial Move Speed");
                DrawLevelField(level, "orbitAngularSpeed", "Rotation Speed (deg/s)");
                DrawLevelField(level, "tickInterval", "Damage Interval");
                break;
        }
    }

    private static void DrawLevelField(SerializedProperty parent, string name, string label)
    {
        SerializedProperty property = parent.FindPropertyRelative(name);
        if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
    }

    private static void DrawEnemyBalance(SerializedProperty balance)
    {
        string[] commonFields =
        {
            "normalKillGold", "eliteKillGold"
        };
        for (int i = 0; i < commonFields.Length; i++)
            EditorGUILayout.PropertyField(balance.FindPropertyRelative(commonFields[i]));

        EditorGUILayout.PropertyField(balance.FindPropertyRelative("growthInterval"),
            new GUIContent("Enemy Level Interval (seconds)"));
        DrawGrowthPercentField(balance.FindPropertyRelative("healthGrowthMultiplier"), "Health / Level (%)");
        DrawGrowthPercentField(balance.FindPropertyRelative("speedGrowthMultiplier"), "Speed / Level (%)");
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("eliteHealthMultiplier"));
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("eliteSpeedMultiplier"));
        EditorGUILayout.PropertyField(balance.FindPropertyRelative("eliteSizeMultiplier"));

        SerializedProperty types = balance.FindPropertyRelative("enemyTypes");
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Enemy Types (available to weighted spawn mixes)", EditorStyles.boldLabel);
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
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("baseHealth"), new GUIContent("Health"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("baseSpeed"), new GUIContent("Move Speed"));
                EditorGUILayout.PropertyField(enemy.FindPropertyRelative("size"), new GUIContent("Size"));
                DrawResourceTextureField(enemy.FindPropertyRelative("spriteResourcePath"), "Enemy Sprite Sheet");
            }
        }
    }

    private static void DrawGrowthPercentField(SerializedProperty multiplierProperty, string label)
    {
        float percent = (Mathf.Max(1f, multiplierProperty.floatValue) - 1f) * 100f;
        percent = Mathf.Max(0f, EditorGUILayout.FloatField(label, percent));
        multiplierProperty.floatValue = 1f + percent * 0.01f;
    }

    private static void DrawResourceTextureField(SerializedProperty pathProperty, string label)
    {
        Texture2D current = FindResourceTexture(pathProperty.stringValue);
        Texture2D next = (Texture2D)EditorGUILayout.ObjectField(label, current, typeof(Texture2D), false);
        if (next == current) return;
        if (next == null)
        {
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
    }

    private static Texture2D FindResourceTexture(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;
        // This field is redrawn frequently. AssetDatabase.FindAssets creates a native
        // hierarchy iterator and can crash Unity while the asset database is refreshing.
        // The serialized value is already a Resources-relative path, so load it directly.
        return Resources.Load<Texture2D>(resourcePath);
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

    private static void SelectCameraBounds()
    {
        RougeCameraBounds bounds = Object.FindFirstObjectByType<RougeCameraBounds>();
        if (bounds == null)
        {
            GameObject go = new GameObject("Camera Movement Bounds");
            Undo.RegisterCreatedObjectUndo(go, "Create Camera Movement Bounds");
            bounds = Undo.AddComponent<RougeCameraBounds>(go);
        }
        RougeCameraFollow follow = Object.FindFirstObjectByType<RougeCameraFollow>();
        if (follow != null && follow.movementBounds != bounds)
        {
            Undo.RecordObject(follow, "Assign Camera Movement Bounds");
            follow.movementBounds = bounds;
            EditorUtility.SetDirty(follow);
        }
        Selection.activeGameObject = bounds.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}

[CustomEditor(typeof(TextAsset), true)]
public sealed class RougeTowerDefenseJsonInspector : Editor
{
    private string _editableJson;
    private Vector2 _scroll;
    private bool _changed;

    private bool IsBalanceJson =>
        AssetDatabase.GetAssetPath(target) == RougeTowerDefenseBalanceJson.AssetPath;

    private void OnEnable()
    {
        if (IsBalanceJson) Reload();
    }

    public override void OnInspectorGUI()
    {
        if (!IsBalanceJson)
        {
            DrawDefaultInspector();
            return;
        }

        EditorGUILayout.HelpBox(
            "Editable runtime configuration. Edit raw JSON here or use the structured editor.",
            MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open Structured Editor", GUILayout.Height(30f))) RougeTowerDefenseBalanceEditor.Open();
            GUI.backgroundColor = _changed ? new Color(1f, 0.72f, 0.18f) : new Color(0.35f, 0.9f, 0.5f);
            if (GUILayout.Button("Save JSON", GUILayout.Height(30f))) Save();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Reload", GUILayout.Height(30f))) Reload();
        }

        if (_changed) EditorGUILayout.HelpBox("Unsaved changes.", MessageType.Warning);
        EditorGUI.BeginChangeCheck();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        string next = EditorGUILayout.TextArea(_editableJson ?? string.Empty,
            GUILayout.ExpandHeight(true), GUILayout.MinHeight(460f));
        EditorGUILayout.EndScrollView();
        if (EditorGUI.EndChangeCheck())
        {
            _editableJson = next;
            _changed = true;
        }
    }

    private void Reload()
    {
        TextAsset textAsset = target as TextAsset;
        _editableJson = textAsset != null ? textAsset.text : string.Empty;
        _changed = false;
        Repaint();
    }

    private void Save()
    {
        try
        {
            RougeTowerDefenseBalanceJsonData data =
                JsonUtility.FromJson<RougeTowerDefenseBalanceJsonData>(_editableJson);
            if (data == null) throw new System.FormatException("JSON is empty or invalid.");
            data.EnsureDefaults();

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string absolutePath = Path.Combine(projectRoot,
                RougeTowerDefenseBalanceJson.AssetPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(absolutePath, _editableJson, new System.Text.UTF8Encoding(false));
            _changed = false;
            AssetDatabase.ImportAsset(RougeTowerDefenseBalanceJson.AssetPath, ImportAssetOptions.ForceUpdate);
            GUIUtility.ExitGUI();
        }
        catch (System.Exception exception)
        {
            EditorUtility.DisplayDialog("JSON Save Failed", exception.Message, "OK");
        }
    }
}
