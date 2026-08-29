using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public sealed class RougeTowerLevelConfig
{
    [Min(0), Tooltip("Gold paid to build or upgrade the tower to this level, before map and tile modifiers.")]
    public int goldCost;
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
    [HideInInspector] public int footprintSize = 1;
    [HideInInspector] public int footprintWidth = 1;
    [HideInInspector] public int footprintHeight = 1;
    [Min(0)] public int purchaseCost = 400;
    [Min(0f), Tooltip("Additional base-cost multiplier for every existing tower of this type. Used by special support towers.")]
    public float specialTowerCountCostMultiplier;
    [Min(1), Tooltip("All-stat buff levels granted by each reinforcement tower to towers on affected map cells.")]
    public int reinforcementAuraBuffLevel = 1;
    [Range(1, 8), Tooltip("Map-cell radius affected by a reinforcement tower. Overlapping areas stack.")]
    public int reinforcementAuraRangeCells = 1;
    public List<RougeTowerLevelConfig> levels = new List<RougeTowerLevelConfig>();
}

[Serializable]
public sealed class RougeIceTowerSpecializationConfig
{
    [Header("基础 - 减速")]
    [Range(0f, 100f), Tooltip("冰霜塔基础攻击附加的减速百分比。")]
    public float slowPercent = 50f;
    [Min(0f), Tooltip("冰霜塔基础减速持续时间（秒）。")]
    public float slowDuration = 1.5f;

    [Header("A路线 - 主冻结")]
    [Min(0f), Tooltip("A路线：普通敌人的冻结时间（秒）。")]
    public float freezeNormalDuration = 1f;
    [Min(0f), Tooltip("A路线：精英敌人的冻结时间（秒）。")]
    public float freezeEliteDuration = 0.75f;
    [Min(0f), Tooltip("A路线：Boss 的冻结时间（秒）。")]
    public float freezeBossDuration = 0.5f;
    [Min(0f), Tooltip("A路线：Boss 解冻后的冻结免疫时间（秒）。")]
    public float freezeBossImmunityDuration = 0.5f;

    [Header("霜寒格")]
    [FormerlySerializedAs("frostSlowPercent")]
    [Range(0f, 100f), Tooltip("非冰塔位于霜寒格时，攻击附加的减速百分比。")]
    public float frostAttackSlowPercent = 20f;
    [FormerlySerializedAs("frostNormalFreezeBonus")]
    [Min(0f), Tooltip("冰塔位于霜寒格时，按当前升级路线增加的减速、冻结或脆弱持续时间（秒）；同时作为非冰塔附加减速的持续时间。")]
    public float frostDurationBonus = 0.5f;

    [Header("A2-b - 随机冰地刺")]
    [Min(1), Tooltip("A2-b：每次触发冰刺的最少随机地块数。")]
    public int iceSpikeMinCells = 3;
    [Min(1), Tooltip("A2-b：每次触发冰刺的最多随机地块数。")]
    public int iceSpikeMaxCells = 5;
    [Min(0.01f), Tooltip("A2-b：两次冰刺触发之间的最短时间（秒）。")]
    public float iceSpikeIntervalMin = 0.8f;
    [Min(0.01f), Tooltip("A2-b：两次冰刺触发之间的最长时间（秒）。")]
    public float iceSpikeIntervalMax = 1.2f;
    [Min(0f), Tooltip("A2-b：冰刺伤害相对冰霜塔本次伤害的倍率。")]
    public float iceSpikeDamageMultiplier = 0.5f;
    [Min(0f), Tooltip("A2-b：冰刺冻结时间相对主冻结效果的倍率。")]
    public float iceSpikeFreezeDurationMultiplier = 0.5f;
    [Header("B路线 - 脆弱基础")]
    [Min(0f), Tooltip("B路线：让敌人护甲效果减半的持续时间（秒）。")]
    public float vulnerabilityDuration = 2f;

    [Header("B2-a - 该塔施加的脆弱增伤")]
    [Min(0f), Tooltip("B2-a：该分支塔楼施加脆弱时的基础易伤；0.5 表示受到伤害 +50%。")]
    public float vulnerabilityDamageBonus = 0.5f;
    [Range(0f, 1f), Tooltip("B2-a：精英敌人易伤倍率。")]
    public float vulnerabilityEliteScale = 1f;
    [Range(0f, 1f), Tooltip("B2-a：Boss 增伤倍率；1 表示同样为 +100%。")]
    public float vulnerabilityBossScale = 1f;

    public void EnsureDefaults()
    {
        slowPercent = Mathf.Clamp(slowPercent, 0f, 100f);
        slowDuration = Mathf.Max(0f, slowDuration);
        freezeNormalDuration = Mathf.Max(0f, freezeNormalDuration);
        freezeEliteDuration = Mathf.Max(0f, freezeEliteDuration);
        freezeBossDuration = Mathf.Max(0f, freezeBossDuration);
        freezeBossImmunityDuration = Mathf.Max(0f, freezeBossImmunityDuration);
        frostAttackSlowPercent = Mathf.Clamp(frostAttackSlowPercent, 0f, 100f);
        frostDurationBonus = Mathf.Max(0f, frostDurationBonus);
        iceSpikeMinCells = Mathf.Max(1, iceSpikeMinCells);
        iceSpikeMaxCells = Mathf.Max(iceSpikeMinCells, iceSpikeMaxCells);
        iceSpikeIntervalMin = Mathf.Max(0.01f, iceSpikeIntervalMin);
        iceSpikeIntervalMax = Mathf.Max(iceSpikeIntervalMin, iceSpikeIntervalMax);
        iceSpikeDamageMultiplier = Mathf.Max(0f, iceSpikeDamageMultiplier);
        iceSpikeFreezeDurationMultiplier = Mathf.Max(0f, iceSpikeFreezeDurationMultiplier);
        vulnerabilityDuration = Mathf.Max(0f, vulnerabilityDuration);
        vulnerabilityDamageBonus = Mathf.Max(0f, vulnerabilityDamageBonus);
        vulnerabilityEliteScale = Mathf.Clamp01(vulnerabilityEliteScale);
        vulnerabilityBossScale = Mathf.Clamp01(vulnerabilityBossScale);
    }
}

[Serializable]
public sealed class RougeMachineGunSpecializationConfig
{
    [Header("A路线 - 暴击")]
    [Range(0f, 1f), Tooltip("A路线：每次攻击触发暴击的概率。")]
    public float criticalChance = 0.25f;
    [Range(0f, 1f), Tooltip("A1：强化后的暴击概率。")]
    public float upgradedCriticalChance = 0.5f;
    [Min(1f), Tooltip("暴击在护甲结算后造成的伤害倍率。")]
    public float criticalDamageMultiplier = 2f;
    [Min(0f), Tooltip("A2：暴击结算护甲时忽略的护甲点数。")]
    public float criticalArmorPenetration = 4f;

    [Header("B路线 - 破片")]
    [Range(0f, 1f), Tooltip("B路线：弹幕击杀敌人后生成破片的概率。")]
    public float fragmentTriggerChance = 0.5f;
    [Min(1), Tooltip("B路线：一次生成的破片数量。")]
    public int fragmentCount = 3;
    [Min(1), Tooltip("B1：强化后一次生成的破片数量。")]
    public int upgradedFragmentCount = 6;
    [Min(0f), Tooltip("B路线：普通破片相对原攻击的伤害倍率。")]
    public float fragmentDamageMultiplier = 0.3f;
    [Range(0f, 1f), Tooltip("B2：主弹命中敌人时嵌入一枚破片的概率。")]
    public float embeddedFragmentChance = 0.5f;
    [Min(0f), Tooltip("B2：每枚嵌入破片记录的原攻击伤害倍率。")]
    public float embeddedFragmentDamageMultiplier = 0.5f;
    [Min(0.01f), Tooltip("破片直线飞行的速度。")]
    public float fragmentSpeed = 70f;
    [Min(0.01f), Tooltip("破片命中路径上敌人的判定半径。")]
    public float fragmentHitRadius = 1.5f;

    public void EnsureDefaults()
    {
        criticalChance = Mathf.Clamp01(criticalChance);
        upgradedCriticalChance = Mathf.Clamp01(upgradedCriticalChance);
        criticalDamageMultiplier = Mathf.Max(1f, criticalDamageMultiplier);
        criticalArmorPenetration = Mathf.Max(0f, criticalArmorPenetration);
        fragmentTriggerChance = Mathf.Clamp01(fragmentTriggerChance);
        fragmentCount = Mathf.Max(1, fragmentCount);
        upgradedFragmentCount = Mathf.Max(fragmentCount, upgradedFragmentCount);
        fragmentDamageMultiplier = Mathf.Max(0f, fragmentDamageMultiplier);
        embeddedFragmentChance = Mathf.Clamp01(embeddedFragmentChance);
        embeddedFragmentDamageMultiplier = Mathf.Max(0f, embeddedFragmentDamageMultiplier);
        fragmentSpeed = Mathf.Max(0.01f, fragmentSpeed);
        fragmentHitRadius = Mathf.Max(0.01f, fragmentHitRadius);
    }
}

[Serializable]
public sealed class RougeCannonSpecializationConfig
{
    [Header("A路线 - 内圈爆破")]
    [Range(0.01f, 1f), Tooltip("A路线：内圈半径相对完整爆炸半径的比例。")]
    public float innerRadiusMultiplier = 1f / 3f;
    [Min(1f), Tooltip("A路线：内圈受到的总伤害倍率。")]
    public float innerDamageMultiplier = 2f;
    [Min(1f), Tooltip("A1：完整爆炸范围倍率。")]
    public float upgradedAoeRadiusMultiplier = 1.25f;
    [Range(0.01f, 1f), Tooltip("A1：强化后的内圈半径比例。")]
    public float upgradedInnerRadiusMultiplier = 0.5f;
    [Min(1f), Tooltip("A1：强化后的内圈总伤害倍率。")]
    public float upgradedInnerDamageMultiplier = 3f;

    [Header("A2 - 小炮弹")]
    [Range(0f, 1f), Tooltip("A2：主炮落地后生成小炮弹的概率。")]
    public float secondaryTriggerChance = 0.25f;
    [Min(1), Tooltip("A2：触发时生成的小炮弹数量。")]
    public int secondaryProjectileCount = 3;
    [Min(0f), Tooltip("A2：小炮弹伤害相对主炮伤害的倍率。")]
    public float secondaryDamageMultiplier = 0.25f;
    [Min(0.01f), Tooltip("A2：小爆炸范围相对主爆炸范围的倍率。")]
    public float secondaryRadiusMultiplier = 0.25f;
    [Min(0.01f), Tooltip("A2：小炮弹的飞行时间（秒）。")]
    public float secondaryFlightDuration = 1f;
    [Min(0f), Tooltip("A2：小炮弹水平位移相对主爆炸半径的倍率。")]
    public float secondaryTravelDistanceMultiplier = 0.25f;
    [Min(0f), Tooltip("A2：小炮弹抛物线高度相对主爆炸半径的倍率。")]
    public float secondaryArcHeightMultiplier = 0.35f;

    [Header("B路线 - 持续炮弹")]
    [Min(0f), Tooltip("B路线：炮弹落地伤害相对主炮伤害的倍率。")]
    public float persistentLandingDamageMultiplier = 0.25f;
    [Min(0.01f), Tooltip("B路线：两次持续爆炸之间的时间（秒）。")]
    public float persistentTickInterval = 0.5f;
    [Min(0f), Tooltip("B路线：每次持续爆炸的伤害倍率。")]
    public float persistentTickDamageMultiplier = 0.2f;
    [Min(1), Tooltip("B路线：落地后持续爆炸的次数。")]
    public int persistentTickCount = 5;
    [Min(0f), Tooltip("B1：每次爆炸施加的轻微击退强度。")]
    public float persistentKnockbackForce = 4f;
    [Min(0), Tooltip("B2：额外增加的持续爆炸次数。")]
    public int upgradedPersistentExtraTicks = 2;
    [Min(0f), Tooltip("B2：每次持续爆炸的伤害倍率。")]
    public float upgradedPersistentDamageMultiplier = 0.25f;

    public void EnsureDefaults()
    {
        innerRadiusMultiplier = Mathf.Clamp(innerRadiusMultiplier, 0.01f, 1f);
        innerDamageMultiplier = Mathf.Max(1f, innerDamageMultiplier);
        upgradedAoeRadiusMultiplier = Mathf.Max(1f, upgradedAoeRadiusMultiplier);
        upgradedInnerRadiusMultiplier = Mathf.Clamp(upgradedInnerRadiusMultiplier, 0.01f, 1f);
        upgradedInnerDamageMultiplier = Mathf.Max(1f, upgradedInnerDamageMultiplier);
        secondaryTriggerChance = Mathf.Clamp01(secondaryTriggerChance);
        secondaryProjectileCount = Mathf.Max(1, secondaryProjectileCount);
        secondaryDamageMultiplier = Mathf.Max(0f, secondaryDamageMultiplier);
        secondaryRadiusMultiplier = Mathf.Max(0.01f, secondaryRadiusMultiplier);
        secondaryFlightDuration = Mathf.Max(0.01f, secondaryFlightDuration);
        secondaryTravelDistanceMultiplier = Mathf.Max(0f, secondaryTravelDistanceMultiplier);
        secondaryArcHeightMultiplier = Mathf.Max(0f, secondaryArcHeightMultiplier);
        persistentLandingDamageMultiplier = Mathf.Max(0f, persistentLandingDamageMultiplier);
        persistentTickInterval = Mathf.Max(0.01f, persistentTickInterval);
        persistentTickDamageMultiplier = Mathf.Max(0f, persistentTickDamageMultiplier);
        persistentTickCount = Mathf.Max(1, persistentTickCount);
        persistentKnockbackForce = Mathf.Max(0f, persistentKnockbackForce);
        upgradedPersistentExtraTicks = Mathf.Max(0, upgradedPersistentExtraTicks);
        upgradedPersistentDamageMultiplier = Mathf.Max(0f,
            upgradedPersistentDamageMultiplier);
    }
}

[Serializable]
public sealed class RougeLaserTowerSpecializationConfig
{
    [Header("A - 破甲")]
    [Min(0.01f), Tooltip("普通敌人每次永久削减 1 点护甲所需的持续照射时间。")]
    public float armorBreakNormalDuration = 1f;
    [Min(0.01f), Tooltip("精英敌人每次永久削减 1 点护甲所需的持续照射时间。")]
    public float armorBreakEliteDuration = 2f;
    [Min(0.01f), Tooltip("Boss 每次永久削减 1 点护甲所需的持续照射时间。")]
    public float armorBreakBossDuration = 4f;
    [Range(0.01f, 1f), Tooltip("A1：破甲所需时间的倍率。")]
    public float acceleratedArmorBreakDurationMultiplier = 0.5f;

    [Header("B - 折射")]
    [Range(0.01f, 1f), Tooltip("折射搜索范围相对激光塔攻击范围的倍率。")]
    public float refractionRangeMultiplier = 0.25f;
    [Range(0f, 1f), Tooltip("B 路线基础折射的伤害倍率。")]
    public float refractionDamageMultiplier = 0.5f;
    [Min(1), Tooltip("B1：每条直连激光最多连续折射的次数。")]
    public int continuousRefractionCount = 3;

    [Header("B2 - 折射攻击")]
    [Min(0f), Tooltip("B2：基础伤害倍率。")]
    public float refractionAttackDamageMultiplier = 5f;
    [Min(0.01f), Tooltip("B2：基础攻击间隔。")]
    public float refractionAttackInterval = 1f;
    [Min(1), Tooltip("B2：最大折射敌人数相对弹幕数的倍率。")]
    public int refractionAttackTargetMultiplier = 2;
    [Range(0f, 1f), Tooltip("B2：每多命中一个敌人的伤害衰减。")]
    public float refractionAttackDamageFalloffPerTarget = 0.05f;
    [Range(0f, 1f), Tooltip("B2：伤害衰减上限。")]
    public float refractionAttackMaximumDamageFalloff = 0.5f;

    public void EnsureDefaults()
    {
        armorBreakNormalDuration = Mathf.Max(0.01f, armorBreakNormalDuration);
        armorBreakEliteDuration = Mathf.Max(0.01f, armorBreakEliteDuration);
        armorBreakBossDuration = Mathf.Max(0.01f, armorBreakBossDuration);
        acceleratedArmorBreakDurationMultiplier = Mathf.Clamp(
            acceleratedArmorBreakDurationMultiplier, 0.01f, 1f);
        refractionRangeMultiplier = Mathf.Clamp(refractionRangeMultiplier, 0.01f, 1f);
        refractionDamageMultiplier = Mathf.Clamp01(refractionDamageMultiplier);
        continuousRefractionCount = Mathf.Max(1, continuousRefractionCount);
        refractionAttackDamageMultiplier = Mathf.Max(0f, refractionAttackDamageMultiplier);
        refractionAttackInterval = Mathf.Max(0.01f, refractionAttackInterval);
        refractionAttackTargetMultiplier = Mathf.Max(1, refractionAttackTargetMultiplier);
        refractionAttackDamageFalloffPerTarget = Mathf.Clamp01(
            refractionAttackDamageFalloffPerTarget);
        refractionAttackMaximumDamageFalloff = Mathf.Clamp01(
            refractionAttackMaximumDamageFalloff);
    }
}

[Serializable]
public sealed class RougeFlameTowerSpecializationConfig
{
    [Header("A - 喷火器")]
    [Min(0f)] public float flamethrowerDamage = 10f;
    [Min(0.01f)] public float flamethrowerAttackInterval = 0.1f;
    [Min(0.1f)] public float flamethrowerRange = 15f;
    [Range(1f, 180f)] public float flamethrowerAngle = 30f;

    [Header("A1 - 旋转喷火器")]
    [Min(0f)] public float rotatingDamage = 20f;
    [Min(0.01f)] public float rotatingAttackInterval = 0.05f;
    [Min(0f)] public float rotatingDegreesPerSecond = 360f;

    [Header("A2 - 扇形 / 集中")]
    [Min(0f)] public float fanSpacingPaddingDegrees = 10f;
    [Min(0f)] public float focusedAnglePerProjectile = 15f;
    [Min(0f)] public float focusedDamageBonusPerProjectile = 0.5f;

    [Header("B - 燃烧")]
    [Min(0f)] public float burnDuration = 3f;
    [Min(0.01f)] public float burnTickInterval = 0.75f;
    [Min(0f)] public float burnDamageMultiplier = 0.25f;
    [Range(0f, 1f)] public float attackSpeedBuffEffectiveness = 0.5f;

    [Header("B1 - 叠层燃烧")]
    [Min(1)] public int maximumBurnStacks = 10;
    [Min(0f)] public float damageBonusPerStack = 0.1f;
    [Min(0f)] public float burnSpeedBonus = 0.25f;

    [Header("B2 - 爆燃")]
    [Min(0f)] public float conflagrationDamageMultiplier = 5f;

    public void EnsureDefaults()
    {
        flamethrowerDamage = Mathf.Max(0f, flamethrowerDamage);
        flamethrowerAttackInterval = Mathf.Max(0.01f, flamethrowerAttackInterval);
        flamethrowerRange = Mathf.Max(0.1f, flamethrowerRange);
        flamethrowerAngle = Mathf.Clamp(flamethrowerAngle, 1f, 180f);
        rotatingDamage = Mathf.Max(0f, rotatingDamage);
        rotatingAttackInterval = Mathf.Max(0.01f, rotatingAttackInterval);
        rotatingDegreesPerSecond = Mathf.Max(0f, rotatingDegreesPerSecond);
        fanSpacingPaddingDegrees = Mathf.Max(0f, fanSpacingPaddingDegrees);
        focusedAnglePerProjectile = Mathf.Max(0f, focusedAnglePerProjectile);
        focusedDamageBonusPerProjectile = Mathf.Max(0f,
            focusedDamageBonusPerProjectile);
        burnDuration = Mathf.Max(0f, burnDuration);
        burnTickInterval = Mathf.Max(0.01f, burnTickInterval);
        burnDamageMultiplier = Mathf.Max(0f, burnDamageMultiplier);
        attackSpeedBuffEffectiveness = Mathf.Clamp01(attackSpeedBuffEffectiveness);
        maximumBurnStacks = Mathf.Max(1, maximumBurnStacks);
        damageBonusPerStack = Mathf.Max(0f, damageBonusPerStack);
        burnSpeedBonus = Mathf.Max(0f, burnSpeedBonus);
        conflagrationDamageMultiplier = Mathf.Max(0f, conflagrationDamageMultiplier);
    }
}

[Serializable]
public sealed class RougeTowerBalanceConfig
{
    [Range(0f, 1f)] public float sellRefundMultiplier = 0.25f;
    public RougeIceTowerSpecializationConfig iceTowerSpecialization =
        new RougeIceTowerSpecializationConfig();
    public RougeMachineGunSpecializationConfig machineGunSpecialization =
        new RougeMachineGunSpecializationConfig();
    public RougeCannonSpecializationConfig cannonSpecialization =
        new RougeCannonSpecializationConfig();
    public RougeLaserTowerSpecializationConfig laserTowerSpecialization =
        new RougeLaserTowerSpecializationConfig();
    public RougeFlameTowerSpecializationConfig flameTowerSpecialization =
        new RougeFlameTowerSpecializationConfig();
    public List<RougeTowerTypeConfig> towers = new List<RougeTowerTypeConfig>();

    public void EnsureDefaults()
    {
        iceTowerSpecialization ??= new RougeIceTowerSpecializationConfig();
        iceTowerSpecialization.EnsureDefaults();
        machineGunSpecialization ??= new RougeMachineGunSpecializationConfig();
        machineGunSpecialization.EnsureDefaults();
        cannonSpecialization ??= new RougeCannonSpecializationConfig();
        cannonSpecialization.EnsureDefaults();
        laserTowerSpecialization ??= new RougeLaserTowerSpecializationConfig();
        laserTowerSpecialization.EnsureDefaults();
        flameTowerSpecialization ??= new RougeFlameTowerSpecializationConfig();
        flameTowerSpecialization.EnsureDefaults();
        foreach (RougeTowerType type in Enum.GetValues(typeof(RougeTowerType)))
        {
            RougeTowerTypeConfig config = Find(type);
            if (config == null)
            {
                config = CreateDefault(type);
                towers.Add(config);
            }
            config.footprintSize = 1;
            config.footprintWidth = 1;
            config.footprintHeight = 1;
            config.specialTowerCountCostMultiplier = Mathf.Max(0f,
                config.specialTowerCountCostMultiplier);
            config.reinforcementAuraBuffLevel = Mathf.Max(1,
                config.reinforcementAuraBuffLevel);
            config.reinforcementAuraRangeCells = Mathf.Clamp(
                config.reinforcementAuraRangeCells <= 0 ? 1 : config.reinforcementAuraRangeCells,
                1, 8);
            while (config.levels.Count < TowerDefenseVisuals.MaxTowerLevel)
            {
                config.levels.Add(CreateDefaultLevel(type, config.levels.Count));
            }
            if (config.levels.Count > TowerDefenseVisuals.MaxTowerLevel)
            {
                config.levels.RemoveRange(TowerDefenseVisuals.MaxTowerLevel,
                    config.levels.Count - TowerDefenseVisuals.MaxTowerLevel);
            }
            if (HasNoConfiguredLevelGoldCosts(config)) ApplyLegacyLevelGoldCosts(config);
            config.purchaseCost = Mathf.Max(0, config.levels[0].goldCost);
        }
    }

    public void MigrateLegacyLevelGoldCosts()
    {
        if (towers == null) return;
        for (int i = 0; i < towers.Count; i++)
        {
            RougeTowerTypeConfig config = towers[i];
            if (config == null || !HasNoConfiguredLevelGoldCosts(config)) continue;
            ApplyLegacyLevelGoldCosts(config);
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
            case RougeTowerType.ChargeTower:
                config.placementRadius = 2.8f;
                config.purchaseCost = 4000;
                config.specialTowerCountCostMultiplier = 0.25f;
                break;
            case RougeTowerType.ReinforcementTower:
                config.placementRadius = 3.1f;
                config.purchaseCost = 6000;
                config.specialTowerCountCostMultiplier = 0.5f;
                config.reinforcementAuraBuffLevel = 1;
                config.reinforcementAuraRangeCells = 1;
                break;
            default: config.placementRadius = 2.8f; config.purchaseCost = 1400; break;
        }
        for (int i = 0; i < TowerDefenseVisuals.MaxTowerLevel; i++)
        {
            config.levels.Add(CreateDefaultLevel(type, i));
        }
        ApplyLegacyLevelGoldCosts(config);
        return config;
    }

    private static bool HasNoConfiguredLevelGoldCosts(RougeTowerTypeConfig config)
    {
        if (config?.levels == null || config.levels.Count == 0) return true;
        for (int i = 0; i < config.levels.Count; i++)
        {
            if (config.levels[i] != null && config.levels[i].goldCost != 0) return false;
        }
        return true;
    }

    private static void ApplyLegacyLevelGoldCosts(RougeTowerTypeConfig config)
    {
        if (config?.levels == null) return;
        int baseCost = Mathf.Max(0, config.purchaseCost);
        for (int i = 0; i < config.levels.Count; i++)
        {
            if (config.levels[i] == null) continue;
            config.levels[i].goldCost = baseCost * (1 << Mathf.Min(i, 30));
        }
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
            effectPercent = type == RougeTowerType.Ice ? 50f : fallback.EffectPercent,
            effectDuration = type == RougeTowerType.Ice ? 2f : fallback.EffectDuration,
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
    [Min(0)] public int killGold = 1;
    [Min(0)] public int eliteKillGold = 20;
    [Min(0.01f)] public float baseHealth = 10f;
    [Range(RougeArmorRules.MinimumEnemyArmor, RougeArmorRules.MaximumEnemyArmor), Tooltip("Armor points. Each point removes 1 damage and then reduces the remainder by 5%; final damage is at least 1.")]
    public float armor = 1f;
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
        armor = Mathf.Clamp(armor, RougeArmorRules.MinimumEnemyArmor,
            RougeArmorRules.MaximumEnemyArmor);
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

    [HideInInspector, Min(0)] public int normalKillGold = 1;
    [HideInInspector, Min(0)] public int eliteKillGold = 20;
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

    public void MigrateLegacyKillGold()
    {
        if (enemyTypes == null) return;
        for (int i = 0; i < enemyTypes.Count; i++)
        {
            if (enemyTypes[i] == null) continue;
            enemyTypes[i].killGold = Mathf.Max(0, normalKillGold);
            enemyTypes[i].eliteKillGold = Mathf.Max(0, eliteKillGold);
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
    [Min(1f)] public float spawnTimeSeconds = 720f;
    [Min(1f)] public float targetArrivalTimeSeconds = 1080f;
    [Min(1f), Tooltip("Desired travel time from Boss spawn to the main tower. Level schedules control the actual spawn minute.")]
    public float targetTravelTimeSeconds = 360f;
    [Min(1f)] public float maxHealth = 800000f;
    [Range(RougeArmorRules.MinimumEnemyArmor, RougeArmorRules.MaximumEnemyArmor), Tooltip("Armor points. Each point removes 1 damage and then reduces the remainder by 5%; final damage is at least 1.")]
    public float armor = 5f;
    [Min(0.1f)] public float moveSpeed = 3.5f; // Fallback when no valid route distance is available.
    [Range(0f, 95f)] public float maximumSlowPercent = 20f;
    [Min(0.5f)] public float radius = 5f;
    [Min(0.1f)] public float navigationRadius = 1.25f;
    public Vector3 fallbackSpawnPosition = new Vector3(0f, 0.25f, 135f);
    [Min(0f)] public float interferenceRadius = 20f;
    [Tooltip("Tower attack-speed Buff level applied by interference. Raw levels stack without a limit; the effect is capped to -3..+5.")]
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
        if (spawnTimeSeconds <= 0f) spawnTimeSeconds = 720f;
        if (targetArrivalTimeSeconds <= spawnTimeSeconds)
            targetArrivalTimeSeconds = spawnTimeSeconds + 360f;
        if (targetTravelTimeSeconds <= 0f)
            targetTravelTimeSeconds = Mathf.Max(30f, targetArrivalTimeSeconds - spawnTimeSeconds);
        maxHealth = Mathf.Max(1f, maxHealth);
        armor = Mathf.Clamp(armor, RougeArmorRules.MinimumEnemyArmor,
            RougeArmorRules.MaximumEnemyArmor);
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
    [Tooltip("Raw damage Buff levels. Effective level is capped to -3..+5; each level is 20%.")]
    public int damageBuffLevel = 2;
    [Tooltip("Raw range Buff levels. Effective level is capped to -3..+5; each level is 20%.")]
    public int rangeBuffLevel;
    [Tooltip("Raw attack-speed Buff levels. Effective level is capped to -3..+5; each level is 20%.")]
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
    public int version = 14;
    public RougeTowerBalanceConfig towerBalance = new RougeTowerBalanceConfig();
    public RougeEnemyBalanceConfig enemyBalance = new RougeEnemyBalanceConfig();
    public List<RougeBossBalanceConfig> bossBalances = new List<RougeBossBalanceConfig>();
    [Tooltip("Legacy single-Boss field retained so older JSON files migrate without data loss.")]
    public RougeBossBalanceConfig bossBalance = new RougeBossBalanceConfig();
    public RougeTacticalSkillBalanceConfig tacticalSkillBalance = new RougeTacticalSkillBalanceConfig();

    public void EnsureDefaults()
    {
        int loadedVersion = version;
        towerBalance ??= new RougeTowerBalanceConfig();
        enemyBalance ??= new RougeEnemyBalanceConfig();
        bossBalance ??= new RougeBossBalanceConfig();
        bossBalances ??= new List<RougeBossBalanceConfig>();
        if (bossBalances.Count == 0) bossBalances.Add(bossBalance);
        if (loadedVersion < 6)
        {
            if (towerBalance.towers != null)
            {
                for (int i = 0; i < towerBalance.towers.Count; i++)
                {
                    RougeTowerTypeConfig tower = towerBalance.towers[i];
                    if (tower != null) tower.reinforcementAuraRangeCells = 1;
                }
            }
            if (enemyBalance.enemyTypes != null)
            {
                for (int i = 0; i < enemyBalance.enemyTypes.Count; i++)
                {
                    RougeEnemyArchetypeConfig enemy = enemyBalance.enemyTypes[i];
                    if (enemy != null) enemy.armor = 1f;
                }
            }
            for (int i = 0; i < bossBalances.Count; i++)
            {
                if (bossBalances[i] != null) bossBalances[i].armor = 5f;
            }
            bossBalance.armor = 5f;
        }
        if (loadedVersion < 8)
        {
            towerBalance.iceTowerSpecialization ??=
                new RougeIceTowerSpecializationConfig();
            towerBalance.iceTowerSpecialization.vulnerabilityDamageBonus = 0.5f;
            towerBalance.iceTowerSpecialization.vulnerabilityEliteScale = 1f;
            towerBalance.iceTowerSpecialization.vulnerabilityBossScale = 1f;
        }
        if (loadedVersion < 9)
        {
            towerBalance.iceTowerSpecialization ??=
                new RougeIceTowerSpecializationConfig();
            towerBalance.iceTowerSpecialization.frostAttackSlowPercent = 20f;
            towerBalance.iceTowerSpecialization.frostDurationBonus = 0.5f;
        }
        if (loadedVersion < 10)
        {
            towerBalance.machineGunSpecialization =
                new RougeMachineGunSpecializationConfig
                {
                    criticalChance = 0.25f,
                    upgradedCriticalChance = 0.5f,
                    criticalDamageMultiplier = 2f,
                    criticalArmorPenetration = 4f,
                    fragmentTriggerChance = 0.5f,
                    fragmentCount = 3,
                    upgradedFragmentCount = 6,
                    fragmentDamageMultiplier = 0.3f,
                    embeddedFragmentChance = 0.5f,
                    embeddedFragmentDamageMultiplier = 0.5f,
                    fragmentSpeed = 70f,
                    fragmentHitRadius = 1.5f
                };
        }
        if (loadedVersion < 11)
        {
            towerBalance.cannonSpecialization = new RougeCannonSpecializationConfig
            {
                innerRadiusMultiplier = 1f / 3f,
                innerDamageMultiplier = 2f,
                upgradedAoeRadiusMultiplier = 1.25f,
                upgradedInnerRadiusMultiplier = 0.5f,
                upgradedInnerDamageMultiplier = 3f,
                secondaryTriggerChance = 0.25f,
                secondaryProjectileCount = 3,
                secondaryDamageMultiplier = 0.25f,
                secondaryRadiusMultiplier = 0.25f,
                secondaryFlightDuration = 1f,
                secondaryTravelDistanceMultiplier = 0.25f,
                secondaryArcHeightMultiplier = 0.35f,
                persistentLandingDamageMultiplier = 0.25f,
                persistentTickInterval = 0.5f,
                persistentTickDamageMultiplier = 0.2f,
                persistentTickCount = 5,
                persistentKnockbackForce = 4f,
                upgradedPersistentExtraTicks = 2,
                upgradedPersistentDamageMultiplier = 0.25f
            };
        }
        if (loadedVersion < 12)
        {
            towerBalance.iceTowerSpecialization ??=
                new RougeIceTowerSpecializationConfig();
            towerBalance.iceTowerSpecialization.frostAttackSlowPercent = 20f;
            towerBalance.iceTowerSpecialization.frostDurationBonus = 0.5f;
        }
        if (loadedVersion < 13)
            towerBalance.laserTowerSpecialization =
                new RougeLaserTowerSpecializationConfig();
        if (loadedVersion < 14)
            towerBalance.flameTowerSpecialization =
                new RougeFlameTowerSpecializationConfig();
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
        if (loadedVersion < 5)
        {
            towerBalance.MigrateLegacyLevelGoldCosts();
            enemyBalance.MigrateLegacyKillGold();
        }
        towerBalance.EnsureDefaults();
        enemyBalance.EnsureDefaults();
        bossBalance.EnsureDefaults();
        tacticalSkillBalance.EnsureDefaults();
        version = Mathf.Max(version, 14);
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
            version = 14,
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
