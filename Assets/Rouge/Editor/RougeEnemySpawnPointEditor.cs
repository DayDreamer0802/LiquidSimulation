using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RougeEnemySpawnPoint))]
public sealed class RougeEnemySpawnPointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty legacyTypeIndex = serializedObject.FindProperty("enemyTypeIndex");
        SerializedProperty typeWeights = serializedObject.FindProperty("enemyTypeWeights");
        string[] names = LoadEnemyTypeNames();

        EditorGUILayout.HelpBox(
            "Each spawn point can mix multiple enemy types by weight percentage. " +
            "Wave growth is applied every 15 seconds. Count caps at +300%; speed caps at +200%.",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((RougeEnemySpawnPoint)target),
                typeof(MonoScript), false);

        DrawEnemyTypeWeights(typeWeights, legacyTypeIndex, names);
        DrawPropertiesExcluding(serializedObject, "m_Script", "enemyTypeIndex", "enemyTypeWeights");
        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawEnemyTypeWeights(SerializedProperty weights, SerializedProperty legacyTypeIndex,
        string[] names)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Enemy Type Weights", EditorStyles.boldLabel);
        int maxTypeIndex = Mathf.Max(0, names.Length - 1);

        if (weights.arraySize == 0)
        {
            legacyTypeIndex.intValue = EditorGUILayout.Popup("Legacy / Fallback Type",
                Mathf.Clamp(legacyTypeIndex.intValue, 0, maxTypeIndex), names);
            EditorGUILayout.HelpBox("The mix is empty, so this spawn point uses the fallback type at 100%.",
                MessageType.None);
            if (GUILayout.Button("Convert Fallback To 100% Mix"))
                AddWeight(weights, legacyTypeIndex.intValue, 100f);
            return;
        }

        float total = 0f;
        for (int i = 0; i < weights.arraySize; i++)
        {
            SerializedProperty entry = weights.GetArrayElementAtIndex(i);
            SerializedProperty typeIndex = entry.FindPropertyRelative("enemyTypeIndex");
            SerializedProperty weight = entry.FindPropertyRelative("weightPercent");
            using (new EditorGUILayout.HorizontalScope())
            {
                typeIndex.intValue = EditorGUILayout.Popup(Mathf.Clamp(typeIndex.intValue, 0, maxTypeIndex), names);
                weight.floatValue = Mathf.Clamp(EditorGUILayout.FloatField(weight.floatValue, GUILayout.Width(64f)),
                    0f, 100f);
                GUILayout.Label("%", GUILayout.Width(14f));
                if (GUILayout.Button("−", GUILayout.Width(28f)))
                {
                    weights.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            total += Mathf.Max(0f, weight.floatValue);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Enemy Type")) AddWeight(weights, 0, 0f);
            using (new EditorGUI.DisabledScope(total <= 0f))
            {
                if (GUILayout.Button("Normalize To 100%")) NormalizeWeights(weights, total);
            }
        }

        MessageType totalMessageType = total > 0f && Mathf.Abs(total - 100f) < 0.01f
            ? MessageType.None
            : MessageType.Warning;
        EditorGUILayout.HelpBox(total <= 0f
            ? "Total weight is 0%; runtime falls back to the legacy enemy type."
            : $"Total: {total:0.##}%. Runtime treats these as relative weights.", totalMessageType);
    }

    private static void AddWeight(SerializedProperty weights, int typeIndex, float weightPercent)
    {
        int index = weights.arraySize;
        weights.arraySize++;
        SerializedProperty entry = weights.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("enemyTypeIndex").intValue = typeIndex;
        entry.FindPropertyRelative("weightPercent").floatValue = weightPercent;
    }

    private static void NormalizeWeights(SerializedProperty weights, float total)
    {
        if (total <= 0f) return;
        for (int i = 0; i < weights.arraySize; i++)
        {
            SerializedProperty weight = weights.GetArrayElementAtIndex(i).FindPropertyRelative("weightPercent");
            weight.floatValue = Mathf.Max(0f, weight.floatValue) * 100f / total;
        }
    }

    private static string[] LoadEnemyTypeNames()
    {
        TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(RougeTowerDefenseBalanceJson.AssetPath);
        if (asset == null) return new[] { "Type 0" };
        try
        {
            RougeTowerDefenseBalanceJsonData data =
                JsonUtility.FromJson<RougeTowerDefenseBalanceJsonData>(asset.text);
            data?.EnsureDefaults();
            if (data?.enemyBalance?.enemyTypes == null || data.enemyBalance.enemyTypes.Count == 0)
                return new[] { "Type 0" };
            string[] names = new string[data.enemyBalance.enemyTypes.Count];
            for (int i = 0; i < names.Length; i++)
            {
                string displayName = data.enemyBalance.enemyTypes[i]?.displayName;
                names[i] = $"{i}: {(string.IsNullOrWhiteSpace(displayName) ? "Unnamed" : displayName)}";
            }
            return names;
        }
        catch
        {
            return new[] { "Type 0" };
        }
    }
}
