using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RougeEnemySpawnPoint))]
public sealed class RougeEnemySpawnPointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox(
            "One spawn point uses one enemy type and one square map cell. " +
            "Count stays fixed at 1-64; spawn-speed scaling and elite chance are controlled " +
            "centrally by the Lv1-100 curves in Tower Defense Balance JSON.",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((RougeEnemySpawnPoint)target),
                typeof(MonoScript), false);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnCount"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnInterval"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("startDelay"));
        SerializedProperty limit = serializedObject.FindProperty("limitWaveCount");
        EditorGUILayout.PropertyField(limit, new GUIContent("Limit Maximum Waves"));
        if (limit.boolValue)
        {
            using (new EditorGUI.IndentLevelScope())
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maximumWaves"));
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enemyType"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnCellSize"));
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomPropertyDrawer(typeof(RougeTowerDefenseMap.EnemySpawn))]
public sealed class RougeMapEnemySpawnDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        property.isExpanded = EditorGUI.Foldout(
            new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
            property.isExpanded, label, true);
        if (property.isExpanded)
        {
            float y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Draw(ref y, position, property.FindPropertyRelative("cell"));
            Draw(ref y, position, property.FindPropertyRelative("spawnCount"));
            Draw(ref y, position, property.FindPropertyRelative("spawnInterval"));
            Draw(ref y, position, property.FindPropertyRelative("startDelay"));
            Draw(ref y, position, property.FindPropertyRelative("enemyType"));
            SerializedProperty limit = property.FindPropertyRelative("limitWaveCount");
            Draw(ref y, position, limit, new GUIContent("Limit Maximum Waves"));
            if (limit.boolValue) Draw(ref y, position, property.FindPropertyRelative("maximumWaves"));
        }
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
        bool limited = property.FindPropertyRelative("limitWaveCount").boolValue;
        int lines = limited ? 8 : 7;
        return lines * EditorGUIUtility.singleLineHeight +
               (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
    }

    private static void Draw(ref float y, Rect position, SerializedProperty property,
        GUIContent label = null)
    {
        Rect row = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(row, property, label ?? new GUIContent(ObjectNames.NicifyVariableName(property.name)));
        y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
    }
}
