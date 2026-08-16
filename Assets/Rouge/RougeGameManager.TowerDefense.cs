using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private const byte EliteEnemyFlag = 0x40;
    private const byte BossEnemyFlag = 0x80;
    private const byte EnemyArchetypeMask = 0x3F;
    private const int LegacyTowerDefenseStartingGold = 500;
    private const int DefaultTowerDefenseStartingGold = 2000;
    private static readonly string[] TowerPrefabResourcePaths =
    {
        "Prefab/tower/Ice",
        "Prefab/tower/MachineGun",
        "Prefab/tower/Cannon",
        "Prefab/tower/Flame",
        "Prefab/tower/Laser",
        "Prefab/tower/PiercingLaser",
        "Prefab/tower/OrbitSphere"
    };
    public static bool TowerDefenseBuildModeActive { get; private set; }

    [Header("Tower Defense")]
    [SerializeField] private bool towerDefenseEnabled = true;
    [SerializeField, Min(0)] private int towerDefenseStartingGold = DefaultTowerDefenseStartingGold;
    [SerializeField] private RougeMainTower mainTower;
    [SerializeField] private RougeTowerBalanceConfig towerBalance = new RougeTowerBalanceConfig();
    [SerializeField] private RougeEnemyBalanceConfig enemyBalance = new RougeEnemyBalanceConfig();
    [SerializeField] private RougeBossBalanceConfig bossBalance = new RougeBossBalanceConfig();
    [SerializeField] private RougeTacticalSkillBalanceConfig tacticalSkillBalance = new RougeTacticalSkillBalanceConfig();
    [SerializeField] private RougeBossSpawnPoint bossSpawnPoint;

    private readonly List<RougeEnemySpawnPoint> _towerDefenseSpawners = new List<RougeEnemySpawnPoint>();
    private readonly List<RougeDefenseTower> _defenseTowers = new List<RougeDefenseTower>();
    private readonly List<TowerProjectile> _towerProjectiles = new List<TowerProjectile>();
    private readonly List<TowerFireZone> _towerFireZones = new List<TowerFireZone>();
    private readonly List<TowerBeamVisual> _towerBeamVisuals = new List<TowerBeamVisual>();
    private readonly List<ActiveOrbitSphereAttack> _activeOrbitSphereAttacks = new List<ActiveOrbitSphereAttack>();
    private readonly Stack<GameObject> _towerProjectileVisualPool = new Stack<GameObject>();
    private readonly int[] _towerTargetIndices = new int[FindTowerTargetsJob.MaxTargetsPerTower];
    private readonly float[] _towerTargetDistances = new float[FindTowerTargetsJob.MaxTargetsPerTower];
    private readonly Vector3[] _towerTargetPositions = new Vector3[FindTowerTargetsJob.MaxTargetsPerTower];
    private bool _towerDefenseInitialized;
    private bool _towerPlacementMode;
    private bool _towerBuildSelectionActive = true;
    private bool _towerDefenseGameOver;
    private string _towerDefenseGameOverReason;
    private int _towerDefenseGold;
    private int _towerDefenseAliveEstimate;
    private float _towerDefenseSpawnerResolveRetryTimer;
    private int _towerDefenseSpawnSearchCursor;
    private RougeTowerType _selectedBuildType = RougeTowerType.Ice;
    private RougeDefenseTower _towerPreview;
    private RougeDefenseTower _selectedTower;
    private bool _previewValid;
    private bool _pendingMainTowerAoe;
    private Canvas _towerDefenseCanvas;
    private Text _towerDefenseStatusText;
    private Text _towerDefenseModeText;
    private Text _towerDefenseGameOverText;
    private Image _mainTowerHealthFill;
    private Button _towerUpgradeButton;
    private Text _towerUpgradeButtonText;
    private Button _towerSellButton;
    private Text _towerSellButtonText;
    private Button _towerTargetPriorityButton;
    private Text _towerTargetPriorityButtonText;
    private Text _towerDamageRankingText;
    private readonly Button[] _towerBuildButtons = new Button[TowerDefenseVisuals.TowerTypeCount];
    private readonly Text[] _towerBuildButtonTexts = new Text[TowerDefenseVisuals.TowerTypeCount];
    private readonly int[] _towerDamageRankOrder = { 0, 1, 2, 3, 4, 5, 6 };
    private float _nextTowerDefenseUiRefreshTime;
    private Image _bossHealthFill;
    private Text _bossStatusText;
    private GameObject _bossPanel;
    private readonly Image[] _bossThresholdMarkers = new Image[3];
    private readonly Text[] _bossThresholdLabels = new Text[3];
    private LineRenderer _bossInterferenceRing;
    private LineRenderer _bossShieldRing;
    private LineRenderer _bossHasteRing;
    private float _bossInterferencePulseTimer;
    private float _bossShieldPulseTimer;
    private float _bossHastePulseTimer;
    private float _bossCurrentHealth;
    private int _bossEnemyIndex = -1;
    private bool _bossSpawned;
    private bool _bossDefeated;
    private bool _bossInterferenceActive;
    private bool _bossShieldActive;
    private bool _bossHasteActive;
    private Vector3 _bossWorldPosition;
    private RougeBossSpriteAnimator _bossSpriteAnimator;
    private bool _bossDeathSequenceActive;
    private float _bossDeathSequenceTimer;
    private int _bossDeathShockwaveStep;
    private bool _bossDeathExplosionTriggered;
    private bool _towerDefenseVictory;
    private bool _towerDefensePlayerWasActive;
    private bool _towerDefenseHudWasActive;

    private struct TowerProjectile
    {
        public GameObject Visual;
        public RougeTowerType Type;
        public Vector3 Start;
        public Vector3 End;
        public float Elapsed;
        public float Duration;
        public float ArcHeight;
        public float Damage;
        public float Radius;
        public float EffectDuration;
        public float TickInterval;
        public int TargetIndex;
    }

    private struct TowerFireZone
    {
        public Vector3 Position;
        public float Radius;
        public float Remaining;
        public float DamagePerTick;
        public float TickInterval;
        public float TickTimer;
        public GameObject Visual;
    }

    private struct TowerBeamVisual
    {
        public GameObject Visual;
        public float Remaining;
    }

    private sealed class ActiveOrbitSphereAttack
    {
        public RougeDefenseTower Tower;
        public Vector3[] Positions;
        public float Distance;
        public float AngleDegrees;
        public float DamageTimer;
        public float OuterHoldRemaining;
        public bool Returning;
    }
    private readonly Matrix4x4[] _orbitSphereRenderMatrices = new Matrix4x4[1023];
    private Material _orbitSphereRenderMaterial;

    private void InitializeTowerDefense()
    {
        if (!towerDefenseEnabled || _towerDefenseInitialized) return;

        _towerDefenseInitialized = true;
        if (RougeTowerDefenseBalanceJson.TryLoad(out RougeTowerDefenseBalanceJsonData jsonBalance))
        {
            towerBalance = jsonBalance.towerBalance;
            enemyBalance = jsonBalance.enemyBalance;
            bossBalance = jsonBalance.bossBalance;
            tacticalSkillBalance = jsonBalance.tacticalSkillBalance;
        }
        towerBalance ??= new RougeTowerBalanceConfig();
        enemyBalance ??= new RougeEnemyBalanceConfig();
        bossBalance ??= new RougeBossBalanceConfig();
        tacticalSkillBalance ??= new RougeTacticalSkillBalanceConfig();
        towerBalance.EnsureDefaults();
        enemyBalance.EnsureDefaults();
        bossBalance.EnsureDefaults();
        tacticalSkillBalance.EnsureDefaults();
        ApplyEnemySpriteSheetTextures();
        TowerDefenseVisuals.SetRuntimeBalance(towerBalance);
        _towerDefenseGold = Mathf.Max(0, towerDefenseStartingGold);
        _towerDefenseAliveEstimate = 0;
        _towerDefenseSpawnSearchCursor = 0;
        _towerDefenseGameOver = false;
        _towerDefenseGameOverReason = string.Empty;
        _towerPlacementMode = false;
        TowerDefenseBuildModeActive = false;
        _towerBuildSelectionActive = true;
        _pendingMainTowerAoe = false;
        Time.timeScale = 1f;
        InitializeTacticalSkills();
        _bossEnemyIndex = -1;
        _bossSpawned = false;
        _bossDefeated = false;
        _bossInterferenceActive = false;
        _bossShieldActive = false;
        _bossHasteActive = false;
        _bossCurrentHealth = 0f;
        _bossDeathSequenceActive = false;
        _bossDeathSequenceTimer = 0f;
        _bossDeathShockwaveStep = 0;
        _bossDeathExplosionTriggered = false;
        _towerDefenseVictory = false;

        if (bossSpawnPoint == null) bossSpawnPoint = UnityEngine.Object.FindFirstObjectByType<RougeBossSpawnPoint>();
        if (player != null)
        {
            _towerDefensePlayerWasActive = player.gameObject.activeSelf;
            player.gameObject.SetActive(false);
        }
        if (_uiText != null)
        {
            _towerDefenseHudWasActive = _uiText.gameObject.activeSelf;
            _uiText.gameObject.SetActive(false);
        }
        RougeCameraFollow cameraFollow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (cameraFollow != null) cameraFollow.SetTowerDefensePan(true);

        AssignNamedTowerPlaceLayers();
        ResolveMainTower();
        ResolveEnemySpawnPoints();
        ResolveExistingDefenseTowers();
        PrepareTowerTargetRequests();
        BuildTowerDefenseUi();
        RefreshTowerDefenseUi();
    }

    private void EnsureTowerDefenseInitialized()
    {
        if (towerDefenseEnabled && !_towerDefenseInitialized) InitializeTowerDefense();
    }

    private void PrepareTowerDefenseSceneBeforeNavigation()
    {
        if (!towerDefenseEnabled) return;
        AssignNamedTowerPlaceLayers();
        if (mainTower == null) mainTower = UnityEngine.Object.FindFirstObjectByType<RougeMainTower>();
    }

    private void DisposeTowerDefense()
    {
        if (!_towerDefenseInitialized) return;

        DisableBossTowerInterferenceMarkers();
        DestroyBossPhaseVisuals();
        if (_bossSpriteAnimator != null) Destroy(_bossSpriteAnimator.gameObject);
        _bossSpriteAnimator = null;
        RougeCameraFollow disposingCamera = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (disposingCamera != null) disposingCamera.EndCinematicFocus();
        Time.timeScale = 1f;
        TowerDefenseBuildModeActive = false;
        if (player != null)
        {
            player.gameObject.SetActive(_towerDefensePlayerWasActive);
            player.SuppressMovement = false;
        }
        if (_uiText != null) _uiText.gameObject.SetActive(_towerDefenseHudWasActive);
        RougeCameraFollow cameraFollow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (cameraFollow != null) cameraFollow.SetTowerDefensePan(false);
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        DisposeTacticalSkills();

        for (int i = 0; i < _towerProjectiles.Count; i++)
        {
            if (_towerProjectiles[i].Visual != null) Destroy(_towerProjectiles[i].Visual);
        }
        _towerProjectiles.Clear();
        while (_towerProjectileVisualPool.Count > 0)
        {
            GameObject pooled = _towerProjectileVisualPool.Pop();
            if (pooled != null) Destroy(pooled);
        }
        for (int i = 0; i < _towerFireZones.Count; i++)
        {
            if (_towerFireZones[i].Visual != null) Destroy(_towerFireZones[i].Visual);
        }
        _towerFireZones.Clear();
        for (int i = 0; i < _towerBeamVisuals.Count; i++)
        {
            if (_towerBeamVisuals[i].Visual != null) Destroy(_towerBeamVisuals[i].Visual);
        }
        _towerBeamVisuals.Clear();
        for (int i = 0; i < _activeOrbitSphereAttacks.Count; i++)
            _activeOrbitSphereAttacks[i].Positions = null;
        _activeOrbitSphereAttacks.Clear();
        if (_towerDefenseCanvas != null) Destroy(_towerDefenseCanvas.gameObject);
        _towerDefenseCanvas = null;
        _towerTargetRequestCount = 0;
        _towerTargetScheduledCount = 0;
        _towerDefenseInitialized = false;
    }

    private void EnsureTowerDefenseConfigDefaults()
    {
        if (towerDefenseStartingGold == LegacyTowerDefenseStartingGold)
            towerDefenseStartingGold = DefaultTowerDefenseStartingGold;
        towerBalance ??= new RougeTowerBalanceConfig();
        enemyBalance ??= new RougeEnemyBalanceConfig();
        bossBalance ??= new RougeBossBalanceConfig();
        tacticalSkillBalance ??= new RougeTacticalSkillBalanceConfig();
        towerBalance.EnsureDefaults();
        enemyBalance.EnsureDefaults();
        bossBalance.EnsureDefaults();
        tacticalSkillBalance.EnsureDefaults();
    }

    private void AssignNamedTowerPlaceLayers()
    {
        int towerPlaceLayer = LayerMask.NameToLayer("TowerPlace");
        if (towerPlaceLayer < 0) return;

        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            GameObject go = transforms[i].gameObject;
            if (go.name.StartsWith("towerPlace", System.StringComparison.OrdinalIgnoreCase))
            {
                go.layer = towerPlaceLayer;
            }
        }
    }

    private void ResolveMainTower()
    {
        if (mainTower == null) mainTower = UnityEngine.Object.FindFirstObjectByType<RougeMainTower>();
        if (mainTower == null)
        {
            Debug.LogError("Tower Defense: scene contains no RougeMainTower. Code will not create or alter the main tower.", this);
            return;
        }

        mainTower.ResetHealth();
    }

    private void ResolveEnemySpawnPoints()
    {
        _towerDefenseSpawners.Clear();
        Scene activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        RougeEnemySpawnPoint[] found = UnityEngine.Object.FindObjectsByType<RougeEnemySpawnPoint>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            RougeEnemySpawnPoint point = found[i];
            if (point == null || point.gameObject.scene != activeScene) continue;
            _towerDefenseSpawners.Add(point);
        }

        if (_towerDefenseSpawners.Count == 0)
        {
            Debug.LogError("Tower Defense: active scene contains no RougeEnemySpawnPoint. Enemy spawning is disabled; no fallback spawn point will be created.", this);
            _towerDefenseSpawnerResolveRetryTimer = 1f;
            return;
        }

        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            _towerDefenseSpawners[i].ResetWaves();
        }
        _towerDefenseSpawnerResolveRetryTimer = 0f;
        Debug.Log($"Tower Defense: found {_towerDefenseSpawners.Count} scene spawn points.", this);
    }

    private void ResolveExistingDefenseTowers()
    {
        _defenseTowers.Clear();
        RougeDefenseTower[] towers = UnityEngine.Object.FindObjectsByType<RougeDefenseTower>(FindObjectsSortMode.None);
        for (int i = 0; i < towers.Length; i++)
        {
            if (towers[i] == null) continue;
            towers[i].Ensure2DVisual();
            _defenseTowers.Add(towers[i]);
        }
    }

    private bool UsesTowerDefenseSpawners()
    {
        // A missing scene spawn point must never fall back to survival-mode spawning.
        return towerDefenseEnabled && _towerDefenseInitialized;
    }

    private bool HasLivingMainTower()
    {
        return towerDefenseEnabled && mainTower != null && !mainTower.IsDestroyed;
    }

    private bool IsMainTowerDestroyed()
    {
        return towerDefenseEnabled && mainTower != null && mainTower.IsDestroyed;
    }

    private float GetMainTowerContactRadius()
    {
        return mainTower != null ? mainTower.contactRadius : 0f;
    }

    private float2 GetEnemyTowerDefenseGoal(float2 fallback)
    {
        if (!UsesTowerDefenseSpawners() || mainTower == null) return fallback;
        Vector3 p = mainTower.transform.position;
        return new float2(p.x, p.z);
    }

    private float2 GetEnemyTowerDefenseSpawnCenter(float2 fallback)
    {
        if (!UsesTowerDefenseSpawners()) return fallback;
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint point = _towerDefenseSpawners[i];
            if (point == null || !point.isActiveAndEnabled) continue;
            Vector3 p = point.transform.position;
            return new float2(p.x, p.z);
        }
        return fallback;
    }

    private void AddTowerDefenseGoldForKills(int kills)
    {
        if (!_towerDefenseInitialized || kills <= 0) return;
        if (_towerDefenseGoldEarned.IsCreated)
        {
            _towerDefenseGold += Mathf.Max(0, _towerDefenseGoldEarned[0]);
            _towerDefenseGoldEarned[0] = 0;
        }
        _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - kills);
        RefreshTowerDefenseUi();
    }

    private void RemoveTowerDefenseAliveEstimate(int count)
    {
        if (!_towerDefenseInitialized || count <= 0) return;
        _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - count);
    }

    private void ApplyMainTowerContactDamage()
    {
        if (!_towerDefenseInitialized || !_mainTowerDamageCount.IsCreated || mainTower == null) return;
        int contacts = _mainTowerDamageCount[0];
        if (contacts <= 0) return;
        _mainTowerDamageCount[0] = 0;
        _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - contacts);
        if (mainTower.ApplyEnemyContacts(contacts)) _pendingMainTowerAoe = true;
        RefreshTowerDefenseUi();
    }

    private bool IsTowerDefenseSimulationPaused()
    {
        return _towerDefenseInitialized && _towerDefenseGameOver;
    }

    private void UpdateTowerDefenseInput(float unscaledDt)
    {
        if (!_towerDefenseInitialized) return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (_towerDefenseGameOver)
        {
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
            return;
        }

        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
        {
            SetTowerPlacementMode(!_towerPlacementMode);
        }

        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (UpdateTacticalSkillInput(mouse, pointerOverUi)) return;
        if (!_towerPlacementMode)
        {
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !pointerOverUi)
            {
                RougeDefenseTower hovered = RaycastDefenseTower();
                if (hovered != null) EnterTowerEditMode(hovered);
            }
            return;
        }

        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            SetTowerPlacementMode(false);
            return;
        }

        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Ice);
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.MachineGun);
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Cannon);
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Flame);
            if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Laser);
            if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.PiercingLaser);
            if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.OrbitSphere);
            if (keyboard.uKey.wasPressedThisFrame) TryUpgradeSelectedTower();
            if (keyboard.escapeKey.wasPressedThisFrame) CancelTowerBuildSelection();
        }

        UpdateTowerPreview();
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame && !pointerOverUi)
        {
            // A green preview is authoritative. Raycasting towers first could select a
            // tower that happened to sit higher along the same perspective ray even
            // though the ground position itself was clear.
            if (_towerPreview != null && _previewValid)
            {
                PlacePreviewTower();
            }
            else
            {
                RougeDefenseTower hovered = RaycastDefenseTower();
                if (hovered != null)
                {
                    CancelTowerBuildSelection();
                    SelectPlacedTower(hovered);
                }
            }
        }
    }

    private void SetTowerPlacementMode(bool enabled)
    {
        _towerPlacementMode = enabled;
        TowerDefenseBuildModeActive = enabled;
        Time.timeScale = enabled ? 0.5f : 1f;

        if (enabled)
        {
            if (_towerBuildSelectionActive) SelectTowerBuildType(_selectedBuildType);
        }
        else
        {
            ClearTacticalSkillSelection();
            if (_towerPreview != null) Destroy(_towerPreview.gameObject);
            _towerPreview = null;
            SelectPlacedTower(null);
        }
        RefreshTowerDefenseUi();
    }

    private void EnterTowerEditMode(RougeDefenseTower tower)
    {
        if (tower == null) return;
        _towerBuildSelectionActive = false;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        SetTowerPlacementMode(true);
        SelectPlacedTower(tower);
    }

    private void SelectTowerBuildType(RougeTowerType type)
    {
        _selectedBuildType = type;
        _towerBuildSelectionActive = true;
        if (!_towerPlacementMode) return;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        GameObject go = InstantiateTowerPrefab(type);
        if (go == null)
        {
            _towerPreview = null;
            _towerBuildSelectionActive = false;
            RefreshTowerDefenseUi();
            return;
        }
        go.name = "Tower Preview - " + TowerDefenseVisuals.GetTowerName(type);
        _towerPreview = go.GetComponent<RougeDefenseTower>();
        _towerPreview.Configure(type, true);
        SelectPlacedTower(null);
        RefreshTowerDefenseUi();
    }

    private static GameObject InstantiateTowerPrefab(RougeTowerType type)
    {
        int typeIndex = (int)type;
        if (typeIndex < 0 || typeIndex >= TowerPrefabResourcePaths.Length)
        {
            Debug.LogError($"No tower prefab path is configured for tower type {type}.");
            return null;
        }

        string resourcePath = TowerPrefabResourcePaths[typeIndex];
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogError($"Tower prefab missing at Resources/{resourcePath}. Tower creation was cancelled.");
            return null;
        }

        RougeDefenseTower tower = prefab.GetComponent<RougeDefenseTower>();
        if (tower == null)
        {
            Debug.LogError($"Tower prefab at Resources/{resourcePath} is missing RougeDefenseTower. Tower creation was cancelled.");
            return null;
        }

        GameObject instance = Instantiate(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return instance;
    }

    private void BeginTowerBuild(RougeTowerType type)
    {
        TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out _, out int cost);
        if (_towerDefenseGameOver || _towerDefenseGold < cost) return;
        if (!_towerPlacementMode)
        {
            _towerBuildSelectionActive = false;
            SetTowerPlacementMode(true);
        }
        SelectTowerBuildType(type);
    }

    private void CancelTowerBuildSelection()
    {
        if (HasTacticalSkillSelection)
        {
            CancelTacticalSkillSelection(false);
            return;
        }
        _towerBuildSelectionActive = false;
        _previewValid = false;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        SelectPlacedTower(null);
        RefreshTowerDefenseUi();
    }

    private void UpdateTowerPreview()
    {
        if (_towerPreview == null) return;
        if (!TryRaycastTowerPlace(out RaycastHit hit))
        {
            _towerPreview.gameObject.SetActive(false);
            _previewValid = false;
            return;
        }

        _towerPreview.gameObject.SetActive(true);
        Vector3 position = hit.point;
        position.y += 0.05f;
        _towerPreview.transform.position = position;
        _previewValid = CanPlacePreviewTower();
        _towerPreview.SetPreviewState(_previewValid);
    }

    private bool CanPlacePreviewTower()
    {
        return _towerPreview != null && _towerPreview.gameObject.activeInHierarchy &&
            _towerDefenseGold >= _towerPreview.PurchaseCost &&
            IsTowerPositionClear(_towerPreview.transform.position, _towerPreview.PlacementRadius);
    }

    private bool TryRaycastTowerPlace(out RaycastHit hit)
    {
        hit = default;
        int layer = LayerMask.NameToLayer("TowerPlace");
        Camera camera = RougeCameraFollow.ResolveCamera();
        if (layer < 0 || camera == null || Mouse.current == null) return false;
        Vector2 pointer = Mouse.current.position.ReadValue();
        Ray ray = camera.ScreenPointToRay(pointer);
        return Physics.Raycast(ray, out hit, 3000f, 1 << layer, QueryTriggerInteraction.Collide);
    }

    private RougeDefenseTower RaycastDefenseTower()
    {
        Camera camera = RougeCameraFollow.ResolveCamera();
        if (camera == null || Mouse.current == null) return null;
        Vector2 pointer = Mouse.current.position.ReadValue();
        Ray ray = camera.ScreenPointToRay(pointer);
        RaycastHit[] hits = Physics.RaycastAll(ray, 3000f, ~0, QueryTriggerInteraction.Collide);
        float nearest = float.MaxValue;
        RougeDefenseTower result = null;
        for (int i = 0; i < hits.Length; i++)
        {
            RougeDefenseTower tower = hits[i].collider.GetComponentInParent<RougeDefenseTower>();
            if (tower == null || tower == _towerPreview || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            result = tower;
        }
        return result;
    }

    private bool IsTowerPositionClear(Vector3 position, float radius)
    {
        Vector2 candidate = new Vector2(position.x, position.z);
        for (int i = _defenseTowers.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null)
            {
                _defenseTowers.RemoveAt(i);
                continue;
            }
            Vector3 p = tower.transform.position;
            float minDistance = radius + tower.PlacementRadius;
            if ((candidate - new Vector2(p.x, p.z)).sqrMagnitude < minDistance * minDistance) return false;
        }

        if (mainTower != null)
        {
            Vector3 p = mainTower.transform.position;
            float minDistance = radius + mainTower.contactRadius;
            if ((candidate - new Vector2(p.x, p.z)).sqrMagnitude < minDistance * minDistance) return false;
        }
        return true;
    }

    private void PlacePreviewTower()
    {
        if (!CanPlacePreviewTower())
        {
            _previewValid = false;
            if (_towerPreview != null) _towerPreview.SetPreviewState(false);
            return;
        }
        int cost = _towerPreview.PurchaseCost;
        _towerDefenseGold -= cost;
        _towerPreview.name = _towerPreview.DisplayName + " Lv.1";
        _towerPreview.FinalizePlacement();
        _defenseTowers.Add(_towerPreview);
        RougeDefenseTower placed = _towerPreview;
        _towerPreview = null;
        _towerTargetScheduledCount = 0;
        SelectTowerBuildType(_selectedBuildType);
        SelectPlacedTower(placed);
        RefreshTowerDefenseUi();
    }

    private void SelectPlacedTower(RougeDefenseTower tower)
    {
        if (_selectedTower != null) _selectedTower.SetRangeVisibility(false);
        _selectedTower = tower;
        if (_selectedTower != null) _selectedTower.SetRangeVisibility(true);
        RefreshTowerDefenseUi();
    }

    private void DeleteTower(RougeDefenseTower tower)
    {
        if (tower == null) return;
        int refund = Mathf.FloorToInt(tower.InvestedGold * Mathf.Clamp01(towerBalance.sellRefundMultiplier));
        _towerDefenseGold += refund;
        _defenseTowers.Remove(tower);
        _towerTargetScheduledCount = 0;
        if (_selectedTower == tower) _selectedTower = null;
        Destroy(tower.gameObject);
        RefreshTowerDefenseUi();
    }

    private void SellSelectedTower()
    {
        if (_selectedTower == null) return;
        RougeDefenseTower tower = _selectedTower;
        DeleteTower(tower);
        SetTowerPlacementMode(false);
    }

    private void TryUpgradeSelectedTower()
    {
        if (_selectedTower == null || !_selectedTower.CanUpgrade) return;
        int cost = _selectedTower.UpgradeCost;
        if (_towerDefenseGold < cost) return;
        if (!_selectedTower.Upgrade()) return;
        _towerDefenseGold -= cost;
        _selectedTower.name = _selectedTower.DisplayName + " Lv." + _selectedTower.Level;
        _selectedTower.SetRangeVisibility(true);
        RefreshTowerDefenseUi();
    }

    private void ToggleSelectedTowerTargetPriority()
    {
        if (_selectedTower == null || !_selectedTower.IsTargetedDamage) return;
        _selectedTower.ToggleTargetPriority();
        // The current simulation may still be reading _towerTargetRequests.
        // Only change the managed tower state here; the normal next-frame update
        // completes that job before rebuilding the request array.
        _towerTargetScheduledCount = 0;
        RefreshTowerDefenseUi(true);
    }

    private void UpdateTowerDefenseSimulation(float dt)
    {
        if (!_towerDefenseInitialized || _towerDefenseGameOver) return;

        if (_bossDeathSequenceActive)
        {
            UpdateBossDeathSequence(Time.unscaledDeltaTime);
            RefreshTowerDefenseUi();
            return;
        }

        if (mainTower != null)
        {
            mainTower.aoeCooldownRemaining = Mathf.Max(0f, mainTower.aoeCooldownRemaining - dt);
        }

        UpdateTacticalSkills(dt);
        UpdateTowerDefenseBoss();
        UpdateTowerDefenseSpawners(dt);
        ApplyPendingMainTowerAoe();
        UpdateTowerFireZones(dt);
        UpdateTowerProjectiles(dt);
        UpdateTowerBeamVisuals(dt);
        UpdateOrbitSphereAttacks(dt);
        UpdateDefenseTowers(dt);
        PrepareTowerTargetRequests();
        RefreshTowerDefenseUi();
    }

    private void UpdateTowerDefenseBoss()
    {
        if (!_bossSpawned && !_bossDefeated && _survivalTime >= Mathf.Max(1f, bossBalance.spawnTimeSeconds))
        {
            TrySpawnTowerDefenseBoss();
        }

        if (!_bossSpawned || _bossEnemyIndex < 0 || _bossEnemyIndex >= _currentMaxEnemies ||
            _bossEnemyIndex >= _stateA.Length)
        {
            DisableBossTowerInterferenceMarkers();
            SetBossPhaseVisualsVisible(false);
            return;
        }

        float4 state = _stateA[_bossEnemyIndex];
        if (state.x <= 0f)
        {
            _bossCurrentHealth = 0f;
            DisableBossTowerInterferenceMarkers();
            SetBossPhaseVisualsVisible(false);
            BeginBossDeathSequence();
            return;
        }

        float4 position = _positionsA[_bossEnemyIndex];
        _bossWorldPosition = new Vector3(position.x, renderHeight, position.z);
        _bossCurrentHealth = Mathf.Max(0f, state.x);
        float healthRatio = Mathf.Clamp01(state.x / Mathf.Max(1f, bossBalance.maxHealth));
        bool activatedSkill = false;
        if (healthRatio <= 0.75f && !_bossInterferenceActive) { _bossInterferenceActive = true; activatedSkill = true; }
        if (healthRatio <= 0.50f && !_bossShieldActive) { _bossShieldActive = true; activatedSkill = true; }
        if (healthRatio <= 0.25f && !_bossHasteActive) { _bossHasteActive = true; activatedSkill = true; }
        if (activatedSkill && _bossSpriteAnimator != null)
            _bossSpriteAnimator.PlaySkill(bossBalance.skillAnimationDuration);
        state.z = GetTowerDefenseEnemySpeed(BossEnemyFlag);
        _stateA[_bossEnemyIndex] = state;
        if (_bossSpriteAnimator != null)
        {
            float4 velocity = _velocitiesA[_bossEnemyIndex];
            _bossSpriteAnimator.SetWorldState(
                _bossWorldPosition + Vector3.up * (Mathf.Max(0.5f, bossBalance.radius) * 1.55f),
                new Vector3(velocity.x, velocity.y, velocity.z));
        }
        UpdateBossPhaseVisuals(Time.deltaTime);
    }

    private void TrySpawnTowerDefenseBoss()
    {
        if (bossSpawnPoint == null)
        {
            bossSpawnPoint = UnityEngine.Object.FindFirstObjectByType<RougeBossSpawnPoint>();
        }

        int index = -1;
        for (int i = 0; i < _currentMaxEnemies; i++)
        {
            if (_stateA[i].x <= 0f && _positionsA[i].y < -100f) { index = i; break; }
        }
        if (index < 0 && _currentMaxEnemies < enemyCount) index = _currentMaxEnemies++;
        if (index < 0) return;

        Vector3 spawn = bossSpawnPoint != null
            ? bossSpawnPoint.transform.position
            : bossBalance.fallbackSpawnPosition;
        float radius = Mathf.Max(0.5f, bossBalance.radius);
        _positionsA[index] = new float4(spawn.x, renderHeight, spawn.z, radius);
        _velocitiesA[index] = float4.zero;
        _stateA[index] = new float4(Mathf.Max(1f, bossBalance.maxHealth), radius,
            Mathf.Max(0.1f, bossBalance.moveSpeed), 0f);
        _effectStateA[index] = default;
        _towerDefenseEnemyKinds[index] = BossEnemyFlag;
        // Kind 3 is clipped by the instanced enemy shader; the Boss is rendered by
        // its own billboard animator so it can play skills and split into shards.
        if (_enemyRenderKinds.IsCreated) _enemyRenderKinds[index] = 3;
        _bossEnemyIndex = index;
        _bossSpawned = true;
        _bossCurrentHealth = Mathf.Max(1f, bossBalance.maxHealth);
        if (_bossSpriteAnimator != null) Destroy(_bossSpriteAnimator.gameObject);
        _bossSpriteAnimator = RougeBossSpriteAnimator.Create(bossBalance, radius * 4.2f);
        if (_bossSpriteAnimator != null)
        {
            _bossSpriteAnimator.SetWorldState(
                new Vector3(spawn.x, renderHeight + radius * 1.55f, spawn.z), Vector3.zero);
        }
        _towerDefenseAliveEstimate++;
        RefreshTowerDefenseUi();
    }

    private void BeginBossDeathSequence()
    {
        if (_bossDeathSequenceActive || _towerDefenseVictory) return;
        _bossDeathSequenceActive = true;
        _bossDeathSequenceTimer = 0f;
        _bossDeathShockwaveStep = 0;
        _bossDeathExplosionTriggered = false;
        _bossSpawned = false;
        _bossDefeated = true;
        _towerPlacementMode = false;
        TowerDefenseBuildModeActive = false;
        if (_towerPreview != null) _towerPreview.gameObject.SetActive(false);
        if (_bossSpriteAnimator != null) _bossSpriteAnimator.BeginDeath();
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (follow != null) follow.BeginCinematicFocus(_bossWorldPosition);
    }

    private void UpdateBossDeathSequence(float unscaledDt)
    {
        _bossDeathSequenceTimer += Mathf.Max(0f, unscaledDt);
        float focusDuration = Mathf.Max(0.5f, bossBalance.deathFocusDuration);
        float preExplosionProgress = Mathf.Clamp01(_bossDeathSequenceTimer / 0.9f);
        if (_bossSpriteAnimator != null && !_bossDeathExplosionTriggered)
            _bossSpriteAnimator.SetDeathShake(preExplosionProgress);

        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (follow != null)
        {
            follow.BeginCinematicFocus(_bossWorldPosition);
            float shake = _bossDeathExplosionTriggered
                ? Mathf.Lerp(0.52f, 0.04f, Mathf.Clamp01((_bossDeathSequenceTimer - 0.9f) / 2f))
                : Mathf.Lerp(0.04f, 0.22f, preExplosionProgress);
            follow.SetCinematicShake(shake);
        }

        if (!_bossDeathExplosionTriggered && _bossDeathSequenceTimer >= 0.9f)
        {
            _bossDeathExplosionTriggered = true;
            if (_bossSpriteAnimator != null) _bossSpriteAnimator.ExplodeIntoShards(7.5f);
            SpawnExplosionVFX(_bossWorldPosition + Vector3.up * 2.5f, Mathf.Max(8f, bossBalance.radius * 2.4f));
            SpawnAOERing(_bossWorldPosition, Mathf.Max(12f, bossBalance.radius * 3f), 0.55f,
                new Color(1f, 0.5f, 0.08f, 1f));
        }

        while (_bossDeathShockwaveStep < 3 &&
               _bossDeathSequenceTimer >= 1.05f + _bossDeathShockwaveStep * 0.48f)
        {
            float radius = _bossDeathShockwaveStep == 0 ? 28f :
                _bossDeathShockwaveStep == 1 ? 62f : Mathf.Max(160f, arenaHalfExtent * 3f);
            EliminateEnemiesInsideBossShockwave(radius, _bossDeathShockwaveStep == 2);
            SpawnAOERing(_bossWorldPosition, radius, 0.62f,
                Color.Lerp(new Color(1f, 0.62f, 0.12f, 1f), new Color(0.22f, 0.86f, 1f, 1f),
                    _bossDeathShockwaveStep * 0.35f));
            _bossDeathShockwaveStep++;
        }

        if (_bossDeathSequenceTimer < focusDuration) return;
        EliminateEnemiesInsideBossShockwave(float.MaxValue, true);
        _bossDeathSequenceActive = false;
        TriggerTowerDefenseVictory();
    }

    private void EliminateEnemiesInsideBossShockwave(float radius, bool eliminateAll)
    {
        if (!_stateA.IsCreated || !_positionsA.IsCreated) return;
        float radiusSq = radius >= 100000f ? float.MaxValue : radius * radius;
        int removed = 0;
        int limit = Mathf.Min(_currentMaxEnemies, _stateA.Length);
        for (int i = 0; i < limit; i++)
        {
            if (i == _bossEnemyIndex) continue;
            float4 state = _stateA[i];
            if (state.x <= 0f) continue;
            float4 position = _positionsA[i];
            float dx = position.x - _bossWorldPosition.x;
            float dz = position.z - _bossWorldPosition.z;
            if (!eliminateAll && dx * dx + dz * dz > radiusSq) continue;
            state.x = 0f;
            state.w = 20.99f;
            position.y = -1000f;
            _stateA[i] = state;
            _positionsA[i] = position;
            if (_stateB.IsCreated && i < _stateB.Length) _stateB[i] = state;
            if (_positionsB.IsCreated && i < _positionsB.Length) _positionsB[i] = position;
            removed++;
        }
        _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - removed);
    }

    private void TriggerTowerDefenseVictory()
    {
        if (_towerDefenseGameOver) return;
        _towerDefenseVictory = true;
        _towerDefenseGameOver = true;
        _towerDefenseGameOverReason = "BOSS DESTROYED";
        Time.timeScale = 0f;
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (follow != null) follow.SetCinematicShake(0f);
        RefreshTowerDefenseUi(true);
    }

    private void DisableBossTowerInterferenceMarkers()
    {
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            if (_defenseTowers[i] != null) _defenseTowers[i].SetBossInterference(false, 1f);
        }
    }

    private void EnsureBossPhaseVisuals()
    {
        if (_bossInterferenceRing == null)
        {
            _bossInterferenceRing = TowerDefenseVisuals.CreateCircleRenderer("Boss Interference Radius", transform);
            _bossInterferenceRing.widthMultiplier = 0.48f;
            _bossInterferenceRing.sharedMaterial = GetTacticalIndicatorMaterial();
            _bossInterferenceRing.sortingOrder = 31990;
        }
        if (_bossShieldRing == null)
        {
            _bossShieldRing = TowerDefenseVisuals.CreateCircleRenderer("Boss Shield Radius", transform);
            _bossShieldRing.widthMultiplier = 0.62f;
            _bossShieldRing.sharedMaterial = GetTacticalIndicatorMaterial();
            _bossShieldRing.sortingOrder = 31991;
        }
        if (_bossHasteRing == null)
        {
            _bossHasteRing = TowerDefenseVisuals.CreateCircleRenderer("Boss Haste Aura", transform);
            _bossHasteRing.widthMultiplier = 0.42f;
            _bossHasteRing.sharedMaterial = GetTacticalIndicatorMaterial();
            _bossHasteRing.sortingOrder = 31992;
        }
    }

    private void UpdateBossPhaseVisuals(float dt)
    {
        EnsureBossPhaseVisuals();
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
        TowerDefenseVisuals.UpdateCircle(_bossInterferenceRing, _bossWorldPosition,
            bossBalance.interferenceRadius + pulse * 0.6f,
            new Color(1f, 0.08f, 0.75f, 0.9f), _bossInterferenceActive);
        TowerDefenseVisuals.UpdateCircle(_bossShieldRing, _bossWorldPosition,
            bossBalance.shieldRadius + pulse * 0.8f,
            new Color(0.08f, 0.78f, 1f, 0.95f), _bossShieldActive);
        TowerDefenseVisuals.UpdateCircle(_bossHasteRing, _bossWorldPosition,
            Mathf.Max(4f, bossBalance.radius * (1.55f + pulse * 0.18f)),
            new Color(1f, 0.58f, 0.05f, 1f), _bossHasteActive);

        _bossInterferencePulseTimer -= dt;
        _bossShieldPulseTimer -= dt;
        _bossHastePulseTimer -= dt;
        if (_bossInterferenceActive && _bossInterferencePulseTimer <= 0f)
        {
            _bossInterferencePulseTimer = 1.1f;
            SpawnAOERing(_bossWorldPosition + Vector3.up * 0.08f, bossBalance.interferenceRadius, 0.85f,
                new Color(1f, 0.08f, 0.75f, 1f));
        }
        if (_bossShieldActive && _bossShieldPulseTimer <= 0f)
        {
            _bossShieldPulseTimer = 0.75f;
            SpawnAOERing(_bossWorldPosition + Vector3.up * 0.12f, bossBalance.shieldRadius, 0.65f,
                new Color(0.08f, 0.78f, 1f, 1f));
        }
        if (_bossHasteActive && _bossHastePulseTimer <= 0f)
        {
            _bossHastePulseTimer = 0.32f;
            SpawnAOERing(_bossWorldPosition + Vector3.up * 0.16f, Mathf.Max(5f, bossBalance.radius * 2f), 0.28f,
                new Color(1f, 0.58f, 0.05f, 1f));
        }
    }

    private void SetBossPhaseVisualsVisible(bool visible)
    {
        if (_bossInterferenceRing != null) _bossInterferenceRing.enabled = visible && _bossInterferenceActive;
        if (_bossShieldRing != null) _bossShieldRing.enabled = visible && _bossShieldActive;
        if (_bossHasteRing != null) _bossHasteRing.enabled = visible && _bossHasteActive;
    }

    private void DestroyBossPhaseVisuals()
    {
        if (_bossInterferenceRing != null) Destroy(_bossInterferenceRing.gameObject);
        if (_bossShieldRing != null) Destroy(_bossShieldRing.gameObject);
        if (_bossHasteRing != null) Destroy(_bossHasteRing.gameObject);
        _bossInterferenceRing = null;
        _bossShieldRing = null;
        _bossHasteRing = null;
    }

    private void UpdateTowerDefenseSpawners(float dt)
    {
        if (_towerDefenseSpawners.Count == 0)
        {
            _towerDefenseSpawnerResolveRetryTimer -= dt;
            if (_towerDefenseSpawnerResolveRetryTimer <= 0f) ResolveEnemySpawnPoints();
            return;
        }

        for (int i = _towerDefenseSpawners.Count - 1; i >= 0; i--)
        {
            RougeEnemySpawnPoint point = _towerDefenseSpawners[i];
            if (point == null)
            {
                _towerDefenseSpawners.RemoveAt(i);
                continue;
            }
            if (!point.isActiveAndEnabled) continue;
            point.timer -= dt;
            if (point.timer > 0f) continue;
            SpawnEnemyBatch(point, point.GetCurrentWaveEnemyCount(_survivalTime));
            point.CompleteWave(_survivalTime);
        }
    }

    private void SpawnEnemyBatch(RougeEnemySpawnPoint point, int count)
    {
        if (!_positionsA.IsCreated || count <= 0) return;
        int spawned = 0;

        // Only scan old slots when the alive estimate says reusable holes may exist.
        // The previous implementation scanned every active slot for every spawner wave,
        // even while all enemies were alive (12 spawners made this a large main-thread cost).
        int existingCount = _currentMaxEnemies;
        if (_towerDefenseAliveEstimate < existingCount && existingCount > 0)
        {
            int checkedSlots = 0;
            while (checkedSlots < existingCount && spawned < count)
            {
                int index = (_towerDefenseSpawnSearchCursor + checkedSlots) % existingCount;
                checkedSlots++;
                if (_stateA[index].x > 0f || _positionsA[index].y > -100f) continue;
                if (ActivateEnemySlot(index, point)) spawned++;
            }
            _towerDefenseSpawnSearchCursor = (_towerDefenseSpawnSearchCursor + checkedSlots) % existingCount;
        }

        while (spawned < count && _currentMaxEnemies < enemyCount)
        {
            if (!ActivateEnemySlot(_currentMaxEnemies, point)) break;
            _currentMaxEnemies++;
            spawned++;
        }
        _towerDefenseAliveEstimate += spawned;
    }

    private bool ActivateEnemySlot(int index, RougeEnemySpawnPoint point)
    {
        byte kind = RollTowerDefenseEnemyKind(point);
        RougeEnemyArchetypeConfig archetype = GetEnemyArchetype(kind);
        bool elite = (kind & EliteEnemyFlag) != 0;
        float health = GetTowerDefenseEnemyHealth(kind);
        float baseRadius = Mathf.Min(enemyRadius * 2f, enemyRadius * (0.8f + currentLevel * 0.0001f));
        float radiusValue = baseRadius * Mathf.Max(0.1f, archetype.size) *
            (elite ? Mathf.Max(1f, enemyBalance.eliteSizeMultiplier) : 1f);
        if (!TryGetReachableEnemySpawnPosition(point, radiusValue, out float2 spawnPosition)) return false;
        float speed = GetTowerDefenseEnemySpeed(kind);
        _positionsA[index] = new float4(spawnPosition.x, renderHeight, spawnPosition.y, radiusValue);
        _velocitiesA[index] = float4.zero;
        _stateA[index] = new float4(health, radiusValue, speed, 0f);
        _effectStateA[index] = default;
        _towerDefenseEnemyKinds[index] = kind;
        if (_enemyRenderKinds.IsCreated) _enemyRenderKinds[index] = kind & EnemyArchetypeMask;
        return true;
    }

    private void ApplyEnemySpriteSheetTextures()
    {
        if (enemyMaterial == null) return;
        enemyBalance ??= new RougeEnemyBalanceConfig();
        enemyBalance.EnsureDefaults();
        Texture2D fallback = Resources.Load<Texture2D>("Sprites/enemy_robot");
        for (int i = 0; i < 3; i++)
        {
            RougeEnemyArchetypeConfig type = enemyBalance.enemyTypes[Mathf.Min(i,
                enemyBalance.enemyTypes.Count - 1)];
            Texture2D sheet = string.IsNullOrWhiteSpace(type.spriteResourcePath)
                ? null
                : Resources.Load<Texture2D>(type.spriteResourcePath);
            enemyMaterial.SetTexture("_EnemySheet" + i, sheet != null ? sheet : fallback);
        }
        enemyMaterial.SetTexture("_MainTex", fallback);
    }

    private bool TryGetReachableEnemySpawnPosition(RougeEnemySpawnPoint point, float enemyNavigationRadius,
        out float2 spawnPosition)
    {
        Vector3 worldCenter = point.transform.position;
        float2 center = new float2(worldCenter.x, worldCenter.z);
        float minimumRadiusSq = point.minimumRadius * point.minimumRadius;
        float maximumRadiusSq = point.spawnRadius * point.spawnRadius;
        float arenaLimit = Mathf.Max(1f, arenaHalfExtent - 2f);

        // Spawn volumes overlap walls in several parts of this map. Sampling only
        // reachable cells prevents enemies from being born inside a sealed pocket and
        // then pushing forever against its nearest corner.
        const int randomAttempts = 32;
        for (int attempt = 0; attempt < randomAttempts; attempt++)
        {
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Mathf.Lerp(minimumRadiusSq, maximumRadiusSq,
                UnityEngine.Random.value));
            float2 candidate = center + new float2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            candidate = math.clamp(candidate, new float2(-arenaLimit), new float2(arenaLimit));
            if (!IsReachableEnemySpawnPosition(candidate, enemyNavigationRadius)) continue;
            spawnPosition = candidate;
            return true;
        }

        // Use the nearest reachable cell center as a deterministic fallback. This is
        // only reached for badly overlapping spawn volumes, so it does not add work to
        // normal waves.
        if (_flowDistanceField.IsCreated && _flowFieldReady)
        {
            float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
            int2 origin = RougeMortonGridUtility.WorldToGrid(center, _flowGridOrigin,
                invCellSize, _flowGridDim);
            int maxRing = math.min(_flowGridDim - 1,
                math.max(8, Mathf.CeilToInt(point.spawnRadius * invCellSize) + 8));
            for (int ring = 0; ring <= maxRing; ring++)
            {
                int minX = math.max(0, origin.x - ring);
                int maxX = math.min(_flowGridDim - 1, origin.x + ring);
                int minY = math.max(0, origin.y - ring);
                int maxY = math.min(_flowGridDim - 1, origin.y + ring);
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (ring > 0 && x != minX && x != maxX && y != minY && y != maxY) continue;
                        float2 candidate = _flowGridOrigin +
                            new float2(x + 0.5f, y + 0.5f) * _flowFieldRuntimeCellSize;
                        if (!IsReachableEnemySpawnPosition(candidate, enemyNavigationRadius)) continue;
                        spawnPosition = candidate;
                        return true;
                    }
                }
            }
        }

        spawnPosition = default;
        return false;
    }

    private bool IsReachableEnemySpawnPosition(float2 position, float enemyNavigationRadius)
    {
        if (_flowDistanceField.IsCreated && _flowFieldReady)
        {
            float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
            int2 cell = RougeMortonGridUtility.WorldToGrid(position, _flowGridOrigin,
                invCellSize, _flowGridDim);
            float distance = _flowDistanceField[RougeMortonGridUtility.EncodeMorton(cell.x, cell.y)];
            if (!math.isfinite(distance) || distance >= 1e17f) return false;
        }

        if (!_obstacles.IsCreated) return true;
        for (int obstacleIndex = 0; obstacleIndex < _obstacleCount; obstacleIndex++)
        {
            RougeObstacle obstacle = _obstacles[obstacleIndex];
            float padding = Mathf.Max(0.1f, enemyNavigationRadius) + obstacle.Padding;
            float2 resolved = RougeObstacleMath.ResolvePointOutside(obstacle, position, padding);
            if (math.lengthsq(resolved - position) > 0.0001f) return false;
        }
        return true;
    }

    private int GetTowerDefenseEnemyPowerStep()
    {
        return Mathf.Max(0, Mathf.FloorToInt(_survivalTime / Mathf.Max(1f, enemyBalance.growthInterval)));
    }

    private int GetTowerDefenseEnemyLevel()
    {
        return GetTowerDefenseEnemyPowerStep() + 1;
    }

    private static float GetTowerDefenseEnemyLevelMultiplier(float growthMultiplier, int level)
    {
        return Mathf.Pow(Mathf.Max(1f, growthMultiplier), Mathf.Max(0, level - 1));
    }

    private float GetTowerDefenseEnemyHealth()
    {
        return Mathf.Max(1f, enemyBalance.enemyTypes[0].baseHealth) *
            GetTowerDefenseEnemyLevelMultiplier(enemyBalance.healthGrowthMultiplier, GetTowerDefenseEnemyLevel());
    }

    private float GetTowerDefenseEnemySpeed()
    {
        return Mathf.Max(0.1f, enemyBalance.enemyTypes[0].baseSpeed) *
            GetTowerDefenseEnemyLevelMultiplier(enemyBalance.speedGrowthMultiplier, GetTowerDefenseEnemyLevel());
    }

    private float GetTowerDefenseEnemyHealth(byte kind)
    {
        if ((kind & BossEnemyFlag) != 0) return Mathf.Max(1f, bossBalance.maxHealth);
        RougeEnemyArchetypeConfig archetype = GetEnemyArchetype(kind);
        float eliteMultiplier = (kind & EliteEnemyFlag) != 0 ? Mathf.Max(1f, enemyBalance.eliteHealthMultiplier) : 1f;
        float levelMultiplier = GetTowerDefenseEnemyLevelMultiplier(
            enemyBalance.healthGrowthMultiplier, GetTowerDefenseEnemyLevel());
        return Mathf.Max(0.01f, archetype.baseHealth) * levelMultiplier * eliteMultiplier;
    }

    private float GetTowerDefenseEnemySpeed(byte kind)
    {
        if ((kind & BossEnemyFlag) != 0)
        {
            return Mathf.Max(0.1f, bossBalance.moveSpeed) * (_bossHasteActive ? bossBalance.hasteSpeedMultiplier : 1f);
        }
        RougeEnemyArchetypeConfig archetype = GetEnemyArchetype(kind);
        float eliteMultiplier = (kind & EliteEnemyFlag) != 0 ? Mathf.Max(0.1f, enemyBalance.eliteSpeedMultiplier) : 1f;
        float levelMultiplier = GetTowerDefenseEnemyLevelMultiplier(
            enemyBalance.speedGrowthMultiplier, GetTowerDefenseEnemyLevel());
        return Mathf.Max(0.01f, archetype.baseSpeed) * levelMultiplier * eliteMultiplier;
    }

    private RougeEnemyArchetypeConfig GetEnemyArchetype(byte kind)
    {
        enemyBalance.EnsureDefaults();
        int index = Mathf.Clamp(kind & EnemyArchetypeMask, 0, enemyBalance.enemyTypes.Count - 1);
        return enemyBalance.enemyTypes[index];
    }

    private byte RollTowerDefenseEnemyKind(RougeEnemySpawnPoint point)
    {
        enemyBalance.EnsureDefaults();
        int availableTypeCount = Mathf.Min(enemyBalance.enemyTypes.Count, EnemyArchetypeMask + 1);
        int selected = point != null ? point.RollEnemyTypeIndex(availableTypeCount) : 0;
        byte kind = (byte)Mathf.Clamp(selected, 0, EnemyArchetypeMask);
        float eliteChance = point != null ? point.GetCurrentEliteChance01(_survivalTime) : 0f;
        if (UnityEngine.Random.value < eliteChance) kind |= EliteEnemyFlag;
        return kind;
    }

    private void ApplyPendingMainTowerAoe()
    {
        if (!_pendingMainTowerAoe || mainTower == null) return;
        _pendingMainTowerAoe = false;
        Vector3 p = mainTower.transform.position;
        TryAddSkillArea(new RougeSkillArea
        {
            Type = 13,
            Position = new float2(p.x, p.z),
            Radius = mainTower.hitAoeRadius,
            Damage = mainTower.hitAoeDamage,
            EffectFlags = (int)SkillHitEffectTag.Knockback,
            EffectKnockbackForce = mainTower.hitAoeKnockback
        });
        SpawnAOERing(new Vector3(p.x, renderHeight + 0.08f, p.z), mainTower.hitAoeRadius, 0.32f,
            new Color(0.2f, 0.85f, 1f, 1f));
    }

    private void UpdateDefenseTowers(float dt)
    {
        _towerLaserDamageFrame++;
        if (_towerLaserDamageFrame == 0) _towerLaserDamageFrame = 1;
        for (int i = _defenseTowers.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null)
            {
                _defenseTowers.RemoveAt(i);
                continue;
            }

            bool bossDebuffed = _bossSpawned && _bossInterferenceActive &&
                Vector2.SqrMagnitude(new Vector2(tower.transform.position.x - _bossWorldPosition.x,
                    tower.transform.position.z - _bossWorldPosition.z)) <=
                bossBalance.interferenceRadius * bossBalance.interferenceRadius;
            tower.SetBossInterference(bossDebuffed,
                bossDebuffed ? Mathf.Clamp(bossBalance.interferenceAttackSpeedMultiplier, 0.05f, 1f) : 1f);
            tower.UpdatePresentation(dt);

            if (tower.TowerType == RougeTowerType.Cannon && tower.cannonBurstShotsRemaining > 0)
            {
                UpdateCannonBurst(tower, dt);
                if (tower.cannonBurstShotsRemaining > 0) continue;
            }

            if (tower.TowerType == RougeTowerType.Laser)
            {
                UpdateContinuousLaserTower(tower, i, dt);
                continue;
            }

            if (tower.TowerType == RougeTowerType.OrbitSphere && IsOrbitSphereAttackActive(tower))
                continue;

            tower.HideLaserBeams();
            tower.attackTimer -= dt * tower.AttackSpeedMultiplier;
            if (tower.attackTimer > 0f) continue;

            if (!tower.IsTargetedDamage)
            {
                FireTower(tower, tower.transform.position);
                tower.attackTimer += tower.AttackInterval;
                continue;
            }

            if (tower.TowerType == RougeTowerType.MachineGun)
            {
                int catchUpShots = 0;
                do
                {
                    if (!FireMachineGunVolley(tower, i))
                    {
                        tower.attackTimer = Mathf.Min(0.05f, tower.AttackInterval);
                        break;
                    }
                    tower.attackTimer += tower.AttackInterval;
                    catchUpShots++;
                }
                while (tower.attackTimer <= 0f && catchUpShots < 4);
                continue;
            }

            if (!TryResolveTowerTarget(tower, i, out Vector3 targetPosition))
            {
                tower.attackTimer = Mathf.Min(0.15f, tower.AttackInterval);
                continue;
            }

            AimTowerAt(tower, targetPosition);
            FireTower(tower, targetPosition);
            tower.attackTimer += tower.AttackInterval;
        }
    }

    private void PrepareTowerTargetRequests()
    {
        if (!_towerTargetRequests.IsCreated)
        {
            _towerTargetRequestCount = 0;
            return;
        }

        int count = Mathf.Min(_defenseTowers.Count, MaxJobifiedTowerCount);
        for (int i = 0; i < count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null)
            {
                _towerTargetRequests[i] = default;
                continue;
            }

            Vector3 position = tower.transform.position;
            bool focusedBossLaser = tower.TowerType == RougeTowerType.Laser &&
                tower.TargetPriority == RougeTowerTargetPriority.BossFirst;
            _towerTargetRequests[i] = new RougeTowerTargetRequest
            {
                Position = new float2(position.x, position.z),
                Range = tower.AttackRange,
                TargetCount = focusedBossLaser || !tower.IsTargetedDamage
                    ? 1
                    : math.clamp(tower.TargetCount, 1, FindTowerTargetsJob.MaxTargetsPerTower),
                PriorityMode = tower.IsTargetedDamage
                    ? (int)tower.TargetPriority
                    : (int)RougeTowerTargetPriority.NearestToGoal
            };
        }
        _towerTargetRequestCount = count;
    }

    private int CollectTowerTargets(RougeDefenseTower tower, int towerListIndex, int requestedCount)
    {
        int capacity = Mathf.Min(requestedCount, FindTowerTargetsJob.MaxTargetsPerTower);
        for (int i = 0; i < capacity; i++)
        {
            _towerTargetIndices[i] = -1;
            _towerTargetDistances[i] = float.MaxValue;
            _towerTargetPositions[i] = default;
        }

        int found = 0;
        if (towerListIndex < 0 || towerListIndex >= _towerTargetScheduledCount ||
            !_towerTargetResultIndices.IsCreated)
        {
            return 0;
        }

        int resultStart = towerListIndex * FindTowerTargetsJob.MaxTargetsPerTower;
        Vector3 origin = tower.transform.position;
        float rangeSq = tower.AttackRange * tower.AttackRange;
        for (int result = 0; result < capacity; result++)
        {
            int enemyIndex = _towerTargetResultIndices[resultStart + result];
            if (!IsEnemyTargetValid(enemyIndex, origin, rangeSq, out Vector3 position)) continue;
            _towerTargetIndices[found] = enemyIndex;
            _towerTargetDistances[found] = _towerTargetResultDistances[resultStart + result];
            _towerTargetPositions[found] = position;
            found++;
        }
        return found;
    }

    private bool FireMachineGunVolley(RougeDefenseTower tower, int towerListIndex)
    {
        int count = CollectTowerTargets(tower, towerListIndex, tower.TargetCount);
        if (count <= 0) return false;
        tower.targetIndex = _towerTargetIndices[0];
        AimTowerAt(tower, _towerTargetPositions[0]);
        tower.PlayAttackAnimation(null);
        Vector3 start = GetTowerMuzzlePosition(tower);
        for (int i = 0; i < count; i++)
        {
            Vector3 target = _towerTargetPositions[i];
            Vector2 planar = new Vector2(target.x - start.x, target.z - start.z);
            float distance = Mathf.Max(0.1f, planar.magnitude);
            // The bullets fan out naturally because every pellet has its own enemy.
            // Keep only a tiny angular jitter so the snapshot projectile still lands
            // inside its impact radius at the doubled maximum range.
            float spreadDegrees = count <= 1 ? 0f : Mathf.Lerp(-0.65f, 0.65f, i / (float)(count - 1));
            float spreadRadians = spreadDegrees * Mathf.Deg2Rad;
            float2 direction = Rotate(new float2(planar.x / distance, planar.y / distance), spreadRadians);
            Vector3 spreadTarget = new Vector3(start.x + direction.x * distance, target.y, start.z + direction.y * distance);
            AccumulateTowerTargetDamage(tower.TowerType, _towerTargetIndices[i], tower.Damage);
            // Damage is target-index based and independent from the capped visual pool.
            // This keeps every pellet functional when hundreds of towers are firing.
            SpawnTowerProjectile(RougeTowerType.MachineGun, start, spreadTarget, 0f,
                1.1f, Mathf.Max(0.04f, distance / 70f), 0f, -1);
        }
        return true;
    }

    private void UpdateContinuousLaserTower(RougeDefenseTower tower, int towerListIndex, float dt)
    {
        bool focusedBossMode = tower.TargetPriority == RougeTowerTargetPriority.BossFirst;
        int requestedTargets = focusedBossMode ? 1 : tower.TargetCount;
        int count = CollectTowerTargets(tower, towerListIndex, requestedTargets);
        if (count <= 0)
        {
            tower.HideLaserBeams();
            return;
        }

        Vector3 start = GetTowerMuzzlePosition(tower);
        tower.targetIndex = _towerTargetIndices[0];
        AimTowerAt(tower, _towerTargetPositions[0]);
        if (focusedBossMode)
        {
            int beamCount = Mathf.Clamp(tower.TargetCount, 1, FindTowerTargetsJob.MaxTargetsPerTower);
            tower.ShowFocusedLaserBeams(start, _towerTargetPositions[0], beamCount);
            float perBeamDamage = Mathf.Max(1f, tower.Damage * 0.33f);
            AccumulateTowerTargetDamage(tower.TowerType, _towerTargetIndices[0],
                perBeamDamage * beamCount * dt * tower.AttackSpeedMultiplier);
            return;
        }

        tower.ShowLaserBeams(start, _towerTargetPositions, count);
        for (int i = 0; i < count; i++)
        {
            AccumulateTowerTargetDamage(tower.TowerType, _towerTargetIndices[i],
                tower.Damage * dt * tower.AttackSpeedMultiplier);
        }
    }

    private void AccumulateTowerTargetDamage(RougeTowerType towerType, int enemyIndex, float damage)
    {
        if (damage <= 0f || (uint)enemyIndex >= (uint)_towerLaserDamage.Length) return;
        if (_towerLaserDamageFrames[enemyIndex] != _towerLaserDamageFrame)
        {
            _towerLaserDamageFrames[enemyIndex] = _towerLaserDamageFrame;
            _towerLaserDamage[enemyIndex] = 0f;
        }
        _towerLaserDamage[enemyIndex] += damage;

        int typeIndex = Mathf.Clamp((int)towerType, 0, TowerDefenseVisuals.TowerTypeCount - 1);
        int entryIndex = enemyIndex * TowerDefenseVisuals.TowerTypeCount + typeIndex;
        if (_towerDamageByTypeFrames[entryIndex] != _towerLaserDamageFrame)
        {
            _towerDamageByTypeFrames[entryIndex] = _towerLaserDamageFrame;
            _towerDamageByType[entryIndex] = 0f;
        }
        _towerDamageByType[entryIndex] += damage;
    }

    private bool TryResolveTowerTarget(RougeDefenseTower tower, int towerListIndex, out Vector3 targetPosition)
    {
        targetPosition = default;
        float rangeSq = tower.AttackRange * tower.AttackRange;
        Vector3 origin = tower.transform.position;
        int resultStart = towerListIndex * FindTowerTargetsJob.MaxTargetsPerTower;
        if (towerListIndex >= 0 && towerListIndex < _towerTargetScheduledCount &&
            resultStart < _towerTargetResultIndices.Length)
        {
            int targetIndex = _towerTargetResultIndices[resultStart];
            if (IsEnemyTargetValid(targetIndex, origin, rangeSq, out targetPosition))
            {
                tower.targetIndex = targetIndex;
                return true;
            }
        }

        tower.targetIndex = -1;
        return false;
    }

    private bool IsEnemyTargetValid(int index, Vector3 origin, float rangeSq, out Vector3 position)
    {
        position = default;
        if (index < 0 || index >= _currentMaxEnemies || index >= _stateA.Length || _stateA[index].x <= 0f) return false;
        float4 p = _positionsA[index];
        float dx = p.x - origin.x;
        float dz = p.z - origin.z;
        if (dx * dx + dz * dz > rangeSq) return false;
        position = new Vector3(p.x, Mathf.Max(renderHeight + 0.8f, p.y + 0.8f), p.z);
        return true;
    }

    private static void AimTowerAt(RougeDefenseTower tower, Vector3 target)
    {
        tower.AimAt(target);
    }

    private void BeginCannonBurst(RougeDefenseTower tower, Vector3 target)
    {
        tower.cannonBurstTarget = target;
        tower.cannonBurstShotsRemaining = Mathf.Max(1, tower.ProjectileCount);
        tower.cannonBurstTimer = 0f;
        UpdateCannonBurst(tower, 0f);
    }

    private void UpdateCannonBurst(RougeDefenseTower tower, float dt)
    {
        if (tower == null || tower.cannonBurstShotsRemaining <= 0) return;
        tower.cannonBurstTimer -= dt * tower.AttackSpeedMultiplier;
        int catchUpShots = 0;
        while (tower.cannonBurstTimer <= 0f && tower.cannonBurstShotsRemaining > 0 && catchUpShots < 3)
        {
            Vector3 target = tower.cannonBurstTarget;
            tower.PlayAttackAnimation(() =>
            {
                if (tower == null) return;
                Vector3 start = GetTowerMuzzlePosition(tower);
                float distance = Vector2.Distance(new Vector2(start.x, start.z),
                    new Vector2(target.x, target.z));
                SpawnTowerProjectile(RougeTowerType.Cannon, start, target, tower.Damage,
                    tower.AoeRadius, Mathf.Clamp(distance / 38f, 0.12f, 0.65f), 0f, -1);
            });
            tower.cannonBurstShotsRemaining--;
            tower.cannonBurstTimer += 0.12f;
            catchUpShots++;
        }
    }

    private void FireTower(RougeDefenseTower tower, Vector3 target)
    {
        switch (tower.TowerType)
        {
            case RougeTowerType.Ice:
            {
                tower.PlayAttackAnimation(() =>
                {
                    if (tower == null) return;
                    Vector3 p = tower.transform.position;
                    TryAddSkillArea(new RougeSkillArea
                    {
                        Type = 13,
                        Position = new float2(p.x, p.z),
                        Radius = tower.AttackRange,
                        Damage = tower.Damage,
                        EffectFlags = (int)SkillHitEffectTag.Slow,
                        EffectSlowPercent = tower.EffectPercent,
                        EffectSlowDuration = tower.EffectDuration,
                        SourceTowerTypePlusOne = (int)tower.TowerType + 1
                    });
                    SpawnAOERing(new Vector3(p.x, renderHeight + 0.08f, p.z), tower.AttackRange, 0.45f,
                        new Color(0.2f, 0.85f, 1f, 1f));
                });
                break;
            }
            case RougeTowerType.Cannon:
                BeginCannonBurst(tower, target);
                break;
            case RougeTowerType.Flame:
            {
                int targetIndex = tower.targetIndex;
                tower.PlayAttackAnimation(() =>
                {
                    if (tower == null) return;
                    SpawnTowerProjectile(RougeTowerType.Flame, GetTowerMuzzlePosition(tower), target, tower.Damage,
                        tower.AoeRadius, 0.85f, 8f, targetIndex, tower.EffectDuration, tower.TickInterval);
                });
                break;
            }
            case RougeTowerType.PiercingLaser:
            {
                tower.PlayAttackAnimation(() =>
                {
                    if (tower == null) return;
                    Vector3 start = GetTowerMuzzlePosition(tower);
                    Vector2 direction2 = new Vector2(target.x - start.x, target.z - start.z).normalized;
                    float beamLength = tower.AttackRange * 2f;
                    const float beamWidth = 5f;
                    Vector3 end = start + new Vector3(direction2.x, 0f, direction2.y) * beamLength;
                    TryAddSkillArea(new RougeSkillArea
                    {
                        Type = 15,
                        Position = new float2(start.x, start.z),
                        Direction = new float2(direction2.x, direction2.y),
                        Length = beamLength,
                        Radius = beamWidth,
                        Damage = tower.Damage,
                        SourceTowerTypePlusOne = (int)tower.TowerType + 1
                    });
                    SpawnTowerBeam(start, end, beamWidth, 0.2f);
                });
                break;
            }
            case RougeTowerType.OrbitSphere:
                tower.PlayAttackAnimation(() => { StartOrbitSphereAttack(tower); });
                break;
        }
    }

    private bool StartOrbitSphereAttack(RougeDefenseTower tower)
    {
        if (tower == null) return false;
        for (int i = 0; i < _activeOrbitSphereAttacks.Count; i++)
        {
            if (_activeOrbitSphereAttacks[i].Tower == tower) return false;
        }

        int sphereCount = Mathf.Max(1, tower.ProjectileCount);
        ActiveOrbitSphereAttack attack = new ActiveOrbitSphereAttack
        {
            Tower = tower,
            Positions = new Vector3[Mathf.Clamp(sphereCount, 1, 64)],
            Distance = Mathf.Max(0.1f, tower.OrbitSphereRadius * 1.5f),
            AngleDegrees = 0f,
            DamageTimer = 0f,
            OuterHoldRemaining = Mathf.Max(0f, tower.OrbitOuterHoldDuration),
            Returning = false
        };
        for (int sphere = 0; sphere < attack.Positions.Length; sphere++)
        {
            float angle = sphere * (Mathf.PI * 2f / attack.Positions.Length);
            attack.Positions[sphere] = tower.transform.position + new Vector3(
                Mathf.Cos(angle) * attack.Distance, renderHeight + 0.22f,
                Mathf.Sin(angle) * attack.Distance);
        }
        _activeOrbitSphereAttacks.Add(attack);
        tower.attackTimer = float.PositiveInfinity;
        return true;
    }

    private bool IsOrbitSphereAttackActive(RougeDefenseTower tower)
    {
        for (int i = 0; i < _activeOrbitSphereAttacks.Count; i++)
        {
            if (_activeOrbitSphereAttacks[i].Tower == tower) return true;
        }
        return false;
    }

    private void UpdateOrbitSphereAttacks(float dt)
    {
        for (int attackIndex = _activeOrbitSphereAttacks.Count - 1; attackIndex >= 0; attackIndex--)
        {
            ActiveOrbitSphereAttack attack = _activeOrbitSphereAttacks[attackIndex];
            RougeDefenseTower tower = attack.Tower;
            if (tower == null)
            {
                _activeOrbitSphereAttacks.RemoveAt(attackIndex);
                continue;
            }

            float effectiveDt = dt * tower.AttackSpeedMultiplier;
            float sphereRadius = tower.OrbitSphereRadius;
            float minimumDistance = Mathf.Max(0.1f, sphereRadius * 1.5f);
            float maximumDistance = Mathf.Max(minimumDistance + 0.1f, tower.AttackRange);
            float radialStep = Mathf.Max(0.1f, tower.OrbitRadialSpeed) * effectiveDt;
            if (!attack.Returning)
            {
                if (attack.Distance < maximumDistance)
                {
                    attack.Distance = Mathf.Min(maximumDistance, attack.Distance + radialStep);
                }
                else if (attack.OuterHoldRemaining > 0f)
                {
                    attack.OuterHoldRemaining = Mathf.Max(0f, attack.OuterHoldRemaining - dt);
                    if (attack.OuterHoldRemaining <= 0f) attack.Returning = true;
                }
                else attack.Returning = true;
            }
            else
            {
                attack.Distance -= radialStep;
                if (attack.Distance <= minimumDistance)
                {
                    tower.attackTimer = tower.AttackInterval;
                    _activeOrbitSphereAttacks.RemoveAt(attackIndex);
                    continue;
                }
            }

            attack.AngleDegrees = Mathf.Repeat(attack.AngleDegrees +
                tower.OrbitAngularSpeed * effectiveDt, 360f);
            int sphereCount = attack.Positions != null ? attack.Positions.Length : 0;
            for (int sphere = 0; sphere < sphereCount; sphere++)
            {
                float angle = (attack.AngleDegrees + sphere * (360f / sphereCount)) * Mathf.Deg2Rad;
                attack.Positions[sphere] = tower.transform.position + new Vector3(
                    Mathf.Cos(angle) * attack.Distance, renderHeight + 0.22f,
                    Mathf.Sin(angle) * attack.Distance);
            }

            tower.ShowLaserBeams(tower.GetCrystalLaserOrigin(), attack.Positions, sphereCount);

            attack.DamageTimer -= effectiveDt;
            float damageInterval = Mathf.Max(0.02f, tower.TickInterval);
            while (attack.DamageTimer <= 0f)
            {
                for (int sphere = 0; sphere < sphereCount; sphere++)
                {
                    Vector3 position = attack.Positions[sphere];
                    TryAddSkillArea(new RougeSkillArea
                    {
                        Type = 13,
                        Position = new float2(position.x, position.z),
                        Radius = sphereRadius,
                        Damage = tower.Damage,
                        SourceTowerTypePlusOne = (int)RougeTowerType.OrbitSphere + 1
                    });
                }
                attack.DamageTimer += damageInterval;
            }
        }
    }

    private void RenderOrbitSphereVisuals()
    {
        // OrbitSphere attacks are now rendered as thin crystal lasers by the tower itself.
    }

    private static Vector3 GetTowerMuzzlePosition(RougeDefenseTower tower)
    {
        return tower.GetShootPosition();
    }

    private void SpawnTowerProjectile(RougeTowerType type, Vector3 start, Vector3 end, float damage, float radius,
        float duration, float arcHeight, int targetIndex, float effectDuration = 0f, float tickInterval = 0f,
        float visualScaleMultiplier = 1f)
    {
        if (_towerProjectiles.Count >= 512) return;
        GameObject visual = GetTowerProjectileVisual(type);
        visual.transform.localScale *= Mathf.Max(0.1f, visualScaleMultiplier);
        visual.transform.position = start;
        visual.SetActive(true);
        _towerProjectiles.Add(new TowerProjectile
        {
            Visual = visual,
            Type = type,
            Start = start,
            End = new Vector3(end.x, renderHeight + 0.2f, end.z),
            Elapsed = 0f,
            Duration = Mathf.Max(0.02f, duration),
            ArcHeight = arcHeight,
            Damage = damage,
            Radius = radius,
            EffectDuration = effectDuration,
            TickInterval = tickInterval,
            TargetIndex = targetIndex
        });
    }

    private GameObject GetTowerProjectileVisual(RougeTowerType type)
    {
        GameObject visual = null;
        while (_towerProjectileVisualPool.Count > 0 && visual == null) visual = _towerProjectileVisualPool.Pop();
        if (visual == null)
        {
            visual = new GameObject("Tower Projectile 2D");
            visual.transform.SetParent(transform, false);
            RougeBillboard projectileBillboard = visual.AddComponent<RougeBillboard>();
            SpriteRenderer projectileRenderer = RougeSpriteAssets.CreateRenderer(
                "Projectile Sprite",
                visual.transform,
                RougeSpriteAssets.Load("Sprites/projectile_energy"),
                Vector3.zero,
                1f,
                40,
                Color.white);
            projectileBillboard.SetRotatingContent(projectileRenderer.transform);
        }
        SpriteRenderer renderer = visual.GetComponentInChildren<SpriteRenderer>();
        if (renderer != null) renderer.color = TowerDefenseVisuals.GetTowerColor(type);
        float scale = type == RougeTowerType.MachineGun ? 0.28f : type == RougeTowerType.Cannon ? 1.05f : 0.72f;
        visual.transform.localScale = Vector3.one * scale;
        return visual;
    }

    private void UpdateTowerProjectiles(float dt)
    {
        for (int i = _towerProjectiles.Count - 1; i >= 0; i--)
        {
            TowerProjectile projectile = _towerProjectiles[i];
            projectile.Elapsed += dt;
            if (projectile.TargetIndex >= 0 && projectile.TargetIndex < _currentMaxEnemies &&
                projectile.TargetIndex < _stateA.Length && _stateA[projectile.TargetIndex].x > 0f)
            {
                float4 target = _positionsA[projectile.TargetIndex];
                projectile.End = new Vector3(target.x, renderHeight + 0.2f, target.z);
            }
            float t = Mathf.Clamp01(projectile.Elapsed / projectile.Duration);
            Vector3 position = Vector3.Lerp(projectile.Start, projectile.End, t);
            position.y += Mathf.Sin(t * Mathf.PI) * projectile.ArcHeight;
            if (projectile.Visual != null)
            {
                projectile.Visual.transform.position = position;
                RougeBillboard billboard = projectile.Visual.GetComponent<RougeBillboard>();
                if (billboard != null) billboard.SetWorldDirection(projectile.End - projectile.Start);
            }

            if (t < 1f)
            {
                _towerProjectiles[i] = projectile;
                continue;
            }

            ResolveTowerProjectileImpact(projectile);
            RecycleTowerProjectileVisual(projectile.Visual);
            int last = _towerProjectiles.Count - 1;
            _towerProjectiles[i] = _towerProjectiles[last];
            _towerProjectiles.RemoveAt(last);
        }
    }

    private void ResolveTowerProjectileImpact(TowerProjectile projectile)
    {
        float2 impact = new float2(projectile.End.x, projectile.End.z);
        if (projectile.Type == RougeTowerType.MachineGun && projectile.Damage <= 0f) return;
        if (projectile.Type == RougeTowerType.Flame)
        {
            AddTowerFireZone(projectile.End, projectile.Radius, projectile.EffectDuration,
                projectile.Damage, projectile.TickInterval);
            SpawnAOERing(projectile.End, projectile.Radius, 0.38f, new Color(1f, 0.24f, 0.04f, 1f));
            return;
        }

        TryAddSkillArea(new RougeSkillArea
        {
            Type = 13,
            Position = impact,
            Radius = projectile.Radius,
            Damage = projectile.Damage,
            SourceTowerTypePlusOne = (int)projectile.Type + 1
        });
        if (projectile.Type == RougeTowerType.Cannon)
        {
            SpawnExplosionVFX(projectile.End + Vector3.up * 0.4f, projectile.Radius * 0.75f);
            SpawnAOERing(projectile.End, projectile.Radius, 0.38f, new Color(1f, 0.42f, 0.08f, 1f));
        }
    }

    private void RecycleTowerProjectileVisual(GameObject visual)
    {
        if (visual == null) return;
        visual.SetActive(false);
        _towerProjectileVisualPool.Push(visual);
    }

    private void AddTowerFireZone(Vector3 position, float radius, float duration, float damagePerTick, float tickInterval)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Tower Fire Zone";
        visual.transform.position = new Vector3(position.x, renderHeight + 0.06f, position.z);
        visual.transform.localScale = new Vector3(radius * 2f, 0.06f, radius * 2f);
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null) renderer.material = TowerDefenseVisuals.CreateMaterial(new Color(1f, 0.12f, 0.02f, 0.5f));
        _towerFireZones.Add(new TowerFireZone
        {
            Position = position,
            Radius = radius,
            Remaining = duration,
            DamagePerTick = damagePerTick,
            TickInterval = Mathf.Max(0.01f, tickInterval),
            TickTimer = 0f,
            Visual = visual
        });
    }

    private void UpdateTowerFireZones(float dt)
    {
        for (int i = _towerFireZones.Count - 1; i >= 0; i--)
        {
            TowerFireZone zone = _towerFireZones[i];
            zone.Remaining -= dt;
            if (zone.Remaining <= 0f)
            {
                if (zone.Visual != null) Destroy(zone.Visual);
                _towerFireZones.RemoveAt(i);
                continue;
            }
            zone.TickTimer -= dt;
            int ticksThisFrame = 0;
            while (zone.TickTimer <= 0f && ticksThisFrame < 4)
            {
                TryAddSkillArea(new RougeSkillArea
                {
                    Type = 13,
                    Position = new float2(zone.Position.x, zone.Position.z),
                    Radius = zone.Radius,
                    Damage = zone.DamagePerTick,
                    SourceTowerTypePlusOne = (int)RougeTowerType.Flame + 1
                });
                zone.TickTimer += zone.TickInterval;
                ticksThisFrame++;
            }
            if (zone.Visual != null)
            {
                float pulse = 0.92f + Mathf.Sin(Time.time * 8f) * 0.08f;
                zone.Visual.transform.localScale = new Vector3(zone.Radius * 2f * pulse, 0.06f, zone.Radius * 2f * pulse);
            }
            _towerFireZones[i] = zone;
        }
    }

    private void SpawnTowerBeam(Vector3 start, Vector3 end, float width, float duration)
    {
        Vector3 direction = end - start;
        direction.y = 0f;
        float length = direction.magnitude;
        if (length <= 0.01f) return;
        direction /= length;
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Piercing Tower Laser";
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
        if (renderer != null && _laserMat != null) renderer.sharedMaterial = _laserMat;
        visual.transform.position = start + direction * (length * 0.5f);
        visual.transform.rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
        visual.transform.localScale = new Vector3(width, length * 0.5f, width * 0.4f);
        _towerBeamVisuals.Add(new TowerBeamVisual { Visual = visual, Remaining = duration });
    }

    private void UpdateTowerBeamVisuals(float dt)
    {
        for (int i = _towerBeamVisuals.Count - 1; i >= 0; i--)
        {
            TowerBeamVisual beam = _towerBeamVisuals[i];
            beam.Remaining -= dt;
            if (beam.Remaining <= 0f || beam.Visual == null)
            {
                if (beam.Visual != null) Destroy(beam.Visual);
                _towerBeamVisuals.RemoveAt(i);
            }
            else
            {
                _towerBeamVisuals[i] = beam;
            }
        }
    }

    private void RenderTowerDefensePausedFrame()
    {
        RenderBullets();
        RenderAOERings();
        RenderExplosions();
        RenderDeathBursts();
        RenderTornados();
        RefreshTowerDefenseUi();
    }

    private void TriggerTowerDefenseGameOver(string reason)
    {
        if (!_towerDefenseInitialized)
        {
            Dispose();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }
        if (_towerDefenseGameOver) return;
        _towerDefenseGameOver = true;
        _towerDefenseGameOverReason = reason;
        _towerPlacementMode = false;
        TowerDefenseBuildModeActive = false;
        Time.timeScale = 0f;
        if (player != null) player.SuppressMovement = true;
        if (_towerPreview != null) _towerPreview.gameObject.SetActive(false);
        RefreshTowerDefenseUi();
    }

    private void BuildTowerDefenseUi()
    {
        GameObject canvasObject = new GameObject("Tower Defense Canvas");
        _towerDefenseCanvas = canvasObject.AddComponent<Canvas>();
        _towerDefenseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _towerDefenseCanvas.sortingOrder = 50;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject statusPanel = CreateUiPanel("Status Panel", canvasObject.transform, new Color(0.025f, 0.04f, 0.07f, 0.88f));
        RectTransform statusRect = statusPanel.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(1f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(1f, 1f);
        statusRect.anchoredPosition = new Vector2(-24f, -24f);
        statusRect.sizeDelta = new Vector2(400f, 250f);

        _towerDefenseStatusText = CreateUiText("Status", statusPanel.transform, 22, TextAnchor.UpperLeft);
        StretchRect(_towerDefenseStatusText.rectTransform, 20f, 16f, 20f, 58f);
        Image hpBackground = CreateUiImage("HP Background", statusPanel.transform, new Color(0.08f, 0.1f, 0.14f, 1f));
        RectTransform hpRect = hpBackground.rectTransform;
        hpRect.anchorMin = new Vector2(0f, 0f);
        hpRect.anchorMax = new Vector2(1f, 0f);
        hpRect.pivot = new Vector2(0.5f, 0f);
        hpRect.anchoredPosition = new Vector2(0f, 24f);
        hpRect.sizeDelta = new Vector2(-44f, 28f);
        _mainTowerHealthFill = CreateUiImage("HP Fill", hpBackground.transform, new Color(0.12f, 0.78f, 1f, 1f));
        StretchRect(_mainTowerHealthFill.rectTransform, 3f, 3f, 3f, 3f);
        _mainTowerHealthFill.type = Image.Type.Simple;
        _mainTowerHealthFill.rectTransform.pivot = new Vector2(0f, 0.5f);

        GameObject damagePanel = CreateUiPanel("Tower Damage Ranking", canvasObject.transform,
            new Color(0.025f, 0.04f, 0.07f, 0.88f));
        RectTransform damageRect = damagePanel.GetComponent<RectTransform>();
        damageRect.anchorMin = new Vector2(0f, 1f);
        damageRect.anchorMax = new Vector2(0f, 1f);
        damageRect.pivot = new Vector2(0f, 1f);
        damageRect.anchoredPosition = new Vector2(24f, -24f);
        damageRect.sizeDelta = new Vector2(310f, 244f);
        _towerDamageRankingText = CreateUiText("Damage Ranking", damagePanel.transform, 19, TextAnchor.UpperLeft);
        StretchRect(_towerDamageRankingText.rectTransform, 18f, 14f, 18f, 14f);

        GameObject buildPanel = CreateUiPanel("Build Panel", canvasObject.transform, new Color(0.025f, 0.04f, 0.07f, 0.92f));
        RectTransform buildRect = buildPanel.GetComponent<RectTransform>();
        buildRect.anchorMin = new Vector2(0.5f, 0f);
        buildRect.anchorMax = new Vector2(0.5f, 0f);
        buildRect.pivot = new Vector2(0.5f, 0f);
        buildRect.anchoredPosition = new Vector2(0f, 24f);
        buildRect.sizeDelta = new Vector2(1500f, 252f);

        _towerDefenseModeText = CreateUiText("Mode", buildPanel.transform, 20, TextAnchor.UpperCenter);
        RectTransform modeRect = _towerDefenseModeText.rectTransform;
        modeRect.anchorMin = new Vector2(0f, 1f);
        modeRect.anchorMax = new Vector2(1f, 1f);
        modeRect.pivot = new Vector2(0.5f, 1f);
        modeRect.anchoredPosition = new Vector2(0f, -8f);
        modeRect.sizeDelta = new Vector2(-28f, 58f);

        Text buildGroupTitle = CreateUiText("Build Group Title", buildPanel.transform, 15, TextAnchor.MiddleCenter);
        SetBottomRect(buildGroupTitle.rectTransform, -315f, 164f, 900f, 20f);
        buildGroupTitle.text = "BUILD TOWERS";
        buildGroupTitle.color = new Color(0.58f, 0.72f, 0.86f, 1f);
        Text actionGroupTitle = CreateUiText("Tower Action Group Title", buildPanel.transform, 15, TextAnchor.MiddleCenter);
        SetBottomRect(actionGroupTitle.rectTransform, 390f, 164f, 420f, 20f);
        actionGroupTitle.text = "SELECTED TOWER";
        actionGroupTitle.color = new Color(0.58f, 0.72f, 0.86f, 1f);
        Image groupSeparator = CreateUiImage("Build Group Separator", buildPanel.transform,
            new Color(0.22f, 0.42f, 0.58f, 0.8f));
        SetBottomRect(groupSeparator.rectTransform, 150f, 16f, 2f, 144f);

        CreateBuildButton(buildPanel.transform, GetTowerBuildLabel(1, RougeTowerType.Ice), -630f, 92f, RougeTowerType.Ice, new Color(0.08f, 0.55f, 0.82f, 1f));
        CreateBuildButton(buildPanel.transform, GetTowerBuildLabel(2, RougeTowerType.MachineGun), -420f, 92f, RougeTowerType.MachineGun, new Color(0.72f, 0.62f, 0.08f, 1f));
        CreateBuildButton(buildPanel.transform, GetTowerBuildLabel(3, RougeTowerType.Cannon), -210f, 92f, RougeTowerType.Cannon, new Color(0.78f, 0.2f, 0.06f, 1f));
        CreateBuildButton(buildPanel.transform, GetTowerBuildLabel(7, RougeTowerType.OrbitSphere), 0f, 92f, RougeTowerType.OrbitSphere, new Color(0.18f, 0.46f, 0.9f, 1f));
        CreateBuildButton(buildPanel.transform, GetTowerBuildLabel(4, RougeTowerType.Flame), -630f, 18f, RougeTowerType.Flame, new Color(0.82f, 0.08f, 0.04f, 1f));
        CreateBuildButton(buildPanel.transform, GetTowerBuildLabel(5, RougeTowerType.Laser), -420f, 18f, RougeTowerType.Laser, new Color(0.08f, 0.65f, 0.35f, 1f));
        CreateBuildButton(buildPanel.transform, GetTowerBuildLabel(6, RougeTowerType.PiercingLaser), -210f, 18f, RougeTowerType.PiercingLaser, new Color(0.62f, 0.08f, 0.68f, 1f));

        Button cancelBuildButton = CreateUiButton("Cancel Build", buildPanel.transform, "X\nCANCEL", new Color(0.55f, 0.08f, 0.1f, 1f));
        RectTransform cancelRect = cancelBuildButton.GetComponent<RectTransform>();
        cancelRect.anchorMin = new Vector2(0.5f, 0f);
        cancelRect.anchorMax = new Vector2(0.5f, 0f);
        cancelRect.anchoredPosition = new Vector2(500f, 18f);
        cancelRect.sizeDelta = new Vector2(200f, 68f);
        cancelBuildButton.onClick.AddListener(CancelTowerBuildSelection);

        _towerUpgradeButton = CreateUiButton("Upgrade", buildPanel.transform, "[U] UPGRADE", new Color(0.15f, 0.58f, 0.28f, 1f));
        RectTransform upgradeRect = _towerUpgradeButton.GetComponent<RectTransform>();
        upgradeRect.anchorMin = new Vector2(0.5f, 0f);
        upgradeRect.anchorMax = new Vector2(0.5f, 0f);
        upgradeRect.anchoredPosition = new Vector2(280f, 18f);
        upgradeRect.sizeDelta = new Vector2(200f, 68f);
        _towerUpgradeButton.onClick.AddListener(TryUpgradeSelectedTower);
        _towerUpgradeButtonText = _towerUpgradeButton.GetComponentInChildren<Text>();

        _towerSellButton = CreateUiButton("Sell Tower", buildPanel.transform, "SELL", new Color(0.72f, 0.08f, 0.1f, 0.98f));
        RectTransform sellRect = _towerSellButton.GetComponent<RectTransform>();
        sellRect.anchorMin = new Vector2(0.5f, 0f);
        sellRect.anchorMax = new Vector2(0.5f, 0f);
        sellRect.anchoredPosition = new Vector2(280f, 92f);
        sellRect.sizeDelta = new Vector2(200f, 68f);
        _towerSellButtonText = _towerSellButton.GetComponentInChildren<Text>();
        _towerSellButton.onClick.AddListener(SellSelectedTower);
        _towerSellButton.gameObject.SetActive(false);

        _towerTargetPriorityButton = CreateUiButton("Target Priority", buildPanel.transform, "TARGET MODE\nNEAREST GOAL",
            new Color(0.12f, 0.38f, 0.68f, 1f));
        RectTransform priorityRect = _towerTargetPriorityButton.GetComponent<RectTransform>();
        priorityRect.anchorMin = new Vector2(0.5f, 0f);
        priorityRect.anchorMax = new Vector2(0.5f, 0f);
        priorityRect.anchoredPosition = new Vector2(500f, 92f);
        priorityRect.sizeDelta = new Vector2(200f, 68f);
        _towerTargetPriorityButtonText = _towerTargetPriorityButton.GetComponentInChildren<Text>();
        _towerTargetPriorityButton.onClick.AddListener(ToggleSelectedTowerTargetPriority);
        _towerTargetPriorityButton.gameObject.SetActive(false);

        BuildTacticalSkillUi(canvasObject.transform);

        _bossPanel = CreateUiPanel("Boss Panel", canvasObject.transform, new Color(0.08f, 0.015f, 0.1f, 0.94f));
        RectTransform bossRect = _bossPanel.GetComponent<RectTransform>();
        bossRect.anchorMin = new Vector2(0.5f, 1f);
        bossRect.anchorMax = new Vector2(0.5f, 1f);
        bossRect.pivot = new Vector2(0.5f, 1f);
        bossRect.anchoredPosition = new Vector2(0f, -24f);
        bossRect.sizeDelta = new Vector2(680f, 112f);
        _bossStatusText = CreateUiText("Boss Status", _bossPanel.transform, 23, TextAnchor.UpperCenter);
        RectTransform bossTextRect = _bossStatusText.rectTransform;
        bossTextRect.anchorMin = new Vector2(0f, 1f);
        bossTextRect.anchorMax = new Vector2(1f, 1f);
        bossTextRect.pivot = new Vector2(0.5f, 1f);
        bossTextRect.anchoredPosition = new Vector2(0f, -8f);
        bossTextRect.sizeDelta = new Vector2(-20f, 58f);
        Image bossHpBackground = CreateUiImage("Boss HP Background", _bossPanel.transform, new Color(0.12f, 0.04f, 0.14f, 1f));
        RectTransform bossHpRect = bossHpBackground.rectTransform;
        bossHpRect.anchorMin = new Vector2(0f, 0f);
        bossHpRect.anchorMax = new Vector2(1f, 0f);
        bossHpRect.pivot = new Vector2(0.5f, 0f);
        bossHpRect.anchoredPosition = new Vector2(0f, 16f);
        bossHpRect.sizeDelta = new Vector2(-36f, 30f);
        _bossHealthFill = CreateUiImage("Boss HP Fill", bossHpBackground.transform, new Color(0.88f, 0.08f, 0.8f, 1f));
        StretchRect(_bossHealthFill.rectTransform, 3f, 3f, 3f, 3f);
        _bossHealthFill.type = Image.Type.Simple;
        _bossHealthFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        CreateBossThresholdMarker(bossHpBackground.transform, 0, 0.75f, "J", new Color(1f, 0.08f, 0.75f, 1f));
        CreateBossThresholdMarker(bossHpBackground.transform, 1, 0.50f, "S", new Color(0.08f, 0.78f, 1f, 1f));
        CreateBossThresholdMarker(bossHpBackground.transform, 2, 0.25f, "H", new Color(1f, 0.58f, 0.05f, 1f));
        _bossPanel.SetActive(false);

        GameObject gameOverPanel = CreateUiPanel("Game Over", canvasObject.transform, new Color(0.05f, 0.01f, 0.01f, 0.94f));
        RectTransform gameOverRect = gameOverPanel.GetComponent<RectTransform>();
        gameOverRect.anchorMin = new Vector2(0.5f, 0.5f);
        gameOverRect.anchorMax = new Vector2(0.5f, 0.5f);
        gameOverRect.pivot = new Vector2(0.5f, 0.5f);
        gameOverRect.sizeDelta = new Vector2(720f, 300f);
        _towerDefenseGameOverText = CreateUiText("Game Over Text", gameOverPanel.transform, 42, TextAnchor.MiddleCenter);
        StretchRect(_towerDefenseGameOverText.rectTransform, 20f, 20f, 20f, 20f);
        gameOverPanel.SetActive(false);
    }

    private void CreateBuildButton(Transform parent, string label, float x, float y, RougeTowerType type, Color color)
    {
        Button button = CreateUiButton(type.ToString(), parent, label, color);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(195f, 68f);
        Text labelText = button.GetComponentInChildren<Text>();
        if (labelText != null) labelText.fontSize = 18;
        button.onClick.AddListener(() => BeginTowerBuild(type));
        int index = (int)type;
        if ((uint)index < (uint)_towerBuildButtons.Length)
        {
            _towerBuildButtons[index] = button;
            _towerBuildButtonTexts[index] = labelText;
        }
    }

    private static void SetPurchaseButtonAvailability(Button button, Text text, bool available)
    {
        if (button != null) button.interactable = available;
        if (text != null) text.color = available ? Color.white : new Color(0.48f, 0.5f, 0.54f, 1f);
    }

    private static string GetTowerBuildLabel(int hotkey, RougeTowerType type)
    {
        TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out _, out int cost);
        return $"[{hotkey}] {TowerDefenseVisuals.GetTowerName(type)}\n${cost}";
    }

    private void RefreshTowerDefenseUi(bool force = false)
    {
        if (_towerDefenseCanvas == null) return;
        if (!force && Time.unscaledTime < _nextTowerDefenseUiRefreshTime) return;
        _nextTowerDefenseUiRefreshTime = Time.unscaledTime + 0.1f;
        if (_towerDefenseStatusText != null)
        {
            float hp = mainTower != null ? mainTower.CurrentHealth : 0f;
            float maxHp = mainTower != null ? mainTower.maxHealth : 0f;
            int activeSpawners = 0;
            for (int i = 0; i < _towerDefenseSpawners.Count; i++)
            {
                if (_towerDefenseSpawners[i] != null && _towerDefenseSpawners[i].isActiveAndEnabled) activeSpawners++;
            }
            float bossCountdown = Mathf.Max(0f, bossBalance.spawnTimeSeconds - _survivalTime);
            string bossTime = _bossSpawned ? "BOSS ACTIVE" : _bossDefeated ? "BOSS DEFEATED" : $"BOSS IN {FormatGameTime(bossCountdown)}";
            int enemyLevel = GetTowerDefenseEnemyLevel();
            float enemyHealthBonus = (GetTowerDefenseEnemyLevelMultiplier(
                enemyBalance.healthGrowthMultiplier, enemyLevel) - 1f) * 100f;
            float enemySpeedBonus = (GetTowerDefenseEnemyLevelMultiplier(
                enemyBalance.speedGrowthMultiplier, enemyLevel) - 1f) * 100f;
            _towerDefenseStatusText.text =
                $"MAIN TOWER  {hp:0} / {maxHp:0}\n" +
                $"GOLD {_towerDefenseGold}   NORMAL +{enemyBalance.normalKillGold}   ELITE +{enemyBalance.eliteKillGold}\n" +
                $"KILLS {totalKills}   TIME {FormatGameTime(_survivalTime)}   {bossTime}\n" +
                $"ENEMY LV {enemyLevel} (NEW)   HP +{enemyHealthBonus:0.#}%   SPEED +{enemySpeedBonus:0.#}%\n" +
                $"ENEMIES ~ {_towerDefenseAliveEstimate}   HP {GetTowerDefenseEnemyHealth():0.#}   SPEED {GetTowerDefenseEnemySpeed():0.0}\n" +
                $"SPAWNERS {activeSpawners}/{_towerDefenseSpawners.Count}";
        }
        if (_mainTowerHealthFill != null)
            SetUiBarFill(_mainTowerHealthFill, mainTower != null ? mainTower.HealthNormalized : 0f);
        for (int typeIndex = 0; typeIndex < _towerBuildButtons.Length; typeIndex++)
        {
            TowerDefenseVisuals.GetBaseStats((RougeTowerType)typeIndex, out _, out _, out _, out _, out int cost);
            SetPurchaseButtonAvailability(_towerBuildButtons[typeIndex], _towerBuildButtonTexts[typeIndex],
                !_towerDefenseGameOver && _towerDefenseGold >= cost);
        }
        RefreshTowerDamageRanking();
        RefreshTacticalSkillUi();
        if (_towerSellButton != null)
        {
            bool showSell = _towerPlacementMode && _selectedTower != null;
            _towerSellButton.gameObject.SetActive(showSell);
            if (showSell)
            {
                int refund = Mathf.FloorToInt(_selectedTower.InvestedGold * Mathf.Clamp01(towerBalance.sellRefundMultiplier));
                if (_towerSellButtonText != null) _towerSellButtonText.text = $"SELL  +{refund}";
            }
        }
        if (_towerTargetPriorityButton != null)
        {
            bool showPriority = _towerPlacementMode && _selectedTower != null && _selectedTower.IsTargetedDamage;
            _towerTargetPriorityButton.gameObject.SetActive(showPriority);
            if (showPriority && _towerTargetPriorityButtonText != null)
            {
                _towerTargetPriorityButtonText.text = _selectedTower.TargetPriority == RougeTowerTargetPriority.BossFirst
                    ? "TARGET MODE\nBOSS FIRST"
                    : "TARGET MODE\nNEAREST GOAL";
            }
        }
        if (_bossPanel != null)
        {
            _bossPanel.SetActive(_bossSpawned || _bossDefeated);
            float bossHealth = _bossSpawned ? Mathf.Max(0f, _bossCurrentHealth) : 0f;
            float bossHealthRatio = Mathf.Clamp01(bossHealth / Mathf.Max(1f, bossBalance.maxHealth));
            if (_bossHealthFill != null) SetUiBarFill(_bossHealthFill, bossHealthRatio);
            RefreshBossThresholdMarkers();
            if (_bossStatusText != null)
            {
                string phases = $"{(_bossInterferenceActive ? "INTERFERENCE " : "")}{(_bossShieldActive ? "SHIELD " : "")}{(_bossHasteActive ? "HASTE" : "")}";
                _bossStatusText.text = _bossDeathSequenceActive
                    ? "BOSS CORE OVERLOAD"
                    : _bossDefeated
                    ? "BOSS DEFEATED"
                    : $"BOSS  {bossHealth:0} / {bossBalance.maxHealth:0}  ({bossHealthRatio * 100f:0.00}%)   {phases}";
            }
        }
        if (_towerDefenseModeText != null)
        {
            if (_towerPlacementMode)
            {
                string tactical = GetTacticalSkillModeText();
                string selected = !string.IsNullOrEmpty(tactical)
                    ? tactical
                    : _selectedTower != null
                    ? $"SELECTED: {_selectedTower.DisplayName}  LV {_selectedTower.Level}/{_selectedTower.MaxLevel}  {GetTowerUiStats(_selectedTower)}"
                    : _towerBuildSelectionActive
                        ? $"BUILD: {TowerDefenseVisuals.GetTowerName(_selectedBuildType)}  |  LEFT CLICK PLACE/SELECT"
                        : "BUILD CANCELLED  |  SELECT A TOWER BUTTON TO BUILD";
                _towerDefenseModeText.text = "EDIT MODE ×0.5  |  RMB EXIT/CANCEL  |  MIDDLE-DRAG  |  WHEEL ZOOM\n" + selected;
            }
            else
            {
                _towerDefenseModeText.text = "CLICK TOWER TO EDIT  |  CLICK BUILD BUTTON  |  LEFT-DRAG  |  WHEEL ZOOM";
            }
        }
        if (_towerUpgradeButton != null)
        {
            bool hasSelection = _selectedTower != null;
            bool canUpgrade = hasSelection && _selectedTower.CanUpgrade;
            bool upgradeAvailable = canUpgrade && _towerDefenseGold >= _selectedTower.UpgradeCost;
            SetPurchaseButtonAvailability(_towerUpgradeButton, _towerUpgradeButtonText, upgradeAvailable);
            if (_towerUpgradeButtonText != null)
            {
                _towerUpgradeButtonText.text = !hasSelection
                    ? "SELECT TOWER\nTO UPGRADE"
                    : !canUpgrade
                        ? $"LV {_selectedTower.Level}/{_selectedTower.MaxLevel}\nMAX LEVEL"
                        : $"[U] LV {_selectedTower.Level} > {_selectedTower.Level + 1}\n${_selectedTower.UpgradeCost}";
            }
        }
        if (_towerDefenseGameOverText != null)
        {
            GameObject panel = _towerDefenseGameOverText.transform.parent.gameObject;
            panel.SetActive(_towerDefenseGameOver);
            if (_towerDefenseGameOver)
            {
                _towerDefenseGameOverText.text = _towerDefenseVictory
                    ? $"MISSION COMPLETE\n{_towerDefenseGameOverReason}\n\nPRESS R TO RESTART"
                    : $"MISSION FAILED\n{_towerDefenseGameOverReason}\n\nPRESS R TO RESTART";
                _towerDefenseGameOverText.color = _towerDefenseVictory
                    ? new Color(0.3f, 1f, 0.7f, 1f)
                    : Color.white;
            }
        }
    }

    private void RefreshTowerDamageRanking()
    {
        // UI callbacks can run after LateUpdate scheduled the next simulation.
        // Keep the last ranking text instead of reading a NativeArray still owned by a job.
        if (_simulationResultBackBufferReady || _towerDamageRankingText == null || !_towerDamageTotalsFixed.IsCreated) return;
        for (int i = 0; i < _towerDamageRankOrder.Length; i++) _towerDamageRankOrder[i] = i;
        for (int i = 0; i < _towerDamageRankOrder.Length - 1; i++)
        {
            int best = i;
            for (int j = i + 1; j < _towerDamageRankOrder.Length; j++)
            {
                if (_towerDamageTotalsFixed[_towerDamageRankOrder[j]] >
                    _towerDamageTotalsFixed[_towerDamageRankOrder[best]]) best = j;
            }
            int temp = _towerDamageRankOrder[i];
            _towerDamageRankOrder[i] = _towerDamageRankOrder[best];
            _towerDamageRankOrder[best] = temp;
        }

        System.Text.StringBuilder builder = _hudBuilder;
        builder.Clear();
        builder.AppendLine("TOWER DAMAGE");
        for (int rank = 0; rank < _towerDamageRankOrder.Length; rank++)
        {
            int typeIndex = _towerDamageRankOrder[rank];
            double damage = _towerDamageTotalsFixed[typeIndex] / 1000.0;
            builder.Append(rank + 1).Append(". ")
                .Append(TowerDefenseVisuals.GetTowerName((RougeTowerType)typeIndex))
                .Append("   ").AppendLine(FormatCompactDamage(damage));
        }
        _towerDamageRankingText.text = builder.ToString();
    }

    private static string FormatCompactDamage(double value)
    {
        if (value >= 1000000d) return $"{value / 1000000d:0.##}M";
        if (value >= 10000d) return $"{value / 1000d:0.##}K";
        return value.ToString("0");
    }

    private static string FormatGameTime(float seconds)
    {
        int wholeSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{wholeSeconds / 60:00}:{wholeSeconds % 60:00}";
    }

    private static string GetTowerUiStats(RougeDefenseTower tower)
    {
        switch (tower.TowerType)
        {
            case RougeTowerType.Ice:
                return $"DMG {tower.Damage:0.#}  CD {tower.AttackInterval:0.##}s  RADIUS {tower.AttackRange:0.#}  SLOW {tower.EffectPercent:0}%/{tower.EffectDuration:0.#}s";
            case RougeTowerType.MachineGun:
                return $"DMG {tower.Damage:0.#}  3 FRAMES  RADIUS {tower.AttackRange:0.#}  TARGETS {tower.TargetCount}";
            case RougeTowerType.Cannon:
                return $"DMG {tower.Damage:0.#}  CD {tower.AttackInterval:0.##}s  RADIUS {tower.AttackRange:0.#}  AOE RADIUS {tower.AoeRadius:0.#}  SHELLS {tower.ProjectileCount}";
            case RougeTowerType.Flame:
                return $"TICK {tower.Damage:0.#}/{tower.TickInterval:0.##}s  RADIUS {tower.AttackRange:0.#}  AOE RADIUS {tower.AoeRadius:0.#}  LIFE {tower.EffectDuration:0.#}s";
            case RougeTowerType.Laser:
                return $"DMG {tower.Damage / 60f:0.#}/FRAME  RADIUS {tower.AttackRange:0.#}  TARGETS {tower.TargetCount}";
            case RougeTowerType.PiercingLaser:
                return $"DMG {tower.Damage:0.#}  CD {tower.AttackInterval:0.##}s  RADIUS {tower.AttackRange:0.#}  BEAM LEN {tower.AttackRange * 2f:0.#}";
            default:
                return $"CRYSTAL LASER DMG {tower.Damage:0.#}/{tower.TickInterval:0.##}s  COUNT {tower.ProjectileCount}  RADIUS {tower.OrbitSphereRadius:0.#}  MAX R {tower.AttackRange:0.#}  HOLD {tower.OrbitOuterHoldDuration:0.##}s  RADIAL {tower.OrbitRadialSpeed:0.#}  ROT {tower.OrbitAngularSpeed:0.#}°/s";
        }
    }

    private static GameObject CreateUiPanel(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private void CreateBossThresholdMarker(Transform parent, int index, float normalizedX, string glyph, Color color)
    {
        Image marker = CreateUiImage("Boss Threshold " + glyph, parent, Color.Lerp(color, Color.black, 0.58f));
        RectTransform rect = marker.rectTransform;
        rect.anchorMin = new Vector2(normalizedX, 0.5f);
        rect.anchorMax = new Vector2(normalizedX, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(24f, 24f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Text label = CreateUiText("Glyph", marker.transform, 14, TextAnchor.MiddleCenter);
        StretchRect(label.rectTransform, 0f, 0f, 0f, 0f);
        label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        label.text = glyph;
        label.color = Color.white;
        _bossThresholdMarkers[index] = marker;
        _bossThresholdLabels[index] = label;
    }

    private void RefreshBossThresholdMarkers()
    {
        for (int i = 0; i < _bossThresholdMarkers.Length; i++)
        {
            if (_bossThresholdMarkers[i] == null) continue;
            bool active = i == 0 ? _bossInterferenceActive : i == 1 ? _bossShieldActive : _bossHasteActive;
            Color color = i == 0
                ? new Color(1f, 0.08f, 0.75f, 1f)
                : i == 1
                    ? new Color(0.08f, 0.78f, 1f, 1f)
                    : new Color(1f, 0.58f, 0.05f, 1f);
            _bossThresholdMarkers[i].color = active ? color : Color.Lerp(color, Color.black, 0.62f);
            float pulse = active ? 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.12f : 1f;
            _bossThresholdMarkers[i].rectTransform.localScale = Vector3.one * pulse;
            if (_bossThresholdLabels[i] != null) _bossThresholdLabels[i].color = active ? Color.white : new Color(0.72f, 0.75f, 0.8f, 1f);
        }
    }

    private static Image CreateUiImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateUiText(string name, Transform parent, int fontSize, TextAnchor alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateUiButton(string name, Transform parent, string label, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
        button.colors = colors;
        Text text = CreateUiText("Label", go.transform, 20, TextAnchor.MiddleCenter);
        text.text = label;
        StretchRect(text.rectTransform, 4f, 4f, 4f, 4f);
        return button;
    }

    private static void StretchRect(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetBottomRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetUiBarFill(Image image, float normalizedValue)
    {
        float value = Mathf.Clamp01(normalizedValue);
        RectTransform rect = image.rectTransform;
        rect.localScale = Vector3.one;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(value, 1f);
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);
        image.enabled = value > 0.0001f;
    }
}
