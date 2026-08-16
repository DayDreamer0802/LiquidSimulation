using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class RougeCameraFollow : MonoBehaviour
{
    private static float s_runtimeHeightOffset;
    private static float s_runtimeFovOffset;
    private static float s_runtimeZoomScale = 1f;
    private static Camera s_primaryCamera;

    public Transform target;
    public Vector3 offset;
    public float smoothSpeed = 0.125f;
    [Header("Tower Defense Pan")]
    [Min(0.01f)] public float mouseDragSpeed = 1f;
    public RougeCameraBounds movementBounds;
    public Vector2 fallbackBoundsCenter = Vector2.zero;
    public Vector2 fallbackBoundsSize = new Vector2(180f, 180f);

    private float _baseFov = -1f;
    private Camera _camera;
    private Transform _originalTarget;
    private bool _towerDefensePanEnabled;
    private float _lastPanZoomScale = 1f;
    private Vector2 _lastPointerPosition;
    private bool _dragging;
    private bool _cinematicFocusActive;
    private Vector3 _cinematicFocusPoint;
    private float _cinematicShakeIntensity;

    public static void SetRuntimeEffects(float heightOffset, float fovOffset, float zoomScale = 1f)
    {
        s_runtimeHeightOffset = heightOffset;
        s_runtimeFovOffset = fovOffset;
        s_runtimeZoomScale = Mathf.Max(0.01f, zoomScale);
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
        _originalTarget = target;
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
        if (_cinematicFocusActive)
        {
            UpdateCinematicFocus();
        }
        else if (_towerDefensePanEnabled)
        {
            ApplyPanZoom();
            UpdateMousePan();
            ClampToMovementBounds();
        }
        else if (target != null)
        {
            Vector3 desiredPosition = target.position + offset * s_runtimeZoomScale;
            float followT = 1f - Mathf.Pow(1f - Mathf.Clamp01(smoothSpeed), Time.deltaTime * 60f);
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, followT);
            smoothedPosition += Vector3.up * s_runtimeHeightOffset;
            transform.position = smoothedPosition;
        }

        if (_cinematicShakeIntensity > 0.001f)
        {
            transform.position += Random.insideUnitSphere * _cinematicShakeIntensity;
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

    public void SetTowerDefensePan(bool enabled)
    {
        if (_originalTarget == null && target != null) _originalTarget = target;
        _towerDefensePanEnabled = enabled;
        target = enabled ? null : _originalTarget;
        _dragging = false;
        _lastPanZoomScale = s_runtimeZoomScale;
        if (enabled && movementBounds == null)
        {
            movementBounds = FindFirstObjectByType<RougeCameraBounds>();
        }
    }

    public void BeginCinematicFocus(Vector3 worldPoint)
    {
        _cinematicFocusActive = true;
        _cinematicFocusPoint = worldPoint;
        _dragging = false;
    }

    public void SetCinematicShake(float intensity)
    {
        _cinematicShakeIntensity = Mathf.Max(0f, intensity);
    }

    public void EndCinematicFocus()
    {
        _cinematicFocusActive = false;
        _cinematicShakeIntensity = 0f;
    }

    private void UpdateCinematicFocus()
    {
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera == null) return;
        Plane ground = new Plane(Vector3.up, new Vector3(0f, _cinematicFocusPoint.y, 0f));
        Ray centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 desired = transform.position;
        if (ground.Raycast(centerRay, out float distance))
        {
            Vector3 currentCenter = centerRay.GetPoint(distance);
            Vector3 delta = _cinematicFocusPoint - currentCenter;
            delta.y = 0f;
            desired += delta;
        }
        float t = 1f - Mathf.Exp(-5.5f * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, t);
    }

    private void UpdateMousePan()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        bool buildMode = RougeGameManager.TowerDefenseBuildModeActive;
        bool dragHeld = mouse.middleButton.isPressed || (!buildMode && mouse.leftButton.isPressed);
        Vector2 pointer = mouse.position.ReadValue();
        if (!dragHeld)
        {
            _dragging = false;
            return;
        }
        if (!_dragging)
        {
            _dragging = true;
            _lastPointerPosition = pointer;
            return;
        }

        Vector2 delta = pointer - _lastPointerPosition;
        _lastPointerPosition = pointer;
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera == null) return;
        float distanceScale = camera.orthographic
            ? camera.orthographicSize * 2f / Mathf.Max(1f, Screen.height)
            : Mathf.Max(0.01f, transform.position.y) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f /
              Mathf.Max(1f, Screen.height);
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        transform.position += (-right * delta.x - forward * delta.y) * distanceScale * mouseDragSpeed;
    }

    private void ApplyPanZoom()
    {
        float nextScale = Mathf.Max(0.01f, s_runtimeZoomScale);
        if (Mathf.Abs(nextScale - _lastPanZoomScale) <= 0.0001f) return;
        float ratio = nextScale / Mathf.Max(0.01f, _lastPanZoomScale);
        _lastPanZoomScale = nextScale;
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera == null) return;
        if (camera.orthographic)
        {
            camera.orthographicSize = Mathf.Clamp(camera.orthographicSize * ratio, 2f, 300f);
            return;
        }

        Ray centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(centerRay, out float distance))
        {
            Vector3 pivot = centerRay.GetPoint(distance);
            transform.position = pivot + (transform.position - pivot) * ratio;
        }
        else
        {
            Vector3 position = transform.position;
            position.y *= ratio;
            transform.position = position;
        }
    }

    private void ClampToMovementBounds()
    {
        Vector3 position = transform.position;
        float minX;
        float maxX;
        float minZ;
        float maxZ;
        float groundPlaneY;
        if (movementBounds != null)
        {
            Bounds bounds = movementBounds.WorldBounds;
            minX = bounds.min.x;
            maxX = bounds.max.x;
            minZ = bounds.min.z;
            maxZ = bounds.max.z;
            groundPlaneY = bounds.center.y;
        }
        else
        {
            Vector2 halfSize = new Vector2(Mathf.Max(1f, fallbackBoundsSize.x), Mathf.Max(1f, fallbackBoundsSize.y)) * 0.5f;
            minX = fallbackBoundsCenter.x - halfSize.x;
            maxX = fallbackBoundsCenter.x + halfSize.x;
            minZ = fallbackBoundsCenter.y - halfSize.y;
            maxZ = fallbackBoundsCenter.y + halfSize.y;
            groundPlaneY = 0f;
        }

        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera != null && TryGetGroundFootprint(camera, groundPlaneY, out Vector2 footprintMin, out Vector2 footprintMax))
        {
            // The rectangle describes the visible playable area, not the camera transform's
            // travel range. Insets are recomputed from the four viewport corners every frame,
            // so zoom and aspect-ratio changes cannot expose space outside the rectangle.
            position.x = ClampCameraAxis(position.x, minX, maxX, footprintMin.x, footprintMax.x);
            position.z = ClampCameraAxis(position.z, minZ, maxZ, footprintMin.y, footprintMax.y);
        }
        else
        {
            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.z = Mathf.Clamp(position.z, minZ, maxZ);
        }
        transform.position = position;
    }

    private bool TryGetGroundFootprint(Camera camera, float groundPlaneY, out Vector2 minimumOffset, out Vector2 maximumOffset)
    {
        minimumOffset = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximumOffset = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        Plane plane = new Plane(Vector3.up, new Vector3(0f, groundPlaneY, 0f));
        Vector3 cameraPosition = transform.position;
        for (int corner = 0; corner < 4; corner++)
        {
            float viewportX = (corner & 1) == 0 ? 0f : 1f;
            float viewportY = (corner & 2) == 0 ? 0f : 1f;
            Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
            if (!plane.Raycast(ray, out float distance) || distance <= 0f) return false;
            Vector3 hit = ray.GetPoint(distance);
            Vector2 offset = new Vector2(hit.x - cameraPosition.x, hit.z - cameraPosition.z);
            minimumOffset = Vector2.Min(minimumOffset, offset);
            maximumOffset = Vector2.Max(maximumOffset, offset);
        }
        return true;
    }

    private static float ClampCameraAxis(float value, float boundsMin, float boundsMax,
        float footprintMinOffset, float footprintMaxOffset)
    {
        float allowedMin = boundsMin - footprintMinOffset;
        float allowedMax = boundsMax - footprintMaxOffset;
        if (allowedMin <= allowedMax) return Mathf.Clamp(value, allowedMin, allowedMax);

        // At very distant zoom levels the view can be wider than the configured rectangle.
        // Centre it instead of letting the clamp oscillate between inverted limits.
        float boundsCenter = (boundsMin + boundsMax) * 0.5f;
        float footprintCenterOffset = (footprintMinOffset + footprintMaxOffset) * 0.5f;
        return boundsCenter - footprintCenterOffset;
    }

    private void OnDrawGizmosSelected()
    {
        if (movementBounds != null) return;
        Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(fallbackBoundsCenter.x, transform.position.y, fallbackBoundsCenter.y),
            new Vector3(Mathf.Max(1f, fallbackBoundsSize.x), 0.1f, Mathf.Max(1f, fallbackBoundsSize.y)));
    }
}
