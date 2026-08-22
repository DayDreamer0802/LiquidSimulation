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
    [InspectorName("水晶塔")] OrbitSphere
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

    public RougeTowerStats(float damage, float attackInterval, float attackRadius,
        int targetCount = 1, int projectileCount = 1, float aoeRadius = 0f,
        float effectPercent = 0f, float effectDuration = 0f, float tickInterval = 0f,
        float orbitSphereRadius = 0f, float orbitRadialSpeed = 0f, float orbitAngularSpeed = 0f,
        float orbitOuterHoldDuration = 0f)
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
    }
}

internal static class TowerDefenseVisuals
{
    public const int MaxTowerLevel = 5;
    public const int TowerTypeCount = 7;
    private static Material s_lineMaterial;
    private static Material s_laserConnectionMaterial;
    private static Material s_crystalLaserMaterial;
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
            case RougeTowerType.Ice: return "ICE TOWER";
            case RougeTowerType.MachineGun: return "MACHINE GUN";
            case RougeTowerType.Cannon: return "CANNON";
            case RougeTowerType.Flame: return "FLAME TOWER";
            case RougeTowerType.Laser: return "LASER TOWER";
            case RougeTowerType.PiercingLaser: return "PIERCING LASER";
            default: return "CRYSTAL TOWER";
        }
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
            default: return new Color(0.35f, 0.62f, 1f);
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
            cost = ScaleGoldCost(configured.purchaseCost);
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
            default: radius = 2.5f; cost = 900; break;
        }
        cost = ScaleGoldCost(cost);
    }

    public static Vector2Int GetFootprintSize(RougeTowerType type)
    {
        RougeTowerTypeConfig configured = s_runtimeBalance?.Find(type);
        return configured != null
            ? new Vector2Int(Mathf.Clamp(configured.footprintWidth, 1, 16),
                Mathf.Clamp(configured.footprintHeight, 1, 16))
            : new Vector2Int(4, 4);
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
            stats.OrbitOuterHoldDuration / attackSpeed);
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
                    effectPercent: Pick(i, 50f, 55f, 60f, 65f, 70f), effectDuration: 3f);
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
            default:
                return new RougeTowerStats(Pick(i, 30f, 45f, 65f, 90f, 125f),
                    Pick(i, 3f, 2.8f, 2.6f, 2.4f, 2.2f), Pick(i, 12f, 15f, 18f, 21f, 25f),
                    projectileCount: Pick(i, 1, 2, 2, 3, 4),
                    tickInterval: Pick(i, 0.3f, 0.28f, 0.25f, 0.22f, 0.2f),
                    orbitSphereRadius: Pick(i, 1.2f, 1.3f, 1.4f, 1.5f, 1.7f),
                    orbitRadialSpeed: Pick(i, 8f, 9f, 10f, 11f, 12f),
                    orbitAngularSpeed: Pick(i, 180f, 210f, 240f, 270f, 320f),
                    orbitOuterHoldDuration: Pick(i, 1.5f, 2f, 2.5f, 3f, 4f));
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
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        s_crystalLaserMaterial = new Material(shader)
        {
            name = "Crystal Tower Thin Blue Laser Material",
            color = new Color(0.08f, 0.58f, 1f, 1f),
            renderQueue = 3000,
            enableInstancing = true
        };
        if (s_crystalLaserMaterial.HasProperty("_BaseColor"))
            s_crystalLaserMaterial.SetColor("_BaseColor", s_crystalLaserMaterial.color);
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
                spriteRenderer.color = transparent ? tint : Color.white;
                continue;
            }

            Material material = renderers[i].material;
            Color baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            Color output = transparent
                ? new Color(baseColor.r * tint.r, baseColor.g * tint.g, baseColor.b * tint.b, tint.a)
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
