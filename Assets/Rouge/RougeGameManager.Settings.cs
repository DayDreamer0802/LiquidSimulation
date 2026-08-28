using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private const string MusicVolumePreference = "Rouge.Audio.MusicVolume";
    private const string SfxVolumePreference = "Rouge.Audio.SfxVolume";
    private const string DamageStatisticsPreference = "Rouge.UI.DamageStatistics";
    private const string TiltBlurPreference = "Rouge.Graphics.TiltBlur";
    private const string TiltCustomBlurPreference = "Rouge.Graphics.TiltCustomBlur";
    private const string TiltWidthPreference = "Rouge.Graphics.TiltClearWidth";
    private const string TargetFrameRatePreference = "Rouge.Graphics.TargetFrameRate";
    private const string DisplayModePreference = "Rouge.Graphics.DisplayMode";
    private const string ResolutionWidthPreference = "Rouge.Graphics.ResolutionWidth";
    private const string ResolutionHeightPreference = "Rouge.Graphics.ResolutionHeight";
    private const string SettingsMenuResourcePath = "UI/RougeSettingsMenu";
    private const string F2HealthResourcePath = "UI/RougeF2MainTowerHealth";
    private const float F2HealthDisplayDuration = 2.2f;
    private const float F2HealthFadeDuration = 0.42f;

    private static readonly int[] TargetFrameRateOptions =
        { 30, 60, 90, 120, 144, 240, -1 };

    private RougeSettingsMenuView _settingsMenuView;
    private RougeF2MainTowerHealthView _f2MainTowerHealthView;
    private readonly List<Vector2Int> _supportedResolutions = new List<Vector2Int>();
    private bool _settingsMenuOpen;
    private bool _showDamageStatistics = true;
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;
    private float _tiltBlurNormalized;
    private bool _customTiltBlurEnabled;
    private float _tiltClearWidthNormalized = 0.5f;
    private int _selectedResolutionIndex;
    private int _selectedDisplayModeIndex = 1;
    private int _selectedTargetFrameRate = -1;
    private float _f2HealthDisplayRemaining;

    private bool IsPlayerSettingsOpen => _settingsMenuOpen;

    private void InitializePlayerSettings()
    {
        _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePreference, 1f));
        _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePreference, 1f));
        _showDamageStatistics = PlayerPrefs.GetInt(DamageStatisticsPreference, 1) != 0;

        float baseBlurRadius = _towerDefenseLevel != null
            ? _towerDefenseLevel.TiltShiftSettings.blurRadius
            : ResolveTiltShiftCamera() != null
                ? ResolveTiltShiftCamera().CaptureSettings().blurRadius
                : 4.5f;
        _tiltBlurNormalized = PlayerPrefs.HasKey(TiltBlurPreference)
            ? Mathf.Clamp01(PlayerPrefs.GetFloat(TiltBlurPreference, 0f))
            : RougeTiltShiftCamera.BlurRadiusToNormalized(baseBlurRadius);
        _customTiltBlurEnabled = PlayerPrefs.GetInt(TiltCustomBlurPreference, 0) != 0;
        _tiltClearWidthNormalized = Mathf.Clamp01(
            PlayerPrefs.GetFloat(TiltWidthPreference, 0.5f));
        ApplyTiltUserSettings();

        _selectedTargetFrameRate = PlayerPrefs.GetInt(TargetFrameRatePreference,
            Application.targetFrameRate > 0 ? Application.targetFrameRate : -1);
        if (GetTargetFrameRateIndex(_selectedTargetFrameRate) < 0)
            _selectedTargetFrameRate = -1;
        ApplyTargetFrameRate(_selectedTargetFrameRate, false);

        BuildSupportedResolutionList();
        _selectedDisplayModeIndex = PlayerPrefs.HasKey(DisplayModePreference)
            ? Mathf.Clamp(PlayerPrefs.GetInt(DisplayModePreference, 1), 0, 2)
            : DisplayModeToIndex(Screen.fullScreenMode);
        ResolveSavedResolutionSelection();
        if (PlayerPrefs.HasKey(DisplayModePreference) ||
            PlayerPrefs.HasKey(ResolutionWidthPreference))
            ApplyResolutionAndDisplay(false);

        ApplyAudioSettings();
        RougeDefenseTower.SetUserSfxVolume(_sfxVolume);
    }

    private void BuildPlayerSettingsUi()
    {
        GameObject settingsPrefab = Resources.Load<GameObject>(SettingsMenuResourcePath);
        if (settingsPrefab != null)
        {
            GameObject instance = Instantiate(settingsPrefab);
            instance.name = "Rouge Settings Menu";
            _settingsMenuView = instance.GetComponent<RougeSettingsMenuView>();
            if (_settingsMenuView != null)
            {
                BindPlayerSettingsUi();
                instance.SetActive(false);
            }
            else
            {
                Debug.LogError("Settings menu prefab is missing RougeSettingsMenuView.", instance);
                Destroy(instance);
            }
        }
        else
            Debug.LogError("Missing Resources/UI/RougeSettingsMenu prefab.");

        GameObject healthPrefab = Resources.Load<GameObject>(F2HealthResourcePath);
        if (healthPrefab == null)
        {
            Debug.LogError("Missing Resources/UI/RougeF2MainTowerHealth prefab.");
            return;
        }
        GameObject healthInstance = Instantiate(healthPrefab);
        healthInstance.name = "F2 Main Tower Health";
        _f2MainTowerHealthView = healthInstance.GetComponent<RougeF2MainTowerHealthView>();
        healthInstance.SetActive(false);
    }

    private void BindPlayerSettingsUi()
    {
        RougeSettingsMenuView view = _settingsMenuView;
        if (view == null) return;
        view.InitializeTabs();
        if (view.closeButton != null) view.closeButton.onClick.AddListener(ClosePlayerSettings);

        ConfigureSlider(view.musicSlider, _musicVolume * 100f, value =>
        {
            _musicVolume = Mathf.Clamp01(value / 100f);
            PlayerPrefs.SetFloat(MusicVolumePreference, _musicVolume);
            ApplyAudioSettings();
            RefreshPlayerSettingsUi();
        });
        ConfigureSlider(view.sfxSlider, _sfxVolume * 100f, value =>
        {
            _sfxVolume = Mathf.Clamp01(value / 100f);
            PlayerPrefs.SetFloat(SfxVolumePreference, _sfxVolume);
            RougeDefenseTower.SetUserSfxVolume(_sfxVolume);
            RefreshPlayerSettingsUi();
        });
        ConfigureSlider(view.tiltBlurSlider, _tiltBlurNormalized * 100f, value =>
        {
            _tiltBlurNormalized = Mathf.Clamp01(value / 100f);
            PlayerPrefs.SetFloat(TiltBlurPreference, _tiltBlurNormalized);
            ApplyTiltUserSettings();
            RefreshPlayerSettingsUi();
        });
        if (view.customTiltBlurButton != null)
            view.customTiltBlurButton.onClick.AddListener(() =>
            {
                _customTiltBlurEnabled = !_customTiltBlurEnabled;
                PlayerPrefs.SetInt(TiltCustomBlurPreference,
                    _customTiltBlurEnabled ? 1 : 0);
                ApplyTiltUserSettings();
                RefreshPlayerSettingsUi();
            });
        ConfigureSlider(view.tiltClearWidthSlider, _tiltClearWidthNormalized, value =>
        {
            _tiltClearWidthNormalized = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(TiltWidthPreference, _tiltClearWidthNormalized);
            ApplyTiltUserSettings();
            RefreshPlayerSettingsUi();
        });

        BindIndexedButtons(view.qualityButtons, index =>
        {
            RougeVisualQualityManager.SetActiveTier((RougeVisualQualityTier)index);
            RefreshPlayerSettingsUi();
            RefreshTowerDefenseUi(true);
        });
        BindIndexedButtons(view.frameRateButtons, index =>
        {
            if ((uint)index >= (uint)TargetFrameRateOptions.Length) return;
            ApplyTargetFrameRate(TargetFrameRateOptions[index], true);
            RefreshPlayerSettingsUi();
        });
        BindIndexedButtons(view.displayModeButtons, index =>
        {
            _selectedDisplayModeIndex = Mathf.Clamp(index, 0, 2);
            ApplyResolutionAndDisplay(true);
            RefreshPlayerSettingsUi();
        });
        if (view.resolutionPreviousButton != null)
            view.resolutionPreviousButton.onClick.AddListener(() => ChangeResolution(-1));
        if (view.resolutionNextButton != null)
            view.resolutionNextButton.onClick.AddListener(() => ChangeResolution(1));
        if (view.damageStatisticsButton != null)
            view.damageStatisticsButton.onClick.AddListener(() =>
            {
                _showDamageStatistics = !_showDamageStatistics;
                PlayerPrefs.SetInt(DamageStatisticsPreference,
                    _showDamageStatistics ? 1 : 0);
                ApplyDamageStatisticsVisibility();
                RefreshPlayerSettingsUi();
            });
        RefreshPlayerSettingsUi();
    }

    private static void ConfigureSlider(Slider slider, float value,
        UnityEngine.Events.UnityAction<float> listener)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(listener);
    }

    private static void BindIndexedButtons(Button[] buttons,
        System.Action<int> callback)
    {
        if (buttons == null || callback == null) return;
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            if (buttons[i] != null) buttons[i].onClick.AddListener(() => callback(index));
        }
    }

    private void OpenPlayerSettings()
    {
        if (_settingsMenuOpen || _settingsMenuView == null) return;
        HideF2MainTowerHealth();
        _settingsMenuOpen = true;
        Time.timeScale = 0f;
        _settingsMenuView.gameObject.SetActive(true);
        _settingsMenuView.ShowTab(0);
        RefreshPlayerSettingsUi();
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (follow != null) follow.SetUserInputBlocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ClosePlayerSettings()
    {
        if (!_settingsMenuOpen) return;
        _settingsMenuOpen = false;
        if (_settingsMenuView != null) _settingsMenuView.gameObject.SetActive(false);
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (follow != null) follow.SetUserInputBlocked(false);
        PlayerPrefs.Save();
        if (_towerDefenseGameOver) Time.timeScale = 0f;
        else ApplyTowerDefenseTimeScale();
    }

    private void RefreshPlayerSettingsUi()
    {
        RougeSettingsMenuView view = _settingsMenuView;
        if (view == null) return;
        if (view.musicSlider != null) view.musicSlider.SetValueWithoutNotify(_musicVolume * 100f);
        if (view.sfxSlider != null) view.sfxSlider.SetValueWithoutNotify(_sfxVolume * 100f);
        if (view.tiltBlurSlider != null)
        {
            view.tiltBlurSlider.SetValueWithoutNotify(_tiltBlurNormalized * 100f);
            view.tiltBlurSlider.interactable = _customTiltBlurEnabled;
        }
        if (view.tiltClearWidthSlider != null)
            view.tiltClearWidthSlider.SetValueWithoutNotify(_tiltClearWidthNormalized);
        if (view.musicValueText != null)
            view.musicValueText.text = $"{Mathf.RoundToInt(_musicVolume * 100f)}";
        if (view.sfxValueText != null)
            view.sfxValueText.text = $"{Mathf.RoundToInt(_sfxVolume * 100f)}";
        if (view.tiltBlurValueText != null)
        {
            float authoredBlur = _towerDefenseLevel != null
                ? _towerDefenseLevel.TiltShiftSettings.blurRadius
                : 4.5f;
            view.tiltBlurValueText.text = _customTiltBlurEnabled
                ? $"{Mathf.RoundToInt(_tiltBlurNormalized * 100f)}  /  半径 {Mathf.Lerp(1f, 26f, _tiltBlurNormalized):0.0}"
                : $"关卡配置  /  半径 {authoredBlur:0.0}";
        }
        if (view.customTiltBlurValueText != null)
            view.customTiltBlurValueText.text = _customTiltBlurEnabled
                ? "自定义"
                : "跟随关卡";
        RougeSettingsMenuView.SetSegmentSelection(
            new[] { view.customTiltBlurButton }, _customTiltBlurEnabled ? 0 : -1);
        if (view.tiltClearWidthValueText != null)
        {
            string baseline = Mathf.Abs(_tiltClearWidthNormalized - 0.5f) < 0.001f
                ? "  关卡基准"
                : $"  ×{_tiltClearWidthNormalized * 2f:0.00}";
            view.tiltClearWidthValueText.text =
                $"{_tiltClearWidthNormalized:0.00}{baseline}";
        }
        RougeSettingsMenuView.SetSegmentSelection(view.qualityButtons,
            (int)RougeVisualQualityManager.ActiveTier);
        RougeSettingsMenuView.SetSegmentSelection(view.frameRateButtons,
            GetTargetFrameRateIndex(_selectedTargetFrameRate));
        RougeSettingsMenuView.SetSegmentSelection(view.displayModeButtons,
            _selectedDisplayModeIndex);
        if (view.resolutionValueText != null && _supportedResolutions.Count > 0)
        {
            Vector2Int resolution = _supportedResolutions[Mathf.Clamp(
                _selectedResolutionIndex, 0, _supportedResolutions.Count - 1)];
            view.resolutionValueText.text = $"{resolution.x} × {resolution.y}";
        }
        if (view.damageStatisticsValueText != null)
            view.damageStatisticsValueText.text = _showDamageStatistics ? "开启" : "关闭";
        RougeSettingsMenuView.SetSegmentSelection(
            new[] { view.damageStatisticsButton }, _showDamageStatistics ? 0 : -1);
    }

    private void ApplyAudioSettings()
    {
        RougeAudioVisualizerPlayer[] players = FindObjectsByType<RougeAudioVisualizerPlayer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
            if (players[i] != null) players[i].SetUserVolume(_musicVolume);
    }

    private void ApplyTiltUserSettings()
    {
        RougeTiltShiftCamera.SetUserAdjustments(_customTiltBlurEnabled,
            _tiltBlurNormalized, _tiltClearWidthNormalized);
    }

    private void ApplyDamageStatisticsVisibility()
    {
        if (_towerDamagePanel != null)
            _towerDamagePanel.SetActive(_showDamageStatistics);
    }

    private void ApplyTargetFrameRate(int frameRate, bool persist)
    {
        _selectedTargetFrameRate = frameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = frameRate;
        if (persist) PlayerPrefs.SetInt(TargetFrameRatePreference, frameRate);
    }

    private static int GetTargetFrameRateIndex(int frameRate)
    {
        for (int i = 0; i < TargetFrameRateOptions.Length; i++)
            if (TargetFrameRateOptions[i] == frameRate) return i;
        return -1;
    }

    private void BuildSupportedResolutionList()
    {
        _supportedResolutions.Clear();
        Resolution[] resolutions = Screen.resolutions;
        for (int i = 0; i < resolutions.Length; i++)
        {
            Vector2Int candidate = new Vector2Int(resolutions[i].width, resolutions[i].height);
            if (candidate.x <= 0 || candidate.y <= 0 || _supportedResolutions.Contains(candidate))
                continue;
            _supportedResolutions.Add(candidate);
        }
        Vector2Int current = new Vector2Int(Screen.width, Screen.height);
        if (!_supportedResolutions.Contains(current)) _supportedResolutions.Add(current);
        _supportedResolutions.Sort((a, b) =>
        {
            long areaCompare = (long)a.x * a.y - (long)b.x * b.y;
            return areaCompare != 0 ? areaCompare < 0 ? -1 : 1 : a.x.CompareTo(b.x);
        });
    }

    private void ResolveSavedResolutionSelection()
    {
        int savedWidth = PlayerPrefs.GetInt(ResolutionWidthPreference, Screen.width);
        int savedHeight = PlayerPrefs.GetInt(ResolutionHeightPreference, Screen.height);
        Vector2Int requested = new Vector2Int(savedWidth, savedHeight);
        _selectedResolutionIndex = _supportedResolutions.IndexOf(requested);
        if (_selectedResolutionIndex >= 0) return;
        _selectedResolutionIndex = Mathf.Max(0,
            _supportedResolutions.IndexOf(new Vector2Int(Screen.width, Screen.height)));
    }

    private void ChangeResolution(int direction)
    {
        if (_supportedResolutions.Count == 0) return;
        _selectedResolutionIndex = Mathf.Clamp(_selectedResolutionIndex + direction,
            0, _supportedResolutions.Count - 1);
        ApplyResolutionAndDisplay(true);
        RefreshPlayerSettingsUi();
    }

    private void ApplyResolutionAndDisplay(bool persist)
    {
        if (_supportedResolutions.Count == 0) return;
        Vector2Int resolution = _supportedResolutions[Mathf.Clamp(_selectedResolutionIndex,
            0, _supportedResolutions.Count - 1)];
        FullScreenMode mode = IndexToDisplayMode(_selectedDisplayModeIndex);
        Screen.SetResolution(resolution.x, resolution.y, mode);
        if (!persist) return;
        PlayerPrefs.SetInt(DisplayModePreference, _selectedDisplayModeIndex);
        PlayerPrefs.SetInt(ResolutionWidthPreference, resolution.x);
        PlayerPrefs.SetInt(ResolutionHeightPreference, resolution.y);
    }

    private static int DisplayModeToIndex(FullScreenMode mode)
    {
        if (mode == FullScreenMode.ExclusiveFullScreen) return 0;
        if (mode == FullScreenMode.Windowed) return 2;
        return 1;
    }

    private static FullScreenMode IndexToDisplayMode(int index)
    {
        if (index == 0) return FullScreenMode.ExclusiveFullScreen;
        if (index == 2) return FullScreenMode.Windowed;
        return FullScreenMode.FullScreenWindow;
    }

    private void ShowF2MainTowerHealth()
    {
        if (_f2MainTowerHealthView == null || mainTower == null) return;
        _f2HealthDisplayRemaining = F2HealthDisplayDuration;
        _f2MainTowerHealthView.gameObject.SetActive(true);
        _f2MainTowerHealthView.SetAlpha(1f);
        _f2MainTowerHealthView.SetHealth(mainTower.CurrentHealth, mainTower.maxHealth);
    }

    private void UpdateF2MainTowerHealth(float unscaledDeltaTime)
    {
        if (_f2MainTowerHealthView == null ||
            !_f2MainTowerHealthView.gameObject.activeSelf) return;
        if (!_tiltShiftObservationActive || mainTower == null || _settingsMenuOpen)
        {
            HideF2MainTowerHealth();
            return;
        }
        _f2HealthDisplayRemaining -= Mathf.Max(0f, unscaledDeltaTime);
        _f2MainTowerHealthView.SetHealth(mainTower.CurrentHealth, mainTower.maxHealth);
        float alpha = _f2HealthDisplayRemaining < F2HealthFadeDuration
            ? Mathf.Clamp01(_f2HealthDisplayRemaining / F2HealthFadeDuration)
            : 1f;
        _f2MainTowerHealthView.SetAlpha(alpha);
        if (_f2HealthDisplayRemaining <= 0f) HideF2MainTowerHealth();
    }

    private void HideF2MainTowerHealth()
    {
        _f2HealthDisplayRemaining = 0f;
        if (_f2MainTowerHealthView != null)
            _f2MainTowerHealthView.gameObject.SetActive(false);
    }

    private void DisposePlayerSettingsUi()
    {
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (follow != null) follow.SetUserInputBlocked(false);
        if (_settingsMenuView != null) Destroy(_settingsMenuView.gameObject);
        if (_f2MainTowerHealthView != null) Destroy(_f2MainTowerHealthView.gameObject);
        _settingsMenuView = null;
        _f2MainTowerHealthView = null;
        _settingsMenuOpen = false;
        PlayerPrefs.Save();
    }
}
