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

    private bool UpdateCameraViewInput(Keyboard keyboard)
    {
        if (keyboard == null) return false;

        if (keyboard.f1Key.wasPressedThisFrame)
        {
            SetCameraViewMode(_cameraViewMode == CameraViewMode.Free
                ? CameraViewMode.Default
                : CameraViewMode.Free);
            return true;
        }

        if (keyboard.f2Key.wasPressedThisFrame)
        {
            SetCameraViewMode(_cameraViewMode == CameraViewMode.TiltShift
                ? CameraViewMode.Default
                : CameraViewMode.TiltShift);
            return true;
        }

        if (keyboard.f3Key.wasPressedThisFrame)
        {
            SetCameraViewMode(_cameraViewMode == CameraViewMode.TopDown
                ? CameraViewMode.Default
                : CameraViewMode.TopDown);
            return true;
        }

        // Free camera movement and tower placement can run together. Right mouse owns
        // camera look while it is held; regular tower input remains available otherwise.
        return false;
    }

    private void ExitDebugUnitView()
    {
        SetCameraViewMode(CameraViewMode.Default);
    }

    private void SetCameraViewMode(CameraViewMode mode)
    {
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        RougeTiltShiftCamera tiltShift = ResolveTiltShiftCamera();

        if (_cameraViewMode == CameraViewMode.Free && follow != null) follow.EndDebugFreeView();
        if (_cameraViewMode == CameraViewMode.TopDown && follow != null) follow.EndTopDownView();
        if (tiltShift != null) tiltShift.SetEffectEnabled(false);

        _cameraViewMode = CameraViewMode.Default;
        _debugUnitViewMode = false;

        switch (mode)
        {
            case CameraViewMode.Free when follow != null:
                follow.BeginDebugFreeView();
                _cameraViewMode = CameraViewMode.Free;
                _debugUnitViewMode = true;
                break;
            case CameraViewMode.TiltShift when tiltShift != null:
                tiltShift.SetEffectEnabled(true);
                _cameraViewMode = CameraViewMode.TiltShift;
                break;
            case CameraViewMode.TopDown when follow != null:
                follow.BeginTopDownView();
                _cameraViewMode = CameraViewMode.TopDown;
                break;
        }

        RefreshTowerDefenseUi(true);
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

    private string GetCameraViewStatusText()
    {
        switch (_cameraViewMode)
        {
            case CameraViewMode.Free:
                return "自由镜头  |  F1 返回默认  |  WASD 移动  |  右键观察  |  空格/E 上升  |  Ctrl/Q 下降  |  Shift 加速";
            case CameraViewMode.TiltShift:
                return "移轴沙盘镜头  |  F2 返回默认  |  F1 自由镜头  |  F3 垂直俯视";
            case CameraViewMode.TopDown:
                return "垂直俯视镜头  |  F3 返回默认  |  F1 自由镜头  |  F2 移轴镜头";
            default:
                return string.Empty;
        }
    }
}
