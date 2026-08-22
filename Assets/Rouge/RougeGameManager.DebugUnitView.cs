using UnityEngine;
using UnityEngine.InputSystem;

public partial class RougeGameManager
{
    private bool _debugUnitViewMode;

    private bool UpdateDebugUnitViewInput(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
        {
            if (_debugUnitViewMode) ExitDebugUnitView();
            else EnterDebugUnitView();
            return true;
        }

        // Free camera movement and tower placement can run together. Right mouse owns
        // camera look while it is held; regular tower input remains available otherwise.
        return false;
    }

    private void EnterDebugUnitView()
    {
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow == null) return;
        _debugUnitViewMode = true;
        follow.BeginDebugFreeView();
        RefreshTowerDefenseUi(true);
    }

    private void ExitDebugUnitView()
    {
        _debugUnitViewMode = false;
        RougeCameraFollow follow = ResolveDebugCameraFollow();
        if (follow != null) follow.EndDebugFreeView();
        RefreshTowerDefenseUi(true);
    }

    private static RougeCameraFollow ResolveDebugCameraFollow()
    {
        Camera camera = RougeCameraFollow.ResolveCamera();
        return camera != null ? camera.GetComponent<RougeCameraFollow>() : null;
    }

    private static string GetDebugUnitViewStatusText()
    {
        return "DEBUG FREE CAMERA  |  F1 EXIT  |  WASD MOVE  |  RMB LOOK  |  SPACE/E UP  |  CTRL/Q DOWN  |  SHIFT FAST";
    }
}
