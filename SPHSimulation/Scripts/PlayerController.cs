using Unity.Mathematics;
using UnityEngine;

namespace HighPerform.SPHSimulation.Scripts
{
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float moveSpeed = 150f; // �ƶ��ٶȣ���Ϊ�����ܴ��ٶ�Ҫ����

        public float3 playerForceOnce;
        public float3 playerForce;
        private Camera _mainCamera;

        void Start()
        {
            _mainCamera = Camera.main;
        }
        public Vector3 GetPlayerMoveVelocity()
        {
            float h = Input.GetAxisRaw("Horizontal"); // A/D
            float v = Input.GetAxisRaw("Vertical");   // W/S

            Vector3 camForward = _mainCamera.transform.forward;
            Vector3 camRight = _mainCamera.transform.right;

            camForward.y = 0;
            camRight.y = 0;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDir = (camForward * v + camRight * h).normalized;

            return moveDir;

        }
        void Update()
        {
   


        }
    }
}