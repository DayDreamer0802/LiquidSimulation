using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RougeSettingsMenuView : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public Button closeButton;
    public Button[] tabButtons;
    public GameObject[] tabPages;

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

    public void InitializeTabs()
    {
        if (tabButtons != null)
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int tabIndex = i;
                if (tabButtons[i] != null)
                    tabButtons[i].onClick.AddListener(() => ShowTab(tabIndex));
            }
        }
        ShowTab(0);
        ApplyRuntimeFont();
    }

    public void ShowTab(int selectedIndex)
    {
        if (tabPages != null)
        {
            for (int i = 0; i < tabPages.Length; i++)
                if (tabPages[i] != null) tabPages[i].SetActive(i == selectedIndex);
        }
        SetSegmentSelection(tabButtons, selectedIndex);
    }

    public static void SetSegmentSelection(Button[] buttons, int selectedIndex)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null) continue;
            bool selected = i == selectedIndex;
            Image image = button.GetComponent<Image>();
            if (image != null)
                image.color = selected
                    ? new Color(0.035f, 0.48f, 0.68f, 0.98f)
                    : new Color(0.018f, 0.09f, 0.14f, 0.96f);
            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
                text.color = selected
                    ? new Color(0.82f, 0.97f, 1f, 1f)
                    : new Color(0.46f, 0.68f, 0.76f, 1f);
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
