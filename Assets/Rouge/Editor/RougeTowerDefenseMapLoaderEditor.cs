using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(RougeTowerDefenseMapLoader))]
public sealed class RougeTowerDefenseMapLoaderEditor : Editor
{
    private const string PreviewModeSessionPrefix =
        "Rouge.TowerDefenseMapLoader.CameraPreviewMode.";

    private RougeCameraPresetMode _previewMode;
    private string _previewModeSessionKey;

    private void OnEnable()
    {
        _previewModeSessionKey = BuildPreviewModeSessionKey();
        _previewMode = (RougeCameraPresetMode)Mathf.Clamp(
            SessionState.GetInt(_previewModeSessionKey, (int)RougeCameraPresetMode.Default),
            (int)RougeCameraPresetMode.Default,
            (int)RougeCameraPresetMode.TopDown);
    }

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
        RougeTowerDefenseMap map = loader.Map;
        Camera sceneCamera = RougeCameraFollow.ResolveCamera();
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Scene 镜头取值 / 预览", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(Application.isPlaying || loader.Map == null))
        {
            int previewMode = GUILayout.SelectionGrid((int)_previewMode,
                new[] { "默认 + UI", "F1 自由", "F2 移轴", "F3 俯视" }, 2);
            RougeCameraPresetMode nextMode = (RougeCameraPresetMode)previewMode;
            if (nextMode != _previewMode)
            {
                SetPreviewMode(nextMode);
                ApplyPreview(loader, map, sceneCamera);
            }

            using (new EditorGUI.DisabledScope(sceneCamera == null))
            {
                if (GUILayout.Button($"保存当前 Scene 镜头 → {CameraPresetLabel(_previewMode)}"))
                    CaptureCameraPreset(map, sceneCamera, _previewMode);
            }

            if (GUILayout.Button("Rebuild 并应用当前镜头预览"))
            {
                loader.LoadMap();
                ApplyPreview(loader, map, sceneCamera);
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(sceneCamera == null))
            {
                RougeTiltShiftCamera sceneTiltShift = sceneCamera != null
                    ? sceneCamera.GetComponent<RougeTiltShiftCamera>()
                    : null;
                using (new EditorGUI.DisabledScope(sceneTiltShift == null))
                {
                    if (GUILayout.Button("Map SO 移轴配置 → Scene 相机"))
                    {
                        Undo.RecordObject(sceneTiltShift, "Apply Map Tilt-Shift Settings");
                        sceneTiltShift.ApplySettings(map.TiltShiftSettings);
                        EditorUtility.SetDirty(sceneTiltShift);
                        EditorSceneManager.MarkSceneDirty(sceneTiltShift.gameObject.scene);
                        Debug.Log($"[地图镜头] 已将 {map.name} 的 F2 移轴配置应用到 Scene 相机。",
                            sceneTiltShift);
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    }
                }
            }

            if (GUILayout.Button("Clear Map Preview"))
            {
                loader.ClearMap();
                loader.ClearCameraPreviewUi();
                RougeTiltShiftCamera tiltShift = sceneCamera != null
                    ? sceneCamera.GetComponent<RougeTiltShiftCamera>()
                    : null;
                if (tiltShift != null) tiltShift.SetEffectEnabled(false);
                SceneView.RepaintAll();
            }
        }
        if (Application.isPlaying)
            EditorGUILayout.HelpBox("The runtime map is loaded from this component.", MessageType.None);
    }

    private string BuildPreviewModeSessionKey()
    {
        if (target == null) return PreviewModeSessionPrefix + "None";
        GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(target);
        return PreviewModeSessionPrefix + globalId;
    }

    private void SetPreviewMode(RougeCameraPresetMode mode)
    {
        _previewMode = mode;
        if (string.IsNullOrEmpty(_previewModeSessionKey))
            _previewModeSessionKey = BuildPreviewModeSessionKey();
        SessionState.SetInt(_previewModeSessionKey, (int)mode);
    }

    private static void CaptureCameraPreset(RougeTowerDefenseMap map, Camera camera,
        RougeCameraPresetMode mode)
    {
        if (map == null || camera == null) return;
        Undo.RecordObject(map, $"Capture {mode} Camera Preset");
        map.SetCameraPreset(mode, RougeCameraViewPreset.Capture(camera));
        bool capturedTiltSettings = false;
        if (mode == RougeCameraPresetMode.TiltShift)
        {
            RougeTiltShiftCamera effect = camera.GetComponent<RougeTiltShiftCamera>();
            if (effect != null)
            {
                map.SetTiltShiftSettings(effect.CaptureSettings());
                capturedTiltSettings = true;
            }
            else
                Debug.LogWarning("[地图镜头] Scene 相机没有 RougeTiltShiftCamera，已写入 F2 镜头，但未写入移轴配置。",
                    camera);
        }
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssetIfDirty(map);
        string extra = capturedTiltSettings ? "与移轴配置" : string.Empty;
        Debug.Log($"[地图镜头] 写入成功：Scene 的 {CameraPresetLabel(mode)}完整镜头{extra}已保存到 {map.name} SO。",
            map);
    }

    private static string CameraPresetLabel(RougeCameraPresetMode mode)
    {
        switch (mode)
        {
            case RougeCameraPresetMode.Free: return "F1 自由";
            case RougeCameraPresetMode.TiltShift: return "F2 移轴";
            case RougeCameraPresetMode.TopDown: return "F3 俯视";
            default: return "默认";
        }
    }

    private void ApplyPreview(RougeTowerDefenseMapLoader loader, RougeTowerDefenseMap map,
        Camera camera)
    {
        if (loader == null || map == null || camera == null) return;
        Undo.RecordObjects(new UnityEngine.Object[] { camera, camera.transform },
            "Preview Map Camera");
        RougeTiltShiftCamera effect = camera.GetComponent<RougeTiltShiftCamera>();
        if (effect != null) Undo.RecordObject(effect, "Preview Tilt Shift");
        loader.ApplyCameraPreview(map, _previewMode,
            _previewMode != RougeCameraPresetMode.TiltShift);
        EditorUtility.SetDirty(camera);
        EditorUtility.SetDirty(camera.transform);
        EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
        if (effect != null)
        {
            EditorUtility.SetDirty(effect);
            EditorSceneManager.MarkSceneDirty(effect.gameObject.scene);
        }
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
}
