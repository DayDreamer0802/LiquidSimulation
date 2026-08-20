using UnityEngine;

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
    [System.NonSerialized] internal float attackTimer;
    [System.NonSerialized] internal int targetIndex = -1;
    [System.NonSerialized] internal int cannonBurstShotsRemaining;
    [System.NonSerialized] internal float cannonBurstTimer;
    [System.NonSerialized] internal Vector3 cannonBurstTarget;
    [System.NonSerialized] internal RougeBillboard billboard;
    [System.NonSerialized] internal LineRenderer collisionRing;
    [System.NonSerialized] internal LineRenderer attackRing;
    private const int MaxLaserConnections = 30;
    private readonly Vector3[] laserVertices = new Vector3[MaxLaserConnections * 2];
    private readonly int[] laserIndices = new int[MaxLaserConnections * 2];
    private GameObject laserBeamObject;
    private Mesh laserBeamMesh;
    private GameObject bossInterferenceMarker;
    private float bossAttackSpeedMultiplier = 1f;
    private float overclockAttackSpeedMultiplier = 1f;
    private float overclockDamageMultiplier = 1f;
    private float overclockRemaining;
    private ParticleSystem overclockParticles;
    private Material overclockParticleMaterial;

    private RougeTowerStats Stats => TowerDefenseVisuals.GetStats(towerType, level);
    public RougeTowerType TowerType => towerType;
    public int Level => level;
    public int MaxLevel => TowerDefenseVisuals.MaxTowerLevel;
    public bool CanUpgrade => level < MaxLevel;
    public float Damage => Stats.Damage * overclockDamageMultiplier;
    public float AttackInterval => Stats.AttackInterval;
    public float AttackRange => Stats.AttackRadius;
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
    public int InvestedGold => investedGold;
    public bool IsTargetedDamage => isTargetedDamage;
    public RougeTowerTargetPriority TargetPriority => targetPriority;
    public float AttackSpeedMultiplier => bossAttackSpeedMultiplier * overclockAttackSpeedMultiplier;
    public bool IsOverclocked => overclockRemaining > 0f;
    // Lv1 purchase is 1x. Upgrades to Lv2..Lv5 cost 2x, 4x, 8x and 16x.
    public int UpgradeCost => CanUpgrade ? purchaseCost * (1 << level) : 0;
    public string DisplayName => TowerDefenseVisuals.GetTowerName(towerType);

    internal void Configure(RougeTowerType type, bool preview)
    {
        towerType = type;
        level = 1;
        TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out placementRadius, out purchaseCost);
        isTargetedDamage = GetDefaultTargetedDamage(type);
        investedGold = preview ? 0 : purchaseCost;
        attackTimer = type == RougeTowerType.Laser ? 0f : AttackInterval * 0.25f;
        targetIndex = -1;
        cannonBurstShotsRemaining = 0;
        cannonBurstTimer = 0f;
        InitializePrefabVisuals(preview);
    }

    internal void FinalizePlacement()
    {
        investedGold = purchaseCost;
        Collider collider = GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        TowerDefenseVisuals.SetRenderersTransparent(gameObject, false, Color.white);
    }

    internal void Ensure2DVisual()
    {
        TowerDefenseVisuals.GetBaseStats(towerType, out _, out _, out _, out placementRadius, out purchaseCost);
        isTargetedDamage = GetDefaultTargetedDamage(towerType);
        investedGold = Mathf.Max(investedGold, purchaseCost);
        InitializePrefabVisuals(false);
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
        cannonBurstShotsRemaining = 0;
        return true;
    }

    internal void ToggleTargetPriority()
    {
        targetPriority = targetPriority == RougeTowerTargetPriority.BossFirst
            ? RougeTowerTargetPriority.NearestToGoal
            : RougeTowerTargetPriority.BossFirst;
        targetIndex = -1;
    }

    internal void SetRangeVisibility(bool visible, bool valid = true)
    {
        if (collisionRing != null) collisionRing.enabled = false;
        TowerDefenseVisuals.UpdateCircle(attackRing, transform.position, AttackRange,
            new Color(0.15f, 0.72f, 1f, 0.78f), visible);
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
        laserBeamMesh.SetIndices(laserIndices, 0, vertexCount, MeshTopology.Lines, 0, true);
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
        laserBeamMesh.SetIndices(laserIndices, 0, vertexCount, MeshTopology.Lines, 0, true);
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

    internal void SetBossInterference(bool active, float speedMultiplier)
    {
        bossAttackSpeedMultiplier = active ? Mathf.Clamp(speedMultiplier, 0.05f, 1f) : 1f;
        if (!active)
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

    internal void ActivateOverclock(float duration, float attackSpeed, float damage)
    {
        overclockRemaining = Mathf.Max(overclockRemaining, Mathf.Max(0f, duration));
        overclockAttackSpeedMultiplier = Mathf.Max(1f, attackSpeed);
        overclockDamageMultiplier = Mathf.Max(1f, damage);
        EnsureOverclockParticles();
        if (overclockParticles != null && !overclockParticles.isPlaying) overclockParticles.Play(true);
    }

    private void UpdateOverclock(float dt)
    {
        if (overclockRemaining <= 0f) return;
        overclockRemaining = Mathf.Max(0f, overclockRemaining - Mathf.Max(0f, dt));
        if (overclockRemaining > 0f) return;
        overclockAttackSpeedMultiplier = 1f;
        overclockDamageMultiplier = 1f;
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
        for (int i = 0; i < laserIndices.Length; i++) laserIndices[i] = i;
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
