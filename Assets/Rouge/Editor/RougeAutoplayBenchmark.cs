using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// RunFromCommandLine intentionally omits -quit: Play mode completion exits the editor.
[InitializeOnLoad]
public static class RougeAutoplayBenchmark
{
    private const string SessionKey = "Rouge.AutoplayBenchmark.Session";
    private static readonly string[] Modes = { "Objective", "lan", "taotao" };
    private static Session _session;
    private static RougeGameManager _manager;
    private static int _lastFrame = -1;
    private static double _runStarted;
    private static double _nextProgressAt;
    private static bool _ending;

    [Serializable]
    private sealed class Session
    {
        public string scene = "Assets/Rouge/New Scene.unity";
        public string temporaryFolder;
        public string output;
        public int runs = 100, firstSeed = 1337, next;
        public float maxGameSeconds = 1800f, stepSeconds = 1f / 20f;
        public float acceptableWinRateDrop = 0.05f, minimumStyleDistance = 0.05f;
        public double maxRunWallSeconds = 3600;
        public bool batch, ending;
        public SceneSetup[] originalScenes;
        public List<RougeAutoplayBenchmarkResult> results = new List<RougeAutoplayBenchmarkResult>();
    }

    [Serializable]
    private sealed class Report
    {
        public string unityVersion, platform, processor, graphics, scene, timingMode;
        public float stepSeconds, acceptableWinRateDrop, minimumStyleDistance;
        public int runsPerMode, firstSeed;
        public bool sufficientSample, accepted;
        public float commanderStyleDistance;
        public List<Summary> summaries = new List<Summary>();
        public List<RougeAutoplayBenchmarkResult> results;
    }

    [Serializable]
    private sealed class Summary
    {
        public string mode;
        public int runs, wins, timeouts, shieldInterventions, gateViolations;
        public double winRate, avgCoreHP, goldWaste, styleDivergence;
        // These are explicitly means of per-run p95, not pooled percentiles.
        public double meanDecisionP95Ms, meanAnalysisLatencyP95Ms, meanFrameP95Ms;
        public int[] actions, buildTypes, upgradeTypes, expansionBands;
    }

    static RougeAutoplayBenchmark()
    {
        string saved = SessionState.GetString(SessionKey, "");
        if (!string.IsNullOrEmpty(saved)) _session = JsonUtility.FromJson<Session>(saved);
        _ending = _session != null && _session.ending;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Rouge/Tower Defense/Autoplay Benchmark/Run 100 Paired Seeds")]
    public static void Run100() => Start(new Session());

    [MenuItem("Rouge/Tower Defense/Autoplay Benchmark/Smoke - 1 Paired Seed")]
    public static void RunSmoke() => Start(new Session { runs = 1 });

    [MenuItem("Rouge/Tower Defense/Autoplay Benchmark/Stop And Save Partial Report")]
    public static void Stop()
    {
        if (_session == null) return;
        Finish(false, "Stopped; partial report is not acceptance evidence.");
    }

    public static void RunFromCommandLine()
    {
        var session = new Session { batch = true };
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            string value = args[i + 1];
            switch (args[i])
            {
                case "-autoplayRuns": session.runs = int.Parse(value); break;
                case "-autoplaySeed": session.firstSeed = int.Parse(value); break;
                case "-autoplayScene": session.scene = value; break;
                case "-autoplayOutput": session.output = value; break;
                case "-autoplayMaxSeconds": session.maxGameSeconds = float.Parse(value, CultureInfo.InvariantCulture); break;
                case "-autoplayMaxWallSeconds": session.maxRunWallSeconds = double.Parse(value, CultureInfo.InvariantCulture); break;
                case "-autoplayAcceptDrop": session.acceptableWinRateDrop = float.Parse(value, CultureInfo.InvariantCulture); break;
            }
        }
        Start(session);
    }

    [MenuItem("Rouge/Tower Defense/Autoplay Benchmark/Validate Policy Scenarios")]
    public static void ValidatePolicyScenarios() => RougeGameManager.ValidateAutoplayPolicyScenarios();

    [MenuItem("Rouge/Tower Defense/Autoplay Benchmark/Validate Policy Scenarios", true)]
    private static bool CanValidatePolicyScenarios() => EditorApplication.isPlaying;

    private static void Start(Session session)
    {
        if (_session != null || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("A benchmark or Play session is already active.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scene before running the benchmark.");
        if (session.runs < 1 || session.maxGameSeconds <= 0 || session.maxRunWallSeconds <= 0 ||
            session.acceptableWinRateDrop < 0 || session.acceptableWinRateDrop > 1)
            throw new ArgumentOutOfRangeException(nameof(session));
        session.originalScenes = EditorSceneManager.GetSceneManagerSetup();
        // Silence before entering Play mode; OnEnable may start background music.
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        session.temporaryFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/__AutoplayBenchmark");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(session.temporaryFolder));
        if (string.IsNullOrEmpty(session.output))
            session.output = "Reports/Autoplay/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
        session.output = Path.GetFullPath(session.output);
        _session = session;
        _ending = false;
        PrepareNextRun();
    }

    private static void SaveSession() => SessionState.SetString(SessionKey, JsonUtility.ToJson(_session));

    private static void PrepareNextRun()
    {
        try
        {
            if (_session.next >= _session.runs * Modes.Length)
            {
                Report report = WriteReport();
                // Smoke runs validate execution and gate invariants, not win-rate acceptance.
                bool valid = report.results.All(r => !r.timedOut && r.gateViolations == 0) &&
                    (!report.sufficientSample || report.accepted);
                Finish(valid, report.sufficientSample ? "Acceptance run complete." : "Smoke complete; sample is too small for acceptance.");
                return;
            }
            int mode = _session.next / _session.runs;
            int seed = checked(_session.firstSeed + _session.next % _session.runs);
            Scene scene = EditorSceneManager.OpenScene(_session.scene, OpenSceneMode.Single);
            RougeTowerDefenseMapLoader loader = UnityEngine.Object.FindFirstObjectByType<RougeTowerDefenseMapLoader>();
            RougeGameManager manager = UnityEngine.Object.FindFirstObjectByType<RougeGameManager>();
            if (loader == null || loader.Map == null || manager == null)
                throw new InvalidOperationException("Benchmark scene needs a map loader, map and game manager.");
            string mapPath = _session.temporaryFolder + "/Map.asset";
            RougeTowerDefenseMap map = UnityEngine.Object.Instantiate(loader.Map);
            var mapData = new SerializedObject(map);
            mapData.FindProperty("gameplaySeed").intValue = seed;
            mapData.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.DeleteAsset(mapPath);
            AssetDatabase.CreateAsset(map, mapPath);
            var loaderData = new SerializedObject(loader);
            loaderData.FindProperty("map").objectReferenceValue = map;
            loaderData.FindProperty("commanderConfigName").stringValue = mode == 2 ? "taotao" : "lan";
            loaderData.FindProperty("showCommanderSelectionOnStartup").boolValue = false;
            // WaitForEndOfFrame used by the reveal never resumes in batch mode.
            // Disable presentation on the temporary copy before manager initialization.
            loaderData.FindProperty("playStartupMapReveal").boolValue = false;
            loaderData.ApplyModifiedPropertiesWithoutUndo();
            // Copy the scene after assigning the cloned seed/map. Source assets and
            // Commander JSON are never saved with benchmark overrides.
            string scenePath = _session.temporaryFolder + "/Run.unity";
            EditorSceneManager.SaveScene(scene, scenePath, true);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
            SaveSession();
            Debug.Log($"Autoplay benchmark: {Modes[mode]}, seed {seed}, run {_session.next + 1}/{_session.runs * 3}");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString());
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (_session == null) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            AudioListener.pause = true;
            AudioListener.volume = 0f;
            QualitySettings.SetQualityLevel(0, false);
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.antiAliasing = 0;
            QualitySettings.pixelLightCount = 0;
            QualitySettings.lodBias = 0.25f;
            QualitySettings.globalTextureMipmapLimit = 3;
            Screen.SetResolution(320, 180, FullScreenMode.Windowed);
            // Preserve cameras for targeting/view logic but skip scene rendering.
            foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                camera.cullingMask = 0;
            _manager = UnityEngine.Object.FindFirstObjectByType<RougeGameManager>();
            if (_manager == null) { Finish(false, "Game manager was not created."); return; }
            if (_session.next == 0) ValidatePolicyScenarios();
            _manager.AutoplayObjectiveBaseline = _session.next / _session.runs == 0;
            _manager.AutoplayBenchmarkActive = true;
            _manager.SetTowerDefenseAutoplayEnabled(true);
            Time.timeScale = 1f;
            Time.captureDeltaTime = _session.stepSeconds;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            _runStarted = EditorApplication.timeSinceStartup;
            _nextProgressAt = _runStarted + 30;
            _lastFrame = -1;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (_ending) Cleanup();
            else EditorApplication.delayCall += PrepareNextRun;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
            Time.captureDeltaTime = 0f;
    }

    private static void Tick()
    {
        if (_session == null || _ending || !EditorApplication.isPlaying || _manager == null ||
            Time.frameCount == _lastFrame) return;
        _lastFrame = Time.frameCount;
        _manager.SampleAutoplayBenchmarkFrame();
        if (EditorApplication.timeSinceStartup >= _nextProgressAt)
        {
            _nextProgressAt = EditorApplication.timeSinceStartup + 30;
            Debug.Log("Autoplay progress: " + _manager.AutoplayBenchmarkStatus);
        }
        bool timeout = _manager.AutoplayBenchmarkGameSeconds >= _session.maxGameSeconds ||
            EditorApplication.timeSinceStartup - _runStarted > _session.maxRunWallSeconds;
        if (!_manager.AutoplayBenchmarkFinished && !timeout) return;
        int mode = _session.next / _session.runs;
        int seed = _session.firstSeed + _session.next % _session.runs;
        RougeAutoplayBenchmarkResult result = _manager.CaptureAutoplayBenchmarkResult(Modes[mode], seed, timeout);
        _session.results.Add(result);
        _session.next++;
        SaveSession();
        WriteReport(); // Persist each completed run, including timeout/failure.
        Debug.Log($"Autoplay result: {result.mode}/{seed}, win={result.win}, HP={result.coreHP}, " +
            $"decision p95={result.decisionP95Ms:F2} ms, frame p95={result.frameP95Ms:F2} ms");
        _manager = null;
        EditorApplication.ExitPlaymode();
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (_session == null || _ending || !EditorApplication.isPlaying) return;
        if (type == LogType.Exception || type == LogType.Error)
            EditorApplication.delayCall += () => Finish(false, "Runtime error: " + message + "\n" + stack);
    }

    private static Report WriteReport()
    {
        var report = new Report
        {
            unityVersion = Application.unityVersion, platform = Application.platform.ToString(),
            processor = SystemInfo.processorType, graphics = SystemInfo.graphicsDeviceName,
            scene = _session.scene, runsPerMode = _session.runs, firstSeed = _session.firstSeed,
            stepSeconds = _session.stepSeconds, acceptableWinRateDrop = _session.acceptableWinRateDrop,
            minimumStyleDistance = _session.minimumStyleDistance,
            timingMode = "Silent fast benchmark: minimum quality, 320x180, camera culling disabled, uncapped FPS, startup reveal disabled; fixed 0.05 s simulation step and next-frame plan completion. CPU p95 includes completion stalls; latency includes worker time. Objective uses lan strategy/talent with final personality ranking disabled. Frame timing excludes normal scene rendering and is not standalone player frame timing.",
            sufficientSample = _session.runs >= 100 && _session.results.Count == _session.runs * 3,
            results = _session.results
        };
        foreach (string mode in Modes)
        {
            var runs = _session.results.Where(r => r.mode == mode).ToArray();
            if (runs.Length == 0) continue;
            report.summaries.Add(new Summary
            {
                mode = mode, runs = runs.Length, wins = runs.Count(r => r.win), timeouts = runs.Count(r => r.timedOut),
                winRate = runs.Count(r => r.win) / (double)runs.Length,
                avgCoreHP = runs.Average(r => r.coreHP), goldWaste = runs.Average(r => r.goldWaste),
                styleDivergence = runs.Sum(r => r.styleDivergences) / (double)Math.Max(1, runs.Sum(r => r.decisions)),
                shieldInterventions = runs.Sum(r => r.shieldInterventions), gateViolations = runs.Sum(r => r.gateViolations),
                meanDecisionP95Ms = runs.Average(r => r.decisionP95Ms),
                meanAnalysisLatencyP95Ms = runs.Average(r => r.analysisLatencyP95Ms),
                meanFrameP95Ms = runs.Average(r => r.frameP95Ms),
                actions = SumVectors(runs.Select(r => r.actions)), buildTypes = SumVectors(runs.Select(r => r.buildTypes)),
                upgradeTypes = SumVectors(runs.Select(r => r.upgradeTypes)), expansionBands = SumVectors(runs.Select(r => r.expansionBands))
            });
        }
        if (report.summaries.Count == 3)
        {
            Summary lan = report.summaries[1], taotao = report.summaries[2];
            report.commanderStyleDistance = (DistributionDistance(lan.buildTypes, taotao.buildTypes) +
                DistributionDistance(lan.upgradeTypes, taotao.upgradeTypes) +
                DistributionDistance(lan.expansionBands, taotao.expansionBands)) / 3f;
            // A zero-win baseline cannot establish a useful recovery threshold.
            report.accepted = report.sufficientSample && report.summaries[0].wins > 0 &&
                report.summaries.All(s => s.timeouts == 0 &&
                s.gateViolations == 0 && s.winRate >= report.summaries[0].winRate - _session.acceptableWinRateDrop) &&
                report.commanderStyleDistance >= _session.minimumStyleDistance;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_session.output));
        File.WriteAllText(_session.output, JsonUtility.ToJson(report, true));
        var csv = new StringBuilder("Mode,Seed,Win,CoreHP,GoldWaste,GameSeconds,TimedOut,StyleDivergence,ShieldInterventions,GateViolations,DecisionP95Ms,AnalysisLatencyP95Ms,FrameP95Ms,FullAnalyses,MaxSpatialCandidates\n");
        foreach (RougeAutoplayBenchmarkResult r in _session.results)
            csv.AppendLine(FormattableString.Invariant($"{r.mode},{r.seed},{r.win},{r.coreHP},{r.goldWaste},{r.gameSeconds},{r.timedOut},{r.styleDivergences / (double)Math.Max(1, r.decisions)},{r.shieldInterventions},{r.gateViolations},{r.decisionP95Ms},{r.analysisLatencyP95Ms},{r.frameP95Ms},{r.fullAnalyses},{r.maxSpatialCandidates}"));
        File.WriteAllText(Path.ChangeExtension(_session.output, ".csv"), csv.ToString());
        return report;
    }

    private static int[] SumVectors(IEnumerable<int[]> vectors)
    {
        int[][] all = vectors.ToArray();
        int[] result = new int[all[0].Length];
        foreach (int[] values in all)
            for (int i = 0; i < result.Length; i++) result[i] += values[i];
        return result;
    }

    private static float DistributionDistance(int[] a, int[] b)
    {
        float totalA = Math.Max(1, a.Sum()), totalB = Math.Max(1, b.Sum());
        float distance = 0f;
        for (int i = 0; i < a.Length; i++) distance += Mathf.Abs(a[i] / totalA - b[i] / totalB);
        return distance * 0.5f; // Total variation, [0,1].
    }

    private static void Finish(bool success, string message)
    {
        if (_session == null || _ending) return;
        _ending = true;
        _session.ending = true;
        SaveSession();
        WriteReport();
        Debug.Log($"Autoplay benchmark {(success ? "finished" : "failed")}: {message}\nReport: {_session.output}");
        SessionState.SetInt(SessionKey + ".ExitCode", success ? 0 : 1);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else Cleanup();
    }

    private static void Cleanup()
    {
        bool batch = _session.batch;
        string folder = _session.temporaryFolder;
        SceneSetup[] originalScenes = _session.originalScenes;
        _session = null;
        SessionState.EraseString(SessionKey);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset(folder);
        if (!batch && originalScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
        if (batch) EditorApplication.Exit(SessionState.GetInt(SessionKey + ".ExitCode", 1));
    }
}
