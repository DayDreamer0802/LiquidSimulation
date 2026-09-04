#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;

// Editor-only instrumentation. Live matches do not allocate timing sample lists.
public partial class RougeGameManager
{
    public bool AutoplayBenchmarkActive { get; set; }
    private readonly List<double> _autoplayBenchmarkDecisionMs = new List<double>();
    private readonly List<double> _autoplayBenchmarkLatencyMs = new List<double>();
    private readonly List<double> _autoplayBenchmarkFrameMs = new List<double>();
    private readonly int[] _autoplayBenchmarkActions = new int[6];
    private readonly int[] _autoplayBenchmarkBuildTypes = new int[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly int[] _autoplayBenchmarkUpgradeTypes = new int[TowerDefenseVisuals.StandardTowerTypeCount];
    private readonly int[] _autoplayBenchmarkExpansionBands = new int[3];
    private int _autoplayBenchmarkDecisions, _autoplayBenchmarkDivergences;
    private int _autoplayBenchmarkShieldInterventions, _autoplayBenchmarkGateViolations;
    private int _autoplayBenchmarkFullAnalyses, _autoplayBenchmarkMaxCandidates;
    private long _autoplayBenchmarkAnalysisStarted;
    private double _autoplayBenchmarkLastFrameTime;
    private double _autoplayBenchmarkDecisionCpuMs;

    public bool AutoplayBenchmarkFinished => _towerDefenseGameOver || _towerDefenseVictory;
    public float AutoplayBenchmarkGameSeconds => _survivalTime;
    public string AutoplayBenchmarkStatus =>
        $"t={_survivalTime:F1}s, HP={(mainTower != null ? mainTower.CurrentHealth : 0):F0}, " +
        $"gold={_towerDefenseGold}, decisions={_autoplayBenchmarkDecisions}, " +
        $"analyses={_autoplayBenchmarkFullAnalyses}, enabled={_towerDefenseAutoplayEnabled}, " +
        $"startup={_towerDefenseStartupActive}, decision={_towerDefenseAutoplayLastDecision}";

    partial void RecordAutoplayCapitalGate(AutoplayCapitalCandidate objective,
        AutoplayCapitalCandidate selected, float epsilon, bool shieldIntervened)
    {
        if (!AutoplayBenchmarkActive) return;
        _autoplayBenchmarkDecisions++;
        bool same = selected.Kind == objective.Kind &&
            selected.Build.Type == objective.Build.Type && selected.Build.Cell == objective.Build.Cell &&
            selected.Upgrade.Tower == objective.Upgrade.Tower &&
            selected.Upgrade.SpecializationChoiceIndex == objective.Upgrade.SpecializationChoiceIndex &&
            selected.Support.Cell == objective.Support.Cell &&
            selected.Charge.TargetCell == objective.Charge.TargetCell;
        if (!same) _autoplayBenchmarkDivergences++;
        if (shieldIntervened) _autoplayBenchmarkShieldInterventions++;
        if (selected.ObjectiveScore + 0.00001f < objective.ObjectiveScore * (1f - epsilon) ||
            !PassesAutoplaySafetyShield(RougeTowerDefenseMapLoader.ActiveMap, selected) ||
            _towerDefenseAutoplaySafetyEmergency && epsilon != 0f)
            _autoplayBenchmarkGateViolations++;
    }

    partial void RecordAutoplayExecutedCapitalAction(AutoplayCapitalActionKind kind,
        RougeTowerType type, Vector2Int cell)
    {
        if (!AutoplayBenchmarkActive) return;
        _autoplayBenchmarkActions[(int)kind]++;
        if (kind == AutoplayCapitalActionKind.Build)
        {
            _autoplayBenchmarkBuildTypes[(int)type]++;
            int index = cell.y * RougeTowerDefenseMapLoader.ActiveMap.Width + cell.x;
            float distance = _towerDefenseAutoplayRouteDistanceByCell[index];
            // Build pads often have no route distance; use main-cell distance then.
            if (!float.IsFinite(distance)) distance = Vector2Int.Distance(cell, _towerDefenseAutoplayRouteMainCell);
            _autoplayBenchmarkExpansionBands[distance <= 4f ? 0 : distance <= 8f ? 1 : 2]++;
        }
        if (kind == AutoplayCapitalActionKind.Upgrade) _autoplayBenchmarkUpgradeTypes[(int)type]++;
    }

    partial void RecordAutoplayAnalysisStarted()
    {
        if (!AutoplayBenchmarkActive) return;
        _autoplayBenchmarkFullAnalyses++;
        _autoplayBenchmarkDecisionCpuMs = 0;
        _autoplayBenchmarkAnalysisStarted = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    partial void RecordAutoplayDecisionWork(long started, bool resolved)
    {
        if (!AutoplayBenchmarkActive) return;
        _autoplayBenchmarkDecisionCpuMs += AutoplayElapsedMilliseconds(started);
        _autoplayBenchmarkMaxCandidates = Mathf.Max(_autoplayBenchmarkMaxCandidates, _autoplaySpatialCandidateCount);
        if (!resolved) return;
        _autoplayBenchmarkDecisionMs.Add(_autoplayBenchmarkDecisionCpuMs);
        _autoplayBenchmarkLatencyMs.Add(AutoplayElapsedMilliseconds(_autoplayBenchmarkAnalysisStarted));
    }

    public void SampleAutoplayBenchmarkFrame()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (_autoplayBenchmarkLastFrameTime > 0)
            _autoplayBenchmarkFrameMs.Add((now - _autoplayBenchmarkLastFrameTime) * 1000.0);
        _autoplayBenchmarkLastFrameTime = now;
    }

    public RougeAutoplayBenchmarkResult CaptureAutoplayBenchmarkResult(string mode, int seed, bool timedOut)
    {
        return new RougeAutoplayBenchmarkResult
        {
            mode = mode, seed = seed, win = _towerDefenseVictory, timedOut = timedOut,
            coreHP = mainTower != null ? Mathf.Max(0f, mainTower.CurrentHealth) : 0f,
            goldWaste = _towerDefenseGold, gameSeconds = _survivalTime,
            decisions = _autoplayBenchmarkDecisions, styleDivergences = _autoplayBenchmarkDivergences,
            shieldInterventions = _autoplayBenchmarkShieldInterventions,
            gateViolations = _autoplayBenchmarkGateViolations,
            decisionP95Ms = Percentile95(_autoplayBenchmarkDecisionMs),
            analysisLatencyP95Ms = Percentile95(_autoplayBenchmarkLatencyMs),
            frameP95Ms = Percentile95(_autoplayBenchmarkFrameMs),
            fullAnalyses = _autoplayBenchmarkFullAnalyses, maxSpatialCandidates = _autoplayBenchmarkMaxCandidates,
            actions = (int[])_autoplayBenchmarkActions.Clone(), buildTypes = (int[])_autoplayBenchmarkBuildTypes.Clone(),
            upgradeTypes = (int[])_autoplayBenchmarkUpgradeTypes.Clone(),
            expansionBands = (int[])_autoplayBenchmarkExpansionBands.Clone()
        };
    }

    private static double Percentile95(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[Math.Max(0, (int)Math.Ceiling(values.Count * 0.95) - 1)];
    }

    // Scenario regressions exercise the production final gate and spatial worker.
    // No NUnit dependency is required by the existing Assembly-CSharp layout.
    public static void ValidateAutoplayPolicyScenarios()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Policy scenarios run in Play mode; use the Smoke benchmark to run them automatically.");
        var root = new GameObject("Autoplay policy regression");
        root.SetActive(false);
        RougeGameManager manager = root.AddComponent<RougeGameManager>();
        RougeTowerDefenseMap map = ScriptableObject.CreateInstance<RougeTowerDefenseMap>();
        float savedBudget = TowerDefenseAutoplayStrategy.personalityRegretBudget;
        try
        {
            TowerDefenseAutoplayStrategy.personalityRegretBudget = 0.1f;
            var objective = new AutoplayCapitalCandidate
            {
                Kind = AutoplayCapitalActionKind.Build, ObjectiveScore = 100f, StyleMultiplier = 1f,
                Build = new AutoplayBuildChoice { IsValid = true, Type = 0, Cell = Vector2Int.zero }
            };
            var random = new System.Random(1337);
            manager.AutoplayBenchmarkActive = true;
            for (int i = 0; i < 500; i++)
            {
                manager._autoplayCapitalCandidates.Clear();
                manager._autoplayCapitalCandidates.Add(objective);
                foreach (AutoplayCapitalActionKind kind in new[] { AutoplayCapitalActionKind.Upgrade,
                    AutoplayCapitalActionKind.Support, AutoplayCapitalActionKind.Charge, AutoplayCapitalActionKind.Hold })
                    manager._autoplayCapitalCandidates.Add(new AutoplayCapitalCandidate
                    {
                        Kind = kind, ObjectiveScore = (float)random.NextDouble() * 100f,
                        StyleMultiplier = 1f + (float)random.NextDouble() * 20f
                    });
                AutoplayCapitalCandidate result = manager.ResolveAutoplayCapitalRegretGate(map, default, objective);
                Require(result.ObjectiveScore >= 90f, "A style action escaped the 10% gate.");
                manager.AutoplayObjectiveBaseline = true;
                result = manager.ResolveAutoplayCapitalRegretGate(map, default, objective);
                Require(result.Kind == objective.Kind && result.ObjectiveScore == 100f,
                    "Objective baseline changed under personality bias.");
                manager.AutoplayObjectiveBaseline = false;
            }
            // Each non-Build category can win in the SAME final comparison.
            foreach (AutoplayCapitalActionKind kind in new[] { AutoplayCapitalActionKind.Upgrade,
                AutoplayCapitalActionKind.Support, AutoplayCapitalActionKind.Charge, AutoplayCapitalActionKind.Hold })
            {
                manager._autoplayCapitalCandidates.Clear();
                manager._autoplayCapitalCandidates.Add(objective);
                manager._autoplayCapitalCandidates.Add(new AutoplayCapitalCandidate
                    { Kind = kind, ObjectiveScore = 95f, StyleMultiplier = 2f });
                Require(manager.ResolveAutoplayCapitalRegretGate(map, default, objective).Kind == kind,
                    kind + " cannot express style inside epsilon.");
            }
            int cells = map.Width * map.Height;
            manager._towerDefenseAutoplayBuildPriors = new AutoplayBuildPrior[cells * TowerDefenseVisuals.StandardTowerTypeCount];
            manager._towerDefenseAutoplayBuildPriors[0] = new AutoplayBuildPrior
                { IsValid = true, CombatPower = 10f, AttackRange = 1000f };
            manager._towerDefenseAutoplaySafetyEmergency = true;
            manager._towerDefenseAutoplaySafetyThreatCell = 0;
            foreach (AutoplayCapitalActionKind kind in new[] { AutoplayCapitalActionKind.Support,
                AutoplayCapitalActionKind.Charge, AutoplayCapitalActionKind.Hold })
            {
                manager._autoplayCapitalCandidates.Clear();
                manager._autoplayCapitalCandidates.Add(objective);
                manager._autoplayCapitalCandidates.Add(new AutoplayCapitalCandidate
                    { Kind = kind, ObjectiveScore = 100f, StyleMultiplier = 100f });
                Require(manager.ResolveAutoplayCapitalRegretGate(map, default, objective).Kind ==
                    AutoplayCapitalActionKind.Build, "Shield veto did not return objective best.");
            }
            Require(manager._autoplayBenchmarkShieldInterventions == 3,
                "Shield interventions were not recorded.");
            Require(manager.GetAutoplayPersonalityRegretBudget(default) == 0f,
                "Emergency must set epsilon to zero.");
            // A remote tower cannot pass the shield even with a high objective score.
            manager._towerDefenseAutoplaySafetyThreatCell = cells - 1;
            manager._towerDefenseAutoplayBuildPriors[0] = new AutoplayBuildPrior
                { IsValid = true, CombatPower = 10f, AttackRange = 0.01f };
            Require(!manager.PassesAutoplaySafetyShield(map, objective), "Remote build passed emergency shield.");
            ValidateAutoplaySparseSpatialIndex();
            Debug.Log("Autoplay policy regressions passed: 500 mixed-action gates, objective isolation, all capital kinds, emergency veto/epsilon, remote build, sparse spatial indexing.");
        }
        finally
        {
            TowerDefenseAutoplayStrategy.personalityRegretBudget = savedBudget;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(map);
        }
    }

    private static void ValidateAutoplaySparseSpatialIndex()
    {
        var cellData = new AutoplaySpatialCell[16];
        cellData[15] = new AutoplaySpatialCell { IsGround = 1, Crowd = 7f, Total = 7f, GroundValue = 1f };
        using var cells = new Unity.Collections.NativeArray<AutoplaySpatialCell>(cellData, Unity.Collections.Allocator.TempJob);
        using var coverage = new Unity.Collections.NativeArray<float>(48, Unity.Collections.Allocator.TempJob);
        using var candidates = new Unity.Collections.NativeArray<AutoplaySpatialCandidateInput>(
            new[] { new AutoplaySpatialCandidateInput
                { CellIndex = 15, IsValid = 1, AttackRange = 0.5f, FunctionGroup = 0 } },
            Unity.Collections.Allocator.TempJob);
        using var results = new Unity.Collections.NativeArray<AutoplaySpatialCandidateResult>(1, Unity.Collections.Allocator.TempJob);
        var job = new ScoreAutoplaySpatialCandidatesJob
        {
            Candidates = candidates, Cells = cells, FunctionCoverage = coverage, Results = results,
            Width = 4, Height = 4, CellCount = 16, CellSize = 1f
        };
        job.Schedule(1, 1).Complete();
        Require(results[0].Pressure.Crowd > 0f, "Sparse result used slot 0 as map cell 0.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

[Serializable]
public sealed class RougeAutoplayBenchmarkResult
{
    public string mode;
    public int seed;
    public bool win, timedOut;
    public float coreHP, goldWaste, gameSeconds;
    public int decisions, styleDivergences, shieldInterventions, gateViolations;
    public int fullAnalyses, maxSpatialCandidates;
    public double decisionP95Ms, analysisLatencyP95Ms, frameP95Ms;
    public int[] actions, buildTypes, upgradeTypes, expansionBands;
}

#endif
