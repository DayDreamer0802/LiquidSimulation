using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class RougeGameManager
{
    private enum CameraViewMode
    {
        Default,
        Free,
        TiltShift,
        TopDown
    }

    private bool _debugUnitViewMode;
    private CameraViewMode _cameraViewMode;
    private CameraViewMode _observationExitViewMode;
    private bool _tiltShiftObservationActive;
    private bool _tiltShiftObservationExiting;
    private bool _cameraViewTransitionPaused;
    private float _cameraTransitionRestoreTimeScale = 1f;
    private RougeCameraFollow.ViewState _observationExitView;
    private RougeDefenseTower _observationPendingTowerSelection;
    private readonly List<GameObject> _observationHiddenUiObjects = new List<GameObject>();

    private bool IsTiltShiftObservationActive => _tiltShiftObservationActive;
    private bool IsCameraViewTransitionPaused => _cameraViewTransitionPaused;

    private bool UpdateCameraViewInput(Keyboard keyboard)
    {
        if (keyboard == null) return false;
        if (_cameraViewTransitionPaused) return true;

        if (_tiltShiftObservationActive)
        {
            if (_tiltShiftObservationExiting) return false;
            if (keyboard.f1Key.wasPressedThisFrame)
            {
                BeginTiltShiftObservationExit(CameraViewMode.Free);
                return true;
            }
            if (keyboard.f2Key.wasPressedThisFrame)
            {
                BeginTiltShiftObservationExit(CameraViewMode.Default);
                return true;
            }
            if (keyboard.f3Key.wasPressedThisFrame)
            {
                BeginTiltShiftObservationExit(CameraViewMode.TopDown);
                return true;
            }
            return false;
        }

        if (keyboard.f1Key.wasPressedThisFrame)
        {
            SetCameraViewMode(_cameraViewMode == CameraViewMode.Free
                ? CameraViewMode.Default
                : CameraViewMode.Free);
            return true;
        }

        if (keyboard.f2Key.wasPressedThisFrame)
        {
            SetCameraViewMode(CameraViewMode.TiltShift);
            return true;
        }

        if (keyboard.f3Key.wasPressedThisFrame)
        {
            SetCameraViewMode(_cameraViewMode == CameraViewMode.TopDown
                ? CameraViewMode.Default
                : CameraViewMode.TopDown);
            return true;
        }

        return false;
    }

    private void ExitDebugUnitView()
    {
        if (_tiltShiftObservationActive)
        {
            ForceTiltShiftObservationExit(CameraViewMode.Default);
            return;
        }
        if (_cameraViewTransitionPaused)
        {
            ForceCameraViewMode(CameraViewMode.Default);
            return;
        }
        SetCameraViewMode(CameraViewMode.Default);
    }

    private void SetCameraViewMode(CameraViewMode mode)
    {
        if (_cameraViewTransitionPaused) return;
        if (mode == CameraViewMode.TiltShift)
        {
            BeginTiltShiftObservation();
            return;
        }
        if (_tiltShiftObservationActive)
        {
            BeginTiltShiftObservationExit(mode);
            return;
        }

        if (_cameraViewMode == mode) return;
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow == null)
        {
            _cameraViewMode = mode;
            _debugUnitViewMode = mode == CameraViewMode.Free;
            ShowCameraModeToast(mode);
            RefreshTowerDefenseControlsHud();
            return;
        }

        RougeCameraFollow.ViewState targetView = ResolveCameraViewPreset(mode, follow);
        follow.EndCinematicFocus();
        ApplyImmediateCameraViewMode(_cameraViewMode, mode, targetView.Rotation);
        PrepareCameraPresetTransition(follow, mode, targetView);
        follow.TransitionAndReleaseScriptedView(targetView);
        _cameraViewMode = mode;
        _debugUnitViewMode = mode == CameraViewMode.Free;
        ShowCameraModeToast(mode);
        BeginCameraViewTransitionPause();
        RefreshTowerDefenseControlsHud();
    }

    private void BeginTiltShiftObservation()
    {
        if (_tiltShiftObservationActive || _cameraViewTransitionPaused) return;

        RougeCameraFollow follow = ResolveDebugCameraFollow();
        RougeTiltShiftCamera tiltShift = ResolveTiltShiftCamera();
        if (tiltShift == null) return;

        _observationExitViewMode = CameraViewMode.Default;
        _observationPendingTowerSelection = null;
        HideF2MainTowerHealth();
        if (_towerDefenseInitialized) SetTowerPlacementMode(false);

        RougeTowerDefenseMap map = _towerDefenseLevel != null
            ? _towerDefenseLevel
            : RougeTowerDefenseMapLoader.ActiveMap;

        if (follow != null)
        {
            RougeCameraFollow.ViewState observationView =
                ResolveCameraViewPreset(CameraViewMode.TiltShift, follow);
            follow.EndCinematicFocus();
            ApplyImmediateCameraViewMode(_cameraViewMode, CameraViewMode.TiltShift,
                observationView.Rotation);
            PrepareCameraPresetTransition(follow, CameraViewMode.TiltShift,
                observationView);
            follow.BeginScriptedView(observationView);
        }

        if (map != null) tiltShift.ApplySettings(map.TiltShiftSettings);
        tiltShift.ClearWorldFocusPoint();
        tiltShift.SetEffectEnabled(true);
        _cameraViewMode = CameraViewMode.TiltShift;
        _debugUnitViewMode = false;
        _tiltShiftObservationActive = true;
        _tiltShiftObservationExiting = false;
        HideObservationUi();
        ShowCameraModeToast(CameraViewMode.TiltShift);
        BeginCameraViewTransitionPause();
        if (follow == null) EndCameraViewTransitionPause();
    }

    private void BeginTiltShiftObservationExit(CameraViewMode destination,
        RougeDefenseTower towerToSelect = null)
    {
        if (!_tiltShiftObservationActive || _tiltShiftObservationExiting) return;
        if (destination == CameraViewMode.TiltShift) destination = CameraViewMode.Default;

        _observationExitViewMode = destination;
        _observationPendingTowerSelection = towerToSelect;
        HideF2MainTowerHealth();
        BeginCameraViewTransitionPause();
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow == null)
        {
            CompleteTiltShiftObservationExit();
            return;
        }

        _observationExitView = ResolveCameraViewPreset(destination, follow);
        follow.EndCinematicFocus();
        ApplyImmediateCameraViewMode(CameraViewMode.TiltShift, destination,
            _observationExitView.Rotation);
        PrepareCameraPresetTransition(follow, destination, _observationExitView);
        follow.TransitionAndReleaseScriptedView(_observationExitView);
        _tiltShiftObservationExiting = true;
    }

    private void ForceTiltShiftObservationExit(CameraViewMode destination)
    {
        if (!_tiltShiftObservationActive) return;
        if (destination == CameraViewMode.TiltShift) destination = CameraViewMode.Default;
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow != null)
        {
            follow.CancelScriptedView();
            _observationExitView = ResolveCameraViewPreset(destination, follow);
            ApplyImmediateCameraViewMode(CameraViewMode.TiltShift, destination,
                _observationExitView.Rotation);
            PrepareCameraPresetTransition(follow, destination, _observationExitView);
            follow.ApplyViewState(_observationExitView);
        }
        _observationExitViewMode = destination;
        CompleteTiltShiftObservationExit();
    }

    private bool UpdateCameraViewTransition()
    {
        if (!_cameraViewTransitionPaused) return false;
        Time.timeScale = 0f;

        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow != null && follow.IsScriptedViewTransitioning) return true;

        if (_tiltShiftObservationActive && _tiltShiftObservationExiting)
            CompleteTiltShiftObservationExit();
        else
            EndCameraViewTransitionPause();
        return true;
    }

    private void CompleteTiltShiftObservationExit()
    {
        HideF2MainTowerHealth();
        RougeTiltShiftCamera tiltShift = ResolveTiltShiftCamera();
        if (tiltShift != null)
        {
            tiltShift.SetEffectEnabled(false);
            tiltShift.ClearWorldFocusPoint();
        }

        _cameraViewMode = _observationExitViewMode;
        _debugUnitViewMode = _cameraViewMode == CameraViewMode.Free;
        _tiltShiftObservationActive = false;
        _tiltShiftObservationExiting = false;
        RestoreObservationUi();
        ShowCameraModeToast(_cameraViewMode);
        ShowPendingTowerDefenseAutoplayReleaseToast();
        EndCameraViewTransitionPause();

        RougeDefenseTower tower = _observationPendingTowerSelection;
        _observationPendingTowerSelection = null;
        if (tower != null && _towerDefenseInitialized && !_towerDefenseGameOver)
            EnterTowerEditMode(tower);
        RefreshTowerDefenseUi(true);
    }

    private void BeginCameraViewTransitionPause()
    {
        if (!_cameraViewTransitionPaused)
            _cameraTransitionRestoreTimeScale = Time.timeScale;
        _cameraViewTransitionPaused = true;
        Time.timeScale = 0f;
    }

    private void EndCameraViewTransitionPause()
    {
        _cameraViewTransitionPaused = false;
        if (_towerDefenseInitialized)
        {
            if (_towerDefenseGameOver) Time.timeScale = 0f;
            else ApplyTowerDefenseTimeScale();
        }
        else
            Time.timeScale = _cameraTransitionRestoreTimeScale;
    }

    private void ForceCameraViewMode(CameraViewMode destination)
    {
        if (destination == CameraViewMode.TiltShift) destination = CameraViewMode.Default;
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow != null)
        {
            follow.CancelScriptedView();
            RougeCameraFollow.ViewState targetView =
                ResolveCameraViewPreset(destination, follow);
            ApplyImmediateCameraViewMode(_cameraViewMode, destination,
                targetView.Rotation);
            PrepareCameraPresetTransition(follow, destination, targetView);
            follow.ApplyViewState(targetView);
        }
        _cameraViewMode = destination;
        _debugUnitViewMode = destination == CameraViewMode.Free;
        ShowCameraModeToast(destination);
        EndCameraViewTransitionPause();
        RefreshTowerDefenseUi(true);
    }

    private static void ApplyImmediateCameraViewMode(CameraViewMode source,
        CameraViewMode destination, Quaternion destinationRotation)
    {
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow == null || source == destination) return;

        if (source == CameraViewMode.Free) follow.EndDebugFreeView();
        if (source == CameraViewMode.TopDown) follow.EndTopDownView();
        if (destination == CameraViewMode.Free)
            follow.BeginDebugFreeView(destinationRotation);
        if (destination == CameraViewMode.TopDown) follow.BeginTopDownView();
    }

    private RougeCameraFollow.ViewState ResolveCameraViewPreset(CameraViewMode mode,
        RougeCameraFollow follow)
    {
        RougeTowerDefenseMap map = _towerDefenseLevel != null
            ? _towerDefenseLevel
            : RougeTowerDefenseMapLoader.ActiveMap;
        RougeCameraViewPreset preset = map != null
            ? map.GetCameraPreset(ToCameraPresetMode(mode))
            : default;
        RougeCameraFollow.ViewState state;
        if (preset.Configured)
        {
            state = preset.ToViewState();
        }
        else
        {
            RougeCameraViewPreset defaultPreset = map != null
                ? map.DefaultCameraView
                : default;
            RougeCameraFollow.ViewState defaultState = defaultPreset.Configured
                ? defaultPreset.ToViewState()
                : follow.BuildTiltShiftObservationView(map != null
                    ? map.DefaultCameraPositionXZ
                    : new Vector2(follow.transform.position.x,
                        follow.transform.position.z));
            switch (mode)
            {
                case CameraViewMode.Free:
                    state = defaultState;
                    state.Orthographic = false;
                    state.FieldOfView = 75f;
                    state.NearClipPlane = 0.03f;
                    break;
                case CameraViewMode.TiltShift:
                    state = follow.BuildTiltShiftObservationView(map != null
                        ? map.TiltShiftCameraPositionXZ
                        : new Vector2(defaultState.Position.x, defaultState.Position.z));
                    break;
                case CameraViewMode.TopDown:
                    state = follow.BuildTopDownViewState(defaultState);
                    break;
                default:
                    state = defaultState;
                    break;
            }
        }

        if (mode == CameraViewMode.Free)
        {
            state = follow.ClampDebugFreeViewState(state);
        }
        else if (mode == CameraViewMode.Default || mode == CameraViewMode.TopDown)
        {
            cameraZoomMultiplier = follow.ClampZoomScale(cameraZoomMultiplier);
            state = follow.ApplySharedZoomToViewState(state, cameraZoomMultiplier);
            state = follow.ClampViewStateToMovementBounds(state);
        }
        return state;
    }

    private void PrepareCameraPresetTransition(RougeCameraFollow follow,
        CameraViewMode destination, RougeCameraFollow.ViewState targetView)
    {
        if (follow == null) return;
        follow.SetViewBaseline(targetView);
        if (destination == CameraViewMode.Default ||
            destination == CameraViewMode.TopDown)
            follow.SetSharedZoomAsApplied(cameraZoomMultiplier);
    }

    private static RougeCameraPresetMode ToCameraPresetMode(CameraViewMode mode)
    {
        switch (mode)
        {
            case CameraViewMode.Free: return RougeCameraPresetMode.Free;
            case CameraViewMode.TiltShift: return RougeCameraPresetMode.TiltShift;
            case CameraViewMode.TopDown: return RougeCameraPresetMode.TopDown;
            default: return RougeCameraPresetMode.Default;
        }
    }

    private void HideObservationUi()
    {
        _observationHiddenUiObjects.Clear();
        var seen = new HashSet<GameObject>();
        Canvas[] canvases = FindObjectsByType<Canvas>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.gameObject.activeInHierarchy ||
                canvas == _towerDefenseAutoplayCanvas ||
                canvas == _towerDefenseLevelEventCanvas ||
                canvas.GetComponent<RougeCameraModeToast>() != null ||
                !seen.Add(canvas.gameObject)) continue;
            _observationHiddenUiObjects.Add(canvas.gameObject);
        }

        RougeFloatingWorldText[] floatingTexts = FindObjectsByType<RougeFloatingWorldText>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < floatingTexts.Length; i++)
        {
            GameObject floatingObject = floatingTexts[i] != null
                ? floatingTexts[i].gameObject
                : null;
            if (floatingObject == null || !floatingObject.activeInHierarchy ||
                !seen.Add(floatingObject)) continue;
            _observationHiddenUiObjects.Add(floatingObject);
        }

        for (int i = 0; i < _observationHiddenUiObjects.Count; i++)
            if (_observationHiddenUiObjects[i] != null)
                _observationHiddenUiObjects[i].SetActive(false);
    }

    private void RestoreObservationUi()
    {
        for (int i = 0; i < _observationHiddenUiObjects.Count; i++)
            if (_observationHiddenUiObjects[i] != null)
                _observationHiddenUiObjects[i].SetActive(true);
        _observationHiddenUiObjects.Clear();
    }

    private static RougeCameraFollow ResolveDebugCameraFollow()
    {
        Camera camera = RougeCameraFollow.ResolveCamera();
        return camera != null ? camera.GetComponent<RougeCameraFollow>() : null;
    }

    private static RougeTiltShiftCamera ResolveTiltShiftCamera()
    {
        Camera camera = RougeCameraFollow.ResolveCamera();
        return camera != null ? camera.GetComponent<RougeTiltShiftCamera>() : null;
    }

    private static void ShowCameraModeToast(CameraViewMode mode)
    {
        switch (mode)
        {
            case CameraViewMode.Free:
                RougeCameraModeToast.Show("自由镜头", new Color(0.30f, 1f, 0.68f, 1f));
                break;
            case CameraViewMode.TiltShift:
                RougeCameraModeToast.Show("移轴观赏", new Color(0.75f, 0.42f, 1f, 1f));
                break;
            case CameraViewMode.TopDown:
                RougeCameraModeToast.Show("垂直俯视", new Color(0.22f, 0.72f, 1f, 1f));
                break;
            default:
                RougeCameraModeToast.Show("默认镜头", new Color(0.08f, 0.82f, 1f, 1f));
                break;
        }
    }

}
