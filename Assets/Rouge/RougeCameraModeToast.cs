using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RougeCameraModeToast : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/RougeCameraModeToast";
    private const float HoldDuration = 0.72f;
    private const float FadeDuration = 0.42f;

    private static RougeCameraModeToast s_instance;
    private static bool s_missingPrefabLogged;

    [Header("Prefab References")]
    public CanvasGroup canvasGroup;
    public Image panel;
    public Text label;

    private float _remaining;

    public static void Prewarm(Font sharedFont)
    {
        if (sharedFont != null)
        {
            const string cameraModeGlyphs = "默认镜头自由移轴观赏垂直俯视";
            sharedFont.RequestCharactersInTexture(cameraModeGlyphs, 27, FontStyle.Bold);
        }
        ResolveOrCreate(sharedFont);
    }

    public static void Show(string value, Color accent)
    {
        RougeCameraModeToast toast = ResolveOrCreate(null);
        if (toast == null || toast.label == null || toast.panel == null ||
            toast.canvasGroup == null) return;

        toast.label.text = value;
        toast.label.color = Color.Lerp(accent, Color.white, 0.42f);
        toast.panel.color = new Color(0.004f, 0.035f, 0.060f, 0.94f);
        Outline outline = toast.panel.GetComponent<Outline>();
        if (outline != null)
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.86f);
        toast._remaining = HoldDuration + FadeDuration;
        toast.canvasGroup.alpha = 1f;
    }

    private static RougeCameraModeToast ResolveOrCreate(Font preferredFont)
    {
        if (s_instance != null)
        {
            s_instance.ApplyFont(preferredFont);
            return s_instance;
        }

        s_instance = FindFirstObjectByType<RougeCameraModeToast>();
        if (s_instance != null)
        {
            s_instance.CacheReferences();
            s_instance.ApplyFont(preferredFont);
            return s_instance;
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            if (!s_missingPrefabLogged)
            {
                s_missingPrefabLogged = true;
                Debug.LogError(
                    "[Rouge UI] Missing editable camera mode toast prefab at " +
                    "Assets/Rouge/Resources/UI/RougeCameraModeToast.prefab.");
            }
            return null;
        }

        GameObject instance = Instantiate(prefab);
        instance.name = prefab.name;
        RougeCameraModeToast toast = instance.GetComponent<RougeCameraModeToast>();
        if (toast == null)
        {
            Debug.LogError("[Rouge UI] Camera mode toast prefab has no RougeCameraModeToast component.",
                instance);
            Destroy(instance);
            return null;
        }

        toast.CacheReferences();
        toast.ApplyFont(preferredFont);
        s_instance = toast;
        return toast;
    }

    private void Awake()
    {
        s_instance = this;
        CacheReferences();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    private void Update()
    {
        if (canvasGroup == null || _remaining <= 0f) return;
        _remaining = Mathf.Max(0f, _remaining - Time.unscaledDeltaTime);
        canvasGroup.alpha = _remaining > FadeDuration
            ? 1f
            : Mathf.Clamp01(_remaining / FadeDuration);
    }

    private void CacheReferences()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (panel == null)
        {
            Transform panelTransform = transform.Find("Mode Panel");
            if (panelTransform != null) panel = panelTransform.GetComponent<Image>();
        }
        if (label == null)
        {
            Transform labelTransform = transform.Find("Mode Panel/Mode Label");
            if (labelTransform != null) label = labelTransform.GetComponent<Text>();
        }
    }

    private void ApplyFont(Font preferredFont)
    {
        if (preferredFont != null && label != null) label.font = preferredFont;
    }
}
