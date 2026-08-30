using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private const int TowerDefenseAutoplayVisibleThoughtLines = 2;
    private const int TowerDefenseAutoplayHeartbeatSegmentCount = 72;
    private const float TowerDefenseAutoplayHeartbeatWidth = 330f;
    private const float TowerDefenseAutoplayHeartbeatHeight = 24f;
    private const float TowerDefenseAutoplayHeartbeatWindowSeconds = 1.8f;
    private const float TowerDefenseAutoplayHudFadeInSeconds = 0.28f;
    private const float TowerDefenseAutoplayReleaseHoldSeconds = 0.50f;
    private const float TowerDefenseAutoplayHudFadeOutSeconds = 1.00f;

    private enum TowerDefenseAutoplayHudPhase : byte
    {
        Hidden,
        FadingIn,
        Visible,
        HoldingRelease,
        FadingOut
    }

    private Canvas _towerDefenseAutoplayCanvas;
    private CanvasGroup _towerDefenseAutoplayCanvasGroup;
    private CanvasGroup _towerDefenseAutoplayInterfaceGroup;
    private Coroutine _towerDefenseAutoplayHudTransitionRoutine;
    private TowerDefenseAutoplayHudPhase _towerDefenseAutoplayHudPhase;
    private GameObject _towerDefenseAutoplayHudRoot;
    private Image _towerDefenseAutoplayPortrait;
    private Image _towerDefenseAutoplayPortraitPulse;
    private Button _towerDefenseAutoplayPortraitButton;
    private Text _towerDefenseAutoplayRoleText;
    private Text _towerDefenseAutoplayStatusText;
    private Text _towerDefenseAutoplayThoughtTitle;
    private Text _towerDefenseAutoplayEntranceText;
    private Text _towerDefenseAutoplayThoughtText;
    private Text _towerDefenseAutoplayHintText;
    private Text _towerDefenseAutoplayMainHealthText;
    private Text _towerDefenseAutoplayBossHealthText;
    private Image _towerDefenseAutoplayMainHealthFill;
    private Image _towerDefenseAutoplayBossHealthFill;
    private Texture2D _towerDefenseAutoplayBackdropGradientTexture;
    private Sprite _towerDefenseAutoplayBackdropGradientSprite;
    private RectTransform _towerDefenseAutoplayHeartbeatRoot;
    private Image[] _towerDefenseAutoplayHeartbeatSegments;
    private float[] _towerDefenseAutoplayHeartbeatTrace;
    private float _towerDefenseAutoplayDisplayedTension = 0.08f;
    private float _towerDefenseAutoplayHeartbeatPhase;
    private float _towerDefenseAutoplayHeartbeatSampleAccumulator;
    private ScrollRect _towerDefenseAutoplayThoughtScroll;
    private int _towerDefenseAutoplayRenderedEntranceRevision = -1;
    private int _towerDefenseAutoplayRenderedThoughtRevision = -1;
    private bool _towerDefenseAutoplayIdentityRendered;
    private int _towerDefenseAutoplayPortraitRapidClickCount;
    private float _towerDefenseAutoplayPortraitRapidClickWindowStarted =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayLastPortraitClickDialogueTime =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayLastPortraitRapidDialogueTime =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayPortraitPulseStarted =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayPortraitPulseDuration;
    private bool _towerDefenseAutoplayPortraitPulseIsRapid;
    private Color _towerDefenseAutoplayPortraitPulseColor = Color.clear;
    private RougeAutoplayCommanderPortraitEmotion
        _towerDefenseAutoplayRenderedPortraitEmotion =
            (RougeAutoplayCommanderPortraitEmotion)(-1);
    private RougeAutoplayCommanderPortraitVariant
        _towerDefenseAutoplayRenderedPortraitVariant =
            (RougeAutoplayCommanderPortraitVariant)(-1);

    partial void RefreshTowerDefenseAutoplayPresentation()
    {
        if (_towerDefenseAutoplayCanvas == null) return;

        bool gameplayVisible = _towerDefenseAutoplayEnabled &&
                               !_towerDefenseAutoplayCleanView &&
                               !_towerDefenseStartupActive &&
                               !_towerDefenseGameOver &&
                               !IsPlayerSettingsOpen;
        bool releaseVisible = IsTowerDefenseAutoplayReleasePresentationActive;
        if (gameplayVisible)
            EnsureTowerDefenseAutoplayHudVisible();
        else if (!releaseVisible)
            HideTowerDefenseAutoplayHudImmediately(!_towerDefenseAutoplayEnabled);

        if ((!gameplayVisible && !releaseVisible) ||
            !_towerDefenseAutoplayCanvas.gameObject.activeSelf) return;

        if (!_towerDefenseAutoplayIdentityRendered)
        {
            if (_towerDefenseAutoplayRoleText != null)
            {
                int discountPercent = Mathf.RoundToInt(
                    (1f - TowerDefenseAutoplayTalentCostMultiplier) * 100f);
                string talentLabel = discountPercent > 0
                    ? $"{TowerDefenseAutoplayTalentName} -{discountPercent}%"
                    : TowerDefenseAutoplayTalentName;
                string affinityColor = CommanderInterfaceColorHex(
                    new Color32(0xDD, 0xF8, 0xFF, 0xFF));
                _towerDefenseAutoplayRoleText.text =
                    $"<b>{TowerDefenseAutoplayCharacterName}</b>" +
                    $"  <size=18><color=#{affinityColor}>" +
                    $"默契{CurrentAutoplayAffinityLabel} · " +
                    $"{talentLabel}</color></size>";
            }
            if (_towerDefenseAutoplayThoughtTitle != null)
                _towerDefenseAutoplayThoughtTitle.text =
                    "刚才";
            _towerDefenseAutoplayIdentityRendered = true;
        }

        if (_towerDefenseAutoplayStatusText != null)
            _towerDefenseAutoplayStatusText.text =
                $"<b>时间 {FormatGameTime(_survivalTime)}</b>   " +
                $"金币 {_towerDefenseGold}   敌人 {_towerDefenseAliveEstimate}   " +
                $"策略 {CurrentAutoplayStrategyLabel} · 并行推演";
        if (_towerDefenseAutoplayHintText != null)
            _towerDefenseAutoplayHintText.text = releaseVisible
                ? "AI 接管已解除   //   指挥权交还中"
                : $"[立绘] 互动   [F2] 隐藏   [F6] 结束   " +
                  $"[F10] ×{(_towerDefenseDoubleSpeed ? 2 : 1)}";

        RefreshTowerDefenseAutoplayHealthLines();
        UpdateTowerDefenseAutoplayHeartbeat();
        RefreshTowerDefenseAutoplayPortraitSprite();
        UpdateTowerDefenseAutoplayPortraitInteractionFeedback();

        bool speechVisible = releaseVisible ||
                             (_towerDefenseAutoplayEntrancePending &&
                              _survivalTime <=
                              _towerDefenseAutoplaySpeechVisibleUntil);
        if (_towerDefenseAutoplayEntrancePending && !speechVisible &&
            !releaseVisible)
            _towerDefenseAutoplayEntrancePending = false;
        if (_towerDefenseAutoplayEntranceText != null &&
            _towerDefenseAutoplayEntranceText.gameObject.activeSelf != speechVisible)
            _towerDefenseAutoplayEntranceText.gameObject.SetActive(speechVisible);

        if (_towerDefenseAutoplayRenderedThoughtRevision !=
            _towerDefenseAutoplayThoughtRevision)
            RefreshTowerDefenseAutoplayThoughtText();

        if (_towerDefenseAutoplayRenderedEntranceRevision !=
            _towerDefenseAutoplayEntranceRevision)
        {
            _towerDefenseAutoplayRenderedEntranceRevision =
                _towerDefenseAutoplayEntranceRevision;
            if (_towerDefenseAutoplayEntranceText != null)
                _towerDefenseAutoplayEntranceText.text =
                    string.IsNullOrWhiteSpace(_towerDefenseAutoplayEntranceLine)
                        ? string.Empty
                        : $"“{_towerDefenseAutoplayEntranceLine}”";
        }
    }

    private bool IsTowerDefenseAutoplayReleasePresentationActive =>
        _towerDefenseAutoplayHudPhase ==
            TowerDefenseAutoplayHudPhase.HoldingRelease ||
        _towerDefenseAutoplayHudPhase ==
            TowerDefenseAutoplayHudPhase.FadingOut;

    private void EnsureTowerDefenseAutoplayHudVisible()
    {
        if (_towerDefenseAutoplayCanvas == null) return;
        // The regular gameplay HUD stays fully hidden while command is handed to
        // the companion. Reapplying this before the phase early-out also clears
        // any partial alpha left when F6 interrupts the release crossfade.
        PrepareGameplayHudForAutoplayReleaseCrossfade();
        if (_towerDefenseAutoplayHudPhase ==
                TowerDefenseAutoplayHudPhase.Visible ||
            _towerDefenseAutoplayHudPhase ==
                TowerDefenseAutoplayHudPhase.FadingIn) return;

        StopTowerDefenseAutoplayHudTransition();
        GameObject canvasObject = _towerDefenseAutoplayCanvas.gameObject;
        if (!canvasObject.activeSelf) canvasObject.SetActive(true);
        // The character is present immediately; only the tactical graphics resolve
        // in. This avoids turning a foreground portrait into a translucent panel.
        if (_towerDefenseAutoplayCanvasGroup != null)
            _towerDefenseAutoplayCanvasGroup.alpha = 1f;
        float startAlpha = _towerDefenseAutoplayInterfaceGroup != null
            ? Mathf.Clamp01(_towerDefenseAutoplayInterfaceGroup.alpha)
            : 1f;
        SetTowerDefenseAutoplayHudInteraction(false);
        if (_towerDefenseAutoplayInterfaceGroup == null || startAlpha >= 0.999f)
        {
            if (_towerDefenseAutoplayInterfaceGroup != null)
                _towerDefenseAutoplayInterfaceGroup.alpha = 1f;
            _towerDefenseAutoplayHudPhase =
                TowerDefenseAutoplayHudPhase.Visible;
            SetTowerDefenseAutoplayHudInteraction(true);
            return;
        }

        _towerDefenseAutoplayHudPhase = TowerDefenseAutoplayHudPhase.FadingIn;
        _towerDefenseAutoplayHudTransitionRoutine = StartCoroutine(
            FadeInTowerDefenseAutoplayHud(startAlpha));
    }

    private IEnumerator FadeInTowerDefenseAutoplayHud(float startAlpha)
    {
        float elapsed = 0f;
        while (elapsed < TowerDefenseAutoplayHudFadeInSeconds &&
               _towerDefenseAutoplayHudPhase ==
                   TowerDefenseAutoplayHudPhase.FadingIn)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed /
                TowerDefenseAutoplayHudFadeInSeconds);
            if (_towerDefenseAutoplayInterfaceGroup != null)
                _towerDefenseAutoplayInterfaceGroup.alpha = Mathf.Lerp(startAlpha,
                    1f, Mathf.SmoothStep(0f, 1f, progress));
            yield return null;
        }

        if (_towerDefenseAutoplayHudPhase !=
            TowerDefenseAutoplayHudPhase.FadingIn) yield break;
        if (_towerDefenseAutoplayInterfaceGroup != null)
            _towerDefenseAutoplayInterfaceGroup.alpha = 1f;
        _towerDefenseAutoplayHudPhase = TowerDefenseAutoplayHudPhase.Visible;
        _towerDefenseAutoplayHudTransitionRoutine = null;
        SetTowerDefenseAutoplayHudInteraction(true);
    }

    private void BeginTowerDefenseAutoplayReleasePresentation(string line)
    {
        string releaseLine = string.IsNullOrWhiteSpace(line)
            ? "指挥权已交还，战术记录转入后台。"
            : line.Trim();
        StopTowerDefenseAutoplayHudTransition();
        _towerDefenseAutoplayHudPhase =
            TowerDefenseAutoplayHudPhase.HoldingRelease;

        if (_towerDefenseAutoplayCanvas != null)
        {
            GameObject canvasObject = _towerDefenseAutoplayCanvas.gameObject;
            if (!canvasObject.activeSelf) canvasObject.SetActive(true);
        }
        if (_towerDefenseAutoplayCanvasGroup != null)
            _towerDefenseAutoplayCanvasGroup.alpha = 1f;
        if (_towerDefenseAutoplayInterfaceGroup != null)
            _towerDefenseAutoplayInterfaceGroup.alpha = 1f;
        PrepareGameplayHudForAutoplayReleaseCrossfade();
        SetTowerDefenseAutoplayHudInteraction(false);

        // The release line uses the existing speech field/revision so it is rendered
        // in the commander's panel, but its lifetime below is wall-clock based. Camera
        // handoff pauses game time, so a scaled timer would either swallow or stall it.
        PresentTowerDefenseAutoplaySpeech(releaseLine);
        _towerDefenseAutoplayHudTransitionRoutine = StartCoroutine(
            HoldAndFadeOutTowerDefenseAutoplayHud(
                TowerDefenseAutoplayReleaseHoldSeconds));
    }

    private IEnumerator HoldAndFadeOutTowerDefenseAutoplayHud(
        float readingSeconds)
    {
        float elapsed = 0f;
        while (elapsed < readingSeconds &&
               _towerDefenseAutoplayHudPhase ==
                   TowerDefenseAutoplayHudPhase.HoldingRelease)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_towerDefenseAutoplayHudPhase !=
            TowerDefenseAutoplayHudPhase.HoldingRelease) yield break;
        _towerDefenseAutoplayHudPhase = TowerDefenseAutoplayHudPhase.FadingOut;
        float startAlpha = _towerDefenseAutoplayCanvasGroup != null
            ? Mathf.Clamp01(_towerDefenseAutoplayCanvasGroup.alpha)
            : 1f;
        elapsed = 0f;
        while (elapsed < TowerDefenseAutoplayHudFadeOutSeconds &&
               _towerDefenseAutoplayHudPhase ==
                   TowerDefenseAutoplayHudPhase.FadingOut)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed /
                TowerDefenseAutoplayHudFadeOutSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            if (_towerDefenseAutoplayCanvasGroup != null)
                _towerDefenseAutoplayCanvasGroup.alpha = Mathf.Lerp(startAlpha,
                    0f, eased);
            SetGameplayHudCrossfadeProgress(eased);
            yield return null;
        }

        if (_towerDefenseAutoplayHudPhase !=
            TowerDefenseAutoplayHudPhase.FadingOut) yield break;
        _towerDefenseAutoplayEntrancePending = false;
        if (_towerDefenseAutoplayEntranceText != null)
            _towerDefenseAutoplayEntranceText.gameObject.SetActive(false);
        if (_towerDefenseAutoplayCanvasGroup != null)
            _towerDefenseAutoplayCanvasGroup.alpha = 0f;
        CompleteGameplayHudAfterAutoplayCrossfade();
        if (_towerDefenseAutoplayCanvas != null)
            _towerDefenseAutoplayCanvas.gameObject.SetActive(false);
        _towerDefenseAutoplayHudPhase = TowerDefenseAutoplayHudPhase.Hidden;
        _towerDefenseAutoplayHudTransitionRoutine = null;
    }

    private void HideTowerDefenseAutoplayHudImmediately(bool clearSpeech)
    {
        StopTowerDefenseAutoplayHudTransition();
        _towerDefenseAutoplayHudPhase = TowerDefenseAutoplayHudPhase.Hidden;
        SetTowerDefenseAutoplayHudInteraction(false);
        if (_towerDefenseAutoplayCanvasGroup != null)
            _towerDefenseAutoplayCanvasGroup.alpha = 0f;
        if (_towerDefenseAutoplayInterfaceGroup != null)
            _towerDefenseAutoplayInterfaceGroup.alpha = 0f;
        if (_towerDefenseAutoplayCanvas != null &&
            _towerDefenseAutoplayCanvas.gameObject.activeSelf)
            _towerDefenseAutoplayCanvas.gameObject.SetActive(false);
        if (!_towerDefenseAutoplayEnabled && !_towerDefenseStartupActive)
            CompleteGameplayHudAfterAutoplayCrossfade();
        if (!clearSpeech) return;
        _towerDefenseAutoplayEntrancePending = false;
        if (_towerDefenseAutoplayEntranceText != null)
            _towerDefenseAutoplayEntranceText.gameObject.SetActive(false);
    }

    private void StopTowerDefenseAutoplayHudTransition()
    {
        if (_towerDefenseAutoplayHudTransitionRoutine == null) return;
        StopCoroutine(_towerDefenseAutoplayHudTransitionRoutine);
        _towerDefenseAutoplayHudTransitionRoutine = null;
    }

    private void SetTowerDefenseAutoplayHudInteraction(bool interactive)
    {
        if (_towerDefenseAutoplayCanvasGroup == null) return;
        _towerDefenseAutoplayCanvasGroup.interactable = interactive;
        _towerDefenseAutoplayCanvasGroup.blocksRaycasts = interactive;
    }

    private void PrepareGameplayHudForAutoplayReleaseCrossfade()
    {
        if (_towerDefenseHudGroup == null) return;
        _towerDefenseHudGroup.alpha = 0f;
        _towerDefenseHudGroup.interactable = false;
        _towerDefenseHudGroup.blocksRaycasts = false;
    }

    private void SetGameplayHudCrossfadeProgress(float progress)
    {
        if (_towerDefenseHudGroup == null) return;
        _towerDefenseHudGroup.alpha = Mathf.Clamp01(progress);
        _towerDefenseHudGroup.interactable = false;
        _towerDefenseHudGroup.blocksRaycasts = false;
    }

    private void CompleteGameplayHudAfterAutoplayCrossfade()
    {
        if (_towerDefenseHudGroup == null) return;
        _towerDefenseHudGroup.alpha = 1f;
        _towerDefenseHudGroup.interactable = true;
        _towerDefenseHudGroup.blocksRaycasts = true;
    }

    private void BuildTowerDefenseAutoplayUi()
    {
        DisposeTowerDefenseAutoplayUi();

        GameObject canvasObject = new GameObject("Autoplay Companion Canvas");
        canvasObject.transform.SetParent(transform, false);
        _towerDefenseAutoplayCanvas = canvasObject.AddComponent<Canvas>();
        _towerDefenseAutoplayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _towerDefenseAutoplayCanvas.sortingOrder = 60;
        _towerDefenseAutoplayCanvasGroup = canvasObject.AddComponent<CanvasGroup>();
        _towerDefenseAutoplayCanvasGroup.alpha = 0f;
        _towerDefenseAutoplayCanvasGroup.interactable = false;
        _towerDefenseAutoplayCanvasGroup.blocksRaycasts = false;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        RougeTowerDefenseUiLayout.ConfigureCanvasScaler(scaler);
        canvasObject.AddComponent<GraphicRaycaster>();

        _towerDefenseAutoplayHudRoot = new GameObject(
            "Autoplay Companion HUD", typeof(RectTransform));
        _towerDefenseAutoplayHudRoot.transform.SetParent(canvasObject.transform, false);
        StretchRect(_towerDefenseAutoplayHudRoot.GetComponent<RectTransform>(),
            0f, 0f, 0f, 0f);

        GameObject interfaceLayer = new GameObject(
            "Autoplay Tactical Interface", typeof(RectTransform),
            typeof(CanvasGroup));
        interfaceLayer.transform.SetParent(_towerDefenseAutoplayHudRoot.transform, false);
        StretchRect(interfaceLayer.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        _towerDefenseAutoplayInterfaceGroup =
            interfaceLayer.GetComponent<CanvasGroup>();
        _towerDefenseAutoplayInterfaceGroup.alpha = 0f;
        _towerDefenseAutoplayInterfaceGroup.interactable = false;
        _towerDefenseAutoplayInterfaceGroup.blocksRaycasts = false;

        BuildTowerDefenseAutoplayBasePanel(interfaceLayer.transform);
        BuildTowerDefenseAutoplayThoughtPanel(interfaceLayer.transform);
        BuildTowerDefenseAutoplayPortrait(_towerDefenseAutoplayHudRoot.transform);
        // The companion is a foreground character, not another translucent HUD
        // layer. Keep all decorative panels and rails behind the portrait.
        if (_towerDefenseAutoplayPortrait != null)
            _towerDefenseAutoplayPortrait.transform.SetAsLastSibling();
        if (_towerDefenseAutoplayPortraitPulse != null)
            _towerDefenseAutoplayPortraitPulse.transform.SetAsLastSibling();

        _towerDefenseAutoplayCanvas.gameObject.SetActive(false);
        _towerDefenseAutoplayHudPhase = TowerDefenseAutoplayHudPhase.Hidden;
        _towerDefenseAutoplayRenderedEntranceRevision = -1;
        _towerDefenseAutoplayRenderedThoughtRevision = -1;
        _towerDefenseAutoplayIdentityRendered = false;
    }

    private void BuildTowerDefenseAutoplayBasePanel(Transform parent)
    {
        Sprite backdropGradient =
            CreateTowerDefenseAutoplayBackdropGradientSprite();
        Image body = CreateUiImage("Autoplay Companion Backdrop", parent,
            new Color(0.005f, 0.30f, 0.42f, 0.56f));
        body.sprite = backdropGradient;
        body.type = Image.Type.Simple;
        RectTransform bodyRect = body.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(0f, 0f);
        bodyRect.pivot = new Vector2(0f, 0f);
        bodyRect.anchoredPosition = new Vector2(154f, 12f);
        bodyRect.sizeDelta = new Vector2(640f, 294f);

        Image topLine = CreateUiImage("Autoplay Companion Top Line", body.transform,
            new Color(0.12f, 0.84f, 1f, 0.62f));
        topLine.sprite = backdropGradient;
        topLine.type = Image.Type.Simple;
        RectTransform topLineRect = topLine.rectTransform;
        topLineRect.anchorMin = new Vector2(0f, 1f);
        topLineRect.anchorMax = new Vector2(1f, 1f);
        topLineRect.pivot = new Vector2(0.5f, 1f);
        topLineRect.anchoredPosition = Vector2.zero;
        topLineRect.sizeDelta = new Vector2(0f, 2f);

        Image leftLine = CreateUiImage("Autoplay Companion Left Line", body.transform,
            new Color(0.12f, 0.84f, 1f, 0.36f));
        RectTransform leftLineRect = leftLine.rectTransform;
        leftLineRect.anchorMin = new Vector2(0f, 0f);
        leftLineRect.anchorMax = new Vector2(0f, 1f);
        leftLineRect.pivot = new Vector2(0f, 0.5f);
        leftLineRect.anchoredPosition = Vector2.zero;
        leftLineRect.sizeDelta = new Vector2(2f, 0f);
    }

    private Sprite CreateTowerDefenseAutoplayBackdropGradientSprite()
    {
        if (_towerDefenseAutoplayBackdropGradientSprite != null)
            return _towerDefenseAutoplayBackdropGradientSprite;

        const int textureWidth = 64;
        const float fadeStart = 0.85f;
        Texture2D texture = new Texture2D(textureWidth, 1,
            TextureFormat.RGBA32, false, true)
        {
            name = "Autoplay Backdrop Right Fade",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[textureWidth];
        for (int x = 0; x < textureWidth; x++)
        {
            float normalized = x / (float)(textureWidth - 1);
            float fade = Mathf.InverseLerp(fadeStart, 1f, normalized);
            byte alpha = (byte)Mathf.RoundToInt(
                (1f - Mathf.SmoothStep(0f, 1f, fade)) * 255f);
            pixels[x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(texture,
            new Rect(0f, 0f, textureWidth, 1f),
            new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect);
        sprite.name = "Autoplay Backdrop Right Fade";
        _towerDefenseAutoplayBackdropGradientTexture = texture;
        _towerDefenseAutoplayBackdropGradientSprite = sprite;
        return sprite;
    }

    private void BuildTowerDefenseAutoplayPortrait(Transform parent)
    {
        _towerDefenseAutoplayPortrait = CreateUiImage(
            "Autoplay Character Portrait", parent, Color.white);
        _towerDefenseAutoplayPortrait.raycastTarget = true;
        _towerDefenseAutoplayPortrait.sprite = TowerDefenseAutoplayCommander
            .ResolvePortraitSprite(GetTowerDefenseAutoplayPortraitEmotion(),
                RougeAutoplayCommanderPortraitVariant.Base);
        _towerDefenseAutoplayPortrait.preserveAspect = true;
        RectTransform portraitRect = _towerDefenseAutoplayPortrait.rectTransform;
        portraitRect.anchorMin = new Vector2(0f, 0f);
        portraitRect.anchorMax = new Vector2(0f, 0f);
        portraitRect.pivot = new Vector2(0f, 0f);
        portraitRect.anchoredPosition = new Vector2(12f, 8f);
        portraitRect.sizeDelta = new Vector2(310f, 476f);

        _towerDefenseAutoplayPortraitPulse = CreateUiImage(
            "Autoplay Portrait Interaction Pulse", parent, Color.clear);
        _towerDefenseAutoplayPortraitPulse.sprite =
            _towerDefenseAutoplayPortrait.sprite;
        _towerDefenseAutoplayPortraitPulse.preserveAspect = true;
        _towerDefenseAutoplayPortraitPulse.raycastTarget = false;
        RectTransform pulseRect = _towerDefenseAutoplayPortraitPulse.rectTransform;
        pulseRect.anchorMin = portraitRect.anchorMin;
        pulseRect.anchorMax = portraitRect.anchorMax;
        pulseRect.pivot = portraitRect.pivot;
        pulseRect.anchoredPosition = portraitRect.anchoredPosition;
        pulseRect.sizeDelta = portraitRect.sizeDelta;
        pulseRect.localScale = Vector3.one;
        pulseRect.SetSiblingIndex(portraitRect.GetSiblingIndex());

        _towerDefenseAutoplayPortraitButton =
            _towerDefenseAutoplayPortrait.gameObject.AddComponent<Button>();
        _towerDefenseAutoplayPortraitButton.targetGraphic =
            _towerDefenseAutoplayPortrait;
        _towerDefenseAutoplayPortraitButton.transition =
            Selectable.Transition.None;
        _towerDefenseAutoplayPortraitButton.onClick.AddListener(
            HandleTowerDefenseAutoplayPortraitClicked);
        ResetTowerDefenseAutoplayPortraitInteraction();
    }

    private void HandleTowerDefenseAutoplayPortraitClicked()
    {
        if (!_towerDefenseAutoplayEnabled || _towerDefenseGameOver ||
            _towerDefenseVictory) return;

        float now = Time.unscaledTime;
        float rapidWindow = TowerDefenseAutoplayDialogueTriggers
            .portraitRapidClickWindowSeconds;
        if (_towerDefenseAutoplayPortraitRapidClickCount <= 0 ||
            now - _towerDefenseAutoplayPortraitRapidClickWindowStarted >
            rapidWindow)
        {
            _towerDefenseAutoplayPortraitRapidClickCount = 0;
            _towerDefenseAutoplayPortraitRapidClickWindowStarted = now;
        }
        _towerDefenseAutoplayPortraitRapidClickCount++;
        bool rapid = _towerDefenseAutoplayPortraitRapidClickCount >=
            TowerDefenseAutoplayDialogueTriggers.portraitRapidClickCount;
        PlayTowerDefenseAutoplayPortraitInteractionFeedback(rapid);

        if (rapid)
        {
            _towerDefenseAutoplayPortraitRapidClickCount = 0;
            _towerDefenseAutoplayPortraitRapidClickWindowStarted = now;
            if (now - _towerDefenseAutoplayLastPortraitRapidDialogueTime >=
                TowerDefenseAutoplayDialogueTriggers
                    .portraitRapidClickDialogueCooldownSeconds &&
                TryEmitTowerDefenseAutoplayInteractionDialogue(
                    GetAutoplayPortraitDialogueCategory(true)))
            {
                _towerDefenseAutoplayLastPortraitRapidDialogueTime = now;
                _towerDefenseAutoplayLastPortraitClickDialogueTime = now;
                return;
            }
        }

        if (now - _towerDefenseAutoplayLastPortraitClickDialogueTime <
            TowerDefenseAutoplayDialogueTriggers
                .portraitClickDialogueCooldownSeconds) return;
        if (TryEmitTowerDefenseAutoplayInteractionDialogue(
                GetAutoplayPortraitDialogueCategory(false)))
            _towerDefenseAutoplayLastPortraitClickDialogueTime = now;
    }

    private AutoplayDialogueCategory GetAutoplayPortraitDialogueCategory(
        bool rapid)
    {
        if (rapid)
        {
            switch (_towerDefenseAutoplayEmotionState)
            {
                case AutoplayEmotionState.Focused:
                    return AutoplayDialogueCategory.PortraitRapidClickFocused;
                case AutoplayEmotionState.Tense:
                    return AutoplayDialogueCategory.PortraitRapidClickTense;
                case AutoplayEmotionState.Critical:
                    return AutoplayDialogueCategory.PortraitRapidClickCritical;
                default:
                    return AutoplayDialogueCategory.PortraitRapidClickCalm;
            }
        }
        switch (_towerDefenseAutoplayEmotionState)
        {
            case AutoplayEmotionState.Focused:
                return AutoplayDialogueCategory.PortraitClickFocused;
            case AutoplayEmotionState.Tense:
                return AutoplayDialogueCategory.PortraitClickTense;
            case AutoplayEmotionState.Critical:
                return AutoplayDialogueCategory.PortraitClickCritical;
            default:
                return AutoplayDialogueCategory.PortraitClickCalm;
        }
    }

    private void PlayTowerDefenseAutoplayPortraitInteractionFeedback(bool rapid)
    {
        _towerDefenseAutoplayPortraitPulseStarted = Time.unscaledTime;
        _towerDefenseAutoplayPortraitPulseDuration = rapid ? 0.58f : 0.34f;
        _towerDefenseAutoplayPortraitPulseIsRapid = rapid;
        Color emotionColor;
        switch (_towerDefenseAutoplayEmotionState)
        {
            case AutoplayEmotionState.Focused:
                emotionColor = new Color(0.3f, 0.58f, 1f, 1f);
                break;
            case AutoplayEmotionState.Tense:
                emotionColor = new Color(1f, 0.56f, 0.12f, 1f);
                break;
            case AutoplayEmotionState.Critical:
                emotionColor = new Color(1f, 0.18f, 0.52f, 1f);
                break;
            default:
                emotionColor = RemapCommanderInterfaceColor(
                    new Color(0.12f, 0.88f, 1f, 1f));
                break;
        }
        _towerDefenseAutoplayPortraitPulseColor = rapid
            ? Color.Lerp(emotionColor, Color.white, 0.18f)
            : emotionColor;
        RefreshTowerDefenseAutoplayPortraitSprite(true);
    }

    private void RefreshTowerDefenseAutoplayPortraitSprite(bool force = false)
    {
        if (_towerDefenseAutoplayPortrait == null) return;
        _towerDefenseAutoplayPortrait.color = Color.white;
        _towerDefenseAutoplayPortrait.canvasRenderer.SetAlpha(1f);
        RougeAutoplayCommanderPortraitEmotion emotion =
            GetTowerDefenseAutoplayPortraitEmotion();
        float elapsed = Time.unscaledTime -
                        _towerDefenseAutoplayPortraitPulseStarted;
        bool interactionActive = elapsed >= 0f &&
            elapsed < _towerDefenseAutoplayPortraitPulseDuration;
        RougeAutoplayCommanderPortraitVariant variant = interactionActive
            ? _towerDefenseAutoplayPortraitPulseIsRapid
                ? RougeAutoplayCommanderPortraitVariant.RapidClick
                : RougeAutoplayCommanderPortraitVariant.Click
            : RougeAutoplayCommanderPortraitVariant.Base;
        if (!force &&
            emotion == _towerDefenseAutoplayRenderedPortraitEmotion &&
            variant == _towerDefenseAutoplayRenderedPortraitVariant)
            return;

        Sprite sprite = TowerDefenseAutoplayCommander.ResolvePortraitSprite(
            emotion, variant);
        if (sprite != null)
        {
            // Only the sprite changes. The portrait and pulse retain the same
            // RectTransforms, anchors, pivots and sizing across every state.
            _towerDefenseAutoplayPortrait.sprite = sprite;
            if (_towerDefenseAutoplayPortraitPulse != null)
                _towerDefenseAutoplayPortraitPulse.sprite = sprite;
        }
        _towerDefenseAutoplayRenderedPortraitEmotion = emotion;
        _towerDefenseAutoplayRenderedPortraitVariant = variant;
    }

    private RougeAutoplayCommanderPortraitEmotion
        GetTowerDefenseAutoplayPortraitEmotion()
    {
        switch (_towerDefenseAutoplayEmotionState)
        {
            case AutoplayEmotionState.Focused:
                return RougeAutoplayCommanderPortraitEmotion.Focused;
            case AutoplayEmotionState.Tense:
                return RougeAutoplayCommanderPortraitEmotion.Tense;
            case AutoplayEmotionState.Critical:
                return RougeAutoplayCommanderPortraitEmotion.Critical;
            default:
                return RougeAutoplayCommanderPortraitEmotion.Calm;
        }
    }

    private void UpdateTowerDefenseAutoplayPortraitInteractionFeedback()
    {
        if (_towerDefenseAutoplayPortrait == null ||
            _towerDefenseAutoplayPortraitPulse == null) return;
        float elapsed = Time.unscaledTime -
                        _towerDefenseAutoplayPortraitPulseStarted;
        if (elapsed < 0f ||
            elapsed >= _towerDefenseAutoplayPortraitPulseDuration)
        {
            _towerDefenseAutoplayPortrait.rectTransform.localScale = Vector3.one;
            _towerDefenseAutoplayPortraitPulse.rectTransform.localScale =
                Vector3.one;
            _towerDefenseAutoplayPortraitPulse.color = Color.clear;
            return;
        }

        float progress = Mathf.Clamp01(elapsed /
            Mathf.Max(0.01f, _towerDefenseAutoplayPortraitPulseDuration));
        float portraitPunch = Mathf.Sin(progress * Mathf.PI) *
                              (_towerDefenseAutoplayPortraitPulseIsRapid
                                  ? 0.055f
                                  : 0.026f);
        _towerDefenseAutoplayPortrait.rectTransform.localScale =
            Vector3.one * (1f + portraitPunch);
        float pulseExpansion = Mathf.SmoothStep(0f,
            _towerDefenseAutoplayPortraitPulseIsRapid ? 0.18f : 0.1f,
            progress);
        _towerDefenseAutoplayPortraitPulse.rectTransform.localScale =
            Vector3.one * (1f + pulseExpansion);
        Color pulseColor = _towerDefenseAutoplayPortraitPulseColor;
        pulseColor.a = (1f - progress) *
                       (_towerDefenseAutoplayPortraitPulseIsRapid ? 0.72f : 0.42f);
        _towerDefenseAutoplayPortraitPulse.color = pulseColor;
    }

    private void ResetTowerDefenseAutoplayPortraitInteraction()
    {
        _towerDefenseAutoplayPortraitRapidClickCount = 0;
        _towerDefenseAutoplayPortraitRapidClickWindowStarted =
            float.NegativeInfinity;
        _towerDefenseAutoplayLastPortraitClickDialogueTime =
            float.NegativeInfinity;
        _towerDefenseAutoplayLastPortraitRapidDialogueTime =
            float.NegativeInfinity;
        _towerDefenseAutoplayPortraitPulseStarted = float.NegativeInfinity;
        _towerDefenseAutoplayPortraitPulseDuration = 0f;
        _towerDefenseAutoplayPortraitPulseIsRapid = false;
        _towerDefenseAutoplayPortraitPulseColor = Color.clear;
        if (_towerDefenseAutoplayPortrait != null)
        {
            _towerDefenseAutoplayPortrait.raycastTarget = true;
            _towerDefenseAutoplayPortrait.color = Color.white;
            _towerDefenseAutoplayPortrait.canvasRenderer.SetAlpha(1f);
            _towerDefenseAutoplayPortrait.rectTransform.localScale = Vector3.one;
        }
        if (_towerDefenseAutoplayPortraitButton != null)
            _towerDefenseAutoplayPortraitButton.interactable = true;
        if (_towerDefenseAutoplayPortraitPulse != null)
        {
            _towerDefenseAutoplayPortraitPulse.rectTransform.localScale =
                Vector3.one;
            _towerDefenseAutoplayPortraitPulse.color = Color.clear;
        }
        RefreshTowerDefenseAutoplayPortraitSprite(true);
    }

    private void BuildTowerDefenseAutoplayThoughtPanel(Transform parent)
    {
        GameObject summary = new GameObject(
            "Autoplay Top Left Summary", typeof(RectTransform));
        summary.transform.SetParent(parent, false);
        RectTransform summaryRect = summary.GetComponent<RectTransform>();
        summaryRect.anchorMin = new Vector2(0f, 1f);
        summaryRect.anchorMax = new Vector2(0f, 1f);
        summaryRect.pivot = new Vector2(0f, 1f);
        summaryRect.anchoredPosition = new Vector2(24f, -24f);
        summaryRect.sizeDelta = new Vector2(480f, 88f);

        GameObject panel = new GameObject(
            "Autoplay Thought Log", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 0f);
        panelRect.pivot = new Vector2(0f, 0f);
        panelRect.anchoredPosition = new Vector2(270f, 16f);
        panelRect.sizeDelta = new Vector2(500f, 286f);

        Image headingLine = CreateUiImage("Autoplay Heading Line", summary.transform,
            new Color(0.12f, 0.83f, 1f, 0.88f));
        RectTransform headingLineRect = headingLine.rectTransform;
        headingLineRect.anchorMin = new Vector2(0f, 1f);
        headingLineRect.anchorMax = new Vector2(0f, 1f);
        headingLineRect.pivot = new Vector2(0f, 1f);
        headingLineRect.anchoredPosition = Vector2.zero;
        headingLineRect.sizeDelta = new Vector2(188f, 2f);

        Image headingTick = CreateUiImage("Autoplay Heading Tick", summary.transform,
            new Color(0.12f, 0.83f, 1f, 0.72f));
        RectTransform headingTickRect = headingTick.rectTransform;
        headingTickRect.anchorMin = new Vector2(0f, 1f);
        headingTickRect.anchorMax = new Vector2(0f, 1f);
        headingTickRect.pivot = new Vector2(0f, 1f);
        headingTickRect.anchoredPosition = Vector2.zero;
        headingTickRect.sizeDelta = new Vector2(2f, 20f);

        _towerDefenseAutoplayRoleText = CreateUiText(
            "Autoplay Character Identity", summary.transform,
            27, TextAnchor.MiddleLeft);
        _towerDefenseAutoplayRoleText.supportRichText = true;
        StrengthenTowerDefenseAutoplayText(_towerDefenseAutoplayRoleText);
        RectTransform roleRect = _towerDefenseAutoplayRoleText.rectTransform;
        roleRect.anchorMin = new Vector2(0f, 1f);
        roleRect.anchorMax = new Vector2(1f, 1f);
        roleRect.pivot = new Vector2(0.5f, 1f);
        roleRect.offsetMin = new Vector2(12f, -37f);
        roleRect.offsetMax = new Vector2(-8f, -3f);
        _towerDefenseAutoplayRoleText.resizeTextForBestFit = true;
        _towerDefenseAutoplayRoleText.resizeTextMinSize = 17;
        _towerDefenseAutoplayRoleText.resizeTextMaxSize = 27;
        _towerDefenseAutoplayRoleText.horizontalOverflow =
            HorizontalWrapMode.Wrap;
        _towerDefenseAutoplayRoleText.verticalOverflow =
            VerticalWrapMode.Truncate;

        _towerDefenseAutoplayStatusText = CreateUiText(
            "Autoplay Time Status", summary.transform,
            19, TextAnchor.MiddleLeft);
        _towerDefenseAutoplayStatusText.supportRichText = true;
        _towerDefenseAutoplayStatusText.color =
            new Color(0.9f, 0.98f, 1f, 1f);
        StrengthenTowerDefenseAutoplayText(_towerDefenseAutoplayStatusText);
        RectTransform statusRect = _towerDefenseAutoplayStatusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.offsetMin = new Vector2(12f, -86f);
        statusRect.offsetMax = new Vector2(-8f, -34f);
        _towerDefenseAutoplayStatusText.resizeTextForBestFit = true;
        _towerDefenseAutoplayStatusText.resizeTextMinSize = 13;
        _towerDefenseAutoplayStatusText.resizeTextMaxSize = 19;
        _towerDefenseAutoplayStatusText.horizontalOverflow =
            HorizontalWrapMode.Wrap;
        _towerDefenseAutoplayStatusText.verticalOverflow =
            VerticalWrapMode.Truncate;

        BuildTowerDefenseAutoplayHealthLine(panel.transform,
            "Main Tower", -4f, new Color(0.16f, 0.90f, 1f, 1f),
            out _towerDefenseAutoplayMainHealthText,
            out _towerDefenseAutoplayMainHealthFill);
        BuildTowerDefenseAutoplayHealthLine(panel.transform,
            "Boss", -32f, new Color(1f, 0.24f, 0.72f, 1f),
            out _towerDefenseAutoplayBossHealthText,
            out _towerDefenseAutoplayBossHealthFill);

        _towerDefenseAutoplayThoughtTitle = CreateUiText(
            "Autoplay Thought Title", panel.transform,
            18, TextAnchor.MiddleLeft);
        _towerDefenseAutoplayThoughtTitle.text = "刚才";
        _towerDefenseAutoplayThoughtTitle.fontStyle = FontStyle.Bold;
        _towerDefenseAutoplayThoughtTitle.color =
            new Color(0.7f, 0.96f, 1f, 1f);
        StrengthenTowerDefenseAutoplayText(_towerDefenseAutoplayThoughtTitle);
        RectTransform titleRect = _towerDefenseAutoplayThoughtTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(12f, -144f);
        titleRect.sizeDelta = new Vector2(48f, 24f);

        BuildTowerDefenseAutoplayHeartbeat(panel.transform);

        _towerDefenseAutoplayEntranceText = CreateUiText(
            "Autoplay Entrance Line", panel.transform,
            22, TextAnchor.UpperLeft);
        _towerDefenseAutoplayEntranceText.fontStyle = FontStyle.Normal;
        _towerDefenseAutoplayEntranceText.color =
            new Color(0.96f, 0.99f, 1f, 1f);
        StrengthenTowerDefenseAutoplayText(_towerDefenseAutoplayEntranceText);
        _towerDefenseAutoplayEntranceText.horizontalOverflow =
            HorizontalWrapMode.Wrap;
        _towerDefenseAutoplayEntranceText.verticalOverflow =
            VerticalWrapMode.Truncate;
        _towerDefenseAutoplayEntranceText.resizeTextForBestFit = true;
        _towerDefenseAutoplayEntranceText.resizeTextMinSize = 16;
        _towerDefenseAutoplayEntranceText.resizeTextMaxSize = 22;
        RectTransform entranceRect = _towerDefenseAutoplayEntranceText.rectTransform;
        entranceRect.anchorMin = new Vector2(0f, 1f);
        entranceRect.anchorMax = new Vector2(1f, 1f);
        entranceRect.pivot = new Vector2(0.5f, 1f);
        entranceRect.offsetMin = new Vector2(12f, -132f);
        entranceRect.offsetMax = new Vector2(-8f, -62f);
        _towerDefenseAutoplayEntranceText.gameObject.SetActive(false);

        Image logLine = CreateUiImage("Autoplay Log Divider", panel.transform,
            new Color(0.12f, 0.83f, 1f, 0.42f));
        RectTransform logLineRect = logLine.rectTransform;
        logLineRect.anchorMin = new Vector2(0f, 1f);
        logLineRect.anchorMax = new Vector2(1f, 1f);
        logLineRect.pivot = new Vector2(0.5f, 1f);
        logLineRect.anchoredPosition = new Vector2(0f, -138f);
        logLineRect.sizeDelta = new Vector2(0f, 1f);

        GameObject scrollObject = new GameObject(
            "Autoplay Thought Scroller", typeof(RectTransform), typeof(ScrollRect));
        scrollObject.transform.SetParent(panel.transform, false);
        RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(12f, 31f);
        scrollRect.offsetMax = new Vector2(-8f, -174f);

        GameObject viewportObject = new GameObject(
            "Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        StretchRect(viewportRect, 0f, 0f, 0f, 0f);

        _towerDefenseAutoplayThoughtText = CreateUiText(
            "Thoughts", viewportObject.transform, 18, TextAnchor.UpperLeft);
        _towerDefenseAutoplayThoughtText.color =
            new Color(0.94f, 0.98f, 1f, 1f);
        _towerDefenseAutoplayThoughtText.lineSpacing = 1.05f;
        StrengthenTowerDefenseAutoplayText(_towerDefenseAutoplayThoughtText);
        _towerDefenseAutoplayThoughtText.horizontalOverflow =
            HorizontalWrapMode.Wrap;
        _towerDefenseAutoplayThoughtText.verticalOverflow =
            VerticalWrapMode.Overflow;
        RectTransform contentRect = _towerDefenseAutoplayThoughtText.rectTransform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        ContentSizeFitter fitter =
            _towerDefenseAutoplayThoughtText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _towerDefenseAutoplayThoughtScroll = scrollObject.GetComponent<ScrollRect>();
        _towerDefenseAutoplayThoughtScroll.viewport = viewportRect;
        _towerDefenseAutoplayThoughtScroll.content = contentRect;
        _towerDefenseAutoplayThoughtScroll.horizontal = false;
        _towerDefenseAutoplayThoughtScroll.vertical = true;
        _towerDefenseAutoplayThoughtScroll.movementType =
            ScrollRect.MovementType.Clamped;
        _towerDefenseAutoplayThoughtScroll.inertia = false;

        _towerDefenseAutoplayHintText = CreateUiText(
            "Autoplay View Hint", panel.transform, 16, TextAnchor.MiddleLeft);
        _towerDefenseAutoplayHintText.text =
            "[F2] 隐藏界面     [F6] 结束托管     [F10] 加速";
        _towerDefenseAutoplayHintText.color =
            new Color(0.72f, 0.92f, 0.98f, 1f);
        StrengthenTowerDefenseAutoplayText(_towerDefenseAutoplayHintText);
        _towerDefenseAutoplayHintText.resizeTextForBestFit = true;
        _towerDefenseAutoplayHintText.resizeTextMinSize = 12;
        _towerDefenseAutoplayHintText.resizeTextMaxSize = 16;
        RectTransform hintRect = _towerDefenseAutoplayHintText.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.offsetMin = new Vector2(12f, 1f);
        hintRect.offsetMax = new Vector2(-8f, 27f);
    }

    private void BuildTowerDefenseAutoplayHeartbeat(Transform parent)
    {
        GameObject root = new GameObject("Autoplay Virtual Heartbeat",
            typeof(RectTransform), typeof(RectMask2D));
        root.transform.SetParent(parent, false);
        _towerDefenseAutoplayHeartbeatRoot = root.GetComponent<RectTransform>();
        _towerDefenseAutoplayHeartbeatRoot.anchorMin = new Vector2(0f, 1f);
        _towerDefenseAutoplayHeartbeatRoot.anchorMax = new Vector2(0f, 1f);
        _towerDefenseAutoplayHeartbeatRoot.pivot = new Vector2(0f, 0.5f);
        _towerDefenseAutoplayHeartbeatRoot.anchoredPosition =
            new Vector2(70f, -156f);
        _towerDefenseAutoplayHeartbeatRoot.sizeDelta = new Vector2(
            TowerDefenseAutoplayHeartbeatWidth,
            TowerDefenseAutoplayHeartbeatHeight);

        _towerDefenseAutoplayHeartbeatSegments = new Image[
            TowerDefenseAutoplayHeartbeatSegmentCount - 1];
        for (int i = 0; i < _towerDefenseAutoplayHeartbeatSegments.Length; i++)
        {
            Image segment = CreateUiImage("Pulse Segment " + i, root.transform,
                new Color(0.28f, 0.94f, 1f, 0.82f));
            segment.raycastTarget = false;
            RectTransform rect = segment.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            _towerDefenseAutoplayHeartbeatSegments[i] = segment;
        }
        _towerDefenseAutoplayHeartbeatTrace = new float[
            TowerDefenseAutoplayHeartbeatSegmentCount];
        float initialBpm = CalculateTowerDefenseAutoplayHeartbeatBpm(0.08f);
        float sampleInterval = TowerDefenseAutoplayHeartbeatWindowSeconds /
            Mathf.Max(1, _towerDefenseAutoplayHeartbeatTrace.Length - 1);
        _towerDefenseAutoplayHeartbeatPhase = 0f;
        _towerDefenseAutoplayHeartbeatSampleAccumulator = 0f;
        for (int sample = 0;
             sample < _towerDefenseAutoplayHeartbeatTrace.Length; sample++)
        {
            float historicalSeconds =
                (sample - (_towerDefenseAutoplayHeartbeatTrace.Length - 1)) *
                sampleInterval;
            _towerDefenseAutoplayHeartbeatTrace[sample] =
                EvaluateTowerDefenseAutoplayHeartbeatWave(
                    historicalSeconds * initialBpm / 60f);
        }
        UpdateTowerDefenseAutoplayHeartbeatGeometry(0.08f, initialBpm);
    }

    private void UpdateTowerDefenseAutoplayHeartbeat()
    {
        if (_towerDefenseAutoplayHeartbeatRoot == null ||
            _towerDefenseAutoplayHeartbeatSegments == null ||
            _towerDefenseAutoplayHeartbeatTrace == null ||
            _towerDefenseAutoplayHeartbeatTrace.Length < 2) return;

        float target = Mathf.Clamp01(_towerDefenseAutoplayTensionTarget);
        float response = target > _towerDefenseAutoplayDisplayedTension
            ? 1.1f
            : 0.45f;
        _towerDefenseAutoplayDisplayedTension = Mathf.MoveTowards(
            _towerDefenseAutoplayDisplayedTension, target,
            Mathf.Max(0f, Time.unscaledDeltaTime) * response);
        float beatsPerMinute = CalculateTowerDefenseAutoplayHeartbeatBpm(
            _towerDefenseAutoplayDisplayedTension);
        float sampleInterval = TowerDefenseAutoplayHeartbeatWindowSeconds /
            Mathf.Max(1, _towerDefenseAutoplayHeartbeatTrace.Length - 1);
        _towerDefenseAutoplayHeartbeatSampleAccumulator += Mathf.Min(0.25f,
            Mathf.Max(0f, Time.unscaledDeltaTime));
        while (_towerDefenseAutoplayHeartbeatSampleAccumulator >= sampleInterval)
        {
            _towerDefenseAutoplayHeartbeatSampleAccumulator -= sampleInterval;
            _towerDefenseAutoplayHeartbeatPhase = Mathf.Repeat(
                _towerDefenseAutoplayHeartbeatPhase +
                sampleInterval * beatsPerMinute / 60f, 1f);
            for (int i = 1; i < _towerDefenseAutoplayHeartbeatTrace.Length; i++)
                _towerDefenseAutoplayHeartbeatTrace[i - 1] =
                    _towerDefenseAutoplayHeartbeatTrace[i];
            _towerDefenseAutoplayHeartbeatTrace[
                _towerDefenseAutoplayHeartbeatTrace.Length - 1] =
                EvaluateTowerDefenseAutoplayHeartbeatWave(
                    _towerDefenseAutoplayHeartbeatPhase);
        }
        float visiblePhase = Mathf.Repeat(
            _towerDefenseAutoplayHeartbeatPhase +
            _towerDefenseAutoplayHeartbeatSampleAccumulator *
            beatsPerMinute / 60f, 1f);
        float beatGlow = Mathf.SmoothStep(0f, 1f,
            EvaluateTowerDefenseAutoplayHeartbeatWave(
                visiblePhase));
        UpdateTowerDefenseAutoplayHeartbeatGeometry(
            _towerDefenseAutoplayDisplayedTension, beatsPerMinute, beatGlow);
    }

    private static float CalculateTowerDefenseAutoplayHeartbeatBpm(float tension)
    {
        tension = Mathf.Clamp01(tension);
        if (tension <= 0.22f)
            return Mathf.Lerp(50f, 70f,
                Mathf.SmoothStep(0f, 1f, tension / 0.22f));
        if (tension <= 0.68f)
            return Mathf.Lerp(70f, 100f, Mathf.SmoothStep(0f, 1f,
                (tension - 0.22f) / 0.46f));
        return Mathf.Lerp(100f, 150f, Mathf.SmoothStep(0f, 1f,
            (tension - 0.68f) / 0.32f));
    }

    private void UpdateTowerDefenseAutoplayHeartbeatGeometry(float tension,
        float beatsPerMinute, float beatGlow = 0f)
    {
        if (_towerDefenseAutoplayHeartbeatSegments == null ||
            _towerDefenseAutoplayHeartbeatTrace == null ||
            _towerDefenseAutoplayHeartbeatTrace.Length !=
            _towerDefenseAutoplayHeartbeatSegments.Length + 1) return;
        int sampleCount = _towerDefenseAutoplayHeartbeatSegments.Length + 1;
        float baseline = TowerDefenseAutoplayHeartbeatHeight * 0.5f;
        float sampleInterval = TowerDefenseAutoplayHeartbeatWindowSeconds /
            Mathf.Max(1, sampleCount - 1);
        float scroll = Mathf.Clamp01(
            _towerDefenseAutoplayHeartbeatSampleAccumulator /
            Mathf.Max(0.0001f, sampleInterval));
        float amplitude = Mathf.Lerp(6.2f, 7.8f, tension);
        float stroke = Mathf.Lerp(1.35f, 1.8f, tension);
        Color calm = RemapCommanderInterfaceColor(
            new Color(0.2f, 0.86f, 1f, 0.76f));
        Color urgent = new Color(1f, 0.3f, 0.08f, 0.95f);
        float colorHeat = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(70f, 150f, beatsPerMinute));
        Color color = Color.Lerp(calm, urgent, colorHeat);
        color.a *= Mathf.Lerp(0.82f, 1f, beatGlow);

        Vector2 previous = Vector2.zero;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            float x01 = sample / (float)(sampleCount - 1);
            float x = x01 * TowerDefenseAutoplayHeartbeatWidth;
            float current = _towerDefenseAutoplayHeartbeatTrace[sample];
            float next = sample + 1 < sampleCount
                ? _towerDefenseAutoplayHeartbeatTrace[sample + 1]
                : EvaluateTowerDefenseAutoplayHeartbeatWave(
                    _towerDefenseAutoplayHeartbeatPhase +
                    sampleInterval * beatsPerMinute / 60f);
            float y = baseline + Mathf.Lerp(current, next, scroll) * amplitude;
            Vector2 point = new Vector2(x, y);
            if (sample > 0)
            {
                Image segment = _towerDefenseAutoplayHeartbeatSegments[sample - 1];
                Vector2 delta = point - previous;
                RectTransform rect = segment.rectTransform;
                rect.anchoredPosition = (previous + point) * 0.5f;
                rect.sizeDelta = new Vector2(delta.magnitude + 0.8f, stroke);
                rect.localEulerAngles = new Vector3(0f, 0f,
                    Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                segment.color = color;
            }
            previous = point;
        }
    }

    private static float EvaluateTowerDefenseAutoplayHeartbeatWave(float time)
    {
        float phase = Mathf.Repeat(time, 1f);
        return HeartbeatGaussian(phase, 0.18f, 0.065f) * 0.12f -
               HeartbeatGaussian(phase, 0.34f, 0.024f) * 0.18f +
               HeartbeatGaussian(phase, 0.39f, 0.032f) -
               HeartbeatGaussian(phase, 0.435f, 0.026f) * 0.34f +
               HeartbeatGaussian(phase, 0.68f, 0.095f) * 0.2f;
    }

    private static float HeartbeatGaussian(float value, float center, float width)
    {
        float normalized = (value - center) / Mathf.Max(0.001f, width);
        return Mathf.Exp(-normalized * normalized);
    }

    private void BuildTowerDefenseAutoplayHealthLine(Transform parent,
        string objectName, float topOffset, Color accent,
        out Text valueText, out Image fill)
    {
        GameObject row = new GameObject(
            "Autoplay " + objectName + " Health", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(2f, topOffset);
        rowRect.sizeDelta = new Vector2(-20f, 27f);

        valueText = CreateUiText("Value", row.transform, 18,
            TextAnchor.UpperLeft);
        valueText.color = Color.Lerp(accent, Color.white, 0.68f);
        StrengthenTowerDefenseAutoplayText(valueText);
        valueText.resizeTextForBestFit = true;
        valueText.resizeTextMinSize = 13;
        valueText.resizeTextMaxSize = 18;
        valueText.horizontalOverflow = HorizontalWrapMode.Wrap;
        valueText.verticalOverflow = VerticalWrapMode.Truncate;
        RectTransform valueRect = valueText.rectTransform;
        valueRect.anchorMin = new Vector2(0f, 1f);
        valueRect.anchorMax = new Vector2(1f, 1f);
        valueRect.pivot = new Vector2(0.5f, 1f);
        valueRect.anchoredPosition = Vector2.zero;
        valueRect.sizeDelta = new Vector2(0f, 21f);

        Image track = CreateUiImage("Track", row.transform,
            new Color(accent.r, accent.g, accent.b, 0.22f));
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(0f, 2f);
        trackRect.sizeDelta = new Vector2(0f, 3f);

        fill = CreateUiImage("Fill", track.transform, accent);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(0f, -1f);
        fillRect.offsetMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);

        Image cap = CreateUiImage("Cap", row.transform,
            Color.Lerp(accent, Color.white, 0.35f));
        RectTransform capRect = cap.rectTransform;
        capRect.anchorMin = new Vector2(0f, 0f);
        capRect.anchorMax = new Vector2(0f, 0f);
        capRect.pivot = new Vector2(0f, 0f);
        capRect.anchoredPosition = new Vector2(0f, 0f);
        capRect.sizeDelta = new Vector2(3f, 7f);
    }

    private static void StrengthenTowerDefenseAutoplayText(Text text)
    {
        if (text == null || text.GetComponent<Outline>() != null) return;
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0.015f, 0.03f, 0.94f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;
    }

    private void RefreshTowerDefenseAutoplayHealthLines()
    {
        float mainMaximum = mainTower != null
            ? Mathf.Max(1f, mainTower.maxHealth)
            : 1f;
        float mainHealth = mainTower != null
            ? Mathf.Clamp(mainTower.CurrentHealth, 0f, mainMaximum)
            : 0f;
        float mainRatio = mainTower != null
            ? Mathf.Clamp01(mainHealth / mainMaximum)
            : 0f;
        if (_towerDefenseAutoplayMainHealthText != null)
            _towerDefenseAutoplayMainHealthText.text = mainTower != null
                ? $"主塔   {mainHealth:0} / {mainMaximum:0}   {mainRatio * 100f:0}%"
                : "主塔   信号未连接";
        SetTowerDefenseAutoplayHealthLineFill(
            _towerDefenseAutoplayMainHealthFill, mainRatio);

        bool bossVisible = _bossSpawned || _bossDeathSequenceActive;
        float bossMaximum = bossVisible ? GetCurrentBossMaxHealth() : 1f;
        float bossHealth = _bossSpawned
            ? Mathf.Clamp(_bossCurrentHealth, 0f, bossMaximum)
            : 0f;
        float bossRatio = bossVisible
            ? Mathf.Clamp01(bossHealth / Mathf.Max(1f, bossMaximum))
            : 0f;
        if (_towerDefenseAutoplayBossHealthText != null)
        {
            string bossName = bossBalance != null
                ? GetLocalizedBossName(bossBalance.displayName)
                : "首领";
            if (bossVisible)
            {
                _towerDefenseAutoplayBossHealthText.text =
                    $"{bossName}   {bossHealth:0} / {bossMaximum:0}   " +
                    $"{bossRatio * 100f:0}%";
            }
            else
            {
                float bossCountdown = GetAutoplaySecondsUntilNextBoss();
                _towerDefenseAutoplayBossHealthText.text =
                    float.IsPositiveInfinity(bossCountdown)
                        ? "首领   本局已无后续目标"
                        : $"首领   {FormatGameTime(bossCountdown)} 后出现";
            }
        }
        SetTowerDefenseAutoplayHealthLineFill(
            _towerDefenseAutoplayBossHealthFill, bossRatio);
    }

    private static void SetTowerDefenseAutoplayHealthLineFill(
        Image fill, float normalizedValue)
    {
        if (fill == null) return;
        RectTransform rect = fill.rectTransform;
        rect.anchorMax = new Vector2(Mathf.Clamp01(normalizedValue), 1f);
    }

    private void RefreshTowerDefenseAutoplayThoughtText()
    {
        if (_towerDefenseAutoplayThoughtText == null) return;

        int count = _towerDefenseAutoplayThoughtLog.Count;
        int first = Mathf.Max(0, count - TowerDefenseAutoplayVisibleThoughtLines);
        StringBuilder builder = new StringBuilder(768);
        string latestColor = CommanderInterfaceColorHex(
            new Color32(0x91, 0xF3, 0xFF, 0xFF));
        string previousColor = CommanderInterfaceColorHex(
            new Color32(0xD4, 0xE8, 0xF0, 0xFF));
        for (int i = first; i < count; i++)
        {
            if (builder.Length > 0) builder.Append('\n');
            bool latest = i == count - 1;
            builder.Append("<color=#")
                .Append(latest ? latestColor : previousColor)
                .Append(latest ? ">› " : ">· ");
            builder.Append(_towerDefenseAutoplayThoughtLog[i]);
            builder.Append("</color>");
        }
        if (builder.Length == 0)
            builder.Append("<color=#").Append(previousColor)
                .Append(">· 正在读取地图与塔位……</color>");

        _towerDefenseAutoplayThoughtText.text = builder.ToString();
        _towerDefenseAutoplayRenderedThoughtRevision =
            _towerDefenseAutoplayThoughtRevision;
        Canvas.ForceUpdateCanvases();
        if (_towerDefenseAutoplayThoughtScroll != null)
            _towerDefenseAutoplayThoughtScroll.verticalNormalizedPosition = 0f;
    }

    private bool HandleTowerDefenseAutoplayToggleInput(Keyboard keyboard)
    {
        if (keyboard == null || !keyboard.f6Key.wasPressedThisFrame) return false;

        if (_towerDefenseAutoplayEnabled)
        {
            SetTowerDefenseAutoplayEnabled(false);
            if (_tiltShiftObservationActive)
                BeginTiltShiftObservationExit(CameraViewMode.Default);
            else if (_cameraViewTransitionPaused)
                ForceCameraViewMode(CameraViewMode.Default);
            return true;
        }

        if (_towerDefenseGameOver || _towerDefenseStartupActive ||
            IsPlayerSettingsOpen || _cameraViewTransitionPaused)
            return true;

        if (_chargeTowerTargetSelectionActive || _chargeTowerEffectSelectionActive)
            CancelPendingChargeTowerConstruction();
        SetTowerPlacementMode(false);
        SetTowerDefenseAutoplayEnabled(true);
        if (!_tiltShiftObservationActive)
            SetCameraViewMode(CameraViewMode.TiltShift);
        // F6 is a command handoff, not a camera-mode shortcut. This intentionally
        // replaces the generic tilt-shift toast emitted by SetCameraViewMode in the
        // same frame, so the top banner describes the player-facing action.
        RougeCameraModeToast.Show(
            TowerDefenseAutoplayCharacterName + " // AI 接管 · 战术托管已连接",
            ActiveCommanderVisualTheme.Accent);
        return true;
    }

    private bool HandleTowerDefenseAutoplayCleanViewInput(Keyboard keyboard)
    {
        if (!_towerDefenseAutoplayEnabled || keyboard == null ||
            !keyboard.f2Key.wasPressedThisFrame) return false;

        ToggleAutoplayCleanView();
        if (_towerDefenseAutoplayCleanView)
        {
            HideF2MainTowerHealth();
        }
        else
        {
            RougeCameraModeToast.Show(
                TowerDefenseAutoplayCharacterName + " // 战术 HUD 已恢复",
                ActiveCommanderVisualTheme.Accent);
        }
        return true;
    }

    private bool HandleTowerDefenseAutoplaySpeedInput(Keyboard keyboard)
    {
        if (!_towerDefenseAutoplayEnabled || keyboard == null ||
            !keyboard.f10Key.wasPressedThisFrame) return false;

        _towerDefenseDoubleSpeed = !_towerDefenseDoubleSpeed;
        ApplyTowerDefenseTimeScale();
        RefreshTowerDefenseUi(true);
        RefreshTowerDefenseAutoplayPresentation();
        return true;
    }

    private void SyncTowerDefenseAutoplayPresentation()
    {
        RefreshTowerDefenseAutoplayPresentation();
    }

    private void ResetTowerDefenseAutoplaySession()
    {
        HideTowerDefenseAutoplayHudImmediately(true);
        LoadTowerDefenseAutoplayProgression();
        _towerDefenseAutoplayEnabled = false;
        _towerDefenseAutoplayCleanView = false;
        _towerDefenseAutoplayTickAccumulator = 0f;
        _towerDefenseAutoplayBuildCursor = 0;
        _towerDefenseAutoplayLastDecision = "托管未启用";
        _towerDefenseAutoplayLastLoggedDecision = string.Empty;
        _towerDefenseAutoplayEntranceLine = string.Empty;
        _towerDefenseAutoplayEntrancePending = false;
        _towerDefenseAutoplaySpeechVisibleUntil = 0f;
        _towerDefenseAutoplayThoughtLog.Clear();
        ResetTowerDefenseAutoplayPortraitInteraction();
        ClearTowerDefenseAutoplaySessionState();
    }

    private void StopTowerDefenseAutoplayForConclusion()
    {
        if (_towerDefenseAutoplayEnabled)
        {
            _towerDefenseAutoplayConclusionStopping = true;
            try
            {
                SetTowerDefenseAutoplayEnabled(false);
            }
            finally
            {
                _towerDefenseAutoplayConclusionStopping = false;
            }
        }
        else
            SetAutoplayCleanView(false);
        HideTowerDefenseAutoplayHudImmediately(true);
    }

    private void DisposeTowerDefenseAutoplayUi()
    {
        StopTowerDefenseAutoplayHudTransition();
        if (_towerDefenseAutoplayPortraitButton != null)
            _towerDefenseAutoplayPortraitButton.onClick.RemoveAllListeners();
        if (_towerDefenseAutoplayCanvas != null)
            Destroy(_towerDefenseAutoplayCanvas.gameObject);
        if (_towerDefenseAutoplayBackdropGradientSprite != null)
            Destroy(_towerDefenseAutoplayBackdropGradientSprite);
        if (_towerDefenseAutoplayBackdropGradientTexture != null)
            Destroy(_towerDefenseAutoplayBackdropGradientTexture);
        _towerDefenseAutoplayCanvas = null;
        _towerDefenseAutoplayCanvasGroup = null;
        _towerDefenseAutoplayInterfaceGroup = null;
        _towerDefenseAutoplayHudTransitionRoutine = null;
        _towerDefenseAutoplayHudPhase = TowerDefenseAutoplayHudPhase.Hidden;
        _towerDefenseAutoplayHudRoot = null;
        _towerDefenseAutoplayPortrait = null;
        _towerDefenseAutoplayPortraitPulse = null;
        _towerDefenseAutoplayPortraitButton = null;
        _towerDefenseAutoplayRoleText = null;
        _towerDefenseAutoplayStatusText = null;
        _towerDefenseAutoplayThoughtTitle = null;
        _towerDefenseAutoplayEntranceText = null;
        _towerDefenseAutoplayThoughtText = null;
        _towerDefenseAutoplayHintText = null;
        _towerDefenseAutoplayMainHealthText = null;
        _towerDefenseAutoplayBossHealthText = null;
        _towerDefenseAutoplayMainHealthFill = null;
        _towerDefenseAutoplayBossHealthFill = null;
        _towerDefenseAutoplayBackdropGradientSprite = null;
        _towerDefenseAutoplayBackdropGradientTexture = null;
        _towerDefenseAutoplayHeartbeatRoot = null;
        _towerDefenseAutoplayHeartbeatSegments = null;
        _towerDefenseAutoplayHeartbeatTrace = null;
        _towerDefenseAutoplayDisplayedTension = 0.08f;
        _towerDefenseAutoplayHeartbeatPhase = 0f;
        _towerDefenseAutoplayHeartbeatSampleAccumulator = 0f;
        _towerDefenseAutoplayThoughtScroll = null;
        _towerDefenseAutoplayRenderedEntranceRevision = -1;
        _towerDefenseAutoplayRenderedThoughtRevision = -1;
        _towerDefenseAutoplayIdentityRendered = false;
        _towerDefenseAutoplayRenderedPortraitEmotion =
            (RougeAutoplayCommanderPortraitEmotion)(-1);
        _towerDefenseAutoplayRenderedPortraitVariant =
            (RougeAutoplayCommanderPortraitVariant)(-1);
    }
}
