using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RougeSettingsMenuView : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public Button closeButton;
    public Button[] tabButtons;
    public GameObject[] tabPages;

    [Header("Archive")]
    public Button archiveButton;
    public GameObject archivePage;
    public RougeArchiveView archiveView;

    [Header("Graphics")]
    public Button[] qualityButtons;
    public Button[] frameRateButtons;
    public Button[] displayModeButtons;
    public Button resolutionPreviousButton;
    public Button resolutionNextButton;
    public Text resolutionValueText;

    [Header("Audio")]
    public Slider musicSlider;
    public Text musicValueText;
    public Slider sfxSlider;
    public Text sfxValueText;

    [Header("Gameplay")]
    public Button damageStatisticsButton;
    public Text damageStatisticsValueText;

    [Header("Tilt Shift")]
    public Button customTiltBlurButton;
    public Text customTiltBlurValueText;
    public Slider tiltBlurSlider;
    public Text tiltBlurValueText;
    public Slider tiltClearWidthSlider;
    public Text tiltClearWidthValueText;

    private static Font s_runtimeFont;
    private int _lastSettingsTabIndex;
    private bool _tabsInitialized;
    private Button _boundArchiveButton;

    public bool IsArchiveVisible => archivePage != null && archivePage.activeSelf;

    public Selectable PreferredSelection
    {
        get
        {
            if (IsArchiveVisible && archiveView != null &&
                archiveView.PreferredSelection != null)
                return archiveView.PreferredSelection;
            if (IsArchiveVisible && archiveButton != null) return archiveButton;
            return tabButtons != null && tabButtons.Length > 0
                ? tabButtons[Mathf.Clamp(_lastSettingsTabIndex, 0,
                    tabButtons.Length - 1)]
                : closeButton;
        }
    }

    public void InitializeTabs()
    {
        if (!_tabsInitialized && tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int tabIndex = i;
                if (tabButtons[i] != null)
                    tabButtons[i].onClick.AddListener(() => ShowTab(tabIndex));
            }
        }
        _tabsInitialized = true;
        BindArchiveButton();
        ShowTab(0);
        ApplyRuntimeFont();
    }

    public void ConfigureArchive(Button button, GameObject page,
        RougeArchiveView view)
    {
        archiveButton = button;
        archivePage = page;
        archiveView = view;
        BindArchiveButton();
        if (archivePage != null) archivePage.SetActive(false);
        ApplyRuntimeFont();
    }

    public void ShowTab(int selectedIndex)
    {
        if (tabPages == null || tabPages.Length == 0) return;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, tabPages.Length - 1);
        _lastSettingsTabIndex = selectedIndex;
        if (tabPages != null)
        {
            for (int i = 0; i < tabPages.Length; i++)
                if (tabPages[i] != null) tabPages[i].SetActive(i == selectedIndex);
        }
        if (archivePage != null) archivePage.SetActive(false);
        SetSegmentSelection(tabButtons, selectedIndex);
        SetSegmentSelection(new[] { archiveButton }, -1);
        SetTerminalHeader(false);
    }

    public void ShowArchive()
    {
        if (archivePage == null) return;
        if (tabPages != null)
        {
            for (int i = 0; i < tabPages.Length; i++)
                if (tabPages[i] != null) tabPages[i].SetActive(false);
        }
        archivePage.SetActive(true);
        SetSegmentSelection(tabButtons, -1);
        SetSegmentSelection(new[] { archiveButton }, 0);
        archiveView?.Refresh();
        SetTerminalHeader(true);
        if (EventSystem.current != null && archiveView != null &&
            archiveView.PreferredSelection != null)
            EventSystem.current.SetSelectedGameObject(
                archiveView.PreferredSelection.gameObject);
    }

    public void ReturnFromArchive()
    {
        ShowTab(_lastSettingsTabIndex);
    }

    private void BindArchiveButton()
    {
        if (_boundArchiveButton == archiveButton) return;
        if (_boundArchiveButton != null)
            _boundArchiveButton.onClick.RemoveListener(ShowArchive);
        if (archiveButton == null)
        {
            _boundArchiveButton = null;
            return;
        }
        archiveButton.onClick.AddListener(ShowArchive);
        _boundArchiveButton = archiveButton;
    }

    private void SetTerminalHeader(bool archive)
    {
        Transform window = transform.Find("Settings Window");
        Transform header = window != null ? window.Find("Header") : null;
        Text title = header != null ? header.Find("Title")?.GetComponent<Text>() : null;
        Text subtitle = header != null
            ? header.Find("Subtitle")?.GetComponent<Text>()
            : null;
        Transform rail = window != null ? window.Find("Tab Rail") : null;
        Text escapeHint = rail != null
            ? rail.Find("Escape Hint")?.GetComponent<Text>()
            : null;
        if (title != null) title.text = archive ? "战术资料库" : "系统设置";
        if (subtitle != null)
            subtitle.text = archive
                ? "TACTICAL ARCHIVE  //  图鉴与锚定档案"
                : "SYSTEM CONFIGURATION  //  所有改动即时生效";
        if (escapeHint != null)
            escapeHint.text = archive ? "ESC  返回设置" : "ESC  返回游戏";
    }

    public static void SetSegmentSelection(Button[] buttons, int selectedIndex)
    {
        if (buttons == null) return;
        RougeCommanderVisualTheme theme =
            RougeCommanderVisualThemes.ResolveActive();
        Color selectedSurface = Color.Lerp(theme.UiPanelRaised,
            theme.Accent, 0.28f);
        selectedSurface.a = 0.78f;
        Color idleSurface = theme.UiPanelRaised;
        idleSurface.a = 0.46f;
        Color selectedText = theme.PrimaryText;
        selectedText.a = 1f;
        Color idleText = Color.Lerp(theme.SecondaryText,
            theme.PrimaryText, 0.30f);
        idleText.a = 1f;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null) continue;
            bool selected = i == selectedIndex;
            Image image = button.GetComponent<Image>();
            if (image != null)
                image.color = selected
                    ? selectedSurface
                    : idleSurface;
            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
                text.color = selected
                    ? selectedText
                    : idleText;
        }
    }

    private void ApplyRuntimeFont()
    {
        if (s_runtimeFont == null)
        {
            s_runtimeFont = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC",
                "Noto Sans CJK SC", "Arial"
            }, 22);
            if (s_runtimeFont == null)
                s_runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        Text[] texts = GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
            if (texts[i] != null) texts[i].font = s_runtimeFont;
    }
}
