using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RougeTowerDefenseSetupTool
{
    [MenuItem("Rouge/Tower Defense/Setup Current Scene")]
    public static void SetupCurrentScene()
    {
        int layer = LayerMask.NameToLayer("TowerPlace");
        if (layer < 0)
        {
            EditorUtility.DisplayDialog("Tower Defense", "ProjectSettings/TagManager needs a TowerPlace layer first.", "OK");
            return;
        }

        int assigned = 0;
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            GameObject go = transforms[i].gameObject;
            if (!go.name.StartsWith("towerPlace", System.StringComparison.OrdinalIgnoreCase)) continue;
            Undo.RecordObject(go, "Assign TowerPlace Layer");
            go.layer = layer;
            EditorUtility.SetDirty(go);
            assigned++;
        }

        RougeMainTower mainTower = Object.FindFirstObjectByType<RougeMainTower>();
        if (mainTower == null)
        {
            GameObject towerObject = new GameObject("Main Tower");
            Undo.RegisterCreatedObjectUndo(towerObject, "Create Main Tower");
            towerObject.transform.position = new Vector3(0f, 0.25f, 0f);
            mainTower = Undo.AddComponent<RougeMainTower>(towerObject);
            CreateSceneMarker(PrimitiveType.Cylinder, "Main Tower Scene Marker", towerObject.transform,
                new Vector3(6f, 10f, 6f), new Vector3(0f, 5f, 0f));
        }

        RougeEnemySpawnPoint spawnPoint = Object.FindFirstObjectByType<RougeEnemySpawnPoint>();
        if (spawnPoint == null)
        {
            GameObject spawnObject = new GameObject("Enemy Spawn Point");
            Undo.RegisterCreatedObjectUndo(spawnObject, "Create Enemy Spawn Point");
            spawnObject.transform.position = new Vector3(0f, 0.25f, 150f);
            spawnPoint = Undo.AddComponent<RougeEnemySpawnPoint>(spawnObject);
            CreateSceneMarker(PrimitiveType.Cylinder, "Spawn Point Scene Marker", spawnObject.transform,
                new Vector3(28f, 0.15f, 28f), Vector3.zero);
        }

        RougeBossSpawnPoint bossSpawn = Object.FindFirstObjectByType<RougeBossSpawnPoint>();
        if (bossSpawn == null)
        {
            GameObject bossObject = new GameObject("Boss Spawn Point");
            Undo.RegisterCreatedObjectUndo(bossObject, "Create Boss Spawn Point");
            bossObject.transform.position = new Vector3(0f, 0.25f, 135f);
            bossSpawn = Undo.AddComponent<RougeBossSpawnPoint>(bossObject);
        }

        RougeCameraBounds cameraBounds = Object.FindFirstObjectByType<RougeCameraBounds>();
        if (cameraBounds == null)
        {
            GameObject boundsObject = new GameObject("Camera Movement Bounds");
            Undo.RegisterCreatedObjectUndo(boundsObject, "Create Camera Movement Bounds");
            cameraBounds = Undo.AddComponent<RougeCameraBounds>(boundsObject);
            BoxCollider box = boundsObject.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(180f, 1f, 180f);
        }

        RougeCameraFollow cameraFollow = Object.FindFirstObjectByType<RougeCameraFollow>();
        if (cameraFollow != null && cameraFollow.movementBounds != cameraBounds)
        {
            Undo.RecordObject(cameraFollow, "Assign Camera Movement Bounds");
            cameraFollow.movementBounds = cameraBounds;
            EditorUtility.SetDirty(cameraFollow);
        }

        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = cameraBounds.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorUtility.DisplayDialog("Tower Defense Ready",
            $"Assigned {assigned} named surfaces to TowerPlace.\nCreated/found enemy, Boss and camera-bound points.\n\nCamera Bounds is selected: drag the cyan Scene handles to set the visible map rectangle.",
            "OK");
    }

    [MenuItem("GameObject/Rouge Tower Defense/Create Enemy Spawn Point", false, 20)]
    private static void CreateEnemySpawnPoint(MenuCommand command)
    {
        GameObject go = new GameObject("Enemy Spawn Point");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create Enemy Spawn Point");
        go.AddComponent<RougeEnemySpawnPoint>();
        go.transform.position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        Selection.activeGameObject = go;
    }

    [MenuItem("GameObject/Rouge Tower Defense/Create Boss Spawn Point", false, 21)]
    private static void CreateBossSpawnPoint(MenuCommand command)
    {
        GameObject go = new GameObject("Boss Spawn Point");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create Boss Spawn Point");
        go.AddComponent<RougeBossSpawnPoint>();
        go.transform.position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        Selection.activeGameObject = go;
    }

    [MenuItem("GameObject/Rouge Tower Defense/Create Camera Movement Bounds", false, 22)]
    private static void CreateCameraMovementBounds(MenuCommand command)
    {
        GameObject go = new GameObject("Camera Movement Bounds");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create Camera Movement Bounds");
        RougeCameraBounds bounds = go.AddComponent<RougeCameraBounds>();
        BoxCollider box = bounds.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(100f, 1f, 100f);
        Selection.activeGameObject = go;
    }

    [MenuItem("GameObject/Rouge Tower Defense/Mark Selection as TowerPlace", false, 23)]
    private static void MarkSelectionAsTowerPlace()
    {
        int layer = LayerMask.NameToLayer("TowerPlace");
        if (layer < 0) return;
        foreach (GameObject go in Selection.gameObjects)
        {
            Undo.RecordObject(go, "Mark TowerPlace");
            go.layer = layer;
            EditorUtility.SetDirty(go);
        }
    }

    private static void CreateSceneMarker(PrimitiveType primitiveType, string name, Transform parent,
        Vector3 scale, Vector3 localPosition)
    {
        GameObject marker = GameObject.CreatePrimitive(primitiveType);
        marker.name = name;
        Undo.RegisterCreatedObjectUndo(marker, "Create Tower Defense Marker");
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        marker.transform.localScale = scale;
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
    }
}
