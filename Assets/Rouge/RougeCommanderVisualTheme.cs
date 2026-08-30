using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual language selected by the active tactical adjutant. Gameplay-semantic
/// colours (danger, valid placement, tower identity and boss phases) deliberately
/// remain outside this palette so a different adjutant never changes the rules a
/// player reads from the screen.
/// </summary>
public sealed class RougeCommanderVisualTheme
{
    public readonly string CommanderId;
    public readonly Color UiBackdrop;
    public readonly Color UiPanel;
    public readonly Color UiPanelRaised;
    public readonly Color Accent;
    public readonly Color AccentSecondary;
    public readonly Color PrimaryText;
    public readonly Color SecondaryText;
    public readonly Color MapBackdropBase;
    public readonly Color MapBackdropOuter;
    public readonly Color MapFrameBase;
    public readonly Color MapFramePanel;
    public readonly Color MapGrid;
    public readonly Color MapGroundBase;
    public readonly Color MapGroundPanel;
    public readonly Color MapGroundAccent;
    public readonly Color MapStandardPadBase;
    public readonly Color MapStandardPadAccent;
    public readonly Color PlacementGridBase;
    public readonly Color PlacementGridLine;
    public readonly Color ScanColor;

    public bool UsesDefaultPalette => string.Equals(
        CommanderId, RougeAutoplayCommanderJson.DefaultCommanderName,
        StringComparison.OrdinalIgnoreCase);

    public RougeCommanderVisualTheme(
        string commanderId,
        Color uiBackdrop,
        Color uiPanel,
        Color uiPanelRaised,
        Color accent,
        Color accentSecondary,
        Color primaryText,
        Color secondaryText,
        Color mapBackdropBase,
        Color mapBackdropOuter,
        Color mapFrameBase,
        Color mapFramePanel,
        Color mapGrid,
        Color mapGroundBase,
        Color mapGroundPanel,
        Color mapGroundAccent,
        Color mapStandardPadBase,
        Color mapStandardPadAccent,
        Color placementGridBase,
        Color placementGridLine,
        Color scanColor)
    {
        CommanderId = commanderId;
        UiBackdrop = uiBackdrop;
        UiPanel = uiPanel;
        UiPanelRaised = uiPanelRaised;
        Accent = accent;
        AccentSecondary = accentSecondary;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        MapBackdropBase = mapBackdropBase;
        MapBackdropOuter = mapBackdropOuter;
        MapFrameBase = mapFrameBase;
        MapFramePanel = mapFramePanel;
        MapGrid = mapGrid;
        MapGroundBase = mapGroundBase;
        MapGroundPanel = mapGroundPanel;
        MapGroundAccent = mapGroundAccent;
        MapStandardPadBase = mapStandardPadBase;
        MapStandardPadAccent = mapStandardPadAccent;
        PlacementGridBase = placementGridBase;
        PlacementGridLine = placementGridLine;
        ScanColor = scanColor;
    }

    public Color RemapInterfaceColor(Color source)
    {
        if (UsesDefaultPalette) return source;

        Color.RGBToHSV(source, out float hue, out float saturation, out float value);
        bool cyanAccent = hue >= 0.44f && hue <= 0.61f &&
                          saturation >= 0.22f && value >= 0.20f;
        if (cyanAccent)
            return MatchThemeHue(source, Accent, saturation, value);

        bool coolDarkSurface = value <= 0.23f &&
                               source.b >= source.r * 1.15f &&
                               source.g >= source.r * 1.03f;
        if (coolDarkSurface)
        {
            Color target = value <= 0.075f ? UiBackdrop :
                value <= 0.14f ? UiPanel : UiPanelRaised;
            Color result = Color.Lerp(source, target, 0.72f);
            result.a = source.a;
            return result;
        }

        bool coolLightText = value >= 0.56f && source.b > source.r &&
                             source.g > source.r && saturation <= 0.48f;
        if (coolLightText)
        {
            Color result = Color.Lerp(source,
                saturation <= 0.18f ? PrimaryText : SecondaryText, 0.58f);
            result.a = source.a;
            return result;
        }

        return source;
    }

    private static Color MatchThemeHue(Color source, Color target,
        float sourceSaturation, float sourceValue)
    {
        Color.RGBToHSV(target, out float targetHue, out float targetSaturation,
            out _);
        float saturation = Mathf.Clamp01(Mathf.Lerp(
            sourceSaturation, targetSaturation, 0.72f));
        Color result = Color.HSVToRGB(targetHue, saturation, sourceValue);
        result.a = source.a;
        return result;
    }
}

public static class RougeCommanderVisualThemes
{
    private static readonly RougeCommanderVisualTheme Lan =
        new RougeCommanderVisualTheme(
            "lan",
            new Color(0.003f, 0.016f, 0.035f, 0.965f),
            new Color(0.012f, 0.055f, 0.09f, 0.96f),
            new Color(0.025f, 0.09f, 0.13f, 0.96f),
            new Color(0.08f, 0.82f, 1f, 1f),
            new Color(0.24f, 0.92f, 1f, 1f),
            new Color(0.82f, 0.97f, 1f, 1f),
            new Color(0.45f, 0.68f, 0.76f, 1f),
            new Color(0.025f, 0.06f, 0.095f, 1f),
            new Color(0.009f, 0.023f, 0.042f, 1f),
            new Color(0.035f, 0.075f, 0.11f, 1f),
            new Color(0.07f, 0.14f, 0.19f, 1f),
            new Color(0.075f, 0.42f, 0.58f, 1f),
            new Color(0.16f, 0.25f, 0.32f, 1f),
            new Color(0.09f, 0.16f, 0.22f, 1f),
            new Color(0.05f, 0.62f, 0.72f, 1f),
            new Color(0.07f, 0.135f, 0.19f, 1f),
            new Color(0.5189f, 0.9077f, 1f, 1f),
            new Color(0.008f, 0.07f, 0.11f, 0.035f),
            new Color(0.32f, 0.84f, 0.92f, 0.82f),
            new Color(0.06f, 1.15f, 1.8f, 1f));

    private static readonly RougeCommanderVisualTheme Taotao =
        new RougeCommanderVisualTheme(
            "taotao",
            new Color(0.035f, 0.008f, 0.028f, 0.97f),
            new Color(0.095f, 0.025f, 0.075f, 0.96f),
            new Color(0.145f, 0.045f, 0.115f, 0.96f),
            new Color(1f, 0.38f, 0.69f, 1f),
            new Color(1f, 0.67f, 0.48f, 1f),
            new Color(1f, 0.90f, 0.96f, 1f),
            new Color(0.79f, 0.57f, 0.69f, 1f),
            new Color(0.075f, 0.022f, 0.062f, 1f),
            new Color(0.024f, 0.006f, 0.022f, 1f),
            new Color(0.105f, 0.030f, 0.085f, 1f),
            new Color(0.175f, 0.060f, 0.145f, 1f),
            new Color(0.55f, 0.10f, 0.36f, 1f),
            new Color(0.27f, 0.12f, 0.22f, 1f),
            new Color(0.16f, 0.055f, 0.13f, 1f),
            new Color(0.95f, 0.25f, 0.58f, 1f),
            new Color(0.17f, 0.050f, 0.135f, 1f),
            new Color(1f, 0.42f, 0.70f, 1f),
            new Color(0.105f, 0.018f, 0.080f, 0.035f),
            new Color(1f, 0.43f, 0.72f, 0.82f),
            new Color(1.7f, 0.18f, 0.92f, 1f));

    public static RougeCommanderVisualTheme Resolve(string commanderId)
    {
        return string.Equals(commanderId, Taotao.CommanderId,
            StringComparison.OrdinalIgnoreCase) ? Taotao : Lan;
    }

    public static RougeCommanderVisualTheme ResolveActive()
    {
        RougeAutoplayCommanderDefinition active = RougeAutoplayCommanderJson.Active;
        return Resolve(active != null ? active.CommanderId :
            RougeAutoplayCommanderJson.SelectedCommanderName);
    }

    public static void ApplyToUiHierarchy(GameObject root,
        RougeCommanderVisualTheme theme)
    {
        if (root == null || theme == null || theme.UsesDefaultPalette) return;

        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null || PreserveSemanticColour(graphic.transform)) continue;
            graphic.color = theme.RemapInterfaceColor(graphic.color);
        }

        Outline[] outlines = root.GetComponentsInChildren<Outline>(true);
        for (int i = 0; i < outlines.Length; i++)
        {
            Outline outline = outlines[i];
            if (outline == null || PreserveSemanticColour(outline.transform)) continue;
            outline.effectColor = theme.RemapInterfaceColor(outline.effectColor);
        }

        Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < selectables.Length; i++)
        {
            Selectable selectable = selectables[i];
            if (selectable == null ||
                PreserveSemanticColour(selectable.transform)) continue;
            ColorBlock colors = selectable.colors;
            colors.normalColor = theme.RemapInterfaceColor(colors.normalColor);
            colors.highlightedColor = theme.RemapInterfaceColor(colors.highlightedColor);
            colors.pressedColor = theme.RemapInterfaceColor(colors.pressedColor);
            colors.selectedColor = theme.RemapInterfaceColor(colors.selectedColor);
            colors.disabledColor = theme.RemapInterfaceColor(colors.disabledColor);
            selectable.colors = colors;
        }
    }

    private static bool PreserveSemanticColour(Transform source)
    {
        for (Transform current = source; current != null; current = current.parent)
        {
            string objectName = current.name;
            if (string.IsNullOrEmpty(objectName)) continue;
            if (string.Equals(objectName, "Archive Page",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(objectName, "Archive Runtime Root",
                    StringComparison.OrdinalIgnoreCase) ||
                objectName.IndexOf("Portrait",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Game Over", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Effect Choice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Tactical Skill ",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                IsTowerIdentityObject(objectName))
                return true;
        }
        return false;
    }

    private static bool IsTowerIdentityObject(string objectName)
    {
        // Some special-tower buttons use spaced display names while the enum does
        // not. Comparing a compact form protects both without exempting their
        // surrounding neutral panels from commander tinting.
        string compactName = objectName.Replace(" ", string.Empty);
        return string.Equals(compactName, nameof(RougeTowerType.Ice),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.MachineGun),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.Cannon),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.Flame),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.Laser),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.PiercingLaser),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.OrbitSphere),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.RocketBarrage),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.ChargeTower),
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(compactName, nameof(RougeTowerType.ReinforcementTower),
                   StringComparison.OrdinalIgnoreCase);
    }
}
