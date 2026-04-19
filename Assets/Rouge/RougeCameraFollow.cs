using UnityEngine;

[DefaultExecutionOrder(1000)]
public class RougeCameraFollow : MonoBehaviour
{
    private static float s_runtimeHeightOffset;
    private static float s_runtimeFovOffset;
    private static Camera s_primaryCamera;

    public Transform target;
    public Vector3 offset;
    public float smoothSpeed = 0.125f;

    private float _baseFov = -1f;
    private Camera _camera;

    public static void SetRuntimeEffects(float heightOffset, float fovOffset)
    {
        s_runtimeHeightOffset = heightOffset;
        s_runtimeFovOffset = fovOffset;
    }

    public static Camera ResolveCamera()
    {
        if (s_primaryCamera != null && s_primaryCamera.isActiveAndEnabled)
        {
            return s_primaryCamera;
        }

        RougeCameraFollow follow = FindFirstObjectByType<RougeCameraFollow>();
        if (follow != null)
        {
            Camera followCamera = follow.GetComponent<Camera>();
            if (followCamera != null && followCamera.isActiveAndEnabled)
            {
                s_primaryCamera = followCamera;
                return s_primaryCamera;
            }
        }

        return Camera.main;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        if (_camera != null && _camera.isActiveAndEnabled)
        {
            s_primaryCamera = _camera;
        }
    }

    private void OnDisable()
    {
        if (s_primaryCamera == _camera)
        {
            s_primaryCamera = null;
        }
    }

    private void LateUpdate()
    {
        if (target != null)
        {
            Vector3 desiredPosition = target.position + offset;
            float followT = 1f - Mathf.Pow(1f - Mathf.Clamp01(smoothSpeed), Time.deltaTime * 60f);
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, followT);
            smoothedPosition += Vector3.up * s_runtimeHeightOffset;
            transform.position = smoothedPosition;
        }

        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
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
