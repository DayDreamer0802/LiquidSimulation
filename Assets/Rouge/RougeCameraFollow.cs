using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class RougeCameraFollow : MonoBehaviour
{
    private const float DebugFreeMinimumHeight = 0f;
    private const float DebugFreeMaximumHeight = 100f;
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
    [Header("Debug Free Camera")]
    [Min(0.1f)] public float debugFreeMoveSpeed = 25f;
    [Min(1f)] public float debugFreeFastMultiplier = 4f;
    [Min(0.01f)] public float debugFreeLookSensitivity = 0.12f;

    private float _baseFov = -1f;
    private Camera _camera;
    private Transform _originalTarget;
    private bool _towerDefensePanEnabled;
    private bool _movementClampEnabled = true;
    private float _lastPanZoomScale = 1f;
    private Vector2 _lastPointerPosition;
    private int _panDragButton;
    private bool _cinematicFocusActive;
    private Vector3 _cinematicFocusPoint;
    private float _cinematicShakeIntensity;
    private bool _debugFreeViewActive;
    private bool _debugLookActive;
    private float _debugFreeYaw;
    private float _debugFreePitch;
    private Vector3 _debugRestorePosition;
    private Quaternion _debugRestoreRotation;
    private bool _debugRestoreOrthographic;
    private float _debugRestoreFov;
    private float _debugRestoreNearClip;
    private CursorLockMode _debugRestoreCursorLock;
    private bool _debugRestoreCursorVisible;
    private float _minimumZoomScale = 0.5f;
    private float _maximumZoomScale = 5f;

    public static void SetRuntimeEffects(float heightOffset, float fovOffset, float zoomScale = 1f)
    {
        s_runtimeHeightOffset = heightOffset;
        s_runtimeFovOffset = fovOffset;
        RougeCameraFollow follow = s_primaryCamera != null
            ? s_primaryCamera.GetComponent<RougeCameraFollow>()
            : FindFirstObjectByType<RougeCameraFollow>();
        s_runtimeZoomScale = follow != null
            ? follow.ClampZoomScale(zoomScale)
            : Mathf.Max(0.01f, zoomScale);
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
        if (_debugFreeViewActive) EndDebugFreeView();
        if (s_primaryCamera == _camera)
        {
            s_primaryCamera = null;
        }
    }

    private void LateUpdate()
    {
        if (_debugFreeViewActive)
        {
            UpdateDebugFreeView();
            return;
        }

        if (_cinematicFocusActive)
        {
            UpdateCinematicFocus();
        }
        else if (_towerDefensePanEnabled)
        {
            ApplyPanZoom();
            UpdateMousePan();
            if (_movementClampEnabled) ClampToMovementBounds();
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

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) return;
        _panDragButton = 0;
        if (_debugLookActive) SetDebugLookActive(false);
    }

    public void SetTowerDefensePan(bool enabled)
    {
        if (_originalTarget == null && target != null) _originalTarget = target;
        _towerDefensePanEnabled = enabled;
        target = enabled ? null : _originalTarget;
        _lastPanZoomScale = s_runtimeZoomScale;
        if (enabled && movementBounds == null)
        {
            movementBounds = FindFirstObjectByType<RougeCameraBounds>();
        }
    }

    public void SetMovementBounds(RougeCameraBounds bounds)
    {
        movementBounds = bounds;
        if (_towerDefensePanEnabled && _movementClampEnabled) ClampToMovementBounds();
    }

    public void SetMovementClampEnabled(bool enabled)
    {
        _movementClampEnabled = enabled;
        if (enabled && _towerDefensePanEnabled) ClampToMovementBounds();
    }

    public float ClampZoomScale(float value)
    {
        return Mathf.Clamp(value, _minimumZoomScale, _maximumZoomScale);
    }

    public void SetZoomLimits(float minimum, float maximum)
    {
        _minimumZoomScale = Mathf.Max(0.01f, minimum);
        _maximumZoomScale = Mathf.Max(_minimumZoomScale, maximum);
        s_runtimeZoomScale = ClampZoomScale(s_runtimeZoomScale);
    }

    public void FocusGroundPointImmediately(Vector3 worldPoint)
    {
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera == null) return;

        Plane ground = new Plane(Vector3.up, worldPoint);
        Ray centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!ground.Raycast(centerRay, out float distance)) return;

        Vector3 currentCenter = centerRay.GetPoint(distance);
        Vector3 delta = worldPoint - currentCenter;
        delta.y = 0f;
        transform.position += delta;
    }

    public void BeginDebugFreeView()
    {
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (_debugFreeViewActive) return;
        _debugRestorePosition = transform.position;
        _debugRestoreRotation = transform.rotation;
        _debugRestoreCursorLock = Cursor.lockState;
        _debugRestoreCursorVisible = Cursor.visible;
        if (camera != null)
        {
            _debugRestoreOrthographic = camera.orthographic;
            _debugRestoreFov = camera.fieldOfView;
            _debugRestoreNearClip = camera.nearClipPlane;
            camera.orthographic = false;
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.03f;
        }
        Vector3 euler = transform.eulerAngles;
        _debugFreeYaw = euler.y;
        _debugFreePitch = NormalizeSignedAngle(euler.x);
        _debugFreeViewActive = true;
        _panDragButton = 0;
        _debugLookActive = false;
        ClampDebugFreePosition();
    }

    public void EndDebugFreeView()
    {
        if (!_debugFreeViewActive) return;
        if (_debugLookActive) SetDebugLookActive(false);
        _debugFreeViewActive = false;
        transform.SetPositionAndRotation(_debugRestorePosition, _debugRestoreRotation);
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera != null)
        {
            camera.orthographic = _debugRestoreOrthographic;
            camera.fieldOfView = _debugRestoreFov;
            camera.nearClipPlane = _debugRestoreNearClip;
        }
        Cursor.lockState = _debugRestoreCursorLock;
        Cursor.visible = _debugRestoreCursorVisible;
    }

    private void UpdateDebugFreeView()
    {
        Mouse mouse = Mouse.current;
        bool wantsLook = mouse != null && mouse.rightButton.isPressed;
        if (wantsLook != _debugLookActive) SetDebugLookActive(wantsLook);
        if (_debugLookActive && mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * debugFreeLookSensitivity;
            _debugFreeYaw += look.x;
            _debugFreePitch = Mathf.Clamp(_debugFreePitch - look.y, -89f, 89f);
        }
        transform.rotation = Quaternion.Euler(_debugFreePitch, _debugFreeYaw, 0f);

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            ClampDebugFreePosition();
            return;
        }
        float horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float forwardInput = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.spaceKey.isPressed || keyboard.eKey.isPressed ? 1f : 0f) -
            (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed || keyboard.qKey.isPressed ? 1f : 0f);
        Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (planarForward.sqrMagnitude < 0.0001f) planarForward = Vector3.forward;
        Vector3 planarRight = Vector3.Cross(Vector3.up, planarForward).normalized;
        Vector3 movement = planarRight * horizontal + planarForward * forwardInput + Vector3.up * vertical;
        if (movement.sqrMagnitude > 1f) movement.Normalize();
        bool fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        float speed = debugFreeMoveSpeed * (fast ? debugFreeFastMultiplier : 1f);
        transform.position += movement * speed * Time.unscaledDeltaTime;
        ClampDebugFreePosition();
    }

    private void SetDebugLookActive(bool active)
    {
        _debugLookActive = active;
        Cursor.lockState = active ? CursorLockMode.Locked : _debugRestoreCursorLock;
        Cursor.visible = active ? false : _debugRestoreCursorVisible;
    }

    private static float NormalizeSignedAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    public void BeginCinematicFocus(Vector3 worldPoint)
    {
        _cinematicFocusActive = true;
        _cinematicFocusPoint = worldPoint;
        _panDragButton = 0;
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
        if (mouse == null)
        {
            _panDragButton = 0;
            return;
        }
        bool buildMode = RougeGameManager.TowerDefenseBuildModeActive;
        Vector2 pointer = mouse.position.ReadValue();
        if (_panDragButton == 0)
        {
            if (mouse.middleButton.wasPressedThisFrame) _panDragButton = 1;
            else if (!buildMode && mouse.leftButton.wasPressedThisFrame) _panDragButton = 2;
            else return;
            _lastPointerPosition = pointer;
            return;
        }

        bool dragHeld = _panDragButton == 1
            ? mouse.middleButton.isPressed
            : !buildMode && mouse.leftButton.isPressed;
        if (!dragHeld)
        {
            _panDragButton = 0;
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
        float nextScale = ClampZoomScale(s_runtimeZoomScale);
        if (Mathf.Abs(nextScale - _lastPanZoomScale) <= 0.0001f) return;
        float ratio = nextScale / Mathf.Max(0.01f, _lastPanZoomScale);
        _lastPanZoomScale = nextScale;
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera == null) return;
        if (camera.orthographic)
        {
            camera.orthographicSize = Mathf.Max(0.01f, camera.orthographicSize * ratio);
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
        GetPlanarMovementBounds(out float minX, out float maxX, out float minZ, out float maxZ,
            out float groundPlaneY);

        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera != null && TryGetViewportGroundCenter(camera, groundPlaneY, out Vector3 groundCenter))
        {
            // The authored rectangle is the travel range of the point shown at the
            // screen center. A tilted perspective camera's transform is offset from
            // this point, so clamping the transform itself makes opposite edges uneven.
            float clampedX = Mathf.Clamp(groundCenter.x, minX, maxX);
            float clampedZ = Mathf.Clamp(groundCenter.z, minZ, maxZ);
            position.x += clampedX - groundCenter.x;
            position.z += clampedZ - groundCenter.z;
        }
        else
        {
            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.z = Mathf.Clamp(position.z, minZ, maxZ);
        }
        transform.position = position;
    }

    private void ClampDebugFreePosition()
    {
        Vector3 position = transform.position;
        position.y = Mathf.Clamp(position.y, DebugFreeMinimumHeight, DebugFreeMaximumHeight);
        if (_movementClampEnabled)
        {
            GetPlanarMovementBounds(out float minX, out float maxX, out float minZ, out float maxZ,
                out _);
            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.z = Mathf.Clamp(position.z, minZ, maxZ);
        }
        transform.position = position;
    }

    private void GetPlanarMovementBounds(out float minX, out float maxX, out float minZ,
        out float maxZ, out float groundPlaneY)
    {
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
    }

    private static bool TryGetViewportGroundCenter(Camera camera, float groundPlaneY,
        out Vector3 groundCenter)
    {
        Plane ground = new Plane(Vector3.up, new Vector3(0f, groundPlaneY, 0f));
        Ray centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (ground.Raycast(centerRay, out float distance) && distance >= 0f)
        {
            groundCenter = centerRay.GetPoint(distance);
            return true;
        }
        groundCenter = default;
        return false;
    }

    private void OnValidate()
    {
        fallbackBoundsSize.x = Mathf.Max(1f, fallbackBoundsSize.x);
        fallbackBoundsSize.y = Mathf.Max(1f, fallbackBoundsSize.y);
    }

    private void OnDrawGizmosSelected()
    {
        Camera camera = GetComponent<Camera>();
        float groundY = movementBounds != null ? movementBounds.WorldBounds.center.y : 0f;
        if (camera != null && TryGetViewportGroundCenter(camera, groundY, out Vector3 groundCenter))
        {
            Gizmos.color = new Color(1f, 0.82f, 0.12f, 0.95f);
            Gizmos.DrawLine(transform.position, groundCenter);
            Gizmos.DrawWireSphere(groundCenter, 0.8f);
        }
        if (movementBounds != null) return;
        Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(fallbackBoundsCenter.x, transform.position.y, fallbackBoundsCenter.y),
            new Vector3(Mathf.Max(1f, fallbackBoundsSize.x), 0.1f, Mathf.Max(1f, fallbackBoundsSize.y)));
    }
}
