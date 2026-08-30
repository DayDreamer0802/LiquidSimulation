using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Self-building legacy Unity UI reader for <see cref="RougeArchiveCatalog"/>.
/// Attach it to a RectTransform inside an existing settings page, then call
/// Initialize. This component deliberately creates no Canvas and owns no pause,
/// input-mode or Time.timeScale state.
/// </summary>
[DisallowMultipleComponent]
public sealed class RougeArchiveView : MonoBehaviour
{
    private const float TopBarHeight = 74f;
    private const float LeftPanelWidth = 306f;
    private const float OuterPadding = 8f;
    private const float ColumnGap = 8f;

    private sealed class BoundButton
    {
        public readonly string Id;
        public readonly Button Button;

        public BoundButton(string id, Button button)
        {
            Id = id;
            Button = button;
        }
    }

    private readonly List<BoundButton> _categoryButtons = new List<BoundButton>();
    private readonly List<BoundButton> _entryButtons = new List<BoundButton>();
    private readonly List<Text> _allTexts = new List<Text>();
    private readonly List<Text> _primaryTexts = new List<Text>();
    private readonly List<Text> _secondaryTexts = new List<Text>();
    private readonly List<Image> _backdropImages = new List<Image>();
    private readonly List<Image> _panelImages = new List<Image>();
    private readonly List<Image> _raisedImages = new List<Image>();
    private readonly List<Image> _accentImages = new List<Image>();
    private readonly List<Image> _railImages = new List<Image>();
    private readonly Dictionary<Button, Text> _buttonLabels =
        new Dictionary<Button, Text>();
    private readonly Dictionary<Button, Image> _buttonRails =
        new Dictionary<Button, Image>();

    private RougeArchiveCatalog _catalog;
    private Font _font;
    private RougeCommanderVisualTheme _theme;
    private RectTransform _runtimeRoot;
    private RectTransform _categoryContent;
    private RectTransform _entryContent;
    private ScrollRect _entryScroll;
    private ScrollRect _bodyScroll;
    private Text _terminalTitleText;
    private Text _categoryCaptionText;
    private Text _entryTitleText;
    private Text _entryStatusText;
    private Text _entryMetadataText;
    private Text _entryBodyText;
    private Button[] _libraryButtons;
    private RougeArchiveLibrary _selectedLibrary;
    private string _selectedCategoryId;
    private string _selectedEntryId;
    private Button _selectedEntryButton;
    private bool _initialized;
    private static Font s_fallbackFont;

    /// <summary>
    /// A sensible target for EventSystem.SetSelectedGameObject after this page is
    /// shown. It prefers the current record, then the active library tab.
    /// </summary>
    public Button PreferredSelection
    {
        get
        {
            if (_selectedEntryButton != null) return _selectedEntryButton;
            int index = (int)_selectedLibrary;
            return _libraryButtons != null && (uint)index < (uint)_libraryButtons.Length
                ? _libraryButtons[index]
                : null;
        }
    }

    public RougeArchiveLibrary SelectedLibrary => _selectedLibrary;
    public string SelectedCategoryId => _selectedCategoryId;
    public string SelectedEntryId => _selectedEntryId;
    public bool IsInitialized => _initialized;

    public RougeArchiveEntry SelectedEntry
    {
        get
        {
            if (_catalog != null &&
                _catalog.TryGetEntry(_selectedEntryId, out RougeArchiveEntry entry))
                return entry;
            return null;
        }
    }

    /// <summary>
    /// Builds the reader on first use, or reapplies font and commander theme on
    /// subsequent calls. Passing null uses safe runtime fallbacks.
    /// </summary>
    public void Initialize(Font font, RougeCommanderVisualTheme theme)
    {
        bool firstInitialization = !_initialized;
        _catalog = RougeArchiveCatalog.Shared;
        _font = font != null ? font : ResolveFallbackFont();
        _theme = theme ?? RougeCommanderVisualThemes.ResolveActive();

        if (_runtimeRoot == null)
        {
            if (GetComponent<RectTransform>() == null)
            {
                Debug.LogError("RougeArchiveView must be attached to a UI RectTransform.", this);
                return;
            }
            BuildInterface();
        }

        ApplyFont();
        ApplyTheme(_theme);
        _initialized = true;
        if (firstInitialization) ShowDefault();
        else Refresh();
    }

    /// <summary>
    /// Returns to the first tactical-index category and its first record.
    /// </summary>
    public void ShowDefault()
    {
        if (_runtimeRoot == null) return;
        _selectedLibrary = RougeArchiveLibrary.TacticalIndex;
        _selectedCategoryId = null;
        _selectedEntryId = null;
        RebuildLibrary(false);
    }

    /// <summary>
    /// Rebuilds buttons from the catalog while preserving the current selection
    /// whenever its stable IDs still exist.
    /// </summary>
    public void Refresh()
    {
        if (_runtimeRoot == null) return;
        RebuildLibrary(true);
    }

    public void ShowLibrary(RougeArchiveLibrary library)
    {
        if (_runtimeRoot == null) return;
        if (!Enum.IsDefined(typeof(RougeArchiveLibrary), library))
            library = RougeArchiveLibrary.TacticalIndex;
        bool preserveSelection = _selectedLibrary == library;
        _selectedLibrary = library;
        if (!preserveSelection)
        {
            _selectedCategoryId = null;
            _selectedEntryId = null;
        }
        RebuildLibrary(preserveSelection);
    }

    public bool ShowEntry(string stableId)
    {
        if (_runtimeRoot == null || string.IsNullOrEmpty(stableId) ||
            !_catalog.TryGetEntry(stableId, out RougeArchiveEntry entry))
            return false;

        _selectedLibrary = entry.Library;
        _selectedCategoryId = entry.CategoryId;
        _selectedEntryId = entry.StableId;
        RebuildLibrary(true);
        return string.Equals(_selectedEntryId, stableId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Recolours the complete reader without rebuilding it. This can be called
    /// when the active commander changes while the settings prefab is alive.
    /// </summary>
    public void ApplyTheme(RougeCommanderVisualTheme theme)
    {
        _theme = theme ?? RougeCommanderVisualThemes.ResolveActive();
        if (_runtimeRoot == null || _theme == null) return;

        Color backdrop = WithAlpha(_theme.UiBackdrop, 0.14f);
        Color panel = WithAlpha(_theme.UiPanel, 0.20f);
        Color raised = WithAlpha(_theme.UiPanelRaised, 0.24f);
        Color accent = WithAlpha(_theme.Accent, 0.82f);
        Color rail = WithAlpha(_theme.Accent, 0.48f);

        SetImageColors(_backdropImages, backdrop);
        SetImageColors(_panelImages, panel);
        SetImageColors(_raisedImages, raised);
        SetImageColors(_accentImages, accent);
        SetImageColors(_railImages, rail);
        SetTextColors(_primaryTexts, _theme.PrimaryText);
        SetTextColors(_secondaryTexts, Color.Lerp(_theme.SecondaryText,
            _theme.PrimaryText, 0.20f));
        Outline[] outlines = _runtimeRoot.GetComponentsInChildren<Outline>(true);
        for (int i = 0; i < outlines.Length; i++)
        {
            if (outlines[i] == null) continue;
            // Legacy Outline duplicates the entire sprite-less quad four times.
            // Explicit one-pixel rails below provide a real frame without turning
            // every archive surface into an opaque commander-colour slab.
            outlines[i].enabled = false;
        }
        UpdateAllButtonVisuals();
    }

    private void BuildInterface()
    {
        GameObject rootObject = new GameObject("Archive Runtime Root",
            typeof(RectTransform), typeof(Image));
        rootObject.transform.SetParent(transform, false);
        _runtimeRoot = rootObject.GetComponent<RectTransform>();
        Stretch(_runtimeRoot, 0f, 0f, 0f, 0f);
        Image rootImage = rootObject.GetComponent<Image>();
        rootImage.raycastTarget = true;
        _backdropImages.Add(rootImage);

        BuildTopBar(_runtimeRoot);
        BuildLeftPanel(_runtimeRoot);
        BuildDetailPanel(_runtimeRoot);
    }

    private void BuildTopBar(Transform parent)
    {
        Image bar = CreateImage("Archive Top Bar", parent, ImageRole.Panel, true);
        SetTopStretch(bar.rectTransform, OuterPadding, OuterPadding,
            OuterPadding, TopBarHeight - OuterPadding);

        _terminalTitleText = CreateText("Terminal Title", bar.transform,
            "ARCHIVE TERMINAL\n资料库", 18, TextAnchor.MiddleLeft, true,
            FontStyle.Bold);
        SetTopLeft(_terminalTitleText.rectTransform, 16f, 8f, 205f, 45f);
        _terminalTitleText.lineSpacing = 0.9f;

        _libraryButtons = new Button[2];
        _libraryButtons[0] = CreateButton("Tactical Index Tab", bar.transform,
            "战区识别库\nTACTICAL INDEX", 16, TextAnchor.MiddleCenter);
        SetTopLeft(_libraryButtons[0].GetComponent<RectTransform>(),
            220f, 6f, 248f, 54f);
        _libraryButtons[0].onClick.AddListener(() =>
            ShowLibrary(RougeArchiveLibrary.TacticalIndex));

        _libraryButtons[1] = CreateButton("Anchor Archive Tab", bar.transform,
            "锚区档案\nANCHOR ARCHIVE", 16, TextAnchor.MiddleCenter);
        SetTopLeft(_libraryButtons[1].GetComponent<RectTransform>(),
            476f, 6f, 248f, 54f);
        _libraryButtons[1].onClick.AddListener(() =>
            ShowLibrary(RougeArchiveLibrary.AnchorArchive));

        Text hint = CreateText("Archive Hint", bar.transform,
            "选择分类与记录  //  滚轮阅读正文", 13,
            TextAnchor.MiddleRight, false, FontStyle.Normal);
        SetTopRight(hint.rectTransform, 14f, 8f, 282f, 45f);

        Image line = CreateImage("Archive Top Divider", bar.transform,
            ImageRole.Accent, false);
        SetBottomStretch(line.rectTransform, 0f, 0f, 0f, 1f);
    }

    private void BuildLeftPanel(Transform parent)
    {
        Image panel = CreateImage("Archive Index Panel", parent,
            ImageRole.Panel, true);
        SetLeftStretch(panel.rectTransform, OuterPadding, OuterPadding,
            LeftPanelWidth, TopBarHeight + OuterPadding);

        Text categoryLabel = CreateText("Category Label", panel.transform,
            "分类  //  CATEGORY", 14, TextAnchor.MiddleLeft, false,
            FontStyle.Bold);
        SetTopStretch(categoryLabel.rectTransform, 14f, 10f, 14f, 27f);

        GameObject categoryObject = new GameObject("Category Buttons",
            typeof(RectTransform), typeof(VerticalLayoutGroup));
        categoryObject.transform.SetParent(panel.transform, false);
        _categoryContent = categoryObject.GetComponent<RectTransform>();
        SetTopStretch(_categoryContent, 10f, 42f, 10f, 204f);
        VerticalLayoutGroup categoryLayout = categoryObject.GetComponent<VerticalLayoutGroup>();
        categoryLayout.padding = new RectOffset(0, 0, 0, 0);
        categoryLayout.spacing = 4f;
        categoryLayout.childAlignment = TextAnchor.UpperCenter;
        categoryLayout.childControlWidth = true;
        categoryLayout.childControlHeight = true;
        categoryLayout.childForceExpandWidth = true;
        categoryLayout.childForceExpandHeight = false;

        Image divider = CreateImage("Index Divider", panel.transform,
            ImageRole.Accent, false);
        SetTopStretch(divider.rectTransform, 10f, 252f, 10f, 1f);

        _categoryCaptionText = CreateText("Record List Label", panel.transform,
            "记录  //  RECORDS", 14, TextAnchor.MiddleLeft, false,
            FontStyle.Bold);
        SetTopStretch(_categoryCaptionText.rectTransform, 14f, 258f, 14f, 28f);

        _entryScroll = CreateListScroll("Archive Entry Scroll", panel.transform,
            out _entryContent);
        RectTransform scrollRect = _entryScroll.GetComponent<RectTransform>();
        Stretch(scrollRect, 10f, 10f, 10f, 291f);
    }

    private void BuildDetailPanel(Transform parent)
    {
        Image panel = CreateImage("Archive Detail Panel", parent,
            ImageRole.Panel, true);
        SetRightStretch(panel.rectTransform,
            OuterPadding + LeftPanelWidth + ColumnGap, OuterPadding,
            OuterPadding, TopBarHeight + OuterPadding);

        Image header = CreateImage("Archive Detail Header", panel.transform,
            ImageRole.Raised, true);
        SetTopStretch(header.rectTransform, 8f, 8f, 8f, 136f);

        _entryTitleText = CreateText("Entry Title", header.transform,
            "选择一条记录", 25, TextAnchor.UpperLeft, true, FontStyle.Bold);
        SetTopStretch(_entryTitleText.rectTransform, 16f, 12f, 16f, 34f);

        _entryStatusText = CreateText("Entry Status", header.transform,
            string.Empty, 14, TextAnchor.MiddleLeft, false, FontStyle.Bold);
        SetTopStretch(_entryStatusText.rectTransform, 16f, 51f, 16f, 24f);

        _entryMetadataText = CreateText("Entry Metadata", header.transform,
            string.Empty, 13, TextAnchor.UpperLeft, false, FontStyle.Normal);
        SetTopStretch(_entryMetadataText.rectTransform, 16f, 77f, 16f, 50f);
        _entryMetadataText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _entryMetadataText.verticalOverflow = VerticalWrapMode.Truncate;

        Image divider = CreateImage("Detail Divider", panel.transform,
            ImageRole.Accent, false);
        SetTopStretch(divider.rectTransform, 8f, 151f, 8f, 1f);

        _bodyScroll = CreateBodyScroll("Archive Body Scroll", panel.transform,
            out _entryBodyText);
        RectTransform bodyScrollRect = _bodyScroll.GetComponent<RectTransform>();
        Stretch(bodyScrollRect, 16f, 12f, 12f, 162f);
    }

    private void RebuildLibrary(bool preserveSelection)
    {
        if (_catalog == null || _runtimeRoot == null) return;

        string requestedCategory = preserveSelection ? _selectedCategoryId : null;
        string requestedEntry = preserveSelection ? _selectedEntryId : null;
        IReadOnlyList<RougeArchiveCategory> categories =
            _catalog.GetCategories(_selectedLibrary);

        ClearButtonContent(_categoryButtons, _categoryContent);

        RougeArchiveCategory chosenCategory = null;
        for (int i = 0; i < categories.Count; i++)
        {
            RougeArchiveCategory category = categories[i];
            if (chosenCategory == null || string.Equals(category.StableId,
                    requestedCategory, StringComparison.Ordinal))
                chosenCategory = category;

            Button button = CreateButton("Category - " + category.StableId,
                _categoryContent, category.Title, 15, TextAnchor.MiddleLeft);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 29f;
            layout.preferredHeight = 29f;
            string categoryId = category.StableId;
            button.onClick.AddListener(() => SelectCategory(categoryId, null));
            _categoryButtons.Add(new BoundButton(categoryId, button));
        }

        _terminalTitleText.text = RougeArchiveCatalog.GetLibraryTitle(
            _selectedLibrary).Replace(" // ", "\n");

        if (chosenCategory == null)
        {
            _selectedCategoryId = null;
            _selectedEntryId = null;
            ClearButtonContent(_entryButtons, _entryContent);
            ShowEmptyDetail("当前资料库没有可读记录。", string.Empty);
            UpdateAllButtonVisuals();
            return;
        }

        SelectCategory(chosenCategory.StableId, requestedEntry);
    }

    private void SelectCategory(string categoryId, string preferredEntryId)
    {
        _selectedCategoryId = categoryId;
        _selectedEntryId = null;
        _selectedEntryButton = null;
        ClearButtonContent(_entryButtons, _entryContent);

        RougeArchiveCategory selectedCategory = FindCategory(categoryId);
        _categoryCaptionText.text = selectedCategory != null
            ? selectedCategory.Title + "  //  RECORDS"
            : "记录  //  RECORDS";

        IReadOnlyList<RougeArchiveEntry> entries = _catalog.GetEntries(categoryId);
        RougeArchiveEntry chosenEntry = null;
        for (int i = 0; i < entries.Count; i++)
        {
            RougeArchiveEntry entry = entries[i];
            if (chosenEntry == null || string.Equals(entry.StableId,
                    preferredEntryId, StringComparison.Ordinal))
                chosenEntry = entry;

            string label = entry.StableId + "\n" + entry.Title;
            Button button = CreateButton("Entry - " + entry.StableId,
                _entryContent, label, 15, TextAnchor.MiddleLeft);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 52f;
            layout.preferredHeight = 52f;
            string entryId = entry.StableId;
            button.onClick.AddListener(() => SelectEntry(entryId, true));
            _entryButtons.Add(new BoundButton(entryId, button));
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_entryContent);
        _entryScroll.verticalNormalizedPosition = 1f;

        if (chosenEntry != null)
            SelectEntry(chosenEntry.StableId, true);
        else
            ShowEmptyDetail("此分类没有可读记录。",
                selectedCategory != null ? selectedCategory.Subtitle : string.Empty);
        UpdateAllButtonVisuals();
    }

    private void SelectEntry(string stableId, bool resetScroll)
    {
        if (!_catalog.TryGetEntry(stableId, out RougeArchiveEntry entry) ||
            entry.Library != _selectedLibrary ||
            !string.Equals(entry.CategoryId, _selectedCategoryId,
                StringComparison.Ordinal))
            return;

        _selectedEntryId = entry.StableId;
        _entryTitleText.text = entry.Title;
        _entryStatusText.text = entry.StableId + "   //   " + entry.Status;
        _entryMetadataText.text = BuildMetadata(entry);
        _entryBodyText.text = BuildBody(entry);

        _selectedEntryButton = null;
        for (int i = 0; i < _entryButtons.Count; i++)
            if (string.Equals(_entryButtons[i].Id, entry.StableId,
                    StringComparison.Ordinal))
            {
                _selectedEntryButton = _entryButtons[i].Button;
                break;
            }

        UpdateAllButtonVisuals();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_entryBodyText.rectTransform);
        if (resetScroll) _bodyScroll.verticalNormalizedPosition = 1f;
    }

    private RougeArchiveCategory FindCategory(string categoryId)
    {
        IReadOnlyList<RougeArchiveCategory> categories =
            _catalog.GetCategories(_selectedLibrary);
        for (int i = 0; i < categories.Count; i++)
            if (string.Equals(categories[i].StableId, categoryId,
                    StringComparison.Ordinal))
                return categories[i];
        return null;
    }

    private void ShowEmptyDetail(string title, string body)
    {
        _selectedEntryButton = null;
        _entryTitleText.text = title;
        _entryStatusText.text = string.Empty;
        _entryMetadataText.text = string.Empty;
        _entryBodyText.text = body ?? string.Empty;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_entryBodyText.rectTransform);
        _bodyScroll.verticalNormalizedPosition = 1f;
    }

    private static string BuildMetadata(RougeArchiveEntry entry)
    {
        string result = string.Empty;
        if (!string.IsNullOrEmpty(entry.Source)) result = "来源  //  " + entry.Source;
        if (!string.IsNullOrEmpty(entry.Reliability))
            result += (result.Length > 0 ? "\n" : string.Empty) +
                      "可信度  //  " + entry.Reliability;
        if (!string.IsNullOrEmpty(entry.Tags))
            result += (result.Length > 0 ? "\n" : string.Empty) +
                      "标签  //  " + entry.Tags;
        return result;
    }

    private static string BuildBody(RougeArchiveEntry entry)
    {
        string result = entry.Body;
        if (entry.RelatedIds.Count == 0) return result;

        result += "\n\n<b>关联记录  //  RELATED</b>\n";
        for (int i = 0; i < entry.RelatedIds.Count; i++)
        {
            if (i > 0) result += "   /   ";
            result += entry.RelatedIds[i];
        }
        return result;
    }

    private ScrollRect CreateListScroll(string name, Transform parent,
        out RectTransform content)
    {
        GameObject scrollObject = new GameObject(name, typeof(RectTransform),
            typeof(Image), typeof(ScrollRect));
        scrollObject.transform.SetParent(parent, false);
        Image inputSurface = scrollObject.GetComponent<Image>();
        inputSurface.color = new Color(0f, 0f, 0f, 0.001f);
        inputSurface.raycastTarget = true;

        GameObject viewportObject = new GameObject("Viewport",
            typeof(RectTransform), typeof(RectMask2D));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport, 0f, 0f, 0f, 0f);

        GameObject contentObject = new GameObject("Content", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 4, 0, 0);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.18f;
        scroll.scrollSensitivity = 30f;
        return scroll;
    }

    private ScrollRect CreateBodyScroll(string name, Transform parent, out Text body)
    {
        GameObject scrollObject = new GameObject(name, typeof(RectTransform),
            typeof(Image), typeof(ScrollRect));
        scrollObject.transform.SetParent(parent, false);
        Image inputSurface = scrollObject.GetComponent<Image>();
        inputSurface.color = new Color(0f, 0f, 0f, 0.001f);
        inputSurface.raycastTarget = true;

        GameObject viewportObject = new GameObject("Viewport",
            typeof(RectTransform), typeof(RectMask2D));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport, 0f, 0f, 0f, 0f);

        body = CreateText("Body", viewportObject.transform, string.Empty, 18,
            TextAnchor.UpperLeft, true, FontStyle.Normal);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;
        body.lineSpacing = 1.13f;
        RectTransform content = body.rectTransform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(-8f, 0f);
        ContentSizeFitter fitter = body.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.18f;
        scroll.scrollSensitivity = 34f;
        return scroll;
    }

    private Image CreateImage(string name, Transform parent, ImageRole role,
        bool raycastTarget)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform),
            typeof(Image));
        gameObject.transform.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.raycastTarget = raycastTarget;
        switch (role)
        {
            case ImageRole.Backdrop: _backdropImages.Add(image); break;
            case ImageRole.Panel:
                _panelImages.Add(image);
                AddPanelRails(gameObject.transform);
                break;
            case ImageRole.Raised:
                _raisedImages.Add(image);
                AddPanelRails(gameObject.transform);
                break;
            case ImageRole.Accent: _accentImages.Add(image); break;
            case ImageRole.Rail: _railImages.Add(image); break;
        }
        return image;
    }

    private Text CreateText(string name, Transform parent, string value,
        int size, TextAnchor alignment, bool primary, FontStyle style)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform),
            typeof(Text));
        gameObject.transform.SetParent(parent, false);
        Text text = gameObject.GetComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.text = value ?? string.Empty;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = true;
        _allTexts.Add(text);
        if (primary) _primaryTexts.Add(text);
        else _secondaryTexts.Add(text);
        return text;
    }

    private Button CreateButton(string name, Transform parent, string label,
        int size, TextAnchor alignment)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform),
            typeof(Image), typeof(Button));
        gameObject.transform.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.raycastTarget = true;
        Button button = gameObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        Text text = CreateText("Label", gameObject.transform, label, size,
            alignment, false, FontStyle.Normal);
        Stretch(text.rectTransform, 12f, 4f, 12f, 4f);
        _buttonLabels.Add(button, text);
        Image rail = CreateImage("Selection Rail", gameObject.transform,
            ImageRole.Rail, false);
        SetBottomStretch(rail.rectTransform, 0f, 0f, 0f, 2f);
        _buttonRails.Add(button, rail);
        return button;
    }

    private void UpdateAllButtonVisuals()
    {
        if (_theme == null) return;
        if (_libraryButtons != null)
            for (int i = 0; i < _libraryButtons.Length; i++)
                SetButtonSelected(_libraryButtons[i], i == (int)_selectedLibrary);

        for (int i = 0; i < _categoryButtons.Count; i++)
            SetButtonSelected(_categoryButtons[i].Button,
                string.Equals(_categoryButtons[i].Id, _selectedCategoryId,
                    StringComparison.Ordinal));
        for (int i = 0; i < _entryButtons.Count; i++)
            SetButtonSelected(_entryButtons[i].Button,
                string.Equals(_entryButtons[i].Id, _selectedEntryId,
                    StringComparison.Ordinal));
    }

    private void SetButtonSelected(Button button, bool selected)
    {
        if (button == null) return;
        Image image = button.targetGraphic as Image;
        if (image != null)
        {
            Color surface = selected
                ? Color.Lerp(_theme.UiPanelRaised, _theme.Accent, 0.26f)
                : _theme.UiPanelRaised;
            surface.a = selected ? 0.72f : 0.22f;
            image.color = surface;
        }
        if (_buttonRails.TryGetValue(button, out Image rail) && rail != null)
            rail.color = WithAlpha(_theme.Accent, selected ? 0.84f : 0.16f);
        if (_buttonLabels.TryGetValue(button, out Text label) && label != null)
            label.color = selected ? _theme.PrimaryText :
                Color.Lerp(_theme.SecondaryText, _theme.PrimaryText, 0.20f);

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    private void ApplyFont()
    {
        for (int i = 0; i < _allTexts.Count; i++)
            if (_allTexts[i] != null) _allTexts[i].font = _font;
    }

    private void AddPanelRails(Transform parent)
    {
        Image top = CreateImage("Archive Top Rail", parent,
            ImageRole.Rail, false);
        SetTopStretch(top.rectTransform, 0f, 0f, 0f, 1f);

        Image left = CreateImage("Archive Left Rail", parent,
            ImageRole.Rail, false);
        RectTransform rect = left.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = new Vector2(1f, 0f);

        Image right = CreateImage("Archive Right Rail", parent,
            ImageRole.Rail, false);
        rect = right.rectTransform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(1f, 0.5f);
        rect.offsetMin = new Vector2(-1f, 0f);
        rect.offsetMax = Vector2.zero;

        Image bottom = CreateImage("Archive Bottom Rail", parent,
            ImageRole.Rail, false);
        SetBottomStretch(bottom.rectTransform, 0f, 0f, 0f, 1f);
    }

    private static Font ResolveFallbackFont()
    {
        if (s_fallbackFont != null) return s_fallbackFont;
        s_fallbackFont = Font.CreateDynamicFontFromOSFont(new[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC",
            "Noto Sans CJK SC", "Arial"
        }, 20);
        if (s_fallbackFont == null)
            s_fallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return s_fallbackFont;
    }

    private static void SetImageColors(List<Image> images, Color color)
    {
        for (int i = 0; i < images.Count; i++)
            if (images[i] != null) images[i].color = color;
    }

    private static void SetTextColors(List<Text> texts, Color color)
    {
        for (int i = 0; i < texts.Count; i++)
            if (texts[i] != null) texts[i].color = color;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static void ClearChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(child);
            else UnityEngine.Object.DestroyImmediate(child);
        }
    }

    private void ClearButtonContent(List<BoundButton> buttons,
        RectTransform parent)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i].Button;
            if (button == null || !_buttonLabels.TryGetValue(button, out Text label))
                continue;
            _buttonLabels.Remove(button);
            _buttonRails.Remove(button);
            _allTexts.Remove(label);
            _primaryTexts.Remove(label);
            _secondaryTexts.Remove(label);
        }
        buttons.Clear();
        ClearChildren(parent);
    }

    private static void Stretch(RectTransform rect, float left, float bottom,
        float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetTopStretch(RectTransform rect, float left, float top,
        float right, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetBottomStretch(RectTransform rect, float left,
        float bottom, float right, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, bottom + height);
    }

    private static void SetTopLeft(RectTransform rect, float left, float top,
        float width, float height)
    {
        rect.anchorMin = Vector2.up;
        rect.anchorMax = Vector2.up;
        rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetTopRight(RectTransform rect, float right, float top,
        float width, float height)
    {
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetLeftStretch(RectTransform rect, float left,
        float bottom, float width, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(left + width, -top);
    }

    private static void SetRightStretch(RectTransform rect, float left,
        float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private enum ImageRole
    {
        Backdrop,
        Panel,
        Raised,
        Accent,
        Rail
    }
}
