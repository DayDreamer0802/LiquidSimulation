using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum RougeInputBinding
{
    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    PrimaryAttack,
    LeapSmash,
    LightPillarStrike,
    BombThrow,
    LaserBeam,
    Shockwave,
    MeteorRain,
    IceZone,
    PoisonBottle,
    Dash
}

[DefaultExecutionOrder(-1000)]
public sealed class RougeInputManager : MonoBehaviour
{
    private const string BindingOverridesPrefsKey = "Rouge.Input.BindingOverrides";

    private static RougeInputManager s_instance;

    private readonly Dictionary<RougeInputBinding, InputBindingRef> _bindingRefs = new Dictionary<RougeInputBinding, InputBindingRef>();
    private InputActionMap _gameplayMap;
    private InputAction _moveAction;
    private InputAction _pointerAction;
    private InputActionRebindingExtensions.RebindingOperation _rebindOperation;

    private struct InputBindingRef
    {
        public InputAction Action;
        public int BindingIndex;
    }

    public event Action<RougeInputBinding> BindingChanged;
    public event Action<RougeInputBinding> RebindStarted;
    public event Action<RougeInputBinding> RebindCanceled;

    public static RougeInputManager Instance
    {
        get
        {
            EnsureInstance();
            return s_instance;
        }
    }

    public bool IsRebinding => _rebindOperation != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static bool TryGetBinding(PlayerSkillType type, out RougeInputBinding binding)
    {
        switch (type)
        {
            case PlayerSkillType.LeapSmash:
                binding = RougeInputBinding.LeapSmash;
                return true;
            case PlayerSkillType.LightPillarStrike:
                binding = RougeInputBinding.LightPillarStrike;
                return true;
            case PlayerSkillType.BombThrow:
                binding = RougeInputBinding.BombThrow;
                return true;
            case PlayerSkillType.LaserBeam:
                binding = RougeInputBinding.LaserBeam;
                return true;
            case PlayerSkillType.MeleeSlash:
                binding = RougeInputBinding.PrimaryAttack;
                return true;
            case PlayerSkillType.Shockwave:
                binding = RougeInputBinding.Shockwave;
                return true;
            case PlayerSkillType.MeteorRain:
                binding = RougeInputBinding.MeteorRain;
                return true;
            case PlayerSkillType.IceZone:
                binding = RougeInputBinding.IceZone;
                return true;
            case PlayerSkillType.PoisonBottle:
                binding = RougeInputBinding.PoisonBottle;
                return true;
            case PlayerSkillType.Dash:
                binding = RougeInputBinding.Dash;
                return true;
            default:
                binding = default;
                return false;
        }
    }

    public Vector2 ReadMoveVector()
    {
        return _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
    }

    public Vector2 ReadPointerPosition()
    {
        if (_pointerAction != null)
        {
            return _pointerAction.ReadValue<Vector2>();
        }

        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    }

    public bool WasPressedThisFrame(RougeInputBinding binding)
    {
        return TryGetAction(binding, out InputAction action) && action.WasPressedThisFrame();
    }

    public bool IsPressed(RougeInputBinding binding)
    {
        return TryGetAction(binding, out InputAction action) && action.IsPressed();
    }

    public string GetBindingDisplayString(RougeInputBinding binding)
    {
        if (!_bindingRefs.TryGetValue(binding, out InputBindingRef bindingRef))
        {
            return "UNBOUND";
        }

        if (bindingRef.BindingIndex < 0 || bindingRef.BindingIndex >= bindingRef.Action.bindings.Count)
        {
            return "UNBOUND";
        }

        string displayString = bindingRef.Action.GetBindingDisplayString(bindingRef.BindingIndex, InputBinding.DisplayStringOptions.DontUseShortDisplayNames);
        return NormalizeBindingDisplayString(displayString);
    }

    public bool StartInteractiveRebind(RougeInputBinding binding, Action<string> onComplete = null, Action onCancel = null)
    {
        if (!_bindingRefs.TryGetValue(binding, out InputBindingRef bindingRef))
        {
            return false;
        }

        CancelInteractiveRebind();
        _gameplayMap.Disable();
        RebindStarted?.Invoke(binding);

        _rebindOperation = bindingRef.Action.PerformInteractiveRebinding(bindingRef.BindingIndex)
            .WithExpectedControlType("Button")
            .WithCancelingThrough("<Keyboard>/escape")
            .WithControlsExcluding("<Pointer>/position")
            .WithControlsExcluding("<Pointer>/delta")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .OnCancel(_ =>
            {
                FinishInteractiveRebind();
                RebindCanceled?.Invoke(binding);
                onCancel?.Invoke();
            })
            .OnComplete(_ =>
            {
                SaveBindingOverrides();
                FinishInteractiveRebind();
                BindingChanged?.Invoke(binding);
                onComplete?.Invoke(GetBindingDisplayString(binding));
            });

        _rebindOperation.Start();
        return true;
    }

    public void CancelInteractiveRebind()
    {
        if (_rebindOperation == null)
        {
            return;
        }

        _rebindOperation.Cancel();
    }

    public void ResetBinding(RougeInputBinding binding)
    {
        if (!_bindingRefs.TryGetValue(binding, out InputBindingRef bindingRef))
        {
            return;
        }

        bindingRef.Action.RemoveBindingOverride(bindingRef.BindingIndex);
        SaveBindingOverrides();
        BindingChanged?.Invoke(binding);
    }

    public void ResetAllBindings()
    {
        if (_gameplayMap == null)
        {
            return;
        }

        _gameplayMap.RemoveAllBindingOverrides();
        SaveBindingOverrides();

        foreach (RougeInputBinding binding in _bindingRefs.Keys)
        {
            BindingChanged?.Invoke(binding);
        }
    }

    public void ApplySkillPresentationDefaults(PlayerSkillConfigSet skillConfig)
    {
        if (skillConfig == null)
        {
            return;
        }

        ApplyDefaultBinding(RougeInputBinding.LeapSmash, skillConfig.LeapSmash.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.LightPillarStrike, skillConfig.LightPillar.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.BombThrow, skillConfig.BombThrow.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.LaserBeam, skillConfig.LaserBeam.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.Shockwave, skillConfig.Shockwave.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.MeteorRain, skillConfig.MeteorRain.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.IceZone, skillConfig.IceZone.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.PoisonBottle, skillConfig.PoisonBottle.Presentation.ActivationKey);
        ApplyDefaultBinding(RougeInputBinding.Dash, skillConfig.Dash.Presentation.ActivationKey);
    }

    private static void EnsureInstance()
    {
        if (s_instance != null)
        {
            return;
        }

        s_instance = FindFirstObjectByType<RougeInputManager>();
        if (s_instance != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("Rouge Input Manager");
        s_instance = managerObject.AddComponent<RougeInputManager>();
        DontDestroyOnLoad(managerObject);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        DontDestroyOnLoad(gameObject);
        BuildActions();
        LoadBindingOverrides();
    }

    private void OnEnable()
    {
        _gameplayMap?.Enable();
    }

    private void OnDisable()
    {
        _gameplayMap?.Disable();
        DisposeRebindOperation();
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }

        DisposeRebindOperation();
        _gameplayMap?.Dispose();
        _gameplayMap = null;
    }

    private void BuildActions()
    {
        if (_gameplayMap != null)
        {
            return;
        }

        _gameplayMap = new InputActionMap("Gameplay");

        _moveAction = _gameplayMap.AddAction("Move", InputActionType.Value);
        _moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        _pointerAction = _gameplayMap.AddAction("PointerPosition", InputActionType.PassThrough, "<Pointer>/position");

        AddButtonAction(RougeInputBinding.PrimaryAttack, "PrimaryAttack", "<Mouse>/leftButton");
        AddButtonAction(RougeInputBinding.LeapSmash, "LeapSmash", "<Keyboard>/space");
        AddButtonAction(RougeInputBinding.LightPillarStrike, "LightPillarStrike", "<Keyboard>/q");
        AddButtonAction(RougeInputBinding.BombThrow, "BombThrow", "<Keyboard>/e");
        AddButtonAction(RougeInputBinding.LaserBeam, "LaserBeam", "<Keyboard>/r");
        AddButtonAction(RougeInputBinding.Shockwave, "Shockwave", "<Keyboard>/v");
        AddButtonAction(RougeInputBinding.MeteorRain, "MeteorRain", "<Keyboard>/t");
        AddButtonAction(RougeInputBinding.IceZone, "IceZone", "<Keyboard>/c");
        AddButtonAction(RougeInputBinding.PoisonBottle, "PoisonBottle", "<Keyboard>/x");
        AddButtonAction(RougeInputBinding.Dash, "Dash", "<Keyboard>/leftShift");

        RegisterBinding(RougeInputBinding.MoveUp, _moveAction, FindBindingIndex(_moveAction, "Up"));
        RegisterBinding(RougeInputBinding.MoveDown, _moveAction, FindBindingIndex(_moveAction, "Down"));
        RegisterBinding(RougeInputBinding.MoveLeft, _moveAction, FindBindingIndex(_moveAction, "Left"));
        RegisterBinding(RougeInputBinding.MoveRight, _moveAction, FindBindingIndex(_moveAction, "Right"));

        _gameplayMap.Enable();
    }

    private void AddButtonAction(RougeInputBinding binding, string actionName, string defaultPath)
    {
        InputAction action = _gameplayMap.AddAction(actionName, InputActionType.Button);
        action.AddBinding(defaultPath);
        RegisterBinding(binding, action, 0);
    }

    private void RegisterBinding(RougeInputBinding binding, InputAction action, int bindingIndex)
    {
        _bindingRefs[binding] = new InputBindingRef
        {
            Action = action,
            BindingIndex = bindingIndex
        };
    }

    private static int FindBindingIndex(InputAction action, string bindingName)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            if (string.Equals(action.bindings[i].name, bindingName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryGetAction(RougeInputBinding binding, out InputAction action)
    {
        if (_bindingRefs.TryGetValue(binding, out InputBindingRef bindingRef))
        {
            action = bindingRef.Action;
            return action != null;
        }

        action = null;
        return false;
    }

    private void ApplyDefaultBinding(RougeInputBinding binding, KeyCode keyCode)
    {
        if (!_bindingRefs.TryGetValue(binding, out InputBindingRef bindingRef))
        {
            return;
        }

        string path = ConvertKeyCodeToPath(keyCode);
        if (string.IsNullOrEmpty(path) || bindingRef.BindingIndex < 0)
        {
            return;
        }

        if (bindingRef.Action.bindings[bindingRef.BindingIndex].path == path)
        {
            return;
        }

        bindingRef.Action.ChangeBinding(bindingRef.BindingIndex).WithPath(path);
    }

    private void LoadBindingOverrides()
    {
        if (_gameplayMap == null)
        {
            return;
        }

        string json = PlayerPrefs.GetString(BindingOverridesPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        _gameplayMap.LoadBindingOverridesFromJson(json);
    }

    private void SaveBindingOverrides()
    {
        if (_gameplayMap == null)
        {
            return;
        }

        string json = _gameplayMap.SaveBindingOverridesAsJson();
        PlayerPrefs.SetString(BindingOverridesPrefsKey, json);
        PlayerPrefs.Save();
    }

    private void FinishInteractiveRebind()
    {
        DisposeRebindOperation();
        _gameplayMap?.Enable();
    }

    private void DisposeRebindOperation()
    {
        if (_rebindOperation == null)
        {
            return;
        }

        _rebindOperation.Dispose();
        _rebindOperation = null;
    }

    private static string ConvertKeyCodeToPath(KeyCode keyCode)
    {
        switch (keyCode)
        {
            case KeyCode.None:
                return null;
            case KeyCode.Space:
                return "<Keyboard>/space";
            case KeyCode.LeftShift:
                return "<Keyboard>/leftShift";
            case KeyCode.RightShift:
                return "<Keyboard>/rightShift";
            case KeyCode.LeftControl:
                return "<Keyboard>/leftCtrl";
            case KeyCode.RightControl:
                return "<Keyboard>/rightCtrl";
            case KeyCode.LeftAlt:
                return "<Keyboard>/leftAlt";
            case KeyCode.RightAlt:
                return "<Keyboard>/rightAlt";
            case KeyCode.Tab:
                return "<Keyboard>/tab";
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                return "<Keyboard>/enter";
            case KeyCode.Escape:
                return "<Keyboard>/escape";
            case KeyCode.Backspace:
                return "<Keyboard>/backspace";
            case KeyCode.Mouse0:
                return "<Mouse>/leftButton";
            case KeyCode.Mouse1:
                return "<Mouse>/rightButton";
            case KeyCode.Mouse2:
                return "<Mouse>/middleButton";
        }

        if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
        {
            return "<Keyboard>/" + keyCode.ToString().ToLowerInvariant();
        }

        if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
        {
            int value = keyCode - KeyCode.Alpha0;
            return "<Keyboard>/" + value;
        }

        if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
        {
            return "<Keyboard>/" + keyCode.ToString().ToLowerInvariant();
        }

        return null;
    }

    private static string NormalizeBindingDisplayString(string displayString)
    {
        if (string.IsNullOrWhiteSpace(displayString))
        {
            return "UNBOUND";
        }

        string normalized = displayString.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "LEFT SHIFT":
                return "L-SHIFT";
            case "RIGHT SHIFT":
                return "R-SHIFT";
            case "LEFT CTRL":
            case "LEFT CONTROL":
                return "L-CTRL";
            case "RIGHT CTRL":
            case "RIGHT CONTROL":
                return "R-CTRL";
            case "LEFT ALT":
                return "L-ALT";
            case "RIGHT ALT":
                return "R-ALT";
            case "LEFT BUTTON":
            case "LEFT MOUSE BUTTON":
            case "LMB":
                return "MOUSE L-CLICK";
            case "RIGHT BUTTON":
            case "RIGHT MOUSE BUTTON":
            case "RMB":
                return "MOUSE R-CLICK";
            case "MIDDLE BUTTON":
            case "MIDDLE MOUSE BUTTON":
            case "MMB":
                return "MOUSE M-CLICK";
            default:
                return normalized;
        }
    }
}