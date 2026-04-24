using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class RougeAudioVisualizerPlayer : MonoBehaviour
{
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
    [SerializeField] private float _edgeAuraWidth = 8f;
    [SerializeField] private float _edgeAuraAlpha = 0.035f;

    [Header("Canvas")]
    [SerializeField] private bool _createOverlayCanvasIfMissing = true;
    [SerializeField] private string _overlayCanvasName = "RougeAudioVisualizerCanvas";

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

    public AudioSource Source => _audioSource;
    public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;

    private void Reset()
    {
        _audioSource = GetComponent<AudioSource>();
    }

    private void Awake()
    {
        EnsureAudioSource();
        EnsureSpectrumBuffer();
        EnsureLevelBuffer();
    }

    private void Start()
    {
        EnsureVisualizerUi();
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

    private void Update()
    {
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
        UpdateBarLayoutIfNeeded();
        UpdateBarHeights();
    }

    private void OnDestroy()
    {
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

        if (_clip != null)
        {
            _audioSource.clip = _clip;
        }

        if (_audioSource == null || _audioSource.clip == null)
        {
            return;
        }

        EnsureVisualizerUi();
        _audioSource.Play();
    }

    public void Play(AudioClip clip)
    {
        _clip = clip;
        Play();
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

        _audioSource.Stop();
    }

    public void SetVolume(float volume)
    {
        if (_audioSource == null)
        {
            return;
        }

        _audioSource.volume = Mathf.Clamp01(volume);
    }

    private void EnsureAudioSource()
    {
        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>();
        }
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

    private void EnsureVisualizerUi()
    {
        if (_root != null && _leftBars != null && _rightBars != null && _leftBars.Length == _barsPerSide && _rightBars.Length == _barsPerSide)
        {
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
            _runtimeCanvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _ownsRuntimeCanvas = true;
        }

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
        if (_edgeAuraWidth <= 0f || _edgeAuraAlpha <= 0f)
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