using UnityEngine;

public enum RougeTowerBuffStat
{
    Damage,
    Range,
    AttackSpeed
}

[System.Serializable]
public struct RougeTowerBuffLevels
{
    public int Damage;
    public int Range;
    public int AttackSpeed;

    public RougeTowerBuffLevels(int damage, int range, int attackSpeed)
    {
        Damage = damage;
        Range = range;
        AttackSpeed = attackSpeed;
    }

    public int Get(RougeTowerBuffStat stat)
    {
        switch (stat)
        {
            case RougeTowerBuffStat.Range: return Range;
            case RougeTowerBuffStat.AttackSpeed: return AttackSpeed;
            default: return Damage;
        }
    }

    public void Add(RougeTowerBuffStat stat, int amount)
    {
        switch (stat)
        {
            case RougeTowerBuffStat.Range:
                Range = AddWithoutOverflow(Range, amount);
                break;
            case RougeTowerBuffStat.AttackSpeed:
                AttackSpeed = AddWithoutOverflow(AttackSpeed, amount);
                break;
            default:
                Damage = AddWithoutOverflow(Damage, amount);
                break;
        }
    }

    public static RougeTowerBuffLevels operator +(RougeTowerBuffLevels left, RougeTowerBuffLevels right)
    {
        return new RougeTowerBuffLevels(
            AddWithoutOverflow(left.Damage, right.Damage),
            AddWithoutOverflow(left.Range, right.Range),
            AddWithoutOverflow(left.AttackSpeed, right.AttackSpeed));
    }

    private static int AddWithoutOverflow(int left, int right)
    {
        long sum = (long)left + right;
        return sum > int.MaxValue ? int.MaxValue : sum < int.MinValue ? int.MinValue : (int)sum;
    }
}

public static class RougeTowerBuffMath
{
    public const int MinimumEffectiveLevel = -3;
    public const int MaximumEffectiveLevel = 3;

    public static int GetEffectiveLevel(int rawLevel)
    {
        return Mathf.Clamp(rawLevel, MinimumEffectiveLevel, MaximumEffectiveLevel);
    }

    public static float GetMultiplier(int rawLevel)
    {
        switch (GetEffectiveLevel(rawLevel))
        {
            case -3: return 0.5f;
            case -2: return 0.7f;
            case -1: return 0.85f;
            case 1: return 1.15f;
            case 2: return 1.3f;
            case 3: return 1.5f;
            default: return 1f;
        }
    }

    public static int GetPercent(int rawLevel)
    {
        switch (GetEffectiveLevel(rawLevel))
        {
            case -3: return -50;
            case -2: return -30;
            case -1: return -15;
            case 1: return 15;
            case 2: return 30;
            case 3: return 50;
            default: return 0;
        }
    }
}

internal enum RougeTowerBuffSource
{
    Permanent,
    TowerPlace,
    BossSkill,
    FocusedMode,
    Overclock,
    Count
}

[DisallowMultipleComponent]
public sealed class RougeDefenseTower : MonoBehaviour
{
  



    [SerializeField] private RougeTowerType towerType;
    [SerializeField, Range(1, TowerDefenseVisuals.MaxTowerLevel)] private int level = 1;
    [SerializeField] private float placementRadius;
    [SerializeField] private int purchaseCost;
    [SerializeField] private int investedGold;
    [SerializeField] private bool isTargetedDamage = true;
    [SerializeField] private RougeTowerTargetPriority targetPriority = RougeTowerTargetPriority.NearestToGoal;
    [SerializeField] private RougeTowerPlaceEffect towerPlaceEffect;
    [System.NonSerialized] internal float attackTimer;
    [System.NonSerialized] internal int targetIndex = -1;
    [System.NonSerialized] internal int projectileBurstShotsRemaining;
    [System.NonSerialized] internal int projectileBurstShotIndex;
    [System.NonSerialized] internal float projectileBurstTimer;
    [System.NonSerialized] internal int projectileBurstPrimaryTargetIndex = -1;
    [System.NonSerialized] internal Vector3 projectileBurstPrimaryTarget;
    [System.NonSerialized] internal RougeBillboard billboard;
    [System.NonSerialized] internal LineRenderer collisionRing;
    [System.NonSerialized] internal LineRenderer attackRing;
    [System.NonSerialized] private LineRenderer selectedHintRing;
    [System.NonSerialized] private LineRenderer upgradeHintRing;
    [System.NonSerialized] private float selectedHintRadius;
    [System.NonSerialized] private float upgradeHintRadius;
    private const float SelectedHintRadiusPadding = 0.2f;
    private const float UpgradeHintRadiusPadding = 2.2f;
    private const int MaxLaserConnections = 30;
    private const float OrbitLaserBeamWidth = 0.18f;
    private readonly Vector3[] laserVertices = new Vector3[MaxLaserConnections * 4];
    private readonly int[] laserLineIndices = new int[MaxLaserConnections * 2];
    private readonly int[] laserRibbonIndices = new int[MaxLaserConnections * 12];
    private GameObject laserBeamObject;
    private Mesh laserBeamMesh;
    private GameObject bossInterferenceMarker;
    private readonly RougeTowerBuffLevels[] buffSources =
        new RougeTowerBuffLevels[(int)RougeTowerBuffSource.Count];
    private float overclockRemaining;
    private bool towerPlaceInitialLevelApplied;
    private ParticleSystem overclockParticles;
    private Material overclockParticleMaterial;

    private RougeTowerStats Stats => TowerDefenseVisuals.GetStats(towerType, level);
    public RougeTowerType TowerType => towerType;
    public int Level => level;
    public int MaxLevel => TowerDefenseVisuals.MaxTowerLevel;
    public bool CanUpgrade => level < MaxLevel;
    public float Damage => Stats.Damage * GetBuffMultiplier(RougeTowerBuffStat.Damage);
    public float AttackInterval => Stats.AttackInterval;
    public float EffectiveAttackInterval => Stats.AttackInterval /
        Mathf.Max(0.01f, GetBuffMultiplier(RougeTowerBuffStat.AttackSpeed));
    public float AttackRange => Stats.AttackRadius * GetBuffMultiplier(RougeTowerBuffStat.Range);
    public Vector2Int FootprintCells => TowerDefenseVisuals.GetFootprintSize(towerType);
    public int TargetCount => Stats.TargetCount;
    public int ProjectileCount => Stats.ProjectileCount;
    public float AoeRadius => Stats.AoeRadius;
    public float EffectPercent => Stats.EffectPercent;
    public float EffectDuration => Stats.EffectDuration;
    public float TickInterval => Stats.TickInterval;
    public float OrbitSphereRadius => Stats.OrbitSphereRadius;
    public float OrbitRadialSpeed => Stats.OrbitRadialSpeed;
    public float OrbitAngularSpeed => Stats.OrbitAngularSpeed;
    public float OrbitOuterHoldDuration => Stats.OrbitOuterHoldDuration;
    public float PlacementRadius => placementRadius;
    public int PurchaseCost => purchaseCost;
    public int PlacementCost => ScaleGoldCost(purchaseCost);
    public int InvestedGold => investedGold;
    public bool IsTargetedDamage => isTargetedDamage;
    public RougeTowerTargetPriority TargetPriority => targetPriority;
    public float AttackSpeedMultiplier => GetBuffMultiplier(RougeTowerBuffStat.AttackSpeed);
    public bool IsOverclocked => overclockRemaining > 0f;
    public RougeTowerPlaceEffect TowerPlaceEffect => towerPlaceEffect;
    public bool AllowsSellRefund => RougeTowerPlaceEffectRules.AllowsSellRefund(towerPlaceEffect);
    public int KillGoldBonus => RougeTowerPlaceEffectRules.GetKillGoldBonus(towerPlaceEffect);
    // Lv1 purchase is 1x. Upgrades to Lv2..Lv5 cost 2x, 4x, 8x and 16x.
    public int UpgradeCost => CanUpgrade ? ScaleGoldCost(purchaseCost * (1 << level)) : 0;
    public string DisplayName => TowerDefenseVisuals.GetTowerName(towerType);

    internal void Configure(RougeTowerType type, bool preview)
    {
        towerType = type;
        level = 1;
        towerPlaceEffect = RougeTowerPlaceEffect.None;
        towerPlaceInitialLevelApplied = false;
        SetBuffSource(RougeTowerBuffSource.TowerPlace, default);
        TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out placementRadius, out purchaseCost);
        isTargetedDamage = GetDefaultTargetedDamage(type);
        investedGold = preview ? 0 : purchaseCost;
        attackTimer = type == RougeTowerType.Laser ? 0f : AttackInterval * 0.25f;
        targetIndex = -1;
        projectileBurstShotsRemaining = 0;
        projectileBurstShotIndex = 0;
        projectileBurstTimer = 0f;
        projectileBurstPrimaryTargetIndex = -1;
        RefreshFocusedModeBuff();
        CacheEditHintRadii();
        InitializePrefabVisuals(preview);
    }

    internal void FinalizePlacement()
    {
        ApplyTowerPlaceInitialLevelBonus();
        investedGold = PlacementCost;
        Collider collider = GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        TowerDefenseVisuals.SetRenderersTransparent(gameObject, false, Color.white);
    }

    internal void Ensure2DVisual()
    {
        TowerDefenseVisuals.GetBaseStats(towerType, out _, out _, out _, out placementRadius, out purchaseCost);
        isTargetedDamage = GetDefaultTargetedDamage(towerType);
        investedGold = Mathf.Max(investedGold, purchaseCost);
        RefreshFocusedModeBuff();
        CacheEditHintRadii();
        InitializePrefabVisuals(false);
    }

    internal void ApplyTowerPlaceEffect(RougeTowerPlaceEffect effect, bool existingTower = false)
    {
        towerPlaceEffect = effect;
        SetBuffSource(RougeTowerBuffSource.TowerPlace,
            RougeTowerPlaceEffectRules.GetBuffLevels(effect));
        if (!existingTower) return;
        ApplyTowerPlaceInitialLevelBonus();
        investedGold = Mathf.Max(investedGold, PlacementCost);
    }

    internal void SetPreviewState(bool valid, bool[] cellValidity = null)
    {
        TowerDefenseVisuals.SetRenderersTransparent(gameObject, true,
            valid ? new Color(0.2f, 1f, 0.35f, 0.62f) : new Color(1f, 0.15f, 0.12f, 0.62f));
        SetRangeVisibility(true, valid);
    }

    internal bool Upgrade()
    {
        if (!CanUpgrade) return false;
        int cost = UpgradeCost;
        level++;
        investedGold += cost;
        targetIndex = -1;
        projectileBurstShotsRemaining = 0;
        projectileBurstShotIndex = 0;
        projectileBurstTimer = 0f;
        projectileBurstPrimaryTargetIndex = -1;
        return true;
    }

    internal void ToggleTargetPriority()
    {
        targetPriority = targetPriority == RougeTowerTargetPriority.BossFirst
            ? RougeTowerTargetPriority.NearestToGoal
            : RougeTowerTargetPriority.BossFirst;
        targetIndex = -1;
        RefreshFocusedModeBuff();
    }

    internal void SetRangeVisibility(bool visible, bool valid = true)
    {
        if (collisionRing != null) collisionRing.enabled = false;
        TowerDefenseVisuals.UpdateCircle(attackRing, transform.position, AttackRange,
            new Color(0.15f, 0.72f, 1f, 0.78f), visible);
    }

    internal void SetEditHintState(bool editMode, bool selected, bool upgradeAvailable)
    {
        bool showSelected = editMode && selected;
        bool showUpgradeable = editMode && upgradeAvailable;
        if (showSelected || showUpgradeable) EnsureEditHintVisuals();

        // Placed tower ranges belong to the active edit selection only. Placement
        // previews are not in _defenseTowers and keep using SetPreviewState instead.
        SetRangeVisibility(showSelected);

        TowerDefenseVisuals.UpdateCircle(selectedHintRing, transform.position, selectedHintRadius,
            new Color(1f, 0.46f, 0.05f, 1f), showSelected, 0.5f);
        TowerDefenseVisuals.UpdateCircle(upgradeHintRing, transform.position, upgradeHintRadius,
            new Color(0.18f, 1f, 0.38f, 1f), showUpgradeable, 0.5f);
    }

    private void CacheEditHintRadii()
    {
        Vector2Int footprint = FootprintCells;
        float microCellSize = RougeTowerDefenseMapLoader.ActiveMap != null
            ? RougeTowerDefenseMapLoader.ActiveMap.MicroCellSize
            : 1f;
        float squareSide = Mathf.Max(footprint.x, footprint.y) * Mathf.Max(0.001f, microCellSize);
        float squareDiagonal = Mathf.Sqrt(squareSide * squareSide * 2f);
        selectedHintRadius = (squareDiagonal + SelectedHintRadiusPadding)/2f;
        upgradeHintRadius = (squareDiagonal + UpgradeHintRadiusPadding)/2f;
    }

    internal void ShowLaserBeams(Vector3 start, Vector3[] targets, int count)
    {
        int connectionCount = Mathf.Min(count, MaxLaserConnections);
        if (connectionCount <= 0)
        {
            HideLaserBeams();
            return;
        }

        EnsureLaserBeamMesh();
        if (towerType == RougeTowerType.OrbitSphere)
        {
            ShowOrbitLaserRibbons(start, targets, connectionCount);
            return;
        }

        Vector3 localStart = transform.InverseTransformPoint(start);
        for (int i = 0; i < connectionCount; i++)
        {
            int vertex = i * 2;
            laserVertices[vertex] = localStart;
            laserVertices[vertex + 1] = transform.InverseTransformPoint(targets[i] + Vector3.up * 0.08f);
        }

        int vertexCount = connectionCount * 2;
        laserBeamMesh.Clear(false);
        laserBeamMesh.SetVertices(laserVertices, 0, vertexCount);
        laserBeamMesh.SetIndices(laserLineIndices, 0, vertexCount, MeshTopology.Lines, 0, true);
        laserBeamObject.SetActive(true);
    }

    private void ShowOrbitLaserRibbons(Vector3 start, Vector3[] targets, int connectionCount)
    {
        Camera mainCamera = Camera.main;
        Vector3 viewDirection = mainCamera != null ? mainCamera.transform.forward : Vector3.down;
        float halfWidth = OrbitLaserBeamWidth * 0.5f;
        for (int i = 0; i < connectionCount; i++)
        {
            Vector3 end = targets[i] + Vector3.up * 0.08f;
            Vector3 beamDirection = end - start;
            Vector3 side = Vector3.Cross(viewDirection, beamDirection);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(Vector3.up, beamDirection);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side = side.normalized * halfWidth;

            int vertex = i * 4;
            laserVertices[vertex] = transform.InverseTransformPoint(start - side);
            laserVertices[vertex + 1] = transform.InverseTransformPoint(start + side);
            laserVertices[vertex + 2] = transform.InverseTransformPoint(end - side);
            laserVertices[vertex + 3] = transform.InverseTransformPoint(end + side);
        }

        int vertexCount = connectionCount * 4;
        int indexCount = connectionCount * 12;
        laserBeamMesh.Clear(false);
        laserBeamMesh.SetVertices(laserVertices, 0, vertexCount);
        laserBeamMesh.SetIndices(laserRibbonIndices, 0, indexCount, MeshTopology.Triangles, 0, true);
        laserBeamObject.SetActive(true);
    }

    internal void ShowFocusedLaserBeams(Vector3 start, Vector3 target, int count)
    {
        int connectionCount = Mathf.Min(count, MaxLaserConnections);
        if (connectionCount <= 0)
        {
            HideLaserBeams();
            return;
        }

        EnsureLaserBeamMesh();
        for (int i = 0; i < connectionCount; i++)
        {
            float offset = (i - (connectionCount - 1) * 0.5f) * 0.055f;
            int vertex = i * 2;
            laserVertices[vertex] = transform.InverseTransformPoint(start + Vector3.right * offset);
            laserVertices[vertex + 1] = transform.InverseTransformPoint(target + Vector3.up * 0.08f);
        }

        int vertexCount = connectionCount * 2;
        laserBeamMesh.Clear(false);
        laserBeamMesh.SetVertices(laserVertices, 0, vertexCount);
        laserBeamMesh.SetIndices(laserLineIndices, 0, vertexCount, MeshTopology.Lines, 0, true);
        laserBeamObject.SetActive(true);
    }

    internal void HideLaserBeams()
    {
        if (laserBeamObject != null) laserBeamObject.SetActive(false);
    }

    internal void AimAt(Vector3 worldTarget)
    {
        if (billboard != null) billboard.SetWorldDirection(worldTarget - transform.position);
    }

    internal Vector3 GetShootPosition()
    {
        return billboard != null ? billboard.ShootPosition : transform.position + Vector3.up * 3f;
    }

    internal void PlayAttackAnimation(System.Action onShotFired)
    {
        if (billboard != null) billboard.PlayShootAnimation(onShotFired);
        else onShotFired?.Invoke();
    }

    internal void UpdatePresentation(float dt)
    {
        UpdateOverclock(dt);
    }

    internal Vector3 GetCrystalLaserOrigin()
    {
        return GetShootPosition();
    }

    public void AddBuff(RougeTowerBuffStat stat, int levelDelta)
    {
        RougeTowerBuffLevels buffs = buffSources[(int)RougeTowerBuffSource.Permanent];
        buffs.Add(stat, levelDelta);
        SetBuffSource(RougeTowerBuffSource.Permanent, buffs);
    }

    public void AddBuff(RougeTowerBuffLevels levelDeltas)
    {
        SetBuffSource(RougeTowerBuffSource.Permanent,
            buffSources[(int)RougeTowerBuffSource.Permanent] + levelDeltas);
    }

    public int GetRawBuffLevel(RougeTowerBuffStat stat)
    {
        int total = 0;
        for (int i = 0; i < buffSources.Length; i++)
        {
            long next = (long)total + buffSources[i].Get(stat);
            total = next > int.MaxValue ? int.MaxValue : next < int.MinValue ? int.MinValue : (int)next;
        }
        return total;
    }

    public int GetEffectiveBuffLevel(RougeTowerBuffStat stat)
    {
        return RougeTowerBuffMath.GetEffectiveLevel(GetRawBuffLevel(stat));
    }

    public string GetBuffDisplayText()
    {
        string text = string.Empty;
        AppendBuffDisplay(ref text, "DMG", RougeTowerBuffStat.Damage);
        AppendBuffDisplay(ref text, "RANGE", RougeTowerBuffStat.Range);
        AppendBuffDisplay(ref text, "ASPD", RougeTowerBuffStat.AttackSpeed);
        return text;
    }

    internal void SetBossInterference(bool active, int attackSpeedBuffLevel)
    {
        SetBuffSource(RougeTowerBuffSource.BossSkill,
            active ? new RougeTowerBuffLevels(0, 0, attackSpeedBuffLevel) : default);
        bool showDebuff = active && GetEffectiveBuffLevel(RougeTowerBuffStat.AttackSpeed) < 0;
        if (!showDebuff)
        {
            if (bossInterferenceMarker != null) bossInterferenceMarker.SetActive(false);
            return;
        }
        if (bossInterferenceMarker == null)
        {
            bossInterferenceMarker = new GameObject("Boss Interference Debuff");
            bossInterferenceMarker.transform.SetParent(transform, false);
            bossInterferenceMarker.transform.localPosition = new Vector3(0f, 5.3f, 0f);
            RougeBillboard markerBillboard = bossInterferenceMarker.AddComponent<RougeBillboard>();
            SpriteRenderer marker = RougeSpriteAssets.CreateRenderer("Debuff Marker", bossInterferenceMarker.transform,
                RougeSpriteAssets.Load("Sprites/projectile_energy"), Vector3.zero, 0.32f, 80,
                new Color(1f, 0.12f, 0.85f, 1f));
            markerBillboard.SetRotatingContent(marker.transform);
        }
        bossInterferenceMarker.SetActive(true);
        float pulse = 0.9f + Mathf.Sin(Time.unscaledTime * 7f) * 0.15f;
        bossInterferenceMarker.transform.localScale = Vector3.one * pulse;
    }

    internal void ActivateOverclock(float duration, RougeTowerBuffLevels buffs)
    {
        overclockRemaining = Mathf.Max(overclockRemaining, Mathf.Max(0f, duration));
        SetBuffSource(RougeTowerBuffSource.Overclock, buffs);
        EnsureOverclockParticles();
        if (overclockParticles != null && !overclockParticles.isPlaying) overclockParticles.Play(true);
    }

    private void UpdateOverclock(float dt)
    {
        if (overclockRemaining <= 0f) return;
        overclockRemaining = Mathf.Max(0f, overclockRemaining - Mathf.Max(0f, dt));
        if (overclockRemaining > 0f) return;
        SetBuffSource(RougeTowerBuffSource.Overclock, default);
        if (overclockParticles != null)
            overclockParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void EnsureOverclockParticles()
    {
        if (overclockParticles != null) return;
        GameObject effect = new GameObject("Overclock Particles");
        effect.transform.SetParent(transform, false);
        effect.transform.localPosition = new Vector3(0f, 2.8f, 0f);
        overclockParticles = effect.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = overclockParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.15f, 0.8f, 1f, 0.95f), new Color(1f, 0.58f, 0.08f, 0.95f));
        main.maxParticles = 48;

        ParticleSystem.EmissionModule emission = overclockParticles.emission;
        emission.rateOverTime = 18f;
        ParticleSystem.ShapeModule shape = overclockParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1.55f;
        shape.radiusThickness = 0.08f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = overclockParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.35f, 0.9f, 1f), 0f),
                new GradientColorKey(new Color(1f, 0.5f, 0.08f), 1f)
            },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystemRenderer particleRenderer = effect.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.sortingOrder = 70;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null)
        {
            overclockParticleMaterial = new Material(shader) { name = "Tower Overclock Particle Material" };
            if (overclockParticleMaterial.HasProperty("_BaseColor"))
                overclockParticleMaterial.SetColor("_BaseColor", Color.white);
            particleRenderer.sharedMaterial = overclockParticleMaterial;
        }
    }

    private void OnValidate()
    {
        level = Mathf.Clamp(level, 1, TowerDefenseVisuals.MaxTowerLevel);
        isTargetedDamage = GetDefaultTargetedDamage(towerType);
        RefreshFocusedModeBuff();
    }

    private void RefreshFocusedModeBuff()
    {
        bool focused = targetPriority == RougeTowerTargetPriority.BossFirst;
        int damageLevel = focused &&
            (towerType == RougeTowerType.MachineGun || towerType == RougeTowerType.Flame) ? -2 : 0;
        SetBuffSource(RougeTowerBuffSource.FocusedMode,
            new RougeTowerBuffLevels(damageLevel, 0, 0));
    }

    private void SetBuffSource(RougeTowerBuffSource source, RougeTowerBuffLevels buffs)
    {
        float previousRange = AttackRange;
        buffSources[(int)source] = buffs;
        if (attackRing != null && attackRing.enabled && !Mathf.Approximately(previousRange, AttackRange))
            SetRangeVisibility(true);
    }

    private float GetBuffMultiplier(RougeTowerBuffStat stat)
    {
        return RougeTowerBuffMath.GetMultiplier(GetRawBuffLevel(stat));
    }

    private void AppendBuffDisplay(ref string text, string label, RougeTowerBuffStat stat)
    {
        int level = GetEffectiveBuffLevel(stat);
        if (level == 0) return;
        if (text.Length > 0) text += "  ";
        text += $"{label} {(level > 0 ? "+" : string.Empty)}{level}";
    }

    private void ApplyTowerPlaceInitialLevelBonus()
    {
        if (towerPlaceInitialLevelApplied) return;
        towerPlaceInitialLevelApplied = true;
        int levelBonus = RougeTowerPlaceEffectRules.GetInitialLevelBonus(towerPlaceEffect);
        level = Mathf.Clamp(level + levelBonus, 1, MaxLevel);
        if (levelBonus > 0)
            attackTimer = towerType == RougeTowerType.Laser ? 0f : EffectiveAttackInterval * 0.25f;
        targetIndex = -1;
    }

    private int ScaleGoldCost(int baseCost)
    {
        return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, baseCost) *
            RougeTowerPlaceEffectRules.GetGoldCostMultiplier(towerPlaceEffect)));
    }

    private void InitializePrefabVisuals(bool preview)
    {
        ReleaseLaserBeamMesh();
        billboard = GetComponentInChildren<RougeBillboard>(true);
        if (billboard == null)
        {
            Debug.LogError($"Tower prefab '{name}' is missing RougeBillboard.", this);
        }
        else
        {
            billboard.SetRotatingContentAngleOffset(180f);
        }

        Collider towerCollider = GetComponent<Collider>();
        if (towerCollider != null) towerCollider.enabled = false;

        if (collisionRing == null) collisionRing = TowerDefenseVisuals.CreateCircleRenderer("Placement Range", transform);
        if (attackRing == null) attackRing = TowerDefenseVisuals.CreateCircleRenderer("Attack Range", transform);
        SetRangeVisibility(preview);
    }

    private void EnsureEditHintVisuals()
    {
        if (selectedHintRing == null)
            selectedHintRing = TowerDefenseVisuals.CreateCircleRenderer("Tower Selected Hint Ring", transform);
        if (upgradeHintRing == null)
            upgradeHintRing = TowerDefenseVisuals.CreateCircleRenderer("Tower Upgrade Hint Ring", transform);
    }

    private void EnsureLaserBeamMesh()
    {
        if (laserBeamObject != null && laserBeamMesh != null) return;
        laserBeamObject = new GameObject("Thin Laser Connections");
        laserBeamObject.transform.SetParent(transform, false);
        MeshFilter filter = laserBeamObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = laserBeamObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = towerType == RougeTowerType.OrbitSphere
            ? TowerDefenseVisuals.GetCrystalLaserMaterial()
            : TowerDefenseVisuals.GetLaserConnectionMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        laserBeamMesh = new Mesh { name = "Tower Thin Laser Lines" };
        laserBeamMesh.MarkDynamic();
        filter.sharedMesh = laserBeamMesh;
        for (int i = 0; i < laserLineIndices.Length; i++) laserLineIndices[i] = i;
        for (int i = 0; i < MaxLaserConnections; i++)
        {
            int vertex = i * 4;
            int index = i * 12;
            // Duplicate the two triangles in reverse winding so the ribbon stays visible
            // with every camera angle and material culling mode.
            laserRibbonIndices[index] = vertex;
            laserRibbonIndices[index + 1] = vertex + 2;
            laserRibbonIndices[index + 2] = vertex + 1;
            laserRibbonIndices[index + 3] = vertex + 1;
            laserRibbonIndices[index + 4] = vertex + 2;
            laserRibbonIndices[index + 5] = vertex + 3;
            laserRibbonIndices[index + 6] = vertex;
            laserRibbonIndices[index + 7] = vertex + 1;
            laserRibbonIndices[index + 8] = vertex + 2;
            laserRibbonIndices[index + 9] = vertex + 1;
            laserRibbonIndices[index + 10] = vertex + 3;
            laserRibbonIndices[index + 11] = vertex + 2;
        }
    }

    private void ReleaseLaserBeamMesh()
    {
        if (laserBeamMesh != null)
        {
            if (Application.isPlaying) Destroy(laserBeamMesh);
            else DestroyImmediate(laserBeamMesh);
        }
        laserBeamMesh = null;
        laserBeamObject = null;
    }

    private void OnDestroy()
    {
        ReleaseLaserBeamMesh();
        if (overclockParticleMaterial != null)
        {
            if (Application.isPlaying) Destroy(overclockParticleMaterial);
            else DestroyImmediate(overclockParticleMaterial);
        }
    }

    private static bool GetDefaultTargetedDamage(RougeTowerType type)
    {
        return type != RougeTowerType.Ice && type != RougeTowerType.OrbitSphere;
    }
}
