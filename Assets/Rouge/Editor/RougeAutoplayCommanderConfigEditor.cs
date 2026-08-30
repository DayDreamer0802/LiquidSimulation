using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RougeAutoplayCommanderConfigEditor
{
    [MenuItem("Rouge/Tower Defense/Validate Commander JSON")]
    public static void ValidateFromMenu()
    {
        try
        {
            string report = ValidateOrThrow();
            Debug.Log(report);
            EditorUtility.DisplayDialog("Commander JSON", report, "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Commander JSON invalid", exception.Message,
                "OK");
        }
    }

    // Batch entry point:
    // Unity -batchmode -nographics -quit -projectPath <path>
    //   -executeMethod RougeAutoplayCommanderConfigEditor.ValidateFromCommandLine
    public static void ValidateFromCommandLine()
    {
        Debug.Log(ValidateOrThrow());
    }

    private static string ValidateOrThrow()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string coreAssetPath = Path.Combine(projectRoot,
            RougeAutoplayCommanderJson.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        string localeAssetPath = Path.Combine(projectRoot,
            RougeAutoplayCommanderJson.LocaleAssetPath.Replace('/',
                Path.DirectorySeparatorChar));
        if (!File.Exists(coreAssetPath))
            throw new InvalidOperationException("Commander core JSON is missing: " +
                                                coreAssetPath);
        if (!File.Exists(localeAssetPath))
            throw new InvalidOperationException("Commander locale JSON is missing: " +
                                                localeAssetPath);

        string coreJson = File.ReadAllText(coreAssetPath);
        string localeJson = File.ReadAllText(localeAssetPath);
        if (!RougeAutoplayCommanderJson.TryParse(coreJson, localeJson,
                out RougeAutoplayCommanderDefinition definition, out string report))
            throw new InvalidOperationException(report);

        Texture2D portrait = Resources.Load<Texture2D>(
            definition.PortraitResourcePath);
        if (portrait == null)
            throw new InvalidOperationException(
                "Commander portrait cannot be loaded from Resources/" +
                definition.PortraitResourcePath);

        int validationProbeCount = RunValidationSmokeTests(coreJson, localeJson);

        int dialogueCount = 0;
        string[] tiers = { "Distant", "Familiar", "Close" };
        string[] categories =
        {
            "TakeoverFirst", "TakeoverQuickReturn", "TakeoverFrequentToggle",
            "TakeoverReturn", "TakeoverHighPressure", "TakeoverLateTier1",
            "TakeoverLateTier2", "TakeoverLateTier3", "TakeoverLateTier4",
            "ReleaseFirst", "Calm", "Crowd", "Hard", "BossArrival", "Boss",
            "BossHealthHalf", "BossHealthQuarter", "BossHealthFinal", "Urgent",
            "BaseLow",
            "BaseCritical", "BaseFirstDamage", "BaseDamaged",
            "BaseBurstDamage", "BuildTower", "UpgradeTower", "PressureRelieved",
            "EmotionToCalm", "EmotionToFocused", "EmotionToTense",
            "EmotionToCritical", "PortraitClickCalm", "PortraitClickFocused",
            "PortraitClickTense", "PortraitClickCritical", "PortraitRapidClickCalm",
            "PortraitRapidClickFocused", "PortraitRapidClickTense",
            "PortraitRapidClickCritical", "Saving", "GreatTile", "Branch",
            "Discount"
        };
        for (int categoryIndex = 0; categoryIndex < categories.Length;
             categoryIndex++)
        for (int tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
            dialogueCount += definition.GetDialogueLines(categories[categoryIndex],
                tiers[tierIndex]).Length;
        for (int tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
            dialogueCount += definition.GetVictoryLines(tiers[tierIndex]).Length;
        for (int tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
            dialogueCount += definition.GetDefeatLines(tiers[tierIndex]).Length;

        RougeAutoplayCommanderJson.ClearCache();
        return $"Commander JSON valid: {definition.Name} ({definition.CommanderId}, " +
               $"{definition.LocaleId}), " +
               $"{definition.BuildOrder.Length} tower IDs, {dialogueCount} dialogue lines, " +
               $"portrait {portrait.width}x{portrait.height}, " +
               $"{validationProbeCount} validation probes.\n" +
               report;
    }

    private static int RunValidationSmokeTests(string validCoreJson,
        string validLocaleJson)
    {
        int probeCount = 0;
        probeCount += AssertRejected(ReplaceOnce(validCoreJson, "{",
                "{\n  \"unexpectedField\": true,"),
            validLocaleJson,
            "unknown field");
        probeCount += AssertRejected(validCoreJson,
            ReplaceOnce(validLocaleJson, "{",
                "{\n  \"unexpectedField\": true,"),
            "locale unknown field");
        probeCount += AssertRejected(validCoreJson,
            ReplaceOnce(MutateLocale(validLocaleJson, _ => { }),
                "\"schemaVersion\":1,", string.Empty),
            "locale missing required field");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.identity.displayName = "<b>岚</b>"),
            "rich-text injection");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.talent.costMultiplier = 0.5f), validLocaleJson,
            definition => Approximately(definition.Source.talent.costMultiplier, 1f),
            "talent.costMultiplier", "talent cost multiplier authority");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.strategy.capitalActionIntervalSeconds = 0.1f), validLocaleJson,
            definition => Approximately(
                definition.Source.strategy.capitalActionIntervalSeconds, 0.65f),
            "strategy.capitalActionIntervalSeconds",
            "capital action cadence authority");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.strategy.emergencyActionIntervalSeconds = 4f), validLocaleJson,
            definition => Approximately(
                definition.Source.strategy.emergencyActionIntervalSeconds, 0.24f),
            "strategy.emergencyActionIntervalSeconds",
            "emergency action cadence authority");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.strategy.modePriorities.economy =
                    data.strategy.modePriorities.opening), validLocaleJson,
            definition => HasValidModePriorityPermutation(
                definition.Source.strategy.modePriorities),
            "strategy.modePriorities.economy",
            "duplicate mode priority convergence");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
        {
            data.strategy.personalityRegretBudget = 0.02f;
            data.strategy.bossRegretBudget = 0.1f;
        }), validLocaleJson,
            definition => Approximately(
                definition.Source.strategy.bossRegretBudget, 0.02f),
            "strategy.bossRegretBudget", "linked boss regret budget clamp");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
        {
            data.dialogue.intervalMinimumSeconds = 120f;
            data.dialogue.intervalMaximumSeconds = 2f;
        }), validLocaleJson,
            definition => Approximately(
                definition.Source.dialogue.intervalMaximumSeconds, 120f),
            "dialogue.intervalMaximumSeconds", "linked dialogue interval clamp");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
        {
            data.dialogue.familiarThreshold = 99;
            data.dialogue.closeThreshold = 1;
        }), validLocaleJson,
            definition => definition.Source.dialogue.closeThreshold == 100,
            "dialogue.closeThreshold", "linked affinity threshold clamp");
        probeCount += AssertRejected(MutateCore(validCoreJson, data =>
                data.strategy.buildOrder[0] = "0"),
            validLocaleJson,
            "numeric tower ID");
        probeCount += AssertRejected(MutateCore(validCoreJson, data =>
                data.commanderId = string.Equals(data.commanderId, "probe",
                    StringComparison.Ordinal) ? "probe2" : "probe"),
            validLocaleJson,
            "core and locale commanderId mismatch");
        probeCount += AssertRejected(ReplaceOnce(
                MutateCore(validCoreJson, _ => { }),
                "\"portraitResourceFolder\":",
                "\"displayName\":\"岚\",\"portraitResourceFolder\":"),
            validLocaleJson,
            "localized text injected into commander core");
        probeCount += AssertRejected(validCoreJson, ReplaceOnce(
                MutateLocale(validLocaleJson, _ => { }),
                "\"category\":", "\"priority\":20,\"category\":"),
            "priority injected into commander locale");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.dialogue.sets[0].category = "UnknownCategory"),
            "unknown dialogue category in commander locale");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
            {
                RougeAutoplayCommanderDialogueSetConfig[] source =
                    data.dialogue.sets;
                RougeAutoplayCommanderDialogueSetConfig[] shortened =
                    new RougeAutoplayCommanderDialogueSetConfig[source.Length - 1];
                Array.Copy(source, 1, shortened, 0, shortened.Length);
                data.dialogue.sets = shortened;
            }),
            "missing required dialogue category in commander locale");

        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.mainTowerBurstWindowSeconds = 0.49f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.mainTowerBurstWindowSeconds, 0.5f),
            "dialogue.triggers.mainTowerBurstWindowSeconds",
            "main-tower burst window below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.mainTowerBurstWindowSeconds = 10.01f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.mainTowerBurstWindowSeconds, 10f),
            "dialogue.triggers.mainTowerBurstWindowSeconds",
            "main-tower burst window above maximum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.mainTowerBurstHealthLossPercent = 0.99f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.mainTowerBurstHealthLossPercent, 1f),
            "dialogue.triggers.mainTowerBurstHealthLossPercent",
            "main-tower burst damage percent below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.mainTowerBurstHealthLossPercent = 50.01f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.mainTowerBurstHealthLossPercent, 50f),
            "dialogue.triggers.mainTowerBurstHealthLossPercent",
            "main-tower burst damage percent above maximum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.thresholds.flowObservationWindowSeconds = 1.99f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .thresholds.flowObservationWindowSeconds, 2f),
            "dialogue.thresholds.flowObservationWindowSeconds",
            "flow observation window below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.thresholds.lowKillSpawnRatio = 0.09f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .thresholds.lowKillSpawnRatio, 0.1f),
            "dialogue.thresholds.lowKillSpawnRatio",
            "kill/spawn ratio below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.thresholds.nearBaseDistanceCells = 0.49f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .thresholds.nearBaseDistanceCells, 0.5f),
            "dialogue.thresholds.nearBaseDistanceCells",
            "near-base distance below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.thresholds.nearBaseSustainSeconds = 10.01f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .thresholds.nearBaseSustainSeconds, 10f),
            "dialogue.thresholds.nearBaseSustainSeconds",
            "near-base sustain time above maximum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.thresholds.economyObservationWindowSeconds = 9.99f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .thresholds.economyObservationWindowSeconds, 10f),
            "dialogue.thresholds.economyObservationWindowSeconds",
            "economy observation window below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.thresholds.lowIncomeSpendRatio = 1.21f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .thresholds.lowIncomeSpendRatio, 1.2f),
            "dialogue.thresholds.lowIncomeSpendRatio",
            "income/spend ratio above maximum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
        {
            float[] minutes = data.dialogue.triggers.lateFirstTakeoverMinutes;
            minutes[1] = minutes[0];
        }), validLocaleJson, definition =>
        {
            float[] minutes = definition.Source.dialogue.triggers
                .lateFirstTakeoverMinutes;
            return minutes[1] > minutes[0] && minutes[2] > minutes[1] &&
                   minutes[3] > minutes[2];
        }, "dialogue.triggers.lateFirstTakeoverMinutes[1]",
            "non-increasing late first-takeover minutes");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.lateFirstTakeoverMinutes[0] = 0.99f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.lateFirstTakeoverMinutes[0], 1f),
            "dialogue.triggers.lateFirstTakeoverMinutes[0]",
            "late first-takeover minute below minimum");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.dialogue.sets[0].distant = Array.Empty<string>()),
            "empty Distant affinity dialogue array");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.dialogue.sets[0].familiar = Array.Empty<string>()),
            "empty Familiar affinity dialogue array");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.dialogue.sets[0].close = Array.Empty<string>()),
            "empty Close affinity dialogue array");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.dialogue.sets[0].close =
                    data.dialogue.sets[0].familiar),
            "identical Familiar and Close dialogue sets");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.outcomes.victory.close = Array.Empty<string>()),
            "empty Close victory dialogue array");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.outcomes.victory.close = data.outcomes.victory.familiar),
            "identical Familiar and Close victory dialogue sets");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.outcomes.defeat.close = Array.Empty<string>()),
            "empty Close defeat dialogue array");
        probeCount += AssertRejected(validCoreJson,
            MutateLocale(validLocaleJson, data =>
                data.outcomes.defeat.close = data.outcomes.defeat.familiar),
            "identical Familiar and Close defeat dialogue sets");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.towerBuildDialogueCooldownSeconds = 4.99f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.towerBuildDialogueCooldownSeconds, 5f),
            "dialogue.triggers.towerBuildDialogueCooldownSeconds",
            "tower-build dialogue cooldown below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.towerUpgradeDialogueCooldownSeconds = 4.99f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.towerUpgradeDialogueCooldownSeconds, 5f),
            "dialogue.triggers.towerUpgradeDialogueCooldownSeconds",
            "tower-upgrade dialogue cooldown below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.pressureReliefConfirmLowSeconds = 0.49f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.pressureReliefConfirmLowSeconds, 0.5f),
            "dialogue.triggers.pressureReliefConfirmLowSeconds",
            "pressure-relief confirmation below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.bossHealthCriticalRatio =
                    data.dialogue.triggers.bossHealthWarningRatio),
            validLocaleJson, definition => definition.Source.dialogue.triggers
                .bossHealthCriticalRatio < definition.Source.dialogue.triggers
                .bossHealthWarningRatio,
            "dialogue.triggers.bossHealthCriticalRatio",
            "boss critical-health ratio not below warning ratio");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.bossHealthFinalRatio =
                    data.dialogue.triggers.bossHealthCriticalRatio),
            validLocaleJson, definition => definition.Source.dialogue.triggers
                .bossHealthFinalRatio < definition.Source.dialogue.triggers
                .bossHealthCriticalRatio,
            "dialogue.triggers.bossHealthFinalRatio",
            "boss final-health ratio not below critical ratio");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.emotions.tenseTensionThreshold =
                    data.dialogue.emotions.focusedTensionThreshold),
            validLocaleJson, definition => definition.Source.dialogue.emotions
                .tenseTensionThreshold > definition.Source.dialogue.emotions
                .focusedTensionThreshold,
            "dialogue.emotions.tenseTensionThreshold",
            "non-increasing emotion tension thresholds");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
        {
            data.dialogue.emotions.calmIntervalMultiplier = 0.8f;
            data.dialogue.emotions.focusedIntervalMultiplier = 1.2f;
        }), validLocaleJson, definition =>
        {
            RougeAutoplayCommanderEmotionConfig emotion =
                definition.Source.dialogue.emotions;
            return emotion.calmIntervalMultiplier >=
                   emotion.focusedIntervalMultiplier &&
                   emotion.focusedIntervalMultiplier >=
                   emotion.tenseIntervalMultiplier &&
                   emotion.tenseIntervalMultiplier >=
                   emotion.criticalIntervalMultiplier;
        }, "dialogue.emotions.focusedIntervalMultiplier",
            "incorrect emotion interval-multiplier order");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.portraitClickDialogueCooldownSeconds = 0.09f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.portraitClickDialogueCooldownSeconds, 0.1f),
            "dialogue.triggers.portraitClickDialogueCooldownSeconds",
            "portrait-click dialogue cooldown below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.portraitRapidClickCount = 2),
            validLocaleJson, definition => definition.Source.dialogue.triggers
                .portraitRapidClickCount == 3,
            "dialogue.triggers.portraitRapidClickCount",
            "portrait rapid-click count below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.portraitRapidClickCount = 13),
            validLocaleJson, definition => definition.Source.dialogue.triggers
                .portraitRapidClickCount == 12,
            "dialogue.triggers.portraitRapidClickCount",
            "portrait rapid-click count above maximum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.portraitRapidClickWindowSeconds = 0.49f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.portraitRapidClickWindowSeconds, 0.5f),
            "dialogue.triggers.portraitRapidClickWindowSeconds",
            "portrait rapid-click window below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.portraitRapidClickWindowSeconds = 5.01f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.portraitRapidClickWindowSeconds, 5f),
            "dialogue.triggers.portraitRapidClickWindowSeconds",
            "portrait rapid-click window above maximum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.triggers.portraitRapidClickDialogueCooldownSeconds =
                    0.49f),
            validLocaleJson, definition => Approximately(definition.Source.dialogue
                .triggers.portraitRapidClickDialogueCooldownSeconds, 0.5f),
            "dialogue.triggers.portraitRapidClickDialogueCooldownSeconds",
            "portrait rapid-click dialogue cooldown below minimum");
        probeCount += AssertCoreNormalized(MutateCore(validCoreJson, data =>
                data.dialogue.sets[0].priority = 0), validLocaleJson,
            definition => definition.GetDialoguePriority(
                definition.Source.dialogue.sets[0].category) == 1,
            "dialogue.sets[0].priority", "dialogue priority below minimum");
        probeCount += AssertCompileRejected(validCoreJson, validLocaleJson,
            data => data.strategy.waveForecastSeconds = float.NaN,
            "non-finite core number");
        probeCount += AssertCompileRejected(validCoreJson, validLocaleJson,
            data => data.talent.costMultiplier = float.PositiveInfinity,
            "non-finite engine-authoritative number");
        return probeCount;
    }

    private static string MutateCore(string validJson,
        Action<RougeAutoplayCommanderConfigData> mutation)
    {
        RougeAutoplayCommanderConfigData data =
            JsonUtility.FromJson<RougeAutoplayCommanderConfigData>(validJson);
        if (data == null)
            throw new InvalidOperationException(
                "Could not clone valid commander JSON for a negative probe.");
        mutation(data);
        return JsonUtility.ToJson(data);
    }

    private static string MutateLocale(string validJson,
        Action<RougeAutoplayCommanderLocaleData> mutation)
    {
        RougeAutoplayCommanderLocaleData data =
            JsonUtility.FromJson<RougeAutoplayCommanderLocaleData>(validJson);
        if (data == null)
            throw new InvalidOperationException(
                "Could not clone valid commander locale JSON for a negative probe.");
        mutation(data);
        return JsonUtility.ToJson(data);
    }

    private static int AssertRejected(string coreJson, string localeJson,
        string scenario)
    {
        if (!RougeAutoplayCommanderJson.TryParse(coreJson, localeJson,
                out _, out _)) return 1;
        throw new InvalidOperationException(
            "Commander validator accepted negative probe: " + scenario);
    }

    private static int AssertCoreNormalized(string coreJson, string localeJson,
        Func<RougeAutoplayCommanderDefinition, bool> assertion,
        string expectedWarningPath, string scenario)
    {
        if (!RougeAutoplayCommanderJson.TryParse(coreJson, localeJson,
                out RougeAutoplayCommanderDefinition definition,
                out string report))
            throw new InvalidOperationException(
                "Commander validator rejected recoverable numeric probe: " +
                scenario + "\n" + report);
        if (definition == null || assertion == null || !assertion(definition))
            throw new InvalidOperationException(
                "Commander validator did not normalize numeric probe as expected: " +
                scenario);
        if (string.IsNullOrWhiteSpace(expectedWarningPath) ||
            report.IndexOf(expectedWarningPath, StringComparison.Ordinal) < 0)
            throw new InvalidOperationException(
                "Commander validator normalized a numeric probe without reporting " +
                "its path: " + scenario);
        return 1;
    }

    private static int AssertCompileRejected(string validCoreJson,
        string validLocaleJson, Action<RougeAutoplayCommanderConfigData> mutation,
        string scenario)
    {
        RougeAutoplayCommanderConfigData core =
            JsonUtility.FromJson<RougeAutoplayCommanderConfigData>(validCoreJson);
        RougeAutoplayCommanderLocaleData locale =
            JsonUtility.FromJson<RougeAutoplayCommanderLocaleData>(validLocaleJson);
        if (core == null || locale == null)
            throw new InvalidOperationException(
                "Could not clone commander documents for compile rejection probe.");
        mutation(core);
        if (!RougeAutoplayCommanderJson.TryCompile(core, locale, out _, out _))
            return 1;
        throw new InvalidOperationException(
            "Commander compiler accepted negative probe: " + scenario);
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) <= 0.00001f;
    }

    private static bool HasValidModePriorityPermutation(
        RougeAutoplayCommanderModePriorityConfig priorities)
    {
        int[] values =
        {
            priorities.opening, priorities.economy, priorities.hold,
            priorities.prepareBoss, priorities.bossFight, priorities.emergency
        };
        bool[] seen = new bool[7];
        for (int i = 0; i < values.Length; i++)
        {
            int value = values[i];
            if (value < 1 || value > 6 || seen[value]) return false;
            seen[value] = true;
        }
        return true;
    }

    private static string ReplaceOnce(string source, string oldValue,
        string newValue)
    {
        int index = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException(
                "Negative probe fixture was not found: " + oldValue);
        return source.Substring(0, index) + newValue +
               source.Substring(index + oldValue.Length);
    }
}
