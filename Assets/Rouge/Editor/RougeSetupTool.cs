using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class RougeSetupTool
{
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
        
        var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        floorMat.color = new Color(0.2f, 0.2f, 0.2f);
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

        var obs2 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obs2.name = "Obstacle 2";
        obs2.transform.position = new Vector3(-30f, 1f, 15f);
        obs2.transform.localScale = new Vector3(8f, 4f, 8f);
        obs2.layer = obstacleLayer;

        Debug.Log("Rouge scene initialized successfully! Press Play to start.");
    }
}
