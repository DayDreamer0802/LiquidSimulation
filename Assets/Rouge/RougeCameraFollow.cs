using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class RougeCameraFollow : MonoBehaviour
{
    private const float DebugFreeMinimumHeight = 3f;
    private const float DebugFreeMaximumHeight = 1000f;
    // Runtime scaling keeps existing scene/prefab serialized durations intact.
    private const float CameraViewTransitionDurationScale = 0.75f;
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
    [Min(0.001f)] public float debugFreeScrollSpeed = 1.25f;
    [Header("Camera View Transition")]
    [SerializeField, Range(0.1f, 2f)] private float cameraViewTransitionDuration = 0.55f;

    public struct ViewState
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Orthographic;
        public float FieldOfView;
        public float OrthographicSize;
        public float NearClipPlane;
        public float FarClipPlane;
    }

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
    private bool _debugFreeClampAnchorInitialized;
    private Vector2 _debugFreeClampAnchorOffsetXZ;
    private bool _topDownViewActive;
    private float _debugFreeYaw;
    private float _debugFreePitch;
    private Vector3 _debugRestorePosition;
    private Quaternion _debugRestoreRotation;
    private bool _debugRestoreOrthographic;
    private float _debugRestoreFov;
    private float _debugRestoreNearClip;
    private CursorLockMode _debugRestoreCursorLock;
    private bool _debugRestoreCursorVisible;
    private Vector3 _topDownRestoreOffsetFromGround;
    private Quaternion _topDownRestoreRotation;
    private float _topDownRestoreFocusDistance;
    private float _minimumZoomScale = 0.5f;
    private float _maximumZoomScale = 2f;
    private float _defaultViewHeight;
    private Quaternion _defaultViewRotation;
    private float _defaultViewFov = 60f;
    private bool _scriptedViewActive;
    private bool _scriptedViewHolding;
    private bool _releaseScriptedViewWhenTransitionCompletes;
    private float _scriptedViewTransitionElapsed;
    private ViewState _scriptedViewStart;
    private ViewState _scriptedViewTarget;
    private bool _userInputBlocked;

    public bool IsScriptedViewActive => _scriptedViewActive;
    public bool IsScriptedViewTransitioning => _scriptedViewActive && !_scriptedViewHolding;
    public float CameraViewTransitionDuration =>
        Mathf.Max(0.01f, cameraViewTransitionDuration * CameraViewTransitionDurationScale);

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
        _defaultViewHeight = transform.position.y;
        _defaultViewRotation = transform.rotation;
        if (_camera != null) _defaultViewFov = _camera.fieldOfView;
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
        _scriptedViewActive = false;
        _scriptedViewHolding = false;
        if (_debugFreeViewActive) EndDebugFreeView();
        if (_topDownViewActive) EndTopDownView();
        if (s_primaryCamera == _camera)
        {
            s_primaryCamera = null;
        }
    }

    private void LateUpdate()
    {
        if (_scriptedViewActive)
        {
            UpdateScriptedView();
            return;
        }

        if (_userInputBlocked) return;

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

    public void SetUserInputBlocked(bool blocked)
    {
        if (_userInputBlocked == blocked) return;
        _userInputBlocked = blocked;
        _panDragButton = 0;
        if (_debugLookActive) SetDebugLookActive(false);
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
        _minimumZoomScale = Mathf.Clamp(minimum, 0.5f, 2f);
        _maximumZoomScale = Mathf.Clamp(maximum, _minimumZoomScale, 2f);
        s_runtimeZoomScale = ClampZoomScale(s_runtimeZoomScale);
    }

    public void SetPositionXZImmediately(Vector2 worldXZ)
    {
        Vector3 position = transform.position;
        position.x = worldXZ.x;
        position.z = worldXZ.y;
        transform.position = position;
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

    public ViewState CaptureViewState()
    {
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        return new ViewState
        {
            Position = transform.position,
            Rotation = transform.rotation,
            Orthographic = camera != null && camera.orthographic,
            FieldOfView = camera != null ? camera.fieldOfView : _defaultViewFov,
            OrthographicSize = camera != null ? camera.orthographicSize : 5f,
            NearClipPlane = camera != null ? camera.nearClipPlane : 0.3f,
            FarClipPlane = camera != null ? camera.farClipPlane : 1000f
        };
    }

    public void ApplyViewState(ViewState state)
    {
        transform.SetPositionAndRotation(state.Position, state.Rotation);
        Camera camera = _camera != null ? _camera : GetComponent<Camera>();
        if (camera == null) return;
        camera.orthographic = state.Orthographic;
        camera.fieldOfView = state.FieldOfView;
        camera.orthographicSize = state.OrthographicSize;
        camera.nearClipPlane = state.NearClipPlane;
        camera.farClipPlane = state.FarClipPlane;
    }

    public ViewState BuildTiltShiftObservationView(Vector2 cameraPositionXZ)
    {
        ViewState state = CaptureViewState();
        float height = Mathf.Abs(_defaultViewHeight) > 0.001f
            ? _defaultViewHeight
            : transform.position.y;
        state.Position = new Vector3(cameraPositionXZ.x, height, cameraPositionXZ.y);
        state.Rotation = _defaultViewRotation;
        state.Orthographic = false;
        state.FieldOfView = _defaultViewFov;
        return state;
    }

    public ViewState BuildTopDownViewState(ViewState sourceState)
    {
        float groundPlaneY = GetGroundPlaneY();
        if (!TryGetViewStateGroundCenter(sourceState, groundPlaneY, out Vector3 groundCenter))
            groundCenter = new Vector3(sourceState.Position.x, groundPlaneY,
                sourceState.Position.z);
        float focusDistance = Mathf.Max(0.01f,
            Vector3.Distance(sourceState.Position, groundCenter));
        sourceState.Position = groundCenter + Vector3.up * focusDistance;
        sourceState.Rotation = Quaternion.Euler(90f, 0f, 0f);
        return sourceState;
    }

    public ViewState ApplySharedZoomToViewState(ViewState state, float zoomScale)
    {
        float scale = ClampZoomScale(zoomScale);
        if (state.Orthographic)
        {
            state.OrthographicSize = Mathf.Max(0.01f, state.OrthographicSize * scale);
            return state;
        }

        float groundPlaneY = GetGroundPlaneY();
        if (TryGetViewStateGroundCenter(state, groundPlaneY, out Vector3 pivot))
            state.Position = pivot + (state.Position - pivot) * scale;
        else
            state.Position = new Vector3(state.Position.x, state.Position.y * scale,
                state.Position.z);
        return state;
    }

    public ViewState ClampViewStateToMovementBounds(ViewState state)
    {
        if (!_movementClampEnabled) return state;
        GetPlanarMovementBounds(out float minX, out float maxX, out float minZ,
            out float maxZ, out float groundPlaneY);
        if (TryGetViewStateGroundCenter(state, groundPlaneY, out Vector3 groundCenter))
        {
            state.Position.x += Mathf.Clamp(groundCenter.x, minX, maxX) - groundCenter.x;
            state.Position.z += Mathf.Clamp(groundCenter.z, minZ, maxZ) - groundCenter.z;
        }
        else
        {
            state.Position.x = Mathf.Clamp(state.Position.x, minX, maxX);
            state.Position.z = Mathf.Clamp(state.Position.z, minZ, maxZ);
        }
        return state;
    }

    public ViewState ClampDebugFreeViewState(ViewState state)
    {
        state.Position.y = Mathf.Clamp(state.Position.y,
            DebugFreeMinimumHeight, DebugFreeMaximumHeight);
        return ClampViewStateToMovementBounds(state);
    }

    public void SetSharedZoomAsApplied(float zoomScale)
    {
        s_runtimeZoomScale = ClampZoomScale(zoomScale);
        _lastPanZoomScale = s_runtimeZoomScale;
    }

    public void SetViewBaseline(ViewState state)
    {
        _baseFov = state.FieldOfView;
    }

    public void BeginScriptedView(ViewState targetState)
    {
        StartScriptedViewTransition(targetState, false);
    }

    public void TransitionAndReleaseScriptedView(ViewState targetState)
    {
        StartScriptedViewTransition(targetState, true);
    }

    public void CancelScriptedView()
    {
        _scriptedViewActive = false;
        _scriptedViewHolding = false;
        _releaseScriptedViewWhenTransitionCompletes = false;
    }

    private void StartScriptedViewTransition(ViewState targetState, bool releaseWhenComplete)
    {
        if (_debugLookActive) SetDebugLookActive(false);
        _scriptedViewStart = CaptureViewState();
        _scriptedViewTarget = targetState;
        _scriptedViewTransitionElapsed = 0f;
        _scriptedViewActive = true;
        _scriptedViewHolding = false;
        _releaseScriptedViewWhenTransitionCompletes = releaseWhenComplete;
        _panDragButton = 0;
    }

    private void UpdateScriptedView()
    {
        if (_scriptedViewHolding)
        {
            ApplyViewState(_scriptedViewTarget);
            return;
        }

        _scriptedViewTransitionElapsed += Time.unscaledDeltaTime;
        float duration = CameraViewTransitionDuration;
        float linearT = Mathf.Clamp01(_scriptedViewTransitionElapsed / duration);
        float smoothStepT = linearT * linearT * (3f - 2f * linearT);
        float easeOutT = 1f - (1f - linearT) * (1f - linearT);
        // A pure smoothstep starts with zero velocity and makes a hotkey switch
        // appear to sit on its first frame. Keep the soft landing, but give the
        // transition a small ease-out contribution so movement starts immediately.
        float smoothT = Mathf.Lerp(smoothStepT, easeOutT, 0.25f);
        ViewState blended = new ViewState
        {
            Position = Vector3.Lerp(_scriptedViewStart.Position,
                _scriptedViewTarget.Position, smoothT),
            Rotation = Quaternion.Slerp(_scriptedViewStart.Rotation,
                _scriptedViewTarget.Rotation, smoothT),
            Orthographic = linearT < 0.5f
                ? _scriptedViewStart.Orthographic
                : _scriptedViewTarget.Orthographic,
            FieldOfView = Mathf.Lerp(_scriptedViewStart.FieldOfView,
                _scriptedViewTarget.FieldOfView, smoothT),
            OrthographicSize = Mathf.Lerp(_scriptedViewStart.OrthographicSize,
                _scriptedViewTarget.OrthographicSize, smoothT),
            NearClipPlane = Mathf.Lerp(_scriptedViewStart.NearClipPlane,
                _scriptedViewTarget.NearClipPlane, smoothT),
            FarClipPlane = Mathf.Lerp(_scriptedViewStart.FarClipPlane,
                _scriptedViewTarget.FarClipPlane, smoothT)
        };
        ApplyViewState(blended);
        if (linearT < 1f) return;

        ApplyViewState(_scriptedViewTarget);
        if (_releaseScriptedViewWhenTransitionCompletes)
        {
            _scriptedViewActive = false;
            _scriptedViewHolding = false;
            _releaseScriptedViewWhenTransitionCompletes = false;
        }
        else
        {
            _scriptedViewHolding = true;
        }
    }

    public void BeginDebugFreeView()
    {
        BeginDebugFreeView(transform.rotation);
    }

    public void BeginDebugFreeView(Quaternion initialRotation)
    {
        if (_debugFreeViewActive)
        {
            SynchronizeDebugFreeLook(initialRotation);
            return;
        }
        _debugRestoreCursorLock = Cursor.lockState;
        _debugRestoreCursorVisible = Cursor.visible;
        SynchronizeDebugFreeLook(initialRotation);
        _debugFreeViewActive = true;
        _debugFreeClampAnchorInitialized = false;
        _panDragButton = 0;
        _debugLookActive = false;
    }

    public void EndDebugFreeView()
    {
        if (!_debugFreeViewActive) return;
        if (_debugLookActive) SetDebugLookActive(false);
        _debugFreeViewActive = false;
        _debugFreeClampAnchorInitialized = false;
        Cursor.lockState = _debugRestoreCursorLock;
        Cursor.visible = _debugRestoreCursorVisible;
    }

    private void SynchronizeDebugFreeLook(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        _debugFreeYaw = euler.y;
        _debugFreePitch = NormalizeSignedAngle(euler.x);
    }

    public void BeginTopDownView()
    {
        if (_topDownViewActive) return;
        _topDownViewActive = true;
        _panDragButton = 0;
    }

    public void EndTopDownView()
    {
        if (!_topDownViewActive) return;
        _topDownViewActive = false;
        _panDragButton = 0;
    }

    private void UpdateDebugFreeView()
    {
        // Capture the authored/clamped F1 endpoint before this frame's look input can
        // change the ray used to define the logical movement anchor.
        EnsureDebugFreeClampAnchor();

        Mouse mouse = Mouse.current;
        // A fresh right-click is reserved for cancelling a tower operation. Holding
        // past that first frame enters camera look, so the two actions never fight.
        bool wantsLook = mouse != null && mouse.rightButton.isPressed &&
                         !mouse.rightButton.wasPressedThisFrame &&
                         !RougeGameManager.TowerDefenseBuildModeActive;
        if (wantsLook != _debugLookActive) SetDebugLookActive(wantsLook);
        if (_debugLookActive && mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * debugFreeLookSensitivity;
            _debugFreeYaw += look.x;
            _debugFreePitch = Mathf.Clamp(_debugFreePitch - look.y, -89f, 89f);
        }
        transform.rotation = Quaternion.Euler(_debugFreePitch, _debugFreeYaw, 0f);

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            float horizontal = (keyboard.dKey.isPressed ? 1f : 0f) -
                (keyboard.aKey.isPressed ? 1f : 0f);
            float forwardInput = (keyboard.wKey.isPressed ? 1f : 0f) -
                (keyboard.sKey.isPressed ? 1f : 0f);
            Vector3 planarForward = Vector3.ProjectOnPlane(
                transform.forward, Vector3.up).normalized;
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = Vector3.forward;
            Vector3 planarRight = Vector3.Cross(Vector3.up, planarForward).normalized;
            Vector3 movement = planarRight * horizontal + planarForward * forwardInput;
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            bool fast = keyboard.leftShiftKey.isPressed ||
                keyboard.rightShiftKey.isPressed;
            float speed = debugFreeMoveSpeed *
                (fast ? debugFreeFastMultiplier : 1f);
            transform.position += movement * speed * Time.unscaledDeltaTime;
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            transform.position += Vector3.up * (scroll * debugFreeScrollSpeed);
        }
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
            else if (!buildMode && mouse.leftButton.wasPressedThisFrame &&
                     (EventSystem.current == null ||
                      !EventSystem.current.IsPointerOverGameObject()))
                _panDragButton = 2;
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
        Plane ground = new Plane(Vector3.up,
            new Vector3(0f, GetGroundPlaneY(), 0f));
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
        position.y = Mathf.Clamp(position.y,
            DebugFreeMinimumHeight, DebugFreeMaximumHeight);
        transform.position = position;
        EnsureDebugFreeClampAnchor();
        if (!_movementClampEnabled) return;

        GetPlanarMovementBounds(out float minX, out float maxX, out float minZ,
            out float maxZ, out _);

        // Keep one logical ground anchor for the whole free-view session. Recomputing
        // the screen-center ground hit every frame is singular near the horizon: at
        // height 65 and pitch 1 degree it is roughly 3,724 units away, then vanishes
        // as soon as the view crosses above the horizon. That made the two clamp
        // branches move the camera thousands of units in consecutive frames.
        float anchorX = position.x - _debugFreeClampAnchorOffsetXZ.x;
        float anchorZ = position.z - _debugFreeClampAnchorOffsetXZ.y;
        position.x += Mathf.Clamp(anchorX, minX, maxX) - anchorX;
        position.z += Mathf.Clamp(anchorZ, minZ, maxZ) - anchorZ;
        transform.position = position;
    }

    private void EnsureDebugFreeClampAnchor()
    {
        if (_debugFreeClampAnchorInitialized) return;

        Vector3 position = transform.position;
        position.y = Mathf.Clamp(position.y,
            DebugFreeMinimumHeight, DebugFreeMaximumHeight);
        transform.position = position;
        ViewState state = new ViewState
        {
            Position = position,
            Rotation = transform.rotation
        };
        if (TryGetViewStateGroundCenter(state, GetGroundPlaneY(),
                out Vector3 groundCenter))
        {
            _debugFreeClampAnchorOffsetXZ = new Vector2(
                position.x - groundCenter.x,
                position.z - groundCenter.z);
        }
        else
        {
            _debugFreeClampAnchorOffsetXZ = Vector2.zero;
        }
        _debugFreeClampAnchorInitialized = true;
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

    private float GetGroundPlaneY()
    {
        return movementBounds != null ? movementBounds.WorldBounds.center.y : 0f;
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

    private static bool TryGetViewStateGroundCenter(ViewState state, float groundPlaneY,
        out Vector3 groundCenter)
    {
        Vector3 direction = state.Rotation * Vector3.forward;
        if (Mathf.Abs(direction.y) > 0.0001f)
        {
            float distance = (groundPlaneY - state.Position.y) / direction.y;
            if (distance >= 0f)
            {
                groundCenter = state.Position + direction * distance;
                return true;
            }
        }
        groundCenter = default;
        return false;
    }

    private void OnValidate()
    {
        fallbackBoundsSize.x = Mathf.Max(1f, fallbackBoundsSize.x);
        fallbackBoundsSize.y = Mathf.Max(1f, fallbackBoundsSize.y);
        debugFreeScrollSpeed = Mathf.Max(0.001f, debugFreeScrollSpeed);
        cameraViewTransitionDuration = Mathf.Clamp(cameraViewTransitionDuration, 0.1f, 2f);
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
