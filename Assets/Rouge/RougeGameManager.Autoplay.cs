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
    private const float TowerDefenseAutoplayEmergencyNearBaseCrisis = 0.55f;

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
    private static int TowerDefenseAutoplayOpeningTowerCount =>
        TowerDefenseAutoplayStrategy.openingTowerCount;
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
    private bool _towerDefenseAutoplayBossPlanInitialized;
    private bool _towerDefenseAutoplayBossPlanAvailable;
    private AutoplayDialogueCategory _towerDefenseAutoplayPendingDialogueCategory;
    private bool _towerDefenseAutoplayHasPendingDialogue;
    private readonly List<string> _towerDefenseAutoplayThoughtLog =
        new List<string>(TowerDefenseAutoplayThoughtCapacity);
    private readonly List<RougeDefenseTower> _towerDefenseAutoplayBossOverrides =
        new List<RougeDefenseTower>();
    private readonly List<RougeDefenseTower> _towerDefenseAutoplayOwnedTowers =
        new List<RougeDefenseTower>();
    private readonly List<float> _towerDefenseAutoplayOwnedTowerBuildTimes =
        new List<float>();
    private float _towerDefenseAutoplayLastSaleGameTime = float.NegativeInfinity;
    private float _towerDefenseAutoplayLastCapitalActionGameTime =
        float.NegativeInfinity;
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
    private float[] _towerDefenseAutoplayGroundValueByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayRouteDistanceByCell = Array.Empty<float>();
    private float[] _towerDefenseAutoplayRouteTrafficByCell = Array.Empty<float>();
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
    private float[] _towerDefenseAutoplayUpgradeRangePriors = Array.Empty<float>();
    private NativeArray<float4> _towerDefenseAutoplayPlanPositions;
    private NativeArray<float4> _towerDefenseAutoplayPlanStates;
    private NativeArray<byte> _towerDefenseAutoplayPlanKinds;
    private NativeArray<float> _towerDefenseAutoplayPlanHardFactors;
    private NativeArray<float> _towerDefenseAutoplayPlanMaximumHealth;
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
        public float ObjectiveEfficiency;
        public float FixedScore;
        public float DynamicScore;
        public float TileScore;
        public float CoverageScore;
        public float PressureScore;
        public float DiversityScore;
        public float GoalDefenseScore;
        public float OpportunityPenalty;
        public AutoplayPressureLayer DominantPressureLayer;
    }

    private struct AutoplayUpgradeChoice
    {
        public bool IsValid;
        public RougeDefenseTower Tower;
        public int OriginalCost;
        public int PaidCost;
        public float Utility;
        public float Efficiency;
        public float ObjectiveEfficiency;
        public float PressureScore;
        public float GrowthScore;
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
        public float FixedScore;
        public float TileScore;
        public float CoverageScore;
        public float BossDamageScore;
        public float OpportunityPenalty;
    }

    private struct AutoplayPressureChannels
    {
        public float Total;
        public float Crowd;
        public float Elite;
        public float Boss;
        public float Urgent;
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
            float pressure = boss ? 24f : elite ? 4f : 1f;
            float maximumHealth = MaximumHealthByKind[kind];
            if (maximumHealth > 0.001f)
                pressure *= 0.65f + math.saturate(state.x / maximumHealth) * 0.35f;

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
                nearBaseEnemyWeight = nearBaseCrisis *
                    (boss ? 2f : elite ? 1.5f : 1f);
                goalThreat = 1f - math.saturate(routeDistance /
                    math.max(1f, MaximumRouteDistance));
                distanceWeight = 1f /
                    (1f + routeDistance * routeDistance * 0.22f);
                pressure *= 1f + goalThreat * 0.9f;
            }

            float hardFactor = HardFactors[kind];
            float crowdPressure = boss
                ? 0f
                : pressure * (elite ? 0.35f : 1f);
            float elitePressure = boss ? 0f : pressure * hardFactor;
            float bossPressure = boss ? pressure : 0f;
            float speedRatio = state.z / math.max(0.01f, BaselineSpeed);
            float speedThreat = math.saturate((speedRatio - 1.08f) /
                                              (1.35f - 1.08f));
            float speedArrival = math.saturate((speedRatio - 0.8f) /
                                               (1.5f - 0.8f));
            float arrivalWeight = distanceWeight *
                math.lerp(0.78f, 1.38f, speedArrival);
            float imminentPressure = pressure * arrivalWeight;
            float urgentFactor = math.max(goalThreat, speedThreat);
            float urgentPressure = urgentFactor >= 0.7f
                ? pressure * (0.4f + urgentFactor * 0.8f)
                : 0f;

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
        // Tension is presentation and cadence information. It never injects an
        // unbounded random score error; personality selection is already guarded by
        // the objective-quality budget below.
        return efficiency;
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
        _towerDefenseAutoplayCoverageByCell = Array.Empty<float>();
        _towerDefenseAutoplayFunctionCoverageByCell = Array.Empty<float>();
        _towerDefenseAutoplayBuildPriors = Array.Empty<AutoplayBuildPrior>();
        _towerDefenseAutoplayUpgradeGrowthPriors = Array.Empty<float>();
        _towerDefenseAutoplayUpgradeRangePriors = Array.Empty<float>();
        _towerDefenseAutoplayEnemyPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayCrowdPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayElitePressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayBossPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayUrgentPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayActiveCrowdPressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayActiveElitePressureByCell = Array.Empty<float>();
        _towerDefenseAutoplayActiveUrgentPressureByCell = Array.Empty<float>();
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
        bool firstUserRelease = !enabled &&
            !_towerDefenseAutoplayEverReleasedThisSession &&
            !_towerDefenseAutoplayConclusionStopping &&
            !_towerDefenseGameOver && !_towerDefenseVictory;
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
            if (firstUserRelease)
            {
                _towerDefenseAutoplayEverReleasedThisSession = true;
                string releaseLine = PickAutoplayDialogueLine(
                    AutoplayDialogueCategory.ReleaseFirst);
                SetAutoplayDecision(
                    $"{TowerDefenseAutoplayCharacterName}：“{releaseLine}”", true);
                if (IsTiltShiftObservationActive)
                    _towerDefenseAutoplayPendingReleaseToastLine = releaseLine;
                RougeCameraModeToast.Show(
                    TowerDefenseAutoplayCharacterName + " // " + releaseLine,
                    new Color(0.18f, 0.88f, 1f, 1f));
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
            new Color(0.18f, 0.88f, 1f, 1f));
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

        bool liveBoss = _bossSpawned && _bossEnemyIndex >= 0 &&
                        _bossCurrentHealth > 0f;
        if (liveBoss && !_towerDefenseAutoplayObservedLiveBoss)
        {
            _towerDefenseAutoplayObservedLiveBoss = true;
            _towerDefenseAutoplayObservedBossHealthWarning = false;
            _towerDefenseAutoplayObservedBossHealthCritical = false;
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
        }

        _towerDefenseAutoplayTensionTarget =
            CalculateTowerDefenseAutoplayTension(snapshot);
        UpdateAutoplayEmotionDialogue(_towerDefenseAutoplayTensionTarget);
        UpdateTowerDefenseAutoplayDialogue(snapshot);

        if (TryApplyAutoplayBossTargeting(map, snapshot, out string bossDecision))
        {
            SetAutoplayDecision(bossDecision, true);
            return;
        }

        int standardTowerCount = CountAutoplayStandardTowers();
        int buildCellCount = CountAutoplayBuildCells(map);
        int freeBuildCellCount = CountOpenAutoplayBuildCells(map);
        UpdateAutoplayTowerPerformanceObservations(map);
        float mainTowerHealthRatio = mainTower != null && mainTower.maxHealth > 0.001f
            ? Mathf.Clamp01(mainTower.CurrentHealth / mainTower.maxHealth)
            : 1f;
        UpdateAutoplayStrategyMode(snapshot, standardTowerCount,
            mainTowerHealthRatio);

        float actionInterval = _towerDefenseAutoplayStrategyMode ==
                               AutoplayStrategyMode.Emergency
            ? TowerDefenseAutoplayEmergencyActionInterval
            : TowerDefenseAutoplayCapitalActionInterval;
        if (Mathf.Max(0f, _survivalTime) -
            _towerDefenseAutoplayLastCapitalActionGameTime < actionInterval)
            return;

        float perceivedUrgentPressure = snapshot.UrgentPressure *
            Mathf.Lerp(0.42f, 1f, AutoplayThreatReadingSkill);
        float perceivedPeakPressure = snapshot.PeakCellPressure *
            Mathf.Lerp(0.5f, 1f, AutoplayThreatReadingSkill);
        int pressureExpansion = perceivedUrgentPressure >=
                                TowerDefenseAutoplayThresholds.highUrgentPressure ||
                                perceivedPeakPressure >=
                                TowerDefenseAutoplayThresholds.highPeakPressure
            ? 2
            : perceivedUrgentPressure >=
                  TowerDefenseAutoplayThresholds.mediumUrgentPressure ||
              snapshot.ActiveEnemies >=
                  TowerDefenseAutoplayThresholds.mediumActiveEnemies ||
              snapshot.IncomingPressure >=
                  TowerDefenseAutoplayThresholds.mediumIncomingPressure
                ? 1
                : 0;
        int bossPreparationExpansion = snapshot.BossPreparation >=
                                       TowerDefenseAutoplayThresholds
                                           .highBossPreparation
            ? 2
            : snapshot.BossPreparation >= TowerDefenseAutoplayThresholds
                .mediumBossPreparation ? 1 : 0;
        int damageExpansion = AutoplayCrisisResponseSkill >= 0.55f
            ? mainTowerHealthRatio <= TowerDefenseAutoplayThresholds
                .criticalCrisisHealthRatio
                ? 2
                : mainTowerHealthRatio <= TowerDefenseAutoplayThresholds
                    .lowCrisisHealthRatio ? 1 : 0
            : mainTowerHealthRatio <= Mathf.Lerp(0.22f, 0.48f,
                AutoplayCrisisResponseSkill) ? 1 : 0;
        int desiredTowerCount = Mathf.Min(buildCellCount,
            TowerDefenseAutoplayOpeningTowerCount + Mathf.FloorToInt(
                Mathf.Max(0f, _survivalTime) / TowerDefenseAutoplayExpansionInterval) +
            Mathf.Max(Mathf.Max(pressureExpansion, damageExpansion),
                bossPreparationExpansion));
        int adaptiveBuildLimit = Mathf.Min(buildCellCount,
            desiredTowerCount + 2);
        EvaluateAutoplayBuildChoices(map, snapshot, out AutoplayBuildChoice bestBuild,
            out AutoplayBuildChoice affordableBuild,
            out AutoplayBuildChoice emergencyBuild);
        EvaluateAutoplayUpgradeChoices(map, snapshot, out AutoplayUpgradeChoice bestUpgrade,
            out AutoplayUpgradeChoice affordableUpgrade);
        EvaluateAutoplaySupportChoices(map, snapshot,
            out AutoplaySupportChoice bestSupport,
            out AutoplaySupportChoice affordableSupport);
        bool saveForStrategicSupport = ShouldSaveForAutoplaySupport(bestSupport,
            affordableSupport, affordableBuild, affordableUpgrade, snapshot,
            mainTowerHealthRatio);
        bool emergencyBelowAdaptiveBuildLimit = standardTowerCount <
                                                adaptiveBuildLimit;

        // Emergency is an engine safety invariant, not merely a faster cadence label.
        // Do not keep saving for a remote premium tile while enemies are already at
        // the goal: buy the quickest defensive coverage or upgrade that can act now.
        if (_towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Emergency)
        {
            AutoplayBuildChoice immediateBuild = emergencyBuild.IsValid &&
                                                  emergencyBuild.PaidCost <=
                                                  _towerDefenseGold
                ? emergencyBuild
                : affordableBuild;
            if (emergencyBelowAdaptiveBuildLimit && immediateBuild.IsValid &&
                (!affordableUpgrade.IsValid ||
                 immediateBuild.ObjectiveEfficiency >=
                 affordableUpgrade.ObjectiveEfficiency * 0.95f) &&
                TryBuildAutoplayStandardTower(map, immediateBuild, "紧急守家",
                    out string emergencyBuildDecision))
            {
                SetAutoplayDecision(emergencyBuildDecision, true);
                return;
            }
            if (affordableUpgrade.IsValid &&
                TryUpgradeAutoplayTower(affordableUpgrade,
                    out string emergencyUpgradeDecision))
            {
                SetAutoplayDecision(emergencyUpgradeDecision, true);
                return;
            }
            if (emergencyBelowAdaptiveBuildLimit && immediateBuild.IsValid &&
                TryBuildAutoplayStandardTower(map, immediateBuild, "紧急补火力",
                    out string emergencyFallbackBuildDecision))
            {
                SetAutoplayDecision(emergencyFallbackBuildDecision, true);
                return;
            }

            if (!emergencyBelowAdaptiveBuildLimit)
            {
                if (bestUpgrade.IsValid)
                    SetAutoplayDecision(DescribeAutoplaySavingPlan(bestUpgrade),
                        false);
                else
                    SetAutoplayDecision(
                        "紧急守家：当前塔数已到自适应上限，等待可执行升级。",
                        false);
            }
            else if (emergencyBuild.IsValid)
                SetAutoplayDecision(DescribeAutoplaySavingPlan(emergencyBuild,
                    "紧急守家：先凑齐最快能落地的近端防守"), false);
            else if (bestUpgrade.IsValid)
                SetAutoplayDecision(DescribeAutoplaySavingPlan(bestUpgrade), false);
            else
                SetAutoplayDecision("紧急守家：当前没有可执行的建造或升级目标。",
                    false);
            return;
        }

        string saleDecision;
        bool stableEnoughToRedeploy = standardTowerCount >=
              TowerDefenseAutoplayOpeningTowerCount +
              TowerDefenseAutoplayThresholds.redeployMinimumExtraTowers &&
            mainTowerHealthRatio >= TowerDefenseAutoplayThresholds
                .redeployMinimumHealthRatio &&
            snapshot.UrgentPressure < TowerDefenseAutoplayThresholds
                .redeployMaximumUrgentPressure &&
            snapshot.ActiveEnemies < TowerDefenseAutoplayThresholds
                .redeployMaximumActiveEnemies &&
            snapshot.BossPreparation < TowerDefenseAutoplayThresholds
                .redeployMaximumBossPreparation;
        if (stableEnoughToRedeploy &&
            TrySellMisplacedAutoplayTower(map, snapshot, bestBuild,
                standardTowerCount, out saleDecision))
        {
            SetAutoplayDecision(saleDecision, true);
            return;
        }

        int openingTarget = Mathf.Min(TowerDefenseAutoplayOpeningTowerCount,
            standardTowerCount + freeBuildCellCount);
        bool opening = standardTowerCount < openingTarget;
        bool expansionDue = standardTowerCount < desiredTowerCount;
        bool canExpand = freeBuildCellCount > 0;
        bool belowAdaptiveBuildLimit = standardTowerCount < adaptiveBuildLimit;

        // The configured opening count is a structural baseline. Within that rule the
        // tower and cell still come from the full joint battlefield evaluation.
        if (opening)
        {
            if (affordableBuild.IsValid &&
                TryBuildAutoplayStandardTower(map, affordableBuild, "开局铺塔",
                    out string openingDecision))
            {
                SetAutoplayDecision(openingDecision, true);
                return;
            }
            if (bestBuild.IsValid)
            {
                SetAutoplayDecision(DescribeAutoplaySavingPlan(bestBuild,
                    $"先凑齐 {openingTarget} 塔基础阵"), false);
                return;
            }
        }

        if (expansionDue && canExpand && bestBuild.IsValid)
        {
            bool immediateDefenseNeed =
                _towerDefenseAutoplaySustainedNearBaseCrisis ||
                _towerDefenseAutoplaySustainedMainTowerDamage;
            bool saveForSuperiorBuild = !immediateDefenseNeed &&
                ShouldSaveForAutoplayBuild(bestBuild, affordableBuild);
            if (_towerDefenseAutoplayStrategyMode !=
                    AutoplayStrategyMode.Emergency &&
                ShouldPreferAutoplaySupport(affordableSupport, affordableBuild,
                    affordableUpgrade, snapshot, mainTowerHealthRatio) &&
                TryBuildAutoplaySupportTower(map, affordableSupport,
                    out string expansionSupportDecision))
            {
                SetAutoplayDecision(expansionSupportDecision, true);
                return;
            }

            if (saveForStrategicSupport && !immediateDefenseNeed)
            {
                SetAutoplayDecision(DescribeAutoplaySupportSavingPlan(bestSupport),
                    false);
                return;
            }

            if (!saveForSuperiorBuild && affordableBuild.IsValid &&
                (!affordableUpgrade.IsValid || affordableBuild.Efficiency >=
                    affordableUpgrade.Efficiency * 1.05f))
            {
                if (TryBuildAutoplayStandardTower(map, affordableBuild, "定时扩建",
                        out string expansionDecision))
                {
                    SetAutoplayDecision(expansionDecision, true);
                    return;
                }
            }

            if (affordableUpgrade.IsValid &&
                (!saveForSuperiorBuild || affordableUpgrade.Efficiency >=
                    bestBuild.Efficiency * 0.88f) &&
                TryUpgradeAutoplayTower(affordableUpgrade, out string pressureUpgrade))
            {
                SetAutoplayDecision(pressureUpgrade, true);
                return;
            }

            SetAutoplayDecision(DescribeAutoplaySavingPlan(bestBuild,
                "扩建候选的综合效用高于当前可买方案"), false);
            return;
        }

        bool valuableSpecialTile = affordableBuild.IsValid &&
                                   AutoplayMapReadingSkill >= 0.28f &&
                                   affordableBuild.TileScore >=
                                   TowerDefenseAutoplayThresholds
                                       .valuableSpecialTileScore;
        bool pressureNeedsCoverage =
            _towerDefenseAutoplaySustainedNearBaseCrisis ||
            _towerDefenseAutoplaySustainedMainTowerDamage ||
            (mainTowerHealthRatio <= TowerDefenseAutoplayThresholds
                 .coverageHealthRatio && snapshot.NearBaseEnemyWeight > 0.01f);
        bool buildBeatsUpgrade = affordableBuild.IsValid &&
            (!affordableUpgrade.IsValid || affordableBuild.Efficiency >=
             affordableUpgrade.Efficiency * 1.04f);
        if (saveForStrategicSupport)
        {
            SetAutoplayDecision(DescribeAutoplaySupportSavingPlan(bestSupport),
                false);
            return;
        }
        if (_towerDefenseAutoplayStrategyMode != AutoplayStrategyMode.Emergency &&
            ShouldPreferAutoplaySupport(affordableSupport, affordableBuild,
                affordableUpgrade, snapshot, mainTowerHealthRatio) &&
            TryBuildAutoplaySupportTower(map, affordableSupport,
                out string supportDecision))
        {
            SetAutoplayDecision(supportDecision, true);
            return;
        }
        if (canExpand && belowAdaptiveBuildLimit && buildBeatsUpgrade &&
            (valuableSpecialTile || pressureNeedsCoverage) &&
            TryBuildAutoplayStandardTower(map, affordableBuild,
                valuableSpecialTile ? "抢占强化格" : "补强防线",
                out string opportunisticBuildDecision))
        {
            SetAutoplayDecision(opportunisticBuildDecision, true);
            return;
        }

        if (affordableUpgrade.IsValid &&
            TryUpgradeAutoplayTower(affordableUpgrade, out string upgradeDecision))
        {
            SetAutoplayDecision(upgradeDecision, true);
            return;
        }

        // When the installed standard towers are all maxed, the next sensible capital
        // action is another jointly scored tower even before a later time threshold.
        if (!HasAutoplayUpgradeableTower() && canExpand &&
            belowAdaptiveBuildLimit && affordableBuild.IsValid &&
            TryBuildAutoplayStandardTower(map, affordableBuild, "满级后扩建",
                out string maxedBuildDecision))
        {
            SetAutoplayDecision(maxedBuildDecision, true);
            return;
        }

        bool saveForSupport = bestSupport.IsValid &&
                              bestSupport.Cost > _towerDefenseGold &&
                              (!bestBuild.IsValid || bestSupport.Efficiency >=
                               bestBuild.Efficiency * 1.06f) &&
                              (!bestUpgrade.IsValid || bestSupport.Efficiency >=
                               bestUpgrade.Efficiency * 1.06f);
        bool saveForBuild = canExpand && bestBuild.IsValid &&
                            (!bestUpgrade.IsValid || bestBuild.Efficiency >=
                             bestUpgrade.Efficiency * 1.08f);
        SetAutoplayDecision(saveForSupport
            ? $"先攒钱：阵地已经成形，下一步在 [{bestSupport.Cell.x}, " +
              $"{bestSupport.Cell.y}] 部署强化塔，还差 " +
              $"{Mathf.Max(0, bestSupport.Cost - _towerDefenseGold)} 金币。"
            : saveForBuild
            ? DescribeAutoplaySavingPlan(bestBuild,
                bestBuild.TileScore >= TowerDefenseAutoplayThresholds
                    .valuableSpecialTileScore
                    ? "这块强化格值得留给合适的塔"
                    : "补一座塔比继续硬升更划算")
            : bestUpgrade.IsValid
                ? DescribeAutoplaySavingPlan(bestUpgrade)
            : snapshot.ActiveEnemies > 0
                ? "眼前有怪，但现在乱花钱不划算。我再攒一会儿。"
                : "这会儿很安静，金币先留给下一波。", false);
    }

    private bool TryApplyAutoplayBossTargeting(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot snapshot, out string decision)
    {
        decision = string.Empty;
        bool liveBoss = _bossSpawned && _bossEnemyIndex >= 0 &&
                        _bossCurrentHealth > 0f;
        int focused = 0;
        int released = 0;

        // Focus mode has a real DPS penalty on several tower families. Keep it only
        // while the priority target is inside this tower's range and local cleanup is
        // safe. Release stale overrides as one squad order instead of spending one
        // entire AI tick per tower.
        for (int i = _towerDefenseAutoplayBossOverrides.Count - 1; i >= 0; i--)
        {
            RougeDefenseTower tower = _towerDefenseAutoplayBossOverrides[i];
            bool keep = liveBoss && ShouldAutoplayUseBossFocus(tower, map,
                snapshot);
            if (keep) continue;
            _towerDefenseAutoplayBossOverrides.RemoveAt(i);
            if (!IsAutoplayStandardTower(tower) ||
                tower.TargetPriority != RougeTowerTargetPriority.BossFirst)
                continue;
            tower.ToggleTargetPriority();
            released++;
        }

        if (liveBoss)
        {
            for (int i = 0; i < _defenseTowers.Count; i++)
            {
                RougeDefenseTower tower = _defenseTowers[i];
                if (!IsAutoplayStandardTower(tower) || !tower.IsTargetedDamage ||
                    tower.TargetPriority == RougeTowerTargetPriority.BossFirst ||
                    !ShouldAutoplayUseBossFocus(tower, map, snapshot))
                    continue;

                tower.ToggleTargetPriority();
                if (tower.TargetPriority != RougeTowerTargetPriority.BossFirst)
                    continue;
                _towerDefenseAutoplayBossOverrides.Add(tower);
                focused++;
            }
        }

        if (focused <= 0 && released <= 0) return false;
        _towerTargetScheduledCount = 0;
        RefreshTowerDefenseUi(true);
        decision = focused > 0 && released > 0
            ? $"Boss 火力配额：{focused} 座入圈塔开始集火，{released} 座转回清漏。"
            : focused > 0
                ? $"Boss 入圈：{focused} 座塔协同集火，圈外火力继续清场。"
                : $"Boss 离开射界或近端告急：{released} 座塔解除集火惩罚。";
        return true;
    }

    private static bool ShouldAutoplayFocusBoss(RougeDefenseTower tower)
    {
        return tower != null && (IsAutoplayBossDamageTower(tower.TowerType) ||
                                 tower.TowerType == RougeTowerType.Cannon) &&
               tower.CanToggleTargetPriority;
    }

    private bool ShouldAutoplayUseBossFocus(RougeDefenseTower tower,
        RougeTowerDefenseMap map, AutoplayBattleSnapshot snapshot)
    {
        if (!ShouldAutoplayFocusBoss(tower) || map == null) return false;
        Vector3 delta = tower.transform.position - _bossWorldPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude > tower.AttackRange * tower.AttackRange)
            return false;
        if (!map.WorldToCell(tower.transform.position, out Vector2Int cell))
            return false;

        AutoplayPressureChannels channels = GetAutoplayActivePressureChannels(map,
            cell, tower.AttackRange);
        bool cleanupCrisis = channels.Urgent >= 2f &&
                             snapshot.UrgentPressure >= 3f;
        return !cleanupCrisis;
    }

    private void RestoreAllAutoplayBossPriorityOverrides()
    {
        bool changed = false;
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
            int choiceIndex = GetAutoplayUpgradeChoice(candidate, choice,
                out routeExplanation);
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
                Efficiency = efficiency
            };
            if (!bestOverall.IsValid || choice.Efficiency > bestOverall.Efficiency)
                bestOverall = choice;
            if (cost <= _towerDefenseGold &&
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

    private static bool ShouldPreferAutoplaySupport(
        AutoplaySupportChoice support, AutoplayBuildChoice build,
        AutoplayUpgradeChoice upgrade, AutoplayBattleSnapshot snapshot,
        float mainTowerHealthRatio)
    {
        if (!support.IsValid || !support.CoversProvenLeader ||
            support.ObservationConfidence < 0.999f ||
            support.AffectedTowers < 3 || support.HighValueTowers < 1 ||
            mainTowerHealthRatio < 0.68f || snapshot.UrgentPressure >= 2.4f ||
            snapshot.NearBaseEnemyWeight >= 3.5f) return false;
        float alternative = float.NegativeInfinity;
        if (build.IsValid) alternative = Mathf.Max(alternative, build.Efficiency);
        if (upgrade.IsValid) alternative = Mathf.Max(alternative, upgrade.Efficiency);
        return float.IsNegativeInfinity(alternative) ||
               support.Efficiency >= alternative * 1.04f;
    }

    private bool ShouldSaveForAutoplaySupport(AutoplaySupportChoice best,
        AutoplaySupportChoice affordable, AutoplayBuildChoice affordableBuild,
        AutoplayUpgradeChoice affordableUpgrade, AutoplayBattleSnapshot snapshot,
        float mainTowerHealthRatio)
    {
        if (!best.IsValid || !best.CoversProvenLeader ||
            best.ObservationConfidence < 0.999f || affordable.IsValid ||
            best.AffectedTowers < 4 || best.HighValueTowers < 1 ||
            _towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Opening ||
            _towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.Emergency ||
            _towerDefenseAutoplayStrategyMode == AutoplayStrategyMode.BossFight ||
            mainTowerHealthRatio < 0.75f || snapshot.UrgentPressure >= 1.5f ||
            snapshot.ActiveEnemies >= 15) return false;
        if (_towerDefenseGold < Mathf.CeilToInt(best.Cost * 0.42f)) return false;
        int shortfall = Mathf.Max(0, best.Cost - _towerDefenseGold);
        if (shortfall > Mathf.Max(1800,
                Mathf.RoundToInt(_towerDefenseGold * 0.75f))) return false;

        float alternative = 0f;
        if (affordableBuild.IsValid)
            alternative = Mathf.Max(alternative, affordableBuild.Efficiency);
        if (affordableUpgrade.IsValid)
            alternative = Mathf.Max(alternative, affordableUpgrade.Efficiency);
        return alternative <= 0f || best.Efficiency >= alternative * 1.18f;
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

    private bool TryBuildAutoplaySupportTower(RougeTowerDefenseMap map,
        AutoplaySupportChoice choice, out string decision)
    {
        decision = string.Empty;
        if (map == null || !choice.IsValid || !choice.CoversProvenLeader ||
            choice.ObservationConfidence < 0.999f ||
            choice.Cost > _towerDefenseGold ||
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

    private int GetAutoplayUpgradeChoice(RougeDefenseTower tower,
        AutoplayUpgradeChoice scoredChoice, out string explanation)
    {
        AutoplayPressureLayer pressure = scoredChoice.DominantPressureLayer;
        bool crowd = pressure == AutoplayPressureLayer.Crowd;
        bool urgent = pressure == AutoplayPressureLayer.Urgent;
        bool hard = pressure == AutoplayPressureLayer.Elite ||
                    pressure == AutoplayPressureLayer.Boss;
        switch (tower.TowerType)
        {
            case RougeTowerType.Ice:
                if (tower.NeedsIceBranchChoice)
                {
                    if (hard)
                    {
                        explanation = "脆弱路线：帮全队处理硬目标";
                        return 1;
                    }
                    explanation = "冻结路线：先把怪群和近端速度压住";
                    return 0;
                }
                if (tower.UsesIceFreeze)
                {
                    explanation = crowd || urgent
                        ? "冰刺：眼前更需要立刻控场"
                        : "永久霜寒：把控场范围慢慢铺开";
                    return crowd || urgent ? 0 : 1;
                }
                explanation = hard
                    ? "脆弱穿甲：专门对付精英和 Boss"
                    : "脆弱增伤：让后续火力更疼";
                return hard ? 1 : 0;

            case RougeTowerType.MachineGun:
                if (tower.NeedsMachineGunBranchChoice)
                {
                    explanation = crowd
                        ? "破片路线：怪多时一起清"
                        : "暴击路线：把单体火力做实";
                    return crowd ? 1 : 0;
                }
                if (tower.UsesMachineGunCritical)
                {
                    explanation = hard
                        ? "暴击穿甲：硬目标更值得针对"
                        : "暴击率：稳定提高输出";
                    return hard ? 1 : 0;
                }
                explanation = crowd
                    ? "更多破片：继续扩大清场面"
                    : "嵌入破片：补一点持续伤害";
                return crowd ? 0 : 1;

            case RougeTowerType.Cannon:
                if (tower.NeedsCannonBranchChoice)
                {
                    explanation = hard || urgent
                        ? "持续炮弹：把关键路口压久一点"
                        : "内圈爆破：怪群越挤越疼";
                    return hard || urgent ? 1 : 0;
                }
                if (tower.UsesCannonInnerBlast)
                {
                    explanation = crowd
                        ? "追加小炮弹：把清场范围再铺开"
                        : "扩大内圈：稳住主要落点";
                    return crowd ? 1 : 0;
                }
                explanation = urgent
                    ? "持续击退：先把贴近主塔的推回去"
                    : "增加持续次数：让路口一直有伤害";
                return urgent ? 0 : 1;

            case RougeTowerType.Flame:
                if (tower.NeedsFlameBranchChoice)
                {
                    explanation = hard
                        ? "燃烧路线：持续压低精英和 Boss 血线"
                        : "喷火器路线：把密集路口直接扫干净";
                    return hard ? 1 : 0;
                }
                if (tower.UsesFlamethrower)
                {
                    explanation = crowd || urgent
                        ? "旋转喷火：同时覆盖更多来路"
                        : "扇形喷火：集中模式把火力并到 Boss";
                    return crowd || urgent ? 0 : 1;
                }
                explanation = hard
                    ? "爆燃：配合冻结直接处理硬目标"
                    : "叠层燃烧：怪群经过火区时持续增伤";
                return hard ? 1 : 0;

            case RougeTowerType.Laser:
                if (tower.NeedsLaserBranchChoice)
                {
                    explanation = crowd
                        ? "折射路线：敌人多时不浪费光束"
                        : "破甲路线：优先拆硬目标";
                    return crowd ? 1 : 0;
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

    private bool ScheduleTowerDefenseAutoplayPlan(RougeTowerDefenseMap map,
        AutoplayBattleSnapshot baseSnapshot)
    {
        if (map == null || _towerDefenseAutoplayPlanScheduled ||
            !_positionsA.IsCreated || !_stateA.IsCreated ||
            !_towerDefenseEnemyKinds.IsCreated) return false;

        int cellCount = map.Width * map.Height;
        int typeCount = TowerDefenseVisuals.StandardTowerTypeCount;
        int candidateCount = typeCount * cellCount;
        if (cellCount <= 0 || candidateCount <= 0) return false;
        int enemyLimit = Mathf.Min(_currentMaxEnemies,
            Mathf.Min(_positionsA.Length,
                Mathf.Min(_stateA.Length, _towerDefenseEnemyKinds.Length)));

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
                TryGetNextAutoplayRouteCell(map, cell, out Vector2Int next)
                    ? next.y * map.Width + next.x
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
            if (_effectStateA.IsCreated && enemyIndex < _effectStateA.Length)
            {
                float maximumHealth = _effectStateA[enemyIndex].MaximumHealth;
                if (maximumHealth > 0.001f)
                    pressure *= 0.65f + Mathf.Clamp01(state.x / maximumHealth) * 0.35f;
            }
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
                snapshot.NearBaseEnemyWeight += nearBaseCrisis *
                    (boss ? 2f : elite ? 1.5f : 1f);
                goalThreat = 1f - Mathf.Clamp01(routeDistance /
                    Mathf.Max(1f, maximumGoalDistance));
                distanceWeight = 1f /
                    (1f + routeDistance * routeDistance * 0.22f);
                pressure *= 1f + goalThreat * 0.9f;
            }

            float crowdPressure = !boss
                ? pressure * (elite ? 0.35f : 1f)
                : 0f;
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
            float hardPressure = !boss ? pressure * hardFactor : 0f;
            float bossPressure = boss ? pressure : 0f;
            // state.z is the live navigation speed. Compare it with the current
            // standard-enemy speed so global level scaling does not mark the whole map.
            float effectiveSpeed = state.z;
            if (_effectStateA.IsCreated && enemyIndex < _effectStateA.Length)
            {
                RougeEnemyEffectState effects = _effectStateA[enemyIndex];
                effectiveSpeed *= effects.FreezeTimer > 0f
                    ? 0.05f
                    : Mathf.Clamp(1f - effects.SlowPercent * 0.01f,
                        0.05f, 1f);
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
            if (urgentFactor >= 0.7f)
            {
                urgentPressure = pressure * (0.4f + urgentFactor * 0.8f);
                snapshot.UrgentPressure += urgentPressure;
            }
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
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint spawner = _towerDefenseSpawners[i];
            if (spawner == null || !spawner.isActiveAndEnabled ||
                spawner.HasReachedWaveLimit() ||
                !map.WorldToCell(spawner.transform.position,
                    out Vector2Int spawnCell)) continue;
            float seconds = Mathf.Max(0f, spawner.timer);
            snapshot.NextWaveSeconds = Mathf.Min(snapshot.NextWaveSeconds, seconds);
            if (seconds > TowerDefenseAutoplayWaveForecastSeconds) continue;

            float readiness = 1f - Mathf.Clamp01(seconds /
                TowerDefenseAutoplayWaveForecastSeconds);
            readiness = Mathf.SmoothStep(0.18f, 1f, readiness);
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
                TowerDefenseAutoplayWaveForecastSeconds - seconds) / interval);
            if (spawner.limitWaveCount)
                forecastBatches = Mathf.Min(forecastBatches,
                    Mathf.Max(0, spawner.maximumWaves - spawner.waveIndex));
            // A dense infinite spawner should drive preparation strongly without
            // making a 0.1-second interval numerically overwhelm every other lane.
            float batchScale = Mathf.Sqrt(Mathf.Clamp(forecastBatches, 1, 12));
            float wavePressure = Mathf.Sqrt(Mathf.Clamp(spawner.spawnCount, 1, 64)) *
                                 Mathf.Lerp(0.42f, 1.15f, readiness) * batchScale;
            float hardPressure = wavePressure * hardFactor;
            float crowdPressure = wavePressure * (1f - hardFactor * 0.45f);
            snapshot.IncomingPressure += wavePressure;
            snapshot.IncomingCrowdPressure += crowdPressure;
            snapshot.IncomingElitePressure += hardPressure;
            AccumulateAutoplayForecastRoutePressure(map, spawnCell, wavePressure,
                crowdPressure, hardPressure, readiness);
        }
    }

    private void AccumulateAutoplayForecastRoutePressure(
        RougeTowerDefenseMap map, Vector2Int source, float total,
        float crowd, float elite, float readiness)
    {
        Vector2Int current = source;
        int maximumSteps = map.Width * map.Height;
        for (int step = 0; step < maximumSteps; step++)
        {
            int index = current.y * map.Width + current.x;
            float routeDistance = _towerDefenseAutoplayRouteDistanceByCell[index];
            if (float.IsPositiveInfinity(routeDistance)) break;
            float progress = 1f - Mathf.Clamp01(routeDistance /
                _towerDefenseAutoplayMaximumRouteDistance);
            float laneWeight = 0.2f + readiness * 0.14f;
            AddAutoplayPressureToCell(index, total * laneWeight,
                crowd * laneWeight, elite * laneWeight, 0f,
                total * laneWeight * Mathf.InverseLerp(0.62f, 1f, progress));
            if (current == _towerDefenseAutoplayRouteMainCell) break;
            if (!TryGetNextAutoplayRouteCell(map, current, out Vector2Int next))
                break;
            current = next;
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
        if (_bossSpawned && _bossEnemyIndex >= 0 && _bossCurrentHealth > 0f)
            return 0f;
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
        bool nearBase = nearBaseIntensity > 0.01f;
        if (nearBase)
        {
            if (float.IsNegativeInfinity(
                    _towerDefenseAutoplayNearBasePressureSince))
                _towerDefenseAutoplayNearBasePressureSince = gameTime;
        }
        else
        {
            _towerDefenseAutoplayNearBasePressureSince = float.NegativeInfinity;
        }

        float nearBaseDuration = nearBase
            ? Mathf.Max(0f, gameTime -
                _towerDefenseAutoplayNearBasePressureSince)
            : 0f;
        float nearBaseSustainProgress = nearBase
            ? Mathf.Clamp01(nearBaseDuration / Mathf.Max(0.1f,
                TowerDefenseAutoplayDialogueThresholds.nearBaseSustainSeconds))
            : 0f;
        float nearBaseProgress = nearBaseIntensity * nearBaseSustainProgress;
        bool sustainedNearBase = nearBaseSustainProgress >= 0.999f &&
            nearBaseProgress >= TowerDefenseAutoplayEmergencyNearBaseCrisis;
        _towerDefenseAutoplayNearBaseCrisis = nearBaseProgress;
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
        // a sustained kill/spawn deficit, enemies holding inside the final two cells,
        // or recent main-tower damage.
        float tension = snapshot.ActiveEnemies > 0 || snapshot.BossEnemies > 0
            ? 0.22f
            : 0.08f;
        if (snapshot.BossEnemies > 0) tension = Mathf.Max(tension, 0.38f);
        if (flowDeficit > 0.001f)
            tension = Mathf.Max(tension, Mathf.Lerp(0.28f, 0.66f, flowDeficit));
        if (nearBase)
            tension = Mathf.Max(tension,
                Mathf.Lerp(0.26f, 0.72f, nearBaseProgress));
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
        int standardTowerCount, float mainTowerHealthRatio)
    {
        RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
        int openingTarget = Mathf.Min(TowerDefenseAutoplayOpeningTowerCount,
            standardTowerCount + CountOpenAutoplayBuildCells(map));
        // Raw enemy counts and whole-route pressure grow naturally with the level and
        // must never pin the controller in Emergency. Only confirmed local danger or
        // sustained damage can override the normal build/upgrade budget here.
        bool emergency = _towerDefenseAutoplaySustainedNearBaseCrisis ||
                         _towerDefenseAutoplaySustainedMainTowerDamage;
        AutoplayStrategyMode desired;
        if (emergency)
            desired = AutoplayStrategyMode.Emergency;
        else if (openingTarget > 0 && standardTowerCount < openingTarget)
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
        if (effectsChanged)
        {
            // Runtime charge effects and permanent frost alter only this small
            // type×cell table; route topology remains reusable.
            RebuildTowerDefenseAutoplayBuildPriors(map);
        }
        if (topologyChanged || effectsChanged) _towerDefenseAutoplayPriorRevision++;

        _towerDefenseAutoplayPriorMap = map;
        _towerDefenseAutoplayPriorTopologyHash = topologyHash;
        _towerDefenseAutoplayPriorEffectHash = effectHash;
        _towerDefenseAutoplayPriorDirty = false;
    }

    private static int MixAutoplayPriorHash(int hash, int value)
    {
        return unchecked((hash ^ value) * 16777619);
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
        Array.Clear(_towerDefenseAutoplayGroundValueByCell, 0, cellCount);
        Array.Clear(_towerDefenseAutoplayRouteTrafficByCell, 0, cellCount);
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

        float maximumSourceDistance = 0f;
        for (int i = 0; i < _towerDefenseSpawners.Count; i++)
        {
            RougeEnemySpawnPoint spawner = _towerDefenseSpawners[i];
            if (spawner == null || !map.WorldToCell(spawner.transform.position,
                    out Vector2Int spawnCell)) continue;
            float sourceWeight = Mathf.Sqrt(Mathf.Clamp(spawner.spawnCount, 1, 64)) /
                                 Mathf.Sqrt(Mathf.Max(1f,
                                     spawner.spawnInterval * 0.35f));
            AccumulateAutoplayRouteTraffic(map, spawnCell, sourceWeight,
                ref maximumSourceDistance);
        }
        if (bossSpawnPoint != null &&
            map.WorldToCell(bossSpawnPoint.transform.position,
                out Vector2Int bossSpawnCell))
            AccumulateAutoplayRouteTraffic(map, bossSpawnCell, 2.2f,
                ref maximumSourceDistance);

        float maximumTraffic = 0f;
        float maximumFiniteDistance = 0f;
        for (int index = 0; index < cellCount; index++)
        {
            maximumTraffic = Mathf.Max(maximumTraffic,
                _towerDefenseAutoplayRouteTrafficByCell[index]);
            float distance = _towerDefenseAutoplayRouteDistanceByCell[index];
            if (!float.IsPositiveInfinity(distance))
                maximumFiniteDistance = Mathf.Max(maximumFiniteDistance, distance);
        }
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

        Vector2Int current = source;
        int maximumSteps = map.Width * map.Height;
        for (int step = 0; step < maximumSteps; step++)
        {
            int index = current.y * map.Width + current.x;
            _towerDefenseAutoplayRouteTrafficByCell[index] += weight;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                Vector2Int shoulder = current + new Vector2Int(dx, dy);
                if (shoulder.x < 0 || shoulder.y < 0 || shoulder.x >= map.Width ||
                    shoulder.y >= map.Height || !map.IsGround(shoulder)) continue;
                _towerDefenseAutoplayRouteTrafficByCell[
                    shoulder.y * map.Width + shoulder.x] += weight * 0.12f;
            }
            if (current == _towerDefenseAutoplayRouteMainCell) break;
            if (!TryGetNextAutoplayRouteCell(map, current, out Vector2Int next))
                break;
            current = next;
        }
    }

    private bool TryGetNextAutoplayRouteCell(RougeTowerDefenseMap map,
        Vector2Int current, out Vector2Int next)
    {
        next = current;
        if (current.x < 0 || current.y < 0 || current.x >= map.Width ||
            current.y >= map.Height) return false;
        float currentDistance = _towerDefenseAutoplayRouteDistanceByCell[
            current.y * map.Width + current.x];
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
                float tileScore = GetAutoplayTileAffinity(type, effect);
                float coverageScore = Mathf.Sqrt(Mathf.Max(0f, groundCoverage)) * 25f;
                float opportunityPenalty = GetAutoplayOpportunityPenalty(type, effect);
                float combatPower = EstimateAutoplayCombatPower(type, stats, buffs);
                float bossDamageScore = IsAutoplayBossDamageTower(type)
                    ? Mathf.Log(1f + EstimateAutoplaySingleTargetPower(stats, buffs)) *
                      30f
                    : 0f;
                float powerScore = Mathf.Log(1f + combatPower) * 24f;
                float priorityTileBonus = tileScore >= 95f
                    ? 130f + (tileScore - 95f) * 1.4f
                    : tileScore > 0f ? 18f : 0f;
                float fixedScore = Mathf.Max(1f, 45f + tileScore * 1.35f +
                    priorityTileBonus + coverageScore + powerScore -
                    opportunityPenalty);
                _towerDefenseAutoplayBuildPriors[typeIndex * cellCount + cellIndex] =
                    new AutoplayBuildPrior
                    {
                        IsValid = true,
                        PlaceEffect = effect,
                        OriginalCost = originalCost,
                        PaidCost = paidCost,
                        AttackRange = attackRange,
                        FixedScore = fixedScore,
                        TileScore = tileScore,
                        CoverageScore = coverageScore,
                        BossDamageScore = bossDamageScore,
                        OpportunityPenalty = opportunityPenalty
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
        if (_towerDefenseAutoplayUpgradeRangePriors == null ||
            _towerDefenseAutoplayUpgradeRangePriors.Length < priorCount)
            _towerDefenseAutoplayUpgradeRangePriors = new float[priorCount];
        Array.Clear(_towerDefenseAutoplayUpgradeGrowthPriors, 0, priorCount);
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
            float growthRatio = Mathf.Max(0f, nextPower / currentPower - 1f);
            float rangeRatio = currentStats.AttackRadius > 0.01f
                ? Mathf.Max(0f,
                    nextStats.AttackRadius / currentStats.AttackRadius - 1f)
                : 0f;
            int priorIndex = typeIndex * levelStride + level;
            _towerDefenseAutoplayUpgradeGrowthPriors[priorIndex] =
                55f + growthRatio * 210f + rangeRatio * 100f;
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
        AutoplayBattleSnapshot snapshot, out AutoplayBuildChoice bestOverall,
        out AutoplayBuildChoice bestAffordable,
        out AutoplayBuildChoice fastestDefensive)
    {
        AutoplayBuildChoice objectiveOverall = default;
        AutoplayBuildChoice objectiveAffordable = default;
        AutoplayBuildChoice personalityOverall = default;
        AutoplayBuildChoice personalityAffordable = default;
        bestOverall = default;
        bestAffordable = default;
        fastestDefensive = default;
        if (map == null) return;

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
                if (IsBetterAutoplayEmergencyBuild(choice, fastestDefensive))
                    fastestDefensive = choice;
                if (!objectiveOverall.IsValid || choice.ObjectiveEfficiency >
                    objectiveOverall.ObjectiveEfficiency)
                    objectiveOverall = choice;
                if (!personalityOverall.IsValid || choice.Efficiency >
                    personalityOverall.Efficiency)
                    personalityOverall = choice;
                if (choice.PaidCost <= _towerDefenseGold &&
                    (!objectiveAffordable.IsValid || choice.ObjectiveEfficiency >
                     objectiveAffordable.ObjectiveEfficiency))
                    objectiveAffordable = choice;
                if (choice.PaidCost <= _towerDefenseGold &&
                    (!personalityAffordable.IsValid || choice.Efficiency >
                     personalityAffordable.Efficiency))
                    personalityAffordable = choice;
            }
        }
        float regretBudget = GetAutoplayPersonalityRegretBudget(snapshot);
        bestOverall = SelectAutoplayPersonalityBuildChoice(objectiveOverall,
            personalityOverall, regretBudget);
        bestAffordable = SelectAutoplayPersonalityBuildChoice(
            objectiveAffordable, personalityAffordable, regretBudget);
    }

    private bool IsBetterAutoplayEmergencyBuild(AutoplayBuildChoice candidate,
        AutoplayBuildChoice current)
    {
        if (!candidate.IsValid) return false;
        if (!current.IsValid) return true;
        bool candidateDefensive = candidate.GoalDefenseScore >= 120f ||
                                  candidate.DominantPressureLayer ==
                                  AutoplayPressureLayer.Urgent;
        bool currentDefensive = current.GoalDefenseScore >= 120f ||
                                current.DominantPressureLayer ==
                                AutoplayPressureLayer.Urgent;
        if (candidateDefensive != currentDefensive) return candidateDefensive;

        int candidateShortfall = Mathf.Max(0,
            candidate.PaidCost - _towerDefenseGold);
        int currentShortfall = Mathf.Max(0, current.PaidCost - _towerDefenseGold);
        if (candidateShortfall != currentShortfall)
            return candidateShortfall < currentShortfall;

        float candidateScore = candidate.GoalDefenseScore * 1.4f +
                               candidate.PressureScore +
                               candidate.ObjectiveEfficiency * 4f;
        float currentScore = current.GoalDefenseScore * 1.4f +
                             current.PressureScore +
                             current.ObjectiveEfficiency * 4f;
        return candidateScore > currentScore;
    }

    private static AutoplayBuildChoice SelectAutoplayPersonalityBuildChoice(
        AutoplayBuildChoice objective, AutoplayBuildChoice personality,
        float regretBudget)
    {
        if (!objective.IsValid) return personality;
        if (!personality.IsValid) return objective;
        if (regretBudget <= 0f)
        {
            objective.Efficiency = objective.ObjectiveEfficiency;
            return objective;
        }
        float minimumQuality = objective.ObjectiveEfficiency *
                               (1f - Mathf.Clamp01(regretBudget));
        return personality.ObjectiveEfficiency >= minimumQuality
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
        float threatFit = GetAutoplayThreatFit(type, snapshot) *
            Mathf.Lerp(0.18f, 1f, AutoplayThreatReadingSkill);
        float objectiveBossPreparationScore = IsAutoplayBossDamageTower(type)
            ? snapshot.BossPreparation * (105f + prior.BossDamageScore +
                prior.CoverageScore * 1.15f) *
              GetAutoplayBossReadinessUrgency(snapshot)
            : 0f;
        float bossPreparationScore = objectiveBossPreparationScore *
                                     TowerDefenseAutoplayCommander.BossConcern;
        float objectiveGoalDefenseScore = GetAutoplayGoalDefenseScore(map,
            snapshot, cell,
            prior.AttackRange) * Mathf.Lerp(0.16f, 1f,
            AutoplayCrisisResponseSkill);
        float goalDefenseScore = objectiveGoalDefenseScore *
            TowerDefenseAutoplayCommander.DefenseBias;
        bool closesConfirmedNearBaseGap =
            _towerDefenseAutoplaySustainedNearBaseCrisis &&
            _towerDefenseAutoplayNearBaseCrisis >=
                TowerDefenseAutoplayEmergencyNearBaseCrisis &&
            objectiveGoalDefenseScore >= 145f &&
            objectiveUncoveredPressure >= 0.75f;
        float repeatedTypePenalty = existingTypeCount <= 0
            ? 0f
            : existingTypeCount * existingTypeCount *
              (type == RougeTowerType.MachineGun ? 42f : 18f);
        if (closesConfirmedNearBaseGap)
            repeatedTypePenalty *= type == RougeTowerType.MachineGun ? 0.18f : 0.35f;
        saturationPenalty += repeatedTypePenalty;
        float priorityTileBonus = prior.TileScore >= 95f
            ? 130f + (prior.TileScore - 95f) * 1.4f
            : prior.TileScore > 0f ? 18f : 0f;
        float rawTileContribution = prior.TileScore * 1.35f + priorityTileBonus;
        bool emergencyStrategy = _towerDefenseAutoplayStrategyMode ==
                                 AutoplayStrategyMode.Emergency;
        if (emergencyStrategy)
        {
            objectiveGoalDefenseScore *= 1.35f;
            goalDefenseScore *= 1.35f;
        }
        float emergencyTilePenalty = emergencyStrategy
            ? rawTileContribution * 0.85f
            : 0f;
        float objectiveFixedScore = prior.FixedScore - prior.CoverageScore * 0.72f -
            rawTileContribution * (1f - AutoplayMapReadingSkill) +
            prior.OpportunityPenalty * (1f - AutoplayMapReadingSkill) -
            emergencyTilePenalty;
        float learnedFixedScore = prior.FixedScore - prior.CoverageScore * 0.72f -
            rawTileContribution * (1f - AutoplayMapReadingSkill) +
            rawTileContribution *
            (TowerDefenseAutoplayCommander.SpecialTileBias - 1f) *
            AutoplayMapReadingSkill +
            prior.OpportunityPenalty * (1f - AutoplayMapReadingSkill) -
            emergencyTilePenalty;
        float missedTilePenalty = bestOpenTileAffinity >= 95f &&
                                   prior.TileScore < 95f
            ? 58f + (bestOpenTileAffinity - Mathf.Max(0f, prior.TileScore)) * 0.55f
            : 0f;
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
                             goalDefenseScore + marginalCoverageScore -
                             missedTilePenalty -
                             saturationPenalty;
        float utility = Mathf.Max(1f, learnedFixedScore + dynamicScore);
        float objectiveDynamicScore = objectivePressureScore + diversityScore +
            threatFit + objectiveBossPreparationScore +
            objectiveGoalDefenseScore + marginalCoverageScore -
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
            ObjectiveEfficiency = objectiveEfficiency,
            FixedScore = learnedFixedScore,
            DynamicScore = dynamicScore,
            TileScore = prior.TileScore,
            CoverageScore = prior.CoverageScore + marginalCoverageScore,
            PressureScore = pressureScore,
            DiversityScore = diversityScore,
            GoalDefenseScore = goalDefenseScore,
            OpportunityPenalty = prior.OpportunityPenalty + missedTilePenalty,
            DominantPressureLayer = dominantLayer
        };
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
        AutoplayBattleSnapshot snapshot, out AutoplayUpgradeChoice bestOverall,
        out AutoplayUpgradeChoice bestAffordable)
    {
        AutoplayUpgradeChoice objectiveOverall = default;
        AutoplayUpgradeChoice objectiveAffordable = default;
        AutoplayUpgradeChoice personalityOverall = default;
        AutoplayUpgradeChoice personalityAffordable = default;
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (!IsAutoplayStandardTower(tower) || !tower.CanUpgrade) continue;
            AutoplayUpgradeChoice choice = ScoreAutoplayUpgradeChoice(map, snapshot,
                tower);
            if (!objectiveOverall.IsValid || choice.ObjectiveEfficiency >
                objectiveOverall.ObjectiveEfficiency)
                objectiveOverall = choice;
            if (!personalityOverall.IsValid || choice.Efficiency >
                personalityOverall.Efficiency)
                personalityOverall = choice;
            if (choice.PaidCost <= _towerDefenseGold &&
                (!objectiveAffordable.IsValid || choice.ObjectiveEfficiency >
                 objectiveAffordable.ObjectiveEfficiency))
                objectiveAffordable = choice;
            if (choice.PaidCost <= _towerDefenseGold &&
                (!personalityAffordable.IsValid || choice.Efficiency >
                 personalityAffordable.Efficiency))
                personalityAffordable = choice;
        }
        float regretBudget = GetAutoplayPersonalityRegretBudget(snapshot);
        bestOverall = SelectAutoplayPersonalityUpgradeChoice(objectiveOverall,
            personalityOverall, regretBudget);
        bestAffordable = SelectAutoplayPersonalityUpgradeChoice(
            objectiveAffordable, personalityAffordable, regretBudget);
    }

    private static AutoplayUpgradeChoice SelectAutoplayPersonalityUpgradeChoice(
        AutoplayUpgradeChoice objective, AutoplayUpgradeChoice personality,
        float regretBudget)
    {
        if (!objective.IsValid) return personality;
        if (!personality.IsValid) return objective;
        if (regretBudget <= 0f)
        {
            objective.Efficiency = objective.ObjectiveEfficiency;
            return objective;
        }
        float minimumQuality = objective.ObjectiveEfficiency *
                               (1f - Mathf.Clamp01(regretBudget));
        return personality.ObjectiveEfficiency >= minimumQuality
            ? personality
            : objective;
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
        float rangeRatio = (uint)priorIndex <
            (uint)_towerDefenseAutoplayUpgradeRangePriors.Length
            ? _towerDefenseAutoplayUpgradeRangePriors[priorIndex]
            : 0f;
        float branchValue = tower.RequiresUpgradeChoice ? 105f : 0f;
        float growthScore = cachedGrowth + branchValue;
        float growthRatio = Mathf.Max(0f, (cachedGrowth - 55f -
            rangeRatio * 100f) / 210f);

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
        float objectivePressureScore = Mathf.Log(1f +
            Mathf.Max(0f, objectiveLocalPressure)) * 58f *
            Mathf.Lerp(0.34f, 1f, AutoplayThreatReadingSkill);
        float pressureScore = Mathf.Log(1f + Mathf.Max(0f, localPressure)) * 58f *
            Mathf.Lerp(0.34f, 1f, AutoplayThreatReadingSkill);
        float threatFit = GetAutoplayThreatFit(tower.TowerType, snapshot) * 0.55f *
            Mathf.Lerp(0.18f, 1f, AutoplayThreatReadingSkill);
        float objectiveBossPreparationScore = 0f;
        if (IsAutoplayBossDamageTower(tower.TowerType))
        {
            float routeCoverage = 0f;
            if (map != null && map.WorldToCell(tower.transform.position,
                    out Vector2Int preparationCell))
                routeCoverage = GetAutoplayGroundCoverageValue(map,
                    preparationCell, tower.AttackRange);
            objectiveBossPreparationScore = snapshot.BossPreparation *
                (105f + Mathf.Log(1f + Mathf.Max(0f, tower.Damage) /
                    Mathf.Max(0.03f, tower.EffectiveAttackInterval) *
                    Mathf.Max(1, tower.AttackProjectileCount)) * 30f +
                    Mathf.Sqrt(Mathf.Max(0f, routeCoverage)) * 22f) *
                GetAutoplayBossReadinessUrgency(snapshot);
            if (objectiveBossPreparationScore >= 45f &&
                dominantLayer != AutoplayPressureLayer.Urgent)
                dominantLayer = AutoplayPressureLayer.Boss;
        }
        float bossPreparationScore = objectiveBossPreparationScore *
            TowerDefenseAutoplayCommander.BossConcern;
        int duplicateCount = _towerDefenseAutoplayTypeCounts[(int)tower.TowerType];
        // Once an area/function is already covered, consolidating its installed
        // towers is preferable to buying yet another copy of the same type.
        float consolidationBonus = Mathf.Min(24f,
            Mathf.Max(0, duplicateCount - 1) * 6f);
        float utility = Mathf.Max(1f, growthScore + pressureScore *
            Mathf.Clamp(0.35f + growthRatio, 0.35f, 1.25f) + threatFit +
            bossPreparationScore + consolidationBonus);
        float objectiveUtility = Mathf.Max(1f, growthScore +
            objectivePressureScore *
            Mathf.Clamp(0.35f + growthRatio, 0.35f, 1.25f) + threatFit +
            objectiveBossPreparationScore + consolidationBonus);
        float costDivisor = Mathf.Max(paidCost <= 0 ? 65f : 100f,
            paidCost + 145f);
        float objectiveEfficiency = objectiveUtility * 100f / costDivisor;
        float styledEfficiency = utility * 100f / costDivisor;
        float efficiency = ApplyAutoplayPersonalityPreference(styledEfficiency,
            TowerDefenseAutoplayCommander.UpgradeBias *
            GetAutoplayPersonalityTowerBias(tower.TowerType));
        efficiency = ApplyAutoplayEconomyReturnSignal(efficiency,
            objectiveEfficiency);
        if (map != null && map.WorldToCell(tower.transform.position,
                out Vector2Int uncertaintyCell))
            efficiency = ApplyAutoplayJudgmentUncertainty(efficiency,
                tower.TowerType, uncertaintyCell);
        return new AutoplayUpgradeChoice
        {
            IsValid = true,
            Tower = tower,
            OriginalCost = originalCost,
            PaidCost = paidCost,
            Utility = utility,
            Efficiency = efficiency,
            ObjectiveEfficiency = objectiveEfficiency,
            PressureScore = pressureScore,
            GrowthScore = growthScore,
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

        float totalContribution = channels.Total * 0.22f;
        float crowdContribution = channels.Crowd * crowdWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.CrowdConcern : 1f);
        float eliteContribution = channels.Elite * eliteWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.EliteConcern : 1f);
        float bossContribution = channels.Boss * bossWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.BossConcern : 1f);
        float urgentContribution = channels.Urgent * urgentWeight *
            (applyPersonality ? TowerDefenseAutoplayCommander.UrgentConcern : 1f);

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

        return totalContribution + crowdContribution + eliteContribution +
               bossContribution + urgentContribution;
    }

    private float GetAutoplayDiversityScore(RougeTowerType type)
    {
        int typeCount = _towerDefenseAutoplayTypeCounts[(int)type];
        int groupCount = _towerDefenseAutoplayFunctionCounts[
            GetAutoplayFunctionGroup(type)];
        // Reward missing battlefield roles, not collecting one of every tower. A
        // composition may legitimately need several focused or several AOE towers.
        float typeDiversity = typeCount == 0 ? 30f : -typeCount * 7f;
        float functionDiversity = groupCount == 0
            ? 82f
            : groupCount == 1 ? 14f : -Mathf.Max(0, groupCount - 2) * 6f;
        return typeDiversity + functionDiversity;
    }

    private static int GetAutoplayFunctionGroup(RougeTowerType type)
    {
        if (type == RougeTowerType.Ice) return 0;
        if (type == RougeTowerType.MachineGun || type == RougeTowerType.Laser ||
            type == RougeTowerType.PiercingLaser) return 1;
        return 2;
    }

    private static float GetAutoplayPersonalityTowerBias(RougeTowerType type)
    {
        if (type == RougeTowerType.Ice)
            return TowerDefenseAutoplayCommander.ControlTowerBias;
        if (type == RougeTowerType.MachineGun || type == RougeTowerType.Laser ||
            type == RougeTowerType.PiercingLaser)
            return TowerDefenseAutoplayCommander.FocusedTowerBias;
        return TowerDefenseAutoplayCommander.AreaTowerBias;
    }

    private static float GetAutoplayThreatFit(RougeTowerType type,
        AutoplayBattleSnapshot snapshot)
    {
        float score = 0f;
        if (snapshot.BossEnemies > 0)
        {
            if (type == RougeTowerType.MachineGun || type == RougeTowerType.Laser ||
                type == RougeTowerType.PiercingLaser || type == RougeTowerType.Flame)
                score += 95f;
            else if (type == RougeTowerType.Ice) score += 28f;
        }
        if (snapshot.BossPreparation > 0f)
        {
            if (IsAutoplayBossDamageTower(type))
                score += snapshot.BossPreparation * 125f;
            else if (type == RougeTowerType.Ice)
                score += snapshot.BossPreparation * 22f;
        }
        int crowd = Mathf.Max(0, snapshot.ActiveEnemies - snapshot.BossEnemies -
            snapshot.EliteEnemies);
        if (crowd >= 8)
        {
            if (type == RougeTowerType.Cannon || type == RougeTowerType.Flame ||
                type == RougeTowerType.RocketBarrage ||
                type == RougeTowerType.OrbitSphere) score += 72f;
            else if (type == RougeTowerType.Ice) score += 48f;
        }
        if (snapshot.EliteEnemies > 0)
        {
            if (type == RougeTowerType.Laser || type == RougeTowerType.PiercingLaser ||
                type == RougeTowerType.Cannon || type == RougeTowerType.Flame)
                score += 42f;
        }
        return score;
    }

    private static bool IsAutoplayTowerAlignedWithThreat(RougeTowerType type,
        AutoplayBattleSnapshot snapshot)
    {
        if ((snapshot.BossEnemies > 0 || snapshot.BossPreparation >= 0.32f ||
             snapshot.IncomingElitePressure >= 4f) &&
            IsAutoplayBossDamageTower(type)) return true;
        if ((snapshot.ActiveEnemies >= 10 || snapshot.IncomingCrowdPressure >= 7f) &&
            (type == RougeTowerType.Cannon || type == RougeTowerType.Flame ||
             type == RougeTowerType.RocketBarrage ||
             type == RougeTowerType.OrbitSphere || type == RougeTowerType.Ice))
            return true;
        return snapshot.UrgentPressure >= 2f && type == RougeTowerType.Ice;
    }

    private static bool IsAutoplayBossDamageTower(RougeTowerType type)
    {
        return type == RougeTowerType.MachineGun ||
               type == RougeTowerType.Laser ||
               type == RougeTowerType.PiercingLaser ||
               type == RougeTowerType.Flame;
    }

    private float GetAutoplayBossReadinessUrgency(
        AutoplayBattleSnapshot snapshot)
    {
        int desiredFocusedTowers = snapshot.BossEnemies > 0
            ? 6
            : snapshot.BossPreparation >= 0.72f
                ? 5
                : snapshot.BossPreparation >= 0.28f ? 3 : 0;
        if (desiredFocusedTowers <= 0) return 1f;
        int focusedTowers = _towerDefenseAutoplayTypeCounts[
                                (int)RougeTowerType.MachineGun] +
                            _towerDefenseAutoplayTypeCounts[
                                (int)RougeTowerType.Laser] +
                            _towerDefenseAutoplayTypeCounts[
                                (int)RougeTowerType.PiercingLaser] +
                            _towerDefenseAutoplayTypeCounts[
                                (int)RougeTowerType.Flame];
        int deficit = Mathf.Max(0, desiredFocusedTowers - focusedTowers);
        return 1f + deficit * 0.24f;
    }

    private static float EstimateAutoplayCombatPower(RougeTowerType type,
        RougeTowerStats stats, RougeTowerBuffLevels buffs)
    {
        float damage = stats.Damage * RougeTowerBuffMath.GetMultiplier(buffs.Damage);
        float interval = stats.AttackInterval /
            RougeTowerBuffMath.GetMultiplier(buffs.AttackSpeed);
        float power = damage / Mathf.Max(0.03f, interval);
        power *= 1f + Mathf.Max(0, stats.TargetCount - 1) * 0.12f;
        power *= 1f + Mathf.Max(0, stats.ProjectileCount - 1) * 0.14f;
        if (stats.AoeRadius > 0f) power *= 1f + Mathf.Min(1.2f,
            stats.AoeRadius * 0.08f);
        if (type == RougeTowerType.Ice) power += 85f;
        return power;
    }

    private static float EstimateAutoplaySingleTargetPower(RougeTowerStats stats,
        RougeTowerBuffLevels buffs)
    {
        float damage = stats.Damage *
            RougeTowerBuffMath.GetMultiplier(buffs.Damage);
        float interval = stats.AttackInterval /
            RougeTowerBuffMath.GetMultiplier(buffs.AttackSpeed);
        return damage / Mathf.Max(0.03f, interval) *
               Mathf.Max(1, stats.ProjectileCount);
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

    private bool ShouldSaveForAutoplayBuild(AutoplayBuildChoice bestOverall,
        AutoplayBuildChoice bestAffordable)
    {
        if (!bestOverall.IsValid || bestOverall.PaidCost <= 0) return false;
        if (!bestAffordable.IsValid) return true;
        // Spending on a near-optimal tower now is safer than waiting through another
        // wave for a tiny efficiency gain. This also prevents personality nudges from
        // turning into long, fragile hoarding plans.
        if (bestAffordable.ObjectiveEfficiency >=
            bestOverall.ObjectiveEfficiency *
            (1f - TowerDefenseAutoplayPersonalityRegretBudget))
            return false;
        int shortfall = Mathf.Max(0, bestOverall.PaidCost - _towerDefenseGold);
        int acceptableShortfall = Mathf.Max(120,
            Mathf.RoundToInt(_towerDefenseGold * 0.5f));
        float qualityThreshold = 1.34f /
            Mathf.Max(0.9f, TowerDefenseAutoplayCommander.SaveBias);
        return bestOverall.PaidCost > bestAffordable.PaidCost &&
               shortfall <= acceptableShortfall &&
               bestOverall.Efficiency > bestAffordable.Efficiency *
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
                return type == RougeTowerType.Cannon || type == RougeTowerType.Flame ||
                       type == RougeTowerType.PiercingLaser ||
                       type == RougeTowerType.RocketBarrage ? 122f : 82f;
            case RougeTowerPlaceEffect.RangeAmplifier:
                return type == RougeTowerType.Ice || type == RougeTowerType.Laser ||
                       type == RougeTowerType.PiercingLaser ? 122f : 78f;
            case RougeTowerPlaceEffect.AttackSpeedAmplifier:
                return type == RougeTowerType.MachineGun || type == RougeTowerType.Laser ||
                       type == RougeTowerType.Flame ? 122f : 76f;
            case RougeTowerPlaceEffect.Bounty:
                return type == RougeTowerType.MachineGun || type == RougeTowerType.Laser
                    ? 104f : 72f;
            case RougeTowerPlaceEffect.Echo:
                return type == RougeTowerType.MachineGun || type == RougeTowerType.Laser ||
                       type == RougeTowerType.RocketBarrage ? 126f : 70f;
            case RougeTowerPlaceEffect.AccumulatedWealth:
                return type == RougeTowerType.MachineGun ? 96f : 68f;
            case RougeTowerPlaceEffect.Explosion:
                return type == RougeTowerType.MachineGun || type == RougeTowerType.Flame
                    ? 98f : 72f;
            case RougeTowerPlaceEffect.Frost:
                return type == RougeTowerType.Ice ? 120f : 64f;
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
            standardTowerCount < TowerDefenseAutoplayOpeningTowerCount + 2 || gameTime -
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
        if (lines == null || lines.Length == 0)
            return TowerDefenseAutoplayCharacterName +
                   "：主塔失去响应，链接正在断开。";
        EnsureAutoplayDialogueRandom();
        return lines[_towerDefenseAutoplayDialogueRandom.Next(lines.Length)];
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
