using UnityEngine;

public class TestRot : MonoBehaviour
{
    public float rotSpeed = 90f; // 每秒旋转的角度
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.Rotate(Vector3.up, rotSpeed *Time.deltaTime);
    }
}
