using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class RougeAutoplayCommanderConfigData
{
    public int schemaVersion = 2;
    public string protocol = RougeAutoplayCommanderJson.Protocol;
    public string commanderId = "lan";
    public string defaultLocale = "zh-CN";
    public RougeAutoplayCommanderVisualConfig visuals =
        new RougeAutoplayCommanderVisualConfig();
    public RougeAutoplayCommanderTalentConfig talent =
        new RougeAutoplayCommanderTalentConfig();
    public RougeAutoplayCommanderPersonalityConfig personality =
        new RougeAutoplayCommanderPersonalityConfig();
    public RougeAutoplayCommanderStrategyConfig strategy =
        new RougeAutoplayCommanderStrategyConfig();
    public RougeAutoplayCommanderDialogueConfig dialogue =
        new RougeAutoplayCommanderDialogueConfig();
}

[Serializable]
public sealed class RougeAutoplayCommanderVisualConfig
{
    // Empty means the built-in Lan portrait set. Otherwise the only accepted value
    // is Commanders/<commanderId>/Portraits.
    public string portraitResourceFolder = string.Empty;
}

[Serializable]
public sealed class RougeAutoplayCommanderIdentityConfig
{
    public string displayName = "岚";
    public string role = "本地战术托管指挥官";
    public string personaLabel = "细致 · 直率 · 有点护短";
    public string background = string.Empty;
    public string speakingStyle = string.Empty;
    public string[] personalityTraits = Array.Empty<string>();
}

[Serializable]
public sealed class RougeAutoplayCommanderTalentConfig
{
    public float costMultiplier = 1f;
}

[Serializable]
public sealed class RougeAutoplayCommanderPersonalityConfig
{
    public RougeAutoplayCommanderConcernConfig concerns =
        new RougeAutoplayCommanderConcernConfig();
    public RougeAutoplayCommanderBiasConfig biases =
        new RougeAutoplayCommanderBiasConfig();
}

[Serializable]
public sealed class RougeAutoplayCommanderConcernConfig
{
    public float crowd = 1f;
    public float elite = 1.05f;
    public float boss = 1.12f;
    public float urgent = 1.15f;
}

[Serializable]
public sealed class RougeAutoplayCommanderBiasConfig
{
    public float save = 1.08f;
    public float build = 1f;
    public float controlTower = 1.08f;
    public float focusedTower = 1.04f;
    public float areaTower = 0.98f;
    public float defense = 1.14f;
    public float specialTile = 1.16f;
    public float upgrade = 1.03f;
    public float redeploy = 1.1f;
}

[Serializable]
public sealed class RougeAutoplayCommanderStrategyConfig
{
    public string[] buildOrder =
    {
        "MachineGun", "Ice", "Cannon", "Flame", "Laser",
        "RocketBarrage", "OrbitSphere", "PiercingLaser"
    };
    public int openingTowerCount = 3;
    public float expansionIntervalSeconds = 38f;
    public float capitalActionIntervalSeconds = 0.65f;
    public float emergencyActionIntervalSeconds = 0.24f;
    public float strategyHoldSeconds = 2.5f;
    public float waveForecastSeconds = 18f;
    public float bossPreparationLeadSeconds = 150f;
    public float saleCooldownSeconds = 45f;
    public float minimumTowerAgeBeforeSaleSeconds = 35f;
    public float personalityRegretBudget = 0.08f;
    public float bossRegretBudget = 0.035f;
    public float maximumPreferenceShift = 0.04f;
    public RougeAutoplayCommanderSkillConfig skills =
        new RougeAutoplayCommanderSkillConfig();
    public RougeAutoplayCommanderThresholdConfig thresholds =
        new RougeAutoplayCommanderThresholdConfig();
    public RougeAutoplayCommanderModePriorityConfig modePriorities =
        new RougeAutoplayCommanderModePriorityConfig();
}

[Serializable]
public sealed class RougeAutoplayCommanderSkillConfig
{
    public float mapReading = 1f;
    public float threatReading = 1f;
    public float crisisResponse = 1f;
    public float adaptation = 1f;
}

[Serializable]
public sealed class RougeAutoplayCommanderThresholdConfig
{
    public float emergencyMainTowerHealthRatio = 0.45f;
    public float emergencyUrgentPressureMinimum = 3f;
    public float emergencyUrgentPressureFraction = 0.2f;
    public float emergencyImminentPressure = 16f;
    public float prepareBossProgress = 0.32f;
    public int economyMaximumActiveEnemies = 4;
    public float economyMaximumIncomingPressure = 5f;
    public float economyMinimumNextWaveSeconds = 6f;
    public float economyMinimumMainTowerHealthRatio = 0.78f;

    public float highUrgentPressure = 5f;
    public float highPeakPressure = 14f;
    public float mediumUrgentPressure = 2f;
    public int mediumActiveEnemies = 16;
    public float mediumIncomingPressure = 9f;
    public float highBossPreparation = 0.72f;
    public float mediumBossPreparation = 0.28f;
    public float criticalCrisisHealthRatio = 0.35f;
    public float lowCrisisHealthRatio = 0.7f;

    public int redeployMinimumExtraTowers = 2;
    public float redeployMinimumHealthRatio = 0.72f;
    public float redeployMaximumUrgentPressure = 1.5f;
    public int redeployMaximumActiveEnemies = 18;
    public float redeployMaximumBossPreparation = 0.18f;

    public float immediateDefenseHealthRatio = 0.9f;
    public float immediateDefenseUrgentPressure = 1f;
    public int immediateDefenseActiveEnemies = 10;
    public float valuableSpecialTileScore = 105f;
    public float coverageUrgentPressure = 2f;
    public int coverageActiveEnemies = 12;
    public float coverageHealthRatio = 0.7f;
}

[Serializable]
public sealed class RougeAutoplayCommanderModeLabelConfig
{
    public string opening = "展开阵地";
    public string economy = "蓄势";
    public string hold = "稳线";
    public string prepareBoss = "备战首领";
    public string bossFight = "集火首领";
    public string emergency = "紧急守家";
}

[Serializable]
public sealed class RougeAutoplayCommanderModePriorityConfig
{
    public int opening = 4;
    public int economy = 1;
    public int hold = 2;
    public int prepareBoss = 3;
    public int bossFight = 5;
    public int emergency = 6;
}

[Serializable]
public sealed class RougeAutoplayCommanderDialogueConfig
{
    public int startingAffinity = 15;
    public int familiarThreshold = 30;
    public int closeThreshold = 70;
    public float intervalMinimumSeconds = 14f;
    public float intervalMaximumSeconds = 22f;
    public float preemptionCooldownSeconds = 7f;
    public int recentHistorySize = 4;
    public RougeAutoplayCommanderDialogueThresholdConfig thresholds =
        new RougeAutoplayCommanderDialogueThresholdConfig();
    public RougeAutoplayCommanderDialogueTriggerConfig triggers =
        new RougeAutoplayCommanderDialogueTriggerConfig();
    public RougeAutoplayCommanderEmotionConfig emotions =
        new RougeAutoplayCommanderEmotionConfig();
    public RougeAutoplayCommanderDialogueRuleConfig[] sets =
        Array.Empty<RougeAutoplayCommanderDialogueRuleConfig>();
}

[Serializable]
public sealed class RougeAutoplayCommanderDialogueThresholdConfig
{
    public float baseCriticalHealthRatio = 0.25f;
    public float baseLowHealthRatio = 0.5f;
    public float urgentPressureMinimum = 2f;
    public float urgentPressureFraction = 0.18f;
    public float hardConcernMinimum = 2f;
    public float hardVersusCrowdFactor = 0.42f;
    public int crowdEnemyCount = 8;
    public float crowdConcernMinimum = 6f;
    public float flowObservationWindowSeconds = 8f;
    public float lowKillSpawnRatio = 0.8f;
    public float nearBaseDistanceCells = 3f;
    public float nearBaseSustainSeconds = 1.5f;
    public float economyObservationWindowSeconds = 30f;
    public float lowIncomeSpendRatio = 0.75f;
}

[Serializable]
public sealed class RougeAutoplayCommanderDialogueTriggerConfig
{
    public float[] lateFirstTakeoverMinutes = { 3f, 6f, 9f, 12f };
    public float mainTowerBurstWindowSeconds = 4f;
    public float mainTowerBurstHealthLossPercent = 10f;
    public float mainTowerHitDialogueChance = 0.3f;
    public float mainTowerHitDialogueCooldownSeconds = 8f;
    public float mainTowerBurstDialogueCooldownSeconds = 12f;
    public float towerBuildDialogueChance = 0.3f;
    public float towerBuildDialogueCooldownSeconds = 28f;
    public float towerUpgradeDialogueChance = 0.28f;
    public float towerUpgradeDialogueCooldownSeconds = 32f;
    public float pressureReliefMinimumHighSeconds = 6f;
    public float pressureReliefConfirmLowSeconds = 2f;
    public float pressureReliefDialogueCooldownSeconds = 30f;
    public float bossHealthWarningRatio = 0.5f;
    public float bossHealthCriticalRatio = 0.25f;
    public float portraitClickDialogueCooldownSeconds = 0.35f;
    public int portraitRapidClickCount = 5;
    public float portraitRapidClickWindowSeconds = 2f;
    public float portraitRapidClickDialogueCooldownSeconds = 1.5f;
}

[Serializable]
public sealed class RougeAutoplayCommanderEmotionConfig
{
    public float focusedTensionThreshold = 0.3f;
    public float tenseTensionThreshold = 0.58f;
    public float criticalTensionThreshold = 0.82f;
    public float transitionConfirmSeconds = 2f;
    public float transitionDialogueCooldownSeconds = 8f;
    public float calmIntervalMultiplier = 1.12f;
    public float focusedIntervalMultiplier = 1.03f;
    public float tenseIntervalMultiplier = 0.94f;
    public float criticalIntervalMultiplier = 0.86f;
}

[Serializable]
public sealed class RougeAutoplayCommanderDialogueRuleConfig
{
    public string category = string.Empty;
    public int priority = 1;
    public bool battleState;
}

[Serializable]
public sealed class RougeAutoplayCommanderDialogueSetConfig
{
    public string category = string.Empty;
    public string[] distant = Array.Empty<string>();
    public string[] familiar = Array.Empty<string>();
    public string[] close = Array.Empty<string>();
}

[Serializable]
public sealed class RougeAutoplayCommanderOutcomeConfig
{
    public RougeAutoplayCommanderAffinityLinesConfig defeat =
        new RougeAutoplayCommanderAffinityLinesConfig
        {
            distant = new[] { "指挥官，主塔失去响应。链接正在断开。" },
            familiar = new[]
            {
                "指挥官……主塔失去响应了。\n我会留到最后。链接……正在断开。"
            },
            close = new[] { "别回头，指挥官。\n让我陪你守到链接的最后一秒。" }
        };
}

[Serializable]
public sealed class RougeAutoplayCommanderAffinityLinesConfig
{
    public string[] distant = Array.Empty<string>();
    public string[] familiar = Array.Empty<string>();
    public string[] close = Array.Empty<string>();
}

[Serializable]
public sealed class RougeAutoplayCommanderLocaleData
{
    public int schemaVersion = 1;
    public string protocol = RougeAutoplayCommanderJson.LocaleProtocol;
    public string commanderId = "lan";
    public string locale = "zh-CN";
    public RougeAutoplayCommanderIdentityConfig identity =
        new RougeAutoplayCommanderIdentityConfig();
    public RougeAutoplayCommanderTalentLocaleConfig talent =
        new RougeAutoplayCommanderTalentLocaleConfig();
    public RougeAutoplayCommanderPersonalityLocaleConfig personality =
        new RougeAutoplayCommanderPersonalityLocaleConfig();
    public RougeAutoplayCommanderStrategyLocaleConfig strategy =
        new RougeAutoplayCommanderStrategyLocaleConfig();
    public RougeAutoplayCommanderDialogueLocaleConfig dialogue =
        new RougeAutoplayCommanderDialogueLocaleConfig();
    public RougeAutoplayCommanderOutcomeConfig outcomes =
        new RougeAutoplayCommanderOutcomeConfig();
}

[Serializable]
public sealed class RougeAutoplayCommanderTalentLocaleConfig
{
    public string name = "标准权限";
    public string description =
        "建造、升级、出售与塔属性全部遵循玩家规则，不获得额外资源或数值加成。";
}

[Serializable]
public sealed class RougeAutoplayCommanderPersonalityLocaleConfig
{
    public string thinkingStyle = string.Empty;
    public string[] decisionPrinciples = Array.Empty<string>();
}

[Serializable]
public sealed class RougeAutoplayCommanderStrategyLocaleConfig
{
    public RougeAutoplayCommanderModeLabelConfig modeLabels =
        new RougeAutoplayCommanderModeLabelConfig();
}

[Serializable]
public sealed class RougeAutoplayCommanderDialogueLocaleConfig
{
    public string distantLabel = "生疏";
    public string familiarLabel = "熟悉";
    public string closeLabel = "亲近";
    public RougeAutoplayCommanderDialogueSetConfig[] sets =
        Array.Empty<RougeAutoplayCommanderDialogueSetConfig>();
}

public enum RougeAutoplayCommanderPortraitEmotion
{
    Calm,
    Focused,
    Tense,
    Critical
}

public enum RougeAutoplayCommanderPortraitVariant
{
    Base,
    Click,
    RapidClick,
    Defeat
}

/// <summary>
/// Validated, runtime-ready commander data. Strings and lookup tables deliberately
/// stay managed; Burst jobs receive only objective battlefield data and never touch
/// this object.
/// </summary>
public sealed class RougeAutoplayCommanderDefinition
{
    private readonly Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig>
        _dialogueRulesByCategory;
    private readonly Dictionary<string, RougeAutoplayCommanderDialogueSetConfig>
        _localizedDialogueByCategory;
    private readonly Dictionary<string, Sprite> _portraitSpritesByPath =
        new Dictionary<string, Sprite>(StringComparer.Ordinal);
    private readonly string _portraitResourceFolder;
    private readonly string _portraitResourcePath;

    public RougeAutoplayCommanderConfigData Source { get; }
    public RougeAutoplayCommanderLocaleData Locale { get; }
    public RougeTowerType[] BuildOrder { get; }

    public string CommanderId => Source.commanderId;
    public string LocaleId => Locale.locale;
    public string Name => Locale.identity.displayName;
    public string Role => Locale.identity.role;
    public string Persona => Locale.identity.personaLabel;
    public string Background => Locale.identity.background;
    public string PortraitResourceFolder => _portraitResourceFolder;
    public string PortraitResourcePath => _portraitResourcePath;
    public string TalentName => Locale.talent.name;
    public string TalentDescription => Locale.talent.description;
    public string DistantAffinityLabel => Locale.dialogue.distantLabel;
    public string FamiliarAffinityLabel => Locale.dialogue.familiarLabel;
    public string CloseAffinityLabel => Locale.dialogue.closeLabel;
    public RougeAutoplayCommanderModeLabelConfig ModeLabels =>
        Locale.strategy.modeLabels;
    public float CostMultiplier => Source.talent.costMultiplier;
    public float CrowdConcern => Source.personality.concerns.crowd;
    public float EliteConcern => Source.personality.concerns.elite;
    public float BossConcern => Source.personality.concerns.boss;
    public float UrgentConcern => Source.personality.concerns.urgent;
    public float SaveBias => Source.personality.biases.save;
    public float BuildBias => Source.personality.biases.build;
    public float ControlTowerBias => Source.personality.biases.controlTower;
    public float FocusedTowerBias => Source.personality.biases.focusedTower;
    public float AreaTowerBias => Source.personality.biases.areaTower;
    public float DefenseBias => Source.personality.biases.defense;
    public float SpecialTileBias => Source.personality.biases.specialTile;
    public float UpgradeBias => Source.personality.biases.upgrade;
    public float RedeployBias => Source.personality.biases.redeploy;
    public float BossPreparationLeadSeconds =>
        Source.strategy.bossPreparationLeadSeconds;
    public string AffinityPreferenceKey =>
        "Rouge.Autoplay." + CommanderId + ".Affinity";

    internal RougeAutoplayCommanderDefinition(
        RougeAutoplayCommanderConfigData source,
        RougeAutoplayCommanderLocaleData locale, RougeTowerType[] buildOrder,
        Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig>
            dialogueRulesByCategory,
        Dictionary<string, RougeAutoplayCommanderDialogueSetConfig>
            localizedDialogueByCategory,
        string portraitResourceFolder, string portraitResourcePath)
    {
        Source = source;
        Locale = locale;
        BuildOrder = buildOrder;
        _dialogueRulesByCategory = dialogueRulesByCategory;
        _localizedDialogueByCategory = localizedDialogueByCategory;
        _portraitResourceFolder = portraitResourceFolder;
        _portraitResourcePath = portraitResourcePath;
    }

    public Sprite ResolvePortraitSprite(
        RougeAutoplayCommanderPortraitEmotion emotion,
        RougeAutoplayCommanderPortraitVariant variant)
    {
        string requestedPath = _portraitResourceFolder + "/" +
                               GetPortraitFileName(emotion, variant);
        Sprite sprite = LoadPortraitSpriteSilently(requestedPath);
        if (sprite != null) return sprite;

        string commanderBasePath = _portraitResourceFolder + "/base_calm";
        if (!string.Equals(requestedPath, commanderBasePath,
                StringComparison.Ordinal))
            sprite = LoadPortraitSpriteSilently(commanderBasePath);
        if (sprite != null) return sprite;

        return LoadPortraitSpriteSilently(
            RougeAutoplayCommanderJson.DefaultPortraitResourcePath);
    }

    private Sprite LoadPortraitSpriteSilently(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;
        if (_portraitSpritesByPath.TryGetValue(resourcePath,
                out Sprite cached))
            return cached;

        // RougeSpriteAssets logs missing textures. Optional portrait variants are
        // probed first so their absence remains an ordinary, silent fallback.
        Sprite sprite = Resources.Load<Texture2D>(resourcePath) != null
            ? RougeSpriteAssets.Load(resourcePath)
            : null;
        _portraitSpritesByPath[resourcePath] = sprite;
        return sprite;
    }

    private static string GetPortraitFileName(
        RougeAutoplayCommanderPortraitEmotion emotion,
        RougeAutoplayCommanderPortraitVariant variant)
    {
        if (variant == RougeAutoplayCommanderPortraitVariant.Defeat)
            return "defeat";
        string state = emotion == RougeAutoplayCommanderPortraitEmotion.Focused
            ? "focused"
            : emotion == RougeAutoplayCommanderPortraitEmotion.Tense
                ? "tense"
                : emotion == RougeAutoplayCommanderPortraitEmotion.Critical
                    ? "critical"
                    : "calm";
        if (variant == RougeAutoplayCommanderPortraitVariant.Click)
            return "click_" + state;
        return variant == RougeAutoplayCommanderPortraitVariant.RapidClick
            ? "rapid_click_" + state
            : "base_" + state;
    }

    public string[] GetDialogueLines(string category, string affinityTier)
    {
        if (string.IsNullOrWhiteSpace(category) ||
            !_localizedDialogueByCategory.TryGetValue(category,
                out RougeAutoplayCommanderDialogueSetConfig set))
            return Array.Empty<string>();

        string[] selected = string.Equals(affinityTier, "Distant",
                StringComparison.OrdinalIgnoreCase)
            ? set.distant
            : string.Equals(affinityTier, "Close",
                StringComparison.OrdinalIgnoreCase)
                ? set.close
                : set.familiar;
        return selected ?? Array.Empty<string>();
    }

    public int GetDialoguePriority(string category)
    {
        return !string.IsNullOrWhiteSpace(category) &&
               _dialogueRulesByCategory.TryGetValue(category,
                   out RougeAutoplayCommanderDialogueRuleConfig set)
            ? set.priority
            : 1;
    }

    public bool IsBattleDialogue(string category)
    {
        return !string.IsNullOrWhiteSpace(category) &&
               _dialogueRulesByCategory.TryGetValue(category,
                   out RougeAutoplayCommanderDialogueRuleConfig set) &&
               set.battleState;
    }

    public string[] GetDefeatLines(string affinityTier)
    {
        RougeAutoplayCommanderAffinityLinesConfig defeat = Locale.outcomes?.defeat;
        if (defeat == null) return Array.Empty<string>();
        string[] selected = string.Equals(affinityTier, "Distant",
                StringComparison.OrdinalIgnoreCase)
            ? defeat.distant
            : string.Equals(affinityTier, "Close",
                StringComparison.OrdinalIgnoreCase)
                ? defeat.close
                : defeat.familiar;
        return selected ?? Array.Empty<string>();
    }
}

public static class RougeAutoplayCommanderJson
{
    [Serializable]
    private sealed class CommanderDiscoveryProbe
    {
        public int schemaVersion = -1;
        public string protocol = string.Empty;
        public string commanderId = string.Empty;
    }

    public const int SchemaVersion = 2;
    public const int LocaleSchemaVersion = 1;
    public const string Protocol = "red-vs-blue.commander/2";
    public const string LocaleProtocol = "red-vs-blue.commander-locale/1";
    public const string DefaultCommanderName = "lan";
    public const string DefaultLocale = "zh-CN";
    public const string DefaultPortraitResourceFolder =
        "Commanders/lan/Portraits";
    public const string DefaultPortraitResourcePath =
        DefaultPortraitResourceFolder + "/base_calm";
    private const int CoreMaximumBytes = 256 * 1024;
    private const int LocaleMaximumBytes = 512 * 1024;

    private static readonly string[] RequiredDialogueCategories =
    {
        "TakeoverFirst", "TakeoverQuickReturn", "TakeoverFrequentToggle",
        "TakeoverReturn", "TakeoverHighPressure", "TakeoverLateTier1",
        "TakeoverLateTier2", "TakeoverLateTier3", "TakeoverLateTier4",
        "ReleaseFirst", "Calm", "Crowd", "Hard", "BossArrival", "Boss",
        "BossHealthHalf", "BossHealthQuarter", "Urgent", "BaseLow",
        "BaseCritical", "BaseFirstDamage", "BaseDamaged",
        "BaseBurstDamage", "BuildTower", "UpgradeTower", "PressureRelieved",
        "EmotionToCalm", "EmotionToFocused", "EmotionToTense",
        "EmotionToCritical", "PortraitClickCalm", "PortraitClickFocused",
        "PortraitClickTense", "PortraitClickCritical", "PortraitRapidClickCalm",
        "PortraitRapidClickFocused", "PortraitRapidClickTense",
        "PortraitRapidClickCritical", "Saving", "GreatTile", "Branch",
        "Discount"
    };

    private static bool s_loadAttempted;
    private static RougeAutoplayCommanderDefinition s_active;
    private static string s_lastLoadReport = string.Empty;
    private static string s_selectedCommanderName = DefaultCommanderName;
    private static string s_selectedLocaleOverride = string.Empty;

    public static string SelectedCommanderName => s_selectedCommanderName;
    public static string SelectedLocaleOverride => s_selectedLocaleOverride;
    public static string ResourcePath =>
        BuildCoreResourcePath(s_selectedCommanderName);
    public static string AssetPath =>
        BuildCoreAssetPath(s_selectedCommanderName);
    public static string LocaleResourcePath => BuildLocaleResourcePath(
        s_selectedCommanderName,
        string.IsNullOrEmpty(s_selectedLocaleOverride)
            ? DefaultLocale
            : s_selectedLocaleOverride);
    public static string LocaleAssetPath => BuildLocaleAssetPath(
        s_selectedCommanderName,
        string.IsNullOrEmpty(s_selectedLocaleOverride)
            ? DefaultLocale
            : s_selectedLocaleOverride);

    public static RougeAutoplayCommanderDefinition Active
    {
        get
        {
            if (!s_loadAttempted)
            {
                s_loadAttempted = true;
                if (!TryLoad(out s_active, out s_lastLoadReport))
                {
                    Debug.LogError("Commander JSON rejected; using the safe fallback. " +
                                   s_lastLoadReport);
                    s_active = CreateFallback();
                }
            }
            return s_active;
        }
    }

    public static string LastLoadReport => s_lastLoadReport;

    /// <summary>
    /// Discovers commander core documents under Resources/Commanders. Discovery
    /// only identifies candidates; every returned id must still pass the normal
    /// strict package loader before it is exposed to the player.
    /// </summary>
    public static string[] DiscoverCommanderPackageNames(
        string preferredCommanderName = null)
    {
        string preferred = NormalizeCommanderFileName(preferredCommanderName);
        if (string.IsNullOrEmpty(preferred)) preferred = DefaultCommanderName;

        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal)
        {
            DefaultCommanderName
        };
        TextAsset[] assets = Resources.LoadAll<TextAsset>("Commanders");
        for (int i = 0; i < assets.Length; i++)
        {
            TextAsset asset = assets[i];
            if (asset == null ||
                !string.Equals(asset.name, "commander",
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(asset.text))
                continue;
            try
            {
                CommanderDiscoveryProbe probe =
                    JsonUtility.FromJson<CommanderDiscoveryProbe>(asset.text);
                if (probe == null || probe.schemaVersion != SchemaVersion ||
                    !string.Equals(probe.protocol, Protocol,
                        StringComparison.Ordinal) ||
                    !IsSafeIdentifier(probe.commanderId))
                    continue;
                names.Add(probe.commanderId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Commander discovery skipped unreadable core '" +
                                 asset.name + "': " + exception.Message);
            }
        }

        List<string> ordered = new List<string>(names);
        ordered.Sort((left, right) =>
        {
            int leftRank = string.Equals(left, preferred,
                    StringComparison.Ordinal)
                ? 0
                : string.Equals(left, DefaultCommanderName,
                    StringComparison.Ordinal)
                    ? 1
                    : 2;
            int rightRank = string.Equals(right, preferred,
                    StringComparison.Ordinal)
                ? 0
                : string.Equals(right, DefaultCommanderName,
                    StringComparison.Ordinal)
                    ? 1
                    : 2;
            int rankComparison = leftRank.CompareTo(rightRank);
            return rankComparison != 0
                ? rankComparison
                : string.Compare(left, right, StringComparison.Ordinal);
        });
        return ordered.ToArray();
    }

    /// <summary>
    /// Loads exactly one registry candidate through the existing strict package
    /// path. Unlike TryLoad, this method never substitutes a different commander.
    /// </summary>
    public static bool TryLoadRegistryPackage(string commanderName,
        string localeOverride, out RougeAutoplayCommanderDefinition definition,
        out string report)
    {
        definition = null;
        string normalizedCommander = NormalizeCommanderFileName(commanderName);
        if (string.IsNullOrEmpty(normalizedCommander))
        {
            report = "Commander package id is invalid: " + commanderName + ".";
            return false;
        }

        string normalizedLocale = string.IsNullOrWhiteSpace(localeOverride)
            ? string.Empty
            : localeOverride.Trim();
        if (!string.IsNullOrEmpty(normalizedLocale) &&
            !IsSafeLocaleIdentifier(normalizedLocale))
        {
            report = "Commander locale override is invalid: " +
                     normalizedLocale + ".";
            return false;
        }
        return TryLoadPackage(normalizedCommander, normalizedLocale,
            out definition, out report);
    }

    internal static RougeAutoplayCommanderDefinition
        CreateSafeBuiltInFallback()
    {
        return CreateFallback();
    }

    public static void ConfigureSelection(string commanderFileName,
        string localeOverride = null)
    {
        string normalizedCommander = NormalizeCommanderFileName(
            commanderFileName);
        if (string.IsNullOrEmpty(normalizedCommander))
        {
            normalizedCommander = DefaultCommanderName;
            if (!string.IsNullOrWhiteSpace(commanderFileName))
                Debug.LogWarning("Commander file name is invalid; using 'lan'. " +
                                 "Use lowercase letters, digits, '-' or '_'.");
        }

        string normalizedLocale = string.IsNullOrWhiteSpace(localeOverride)
            ? string.Empty
            : localeOverride.Trim();
        if (!string.IsNullOrEmpty(normalizedLocale) &&
            !IsSafeLocaleIdentifier(normalizedLocale))
        {
            Debug.LogWarning("Commander locale override is invalid; using the " +
                             "commander package default locale.");
            normalizedLocale = string.Empty;
        }

        if (string.Equals(s_selectedCommanderName, normalizedCommander,
                StringComparison.Ordinal) &&
            string.Equals(s_selectedLocaleOverride, normalizedLocale,
                StringComparison.Ordinal)) return;
        s_selectedCommanderName = normalizedCommander;
        s_selectedLocaleOverride = normalizedLocale;
        ClearCache();
    }

    public static void ClearCache()
    {
        s_loadAttempted = false;
        s_active = null;
        s_lastLoadReport = string.Empty;
    }

    public static bool TryLoad(out RougeAutoplayCommanderDefinition definition,
        out string report)
    {
        if (TryLoadPackage(s_selectedCommanderName, s_selectedLocaleOverride,
                out definition, out report))
            return true;

        string requestedReport = report;
        if (!string.Equals(s_selectedCommanderName, DefaultCommanderName,
                StringComparison.Ordinal) &&
            TryLoadPackage(DefaultCommanderName, s_selectedLocaleOverride,
                out definition, out string fallbackReport))
        {
            report = "Requested commander package '" + s_selectedCommanderName +
                     "' was rejected: " + requestedReport +
                     "\nLoaded the complete built-in 'lan' package instead. " +
                     fallbackReport;
            Debug.LogWarning(report);
            return true;
        }
        definition = null;
        report = requestedReport;
        return false;
    }

    private static bool TryLoadPackage(string commanderName,
        string localeOverride, out RougeAutoplayCommanderDefinition definition,
        out string report)
    {
        definition = null;
        string corePath = BuildCoreResourcePath(commanderName);
        TextAsset coreAsset = Resources.Load<TextAsset>(corePath);
        if (coreAsset == null || string.IsNullOrWhiteSpace(coreAsset.text))
        {
            report = "Missing or empty commander core: " +
                     BuildCoreAssetPath(commanderName);
            return false;
        }

        if (!TryDeserializeDocument(coreAsset.text, CoreMaximumBytes,
                "Commander core", out RougeAutoplayCommanderConfigData core,
                out report)) return false;
        if (!string.Equals(core.commanderId, commanderName,
                StringComparison.Ordinal))
        {
            report = "Commander folder name and commanderId must match exactly: " +
                     commanderName + " != " + core.commanderId + ".";
            return false;
        }
        if (!IsSafeLocaleIdentifier(core.defaultLocale))
        {
            report = "Commander defaultLocale is invalid: " + core.defaultLocale;
            return false;
        }

        string requestedLocale = string.IsNullOrWhiteSpace(localeOverride)
            ? core.defaultLocale
            : localeOverride;
        if (TryLoadLocale(coreAsset.text, commanderName, requestedLocale,
                out definition, out report)) return true;

        string requestedLocaleReport = report;
        if (!string.Equals(requestedLocale, core.defaultLocale,
                StringComparison.Ordinal) &&
            TryLoadLocale(coreAsset.text, commanderName, core.defaultLocale,
                out definition, out string defaultLocaleReport))
        {
            report = "Locale '" + requestedLocale + "' was rejected: " +
                     requestedLocaleReport + "\nLoaded package default locale '" +
                     core.defaultLocale + "'. " + defaultLocaleReport;
            Debug.LogWarning(report);
            return true;
        }

        definition = null;
        report = requestedLocaleReport;
        return false;
    }

    private static bool TryLoadLocale(string coreJson, string commanderName,
        string localeName, out RougeAutoplayCommanderDefinition definition,
        out string report)
    {
        definition = null;
        if (!IsSafeLocaleIdentifier(localeName))
        {
            report = "Locale name is invalid: " + localeName;
            return false;
        }
        string localePath = BuildLocaleResourcePath(commanderName, localeName);
        TextAsset localeAsset = Resources.Load<TextAsset>(localePath);
        if (localeAsset == null || string.IsNullOrWhiteSpace(localeAsset.text))
        {
            report = "Missing or empty commander locale: " +
                     BuildLocaleAssetPath(commanderName, localeName);
            return false;
        }
        if (!TryParse(coreJson, localeAsset.text, out definition, out report))
            return false;
        if (!string.Equals(definition.CommanderId, commanderName,
                StringComparison.Ordinal) ||
            !string.Equals(definition.LocaleId, localeName,
                StringComparison.Ordinal))
        {
            report = "Commander folder, commanderId and locale file name must " +
                     "match their document identifiers exactly.";
            definition = null;
            return false;
        }
        return true;
    }

    public static bool TryParse(string coreJson, string localeJson,
        out RougeAutoplayCommanderDefinition definition, out string report)
    {
        definition = null;
        if (!TryDeserializeDocument(coreJson, CoreMaximumBytes, "Commander core",
                out RougeAutoplayCommanderConfigData data, out report))
            return false;
        if (!TryDeserializeDocument(localeJson, LocaleMaximumBytes,
                "Commander locale", out RougeAutoplayCommanderLocaleData locale,
                out report)) return false;
        try
        {
            return TryCompile(data, locale, out definition, out report);
        }
        catch (Exception exception)
        {
            definition = null;
            report = "Commander JSON validation failed unexpectedly: " +
                     exception.Message;
            return false;
        }
    }

    private static bool TryDeserializeDocument<T>(string json, int maximumBytes,
        string label, out T data, out string report) where T : class
    {
        data = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            report = label + " JSON is empty.";
            return false;
        }
        if (Encoding.UTF8.GetByteCount(json) > maximumBytes)
        {
            report = label + " JSON exceeds its " + maximumBytes / 1024 +
                     " KiB limit.";
            return false;
        }
        if (!RougeStrictJsonShapeValidator.TryValidate<T>(json,
                out string shapeReport))
        {
            report = label + ": " + shapeReport;
            return false;
        }
        try
        {
            data = JsonUtility.FromJson<T>(json);
            report = label + " JSON shape is valid.";
            return data != null;
        }
        catch (Exception exception)
        {
            report = label + " JSON could not be parsed: " + exception.Message;
            return false;
        }
    }

    public static bool TryCompile(RougeAutoplayCommanderConfigData data,
        RougeAutoplayCommanderLocaleData locale,
        out RougeAutoplayCommanderDefinition definition, out string report)
    {
        definition = null;
        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();

        if (data == null || locale == null)
        {
            report = "Commander core and locale documents are required.";
            return false;
        }
        if (data.schemaVersion != SchemaVersion)
            errors.Add($"schemaVersion must be {SchemaVersion}.");
        if (!string.Equals(data.protocol, Protocol, StringComparison.Ordinal))
            errors.Add("protocol must be \"" + Protocol + "\".");
        if (!IsSafeIdentifier(data.commanderId))
            errors.Add("commanderId must use 1-48 lowercase letters, digits, '-' or '_'.");
        if (!IsSafeLocaleIdentifier(data.defaultLocale))
            errors.Add("defaultLocale must be a safe locale identifier such as zh-CN.");
        if (data.visuals == null || data.talent == null)
            errors.Add("visuals and talent are required.");
        if (data.personality == null || data.personality.concerns == null ||
            data.personality.biases == null)
            errors.Add("personality, personality.concerns and personality.biases are required.");
        if (data.strategy == null || data.strategy.skills == null ||
            data.strategy.thresholds == null || data.strategy.modePriorities == null)
            errors.Add("strategy and all nested strategy sections are required.");
        if (data.dialogue == null || data.dialogue.thresholds == null ||
            data.dialogue.triggers == null || data.dialogue.emotions == null)
            errors.Add("dialogue, dialogue.thresholds, dialogue.triggers and dialogue.emotions are required.");
        if (locale.schemaVersion != LocaleSchemaVersion)
            errors.Add($"locale.schemaVersion must be {LocaleSchemaVersion}.");
        if (!string.Equals(locale.protocol, LocaleProtocol,
                StringComparison.Ordinal))
            errors.Add("locale.protocol must be \"" + LocaleProtocol + "\".");
        if (!string.Equals(data.commanderId, locale.commanderId,
                StringComparison.Ordinal))
            errors.Add("commander core and locale commanderId values must match exactly.");
        if (!IsSafeLocaleIdentifier(locale.locale))
            errors.Add("locale.locale must be a safe locale identifier such as zh-CN.");
        if (locale.identity == null || locale.talent == null ||
            locale.personality == null || locale.strategy == null ||
            locale.strategy.modeLabels == null || locale.dialogue == null ||
            locale.outcomes == null || locale.outcomes.defeat == null)
            errors.Add("locale identity, talent, personality, strategy, dialogue and outcomes are required.");
        if (errors.Count > 0)
        {
            report = BuildReport(errors, warnings);
            return false;
        }

        NormalizeCoreNumbers(data, errors, warnings);

        string portraitFolder = data.visuals.portraitResourceFolder?.Trim();
        if (!string.IsNullOrEmpty(portraitFolder) &&
            !string.Equals(portraitFolder,
                "Commanders/" + data.commanderId + "/Portraits",
                StringComparison.Ordinal))
            errors.Add("visuals.portraitResourceFolder must be empty or exactly " +
                       "Commanders/<commanderId>/Portraits.");
        ValidateRange(errors, "personality.concerns.crowd",
            data.personality.concerns.crowd, 0.75f, 1.35f);
        ValidateRange(errors, "personality.concerns.elite",
            data.personality.concerns.elite, 0.75f, 1.35f);
        ValidateRange(errors, "personality.concerns.boss",
            data.personality.concerns.boss, 0.75f, 1.35f);
        ValidateRange(errors, "personality.concerns.urgent",
            data.personality.concerns.urgent, 0.75f, 1.35f);
        ValidateBiases(data.personality.biases, errors);
        ValidateStrategy(data.strategy, errors);
        ValidateDialogue(data.dialogue, errors);
        ValidateLocale(locale, errors, warnings);

        RougeTowerType[] buildOrder = CompileBuildOrder(data.strategy.buildOrder,
            errors);
        Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig> dialogueRules =
            CompileDialogueRules(data.dialogue.sets, errors);
        Dictionary<string, RougeAutoplayCommanderDialogueSetConfig> localizedDialogue =
            CompileLocalizedDialogueSets(locale.dialogue.sets, errors, warnings);

        if (errors.Count > 0)
        {
            report = BuildReport(errors, warnings);
            return false;
        }

        string resolvedPortraitFolder = string.IsNullOrEmpty(portraitFolder)
            ? DefaultPortraitResourceFolder
            : portraitFolder;
        string portraitPath = resolvedPortraitFolder + "/base_calm";
        if (Resources.Load<Texture2D>(portraitPath) == null)
        {
            if (!string.Equals(portraitPath, DefaultPortraitResourcePath,
                    StringComparison.Ordinal))
                warnings.Add("Configured portrait base is missing; using the " +
                             "built-in Lan portrait set.");
            portraitPath = DefaultPortraitResourcePath;
        }
        if (Resources.Load<Texture2D>(portraitPath) == null)
            errors.Add("Built-in fallback portrait is missing at Resources/" +
                       DefaultPortraitResourcePath + ".");
        if (errors.Count > 0)
        {
            report = BuildReport(errors, warnings);
            return false;
        }

        definition = new RougeAutoplayCommanderDefinition(data, locale,
            buildOrder, dialogueRules, localizedDialogue,
            resolvedPortraitFolder, portraitPath);
        report = BuildReport(errors, warnings);
        return true;
    }

    private static string BuildCoreResourcePath(string commanderName)
    {
        return "Commanders/" + commanderName + "/commander";
    }

    private static string BuildCoreAssetPath(string commanderName)
    {
        return "Assets/Rouge/Resources/Commanders/" + commanderName +
               "/commander.json";
    }

    private static string BuildLocaleResourcePath(string commanderName,
        string locale)
    {
        return "Commanders/" + commanderName + "/Locales/" + locale;
    }

    private static string BuildLocaleAssetPath(string commanderName,
        string locale)
    {
        return "Assets/Rouge/Resources/Commanders/" + commanderName +
               "/Locales/" + locale + ".json";
    }

    private static string NormalizeCommanderFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultCommanderName;
        string normalized = value.Trim();
        if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(0, normalized.Length - 5);
        normalized = normalized.ToLowerInvariant();
        return IsSafeIdentifier(normalized) ? normalized : string.Empty;
    }

    private const float StrictOrderingEpsilon = 0.0001f;
    private const float StrictMinuteStep = 0.01f;

    private static void NormalizeCoreNumbers(
        RougeAutoplayCommanderConfigData data, List<string> errors,
        List<string> warnings)
    {
        if (!ForceAuthoritativeFloat(ref data.talent.costMultiplier, 1f,
                "talent.costMultiplier", warnings))
            errors.Add("talent.costMultiplier must be finite.");

        RougeAutoplayCommanderConcernConfig concerns = data.personality.concerns;
        ClampFiniteFloat(ref concerns.crowd, 0.75f, 1.35f,
            "personality.concerns.crowd", warnings);
        ClampFiniteFloat(ref concerns.elite, 0.75f, 1.35f,
            "personality.concerns.elite", warnings);
        ClampFiniteFloat(ref concerns.boss, 0.75f, 1.35f,
            "personality.concerns.boss", warnings);
        ClampFiniteFloat(ref concerns.urgent, 0.75f, 1.35f,
            "personality.concerns.urgent", warnings);

        NormalizeBiasNumbers(data.personality.biases, warnings);
        NormalizeStrategyNumbers(data.strategy, warnings);
        NormalizeDialogueNumbers(data.dialogue, warnings);
    }

    private static void NormalizeBiasNumbers(
        RougeAutoplayCommanderBiasConfig biases, List<string> warnings)
    {
        ClampFiniteFloat(ref biases.save, 0.75f, 1.25f,
            "personality.biases.save", warnings);
        ClampFiniteFloat(ref biases.build, 0.75f, 1.25f,
            "personality.biases.build", warnings);
        ClampFiniteFloat(ref biases.controlTower, 0.75f, 1.25f,
            "personality.biases.controlTower", warnings);
        ClampFiniteFloat(ref biases.focusedTower, 0.75f, 1.25f,
            "personality.biases.focusedTower", warnings);
        ClampFiniteFloat(ref biases.areaTower, 0.75f, 1.25f,
            "personality.biases.areaTower", warnings);
        ClampFiniteFloat(ref biases.defense, 0.75f, 1.25f,
            "personality.biases.defense", warnings);
        ClampFiniteFloat(ref biases.specialTile, 0.75f, 1.25f,
            "personality.biases.specialTile", warnings);
        ClampFiniteFloat(ref biases.upgrade, 0.75f, 1.25f,
            "personality.biases.upgrade", warnings);
        ClampFiniteFloat(ref biases.redeploy, 0.75f, 1.25f,
            "personality.biases.redeploy", warnings);
    }

    private static void NormalizeStrategyNumbers(
        RougeAutoplayCommanderStrategyConfig strategy, List<string> warnings)
    {
        ClampInt(ref strategy.openingTowerCount, 1, 8,
            "strategy.openingTowerCount", warnings);
        ClampFiniteFloat(ref strategy.expansionIntervalSeconds, 5f, 180f,
            "strategy.expansionIntervalSeconds", warnings);
        ForceAuthoritativeFloat(ref strategy.capitalActionIntervalSeconds, 0.65f,
            "strategy.capitalActionIntervalSeconds", warnings);
        ForceAuthoritativeFloat(ref strategy.emergencyActionIntervalSeconds, 0.24f,
            "strategy.emergencyActionIntervalSeconds", warnings);
        ClampFiniteFloat(ref strategy.strategyHoldSeconds, 0f, 30f,
            "strategy.strategyHoldSeconds", warnings);
        ClampFiniteFloat(ref strategy.waveForecastSeconds, 1f, 120f,
            "strategy.waveForecastSeconds", warnings);
        ClampFiniteFloat(ref strategy.bossPreparationLeadSeconds, 5f, 600f,
            "strategy.bossPreparationLeadSeconds", warnings);
        ClampFiniteFloat(ref strategy.saleCooldownSeconds, 0f, 300f,
            "strategy.saleCooldownSeconds", warnings);
        ClampFiniteFloat(ref strategy.minimumTowerAgeBeforeSaleSeconds, 0f, 300f,
            "strategy.minimumTowerAgeBeforeSaleSeconds", warnings);
        bool personalityBudgetFinite = ClampFiniteFloat(
            ref strategy.personalityRegretBudget, 0f, 0.15f,
            "strategy.personalityRegretBudget", warnings);
        if (personalityBudgetFinite)
            ClampFiniteFloat(ref strategy.bossRegretBudget, 0f,
                strategy.personalityRegretBudget, "strategy.bossRegretBudget",
                warnings);
        ClampFiniteFloat(ref strategy.maximumPreferenceShift, 0f, 0.08f,
            "strategy.maximumPreferenceShift", warnings);

        ClampFiniteFloat(ref strategy.skills.mapReading, 0f, 1f,
            "strategy.skills.mapReading", warnings);
        ClampFiniteFloat(ref strategy.skills.threatReading, 0f, 1f,
            "strategy.skills.threatReading", warnings);
        ClampFiniteFloat(ref strategy.skills.crisisResponse, 0f, 1f,
            "strategy.skills.crisisResponse", warnings);
        ClampFiniteFloat(ref strategy.skills.adaptation, 0f, 1f,
            "strategy.skills.adaptation", warnings);

        NormalizeThresholdNumbers(strategy.thresholds, warnings);
        NormalizeModePriorityNumbers(strategy.modePriorities, warnings);
    }

    private static void NormalizeThresholdNumbers(
        RougeAutoplayCommanderThresholdConfig value, List<string> warnings)
    {
        ClampFiniteFloat(ref value.emergencyMainTowerHealthRatio, 0f, 1f,
            "strategy.thresholds.emergencyMainTowerHealthRatio", warnings);
        ClampFiniteFloat(ref value.emergencyUrgentPressureMinimum, 0f, 100f,
            "strategy.thresholds.emergencyUrgentPressureMinimum", warnings);
        ClampFiniteFloat(ref value.emergencyUrgentPressureFraction, 0f, 1f,
            "strategy.thresholds.emergencyUrgentPressureFraction", warnings);
        ClampFiniteFloat(ref value.emergencyImminentPressure, 0f, 200f,
            "strategy.thresholds.emergencyImminentPressure", warnings);
        ClampFiniteFloat(ref value.prepareBossProgress, 0f, 1f,
            "strategy.thresholds.prepareBossProgress", warnings);
        ClampInt(ref value.economyMaximumActiveEnemies, 0, 64,
            "strategy.thresholds.economyMaximumActiveEnemies", warnings);
        ClampFiniteFloat(ref value.economyMaximumIncomingPressure, 0f, 200f,
            "strategy.thresholds.economyMaximumIncomingPressure", warnings);
        ClampFiniteFloat(ref value.economyMinimumNextWaveSeconds, 0f, 120f,
            "strategy.thresholds.economyMinimumNextWaveSeconds", warnings);
        ClampFiniteFloat(ref value.economyMinimumMainTowerHealthRatio, 0f, 1f,
            "strategy.thresholds.economyMinimumMainTowerHealthRatio", warnings);

        bool highUrgentFinite = ClampFiniteFloat(ref value.highUrgentPressure,
            0f, 100f, "strategy.thresholds.highUrgentPressure", warnings);
        ClampFiniteFloat(ref value.highPeakPressure, 0f, 200f,
            "strategy.thresholds.highPeakPressure", warnings);
        if (highUrgentFinite)
            ClampFiniteFloat(ref value.mediumUrgentPressure, 0f,
                value.highUrgentPressure,
                "strategy.thresholds.mediumUrgentPressure", warnings);
        ClampInt(ref value.mediumActiveEnemies, 1, 128,
            "strategy.thresholds.mediumActiveEnemies", warnings);
        ClampFiniteFloat(ref value.mediumIncomingPressure, 0f, 200f,
            "strategy.thresholds.mediumIncomingPressure", warnings);
        bool highBossFinite = ClampFiniteFloat(ref value.highBossPreparation,
            0f, 1f, "strategy.thresholds.highBossPreparation", warnings);
        if (highBossFinite)
            ClampFiniteFloat(ref value.mediumBossPreparation, 0f,
                value.highBossPreparation,
                "strategy.thresholds.mediumBossPreparation", warnings);
        bool criticalCrisisFinite = ClampFiniteFloat(
            ref value.criticalCrisisHealthRatio, 0f, 1f,
            "strategy.thresholds.criticalCrisisHealthRatio", warnings);
        if (criticalCrisisFinite)
            ClampFiniteFloat(ref value.lowCrisisHealthRatio,
                value.criticalCrisisHealthRatio, 1f,
                "strategy.thresholds.lowCrisisHealthRatio", warnings);

        ClampInt(ref value.redeployMinimumExtraTowers, 0, 8,
            "strategy.thresholds.redeployMinimumExtraTowers", warnings);
        ClampFiniteFloat(ref value.redeployMinimumHealthRatio, 0f, 1f,
            "strategy.thresholds.redeployMinimumHealthRatio", warnings);
        ClampFiniteFloat(ref value.redeployMaximumUrgentPressure, 0f, 100f,
            "strategy.thresholds.redeployMaximumUrgentPressure", warnings);
        ClampInt(ref value.redeployMaximumActiveEnemies, 0, 128,
            "strategy.thresholds.redeployMaximumActiveEnemies", warnings);
        ClampFiniteFloat(ref value.redeployMaximumBossPreparation, 0f, 1f,
            "strategy.thresholds.redeployMaximumBossPreparation", warnings);
        ClampFiniteFloat(ref value.immediateDefenseHealthRatio, 0f, 1f,
            "strategy.thresholds.immediateDefenseHealthRatio", warnings);
        ClampFiniteFloat(ref value.immediateDefenseUrgentPressure, 0f, 100f,
            "strategy.thresholds.immediateDefenseUrgentPressure", warnings);
        ClampInt(ref value.immediateDefenseActiveEnemies, 0, 128,
            "strategy.thresholds.immediateDefenseActiveEnemies", warnings);
        ClampFiniteFloat(ref value.valuableSpecialTileScore, 0f, 500f,
            "strategy.thresholds.valuableSpecialTileScore", warnings);
        ClampFiniteFloat(ref value.coverageUrgentPressure, 0f, 100f,
            "strategy.thresholds.coverageUrgentPressure", warnings);
        ClampInt(ref value.coverageActiveEnemies, 0, 128,
            "strategy.thresholds.coverageActiveEnemies", warnings);
        ClampFiniteFloat(ref value.coverageHealthRatio, 0f, 1f,
            "strategy.thresholds.coverageHealthRatio", warnings);
    }

    private static void NormalizeModePriorityNumbers(
        RougeAutoplayCommanderModePriorityConfig priorities,
        List<string> warnings)
    {
        int[] original =
        {
            priorities.opening, priorities.economy, priorities.hold,
            priorities.prepareBoss, priorities.bossFight, priorities.emergency
        };
        int[] normalized = new int[original.Length];
        bool[] used = new bool[7];

        // Preserve every already-valid unique assignment first. Remaining invalid or
        // duplicate slots receive the nearest free priority in stable field order.
        for (int i = 0; i < original.Length; i++)
        {
            int value = original[i];
            if (value < 1 || value > 6 || used[value]) continue;
            normalized[i] = value;
            used[value] = true;
        }
        for (int i = 0; i < original.Length; i++)
        {
            if (normalized[i] != 0) continue;
            int desired = Mathf.Clamp(original[i], 1, 6);
            int selected = FindNearestUnusedPriority(desired, used);
            normalized[i] = selected;
            used[selected] = true;
        }

        ApplyNormalizedInt(ref priorities.opening, normalized[0],
            "strategy.modePriorities.opening", warnings);
        ApplyNormalizedInt(ref priorities.economy, normalized[1],
            "strategy.modePriorities.economy", warnings);
        ApplyNormalizedInt(ref priorities.hold, normalized[2],
            "strategy.modePriorities.hold", warnings);
        ApplyNormalizedInt(ref priorities.prepareBoss, normalized[3],
            "strategy.modePriorities.prepareBoss", warnings);
        ApplyNormalizedInt(ref priorities.bossFight, normalized[4],
            "strategy.modePriorities.bossFight", warnings);
        ApplyNormalizedInt(ref priorities.emergency, normalized[5],
            "strategy.modePriorities.emergency", warnings);
    }

    private static int FindNearestUnusedPriority(int desired, bool[] used)
    {
        for (int distance = 0; distance <= 5; distance++)
        {
            int lower = desired - distance;
            if (lower >= 1 && !used[lower]) return lower;
            int upper = desired + distance;
            if (upper <= 6 && !used[upper]) return upper;
        }
        return 1;
    }

    private static void NormalizeDialogueNumbers(
        RougeAutoplayCommanderDialogueConfig dialogue, List<string> warnings)
    {
        ClampInt(ref dialogue.startingAffinity, 0, 100,
            "dialogue.startingAffinity", warnings);
        ClampInt(ref dialogue.familiarThreshold, 1, 99,
            "dialogue.familiarThreshold", warnings);
        ClampInt(ref dialogue.closeThreshold, dialogue.familiarThreshold + 1, 100,
            "dialogue.closeThreshold", warnings);
        bool intervalMinimumFinite = ClampFiniteFloat(
            ref dialogue.intervalMinimumSeconds, 2f, 120f,
            "dialogue.intervalMinimumSeconds", warnings);
        if (intervalMinimumFinite)
            ClampFiniteFloat(ref dialogue.intervalMaximumSeconds,
                dialogue.intervalMinimumSeconds, 180f,
                "dialogue.intervalMaximumSeconds", warnings);
        ClampFiniteFloat(ref dialogue.preemptionCooldownSeconds, 0f, 60f,
            "dialogue.preemptionCooldownSeconds", warnings);
        ClampInt(ref dialogue.recentHistorySize, 0, 32,
            "dialogue.recentHistorySize", warnings);

        RougeAutoplayCommanderDialogueThresholdConfig thresholds =
            dialogue.thresholds;
        bool baseCriticalFinite = ClampFiniteFloat(
            ref thresholds.baseCriticalHealthRatio, 0f, 1f,
            "dialogue.thresholds.baseCriticalHealthRatio", warnings);
        if (baseCriticalFinite)
            ClampFiniteFloat(ref thresholds.baseLowHealthRatio,
                thresholds.baseCriticalHealthRatio, 1f,
                "dialogue.thresholds.baseLowHealthRatio", warnings);
        ClampFiniteFloat(ref thresholds.urgentPressureMinimum, 0f, 100f,
            "dialogue.thresholds.urgentPressureMinimum", warnings);
        ClampFiniteFloat(ref thresholds.urgentPressureFraction, 0f, 1f,
            "dialogue.thresholds.urgentPressureFraction", warnings);
        ClampFiniteFloat(ref thresholds.hardConcernMinimum, 0f, 100f,
            "dialogue.thresholds.hardConcernMinimum", warnings);
        ClampFiniteFloat(ref thresholds.hardVersusCrowdFactor, 0f, 2f,
            "dialogue.thresholds.hardVersusCrowdFactor", warnings);
        ClampInt(ref thresholds.crowdEnemyCount, 0, 128,
            "dialogue.thresholds.crowdEnemyCount", warnings);
        ClampFiniteFloat(ref thresholds.crowdConcernMinimum, 0f, 200f,
            "dialogue.thresholds.crowdConcernMinimum", warnings);
        ClampFiniteFloat(ref thresholds.flowObservationWindowSeconds, 2f, 30f,
            "dialogue.thresholds.flowObservationWindowSeconds", warnings);
        ClampFiniteFloat(ref thresholds.lowKillSpawnRatio, 0.1f, 1.2f,
            "dialogue.thresholds.lowKillSpawnRatio", warnings);
        ClampFiniteFloat(ref thresholds.nearBaseDistanceCells, 0.5f, 7.5f,
            "dialogue.thresholds.nearBaseDistanceCells", warnings);
        ClampFiniteFloat(ref thresholds.nearBaseSustainSeconds, 0.5f, 10f,
            "dialogue.thresholds.nearBaseSustainSeconds", warnings);
        ClampFiniteFloat(ref thresholds.economyObservationWindowSeconds, 10f,
            120f, "dialogue.thresholds.economyObservationWindowSeconds", warnings);
        ClampFiniteFloat(ref thresholds.lowIncomeSpendRatio, 0.1f, 1.2f,
            "dialogue.thresholds.lowIncomeSpendRatio", warnings);

        NormalizeDialogueTriggerNumbers(dialogue.triggers, warnings);
        NormalizeEmotionNumbers(dialogue.emotions, warnings);
        if (dialogue.sets == null) return;
        for (int i = 0; i < dialogue.sets.Length; i++)
        {
            RougeAutoplayCommanderDialogueRuleConfig rule = dialogue.sets[i];
            if (rule == null) continue;
            ClampInt(ref rule.priority, 1, 20,
                $"dialogue.sets[{i}].priority", warnings);
        }
    }

    private static void NormalizeDialogueTriggerNumbers(
        RougeAutoplayCommanderDialogueTriggerConfig triggers,
        List<string> warnings)
    {
        NormalizeLateTakeoverMinutes(triggers.lateFirstTakeoverMinutes, warnings);
        ClampFiniteFloat(ref triggers.mainTowerBurstWindowSeconds, 0.5f, 10f,
            "dialogue.triggers.mainTowerBurstWindowSeconds", warnings);
        ClampFiniteFloat(ref triggers.mainTowerBurstHealthLossPercent, 1f, 50f,
            "dialogue.triggers.mainTowerBurstHealthLossPercent", warnings);
        ClampFiniteFloat(ref triggers.mainTowerHitDialogueChance, 0f, 1f,
            "dialogue.triggers.mainTowerHitDialogueChance", warnings);
        ClampFiniteFloat(ref triggers.mainTowerHitDialogueCooldownSeconds, 0f,
            120f, "dialogue.triggers.mainTowerHitDialogueCooldownSeconds",
            warnings);
        ClampFiniteFloat(ref triggers.mainTowerBurstDialogueCooldownSeconds, 0f,
            120f, "dialogue.triggers.mainTowerBurstDialogueCooldownSeconds",
            warnings);
        ClampFiniteFloat(ref triggers.towerBuildDialogueChance, 0f, 1f,
            "dialogue.triggers.towerBuildDialogueChance", warnings);
        ClampFiniteFloat(ref triggers.towerBuildDialogueCooldownSeconds, 5f, 180f,
            "dialogue.triggers.towerBuildDialogueCooldownSeconds", warnings);
        ClampFiniteFloat(ref triggers.towerUpgradeDialogueChance, 0f, 1f,
            "dialogue.triggers.towerUpgradeDialogueChance", warnings);
        ClampFiniteFloat(ref triggers.towerUpgradeDialogueCooldownSeconds, 5f,
            180f, "dialogue.triggers.towerUpgradeDialogueCooldownSeconds",
            warnings);
        ClampFiniteFloat(ref triggers.pressureReliefMinimumHighSeconds, 1f, 60f,
            "dialogue.triggers.pressureReliefMinimumHighSeconds", warnings);
        ClampFiniteFloat(ref triggers.pressureReliefConfirmLowSeconds, 0.5f, 10f,
            "dialogue.triggers.pressureReliefConfirmLowSeconds", warnings);
        ClampFiniteFloat(ref triggers.pressureReliefDialogueCooldownSeconds, 5f,
            180f, "dialogue.triggers.pressureReliefDialogueCooldownSeconds",
            warnings);
        bool warningRatioFinite = ClampFiniteFloat(
            ref triggers.bossHealthWarningRatio, 0.05f, 0.95f,
            "dialogue.triggers.bossHealthWarningRatio", warnings);
        if (warningRatioFinite)
            ClampFiniteFloat(ref triggers.bossHealthCriticalRatio, 0.01f,
                triggers.bossHealthWarningRatio - StrictOrderingEpsilon,
                "dialogue.triggers.bossHealthCriticalRatio", warnings);
        ClampFiniteFloat(ref triggers.portraitClickDialogueCooldownSeconds, 0.1f,
            30f, "dialogue.triggers.portraitClickDialogueCooldownSeconds",
            warnings);
        ClampInt(ref triggers.portraitRapidClickCount, 3, 12,
            "dialogue.triggers.portraitRapidClickCount", warnings);
        ClampFiniteFloat(ref triggers.portraitRapidClickWindowSeconds, 0.5f, 5f,
            "dialogue.triggers.portraitRapidClickWindowSeconds", warnings);
        ClampFiniteFloat(ref triggers.portraitRapidClickDialogueCooldownSeconds,
            0.5f, 60f,
            "dialogue.triggers.portraitRapidClickDialogueCooldownSeconds",
            warnings);
    }

    private static void NormalizeLateTakeoverMinutes(float[] minutes,
        List<string> warnings)
    {
        if (minutes == null || minutes.Length != 4) return;
        bool allFinite = true;
        for (int i = 0; i < minutes.Length; i++)
            allFinite &= IsFinite(minutes[i]);
        if (!allFinite)
        {
            for (int i = 0; i < minutes.Length; i++)
                ClampFiniteFloat(ref minutes[i], 1f, 1440f,
                    $"dialogue.triggers.lateFirstTakeoverMinutes[{i}]", warnings);
            return;
        }

        for (int i = 0; i < minutes.Length; i++)
        {
            float minimum = i == 0 ? 1f : minutes[i - 1] + StrictMinuteStep;
            float maximum = 1440f -
                            StrictMinuteStep * (minutes.Length - i - 1);
            ClampFiniteFloat(ref minutes[i], minimum, maximum,
                $"dialogue.triggers.lateFirstTakeoverMinutes[{i}]", warnings);
        }
    }

    private static void NormalizeEmotionNumbers(
        RougeAutoplayCommanderEmotionConfig emotions, List<string> warnings)
    {
        bool focusedFinite = ClampFiniteFloat(
            ref emotions.focusedTensionThreshold, 0.05f, 0.75f,
            "dialogue.emotions.focusedTensionThreshold", warnings);
        bool tenseFinite = focusedFinite && ClampFiniteFloat(
            ref emotions.tenseTensionThreshold,
            emotions.focusedTensionThreshold + StrictOrderingEpsilon, 0.9f,
            "dialogue.emotions.tenseTensionThreshold", warnings);
        if (tenseFinite)
            ClampFiniteFloat(ref emotions.criticalTensionThreshold,
                emotions.tenseTensionThreshold + StrictOrderingEpsilon, 0.98f,
                "dialogue.emotions.criticalTensionThreshold", warnings);
        ClampFiniteFloat(ref emotions.transitionConfirmSeconds, 0.5f, 10f,
            "dialogue.emotions.transitionConfirmSeconds", warnings);
        ClampFiniteFloat(ref emotions.transitionDialogueCooldownSeconds, 3f, 60f,
            "dialogue.emotions.transitionDialogueCooldownSeconds", warnings);

        bool calmFinite = ClampFiniteFloat(ref emotions.calmIntervalMultiplier,
            0.8f, 1.2f, "dialogue.emotions.calmIntervalMultiplier", warnings);
        bool focusedIntervalFinite = ClampFiniteFloat(
            ref emotions.focusedIntervalMultiplier, 0.8f,
            calmFinite ? emotions.calmIntervalMultiplier : 1.2f,
            "dialogue.emotions.focusedIntervalMultiplier", warnings);
        bool tenseIntervalFinite = ClampFiniteFloat(
            ref emotions.tenseIntervalMultiplier, 0.8f,
            focusedIntervalFinite ? emotions.focusedIntervalMultiplier : 1.2f,
            "dialogue.emotions.tenseIntervalMultiplier", warnings);
        ClampFiniteFloat(ref emotions.criticalIntervalMultiplier, 0.8f,
            tenseIntervalFinite ? emotions.tenseIntervalMultiplier : 1.2f,
            "dialogue.emotions.criticalIntervalMultiplier", warnings);
    }

    private static bool ClampFiniteFloat(ref float value, float minimum,
        float maximum, string path, List<string> warnings)
    {
        if (!IsFinite(value)) return false;
        float original = value;
        value = Mathf.Clamp(value, minimum, maximum);
        if (value != original)
            warnings.Add(path + " clamped from " + FormatFloat(original) +
                         " to " + FormatFloat(value) + ".");
        return true;
    }

    private static bool ForceAuthoritativeFloat(ref float value,
        float authoritativeValue, string path, List<string> warnings)
    {
        if (!IsFinite(value)) return false;
        float original = value;
        value = authoritativeValue;
        if (value != original)
            warnings.Add(path + " forced from " + FormatFloat(original) +
                         " to engine-authoritative " + FormatFloat(value) + ".");
        return true;
    }

    private static void ClampInt(ref int value, int minimum, int maximum,
        string path, List<string> warnings)
    {
        int original = value;
        ApplyNormalizedInt(ref value, Mathf.Clamp(value, minimum, maximum), path,
            warnings, original);
    }

    private static void ApplyNormalizedInt(ref int value, int normalized,
        string path, List<string> warnings)
    {
        ApplyNormalizedInt(ref value, normalized, path, warnings, value);
    }

    private static void ApplyNormalizedInt(ref int value, int normalized,
        string path, List<string> warnings, int original)
    {
        value = normalized;
        if (value != original)
            warnings.Add(path + " clamped from " + original + " to " + value +
                         ".");
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("R",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateBiases(RougeAutoplayCommanderBiasConfig biases,
        List<string> errors)
    {
        ValidateRange(errors, "personality.biases.save", biases.save, 0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.build", biases.build, 0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.controlTower", biases.controlTower,
            0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.focusedTower", biases.focusedTower,
            0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.areaTower", biases.areaTower,
            0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.defense", biases.defense, 0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.specialTile", biases.specialTile,
            0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.upgrade", biases.upgrade, 0.75f, 1.25f);
        ValidateRange(errors, "personality.biases.redeploy", biases.redeploy, 0.75f, 1.25f);
    }

    private static void ValidateStrategy(RougeAutoplayCommanderStrategyConfig strategy,
        List<string> errors)
    {
        if (strategy.buildOrder == null || strategy.buildOrder.Length == 0)
            errors.Add("strategy.buildOrder must contain at least one standard tower ID.");
        if (strategy.openingTowerCount < 1 || strategy.openingTowerCount > 8)
            errors.Add("strategy.openingTowerCount must be within [1, 8].");
        ValidateRange(errors, "strategy.expansionIntervalSeconds",
            strategy.expansionIntervalSeconds, 5f, 180f);
        ValidateRange(errors, "strategy.capitalActionIntervalSeconds",
            strategy.capitalActionIntervalSeconds, 0.1f, 5f);
        ValidateRange(errors, "strategy.emergencyActionIntervalSeconds",
            strategy.emergencyActionIntervalSeconds, 0.1f,
            strategy.capitalActionIntervalSeconds);
        ValidateRange(errors, "strategy.strategyHoldSeconds",
            strategy.strategyHoldSeconds, 0f, 30f);
        ValidateRange(errors, "strategy.waveForecastSeconds",
            strategy.waveForecastSeconds, 1f, 120f);
        ValidateRange(errors, "strategy.bossPreparationLeadSeconds",
            strategy.bossPreparationLeadSeconds, 5f, 600f);
        ValidateRange(errors, "strategy.saleCooldownSeconds",
            strategy.saleCooldownSeconds, 0f, 300f);
        ValidateRange(errors, "strategy.minimumTowerAgeBeforeSaleSeconds",
            strategy.minimumTowerAgeBeforeSaleSeconds, 0f, 300f);
        ValidateRange(errors, "strategy.personalityRegretBudget",
            strategy.personalityRegretBudget, 0f, 0.15f);
        ValidateRange(errors, "strategy.bossRegretBudget",
            strategy.bossRegretBudget, 0f, strategy.personalityRegretBudget);
        ValidateRange(errors, "strategy.maximumPreferenceShift",
            strategy.maximumPreferenceShift, 0f, 0.08f);
        ValidateSkill(errors, "strategy.skills.mapReading", strategy.skills.mapReading);
        ValidateSkill(errors, "strategy.skills.threatReading", strategy.skills.threatReading);
        ValidateSkill(errors, "strategy.skills.crisisResponse", strategy.skills.crisisResponse);
        ValidateSkill(errors, "strategy.skills.adaptation", strategy.skills.adaptation);
        ValidateThresholds(strategy.thresholds, errors);
        ValidateModePriorities(strategy.modePriorities, errors);
    }

    private static void ValidateThresholds(
        RougeAutoplayCommanderThresholdConfig value, List<string> errors)
    {
        ValidateRatio(errors, "strategy.thresholds.emergencyMainTowerHealthRatio",
            value.emergencyMainTowerHealthRatio);
        ValidateRange(errors, "strategy.thresholds.emergencyUrgentPressureMinimum",
            value.emergencyUrgentPressureMinimum, 0f, 100f);
        ValidateRatio(errors, "strategy.thresholds.emergencyUrgentPressureFraction",
            value.emergencyUrgentPressureFraction);
        ValidateRange(errors, "strategy.thresholds.emergencyImminentPressure",
            value.emergencyImminentPressure, 0f, 200f);
        ValidateRatio(errors, "strategy.thresholds.prepareBossProgress",
            value.prepareBossProgress);
        if (value.economyMaximumActiveEnemies < 0 ||
            value.economyMaximumActiveEnemies > 64)
            errors.Add("strategy.thresholds.economyMaximumActiveEnemies must be within [0, 64].");
        ValidateRange(errors, "strategy.thresholds.economyMaximumIncomingPressure",
            value.economyMaximumIncomingPressure, 0f, 200f);
        ValidateRange(errors, "strategy.thresholds.economyMinimumNextWaveSeconds",
            value.economyMinimumNextWaveSeconds, 0f, 120f);
        ValidateRatio(errors, "strategy.thresholds.economyMinimumMainTowerHealthRatio",
            value.economyMinimumMainTowerHealthRatio);
        ValidateRange(errors, "strategy.thresholds.highUrgentPressure",
            value.highUrgentPressure, 0f, 100f);
        ValidateRange(errors, "strategy.thresholds.highPeakPressure",
            value.highPeakPressure, 0f, 200f);
        ValidateRange(errors, "strategy.thresholds.mediumUrgentPressure",
            value.mediumUrgentPressure, 0f, value.highUrgentPressure);
        if (value.mediumActiveEnemies < 1 || value.mediumActiveEnemies > 128)
            errors.Add("strategy.thresholds.mediumActiveEnemies must be within [1, 128].");
        ValidateRange(errors, "strategy.thresholds.mediumIncomingPressure",
            value.mediumIncomingPressure, 0f, 200f);
        ValidateRatio(errors, "strategy.thresholds.highBossPreparation",
            value.highBossPreparation);
        ValidateRange(errors, "strategy.thresholds.mediumBossPreparation",
            value.mediumBossPreparation, 0f, value.highBossPreparation);
        ValidateRatio(errors, "strategy.thresholds.criticalCrisisHealthRatio",
            value.criticalCrisisHealthRatio);
        ValidateRange(errors, "strategy.thresholds.lowCrisisHealthRatio",
            value.lowCrisisHealthRatio, value.criticalCrisisHealthRatio, 1f);
        if (value.redeployMinimumExtraTowers < 0 ||
            value.redeployMinimumExtraTowers > 8)
            errors.Add("strategy.thresholds.redeployMinimumExtraTowers must be within [0, 8].");
        ValidateRatio(errors, "strategy.thresholds.redeployMinimumHealthRatio",
            value.redeployMinimumHealthRatio);
        ValidateRange(errors, "strategy.thresholds.redeployMaximumUrgentPressure",
            value.redeployMaximumUrgentPressure, 0f, 100f);
        if (value.redeployMaximumActiveEnemies < 0 ||
            value.redeployMaximumActiveEnemies > 128)
            errors.Add("strategy.thresholds.redeployMaximumActiveEnemies must be within [0, 128].");
        ValidateRatio(errors, "strategy.thresholds.redeployMaximumBossPreparation",
            value.redeployMaximumBossPreparation);
        ValidateRatio(errors, "strategy.thresholds.immediateDefenseHealthRatio",
            value.immediateDefenseHealthRatio);
        ValidateRange(errors, "strategy.thresholds.immediateDefenseUrgentPressure",
            value.immediateDefenseUrgentPressure, 0f, 100f);
        if (value.immediateDefenseActiveEnemies < 0 ||
            value.immediateDefenseActiveEnemies > 128)
            errors.Add("strategy.thresholds.immediateDefenseActiveEnemies must be within [0, 128].");
        ValidateRange(errors, "strategy.thresholds.valuableSpecialTileScore",
            value.valuableSpecialTileScore, 0f, 500f);
        ValidateRange(errors, "strategy.thresholds.coverageUrgentPressure",
            value.coverageUrgentPressure, 0f, 100f);
        if (value.coverageActiveEnemies < 0 || value.coverageActiveEnemies > 128)
            errors.Add("strategy.thresholds.coverageActiveEnemies must be within [0, 128].");
        ValidateRatio(errors, "strategy.thresholds.coverageHealthRatio",
            value.coverageHealthRatio);
    }

    private static void ValidateModeText(RougeAutoplayCommanderModeLabelConfig labels,
        List<string> errors)
    {
        ValidateRequiredText(errors, "locale.strategy.modeLabels.opening",
            labels.opening, 32);
        ValidateRequiredText(errors, "locale.strategy.modeLabels.economy",
            labels.economy, 32);
        ValidateRequiredText(errors, "locale.strategy.modeLabels.hold", labels.hold,
            32);
        ValidateRequiredText(errors, "locale.strategy.modeLabels.prepareBoss",
            labels.prepareBoss, 32);
        ValidateRequiredText(errors, "locale.strategy.modeLabels.bossFight",
            labels.bossFight, 32);
        ValidateRequiredText(errors, "locale.strategy.modeLabels.emergency",
            labels.emergency, 32);
    }

    private static void ValidateModePriorities(
        RougeAutoplayCommanderModePriorityConfig priorities, List<string> errors)
    {
        int[] values =
        {
            priorities.opening, priorities.economy, priorities.hold,
            priorities.prepareBoss, priorities.bossFight, priorities.emergency
        };
        HashSet<int> unique = new HashSet<int>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < 1 || values[i] > 6)
                errors.Add("strategy.modePriorities values must be within [1, 6].");
            unique.Add(values[i]);
        }
        if (unique.Count != values.Length)
            errors.Add("strategy.modePriorities must contain six unique priorities.");
    }

    private static void ValidateDialogue(RougeAutoplayCommanderDialogueConfig dialogue,
        List<string> errors)
    {
        if (dialogue.startingAffinity < 0 || dialogue.startingAffinity > 100)
            errors.Add("dialogue.startingAffinity must be within [0, 100].");
        if (dialogue.familiarThreshold < 1 || dialogue.familiarThreshold > 99 ||
            dialogue.closeThreshold <= dialogue.familiarThreshold ||
            dialogue.closeThreshold > 100)
            errors.Add("dialogue affinity thresholds must satisfy 0 < familiar < close <= 100.");
        ValidateRange(errors, "dialogue.intervalMinimumSeconds",
            dialogue.intervalMinimumSeconds, 2f, 120f);
        ValidateRange(errors, "dialogue.intervalMaximumSeconds",
            dialogue.intervalMaximumSeconds, dialogue.intervalMinimumSeconds, 180f);
        ValidateRange(errors, "dialogue.preemptionCooldownSeconds",
            dialogue.preemptionCooldownSeconds, 0f, 60f);
        if (dialogue.recentHistorySize < 0 || dialogue.recentHistorySize > 32)
            errors.Add("dialogue.recentHistorySize must be within [0, 32].");
        RougeAutoplayCommanderDialogueThresholdConfig threshold = dialogue.thresholds;
        ValidateRatio(errors, "dialogue.thresholds.baseCriticalHealthRatio",
            threshold.baseCriticalHealthRatio);
        ValidateRange(errors, "dialogue.thresholds.baseLowHealthRatio",
            threshold.baseLowHealthRatio, threshold.baseCriticalHealthRatio, 1f);
        ValidateRange(errors, "dialogue.thresholds.urgentPressureMinimum",
            threshold.urgentPressureMinimum, 0f, 100f);
        ValidateRatio(errors, "dialogue.thresholds.urgentPressureFraction",
            threshold.urgentPressureFraction);
        ValidateRange(errors, "dialogue.thresholds.hardConcernMinimum",
            threshold.hardConcernMinimum, 0f, 100f);
        ValidateRange(errors, "dialogue.thresholds.hardVersusCrowdFactor",
            threshold.hardVersusCrowdFactor, 0f, 2f);
        if (threshold.crowdEnemyCount < 0 || threshold.crowdEnemyCount > 128)
            errors.Add("dialogue.thresholds.crowdEnemyCount must be within [0, 128].");
        ValidateRange(errors, "dialogue.thresholds.crowdConcernMinimum",
            threshold.crowdConcernMinimum, 0f, 200f);
        ValidateRange(errors, "dialogue.thresholds.flowObservationWindowSeconds",
            threshold.flowObservationWindowSeconds, 2f, 30f);
        ValidateRange(errors, "dialogue.thresholds.lowKillSpawnRatio",
            threshold.lowKillSpawnRatio, 0.1f, 1.2f);
        ValidateRange(errors, "dialogue.thresholds.nearBaseDistanceCells",
            threshold.nearBaseDistanceCells, 0.5f, 7.5f);
        ValidateRange(errors, "dialogue.thresholds.nearBaseSustainSeconds",
            threshold.nearBaseSustainSeconds, 0.5f, 10f);
        ValidateRange(errors,
            "dialogue.thresholds.economyObservationWindowSeconds",
            threshold.economyObservationWindowSeconds, 10f, 120f);
        ValidateRange(errors, "dialogue.thresholds.lowIncomeSpendRatio",
            threshold.lowIncomeSpendRatio, 0.1f, 1.2f);

        RougeAutoplayCommanderDialogueTriggerConfig triggers = dialogue.triggers;
        if (triggers.lateFirstTakeoverMinutes == null ||
            triggers.lateFirstTakeoverMinutes.Length != 4)
            errors.Add("dialogue.triggers.lateFirstTakeoverMinutes must contain exactly four values.");
        else
        {
            float previousMinute = 0f;
            for (int i = 0; i < triggers.lateFirstTakeoverMinutes.Length; i++)
            {
                float minute = triggers.lateFirstTakeoverMinutes[i];
                ValidateRange(errors,
                    $"dialogue.triggers.lateFirstTakeoverMinutes[{i}]", minute,
                    1f, 1440f);
                if (i > 0 && minute <= previousMinute)
                    errors.Add("dialogue.triggers.lateFirstTakeoverMinutes must be strictly increasing.");
                previousMinute = minute;
            }
        }
        ValidateRange(errors, "dialogue.triggers.mainTowerBurstWindowSeconds",
            triggers.mainTowerBurstWindowSeconds, 0.5f, 10f);
        ValidateRange(errors, "dialogue.triggers.mainTowerBurstHealthLossPercent",
            triggers.mainTowerBurstHealthLossPercent, 1f, 50f);
        ValidateRatio(errors, "dialogue.triggers.mainTowerHitDialogueChance",
            triggers.mainTowerHitDialogueChance);
        ValidateRange(errors,
            "dialogue.triggers.mainTowerHitDialogueCooldownSeconds",
            triggers.mainTowerHitDialogueCooldownSeconds, 0f, 120f);
        ValidateRange(errors,
            "dialogue.triggers.mainTowerBurstDialogueCooldownSeconds",
            triggers.mainTowerBurstDialogueCooldownSeconds, 0f, 120f);
        ValidateRatio(errors, "dialogue.triggers.towerBuildDialogueChance",
            triggers.towerBuildDialogueChance);
        ValidateRange(errors,
            "dialogue.triggers.towerBuildDialogueCooldownSeconds",
            triggers.towerBuildDialogueCooldownSeconds, 5f, 180f);
        ValidateRatio(errors, "dialogue.triggers.towerUpgradeDialogueChance",
            triggers.towerUpgradeDialogueChance);
        ValidateRange(errors,
            "dialogue.triggers.towerUpgradeDialogueCooldownSeconds",
            triggers.towerUpgradeDialogueCooldownSeconds, 5f, 180f);
        ValidateRange(errors,
            "dialogue.triggers.pressureReliefMinimumHighSeconds",
            triggers.pressureReliefMinimumHighSeconds, 1f, 60f);
        ValidateRange(errors,
            "dialogue.triggers.pressureReliefConfirmLowSeconds",
            triggers.pressureReliefConfirmLowSeconds, 0.5f, 10f);
        ValidateRange(errors,
            "dialogue.triggers.pressureReliefDialogueCooldownSeconds",
            triggers.pressureReliefDialogueCooldownSeconds, 5f, 180f);
        ValidateRange(errors, "dialogue.triggers.bossHealthWarningRatio",
            triggers.bossHealthWarningRatio, 0.05f, 0.95f);
        ValidateRange(errors, "dialogue.triggers.bossHealthCriticalRatio",
            triggers.bossHealthCriticalRatio, 0.01f,
            triggers.bossHealthWarningRatio);
        if (triggers.bossHealthCriticalRatio >=
            triggers.bossHealthWarningRatio)
            errors.Add("dialogue.triggers boss health ratios must satisfy 0 < critical < warning < 1.");
        ValidateRange(errors,
            "dialogue.triggers.portraitClickDialogueCooldownSeconds",
            triggers.portraitClickDialogueCooldownSeconds, 0.1f, 30f);
        if (triggers.portraitRapidClickCount < 3 ||
            triggers.portraitRapidClickCount > 12)
            errors.Add("dialogue.triggers.portraitRapidClickCount must be within [3, 12].");
        ValidateRange(errors,
            "dialogue.triggers.portraitRapidClickWindowSeconds",
            triggers.portraitRapidClickWindowSeconds, 0.5f, 5f);
        ValidateRange(errors,
            "dialogue.triggers.portraitRapidClickDialogueCooldownSeconds",
            triggers.portraitRapidClickDialogueCooldownSeconds, 0.5f, 60f);

        RougeAutoplayCommanderEmotionConfig emotions = dialogue.emotions;
        ValidateRange(errors, "dialogue.emotions.focusedTensionThreshold",
            emotions.focusedTensionThreshold, 0.05f, 0.75f);
        ValidateRange(errors, "dialogue.emotions.tenseTensionThreshold",
            emotions.tenseTensionThreshold, emotions.focusedTensionThreshold,
            0.9f);
        ValidateRange(errors, "dialogue.emotions.criticalTensionThreshold",
            emotions.criticalTensionThreshold, emotions.tenseTensionThreshold,
            0.98f);
        if (emotions.focusedTensionThreshold >= emotions.tenseTensionThreshold ||
            emotions.tenseTensionThreshold >= emotions.criticalTensionThreshold)
            errors.Add("dialogue.emotions tension thresholds must be strictly increasing.");
        ValidateRange(errors, "dialogue.emotions.transitionConfirmSeconds",
            emotions.transitionConfirmSeconds, 0.5f, 10f);
        ValidateRange(errors,
            "dialogue.emotions.transitionDialogueCooldownSeconds",
            emotions.transitionDialogueCooldownSeconds, 3f, 60f);
        ValidateRange(errors, "dialogue.emotions.calmIntervalMultiplier",
            emotions.calmIntervalMultiplier, 0.8f, 1.2f);
        ValidateRange(errors, "dialogue.emotions.focusedIntervalMultiplier",
            emotions.focusedIntervalMultiplier, 0.8f, 1.2f);
        ValidateRange(errors, "dialogue.emotions.tenseIntervalMultiplier",
            emotions.tenseIntervalMultiplier, 0.8f, 1.2f);
        ValidateRange(errors, "dialogue.emotions.criticalIntervalMultiplier",
            emotions.criticalIntervalMultiplier, 0.8f, 1.2f);
        if (emotions.calmIntervalMultiplier < emotions.focusedIntervalMultiplier ||
            emotions.focusedIntervalMultiplier < emotions.tenseIntervalMultiplier ||
            emotions.tenseIntervalMultiplier < emotions.criticalIntervalMultiplier)
            errors.Add("dialogue.emotions interval multipliers must be non-increasing from Calm to Critical.");

        if (dialogue.sets == null)
            errors.Add("dialogue.sets is required.");
    }

    private static void ValidateLocale(RougeAutoplayCommanderLocaleData locale,
        List<string> errors, List<string> warnings)
    {
        ValidateRequiredText(errors, "locale.identity.displayName",
            locale.identity.displayName, 48);
        ValidateRequiredText(errors, "locale.identity.role", locale.identity.role,
            96);
        ValidateRequiredText(errors, "locale.identity.personaLabel",
            locale.identity.personaLabel, 120);
        ValidateRequiredText(errors, "locale.identity.background",
            locale.identity.background, 1200);
        ValidateRequiredText(errors, "locale.identity.speakingStyle",
            locale.identity.speakingStyle, 500);
        ValidateTextArray(errors, "locale.identity.personalityTraits",
            locale.identity.personalityTraits, 1, 12, 40);
        ValidateRequiredText(errors, "locale.talent.name", locale.talent.name, 64);
        ValidateRequiredText(errors, "locale.talent.description",
            locale.talent.description, 300);
        ValidateRequiredText(errors, "locale.personality.thinkingStyle",
            locale.personality.thinkingStyle, 800);
        ValidateTextArray(errors, "locale.personality.decisionPrinciples",
            locale.personality.decisionPrinciples, 1, 16, 180);
        ValidateModeText(locale.strategy.modeLabels, errors);
        ValidateRequiredText(errors, "locale.dialogue.distantLabel",
            locale.dialogue.distantLabel, 24);
        ValidateRequiredText(errors, "locale.dialogue.familiarLabel",
            locale.dialogue.familiarLabel, 24);
        ValidateRequiredText(errors, "locale.dialogue.closeLabel",
            locale.dialogue.closeLabel, 24);
        if (locale.dialogue.sets == null)
            errors.Add("locale.dialogue.sets is required.");
        ValidateAffinityLines(locale.outcomes.defeat, "locale.outcomes.defeat",
            true, errors, warnings);
    }

    private static RougeTowerType[] CompileBuildOrder(string[] source,
        List<string> errors)
    {
        if (source == null || source.Length == 0) return Array.Empty<RougeTowerType>();
        List<RougeTowerType> result = new List<RougeTowerType>(source.Length);
        HashSet<RougeTowerType> seen = new HashSet<RougeTowerType>();
        for (int i = 0; i < source.Length; i++)
        {
            string id = source[i]?.Trim();
            if (!Enum.TryParse(id, true, out RougeTowerType type) ||
                !string.Equals(Enum.GetName(typeof(RougeTowerType), type), id,
                    StringComparison.OrdinalIgnoreCase) ||
                (int)type < 0 || (int)type >= TowerDefenseVisuals.StandardTowerTypeCount)
            {
                errors.Add($"strategy.buildOrder[{i}] is not a standard tower ID: {id}");
                continue;
            }
            if (!seen.Add(type))
            {
                errors.Add($"strategy.buildOrder contains duplicate tower ID: {id}");
                continue;
            }
            result.Add(type);
        }
        return result.ToArray();
    }

    private static Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig>
        CompileDialogueRules(RougeAutoplayCommanderDialogueRuleConfig[] sets,
            List<string> errors)
    {
        Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig> result =
            new Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig>(
                StringComparer.Ordinal);
        if (sets != null)
        {
            for (int i = 0; i < sets.Length; i++)
            {
                RougeAutoplayCommanderDialogueRuleConfig set = sets[i];
                if (set == null || string.IsNullOrWhiteSpace(set.category))
                {
                    errors.Add($"dialogue.sets[{i}].category is required.");
                    continue;
                }
                string category = set.category.Trim();
                if (Array.IndexOf(RequiredDialogueCategories, category) < 0)
                {
                    errors.Add($"dialogue.sets[{i}].category is unknown: {category}");
                    continue;
                }
                if (result.ContainsKey(category))
                {
                    errors.Add("dialogue.sets contains duplicate category: " + category);
                    continue;
                }
                if (set.priority < 1 || set.priority > 20)
                    errors.Add(category + ".priority must be within [1, 20].");
                result.Add(category, set);
            }
        }
        for (int i = 0; i < RequiredDialogueCategories.Length; i++)
            if (!result.ContainsKey(RequiredDialogueCategories[i]))
                errors.Add("dialogue.sets is missing category: " +
                           RequiredDialogueCategories[i]);
        return result;
    }

    private static Dictionary<string, RougeAutoplayCommanderDialogueSetConfig>
        CompileLocalizedDialogueSets(
            RougeAutoplayCommanderDialogueSetConfig[] sets,
            List<string> errors, List<string> warnings)
    {
        Dictionary<string, RougeAutoplayCommanderDialogueSetConfig> result =
            new Dictionary<string, RougeAutoplayCommanderDialogueSetConfig>(
                StringComparer.Ordinal);
        if (sets != null)
        {
            for (int i = 0; i < sets.Length; i++)
            {
                RougeAutoplayCommanderDialogueSetConfig set = sets[i];
                if (set == null || string.IsNullOrWhiteSpace(set.category))
                {
                    errors.Add($"locale.dialogue.sets[{i}].category is required.");
                    continue;
                }
                string category = set.category.Trim();
                if (Array.IndexOf(RequiredDialogueCategories, category) < 0)
                {
                    errors.Add($"locale.dialogue.sets[{i}].category is unknown: " +
                               category);
                    continue;
                }
                if (result.ContainsKey(category))
                {
                    errors.Add("locale.dialogue.sets contains duplicate category: " +
                               category);
                    continue;
                }
                ValidateAffinityLines(set, "locale.dialogue." + category, false,
                    errors, warnings);
                result.Add(category, set);
            }
        }
        for (int i = 0; i < RequiredDialogueCategories.Length; i++)
            if (!result.ContainsKey(RequiredDialogueCategories[i]))
                errors.Add("locale.dialogue.sets is missing category: " +
                           RequiredDialogueCategories[i]);
        return result;
    }

    private static void ValidateLines(string[] lines, string path, bool required,
        bool allowLineBreaks, List<string> errors, List<string> warnings)
    {
        if (lines == null || lines.Length == 0)
        {
            if (required) errors.Add(path + " must contain at least one line.");
            return;
        }
        if (lines.Length > 64) errors.Add(path + " may contain at most 64 lines.");
        HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                errors.Add($"{path}[{i}] may not be empty.");
                continue;
            }
            if (line.Length > 180)
                errors.Add($"{path}[{i}] exceeds 180 characters.");
            ValidateSafeCharacters(errors, $"{path}[{i}]", line,
                allowLineBreaks);
            if (!unique.Add(line))
                warnings.Add($"{path}[{i}] duplicates another line in the same set.");
        }
    }

    private static void ValidateAffinityLines(
        RougeAutoplayCommanderDialogueSetConfig lines, string path,
        bool allowLineBreaks, List<string> errors, List<string> warnings)
    {
        if (lines == null)
        {
            errors.Add(path + " is required.");
            return;
        }
        ValidateAffinityLines(lines.distant, lines.familiar, lines.close, path,
            allowLineBreaks, errors, warnings);
    }

    private static void ValidateAffinityLines(
        RougeAutoplayCommanderAffinityLinesConfig lines, string path,
        bool allowLineBreaks, List<string> errors, List<string> warnings)
    {
        if (lines == null)
        {
            errors.Add(path + " is required.");
            return;
        }
        ValidateAffinityLines(lines.distant, lines.familiar, lines.close, path,
            allowLineBreaks, errors, warnings);
    }

    private static void ValidateAffinityLines(string[] distant, string[] familiar,
        string[] close, string path, bool allowLineBreaks, List<string> errors,
        List<string> warnings)
    {
        ValidateLines(distant, path + ".distant", true, allowLineBreaks, errors,
            warnings);
        ValidateLines(familiar, path + ".familiar", true, allowLineBreaks, errors,
            warnings);
        ValidateLines(close, path + ".close", true, allowLineBreaks, errors,
            warnings);
        if (HaveSameLines(distant, familiar) || HaveSameLines(distant, close) ||
            HaveSameLines(familiar, close))
            errors.Add(path +
                       " must provide meaningfully different Distant, Familiar and Close line sets.");
    }

    private static bool HaveSameLines(string[] left, string[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;
        HashSet<string> values = new HashSet<string>(left, StringComparer.Ordinal);
        return values.Count == right.Length && values.SetEquals(right);
    }

    private static void ValidateSkill(List<string> errors, string path, float value)
    {
        ValidateRange(errors, path, value, 0f, 1f);
    }

    private static void ValidateRatio(List<string> errors, string path, float value)
    {
        ValidateRange(errors, path, value, 0f, 1f);
    }

    private static void ValidateRange(List<string> errors, string path, float value,
        float minimum, float maximum)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum ||
            value > maximum)
            errors.Add($"{path} must be finite and within [{minimum}, {maximum}].");
    }

    private static void ValidateRequiredText(List<string> errors, string path,
        string value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(path + " is required.");
        else if (value.Length > maximumLength)
            errors.Add($"{path} exceeds {maximumLength} characters.");
        if (!string.IsNullOrEmpty(value))
            ValidateSafeCharacters(errors, path, value, false);
    }

    private static void ValidateTextArray(List<string> errors, string path,
        string[] values, int minimumCount, int maximumCount, int maximumItemLength)
    {
        if (values == null || values.Length < minimumCount ||
            values.Length > maximumCount)
        {
            errors.Add($"{path} must contain [{minimumCount}, {maximumCount}] items.");
            return;
        }
        for (int i = 0; i < values.Length; i++)
            ValidateRequiredText(errors, $"{path}[{i}]", values[i],
                maximumItemLength);
    }

    private static void ValidateSafeCharacters(List<string> errors, string path,
        string value, bool allowLineBreaks)
    {
        if (value.IndexOf('<') >= 0 || value.IndexOf('>') >= 0)
        {
            errors.Add(path + " may not contain '<' or '>' rich-text delimiters.");
            return;
        }
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (!char.IsControl(character)) continue;
            if (allowLineBreaks && (character == '\n' || character == '\r'))
                continue;
            errors.Add(path + " contains a forbidden control character.");
            return;
        }
    }

    private static bool IsSafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 48) return false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' ||
                c == '_') continue;
            return false;
        }
        return true;
    }

    private static bool IsSafeLocaleIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2 ||
            value.Length > 24 || value[0] == '-' || value[value.Length - 1] == '-')
            return false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' ||
                c >= '0' && c <= '9' || c == '-') continue;
            return false;
        }
        return true;
    }

    private static string BuildReport(List<string> errors, List<string> warnings)
    {
        StringBuilder builder = new StringBuilder();
        if (errors.Count == 0)
            builder.Append("Commander config is valid.");
        else
        {
            builder.Append("Commander config has ").Append(errors.Count)
                .Append(" error(s):");
            for (int i = 0; i < errors.Count; i++)
                builder.Append("\n- ").Append(errors[i]);
        }
        if (warnings.Count > 0)
        {
            builder.Append("\nWarnings:");
            for (int i = 0; i < warnings.Count; i++)
                builder.Append("\n- ").Append(warnings[i]);
        }
        return builder.ToString();
    }

    private static RougeAutoplayCommanderDefinition CreateFallback()
    {
        RougeAutoplayCommanderConfigData data = new RougeAutoplayCommanderConfigData();
        RougeAutoplayCommanderLocaleData locale =
            new RougeAutoplayCommanderLocaleData();
        locale.identity.background =
            "内置安全回退指挥官。她只在外部配置无效时接管防线。";
        locale.identity.speakingStyle = "简洁、可靠、直接报告战场状态。";
        locale.identity.personalityTraits = new[] { "可靠", "克制" };
        locale.personality.thinkingStyle = "先守住主塔，再按预算补齐火力。";
        locale.personality.decisionPrinciples =
            new[] { "合法动作优先", "主塔安全优先" };
        data.dialogue.sets = new RougeAutoplayCommanderDialogueRuleConfig[
            RequiredDialogueCategories.Length];
        locale.dialogue.sets = new RougeAutoplayCommanderDialogueSetConfig[
            RequiredDialogueCategories.Length];
        Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig> ruleLookup =
            new Dictionary<string, RougeAutoplayCommanderDialogueRuleConfig>(
                StringComparer.Ordinal);
        Dictionary<string, RougeAutoplayCommanderDialogueSetConfig> textLookup =
            new Dictionary<string, RougeAutoplayCommanderDialogueSetConfig>(
                StringComparer.Ordinal);
        for (int i = 0; i < RequiredDialogueCategories.Length; i++)
        {
            string category = RequiredDialogueCategories[i];
            RougeAutoplayCommanderDialogueRuleConfig rule =
                new RougeAutoplayCommanderDialogueRuleConfig
                {
                    category = category,
                    priority = category.StartsWith("PortraitClick",
                                   StringComparison.Ordinal) ||
                               category.StartsWith("PortraitRapidClick",
                                   StringComparison.Ordinal)
                        ? 20
                        : 1,
                    battleState = category == "Calm" || category == "Crowd" ||
                                  category == "Hard" || category == "Boss" ||
                                  category == "Urgent" || category == "BaseLow" ||
                                  category == "BaseCritical"
                };
            RougeAutoplayCommanderDialogueSetConfig text =
                new RougeAutoplayCommanderDialogueSetConfig
                {
                    category = category,
                    distant = new[] { "状态收到。我会执行安全方案。" },
                    familiar = new[] { "收到，指挥官。我会继续观察并执行安全方案。" },
                    close = new[] { "交给我吧。我会陪你把防线守稳。" }
                };
            data.dialogue.sets[i] = rule;
            locale.dialogue.sets[i] = text;
            ruleLookup.Add(category, rule);
            textLookup.Add(category, text);
        }
        RougeTowerType[] buildOrder =
        {
            RougeTowerType.MachineGun, RougeTowerType.Ice, RougeTowerType.Cannon,
            RougeTowerType.Flame, RougeTowerType.Laser,
            RougeTowerType.RocketBarrage, RougeTowerType.OrbitSphere,
            RougeTowerType.PiercingLaser
        };
        return new RougeAutoplayCommanderDefinition(data, locale, buildOrder,
            ruleLookup, textLookup, DefaultPortraitResourceFolder,
            DefaultPortraitResourcePath);
    }
}

/// <summary>
/// JsonUtility is intentionally kept as the runtime deserializer, but by itself it
/// silently accepts misspelled and missing fields. This small structural pass rejects
/// both before defaults can hide an invalid model response.
/// </summary>
internal static class RougeStrictJsonShapeValidator
{
    public static bool TryValidate<T>(string json, out string report)
    {
        Reader reader = new Reader(json);
        return reader.TryValidate(typeof(T), out report);
    }

    private sealed class Reader
    {
        private readonly string _json;
        private readonly List<string> _errors = new List<string>();
        private int _index;

        public Reader(string json)
        {
            _json = json ?? string.Empty;
        }

        public bool TryValidate(Type rootType, out string report)
        {
            try
            {
                ReadValue(rootType, "$", false);
                SkipWhitespace();
                if (_index != _json.Length)
                    AddError("$", "contains trailing content");
            }
            catch (FormatException exception)
            {
                AddError("$", exception.Message);
            }

            if (_errors.Count == 0)
            {
                report = "Commander JSON shape is valid.";
                return true;
            }

            StringBuilder builder = new StringBuilder(
                "Commander JSON shape is invalid:");
            for (int i = 0; i < _errors.Count; i++)
                builder.Append("\n- ").Append(_errors[i]);
            report = builder.ToString();
            return false;
        }

        private void ReadValue(Type expectedType, string path, bool allowNull)
        {
            SkipWhitespace();
            if (_index >= _json.Length)
                throw new FormatException("ended before a value was complete");

            if (StartsWith("null"))
            {
                ReadLiteral("null");
                if (!allowNull) AddError(path, "may not be null");
                return;
            }

            if (expectedType.IsArray)
            {
                if (Peek() != '[')
                {
                    AddError(path, "must be an array");
                    SkipValue();
                    return;
                }
                ReadArray(expectedType.GetElementType(), path);
                return;
            }

            if (expectedType == typeof(string))
            {
                if (Peek() != '"')
                {
                    AddError(path, "must be a string");
                    SkipValue();
                    return;
                }
                ReadString();
                return;
            }

            if (expectedType == typeof(bool))
            {
                if (StartsWith("true")) ReadLiteral("true");
                else if (StartsWith("false")) ReadLiteral("false");
                else
                {
                    AddError(path, "must be a boolean");
                    SkipValue();
                }
                return;
            }

            if (expectedType == typeof(int))
            {
                if (!IsNumberStart(Peek()))
                {
                    AddError(path, "must be an integer");
                    SkipValue();
                    return;
                }
                ReadInteger(path);
                return;
            }

            if (expectedType == typeof(float) || expectedType == typeof(double))
            {
                if (!IsNumberStart(Peek()))
                {
                    AddError(path, "must be a number");
                    SkipValue();
                    return;
                }
                ReadNumber();
                return;
            }

            if (Peek() != '{')
            {
                AddError(path, "must be an object");
                SkipValue();
                return;
            }
            ReadObject(expectedType, path);
        }

        private void ReadObject(Type expectedType, string path)
        {
            Expect('{');
            FieldInfo[] fields = expectedType.GetFields(
                BindingFlags.Instance | BindingFlags.Public);
            Dictionary<string, FieldInfo> allowed =
                new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i++)
                allowed[fields[i].Name] = fields[i];
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            SkipWhitespace();
            if (TryConsume('}'))
            {
                ReportMissingFields(path, fields, seen);
                return;
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                    throw new FormatException("object field name must be a string");
                string key = ReadString();
                SkipWhitespace();
                Expect(':');
                string fieldPath = path + "." + key;
                if (!allowed.TryGetValue(key, out FieldInfo field))
                {
                    AddError(fieldPath, "is not an allowed field");
                    SkipValue();
                }
                else
                {
                    if (!seen.Add(key)) AddError(fieldPath, "is duplicated");
                    ReadValue(field.FieldType, fieldPath, false);
                }

                SkipWhitespace();
                if (TryConsume('}')) break;
                Expect(',');
            }
            ReportMissingFields(path, fields, seen);
        }

        private void ReadArray(Type elementType, string path)
        {
            Expect('[');
            SkipWhitespace();
            if (TryConsume(']')) return;
            int itemIndex = 0;
            while (true)
            {
                ReadValue(elementType, path + "[" + itemIndex + "]", false);
                itemIndex++;
                SkipWhitespace();
                if (TryConsume(']')) return;
                Expect(',');
            }
        }

        private void ReportMissingFields(string path, FieldInfo[] fields,
            HashSet<string> seen)
        {
            for (int i = 0; i < fields.Length; i++)
                if (!seen.Contains(fields[i].Name))
                    AddError(path + "." + fields[i].Name, "is required");
        }

        private void SkipValue()
        {
            SkipWhitespace();
            char value = Peek();
            if (value == '"')
            {
                ReadString();
                return;
            }
            if (value == '{')
            {
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}')) return;
                while (true)
                {
                    ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipValue();
                    SkipWhitespace();
                    if (TryConsume('}')) return;
                    Expect(',');
                }
            }
            if (value == '[')
            {
                Expect('[');
                SkipWhitespace();
                if (TryConsume(']')) return;
                while (true)
                {
                    SkipValue();
                    SkipWhitespace();
                    if (TryConsume(']')) return;
                    Expect(',');
                }
            }
            if (StartsWith("true")) ReadLiteral("true");
            else if (StartsWith("false")) ReadLiteral("false");
            else if (StartsWith("null")) ReadLiteral("null");
            else if (IsNumberStart(value)) ReadNumber();
            else throw new FormatException("contains an invalid JSON value");
        }

        private string ReadString()
        {
            Expect('"');
            StringBuilder builder = new StringBuilder();
            while (_index < _json.Length)
            {
                char value = _json[_index++];
                if (value == '"') return builder.ToString();
                if (value < 0x20)
                    throw new FormatException("string contains an unescaped control character");
                if (value != '\\')
                {
                    builder.Append(value);
                    continue;
                }
                if (_index >= _json.Length)
                    throw new FormatException("string escape is incomplete");
                char escape = _json[_index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        builder.Append(ReadUnicodeEscape());
                        break;
                    default:
                        throw new FormatException("string contains an invalid escape");
                }
            }
            throw new FormatException("string is not terminated");
        }

        private char ReadUnicodeEscape()
        {
            if (_index + 4 > _json.Length)
                throw new FormatException("unicode escape is incomplete");
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                char digit = _json[_index++];
                value <<= 4;
                if (digit >= '0' && digit <= '9') value += digit - '0';
                else if (digit >= 'a' && digit <= 'f') value += digit - 'a' + 10;
                else if (digit >= 'A' && digit <= 'F') value += digit - 'A' + 10;
                else throw new FormatException("unicode escape contains a non-hex digit");
            }
            return (char)value;
        }

        private void ReadNumber()
        {
            int start = _index;
            if (TryConsume('-')) { }
            if (TryConsume('0')) { }
            else
            {
                if (!IsDigit(Peek()))
                    throw new FormatException("number is invalid");
                while (IsDigit(Peek())) _index++;
            }
            if (TryConsume('.'))
            {
                if (!IsDigit(Peek()))
                    throw new FormatException("number fraction is invalid");
                while (IsDigit(Peek())) _index++;
            }
            char exponent = Peek();
            if (exponent == 'e' || exponent == 'E')
            {
                _index++;
                char sign = Peek();
                if (sign == '+' || sign == '-') _index++;
                if (!IsDigit(Peek()))
                    throw new FormatException("number exponent is invalid");
                while (IsDigit(Peek())) _index++;
            }
            if (_index == start) throw new FormatException("number is invalid");
        }

        private void ReadInteger(string path)
        {
            int start = _index;
            ReadNumber();
            int length = _index - start;
            string token = _json.Substring(start, length);
            if (token.IndexOf('.') >= 0 || token.IndexOf('e') >= 0 ||
                token.IndexOf('E') >= 0)
            {
                AddError(path, "must be an integer without a fraction or exponent");
                return;
            }
            if (!int.TryParse(token, out _))
                AddError(path, "must fit in a signed 32-bit integer");
        }

        private void ReadLiteral(string value)
        {
            if (!StartsWith(value))
                throw new FormatException("JSON literal is invalid");
            _index += value.Length;
        }

        private bool StartsWith(string value)
        {
            if (_index + value.Length > _json.Length) return false;
            return string.CompareOrdinal(_json, _index, value, 0, value.Length) == 0;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            if (!TryConsume(expected))
                throw new FormatException("expected '" + expected + "'");
        }

        private bool TryConsume(char value)
        {
            if (_index >= _json.Length || _json[_index] != value) return false;
            _index++;
            return true;
        }

        private char Peek()
        {
            return _index < _json.Length ? _json[_index] : '\0';
        }

        private void SkipWhitespace()
        {
            while (_index < _json.Length &&
                   (_json[_index] == ' ' || _json[_index] == '\t' ||
                    _json[_index] == '\r' || _json[_index] == '\n'))
                _index++;
        }

        private static bool IsDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static bool IsNumberStart(char value)
        {
            return value == '-' || IsDigit(value);
        }

        private void AddError(string path, string message)
        {
            if (_errors.Count < 64) _errors.Add(path + " " + message + ".");
        }
    }
}
