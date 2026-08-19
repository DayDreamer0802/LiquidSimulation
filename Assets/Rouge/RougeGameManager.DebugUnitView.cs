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

        // Consume normal tower/build mouse input while the free camera owns it.
        return _debugUnitViewMode;
    }

    private void EnterDebugUnitView()
    {
        if (_towerPlacementMode) SetTowerPlacementMode(false);
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
        return "DEBUG FREE CAMERA  |  F1 EXIT  |  WASD MOVE  |  MOUSE LOOK  |  SPACE/E UP  |  CTRL/Q DOWN  |  SHIFT FAST";
    }
}
