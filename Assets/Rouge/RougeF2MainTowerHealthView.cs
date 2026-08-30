using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RougeF2MainTowerHealthView : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public Image healthFill;
    public Text healthText;

    private static Font s_runtimeFont;

    private void Awake()
    {
        if (s_runtimeFont == null)
        {
            s_runtimeFont = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC",
                "Noto Sans CJK SC", "Arial"
            }, 20);
            if (s_runtimeFont == null)
                s_runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        if (healthText != null) healthText.font = s_runtimeFont;
    }

    public void SetHealth(float current, float maximum)
    {
        float normalized = maximum > 0.001f ? Mathf.Clamp01(current / maximum) : 0f;
        if (healthFill != null)
        {
            RectTransform rect = healthFill.rectTransform;
            rect.anchorMax = new Vector2(normalized, 1f);
            RougeCommanderVisualTheme theme =
                RougeCommanderVisualThemes.ResolveActive();
            Color healthyColor = theme.UsesDefaultPalette
                ? new Color(0.08f, 0.82f, 1f, 1f)
                : theme.Accent;
            healthFill.color = Color.Lerp(
                new Color(1f, 0.18f, 0.12f, 1f),
                healthyColor, normalized);
        }
        if (healthText != null)
            healthText.text = $"主塔核心  {Mathf.Max(0f, current):0} / {Mathf.Max(0f, maximum):0}";
    }

    public void SetAlpha(float alpha)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = Mathf.Clamp01(alpha);
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
