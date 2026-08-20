using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RougeTowerDefenseMapLoader))]
public sealed class RougeTowerDefenseMapLoaderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox(
            "Attach this component to one scene GameObject and assign a Tower Defense Map. " +
            "It builds the map before RougeGameManager initializes when Play starts.\n" +
            "Per-level camera clamp and zoom are configured on the selected Map in Map Painter.",
            MessageType.Info);
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        RougeTowerDefenseMapLoader loader = (RougeTowerDefenseMapLoader)target;
        using (new EditorGUI.DisabledScope(Application.isPlaying || loader.Map == null))
        {
            if (GUILayout.Button("Rebuild Map Preview"))
            {
                loader.LoadMap();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Clear Map Preview"))
            {
                loader.ClearMap();
                SceneView.RepaintAll();
            }
        }
        if (Application.isPlaying)
            EditorGUILayout.HelpBox("The runtime map is loaded from this component.", MessageType.None);
    }
}
