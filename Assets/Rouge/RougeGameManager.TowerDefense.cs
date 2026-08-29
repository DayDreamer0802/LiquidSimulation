using System.Collections;
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
    private const int InitialTowerDefenseEnemyCap = 1000;
    private const int TowerDefenseEnemyCapPerMinute = 5000;
    private const int MaximumTowerDefenseEnemyCap = 100000;
    private const float MachineGunScatterHalfAngleDegrees = 9f;
    private const float MachineGunFocusedSpreadMultiplier = 0.5f;
    private const float TowerProjectileBurstInterval = 0.25f;
    private const float EchoAttackRepeatDelay = 0.25f;
    private const float FlameLandingOffsetRadiusMultiplier = 0.2f;
    private const float PiercingLaserChargeStageDuration = 0.5f;
    private const int PiercingLaserChargeStageCount = 3;
    private const float PiercingLaserChargeDuration =
        PiercingLaserChargeStageDuration * PiercingLaserChargeStageCount;
    private const float PiercingLaserTurnDuration = 0.25f;
    private const float PiercingLaserFireDuration = 0.75f;
    private const float PiercingLaserDamageTime = 0.25f;
    private const float PiercingLaserBeamRadius = 2.8f;
    private const float PiercingLaserMaxVisualWidthMultiplier = 1.25f;
    private const int TowerDefenseFixedRandomSeed = 1337;
    private const int ChargeTowerBaseGoldCost = 4000;
    private const float ChargeTowerCountCostMultiplier = 0.25f;
    private const int ReinforcementTowerBaseGoldCost = 6000;
    private const float ReinforcementTowerCountCostMultiplier = 0.5f;
    private const float AccumulatedWealthPayoutInterval = 30f;
    private const float AccumulatedWealthPayoutMultiplier = 1.5f;
    private const int TowerDefenseMapCellCapacity =
        RougeTowerDefenseMap.MaxMapCells * RougeTowerDefenseMap.MaxMapCells;
    private static readonly RougeTowerPlaceEffect[] ChargeTowerEffectPool =
    {
        RougeTowerPlaceEffect.DamageAmplifier,
        RougeTowerPlaceEffect.RangeAmplifier,
        RougeTowerPlaceEffect.AttackSpeedAmplifier,
        RougeTowerPlaceEffect.PremiumAmplifier,
        RougeTowerPlaceEffect.FreeLevelNoRefund,
        RougeTowerPlaceEffect.Bounty,
        RougeTowerPlaceEffect.Discount,
        RougeTowerPlaceEffect.Relocation,
        RougeTowerPlaceEffect.Echo,
        RougeTowerPlaceEffect.AccumulatedWealth,
        RougeTowerPlaceEffect.Explosion,
        RougeTowerPlaceEffect.Frost
    };
    private static readonly int LaserAlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int LaserVisualPhaseId = Shader.PropertyToID("_VisualPhase");
    private static readonly int LaserImpactFlashId = Shader.PropertyToID("_ImpactFlash");
    private static readonly int LaserStartFadeId = Shader.PropertyToID("_StartFade");
    private static readonly int LaserRootHemisphereId = Shader.PropertyToID("_RootHemisphere");
    private static readonly int LaserCoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int LaserBeamColorId = Shader.PropertyToID("_BeamColor");
    private static readonly int LaserGlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int LaserBaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LaserCoreRadiusId = Shader.PropertyToID("_CoreRadius");
    private static readonly int LaserBeamRadiusId = Shader.PropertyToID("_BeamRadius");
    private static readonly int LaserGlowSoftnessId = Shader.PropertyToID("_GlowSoftness");
    private static readonly int LaserRibbonIntensityId = Shader.PropertyToID("_RibbonIntensity");
    private static readonly int LaserNoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    private static readonly int LaserFlowScaleId = Shader.PropertyToID("_FlowScale");
    private static readonly int LaserFlowSpeedId = Shader.PropertyToID("_FlowSpeed");
    private static readonly string[] TowerPrefabResourcePaths =
    {
        "Prefab/tower/Ice",
        "Prefab/tower/MachineGun",
        "Prefab/tower/Cannon",
        "Prefab/tower/Flame",
        "Prefab/tower/Laser",
        "Prefab/tower/PiercingLaser",
        "Prefab/tower/OrbitSphere",
        "Prefab/tower/RocketBarrage",
        "Prefab/tower/ChargeTower",
        "Prefab/tower/ReinforcementTower"
    };
    public static bool TowerDefenseBuildModeActive { get; private set; }

    [Header("Tower Defense")]
    [SerializeField] private bool towerDefenseEnabled = true;
    [SerializeField, Min(0)] private int towerDefenseStartingGold = DefaultTowerDefenseStartingGold;
    [SerializeField] private RougeMainTower mainTower;
    [SerializeField] private RougeTowerBalanceConfig towerBalance = new RougeTowerBalanceConfig();
    [SerializeField] private RougeEnemyBalanceConfig enemyBalance = new RougeEnemyBalanceConfig();
    [SerializeField] private List<RougeBossBalanceConfig> bossBalances = new List<RougeBossBalanceConfig>();
    [SerializeField] private RougeBossBalanceConfig bossBalance = new RougeBossBalanceConfig();
    [SerializeField] private RougeTacticalSkillBalanceConfig tacticalSkillBalance = new RougeTacticalSkillBalanceConfig();
    [SerializeField] private RougeBossSpawnPoint bossSpawnPoint;

    private readonly List<RougeEnemySpawnPoint> _towerDefenseSpawners = new List<RougeEnemySpawnPoint>();
    private readonly List<RougeDefenseTower> _defenseTowers = new List<RougeDefenseTower>();
    private readonly List<TowerProjectile> _towerProjectiles = new List<TowerProjectile>();
    private readonly List<TowerFireZone> _towerFireZones = new List<TowerFireZone>();
    private readonly List<TowerFlameJetVisual> _towerFlameJetVisuals =
        new List<TowerFlameJetVisual>();
    private readonly Stack<GameObject> _towerFlameJetVisualPool = new Stack<GameObject>();
    private Material _towerFlameJetMaterial;
    private readonly List<TowerPersistentCannonZone> _towerPersistentCannonZones =
        new List<TowerPersistentCannonZone>();
    private Material _towerFireZoneMaterial;
    private readonly List<TowerBeamVisual> _towerBeamVisuals = new List<TowerBeamVisual>();
    private readonly List<ActiveOrbitSphereAttack> _activeOrbitSphereAttacks = new List<ActiveOrbitSphereAttack>();
    private readonly List<IceSpikeVisual> _iceSpikeVisuals = new List<IceSpikeVisual>();
    private readonly List<Vector2Int> _iceSpikeCandidateCells = new List<Vector2Int>(256);
    private readonly List<Vector2Int> _selectedSupportHighlightCells =
        new List<Vector2Int>(81);
    private readonly Stack<GameObject> _towerProjectileVisualPool = new Stack<GameObject>();
    private readonly int[] _towerTargetIndices = new int[FindTowerTargetsJob.MaxTargetsPerTower];
    private readonly float[] _towerTargetDistances = new float[FindTowerTargetsJob.MaxTargetsPerTower];
    private readonly Vector3[] _towerTargetPositions = new Vector3[FindTowerTargetsJob.MaxTargetsPerTower];
    private const int MaximumLaserRefractionHits = FindTowerTargetsJob.MaxTargetsPerTower * 3;
    private const int MaximumLaserVisualSegments = FindTowerTargetsJob.MaxTargetsPerTower * 4;
    private readonly int[] _laserRefractionIndices = new int[MaximumLaserRefractionHits];
    private readonly Vector3[] _laserRefractionPositions = new Vector3[MaximumLaserRefractionHits];
    private readonly float[] _laserRefractionDamageMultipliers =
        new float[MaximumLaserRefractionHits];
    private readonly Vector3[] _laserVisualSegmentStarts =
        new Vector3[MaximumLaserVisualSegments];
    private readonly Vector3[] _laserVisualSegmentEnds =
        new Vector3[MaximumLaserVisualSegments];
    private readonly int[] _accumulatedWealthPendingGold = new int[TowerDefenseMapCellCapacity];
    private readonly float[] _accumulatedWealthPayoutTimers = new float[TowerDefenseMapCellCapacity];
    private bool _towerDefenseInitialized;
    private bool _towerDefenseStartupActive;
    private Coroutine _towerDefenseStartupRoutine;
    private bool _towerPlacementMode;
    private bool _showAllTowerAttackRanges;
    private bool _towerDefenseDoubleSpeed;
    private bool _towerBuildSelectionActive = true;
    private bool _chargeTowerBuildSelectionActive;
    private bool _reinforcementTowerBuildSelectionActive;
    private bool _chargeTowerTargetSelectionActive;
    private bool _chargeTowerEffectSelectionActive;
    private bool _towerDefenseGameOver;
    private bool _towerDefenseSceneReloadRequested;
    private string _towerDefenseGameOverReason;
    private int _towerDefenseGold;
    private int _towerDefenseGoldEarnedTotal;
    private int _towerDefenseAliveEstimate;
    private int _towerDefenseSpawnedTotal;
    private float _towerDefenseSpawnerResolveRetryTimer;
    private bool _towerDefenseAllSpawnersExhausted;
    private float _nextKillAllVerificationTime;
    private int _towerDefenseSpawnSearchCursor;
    private RougeTowerType _selectedBuildType = RougeTowerType.Ice;
    private RougeDefenseTower _towerPreview;
    private RougeDefenseTower _towerPlacementHoveredTower;
    private RougeDefenseTower _pendingChargeTower;
    private Vector2Int _pendingChargeTowerCell;
    private bool _pendingChargeTowerTargetValid;
    private int _pendingChargeTowerEscrow;
    private int _chargeTowerRefreshCount;
    private readonly RougeTowerPlaceEffect[] _chargeTowerEffectChoices =
        new RougeTowerPlaceEffect[3];
    private RougeDefenseTower _selectedTower;
    private RougeDefenseTower _relocatingTower;
    private bool _towerRelocationActive;
    private Vector2Int _relocationOriginalAnchor;
    private bool _previewValid;
    private Vector2Int _previewTowerAnchor;
    private bool _suppressRepeatedPreviewAtPlacedCell;
    private Vector2Int _lastPlacedTowerAnchor;
    private bool[] _previewCellValidity;
    private bool _towerMiddleClickPending;
    private Vector2 _towerMiddleClickStartPosition;
    private RougeDefenseTower _towerMiddleClickTarget;
    private const float TowerMiddleClickDragThreshold = 10f;
    private bool _pendingMainTowerAoe;
    private Canvas _towerDefenseCanvas;
    private Text _towerDefenseStatusText;
    private Text _towerDefenseControlsText;
    private Button _visualQualityButton;
    private Text _visualQualityButtonText;
    private Image _selectedTowerPortraitFrame;
    private Image _selectedTowerPortrait;
    private Text _selectedTowerSummaryText;
    private Text _selectedTowerBuffText;
    private RectTransform _towerActionContainer;
    private Text _towerDefenseGameOverText;
    private Image _mainTowerHealthFill;
    private Text _mainTowerHealthText;
    private GameObject _towerDamagePanel;
    private GameObject _towerPlaceEffectPanel;
    private Text _towerPlaceEffectText;
    private Button _towerCancelBuildButton;
    private Text _towerCancelBuildButtonText;
    private Button _towerUpgradeButton;
    private Text _towerUpgradeButtonText;
    private Button _towerUpgradeChoiceButton;
    private Text _towerUpgradeChoiceButtonText;
    private Button _towerSellButton;
    private Text _towerSellButtonText;
    private Button _towerTargetPriorityButton;
    private Text _towerTargetPriorityButtonText;
    private Button _towerRelocateButton;
    private Text _towerRelocateButtonText;
    private Text _towerDamageRankingText;
    private Button _chargeTowerBuildButton;
    private Text _chargeTowerBuildButtonText;
    private Button _reinforcementTowerBuildButton;
    private Text _reinforcementTowerBuildButtonText;
    private GameObject _chargeTowerEffectSelectionPanel;
    private Text _chargeTowerEffectSelectionSummary;
    private readonly Button[] _chargeTowerEffectChoiceButtons = new Button[3];
    private readonly Text[] _chargeTowerEffectChoiceTexts = new Text[3];
    private Button _chargeTowerRefreshButton;
    private Text _chargeTowerRefreshButtonText;
    private readonly Button[] _towerBuildButtons = new Button[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly Text[] _towerBuildButtonTexts = new Text[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly int[] _towerDamageRankOrder = new int[TowerDefenseVisuals.StandardTowerTypeCount];
    private float _nextTowerDefenseUiRefreshTime;
    private Image _bossHealthFill;
    private Text _bossStatusText;
    private GameObject _bossPanel;
    private readonly Image[] _bossThresholdMarkers = new Image[3];
    private readonly Text[] _bossThresholdLabels = new Text[3];
    private static Font s_towerDefenseHudFont;
    private LineRenderer _bossInterferenceRing;
    private LineRenderer _bossShieldRing;
    private LineRenderer _bossHasteRing;
    private float _bossInterferencePulseTimer;
    private float _bossShieldPulseTimer;
    private float _bossHastePulseTimer;
    private float _bossCurrentHealth;
    private float _bossBaseMoveSpeed;
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
    private bool _bossDeathShouldGrantVictory;
    private bool _towerDefenseVictory;
    private RougeTowerDefenseMap _towerDefenseLevel;
    private readonly List<RougeTowerDefenseMap.BossEncounter> _bossSchedule =
        new List<RougeTowerDefenseMap.BossEncounter>();
    private int _nextBossEncounterIndex;
    private RougeTowerDefenseMap.BossEncounter _activeBossEncounter;
    private bool _towerDefensePlayerWasActive;
    private bool _towerDefenseHudWasActive;

    private const int MachineGunProjectileNormal = 0;
    private const int MachineGunProjectileFragment = 1;
    private const int MaximumMachineGunFragmentsPerBurst = 36;
    private const float MachineGunCollisionSearchEnemyRadius = 5f;

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
        public float BurnDamage;
        public float BurnDuration;
        public float BurnTickInterval;
        public int BurnMaximumStacks;
        public float BurnDamageBonusPerStack;
        public float ConflagrationDamage;
        public int TargetIndex;
        public Vector2 TargetOffset;
        public int KillGoldBonus;
        public int WealthCellIndexPlusOne;
        public int TileEffect;
        public float CriticalChance;
        public float CriticalDamageMultiplier;
        public float CriticalArmorPenetration;
        public float FragmentTriggerChance;
        public int FragmentCount;
        public float FragmentDamageMultiplier;
        public float FragmentTravelDistance;
        public int MachineGunProjectileMode;
        public float EmbeddedFragmentChance;
        public float CannonInnerRadiusMultiplier;
        public float CannonInnerDamageMultiplier;
        public float CannonSecondaryTriggerChance;
        public int CannonSecondaryProjectileCount;
        public float CannonSecondaryDamageMultiplier;
        public float CannonSecondaryRadiusMultiplier;
        public float CannonSecondaryFlightDuration;
        public float CannonSecondaryTravelDistanceMultiplier;
        public float CannonSecondaryArcHeightMultiplier;
        public float CannonPersistentLandingDamageMultiplier;
        public float CannonPersistentTickInterval;
        public float CannonPersistentTickDamageMultiplier;
        public int CannonPersistentTickCount;
        public float CannonPersistentKnockbackForce;
    }

    private struct TowerFireZone
    {
        public Vector3 Position;
        public float Radius;
        public float Remaining;
        public float Duration;
        public float DamagePerTick;
        public float TickInterval;
        public float TickTimer;
        public float BurnDamage;
        public float BurnDuration;
        public float BurnTickInterval;
        public int BurnMaximumStacks;
        public float BurnDamageBonusPerStack;
        public float ConflagrationDamage;
        public float VisualPhase;
        public int KillGoldBonus;
        public int WealthCellIndexPlusOne;
        public int TileEffect;
        public GameObject Visual;
        public Renderer Renderer;
        public MaterialPropertyBlock Properties;
    }

    private struct TowerFlameJetVisual
    {
        public GameObject Root;
        public LineRenderer Line;
        public float Remaining;
        public float Duration;
        public float StartWidth;
        public float EndWidth;
    }

    private struct TowerPersistentCannonZone
    {
        public GameObject Visual;
        public Vector3 Position;
        public float Radius;
        public float DamagePerTick;
        public float TickInterval;
        public float TickTimer;
        public int RemainingTicks;
        public float KnockbackForce;
        public int KillGoldBonus;
        public int WealthCellIndexPlusOne;
        public int TileEffect;
    }

    private struct TowerBeamVisual
    {
        public RougeDefenseTower SourceTower;
        public GameObject Visual;
        public GameObject GlowVisual;
        public GameObject RootCapVisual;
        public GameObject RootGlowCapVisual;
        public GameObject ChargeVisual;
        public MeshRenderer Renderer;
        public MeshRenderer GlowRenderer;
        public MeshRenderer RootCapRenderer;
        public MeshRenderer RootGlowCapRenderer;
        public MeshRenderer ChargeRenderer;
        public MaterialPropertyBlock Properties;
        public MaterialPropertyBlock GlowProperties;
        public MaterialPropertyBlock ChargeProperties;
        public Vector3 Start;
        public Vector3 Direction;
        public Vector3 TurnStartDirection;
        public float Length;
        public float MaxWidth;
        public float ChargeElapsed;
        public float FireElapsed;
        public float TurnElapsed;
        public float Damage;
        public int KillGoldBonus;
        public int WealthCellIndexPlusOne;
        public int TileEffect;
        public int TargetIndex;
        public bool ChargeComplete;
        public bool TargetLost;
        public bool DamageApplied;
        public bool FiringAnimationPlayed;
    }

    private struct IceSpikeVisual
    {
        public GameObject Root;
        public SpriteRenderer Renderer;
        public float Elapsed;
        public float Duration;
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
        RougeDefenseTower.PreloadTowerAudio();
        UnityEngine.Random.InitState(TowerDefenseFixedRandomSeed);
        if (RougeTowerDefenseBalanceJson.TryLoad(out RougeTowerDefenseBalanceJsonData jsonBalance))
        {
            towerBalance = jsonBalance.towerBalance;
            enemyBalance = jsonBalance.enemyBalance;
            bossBalances = jsonBalance.bossBalances;
            bossBalance = jsonBalance.bossBalance;
            tacticalSkillBalance = jsonBalance.tacticalSkillBalance;
        }
        towerBalance ??= new RougeTowerBalanceConfig();
        enemyBalance ??= new RougeEnemyBalanceConfig();
        bossBalances ??= new List<RougeBossBalanceConfig>();
        bossBalance ??= new RougeBossBalanceConfig();
        tacticalSkillBalance ??= new RougeTacticalSkillBalanceConfig();
        towerBalance.EnsureDefaults();
        enemyBalance.EnsureDefaults();
        EnsureBossBalanceDefaults();
        tacticalSkillBalance.EnsureDefaults();
        _towerDefenseLevel = RougeTowerDefenseMapLoader.ActiveMap;
        InitializePlayerSettings();
        cameraZoomMultiplier = 1f;
        _cameraViewMode = CameraViewMode.Default;
        ApplyEnemySpriteSheetTextures();
        TowerDefenseVisuals.SetRuntimeBalance(towerBalance);
        TowerDefenseVisuals.SetRuntimeLevelModifiers(
            _towerDefenseLevel != null ? _towerDefenseLevel.TowerGoldCostMultiplier : 1f,
            _towerDefenseLevel != null ? _towerDefenseLevel.TowerDamageMultiplier : 1f,
            _towerDefenseLevel != null ? _towerDefenseLevel.TowerAttackSpeedMultiplier : 1f);
        _towerDefenseGold = _towerDefenseLevel != null
            ? Mathf.Max(0, _towerDefenseLevel.StartingGold)
            : Mathf.Max(0, towerDefenseStartingGold);
        _towerDefenseGoldEarnedTotal = 0;
        System.Array.Clear(_accumulatedWealthPendingGold, 0,
            _accumulatedWealthPendingGold.Length);
        System.Array.Clear(_accumulatedWealthPayoutTimers, 0,
            _accumulatedWealthPayoutTimers.Length);
        _towerDefenseAliveEstimate = 0;
        _towerDefenseSpawnSearchCursor = 0;
        _towerDefenseAllSpawnersExhausted = false;
        _nextKillAllVerificationTime = 0f;
        _towerDefenseGameOver = false;
        _towerDefenseGameOverReason = string.Empty;
        _towerPlacementMode = false;
        _towerDefenseDoubleSpeed = false;
        TowerDefenseBuildModeActive = false;
        _towerBuildSelectionActive = false;
        _chargeTowerBuildSelectionActive = false;
        _reinforcementTowerBuildSelectionActive = false;
        _chargeTowerTargetSelectionActive = false;
        _chargeTowerEffectSelectionActive = false;
        _pendingChargeTower = null;
        _pendingChargeTowerEscrow = 0;
        _pendingChargeTowerTargetValid = false;
        _chargeTowerRefreshCount = 0;
        _towerRelocationActive = false;
        _relocatingTower = null;
        _towerPlacementHoveredTower = null;
        _relocationOriginalAnchor = default;
        _pendingMainTowerAoe = false;
        Time.timeScale = 1f;
        if (CommanderSkillsEnabled) InitializeTacticalSkills();
        _bossEnemyIndex = -1;
        _bossSpawned = false;
        _bossDefeated = false;
        _bossInterferenceActive = false;
        _bossShieldActive = false;
        _bossHasteActive = false;
        _bossCurrentHealth = 0f;
        _bossBaseMoveSpeed = 0f;
        _bossDeathSequenceActive = false;
        _bossDeathSequenceTimer = 0f;
        _bossDeathShockwaveStep = 0;
        _bossDeathExplosionTriggered = false;
        _bossDeathShouldGrantVictory = false;
        _towerDefenseVictory = false;
        _towerDefenseBossArrivalActive = false;
        _towerDefenseBossArrivalTimer = 0f;
        _towerDefenseBossLandingShakeRemaining = 0f;
        InitializeTowerDefenseFailurePresentation();
        BuildBossSchedule();
        InitializeTowerDefenseLevelEvents();

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
        AssignNamedTowerPlaceLayers();
        SetTowerPlaceVisualsVisible(false);
        ResolveMainTower();
        RougeCameraFollow cameraFollow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.SetTowerDefensePan(true);
            RougeCameraFollow.ViewState defaultView =
                ResolveCameraViewPreset(CameraViewMode.Default, cameraFollow);
            PrepareCameraPresetTransition(cameraFollow, CameraViewMode.Default, defaultView);
            cameraFollow.ApplyViewState(defaultView);
        }
        ResolveEnemySpawnPoints();
        ResolveExistingDefenseTowers();
        RefreshReinforcementTowerAuras();
        PrepareTowerTargetRequests();
        ResetTowerDefenseAutoplaySession();
        BuildTowerDefenseUi();
        BuildTowerDefenseAutoplayUi();
        BuildTowerDefenseLevelEventUi();
        PrewarmTowerDefenseCameraUiGlyphs();
        RougeCameraModeToast.Prewarm(GetTowerDefenseHudFont());
        BuildPlayerSettingsUi();
        ApplyDamageStatisticsVisibility();
        RefreshTowerDefenseUi();
        BeginTowerDefenseStartup();
    }

    private void BeginTowerDefenseStartup()
    {
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        if (loader == null || !loader.StartupRevealEnabled || _towerDefenseLevel == null)
        {
            loader?.CancelStartupReveal();
            _towerDefenseStartupActive = false;
            return;
        }

        _towerDefenseStartupActive = true;
        Time.timeScale = 0f;
        if (_towerDefenseCanvas != null) _towerDefenseCanvas.gameObject.SetActive(false);
        _towerDefenseStartupRoutine = StartCoroutine(PlayTowerDefenseStartup(loader));
    }

    private IEnumerator PlayTowerDefenseStartup(RougeTowerDefenseMapLoader loader)
    {
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        RougeTiltShiftCamera tiltShift = ResolveTiltShiftCamera();
        if (follow != null)
        {
            follow.CancelScriptedView();
            RougeCameraFollow.ViewState openingView =
                ResolveCameraViewPreset(CameraViewMode.TiltShift, follow);
            follow.SetViewBaseline(openingView);
            follow.ApplyViewState(openingView);
        }
        if (tiltShift != null)
        {
            tiltShift.ApplySettings(_towerDefenseLevel.TiltShiftSettings);
            tiltShift.ClearWorldFocusPoint();
            tiltShift.SetEffectEnabled(true);
        }

        yield return loader.PlayStartupReveal(mainTower);

        if (follow != null)
        {
            RougeCameraFollow.ViewState gameplayView =
                ResolveCameraViewPreset(CameraViewMode.Default, follow);
            PrepareCameraPresetTransition(follow, CameraViewMode.Default, gameplayView);
            follow.TransitionAndReleaseScriptedView(gameplayView);
            while (follow != null && follow.IsScriptedViewActive)
                yield return null;
        }

        if (tiltShift != null) tiltShift.SetEffectEnabled(false);
        if (_towerDefenseCanvas != null) _towerDefenseCanvas.gameObject.SetActive(true);
        _towerDefenseStartupActive = false;
        if (_towerDefenseGameOver)
            Time.timeScale = 0f;
        else
            ApplyTowerDefenseTimeScale();
        RefreshTowerDefenseUi(true);
        _towerDefenseStartupRoutine = null;
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

        ResetTowerDefenseAutoplaySession();
        DisposeTowerDefenseAutoplayUi();
        DisposeTowerDefenseLevelEvents();
        DisposeTowerDefenseFailurePresentation();
        StopAllTowerAttackSounds();
        if (_towerDefenseStartupRoutine != null)
        {
            StopCoroutine(_towerDefenseStartupRoutine);
            _towerDefenseStartupRoutine = null;
        }
        RougeTowerDefenseMapLoader.Active?.CancelStartupReveal();
        _towerDefenseStartupActive = false;
        RougeDefenseTower.ShutdownTowerAudio();
        HideTowerDefenseSpawnWarnings();
        if (_cameraViewMode != CameraViewMode.Default) ExitDebugUnitView();

        DisableBossTowerInterferenceMarkers();
        DestroyBossPhaseVisuals();
        if (_bossSpriteAnimator != null) Destroy(_bossSpriteAnimator.gameObject);
        _bossSpriteAnimator = null;
        _towerDefenseBossArrivalActive = false;
        _towerDefenseBossArrivalTimer = 0f;
        _towerDefenseBossLandingShakeRemaining = 0f;
        RougeCameraFollow disposingCamera = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (disposingCamera != null)
        {
            disposingCamera.SetCinematicShake(0f);
            disposingCamera.EndCinematicFocus();
        }
        Time.timeScale = 1f;
        TowerDefenseBuildModeActive = false;
        _towerPlacementMode = false;
        _towerDefenseDoubleSpeed = false;
        SetTowerPlacementHoveredTower(null);
        if (_pendingChargeTower != null) Destroy(_pendingChargeTower.gameObject);
        _pendingChargeTower = null;
        _pendingChargeTowerEscrow = 0;
        _pendingChargeTowerTargetValid = false;
        _chargeTowerTargetSelectionActive = false;
        _chargeTowerEffectSelectionActive = false;
        _chargeTowerBuildSelectionActive = false;
        _reinforcementTowerBuildSelectionActive = false;
        ClearTowerRelocationState();
        RefreshTowerEditHints();
        if (player != null)
        {
            player.gameObject.SetActive(_towerDefensePlayerWasActive);
            player.SuppressMovement = false;
        }
        if (_uiText != null) _uiText.gameObject.SetActive(_towerDefenseHudWasActive);
        RougeCameraFollow cameraFollow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (cameraFollow != null) cameraFollow.SetTowerDefensePan(false);
        SetTowerPlaceVisualsVisible(false);
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        DisposeTacticalSkills();

        for (int i = 0; i < _towerProjectiles.Count; i++)
        {
            if (_towerProjectiles[i].Visual != null) Destroy(_towerProjectiles[i].Visual);
        }
        _towerProjectiles.Clear();
        for (int i = 0; i < _towerPersistentCannonZones.Count; i++)
        {
            if (_towerPersistentCannonZones[i].Visual != null)
                Destroy(_towerPersistentCannonZones[i].Visual);
        }
        _towerPersistentCannonZones.Clear();
        DisposeRocketBarrageSystem();
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
        if (_towerFireZoneMaterial != null) Destroy(_towerFireZoneMaterial);
        _towerFireZoneMaterial = null;
        for (int i = 0; i < _towerFlameJetVisuals.Count; i++)
            if (_towerFlameJetVisuals[i].Root != null)
                Destroy(_towerFlameJetVisuals[i].Root);
        _towerFlameJetVisuals.Clear();
        while (_towerFlameJetVisualPool.Count > 0)
        {
            GameObject pooled = _towerFlameJetVisualPool.Pop();
            if (pooled != null) Destroy(pooled);
        }
        if (_towerFlameJetMaterial != null) Destroy(_towerFlameJetMaterial);
        _towerFlameJetMaterial = null;
        for (int i = 0; i < _towerBeamVisuals.Count; i++)
        {
            DestroyTowerBeamVisual(_towerBeamVisuals[i]);
        }
        _towerBeamVisuals.Clear();
        for (int i = 0; i < _activeOrbitSphereAttacks.Count; i++)
            _activeOrbitSphereAttacks[i].Positions = null;
        _activeOrbitSphereAttacks.Clear();
        for (int i = 0; i < _iceSpikeVisuals.Count; i++)
        {
            if (_iceSpikeVisuals[i].Root != null) Destroy(_iceSpikeVisuals[i].Root);
        }
        _iceSpikeVisuals.Clear();
        _iceSpikeCandidateCells.Clear();
        if (_towerDefenseCanvas != null) Destroy(_towerDefenseCanvas.gameObject);
        _towerDefenseCanvas = null;
        _towerDamagePanel = null;
        DisposePlayerSettingsUi();
        _towerTargetRequestCount = 0;
        _towerTargetScheduledCount = 0;
        _bossSchedule.Clear();
        _activeBossEncounter = null;
        _towerDefenseLevel = null;
        System.Array.Clear(_accumulatedWealthPendingGold, 0,
            _accumulatedWealthPendingGold.Length);
        System.Array.Clear(_accumulatedWealthPayoutTimers, 0,
            _accumulatedWealthPayoutTimers.Length);
        TowerDefenseVisuals.SetRuntimeLevelModifiers(1f, 1f, 1f);
        _towerDefenseInitialized = false;
    }

    private void EnsureTowerDefenseConfigDefaults()
    {
        if (towerDefenseStartingGold == LegacyTowerDefenseStartingGold)
            towerDefenseStartingGold = DefaultTowerDefenseStartingGold;
        towerBalance ??= new RougeTowerBalanceConfig();
        enemyBalance ??= new RougeEnemyBalanceConfig();
        bossBalances ??= new List<RougeBossBalanceConfig>();
        bossBalance ??= new RougeBossBalanceConfig();
        tacticalSkillBalance ??= new RougeTacticalSkillBalanceConfig();
        towerBalance.EnsureDefaults();
        enemyBalance.EnsureDefaults();
        EnsureBossBalanceDefaults();
        tacticalSkillBalance.EnsureDefaults();
    }

    private void EnsureBossBalanceDefaults()
    {
        bossBalances ??= new List<RougeBossBalanceConfig>();
        bossBalance ??= new RougeBossBalanceConfig();
        if (bossBalances.Count == 0) bossBalances.Add(bossBalance);
        for (int i = bossBalances.Count - 1; i >= 0; i--)
        {
            if (bossBalances[i] == null)
            {
                bossBalances.RemoveAt(i);
                continue;
            }
            bossBalances[i].EnsureDefaults();
        }
        if (bossBalances.Count == 0) bossBalances.Add(new RougeBossBalanceConfig());
        bossBalance = bossBalances[0];
        bossBalance.EnsureDefaults();
    }

    private void BuildBossSchedule()
    {
        _bossSchedule.Clear();
        _nextBossEncounterIndex = 0;
        _activeBossEncounter = null;
        if (_towerDefenseLevel != null)
        {
            IReadOnlyList<RougeTowerDefenseMap.BossEncounter> configured =
                _towerDefenseLevel.BossEncounters;
            for (int i = 0; configured != null && i < configured.Count; i++)
            {
                if (configured[i] != null) _bossSchedule.Add(configured[i]);
            }
        }
        else
        {
            _bossSchedule.Add(new RougeTowerDefenseMap.BossEncounter
            {
                bossId = bossBalance.bossId,
                spawnMinute = Mathf.Max(0f, bossBalance.spawnTimeSeconds / 60f),
                defeatGrantsVictory = true
            });
        }
        _bossSchedule.Sort((left, right) => left.spawnMinute.CompareTo(right.spawnMinute));
    }

    private RougeBossBalanceConfig FindBossBalance(int bossId)
    {
        for (int i = 0; i < bossBalances.Count; i++)
        {
            RougeBossBalanceConfig configured = bossBalances[i];
            if (configured != null && configured.bossId == bossId) return configured;
        }
        return null;
    }

    private bool HasLevelVictoryCondition(RougeLevelVictoryConditionType type)
    {
        // Scenes without a map keep the legacy Boss-kill victory behavior.
        return _towerDefenseLevel != null
            ? _towerDefenseLevel.HasVictoryCondition(type)
            : type == RougeLevelVictoryConditionType.KillBoss;
    }

    private bool IsTowerTypeDisabled(RougeTowerType type)
    {
        return _towerDefenseLevel != null && _towerDefenseLevel.IsTowerDisabled((int)type);
    }

    private bool CanAffordTowerType(RougeTowerType type)
    {
        if (IsTowerTypeDisabled(type) || _chargeTowerTargetSelectionActive ||
            _chargeTowerEffectSelectionActive) return false;
        if (type == RougeTowerType.ChargeTower)
            return _towerDefenseGold >= GetChargeTowerGoldCost();
        if (type == RougeTowerType.ReinforcementTower)
            return _towerDefenseGold >= GetReinforcementTowerGoldCost();
        TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out _, out int cost);
        return _towerDefenseGold >= Mathf.Max(0, cost);
    }

    private bool CanAffordAnyTowerType()
    {
        for (int typeIndex = 0; typeIndex < TowerDefenseVisuals.StandardTowerTypeCount; typeIndex++)
        {
            if (CanAffordTowerType((RougeTowerType)typeIndex)) return true;
        }
        return CanAffordTowerType(RougeTowerType.ChargeTower) ||
               CanAffordTowerType(RougeTowerType.ReinforcementTower);
    }

    private int GetChargeTowerGoldCost()
    {
        return CalculateChargeTowerGoldCost();
    }

    private int CalculateChargeTowerGoldCost()
    {
        int existingChargeTowers = 0;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            if (_defenseTowers[i] != null && _defenseTowers[i].IsChargeTower)
                existingChargeTowers++;
        }
        int baseCost = TowerDefenseVisuals.GetLevelGoldCost(RougeTowerType.ChargeTower, 1);
        if (baseCost <= 0) baseCost = ChargeTowerBaseGoldCost;
        float countMultiplier = GetSpecialTowerCountCostMultiplier(
            RougeTowerType.ChargeTower, ChargeTowerCountCostMultiplier);
        double multiplier = 1d + existingChargeTowers * countMultiplier;
        return (int)System.Math.Min(int.MaxValue,
            System.Math.Ceiling(baseCost * multiplier));
    }

    private int GetReinforcementTowerGoldCost()
    {
        return CalculateReinforcementTowerGoldCost();
    }

    private int CalculateReinforcementTowerGoldCost()
    {
        int existingTowers = 0;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            if (_defenseTowers[i] != null && _defenseTowers[i].IsReinforcementTower)
                existingTowers++;
        }
        int baseCost = TowerDefenseVisuals.GetLevelGoldCost(
            RougeTowerType.ReinforcementTower, 1);
        if (baseCost <= 0) baseCost = ReinforcementTowerBaseGoldCost;
        float countMultiplier = GetSpecialTowerCountCostMultiplier(
            RougeTowerType.ReinforcementTower, ReinforcementTowerCountCostMultiplier);
        double multiplier = 1d + existingTowers * countMultiplier;
        return (int)System.Math.Min(int.MaxValue,
            System.Math.Ceiling(baseCost * multiplier));
    }

    private float GetSpecialTowerCountCostMultiplier(RougeTowerType type, float fallback)
    {
        RougeTowerTypeConfig config = towerBalance?.Find(type);
        return config != null
            ? Mathf.Max(0f, config.specialTowerCountCostMultiplier)
            : Mathf.Max(0f, fallback);
    }

    private bool HasPendingBossEncounter()
    {
        return _bossSpawned || _towerDefenseBossArrivalActive ||
               _bossDeathSequenceActive ||
               _nextBossEncounterIndex < _bossSchedule.Count;
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

    private void SetTowerPlaceVisualsVisible(bool visible)
    {
        RougeDefenseTower gridPreview = visible && _towerPreview != null &&
            _towerPreview.gameObject.activeInHierarchy ? _towerPreview : null;
        _selectedSupportHighlightCells.Clear();
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (visible && map != null && _selectedTower != null)
        {
            if (_selectedTower.IsChargeTower && _selectedTower.HasChargeTargetCell)
            {
                _selectedSupportHighlightCells.Add(_selectedTower.ChargeTargetCell);
            }
            else if (_selectedTower.IsReinforcementTower &&
                     map.WorldToCell(_selectedTower.transform.position,
                         out Vector2Int reinforcementCell))
            {
                int auraRange = _selectedTower.ReinforcementAuraRangeCells;
                for (int y = -auraRange; y <= auraRange; y++)
                {
                    for (int x = -auraRange; x <= auraRange; x++)
                    {
                        Vector2Int cell = reinforcementCell + new Vector2Int(x, y);
                        if (map.IsTowerPlace(cell))
                            _selectedSupportHighlightCells.Add(cell);
                    }
                }
            }
        }
        RougeTowerDefenseMapLoader.Active?.SetTowerPlaceGridState(
            visible, _defenseTowers, gridPreview, _previewCellValidity, _previewValid,
            _chargeTowerTargetSelectionActive, _pendingChargeTowerCell,
            _pendingChargeTowerTargetValid, _selectedSupportHighlightCells);
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
        if (_towerDefenseAllSpawnersExhausted) return;
        _towerDefenseSpawners.Clear();
        Scene activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        bool useRuntimeMap = UnityEngine.Object.FindFirstObjectByType<RougeTowerDefenseMapLoader>() != null;
        RougeEnemySpawnPoint[] found = UnityEngine.Object.FindObjectsByType<RougeEnemySpawnPoint>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            RougeEnemySpawnPoint point = found[i];
            if (point == null || point.gameObject.scene != activeScene) continue;
            if (useRuntimeMap && point.GetComponentInParent<RougeRuntimeMapObject>() == null) continue;
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
        _towerDefenseAllSpawnersExhausted = false;
        _towerDefenseSpawnerResolveRetryTimer = 0f;
        Debug.Log($"Tower Defense: found {_towerDefenseSpawners.Count} scene spawn points.", this);
    }

    private void ResolveExistingDefenseTowers()
    {
        _defenseTowers.Clear();
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        var occupiedCells = new HashSet<Vector2Int>();
        if (map != null && mainTower != null &&
            map.WorldToCell(mainTower.transform.position, out Vector2Int mainTowerCell))
            occupiedCells.Add(mainTowerCell);
        RougeDefenseTower[] towers = UnityEngine.Object.FindObjectsByType<RougeDefenseTower>(FindObjectsSortMode.None);
        for (int i = 0; i < towers.Length; i++)
        {
            RougeDefenseTower tower = towers[i];
            if (tower == null) continue;
            if (map != null && (!map.WorldToCell(tower.transform.position, out Vector2Int cell) ||
                                !map.IsTowerPlace(cell) || !occupiedCells.Add(cell)))
            {
                Debug.LogWarning($"Tower Defense removed '{tower.name}' because map cell placement now allows exactly one tower per build cell.", tower);
                Destroy(tower.gameObject);
                continue;
            }
            if (map != null && map.WorldToCell(tower.transform.position, out Vector2Int snappedCell))
                tower.transform.position = map.CellCenter(snappedCell, tower.transform.position.y);
            tower.Ensure2DVisual();
            _defenseTowers.Add(tower);
        }

        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        if (loader != null)
        {
            // Rebuild permanent A2 terrain before charge-tower overrides so removing a
            // charge tower later correctly reveals the underlying frost conversion.
            for (int i = 0; i < _defenseTowers.Count; i++)
            {
                RougeDefenseTower source = _defenseTowers[i];
                if (source == null || !source.CreatesPermanentFrostTiles || map == null ||
                    !map.WorldToCell(source.transform.position, out Vector2Int center)) continue;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        loader.SetPermanentTowerPlaceEffect(center + new Vector2Int(x, y),
                            RougeTowerPlaceEffect.Frost);
                    }
                }
            }
            for (int i = 0; i < _defenseTowers.Count; i++)
            {
                RougeDefenseTower tower = _defenseTowers[i];
                if (tower != null && tower.HasChargeTargetCell &&
                    tower.ChargedTileEffect != RougeTowerPlaceEffect.None)
                    loader.TrySetRuntimeTowerPlaceEffect(tower.ChargeTargetCell,
                        tower.ChargedTileEffect);
            }
        }

        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null) continue;
            tower.ApplyTowerPlaceEffect(tower.IsChargeTower
                ? RougeTowerPlaceEffect.None
                : GetTowerPlaceEffectAtWorld(tower.transform.position), true);
            tower.name = tower.DisplayName + " Lv." + tower.Level;
        }
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower != null && tower.CreatesPermanentFrostTiles && tower.IsOnFrostTile)
                ApplyPermanentFrostAroundIceTower(tower);
        }
    }

    private void RefreshReinforcementTowerAuras()
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            if (_defenseTowers[i] != null) _defenseTowers[i].SetReinforcementAuraLevel(0);
        }
        if (map == null) return;

        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || !map.WorldToCell(tower.transform.position, out Vector2Int cell))
                continue;
            int auraLevel = 0;
            for (int sourceIndex = 0; sourceIndex < _defenseTowers.Count; sourceIndex++)
            {
                RougeDefenseTower source = _defenseTowers[sourceIndex];
                if (source == null || !source.IsReinforcementTower ||
                    !map.WorldToCell(source.transform.position, out Vector2Int sourceCell))
                    continue;
                int distance = Mathf.Max(Mathf.Abs(cell.x - sourceCell.x),
                    Mathf.Abs(cell.y - sourceCell.y));
                if (distance <= source.ReinforcementAuraRangeCells)
                    auraLevel += source.ReinforcementAuraBuffLevel;
            }
            tower.SetReinforcementAuraLevel(auraLevel);
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
        int immediatelyAvailableGold = 0;
        if (_towerDefenseGoldEarned.IsCreated)
        {
            int earned = ApplyTowerDefenseLevelEventGoldMultiplier(
                Mathf.Max(0, _towerDefenseGoldEarned[0]));
            immediatelyAvailableGold += earned;
            _towerDefenseGoldEarned[0] = 0;
        }
        if (_towerDefenseWealthGoldEarned.IsCreated)
        {
            RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
            RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
            for (int cellIndex = 0; cellIndex < _towerDefenseWealthGoldEarned.Length; cellIndex++)
            {
                int earned = ApplyTowerDefenseLevelEventGoldMultiplier(
                    Mathf.Max(0, _towerDefenseWealthGoldEarned[cellIndex]));
                if (earned <= 0) continue;
                _towerDefenseWealthGoldEarned[cellIndex] = 0;
                Vector2Int cell = DecodeTowerDefenseMapCellIndex(cellIndex);
                bool stillAccumulates = map != null && map.Contains(cell) && loader != null &&
                    loader.GetEffectiveTowerPlaceEffect(cell) ==
                    RougeTowerPlaceEffect.AccumulatedWealth;
                if (!stillAccumulates)
                {
                    immediatelyAvailableGold = AddGoldWithoutOverflow(
                        immediatelyAvailableGold, earned);
                    continue;
                }
                _accumulatedWealthPendingGold[cellIndex] = AddGoldWithoutOverflow(
                    _accumulatedWealthPendingGold[cellIndex], earned);
                if (_accumulatedWealthPayoutTimers[cellIndex] <= 0f)
                    _accumulatedWealthPayoutTimers[cellIndex] =
                        AccumulatedWealthPayoutInterval;
            }
        }
        if (immediatelyAvailableGold > 0)
        {
            _towerDefenseGold = AddGoldWithoutOverflow(_towerDefenseGold,
                immediatelyAvailableGold);
            _towerDefenseGoldEarnedTotal = AddGoldWithoutOverflow(
                _towerDefenseGoldEarnedTotal, immediatelyAvailableGold);
        }
        _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - kills);
        RefreshTowerDefenseUi();
    }

    private static int AddGoldWithoutOverflow(int current, int addition)
    {
        long total = (long)Mathf.Max(0, current) + Mathf.Max(0, addition);
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static int EncodeTowerDefenseMapCellIndex(Vector2Int cell)
    {
        if ((uint)cell.x >= RougeTowerDefenseMap.MaxMapCells ||
            (uint)cell.y >= RougeTowerDefenseMap.MaxMapCells)
            return -1;
        return cell.y * RougeTowerDefenseMap.MaxMapCells + cell.x;
    }

    private static Vector2Int DecodeTowerDefenseMapCellIndex(int index)
    {
        return new Vector2Int(index % RougeTowerDefenseMap.MaxMapCells,
            index / RougeTowerDefenseMap.MaxMapCells);
    }

    private int GetTowerWealthCellIndexPlusOne(RougeDefenseTower tower)
    {
        if (tower == null || tower.TowerPlaceEffect !=
            RougeTowerPlaceEffect.AccumulatedWealth)
            return 0;
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null || !map.WorldToCell(tower.transform.position, out Vector2Int cell))
            return 0;
        int cellIndex = EncodeTowerDefenseMapCellIndex(cell);
        return cellIndex >= 0 ? cellIndex + 1 : 0;
    }

    private void UpdateAccumulatedWealthTiles(float dt)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        if (map == null || loader == null) return;
        float elapsed = Mathf.Max(0f, dt);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                int cellIndex = EncodeTowerDefenseMapCellIndex(cell);
                if (loader.GetEffectiveTowerPlaceEffect(cell) !=
                    RougeTowerPlaceEffect.AccumulatedWealth)
                {
                    _accumulatedWealthPayoutTimers[cellIndex] = 0f;
                    continue;
                }
                if (_accumulatedWealthPayoutTimers[cellIndex] <= 0f)
                    _accumulatedWealthPayoutTimers[cellIndex] =
                        AccumulatedWealthPayoutInterval;
                _accumulatedWealthPayoutTimers[cellIndex] -= elapsed;
                if (_accumulatedWealthPayoutTimers[cellIndex] > 0f) continue;
                SettleAccumulatedWealthCell(cellIndex);
                _accumulatedWealthPayoutTimers[cellIndex] +=
                    AccumulatedWealthPayoutInterval;
                if (_accumulatedWealthPayoutTimers[cellIndex] <= 0f)
                    _accumulatedWealthPayoutTimers[cellIndex] =
                        AccumulatedWealthPayoutInterval;
            }
        }
    }

    private int SettleAccumulatedWealthCell(int cellIndex)
    {
        if ((uint)cellIndex >= (uint)_accumulatedWealthPendingGold.Length) return 0;
        int pending = Mathf.Max(0, _accumulatedWealthPendingGold[cellIndex]);
        _accumulatedWealthPendingGold[cellIndex] = 0;
        if (pending <= 0) return 0;
        int payout = pending >= int.MaxValue / 2
            ? int.MaxValue
            : Mathf.CeilToInt(pending * AccumulatedWealthPayoutMultiplier);
        _towerDefenseGold = AddGoldWithoutOverflow(_towerDefenseGold, payout);
        _towerDefenseGoldEarnedTotal = AddGoldWithoutOverflow(
            _towerDefenseGoldEarnedTotal, payout);
        SpawnAccumulatedWealthPayoutText(cellIndex, payout);
        return payout;
    }

    private void DrainAccumulatedWealthNativeBucketForCell(int cellIndex)
    {
        if (!_towerDefenseWealthGoldEarned.IsCreated ||
            (uint)cellIndex >= (uint)_towerDefenseWealthGoldEarned.Length)
            return;
        int earned = ApplyTowerDefenseLevelEventGoldMultiplier(
            Mathf.Max(0, _towerDefenseWealthGoldEarned[cellIndex]));
        _towerDefenseWealthGoldEarned[cellIndex] = 0;
        if (earned > 0)
            _accumulatedWealthPendingGold[cellIndex] = AddGoldWithoutOverflow(
                _accumulatedWealthPendingGold[cellIndex], earned);
    }

    private void SpawnAccumulatedWealthPayoutText(int cellIndex, int payout)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null || payout <= 0) return;
        Vector2Int cell = DecodeTowerDefenseMapCellIndex(cellIndex);
        if (!map.Contains(cell)) return;
        Vector3 center = map.CellCenter(cell, renderHeight + 2.1f);
        RougeFloatingWorldText.Create($"金币 +{payout}", center,
            new Color(1f, 0.78f, 0.12f, 1f), GetTowerDefenseHudFont());
    }

    private void RemoveTowerDefenseAliveEstimate(int count)
    {
        if (!_towerDefenseInitialized || count <= 0) return;
        _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - count);
    }

    private void ApplyMainTowerContactDamage()
    {
        if (!_towerDefenseInitialized) return;
        if (_bossReachedGoalCount.IsCreated && _bossReachedGoalCount[0] > 0)
        {
            _bossReachedGoalCount[0] = 0;
            if (_mainTowerDamageCount.IsCreated) _mainTowerDamageCount[0] = 0;
            _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - 1);
            TriggerTowerDefenseGameOver("首领突破了主塔防线");
            return;
        }
        if (!_mainTowerDamageCount.IsCreated || mainTower == null) return;
        int contacts = _mainTowerDamageCount[0];
        if (contacts <= 0) return;
        _mainTowerDamageCount[0] = 0;
        _towerDefenseAliveEstimate = Mathf.Max(0, _towerDefenseAliveEstimate - contacts);
        float healthBeforeDamage = mainTower.CurrentHealth;
        if (mainTower.ApplyEnemyContacts(contacts)) _pendingMainTowerAoe = true;
        NotifyTowerDefenseAutoplayMainTowerDamaged(
            Mathf.Max(0f, healthBeforeDamage - mainTower.CurrentHealth));
        RefreshTowerDefenseUi();
    }

    private bool IsTowerDefenseSimulationPaused()
    {
        return _towerDefenseInitialized &&
               (_towerDefenseGameOver || _towerDefenseStartupActive ||
                IsCameraViewTransitionPaused || IsPlayerSettingsOpen);
    }

    private void UpdateTowerDefenseInput(float unscaledDt)
    {
        if (!_towerDefenseInitialized) return;
        UpdateF2MainTowerHealth(unscaledDt);
        if (_towerDefenseStartupActive) return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (_towerDefenseGameOver && !_towerDefenseVictory)
        {
            if (!_towerDefenseFailureSequenceActive &&
                _towerDefenseFailureResultReady && keyboard != null &&
                keyboard.rKey.wasPressedThisFrame)
                ReloadTowerDefenseScene();
            return;
        }
        if (HandleTowerDefenseAutoplayToggleInput(keyboard)) return;
        SyncTowerDefenseAutoplayPresentation();
        if (IsPlayerSettingsOpen)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                ClosePlayerSettings();
            return;
        }
        if (UpdateCameraViewTransition()) return;
        if (_towerDefenseAutoplayEnabled)
        {
            HideF2MainTowerHealth();
            if (HandleTowerDefenseAutoplayCleanViewInput(keyboard)) return;
            if (HandleTowerDefenseAutoplaySpeedInput(keyboard)) return;
            if (!IsTiltShiftObservationActive)
            {
                SetCameraViewMode(CameraViewMode.TiltShift);
                return;
            }
            if (!_tiltShiftObservationExiting && keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                OpenPlayerSettings();
                SyncTowerDefenseAutoplayPresentation();
                return;
            }

            UpdateTowerDefenseAutoplay(Time.deltaTime);
            return;
        }
        if (IsTiltShiftObservationActive)
        {
            if (!_tiltShiftObservationExiting && keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                OpenPlayerSettings();
                return;
            }
            if (UpdateCameraViewInput(keyboard)) return;
            if (!_tiltShiftObservationExiting && mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                RougeDefenseTower hovered = RaycastDefenseTower();
                if (hovered != null)
                    BeginTiltShiftObservationExit(CameraViewMode.Default, hovered);
                else
                    ShowF2MainTowerHealth();
            }
            return;
        }
#if UNITY_EDITOR
        if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
        {
            _towerDefenseGold = _towerDefenseGold > int.MaxValue - 1000
                ? int.MaxValue
                : _towerDefenseGold + 1000;
            RefreshTowerDefenseUi(true);
        }
#endif
        if (_towerDefenseGameOver)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                OpenPlayerSettings();
                return;
            }
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                ReloadTowerDefenseScene();
            return;
        }

        if (_chargeTowerTargetSelectionActive)
        {
            ApplyTowerDefenseTimeScale();
            UpdateChargeTowerTargetSelection();
            bool targetPointerOverUi = EventSystem.current != null &&
                                       EventSystem.current.IsPointerOverGameObject();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelPendingChargeTowerConstruction();
                return;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !targetPointerOverUi &&
                _pendingChargeTowerTargetValid)
                ConfirmChargeTowerTargetSelection();
            return;
        }

        if (_chargeTowerEffectSelectionActive)
        {
            ApplyTowerDefenseTimeScale();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                CancelPendingChargeTowerConstruction();
            return;
        }

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame &&
            !_towerPlacementMode)
        {
            OpenPlayerSettings();
            return;
        }

        // Tower/build state is authoritative for simulation speed. Keeping this
        // synchronized also clears a stale 0.5 scale after any exit path.
        ApplyTowerDefenseTimeScale();

        if (keyboard != null && keyboard.f10Key.wasPressedThisFrame && !_towerPlacementMode)
        {
            _towerDefenseDoubleSpeed = !_towerDefenseDoubleSpeed;
            ApplyTowerDefenseTimeScale();
            RefreshTowerDefenseUi(true);
        }

        if (UpdateCameraViewInput(keyboard)) return;

        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool debugCameraLooking = _debugUnitViewMode && mouse != null && mouse.rightButton.isPressed;
        if (CommanderSkillsEnabled && !debugCameraLooking && UpdateTacticalSkillInput(mouse, pointerOverUi)) return;
        if (!_towerPlacementMode)
        {
            if (!debugCameraLooking && mouse != null && mouse.leftButton.wasPressedThisFrame && !pointerOverUi)
            {
                RougeDefenseTower hovered = RaycastDefenseTower();
                if (hovered != null) EnterTowerEditMode(hovered);
            }
            return;
        }

        if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
        {
            _showAllTowerAttackRanges = !_showAllTowerAttackRanges;
            RefreshTowerEditHints();
            RefreshTowerDefenseUi(true);
        }

        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            if (_towerRelocationActive) CancelTowerRelocation();
            else SetTowerPlacementMode(false);
            return;
        }

        UpdateSelectedTowerMiddleClick(mouse, pointerOverUi);

        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Ice);
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.MachineGun);
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Cannon);
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Flame);
            if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.Laser);
            if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.PiercingLaser);
            if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.OrbitSphere);
            if (keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame) SelectTowerBuildType(RougeTowerType.RocketBarrage);
            if (keyboard.cKey.wasPressedThisFrame) BeginChargeTowerBuild();
            if (keyboard.vKey.wasPressedThisFrame) BeginReinforcementTowerBuild();
            if (keyboard.uKey.wasPressedThisFrame) TryUpgradeSelectedTower();
            if (keyboard.rKey.wasPressedThisFrame) BeginSelectedTowerRelocation();
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_towerRelocationActive) CancelTowerRelocation();
                else SetTowerPlacementMode(false);
                return;
            }
        }

        UpdateTowerPreview();
        if (mouse == null || debugCameraLooking) return;

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

    private void UpdateSelectedTowerMiddleClick(Mouse mouse, bool pointerOverUi)
    {
        if (mouse == null)
        {
            _towerMiddleClickPending = false;
            _towerMiddleClickTarget = null;
            return;
        }

        Vector2 pointer = mouse.position.ReadValue();
        if (mouse.middleButton.wasPressedThisFrame)
        {
            _towerMiddleClickTarget = !pointerOverUi && _selectedTower != null &&
                _selectedTower.IsTargetedDamage ? _selectedTower : null;
            _towerMiddleClickPending = _towerMiddleClickTarget != null;
            _towerMiddleClickStartPosition = pointer;
        }
        if (_towerMiddleClickPending && mouse.middleButton.isPressed &&
            (pointer - _towerMiddleClickStartPosition).sqrMagnitude >
            TowerMiddleClickDragThreshold * TowerMiddleClickDragThreshold)
        {
            _towerMiddleClickPending = false;
        }
        if (!mouse.middleButton.wasReleasedThisFrame) return;

        RougeDefenseTower target = _towerMiddleClickPending ? _towerMiddleClickTarget : null;
        _towerMiddleClickPending = false;
        _towerMiddleClickTarget = null;
        if (target == null || target != _selectedTower || pointerOverUi) return;
        ToggleSelectedTowerTargetPriority();
    }

    private void SetTowerPlacementMode(bool enabled)
    {
        _towerPlacementMode = enabled;
        TowerDefenseBuildModeActive = enabled;
        if (!enabled)
        {
            SetTowerPlacementHoveredTower(null);
            _towerMiddleClickPending = false;
            _towerMiddleClickTarget = null;
        }
        SetTowerPlaceVisualsVisible(enabled);
        ApplyTowerDefenseTimeScale();

        if (enabled)
        {
            if (_towerBuildSelectionActive) SelectTowerBuildType(_selectedBuildType);
        }
        else
        {
            ClearTacticalSkillSelection();
            ClearTowerRelocationState();
            _suppressRepeatedPreviewAtPlacedCell = false;
            _chargeTowerBuildSelectionActive = false;
            _reinforcementTowerBuildSelectionActive = false;
            if (_towerPreview != null) Destroy(_towerPreview.gameObject);
            _towerPreview = null;
            SelectPlacedTower(null);
        }
        RefreshTowerEditHints();
        RefreshTowerDefenseUi();
    }

    private float GetTowerDefensePlayTimeScale()
    {
        return _towerDefenseDoubleSpeed ? 2f : 1f;
    }

    private void ApplyTowerDefenseTimeScale()
    {
        if (_towerDefenseGameOver || _towerDefenseStartupActive ||
            IsCameraViewTransitionPaused || IsPlayerSettingsOpen)
        {
            Time.timeScale = 0f;
            return;
        }
        Time.timeScale = _chargeTowerTargetSelectionActive || _chargeTowerEffectSelectionActive
            ? 0f
            : _towerPlacementMode
            ? 0.5f
            : GetTowerDefensePlayTimeScale();
    }

    private void EnterTowerEditMode(RougeDefenseTower tower)
    {
        if (tower == null) return;
        ClearTowerRelocationState();
        _towerBuildSelectionActive = false;
        _chargeTowerBuildSelectionActive = false;
        _reinforcementTowerBuildSelectionActive = false;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        SetTowerPlacementMode(true);
        SelectPlacedTower(tower);
    }

    private void SelectTowerBuildType(RougeTowerType type)
    {
        ClearTowerRelocationState();
        _chargeTowerBuildSelectionActive = false;
        _reinforcementTowerBuildSelectionActive = false;
        if (IsTowerTypeDisabled(type))
        {
            _towerBuildSelectionActive = false;
            _previewValid = false;
            if (_towerPreview != null) Destroy(_towerPreview.gameObject);
            _towerPreview = null;
            RefreshTowerDefenseUi();
            return;
        }
        _selectedBuildType = type;
        _towerBuildSelectionActive = true;
        if (!_towerPlacementMode) return;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        GameObject go = InstantiateTowerPrefab(type);
        if (go == null)
        {
            _towerPreview = null;
            _towerBuildSelectionActive = false;
            SetTowerPlaceVisualsVisible(true);
            RefreshTowerDefenseUi();
            return;
        }
        // Prefabs are instantiated at the world origin. Keep the preview hidden until
        // UpdateTowerPreview has a valid mouse hit, otherwise the tower and its range
        // rings are visible at (0, 0, 0) for one frame.
        go.SetActive(false);
        go.name = "Tower Preview - " + TowerDefenseVisuals.GetTowerName(type);
        _towerPreview = go.GetComponent<RougeDefenseTower>();
        _towerPreview.Configure(type, true);
        // Clear the previous tower's grid footprint until this preview receives
        // its first valid pointer position in UpdateTowerPreview.
        SetTowerPlaceVisualsVisible(true);
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
        if (tower.TowerType != type)
        {
            Debug.LogError($"Tower prefab at Resources/{resourcePath} has tower type " +
                           $"{tower.TowerType}, expected {type}. Tower creation was cancelled.");
            return null;
        }

        GameObject instance = Instantiate(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return instance;
    }

    private void BeginTowerBuild(RougeTowerType type)
    {
        if (IsTowerTypeDisabled(type)) return;
        if (_towerDefenseGameOver || !CanAffordTowerType(type)) return;
        if (!_towerPlacementMode)
        {
            _towerBuildSelectionActive = false;
            SetTowerPlacementMode(true);
        }
        SelectTowerBuildType(type);
    }

    private void BeginChargeTowerBuild()
    {
        if (_towerDefenseGameOver || !CanAffordTowerType(RougeTowerType.ChargeTower)) return;
        ClearTacticalSkillSelection();
        ClearTowerRelocationState();
        _reinforcementTowerBuildSelectionActive = false;
        if (!_towerPlacementMode)
        {
            _towerBuildSelectionActive = false;
            SetTowerPlacementMode(true);
        }
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);

        GameObject go = InstantiateTowerPrefab(RougeTowerType.ChargeTower);
        if (go == null)
        {
            _chargeTowerBuildSelectionActive = false;
            RefreshTowerDefenseUi(true);
            return;
        }
        go.SetActive(false);
        go.name = "Tower Preview - 充能塔";
        _towerPreview = go.GetComponent<RougeDefenseTower>();
        _towerPreview.ConfigureAsChargeTower(true);
        _towerBuildSelectionActive = false;
        _chargeTowerBuildSelectionActive = true;
        _previewValid = false;
        SetTowerPlaceVisualsVisible(true);
        SelectPlacedTower(null);
        RefreshTowerDefenseUi(true);
    }

    private void BeginReinforcementTowerBuild()
    {
        if (_towerDefenseGameOver ||
            !CanAffordTowerType(RougeTowerType.ReinforcementTower)) return;
        ClearTacticalSkillSelection();
        ClearTowerRelocationState();
        _chargeTowerBuildSelectionActive = false;
        if (!_towerPlacementMode)
        {
            _towerBuildSelectionActive = false;
            SetTowerPlacementMode(true);
        }
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);

        GameObject go = InstantiateTowerPrefab(RougeTowerType.ReinforcementTower);
        if (go == null)
        {
            _reinforcementTowerBuildSelectionActive = false;
            RefreshTowerDefenseUi(true);
            return;
        }
        go.SetActive(false);
        go.name = "Tower Preview - 强化塔";
        _towerPreview = go.GetComponent<RougeDefenseTower>();
        _towerPreview.ConfigureAsReinforcementTower(true);
        _towerPreview.SetReinforcementTowerPlacementCost(GetReinforcementTowerGoldCost());
        _towerBuildSelectionActive = false;
        _reinforcementTowerBuildSelectionActive = true;
        _previewValid = false;
        SetTowerPlaceVisualsVisible(true);
        SelectPlacedTower(null);
        RefreshTowerDefenseUi(true);
    }

    private void CancelTowerBuildSelection()
    {
        if (_chargeTowerTargetSelectionActive || _chargeTowerEffectSelectionActive)
        {
            CancelPendingChargeTowerConstruction();
            return;
        }
        if (HasTacticalSkillSelection)
        {
            CancelTacticalSkillSelection(false);
            return;
        }
        if (_towerRelocationActive)
        {
            CancelTowerRelocation();
            return;
        }
        _towerBuildSelectionActive = false;
        _chargeTowerBuildSelectionActive = false;
        _reinforcementTowerBuildSelectionActive = false;
        _previewValid = false;
        SetTowerPlacementHoveredTower(null);
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        SelectPlacedTower(null);
        RefreshTowerDefenseUi();
    }

    private void BeginSelectedTowerRelocation()
    {
        RougeDefenseTower tower = _selectedTower;
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (_towerRelocationActive || _towerDefenseGameOver || tower == null || !tower.CanRelocate || map == null ||
            _towerDefenseGold < tower.RelocationCost) return;

        if (!map.WorldToCell(tower.transform.position, out _)) return;
        ClearTacticalSkillSelection();
        ClearTowerRelocationState();
        _towerBuildSelectionActive = false;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;

        GameObject go = InstantiateTowerPrefab(tower.TowerType);
        if (go == null)
        {
            RefreshTowerDefenseUi(true);
            return;
        }

        go.SetActive(false);
        go.name = "Tower Relocation Preview - " + tower.DisplayName;
        _towerPreview = go.GetComponent<RougeDefenseTower>();
        _towerPreview.Configure(tower.TowerType, true);
        _relocatingTower = tower;
        _towerRelocationActive = true;
        map.WorldToCell(tower.transform.position, out _relocationOriginalAnchor);
        tower.SetRangeVisibility(false);
        _previewValid = false;
        _previewCellValidity = null;
        SetTowerPlaceVisualsVisible(true);
        RefreshTowerDefenseUi(true);
    }

    private void CancelTowerRelocation()
    {
        RougeDefenseTower tower = _relocatingTower;
        ClearTowerRelocationState();
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        SelectPlacedTower(tower);
        RefreshTowerDefenseUi(true);
    }

    private void ClearTowerRelocationState()
    {
        if (!_towerRelocationActive && _relocatingTower == null) return;
        RougeDefenseTower tower = _relocatingTower;
        if (_towerPreview != null) Destroy(_towerPreview.gameObject);
        _towerPreview = null;
        _towerRelocationActive = false;
        _relocatingTower = null;
        _relocationOriginalAnchor = default;
        _previewValid = false;
        _previewCellValidity = null;
        if (tower != null && tower == _selectedTower)
            tower.SetRangeVisibility(_towerPlacementMode);
    }

    private void CompleteTowerRelocation()
    {
        RougeDefenseTower tower = _relocatingTower;
        if (tower == null || _towerPreview == null) return;
        int cost = tower.RelocationCost;
        if (_towerDefenseGold < cost) return;

        Vector3 destination = _towerPreview.transform.position;
        RougeTowerPlaceEffect destinationEffect = _towerPreview.TowerPlaceEffect;
        _towerDefenseGold -= cost;
        RecordTowerDefenseGoldSpent(cost);
        StopPiercingLaserAttacksForTower(tower);
        StopOrbitSphereAttacksForTower(tower);
        tower.StopAttackSounds();
        tower.transform.position = destination;
        tower.FinalizeRelocation(destinationEffect);
        tower.PlayPlacementSound();
        PlayTowerConstructionEffect(tower);
        tower.name = tower.DisplayName + " Lv." + tower.Level;
        RefreshReinforcementTowerAuras();
        _towerTargetScheduledCount = 0;
        ClearTowerRelocationState();
        SetTowerPlaceVisualsVisible(true);
        SelectPlacedTower(tower);
        RefreshTowerDefenseUi(true);
    }

    private void UpdateTowerPreview()
    {
        if (_towerPreview == null)
        {
            SetTowerPlacementHoveredTower(null);
            return;
        }
        if (!TryGetTowerPlacementFromPointer(out Vector2Int anchor, out Vector3 position))
        {
            SetTowerPlacementHoveredTower(null);
            _towerPreview.gameObject.SetActive(false);
            _previewValid = false;
            SetTowerPlaceVisualsVisible(true);
            return;
        }

        // Continuous build remains active, but do not immediately draw the next
        // preview and range circle on top of the tower that just materialized.
        // Moving to another grid cell reveals the prepared preview again.
        if (_suppressRepeatedPreviewAtPlacedCell)
        {
            if (anchor == _lastPlacedTowerAnchor)
            {
                SetTowerPlacementHoveredTower(null);
                _towerPreview.gameObject.SetActive(false);
                _previewValid = false;
                SetTowerPlaceVisualsVisible(true);
                return;
            }
            _suppressRepeatedPreviewAtPlacedCell = false;
        }

        _towerPreview.gameObject.SetActive(true);
        _previewTowerAnchor = anchor;
        _towerPreview.transform.position = position;
        if (_towerPreview.IsChargeTower)
        {
            _towerPreview.SetChargeTowerPlacementCost(GetChargeTowerGoldCost());
        }
        else if (_towerPreview.IsReinforcementTower)
        {
            _towerPreview.SetReinforcementTowerPlacementCost(GetReinforcementTowerGoldCost());
        }
        RougeDefenseTower ignoredTower = _towerRelocationActive ? _relocatingTower : _towerPreview;
        _previewCellValidity = GetTowerFootprintCellValidity(anchor,
            _towerPreview.FootprintCells, ignoredTower);
        _previewValid = CanPlacePreviewTower();
        RougeDefenseTower occupiedTower = FindPlacedTowerInCell(anchor, ignoredTower);
        _towerPreview.SetPreviewState(_previewValid, _previewCellValidity);
        if (occupiedTower != null)
            _towerPreview.SetRangeVisibility(false);
        SetTowerPlacementHoveredTower(occupiedTower);
        SetTowerPlaceVisualsVisible(true);
    }

    private RougeDefenseTower FindPlacedTowerInCell(Vector2Int cell,
        RougeDefenseTower ignoredTower)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null) return null;
        for (int i = _defenseTowers.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || tower == ignoredTower) continue;
            if (map.WorldToCell(tower.transform.position, out Vector2Int towerCell) &&
                towerCell == cell)
                return tower;
        }
        return null;
    }

    private void SetTowerPlacementHoveredTower(RougeDefenseTower tower)
    {
        RougeDefenseTower previous = _towerPlacementHoveredTower;
        if (previous != null && previous != tower)
        {
            bool keepPreviousRange = _towerPlacementMode &&
                                     (_showAllTowerAttackRanges || previous == _selectedTower);
            previous.SetRangeVisibility(keepPreviousRange);
        }

        _towerPlacementHoveredTower = tower;
        if (_towerPlacementHoveredTower != null)
            _towerPlacementHoveredTower.SetRangeVisibility(true);
    }

    private bool CanPlacePreviewTower()
    {
        if (_towerPreview == null || !_towerPreview.gameObject.activeInHierarchy ||
            !AreAllFootprintCellsValid(_previewCellValidity)) return false;
        if (!_towerRelocationActive && _towerPreview.IsChargeTower)
        {
            RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
            if (map == null || !map.WorldToCell(_towerPreview.transform.position,
                    out Vector2Int ownerCell) || !map.IsTowerPlace(ownerCell))
                return false;
            return _towerDefenseGold >= _towerPreview.PlacementCost;
        }
        if (!_towerRelocationActive) return _towerDefenseGold >= _towerPreview.PlacementCost;
        return _relocatingTower != null && _previewTowerAnchor != _relocationOriginalAnchor &&
            _towerDefenseGold >= _relocatingTower.RelocationCost;
    }

    private RougeTowerPlaceEffect GetTowerPlaceEffectAtWorld(Vector3 worldPosition)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null || !map.WorldToCell(worldPosition, out Vector2Int centerCell))
            return RougeTowerPlaceEffect.None;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        return loader != null
            ? loader.GetEffectiveTowerPlaceEffect(centerCell)
            : map.GetTowerPlaceEffect(centerCell);
    }

    private bool TryGetPointerGroundPosition(out Vector3 worldPosition)
    {
        worldPosition = default;
        Camera camera = RougeCameraFollow.ResolveCamera();
        if (camera == null || Mouse.current == null) return false;
        Vector2 pointer = Mouse.current.position.ReadValue();
        Ray ray = camera.ScreenPointToRay(pointer);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (!groundPlane.Raycast(ray, out float distance) || distance < 0f) return false;
        worldPosition = ray.GetPoint(distance);
        return true;
    }

    private bool TryGetTowerPlacementFromPointer(out Vector2Int anchor, out Vector3 snappedPosition)
    {
        anchor = default;
        snappedPosition = default;
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null || _towerPreview == null || !TryGetPointerGroundPosition(out Vector3 worldPosition)) return false;
        if (!map.WorldToCell(worldPosition, out Vector2Int cell)) return false;
        snappedPosition = map.CellCenter(cell, 0.05f);
        _towerPreview.ApplyTowerPlaceEffect(_towerPreview.IsChargeTower
            ? RougeTowerPlaceEffect.None
            : GetTowerPlaceEffectAtWorld(snappedPosition));
        _towerPreview.SetReinforcementAuraLevel(
            GetReinforcementAuraLevelAtCell(map, cell));
        anchor = cell;
        return true;
    }

    private static bool AreAllFootprintCellsValid(bool[] validity)
    {
        if (validity == null || validity.Length == 0) return false;
        for (int i = 0; i < validity.Length; i++) if (!validity[i]) return false;
        return true;
    }

    private RougeDefenseTower RaycastDefenseTower()
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null || !TryGetPointerGroundPosition(out Vector3 worldPosition) ||
            !map.WorldToCell(worldPosition, out Vector2Int pointerCell)) return null;
        for (int i = _defenseTowers.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || tower == _towerPreview) continue;
            if (map.WorldToCell(tower.transform.position, out Vector2Int towerCell) &&
                towerCell == pointerCell)
                return tower;
        }
        return null;
    }

    private bool[] GetTowerFootprintCellValidity(Vector2Int candidateAnchor, Vector2Int candidateSize,
        RougeDefenseTower ignoredTower)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        bool[] validity = new bool[1];
        if (map == null) return validity;
        validity[0] = map.IsTowerPlace(candidateAnchor);

        for (int i = _defenseTowers.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null)
            {
                _defenseTowers.RemoveAt(i);
                continue;
            }
            if (tower == ignoredTower) continue;
            if (map.WorldToCell(tower.transform.position, out Vector2Int towerCell) &&
                towerCell == candidateAnchor)
                validity[0] = false;
        }
        if (mainTower != null &&
            map.WorldToCell(mainTower.transform.position, out Vector2Int mainTowerCell) &&
            mainTowerCell == candidateAnchor)
            validity[0] = false;
        return validity;
    }

    private void PlacePreviewTower()
    {
        if (!CanPlacePreviewTower())
        {
            _previewValid = false;
            if (_towerPreview != null) _towerPreview.SetPreviewState(false, _previewCellValidity);
            return;
        }
        if (_towerRelocationActive)
        {
            CompleteTowerRelocation();
            return;
        }
        if (_towerPreview.IsChargeTower)
        {
            BeginChargeTowerEffectSelection();
            return;
        }
        int cost = _towerPreview.PlacementCost;
        _towerDefenseGold -= cost;
        RecordTowerDefenseGoldSpent(cost);
        _towerPreview.FinalizePlacement();
        _towerPreview.name = _towerPreview.DisplayName + " Lv." + _towerPreview.Level;
        bool placedReinforcementTower = _towerPreview.IsReinforcementTower;
        RougeDefenseTower placed = _towerPreview;
        _lastPlacedTowerAnchor = _previewTowerAnchor;
        _suppressRepeatedPreviewAtPlacedCell = true;
        _defenseTowers.Add(placed);
        placed.PlayPlacementSound();
        PlayTowerConstructionEffect(placed);
        RefreshReinforcementTowerAuras();
        SetTowerPlaceVisualsVisible(true);
        _towerPreview = null;
        _towerTargetScheduledCount = 0;
        if (placedReinforcementTower)
        {
            bool canRepeatReinforcement =
                _towerDefenseGold >= GetReinforcementTowerGoldCost();
            if (canRepeatReinforcement)
                BeginReinforcementTowerBuild();
            else
            {
                _reinforcementTowerBuildSelectionActive = false;
                _previewValid = false;
                SetTowerPlaceVisualsVisible(true);
                SelectPlacedTower(placed);
            }
            RefreshTowerDefenseUi();
            return;
        }
        bool canRepeatSelectedType = CanAffordTowerType(_selectedBuildType);
        bool canAffordAnyTower = canRepeatSelectedType || CanAffordAnyTowerType();
        if (canRepeatSelectedType)
        {
            SelectTowerBuildType(_selectedBuildType);
        }
        else
        {
            // Keep edit mode active while cancelling the current build preparation.
            _towerBuildSelectionActive = false;
            _previewValid = false;
            SetTowerPlaceVisualsVisible(true);
            SelectPlacedTower(canAffordAnyTower ? null : placed);
        }
        RefreshTowerDefenseUi();
    }

    private void BeginChargeTowerEffectSelection()
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (_towerPreview == null || !_towerPreview.IsChargeTower || map == null ||
            !map.WorldToCell(_towerPreview.transform.position, out _)) return;
        int cost = GetChargeTowerGoldCost();
        _towerPreview.SetChargeTowerPlacementCost(cost);
        if (_towerDefenseGold < cost) return;

        _towerDefenseGold -= cost;
        _pendingChargeTowerEscrow = cost;
        _pendingChargeTowerCell = default;
        _pendingChargeTowerTargetValid = false;
        _pendingChargeTower = _towerPreview;
        _towerPreview = null;
        _towerBuildSelectionActive = false;
        _chargeTowerBuildSelectionActive = false;
        _chargeTowerTargetSelectionActive = true;
        _chargeTowerEffectSelectionActive = false;
        _chargeTowerRefreshCount = 0;
        _previewValid = false;
        _previewCellValidity = null;
        StopAllTowerAttackSounds();
        SetTowerPlaceVisualsVisible(true);
        ApplyTowerDefenseTimeScale();
        RefreshTowerDefenseUi(true);
    }

    private void UpdateChargeTowerTargetSelection()
    {
        _pendingChargeTowerTargetValid = false;
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        if (map != null && loader != null && TryGetPointerGroundPosition(out Vector3 position) &&
            map.WorldToCell(position, out Vector2Int cell))
        {
            _pendingChargeTowerCell = cell;
            bool adjacent = _pendingChargeTower != null &&
                map.WorldToCell(_pendingChargeTower.transform.position,
                    out Vector2Int chargeTowerCell) &&
                Mathf.Max(Mathf.Abs(cell.x - chargeTowerCell.x),
                    Mathf.Abs(cell.y - chargeTowerCell.y)) == 1;
            _pendingChargeTowerTargetValid = adjacent && map.IsTowerPlace(cell) &&
                !loader.TryGetRuntimeTowerPlaceEffect(cell, out _);
        }
        SetTowerPlaceVisualsVisible(true);
        RefreshTowerDefenseUi();
    }

    private void ConfirmChargeTowerTargetSelection()
    {
        if (!_chargeTowerTargetSelectionActive || !_pendingChargeTowerTargetValid ||
            _pendingChargeTower == null) return;
        _chargeTowerTargetSelectionActive = false;
        _chargeTowerEffectSelectionActive = true;
        RollChargeTowerEffectChoices();
        SetTowerPlaceVisualsVisible(false);
        if (_chargeTowerEffectSelectionPanel != null)
            _chargeTowerEffectSelectionPanel.transform.SetAsLastSibling();
        ApplyTowerDefenseTimeScale();
        RefreshTowerDefenseUi(true);
    }

    private void ConfirmChargeTowerEffect(int choiceIndex)
    {
        if (!_chargeTowerEffectSelectionActive || _pendingChargeTower == null ||
            choiceIndex < 0 || choiceIndex >= _chargeTowerEffectChoices.Length) return;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        RougeTowerPlaceEffect effect = _chargeTowerEffectChoices[choiceIndex];
        if (loader == null || !loader.TrySetRuntimeTowerPlaceEffect(_pendingChargeTowerCell, effect))
        {
            CancelPendingChargeTowerConstruction();
            return;
        }

        RougeDefenseTower placed = _pendingChargeTower;
        placed.SetChargeTarget(_pendingChargeTowerCell, effect);
        placed.FinalizePlacement();
        placed.name = placed.DisplayName;
        _defenseTowers.Add(placed);
        ApplyActivatedEffectToTowersInCell(_pendingChargeTowerCell, effect);
        placed.PlayPlacementSound();
        PlayTowerConstructionEffect(placed);
        RecordTowerDefenseGoldSpent(_pendingChargeTowerEscrow);
        _pendingChargeTower = null;
        _pendingChargeTowerEscrow = 0;
        _pendingChargeTowerTargetValid = false;
        _chargeTowerTargetSelectionActive = false;
        _chargeTowerEffectSelectionActive = false;
        _chargeTowerRefreshCount = 0;
        _towerTargetScheduledCount = 0;
        if (_chargeTowerEffectSelectionPanel != null)
            _chargeTowerEffectSelectionPanel.SetActive(false);
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        SelectPlacedTower(placed);
        ApplyTowerDefenseTimeScale();
        RefreshTowerDefenseUi(true);
    }

    private void ApplyActivatedEffectToTowersInCell(Vector2Int cell, RougeTowerPlaceEffect effect)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null) return;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || tower.IsChargeTower ||
                !map.WorldToCell(tower.transform.position, out Vector2Int towerCell) || towerCell != cell)
                continue;
            tower.ApplyActivatedTowerPlaceEffect(effect);
            tower.name = tower.DisplayName + " Lv." + tower.Level;
        }
        RefreshReinforcementTowerAuras();
    }

    private void CancelPendingChargeTowerConstruction()
    {
        if (!_chargeTowerTargetSelectionActive && !_chargeTowerEffectSelectionActive) return;
        _towerDefenseGold += Mathf.Max(0, _pendingChargeTowerEscrow);
        _pendingChargeTowerEscrow = 0;
        if (_pendingChargeTower != null) Destroy(_pendingChargeTower.gameObject);
        _pendingChargeTower = null;
        _pendingChargeTowerTargetValid = false;
        _chargeTowerTargetSelectionActive = false;
        _chargeTowerEffectSelectionActive = false;
        _chargeTowerBuildSelectionActive = false;
        _chargeTowerRefreshCount = 0;
        if (_chargeTowerEffectSelectionPanel != null)
            _chargeTowerEffectSelectionPanel.SetActive(false);
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        ApplyTowerDefenseTimeScale();
        RefreshTowerDefenseUi(true);
    }

    private void RefreshChargeTowerEffectChoices()
    {
        if (!_chargeTowerEffectSelectionActive) return;
        int refreshCost = GetChargeTowerRefreshGoldCost(_chargeTowerRefreshCount);
        if (_towerDefenseGold < refreshCost) return;
        _towerDefenseGold -= refreshCost;
        RecordTowerDefenseGoldSpent(refreshCost);
        _chargeTowerRefreshCount++;
        RollChargeTowerEffectChoices();
        RefreshTowerDefenseUi(true);
    }

    private static int GetChargeTowerRefreshGoldCost(int refreshCount)
    {
        if (refreshCount <= 0) return 0;
        if (refreshCount == 1) return 300;
        if (refreshCount == 2) return 450;
        if (refreshCount == 3) return 725;
        double cost = 725d;
        for (int i = 3; i < refreshCount; i++) cost = System.Math.Ceiling(cost * 1.5d);
        return (int)System.Math.Min(int.MaxValue, cost);
    }

    private void RollChargeTowerEffectChoices()
    {
        int[] indices = new int[ChargeTowerEffectPool.Length];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        for (int i = indices.Length - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            (indices[i], indices[swapIndex]) = (indices[swapIndex], indices[i]);
        }
        for (int i = 0; i < _chargeTowerEffectChoices.Length; i++)
            _chargeTowerEffectChoices[i] = ChargeTowerEffectPool[indices[i]];
    }

    private void SelectPlacedTower(RougeDefenseTower tower)
    {
        if (_selectedTower != null) _selectedTower.SetRangeVisibility(false);
        _selectedTower = tower;
        if (_selectedTower != null) _selectedTower.SetRangeVisibility(_towerPlacementMode);
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        RefreshTowerEditHints();
        RefreshTowerDefenseUi(true);
    }

    private void DeleteTower(RougeDefenseTower tower,
        float refundMultiplierOverride = -1f)
    {
        if (tower == null || !CanSellTower(tower)) return;
        float refundMultiplier = refundMultiplierOverride >= 0f
            ? Mathf.Clamp01(refundMultiplierOverride)
            : Mathf.Clamp01(towerBalance.sellRefundMultiplier);
        int refund = tower.AllowsSellRefund
            ? Mathf.FloorToInt(tower.InvestedGold * refundMultiplier)
            : 0;
        _towerDefenseGold += refund;
        if (tower.IsChargeTower)
        {
            RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
            if (loader != null && tower.HasChargeTargetCell)
            {
                Vector2Int chargedCell = tower.ChargeTargetCell;
                if (loader.TryGetRuntimeTowerPlaceEffect(chargedCell,
                        out RougeTowerPlaceEffect runtimeEffect) &&
                    runtimeEffect == RougeTowerPlaceEffect.AccumulatedWealth)
                {
                    int cellIndex = EncodeTowerDefenseMapCellIndex(chargedCell);
                    if (cellIndex >= 0)
                    {
                        DrainAccumulatedWealthNativeBucketForCell(cellIndex);
                        SettleAccumulatedWealthCell(cellIndex);
                    }
                }
                if (loader.ClearRuntimeTowerPlaceEffect(chargedCell))
                    ApplyActivatedEffectToTowersInCell(chargedCell,
                        loader.GetEffectiveTowerPlaceEffect(chargedCell));
            }
            tower.ClearChargeTarget();
        }
        _defenseTowers.Remove(tower);
        StopPiercingLaserAttacksForTower(tower);
        StopOrbitSphereAttacksForTower(tower);
        tower.StopAttackSounds();
        tower.PlaySellSound();
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        _towerTargetScheduledCount = 0;
        if (_selectedTower == tower) _selectedTower = null;
        Destroy(tower.gameObject);
        RefreshReinforcementTowerAuras();
        RefreshTowerDefenseUi();
    }

    private void SellSelectedTower()
    {
        if (_towerRelocationActive || _selectedTower == null || !CanSellTower(_selectedTower)) return;
        RougeDefenseTower tower = _selectedTower;
        DeleteTower(tower);
        SetTowerPlacementMode(false);
    }

    private bool CanSellTower(RougeDefenseTower tower)
    {
        return tower != null;
    }

    private void TryUpgradeSelectedTower()
    {
        if (_towerRelocationActive || _selectedTower == null || !_selectedTower.CanUpgrade ||
            _selectedTower.RequiresUpgradeChoice) return;
        int cost = _selectedTower.UpgradeCost;
        if (_towerDefenseGold < cost) return;
        if (!_selectedTower.Upgrade()) return;
        _towerDefenseGold -= cost;
        RecordTowerDefenseGoldSpent(cost);
        PlayTowerUpgradeFeedback(_selectedTower);
        _selectedTower.name = _selectedTower.DisplayName + " Lv." + _selectedTower.Level;
        _selectedTower.SetRangeVisibility(_towerPlacementMode);
        RefreshTowerDefenseUi(true);
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

    private void TryUpgradeSelectedTowerPrimaryButton()
    {
        if (_selectedTower != null && _selectedTower.RequiresUpgradeChoice)
            TryUpgradeSelectedTowerChoice(0);
        else
            TryUpgradeSelectedTower();
    }

    private void TryUpgradeSelectedTowerChoice(int choiceIndex)
    {
        if (_towerRelocationActive || _selectedTower == null ||
            !_selectedTower.RequiresUpgradeChoice) return;
        int cost = _selectedTower.UpgradeCost;
        if (_towerDefenseGold < cost ||
            !_selectedTower.UpgradeSpecializationChoice(choiceIndex)) return;
        _towerDefenseGold -= cost;
        RecordTowerDefenseGoldSpent(cost);
        PlayTowerUpgradeFeedback(_selectedTower);
        _selectedTower.name = _selectedTower.DisplayName + " Lv." + _selectedTower.Level;
        if (_selectedTower.CreatesPermanentFrostTiles)
            ApplyPermanentFrostAroundIceTower(_selectedTower);
        _selectedTower.SetRangeVisibility(_towerPlacementMode);
        RefreshTowerDefenseUi(true);
    }

    private void ApplyPermanentFrostAroundIceTower(RougeDefenseTower tower)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        if (tower == null || map == null || loader == null ||
            !map.WorldToCell(tower.transform.position, out Vector2Int center)) return;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                Vector2Int cell = center + new Vector2Int(x, y);
                if (!loader.SetPermanentTowerPlaceEffect(cell,
                        RougeTowerPlaceEffect.Frost))
                    continue;
                ApplyActivatedEffectToTowersInCell(cell,
                    loader.GetEffectiveTowerPlaceEffect(cell));
            }
        }
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
    }

    private void RefreshTowerEditHints()
    {
        for (int i = _defenseTowers.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null) continue;
            bool relocationSource = _towerRelocationActive && tower == _relocatingTower;
            bool upgradeAvailable = !relocationSource && tower.CanUpgrade &&
                _towerDefenseGold >= tower.UpgradeCost;
            tower.SetEditHintState(_towerPlacementMode,
                !relocationSource && tower == _selectedTower,
                upgradeAvailable, _showAllTowerAttackRanges);
        }
        if (_towerPlacementHoveredTower != null)
            _towerPlacementHoveredTower.SetRangeVisibility(_towerPlacementMode);
    }

    private void UpdateTowerDefenseSimulation(float dt)
    {
        if (!_towerDefenseInitialized || _towerDefenseGameOver) return;

        UpdateTowerDefenseLevelEvents();

        if (_bossDeathSequenceActive)
        {
            UpdateBossDeathSequence(Time.unscaledDeltaTime);
            RefreshTowerDefenseUi();
            return;
        }

        // Target-index damage can be produced by projectile impacts as well as continuous
        // lasers, so establish the frame before either system updates.
        _towerLaserDamageFrame++;
        if (_towerLaserDamageFrame == 0) _towerLaserDamageFrame = 1;

        if (mainTower != null)
        {
            mainTower.aoeCooldownRemaining = Mathf.Max(0f, mainTower.aoeCooldownRemaining - dt);
        }

        if (CommanderSkillsEnabled) UpdateTacticalSkills(dt);
        UpdateTowerDefenseBoss();
        UpdateTowerDefenseSpawners(dt);
        ApplyPendingMainTowerAoe();
        UpdateTowerPersistentCannonZones(dt);
        UpdateTowerFireZones(dt);
        UpdateTowerProjectiles(dt);
        UpdateRocketBarrageSystem(dt);
        UpdateTowerBeamVisuals(dt);
        UpdateTowerFlameJetVisuals(dt);
        UpdateOrbitSphereAttacks(dt);
        UpdateIceSpikeVisuals(dt);
        UpdateDefenseTowers(dt);
        UpdateAccumulatedWealthTiles(dt);
        PrepareTowerTargetRequests();
        EvaluateTowerDefenseVictoryConditions();
        RefreshTowerDefenseUi();
    }

    private void UpdateTowerDefenseBoss()
    {
        UpdateTowerDefenseBossLandingShake(Time.unscaledDeltaTime);
        if (_towerDefenseBossArrivalActive)
        {
            UpdateTowerDefenseBossArrival(Time.deltaTime);
            return;
        }
        if (!_bossSpawned && !_bossDeathSequenceActive) TryStartNextBossEncounter();

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
            UpdateTowerDefenseBossScoreHealth(0f);
            DisableBossTowerInterferenceMarkers();
            SetBossPhaseVisualsVisible(false);
            BeginBossDeathSequence();
            return;
        }

        float4 position = _positionsA[_bossEnemyIndex];
        _bossWorldPosition = new Vector3(position.x, renderHeight, position.z);
        _bossCurrentHealth = Mathf.Max(0f, state.x);
        UpdateTowerDefenseBossScoreHealth(_bossCurrentHealth);
        float healthRatio = Mathf.Clamp01(state.x / GetCurrentBossMaxHealth());
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
            bool frozen = _effectStateA.IsCreated &&
                          _bossEnemyIndex < _effectStateA.Length &&
                          _effectStateA[_bossEnemyIndex].FreezeTimer > 0f;
            bool burning = _effectStateA.IsCreated &&
                           _bossEnemyIndex < _effectStateA.Length &&
                           _effectStateA[_bossEnemyIndex].BurnTimer > 0f;
            _bossSpriteAnimator.SetFrozenVisual(frozen);
            _bossSpriteAnimator.SetBurningVisual(burning);
        }
        UpdateBossPhaseVisuals(Time.deltaTime);
    }

    private void TryStartNextBossEncounter()
    {
        while (_nextBossEncounterIndex < _bossSchedule.Count)
        {
            RougeTowerDefenseMap.BossEncounter encounter = _bossSchedule[_nextBossEncounterIndex];
            if (_survivalTime < Mathf.Max(0f, encounter.spawnMinute) * 60f) return;
            RougeBossBalanceConfig configuredBoss = FindBossBalance(encounter.bossId);
            if (configuredBoss == null)
            {
                Debug.LogWarning($"Level Boss schedule references unknown Boss ID {encounter.bossId}; entry skipped.",
                    _towerDefenseLevel);
                _nextBossEncounterIndex++;
                continue;
            }

            bossBalance = configuredBoss;
            bossBalance.EnsureDefaults();
            _activeBossEncounter = encounter;
            if (!BeginTowerDefenseBossArrival()) return;
            _nextBossEncounterIndex++;
            _bossDefeated = false;
            return;
        }
    }

    private bool TrySpawnTowerDefenseBoss()
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
        if (index < 0) return false;

        Vector3 spawn = bossSpawnPoint != null
            ? bossSpawnPoint.transform.position
            : bossBalance.fallbackSpawnPosition;
        _bossBaseMoveSpeed = CalculateBossMoveSpeedAtSpawn(spawn);
        float radius = Mathf.Max(0.5f, bossBalance.radius);
        _positionsA[index] = new float4(spawn.x, renderHeight, spawn.z, radius);
        _velocitiesA[index] = float4.zero;
        _stateA[index] = new float4(GetCurrentBossMaxHealth(), radius,
            _bossBaseMoveSpeed, 0f);
        _effectStateA[index] = new RougeEnemyEffectState
        {
            MaximumHealth = GetCurrentBossMaxHealth(),
            Armor = Mathf.Clamp(bossBalance.armor, RougeArmorRules.MinimumEnemyArmor,
                RougeArmorRules.MaximumEnemyArmor)
        };
        _effectStateB[index] = _effectStateA[index];
        _towerDefenseEnemyKinds[index] = BossEnemyFlag;
        // Kind 3 is clipped by the instanced enemy shader; the Boss is rendered by
        // its own billboard animator so it can play skills and split into shards.
        if (_enemyRenderKinds.IsCreated) _enemyRenderKinds[index] = 3;
        _bossEnemyIndex = index;
        _bossSpawned = true;
        _bossInterferenceActive = false;
        _bossShieldActive = false;
        _bossHasteActive = false;
        _bossCurrentHealth = GetCurrentBossMaxHealth();
        RegisterTowerDefenseBossForScore(_bossCurrentHealth);
        if (_bossSpriteAnimator != null) Destroy(_bossSpriteAnimator.gameObject);
        _bossSpriteAnimator = RougeBossSpriteAnimator.Create(bossBalance, radius * 4.2f);
        if (_bossSpriteAnimator != null)
        {
            _bossSpriteAnimator.SetWorldState(
                new Vector3(spawn.x, renderHeight + radius * 1.55f, spawn.z), Vector3.zero);
        }
        _towerDefenseAliveEstimate++;
        _towerDefenseSpawnedTotal++;
        RefreshTowerDefenseUi();
        return true;
    }

    private float CalculateBossMoveSpeedAtSpawn(Vector3 spawn)
    {
        float2 spawnPosition = new float2(spawn.x, spawn.z);
        float2 goalPosition = GetEnemyTowerDefenseGoal(float2.zero);
        float routeDistance = math.distance(spawnPosition, goalPosition);

        if (_flowDistanceField.IsCreated && _flowFieldReady)
        {
            float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
            int2 cell = RougeMortonGridUtility.WorldToGrid(
                spawnPosition, _flowGridOrigin, invCellSize, _flowGridDim);
            int cellIndex = RougeMortonGridUtility.EncodeMorton(cell.x, cell.y);
            float flowDistance = _flowDistanceField[cellIndex];
            if (math.isfinite(flowDistance) && flowDistance > 0f && flowDistance < 1e17f)
            {
                float2 cellCenter = _flowGridOrigin +
                    new float2(cell.x + 0.5f, cell.y + 0.5f) * _flowFieldRuntimeCellSize;
                routeDistance = flowDistance + math.distance(spawnPosition, cellCenter);
            }
        }

        float travelSeconds = Mathf.Max(30f, bossBalance.targetTravelTimeSeconds);
        float calculatedSpeed = routeDistance / travelSeconds;
        if (!float.IsFinite(calculatedSpeed) || calculatedSpeed <= 0f)
            return Mathf.Max(0.1f, bossBalance.moveSpeed);
        return Mathf.Max(0.1f, calculatedSpeed);
    }

    private void BeginBossDeathSequence()
    {
        if (_bossDeathSequenceActive || _towerDefenseVictory) return;
        _bossDeathSequenceActive = true;
        StopAllTowerAttackSounds();
        _bossDeathSequenceTimer = 0f;
        _bossDeathShockwaveStep = 0;
        _bossDeathExplosionTriggered = false;
        _bossDeathShouldGrantVictory = _activeBossEncounter != null &&
            _activeBossEncounter.defeatGrantsVictory &&
            HasLevelVictoryCondition(RougeLevelVictoryConditionType.KillBoss);
        _bossSpawned = false;
        _bossDefeated = _nextBossEncounterIndex >= _bossSchedule.Count;
        _towerPlacementMode = false;
        TowerDefenseBuildModeActive = false;
        ClearTowerRelocationState();
        SetTowerPlaceVisualsVisible(false);
        Time.timeScale = GetTowerDefensePlayTimeScale();
        RefreshTowerEditHints();
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

        while (_bossDeathShouldGrantVictory && _bossDeathShockwaveStep < 3 &&
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
        if (_bossDeathShouldGrantVictory)
            EliminateEnemiesInsideBossShockwave(float.MaxValue, true);
        _bossDeathSequenceActive = false;
        ReleaseDefeatedBossSlot();
        _bossEnemyIndex = -1;
        _bossCurrentHealth = 0f;
        _activeBossEncounter = null;
        _bossInterferenceActive = false;
        _bossShieldActive = false;
        _bossHasteActive = false;
        if (_bossSpriteAnimator != null) Destroy(_bossSpriteAnimator.gameObject);
        _bossSpriteAnimator = null;
        _towerDefenseBossArrivalActive = false;
        _towerDefenseBossArrivalTimer = 0f;
        _towerDefenseBossLandingShakeRemaining = 0f;
        RougeCameraFollow endingFollow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (endingFollow != null) endingFollow.EndCinematicFocus();
        if (_bossDeathShouldGrantVictory)
        {
            TriggerTowerDefenseVictory("首领已击破");
            return;
        }
        _bossDeathShouldGrantVictory = false;
        EvaluateTowerDefenseVictoryConditions();
    }

    private void ReleaseDefeatedBossSlot()
    {
        int index = _bossEnemyIndex;
        if (index < 0 || !_stateA.IsCreated || index >= _stateA.Length) return;
        float4 state = _stateA[index];
        state.x = 0f;
        state.w = 20.99f;
        float4 position = _positionsA[index];
        position.y = -1000f;
        _stateA[index] = state;
        _positionsA[index] = position;
        if (_stateB.IsCreated && index < _stateB.Length) _stateB[index] = state;
        if (_positionsB.IsCreated && index < _positionsB.Length) _positionsB[index] = position;
        if (_towerDefenseEnemyKinds.IsCreated && index < _towerDefenseEnemyKinds.Length)
            _towerDefenseEnemyKinds[index] = 0;
        if (_enemyRenderKinds.IsCreated && index < _enemyRenderKinds.Length)
            _enemyRenderKinds[index] = 0;
    }

    private void EliminateEnemiesInsideBossShockwave(float radius, bool eliminateAll)
    {
        if (!_stateA.IsCreated || !_positionsA.IsCreated) return;
        float radiusSq = radius >= 100000f ? float.MaxValue : radius * radius;
        int removed = 0;
        int burstCount = 0;
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
            if (burstCount < 24)
            {
                float enemyRadius = Mathf.Max(0.35f, position.w);
                SpawnDeathBurstVFX(new Vector3(position.x,
                    renderHeight + enemyRadius * 0.75f, position.z),
                    enemyRadius * 1.65f);
                burstCount++;
            }
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

    private void TriggerTowerDefenseVictory(string reason)
    {
        if (_towerDefenseGameOver) return;
        StopTowerDefenseAutoplayForConclusion();
        if (_cameraViewMode != CameraViewMode.Default) ExitDebugUnitView();
        _towerDefenseVictory = true;
        _towerDefenseGameOver = true;
        _towerDefenseGameOverReason = string.IsNullOrWhiteSpace(reason) ? "胜利条件已达成" : reason;
        HideTowerDefenseSpawnWarnings();
        StopAllTowerAttackSounds();
        Time.timeScale = 0f;
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.GetComponent<RougeCameraFollow>();
        if (follow != null) follow.SetCinematicShake(0f);
        RefreshTowerDefenseUi(true);
    }

    private void EvaluateTowerDefenseVictoryConditions()
    {
        if (_towerDefenseGameOver || _bossDeathSequenceActive || _towerDefenseLevel == null) return;
        IReadOnlyList<RougeTowerDefenseMap.VictoryCondition> conditions =
            _towerDefenseLevel.VictoryConditions;
        for (int i = 0; conditions != null && i < conditions.Count; i++)
        {
            RougeTowerDefenseMap.VictoryCondition condition = conditions[i];
            if (condition == null) continue;
            switch (condition.type)
            {
                case RougeLevelVictoryConditionType.KillEnemies:
                    if (totalKills >= Mathf.Max(1, condition.targetAmount))
                        TriggerTowerDefenseVictory($"已消灭 {Mathf.Max(1, condition.targetAmount)} 个敌人");
                    break;
                case RougeLevelVictoryConditionType.SurviveSeconds:
                    if (_survivalTime >= Mathf.Max(0.1f, condition.targetSeconds))
                        TriggerTowerDefenseVictory($"已坚守 {FormatGameTime(condition.targetSeconds)}");
                    break;
                case RougeLevelVictoryConditionType.KillAllEnemies:
                    if (AreAllLevelEnemiesDefeated())
                        TriggerTowerDefenseVictory("所有敌人已消灭");
                    break;
                case RougeLevelVictoryConditionType.EarnGold:
                    if (_towerDefenseGoldEarnedTotal >= Mathf.Max(1, condition.targetAmount))
                        TriggerTowerDefenseVictory($"已累计获得 {Mathf.Max(1, condition.targetAmount)} 金币");
                    break;
                // Boss kills are event-gated by the individual Boss encounter's Victory On Defeat flag.
                case RougeLevelVictoryConditionType.KillBoss:
                    break;
            }
            if (_towerDefenseGameOver) return;
        }
    }

    private bool AreAllLevelEnemiesDefeated()
    {
        if (HasPendingBossEncounter()) return false;
        if (_towerDefenseSpawners.Count == 0 && !_towerDefenseAllSpawnersExhausted &&
            _towerDefenseLevel != null && _towerDefenseLevel.EnemySpawns.Count > 0)
            return false;
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint point = _towerDefenseSpawners[i];
            if (point == null || !point.isActiveAndEnabled) continue;
            if (!point.limitWaveCount || point.waveIndex < Mathf.Max(1, point.maximumWaves)) return false;
        }
        if (Time.unscaledTime < _nextKillAllVerificationTime) return false;
        _nextKillAllVerificationTime = Time.unscaledTime + 0.5f;
        if (_stateA.IsCreated)
        {
            int limit = Mathf.Min(_currentMaxEnemies, _stateA.Length);
            for (int i = 0; i < limit; i++)
            {
                if (_stateA[i].x > 0f) return false;
            }
        }
        _towerDefenseAliveEstimate = 0;
        _towerDefenseSpawnedTotal = 0;
        return true;
    }

    private void DisableBossTowerInterferenceMarkers()
    {
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            if (_defenseTowers[i] != null) _defenseTowers[i].SetBossInterference(false, 0);
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
            if (_towerDefenseAllSpawnersExhausted) return;
            _towerDefenseSpawnerResolveRetryTimer -= dt;
            if (_towerDefenseSpawnerResolveRetryTimer <= 0f) ResolveEnemySpawnPoints();
            return;
        }

        bool exhaustedSpawnPoint = false;
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
            point.UpdateSpawnWarning();
            if (point.timer > 0f) continue;
            int enemyLevel = GetTowerDefenseEnemyLevel();
            SpawnEnemyBatch(point, Mathf.Clamp(point.spawnCount, 1, 64));
            point.CompleteWave(enemyBalance.EvaluateSpawnSpeedMultiplier(enemyLevel) *
                               _towerDefenseLevelEventSpawnRateMultiplier);
            if (point.HasReachedWaveLimit())
            {
                exhaustedSpawnPoint = true;
                _towerDefenseSpawners.RemoveAt(i);
                point.gameObject.SetActive(false);
                Destroy(point.gameObject);
            }
        }
        if (exhaustedSpawnPoint && _towerDefenseSpawners.Count == 0)
            _towerDefenseAllSpawnersExhausted = true;
    }

    private void TriggerAllTowerDefenseSpawnPointsOnce()
    {
        int enemyLevel = GetTowerDefenseEnemyLevel();
        float spawnSpeedMultiplier = enemyBalance.EvaluateSpawnSpeedMultiplier(enemyLevel) *
                                     _towerDefenseLevelEventSpawnRateMultiplier;
        bool exhaustedSpawnPoint = false;
        for (int i = _towerDefenseSpawners.Count - 1; i >= 0; i--)
        {
            RougeEnemySpawnPoint point = _towerDefenseSpawners[i];
            if (point == null || !point.isActiveAndEnabled || point.HasReachedWaveLimit())
                continue;

            point.timer = 0f;
            point.HideSpawnWarning();
            SpawnEnemyBatch(point, Mathf.Clamp(point.spawnCount, 1, 64));
            point.CompleteWave(spawnSpeedMultiplier);
            if (!point.HasReachedWaveLimit())
                continue;

            exhaustedSpawnPoint = true;
            _towerDefenseSpawners.RemoveAt(i);
            point.gameObject.SetActive(false);
            Destroy(point.gameObject);
        }
        if (exhaustedSpawnPoint && _towerDefenseSpawners.Count == 0)
            _towerDefenseAllSpawnersExhausted = true;
    }

    private void HideTowerDefenseSpawnWarnings()
    {
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint point = _towerDefenseSpawners[i];
            if (point != null) point.HideSpawnWarning();
        }
    }

    private void SpawnEnemyBatch(RougeEnemySpawnPoint point, int count)
    {
        if (!_positionsA.IsCreated || count <= 0) return;
        int remainingCapacity = GetTowerDefenseAliveEnemyCap() - _towerDefenseAliveEstimate;
        if (remainingCapacity <= 0) return;
        count = Mathf.Min(count, remainingCapacity);
        int waveEnemyTypeIndex = RollTowerDefenseWaveEnemyTypeIndex(point);
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
                if (ActivateEnemySlot(index, point, waveEnemyTypeIndex, spawned, count)) spawned++;
            }
            _towerDefenseSpawnSearchCursor = (_towerDefenseSpawnSearchCursor + checkedSlots) % existingCount;
        }

        while (spawned < count && _currentMaxEnemies < enemyCount)
        {
            if (!ActivateEnemySlot(_currentMaxEnemies, point, waveEnemyTypeIndex, spawned, count)) break;
            _currentMaxEnemies++;
            spawned++;
        }
        _towerDefenseAliveEstimate += spawned;
        _towerDefenseSpawnedTotal += spawned;
    }

    private int GetTowerDefenseAliveEnemyCap()
    {
        // Open capacity continuously so minute boundaries do not release a large
        // accumulated wave all at once: +5k per minute, reaching 100k at 20m.
        int timeBasedCap = Mathf.CeilToInt(
            Mathf.Max(0f, _survivalTime) * TowerDefenseEnemyCapPerMinute / 60f);
        return Mathf.Min(enemyCount,
            Mathf.Clamp(timeBasedCap, InitialTowerDefenseEnemyCap, MaximumTowerDefenseEnemyCap));
    }

    private bool ActivateEnemySlot(int index, RougeEnemySpawnPoint point, int waveEnemyTypeIndex,
        int spawnOrdinal, int formationCount)
    {
        byte kind = RollTowerDefenseEnemyKind(waveEnemyTypeIndex);
        RougeEnemyArchetypeConfig archetype = GetEnemyArchetype(kind);
        bool elite = (kind & EliteEnemyFlag) != 0;
        float health = GetTowerDefenseEnemyHealth(kind);
        float microCellSize = RougeTowerDefenseMapLoader.ActiveMap != null
            ? RougeTowerDefenseMapLoader.ActiveMap.MicroCellSize : 1f;
        // Visual scale is supplied to the billboard shader separately. Keep navigation
        // footprints small enough to turn through a one-tile-wide lane; allowing elite
        // visual scale to double this radius used to make them wider than the road itself.
        float baseRadius = microCellSize * 0.5f;
        float scaledRadius = baseRadius * Mathf.Max(0.1f, archetype.size) *
            (elite ? Mathf.Max(1f, enemyBalance.eliteSizeMultiplier) : 1f);
        float navigationRadius = Mathf.Min(scaledRadius, microCellSize);
        // Crowd separation follows the visible billboard more closely, but remains below
        // one road tile so even elite enemies cannot recreate the old corner deadlock.
        float crowdRadius = Mathf.Min(scaledRadius * 1.35f, microCellSize * 1.75f);
        if (!TryGetReachableEnemySpawnPosition(point, navigationRadius, spawnOrdinal, formationCount,
                out float2 spawnPosition)) return false;
        float speed = GetTowerDefenseEnemySpeed(kind);
        _positionsA[index] = new float4(spawnPosition.x, renderHeight, spawnPosition.y, crowdRadius);
        _velocitiesA[index] = float4.zero;
        _stateA[index] = new float4(health, navigationRadius, speed, 0f);
        RougeEnemyEffectState initialEffects = new RougeEnemyEffectState
        {
            MaximumHealth = health,
            Armor = Mathf.Clamp(archetype.armor, RougeArmorRules.MinimumEnemyArmor,
                RougeArmorRules.MaximumEnemyArmor),
            BaseKillGold = Mathf.Max(0, elite ? archetype.eliteKillGold : archetype.killGold)
        };
        _effectStateA[index] = initialEffects;
        // A reused slot may still contain an old airborne/death effect in the back buffer.
        // Initialise both sides while the previous job is complete so the next swap can
        // never expose that stale state as a one-frame white flash.
        _positionsB[index] = _positionsA[index];
        _velocitiesB[index] = float4.zero;
        _stateB[index] = _stateA[index];
        _effectStateB[index] = initialEffects;
        _towerDefenseEnemyKinds[index] = kind;
        if (_enemyRenderKinds.IsCreated) _enemyRenderKinds[index] = kind;
        return true;
    }

    private void ApplyEnemySpriteSheetTextures()
    {
        if (enemyMaterial == null) return;
        enemyBalance ??= new RougeEnemyBalanceConfig();
        enemyBalance.EnsureDefaults();
        Texture2D fallback = Resources.Load<Texture2D>("Sprites/enemy_robot");
        Texture2D frozenOverlay =
            Resources.Load<Texture2D>("Sprites/Effects/enemy_frozen_overlay");
        if (enemyMaterial.HasProperty("_FrozenOverlay") && frozenOverlay != null)
            enemyMaterial.SetTexture("_FrozenOverlay", frozenOverlay);
        for (int i = 0; i < 3; i++)
        {
            RougeEnemyArchetypeConfig type = enemyBalance.enemyTypes[Mathf.Min(i,
                enemyBalance.enemyTypes.Count - 1)];
            Texture2D sheet = string.IsNullOrWhiteSpace(type.spriteResourcePath)
                ? null
                : Resources.Load<Texture2D>(type.spriteResourcePath);
            enemyMaterial.SetTexture("_EnemySheet" + i, sheet != null ? sheet : fallback);
            enemyMaterial.SetVector("_EnemySheetAnimation" + i, new Vector4(
                Mathf.Max(1, type.spriteSheetColumns),
                Mathf.Max(1, type.spriteSheetRows),
                Mathf.Max(0.01f, type.spriteAnimationFps),
                Mathf.Clamp(type.spriteDeathFrameCount, 0,
                    Mathf.Max(0, type.spriteSheetColumns * type.spriteSheetRows - 1))));
        }
        enemyMaterial.SetVector("_EnemyTypeSizes", new Vector4(
            Mathf.Max(0.1f, enemyBalance.enemyTypes[0].size),
            Mathf.Max(0.1f, enemyBalance.enemyTypes[Mathf.Min(1, enemyBalance.enemyTypes.Count - 1)].size),
            Mathf.Max(0.1f, enemyBalance.enemyTypes[Mathf.Min(2, enemyBalance.enemyTypes.Count - 1)].size),
            Mathf.Max(1f, enemyBalance.eliteSizeMultiplier)));
        enemyMaterial.SetTexture("_MainTex", fallback);
    }

    private bool TryGetReachableEnemySpawnPosition(RougeEnemySpawnPoint point, float enemyNavigationRadius,
        int spawnOrdinal, int formationCount, out float2 spawnPosition)
    {
        Vector3 worldCenter = point.transform.position;
        float2 center = new float2(worldCenter.x, worldCenter.z);
        float2 arenaLimits = _usesMapArenaBounds
            ? math.max(new float2(1f), _mapArenaHalfExtents - 2f)
            : new float2(Mathf.Max(1f, arenaHalfExtent - 2f));
        float spawnCellSize = Mathf.Max(0.1f, point.spawnCellSize);
        float subCellSize = spawnCellSize / RougeTowerDefenseMap.MicroCellsPerTile;
        int safeCount = Mathf.Clamp(formationCount, 1, 64);
        int columns = Mathf.CeilToInt(Mathf.Sqrt(safeCount));
        int rows = Mathf.CeilToInt(safeCount / (float)columns);
        int row = spawnOrdinal / columns;
        int column = spawnOrdinal % columns;
        int itemsInRow = Mathf.Min(columns, safeCount - row * columns);
        float offsetX = (column - (itemsInRow - 1) * 0.5f) * subCellSize;
        float offsetY = ((rows - 1) * 0.5f - row) * subCellSize;
        float2 formationCandidate = center + new float2(offsetX, offsetY);
        formationCandidate = math.clamp(formationCandidate, -arenaLimits, arenaLimits);
        if (IsReachableEnemySpawnPosition(formationCandidate, enemyNavigationRadius))
        {
            spawnPosition = formationCandidate;
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
                math.max(8, Mathf.CeilToInt(spawnCellSize * 0.5f * invCellSize) + 8));
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
        return Mathf.Clamp(GetTowerDefenseEnemyPowerStep() + 1, 1,
            RougeEnemyBalanceConfig.MaximumEnemyLevel);
    }

    private float GetTowerDefenseEnemyHealthMultiplier(float archetypeGrowthMultiplier = 1f)
    {
        float levelMultiplier = enemyBalance.EvaluateHealthMultiplier(
            GetTowerDefenseEnemyLevel(), archetypeGrowthMultiplier);
        float levelRuleMultiplier = _towerDefenseLevel != null
            ? _towerDefenseLevel.EnemyHealthMultiplier
            : 1f;
        return levelMultiplier * Mathf.Max(0.01f, levelRuleMultiplier) *
               Mathf.Max(0.01f, _towerDefenseLevelEventEnemyHealthMultiplier);
    }

    private float GetTowerDefenseEnemyMoveSpeedMultiplier()
    {
        float levelMultiplier = _towerDefenseLevel != null
            ? Mathf.Max(0.01f, _towerDefenseLevel.EnemyMoveSpeedMultiplier)
            : 1f;
        return levelMultiplier *
               Mathf.Max(0.01f, _towerDefenseLevelEventEnemyMoveSpeedMultiplier);
    }

    private float GetCurrentBossMaxHealth()
    {
        float levelRuleMultiplier = _towerDefenseLevel != null
            ? Mathf.Max(0.01f, _towerDefenseLevel.EnemyHealthMultiplier)
            : 1f;
        return Mathf.Max(1f, bossBalance.maxHealth) * levelRuleMultiplier;
    }

    private float GetTowerDefenseEnemyHealth()
    {
        RougeEnemyArchetypeConfig archetype = enemyBalance.enemyTypes[0];
        return Mathf.Max(1f, archetype.baseHealth) *
            GetTowerDefenseEnemyHealthMultiplier(archetype.healthGrowthMultiplier);
    }

    private float GetTowerDefenseEnemySpeed()
    {
        return Mathf.Max(0.1f, enemyBalance.enemyTypes[0].baseSpeed) *
            enemyBalance.EvaluateSpeedMultiplier(GetTowerDefenseEnemyLevel()) *
            GetTowerDefenseEnemyMoveSpeedMultiplier();
    }

    private float GetTowerDefenseEnemyHealth(byte kind)
    {
        if ((kind & BossEnemyFlag) != 0) return GetCurrentBossMaxHealth();
        RougeEnemyArchetypeConfig archetype = GetEnemyArchetype(kind);
        float eliteMultiplier = (kind & EliteEnemyFlag) != 0
            ? Mathf.Max(1f, enemyBalance.eliteHealthMultiplier) *
              Mathf.Max(0.01f, _towerDefenseLevelEventEliteHealthMultiplier)
            : 1f;
        float levelMultiplier = GetTowerDefenseEnemyHealthMultiplier(archetype.healthGrowthMultiplier);
        return Mathf.Max(0.01f, archetype.baseHealth) * levelMultiplier * eliteMultiplier;
    }

    private float GetTowerDefenseEnemySpeed(byte kind)
    {
        if ((kind & BossEnemyFlag) != 0)
        {
            float baseSpeed = _bossBaseMoveSpeed > 0f ? _bossBaseMoveSpeed : bossBalance.moveSpeed;
            return Mathf.Max(0.1f, baseSpeed) * GetTowerDefenseEnemyMoveSpeedMultiplier() *
                   (_bossHasteActive ? bossBalance.hasteSpeedMultiplier : 1f);
        }
        RougeEnemyArchetypeConfig archetype = GetEnemyArchetype(kind);
        float eliteMultiplier = (kind & EliteEnemyFlag) != 0
            ? Mathf.Max(0.1f, enemyBalance.eliteSpeedMultiplier) *
              Mathf.Max(0.01f, _towerDefenseLevelEventEliteMoveSpeedMultiplier)
            : 1f;
        float levelMultiplier = enemyBalance.EvaluateSpeedMultiplier(GetTowerDefenseEnemyLevel());
        return Mathf.Max(0.01f, archetype.baseSpeed) * levelMultiplier * eliteMultiplier *
               GetTowerDefenseEnemyMoveSpeedMultiplier();
    }

    private RougeEnemyArchetypeConfig GetEnemyArchetype(byte kind)
    {
        enemyBalance.EnsureDefaults();
        int index = Mathf.Clamp(kind & EnemyArchetypeMask, 0, enemyBalance.enemyTypes.Count - 1);
        return enemyBalance.enemyTypes[index];
    }

    private int RollTowerDefenseWaveEnemyTypeIndex(RougeEnemySpawnPoint point)
    {
        enemyBalance.EnsureDefaults();
        int availableTypeCount = Mathf.Min(enemyBalance.enemyTypes.Count, EnemyArchetypeMask + 1);
        int selected = point != null ? point.GetEnemyTypeIndex() : 0;
        return Mathf.Clamp(selected, 0, Mathf.Max(0, availableTypeCount - 1));
    }

    private byte RollTowerDefenseEnemyKind(int waveEnemyTypeIndex)
    {
        byte kind = (byte)Mathf.Clamp(waveEnemyTypeIndex, 0, EnemyArchetypeMask);
        if (_towerDefenseLevelEventsControlEliteUnlock)
        {
            if (!_towerDefenseLevelEventElitesUnlocked) return kind;
        }
        else
        {
            float eliteSpawnDelay = _towerDefenseLevel != null
                ? Mathf.Max(0f, _towerDefenseLevel.EliteSpawnDelaySeconds)
                : RougeTowerDefenseMap.DefaultEliteSpawnDelaySeconds;
            if (_survivalTime < eliteSpawnDelay) return kind;
        }

        float eliteChance = Mathf.Clamp01(
            enemyBalance.EvaluateEliteChance01(GetTowerDefenseEnemyLevel()) *
            _towerDefenseLevelEventEliteChanceMultiplier);
        if (UnityEngine.Random.value < eliteChance) kind |= EliteEnemyFlag;
        return kind;
    }

    private void StopAllTowerAttackSounds()
    {
        RougeDefenseTower.StopAllTowerCombatSounds();
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
            // Main-tower contact uses an explicit radial repulse in ProcessTowerArea.
            // Keeping it separate from configurable skill knockback prevents the goal/
            // caster centre rules from ever reversing the impulse toward the tower.
            PullForce = Mathf.Max(0f, mainTower.hitAoeKnockback)
        });
        SpawnAOERing(new Vector3(p.x, renderHeight + 0.08f, p.z), mainTower.hitAoeRadius, 0.32f,
            new Color(0.2f, 0.85f, 1f, 1f));
    }

    private void UpdateIceSpikeAugment(RougeDefenseTower tower, float dt)
    {
        if (tower == null || !tower.CreatesIceSpikes)
        {
            if (tower != null) tower.iceSpikeTimer = 0f;
            return;
        }

        tower.iceSpikeTimer -= Mathf.Max(0f, dt);
        if (tower.iceSpikeTimer > 0f) return;
        SpawnIceSpikeAttack(tower);
        RougeIceTowerSpecializationConfig config =
            TowerDefenseVisuals.GetIceSpecializationConfig();
        tower.iceSpikeTimer = UnityEngine.Random.Range(
            config.iceSpikeIntervalMin, config.iceSpikeIntervalMax);
    }

    private void SpawnIceSpikeAttack(RougeDefenseTower tower)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (tower == null || map == null) return;
        Vector3 source = tower.transform.position;
        if (!map.WorldToCell(source, out Vector2Int sourceCell)) return;

        _iceSpikeCandidateCells.Clear();
        int cellRadius = Mathf.CeilToInt(tower.AttackRange /
                                        Mathf.Max(0.1f, map.CellSize));
        for (int y = -cellRadius; y <= cellRadius; y++)
        {
            for (int x = -cellRadius; x <= cellRadius; x++)
            {
                Vector2Int cell = sourceCell + new Vector2Int(x, y);
                if (!map.Contains(cell)) continue;
                Vector3 center = map.CellCenter(cell, renderHeight);
                if (Mathf.Max(Mathf.Abs(center.x - source.x),
                        Mathf.Abs(center.z - source.z)) <= tower.AttackRange)
                    _iceSpikeCandidateCells.Add(cell);
            }
        }
        if (_iceSpikeCandidateCells.Count == 0) return;

        RougeIceTowerSpecializationConfig config =
            TowerDefenseVisuals.GetIceSpecializationConfig();
        int spawnCount = Mathf.Min(_iceSpikeCandidateCells.Count,
            UnityEngine.Random.Range(config.iceSpikeMinCells,
                config.iceSpikeMaxCells + 1));
        float spikeDamageMultiplier = config.iceSpikeDamageMultiplier;
        float spikeFreezeMultiplier = config.iceSpikeFreezeDurationMultiplier;
        float frostDurationBonus = tower.IsOnFrostTile ? config.frostDurationBonus : 0f;
        float normalFreeze = config.freezeNormalDuration * spikeFreezeMultiplier +
            frostDurationBonus;
        float eliteFreeze = config.freezeEliteDuration * spikeFreezeMultiplier +
            frostDurationBonus;
        float bossFreeze = config.freezeBossDuration * spikeFreezeMultiplier +
            frostDurationBonus;

        for (int i = 0; i < spawnCount; i++)
        {
            int selectedIndex = UnityEngine.Random.Range(i, _iceSpikeCandidateCells.Count);
            (_iceSpikeCandidateCells[i], _iceSpikeCandidateCells[selectedIndex]) =
                (_iceSpikeCandidateCells[selectedIndex], _iceSpikeCandidateCells[i]);
            Vector2Int targetCell = _iceSpikeCandidateCells[i];
            Vector3 target = map.CellCenter(targetCell, renderHeight);
            TryAddTowerDirectDamageArea(new RougeSkillArea
            {
                Type = 21,
                Position = new float2(target.x, target.z),
                Radius = map.CellSize * 0.5f,
                Damage = tower.Damage * spikeDamageMultiplier,
                EffectFlags = (int)SkillHitEffectTag.Freeze,
                EffectFreezeDuration = normalFreeze,
                EffectEliteFreezeDuration = eliteFreeze,
                EffectBossFreezeDuration = bossFreeze,
                EffectBossFreezeImmunityDuration = config.freezeBossImmunityDuration,
                SourceTowerTypePlusOne = (int)RougeTowerType.Ice + 1,
                SourceTowerTileEffect = (int)tower.TowerPlaceEffect,
                SourceTowerKillGoldBonus = tower.KillGoldPercentBonus,
                SourceTowerWealthCellIndexPlusOne = GetTowerWealthCellIndexPlusOne(tower)
            }, TowerFrostAreaSlowMultiplier);
            SpawnIceSpikeVisual(target, map.CellSize);
            SpawnAOERing(target + Vector3.up * 0.06f, map.CellSize * 0.5f, 0.3f,
                new Color(0.18f, 0.84f, 1f, 1f));
        }
    }

    private void SpawnIceSpikeVisual(Vector3 position, float cellSize)
    {
        Sprite sprite = RougeSpriteAssets.Load("Sprites/Effects/ice_spike_attack");
        if (sprite == null) return;
        GameObject root = new GameObject("Ice Spike Attack Visual");
        root.transform.SetParent(transform, false);
        root.transform.position = position + Vector3.up * (cellSize * 0.52f);
        root.AddComponent<RougeBillboard>();
        float spriteHeight = sprite.rect.height / Mathf.Max(1f, sprite.pixelsPerUnit);
        float scale = cellSize * 1.08f / Mathf.Max(0.01f, spriteHeight);
        SpriteRenderer renderer = RougeSpriteAssets.CreateRenderer("Ice Spikes", root.transform,
            sprite, Vector3.zero, scale, 170, Color.white);
        root.transform.localScale = Vector3.one * 0.15f;
        _iceSpikeVisuals.Add(new IceSpikeVisual
        {
            Root = root,
            Renderer = renderer,
            Elapsed = 0f,
            Duration = 0.58f
        });
    }

    private void SpawnVulnerabilityLandingBlastVfx(Vector3 position, float radius)
    {
        float visualSize = Mathf.Clamp(radius * 0.38f, 1.6f, 7f);
        SpawnIceSpikeVisual(position, visualSize);
        const int shardCount = 6;
        float shardDistance = Mathf.Max(0.8f, radius * 0.48f);
        for (int i = 0; i < shardCount; i++)
        {
            float angle = (i / (float)shardCount) * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) *
                             shardDistance;
            SpawnIceSpikeVisual(position + offset, visualSize * 0.62f);
        }
        SpawnAOERing(position + Vector3.up * 0.07f, radius, 0.48f,
            new Color(0.2f, 0.86f, 1f, 1f));
    }

    private void UpdateIceSpikeVisuals(float dt)
    {
        for (int i = _iceSpikeVisuals.Count - 1; i >= 0; i--)
        {
            IceSpikeVisual visual = _iceSpikeVisuals[i];
            if (visual.Root == null || visual.Renderer == null)
            {
                _iceSpikeVisuals.RemoveAt(i);
                continue;
            }
            visual.Elapsed += Mathf.Max(0f, dt);
            float progress = Mathf.Clamp01(visual.Elapsed / Mathf.Max(0.01f, visual.Duration));
            float grow = progress < 0.28f
                ? Mathf.Lerp(0.15f, 1.12f, progress / 0.28f)
                : Mathf.Lerp(1.12f, 0.96f, (progress - 0.28f) / 0.72f);
            visual.Root.transform.localScale = Vector3.one * grow;
            Color color = visual.Renderer.color;
            color.a = progress < 0.68f ? 1f : 1f - (progress - 0.68f) / 0.32f;
            visual.Renderer.color = color;
            if (progress >= 1f)
            {
                Destroy(visual.Root);
                _iceSpikeVisuals.RemoveAt(i);
            }
            else
            {
                _iceSpikeVisuals[i] = visual;
            }
        }
    }

    private void UpdateDefenseTowers(float dt)
    {
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
                bossDebuffed ? bossBalance.interferenceAttackSpeedBuffLevel : 0);
            tower.UpdatePresentation(dt);
            UpdateIceSpikeAugment(tower, dt);
            if (tower.EchoAttackRepeatPending)
            {
                if (tower.TickEchoAttackRepeatDelay(dt, out Vector3 repeatTarget))
                {
                    if (tower.IsTargetedDamage &&
                        tower.TowerType != RougeTowerType.PiercingLaser &&
                        !tower.UsesRotatingFlamethrower)
                        AimTowerAt(tower, repeatTarget);
                    FireTower(tower, i, repeatTarget);
                }
                continue;
            }
            if (tower.IsSpecialTower)
            {
                tower.HideLaserBeams();
                continue;
            }

            bool usesProjectileBurst = tower.TowerType == RougeTowerType.Cannon ||
                (tower.TowerType == RougeTowerType.Flame && !tower.UsesFlamethrower);
            if (usesProjectileBurst && tower.projectileBurstShotsRemaining > 0)
            {
                UpdateProjectileBurst(tower, i, dt);
                if (tower.projectileBurstShotsRemaining > 0) continue;
            }

            if (tower.TowerType == RougeTowerType.Laser)
            {
                UpdateContinuousLaserTower(tower, i, dt);
                continue;
            }

            if (tower.TowerType == RougeTowerType.OrbitSphere && IsOrbitSphereAttackActive(tower))
                continue;

            // The piercing laser owns its complete charge/fire sequence. Its normal attack
            // cooldown begins only after the fixed 0.75 second beam has fully collapsed.
            if (tower.TowerType == RougeTowerType.PiercingLaser &&
                IsPiercingLaserAttackActive(tower))
                continue;

            tower.HideLaserBeams();
            tower.attackTimer -= dt * tower.AttackSpeedMultiplier;
            if (tower.attackTimer > 0f) continue;

            if (!tower.IsTargetedDamage)
            {
                bool echoesAttack = tower.RepeatsAttackFromEcho;
                if (echoesAttack) tower.BeginEchoAttackCycle(tower.transform.position);
                FireTower(tower, i, tower.transform.position);
                if (!echoesAttack) tower.attackTimer += tower.AttackInterval;
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

            if (tower.UsesRotatingFlamethrower)
            {
                Vector3 rotatingTarget = tower.transform.position +
                    tower.GetCurrentAimDirection() * tower.AttackRange;
                if (tower.RepeatsAttackFromEcho)
                {
                    tower.BeginEchoAttackCycle(rotatingTarget);
                    FireFlamethrower(tower, rotatingTarget);
                }
                else
                {
                    int catchUpShots = 0;
                    do
                    {
                        tower.attackTimer += tower.AttackInterval;
                        catchUpShots++;
                    }
                    while (tower.attackTimer <= 0f && catchUpShots < 8);
                    FireFlamethrower(tower, rotatingTarget, catchUpShots);
                }
                continue;
            }

            if (!TryResolveTowerTarget(tower, i, out Vector3 targetPosition))
            {
                tower.attackTimer = Mathf.Min(0.15f, tower.AttackInterval);
                continue;
            }

            // Piercing laser owns a visible 0.25 second turn during its charge instead
            // of snapping the turret to the target before the sequence begins.
            if (tower.TowerType != RougeTowerType.PiercingLaser)
                AimTowerAt(tower, targetPosition);
            if (tower.UsesFlamethrower && !tower.RepeatsAttackFromEcho)
            {
                int catchUpShots = 0;
                do
                {
                    tower.attackTimer += tower.AttackInterval;
                    catchUpShots++;
                }
                while (tower.attackTimer <= 0f && catchUpShots < 8);
                FireFlamethrower(tower, targetPosition, catchUpShots);
                continue;
            }
            bool repeatsAttack = tower.RepeatsAttackFromEcho;
            if (repeatsAttack) tower.BeginEchoAttackCycle(targetPosition);
            FireTower(tower, i, targetPosition);
            if (tower.TowerType != RougeTowerType.PiercingLaser && !repeatsAttack)
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

            if (tower.IsSpecialTower)
            {
                _towerTargetRequests[i] = default;
                continue;
            }
            if (tower.UsesRotatingFlamethrower)
            {
                _towerTargetRequests[i] = default;
                continue;
            }

            Vector3 position = tower.transform.position;
            bool focusedBossLaser = tower.TowerType == RougeTowerType.Laser &&
                tower.TargetPriority == RougeTowerTargetPriority.BossFirst;
            bool usesSingleAimDirection = focusedBossLaser || tower.TowerType == RougeTowerType.MachineGun;
            bool rotatesFlameTargets = tower.TowerType == RougeTowerType.Flame &&
                !tower.UsesFlamethrower &&
                tower.TargetPriority != RougeTowerTargetPriority.BossFirst;
            int requestedTargets = rotatesFlameTargets
                ? tower.AttackProjectileCount
                : tower.AttackTargetCount;
            _towerTargetRequests[i] = new RougeTowerTargetRequest
            {
                Position = new float2(position.x, position.z),
                Range = tower.AttackRange,
                TargetCount = usesSingleAimDirection || !tower.IsTargetedDamage
                    ? 1
                    : math.clamp(requestedTargets, 1, FindTowerTargetsJob.MaxTargetsPerTower),
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
        int count = CollectTowerTargets(tower, towerListIndex, 1);
        if (count <= 0) return false;
        tower.targetIndex = _towerTargetIndices[0];
        AimTowerAt(tower, _towerTargetPositions[0]);
        tower.PlayAttackAnimation(null);
        Vector3 start = GetTowerMuzzlePosition(tower);
        Vector3 primaryTarget = _towerTargetPositions[0];
        Vector2 planar = new Vector2(primaryTarget.x - start.x, primaryTarget.z - start.z);
        float distance = Mathf.Max(0.1f, planar.magnitude);
        float2 baseDirection = new float2(planar.x / distance, planar.y / distance);
        int pelletCount = Mathf.Clamp(tower.AttackTargetCount, 1,
            FindTowerTargetsJob.MaxTargetsPerTower);
        bool focusedMode = tower.TargetPriority == RougeTowerTargetPriority.BossFirst;
        float halfSpreadDegrees = MachineGunScatterHalfAngleDegrees *
            (focusedMode ? MachineGunFocusedSpreadMultiplier : 1f);
        float pelletDamage = tower.Damage;
        const float pelletHitRadius = 1.5f;
        RougeMachineGunSpecializationConfig machineGun =
            TowerDefenseVisuals.GetMachineGunSpecializationConfig();
        float criticalChance = tower.UsesMachineGunCritical
            ? tower.HasUpgradedCriticalChance
                ? machineGun.upgradedCriticalChance
                : machineGun.criticalChance
            : 0f;
        float criticalArmorPenetration = tower.HasCriticalArmorPenetration
            ? machineGun.criticalArmorPenetration
            : 0f;
        int fragmentCount = tower.UsesMachineGunFragments && !tower.UsesEmbeddedFragments
            ? tower.HasUpgradedFragmentCount
                ? machineGun.upgradedFragmentCount
                : machineGun.fragmentCount
            : 0;
        float fragmentDamageMultiplier = tower.UsesEmbeddedFragments
            ? machineGun.embeddedFragmentDamageMultiplier
            : machineGun.fragmentDamageMultiplier;
        for (int i = 0; i < pelletCount; i++)
        {
            float spreadDegrees = pelletCount <= 1
                ? 0f
                : Mathf.Lerp(-halfSpreadDegrees, halfSpreadDegrees, i / (float)(pelletCount - 1));
            float spreadRadians = spreadDegrees * Mathf.Deg2Rad;
            float2 direction = Rotate(baseDirection, spreadRadians);
            Vector3 spreadTarget = new Vector3(start.x + direction.x * distance,
                renderHeight + 0.12f, start.z + direction.y * distance);
            SpawnTowerProjectile(RougeTowerType.MachineGun, start, spreadTarget, pelletDamage,
                pelletHitRadius, Mathf.Max(0.04f, distance / 70f), 0f, -1,
                killGoldBonus: tower.KillGoldPercentBonus,
                wealthCellIndexPlusOne: GetTowerWealthCellIndexPlusOne(tower),
                tileEffect: (int)tower.TowerPlaceEffect,
                criticalChance: criticalChance,
                criticalDamageMultiplier: machineGun.criticalDamageMultiplier,
                criticalArmorPenetration: criticalArmorPenetration,
                fragmentTriggerChance: tower.UsesMachineGunFragments &&
                                       !tower.UsesEmbeddedFragments
                    ? machineGun.fragmentTriggerChance
                    : 0f,
                fragmentCount: fragmentCount,
                fragmentDamageMultiplier: fragmentDamageMultiplier,
                fragmentTravelDistance: tower.AttackRange,
                embeddedFragmentChance: tower.UsesEmbeddedFragments
                    ? machineGun.embeddedFragmentChance
                    : 0f);
        }
        return true;
    }

    private void UpdateContinuousLaserTower(RougeDefenseTower tower, int towerListIndex, float dt)
    {
        if (_chargeTowerTargetSelectionActive || _chargeTowerEffectSelectionActive)
        {
            return;
        }

        bool focusedBossMode = tower.TargetPriority == RougeTowerTargetPriority.BossFirst;
        int requestedTargets = focusedBossMode || tower.UsesLaserRefractionAttack
            ? 1
            : tower.AttackTargetCount;
        int count = CollectTowerTargets(tower, towerListIndex, requestedTargets);
        if (count <= 0)
        {
            tower.SetContinuousAttackSound(false);
            tower.HideLaserBeams();
            tower.ResetLaserArmorBreakTracking();
            return;
        }

        tower.SetContinuousAttackSound(true);
        Vector3 start = GetTowerMuzzlePosition(tower);
        tower.targetIndex = _towerTargetIndices[0];
        AimTowerAt(tower, _towerTargetPositions[0]);
        int refractionCount = 0;
        if (tower.UsesLaserRefraction)
        {
            int visualSegmentCount = BuildLaserRefractionTargets(tower, start, count,
                focusedBossMode, out refractionCount);
            tower.ShowLaserSegments(_laserVisualSegmentStarts, _laserVisualSegmentEnds,
                visualSegmentCount);
        }
        else if (focusedBossMode)
        {
            int beamCount = Mathf.Clamp(tower.AttackTargetCount, 1,
                FindTowerTargetsJob.MaxTargetsPerTower);
            tower.ShowFocusedLaserBeams(start, _towerTargetPositions[0], beamCount);
        }
        else
        {
            tower.ShowLaserBeams(start, _towerTargetPositions, count);
        }

        UpdateLaserArmorBreak(tower, count, dt);

        // Damage is authored per attack tick. Keep the beam presentation continuous,
        // but resolve armor once per complete tick and once per beam. Applying the
        // minimum-one-damage rule to fractional frame shares inflated DPS with frame rate.
        tower.attackTimer -= Mathf.Max(0f, dt) * tower.AttackSpeedMultiplier;
        int catchUpTicks = 0;
        while (tower.attackTimer <= 0f && catchUpTicks < 4)
        {
            if (tower.UsesLaserRefractionAttack)
            {
                AccumulateLaserDamage(tower, _towerTargetIndices[0], tower.Damage);
            }
            else if (focusedBossMode)
            {
                int beamCount = Mathf.Clamp(tower.AttackTargetCount, 1,
                    FindTowerTargetsJob.MaxTargetsPerTower);
                for (int beamIndex = 0; beamIndex < beamCount; beamIndex++)
                {
                    AccumulateLaserDamage(tower, _towerTargetIndices[0], tower.Damage);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    AccumulateLaserDamage(tower, _towerTargetIndices[i], tower.Damage);
                }
            }
            for (int i = 0; i < refractionCount; i++)
                AccumulateLaserDamage(tower, _laserRefractionIndices[i],
                    tower.Damage * _laserRefractionDamageMultipliers[i]);
            tower.attackTimer += Mathf.Max(0.001f, tower.AttackInterval);
            catchUpTicks++;
        }
    }

    private void AccumulateLaserDamage(RougeDefenseTower tower, int enemyIndex, float damage)
    {
        AccumulateTowerTargetDamage(tower.TowerType, tower.KillGoldPercentBonus,
            GetTowerWealthCellIndexPlusOne(tower), (int)tower.TowerPlaceEffect,
            enemyIndex, damage);
    }

    private int BuildLaserRefractionTargets(RougeDefenseTower tower, Vector3 towerStart,
        int directTargetCount, bool focusedBossMode, out int refractionCount)
    {
        refractionCount = 0;
        int visualCount = 0;
        int directVisualCount = tower.UsesLaserRefractionAttack
            ? 1
            : focusedBossMode
                ? Mathf.Clamp(tower.AttackTargetCount, 1,
                    FindTowerTargetsJob.MaxTargetsPerTower)
                : directTargetCount;
        for (int i = 0; i < directVisualCount && visualCount < MaximumLaserVisualSegments; i++)
        {
            int targetSlot = focusedBossMode || tower.UsesLaserRefractionAttack ? 0 : i;
            _laserVisualSegmentStarts[visualCount] = towerStart;
            _laserVisualSegmentEnds[visualCount] = _towerTargetPositions[targetSlot];
            visualCount++;
        }

        float range = tower.LaserRefractionRange;
        if (tower.UsesLaserRefractionAttack)
        {
            int maximumTargets = Mathf.Min(tower.LaserRefractionAttackCount,
                MaximumLaserRefractionHits);
            Vector3 source = _towerTargetPositions[0];
            RougeLaserTowerSpecializationConfig config =
                TowerDefenseVisuals.GetLaserSpecializationConfig();
            for (int i = 0; i < maximumTargets; i++)
            {
                int enemyIndex = FindNearestLaserRefractionTarget(source, range,
                    directTargetCount, refractionCount, out Vector3 targetPosition);
                if (enemyIndex < 0) break;
                _laserRefractionIndices[refractionCount] = enemyIndex;
                _laserRefractionPositions[refractionCount] = targetPosition;
                float falloff = Mathf.Min(config.refractionAttackMaximumDamageFalloff,
                    config.refractionAttackDamageFalloffPerTarget * (i + 1));
                _laserRefractionDamageMultipliers[refractionCount] = 1f - falloff;
                refractionCount++;
                if (visualCount < MaximumLaserVisualSegments)
                {
                    _laserVisualSegmentStarts[visualCount] = source;
                    _laserVisualSegmentEnds[visualCount] = targetPosition;
                    visualCount++;
                }
            }
            return visualCount;
        }

        RougeLaserTowerSpecializationConfig laserConfig =
            TowerDefenseVisuals.GetLaserSpecializationConfig();
        int hops = tower.UsesContinuousLaserRefraction
            ? Mathf.Min(3, laserConfig.continuousRefractionCount)
            : 1;
        for (int direct = 0; direct < directTargetCount; direct++)
        {
            Vector3 source = _towerTargetPositions[direct];
            for (int hop = 0; hop < hops && refractionCount < MaximumLaserRefractionHits; hop++)
            {
                int enemyIndex = FindNearestLaserRefractionTarget(source, range,
                    directTargetCount, refractionCount, out Vector3 targetPosition);
                if (enemyIndex < 0) break;
                _laserRefractionIndices[refractionCount] = enemyIndex;
                _laserRefractionPositions[refractionCount] = targetPosition;
                _laserRefractionDamageMultipliers[refractionCount] =
                    tower.UsesContinuousLaserRefraction
                        ? Mathf.Max(0.25f, 0.75f - hop * 0.25f)
                        : laserConfig.refractionDamageMultiplier;
                refractionCount++;
                if (visualCount < MaximumLaserVisualSegments)
                {
                    _laserVisualSegmentStarts[visualCount] = source;
                    _laserVisualSegmentEnds[visualCount] = targetPosition;
                    visualCount++;
                }
                source = targetPosition;
            }
        }
        return visualCount;
    }

    private int FindNearestLaserRefractionTarget(Vector3 source, float range,
        int directTargetCount, int selectedRefractionCount, out Vector3 targetPosition)
    {
        targetPosition = default;
        if (!_enemyTargetCellHeads.IsCreated || !_enemyTargetCellNext.IsCreated ||
            !_positionsA.IsCreated || !_stateA.IsCreated || range <= 0f)
            return -1;

        int bestIndex = -1;
        float bestDistanceSq = range * range;
        float2 center = new float2(source.x, source.z);
        float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
        int2 minCell = RougeMortonGridUtility.WorldToGrid(
            center - new float2(range), _flowGridOrigin, invCellSize, _flowGridDim);
        int2 maxCell = RougeMortonGridUtility.WorldToGrid(
            center + new float2(range), _flowGridOrigin, invCellSize, _flowGridDim);
        for (int y = minCell.y; y <= maxCell.y; y++)
        {
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                int cell = RougeMortonGridUtility.EncodeMorton(x, y);
                for (int enemyIndex = _enemyTargetCellHeads[cell];
                     enemyIndex >= 0;
                     enemyIndex = _enemyTargetCellNext[enemyIndex])
                {
                    if ((uint)enemyIndex >= (uint)_currentMaxEnemies ||
                        (uint)enemyIndex >= (uint)_stateA.Length ||
                        _stateA[enemyIndex].x <= 0f ||
                        IsLaserRefractionTargetExcluded(enemyIndex, directTargetCount,
                            selectedRefractionCount))
                        continue;
                    float4 position = _positionsA[enemyIndex];
                    float distanceSq = math.lengthsq(position.xz - center);
                    if (distanceSq > bestDistanceSq) continue;
                    bestDistanceSq = distanceSq;
                    bestIndex = enemyIndex;
                    targetPosition = new Vector3(position.x,
                        Mathf.Max(renderHeight + 0.8f, position.y + 0.8f), position.z);
                }
            }
        }
        return bestIndex;
    }

    private bool IsLaserRefractionTargetExcluded(int enemyIndex, int directTargetCount,
        int selectedRefractionCount)
    {
        for (int i = 0; i < directTargetCount; i++)
            if (_towerTargetIndices[i] == enemyIndex) return true;
        for (int i = 0; i < selectedRefractionCount; i++)
            if (_laserRefractionIndices[i] == enemyIndex) return true;
        return false;
    }

    private void UpdateLaserArmorBreak(RougeDefenseTower tower, int directTargetCount,
        float deltaTime)
    {
        if (!tower.UsesLaserArmorBreak)
        {
            tower.ResetLaserArmorBreakTracking();
            return;
        }

        RougeLaserTowerSpecializationConfig config =
            TowerDefenseVisuals.GetLaserSpecializationConfig();
        tower.BeginLaserArmorBreakFrame();
        for (int i = 0; i < directTargetCount; i++)
        {
            int enemyIndex = _towerTargetIndices[i];
            if ((uint)enemyIndex >= (uint)_towerDefenseEnemyKinds.Length) continue;
            byte kind = _towerDefenseEnemyKinds[enemyIndex];
            float requiredDuration = (kind & BossEnemyFlag) != 0
                ? config.armorBreakBossDuration
                : (kind & EliteEnemyFlag) != 0
                    ? config.armorBreakEliteDuration
                    : config.armorBreakNormalDuration;
            if (tower.HasAcceleratedLaserArmorBreak)
                requiredDuration *= config.acceleratedArmorBreakDurationMultiplier;
            int reductions = tower.TrackLaserArmorBreakTarget(enemyIndex,
                Mathf.Max(0f, deltaTime), requiredDuration);
            if (reductions > 0) PermanentlyReduceEnemyArmor(enemyIndex, reductions);
        }
        tower.EndLaserArmorBreakFrame();
    }

    private void PermanentlyReduceEnemyArmor(int enemyIndex, int amount)
    {
        if (amount <= 0 || !_effectStateA.IsCreated ||
            (uint)enemyIndex >= (uint)_effectStateA.Length)
            return;
        RougeEnemyEffectState effects = _effectStateA[enemyIndex];
        effects.Armor = Mathf.Clamp(effects.Armor - amount,
            RougeArmorRules.MinimumEnemyArmor, RougeArmorRules.MaximumEnemyArmor);
        _effectStateA[enemyIndex] = effects;
    }

    private bool AccumulateTowerTargetDamage(RougeTowerType towerType, int killGoldBonus,
        int wealthCellIndexPlusOne, int tileEffect, int enemyIndex, float damage,
        float armorPenetration = 0f, float postArmorMultiplier = 1f)
    {
        if (damage <= 0f || (uint)enemyIndex >= (uint)_towerLaserDamage.Length) return false;
        ApplyFrostTileSlowToEnemy(tileEffect, enemyIndex,
            TowerFrostDirectSlowMultiplier);
        damage = ResolveTowerDirectHitDamage(enemyIndex, damage, armorPenetration,
            postArmorMultiplier);
        if (_towerLaserDamageFrames[enemyIndex] != _towerLaserDamageFrame)
        {
            _towerLaserDamageFrames[enemyIndex] = _towerLaserDamageFrame;
            _towerLaserDamage[enemyIndex] = 0f;
            _towerKillGoldBonus[enemyIndex] = 0;
            _towerWealthCellIndexPlusOne[enemyIndex] = 0;
            _towerKillTileEffects[enemyIndex] = (int)RougeTowerPlaceEffect.None;
        }
        float accumulatedBefore = _towerLaserDamage[enemyIndex];
        _towerLaserDamage[enemyIndex] += damage;
        float currentHealth = enemyIndex < _stateA.Length ? _stateA[enemyIndex].x : 0f;
        bool killed = currentHealth > 0f && accumulatedBefore < currentHealth &&
                      _towerLaserDamage[enemyIndex] >= currentHealth;
        if (killed)
        {
            _towerKillGoldBonus[enemyIndex] = Mathf.Max(0, killGoldBonus);
            _towerWealthCellIndexPlusOne[enemyIndex] =
                Mathf.Max(0, wealthCellIndexPlusOne);
            _towerKillTileEffects[enemyIndex] = tileEffect;
        }

        int typeIndex = Mathf.Clamp((int)towerType, 0, TowerDefenseVisuals.TowerTypeCount - 1);
        int entryIndex = enemyIndex * TowerDefenseVisuals.TowerTypeCount + typeIndex;
        if (_towerDamageByTypeFrames[entryIndex] != _towerLaserDamageFrame)
        {
            _towerDamageByTypeFrames[entryIndex] = _towerLaserDamageFrame;
            _towerDamageByType[entryIndex] = 0f;
        }
        _towerDamageByType[entryIndex] += damage;
        return killed;
    }

    private void ApplyFrostTileSlowToEnemy(int tileEffect, int enemyIndex,
        float slowMultiplier)
    {
        if (tileEffect != (int)RougeTowerPlaceEffect.Frost || slowMultiplier <= 0f ||
            !_effectStateA.IsCreated || (uint)enemyIndex >= (uint)_effectStateA.Length)
            return;

        RougeIceTowerSpecializationConfig config =
            TowerDefenseVisuals.GetIceSpecializationConfig();
        RougeEnemyEffectState effects = _effectStateA[enemyIndex];
        effects.SlowStacks = 1f;
        effects.SlowPercent = Mathf.Max(effects.SlowPercent,
            config.frostAttackSlowPercent * Mathf.Clamp01(slowMultiplier));
        effects.SlowTimer = Mathf.Max(effects.SlowTimer, config.frostDurationBonus);
        _effectStateA[enemyIndex] = effects;
    }

    private float ResolveTowerDirectHitDamage(int enemyIndex, float rawDamage,
        float armorPenetration = 0f, float postArmorMultiplier = 1f)
    {
        rawDamage = Mathf.Max(0f, rawDamage);
        if (rawDamage <= 0f || !_effectStateA.IsCreated ||
            (uint)enemyIndex >= (uint)_effectStateA.Length)
            return rawDamage;
        RougeEnemyEffectState effects = _effectStateA[enemyIndex];
        float armor = effects.Armor;
        bool vulnerable = effects.VulnerabilityTimer > 0f;
        if (vulnerable && armor > 0f) armor *= 0.5f;
        float totalPenetration = Mathf.Max(0f, armorPenetration) +
            (vulnerable && effects.VulnerabilityArmorPenetrationTimer > 0f
                ? Mathf.Max(0f, effects.VulnerabilityArmorPenetration)
                : 0f);
        armor -= totalPenetration;
        float resolved = (rawDamage - armor) *
            (1f - armor * RougeArmorRules.DamageReductionPerArmorPoint);
        resolved = Mathf.Max(1f, resolved);
        if (vulnerable)
            resolved *= 1f + Mathf.Max(0f, effects.VulnerabilityDamageBonus);
        return Mathf.Max(1f, resolved * Mathf.Max(0f, postArmorMultiplier));
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
        int visualFlags = (int)math.floor(math.max(_stateA[index].w, 0f) / 10f + 0.0001f);
        if (p.y > renderHeight + 0.05f || (visualFlags & 4) != 0) return false;
        float dx = p.x - origin.x;
        float dz = p.z - origin.z;
        float squareRange = math.sqrt(math.max(0f, rangeSq));
        if (math.max(math.abs(dx), math.abs(dz)) > squareRange) return false;
        position = new Vector3(p.x, Mathf.Max(renderHeight + 0.8f, p.y + 0.8f), p.z);
        return true;
    }

    private static void AimTowerAt(RougeDefenseTower tower, Vector3 target)
    {
        tower.AimAt(target);
    }

    private void BeginProjectileBurst(RougeDefenseTower tower, int towerListIndex,
        Vector3 initialTarget)
    {
        tower.projectileBurstShotsRemaining = Mathf.Max(1, tower.AttackProjectileCount);
        tower.projectileBurstShotIndex = 0;
        tower.projectileBurstTimer = 0f;
        tower.projectileBurstPrimaryTargetIndex = tower.targetIndex;
        tower.projectileBurstPrimaryTarget = initialTarget;
        UpdateProjectileBurst(tower, towerListIndex, 0f);
    }

    private void UpdateProjectileBurst(RougeDefenseTower tower, int towerListIndex, float dt)
    {
        if (tower == null || tower.projectileBurstShotsRemaining <= 0) return;
        tower.projectileBurstTimer -= dt * tower.AttackSpeedMultiplier;
        int catchUpShots = 0;
        while (tower.projectileBurstTimer <= 0f &&
               tower.projectileBurstShotsRemaining > 0 && catchUpShots < 3)
        {
            int targetIndex;
            Vector3 target;
            if (tower.TowerType == RougeTowerType.Cannon)
            {
                targetIndex = tower.projectileBurstPrimaryTargetIndex;
                target = tower.projectileBurstPrimaryTarget;
                float rangeSq = tower.AttackRange * tower.AttackRange;
                if (IsEnemyTargetValid(targetIndex, tower.transform.position, rangeSq,
                    out Vector3 currentTarget))
                {
                    target = currentTarget;
                    tower.projectileBurstPrimaryTarget = currentTarget;
                }
            }
            else
            {
                bool focusedMode = tower.TargetPriority == RougeTowerTargetPriority.BossFirst;
                int requestedTargets = focusedMode ? 1 : tower.AttackProjectileCount;
                int targetCount = CollectTowerTargets(tower, towerListIndex, requestedTargets);
                if (targetCount <= 0)
                {
                    tower.projectileBurstShotsRemaining = 0;
                    tower.targetIndex = -1;
                    break;
                }
                int targetSlot = focusedMode ? 0 : tower.projectileBurstShotIndex % targetCount;
                targetIndex = _towerTargetIndices[targetSlot];
                target = _towerTargetPositions[targetSlot];
            }
            tower.targetIndex = targetIndex;
            AimTowerAt(tower, target);
            tower.PlayAttackAnimation(() =>
            {
                if (tower == null) return;
                Vector3 start = GetTowerMuzzlePosition(tower);
                if (tower.TowerType == RougeTowerType.Cannon)
                {
                    float distance = Vector2.Distance(new Vector2(start.x, start.z),
                        new Vector2(target.x, target.z));
                    RougeCannonSpecializationConfig cannon =
                        TowerDefenseVisuals.GetCannonSpecializationConfig();
                    float explosionRadius = tower.AoeRadius *
                        (tower.HasUpgradedCannonInnerBlast
                            ? cannon.upgradedAoeRadiusMultiplier
                            : 1f);
                    float innerRadiusMultiplier = tower.UsesCannonInnerBlast
                        ? tower.HasUpgradedCannonInnerBlast
                            ? cannon.upgradedInnerRadiusMultiplier
                            : cannon.innerRadiusMultiplier
                        : 0f;
                    float innerDamageMultiplier = tower.HasUpgradedCannonInnerBlast
                        ? cannon.upgradedInnerDamageMultiplier
                        : cannon.innerDamageMultiplier;
                    int persistentTicks = tower.UsesPersistentCannonShell
                        ? cannon.persistentTickCount +
                          (tower.HasUpgradedPersistentCannonTicks
                              ? cannon.upgradedPersistentExtraTicks
                              : 0)
                        : 0;
                    float persistentDamageMultiplier =
                        tower.HasUpgradedPersistentCannonTicks
                            ? cannon.upgradedPersistentDamageMultiplier
                            : cannon.persistentTickDamageMultiplier;
                    SpawnTowerProjectile(RougeTowerType.Cannon, start, target, tower.Damage,
                        explosionRadius, Mathf.Clamp(distance / 38f, 0.12f, 0.65f), 0f, -1,
                        killGoldBonus: tower.KillGoldPercentBonus,
                        wealthCellIndexPlusOne: GetTowerWealthCellIndexPlusOne(tower),
                        tileEffect: (int)tower.TowerPlaceEffect,
                        cannonInnerRadiusMultiplier: innerRadiusMultiplier,
                        cannonInnerDamageMultiplier: innerDamageMultiplier,
                        cannonSecondaryTriggerChance:
                            tower.HasCannonSecondaryBombardment
                                ? cannon.secondaryTriggerChance
                                : 0f,
                        cannonSecondaryProjectileCount: cannon.secondaryProjectileCount,
                        cannonSecondaryDamageMultiplier: cannon.secondaryDamageMultiplier,
                        cannonSecondaryRadiusMultiplier: cannon.secondaryRadiusMultiplier,
                        cannonSecondaryFlightDuration: cannon.secondaryFlightDuration,
                        cannonSecondaryTravelDistanceMultiplier:
                            cannon.secondaryTravelDistanceMultiplier,
                        cannonSecondaryArcHeightMultiplier:
                            cannon.secondaryArcHeightMultiplier,
                        cannonPersistentLandingDamageMultiplier:
                            tower.UsesPersistentCannonShell
                                ? cannon.persistentLandingDamageMultiplier
                                : 0f,
                        cannonPersistentTickInterval: cannon.persistentTickInterval,
                        cannonPersistentTickDamageMultiplier: persistentDamageMultiplier,
                        cannonPersistentTickCount: persistentTicks,
                        cannonPersistentKnockbackForce:
                            tower.HasPersistentCannonKnockback
                                ? cannon.persistentKnockbackForce
                                : 0f);
                }
                else
                {
                    // The release frame happens after the authored firing animation. The
                    // original target may have died and its array slot may already belong
                    // to an enemy at a distant spawn point, so resolve it again now.
                    Vector3 flameLandingTarget = target;
                    float rangeSq = tower.AttackRange * tower.AttackRange;
                    if (IsEnemyTargetValid(targetIndex, tower.transform.position, rangeSq,
                        out Vector3 currentFlameTarget))
                        flameLandingTarget = currentFlameTarget;
                    Vector2 landingOffset = UnityEngine.Random.insideUnitCircle *
                        (tower.AoeRadius * FlameLandingOffsetRadiusMultiplier);
                    // Fireballs target a ground location rather than homing on an enemy
                    // index. This prevents later slot reuse from teleporting the AOE.
                    RougeFlameTowerSpecializationConfig flame =
                        TowerDefenseVisuals.GetFlameSpecializationConfig();
                    float burnTickInterval = tower.UsesStackingBurn
                        ? flame.burnTickInterval /
                          Mathf.Max(0.01f, 1f + flame.burnSpeedBonus)
                        : flame.burnTickInterval;
                    SpawnTowerProjectile(RougeTowerType.Flame, start, flameLandingTarget,
                        tower.Damage, tower.AoeRadius, 0.85f, 8f, -1,
                        tower.EffectDuration, tower.TickInterval,
                        targetOffset: landingOffset, killGoldBonus: tower.KillGoldPercentBonus,
                        wealthCellIndexPlusOne: GetTowerWealthCellIndexPlusOne(tower),
                        tileEffect: (int)tower.TowerPlaceEffect,
                        burnDamage: tower.AppliesTowerBurn
                            ? tower.Damage * flame.burnDamageMultiplier
                            : 0f,
                        burnDuration: tower.AppliesTowerBurn ? flame.burnDuration : 0f,
                        burnTickInterval: burnTickInterval,
                        burnMaximumStacks: tower.UsesStackingBurn
                            ? flame.maximumBurnStacks
                            : 1,
                        burnDamageBonusPerStack: tower.UsesStackingBurn
                            ? flame.damageBonusPerStack
                            : 0f,
                        conflagrationDamage: tower.UsesConflagration
                            ? tower.Damage * flame.conflagrationDamageMultiplier
                            : 0f);
                }
            });
            tower.projectileBurstShotsRemaining--;
            tower.projectileBurstShotIndex++;
            tower.projectileBurstTimer += TowerProjectileBurstInterval;
            catchUpShots++;
        }

        if (tower.projectileBurstShotsRemaining <= 0 && tower.EchoAttackCycleActive)
            CompleteEchoAttackStep(tower);
    }

    private void FireTower(RougeDefenseTower tower, int towerListIndex, Vector3 target)
    {
        switch (tower.TowerType)
        {
            case RougeTowerType.Ice:
            {
                tower.PlayAttackAnimation(() =>
                {
                    if (tower == null) return;
                    RougeIceTowerSpecializationConfig config =
                        TowerDefenseVisuals.GetIceSpecializationConfig();
                    Vector3 p = tower.transform.position;
                    bool freezes = tower.UsesIceFreeze;
                    bool appliesVulnerability = tower.UsesIceVulnerability;
                    float frostDurationBonus = tower.IsOnFrostTile
                        ? config.frostDurationBonus
                        : 0f;
                    float normalFreeze = config.freezeNormalDuration + frostDurationBonus;
                    float eliteFreeze = config.freezeEliteDuration + frostDurationBonus;
                    float bossFreeze = config.freezeBossDuration + frostDurationBonus;
                    TryAddTowerDirectDamageArea(new RougeSkillArea
                    {
                        Type = 13,
                        Position = new float2(p.x, p.z),
                        Radius = tower.AttackRange,
                        Damage = tower.Damage,
                        EffectFlags = freezes
                            ? (int)SkillHitEffectTag.Freeze
                            : (int)SkillHitEffectTag.Slow,
                        EffectSlowPercent = freezes ? 0f : config.slowPercent,
                        EffectSlowDuration = freezes
                            ? 0f
                            : config.slowDuration + frostDurationBonus,
                        EffectFreezeDuration = freezes ? normalFreeze : 0f,
                        EffectEliteFreezeDuration = freezes ? eliteFreeze : 0f,
                        EffectBossFreezeDuration = freezes ? bossFreeze : 0f,
                        EffectBossFreezeImmunityDuration = freezes
                            ? config.freezeBossImmunityDuration
                            : 0f,
                        EffectVulnerabilityDuration = appliesVulnerability
                            ? config.vulnerabilityDuration + frostDurationBonus
                            : 0f,
                        EffectVulnerabilityDamageBonus =
                            tower.AmplifiesVulnerableDamage
                                ? config.vulnerabilityDamageBonus
                                : 0f,
                        EffectVulnerabilityEliteScale = config.vulnerabilityEliteScale,
                        EffectVulnerabilityBossScale = config.vulnerabilityBossScale,
                        EffectVulnerabilityArmorPenetration =
                            tower.AddsVulnerableArmorPenetration
                                ? RougeArmorRules.VulnerableArmorPenetration
                                : 0f,
                        SourceTowerTypePlusOne = (int)tower.TowerType + 1,
                        SourceTowerTileEffect = (int)tower.TowerPlaceEffect,
                        SourceTowerKillGoldBonus = tower.KillGoldPercentBonus,
                        SourceTowerWealthCellIndexPlusOne =
                            GetTowerWealthCellIndexPlusOne(tower)
                    }, TowerFrostAreaSlowMultiplier);
                    SpawnAOERing(new Vector3(p.x, renderHeight + 0.08f, p.z), tower.AttackRange, 0.45f,
                        new Color(0.2f, 0.85f, 1f, 1f));
                    CompleteEchoAttackStep(tower);
                });
                break;
            }
            case RougeTowerType.Cannon:
                BeginProjectileBurst(tower, towerListIndex, target);
                break;
            case RougeTowerType.Flame:
                if (tower.UsesFlamethrower) FireFlamethrower(tower, target);
                else BeginProjectileBurst(tower, towerListIndex, target);
                break;
            case RougeTowerType.PiercingLaser:
            {
                StartPiercingLaserAttack(tower, target);
                break;
            }
            case RougeTowerType.OrbitSphere:
                tower.PlayAttackAnimation(() => { StartOrbitSphereAttack(tower); });
                break;
            case RougeTowerType.RocketBarrage:
                StartRocketBarrage(tower);
                break;
        }
    }

    private void FireFlamethrower(RougeDefenseTower tower, Vector3 target,
        int shotCount = 1)
    {
        if (tower == null) return;
        RougeFlameTowerSpecializationConfig flame =
            TowerDefenseVisuals.GetFlameSpecializationConfig();
        Vector3 origin3 = tower.transform.position;
        Vector2 aim = new Vector2(target.x - origin3.x, target.z - origin3.z);
        if (tower.UsesRotatingFlamethrower)
        {
            Vector3 rotatingDirection = tower.GetCurrentAimDirection();
            aim = new Vector2(rotatingDirection.x, rotatingDirection.z);
        }
        if (aim.sqrMagnitude <= 0.0001f) aim = Vector2.up;
        aim.Normalize();

        int configuredJets = Mathf.Max(1, tower.AttackProjectileCount);
        bool focusedFan = tower.UsesFanFlamethrower &&
            tower.TargetPriority == RougeTowerTargetPriority.BossFirst;
        int emittedJets = focusedFan ? 1 : configuredJets;
        float coneAngle = flame.flamethrowerAngle + (focusedFan
            ? configuredJets * flame.focusedAnglePerProjectile
            : 0f);
        bool showPresentation = tower.flamethrowerPresentationTimer <= 0f;
        for (int jet = 0; jet < emittedJets; jet++)
        {
            float offsetDegrees = 0f;
            if (emittedJets > 1)
            {
                if (tower.UsesFanFlamethrower)
                {
                    float spacing = flame.flamethrowerAngle +
                                    flame.fanSpacingPaddingDegrees;
                    offsetDegrees = (jet - (emittedJets - 1) * 0.5f) * spacing;
                }
                else
                {
                    offsetDegrees = 360f * jet / emittedJets;
                }
            }
            float radians = offsetDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            float2 direction = new float2(
                aim.x * cos - aim.y * sin,
                aim.x * sin + aim.y * cos);
            TryAddTowerDirectDamageArea(new RougeSkillArea
            {
                Type = 22,
                Position = new float2(origin3.x, origin3.z),
                Direction = direction,
                Radius = tower.AttackRange,
                Damage = tower.Damage * Mathf.Max(1, shotCount),
                AuxA = coneAngle * 0.5f,
                SourceTowerTypePlusOne = (int)RougeTowerType.Flame + 1,
                SourceTowerTileEffect = (int)tower.TowerPlaceEffect,
                SourceTowerKillGoldBonus = tower.KillGoldPercentBonus,
                SourceTowerWealthCellIndexPlusOne =
                    GetTowerWealthCellIndexPlusOne(tower)
            }, TowerFrostAreaSlowMultiplier);
            if (showPresentation)
                SpawnTowerFlameJetVisual(tower.GetShootPosition(), direction,
                    tower.AttackRange, coneAngle);
        }

        if (showPresentation)
        {
            tower.PlayAttackAnimation(null);
            tower.flamethrowerPresentationTimer = 0.18f;
        }
        CompleteEchoAttackStep(tower);
    }

    private void SpawnTowerFlameJetVisual(Vector3 start, float2 direction,
        float range, float coneAngle)
    {
        GameObject root = null;
        while (_towerFlameJetVisualPool.Count > 0 && root == null)
            root = _towerFlameJetVisualPool.Pop();
        LineRenderer line;
        if (root == null)
        {
            root = new GameObject("Flamethrower Jet");
            root.transform.SetParent(transform, false);
            line = root.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }
        else
        {
            line = root.GetComponent<LineRenderer>();
        }
        if (line == null) return;
        if (_towerFlameJetMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                _towerFlameJetMaterial = new Material(shader)
                {
                    name = "Tower Flamethrower Jet Material",
                    hideFlags = HideFlags.DontSave
                };
            }
        }
        line.sharedMaterial = _towerFlameJetMaterial;
        float2 normalized = math.normalizesafe(direction, new float2(0f, 1f));
        Vector3 flatStart = new Vector3(start.x,
            Mathf.Max(renderHeight + 0.35f, start.y), start.z);
        Vector3 end = flatStart + new Vector3(normalized.x, 0f, normalized.y) * range;
        line.SetPosition(0, flatStart);
        line.SetPosition(1, end);
        float startWidth = 0.38f;
        float endWidth = Mathf.Clamp(2f * range *
            Mathf.Tan(Mathf.Clamp(coneAngle * 0.5f, 0f, 80f) * Mathf.Deg2Rad),
            0.8f, 14f);
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.startColor = new Color(1f, 0.92f, 0.25f, 0.72f);
        line.endColor = new Color(1f, 0.08f, 0.01f, 0.04f);
        root.SetActive(true);
        _towerFlameJetVisuals.Add(new TowerFlameJetVisual
        {
            Root = root,
            Line = line,
            Remaining = 0.14f,
            Duration = 0.14f,
            StartWidth = startWidth,
            EndWidth = endWidth
        });
    }

    private void UpdateTowerFlameJetVisuals(float dt)
    {
        for (int i = _towerFlameJetVisuals.Count - 1; i >= 0; i--)
        {
            TowerFlameJetVisual visual = _towerFlameJetVisuals[i];
            visual.Remaining -= Mathf.Max(0f, dt);
            if (visual.Remaining <= 0f || visual.Root == null || visual.Line == null)
            {
                if (visual.Root != null)
                {
                    visual.Root.SetActive(false);
                    _towerFlameJetVisualPool.Push(visual.Root);
                }
                _towerFlameJetVisuals.RemoveAt(i);
                continue;
            }
            float life = Mathf.Clamp01(visual.Remaining /
                                       Mathf.Max(0.01f, visual.Duration));
            visual.Line.startWidth = visual.StartWidth * (0.7f + life * 0.3f);
            visual.Line.endWidth = visual.EndWidth * (0.6f + life * 0.4f);
            visual.Line.startColor = new Color(1f, 0.92f, 0.25f, life * 0.72f);
            visual.Line.endColor = new Color(1f, 0.08f, 0.01f, life * 0.08f);
            _towerFlameJetVisuals[i] = visual;
        }
    }

    private bool CompleteEchoAttackStep(RougeDefenseTower tower)
    {
        if (tower == null || !tower.EchoAttackCycleActive) return false;
        if (tower.TryScheduleEchoAttackRepeat(EchoAttackRepeatDelay)) return true;

        tower.FinishEchoAttackCycle();
        return true;
    }

    private bool StartOrbitSphereAttack(RougeDefenseTower tower)
    {
        if (tower == null) return false;
        for (int i = 0; i < _activeOrbitSphereAttacks.Count; i++)
        {
            if (_activeOrbitSphereAttacks[i].Tower == tower) return false;
        }

        int sphereCount = Mathf.Max(1, tower.AttackProjectileCount);
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
                    _activeOrbitSphereAttacks.RemoveAt(attackIndex);
                    if (!CompleteEchoAttackStep(tower))
                        tower.attackTimer = tower.AttackInterval;
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
                    Vector3 towerPosition = tower.transform.position;
                    float2 lineStart = new float2(towerPosition.x, towerPosition.z);
                    float2 lineEnd = new float2(position.x, position.z);
                    float2 lineDelta = lineEnd - lineStart;
                    float lineLength = math.max(math.length(lineDelta), 0.001f);
                    TryAddTowerDirectDamageArea(new RougeSkillArea
                    {
                        Type = 15,
                        Position = lineStart,
                        Direction = lineDelta / lineLength,
                        Length = lineLength,
                        Radius = sphereRadius,
                        Damage = tower.Damage,
                        // Positive angular speed moves counter-clockwise in X/Z, so the
                        // corresponding beam tangent is (-direction.y, direction.x).
                        AuxA = tower.OrbitAngularSpeed < 0f ? -1f : 1f,
                        SourceTowerTypePlusOne = (int)RougeTowerType.OrbitSphere + 1,
                        SourceTowerTileEffect = (int)tower.TowerPlaceEffect,
                        SourceTowerKillGoldBonus = tower.KillGoldPercentBonus,
                        SourceTowerWealthCellIndexPlusOne =
                            GetTowerWealthCellIndexPlusOne(tower)
                    }, TowerFrostAreaSlowMultiplier);
                }
                attack.DamageTimer += damageInterval;
            }
        }
    }

    private void RenderOrbitSphereVisuals()
    {
        // OrbitSphere attacks are now rendered as thin crystal lasers by the tower itself.
        RenderRocketBarrageMissiles();
    }

    private static Vector3 GetTowerMuzzlePosition(RougeDefenseTower tower)
    {
        return tower.GetShootPosition();
    }

    private void SpawnTowerProjectile(RougeTowerType type, Vector3 start, Vector3 end, float damage, float radius,
        float duration, float arcHeight, int targetIndex, float effectDuration = 0f, float tickInterval = 0f,
        float visualScaleMultiplier = 1f, Vector2 targetOffset = default, int killGoldBonus = 0,
        int wealthCellIndexPlusOne = 0, int tileEffect = 0, float criticalChance = 0f,
        float criticalDamageMultiplier = 1f, float criticalArmorPenetration = 0f,
        float fragmentTriggerChance = 0f, int fragmentCount = 0,
        float fragmentDamageMultiplier = 0f, float fragmentTravelDistance = 0f,
        int machineGunProjectileMode = MachineGunProjectileNormal,
        float embeddedFragmentChance = 0f, float cannonInnerRadiusMultiplier = 0f,
        float cannonInnerDamageMultiplier = 1f, float cannonSecondaryTriggerChance = 0f,
        int cannonSecondaryProjectileCount = 0,
        float cannonSecondaryDamageMultiplier = 0f,
        float cannonSecondaryRadiusMultiplier = 0f,
        float cannonSecondaryFlightDuration = 0f,
        float cannonSecondaryTravelDistanceMultiplier = 0f,
        float cannonSecondaryArcHeightMultiplier = 0f,
        float cannonPersistentLandingDamageMultiplier = 0f,
        float cannonPersistentTickInterval = 0f,
        float cannonPersistentTickDamageMultiplier = 0f,
        int cannonPersistentTickCount = 0,
        float cannonPersistentKnockbackForce = 0f, float burnDamage = 0f,
        float burnDuration = 0f, float burnTickInterval = 0f,
        int burnMaximumStacks = 1, float burnDamageBonusPerStack = 0f,
        float conflagrationDamage = 0f)
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
            End = new Vector3(end.x + targetOffset.x, renderHeight + 0.2f,
                end.z + targetOffset.y),
            Elapsed = 0f,
            Duration = Mathf.Max(0.02f, duration),
            ArcHeight = arcHeight,
            Damage = damage,
            Radius = radius,
            EffectDuration = effectDuration,
            TickInterval = tickInterval,
            BurnDamage = Mathf.Max(0f, burnDamage),
            BurnDuration = Mathf.Max(0f, burnDuration),
            BurnTickInterval = Mathf.Max(0f, burnTickInterval),
            BurnMaximumStacks = Mathf.Max(1, burnMaximumStacks),
            BurnDamageBonusPerStack = Mathf.Max(0f, burnDamageBonusPerStack),
            ConflagrationDamage = Mathf.Max(0f, conflagrationDamage),
            TargetIndex = targetIndex,
            TargetOffset = targetOffset,
            KillGoldBonus = killGoldBonus,
            WealthCellIndexPlusOne = Mathf.Max(0, wealthCellIndexPlusOne),
            TileEffect = tileEffect,
            CriticalChance = Mathf.Clamp01(criticalChance),
            CriticalDamageMultiplier = Mathf.Max(1f, criticalDamageMultiplier),
            CriticalArmorPenetration = Mathf.Max(0f, criticalArmorPenetration),
            FragmentTriggerChance = Mathf.Clamp01(fragmentTriggerChance),
            FragmentCount = Mathf.Max(0, fragmentCount),
            FragmentDamageMultiplier = Mathf.Max(0f, fragmentDamageMultiplier),
            FragmentTravelDistance = Mathf.Max(0f, fragmentTravelDistance),
            MachineGunProjectileMode = machineGunProjectileMode,
            EmbeddedFragmentChance = Mathf.Clamp01(embeddedFragmentChance),
            CannonInnerRadiusMultiplier = Mathf.Max(0f, cannonInnerRadiusMultiplier),
            CannonInnerDamageMultiplier = Mathf.Max(1f, cannonInnerDamageMultiplier),
            CannonSecondaryTriggerChance = Mathf.Clamp01(cannonSecondaryTriggerChance),
            CannonSecondaryProjectileCount = Mathf.Max(0, cannonSecondaryProjectileCount),
            CannonSecondaryDamageMultiplier = Mathf.Max(0f, cannonSecondaryDamageMultiplier),
            CannonSecondaryRadiusMultiplier = Mathf.Max(0f, cannonSecondaryRadiusMultiplier),
            CannonSecondaryFlightDuration = Mathf.Max(0f, cannonSecondaryFlightDuration),
            CannonSecondaryTravelDistanceMultiplier = Mathf.Max(0f,
                cannonSecondaryTravelDistanceMultiplier),
            CannonSecondaryArcHeightMultiplier = Mathf.Max(0f,
                cannonSecondaryArcHeightMultiplier),
            CannonPersistentLandingDamageMultiplier = Mathf.Max(0f,
                cannonPersistentLandingDamageMultiplier),
            CannonPersistentTickInterval = Mathf.Max(0f, cannonPersistentTickInterval),
            CannonPersistentTickDamageMultiplier = Mathf.Max(0f,
                cannonPersistentTickDamageMultiplier),
            CannonPersistentTickCount = Mathf.Max(0, cannonPersistentTickCount),
            CannonPersistentKnockbackForce = Mathf.Max(0f,
                cannonPersistentKnockbackForce)
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
            float previousT = Mathf.Clamp01(projectile.Elapsed / projectile.Duration);
            projectile.Elapsed += dt;
            if (projectile.TargetIndex >= 0 && projectile.TargetIndex < _currentMaxEnemies &&
                projectile.TargetIndex < _stateA.Length && _stateA[projectile.TargetIndex].x > 0f)
            {
                float4 target = _positionsA[projectile.TargetIndex];
                projectile.End = new Vector3(target.x + projectile.TargetOffset.x,
                    renderHeight + 0.2f, target.z + projectile.TargetOffset.y);
            }
            float t = Mathf.Clamp01(projectile.Elapsed / projectile.Duration);
            Vector3 previousPosition = Vector3.Lerp(projectile.Start, projectile.End,
                previousT);
            previousPosition.y += Mathf.Sin(previousT * Mathf.PI) * projectile.ArcHeight;
            Vector3 position = Vector3.Lerp(projectile.Start, projectile.End, t);
            position.y += Mathf.Sin(t * Mathf.PI) * projectile.ArcHeight;
            if (projectile.Visual != null)
            {
                projectile.Visual.transform.position = position;
                RougeBillboard billboard = projectile.Visual.GetComponent<RougeBillboard>();
                if (billboard != null) billboard.SetWorldDirection(projectile.End - projectile.Start);
            }

            bool isMachineGunFragment = projectile.Type == RougeTowerType.MachineGun &&
                                        projectile.MachineGunProjectileMode !=
                                        MachineGunProjectileNormal;
            if (isMachineGunFragment && projectile.Damage > 0f &&
                TryFindEnemyAlongMachineGunPath(
                    new float2(previousPosition.x, previousPosition.z),
                    new float2(position.x, position.z), projectile.Radius,
                    out int sweptEnemyIndex))
            {
                ResolveMachineGunProjectileHit(projectile, sweptEnemyIndex);
                RecycleTowerProjectileVisual(projectile.Visual);
                int sweptLast = _towerProjectiles.Count - 1;
                _towerProjectiles[i] = _towerProjectiles[sweptLast];
                _towerProjectiles.RemoveAt(sweptLast);
                continue;
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
        if (projectile.Type == RougeTowerType.MachineGun)
        {
            bool isFragment = projectile.MachineGunProjectileMode !=
                              MachineGunProjectileNormal;
            bool foundEnemy = isFragment
                ? TryFindEnemyAlongMachineGunPath(
                    new float2(projectile.Start.x, projectile.Start.z), impact,
                    projectile.Radius, out int enemyIndex)
                : TryFindEnemyAtMachineGunImpact(impact, projectile.Radius, out enemyIndex);
            if (projectile.Damage > 0f && foundEnemy)
                ResolveMachineGunProjectileHit(projectile, enemyIndex);
            return;
        }
        if (projectile.Type == RougeTowerType.Cannon)
        {
            ResolveCannonProjectileImpact(projectile);
            return;
        }
        if (projectile.Type == RougeTowerType.Flame)
        {
            AddTowerFireZone(projectile.End, projectile.Radius, projectile.EffectDuration,
                projectile.Damage, projectile.TickInterval, projectile.KillGoldBonus,
                projectile.WealthCellIndexPlusOne, projectile.TileEffect,
                projectile.BurnDamage, projectile.BurnDuration,
                projectile.BurnTickInterval, projectile.BurnMaximumStacks,
                projectile.BurnDamageBonusPerStack, projectile.ConflagrationDamage);
            SpawnAOERing(projectile.End, projectile.Radius, 0.38f, new Color(1f, 0.24f, 0.04f, 1f));
            return;
        }

        TryAddTowerDirectDamageArea(new RougeSkillArea
        {
            Type = 13,
            Position = impact,
            Radius = projectile.Radius,
            Damage = projectile.Damage,
            SourceTowerTypePlusOne = (int)projectile.Type + 1,
            SourceTowerTileEffect = projectile.TileEffect,
            SourceTowerKillGoldBonus = projectile.KillGoldBonus,
            SourceTowerWealthCellIndexPlusOne = projectile.WealthCellIndexPlusOne
        }, TowerFrostAreaSlowMultiplier);
    }

    private void ResolveCannonProjectileImpact(TowerProjectile projectile)
    {
        float landingDamage = projectile.CannonPersistentTickCount > 0
            ? projectile.Damage * projectile.CannonPersistentLandingDamageMultiplier
            : projectile.Damage;
        AddCannonDamageArea(projectile.End, projectile.Radius, landingDamage,
            projectile.CannonPersistentKnockbackForce, projectile.KillGoldBonus,
            projectile.WealthCellIndexPlusOne, projectile.TileEffect,
            projectile.CannonInnerRadiusMultiplier,
            projectile.CannonInnerDamageMultiplier);

        if (projectile.CannonPersistentTickCount > 0)
        {
            AddPersistentCannonZone(projectile.End, projectile.Radius,
                projectile.Damage * projectile.CannonPersistentTickDamageMultiplier,
                projectile.CannonPersistentTickInterval,
                projectile.CannonPersistentTickCount,
                projectile.CannonPersistentKnockbackForce,
                projectile.KillGoldBonus, projectile.WealthCellIndexPlusOne,
                projectile.TileEffect);
        }

        if (projectile.CannonSecondaryProjectileCount > 0 &&
            UnityEngine.Random.value < projectile.CannonSecondaryTriggerChance)
        {
            SpawnSecondaryCannonProjectiles(projectile);
        }

        SpawnExplosionVFX(projectile.End + Vector3.up * 0.4f,
            projectile.Radius * 0.75f);
        SpawnAOERing(projectile.End, projectile.Radius, 0.38f,
            new Color(1f, 0.42f, 0.08f, 1f));
    }

    private void AddCannonDamageArea(Vector3 position, float radius, float damage,
        float knockbackForce, int killGoldBonus, int wealthCellIndexPlusOne,
        int tileEffect, float innerRadiusMultiplier = 0f,
        float innerDamageMultiplier = 1f,
        float frostSlowMultiplier = TowerFrostAreaSlowMultiplier)
    {
        if (damage <= 0f || radius <= 0f) return;
        TryAddTowerDirectDamageArea(new RougeSkillArea
        {
            Type = 13,
            Position = new float2(position.x, position.z),
            Radius = radius,
            Damage = damage,
            PullForce = Mathf.Max(0f, knockbackForce),
            AuxA = Mathf.Max(0f, innerRadiusMultiplier),
            AuxB = Mathf.Max(1f, innerDamageMultiplier),
            SourceTowerTypePlusOne = (int)RougeTowerType.Cannon + 1,
            SourceTowerTileEffect = tileEffect,
            SourceTowerKillGoldBonus = killGoldBonus,
            SourceTowerWealthCellIndexPlusOne = wealthCellIndexPlusOne
        }, frostSlowMultiplier);
    }

    private void ResolveMachineGunProjectileHit(TowerProjectile projectile,
        int enemyIndex)
    {
        bool critical = projectile.MachineGunProjectileMode ==
                        MachineGunProjectileNormal &&
                        UnityEngine.Random.value < projectile.CriticalChance;
        bool killed = AccumulateTowerTargetDamage(RougeTowerType.MachineGun,
            projectile.KillGoldBonus, projectile.WealthCellIndexPlusOne,
            projectile.TileEffect, enemyIndex, projectile.Damage,
            critical ? projectile.CriticalArmorPenetration : 0f,
            critical ? projectile.CriticalDamageMultiplier : 1f);
        if (projectile.MachineGunProjectileMode == MachineGunProjectileNormal &&
            projectile.EmbeddedFragmentChance > 0f &&
            UnityEngine.Random.value < projectile.EmbeddedFragmentChance)
        {
            AddEmbeddedMachineGunFragment(enemyIndex,
                projectile.Damage * projectile.FragmentDamageMultiplier,
                projectile.FragmentTravelDistance, projectile.KillGoldBonus,
                projectile.WealthCellIndexPlusOne, projectile.TileEffect);
        }
        if (!killed || projectile.MachineGunProjectileMode !=
            MachineGunProjectileNormal || projectile.FragmentCount <= 0 ||
            projectile.EmbeddedFragmentChance > 0f ||
            UnityEngine.Random.value >= projectile.FragmentTriggerChance)
            return;

        float4 enemyPosition = _positionsA[enemyIndex];
        SpawnMachineGunFragments(enemyPosition.xz, projectile.FragmentCount,
            projectile.Damage * projectile.FragmentDamageMultiplier,
            projectile.FragmentTravelDistance, MachineGunProjectileFragment,
            0f, projectile.KillGoldBonus,
            projectile.WealthCellIndexPlusOne, projectile.TileEffect);
    }

    private void SpawnSecondaryCannonProjectiles(TowerProjectile source)
    {
        int count = Mathf.Max(1, source.CannonSecondaryProjectileCount);
        float travelDistance = source.Radius *
                               source.CannonSecondaryTravelDistanceMultiplier;
        float phase = UnityEngine.Random.value * Mathf.PI * 2f;
        Vector3 start = source.End;
        start.y = renderHeight + 0.2f;
        for (int i = 0; i < count; i++)
        {
            float angle = phase + Mathf.PI * 2f * i / count;
            Vector3 end = new Vector3(start.x + Mathf.Cos(angle) * travelDistance,
                renderHeight + 0.2f, start.z + Mathf.Sin(angle) * travelDistance);
            SpawnTowerProjectile(RougeTowerType.Cannon, start, end,
                source.Damage * source.CannonSecondaryDamageMultiplier,
                source.Radius * source.CannonSecondaryRadiusMultiplier,
                source.CannonSecondaryFlightDuration,
                source.Radius * source.CannonSecondaryArcHeightMultiplier, -1,
                visualScaleMultiplier: 0.55f,
                killGoldBonus: source.KillGoldBonus,
                wealthCellIndexPlusOne: source.WealthCellIndexPlusOne,
                tileEffect: source.TileEffect);
        }
    }

    private void AddPersistentCannonZone(Vector3 position, float radius,
        float damagePerTick, float tickInterval, int tickCount, float knockbackForce,
        int killGoldBonus, int wealthCellIndexPlusOne, int tileEffect)
    {
        GameObject visual = GetTowerProjectileVisual(RougeTowerType.Cannon);
        visual.name = "Persistent Cannon Shell";
        visual.transform.position = new Vector3(position.x, renderHeight + 0.14f,
            position.z);
        visual.SetActive(true);
        _towerPersistentCannonZones.Add(new TowerPersistentCannonZone
        {
            Visual = visual,
            Position = position,
            Radius = Mathf.Max(0.01f, radius),
            DamagePerTick = Mathf.Max(0f, damagePerTick),
            TickInterval = Mathf.Max(0.01f, tickInterval),
            TickTimer = Mathf.Max(0.01f, tickInterval),
            RemainingTicks = Mathf.Max(1, tickCount),
            KnockbackForce = Mathf.Max(0f, knockbackForce),
            KillGoldBonus = killGoldBonus,
            WealthCellIndexPlusOne = wealthCellIndexPlusOne,
            TileEffect = tileEffect
        });
    }

    private void UpdateTowerPersistentCannonZones(float dt)
    {
        for (int i = _towerPersistentCannonZones.Count - 1; i >= 0; i--)
        {
            TowerPersistentCannonZone zone = _towerPersistentCannonZones[i];
            zone.TickTimer -= dt;
            int ticksThisFrame = 0;
            while (zone.TickTimer <= 0f && zone.RemainingTicks > 0 &&
                   ticksThisFrame < 4)
            {
                AddCannonDamageArea(zone.Position, zone.Radius, zone.DamagePerTick,
                    zone.KnockbackForce, zone.KillGoldBonus,
                    zone.WealthCellIndexPlusOne, zone.TileEffect,
                    frostSlowMultiplier: 0f);
                SpawnExplosionVFX(zone.Position + Vector3.up * 0.25f,
                    zone.Radius * 0.35f);
                SpawnAOERing(zone.Position, zone.Radius, 0.22f,
                    new Color(1f, 0.55f, 0.12f, 1f));
                zone.RemainingTicks--;
                zone.TickTimer += zone.TickInterval;
                ticksThisFrame++;
            }

            if (zone.RemainingTicks <= 0)
            {
                RecycleTowerProjectileVisual(zone.Visual);
                _towerPersistentCannonZones.RemoveAt(i);
                continue;
            }
            _towerPersistentCannonZones[i] = zone;
        }
    }

    private void SpawnMachineGunFragments(float2 origin, int count, float damage,
        float travelDistance, int projectileMode, float embeddedFragmentChance,
        int killGoldBonus = 0, int wealthCellIndexPlusOne = 0, int tileEffect = 0)
    {
        count = Mathf.Max(1, count);
        damage = Mathf.Max(0f, damage);
        if (count > MaximumMachineGunFragmentsPerBurst)
        {
            damage *= count / (float)MaximumMachineGunFragmentsPerBurst;
            count = MaximumMachineGunFragmentsPerBurst;
        }
        travelDistance = Mathf.Max(0.1f, travelDistance);
        RougeMachineGunSpecializationConfig config =
            TowerDefenseVisuals.GetMachineGunSpecializationConfig();
        Vector3 start = new Vector3(origin.x, renderHeight + 0.2f, origin.y);
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.PI * 2f * i / count;
            Vector3 end = new Vector3(origin.x + Mathf.Cos(angle) * travelDistance,
                renderHeight + 0.2f, origin.y + Mathf.Sin(angle) * travelDistance);
            SpawnTowerProjectile(RougeTowerType.MachineGun, start, end, damage,
                config.fragmentHitRadius,
                Mathf.Max(0.02f, travelDistance / config.fragmentSpeed), 0f, -1,
                visualScaleMultiplier: 0.72f,
                killGoldBonus: killGoldBonus,
                wealthCellIndexPlusOne: wealthCellIndexPlusOne,
                tileEffect: tileEffect,
                fragmentTravelDistance: travelDistance,
                machineGunProjectileMode: projectileMode,
                embeddedFragmentChance: embeddedFragmentChance);
        }
    }

    private void AddEmbeddedMachineGunFragment(int enemyIndex, float damage,
        float travelDistance, int killGoldBonus, int wealthCellIndexPlusOne,
        int tileEffect)
    {
        if (damage <= 0f || !_effectStateA.IsCreated ||
            (uint)enemyIndex >= (uint)_effectStateA.Length)
            return;

        RougeEnemyEffectState effects = _effectStateA[enemyIndex];
        effects.EmbeddedMachineGunFragmentCount++;
        if (damage >= effects.EmbeddedMachineGunFragmentDamage)
        {
            effects.EmbeddedMachineGunFragmentDamage = damage;
            effects.EmbeddedMachineGunFragmentRange = Mathf.Max(0.1f, travelDistance);
            effects.EmbeddedMachineGunKillGoldBonus = killGoldBonus;
            effects.EmbeddedMachineGunWealthCellIndexPlusOne = wealthCellIndexPlusOne;
            effects.EmbeddedMachineGunTileEffect = tileEffect;
        }
        _effectStateA[enemyIndex] = effects;
    }

    private bool TryFindEnemyAtMachineGunImpact(float2 impact, float radius, out int enemyIndex)
    {
        enemyIndex = -1;
        if (!_enemyTargetCellHeads.IsCreated || !_enemyTargetCellNext.IsCreated ||
            !_positionsA.IsCreated || !_stateA.IsCreated)
            return false;

        float safeRadius = Mathf.Max(0f, radius);
        float searchRadius = safeRadius + MachineGunCollisionSearchEnemyRadius;
        float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
        int2 minCell = RougeMortonGridUtility.WorldToGrid(
            impact - new float2(searchRadius), _flowGridOrigin, invCellSize, _flowGridDim);
        int2 maxCell = RougeMortonGridUtility.WorldToGrid(
            impact + new float2(searchRadius), _flowGridOrigin, invCellSize, _flowGridDim);
        float nearestDistanceSq = float.MaxValue;

        for (int y = minCell.y; y <= maxCell.y; y++)
        {
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                int cell = RougeMortonGridUtility.EncodeMorton(x, y);
                for (int candidate = _enemyTargetCellHeads[cell];
                     candidate >= 0;
                     candidate = _enemyTargetCellNext[candidate])
                {
                    if ((uint)candidate >= (uint)_currentMaxEnemies || _stateA[candidate].x <= 0f)
                        continue;
                    float distanceSq = math.lengthsq(_positionsA[candidate].xz - impact);
                    float combinedRadius = safeRadius + math.max(0f,
                        _stateA[candidate].y);
                    if (distanceSq > combinedRadius * combinedRadius ||
                        distanceSq >= nearestDistanceSq)
                        continue;
                    nearestDistanceSq = distanceSq;
                    enemyIndex = candidate;
                }
            }
        }

        return enemyIndex >= 0;
    }

    private bool TryFindEnemyAlongMachineGunPath(float2 start, float2 end, float radius,
        out int enemyIndex)
    {
        enemyIndex = -1;
        if (!_enemyTargetCellHeads.IsCreated || !_enemyTargetCellNext.IsCreated ||
            !_positionsA.IsCreated || !_stateA.IsCreated)
            return false;

        float safeRadius = Mathf.Max(0f, radius);
        float2 padding = new float2(safeRadius +
                                    MachineGunCollisionSearchEnemyRadius);
        float2 boundsMin = math.min(start, end) - padding;
        float2 boundsMax = math.max(start, end) + padding;
        float invCellSize = 1f / math.max(_flowFieldRuntimeCellSize, 0.001f);
        int2 minCell = RougeMortonGridUtility.WorldToGrid(
            boundsMin, _flowGridOrigin, invCellSize, _flowGridDim);
        int2 maxCell = RougeMortonGridUtility.WorldToGrid(
            boundsMax, _flowGridOrigin, invCellSize, _flowGridDim);
        float2 segment = end - start;
        float segmentLengthSq = math.lengthsq(segment);
        float firstHitT = float.MaxValue;
        float nearestDistanceSq = float.MaxValue;

        for (int y = minCell.y; y <= maxCell.y; y++)
        {
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                int cell = RougeMortonGridUtility.EncodeMorton(x, y);
                for (int candidate = _enemyTargetCellHeads[cell];
                     candidate >= 0;
                     candidate = _enemyTargetCellNext[candidate])
                {
                    if ((uint)candidate >= (uint)_currentMaxEnemies ||
                        _stateA[candidate].x <= 0f)
                        continue;
                    float2 candidatePosition = _positionsA[candidate].xz;
                    float hitT = segmentLengthSq > 0.0001f
                        ? math.saturate(math.dot(candidatePosition - start, segment) /
                                        segmentLengthSq)
                        : 0f;
                    float distanceSq = math.lengthsq(
                        candidatePosition - (start + segment * hitT));
                    float combinedRadius = safeRadius + math.max(0f,
                        _stateA[candidate].y);
                    if (distanceSq > combinedRadius * combinedRadius ||
                        hitT > firstHitT ||
                        (Mathf.Approximately(hitT, firstHitT) &&
                         distanceSq >= nearestDistanceSq))
                        continue;
                    firstHitT = hitT;
                    nearestDistanceSq = distanceSq;
                    enemyIndex = candidate;
                }
            }
        }

        return enemyIndex >= 0;
    }

    private void RecycleTowerProjectileVisual(GameObject visual)
    {
        if (visual == null) return;
        visual.SetActive(false);
        _towerProjectileVisualPool.Push(visual);
    }

    private void AddTowerFireZone(Vector3 position, float radius, float duration, float damagePerTick,
        float tickInterval, int killGoldBonus, int wealthCellIndexPlusOne, int tileEffect,
        float burnDamage = 0f, float burnDuration = 0f, float burnTickInterval = 0f,
        int burnMaximumStacks = 1, float burnDamageBonusPerStack = 0f,
        float conflagrationDamage = 0f)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Tower Fire Zone";
        float visualPhase = Mathf.Repeat(position.x * 0.173f + position.z * 0.319f, 1f);
        // The route tile top is normally renderHeight + 0.08. Keep the thin
        // cylinder's top at roughly +0.12 (the old visual's visible height)
        // instead of burying it inside the map mesh.
        visual.transform.position = new Vector3(position.x,
            renderHeight + 0.095f + visualPhase * 0.004f, position.z);
        visual.transform.rotation = Quaternion.Euler(0f, visualPhase * 360f, 0f);
        visual.transform.localScale = new Vector3(radius * 2f, 0.025f, radius * 2f);
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = visual.GetComponent<Renderer>();
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetTowerFireZoneMaterial();
            ConfigureGroundAoEVisual(renderer, _towerFireZoneMaterial);
            // Draw after the opaque floor but before the transparent build grid.
            // The cyan/yellow placement rails remain readable across the hazard.
            _towerFireZoneMaterial.renderQueue = 2990;
            properties.SetFloat("_TimeOffset", visualPhase * 8f);
            // The zone is created after UpdateTowerFireZones for this frame, so
            // starting at zero made the freshly spawned AOE entirely invisible.
            properties.SetFloat("_LifeAlpha", 1f);
            renderer.SetPropertyBlock(properties);
        }
        float safeDuration = Mathf.Max(0.01f, duration);
        _towerFireZones.Add(new TowerFireZone
        {
            Position = position,
            Radius = radius,
            Remaining = safeDuration,
            Duration = safeDuration,
            DamagePerTick = damagePerTick,
            TickInterval = Mathf.Max(0.01f, tickInterval),
            TickTimer = 0f,
            BurnDamage = Mathf.Max(0f, burnDamage),
            BurnDuration = Mathf.Max(0f, burnDuration),
            BurnTickInterval = Mathf.Max(0f, burnTickInterval),
            BurnMaximumStacks = Mathf.Max(1, burnMaximumStacks),
            BurnDamageBonusPerStack = Mathf.Max(0f, burnDamageBonusPerStack),
            ConflagrationDamage = Mathf.Max(0f, conflagrationDamage),
            VisualPhase = visualPhase,
            KillGoldBonus = killGoldBonus,
            WealthCellIndexPlusOne = Mathf.Max(0, wealthCellIndexPlusOne),
            TileEffect = tileEffect,
            Visual = visual,
            Renderer = renderer,
            Properties = properties
        });
    }

    private Material GetTowerFireZoneMaterial()
    {
        if (_towerFireZoneMaterial != null) return _towerFireZoneMaterial;
        _towerFireZoneMaterial = CreateRuntimeMaterial(
            "Rouge/GroundZone", "Tower Fire Zone", false);
        ConfigureGroundZoneMaterial(
            _towerFireZoneMaterial,
            new Color(1f, 0.26f, 0.025f, 0.46f),
            new Color(0.18f, 0.01f, 0.002f, 0.12f),
            3f,
            2.2f,
            0.045f,
            2.4f,
            0.85f,
            0.85f,
            0.65f,
            1f);
        if (_towerFireZoneMaterial.HasProperty("_HotColor"))
            _towerFireZoneMaterial.SetColor("_HotColor", new Color(1.35f, 0.65f, 0.08f, 1f));
        _towerFireZoneMaterial.enableInstancing = true;
        return _towerFireZoneMaterial;
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
                    EffectFlags = zone.BurnDamage > 0f
                        ? (int)SkillHitEffectTag.Burn
                        : 0,
                    EffectBurnDamage = zone.BurnDamage,
                    EffectBurnDuration = zone.BurnDuration,
                    EffectBurnTickInterval = zone.BurnTickInterval,
                    EffectBurnMaximumStacks = zone.BurnMaximumStacks,
                    EffectBurnDamageBonusPerStack = zone.BurnDamageBonusPerStack,
                    EffectConflagrationDamage = zone.ConflagrationDamage,
                    SourceTowerTypePlusOne = (int)RougeTowerType.Flame + 1,
                    SourceTowerTileEffect = zone.TileEffect,
                    SourceTowerKillGoldBonus = zone.KillGoldBonus,
                    SourceTowerWealthCellIndexPlusOne = zone.WealthCellIndexPlusOne
                });
                zone.TickTimer += zone.TickInterval;
                ticksThisFrame++;
            }
            if (zone.Visual != null && zone.Renderer != null && zone.Properties != null)
            {
                float fadeOut = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(zone.Remaining / Mathf.Max(0.01f, zone.Duration * 0.18f)));
                zone.Properties.SetFloat("_TimeOffset", zone.VisualPhase * 8f);
                zone.Properties.SetFloat("_LifeAlpha", fadeOut);
                zone.Renderer.SetPropertyBlock(zone.Properties);
            }
            _towerFireZones[i] = zone;
        }
    }

    private void StartPiercingLaserAttack(RougeDefenseTower tower, Vector3 target)
    {
        if (tower == null || IsPiercingLaserAttackActive(tower)) return;

        Vector3 start = GetTowerMuzzlePosition(tower);
        Vector3 direction = target - start;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;
        direction.Normalize();
        Vector3 currentAimDirection = tower.GetCurrentAimDirection();

        TowerBeamVisual beam = new TowerBeamVisual
        {
            SourceTower = tower,
            Start = start,
            Direction = currentAimDirection,
            TurnStartDirection = currentAimDirection,
            Length = tower.AttackRange * 2f,
            // The hit radius stays unchanged; this multiplier affects only the peak visual.
            MaxWidth = PiercingLaserBeamRadius * PiercingLaserMaxVisualWidthMultiplier,
            Damage = tower.Damage,
            KillGoldBonus = tower.KillGoldPercentBonus,
            WealthCellIndexPlusOne = GetTowerWealthCellIndexPlusOne(tower),
            TileEffect = (int)tower.TowerPlaceEffect,
            TargetIndex = tower.targetIndex,
            Properties = new MaterialPropertyBlock(),
            GlowProperties = new MaterialPropertyBlock(),
            ChargeProperties = new MaterialPropertyBlock()
        };

        beam.Visual = CreatePiercingLaserPrimitive(PrimitiveType.Cylinder,
            "Piercing Laser Core", out beam.Renderer);
        beam.GlowVisual = CreatePiercingLaserPrimitive(PrimitiveType.Cylinder,
            "Piercing Laser Outer Glow", out beam.GlowRenderer);
        beam.RootCapVisual = CreatePiercingLaserPrimitive(PrimitiveType.Sphere,
            "Piercing Laser Hemispherical Root", out beam.RootCapRenderer);
        beam.RootGlowCapVisual = CreatePiercingLaserPrimitive(PrimitiveType.Sphere,
            "Piercing Laser Hemispherical Root Glow", out beam.RootGlowCapRenderer);
        beam.ChargeVisual = CreatePiercingLaserPrimitive(PrimitiveType.Sphere,
            "Piercing Laser Charge", out beam.ChargeRenderer);

        ConfigurePiercingLaserStyle(beam.Properties,
            new Color(1.65f, 1.58f, 2.05f, 1f),
            new Color(0.95f, 0.035f, 2.2f, 1f),
            new Color(0.07f, 0.34f, 1.8f, 1f), 0.045f, 0.44f, 0.26f, 1.75f);
        ConfigurePiercingLaserStyle(beam.GlowProperties,
            new Color(0.32f, 0.035f, 0.82f, 1f),
            new Color(0.17f, 0.018f, 1.3f, 1f),
            new Color(0.025f, 0.24f, 1.85f, 1f), 0.02f, 0.22f, 0.72f, 0.65f);
        ConfigurePiercingLaserStyle(beam.ChargeProperties,
            new Color(2.2f, 2.0f, 2.55f, 1f),
            new Color(1.25f, 0.045f, 2.45f, 1f),
            new Color(0.055f, 0.42f, 2.1f, 1f), 0.10f, 0.52f, 0.40f, 2.0f);

        // The cylinder begins almost fully opaque so it joins cleanly to the separate
        // hemispherical root cap. Other users keep the material's original soft fade.
        beam.Properties.SetFloat(LaserStartFadeId, 0.006f);
        beam.GlowProperties.SetFloat(LaserStartFadeId, 0.008f);

        if (beam.GlowVisual != null) beam.GlowVisual.SetActive(false);
        if (beam.RootCapVisual != null) beam.RootCapVisual.SetActive(false);
        if (beam.RootGlowCapVisual != null) beam.RootGlowCapVisual.SetActive(false);
        UpdatePiercingLaserChargeVisual(ref beam, 0f);
        _towerBeamVisuals.Add(beam);
        tower.PlayPiercingChargeSound();
    }

    private GameObject CreatePiercingLaserPrimitive(PrimitiveType primitiveType, string objectName,
        out MeshRenderer renderer)
    {
        GameObject visual = GameObject.CreatePrimitive(primitiveType);
        visual.name = objectName;
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        renderer = visual.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            if (_laserMat != null) renderer.sharedMaterial = _laserMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        return visual;
    }

    private static void ConfigurePiercingLaserStyle(MaterialPropertyBlock properties,
        Color coreColor, Color beamColor, Color glowColor, float coreRadius,
        float beamRadius, float glowSoftness, float ribbonIntensity)
    {
        properties.SetColor(LaserCoreColorId, coreColor);
        properties.SetColor(LaserBeamColorId, beamColor);
        properties.SetColor(LaserGlowColorId, glowColor);
        properties.SetColor(LaserBaseColorId, beamColor);
        properties.SetFloat(LaserCoreRadiusId, coreRadius);
        properties.SetFloat(LaserBeamRadiusId, beamRadius);
        properties.SetFloat(LaserGlowSoftnessId, glowSoftness);
        properties.SetFloat(LaserRibbonIntensityId, ribbonIntensity);
        properties.SetFloat(LaserNoiseStrengthId, 0.36f);
        properties.SetFloat(LaserFlowScaleId, 20f);
        properties.SetFloat(LaserFlowSpeedId, -24f);
    }

    private void UpdateTowerBeamVisuals(float dt)
    {
        for (int i = _towerBeamVisuals.Count - 1; i >= 0; i--)
        {
            TowerBeamVisual beam = _towerBeamVisuals[i];
            if (beam.SourceTower == null || beam.Visual == null)
            {
                DestroyTowerBeamVisual(beam);
                _towerBeamVisuals.RemoveAt(i);
                continue;
            }

            beam.Start = GetTowerMuzzlePosition(beam.SourceTower);
            float safeDt = Mathf.Max(0f, dt);
            bool wasChargeComplete = beam.ChargeComplete;
            if (!beam.ChargeComplete)
            {
                // Charge time is expressed in base seconds. Attack speed advances only
                // this clock, so all three 0.5 second charge pulses react to buffs/debuffs.
                beam.ChargeElapsed += safeDt *
                    Mathf.Max(0.01f, beam.SourceTower.AttackSpeedMultiplier);
                beam.ChargeComplete = beam.ChargeElapsed >= PiercingLaserChargeDuration;
                beam.ChargeElapsed = Mathf.Min(beam.ChargeElapsed, PiercingLaserChargeDuration);
                UpdatePiercingLaserTracking(ref beam, safeDt);
                UpdatePiercingLaserChargeVisual(ref beam,
                    Mathf.Clamp01(beam.ChargeElapsed / PiercingLaserChargeDuration));
            }

            if (beam.ChargeComplete)
            {
                // Firing deliberately uses unscaled combat time. Attack speed must not
                // change the 0.25 second impact peak or the 0.75 second beam lifetime.
                if (wasChargeComplete) beam.FireElapsed += safeDt;
                UpdatePiercingLaserFireVisual(ref beam, beam.FireElapsed);
            }

            if (beam.ChargeComplete && beam.FireElapsed >= PiercingLaserFireDuration)
            {
                RougeDefenseTower sourceTower = beam.SourceTower;
                DestroyTowerBeamVisual(beam);
                _towerBeamVisuals.RemoveAt(i);
                if (sourceTower != null && !CompleteEchoAttackStep(sourceTower))
                {
                    // attackTimer is expressed in unscaled attack-interval units and is reduced
                    // by AttackSpeedMultiplier in UpdateDefenseTowers.
                    sourceTower.attackTimer = sourceTower.AttackInterval;
                    sourceTower.targetIndex = -1;
                }
                continue;
            }

            _towerBeamVisuals[i] = beam;
        }
    }

    private void UpdatePiercingLaserTracking(ref TowerBeamVisual beam, float dt)
    {
        if (beam.SourceTower == null) return;

        if (!beam.TargetLost)
        {
            if (!IsEnemyTargetValid(beam.TargetIndex, beam.Start, float.MaxValue,
                    out Vector3 targetPosition))
            {
                // Once the target dies or becomes invalid, this attack permanently keeps
                // its last aim. Do not follow a new enemy that later reuses the same slot.
                beam.TargetLost = true;
            }
            else
            {
                Vector3 desiredDirection = targetPosition - beam.Start;
                desiredDirection.y = 0f;
                if (desiredDirection.sqrMagnitude > 0.0001f)
                {
                    desiredDirection.Normalize();
                    if (beam.TurnElapsed < PiercingLaserTurnDuration)
                    {
                        beam.TurnElapsed = Mathf.Min(PiercingLaserTurnDuration,
                            beam.TurnElapsed + Mathf.Max(0f, dt));
                        float turnProgress = Mathf.Clamp01(beam.TurnElapsed /
                            PiercingLaserTurnDuration);
                        turnProgress = Mathf.SmoothStep(0f, 1f, turnProgress);
                        beam.Direction = Vector3.Slerp(beam.TurnStartDirection,
                            desiredDirection, turnProgress).normalized;
                    }
                    else
                    {
                        // Moving targets remain smoothly tracked with a 0.25 second settle
                        // time instead of making the charging guide beam jitter or snap.
                        float followT = 1f - Mathf.Exp(-4.6f * Mathf.Max(0f, dt) /
                            PiercingLaserTurnDuration);
                        beam.Direction = Vector3.Slerp(beam.Direction,
                            desiredDirection, followT).normalized;
                    }
                }
            }
        }

        AimTowerAt(beam.SourceTower, beam.Start + beam.Direction * 10f);
    }

    private void UpdatePiercingLaserChargeVisual(ref TowerBeamVisual beam, float progress)
    {
        // Three distinct scale pulses: each occupies 0.5 base seconds and grows then
        // contracts once. Later stages retain more energy so the buildup still escalates.
        float stagePosition = Mathf.Min(progress * PiercingLaserChargeStageCount,
            PiercingLaserChargeStageCount - 0.0001f);
        int stageIndex = Mathf.Clamp(Mathf.FloorToInt(stagePosition), 0,
            PiercingLaserChargeStageCount - 1);
        float stageProgress = stagePosition - stageIndex;
        float rawPulse = Mathf.Sin(stageProgress * Mathf.PI);
        float stagePulse = rawPulse * rawPulse * (3f - 2f * rawPulse);
        float buildup = (stageIndex + stageProgress) / PiercingLaserChargeStageCount;
        float stageStrength = (stageIndex + 1f) / PiercingLaserChargeStageCount;

        float guideWidth = beam.MaxWidth * Mathf.Lerp(0.016f, 0.045f, buildup) *
            Mathf.Lerp(0.78f, 1.28f, stagePulse);
        SetPiercingLaserTransform(beam.Visual, beam.Start, beam.Direction,
            beam.Length, guideWidth, 0.62f);
        if (beam.Visual != null) beam.Visual.SetActive(true);
        if (beam.GlowVisual != null) beam.GlowVisual.SetActive(false);
        if (beam.RootCapVisual != null) beam.RootCapVisual.SetActive(false);
        if (beam.RootGlowCapVisual != null) beam.RootGlowCapVisual.SetActive(false);

        float chargeDiameter = beam.MaxWidth * Mathf.Lerp(0.055f, 0.19f, stageStrength) *
            Mathf.Lerp(0.72f, 1.65f, stagePulse);
        if (beam.ChargeVisual != null)
        {
            beam.ChargeVisual.SetActive(true);
            beam.ChargeVisual.transform.position = beam.Start + beam.Direction * 0.24f;
            beam.ChargeVisual.transform.localScale = Vector3.one * chargeDiameter;
        }

        SetPiercingLaserRuntimeProperties(beam.Renderer, beam.Properties,
            Mathf.Lerp(0.14f, 0.46f, buildup) * Mathf.Lerp(0.82f, 1.2f, stagePulse),
            progress, stagePulse * 0.12f);
        SetPiercingLaserRuntimeProperties(beam.ChargeRenderer, beam.ChargeProperties,
            Mathf.Lerp(0.34f, 1.3f, buildup) * Mathf.Lerp(0.76f, 1.28f, stagePulse),
            progress, stagePulse * Mathf.Lerp(0.22f, 0.58f, stageStrength));
    }

    private void UpdatePiercingLaserFireVisual(ref TowerBeamVisual beam, float fireTime)
    {
        if (!beam.FiringAnimationPlayed)
        {
            beam.FiringAnimationPlayed = true;
            if (beam.SourceTower != null) beam.SourceTower.PlayAttackAnimation(null);
        }

        if (beam.ChargeVisual != null) beam.ChargeVisual.SetActive(false);
        if (beam.Visual != null) beam.Visual.SetActive(true);
        if (beam.GlowVisual != null) beam.GlowVisual.SetActive(true);
        if (beam.RootCapVisual != null) beam.RootCapVisual.SetActive(true);
        if (beam.RootGlowCapVisual != null) beam.RootGlowCapVisual.SetActive(true);

        float normalized = Mathf.Clamp01(fireTime / PiercingLaserFireDuration);
        float envelope;
        if (fireTime <= PiercingLaserDamageTime)
        {
            float grow = Mathf.Clamp01(fireTime / PiercingLaserDamageTime);
            envelope = Mathf.SmoothStep(0f, 1f, grow);
        }
        else
        {
            float fade = Mathf.Clamp01((fireTime - PiercingLaserDamageTime) /
                Mathf.Max(0.01f, PiercingLaserFireDuration - PiercingLaserDamageTime));
            envelope = 1f - Mathf.SmoothStep(0f, 1f, fade);
        }
        float visibleLength = beam.Length * Mathf.SmoothStep(0.03f, 1f,
            Mathf.Clamp01(fireTime / 0.08f));
        float width = beam.MaxWidth * Mathf.Lerp(0.065f, 1f, envelope);
        SetPiercingLaserTransform(beam.Visual, beam.Start, beam.Direction,
            visibleLength, width, 0.58f);
        SetPiercingLaserTransform(beam.GlowVisual, beam.Start, beam.Direction,
            visibleLength, width * 1.58f, 0.84f);
        SetPiercingLaserRootCapTransform(beam.RootCapVisual, beam.Start,
            beam.Direction, width * 0.92f, 0.58f);
        SetPiercingLaserRootCapTransform(beam.RootGlowCapVisual, beam.Start,
            beam.Direction, width * 1.58f * 0.92f, 0.84f);

        float impactFlash = Mathf.Pow(1f - Mathf.Clamp01(
            Mathf.Abs(fireTime - PiercingLaserDamageTime) / 0.1f), 2f);
        SetPiercingLaserRuntimeProperties(beam.Renderer, beam.Properties,
            Mathf.Lerp(0.42f, 1.35f, envelope), normalized, impactFlash);
        SetPiercingLaserRuntimeProperties(beam.GlowRenderer, beam.GlowProperties,
            Mathf.Lerp(0.18f, 0.66f, envelope), normalized, impactFlash * 0.62f);
        SetPiercingLaserRuntimeProperties(beam.RootCapRenderer, beam.Properties,
            Mathf.Lerp(0.42f, 1.35f, envelope), normalized, impactFlash, true);
        SetPiercingLaserRuntimeProperties(beam.RootGlowCapRenderer, beam.GlowProperties,
            Mathf.Lerp(0.18f, 0.66f, envelope), normalized, impactFlash * 0.62f, true);

        if (!beam.DamageApplied && fireTime >= PiercingLaserDamageTime)
        {
            beam.DamageApplied = TryAddTowerDirectDamageArea(new RougeSkillArea
            {
                Type = 15,
                Position = new float2(beam.Start.x, beam.Start.z),
                Direction = new float2(beam.Direction.x, beam.Direction.z),
                Length = beam.Length,
                Radius = PiercingLaserBeamRadius,
                Damage = beam.Damage,
                SourceTowerTypePlusOne = (int)RougeTowerType.PiercingLaser + 1,
                SourceTowerTileEffect = beam.TileEffect,
                SourceTowerKillGoldBonus = beam.KillGoldBonus,
                SourceTowerWealthCellIndexPlusOne = beam.WealthCellIndexPlusOne
            }, TowerFrostAreaSlowMultiplier);
        }
    }

    private static void SetPiercingLaserRuntimeProperties(MeshRenderer renderer,
        MaterialPropertyBlock properties, float alpha, float phase, float impactFlash,
        bool rootHemisphere = false)
    {
        if (renderer == null || properties == null) return;
        properties.SetFloat(LaserAlphaId, alpha);
        properties.SetFloat(LaserVisualPhaseId, phase);
        properties.SetFloat(LaserImpactFlashId, impactFlash);
        properties.SetFloat(LaserRootHemisphereId, rootHemisphere ? 1f : 0f);
        renderer.SetPropertyBlock(properties);
    }

    private static void SetPiercingLaserTransform(GameObject visual, Vector3 start,
        Vector3 direction, float length, float width, float depthScale)
    {
        if (visual == null) return;
        float safeLength = Mathf.Max(0.01f, length);
        float safeWidth = Mathf.Max(0.01f, width);
        visual.transform.position = start + direction * (safeLength * 0.5f);
        visual.transform.rotation = Quaternion.LookRotation(direction, Vector3.up) *
            Quaternion.Euler(90f, 0f, 0f);
        visual.transform.localScale = new Vector3(safeWidth, safeLength * 0.5f,
            safeWidth * depthScale);
    }

    private static void SetPiercingLaserRootCapTransform(GameObject visual, Vector3 start,
        Vector3 direction, float diameter, float depthScale)
    {
        if (visual == null) return;
        float safeDiameter = Mathf.Max(0.01f, diameter);
        visual.transform.position = start;
        visual.transform.rotation = Quaternion.LookRotation(direction, Vector3.up) *
            Quaternion.Euler(90f, 0f, 0f);
        // Local Y follows the beam axis. Clipping Y > 0 leaves the rear half of this
        // ellipsoid visible as the rounded root while the cylinder begins at the seam.
        visual.transform.localScale = new Vector3(safeDiameter, safeDiameter,
            safeDiameter * depthScale);
    }

    private bool IsPiercingLaserAttackActive(RougeDefenseTower tower)
    {
        for (int i = 0; i < _towerBeamVisuals.Count; i++)
        {
            if (_towerBeamVisuals[i].SourceTower == tower) return true;
        }
        return false;
    }

    private void StopPiercingLaserAttacksForTower(RougeDefenseTower tower)
    {
        for (int i = _towerBeamVisuals.Count - 1; i >= 0; i--)
        {
            if (_towerBeamVisuals[i].SourceTower != tower) continue;
            DestroyTowerBeamVisual(_towerBeamVisuals[i]);
            _towerBeamVisuals.RemoveAt(i);
        }
    }

    private void StopOrbitSphereAttacksForTower(RougeDefenseTower tower)
    {
        for (int i = _activeOrbitSphereAttacks.Count - 1; i >= 0; i--)
        {
            if (_activeOrbitSphereAttacks[i].Tower != tower) continue;
            _activeOrbitSphereAttacks[i].Positions = null;
            _activeOrbitSphereAttacks.RemoveAt(i);
        }
        if (tower != null) tower.HideLaserBeams();
    }

    private static void DestroyTowerBeamVisual(TowerBeamVisual beam)
    {
        if (beam.Visual != null) Destroy(beam.Visual);
        if (beam.GlowVisual != null) Destroy(beam.GlowVisual);
        if (beam.RootCapVisual != null) Destroy(beam.RootCapVisual);
        if (beam.RootGlowCapVisual != null) Destroy(beam.RootGlowCapVisual);
        if (beam.ChargeVisual != null) Destroy(beam.ChargeVisual);
    }

    private void RenderTowerDefensePausedFrame(bool refreshUi = true)
    {
        RenderBullets();
        RenderAOERings();
        RenderExplosions();
        RenderDeathBursts();
        RenderTornados();
        if (refreshUi) RefreshTowerDefenseUi();
    }

    private void TriggerTowerDefenseGameOver(string reason)
    {
        if (!_towerDefenseInitialized)
        {
            ReloadTowerDefenseScene();
            return;
        }
        if (_towerDefenseGameOver) return;
        StopTowerDefenseAutoplayForConclusion();
        if (_cameraViewMode != CameraViewMode.Default) ExitDebugUnitView();
        _towerDefenseGameOver = true;
        _towerDefenseGameOverReason = reason;
        HideTowerDefenseSpawnWarnings();
        StopAllTowerAttackSounds();
        _towerPlacementMode = false;
        TowerDefenseBuildModeActive = false;
        ClearTowerRelocationState();
        SetTowerPlaceVisualsVisible(false);
        RefreshTowerEditHints();
        Time.timeScale = 0f;
        if (player != null) player.SuppressMovement = true;
        if (_towerPreview != null) _towerPreview.gameObject.SetActive(false);
        BeginTowerDefenseFailureSequence();
    }

    private void ReloadTowerDefenseScene()
    {
        if (_towerDefenseSceneReloadRequested) return;
        _towerDefenseSceneReloadRequested = true;
        Time.timeScale = 1f;

        Scene currentScene = gameObject.scene.IsValid()
            ? gameObject.scene
            : SceneManager.GetActiveScene();
        int buildIndex = currentScene.buildIndex;
        string sceneName = currentScene.name;

        // Finish scheduled jobs and release this run before loading the next copy.
        // OnDisable sees _initialized == false afterwards, so cleanup cannot run twice.
        Dispose();
        if (buildIndex >= 0)
            SceneManager.LoadScene(buildIndex, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private void BuildTowerDefenseUi()
    {
        GameObject canvasObject = new GameObject("Tower Defense Canvas");
        _towerDefenseCanvas = canvasObject.AddComponent<Canvas>();
        _towerDefenseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _towerDefenseCanvas.sortingOrder = 50;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        RougeTowerDefenseUiLayout.ConfigureCanvasScaler(scaler);
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject statusPanel = CreateUiPanel("Status Panel", canvasObject.transform, new Color(0.025f, 0.04f, 0.07f, 0.88f));
        RectTransform statusRect = statusPanel.GetComponent<RectTransform>();
        RougeTowerDefenseUiLayout.ConfigureStatusPanel(statusRect);
        AddHudPanelChrome(statusPanel, new Color(0.08f, 0.72f, 0.94f, 1f));

        Text statusTitle = CreateUiText("Status Title", statusPanel.transform, 21, TextAnchor.MiddleLeft);
        RectTransform statusTitleRect = statusTitle.rectTransform;
        statusTitleRect.anchorMin = new Vector2(0f, 1f);
        statusTitleRect.anchorMax = new Vector2(1f, 1f);
        statusTitleRect.pivot = new Vector2(0.5f, 1f);
        statusTitleRect.anchoredPosition = new Vector2(-70f, -7f);
        statusTitleRect.sizeDelta = new Vector2(-180f, 30f);
        statusTitle.text = "基地状态";
        statusTitle.fontStyle = FontStyle.Bold;
        statusTitle.color = new Color(0.72f, 0.92f, 1f, 1f);

        _visualQualityButton = CreateUiButton("Visual Quality", statusPanel.transform,
            string.Empty, new Color(0.055f, 0.24f, 0.34f, 0.96f));
        RectTransform qualityRect = _visualQualityButton.GetComponent<RectTransform>();
        qualityRect.anchorMin = new Vector2(1f, 1f);
        qualityRect.anchorMax = new Vector2(1f, 1f);
        qualityRect.pivot = new Vector2(1f, 1f);
        qualityRect.anchoredPosition = new Vector2(-14f, -8f);
        qualityRect.sizeDelta = new Vector2(142f, 28f);
        _visualQualityButtonText = _visualQualityButton.GetComponentInChildren<Text>();
        if (_visualQualityButtonText != null) _visualQualityButtonText.fontSize = 14;
        _visualQualityButton.onClick.AddListener(RougeVisualQualityManager.CycleActiveTier);

        _towerDefenseStatusText = CreateUiText("Status", statusPanel.transform, 17, TextAnchor.UpperLeft);
        _towerDefenseStatusText.lineSpacing = 1.05f;
        StretchRect(_towerDefenseStatusText.rectTransform, 20f, 42f, 20f, 44f);
        Image hpBackground = CreateUiImage("HP Background", statusPanel.transform, new Color(0.08f, 0.1f, 0.14f, 1f));
        RectTransform hpRect = hpBackground.rectTransform;
        hpRect.anchorMin = new Vector2(0f, 0f);
        hpRect.anchorMax = new Vector2(1f, 0f);
        hpRect.pivot = new Vector2(0.5f, 0f);
        hpRect.anchoredPosition = new Vector2(0f, 14f);
        hpRect.sizeDelta = new Vector2(-40f, 24f);
        _mainTowerHealthFill = CreateUiImage("HP Fill", hpBackground.transform, new Color(0.12f, 0.78f, 1f, 1f));
        StretchRect(_mainTowerHealthFill.rectTransform, 3f, 3f, 3f, 3f);
        _mainTowerHealthFill.type = Image.Type.Simple;
        _mainTowerHealthFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        _mainTowerHealthText = CreateUiText("HP Text", hpBackground.transform, 15, TextAnchor.MiddleCenter);
        _mainTowerHealthText.fontStyle = FontStyle.Bold;
        StretchRect(_mainTowerHealthText.rectTransform, 3f, 1f, 3f, 1f);

        _towerDamagePanel = CreateUiPanel("Tower Damage Ranking", canvasObject.transform,
            new Color(0.025f, 0.04f, 0.07f, 0.88f));
        RectTransform damageRect = _towerDamagePanel.GetComponent<RectTransform>();
        RougeTowerDefenseUiLayout.ConfigureDamagePanel(damageRect);
        AddHudPanelChrome(_towerDamagePanel, new Color(0.08f, 0.72f, 0.94f, 1f));
        Text damageTitle = CreateUiText("Damage Ranking Title", _towerDamagePanel.transform, 21, TextAnchor.MiddleLeft);
        RectTransform damageTitleRect = damageTitle.rectTransform;
        damageTitleRect.anchorMin = new Vector2(0f, 1f);
        damageTitleRect.anchorMax = new Vector2(1f, 1f);
        damageTitleRect.pivot = new Vector2(0.5f, 1f);
        damageTitleRect.anchoredPosition = new Vector2(0f, -7f);
        damageTitleRect.sizeDelta = new Vector2(-36f, 30f);
        damageTitle.text = "塔楼输出";
        damageTitle.fontStyle = FontStyle.Bold;
        damageTitle.color = new Color(0.44f, 0.90f, 1f, 1f);
        _towerDamageRankingText = CreateUiText("Damage Ranking", _towerDamagePanel.transform, 18, TextAnchor.UpperLeft);
        _towerDamageRankingText.lineSpacing = 1.32f;
        StretchRect(_towerDamageRankingText.rectTransform, 18f, 43f, 18f, 12f);

        _towerPlaceEffectPanel = CreateUiPanel("Tower Intelligence", canvasObject.transform,
            new Color(0.004f, 0.018f, 0.029f, 0.76f));
        RectTransform towerInfoRect = _towerPlaceEffectPanel.GetComponent<RectTransform>();
        towerInfoRect.anchorMin = new Vector2(0f, 1f);
        towerInfoRect.anchorMax = new Vector2(0f, 1f);
        towerInfoRect.pivot = new Vector2(0f, 1f);
        towerInfoRect.anchoredPosition = new Vector2(20f, -20f);
        towerInfoRect.sizeDelta = new Vector2(440f, 700f);
        AddHudPanelChrome(_towerPlaceEffectPanel, new Color(0.10f, 0.78f, 0.82f, 1f));
        CanvasGroup towerInfoCanvasGroup = _towerPlaceEffectPanel.AddComponent<CanvasGroup>();
        towerInfoCanvasGroup.interactable = false;
        towerInfoCanvasGroup.blocksRaycasts = false;

        Text towerInfoTitle = CreateUiText("Tower Intelligence Title",
            _towerPlaceEffectPanel.transform, 21, TextAnchor.MiddleLeft);
        SetTopStretchRect(towerInfoTitle.rectTransform, 18f, 8f, 18f, 32f);
        towerInfoTitle.text = "塔楼情报";
        towerInfoTitle.fontStyle = FontStyle.Bold;
        towerInfoTitle.color = new Color(0.44f, 0.94f, 0.94f, 1f);
        _towerPlaceEffectText = CreateUiText("Tower Intelligence Detail",
            _towerPlaceEffectPanel.transform, 16, TextAnchor.UpperLeft);
        _towerPlaceEffectText.lineSpacing = 1.12f;
        _towerPlaceEffectText.supportRichText = true;
        _towerPlaceEffectText.resizeTextForBestFit = false;
        _towerPlaceEffectText.color = new Color(0.88f, 0.94f, 0.97f, 1f);
        StretchRect(_towerPlaceEffectText.rectTransform, 18f, 48f, 18f, 16f);
        _towerPlaceEffectPanel.SetActive(false);

        GameObject buildPanel = CreateUiPanel("Command Dock", canvasObject.transform,
            new Color(0.004f, 0.014f, 0.024f, 0.995f));
        RectTransform buildRect = buildPanel.GetComponent<RectTransform>();
        RougeTowerDefenseUiLayout.ConfigureCommandDock(buildRect);
        AddHudPanelChrome(buildPanel, new Color(0.08f, 0.68f, 0.9f, 1f));
        buildPanel.AddComponent<RougeTiltShiftUiBoundary>();

        _towerDefenseControlsText = CreateUiText("Camera Controls", canvasObject.transform,
            14, TextAnchor.MiddleCenter);
        RectTransform cameraControlsRect = _towerDefenseControlsText.rectTransform;
        cameraControlsRect.anchorMin = new Vector2(0f, 0f);
        cameraControlsRect.anchorMax = new Vector2(1f, 0f);
        cameraControlsRect.pivot = new Vector2(0.5f, 0f);
        cameraControlsRect.anchoredPosition = new Vector2(0f, 238f);
        cameraControlsRect.sizeDelta = new Vector2(-72f, 42f);
        _towerDefenseControlsText.lineSpacing = 1f;
        _towerDefenseControlsText.color = new Color(0.64f, 0.82f, 0.90f, 0.88f);

        GameObject selectedSection = CreateCommandDockSection("Selected Tower Section",
            buildPanel.transform, 0f, 0.27f);
        GameObject buildSection = CreateCommandDockSection("Build Tower Section",
            buildPanel.transform, 0.27f, 0.72f);
        GameObject actionSection = CreateCommandDockSection("Tower Action Section",
            buildPanel.transform, 0.72f, 1f);
        _towerActionContainer = actionSection.GetComponent<RectTransform>();

        Text selectedGroupTitle = CreateUiText("Selected Group Title",
            selectedSection.transform, 19, TextAnchor.MiddleLeft);
        SetTopStretchRect(selectedGroupTitle.rectTransform, 14f, 8f, 14f, 28f);
        selectedGroupTitle.text = "选中塔楼";
        selectedGroupTitle.fontStyle = FontStyle.Bold;
        selectedGroupTitle.color = new Color(0.44f, 0.90f, 1f, 1f);

        _selectedTowerPortraitFrame = CreateUiImage("Tower Portrait Frame",
            selectedSection.transform, new Color(0.025f, 0.09f, 0.13f, 0.96f));
        RectTransform portraitFrameRect = _selectedTowerPortraitFrame.rectTransform;
        SetBottomLeftRect(portraitFrameRect, 16f, 18f, 112f, 142f);
        Outline portraitOutline = _selectedTowerPortraitFrame.gameObject.AddComponent<Outline>();
        portraitOutline.effectColor = new Color(0.10f, 0.70f, 0.94f, 0.72f);
        portraitOutline.effectDistance = new Vector2(2f, -2f);
        Text portraitPlaceholder = CreateUiText("Portrait Placeholder",
            _selectedTowerPortraitFrame.transform, 42, TextAnchor.MiddleCenter);
        portraitPlaceholder.text = "◇";
        portraitPlaceholder.color = new Color(0.18f, 0.48f, 0.62f, 0.72f);
        StretchRect(portraitPlaceholder.rectTransform, 4f, 4f, 4f, 4f);
        _selectedTowerPortrait = CreateUiImage("Tower Portrait",
            _selectedTowerPortraitFrame.transform, Color.white);
        _selectedTowerPortrait.preserveAspect = true;
        StretchRect(_selectedTowerPortrait.rectTransform, 8f, 8f, 8f, 8f);

        _selectedTowerSummaryText = CreateUiText("Selected Tower Summary",
            selectedSection.transform, 18, TextAnchor.UpperLeft);
        RectTransform selectedSummaryRect = _selectedTowerSummaryText.rectTransform;
        selectedSummaryRect.anchorMin = new Vector2(0f, 0f);
        selectedSummaryRect.anchorMax = new Vector2(1f, 1f);
        selectedSummaryRect.offsetMin = new Vector2(146f, 96f);
        selectedSummaryRect.offsetMax = new Vector2(-14f, -48f);
        _selectedTowerSummaryText.lineSpacing = 1.12f;
        _selectedTowerSummaryText.supportRichText = true;

        _selectedTowerBuffText = CreateUiText("Selected Tower Buffs",
            selectedSection.transform, 15, TextAnchor.UpperLeft);
        RectTransform selectedBuffRect = _selectedTowerBuffText.rectTransform;
        selectedBuffRect.anchorMin = new Vector2(0f, 0f);
        selectedBuffRect.anchorMax = new Vector2(1f, 0f);
        selectedBuffRect.pivot = new Vector2(0.5f, 0f);
        selectedBuffRect.anchoredPosition = new Vector2(66f, 12f);
        selectedBuffRect.sizeDelta = new Vector2(-174f, 78f);
        _selectedTowerBuffText.supportRichText = true;
        _selectedTowerBuffText.resizeTextForBestFit = false;
        _selectedTowerBuffText.lineSpacing = 1.05f;
        _selectedTowerBuffText.color = new Color(0.82f, 0.91f, 0.96f, 1f);

        Text buildGroupTitle = CreateUiText("Build Group Title", buildSection.transform,
            19, TextAnchor.MiddleLeft);
        SetTopStretchRect(buildGroupTitle.rectTransform, 14f, 8f, 14f, 28f);
        buildGroupTitle.text = "建造塔楼";
        buildGroupTitle.fontStyle = FontStyle.Bold;
        buildGroupTitle.color = new Color(0.44f, 0.90f, 1f, 1f);
        Text actionGroupTitle = CreateUiText("Tower Action Group Title",
            actionSection.transform, 19, TextAnchor.MiddleLeft);
        SetTopStretchRect(actionGroupTitle.rectTransform, 14f, 8f, 14f, 28f);
        actionGroupTitle.text = "塔楼操作";
        actionGroupTitle.fontStyle = FontStyle.Bold;
        actionGroupTitle.color = new Color(0.44f, 0.90f, 1f, 1f);
        // Five columns still fit the 4:3 reference canvas after CanvasScaler has
        // applied its width/height blend.
        float[] buildColumns = { -280f, -140f, 0f, 140f, 280f };
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(1, RougeTowerType.Ice), buildColumns[0], 92f, RougeTowerType.Ice, new Color(0.08f, 0.55f, 0.82f, 1f));
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(2, RougeTowerType.MachineGun), buildColumns[1], 92f, RougeTowerType.MachineGun, new Color(0.92f, 0.73f, 0.06f, 1f));
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(3, RougeTowerType.Cannon), buildColumns[2], 92f, RougeTowerType.Cannon, new Color(0.95f, 0.22f, 0.08f, 1f));
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(7, RougeTowerType.OrbitSphere), buildColumns[3], 92f, RougeTowerType.OrbitSphere, new Color(0.18f, 0.66f, 0.96f, 1f));
        CreateChargeTowerBuildButton(buildSection.transform, buildColumns[4], 92f);
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(4, RougeTowerType.Flame), buildColumns[0], 28f, RougeTowerType.Flame, new Color(1f, 0.24f, 0.08f, 1f));
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(5, RougeTowerType.Laser), buildColumns[1], 28f, RougeTowerType.Laser, new Color(0.20f, 0.94f, 0.30f, 1f));
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(6, RougeTowerType.PiercingLaser), buildColumns[2], 28f, RougeTowerType.PiercingLaser, new Color(0.78f, 0.24f, 0.96f, 1f));
        CreateBuildButton(buildSection.transform, GetTowerBuildLabel(8, RougeTowerType.RocketBarrage), buildColumns[3], 28f, RougeTowerType.RocketBarrage, new Color(1f, 0.54f, 0.06f, 1f));
        CreateReinforcementTowerBuildButton(buildSection.transform, buildColumns[4], 28f);

        _towerCancelBuildButton = CreateUiButton("Cancel Build", actionSection.transform, "[Esc] 取消",
            new Color(0.55f, 0.08f, 0.1f, 1f));
        _towerCancelBuildButtonText = _towerCancelBuildButton.GetComponentInChildren<Text>();
        StyleCommandButton(_towerCancelBuildButton, new Color(1f, 0.20f, 0.24f, 1f));
        _towerCancelBuildButton.onClick.AddListener(CancelTowerBuildSelection);

        _towerUpgradeButton = CreateUiButton("Upgrade", actionSection.transform, "[U] 升级", new Color(0.15f, 0.58f, 0.28f, 1f));
        StyleCommandButton(_towerUpgradeButton, new Color(0.22f, 1f, 0.42f, 1f));
        _towerUpgradeButton.onClick.AddListener(TryUpgradeSelectedTowerPrimaryButton);
        _towerUpgradeButtonText = _towerUpgradeButton.GetComponentInChildren<Text>();

        _towerUpgradeChoiceButton = CreateUiButton("Upgrade Choice B", actionSection.transform,
            "分支升级 B", new Color(0.12f, 0.46f, 0.72f, 1f));
        StyleCommandButton(_towerUpgradeChoiceButton, new Color(0.20f, 0.66f, 1f, 1f));
        _towerUpgradeChoiceButton.onClick.AddListener(() =>
            TryUpgradeSelectedTowerChoice(1));
        _towerUpgradeChoiceButtonText =
            _towerUpgradeChoiceButton.GetComponentInChildren<Text>();
        _towerUpgradeChoiceButton.gameObject.SetActive(false);

        _towerSellButton = CreateUiButton("Sell Tower", actionSection.transform, "▣  出售", new Color(0.72f, 0.08f, 0.1f, 0.98f));
        StyleCommandButton(_towerSellButton, new Color(1f, 0.28f, 0.24f, 1f));
        _towerSellButtonText = _towerSellButton.GetComponentInChildren<Text>();
        _towerSellButton.onClick.AddListener(SellSelectedTower);
        _towerSellButton.gameObject.SetActive(false);

        _towerTargetPriorityButton = CreateUiButton("Target Priority", actionSection.transform, "◎  索敌模式\n离终点最近",
            new Color(0.12f, 0.38f, 0.68f, 1f));
        StyleCommandButton(_towerTargetPriorityButton, new Color(0.22f, 0.70f, 1f, 1f));
        _towerTargetPriorityButtonText = _towerTargetPriorityButton.GetComponentInChildren<Text>();
        _towerTargetPriorityButton.onClick.AddListener(ToggleSelectedTowerTargetPriority);
        _towerTargetPriorityButton.gameObject.SetActive(false);

        _towerRelocateButton = CreateUiButton("Relocate Tower", actionSection.transform, "[R] 搬运",
            new Color(0.55f, 0.22f, 0.72f, 1f));
        StyleCommandButton(_towerRelocateButton, new Color(0.86f, 0.38f, 1f, 1f));
        _towerRelocateButtonText = _towerRelocateButton.GetComponentInChildren<Text>();
        _towerRelocateButton.onClick.AddListener(BeginSelectedTowerRelocation);
        _towerRelocateButton.gameObject.SetActive(false);

        if (CommanderSkillsEnabled) BuildTacticalSkillUi(canvasObject.transform);

        BuildChargeTowerEffectSelectionUi(canvasObject.transform);

        _bossPanel = CreateUiPanel("Boss Panel", canvasObject.transform, new Color(0.08f, 0.015f, 0.1f, 0.94f));
        RectTransform bossRect = _bossPanel.GetComponent<RectTransform>();
        bossRect.anchorMin = new Vector2(0.5f, 1f);
        bossRect.anchorMax = new Vector2(0.5f, 1f);
        bossRect.pivot = new Vector2(0.5f, 1f);
        bossRect.anchoredPosition = new Vector2(0f, -24f);
        bossRect.sizeDelta = new Vector2(680f, 112f);
        AddHudPanelChrome(_bossPanel, new Color(0.92f, 0.12f, 0.78f, 1f));
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
        AddHudPanelChrome(gameOverPanel, new Color(0.95f, 0.18f, 0.12f, 1f));
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
        rect.sizeDelta = new Vector2(132f, 56f);
        Text labelText = button.GetComponentInChildren<Text>();
        if (labelText != null)
        {
            labelText.fontSize = 15;
            labelText.alignment = TextAnchor.MiddleLeft;
            StretchRect(labelText.rectTransform, 44f, 3f, 4f, 3f);
        }
        StyleCommandButton(button, color);
        CreateBuildButtonGlyph(button.transform, GetTowerBuildGlyph(type), color);
        button.onClick.AddListener(() => BeginTowerBuild(type));
        int index = (int)type;
        if ((uint)index < (uint)_towerBuildButtons.Length)
        {
            _towerBuildButtons[index] = button;
            _towerBuildButtonTexts[index] = labelText;
        }
    }

    private void RefreshSelectedTowerCommandCard()
    {
        if (_selectedTowerSummaryText == null || _selectedTowerBuffText == null ||
            _selectedTowerPortrait == null || _selectedTowerPortraitFrame == null) return;

        bool hasPreview = _towerPreview != null &&
                          _towerPreview.gameObject.activeInHierarchy;
        RougeDefenseTower tower = hasPreview ? _towerPreview : _selectedTower;
        if (tower == null || !tower.gameObject.activeInHierarchy)
        {
            _selectedTowerPortrait.enabled = false;
            _selectedTowerPortrait.sprite = null;
            _selectedTowerPortraitFrame.color = new Color(0.025f, 0.065f, 0.09f, 0.96f);
            _selectedTowerSummaryText.text = "<b>未选择塔楼</b>";
            _selectedTowerBuffText.text = string.Empty;
            return;
        }

        Color towerColor = TowerDefenseVisuals.GetTowerColor(tower.TowerType);
        _selectedTowerPortraitFrame.color = Color.Lerp(
            new Color(0.012f, 0.035f, 0.052f, 0.98f), towerColor, 0.18f);
        Outline portraitOutline = _selectedTowerPortraitFrame.GetComponent<Outline>();
        if (portraitOutline != null)
            portraitOutline.effectColor = new Color(towerColor.r, towerColor.g,
                towerColor.b, 0.78f);

        Sprite portrait = ResolveTowerPortraitSprite(tower);
        _selectedTowerPortrait.sprite = portrait;
        _selectedTowerPortrait.enabled = portrait != null;

        float attacksPerSecond = 1f / Mathf.Max(0.01f, tower.EffectiveAttackInterval);
        float estimatedDps = tower.Damage * attacksPerSecond *
            Mathf.Max(1, tower.AttackTargetCount) * Mathf.Max(1, tower.AttackProjectileCount);
        string contextLabel = hasPreview
            ? "<color=#FFE075><size=14><b>建造预览</b></size></color>  "
            : string.Empty;
        _selectedTowerSummaryText.text =
            $"{contextLabel}<b>{tower.DisplayName}</b>  <size=15>Lv.{tower.Level}</size>\n" +
            $"<size=16>攻击范围 {tower.AttackRange:0.#}    DPS {FormatCompactDamage(estimatedDps)}</size>";

        string route = GetTowerRouteHudLabel(tower);
        string buffs = ColorizeTowerBuffText(tower.GetBuffDisplayText());
        string tile = tower.TowerPlaceEffect == RougeTowerPlaceEffect.None
            ? string.Empty
            : $"<color=#8CFFF0>◆ {GetTowerPlaceEffectShortName(tower.TowerPlaceEffect)}</color>";
        System.Text.StringBuilder builder = _hudBuilder;
        builder.Clear();
        if (!string.IsNullOrEmpty(route)) builder.Append(route);
        if (!string.IsNullOrEmpty(buffs))
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.Append(buffs);
        }
        if (!string.IsNullOrEmpty(tile))
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.Append(tile);
        }
        if (hasPreview)
        {
            string tileDescription = GetTowerPlaceEffectDescription(
                tower.TowerPlaceEffect, tower);
            if (!string.IsNullOrWhiteSpace(tileDescription))
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.Append("<color=#C8DCE8>")
                    .Append(tileDescription.Trim()).Append("</color>");
            }
            if (builder.Length > 0) builder.AppendLine();
            builder.Append("<color=#FFE075>建造花费 ")
                .Append(tower.PlacementCost).Append(" 金币</color>");
        }
        _selectedTowerBuffText.text = builder.ToString();
    }

    private static Sprite ResolveTowerPortraitSprite(RougeDefenseTower tower)
    {
        RougeBillboard billboard = tower != null
            ? tower.GetComponentInChildren<RougeBillboard>(true)
            : null;
        if (billboard == null) return null;
        SpriteRenderer[] renderers = billboard.GetComponentsInChildren<SpriteRenderer>(true);
        Sprite best = null;
        float bestArea = -1f;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null ||
                !renderer.gameObject.activeInHierarchy) continue;
            Rect rect = renderer.sprite.rect;
            float area = rect.width * rect.height;
            if (area <= bestArea) continue;
            bestArea = area;
            best = renderer.sprite;
        }
        return best;
    }

    private static string GetTowerRouteHudLabel(RougeDefenseTower tower)
    {
        if (tower == null) return string.Empty;
        if (tower.UsesIceFreeze) return "<color=#FFD45C>◆ A 路线 · 冻结</color>";
        if (tower.UsesIceVulnerability) return "<color=#C77DFF>◆ B 路线 · 脆弱</color>";
        if (tower.UsesMachineGunCritical) return "<color=#FFD45C>◆ A 路线 · 暴击</color>";
        if (tower.UsesMachineGunFragments) return "<color=#C77DFF>◆ B 路线 · 破片</color>";
        if (tower.UsesCannonInnerBlast) return "<color=#FFD45C>◆ A 路线 · 内圈爆破</color>";
        if (tower.UsesPersistentCannonShell) return "<color=#C77DFF>◆ B 路线 · 持续炮弹</color>";
        if (tower.UsesLaserArmorBreak) return "<color=#FFD45C>◆ A 路线 · 破甲</color>";
        if (tower.UsesLaserRefraction) return "<color=#C77DFF>◆ B 路线 · 折射</color>";
        return string.Empty;
    }

    private static string ColorizeTowerBuffText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string[] entries = text.Split(new[] { "  " },
            System.StringSplitOptions.RemoveEmptyEntries);
        System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length + 48);
        for (int i = 0; i < entries.Length; i++)
        {
            if (i > 0) builder.Append("   ");
            bool negative = entries[i].Contains("-");
            builder.Append(negative ? "<color=#FF8C96>▼ " : "<color=#9CFFAE>▲ ")
                .Append(entries[i]).Append("</color>");
        }
        return builder.ToString();
    }

    private void LayoutTowerActionButtons()
    {
        if (_towerActionContainer == null) return;
        int count = CountActiveActionButtons();
        int index = 0;
        LayoutTowerActionButton(_towerSellButton, ref index, count);
        LayoutTowerActionButton(_towerTargetPriorityButton, ref index, count);
        LayoutTowerActionButton(_towerRelocateButton, ref index, count);
        LayoutTowerActionButton(_towerUpgradeButton, ref index, count);
        LayoutTowerActionButton(_towerUpgradeChoiceButton, ref index, count);
        LayoutTowerActionButton(_towerCancelBuildButton, ref index, count);
    }

    private int CountActiveActionButtons()
    {
        int count = 0;
        if (IsActiveActionButton(_towerSellButton)) count++;
        if (IsActiveActionButton(_towerTargetPriorityButton)) count++;
        if (IsActiveActionButton(_towerRelocateButton)) count++;
        if (IsActiveActionButton(_towerUpgradeButton)) count++;
        if (IsActiveActionButton(_towerUpgradeChoiceButton)) count++;
        if (IsActiveActionButton(_towerCancelBuildButton)) count++;
        return count;
    }

    private static bool IsActiveActionButton(Button button)
    {
        return button != null && button.gameObject.activeSelf;
    }

    private static void LayoutTowerActionButton(Button button, ref int index, int count)
    {
        if (!IsActiveActionButton(button)) return;
        int rows = Mathf.Max(1, (count + 1) / 2);
        int row = index / 2;
        int column = index % 2;
        bool centeredLast = (count & 1) != 0 && index == count - 1;
        float x = centeredLast ? 0f : column == 0 ? -107f : 107f;
        float startY = rows == 1 ? 77f : rows == 2 ? 103f : 128f;
        float step = rows == 3 ? 54f : 66f;
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(x, startY - row * step);
        rect.sizeDelta = new Vector2(centeredLast ? 420f : 206f, 48f);
        index++;
    }

    private static void SetPurchaseButtonAvailability(Button button, Text text, bool available)
    {
        if (button != null) button.interactable = available;
        if (text != null) text.color = available ? Color.white : new Color(0.48f, 0.5f, 0.54f, 1f);
    }

    private static string GetTowerBuildLabel(int hotkey, RougeTowerType type)
    {
        TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out _, out int cost);
        return $"[{hotkey}] {TowerDefenseVisuals.GetTowerName(type)}\n{Mathf.Max(0, cost)} 金币";
    }

    private static string GetTowerBuildGlyph(RougeTowerType type)
    {
        switch (type)
        {
            case RougeTowerType.Ice: return "❄";
            case RougeTowerType.MachineGun: return "⌁";
            case RougeTowerType.Cannon: return "◉";
            case RougeTowerType.Flame: return "♨";
            case RougeTowerType.Laser: return "✦";
            case RougeTowerType.PiercingLaser: return "ϟ";
            case RougeTowerType.OrbitSphere: return "♦";
            case RougeTowerType.RocketBarrage: return "➤";
            case RougeTowerType.ChargeTower: return "◇";
            case RougeTowerType.ReinforcementTower: return "◆";
            default: return "·";
        }
    }

    private string GetBossScheduleStatus()
    {
        string activeBossName = GetLocalizedBossName(bossBalance != null ? bossBalance.displayName : null);
        if (_bossDeathSequenceActive) return $"{activeBossName} 已击破";
        if (_bossSpawned) return $"{activeBossName} 交战中";
        if (_nextBossEncounterIndex >= _bossSchedule.Count) return "首领已全部击破";
        RougeTowerDefenseMap.BossEncounter next = _bossSchedule[_nextBossEncounterIndex];
        RougeBossBalanceConfig nextBalance = FindBossBalance(next.bossId);
        string bossName = nextBalance != null && !string.IsNullOrWhiteSpace(nextBalance.displayName)
            ? GetLocalizedBossName(nextBalance.displayName)
            : $"首领 {next.bossId}";
        float countdown = Mathf.Max(0f, next.spawnMinute * 60f - _survivalTime);
        return $"{bossName}来袭 {FormatGameTime(countdown)}";
    }

    private static string GetLocalizedBossName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "首领";
        if (string.Equals(displayName, "Overlord", System.StringComparison.OrdinalIgnoreCase)) return "霸主";
        if (displayName.StartsWith("Boss ", System.StringComparison.OrdinalIgnoreCase))
            return "首领 " + displayName.Substring(5);
        return displayName;
    }

    private void RefreshTowerDefenseControlsHud()
    {
        if (_towerDefenseControlsText == null) return;

        string qualityHint =
            $"[F5] 光影 {RougeVisualQualityManager.ActiveTierLabel} · " +
            $"[F6] {TowerDefenseAutoplayCharacterName}托管";
        string speedHint = _towerDefenseDoubleSpeed
            ? "[F10] 速度 ×2"
            : "[F10] 速度 ×1";

        if (IsTiltShiftObservationActive || _cameraViewMode == CameraViewMode.TiltShift)
        {
            _towerDefenseControlsText.text =
                "[F2 观赏] 左键塔楼：退出并编辑 · 左键空地：显示主塔血条 · " +
                "[F1] 自由 · [F2] 默认 · [F3] 俯视 · [Esc] 设置\n" + qualityHint;
            return;
        }

        if (_chargeTowerEffectSelectionActive)
        {
            _towerDefenseControlsText.text =
                "[充能效果] 点击效果卡片确认 · [Esc] 取消\n" + qualityHint;
            return;
        }

        if (_chargeTowerTargetSelectionActive)
        {
            _towerDefenseControlsText.text =
                "[充能目标] 左键选择有效地图格 · [Esc] 取消\n" + qualityHint;
            return;
        }

        string cameraHint;
        switch (_cameraViewMode)
        {
            case CameraViewMode.Free:
                cameraHint = "[F1] 默认 · [F2] 观赏 · [F3] 俯视 · " +
                             "WASD 平移 · 滚轮升降 · 按住右键旋转 · Shift 加速";
                break;
            case CameraViewMode.TopDown:
                cameraHint = "[F1] 自由 · [F2] 观赏 · [F3] 默认 · " +
                             (_towerPlacementMode
                                 ? "中键拖动平移 · 滚轮调整视距"
                                 : "左键/中键拖动平移 · 滚轮调整视距");
                break;
            default:
                cameraHint = "[F1] 自由 · [F2] 观赏 · [F3] 俯视 · " +
                             (_towerPlacementMode
                                 ? "中键拖动平移 · 滚轮调整视距"
                                 : "左键/中键拖动平移 · 滚轮调整视距");
                break;
        }

        if (_towerPlacementMode)
        {
            string rangeHint = _showAllTowerAttackRanges
                ? "[F4] 隐藏全塔范围"
                : "[F4] 显示全塔范围";
            string cancelHint = "[右键/Esc] 退出建造";
            string actionHint;

            if (_towerRelocationActive)
            {
                cancelHint = "[右键/Esc] 取消搬运";
                actionHint = "[搬运] 左键确认落点 · " + cancelHint + " · " + rangeHint;
            }
            else if (HasTacticalSkillSelection)
            {
                actionHint = "[技能目标] 左键确认 · [Esc] 取消 · " + rangeHint;
            }
            else if (_towerPreview != null && _towerPreview.gameObject.activeInHierarchy)
            {
                actionHint = "[建造] [1-8] 标准塔 · [C] 充能塔 · [V] 强化塔 · " +
                             "左键放置 · " + cancelHint + " · " + rangeHint;
            }
            else if (_selectedTower != null && _selectedTower.gameObject.activeInHierarchy)
            {
                string upgradeHint = _selectedTower.CanUpgrade ? "[U] 升级" : "已满级";
                string relocateHint = _selectedTower.CanRelocate ? " · [R] 搬运" : string.Empty;
                string targetHint = _selectedTower.IsTargetedDamage
                    ? " · [中键] 切换索敌"
                    : string.Empty;
                actionHint = "[塔楼编辑] 左键选择塔楼 · " + upgradeHint +
                             relocateHint + targetHint + " · " + cancelHint + " · " + rangeHint;
            }
            else
            {
                actionHint = "[建造] 点击建造按钮或按 [1-8/C/V] 选择塔楼 · " +
                             cancelHint + " · " + rangeHint;
            }

            _towerDefenseControlsText.text = actionHint + "\n" + cameraHint +
                                             " · " + qualityHint;
            return;
        }

        string viewLabel = _cameraViewMode == CameraViewMode.Free
            ? "[自由镜头]"
            : _cameraViewMode == CameraViewMode.TopDown
                ? "[俯视镜头]"
                : "[默认镜头]";
        _towerDefenseControlsText.text = viewLabel + " " + cameraHint +
            "\n左键塔楼进入编辑 · 点击建造按钮开始建造 · [Esc] 设置 · " +
            qualityHint + " · " + speedHint;
    }

    private void RefreshTowerDefenseUi(bool force = false)
    {
        if (_towerDefenseCanvas == null) return;
        if (!force && Time.unscaledTime < _nextTowerDefenseUiRefreshTime) return;
        _nextTowerDefenseUiRefreshTime = Time.unscaledTime + 0.1f;
        RefreshTowerEditHints();
        if (_visualQualityButtonText != null)
            _visualQualityButtonText.text = $"[F5] 光影 {RougeVisualQualityManager.ActiveTierLabel}";
        RefreshTowerDefenseControlsHud();
        float mainTowerHp = mainTower != null ? mainTower.CurrentHealth : 0f;
        float mainTowerMaxHp = mainTower != null ? mainTower.maxHealth : 0f;
        if (_towerDefenseStatusText != null)
        {
            int activeSpawners = 0;
            for (int i = 0; i < _towerDefenseSpawners.Count; i++)
            {
                if (_towerDefenseSpawners[i] != null && _towerDefenseSpawners[i].isActiveAndEnabled) activeSpawners++;
            }
            string bossTime = GetBossScheduleStatus();
            int enemyLevel = GetTowerDefenseEnemyLevel();
            float enemyHealthBonus = (GetTowerDefenseEnemyHealthMultiplier() - 1f) * 100f;
            float enemySpeedBonus = (enemyBalance.EvaluateSpeedMultiplier(enemyLevel) *
                                     GetTowerDefenseEnemyMoveSpeedMultiplier() - 1f) * 100f;
            _towerDefenseStatusText.text =
                $"时间 {FormatGameTime(_survivalTime)}    当前金币 {_towerDefenseGold}    剩余敌人 {_towerDefenseAliveEstimate}\n" +
                $"击杀 {totalKills}    累计金币 {_towerDefenseGoldEarnedTotal}    {bossTime}\n" +
                $"敌人 Lv.{enemyLevel}    生命 +{enemyHealthBonus:0.#}%    移速 +{enemySpeedBonus:0.#}%\n" +
                $"出生点 {activeSpawners}/{_towerDefenseSpawners.Count}";
        }
        if (_mainTowerHealthFill != null)
            SetUiBarFill(_mainTowerHealthFill, mainTower != null ? mainTower.HealthNormalized : 0f);
        if (_mainTowerHealthText != null)
            _mainTowerHealthText.text = $"主塔  {mainTowerHp:0} / {mainTowerMaxHp:0}";
        for (int typeIndex = 0; typeIndex < _towerBuildButtons.Length; typeIndex++)
        {
            RougeTowerType type = (RougeTowerType)typeIndex;
            TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out _, out _);
            bool disabled = IsTowerTypeDisabled(type);
            SetPurchaseButtonAvailability(_towerBuildButtons[typeIndex], _towerBuildButtonTexts[typeIndex],
                !disabled && !_towerDefenseGameOver && CanAffordTowerType(type));
            if (_towerBuildButtonTexts[typeIndex] != null)
                _towerBuildButtonTexts[typeIndex].text = disabled
                    ? $"{TowerDefenseVisuals.GetTowerName(type)}\n未解锁"
                    : GetTowerBuildLabel(typeIndex + 1, type);
        }
        SetPurchaseButtonAvailability(_chargeTowerBuildButton, _chargeTowerBuildButtonText,
            !_towerDefenseGameOver && CanAffordTowerType(RougeTowerType.ChargeTower));
        if (_chargeTowerBuildButtonText != null)
            _chargeTowerBuildButtonText.text = IsTowerTypeDisabled(RougeTowerType.ChargeTower)
                ? "充能塔\n未解锁"
                : GetChargeTowerBuildLabel();
        SetPurchaseButtonAvailability(_reinforcementTowerBuildButton,
            _reinforcementTowerBuildButtonText,
            !_towerDefenseGameOver && CanAffordTowerType(RougeTowerType.ReinforcementTower));
        if (_reinforcementTowerBuildButtonText != null)
            _reinforcementTowerBuildButtonText.text =
                IsTowerTypeDisabled(RougeTowerType.ReinforcementTower)
                    ? "强化塔\n未解锁"
                    : GetReinforcementTowerBuildLabel();
        RefreshChargeTowerEffectSelectionUi();
        RefreshTowerDamageRanking();
        if (CommanderSkillsEnabled) RefreshTacticalSkillUi();
        if (_towerCancelBuildButton != null)
        {
            bool canCancel = _towerPlacementMode &&
                (HasTacticalSkillSelection || _towerBuildSelectionActive ||
                 _chargeTowerBuildSelectionActive || _reinforcementTowerBuildSelectionActive ||
                 _chargeTowerTargetSelectionActive || _towerRelocationActive ||
                 _selectedTower != null);
            bool showCancel = canCancel &&
                (HasTacticalSkillSelection || _towerBuildSelectionActive ||
                 _chargeTowerBuildSelectionActive || _reinforcementTowerBuildSelectionActive ||
                 _chargeTowerTargetSelectionActive || _towerRelocationActive);
            _towerCancelBuildButton.gameObject.SetActive(showCancel);
            SetPurchaseButtonAvailability(_towerCancelBuildButton, _towerCancelBuildButtonText, canCancel);
            if (_towerCancelBuildButtonText != null)
                _towerCancelBuildButtonText.text = _towerRelocationActive ? "[Esc] 取消搬运" : "取消";
        }
        if (_towerSellButton != null)
        {
            bool showSell = _towerPlacementMode && !_towerRelocationActive && _selectedTower != null;
            _towerSellButton.gameObject.SetActive(showSell);
            if (showSell)
            {
                bool canSell = CanSellTower(_selectedTower);
                int refund = _selectedTower.AllowsSellRefund
                    ? Mathf.FloorToInt(_selectedTower.InvestedGold * Mathf.Clamp01(towerBalance.sellRefundMultiplier))
                    : 0;
                SetPurchaseButtonAvailability(_towerSellButton, _towerSellButtonText, canSell);
                if (_towerSellButtonText != null)
                    _towerSellButtonText.text = canSell
                        ? $"出售  +{refund}"
                        : "不可出售";
            }
        }
        if (_towerTargetPriorityButton != null)
        {
            bool showPriority = _towerPlacementMode && !_towerRelocationActive &&
                _selectedTower != null && _selectedTower.IsTargetedDamage &&
                _selectedTower.CanToggleTargetPriority;
            _towerTargetPriorityButton.gameObject.SetActive(showPriority);
            if (showPriority && _towerTargetPriorityButtonText != null)
            {
                if (_selectedTower.TowerType == RougeTowerType.MachineGun)
                {
                    _towerTargetPriorityButtonText.text =
                        _selectedTower.TargetPriority == RougeTowerTargetPriority.BossFirst
                            ? "[中键] 集中模式\n伤害 Lv-1 / 攻速 Lv-1"
                            : "[中键] 散射\n离终点最近";
                }
                else if (_selectedTower.TowerType == RougeTowerType.Flame)
                {
                    _towerTargetPriorityButtonText.text =
                        _selectedTower.TargetPriority == RougeTowerTargetPriority.BossFirst
                            ? _selectedTower.UsesFanFlamethrower
                                ? "[中键] 集中喷火\n合并角度 / 伤害 Lv-2"
                                : "[中键] 集火首领\n伤害 Lv-2"
                            : "[中键] 轮换目标\n离终点最近";
                }
                else if (_selectedTower.TowerType == RougeTowerType.Laser)
                {
                    _towerTargetPriorityButtonText.text =
                        _selectedTower.TargetPriority == RougeTowerTargetPriority.BossFirst
                            ? _selectedTower.IgnoresFocusedLaserPenalty
                                ? "[中键] 集中模式\n无伤害与攻速减益"
                                : "[中键] 集中模式\n伤害 Lv-1 / 攻速 Lv-1"
                            : "[中键] 分散模式\n离终点最近";
                }
                else
                {
                    _towerTargetPriorityButtonText.text =
                        _selectedTower.TargetPriority == RougeTowerTargetPriority.BossFirst
                            ? "[中键] 索敌\n首领优先"
                            : "[中键] 索敌\n离终点最近";
                }
            }
        }
        if (_towerRelocateButton != null)
        {
            RougeDefenseTower relocationTower = _towerRelocationActive ? _relocatingTower : _selectedTower;
            bool showRelocate = _towerPlacementMode && relocationTower != null && relocationTower.CanRelocate;
            _towerRelocateButton.gameObject.SetActive(showRelocate);
            if (showRelocate)
            {
                bool canBeginRelocation = !_towerRelocationActive &&
                    _towerDefenseGold >= relocationTower.RelocationCost;
                SetPurchaseButtonAvailability(_towerRelocateButton, _towerRelocateButtonText, canBeginRelocation);
                if (_towerRelocateButtonText != null)
                    _towerRelocateButtonText.text = _towerRelocationActive
                        ? $"搬运中\n费用 {relocationTower.RelocationCost}"
                        : $"[R] 搬运\n{relocationTower.RelocationCost} 金币";
            }
        }
        if (_bossPanel != null)
        {
            _bossPanel.SetActive(_bossSpawned || _bossDeathSequenceActive);
            float bossHealth = _bossSpawned ? Mathf.Max(0f, _bossCurrentHealth) : 0f;
            float bossMaximumHealth = GetCurrentBossMaxHealth();
            float bossHealthRatio = Mathf.Clamp01(bossHealth / bossMaximumHealth);
            if (_bossHealthFill != null) SetUiBarFill(_bossHealthFill, bossHealthRatio);
            RefreshBossThresholdMarkers();
            if (_bossStatusText != null)
            {
                string phases = $"{(_bossInterferenceActive ? "干扰 " : "")}{(_bossShieldActive ? "护盾 " : "")}{(_bossHasteActive ? "狂暴" : "")}";
                _bossStatusText.text = _bossDeathSequenceActive
                    ? "首领核心过载"
                    : $"{GetLocalizedBossName(bossBalance.displayName)}  {bossHealth:0} / {bossMaximumHealth:0}  ({bossHealthRatio * 100f:0.00}%)   {phases}";
            }
        }
        if (_towerUpgradeButton != null)
        {
            bool hasSelection = _selectedTower != null;
            bool canUpgrade = hasSelection && _selectedTower.CanUpgrade;
            bool upgradeAvailable = !_towerRelocationActive && canUpgrade &&
                _towerDefenseGold >= _selectedTower.UpgradeCost;
            bool showUpgradeChoices = !_towerRelocationActive && hasSelection &&
                                      _selectedTower.RequiresUpgradeChoice;
            _towerUpgradeButton.gameObject.SetActive(!_towerRelocationActive &&
                                                     canUpgrade);
            if (_towerUpgradeChoiceButton != null)
            {
                _towerUpgradeChoiceButton.gameObject.SetActive(showUpgradeChoices);
                SetPurchaseButtonAvailability(_towerUpgradeChoiceButton,
                    _towerUpgradeChoiceButtonText, showUpgradeChoices && upgradeAvailable);
            }
            SetPurchaseButtonAvailability(_towerUpgradeButton, _towerUpgradeButtonText, upgradeAvailable);
            if (_towerUpgradeButtonText != null)
            {
                _towerUpgradeButtonText.text = showUpgradeChoices
                    ? GetUpgradeChoiceButtonText(_selectedTower, 0)
                    : _towerRelocationActive
                    ? "搬运中\n不可升级"
                    : !hasSelection
                    ? "选择塔楼\n进行升级"
                    : !canUpgrade
                        ? _selectedTower.IsSpecialTower
                            ? $"{_selectedTower.DisplayName}\n不可升级"
                            : $"等级 {_selectedTower.Level}/{_selectedTower.MaxLevel}\n已满级"
                        : $"[U] {_selectedTower.Level} → {_selectedTower.Level + 1} 级\n{_selectedTower.UpgradeCost} 金币";
            }
            if (showUpgradeChoices && _towerUpgradeChoiceButtonText != null)
                _towerUpgradeChoiceButtonText.text =
                    GetUpgradeChoiceButtonText(_selectedTower, 1);
        }
        RefreshSelectedTowerCommandCard();
        RefreshTowerPlaceEffectHud();
        LayoutTowerActionButtons();
        if (_towerDefenseGameOverText != null)
        {
            GameObject panel = _towerDefenseGameOverText.transform.parent.gameObject;
            panel.SetActive(_towerDefenseGameOver);
            if (_towerDefenseGameOver)
            {
                _towerDefenseGameOverText.text = _towerDefenseVictory
                    ? $"任务完成\n{_towerDefenseGameOverReason}\n\n按 R 重新开始"
                    : $"任务失败\n{_towerDefenseGameOverReason}\n\n按 R 重新开始";
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
        double topDamage = _towerDamageTotalsFixed[_towerDamageRankOrder[0]] / 1000.0;
        for (int rank = 0; rank < _towerDamageRankOrder.Length; rank++)
        {
            int typeIndex = _towerDamageRankOrder[rank];
            double damage = _towerDamageTotalsFixed[typeIndex] / 1000.0;
            RougeTowerType type = (RougeTowerType)typeIndex;
            Color towerColor = Color.Lerp(TowerDefenseVisuals.GetTowerColor(type),
                Color.white, 0.08f);
            string colorHex = ColorUtility.ToHtmlStringRGB(towerColor);
            int filledSegments = topDamage > 0.001
                ? Mathf.Clamp(Mathf.CeilToInt((float)(damage / topDamage) * 6f), 1, 6)
                : 0;
            builder.Append("<color=#").Append(colorHex).Append("><b>")
                .Append(rank + 1).Append(". ")
                .Append(TowerDefenseVisuals.GetTowerName(type)).Append("</b>  ")
                .Append('━', filledSegments).Append("</color>")
                .Append("<color=#29485A>").Append('━', 6 - filledSegments)
                .Append("</color>  <b>").Append(FormatCompactDamage(damage))
                .Append("</b>");
            if (rank < _towerDamageRankOrder.Length - 1) builder.AppendLine();
        }
        _towerDamageRankingText.text = builder.ToString();
    }

    private void RefreshTowerPlaceEffectHud()
    {
        if (_towerPlaceEffectPanel == null || _towerPlaceEffectText == null) return;

        RougeDefenseTower contextTower = _towerPreview != null &&
                                         _towerPreview.gameObject.activeInHierarchy
            ? _towerPreview
            : _selectedTower;
        bool show = _towerPlacementMode && contextTower != null &&
                    contextTower.gameObject.activeInHierarchy;
        _towerPlaceEffectPanel.SetActive(show);
        if (!show) return;

        RougeTowerPlaceEffect effect = contextTower.TowerPlaceEffect;
        RougeTowerDefenseMap map = _towerDefenseLevel != null
            ? _towerDefenseLevel
            : RougeTowerDefenseMapLoader.ActiveMap;
        System.Text.StringBuilder builder = _hudBuilder;
        builder.Clear();

        string mapDescription = GetTowerPlaceEffectDescription(effect, contextTower);
        if (effect == RougeTowerPlaceEffect.AccumulatedWealth)
            mapDescription += "\n" + GetAccumulatedWealthTileStatus(
                contextTower.transform.position);
        AppendTowerInfoSection(builder,
            "地图格效果：" + GetTowerPlaceEffectShortName(effect), mapDescription);

        string towerDescription = GetTowerPlayerDescription(contextTower) +
                                  GetReinforcementTowerTileDescription(contextTower);
        if (contextTower.IsChargeTower)
        {
            RougeTowerPlaceEffect chargedEffect = contextTower.ChargedTileEffect;
            towerDescription += chargedEffect == RougeTowerPlaceEffect.None
                ? "\n尚未指定目标地图格。"
                : "\n目标地图格效果：" +
                  GetTowerPlaceEffectShortName(chargedEffect) + "。\n" +
                  GetTowerPlaceEffectDescription(chargedEffect, null);
            if (chargedEffect == RougeTowerPlaceEffect.AccumulatedWealth)
            {
                Vector3 targetPosition = map != null && contextTower.HasChargeTargetCell
                    ? map.CellCenter(contextTower.ChargeTargetCell)
                    : contextTower.transform.position;
                towerDescription += "\n" + GetAccumulatedWealthTileStatus(targetPosition);
            }
            if (contextTower.HasChargeTargetCell)
                towerDescription += $"\n目标格：[{contextTower.ChargeTargetCell.x}, " +
                                    $"{contextTower.ChargeTargetCell.y}]。";
        }
        AppendTowerInfoSection(builder,
            $"塔楼效果：{contextTower.DisplayName}  Lv.{contextTower.Level}/{contextTower.MaxLevel}",
            towerDescription);

        if (!contextTower.IsSpecialTower)
        {
            builder.AppendLine();
            builder.Append("<color=#7FEAFF><b>")
                .Append(GetTowerUiStats(contextTower))
                .AppendLine("</b></color>");
        }

        string towerBuffDescription = GetTowerBuffExplanation(contextTower);
        if (!string.IsNullOrEmpty(towerBuffDescription))
            AppendTowerInfoSection(builder, "当前 Buff：", towerBuffDescription, false);

        builder.AppendLine();
        builder.Append("<color=#FFD45C><b>");
        if (_towerRelocationActive && _relocatingTower != null)
            builder.Append("搬运费用：").Append(_relocatingTower.RelocationCost)
                .Append(" 金币");
        else if (contextTower == _towerPreview)
            builder.Append("建造花费：").Append(contextTower.PlacementCost)
                .Append(" 金币");
        else if (contextTower.CanUpgrade)
            builder.Append("下次升级：Lv.").Append(contextTower.Level + 1)
                .Append(" · ").Append(contextTower.UpgradeCost).Append(" 金币");
        else
            builder.Append("等级已满");
        builder.Append("</b></color>");

        _towerPlaceEffectText.text = builder.ToString();
        RectTransform infoRect = _towerPlaceEffectPanel.GetComponent<RectTransform>();
        if (infoRect != null)
        {
            // Keep the card only as tall as its content instead of covering a large
            // empty part of the board. The translucent backplate remains for contrast.
            float preferredHeight = _towerPlaceEffectText.preferredHeight;
            Vector2 size = infoRect.sizeDelta;
            size.y = Mathf.Clamp(preferredHeight + 68f, 210f, 700f);
            infoRect.sizeDelta = size;
        }
    }

    private static void AppendTowerInfoSection(System.Text.StringBuilder builder,
        string title, string description, bool addDiamonds = true)
    {
        if (string.IsNullOrWhiteSpace(description)) return;
        if (builder.Length > 0) builder.AppendLine();
        builder.Append("<color=#7FEAFF><b>").Append(title)
            .AppendLine("</b></color>");
        if (!addDiamonds)
        {
            builder.AppendLine(description.Trim());
            return;
        }

        string[] lines = description.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length > 0) builder.Append("◆ ").AppendLine(line);
        }
    }

    private static string GetTowerPlaceEffectShortName(RougeTowerPlaceEffect effect)
    {
        effect = RougeTowerPlaceEffectRules.NormalizeLegacy(effect);
        if (effect == RougeTowerPlaceEffect.None) return "无特殊效果";
        string displayName = RougeTowerPlaceEffectRules.GetDisplayName(effect);
        int separator = displayName.IndexOf(" - ", System.StringComparison.Ordinal);
        return separator >= 0 ? displayName.Substring(separator + 3) : displayName;
    }

    private static string GetTowerPlaceEffectDescription(RougeTowerPlaceEffect effect,
        RougeDefenseTower contextTower)
    {
        if (RougeTowerPlaceEffectRules.NormalizeLegacy(effect) !=
            RougeTowerPlaceEffect.Frost)
            return RougeTowerPlaceEffectRules.GetDescription(effect);

        return "直接伤害附加减速，范围伤害效果减半。";
    }

    private int GetReinforcementAuraLevelAtCell(RougeTowerDefenseMap map,
        Vector2Int contextCell)
    {
        if (map == null) return 0;
        int auraLevel = 0;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null || !tower.IsReinforcementTower ||
                (_towerRelocationActive && tower == _relocatingTower) ||
                !map.WorldToCell(tower.transform.position, out Vector2Int towerCell) ||
                Mathf.Max(Mathf.Abs(towerCell.x - contextCell.x),
                    Mathf.Abs(towerCell.y - contextCell.y)) > tower.ReinforcementAuraRangeCells)
                continue;
            auraLevel += tower.ReinforcementAuraBuffLevel;
        }
        return auraLevel;
    }

    private string GetReinforcementTowerTileDescription(RougeDefenseTower contextTower)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (contextTower == null || map == null ||
            !map.WorldToCell(contextTower.transform.position, out Vector2Int contextCell))
            return string.Empty;

        int auraLevel = GetReinforcementAuraLevelAtCell(map, contextCell);

        bool previewAddsAura = contextTower == _towerPreview &&
                               contextTower.IsReinforcementTower;
        if (previewAddsAura) auraLevel += contextTower.ReinforcementAuraBuffLevel;
        if (auraLevel <= 0) return string.Empty;

        string previewSuffix = previewAddsAura ? "建造后，" : string.Empty;
        int percent = RougeTowerBuffMath.GetPercent(auraLevel);
        return $"\n<color=#FFBF4A><b>{previewSuffix}附近强化塔会让这座塔的伤害、范围和攻速提高 {percent}%。</b></color>";
    }

    private string GetAccumulatedWealthTileStatus(Vector3 worldPosition)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        if (map == null || !map.WorldToCell(worldPosition, out Vector2Int cell))
            return "<color=#FFD45A><b>地块累计：0 金币</b></color>";
        int cellIndex = EncodeTowerDefenseMapCellIndex(cell);
        if (cellIndex < 0) return "<color=#FFD45A><b>地块累计：0 金币</b></color>";
        int pending = Mathf.Max(0, _accumulatedWealthPendingGold[cellIndex]);
        int payout = pending >= int.MaxValue / 2
            ? int.MaxValue
            : Mathf.CeilToInt(pending * AccumulatedWealthPayoutMultiplier);
        float remaining = _accumulatedWealthPayoutTimers[cellIndex] > 0f
            ? _accumulatedWealthPayoutTimers[cellIndex]
            : AccumulatedWealthPayoutInterval;
        return $"<color=#FFD45A><b>地块累计：{pending} 金币</b></color>  |  " +
               $"预计结算 +{payout}  |  {remaining:0.0} 秒后结算";
    }

    private static string FormatCompactDamage(double value)
    {
        if (value >= 100000000d) return $"{value / 100000000d:0.##}亿";
        if (value >= 10000d) return $"{value / 10000d:0.##}万";
        return value.ToString("0");
    }

    private static string FormatGameTime(float seconds)
    {
        int wholeSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{wholeSeconds / 60:00}:{wholeSeconds % 60:00}";
    }

    private static string GetTowerUiStats(RougeDefenseTower tower)
    {
        if (tower.IsChargeTower)
            return "辅助塔  不可攻击";
        if (tower.IsReinforcementTower)
            return $"强化范围 {tower.ReinforcementAuraRangeCells} 格";
        int barrageCount = tower.TowerType == RougeTowerType.MachineGun ||
                           tower.TowerType == RougeTowerType.Laser
            ? tower.AttackTargetCount
            : tower.AttackProjectileCount;
        barrageCount = Mathf.Max(1, barrageCount);
        float dpsPerBarrage;
        if (tower.TowerType == RougeTowerType.MachineGun)
        {
            dpsPerBarrage = tower.Damage /
                            Mathf.Max(0.001f, tower.EffectiveAttackInterval);
            return $"DPS：{dpsPerBarrage:0.##} × {barrageCount}\n" +
                   $"范围：{tower.AttackRange:0.#}";
        }
        if (tower.TowerType == RougeTowerType.Laser)
        {
            if (tower.UsesLaserRefractionAttack)
            {
                return $"伤害：{tower.Damage:0.##}\n" +
                       $"折射次数：{tower.LaserRefractionAttackCount}\n" +
                       $"范围：{tower.AttackRange:0.#}\n" +
                       $"攻击间隔：{tower.EffectiveAttackInterval:0.00} 秒";
            }
            dpsPerBarrage = tower.Damage /
                             Mathf.Max(0.001f, tower.EffectiveAttackInterval);
            return $"DPS：{dpsPerBarrage:0.##} × {barrageCount}\n" +
                   $"范围：{tower.AttackRange:0.#}";
        }
        if (tower.UsesFlamethrower)
        {
            dpsPerBarrage = tower.Damage /
                            Mathf.Max(0.001f, tower.EffectiveAttackInterval);
            int displayedJets = tower.UsesFanFlamethrower &&
                                tower.TargetPriority == RougeTowerTargetPriority.BossFirst
                ? 1
                : barrageCount;
            RougeFlameTowerSpecializationConfig flame =
                TowerDefenseVisuals.GetFlameSpecializationConfig();
            float displayedAngle = flame.flamethrowerAngle +
                (tower.UsesFanFlamethrower &&
                 tower.TargetPriority == RougeTowerTargetPriority.BossFirst
                    ? barrageCount * flame.focusedAnglePerProjectile
                    : 0f);
            return $"DPS：{dpsPerBarrage:0.##} × {displayedJets}\n" +
                   $"喷火角：{displayedAngle:0.#}°\n" +
                   $"范围：{tower.AttackRange:0.#}";
        }
        if (tower.TowerType == RougeTowerType.OrbitSphere)
        {
            float effectiveTickInterval = tower.TickInterval /
                Mathf.Max(0.01f, tower.AttackSpeedMultiplier);
            dpsPerBarrage = tower.Damage / Mathf.Max(0.001f, effectiveTickInterval);
            return $"DPS：{dpsPerBarrage:0.##} × {barrageCount}\n" +
                   $"范围：{tower.AttackRange:0.#}";
        }
        return $"伤害：{tower.Damage:0.##}\n" +
               $"范围：{tower.AttackRange:0.#}\n" +
               $"攻击间隔：{tower.EffectiveAttackInterval:0.00} 秒";
    }

    private static string GetTowerBuffExplanation(RougeDefenseTower tower)
    {
        if (tower == null) return string.Empty;
        string explanation = string.Empty;
        AppendTowerBuffExplanation(ref explanation, tower, "伤害",
            "造成的伤害", RougeTowerBuffStat.Damage);
        AppendTowerBuffExplanation(ref explanation, tower, "范围",
            "攻击范围", RougeTowerBuffStat.Range);
        AppendTowerBuffExplanation(ref explanation, tower, "攻速",
            "攻击速度", RougeTowerBuffStat.AttackSpeed);
        return explanation;
    }

    private static void AppendTowerBuffExplanation(ref string text,
        RougeDefenseTower tower, string label, string effectLabel,
        RougeTowerBuffStat stat)
    {
        int level = tower.GetEffectiveBuffLevel(stat);
        if (level == 0) return;
        if (text.Length > 0) text += "\n";
        int percent = RougeTowerBuffMath.GetPercent(level);
        string color = level > 0 ? "#75F59A" : "#FF8078";
        text += $"◆ <color={color}><b>{label} {(level > 0 ? "+" : string.Empty)}{level}</b></color>：" +
                $"{effectLabel} {(percent > 0 ? "+" : string.Empty)}{percent}%";
    }

    private static string GetTowerPlayerDescription(RougeDefenseTower tower)
    {
        if (tower == null) return string.Empty;
        if (tower.IsChargeTower)
            return "用途：改变附近一格的特殊效果。";
        if (tower.IsReinforcementTower)
            return "用途：提高附近塔楼的伤害、范围和攻速。";

        switch (tower.TowerType)
        {
            case RougeTowerType.Ice:
            {
                RougeIceTowerSpecializationConfig config =
                    TowerDefenseVisuals.GetIceSpecializationConfig();
                if (tower.UsesIceFreeze)
                {
                    string text = "攻击会冻结范围内的敌人；精英和首领更快解冻。";
                    if (tower.IceAugment == RougeIceTowerAugment.IceSpikes)
                        return text + " 还会定时在攻击范围内生成冰地刺。";
                    if (tower.IceAugment == RougeIceTowerAugment.PermanentFrostTiles)
                        return text + " 周围 8 格会永久变成霜寒格。";
                    return text;
                }
                if (tower.UsesIceVulnerability)
                {
                    float vulnerabilityDuration = config.vulnerabilityDuration +
                        (tower.IsOnFrostTile ? config.frostDurationBonus : 0f);
                    string text = $"攻击会减速敌人，并使其脆弱 {vulnerabilityDuration:0.##} 秒。";
                    if (tower.IceAugment == RougeIceTowerAugment.VulnerabilityDamage)
                        text += $" 这些敌人还会额外受到 {config.vulnerabilityDamageBonus * 100f:0.#}% 伤害。";
                    else if (tower.IceAugment ==
                             RougeIceTowerAugment.VulnerabilityArmorPenetration)
                        text += " 攻击脆弱单位视为 +4 穿甲。";
                    return text + "\n脆弱：若敌人护甲大于 0，敌人护甲减半。\n" +
                           GetArmorRuleDescription();
                }
                float slowDuration = config.slowDuration +
                    (tower.IsOnFrostTile ? config.frostDurationBonus : 0f);
                return $"攻击范围内所有敌人，并使其减速 {config.slowPercent:0.#}% / {slowDuration:0.##} 秒。";
            }
            case RougeTowerType.MachineGun:
            {
                RougeMachineGunSpecializationConfig config =
                    TowerDefenseVisuals.GetMachineGunSpecializationConfig();
                if (tower.UsesMachineGunCritical)
                {
                    float chance = tower.HasUpgradedCriticalChance
                        ? config.upgradedCriticalChance
                        : config.criticalChance;
                    if (tower.HasCriticalArmorPenetration)
                        return $"攻击有 {chance * 100f:0.#}% 概率暴击，造成 {config.criticalDamageMultiplier:0.##} 倍伤害，并获得 {config.criticalArmorPenetration:0.#} 穿甲。\n" +
                               GetArmorRuleDescription();
                    return $"攻击有 {chance * 100f:0.#}% 概率暴击，造成 {config.criticalDamageMultiplier:0.##} 倍伤害。";
                }
                if (tower.UsesMachineGunFragments)
                {
                    if (!tower.UsesEmbeddedFragments)
                        return $"弹幕击杀敌人后有 {config.fragmentTriggerChance * 100f:0.#}% 概率向四周造成多段伤害，每段为原攻击的 {config.fragmentDamageMultiplier * 100f:0.#}%。";
                    return $"弹幕命中敌人时有 {config.embeddedFragmentChance * 100f:0.#}% 概率嵌入 1 枚破片；敌人死亡后，所有已嵌入破片向四周射出。每枚造成原攻击 {config.embeddedFragmentDamageMultiplier * 100f:0.#}% 伤害，多枚取最高伤害；射出的破片不会再次嵌入。";
                }
                return "快速发射多枚弹幕，适合清理密集敌人。";
            }
            case RougeTowerType.Cannon:
            {
                RougeCannonSpecializationConfig config =
                    TowerDefenseVisuals.GetCannonSpecializationConfig();
                if (tower.UsesCannonInnerBlast)
                {
                    if (tower.HasUpgradedCannonInnerBlast)
                        return $"爆炸范围提高 {(config.upgradedAoeRadiusMultiplier - 1f) * 100f:0.#}%；内圈为半径的 {config.upgradedInnerRadiusMultiplier * 100f:0.#}%，其中的敌人受到 {config.upgradedInnerDamageMultiplier:0.##} 倍伤害。";
                    string text = $"爆炸内圈为半径的 {config.innerRadiusMultiplier * 100f:0.#}%，其中的敌人受到 {config.innerDamageMultiplier:0.##} 倍伤害。";
                    if (tower.HasCannonSecondaryBombardment)
                        text += $" 落地后有 {config.secondaryTriggerChance * 100f:0.#}% 概率向外抛出 {config.secondaryProjectileCount} 枚小炮弹；水平飞出约主爆炸半径的 {config.secondaryTravelDistanceMultiplier * 100f:0.#}%，飞行 {config.secondaryFlightDuration:0.##} 秒后爆炸，伤害为主炮的 {config.secondaryDamageMultiplier * 100f:0.#}%，范围为 {config.secondaryRadiusMultiplier * 100f:0.#}%。";
                    return text;
                }
                if (tower.UsesPersistentCannonShell)
                {
                    int ticks = config.persistentTickCount +
                                (tower.HasUpgradedPersistentCannonTicks
                                    ? config.upgradedPersistentExtraTicks
                                    : 0);
                    float tickDamage = tower.HasUpgradedPersistentCannonTicks
                        ? config.upgradedPersistentDamageMultiplier
                        : config.persistentTickDamageMultiplier;
                    string text = $"炮弹落地造成 {config.persistentLandingDamageMultiplier * 100f:0.#}% 伤害并留在地上，之后每 {config.persistentTickInterval:0.##} 秒爆炸一次，共 {ticks} 次；每次造成 {tickDamage * 100f:0.#}% 伤害。";
                    if (tower.HasPersistentCannonKnockback)
                        text += " 每次爆炸还会轻微推开敌人。";
                    return text;
                }
                return "发射会爆炸的炮弹，对落点附近的敌人造成伤害。";
            }
            case RougeTowerType.Flame:
            {
                RougeFlameTowerSpecializationConfig config =
                    TowerDefenseVisuals.GetFlameSpecializationConfig();
                if (tower.UsesRotatingFlamethrower)
                    return $"以 {config.rotatingDegreesPerSecond:0.#}°/秒持续旋转喷火；基础伤害 {config.rotatingDamage:0.##}，基础间隔 {config.rotatingAttackInterval:0.##} 秒。旋转方向不受索敌模式改变。";
                if (tower.UsesFanFlamethrower)
                    return $"多支喷火器以目标方向为中心扇形展开，相邻喷口间隔为喷火角度 + {config.fanSpacingPaddingDegrees:0.#}°。集中模式会合并为一个大喷口：每个弹幕 +{config.focusedAnglePerProjectile:0.#}°、+{config.focusedDamageBonusPerProjectile * 100f:0.#}% 伤害，并保留集中模式减益。";
                if (tower.UsesFlamethrower)
                    return $"改为喷火器：基础伤害 {config.flamethrowerDamage:0.##}，基础间隔 {config.flamethrowerAttackInterval:0.##} 秒，{config.flamethrowerAngle:0.#}° 扇形，基础范围 {config.flamethrowerRange:0.#}；多弹幕围绕目标方向作 360° 散射。";
                if (tower.UsesStackingBurn)
                    return $"火区施加 {config.burnDuration:0.##} 秒燃烧，最多 {config.maximumBurnStacks} 层；每层提高 {config.damageBonusPerStack * 100f:0.#}% 跳伤，跳伤速度提高 {config.burnSpeedBonus * 100f:0.#}%。燃烧每跳造成塔伤害的 {config.burnDamageMultiplier * 100f:0.#}%。";
                if (tower.UsesConflagration)
                    return $"火区施加燃烧；命中被冻结的敌人时立即清除冻结与燃烧，并爆燃造成塔伤害的 {config.conflagrationDamageMultiplier * 100f:0.#}%。";
                if (tower.AppliesTowerBurn)
                    return $"经过火区的敌人燃烧 {config.burnDuration:0.##} 秒，每 {config.burnTickInterval:0.##} 秒受到塔伤害的 {config.burnDamageMultiplier * 100f:0.#}%；重复施加只刷新时间并保留最高伤害。攻速 Buff 仅生效 {config.attackSpeedBuffEffectiveness * 100f:0.#}%。";
                return "投出火球并留下燃烧区域，持续伤害其中的敌人。";
            }
            case RougeTowerType.Laser:
            {
                RougeLaserTowerSpecializationConfig config =
                    TowerDefenseVisuals.GetLaserSpecializationConfig();
                if (tower.UsesLaserArmorBreak)
                {
                    float multiplier = tower.HasAcceleratedLaserArmorBreak
                        ? config.acceleratedArmorBreakDurationMultiplier
                        : 1f;
                    string text = $"持续照射同一目标 {config.armorBreakNormalDuration * multiplier:0.##} 秒，永久削减 1 点护甲；精英需要 {config.armorBreakEliteDuration * multiplier:0.##} 秒，Boss 需要 {config.armorBreakBossDuration * multiplier:0.##} 秒。";
                    if (tower.IgnoresFocusedLaserPenalty)
                        text += " 集中模式不再降低自身伤害与攻速。";
                    return text + "\n" + GetArmorRuleDescription();
                }
                if (tower.UsesLaserRefractionAttack)
                    return $"基础伤害提高至 {config.refractionAttackDamageMultiplier:0.##} 倍，基础攻击间隔变为 {config.refractionAttackInterval:0.##} 秒。激光先连接一个目标，再同时折射至最多弹幕数 × {config.refractionAttackTargetMultiplier} 个敌人；每多命中一个敌人，伤害降低 {config.refractionAttackDamageFalloffPerTarget * 100f:0.#}%，最多降低 {config.refractionAttackMaximumDamageFalloff * 100f:0.#}%。折射范围为攻击范围的四分之一。";
                if (tower.UsesContinuousLaserRefraction)
                    return "每条直连激光最多连续折射 3 次，依次造成 75%、50%、25% 伤害。折射范围为攻击范围的四分之一。";
                if (tower.UsesLaserRefraction)
                    return $"激光命中敌人时，折射到附近一个未被激光直接连接的单位，造成 {config.refractionDamageMultiplier * 100f:0.#}% 伤害。折射范围为攻击范围的四分之一。";
                return "持续连接多个敌人；集中模式会把火力集中到首领。";
            }
            case RougeTowerType.PiercingLaser:
                return "发射直线激光，伤害路径上的所有敌人。";
            case RougeTowerType.OrbitSphere:
                return "水晶沿范围边缘移动，并持续攻击附近敌人。";
            case RougeTowerType.RocketBarrage:
                return "一次发射多枚火箭，对多个落点造成爆炸伤害。";
            default:
                return string.Empty;
        }
    }

    private static string GetUpgradeChoiceButtonText(RougeDefenseTower tower,
        int choiceIndex)
    {
        if (tower == null) return "升级分支";
        string price = tower.UpgradeCost > 0 ? $"{tower.UpgradeCost} 金币" : "免费选择";
        if (tower.TowerType == RougeTowerType.Flame)
        {
            if (tower.NeedsFlameBranchChoice)
                return choiceIndex == 0
                    ? $"A 喷火器\n{price}"
                    : $"B 燃烧\n{price}";
            if (tower.FlameBranch == RougeFlameTowerBranch.Flamethrower)
                return choiceIndex == 0
                    ? $"A1 旋转喷火器\n{price}"
                    : $"A2 扇形喷火器\n{price}";
            return choiceIndex == 0
                ? $"B1 叠层燃烧\n{price}"
                : $"B2 爆燃\n{price}";
        }
        if (tower.TowerType == RougeTowerType.Laser)
        {
            if (tower.NeedsLaserBranchChoice)
            {
                return choiceIndex == 0
                    ? $"A 破甲\n{price}"
                    : $"B 折射\n{price}";
            }
            if (tower.LaserBranch == RougeLaserTowerBranch.ArmorBreak)
            {
                return choiceIndex == 0
                    ? $"A1 加速穿甲\n{price}"
                    : $"A2 强力集中\n{price}";
            }
            return choiceIndex == 0
                ? $"B1 连续折射\n{price}"
                : $"B2 折射攻击\n{price}";
        }
        if (tower.TowerType == RougeTowerType.MachineGun)
        {
            if (tower.NeedsMachineGunBranchChoice)
            {
                return choiceIndex == 0
                    ? $"A 暴击\n{price}"
                    : $"B 多段伤害\n{price}";
            }
            if (tower.MachineGunBranch == RougeMachineGunBranch.Critical)
            {
                return choiceIndex == 0
                    ? $"A1 暴击率 50%\n{price}"
                    : $"A2 暴击穿甲 +4\n{price}";
            }
            return choiceIndex == 0
                ? $"B1 强化多段伤害\n{price}"
                : $"B2 嵌入破片\n{price}";
        }
        if (tower.TowerType == RougeTowerType.Cannon)
        {
            if (tower.NeedsCannonBranchChoice)
            {
                return choiceIndex == 0
                    ? $"A 内圈爆破\n{price}"
                    : $"B 持续炮弹\n{price}";
            }
            if (tower.CannonBranch == RougeCannonBranch.InnerBlast)
            {
                return choiceIndex == 0
                    ? $"A1 强化内圈\n{price}"
                    : $"A2 小炮弹\n{price}";
            }
            return choiceIndex == 0
                ? $"B1 轻微击退\n{price}"
                : $"B2 7 次 · 25%\n{price}";
        }
        if (tower.NeedsIceBranchChoice)
        {
            return choiceIndex == 0
                ? $"冻结路线\n{price}"
                : $"脆弱路线\n{price}";
        }

        if (tower.IceBranch == RougeIceTowerBranch.Freeze)
        {
            return choiceIndex == 0
                ? $"冰地刺\n{price}"
                : $"制造霜寒格\n{price}";
        }

        return choiceIndex == 0
            ? $"额外受伤\n{price}"
            : $"攻击脆弱单位 +4 穿甲\n{price}";
    }

    private static string GetArmorRuleDescription()
    {
        return "护甲：每 1 点护甲使受到的伤害先 -1，再减少 5%。敌人护甲范围为 -20 至 15 点，穿甲可额外叠加。";
    }

    private void CreateChargeTowerBuildButton(Transform parent, float x, float y)
    {
        _chargeTowerBuildButton = CreateUiButton("Charge Tower", parent,
            GetChargeTowerBuildLabel(), new Color(0.06f, 0.7f, 0.62f, 1f));
        RectTransform rect = _chargeTowerBuildButton.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(132f, 56f);
        _chargeTowerBuildButtonText = _chargeTowerBuildButton.GetComponentInChildren<Text>();
        if (_chargeTowerBuildButtonText != null)
        {
            _chargeTowerBuildButtonText.fontSize = 14;
            _chargeTowerBuildButtonText.alignment = TextAnchor.MiddleLeft;
            StretchRect(_chargeTowerBuildButtonText.rectTransform, 44f, 3f, 4f, 3f);
        }
        Color accent = new Color(0.08f, 0.92f, 0.78f, 1f);
        StyleCommandButton(_chargeTowerBuildButton, accent);
        CreateBuildButtonGlyph(_chargeTowerBuildButton.transform,
            GetTowerBuildGlyph(RougeTowerType.ChargeTower), accent);
        _chargeTowerBuildButton.onClick.AddListener(BeginChargeTowerBuild);
    }

    private void CreateReinforcementTowerBuildButton(Transform parent, float x, float y)
    {
        _reinforcementTowerBuildButton = CreateUiButton("Reinforcement Tower", parent,
            GetReinforcementTowerBuildLabel(), new Color(0.88f, 0.55f, 0.08f, 1f));
        RectTransform rect = _reinforcementTowerBuildButton.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(132f, 56f);
        _reinforcementTowerBuildButtonText =
            _reinforcementTowerBuildButton.GetComponentInChildren<Text>();
        if (_reinforcementTowerBuildButtonText != null)
        {
            _reinforcementTowerBuildButtonText.fontSize = 14;
            _reinforcementTowerBuildButtonText.alignment = TextAnchor.MiddleLeft;
            StretchRect(_reinforcementTowerBuildButtonText.rectTransform, 44f, 3f, 4f, 3f);
        }
        Color accent = new Color(1f, 0.62f, 0.10f, 1f);
        StyleCommandButton(_reinforcementTowerBuildButton, accent);
        CreateBuildButtonGlyph(_reinforcementTowerBuildButton.transform,
            GetTowerBuildGlyph(RougeTowerType.ReinforcementTower), accent);
        _reinforcementTowerBuildButton.onClick.AddListener(BeginReinforcementTowerBuild);
    }

    private void BuildChargeTowerEffectSelectionUi(Transform canvas)
    {
        _chargeTowerEffectSelectionPanel = CreateUiPanel("Charge Tower Effect Selection", canvas,
            new Color(0.005f, 0.012f, 0.02f, 0.82f));
        StretchRect(_chargeTowerEffectSelectionPanel.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        _chargeTowerEffectSelectionPanel.GetComponent<Image>().raycastTarget = true;

        GameObject content = CreateUiPanel("Selection Content", _chargeTowerEffectSelectionPanel.transform,
            new Color(0.025f, 0.055f, 0.075f, 0.98f));
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(940f, 560f);
        content.GetComponent<Image>().raycastTarget = true;
        AddHudPanelChrome(content, new Color(0.08f, 0.92f, 0.78f, 1f));

        Text title = CreateUiText("Title", content.transform, 30, TextAnchor.MiddleCenter);
        SetBottomRect(title.rectTransform, 0f, 490f, 880f, 46f);
        title.text = "充能塔：为周围8格内指定地图格选择效果（三选一）";
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.56f, 1f, 0.88f, 1f);

        _chargeTowerEffectSelectionSummary = CreateUiText("Summary", content.transform, 18,
            TextAnchor.MiddleCenter);
        SetBottomRect(_chargeTowerEffectSelectionSummary.rectTransform, 0f, 438f, 880f, 48f);
        _chargeTowerEffectSelectionSummary.color = new Color(0.76f, 0.88f, 0.94f, 1f);

        for (int i = 0; i < _chargeTowerEffectChoiceButtons.Length; i++)
        {
            int capturedIndex = i;
            Button button = CreateUiButton("Effect Choice " + (i + 1), content.transform,
                string.Empty, new Color(0.12f, 0.32f, 0.38f, 1f));
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2((i - 1) * 292f, 176f);
            rect.sizeDelta = new Vector2(276f, 224f);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 18;
                label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
            }
            button.onClick.AddListener(() => ConfirmChargeTowerEffect(capturedIndex));
            _chargeTowerEffectChoiceButtons[i] = button;
            _chargeTowerEffectChoiceTexts[i] = label;
        }

        _chargeTowerRefreshButton = CreateUiButton("Refresh Choices", content.transform,
            "刷新", new Color(0.12f, 0.48f, 0.72f, 1f));
        RectTransform refreshRect = _chargeTowerRefreshButton.GetComponent<RectTransform>();
        refreshRect.anchorMin = new Vector2(0.5f, 0f);
        refreshRect.anchorMax = new Vector2(0.5f, 0f);
        refreshRect.pivot = new Vector2(0.5f, 0f);
        refreshRect.anchoredPosition = new Vector2(-150f, 62f);
        refreshRect.sizeDelta = new Vector2(270f, 62f);
        _chargeTowerRefreshButtonText = _chargeTowerRefreshButton.GetComponentInChildren<Text>();
        _chargeTowerRefreshButton.onClick.AddListener(RefreshChargeTowerEffectChoices);

        Button cancel = CreateUiButton("Cancel Charge Tower", content.transform,
            "取消建造（返还托管金币）", new Color(0.68f, 0.12f, 0.16f, 1f));
        RectTransform cancelRect = cancel.GetComponent<RectTransform>();
        cancelRect.anchorMin = new Vector2(0.5f, 0f);
        cancelRect.anchorMax = new Vector2(0.5f, 0f);
        cancelRect.pivot = new Vector2(0.5f, 0f);
        cancelRect.anchoredPosition = new Vector2(150f, 62f);
        cancelRect.sizeDelta = new Vector2(270f, 62f);
        cancel.onClick.AddListener(CancelPendingChargeTowerConstruction);
        _chargeTowerEffectSelectionPanel.SetActive(false);
    }

    private void RefreshChargeTowerEffectSelectionUi()
    {
        if (_chargeTowerEffectSelectionPanel == null) return;
        _chargeTowerEffectSelectionPanel.SetActive(_chargeTowerEffectSelectionActive);
        if (!_chargeTowerEffectSelectionActive) return;

        if (_chargeTowerEffectSelectionSummary != null)
        {
            _chargeTowerEffectSelectionSummary.text =
                $"地块 [{_pendingChargeTowerCell.x}, {_pendingChargeTowerCell.y}]  |  " +
                $"已托管 {_pendingChargeTowerEscrow} 金币  |  当前可用 {_towerDefenseGold} 金币\n" +
                "取消只返还托管的建造费；已支付的刷新费不返还";
        }
        for (int i = 0; i < _chargeTowerEffectChoices.Length; i++)
        {
            RougeTowerPlaceEffect effect = _chargeTowerEffectChoices[i];
            if (_chargeTowerEffectChoiceTexts[i] != null)
            {
                _chargeTowerEffectChoiceTexts[i].text =
                    RougeTowerPlaceEffectRules.GetDisplayName(effect) + "\n\n" +
                    GetTowerPlaceEffectDescription(effect, null);
            }
            if (_chargeTowerEffectChoiceButtons[i] != null)
            {
                Color color = RougeTowerPlaceEffectRules.GetVisualColor(effect);
                color.a = 1f;
                _chargeTowerEffectChoiceButtons[i].image.color = Color.Lerp(color,
                    new Color(0.035f, 0.08f, 0.11f, 1f), 0.42f);
            }
        }

        int refreshCost = GetChargeTowerRefreshGoldCost(_chargeTowerRefreshCount);
        SetPurchaseButtonAvailability(_chargeTowerRefreshButton, _chargeTowerRefreshButtonText,
            _towerDefenseGold >= refreshCost);
        if (_chargeTowerRefreshButtonText != null)
            _chargeTowerRefreshButtonText.text = refreshCost == 0
                ? "刷新三项（本次免费）"
                : $"刷新三项（{refreshCost} 金币）";
    }

    private string GetTowerBuildModeText()
    {
        if (_chargeTowerTargetSelectionActive)
        {
            string state = _pendingChargeTowerTargetValid
                ? $"目标 [{_pendingChargeTowerCell.x}, {_pendingChargeTowerCell.y}] 可改变"
                : "请选择周围8格内的塔楼地图格（已被其他充能塔改变的格子不可重复指定）";
            return $"充能塔：指定周围8格内目标地图格  |  {state}  |  左键确认 / Esc 取消";
        }
        string towerName = _chargeTowerBuildSelectionActive
            ? "充能塔"
            : _reinforcementTowerBuildSelectionActive
                ? "强化塔"
                : TowerDefenseVisuals.GetTowerName(_selectedBuildType);
        if (_towerPreview == null || !_towerPreview.gameObject.activeInHierarchy)
            return $"建造：{towerName}  |  将指针移到可建造地格";
        if (_previewValid)
        {
            return $"建造：{towerName}  |  可建造  |  左键放置";
        }
        string reason;
        if (_towerDefenseGold < _towerPreview.PlacementCost)
            reason = "金币不足";
        else
            reason = "位置被占用或超出区域";
        return $"建造：{towerName}  |  <color=#FF665E><b>不可建造：{reason}</b></color>";
    }

    private string GetChargeTowerBuildLabel()
    {
        int cost = _towerPreview != null && _towerPreview.IsChargeTower &&
                   _towerPreview.gameObject.activeInHierarchy
            ? _towerPreview.PlacementCost
            : GetChargeTowerGoldCost();
        return $"[C] 充能塔\n{cost} 金币";
    }

    private string GetReinforcementTowerBuildLabel()
    {
        int cost = _towerPreview != null && _towerPreview.IsReinforcementTower &&
                   _towerPreview.gameObject.activeInHierarchy
            ? _towerPreview.PlacementCost
            : GetReinforcementTowerGoldCost();
        return $"[V] 强化塔\n{cost} 金币";
    }

    private static GameObject CreateUiPanel(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return go;
    }

    private static GameObject CreateCommandDockSection(string name, Transform parent,
        float anchorMinX, float anchorMaxX)
    {
        GameObject section = CreateUiPanel(name, parent,
            new Color(0.006f, 0.018f, 0.028f, 0.94f));
        RectTransform rect = section.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(anchorMinX, 0f);
        rect.anchorMax = new Vector2(anchorMaxX, 1f);
        rect.offsetMin = new Vector2(6f, 8f);
        rect.offsetMax = new Vector2(-6f, -8f);
        Outline outline = section.AddComponent<Outline>();
        outline.effectColor = new Color(0.10f, 0.58f, 0.78f, 0.30f);
        outline.effectDistance = new Vector2(1f, -1f);
        return section;
    }

    private static void StyleCommandButton(Button button, Color accent)
    {
        if (button == null) return;
        Color background = Color.Lerp(new Color(0.012f, 0.028f, 0.044f, 1f),
            accent, 0.16f);
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = background;
        ColorBlock colors = button.colors;
        colors.normalColor = background;
        colors.highlightedColor = Color.Lerp(background, accent, 0.30f);
        colors.pressedColor = Color.Lerp(background, Color.black, 0.24f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.035f, 0.050f, 0.062f, 0.72f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.62f);
            outline.effectDistance = new Vector2(1.25f, -1.25f);
        }
        Image accentLine = CreateUiImage("Button Accent", button.transform, accent);
        RectTransform accentRect = accentLine.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(1f, 0f);
        accentRect.pivot = new Vector2(0.5f, 0f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(-4f, 2f);
    }

    private static void CreateBuildButtonGlyph(Transform parent, string glyph, Color accent)
    {
        GameObject plate = CreateUiPanel("Glyph Plate", parent,
            Color.Lerp(new Color(0.012f, 0.028f, 0.044f, 0.98f), accent, 0.18f));
        RectTransform plateRect = plate.GetComponent<RectTransform>();
        plateRect.anchorMin = new Vector2(0f, 0.5f);
        plateRect.anchorMax = new Vector2(0f, 0.5f);
        plateRect.pivot = new Vector2(0f, 0.5f);
        plateRect.anchoredPosition = new Vector2(5f, 0f);
        plateRect.sizeDelta = new Vector2(34f, 44f);
        Outline outline = plate.AddComponent<Outline>();
        outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.62f);
        outline.effectDistance = new Vector2(1f, -1f);
        Text icon = CreateUiText("Glyph", plate.transform, 25, TextAnchor.MiddleCenter);
        icon.text = glyph;
        icon.color = Color.Lerp(accent, Color.white, 0.16f);
        icon.fontStyle = FontStyle.Bold;
        StretchRect(icon.rectTransform, 1f, 1f, 1f, 1f);
    }

    private static void AddHudPanelChrome(GameObject panel, Color accentColor)
    {
        if (panel == null) return;
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.32f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;

        Image accent = CreateUiImage("Accent", panel.transform, accentColor);
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, 4f);
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
        text.font = GetTowerDefenseHudFont();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(1f, -1f);
        shadow.useGraphicAlpha = true;
        return text;
    }

    private static Font GetTowerDefenseHudFont()
    {
        if (s_towerDefenseHudFont != null) return s_towerDefenseHudFont;
        s_towerDefenseHudFont = Font.CreateDynamicFontFromOSFont(new[]
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "PingFang SC",
            "Noto Sans CJK SC",
            "Arial"
        }, 22);
        if (s_towerDefenseHudFont == null)
            s_towerDefenseHudFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return s_towerDefenseHudFont;
    }

    private static void PrewarmTowerDefenseCameraUiGlyphs()
    {
        Font font = GetTowerDefenseHudFont();
        if (font == null) return;

        const string glyphs =
            "默认镜头自由移轴观赏垂直俯视平滚轮升降按住右键旋转加速中左拖动调整视距" +
            "点击塔楼选择空地显示主塔血条退出设置充能效果卡片确认取消目标有效地图格" +
            "隐藏全范围建造搬运标准强化放置满级索敌开始光影游戏速度伤害统计" +
            "WASDShiftEscF1234506789CVUR/[]·：+-";
        font.RequestCharactersInTexture(glyphs, 14, FontStyle.Normal);
        font.RequestCharactersInTexture(glyphs, 27, FontStyle.Bold);
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
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.72f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        Text text = CreateUiText("Label", go.transform, 17, TextAnchor.MiddleCenter);
        text.fontStyle = FontStyle.Bold;
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

    private static void SetBottomLeftRect(RectTransform rect, float x, float y,
        float width, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetTopStretchRect(RectTransform rect, float left, float top,
        float right, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
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

    // Rocket-barrage missiles are data only. No per-missile GameObject, Transform,
    // MonoBehaviour, Instantiate, or Destroy work is performed by this system.
    private const int MaxActiveRocketBarrageMissiles = 8192;
    private const int MaxRocketBarrageShotsPerSalvo = 512;
    private const int MaxRocketBarrageCatchUpShots = 8;

    private struct ActiveRocketBarrageSalvo
    {
        public RougeDefenseTower Tower;
        public int ShotsRemaining;
        public float ShotTimer;
        public uint RandomState;
    }

    private struct ActiveRocketBarrageMissile
    {
        public Vector3 Start;
        public Vector3 End;
        public Vector3 Position;
        public Vector3 PreviousPosition;
        public float2 BrownianOffset;
        public float2 BrownianVelocity;
        public float Elapsed;
        public float Duration;
        public float ArcHeight;
        public float Damage;
        public float Radius;
        public float BrownianStrength;
        public int KillGoldBonus;
        public int WealthCellIndexPlusOne;
        public int TileEffect;
        public uint RandomState;
    }

    private readonly List<ActiveRocketBarrageSalvo> _activeRocketBarrageSalvos =
        new List<ActiveRocketBarrageSalvo>(128);
    private readonly List<ActiveRocketBarrageMissile> _activeRocketBarrageMissiles =
        new List<ActiveRocketBarrageMissile>(MaxActiveRocketBarrageMissiles);
    private readonly Matrix4x4[] _rocketBarrageRenderMatrices = new Matrix4x4[1023];
    private Mesh _rocketBarrageMissileMesh;
    private Material _rocketBarrageMissileMaterial;

    private void StartRocketBarrage(RougeDefenseTower tower)
    {
        if (tower == null) return;
        for (int i = 0; i < _activeRocketBarrageSalvos.Count; i++)
        {
            if (_activeRocketBarrageSalvos[i].Tower == tower) return;
        }

        uint seed = (uint)tower.GetInstanceID() * 747796405u +
                    (uint)Mathf.Max(1, _towerLaserDamageFrame) * 2891336453u;
        if (seed == 0u) seed = 0x9E3779B9u;
        _activeRocketBarrageSalvos.Add(new ActiveRocketBarrageSalvo
        {
            Tower = tower,
            ShotsRemaining = Mathf.Clamp(tower.AttackProjectileCount, 1,
                MaxRocketBarrageShotsPerSalvo),
            ShotTimer = 0f,
            RandomState = seed
        });
        tower.PlayAttackSound();
    }

    private void UpdateRocketBarrageSystem(float dt)
    {
        UpdateRocketBarrageSalvos(dt);
        UpdateRocketBarrageMissiles(dt);
    }

    private void UpdateRocketBarrageSalvos(float dt)
    {
        for (int i = _activeRocketBarrageSalvos.Count - 1; i >= 0; i--)
        {
            ActiveRocketBarrageSalvo salvo = _activeRocketBarrageSalvos[i];
            RougeDefenseTower tower = salvo.Tower;
            if (tower == null)
            {
                RemoveRocketBarrageSalvoAtSwapBack(i);
                continue;
            }

            salvo.ShotTimer -= dt * tower.AttackSpeedMultiplier;
            int catchUpShots = 0;
            while (salvo.ShotTimer <= 0f && salvo.ShotsRemaining > 0 &&
                   catchUpShots < MaxRocketBarrageCatchUpShots)
            {
                tower.PlayAttackAnimation(null);
                SpawnRocketBarrageMissile(tower, ref salvo.RandomState);
                salvo.ShotsRemaining--;
                salvo.ShotTimer += Mathf.Max(0.01f, tower.ProjectileInterval);
                catchUpShots++;
            }

            if (salvo.ShotsRemaining <= 0)
            {
                RemoveRocketBarrageSalvoAtSwapBack(i);
                continue;
            }
            _activeRocketBarrageSalvos[i] = salvo;
        }
    }

    private void SpawnRocketBarrageMissile(RougeDefenseTower tower, ref uint randomState)
    {
        if (_activeRocketBarrageMissiles.Count >= MaxActiveRocketBarrageMissiles) return;

        Vector3 towerPosition = tower.transform.position;
        float2 landingOffset = NextPointInsideUnitCircle(ref randomState) * tower.AttackRange;
        Vector3 end = new Vector3(
            Mathf.Clamp(towerPosition.x + landingOffset.x, -arenaHalfExtent, arenaHalfExtent),
            renderHeight + 0.2f,
            Mathf.Clamp(towerPosition.z + landingOffset.y, -arenaHalfExtent, arenaHalfExtent));
        Vector3 start = GetTowerMuzzlePosition(tower);
        float distance = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(end.x, end.z));
        Vector3 firstDirection = end - start;
        if (firstDirection.sqrMagnitude < 0.001f) firstDirection = Vector3.up;
        Vector3 initialPosition = start + firstDirection.normalized * 0.05f;

        _activeRocketBarrageMissiles.Add(new ActiveRocketBarrageMissile
        {
            Start = start,
            End = end,
            Position = initialPosition,
            PreviousPosition = start,
            BrownianOffset = float2.zero,
            BrownianVelocity = float2.zero,
            Elapsed = 0f,
            Duration = Mathf.Max(0.05f, tower.ProjectileFlightDuration),
            ArcHeight = Mathf.Max(8f, distance * 0.55f),
            Damage = tower.Damage,
            Radius = Mathf.Max(0.1f, tower.AoeRadius),
            BrownianStrength = Mathf.Max(0f, tower.BrownianStrength),
            KillGoldBonus = tower.KillGoldPercentBonus,
            WealthCellIndexPlusOne = GetTowerWealthCellIndexPlusOne(tower),
            TileEffect = (int)tower.TowerPlaceEffect,
            RandomState = NextRocketRandom(ref randomState)
        });
    }

    private void UpdateRocketBarrageMissiles(float dt)
    {
        float safeDt = Mathf.Max(0f, dt);
        float sqrtDt = Mathf.Sqrt(safeDt);
        float velocityDamping = Mathf.Exp(-3.25f * safeDt);
        for (int i = _activeRocketBarrageMissiles.Count - 1; i >= 0; i--)
        {
            ActiveRocketBarrageMissile missile = _activeRocketBarrageMissiles[i];
            missile.Elapsed += safeDt;
            float progress = Mathf.Clamp01(missile.Elapsed / missile.Duration);
            missile.PreviousPosition = missile.Position;

            // Damped, velocity-integrated Brownian forcing avoids harsh teleporting while
            // retaining a random walk. The envelope preserves both trajectory endpoints.
            float2 randomDirection = NextUnitDirection(ref missile.RandomState);
            missile.BrownianVelocity = missile.BrownianVelocity * velocityDamping +
                randomDirection * (missile.BrownianStrength * sqrtDt);
            missile.BrownianOffset += missile.BrownianVelocity * safeDt;
            float driftEnvelope = Mathf.Sin(progress * Mathf.PI);

            Vector3 position = Vector3.LerpUnclamped(missile.Start, missile.End, progress);
            position.y += Mathf.Sin(progress * Mathf.PI) * missile.ArcHeight;
            position.x += missile.BrownianOffset.x * driftEnvelope;
            position.z += missile.BrownianOffset.y * driftEnvelope;
            missile.Position = position;

            if (progress < 1f)
            {
                _activeRocketBarrageMissiles[i] = missile;
                continue;
            }

            ResolveRocketBarrageImpact(missile);
            RemoveRocketBarrageMissileAtSwapBack(i);
        }
    }

    private void ResolveRocketBarrageImpact(ActiveRocketBarrageMissile missile)
    {
        float2 impact = new float2(missile.End.x, missile.End.z);
        TryAddTowerDirectDamageArea(new RougeSkillArea
        {
            Type = 13,
            Position = impact,
            Radius = missile.Radius,
            Damage = missile.Damage,
            SourceTowerTypePlusOne = (int)RougeTowerType.RocketBarrage + 1,
            SourceTowerTileEffect = missile.TileEffect,
            SourceTowerKillGoldBonus = missile.KillGoldBonus,
            SourceTowerWealthCellIndexPlusOne = missile.WealthCellIndexPlusOne
        }, TowerFrostAreaSlowMultiplier);
        SpawnExplosionVFX(missile.End + Vector3.up * 0.35f,
            Mathf.Max(1.25f, missile.Radius * 0.72f));
        SpawnAOERing(missile.End, missile.Radius, 0.26f,
            new Color(1f, 0.3f, 0.035f, 1f));
    }

    private void RenderRocketBarrageMissiles()
    {
        int count = _activeRocketBarrageMissiles.Count;
        if (count <= 0 || !EnsureRocketBarrageRenderResources()) return;

        Camera camera = RougeCameraFollow.ResolveCamera();
        Quaternion facing = camera != null
            ? Quaternion.LookRotation(-camera.transform.forward, camera.transform.up)
            : Quaternion.Euler(90f, 0f, 0f);
        Quaternion inverseFacing = Quaternion.Inverse(facing);

        for (int startIndex = 0; startIndex < count; startIndex += _rocketBarrageRenderMatrices.Length)
        {
            int batchCount = Mathf.Min(_rocketBarrageRenderMatrices.Length, count - startIndex);
            for (int i = 0; i < batchCount; i++)
            {
                ActiveRocketBarrageMissile missile = _activeRocketBarrageMissiles[startIndex + i];
                Vector3 worldDirection = missile.Position - missile.PreviousPosition;
                if (worldDirection.sqrMagnitude < 0.000001f) worldDirection = missile.End - missile.Start;
                Vector3 localDirection = inverseFacing * worldDirection;
                float angle = Mathf.Atan2(-localDirection.x, localDirection.y) * Mathf.Rad2Deg;
                Quaternion rotation = facing * Quaternion.Euler(0f, 0f, angle);
                _rocketBarrageRenderMatrices[i] = Matrix4x4.TRS(
                    missile.Position, rotation, new Vector3(0.58f, 1.35f, 1f));
            }
            Graphics.DrawMeshInstanced(_rocketBarrageMissileMesh, 0,
                _rocketBarrageMissileMaterial, _rocketBarrageRenderMatrices, batchCount);
        }
    }

    private bool EnsureRocketBarrageRenderResources()
    {
        _rocketBarrageMissileMesh ??= _bulletMesh != null ? _bulletMesh : enemyMesh;
        if (_rocketBarrageMissileMesh == null) return false;
        if (_rocketBarrageMissileMaterial != null) return true;

        _rocketBarrageMissileMaterial = _bulletMaterial != null
            ? new Material(_bulletMaterial)
            : CreateRuntimeMaterial("Rouge/SpriteInstanced", "Rocket Barrage Missile", true);
        _rocketBarrageMissileMaterial.name = "Rocket Barrage Missile (Instanced)";
        _rocketBarrageMissileMaterial.hideFlags = HideFlags.DontSave;
        _rocketBarrageMissileMaterial.enableInstancing = true;
        Texture2D projectileTexture = Resources.Load<Texture2D>("Sprites/projectile_rocket_missile");
        if (projectileTexture != null) _rocketBarrageMissileMaterial.SetTexture("_MainTex", projectileTexture);
        ApplyBaseColor(_rocketBarrageMissileMaterial, Color.white);
        return true;
    }

    private void DisposeRocketBarrageSystem()
    {
        _activeRocketBarrageSalvos.Clear();
        _activeRocketBarrageMissiles.Clear();
        if (_rocketBarrageMissileMaterial != null) Destroy(_rocketBarrageMissileMaterial);
        _rocketBarrageMissileMaterial = null;
        _rocketBarrageMissileMesh = null;
    }

    private void RemoveRocketBarrageSalvoAtSwapBack(int index)
    {
        int last = _activeRocketBarrageSalvos.Count - 1;
        _activeRocketBarrageSalvos[index] = _activeRocketBarrageSalvos[last];
        _activeRocketBarrageSalvos.RemoveAt(last);
    }

    private void RemoveRocketBarrageMissileAtSwapBack(int index)
    {
        int last = _activeRocketBarrageMissiles.Count - 1;
        _activeRocketBarrageMissiles[index] = _activeRocketBarrageMissiles[last];
        _activeRocketBarrageMissiles.RemoveAt(last);
    }

    private static float2 NextPointInsideUnitCircle(ref uint state)
    {
        float angle = NextRocket01(ref state) * Mathf.PI * 2f;
        float radius = Mathf.Sqrt(NextRocket01(ref state));
        return new float2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private static float2 NextUnitDirection(ref uint state)
    {
        float angle = NextRocket01(ref state) * Mathf.PI * 2f;
        return new float2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static float NextRocket01(ref uint state)
    {
        return (NextRocketRandom(ref state) & 0x00FFFFFFu) * (1f / 16777216f);
    }

    private static uint NextRocketRandom(ref uint state)
    {
        if (state == 0u) state = 0x9E3779B9u;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}

[DisallowMultipleComponent]
internal sealed class RougeFloatingWorldText : MonoBehaviour
{
    private TextMesh _textMesh;
    private Color _baseColor;
    private float _duration;
    private float _elapsed;
    private float _riseDistance;
    private Vector3 _startPosition;

    public static RougeFloatingWorldText Create(string text, Vector3 position, Color color,
        Font font, float duration = 1.35f, float riseDistance = 2.2f)
    {
        GameObject instance = new GameObject("Floating World Text - " + text);
        instance.transform.position = position;
        TextMesh textMesh = instance.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.anchor = TextAnchor.LowerCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 64;
        textMesh.characterSize = 0.16f;
        textMesh.color = color;
        MeshRenderer renderer = textMesh.GetComponent<MeshRenderer>();
        if (font != null)
        {
            textMesh.font = font;
            if (renderer != null) renderer.sharedMaterial = font.material;
        }
        if (renderer != null) renderer.sortingOrder = 240;

        RougeFloatingWorldText floating = instance.AddComponent<RougeFloatingWorldText>();
        floating._textMesh = textMesh;
        floating._baseColor = color;
        floating._duration = Mathf.Max(0.1f, duration);
        floating._riseDistance = Mathf.Max(0f, riseDistance);
        floating._startPosition = position;
        return floating;
    }

    private void LateUpdate()
    {
        _elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_elapsed / _duration);
        float easedRise = 1f - (1f - progress) * (1f - progress);
        transform.position = _startPosition + Vector3.up * (_riseDistance * easedRise);

        Camera camera = RougeCameraFollow.ResolveCamera();
        if (camera != null)
        {
            transform.rotation = Quaternion.LookRotation(camera.transform.forward,
                camera.transform.up);
        }

        if (_textMesh != null)
        {
            float fade = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.55f, 1f, progress));
            Color color = _baseColor;
            color.a *= fade;
            _textMesh.color = color;
        }
        if (progress >= 1f) Destroy(gameObject);
    }
}
