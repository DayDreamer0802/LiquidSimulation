using System.Collections.Generic;
using UnityEngine;

// Keep the first four values stable so already-serialized towers retain their type.
public enum RougeTowerType
{
    [InspectorName("冰塔")] Ice,
    [InspectorName("机枪塔")] MachineGun,
    [InspectorName("加农炮")] Cannon,
    [InspectorName("火焰塔")] Flame,
    [InspectorName("激光塔")] Laser,
    [InspectorName("穿透激光塔")] PiercingLaser,
    [InspectorName("水晶塔")] OrbitSphere,
    [InspectorName("火箭齐射塔")] RocketBarrage,
    [InspectorName("充能塔")] ChargeTower = 8,
    [InspectorName("强化塔")] ReinforcementTower = 9
}

public enum RougeTowerTargetPriority
{
    // Zero is intentional: old scene towers that did not serialize this field
    // automatically migrate to the desired default mode.
    [InspectorName("普通模式（终点最近）")]
    NearestToGoal = 0,
    [InspectorName("集中模式（Boss 优先）")]
    BossFirst = 1
}

public enum RougeIceTowerBranch
{
    None = 0,
    [InspectorName("A - 冻结路线")] Freeze = 1,
    [InspectorName("B - 脆弱路线")] Vulnerability = 2
}

public enum RougeIceTowerAugment
{
    None = 0,
    [InspectorName("A2-b - 随机冰地刺")] IceSpikes = 1,
    [InspectorName("A2-a - 相邻格永久变为霜寒格")] PermanentFrostTiles = 2,
    [InspectorName("B2-a - 脆弱单位受到更多伤害")] VulnerabilityDamage = 3,
    [InspectorName("B2-b - 攻击脆弱单位视为 +4 穿甲")] VulnerabilityArmorPenetration = 4
}

public static class RougeArmorRules
{
    public const float MinimumEnemyArmor = -20f;
    public const float MaximumEnemyArmor = 15f;
    public const float DamageReductionPerArmorPoint = 0.05f;
    public const float VulnerableArmorPenetration = 4f;
}

public enum RougeMachineGunBranch
{
    None = 0,
    [InspectorName("A - 暴击路线")] Critical = 1,
    [InspectorName("B - 破片路线")] Fragments = 2
}

public enum RougeMachineGunAugment
{
    None = 0,
    [InspectorName("A1 - 暴击率提升至 50%")] CriticalChance = 1,
    [InspectorName("A2 - 暴击获得 4 穿甲")] CriticalArmorPenetration = 2,
    [InspectorName("B1 - 破片提升至 6 枚")] FragmentCount = 3,
    [InspectorName("B2 - 破片有 50% 概率嵌入")] EmbeddedFragments = 4
}

public enum RougeLaserTowerBranch
{
    None = 0,
    [InspectorName("A - 破甲")] ArmorBreak = 1,
    [InspectorName("B - 折射")] Refraction = 2
}

public enum RougeLaserTowerAugment
{
    None = 0,
    [InspectorName("A1 - 加速穿甲")] AcceleratedArmorBreak = 1,
    [InspectorName("A2 - 强力集中")] StrongFocus = 2,
    [InspectorName("B1 - 连续折射")] ContinuousRefraction = 3,
    [InspectorName("B2 - 折射攻击")] RefractionAttack = 4
}

public enum RougeCannonBranch
{
    None = 0,
    [InspectorName("A - 内圈爆破")] InnerBlast = 1,
    [InspectorName("B - 持续炮弹")] PersistentShell = 2
}

public enum RougeCannonAugment
{
    None = 0,
    [InspectorName("A1 - 扩大爆炸与强化内圈")] InnerBlastArea = 1,
    [InspectorName("A2 - 追加 3 枚小炮弹")] SecondaryBombardment = 2,
    [InspectorName("B1 - 持续爆炸轻微击退")] PersistentKnockback = 3,
    [InspectorName("B2 - 额外触发 2 次并提高伤害")] PersistentExtraTicks = 4
}

public enum RougeFlameTowerBranch
{
    None = 0,
    [InspectorName("A - 喷火器")] Flamethrower = 1,
    [InspectorName("B - 燃烧")] Burning = 2
}

public enum RougeFlameTowerAugment
{
    None = 0,
    [InspectorName("A1 - 旋转喷火器")] RotatingFlamethrower = 1,
    [InspectorName("A2 - 扇形喷火器")] FanFlamethrower = 2,
    [InspectorName("B1 - 叠层燃烧")] StackingBurn = 3,
    [InspectorName("B2 - 爆燃")] Conflagration = 4
}

public readonly struct RougeTowerStats
{
    public readonly float Damage;
    public readonly float AttackInterval;
    public readonly float AttackRadius;
    public readonly int TargetCount;
    public readonly int ProjectileCount;
    public readonly float AoeRadius;
    public readonly float EffectPercent;
    public readonly float EffectDuration;
    public readonly float TickInterval;
    public readonly float OrbitSphereRadius;
    public readonly float OrbitRadialSpeed;
    public readonly float OrbitAngularSpeed;
    public readonly float OrbitOuterHoldDuration;
    public readonly float ProjectileInterval;
    public readonly float ProjectileFlightDuration;
    public readonly float BrownianStrength;

    public RougeTowerStats(float damage, float attackInterval, float attackRadius,
        int targetCount = 1, int projectileCount = 1, float aoeRadius = 0f,
        float effectPercent = 0f, float effectDuration = 0f, float tickInterval = 0f,
        float orbitSphereRadius = 0f, float orbitRadialSpeed = 0f, float orbitAngularSpeed = 0f,
        float orbitOuterHoldDuration = 0f, float projectileInterval = 0f,
        float projectileFlightDuration = 0f, float brownianStrength = 0f)
    {
        Damage = damage;
        AttackInterval = attackInterval;
        AttackRadius = attackRadius;
        TargetCount = targetCount;
        ProjectileCount = projectileCount;
        AoeRadius = aoeRadius;
        EffectPercent = effectPercent;
        EffectDuration = effectDuration;
        TickInterval = tickInterval;
        OrbitSphereRadius = orbitSphereRadius;
        OrbitRadialSpeed = orbitRadialSpeed;
        OrbitAngularSpeed = orbitAngularSpeed;
        OrbitOuterHoldDuration = orbitOuterHoldDuration;
        ProjectileInterval = projectileInterval;
        ProjectileFlightDuration = projectileFlightDuration;
        BrownianStrength = brownianStrength;
    }
}

internal static class TowerDefenseVisuals
{
    public const int MaxTowerLevel = 3;
    public const int StandardTowerTypeCount = 8;
    public const int TowerTypeCount = 10;
    private static Material s_lineMaterial;
    private static Material s_laserConnectionMaterial;
    private static Material s_crystalLaserMaterial;
    private static Material s_towerSelectedIndicatorMaterial;
    private static Material s_towerUpgradeIndicatorMaterial;
    private static RougeTowerBalanceConfig s_runtimeBalance;
    private static float s_runtimeGoldCostMultiplier = 1f;
    private static float s_runtimeDamageMultiplier = 1f;
    private static float s_runtimeAttackSpeedMultiplier = 1f;

    public static void SetRuntimeBalance(RougeTowerBalanceConfig balance)
    {
        s_runtimeBalance = balance;
        s_runtimeBalance?.EnsureDefaults();
    }

    public static void SetRuntimeLevelModifiers(float goldCostMultiplier, float damageMultiplier,
        float attackSpeedMultiplier)
    {
        s_runtimeGoldCostMultiplier = Mathf.Max(0f, goldCostMultiplier);
        s_runtimeDamageMultiplier = Mathf.Max(0f, damageMultiplier);
        s_runtimeAttackSpeedMultiplier = Mathf.Max(0.01f, attackSpeedMultiplier);
    }

    public static string GetTowerName(RougeTowerType type)
    {
        switch (type)
        {
            case RougeTowerType.Ice: return "冰霜塔";
            case RougeTowerType.MachineGun: return "机枪塔";
            case RougeTowerType.Cannon: return "加农炮";
            case RougeTowerType.Flame: return "火焰塔";
            case RougeTowerType.Laser: return "激光塔";
            case RougeTowerType.PiercingLaser: return "穿透激光";
            case RougeTowerType.OrbitSphere: return "水晶塔";
            case RougeTowerType.RocketBarrage: return "火箭齐射";
            case RougeTowerType.ChargeTower: return "充能塔";
            case RougeTowerType.ReinforcementTower: return "强化塔";
            default: return "未知塔楼";
        }
    }

    public static bool IsSpecialTowerType(RougeTowerType type)
    {
        return type == RougeTowerType.ChargeTower ||
               type == RougeTowerType.ReinforcementTower;
    }

    public static Color GetTowerColor(RougeTowerType type)
    {
        switch (type)
        {
            case RougeTowerType.Ice: return new Color(0.1f, 0.72f, 1f);
            case RougeTowerType.MachineGun: return new Color(0.95f, 0.88f, 0.2f);
            case RougeTowerType.Cannon: return new Color(1f, 0.35f, 0.12f);
            case RougeTowerType.Flame: return new Color(1f, 0.12f, 0.08f);
            case RougeTowerType.Laser: return new Color(0.15f, 1f, 0.58f);
            case RougeTowerType.PiercingLaser: return new Color(0.95f, 0.12f, 1f);
            case RougeTowerType.OrbitSphere: return new Color(0.35f, 0.62f, 1f);
            case RougeTowerType.RocketBarrage: return new Color(0.42f, 0.5f, 0.16f);
            case RougeTowerType.ChargeTower: return new Color(0.18f, 0.9f, 1f);
            case RougeTowerType.ReinforcementTower: return new Color(0.9f, 0.24f, 1f);
            default: return Color.white;
        }
    }

    public static void GetBaseStats(RougeTowerType type, out float damage, out float interval,
        out float range, out float radius, out int cost)
    {
        RougeTowerStats stats = GetStats(type, 1);
        damage = stats.Damage;
        interval = stats.AttackInterval;
        range = stats.AttackRadius;
        RougeTowerTypeConfig configured = s_runtimeBalance?.Find(type);
        if (configured != null)
        {
            radius = Mathf.Max(0.1f, configured.placementRadius);
            cost = GetLevelGoldCost(type, 1);
            return;
        }
        switch (type)
        {
            case RougeTowerType.MachineGun: radius = 2f; cost = 400; break;
            case RougeTowerType.Ice: radius = 2.2f; cost = 625; break;
            case RougeTowerType.Cannon: radius = 2.7f; cost = 750; break;
            case RougeTowerType.Flame: radius = 2.4f; cost = 625; break;
            case RougeTowerType.Laser: radius = 2.3f; cost = 750; break;
            case RougeTowerType.PiercingLaser: radius = 2.8f; cost = 1000; break;
            case RougeTowerType.OrbitSphere: radius = 2.5f; cost = 900; break;
            case RougeTowerType.RocketBarrage: radius = 2.8f; cost = 1400; break;
            case RougeTowerType.ChargeTower: radius = 2.8f; cost = 4000; break;
            case RougeTowerType.ReinforcementTower: radius = 3.1f; cost = 6000; break;
            default: radius = 2f; cost = 0; break;
        }
        cost = ScaleGoldCost(cost);
    }

    public static int GetLevelGoldCost(RougeTowerType type, int requestedLevel)
    {
        int levelIndex = Mathf.Clamp(requestedLevel, 1, MaxTowerLevel) - 1;
        RougeTowerTypeConfig configured = s_runtimeBalance?.Find(type);
        if (configured?.levels != null && levelIndex < configured.levels.Count &&
            configured.levels[levelIndex] != null)
        {
            return ScaleGoldCost(configured.levels[levelIndex].goldCost);
        }

        int baseCost;
        switch (type)
        {
            case RougeTowerType.MachineGun: baseCost = 400; break;
            case RougeTowerType.Ice: baseCost = 625; break;
            case RougeTowerType.Cannon: baseCost = 750; break;
            case RougeTowerType.Flame: baseCost = 625; break;
            case RougeTowerType.Laser: baseCost = 750; break;
            case RougeTowerType.PiercingLaser: baseCost = 1000; break;
            case RougeTowerType.OrbitSphere: baseCost = 900; break;
            case RougeTowerType.RocketBarrage: baseCost = 1400; break;
            case RougeTowerType.ChargeTower: baseCost = 4000; break;
            case RougeTowerType.ReinforcementTower: baseCost = 6000; break;
            default: baseCost = 0; break;
        }
        return ScaleGoldCost(baseCost * (1 << levelIndex));
    }

    public static Vector2Int GetFootprintSize(RougeTowerType type)
    {
        return Vector2Int.one;
    }

    public static int GetReinforcementAuraBuffLevel()
    {
        RougeTowerTypeConfig configured = s_runtimeBalance?.Find(
            RougeTowerType.ReinforcementTower);
        return Mathf.Max(1, configured?.reinforcementAuraBuffLevel ?? 1);
    }

    public static int GetReinforcementAuraRangeCells()
    {
        RougeTowerTypeConfig configured = s_runtimeBalance?.Find(
            RougeTowerType.ReinforcementTower);
        return Mathf.Clamp(configured?.reinforcementAuraRangeCells ?? 1, 1, 8);
    }

    public static RougeIceTowerSpecializationConfig GetIceSpecializationConfig()
    {
        RougeIceTowerSpecializationConfig config = s_runtimeBalance?.iceTowerSpecialization;
        if (config == null)
        {
            config = new RougeIceTowerSpecializationConfig();
            if (s_runtimeBalance != null) s_runtimeBalance.iceTowerSpecialization = config;
        }
        config.EnsureDefaults();
        return config;
    }

    public static RougeMachineGunSpecializationConfig GetMachineGunSpecializationConfig()
    {
        RougeMachineGunSpecializationConfig config =
            s_runtimeBalance?.machineGunSpecialization;
        if (config == null)
        {
            config = new RougeMachineGunSpecializationConfig();
            if (s_runtimeBalance != null) s_runtimeBalance.machineGunSpecialization = config;
        }
        config.EnsureDefaults();
        return config;
    }

    public static RougeCannonSpecializationConfig GetCannonSpecializationConfig()
    {
        RougeCannonSpecializationConfig config = s_runtimeBalance?.cannonSpecialization;
        if (config == null)
        {
            config = new RougeCannonSpecializationConfig();
            if (s_runtimeBalance != null) s_runtimeBalance.cannonSpecialization = config;
        }
        config.EnsureDefaults();
        return config;
    }

    public static RougeLaserTowerSpecializationConfig GetLaserSpecializationConfig()
    {
        RougeLaserTowerSpecializationConfig config = s_runtimeBalance?.laserTowerSpecialization;
        if (config == null)
        {
            config = new RougeLaserTowerSpecializationConfig();
            if (s_runtimeBalance != null) s_runtimeBalance.laserTowerSpecialization = config;
        }
        config.EnsureDefaults();
        return config;
    }

    public static RougeFlameTowerSpecializationConfig GetFlameSpecializationConfig()
    {
        RougeFlameTowerSpecializationConfig config = s_runtimeBalance?.flameTowerSpecialization;
        if (config == null)
        {
            config = new RougeFlameTowerSpecializationConfig();
            if (s_runtimeBalance != null) s_runtimeBalance.flameTowerSpecialization = config;
        }
        config.EnsureDefaults();
        return config;
    }

    public static float ApplyRuntimeTowerDamage(float damage)
    {
        return Mathf.Max(0f, damage) * s_runtimeDamageMultiplier;
    }

    public static float ApplyRuntimeTowerAttackInterval(float interval)
    {
        return Mathf.Max(0.001f, interval) /
               Mathf.Max(0.01f, s_runtimeAttackSpeedMultiplier);
    }

    public static RougeTowerStats GetStats(RougeTowerType type, int requestedLevel)
    {
        RougeTowerTypeConfig configured = s_runtimeBalance?.Find(type);
        int levelIndex = Mathf.Clamp(requestedLevel, 1, MaxTowerLevel) - 1;
        if (configured != null && configured.levels != null && levelIndex < configured.levels.Count &&
            configured.levels[levelIndex] != null)
        {
            return ApplyRuntimeLevelModifiers(configured.levels[levelIndex].ToStats());
        }
        return ApplyRuntimeLevelModifiers(GetFallbackStats(type, requestedLevel));
    }

    private static int ScaleGoldCost(int baseCost)
    {
        return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, baseCost) * s_runtimeGoldCostMultiplier));
    }

    private static RougeTowerStats ApplyRuntimeLevelModifiers(RougeTowerStats stats)
    {
        float attackSpeed = Mathf.Max(0.01f, s_runtimeAttackSpeedMultiplier);
        return new RougeTowerStats(
            stats.Damage * s_runtimeDamageMultiplier,
            stats.AttackInterval / attackSpeed,
            stats.AttackRadius,
            stats.TargetCount,
            stats.ProjectileCount,
            stats.AoeRadius,
            stats.EffectPercent,
            stats.EffectDuration,
            stats.TickInterval > 0f ? stats.TickInterval / attackSpeed : 0f,
            stats.OrbitSphereRadius,
            stats.OrbitRadialSpeed * attackSpeed,
            stats.OrbitAngularSpeed * attackSpeed,
            stats.OrbitOuterHoldDuration / attackSpeed,
            stats.ProjectileInterval / attackSpeed,
            stats.ProjectileFlightDuration,
            stats.BrownianStrength);
    }

    public static RougeTowerStats GetFallbackStats(RougeTowerType type, int requestedLevel)
    {
        int i = Mathf.Clamp(requestedLevel, 1, MaxTowerLevel) - 1;
        switch (type)
        {
            case RougeTowerType.MachineGun:
                return new RougeTowerStats(Pick(i, 3f, 6f, 9f, 12f, 15f), 3f / 60f,
                    Pick(i, 18f, 22f, 26f, 30f, 36f), Pick(i, 3, 6, 9, 12, 15));
            case RougeTowerType.Ice:
                return new RougeTowerStats(Pick(i, 10f, 18f, 26f, 32f, 40f),
                    Pick(i, 5f, 4.5f, 4f, 3.5f, 3f), Pick(i, 12f, 14f, 16f, 18f, 20f),
                    effectPercent: 50f, effectDuration: 2f);
            case RougeTowerType.Cannon:
                return new RougeTowerStats(Pick(i, 50f, 75f, 100f, 125f, 200f),
                    Pick(i, 3f, 2.7f, 2.4f, 2.2f, 2f), Pick(i, 22f, 26f, 30f, 34f, 40f),
                    projectileCount: Pick(i, 1, 1, 1, 1, 2),
                    aoeRadius: Pick(i, 6f, 8f, 10f, 12f, 15f));
            case RougeTowerType.Flame:
                return new RougeTowerStats(Pick(i, 10f, 12f, 14f, 16f, 20f), 5f,
                    Pick(i, 10f, 12f, 14f, 16f, 18f),
                    aoeRadius: Pick(i, 5f, 6f, 7f, 8f, 10f),
                    effectDuration: Pick(i, 4f, 4f, 4f, 4f, 6f),
                    tickInterval: Pick(i, 0.5f, 0.5f, 0.5f, 0.5f, 0.25f));
            case RougeTowerType.Laser:
                // Damage is stored as damage/second: the supplied per-frame value uses a 60 FPS baseline.
                return new RougeTowerStats(Pick(i, 1f, 1f, 1f, 1f, 2f) * 60f, 1f / 60f,
                    Pick(i, 15f, 17f, 19f, 21f, 24f), Pick(i, 5, 10, 15, 20, 30));
            case RougeTowerType.PiercingLaser:
                return new RougeTowerStats(Pick(i, 200f, 300f, 400f, 500f, 1000f),
                    Pick(i, 5f, 4.75f, 4.5f, 4.25f, 4f), Pick(i, 20f, 22f, 24f, 26f, 30f));
            case RougeTowerType.OrbitSphere:
                return new RougeTowerStats(Pick(i, 30f, 45f, 65f, 90f, 125f),
                    Pick(i, 3f, 2.8f, 2.6f, 2.4f, 2.2f), Pick(i, 12f, 15f, 18f, 21f, 25f),
                    projectileCount: Pick(i, 1, 2, 2, 3, 4),
                    tickInterval: Pick(i, 0.3f, 0.28f, 0.25f, 0.22f, 0.2f),
                    orbitSphereRadius: Pick(i, 1.2f, 1.3f, 1.4f, 1.5f, 1.7f),
                    orbitRadialSpeed: Pick(i, 8f, 9f, 10f, 11f, 12f),
                    orbitAngularSpeed: Pick(i, 180f, 210f, 240f, 270f, 320f),
                    orbitOuterHoldDuration: Pick(i, 1.5f, 2f, 2.5f, 3f, 4f));
            case RougeTowerType.RocketBarrage:
                return new RougeTowerStats(Pick(i, 18f, 28f, 42f, 62f, 90f),
                    Pick(i, 3.2f, 3f, 2.8f, 2.6f, 2.4f), Pick(i, 18f, 21f, 24f, 27f, 30f),
                    projectileCount: Pick(i, 6, 8, 10, 12, 16),
                    aoeRadius: Pick(i, 2.25f, 2.45f, 2.65f, 2.85f, 3.1f),
                    projectileInterval: Pick(i, 0.09f, 0.08f, 0.075f, 0.07f, 0.06f),
                    projectileFlightDuration: Pick(i, 1.05f, 1f, 0.95f, 0.9f, 0.85f),
                    brownianStrength: Pick(i, 3f, 3.2f, 3.4f, 3.6f, 3.8f));
            case RougeTowerType.ChargeTower:
            case RougeTowerType.ReinforcementTower:
            default:
                return new RougeTowerStats(0f, 1f, 0f);
        }
    }

    private static float Pick(int i, float a, float b, float c, float d, float e)
    {
        switch (i) { case 0: return a; case 1: return b; case 2: return c; case 3: return d; default: return e; }
    }

    private static int Pick(int i, int a, int b, int c, int d, int e)
    {
        switch (i) { case 0: return a; case 1: return b; case 2: return c; case 3: return d; default: return e; }
    }

    public static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
        Vector3 scale, Vector3 localPosition, Color color, Quaternion? localRotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        go.transform.localRotation = localRotation ?? Quaternion.identity;
        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
            else UnityEngine.Object.DestroyImmediate(collider);
        }
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.material = CreateMaterial(color);
        return go;
    }

    public static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        material.enableInstancing = true;
        return material;
    }

    public static LineRenderer CreateCircleRenderer(string name, Transform parent)
    {
        LineRenderer line = CreateBeamRenderer(name, parent, 0.12f);
        line.loop = true;
        line.positionCount = 65;
        return line;
    }

    public static GameObject CreateTowerEditIndicator(string name, Transform parent, bool selected)
    {
        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Quad);
        indicator.name = name;
        indicator.transform.SetParent(parent, false);
        indicator.transform.localPosition = new Vector3(0f, 0.24f, 0f);
        indicator.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Collider collider = indicator.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
            else UnityEngine.Object.DestroyImmediate(collider);
        }
        MeshRenderer renderer = indicator.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetTowerEditIndicatorMaterial(selected);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        indicator.SetActive(false);
        return indicator;
    }

    private static Material GetTowerEditIndicatorMaterial(bool selected)
    {
        // Each mode gets one shared material; animation and shape remain fully shader-driven.
        Material cached = selected
            ? s_towerSelectedIndicatorMaterial
            : s_towerUpgradeIndicatorMaterial;
        if (cached != null) return cached;

        Shader shader = Shader.Find("Rouge/Tower Edit Indicator") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Sprites/Default");
        Material material = new Material(shader)
        {
            name = selected ? "Tower Selected Shader Material" : "Tower Upgrade Ready Shader Material",
            renderQueue = 3020,
            enableInstancing = true
        };
        Color tint = selected
            ? new Color(1f, 0.38f, 0.035f, 1f)
            : new Color(0.2f, 1f, 0.46f, 1f);
        if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", tint);
        else if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        else material.color = tint;
        if (material.HasProperty("_Mode")) material.SetFloat("_Mode", selected ? 0f : 1f);
        if (material.HasProperty("_PulseSpeed")) material.SetFloat("_PulseSpeed", selected ? 1.42f : 1.05f);
        if (material.HasProperty("_RotationSpeed")) material.SetFloat("_RotationSpeed", selected ? 1.5f : 0.9f);

        if (selected) s_towerSelectedIndicatorMaterial = material;
        else s_towerUpgradeIndicatorMaterial = material;
        return material;
    }

    public static LineRenderer CreateBeamRenderer(string name, Transform parent, float width)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        if (s_lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            s_lineMaterial = new Material(shader);
        }
        line.sharedMaterial = s_lineMaterial;
        return line;
    }

    public static Material GetLaserConnectionMaterial()
    {
        if (s_laserConnectionMaterial != null) return s_laserConnectionMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        s_laserConnectionMaterial = new Material(shader)
        {
            name = "Shared Thin Tower Laser Material",
            color = new Color(0.18f, 1f, 0.72f, 1f),
            renderQueue = 3000,
            enableInstancing = true
        };
        if (s_laserConnectionMaterial.HasProperty("_BaseColor"))
        {
            s_laserConnectionMaterial.SetColor("_BaseColor", s_laserConnectionMaterial.color);
        }
        return s_laserConnectionMaterial;
    }

    public static Material GetCrystalLaserMaterial()
    {
        if (s_crystalLaserMaterial != null) return s_crystalLaserMaterial;
        s_crystalLaserMaterial = Resources.Load<Material>("Rouge_CrystalLaserRibbon");
        if (s_crystalLaserMaterial != null) return s_crystalLaserMaterial;

        Shader shader = Shader.Find("Rouge/TowerLaserRibbon");
        bool usesEnergyRibbonShader = shader != null;
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        s_crystalLaserMaterial = new Material(shader)
        {
            name = "Crystal Tower Energy Ribbon Material",
            renderQueue = 3000,
            enableInstancing = true
        };
        Color fallbackColor = new Color(0.08f, 0.58f, 1f, 1f);
        if (usesEnergyRibbonShader)
        {
            s_crystalLaserMaterial.SetColor("_CoreColor", new Color(1.25f, 1.5f, 1.85f, 1f));
            s_crystalLaserMaterial.SetColor("_BeamColor", new Color(0.025f, 0.46f, 2.0f, 1f));
            s_crystalLaserMaterial.SetColor("_GlowColor", new Color(0.20f, 0.018f, 1.2f, 1f));
            s_crystalLaserMaterial.SetFloat("_CoreWidth", 0.07f);
            s_crystalLaserMaterial.SetFloat("_BeamWidth", 0.42f);
            s_crystalLaserMaterial.SetFloat("_EdgeSoftness", 0.50f);
            s_crystalLaserMaterial.SetFloat("_FlowScale", 34f);
            s_crystalLaserMaterial.SetFloat("_FlowSpeed", 20f);
            s_crystalLaserMaterial.SetFloat("_PulseSpeed", 7.5f);
            s_crystalLaserMaterial.SetFloat("_SparkIntensity", 1.45f);
            s_crystalLaserMaterial.SetFloat("_EndFade", 0.018f);
            s_crystalLaserMaterial.SetFloat("_Alpha", 0.9f);
        }
        else
        {
            s_crystalLaserMaterial.color = fallbackColor;
            if (s_crystalLaserMaterial.HasProperty("_BaseColor"))
                s_crystalLaserMaterial.SetColor("_BaseColor", fallbackColor);
        }
        return s_crystalLaserMaterial;
    }

    public static void UpdateCircle(LineRenderer line, Vector3 center, float radius, Color color, bool visible,
        float heightOffset = 0.15f)
    {
        if (line == null) return;
        line.enabled = visible;
        if (!visible) return;
        line.startColor = color;
        line.endColor = color;
        center.y += heightOffset;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)(line.positionCount - 1) * Mathf.PI * 2f;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    public static void UpdateGridSquare(LineRenderer line, Vector3 center, float cellSize,
        int halfCellCount, Color color, bool visible)
    {
        if (line == null) return;
        line.enabled = visible;
        if (!visible) return;
        line.loop = false;
        line.startColor = color;
        line.endColor = color;
        cellSize = Mathf.Max(0.1f, cellSize);
        halfCellCount = Mathf.Max(1, halfCellCount);
        int cellsAcross = halfCellCount * 2;
        float halfExtent = halfCellCount * cellSize;
        center.y += 0.15f;
        var points = new List<Vector3>((cellsAcross + 1) * 4 + 4);

        for (int x = 0; x <= cellsAcross; x++)
        {
            float px = center.x - halfExtent + x * cellSize;
            float z0 = (x & 1) == 0 ? center.z - halfExtent : center.z + halfExtent;
            float z1 = (x & 1) == 0 ? center.z + halfExtent : center.z - halfExtent;
            points.Add(new Vector3(px, center.y, z0));
            points.Add(new Vector3(px, center.y, z1));
        }

        bool verticalEndedAtTop = (cellsAcross & 1) == 0;
        for (int row = 0; row <= cellsAcross; row++)
        {
            int yIndex = verticalEndedAtTop ? cellsAcross - row : row;
            float pz = center.z - halfExtent + yIndex * cellSize;
            bool rightToLeft = (row & 1) == 0;
            float x0 = rightToLeft ? center.x + halfExtent : center.x - halfExtent;
            float x1 = rightToLeft ? center.x - halfExtent : center.x + halfExtent;
            points.Add(new Vector3(x0, center.y, pz));
            points.Add(new Vector3(x1, center.y, pz));
        }
        line.positionCount = points.Count;
        line.SetPositions(points.ToArray());
    }

    public static void UpdateCellOutline(LineRenderer line, Vector3 center, float cellSize,
        Color color, bool visible)
    {
        UpdateSquareOutline(line, center, Mathf.Max(0.05f, cellSize) * 0.5f, color, visible);
    }

    public static void UpdateSquareOutline(LineRenderer line, Vector3 center, float halfExtent,
        Color color, bool visible)
    {
        if (line == null) return;
        line.enabled = visible;
        if (!visible) return;
        line.loop = true;
        line.startColor = color;
        line.endColor = color;
        center.y += 0.16f;
        halfExtent = Mathf.Max(0.05f, halfExtent);
        line.positionCount = 4;
        line.SetPosition(0, center + new Vector3(-halfExtent, 0f, -halfExtent));
        line.SetPosition(1, center + new Vector3(-halfExtent, 0f, halfExtent));
        line.SetPosition(2, center + new Vector3(halfExtent, 0f, halfExtent));
        line.SetPosition(3, center + new Vector3(halfExtent, 0f, -halfExtent));
    }

    public static void SetRenderersTransparent(GameObject root, bool transparent, Color tint)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is LineRenderer) continue;
            if (renderers[i] is SpriteRenderer spriteRenderer)
            {
                // Keep the authored sci-fi artwork readable during placement. The grid
                // and energy rings carry most of the valid/invalid state instead of
                // turning the whole tower into a flat green or red silhouette.
                bool invalidPreview = transparent && tint.r > 0.8f && tint.g < 0.25f;
                float tintStrength = invalidPreview ? 0.55f : 0.24f;
                spriteRenderer.color = transparent
                    ? new Color(
                        Mathf.Lerp(1f, tint.r, tintStrength),
                        Mathf.Lerp(1f, tint.g, tintStrength),
                        Mathf.Lerp(1f, tint.b, tintStrength),
                        invalidPreview ? Mathf.Max(0.74f, tint.a) : Mathf.Lerp(0.72f, tint.a, 0.3f))
                    : tint;
                continue;
            }

            Material material = renderers[i].material;
            Color baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            bool invalidMaterialPreview = transparent && tint.r > 0.8f && tint.g < 0.25f;
            float materialTintStrength = invalidMaterialPreview ? 0.54f : 0.3f;
            Color output = transparent
                ? new Color(
                    baseColor.r * Mathf.Lerp(1f, tint.r, materialTintStrength),
                    baseColor.g * Mathf.Lerp(1f, tint.g, materialTintStrength),
                    baseColor.b * Mathf.Lerp(1f, tint.b, materialTintStrength),
                    invalidMaterialPreview ? Mathf.Max(0.76f, tint.a) : Mathf.Lerp(0.72f, tint.a, 0.3f))
                : new Color(baseColor.r, baseColor.g, baseColor.b, 1f);
            material.color = output;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", output);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", transparent ? 1f : 0f);
        }
    }

    public static void DestroyChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            else UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }
}
