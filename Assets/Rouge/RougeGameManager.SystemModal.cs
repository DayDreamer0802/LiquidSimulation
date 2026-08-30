using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private const int SystemModalSortingOrder = 32766;

    private bool _systemModalHudSnapshotCaptured;
    private bool _towerDefenseCanvasWasVisibleBeforeModal;
    private bool _autoplayCanvasWasVisibleBeforeModal;
    private bool _levelEventCanvasWasVisibleBeforeModal;
    private CursorLockMode _cursorLockModeBeforeModal;
    private bool _cursorVisibleBeforeModal;
    private GameObject _selectionBeforeSystemModal;
    private Coroutine _systemModalFadeRoutine;
    private bool _systemModalClosing;

    public static bool IsSystemModalInputBlocked { get; private set; }

    private void BuildArchiveUi()
    {
        RougeSettingsMenuView menu = _settingsMenuView;
        if (menu == null) return;

        if (menu.canvasGroup == null)
            menu.canvasGroup = menu.GetComponent<CanvasGroup>();
        NormalizeSystemModalCanvas(menu.gameObject);
        ApplyLineFirstSystemModalStyle(menu.gameObject);
        Transform window = menu.transform.Find("Settings Window");
        Transform rail = window != null ? window.Find("Tab Rail") : null;
        Transform content = window != null ? window.Find("Content") : null;
        if (rail == null || content == null)
        {
            Debug.LogError("Settings prefab is missing its Tab Rail or Content root.",
                menu);
            return;
        }

        Button archiveButton = menu.archiveButton != null
            ? menu.archiveButton
            : CreateArchiveEntryButton(rail);
        GameObject archivePage = menu.archivePage;
        if (archivePage == null)
        {
            archivePage = new GameObject("Archive Page", typeof(RectTransform));
            archivePage.layer = 5;
            RectTransform rect = archivePage.GetComponent<RectTransform>();
            rect.SetParent(content, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        RougeArchiveView archiveView = menu.archiveView != null
            ? menu.archiveView
            : archivePage.GetComponent<RougeArchiveView>();
        if (archiveView == null)
            archiveView = archivePage.AddComponent<RougeArchiveView>();
        archiveView.Initialize(GetTowerDefenseHudFont(),
            ActiveCommanderVisualTheme);
        menu.ConfigureArchive(archiveButton, archivePage, archiveView);
    }

    private static Button CreateArchiveEntryButton(Transform parent)
    {
        GameObject root = new GameObject("Archive Entry", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
        root.layer = 5;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -390f);
        rect.sizeDelta = new Vector2(-28f, 64f);

        Image image = root.GetComponent<Image>();
        image.color = new Color(0.018f, 0.09f, 0.14f, 0.34f);
        image.raycastTarget = true;
        Outline outline = root.GetComponent<Outline>();
        outline.effectColor = new Color(0.08f, 0.72f, 0.92f, 0.72f);
        outline.effectDistance = new Vector2(1f, 1f);
        outline.useGraphicAlpha = false;

        Button button = root.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.76f, 0.96f, 1f, 1f);
        colors.pressedColor = new Color(0.46f, 0.82f, 0.94f, 1f);
        colors.selectedColor = new Color(0.72f, 0.94f, 1f, 1f);
        colors.fadeDuration = 0.09f;
        button.colors = colors;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Text));
        labelObject.layer = 5;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(10f, 5f);
        labelRect.offsetMax = new Vector2(-10f, -5f);
        Text label = labelObject.GetComponent<Text>();
        label.font = GetTowerDefenseHudFont();
        label.fontSize = 20;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.82f, 0.97f, 1f, 1f);
        label.raycastTarget = false;
        label.text = "资 料 库  //  ARCHIVE";
        return button;
    }

    private static void NormalizeSystemModalCanvas(GameObject root)
    {
        if (root == null) return;
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SystemModalSortingOrder;
        }
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }

    private void ApplyLineFirstSystemModalStyle(GameObject root)
    {
        if (root == null) return;
        RougeCommanderVisualTheme theme = ActiveCommanderVisualTheme;
        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || IsArchiveOwnedVisual(image.transform)) continue;
            Color color = image.color;
            switch (image.name)
            {
                case "Screen Dimmer":
                    color = Color.Lerp(theme.UiBackdrop, Color.black, 0.62f);
                    color.a = 0.92f;
                    break;
                case "Settings Window":
                    color = Color.Lerp(theme.UiBackdrop, theme.UiPanel, 0.12f);
                    color.a = 0.98f;
                    break;
                case "Header":
                    color = theme.UiPanel;
                    color.a = 0.46f;
                    break;
                case "Tab Rail":
                    color = theme.UiPanel;
                    color.a = 0.72f;
                    break;
                case "Content":
                    color = theme.UiPanel;
                    color.a = 0.62f;
                    break;
                default:
                    if (image.name.EndsWith(" Row",
                            System.StringComparison.Ordinal))
                    {
                        color = theme.UiPanelRaised;
                        color.a = 0.34f;
                    }
                    else if (image.GetComponent<Button>() != null)
                    {
                        color = theme.UiPanelRaised;
                        color.a = 0.46f;
                    }
                    break;
            }
            image.color = color;
        }

        // Unity's legacy Outline duplicates the complete source quad four times.
        // On sprite-less panels that is a four-layer colour wash, not a one-pixel
        // border; nested rows were therefore turning the whole Taotao menu pink.
        Outline[] outlines = root.GetComponentsInChildren<Outline>(true);
        for (int i = 0; i < outlines.Length; i++)
        {
            Outline outline = outlines[i];
            if (outline == null || IsArchiveOwnedVisual(outline.transform)) continue;
            Image sourceImage = outline.GetComponent<Image>();
            if (sourceImage != null && sourceImage.sprite == null)
                outline.enabled = false;
        }

        // Text is deliberately not treated as another tintable surface. Commander
        // identity lives in the rails and selection accents; copy must retain a
        // stable luminance contrast against both palettes.
        Text[] texts = root.GetComponentsInChildren<Text>(true);
        Color primaryText = theme.PrimaryText;
        primaryText.a = 1f;
        Color secondaryText = Color.Lerp(theme.SecondaryText,
            theme.PrimaryText, 0.30f);
        secondaryText.a = 1f;
        for (int i = 0; i < texts.Length; i++)
        {
            Text label = texts[i];
            if (label == null || IsArchiveOwnedVisual(label.transform)) continue;
            bool primary = label.fontStyle == FontStyle.Bold ||
                           label.GetComponentInParent<Button>() != null ||
                           label.name.IndexOf("Title",
                               System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           label.name.IndexOf("Value",
                               System.StringComparison.OrdinalIgnoreCase) >= 0;
            label.color = primary ? primaryText : secondaryText;
        }
    }

    private static bool IsArchiveOwnedVisual(Transform source)
    {
        for (Transform current = source; current != null; current = current.parent)
        {
            if (string.Equals(current.name, "Archive Page",
                    System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.name, "Archive Runtime Root",
                    System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void CaptureAndHideGameplayHudForSystemModal()
    {
        IsSystemModalInputBlocked = true;
        if (!_systemModalHudSnapshotCaptured)
        {
            _towerDefenseCanvasWasVisibleBeforeModal =
                _towerDefenseCanvas != null &&
                _towerDefenseCanvas.gameObject.activeSelf;
            _autoplayCanvasWasVisibleBeforeModal =
                _towerDefenseAutoplayCanvas != null &&
                _towerDefenseAutoplayCanvas.gameObject.activeSelf;
            _levelEventCanvasWasVisibleBeforeModal =
                _towerDefenseLevelEventCanvas != null &&
                _towerDefenseLevelEventCanvas.gameObject.activeSelf;
            _cursorLockModeBeforeModal = Cursor.lockState;
            _cursorVisibleBeforeModal = Cursor.visible;
            _selectionBeforeSystemModal = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;
            _systemModalHudSnapshotCaptured = true;
        }
        EnforceSystemModalHudIsolation();
    }

    private void EnforceSystemModalHudIsolation()
    {
        SetCanvasVisible(_towerDefenseCanvas, false);
        SetCanvasVisible(_towerDefenseAutoplayCanvas, false);
        SetCanvasVisible(_towerDefenseLevelEventCanvas, false);
        HideF2MainTowerHealth();
    }

    private void RestoreGameplayHudAfterSystemModal()
    {
        IsSystemModalInputBlocked = false;
        if (!_systemModalHudSnapshotCaptured) return;
        SetCanvasVisible(_towerDefenseCanvas,
            _towerDefenseCanvasWasVisibleBeforeModal);
        SetCanvasVisible(_towerDefenseAutoplayCanvas,
            _autoplayCanvasWasVisibleBeforeModal);
        SetCanvasVisible(_towerDefenseLevelEventCanvas,
            _levelEventCanvasWasVisibleBeforeModal);

        Cursor.lockState = _cursorLockModeBeforeModal;
        Cursor.visible = _cursorVisibleBeforeModal;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(
                _selectionBeforeSystemModal != null &&
                _selectionBeforeSystemModal.activeInHierarchy
                    ? _selectionBeforeSystemModal
                    : null);
        ClearSystemModalSnapshot();
        SyncTowerDefenseAutoplayPresentation();
        RefreshTowerDefenseUi(true);
    }

    private void ClearSystemModalSnapshot()
    {
        _systemModalHudSnapshotCaptured = false;
        _selectionBeforeSystemModal = null;
    }

    private static void SetCanvasVisible(Canvas canvas, bool visible)
    {
        if (canvas != null && canvas.gameObject.activeSelf != visible)
            canvas.gameObject.SetActive(visible);
    }

    private void FocusSystemModal()
    {
        if (EventSystem.current == null || _settingsMenuView == null) return;
        Selectable selectable = _settingsMenuView.PreferredSelection;
        EventSystem.current.SetSelectedGameObject(
            selectable != null ? selectable.gameObject : null);
    }

    private void BeginSystemModalReveal()
    {
        CancelSystemModalTransition();
        _systemModalClosing = false;
        CanvasGroup group = _settingsMenuView != null
            ? _settingsMenuView.canvasGroup
            : null;
        if (group == null) return;
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = true;
        _systemModalFadeRoutine = StartCoroutine(RevealSystemModal(group));
    }

    private IEnumerator RevealSystemModal(CanvasGroup group)
    {
        const float duration = 0.2f;
        float elapsed = 0f;
        while (elapsed < duration && group != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            group.alpha = 1f - (1f - t) * (1f - t);
            EnforceSystemModalHudIsolation();
            yield return null;
        }
        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
        _systemModalFadeRoutine = null;
        FocusSystemModal();
    }

    private void BeginSystemModalClose()
    {
        if (_systemModalClosing) return;
        _systemModalClosing = true;
        if (_systemModalFadeRoutine != null)
            StopCoroutine(_systemModalFadeRoutine);
        CanvasGroup group = _settingsMenuView != null
            ? _settingsMenuView.canvasGroup
            : null;
        if (group == null)
        {
            CompleteClosePlayerSettings();
            return;
        }
        group.interactable = false;
        group.blocksRaycasts = true;
        _systemModalFadeRoutine = StartCoroutine(HideSystemModal(group));
    }

    private IEnumerator HideSystemModal(CanvasGroup group)
    {
        const float duration = 0.16f;
        float from = group != null ? group.alpha : 1f;
        float elapsed = 0f;
        while (elapsed < duration && group != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            group.alpha = Mathf.Lerp(from, 0f, t * t);
            EnforceSystemModalHudIsolation();
            yield return null;
        }
        _systemModalFadeRoutine = null;
        CompleteClosePlayerSettings();
    }

    private void CancelSystemModalTransition()
    {
        if (_systemModalFadeRoutine != null)
            StopCoroutine(_systemModalFadeRoutine);
        _systemModalFadeRoutine = null;
        _systemModalClosing = false;
    }

    private void HandlePlayerSettingsEscape()
    {
        if (_settingsMenuView != null && _settingsMenuView.IsArchiveVisible)
        {
            _settingsMenuView.ReturnFromArchive();
            FocusSystemModal();
            return;
        }
        ClosePlayerSettings();
    }
}
