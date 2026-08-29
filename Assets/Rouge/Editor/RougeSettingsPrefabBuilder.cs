using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class RougeSettingsPrefabBuilder
{
    private const string UiFolder = "Assets/Rouge/Resources/UI";
    private const string SettingsPrefabPath = UiFolder + "/RougeSettingsMenu.prefab";
    private const string HealthPrefabPath = UiFolder + "/RougeF2MainTowerHealth.prefab";
    private const string CameraToastPrefabPath = UiFolder + "/RougeCameraModeToast.prefab";

    private static readonly Color ScreenDim = new Color(0.002f, 0.012f, 0.025f, 0.82f);
    private static readonly Color WindowBlue = new Color(0.008f, 0.035f, 0.065f, 0.985f);
    private static readonly Color PanelBlue = new Color(0.012f, 0.065f, 0.105f, 0.94f);
    private static readonly Color RowBlue = new Color(0.015f, 0.085f, 0.135f, 0.88f);
    private static readonly Color ButtonBlue = new Color(0.018f, 0.09f, 0.14f, 0.96f);
    private static readonly Color Cyan = new Color(0.08f, 0.82f, 1f, 1f);
    private static readonly Color CyanLine = new Color(0.08f, 0.72f, 0.92f, 0.72f);
    private static readonly Color TextPrimary = new Color(0.82f, 0.97f, 1f, 1f);
    private static readonly Color TextSecondary = new Color(0.46f, 0.68f, 0.76f, 1f);
    private static readonly Color TrackColor = new Color(0.004f, 0.025f, 0.045f, 0.98f);

    private static Font s_editorFont;

    static RougeSettingsPrefabBuilder()
    {
        EditorApplication.delayCall += CreateMissingPrefabs;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += CreateMissingPrefabs;
    }

    [MenuItem("Rouge/UI/Force Rebuild UI Prefabs", false, 2100)]
    private static void ForceRebuildPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Rouge UI] Exit Play Mode before rebuilding settings prefabs.");
            return;
        }

        EnsureAssetFolder(UiFolder);
        bool settingsSaved = BuildSettingsPrefab();
        bool healthSaved = BuildHealthPrefab();
        bool cameraToastSaved = BuildCameraToastPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (settingsSaved && healthSaved && cameraToastSaved)
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
            Debug.Log($"[Rouge UI] Rebuilt editable prefabs:\n{SettingsPrefabPath}\n" +
                      $"{HealthPrefabPath}\n{CameraToastPrefabPath}");
        }
    }

    private static void CreateMissingPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += CreateMissingPrefabs;
            return;
        }

        bool settingsMissing = !AssetExists(SettingsPrefabPath);
        bool healthMissing = !AssetExists(HealthPrefabPath);
        bool cameraToastMissing = !AssetExists(CameraToastPrefabPath);
        if (!settingsMissing && !healthMissing && !cameraToastMissing) return;

        try
        {
            EnsureAssetFolder(UiFolder);
            bool changed = false;
            if (settingsMissing) changed |= BuildSettingsPrefab();
            if (healthMissing) changed |= BuildHealthPrefab();
            if (cameraToastMissing) changed |= BuildCameraToastPrefab();
            if (!changed) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Rouge UI] Created missing editable UI prefab assets. Existing prefabs were left untouched.");
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static bool BuildSettingsPrefab()
    {
        GameObject root = CreateCanvasRoot("Rouge Settings Menu", 500, true,
            out CanvasGroup canvasGroup);
        try
        {
            RougeSettingsMenuView view = root.AddComponent<RougeSettingsMenuView>();
            view.canvasGroup = canvasGroup;

            Image dimmer = CreateImage("Screen Dimmer", root.transform, ScreenDim, true);
            Stretch(dimmer.rectTransform, 0f, 0f, 0f, 0f);

            Image window = CreateImage("Settings Window", root.transform, WindowBlue, true);
            Anchor(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1320f, 820f));
            AddOutline(window, CyanLine, 1.25f);
            AddEdgeLines(window.rectTransform, 2f, CyanLine);

            Image header = CreateImage("Header", window.transform,
                new Color(0.01f, 0.075f, 0.115f, 0.96f), false);
            TopStretch(header.rectTransform, 2f, 2f, 2f, 94f);
            AddBottomLine(header.rectTransform, 2f, CyanLine);

            Text title = CreateText("Title", header.transform, "系统设置", 32,
                TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            Stretch(title.rectTransform, 34f, 24f, 220f, 18f);
            Text subtitle = CreateText("Subtitle", header.transform,
                "SYSTEM CONFIGURATION  //  所有改动即时生效", 14,
                TextSecondary, TextAnchor.LowerLeft, FontStyle.Normal);
            Stretch(subtitle.rectTransform, 36f, 10f, 220f, 55f);

            Button closeButton = CreateButton("Close", header.transform, "×", 30);
            Anchor(closeButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                new Vector2(-26f, -47f), new Vector2(62f, 54f), Vector2.one);
            view.closeButton = closeButton;

            Image tabRail = CreateImage("Tab Rail", window.transform,
                new Color(0.006f, 0.045f, 0.075f, 0.96f), false);
            Stretch(tabRail.rectTransform, 18f, 18f, 1084f, 112f);
            AddOutline(tabRail, new Color(CyanLine.r, CyanLine.g, CyanLine.b, 0.42f), 1f);

            Text category = CreateText("Category Label", tabRail.transform, "设置分类", 15,
                TextSecondary, TextAnchor.MiddleCenter, FontStyle.Bold);
            TopStretch(category.rectTransform, 14f, 16f, 14f, 30f);

            string[] tabLabels = { "图  像", "音  频", "游  戏", "移  轴" };
            Button[] tabButtons = new Button[tabLabels.Length];
            for (int i = 0; i < tabButtons.Length; i++)
            {
                tabButtons[i] = CreateButton($"Tab {i + 1} - {tabLabels[i]}",
                    tabRail.transform, tabLabels[i], 21);
                TopStretch(tabButtons[i].GetComponent<RectTransform>(), 14f,
                    62f + i * 82f, 14f, 64f);
            }
            view.tabButtons = tabButtons;

            Text escapeHint = CreateText("Escape Hint", tabRail.transform,
                "ESC  返回游戏", 14, TextSecondary, TextAnchor.MiddleCenter, FontStyle.Normal);
            BottomStretch(escapeHint.rectTransform, 14f, 18f, 14f, 30f);

            Image content = CreateImage("Content", window.transform, PanelBlue, false);
            Stretch(content.rectTransform, 254f, 18f, 18f, 112f);
            AddOutline(content, new Color(CyanLine.r, CyanLine.g, CyanLine.b, 0.42f), 1f);

            GameObject graphicsPage = CreatePage("Graphics Page", content.transform);
            GameObject audioPage = CreatePage("Audio Page", content.transform);
            GameObject gameplayPage = CreatePage("Gameplay Page", content.transform);
            GameObject tiltPage = CreatePage("Tilt Shift Page", content.transform);
            view.tabPages = new[] { graphicsPage, audioPage, gameplayPage, tiltPage };

            BuildGraphicsPage(graphicsPage.transform, view);
            BuildAudioPage(audioPage.transform, view);
            BuildGameplayPage(gameplayPage.transform, view);
            BuildTiltShiftPage(tiltPage.transform, view);

            graphicsPage.SetActive(true);
            audioPage.SetActive(false);
            gameplayPage.SetActive(false);
            tiltPage.SetActive(false);

            return SavePrefab(root, SettingsPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void BuildGraphicsPage(Transform page, RougeSettingsMenuView view)
    {
        CreatePageHeading(page, "图像设置", "GRAPHICS  //  显示与性能");

        CreateSectionLabel(page, "渲染质量", 82f);
        view.qualityButtons = CreateSegmentButtons(page, "Quality", 116f,
            new[] { "性能", "均衡", "增强" });

        CreateSectionLabel(page, "目标帧率", 202f);
        view.frameRateButtons = CreateSegmentButtons(page, "Frame Rate", 236f,
            new[] { "30", "60", "90", "120", "144", "240", "不限" }, 8f);

        CreateSectionLabel(page, "显示模式", 322f);
        view.displayModeButtons = CreateSegmentButtons(page, "Display Mode", 356f,
            new[] { "独占全屏", "无边框", "窗口" });

        CreateSectionLabel(page, "分辨率", 442f);
        Image resolutionRow = CreateRowPanel("Resolution", page, 476f, 76f);
        view.resolutionPreviousButton = CreateButton("Previous", resolutionRow.transform, "‹", 30);
        Anchor(view.resolutionPreviousButton.GetComponent<RectTransform>(),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 0f),
            new Vector2(64f, 48f));
        view.resolutionNextButton = CreateButton("Next", resolutionRow.transform, "›", 30);
        Anchor(view.resolutionNextButton.GetComponent<RectTransform>(),
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-48f, 0f),
            new Vector2(64f, 48f));
        view.resolutionValueText = CreateText("Resolution Value", resolutionRow.transform,
            "1920 × 1080", 24, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(view.resolutionValueText.rectTransform, 104f, 8f, 104f, 8f);

        Text note = CreateText("Graphics Note", page,
            "提示：切换显示模式或分辨率时，系统可能短暂重建交换链。", 14,
            TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal);
        TopStretch(note.rectTransform, 26f, 575f, 26f, 42f);
    }

    private static void BuildAudioPage(Transform page, RougeSettingsMenuView view)
    {
        CreatePageHeading(page, "音频设置", "AUDIO  //  独立音量控制");

        view.musicSlider = CreateSliderRow(page, "Music Volume", "音乐音量",
            "背景音乐与环境声", 100f, 0f, 100f, true, out Text musicValue);
        view.musicValueText = musicValue;

        view.sfxSlider = CreateSliderRow(page, "Sfx Volume", "音效音量",
            "防御塔攻击、建造与战斗反馈", 244f, 0f, 100f, true, out Text sfxValue);
        view.sfxValueText = sfxValue;

        Image info = CreateRowPanel("Audio Information", page, 410f, 114f);
        Text infoTitle = CreateText("Title", info.transform, "双通道混音", 18,
            TextPrimary, TextAnchor.UpperLeft, FontStyle.Bold);
        Stretch(infoTitle.rectTransform, 20f, 56f, 20f, 18f);
        Text infoBody = CreateText("Body", info.transform,
            "音乐与音效互不影响。数值 0 为静音，100 保留关卡设计的原始响度。", 15,
            TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal);
        Stretch(infoBody.rectTransform, 20f, 14f, 20f, 50f);
    }

    private static void BuildGameplayPage(Transform page, RougeSettingsMenuView view)
    {
        CreatePageHeading(page, "游戏设置", "GAMEPLAY  //  信息显示");
        CreateSectionLabel(page, "战斗界面", 96f);

        Button toggle = CreateButton("Damage Statistics Toggle", page,
            "显示伤害统计", 20);
        TopStretch(toggle.GetComponent<RectTransform>(), 26f, 136f, 26f, 84f);
        Text buttonLabel = toggle.GetComponentInChildren<Text>();
        if (buttonLabel != null)
        {
            buttonLabel.alignment = TextAnchor.MiddleLeft;
            buttonLabel.rectTransform.offsetMin = new Vector2(22f, 6f);
            buttonLabel.rectTransform.offsetMax = new Vector2(-150f, -6f);
        }
        Text toggleValue = CreateText("Value", toggle.transform, "开启", 19,
            Cyan, TextAnchor.MiddleRight, FontStyle.Bold);
        Stretch(toggleValue.rectTransform, 180f, 8f, 22f, 8f);
        view.damageStatisticsButton = toggle;
        view.damageStatisticsValueText = toggleValue;

        Text description = CreateText("Description", page,
            "控制战斗 HUD 中的塔楼伤害排行与累计输出信息。关闭后不影响伤害统计本身。", 15,
            TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal);
        TopStretch(description.rectTransform, 30f, 244f, 30f, 70f);
    }

    private static void BuildTiltShiftPage(Transform page, RougeSettingsMenuView view)
    {
        CreatePageHeading(page, "移轴设置", "TILT SHIFT  //  F2 观测镜头");

        Button customBlurToggle = CreateButton("Custom Blur Toggle", page,
            "自定义模糊", 20);
        TopStretch(customBlurToggle.GetComponent<RectTransform>(), 26f, 88f, 26f, 66f);
        Text toggleLabel = customBlurToggle.GetComponentInChildren<Text>();
        if (toggleLabel != null)
        {
            toggleLabel.alignment = TextAnchor.MiddleLeft;
            toggleLabel.rectTransform.offsetMin = new Vector2(22f, 6f);
            toggleLabel.rectTransform.offsetMax = new Vector2(-180f, -6f);
        }
        Text toggleValue = CreateText("Value", customBlurToggle.transform,
            "跟随关卡", 18, TextSecondary, TextAnchor.MiddleRight, FontStyle.Bold);
        Stretch(toggleValue.rectTransform, 180f, 8f, 22f, 8f);
        view.customTiltBlurButton = customBlurToggle;
        view.customTiltBlurValueText = toggleValue;

        view.tiltBlurSlider = CreateSliderRow(page, "Blur Radius", "虚化强度",
            "开启自定义模糊后可调整景深虚化半径", 176f,
            0f, 100f, true, out Text blurValue);
        view.tiltBlurSlider.interactable = false;
        view.tiltBlurValueText = blurValue;

        view.tiltClearWidthSlider = CreateSliderRow(page, "Clear Band Width", "清晰带宽度",
            "0.50 为关卡基准；左右调整清晰区域覆盖范围", 320f,
            0f, 1f, false, out Text clearValue);
        view.tiltClearWidthValueText = clearValue;

        Image notePanel = CreateRowPanel("Tilt Shift Note", page, 486f, 108f);
        Text note = CreateText("Body", notePanel.transform,
            "默认模糊跟随关卡配置；自定义参数只修饰 F2 观测画面，不改变镜头视距与地图边界。", 15,
            TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal);
        Stretch(note.rectTransform, 22f, 16f, 22f, 16f);
    }

    private static bool BuildHealthPrefab()
    {
        GameObject root = CreateCanvasRoot("Rouge F2 Main Tower Health", 260, false,
            out CanvasGroup canvasGroup);
        try
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            RougeF2MainTowerHealthView view = root.AddComponent<RougeF2MainTowerHealthView>();
            view.canvasGroup = canvasGroup;

            Image panel = CreateImage("Health Panel", root.transform,
                new Color(0.006f, 0.04f, 0.075f, 0.95f), false);
            Anchor(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 22f), new Vector2(650f, 96f), new Vector2(0.5f, 0f));
            AddOutline(panel, CyanLine, 1.2f);
            AddEdgeLines(panel.rectTransform, 2f, CyanLine);

            Text modeLabel = CreateText("Mode Label", panel.transform,
                "F2 观测  //  MAIN TOWER CORE", 13, TextSecondary,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            TopStretch(modeLabel.rectTransform, 22f, 10f, 22f, 20f);

            Image track = CreateImage("Health Track", panel.transform, TrackColor, false);
            Stretch(track.rectTransform, 22f, 15f, 22f, 39f);
            AddOutline(track, new Color(CyanLine.r, CyanLine.g, CyanLine.b, 0.5f), 1f);

            Image fill = CreateImage("Health Fill", track.transform, Cyan, false);
            Stretch(fill.rectTransform, 2f, 2f, 2f, 2f);
            view.healthFill = fill;

            Text healthText = CreateText("Health Text", track.transform,
                "主塔核心  500 / 500", 17, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(healthText.rectTransform, 10f, 0f, 10f, 0f);
            view.healthText = healthText;

            return SavePrefab(root, HealthPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static bool BuildCameraToastPrefab()
    {
        GameObject root = CreateCanvasRoot("Rouge Camera Mode Toast", 420, false,
            out CanvasGroup canvasGroup);
        try
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            RougeCameraModeToast view = root.AddComponent<RougeCameraModeToast>();
            view.canvasGroup = canvasGroup;

            Image panel = CreateImage("Mode Panel", root.transform,
                new Color(0.004f, 0.035f, 0.060f, 0.94f), false);
            Anchor(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -76f), new Vector2(420f, 72f), new Vector2(0.5f, 1f));
            AddOutline(panel, new Color(0.08f, 0.82f, 1f, 0.86f), 1.3f);
            AddEdgeLines(panel.rectTransform, 2f, CyanLine);
            view.panel = panel;

            Text label = CreateText("Mode Label", panel.transform, "默认镜头", 27,
                TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform, 18f, 6f, 18f, 6f);
            view.label = label;

            return SavePrefab(root, CameraToastPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject CreateCanvasRoot(string name, int sortingOrder,
        bool receivesEvents, out CanvasGroup canvasGroup)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup));
        root.layer = 5;
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        if (receivesEvents) root.AddComponent<GraphicRaycaster>();
        canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = receivesEvents;
        canvasGroup.blocksRaycasts = receivesEvents;
        return root;
    }

    private static GameObject CreatePage(string name, Transform parent)
    {
        RectTransform rect = CreateRect(name, parent);
        Stretch(rect, 0f, 0f, 0f, 0f);
        return rect.gameObject;
    }

    private static void CreatePageHeading(Transform parent, string titleText,
        string subtitleText)
    {
        Text title = CreateText("Page Title", parent, titleText, 27,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
        TopStretch(title.rectTransform, 26f, 18f, 26f, 36f);
        Text subtitle = CreateText("Page Subtitle", parent, subtitleText, 13,
            TextSecondary, TextAnchor.MiddleRight, FontStyle.Normal);
        TopStretch(subtitle.rectTransform, 240f, 24f, 26f, 24f);
        Image line = CreateImage("Heading Line", parent, CyanLine, false);
        TopStretch(line.rectTransform, 26f, 65f, 26f, 1.5f);
    }

    private static void CreateSectionLabel(Transform parent, string value, float top)
    {
        Text label = CreateText(value + " Label", parent, value, 16,
            TextSecondary, TextAnchor.MiddleLeft, FontStyle.Bold);
        TopStretch(label.rectTransform, 26f, top, 26f, 26f);
    }

    private static Button[] CreateSegmentButtons(Transform parent, string name,
        float top, string[] labels, float spacing = 10f)
    {
        RectTransform group = CreateRect(name + " Segments", parent);
        TopStretch(group, 26f, top, 26f, 58f);
        HorizontalLayoutGroup layout = group.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Button[] result = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            Button button = CreateButton($"{name} {labels[i]}", group, labels[i],
                labels.Length > 5 ? 16 : 18);
            LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = 52f;
            result[i] = button;
        }
        return result;
    }

    private static Slider CreateSliderRow(Transform parent, string objectName,
        string title, string description, float top, float minimum, float maximum,
        bool wholeNumbers, out Text valueText)
    {
        Image row = CreateRowPanel(objectName, parent, top, 122f);
        Text titleText = CreateText("Title", row.transform, title, 20,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
        TopStretch(titleText.rectTransform, 20f, 12f, 180f, 28f);
        valueText = CreateText("Value", row.transform,
            wholeNumbers ? maximum.ToString("0") : maximum.ToString("0.00"), 20,
            Cyan, TextAnchor.MiddleRight, FontStyle.Bold);
        Anchor(valueText.rectTransform, Vector2.one, Vector2.one,
            new Vector2(-20f, -27f), new Vector2(150f, 28f), Vector2.one);
        Text descriptionText = CreateText("Description", row.transform, description, 13,
            TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal);
        TopStretch(descriptionText.rectTransform, 20f, 39f, 180f, 22f);

        Slider slider = CreateSlider("Slider", row.transform, minimum, maximum, wholeNumbers);
        Stretch(slider.GetComponent<RectTransform>(), 20f, 14f, 20f, 70f);
        return slider;
    }

    private static Image CreateRowPanel(string name, Transform parent, float top, float height)
    {
        Image row = CreateImage(name + " Row", parent, RowBlue, false);
        TopStretch(row.rectTransform, 26f, top, 26f, height);
        AddOutline(row, new Color(CyanLine.r, CyanLine.g, CyanLine.b, 0.38f), 1f);
        return row;
    }

    private static Slider CreateSlider(string name, Transform parent, float minimum,
        float maximum, bool wholeNumbers)
    {
        RectTransform root = CreateRect(name, parent);
        Slider slider = root.gameObject.AddComponent<Slider>();
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = wholeNumbers;
        slider.value = maximum;
        slider.direction = Slider.Direction.LeftToRight;

        Image background = CreateImage("Background", root, TrackColor, false);
        background.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        background.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        background.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        background.rectTransform.anchoredPosition = Vector2.zero;
        background.rectTransform.sizeDelta = new Vector2(0f, 8f);

        RectTransform fillArea = CreateRect("Fill Area", root);
        Stretch(fillArea, 9f, 8f, 9f, 8f);
        Image fill = CreateImage("Fill", fillArea, Cyan, false);
        Stretch(fill.rectTransform, 0f, 0f, 0f, 0f);

        RectTransform handleArea = CreateRect("Handle Slide Area", root);
        Stretch(handleArea, 10f, 0f, 10f, 0f);
        Image handle = CreateImage("Handle", handleArea, TextPrimary, true);
        Anchor(handle.rectTransform, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 26f));
        AddOutline(handle, Cyan, 1f);

        // The transparent full-width graphic both makes the entire track clickable and
        // supplies a dark overlay whenever Selectable.interactable is false.
        Image interactionOverlay = CreateImage("Interaction Overlay", root, Color.white, true);
        Stretch(interactionOverlay.rectTransform, 0f, 0f, 0f, 0f);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = interactionOverlay;
        ColorBlock colors = slider.colors;
        colors.normalColor = Color.clear;
        colors.highlightedColor = new Color(0.08f, 0.72f, 0.92f, 0.06f);
        colors.pressedColor = new Color(0.08f, 0.72f, 0.92f, 0.12f);
        colors.selectedColor = Color.clear;
        colors.disabledColor = new Color(0.002f, 0.01f, 0.018f, 0.72f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        slider.colors = colors;
        slider.navigation = Navigation.defaultNavigation;
        return slider;
    }

    private static Button CreateButton(string name, Transform parent, string label,
        int fontSize)
    {
        Image image = CreateImage(name, parent, ButtonBlue, true);
        AddOutline(image, new Color(CyanLine.r, CyanLine.g, CyanLine.b, 0.64f), 1f);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.78f, 0.96f, 1f, 1f);
        colors.pressedColor = new Color(0.45f, 0.82f, 0.94f, 1f);
        colors.selectedColor = new Color(0.72f, 0.94f, 1f, 1f);
        colors.disabledColor = new Color(0.36f, 0.42f, 0.46f, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.navigation = Navigation.defaultNavigation;

        Text text = CreateText("Label", image.transform, label, fontSize,
            TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(text.rectTransform, 10f, 5f, 10f, 5f);
        return button;
    }

    private static Image CreateImage(string name, Transform parent, Color color,
        bool raycastTarget)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        image.type = Image.Type.Simple;
        return image;
    }

    private static Text CreateText(string name, Transform parent, string value,
        int fontSize, Color color, TextAnchor alignment, FontStyle style)
    {
        RectTransform rect = CreateRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = GetEditorFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = 5;
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        if (parent != null) rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Font GetEditorFont()
    {
        if (s_editorFont == null)
            s_editorFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return s_editorFont;
    }

    private static void AddOutline(Graphic graphic, Color color, float distance)
    {
        Outline outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, distance);
        outline.useGraphicAlpha = false;
    }

    private static void AddEdgeLines(RectTransform parent, float thickness, Color color)
    {
        Image top = CreateImage("Top Accent", parent, color, false);
        TopStretch(top.rectTransform, 0f, 0f, 0f, thickness);
        Image bottom = CreateImage("Bottom Accent", parent, color, false);
        BottomStretch(bottom.rectTransform, 0f, 0f, 0f, thickness);
    }

    private static void AddBottomLine(RectTransform parent, float thickness, Color color)
    {
        Image line = CreateImage("Bottom Line", parent, color, false);
        BottomStretch(line.rectTransform, 0f, 0f, 0f, thickness);
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

    private static void TopStretch(RectTransform rect, float left, float top,
        float right, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2((left - right) * 0.5f, -top);
        rect.sizeDelta = new Vector2(-(left + right), height);
    }

    private static void BottomStretch(RectTransform rect, float left, float bottom,
        float right, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2((left - right) * 0.5f, bottom);
        rect.sizeDelta = new Vector2(-(left + right), height);
    }

    private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta, Vector2? pivot = null)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    private static bool SavePrefab(GameObject root, string path)
    {
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        if (saved != null) return true;
        Debug.LogError("[Rouge UI] Failed to save prefab: " + path);
        return false;
    }

    private static bool AssetExists(string assetPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null) return true;
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot)) return false;
        string relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
        return File.Exists(Path.Combine(projectRoot, relative));
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
