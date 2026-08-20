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

        DrawPropertiesExcluding(serializedObject, "m_Script", "enemyTypeIndex");
        serializedObject.ApplyModifiedProperties();
    }
}
