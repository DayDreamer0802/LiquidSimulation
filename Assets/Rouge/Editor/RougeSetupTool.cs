using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class RougeSetupTool
{
    private const string FloorMaterialPath = "Assets/Rouge/Rouge_CosmicFloor.mat";
    private const string BarrierMaterialPath = "Assets/Rouge/Rouge_CosmicBarrier.mat";

    [MenuItem("Rouge/Initialize Scene")]
    public static void InitializeScene()
    {
        // Setup Lighting
        var lightObj = new GameObject("Directional Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObj.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

        // Setup Floor
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(50f, 1f, 50f);
        
        Material floorMat = LoadSharedMaterial(FloorMaterialPath, "Rouge/CosmicFloor", new Color(0.08f, 0.12f, 0.18f, 1f));
        floor.GetComponent<MeshRenderer>().material = floorMat;

        // Setup Player
        var playerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerObj.name = "Player";
        playerObj.transform.position = new Vector3(0f, 1f, 0f);
        var playerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        playerMat.color = Color.blue;
        playerObj.GetComponent<MeshRenderer>().material = playerMat;
        var playerBase = playerObj.AddComponent<PlayerBase>();

        // Setup Camera
        var camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        camObj.transform.position = new Vector3(0f, 40f, -25f);
        camObj.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

        var camFollow = camObj.AddComponent<RougeCameraFollow>();
        camFollow.target = playerObj.transform;
        camFollow.offset = new Vector3(0f, 40f, -25f);

        // Setup GameManager
        var gmObj = new GameObject("Rouge Game Manager");
        var gm = gmObj.AddComponent<RougeGameManager>();

        // Setup Obstacles
        var obstacleLayer = LayerMask.NameToLayer("Default");
        var obs1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obs1.name = "Obstacle 1";
        obs1.transform.position = new Vector3(20f, 1f, 20f);
        obs1.transform.localScale = new Vector3(10f, 4f, 10f);
        obs1.layer = obstacleLayer;
        obs1.GetComponent<MeshRenderer>().material = LoadSharedMaterial(BarrierMaterialPath, "Rouge/CosmicBarrier", new Color(0.18f, 0.28f, 0.4f, 0.88f));

        var obs2 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obs2.name = "Obstacle 2";
        obs2.transform.position = new Vector3(-30f, 1f, 15f);
        obs2.transform.localScale = new Vector3(8f, 4f, 8f);
        obs2.layer = obstacleLayer;
        obs2.GetComponent<MeshRenderer>().material = LoadSharedMaterial(BarrierMaterialPath, "Rouge/CosmicBarrier", new Color(0.18f, 0.28f, 0.4f, 0.88f));

        Debug.Log("Rouge scene initialized successfully! Press Play to start.");
    }

    private static Material LoadSharedMaterial(string assetPath, string shaderName, Color fallbackColor)
    {
        Material sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (sharedMaterial != null)
        {
            return sharedMaterial;
        }

        Shader shader = Shader.Find(shaderName);
        var material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));
        material.color = fallbackColor;
        return material;
    }
}
