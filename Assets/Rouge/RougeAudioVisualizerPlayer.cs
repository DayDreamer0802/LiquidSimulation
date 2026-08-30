using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class RougeAudioVisualizerPlayer : MonoBehaviour
{
    private const int DecorativeCanvasSortingOrder = 40;
    private const string MusicVolumePreference = "Rouge.Audio.MusicVolume";
    private const string SelectionMusicResourcePath =
        "Music/commander_selection_pynchon";
    private const float DefaultFadeOutDuration = 0.32f;
    private const float DefaultFadeInDuration = 0.48f;

    private struct BarVisual
    {
        public RectTransform Root;
        public Image Glow;
        public Image Core;
        public Image Highlight;
    }

    [Header("Playback")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _clip;
    [SerializeField] private bool _playOnEnable;

    [Header("Visualizer")]
    [SerializeField, Range(12, 64)] private int _barsPerSide = 30;
    [SerializeField, Range(0.08f, 0.25f)] private float _sideWidthRatio = 0.125f;
    [SerializeField, Range(64, 1024)] private int _spectrumSize = 256;
    [SerializeField] private FFTWindow _fftWindow = FFTWindow.BlackmanHarris;
    [SerializeField] private float _sensitivity = 220f;
    [SerializeField] private float _riseSpeed = 8f;
    [SerializeField] private float _fallSpeed = 3f;
    [SerializeField] private float _horizontalPadding = 18f;
    [SerializeField] private float _edgePadding = 2f;
    [SerializeField] private float _verticalPadding = 0f;
    [SerializeField] private float _barSpacing = 1f;
    [SerializeField] private float _minBarHeight = 6f;
    [SerializeField, Range(0.2f, 1f)] private float _floatingHeightRatio = 0.34f;
    [SerializeField, Range(0.1f, 1f)] private float _maxHeightRatio = 0.92f;
    [SerializeField] private float _activeAlpha = 0.82f;
    [SerializeField] private float _inactiveAlpha = 0f;
    [SerializeField] private float _fadeSpeed = 4f;
    [SerializeField] private Color _lowColor = new Color(0.36f, 0.73f, 0.98f, 0.42f);
    [SerializeField] private Color _highColor = new Color(0.78f, 0.92f, 1f, 0.72f);

    [Header("Style")]
    [SerializeField, Range(1f, 4f)] private float _glowWidthMultiplier = 1.2f;
    [SerializeField, Range(0.15f, 1f)] private float _coreWidthMultiplier = 0.22f;
    [SerializeField, Range(0.05f, 0.5f)] private float _highlightHeightRatio = 0.1f;
    [SerializeField] private float _highlightMaxHeight = 10f;
    [SerializeField] private bool _showEdgeAura;
    [SerializeField] private float _edgeAuraWidth = 8f;
    [SerializeField] private float _edgeAuraAlpha = 0.035f;

    [Header("Canvas")]
    [SerializeField] private bool _createOverlayCanvasIfMissing = true;
    [SerializeField] private bool _useDedicatedOverlayCanvas = true;
    [SerializeField] private string _overlayCanvasName = "RougeAudioVisualizerCanvas";
    [SerializeField, Range(10f, 60f)] private float _visualRefreshRate = 30f;

    private float[] _spectrum;
    private float[] _levels;
    private RectTransform _root;
    private RectTransform _leftContainer;
    private RectTransform _rightContainer;
    private BarVisual[] _leftBars;
    private BarVisual[] _rightBars;
    private CanvasGroup _canvasGroup;
    private Canvas _runtimeCanvas;
    private bool _ownsRuntimeCanvas;
    private Vector2 _cachedLeftSize;
    private Vector2 _cachedRightSize;
    private float _visualRefreshTimer;
    private float _authoredVolume = 1f;
    private float _userVolume = 1f;
    private float _transitionGain = 1f;
    private bool _authoredVolumeCaptured;
    private bool _visualizerRequested;
    private Coroutine _trackTransition;

    public AudioSource Source => _audioSource;
    public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;
    public bool IsVisualizerRequested => _visualizerRequested;

    /// <summary>
    /// Enters commander selection, switches to its loop, and explicitly enables
    /// the decorative spectrum. Safe to call while gameplay is paused.
    /// </summary>
    public static void EnterSelectionMusic(
        float fadeOutSeconds = DefaultFadeOutDuration,
        float fadeInSeconds = DefaultFadeInDuration)
    {
        RougeAudioVisualizerPlayer player = ResolvePlayer();
        if (player == null)
        {
            Debug.LogWarning("Commander selection music requested without a RougeAudioVisualizerPlayer.");
            return;
        }

        player.SetUserVolume(Mathf.Clamp01(
            PlayerPrefs.GetFloat(MusicVolumePreference, 1f)));
        player.SetVisualizerRequested(true);

        AudioClip selectionClip = Resources.Load<AudioClip>(SelectionMusicResourcePath);
        if (selectionClip == null)
        {
            Debug.LogWarning(
                $"Missing selection music at Resources/{SelectionMusicResourcePath}.",
                player);
            selectionClip = player._clip;
        }

        player.SwitchTrack(selectionClip, true, fadeOutSeconds, fadeInSeconds);
    }

    /// <summary>
    /// Leaves commander selection, immediately removes its spectrum, and returns
    /// to the serialized gameplay music without changing authored/user volume.
    /// </summary>
    public static void ExitSelectionMusic(
        float fadeOutSeconds = DefaultFadeOutDuration,
        float fadeInSeconds = DefaultFadeInDuration)
    {
        RougeAudioVisualizerPlayer player = ResolvePlayer();
        if (player == null)
        {
            return;
        }

        player.SetVisualizerRequested(false);
        player.SwitchTrack(player._clip, true, fadeOutSeconds, fadeInSeconds);
    }

    /// <summary>
    /// Controls only the decorative bars. It never starts, stops, or changes music.
    /// </summary>
    public static void SetSelectionVisualizerVisible(bool visible)
    {
        RougeAudioVisualizerPlayer player = ResolvePlayer();
        if (player != null)
        {
            player.SetVisualizerRequested(visible);
        }
    }

    private void Reset()
    {
        _audioSource = GetComponent<AudioSource>();
    }

    private void Awake()
    {
        EnsureAudioSource();
        CaptureAuthoredVolume();
        _userVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(MusicVolumePreference, 1f));
        _transitionGain = 1f;
        ApplyEffectiveVolume();
        EnsureSpectrumBuffer();
        EnsureLevelBuffer();
    }

    private void OnEnable()
    {
        EnsureAudioSource();
        EnsureSpectrumBuffer();
        EnsureLevelBuffer();

        if (_playOnEnable)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        _visualizerRequested = false;
        HideVisualizerImmediately();

        if (_trackTransition != null)
        {
            StopCoroutine(_trackTransition);
            _trackTransition = null;
        }

        SetTransitionGain(1f);
    }

    private void Update()
    {
        if (!_visualizerRequested)
        {
            HideVisualizerImmediately();
            return;
        }

        EnsureVisualizerUi();

        if (_levels == null || _levels.Length != _barsPerSide)
        {
            EnsureLevelBuffer();
        }

        float deltaTime = Time.unscaledDeltaTime;
        bool isPlaying = _audioSource != null && _audioSource.isPlaying && _audioSource.clip != null;

        if (isPlaying)
        {
            SampleSpectrum(deltaTime);
        }
        else
        {
            DecaySpectrum(deltaTime);
        }

        UpdateCanvasAlpha(isPlaying, deltaTime);

        if (!isPlaying && (_canvasGroup == null || _canvasGroup.alpha <= 0.001f))
        {
            return;
        }

        UpdateBarLayoutIfNeeded();
        _visualRefreshTimer += deltaTime;
        float refreshInterval = 1f / Mathf.Max(1f, _visualRefreshRate);
        if (_visualRefreshTimer < refreshInterval)
        {
            return;
        }

        _visualRefreshTimer = Mathf.Min(_visualRefreshTimer - refreshInterval, refreshInterval);
        UpdateBarHeights();
    }

    private void OnDestroy()
    {
        if (_trackTransition != null)
        {
            StopCoroutine(_trackTransition);
            _trackTransition = null;
        }

        if (_root != null)
        {
            Destroy(_root.gameObject);
        }

        if (_ownsRuntimeCanvas && _runtimeCanvas != null)
        {
            Destroy(_runtimeCanvas.gameObject);
        }
    }

    private void OnValidate()
    {
        _barsPerSide = Mathf.Clamp(_barsPerSide, 12, 64);
        _sideWidthRatio = Mathf.Clamp(_sideWidthRatio, 0.08f, 0.25f);
        _spectrumSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_spectrumSize, 64, 1024));
        _horizontalPadding = Mathf.Max(0f, _horizontalPadding);
        _edgePadding = Mathf.Max(0f, _edgePadding);
        _verticalPadding = Mathf.Max(0f, _verticalPadding);
        _barSpacing = Mathf.Max(0f, _barSpacing);
        _minBarHeight = Mathf.Max(1f, _minBarHeight);
        _floatingHeightRatio = Mathf.Clamp(_floatingHeightRatio, 0.2f, 1f);
        _maxHeightRatio = Mathf.Clamp01(_maxHeightRatio);
        _glowWidthMultiplier = Mathf.Clamp(_glowWidthMultiplier, 1f, 4f);
        _coreWidthMultiplier = Mathf.Clamp(_coreWidthMultiplier, 0.15f, 1f);
        _highlightHeightRatio = Mathf.Clamp(_highlightHeightRatio, 0.05f, 0.5f);
        _highlightMaxHeight = Mathf.Max(1f, _highlightMaxHeight);
        _edgeAuraWidth = Mathf.Max(0f, _edgeAuraWidth);
        _edgeAuraAlpha = Mathf.Clamp01(_edgeAuraAlpha);
        _riseSpeed = Mathf.Max(0.01f, _riseSpeed);
        _fallSpeed = Mathf.Max(0.01f, _fallSpeed);
        _fadeSpeed = Mathf.Max(0.01f, _fadeSpeed);
        _sensitivity = Mathf.Max(1f, _sensitivity);
    }

    public void Play()
    {
        EnsureAudioSource();
        if (_audioSource == null)
        {
            return;
        }

        if (_trackTransition != null)
        {
            StopCoroutine(_trackTransition);
            _trackTransition = null;
        }

        if (_clip != null)
        {
            _audioSource.clip = _clip;
        }

        if (_audioSource == null || _audioSource.clip == null)
        {
            return;
        }

        _audioSource.loop = true;
        _transitionGain = 1f;
        ApplyEffectiveVolume();
        _audioSource.Play();
    }

    public void Play(AudioClip clip)
    {
        EnsureAudioSource();
        if (_audioSource == null || clip == null)
        {
            return;
        }

        if (_trackTransition != null)
        {
            StopCoroutine(_trackTransition);
            _trackTransition = null;
        }

        ApplyTrackImmediately(clip, true);
    }

    public void Pause()
    {
        if (_audioSource == null)
        {
            return;
        }

        _audioSource.Pause();
    }

    public void Resume()
    {
        if (_audioSource == null || _audioSource.clip == null)
        {
            return;
        }

        _audioSource.UnPause();
    }

    public void Stop()
    {
        if (_audioSource == null)
        {
            return;
        }

        if (_trackTransition != null)
        {
            StopCoroutine(_trackTransition);
            _trackTransition = null;
        }

        _audioSource.Stop();
        SetTransitionGain(1f);
    }

    /// <summary>
    /// Switches music using unscaled time. The transition gain is independent of
    /// the authored and saved user volumes, so neither setting is overwritten.
    /// </summary>
    public void SwitchTrack(AudioClip clip, bool loop = true,
        float fadeOutSeconds = DefaultFadeOutDuration,
        float fadeInSeconds = DefaultFadeInDuration)
    {
        EnsureAudioSource();
        if (_audioSource == null || clip == null)
        {
            return;
        }

        if (_trackTransition != null)
        {
            StopCoroutine(_trackTransition);
            _trackTransition = null;
        }

        fadeOutSeconds = Mathf.Max(0f, fadeOutSeconds);
        fadeInSeconds = Mathf.Max(0f, fadeInSeconds);
        if (!isActiveAndEnabled ||
            (fadeOutSeconds <= 0f && fadeInSeconds <= 0f))
        {
            ApplyTrackImmediately(clip, loop);
            return;
        }

        _trackTransition = StartCoroutine(
            SwitchTrackRoutine(clip, loop, fadeOutSeconds, fadeInSeconds));
    }

    public void SetVisualizerRequested(bool requested)
    {
        if (_visualizerRequested == requested)
        {
            if (!requested)
            {
                HideVisualizerImmediately();
            }
            return;
        }

        _visualizerRequested = requested;
        if (requested)
        {
            EnsureVisualizerUi();
            if (_root != null)
            {
                _root.gameObject.SetActive(true);
            }
        }
        else
        {
            HideVisualizerImmediately();
        }
    }

    public void SetVolume(float volume)
    {
        EnsureAudioSource();
        if (_audioSource == null) return;
        _authoredVolume = Mathf.Clamp01(volume);
        _authoredVolumeCaptured = true;
        ApplyEffectiveVolume();
    }

    public void SetUserVolume(float volume)
    {
        EnsureAudioSource();
        if (_audioSource == null) return;
        CaptureAuthoredVolume();
        _userVolume = Mathf.Clamp01(volume);
        ApplyEffectiveVolume();
    }

    private static RougeAudioVisualizerPlayer ResolvePlayer()
    {
        RougeAudioVisualizerPlayer[] players =
            FindObjectsByType<RougeAudioVisualizerPlayer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].isActiveAndEnabled)
            {
                return players[i];
            }
        }

        return null;
    }

    private IEnumerator SwitchTrackRoutine(AudioClip clip, bool loop,
        float fadeOutSeconds, float fadeInSeconds)
    {
        bool sameTrack = _audioSource.clip == clip;
        if (!sameTrack && _audioSource.isPlaying && fadeOutSeconds > 0f)
        {
            yield return FadeTransitionGain(_transitionGain, 0f, fadeOutSeconds);
        }
        else if (!sameTrack)
        {
            SetTransitionGain(0f);
        }

        if (!sameTrack)
        {
            _audioSource.Stop();
            _audioSource.clip = clip;
        }

        _audioSource.loop = loop;
        if (!_audioSource.isPlaying)
        {
            _audioSource.Play();
        }

        if (fadeInSeconds > 0f)
        {
            yield return FadeTransitionGain(_transitionGain, 1f, fadeInSeconds);
        }
        else
        {
            SetTransitionGain(1f);
        }

        _trackTransition = null;
    }

    private IEnumerator FadeTransitionGain(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetTransitionGain(to);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetTransitionGain(Mathf.Lerp(from, to,
                Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }

        SetTransitionGain(to);
    }

    private void ApplyTrackImmediately(AudioClip clip, bool loop)
    {
        _audioSource.Stop();
        _audioSource.clip = clip;
        _audioSource.loop = loop;
        SetTransitionGain(1f);
        _audioSource.Play();
    }

    private void SetTransitionGain(float gain)
    {
        _transitionGain = Mathf.Clamp01(gain);
        ApplyEffectiveVolume();
    }

    private void ApplyEffectiveVolume()
    {
        if (_audioSource == null)
        {
            return;
        }

        _audioSource.volume = _authoredVolume * _userVolume * _transitionGain;
    }

    private void EnsureAudioSource()
    {
        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>();
        }
    }

    private void CaptureAuthoredVolume()
    {
        if (_authoredVolumeCaptured || _audioSource == null) return;
        _authoredVolume = Mathf.Clamp01(_audioSource.volume);
        _authoredVolumeCaptured = true;
    }

    private void EnsureSpectrumBuffer()
    {
        if (_spectrum == null || _spectrum.Length != _spectrumSize)
        {
            _spectrum = new float[_spectrumSize];
        }
    }

    private void EnsureLevelBuffer()
    {
        if (_levels == null || _levels.Length != _barsPerSide)
        {
            _levels = new float[_barsPerSide];
        }
    }

    private void HideVisualizerImmediately()
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
        }

        if (_root != null && _root.gameObject.activeSelf)
        {
            _root.gameObject.SetActive(false);
        }

        if (_levels == null)
        {
            return;
        }

        for (int i = 0; i < _levels.Length; i++)
        {
            _levels[i] = 0f;
        }
    }

    private void EnsureVisualizerUi()
    {
        if (_root != null && _leftBars != null && _rightBars != null && _leftBars.Length == _barsPerSide && _rightBars.Length == _barsPerSide)
        {
            Canvas parentCanvas = _root.GetComponentInParent<Canvas>();
            if (_useDedicatedOverlayCanvas && parentCanvas != null)
                parentCanvas.sortingOrder = DecorativeCanvasSortingOrder;
            return;
        }

        Canvas targetCanvas = ResolveTargetCanvas();
        if (targetCanvas == null)
        {
            return;
        }

        if (_root != null)
        {
            Destroy(_root.gameObject);
        }

        GameObject rootObject = new GameObject("AudioVisualizerRoot", typeof(RectTransform), typeof(CanvasGroup));
        _root = rootObject.GetComponent<RectTransform>();
        _canvasGroup = rootObject.GetComponent<CanvasGroup>();
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.alpha = _inactiveAlpha;
        _root.SetParent(targetCanvas.transform, false);
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;

        _leftContainer = CreateContainer("LeftVisualizer", _root, 0f, _sideWidthRatio, false);
        _rightContainer = CreateContainer("RightVisualizer", _root, 1f - _sideWidthRatio, 1f, true);

        _leftBars = CreateBars("LeftBar", _leftContainer);
        _rightBars = CreateBars("RightBar", _rightContainer);

        _cachedLeftSize = Vector2.zero;
        _cachedRightSize = Vector2.zero;
    }

    private Canvas ResolveTargetCanvas()
    {
        if (_root != null)
        {
            Canvas parentCanvas = _root.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                return parentCanvas;
            }
        }

        if (_useDedicatedOverlayCanvas)
        {
            return ResolveDedicatedOverlayCanvas();
        }

        Canvas preferredCanvas = null;
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode == RenderMode.WorldSpace)
            {
                continue;
            }

            if (canvas.name == "RougeCanvas")
            {
                return canvas;
            }

            if (preferredCanvas == null)
            {
                preferredCanvas = canvas;
            }
        }

        if (preferredCanvas != null)
        {
            return preferredCanvas;
        }

        if (!_createOverlayCanvasIfMissing)
        {
            return null;
        }

        if (_runtimeCanvas == null)
        {
            GameObject canvasObject = new GameObject(_overlayCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _runtimeCanvas = canvasObject.GetComponent<Canvas>();
            _runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Keep the decorative spectrum behind the gameplay HUD (sorting 50).
            _runtimeCanvas.sortingOrder = DecorativeCanvasSortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _ownsRuntimeCanvas = true;
        }

        return _runtimeCanvas;
    }

    private Canvas ResolveDedicatedOverlayCanvas()
    {
        if (_runtimeCanvas != null)
        {
            _runtimeCanvas.sortingOrder = DecorativeCanvasSortingOrder;
            return _runtimeCanvas;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode == RenderMode.WorldSpace)
            {
                continue;
            }

            if (canvas.name != _overlayCanvasName)
            {
                continue;
            }

            _runtimeCanvas = canvas;
            _ownsRuntimeCanvas = false;
            _runtimeCanvas.sortingOrder = DecorativeCanvasSortingOrder;
            return _runtimeCanvas;
        }

        if (!_createOverlayCanvasIfMissing)
        {
            return null;
        }

        GameObject canvasObject = new GameObject(_overlayCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _runtimeCanvas = canvasObject.GetComponent<Canvas>();
        _runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Keep the decorative spectrum behind the gameplay HUD (sorting 50).
        _runtimeCanvas.sortingOrder = DecorativeCanvasSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        _ownsRuntimeCanvas = true;
        return _runtimeCanvas;
    }

    private RectTransform CreateContainer(string objectName, Transform parent, float anchorMinX, float anchorMaxX, bool alignRight)
    {
        GameObject containerObject = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = containerObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        float anchorMinY = 0f;
        float anchorMaxY = _floatingHeightRatio;
        rect.anchorMin = new Vector2(anchorMinX, anchorMinY);
        rect.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        CreateEdgeAura(rect, alignRight);
        return rect;
    }

    private void CreateEdgeAura(RectTransform parent, bool alignRight)
    {
        if (!_showEdgeAura || _edgeAuraWidth <= 0f || _edgeAuraAlpha <= 0f)
        {
            return;
        }

        GameObject auraObject = new GameObject("Aura", typeof(RectTransform), typeof(Image));
        RectTransform rect = auraObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(alignRight ? 1f : 0f, 0f);
        rect.anchorMax = new Vector2(alignRight ? 1f : 0f, 1f);
        rect.pivot = new Vector2(alignRight ? 1f : 0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(_edgeAuraWidth, 0f);

        Image aura = auraObject.GetComponent<Image>();
        aura.raycastTarget = false;
        aura.color = new Color(_lowColor.r, _lowColor.g, _lowColor.b, _edgeAuraAlpha);
    }

    private BarVisual[] CreateBars(string prefix, Transform parent)
    {
        BarVisual[] bars = new BarVisual[_barsPerSide];
        for (int i = 0; i < bars.Length; i++)
        {
            bars[i] = CreateBar(prefix + i, parent);
        }

        return bars;
    }

    private BarVisual CreateBar(string objectName, Transform parent)
    {
        GameObject glowObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        RectTransform glowRect = glowObject.GetComponent<RectTransform>();
        glowRect.SetParent(parent, false);
        glowRect.anchorMin = Vector2.zero;
        glowRect.anchorMax = Vector2.zero;
        glowRect.pivot = new Vector2(0.5f, 0f);

        Image glow = glowObject.GetComponent<Image>();
        glow.raycastTarget = false;

        GameObject coreObject = new GameObject("Core", typeof(RectTransform), typeof(Image));
        RectTransform coreRect = coreObject.GetComponent<RectTransform>();
        coreRect.SetParent(glowRect, false);
        coreRect.anchorMin = new Vector2(0.5f, 0f);
        coreRect.anchorMax = new Vector2(0.5f, 1f);
        coreRect.pivot = new Vector2(0.5f, 0f);
        coreRect.anchoredPosition = Vector2.zero;

        Image core = coreObject.GetComponent<Image>();
        core.raycastTarget = false;

        GameObject highlightObject = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
        RectTransform highlightRect = highlightObject.GetComponent<RectTransform>();
        highlightRect.SetParent(glowRect, false);
        highlightRect.anchorMin = new Vector2(0.5f, 1f);
        highlightRect.anchorMax = new Vector2(0.5f, 1f);
        highlightRect.pivot = new Vector2(0.5f, 1f);
        highlightRect.anchoredPosition = Vector2.zero;

        Image highlight = highlightObject.GetComponent<Image>();
        highlight.raycastTarget = false;

        return new BarVisual
        {
            Root = glowRect,
            Glow = glow,
            Core = core,
            Highlight = highlight
        };
    }

    private void SampleSpectrum(float deltaTime)
    {
        EnsureSpectrumBuffer();
        _audioSource.GetSpectrumData(_spectrum, 0, _fftWindow);

        int sampleCount = Mathf.Max(1, _spectrum.Length / 2);
        for (int i = 0; i < _levels.Length; i++)
        {
            float energy = ReadBandEnergy(i, sampleCount);
            float bandBoost = 1f + i * 0.07f;
            float target = Mathf.Clamp01(Mathf.Sqrt(energy * _sensitivity * bandBoost));
            float speed = target >= _levels[i] ? _riseSpeed : _fallSpeed;
            _levels[i] = Mathf.MoveTowards(_levels[i], target, speed * deltaTime);
        }
    }

    private float ReadBandEnergy(int bandIndex, int sampleCount)
    {
        float startT = Mathf.Pow(bandIndex / (float)_levels.Length, 1.65f);
        float endT = Mathf.Pow((bandIndex + 1f) / _levels.Length, 1.65f);
        int startIndex = Mathf.Clamp(Mathf.FloorToInt(startT * sampleCount), 0, sampleCount - 1);
        int endIndex = Mathf.Clamp(Mathf.CeilToInt(endT * sampleCount), startIndex + 1, sampleCount);

        float sum = 0f;
        float weightTotal = 0f;
        for (int i = startIndex; i < endIndex; i++)
        {
            float weight = 1f + i * 0.02f;
            sum += _spectrum[i] * weight;
            weightTotal += weight;
        }

        if (weightTotal <= 0f)
        {
            return 0f;
        }

        return sum / weightTotal;
    }

    private void DecaySpectrum(float deltaTime)
    {
        if (_levels == null)
        {
            return;
        }

        for (int i = 0; i < _levels.Length; i++)
        {
            _levels[i] = Mathf.MoveTowards(_levels[i], 0f, _fallSpeed * deltaTime);
        }
    }

    private void UpdateCanvasAlpha(bool isPlaying, float deltaTime)
    {
        if (_canvasGroup == null)
        {
            return;
        }

        float targetAlpha = isPlaying ? _activeAlpha : _inactiveAlpha;
        _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, targetAlpha, _fadeSpeed * deltaTime);
    }

    private void UpdateBarLayoutIfNeeded()
    {
        if (_leftContainer == null || _rightContainer == null || _leftBars == null || _rightBars == null)
        {
            return;
        }

        Vector2 leftSize = _leftContainer.rect.size;
        Vector2 rightSize = _rightContainer.rect.size;
        if (leftSize == _cachedLeftSize && rightSize == _cachedRightSize)
        {
            return;
        }

        _cachedLeftSize = leftSize;
        _cachedRightSize = rightSize;
        LayoutBars(_leftContainer, _leftBars, false);
        LayoutBars(_rightContainer, _rightBars, true);
    }

    private void LayoutBars(RectTransform container, BarVisual[] bars, bool alignFromRight)
    {
        if (container == null || bars == null || bars.Length == 0)
        {
            return;
        }

        float totalSpacing = _barSpacing * (bars.Length - 1);
        float availableWidth = Mathf.Max(8f, container.rect.width - _edgePadding - _horizontalPadding - totalSpacing);
        float laneWidth = availableWidth / bars.Length;
        float glowWidth = Mathf.Max(1f, laneWidth * _glowWidthMultiplier);
        float coreWidth = Mathf.Max(1f, laneWidth * _coreWidthMultiplier);
        float highlightWidth = Mathf.Max(1f, coreWidth);

        for (int i = 0; i < bars.Length; i++)
        {
            RectTransform rect = bars[i].Root;
            float x = _edgePadding + laneWidth * 0.5f + i * (laneWidth + _barSpacing);
            if (alignFromRight)
            {
                x = container.rect.width - _edgePadding - laneWidth * 0.5f - i * (laneWidth + _barSpacing);
            }

            rect.anchoredPosition = new Vector2(x, _verticalPadding);
            rect.sizeDelta = new Vector2(glowWidth, rect.sizeDelta.y);
            bars[i].Core.rectTransform.sizeDelta = new Vector2(coreWidth, 0f);
            bars[i].Highlight.rectTransform.sizeDelta = new Vector2(highlightWidth, bars[i].Highlight.rectTransform.sizeDelta.y);
        }
    }

    private void UpdateBarHeights()
    {
        if (_leftContainer == null || _rightContainer == null || _leftBars == null || _rightBars == null || _levels == null)
        {
            return;
        }

        float leftMaxHeight = Mathf.Max(_minBarHeight, (_leftContainer.rect.height - _verticalPadding) * _maxHeightRatio);
        float rightMaxHeight = Mathf.Max(_minBarHeight, (_rightContainer.rect.height - _verticalPadding) * _maxHeightRatio);

        for (int i = 0; i < _levels.Length; i++)
        {
            float level = _levels[i];
            float gradientT = _levels.Length <= 1 ? 0f : i / (float)(_levels.Length - 1);
            Color baseColor = Color.Lerp(_lowColor, _highColor, gradientT);
            Color coreColor = Color.Lerp(baseColor, Color.white, level * 0.08f);
            Color glowColor = Color.Lerp(baseColor, _highColor, level * 0.12f);
            Color highlightColor = Color.Lerp(baseColor, Color.white, 0.2f + level * 0.1f);

            glowColor.a = Mathf.Lerp(0.05f, 0.2f, level);
            coreColor.a = Mathf.Lerp(0.5f, 0.88f, level);
            highlightColor.a = Mathf.Lerp(0.08f, 0.22f, level);

            UpdateBarVisual(_leftBars[i], level, leftMaxHeight, glowColor, coreColor, highlightColor);
            UpdateBarVisual(_rightBars[i], level, rightMaxHeight, glowColor, coreColor, highlightColor);
        }
    }

    private void UpdateBarVisual(BarVisual bar, float level, float maxHeight, Color glowColor, Color coreColor, Color highlightColor)
    {
        if (bar.Root == null)
        {
            return;
        }

        float barHeight = Mathf.Lerp(_minBarHeight, maxHeight, level);
        float highlightHeight = Mathf.Min(_highlightMaxHeight, Mathf.Max(_minBarHeight * 0.4f, barHeight * _highlightHeightRatio));

        bar.Root.sizeDelta = new Vector2(bar.Root.sizeDelta.x, barHeight);
        bar.Glow.color = glowColor;
        bar.Core.color = coreColor;
        bar.Highlight.color = highlightColor;
        bar.Highlight.rectTransform.sizeDelta = new Vector2(bar.Highlight.rectTransform.sizeDelta.x, highlightHeight);
    }
}
