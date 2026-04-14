using UnityEngine;

[DefaultExecutionOrder(1000)]
public class RougeCameraFollow : MonoBehaviour
{
    private static float s_runtimeHeightOffset;
    private static float s_runtimeFovOffset;

    public Transform target;
    public Vector3 offset;
    public float smoothSpeed = 0.125f;

    private float _baseFov = -1f;

    public static void SetRuntimeEffects(float heightOffset, float fovOffset)
    {
        s_runtimeHeightOffset = heightOffset;
        s_runtimeFovOffset = fovOffset;
    }

    private void LateUpdate()
    {
        if (target != null)
        {
            Vector3 desiredPosition = target.position + offset;
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
            smoothedPosition += Vector3.up * s_runtimeHeightOffset;
            transform.position = smoothedPosition;
        }

        Camera camera = GetComponent<Camera>();
        if (camera == null || camera.orthographic)
        {
            return;
        }

        if (_baseFov < 1f)
        {
            _baseFov = camera.fieldOfView;
        }

        float targetFov = _baseFov + s_runtimeFovOffset;
        camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, targetFov, 14f * Time.deltaTime);
    }
}
