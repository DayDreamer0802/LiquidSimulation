using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class RougeTowerDefenseMapLoader
{
    private IEnumerator RunCommanderSelectionStartup()
    {
        GameObject viewObject = new GameObject(
            "Commander Startup Flow", typeof(RectTransform));
        viewObject.transform.SetParent(transform, false);
        _commanderSelectionView = viewObject.AddComponent<RougeCommanderSelectionView>();
        _commanderSelectionView.BeginLoading(commanderConfigName);
        RougeAudioVisualizerPlayer.EnterSelectionMusic();

        // Let the loading layer render before synchronous package validation begins.
        yield return null;
        float loadingStarted = Time.unscaledTime;

        string preferredCommander =
            RougeAutoplayCommanderJson.SelectedCommanderName;
        string preferredLocale =
            RougeAutoplayCommanderJson.SelectedLocaleOverride;
        string[] packageNames =
            RougeAutoplayCommanderJson.DiscoverCommanderPackageNames(
                preferredCommander);
        List<RougeAutoplayCommanderDefinition> commanders =
            new List<RougeAutoplayCommanderDefinition>(packageNames.Length);
        List<Sprite> portraits = new List<Sprite>(packageNames.Length);
        bool loadedBuiltInLan = false;

        for (int i = 0; i < packageNames.Length; i++)
        {
            string packageName = packageNames[i];
            _commanderSelectionView.SetLoadingStage(
                $"正在加载角色包 {packageName}  ({i + 1}/{packageNames.Length})",
                "VALIDATING CORE + LOCALE // " + packageName);
            yield return null;
            string localeOverride = string.Equals(packageName,
                    preferredCommander, StringComparison.Ordinal)
                ? preferredLocale
                : string.Empty;
            if (!RougeAutoplayCommanderJson.TryLoadRegistryPackage(
                    packageName, localeOverride,
                    out RougeAutoplayCommanderDefinition commander,
                    out string report))
            {
                Debug.LogWarning("Commander registry skipped package '" +
                                 packageName + "': " + report, this);
                yield return null;
                continue;
            }
            if (!string.IsNullOrEmpty(report) &&
                report.IndexOf("\nWarnings:", StringComparison.Ordinal) >= 0)
                Debug.LogWarning("Commander package '" + packageName +
                                 "' loaded with normalization warnings: " +
                                 report, this);

            _commanderSelectionView.SetLoadingStage(
                $"正在载入 {commander.Name} 的视觉资源",
                "LOADING PORTRAIT // " + packageName);
            Sprite portrait = commander.ResolvePortraitSprite(
                RougeAutoplayCommanderPortraitEmotion.Calm,
                RougeAutoplayCommanderPortraitVariant.Base);
            if (portrait == null)
            {
                Debug.LogWarning("Commander registry skipped package '" +
                                 packageName +
                                 "': no usable portrait could be resolved.", this);
                yield return null;
                continue;
            }

            commanders.Add(commander);
            portraits.Add(portrait);
            if (string.Equals(commander.CommanderId,
                    RougeAutoplayCommanderJson.DefaultCommanderName,
                    StringComparison.Ordinal))
                loadedBuiltInLan = true;
            yield return null;
        }

        // Lan is the safety anchor of the registry even if its editable package was
        // accidentally damaged. Other invalid packages are console-only skips.
        if (!loadedBuiltInLan)
        {
            RougeAutoplayCommanderDefinition fallback =
                RougeAutoplayCommanderJson.CreateSafeBuiltInFallback();
            Sprite fallbackPortrait = fallback.ResolvePortraitSprite(
                RougeAutoplayCommanderPortraitEmotion.Calm,
                RougeAutoplayCommanderPortraitVariant.Base);
            commanders.Add(fallback);
            portraits.Add(fallbackPortrait);
            Debug.LogWarning("Built-in Lan package failed strict registry loading; " +
                             "registered the safe in-code Lan fallback.", this);
        }

        float minimumDuration = Mathf.Max(0.1f, commanderLoadingMinimumSeconds);
        while (Time.unscaledTime - loadingStarted < minimumDuration)
            yield return null;

        _commanderSelectionView.SetLoadingStage(
            $"角色注册表已就绪 · {commanders.Count} 个可用角色",
            "COMMANDER REGISTRY READY");
        _commanderSelectionView.BuildSelection(commanders, portraits,
            preferredCommander);
        yield return _commanderSelectionView.RevealSelection();
        yield return _commanderSelectionView.WaitForConfirmation();

        RougeAutoplayCommanderDefinition selected =
            _commanderSelectionView.SelectedCommander ?? commanders[0];
        commanderConfigName = selected.CommanderId;
        commanderLocaleOverride = selected.LocaleId;
        RougeAutoplayCommanderJson.ConfigureSelection(
            commanderConfigName, commanderLocaleOverride);
        // Resolve once here so the GameManager can only ever observe the committed
        // package, including when the originally requested package fell back to Lan.
        _ = RougeAutoplayCommanderJson.Active;
        ApplyCommanderVisualTheme(
            RougeCommanderVisualThemes.ResolveActive());

        // Build and prime the selected commander's battlefield while the opaque
        // commitment layer is still covering the scene. The first visible frame
        // is therefore the empty anchor zone, never a cyan map that later repaints.
        if (loadOnEnable) LoadMap();

        Debug.Log("Commander selected: " + selected.CommanderId + " [" +
                  selected.LocaleId + "]. Entering level.", this);
        RougeAudioVisualizerPlayer.ExitSelectionMusic();
        yield return _commanderSelectionView.PlayCommitAndFade();

        DisposeCommanderSelectionView();
        Time.timeScale = _timeScaleBeforeCommanderSelection;
        _commanderSelectionPending = false;
        _commanderStartupRoutine = null;
    }

    private void DisposeCommanderSelectionView()
    {
        if (_commanderSelectionView == null) return;
        GameObject viewObject = _commanderSelectionView.gameObject;
        _commanderSelectionView = null;
        if (Application.isPlaying) Destroy(viewObject);
        else DestroyImmediate(viewObject);
    }
}

public sealed class RougeCommanderSelectionView : MonoBehaviour
{
    private const float RosterCardHeight = 112f;
    private const float RosterCardSpacing = 12f;
    private const float RosterViewportHeight = 584f;
    private const float ThemeTransitionDuration = 0.45f;
    private const string SfxVolumePreference = "Rouge.Audio.SfxVolume";

    private static readonly Color BackdropColor =
        new Color(0.003f, 0.016f, 0.035f, 0.965f);
    private static readonly Color PanelColor =
        new Color(0.012f, 0.055f, 0.09f, 0.96f);
    private static readonly Color Cyan =
        new Color(0.08f, 0.82f, 1f, 1f);
    private static readonly Color CyanSoft =
        new Color(0.08f, 0.72f, 0.92f, 0.42f);
    private static readonly Color MainText =
        new Color(0.82f, 0.97f, 1f, 1f);
    private static readonly Color SecondaryText =
        new Color(0.45f, 0.68f, 0.76f, 1f);
    private static readonly Color Gold =
        new Color(1f, 0.71f, 0.28f, 1f);
    private static readonly Color WashBottomLeft =
        new Color(0.08f, 0.82f, 1f, 0.012f);
    private static readonly Color WashTopLeft =
        new Color(0.08f, 0.82f, 1f, 0.035f);
    private static readonly Color WashTopRight =
        new Color(0.24f, 0.92f, 1f, 0.055f);
    private static readonly Color WashBottomRight =
        new Color(0.08f, 0.72f, 0.92f, 0.022f);
    private static readonly Color ScanBandColor =
        new Color(0.24f, 0.92f, 1f, 0.095f);

    private sealed class ThemeGraphicState
    {
        public Graphic Graphic;
        public Color LanColor;
        public Color TaotaoColor;
    }

    private sealed class ThemeShadowState
    {
        public Shadow Shadow;
        public Color LanColor;
        public Color TaotaoColor;
    }

    private sealed class ThemeSelectableState
    {
        public Selectable Selectable;
        public ColorBlock LanColors;
        public ColorBlock TaotaoColors;
    }

    private static Font s_font;

    private CanvasGroup _canvasGroup;
    private CanvasGroup _loadingGroup;
    private CanvasGroup _selectionGroup;
    private RougeCommanderThemeWashGraphic _themeWash;
    private RougeCommanderThemeScanBandGraphic _themeScanBand;
    private RectTransform _themeScanBandRect;
    private RectTransform _loadingProgressFill;
    private Image[] _loadingSegments;
    private Text _loadingTitle;
    private Text _loadingProtocol;
    private RectTransform _heroStage;
    private RectTransform _heroScanline;
    private Image _heroPortrait;
    private Image _heroGlow;
    private Text _nameText;
    private Text _roleText;
    private Text _personaText;
    private Text _traitsText;
    private Text _backgroundText;
    private Text _thinkingText;
    private Text _talentText;
    private Text _sourceText;
    private Text _affinityTierText;
    private Text _affinityValueText;
    private RectTransform _affinityProgressFill;
    private Image _affinityProgressImage;
    private RougeCommanderRadarGraphic _radar;
    private Text[] _radarLabels;
    private RougeAutoplayCommanderDefinition[] _commanders =
        Array.Empty<RougeAutoplayCommanderDefinition>();
    private Sprite[] _commanderPortraits = Array.Empty<Sprite>();
    private RougeCommanderSelectionCard[] _cards =
        Array.Empty<RougeCommanderSelectionCard>();
    private Button[] _cardButtons = Array.Empty<Button>();
    private ScrollRect _rosterScroll;
    private RectTransform _rosterViewport;
    private RectTransform _rosterContent;
    private int _lockedCommanderIndex = -1;
    private Button _confirmButton;
    private Text _confirmLabel;
    private bool _confirmed;
    private float _loadingPhase;
    private float _selectionReveal;
    private float _affinityNormalized;
    private float _themeBlend;
    private float _themeTransitionFrom;
    private float _themeTransitionTarget;
    private float _themeTransitionElapsed;
    private float _themeSweepElapsed = 1f;
    private float _themeSweepDirection = 1f;
    private bool _themeBaselineCaptured;
    private bool _themeTransitionActive;
    private readonly List<ThemeGraphicState> _themeGraphics =
        new List<ThemeGraphicState>();
    private readonly List<ThemeShadowState> _themeShadows =
        new List<ThemeShadowState>();
    private readonly List<ThemeSelectableState> _themeSelectables =
        new List<ThemeSelectableState>();
    private readonly HashSet<Graphic> _capturedThemeGraphics =
        new HashSet<Graphic>();
    private readonly HashSet<Shadow> _capturedThemeShadows =
        new HashSet<Shadow>();
    private readonly HashSet<Selectable> _capturedThemeSelectables =
        new HashSet<Selectable>();
    private AudioSource _selectionVoiceSource;
    private readonly Dictionary<string, AudioClip[]> _selectionVoiceClips =
        new Dictionary<string, AudioClip[]>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lastSelectionVoiceIndices =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public RougeAutoplayCommanderDefinition SelectedCommander { get; private set; }

    public void BeginLoading(string requestedPackage)
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        RougeTowerDefenseUiLayout.ConfigureCanvasScaler(scaler);
        gameObject.AddComponent<GraphicRaycaster>();
        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 1f;
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;

        _selectionVoiceSource = gameObject.AddComponent<AudioSource>();
        _selectionVoiceSource.playOnAwake = false;
        _selectionVoiceSource.loop = false;
        _selectionVoiceSource.spatialBlend = 0f;
        _selectionVoiceSource.ignoreListenerPause = true;
        _selectionVoiceSource.priority = 64;
        _selectionVoiceSource.volume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(SfxVolumePreference, 1f));

        RectTransform rootRect = GetComponent<RectTransform>();
        Stretch(rootRect);
        BuildBackdrop(transform);

        GameObject loading = CreateRect("Loading", transform);
        Stretch(loading.GetComponent<RectTransform>());
        _loadingGroup = loading.AddComponent<CanvasGroup>();

        RougeCommanderSlantedGraphic panel = CreatePanel(
            "Loading Protocol Panel", loading.transform,
            new Color(0.008f, 0.04f, 0.072f, 0.97f), 34f);
        RectTransform panelRect = panel.rectTransform;
        SetCenter(panelRect, new Vector2(0f, 0f), new Vector2(860f, 230f));
        AddPanelBorder(panel.transform, 34f, CyanSoft, 2f);

        _loadingProtocol = CreateText("Protocol", panel.transform, 18,
            TextAnchor.MiddleLeft, Cyan);
        SetTopLeft(_loadingProtocol.rectTransform, new Vector2(54f, -36f),
            new Vector2(740f, 28f));
        _loadingProtocol.text = "SYSTEM BOOT // COMMANDER REGISTRY";
        _loadingProtocol.fontStyle = FontStyle.Bold;

        _loadingTitle = CreateText("Loading Stage", panel.transform, 30,
            TextAnchor.MiddleLeft, MainText);
        SetTopLeft(_loadingTitle.rectTransform, new Vector2(54f, -78f),
            new Vector2(740f, 42f));
        _loadingTitle.text = "正在发现指挥官包";
        _loadingTitle.fontStyle = FontStyle.Bold;

        Text packageText = CreateText("Requested Package", panel.transform, 17,
            TextAnchor.MiddleLeft, SecondaryText);
        SetTopLeft(packageText.rectTransform, new Vector2(54f, -124f),
            new Vector2(740f, 26f));
        packageText.text = "载入目标  /  " +
                           (string.IsNullOrWhiteSpace(requestedPackage)
                               ? "lan"
                               : requestedPackage.Trim());

        GameObject track = CreateImage("Loading Track", panel.transform,
            new Color(0.06f, 0.18f, 0.24f, 0.9f)).gameObject;
        RectTransform trackRect = track.GetComponent<RectTransform>();
        SetTopLeft(trackRect, new Vector2(54f, -176f),
            new Vector2(744f, 8f));
        _loadingProgressFill = CreateImage("Progress", track.transform, Cyan)
            .rectTransform;
        _loadingProgressFill.anchorMin = Vector2.zero;
        _loadingProgressFill.anchorMax = new Vector2(0f, 1f);
        _loadingProgressFill.pivot = new Vector2(0f, 0.5f);
        _loadingProgressFill.anchoredPosition = Vector2.zero;
        _loadingProgressFill.sizeDelta = new Vector2(0f, 0f);

        _loadingSegments = new Image[12];
        for (int i = 0; i < _loadingSegments.Length; i++)
        {
            Image segment = CreateImage("Signal " + (i + 1), panel.transform,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.14f));
            RectTransform rect = segment.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-54f - i * 10f, 25f);
            rect.sizeDelta = new Vector2(5f, 18f + i * 1.6f);
            _loadingSegments[i] = segment;
        }

        // Theme the loading layer before its first rendered frame. BuildSelection
        // later captures only newly-created controls, so this never turns a
        // previously remapped colour into a new baseline.
        CaptureCommanderThemeBaseline();
        ApplyCommanderThemeImmediate(requestedPackage);
    }

    public void SetLoadingStage(string title, string protocol)
    {
        if (_loadingTitle != null) _loadingTitle.text = title;
        if (_loadingProtocol != null)
            _loadingProtocol.text = "SYSTEM BOOT // " + protocol;
    }

    public void BuildSelection(
        IReadOnlyList<RougeAutoplayCommanderDefinition> commanders,
        IReadOnlyList<Sprite> portraits, string preferredCommanderId)
    {
        int count = commanders == null || portraits == null
            ? 0
            : Mathf.Min(commanders.Count, portraits.Count);
        if (count <= 0)
            throw new InvalidOperationException(
                "Commander selection requires at least one validated package.");

        _commanders = new RougeAutoplayCommanderDefinition[count];
        _commanderPortraits = new Sprite[count];
        _lockedCommanderIndex = 0;
        for (int i = 0; i < count; i++)
        {
            _commanders[i] = commanders[i];
            _commanderPortraits[i] = portraits[i];
            if (commanders[i] != null &&
                string.Equals(commanders[i].CommanderId, preferredCommanderId,
                    StringComparison.Ordinal))
                _lockedCommanderIndex = i;
        }
        SelectedCommander = _commanders[_lockedCommanderIndex];

        GameObject selection = CreateRect("Commander Selection", transform);
        Stretch(selection.GetComponent<RectTransform>());
        _selectionGroup = selection.AddComponent<CanvasGroup>();
        _selectionGroup.alpha = 0f;
        _selectionGroup.interactable = false;
        _selectionGroup.blocksRaycasts = false;

        BuildHeader(selection.transform, count);
        BuildRoster(selection.transform);
        BuildHeroPortrait(selection.transform,
            _commanderPortraits[_lockedCommanderIndex]);
        BuildDossier(selection.transform);
        BuildFooter(selection.transform);
        ConfigureNavigation();
        RefreshLockedCards();
        RenderCommander(_lockedCommanderIndex);
        CaptureCommanderThemeBaseline();
        ApplyCommanderThemeImmediate(SelectedCommander != null
            ? SelectedCommander.CommanderId
            : RougeAutoplayCommanderJson.DefaultCommanderName);
    }

    public IEnumerator RevealSelection()
    {
        float elapsed = 0f;
        const float duration = 0.24f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseOut(Mathf.Clamp01(elapsed / duration));
            if (_loadingGroup != null) _loadingGroup.alpha = 1f - t;
            if (_selectionGroup != null) _selectionGroup.alpha = t;
            _selectionReveal = t;
            yield return null;
        }

        if (_loadingGroup != null) _loadingGroup.gameObject.SetActive(false);
        if (_selectionGroup != null)
        {
            _selectionGroup.alpha = 1f;
            _selectionGroup.interactable = true;
            _selectionGroup.blocksRaycasts = true;
        }
        if (_lockedCommanderIndex >= 0 &&
            _lockedCommanderIndex < _cards.Length &&
            EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(
                _cards[_lockedCommanderIndex].gameObject);
    }

    public IEnumerator WaitForConfirmation()
    {
        while (!_confirmed) yield return null;
    }

    public IEnumerator PlayCommitAndFade()
    {
        if (_selectionGroup != null)
        {
            _selectionGroup.interactable = false;
            _selectionGroup.blocksRaycasts = false;
        }
        if (_confirmLabel != null) _confirmLabel.text = "链接建立  /  ENTERING";
        if (_confirmButton != null && _confirmButton.targetGraphic != null)
            _confirmButton.targetGraphic.color = Gold;

        float hold = 0f;
        while (hold < 0.12f)
        {
            hold += Time.unscaledDeltaTime;
            yield return null;
        }

        float elapsed = 0f;
        const float duration = 0.2f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (_canvasGroup != null) _canvasGroup.alpha = 1f - t;
            yield return null;
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _loadingPhase += dt;
        UpdateCommanderTheme(dt);
        UpdateThemeScanBand(dt);
        if (_loadingProgressFill != null && _loadingGroup != null &&
            _loadingGroup.gameObject.activeSelf)
        {
            float progress = 0.12f + Mathf.PingPong(_loadingPhase * 0.72f, 0.76f);
            _loadingProgressFill.anchorMax = new Vector2(progress, 1f);
            _loadingProgressFill.sizeDelta = Vector2.zero;
            for (int i = 0; i < _loadingSegments.Length; i++)
            {
                float wave = 0.5f + 0.5f * Mathf.Sin(
                    _loadingPhase * 7f - i * 0.62f);
                Color signal = RougeCommanderSelectionPalette.BlendLanToTaotao(
                    Cyan, _themeBlend);
                signal.a = Mathf.Lerp(0.12f, 0.9f, wave);
                _loadingSegments[i].color = signal;
            }
        }

        if (_heroScanline != null && _selectionReveal > 0f)
        {
            float stageHeight = _heroStage != null
                ? Mathf.Max(1f, _heroStage.rect.height)
                : 600f;
            float halfTravel = stageHeight * 0.5f + 4f;
            float y = Mathf.Lerp(-halfTravel, halfTravel,
                Mathf.Repeat(_loadingPhase * 0.19f, 1f));
            _heroScanline.anchoredPosition = new Vector2(0f, y);
        }

    }

    private void BuildBackdrop(Transform parent)
    {
        Image backdrop = CreateImage("Deep Navy Backdrop", parent, BackdropColor);
        Stretch(backdrop.rectTransform);
        // The startup screen is modal. Consume clicks on otherwise empty space so
        // they cannot reach the paused gameplay UI beneath this canvas.
        backdrop.raycastTarget = true;

        GameObject washObject = CreateRect("Commander Theme Wash", parent);
        Stretch(washObject.GetComponent<RectTransform>());
        _themeWash = washObject.AddComponent<RougeCommanderThemeWashGraphic>();
        _themeWash.raycastTarget = false;
        _themeWash.SetCornerColors(WashBottomLeft, WashTopLeft,
            WashTopRight, WashBottomRight);

        for (int i = 1; i < 16; i++)
        {
            Image line = CreateImage("Vertical Grid " + i, parent,
                new Color(0.05f, 0.52f, 0.72f, i % 4 == 0 ? 0.11f : 0.045f));
            RectTransform rect = line.rectTransform;
            rect.anchorMin = new Vector2(i / 16f, 0f);
            rect.anchorMax = new Vector2(i / 16f, 1f);
            rect.sizeDelta = new Vector2(i % 4 == 0 ? 2f : 1f, 0f);
        }
        for (int i = 1; i < 9; i++)
        {
            Image line = CreateImage("Horizontal Grid " + i, parent,
                new Color(0.05f, 0.52f, 0.72f, i % 2 == 0 ? 0.09f : 0.04f));
            RectTransform rect = line.rectTransform;
            rect.anchorMin = new Vector2(0f, i / 9f);
            rect.anchorMax = new Vector2(1f, i / 9f);
            rect.sizeDelta = new Vector2(0f, i % 2 == 0 ? 2f : 1f);
        }

        GameObject scanBandObject = CreateRect(
            "Commander Theme Diagonal Scan", parent);
        _themeScanBandRect = scanBandObject.GetComponent<RectTransform>();
        _themeScanBandRect.anchorMin = _themeScanBandRect.anchorMax =
            new Vector2(0.5f, 0.5f);
        _themeScanBandRect.pivot = new Vector2(0.5f, 0.5f);
        _themeScanBandRect.sizeDelta = new Vector2(2600f, 176f);
        _themeScanBandRect.localRotation = Quaternion.Euler(0f, 0f, -16f);
        _themeScanBand =
            scanBandObject.AddComponent<RougeCommanderThemeScanBandGraphic>();
        _themeScanBand.color = ScanBandColor;
        _themeScanBand.raycastTarget = false;
        UpdateThemeScanBand(0f);

        Image topGlow = CreateImage("Top Signal", parent,
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.34f));
        topGlow.rectTransform.anchorMin = new Vector2(0f, 1f);
        topGlow.rectTransform.anchorMax = new Vector2(1f, 1f);
        topGlow.rectTransform.pivot = new Vector2(0.5f, 1f);
        topGlow.rectTransform.sizeDelta = new Vector2(0f, 3f);
    }

    private void BuildHeader(Transform parent, int availableCount)
    {
        Text eyebrow = CreateText("Eyebrow", parent, 16,
            TextAnchor.MiddleLeft, Cyan);
        SetTopLeft(eyebrow.rectTransform, new Vector2(64f, -38f),
            new Vector2(700f, 24f));
        eyebrow.text = "COMMANDER LINK  //  TACTICAL HANDOFF PROTOCOL";
        eyebrow.fontStyle = FontStyle.Bold;

        Text title = CreateText("Title", parent, 42,
            TextAnchor.MiddleLeft, MainText);
        SetTopLeft(title.rectTransform, new Vector2(64f, -65f),
            new Vector2(700f, 56f));
        title.text = "选择指挥官";
        title.fontStyle = FontStyle.Bold;

        Text count = CreateText("Available Count", parent, 17,
            TextAnchor.MiddleRight, SecondaryText);
        SetTopRight(count.rectTransform, new Vector2(-64f, -54f),
            new Vector2(420f, 36f));
        count.text = "LOCAL REGISTRY  /  可用角色 " +
                     Mathf.Max(0, availableCount).ToString("D2");
    }

    private void BuildRoster(Transform parent)
    {
        Text label = CreateText("Roster Label", parent, 16,
            TextAnchor.MiddleLeft, SecondaryText);
        SetTopLeft(label.rectTransform, new Vector2(64f, -174f),
            new Vector2(390f, 24f));
        label.text = _commanders.Length > 6
            ? "AVAILABLE // 可选角色  ·  SCROLL"
            : "AVAILABLE // 可选角色";

        Image scrollSurface = CreateImage("Commander Roster", parent,
            new Color(0.01f, 0.08f, 0.12f, 0.01f));
        scrollSurface.raycastTarget = true;
        RectTransform scrollRect = scrollSurface.rectTransform;
        SetTopLeft(scrollRect, new Vector2(60f, -204f),
            new Vector2(390f, RosterViewportHeight));
        _rosterScroll = scrollSurface.gameObject.AddComponent<ScrollRect>();
        _rosterScroll.horizontal = false;
        _rosterScroll.vertical = true;
        _rosterScroll.movementType = ScrollRect.MovementType.Clamped;
        _rosterScroll.inertia = true;
        _rosterScroll.decelerationRate = 0.18f;
        _rosterScroll.scrollSensitivity = 34f;

        GameObject viewportObject = CreateRect("Roster Viewport",
            scrollSurface.transform);
        _rosterViewport = viewportObject.GetComponent<RectTransform>();
        Stretch(_rosterViewport);
        viewportObject.AddComponent<RectMask2D>();

        GameObject contentObject = CreateRect("Roster Content",
            viewportObject.transform);
        _rosterContent = contentObject.GetComponent<RectTransform>();
        _rosterContent.anchorMin = new Vector2(0f, 1f);
        _rosterContent.anchorMax = new Vector2(1f, 1f);
        _rosterContent.pivot = new Vector2(0.5f, 1f);
        _rosterContent.anchoredPosition = Vector2.zero;
        float contentHeight = Mathf.Max(RosterViewportHeight,
            _commanders.Length * (RosterCardHeight + RosterCardSpacing) -
            RosterCardSpacing);
        _rosterContent.sizeDelta = new Vector2(0f, contentHeight);
        _rosterScroll.viewport = _rosterViewport;
        _rosterScroll.content = _rosterContent;
        _rosterScroll.verticalNormalizedPosition = 1f;

        _cards = new RougeCommanderSelectionCard[_commanders.Length];
        _cardButtons = new Button[_commanders.Length];
        for (int i = 0; i < _commanders.Length; i++)
            BuildCommanderCard(i);
    }

    private void BuildCommanderCard(int commanderIndex)
    {
        RougeAutoplayCommanderDefinition commander =
            _commanders[commanderIndex];
        Sprite portrait = _commanderPortraits[commanderIndex];
        GameObject cardObject = CreateRect(
            "Commander Card - " + commander.Name, _rosterContent);
        RectTransform cardRect = cardObject.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0f, 1f);
        cardRect.anchorMax = new Vector2(0f, 1f);
        cardRect.pivot = new Vector2(0f, 1f);
        cardRect.anchoredPosition = new Vector2(4f,
            -commanderIndex * (RosterCardHeight + RosterCardSpacing));
        cardRect.sizeDelta = new Vector2(376f, RosterCardHeight);

        RougeCommanderSlantedGraphic fill =
            cardObject.AddComponent<RougeCommanderSlantedGraphic>();
        fill.color = new Color(0.018f, 0.09f, 0.14f, 0.98f);
        fill.Slant = 18f;
        cardObject.AddComponent<Mask>().showMaskGraphic = true;

        Image portraitBackdrop = CreateImage("Portrait Backdrop",
            cardObject.transform, new Color(0.02f, 0.18f, 0.24f, 0.8f));
        SetTopLeft(portraitBackdrop.rectTransform, new Vector2(8f, -7f),
            new Vector2(98f, 98f));
        Image image = CreateImage("Portrait", portraitBackdrop.transform,
            Color.white);
        Stretch(image.rectTransform, 2f);
        image.sprite = portrait;
        image.preserveAspect = true;
        image.raycastTarget = false;

        Image accent = CreateImage("Selection Accent", cardObject.transform,
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.72f));
        SetTopLeft(accent.rectTransform, new Vector2(0f, -8f),
            new Vector2(4f, 96f));
        accent.raycastTarget = false;

        Text index = CreateText("Index", cardObject.transform, 14,
            TextAnchor.MiddleLeft, Cyan);
        SetTopLeft(index.rectTransform, new Vector2(120f, -9f),
            new Vector2(226f, 21f));
        bool builtIn = string.Equals(commander.CommanderId,
            RougeAutoplayCommanderJson.DefaultCommanderName,
            StringComparison.Ordinal);
        index.text = (commanderIndex + 1).ToString("D2") + "  /  " +
                     (builtIn ? "BUILT-IN" : "RESOURCE MOD");

        Text name = CreateText("Name", cardObject.transform, 29,
            TextAnchor.MiddleLeft, MainText);
        SetTopLeft(name.rectTransform, new Vector2(120f, -31f),
            new Vector2(226f, 38f));
        name.text = commander.Name;
        name.fontStyle = FontStyle.Bold;

        Text persona = CreateText("Persona", cardObject.transform, 15,
            TextAnchor.MiddleLeft, SecondaryText);
        SetTopLeft(persona.rectTransform, new Vector2(120f, -73f),
            new Vector2(226f, 28f));
        persona.text = commander.Persona;
        persona.resizeTextForBestFit = true;
        persona.resizeTextMinSize = 13;
        persona.resizeTextMaxSize = 15;

        RougeCommanderSlantedGraphic border = AddPanelBorder(
            cardObject.transform, 18f, CyanSoft, 2f);
        Stretch(border.rectTransform);

        Button button = cardObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = fill;
        int capturedIndex = commanderIndex;
        button.onClick.AddListener(() => SelectCommander(capturedIndex));
        RougeCommanderSelectionCard card =
            cardObject.AddComponent<RougeCommanderSelectionCard>();
        card.Initialize(fill, border, image,
            cardRect.anchoredPosition);
        _cards[commanderIndex] = card;
        _cardButtons[commanderIndex] = button;
    }

    private void BuildHeroPortrait(Transform parent, Sprite portrait)
    {
        RougeCommanderSlantedGraphic stage = CreatePanel(
            "Portrait Stage", parent, new Color(0.005f, 0.035f, 0.065f, 0.72f), 46f);
        RectTransform stageRect = stage.rectTransform;
        stageRect.anchorMin = new Vector2(0.235f, 0.13f);
        stageRect.anchorMax = new Vector2(0.56f, 0.86f);
        stageRect.offsetMin = Vector2.zero;
        stageRect.offsetMax = Vector2.zero;
        _heroStage = stageRect;
        stage.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        _heroGlow = CreateImage("Portrait Glow", stage.transform,
            new Color(0.1f, 0.75f, 1f, 0.16f));
        Stretch(_heroGlow.rectTransform, -10f);
        _heroGlow.sprite = portrait;
        _heroGlow.preserveAspect = true;

        _heroPortrait = CreateImage("Commander Portrait", stage.transform,
            Color.white);
        Stretch(_heroPortrait.rectTransform, 8f);
        _heroPortrait.sprite = portrait;
        _heroPortrait.preserveAspect = true;

        Image scanline = CreateImage("Portrait Scanline", stage.transform,
            new Color(0.35f, 0.95f, 1f, 0.24f));
        _heroScanline = scanline.rectTransform;
        _heroScanline.anchorMin = new Vector2(0f, 0.5f);
        _heroScanline.anchorMax = new Vector2(1f, 0.5f);
        _heroScanline.pivot = new Vector2(0.5f, 0.5f);
        _heroScanline.sizeDelta = new Vector2(0f, 4f);

        RougeCommanderSlantedGraphic border = AddPanelBorder(
            parent, 46f, new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f), 2f);
        CopyRect(border.rectTransform, stageRect);
    }

    private void BuildDossier(Transform parent)
    {
        RougeCommanderSlantedGraphic panel = CreatePanel(
            "Commander Dossier", parent, PanelColor, 28f);
        RectTransform rect = panel.rectTransform;
        rect.anchorMin = new Vector2(0.58f, 0.14f);
        rect.anchorMax = new Vector2(0.965f, 0.86f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        AddPanelBorder(panel.transform, 28f,
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.38f), 2f);

        _sourceText = CreateText("Source", panel.transform, 15,
            TextAnchor.MiddleLeft, Cyan);
        SetTopLeft(_sourceText.rectTransform, new Vector2(38f, -26f),
            new Vector2(640f, 24f));

        _nameText = CreateText("Commander Name", panel.transform, 42,
            TextAnchor.MiddleLeft, MainText);
        SetTopLeft(_nameText.rectTransform, new Vector2(38f, -53f),
            new Vector2(360f, 54f));
        _nameText.fontStyle = FontStyle.Bold;

        _roleText = CreateText("Role", panel.transform, 19,
            TextAnchor.MiddleLeft, SecondaryText);
        SetTopLeft(_roleText.rectTransform, new Vector2(40f, -111f),
            new Vector2(350f, 28f));

        _personaText = CreateText("Persona", panel.transform, 19,
            TextAnchor.MiddleLeft, Gold);
        SetTopLeft(_personaText.rectTransform, new Vector2(40f, -143f),
            new Vector2(350f, 28f));

        _traitsText = CreateText("Traits", panel.transform, 16,
            TextAnchor.UpperLeft, MainText);
        SetTopLeft(_traitsText.rectTransform, new Vector2(40f, -180f),
            new Vector2(350f, 44f));

        RougeCommanderSlantedGraphic affinitySurface = CreatePanel(
            "Affinity Surface", panel.transform,
            new Color(0.018f, 0.12f, 0.17f, 0.94f), 12f);
        SetTopLeft(affinitySurface.rectTransform, new Vector2(420f, -64f),
            new Vector2(260f, 144f));
        AddPanelBorder(affinitySurface.transform, 12f,
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.48f), 1.5f);

        Text affinityTitle = CreateText("Affinity Title",
            affinitySurface.transform, 13, TextAnchor.MiddleLeft, Cyan);
        SetTopLeft(affinityTitle.rectTransform, new Vector2(18f, -13f),
            new Vector2(224f, 22f));
        affinityTitle.text = "LINK AFFINITY // 默契";

        _affinityTierText = CreateText("Affinity Tier",
            affinitySurface.transform, 23, TextAnchor.MiddleLeft, Gold);
        SetTopLeft(_affinityTierText.rectTransform, new Vector2(18f, -39f),
            new Vector2(132f, 34f));
        _affinityTierText.fontStyle = FontStyle.Bold;

        _affinityValueText = CreateText("Affinity Value",
            affinitySurface.transform, 17, TextAnchor.MiddleRight, MainText);
        SetTopRight(_affinityValueText.rectTransform, new Vector2(-18f, -43f),
            new Vector2(96f, 28f));

        Image affinityTrack = CreateImage("Affinity Track",
            affinitySurface.transform, new Color(0.05f, 0.24f, 0.3f, 0.92f));
        SetTopLeft(affinityTrack.rectTransform, new Vector2(18f, -82f),
            new Vector2(224f, 10f));
        _affinityProgressImage = CreateImage("Affinity Progress",
            affinityTrack.transform, Cyan);
        _affinityProgressFill = _affinityProgressImage.rectTransform;
        _affinityProgressFill.anchorMin = Vector2.zero;
        _affinityProgressFill.anchorMax = new Vector2(0f, 1f);
        _affinityProgressFill.pivot = new Vector2(0f, 0.5f);
        _affinityProgressFill.anchoredPosition = Vector2.zero;
        _affinityProgressFill.sizeDelta = Vector2.zero;

        Text affinityHint = CreateText("Affinity Hint",
            affinitySurface.transform, 12, TextAnchor.MiddleLeft, SecondaryText);
        SetTopLeft(affinityHint.rectTransform, new Vector2(18f, -102f),
            new Vector2(224f, 24f));
        affinityHint.text = "随战局互动累积  ·  记录到角色档案";

        Text backgroundLabel = CreateText("Background Label", panel.transform,
            14, TextAnchor.MiddleLeft, Cyan);
        SetTopLeft(backgroundLabel.rectTransform, new Vector2(40f, -236f),
            new Vector2(300f, 22f));
        backgroundLabel.text = "BACKGROUND // 背景";
        _backgroundText = CreateText("Background", panel.transform, 18,
            TextAnchor.UpperLeft, SecondaryText);
        SetTopLeft(_backgroundText.rectTransform, new Vector2(40f, -263f),
            new Vector2(300f, 132f));
        _backgroundText.resizeTextForBestFit = true;
        _backgroundText.resizeTextMinSize = 16;
        _backgroundText.resizeTextMaxSize = 18;
        _backgroundText.lineSpacing = 1.08f;

        Text thinkingLabel = CreateText("Thinking Label", panel.transform,
            14, TextAnchor.MiddleLeft, Cyan);
        SetTopLeft(thinkingLabel.rectTransform, new Vector2(365f, -236f),
            new Vector2(315f, 22f));
        thinkingLabel.text = "DECISION MODEL // 思考方式";
        _thinkingText = CreateText("Thinking", panel.transform, 18,
            TextAnchor.UpperLeft, SecondaryText);
        SetTopLeft(_thinkingText.rectTransform, new Vector2(365f, -263f),
            new Vector2(315f, 132f));
        _thinkingText.resizeTextForBestFit = true;
        _thinkingText.resizeTextMinSize = 16;
        _thinkingText.resizeTextMaxSize = 18;
        _thinkingText.lineSpacing = 1.08f;

        Text radarTitle = CreateText("Radar Title", panel.transform, 14,
            TextAnchor.MiddleLeft, Cyan);
        SetBottomLeft(radarTitle.rectTransform, new Vector2(40f, 286f),
            new Vector2(360f, 22f));
        radarTitle.text = "TACTICAL STYLE // 六维对立倾向";

        GameObject radarObject = CreateRect("Tactical Spectrum", panel.transform);
        RectTransform radarRect = radarObject.GetComponent<RectTransform>();
        SetBottomLeft(radarRect, new Vector2(42f, 48f),
            new Vector2(270f, 224f));
        _radar = radarObject.AddComponent<RougeCommanderRadarGraphic>();
        _radar.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f);

        _radarLabels = new Text[6];
        Vector2[] labelPositions =
        {
            new Vector2(74f, 208f), new Vector2(186f, 154f),
            new Vector2(185f, 43f), new Vector2(74f, -4f),
            new Vector2(-34f, 43f), new Vector2(-34f, 154f)
        };
        string[] names = { "存钱", "控场", "攻坚", "铺塔", "纯伤", "清群" };
        for (int i = 0; i < 6; i++)
        {
            Text axis = CreateText("Axis " + names[i], panel.transform, 14,
                TextAnchor.MiddleCenter, SecondaryText);
            RectTransform axisRect = axis.rectTransform;
            SetBottomLeft(axisRect,
                new Vector2(42f + labelPositions[i].x,
                    48f + labelPositions[i].y),
                new Vector2(82f, 24f));
            _radarLabels[i] = axis;
        }

        Text budget = CreateText("Spectrum Rule", panel.transform, 14,
            TextAnchor.UpperLeft, SecondaryText);
        SetBottomLeft(budget.rectTransform, new Vector2(350f, 194f),
            new Vector2(310f, 74f));
        budget.text = "存钱 ↔ 铺塔\n控场减益 ↔ 直接伤害\n清群 ↔ 攻坚（单体/破甲/精英首领）\n每项 0–50 · 每对合计 50";

        _talentText = CreateText("Talent", panel.transform, 17,
            TextAnchor.UpperLeft, MainText);
        SetBottomLeft(_talentText.rectTransform, new Vector2(350f, 66f),
            new Vector2(310f, 112f));
        _talentText.resizeTextForBestFit = true;
        _talentText.resizeTextMinSize = 15;
        _talentText.resizeTextMaxSize = 17;
        _talentText.lineSpacing = 1.08f;
    }

    private void BuildFooter(Transform parent)
    {
        Text hint = CreateText("Input Hint", parent, 15,
            TextAnchor.MiddleLeft, SecondaryText);
        SetBottomLeft(hint.rectTransform, new Vector2(64f, 42f),
            new Vector2(720f, 32f));
        hint.text = "悬停高亮   ·   点击角色切换并试听语音   ·   确认后进入战区";

        RougeCommanderSlantedGraphic surface = CreatePanel(
            "Confirm Surface", parent, new Color(0.035f, 0.22f, 0.3f, 0.98f),
            20f);
        surface.raycastTarget = true;
        RectTransform surfaceRect = surface.rectTransform;
        surfaceRect.anchorMin = new Vector2(1f, 0f);
        surfaceRect.anchorMax = new Vector2(1f, 0f);
        surfaceRect.pivot = new Vector2(1f, 0f);
        surfaceRect.anchoredPosition = new Vector2(-68f, 34f);
        surfaceRect.sizeDelta = new Vector2(354f, 68f);
        _confirmButton = surface.gameObject.AddComponent<Button>();
        _confirmButton.targetGraphic = surface;
        ColorBlock colors = _confirmButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.72f, 0.82f, 0.86f, 1f);
        colors.fadeDuration = 0.08f;
        _confirmButton.colors = colors;
        _confirmButton.onClick.AddListener(ConfirmSelection);
        AddPanelBorder(surface.transform, 20f,
            new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f), 2f);
        _confirmLabel = CreateText("Confirm Label", surface.transform, 20,
            TextAnchor.MiddleCenter, MainText);
        Stretch(_confirmLabel.rectTransform, 8f);
        _confirmLabel.text = "确认部署  /  ENTER";
        _confirmLabel.fontStyle = FontStyle.Bold;
    }

    private void ConfigureNavigation()
    {
        for (int i = 0; i < _cardButtons.Length; i++)
        {
            Navigation navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? _cardButtons[i - 1] : _confirmButton,
                selectOnDown = i + 1 < _cardButtons.Length
                    ? _cardButtons[i + 1]
                    : _confirmButton,
                selectOnRight = _confirmButton
            };
            _cardButtons[i].navigation = navigation;
        }
        UpdateConfirmNavigation();
    }

    private void UpdateConfirmNavigation()
    {
        if (_confirmButton == null || _cardButtons.Length == 0) return;
        int index = Mathf.Clamp(_lockedCommanderIndex, 0,
            _cardButtons.Length - 1);
        Navigation navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = _cardButtons[index],
            selectOnUp = _cardButtons[index],
            selectOnDown = _cardButtons[index]
        };
        _confirmButton.navigation = navigation;
    }

    private void SelectCommander(int commanderIndex)
    {
        if (_confirmed || commanderIndex < 0 ||
            commanderIndex >= _commanders.Length ||
            _commanders[commanderIndex] == null) return;
        bool changed = commanderIndex != _lockedCommanderIndex;
        _lockedCommanderIndex = commanderIndex;
        SelectedCommander = _commanders[commanderIndex];
        RefreshLockedCards();
        RenderCommander(commanderIndex);
        BeginCommanderThemeTransition(SelectedCommander.CommanderId);
        if (changed) PlaySelectionVoice(SelectedCommander);
        UpdateConfirmNavigation();
        if (EventSystem.current != null && _confirmButton != null)
            EventSystem.current.SetSelectedGameObject(_confirmButton.gameObject);
    }

    private void ConfirmSelection()
    {
        if (_confirmed || SelectedCommander == null) return;
        RenderCommander(_lockedCommanderIndex);
        RefreshLockedCards();
        CompleteCommanderThemeTransition();
        _confirmed = true;
    }

    private void RefreshLockedCards()
    {
        for (int i = 0; i < _cards.Length; i++)
            if (_cards[i] != null)
                _cards[i].SetLocked(i == _lockedCommanderIndex);
    }

    private void EnsureRosterCardVisible(int commanderIndex)
    {
        if (_rosterContent == null || _rosterViewport == null ||
            commanderIndex < 0 || commanderIndex >= _cards.Length) return;
        float viewportHeight = _rosterViewport.rect.height > 1f
            ? _rosterViewport.rect.height
            : RosterViewportHeight;
        float cardTop = commanderIndex *
                        (RosterCardHeight + RosterCardSpacing);
        float cardBottom = cardTop + RosterCardHeight;
        float scrollTop = Mathf.Max(0f, _rosterContent.anchoredPosition.y);
        if (cardTop < scrollTop)
            scrollTop = cardTop;
        else if (cardBottom > scrollTop + viewportHeight)
            scrollTop = cardBottom - viewportHeight;
        float maximumScroll = Mathf.Max(0f,
            _rosterContent.rect.height - viewportHeight);
        Vector2 position = _rosterContent.anchoredPosition;
        position.y = Mathf.Clamp(scrollTop, 0f, maximumScroll);
        _rosterContent.anchoredPosition = position;
        _rosterScroll?.StopMovement();
    }

    private void RenderCommander(int commanderIndex)
    {
        if (commanderIndex < 0 || commanderIndex >= _commanders.Length) return;
        RougeAutoplayCommanderDefinition commander =
            _commanders[commanderIndex];
        Sprite portrait = _commanderPortraits[commanderIndex];
        if (commander == null) return;
        if (_heroPortrait != null) _heroPortrait.sprite = portrait;
        if (_heroGlow != null) _heroGlow.sprite = portrait;
        if (_nameText != null) _nameText.text = commander.Name;
        if (_roleText != null) _roleText.text = commander.Role;
        if (_personaText != null) _personaText.text = commander.Persona;
        if (_sourceText != null)
            _sourceText.text =
                (string.Equals(commander.CommanderId,
                     RougeAutoplayCommanderJson.DefaultCommanderName,
                     StringComparison.Ordinal)
                    ? "BUILT-IN PACKAGE  /  "
                    : "RESOURCE MOD  /  ") + commander.CommanderId +
                "  /  " + commander.LocaleId;
        if (_traitsText != null)
            _traitsText.text = BuildTraitLine(
                commander.Locale.identity.personalityTraits);
        if (_backgroundText != null)
            _backgroundText.text = commander.Background;
        if (_thinkingText != null)
            _thinkingText.text = commander.Locale.personality.thinkingStyle;
        if (_talentText != null)
            _talentText.text = "权限  /  " + commander.TalentName + "\n" +
                               commander.TalentDescription;

        int affinity = Mathf.Clamp(PlayerPrefs.GetInt(
            commander.AffinityPreferenceKey,
            commander.Source.dialogue.startingAffinity), 0, 100);
        int familiarThreshold = commander.Source.dialogue.familiarThreshold;
        int closeThreshold = commander.Source.dialogue.closeThreshold;
        string affinityTier = affinity >= closeThreshold
            ? commander.CloseAffinityLabel
            : affinity >= familiarThreshold
                ? commander.FamiliarAffinityLabel
                : commander.DistantAffinityLabel;
        if (_affinityTierText != null)
            _affinityTierText.text = affinityTier;
        if (_affinityValueText != null)
            _affinityValueText.text = affinity + " / 100";
        if (_affinityProgressFill != null)
        {
            _affinityNormalized = affinity / 100f;
            _affinityProgressFill.anchorMax =
                new Vector2(_affinityNormalized, 1f);
            _affinityProgressFill.sizeDelta = Vector2.zero;
            ApplyAffinityThemeColor();
        }

        int[] values = RougeCommanderTacticalSpectrum.Calculate(commander);
        if (_radar != null) _radar.SetScores(values, 50f);
        if (_radarLabels != null)
            for (int i = 0; i < Mathf.Min(values.Length, _radarLabels.Length); i++)
                _radarLabels[i].text =
                    RougeCommanderTacticalSpectrum.AxisNames[i] + " " + values[i];
    }

    private void CaptureCommanderThemeBaseline()
    {
        Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null || IsRuntimeThemeGraphic(graphic) ||
                !_capturedThemeGraphics.Add(graphic))
                continue;
            Color lanColor = graphic.color;
            _themeGraphics.Add(new ThemeGraphicState
            {
                Graphic = graphic,
                LanColor = lanColor,
                TaotaoColor =
                    RougeCommanderSelectionPalette.TaotaoTarget(lanColor)
            });
        }

        Shadow[] shadows = GetComponentsInChildren<Shadow>(true);
        for (int i = 0; i < shadows.Length; i++)
        {
            Shadow shadow = shadows[i];
            if (shadow == null || !_capturedThemeShadows.Add(shadow)) continue;
            Color lanColor = shadow.effectColor;
            _themeShadows.Add(new ThemeShadowState
            {
                Shadow = shadow,
                LanColor = lanColor,
                TaotaoColor =
                    RougeCommanderSelectionPalette.TaotaoTarget(lanColor)
            });
        }

        Selectable[] selectables = GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < selectables.Length; i++)
        {
            Selectable selectable = selectables[i];
            if (selectable == null ||
                !_capturedThemeSelectables.Add(selectable))
                continue;
            ColorBlock lanColors = selectable.colors;
            _themeSelectables.Add(new ThemeSelectableState
            {
                Selectable = selectable,
                LanColors = lanColors,
                TaotaoColors = BuildTaotaoColorBlock(lanColors)
            });
        }

        _themeBaselineCaptured = true;
    }

    private bool IsRuntimeThemeGraphic(Graphic graphic)
    {
        if (graphic == _themeWash || graphic == _themeScanBand ||
            graphic == _radar || graphic == _affinityProgressImage ||
            graphic == _heroPortrait)
            return true;

        if (_loadingSegments != null)
            for (int i = 0; i < _loadingSegments.Length; i++)
                if (graphic == _loadingSegments[i]) return true;

        RougeCommanderSelectionCard card =
            graphic.GetComponentInParent<RougeCommanderSelectionCard>();
        return card != null && card.OwnsRuntimeColor(graphic);
    }

    private static ColorBlock BuildTaotaoColorBlock(ColorBlock lanColors)
    {
        ColorBlock result = lanColors;
        result.normalColor = RougeCommanderSelectionPalette.TaotaoTarget(
            lanColors.normalColor);
        result.highlightedColor = RougeCommanderSelectionPalette.TaotaoTarget(
            lanColors.highlightedColor);
        result.pressedColor = RougeCommanderSelectionPalette.TaotaoTarget(
            lanColors.pressedColor);
        result.selectedColor = RougeCommanderSelectionPalette.TaotaoTarget(
            lanColors.selectedColor);
        result.disabledColor = RougeCommanderSelectionPalette.TaotaoTarget(
            lanColors.disabledColor);
        return result;
    }

    private void ApplyCommanderThemeImmediate(string commanderId)
    {
        _themeTransitionActive = false;
        _themeTransitionElapsed = 0f;
        _themeTransitionTarget =
            RougeCommanderSelectionPalette.TargetBlend(commanderId);
        _themeTransitionFrom = _themeTransitionTarget;
        _themeSweepElapsed = 1f;
        ApplyCommanderThemeBlend(_themeTransitionTarget);
        ResetThemeScanBand();
    }

    private void BeginCommanderThemeTransition(string commanderId)
    {
        float target = RougeCommanderSelectionPalette.TargetBlend(commanderId);
        if (!_themeBaselineCaptured)
        {
            _themeBlend = target;
            return;
        }

        if (Mathf.Abs(target - _themeBlend) <= 0.0001f)
        {
            _themeTransitionActive = false;
            _themeTransitionTarget = target;
            ApplyCommanderThemeBlend(target);
            return;
        }

        // Starting from the current scalar makes a mid-transition reversal just
        // as smooth as a fresh selection, while every colour still derives from
        // the immutable Lan baseline captured above.
        _themeTransitionFrom = _themeBlend;
        _themeTransitionTarget = target;
        _themeTransitionElapsed = 0f;
        _themeSweepElapsed = 0f;
        _themeSweepDirection = target > _themeBlend ? 1f : -1f;
        ResetThemeScanBand();
        _themeTransitionActive = true;
    }

    private void UpdateCommanderTheme(float unscaledDeltaTime)
    {
        if (!_themeTransitionActive || !_themeBaselineCaptured) return;
        _themeTransitionElapsed += Mathf.Max(0f, unscaledDeltaTime);
        float progress = Mathf.Clamp01(
            _themeTransitionElapsed / ThemeTransitionDuration);
        float eased = progress * progress * (3f - 2f * progress);
        ApplyCommanderThemeBlend(Mathf.LerpUnclamped(
            _themeTransitionFrom, _themeTransitionTarget, eased));
        if (progress >= 1f) _themeTransitionActive = false;
    }

    private void CompleteCommanderThemeTransition()
    {
        string commanderId = SelectedCommander != null
            ? SelectedCommander.CommanderId
            : RougeAutoplayCommanderJson.DefaultCommanderName;
        _themeTransitionTarget =
            RougeCommanderSelectionPalette.TargetBlend(commanderId);
        _themeTransitionActive = false;
        _themeTransitionElapsed = ThemeTransitionDuration;
        ApplyCommanderThemeBlend(_themeTransitionTarget);
        _themeSweepElapsed = 1f;
        ResetThemeScanBand();
    }

    private void ApplyCommanderThemeBlend(float blend)
    {
        _themeBlend = Mathf.Clamp01(blend);
        if (!_themeBaselineCaptured) return;

        for (int i = 0; i < _themeGraphics.Count; i++)
        {
            ThemeGraphicState state = _themeGraphics[i];
            if (state.Graphic != null)
                state.Graphic.color = Color.LerpUnclamped(
                    state.LanColor, state.TaotaoColor, _themeBlend);
        }

        for (int i = 0; i < _themeShadows.Count; i++)
        {
            ThemeShadowState state = _themeShadows[i];
            if (state.Shadow != null)
                state.Shadow.effectColor = Color.LerpUnclamped(
                    state.LanColor, state.TaotaoColor, _themeBlend);
        }

        for (int i = 0; i < _themeSelectables.Count; i++)
        {
            ThemeSelectableState state = _themeSelectables[i];
            if (state.Selectable == null) continue;
            ColorBlock colors = state.LanColors;
            colors.normalColor = Color.LerpUnclamped(
                state.LanColors.normalColor,
                state.TaotaoColors.normalColor, _themeBlend);
            colors.highlightedColor = Color.LerpUnclamped(
                state.LanColors.highlightedColor,
                state.TaotaoColors.highlightedColor, _themeBlend);
            colors.pressedColor = Color.LerpUnclamped(
                state.LanColors.pressedColor,
                state.TaotaoColors.pressedColor, _themeBlend);
            colors.selectedColor = Color.LerpUnclamped(
                state.LanColors.selectedColor,
                state.TaotaoColors.selectedColor, _themeBlend);
            colors.disabledColor = Color.LerpUnclamped(
                state.LanColors.disabledColor,
                state.TaotaoColors.disabledColor, _themeBlend);
            state.Selectable.colors = colors;
        }

        for (int i = 0; i < _cards.Length; i++)
            if (_cards[i] != null) _cards[i].SetThemeBlend(_themeBlend);
        if (_radar != null) _radar.SetThemeBlend(_themeBlend);

        ApplyAffinityThemeColor();
        ApplyThemeBackdropEffects();
        ApplyLoadingSignalTheme();
    }

    private void ApplyAffinityThemeColor()
    {
        if (_affinityProgressImage == null) return;
        Color themedAccent = RougeCommanderSelectionPalette.BlendLanToTaotao(
            Cyan, _themeBlend);
        _affinityProgressImage.color = Color.LerpUnclamped(
            themedAccent, Gold, Mathf.Clamp01(_affinityNormalized));
    }

    private void ApplyThemeBackdropEffects()
    {
        if (_themeWash != null)
            _themeWash.SetCornerColors(
                RougeCommanderSelectionPalette.BlendLanToTaotao(
                    WashBottomLeft, _themeBlend),
                RougeCommanderSelectionPalette.BlendLanToTaotao(
                    WashTopLeft, _themeBlend),
                RougeCommanderSelectionPalette.BlendLanToTaotao(
                    WashTopRight, _themeBlend),
                RougeCommanderSelectionPalette.BlendLanToTaotao(
                    WashBottomRight, _themeBlend));
        if (_themeScanBand != null)
        {
            float currentAlpha = _themeScanBand.color.a;
            Color scanColor = RougeCommanderSelectionPalette.BlendLanToTaotao(
                ScanBandColor, _themeBlend);
            scanColor.a = currentAlpha;
            _themeScanBand.color = scanColor;
        }
    }

    private void ApplyLoadingSignalTheme()
    {
        if (_loadingSegments == null) return;
        Color themedSignal = RougeCommanderSelectionPalette.BlendLanToTaotao(
            Cyan, _themeBlend);
        for (int i = 0; i < _loadingSegments.Length; i++)
        {
            Image segment = _loadingSegments[i];
            if (segment == null) continue;
            Color current = segment.color;
            current.r = themedSignal.r;
            current.g = themedSignal.g;
            current.b = themedSignal.b;
            segment.color = current;
        }
    }

    private void UpdateThemeScanBand(float unscaledDeltaTime)
    {
        if (_themeScanBandRect == null) return;
        const float sweepDuration = 0.62f;
        _themeSweepElapsed += Mathf.Max(0f, unscaledDeltaTime);
        bool switching = _themeSweepElapsed < sweepDuration;
        if (!switching)
        {
            ResetThemeScanBand();
            return;
        }
        float progress = Mathf.Clamp01(_themeSweepElapsed / sweepDuration);
        if (switching && _themeSweepDirection < 0f) progress = 1f - progress;
        float eased = progress * progress * (3f - 2f * progress);
        _themeScanBandRect.anchoredPosition = Vector2.LerpUnclamped(
            new Vector2(-520f, -760f), new Vector2(520f, 760f), eased);
        if (_themeScanBand != null)
        {
            Color band = RougeCommanderSelectionPalette.BlendLanToTaotao(
                ScanBandColor, _themeBlend);
            float envelope = Mathf.Sin(
                Mathf.Clamp01(_themeSweepElapsed / sweepDuration) * Mathf.PI);
            band.a = 0.28f * envelope;
            _themeScanBand.color = band;
        }
    }

    private void ResetThemeScanBand()
    {
        Vector2 start = _themeSweepDirection < 0f
            ? new Vector2(520f, 760f)
            : new Vector2(-520f, -760f);
        if (_themeScanBandRect != null)
            _themeScanBandRect.anchoredPosition = start;
        if (_themeScanBand == null) return;
        Color band = RougeCommanderSelectionPalette.BlendLanToTaotao(
            ScanBandColor, _themeBlend);
        band.a = 0f;
        _themeScanBand.color = band;
    }

    private void PlaySelectionVoice(
        RougeAutoplayCommanderDefinition commander)
    {
        if (_selectionVoiceSource == null || commander == null ||
            string.IsNullOrWhiteSpace(commander.CommanderId)) return;

        string commanderId = commander.CommanderId;
        if (!_selectionVoiceClips.TryGetValue(commanderId,
                out AudioClip[] clips))
        {
            clips = Resources.LoadAll<AudioClip>(
                "Commanders/" + commanderId + "/Voices/Selection");
            if (clips == null) clips = Array.Empty<AudioClip>();
            Array.Sort(clips, (left, right) => string.CompareOrdinal(
                left != null ? left.name : string.Empty,
                right != null ? right.name : string.Empty));
            _selectionVoiceClips[commanderId] = clips;
        }

        if (clips.Length == 0) return;
        int clipIndex = UnityEngine.Random.Range(0, clips.Length);
        if (clips.Length > 1 &&
            _lastSelectionVoiceIndices.TryGetValue(commanderId,
                out int previousIndex) && clipIndex == previousIndex)
            clipIndex = (clipIndex + UnityEngine.Random.Range(1, clips.Length)) %
                        clips.Length;
        AudioClip clip = clips[clipIndex];
        if (clip == null) return;

        _lastSelectionVoiceIndices[commanderId] = clipIndex;
        _selectionVoiceSource.Stop();
        _selectionVoiceSource.clip = clip;
        _selectionVoiceSource.volume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(SfxVolumePreference, 1f));
        _selectionVoiceSource.Play();
    }

    private static string BuildTraitLine(string[] traits)
    {
        if (traits == null || traits.Length == 0) return "—";
        return "[ " + string.Join(" ]   [ ", traits) + " ]";
    }

    private static RougeCommanderSlantedGraphic CreatePanel(string name,
        Transform parent, Color color, float slant)
    {
        GameObject go = CreateRect(name, parent);
        RougeCommanderSlantedGraphic graphic =
            go.AddComponent<RougeCommanderSlantedGraphic>();
        graphic.color = color;
        graphic.Slant = slant;
        graphic.raycastTarget = false;
        return graphic;
    }

    private static RougeCommanderSlantedGraphic AddPanelBorder(Transform parent,
        float slant, Color color, float width)
    {
        GameObject go = CreateRect("Signal Border", parent);
        RougeCommanderSlantedGraphic graphic =
            go.AddComponent<RougeCommanderSlantedGraphic>();
        graphic.Slant = slant;
        graphic.BorderWidth = width;
        graphic.color = color;
        graphic.raycastTarget = false;
        Stretch(graphic.rectTransform);
        return graphic;
    }

    private static GameObject CreateRect(string name, Transform parent)
    {
        // Custom Graphic subclasses do not get Graphic's CanvasRenderer
        // requirement applied reliably when they are attached at runtime.
        // Seed every lightweight UI rect with one so slanted panels, borders,
        // and the tactical radar all have a valid render surface.
        GameObject go = new GameObject(name, typeof(RectTransform),
            typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(string name, Transform parent, int size,
        TextAnchor alignment, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = GetFont();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    private static Font GetFont()
    {
        if (s_font != null) return s_font;
        s_font = Font.CreateDynamicFontFromOSFont(new[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC",
            "Noto Sans CJK SC", "Arial"
        }, 22);
        if (s_font == null)
            s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return s_font;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetCenter(RectTransform rect, Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetTopLeft(RectTransform rect, Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetTopRight(RectTransform rect, Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetBottomLeft(RectTransform rect, Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void CopyRect(RectTransform target, RectTransform source)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.offsetMin = source.offsetMin;
        target.offsetMax = source.offsetMax;
    }

    private static float EaseOut(float value)
    {
        float inverse = 1f - value;
        return 1f - inverse * inverse * inverse;
    }
}

internal static class RougeCommanderSelectionPalette
{
    private static readonly RougeCommanderVisualTheme TaotaoTheme =
        RougeCommanderVisualThemes.Resolve("taotao");
    private static readonly Color PreservedGold =
        new Color(1f, 0.71f, 0.28f, 1f);

    public static float TargetBlend(string commanderId)
    {
        RougeCommanderVisualTheme theme =
            RougeCommanderVisualThemes.Resolve(commanderId);
        return theme != null && !theme.UsesDefaultPalette ? 1f : 0f;
    }

    public static Color TaotaoTarget(Color lanColor)
    {
        // Gold communicates commitment/affinity rather than adjutant identity.
        // Keep it stable even if a future remapper broadens its hue matching.
        if (Mathf.Abs(lanColor.r - PreservedGold.r) <= 0.025f &&
            Mathf.Abs(lanColor.g - PreservedGold.g) <= 0.025f &&
            Mathf.Abs(lanColor.b - PreservedGold.b) <= 0.025f)
            return lanColor;
        return TaotaoTheme.RemapInterfaceColor(lanColor);
    }

    public static Color BlendLanToTaotao(Color lanColor, float blend)
    {
        return Color.LerpUnclamped(lanColor, TaotaoTarget(lanColor),
            Mathf.Clamp01(blend));
    }
}

public sealed class RougeCommanderThemeWashGraphic : MaskableGraphic
{
    private Color _bottomLeft;
    private Color _topLeft;
    private Color _topRight;
    private Color _bottomRight;

    public void SetCornerColors(Color bottomLeft, Color topLeft,
        Color topRight, Color bottomRight)
    {
        if (_bottomLeft == bottomLeft && _topLeft == topLeft &&
            _topRight == topRight && _bottomRight == bottomRight)
            return;
        _bottomLeft = bottomLeft;
        _topLeft = topLeft;
        _topRight = topRight;
        _bottomRight = bottomRight;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        helper.AddVert(new Vector2(rect.xMin, rect.yMin), _bottomLeft,
            Vector2.zero);
        helper.AddVert(new Vector2(rect.xMin, rect.yMax), _topLeft,
            Vector2.up);
        helper.AddVert(new Vector2(rect.xMax, rect.yMax), _topRight,
            Vector2.one);
        helper.AddVert(new Vector2(rect.xMax, rect.yMin), _bottomRight,
            Vector2.right);
        helper.AddTriangle(0, 1, 2);
        helper.AddTriangle(0, 2, 3);
    }
}

public sealed class RougeCommanderThemeScanBandGraphic : MaskableGraphic
{
    private static readonly float[] Stops = { 0f, 0.2f, 0.5f, 0.8f, 1f };
    private static readonly float[] Alpha = { 0f, 0.22f, 1f, 0.22f, 0f };

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        for (int i = 0; i < Stops.Length; i++)
        {
            float y = Mathf.LerpUnclamped(rect.yMin, rect.yMax, Stops[i]);
            Color rowColor = color;
            rowColor.a *= Alpha[i];
            helper.AddVert(new Vector2(rect.xMin, y), rowColor,
                new Vector2(0f, Stops[i]));
            helper.AddVert(new Vector2(rect.xMax, y), rowColor,
                new Vector2(1f, Stops[i]));
        }

        for (int i = 0; i < Stops.Length - 1; i++)
        {
            int start = i * 2;
            helper.AddTriangle(start, start + 2, start + 3);
            helper.AddTriangle(start, start + 3, start + 1);
        }
    }
}

public sealed class RougeCommanderSelectionCard : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private RougeCommanderSlantedGraphic _fill;
    private RougeCommanderSlantedGraphic _border;
    private Image _portrait;
    private Vector2 _baseAnchoredPosition;
    private bool _pointerInside;
    private bool _selected;
    private bool _locked;
    private float _blend;
    private float _themeBlend;

    public void Initialize(RougeCommanderSlantedGraphic fill,
        RougeCommanderSlantedGraphic border, Image portrait,
        Vector2 baseAnchoredPosition)
    {
        _fill = fill;
        _border = border;
        _portrait = portrait;
        _baseAnchoredPosition = baseAnchoredPosition;
    }

    public void SetLocked(bool value)
    {
        _locked = value;
    }

    public void SetThemeBlend(float value)
    {
        _themeBlend = Mathf.Clamp01(value);
        ApplyVisualState();
    }

    public bool OwnsRuntimeColor(Graphic graphic)
    {
        return graphic == _fill || graphic == _border || graphic == _portrait;
    }

    private void Update()
    {
        float target = _pointerInside || _selected ? 1f : 0f;
        _blend = Mathf.MoveTowards(_blend, target,
            Time.unscaledDeltaTime / 0.14f);
        ApplyVisualState();
    }

    private void ApplyVisualState()
    {
        float eased = 1f - (1f - _blend) * (1f - _blend);
        transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.014f, eased);
        RectTransform rect = transform as RectTransform;
        if (rect != null)
            rect.anchoredPosition = _baseAnchoredPosition +
                                    new Vector2(Mathf.Lerp(0f, 4f, eased), 0f);
        if (_fill != null)
        {
            Color lanFill = Color.Lerp(
                new Color(0.018f, 0.09f, 0.14f, 0.98f),
                _locked
                    ? new Color(0.05f, 0.15f, 0.18f, 1f)
                    : new Color(0.025f, 0.15f, 0.21f, 1f),
                _locked ? 0.82f : eased);
            _fill.color = RougeCommanderSelectionPalette.BlendLanToTaotao(
                lanFill, _themeBlend);
        }
        if (_border != null)
        {
            Color lanBorder = _locked
                ? new Color(1f, 0.71f, 0.28f, 0.95f)
                : new Color(0.08f, 0.82f, 1f,
                    Mathf.Lerp(0.35f, 0.95f, eased));
            _border.color = RougeCommanderSelectionPalette.BlendLanToTaotao(
                lanBorder, _themeBlend);
        }
        if (_portrait != null)
            _portrait.color = Color.Lerp(Color.white,
                new Color(1.06f, 1.06f, 1.06f, 1f), eased);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerInside = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        _selected = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        _selected = false;
    }
}

public sealed class RougeCommanderSlantedGraphic : MaskableGraphic
{
    [SerializeField] private float slant = 24f;
    [SerializeField] private float borderWidth;

    public float Slant
    {
        get => slant;
        set
        {
            slant = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    public float BorderWidth
    {
        get => borderWidth;
        set
        {
            borderWidth = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        float safeSlant = Mathf.Min(slant, rect.width * 0.35f);
        Vector2[] outer =
        {
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.xMax - safeSlant, rect.yMin),
            new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMin + safeSlant, rect.yMax)
        };
        float width = Mathf.Min(borderWidth,
            Mathf.Min(rect.width, rect.height) * 0.2f);
        if (width <= 0.01f)
        {
            AddQuad(helper, outer[0], outer[1], outer[2], outer[3], color);
            return;
        }

        Vector2[] inner =
        {
            outer[0] + new Vector2(width, width),
            outer[1] + new Vector2(-width, width),
            outer[2] + new Vector2(-width, -width),
            outer[3] + new Vector2(width, -width)
        };
        AddQuad(helper, outer[0], outer[1], inner[1], inner[0], color);
        AddQuad(helper, outer[1], outer[2], inner[2], inner[1], color);
        AddQuad(helper, outer[2], outer[3], inner[3], inner[2], color);
        AddQuad(helper, outer[3], outer[0], inner[0], inner[3], color);
    }

    private static void AddQuad(VertexHelper helper, Vector2 a, Vector2 b,
        Vector2 c, Vector2 d, Color color)
    {
        int start = helper.currentVertCount;
        helper.AddVert(a, color, Vector2.zero);
        helper.AddVert(b, color, Vector2.right);
        helper.AddVert(c, color, Vector2.one);
        helper.AddVert(d, color, Vector2.up);
        helper.AddTriangle(start, start + 1, start + 2);
        helper.AddTriangle(start, start + 2, start + 3);
    }
}

public sealed class RougeCommanderRadarGraphic : MaskableGraphic
{
    private static readonly Color LanFill =
        new Color(0.08f, 0.82f, 1f, 0.28f);
    private static readonly Color LanGrid =
        new Color(0.08f, 0.66f, 0.84f, 0.18f);
    private static readonly Color LanOutline =
        new Color(0.18f, 0.9f, 1f, 0.95f);
    private readonly float[] _scores = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
    private float _themeBlend;

    public void SetThemeBlend(float value)
    {
        _themeBlend = Mathf.Clamp01(value);
        color = RougeCommanderSelectionPalette.BlendLanToTaotao(
            LanFill, _themeBlend);
        SetVerticesDirty();
    }

    public void SetScores(int[] scores, float maximum)
    {
        for (int i = 0; i < _scores.Length; i++)
            _scores[i] = scores != null && i < scores.Length
                ? Mathf.Clamp01(scores[i] / Mathf.Max(1f, maximum))
                : 0f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Max(4f,
            Mathf.Min(rect.width, rect.height) * 0.43f);
        Color grid = RougeCommanderSelectionPalette.BlendLanToTaotao(
            LanGrid, _themeBlend);
        for (int ring = 1; ring <= 4; ring++)
        {
            float ringRadius = radius * ring / 4f;
            for (int i = 0; i < 6; i++)
                AddLine(helper, Point(center, ringRadius, i),
                    Point(center, ringRadius, (i + 1) % 6), 1.1f, grid);
        }
        for (int i = 0; i < 6; i++)
            AddLine(helper, center, Point(center, radius, i), 1f, grid);

        Vector2[] points = new Vector2[6];
        for (int i = 0; i < 6; i++)
            points[i] = Point(center, radius * _scores[i], i);
        Color fill = color;
        for (int i = 0; i < 6; i++)
            AddTriangle(helper, center, points[i], points[(i + 1) % 6], fill);
        Color outline = RougeCommanderSelectionPalette.BlendLanToTaotao(
            LanOutline, _themeBlend);
        for (int i = 0; i < 6; i++)
            AddLine(helper, points[i], points[(i + 1) % 6], 2.2f, outline);
    }

    private static Vector2 Point(Vector2 center, float radius, int index)
    {
        float angle = (90f - index * 60f) * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private static void AddTriangle(VertexHelper helper, Vector2 a, Vector2 b,
        Vector2 c, Color color)
    {
        int start = helper.currentVertCount;
        helper.AddVert(a, color, Vector2.zero);
        helper.AddVert(b, color, Vector2.zero);
        helper.AddVert(c, color, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
    }

    private static void AddLine(VertexHelper helper, Vector2 a, Vector2 b,
        float width, Color color)
    {
        Vector2 direction = b - a;
        if (direction.sqrMagnitude < 0.0001f) return;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized *
                         width * 0.5f;
        int start = helper.currentVertCount;
        helper.AddVert(a - normal, color, Vector2.zero);
        helper.AddVert(a + normal, color, Vector2.zero);
        helper.AddVert(b + normal, color, Vector2.zero);
        helper.AddVert(b - normal, color, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
        helper.AddTriangle(start, start + 2, start + 3);
    }
}

public static class RougeCommanderTacticalSpectrum
{
    // Clockwise from the top. Each opposing pair is guaranteed to total 50:
    // (0,3), (1,4), (2,5).
    public static readonly string[] AxisNames =
        { "存钱", "控场", "攻坚", "铺塔", "纯伤", "清群" };

    public static int[] Calculate(RougeAutoplayCommanderDefinition commander)
    {
        if (commander == null) return new[] { 25, 25, 25, 25, 25, 25 };
        RougeAutoplayCommanderBiasConfig bias = commander.Source.personality.biases;
        RougeAutoplayCommanderConcernConfig concern =
            commander.Source.personality.concerns;

        float saving = SignedBias(bias.save);
        // This axis is saving versus adding another tower. Upgrade preference is
        // compared independently in the capital market and must not masquerade as
        // expansion here.
        float expansion = SignedBias(bias.build);
        float hunting = (SignedBias(bias.focusedTower) +
                         SignedConcern(concern.elite) +
                         SignedConcern(concern.boss)) / 3f;
        float utility = SignedBias(bias.controlTower);
        float directDamage = (SignedBias(bias.focusedTower) +
                              SignedBias(bias.areaTower)) * 0.5f;
        float crowd = (SignedBias(bias.areaTower) +
                       SignedConcern(concern.crowd)) * 0.5f;

        int[] result = new int[6];
        SplitPair(saving, expansion, out result[0], out result[3]);
        SplitPair(hunting, crowd, out result[2], out result[5]);
        SplitPair(utility, directDamage, out result[1], out result[4]);
        return result;
    }

    private static void SplitPair(float firstSignal, float secondSignal,
        out int first, out int second)
    {
        float delta = Mathf.Clamp(firstSignal - secondSignal, -1f, 1f);
        first = Mathf.Clamp(Mathf.RoundToInt(25f + delta * 25f), 0, 50);
        second = 50 - first;
    }

    private static float SignedBias(float value)
    {
        return Mathf.Clamp((value - 1f) / 0.25f, -1f, 1f);
    }

    private static float SignedConcern(float value)
    {
        float divisor = value >= 1f ? 0.35f : 0.25f;
        return Mathf.Clamp((value - 1f) / divisor, -1f, 1f);
    }
}
