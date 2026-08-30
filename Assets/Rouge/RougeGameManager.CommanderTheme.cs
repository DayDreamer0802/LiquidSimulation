using UnityEngine;

public partial class RougeGameManager
{
    private RougeCommanderVisualTheme _activeCommanderVisualTheme;

    private RougeCommanderVisualTheme ActiveCommanderVisualTheme
    {
        get
        {
            if (_activeCommanderVisualTheme == null)
                _activeCommanderVisualTheme =
                    RougeCommanderVisualThemes.ResolveActive();
            return _activeCommanderVisualTheme;
        }
    }

    private void ApplyActiveCommanderUiTheme()
    {
        _activeCommanderVisualTheme = RougeCommanderVisualThemes.ResolveActive();

        ApplyActiveCommanderUiTheme(
            _towerDefenseCanvas != null
                ? _towerDefenseCanvas.gameObject
                : null);
        ApplyActiveCommanderUiTheme(
            _towerDefenseAutoplayCanvas != null
                ? _towerDefenseAutoplayCanvas.gameObject
                : null);
        ApplyActiveCommanderUiTheme(
            _towerDefenseLevelEventCanvas != null
                ? _towerDefenseLevelEventCanvas.gameObject
                : null);
        ApplyActiveCommanderUiTheme(
            _settingsMenuView != null
                ? _settingsMenuView.gameObject
                : null);
        if (_settingsMenuView != null)
            ApplyLineFirstSystemModalStyle(_settingsMenuView.gameObject);
        ApplyActiveCommanderUiTheme(
            _f2MainTowerHealthView != null
                ? _f2MainTowerHealthView.gameObject
                : null);
    }

    private void ApplyActiveCommanderUiTheme(GameObject root)
    {
        if (root == null) return;
        RougeCommanderVisualThemes.ApplyToUiHierarchy(
            root, ActiveCommanderVisualTheme);
    }

    private Color RemapCommanderInterfaceColor(Color authoredColor)
    {
        return ActiveCommanderVisualTheme.RemapInterfaceColor(authoredColor);
    }

    private string CommanderInterfaceColorHex(Color authoredColor)
    {
        return ColorUtility.ToHtmlStringRGB(
            RemapCommanderInterfaceColor(authoredColor));
    }
}
