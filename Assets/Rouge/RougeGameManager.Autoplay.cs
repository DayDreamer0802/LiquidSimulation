using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Jobs;
using Unity.Mathematics;

public partial class RougeGameManager
{
    private const float TowerDefenseAutoplayTickSeconds = 0.25f;
    private const float TowerDefenseAutoplayMaximumPlanAgeSeconds = 0.75f;
    private const int TowerDefenseAutoplayEnemyAnalysisBatchSize = 128;
    private const int TowerDefenseAutoplaySpatialScoreBatchSize = 32;
    private const int TowerDefenseAutoplayPressureProjectionCells = 4;
    private const int TowerDefenseAutoplayThoughtCapacity = 22;
    private const float TowerDefenseAutoplayAmbientLogInterval = 3f;
    private const float TowerDefenseAutoplaySupportObservationSeconds = 18f;
    private const float TowerDefenseAutoplaySupportLeaderHoldSeconds = 12f;
    private const float TowerDefenseAutoplaySupportDamageRateHorizonSeconds = 10f;
    private const float TowerDefenseAutoplaySupportLeaderRetentionRatio = 0.88f;
    private const float TowerDefenseAutoplaySupportGoldAttributionConfidence = 0.35f;
    private const float TowerDefenseAutoplaySupportGoldDamageEquivalent = 4f;
    private const float TowerDefenseAutoplayNearBaseOuterCrisisCells = 8f;
    private const float TowerDefenseAutoplayImmediateCoreDefenseCells = 2f;
    private const float TowerDefenseAutoplayCoreLiquidationMinimumAgeSeconds = 8f;
    private const float TowerDefenseAutoplayCoreLiquidationCooldownSeconds = 4f;
    private const float TowerDefenseAutoplayNearBaseEarlyWarning = 0.18f;
    private const float TowerDefenseAutoplayEmergencyNearBaseCrisis = 0.35f;
    private const float TowerDefenseAutoplayImmediateNearBaseCrisis = 0.75f;
    private const float TowerDefenseAutoplayNearBaseDecisionSustainSeconds = 0.5f;
    private const float TowerDefenseAutoplayHeatMinimumHalfLifeSeconds = 1.25f;
    private const float TowerDefenseAutoplayHeatMaximumHalfLifeSeconds = 2.75f;
    private const float TowerDefenseAutoplayLaneDefenseInnerCells = 2.25f;
    private const float TowerDefenseAutoplayLaneDefenseOuterCells = 8f;
    private const float TowerDefenseAutoplayLaneDefenseAnchorCells = 3.5f;
    private const float TowerDefenseAutoplayLaneDefenseStableThreshold = 0.95f;
    private const float TowerDefenseAutoplayLaneDefensePressureThreshold = 0.7f;
    private const float TowerDefenseAutoplayChargeCooldownSeconds = 36f;
    private const float TowerDefenseAutoplayCapitalHoldSeconds = 12f;
    private const float TowerDefenseAutoplayCapitalHoldCooldownSeconds = 8f;
    private const float TowerDefenseAutoplayStyleRatioMinimum = 0.9f;
    private const float TowerDefenseAutoplayStyleRatioMaximum = 1.1f;

    // JSON is parsed and validated once on the managed side. Burst jobs continue to
    // receive only their compact battlefield inputs; they never parse strings or
    // execute untrusted/generated code.
    private static RougeAutoplayCommanderDefinition TowerDefenseAutoplayCommander =>
        RougeAutoplayCommanderJson.Active;
    private static RougeAutoplayCommanderStrategyConfig TowerDefenseAutoplayStrategy =>
        TowerDefenseAutoplayCommander.Source.strategy;
    private static RougeAutoplayCommanderDialogueConfig TowerDefenseAutoplayDialogue =>
        TowerDefenseAutoplayCommander.Source.dialogue;
    private static RougeAutoplayCommanderThresholdConfig TowerDefenseAutoplayThresholds =>
        TowerDefenseAutoplayStrategy.thresholds;
    private static RougeAutoplayCommanderDialogueThresholdConfig
        TowerDefenseAutoplayDialogueThresholds => TowerDefenseAutoplayDialogue.thresholds;
    private static RougeAutoplayCommanderDialogueTriggerConfig
        TowerDefenseAutoplayDialogueTriggers => TowerDefenseAutoplayDialogue.triggers;
    private static RougeAutoplayCommanderEmotionConfig
        TowerDefenseAutoplayEmotions => TowerDefenseAutoplayDialogue.emotions;
    private static RougeTowerType[] TowerDefenseAutoplayBuildOrder =>
        TowerDefenseAutoplayCommander.BuildOrder;
    private static float TowerDefenseAutoplayExpansionInterval =>
        TowerDefenseAutoplayStrategy.expansionIntervalSeconds;
    private static float TowerDefenseAutoplayCapitalActionInterval =>
        TowerDefenseAutoplayStrategy.capitalActionIntervalSeconds;
    private static float TowerDefenseAutoplayEmergencyActionInterval =>
        TowerDefenseAutoplayStrategy.emergencyActionIntervalSeconds;
    private static float TowerDefenseAutoplayStrategyHoldSeconds =>
        TowerDefenseAutoplayStrategy.strategyHoldSeconds;
    private static float TowerDefenseAutoplayWaveForecastSeconds =>
        TowerDefenseAutoplayStrategy.waveForecastSeconds;
    private static float TowerDefenseAutoplayDialogueIntervalMin =>
        TowerDefenseAutoplayDialogue.intervalMinimumSeconds;
    private static float TowerDefenseAutoplayDialogueIntervalMax =>
        TowerDefenseAutoplayDialogue.intervalMaximumSeconds;
    private static float TowerDefenseAutoplayDialoguePreemptionCooldown =>
        TowerDefenseAutoplayDialogue.preemptionCooldownSeconds;
    private static int TowerDefenseAutoplayDialogueHistorySize =>
        TowerDefenseAutoplayDialogue.recentHistorySize;
    private static float TowerDefenseAutoplaySaleCooldown =>
        TowerDefenseAutoplayStrategy.saleCooldownSeconds;
    private static float TowerDefenseAutoplayMinimumTowerAgeBeforeSale =>
        TowerDefenseAutoplayStrategy.minimumTowerAgeBeforeSaleSeconds;
    private static float TowerDefenseAutoplayPersonalityRegretBudget =>
        TowerDefenseAutoplayStrategy.personalityRegretBudget;
    private static float TowerDefenseAutoplayBossRegretBudget =>
        TowerDefenseAutoplayStrategy.bossRegretBudget;
    private static float TowerDefenseAutoplayMaximumPreferenceShift =>
        TowerDefenseAutoplayStrategy.maximumPreferenceShift;
    private static string TowerDefenseAutoplayAffinityPreference =>
        TowerDefenseAutoplayCommander.AffinityPreferenceKey;

    // Affinity changes relationship dialogue only. Tactical decisions always run
    // with the complete decision model so personality never means playing badly.
    [SerializeField, Range(0, 100)]
    private int _towerDefenseAutoplayAffinity = 15;
    private bool _towerDefenseAutoplayProgressionLoaded;

    private enum AutoplayAffinityTier : byte
    {
        Distant,
        Familiar,
        Close
    }

    [SerializeField, HideInInspector] private bool _towerDefenseAutoplayEnabled;
    [SerializeField, HideInInspector] private bool _towerDefenseAutoplayCleanView;
    private bool _towerDefenseAutoplayConclusionStopping;
    private float _towerDefenseAutoplayTickAccumulator;
    private float _towerDefenseAutoplayTensionTarget = 0.08f;
    private int _towerDefenseAutoplayBuildCursor;
    private int _towerDefenseAutoplayStyleDecisionSequence;
    private int _towerDefenseAutoplayStyleRollSequence = -1;
    private uint _towerDefenseAutoplayStyleSaveRoll;
    private uint _towerDefenseAutoplayStyleControlRoll;
    private uint _towerDefenseAutoplayStyleRoleRoll;
    private System.Random _towerDefenseAutoplayStyleRandom;
    private float _towerDefenseAutoplayStyleSaveRatioScale = 1f;
    private float _towerDefenseAutoplayStyleControlRatioScale = 1f;
    private float _towerDefenseAutoplayStyleRoleRatioScale = 1f;
    private string _towerDefenseAutoplayLastDecision = "托管未启用";
    private string _towerDefenseAutoplayLastLoggedDecision = string.Empty;
    private string _towerDefenseAutoplayEntranceLine = string.Empty;
    private string _towerDefenseAutoplayPendingReleaseToastLine = string.Empty;
    private int _towerDefenseAutoplayEntranceRevision;
    private bool _towerDefenseAutoplayEntrancePending;
    private float _towerDefenseAutoplaySpeechVisibleUntil;
    private System.Random _towerDefenseAutoplayDialogueRandom;
    private readonly int[] _towerDefenseAutoplayLastDialogueIndices =
        new int[(int)AutoplayDialogueCategory.Count];
    private readonly List<string> _towerDefenseAutoplayRecentDialogueLines =
        new List<string>();
    private readonly Dictionary<string, List<int>>
        _towerDefenseAutoplayDialogueShuffleBags =
            new Dictionary<string, List<int>>(StringComparer.Ordinal);
    private readonly List<AutoplayMainTowerDamageSample>
        _towerDefenseAutoplayMainTowerDamageSamples =
            new List<AutoplayMainTowerDamageSample>(64);
    private readonly List<AutoplayMainTowerDamageSample>
        _towerDefenseAutoplayEmotionDamageSamples =
            new List<AutoplayMainTowerDamageSample>(64);
    private readonly List<AutoplayFlowSample> _towerDefenseAutoplayFlowSamples =
        new List<AutoplayFlowSample>(128);
    private readonly List<AutoplayEconomySample>
        _towerDefenseAutoplayEconomySamples =
            new List<AutoplayEconomySample>(128);
    private float _towerDefenseAutoplayEconomyStress;
    private float _towerDefenseAutoplayNearBasePressureSince =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayNearBaseCrisis;
    private float _towerDefenseAutoplayNearBaseInstantRisk;
    private bool _towerDefenseAutoplayImmediateCoreBreach;
    private int _towerDefenseAutoplayImmediateCoreThreatCellIndex = -1;
    private float _towerDefenseAutoplayImmediateCoreThreatPressure;
    private float _towerDefenseAutoplayLastRealNearBaseRisk;
    private float _towerDefenseAutoplayLastRealNearBaseRiskAt =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayEnemyFlowBacklog;
    private float _towerDefenseAutoplayBossReadinessUrgency = 1f;
    private float _towerDefenseAutoplayBossPowerDeficit;
    private float _towerDefenseAutoplayBossControlDeficit;
    private float _towerDefenseAutoplayBossCombatNeed;
    private float _towerDefenseAutoplayBossRequiredPower = 1f;
    private float _towerDefenseAutoplayHeatmapUpdatedAt = float.NegativeInfinity;
    private int _towerDefenseAutoplayHeatmapRevision;
    private int _towerDefenseAutoplayHotspotRevision = -1;
    private RougeTowerDefenseMap _towerDefenseAutoplayHeatmapMap;
    private bool _towerDefenseAutoplaySustainedNearBaseCrisis;
    private bool _towerDefenseAutoplaySustainedMainTowerDamage;
    private float _towerDefenseAutoplayManualSpeechProtectedUntil =
        float.NegativeInfinity;
    private bool _towerDefenseAutoplayMainTowerEverDamagedThisSession;
    private float _towerDefenseAutoplayLastMainTowerHitDialogueGameTime =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayLastMainTowerBurstDialogueGameTime =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayLastBuildDialogueGameTime =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayLastUpgradeDialogueGameTime =
        float.NegativeInfinity;
    private bool _towerDefenseAutoplayTrackingHighPressure;
    private float _towerDefenseAutoplayHighPressureSince = float.NegativeInfinity;
    private float _towerDefenseAutoplayLowPressureSince = float.NegativeInfinity;
    private float _towerDefenseAutoplayLastPressureReliefDialogueGameTime =
        float.NegativeInfinity;
    private AutoplayEmotionState _towerDefenseAutoplayEmotionState =
        AutoplayEmotionState.Calm;
    private AutoplayEmotionState _towerDefenseAutoplayEmotionCandidate =
        AutoplayEmotionState.Calm;
    private bool _towerDefenseAutoplayEmotionInitialized;
    private float _towerDefenseAutoplayEmotionCandidateSince;
    private float _towerDefenseAutoplayLastEmotionDialogueGameTime =
        float.NegativeInfinity;
    private bool _towerDefenseAutoplayDialogueIndicesInitialized;
    private bool _towerDefenseAutoplayEverEnabledThisSession;
    private bool _towerDefenseAutoplayEverReleasedThisSession;
    private int _towerDefenseAutoplaySessionToggleCount;
    private int _towerDefenseAutoplayRapidToggleStreak;
    private float _towerDefenseAutoplayLastToggleGameTime = float.NegativeInfinity;
    private float _towerDefenseAutoplayLastExitGameTime = float.NegativeInfinity;
    private float _towerDefenseAutoplayLastDialogueGameTime = float.NegativeInfinity;
    private float _towerDefenseAutoplayNextDialogueGameTime;
    private int _towerDefenseAutoplayLastDialoguePriority;
    private float _towerDefenseAutoplayLastAmbientLogGameTime =
        float.NegativeInfinity;
    private int _towerDefenseAutoplayThoughtRevision;
    private AutoplayDialogueCategory _towerDefenseAutoplayLastBattleDialogueCategory;
    private bool _towerDefenseAutoplayHasBattleDialogueCategory;
    private bool _towerDefenseAutoplayObservedLiveBoss;
    private bool _towerDefenseAutoplayObservedBossHealthWarning;
    private bool _towerDefenseAutoplayObservedBossHealthCritical;
    private bool _towerDefenseAutoplayObservedBossHealthFinal;
    private bool _towerDefenseAutoplayBossPlanInitialized;
    private bool _towerDefenseAutoplayBossPlanAvailable;
    private AutoplayDialogueCategory _towerDefenseAutoplayPendingDialogueCategory;
    private bool _towerDefenseAutoplayHasPendingDialogue;
    private readonly List<string> _towerDefenseAutoplayThoughtLog =
        new List<string>(TowerDefenseAutoplayThoughtCapacity);
    private readonly List<RougeDefenseTower> _towerDefenseAutoplayBossOverrides =
        new List<RougeDefenseTower>();
    private readonly List<RougeDefenseTower>
        _towerDefenseAutoplayBossFocusCandidates =
        new List<RougeDefenseTower>();
    private readonly HashSet<RougeDefenseTower>
        _towerDefenseAutoplayDesiredBossFocus =
        new HashSet<RougeDefenseTower>();
    private readonly HashSet<RougeDefenseTower>
        _towerDefenseAutoplayReservedBossGuards =
        new HashSet<RougeDefenseTower>();
    private readonly List<AutoplayHeatHotspot>
        _towerDefenseAutoplayNearBaseHotspots =
        new List<AutoplayHeatHotspot>(4);
    private readonly List<AutoplayLaneAnchor>
        _towerDefenseAutoplayLaneAnchors =
        new List<AutoplayLaneAnchor>(8);
    private readonly List<AutoplayBuildChoice>
        _towerDefenseAutoplayBuildChoiceScratch =
        new List<AutoplayBuildChoice>(512);
    private readonly List<AutoplayUpgradeChoice>
        _towerDefenseAutoplayUpgradeChoiceScratch =
        new List<AutoplayUpgradeChoice>(64);
    private readonly List<RougeDefenseTower> _towerDefenseAutoplayOwnedTowers =
        new List<RougeDefenseTower>();
    private readonly List<float> _towerDefenseAutoplayOwnedTowerBuildTimes =
        new List<float>();
    private float _towerDefenseAutoplayLastSaleGameTime = float.NegativeInfinity;
    private float _towerDefenseAutoplayLastCapitalActionGameTime =
        float.NegativeInfinity;
    private bool _towerDefenseAutoplayCapitalHoldActive;
    private float _towerDefenseAutoplayCapitalHoldUntilGameTime =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayCapitalHoldCooldownUntilGameTime =
        float.NegativeInfinity;
    private float _towerDefenseAutoplayLastChargeGameTime = float.NegativeInfinity;
    private int _towerDefenseAutoplayExpansionBaselineTowerCount;
    private float _towerDefenseAutoplayNextExpansionGameTime =
        float.PositiveInfinity;
    private AutoplayStrategyMode _towerDefenseAutoplayStrategyMode =
        AutoplayStrategyMode.Opening;
    private float _towerDefenseAutoplayStrategyModeSince;
    private float[] _towerDefenseAutoplayEnemyPressureByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayCrowdPressureByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayElitePressureByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayBossPressureByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayUrgentPressureByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayActiveCrowdPressureByCell =
        Array.Empty<float>();
    private float[] _towerDefenseAutoplayActiveElitePressureByCell =
        Array.Empty<float>();
    private float[] _towerDefenseAutoplayActiveUrgentPressureByCell =
        Array.Empty<float>();
    private float[] _towerDefenseAutoplayNonBossHeatByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayGroundValueByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayRouteDistanceByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayRouteTrafficByCell = Array.Empty<float>();
    // RouteTraffic includes a light shoulder blur for ordinary circular coverage.
    // CoreTraffic expands every equal-cost shortest-path branch, so a symmetric fork
    // is visible before enemies spawn. RouteNext remains a single representative edge
    // for tangent/targeting calculations that require one concrete direction.
    private float[] _towerDefenseAutoplayRouteCoreTrafficByCell =
        Array.Empty<float>();
    private int[] _towerDefenseAutoplayRouteNextByCell = Array.Empty<int>();
    private int[] _towerDefenseAutoplayRouteCellsByDescendingDistance =
        Array.Empty<int>();
    private int _towerDefenseAutoplayRouteCellCount;
    private float[] _towerDefenseAutoplayRouteBranchFlowScratch =
        Array.Empty<float>();
    private readonly int[] _towerDefenseAutoplayRouteBranchNextScratch = new int[8];
    // Boss investment keeps a separate collapsed trace because it needs exact route
    // length and coverage accounting in addition to the shared ordinary-lane traffic.
    private int[] _towerDefenseAutoplayBossRouteCells = Array.Empty<int>();
    private bool[] _towerDefenseAutoplayBossRouteVisited = Array.Empty<bool>();
    private int _towerDefenseAutoplayBossRouteCellCount;
    private int _towerDefenseAutoplayBossRouteHash;
    private bool _towerDefenseAutoplayBossRouteUsesFlowField;
    private int[] _towerDefenseAutoplayRoutePredecessorCountByCell =
        Array.Empty<int>();
    private Vector2[] _towerDefenseAutoplayRouteTangentByCell =
        Array.Empty<Vector2>();
    private float _towerDefenseAutoplayMaximumCoreTraffic = 1f;
    private float[] _towerDefenseAutoplayCoverageByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayFunctionCoverageByCell =
        Array.Empty<float>();
    private bool[] _towerDefenseAutoplayOccupiedCells = Array.Empty<bool>();
    private bool[] _towerDefenseAutoplayBuildableTopology = Array.Empty<bool>();
    private RougeTowerPlaceEffect[] _towerDefenseAutoplayEffectiveEffects =
        Array.Empty<RougeTowerPlaceEffect>();
    private AutoplayBuildPrior[] _towerDefenseAutoplayBuildPriors =
        Array.Empty<AutoplayBuildPrior>();
    private float[] _towerDefenseAutoplayUpgradeGrowthPriors = Array.Empty<float>();
    private float[] _towerDefenseAutoplayUpgradeAbsoluteGainPriors =
        Array.Empty<float>();
    private float[] _towerDefenseAutoplayUpgradeRangePriors = Array.Empty<float>();
    private NativeArray<float4> _towerDefenseAutoplayPlanPositions;
    private NativeArray<float4> _towerDefenseAutoplayPlanStates;
    private NativeArray<byte> _towerDefenseAutoplayPlanKinds;
    private NativeArray<float> _towerDefenseAutoplayPlanHardFactors;
    private NativeArray<float> _towerDefenseAutoplayPlanMaximumHealth;
    private NativeArray<RougeEnemyEffectState> _towerDefenseAutoplayPlanEffects;
    private NativeArray<AutoplayEnemyContribution>
        _towerDefenseAutoplayPlanEnemyContributions;
    private NativeArray<AutoplaySpatialCell> _towerDefenseAutoplayPlanCells;
    private NativeArray<float> _towerDefenseAutoplayPlanFunctionCoverage;
    private NativeArray<int> _towerDefenseAutoplayPlanRouteNext;
    private NativeArray<AutoplaySpatialCandidateInput>
        _towerDefenseAutoplayPlanCandidates;
    private NativeArray<AutoplaySpatialCandidateResult>
        _towerDefenseAutoplayPlanCandidateResults;
    private NativeArray<AutoplayEnemyTotals> _towerDefenseAutoplayPlanTotals;
    private JobHandle _towerDefenseAutoplayPlanHandle;
    private bool _towerDefenseAutoplayPlanScheduled;
    private bool _towerDefenseAutoplayPlanResultsReady;
    private int _towerDefenseAutoplayPlanGeneration;
    private int _towerDefenseAutoplayPendingPlanGeneration;
    private int _towerDefenseAutoplayPendingPriorRevision;
    private int _towerDefenseAutoplayPendingCellCount;
    private float _towerDefenseAutoplayPendingPlanGameTime;
    private RougeTowerDefenseMap _towerDefenseAutoplayPendingMap;
    private AutoplayBattleSnapshot _towerDefenseAutoplayPendingBaseSnapshot;
    private RougeTowerDefenseMap _towerDefenseAutoplayPriorMap;
    private int _towerDefenseAutoplayPriorTopologyHash;
    private int _towerDefenseAutoplayPriorEffectHash;
    private int _towerDefenseAutoplayPriorRevision;
    private bool _towerDefenseAutoplayPriorDirty = true;
    private Vector2Int _towerDefenseAutoplayRouteMainCell;
    private bool _towerDefenseAutoplayHasRouteMainCell;
    private float _towerDefenseAutoplayMaximumRouteDistance = 1f;
    private readonly int[] _towerDefenseAutoplayTypeCounts =
        new int[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly int[] _towerDefenseAutoplayFunctionCounts = new int[3];

    // Reinforcement analysis stays entirely on the managed decision side. None of
    // these object references or dictionaries are captured by a Burst job.
    private readonly Dictionary<RougeDefenseTower, AutoplayTowerObservation>
        _towerDefenseAutoplayTowerObservations =
            new Dictionary<RougeDefenseTower, AutoplayTowerObservation>();
    private readonly List<RougeDefenseTower>
        _towerDefenseAutoplayStaleTowerObservations =
            new List<RougeDefenseTower>();
    private readonly List<AutoplayTowerPerformance>
        _towerDefenseAutoplayTowerPerformances =
            new List<AutoplayTowerPerformance>();
    private readonly long[] _towerDefenseAutoplayLastTowerDamageFixed =
        new long[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly float[] _towerDefenseAutoplayRecentDamageRateByType =
        new float[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly float[] _towerDefenseAutoplayDamageDeltaByType =
        new float[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly float[] _towerDefenseAutoplayPerformanceWeightByType =
        new float[TowerDefenseVisuals.StandardTowerTypeCount];
    private bool _towerDefenseAutoplayTowerObservationInitialized;
    private float _towerDefenseAutoplayLastTowerObservationAt =
        float.NegativeInfinity;
    private int _towerDefenseAutoplayLastObservedGoldEarned;
    private RougeDefenseTower _towerDefenseAutoplayProvisionalSupportLeader;
    private float _towerDefenseAutoplaySupportLeaderSince =
        float.NegativeInfinity;
    private float _towerDefenseAutoplaySupportLeaderScore;

    private struct AutoplayBattleSnapshot
    {
        public int ActiveEnemies;
        public int EliteEnemies;
        public int BossEnemies;
        public float TotalPressure;
        public float CrowdPressure;
        public float ElitePressure;
        public float BossPressure;
        public float UrgentPressure;
        public float PeakCellPressure;
        public float ImminentEnemyWeight;
        public float ImminentPressure;
        public float ImminentElitePressure;
        public float ImminentBossPressure;
        public float NearBaseEnemyWeight;
        public float IncomingPressure;
        public float IncomingCrowdPressure;
        public float IncomingElitePressure;
        public float NextWaveSeconds;
        public float SecondsUntilBoss;
        public float BossPreparation;
        public float PositiveArmorPressure;
        public float UncoveredArmorPressure;
        public float FastUncontrolledPressure;
        public float VulnerablePressure;
        public float LateHealthRatioSum;
        public float LateHealthWeight;
        public Vector2Int MainCell;
        public bool HasMainCell;
    }

    private struct AutoplayMainTowerDamageSample
    {
        public float GameTime;
        public float Damage;
    }

    private struct AutoplayFlowSample
    {
        public float GameTime;
        public int SpawnedTotal;
        public int KillTotal;
    }

    private struct AutoplayFlowPressure
    {
        public float SpawnPerSecond;
        public float KillsPerSecond;
        public float KillSpawnRatio;
        public float KillTrend;
        public float KillVolatility;
        public float Confidence;
    }

    private struct AutoplayEconomySample
    {
        public float GameTime;
        public int EarnedTotal;
        public int SpentTotal;
    }

    private struct AutoplayLaneAnchor
    {
        public int KeyCellIndex;
        public int CoverageCellIndex;
        public float NextSpawnSeconds;
    }

    private struct AutoplayBuildChoice
    {
        public bool IsValid;
        public RougeTowerType Type;
        public Vector2Int Cell;
        public RougeTowerPlaceEffect PlaceEffect;
        public int BuildOrderIndex;
        public int OriginalCost;
        public int PaidCost;
        public float Utility;
        public float Efficiency;
        public float ObjectiveUtility;
        public float ObjectiveEfficiency;
        public float CapitalScore;
        public float ObjectiveCapitalScore;
        public float FixedScore;
        public float DynamicScore;
        public float TileScore;
        public float CoverageScore;
        public float PressureScore;
        public float DiversityScore;
        public float GoalDefenseScore;
        public float NearBaseHeatCoverage;
        public int NearBaseHeatCellIndex;
        public float OpportunityPenalty;
        public float GeometryScore;
        public float MarginalPower;
        public float BossRouteCoverage;
        public AutoplayPressureLayer DominantPressureLayer;
    }

    private struct AutoplayUpgradeChoice
    {
        public bool IsValid;
        public RougeDefenseTower Tower;
        public int SpecializationChoiceIndex;
        public int OriginalCost;
        public int PaidCost;
        public float Utility;
        public float Efficiency;
        public float ObjectiveUtility;
        public float ObjectiveEfficiency;
        public float CapitalScore;
        public float ObjectiveCapitalScore;
        public float PressureScore;
        public float GrowthScore;
        public float NearBaseHeatCoverage;
        public int NearBaseHeatCellIndex;
        public float ObservedCoreRate;
        public float ObservedCoreConfidence;
        public float UncoveredArmorPressure;
        public float FastUncontrolledPressure;
        public float VulnerablePressure;
        public float LateHealthRatio;
        public float EarlyRouteExposure;
        public float LateRouteExposure;
        public float RouteReuse;
        public float Bottleneck;
        public float MarginalPower;
        public AutoplayPressureLayer DominantPressureLayer;
    }

    private struct AutoplaySupportChoice
    {
        public bool IsValid;
        public Vector2Int Cell;
        public int Cost;
        public int AffectedTowers;
        public int HighValueTowers;
        public RougeTowerType AnchorType;
        public float AnchorScore;
        public float ObservationConfidence;
        public bool CoversProvenLeader;
        public float Utility;
        public float Efficiency;
        public float CapitalGain;
    }

    private struct AutoplayChargeChoice
    {
        public bool IsValid;
        public Vector2Int OwnerCell;
        public Vector2Int TargetCell;
        public RougeDefenseTower TargetTower;
        public int OriginalCost;
        public int PaidCost;
        public float TargetStrategicScore;
        public float ExpectedEffectUtility;
        public float OwnerOpportunityCost;
        public float CapitalGain;
    }

    private struct AutoplayTowerObservation
    {
        public float ObservedSeconds;
        public float ObservedDamage;
        public float AttributedKillGold;
    }

    private struct AutoplayTowerPerformance
    {
        public RougeDefenseTower Tower;
        public Vector2Int Cell;
        public float ObservationAge;
        public float ContributionValue;
        public float ReturnOnInvestment;
        public float RecentDamageRate;
        public float RescueFactor;
        public float StrategicScore;
    }

    private struct AutoplayBuildPrior
    {
        public bool IsValid;
        public RougeTowerPlaceEffect PlaceEffect;
        public int OriginalCost;
        public int PaidCost;
        public float AttackRange;
        public float CombatPower;
        public float FixedScore;
        public float TileScore;
        public float CoverageScore;
        public float SingleTargetPower;
        public float BossRouteCoverage;
        public float OpportunityPenalty;
        public float GeometryScore;
        public float PathDwell;
        public float RouteReuse;
        public float Bottleneck;
        public float DirectionConsistency;
        public float PiercingLaneScore;
        public float RandomAreaHitChance;
        public float SingleTargetRealization;
        public float EarlyRouteExposure;
        public float LateRouteExposure;
        public float FinisherFit;
        public float SetupFit;
    }

    private struct AutoplayRouteGeometry
    {
        public float PathDwell;
        public float Reuse;
        public float Bottleneck;
        public float DirectionConsistency;
        public float PiercingLane;
        public float RandomAreaHitChance;
        public float EarlyExposure;
        public float LateExposure;
    }

    private struct AutoplayPressureChannels
    {
        public float Total;
        public float Crowd;
        public float Elite;
        public float Boss;
        public float Urgent;
    }

    private struct AutoplayHeatHotspot
    {
        public Vector2Int Cell;
        public float Risk;
        public float RequiredCoverage;
        public float CurrentCoverage;
    }

    private struct AutoplaySpatialCell
    {
        public float Total;
        public float Crowd;
        public float Elite;
        public float Boss;
        public float Urgent;
        public float ActiveCrowd;
        public float ActiveElite;
        public float ActiveUrgent;
        public float GroundValue;
        public float Coverage;
        public float RouteDistance;
        public byte IsGround;
    }

    private struct AutoplaySpatialCandidateInput
    {
        public float AttackRange;
        public byte IsValid;
        public byte FunctionGroup;
    }

    private struct AutoplaySpatialCandidateResult
    {
        public AutoplayPressureChannels Pressure;
        public AutoplayPressureChannels UncoveredPressure;
        public float MarginalRouteCoverage;
    }

    private struct AutoplayEnemyContribution
    {
        public int CellIndex;
        public float Pressure;
        public float Crowd;
        public float Elite;
        public float Boss;
        public float Urgent;
        public float SpeedRatio;
        public float ArrivalWeight;
        public float ImminentPressure;
        public float ImminentElitePressure;
        public float ImminentBossPressure;
        public float NearBaseEnemyWeight;
        public float PositiveArmorPressure;
        public float UncoveredArmorPressure;
        public float FastUncontrolledPressure;
        public float VulnerablePressure;
        public float HealthRatio;
        public float RouteProgress;
        public byte IsValid;
        public byte IsElite;
        public byte IsBoss;
    }

    private struct AutoplayEnemyTotals
    {
        public int ActiveEnemies;
        public int EliteEnemies;
        public int BossEnemies;
        public float TotalPressure;
        public float CrowdPressure;
        public float ElitePressure;
        public float BossPressure;
        public float UrgentPressure;
        public float PeakCellPressure;
        public float ImminentEnemyWeight;
        public float ImminentPressure;
        public float ImminentElitePressure;
        public float ImminentBossPressure;
        public float NearBaseEnemyWeight;
        public float PositiveArmorPressure;
        public float UncoveredArmorPressure;
        public float FastUncontrolledPressure;
        public float VulnerablePressure;
        public float LateHealthRatioSum;
        public float LateHealthWeight;
    }

    private static float CalculateAutoplayNearBaseCrisis(float routeDistance,
        float fullCrisisDistanceCells)
    {
        // The configured inner route distance is a full crisis. From there to eight
        // cells the signal fades continuously; enemies farther away contribute none.
        // This pure math helper is shared by the Burst and synchronous paths.
        float fullDistance = math.clamp(fullCrisisDistanceCells, 0.5f,
            TowerDefenseAutoplayNearBaseOuterCrisisCells - 0.5f);
        return math.saturate((TowerDefenseAutoplayNearBaseOuterCrisisCells -
                              routeDistance) /
                             (TowerDefenseAutoplayNearBaseOuterCrisisCells -
                              fullDistance));
    }

    [BurstCompile(FloatMode = FloatMode.Fast,
        FloatPrecision = FloatPrecision.Standard)]
    private struct AnalyzeAutoplayEnemiesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4> Positions;
        [ReadOnly] public NativeArray<float4> States;
        [ReadOnly] public NativeArray<byte> Kinds;
        [ReadOnly] public NativeArray<RougeEnemyEffectState> Effects;
        [ReadOnly] public NativeArray<float> HardFactors;
        [ReadOnly] public NativeArray<float> MaximumHealthByKind;
        [ReadOnly] public NativeArray<AutoplaySpatialCell> Cells;
        [WriteOnly] public NativeArray<AutoplayEnemyContribution> Contributions;
        public int Width;
        public int Height;
        public float CellSize;
        public float OriginX;
        public float OriginY;
        public float RenderHeight;
        public float BaselineSpeed;
        public float MaximumRouteDistance;
        public float NearBaseFullCrisisCells;
        public int MainCellX;
        public int MainCellY;
        public byte HasMainCell;

        public void Execute(int index)
        {
            float4 state = States[index];
            if (state.x <= 0f)
            {
                Contributions[index] = default;
                return;
            }

            float4 position = Positions[index];
            int visualFlags = (int)math.floor(
                math.max(state.w, 0f) / 10f + 0.0001f);
            if (position.y > RenderHeight + 0.05f || (visualFlags & 4) != 0)
            {
                Contributions[index] = default;
                return;
            }

            int x = (int)math.floor((position.x - OriginX) /
                                    math.max(0.1f, CellSize));
            int y = (int)math.floor((position.z - OriginY) /
                                    math.max(0.1f, CellSize));
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                Contributions[index] = default;
                return;
            }

            byte kind = Kinds[index];
            bool boss = (kind & BossEnemyFlag) != 0;
            bool elite = !boss && (kind & EliteEnemyFlag) != 0;
            RougeEnemyEffectState effects = Effects[index];
            float pressure = boss ? 24f : elite ? 4f : 1f;
            float maximumHealth = effects.MaximumHealth > 0.001f
                ? effects.MaximumHealth
                : MaximumHealthByKind[kind];
            float healthRatio = maximumHealth > 0.001f
                ? math.saturate(state.x / maximumHealth)
                : 1f;
            if (maximumHealth > 0.001f)
                pressure *= 0.65f + healthRatio * 0.35f;

            int cellIndex = y * Width + x;
            float goalThreat = 0f;
            float distanceWeight = 0f;
            float nearBaseEnemyWeight = 0f;
            if (HasMainCell != 0)
            {
                float routeDistance = Cells[cellIndex].RouteDistance;
                if (!math.isfinite(routeDistance))
                    routeDistance = math.abs(x - MainCellX) +
                                    math.abs(y - MainCellY);
                float nearBaseCrisis = CalculateAutoplayNearBaseCrisis(
                    routeDistance, NearBaseFullCrisisCells);
                // Boss pressure has its own channel. Mixing it into the cleanup
                // signal makes the Boss itself look like a minion leak.
                nearBaseEnemyWeight = boss
                    ? 0f
                    : nearBaseCrisis * (elite ? 1.5f : 1f);
                goalThreat = 1f - math.saturate(routeDistance /
                    math.max(1f, MaximumRouteDistance));
                distanceWeight = 1f /
                    (1f + routeDistance * routeDistance * 0.22f);
                pressure *= 1f + goalThreat * 0.9f;
            }

            float hardFactor = HardFactors[kind];
            float eliteShare = boss ? 0f : math.saturate(hardFactor);
            float crowdPressure = boss
                ? 0f
                : pressure * (1f - eliteShare);
            float elitePressure = boss ? 0f : pressure * eliteShare;
            float bossPressure = boss ? pressure : 0f;
            float slowMultiplier = effects.FreezeTimer > 0f
                ? 0.05f
                : effects.SlowTimer > 0f
                    ? math.clamp(1f - effects.SlowPercent * 0.01f, 0.05f, 1f)
                    : 1f;
            float speedRatio = state.z * slowMultiplier /
                               math.max(0.01f, BaselineSpeed);
            float speedThreat = math.saturate((speedRatio - 1.08f) /
                                              (1.35f - 1.08f));
            float speedArrival = math.saturate((speedRatio - 0.8f) /
                                               (1.5f - 0.8f));
            float arrivalWeight = distanceWeight *
                math.lerp(0.78f, 1.38f, speedArrival);
            float imminentPressure = pressure * arrivalWeight;
            float urgentFactor = math.max(goalThreat, speedThreat);
            float urgentPressure = !boss && urgentFactor >= 0.7f
                ? pressure * (0.4f + urgentFactor * 0.8f)
                : 0f;
            float positiveArmor = math.max(0f, effects.Armor);
            float armorDemand = pressure * math.saturate(positiveArmor / 8f);
            bool vulnerable = effects.VulnerabilityTimer > 0f ||
                              effects.VulnerabilityDamageBonusTimer > 0f ||
                              effects.VulnerabilityArmorPenetrationTimer > 0f;
            float uncoveredArmor = armorDemand * (vulnerable ? 0.2f : 1f);
            float fastUncontrolled = pressure * speedThreat;
            float lateWeight = pressure * math.saturate((goalThreat - 0.45f) /
                                                        0.55f);

            Contributions[index] = new AutoplayEnemyContribution
            {
                CellIndex = cellIndex,
                Pressure = pressure,
                Crowd = crowdPressure,
                Elite = elitePressure,
                Boss = bossPressure,
                Urgent = urgentPressure,
                SpeedRatio = speedRatio,
                ArrivalWeight = arrivalWeight,
                ImminentPressure = imminentPressure,
                ImminentElitePressure = boss ? 0f :
                    imminentPressure * hardFactor,
                ImminentBossPressure = boss ? imminentPressure : 0f,
                NearBaseEnemyWeight = nearBaseEnemyWeight,
                PositiveArmorPressure = armorDemand,
                UncoveredArmorPressure = uncoveredArmor,
                FastUncontrolledPressure = fastUncontrolled,
                VulnerablePressure = vulnerable ? pressure : 0f,
                HealthRatio = healthRatio,
                RouteProgress = goalThreat,
                IsValid = 1,
                IsElite = elite ? (byte)1 : (byte)0,
                IsBoss = boss ? (byte)1 : (byte)0
            };
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast,
        FloatPrecision = FloatPrecision.Standard)]
    private struct ReduceAutoplayEnemyPressureJob : IJob
    {
        [ReadOnly] public NativeArray<AutoplayEnemyContribution> Contributions;
        [ReadOnly] public NativeArray<int> RouteNext;
        public NativeArray<AutoplaySpatialCell> Cells;
        public NativeArray<AutoplayEnemyTotals> Totals;
        public int EnemyCount;

        public void Execute()
        {
            AutoplayEnemyTotals totals = default;
            int contributionCount = math.min(EnemyCount, Contributions.Length);
            for (int i = 0; i < contributionCount; i++)
            {
                AutoplayEnemyContribution contribution = Contributions[i];
                if (contribution.IsValid == 0) continue;

                totals.ActiveEnemies++;
                totals.TotalPressure += contribution.Pressure;
                totals.CrowdPressure += contribution.Crowd;
                totals.ElitePressure += contribution.Elite;
                totals.BossPressure += contribution.Boss;
                totals.UrgentPressure += contribution.Urgent;
                totals.ImminentEnemyWeight += contribution.ArrivalWeight;
                totals.ImminentPressure += contribution.ImminentPressure;
                totals.ImminentElitePressure +=
                    contribution.ImminentElitePressure;
                totals.ImminentBossPressure +=
                    contribution.ImminentBossPressure;
                totals.NearBaseEnemyWeight += contribution.NearBaseEnemyWeight;
                totals.PositiveArmorPressure +=
                    contribution.PositiveArmorPressure;
                totals.UncoveredArmorPressure +=
                    contribution.UncoveredArmorPressure;
                totals.FastUncontrolledPressure +=
                    contribution.FastUncontrolledPressure;
                totals.VulnerablePressure += contribution.VulnerablePressure;
                float lateWeight = contribution.Pressure *
                    math.saturate((contribution.RouteProgress - 0.45f) / 0.55f);
                totals.LateHealthRatioSum += contribution.HealthRatio * lateWeight;
                totals.LateHealthWeight += lateWeight;
                if (contribution.IsBoss != 0) totals.BossEnemies++;
                else if (contribution.IsElite != 0) totals.EliteEnemies++;

                int projectionCells = math.clamp(
                    (int)math.ceil(2f + contribution.SpeedRatio * 1.4f),
                    2, TowerDefenseAutoplayPressureProjectionCells);
                int cellIndex = contribution.CellIndex;
                for (int step = 0; step <= projectionCells; step++)
                {
                    if ((uint)cellIndex >= (uint)Cells.Length) break;
                    float weight = step == 0 ? 1f : math.pow(0.68f, step);
                    float urgentWeight = math.lerp(weight, 1f, 0.22f);
                    AutoplaySpatialCell cell = Cells[cellIndex];
                    cell.Total += contribution.Pressure * weight;
                    cell.Crowd += contribution.Crowd * weight;
                    cell.Elite += contribution.Elite * weight;
                    cell.Boss += contribution.Boss * weight;
                    cell.Urgent += contribution.Urgent * urgentWeight;
                    cell.ActiveCrowd += contribution.Crowd * weight;
                    cell.ActiveElite += contribution.Elite * weight;
                    // Active pressure channels intentionally describe non-Boss
                    // cleanup work. Otherwise an urgent Boss would make its own
                    // focus order look like a minion leak crisis.
                    if (contribution.IsBoss == 0)
                        cell.ActiveUrgent += contribution.Urgent * urgentWeight;
                    Cells[cellIndex] = cell;
                    if (step >= projectionCells) break;
                    int next = RouteNext[cellIndex];
                    if ((uint)next >= (uint)Cells.Length || next == cellIndex)
                        break;
                    cellIndex = next;
                }
            }

            for (int i = 0; i < Cells.Length; i++)
                totals.PeakCellPressure = math.max(totals.PeakCellPressure,
                    Cells[i].Total);
            Totals[0] = totals;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast,
        FloatPrecision = FloatPrecision.Standard)]
    private struct ScoreAutoplaySpatialCandidatesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<AutoplaySpatialCandidateInput> Candidates;
        [ReadOnly] public NativeArray<AutoplaySpatialCell> Cells;
        [ReadOnly] public NativeArray<float> FunctionCoverage;
        [WriteOnly] public NativeArray<AutoplaySpatialCandidateResult> Results;
        public int Width;
        public int Height;
        public int CellCount;
        public float CellSize;

        public void Execute(int index)
        {
            AutoplaySpatialCandidateInput candidate = Candidates[index];
            if (candidate.IsValid == 0 || candidate.AttackRange <= 0f)
            {
                Results[index] = default;
                return;
            }

            int cellIndex = index % CellCount;
            int towerX = cellIndex % Width;
            int towerY = cellIndex / Width;
            float range = candidate.AttackRange;
            float rangeSquared = range * range;
            int radiusCells = math.max(1,
                (int)math.ceil(range / math.max(0.1f, CellSize)));
            AutoplaySpatialCandidateResult result = default;
            int minY = math.max(0, towerY - radiusCells);
            int maxY = math.min(Height - 1, towerY + radiusCells);
            int minX = math.max(0, towerX - radiusCells);
            int maxX = math.min(Width - 1, towerX + radiusCells);
            int functionOffset = candidate.FunctionGroup * CellCount;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (x - towerX) * CellSize;
                float dy = (y - towerY) * CellSize;
                float distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > rangeSquared) continue;
                float distanceRatio = math.saturate(
                    math.sqrt(distanceSquared) / range);
                int coveredIndex = y * Width + x;
                AutoplaySpatialCell cell = Cells[coveredIndex];
                float pressureFalloff = math.lerp(1f, 0.45f, distanceRatio);
                result.Pressure.Total += cell.Total * pressureFalloff;
                result.Pressure.Crowd += cell.Crowd * pressureFalloff;
                result.Pressure.Elite += cell.Elite * pressureFalloff;
                result.Pressure.Boss += cell.Boss * pressureFalloff;
                result.Pressure.Urgent += cell.Urgent * pressureFalloff;
                if (cell.IsGround == 0) continue;

                float marginalFalloff = math.lerp(1f, 0.42f, distanceRatio);
                float function = FunctionCoverage[functionOffset + coveredIndex];
                result.MarginalRouteCoverage += cell.GroundValue *
                    marginalFalloff /
                    (1f + cell.Coverage * 0.28f + function * 0.72f);
                float coverageDivisor = 1f + function * 0.85f +
                                        cell.Coverage * 0.2f;
                float uncoveredScale = marginalFalloff / coverageDivisor;
                result.UncoveredPressure.Total += cell.Total * uncoveredScale;
                result.UncoveredPressure.Crowd += cell.Crowd * uncoveredScale;
                result.UncoveredPressure.Elite += cell.Elite * uncoveredScale;
                result.UncoveredPressure.Boss += cell.Boss * uncoveredScale;
                result.UncoveredPressure.Urgent += cell.Urgent * uncoveredScale;
            }
            Results[index] = result;
        }
    }

    private enum AutoplayPressureLayer : byte
    {
        Total,
        Crowd,
        Elite,
        Boss,
        Urgent
    }

    private enum AutoplayStrategyMode : byte
    {
        Opening,
        Economy,
        Hold,
        PrepareBoss,
        BossFight,
        Emergency
    }

    private enum AutoplayCapitalActionKind : byte
    {
        None,
        Hold,
        Build,
        Upgrade,
        Support,
        Charge
    }

    private enum AutoplayEmotionState : byte
    {
        Calm,
        Focused,
        Tense,
        Critical
    }

    private enum AutoplayDialogueCategory : byte
    {
        TakeoverFirst,
        TakeoverQuickReturn,
        TakeoverFrequentToggle,
        TakeoverReturn,
        TakeoverHighPressure,
        TakeoverLateTier1,
        TakeoverLateTier2,
        TakeoverLateTier3,
        TakeoverLateTier4,
        ReleaseFirst,
        Calm,
        Crowd,
        Hard,
        BossArrival,
        Boss,
        BossHealthHalf,
        BossHealthQuarter,
        BossHealthFinal,
        Urgent,
        BaseLow,
        BaseCritical,
        BaseFirstDamage,
        BaseDamaged,
        BaseBurstDamage,
        BuildTower,
        UpgradeTower,
        PressureRelieved,
        EmotionToCalm,
        EmotionToFocused,
        EmotionToTense,
        EmotionToCritical,
        PortraitClickCalm,
        PortraitClickFocused,
        PortraitClickTense,
        PortraitClickCritical,
        PortraitRapidClickCalm,
        PortraitRapidClickFocused,
        PortraitRapidClickTense,
        PortraitRapidClickCritical,
        Saving,
        GreatTile,
        Branch,
        Discount,
        Count
    }

    partial void RefreshTowerDefenseAutoplayPresentation();

    public int TowerDefenseAutoplayAffinity => _towerDefenseAutoplayAffinity;

    public void SetTowerDefenseAutoplayAffinity(int value)
    {
        int next = Mathf.Clamp(value, 0, 100);
        if (_towerDefenseAutoplayAffinity == next) return;
        _towerDefenseAutoplayAffinity = next;
        _towerDefenseAutoplayRecentDialogueLines.Clear();
        _towerDefenseAutoplayIdentityRendered = false;
        PlayerPrefs.SetInt(TowerDefenseAutoplayAffinityPreference, next);
        PlayerPrefs.Save();
        RefreshTowerDefenseAutoplayPresentation();
    }

    public void AddTowerDefenseAutoplayAffinity(int amount)
    {
        SetTowerDefenseAutoplayAffinity(_towerDefenseAutoplayAffinity + amount);
    }

    private void LoadTowerDefenseAutoplayProgression()
    {
        if (_towerDefenseAutoplayProgressionLoaded) return;
        _towerDefenseAutoplayProgressionLoaded = true;
        _towerDefenseAutoplayAffinity = Mathf.Clamp(
            PlayerPrefs.GetInt(TowerDefenseAutoplayAffinityPreference,
                TowerDefenseAutoplayDialogue.startingAffinity), 0, 100);
    }

    private static float AutoplayMapReadingSkill =>
        TowerDefenseAutoplayStrategy.skills.mapReading;
    private static float AutoplayThreatReadingSkill =>
        TowerDefenseAutoplayStrategy.skills.threatReading;
    private static float AutoplayCrisisResponseSkill =>
        TowerDefenseAutoplayStrategy.skills.crisisResponse;
    private static float AutoplayAdaptationSkill =>
        TowerDefenseAutoplayStrategy.skills.adaptation;

    private AutoplayAffinityTier CurrentAutoplayAffinityTier =>
        _towerDefenseAutoplayAffinity >= TowerDefenseAutoplayDialogue.closeThreshold
            ? AutoplayAffinityTier.Close
            : _towerDefenseAutoplayAffinity >=
              TowerDefenseAutoplayDialogue.familiarThreshold
                ? AutoplayAffinityTier.Familiar
                : AutoplayAffinityTier.Distant;

    private string CurrentAutoplayAffinityLabel
    {
        get
        {
            switch (CurrentAutoplayAffinityTier)
            {
                case AutoplayAffinityTier.Close:
                    return TowerDefenseAutoplayCommander.CloseAffinityLabel;
                case AutoplayAffinityTier.Familiar:
                    return TowerDefenseAutoplayCommander.FamiliarAffinityLabel;
                default:
                    return TowerDefenseAutoplayCommander.DistantAffinityLabel;
            }
        }
    }

    private string CurrentAutoplayStrategyLabel
    {
        get
        {
            switch (_towerDefenseAutoplayStrategyMode)
            {
                case AutoplayStrategyMode.Economy:
                    return TowerDefenseAutoplayCommander.ModeLabels.economy;
                case AutoplayStrategyMode.Hold:
                    return TowerDefenseAutoplayCommander.ModeLabels.hold;
                case AutoplayStrategyMode.PrepareBoss:
                    return TowerDefenseAutoplayCommander.ModeLabels.prepareBoss;
                case AutoplayStrategyMode.BossFight:
                    return TowerDefenseAutoplayCommander.ModeLabels.bossFight;
                case AutoplayStrategyMode.Emergency:
                    return TowerDefenseAutoplayCommander.ModeLabels.emergency;
                default:
                    return TowerDefenseAutoplayCommander.ModeLabels.opening;
            }
        }
    }

    private float ApplyAutoplayJudgmentUncertainty(float efficiency,
        RougeTowerType type, Vector2Int cell)
    {
        // Objective cell tie-breaking remains reproducible. Per-match variation is
        // applied later by the bounded radar draw, while this signature prevents two
        // commanders from ranking equally sound cells/types in exactly the same order.
        uint hash = 2166136261u;
        string commanderId = TowerDefenseAutoplayCommander.CommanderId ??
                             string.Empty;
        unchecked
        {
            for (int i = 0; i < commanderId.Length; i++)
            {
                hash ^= commanderId[i];
                hash *= 16777619u;
            }
            hash ^= (uint)((int)type + 1) * 2246822519u;
            hash *= 16777619u;
            hash ^= (uint)(cell.x + 257) * 3266489917u;
            hash *= 16777619u;
            hash ^= (uint)(cell.y + 521) * 668265263u;
            hash *= 16777619u;
            hash ^= (uint)_towerDefenseGameplaySeed * 374761393u;
        }
        float signature = (hash & 0xffffu) / 32767.5f - 1f;
        float stress = Mathf.Clamp01((_towerDefenseAutoplayTensionTarget -
                                      0.55f) / 0.45f);
        float maximumShift = Mathf.Lerp(
            TowerDefenseAutoplayMaximumPreferenceShift * 0.45f, 0.005f,
            stress);
        return efficiency * (1f + signature * maximumShift);
    }

    private float ApplyAutoplayPersonalityPreference(float objectiveEfficiency,
        float rawPreference)
    {
        float normalizedPreference = Mathf.Clamp((rawPreference - 1f) / 0.2f,
            -1f, 1f);
        // This commander is composed: pressure pulls her closer to the objective
        // ranking instead of making her personality swings larger.
        float stress = Mathf.Clamp01((_towerDefenseAutoplayTensionTarget - 0.55f) /
                                     0.45f);
        float maximumShift = Mathf.Lerp(
            TowerDefenseAutoplayMaximumPreferenceShift, 0.02f, stress);
        return objectiveEfficiency *
               (1f + normalizedPreference * maximumShift);
    }

    private float ApplyAutoplayEconomyReturnSignal(float efficiency,
        float objectiveEfficiency)
    {
        if (_towerDefenseAutoplayEconomyStress <= 0.0001f) return efficiency;
        // Objective efficiency already includes crowd damage, control coverage and
        // last-line defense before dividing by paid gold. Economy pressure may only
        // nudge this ordering by +/-2%; it must never turn support into "no income".
        float returnQuality = Mathf.InverseLerp(5f, 35f, objectiveEfficiency);
        float multiplier = Mathf.Lerp(1f,
            Mathf.Lerp(0.98f, 1.02f, returnQuality),
            _towerDefenseAutoplayEconomyStress);
        return efficiency * multiplier;
    }

    private float GetAutoplayPersonalityRegretBudget(
        AutoplayBattleSnapshot snapshot)
    {
        if (_towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Emergency ||
            _towerDefenseAutoplaySustainedNearBaseCrisis ||
            _towerDefenseAutoplaySustainedMainTowerDamage)
            return 0f;
        float healthRatio = mainTower != null && mainTower.maxHealth > 0.001f
            ? Mathf.Clamp01(mainTower.CurrentHealth / mainTower.maxHealth)
            : 1f;
        if (healthRatio <= 0.5f || snapshot.UrgentPressure >= 2f)
            return 0f;
        if (snapshot.BossEnemies > 0 || snapshot.BossPreparation >= 0.65f)
            return TowerDefenseAutoplayBossRegretBudget;
        return TowerDefenseAutoplayPersonalityRegretBudget;
    }

    public bool TowerDefenseAutoplayEnabled => _towerDefenseAutoplayEnabled;
    public bool IsTowerDefenseAutoplayEnabled => _towerDefenseAutoplayEnabled;
    public bool AutoplayCleanView => _towerDefenseAutoplayCleanView;
    public bool TowerDefenseAutoplayCleanView => _towerDefenseAutoplayCleanView;
    public string TowerDefenseAutoplayCharacterName =>
        TowerDefenseAutoplayCommander.Name;
    public string TowerDefenseAutoplayPersonaLabel =>
        TowerDefenseAutoplayCommander.Persona;
    public string TowerDefenseAutoplayTalentName =>
        TowerDefenseAutoplayCommander.TalentName;
    public string TowerDefenseAutoplayTalentDescription =>
        TowerDefenseAutoplayCommander.TalentDescription;
    public float TowerDefenseAutoplayTalentCostMultiplier =>
        TowerDefenseAutoplayCommander.CostMultiplier;
    public string TowerDefenseAutoplayRoleName => TowerDefenseAutoplayCommander.Role;
    public string TowerDefenseAutoplayPortraitResourcePath =>
        TowerDefenseAutoplayCommander.PortraitResourcePath;
    public string TowerDefenseAutoplayLastDecision => _towerDefenseAutoplayLastDecision;
    public IReadOnlyList<string> TowerDefenseAutoplayThoughtLog =>
        _towerDefenseAutoplayThoughtLog;
    public string TowerDefenseAutoplayEntranceLine => _towerDefenseAutoplayEntranceLine;
    public int TowerDefenseAutoplayEntranceRevision => _towerDefenseAutoplayEntranceRevision;
    public bool TowerDefenseAutoplayEntrancePending =>
        _towerDefenseAutoplayEntrancePending;
    public int TowerDefenseAutoplayPriorRevision => _towerDefenseAutoplayPriorRevision;

    /// <summary>
    /// Marks map/tower balance priors stale without releasing their reusable buffers.
    /// Session reset code may call this after changing runtime balance data.
    /// </summary>
    public void InvalidateTowerDefenseAutoplayPriorCache()
    {
        _towerDefenseAutoplayPriorDirty = true;
    }

    /// <summary>
    /// Releases references and reusable prior buffers. Safe for session disposal; map
    /// instance/hash detection will lazily rebuild everything on the next live tick.
    /// </summary>
    public void ClearTowerDefenseAutoplayPriorCache()
    {
        InvalidateTowerDefenseAutoplayPlan();
        _towerDefenseAutoplayPriorMap = null;
        _towerDefenseAutoplayPriorTopologyHash = 0;
        _towerDefenseAutoplayPriorEffectHash = 0;
        _towerDefenseAutoplayPriorDirty = true;
        _towerDefenseAutoplayBuildableTopology = Array.Empty<bool>();
        _towerDefenseAutoplayEffectiveEffects = Array.Empty<RougeTowerPlaceEffect>();
        _towerDefenseAutoplayGroundValueByCell = Array.Empty<float>();
        _towerDefenseAutoplayRouteDistanceByCell = Array.Empty<float>();
        _towerDefenseAutoplayRouteTrafficByCell = Array.Empty<float>();
        _towerDefenseAutoplayRouteCoreTrafficByCell = Array.Empty<float>();
        _towerDefenseAutoplayRouteNextByCell = Array.Empty<int>();
        _towerDefenseAutoplayBossRouteCells = Array.Empty<int>();
        _towerDefenseAutoplayBossRouteVisited = Array.Empty<bool>();
        _towerDefenseAutoplayBossRouteCellCount = 0;
        _towerDefenseAutoplayBossRouteHash = 0;
        _towerDefenseAutoplayBossRouteUsesFlowField = false;
        _towerDefenseAutoplayRoutePredecessorCountByCell = Array.Empty<int>();
        _towerDefenseAutoplayRouteTangentByCell = Array.Empty<Vector2>();
        _towerDefenseAutoplayMaximumCoreTraffic = 1f;
        _towerDefenseAutoplayCoverageByCell = Array.Empty<float>();
        _towerDefenseAutoplayFunctionCoverageByCell = Array.Empty<float>();
        _towerDefenseAutoplayBuildPriors = Array.Empty<AutoplayBuildPrior>();
        _towerDefenseAutoplayUpgradeGrowthPriors = Array.Empty<float>();
        _towerDefenseAutoplayUpgradeAbsoluteGainPriors = Array.Empty<float>();
        _towerDefenseAutoplayUpgradeRangePriors = Array.Empty<float>();
        _towerDefenseAutoplayEnemyPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayCrowdPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayElitePressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayBossPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayUrgentPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayActiveCrowdPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayActiveElitePressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayActiveUrgentPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayNonBossHeatByCell = Array.Empty<float>();
        _towerDefenseAutoplayHeatmapMap = null;
        _towerDefenseAutoplayHeatmapUpdatedAt = float.NegativeInfinity;
        _towerDefenseAutoplayHeatmapRevision = 0;
        _towerDefenseAutoplayHotspotRevision = -1;
        _towerDefenseAutoplayNearBaseInstantRisk = 0f;
        _towerDefenseAutoplayImmediateCoreBreach = false;
        _towerDefenseAutoplayImmediateCoreThreatCellIndex = -1;
        _towerDefenseAutoplayImmediateCoreThreatPressure = 0f;
        _towerDefenseAutoplayLastRealNearBaseRisk = 0f;
        _towerDefenseAutoplayLastRealNearBaseRiskAt = float.NegativeInfinity;
        _towerDefenseAutoplayEnemyFlowBacklog = 0f;
        _towerDefenseAutoplayBossReadinessUrgency = 1f;
        _towerDefenseAutoplayBossPowerDeficit = 0f;
        _towerDefenseAutoplayBossControlDeficit = 0f;
        _towerDefenseAutoplayBossCombatNeed = 0f;
        _towerDefenseAutoplayBossRequiredPower = 1f;
        _towerDefenseAutoplayOccupiedCells = Array.Empty<bool>();
        _towerDefenseAutoplayHasRouteMainCell = false;
        _towerDefenseAutoplayMaximumRouteDistance = 1f;
    }

    public void ClearTowerDefenseAutoplayDialogueSession()
    {
        _towerDefenseAutoplayDialogueRandom = null;
        _towerDefenseAutoplayDialogueIndicesInitialized = false;
        _towerDefenseAutoplayEverEnabledThisSession = false;
        _towerDefenseAutoplayEverReleasedThisSession = false;
        _towerDefenseAutoplaySessionToggleCount = 0;
        _towerDefenseAutoplayRapidToggleStreak = 0;
        _towerDefenseAutoplayLastToggleGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayLastExitGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayLastDialogueGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayNextDialogueGameTime = 0f;
        _towerDefenseAutoplayLastDialoguePriority = 0;
        _towerDefenseAutoplayLastAmbientLogGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayHasBattleDialogueCategory = false;
        _towerDefenseAutoplayObservedLiveBoss = false;
        _towerDefenseAutoplayObservedBossHealthWarning = false;
        _towerDefenseAutoplayObservedBossHealthCritical = false;
        _towerDefenseAutoplayObservedBossHealthFinal = false;
        _towerDefenseAutoplayBossPlanInitialized = false;
        _towerDefenseAutoplayBossPlanAvailable = false;
        _towerDefenseAutoplayHasPendingDialogue = false;
        _towerDefenseAutoplayEntrancePending = false;
        _towerDefenseAutoplayEntranceLine = string.Empty;
        _towerDefenseAutoplayPendingReleaseToastLine = string.Empty;
        _towerDefenseAutoplaySpeechVisibleUntil = 0f;
        _towerDefenseAutoplayThoughtLog.Clear();
        _towerDefenseAutoplayRecentDialogueLines.Clear();
        _towerDefenseAutoplayDialogueShuffleBags.Clear();
        _towerDefenseAutoplayMainTowerDamageSamples.Clear();
        _towerDefenseAutoplayEmotionDamageSamples.Clear();
        _towerDefenseAutoplayFlowSamples.Clear();
        _towerDefenseAutoplayEconomySamples.Clear();
        _towerDefenseAutoplayEconomyStress = 0f;
        _towerDefenseAutoplayNearBasePressureSince = float.NegativeInfinity;
        _towerDefenseAutoplayNearBaseCrisis = 0f;
        _towerDefenseAutoplayNearBaseInstantRisk = 0f;
        _towerDefenseAutoplayImmediateCoreBreach = false;
        _towerDefenseAutoplayImmediateCoreThreatCellIndex = -1;
        _towerDefenseAutoplayImmediateCoreThreatPressure = 0f;
        _towerDefenseAutoplayStyleDecisionSequence = 0;
        _towerDefenseAutoplayStyleRollSequence = -1;
        _towerDefenseAutoplayStyleSaveRoll = 0u;
        _towerDefenseAutoplayStyleControlRoll = 0u;
        _towerDefenseAutoplayStyleRoleRoll = 0u;
        _towerDefenseAutoplayStyleRandom = null;
        _towerDefenseAutoplayStyleSaveRatioScale = 1f;
        _towerDefenseAutoplayStyleControlRatioScale = 1f;
        _towerDefenseAutoplayStyleRoleRatioScale = 1f;
        _towerDefenseAutoplayLastRealNearBaseRisk = 0f;
        _towerDefenseAutoplayLastRealNearBaseRiskAt = float.NegativeInfinity;
        _towerDefenseAutoplayEnemyFlowBacklog = 0f;
        _towerDefenseAutoplayBossReadinessUrgency = 1f;
        _towerDefenseAutoplayBossPowerDeficit = 0f;
        _towerDefenseAutoplayBossControlDeficit = 0f;
        _towerDefenseAutoplayBossCombatNeed = 0f;
        _towerDefenseAutoplayBossRequiredPower = 1f;
        _towerDefenseAutoplayHeatmapUpdatedAt = float.NegativeInfinity;
        _towerDefenseAutoplayHeatmapRevision = 0;
        _towerDefenseAutoplayHotspotRevision = -1;
        _towerDefenseAutoplaySustainedNearBaseCrisis = false;
        _towerDefenseAutoplaySustainedMainTowerDamage = false;
        _towerDefenseAutoplayManualSpeechProtectedUntil = float.NegativeInfinity;
        _towerDefenseAutoplayMainTowerEverDamagedThisSession = false;
        _towerDefenseAutoplayLastMainTowerHitDialogueGameTime =
            float.NegativeInfinity;
        _towerDefenseAutoplayLastMainTowerBurstDialogueGameTime =
            float.NegativeInfinity;
        _towerDefenseAutoplayLastBuildDialogueGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayLastUpgradeDialogueGameTime = float.NegativeInfinity;
        ResetAutoplayPressureTransitionTracking();
        _towerDefenseAutoplayLastPressureReliefDialogueGameTime =
            float.NegativeInfinity;
        ResetAutoplayEmotionTracking();
        _towerDefenseAutoplayLastEmotionDialogueGameTime =
            float.NegativeInfinity;
        _towerDefenseAutoplayThoughtRevision++;
        _towerDefenseAutoplayLastLoggedDecision = string.Empty;
        _towerDefenseAutoplayLastDecision = _towerDefenseAutoplayEnabled
            ? $"{TowerDefenseAutoplayCharacterName}正在重新建立战场上下文。"
            : "托管未启用";
    }

    public void ClearTowerDefenseAutoplaySessionState()
    {
        DisposeTowerDefenseAutoplayPlanner();
        RestoreAllAutoplayBossPriorityOverrides();
        _towerDefenseAutoplayOwnedTowers.Clear();
        _towerDefenseAutoplayOwnedTowerBuildTimes.Clear();
        ResetAutoplayTowerPerformanceObservations();
        _towerDefenseAutoplayLastSaleGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayLastCapitalActionGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayCapitalHoldActive = false;
        _towerDefenseAutoplayCapitalHoldUntilGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayCapitalHoldCooldownUntilGameTime =
            float.NegativeInfinity;
        _towerDefenseAutoplayLastChargeGameTime = float.NegativeInfinity;
        _towerDefenseAutoplayExpansionBaselineTowerCount = 0;
        _towerDefenseAutoplayNextExpansionGameTime = float.PositiveInfinity;
        _towerDefenseAutoplayStrategyMode = AutoplayStrategyMode.Opening;
        _towerDefenseAutoplayStrategyModeSince = Mathf.Max(0f, _survivalTime);
        ClearTowerDefenseAutoplayPriorCache();
        ClearTowerDefenseAutoplayDialogueSession();
        _towerDefenseAutoplayTensionTarget = 0.08f;
    }

    private void ResetAutoplayTowerPerformanceObservations()
    {
        _towerDefenseAutoplayTowerObservations.Clear();
        _towerDefenseAutoplayStaleTowerObservations.Clear();
        _towerDefenseAutoplayTowerPerformances.Clear();
        Array.Clear(_towerDefenseAutoplayLastTowerDamageFixed, 0,
            _towerDefenseAutoplayLastTowerDamageFixed.Length);
        Array.Clear(_towerDefenseAutoplayRecentDamageRateByType, 0,
            _towerDefenseAutoplayRecentDamageRateByType.Length);
        Array.Clear(_towerDefenseAutoplayDamageDeltaByType, 0,
            _towerDefenseAutoplayDamageDeltaByType.Length);
        Array.Clear(_towerDefenseAutoplayPerformanceWeightByType, 0,
            _towerDefenseAutoplayPerformanceWeightByType.Length);
        _towerDefenseAutoplayTowerObservationInitialized = false;
        _towerDefenseAutoplayLastTowerObservationAt = float.NegativeInfinity;
        _towerDefenseAutoplayLastObservedGoldEarned = 0;
        _towerDefenseAutoplayProvisionalSupportLeader = null;
        _towerDefenseAutoplaySupportLeaderSince = float.NegativeInfinity;
        _towerDefenseAutoplaySupportLeaderScore = 0f;
    }

    private void RebaselineAutoplayTowerPerformanceSampling()
    {
        // Damage totals continue while the player has control. Do not attribute that
        // inactive interval to whichever towers happen to exist when autoplay returns.
        _towerDefenseAutoplayTowerObservationInitialized = false;
        _towerDefenseAutoplayLastTowerObservationAt = float.NegativeInfinity;
        Array.Clear(_towerDefenseAutoplayRecentDamageRateByType, 0,
            _towerDefenseAutoplayRecentDamageRateByType.Length);
        Array.Clear(_towerDefenseAutoplayDamageDeltaByType, 0,
            _towerDefenseAutoplayDamageDeltaByType.Length);
        _towerDefenseAutoplayProvisionalSupportLeader = null;
        _towerDefenseAutoplaySupportLeaderSince = float.NegativeInfinity;
        _towerDefenseAutoplaySupportLeaderScore = 0f;
    }

    /// <summary>
    /// Intended for the F6/UI wiring. Camera transitions are deliberately left to the
    /// existing tower-defense input and observation partials.
    /// </summary>
    public void ToggleTowerDefenseAutoplay()
    {
        SetTowerDefenseAutoplayEnabled(!_towerDefenseAutoplayEnabled);
    }

    public void SetTowerDefenseAutoplayEnabled(bool enabled)
    {
        if (_towerDefenseAutoplayEnabled == enabled) return;

        float gameTime = Mathf.Max(0f, _survivalTime);
        bool userRelease = !enabled &&
            !_towerDefenseAutoplayConclusionStopping &&
            !_towerDefenseGameOver && !_towerDefenseVictory;
        bool firstUserRelease = userRelease &&
            !_towerDefenseAutoplayEverReleasedThisSession;
        _towerDefenseAutoplaySessionToggleCount++;
        _towerDefenseAutoplayRapidToggleStreak =
            gameTime - _towerDefenseAutoplayLastToggleGameTime <= 8f
                ? _towerDefenseAutoplayRapidToggleStreak + 1
                : 1;
        _towerDefenseAutoplayLastToggleGameTime = gameTime;
        InvalidateTowerDefenseAutoplayPlan();
        _towerDefenseAutoplayEnabled = enabled;
        _towerDefenseAutoplayTickAccumulator = 0f;
        _towerDefenseAutoplayCleanView = false;
        _towerDefenseAutoplayEntrancePending = false;
        ResetAutoplayPressureTransitionTracking();
        ResetAutoplayEmotionTracking();
        ResetTowerDefenseAutoplayPortraitInteraction();

        if (enabled)
        {
            HideF2MainTowerHealth();
            PruneAutoplayTowerList();
            RebaselineAutoplayTowerPerformanceSampling();
            _towerDefenseAutoplayMainTowerDamageSamples.Clear();
            _towerDefenseAutoplayBuildCursor =
                CountAutoplayStandardTowers() % TowerDefenseAutoplayBuildOrder.Length;
            ResetAutoplayExpansionSchedule(CountAutoplayStandardTowers(), gameTime);
            RougeTowerDefenseMap takeoverMap = RougeTowerDefenseMapLoader.ActiveMap;
            AutoplayDialogueCategory battleCategory;
            if (takeoverMap != null)
            {
                AutoplayBattleSnapshot takeoverSnapshot =
                    BuildAutoplayBattleSnapshot(takeoverMap, true);
                _towerDefenseAutoplayTensionTarget =
                    CalculateTowerDefenseAutoplayTension(takeoverSnapshot);
                battleCategory = GetAutoplayBattleDialogueCategory(
                    takeoverSnapshot);
            }
            else
            {
                battleCategory = GetAutoplayImmediateBattleDialogueCategory();
            }
            AutoplayDialogueCategory takeoverCategory =
                SelectAutoplayTakeoverCategory(gameTime);
            _towerDefenseAutoplayEverEnabledThisSession = true;
            _towerDefenseAutoplayLastBattleDialogueCategory = battleCategory;
            _towerDefenseAutoplayHasBattleDialogueCategory = true;
            bool canSpeakTakeover = gameTime >=
                    _towerDefenseAutoplayNextDialogueGameTime ||
                gameTime - _towerDefenseAutoplayLastDialogueGameTime >= 2f;
            _towerDefenseAutoplayEntrancePending = canSpeakTakeover;
            if (canSpeakTakeover)
            {
                _towerDefenseAutoplayEntranceLine =
                    PickAutoplayDialogueLine(takeoverCategory);
                PresentTowerDefenseAutoplaySpeech(_towerDefenseAutoplayEntranceLine);
                RegisterAutoplayDialogueTiming(
                    GetAutoplayDialoguePriority(takeoverCategory));
                SetAutoplayDecision(
                    $"{TowerDefenseAutoplayCharacterName}：“{_towerDefenseAutoplayEntranceLine}”",
                    true);
                QueueAutoplayDialogue(battleCategory);
            }
            else
            {
                SetAutoplayDecision("托管重新接管：沿用本局记忆并复核当前敌压。", true);
            }
        }
        else
        {
            RestoreAllAutoplayBossPriorityOverrides();
            _towerDefenseAutoplayMainTowerDamageSamples.Clear();
            _towerDefenseAutoplayLastExitGameTime = gameTime;
            if (userRelease)
            {
                if (firstUserRelease)
                    _towerDefenseAutoplayEverReleasedThisSession = true;
                string releaseLine = PickAutoplayDialogueLine(
                    AutoplayDialogueCategory.ReleaseFirst);
                RegisterAutoplayDialogueTiming(
                    GetAutoplayDialoguePriority(
                        AutoplayDialogueCategory.ReleaseFirst));
                BeginTowerDefenseAutoplayReleasePresentation(releaseLine);
                SetAutoplayDecision(
                    $"{TowerDefenseAutoplayCharacterName}：“{releaseLine}”", true);
                if (IsTiltShiftObservationActive)
                    _towerDefenseAutoplayPendingReleaseToastLine = releaseLine;
                RougeCameraModeToast.Show(
                    TowerDefenseAutoplayCharacterName + " // " + releaseLine,
                    ActiveCommanderVisualTheme.Accent);
            }
            else
            {
                SetAutoplayDecision(
                    "托管已关闭：指挥权交还，本局战场记忆保留。", true);
            }
        }

        RefreshTowerDefenseUi(true);
        RefreshTowerDefenseAutoplayPresentation();
    }

    public void SetAutoplayCleanView(bool cleanView)
    {
        bool next = _towerDefenseAutoplayEnabled && cleanView;
        if (_towerDefenseAutoplayCleanView == next) return;
        _towerDefenseAutoplayCleanView = next;
        RefreshTowerDefenseUi(true);
        RefreshTowerDefenseAutoplayPresentation();
    }

    public void ToggleAutoplayCleanView()
    {
        if (!_towerDefenseAutoplayEnabled) return;
        SetAutoplayCleanView(!_towerDefenseAutoplayCleanView);
    }

    public void AcknowledgeTowerDefenseAutoplayEntrance()
    {
        _towerDefenseAutoplayEntrancePending = false;
        RefreshTowerDefenseAutoplayPresentation();
    }

    private void PresentTowerDefenseAutoplaySpeech(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        _towerDefenseAutoplayEntranceLine = line.Trim();
        _towerDefenseAutoplayEntrancePending = true;
        _towerDefenseAutoplayEntranceRevision++;
        float readingSeconds = Mathf.Clamp(2.7f +
            _towerDefenseAutoplayEntranceLine.Length * 0.075f, 4.2f, 6.5f);
        _towerDefenseAutoplaySpeechVisibleUntil =
            Mathf.Max(0f, _survivalTime) + readingSeconds;
        RefreshTowerDefenseAutoplayPresentation();
    }

    private void ShowPendingTowerDefenseAutoplayReleaseToast()
    {
        if (string.IsNullOrWhiteSpace(
                _towerDefenseAutoplayPendingReleaseToastLine)) return;
        RougeCameraModeToast.Show(
            TowerDefenseAutoplayCharacterName + " // " +
            _towerDefenseAutoplayPendingReleaseToastLine,
            ActiveCommanderVisualTheme.Accent);
        _towerDefenseAutoplayPendingReleaseToastLine = string.Empty;
    }

    /// <summary>
    /// Convenience overload for the normal game loop. Time.deltaTime is scaled time,
    /// so the planner cadence follows game time rather than wall-clock time.
    /// </summary>
    public void UpdateTowerDefenseAutoplay()
    {
        UpdateTowerDefenseAutoplay(Time.deltaTime);
    }

    /// <summary>
    /// Advances the local controller. A decision tick returns immediately after its
    /// first successful gameplay action, guaranteeing at most one action per tick.
    /// </summary>
    public void UpdateTowerDefenseAutoplay(float scaledGameDeltaTime)
    {
        if (!_towerDefenseAutoplayEnabled) return;

        if (!CanRunTowerDefenseAutoplay(out string pauseReason))
        {
            _towerDefenseAutoplayTickAccumulator = 0f;
            SetAutoplayDecision(pauseReason, false);
            return;
        }

        // Target-priority switching is a realtime combat order, not a capital
        // decision. Check it every rendered frame so a tower does not wait for the
        // next planner result after the boss crosses its range boundary.
        if (TryApplyAutoplayBossTargeting(out string bossDecision))
            SetAutoplayDecision(bossDecision, true);

        // Poll the worker every rendered frame. IsCompleted is non-blocking; the
        // main thread only consumes a plan after all Burst jobs have finished.
        if (_towerDefenseAutoplayPlanScheduled)
        {
            RunTowerDefenseAutoplayDecision();
            return;
        }

        _towerDefenseAutoplayTickAccumulator += Mathf.Max(0f, scaledGameDeltaTime);
        if (_towerDefenseAutoplayTickAccumulator + 0.00001f <
            TowerDefenseAutoplayTickSeconds) return;

        // Do not accumulate a large action burst after a slow frame. One decision is
        // made now; at most one interval is retained for the next rendered frame.
        _towerDefenseAutoplayTickAccumulator -= TowerDefenseAutoplayTickSeconds;
        _towerDefenseAutoplayTickAccumulator = Mathf.Min(
            _towerDefenseAutoplayTickAccumulator, TowerDefenseAutoplayTickSeconds);
        RunTowerDefenseAutoplayDecision();
    }

    private bool CanRunTowerDefenseAutoplay(out string pauseReason)
    {
        if (!_initialized || !_towerDefenseInitialized || !towerDefenseEnabled)
        {
            pauseReason = "等待塔防系统初始化。";
            return false;
        }
        if (_towerDefenseSceneReloadRequested)
        {
            pauseReason = "场景正在重载，托管暂不下达命令。";
            return false;
        }
        if (_towerDefenseGameOver || _towerDefenseVictory)
        {
            pauseReason = _towerDefenseVictory
                ? $"任务已完成，{TowerDefenseAutoplayCharacterName}停止下达新命令。"
                : $"主塔防线已失守，{TowerDefenseAutoplayCharacterName}停止下达新命令。";
            return false;
        }
        if (mainTower != null && mainTower.IsDestroyed)
        {
            pauseReason =
                $"主塔已经失守，{TowerDefenseAutoplayCharacterName}停止下达新命令。";
            return false;
        }
        if (_towerDefenseStartupActive)
        {
            pauseReason = "等待开场演出结束。";
            return false;
        }
        if (IsPlayerSettingsOpen ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            pauseReason = "设置界面打开，托管保持待机。";
            return false;
        }
        if (IsCameraViewTransitionPaused)
        {
            pauseReason = "观赏镜头过渡中，等待游戏时间恢复。";
            return false;
        }
        if (_bossDeathSequenceActive)
        {
            pauseReason = "Boss 击破演出中，托管保持观战。";
            return false;
        }
        if (_towerPlacementMode || _towerPreview != null || _towerRelocationActive ||
            _chargeTowerTargetSelectionActive || _chargeTowerEffectSelectionActive ||
            HasTacticalSkillSelection)
        {
            pauseReason =
                $"玩家正在进行塔楼操作，{TowerDefenseAutoplayCharacterName}避免同时修改战场。";
            return false;
        }
        if (RougeTowerDefenseMapLoader.ActiveMap == null)
        {
            pauseReason = "等待塔防地图载入。";
            return false;
        }

        // Tilt-shift/F2 observation is intentionally allowed: autoplay owns that view.
        pauseReason = string.Empty;
        return true;
    }

    private void RunTowerDefenseAutoplayDecision()
    {
        PruneAutoplayTowerList();

        if (_towerDefenseAutoplayPlanScheduled)
        {
            if (!TryConsumeTowerDefenseAutoplayPlan(out RougeTowerDefenseMap map,
                    out AutoplayBattleSnapshot snapshot))
                return;
            RunTowerDefenseAutoplayResolvedDecision(map, snapshot);
            _towerDefenseAutoplayPlanResultsReady = false;
            return;
        }

        RougeTowerDefenseMap activeMap = RougeTowerDefenseMapLoader.ActiveMap;
        AutoplayBattleSnapshot baseSnapshot =
            BuildAutoplayBattleSnapshot(activeMap, false);
        if (ScheduleTowerDefenseAutoplayPlan(activeMap, baseSnapshot)) return;

        // Native storage can be unavailable during an unusual teardown frame. Keep
        // a safe synchronous fallback instead of issuing an action from empty data.
        RunTowerDefenseAutoplayResolvedDecision(activeMap,
            BuildAutoplayBattleSnapshot(activeMap, true));
    }

    private void RunTowerDefenseAutoplayResolvedDecision(
        RougeTowerDefenseMap map, AutoplayBattleSnapshot snapshot)
    {

        bool liveBoss = TryGetAutoplayLiveBossTarget(out _, out _);
        // Spatial pressure intentionally ignores enemies outside the authored grid,
        // but strategy state must still trust the authoritative live Boss handle.
        // Otherwise an edge-of-map Boss can leave the commander in PrepareBoss even
        // though the encounter is already active.
        if (liveBoss && snapshot.BossEnemies <= 0)
        {
            snapshot.BossEnemies = 1;
            snapshot.ActiveEnemies = Mathf.Max(1, snapshot.ActiveEnemies);
        }
        if (liveBoss && !_towerDefenseAutoplayObservedLiveBoss)
        {
            _towerDefenseAutoplayObservedLiveBoss = true;
            _towerDefenseAutoplayObservedBossHealthWarning = false;
            _towerDefenseAutoplayObservedBossHealthCritical = false;
            _towerDefenseAutoplayObservedBossHealthFinal = false;
            EmitTowerDefenseAutoplayEventDialogue(
                AutoplayDialogueCategory.BossArrival);
        }
        else if (liveBoss)
        {
            UpdateAutoplayBossHealthDialogue();
        }
        else if (!liveBoss && !_bossDeathSequenceActive)
        {
            _towerDefenseAutoplayObservedLiveBoss = false;
            _towerDefenseAutoplayObservedBossHealthWarning = false;
            _towerDefenseAutoplayObservedBossHealthCritical = false;
            _towerDefenseAutoplayObservedBossHealthFinal = false;
        }

        UpdateAutoplayEnemyFlowHeatmap(map, ref snapshot);
        _towerDefenseAutoplayImmediateCoreBreach =
            TryGetAutoplayImmediateCoreBreach(map,
                out _towerDefenseAutoplayImmediateCoreThreatCellIndex,
                out _towerDefenseAutoplayImmediateCoreThreatPressure);
        _towerDefenseAutoplayTensionTarget =
            CalculateTowerDefenseAutoplayTension(snapshot);
        UpdateAutoplayEmotionDialogue(_towerDefenseAutoplayTensionTarget);
        UpdateTowerDefenseAutoplayDialogue(snapshot);

        int standardTowerCount = CountAutoplayStandardTowers();
        int buildCellCount = CountAutoplayBuildCells(map);
        int freeBuildCellCount = CountOpenAutoplayBuildCells(map);
        float decisionGameTime = Mathf.Max(0f, _survivalTime);
        AdvanceAutoplayExpansionSchedule(standardTowerCount, decisionGameTime);
        UpdateAutoplayTowerPerformanceObservations(map);
        float mainTowerHealthRatio = mainTower != null && mainTower.maxHealth > 0.001f
            ? Mathf.Clamp01(mainTower.CurrentHealth / mainTower.maxHealth)
            : 1f;
        UpdateAutoplayStrategyMode(snapshot, standardTowerCount,
            mainTowerHealthRatio, _towerDefenseAutoplayImmediateCoreBreach);
        UpdateAutoplayBossReadinessUrgency(map, snapshot, liveBoss);

        float actionInterval = _towerDefenseAutoplayStrategyMode ==
                               AutoplayStrategyMode.Emergency
            ? TowerDefenseAutoplayEmergencyActionInterval
            : TowerDefenseAutoplayCapitalActionInterval;
        if (Mathf.Max(0f, _survivalTime) -
            _towerDefenseAutoplayLastCapitalActionGameTime < actionInterval)
            return;

        float tacticalNearBaseRisk =
            GetAutoplayTacticalNearBaseRisk(decisionGameTime);
        bool scheduledExpansionOpportunity = decisionGameTime >=
                                             _towerDefenseAutoplayNextExpansionGameTime;
        int desiredTowerCount = Mathf.Min(buildCellCount,
            _towerDefenseAutoplayExpansionBaselineTowerCount +
            (scheduledExpansionOpportunity ? 1 : 0));
        bool bossCombatMarketOpen = IsAutoplayBossCombatMarketOpen(snapshot);
        bool missingFunctionGroup = HasMissingEnabledAutoplayFunctionGroup();
        bool canExpand = freeBuildCellCount > 0;
        bool hasUncoveredNearBaseHotspot =
            HasAutoplayUncoveredNearBaseHotspot(map);
        bool hasUncoveredLane = TryGetAutoplayLaneDefenseGap(map, snapshot,
            out _, out _);
        bool emergencyRecoveryBuildWindow =
            _towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Emergency &&
            _towerDefenseAutoplaySustainedMainTowerDamage &&
            scheduledExpansionOpportunity &&
            !hasUncoveredNearBaseHotspot;
        EvaluateAutoplayBuildChoices(map, snapshot,
            emergencyRecoveryBuildWindow,
            out AutoplayBuildChoice bestBuild,
            out AutoplayBuildChoice affordableBuild,
            out AutoplayBuildChoice emergencyBuild);
        bool bossInvestmentStage = IsAutoplayBossInvestmentStage(snapshot);
        // Opening is a route-safety state. It establishes dependable damage on the
        // observed entrances, then hands composition to the commander's configured
        // style instead of forcing every commander through the same three-role script.
        bool expansionDue = scheduledExpansionOpportunity && canExpand &&
                            standardTowerCount < desiredTowerCount;
        bool closesUncoveredNearBaseHeat =
            DoesAutoplayBuildCloseUncoveredNearBaseHotspot(map,
                emergencyBuild);
        bool defensiveBuildOpportunity = hasUncoveredNearBaseHotspot &&
                                         closesUncoveredNearBaseHeat;
        bool reserveImmediateDefense = defensiveBuildOpportunity &&
            emergencyBuild.PaidCost <= _towerDefenseGold;
        int immediateDefenseCapitalReserve = reserveImmediateDefense
            ? emergencyBuild.PaidCost
            : 0;
        // Only a concrete, already-detected defense purchase reserves cash. Saving
        // personality is represented by ROI sensitivity and hold quality below; it
        // must not hide otherwise executable builds behind an arbitrary cash floor.
        int capitalReserve = immediateDefenseCapitalReserve;
        int maximumCoreUpgradeCost = _towerDefenseGold >=
                                     immediateDefenseCapitalReserve
            ? _towerDefenseGold - immediateDefenseCapitalReserve
            : -1;
        EvaluateAutoplayUpgradeChoices(map, snapshot,
            _towerDefenseAutoplayImmediateCoreBreach
                ? _towerDefenseAutoplayImmediateCoreThreatCellIndex
                : emergencyBuild.IsValid
                    ? emergencyBuild.NearBaseHeatCellIndex
                    : -1,
            maximumCoreUpgradeCost,
            out AutoplayUpgradeChoice bestUpgrade,
            out AutoplayUpgradeChoice affordableUpgrade,
            out AutoplayUpgradeChoice affordableCoreUpgrade,
            out AutoplayUpgradeChoice bestHeatUpgrade,
            out AutoplayUpgradeChoice affordableHeatUpgrade);
        EvaluateAutoplaySupportChoices(map, snapshot,
            out AutoplaySupportChoice bestSupport,
            out AutoplaySupportChoice affordableSupport);
        EvaluateAutoplayChargeChoice(map, out AutoplayChargeChoice chargeChoice);

        AutoplayBuildChoice immediateCoreFirepowerBuild = default;
        AutoplayUpgradeChoice immediateCoreFirepowerUpgrade = default;
        float immediateCoreBuildScore = float.NegativeInfinity;
        float immediateCoreUpgradeScore = float.NegativeInfinity;
        if (_towerDefenseAutoplayImmediateCoreBreach)
        {
            immediateCoreFirepowerBuild =
                SelectAutoplayImmediateCoreFirepowerBuild(map, snapshot,
                    missingFunctionGroup, out immediateCoreBuildScore);
            immediateCoreFirepowerUpgrade =
                SelectAutoplayImmediateCoreFirepowerUpgrade(map,
                    out immediateCoreUpgradeScore);
        }

        // A real non-Boss hotspot remains an immediate tactical obligation. When the
        // Emergency state was caused by damage but no such hotspot exists, a live or
        // imminent Boss route—or a scheduled recovery window—may fall through to the
        // same marginal-gain auction instead of fabricating a generic placement.
        if (_towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Emergency)
        {
            _towerDefenseAutoplayCapitalHoldActive = false;
            bool emergencyBuildCoversHeat = hasUncoveredNearBaseHotspot &&
                canExpand && closesUncoveredNearBaseHeat;
            bool heatDefenseNeeded = hasUncoveredNearBaseHotspot ||
                                     _towerDefenseAutoplayImmediateCoreBreach;
            // Inside two cells, cash is combat time. Ignore saving personality and
            // unaffordable premium plans: buy the strongest affordable direct-power
            // increase that can actually hit the current core threat.
            if (_towerDefenseAutoplayImmediateCoreBreach)
            {
                bool preferImmediateUpgrade =
                    immediateCoreFirepowerUpgrade.IsValid &&
                    (!immediateCoreFirepowerBuild.IsValid ||
                     immediateCoreUpgradeScore >= immediateCoreBuildScore);
                if (preferImmediateUpgrade &&
                    TryUpgradeAutoplayTower(immediateCoreFirepowerUpgrade,
                        out string coreFirepowerUpgradeDecision))
                {
                    SetAutoplayDecision("两格火力接管：" +
                        coreFirepowerUpgradeDecision, true);
                    return;
                }
                if (immediateCoreFirepowerBuild.IsValid &&
                    TryBuildAutoplayStandardTower(map,
                        immediateCoreFirepowerBuild, "两格火力接管",
                        out string coreFirepowerBuildDecision))
                {
                    SetAutoplayDecision(coreFirepowerBuildDecision, true);
                    return;
                }
                if (!preferImmediateUpgrade &&
                    immediateCoreFirepowerUpgrade.IsValid &&
                    TryUpgradeAutoplayTower(immediateCoreFirepowerUpgrade,
                        out string fallbackCoreFirepowerUpgradeDecision))
                {
                    SetAutoplayDecision("两格火力接管：" +
                        fallbackCoreFirepowerUpgradeDecision, true);
                    return;
                }
            }
            // An enemy already inside the two-cell core band is a forecasted impact,
            // not a reason to wait for main-tower damage. Consolidate an installed
            // near defense before spending the same money on another level-one tower.
            if (_towerDefenseAutoplayImmediateCoreBreach &&
                affordableHeatUpgrade.IsValid &&
                ShouldPreferAutoplayCoreUpgrade(affordableHeatUpgrade,
                    emergencyBuildCoversHeat ? emergencyBuild : default,
                    snapshot, missingFunctionGroup, false) &&
                TryUpgradeAutoplayTower(affordableHeatUpgrade,
                    out string immediateCoreUpgradeDecision))
            {
                SetAutoplayDecision(
                    $"近端加固（{TowerDefenseAutoplayImmediateCoreDefenseCells:0}格警戒）：" +
                    immediateCoreUpgradeDecision, true);
                return;
            }
            if (_towerDefenseAutoplayImmediateCoreBreach &&
                bestHeatUpgrade.IsValid &&
                ShouldPreferAutoplayCoreUpgrade(bestHeatUpgrade,
                    emergencyBuildCoversHeat ? emergencyBuild : default,
                    snapshot, missingFunctionGroup, true) &&
                TryLiquidateOuterAutoplayTowerForCoreUpgrade(map, snapshot,
                    bestHeatUpgrade,
                    _towerDefenseAutoplayImmediateCoreThreatCellIndex,
                    out string coreLiquidationDecision))
            {
                SetAutoplayDecision(coreLiquidationDecision, true);
                return;
            }
            if (heatDefenseNeeded && affordableHeatUpgrade.IsValid &&
                affordableHeatUpgrade.PaidCost <= 0 &&
                TryUpgradeAutoplayTower(affordableHeatUpgrade,
                    out string freeEmergencyHeatUpgradeDecision))
            {
                SetAutoplayDecision(freeEmergencyHeatUpgradeDecision, true);
                return;
            }
            bool affordableEmergencyBuild = emergencyBuildCoversHeat &&
                emergencyBuild.PaidCost <= _towerDefenseGold;
            bool preferEmergencyHeatUpgrade = affordableEmergencyBuild &&
                affordableHeatUpgrade.IsValid &&
                GetAutoplayEmergencyPurchaseScore(
                    GetAutoplayUpgradeCapitalGain(affordableHeatUpgrade),
                    affordableHeatUpgrade.PaidCost) >
                GetAutoplayEmergencyPurchaseScore(
                    GetAutoplayBuildCapitalGain(emergencyBuild, snapshot,
                        missingFunctionGroup), emergencyBuild.PaidCost) * 1.05f;
            if (preferEmergencyHeatUpgrade &&
                TryUpgradeAutoplayTower(affordableHeatUpgrade,
                    out string strongerEmergencyHeatUpgradeDecision))
            {
                SetAutoplayDecision(strongerEmergencyHeatUpgradeDecision, true);
                return;
            }
            if (affordableEmergencyBuild &&
                TryBuildAutoplayStandardTower(map, emergencyBuild, "紧急守家",
                    out string emergencyBuildDecision))
            {
                SetAutoplayDecision(emergencyBuildDecision, true);
                return;
            }
            if (heatDefenseNeeded && affordableHeatUpgrade.IsValid &&
                TryUpgradeAutoplayTower(affordableHeatUpgrade,
                    out string emergencyHeatUpgradeDecision))
            {
                SetAutoplayDecision(emergencyHeatUpgradeDecision, true);
                return;
            }
            if (emergencyBuildCoversHeat &&
                emergencyBuild.PaidCost > _towerDefenseGold)
            {
                SetAutoplayDecision(DescribeAutoplaySavingPlan(emergencyBuild,
                    "紧急守家：为当前未覆盖的敌潮热点留钱"), false);
                return;
            }
            if (heatDefenseNeeded ||
                (!bossCombatMarketOpen && !emergencyRecoveryBuildWindow &&
                 !hasUncoveredLane))
            {
                if (affordableUpgrade.IsValid &&
                    TryUpgradeAutoplayTower(affordableUpgrade,
                        out string emergencyUpgradeDecision))
                {
                    SetAutoplayDecision(emergencyUpgradeDecision, true);
                    return;
                }
                SetAutoplayDecision(bestUpgrade.IsValid
                    ? DescribeAutoplaySavingPlan(bestUpgrade)
                    : "紧急守家：当前没有可执行的热点建造或升级目标。", false);
                return;
            }
        }

        // Stable play has one capital market. Route gaps, role coverage, special
        // tiles, geometry, current pressure and upgrades all contribute continuous
        // marginal value; none of them gets to exclude another action by script.
        // Emergency handling above is the only tactical hard override.
        bool buildWindow = canExpand && bestBuild.IsValid;
        bool restrictBuildToDefenseTarget = false;
        AutoplayBuildChoice restrictedBuild = default;
        bool stableSupportWindow = bestSupport.IsValid &&
            bestSupport.CoversProvenLeader &&
            bestSupport.ObservationConfidence >= 0.999f &&
            bestSupport.AffectedTowers >= 3 &&
            bestSupport.HighValueTowers >= 1 && freeBuildCellCount >= 2 &&
            mainTowerHealthRatio >= 0.68f &&
            tacticalNearBaseRisk < TowerDefenseAutoplayNearBaseEarlyWarning &&
            !_towerDefenseAutoplaySustainedMainTowerDamage &&
            _towerDefenseAutoplayEnemyFlowBacklog < 0.2f;
        bool stableChargeWindow = chargeChoice.IsValid && freeBuildCellCount >= 2 &&
            mainTowerHealthRatio >= 0.8f && snapshot.BossEnemies <= 0 &&
            snapshot.BossPreparation < TowerDefenseAutoplayThresholds
                .mediumBossPreparation &&
            tacticalNearBaseRisk < TowerDefenseAutoplayNearBaseEarlyWarning &&
            !_towerDefenseAutoplaySustainedMainTowerDamage &&
            _towerDefenseAutoplayEnemyFlowBacklog < 0.2f &&
            decisionGameTime - _towerDefenseAutoplayLastChargeGameTime >=
                TowerDefenseAutoplayChargeCooldownSeconds;
        bool allowSafeCapitalHold = capitalReserve <= 0 &&
            mainTowerHealthRatio >= 0.68f &&
            tacticalNearBaseRisk < TowerDefenseAutoplayNearBaseEarlyWarning &&
            !_towerDefenseAutoplaySustainedMainTowerDamage &&
            _towerDefenseAutoplayEnemyFlowBacklog < 0.35f &&
            snapshot.UrgentPressure < TowerDefenseAutoplayThresholds
                .redeployMaximumUrgentPressure &&
            snapshot.BossEnemies <= 0;
        bool allowDefensiveShortHold = defensiveBuildOpportunity &&
            emergencyBuild.PaidCost > _towerDefenseGold &&
            !_towerDefenseAutoplaySustainedMainTowerDamage &&
            tacticalNearBaseRisk < TowerDefenseAutoplayEmergencyNearBaseCrisis;
        bool allowShortCapitalHold = allowSafeCapitalHold ||
                                     allowDefensiveShortHold;
        bool defensiveHoldOnly = !allowSafeCapitalHold &&
                                 allowDefensiveShortHold;

        if (TryGetBestAutoplayFreeUpgrade(out AutoplayUpgradeChoice freeUpgrade) &&
            TryUpgradeAutoplayTower(freeUpgrade,
                out string freeUpgradeDecision))
        {
            if (expansionDue) DeferAutoplayExpansionSchedule(decisionGameTime);
            SetAutoplayDecision(freeUpgradeDecision, true);
            return;
        }

        AutoplayCapitalActionKind capitalAction = SelectAutoplayCapitalAction(
            snapshot, buildWindow, missingFunctionGroup, capitalReserve,
            restrictedBuild, restrictBuildToDefenseTarget,
            stableSupportWindow, bestSupport,
            stableChargeWindow,
            chargeChoice, allowShortCapitalHold, defensiveHoldOnly,
            affordableCoreUpgrade,
            out AutoplayBuildChoice capitalBuild,
            out AutoplayUpgradeChoice capitalUpgrade,
            out AutoplaySupportChoice capitalSupport,
            out AutoplayChargeChoice capitalCharge);

        if (capitalAction == AutoplayCapitalActionKind.Hold)
        {
            string holdDecision;
            if (capitalBuild.IsValid)
                holdDecision = DescribeAutoplaySavingPlan(capitalBuild,
                    "短期等待显著更强的建造方案");
            else if (capitalUpgrade.IsValid)
                holdDecision = DescribeAutoplaySavingPlan(capitalUpgrade);
            else if (capitalSupport.IsValid)
                holdDecision = DescribeAutoplaySupportSavingPlan(capitalSupport);
            else
                holdDecision = DescribeAutoplayChargeSavingPlan(capitalCharge);
            _towerDefenseAutoplayLastCapitalActionGameTime = decisionGameTime;
            SetAutoplayDecision(holdDecision, false);
            return;
        }

        if (capitalAction == AutoplayCapitalActionKind.Build &&
            TryBuildAutoplayStandardTower(map, capitalBuild,
                emergencyRecoveryBuildWindow ? "高压补线" : "价值扩建",
                out string expansionDecision))
        {
            SetAutoplayDecision(expansionDecision, true);
            return;
        }
        if (capitalAction == AutoplayCapitalActionKind.Upgrade &&
            TryUpgradeAutoplayTower(capitalUpgrade, out string upgradeDecision))
        {
            if (expansionDue) DeferAutoplayExpansionSchedule(decisionGameTime);
            SetAutoplayDecision(upgradeDecision, true);
            return;
        }
        if (capitalAction == AutoplayCapitalActionKind.Support &&
            TryBuildAutoplaySupportTower(map, capitalSupport,
                out string supportDecision))
        {
            if (expansionDue) DeferAutoplayExpansionSchedule(decisionGameTime);
            SetAutoplayDecision(supportDecision, true);
            return;
        }
        if (capitalAction == AutoplayCapitalActionKind.Charge &&
            TryBuildAutoplayChargeTower(map, capitalCharge,
                out string chargeDecision))
        {
            if (expansionDue) DeferAutoplayExpansionSchedule(decisionGameTime);
            SetAutoplayDecision(chargeDecision, true);
            return;
        }

        if (expansionDue)
        {
            bool superiorBuildPlan = !bossInvestmentStage && bestBuild.IsValid &&
                ShouldSaveForAutoplayBuild(bestBuild, affordableBuild) &&
                !affordableUpgrade.IsValid;
            if (superiorBuildPlan)
            {
                SetAutoplayDecision(DescribeAutoplaySavingPlan(bestBuild,
                    "扩建窗口已经开启，但只为明显更好的落点留钱"), false);
                return;
            }
            DeferAutoplayExpansionSchedule(decisionGameTime);
        }
        if (stableSupportWindow && ShouldReserveForAutoplaySupport(bestSupport,
                affordableSupport, mainTowerHealthRatio) &&
            !affordableUpgrade.IsValid && !affordableBuild.IsValid)
        {
            SetAutoplayDecision(DescribeAutoplaySupportSavingPlan(bestSupport), false);
            return;
        }

        // Redeployment is optimization, never a prerequisite for opening, upgrading
        // or an expansion that is already due.
        bool stableEnoughToRedeploy = standardTowerCount >=
              Mathf.Max(1, _towerDefenseAutoplayFunctionCounts.Length) +
              TowerDefenseAutoplayThresholds.redeployMinimumExtraTowers &&
            mainTowerHealthRatio >= TowerDefenseAutoplayThresholds
                .redeployMinimumHealthRatio &&
            snapshot.UrgentPressure < TowerDefenseAutoplayThresholds
                .redeployMaximumUrgentPressure &&
            snapshot.ActiveEnemies < TowerDefenseAutoplayThresholds
                .redeployMaximumActiveEnemies &&
            snapshot.BossEnemies <= 0 &&
            snapshot.BossPreparation < TowerDefenseAutoplayThresholds
                .redeployMaximumBossPreparation;
        if (stableEnoughToRedeploy &&
            TrySellMisplacedAutoplayTower(map, snapshot, bestBuild,
                standardTowerCount, out string saleDecision))
        {
            SetAutoplayDecision(saleDecision, true);
            return;
        }

        SetAutoplayDecision(bestUpgrade.IsValid
            ? DescribeAutoplaySavingPlan(bestUpgrade)
            : snapshot.ActiveEnemies > 0
                ? "当前没有正边际收益足够高的动作，继续观察敌潮。"
                : "当前没有正边际收益足够高的动作，等待下一次波次预测。", false);
    }

    private bool TryGetBestAutoplayFreeUpgrade(
        out AutoplayUpgradeChoice best)
    {
        best = default;
        for (int i = 0; i < _towerDefenseAutoplayUpgradeChoiceScratch.Count; i++)
        {
            AutoplayUpgradeChoice choice =
                _towerDefenseAutoplayUpgradeChoiceScratch[i];
            if (!choice.IsValid || choice.PaidCost > 0) continue;
            if (!best.IsValid || choice.ObjectiveUtility > best.ObjectiveUtility)
                best = choice;
        }
        if (!best.IsValid) return false;
        best.Utility = best.ObjectiveUtility;
        best.Efficiency = best.ObjectiveEfficiency;
        best.CapitalScore = best.ObjectiveCapitalScore;
        return true;
    }

    private AutoplayCapitalActionKind SelectAutoplayCapitalAction(
        AutoplayBattleSnapshot snapshot, bool buildWindow,
        bool missingFunctionGroup, int capitalReserve,
        AutoplayBuildChoice reservedDefenseBuild,
        bool restrictBuildToDefenseTarget,
        bool stableSupportWindow, AutoplaySupportChoice supportChoice,
        bool stableChargeWindow, AutoplayChargeChoice chargeChoice,
        bool allowShortHold, bool defensiveHoldOnly,
        AutoplayUpgradeChoice affordableCoreUpgrade,
        out AutoplayBuildChoice selectedBuild,
        out AutoplayUpgradeChoice selectedUpgrade,
        out AutoplaySupportChoice selectedSupport,
        out AutoplayChargeChoice selectedCharge)
    {
        selectedBuild = default;
        selectedUpgrade = default;
        selectedSupport = default;
        selectedCharge = default;
        int spendableGold = Mathf.Max(0, _towerDefenseGold - capitalReserve);
        int supportPaidCost = supportChoice.IsValid
            ? GetTowerDefenseAutoplayPaidCost(supportChoice.Cost)
            : 0;
        bool supportMarketOpen = stableSupportWindow && supportChoice.IsValid &&
            IsValidAutoplayCapitalGain(supportChoice.CapitalGain);
        bool supportEligible = supportMarketOpen &&
            supportPaidCost <= spendableGold &&
            IsValidAutoplayCapitalGain(supportChoice.CapitalGain);
        bool chargeMarketOpen = stableChargeWindow && chargeChoice.IsValid &&
            IsValidAutoplayCapitalGain(chargeChoice.CapitalGain);
        bool chargeEligible = chargeMarketOpen &&
            chargeChoice.PaidCost <= spendableGold &&
            IsValidAutoplayCapitalGain(chargeChoice.CapitalGain);

        float maximumRoi = 0f;
        float maximumGain = 0f;
        int minimumPositiveCost = int.MaxValue;
        if (buildWindow)
        {
            for (int i = 0; i < _towerDefenseAutoplayBuildChoiceScratch.Count; i++)
            {
                AutoplayBuildChoice choice =
                    _towerDefenseAutoplayBuildChoiceScratch[i];
                if (!choice.IsValid || restrictBuildToDefenseTarget &&
                    !IsSameAutoplayBuildAction(choice, reservedDefenseBuild))
                    continue;
                float gain = GetAutoplayBuildCapitalGain(choice, snapshot,
                    missingFunctionGroup);
                if (!IsValidAutoplayCapitalGain(gain)) continue;
                AccumulateAutoplayCapitalScale(gain, choice.PaidCost,
                    ref maximumRoi, ref maximumGain,
                    ref minimumPositiveCost);
            }
        }
        for (int i = 0; i < _towerDefenseAutoplayUpgradeChoiceScratch.Count; i++)
        {
            AutoplayUpgradeChoice choice =
                _towerDefenseAutoplayUpgradeChoiceScratch[i];
            if (!choice.IsValid) continue;
            float gain = GetAutoplayUpgradeCapitalGain(choice);
            if (!IsValidAutoplayCapitalGain(gain)) continue;
            AccumulateAutoplayCapitalScale(gain, choice.PaidCost,
                ref maximumRoi, ref maximumGain, ref minimumPositiveCost);
        }
        if (supportMarketOpen)
            AccumulateAutoplayCapitalScale(supportChoice.CapitalGain,
                supportPaidCost, ref maximumRoi, ref maximumGain,
                ref minimumPositiveCost);
        if (chargeMarketOpen)
            AccumulateAutoplayCapitalScale(
                chargeChoice.CapitalGain,
                chargeChoice.PaidCost, ref maximumRoi, ref maximumGain,
                ref minimumPositiveCost);

        if (maximumGain <= 0f)
        {
            _towerDefenseAutoplayCapitalHoldActive = false;
            return AutoplayCapitalActionKind.None;
        }
        int referenceCost = minimumPositiveCost == int.MaxValue
            ? 0
            : minimumPositiveCost * 2;
        float wealth = GetAutoplayCapitalWealth(spendableGold, referenceCost);
        float bestScore = float.NegativeInfinity;
        float bestUnaffordableScore = float.NegativeInfinity;
        AutoplayCapitalActionKind bestKind = AutoplayCapitalActionKind.None;
        AutoplayCapitalActionKind holdKind = AutoplayCapitalActionKind.None;
        AutoplayBuildChoice holdBuild = default;
        AutoplayUpgradeChoice holdUpgrade = default;
        AutoplaySupportChoice holdSupport = default;
        AutoplayChargeChoice holdCharge = default;
        if (buildWindow)
        {
            for (int i = 0; i < _towerDefenseAutoplayBuildChoiceScratch.Count; i++)
            {
                AutoplayBuildChoice choice =
                    _towerDefenseAutoplayBuildChoiceScratch[i];
                int choiceBudget = IsSameAutoplayBuildAction(choice,
                    reservedDefenseBuild)
                        ? _towerDefenseGold
                        : spendableGold;
                if (!choice.IsValid || restrictBuildToDefenseTarget &&
                    !IsSameAutoplayBuildAction(choice, reservedDefenseBuild))
                    continue;
                float gain = GetAutoplayBuildCapitalGain(choice, snapshot,
                    missingFunctionGroup);
                if (!IsValidAutoplayCapitalGain(gain)) continue;
                float roi = GetAutoplayCapitalRoi(gain, choice.PaidCost);
                float score = GetAutoplayNormalizedCapitalScore(roi, gain,
                    maximumRoi, maximumGain, wealth);
                if (choice.PaidCost > choiceBudget)
                {
                    float progress = choice.PaidCost > 0
                        ? spendableGold / (float)choice.PaidCost
                        : 1f;
                    bool permittedHold = !defensiveHoldOnly ||
                        IsSameAutoplayBuildAction(choice,
                            reservedDefenseBuild);
                    if (permittedHold && progress >= 0.65f && progress < 1f &&
                        score > bestUnaffordableScore)
                    {
                        bestUnaffordableScore = score;
                        holdKind = AutoplayCapitalActionKind.Build;
                        holdBuild = choice;
                        holdUpgrade = default;
                        holdSupport = default;
                        holdCharge = default;
                    }
                    continue;
                }
                if (score <= bestScore) continue;
                bestScore = score;
                bestKind = AutoplayCapitalActionKind.Build;
                selectedBuild = choice;
            }
        }
        for (int i = 0; i < _towerDefenseAutoplayUpgradeChoiceScratch.Count; i++)
        {
            AutoplayUpgradeChoice choice =
                _towerDefenseAutoplayUpgradeChoiceScratch[i];
            if (!choice.IsValid) continue;
            float gain = GetAutoplayUpgradeCapitalGain(choice);
            if (!IsValidAutoplayCapitalGain(gain)) continue;
            float roi = GetAutoplayCapitalRoi(gain, choice.PaidCost);
            float score = GetAutoplayNormalizedCapitalScore(roi, gain,
                maximumRoi, maximumGain, wealth);
            if (choice.PaidCost > spendableGold)
            {
                float progress = choice.PaidCost > 0
                    ? spendableGold / (float)choice.PaidCost
                    : 1f;
                if (!defensiveHoldOnly && progress >= 0.65f && progress < 1f &&
                    score > bestUnaffordableScore)
                {
                    bestUnaffordableScore = score;
                    holdKind = AutoplayCapitalActionKind.Upgrade;
                    holdUpgrade = choice;
                    holdBuild = default;
                    holdSupport = default;
                    holdCharge = default;
                }
                continue;
            }
            if (score <= bestScore) continue;
            bestScore = score;
            bestKind = AutoplayCapitalActionKind.Upgrade;
            selectedUpgrade = choice;
        }
        if (supportMarketOpen)
        {
            float gain = supportChoice.CapitalGain;
            float score = GetAutoplayNormalizedCapitalScore(
                GetAutoplayCapitalRoi(gain, supportPaidCost), gain,
                maximumRoi, maximumGain, wealth);
            if (!supportEligible)
            {
                float progress = supportPaidCost > 0
                    ? spendableGold / (float)supportPaidCost
                    : 1f;
                if (!defensiveHoldOnly && progress >= 0.65f && progress < 1f &&
                    score > bestUnaffordableScore)
                {
                    bestUnaffordableScore = score;
                    holdKind = AutoplayCapitalActionKind.Support;
                    holdBuild = default;
                    holdUpgrade = default;
                    holdSupport = supportChoice;
                    holdCharge = default;
                }
            }
            else if (score > bestScore)
            {
                bestScore = score;
                bestKind = AutoplayCapitalActionKind.Support;
                selectedSupport = supportChoice;
            }
        }
        if (chargeMarketOpen)
        {
            float gain = chargeChoice.CapitalGain;
            float score = GetAutoplayNormalizedCapitalScore(
                GetAutoplayCapitalRoi(gain, chargeChoice.PaidCost), gain,
                maximumRoi, maximumGain, wealth);
            if (!chargeEligible)
            {
                float progress = chargeChoice.PaidCost > 0
                    ? spendableGold / (float)chargeChoice.PaidCost
                    : 1f;
                if (!defensiveHoldOnly && progress >= 0.65f && progress < 1f &&
                    score > bestUnaffordableScore)
                {
                    bestUnaffordableScore = score;
                    holdKind = AutoplayCapitalActionKind.Charge;
                    holdBuild = default;
                    holdUpgrade = default;
                    holdSupport = default;
                    holdCharge = chargeChoice;
                }
            }
            else if (score > bestScore)
            {
                bestScore = score;
                bestKind = AutoplayCapitalActionKind.Charge;
                selectedCharge = chargeChoice;
            }
        }

        // A fully observed core tower may break a near tie, but never rescue an
        // objectively weak upgrade. Observed DPS chooses the proven candidate; the
        // bounded 0..4% confidence nudge only resolves close capital scores.
        if (affordableCoreUpgrade.IsValid &&
            affordableCoreUpgrade.PaidCost <= spendableGold)
        {
            float coreGain = GetAutoplayUpgradeCapitalGain(
                affordableCoreUpgrade);
            float coreBaseScore = GetAutoplayNormalizedCapitalScore(
                GetAutoplayCapitalRoi(coreGain,
                    affordableCoreUpgrade.PaidCost), coreGain,
                maximumRoi, maximumGain, wealth);
            bool objectivelyClose = bestKind == AutoplayCapitalActionKind.None ||
                                    coreBaseScore >= bestScore * 0.94f;
            if (objectivelyClose)
            {
                float evidence = Mathf.InverseLerp(0.65f, 1f,
                    affordableCoreUpgrade.ObservedCoreConfidence);
                float coreScore = coreBaseScore *
                                  Mathf.Lerp(1f, 1.04f, evidence);
                if (coreScore > bestScore)
                {
                    bestScore = coreScore;
                    bestKind = AutoplayCapitalActionKind.Upgrade;
                    selectedBuild = default;
                    selectedUpgrade = affordableCoreUpgrade;
                }
            }
        }

        float holdQualityRatio = 1.25f /
            Mathf.Clamp(TowerDefenseAutoplayCommander.SaveBias, 0.9f, 1.1f);
        bool clearlySuperiorHold = holdKind != AutoplayCapitalActionKind.None &&
            (bestKind == AutoplayCapitalActionKind.None ||
             bestUnaffordableScore >= bestScore * holdQualityRatio &&
             bestUnaffordableScore - bestScore >= 0.08f);
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (allowShortHold && clearlySuperiorHold &&
            gameTime >= _towerDefenseAutoplayCapitalHoldCooldownUntilGameTime)
        {
            if (!_towerDefenseAutoplayCapitalHoldActive)
            {
                _towerDefenseAutoplayCapitalHoldActive = true;
                _towerDefenseAutoplayCapitalHoldUntilGameTime = gameTime +
                    TowerDefenseAutoplayCapitalHoldSeconds;
            }
            if (gameTime < _towerDefenseAutoplayCapitalHoldUntilGameTime)
            {
                selectedBuild = holdBuild;
                selectedUpgrade = holdUpgrade;
                selectedSupport = holdSupport;
                selectedCharge = holdCharge;
                return AutoplayCapitalActionKind.Hold;
            }

            // A changing target cannot extend the window forever. After one timed
            // attempt, spend on the best executable action and briefly cool down.
            _towerDefenseAutoplayCapitalHoldActive = false;
            _towerDefenseAutoplayCapitalHoldCooldownUntilGameTime = gameTime +
                TowerDefenseAutoplayCapitalHoldCooldownSeconds;
        }
        else
        {
            if (_towerDefenseAutoplayCapitalHoldActive && allowShortHold)
                _towerDefenseAutoplayCapitalHoldCooldownUntilGameTime =
                    Mathf.Max(
                        _towerDefenseAutoplayCapitalHoldCooldownUntilGameTime,
                        gameTime + TowerDefenseAutoplayCapitalHoldCooldownSeconds);
            _towerDefenseAutoplayCapitalHoldActive = false;
        }
        return bestKind;
    }

    private float GetAutoplayBuildCapitalGain(AutoplayBuildChoice choice,
        AutoplayBattleSnapshot snapshot, bool missingFunctionGroup)
    {
        float strategicGain = choice.ObjectiveUtility *
            GetAutoplayBuildCapitalContext(choice, snapshot,
                missingFunctionGroup);
        // Utility encodes geometry, pressure and role fit; retain a separate absolute
        // output term so a rich commander can distinguish a large power purchase from
        // another cheap transition tower instead of comparing only logarithms.
        float powerGain = Mathf.Sqrt(Mathf.Max(0f, choice.MarginalPower)) * 18f;
        return strategicGain + powerGain;
    }

    private float GetAutoplayUpgradeCapitalGain(
        AutoplayUpgradeChoice choice)
    {
        float strategicGain = choice.ObjectiveUtility *
            GetAutoplayUpgradeCapitalContext(choice);
        float powerGain = Mathf.Sqrt(Mathf.Max(0f, choice.MarginalPower)) * 18f;
        return strategicGain + powerGain;
    }

    private float GetAutoplayBuildCapitalContext(AutoplayBuildChoice choice,
        AutoplayBattleSnapshot snapshot, bool missingFunctionGroup)
    {
        float context = 0.88f;
        int functionGroup = GetAutoplayFunctionGroup(choice.Type);
        if (missingFunctionGroup &&
            _towerDefenseAutoplayFunctionCounts[functionGroup] <= 0)
            context = Mathf.Max(context, 1.1f);
        if (choice.TileScore >= 95f && choice.OpportunityPenalty <= 0f)
            context = Mathf.Max(context, 1.08f);
        if (choice.NearBaseHeatCoverage >=
            TowerDefenseAutoplayNearBaseEarlyWarning)
            context = Mathf.Max(context, 1.18f);
        context += Mathf.Clamp01(
            (_towerDefenseAutoplayEnemyFlowBacklog - 0.18f) / 0.55f) * 0.15f;
        bool bossRouteInvestment = IsAutoplayBossInvestmentStage(snapshot) &&
            choice.BossRouteCoverage > 0.001f &&
            (_towerDefenseAutoplayBossPowerDeficit > 0.05f &&
             IsAutoplayBossDamageTower(choice.Type) ||
             _towerDefenseAutoplayBossControlDeficit > 0.05f &&
             IsAutoplayControlTower(choice.Type));
        if (bossRouteInvestment) context = Mathf.Max(context, 1.08f);
        float commanderPreference = Mathf.Clamp(
            TowerDefenseAutoplayCommander.BuildBias *
            GetAutoplayPersonalityTowerBias(choice.Type), 0.88f, 1.12f);
        return Mathf.Clamp(context * commanderPreference, 0.72f, 1.42f);
    }

    private float GetAutoplayUpgradeCapitalContext(
        AutoplayUpgradeChoice choice)
    {
        float evidence = Mathf.Lerp(1f, 1.08f,
            Mathf.Clamp01(choice.ObservedCoreConfidence));
        float commanderPreference = Mathf.Clamp(
            TowerDefenseAutoplayCommander.UpgradeBias *
            GetAutoplayPersonalityUpgradeBias(choice.Tower,
                choice.SpecializationChoiceIndex),
            0.88f, 1.12f);
        return Mathf.Clamp(evidence * commanderPreference, 0.82f, 1.22f);
    }

    private static float GetAutoplayCapitalRoi(float gain, int paidCost)
    {
        if (!IsValidAutoplayCapitalGain(gain)) return 0f;
        return Mathf.Max(0f, gain) * 100f /
               Mathf.Max(100f, paidCost + 180f);
    }

    private static float GetAutoplayEmergencyPurchaseScore(float gain,
        int paidCost)
    {
        if (!IsValidAutoplayCapitalGain(gain)) return 0f;
        // Preserve both immediate output and cost efficiency without letting a tiny
        // cheap upgrade automatically beat the only meaningful defensive purchase.
        return gain / Mathf.Sqrt(Mathf.Max(100f, paidCost + 180f));
    }

    private bool ShouldPreferAutoplayCoreUpgrade(
        AutoplayUpgradeChoice upgrade, AutoplayBuildChoice build,
        AutoplayBattleSnapshot snapshot, bool missingFunctionGroup,
        bool requiresLiquidation)
    {
        if (!upgrade.IsValid || upgrade.Tower == null) return false;
        if (!build.IsValid) return true;
        float upgradeGain = GetAutoplayUpgradeCapitalGain(upgrade);
        float buildGain = GetAutoplayBuildCapitalGain(build, snapshot,
            missingFunctionGroup);
        if (!IsValidAutoplayCapitalGain(upgradeGain)) return false;
        if (!IsValidAutoplayCapitalGain(buildGain)) return true;

        // In the two-cell band the question is how much defense arrives now, not which
        // purchase has the prettiest low-cost ROI. Selling is deliberately held to a
        // much higher bar than spending available gold on the same upgrade.
        float requiredGainRatio = requiresLiquidation ? 1.25f : 1.03f;
        if (upgradeGain >= buildGain * requiredGainRatio) return true;
        if (requiresLiquidation) return false;

        float upgradePower = Mathf.Max(0f, upgrade.MarginalPower);
        float buildPower = Mathf.Max(0f, build.MarginalPower);
        bool materiallyMorePower = upgradePower >= buildPower * 1.18f &&
                                   upgradeGain >= buildGain * 0.88f;
        float upgradeEmergencyScore = GetAutoplayEmergencyPurchaseScore(
            upgradeGain, upgrade.PaidCost);
        float buildEmergencyScore = GetAutoplayEmergencyPurchaseScore(
            buildGain, build.PaidCost);
        return materiallyMorePower ||
               upgradeEmergencyScore >= buildEmergencyScore * 1.08f;
    }

    private static bool IsSameAutoplayBuildAction(AutoplayBuildChoice left,
        AutoplayBuildChoice right)
    {
        return left.IsValid && right.IsValid && left.Type == right.Type &&
               left.Cell == right.Cell;
    }

    private static bool IsValidAutoplayCapitalGain(float gain)
    {
        return float.IsFinite(gain) && gain > 0f;
    }

    private static void AccumulateAutoplayCapitalScale(float gain, int paidCost,
        ref float maximumRoi, ref float maximumGain,
        ref int minimumPositiveCost)
    {
        if (!IsValidAutoplayCapitalGain(gain)) return;
        maximumGain = Mathf.Max(maximumGain, gain);
        maximumRoi = Mathf.Max(maximumRoi,
            GetAutoplayCapitalRoi(gain, paidCost));
        if (paidCost > 0)
            minimumPositiveCost = Mathf.Min(minimumPositiveCost, paidCost);
    }

    private bool TryApplyAutoplayBossTargeting(out string decision)
    {
        decision = string.Empty;
        bool liveBoss = TryGetAutoplayLiveBossTarget(out Vector3 bossPosition,
            out float bossRadius);
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        int focused = 0;
        int released = 0;
        int cleanupAssigned = 0;
        int cleanupReleased = 0;

        _towerDefenseAutoplayBossFocusCandidates.Clear();
        _towerDefenseAutoplayDesiredBossFocus.Clear();
        _towerDefenseAutoplayReservedBossGuards.Clear();
        if (liveBoss)
        {
            for (int i = 0; i < _defenseTowers.Count; i++)
            {
                RougeDefenseTower tower = _defenseTowers[i];
                if (ShouldAutoplayUseBossFocus(tower, bossPosition, bossRadius))
                    _towerDefenseAutoplayBossFocusCandidates.Add(tower);
            }
            AllocateAutoplayBossCleanupGuards(map,
                _towerDefenseAutoplayBossFocusCandidates,
                _towerDefenseAutoplayReservedBossGuards);
            for (int i = 0;
                 i < _towerDefenseAutoplayBossFocusCandidates.Count; i++)
            {
                RougeDefenseTower tower =
                    _towerDefenseAutoplayBossFocusCandidates[i];
                if (!_towerDefenseAutoplayReservedBossGuards.Contains(tower))
                    _towerDefenseAutoplayDesiredBossFocus.Add(tower);
            }
        }

        // Apply the computed squad order in one pass. At least one in-range tower
        // remains on the Boss; only the minimum towers needed to cover real non-Boss
        // hotspots stay on normal cleanup targeting.
        _towerDefenseAutoplayBossOverrides.Clear();
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null) continue;
            bool shouldCleanupFirst = liveBoss &&
                _towerDefenseAutoplayReservedBossGuards.Contains(tower);
            if (tower.SetAutoplayCleanupFirst(shouldCleanupFirst))
            {
                if (shouldCleanupFirst) cleanupAssigned++;
                else cleanupReleased++;
            }
            if (!ShouldAutoplayFocusBoss(tower)) continue;
            bool shouldFocus = _towerDefenseAutoplayDesiredBossFocus.Contains(tower);
            bool isFocused = tower.TargetPriority ==
                             RougeTowerTargetPriority.BossFirst;
            if (shouldFocus != isFocused)
            {
                tower.ToggleTargetPriority();
                if (tower.TargetPriority == RougeTowerTargetPriority.BossFirst)
                    focused++;
                else
                    released++;
            }
            if (shouldFocus && tower.TargetPriority ==
                    RougeTowerTargetPriority.BossFirst)
                _towerDefenseAutoplayBossOverrides.Add(tower);
        }

        if (focused <= 0 && released <= 0 && cleanupAssigned <= 0 &&
            cleanupReleased <= 0) return false;
        _towerTargetScheduledCount = 0;
        RefreshTowerDefenseUi(true);
        int guardCount = _towerDefenseAutoplayReservedBossGuards.Count;
        decision = focused > 0 && released > 0
            ? $"Boss 火力配额：{focused} 座入圈塔开始集火，{released} 座转回清漏，" +
              $"当前保留 {guardCount} 座守热点。"
            : focused > 0
                ? $"Boss 入圈：{focused} 座塔协同集火，{guardCount} 座按敌潮热区清漏。"
                : released > 0 && liveBoss
                    ? $"敌潮逼近：{released} 座塔退出集火，保留 {guardCount} 座清漏。"
                    : liveBoss
                        ? $"Boss 火力配额更新：当前保留 {guardCount} 座塔清漏。"
                        : released > 0
                            ? $"Boss 离开射界：{released} 座塔恢复普通索敌。"
                            : "Boss 离开射界：清漏配额已解除。";
        return true;
    }

    private void AllocateAutoplayBossCleanupGuards(RougeTowerDefenseMap map,
        List<RougeDefenseTower> candidates,
        HashSet<RougeDefenseTower> reserved)
    {
        if (map == null || candidates == null || candidates.Count <= 1) return;
        CollectAutoplayNearBaseHeatHotspots(map);
        if (_towerDefenseAutoplayNearBaseHotspots.Count == 0) return;

        ApplyAutoplayExistingCleanupCoverageRestrictedFirst(map, candidates);

        int maximumReserved = candidates.Count - 1;
        while (reserved.Count < maximumReserved)
        {
            RougeDefenseTower best = null;
            float bestScore = 0f;
            for (int candidateIndex = 0; candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                RougeDefenseTower tower = candidates[candidateIndex];
                if (reserved.Contains(tower)) continue;
                float coverageUnits = GetAutoplayCleanupCoverageUnits(tower);
                float coveredRisk = EstimateAutoplayCleanupCoverageValue(map,
                    tower, coverageUnits);
                if (coveredRisk <= 0f) continue;
                float cleanupPower = tower.Damage /
                    Mathf.Max(0.03f, tower.EffectiveAttackInterval) *
                    (1f + Mathf.Max(0, tower.AttackTargetCount - 1) * 0.65f) *
                    (1f + Mathf.Max(0f, tower.AoeRadius) * 0.08f);
                float bossPower = tower.Damage /
                    Mathf.Max(0.03f, tower.EffectiveAttackInterval) *
                    (tower.UsesFlamethrower
                        ? 1
                        : Mathf.Max(1, tower.AttackProjectileCount));
                float score = coveredRisk * 1000f +
                    Mathf.Log(1f + cleanupPower) * 24f -
                    Mathf.Log(1f + bossPower) * 10f;
                if (tower.AutoplayCleanupFirst) score += 18f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = tower;
            }
            if (best == null) break;
            reserved.Add(best);
            ApplyAutoplayCleanupCoverage(map, best,
                GetAutoplayCleanupCoverageUnits(best));
        }
    }

    private void CollectAutoplayNearBaseHeatHotspots(RougeTowerDefenseMap map)
    {
        if (_towerDefenseAutoplayHotspotRevision ==
            _towerDefenseAutoplayHeatmapRevision)
        {
            ResetAutoplayHotspotCoverage();
            return;
        }

        _towerDefenseAutoplayNearBaseHotspots.Clear();
        _towerDefenseAutoplayHotspotRevision =
            _towerDefenseAutoplayHeatmapRevision;
        if (map == null || _towerDefenseAutoplayHeatmapMap != map ||
            _towerDefenseAutoplayNearBaseCrisis <
                TowerDefenseAutoplayNearBaseEarlyWarning) return;
        int cellCount = Mathf.Min(map.Width * map.Height,
            _towerDefenseAutoplayNonBossHeatByCell.Length);
        for (int index = 0; index < cellCount; index++)
        {
            float risk = GetAutoplayNearBaseHeatRiskAtCell(index);
            if (risk < TowerDefenseAutoplayNearBaseEarlyWarning) continue;
            Vector2Int cell = new Vector2Int(index % map.Width,
                index / map.Width);
            bool merged = false;
            for (int i = 0; i < _towerDefenseAutoplayNearBaseHotspots.Count; i++)
            {
                AutoplayHeatHotspot existing =
                    _towerDefenseAutoplayNearBaseHotspots[i];
                Vector2Int delta = existing.Cell - cell;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) > 2) continue;
                if (risk > existing.Risk)
                {
                    existing.Cell = cell;
                    existing.Risk = risk;
                    _towerDefenseAutoplayNearBaseHotspots[i] = existing;
                }
                merged = true;
                break;
            }
            if (merged) continue;
            _towerDefenseAutoplayNearBaseHotspots.Add(new AutoplayHeatHotspot
            {
                Cell = cell,
                Risk = risk
            });
        }

        _towerDefenseAutoplayNearBaseHotspots.Sort((left, right) =>
            right.Risk.CompareTo(left.Risk));
        if (_towerDefenseAutoplayNearBaseHotspots.Count > 3)
            _towerDefenseAutoplayNearBaseHotspots.RemoveRange(3,
                _towerDefenseAutoplayNearBaseHotspots.Count - 3);
        for (int i = 0; i < _towerDefenseAutoplayNearBaseHotspots.Count; i++)
        {
            AutoplayHeatHotspot hotspot =
                _towerDefenseAutoplayNearBaseHotspots[i];
            hotspot.RequiredCoverage = Mathf.Lerp(0.75f, 1.75f,
                Mathf.InverseLerp(TowerDefenseAutoplayNearBaseEarlyWarning,
                    TowerDefenseAutoplayImmediateNearBaseCrisis,
                    hotspot.Risk));
            _towerDefenseAutoplayNearBaseHotspots[i] = hotspot;
        }
    }

    private void ResetAutoplayHotspotCoverage()
    {
        for (int i = 0; i < _towerDefenseAutoplayNearBaseHotspots.Count; i++)
        {
            AutoplayHeatHotspot hotspot =
                _towerDefenseAutoplayNearBaseHotspots[i];
            hotspot.CurrentCoverage = 0f;
            _towerDefenseAutoplayNearBaseHotspots[i] = hotspot;
        }
    }

    private float EstimateAutoplayCleanupCoverageValue(
        RougeTowerDefenseMap map, RougeDefenseTower tower, float capacity)
    {
        float value = 0f;
        float remaining = Mathf.Max(0f, capacity);
        for (int i = 0;
             i < _towerDefenseAutoplayNearBaseHotspots.Count && remaining > 0.001f;
             i++)
        {
            AutoplayHeatHotspot hotspot =
                _towerDefenseAutoplayNearBaseHotspots[i];
            if (!DoesAutoplayTowerCoverCell(map, tower, hotspot.Cell)) continue;
            float deficit = Mathf.Max(0f,
                hotspot.RequiredCoverage - hotspot.CurrentCoverage);
            float assigned = Mathf.Min(remaining, deficit);
            value += hotspot.Risk * assigned;
            remaining -= assigned;
        }
        return value;
    }

    private void ApplyAutoplayCleanupCoverage(RougeTowerDefenseMap map,
        RougeDefenseTower tower, float capacity)
    {
        float remaining = Mathf.Max(0f, capacity);
        for (int i = 0;
             i < _towerDefenseAutoplayNearBaseHotspots.Count && remaining > 0.001f;
             i++)
        {
            AutoplayHeatHotspot hotspot =
                _towerDefenseAutoplayNearBaseHotspots[i];
            if (!DoesAutoplayTowerCoverCell(map, tower, hotspot.Cell)) continue;
            float deficit = Mathf.Max(0f,
                hotspot.RequiredCoverage - hotspot.CurrentCoverage);
            float assigned = Mathf.Min(remaining, deficit);
            if (assigned <= 0f) continue;
            hotspot.CurrentCoverage += assigned;
            remaining -= assigned;
            _towerDefenseAutoplayNearBaseHotspots[i] = hotspot;
        }
    }

    private void ApplyAutoplayExistingCleanupCoverageRestrictedFirst(
        RougeTowerDefenseMap map, List<RougeDefenseTower> excluded)
    {
        int hotspotCount = _towerDefenseAutoplayNearBaseHotspots.Count;
        // Assign narrow/specialized coverage first. If a wide tower that covers A+B
        // consumes A before an A-only tower is considered, a greedy pass falsely
        // reports B as uncovered and can trigger another unnecessary construction.
        for (int breadth = 1; breadth <= hotspotCount; breadth++)
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) ||
                excluded != null && excluded.Contains(tower) ||
                CountAutoplayCoveredHeatHotspots(map, tower) != breadth)
                continue;
            ApplyAutoplayCleanupCoverage(map, tower,
                GetAutoplayCleanupCoverageUnits(tower));
        }
    }

    private int CountAutoplayCoveredHeatHotspots(RougeTowerDefenseMap map,
        RougeDefenseTower tower)
    {
        int count = 0;
        for (int i = 0; i < _towerDefenseAutoplayNearBaseHotspots.Count; i++)
            if (DoesAutoplayTowerCoverCell(map, tower,
                    _towerDefenseAutoplayNearBaseHotspots[i].Cell))
                count++;
        return count;
    }

    private bool DoesAutoplayBuildCloseUncoveredNearBaseHotspot(
        RougeTowerDefenseMap map, AutoplayBuildChoice choice)
    {
        if (map == null || !choice.IsValid ||
            choice.NearBaseHeatCoverage <
                TowerDefenseAutoplayNearBaseEarlyWarning ||
            choice.NearBaseHeatCellIndex < 0) return false;

        CollectAutoplayNearBaseHeatHotspots(map);
        if (_towerDefenseAutoplayNearBaseHotspots.Count == 0) return false;
        ApplyAutoplayExistingCleanupCoverageRestrictedFirst(map, null);

        Vector2Int targetCell = new Vector2Int(
            choice.NearBaseHeatCellIndex % map.Width,
            choice.NearBaseHeatCellIndex / map.Width);
        for (int i = 0; i < _towerDefenseAutoplayNearBaseHotspots.Count; i++)
        {
            AutoplayHeatHotspot hotspot =
                _towerDefenseAutoplayNearBaseHotspots[i];
            Vector2Int delta = hotspot.Cell - targetCell;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) > 2) continue;
            if (hotspot.RequiredCoverage - hotspot.CurrentCoverage >= 0.12f)
                return true;
        }
        return false;
    }

    private bool HasAutoplayUncoveredNearBaseHotspot(
        RougeTowerDefenseMap map)
    {
        if (map == null) return false;
        CollectAutoplayNearBaseHeatHotspots(map);
        if (_towerDefenseAutoplayNearBaseHotspots.Count == 0) return false;
        ApplyAutoplayExistingCleanupCoverageRestrictedFirst(map, null);
        for (int i = 0; i < _towerDefenseAutoplayNearBaseHotspots.Count; i++)
        {
            AutoplayHeatHotspot hotspot =
                _towerDefenseAutoplayNearBaseHotspots[i];
            if (hotspot.RequiredCoverage - hotspot.CurrentCoverage >= 0.12f)
                return true;
        }
        return false;
    }

    private bool TryGetAutoplayLaneDefenseGap(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot, out int gapCellIndex,
        out float gapSeverity)
    {
        gapCellIndex = -1;
        gapSeverity = 0f;
        if (map == null || !_towerDefenseAutoplayHasRouteMainCell) return false;

        float healthRatio = mainTower != null && mainTower.maxHealth > 0.001f
            ? Mathf.Clamp01(mainTower.CurrentHealth / mainTower.maxHealth)
            : 1f;
        bool pressureElevated = snapshot.UrgentPressure >=
                                TowerDefenseAutoplayThresholds
                                    .coverageUrgentPressure ||
            snapshot.ActiveEnemies >= TowerDefenseAutoplayThresholds
                .coverageActiveEnemies ||
            healthRatio <= TowerDefenseAutoplayThresholds.coverageHealthRatio;
        bool immediateDefense = healthRatio <= TowerDefenseAutoplayThresholds
                .immediateDefenseHealthRatio &&
            (snapshot.UrgentPressure >= TowerDefenseAutoplayThresholds
                 .immediateDefenseUrgentPressure ||
             snapshot.ActiveEnemies >= TowerDefenseAutoplayThresholds
                 .immediateDefenseActiveEnemies);
        float threshold = pressureElevated || immediateDefense
            ? TowerDefenseAutoplayLaneDefensePressureThreshold
            : TowerDefenseAutoplayLaneDefenseStableThreshold;
        int cellCount = Mathf.Min(map.Width * map.Height,
            _towerDefenseAutoplayEnemyPressureByCell.Length);

        // Sample the time-weighted forecast once per independent route. Multiple spawn
        // points that have merged by this band add heat to the same lane; simultaneous
        // branches remain separate. Installed coverage strongly saturates that lane's
        // unmet demand, so the next purchase naturally moves to another hot branch.
        float planningHorizon = CollectAutoplayLaneAnchors(map);
        for (int laneIndex = 0;
             laneIndex < _towerDefenseAutoplayLaneAnchors.Count; laneIndex++)
        {
            AutoplayLaneAnchor lane = _towerDefenseAutoplayLaneAnchors[laneIndex];
            int index = lane.CoverageCellIndex;
            if ((uint)index >= (uint)cellCount) continue;
            float total = _towerDefenseAutoplayEnemyPressureByCell[index];
            float boss = (uint)index <
                         (uint)_towerDefenseAutoplayBossPressureByCell.Length
                ? _towerDefenseAutoplayBossPressureByCell[index]
                : 0f;
            float crowd = (uint)index <
                          (uint)_towerDefenseAutoplayCrowdPressureByCell.Length
                ? _towerDefenseAutoplayCrowdPressureByCell[index]
                : 0f;
            float elite = (uint)index <
                          (uint)_towerDefenseAutoplayElitePressureByCell.Length
                ? _towerDefenseAutoplayElitePressureByCell[index]
                : 0f;
            float urgent = (uint)index <
                           (uint)_towerDefenseAutoplayUrgentPressureByCell.Length
                ? _towerDefenseAutoplayUrgentPressureByCell[index]
                : 0f;
            float nonBossPressure = Mathf.Max(crowd + elite,
                Mathf.Max(0f, total - boss));
            if (nonBossPressure <= 0.01f && urgent <= 0.01f) continue;
            float coverage = (uint)index <
                             (uint)_towerDefenseAutoplayCoverageByCell.Length
                ? Mathf.Max(0f, _towerDefenseAutoplayCoverageByCell[index])
                : 0f;
            float urgency = planningHorizon > 0.001f
                ? 1f - Mathf.Clamp01(lane.NextSpawnSeconds / planningHorizon)
                : 1f;
            float uncovered = (nonBossPressure + urgent * 0.65f) *
                              Mathf.Lerp(0.92f, 1.08f, urgency) /
                              (1f + coverage * 5f);
            if (uncovered <= gapSeverity) continue;
            gapSeverity = uncovered;
            gapCellIndex = index;
        }

        // A spawn point only owns one cached shortest-path successor, so tracing the
        // spawner itself can collapse a real fork onto one arbitrary branch. Once the
        // opening observation has seen live enemies, trace their current cells instead:
        // positions that already separated left/right retain that branch identity all
        // the way to the near-core coverage band. Projected duplicate cells do not add
        // together here; only the strongest unmet anchor wins this decision.
        for (int sourceIndex = 0; sourceIndex < cellCount; sourceIndex++)
        {
            float activeCrowd = (uint)sourceIndex <
                                (uint)_towerDefenseAutoplayActiveCrowdPressureByCell.Length
                ? _towerDefenseAutoplayActiveCrowdPressureByCell[sourceIndex]
                : 0f;
            float activeElite = (uint)sourceIndex <
                                (uint)_towerDefenseAutoplayActiveElitePressureByCell.Length
                ? _towerDefenseAutoplayActiveElitePressureByCell[sourceIndex]
                : 0f;
            float activeUrgent = (uint)sourceIndex <
                                 (uint)_towerDefenseAutoplayActiveUrgentPressureByCell.Length
                ? _towerDefenseAutoplayActiveUrgentPressureByCell[sourceIndex]
                : 0f;
            float activePressure = activeCrowd + activeElite +
                                   activeUrgent * 0.65f;
            if (activePressure <= 0.01f) continue;

            Vector2Int sourceCell = new Vector2Int(sourceIndex % map.Width,
                sourceIndex / map.Width);
            if (!TryTraceAutoplayLaneAnchor(map, sourceCell, out _,
                    out int coverageCellIndex) ||
                (uint)coverageCellIndex >= (uint)cellCount) continue;

            float coverage = (uint)coverageCellIndex <
                             (uint)_towerDefenseAutoplayCoverageByCell.Length
                ? Mathf.Max(0f,
                    _towerDefenseAutoplayCoverageByCell[coverageCellIndex])
                : 0f;
            float coreTraffic = (uint)coverageCellIndex <
                                (uint)_towerDefenseAutoplayRouteCoreTrafficByCell.Length
                ? _towerDefenseAutoplayRouteCoreTrafficByCell[coverageCellIndex]
                : 0f;
            float traffic = Mathf.Clamp01(coreTraffic /
                Mathf.Max(0.0001f, _towerDefenseAutoplayMaximumCoreTraffic));
            float uncovered = activePressure / (1f + coverage * 5f);
            uncovered *= Mathf.Lerp(0.9f, 1.12f, Mathf.Sqrt(traffic));
            if (uncovered <= gapSeverity) continue;
            gapSeverity = uncovered;
            gapCellIndex = coverageCellIndex;
        }

        for (int index = 0; index < cellCount; index++)
        {
            float routeDistance = (uint)index <
                                  (uint)_towerDefenseAutoplayRouteDistanceByCell.Length
                ? _towerDefenseAutoplayRouteDistanceByCell[index]
                : float.PositiveInfinity;
            if (float.IsPositiveInfinity(routeDistance) ||
                routeDistance < TowerDefenseAutoplayLaneDefenseInnerCells ||
                routeDistance > TowerDefenseAutoplayLaneDefenseOuterCells)
                continue;
            float coreTraffic = (uint)index <
                                (uint)_towerDefenseAutoplayRouteCoreTrafficByCell.Length
                ? _towerDefenseAutoplayRouteCoreTrafficByCell[index]
                : 0f;
            if (coreTraffic <= 0.0001f) continue;

            // Exclude the final merged trunk. A single well-covered goal cell must not
            // hide an uncovered left or right approach immediately before the merge.
            int predecessors = (uint)index <
                               (uint)_towerDefenseAutoplayRoutePredecessorCountByCell.Length
                ? _towerDefenseAutoplayRoutePredecessorCountByCell[index]
                : 0;
            if (predecessors > 1) continue;
            float activeCrowd = (uint)index <
                                (uint)_towerDefenseAutoplayActiveCrowdPressureByCell.Length
                ? _towerDefenseAutoplayActiveCrowdPressureByCell[index]
                : 0f;
            float activeElite = (uint)index <
                                (uint)_towerDefenseAutoplayActiveElitePressureByCell.Length
                ? _towerDefenseAutoplayActiveElitePressureByCell[index]
                : 0f;
            float activeUrgent = (uint)index <
                                 (uint)_towerDefenseAutoplayActiveUrgentPressureByCell.Length
                ? _towerDefenseAutoplayActiveUrgentPressureByCell[index]
                : 0f;
            // Forecast heat is already collapsed to one anchor per lane above. This
            // second pass exists only for enemies actually moving between anchors.
            if (activeCrowd + activeElite + activeUrgent <= 0.01f) continue;
            float total = _towerDefenseAutoplayEnemyPressureByCell[index];
            float boss = (uint)index <
                         (uint)_towerDefenseAutoplayBossPressureByCell.Length
                ? _towerDefenseAutoplayBossPressureByCell[index]
                : 0f;
            float crowd = (uint)index <
                          (uint)_towerDefenseAutoplayCrowdPressureByCell.Length
                ? _towerDefenseAutoplayCrowdPressureByCell[index]
                : 0f;
            float elite = (uint)index <
                          (uint)_towerDefenseAutoplayElitePressureByCell.Length
                ? _towerDefenseAutoplayElitePressureByCell[index]
                : 0f;
            float urgent = (uint)index <
                           (uint)_towerDefenseAutoplayUrgentPressureByCell.Length
                ? _towerDefenseAutoplayUrgentPressureByCell[index]
                : 0f;
            float nonBossPressure = Mathf.Max(crowd + elite,
                Mathf.Max(0f, total - boss));
            if (nonBossPressure <= 0.01f && urgent <= 0.01f) continue;

            float coverage = (uint)index <
                             (uint)_towerDefenseAutoplayCoverageByCell.Length
                ? Mathf.Max(0f, _towerDefenseAutoplayCoverageByCell[index])
                : 0f;
            float uncovered = (nonBossPressure + urgent * 0.65f) /
                (1f + coverage * 1.15f);
            float traffic = Mathf.Clamp01(coreTraffic /
                Mathf.Max(0.0001f, _towerDefenseAutoplayMaximumCoreTraffic));
            uncovered *= Mathf.Lerp(0.85f, 1.12f, Mathf.Sqrt(traffic));
            if (uncovered <= gapSeverity) continue;
            gapSeverity = uncovered;
            gapCellIndex = index;
        }
        return gapCellIndex >= 0 && gapSeverity >= threshold;
    }

    private float CollectAutoplayLaneAnchors(RougeTowerDefenseMap map)
    {
        _towerDefenseAutoplayLaneAnchors.Clear();
        if (map == null) return 0f;

        // The window is temporal and commander-configurable. It deliberately contains
        // the next normal expansion interval, so a route scheduled just beyond the
        // short combat forecast is still covered before it starts, without reserving
        // towers at minute-five entrances during the opening seconds.
        float planningHorizon = Mathf.Max(TowerDefenseAutoplayWaveForecastSeconds,
            TowerDefenseAutoplayExpansionInterval);
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint spawner = _towerDefenseSpawners[i];
            if (spawner == null || !spawner.isActiveAndEnabled ||
                spawner.HasReachedWaveLimit()) continue;
            float seconds = Mathf.Max(0f, spawner.timer);
            if (seconds > planningHorizon ||
                !map.WorldToCell(spawner.transform.position,
                    out Vector2Int spawnCell) ||
                !TryTraceAutoplayLaneAnchor(map, spawnCell,
                    out int keyCellIndex, out int coverageCellIndex)) continue;

            int existingIndex = -1;
            for (int laneIndex = 0;
                 laneIndex < _towerDefenseAutoplayLaneAnchors.Count; laneIndex++)
            {
                if (_towerDefenseAutoplayLaneAnchors[laneIndex].KeyCellIndex !=
                    keyCellIndex) continue;
                existingIndex = laneIndex;
                break;
            }
            if (existingIndex >= 0)
            {
                AutoplayLaneAnchor existing =
                    _towerDefenseAutoplayLaneAnchors[existingIndex];
                if (seconds < existing.NextSpawnSeconds)
                {
                    existing.NextSpawnSeconds = seconds;
                    existing.CoverageCellIndex = coverageCellIndex;
                    _towerDefenseAutoplayLaneAnchors[existingIndex] = existing;
                }
                continue;
            }
            _towerDefenseAutoplayLaneAnchors.Add(new AutoplayLaneAnchor
            {
                KeyCellIndex = keyCellIndex,
                CoverageCellIndex = coverageCellIndex,
                NextSpawnSeconds = seconds
            });
        }
        return planningHorizon;
    }

    private bool TryTraceAutoplayLaneAnchor(RougeTowerDefenseMap map,
        Vector2Int source, out int keyCellIndex, out int coverageCellIndex)
    {
        keyCellIndex = -1;
        coverageCellIndex = -1;
        if (map == null || source.x < 0 || source.y < 0 ||
            source.x >= map.Width || source.y >= map.Height ||
            !map.IsGround(source)) return false;

        float bestCoverageDistance = float.PositiveInfinity;
        Vector2Int current = source;
        int cellCount = map.Width * map.Height;
        for (int step = 0; step < cellCount; step++)
        {
            int index = current.y * map.Width + current.x;
            if ((uint)index >=
                (uint)_towerDefenseAutoplayRouteDistanceByCell.Length) break;
            float routeDistance = _towerDefenseAutoplayRouteDistanceByCell[index];
            if (float.IsPositiveInfinity(routeDistance)) break;
            if (routeDistance >= TowerDefenseAutoplayLaneDefenseInnerCells &&
                routeDistance <= TowerDefenseAutoplayLaneDefenseOuterCells)
            {
                float coverageDistance = Mathf.Abs(routeDistance -
                    TowerDefenseAutoplayLaneDefenseAnchorCells);
                if (coverageDistance < bestCoverageDistance)
                {
                    bestCoverageDistance = coverageDistance;
                    coverageCellIndex = index;
                }
            }
            if (current == _towerDefenseAutoplayRouteMainCell) break;
            int nextIndex = (uint)index <
                            (uint)_towerDefenseAutoplayRouteNextByCell.Length
                ? _towerDefenseAutoplayRouteNextByCell[index]
                : -1;
            if ((uint)nextIndex >= (uint)cellCount || nextIndex == index) break;
            current = new Vector2Int(nextIndex % map.Width,
                nextIndex / map.Width);
        }
        // The anchor itself is the topology identity. Routes that have merged before
        // this defensible band are one lane; top/bottom or left/right approaches that
        // only meet at the core remain separate even if their final goal cell matches.
        keyCellIndex = coverageCellIndex;
        return coverageCellIndex >= 0;
    }

    private static float GetAutoplayCleanupCoverageUnits(
        RougeDefenseTower tower)
    {
        if (tower == null) return 0f;
        float rawPower = tower.Damage /
            Mathf.Max(0.03f, tower.EffectiveAttackInterval);
        rawPower *= 1f + Mathf.Max(0, tower.AttackTargetCount - 1) * 0.65f;
        rawPower *= 1f + Mathf.Min(1.2f,
            Mathf.Max(0f, tower.AoeRadius) * 0.09f);
        float units = Mathf.Log(1f + Mathf.Max(0f, rawPower)) / 4f;
        if (tower.TowerType == RougeTowerType.Ice) units += 0.35f;
        return Mathf.Clamp(units, 0.35f, 2.4f);
    }

    private static bool DoesAutoplayTowerCoverCell(RougeTowerDefenseMap map,
        RougeDefenseTower tower, Vector2Int cell)
    {
        if (map == null || tower == null || tower.AttackRange <= 0f) return false;
        Vector3 delta = tower.transform.position - map.CellCenter(cell);
        delta.y = 0f;
        return delta.sqrMagnitude <= tower.AttackRange * tower.AttackRange;
    }

    private static bool ShouldAutoplayFocusBoss(RougeDefenseTower tower)
    {
        return tower != null && tower.IsTargetedDamage &&
               tower.CanToggleTargetPriority;
    }

    private bool TryGetAutoplayLiveBossTarget(out Vector3 bossPosition,
        out float bossRadius)
    {
        bossPosition = _bossWorldPosition;
        bossRadius = Mathf.Max(0f, bossBalance.radius);
        if (!_bossSpawned || _bossEnemyIndex < 0) return false;

        if (_positionsA.IsCreated && _stateA.IsCreated &&
            _bossEnemyIndex < _positionsA.Length &&
            _bossEnemyIndex < _stateA.Length)
        {
            if (_stateA[_bossEnemyIndex].x <= 0f) return false;
            float4 position = _positionsA[_bossEnemyIndex];
            bossPosition = new Vector3(position.x, renderHeight, position.z);
            bossRadius = Mathf.Max(0f, position.w);
            return true;
        }

        return _bossCurrentHealth > 0f;
    }

    private static bool ShouldAutoplayUseBossFocus(RougeDefenseTower tower,
        Vector3 bossPosition, float bossRadius)
    {
        if (!ShouldAutoplayFocusBoss(tower)) return false;
        Vector3 delta = tower.transform.position - bossPosition;
        delta.y = 0f;
        float focusRange = tower.AttackRange + Mathf.Max(0f, bossRadius);
        return delta.sqrMagnitude <= focusRange * focusRange;
    }

    private void RestoreAllAutoplayBossPriorityOverrides()
    {
        bool changed = false;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower != null && tower.SetAutoplayCleanupFirst(false))
                changed = true;
        }
        for (int i = _towerDefenseAutoplayBossOverrides.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _towerDefenseAutoplayBossOverrides[i];
            if (IsAutoplayStandardTower(tower) &&
                tower.TargetPriority == RougeTowerTargetPriority.BossFirst)
            {
                tower.ToggleTargetPriority();
                changed = true;
            }
        }
        _towerDefenseAutoplayBossOverrides.Clear();
        _towerDefenseAutoplayBossFocusCandidates.Clear();
        _towerDefenseAutoplayDesiredBossFocus.Clear();
        _towerDefenseAutoplayReservedBossGuards.Clear();
        if (!changed) return;
        _towerTargetScheduledCount = 0;
        RefreshTowerDefenseUi(true);
    }

    private bool TryBuildAutoplayStandardTower(RougeTowerDefenseMap map,
        AutoplayBuildChoice choice, string reason, out string decision)
    {
        decision = string.Empty;
        if (map == null || !choice.IsValid) return false;
        RougeTowerType type = choice.Type;
        Vector2Int cell = choice.Cell;

        GameObject towerObject = InstantiateTowerPrefab(type);
        if (towerObject == null)
        {
            _towerDefenseAutoplayBuildCursor =
                (choice.BuildOrderIndex + 1) % TowerDefenseAutoplayBuildOrder.Length;
            decision = $"无法部署 {TowerDefenseVisuals.GetTowerName(type)}：预制体不可用。";
            return false;
        }

        towerObject.SetActive(false);
        RougeDefenseTower tower = towerObject.GetComponent<RougeDefenseTower>();
        if (tower == null)
        {
            Destroy(towerObject);
            decision = $"无法部署 {TowerDefenseVisuals.GetTowerName(type)}：缺少塔楼组件。";
            return false;
        }

        tower.Configure(type, true);
        tower.transform.position = map.CellCenter(cell, 0.05f);
        RougeTowerPlaceEffect placeEffect = GetTowerPlaceEffectAtWorld(
            tower.transform.position);
        tower.ApplyTowerPlaceEffect(placeEffect);
        tower.SetReinforcementAuraLevel(GetReinforcementAuraLevelAtCell(map, cell));
        int originalCost = tower.PlacementCost;
        int paidCost = GetTowerDefenseAutoplayPaidCost(originalCost);

        // Re-check after prefab configuration so balance/map modifiers remain the
        // authority even if they changed after the type-selection pass.
        if (_towerDefenseGold < paidCost || IsTowerTypeDisabled(type) ||
            !IsAutoplayBuildCellFree(map, cell))
        {
            Destroy(towerObject);
            return false;
        }

        _towerDefenseGold -= paidCost;
        RecordTowerDefenseGoldSpent(paidCost);
        tower.FinalizePlacement();
        tower.RecordActualGoldPaid(originalCost, paidCost);
        tower.name = tower.DisplayName + " Lv." + tower.Level;
        towerObject.SetActive(true);
        _defenseTowers.Add(tower);
        _towerDefenseAutoplayOwnedTowers.Add(tower);
        _towerDefenseAutoplayOwnedTowerBuildTimes.Add(Mathf.Max(0f, _survivalTime));
        _towerDefenseAutoplayLastCapitalActionGameTime =
            Mathf.Max(0f, _survivalTime);
        tower.PlayPlacementSound();
        PlayTowerConstructionEffect(tower);
        RefreshReinforcementTowerAuras();
        _towerTargetScheduledCount = 0;
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        RefreshTowerDefenseUi(true);

        _towerDefenseAutoplayBuildCursor =
            (choice.BuildOrderIndex + 1) % TowerDefenseAutoplayBuildOrder.Length;
        _towerDefenseAutoplayStyleDecisionSequence++;
        string effectLabel = placeEffect == RougeTowerPlaceEffect.None
            ? "普通塔位"
            : GetTowerPlaceEffectShortName(placeEffect);
        string guardNote = choice.GoalDefenseScore >= 145f
            ? "，也能照看主塔附近"
            : string.Empty;
        decision = $"{reason}：把 {tower.DisplayName} 放到{effectLabel}，" +
                   $"主要应对{GetAutoplayPressureLayerLabel(choice.DominantPressureLayer)}" +
                   $"{guardNote}；{FormatAutoplayCost(originalCost, paidCost)}。";
        ClearPendingAutoplayDialogue(AutoplayDialogueCategory.Saving);
        if (choice.PlaceEffect != RougeTowerPlaceEffect.None &&
            choice.TileScore >= 96f && choice.OpportunityPenalty <= 0f)
            QueueAutoplayDialogue(AutoplayDialogueCategory.GreatTile);
        else if (paidCost < originalCost)
            QueueAutoplayDialogue(AutoplayDialogueCategory.Discount);
        TryQueueAutoplayActionDialogue(AutoplayDialogueCategory.BuildTower,
            TowerDefenseAutoplayDialogueTriggers.towerBuildDialogueChance,
            TowerDefenseAutoplayDialogueTriggers.towerBuildDialogueCooldownSeconds,
            ref _towerDefenseAutoplayLastBuildDialogueGameTime);
        return true;
    }

    private bool TryUpgradeAutoplayTower(AutoplayUpgradeChoice choice,
        out string decision)
    {
        decision = string.Empty;
        RougeDefenseTower candidate = choice.Tower;
        if (!choice.IsValid || !IsAutoplayStandardTower(candidate) ||
            !candidate.CanUpgrade) return false;
        int originalCost = candidate.UpgradeCost;
        int paidCost = GetTowerDefenseAutoplayPaidCost(originalCost);
        if (_towerDefenseGold < paidCost) return false;
        bool choseBranch = candidate.RequiresUpgradeChoice;
        string routeExplanation = string.Empty;
        bool upgraded;

        if (candidate.RequiresUpgradeChoice)
        {
            int liveChoiceIndex = GetAutoplayUpgradeChoice(candidate, choice,
                out routeExplanation);
            int choiceIndex = (uint)choice.SpecializationChoiceIndex <= 1u
                ? choice.SpecializationChoiceIndex
                : liveChoiceIndex;
            upgraded = candidate.UpgradeSpecializationChoice(choiceIndex);
        }
        else
        {
            upgraded = candidate.Upgrade();
        }

        if (!upgraded) return false;
        _towerDefenseGold -= paidCost;
        RecordTowerDefenseGoldSpent(paidCost);
        candidate.RecordActualGoldPaid(originalCost, paidCost);
        PlayTowerUpgradeFeedback(candidate);
        candidate.name = candidate.DisplayName + " Lv." + candidate.Level;
        if (candidate.CreatesPermanentFrostTiles)
            ApplyPermanentFrostAroundIceTower(candidate);
        candidate.SetRangeVisibility(_towerPlacementMode);
        _towerDefenseAutoplayLastCapitalActionGameTime =
            Mathf.Max(0f, _survivalTime);
        _towerDefenseAutoplayStyleDecisionSequence++;
        RefreshTowerDefenseUi(true);

        string routeSuffix = string.IsNullOrEmpty(routeExplanation)
            ? string.Empty
            : $"，固定选择“{routeExplanation}”";
        decision = $"升级：{candidate.DisplayName} 到 Lv.{candidate.Level}" +
                   $"{routeSuffix}；这一笔主要补" +
                   $"{GetAutoplayPressureLayerLabel(choice.DominantPressureLayer)}火力；" +
                   $"{FormatAutoplayCost(originalCost, paidCost)}。";
        ClearPendingAutoplayDialogue(AutoplayDialogueCategory.Saving);
        if (choseBranch)
            QueueAutoplayDialogue(AutoplayDialogueCategory.Branch);
        else if (paidCost < originalCost)
            QueueAutoplayDialogue(AutoplayDialogueCategory.Discount);
        TryQueueAutoplayActionDialogue(AutoplayDialogueCategory.UpgradeTower,
            TowerDefenseAutoplayDialogueTriggers.towerUpgradeDialogueChance,
            TowerDefenseAutoplayDialogueTriggers
                .towerUpgradeDialogueCooldownSeconds,
            ref _towerDefenseAutoplayLastUpgradeDialogueGameTime);
        return true;
    }

    private void UpdateAutoplayTowerPerformanceObservations(
        RougeTowerDefenseMap map)
    {
        if (map == null || _simulationResultBackBufferReady ||
            !_towerDamageTotalsFixed.IsCreated) return;

        float gameTime = Mathf.Max(0f, _survivalTime);
        _towerDefenseAutoplayStaleTowerObservations.Clear();
        foreach (KeyValuePair<RougeDefenseTower, AutoplayTowerObservation> pair in
                 _towerDefenseAutoplayTowerObservations)
        {
            if (!IsAutoplayStandardTower(pair.Key) ||
                !_defenseTowers.Contains(pair.Key))
                _towerDefenseAutoplayStaleTowerObservations.Add(pair.Key);
        }
        for (int i = 0; i < _towerDefenseAutoplayStaleTowerObservations.Count; i++)
            _towerDefenseAutoplayTowerObservations.Remove(
                _towerDefenseAutoplayStaleTowerObservations[i]);

        Array.Clear(_towerDefenseAutoplayPerformanceWeightByType, 0,
            _towerDefenseAutoplayPerformanceWeightByType.Length);
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower)) continue;
            int typeIndex = (int)tower.TowerType;
            _towerDefenseAutoplayPerformanceWeightByType[typeIndex] +=
                EstimateAutoplayObservedTowerWeight(tower);
            if (!_towerDefenseAutoplayTowerObservations.ContainsKey(tower))
                _towerDefenseAutoplayTowerObservations.Add(tower,
                    new AutoplayTowerObservation());
        }

        if (!_towerDefenseAutoplayTowerObservationInitialized)
        {
            for (int typeIndex = 0;
                 typeIndex < TowerDefenseVisuals.StandardTowerTypeCount;
                 typeIndex++)
                _towerDefenseAutoplayLastTowerDamageFixed[typeIndex] =
                    _towerDamageTotalsFixed[typeIndex];
            _towerDefenseAutoplayLastObservedGoldEarned =
                Mathf.Max(0, _towerDefenseGoldEarnedTotal);
            _towerDefenseAutoplayTowerObservationInitialized = true;
            _towerDefenseAutoplayLastTowerObservationAt = gameTime;
            return;
        }

        float elapsed = gameTime - _towerDefenseAutoplayLastTowerObservationAt;
        if (elapsed < 0.05f) return;
        for (int typeIndex = 0;
             typeIndex < TowerDefenseVisuals.StandardTowerTypeCount;
             typeIndex++)
        {
            if (_towerDamageTotalsFixed[typeIndex] >=
                _towerDefenseAutoplayLastTowerDamageFixed[typeIndex]) continue;
            // Native totals were reset by a new simulation/session. Re-establish a
            // clean baseline instead of treating old damage as belonging to new towers.
            ResetAutoplayTowerPerformanceObservations();
            UpdateAutoplayTowerPerformanceObservations(map);
            return;
        }

        float smoothing = 1f - Mathf.Exp(-elapsed /
            TowerDefenseAutoplaySupportDamageRateHorizonSeconds);
        float totalDamageDelta = 0f;
        // Combat telemetry is accumulated by tower type, not by tower instance.
        // Split each type delta by current effective combat/utility weight; this is
        // deliberately an estimate and never feeds the Burst simulation itself.
        for (int typeIndex = 0;
             typeIndex < TowerDefenseVisuals.StandardTowerTypeCount;
             typeIndex++)
        {
            long current = _towerDamageTotalsFixed[typeIndex];
            float damageDelta = Mathf.Max(0f,
                (current - _towerDefenseAutoplayLastTowerDamageFixed[typeIndex]) /
                1000f);
            _towerDefenseAutoplayDamageDeltaByType[typeIndex] = damageDelta;
            float rate = damageDelta / Mathf.Max(0.05f, elapsed);
            _towerDefenseAutoplayRecentDamageRateByType[typeIndex] = Mathf.Lerp(
                _towerDefenseAutoplayRecentDamageRateByType[typeIndex], rate,
                smoothing);
            _towerDefenseAutoplayLastTowerDamageFixed[typeIndex] = current;
            totalDamageDelta += damageDelta;
        }

        int currentGoldEarned = Mathf.Max(0, _towerDefenseGoldEarnedTotal);
        int goldDelta = Mathf.Max(0,
            currentGoldEarned - _towerDefenseAutoplayLastObservedGoldEarned);
        _towerDefenseAutoplayLastObservedGoldEarned = currentGoldEarned;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) ||
                !_towerDefenseAutoplayTowerObservations.TryGetValue(tower,
                    out AutoplayTowerObservation observation)) continue;
            int typeIndex = (int)tower.TowerType;
            float typeWeight = _towerDefenseAutoplayPerformanceWeightByType[typeIndex];
            if (typeWeight <= 0.001f) continue;
            float share = EstimateAutoplayObservedTowerWeight(tower) / typeWeight;
            float attributedDamage =
                _towerDefenseAutoplayDamageDeltaByType[typeIndex] * share;
            observation.ObservedSeconds += elapsed;
            observation.ObservedDamage += attributedDamage;
            if (goldDelta > 0 && totalDamageDelta > 0.001f &&
                attributedDamage > 0f)
            {
                // Kill rewards have no per-source counter. Attribute only 35% of the
                // observed gain by damage share, with a small bounty-tile adjustment,
                // so economy remains supporting evidence rather than a false fact.
                float bountyConfidence = 1f + Mathf.Clamp(
                    tower.KillGoldPercentBonus * 0.005f, 0f, 0.5f);
                observation.AttributedKillGold += goldDelta *
                    (attributedDamage / totalDamageDelta) *
                    TowerDefenseAutoplaySupportGoldAttributionConfidence *
                    bountyConfidence;
            }
            _towerDefenseAutoplayTowerObservations[tower] = observation;
        }
        _towerDefenseAutoplayLastTowerObservationAt = gameTime;
    }

    private static float EstimateAutoplayObservedTowerWeight(
        RougeDefenseTower tower)
    {
        if (!IsAutoplayStandardTower(tower)) return 0f;
        float power = Mathf.Max(0f, tower.Damage) /
                      Mathf.Max(0.03f, tower.EffectiveAttackInterval);
        power *= 1f + Mathf.Max(0, tower.AttackTargetCount - 1) * 0.1f;
        power *= 1f + Mathf.Max(0, tower.AttackProjectileCount - 1) * 0.12f;
        if (tower.AoeRadius > 0f)
            power *= 1f + Mathf.Min(1.25f, tower.AoeRadius * 0.085f);
        if (tower.TowerType == RougeTowerType.Ice)
            power += Mathf.Max(45f, power * 0.3f);
        return Mathf.Max(1f, power);
    }

    private static float GetAutoplayTowerUtilityFactor(RougeTowerType type)
    {
        switch (type)
        {
            case RougeTowerType.Ice: return 0.42f;
            case RougeTowerType.Cannon: return 0.2f;
            case RougeTowerType.Flame: return 0.22f;
            case RougeTowerType.Laser: return 0.1f;
            case RougeTowerType.OrbitSphere: return 0.18f;
            case RougeTowerType.RocketBarrage: return 0.22f;
            case RougeTowerType.PiercingLaser: return 0.04f;
            default: return 0.04f;
        }
    }

    private float GetAutoplayTowerRescueFactor(RougeTowerDefenseMap map,
        Vector2Int cell)
    {
        if (map == null) return 0f;
        int index = cell.y * map.Width + cell.x;
        if ((uint)index >= (uint)_towerDefenseAutoplayEnemyPressureByCell.Length)
            return 0f;
        float urgent = (uint)index <
                       (uint)_towerDefenseAutoplayUrgentPressureByCell.Length
            ? _towerDefenseAutoplayUrgentPressureByCell[index]
            : 0f;
        float activeUrgent = (uint)index <
                             (uint)_towerDefenseAutoplayActiveUrgentPressureByCell.Length
            ? _towerDefenseAutoplayActiveUrgentPressureByCell[index]
            : 0f;
        float pressure = _towerDefenseAutoplayEnemyPressureByCell[index];
        float routeProximity = 0f;
        if ((uint)index < (uint)_towerDefenseAutoplayRouteDistanceByCell.Length &&
            float.IsFinite(_towerDefenseAutoplayRouteDistanceByCell[index]))
            routeProximity = 1f - Mathf.Clamp01(
                _towerDefenseAutoplayRouteDistanceByCell[index] /
                Mathf.Max(1f, _towerDefenseAutoplayMaximumRouteDistance));
        return Mathf.Clamp01((urgent + activeUrgent) * 0.12f +
                             pressure * 0.025f + routeProximity * 0.24f);
    }

    private bool TryGetProvenAutoplaySupportLeader(RougeTowerDefenseMap map,
        out AutoplayTowerPerformance leader, out float confidence)
    {
        leader = default;
        confidence = 0f;
        _towerDefenseAutoplayTowerPerformances.Clear();
        if (map == null || !_towerDefenseAutoplayTowerObservationInitialized ||
            _towerDefenseAutoplayTowerObservations.Count == 0) return false;

        Array.Clear(_towerDefenseAutoplayPerformanceWeightByType, 0,
            _towerDefenseAutoplayPerformanceWeightByType.Length);
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (IsAutoplayStandardTower(tower))
                _towerDefenseAutoplayPerformanceWeightByType[(int)tower.TowerType] +=
                    EstimateAutoplayObservedTowerWeight(tower);
        }

        float gameTime = Mathf.Max(0f, _survivalTime);
        float maxContribution = 0f;
        float maxReturn = 0f;
        float maxRecentRate = 0f;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) ||
                !_towerDefenseAutoplayTowerObservations.TryGetValue(tower,
                    out AutoplayTowerObservation observation) ||
                !map.WorldToCell(tower.transform.position, out Vector2Int cell))
                continue;

            float weight = EstimateAutoplayObservedTowerWeight(tower);
            float typeWeight = Mathf.Max(0.01f,
                _towerDefenseAutoplayPerformanceWeightByType[(int)tower.TowerType]);
            float utilityFactor = GetAutoplayTowerUtilityFactor(tower.TowerType);
            float rescueFactor = GetAutoplayTowerRescueFactor(map, cell);
            float observationAge = Mathf.Max(0f, observation.ObservedSeconds);
            float staticUtility = weight * Mathf.Min(observationAge, 45f) * 0.035f *
                                  (utilityFactor + rescueFactor * 0.18f);
            float contribution = observation.ObservedDamage *
                                 (1f + utilityFactor + rescueFactor * 0.18f) +
                                 staticUtility + observation.AttributedKillGold *
                                 TowerDefenseAutoplaySupportGoldDamageEquivalent;
            float recentRate = _towerDefenseAutoplayRecentDamageRateByType[
                                   (int)tower.TowerType] * weight / typeWeight *
                               (1f + utilityFactor + rescueFactor * 0.12f);
            float roi = contribution / Mathf.Max(100f, tower.InvestedGold);
            _towerDefenseAutoplayTowerPerformances.Add(
                new AutoplayTowerPerformance
                {
                    Tower = tower,
                    Cell = cell,
                    ObservationAge = observationAge,
                    ContributionValue = contribution,
                    ReturnOnInvestment = roi,
                    RecentDamageRate = recentRate,
                    RescueFactor = rescueFactor
                });
            maxContribution = Mathf.Max(maxContribution, contribution);
            maxReturn = Mathf.Max(maxReturn, roi);
            maxRecentRate = Mathf.Max(maxRecentRate, recentRate);
        }
        if (_towerDefenseAutoplayTowerPerformances.Count == 0) return false;

        int bestIndex = 0;
        int retainedIndex = -1;
        for (int i = 0; i < _towerDefenseAutoplayTowerPerformances.Count; i++)
        {
            AutoplayTowerPerformance performance =
                _towerDefenseAutoplayTowerPerformances[i];
            performance.StrategicScore =
                performance.ContributionValue / Mathf.Max(0.01f, maxContribution) *
                    0.58f +
                performance.ReturnOnInvestment / Mathf.Max(0.0001f, maxReturn) *
                    0.2f +
                performance.RecentDamageRate / Mathf.Max(0.01f, maxRecentRate) *
                    0.14f +
                performance.RescueFactor * 0.08f;
            _towerDefenseAutoplayTowerPerformances[i] = performance;
            if (performance.StrategicScore >
                _towerDefenseAutoplayTowerPerformances[bestIndex].StrategicScore)
                bestIndex = i;
            if (performance.Tower ==
                _towerDefenseAutoplayProvisionalSupportLeader)
                retainedIndex = i;
        }

        AutoplayTowerPerformance best =
            _towerDefenseAutoplayTowerPerformances[bestIndex];
        AutoplayTowerPerformance selected = best;
        if (retainedIndex >= 0)
        {
            AutoplayTowerPerformance retained =
                _towerDefenseAutoplayTowerPerformances[retainedIndex];
            if (retained.StrategicScore >= best.StrategicScore *
                TowerDefenseAutoplaySupportLeaderRetentionRatio)
                selected = retained;
        }
        if (selected.Tower != _towerDefenseAutoplayProvisionalSupportLeader)
        {
            _towerDefenseAutoplayProvisionalSupportLeader = selected.Tower;
            _towerDefenseAutoplaySupportLeaderSince = gameTime;
        }
        _towerDefenseAutoplaySupportLeaderScore = selected.StrategicScore;
        leader = selected;

        float observationConfidence = Mathf.Clamp01(selected.ObservationAge /
            TowerDefenseAutoplaySupportObservationSeconds);
        float persistenceConfidence = Mathf.Clamp01((gameTime -
            _towerDefenseAutoplaySupportLeaderSince) /
            TowerDefenseAutoplaySupportLeaderHoldSeconds);
        confidence = Mathf.Min(observationConfidence, persistenceConfidence);
        bool hasMeasuredOutput =
            _towerDefenseAutoplayTowerObservations.TryGetValue(selected.Tower,
                out AutoplayTowerObservation selectedObservation) &&
            (selectedObservation.ObservedDamage >= 20f ||
             selected.RecentDamageRate >= 0.25f);
        return confidence >= 0.999f && hasMeasuredOutput &&
               selected.StrategicScore >= 0.55f;
    }

    private bool TryGetAutoplayTowerPerformance(RougeDefenseTower tower,
        out AutoplayTowerPerformance performance)
    {
        for (int i = 0; i < _towerDefenseAutoplayTowerPerformances.Count; i++)
        {
            if (_towerDefenseAutoplayTowerPerformances[i].Tower != tower) continue;
            performance = _towerDefenseAutoplayTowerPerformances[i];
            return true;
        }
        performance = default;
        return false;
    }

    private void EvaluateAutoplaySupportChoices(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot,
        out AutoplaySupportChoice bestOverall,
        out AutoplaySupportChoice bestAffordable)
    {
        bestOverall = default;
        bestAffordable = default;
        if (map == null ||
            IsTowerTypeDisabled(RougeTowerType.ReinforcementTower) ||
            CountAutoplayStandardTowers() < 4) return;
        if (!TryGetProvenAutoplaySupportLeader(map,
                out AutoplayTowerPerformance leader,
                out float observationConfidence)) return;
        // Reinforcement auras stack, so without this guard the same proven cluster
        // can purchase several support towers in consecutive decision ticks.
        if (IsAutoplayTowerCoveredByReinforcement(map, leader.Tower)) return;

        int cost = GetReinforcementTowerGoldCost();
        int auraLevel = Mathf.Max(1,
            TowerDefenseVisuals.GetReinforcementAuraBuffLevel());
        int auraRange = Mathf.Max(1,
            TowerDefenseVisuals.GetReinforcementAuraRangeCells());
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (!IsAutoplayBuildCellFree(map, cell)) continue;
            if (Mathf.Max(Mathf.Abs(leader.Cell.x - cell.x),
                    Mathf.Abs(leader.Cell.y - cell.y)) > auraRange) continue;
            int affected = 0;
            int highValue = 0;
            bool leaderMarginallyAffected = false;
            float marginalPower = 0f;
            float protectedInvestment = 0f;
            float performanceCoverage = 0f;
            for (int i = 0; i < _defenseTowers.Count; i++)
            {
                RougeDefenseTower tower = _defenseTowers[i];
                if (!IsAutoplayStandardTower(tower) ||
                    !map.WorldToCell(tower.transform.position,
                        out Vector2Int towerCell) ||
                    Mathf.Max(Mathf.Abs(towerCell.x - cell.x),
                        Mathf.Abs(towerCell.y - cell.y)) > auraRange) continue;

                int damageLevel = tower.GetRawBuffLevel(RougeTowerBuffStat.Damage);
                int speedLevel = tower.GetRawBuffLevel(RougeTowerBuffStat.AttackSpeed);
                int rangeLevel = tower.GetRawBuffLevel(RougeTowerBuffStat.Range);
                float damageRatio = RougeTowerBuffMath.GetMultiplier(
                    damageLevel + auraLevel) /
                    Mathf.Max(0.01f,
                        RougeTowerBuffMath.GetMultiplier(damageLevel));
                float speedRatio = RougeTowerBuffMath.GetMultiplier(
                    speedLevel + auraLevel) /
                    Mathf.Max(0.01f,
                        RougeTowerBuffMath.GetMultiplier(speedLevel));
                float rangeRatio = RougeTowerBuffMath.GetMultiplier(
                    rangeLevel + auraLevel) /
                    Mathf.Max(0.01f,
                        RougeTowerBuffMath.GetMultiplier(rangeLevel));
                float marginalRatio = damageRatio * speedRatio - 1f +
                                      (rangeRatio - 1f) * 0.32f;
                if (marginalRatio <= 0.015f) continue;
                if (tower == leader.Tower) leaderMarginallyAffected = true;
                float combatPower = EstimateAutoplayObservedTowerWeight(tower);
                marginalPower += combatPower * marginalRatio;
                protectedInvestment += Mathf.Max(0, tower.InvestedGold);
                if (TryGetAutoplayTowerPerformance(tower,
                        out AutoplayTowerPerformance performance))
                {
                    performanceCoverage += performance.StrategicScore;
                    if (performance.StrategicScore >=
                        _towerDefenseAutoplaySupportLeaderScore * 0.72f)
                        highValue++;
                }
                affected++;
            }
            if (affected < 3 || !leaderMarginallyAffected) continue;

            int cellIndex = y * map.Width + x;
            RougeTowerPlaceEffect effect =
                _towerDefenseAutoplayEffectiveEffects[cellIndex];
            // A support structure cannot convert personal damage/range/level tile
            // bonuses into aura strength, so reserve every enhanced tile for a combat
            // tower whenever a normal alternative exists.
            float specialTilePenalty = effect == RougeTowerPlaceEffect.None
                ? 0f
                : IsAutoplayDedicatedEffect(effect) ? 820f : 560f;
            float pressureFit = 1f + Mathf.Clamp01(
                snapshot.TotalPressure / 18f) * 0.08f + Mathf.Clamp01(
                snapshot.UrgentPressure / 3f) * 0.04f;
            float utility = Mathf.Max(1f, (marginalPower * 22f +
                affected * 112f + highValue * 118f +
                performanceCoverage * 245f +
                Mathf.Sqrt(protectedInvestment) * 10f +
                Mathf.Sqrt(Mathf.Max(0f, leader.ContributionValue)) * 7f) *
                pressureFit - specialTilePenalty);
            // Support's internal ranking deliberately includes observation history,
            // protected investment and cluster quality. Those raw terms are much
            // larger than build/upgrade utility, so expose a separately calibrated
            // marginal-defense value to the cross-category capital auction.
            float capitalGain = Mathf.Max(0f, (
                Mathf.Log(1f + Mathf.Max(0f, marginalPower)) * 42f +
                affected * 38f + highValue * 46f +
                performanceCoverage * 88f +
                Mathf.Log(1f + Mathf.Max(0f, protectedInvestment)) * 18f +
                Mathf.Log(1f + Mathf.Max(0f,
                    leader.ContributionValue)) * 22f) * pressureFit -
                specialTilePenalty * 0.42f);
            float efficiency = utility * 100f / Mathf.Max(100f, cost + 180f);
            AutoplaySupportChoice choice = new AutoplaySupportChoice
            {
                IsValid = true,
                Cell = cell,
                Cost = cost,
                AffectedTowers = affected,
                HighValueTowers = highValue,
                AnchorType = leader.Tower.TowerType,
                AnchorScore = leader.StrategicScore,
                ObservationConfidence = observationConfidence,
                CoversProvenLeader = leaderMarginallyAffected,
                Utility = utility,
                Efficiency = efficiency,
                CapitalGain = capitalGain
            };
            if (!bestOverall.IsValid || choice.Efficiency > bestOverall.Efficiency)
                bestOverall = choice;
            if (GetTowerDefenseAutoplayPaidCost(cost) <= _towerDefenseGold &&
                (!bestAffordable.IsValid ||
                 choice.Efficiency > bestAffordable.Efficiency))
                bestAffordable = choice;
        }
    }

    private bool IsAutoplayTowerCoveredByReinforcement(
        RougeTowerDefenseMap map, RougeDefenseTower target)
    {
        if (map == null || target == null ||
            !map.WorldToCell(target.transform.position, out Vector2Int targetCell))
            return false;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower source = _defenseTowers[i];
            if (source == null || !source.IsReinforcementTower ||
                !map.WorldToCell(source.transform.position,
                    out Vector2Int sourceCell)) continue;
            int distance = Mathf.Max(Mathf.Abs(targetCell.x - sourceCell.x),
                Mathf.Abs(targetCell.y - sourceCell.y));
            if (distance <= source.ReinforcementAuraRangeCells) return true;
        }
        return false;
    }

    private bool ShouldReserveForAutoplaySupport(AutoplaySupportChoice best,
        AutoplaySupportChoice affordable, float mainTowerHealthRatio)
    {
        if (!best.IsValid || !best.CoversProvenLeader ||
            best.ObservationConfidence < 0.999f || affordable.IsValid ||
            best.AffectedTowers < 4 || best.HighValueTowers < 1 ||
            _towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Opening ||
            _towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Emergency ||
            _towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.BossFight ||
            mainTowerHealthRatio < 0.75f ||
            _towerDefenseAutoplaySustainedNearBaseCrisis ||
            _towerDefenseAutoplaySustainedMainTowerDamage ||
            _towerDefenseAutoplayEnemyFlowBacklog >= 0.2f) return false;
        // Reserve only near the finish line. This realizes the proven support plan
        // without letting an expensive aura freeze ordinary defense for a whole wave.
        if (_towerDefenseGold < Mathf.CeilToInt(best.Cost * 0.65f)) return false;
        int shortfall = Mathf.Max(0, best.Cost - _towerDefenseGold);
        if (shortfall > Mathf.Max(1800,
                Mathf.RoundToInt(_towerDefenseGold * 0.55f))) return false;
        return true;
    }

    private string DescribeAutoplaySupportSavingPlan(
        AutoplaySupportChoice support)
    {
        QueueAutoplayDialogue(AutoplayDialogueCategory.Saving);
        return $"协同投资：持续领先的" +
               $"{TowerDefenseVisuals.GetTowerName(support.AnchorType)}位于增幅圈内，" +
               $"[{support.Cell.x}, {support.Cell.y}] 还能覆盖 " +
               $"{support.AffectedTowers} 座塔，保留预算，还差 " +
               $"{Mathf.Max(0, support.Cost - _towerDefenseGold)} 金币。";
    }

    private string DescribeAutoplayChargeSavingPlan(
        AutoplayChargeChoice charge)
    {
        QueueAutoplayDialogue(AutoplayDialogueCategory.Saving);
        if (!charge.IsValid || charge.TargetTower == null)
            return "格位投资：暂时保留预算，等待可靠的充能目标。";
        return $"格位投资：准备在 [{charge.OwnerCell.x}, " +
               $"{charge.OwnerCell.y}] 为{charge.TargetTower.DisplayName}充能，" +
               $"还差 {Mathf.Max(0, charge.PaidCost - _towerDefenseGold)} 金币。";
    }

    private bool TryBuildAutoplaySupportTower(RougeTowerDefenseMap map,
        AutoplaySupportChoice choice, out string decision)
    {
        decision = string.Empty;
        if (map == null || !choice.IsValid || !choice.CoversProvenLeader ||
            choice.ObservationConfidence < 0.999f ||
            GetTowerDefenseAutoplayPaidCost(choice.Cost) > _towerDefenseGold ||
            IsTowerTypeDisabled(RougeTowerType.ReinforcementTower) ||
            !IsAutoplayBuildCellFree(map, choice.Cell)) return false;
        if (_towerDefenseAutoplayProvisionalSupportLeader == null ||
            !map.WorldToCell(
                _towerDefenseAutoplayProvisionalSupportLeader.transform.position,
                out Vector2Int leaderCell) ||
            Mathf.Max(Mathf.Abs(leaderCell.x - choice.Cell.x),
                Mathf.Abs(leaderCell.y - choice.Cell.y)) > Mathf.Max(1,
                TowerDefenseVisuals.GetReinforcementAuraRangeCells())) return false;

        GameObject towerObject = InstantiateTowerPrefab(
            RougeTowerType.ReinforcementTower);
        if (towerObject == null)
        {
            decision = "无法部署强化塔：预制体不可用。";
            return false;
        }
        towerObject.SetActive(false);
        RougeDefenseTower tower = towerObject.GetComponent<RougeDefenseTower>();
        if (tower == null)
        {
            Destroy(towerObject);
            decision = "无法部署强化塔：缺少塔楼组件。";
            return false;
        }

        tower.ConfigureAsReinforcementTower(true);
        tower.SetReinforcementTowerPlacementCost(choice.Cost);
        tower.transform.position = map.CellCenter(choice.Cell, 0.05f);
        RougeTowerPlaceEffect placeEffect = GetTowerPlaceEffectAtWorld(
            tower.transform.position);
        tower.ApplyTowerPlaceEffect(placeEffect);
        tower.SetReinforcementAuraLevel(
            GetReinforcementAuraLevelAtCell(map, choice.Cell));
        int paidCost = GetTowerDefenseAutoplayPaidCost(tower.PlacementCost);
        if (paidCost > _towerDefenseGold ||
            !IsAutoplayBuildCellFree(map, choice.Cell))
        {
            Destroy(towerObject);
            return false;
        }

        _towerDefenseGold -= paidCost;
        RecordTowerDefenseGoldSpent(paidCost);
        tower.FinalizePlacement();
        tower.RecordActualGoldPaid(choice.Cost, paidCost);
        tower.name = tower.DisplayName;
        towerObject.SetActive(true);
        _defenseTowers.Add(tower);
        tower.PlayPlacementSound();
        PlayTowerConstructionEffect(tower);
        RefreshReinforcementTowerAuras();
        _towerTargetScheduledCount = 0;
        _towerDefenseAutoplayLastCapitalActionGameTime =
            Mathf.Max(0f, _survivalTime);
        _towerDefenseAutoplayStyleDecisionSequence++;
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        RefreshTowerDefenseUi(true);
        decision = $"阵地协同：在 [{choice.Cell.x}, {choice.Cell.y}] 部署强化塔，" +
                   $"围绕持续领先的{TowerDefenseVisuals.GetTowerName(choice.AnchorType)}" +
                   $"同时增幅 {choice.AffectedTowers} 座主力塔；" +
                   $"{FormatAutoplayCost(choice.Cost, paidCost)}。";
        TryQueueAutoplayActionDialogue(AutoplayDialogueCategory.BuildTower,
            TowerDefenseAutoplayDialogueTriggers.towerBuildDialogueChance,
            TowerDefenseAutoplayDialogueTriggers.towerBuildDialogueCooldownSeconds,
            ref _towerDefenseAutoplayLastBuildDialogueGameTime);
        return true;
    }

    private void EvaluateAutoplayChargeChoice(RougeTowerDefenseMap map,
        out AutoplayChargeChoice best)
    {
        best = default;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        int standardTowerCount = CountAutoplayStandardTowers();
        if (map == null || loader == null || loader.Map != map ||
            IsTowerTypeDisabled(RougeTowerType.ChargeTower) ||
            CountOpenAutoplayBuildCells(map) < 2 || standardTowerCount < 4)
            return;

        int chargeTowerCount = CountAutoplayChargeTowers();
        int maximumChargeTowers = Mathf.Max(1, standardTowerCount / 4);
        if (chargeTowerCount >= maximumChargeTowers) return;

        // This also refreshes the per-tower performance list. A charge tower is a
        // costly map edit, so only measured combat output may nominate its target.
        if (!TryGetProvenAutoplaySupportLeader(map, out _, out _)) return;
        int originalCost = GetChargeTowerGoldCost();
        int paidCost = GetTowerDefenseAutoplayPaidCost(originalCost);
        int cellCount = map.Width * map.Height;

        for (int performanceIndex = 0;
             performanceIndex < _towerDefenseAutoplayTowerPerformances.Count;
             performanceIndex++)
        {
            AutoplayTowerPerformance performance =
                _towerDefenseAutoplayTowerPerformances[performanceIndex];
            RougeDefenseTower target = performance.Tower;
            if (!IsAutoplayStandardTower(target) || performance.ObservationAge <
                    TowerDefenseAutoplaySupportObservationSeconds ||
                performance.StrategicScore < 0.5f ||
                !_towerDefenseAutoplayTowerObservations.TryGetValue(target,
                    out AutoplayTowerObservation observation) ||
                observation.ObservedDamage < 20f &&
                performance.RecentDamageRate < 0.25f)
                continue;

            Vector2Int targetCell = performance.Cell;
            if (loader.TryGetRuntimeTowerPlaceEffect(targetCell, out _) ||
                loader.GetEffectiveTowerPlaceEffect(targetCell) !=
                RougeTowerPlaceEffect.None)
                continue;
            float expectedEffectUtility =
                GetExpectedAutoplayChargeEffectUtility(target);

            for (int offsetY = -1; offsetY <= 1; offsetY++)
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0) continue;
                Vector2Int ownerCell = targetCell +
                                       new Vector2Int(offsetX, offsetY);
                if (!IsAutoplayBuildCellFree(map, ownerCell) ||
                    loader.GetEffectiveTowerPlaceEffect(ownerCell) !=
                    RougeTowerPlaceEffect.None)
                    continue;

                int ownerIndex = ownerCell.y * map.Width + ownerCell.x;
                if ((uint)ownerIndex >= (uint)cellCount) continue;
                float ownerOpportunityCost =
                    GetAutoplayChargeOwnerOpportunityCost(cellCount, ownerIndex);
                float score = performance.StrategicScore * 220f +
                    Mathf.Log(1f + Mathf.Max(0f, performance.RecentDamageRate)) * 18f +
                    expectedEffectUtility - ownerOpportunityCost * 0.35f;
                if (!float.IsFinite(score) || score <= 0f) continue;
                if (best.IsValid && score <= best.TargetStrategicScore) continue;
                best = new AutoplayChargeChoice
                {
                    IsValid = true,
                    OwnerCell = ownerCell,
                    TargetCell = targetCell,
                    TargetTower = target,
                    OriginalCost = originalCost,
                    PaidCost = paidCost,
                    TargetStrategicScore = score,
                    ExpectedEffectUtility = expectedEffectUtility,
                    OwnerOpportunityCost = ownerOpportunityCost,
                    CapitalGain = score
                };
            }
        }
    }

    private float GetAutoplayChargeOwnerOpportunityCost(int cellCount,
        int cellIndex)
    {
        float opportunityCost = 0f;
        for (int typeIndex = 0;
             typeIndex < TowerDefenseVisuals.StandardTowerTypeCount; typeIndex++)
        {
            int priorIndex = typeIndex * cellCount + cellIndex;
            if ((uint)priorIndex >= (uint)_towerDefenseAutoplayBuildPriors.Length)
                continue;
            AutoplayBuildPrior prior = _towerDefenseAutoplayBuildPriors[priorIndex];
            if (prior.IsValid)
                opportunityCost = Mathf.Max(opportunityCost, prior.FixedScore);
        }
        return opportunityCost;
    }

    private static float GetExpectedAutoplayChargeEffectUtility(
        RougeDefenseTower target)
    {
        if (target == null || ChargeTowerEffectPool.Length == 0) return 0f;
        float sum = 0f;
        float maximum = 0f;
        for (int i = 0; i < ChargeTowerEffectPool.Length; i++)
        {
            float value = ScoreAutoplayChargeEffect(target,
                ChargeTowerEffectPool[i]);
            sum += value;
            maximum = Mathf.Max(maximum, value);
        }
        float mean = sum / ChargeTowerEffectPool.Length;
        return mean + (maximum - mean) * 0.35f;
    }

    private static float ScoreAutoplayChargeEffect(RougeDefenseTower target,
        RougeTowerPlaceEffect effect)
    {
        if (target == null) return float.NegativeInfinity;
        float score = GetAutoplayTileAffinity(target.TowerType, effect);
        if (effect == RougeTowerPlaceEffect.FreeLevelNoRefund &&
            !target.CanUpgrade)
            score -= 100f;
        if (effect == RougeTowerPlaceEffect.Discount && !target.CanUpgrade)
            score -= 80f;
        if (effect == RougeTowerPlaceEffect.Relocation) score -= 45f;
        return score;
    }

    private bool TryBuildAutoplayChargeTower(RougeTowerDefenseMap map,
        AutoplayChargeChoice choice, out string decision)
    {
        decision = string.Empty;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        RougeDefenseTower target = choice.TargetTower;
        if (map == null || loader == null || loader.Map != map ||
            !choice.IsValid || !IsAutoplayStandardTower(target) ||
            !_defenseTowers.Contains(target) ||
            IsTowerTypeDisabled(RougeTowerType.ChargeTower) ||
            CountOpenAutoplayBuildCells(map) < 2 ||
            !IsAutoplayBuildCellFree(map, choice.OwnerCell) ||
            loader.GetEffectiveTowerPlaceEffect(choice.OwnerCell) !=
            RougeTowerPlaceEffect.None ||
            !map.WorldToCell(target.transform.position, out Vector2Int targetCell) ||
            targetCell != choice.TargetCell ||
            Mathf.Max(Mathf.Abs(targetCell.x - choice.OwnerCell.x),
                Mathf.Abs(targetCell.y - choice.OwnerCell.y)) != 1 ||
            loader.TryGetRuntimeTowerPlaceEffect(targetCell, out _) ||
            loader.GetEffectiveTowerPlaceEffect(targetCell) !=
            RougeTowerPlaceEffect.None)
            return false;

        int standardTowerCount = CountAutoplayStandardTowers();
        if (CountAutoplayChargeTowers() >= Mathf.Max(1,
                standardTowerCount / 4)) return false;
        int originalCost = GetChargeTowerGoldCost();
        int paidCost = GetTowerDefenseAutoplayPaidCost(originalCost);
        if (_towerDefenseGold < paidCost) return false;

        GameObject towerObject = InstantiateTowerPrefab(RougeTowerType.ChargeTower);
        if (towerObject == null) return false;
        towerObject.SetActive(false);
        RougeDefenseTower tower = towerObject.GetComponent<RougeDefenseTower>();
        if (tower == null)
        {
            Destroy(towerObject);
            return false;
        }
        tower.ConfigureAsChargeTower(true);
        tower.SetChargeTowerPlacementCost(originalCost);
        tower.transform.position = map.CellCenter(choice.OwnerCell, 0.05f);

        // Roll exactly once at commit time. Evaluating on every planning tick would
        // silently give autoplay unlimited free rerolls compared with the player.
        RollChargeTowerEffectChoices();
        RougeTowerPlaceEffect selectedEffect = _chargeTowerEffectChoices[0];
        float selectedScore = ScoreAutoplayChargeEffect(target, selectedEffect);
        for (int i = 1; i < _chargeTowerEffectChoices.Length; i++)
        {
            RougeTowerPlaceEffect effect = _chargeTowerEffectChoices[i];
            float score = ScoreAutoplayChargeEffect(target, effect);
            if (score <= selectedScore) continue;
            selectedEffect = effect;
            selectedScore = score;
        }
        if (!loader.TrySetRuntimeTowerPlaceEffect(targetCell, selectedEffect))
        {
            Destroy(towerObject);
            return false;
        }

        _towerDefenseGold -= paidCost;
        RecordTowerDefenseGoldSpent(paidCost);
        tower.SetChargeTarget(targetCell, selectedEffect);
        tower.FinalizePlacement();
        tower.RecordActualGoldPaid(originalCost, paidCost);
        tower.name = tower.DisplayName;
        towerObject.SetActive(true);
        _defenseTowers.Add(tower);
        ApplyActivatedEffectToTowersInCell(targetCell, selectedEffect);
        tower.PlayPlacementSound();
        PlayTowerConstructionEffect(tower);
        RefreshReinforcementTowerAuras();
        _towerTargetScheduledCount = 0;
        _towerDefenseAutoplayPriorDirty = true;
        _towerDefenseAutoplayLastCapitalActionGameTime =
            Mathf.Max(0f, _survivalTime);
        _towerDefenseAutoplayLastChargeGameTime =
            Mathf.Max(0f, _survivalTime);
        _towerDefenseAutoplayStyleDecisionSequence++;
        SetTowerPlaceVisualsVisible(_towerPlacementMode);
        RefreshTowerDefenseUi(true);

        string effectName = RougeTowerPlaceEffectRules.GetDisplayName(selectedEffect);
        decision = $"专属充能：在 [{choice.OwnerCell.x}, {choice.OwnerCell.y}] " +
                   $"建造充能塔，为 [{targetCell.x}, {targetCell.y}] 的" +
                   $"{target.DisplayName}选择“{effectName}”；" +
                   $"{FormatAutoplayCost(originalCost, paidCost)}。";
        QueueAutoplayDialogue(AutoplayDialogueCategory.GreatTile);
        return true;
    }

    private int CountAutoplayChargeTowers()
    {
        int count = 0;
        for (int i = 0; i < _defenseTowers.Count; i++)
            if (_defenseTowers[i] != null && _defenseTowers[i].IsChargeTower)
                count++;
        return count;
    }

    private int GetAutoplayUpgradeChoice(RougeDefenseTower tower,
        AutoplayUpgradeChoice scoredChoice, out string explanation)
    {
        AutoplayPressureLayer pressure = scoredChoice.DominantPressureLayer;
        bool crowd = pressure == AutoplayPressureLayer.Crowd;
        bool urgent = pressure == AutoplayPressureLayer.Urgent;
        bool hard = pressure == AutoplayPressureLayer.Elite ||
                    pressure == AutoplayPressureLayer.Boss;
        float assaultStyle = GetAutoplayAssaultStyleStrength();
        float armorCommitThreshold = Mathf.Lerp(1.35f, 0.65f, assaultStyle);
        bool armorDemand = scoredChoice.UncoveredArmorPressure >=
                           armorCommitThreshold &&
                           scoredChoice.UncoveredArmorPressure >=
                           scoredChoice.VulnerablePressure * 0.35f;
        bool controlDemand = scoredChoice.FastUncontrolledPressure >= 0.9f;
        bool lateLowHealth = scoredChoice.LateHealthRatio <= 0.48f;
        bool setupPosition = scoredChoice.EarlyRouteExposure >
                             scoredChoice.LateRouteExposure * 1.12f;
        bool finisherPosition = scoredChoice.LateRouteExposure >
                                scoredChoice.EarlyRouteExposure * 1.12f;
        bool chokePosition = scoredChoice.RouteReuse >= 0.52f ||
                             scoredChoice.Bottleneck >= 0.58f;
        switch (tower.TowerType)
        {
            case RougeTowerType.Ice:
                if (tower.NeedsIceBranchChoice)
                {
                    bool missingFreeze = controlDemand &&
                        !HasAutoplayIceBranch(RougeIceTowerBranch.Freeze);
                    if (armorDemand && !missingFreeze)
                    {
                        explanation = "脆弱路线：帮全队处理硬目标";
                        return 1;
                    }
                    explanation = "冻结路线：先把怪群和近端速度压住";
                    return 0;
                }
                if (tower.UsesIceFreeze)
                {
                    int frostTargets = CountAutoplayPermanentFrostTargets(tower);
                    bool frostNetwork = !urgent && frostTargets >= 2 &&
                                        (setupPosition || chokePosition);
                    explanation = frostNetwork
                        ? $"永久霜寒：相邻有 {frostTargets} 个可利用塔位"
                        : "冰刺：眼前更需要立刻控场";
                    return frostNetwork ? 1 : 0;
                }
                explanation = armorDemand
                    ? "脆弱穿甲：专门处理高护甲目标"
                    : "脆弱增伤：让后续火力更疼";
                return armorDemand ? 1 : 0;

            case RougeTowerType.MachineGun:
                if (tower.NeedsMachineGunBranchChoice)
                {
                    bool fragmentLane = crowd && !armorDemand &&
                                        (chokePosition || !finisherPosition);
                    explanation = fragmentLane
                        ? "破片路线：怪多时一起清"
                        : "暴击路线：把单体火力做实";
                    return fragmentLane ? 1 : 0;
                }
                if (tower.UsesMachineGunCritical)
                {
                    explanation = armorDemand
                        ? "暴击穿甲：高护甲目标更值得针对"
                        : "暴击率：稳定提高输出";
                    return armorDemand ? 1 : 0;
                }
                explanation = crowd
                    ? "更多破片：继续扩大清场面"
                    : "嵌入破片：补一点持续伤害";
                return crowd ? 0 : 1;

            case RougeTowerType.Cannon:
                if (tower.NeedsCannonBranchChoice)
                {
                    bool persistentLane = !finisherPosition &&
                                          (setupPosition || chokePosition);
                    explanation = persistentLane
                        ? "持续炮弹：把关键路口压久一点"
                        : "内圈爆破：怪群越挤越疼";
                    return persistentLane ? 1 : 0;
                }
                if (tower.UsesCannonInnerBlast)
                {
                    explanation = crowd
                        ? "追加小炮弹：把清场范围再铺开"
                        : "扩大内圈：稳住主要落点";
                    return crowd ? 1 : 0;
                }
                explanation = urgent || controlDemand
                    ? "持续击退：先把贴近主塔的推回去"
                    : "增加持续次数：让路口一直有伤害";
                return urgent || controlDemand ? 0 : 1;

            case RougeTowerType.Flame:
                if (tower.NeedsFlameBranchChoice)
                {
                    bool burnSetup = !urgent && !finisherPosition &&
                                     !lateLowHealth &&
                                     (setupPosition || chokePosition) &&
                                     (hard || scoredChoice.RouteReuse >= 0.45f);
                    explanation = burnSetup
                        ? "燃烧路线：持续压低精英和 Boss 血线"
                        : "喷火器路线：把密集路口直接扫干净";
                    return burnSetup ? 1 : 0;
                }
                if (tower.UsesFlamethrower)
                {
                    explanation = crowd || urgent
                        ? "旋转喷火：同时覆盖更多来路"
                        : "扇形喷火：集中模式把火力并到 Boss";
                    return crowd || urgent ? 0 : 1;
                }
                bool conflagrationReady = hard &&
                    HasAutoplayIceBranch(RougeIceTowerBranch.Freeze);
                explanation = conflagrationReady
                    ? "爆燃：配合冻结直接处理硬目标"
                    : hard
                        ? "叠层燃烧：缺少冻结联动，先提高持续伤害"
                        : "叠层燃烧：怪群经过火区时持续增伤";
                return conflagrationReady ? 1 : 0;

            case RougeTowerType.Laser:
                if (tower.NeedsLaserBranchChoice)
                {
                    bool armorBreakNeeded = armorDemand;
                    explanation = armorBreakNeeded
                        ? "破甲路线：优先拆掉实际存在的护甲"
                        : crowd && (chokePosition || !finisherPosition)
                            ? "折射路线：敌人多时不浪费光束"
                            : "折射路线：没有护甲需求时保留直接增伤";
                    return armorBreakNeeded ? 0 : 1;
                }
                if (tower.UsesLaserArmorBreak)
                {
                    explanation = pressure == AutoplayPressureLayer.Boss
                        ? "强力集中：Boss 战需要锁得更稳"
                        : "加速破甲：更快拆掉精英防御";
                    return pressure == AutoplayPressureLayer.Boss ? 1 : 0;
                }
                explanation = crowd
                    ? "连续折射：让光束在人群里多跳几次"
                    : "折射攻击：补足单个目标的伤害";
                return crowd ? 0 : 1;

            default:
                explanation = "默认分支";
                return 0;
        }
    }

    private bool HasAutoplayIceBranch(RougeIceTowerBranch branch)
    {
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (IsAutoplayStandardTower(tower) &&
                tower.TowerType == RougeTowerType.Ice &&
                tower.IceBranch == branch) return true;
        }
        return false;
    }

    private int CountAutoplayPermanentFrostTargets(RougeDefenseTower tower)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        if (tower == null || map == null || loader == null ||
            !map.WorldToCell(tower.transform.position, out Vector2Int center))
            return 0;

        int useful = 0;
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0) continue;
            Vector2Int cell = center + new Vector2Int(x, y);
            if (!map.IsTowerPlace(cell)) continue;
            RougeTowerPlaceEffect effect = loader.GetEffectiveTowerPlaceEffect(cell);
            // Permanent frost should extend onto ordinary/frost pads, not erase an
            // authored damage, range, economy or attack-speed puzzle piece.
            if (effect != RougeTowerPlaceEffect.None &&
                effect != RougeTowerPlaceEffect.Frost) continue;
            useful++;
        }
        return useful;
    }

    private bool ScheduleTowerDefenseAutoplayPlan(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot baseSnapshot)
    {
        if (map == null || _towerDefenseAutoplayPlanScheduled ||
            !_positionsA.IsCreated || !_stateA.IsCreated ||
            !_towerDefenseEnemyKinds.IsCreated || !_effectStateA.IsCreated)
            return false;

        int cellCount = map.Width * map.Height;
        int typeCount = TowerDefenseVisuals.StandardTowerTypeCount;
        int candidateCount = typeCount * cellCount;
        if (cellCount <= 0 || candidateCount <= 0) return false;
        int enemyLimit = Mathf.Min(_currentMaxEnemies,
            Mathf.Min(_positionsA.Length,
                Mathf.Min(_stateA.Length, Mathf.Min(_towerDefenseEnemyKinds.Length,
                    _effectStateA.Length))));

        EnsureAutoplayNativeArrayLength(ref _towerDefenseAutoplayPlanCells,
            cellCount);
        EnsureAutoplayNativeArrayLength(
            ref _towerDefenseAutoplayPlanFunctionCoverage, cellCount * 3);
        EnsureAutoplayNativeArrayLength(ref _towerDefenseAutoplayPlanRouteNext,
            cellCount);
        EnsureAutoplayNativeArrayLength(
            ref _towerDefenseAutoplayPlanCandidates, candidateCount);
        EnsureAutoplayNativeArrayLength(
            ref _towerDefenseAutoplayPlanCandidateResults, candidateCount);
        EnsureAutoplayNativeArrayLength(ref _towerDefenseAutoplayPlanTotals, 1);
        EnsureAutoplayNativeArrayLength(
            ref _towerDefenseAutoplayPlanHardFactors, 256);
        EnsureAutoplayNativeArrayLength(
            ref _towerDefenseAutoplayPlanMaximumHealth, 256);
        EnsureAutoplayNativeArrayCapacity(ref _towerDefenseAutoplayPlanPositions,
            enemyLimit);
        EnsureAutoplayNativeArrayCapacity(ref _towerDefenseAutoplayPlanStates,
            enemyLimit);
        EnsureAutoplayNativeArrayCapacity(ref _towerDefenseAutoplayPlanKinds,
            enemyLimit);
        EnsureAutoplayNativeArrayCapacity(ref _towerDefenseAutoplayPlanEffects,
            enemyLimit);
        EnsureAutoplayNativeArrayCapacity(
            ref _towerDefenseAutoplayPlanEnemyContributions, enemyLimit);

        if (enemyLimit > 0)
        {
            NativeArray<float4>.Copy(_positionsA,
                _towerDefenseAutoplayPlanPositions, enemyLimit);
            NativeArray<float4>.Copy(_stateA,
                _towerDefenseAutoplayPlanStates, enemyLimit);
            NativeArray<byte>.Copy(_towerDefenseEnemyKinds,
                _towerDefenseAutoplayPlanKinds, enemyLimit);
            NativeArray<RougeEnemyEffectState>.Copy(_effectStateA,
                _towerDefenseAutoplayPlanEffects, enemyLimit);
        }

        enemyBalance?.EnsureDefaults();
        RougeEnemyArchetypeConfig baselineArchetype = enemyBalance != null &&
            enemyBalance.enemyTypes != null && enemyBalance.enemyTypes.Count > 0
                ? enemyBalance.enemyTypes[0]
                : null;
        float baselineHealth = Mathf.Max(0.01f,
            baselineArchetype?.baseHealth ?? 10f);
        float baselineArmor = baselineArchetype?.armor ?? 1f;
        for (int kindValue = 0; kindValue < 256; kindValue++)
        {
            byte kind = (byte)kindValue;
            bool boss = (kind & BossEnemyFlag) != 0;
            bool elite = !boss && (kind & EliteEnemyFlag) != 0;
            float hardFactor = elite ? 1f : 0f;
            if (!boss && enemyBalance != null &&
                enemyBalance.enemyTypes != null &&
                enemyBalance.enemyTypes.Count > 0)
            {
                RougeEnemyArchetypeConfig archetype = enemyBalance.enemyTypes[
                    Mathf.Clamp(kind & EnemyArchetypeMask, 0,
                        enemyBalance.enemyTypes.Count - 1)];
                float healthFactor = Mathf.Clamp01(
                    archetype.baseHealth / baselineHealth - 1f);
                float armorFactor = Mathf.Clamp01(
                    (archetype.armor - baselineArmor) / 4f);
                hardFactor = Mathf.Max(hardFactor,
                    healthFactor * 0.7f + armorFactor * 0.6f);
            }
            _towerDefenseAutoplayPlanHardFactors[kindValue] = hardFactor;
            _towerDefenseAutoplayPlanMaximumHealth[kindValue] =
                Mathf.Max(0.01f, GetTowerDefenseEnemyHealth(kind));
        }

        for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
        {
            int x = cellIndex % map.Width;
            int y = cellIndex / map.Width;
            Vector2Int cell = new Vector2Int(x, y);
            _towerDefenseAutoplayPlanCells[cellIndex] = new AutoplaySpatialCell
            {
                Total = _towerDefenseAutoplayEnemyPressureByCell[cellIndex],
                Crowd = _towerDefenseAutoplayCrowdPressureByCell[cellIndex],
                Elite = _towerDefenseAutoplayElitePressureByCell[cellIndex],
                Boss = _towerDefenseAutoplayBossPressureByCell[cellIndex],
                Urgent = _towerDefenseAutoplayUrgentPressureByCell[cellIndex],
                GroundValue = _towerDefenseAutoplayGroundValueByCell[cellIndex],
                Coverage = _towerDefenseAutoplayCoverageByCell[cellIndex],
                RouteDistance = _towerDefenseAutoplayRouteDistanceByCell[cellIndex],
                IsGround = map.IsGround(cell) ? (byte)1 : (byte)0
            };
            _towerDefenseAutoplayPlanRouteNext[cellIndex] =
                (uint)cellIndex <
                (uint)_towerDefenseAutoplayRouteNextByCell.Length
                    ? _towerDefenseAutoplayRouteNextByCell[cellIndex]
                    : -1;
        }

        NativeArray<float>.Copy(_towerDefenseAutoplayFunctionCoverageByCell,
            _towerDefenseAutoplayPlanFunctionCoverage, cellCount * 3);
        for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
        for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
        {
            int candidateIndex = typeIndex * cellCount + cellIndex;
            AutoplayBuildPrior prior =
                _towerDefenseAutoplayBuildPriors[candidateIndex];
            bool valid = prior.IsValid &&
                         _towerDefenseAutoplayBuildableTopology[cellIndex] &&
                         !_towerDefenseAutoplayOccupiedCells[cellIndex];
            _towerDefenseAutoplayPlanCandidates[candidateIndex] =
                new AutoplaySpatialCandidateInput
                {
                    AttackRange = prior.AttackRange,
                    IsValid = valid ? (byte)1 : (byte)0,
                    FunctionGroup = (byte)GetAutoplayFunctionGroup(
                        (RougeTowerType)typeIndex)
                };
        }

        Vector2 origin = map.Origin;
        JobHandle analyzeHandle = new AnalyzeAutoplayEnemiesJob
        {
            Positions = _towerDefenseAutoplayPlanPositions,
            States = _towerDefenseAutoplayPlanStates,
            Kinds = _towerDefenseAutoplayPlanKinds,
            Effects = _towerDefenseAutoplayPlanEffects,
            HardFactors = _towerDefenseAutoplayPlanHardFactors,
            MaximumHealthByKind = _towerDefenseAutoplayPlanMaximumHealth,
            Cells = _towerDefenseAutoplayPlanCells,
            Contributions = _towerDefenseAutoplayPlanEnemyContributions,
            Width = map.Width,
            Height = map.Height,
            CellSize = map.CellSize,
            OriginX = origin.x,
            OriginY = origin.y,
            RenderHeight = renderHeight,
            BaselineSpeed = Mathf.Max(0.01f, GetTowerDefenseEnemySpeed(0)),
            MaximumRouteDistance = Mathf.Max(1f,
                _towerDefenseAutoplayMaximumRouteDistance),
            NearBaseFullCrisisCells = TowerDefenseAutoplayDialogueThresholds
                .nearBaseDistanceCells,
            MainCellX = baseSnapshot.MainCell.x,
            MainCellY = baseSnapshot.MainCell.y,
            HasMainCell = baseSnapshot.HasMainCell ? (byte)1 : (byte)0
        }.Schedule(enemyLimit, TowerDefenseAutoplayEnemyAnalysisBatchSize);

        JobHandle reduceHandle = new ReduceAutoplayEnemyPressureJob
        {
            Contributions = _towerDefenseAutoplayPlanEnemyContributions,
            RouteNext = _towerDefenseAutoplayPlanRouteNext,
            Cells = _towerDefenseAutoplayPlanCells,
            Totals = _towerDefenseAutoplayPlanTotals,
            EnemyCount = enemyLimit
        }.Schedule(analyzeHandle);

        _towerDefenseAutoplayPlanHandle = new ScoreAutoplaySpatialCandidatesJob
        {
            Candidates = _towerDefenseAutoplayPlanCandidates,
            Cells = _towerDefenseAutoplayPlanCells,
            FunctionCoverage = _towerDefenseAutoplayPlanFunctionCoverage,
            Results = _towerDefenseAutoplayPlanCandidateResults,
            Width = map.Width,
            Height = map.Height,
            CellCount = cellCount,
            CellSize = map.CellSize
        }.Schedule(candidateCount, TowerDefenseAutoplaySpatialScoreBatchSize,
            reduceHandle);
        JobHandle.ScheduleBatchedJobs();

        _towerDefenseAutoplayPlanScheduled = true;
        _towerDefenseAutoplayPlanResultsReady = false;
        _towerDefenseAutoplayPendingPlanGeneration =
            _towerDefenseAutoplayPlanGeneration;
        _towerDefenseAutoplayPendingPriorRevision =
            _towerDefenseAutoplayPriorRevision;
        _towerDefenseAutoplayPendingCellCount = cellCount;
        _towerDefenseAutoplayPendingPlanGameTime = Mathf.Max(0f, _survivalTime);
        _towerDefenseAutoplayPendingMap = map;
        _towerDefenseAutoplayPendingBaseSnapshot = baseSnapshot;
        return true;
    }

    private bool TryConsumeTowerDefenseAutoplayPlan(
        out RougeTowerDefenseMap map, out AutoplayBattleSnapshot snapshot)
    {
        map = null;
        snapshot = default;
        if (!_towerDefenseAutoplayPlanScheduled ||
            !_towerDefenseAutoplayPlanHandle.IsCompleted) return false;

        _towerDefenseAutoplayPlanHandle.Complete();
        _towerDefenseAutoplayPlanScheduled = false;
        _towerDefenseAutoplayPlanHandle = default;
        float age = Mathf.Max(0f, _survivalTime) -
                    _towerDefenseAutoplayPendingPlanGameTime;
        bool valid = _towerDefenseAutoplayPendingPlanGeneration ==
                         _towerDefenseAutoplayPlanGeneration &&
                     _towerDefenseAutoplayPendingMap != null &&
                     _towerDefenseAutoplayPendingMap ==
                         RougeTowerDefenseMapLoader.ActiveMap &&
                     _towerDefenseAutoplayPendingPriorRevision ==
                         _towerDefenseAutoplayPriorRevision &&
                     age <= TowerDefenseAutoplayMaximumPlanAgeSeconds;
        if (!valid)
        {
            _towerDefenseAutoplayPlanResultsReady = false;
            return false;
        }

        map = _towerDefenseAutoplayPendingMap;
        snapshot = _towerDefenseAutoplayPendingBaseSnapshot;
        int cellCount = _towerDefenseAutoplayPendingCellCount;
        for (int i = 0; i < cellCount; i++)
        {
            AutoplaySpatialCell cell = _towerDefenseAutoplayPlanCells[i];
            _towerDefenseAutoplayEnemyPressureByCell[i] = cell.Total;
            _towerDefenseAutoplayCrowdPressureByCell[i] = cell.Crowd;
            _towerDefenseAutoplayElitePressureByCell[i] = cell.Elite;
            _towerDefenseAutoplayBossPressureByCell[i] = cell.Boss;
            _towerDefenseAutoplayUrgentPressureByCell[i] = cell.Urgent;
            _towerDefenseAutoplayActiveCrowdPressureByCell[i] = cell.ActiveCrowd;
            _towerDefenseAutoplayActiveElitePressureByCell[i] = cell.ActiveElite;
            _towerDefenseAutoplayActiveUrgentPressureByCell[i] = cell.ActiveUrgent;
        }

        AutoplayEnemyTotals totals = _towerDefenseAutoplayPlanTotals[0];
        snapshot.ActiveEnemies = totals.ActiveEnemies;
        snapshot.EliteEnemies = totals.EliteEnemies;
        snapshot.BossEnemies = totals.BossEnemies;
        snapshot.TotalPressure = totals.TotalPressure;
        snapshot.CrowdPressure = totals.CrowdPressure;
        snapshot.ElitePressure = totals.ElitePressure;
        snapshot.BossPressure = totals.BossPressure;
        snapshot.UrgentPressure = totals.UrgentPressure;
        snapshot.PeakCellPressure = totals.PeakCellPressure;
        snapshot.ImminentEnemyWeight = totals.ImminentEnemyWeight;
        snapshot.ImminentPressure = totals.ImminentPressure;
        snapshot.ImminentElitePressure = totals.ImminentElitePressure;
        snapshot.ImminentBossPressure = totals.ImminentBossPressure;
        snapshot.NearBaseEnemyWeight = totals.NearBaseEnemyWeight;
        snapshot.PositiveArmorPressure = totals.PositiveArmorPressure;
        snapshot.UncoveredArmorPressure = totals.UncoveredArmorPressure;
        snapshot.FastUncontrolledPressure = totals.FastUncontrolledPressure;
        snapshot.VulnerablePressure = totals.VulnerablePressure;
        snapshot.LateHealthRatioSum = totals.LateHealthRatioSum;
        snapshot.LateHealthWeight = totals.LateHealthWeight;
        _towerDefenseAutoplayPlanResultsReady = true;
        return true;
    }

    private void InvalidateTowerDefenseAutoplayPlan()
    {
        _towerDefenseAutoplayPlanGeneration++;
        _towerDefenseAutoplayPlanResultsReady = false;
    }

    private void DisposeTowerDefenseAutoplayPlanner()
    {
        InvalidateTowerDefenseAutoplayPlan();
        if (_towerDefenseAutoplayPlanScheduled)
            _towerDefenseAutoplayPlanHandle.Complete();
        _towerDefenseAutoplayPlanScheduled = false;
        _towerDefenseAutoplayPlanHandle = default;
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanPositions);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanStates);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanKinds);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanHardFactors);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanMaximumHealth);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanEffects);
        DisposeAutoplayNativeArray(
            ref _towerDefenseAutoplayPlanEnemyContributions);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanCells);
        DisposeAutoplayNativeArray(
            ref _towerDefenseAutoplayPlanFunctionCoverage);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanRouteNext);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanCandidates);
        DisposeAutoplayNativeArray(
            ref _towerDefenseAutoplayPlanCandidateResults);
        DisposeAutoplayNativeArray(ref _towerDefenseAutoplayPlanTotals);
        _towerDefenseAutoplayPendingMap = null;
    }

    private static void EnsureAutoplayNativeArrayLength<T>(
        ref NativeArray<T> array, int length) where T : struct
    {
        length = Mathf.Max(1, length);
        if (array.IsCreated && array.Length == length) return;
        if (array.IsCreated) array.Dispose();
        array = new NativeArray<T>(length, Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);
    }

    private static void EnsureAutoplayNativeArrayCapacity<T>(
        ref NativeArray<T> array, int required) where T : struct
    {
        required = Mathf.Max(1, required);
        if (array.IsCreated && array.Length >= required) return;
        int capacity = Mathf.NextPowerOfTwo(required);
        if (array.IsCreated) array.Dispose();
        array = new NativeArray<T>(capacity, Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory);
    }

    private static void DisposeAutoplayNativeArray<T>(
        ref NativeArray<T> array) where T : struct
    {
        if (array.IsCreated) array.Dispose();
        array = default;
    }

    private AutoplayBattleSnapshot BuildAutoplayBattleSnapshot(
        RougeTowerDefenseMap map, bool includeActiveEnemies = true)
    {
        AutoplayBattleSnapshot snapshot = default;
        snapshot.NextWaveSeconds = float.PositiveInfinity;
        if (map == null) return snapshot;

        EnsureAutoplayBossPlanInitialized(map);
        snapshot.SecondsUntilBoss = GetAutoplaySecondsUntilNextBoss();
        if (!float.IsPositiveInfinity(snapshot.SecondsUntilBoss))
        {
            float preparationLead = Mathf.Max(1f,
                TowerDefenseAutoplayCommander.BossPreparationLeadSeconds);
            snapshot.BossPreparation = Mathf.Clamp01(1f -
                snapshot.SecondsUntilBoss /
                preparationLead);
            // A gentle curve keeps normal wave management in charge at the start of
            // the warning window, then commits capital as the encounter approaches.
            snapshot.BossPreparation = Mathf.SmoothStep(0f, 1f,
                snapshot.BossPreparation);
        }

        int cellCount = map.Width * map.Height;
        snapshot.HasMainCell = mainTower != null &&
            map.WorldToCell(mainTower.transform.position, out snapshot.MainCell);
        EnsureTowerDefenseAutoplayPriorCache(map, snapshot.MainCell,
            snapshot.HasMainCell);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayEnemyPressureByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayCrowdPressureByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayElitePressureByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayBossPressureByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayUrgentPressureByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(
            ref _towerDefenseAutoplayActiveCrowdPressureByCell, cellCount);
        EnsureAutoplayScoreBuffer(
            ref _towerDefenseAutoplayActiveElitePressureByCell, cellCount);
        EnsureAutoplayScoreBuffer(
            ref _towerDefenseAutoplayActiveUrgentPressureByCell, cellCount);
        EnsureAutoplayOccupancyBuffer(cellCount);
        Array.Clear(_towerDefenseAutoplayEnemyPressureByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayCrowdPressureByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayElitePressureByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayBossPressureByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayUrgentPressureByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayActiveCrowdPressureByCell, 0,
            cellCount);
        Array.Clear(_towerDefenseAutoplayActiveElitePressureByCell, 0,
            cellCount);
        Array.Clear(_towerDefenseAutoplayActiveUrgentPressureByCell, 0,
            cellCount);
        Array.Clear(_towerDefenseAutoplayCoverageByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayFunctionCoverageByCell, 0,
            cellCount * 3);
        Array.Clear(_towerDefenseAutoplayOccupiedCells, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayTypeCounts, 0,
            _towerDefenseAutoplayTypeCounts.Length);
        Array.Clear(_towerDefenseAutoplayFunctionCounts, 0,
            _towerDefenseAutoplayFunctionCounts.Length);

        if (snapshot.HasMainCell)
            _towerDefenseAutoplayOccupiedCells[snapshot.MainCell.y * map.Width +
                snapshot.MainCell.x] = true;

        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null) continue;
            bool hasTowerCell = map.WorldToCell(tower.transform.position,
                out Vector2Int towerCell);
            if (hasTowerCell)
                _towerDefenseAutoplayOccupiedCells[towerCell.y * map.Width +
                    towerCell.x] = true;
            if (!IsAutoplayStandardTower(tower)) continue;
            int typeIndex = (int)tower.TowerType;
            _towerDefenseAutoplayTypeCounts[typeIndex]++;
            int functionGroup = GetAutoplayFunctionGroup(tower.TowerType);
            _towerDefenseAutoplayFunctionCounts[functionGroup]++;
            if (hasTowerCell)
                AccumulateAutoplayTowerCoverage(map, tower, towerCell,
                    functionGroup);
        }

        bool authoritativeLiveBoss = TryGetAutoplayLiveBossTarget(out _, out _);
        UpdateAutoplayBossReadinessUrgency(map, snapshot,
            authoritativeLiveBoss);
        AccumulateAutoplayBossRoutePressure(map, snapshot,
            authoritativeLiveBoss);

        // This is the only active-enemy scan in a decision. Pressure is collapsed to
        // the coarse map grid before any type/cell candidate is evaluated.
        bool canScanActiveEnemies = _positionsA.IsCreated && _stateA.IsCreated;
        enemyBalance?.EnsureDefaults();
        RougeEnemyArchetypeConfig baselineArchetype = enemyBalance != null &&
            enemyBalance.enemyTypes != null && enemyBalance.enemyTypes.Count > 0
            ? enemyBalance.enemyTypes[0]
            : null;
        float baselineHealth = Mathf.Max(0.01f,
            baselineArchetype?.baseHealth ?? 10f);
        float baselineArmor = baselineArchetype?.armor ?? 1f;
        float baselineRuntimeSpeed = Mathf.Max(0.01f,
            GetTowerDefenseEnemySpeed(0));
        AccumulateAutoplayIncomingWavePressure(map, baselineHealth,
            baselineArmor, ref snapshot);
        if (!includeActiveEnemies || !canScanActiveEnemies)
        {
            snapshot.PeakCellPressure = GetAutoplayPeakCellPressure(cellCount);
            return snapshot;
        }
        int maximumGoalDistance = snapshot.HasMainCell
            ? Mathf.CeilToInt(_towerDefenseAutoplayMaximumRouteDistance)
            : 1;
        int enemyLimit = Mathf.Min(_currentMaxEnemies,
            Mathf.Min(_positionsA.Length, _stateA.Length));
        for (int enemyIndex = 0; enemyIndex < enemyLimit; enemyIndex++)
        {
            var state = _stateA[enemyIndex];
            if (state.x <= 0f) continue;
            var position = _positionsA[enemyIndex];
            int visualFlags = (int)Mathf.Floor(
                Mathf.Max(state.w, 0f) / 10f + 0.0001f);
            if (position.y > renderHeight + 0.05f || (visualFlags & 4) != 0)
                continue;
            if (!map.WorldToCell(new Vector3(position.x, 0f, position.z),
                    out Vector2Int cell)) continue;

            byte kind = _towerDefenseEnemyKinds.IsCreated &&
                        enemyIndex < _towerDefenseEnemyKinds.Length
                ? _towerDefenseEnemyKinds[enemyIndex]
                : (byte)0;
            bool boss = (kind & BossEnemyFlag) != 0;
            bool elite = !boss && (kind & EliteEnemyFlag) != 0;
            RougeEnemyArchetypeConfig archetype = null;
            if (!boss && enemyBalance != null && enemyBalance.enemyTypes != null &&
                enemyBalance.enemyTypes.Count > 0)
            {
                int archetypeIndex = Mathf.Clamp(kind & EnemyArchetypeMask, 0,
                    enemyBalance.enemyTypes.Count - 1);
                archetype = enemyBalance.enemyTypes[archetypeIndex];
            }
            float pressure = boss ? 24f : elite ? 4f : 1f;
            bool hasEffects = _effectStateA.IsCreated &&
                              enemyIndex < _effectStateA.Length;
            RougeEnemyEffectState effects = hasEffects
                ? _effectStateA[enemyIndex]
                : default;
            float maximumHealth = effects.MaximumHealth > 0.001f
                ? effects.MaximumHealth
                : Mathf.Max(0.01f, GetTowerDefenseEnemyHealth(kind));
            float healthRatio = Mathf.Clamp01(state.x / maximumHealth);
            pressure *= 0.65f + healthRatio * 0.35f;
            float goalThreat = 0f;
            float distanceWeight = 0f;
            if (snapshot.HasMainCell)
            {
                float routeDistance = GetAutoplayRemainingRouteDistanceInCells(map,
                    new Vector3(position.x, 0f, position.z), cell);
                if (float.IsPositiveInfinity(routeDistance))
                    routeDistance = Mathf.Abs(cell.x - snapshot.MainCell.x) +
                                    Mathf.Abs(cell.y - snapshot.MainCell.y);
                float nearBaseCrisis = CalculateAutoplayNearBaseCrisis(
                    routeDistance, TowerDefenseAutoplayDialogueThresholds
                        .nearBaseDistanceCells);
                if (!boss)
                    snapshot.NearBaseEnemyWeight += nearBaseCrisis *
                        (elite ? 1.5f : 1f);
                goalThreat = 1f - Mathf.Clamp01(routeDistance /
                    Mathf.Max(1f, maximumGoalDistance));
                distanceWeight = 1f /
                    (1f + routeDistance * routeDistance * 0.22f);
                pressure *= 1f + goalThreat * 0.9f;
            }

            float hardFactor = elite ? 1f : 0f;
            if (!boss && archetype != null)
            {
                float healthFactor = Mathf.Clamp01(
                    archetype.baseHealth / baselineHealth - 1f);
                float armorFactor = Mathf.Clamp01(
                    (archetype.armor - baselineArmor) / 4f);
                hardFactor = Mathf.Max(hardFactor,
                    healthFactor * 0.7f + armorFactor * 0.6f);
            }
            float eliteShare = boss ? 0f : Mathf.Clamp01(hardFactor);
            float crowdPressure = !boss ? pressure * (1f - eliteShare) : 0f;
            float hardPressure = !boss ? pressure * eliteShare : 0f;
            float bossPressure = boss ? pressure : 0f;
            // state.z is the live navigation speed. Compare it with the current
            // standard-enemy speed so global level scaling does not mark the whole map.
            float effectiveSpeed = state.z;
            if (hasEffects)
            {
                effectiveSpeed *= effects.FreezeTimer > 0f
                    ? 0.05f
                    : effects.SlowTimer > 0f
                        ? Mathf.Clamp(1f - effects.SlowPercent * 0.01f,
                            0.05f, 1f)
                        : 1f;
            }
            float speedRatio = effectiveSpeed / baselineRuntimeSpeed;
            float speedThreat = Mathf.InverseLerp(1.08f, 1.35f, speedRatio);
            float arrivalWeight = distanceWeight * Mathf.Lerp(0.78f, 1.38f,
                Mathf.InverseLerp(0.8f, 1.5f, speedRatio));
            float imminentPressure = pressure * arrivalWeight;
            snapshot.ImminentEnemyWeight += arrivalWeight;
            snapshot.ImminentPressure += imminentPressure;
            if (boss) snapshot.ImminentBossPressure += imminentPressure;
            else if (hardFactor > 0.01f)
                snapshot.ImminentElitePressure += imminentPressure * hardFactor;
            float urgentFactor = Mathf.Max(goalThreat, speedThreat);
            float urgentPressure = 0f;
            if (!boss && urgentFactor >= 0.7f)
            {
                urgentPressure = pressure * (0.4f + urgentFactor * 0.8f);
                snapshot.UrgentPressure += urgentPressure;
            }
            float positiveArmor = Mathf.Max(0f, effects.Armor);
            float armorDemand = pressure * Mathf.Clamp01(positiveArmor / 8f);
            bool vulnerable = effects.VulnerabilityTimer > 0f ||
                              effects.VulnerabilityDamageBonusTimer > 0f ||
                              effects.VulnerabilityArmorPenetrationTimer > 0f;
            snapshot.PositiveArmorPressure += armorDemand;
            snapshot.UncoveredArmorPressure += armorDemand *
                (vulnerable ? 0.2f : 1f);
            snapshot.FastUncontrolledPressure += pressure * speedThreat;
            if (vulnerable) snapshot.VulnerablePressure += pressure;
            float lateWeight = pressure * Mathf.Clamp01((goalThreat - 0.45f) /
                                                        0.55f);
            snapshot.LateHealthRatioSum += healthRatio * lateWeight;
            snapshot.LateHealthWeight += lateWeight;
            AccumulateAutoplayProjectedEnemyPressure(map, cell, pressure,
                crowdPressure, hardPressure, bossPressure, urgentPressure,
                speedRatio);
            snapshot.CrowdPressure += crowdPressure;
            snapshot.ElitePressure += hardPressure;
            snapshot.BossPressure += bossPressure;
            snapshot.TotalPressure += pressure;
            snapshot.ActiveEnemies++;
            if (boss) snapshot.BossEnemies++;
            else if (elite) snapshot.EliteEnemies++;
        }
        // Forecast and forward projection both paint cells beyond each enemy's
        // current cell, so the true choke-point peak must be reduced after every
        // source has contributed.
        snapshot.PeakCellPressure = GetAutoplayPeakCellPressure(cellCount);
        return snapshot;
    }

    private float GetAutoplayPeakCellPressure(int cellCount)
    {
        float peak = 0f;
        int limit = Mathf.Min(cellCount,
            _towerDefenseAutoplayEnemyPressureByCell.Length);
        for (int i = 0; i < limit; i++)
            peak = Mathf.Max(peak,
                _towerDefenseAutoplayEnemyPressureByCell[i]);
        return peak;
    }

    private void UpdateAutoplayEnemyFlowHeatmap(RougeTowerDefenseMap map,
        ref AutoplayBattleSnapshot snapshot)
    {
        if (map == null || !snapshot.HasMainCell)
        {
            snapshot.NearBaseEnemyWeight = 0f;
            _towerDefenseAutoplayNearBaseCrisis = 0f;
            _towerDefenseAutoplayNearBaseInstantRisk = 0f;
            _towerDefenseAutoplayLastRealNearBaseRisk = 0f;
            _towerDefenseAutoplayLastRealNearBaseRiskAt =
                float.NegativeInfinity;
            _towerDefenseAutoplayHeatmapRevision++;
            _towerDefenseAutoplayHotspotRevision = -1;
            _towerDefenseAutoplayNearBaseHotspots.Clear();
            return;
        }

        int cellCount = map.Width * map.Height;
        bool reset = _towerDefenseAutoplayHeatmapMap != map ||
                     _towerDefenseAutoplayNonBossHeatByCell == null ||
                     _towerDefenseAutoplayNonBossHeatByCell.Length < cellCount;
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayNonBossHeatByCell,
            cellCount);
        if (reset)
        {
            Array.Clear(_towerDefenseAutoplayNonBossHeatByCell, 0, cellCount);
            _towerDefenseAutoplayHeatmapMap = map;
            _towerDefenseAutoplayHeatmapUpdatedAt = float.NegativeInfinity;
            _towerDefenseAutoplayNearBaseInstantRisk = 0f;
            _towerDefenseAutoplayLastRealNearBaseRisk = 0f;
            _towerDefenseAutoplayLastRealNearBaseRiskAt =
                float.NegativeInfinity;
        }

        float gameTime = Mathf.Max(0f, _survivalTime);
        AutoplayFlowPressure flow = MeasureAutoplayEnemyFlow(gameTime);
        _towerDefenseAutoplayEnemyFlowBacklog = flow.Confidence * Mathf.Clamp01(
            (flow.SpawnPerSecond - flow.KillsPerSecond) /
            Mathf.Max(1f, flow.SpawnPerSecond));
        float halfLife = Mathf.Lerp(
            TowerDefenseAutoplayHeatMinimumHalfLifeSeconds,
            TowerDefenseAutoplayHeatMaximumHalfLifeSeconds,
            _towerDefenseAutoplayEnemyFlowBacklog);
        float elapsed = float.IsNegativeInfinity(
            _towerDefenseAutoplayHeatmapUpdatedAt)
            ? TowerDefenseAutoplayTickSeconds
            : Mathf.Clamp(gameTime - _towerDefenseAutoplayHeatmapUpdatedAt,
                0.02f, 30f);
        float decay = Mathf.Pow(0.5f, elapsed / Mathf.Max(0.1f, halfLife));
        _towerDefenseAutoplayHeatmapUpdatedAt = gameTime;

        float peakInstantRisk = 0f;
        for (int i = 0; i < cellCount; i++)
        {
            float crowd = (uint)i <
                          (uint)_towerDefenseAutoplayActiveCrowdPressureByCell.Length
                ? _towerDefenseAutoplayActiveCrowdPressureByCell[i]
                : 0f;
            float elite = (uint)i <
                          (uint)_towerDefenseAutoplayActiveElitePressureByCell.Length
                ? _towerDefenseAutoplayActiveElitePressureByCell[i]
                : 0f;
            float urgent = (uint)i <
                           (uint)_towerDefenseAutoplayActiveUrgentPressureByCell.Length
                ? _towerDefenseAutoplayActiveUrgentPressureByCell[i]
                : 0f;
            // Active channels already contain the two-to-four-cell movement
            // projection and explicitly exclude Boss pressure.
            float instant = Mathf.Max(crowd + elite * 1.35f, urgent * 1.1f);
            float target = instant *
                (1f + _towerDefenseAutoplayEnemyFlowBacklog * 0.3f);
            _towerDefenseAutoplayNonBossHeatByCell[i] = Mathf.Max(target,
                _towerDefenseAutoplayNonBossHeatByCell[i] * decay);

            float routeDistance = (uint)i <
                                  (uint)_towerDefenseAutoplayRouteDistanceByCell.Length
                ? _towerDefenseAutoplayRouteDistanceByCell[i]
                : float.PositiveInfinity;
            if (float.IsInfinity(routeDistance)) continue;
            float proximity = CalculateAutoplayNearBaseCrisis(routeDistance,
                TowerDefenseAutoplayDialogueThresholds.nearBaseDistanceCells);
            peakInstantRisk = Mathf.Max(peakInstantRisk, instant * proximity);
        }

        _towerDefenseAutoplayNearBaseInstantRisk =
            Mathf.Clamp01(peakInstantRisk);
        if (_towerDefenseAutoplayNearBaseInstantRisk > 0.0001f)
        {
            _towerDefenseAutoplayLastRealNearBaseRisk =
                _towerDefenseAutoplayNearBaseInstantRisk;
            _towerDefenseAutoplayLastRealNearBaseRiskAt = gameTime;
        }

        float peakRisk = 0f;
        for (int i = 0; i < cellCount; i++)
        {
            float routeDistance = (uint)i <
                                  (uint)_towerDefenseAutoplayRouteDistanceByCell.Length
                ? _towerDefenseAutoplayRouteDistanceByCell[i]
                : float.PositiveInfinity;
            if (float.IsInfinity(routeDistance)) continue;
            float proximity = CalculateAutoplayNearBaseCrisis(routeDistance,
                TowerDefenseAutoplayDialogueThresholds.nearBaseDistanceCells);
            peakRisk = Mathf.Max(peakRisk,
                _towerDefenseAutoplayNonBossHeatByCell[i] * proximity);
        }
        snapshot.NearBaseEnemyWeight = Mathf.Clamp01(peakRisk);
        _towerDefenseAutoplayHeatmapRevision++;
    }

    private float GetAutoplayTacticalNearBaseRisk(float gameTime)
    {
        if (_towerDefenseAutoplayNearBaseInstantRisk > 0.0001f)
            return _towerDefenseAutoplayNearBaseInstantRisk;

        // The analysis job and the managed decision alternate across autoplay
        // ticks. Keep one sampling interval of tolerance, but never let the
        // decaying heat field itself manufacture a sustained tactical crisis.
        float sampleGrace = TowerDefenseAutoplayTickSeconds * 1.1f;
        return gameTime - _towerDefenseAutoplayLastRealNearBaseRiskAt <=
               sampleGrace
            ? _towerDefenseAutoplayLastRealNearBaseRisk
            : 0f;
    }

    private bool TryGetAutoplayImmediateCoreBreach(RougeTowerDefenseMap map,
        out int threatCellIndex, out float threatPressure)
    {
        threatCellIndex = -1;
        threatPressure = 0f;
        if (map == null) return false;
        int cellCount = Mathf.Min(map.Width * map.Height,
            _towerDefenseAutoplayRouteDistanceByCell.Length);
        for (int index = 0; index < cellCount; index++)
        {
            float routeDistance = _towerDefenseAutoplayRouteDistanceByCell[index];
            if (!float.IsFinite(routeDistance) || routeDistance >
                TowerDefenseAutoplayImmediateCoreDefenseCells + 0.001f)
                continue;
            float crowd = (uint)index <
                          (uint)_towerDefenseAutoplayActiveCrowdPressureByCell.Length
                ? _towerDefenseAutoplayActiveCrowdPressureByCell[index]
                : 0f;
            float elite = (uint)index <
                          (uint)_towerDefenseAutoplayActiveElitePressureByCell.Length
                ? _towerDefenseAutoplayActiveElitePressureByCell[index]
                : 0f;
            float urgent = (uint)index <
                           (uint)_towerDefenseAutoplayActiveUrgentPressureByCell.Length
                ? _towerDefenseAutoplayActiveUrgentPressureByCell[index]
                : 0f;
            float proximity = 1f - Mathf.Clamp01(routeDistance /
                Mathf.Max(0.1f, TowerDefenseAutoplayImmediateCoreDefenseCells));
            float pressure = (crowd + elite * 1.35f + urgent * 1.15f) *
                             Mathf.Lerp(1.2f, 1.65f, proximity);
            if (pressure <= threatPressure) continue;
            threatPressure = pressure;
            threatCellIndex = index;
        }
        if (TryGetAutoplayLiveBossTarget(out Vector3 bossPosition, out _) &&
            map.WorldToCell(bossPosition, out Vector2Int bossCell))
        {
            int bossCellIndex = bossCell.y * map.Width + bossCell.x;
            if ((uint)bossCellIndex <
                (uint)_towerDefenseAutoplayRouteDistanceByCell.Length)
            {
                float bossDistance =
                    _towerDefenseAutoplayRouteDistanceByCell[bossCellIndex];
                if (float.IsFinite(bossDistance) && bossDistance <=
                    TowerDefenseAutoplayImmediateCoreDefenseCells + 0.001f)
                {
                    float bossPressure = 24f * Mathf.Lerp(1.2f, 1.65f,
                        1f - Mathf.Clamp01(bossDistance /
                            Mathf.Max(0.1f,
                                TowerDefenseAutoplayImmediateCoreDefenseCells)));
                    if (bossPressure > threatPressure)
                    {
                        threatPressure = bossPressure;
                        threatCellIndex = bossCellIndex;
                    }
                }
            }
        }
        return threatCellIndex >= 0 && threatPressure > 0.01f;
    }

    private float GetAutoplayNearBaseHeatRiskAtCell(int index)
    {
        if ((uint)index >= (uint)_towerDefenseAutoplayNonBossHeatByCell.Length ||
            (uint)index >= (uint)_towerDefenseAutoplayRouteDistanceByCell.Length)
            return 0f;
        float routeDistance = _towerDefenseAutoplayRouteDistanceByCell[index];
        if (float.IsInfinity(routeDistance)) return 0f;
        float proximity = CalculateAutoplayNearBaseCrisis(routeDistance,
            TowerDefenseAutoplayDialogueThresholds.nearBaseDistanceCells);
        return _towerDefenseAutoplayNonBossHeatByCell[index] * proximity;
    }

    private float GetAutoplayNearBaseHeatCoverage(RougeTowerDefenseMap map,
        Vector2Int towerCell, float attackRange, bool uncoveredOnly,
        out int peakCellIndex)
    {
        peakCellIndex = -1;
        if (map == null || attackRange <= 0f ||
            _towerDefenseAutoplayNearBaseCrisis <= 0.01f) return 0f;
        float cellSize = Mathf.Max(0.1f, map.CellSize);
        int radiusCells = Mathf.Max(1, Mathf.CeilToInt(attackRange / cellSize));
        float rangeSquared = attackRange * attackRange;
        Vector3 center = map.CellCenter(towerCell);
        float peak = 0f;
        float supportingHeat = 0f;
        for (int y = Mathf.Max(0, towerCell.y - radiusCells);
             y <= Mathf.Min(map.Height - 1, towerCell.y + radiusCells); y++)
        for (int x = Mathf.Max(0, towerCell.x - radiusCells);
             x <= Mathf.Min(map.Width - 1, towerCell.x + radiusCells); x++)
        {
            int index = y * map.Width + x;
            float risk = GetAutoplayNearBaseHeatRiskAtCell(index);
            if (risk <= 0f) continue;
            float distanceSquared = (map.CellCenter(new Vector2Int(x, y)) -
                                     center).sqrMagnitude;
            if (distanceSquared > rangeSquared) continue;
            float falloff = Mathf.Lerp(1f, 0.55f,
                Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / attackRange));
            if (uncoveredOnly &&
                (uint)index < (uint)_towerDefenseAutoplayCoverageByCell.Length)
                risk /= 1f + _towerDefenseAutoplayCoverageByCell[index] * 0.7f;
            risk *= falloff;
            if (risk > peak)
            {
                peak = risk;
                peakCellIndex = index;
            }
            supportingHeat += risk * 0.08f;
        }
        return Mathf.Clamp01(Mathf.Max(peak, supportingHeat));
    }

    private static bool AreAutoplayHeatCellsInSameHotspot(
        RougeTowerDefenseMap map, int firstIndex, int secondIndex)
    {
        if (map == null || firstIndex < 0 || secondIndex < 0) return false;
        int firstX = firstIndex % map.Width;
        int firstY = firstIndex / map.Width;
        int secondX = secondIndex % map.Width;
        int secondY = secondIndex / map.Width;
        return Mathf.Abs(firstX - secondX) + Mathf.Abs(firstY - secondY) <= 2;
    }

    private void AccumulateAutoplayTowerCoverage(RougeTowerDefenseMap map,
        RougeDefenseTower tower, Vector2Int towerCell, int functionGroup)
    {
        if (map == null || tower == null || tower.AttackRange <= 0f) return;
        int cellCount = map.Width * map.Height;
        float rawPower = Mathf.Max(0.01f, tower.Damage /
            Mathf.Max(0.03f, tower.EffectiveAttackInterval) *
            Mathf.Max(1, tower.AttackProjectileCount));
        rawPower *= 1f + Mathf.Max(0, tower.AttackTargetCount - 1) * 0.12f;
        if (tower.AoeRadius > 0f)
            rawPower *= 1f + Mathf.Min(1.1f, tower.AoeRadius * 0.07f);
        float coveragePower = Mathf.Clamp(Mathf.Log(1f + rawPower) / 4.5f,
            0.55f, 2.6f);
        if (tower.TowerType == RougeTowerType.Ice)
            coveragePower = Mathf.Min(2.8f, coveragePower + 0.75f);
        float cellSize = Mathf.Max(0.1f, map.CellSize);
        int radiusCells = Mathf.Max(1,
            Mathf.CeilToInt(tower.AttackRange / cellSize));
        float rangeSquared = tower.AttackRange * tower.AttackRange;
        Vector3 center = map.CellCenter(towerCell);
        for (int y = Mathf.Max(0, towerCell.y - radiusCells);
             y <= Mathf.Min(map.Height - 1, towerCell.y + radiusCells); y++)
        for (int x = Mathf.Max(0, towerCell.x - radiusCells);
             x <= Mathf.Min(map.Width - 1, towerCell.x + radiusCells); x++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (!map.IsGround(cell)) continue;
            float distanceSquared = (map.CellCenter(cell) - center).sqrMagnitude;
            if (distanceSquared > rangeSquared) continue;
            float falloff = Mathf.Lerp(1f, 0.35f,
                Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / tower.AttackRange));
            int index = y * map.Width + x;
            _towerDefenseAutoplayCoverageByCell[index] += coveragePower * falloff;
            _towerDefenseAutoplayFunctionCoverageByCell[
                functionGroup * cellCount + index] += coveragePower * falloff;
        }
    }

    private void AccumulateAutoplayIncomingWavePressure(
        RougeTowerDefenseMap map, float baselineHealth, float baselineArmor,
        ref AutoplayBattleSnapshot snapshot)
    {
        if (map == null) return;
        // Opening purchases can consume the whole starting bank in much less than the
        // combat forecast. Include at least one normal expansion interval so a lane
        // that starts at second 30 is visible before second 3 spends all available gold.
        float forecastHorizon = Mathf.Max(TowerDefenseAutoplayWaveForecastSeconds,
            TowerDefenseAutoplayExpansionInterval);
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint spawner = _towerDefenseSpawners[i];
            if (spawner == null || !spawner.isActiveAndEnabled ||
                spawner.HasReachedWaveLimit() ||
                !map.WorldToCell(spawner.transform.position,
                    out Vector2Int spawnCell)) continue;
            float seconds = Mathf.Max(0f, spawner.timer);
            snapshot.NextWaveSeconds = Mathf.Min(snapshot.NextWaveSeconds, seconds);
            if (seconds > forecastHorizon) continue;

            float readiness = 1f - Mathf.Clamp01(seconds / forecastHorizon);
            readiness = Mathf.Lerp(0.45f, 1f,
                Mathf.SmoothStep(0f, 1f, readiness));
            int typeIndex = Mathf.Max(0, spawner.GetEnemyTypeIndex());
            RougeEnemyArchetypeConfig archetype = enemyBalance != null &&
                enemyBalance.enemyTypes != null && enemyBalance.enemyTypes.Count > 0
                    ? enemyBalance.enemyTypes[Mathf.Clamp(typeIndex, 0,
                        enemyBalance.enemyTypes.Count - 1)]
                    : null;
            float healthFactor = archetype != null
                ? Mathf.Clamp01(archetype.baseHealth / baselineHealth - 1f)
                : 0f;
            float armorFactor = archetype != null
                ? Mathf.Clamp01((archetype.armor - baselineArmor) / 4f)
                : 0f;
            float hardFactor = Mathf.Clamp01(healthFactor * 0.7f +
                                               armorFactor * 0.6f);
            if (spawner.enemyType == RougeEnemyType.Heavy)
                hardFactor = Mathf.Max(hardFactor, 0.8f);
            float spawnSpeedMultiplier = enemyBalance != null
                ? enemyBalance.EvaluateSpawnSpeedMultiplier(
                    GetTowerDefenseEnemyLevel()) *
                  Mathf.Max(0.01f, _towerDefenseLevelEventSpawnRateMultiplier)
                : 1f;
            float interval = Mathf.Max(0.05f,
                spawner.spawnInterval / Mathf.Max(0.01f, spawnSpeedMultiplier));
            int forecastBatches = 1 + Mathf.FloorToInt(Mathf.Max(0f,
                forecastHorizon - seconds) / interval);
            if (spawner.limitWaveCount)
                forecastBatches = Mathf.Min(forecastBatches,
                    Mathf.Max(0, spawner.maximumWaves - spawner.waveIndex));
            // A dense infinite spawner should drive preparation strongly without
            // making a 0.1-second interval numerically overwhelm every other lane.
            float batchScale = Mathf.Sqrt(Mathf.Clamp(forecastBatches, 1, 12));
            float wavePressure = Mathf.Sqrt(Mathf.Clamp(spawner.spawnCount, 1, 64)) *
                                 Mathf.Lerp(0.42f, 1.15f, readiness) * batchScale;
            float eliteShare = Mathf.Clamp01(hardFactor);
            float hardPressure = wavePressure * eliteShare;
            float crowdPressure = wavePressure * (1f - eliteShare);
            snapshot.IncomingPressure += wavePressure;
            snapshot.IncomingCrowdPressure += crowdPressure;
            snapshot.IncomingElitePressure += hardPressure;
            AccumulateAutoplayForecastRoutePressure(map, spawnCell, wavePressure,
                crowdPressure, hardPressure, readiness, seconds,
                forecastHorizon, GetTowerDefenseEnemySpeed((byte)typeIndex));
        }
    }

    private void AccumulateAutoplayForecastRoutePressure(
        RougeTowerDefenseMap map, Vector2Int source, float total,
        float crowd, float elite, float readiness, float spawnSeconds,
        float forecastHorizon, float enemySpeed)
    {
        int sourceIndex = source.y * map.Width + source.x;
        float sourceDistance = (uint)sourceIndex <
                               (uint)_towerDefenseAutoplayRouteDistanceByCell.Length
            ? _towerDefenseAutoplayRouteDistanceByCell[sourceIndex]
            : 0f;
        float routeTravelSeconds = Mathf.Max(0f, sourceDistance) * map.CellSize /
                                   Mathf.Max(0.1f, enemySpeed);
        int cellCount = map.Width * map.Height;
        Array.Clear(_towerDefenseAutoplayRouteBranchFlowScratch, 0, cellCount);
        _towerDefenseAutoplayRouteBranchFlowScratch[sourceIndex] = 1f;
        for (int orderOffset = 0;
             orderOffset < _towerDefenseAutoplayRouteCellCount; orderOffset++)
        {
            int index =
                _towerDefenseAutoplayRouteCellsByDescendingDistance[orderOffset];
            float branchShare =
                _towerDefenseAutoplayRouteBranchFlowScratch[index];
            if (branchShare <= 0.000001f) continue;
            float routeDistance = _towerDefenseAutoplayRouteDistanceByCell[index];
            if (float.IsPositiveInfinity(routeDistance)) continue;
            float progress = 1f - Mathf.Clamp01(routeDistance /
                _towerDefenseAutoplayMaximumRouteDistance);
            float traveledCells = Mathf.Max(0f, sourceDistance - routeDistance);
            float arrivalSeconds = spawnSeconds + traveledCells * map.CellSize /
                                   Mathf.Max(0.1f, enemySpeed);
            float arrivalHorizon = forecastHorizon + routeTravelSeconds;
            float arrivalReadiness = 1f - Mathf.Clamp01(arrivalSeconds /
                Mathf.Max(0.1f, arrivalHorizon));
            arrivalReadiness = Mathf.Lerp(0.45f, 1f,
                Mathf.SmoothStep(0f, 1f, arrivalReadiness));
            float laneWeight = 0.2f + Mathf.Min(readiness, arrivalReadiness) * 0.14f;
            laneWeight *= branchShare;
            AddAutoplayPressureToCell(index, total * laneWeight,
                crowd * laneWeight, elite * laneWeight, 0f,
                total * laneWeight * Mathf.InverseLerp(0.62f, 1f, progress));
            if (index == _towerDefenseAutoplayRouteMainCell.y * map.Width +
                         _towerDefenseAutoplayRouteMainCell.x) continue;
            int nextCount = CollectAutoplayShortestRouteBranches(map, index,
                _towerDefenseAutoplayRouteBranchNextScratch);
            if (nextCount <= 0) continue;
            float nextShare = branchShare / nextCount;
            for (int nextOffset = 0; nextOffset < nextCount; nextOffset++)
                _towerDefenseAutoplayRouteBranchFlowScratch[
                    _towerDefenseAutoplayRouteBranchNextScratch[nextOffset]] +=
                    nextShare;
        }
    }

    private void AccumulateAutoplayProjectedEnemyPressure(
        RougeTowerDefenseMap map, Vector2Int source, float total,
        float crowd, float elite, float boss, float urgent, float speedRatio)
    {
        Vector2Int current = source;
        int projectionCells = Mathf.Clamp(Mathf.CeilToInt(2f + speedRatio * 1.4f),
            2, TowerDefenseAutoplayPressureProjectionCells);
        for (int step = 0; step <= projectionCells; step++)
        {
            int index = current.y * map.Width + current.x;
            float weight = step == 0 ? 1f : Mathf.Pow(0.68f, step);
            AddAutoplayPressureToCell(index, total * weight, crowd * weight,
                elite * weight, boss * weight,
                urgent * Mathf.Lerp(weight, 1f, 0.22f));
            AddAutoplayActivePressureToCell(index, crowd * weight,
                elite * weight,
                (boss > 0f ? 0f : urgent) *
                Mathf.Lerp(weight, 1f, 0.22f));
            if (step >= projectionCells ||
                !TryGetNextAutoplayRouteCell(map, current, out Vector2Int next))
                break;
            current = next;
        }
    }

    private void AddAutoplayPressureToCell(int index, float total,
        float crowd, float elite, float boss, float urgent)
    {
        if ((uint)index >= (uint)_towerDefenseAutoplayEnemyPressureByCell.Length)
            return;
        _towerDefenseAutoplayEnemyPressureByCell[index] += total;
        _towerDefenseAutoplayCrowdPressureByCell[index] += crowd;
        _towerDefenseAutoplayElitePressureByCell[index] += elite;
        _towerDefenseAutoplayBossPressureByCell[index] += boss;
        _towerDefenseAutoplayUrgentPressureByCell[index] += urgent;
    }

    private void AddAutoplayActivePressureToCell(int index, float crowd,
        float elite, float urgent)
    {
        if ((uint)index >=
            (uint)_towerDefenseAutoplayActiveCrowdPressureByCell.Length)
            return;
        _towerDefenseAutoplayActiveCrowdPressureByCell[index] += crowd;
        _towerDefenseAutoplayActiveElitePressureByCell[index] += elite;
        _towerDefenseAutoplayActiveUrgentPressureByCell[index] += urgent;
    }

    private float GetAutoplayRemainingRouteDistanceInCells(
        RougeTowerDefenseMap map, Vector3 worldPosition, Vector2Int mapCell)
    {
        if (_flowFieldReady && _flowDistanceField.IsCreated &&
            _flowGridDim > 0 && _flowFieldRuntimeCellSize > 0.001f)
        {
            float inverseCellSize = 1f / _flowFieldRuntimeCellSize;
            Unity.Mathematics.int2 flowCell =
                RougeMortonGridUtility.WorldToGrid(
                    new Unity.Mathematics.float2(worldPosition.x, worldPosition.z),
                    _flowGridOrigin, inverseCellSize, _flowGridDim);
            int flowIndex = RougeMortonGridUtility.EncodeMorton(flowCell.x,
                flowCell.y);
            if ((uint)flowIndex < (uint)_flowDistanceField.Length)
            {
                float worldDistance = _flowDistanceField[flowIndex];
                if (Unity.Mathematics.math.isfinite(worldDistance) &&
                    worldDistance >= 0f && worldDistance < 1e17f)
                    return worldDistance / Mathf.Max(0.1f, map.CellSize);
            }
        }

        int mapIndex = mapCell.y * map.Width + mapCell.x;
        return (uint)mapIndex <
               (uint)_towerDefenseAutoplayRouteDistanceByCell.Length
            ? _towerDefenseAutoplayRouteDistanceByCell[mapIndex]
            : float.PositiveInfinity;
    }

    private float GetAutoplaySecondsUntilNextBoss()
    {
        EnsureAutoplayBossPlanInitialized(RougeTowerDefenseMapLoader.ActiveMap);
        if (_towerDefenseBossArrivalActive) return 0f;
        if (!_towerDefenseAutoplayBossPlanAvailable)
            return float.PositiveInfinity;
        // The encounter index advances when the current arrival begins. Once that
        // Boss is live it therefore already identifies the following encounter; do
        // not alias the active Boss to a permanent "next Boss in 0 seconds" signal.
        if (_nextBossEncounterIndex < 0 ||
            _nextBossEncounterIndex >= _bossSchedule.Count)
            return float.PositiveInfinity;
        RougeTowerDefenseMap.BossEncounter encounter =
            _bossSchedule[_nextBossEncounterIndex];
        if (encounter == null) return float.PositiveInfinity;
        float spawnTime = Mathf.Max(0f, encounter.spawnMinute) * 60f;
        return Mathf.Max(0f, spawnTime - Mathf.Max(0f, _survivalTime));
    }

    private void EnsureAutoplayBossPlanInitialized(RougeTowerDefenseMap map)
    {
        if (_towerDefenseAutoplayBossPlanInitialized || map == null) return;
        _towerDefenseAutoplayBossPlanInitialized = true;
        _towerDefenseAutoplayBossPlanAvailable =
            map.HasBossSpawn && _bossSchedule.Count > 0;
    }

    private float CalculateTowerDefenseAutoplayTension(
        AutoplayBattleSnapshot snapshot)
    {
        float gameTime = Mathf.Max(0f, _survivalTime);
        float mainHealthRatio = mainTower != null && mainTower.maxHealth > 0.001f
            ? Mathf.Clamp01(mainTower.CurrentHealth / mainTower.maxHealth)
            : 1f;

        AutoplayFlowPressure flow = MeasureAutoplayEnemyFlow(gameTime);
        _towerDefenseAutoplayEconomyStress =
            MeasureAutoplayEconomyStress(gameTime);
        float ratioRange = Mathf.Max(0.05f,
            1f - TowerDefenseAutoplayDialogueThresholds.lowKillSpawnRatio);
        float flowDeficit = flow.Confidence * Mathf.Clamp01(
            (1f - flow.KillSpawnRatio) / ratioRange);
        flowDeficit *= Mathf.Clamp01(flow.SpawnPerSecond);
        if (flow.KillTrend < 0.75f)
            flowDeficit = Mathf.Clamp01(flowDeficit * 1.15f);
        else if (flow.KillTrend > 1.2f)
            flowDeficit *= 0.78f;
        if (flow.KillVolatility > 0.8f && flow.KillTrend >= 0.9f)
            flowDeficit *= 0.85f;

        float nearBaseIntensity = Mathf.Clamp01(snapshot.NearBaseEnemyWeight);
        bool nearBase = nearBaseIntensity >=
                        TowerDefenseAutoplayNearBaseEarlyWarning;
        float tacticalNearBaseIntensity = Mathf.Clamp01(
            GetAutoplayTacticalNearBaseRisk(gameTime));
        bool tacticalNearBase = tacticalNearBaseIntensity >=
                                TowerDefenseAutoplayNearBaseEarlyWarning;
        if (tacticalNearBase)
        {
            if (float.IsNegativeInfinity(
                    _towerDefenseAutoplayNearBasePressureSince))
                _towerDefenseAutoplayNearBasePressureSince = gameTime;
        }
        else
        {
            _towerDefenseAutoplayNearBasePressureSince = float.NegativeInfinity;
        }

        float nearBaseDuration = tacticalNearBase
            ? Mathf.Max(0f, gameTime -
                _towerDefenseAutoplayNearBasePressureSince)
            : 0f;
        float nearBaseSustainProgress = tacticalNearBase
            ? Mathf.Clamp01(nearBaseDuration /
                TowerDefenseAutoplayNearBaseDecisionSustainSeconds)
            : 0f;
        float configuredDialogueSustainProgress = tacticalNearBase
            ? Mathf.Clamp01(nearBaseDuration / Mathf.Max(0.5f,
                TowerDefenseAutoplayDialogueThresholds.nearBaseSustainSeconds))
            : 0f;
        bool sustainedNearBase = tacticalNearBaseIntensity >=
                TowerDefenseAutoplayImmediateNearBaseCrisis ||
            (nearBaseSustainProgress >= 0.999f && tacticalNearBaseIntensity >=
                TowerDefenseAutoplayEmergencyNearBaseCrisis);
        // Persistent heat remains valuable for emotion, hotspot placement and
        // release hysteresis. Only current/recent real non-Boss occupancy is
        // allowed to enter the tactical sustained-crisis state.
        _towerDefenseAutoplayNearBaseCrisis = nearBaseIntensity;
        _towerDefenseAutoplaySustainedNearBaseCrisis = sustainedNearBase;

        float recentDamageRatio = GetAutoplayRecentMainTowerDamageRatio(
            gameTime, out bool sustainedDamage);
        _towerDefenseAutoplaySustainedMainTowerDamage = sustainedDamage;
        float configuredDamageRatio = Mathf.Max(0.01f,
            TowerDefenseAutoplayDialogueTriggers
                .mainTowerBurstHealthLossPercent * 0.01f);
        float damageStress = Mathf.Clamp01(recentDamageRatio /
                                           configuredDamageRatio);

        // Enemy stats and raw counts grow with game time, so they are deliberately
        // not tension inputs. Presence is ambient; actual loss of control comes from
        // a sustained kill/spawn deficit, the persistent non-Boss heat field in the
        // final three-to-eight route cells, or recent main-tower damage.
        float tension = snapshot.ActiveEnemies > 0 || snapshot.BossEnemies > 0
            ? 0.22f
            : 0.08f;
        if (snapshot.BossEnemies > 0) tension = Mathf.Max(tension, 0.38f);
        if (flowDeficit > 0.001f)
            tension = Mathf.Max(tension, Mathf.Lerp(0.28f, 0.66f, flowDeficit));
        if (nearBase)
        {
            float emotionalNearBaseIntensity = nearBaseIntensity *
                Mathf.Lerp(0.55f, 1f, configuredDialogueSustainProgress);
            tension = Mathf.Max(tension,
                Mathf.Lerp(0.26f, 0.72f, emotionalNearBaseIntensity));
        }
        if (recentDamageRatio > 0.0001f)
            tension = Mathf.Max(tension,
                Mathf.Lerp(0.46f, 0.86f, damageStress));
        if (sustainedDamage)
            tension = Mathf.Max(tension,
                Mathf.Lerp(0.66f, 0.86f, damageStress));
        tension += _towerDefenseAutoplayEconomyStress * 0.05f;
        if (sustainedNearBase && flowDeficit >= 0.55f)
            tension = Mathf.Max(tension, 0.86f);
        if (sustainedNearBase && sustainedDamage)
            tension = Mathf.Max(tension, 0.94f);

        // Low remaining health makes the commander more cautious, but it must not
        // pin the mood at Critical after the pressure line has already recovered.
        // Actual escalation still comes from current flow, proximity, or damage.
        if (mainHealthRatio <= TowerDefenseAutoplayDialogueThresholds
                .baseCriticalHealthRatio)
            tension += 0.08f;
        else if (mainHealthRatio <= TowerDefenseAutoplayDialogueThresholds
                     .baseLowHealthRatio)
            tension += 0.04f;
        if (snapshot.ActiveEnemies <= 0 && snapshot.BossEnemies <= 0 &&
            recentDamageRatio <= 0.0001f)
            tension = Mathf.Min(tension, 0.16f);
        return Mathf.Clamp01(tension);
    }

    private AutoplayFlowPressure MeasureAutoplayEnemyFlow(float gameTime)
    {
        const float minimumSampleInterval = 0.5f;
        float window = TowerDefenseAutoplayDialogueThresholds
            .flowObservationWindowSeconds;
        if (_towerDefenseAutoplayFlowSamples.Count == 0 ||
            gameTime - _towerDefenseAutoplayFlowSamples[
                _towerDefenseAutoplayFlowSamples.Count - 1].GameTime >=
            minimumSampleInterval)
        {
            _towerDefenseAutoplayFlowSamples.Add(new AutoplayFlowSample
            {
                GameTime = gameTime,
                SpawnedTotal = _towerDefenseSpawnedTotal,
                KillTotal = totalKills
            });
        }

        float cutoff = gameTime - window;
        while (_towerDefenseAutoplayFlowSamples.Count > 2 &&
               _towerDefenseAutoplayFlowSamples[1].GameTime <= cutoff)
            _towerDefenseAutoplayFlowSamples.RemoveAt(0);

        AutoplayFlowPressure result = new AutoplayFlowPressure
        {
            KillSpawnRatio = 1f,
            KillTrend = 1f
        };
        if (_towerDefenseAutoplayFlowSamples.Count < 2) return result;

        AutoplayFlowSample first = _towerDefenseAutoplayFlowSamples[0];
        AutoplayFlowSample last = _towerDefenseAutoplayFlowSamples[
            _towerDefenseAutoplayFlowSamples.Count - 1];
        float duration = Mathf.Max(0f, last.GameTime - first.GameTime);
        if (duration < 1.99f) return result;

        int spawned = Mathf.Max(0, last.SpawnedTotal - first.SpawnedTotal);
        int killed = Mathf.Max(0, last.KillTotal - first.KillTotal);
        result.SpawnPerSecond = spawned / duration;
        result.KillsPerSecond = killed / duration;
        result.KillSpawnRatio = spawned <= 0
            ? 1f
            : result.KillsPerSecond / Mathf.Max(1f, result.SpawnPerSecond);
        result.Confidence = Mathf.Clamp01(duration / window);

        float middleTime = first.GameTime + duration * 0.5f;
        int middleIndex = 1;
        while (middleIndex < _towerDefenseAutoplayFlowSamples.Count - 1 &&
               _towerDefenseAutoplayFlowSamples[middleIndex].GameTime < middleTime)
            middleIndex++;
        AutoplayFlowSample middle = _towerDefenseAutoplayFlowSamples[middleIndex];
        float earlyDuration = Mathf.Max(0.1f, middle.GameTime - first.GameTime);
        float lateDuration = Mathf.Max(0.1f, last.GameTime - middle.GameTime);
        float earlyKillRate = Mathf.Max(0,
            middle.KillTotal - first.KillTotal) / earlyDuration;
        float lateKillRate = Mathf.Max(0,
            last.KillTotal - middle.KillTotal) / lateDuration;
        result.KillTrend = lateKillRate / Mathf.Max(1f, earlyKillRate);

        float variance = 0f;
        int intervalCount = 0;
        for (int i = 1; i < _towerDefenseAutoplayFlowSamples.Count; i++)
        {
            AutoplayFlowSample a = _towerDefenseAutoplayFlowSamples[i - 1];
            AutoplayFlowSample b = _towerDefenseAutoplayFlowSamples[i];
            float interval = b.GameTime - a.GameTime;
            if (interval <= 0.0001f) continue;
            float rate = Mathf.Max(0, b.KillTotal - a.KillTotal) / interval;
            float delta = rate - result.KillsPerSecond;
            variance += delta * delta;
            intervalCount++;
        }
        if (intervalCount > 0)
            result.KillVolatility = Mathf.Sqrt(variance / intervalCount) /
                                    Mathf.Max(1f, result.KillsPerSecond);
        return result;
    }

    private float MeasureAutoplayEconomyStress(float gameTime)
    {
        const float minimumSampleInterval = 1f;
        float window = TowerDefenseAutoplayDialogueThresholds
            .economyObservationWindowSeconds;
        if (_towerDefenseAutoplayEconomySamples.Count == 0 ||
            gameTime - _towerDefenseAutoplayEconomySamples[
                _towerDefenseAutoplayEconomySamples.Count - 1].GameTime >=
            minimumSampleInterval)
        {
            _towerDefenseAutoplayEconomySamples.Add(new AutoplayEconomySample
            {
                GameTime = gameTime,
                EarnedTotal = _towerDefenseGoldEarnedTotal,
                SpentTotal = _towerDefenseGoldSpentTotal
            });
        }

        float cutoff = gameTime - window;
        while (_towerDefenseAutoplayEconomySamples.Count > 2 &&
               _towerDefenseAutoplayEconomySamples[1].GameTime <= cutoff)
            _towerDefenseAutoplayEconomySamples.RemoveAt(0);
        if (_towerDefenseAutoplayEconomySamples.Count < 2) return 0f;

        AutoplayEconomySample first = _towerDefenseAutoplayEconomySamples[0];
        AutoplayEconomySample last = _towerDefenseAutoplayEconomySamples[
            _towerDefenseAutoplayEconomySamples.Count - 1];
        float duration = Mathf.Max(0f, last.GameTime - first.GameTime);
        if (duration < 9.99f) return 0f;
        float earnedPerSecond = Mathf.Max(0,
            last.EarnedTotal - first.EarnedTotal) / duration;
        float spentPerSecond = Mathf.Max(0,
            last.SpentTotal - first.SpentTotal) / duration;
        if (spentPerSecond < 1f) return 0f;
        float incomeSpendRatio = earnedPerSecond / Mathf.Max(1f, spentPerSecond);
        float ratioRange = Mathf.Max(0.05f,
            1f - TowerDefenseAutoplayDialogueThresholds.lowIncomeSpendRatio);
        float deficit = Mathf.Clamp01((1f - incomeSpendRatio) / ratioRange);
        return deficit * Mathf.Clamp01(duration / window);
    }

    private float GetAutoplayRecentMainTowerDamageRatio(float gameTime,
        out bool sustained)
    {
        float window = TowerDefenseAutoplayDialogueTriggers
            .mainTowerBurstWindowSeconds;
        int staleCount = 0;
        while (staleCount < _towerDefenseAutoplayEmotionDamageSamples.Count &&
               gameTime - _towerDefenseAutoplayEmotionDamageSamples[staleCount]
                   .GameTime > window)
            staleCount++;
        if (staleCount > 0)
            _towerDefenseAutoplayEmotionDamageSamples.RemoveRange(0, staleCount);

        float damage = 0f;
        for (int i = 0; i < _towerDefenseAutoplayEmotionDamageSamples.Count; i++)
            damage += _towerDefenseAutoplayEmotionDamageSamples[i].Damage;
        float ratio = mainTower != null && mainTower.maxHealth > 0.001f
            ? damage / mainTower.maxHealth
            : 0f;
        float observedDuration = _towerDefenseAutoplayEmotionDamageSamples.Count >= 2
            ? _towerDefenseAutoplayEmotionDamageSamples[
                  _towerDefenseAutoplayEmotionDamageSamples.Count - 1].GameTime -
              _towerDefenseAutoplayEmotionDamageSamples[0].GameTime
            : 0f;
        float configuredRatio = TowerDefenseAutoplayDialogueTriggers
            .mainTowerBurstHealthLossPercent * 0.01f;
        sustained = ratio >= configuredRatio &&
                    _towerDefenseAutoplayEmotionDamageSamples.Count >= 2 &&
                    observedDuration >= Mathf.Min(1f, window * 0.25f);
        return Mathf.Max(0f, ratio);
    }

    private void UpdateAutoplayStrategyMode(AutoplayBattleSnapshot snapshot,
        int standardTowerCount, float mainTowerHealthRatio,
        bool immediateCoreBreach)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        bool hasOpeningCapacity = CountOpenAutoplayBuildCells(map) > 0;
        bool needsOpeningFoundation = hasOpeningCapacity &&
            (standardTowerCount <= 0 || HasMissingEnabledAutoplayFunctionGroup());
        // Raw enemy counts and whole-route pressure grow naturally with the level and
        // must never pin the controller in Emergency. Confirmed local danger, a real
        // urgent fraction, imminent impact, low health or sustained damage may override
        // the normal build/upgrade budget.
        float totalPressure = Mathf.Max(0.001f, snapshot.TotalPressure);
        bool urgentEmergency = snapshot.UrgentPressure >=
                               TowerDefenseAutoplayThresholds
                                   .emergencyUrgentPressureMinimum &&
            snapshot.UrgentPressure / totalPressure >=
            TowerDefenseAutoplayThresholds.emergencyUrgentPressureFraction;
        bool imminentEmergency = snapshot.ImminentPressure >=
                                  TowerDefenseAutoplayThresholds
                                      .emergencyImminentPressure;
        bool healthEmergency = mainTowerHealthRatio <=
                               TowerDefenseAutoplayThresholds
                                   .emergencyMainTowerHealthRatio;
        bool emergency = immediateCoreBreach ||
                         _towerDefenseAutoplaySustainedNearBaseCrisis ||
                         _towerDefenseAutoplaySustainedMainTowerDamage ||
                         urgentEmergency || imminentEmergency || healthEmergency;
        AutoplayStrategyMode desired;
        if (emergency)
            desired = AutoplayStrategyMode.Emergency;
        else if (needsOpeningFoundation)
            desired = AutoplayStrategyMode.Opening;
        else if (snapshot.BossEnemies > 0)
            desired = AutoplayStrategyMode.BossFight;
        else if (snapshot.BossPreparation >=
                 TowerDefenseAutoplayThresholds.prepareBossProgress)
            desired = AutoplayStrategyMode.PrepareBoss;
        else if (snapshot.ActiveEnemies <= TowerDefenseAutoplayThresholds
                     .economyMaximumActiveEnemies &&
                 snapshot.IncomingPressure < TowerDefenseAutoplayThresholds
                     .economyMaximumIncomingPressure &&
                 (float.IsPositiveInfinity(snapshot.NextWaveSeconds) ||
                  snapshot.NextWaveSeconds > TowerDefenseAutoplayThresholds
                      .economyMinimumNextWaveSeconds) &&
                 mainTowerHealthRatio >= TowerDefenseAutoplayThresholds
                     .economyMinimumMainTowerHealthRatio)
            desired = AutoplayStrategyMode.Economy;
        else
            desired = AutoplayStrategyMode.Hold;

        if (desired == _towerDefenseAutoplayStrategyMode) return;
        float gameTime = Mathf.Max(0f, _survivalTime);
        bool higherPriority = GetAutoplayStrategyPriority(desired) >
                              GetAutoplayStrategyPriority(
                                  _towerDefenseAutoplayStrategyMode);
        bool currentPlanMatured = gameTime - _towerDefenseAutoplayStrategyModeSince >=
                                  TowerDefenseAutoplayStrategyHoldSeconds;
        if (!higherPriority && !currentPlanMatured) return;

        _towerDefenseAutoplayStrategyMode = desired;
        _towerDefenseAutoplayStrategyModeSince = gameTime;
        SetAutoplayDecision($"策略切换：{CurrentAutoplayStrategyLabel}。" +
            DescribeAutoplayStrategyContext(snapshot), true);
    }

    private static int GetAutoplayStrategyPriority(AutoplayStrategyMode mode)
    {
        switch (mode)
        {
            case AutoplayStrategyMode.Emergency:
                return TowerDefenseAutoplayStrategy.modePriorities.emergency;
            case AutoplayStrategyMode.BossFight:
                return TowerDefenseAutoplayStrategy.modePriorities.bossFight;
            case AutoplayStrategyMode.Opening:
                return TowerDefenseAutoplayStrategy.modePriorities.opening;
            case AutoplayStrategyMode.PrepareBoss:
                return TowerDefenseAutoplayStrategy.modePriorities.prepareBoss;
            case AutoplayStrategyMode.Hold:
                return TowerDefenseAutoplayStrategy.modePriorities.hold;
            default:
                return TowerDefenseAutoplayStrategy.modePriorities.economy;
        }
    }

    private static string DescribeAutoplayStrategyContext(
        AutoplayBattleSnapshot snapshot)
    {
        if (snapshot.BossEnemies > 0) return " 首领已进入战场，动态分配集火塔。";
        if (snapshot.UrgentPressure >= TowerDefenseAutoplayThresholds
                .emergencyUrgentPressureMinimum)
            return " 近端敌压上升，暂停长期投资。";
        if (snapshot.BossPreparation >=
            TowerDefenseAutoplayThresholds.prepareBossProgress)
            return $" 距离首领约 {snapshot.SecondsUntilBoss:0} 秒，补齐单体火力。";
        if (!float.IsPositiveInfinity(snapshot.NextWaveSeconds))
            return $" 下一批敌军约 {snapshot.NextWaveSeconds:0.0} 秒后抵达。";
        return " 当前没有迫近波次，优先提高长期收益。";
    }

    private static void EnsureAutoplayScoreBuffer(ref float[] buffer, int length)
    {
        if (buffer == null || buffer.Length < length) buffer = new float[length];
    }

    private void EnsureAutoplayOccupancyBuffer(int length)
    {
        if (_towerDefenseAutoplayOccupiedCells == null ||
            _towerDefenseAutoplayOccupiedCells.Length < length)
            _towerDefenseAutoplayOccupiedCells = new bool[length];
    }

    private void EnsureTowerDefenseAutoplayPriorCache(RougeTowerDefenseMap map,
        Vector2Int mainCell, bool hasMainCell)
    {
        int cellCount = map.Width * map.Height;
        if (_towerDefenseAutoplayBuildableTopology == null ||
            _towerDefenseAutoplayBuildableTopology.Length < cellCount)
            _towerDefenseAutoplayBuildableTopology = new bool[cellCount];
        if (_towerDefenseAutoplayEffectiveEffects == null ||
            _towerDefenseAutoplayEffectiveEffects.Length < cellCount)
            _towerDefenseAutoplayEffectiveEffects =
                new RougeTowerPlaceEffect[cellCount];
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayRouteDistanceByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayRouteTrafficByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(
            ref _towerDefenseAutoplayRouteCoreTrafficByCell, cellCount);
        if (_towerDefenseAutoplayRouteNextByCell == null ||
            _towerDefenseAutoplayRouteNextByCell.Length < cellCount)
            _towerDefenseAutoplayRouteNextByCell = new int[cellCount];
        if (_towerDefenseAutoplayRouteCellsByDescendingDistance == null ||
            _towerDefenseAutoplayRouteCellsByDescendingDistance.Length < cellCount)
            _towerDefenseAutoplayRouteCellsByDescendingDistance = new int[cellCount];
        if (_towerDefenseAutoplayRouteBranchFlowScratch == null ||
            _towerDefenseAutoplayRouteBranchFlowScratch.Length < cellCount)
            _towerDefenseAutoplayRouteBranchFlowScratch = new float[cellCount];
        if (_towerDefenseAutoplayRoutePredecessorCountByCell == null ||
            _towerDefenseAutoplayRoutePredecessorCountByCell.Length < cellCount)
            _towerDefenseAutoplayRoutePredecessorCountByCell = new int[cellCount];
        if (_towerDefenseAutoplayRouteTangentByCell == null ||
            _towerDefenseAutoplayRouteTangentByCell.Length < cellCount)
            _towerDefenseAutoplayRouteTangentByCell = new Vector2[cellCount];
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayCoverageByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(
            ref _towerDefenseAutoplayFunctionCoverageByCell, cellCount * 3);

        int topologyHash = 486187739;
        topologyHash = MixAutoplayPriorHash(topologyHash, map.GetInstanceID());
        topologyHash = MixAutoplayPriorHash(topologyHash, map.Width);
        topologyHash = MixAutoplayPriorHash(topologyHash, map.Height);
        topologyHash = MixAutoplayPriorHash(topologyHash,
            Mathf.RoundToInt(map.CellSize * 1000f));
        topologyHash = MixAutoplayPriorHash(topologyHash,
            Mathf.RoundToInt(map.Origin.x * 100f));
        topologyHash = MixAutoplayPriorHash(topologyHash,
            Mathf.RoundToInt(map.Origin.y * 100f));
        topologyHash = MixAutoplayPriorHash(topologyHash, hasMainCell ? 1 : 0);
        bool authoritativeFlowAvailable = _flowFieldReady &&
            _flowDistanceField.IsCreated && _flowDirectionField.IsCreated &&
            _flowGridDim > 0 && _flowFieldRuntimeCellSize > 0.001f;
        // The first autoplay snapshot can be built before the runtime flow solve has
        // completed. Include readiness in the topology key so ordinary routes are
        // rebuilt once the authoritative directions become available.
        topologyHash = MixAutoplayPriorHash(topologyHash,
            authoritativeFlowAvailable ? 1 : 0);
        if (hasMainCell)
        {
            topologyHash = MixAutoplayPriorHash(topologyHash, mainCell.x);
            topologyHash = MixAutoplayPriorHash(topologyHash, mainCell.y);
        }

        int effectHash = 16777619;
        RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
        bool useLoaderEffects = loader != null && loader.Map == map;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            int cellIndex = y * map.Width + x;
            bool ground = map.IsGround(cell);
            bool buildable = map.IsTowerPlace(cell);
            _towerDefenseAutoplayBuildableTopology[cellIndex] = buildable;
            topologyHash = MixAutoplayPriorHash(topologyHash,
                (ground ? 1 : 0) | (buildable ? 2 : 0));

            RougeTowerPlaceEffect effect = buildable
                ? useLoaderEffects
                    ? loader.GetEffectiveTowerPlaceEffect(cell)
                    : map.GetTowerPlaceEffect(cell)
                : RougeTowerPlaceEffect.None;
            effect = RougeTowerPlaceEffectRules.NormalizeLegacy(effect);
            _towerDefenseAutoplayEffectiveEffects[cellIndex] = effect;
            effectHash = MixAutoplayPriorHash(effectHash, (int)effect);
        }

        topologyHash = MixAutoplayPriorHash(topologyHash,
            _towerDefenseSpawners.Count);
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint spawner = _towerDefenseSpawners[i];
            if (spawner == null)
            {
                topologyHash = MixAutoplayPriorHash(topologyHash, -1);
                continue;
            }
            if (map.WorldToCell(spawner.transform.position, out Vector2Int spawnCell))
            {
                topologyHash = MixAutoplayPriorHash(topologyHash, spawnCell.x);
                topologyHash = MixAutoplayPriorHash(topologyHash, spawnCell.y);
                topologyHash = MixAutoplayPriorHash(topologyHash,
                    Mathf.Clamp(spawner.spawnCount, 1, 64));
                topologyHash = MixAutoplayPriorHash(topologyHash,
                    Mathf.RoundToInt(spawner.spawnInterval * 100f));
                topologyHash = MixAutoplayPriorHash(topologyHash,
                    spawner.isActiveAndEnabled ? 1 : 0);
                topologyHash = MixAutoplayPriorHash(topologyHash,
                    spawner.HasReachedWaveLimit() ? 1 : 0);
            }
            else
            {
                Vector3 position = spawner.transform.position;
                topologyHash = MixAutoplayPriorHash(topologyHash,
                    Mathf.RoundToInt(position.x * 10f));
                topologyHash = MixAutoplayPriorHash(topologyHash,
                    Mathf.RoundToInt(position.z * 10f));
            }
        }
        if (bossSpawnPoint != null &&
            map.WorldToCell(bossSpawnPoint.transform.position,
                out Vector2Int bossSpawnCell))
        {
            topologyHash = MixAutoplayPriorHash(topologyHash, bossSpawnCell.x);
            topologyHash = MixAutoplayPriorHash(topologyHash, bossSpawnCell.y);
        }

        bool topologyChanged = _towerDefenseAutoplayPriorDirty ||
            _towerDefenseAutoplayPriorMap != map ||
            _towerDefenseAutoplayPriorTopologyHash != topologyHash;
        bool effectsChanged = topologyChanged ||
            _towerDefenseAutoplayPriorEffectHash != effectHash;
        if (topologyChanged)
        {
            RebuildTowerDefenseAutoplayTopologyPriors(map, mainCell, hasMainCell);
            RebuildTowerDefenseAutoplayUpgradePriors();
        }
        bool bossRouteChanged = RebuildAutoplayBossRoute(map);
        if (effectsChanged || bossRouteChanged)
        {
            // Runtime charge effects and permanent frost alter only this small
            // type×cell table. An authoritative Boss-flow change also refreshes
            // only the cached per-cell Boss coverage, not the general route topology.
            RebuildTowerDefenseAutoplayBuildPriors(map);
        }
        if (topologyChanged || effectsChanged || bossRouteChanged)
            _towerDefenseAutoplayPriorRevision++;

        _towerDefenseAutoplayPriorMap = map;
        _towerDefenseAutoplayPriorTopologyHash = topologyHash;
        _towerDefenseAutoplayPriorEffectHash = effectHash;
        _towerDefenseAutoplayPriorDirty = false;
    }

    private static int MixAutoplayPriorHash(int hash, int value)
    {
        return unchecked((hash ^ value) * 16777619);
    }

    private bool RebuildAutoplayBossRoute(RougeTowerDefenseMap map)
    {
        int cellCount = map != null ? map.Width * map.Height : 0;
        if (cellCount <= 0)
        {
            bool cleared = _towerDefenseAutoplayBossRouteCellCount != 0;
            _towerDefenseAutoplayBossRouteCellCount = 0;
            _towerDefenseAutoplayBossRouteHash = 0;
            _towerDefenseAutoplayBossRouteUsesFlowField = false;
            return cleared;
        }
        if (_towerDefenseAutoplayBossRouteCells == null ||
            _towerDefenseAutoplayBossRouteCells.Length < cellCount)
            _towerDefenseAutoplayBossRouteCells = new int[cellCount];
        if (_towerDefenseAutoplayBossRouteVisited == null ||
            _towerDefenseAutoplayBossRouteVisited.Length < cellCount)
            _towerDefenseAutoplayBossRouteVisited = new bool[cellCount];

        Array.Clear(_towerDefenseAutoplayBossRouteVisited, 0, cellCount);
        int routeCount = 0;
        bool usesFlowField = TryBuildAutoplayBossRouteFromFlowField(map,
            ref routeCount);
        if (!usesFlowField)
        {
            Array.Clear(_towerDefenseAutoplayBossRouteVisited, 0, cellCount);
            routeCount = 0;
            BuildFallbackAutoplayBossRoute(map, ref routeCount);
        }

        int routeHash = MixAutoplayPriorHash(16777619,
            usesFlowField ? 1 : 0);
        routeHash = MixAutoplayPriorHash(routeHash, routeCount);
        for (int i = 0; i < routeCount; i++)
            routeHash = MixAutoplayPriorHash(routeHash,
                _towerDefenseAutoplayBossRouteCells[i]);
        bool changed = routeHash != _towerDefenseAutoplayBossRouteHash ||
                       routeCount != _towerDefenseAutoplayBossRouteCellCount ||
                       usesFlowField !=
                       _towerDefenseAutoplayBossRouteUsesFlowField;
        _towerDefenseAutoplayBossRouteCellCount = routeCount;
        _towerDefenseAutoplayBossRouteHash = routeHash;
        _towerDefenseAutoplayBossRouteUsesFlowField = usesFlowField;
        return changed;
    }

    private bool TryBuildAutoplayBossRouteFromFlowField(
        RougeTowerDefenseMap map, ref int routeCount)
    {
        if (!_flowFieldReady || !_flowDistanceField.IsCreated ||
            !_flowDirectionField.IsCreated || _flowGridDim <= 0 ||
            _flowFieldRuntimeCellSize <= 0.001f ||
            !TryGetAutoplayBossRouteStart(map, out Vector2Int start))
            return false;

        Vector3 startWorld = map.CellCenter(start);
        if (bossSpawnPoint != null &&
            map.WorldToCell(bossSpawnPoint.transform.position,
                out Vector2Int runtimeStart) && runtimeStart == start)
            startWorld = bossSpawnPoint.transform.position;
        float inverseCellSize = 1f / _flowFieldRuntimeCellSize;
        int2 flowCell = RougeMortonGridUtility.WorldToGrid(
            new float2(startWorld.x, startWorld.z), _flowGridOrigin,
            inverseCellSize, _flowGridDim);
        int maximumSteps = Mathf.Min(_flowDirectionField.Length,
            Mathf.Max(64, _flowGridDim * 4));

        for (int step = 0; step < maximumSteps; step++)
        {
            int flowIndex = RougeMortonGridUtility.EncodeMorton(flowCell.x,
                flowCell.y);
            if ((uint)flowIndex >= (uint)_flowDirectionField.Length ||
                (uint)flowIndex >= (uint)_flowDistanceField.Length)
                return false;

            float2 world = _flowGridOrigin +
                           (new float2(flowCell.x + 0.5f,
                                flowCell.y + 0.5f) *
                            _flowFieldRuntimeCellSize);
            if (map.WorldToCell(new Vector3(world.x, 0f, world.y),
                    out Vector2Int mapCell) && map.IsGround(mapCell))
            {
                if (!TryAppendAutoplayBossRouteCell(map, mapCell,
                        ref routeCount)) return false;
                if (mapCell == _towerDefenseAutoplayRouteMainCell)
                    return routeCount > 0;
            }

            float currentDistance = _flowDistanceField[flowIndex];
            float2 direction = _flowDirectionField[flowIndex];
            if (!math.isfinite(currentDistance) || currentDistance < 0f ||
                currentDistance >= 1e17f ||
                math.lengthsq(direction) <= 0.0001f) return false;
            int stepX = direction.x > 0.35f ? 1 : direction.x < -0.35f ? -1 : 0;
            int stepY = direction.y > 0.35f ? 1 : direction.y < -0.35f ? -1 : 0;
            if (stepX == 0 && stepY == 0) return false;
            int2 next = flowCell + new int2(stepX, stepY);
            if ((uint)next.x >= (uint)_flowGridDim ||
                (uint)next.y >= (uint)_flowGridDim) return false;
            int nextIndex = RougeMortonGridUtility.EncodeMorton(next.x, next.y);
            if ((uint)nextIndex >= (uint)_flowDistanceField.Length) return false;
            float nextDistance = _flowDistanceField[nextIndex];
            if (!math.isfinite(nextDistance) ||
                nextDistance + 0.0001f >= currentDistance) return false;
            flowCell = next;
        }
        return false;
    }

    private void BuildFallbackAutoplayBossRoute(RougeTowerDefenseMap map,
        ref int routeCount)
    {
        if (!TryGetAutoplayBossRouteStart(map, out Vector2Int current)) return;
        int cellCount = map.Width * map.Height;
        for (int step = 0; step < cellCount; step++)
        {
            if (!TryAppendAutoplayBossRouteCell(map, current,
                    ref routeCount)) return;
            if (current == _towerDefenseAutoplayRouteMainCell) return;
            int index = current.y * map.Width + current.x;
            int nextIndex = (uint)index <
                            (uint)_towerDefenseAutoplayRouteNextByCell.Length
                ? _towerDefenseAutoplayRouteNextByCell[index]
                : -1;
            if ((uint)nextIndex >= (uint)cellCount || nextIndex == index) return;
            current = new Vector2Int(nextIndex % map.Width,
                nextIndex / map.Width);
        }
    }

    private bool TryAppendAutoplayBossRouteCell(RougeTowerDefenseMap map,
        Vector2Int cell, ref int routeCount)
    {
        int index = cell.y * map.Width + cell.x;
        if ((uint)index >= (uint)_towerDefenseAutoplayBossRouteVisited.Length)
            return false;
        if (_towerDefenseAutoplayBossRouteVisited[index]) return true;
        if ((uint)routeCount >= (uint)_towerDefenseAutoplayBossRouteCells.Length)
            return false;
        _towerDefenseAutoplayBossRouteVisited[index] = true;
        _towerDefenseAutoplayBossRouteCells[routeCount++] = index;
        return true;
    }

    private float GetAutoplayBossRouteSegmentLengthCells(int routeOffset,
        int width)
    {
        if ((uint)routeOffset >=
            (uint)_towerDefenseAutoplayBossRouteCellCount) return 0f;
        if (routeOffset + 1 >= _towerDefenseAutoplayBossRouteCellCount) return 1f;
        int index = _towerDefenseAutoplayBossRouteCells[routeOffset];
        int nextIndex = _towerDefenseAutoplayBossRouteCells[routeOffset + 1];
        int dx = nextIndex % width - index % width;
        int dy = nextIndex / width - index / width;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private void RebuildTowerDefenseAutoplayTopologyPriors(
        RougeTowerDefenseMap map, Vector2Int mainCell, bool hasMainCell)
    {
        int cellCount = map.Width * map.Height;
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayGroundValueByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayRouteDistanceByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(ref _towerDefenseAutoplayRouteTrafficByCell,
            cellCount);
        EnsureAutoplayScoreBuffer(
            ref _towerDefenseAutoplayRouteCoreTrafficByCell, cellCount);
        if (_towerDefenseAutoplayRouteNextByCell == null ||
            _towerDefenseAutoplayRouteNextByCell.Length < cellCount)
            _towerDefenseAutoplayRouteNextByCell = new int[cellCount];
        if (_towerDefenseAutoplayRouteTangentByCell == null ||
            _towerDefenseAutoplayRouteTangentByCell.Length < cellCount)
            _towerDefenseAutoplayRouteTangentByCell = new Vector2[cellCount];
        Array.Clear(_towerDefenseAutoplayGroundValueByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayRouteTrafficByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayRouteCoreTrafficByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayRoutePredecessorCountByCell, 0,
            cellCount);
        Array.Clear(_towerDefenseAutoplayRouteTangentByCell, 0, cellCount);
        for (int i = 0; i < cellCount; i++)
            _towerDefenseAutoplayRouteNextByCell[i] = -1;
        _towerDefenseAutoplayMaximumCoreTraffic = 1f;
        for (int i = 0; i < cellCount; i++)
            _towerDefenseAutoplayRouteDistanceByCell[i] = float.PositiveInfinity;

        _towerDefenseAutoplayRouteMainCell = mainCell;
        _towerDefenseAutoplayHasRouteMainCell = hasMainCell &&
                                                map.IsGround(mainCell);
        _towerDefenseAutoplayMaximumRouteDistance = 1f;
        if (!_towerDefenseAutoplayHasRouteMainCell)
        {
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (map.IsGround(cell))
                    _towerDefenseAutoplayGroundValueByCell[y * map.Width + x] = 1f;
            }
            return;
        }

        // A small reverse Dijkstra on the authored tile grid mirrors the game's
        // flow-field semantics while remaining stable and cheap (typical maps are
        // only a few hundred cells). It replaces the old spawn-to-goal straight-line
        // guess, which broke as soon as a route bent around a wall or tower pad.
        bool[] visited = new bool[cellCount];
        int mainIndex = mainCell.y * map.Width + mainCell.x;
        _towerDefenseAutoplayRouteDistanceByCell[mainIndex] = 0f;
        for (int iteration = 0; iteration < cellCount; iteration++)
        {
            int currentIndex = -1;
            float currentDistance = float.PositiveInfinity;
            for (int index = 0; index < cellCount; index++)
            {
                if (visited[index] ||
                    _towerDefenseAutoplayRouteDistanceByCell[index] >=
                    currentDistance) continue;
                currentIndex = index;
                currentDistance = _towerDefenseAutoplayRouteDistanceByCell[index];
            }
            if (currentIndex < 0 || float.IsPositiveInfinity(currentDistance))
                break;
            visited[currentIndex] = true;
            Vector2Int current = new Vector2Int(currentIndex % map.Width,
                currentIndex / map.Width);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                Vector2Int neighbor = current + new Vector2Int(dx, dy);
                if (!IsAutoplayRouteStepValid(map, current, neighbor)) continue;
                int neighborIndex = neighbor.y * map.Width + neighbor.x;
                float stepCost = dx != 0 && dy != 0 ? 1.41421356f : 1f;
                float candidate = currentDistance + stepCost;
                if (candidate < _towerDefenseAutoplayRouteDistanceByCell[neighborIndex])
                    _towerDefenseAutoplayRouteDistanceByCell[neighborIndex] = candidate;
            }
        }

        RebuildAutoplayRouteDistanceOrder(map);
        RebuildAutoplayRouteSkeleton(map);

        float maximumSourceDistance = 0f;
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint spawner = _towerDefenseSpawners[i];
            if (spawner == null || !spawner.isActiveAndEnabled ||
                spawner.HasReachedWaveLimit() ||
                !map.WorldToCell(spawner.transform.position,
                    out Vector2Int spawnCell)) continue;
            // Preserve the authored density ordering without letting a 0.1-second
            // spawner numerically erase every other lane. The old Max(1, interval)
            // flattened all intervals below 2.86 seconds to exactly the same weight.
            float sourceWeight = Mathf.Sqrt(
                Mathf.Clamp(spawner.spawnCount, 1, 64) /
                Mathf.Max(0.1f, spawner.spawnInterval));
            AccumulateAutoplayRouteTraffic(map, spawnCell, sourceWeight,
                ref maximumSourceDistance);
        }
        if (bossSpawnPoint != null &&
            map.WorldToCell(bossSpawnPoint.transform.position,
                out Vector2Int bossSpawnCell))
            AccumulateAutoplayRouteTraffic(map, bossSpawnCell, 2.2f,
                ref maximumSourceDistance);
        RebuildAutoplayCoreRoutePredecessorCounts(map);

        float maximumTraffic = 0f;
        float maximumCoreTraffic = 0f;
        float maximumFiniteDistance = 0f;
        for (int index = 0; index < cellCount; index++)
        {
            maximumTraffic = Mathf.Max(maximumTraffic,
                _towerDefenseAutoplayRouteTrafficByCell[index]);
            maximumCoreTraffic = Mathf.Max(maximumCoreTraffic,
                _towerDefenseAutoplayRouteCoreTrafficByCell[index]);
            float distance = _towerDefenseAutoplayRouteDistanceByCell[index];
            if (!float.IsPositiveInfinity(distance))
                maximumFiniteDistance = Mathf.Max(maximumFiniteDistance, distance);
        }
        _towerDefenseAutoplayMaximumCoreTraffic = Mathf.Max(0.0001f,
            maximumCoreTraffic);
        _towerDefenseAutoplayMaximumRouteDistance = Mathf.Max(1f,
            maximumSourceDistance > 0f ? maximumSourceDistance : maximumFiniteDistance);

        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (!map.IsGround(cell)) continue;
            int index = y * map.Width + x;
            float traffic = maximumTraffic > 0.0001f
                ? _towerDefenseAutoplayRouteTrafficByCell[index] / maximumTraffic
                : 0f;
            float distance = _towerDefenseAutoplayRouteDistanceByCell[index];
            float progress = float.IsPositiveInfinity(distance)
                ? 0f
                : 1f - Mathf.Clamp01(distance /
                    _towerDefenseAutoplayMaximumRouteDistance);
            // Off-route ground keeps a tiny exploration value. Authored lanes and
            // chokepoints shared by several spawners dominate tower uptime instead of
            // rewarding raw empty floor area.
            _towerDefenseAutoplayGroundValueByCell[index] = 0.08f +
                traffic * 4.6f + (traffic > 0.01f ? progress * 0.72f : 0f);
        }
    }

    private void RebuildAutoplayRouteSkeleton(RougeTowerDefenseMap map)
    {
        int cellCount = map.Width * map.Height;
        for (int index = 0; index < cellCount; index++)
        {
            int x = index % map.Width;
            int y = index / map.Width;
            Vector2Int cell = new Vector2Int(x, y);
            if (!map.IsGround(cell) ||
                float.IsPositiveInfinity(
                    _towerDefenseAutoplayRouteDistanceByCell[index]))
                continue;

            // The coarse Dijkstra distance remains a safe fallback during startup,
            // but once the runtime flow is ready it must decide symmetric forks too.
            // This is the same field enemies consume, so left/right lane accounting
            // can no longer disagree with the route visible on screen.
            bool hasNext = TryGetAutoplayFlowNextMapCell(map, cell,
                out Vector2Int next);
            if (!hasNext)
                hasNext = TryGetNextAutoplayRouteCell(map, cell, out next);
            if (!hasNext) continue;
            int nextIndex = next.y * map.Width + next.x;
            _towerDefenseAutoplayRouteNextByCell[index] = nextIndex;
        }

        // Use a short look-ahead instead of a single grid edge. Eight-neighbour flow
        // fields often alternate horizontal/diagonal steps along a visually straight
        // lane; a one-step tangent would falsely describe that lane as a bend.
        for (int index = 0; index < cellCount; index++)
        {
            if (_towerDefenseAutoplayRouteNextByCell[index] < 0) continue;
            int lookahead = index;
            for (int step = 0; step < 3; step++)
            {
                int nextIndex = _towerDefenseAutoplayRouteNextByCell[lookahead];
                if ((uint)nextIndex >= (uint)cellCount || nextIndex == lookahead)
                    break;
                lookahead = nextIndex;
            }
            Vector2 delta = new Vector2(lookahead % map.Width - index % map.Width,
                lookahead / map.Width - index / map.Width);
            _towerDefenseAutoplayRouteTangentByCell[index] =
                delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.zero;
        }
    }

    private void RebuildAutoplayRouteDistanceOrder(RougeTowerDefenseMap map)
    {
        _towerDefenseAutoplayRouteCellCount = 0;
        int cellCount = map.Width * map.Height;
        for (int index = 0; index < cellCount; index++)
        {
            if (float.IsPositiveInfinity(
                    _towerDefenseAutoplayRouteDistanceByCell[index])) continue;
            int insert = _towerDefenseAutoplayRouteCellCount++;
            float distance = _towerDefenseAutoplayRouteDistanceByCell[index];
            while (insert > 0)
            {
                int previous =
                    _towerDefenseAutoplayRouteCellsByDescendingDistance[insert - 1];
                if (_towerDefenseAutoplayRouteDistanceByCell[previous] >= distance)
                    break;
                _towerDefenseAutoplayRouteCellsByDescendingDistance[insert] = previous;
                insert--;
            }
            _towerDefenseAutoplayRouteCellsByDescendingDistance[insert] = index;
        }
    }

    private void RebuildAutoplayCoreRoutePredecessorCounts(
        RougeTowerDefenseMap map)
    {
        int cellCount = map.Width * map.Height;
        Array.Clear(_towerDefenseAutoplayRoutePredecessorCountByCell, 0,
            cellCount);
        for (int index = 0; index < cellCount; index++)
        {
            if (_towerDefenseAutoplayRouteCoreTrafficByCell[index] <= 0.0001f)
                continue;
            int nextCount = CollectAutoplayShortestRouteBranches(map, index,
                _towerDefenseAutoplayRouteBranchNextScratch);
            for (int nextOffset = 0; nextOffset < nextCount; nextOffset++)
            {
                int nextIndex =
                    _towerDefenseAutoplayRouteBranchNextScratch[nextOffset];
                if (_towerDefenseAutoplayRouteCoreTrafficByCell[nextIndex] <=
                    0.0001f) continue;
                _towerDefenseAutoplayRoutePredecessorCountByCell[nextIndex]++;
            }
        }
    }

    private int CollectAutoplayShortestRouteBranches(RougeTowerDefenseMap map,
        int currentIndex, int[] results)
    {
        if (map == null || results == null || results.Length == 0 ||
            (uint)currentIndex >= (uint)(map.Width * map.Height)) return 0;
        float currentDistance =
            _towerDefenseAutoplayRouteDistanceByCell[currentIndex];
        if (!float.IsFinite(currentDistance) || currentDistance <= 0f) return 0;
        Vector2Int current = new Vector2Int(currentIndex % map.Width,
            currentIndex / map.Width);
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            Vector2Int candidate = current + new Vector2Int(dx, dy);
            if (!IsAutoplayRouteStepValid(map, current, candidate)) continue;
            int candidateIndex = candidate.y * map.Width + candidate.x;
            float candidateDistance =
                _towerDefenseAutoplayRouteDistanceByCell[candidateIndex];
            float stepCost = dx != 0 && dy != 0 ? 1.41421356f : 1f;
            if (!float.IsFinite(candidateDistance) ||
                candidateDistance >= currentDistance - 0.0001f ||
                candidateDistance + stepCost > currentDistance + 0.001f)
                continue;
            if (count < results.Length) results[count++] = candidateIndex;
        }
        return count;
    }

    private static bool IsAutoplayRouteStepValid(RougeTowerDefenseMap map,
        Vector2Int from, Vector2Int to)
    {
        if (to.x < 0 || to.y < 0 || to.x >= map.Width || to.y >= map.Height ||
            !map.IsGround(to)) return false;
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if (dx == 0 || dy == 0) return true;
        // Do not let the coarse planner cut diagonally through two blocked corners.
        return map.IsGround(new Vector2Int(from.x + dx, from.y)) &&
               map.IsGround(new Vector2Int(from.x, from.y + dy));
    }

    private void AccumulateAutoplayRouteTraffic(RougeTowerDefenseMap map,
        Vector2Int source, float weight, ref float maximumSourceDistance)
    {
        if (source.x < 0 || source.y < 0 || source.x >= map.Width ||
            source.y >= map.Height || !map.IsGround(source)) return;
        int sourceIndex = source.y * map.Width + source.x;
        float sourceDistance = _towerDefenseAutoplayRouteDistanceByCell[sourceIndex];
        if (float.IsPositiveInfinity(sourceDistance)) return;
        maximumSourceDistance = Mathf.Max(maximumSourceDistance, sourceDistance);

        int cellCount = map.Width * map.Height;
        Array.Clear(_towerDefenseAutoplayRouteBranchFlowScratch, 0, cellCount);
        _towerDefenseAutoplayRouteBranchFlowScratch[sourceIndex] = weight;
        for (int orderOffset = 0;
             orderOffset < _towerDefenseAutoplayRouteCellCount; orderOffset++)
        {
            int index =
                _towerDefenseAutoplayRouteCellsByDescendingDistance[orderOffset];
            float branchWeight =
                _towerDefenseAutoplayRouteBranchFlowScratch[index];
            if (branchWeight <= 0.000001f) continue;
            _towerDefenseAutoplayRouteCoreTrafficByCell[index] += branchWeight;
            _towerDefenseAutoplayRouteTrafficByCell[index] += branchWeight;
            Vector2Int current = new Vector2Int(index % map.Width,
                index / map.Width);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                Vector2Int shoulder = current + new Vector2Int(dx, dy);
                if (shoulder.x < 0 || shoulder.y < 0 || shoulder.x >= map.Width ||
                    shoulder.y >= map.Height || !map.IsGround(shoulder)) continue;
                _towerDefenseAutoplayRouteTrafficByCell[
                    shoulder.y * map.Width + shoulder.x] += branchWeight * 0.12f;
            }
            if (current == _towerDefenseAutoplayRouteMainCell) continue;
            int nextCount = CollectAutoplayShortestRouteBranches(map, index,
                _towerDefenseAutoplayRouteBranchNextScratch);
            if (nextCount <= 0) continue;
            float nextWeight = branchWeight / nextCount;
            for (int nextOffset = 0; nextOffset < nextCount; nextOffset++)
                _towerDefenseAutoplayRouteBranchFlowScratch[
                    _towerDefenseAutoplayRouteBranchNextScratch[nextOffset]] +=
                    nextWeight;
        }
    }

    private bool TryGetNextAutoplayRouteCell(RougeTowerDefenseMap map,
        Vector2Int current, out Vector2Int next)
    {
        next = current;
        if (current.x < 0 || current.y < 0 || current.x >= map.Width ||
            current.y >= map.Height) return false;
        int currentIndex = current.y * map.Width + current.x;
        int cachedNext = (uint)currentIndex <
                         (uint)_towerDefenseAutoplayRouteNextByCell.Length
            ? _towerDefenseAutoplayRouteNextByCell[currentIndex]
            : -1;
        if ((uint)cachedNext < (uint)(map.Width * map.Height) &&
            cachedNext != currentIndex)
        {
            Vector2Int cachedCell = new Vector2Int(cachedNext % map.Width,
                cachedNext / map.Width);
            if (IsAutoplayRouteStepValid(map, current, cachedCell))
            {
                next = cachedCell;
                return true;
            }
        }
        float currentDistance = _towerDefenseAutoplayRouteDistanceByCell[
            currentIndex];
        float bestDistance = currentDistance;
        float bestGoalDistance = float.PositiveInfinity;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            Vector2Int candidate = current + new Vector2Int(dx, dy);
            if (!IsAutoplayRouteStepValid(map, current, candidate)) continue;
            float distance = _towerDefenseAutoplayRouteDistanceByCell[
                candidate.y * map.Width + candidate.x];
            float goalDistance = (candidate -
                                  _towerDefenseAutoplayRouteMainCell).sqrMagnitude;
            if (distance > bestDistance - 0.0001f ||
                Mathf.Approximately(distance, bestDistance) &&
                goalDistance >= bestGoalDistance) continue;
            bestDistance = distance;
            bestGoalDistance = goalDistance;
            next = candidate;
        }
        return next != current;
    }

    private bool TryGetAutoplayFlowNextMapCell(RougeTowerDefenseMap map,
        Vector2Int current, out Vector2Int next)
    {
        next = current;
        if (map == null || !_flowFieldReady || !_flowDistanceField.IsCreated ||
            !_flowDirectionField.IsCreated || _flowGridDim <= 0 ||
            _flowFieldRuntimeCellSize <= 0.001f || !map.IsGround(current))
            return false;

        Vector3 worldStart = map.CellCenter(current);
        float inverseCellSize = 1f / _flowFieldRuntimeCellSize;
        int2 flowCell = RougeMortonGridUtility.WorldToGrid(
            new float2(worldStart.x, worldStart.z), _flowGridOrigin,
            inverseCellSize, _flowGridDim);
        int maximumSteps = Mathf.Clamp(
            Mathf.CeilToInt(map.CellSize / _flowFieldRuntimeCellSize) * 3,
            8, 64);

        for (int step = 0; step < maximumSteps; step++)
        {
            int flowIndex = RougeMortonGridUtility.EncodeMorton(flowCell.x,
                flowCell.y);
            if ((uint)flowIndex >= (uint)_flowDistanceField.Length ||
                (uint)flowIndex >= (uint)_flowDirectionField.Length)
                return false;
            float currentDistance = _flowDistanceField[flowIndex];
            float2 direction = _flowDirectionField[flowIndex];
            if (!math.isfinite(currentDistance) || currentDistance < 0f ||
                currentDistance >= 1e17f ||
                math.lengthsq(direction) <= 0.0001f) return false;

            int stepX = direction.x > 0.35f ? 1 : direction.x < -0.35f ? -1 : 0;
            int stepY = direction.y > 0.35f ? 1 : direction.y < -0.35f ? -1 : 0;
            if (stepX == 0 && stepY == 0) return false;
            int2 nextFlow = flowCell + new int2(stepX, stepY);
            if ((uint)nextFlow.x >= (uint)_flowGridDim ||
                (uint)nextFlow.y >= (uint)_flowGridDim) return false;
            int nextFlowIndex = RougeMortonGridUtility.EncodeMorton(nextFlow.x,
                nextFlow.y);
            if ((uint)nextFlowIndex >= (uint)_flowDistanceField.Length)
                return false;
            float nextDistance = _flowDistanceField[nextFlowIndex];
            if (!math.isfinite(nextDistance) ||
                nextDistance + 0.0001f >= currentDistance) return false;

            flowCell = nextFlow;
            float2 nextWorld = _flowGridOrigin +
                (new float2(flowCell.x + 0.5f, flowCell.y + 0.5f) *
                 _flowFieldRuntimeCellSize);
            if (!map.WorldToCell(new Vector3(nextWorld.x, 0f, nextWorld.y),
                    out Vector2Int mapCell) || mapCell == current) continue;
            if (!IsAutoplayRouteStepValid(map, current, mapCell)) return false;
            next = mapCell;
            return true;
        }
        return false;
    }

    private AutoplayRouteGeometry CalculateAutoplayRouteGeometry(
        RougeTowerDefenseMap map, Vector2Int towerCell, float attackRange,
        float aoeRadius, bool calculatePiercingLane)
    {
        AutoplayRouteGeometry geometry = default;
        int cellCount = map != null ? map.Width * map.Height : 0;
        if (cellCount <= 0 || attackRange <= 0.01f ||
            _towerDefenseAutoplayRouteCoreTrafficByCell.Length < cellCount ||
            _towerDefenseAutoplayRouteNextByCell.Length < cellCount ||
            _towerDefenseAutoplayRouteTangentByCell.Length < cellCount)
            return geometry;

        float cellSize = Mathf.Max(0.1f, map.CellSize);
        float rangeSquared = attackRange * attackRange;
        float randomReach = attackRange + Mathf.Max(0f, aoeRadius);
        float randomReachSquared = randomReach * randomReach;
        float rangeCells = attackRange / cellSize;
        float straightCrossingCells = Mathf.Max(1f, rangeCells * 2f + 1f);
        Vector3 towerWorld = map.CellCenter(towerCell);
        Vector2 towerPosition = new Vector2(towerWorld.x, towerWorld.z);
        float weightedPathCells = 0f;
        float rawPathCells = 0f;
        float routeWeight = 0f;
        float bottleneckSum = 0f;
        float maximumMerge = 0f;
        float earlySum = 0f;
        float lateSum = 0f;
        float doubleAngleX = 0f;
        float doubleAngleY = 0f;
        float directionWeight = 0f;
        float randomLaneWorld = 0f;

        for (int index = 0; index < cellCount; index++)
        {
            float coreTraffic = _towerDefenseAutoplayRouteCoreTrafficByCell[index];
            if (coreTraffic <= 0.0001f) continue;
            int x = index % map.Width;
            int y = index / map.Width;
            Vector3 routeWorld = map.CellCenter(new Vector2Int(x, y));
            Vector2 offset = new Vector2(routeWorld.x - towerPosition.x,
                routeWorld.z - towerPosition.y);
            float distanceSquared = offset.sqrMagnitude;
            float segmentCells = GetAutoplayRouteSegmentLengthCells(index,
                map.Width);
            float traffic = Mathf.Clamp01(coreTraffic /
                _towerDefenseAutoplayMaximumCoreTraffic);

            if (distanceSquared <= randomReachSquared && aoeRadius > 0.01f)
            {
                float edgeWeight = Mathf.Clamp01((randomReach -
                    Mathf.Sqrt(distanceSquared)) / Mathf.Max(aoeRadius, cellSize));
                randomLaneWorld += segmentCells * cellSize *
                    Mathf.Lerp(0.45f, 1f, traffic) *
                    Mathf.Lerp(0.35f, 1f, edgeWeight);
            }
            if (distanceSquared > rangeSquared) continue;

            float distance = Mathf.Sqrt(distanceSquared);
            float falloff = Mathf.Lerp(1f, 0.62f,
                Mathf.Clamp01(distance / attackRange));
            float weightedSegment = segmentCells * falloff *
                Mathf.Lerp(0.38f, 1f, traffic);
            weightedPathCells += weightedSegment;
            rawPathCells += segmentCells * falloff;
            routeWeight += weightedSegment;

            int predecessors = (uint)index <
                               (uint)_towerDefenseAutoplayRoutePredecessorCountByCell
                                   .Length
                ? _towerDefenseAutoplayRoutePredecessorCountByCell[index]
                : 0;
            // A tiny side route joining a dominant lane is not a full-strength
            // chokepoint. Weight the discrete predecessor signal by real core flow.
            float merge = Mathf.Clamp01((predecessors - 1f) / 2f) * traffic;
            maximumMerge = Mathf.Max(maximumMerge, merge);
            bottleneckSum += weightedSegment * (traffic * 0.38f + merge * 0.62f);

            float distanceToGoal = _towerDefenseAutoplayRouteDistanceByCell[index];
            float progress = float.IsPositiveInfinity(distanceToGoal)
                ? 0f
                : 1f - Mathf.Clamp01(distanceToGoal /
                    _towerDefenseAutoplayMaximumRouteDistance);
            earlySum += weightedSegment * (1f - progress);
            lateSum += weightedSegment * progress;

            Vector2 tangent = _towerDefenseAutoplayRouteTangentByCell[index];
            if (tangent.sqrMagnitude <= 0.0001f) continue;
            tangent.Normalize();
            float tangentWeight = segmentCells * falloff *
                Mathf.Lerp(0.72f, 1f, traffic);
            // A beam is an unoriented axis: directions 180 degrees apart are the
            // same lane, so measure coherence in doubled-angle space.
            doubleAngleX += tangentWeight *
                (tangent.x * tangent.x - tangent.y * tangent.y);
            doubleAngleY += tangentWeight * 2f * tangent.x * tangent.y;
            directionWeight += tangentWeight;
        }

        if (routeWeight <= 0.0001f) return geometry;
        float pathRatio = weightedPathCells / straightCrossingCells;
        float rawPathRatio = rawPathCells / straightCrossingCells;
        geometry.PathDwell = Mathf.Clamp01(pathRatio);
        geometry.Reuse = Mathf.Clamp01((rawPathRatio - 0.72f) * 0.78f +
            weightedPathCells / Mathf.Max(0.001f, rawPathCells) * 0.24f +
            maximumMerge * 0.42f);
        geometry.Bottleneck = Mathf.Clamp01(
            bottleneckSum / routeWeight * 0.82f + maximumMerge * 0.38f);
        geometry.DirectionConsistency = directionWeight > 0.0001f
            ? Mathf.Clamp01(Mathf.Sqrt(doubleAngleX * doubleAngleX +
                doubleAngleY * doubleAngleY) / directionWeight)
            : 0f;
        if (calculatePiercingLane)
            geometry.PiercingLane = CalculateAutoplayPiercingLaneScore(map,
                towerPosition, attackRange);
        geometry.EarlyExposure = geometry.PathDwell *
            Mathf.Clamp01(earlySum / routeWeight);
        geometry.LateExposure = geometry.PathDwell *
            Mathf.Clamp01(lateSum / routeWeight);

        if (aoeRadius > 0.01f && randomLaneWorld > 0.001f)
        {
            float hitRadius = Mathf.Max(cellSize * 0.26f, aoeRadius);
            float corridorArea = randomLaneWorld * hitRadius * 2f +
                                 Mathf.PI * hitRadius * hitRadius;
            float landingArea = Mathf.PI * attackRange * attackRange;
            geometry.RandomAreaHitChance = Mathf.Clamp01(1f -
                Mathf.Exp(-corridorArea / Mathf.Max(0.01f, landingArea)));
        }
        return geometry;
    }

    private float CalculateAutoplayPiercingLaneScore(
        RougeTowerDefenseMap map, Vector2 towerPosition, float attackRange)
    {
        int cellCount = map.Width * map.Height;
        int downstreamIndex = -1;
        int busiestIndex = -1;
        int nearestIndex = -1;
        float downstreamDistance = float.PositiveInfinity;
        float busiestTraffic = 0f;
        float nearestDistance = float.PositiveInfinity;
        for (int targetIndex = 0; targetIndex < cellCount; targetIndex++)
        {
            float coreTraffic =
                _towerDefenseAutoplayRouteCoreTrafficByCell[targetIndex];
            if (coreTraffic <= 0.0001f) continue;
            Vector2 targetPosition = GetAutoplayRouteWorldPosition(map,
                targetIndex);
            float targetDistance = Vector2.Distance(targetPosition,
                towerPosition);
            if (targetDistance <= 0.001f || targetDistance > attackRange) continue;
            float routeDistance = _towerDefenseAutoplayRouteDistanceByCell[
                targetIndex];
            if (routeDistance < downstreamDistance)
            {
                downstreamDistance = routeDistance;
                downstreamIndex = targetIndex;
            }
            if (coreTraffic > busiestTraffic)
            {
                busiestTraffic = coreTraffic;
                busiestIndex = targetIndex;
            }
            if (targetDistance < nearestDistance)
            {
                nearestDistance = targetDistance;
                nearestIndex = targetIndex;
            }
        }

        // The tower aims at an actual target, not at an arbitrary mathematically
        // perfect route cell. Three plausible anchors cover its downstream-first
        // targeting, the dominant lane, and the nearest available target while
        // reducing the old build-cell × route² topology rebuild to O(build × route).
        float maximumScore = ScoreAutoplayPiercingAnchor(map, towerPosition,
            attackRange, downstreamIndex);
        if (busiestIndex != downstreamIndex)
            maximumScore = Mathf.Max(maximumScore,
                ScoreAutoplayPiercingAnchor(map, towerPosition, attackRange,
                    busiestIndex));
        if (nearestIndex != downstreamIndex && nearestIndex != busiestIndex)
            maximumScore = Mathf.Max(maximumScore,
                ScoreAutoplayPiercingAnchor(map, towerPosition, attackRange,
                    nearestIndex));

        float idealLaneCells = Mathf.Max(1f, attackRange * 2f /
            Mathf.Max(0.1f, map.CellSize));
        return Mathf.Clamp01(1f - Mathf.Exp(-maximumScore /
            (idealLaneCells * 0.72f)));
    }

    private float ScoreAutoplayPiercingAnchor(RougeTowerDefenseMap map,
        Vector2 towerPosition, float attackRange, int targetIndex)
    {
        int cellCount = map.Width * map.Height;
        if ((uint)targetIndex >= (uint)cellCount) return 0f;
        Vector2 direction = GetAutoplayRouteWorldPosition(map, targetIndex) -
                            towerPosition;
        float targetDistance = direction.magnitude;
        if (targetDistance <= 0.001f || targetDistance > attackRange) return 0f;
        direction /= targetDistance;
        float beamLength = attackRange * 2f;
        float beamRadius = Mathf.Max(map.CellSize * 0.28f,
            PiercingLaserBeamRadius);
        float score = 0f;
        for (int routeIndex = 0; routeIndex < cellCount; routeIndex++)
        {
            float coreTraffic =
                _towerDefenseAutoplayRouteCoreTrafficByCell[routeIndex];
            if (coreTraffic <= 0.0001f) continue;
            Vector2 offset = GetAutoplayRouteWorldPosition(map, routeIndex) -
                             towerPosition;
            float forward = Vector2.Dot(offset, direction);
            if (forward < 0f || forward > beamLength) continue;
            float lateral = Mathf.Abs(offset.x * direction.y -
                                      offset.y * direction.x);
            if (lateral > beamRadius) continue;

            Vector2 tangent =
                _towerDefenseAutoplayRouteTangentByCell[routeIndex];
            float alignment = tangent.sqrMagnitude > 0.0001f
                ? Mathf.Abs(Vector2.Dot(tangent.normalized, direction))
                : 1f;
            float traffic = Mathf.Clamp01(coreTraffic /
                _towerDefenseAutoplayMaximumCoreTraffic);
            float lateralFalloff = Mathf.Lerp(1f, 0.32f,
                Mathf.Clamp01(lateral / beamRadius));
            float forwardFalloff = Mathf.Lerp(1f, 0.72f,
                Mathf.Clamp01(forward / beamLength));
            score += GetAutoplayRouteSegmentLengthCells(routeIndex,
                map.Width) * Mathf.Lerp(0.42f, 1f, traffic) *
                Mathf.Lerp(0.48f, 1f, alignment) * lateralFalloff *
                forwardFalloff;
        }
        return score;
    }

    private Vector2 GetAutoplayRouteWorldPosition(RougeTowerDefenseMap map,
        int index)
    {
        Vector3 center = map.CellCenter(new Vector2Int(index % map.Width,
            index / map.Width));
        return new Vector2(center.x, center.z);
    }

    private float GetAutoplayRouteSegmentLengthCells(int index, int width)
    {
        int nextIndex = (uint)index <
            (uint)_towerDefenseAutoplayRouteNextByCell.Length
                ? _towerDefenseAutoplayRouteNextByCell[index]
                : -1;
        if ((uint)nextIndex >= (uint)_towerDefenseAutoplayRouteNextByCell.Length ||
            nextIndex == index) return 1f;
        int dx = nextIndex % width - index % width;
        int dy = nextIndex / width - index / width;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static float ScoreAutoplayRouteGeometry(RougeTowerType type,
        AutoplayRouteGeometry geometry, out float finisherFit,
        out float setupFit)
    {
        float bend = (1f - geometry.DirectionConsistency) *
                     Mathf.Lerp(0.35f, 1f, geometry.PathDwell);
        finisherFit = 0f;
        setupFit = 0f;
        switch (type)
        {
            case RougeTowerType.MachineGun:
                finisherFit = geometry.LateExposure;
                setupFit = geometry.EarlyExposure * 0.32f;
                return geometry.PathDwell * 26f + geometry.Reuse * 14f +
                       geometry.Bottleneck * 30f + finisherFit * 72f;
            case RougeTowerType.Laser:
                finisherFit = geometry.LateExposure * 0.92f;
                setupFit = geometry.EarlyExposure * 0.46f;
                return geometry.PathDwell * 30f + geometry.Reuse * 20f +
                       geometry.Bottleneck * 34f + finisherFit * 66f;
            case RougeTowerType.Ice:
                finisherFit = geometry.LateExposure * 0.38f;
                setupFit = geometry.EarlyExposure;
                return geometry.PathDwell * 42f + geometry.Reuse * 58f +
                       geometry.Bottleneck * 60f + bend * 38f + setupFit * 24f;
            case RougeTowerType.Cannon:
                finisherFit = geometry.LateExposure * 0.22f;
                setupFit = geometry.EarlyExposure * 0.72f;
                return geometry.PathDwell * 45f + geometry.Reuse * 64f +
                       geometry.Bottleneck * 45f + bend * 22f + setupFit * 20f;
            case RougeTowerType.Flame:
                finisherFit = geometry.LateExposure * 0.28f;
                setupFit = geometry.EarlyExposure;
                return geometry.PathDwell * 50f + geometry.Reuse * 58f +
                       geometry.Bottleneck * 44f + bend * 28f + setupFit * 46f;
            case RougeTowerType.OrbitSphere:
                finisherFit = geometry.LateExposure * 0.35f;
                setupFit = geometry.EarlyExposure * 0.78f;
                return geometry.PathDwell * 52f + geometry.Reuse * 72f +
                       geometry.Bottleneck * 52f + bend * 34f;
            case RougeTowerType.PiercingLaser:
                finisherFit = geometry.LateExposure * 0.48f;
                setupFit = geometry.EarlyExposure * 0.62f;
                return geometry.PiercingLane * 142f +
                       geometry.DirectionConsistency * 28f +
                       geometry.PathDwell * 22f;
            case RougeTowerType.RocketBarrage:
                finisherFit = geometry.LateExposure * 0.18f;
                setupFit = geometry.EarlyExposure * 0.82f;
                return geometry.RandomAreaHitChance * 145f +
                       geometry.Reuse * 42f + geometry.Bottleneck * 32f;
            default:
                return geometry.PathDwell * 35f + geometry.Reuse * 25f;
        }
    }

    private void RebuildTowerDefenseAutoplayBuildPriors(RougeTowerDefenseMap map)
    {
        int cellCount = map.Width * map.Height;
        int priorCount = cellCount * TowerDefenseVisuals.StandardTowerTypeCount;
        if (_towerDefenseAutoplayBuildPriors == null ||
            _towerDefenseAutoplayBuildPriors.Length < priorCount)
            _towerDefenseAutoplayBuildPriors = new AutoplayBuildPrior[priorCount];
        Array.Clear(_towerDefenseAutoplayBuildPriors, 0, priorCount);

        for (int typeIndex = 0;
             typeIndex < TowerDefenseVisuals.StandardTowerTypeCount; typeIndex++)
        {
            RougeTowerType type = (RougeTowerType)typeIndex;
            TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out _,
                out int originalCost);
            int paidCost = GetTowerDefenseAutoplayPaidCost(originalCost);
            for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                if (!_towerDefenseAutoplayBuildableTopology[cellIndex]) continue;
                int x = cellIndex % map.Width;
                int y = cellIndex / map.Width;
                Vector2Int cell = new Vector2Int(x, y);
                RougeTowerPlaceEffect effect =
                    _towerDefenseAutoplayEffectiveEffects[cellIndex];
                int startingLevel = Mathf.Clamp(1 +
                    RougeTowerPlaceEffectRules.GetInitialLevelBonus(effect), 1,
                    TowerDefenseVisuals.MaxTowerLevel);
                RougeTowerStats stats = TowerDefenseVisuals.GetStats(type,
                    startingLevel);
                RougeTowerBuffLevels buffs =
                    RougeTowerPlaceEffectRules.GetBuffLevels(effect);
                float attackRange = stats.AttackRadius *
                    RougeTowerBuffMath.GetMultiplier(buffs.Range);
                float groundCoverage = GetAutoplayGroundCoverageValue(map, cell,
                    attackRange);
                AutoplayRouteGeometry geometry =
                    CalculateAutoplayRouteGeometry(map, cell, attackRange,
                        stats.AoeRadius,
                        type == RougeTowerType.PiercingLaser);
                float rawGeometryScore = ScoreAutoplayRouteGeometry(type,
                    geometry, out float finisherFit, out float setupFit);
                // Keep the established 0..60 scale, but preserve ordering among the
                // many good U-bends, chokepoints and beam lanes that used to hard-cap
                // at exactly 60 and become indistinguishable.
                float geometryScore = Mathf.Min(60f, rawGeometryScore) +
                    20f * (1f - Mathf.Exp(-Mathf.Max(0f,
                        rawGeometryScore - 60f) / 45f));
                float singleTargetRealization = 1f;
                if (type == RougeTowerType.RocketBarrage)
                {
                    float ratio = stats.AoeRadius /
                                  Mathf.Max(0.1f, attackRange);
                    singleTargetRealization = Mathf.Clamp01(ratio * ratio);
                }
                else if (type == RougeTowerType.OrbitSphere)
                {
                    float representativeRadius = Mathf.Max(
                        stats.OrbitSphereRadius,
                        attackRange * 0.65f);
                    float halfAngle = Mathf.Asin(Mathf.Clamp01(
                        stats.OrbitSphereRadius /
                        Mathf.Max(0.1f, representativeRadius)));
                    singleTargetRealization = Mathf.Clamp01(
                        Mathf.Max(1, stats.ProjectileCount) * halfAngle /
                        Mathf.PI);
                }
                float tileScore = GetAutoplayTileAffinity(type, effect);
                float coverageScore = Mathf.Sqrt(Mathf.Max(0f, groundCoverage)) * 25f;
                float opportunityPenalty = GetAutoplayOpportunityPenalty(type, effect);
                float combatPower = EstimateAutoplayCombatPower(type, stats, buffs);
                float singleTargetPower = IsAutoplayBossDamageTower(type)
                    ? EstimateAutoplaySingleTargetPower(type, stats, buffs)
                    : 0f;
                float bossRouteCoverage = GetAutoplayBossRouteCoverage(map,
                    map.CellCenter(cell), attackRange);
                float powerScore = Mathf.Log(1f + combatPower) * 24f;
                float priorityTileBonus = GetAutoplayPriorityTileBonus(tileScore);
                float fixedScore = Mathf.Max(1f, 45f + tileScore * 1.35f +
                    priorityTileBonus + coverageScore + powerScore +
                    geometryScore -
                    opportunityPenalty);
                _towerDefenseAutoplayBuildPriors[typeIndex * cellCount + cellIndex] =
                    new AutoplayBuildPrior
                    {
                        IsValid = true,
                        PlaceEffect = effect,
                        OriginalCost = originalCost,
                        PaidCost = paidCost,
                        AttackRange = attackRange,
                        CombatPower = combatPower,
                        FixedScore = fixedScore,
                        TileScore = tileScore,
                        CoverageScore = coverageScore,
                        SingleTargetPower = singleTargetPower,
                        BossRouteCoverage = bossRouteCoverage,
                        OpportunityPenalty = opportunityPenalty,
                        GeometryScore = geometryScore,
                        PathDwell = geometry.PathDwell,
                        RouteReuse = geometry.Reuse,
                        Bottleneck = geometry.Bottleneck,
                        DirectionConsistency = geometry.DirectionConsistency,
                        PiercingLaneScore = geometry.PiercingLane,
                        RandomAreaHitChance = geometry.RandomAreaHitChance,
                        SingleTargetRealization = singleTargetRealization,
                        EarlyRouteExposure = geometry.EarlyExposure,
                        LateRouteExposure = geometry.LateExposure,
                        FinisherFit = finisherFit,
                        SetupFit = setupFit
                    };
            }
        }
    }

    private void RebuildTowerDefenseAutoplayUpgradePriors()
    {
        int levelStride = TowerDefenseVisuals.MaxTowerLevel + 1;
        int priorCount = TowerDefenseVisuals.StandardTowerTypeCount * levelStride;
        if (_towerDefenseAutoplayUpgradeGrowthPriors == null ||
            _towerDefenseAutoplayUpgradeGrowthPriors.Length < priorCount)
            _towerDefenseAutoplayUpgradeGrowthPriors = new float[priorCount];
        if (_towerDefenseAutoplayUpgradeAbsoluteGainPriors == null ||
            _towerDefenseAutoplayUpgradeAbsoluteGainPriors.Length < priorCount)
            _towerDefenseAutoplayUpgradeAbsoluteGainPriors =
                new float[priorCount];
        if (_towerDefenseAutoplayUpgradeRangePriors == null ||
            _towerDefenseAutoplayUpgradeRangePriors.Length < priorCount)
            _towerDefenseAutoplayUpgradeRangePriors = new float[priorCount];
        Array.Clear(_towerDefenseAutoplayUpgradeGrowthPriors, 0, priorCount);
        Array.Clear(_towerDefenseAutoplayUpgradeAbsoluteGainPriors, 0,
            priorCount);
        Array.Clear(_towerDefenseAutoplayUpgradeRangePriors, 0, priorCount);

        for (int typeIndex = 0;
             typeIndex < TowerDefenseVisuals.StandardTowerTypeCount; typeIndex++)
        for (int level = 1; level < TowerDefenseVisuals.MaxTowerLevel; level++)
        {
            RougeTowerType type = (RougeTowerType)typeIndex;
            RougeTowerStats currentStats = TowerDefenseVisuals.GetStats(type, level);
            RougeTowerStats nextStats = TowerDefenseVisuals.GetStats(type, level + 1);
            float currentPower = Mathf.Max(0.01f,
                EstimateAutoplayCombatPower(type, currentStats, default));
            float nextPower = Mathf.Max(currentPower,
                EstimateAutoplayCombatPower(type, nextStats, default));
            float currentRawPower = Mathf.Max(0.01f,
                EstimateAutoplayCombatPower(type, currentStats, default, false));
            float nextRawPower = Mathf.Max(currentRawPower,
                EstimateAutoplayCombatPower(type, nextStats, default, false));
            // Ice has a fixed strategic-control value in the composite estimate. It
            // must not dilute the actual damage gained by an upgrade; branch/control
            // value is added separately when the concrete tower is scored.
            float growthRatio = Mathf.Clamp(
                Mathf.Max(nextPower / currentPower,
                    nextRawPower / currentRawPower) - 1f, 0f, 4f);
            float rangeRatio = currentStats.AttackRadius > 0.01f
                ? Mathf.Max(0f,
                    nextStats.AttackRadius / currentStats.AttackRadius - 1f)
                : 0f;
            int priorIndex = typeIndex * levelStride + level;
            _towerDefenseAutoplayUpgradeGrowthPriors[priorIndex] =
                55f + growthRatio * 210f + rangeRatio * 100f;
            _towerDefenseAutoplayUpgradeAbsoluteGainPriors[priorIndex] =
                Mathf.Max(nextPower - currentPower,
                    nextRawPower - currentRawPower);
            _towerDefenseAutoplayUpgradeRangePriors[priorIndex] = rangeRatio;
        }
        // Branch-specific combat formulas are intentionally not expanded into a
        // branch×cell matrix in v1. Their small explainable bonus is layered at runtime.
    }

    private float GetAutoplayGroundCoverageValue(RougeTowerDefenseMap map,
        Vector2Int towerCell, float attackRange)
    {
        float coverage = 0f;
        if (map == null || attackRange <= 0f) return coverage;
        VisitAutoplayGroundCoverageCells(map, towerCell, attackRange,
            ref coverage);
        return coverage;
    }

    private void EvaluateAutoplayBuildChoices(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot, bool emergencyRecoveryBuildWindow,
        out AutoplayBuildChoice bestOverall,
        out AutoplayBuildChoice bestAffordable,
        out AutoplayBuildChoice fastestDefensive)
    {
        bestOverall = default;
        bestAffordable = default;
        fastestDefensive = default;
        _towerDefenseAutoplayBuildChoiceScratch.Clear();
        if (map == null) return;
        bool missingFunctionGroup = HasMissingEnabledAutoplayFunctionGroup();

        for (int orderOffset = 0; orderOffset < TowerDefenseAutoplayBuildOrder.Length;
             orderOffset++)
        {
            int orderIndex = (_towerDefenseAutoplayBuildCursor + orderOffset) %
                             TowerDefenseAutoplayBuildOrder.Length;
            RougeTowerType type = TowerDefenseAutoplayBuildOrder[orderIndex];
            if (IsTowerTypeDisabled(type)) continue;
            float bestOpenTileAffinity = GetBestOpenAutoplayTileAffinity(map, type);

            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                int cellIndex = y * map.Width + x;
                if (!_towerDefenseAutoplayBuildableTopology[cellIndex] ||
                    _towerDefenseAutoplayOccupiedCells[cellIndex]) continue;
                AutoplayBuildPrior prior = _towerDefenseAutoplayBuildPriors[
                    (int)type * map.Width * map.Height + cellIndex];
                if (!prior.IsValid) continue;
                AutoplayBuildChoice choice = ScoreAutoplayBuildChoice(map, snapshot,
                    type, orderIndex, cell, prior, bestOpenTileAffinity);
                if (IsBetterAutoplayEmergencyBuild(choice, fastestDefensive,
                        snapshot, missingFunctionGroup))
                    fastestDefensive = choice;
                _towerDefenseAutoplayBuildChoiceScratch.Add(choice);
            }
        }
        ScoreAutoplayBuildCapitalChoices(out AutoplayBuildChoice objectiveOverall,
            out AutoplayBuildChoice objectiveAffordable,
            out AutoplayBuildChoice personalityOverall,
            out AutoplayBuildChoice personalityAffordable);
        float regretBudget = GetAutoplayPersonalityRegretBudget(snapshot);
        bestOverall = SelectAutoplayCommanderWeightedBuildChoice(
            objectiveOverall, personalityOverall, regretBudget);
        bestAffordable = SelectAutoplayCommanderWeightedBuildChoice(
            objectiveAffordable, personalityAffordable, regretBudget);
    }

    private void ScoreAutoplayBuildCapitalChoices(
        out AutoplayBuildChoice objectiveOverall,
        out AutoplayBuildChoice objectiveAffordable,
        out AutoplayBuildChoice personalityOverall,
        out AutoplayBuildChoice personalityAffordable)
    {
        objectiveOverall = default;
        objectiveAffordable = default;
        personalityOverall = default;
        personalityAffordable = default;
        float maximumObjectiveEfficiency = 0f;
        float maximumObjectiveUtility = 0f;
        float maximumStyledEfficiency = 0f;
        float maximumStyledUtility = 0f;
        int minimumPositiveCost = int.MaxValue;
        for (int i = 0; i < _towerDefenseAutoplayBuildChoiceScratch.Count; i++)
        {
            AutoplayBuildChoice choice =
                _towerDefenseAutoplayBuildChoiceScratch[i];
            maximumObjectiveEfficiency = Mathf.Max(maximumObjectiveEfficiency,
                choice.ObjectiveEfficiency);
            maximumObjectiveUtility = Mathf.Max(maximumObjectiveUtility,
                choice.ObjectiveUtility);
            maximumStyledEfficiency = Mathf.Max(maximumStyledEfficiency,
                choice.Efficiency);
            maximumStyledUtility = Mathf.Max(maximumStyledUtility,
                choice.Utility);
            if (choice.PaidCost > 0)
                minimumPositiveCost = Mathf.Min(minimumPositiveCost,
                    choice.PaidCost);
        }

        int referenceCost = minimumPositiveCost == int.MaxValue
            ? 0
            : minimumPositiveCost * 2;
        float wealth = GetAutoplayCapitalWealth(_towerDefenseGold, referenceCost);
        for (int i = 0; i < _towerDefenseAutoplayBuildChoiceScratch.Count; i++)
        {
            AutoplayBuildChoice choice =
                _towerDefenseAutoplayBuildChoiceScratch[i];
            choice.ObjectiveCapitalScore = GetAutoplayNormalizedCapitalScore(
                choice.ObjectiveEfficiency, choice.ObjectiveUtility,
                maximumObjectiveEfficiency, maximumObjectiveUtility, wealth);
            choice.CapitalScore = GetAutoplayNormalizedCapitalScore(
                choice.Efficiency, choice.Utility, maximumStyledEfficiency,
                maximumStyledUtility, wealth);
            _towerDefenseAutoplayBuildChoiceScratch[i] = choice;

            if (!objectiveOverall.IsValid || choice.ObjectiveCapitalScore >
                objectiveOverall.ObjectiveCapitalScore)
                objectiveOverall = choice;
            if (!personalityOverall.IsValid || choice.CapitalScore >
                personalityOverall.CapitalScore)
                personalityOverall = choice;
            if (choice.PaidCost > _towerDefenseGold) continue;
            if (!objectiveAffordable.IsValid || choice.ObjectiveCapitalScore >
                objectiveAffordable.ObjectiveCapitalScore)
                objectiveAffordable = choice;
            if (!personalityAffordable.IsValid || choice.CapitalScore >
                personalityAffordable.CapitalScore)
                personalityAffordable = choice;
        }
    }

    private AutoplayBuildChoice SelectAutoplayBossRouteBuild(
        AutoplayBattleSnapshot snapshot, bool missingFunctionGroup)
    {
        AutoplayBuildChoice best = default;
        float bestGain = float.NegativeInfinity;
        for (int i = 0; i < _towerDefenseAutoplayBuildChoiceScratch.Count; i++)
        {
            AutoplayBuildChoice choice =
                _towerDefenseAutoplayBuildChoiceScratch[i];
            if (!choice.IsValid || choice.BossRouteCoverage <= 0.001f) continue;
            bool closesPowerGap = _towerDefenseAutoplayBossPowerDeficit > 0.05f &&
                                  IsAutoplayBossDamageTower(choice.Type);
            bool closesControlGap = _towerDefenseAutoplayBossControlDeficit > 0.05f &&
                                    IsAutoplayControlTower(choice.Type);
            if (!closesPowerGap && !closesControlGap) continue;
            float gain = GetAutoplayBuildCapitalGain(choice, snapshot,
                missingFunctionGroup);
            if (gain < bestGain || Mathf.Approximately(gain, bestGain) &&
                choice.BossRouteCoverage <= best.BossRouteCoverage) continue;
            bestGain = gain;
            best = choice;
        }
        if (best.IsValid)
        {
            // Boss preparation is an objective combat budget. Personality remains in
            // the score itself, but cannot swap the one admitted route build for a
            // cheaper unrelated placement after the market gate has selected it.
            best.Utility = best.ObjectiveUtility;
            best.Efficiency = best.ObjectiveEfficiency;
            best.CapitalScore = best.ObjectiveCapitalScore;
        }
        return best;
    }

    private static float GetAutoplayCapitalWealth(int spendableGold,
        int referenceCost)
    {
        if (referenceCost <= 0) return 1f;
        float progress = Mathf.InverseLerp(Mathf.Max(100, referenceCost),
            Mathf.Max(400, referenceCost * 4f), Mathf.Max(0, spendableGold));
        int[] spectrum = RougeCommanderTacticalSpectrum.Calculate(
            TowerDefenseAutoplayCommander);
        float expansionLean = spectrum != null && spectrum.Length > 3
            ? (spectrum[3] - spectrum[0]) / 50f
            : 0f;
        // Builders lean toward absolute board gain; savers lean toward ROI. This is a
        // continuous comparison, not permission to build or an instruction to hold.
        return Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, progress) +
                             expansionLean * 0.12f);
    }

    private static float GetAutoplayNormalizedCapitalScore(float efficiency,
        float utility, float maximumEfficiency, float maximumUtility,
        float wealth)
    {
        if (!float.IsFinite(efficiency)) efficiency = 0f;
        if (!float.IsFinite(utility)) utility = 0f;
        if (!float.IsFinite(maximumEfficiency) || maximumEfficiency < 0f)
            maximumEfficiency = 0f;
        if (!float.IsFinite(maximumUtility) || maximumUtility < 0f)
            maximumUtility = 0f;
        if (!float.IsFinite(wealth)) wealth = 0f;
        float normalizedEfficiency = maximumEfficiency > 0.0001f
            ? Mathf.Max(0f, efficiency) / maximumEfficiency
            : 0f;
        float normalizedUtility = maximumUtility > 0.0001f
            ? Mathf.Max(0f, utility) / maximumUtility
            : 0f;
        return Mathf.Lerp(normalizedEfficiency, normalizedUtility,
            Mathf.Clamp01(wealth));
    }

    private bool IsBetterAutoplayEmergencyBuild(AutoplayBuildChoice candidate,
        AutoplayBuildChoice current, AutoplayBattleSnapshot snapshot,
        bool missingFunctionGroup)
    {
        if (!candidate.IsValid) return false;
        if (!current.IsValid) return true;
        bool candidateCoversHeat = candidate.NearBaseHeatCoverage >=
                                   TowerDefenseAutoplayNearBaseEarlyWarning;
        bool currentCoversHeat = current.NearBaseHeatCoverage >=
                                 TowerDefenseAutoplayNearBaseEarlyWarning;
        if (candidateCoversHeat != currentCoversHeat) return candidateCoversHeat;
        bool candidateDefensive = candidateCoversHeat ||
                                  candidate.DominantPressureLayer ==
                                  AutoplayPressureLayer.Urgent;
        bool currentDefensive = currentCoversHeat ||
                                current.DominantPressureLayer ==
                                AutoplayPressureLayer.Urgent;
        if (candidateDefensive != currentDefensive) return candidateDefensive;

        bool candidateAffordable = candidate.PaidCost <= _towerDefenseGold;
        bool currentAffordable = current.PaidCost <= _towerDefenseGold;
        if (candidateAffordable != currentAffordable) return candidateAffordable;

        float candidateScore = GetAutoplayEmergencyPurchaseScore(
            GetAutoplayBuildCapitalGain(candidate, snapshot,
                missingFunctionGroup), candidate.PaidCost);
        float currentScore = GetAutoplayEmergencyPurchaseScore(
            GetAutoplayBuildCapitalGain(current, snapshot,
                missingFunctionGroup), current.PaidCost);
        if (!Mathf.Approximately(candidateScore, currentScore))
            return candidateScore > currentScore;
        return candidate.PaidCost < current.PaidCost;
    }

    private static AutoplayBuildChoice SelectAutoplayCommanderWeightedBuildChoice(
        AutoplayBuildChoice objective, AutoplayBuildChoice personality,
        float regretBudget)
    {
        // Personality already nudges CapitalScore continuously. It may resolve a
        // close comparison, but it must never remove control or damage candidates via
        // a binary style roll.
        const float maximumStyleRegret = 0.08f;
        return SelectAutoplayPersonalityBuildChoice(objective, personality,
            Mathf.Min(Mathf.Clamp01(regretBudget), maximumStyleRegret));
    }

    private AutoplayBuildChoice SelectAutoplayImmediateCoreFirepowerBuild(
        RougeTowerDefenseMap map, AutoplayBattleSnapshot snapshot,
        bool missingFunctionGroup, out float bestScore)
    {
        bestScore = float.NegativeInfinity;
        AutoplayBuildChoice best = default;
        if (map == null || _towerDefenseAutoplayImmediateCoreThreatCellIndex < 0)
            return best;
        int cellCount = map.Width * map.Height;
        if ((uint)_towerDefenseAutoplayImmediateCoreThreatCellIndex >=
            (uint)cellCount) return best;
        Vector2Int threatCell = new Vector2Int(
            _towerDefenseAutoplayImmediateCoreThreatCellIndex % map.Width,
            _towerDefenseAutoplayImmediateCoreThreatCellIndex / map.Width);

        for (int i = 0; i < _towerDefenseAutoplayBuildChoiceScratch.Count; i++)
        {
            AutoplayBuildChoice choice =
                _towerDefenseAutoplayBuildChoiceScratch[i];
            if (!choice.IsValid || choice.PaidCost > _towerDefenseGold ||
                !DoesAutoplayBuildCoverCell(map, choice, threatCell))
                continue;
            RougeTowerAiRoleProfile role = GetAutoplayTowerRoleProfile(
                choice.Type);
            float directPower = role != null ? role.directDamage : 0f;
            if (directPower < 0.35f) continue;
            float capitalGain = GetAutoplayBuildCapitalGain(choice, snapshot,
                missingFunctionGroup);
            float score = capitalGain * Mathf.Lerp(0.45f, 1f, directPower) +
                Mathf.Sqrt(Mathf.Max(0f, choice.MarginalPower)) *
                directPower * 48f + choice.NearBaseHeatCoverage * 220f;
            if (best.IsValid && score <= bestScore) continue;
            bestScore = score;
            best = choice;
        }
        return best;
    }

    private AutoplayUpgradeChoice SelectAutoplayImmediateCoreFirepowerUpgrade(
        RougeTowerDefenseMap map, out float bestScore)
    {
        bestScore = float.NegativeInfinity;
        AutoplayUpgradeChoice best = default;
        if (map == null || _towerDefenseAutoplayImmediateCoreThreatCellIndex < 0)
            return best;
        int cellCount = map.Width * map.Height;
        if ((uint)_towerDefenseAutoplayImmediateCoreThreatCellIndex >=
            (uint)cellCount) return best;
        Vector2Int threatCell = new Vector2Int(
            _towerDefenseAutoplayImmediateCoreThreatCellIndex % map.Width,
            _towerDefenseAutoplayImmediateCoreThreatCellIndex / map.Width);

        for (int i = 0; i < _towerDefenseAutoplayUpgradeChoiceScratch.Count; i++)
        {
            AutoplayUpgradeChoice choice =
                _towerDefenseAutoplayUpgradeChoiceScratch[i];
            if (!choice.IsValid || choice.PaidCost > _towerDefenseGold ||
                !DoesAutoplayTowerCoverCell(map, choice.Tower, threatCell))
                continue;
            RougeTowerAiRoleProfile role = GetAutoplayUpgradeRoleProfile(
                choice.Tower, choice.SpecializationChoiceIndex);
            float directPower = role != null ? role.directDamage : 0f;
            if (directPower < 0.35f) continue;
            float capitalGain = GetAutoplayUpgradeCapitalGain(choice);
            float score = capitalGain * Mathf.Lerp(0.45f, 1f, directPower) +
                Mathf.Sqrt(Mathf.Max(0f, choice.MarginalPower)) *
                directPower * 48f + choice.NearBaseHeatCoverage * 220f;
            if (best.IsValid && score <= bestScore) continue;
            bestScore = score;
            best = choice;
        }
        return best;
    }

    private static bool DoesAutoplayBuildCoverCell(RougeTowerDefenseMap map,
        AutoplayBuildChoice choice, Vector2Int cell)
    {
        if (map == null || !choice.IsValid) return false;
        RougeTowerStats stats = TowerDefenseVisuals.GetStats(choice.Type, 1);
        if (stats.AttackRadius <= 0f) return false;
        Vector3 delta = map.CellCenter(choice.Cell) - map.CellCenter(cell);
        delta.y = 0f;
        return delta.sqrMagnitude <= stats.AttackRadius * stats.AttackRadius;
    }

    private bool ShouldAutoplayStyleHoldContingencyReserve()
    {
        EnsureAutoplayStyleDecisionRolls();
        int[] spectrum = RougeCommanderTacticalSpectrum.Calculate(
            TowerDefenseAutoplayCommander);
        return RollAutoplayStyleFirstChoice(spectrum, 0, 3,
            _towerDefenseAutoplayStyleSaveRatioScale,
            GetAutoplayStyleDecisionRoll(0));
    }

    private float GetAutoplayStyleContingencyHoldSeconds()
    {
        EnsureAutoplayStyleDecisionRolls();
        int[] spectrum = RougeCommanderTacticalSpectrum.Calculate(
            TowerDefenseAutoplayCommander);
        float saveProbability = GetAdjustedAutoplayStyleFirstProbability(
            spectrum, 0, 3, _towerDefenseAutoplayStyleSaveRatioScale);
        return Mathf.Lerp(3.5f, 8f, saveProbability);
    }

    private static bool RollAutoplayStyleFirstChoice(int[] spectrum,
        int firstIndex, int secondIndex, float firstRatioScale, uint roll)
    {
        float probability = GetAdjustedAutoplayStyleFirstProbability(spectrum,
            firstIndex, secondIndex, firstRatioScale);
        float unitRoll = (roll & 0x00ffffffu) / 16777216f;
        return unitRoll < probability;
    }

    private static float GetAdjustedAutoplayStyleFirstProbability(
        int[] spectrum, int firstIndex, int secondIndex, float firstRatioScale)
    {
        if (spectrum == null || (uint)firstIndex >= (uint)spectrum.Length ||
            (uint)secondIndex >= (uint)spectrum.Length) return 0.5f;
        int firstWeight = Mathf.Max(0, spectrum[firstIndex]);
        int secondWeight = Mathf.Max(0, spectrum[secondIndex]);
        int totalWeight = firstWeight + secondWeight;
        if (totalWeight <= 0) return 0.5f;

        // Per-match variation changes the configured side by at most +/-10%
        // relative. Example: 32:18 => 64%; the live profile stays 57.6%-70.4%.
        float baseProbability = firstWeight / (float)totalWeight;
        return Mathf.Clamp01(baseProbability * Mathf.Clamp(firstRatioScale,
            TowerDefenseAutoplayStyleRatioMinimum,
            TowerDefenseAutoplayStyleRatioMaximum));
    }

    private uint GetAutoplayStyleDecisionRoll(int channel)
    {
        EnsureAutoplayStyleDecisionRolls();
        if (channel == 0) return _towerDefenseAutoplayStyleSaveRoll;
        if (channel == 1) return _towerDefenseAutoplayStyleControlRoll;
        return _towerDefenseAutoplayStyleRoleRoll;
    }

    private void EnsureAutoplayStyleDecisionRolls()
    {
        if (_towerDefenseAutoplayStyleRandom == null)
        {
            string commanderId = TowerDefenseAutoplayCommander.CommanderId ??
                                 string.Empty;
            int commanderHash = 17;
            unchecked
            {
                for (int i = 0; i < commanderId.Length; i++)
                    commanderHash = commanderHash * 31 + commanderId[i];
                long ticks = DateTime.UtcNow.Ticks;
                int seed = Environment.TickCount * 397 ^ GetInstanceID() * 7919 ^
                           commanderHash * 104729 ^ (int)ticks ^
                           (int)(ticks >> 32);
                _towerDefenseAutoplayStyleRandom = new System.Random(seed);
            }
            _towerDefenseAutoplayStyleSaveRatioScale =
                NextAutoplayStyleRatioScale();
            _towerDefenseAutoplayStyleControlRatioScale =
                NextAutoplayStyleRatioScale();
            _towerDefenseAutoplayStyleRoleRatioScale =
                NextAutoplayStyleRatioScale();
        }
        if (_towerDefenseAutoplayStyleRollSequence ==
            _towerDefenseAutoplayStyleDecisionSequence) return;
        _towerDefenseAutoplayStyleRollSequence =
            _towerDefenseAutoplayStyleDecisionSequence;
        _towerDefenseAutoplayStyleSaveRoll = NextAutoplayStyleRoll();
        _towerDefenseAutoplayStyleControlRoll = NextAutoplayStyleRoll();
        _towerDefenseAutoplayStyleRoleRoll = NextAutoplayStyleRoll();
    }

    private float NextAutoplayStyleRatioScale()
    {
        return Mathf.Lerp(TowerDefenseAutoplayStyleRatioMinimum,
            TowerDefenseAutoplayStyleRatioMaximum,
            (float)_towerDefenseAutoplayStyleRandom.NextDouble());
    }

    private uint NextAutoplayStyleRoll()
    {
        return unchecked(((uint)_towerDefenseAutoplayStyleRandom.Next() << 1) ^
                         (uint)_towerDefenseAutoplayStyleRandom.Next());
    }

    private static AutoplayBuildChoice SelectAutoplayPersonalityBuildChoice(
        AutoplayBuildChoice objective, AutoplayBuildChoice personality,
        float regretBudget)
    {
        if (!objective.IsValid) return personality;
        if (!personality.IsValid) return objective;
        if (regretBudget <= 0f)
        {
            objective.Utility = objective.ObjectiveUtility;
            objective.Efficiency = objective.ObjectiveEfficiency;
            objective.CapitalScore = objective.ObjectiveCapitalScore;
            return objective;
        }
        float minimumQuality = objective.ObjectiveCapitalScore *
                               (1f - Mathf.Clamp01(regretBudget));
        return personality.ObjectiveCapitalScore >= minimumQuality
            ? personality
            : objective;
    }

    private AutoplayBuildChoice ScoreAutoplayBuildChoice(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot, RougeTowerType type, int orderIndex,
        Vector2Int cell, AutoplayBuildPrior prior, float bestOpenTileAffinity)
    {
        AutoplayPressureChannels channels;
        float marginalRouteCoverage;
        float objectiveUncoveredPressure;
        float uncoveredPressure;
        float piercingLineValue = 0f;
        int cellCount = map.Width * map.Height;
        int spatialIndex = (int)type * cellCount + cell.y * map.Width + cell.x;
        if (_towerDefenseAutoplayPlanResultsReady &&
            (uint)spatialIndex <
            (uint)_towerDefenseAutoplayPlanCandidateResults.Length)
        {
            AutoplaySpatialCandidateResult spatial =
                _towerDefenseAutoplayPlanCandidateResults[spatialIndex];
            channels = spatial.Pressure;
            marginalRouteCoverage = spatial.MarginalRouteCoverage;
            objectiveUncoveredPressure = CombineAutoplayPressureForTower(type,
                spatial.UncoveredPressure, out _, false);
            uncoveredPressure = CombineAutoplayPressureForTower(type,
                spatial.UncoveredPressure, out _, true);
        }
        else
        {
            channels = GetAutoplayPressureChannels(map, cell,
                prior.AttackRange);
            marginalRouteCoverage = GetAutoplayMarginalDefenseValue(map, cell,
                prior.AttackRange, type, out objectiveUncoveredPressure,
                out uncoveredPressure);
        }
        float geometryPowerFactor = 1f;
        if (type == RougeTowerType.PiercingLaser)
        {
            // The generic spatial pass samples a focused tower's attack circle. A beam
            // crossing a busy straight lane can hit several bodies in that same shot,
            // while an elite or Boss is still only one target. Derive the multi-hit
            // realization from live non-Boss crowd pressure and the authored lane fit.
            float laneScore = Mathf.Clamp01(prior.PiercingLaneScore);
            float crowdOrTotalPressure = Mathf.Max(channels.Crowd,
                Mathf.Max(0f, channels.Total - channels.Elite - channels.Boss));
            float pressureDemand = 1f - Mathf.Exp(-crowdOrTotalPressure / 8f);
            float straightCrowdOpportunity = Mathf.Clamp01(
                laneScore * pressureDemand);
            piercingLineValue = Mathf.Sqrt(straightCrowdOpportunity);
            float expectedPierceHits = 1f + piercingLineValue * 2f;
            float crowdRealization = Mathf.Min(3f, expectedPierceHits *
                Mathf.Lerp(0.42f, 1f, laneScore));

            // MachineGun, Laser and PiercingLaser intentionally share the focused
            // coverage group. On a strong beam lane, restore only the crowd/total part
            // of that circular overlap discount; elite and Boss pressure stay untouched.
            AutoplayPressureChannels crowdChannels = new AutoplayPressureChannels
            {
                Total = Mathf.Max(0f,
                    channels.Total - channels.Elite - channels.Boss),
                Crowd = channels.Crowd
            };
            float objectiveFullPressure = CombineAutoplayPressureForTower(type,
                channels, out _, false);
            float styledFullPressure = CombineAutoplayPressureForTower(type,
                channels, out _, true);
            float objectiveCrowdPressure = CombineAutoplayPressureForTower(type,
                crowdChannels, out _, false);
            float styledCrowdPressure = CombineAutoplayPressureForTower(type,
                crowdChannels, out _, true);
            float overlapRelief = Mathf.SmoothStep(0f, 1f,
                piercingLineValue) * 0.78f;
            objectiveUncoveredPressure += Mathf.Min(objectiveCrowdPressure,
                Mathf.Max(0f, objectiveFullPressure -
                    objectiveUncoveredPressure)) * overlapRelief;
            uncoveredPressure += Mathf.Min(styledCrowdPressure,
                Mathf.Max(0f, styledFullPressure - uncoveredPressure)) *
                overlapRelief;
            float objectiveUncoveredCrowd = Mathf.Min(objectiveCrowdPressure,
                Mathf.Max(0f, objectiveUncoveredPressure));
            float styledUncoveredCrowd = Mathf.Min(styledCrowdPressure,
                Mathf.Max(0f, uncoveredPressure));
            objectiveUncoveredPressure += objectiveUncoveredCrowd *
                (crowdRealization - 1f);
            uncoveredPressure += styledUncoveredCrowd *
                (crowdRealization - 1f);

            ApplyAutoplayPiercingCrowdRealization(ref channels,
                crowdRealization);
            // MarginalPower feeds the common capital auction. With no crowd demand the
            // multiplier is exactly one, so Boss/single-target power never inherits
            // speculative pierce hits.
            geometryPowerFactor = expectedPierceHits;
            marginalRouteCoverage *= Mathf.Lerp(0.48f, 1f,
                laneScore);
        }
        else if (type == RougeTowerType.RocketBarrage)
        {
            // Rockets choose random landing points inside their whole attack disk.
            // Route-corridor probability is meaningful for crowds, but a single
            // elite/Boss only occupies its own AOE disk. Keeping those probabilities
            // separate prevents a busy lane from pretending every random rocket hits
            // the one target that matters.
            float crowdHitFactor = Mathf.Lerp(0.24f, 1f,
                prior.RandomAreaHitChance);
            float singleHitFactor = Mathf.Clamp(prior.SingleTargetRealization,
                0.01f, 1f);
            float crowdShare = channels.Total > 0.0001f
                ? Mathf.Clamp01(channels.Crowd / channels.Total)
                : 0.5f;
            geometryPowerFactor = Mathf.Lerp(singleHitFactor,
                crowdHitFactor, crowdShare);
            channels.Total *= crowdHitFactor;
            channels.Crowd *= crowdHitFactor;
            channels.Elite *= singleHitFactor;
            channels.Boss *= singleHitFactor;
            channels.Urgent *= Mathf.Lerp(singleHitFactor, crowdHitFactor,
                0.35f);
            marginalRouteCoverage *= crowdHitFactor;
            objectiveUncoveredPressure *= crowdHitFactor;
            uncoveredPressure *= crowdHitFactor;
        }
        else if (type == RougeTowerType.OrbitSphere)
        {
            // Dense lanes can realize much of a sweep, while one target is only hit
            // during the angular duty cycle of the rotating beams.
            float sweepFit = Mathf.Clamp01(prior.PathDwell * 0.42f +
                prior.RouteReuse * 0.34f + prior.Bottleneck * 0.24f);
            float crowdHitFactor = Mathf.Lerp(0.32f, 1f, sweepFit);
            float singleHitFactor = Mathf.Clamp(prior.SingleTargetRealization,
                0.02f, 1f);
            float crowdShare = channels.Total > 0.0001f
                ? Mathf.Clamp01(channels.Crowd / channels.Total)
                : 0.5f;
            geometryPowerFactor = Mathf.Lerp(singleHitFactor,
                crowdHitFactor, crowdShare);
            channels.Total *= crowdHitFactor;
            channels.Crowd *= crowdHitFactor;
            channels.Elite *= singleHitFactor;
            channels.Boss *= singleHitFactor;
            channels.Urgent *= Mathf.Lerp(singleHitFactor, crowdHitFactor,
                0.35f);
            marginalRouteCoverage *= crowdHitFactor;
            objectiveUncoveredPressure *= crowdHitFactor;
            uncoveredPressure *= crowdHitFactor;
        }
        float realizationFactor = GetAutoplayPressureRealizationFactor(
            prior.CombatPower);
        channels.Total *= realizationFactor;
        channels.Crowd *= realizationFactor;
        channels.Elite *= realizationFactor;
        channels.Boss *= realizationFactor;
        channels.Urgent *= realizationFactor;
        marginalRouteCoverage *= realizationFactor;
        objectiveUncoveredPressure *= realizationFactor;
        uncoveredPressure *= realizationFactor;
        float objectiveLocalPressure = CombineAutoplayPressureForTower(type,
            channels, out _, false);
        float localPressure = CombineAutoplayPressureForTower(type, channels,
            out AutoplayPressureLayer dominantLayer, true);
        objectiveLocalPressure = Mathf.Max(objectiveUncoveredPressure,
            objectiveLocalPressure * 0.14f);
        localPressure = Mathf.Max(uncoveredPressure, localPressure * 0.14f);
        float objectivePressureScore = Mathf.Log(1f +
            Mathf.Max(0f, objectiveLocalPressure)) * 70f *
            Mathf.Lerp(0.34f, 1f, AutoplayThreatReadingSkill);
        float pressureScore = Mathf.Log(1f + Mathf.Max(0f, localPressure)) * 70f *
            Mathf.Lerp(0.34f, 1f, AutoplayThreatReadingSkill);
        float marginalCoverageScore = Mathf.Sqrt(Mathf.Max(0f,
            marginalRouteCoverage)) * 42f;
        int existingTypeCount = _towerDefenseAutoplayTypeCounts[(int)type];
        float diversityScore = GetAutoplayDiversityScore(type) *
            Mathf.Lerp(0.82f, 1f, AutoplayAdaptationSkill);
        bool threatAligned = IsAutoplayTowerAlignedWithThreat(type, snapshot);
        float saturationPenalty = existingTypeCount <= 3
            ? 0f
            : (existingTypeCount - 3) * (existingTypeCount - 3) *
              (threatAligned ? 14f : 42f);
        float objectiveThreatFit = GetAutoplayThreatFit(type, snapshot) *
            Mathf.Lerp(0.18f, 1f, AutoplayThreatReadingSkill);
        float assaultArmorStyleScore =
            GetAutoplayAssaultArmorBuildStyleScore(type, snapshot);
        float threatFit = objectiveThreatFit + assaultArmorStyleScore;
        float bossCommitment = GetAutoplayBossPreparationCommitment(snapshot);
        float objectiveBossPreparationScore = 0f;
        if (bossCommitment > 0f && prior.BossRouteCoverage > 0.001f)
        {
            if (IsAutoplayBossDamageTower(type))
                objectiveBossPreparationScore +=
                    GetAutoplayBossPowerInvestmentScore(
                        prior.BossRouteCoverage, prior.SingleTargetPower);
            if (IsAutoplayControlTower(type))
                objectiveBossPreparationScore +=
                    GetAutoplayBossControlInvestmentScore(
                        prior.BossRouteCoverage, prior.BossRouteCoverage);
            objectiveBossPreparationScore *= bossCommitment *
                GetAutoplayBossReadinessUrgency();
        }
        float bossPreparationScore = objectiveBossPreparationScore *
                                     TowerDefenseAutoplayCommander.BossConcern;
        float objectiveGoalDefenseScore = GetAutoplayGoalDefenseScore(map,
            snapshot, cell,
            prior.AttackRange) * realizationFactor *
            Mathf.Lerp(0.16f, 1f, AutoplayCrisisResponseSkill);
        float nearBaseHeatCoverage = GetAutoplayNearBaseHeatCoverage(map, cell,
            prior.AttackRange, true, out int nearBaseHeatCellIndex) *
            realizationFactor;
        objectiveGoalDefenseScore += nearBaseHeatCoverage * 190f;
        float goalDefenseScore = objectiveGoalDefenseScore *
            TowerDefenseAutoplayCommander.DefenseBias;
        float repeatedTypePenalty = existingTypeCount <= 0
            ? 0f
            : existingTypeCount * existingTypeCount *
              (type == RougeTowerType.MachineGun ? 42f : 18f);
        saturationPenalty += repeatedTypePenalty;
        if (IsAutoplayDedicatedEffect(prior.PlaceEffect))
        {
            int sameEffectTypeCount = CountAutoplayTowersOnEffect(type,
                prior.PlaceEffect);
            saturationPenalty += sameEffectTypeCount *
                (prior.PlaceEffect == RougeTowerPlaceEffect.Bounty &&
                 type == RougeTowerType.MachineGun ? 105f : 52f);
        }
        if (type == RougeTowerType.PiercingLaser && piercingLineValue > 0f)
            saturationPenalty *= Mathf.Lerp(1f, 0.58f,
                Mathf.SmoothStep(0f, 1f, piercingLineValue));
        float priorityTileBonus = GetAutoplayPriorityTileBonus(prior.TileScore);
        float rawTileContribution = prior.TileScore * 1.35f + priorityTileBonus;
        bool emergencyStrategy = _towerDefenseAutoplayStrategyMode ==
                                 AutoplayStrategyMode.Emergency;
        float finisherDemand = Mathf.Clamp01(snapshot.UrgentPressure / 3f +
            nearBaseHeatCoverage * 0.7f + (emergencyStrategy ? 0.42f : 0f));
        float routeTimingScore = (prior.SetupFit *
            Mathf.Lerp(1f, 0.2f, finisherDemand) + prior.FinisherFit *
            Mathf.Lerp(0.25f, 1f, finisherDemand)) * 44f *
            AutoplayMapReadingSkill;
        if (emergencyStrategy)
        {
            objectiveGoalDefenseScore *= 1.35f;
            goalDefenseScore *= 1.35f;
        }
        float emergencyTilePenalty = emergencyStrategy &&
                                     IsAutoplayLongTermEconomyEffect(
                                         prior.PlaceEffect)
            ? rawTileContribution * 0.65f
            : 0f;
        float hiddenGeometry = prior.GeometryScore *
                               (1f - AutoplayMapReadingSkill);
        float objectiveFixedScore = prior.FixedScore - prior.CoverageScore * 0.72f -
            rawTileContribution * (1f - AutoplayMapReadingSkill) +
            prior.OpportunityPenalty * (1f - AutoplayMapReadingSkill) -
            emergencyTilePenalty - hiddenGeometry;
        float learnedFixedScore = prior.FixedScore - prior.CoverageScore * 0.72f -
            rawTileContribution * (1f - AutoplayMapReadingSkill) +
            rawTileContribution *
            (TowerDefenseAutoplayCommander.SpecialTileBias - 1f) *
            AutoplayMapReadingSkill +
            prior.OpportunityPenalty * (1f - AutoplayMapReadingSkill) -
            emergencyTilePenalty - hiddenGeometry;
        float missedTilePenalty = bestOpenTileAffinity >= 95f &&
                                   prior.TileScore < 95f
            ? 58f + (bestOpenTileAffinity - Mathf.Max(0f, prior.TileScore)) * 0.55f
            : 0f;
        if (type == RougeTowerType.PiercingLaser && piercingLineValue > 0f)
            missedTilePenalty *= Mathf.Lerp(1f, 0.62f,
                Mathf.SmoothStep(0f, 1f, piercingLineValue));
        float objectiveMissedTilePenalty = missedTilePenalty *
            AutoplayMapReadingSkill;
        missedTilePenalty = objectiveMissedTilePenalty *
            TowerDefenseAutoplayCommander.SpecialTileBias;
        float mainHealthRatio = mainTower != null && mainTower.maxHealth > 0.001f
            ? Mathf.Clamp01(mainTower.CurrentHealth / mainTower.maxHealth)
            : 1f;
        bool goalEmergency = _towerDefenseAutoplaySustainedNearBaseCrisis ||
                             mainHealthRatio <= 0.5f;
        if (goalEmergency && objectiveGoalDefenseScore >= 145f)
            objectiveMissedTilePenalty *= 0.18f;
        if (goalEmergency && goalDefenseScore >= 145f)
            missedTilePenalty *= 0.18f;
        if (bossPreparationScore >= 45f &&
            dominantLayer != AutoplayPressureLayer.Urgent)
            dominantLayer = AutoplayPressureLayer.Boss;
        float dynamicScore = pressureScore + diversityScore + threatFit +
                             bossPreparationScore +
                             goalDefenseScore + marginalCoverageScore +
                             routeTimingScore -
                             missedTilePenalty -
                             saturationPenalty;
        float utility = Mathf.Max(1f, learnedFixedScore + dynamicScore);
        float objectiveDynamicScore = objectivePressureScore + diversityScore +
            objectiveThreatFit + objectiveBossPreparationScore +
            objectiveGoalDefenseScore + marginalCoverageScore +
            routeTimingScore -
            objectiveMissedTilePenalty -
            saturationPenalty;
        float objectiveUtility = Mathf.Max(1f, objectiveFixedScore +
            objectiveDynamicScore);
        float costDivisor = Mathf.Max(100f, prior.PaidCost + 180f);
        float objectiveEfficiency = objectiveUtility * 100f / costDivisor;
        float styledEfficiency = utility * 100f / costDivisor;
        float efficiency = ApplyAutoplayPersonalityPreference(styledEfficiency,
            TowerDefenseAutoplayCommander.BuildBias *
            GetAutoplayPersonalityTowerBias(type));
        efficiency = ApplyAutoplayEconomyReturnSignal(efficiency,
            objectiveEfficiency);
        efficiency = ApplyAutoplayJudgmentUncertainty(efficiency, type, cell);

        return new AutoplayBuildChoice
        {
            IsValid = true,
            Type = type,
            Cell = cell,
            PlaceEffect = prior.PlaceEffect,
            BuildOrderIndex = orderIndex,
            OriginalCost = prior.OriginalCost,
            PaidCost = prior.PaidCost,
            Utility = utility,
            Efficiency = efficiency,
            ObjectiveUtility = objectiveUtility,
            ObjectiveEfficiency = objectiveEfficiency,
            FixedScore = learnedFixedScore,
            DynamicScore = dynamicScore,
            TileScore = prior.TileScore,
            CoverageScore = prior.CoverageScore + marginalCoverageScore,
            PressureScore = pressureScore,
            DiversityScore = diversityScore,
            GoalDefenseScore = goalDefenseScore,
            NearBaseHeatCoverage = nearBaseHeatCoverage,
            NearBaseHeatCellIndex = nearBaseHeatCellIndex,
            OpportunityPenalty = prior.OpportunityPenalty + missedTilePenalty,
            GeometryScore = prior.GeometryScore + routeTimingScore,
            MarginalPower = prior.CombatPower * geometryPowerFactor,
            BossRouteCoverage = prior.BossRouteCoverage,
            DominantPressureLayer = dominantLayer
        };
    }

    private static void ApplyAutoplayPiercingCrowdRealization(
        ref AutoplayPressureChannels channels, float realization)
    {
        realization = Mathf.Clamp(realization, 0f, 3f);
        float singleTargetPressure = Mathf.Min(Mathf.Max(0f, channels.Total),
            Mathf.Max(0f, channels.Elite) + Mathf.Max(0f, channels.Boss));
        float crowdOrUnclassifiedPressure = Mathf.Max(0f,
            channels.Total - singleTargetPressure);
        channels.Total = singleTargetPressure +
                         crowdOrUnclassifiedPressure * realization;
        channels.Crowd *= realization;
    }

    private float GetBestOpenAutoplayTileAffinity(RougeTowerDefenseMap map,
        RougeTowerType type)
    {
        float best = 0f;
        if (map == null) return best;
        int cellCount = map.Width * map.Height;
        for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
        {
            if (!_towerDefenseAutoplayBuildableTopology[cellIndex] ||
                _towerDefenseAutoplayOccupiedCells[cellIndex]) continue;
            best = Mathf.Max(best, GetAutoplayTileAffinity(type,
                _towerDefenseAutoplayEffectiveEffects[cellIndex]));
        }
        return best;
    }

    private float GetAutoplayGoalDefenseScore(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot, Vector2Int cell, float attackRange)
    {
        if (map == null || !snapshot.HasMainCell) return 0f;
        float cellDistance = Vector2.Distance(cell, snapshot.MainCell);
        float reachInCells = attackRange / Mathf.Max(0.1f, map.CellSize) + 1.5f;
        float reach = Mathf.Clamp01(1f - cellDistance / Mathf.Max(1f, reachInCells));
        float nearGoal = Mathf.Clamp01(1f - cellDistance / 8f);
        float score = reach * 165f + nearGoal * nearGoal * 95f;
        if (_towerDefenseAutoplayImmediateCoreBreach)
        {
            float directCoreCoverage = Mathf.Clamp01(1f - cellDistance /
                Mathf.Max(1f, reachInCells));
            score *= Mathf.Lerp(1.75f, 2.65f, directCoreCoverage);
        }
        if (snapshot.UrgentPressure >= 2f) score *= 1.35f;
        if (mainTower != null && mainTower.maxHealth > 0.001f)
        {
            float healthRatio = Mathf.Clamp01(mainTower.CurrentHealth /
                                               mainTower.maxHealth);
            if (healthRatio <= 0.35f) score *= 1.65f;
            else if (healthRatio <= 0.7f) score *= 1.3f;
        }
        return score;
    }

    private void EvaluateAutoplayUpgradeChoices(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot, int emergencyHeatCellIndex,
        int maximumCoreUpgradeCost,
        out AutoplayUpgradeChoice bestOverall,
        out AutoplayUpgradeChoice bestAffordable,
        out AutoplayUpgradeChoice bestAffordableCore,
        out AutoplayUpgradeChoice bestHeat,
        out AutoplayUpgradeChoice bestAffordableHeat)
    {
        bestOverall = default;
        bestAffordable = default;
        bestAffordableCore = default;
        bestHeat = default;
        bestAffordableHeat = default;
        _towerDefenseAutoplayUpgradeChoiceScratch.Clear();

        float maximumObservedCoreRate = 0f;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) || !tower.CanUpgrade) continue;
            int paidCost = GetTowerDefenseAutoplayPaidCost(tower.UpgradeCost);
            bool realizable = paidCost <= _towerDefenseGold &&
                (paidCost <= 0 || paidCost <= maximumCoreUpgradeCost);
            if (!realizable) continue;
            float observedRate = GetAutoplayObservedUpgradeCoreRate(tower,
                out float observedConfidence);
            if (observedConfidence >= 0.65f)
                maximumObservedCoreRate = Mathf.Max(maximumObservedCoreRate,
                    observedRate);
        }

        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) || !tower.CanUpgrade) continue;
            AutoplayUpgradeChoice choice = ScoreAutoplayUpgradeChoice(map, snapshot,
                tower);
            _towerDefenseAutoplayUpgradeChoiceScratch.Add(choice);
        }

        ScoreAutoplayUpgradeCapitalChoices(
            out AutoplayUpgradeChoice objectiveOverall,
            out AutoplayUpgradeChoice objectiveAffordable,
            out AutoplayUpgradeChoice personalityOverall,
            out AutoplayUpgradeChoice personalityAffordable);
        float objectiveCoreFloor = objectiveAffordable.IsValid
            ? objectiveAffordable.ObjectiveCapitalScore * 0.88f
            : 0f;

        for (int i = 0; i < _towerDefenseAutoplayUpgradeChoiceScratch.Count; i++)
        {
            AutoplayUpgradeChoice choice =
                _towerDefenseAutoplayUpgradeChoiceScratch[i];
            bool affordable = choice.PaidCost <= _towerDefenseGold;

            bool coversEmergencyHeat =
                choice.NearBaseHeatCoverage >=
                    TowerDefenseAutoplayNearBaseEarlyWarning &&
                (emergencyHeatCellIndex < 0 ||
                 AreAutoplayHeatCellsInSameHotspot(map,
                     choice.NearBaseHeatCellIndex, emergencyHeatCellIndex));
            float emergencyUpgradeScore = coversEmergencyHeat
                ? GetAutoplayEmergencyPurchaseScore(
                    GetAutoplayUpgradeCapitalGain(choice), choice.PaidCost)
                : 0f;
            float heatUpgradeSelectionScore =
                _towerDefenseAutoplayImmediateCoreBreach && coversEmergencyHeat
                    ? GetAutoplayUpgradeCapitalGain(choice)
                    : emergencyUpgradeScore;
            float bestHeatScore = bestHeat.IsValid
                ? _towerDefenseAutoplayImmediateCoreBreach
                    ? GetAutoplayUpgradeCapitalGain(bestHeat)
                    : GetAutoplayEmergencyPurchaseScore(
                        GetAutoplayUpgradeCapitalGain(bestHeat),
                        bestHeat.PaidCost)
                : 0f;
            if (coversEmergencyHeat &&
                (!bestHeat.IsValid || heatUpgradeSelectionScore > bestHeatScore))
            {
                choice.Utility = choice.ObjectiveUtility;
                choice.Efficiency = choice.ObjectiveEfficiency;
                choice.CapitalScore = choice.ObjectiveCapitalScore;
                bestHeat = choice;
            }
            float bestAffordableHeatScore = bestAffordableHeat.IsValid
                ? _towerDefenseAutoplayImmediateCoreBreach
                    ? GetAutoplayUpgradeCapitalGain(bestAffordableHeat)
                    : GetAutoplayEmergencyPurchaseScore(
                        GetAutoplayUpgradeCapitalGain(bestAffordableHeat),
                        bestAffordableHeat.PaidCost)
                : 0f;
            if (affordable && coversEmergencyHeat &&
                (!bestAffordableHeat.IsValid || heatUpgradeSelectionScore >
                 bestAffordableHeatScore))
            {
                choice.Utility = choice.ObjectiveUtility;
                choice.Efficiency = choice.ObjectiveEfficiency;
                choice.CapitalScore = choice.ObjectiveCapitalScore;
                bestAffordableHeat = choice;
            }

            bool coreSpendFits = choice.PaidCost <= 0 ||
                                 choice.PaidCost <= maximumCoreUpgradeCost;
            bool provenCore = affordable && coreSpendFits &&
                maximumObservedCoreRate > 0.001f &&
                choice.ObservedCoreConfidence >= 0.65f &&
                choice.ObservedCoreRate >= maximumObservedCoreRate * 0.65f &&
                choice.ObjectiveCapitalScore >= objectiveCoreFloor;
            if (provenCore && IsBetterAutoplayCoreUpgrade(choice,
                    bestAffordableCore))
            {
                choice.Utility = choice.ObjectiveUtility;
                choice.Efficiency = choice.ObjectiveEfficiency;
                choice.CapitalScore = choice.ObjectiveCapitalScore;
                bestAffordableCore = choice;
            }
        }
        float regretBudget = GetAutoplayPersonalityRegretBudget(snapshot);
        bestOverall = SelectAutoplayPersonalityUpgradeChoice(objectiveOverall,
            personalityOverall, regretBudget);
        bestAffordable = SelectAutoplayPersonalityUpgradeChoice(
            objectiveAffordable, personalityAffordable, regretBudget);
    }

    private void ScoreAutoplayUpgradeCapitalChoices(
        out AutoplayUpgradeChoice objectiveOverall,
        out AutoplayUpgradeChoice objectiveAffordable,
        out AutoplayUpgradeChoice personalityOverall,
        out AutoplayUpgradeChoice personalityAffordable)
    {
        objectiveOverall = default;
        objectiveAffordable = default;
        personalityOverall = default;
        personalityAffordable = default;
        float maximumObjectiveEfficiency = 0f;
        float maximumObjectiveUtility = 0f;
        float maximumStyledEfficiency = 0f;
        float maximumStyledUtility = 0f;
        int minimumPositiveCost = int.MaxValue;
        for (int i = 0; i < _towerDefenseAutoplayUpgradeChoiceScratch.Count; i++)
        {
            AutoplayUpgradeChoice choice =
                _towerDefenseAutoplayUpgradeChoiceScratch[i];
            maximumObjectiveEfficiency = Mathf.Max(maximumObjectiveEfficiency,
                choice.ObjectiveEfficiency);
            maximumObjectiveUtility = Mathf.Max(maximumObjectiveUtility,
                choice.ObjectiveUtility);
            maximumStyledEfficiency = Mathf.Max(maximumStyledEfficiency,
                choice.Efficiency);
            maximumStyledUtility = Mathf.Max(maximumStyledUtility,
                choice.Utility);
            if (choice.PaidCost > 0)
                minimumPositiveCost = Mathf.Min(minimumPositiveCost,
                    choice.PaidCost);
        }

        int referenceCost = minimumPositiveCost == int.MaxValue
            ? 0
            : minimumPositiveCost * 2;
        float wealth = GetAutoplayCapitalWealth(_towerDefenseGold, referenceCost);
        for (int i = 0; i < _towerDefenseAutoplayUpgradeChoiceScratch.Count; i++)
        {
            AutoplayUpgradeChoice choice =
                _towerDefenseAutoplayUpgradeChoiceScratch[i];
            choice.ObjectiveCapitalScore = GetAutoplayNormalizedCapitalScore(
                choice.ObjectiveEfficiency, choice.ObjectiveUtility,
                maximumObjectiveEfficiency, maximumObjectiveUtility, wealth);
            choice.CapitalScore = GetAutoplayNormalizedCapitalScore(
                choice.Efficiency, choice.Utility, maximumStyledEfficiency,
                maximumStyledUtility, wealth);
            _towerDefenseAutoplayUpgradeChoiceScratch[i] = choice;

            if (!objectiveOverall.IsValid || choice.ObjectiveCapitalScore >
                objectiveOverall.ObjectiveCapitalScore)
                objectiveOverall = choice;
            if (!personalityOverall.IsValid || choice.CapitalScore >
                personalityOverall.CapitalScore)
                personalityOverall = choice;
            if (choice.PaidCost > _towerDefenseGold) continue;
            if (!objectiveAffordable.IsValid || choice.ObjectiveCapitalScore >
                objectiveAffordable.ObjectiveCapitalScore)
                objectiveAffordable = choice;
            if (!personalityAffordable.IsValid || choice.CapitalScore >
                personalityAffordable.CapitalScore)
                personalityAffordable = choice;
        }
    }

    private static bool IsBetterAutoplayCoreUpgrade(
        AutoplayUpgradeChoice candidate, AutoplayUpgradeChoice current)
    {
        if (!candidate.IsValid) return false;
        if (!current.IsValid) return true;
        // Live output is the primary evidence. Static upgrade efficiency breaks ties
        // only when two candidates perform within roughly ten percent of one another;
        // otherwise cheap machine-gun upgrades would win after merely entering the
        // core set and the observation signal would have no practical effect.
        if (candidate.ObservedCoreRate > current.ObservedCoreRate * 1.1f)
            return true;
        if (current.ObservedCoreRate > candidate.ObservedCoreRate * 1.1f)
            return false;
        return candidate.ObjectiveCapitalScore > current.ObjectiveCapitalScore;
    }

    private float GetAutoplayObservedUpgradeCoreRate(RougeDefenseTower tower,
        out float confidence)
    {
        confidence = 0f;
        if (!IsAutoplayStandardTower(tower) ||
            !_towerDefenseAutoplayTowerObservations.TryGetValue(tower,
                out AutoplayTowerObservation observation))
            return 0f;

        int typeIndex = (int)tower.TowerType;
        float typeWeight = _towerDefenseAutoplayPerformanceWeightByType[typeIndex];
        if (typeWeight <= 0.001f) return 0f;
        float towerWeight = EstimateAutoplayObservedTowerWeight(tower);
        float recentRate = _towerDefenseAutoplayRecentDamageRateByType[typeIndex] *
                           towerWeight / typeWeight;
        float longRate = observation.ObservedDamage /
                         Mathf.Max(1f, observation.ObservedSeconds);
        bool hasMeasuredOutput = observation.ObservedDamage >= 20f ||
                                 recentRate >= 0.25f;
        if (!hasMeasuredOutput) return 0f;

        confidence = Mathf.Clamp01(observation.ObservedSeconds / 18f);
        float utilityAdjustment = 1f +
            GetAutoplayTowerUtilityFactor(tower.TowerType) * 0.25f;
        return Mathf.Max(0f, recentRate * 0.7f + longRate * 0.3f) *
               utilityAdjustment;
    }

    private static AutoplayUpgradeChoice SelectAutoplayPersonalityUpgradeChoice(
        AutoplayUpgradeChoice objective, AutoplayUpgradeChoice personality,
        float regretBudget)
    {
        if (!objective.IsValid) return personality;
        if (!personality.IsValid) return objective;
        if (regretBudget <= 0f)
        {
            objective.Utility = objective.ObjectiveUtility;
            objective.Efficiency = objective.ObjectiveEfficiency;
            objective.CapitalScore = objective.ObjectiveCapitalScore;
            return objective;
        }
        float minimumQuality = objective.ObjectiveCapitalScore *
                               (1f - Mathf.Clamp01(regretBudget));
        return personality.ObjectiveCapitalScore >= minimumQuality
            ? personality
            : objective;
    }

    private float GetAutoplayUpgradeBranchValue(RougeDefenseTower tower,
        AutoplayBattleSnapshot snapshot, AutoplayUpgradeChoice scoredChoice,
        int choiceIndex)
    {
        if (tower == null || !tower.RequiresUpgradeChoice ||
            (uint)choiceIndex > 1u) return 0f;

        float nonBossEnemies = Mathf.Max(0,
            snapshot.ActiveEnemies - snapshot.BossEnemies);
        float crowdDemand = Mathf.Clamp01((snapshot.IncomingCrowdPressure * 0.22f +
            nonBossEnemies * 0.09f +
            (scoredChoice.DominantPressureLayer == AutoplayPressureLayer.Crowd
                ? 1.2f
                : 0f)) / 4f);
        float hardTargetDemand = Mathf.Clamp01((snapshot.EliteEnemies +
            snapshot.BossEnemies * 2f + snapshot.IncomingElitePressure * 0.24f +
            (scoredChoice.DominantPressureLayer == AutoplayPressureLayer.Boss
                ? 1.4f
                : 0f)) / 4f);
        float armorDemand = Mathf.Clamp01(Mathf.Log(1f +
            Mathf.Max(0f, scoredChoice.UncoveredArmorPressure)) / Mathf.Log(7f));
        float controlDemand = Mathf.Clamp01(Mathf.Log(1f +
            Mathf.Max(0f, scoredChoice.FastUncontrolledPressure)) / Mathf.Log(6f));
        controlDemand = Mathf.Max(controlDemand,
            GetAutoplayBossPreparationCommitment(snapshot) *
            _towerDefenseAutoplayBossControlDeficit);
        float urgentDemand = Mathf.Clamp01((snapshot.UrgentPressure +
            (scoredChoice.DominantPressureLayer == AutoplayPressureLayer.Urgent
                ? 1.5f
                : 0f)) / 4f);
        float chokeFit = Mathf.Clamp01(Mathf.Max(scoredChoice.RouteReuse,
            scoredChoice.Bottleneck));
        float setupFit = Mathf.Clamp01(scoredChoice.EarlyRouteExposure * 0.75f +
                                       chokeFit * 0.45f);
        float finisherFit = Mathf.Clamp01(scoredChoice.LateRouteExposure * 0.8f +
                                          urgentDemand * 0.35f);
        float fit;

        // A specialization is part of the product being bought. Model the chosen
        // route's real role before the upgrade enters the common capital auction;
        // otherwise every branch looks like the same anonymous +105 utility.
        switch (tower.TowerType)
        {
            case RougeTowerType.Ice:
                if (tower.NeedsIceBranchChoice)
                    fit = choiceIndex == 0
                        ? Mathf.Max(controlDemand, Mathf.Max(crowdDemand, chokeFit))
                        : Mathf.Max(armorDemand, hardTargetDemand);
                else if (tower.UsesIceFreeze)
                {
                    if (choiceIndex == 0)
                        fit = Mathf.Max(urgentDemand,
                            Mathf.Max(controlDemand, crowdDemand));
                    else
                    {
                        float frostTargets = Mathf.Clamp01(
                            CountAutoplayPermanentFrostTargets(tower) / 4f);
                        fit = Mathf.Max(frostTargets,
                            Mathf.Max(setupFit, chokeFit)) * (1f - urgentDemand * 0.35f);
                    }
                }
                else
                {
                    fit = choiceIndex == 0
                        ? Mathf.Max(hardTargetDemand,
                            Mathf.Clamp01(scoredChoice.VulnerablePressure / 6f))
                        : armorDemand;
                }
                return Mathf.Min(245f, 135f + fit * 100f +
                    (tower.IsOnFrostTile ? 10f : 0f));

            case RougeTowerType.MachineGun:
                if (tower.NeedsMachineGunBranchChoice)
                    fit = choiceIndex == 0
                        ? Mathf.Max(hardTargetDemand, finisherFit)
                        : Mathf.Max(crowdDemand, chokeFit);
                else if (tower.UsesMachineGunCritical)
                    fit = choiceIndex == 0
                        ? Mathf.Max(0.45f, hardTargetDemand)
                        : armorDemand;
                else
                    fit = choiceIndex == 0
                        ? Mathf.Max(crowdDemand, chokeFit)
                        : Mathf.Max(hardTargetDemand, finisherFit);
                return 78f + fit * 92f;

            case RougeTowerType.Cannon:
                if (tower.NeedsCannonBranchChoice)
                    fit = choiceIndex == 0
                        ? Mathf.Max(crowdDemand, finisherFit)
                        : Mathf.Max(chokeFit, setupFit);
                else if (tower.UsesCannonInnerBlast)
                    fit = choiceIndex == 0
                        ? Mathf.Max(crowdDemand, chokeFit)
                        : Mathf.Max(crowdDemand, setupFit);
                else
                    fit = choiceIndex == 0
                        ? Mathf.Max(urgentDemand, controlDemand)
                        : Mathf.Max(chokeFit, setupFit);
                return 100f + fit * 105f;

            case RougeTowerType.Flame:
                if (tower.NeedsFlameBranchChoice)
                    fit = choiceIndex == 0
                        ? Mathf.Max(crowdDemand, Mathf.Max(chokeFit, finisherFit))
                        : Mathf.Max(hardTargetDemand, setupFit);
                else if (tower.UsesFlamethrower)
                    fit = choiceIndex == 0
                        ? Mathf.Max(crowdDemand, chokeFit)
                        : Mathf.Max(hardTargetDemand, finisherFit);
                else
                {
                    bool hasFreezeSource =
                        HasAutoplayIceBranch(RougeIceTowerBranch.Freeze);
                    fit = choiceIndex == 0
                        ? Mathf.Max(crowdDemand, setupFit)
                        : hasFreezeSource
                            ? Mathf.Max(hardTargetDemand, armorDemand)
                            : 0f;
                }
                return 105f + fit * 112f;

            case RougeTowerType.Laser:
                if (tower.NeedsLaserBranchChoice)
                    fit = choiceIndex == 0
                        ? armorDemand
                        : Mathf.Max(crowdDemand, chokeFit);
                else if (tower.UsesLaserArmorBreak)
                    fit = choiceIndex == 0
                        ? Mathf.Max(armorDemand, hardTargetDemand)
                        : Mathf.Max(hardTargetDemand, finisherFit);
                else
                    fit = choiceIndex == 0
                        ? Mathf.Max(crowdDemand, chokeFit)
                        : Mathf.Max(hardTargetDemand, finisherFit);
                return 105f + fit * 118f;

            default:
                return 0f;
        }
    }

    private AutoplayUpgradeChoice ScoreAutoplayUpgradeChoice(
        RougeTowerDefenseMap map, AutoplayBattleSnapshot snapshot,
        RougeDefenseTower tower)
    {
        int originalCost = tower.UpgradeCost;
        int paidCost = GetTowerDefenseAutoplayPaidCost(originalCost);
        int levelStride = TowerDefenseVisuals.MaxTowerLevel + 1;
        int priorIndex = (int)tower.TowerType * levelStride + tower.Level;
        float cachedGrowth = (uint)priorIndex <
            (uint)_towerDefenseAutoplayUpgradeGrowthPriors.Length
            ? _towerDefenseAutoplayUpgradeGrowthPriors[priorIndex]
            : 55f;
        float absolutePowerGain = (uint)priorIndex <
            (uint)_towerDefenseAutoplayUpgradeAbsoluteGainPriors.Length
            ? _towerDefenseAutoplayUpgradeAbsoluteGainPriors[priorIndex]
            : 0f;
        float rangeRatio = (uint)priorIndex <
            (uint)_towerDefenseAutoplayUpgradeRangePriors.Length
            ? _towerDefenseAutoplayUpgradeRangePriors[priorIndex]
            : 0f;
        // The cached delta is the clean type×level baseline. Real towers may already
        // sit on damage/speed pads, receive reinforcement aura, or carry a branch
        // that multiplies their throughput; upgrading those instances creates a
        // larger absolute gain. Keep the realization bounded so one temporary buff
        // cannot overwhelm the rest of the capital market.
        float installedUpgradeRealization =
            GetAutoplayInstalledUpgradeRealization(tower);
        absolutePowerGain *= installedUpgradeRealization;

        float objectiveLocalPressure = 0f;
        float localPressure = 0f;
        AutoplayPressureLayer dominantLayer = AutoplayPressureLayer.Total;
        if (map != null && map.WorldToCell(tower.transform.position,
                out Vector2Int towerCell))
        {
            float projectedRange = tower.AttackRange * (1f + rangeRatio);
            AutoplayPressureChannels channels = GetAutoplayPressureChannels(map,
                towerCell, projectedRange);
            objectiveLocalPressure = CombineAutoplayPressureForTower(
                tower.TowerType, channels, out _, false);
            localPressure = CombineAutoplayPressureForTower(tower.TowerType,
                channels, out dominantLayer, true);
        }
        AutoplayBuildPrior placementPrior = default;
        if (map != null && map.WorldToCell(tower.transform.position,
                out Vector2Int placementCell))
        {
            int placementIndex = (int)tower.TowerType * map.Width * map.Height +
                                 placementCell.y * map.Width + placementCell.x;
            if ((uint)placementIndex <
                (uint)_towerDefenseAutoplayBuildPriors.Length)
                placementPrior = _towerDefenseAutoplayBuildPriors[placementIndex];
        }
        float lateHealthRatio = snapshot.LateHealthWeight > 0.001f
            ? Mathf.Clamp01(snapshot.LateHealthRatioSum /
                            snapshot.LateHealthWeight)
            : 0.5f;
        AutoplayUpgradeChoice specializationContext = new AutoplayUpgradeChoice
        {
            Tower = tower,
            SpecializationChoiceIndex = -1,
            UncoveredArmorPressure = snapshot.UncoveredArmorPressure,
            FastUncontrolledPressure = snapshot.FastUncontrolledPressure,
            VulnerablePressure = snapshot.VulnerablePressure,
            LateHealthRatio = lateHealthRatio,
            EarlyRouteExposure = placementPrior.EarlyRouteExposure,
            LateRouteExposure = placementPrior.LateRouteExposure,
            RouteReuse = placementPrior.RouteReuse,
            Bottleneck = placementPrior.Bottleneck,
            MarginalPower = absolutePowerGain,
            DominantPressureLayer = dominantLayer
        };
        float objectivePressureScore = Mathf.Log(1f +
            Mathf.Max(0f, objectiveLocalPressure)) * 58f *
            Mathf.Lerp(0.34f, 1f, AutoplayThreatReadingSkill);
        float pressureScore = Mathf.Log(1f + Mathf.Max(0f, localPressure)) * 58f *
            Mathf.Lerp(0.34f, 1f, AutoplayThreatReadingSkill);
        float objectiveThreatFit = GetAutoplayThreatFit(tower.TowerType,
            snapshot) * 0.55f *
            Mathf.Lerp(0.18f, 1f, AutoplayThreatReadingSkill);
        float objectiveBossPreparationScore = 0f;
        float bossCommitment = GetAutoplayBossPreparationCommitment(snapshot);
        if (bossCommitment > 0f && map != null)
        {
            float currentBossRouteCoverage = GetAutoplayBossRouteCoverage(map,
                tower.transform.position, tower.AttackRange);
            float projectedBossRouteCoverage = GetAutoplayBossRouteCoverage(map,
                tower.transform.position, tower.AttackRange * (1f + rangeRatio));
            if (IsAutoplayBossDamageTower(tower.TowerType) &&
                !tower.UsesRotatingFlamethrower)
            {
                RougeTowerStats currentStats = TowerDefenseVisuals.GetStats(
                    tower.TowerType, tower.Level);
                RougeTowerStats nextStats = TowerDefenseVisuals.GetStats(
                    tower.TowerType, tower.Level + 1);
                float singleTargetGain = Mathf.Max(0f,
                    EstimateAutoplaySingleTargetPower(tower.TowerType,
                        nextStats, default) -
                    EstimateAutoplaySingleTargetPower(tower.TowerType,
                        currentStats, default)) * installedUpgradeRealization;
                objectiveBossPreparationScore +=
                    GetAutoplayBossPowerInvestmentScore(
                        projectedBossRouteCoverage, singleTargetGain);
            }
            if (IsAutoplayControlTower(tower.TowerType))
                objectiveBossPreparationScore +=
                    GetAutoplayBossControlInvestmentScore(
                        projectedBossRouteCoverage,
                        Mathf.Max(0f, projectedBossRouteCoverage -
                            currentBossRouteCoverage));
            objectiveBossPreparationScore *= bossCommitment *
                GetAutoplayBossReadinessUrgency();
            if (objectiveBossPreparationScore >= 45f &&
                dominantLayer != AutoplayPressureLayer.Urgent)
                dominantLayer = AutoplayPressureLayer.Boss;
        }
        float bossPreparationScore = objectiveBossPreparationScore *
            TowerDefenseAutoplayCommander.BossConcern;
        specializationContext.DominantPressureLayer = dominantLayer;
        int specializationChoiceIndex = tower.RequiresUpgradeChoice
            ? GetAutoplayUpgradeChoice(tower, specializationContext, out _)
            : -1;
        float branchValue = GetAutoplayUpgradeBranchValue(tower, snapshot,
            specializationContext, specializationChoiceIndex);
        float assaultArmorStyleScore =
            GetAutoplayAssaultArmorUpgradeStyleScore(tower,
                specializationChoiceIndex, snapshot);
        // Relative growth recognizes efficient transitions; absolute gain keeps a
        // wealthy controller from preferring another cheap low-tier purchase over a
        // large upgrade that adds far more realized output. Branch value is based on
        // the exact specialization that will be executed, not a generic tower bonus.
        float absoluteGrowthScore = Mathf.Log(1f +
            Mathf.Max(0f, absolutePowerGain)) * 32f;
        float growthScore = cachedGrowth + absoluteGrowthScore + branchValue;
        float growthRatio = Mathf.Max(0f, (cachedGrowth - 55f -
            rangeRatio * 100f) / 210f);
        int duplicateCount = _towerDefenseAutoplayTypeCounts[(int)tower.TowerType];
        // Once an area/function is already covered, consolidating its installed
        // towers is preferable to buying yet another copy of the same type.
        float consolidationBonus = Mathf.Min(72f,
            Mathf.Max(0, duplicateCount - 1) * 18f);
        float nearBaseHeatCoverage = 0f;
        int nearBaseHeatCellIndex = -1;
        float heatReinforcement = 0f;
        if (map != null && map.WorldToCell(tower.transform.position,
                out Vector2Int heatCell))
        {
            // An upgrade reinforces an installed defense; unlike a new build, its own
            // existing coverage must not divide away the hotspot it is defending.
            nearBaseHeatCoverage = GetAutoplayNearBaseHeatCoverage(map, heatCell,
                tower.AttackRange * (1f + rangeRatio), false,
                out nearBaseHeatCellIndex);
            heatReinforcement = nearBaseHeatCoverage *
                (80f + growthRatio * 170f);
        }
        float utility = Mathf.Max(1f, growthScore + pressureScore *
            Mathf.Clamp(0.35f + growthRatio, 0.35f, 1.25f) +
            objectiveThreatFit + assaultArmorStyleScore +
            bossPreparationScore + consolidationBonus + heatReinforcement);
        float objectiveUtility = Mathf.Max(1f, growthScore +
            objectivePressureScore *
            Mathf.Clamp(0.35f + growthRatio, 0.35f, 1.25f) +
            objectiveThreatFit +
            objectiveBossPreparationScore + consolidationBonus +
            heatReinforcement);
        float costDivisor = Mathf.Max(paidCost <= 0 ? 65f : 100f,
            paidCost + 180f);
        float objectiveEfficiency = objectiveUtility * 100f / costDivisor;
        float styledEfficiency = utility * 100f / costDivisor;
        float efficiency = ApplyAutoplayPersonalityPreference(styledEfficiency,
            TowerDefenseAutoplayCommander.UpgradeBias *
            GetAutoplayPersonalityUpgradeBias(tower,
                specializationChoiceIndex));
        efficiency = ApplyAutoplayEconomyReturnSignal(efficiency,
            objectiveEfficiency);
        if (map != null && map.WorldToCell(tower.transform.position,
                out Vector2Int uncertaintyCell))
            efficiency = ApplyAutoplayJudgmentUncertainty(efficiency,
                tower.TowerType, uncertaintyCell);
        float observedCoreRate = GetAutoplayObservedUpgradeCoreRate(tower,
            out float observedCoreConfidence);
        return new AutoplayUpgradeChoice
        {
            IsValid = true,
            Tower = tower,
            SpecializationChoiceIndex = specializationChoiceIndex,
            OriginalCost = originalCost,
            PaidCost = paidCost,
            Utility = utility,
            Efficiency = efficiency,
            ObjectiveUtility = objectiveUtility,
            ObjectiveEfficiency = objectiveEfficiency,
            PressureScore = pressureScore,
            GrowthScore = growthScore,
            NearBaseHeatCoverage = nearBaseHeatCoverage,
            NearBaseHeatCellIndex = nearBaseHeatCellIndex,
            ObservedCoreRate = observedCoreRate,
            ObservedCoreConfidence = observedCoreConfidence,
            MarginalPower = absolutePowerGain,
            UncoveredArmorPressure = snapshot.UncoveredArmorPressure,
            FastUncontrolledPressure = snapshot.FastUncontrolledPressure,
            VulnerablePressure = snapshot.VulnerablePressure,
            LateHealthRatio = lateHealthRatio,
            EarlyRouteExposure = placementPrior.EarlyRouteExposure,
            LateRouteExposure = placementPrior.LateRouteExposure,
            RouteReuse = placementPrior.RouteReuse,
            Bottleneck = placementPrior.Bottleneck,
            DominantPressureLayer = dominantLayer
        };
    }

    private void VisitAutoplayGroundCoverageCells(RougeTowerDefenseMap map,
        Vector2Int towerCell, float attackRange, ref float value)
    {
        float cellSize = Mathf.Max(0.1f, map.CellSize);
        int radiusCells = Mathf.Max(1, Mathf.CeilToInt(attackRange / cellSize));
        float rangeSquared = attackRange * attackRange;
        Vector3 towerCenter = map.CellCenter(towerCell);
        int minY = Mathf.Max(0, towerCell.y - radiusCells);
        int maxY = Mathf.Min(map.Height - 1, towerCell.y + radiusCells);
        int minX = Mathf.Max(0, towerCell.x - radiusCells);
        int maxX = Mathf.Min(map.Width - 1, towerCell.x + radiusCells);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector3 center = map.CellCenter(new Vector2Int(x, y));
            float distanceSquared = (center - towerCenter).sqrMagnitude;
            if (distanceSquared > rangeSquared) continue;
            float falloff = Mathf.Lerp(1f, 0.45f,
                Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / attackRange));
            int index = y * map.Width + x;
            value += _towerDefenseAutoplayGroundValueByCell[index] * falloff;
        }
    }

    private AutoplayPressureChannels GetAutoplayPressureChannels(
        RougeTowerDefenseMap map, Vector2Int towerCell, float attackRange)
    {
        AutoplayPressureChannels channels = default;
        if (map == null || attackRange <= 0f) return channels;
        float cellSize = Mathf.Max(0.1f, map.CellSize);
        int radiusCells = Mathf.Max(1, Mathf.CeilToInt(attackRange / cellSize));
        float rangeSquared = attackRange * attackRange;
        Vector3 towerCenter = map.CellCenter(towerCell);
        int minY = Mathf.Max(0, towerCell.y - radiusCells);
        int maxY = Mathf.Min(map.Height - 1, towerCell.y + radiusCells);
        int minX = Mathf.Max(0, towerCell.x - radiusCells);
        int maxX = Mathf.Min(map.Width - 1, towerCell.x + radiusCells);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector3 center = map.CellCenter(new Vector2Int(x, y));
            float distanceSquared = (center - towerCenter).sqrMagnitude;
            if (distanceSquared > rangeSquared) continue;
            float falloff = Mathf.Lerp(1f, 0.45f,
                Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / attackRange));
            int index = y * map.Width + x;
            channels.Total += _towerDefenseAutoplayEnemyPressureByCell[index] * falloff;
            channels.Crowd += _towerDefenseAutoplayCrowdPressureByCell[index] * falloff;
            channels.Elite += _towerDefenseAutoplayElitePressureByCell[index] * falloff;
            channels.Boss += _towerDefenseAutoplayBossPressureByCell[index] * falloff;
            channels.Urgent += _towerDefenseAutoplayUrgentPressureByCell[index] * falloff;
        }
        return channels;
    }

    private AutoplayPressureChannels GetAutoplayActivePressureChannels(
        RougeTowerDefenseMap map, Vector2Int towerCell, float attackRange)
    {
        AutoplayPressureChannels channels = default;
        if (map == null || attackRange <= 0f) return channels;
        float cellSize = Mathf.Max(0.1f, map.CellSize);
        int radiusCells = Mathf.Max(1, Mathf.CeilToInt(attackRange / cellSize));
        float rangeSquared = attackRange * attackRange;
        Vector3 towerCenter = map.CellCenter(towerCell);
        int minY = Mathf.Max(0, towerCell.y - radiusCells);
        int maxY = Mathf.Min(map.Height - 1, towerCell.y + radiusCells);
        int minX = Mathf.Max(0, towerCell.x - radiusCells);
        int maxX = Mathf.Min(map.Width - 1, towerCell.x + radiusCells);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            Vector3 center = map.CellCenter(new Vector2Int(x, y));
            float distanceSquared = (center - towerCenter).sqrMagnitude;
            if (distanceSquared > rangeSquared) continue;
            float falloff = Mathf.Lerp(1f, 0.45f,
                Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / attackRange));
            int index = y * map.Width + x;
            channels.Crowd +=
                _towerDefenseAutoplayActiveCrowdPressureByCell[index] * falloff;
            channels.Elite +=
                _towerDefenseAutoplayActiveElitePressureByCell[index] * falloff;
            channels.Urgent +=
                _towerDefenseAutoplayActiveUrgentPressureByCell[index] * falloff;
        }
        channels.Total = channels.Crowd + channels.Elite;
        return channels;
    }

    private float GetAutoplayMarginalDefenseValue(RougeTowerDefenseMap map,
        Vector2Int towerCell, float attackRange, RougeTowerType type,
        out float objectiveUncoveredPressure,
        out float styledUncoveredPressure)
    {
        objectiveUncoveredPressure = 0f;
        styledUncoveredPressure = 0f;
        if (map == null || attackRange <= 0f) return 0f;
        int cellCount = map.Width * map.Height;
        int functionGroup = GetAutoplayFunctionGroup(type);
        float cellSize = Mathf.Max(0.1f, map.CellSize);
        int radiusCells = Mathf.Max(1, Mathf.CeilToInt(attackRange / cellSize));
        float rangeSquared = attackRange * attackRange;
        Vector3 towerCenter = map.CellCenter(towerCell);
        float marginalRoute = 0f;
        for (int y = Mathf.Max(0, towerCell.y - radiusCells);
             y <= Mathf.Min(map.Height - 1, towerCell.y + radiusCells); y++)
        for (int x = Mathf.Max(0, towerCell.x - radiusCells);
             x <= Mathf.Min(map.Width - 1, towerCell.x + radiusCells); x++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            if (!map.IsGround(cell)) continue;
            float distanceSquared = (map.CellCenter(cell) - towerCenter).sqrMagnitude;
            if (distanceSquared > rangeSquared) continue;
            float falloff = Mathf.Lerp(1f, 0.42f,
                Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / attackRange));
            int index = y * map.Width + x;
            float overallCoverage = _towerDefenseAutoplayCoverageByCell[index];
            float functionCoverage = _towerDefenseAutoplayFunctionCoverageByCell[
                functionGroup * cellCount + index];
            float routeValue = _towerDefenseAutoplayGroundValueByCell[index];
            marginalRoute += routeValue * falloff /
                             (1f + overallCoverage * 0.28f +
                              functionCoverage * 0.72f);

            AutoplayPressureChannels cellChannels = new AutoplayPressureChannels
            {
                Total = _towerDefenseAutoplayEnemyPressureByCell[index],
                Crowd = _towerDefenseAutoplayCrowdPressureByCell[index],
                Elite = _towerDefenseAutoplayElitePressureByCell[index],
                Boss = _towerDefenseAutoplayBossPressureByCell[index],
                Urgent = _towerDefenseAutoplayUrgentPressureByCell[index]
            };
            float coverageDivisor = 1f + functionCoverage * 0.85f +
                                    overallCoverage * 0.2f;
            objectiveUncoveredPressure += CombineAutoplayPressureForTower(type,
                cellChannels, out _, false) * falloff / coverageDivisor;
            styledUncoveredPressure += CombineAutoplayPressureForTower(type,
                cellChannels, out _, true) * falloff / coverageDivisor;
        }
        return marginalRoute;
    }

    private static float CombineAutoplayPressureForTower(RougeTowerType type,
        AutoplayPressureChannels channels, out AutoplayPressureLayer dominantLayer,
        bool applyPersonality = true)
    {
        float crowdWeight;
        float eliteWeight;
        float bossWeight;
        float urgentWeight;
        if (type == RougeTowerType.Ice)
        {
            crowdWeight = 0.55f;
            eliteWeight = 0.35f;
            bossWeight = 0.25f;
            urgentWeight = 1.35f;
        }
        else if (type == RougeTowerType.MachineGun || type == RougeTowerType.Laser ||
                 type == RougeTowerType.PiercingLaser)
        {
            crowdWeight = 0.28f;
            eliteWeight = 1.05f;
            bossWeight = 1.25f;
            urgentWeight = 0.48f;
        }
        else
        {
            crowdWeight = 1.2f;
            eliteWeight = 0.48f;
            bossWeight = 0.22f;
            urgentWeight = 0.42f;
        }

        float classifiedPressure = channels.Crowd + channels.Elite +
                                   channels.Boss;
        float unclassifiedPressure = Mathf.Max(0f,
            channels.Total - classifiedPressure);
        float totalContribution = unclassifiedPressure * 0.75f;
        float crowdContribution = channels.Crowd * crowdWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.CrowdConcern : 1f);
        float eliteContribution = channels.Elite * eliteWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.EliteConcern : 1f);
        float bossContribution = channels.Boss * bossWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.BossConcern : 1f);
        float baseContribution = totalContribution + crowdContribution +
                                 eliteContribution + bossContribution;
        float urgentRatio = channels.Total > 0.0001f
            ? Mathf.Clamp01(channels.Urgent / channels.Total)
            : 0f;
        float effectiveUrgentWeight = urgentWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.UrgentConcern : 1f);
        float urgentContribution = baseContribution * urgentRatio *
                                   effectiveUrgentWeight * 0.45f;

        dominantLayer = AutoplayPressureLayer.Total;
        float dominantValue = totalContribution;
        if (crowdContribution > dominantValue)
        {
            dominantLayer = AutoplayPressureLayer.Crowd;
            dominantValue = crowdContribution;
        }
        if (eliteContribution > dominantValue)
        {
            dominantLayer = AutoplayPressureLayer.Elite;
            dominantValue = eliteContribution;
        }
        if (bossContribution > dominantValue)
        {
            dominantLayer = AutoplayPressureLayer.Boss;
            dominantValue = bossContribution;
        }
        if (urgentContribution > dominantValue)
            dominantLayer = AutoplayPressureLayer.Urgent;

        return baseContribution + urgentContribution;
    }

    private float GetAutoplayDiversityScore(RougeTowerType type)
    {
        int typeCount = _towerDefenseAutoplayTypeCounts[(int)type];
        int groupCount = _towerDefenseAutoplayFunctionCounts[
            GetAutoplayFunctionGroup(type)];
        // Reward missing battlefield roles, not collecting one of every tower. A
        // composition may legitimately need several focused or several AOE towers.
        float typeDiversity = typeCount == 0 ? 30f : -typeCount * 7f;
        float rolePreference = Mathf.Clamp(
            GetAutoplayPersonalityTowerBias(type), 0.75f, 1.25f);
        float functionDiversity = groupCount == 0
            ? 48f * Mathf.Lerp(0.68f, 1.22f,
                Mathf.InverseLerp(0.75f, 1.25f, rolePreference))
            : groupCount == 1 ? 10f : -Mathf.Max(0, groupCount - 2) * 6f;
        return typeDiversity + functionDiversity;
    }

    private bool HasMissingEnabledAutoplayFunctionGroup()
    {
        for (int group = 0; group < _towerDefenseAutoplayFunctionCounts.Length;
             group++)
            if (_towerDefenseAutoplayFunctionCounts[group] == 0 &&
                HasEnabledAutoplayTowerInFunctionGroup(group))
                return true;
        return false;
    }

    private bool HasEnabledAutoplayTowerInFunctionGroup(int group)
    {
        for (int i = 0; i < TowerDefenseAutoplayBuildOrder.Length; i++)
        {
            RougeTowerType type = TowerDefenseAutoplayBuildOrder[i];
            if (!IsTowerTypeDisabled(type) &&
                GetAutoplayFunctionGroup(type) == group) return true;
        }
        return false;
    }

    private int CountAutoplayTowersOnEffect(RougeTowerType type,
        RougeTowerPlaceEffect effect)
    {
        int count = 0;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (IsAutoplayStandardTower(tower) && tower.TowerType == type &&
                tower.TowerPlaceEffect == effect) count++;
        }
        return count;
    }

    private RougeTowerAiRoleProfile GetAutoplayTowerRoleProfile(
        RougeTowerType type)
    {
        RougeTowerTypeConfig config = towerBalance?.Find(type);
        if (config?.aiRoleProfile != null) return config.aiRoleProfile;
        return null;
    }

    private RougeTowerAiRoleProfile GetAutoplayUpgradeRoleProfile(
        RougeDefenseTower tower, int specializationChoiceIndex)
    {
        if (tower == null) return null;
        RougeTowerTypeConfig config = towerBalance?.Find(tower.TowerType);
        if (config == null) return null;
        if (specializationChoiceIndex >= 0)
        {
            RougeTowerAiRoleProfile specialization =
                config.FindAiSpecialization(tower.Level,
                    tower.SpecializationBranchIndex,
                    specializationChoiceIndex);
            if (specialization != null) return specialization;
        }
        return config.aiRoleProfile;
    }

    private RougeTowerAiRoleProfile GetAutoplayInstalledRoleProfile(
        RougeDefenseTower tower)
    {
        if (tower == null) return null;
        RougeTowerTypeConfig config = towerBalance?.Find(tower.TowerType);
        if (config == null) return null;
        int branch = tower.SpecializationBranchIndex;
        int augmentChoice = tower.SpecializationAugmentChoiceIndex;
        if (tower.Level >= 3 && branch > 0 && augmentChoice >= 0)
        {
            RougeTowerAiRoleProfile augment = config.FindAiSpecialization(
                2, branch, augmentChoice);
            if (augment != null) return augment;
        }
        if (tower.Level >= 2 && branch > 0)
        {
            RougeTowerAiRoleProfile specialization =
                config.FindAiSpecialization(1, 0, branch - 1);
            if (specialization != null) return specialization;
        }
        return config.aiRoleProfile;
    }

    private int GetAutoplayFunctionGroup(RougeTowerType type)
    {
        RougeTowerAiRoleProfile role = GetAutoplayTowerRoleProfile(type);
        if (role == null) return 1;
        float control = role.control;
        float assault = Mathf.Max(role.singleTarget, role.armorBreaking);
        float crowd = role.areaDamage;
        if (control >= assault && control >= crowd) return 0;
        return assault >= crowd ? 1 : 2;
    }

    private float GetAutoplayPersonalityTowerBias(RougeTowerType type)
    {
        return GetAutoplayPersonalityRoleBias(
            GetAutoplayTowerRoleProfile(type));
    }

    private float GetAutoplayPersonalityUpgradeBias(RougeDefenseTower tower,
        int specializationChoiceIndex)
    {
        return GetAutoplayPersonalityRoleBias(
            GetAutoplayUpgradeRoleProfile(tower, specializationChoiceIndex));
    }

    private static float GetAutoplayPersonalityRoleBias(
        RougeTowerAiRoleProfile role)
    {
        if (role == null) return 1f;
        float control = Mathf.Max(0f, role.control);
        float assault = Mathf.Max(0f,
            Mathf.Max(role.singleTarget, role.armorBreaking));
        float crowd = Mathf.Max(0f, role.areaDamage);
        float total = control + assault + crowd;
        if (total <= 0.001f) return 1f;
        return (control * TowerDefenseAutoplayCommander.ControlTowerBias +
                assault * TowerDefenseAutoplayCommander.FocusedTowerBias +
                crowd * TowerDefenseAutoplayCommander.AreaTowerBias) / total;
    }

    private float GetAutoplayAssaultStyleStrength()
    {
        EnsureAutoplayStyleDecisionRolls();
        int[] spectrum = RougeCommanderTacticalSpectrum.Calculate(
            TowerDefenseAutoplayCommander);
        float assaultProbability = GetAdjustedAutoplayStyleFirstProbability(
            spectrum, 2, 5, _towerDefenseAutoplayStyleRoleRatioScale);
        // 10:40 has no meaningful armor-breaking preference, 25:25 is neutral,
        // and 40:10 receives the full specialization bonus.
        return Mathf.InverseLerp(0.2f, 0.8f, assaultProbability);
    }

    private float GetAutoplayAssaultArmorBuildStyleScore(RougeTowerType type,
        AutoplayBattleSnapshot snapshot)
    {
        float affinity = GetAutoplayArmorBreakingBuildAffinity(type);
        if (affinity <= 0f) return 0f;
        return GetAutoplayArmorBreakingOpportunity(snapshot) * affinity *
               GetAutoplayAssaultStyleStrength() * 72f;
    }

    private float GetAutoplayAssaultArmorUpgradeStyleScore(
        RougeDefenseTower tower, int specializationChoiceIndex,
        AutoplayBattleSnapshot snapshot)
    {
        float affinity = GetAutoplayArmorBreakingUpgradeAffinity(tower,
            specializationChoiceIndex);
        if (affinity <= 0f) return 0f;
        return GetAutoplayArmorBreakingOpportunity(snapshot) * affinity *
               GetAutoplayAssaultStyleStrength() * 88f;
    }

    private static float GetAutoplayArmorBreakingOpportunity(
        AutoplayBattleSnapshot snapshot)
    {
        float armorSignal = Mathf.Clamp01(Mathf.Log(1f +
            Mathf.Max(0f, snapshot.UncoveredArmorPressure)) / Mathf.Log(9f));
        float hardTargetSignal = Mathf.Clamp01(
            snapshot.EliteEnemies * 0.28f + snapshot.BossEnemies * 0.8f +
            snapshot.IncomingElitePressure * 0.055f +
            snapshot.BossPreparation * 0.7f);
        // An assault commander may prepare a real armor-breaking route shortly
        // before elites/Bosses arrive, but live uncovered armor remains authoritative.
        return Mathf.Max(armorSignal, hardTargetSignal * 0.35f);
    }

    private float GetAutoplayArmorBreakingBuildAffinity(
        RougeTowerType type)
    {
        RougeTowerAiRoleProfile role = GetAutoplayTowerRoleProfile(type);
        return role != null ? Mathf.Clamp01(role.armorBreaking) : 0f;
    }

    private float GetAutoplayArmorBreakingUpgradeAffinity(
        RougeDefenseTower tower, int specializationChoiceIndex)
    {
        RougeTowerAiRoleProfile role = GetAutoplayUpgradeRoleProfile(tower,
            specializationChoiceIndex);
        return role != null ? Mathf.Clamp01(role.armorBreaking) : 0f;
    }

    private float GetAutoplayThreatFit(RougeTowerType type,
        AutoplayBattleSnapshot snapshot)
    {
        RougeTowerAiRoleProfile role = GetAutoplayTowerRoleProfile(type);
        if (role == null) return 0f;
        float score = 0f;
        if (snapshot.BossEnemies > 0)
            score += role.directDamage *
                Mathf.Lerp(28f, 95f, role.singleTarget);
        int crowd = Mathf.Max(0, snapshot.ActiveEnemies - snapshot.BossEnemies -
            snapshot.EliteEnemies);
        if (crowd >= 8)
            score += Mathf.Max(role.areaDamage * 72f,
                role.control * 48f);
        if (snapshot.EliteEnemies > 0)
            score += Mathf.Max(role.singleTarget,
                role.armorBreaking) * role.directDamage * 42f;
        float armorSignal = Mathf.Clamp01(Mathf.Log(1f +
            snapshot.UncoveredArmorPressure) / Mathf.Log(9f));
        if (armorSignal > 0f)
        {
            // Armor demand reads the normalized capability vector. Raw damage keeps
            // only a small fallback so it cannot masquerade as actual armor breaking.
            score += armorSignal * (role.armorBreaking * 82f +
                role.directDamage * (1f - role.armorBreaking) * 12f);
        }
        float controlSignal = Mathf.Clamp01(Mathf.Log(1f +
            snapshot.FastUncontrolledPressure) / Mathf.Log(7f));
        score += controlSignal * role.control * 88f;
        float vulnerableSignal = Mathf.Clamp01(Mathf.Log(1f +
            snapshot.VulnerablePressure) / Mathf.Log(9f));
        score += vulnerableSignal * role.directDamage * 28f;
        return score;
    }

    private bool IsAutoplayTowerAlignedWithThreat(RougeTowerType type,
        AutoplayBattleSnapshot snapshot)
    {
        RougeTowerAiRoleProfile role = GetAutoplayTowerRoleProfile(type);
        if (role == null) return false;
        if ((snapshot.BossEnemies > 0 || snapshot.BossPreparation >= 0.32f ||
             snapshot.IncomingElitePressure >= 4f) &&
            Mathf.Max(role.singleTarget, role.armorBreaking) >= 0.5f)
            return true;
        if ((snapshot.ActiveEnemies >= 10 || snapshot.IncomingCrowdPressure >= 7f) &&
            Mathf.Max(role.areaDamage, role.control) >= 0.5f)
            return true;
        if (snapshot.UncoveredArmorPressure >= 1.25f &&
            role.armorBreaking >= 0.35f) return true;
        if (snapshot.FastUncontrolledPressure >= 0.9f &&
            role.control >= 0.5f) return true;
        return snapshot.UrgentPressure >= 2f &&
               Mathf.Max(role.control, role.directDamage) >= 0.75f;
    }

    private bool IsAutoplayBossDamageTower(RougeTowerType type)
    {
        RougeTowerAiRoleProfile role = GetAutoplayTowerRoleProfile(type);
        return role != null && role.directDamage >= 0.75f &&
               role.singleTarget >= 0.5f;
    }

    private bool IsAutoplayControlTower(RougeTowerType type)
    {
        RougeTowerAiRoleProfile role = GetAutoplayTowerRoleProfile(type);
        return role != null && role.control >= 0.5f;
    }

    private static bool IsAutoplayBossInvestmentStage(
        AutoplayBattleSnapshot snapshot)
    {
        return snapshot.BossEnemies > 0 || snapshot.BossPreparation >=
            TowerDefenseAutoplayThresholds.prepareBossProgress;
    }

    private static float GetAutoplayBossPreparationCommitment(
        AutoplayBattleSnapshot snapshot)
    {
        if (snapshot.BossEnemies > 0) return 1f;
        float threshold = TowerDefenseAutoplayThresholds.prepareBossProgress;
        if (snapshot.BossPreparation < threshold) return 0f;
        float progress = Mathf.InverseLerp(threshold, 1f,
            snapshot.BossPreparation);
        return Mathf.Lerp(0.42f, 1f, Mathf.SmoothStep(0f, 1f, progress));
    }

    private bool IsAutoplayBossCombatMarketOpen(
        AutoplayBattleSnapshot snapshot)
    {
        return IsAutoplayBossInvestmentStage(snapshot) &&
               _towerDefenseAutoplayBossCombatNeed > 0.05f;
    }

    private bool TryGetAutoplayBossRouteStart(RougeTowerDefenseMap map,
        out Vector2Int start)
    {
        start = default;
        if (map == null) return false;
        if (map.HasBossSpawn)
        {
            Vector2Int authored = map.BossSpawnCell;
            if ((uint)authored.x < (uint)map.Width &&
                (uint)authored.y < (uint)map.Height && map.IsGround(authored))
            {
                start = authored;
                return true;
            }
        }
        return bossSpawnPoint != null &&
               map.WorldToCell(bossSpawnPoint.transform.position, out start) &&
               map.IsGround(start);
    }

    private float GetAutoplayBossRouteCoverage(RougeTowerDefenseMap map,
        Vector3 towerPosition, float attackRange)
    {
        if (map == null || attackRange <= 0f ||
            _towerDefenseAutoplayBossRouteCellCount <= 0)
            return 0f;

        float coveredLength = 0f;
        float routeLength = 0f;
        float rangeSquared = attackRange * attackRange;
        for (int routeOffset = 0;
             routeOffset < _towerDefenseAutoplayBossRouteCellCount;
             routeOffset++)
        {
            int index = _towerDefenseAutoplayBossRouteCells[routeOffset];
            Vector2Int current = new Vector2Int(index % map.Width,
                index / map.Width);
            float segmentLength = GetAutoplayBossRouteSegmentLengthCells(
                routeOffset, map.Width);
            routeLength += segmentLength;
            Vector3 delta = map.CellCenter(current) - towerPosition;
            delta.y = 0f;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared <= rangeSquared)
            {
                float falloff = Mathf.Lerp(1f, 0.45f,
                    Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / attackRange));
                coveredLength += segmentLength * falloff;
            }
        }
        return routeLength > 0.001f
            ? Mathf.Clamp01(coveredLength / routeLength)
            : 0f;
    }

    private bool CanAutoplayReachBossRouteWithControl(
        RougeTowerDefenseMap map)
    {
        if (map == null || IsTowerTypeDisabled(RougeTowerType.Ice) ||
            bossBalance == null || bossBalance.maximumSlowPercent <= 0f)
            return false;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) ||
                tower.TowerType != RougeTowerType.Ice ||
                !tower.CanUpgrade) continue;
            RougeTowerStats currentStats = TowerDefenseVisuals.GetStats(
                RougeTowerType.Ice, tower.Level);
            RougeTowerStats maximumStats = TowerDefenseVisuals.GetStats(
                RougeTowerType.Ice, TowerDefenseVisuals.MaxTowerLevel);
            float maximumRange = tower.AttackRange * maximumStats.AttackRadius /
                Mathf.Max(0.1f, currentStats.AttackRadius);
            if (GetAutoplayBossRouteCoverage(map, tower.transform.position,
                    maximumRange) > 0.001f) return true;
        }

        int cellCount = map.Width * map.Height;
        int priorOffset = (int)RougeTowerType.Ice * cellCount;
        for (int index = 0; index < cellCount; index++)
        {
            if (!_towerDefenseAutoplayBuildableTopology[index] ||
                _towerDefenseAutoplayOccupiedCells[index]) continue;
            int priorIndex = priorOffset + index;
            if ((uint)priorIndex >=
                (uint)_towerDefenseAutoplayBuildPriors.Length) continue;
            AutoplayBuildPrior prior = _towerDefenseAutoplayBuildPriors[priorIndex];
            if (prior.IsValid && prior.BossRouteCoverage > 0.001f) return true;
        }
        return false;
    }

    private void UpdateAutoplayBossReadinessUrgency(
        RougeTowerDefenseMap map, AutoplayBattleSnapshot snapshot,
        bool authoritativeLiveBoss)
    {
        _towerDefenseAutoplayBossReadinessUrgency = 1f;
        _towerDefenseAutoplayBossPowerDeficit = 0f;
        _towerDefenseAutoplayBossControlDeficit = 0f;
        _towerDefenseAutoplayBossCombatNeed = 0f;
        _towerDefenseAutoplayBossRequiredPower = 1f;
        bool investmentStage = authoritativeLiveBoss ||
            IsAutoplayBossInvestmentStage(snapshot);
        if (!investmentStage || map == null ||
            _towerDefenseAutoplayBossRouteCellCount <= 0) return;

        int cellCount = map.Width * map.Height;
        int controlOffset = cellCount * GetAutoplayFunctionGroup(
            RougeTowerType.Ice);
        float controlledLength = 0f;
        float routeLength = 0f;
        for (int routeOffset = 0;
             routeOffset < _towerDefenseAutoplayBossRouteCellCount;
             routeOffset++)
        {
            int index = _towerDefenseAutoplayBossRouteCells[routeOffset];
            float segmentLength = GetAutoplayBossRouteSegmentLengthCells(
                routeOffset, map.Width);
            float control = (uint)(controlOffset + index) <
                            (uint)_towerDefenseAutoplayFunctionCoverageByCell.Length
                ? _towerDefenseAutoplayFunctionCoverageByCell[controlOffset + index]
                : 0f;
            controlledLength += segmentLength *
                (1f - Mathf.Exp(-Mathf.Max(0f, control) * 0.85f));
            routeLength += segmentLength;
        }
        if (routeLength <= 0.001f) return;

        float controlCoverage = Mathf.Clamp01(controlledLength / routeLength);
        bool controlReachable = CanAutoplayReachBossRouteWithControl(map);
        float maximumSlow = bossBalance != null
            ? Mathf.Clamp01(bossBalance.maximumSlowPercent * 0.01f)
            : 0f;
        if (controlReachable && maximumSlow > 0.001f)
        {
            // Target an average route slow of at most ten percent. This is a route
            // coverage requirement derived from the Boss's actual slow cap, not an
            // Ice-tower count quota.
            float targetAverageSlow = Mathf.Min(0.1f, maximumSlow * 0.6f);
            float desiredControlCoverage = Mathf.Clamp01(
                targetAverageSlow / maximumSlow);
            _towerDefenseAutoplayBossControlDeficit = Mathf.Clamp01(1f -
                controlCoverage / Mathf.Max(0.001f, desiredControlCoverage));
        }

        float installedRoutePower = 0f;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) ||
                !IsAutoplayBossDamageTower(tower.TowerType) ||
                tower.UsesRotatingFlamethrower) continue;
            float routeCoverage = GetAutoplayBossRouteCoverage(map,
                tower.transform.position, tower.AttackRange);
            installedRoutePower +=
                EstimateAutoplayInstalledSingleTargetPower(tower) * routeCoverage;
        }

        float controlledTravelMultiplier = 1f + controlCoverage * maximumSlow /
            Mathf.Max(0.05f, 1f - maximumSlow);
        float travelSeconds = bossBalance != null
            ? Mathf.Max(30f, bossBalance.targetTravelTimeSeconds)
            : 360f;
        _towerDefenseAutoplayBossRequiredPower = Mathf.Max(1f,
            GetCurrentBossMaxHealth() /
            Mathf.Max(30f, travelSeconds * controlledTravelMultiplier));
        _towerDefenseAutoplayBossPowerDeficit = Mathf.Clamp01(1f -
            installedRoutePower / _towerDefenseAutoplayBossRequiredPower);
        _towerDefenseAutoplayBossCombatNeed = Mathf.Max(
            _towerDefenseAutoplayBossPowerDeficit,
            _towerDefenseAutoplayBossControlDeficit * 0.9f);
        _towerDefenseAutoplayBossReadinessUrgency = Mathf.Clamp(
            1f + _towerDefenseAutoplayBossPowerDeficit * 1.05f +
            _towerDefenseAutoplayBossControlDeficit * 0.55f, 1f, 2.6f);
    }

    private float GetAutoplayBossPowerInvestmentScore(float routeCoverage,
        float marginalSingleTargetPower)
    {
        if (_towerDefenseAutoplayBossPowerDeficit <= 0f ||
            routeCoverage <= 0f || marginalSingleTargetPower <= 0f) return 0f;
        float effectivePower = marginalSingleTargetPower * routeCoverage;
        float requiredShare = effectivePower /
            Mathf.Max(1f, _towerDefenseAutoplayBossRequiredPower);
        return _towerDefenseAutoplayBossPowerDeficit *
            (Mathf.Log(1f + effectivePower) * 22f +
             Mathf.Sqrt(Mathf.Clamp01(requiredShare)) * 180f +
             routeCoverage * 32f);
    }

    private float GetAutoplayBossControlInvestmentScore(
        float projectedRouteCoverage, float marginalRouteCoverage)
    {
        if (_towerDefenseAutoplayBossControlDeficit <= 0f ||
            projectedRouteCoverage <= 0f) return 0f;
        return _towerDefenseAutoplayBossControlDeficit *
            (45f + projectedRouteCoverage * 160f +
             Mathf.Max(0f, marginalRouteCoverage) * 220f);
    }

    private void AccumulateAutoplayBossRoutePressure(
        RougeTowerDefenseMap map, AutoplayBattleSnapshot snapshot,
        bool authoritativeLiveBoss)
    {
        bool investmentStage = authoritativeLiveBoss ||
                               IsAutoplayBossInvestmentStage(snapshot);
        if (!investmentStage || _towerDefenseAutoplayBossCombatNeed <= 0.05f ||
            map == null || _towerDefenseAutoplayBossRouteCellCount <= 0) return;
        float commitment = authoritativeLiveBoss
            ? 1f
            : GetAutoplayBossPreparationCommitment(snapshot);
        float routePressure = Mathf.Lerp(7f, 18f, commitment) *
            Mathf.Lerp(0.65f, 1f, _towerDefenseAutoplayBossCombatNeed);
        for (int routeOffset = 0;
             routeOffset < _towerDefenseAutoplayBossRouteCellCount;
             routeOffset++)
        {
            int index = _towerDefenseAutoplayBossRouteCells[routeOffset];
            AddAutoplayPressureToCell(index, routePressure, 0f, 0f,
                routePressure, 0f);
        }
    }

    private float GetAutoplayBossReadinessUrgency()
    {
        return _towerDefenseAutoplayBossReadinessUrgency;
    }

    private static float EstimateAutoplayCombatPower(RougeTowerType type,
        RougeTowerStats stats, RougeTowerBuffLevels buffs,
        bool includeIceControl = true)
    {
        float damage = stats.Damage * RougeTowerBuffMath.GetMultiplier(buffs.Damage);
        float speed = RougeTowerBuffMath.GetMultiplier(buffs.AttackSpeed);
        float interval = stats.AttackInterval / Mathf.Max(0.01f, speed);
        int targets = Mathf.Max(1, stats.TargetCount);
        int projectiles = Mathf.Max(1, stats.ProjectileCount);
        switch (type)
        {
            case RougeTowerType.MachineGun:
                // Extra barrels fan out rather than creating guaranteed independent
                // hits. Crowd density decides the remaining value at decision time.
                return damage / Mathf.Max(0.03f, interval) *
                       (1f + Mathf.Max(0, targets - 1) * 0.55f);
            case RougeTowerType.Ice:
            {
                float direct = damage / Mathf.Max(0.03f, interval);
                return direct + (includeIceControl ? 70f : 0f);
            }
            case RougeTowerType.Cannon:
            {
                float burstTime = Mathf.Max(0, projectiles - 1) *
                                  TowerProjectileBurstInterval /
                                  Mathf.Max(0.01f, speed);
                return damage * projectiles /
                       Mathf.Max(0.03f, interval + burstTime);
            }
            case RougeTowerType.Flame:
            {
                int ticks = stats.TickInterval > 0f
                    ? Mathf.Max(1, Mathf.CeilToInt(stats.EffectDuration /
                                                  stats.TickInterval))
                    : 1;
                float burstTime = Mathf.Max(0, projectiles - 1) *
                                  TowerProjectileBurstInterval /
                                  Mathf.Max(0.01f, speed);
                return damage * ticks * projectiles /
                       Mathf.Max(0.03f, interval + burstTime);
            }
            case RougeTowerType.Laser:
                return damage / Mathf.Max(0.03f, interval) * targets;
            case RougeTowerType.PiercingLaser:
            {
                float cycle = interval + PiercingLaserChargeDuration /
                    Mathf.Max(0.01f, speed) + PiercingLaserFireDuration;
                return damage / Mathf.Max(0.03f, cycle);
            }
            case RougeTowerType.OrbitSphere:
            {
                float range = stats.AttackRadius *
                              RougeTowerBuffMath.GetMultiplier(buffs.Range);
                float minimumRadius = stats.OrbitSphereRadius * 1.5f;
                float travel = 2f * Mathf.Max(0f, range - minimumRadius) /
                               Mathf.Max(0.01f,
                                   stats.OrbitRadialSpeed * speed);
                float active = travel + stats.OrbitOuterHoldDuration;
                float duty = active /
                    Mathf.Max(0.03f, active + interval);
                float tick = stats.TickInterval /
                             Mathf.Max(0.01f, speed);
                return damage / Mathf.Max(0.02f, tick) * projectiles * duty;
            }
            case RougeTowerType.RocketBarrage:
            {
                float salvo = Mathf.Max(0, projectiles - 1) *
                              stats.ProjectileInterval /
                              Mathf.Max(0.01f, speed);
                float cycle = Mathf.Max(interval, salvo);
                return damage * projectiles / Mathf.Max(0.03f, cycle);
            }
            default:
                return damage / Mathf.Max(0.03f, interval);
        }
    }

    private static float GetAutoplayPressureRealizationFactor(float combatPower)
    {
        // A candidate may cover a hotspot geometrically without having enough output
        // to serve all of it. Keep a floor for control/utility actions, then approach
        // full pressure credit only as sustained capacity rises.
        float capacity = 1f - Mathf.Exp(-Mathf.Max(0f, combatPower) / 115f);
        return Mathf.Lerp(0.28f, 1f, capacity);
    }

    private static float EstimateAutoplaySingleTargetPower(RougeTowerType type,
        RougeTowerStats stats, RougeTowerBuffLevels buffs)
    {
        return EstimateAutoplayCombatPower(type, stats, buffs, false);
    }

    private static float GetAutoplayInstalledUpgradeRealization(
        RougeDefenseTower tower)
    {
        if (tower == null) return 1f;
        RougeTowerStats baseStats = TowerDefenseVisuals.GetStats(
            tower.TowerType, tower.Level);
        float baseline = Mathf.Max(0.01f,
            EstimateAutoplayCombatPower(tower.TowerType, baseStats, default,
                false));
        float installed = Mathf.Max(0.01f,
            EstimateAutoplayInstalledCombatPower(tower));
        return Mathf.Clamp(installed / baseline, 0.65f, 3f);
    }

    private static float EstimateAutoplayInstalledCombatPower(
        RougeDefenseTower tower)
    {
        if (tower == null) return 0f;
        float damage = Mathf.Max(0f, tower.Damage);
        float interval = Mathf.Max(0.03f, tower.EffectiveAttackInterval);
        float speed = Mathf.Max(0.01f, tower.AttackSpeedMultiplier);
        int targets = Mathf.Max(1, tower.AttackTargetCount);
        int projectiles = Mathf.Max(1, tower.AttackProjectileCount);
        switch (tower.TowerType)
        {
            case RougeTowerType.MachineGun:
                return damage / interval *
                       (1f + Mathf.Max(0, targets - 1) * 0.55f);
            case RougeTowerType.Ice:
                return damage / interval;
            case RougeTowerType.Cannon:
            {
                float burstTime = Mathf.Max(0, projectiles - 1) *
                                  TowerProjectileBurstInterval / speed;
                return damage * projectiles /
                       Mathf.Max(0.03f, interval + burstTime);
            }
            case RougeTowerType.Flame:
            {
                if (tower.UsesFlamethrower) return damage / interval;
                int ticks = tower.TickInterval > 0f
                    ? Mathf.Max(1, Mathf.CeilToInt(tower.EffectDuration /
                                                  tower.TickInterval))
                    : 1;
                float burstTime = Mathf.Max(0, projectiles - 1) *
                                  TowerProjectileBurstInterval / speed;
                return damage * ticks * projectiles /
                       Mathf.Max(0.03f, interval + burstTime);
            }
            case RougeTowerType.Laser:
                return damage / interval * targets;
            case RougeTowerType.PiercingLaser:
                return damage / Mathf.Max(0.03f,
                    interval + PiercingLaserChargeDuration / speed +
                    PiercingLaserFireDuration);
            case RougeTowerType.OrbitSphere:
            {
                float minimumRadius = tower.OrbitSphereRadius * 1.5f;
                float travel = 2f * Mathf.Max(0f,
                    tower.AttackRange - minimumRadius) /
                    Mathf.Max(0.01f, tower.OrbitRadialSpeed * speed);
                float active = travel + tower.OrbitOuterHoldDuration;
                float duty = active / Mathf.Max(0.03f, active + interval);
                float tick = tower.TickInterval / speed;
                return damage / Mathf.Max(0.02f, tick) * projectiles * duty;
            }
            case RougeTowerType.RocketBarrage:
            {
                float salvo = Mathf.Max(0, projectiles - 1) *
                              tower.ProjectileInterval / speed;
                float cycle = Mathf.Max(interval, salvo);
                return damage * projectiles / Mathf.Max(0.03f, cycle);
            }
            default:
                return damage / interval;
        }
    }

    private static float EstimateAutoplayInstalledSingleTargetPower(
        RougeDefenseTower tower)
    {
        if (tower == null) return 0f;
        float damage = Mathf.Max(0f, tower.Damage);
        float interval = Mathf.Max(0.03f, tower.EffectiveAttackInterval);
        int targets = Mathf.Max(1, tower.AttackTargetCount);
        int projectiles = Mathf.Max(1, tower.AttackProjectileCount);
        switch (tower.TowerType)
        {
            case RougeTowerType.MachineGun:
                return damage / interval *
                       (1f + Mathf.Max(0, targets - 1) * 0.55f);
            case RougeTowerType.Laser:
                return damage / interval * targets;
            case RougeTowerType.PiercingLaser:
                return damage / Mathf.Max(0.03f,
                    interval + PiercingLaserChargeDuration +
                    PiercingLaserFireDuration);
            case RougeTowerType.Flame:
                if (tower.UsesFlamethrower) return damage / interval;
                int ticks = tower.TickInterval > 0f
                    ? Mathf.Max(1, Mathf.CeilToInt(tower.EffectDuration /
                                                  tower.TickInterval))
                    : 1;
                float burstTime = Mathf.Max(0, projectiles - 1) *
                                  TowerProjectileBurstInterval;
                return damage * ticks * projectiles /
                       Mathf.Max(0.03f, interval + burstTime);
            default:
                return damage / interval * projectiles;
        }
    }

    private static float GetAutoplayOpportunityPenalty(RougeTowerType type,
        RougeTowerPlaceEffect effect)
    {
        if (!IsAutoplayDedicatedEffect(effect)) return 0f;
        float selectedAffinity = GetAutoplayTileAffinity(type, effect);
        float bestAffinity = selectedAffinity;
        for (int i = 0; i < TowerDefenseVisuals.StandardTowerTypeCount; i++)
            bestAffinity = Mathf.Max(bestAffinity,
                GetAutoplayTileAffinity((RougeTowerType)i, effect));
        float gap = bestAffinity - selectedAffinity;
        return gap <= 5f ? 0f : 185f + gap * 4.2f;
    }

    private static bool IsAutoplayDedicatedEffect(RougeTowerPlaceEffect effect)
    {
        return effect == RougeTowerPlaceEffect.DamageAmplifier ||
               effect == RougeTowerPlaceEffect.RangeAmplifier ||
               effect == RougeTowerPlaceEffect.AttackSpeedAmplifier ||
               effect == RougeTowerPlaceEffect.Bounty ||
               effect == RougeTowerPlaceEffect.Echo ||
               effect == RougeTowerPlaceEffect.AccumulatedWealth ||
               effect == RougeTowerPlaceEffect.Explosion ||
               effect == RougeTowerPlaceEffect.Frost;
    }

    private static bool IsAutoplayLongTermEconomyEffect(
        RougeTowerPlaceEffect effect)
    {
        return effect == RougeTowerPlaceEffect.Bounty ||
               effect == RougeTowerPlaceEffect.AccumulatedWealth;
    }

    private int CalculateAutoplayContingencyReserve()
    {
        int minimumResponseCost = int.MaxValue;
        for (int i = 0; i < _towerDefenseAutoplayBuildChoiceScratch.Count; i++)
        {
            int paidCost = _towerDefenseAutoplayBuildChoiceScratch[i].PaidCost;
            if (paidCost > 0)
                minimumResponseCost = Mathf.Min(minimumResponseCost, paidCost);
        }
        if (minimumResponseCost == int.MaxValue) return 0;

        int[] spectrum = RougeCommanderTacticalSpectrum.Calculate(
            TowerDefenseAutoplayCommander);
        float saveStyle = spectrum.Length > 0
            ? Mathf.Clamp01(spectrum[0] / 50f)
            : 0.5f;
        int target = Mathf.RoundToInt(minimumResponseCost *
            Mathf.Lerp(0.75f, 1.5f, saveStyle));
        return Mathf.Clamp(target, 0, _towerDefenseGold);
    }

    private static bool IsAutoplayContingencyReserveWorthyUpgrade(
        AutoplayUpgradeChoice choice)
    {
        if (!choice.IsValid || choice.Tower == null) return false;
        if (choice.PaidCost <= 0) return true;
        bool provenOutput = choice.ObservedCoreConfidence >= 0.65f &&
                            choice.ObservedCoreRate > 0.001f;
        bool premiumPlacement = GetAutoplayTileAffinity(choice.Tower.TowerType,
            choice.Tower.TowerPlaceEffect) >= 105f;
        return choice.ObjectiveCapitalScore >= 0.9f ||
               choice.ObjectiveCapitalScore >= 0.8f &&
               (provenOutput || premiumPlacement);
    }

    private bool ShouldSaveForAutoplayBuild(AutoplayBuildChoice bestOverall,
        AutoplayBuildChoice bestAffordable)
    {
        if (!bestOverall.IsValid || bestOverall.PaidCost <= 0) return false;
        if (!bestAffordable.IsValid) return true;
        // Spending on a near-optimal tower now is safer than waiting through another
        // wave for a tiny efficiency gain. This also prevents personality nudges from
        // turning into long, fragile hoarding plans.
        if (bestAffordable.ObjectiveCapitalScore >=
            bestOverall.ObjectiveCapitalScore *
            (1f - TowerDefenseAutoplayPersonalityRegretBudget))
            return false;
        int shortfall = Mathf.Max(0, bestOverall.PaidCost - _towerDefenseGold);
        int acceptableShortfall = Mathf.Max(120,
            Mathf.RoundToInt(_towerDefenseGold * 0.5f));
        float qualityThreshold = 1.34f /
            Mathf.Max(0.9f, TowerDefenseAutoplayCommander.SaveBias);
        return bestOverall.PaidCost > bestAffordable.PaidCost &&
               shortfall <= acceptableShortfall &&
               bestOverall.CapitalScore > bestAffordable.CapitalScore *
                    qualityThreshold;
    }

    private string DescribeAutoplaySavingPlan(AutoplayBuildChoice choice,
        string reason)
    {
        if (!choice.IsValid) return $"{reason}，但当前没有可用塔位，保留金币。";
        QueueAutoplayDialogue(AutoplayDialogueCategory.Saving);
        int shortfall = Mathf.Max(0, choice.PaidCost - _towerDefenseGold);
        string effect = choice.PlaceEffect == RougeTowerPlaceEffect.None
            ? "合适的普通塔位"
            : GetTowerPlaceEffectShortName(choice.PlaceEffect);
        return shortfall > 0
            ? $"先攒钱：想在{effect}放 {TowerDefenseVisuals.GetTowerName(choice.Type)}，" +
              $"还差 {shortfall} 金币。"
            : $"先等等：{reason}，现在不急着买次优方案。";
    }

    private string DescribeAutoplaySavingPlan(AutoplayUpgradeChoice choice)
    {
        if (!choice.IsValid || choice.Tower == null)
            return "当前没有可升级目标，保留金币。";
        QueueAutoplayDialogue(AutoplayDialogueCategory.Saving);
        int shortfall = Mathf.Max(0, choice.PaidCost - _towerDefenseGold);
        return shortfall > 0
            ? $"先攒钱：{choice.Tower.DisplayName} 下一次升级还差 {shortfall} 金币。"
            : $"先留着钱：{choice.Tower.DisplayName} 是下一步升级候选。";
    }

    private static string DescribeAutoplayBuildReasons(AutoplayBuildChoice choice)
    {
        string tile = choice.PlaceEffect == RougeTowerPlaceEffect.None
            ? "普通塔位"
            : GetTowerPlaceEffectShortName(choice.PlaceEffect);
        string guard = choice.GoalDefenseScore >= 145f
            ? "，能覆盖主塔附近"
            : string.Empty;
        return $"{tile}适合这座塔，主要应对" +
               $"{GetAutoplayPressureLayerLabel(choice.DominantPressureLayer)}{guard}";
    }

    private static string GetAutoplayPressureLayerLabel(
        AutoplayPressureLayer layer)
    {
        switch (layer)
        {
            case AutoplayPressureLayer.Crowd: return "怪群";
            case AutoplayPressureLayer.Elite: return "精英/重甲";
            case AutoplayPressureLayer.Boss: return "Boss";
            case AutoplayPressureLayer.Urgent: return "主塔近端";
            default: return "整体";
        }
    }

    private static int GetTowerDefenseAutoplayPaidCost(int originalCost)
    {
        if (originalCost <= 0) return 0;
        return Mathf.Max(1, Mathf.CeilToInt(originalCost *
            TowerDefenseAutoplayCommander.CostMultiplier));
    }

    private static string FormatAutoplayCost(int originalCost, int paidCost)
    {
        int saved = Mathf.Max(0, originalCost - paidCost);
        return originalCost <= 0
            ? "免费"
            : saved > 0
                ? $"花了 {paidCost} 金币，省下 {saved}"
                : $"花了 {paidCost} 金币";
    }


    private static float GetAutoplayPriorityTileBonus(float tileScore)
    {
        if (tileScore <= 0f) return 0f;
        // Special tiles matter, but crossing an arbitrary score threshold must not
        // suddenly be worth another whole tower. Keep the preference continuous so
        // geometry and actual output can still overturn a merely decent pairing.
        return Mathf.Lerp(12f, 72f,
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(55f, 130f, tileScore)));
    }

    private static float GetAutoplayTileAffinity(RougeTowerType type,
        RougeTowerPlaceEffect effect)
    {
        switch (effect)
        {
            case RougeTowerPlaceEffect.PremiumAmplifier:
                return 130f;
            case RougeTowerPlaceEffect.FreeLevelNoRefund:
                return 118f;
            case RougeTowerPlaceEffect.Discount:
                return 108f;
            case RougeTowerPlaceEffect.DamageAmplifier:
                return type == RougeTowerType.MachineGun ? 126f
                    : type == RougeTowerType.Laser ? 124f
                    : type == RougeTowerType.PiercingLaser ? 122f
                    : type == RougeTowerType.RocketBarrage ? 122f
                    : type == RougeTowerType.Cannon || type == RougeTowerType.Flame
                        ? 121f
                        : 82f;
            case RougeTowerPlaceEffect.RangeAmplifier:
                return type == RougeTowerType.Ice ? 126f
                    : type == RougeTowerType.OrbitSphere ? 123f
                    : type == RougeTowerType.Flame ? 121f
                    : type == RougeTowerType.PiercingLaser ? 118f
                    : type == RougeTowerType.RocketBarrage ? 42f
                    : 78f;
            case RougeTowerPlaceEffect.AttackSpeedAmplifier:
                // Slow, heavy attack cycles gain the most tactical value from attack
                // speed. Machine gun and laser already fire rapidly and pay the same
                // severe range penalty, so they should not monopolize this pad.
                return type == RougeTowerType.PiercingLaser ? 126f
                    : type == RougeTowerType.Cannon ? 124f
                    : type == RougeTowerType.RocketBarrage ? 123f
                    : type == RougeTowerType.Flame ? 122f
                    : 76f;
            case RougeTowerPlaceEffect.Bounty:
                return type == RougeTowerType.MachineGun || type == RougeTowerType.Laser
                    ? 104f : 72f;
            case RougeTowerPlaceEffect.Echo:
                // Most towers repeat the complete attack (roughly +100%). Machine gun,
                // laser and rocket instead receive only 1.5x targets/projectiles, so
                // treating those three as the best Echo users had the ranking reversed.
                return type == RougeTowerType.PiercingLaser ? 126f
                    : type == RougeTowerType.Cannon ? 124f
                    : type == RougeTowerType.Flame ? 122f
                    : type == RougeTowerType.OrbitSphere ? 121f
                    : type == RougeTowerType.Ice ? 116f
                    : 96f;
            case RougeTowerPlaceEffect.AccumulatedWealth:
                return type == RougeTowerType.MachineGun ? 96f : 68f;
            case RougeTowerPlaceEffect.Explosion:
                return type == RougeTowerType.MachineGun || type == RougeTowerType.Flame
                    ? 98f : 72f;
            case RougeTowerPlaceEffect.Frost:
                // Frost is most valuable on frequent direct hits. Ice already supplies
                // stronger native control, while area attacks receive only half slow.
                return type == RougeTowerType.MachineGun ? 126f
                    : type == RougeTowerType.Laser ? 124f
                    : type == RougeTowerType.PiercingLaser ? 110f
                    : type == RougeTowerType.Ice ? 62f
                    : 88f;
            case RougeTowerPlaceEffect.Relocation:
                return 58f;
            default:
                return 0f;
        }
    }

    private bool IsAutoplayBuildCellFree(RougeTowerDefenseMap map, Vector2Int cell)
    {
        if (map == null || !map.IsTowerPlace(cell)) return false;
        if (mainTower != null && map.WorldToCell(mainTower.transform.position,
                out Vector2Int mainCell) && mainCell == cell) return false;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower != null && map.WorldToCell(tower.transform.position,
                    out Vector2Int towerCell) && towerCell == cell) return false;
        }
        return true;
    }

    private int CountAutoplayBuildCells(RougeTowerDefenseMap map)
    {
        if (map == null) return 0;
        // Capacity for standard towers equals those already installed plus genuinely
        // free tower cells; special towers and the main tower consume a slot too.
        int count = CountAutoplayStandardTowers();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            Vector2Int cell = new Vector2Int(x, y);
            int cellIndex = y * map.Width + x;
            if (map.IsTowerPlace(cell) &&
                cellIndex < _towerDefenseAutoplayOccupiedCells.Length &&
                !_towerDefenseAutoplayOccupiedCells[cellIndex]) count++;
        }
        return count;
    }

    private int CountOpenAutoplayBuildCells(RougeTowerDefenseMap map)
    {
        if (map == null) return 0;
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            int index = y * map.Width + x;
            if ((uint)index < (uint)_towerDefenseAutoplayBuildableTopology.Length &&
                _towerDefenseAutoplayBuildableTopology[index] &&
                (uint)index < (uint)_towerDefenseAutoplayOccupiedCells.Length &&
                !_towerDefenseAutoplayOccupiedCells[index]) count++;
        }
        return count;
    }

    private int CountAutoplayStandardTowers()
    {
        int count = 0;
        for (int i = 0; i < _defenseTowers.Count; i++)
            if (IsAutoplayStandardTower(_defenseTowers[i])) count++;
        return count;
    }

    private void ResetAutoplayExpansionSchedule(int standardTowerCount,
        float gameTime)
    {
        _towerDefenseAutoplayExpansionBaselineTowerCount = Mathf.Max(0,
            standardTowerCount);
        _towerDefenseAutoplayNextExpansionGameTime = gameTime +
            TowerDefenseAutoplayExpansionInterval;
    }

    private void AdvanceAutoplayExpansionSchedule(int standardTowerCount,
        float gameTime)
    {
        if (float.IsPositiveInfinity(
                _towerDefenseAutoplayNextExpansionGameTime))
        {
            _towerDefenseAutoplayExpansionBaselineTowerCount = Mathf.Max(0,
                standardTowerCount);
            _towerDefenseAutoplayNextExpansionGameTime =
                gameTime + TowerDefenseAutoplayExpansionInterval;
            return;
        }

        if (gameTime < _towerDefenseAutoplayNextExpansionGameTime) return;

        if (standardTowerCount > _towerDefenseAutoplayExpansionBaselineTowerCount)
        {
            // Any role- or lane-driven construction already delivered the growth this
            // timer would have requested. Rebaseline to the real formation instead of
            // preserving a hidden count debt from the old three-tower opening cap.
            _towerDefenseAutoplayExpansionBaselineTowerCount = standardTowerCount;
            _towerDefenseAutoplayNextExpansionGameTime =
                gameTime + TowerDefenseAutoplayExpansionInterval;
        }
    }

    private void DeferAutoplayExpansionSchedule(float gameTime)
    {
        // The timer opens a planning window; it is not a debt that must be paid with a
        // tower. Recheck soon enough to react to a changed wave without hammering the
        // same skipped expansion on every 0.65-second capital tick.
        float retrySeconds = Mathf.Clamp(TowerDefenseAutoplayExpansionInterval * 0.35f,
            5f, 20f);
        _towerDefenseAutoplayNextExpansionGameTime = gameTime + retrySeconds;
    }

    private bool HasAutoplayUpgradeableTower()
    {
        for (int i = 0; i < _defenseTowers.Count; i++)
            if (IsAutoplayStandardTower(_defenseTowers[i]) &&
                _defenseTowers[i].CanUpgrade) return true;
        return false;
    }

    private static bool IsAutoplayStandardTower(RougeDefenseTower tower)
    {
        return tower != null && (uint)(int)tower.TowerType <
            TowerDefenseVisuals.StandardTowerTypeCount;
    }

    private void PruneAutoplayTowerList()
    {
        for (int i = _defenseTowers.Count - 1; i >= 0; i--)
            if (_defenseTowers[i] == null) _defenseTowers.RemoveAt(i);
        for (int i = _towerDefenseAutoplayOwnedTowers.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _towerDefenseAutoplayOwnedTowers[i];
            if (tower != null && _defenseTowers.Contains(tower)) continue;
            _towerDefenseAutoplayOwnedTowers.RemoveAt(i);
            if (i < _towerDefenseAutoplayOwnedTowerBuildTimes.Count)
                _towerDefenseAutoplayOwnedTowerBuildTimes.RemoveAt(i);
        }
    }

    private bool TrySellMisplacedAutoplayTower(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot, AutoplayBuildChoice bestBuild,
        int standardTowerCount, out string decision)
    {
        decision = string.Empty;
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (map == null || !bestBuild.IsValid || gameTime < 75f ||
            standardTowerCount < Mathf.Max(1,
                _towerDefenseAutoplayFunctionCounts.Length) + 2 || gameTime -
            _towerDefenseAutoplayLastSaleGameTime < TowerDefenseAutoplaySaleCooldown)
            return false;

        RougeDefenseTower worstTower = null;
        float worstRatio = float.PositiveInfinity;
        string worstReason = string.Empty;
        for (int i = 0; i < _towerDefenseAutoplayOwnedTowers.Count; i++)
        {
            RougeDefenseTower tower = _towerDefenseAutoplayOwnedTowers[i];
            float builtAt = i < _towerDefenseAutoplayOwnedTowerBuildTimes.Count
                ? _towerDefenseAutoplayOwnedTowerBuildTimes[i]
                : 0f;
            if (!IsAutoplayStandardTower(tower) || !tower.AllowsSellRefund ||
                gameTime - builtAt < TowerDefenseAutoplayMinimumTowerAgeBeforeSale ||
                !map.WorldToCell(tower.transform.position, out Vector2Int towerCell))
                continue;

            float refundMultiplier = GetTowerDefenseAutoplaySellRefundMultiplier();
            int refund = Mathf.FloorToInt(tower.InvestedGold * refundMultiplier);
            if (_towerDefenseGold + refund < bestBuild.PaidCost) continue;

            float affinity = GetAutoplayTileAffinity(tower.TowerType,
                tower.TowerPlaceEffect);
            float goalDefense = GetAutoplayGoalDefenseScore(map, snapshot,
                towerCell, tower.AttackRange);
            AutoplayPressureChannels channels = GetAutoplayPressureChannels(map,
                towerCell, tower.AttackRange);
            float localPressure = CombineAutoplayPressureForTower(tower.TowerType,
                channels, out _);
            float combatPower = Mathf.Max(1f, tower.Damage /
                Mathf.Max(0.03f, tower.EffectiveAttackInterval) *
                Mathf.Max(1, tower.AttackTargetCount) *
                (1f + Mathf.Max(0, tower.AttackProjectileCount - 1) * 0.14f));
            float keepScore = Mathf.Log(1f + combatPower) * 28f +
                              Mathf.Log(1f + localPressure) * 58f +
                              affinity * 1.25f + goalDefense;
            bool mismatchedSpecial = IsAutoplayDedicatedEffect(
                tower.TowerPlaceEffect) && affinity < 90f &&
                bestBuild.Utility * TowerDefenseAutoplayCommander.RedeployBias >
                keepScore * 1.25f;
            bool specialUpgrade = bestBuild.TileScore >= 105f && affinity < 90f &&
                                   bestBuild.Utility *
                                   TowerDefenseAutoplayCommander.RedeployBias >
                                   keepScore * 1.35f;
            if (!mismatchedSpecial && !specialUpgrade)
                continue;

            float ratio = keepScore / Mathf.Max(1f, bestBuild.Utility);
            if (ratio >= worstRatio) continue;
            worstRatio = ratio;
            worstTower = tower;
            worstReason = mismatchedSpecial
                ? "格子和塔型不搭"
                : "有更合适的强化格可用";
        }

        if (worstTower == null) return false;
        string towerName = worstTower.DisplayName;
        int invested = worstTower.InvestedGold;
        float sellRefundMultiplier = GetTowerDefenseAutoplaySellRefundMultiplier();
        int refundGold = Mathf.FloorToInt(invested * sellRefundMultiplier);
        int ownedIndex = _towerDefenseAutoplayOwnedTowers.IndexOf(worstTower);
        if (ownedIndex >= 0)
        {
            _towerDefenseAutoplayOwnedTowers.RemoveAt(ownedIndex);
            if (ownedIndex < _towerDefenseAutoplayOwnedTowerBuildTimes.Count)
                _towerDefenseAutoplayOwnedTowerBuildTimes.RemoveAt(ownedIndex);
        }
        DeleteTower(worstTower, sellRefundMultiplier);
        _towerDefenseAutoplayLastSaleGameTime = gameTime;
        _towerDefenseAutoplayLastCapitalActionGameTime = gameTime;
        string salePrefix = $"重新布防：{towerName}的位置不理想（{worstReason}），" +
                            $"卖出并收回 {refundGold}/{invested} 金币";
        if (TryBuildAutoplayStandardTower(map, bestBuild, "原子换防",
                out string rebuildDecision))
            decision = $"{salePrefix}；{rebuildDecision}";
        else
            decision = $"{salePrefix}，目标塔位状态变化，暂缓下一笔投入。";
        return true;
    }

    private bool TryLiquidateOuterAutoplayTowerForCoreUpgrade(
        RougeTowerDefenseMap map, AutoplayBattleSnapshot snapshot,
        AutoplayUpgradeChoice upgrade, int threatCellIndex,
        out string decision)
    {
        decision = string.Empty;
        if (map == null || !snapshot.HasMainCell || !upgrade.IsValid ||
            !IsAutoplayStandardTower(upgrade.Tower) ||
            !upgrade.Tower.CanUpgrade || upgrade.PaidCost <= _towerDefenseGold ||
            threatCellIndex < 0 ||
            threatCellIndex >= map.Width * map.Height ||
            CountAutoplayStandardTowers() <= 2)
            return false;

        float gameTime = Mathf.Max(0f, _survivalTime);
        if (gameTime - _towerDefenseAutoplayLastSaleGameTime <
            TowerDefenseAutoplayCoreLiquidationCooldownSeconds)
            return false;

        Vector2Int threatCell = new Vector2Int(threatCellIndex % map.Width,
            threatCellIndex / map.Width);
        RougeDefenseTower liquidationTower = null;
        float lowestKeepScore = float.PositiveInfinity;
        int liquidationRefund = 0;
        float refundMultiplier = GetTowerDefenseAutoplaySellRefundMultiplier();

        for (int i = 0; i < _towerDefenseAutoplayOwnedTowers.Count; i++)
        {
            RougeDefenseTower tower = _towerDefenseAutoplayOwnedTowers[i];
            float builtAt = i < _towerDefenseAutoplayOwnedTowerBuildTimes.Count
                ? _towerDefenseAutoplayOwnedTowerBuildTimes[i]
                : 0f;
            if (!IsAutoplayStandardTower(tower) || tower == upgrade.Tower ||
                !tower.AllowsSellRefund || gameTime - builtAt <
                TowerDefenseAutoplayCoreLiquidationMinimumAgeSeconds ||
                !map.WorldToCell(tower.transform.position,
                    out Vector2Int towerCell) ||
                DoesAutoplayTowerCoverCell(map, tower, threatCell))
                continue;

            float coreDistance = Vector2.Distance(towerCell, snapshot.MainCell);
            if (coreDistance <
                TowerDefenseAutoplayImmediateCoreDefenseCells + 2f)
                continue;

            // Never finance the core by dismantling a tower that is currently firing
            // into another live lane. Only genuinely idle outer assets are candidates.
            AutoplayPressureChannels active = GetAutoplayActivePressureChannels(
                map, towerCell, tower.AttackRange);
            float activeDemand = active.Crowd + active.Elite * 1.35f +
                                 active.Urgent * 1.15f;
            if (activeDemand > 0.2f) continue;

            int refund = Mathf.FloorToInt(tower.InvestedGold *
                                          refundMultiplier);
            if (_towerDefenseGold + refund < upgrade.PaidCost) continue;
            float affinity = GetAutoplayTileAffinity(tower.TowerType,
                tower.TowerPlaceEffect);
            float keepScore = tower.InvestedGold * 0.035f + affinity * 0.18f -
                              coreDistance * 4f;
            if (keepScore >= lowestKeepScore) continue;
            lowestKeepScore = keepScore;
            liquidationTower = tower;
            liquidationRefund = refund;
        }

        if (liquidationTower == null) return false;
        string soldName = liquidationTower.DisplayName;
        int investedGold = liquidationTower.InvestedGold;
        int ownedIndex = _towerDefenseAutoplayOwnedTowers.IndexOf(
            liquidationTower);
        if (ownedIndex >= 0)
        {
            _towerDefenseAutoplayOwnedTowers.RemoveAt(ownedIndex);
            if (ownedIndex < _towerDefenseAutoplayOwnedTowerBuildTimes.Count)
                _towerDefenseAutoplayOwnedTowerBuildTimes.RemoveAt(ownedIndex);
        }
        DeleteTower(liquidationTower, refundMultiplier);
        _towerDefenseAutoplayLastSaleGameTime = gameTime;
        if (!TryUpgradeAutoplayTower(upgrade, out string upgradeDecision))
        {
            decision = $"近端换防：卖出闲置远端 {soldName}，回收 " +
                       $"{liquidationRefund}/{investedGold} 金币；升级目标状态变化，" +
                       "资金转入近端防御储备。";
            return true;
        }

        decision = $"近端换防：敌人进入 {TowerDefenseAutoplayImmediateCoreDefenseCells:0}" +
                   $" 格警戒，卖出未接敌的远端 {soldName}，回收 " +
                   $"{liquidationRefund}/{investedGold} 金币；{upgradeDecision}";
        return true;
    }

    private float GetTowerDefenseAutoplaySellRefundMultiplier()
    {
        return towerBalance != null
            ? Mathf.Clamp01(towerBalance.sellRefundMultiplier)
            : 0.25f;
    }

    private AutoplayDialogueCategory SelectAutoplayTakeoverCategory(float gameTime)
    {
        float healthRatio = mainTower != null && mainTower.maxHealth > 0.001f
            ? Mathf.Clamp01(mainTower.CurrentHealth / mainTower.maxHealth)
            : 1f;
        if (_towerDefenseAutoplayTensionTarget >=
                TowerDefenseAutoplayEmotions.tenseTensionThreshold ||
            healthRatio <= TowerDefenseAutoplayDialogueThresholds
                .baseLowHealthRatio)
            return AutoplayDialogueCategory.TakeoverHighPressure;
        if (!_towerDefenseAutoplayEverEnabledThisSession)
        {
            float[] lateMinutes = TowerDefenseAutoplayDialogueTriggers
                .lateFirstTakeoverMinutes;
            float elapsedMinutes = gameTime / 60f;
            if (elapsedMinutes >= lateMinutes[3])
                return AutoplayDialogueCategory.TakeoverLateTier4;
            if (elapsedMinutes >= lateMinutes[2])
                return AutoplayDialogueCategory.TakeoverLateTier3;
            if (elapsedMinutes >= lateMinutes[1])
                return AutoplayDialogueCategory.TakeoverLateTier2;
            if (elapsedMinutes >= lateMinutes[0])
                return AutoplayDialogueCategory.TakeoverLateTier1;
            return AutoplayDialogueCategory.TakeoverFirst;
        }
        if (_towerDefenseAutoplayRapidToggleStreak >= 4 ||
            _towerDefenseAutoplaySessionToggleCount >= 8)
            return AutoplayDialogueCategory.TakeoverFrequentToggle;
        if (gameTime - _towerDefenseAutoplayLastExitGameTime <= 12f)
            return AutoplayDialogueCategory.TakeoverQuickReturn;
        return AutoplayDialogueCategory.TakeoverReturn;
    }

    private AutoplayDialogueCategory GetAutoplayImmediateBattleDialogueCategory()
    {
        if (mainTower != null && mainTower.maxHealth > 0.001f)
        {
            float healthRatio = Mathf.Clamp01(mainTower.CurrentHealth /
                                               mainTower.maxHealth);
            if (healthRatio <= TowerDefenseAutoplayDialogueThresholds
                    .baseCriticalHealthRatio)
                return AutoplayDialogueCategory.BaseCritical;
            if (healthRatio <= TowerDefenseAutoplayDialogueThresholds
                    .baseLowHealthRatio)
                return AutoplayDialogueCategory.BaseLow;
        }
        if (_bossSpawned && _bossCurrentHealth > 0f)
            return AutoplayDialogueCategory.Boss;
        return _towerDefenseAliveEstimate >= 12
            ? AutoplayDialogueCategory.Crowd
            : AutoplayDialogueCategory.Calm;
    }

    private void UpdateTowerDefenseAutoplayDialogue(
        AutoplayBattleSnapshot snapshot)
    {
        AutoplayDialogueCategory category = GetAutoplayBattleDialogueCategory(snapshot);
        UpdateAutoplayPressureTransitionDialogue(_towerDefenseAutoplayTensionTarget);
        bool changed = !_towerDefenseAutoplayHasBattleDialogueCategory ||
                       category != _towerDefenseAutoplayLastBattleDialogueCategory;
        int previousPriority = _towerDefenseAutoplayHasBattleDialogueCategory
            ? GetAutoplayDialoguePriority(
                _towerDefenseAutoplayLastBattleDialogueCategory)
            : 0;
        int nextPriority = GetAutoplayDialoguePriority(category);
        _towerDefenseAutoplayLastBattleDialogueCategory = category;
        _towerDefenseAutoplayHasBattleDialogueCategory = true;

        if (changed && nextPriority > previousPriority &&
            TryEmitAutoplayDialogue(category, true)) return;
        if (changed) QueueAutoplayDialogue(category);

        if (_towerDefenseAutoplayHasPendingDialogue)
        {
            AutoplayDialogueCategory pending =
                _towerDefenseAutoplayPendingDialogueCategory;
            bool pendingMayPreempt = GetAutoplayDialoguePriority(pending) >
                                     _towerDefenseAutoplayLastDialoguePriority;
            if (TryEmitAutoplayDialogue(pending, pendingMayPreempt)) return;
        }
        if (_survivalTime >= _towerDefenseAutoplayNextDialogueGameTime)
            TryEmitAutoplayDialogue(category, false);
    }

    private AutoplayDialogueCategory GetAutoplayBattleDialogueCategory(
        AutoplayBattleSnapshot snapshot)
    {
        if (mainTower != null && mainTower.maxHealth > 0.001f)
        {
            float healthRatio = Mathf.Clamp01(mainTower.CurrentHealth /
                                               mainTower.maxHealth);
            if (healthRatio <= TowerDefenseAutoplayDialogueThresholds
                    .baseCriticalHealthRatio)
                return AutoplayDialogueCategory.BaseCritical;
            if (healthRatio <= TowerDefenseAutoplayDialogueThresholds
                    .baseLowHealthRatio)
                return AutoplayDialogueCategory.BaseLow;
        }
        if (snapshot.BossPressure > 0.01f) return AutoplayDialogueCategory.Boss;
        if (snapshot.UrgentPressure >= Mathf.Max(
                TowerDefenseAutoplayDialogueThresholds.urgentPressureMinimum,
                snapshot.TotalPressure * TowerDefenseAutoplayDialogueThresholds
                    .urgentPressureFraction))
            return AutoplayDialogueCategory.Urgent;

        float hardConcern = snapshot.ElitePressure *
                            TowerDefenseAutoplayCommander.EliteConcern;
        float crowdConcern = snapshot.CrowdPressure *
                             TowerDefenseAutoplayCommander.CrowdConcern;
        if (hardConcern >= TowerDefenseAutoplayDialogueThresholds
                .hardConcernMinimum &&
            hardConcern > crowdConcern * TowerDefenseAutoplayDialogueThresholds
                .hardVersusCrowdFactor)
            return AutoplayDialogueCategory.Hard;
        if (snapshot.ActiveEnemies >= TowerDefenseAutoplayDialogueThresholds
                .crowdEnemyCount ||
            crowdConcern >= TowerDefenseAutoplayDialogueThresholds
                .crowdConcernMinimum)
            return AutoplayDialogueCategory.Crowd;
        return AutoplayDialogueCategory.Calm;
    }

    private bool QueueAutoplayDialogue(AutoplayDialogueCategory category)
    {
        if (_survivalTime >= _towerDefenseAutoplayNextDialogueGameTime &&
            TryEmitAutoplayDialogue(category, false)) return true;
        bool replacesStaleBattleState =
            _towerDefenseAutoplayHasPendingDialogue &&
            IsAutoplayBattleDialogueCategory(category) &&
            IsAutoplayBattleDialogueCategory(
                _towerDefenseAutoplayPendingDialogueCategory);
        if (!_towerDefenseAutoplayHasPendingDialogue ||
            replacesStaleBattleState ||
            GetAutoplayDialoguePriority(category) > GetAutoplayDialoguePriority(
                _towerDefenseAutoplayPendingDialogueCategory))
        {
            _towerDefenseAutoplayPendingDialogueCategory = category;
            _towerDefenseAutoplayHasPendingDialogue = true;
            return true;
        }
        return false;
    }

    private void ClearPendingAutoplayDialogue(AutoplayDialogueCategory category)
    {
        if (_towerDefenseAutoplayHasPendingDialogue &&
            _towerDefenseAutoplayPendingDialogueCategory == category)
            _towerDefenseAutoplayHasPendingDialogue = false;
    }

    private static bool IsAutoplayBattleDialogueCategory(
        AutoplayDialogueCategory category)
    {
        return TowerDefenseAutoplayCommander.IsBattleDialogue(category.ToString());
    }

    private bool TryEmitAutoplayDialogue(AutoplayDialogueCategory category,
        bool allowPriorityPreemption)
    {
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (gameTime < _towerDefenseAutoplayManualSpeechProtectedUntil)
            return false;
        bool cooledDown = gameTime >= _towerDefenseAutoplayNextDialogueGameTime;
        bool canPreempt = allowPriorityPreemption &&
            GetAutoplayDialoguePriority(category) >
                _towerDefenseAutoplayLastDialoguePriority &&
            gameTime - _towerDefenseAutoplayLastDialogueGameTime >=
                TowerDefenseAutoplayDialoguePreemptionCooldown;
        if (!cooledDown && !canPreempt) return false;

        string line = PickAutoplayDialogueLine(category);
        if (string.IsNullOrEmpty(line)) return false;
        RegisterAutoplayDialogueTiming(GetAutoplayDialoguePriority(category));
        if (_towerDefenseAutoplayHasPendingDialogue &&
            _towerDefenseAutoplayPendingDialogueCategory == category)
            _towerDefenseAutoplayHasPendingDialogue = false;
        PresentTowerDefenseAutoplaySpeech(line);
        _towerDefenseAutoplayLastDecision =
            $"{TowerDefenseAutoplayCharacterName}：“{line}”";
        return true;
    }

    private void EmitTowerDefenseAutoplayEventDialogue(
        AutoplayDialogueCategory category)
    {
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (gameTime < _towerDefenseAutoplayManualSpeechProtectedUntil)
        {
            QueueAutoplayDialogue(category);
            return;
        }
        string line = PickAutoplayDialogueLine(category);
        if (string.IsNullOrWhiteSpace(line)) return;
        RegisterAutoplayDialogueTiming(GetAutoplayDialoguePriority(category));
        ClearPendingAutoplayDialogue(category);
        PresentTowerDefenseAutoplaySpeech(line);
        _towerDefenseAutoplayLastDecision =
            $"{TowerDefenseAutoplayCharacterName}：“{line}”";
    }

    private bool TryEmitTowerDefenseAutoplayInteractionDialogue(
        AutoplayDialogueCategory category)
    {
        float gameTime = Mathf.Max(0f, _survivalTime);
        string line = PickAutoplayDialogueLine(category);
        if (string.IsNullOrWhiteSpace(line)) return false;
        int priority = GetAutoplayDialoguePriority(category);
        PresentTowerDefenseAutoplaySpeech(line);
        _towerDefenseAutoplayLastDecision =
            $"{TowerDefenseAutoplayCharacterName}：“{line}”";
        _towerDefenseAutoplayLastDialogueGameTime = gameTime;
        _towerDefenseAutoplayLastDialoguePriority = priority;
        _towerDefenseAutoplayManualSpeechProtectedUntil =
            Mathf.Min(_towerDefenseAutoplaySpeechVisibleUntil, gameTime + 2.2f);
        _towerDefenseAutoplayNextDialogueGameTime = Mathf.Max(
            _towerDefenseAutoplayNextDialogueGameTime, gameTime + 2.2f);
        return true;
    }

    private void UpdateAutoplayBossHealthDialogue()
    {
        float maximumHealth = GetCurrentBossMaxHealth();
        if (maximumHealth <= 0.001f) return;
        float healthRatio = Mathf.Clamp01(_bossCurrentHealth / maximumHealth);
        if (!_towerDefenseAutoplayObservedBossHealthFinal &&
            healthRatio <= TowerDefenseAutoplayDialogueTriggers
                .bossHealthFinalRatio)
        {
            _towerDefenseAutoplayObservedBossHealthFinal = true;
            _towerDefenseAutoplayObservedBossHealthCritical = true;
            _towerDefenseAutoplayObservedBossHealthWarning = true;
            EmitTowerDefenseAutoplayEventDialogue(
                AutoplayDialogueCategory.BossHealthFinal);
            return;
        }
        if (!_towerDefenseAutoplayObservedBossHealthCritical &&
            healthRatio <=
            TowerDefenseAutoplayDialogueTriggers.bossHealthCriticalRatio)
        {
            _towerDefenseAutoplayObservedBossHealthCritical = true;
            _towerDefenseAutoplayObservedBossHealthWarning = true;
            EmitTowerDefenseAutoplayEventDialogue(
                AutoplayDialogueCategory.BossHealthQuarter);
            return;
        }
        if (_towerDefenseAutoplayObservedBossHealthWarning ||
            healthRatio > TowerDefenseAutoplayDialogueTriggers
                .bossHealthWarningRatio) return;
        _towerDefenseAutoplayObservedBossHealthWarning = true;
        EmitTowerDefenseAutoplayEventDialogue(
            AutoplayDialogueCategory.BossHealthHalf);
    }

    private void TryQueueAutoplayActionDialogue(AutoplayDialogueCategory category,
        float chance, float cooldownSeconds, ref float lastQueuedGameTime)
    {
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (gameTime - lastQueuedGameTime < cooldownSeconds) return;
        EnsureAutoplayDialogueRandom();
        if (_towerDefenseAutoplayDialogueRandom.NextDouble() >= chance) return;
        if (QueueAutoplayDialogue(category)) lastQueuedGameTime = gameTime;
    }

    private void UpdateAutoplayPressureTransitionDialogue(float tension)
    {
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (tension >= TowerDefenseAutoplayEmotions.tenseTensionThreshold)
        {
            if (!_towerDefenseAutoplayTrackingHighPressure)
            {
                _towerDefenseAutoplayTrackingHighPressure = true;
                _towerDefenseAutoplayHighPressureSince = gameTime;
            }
            _towerDefenseAutoplayLowPressureSince = float.NegativeInfinity;
            return;
        }
        if (!_towerDefenseAutoplayTrackingHighPressure) return;
        if (tension > TowerDefenseAutoplayEmotions.tenseTensionThreshold - 0.1f)
        {
            _towerDefenseAutoplayLowPressureSince = float.NegativeInfinity;
            return;
        }

        if (float.IsNegativeInfinity(_towerDefenseAutoplayLowPressureSince))
        {
            _towerDefenseAutoplayLowPressureSince = gameTime;
            return;
        }
        if (gameTime - _towerDefenseAutoplayLowPressureSince <
            TowerDefenseAutoplayDialogueTriggers.pressureReliefConfirmLowSeconds)
            return;

        float highDuration = _towerDefenseAutoplayLowPressureSince -
                             _towerDefenseAutoplayHighPressureSince;
        ResetAutoplayPressureTransitionTracking();
        if (highDuration < TowerDefenseAutoplayDialogueTriggers
                .pressureReliefMinimumHighSeconds ||
            gameTime - _towerDefenseAutoplayLastPressureReliefDialogueGameTime <
            TowerDefenseAutoplayDialogueTriggers
                .pressureReliefDialogueCooldownSeconds)
            return;
        _towerDefenseAutoplayLastPressureReliefDialogueGameTime = gameTime;
        QueueAutoplayDialogue(AutoplayDialogueCategory.PressureRelieved);
    }

    private void ResetAutoplayPressureTransitionTracking()
    {
        _towerDefenseAutoplayTrackingHighPressure = false;
        _towerDefenseAutoplayHighPressureSince = float.NegativeInfinity;
        _towerDefenseAutoplayLowPressureSince = float.NegativeInfinity;
    }

    private void UpdateAutoplayEmotionDialogue(float tension)
    {
        AutoplayEmotionState next = ResolveAutoplayEmotionState(tension,
            _towerDefenseAutoplayEmotionState);
        if (_towerDefenseAutoplayEmotionInitialized &&
            next < _towerDefenseAutoplayEmotionState)
            next = (AutoplayEmotionState)((int)_towerDefenseAutoplayEmotionState - 1);
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (!_towerDefenseAutoplayEmotionInitialized)
        {
            _towerDefenseAutoplayEmotionInitialized = true;
            _towerDefenseAutoplayEmotionState = next;
            _towerDefenseAutoplayEmotionCandidate = next;
            _towerDefenseAutoplayEmotionCandidateSince = gameTime;
            return;
        }
        if (next == _towerDefenseAutoplayEmotionState)
        {
            _towerDefenseAutoplayEmotionCandidate = next;
            _towerDefenseAutoplayEmotionCandidateSince = gameTime;
            return;
        }
        if (next != _towerDefenseAutoplayEmotionCandidate)
        {
            _towerDefenseAutoplayEmotionCandidate = next;
            _towerDefenseAutoplayEmotionCandidateSince = gameTime;
            return;
        }
        if (gameTime - _towerDefenseAutoplayEmotionCandidateSince <
            TowerDefenseAutoplayEmotions.transitionConfirmSeconds) return;

        _towerDefenseAutoplayEmotionState = next;
        if (gameTime - _towerDefenseAutoplayLastEmotionDialogueGameTime <
            TowerDefenseAutoplayEmotions.transitionDialogueCooldownSeconds)
            return;
        _towerDefenseAutoplayLastEmotionDialogueGameTime = gameTime;
        QueueAutoplayDialogue(GetAutoplayEmotionDialogueCategory(next));
    }

    private static AutoplayEmotionState ResolveAutoplayEmotionState(float tension,
        AutoplayEmotionState current)
    {
        if (tension >= TowerDefenseAutoplayEmotions.criticalTensionThreshold)
            return AutoplayEmotionState.Critical;
        if (current == AutoplayEmotionState.Critical &&
            tension >= TowerDefenseAutoplayEmotions.criticalTensionThreshold - 0.12f)
            return AutoplayEmotionState.Critical;
        if (tension >= TowerDefenseAutoplayEmotions.tenseTensionThreshold)
            return AutoplayEmotionState.Tense;
        if (current >= AutoplayEmotionState.Tense &&
            tension >= TowerDefenseAutoplayEmotions.tenseTensionThreshold - 0.1f)
            return AutoplayEmotionState.Tense;
        if (tension >= TowerDefenseAutoplayEmotions.focusedTensionThreshold)
            return AutoplayEmotionState.Focused;
        if (current >= AutoplayEmotionState.Focused &&
            tension >= TowerDefenseAutoplayEmotions.focusedTensionThreshold - 0.08f)
            return AutoplayEmotionState.Focused;
        return AutoplayEmotionState.Calm;
    }

    private static AutoplayDialogueCategory GetAutoplayEmotionDialogueCategory(
        AutoplayEmotionState emotion)
    {
        switch (emotion)
        {
            case AutoplayEmotionState.Focused:
                return AutoplayDialogueCategory.EmotionToFocused;
            case AutoplayEmotionState.Tense:
                return AutoplayDialogueCategory.EmotionToTense;
            case AutoplayEmotionState.Critical:
                return AutoplayDialogueCategory.EmotionToCritical;
            default:
                return AutoplayDialogueCategory.EmotionToCalm;
        }
    }

    private void ResetAutoplayEmotionTracking()
    {
        _towerDefenseAutoplayEmotionState = AutoplayEmotionState.Calm;
        _towerDefenseAutoplayEmotionCandidate = AutoplayEmotionState.Calm;
        _towerDefenseAutoplayEmotionInitialized = false;
        _towerDefenseAutoplayEmotionCandidateSince = 0f;
    }

    private void NotifyTowerDefenseAutoplayMainTowerDamaged(float damage)
    {
        if (damage <= 0.0001f) return;

        bool firstDamage = !_towerDefenseAutoplayMainTowerEverDamagedThisSession;
        _towerDefenseAutoplayMainTowerEverDamagedThisSession = true;
        if (mainTower == null || mainTower.IsDestroyed ||
            !_towerDefenseAutoplayEnabled || _towerDefenseGameOver ||
            _towerDefenseVictory)
        {
            _towerDefenseAutoplayMainTowerDamageSamples.Clear();
            return;
        }

        float gameTime = Mathf.Max(0f, _survivalTime);
        float window = TowerDefenseAutoplayDialogueTriggers
            .mainTowerBurstWindowSeconds;
        _towerDefenseAutoplayMainTowerDamageSamples.Add(
            new AutoplayMainTowerDamageSample
            {
                GameTime = gameTime,
                Damage = damage
            });
        _towerDefenseAutoplayEmotionDamageSamples.Add(
            new AutoplayMainTowerDamageSample
            {
                GameTime = gameTime,
                Damage = damage
            });
        int staleCount = 0;
        while (staleCount < _towerDefenseAutoplayMainTowerDamageSamples.Count &&
               gameTime - _towerDefenseAutoplayMainTowerDamageSamples[staleCount]
                   .GameTime > window)
            staleCount++;
        if (staleCount > 0)
            _towerDefenseAutoplayMainTowerDamageSamples.RemoveRange(0, staleCount);

        float accumulatedDamage = 0f;
        for (int i = 0; i < _towerDefenseAutoplayMainTowerDamageSamples.Count; i++)
            accumulatedDamage +=
                _towerDefenseAutoplayMainTowerDamageSamples[i].Damage;
        float lossPercent = mainTower != null && mainTower.maxHealth > 0.001f
            ? accumulatedDamage / mainTower.maxHealth * 100f
            : 0f;
        bool burstCooledDown = gameTime -
            _towerDefenseAutoplayLastMainTowerBurstDialogueGameTime >=
            TowerDefenseAutoplayDialogueTriggers
                .mainTowerBurstDialogueCooldownSeconds;
        bool burstThresholdReached = burstCooledDown &&
            lossPercent >= TowerDefenseAutoplayDialogueTriggers
                .mainTowerBurstHealthLossPercent;

        if (firstDamage)
        {
            _towerDefenseAutoplayLastMainTowerHitDialogueGameTime = gameTime;
            EmitTowerDefenseAutoplayEventDialogue(
                AutoplayDialogueCategory.BaseFirstDamage);
            if (burstThresholdReached)
            {
                _towerDefenseAutoplayLastMainTowerBurstDialogueGameTime = gameTime;
                _towerDefenseAutoplayMainTowerDamageSamples.Clear();
                QueueAutoplayDialogue(AutoplayDialogueCategory.BaseBurstDamage);
            }
            return;
        }

        if (burstThresholdReached)
        {
            _towerDefenseAutoplayLastMainTowerBurstDialogueGameTime = gameTime;
            _towerDefenseAutoplayLastMainTowerHitDialogueGameTime = gameTime;
            _towerDefenseAutoplayMainTowerDamageSamples.Clear();
            EmitTowerDefenseAutoplayEventDialogue(
                AutoplayDialogueCategory.BaseBurstDamage);
            return;
        }

        if (gameTime - _towerDefenseAutoplayLastMainTowerHitDialogueGameTime <
            TowerDefenseAutoplayDialogueTriggers
                .mainTowerHitDialogueCooldownSeconds)
            return;
        EnsureAutoplayDialogueRandom();
        if (_towerDefenseAutoplayDialogueRandom.NextDouble() >=
            TowerDefenseAutoplayDialogueTriggers.mainTowerHitDialogueChance)
            return;
        _towerDefenseAutoplayLastMainTowerHitDialogueGameTime = gameTime;
        EmitTowerDefenseAutoplayEventDialogue(AutoplayDialogueCategory.BaseDamaged);
    }

    private void RegisterAutoplayDialogueTiming(int priority)
    {
        EnsureAutoplayDialogueRandom();
        float gameTime = Mathf.Max(0f, _survivalTime);
        float emotionIntervalMultiplier = GetAutoplayEmotionIntervalMultiplier();
        _towerDefenseAutoplayLastDialogueGameTime = gameTime;
        _towerDefenseAutoplayNextDialogueGameTime = gameTime +
            TowerDefenseAutoplayDialogueIntervalMin * emotionIntervalMultiplier +
            (float)_towerDefenseAutoplayDialogueRandom.NextDouble() *
            (TowerDefenseAutoplayDialogueIntervalMax -
             TowerDefenseAutoplayDialogueIntervalMin) *
            emotionIntervalMultiplier;
        _towerDefenseAutoplayLastDialoguePriority = priority;
    }

    private float GetAutoplayEmotionIntervalMultiplier()
    {
        switch (_towerDefenseAutoplayEmotionState)
        {
            case AutoplayEmotionState.Focused:
                return TowerDefenseAutoplayEmotions.focusedIntervalMultiplier;
            case AutoplayEmotionState.Tense:
                return TowerDefenseAutoplayEmotions.tenseIntervalMultiplier;
            case AutoplayEmotionState.Critical:
                return TowerDefenseAutoplayEmotions.criticalIntervalMultiplier;
            default:
                return TowerDefenseAutoplayEmotions.calmIntervalMultiplier;
        }
    }

    private string PickAutoplayDialogueLine(AutoplayDialogueCategory category)
    {
        EnsureAutoplayDialogueRandom();
        string[] lines = GetAutoplayDialogueLines(category);
        if (lines == null || lines.Length == 0) return string.Empty;
        int categoryIndex = (int)category;
        int previous = _towerDefenseAutoplayLastDialogueIndices[categoryIndex];
        string bagKey = categoryIndex + ":" + CurrentAutoplayAffinityTier + ":" +
                        lines.Length;
        if (!_towerDefenseAutoplayDialogueShuffleBags.TryGetValue(bagKey,
                out List<int> bag) || bag.Count == 0)
        {
            bag = new List<int>(lines.Length);
            for (int i = 0; i < lines.Length; i++) bag.Add(i);
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int swap = _towerDefenseAutoplayDialogueRandom.Next(i + 1);
                int value = bag[i];
                bag[i] = bag[swap];
                bag[swap] = value;
            }
            _towerDefenseAutoplayDialogueShuffleBags[bagKey] = bag;
        }

        int pickPosition = bag.Count - 1;
        for (int i = bag.Count - 1; i >= 0; i--)
        {
            int candidate = bag[i];
            if (candidate == previous) continue;
            if (_towerDefenseAutoplayRecentDialogueLines.Contains(lines[candidate]))
                continue;
            pickPosition = i;
            break;
        }
        if (lines.Length > 1 && bag[pickPosition] == previous)
            for (int i = bag.Count - 1; i >= 0; i--)
                if (bag[i] != previous)
                {
                    pickPosition = i;
                    break;
                }

        int selected = bag[pickPosition];
        bag.RemoveAt(pickPosition);
        _towerDefenseAutoplayLastDialogueIndices[categoryIndex] = selected;
        string line = lines[selected];
        _towerDefenseAutoplayRecentDialogueLines.Add(line);
        while (_towerDefenseAutoplayRecentDialogueLines.Count >
               TowerDefenseAutoplayDialogueHistorySize)
            _towerDefenseAutoplayRecentDialogueLines.RemoveAt(0);
        return line;
    }

    private void EnsureAutoplayDialogueRandom()
    {
        if (!_towerDefenseAutoplayDialogueIndicesInitialized)
        {
            for (int i = 0; i < _towerDefenseAutoplayLastDialogueIndices.Length; i++)
                _towerDefenseAutoplayLastDialogueIndices[i] = -1;
            _towerDefenseAutoplayDialogueIndicesInitialized = true;
        }
        if (_towerDefenseAutoplayDialogueRandom != null) return;
        int seed = unchecked(Environment.TickCount * 397 ^ GetInstanceID() * 7919 ^
                             _towerDefenseAutoplayEntranceRevision * 104729);
        _towerDefenseAutoplayDialogueRandom = new System.Random(seed);
    }

    private static int GetAutoplayDialoguePriority(
        AutoplayDialogueCategory category)
    {
        return TowerDefenseAutoplayCommander.GetDialoguePriority(category.ToString());
    }

    private string[] GetAutoplayDialogueLines(
        AutoplayDialogueCategory category)
    {
        return TowerDefenseAutoplayCommander.GetDialogueLines(category.ToString(),
            CurrentAutoplayAffinityTier.ToString());
    }

    private string PickTowerDefenseAutoplayDefeatLine()
    {
        string[] lines = TowerDefenseAutoplayCommander.GetDefeatLines(
            CurrentAutoplayAffinityTier.ToString());
        return PickTowerDefenseAutoplayOutcomeLine(lines,
            TowerDefenseAutoplayCharacterName +
            "：主塔失去响应，链接正在断开。");
    }

    private string PickTowerDefenseAutoplayVictoryLine()
    {
        string[] lines = TowerDefenseAutoplayCommander.GetVictoryLines(
            CurrentAutoplayAffinityTier.ToString());
        return PickTowerDefenseAutoplayOutcomeLine(lines,
            "任务完成。指挥官，合作愉快。");
    }

    private string PickTowerDefenseAutoplayOutcomeLine(string[] lines,
        string fallback)
    {
        if (lines == null || lines.Length == 0) return fallback;
        EnsureAutoplayDialogueRandom();
        int start = _towerDefenseAutoplayDialogueRandom.Next(lines.Length);
        string selected = lines[start];
        for (int offset = 0; offset < lines.Length; offset++)
        {
            string candidate = lines[(start + offset) % lines.Length];
            if (_towerDefenseAutoplayRecentDialogueLines.Contains(candidate))
                continue;
            selected = candidate;
            break;
        }
        _towerDefenseAutoplayRecentDialogueLines.Add(selected);
        while (_towerDefenseAutoplayRecentDialogueLines.Count >
               TowerDefenseAutoplayDialogueHistorySize)
            _towerDefenseAutoplayRecentDialogueLines.RemoveAt(0);
        return selected;
    }

    private void SetAutoplayDecision(string decision, bool forceLog)
    {
        if (string.IsNullOrWhiteSpace(decision)) return;
        _towerDefenseAutoplayLastDecision = decision;
        if (!forceLog && string.Equals(decision, _towerDefenseAutoplayLastLoggedDecision,
                StringComparison.Ordinal)) return;
        float gameTime = Mathf.Max(0f, _survivalTime);
        if (!forceLog && gameTime - _towerDefenseAutoplayLastAmbientLogGameTime <
            TowerDefenseAutoplayAmbientLogInterval)
            return;
        _towerDefenseAutoplayLastLoggedDecision = decision;
        if (!forceLog) _towerDefenseAutoplayLastAmbientLogGameTime = gameTime;

        int seconds = Mathf.FloorToInt(gameTime);
        string line = $"[{seconds / 60:00}:{seconds % 60:00}] {decision}";
        _towerDefenseAutoplayThoughtLog.Add(line);
        while (_towerDefenseAutoplayThoughtLog.Count > TowerDefenseAutoplayThoughtCapacity)
            _towerDefenseAutoplayThoughtLog.RemoveAt(0);
        _towerDefenseAutoplayThoughtRevision++;
        RefreshTowerDefenseAutoplayPresentation();
    }
}
