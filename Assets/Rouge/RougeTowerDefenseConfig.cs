using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class RougeTowerLevelConfig
{
    [Min(0f)] public float damage;
    [Min(0.001f)] public float attackInterval = 1f;
    [Min(0f)] public float attackRange = 10f;
    [Min(1)] public int targetCount = 1;
    [Min(1)] public int projectileCount = 1;
    [Min(0f)] public float aoeRadius;
    [Range(0f, 100f)] public float effectPercent;
    [Min(0f)] public float effectDuration;
    [Min(0f)] public float tickInterval;
    [Min(0.1f)] public float orbitSphereRadius = 1.2f;
    [Min(0.1f)] public float orbitRadialSpeed = 8f;
    [Min(0f)] public float orbitAngularSpeed = 180f;
    [Min(0f), Tooltip("Seconds crystal lasers remain at maximum range before returning.")]
    public float orbitOuterHoldDuration = 1.5f;
    [Min(0.01f), Tooltip("Seconds between shots inside a rocket-tower salvo.")]
    public float projectileInterval = 0.09f;
    [Min(0.05f), Tooltip("Flight time for one rocket-tower missile.")]
    public float projectileFlightDuration = 1.05f;
    [Min(0f), Tooltip("Strength of the rocket tower's damped Brownian air drift.")]
    public float brownianStrength = 3f;

    public RougeTowerStats ToStats()
    {
        return new RougeTowerStats(damage, Mathf.Max(0.001f, attackInterval), Mathf.Max(0f, attackRange),
            Mathf.Max(1, targetCount), Mathf.Max(1, projectileCount), Mathf.Max(0f, aoeRadius),
            Mathf.Clamp(effectPercent, 0f, 100f), Mathf.Max(0f, effectDuration), Mathf.Max(0f, tickInterval),
            Mathf.Max(0.1f, orbitSphereRadius), Mathf.Max(0.1f, orbitRadialSpeed), Mathf.Max(0f, orbitAngularSpeed),
            Mathf.Max(0f, orbitOuterHoldDuration), Mathf.Max(0.01f, projectileInterval),
            Mathf.Max(0.05f, projectileFlightDuration), Mathf.Max(0f, brownianStrength));
    }
}

[Serializable]
public sealed class RougeTowerTypeConfig
{
    public RougeTowerType towerType;
    [Min(0.1f)] public float placementRadius = 2f;
    [HideInInspector] public int footprintSize = 4;
    [Range(1, 16), Tooltip("Tower footprint width in map micro cells.")]
    public int footprintWidth;
    [Range(1, 16), Tooltip("Tower footprint height in map micro cells.")]
    public int footprintHeight;
    [Min(0)] public int purchaseCost = 400;
    public List<RougeTowerLevelConfig> levels = new List<RougeTowerLevelConfig>();
}

[Serializable]
public sealed class RougeTowerBalanceConfig
{
    [Range(0f, 1f)] public float sellRefundMultiplier = 0.25f;
    public List<RougeTowerTypeConfig> towers = new List<RougeTowerTypeConfig>();

    public void EnsureDefaults()
    {
        foreach (RougeTowerType type in Enum.GetValues(typeof(RougeTowerType)))
        {
            RougeTowerTypeConfig config = Find(type);
            if (config == null)
            {
                config = CreateDefault(type);
                towers.Add(config);
            }
            int legacySize = Mathf.Clamp(config.footprintSize <= 0 ? 4 : config.footprintSize, 1, 16);
            config.footprintWidth = Mathf.Clamp(config.footprintWidth <= 0 ? legacySize : config.footprintWidth, 1, 16);
            config.footprintHeight = Mathf.Clamp(config.footprintHeight <= 0 ? legacySize : config.footprintHeight, 1, 16);
            while (config.levels.Count < TowerDefenseVisuals.MaxTowerLevel)
            {
                config.levels.Add(CreateDefaultLevel(type, config.levels.Count));
            }
            if (config.levels.Count > TowerDefenseVisuals.MaxTowerLevel)
            {
                config.levels.RemoveRange(TowerDefenseVisuals.MaxTowerLevel,
                    config.levels.Count - TowerDefenseVisuals.MaxTowerLevel);
            }
        }
    }

    public RougeTowerTypeConfig Find(RougeTowerType type)
    {
        for (int i = 0; i < towers.Count; i++)
        {
            if (towers[i] != null && towers[i].towerType == type) return towers[i];
        }
        return null;
    }

    private static RougeTowerTypeConfig CreateDefault(RougeTowerType type)
    {
        RougeTowerTypeConfig config = new RougeTowerTypeConfig { towerType = type };
        switch (type)
        {
            case RougeTowerType.MachineGun: config.placementRadius = 2f; config.purchaseCost = 400; break;
            case RougeTowerType.Ice: config.placementRadius = 2.2f; config.purchaseCost = 625; break;
            case RougeTowerType.Cannon: config.placementRadius = 2.7f; config.purchaseCost = 750; break;
            case RougeTowerType.Flame: config.placementRadius = 2.4f; config.purchaseCost = 625; break;
            case RougeTowerType.Laser: config.placementRadius = 2.3f; config.purchaseCost = 750; break;
            case RougeTowerType.PiercingLaser: config.placementRadius = 2.8f; config.purchaseCost = 1000; break;
            case RougeTowerType.OrbitSphere: config.placementRadius = 2.5f; config.purchaseCost = 900; break;
            default: config.placementRadius = 2.8f; config.purchaseCost = 1400; break;
        }
        for (int i = 0; i < TowerDefenseVisuals.MaxTowerLevel; i++)
        {
            config.levels.Add(CreateDefaultLevel(type, i));
        }
        return config;
    }

    private static RougeTowerLevelConfig CreateDefaultLevel(RougeTowerType type, int i)
    {
        RougeTowerStats fallback = TowerDefenseVisuals.GetFallbackStats(type, i + 1);
        return new RougeTowerLevelConfig
        {
            damage = fallback.Damage,
            attackInterval = fallback.AttackInterval,
            attackRange = fallback.AttackRadius * 2f,
            targetCount = fallback.TargetCount,
            projectileCount = fallback.ProjectileCount,
            aoeRadius = fallback.AoeRadius,
            effectPercent = type == RougeTowerType.Ice ? 25f : fallback.EffectPercent,
            effectDuration = fallback.EffectDuration,
            tickInterval = fallback.TickInterval,
            orbitSphereRadius = fallback.OrbitSphereRadius,
            orbitRadialSpeed = fallback.OrbitRadialSpeed,
            orbitAngularSpeed = fallback.OrbitAngularSpeed,
            orbitOuterHoldDuration = fallback.OrbitOuterHoldDuration,
            projectileInterval = fallback.ProjectileInterval,
            projectileFlightDuration = fallback.ProjectileFlightDuration,
            brownianStrength = fallback.BrownianStrength
        };
    }
}

[Serializable]
public sealed class RougeEnemyArchetypeConfig
{
    public string displayName = "Normal";
    [Min(0.01f)] public float baseHealth = 10f;
    [Min(0.01f), Tooltip("Scales only the health gained from the global enemy-level curve. 1 keeps the global curve unchanged; values above 1 grow faster without changing level-1 base health.")]
    public float healthGrowthMultiplier = 1f;
    [Min(0.01f)] public float baseSpeed = 6f;
    [Min(0.1f)] public float size = 1f;
    [Tooltip("Texture under an Assets/.../Resources folder, without extension.")]
    public string spriteResourcePath = "Sprites/enemy_standard_sheet";
    [Min(1)] public int spriteSheetColumns = 3;
    [Min(1)] public int spriteSheetRows = 2;
    [Min(0.01f)] public float spriteAnimationFps = 9f;
    [Min(0)] public int spriteDeathFrameCount = 2;

    public void EnsureDefaults()
    {
        if (healthGrowthMultiplier <= 0f) healthGrowthMultiplier = 1f;
        bool isStandardSheet = !string.IsNullOrEmpty(spriteResourcePath) &&
            spriteResourcePath.EndsWith("enemy_standard_sheet", StringComparison.OrdinalIgnoreCase);
        if (spriteSheetColumns < 1) spriteSheetColumns = 3;
        if (spriteSheetRows < 1) spriteSheetRows = isStandardSheet ? 3 : 2;
        if (spriteAnimationFps <= 0f) spriteAnimationFps = isStandardSheet ? 14f : 9f;
        if (spriteDeathFrameCount < 0) spriteDeathFrameCount = 2;
        spriteDeathFrameCount = Mathf.Min(spriteDeathFrameCount,
            Mathf.Max(0, spriteSheetColumns * spriteSheetRows - 1));
    }
}

[Serializable]
public sealed class RougeEnemyBalanceConfig
{
    public const int MaximumEnemyLevel = 100;

    [Min(0)] public int normalKillGold = 1;
    [Min(0)] public int eliteKillGold = 20;
    [Min(1f)] public float growthInterval = 15f;
    [HideInInspector] public float healthGrowthMultiplier = 1.10f; // Legacy scene data; HP now follows milestones.
    [Tooltip("Enemy health multiplier by level. X is enemy level (1-100); Y is the multiplier.")]
    public AnimationCurve healthMultiplierByLevel = CreateDefaultHealthMultiplierCurve();
    [HideInInspector] public float speedGrowthMultiplier = 1.007f; // Legacy JSON data; speed now follows its level curve.
    [Tooltip("Enemy speed multiplier by level. X is enemy level (1-100); Y is the multiplier.")]
    public AnimationCurve speedMultiplierByLevel = CreateDefaultSpeedMultiplierCurve();
    [Tooltip("Spawn frequency multiplier by level. X is enemy level (1-100); Y divides each spawner's base interval.")]
    public AnimationCurve spawnSpeedMultiplierByLevel = CreateDefaultSpawnSpeedMultiplierCurve();
    [Tooltip("Elite spawn chance per thousand by level. X is enemy level (1-100); Y is permille (‰).")]
    public AnimationCurve eliteChancePermilleByLevel = CreateDefaultEliteChancePermilleCurve();
    [Min(1f)] public float eliteHealthMultiplier = 20f;
    [Min(0.1f)] public float eliteSpeedMultiplier = 1.25f;
    [Min(1f)] public float eliteSizeMultiplier = 2f;
    public List<RougeEnemyArchetypeConfig> enemyTypes = new List<RougeEnemyArchetypeConfig>();

    public void EnsureDefaults()
    {
        if (healthMultiplierByLevel == null || healthMultiplierByLevel.length == 0)
            healthMultiplierByLevel = CreateDefaultHealthMultiplierCurve();
        if (speedMultiplierByLevel == null || speedMultiplierByLevel.length == 0)
            speedMultiplierByLevel = CreateDefaultSpeedMultiplierCurve();
        if (spawnSpeedMultiplierByLevel == null || spawnSpeedMultiplierByLevel.length == 0)
            spawnSpeedMultiplierByLevel = CreateDefaultSpawnSpeedMultiplierCurve();
        if (eliteChancePermilleByLevel == null || eliteChancePermilleByLevel.length == 0)
            eliteChancePermilleByLevel = CreateDefaultEliteChancePermilleCurve();
        healthMultiplierByLevel.preWrapMode = WrapMode.ClampForever;
        healthMultiplierByLevel.postWrapMode = WrapMode.ClampForever;
        speedMultiplierByLevel.preWrapMode = WrapMode.ClampForever;
        speedMultiplierByLevel.postWrapMode = WrapMode.ClampForever;
        spawnSpeedMultiplierByLevel.preWrapMode = WrapMode.ClampForever;
        spawnSpeedMultiplierByLevel.postWrapMode = WrapMode.ClampForever;
        eliteChancePermilleByLevel.preWrapMode = WrapMode.ClampForever;
        eliteChancePermilleByLevel.postWrapMode = WrapMode.ClampForever;
        if (enemyTypes == null) enemyTypes = new List<RougeEnemyArchetypeConfig>();
        if (enemyTypes.Count == 0)
        {
            enemyTypes.Add(new RougeEnemyArchetypeConfig
                { displayName = "Standard", baseHealth = 10f, baseSpeed = 6f, size = 1f,
                    spriteResourcePath = "Sprites/enemy_standard_sheet", spriteSheetColumns = 3,
                    spriteSheetRows = 3, spriteAnimationFps = 14f, spriteDeathFrameCount = 2 });
            enemyTypes.Add(new RougeEnemyArchetypeConfig
                { displayName = "Swift", baseHealth = 8.2f, baseSpeed = 7f, size = 0.9f, spriteResourcePath = "Sprites/enemy_swift_sheet" });
            enemyTypes.Add(new RougeEnemyArchetypeConfig
                { displayName = "Heavy", baseHealth = 13.5f, baseSpeed = 5.3f, size = 1.18f, spriteResourcePath = "Sprites/enemy_heavy_sheet" });
        }
        while (enemyTypes.Count < 3) enemyTypes.Add(new RougeEnemyArchetypeConfig { displayName = "Variant " + (enemyTypes.Count + 1) });
        for (int i = 0; i < enemyTypes.Count; i++)
        {
            enemyTypes[i] ??= new RougeEnemyArchetypeConfig();
            enemyTypes[i].EnsureDefaults();
        }
    }

    public float EvaluateHealthMultiplier(int enemyLevel)
    {
        if (healthMultiplierByLevel == null || healthMultiplierByLevel.length == 0)
            healthMultiplierByLevel = CreateDefaultHealthMultiplierCurve();
        return Mathf.Max(0.01f, healthMultiplierByLevel.Evaluate(
            Mathf.Clamp(enemyLevel, 1, MaximumEnemyLevel)));
    }

    public float EvaluateHealthMultiplier(int enemyLevel, float archetypeGrowthMultiplier)
    {
        float globalMultiplier = EvaluateHealthMultiplier(enemyLevel);
        float growthMultiplier = Mathf.Max(0.01f, archetypeGrowthMultiplier);
        return Mathf.Max(0.01f, 1f + (globalMultiplier - 1f) * growthMultiplier);
    }

    public float EvaluateSpeedMultiplier(int enemyLevel)
    {
        if (speedMultiplierByLevel == null || speedMultiplierByLevel.length == 0)
            speedMultiplierByLevel = CreateDefaultSpeedMultiplierCurve();
        return Mathf.Max(0.01f, speedMultiplierByLevel.Evaluate(
            Mathf.Clamp(enemyLevel, 1, MaximumEnemyLevel)));
    }

    public float EvaluateSpawnSpeedMultiplier(int enemyLevel)
    {
        if (spawnSpeedMultiplierByLevel == null || spawnSpeedMultiplierByLevel.length == 0)
            spawnSpeedMultiplierByLevel = CreateDefaultSpawnSpeedMultiplierCurve();
        return Mathf.Max(0.01f, spawnSpeedMultiplierByLevel.Evaluate(
            Mathf.Clamp(enemyLevel, 1, MaximumEnemyLevel)));
    }

    public float EvaluateEliteChance01(int enemyLevel)
    {
        if (eliteChancePermilleByLevel == null || eliteChancePermilleByLevel.length == 0)
            eliteChancePermilleByLevel = CreateDefaultEliteChancePermilleCurve();
        float permille = Mathf.Max(0f, eliteChancePermilleByLevel.Evaluate(
            Mathf.Clamp(enemyLevel, 1, MaximumEnemyLevel)));
        return Mathf.Clamp01(permille * 0.001f);
    }

    private static AnimationCurve CreateDefaultHealthMultiplierCurve()
    {
        Keyframe[] keys =
        {
            new Keyframe(1f, 1f),
            new Keyframe(13f, 8f),
            new Keyframe(25f, 24f),
            new Keyframe(37f, 48f),
            new Keyframe(49f, 72f),
            new Keyframe(61f, 144f),
            new Keyframe(81f, 288f),
            new Keyframe(100f, 288f)
        };

        // Linear defaults make the level preview predictable. Designers can edit
        // individual tangents in the curve window when a smoother ramp is desired.
        SetLinearTangents(keys);
        return CreateClampedCurve(keys);
    }

    private static AnimationCurve CreateDefaultSpeedMultiplierCurve()
    {
        int[] levels = { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        Keyframe[] keys = new Keyframe[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            int level = levels[i];
            keys[i] = new Keyframe(level, Mathf.Pow(1.007f, level - 1));
        }
        SetLinearTangents(keys);
        return CreateClampedCurve(keys);
    }

    private static AnimationCurve CreateDefaultSpawnSpeedMultiplierCurve()
    {
        Keyframe[] keys =
        {
            new Keyframe(1f, 1f),
            new Keyframe(41f, 2f),
            new Keyframe(81f, 3f),
            new Keyframe(MaximumEnemyLevel, 3f)
        };
        SetLinearTangents(keys);
        return CreateClampedCurve(keys);
    }

    private static AnimationCurve CreateDefaultEliteChancePermilleCurve()
    {
        Keyframe[] keys =
        {
            new Keyframe(1f, 1f),
            new Keyframe(MaximumEnemyLevel, 5f)
        };
        SetLinearTangents(keys);
        return CreateClampedCurve(keys);
    }

    private static void SetLinearTangents(Keyframe[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].inTangent = i > 0
                ? (keys[i].value - keys[i - 1].value) / (keys[i].time - keys[i - 1].time)
                : 0f;
            keys[i].outTangent = i + 1 < keys.Length
                ? (keys[i + 1].value - keys[i].value) / (keys[i + 1].time - keys[i].time)
                : 0f;
        }
    }

    private static AnimationCurve CreateClampedCurve(Keyframe[] keys)
    {
        return new AnimationCurve(keys)
        {
            preWrapMode = WrapMode.ClampForever,
            postWrapMode = WrapMode.ClampForever
        };
    }
}

[Serializable]
public sealed class RougeBossBalanceConfig
{
    [Tooltip("Stable integer ID referenced by level Boss schedules and external content.")]
    public int bossId;
    public string displayName = "Overlord";
    [Min(1f)] public float spawnTimeSeconds = 900f;
    [Min(1f)] public float targetArrivalTimeSeconds = 1200f;
    [Min(1f), Tooltip("Desired travel time from Boss spawn to the main tower. Level schedules control the actual spawn minute.")]
    public float targetTravelTimeSeconds = 300f;
    [Min(1f)] public float maxHealth = 1000000f;
    [Min(0.1f)] public float moveSpeed = 3.5f; // Fallback when no valid route distance is available.
    [Range(0f, 95f)] public float maximumSlowPercent = 20f;
    [Min(0.5f)] public float radius = 5f;
    [Min(0.1f)] public float navigationRadius = 1.25f;
    public Vector3 fallbackSpawnPosition = new Vector3(0f, 0.25f, 135f);
    [Min(0f)] public float interferenceRadius = 20f;
    [Tooltip("Tower attack-speed Buff level applied by interference. Raw levels stack without a limit; the effect is capped to -3..+3.")]
    public int interferenceAttackSpeedBuffLevel = -2;
    [Min(0f)] public float shieldRadius = 30f;
    [Range(0.01f, 1f)] public float shieldDamageMultiplier = 0.5f;
    [Min(1f)] public float minimumShieldedDamage = 1f;
    [Min(1f)] public float hasteSpeedMultiplier = 1.25f;
    [Tooltip("Texture under an Assets/.../Resources folder, without extension. A single image is supported.")]
    public string spriteResourcePath = "Sprites/boss_overlord";
    [Range(1, 8)] public int spriteSheetColumns = 1;
    [Range(1, 8)] public int spriteSheetRows = 1;
    [Min(0.1f)] public float skillAnimationDuration = 1f;
    [Min(0.5f)] public float deathFocusDuration = 3.2f;

    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(displayName)) displayName = $"Boss {bossId}";
        if (spawnTimeSeconds <= 0f) spawnTimeSeconds = 900f;
        if (targetArrivalTimeSeconds <= spawnTimeSeconds)
            targetArrivalTimeSeconds = spawnTimeSeconds + 300f;
        if (targetTravelTimeSeconds <= 0f)
            targetTravelTimeSeconds = Mathf.Max(30f, targetArrivalTimeSeconds - spawnTimeSeconds);
        maxHealth = Mathf.Max(1f, maxHealth);
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        maximumSlowPercent = Mathf.Clamp(maximumSlowPercent, 0f, 95f);
        radius = Mathf.Max(0.5f, radius);
        if (navigationRadius <= 0f) navigationRadius = 1.25f;
        interferenceRadius = Mathf.Max(0f, interferenceRadius);
        shieldRadius = Mathf.Max(0f, shieldRadius);
        shieldDamageMultiplier = Mathf.Clamp(shieldDamageMultiplier, 0.01f, 1f);
        minimumShieldedDamage = Mathf.Max(1f, minimumShieldedDamage);
        hasteSpeedMultiplier = Mathf.Max(1f, hasteSpeedMultiplier);
        if (string.IsNullOrWhiteSpace(spriteResourcePath)) spriteResourcePath = "Sprites/boss_overlord";
        spriteSheetColumns = Mathf.Clamp(spriteSheetColumns, 1, 8);
        spriteSheetRows = Mathf.Clamp(spriteSheetRows, 1, 8);
        skillAnimationDuration = Mathf.Max(0.1f, skillAnimationDuration);
        deathFocusDuration = Mathf.Max(0.5f, deathFocusDuration);
    }
}

[Serializable]
public sealed class RougeWindmillTacticalSkillConfig
{
    [Min(0)] public int initialCost = 5000;
    [Min(1f)] public float costMultiplier = 1.5f;
    [Min(0f)] public float cooldown = 15f;
    [Min(0.05f)] public float fallDuration = 0.35f;
    [Min(0f)] public float fallHeight = 30f;
    [Min(0f)] public float impactRadius = 30f;
    [Min(0f)] public float impactDamage = 500f;
    [Min(0f)] public float startDelay = 0.3f;
    [Min(0.05f)] public float duration = 5f;
    [Min(0.05f)] public float tickInterval = 0.1f;
    [Min(0f)] public float tickDamage = 25f;
    [Min(0.1f)] public float radius = 12f;
    [Min(0f)] public float moveSpeed = 20f;
    [Min(0f)] public float killLaunchHeight = 18f;
    [Min(0f)] public float obstaclePadding = 1.2f;
}

[Serializable]
public sealed class RougeBlackHoleTacticalSkillConfig
{
    [Min(0)] public int initialCost = 10000;
    [Min(1f)] public float costMultiplier = 1.5f;
    [Min(0f)] public float cooldown = 20f;
    [Min(0.05f)] public float duration = 5f;
    [Min(0.05f)] public float tickInterval = 0.1f;
    [Min(0.1f)] public float pullRadius = 30f;
    [Min(0f)] public float pullSpeed = 5f;
    [Min(0.1f)] public float explosionRadius = 10f;
    [Min(0f)] public float explosionDamage = 10000f;
    [Min(0f)] public float killLaunchHeight = 22f;
}

[Serializable]
public sealed class RougeOverclockTacticalSkillConfig
{
    [Min(0)] public int initialCost = 2500;
    [Min(1f)] public float costMultiplier = 1.5f;
    [Min(0f)] public float cooldown = 15f;
    [Min(0.05f)] public float duration = 7f;
    [Tooltip("Raw damage Buff levels. Effective level is capped to -3..+3.")]
    public int damageBuffLevel = 2;
    [Tooltip("Raw range Buff levels. Effective level is capped to -3..+3.")]
    public int rangeBuffLevel;
    [Tooltip("Raw attack-speed Buff levels. Effective level is capped to -3..+3.")]
    public int attackSpeedBuffLevel = 2;
}

[Serializable]
public sealed class RougeMissileBarrageTacticalSkillConfig
{
    [Min(0f)] public float cooldown = 25f;
    [Min(0.1f)] public float selectionRadius = 50f;
    [Min(0.05f)] public float duration = 10f;
    [Min(0.01f)] public float minimumInterval = 0.3f;
    [Min(0.01f)] public float maximumInterval = 0.4f;
    [Min(0.1f)] public float impactRadius = 5f;
    [Min(0f)] public float impactDamage = 100f;
    [Min(0f)] public float fallHeight = 35f;
    [Min(0.05f)] public float fallDuration = 0.45f;
}

[Serializable]
public sealed class RougeTacticalSkillBalanceConfig
{
    [Min(0f)] public float damageGrowthPerCast = 0.1f;
    public RougeWindmillTacticalSkillConfig windmill = new RougeWindmillTacticalSkillConfig();
    public RougeBlackHoleTacticalSkillConfig blackHole = new RougeBlackHoleTacticalSkillConfig();
    public RougeOverclockTacticalSkillConfig overclock = new RougeOverclockTacticalSkillConfig();
    public RougeMissileBarrageTacticalSkillConfig missileBarrage = new RougeMissileBarrageTacticalSkillConfig();

    public void EnsureDefaults()
    {
        windmill ??= new RougeWindmillTacticalSkillConfig();
        blackHole ??= new RougeBlackHoleTacticalSkillConfig();
        overclock ??= new RougeOverclockTacticalSkillConfig();
        missileBarrage ??= new RougeMissileBarrageTacticalSkillConfig();
    }
}

[Serializable]
public sealed class RougeTowerDefenseBalanceJsonData
{
    public int version = 4;
    public RougeTowerBalanceConfig towerBalance = new RougeTowerBalanceConfig();
    public RougeEnemyBalanceConfig enemyBalance = new RougeEnemyBalanceConfig();
    public List<RougeBossBalanceConfig> bossBalances = new List<RougeBossBalanceConfig>();
    [Tooltip("Legacy single-Boss field retained so older JSON files migrate without data loss.")]
    public RougeBossBalanceConfig bossBalance = new RougeBossBalanceConfig();
    public RougeTacticalSkillBalanceConfig tacticalSkillBalance = new RougeTacticalSkillBalanceConfig();

    public void EnsureDefaults()
    {
        towerBalance ??= new RougeTowerBalanceConfig();
        enemyBalance ??= new RougeEnemyBalanceConfig();
        bossBalance ??= new RougeBossBalanceConfig();
        bossBalances ??= new List<RougeBossBalanceConfig>();
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
        tacticalSkillBalance ??= new RougeTacticalSkillBalanceConfig();
        towerBalance.EnsureDefaults();
        enemyBalance.EnsureDefaults();
        bossBalance.EnsureDefaults();
        tacticalSkillBalance.EnsureDefaults();
        version = Mathf.Max(version, 4);
    }
}

public sealed class RougeTowerDefenseBalanceProfile : ScriptableObject
{
    public RougeTowerBalanceConfig towerBalance = new RougeTowerBalanceConfig();
    public RougeEnemyBalanceConfig enemyBalance = new RougeEnemyBalanceConfig();
    public List<RougeBossBalanceConfig> bossBalances = new List<RougeBossBalanceConfig>();
    [HideInInspector]
    public RougeBossBalanceConfig bossBalance = new RougeBossBalanceConfig();
    public RougeTacticalSkillBalanceConfig tacticalSkillBalance = new RougeTacticalSkillBalanceConfig();

    public void EnsureDefaults()
    {
        towerBalance ??= new RougeTowerBalanceConfig();
        enemyBalance ??= new RougeEnemyBalanceConfig();
        bossBalance ??= new RougeBossBalanceConfig();
        bossBalances ??= new List<RougeBossBalanceConfig>();
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
        tacticalSkillBalance ??= new RougeTacticalSkillBalanceConfig();
        towerBalance.EnsureDefaults();
        enemyBalance.EnsureDefaults();
        bossBalance.EnsureDefaults();
        tacticalSkillBalance.EnsureDefaults();
    }

    public RougeTowerDefenseBalanceJsonData ToJsonData()
    {
        EnsureDefaults();
        return new RougeTowerDefenseBalanceJsonData
        {
            version = 4,
            towerBalance = towerBalance,
            enemyBalance = enemyBalance,
            bossBalances = bossBalances,
            bossBalance = bossBalance,
            tacticalSkillBalance = tacticalSkillBalance
        };
    }

    public void Apply(RougeTowerDefenseBalanceJsonData data)
    {
        data ??= new RougeTowerDefenseBalanceJsonData();
        data.EnsureDefaults();
        towerBalance = data.towerBalance;
        enemyBalance = data.enemyBalance;
        bossBalances = data.bossBalances;
        bossBalance = data.bossBalance;
        tacticalSkillBalance = data.tacticalSkillBalance;
    }
}

public static class RougeTowerDefenseBalanceJson
{
    public const string ResourcePath = "Config/tower_defense_balance";
    public const string AssetPath = "Assets/Resources/Config/tower_defense_balance.json";

    public static bool TryLoad(out RougeTowerDefenseBalanceJsonData data)
    {
        data = null;
        TextAsset jsonAsset = Resources.Load<TextAsset>(ResourcePath);
        if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text)) return false;
        try
        {
            data = JsonUtility.FromJson<RougeTowerDefenseBalanceJsonData>(jsonAsset.text);
            if (data == null) return false;
            data.EnsureDefaults();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Tower Defense balance JSON could not be loaded: {exception.Message}");
            data = null;
            return false;
        }
    }
}
