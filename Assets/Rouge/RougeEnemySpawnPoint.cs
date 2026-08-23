using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public enum RougeEnemyType
{
    [InspectorName("普通")]
    Standard = 0,
    [InspectorName("迅捷")]
    Swift = 1,
    [InspectorName("重装")]
    Heavy = 2
}

[DisallowMultipleComponent]
public sealed class RougeEnemySpawnPoint : MonoBehaviour
{
    private const float SpawnWarningLeadTime = 3f;
    private const float SpawnWarningUiSize = 96f;
    private const float SpawnWarningCellCoverage = 0.82f;

    [Header("Wave")]
    [Range(1, 64)] public int spawnCount = 25;
    [Min(0.1f)] public float spawnInterval = 5f;
    [Min(0f)] public float startDelay = 1f;
    [Tooltip("Remove this spawn point after it has produced the configured number of waves.")]
    public bool limitWaveCount;
    [Min(1)] public int maximumWaves = 1;

    [Header("Enemy")]
    public RougeEnemyType enemyType = RougeEnemyType.Standard;
    [HideInInspector, Min(0)] public int enemyTypeIndex;

    [Header("Spawn Cell")]
    [Min(0.1f)] public float spawnCellSize = 8f;

    [System.NonSerialized] internal float timer;
    [System.NonSerialized] internal int waveIndex;
    private bool _warningEnabledForCountdown;
    private GameObject _warningCanvasObject;
    private Canvas _warningCanvas;
    private RectTransform _warningVisual;
    private CanvasGroup _warningCanvasGroup;
    private Material _warningOverlayMaterial;

    public int GetEnemyTypeIndex()
    {
        return (int)enemyType;
    }

    public void ConfigureFromMap(int count, float interval, float delay, float cellSize,
        RougeEnemyType type, bool limitWaves, int maxWaves)
    {
        spawnCount = Mathf.Clamp(count, 1, 64);
        spawnInterval = Mathf.Max(0.1f, interval);
        startDelay = Mathf.Max(0f, delay);
        spawnCellSize = Mathf.Max(0.1f, cellSize);
        enemyType = type;
        limitWaveCount = limitWaves;
        maximumWaves = Mathf.Max(1, maxWaves);
        timer = startDelay;
        waveIndex = 0;
        _warningEnabledForCountdown = startDelay >= SpawnWarningLeadTime &&
                                      spawnInterval >= SpawnWarningLeadTime;
        HideSpawnWarning();
    }

    private void OnEnable()
    {
        timer = Mathf.Max(0f, startDelay);
        waveIndex = 0;
        _warningEnabledForCountdown = startDelay >= SpawnWarningLeadTime &&
                                      spawnInterval >= SpawnWarningLeadTime;
        HideSpawnWarning();
    }

    internal void ResetWaves()
    {
        timer = Mathf.Max(0f, startDelay);
        waveIndex = 0;
        _warningEnabledForCountdown = startDelay >= SpawnWarningLeadTime &&
                                      spawnInterval >= SpawnWarningLeadTime;
        HideSpawnWarning();
    }

    internal void CompleteWave(float spawnSpeedMultiplier)
    {
        float nextInterval = Mathf.Max(0.05f,
            spawnInterval / Mathf.Max(0.01f, spawnSpeedMultiplier));
        timer += nextInterval;
        _warningEnabledForCountdown = nextInterval >= SpawnWarningLeadTime;
        waveIndex++;
    }

    internal void UpdateSpawnWarning()
    {
        bool visible = _warningEnabledForCountdown && timer > 0f && timer <= SpawnWarningLeadTime;
        if (!visible)
        {
            HideSpawnWarning();
            return;
        }

        EnsureSpawnWarningVisual();
        if (_warningVisual == null || _warningCanvasGroup == null) return;
        if (!UpdateSpawnWarningGroundTransform())
        {
            HideSpawnWarning();
            return;
        }
        if (!_warningVisual.gameObject.activeSelf) _warningVisual.gameObject.SetActive(true);

        float elapsed = SpawnWarningLeadTime - timer;
        float pulse = 0.5f + Mathf.Sin(elapsed * Mathf.PI * 4f) * 0.5f;
        _warningCanvasGroup.alpha = Mathf.Lerp(0.72f, 1f, pulse);
        _warningVisual.localScale = Vector3.one * Mathf.Lerp(0.97f, 1.04f, pulse);
    }

    internal void HideSpawnWarning()
    {
        if (_warningVisual != null) _warningVisual.gameObject.SetActive(false);
    }

    private void EnsureSpawnWarningVisual()
    {
        if (_warningVisual != null) return;

        _warningCanvasObject = new GameObject("Enemy Spawn Warning Canvas",
            typeof(RectTransform), typeof(Canvas));
        _warningCanvas = _warningCanvasObject.GetComponent<Canvas>();
        _warningCanvas.renderMode = RenderMode.WorldSpace;
        _warningCanvas.overrideSorting = true;
        _warningCanvas.sortingOrder = 32760;
        RectTransform canvasRect = _warningCanvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(SpawnWarningUiSize, SpawnWarningUiSize);

        GameObject root = new GameObject("Enemy Spawn Warning",
            typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(_warningCanvasObject.transform, false);
        _warningVisual = root.GetComponent<RectTransform>();
        _warningVisual.anchorMin = new Vector2(0.5f, 0.5f);
        _warningVisual.anchorMax = new Vector2(0.5f, 0.5f);
        _warningVisual.pivot = new Vector2(0.5f, 0.5f);
        _warningVisual.anchoredPosition = Vector2.zero;
        _warningVisual.sizeDelta = new Vector2(SpawnWarningUiSize, SpawnWarningUiSize);
        _warningCanvasGroup = root.GetComponent<CanvasGroup>();
        _warningCanvasGroup.blocksRaycasts = false;
        _warningCanvasGroup.interactable = false;

        Color warningColor = new Color(1f, 0.055f, 0.035f, 1f);
        CreateWarningCorner(root.transform, "Top Left", new Vector2(0f, 1f),
            new Vector2(0f, 1f), warningColor);
        CreateWarningCorner(root.transform, "Top Right", new Vector2(1f, 1f),
            new Vector2(1f, 1f), warningColor);
        CreateWarningCorner(root.transform, "Bottom Left", new Vector2(0f, 0f),
            new Vector2(0f, 0f), warningColor);
        CreateWarningCorner(root.transform, "Bottom Right", new Vector2(1f, 0f),
            new Vector2(1f, 0f), warningColor);

        GameObject labelObject = new GameObject("Exclamation", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(root.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(58f, 72f);
        labelRect.anchoredPosition = new Vector2(0f, 1f);

        Text label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = "!";
        label.fontSize = 62;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = warningColor;
        label.raycastTarget = false;
        Outline outline = labelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.12f, 0f, 0f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        Shader uiShader = Shader.Find("UI/Default");
        if (uiShader != null)
        {
            _warningOverlayMaterial = new Material(uiShader)
            {
                name = "Enemy Spawn Warning Overlay Material",
                renderQueue = 5000
            };
            // World-space UI normally participates in depth testing, so a dense enemy
            // group can still cover it even with the maximum Canvas sorting order.
            _warningOverlayMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++) graphics[i].material = _warningOverlayMaterial;
        }

        root.SetActive(false);
    }

    private bool UpdateSpawnWarningGroundTransform()
    {
        Camera gameplayCamera = RougeCameraFollow.ResolveCamera();
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (gameplayCamera == null || _warningCanvasObject == null || _warningVisual == null)
            return false;

        Vector3 worldCenter = transform.position + Vector3.up * 0.12f;
        Vector3 viewportCenter = gameplayCamera.WorldToViewportPoint(worldCenter);
        if (viewportCenter.z <= 0f || viewportCenter.x < -0.15f || viewportCenter.x > 1.15f ||
            viewportCenter.y < -0.15f || viewportCenter.y > 1.15f)
            return false;

        if (_warningCanvas != null) _warningCanvas.worldCamera = gameplayCamera;
        Transform warningTransform = _warningCanvasObject.transform;
        warningTransform.position = worldCenter;
        // Canvas local X/Y becomes world X/Z, so the complete marker lies on the map
        // and receives exactly the same oblique perspective as the spawn tile.
        warningTransform.rotation = Quaternion.Euler(90f, 0f, 0f);
        float worldScale = Mathf.Max(0.1f, spawnCellSize) * SpawnWarningCellCoverage /
            SpawnWarningUiSize;
        warningTransform.localScale = Vector3.one * worldScale;
        return true;
    }

    private static void CreateWarningCorner(Transform parent, string name,
        Vector2 anchor, Vector2 pivot, Color color)
    {
        CreateWarningLine(parent, name + " Horizontal", anchor, pivot,
            new Vector2(25f, 4f), color);
        CreateWarningLine(parent, name + " Vertical", anchor, pivot,
            new Vector2(4f, 25f), color);
    }

    private static void CreateWarningLine(Transform parent, string name,
        Vector2 anchor, Vector2 pivot, Vector2 size, Color color)
    {
        GameObject lineObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        lineObject.transform.SetParent(parent, false);
        RectTransform lineRect = lineObject.GetComponent<RectTransform>();
        lineRect.anchorMin = anchor;
        lineRect.anchorMax = anchor;
        lineRect.pivot = pivot;
        lineRect.anchoredPosition = Vector2.zero;
        lineRect.sizeDelta = size;
        Image line = lineObject.GetComponent<Image>();
        line.color = color;
        line.raycastTarget = false;
        Shadow shadow = lineObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.12f, 0f, 0f, 0.92f);
        shadow.effectDistance = new Vector2(2f, -2f);
        shadow.useGraphicAlpha = true;
    }

    private void OnDisable()
    {
        HideSpawnWarning();
    }

    private void OnDestroy()
    {
        if (_warningCanvasObject != null) Destroy(_warningCanvasObject);
        if (_warningOverlayMaterial != null) Destroy(_warningOverlayMaterial);
        _warningCanvasObject = null;
        _warningCanvas = null;
        _warningVisual = null;
        _warningCanvasGroup = null;
        _warningOverlayMaterial = null;
    }

    internal bool HasReachedWaveLimit()
    {
        return limitWaveCount && waveIndex >= Mathf.Max(1, maximumWaves);
    }

    private void OnValidate()
    {
        spawnCount = Mathf.Clamp(spawnCount, 1, 64);
        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        startDelay = Mathf.Max(0f, startDelay);
        maximumWaves = Mathf.Max(1, maximumWaves);
        enemyTypeIndex = Mathf.Max(0, enemyTypeIndex);
        spawnCellSize = Mathf.Max(0.1f, spawnCellSize);
    }

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;
        Vector3 center = transform.position + Vector3.up * 0.08f;
        Gizmos.color = new Color(1f, 0.12f, 0.08f, 0.16f);
        Gizmos.DrawCube(center, new Vector3(spawnCellSize, 0.1f, spawnCellSize));
        Gizmos.color = new Color(1f, 0.24f, 0.12f, 0.95f);
        Gizmos.DrawWireCube(center, new Vector3(spawnCellSize, 0.1f, spawnCellSize));
#if UNITY_EDITOR
        UnityEditor.Handles.color = new Color(1f, 0.35f, 0.12f, 1f);
        UnityEditor.Handles.Label(center + Vector3.up * 3f,
            $"Enemy Spawn / {enemyType}\n" +
            $"Fixed {spawnCount} every {spawnInterval:0.##}s (before JSON speed curve)\n" +
            (limitWaveCount ? $"Maximum {maximumWaves} waves\n" : "Unlimited waves\n") +
            $"Cell {spawnCellSize:0.#} × {spawnCellSize:0.#}");
#endif
    }
}
