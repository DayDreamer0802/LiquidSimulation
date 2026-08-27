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
    public const int MaximumEffectiveLevel = 5;

    public static int GetEffectiveLevel(int rawLevel)
    {
        return Mathf.Clamp(rawLevel, MinimumEffectiveLevel, MaximumEffectiveLevel);
    }

    public static float GetMultiplier(int rawLevel)
    {
        return Mathf.Max(0.01f, 1f + GetEffectiveLevel(rawLevel) * 0.2f);
    }

    public static int GetPercent(int rawLevel)
    {
        return GetEffectiveLevel(rawLevel) * 20;
    }
}

internal enum RougeTowerBuffSource
{
    Permanent,
    TowerPlace,
    BossSkill,
    FocusedMode,
    Overclock,
    ReinforcementAura,
    Count
}

[DisallowMultipleComponent]
public sealed partial class RougeDefenseTower : MonoBehaviour
{
    private const int DefaultChargeTowerPlacementCost = 4000;
    private const int DefaultReinforcementTowerPlacementCost = 6000;

    [SerializeField] private RougeTowerType towerType;
    [SerializeField, Range(1, TowerDefenseVisuals.MaxTowerLevel)] private int level = 1;
    [SerializeField] private float placementRadius;
    [SerializeField] private int purchaseCost;
    [SerializeField] private bool chargeTower;
    [SerializeField] private int chargeTowerPlacementCost;
    [SerializeField] private bool reinforcementTower;
    [SerializeField] private int reinforcementTowerPlacementCost;
    [SerializeField] private int investedGold;
    [SerializeField] private bool isTargetedDamage = true;
    [SerializeField] private RougeTowerTargetPriority targetPriority = RougeTowerTargetPriority.NearestToGoal;
    [SerializeField] private RougeIceTowerBranch iceBranch;
    [SerializeField] private RougeIceTowerAugment iceAugment;
    [SerializeField] private RougeMachineGunBranch machineGunBranch;
    [SerializeField] private RougeMachineGunAugment machineGunAugment;
    [SerializeField] private RougeCannonBranch cannonBranch;
    [SerializeField] private RougeCannonAugment cannonAugment;
    [SerializeField] private RougeTowerPlaceEffect towerPlaceEffect;
    [SerializeField] private bool hasChargeTargetCell;
    [SerializeField] private Vector2Int chargeTargetCell;
    [SerializeField] private RougeTowerPlaceEffect chargedTileEffect;
    [System.NonSerialized] internal float attackTimer;
    [System.NonSerialized] internal int targetIndex = -1;
    [System.NonSerialized] internal int projectileBurstShotsRemaining;
    [System.NonSerialized] internal int projectileBurstShotIndex;
    [System.NonSerialized] internal float projectileBurstTimer;
    [System.NonSerialized] internal int projectileBurstPrimaryTargetIndex = -1;
    [System.NonSerialized] internal Vector3 projectileBurstPrimaryTarget;
    [System.NonSerialized] internal float iceSpikeTimer;
    [System.NonSerialized] private System.Action playAttackSoundReleaseCue;
    [System.NonSerialized] private bool echoAttackCycleActive;
    [System.NonSerialized] private int echoAttackRepeatsRemaining;
    [System.NonSerialized] private Vector3 echoAttackTarget;
    [System.NonSerialized] private float echoAttackRepeatDelayRemaining = -1f;
    [System.NonSerialized] internal RougeBillboard billboard;
    [System.NonSerialized] internal LineRenderer collisionRing;
    [System.NonSerialized] internal LineRenderer attackRing;
    [System.NonSerialized] private GameObject selectedHintEffect;
    [System.NonSerialized] private GameObject upgradeHintEffect;
    [System.NonSerialized] private float selectedHintRadius;
    [System.NonSerialized] private float upgradeHintRadius;
    private const float SelectedHintRadiusPadding = 0.2f;
    private const float UpgradeHintRadiusPadding = 2.2f;
    private const int MaxLaserConnections = 45;
    private const float OrbitLaserBeamWidth = 0.86f;
    private readonly Vector3[] laserVertices = new Vector3[MaxLaserConnections * 4];
    private readonly Vector2[] laserRibbonUvs = new Vector2[MaxLaserConnections * 4];
    private readonly int[] laserLineIndices = new int[MaxLaserConnections * 2];
    private readonly int[] laserRibbonIndices = new int[MaxLaserConnections * 12];
    private GameObject laserBeamObject;
    private Mesh laserBeamMesh;
    private GameObject bossInterferenceMarker;
    private readonly RougeTowerBuffLevels[] buffSources =
        new RougeTowerBuffLevels[(int)RougeTowerBuffSource.Count];
    private float overclockRemaining;
    private bool towerPlaceInitialLevelApplied;
    private bool towerPlaceLevelBonusReceived;
    private ParticleSystem overclockParticles;
    private Material overclockParticleMaterial;
    private bool towerVisualBaseScaleCached;
    private Vector3 towerVisualBaseScale;

    private RougeTowerStats Stats => TowerDefenseVisuals.GetStats(towerType, level);
    public RougeTowerType TowerType => towerType;
    public int Level => level;
    public int MaxLevel => TowerDefenseVisuals.MaxTowerLevel;
    public bool CanUpgrade => !IsSpecialTower && level < MaxLevel;
    public float Damage => Stats.Damage * GetBuffMultiplier(RougeTowerBuffStat.Damage);
    public float AttackInterval => Stats.AttackInterval;
    public float EffectiveAttackInterval => Stats.AttackInterval /
        Mathf.Max(0.01f, GetBuffMultiplier(RougeTowerBuffStat.AttackSpeed));
    public float AttackRange => Stats.AttackRadius * GetBuffMultiplier(RougeTowerBuffStat.Range);
    public Vector2Int FootprintCells => Vector2Int.one;
    public int TargetCount => Stats.TargetCount;
    public int ProjectileCount => Stats.ProjectileCount;
    public int AttackTargetCount => UsesEchoBarrageMultiplier &&
        (towerType == RougeTowerType.MachineGun || towerType == RougeTowerType.Laser)
            ? Mathf.CeilToInt(TargetCount * 1.5f)
            : TargetCount;
    public int AttackProjectileCount => UsesEchoBarrageMultiplier &&
        towerType == RougeTowerType.RocketBarrage
            ? Mathf.CeilToInt(ProjectileCount * 1.5f)
            : ProjectileCount;
    public float AoeRadius => Stats.AoeRadius;
    public float EffectPercent => towerType == RougeTowerType.Ice
        ? TowerDefenseVisuals.GetIceSpecializationConfig().slowPercent
        : Stats.EffectPercent;
    public float EffectDuration => towerType == RougeTowerType.Ice
        ? TowerDefenseVisuals.GetIceSpecializationConfig().slowDuration
        : Stats.EffectDuration;
    public float TickInterval => Stats.TickInterval;
    public float OrbitSphereRadius => Stats.OrbitSphereRadius;
    public float OrbitRadialSpeed => Stats.OrbitRadialSpeed;
    public float OrbitAngularSpeed => Stats.OrbitAngularSpeed;
    public float OrbitOuterHoldDuration => Stats.OrbitOuterHoldDuration;
    public float ProjectileInterval => Stats.ProjectileInterval;
    public float ProjectileFlightDuration => Stats.ProjectileFlightDuration;
    public float BrownianStrength => Stats.BrownianStrength;
    public float PlacementRadius => placementRadius;
    public int PurchaseCost => purchaseCost;
    public int PlacementCost => IsChargeTower
        ? Mathf.Max(0, chargeTowerPlacementCost)
        : IsReinforcementTower
            ? Mathf.Max(0, reinforcementTowerPlacementCost)
            : Mathf.Max(0, purchaseCost);
    public int InvestedGold => investedGold;
    public bool IsChargeTower => chargeTower || towerType == RougeTowerType.ChargeTower;
    public bool IsReinforcementTower => reinforcementTower ||
                                        towerType == RougeTowerType.ReinforcementTower;
    public bool IsSpecialTower => IsChargeTower || IsReinforcementTower;
    public bool IsTargetedDamage => isTargetedDamage;
    public RougeTowerTargetPriority TargetPriority => targetPriority;
    public RougeIceTowerBranch IceBranch => towerType == RougeTowerType.Ice
        ? iceBranch
        : RougeIceTowerBranch.None;
    public RougeIceTowerAugment IceAugment => towerType == RougeTowerType.Ice
        ? iceAugment
        : RougeIceTowerAugment.None;
    public bool NeedsIceBranchChoice => towerType == RougeTowerType.Ice &&
                                        iceBranch == RougeIceTowerBranch.None;
    public bool UsesIceFreeze => towerType == RougeTowerType.Ice &&
                                 iceBranch == RougeIceTowerBranch.Freeze;
    public bool UsesIceVulnerability => towerType == RougeTowerType.Ice &&
                                        iceBranch == RougeIceTowerBranch.Vulnerability;
    public bool CreatesIceSpikes => UsesIceFreeze &&
                                    iceAugment == RougeIceTowerAugment.IceSpikes;
    public bool CreatesPermanentFrostTiles => UsesIceFreeze &&
        iceAugment == RougeIceTowerAugment.PermanentFrostTiles;
    public bool AmplifiesVulnerableDamage => UsesIceVulnerability &&
        iceAugment == RougeIceTowerAugment.VulnerabilityDamage;
    public bool ReducesVulnerableArmor => UsesIceVulnerability &&
        iceAugment == RougeIceTowerAugment.VulnerabilityArmor;
    public RougeMachineGunBranch MachineGunBranch => towerType == RougeTowerType.MachineGun
        ? machineGunBranch
        : RougeMachineGunBranch.None;
    public RougeMachineGunAugment MachineGunAugment => towerType == RougeTowerType.MachineGun
        ? machineGunAugment
        : RougeMachineGunAugment.None;
    public bool RequiresUpgradeChoice => CanUpgrade &&
        (towerType == RougeTowerType.Ice || towerType == RougeTowerType.MachineGun ||
         towerType == RougeTowerType.Cannon);
    public bool NeedsMachineGunBranchChoice => towerType == RougeTowerType.MachineGun &&
                                               machineGunBranch == RougeMachineGunBranch.None;
    public bool UsesMachineGunCritical => towerType == RougeTowerType.MachineGun &&
                                          machineGunBranch == RougeMachineGunBranch.Critical;
    public bool UsesMachineGunFragments => towerType == RougeTowerType.MachineGun &&
                                           machineGunBranch == RougeMachineGunBranch.Fragments;
    public bool HasUpgradedCriticalChance => UsesMachineGunCritical &&
        machineGunAugment == RougeMachineGunAugment.CriticalChance;
    public bool HasCriticalArmorPenetration => UsesMachineGunCritical &&
        machineGunAugment == RougeMachineGunAugment.CriticalArmorPenetration;
    public bool HasUpgradedFragmentCount => UsesMachineGunFragments &&
        machineGunAugment == RougeMachineGunAugment.FragmentCount;
    public bool UsesEmbeddedFragments => UsesMachineGunFragments &&
        machineGunAugment == RougeMachineGunAugment.EmbeddedFragments;
    public RougeCannonBranch CannonBranch => towerType == RougeTowerType.Cannon
        ? cannonBranch
        : RougeCannonBranch.None;
    public RougeCannonAugment CannonAugment => towerType == RougeTowerType.Cannon
        ? cannonAugment
        : RougeCannonAugment.None;
    public bool NeedsCannonBranchChoice => towerType == RougeTowerType.Cannon &&
                                           cannonBranch == RougeCannonBranch.None;
    public bool UsesCannonInnerBlast => towerType == RougeTowerType.Cannon &&
                                        cannonBranch == RougeCannonBranch.InnerBlast;
    public bool UsesPersistentCannonShell => towerType == RougeTowerType.Cannon &&
                                             cannonBranch == RougeCannonBranch.PersistentShell;
    public bool HasUpgradedCannonInnerBlast => UsesCannonInnerBlast &&
        cannonAugment == RougeCannonAugment.InnerBlastArea;
    public bool HasCannonSecondaryBombardment => UsesCannonInnerBlast &&
        cannonAugment == RougeCannonAugment.SecondaryBombardment;
    public bool HasPersistentCannonKnockback => UsesPersistentCannonShell &&
        cannonAugment == RougeCannonAugment.PersistentKnockback;
    public bool HasUpgradedPersistentCannonTicks => UsesPersistentCannonShell &&
        cannonAugment == RougeCannonAugment.PersistentExtraTicks;
    public float AttackSpeedMultiplier => GetBuffMultiplier(RougeTowerBuffStat.AttackSpeed);
    public bool IsOverclocked => overclockRemaining > 0f;
    public int ReinforcementAuraBuffLevel => IsReinforcementTower
        ? TowerDefenseVisuals.GetReinforcementAuraBuffLevel()
        : 0;
    public int ReinforcementAuraRangeCells => IsReinforcementTower
        ? TowerDefenseVisuals.GetReinforcementAuraRangeCells()
        : 0;
    public bool HasChargeTargetCell => IsChargeTower && hasChargeTargetCell;
    public Vector2Int ChargeTargetCell => chargeTargetCell;
    public RougeTowerPlaceEffect ChargedTileEffect =>
        RougeTowerPlaceEffectRules.NormalizeLegacy(chargedTileEffect);
    public RougeTowerPlaceEffect TowerPlaceEffect =>
        RougeTowerPlaceEffectRules.NormalizeLegacy(towerPlaceEffect);
    public bool IsOnFrostTile => TowerPlaceEffect == RougeTowerPlaceEffect.Frost;
    public bool RepeatsAttackFromEcho => towerPlaceEffect == RougeTowerPlaceEffect.Echo &&
        !UsesEchoBarrageMultiplier;
    public bool AllowsSellRefund => RougeTowerPlaceEffectRules.AllowsSellRefund(towerPlaceEffect);
    public int KillGoldPercentBonus =>
        RougeTowerPlaceEffectRules.GetKillGoldPercentBonus(towerPlaceEffect);
    public bool CanRelocate => RougeTowerPlaceEffectRules.EnablesRelocation(towerPlaceEffect);
    public bool TriggersExplosionOnKill => towerPlaceEffect == RougeTowerPlaceEffect.Explosion;
    public int RelocationCost => RougeTowerPlaceEffectRules.GetRelocationGoldCost(investedGold);
    public int UpgradeCost => CanUpgrade
        ? level >= 2 && (NeedsIceBranchChoice || NeedsMachineGunBranchChoice ||
                         NeedsCannonBranchChoice)
            ? 0
            : ScaleUpgradeGoldCost(TowerDefenseVisuals.GetLevelGoldCost(towerType, level + 1))
        : 0;
    public string DisplayName => IsChargeTower
        ? "充能塔"
        : IsReinforcementTower
            ? "强化塔"
            : TowerDefenseVisuals.GetTowerName(towerType);

    private bool UsesEchoBarrageMultiplier => towerPlaceEffect == RougeTowerPlaceEffect.Echo &&
        (towerType == RougeTowerType.MachineGun || towerType == RougeTowerType.Laser ||
         towerType == RougeTowerType.RocketBarrage);

    internal bool EchoAttackCycleActive => echoAttackCycleActive;
    internal bool EchoAttackRepeatPending => echoAttackCycleActive &&
        echoAttackRepeatDelayRemaining >= 0f;

    internal void BeginEchoAttackCycle(Vector3 target)
    {
        echoAttackCycleActive = true;
        echoAttackRepeatsRemaining = 1;
        echoAttackTarget = target;
        echoAttackRepeatDelayRemaining = -1f;
        attackTimer = float.PositiveInfinity;
    }

    internal bool TryScheduleEchoAttackRepeat(float delay)
    {
        if (!echoAttackCycleActive || echoAttackRepeatsRemaining <= 0) return false;
        echoAttackRepeatsRemaining--;
        echoAttackRepeatDelayRemaining = Mathf.Max(0f, delay);
        return true;
    }

    internal bool TickEchoAttackRepeatDelay(float dt, out Vector3 target)
    {
        target = echoAttackTarget;
        if (!EchoAttackRepeatPending) return false;
        echoAttackRepeatDelayRemaining -= Mathf.Max(0f, dt);
        if (echoAttackRepeatDelayRemaining > 0f) return false;
        echoAttackRepeatDelayRemaining = -1f;
        return true;
    }

    internal void FinishEchoAttackCycle()
    {
        echoAttackCycleActive = false;
        echoAttackRepeatsRemaining = 0;
        echoAttackRepeatDelayRemaining = -1f;
        attackTimer = AttackInterval;
        targetIndex = -1;
    }

    private void ResetEchoAttackCycle()
    {
        bool hadDeferredCooldown = echoAttackCycleActive && float.IsPositiveInfinity(attackTimer);
        echoAttackCycleActive = false;
        echoAttackRepeatsRemaining = 0;
        echoAttackTarget = default;
        echoAttackRepeatDelayRemaining = -1f;
        if (hadDeferredCooldown) attackTimer = AttackInterval;
    }

    internal void Configure(RougeTowerType type, bool preview)
    {
        towerType = type;
        chargeTower = type == RougeTowerType.ChargeTower;
        chargeTowerPlacementCost = chargeTower ? DefaultChargeTowerPlacementCost : 0;
        reinforcementTower = type == RougeTowerType.ReinforcementTower;
        reinforcementTowerPlacementCost = reinforcementTower
            ? DefaultReinforcementTowerPlacementCost
            : 0;
        level = 1;
        iceBranch = RougeIceTowerBranch.None;
        iceAugment = RougeIceTowerAugment.None;
        machineGunBranch = RougeMachineGunBranch.None;
        machineGunAugment = RougeMachineGunAugment.None;
        cannonBranch = RougeCannonBranch.None;
        cannonAugment = RougeCannonAugment.None;
        iceSpikeTimer = 0f;
        towerPlaceEffect = RougeTowerPlaceEffect.None;
        hasChargeTargetCell = false;
        chargeTargetCell = default;
        chargedTileEffect = RougeTowerPlaceEffect.None;
        towerPlaceInitialLevelApplied = IsSpecialTower;
        towerPlaceLevelBonusReceived = false;
        SetBuffSource(RougeTowerBuffSource.TowerPlace, default);
        TowerDefenseVisuals.GetBaseStats(type, out _, out _, out _, out placementRadius, out purchaseCost);
        if (chargeTower) chargeTowerPlacementCost = purchaseCost;
        if (reinforcementTower) reinforcementTowerPlacementCost = purchaseCost;
        isTargetedDamage = !IsSpecialTower && GetDefaultTargetedDamage(type);
        investedGold = preview ? 0 : purchaseCost;
        attackTimer = IsSpecialTower
            ? float.MaxValue
            : type == RougeTowerType.Laser
                ? 0f
                : AttackInterval * 0.25f;
        targetIndex = -1;
        projectileBurstShotsRemaining = 0;
        projectileBurstShotIndex = 0;
        projectileBurstTimer = 0f;
        projectileBurstPrimaryTargetIndex = -1;
        ResetEchoAttackCycle();
        RefreshFocusedModeBuff();
        CacheEditHintRadii();
        InitializePrefabVisuals(preview);
    }

    internal void ConfigureAsChargeTower(bool preview)
    {
        Configure(RougeTowerType.ChargeTower, preview);
    }

    internal void ConfigureAsReinforcementTower(bool preview)
    {
        Configure(RougeTowerType.ReinforcementTower, preview);
    }

    internal void SetChargeTowerPlacementCost(int cost)
    {
        if (IsChargeTower) chargeTowerPlacementCost = Mathf.Max(0, cost);
    }

    internal void SetReinforcementTowerPlacementCost(int cost)
    {
        if (IsReinforcementTower) reinforcementTowerPlacementCost = Mathf.Max(0, cost);
    }

    internal void SetChargeTarget(Vector2Int targetCell, RougeTowerPlaceEffect effect)
    {
        if (!IsChargeTower) return;
        hasChargeTargetCell = true;
        chargeTargetCell = targetCell;
        chargedTileEffect = RougeTowerPlaceEffectRules.NormalizeLegacy(effect);
    }

    internal void ClearChargeTarget()
    {
        hasChargeTargetCell = false;
        chargeTargetCell = default;
        chargedTileEffect = RougeTowerPlaceEffect.None;
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
        SyncSpecialTowerType();
        ClampLevelAndSpecializations();
        TowerDefenseVisuals.GetBaseStats(towerType, out _, out _, out _, out placementRadius, out purchaseCost);
        if (IsChargeTower) chargeTowerPlacementCost = purchaseCost;
        if (IsReinforcementTower) reinforcementTowerPlacementCost = purchaseCost;
        isTargetedDamage = !IsSpecialTower && GetDefaultTargetedDamage(towerType);
        investedGold = Mathf.Max(investedGold, PlacementCost);
        RefreshFocusedModeBuff();
        CacheEditHintRadii();
        InitializePrefabVisuals(false);
    }

    internal void ApplyTowerPlaceEffect(RougeTowerPlaceEffect effect, bool existingTower = false)
    {
        if (IsChargeTower) effect = RougeTowerPlaceEffect.None;
        effect = RougeTowerPlaceEffectRules.NormalizeLegacy(effect);
        towerPlaceEffect = effect;
        SetBuffSource(RougeTowerBuffSource.TowerPlace,
            RougeTowerPlaceEffectRules.GetBuffLevels(effect));
        RefreshTowerPlacePresentation();
        if (!existingTower) return;
        ApplyTowerPlaceInitialLevelBonus();
        investedGold = Mathf.Max(investedGold, PlacementCost);
    }

    internal void ApplyActivatedTowerPlaceEffect(RougeTowerPlaceEffect effect)
    {
        if (IsChargeTower) return;
        effect = RougeTowerPlaceEffectRules.NormalizeLegacy(effect);
        RougeTowerPlaceEffect previousEffect = towerPlaceEffect;
        if (previousEffect == RougeTowerPlaceEffect.Echo && effect != RougeTowerPlaceEffect.Echo)
            ResetEchoAttackCycle();
        towerPlaceEffect = effect;
        SetBuffSource(RougeTowerBuffSource.TowerPlace,
            RougeTowerPlaceEffectRules.GetBuffLevels(effect));
        RefreshTowerPlacePresentation();
        if (previousEffect == effect || towerPlaceLevelBonusReceived ||
            RougeTowerPlaceEffectRules.GetInitialLevelBonus(effect) <= 0) return;
        level = Mathf.Clamp(level + RougeTowerPlaceEffectRules.GetInitialLevelBonus(effect), 1, MaxLevel);
        towerPlaceLevelBonusReceived = true;
        attackTimer = towerType == RougeTowerType.Laser ? 0f : EffectiveAttackInterval * 0.25f;
        targetIndex = -1;
    }

    internal void FinalizeRelocation(RougeTowerPlaceEffect destinationEffect)
    {
        towerPlaceEffect = destinationEffect;
        SetBuffSource(RougeTowerBuffSource.TowerPlace,
            RougeTowerPlaceEffectRules.GetBuffLevels(destinationEffect));
        RefreshTowerPlacePresentation();
        attackTimer = towerType == RougeTowerType.Laser ? 0f : EffectiveAttackInterval * 0.25f;
        targetIndex = -1;
        projectileBurstShotsRemaining = 0;
        projectileBurstShotIndex = 0;
        projectileBurstTimer = 0f;
        projectileBurstPrimaryTargetIndex = -1;
        ResetEchoAttackCycle();
        HideLaserBeams();
    }

    internal void SetPreviewState(bool valid, bool[] cellValidity = null)
    {
        TowerDefenseVisuals.SetRenderersTransparent(gameObject, true,
            IsSpecialTower
                ? Color.white
                : valid
                ? new Color(0.12f, 1f, 0.3f, 0.68f)
                : new Color(1f, 0.13f, 0.09f, 0.76f));
        SetRangeVisibility(true, valid);
    }

    internal bool Upgrade()
    {
        if (!CanUpgrade || RequiresUpgradeChoice) return false;
        int cost = UpgradeCost;
        level++;
        investedGold += cost;
        ResetCombatAfterLevelChange();
        return true;
    }

    internal bool UpgradeSpecializationChoice(int choiceIndex)
    {
        if (!RequiresUpgradeChoice || (uint)choiceIndex > 1u) return false;
        int cost = UpgradeCost;
        if (towerType == RougeTowerType.Ice)
        {
            if (NeedsIceBranchChoice)
            {
                iceBranch = choiceIndex == 0
                    ? RougeIceTowerBranch.Freeze
                    : RougeIceTowerBranch.Vulnerability;
                // A free-level tile can create a level-2 tower before its route is
                // selected. Completing the missing choice must not add another level.
                if (level < 2) level++;
            }
            else
            {
                iceAugment = iceBranch == RougeIceTowerBranch.Freeze
                    ? choiceIndex == 0
                        ? RougeIceTowerAugment.IceSpikes
                        : RougeIceTowerAugment.PermanentFrostTiles
                    : choiceIndex == 0
                        ? RougeIceTowerAugment.VulnerabilityDamage
                        : RougeIceTowerAugment.VulnerabilityArmor;
                level++;
            }
        }
        else if (towerType == RougeTowerType.MachineGun)
        {
            if (NeedsMachineGunBranchChoice)
            {
                machineGunBranch = choiceIndex == 0
                    ? RougeMachineGunBranch.Critical
                    : RougeMachineGunBranch.Fragments;
                if (level < 2) level++;
            }
            else
            {
                machineGunAugment = machineGunBranch == RougeMachineGunBranch.Critical
                    ? choiceIndex == 0
                        ? RougeMachineGunAugment.CriticalChance
                        : RougeMachineGunAugment.CriticalArmorPenetration
                    : choiceIndex == 0
                        ? RougeMachineGunAugment.FragmentCount
                        : RougeMachineGunAugment.EmbeddedFragments;
                level++;
            }
        }
        else
        {
            if (NeedsCannonBranchChoice)
            {
                cannonBranch = choiceIndex == 0
                    ? RougeCannonBranch.InnerBlast
                    : RougeCannonBranch.PersistentShell;
                if (level < 2) level++;
            }
            else
            {
                cannonAugment = cannonBranch == RougeCannonBranch.InnerBlast
                    ? choiceIndex == 0
                        ? RougeCannonAugment.InnerBlastArea
                        : RougeCannonAugment.SecondaryBombardment
                    : choiceIndex == 0
                        ? RougeCannonAugment.PersistentKnockback
                        : RougeCannonAugment.PersistentExtraTicks;
                level++;
            }
        }
        investedGold += cost;
        iceSpikeTimer = 0f;
        ResetCombatAfterLevelChange();
        return true;
    }

    internal void SetReinforcementAuraLevel(int auraLevel)
    {
        int levelBonus = Mathf.Max(0, auraLevel);
        SetBuffSource(RougeTowerBuffSource.ReinforcementAura,
            new RougeTowerBuffLevels(levelBonus, levelBonus, levelBonus));
    }

    private void ResetCombatAfterLevelChange()
    {
        targetIndex = -1;
        projectileBurstShotsRemaining = 0;
        projectileBurstShotIndex = 0;
        projectileBurstTimer = 0f;
        projectileBurstPrimaryTargetIndex = -1;
        ResetEchoAttackCycle();
        if (attackRing != null && attackRing.enabled) SetRangeVisibility(true);
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
        if (IsChargeTower)
        {
            if (attackRing != null) attackRing.enabled = false;
            return;
        }
        if (attackRing != null)
            attackRing.widthMultiplier = valid ? 0.12f : 0.18f;
        Color rangeColor = valid
            ? new Color(0.12f, 0.82f, 1f, 0.88f)
            : new Color(1f, 0.14f, 0.1f, 0.82f);
        if (IsReinforcementTower)
        {
            RougeTowerDefenseMap map = RougeTowerDefenseMapLoader.ActiveMap;
            Vector2Int ownedCell = default;
            bool hasOwnedCell = map != null &&
                map.WorldToCell(transform.position, out ownedCell);
            Vector3 center = hasOwnedCell
                ? map.CellCenter(ownedCell, transform.position.y)
                : transform.position;
            float halfExtent = hasOwnedCell
                ? (ReinforcementAuraRangeCells + 0.5f) * map.CellSize
                : 0f;
            TowerDefenseVisuals.UpdateSquareOutline(attackRing, center,
                halfExtent, rangeColor, visible && hasOwnedCell);
            return;
        }
        TowerDefenseVisuals.UpdateCircle(attackRing, transform.position, AttackRange,
            rangeColor, visible);
    }

    internal void SetEditHintState(bool editMode, bool selected, bool upgradeAvailable,
        bool showAllAttackRanges)
    {
        bool showSelected = editMode && selected;
        bool showUpgradeable = editMode && upgradeAvailable && !selected;
        if (showSelected || showUpgradeable) EnsureEditHintVisuals();

        // Placed tower ranges belong to the active edit selection only. Placement
        // previews are not in _defenseTowers and keep using SetPreviewState instead.
        SetRangeVisibility(editMode && (showSelected || showAllAttackRanges));

        if (selectedHintEffect != null)
        {
            selectedHintEffect.SetActive(showSelected);
            selectedHintEffect.transform.localScale = new Vector3(
                selectedHintRadius * 2.35f, selectedHintRadius * 2.35f, 1f);
        }
        if (upgradeHintEffect != null)
        {
            upgradeHintEffect.SetActive(showUpgradeable);
            upgradeHintEffect.transform.localScale = new Vector3(
                upgradeHintRadius * 2.05f, upgradeHintRadius * 2.05f, 1f);
        }
    }

    private void CacheEditHintRadii()
    {
        Vector2Int footprint = FootprintCells;
        float placementCellSize = RougeTowerDefenseMapLoader.ActiveMap != null
            ? RougeTowerDefenseMapLoader.ActiveMap.CellSize
            : 1f;
        float squareSide = Mathf.Max(footprint.x, footprint.y) *
            Mathf.Max(0.001f, placementCellSize);
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
        float widthPulse = 0.92f + Mathf.Sin(Time.time * 9.5f) * 0.08f;
        float halfWidth = OrbitLaserBeamWidth * widthPulse * 0.5f;
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
        laserBeamMesh.SetUVs(0, laserRibbonUvs, 0, vertexCount);
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

    internal Vector3 GetCurrentAimDirection()
    {
        if (billboard != null && billboard.TryGetWorldDirection(out Vector3 direction))
            return direction;
        Vector3 fallback = transform.forward;
        fallback.y = 0f;
        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
    }

    internal Vector3 GetShootPosition()
    {
        return billboard != null ? billboard.ShootPosition : transform.position + Vector3.up * 3f;
    }

    internal void PlayAttackAnimation(System.Action onShotFired)
    {
        // Rocket barrage plays one longer cue when the whole salvo starts. Its per-missile
        // animation calls pass null and retain the original allocation profile.
        if (towerType == RougeTowerType.RocketBarrage && onShotFired == null)
        {
            if (billboard != null) billboard.PlayShootAnimation(null);
            return;
        }

        System.Action releaseCue = null;
        if (towerType != RougeTowerType.RocketBarrage)
        {
            if (playAttackSoundReleaseCue == null)
                playAttackSoundReleaseCue = PlayAttackSound;
            releaseCue = playAttackSoundReleaseCue;
        }

        if (billboard != null) billboard.PlayShootAnimation(releaseCue, onShotFired);
        else RougeBillboard.InvokeReleaseCallbacks(releaseCue, onShotFired);
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
        AppendBuffDisplay(ref text, "伤害", RougeTowerBuffStat.Damage);
        AppendBuffDisplay(ref text, "范围", RougeTowerBuffStat.Range);
        AppendBuffDisplay(ref text, "攻速", RougeTowerBuffStat.AttackSpeed);
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
        SyncSpecialTowerType();
        ClampLevelAndSpecializations();
        isTargetedDamage = !IsSpecialTower && GetDefaultTargetedDamage(towerType);
        RefreshFocusedModeBuff();
    }

    private void ClampLevelAndSpecializations()
    {
        level = Mathf.Clamp(level, 1, TowerDefenseVisuals.MaxTowerLevel);
        if (towerType != RougeTowerType.Ice)
        {
            iceBranch = RougeIceTowerBranch.None;
            iceAugment = RougeIceTowerAugment.None;
        }
        else
        {
            // Pre-branch saves may contain an old Lv3-Lv5 ice tower. Bring it to the
            // route-selection stage so the player still receives both required choices.
            if (iceBranch == RougeIceTowerBranch.None && level >= 3) level = 2;
            if (level < 2) iceBranch = RougeIceTowerBranch.None;
            if (level < 3) iceAugment = RougeIceTowerAugment.None;
            if (iceBranch == RougeIceTowerBranch.Freeze &&
                iceAugment > RougeIceTowerAugment.PermanentFrostTiles)
                iceAugment = RougeIceTowerAugment.None;
            if (iceBranch == RougeIceTowerBranch.Vulnerability &&
                iceAugment != RougeIceTowerAugment.None &&
                iceAugment < RougeIceTowerAugment.VulnerabilityDamage)
                iceAugment = RougeIceTowerAugment.None;
        }

        if (towerType != RougeTowerType.MachineGun)
        {
            machineGunBranch = RougeMachineGunBranch.None;
            machineGunAugment = RougeMachineGunAugment.None;
        }
        else
        {
            // Old saves can contain a level-3 machine gun with no route selected.
            if (machineGunBranch == RougeMachineGunBranch.None && level >= 3) level = 2;
            if (level < 2) machineGunBranch = RougeMachineGunBranch.None;
            if (level < 3) machineGunAugment = RougeMachineGunAugment.None;
            if (machineGunBranch == RougeMachineGunBranch.Critical &&
                machineGunAugment > RougeMachineGunAugment.CriticalArmorPenetration)
                machineGunAugment = RougeMachineGunAugment.None;
            if (machineGunBranch == RougeMachineGunBranch.Fragments &&
                machineGunAugment != RougeMachineGunAugment.None &&
                machineGunAugment < RougeMachineGunAugment.FragmentCount)
                machineGunAugment = RougeMachineGunAugment.None;
        }

        if (towerType != RougeTowerType.Cannon)
        {
            cannonBranch = RougeCannonBranch.None;
            cannonAugment = RougeCannonAugment.None;
        }
        else
        {
            if (cannonBranch == RougeCannonBranch.None && level >= 3) level = 2;
            if (level < 2) cannonBranch = RougeCannonBranch.None;
            if (level < 3) cannonAugment = RougeCannonAugment.None;
            if (cannonBranch == RougeCannonBranch.InnerBlast &&
                cannonAugment > RougeCannonAugment.SecondaryBombardment)
                cannonAugment = RougeCannonAugment.None;
            if (cannonBranch == RougeCannonBranch.PersistentShell &&
                cannonAugment != RougeCannonAugment.None &&
                cannonAugment < RougeCannonAugment.PersistentKnockback)
                cannonAugment = RougeCannonAugment.None;
        }
    }

    private void SyncSpecialTowerType()
    {
        // Migrate any scene/prefab saved by the earlier flag-only implementation.
        if (towerType == RougeTowerType.OrbitSphere)
        {
            if (chargeTower) towerType = RougeTowerType.ChargeTower;
            else if (reinforcementTower) towerType = RougeTowerType.ReinforcementTower;
        }

        chargeTower = towerType == RougeTowerType.ChargeTower;
        reinforcementTower = towerType == RougeTowerType.ReinforcementTower;
        if (chargeTower && chargeTowerPlacementCost <= 0)
            chargeTowerPlacementCost = DefaultChargeTowerPlacementCost;
        if (reinforcementTower && reinforcementTowerPlacementCost <= 0)
            reinforcementTowerPlacementCost = DefaultReinforcementTowerPlacementCost;
    }

    private void RefreshFocusedModeBuff()
    {
        bool focused = targetPriority == RougeTowerTargetPriority.BossFirst;
        int damageLevel = 0;
        int attackSpeedLevel = 0;
        if (focused)
        {
            if (towerType == RougeTowerType.Laser)
            {
                damageLevel = -1;
                attackSpeedLevel = -1;
            }
            else if (towerType == RougeTowerType.MachineGun)
            {
                damageLevel = -1;
                attackSpeedLevel = -1;
            }
            else if (towerType == RougeTowerType.Flame)
            {
                damageLevel = -2;
            }
        }
        SetBuffSource(RougeTowerBuffSource.FocusedMode,
            new RougeTowerBuffLevels(damageLevel, 0, attackSpeedLevel));
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
        text += $"{label} Lv{(level > 0 ? "+" : string.Empty)}{level}";
    }

    private void ApplyTowerPlaceInitialLevelBonus()
    {
        if (towerPlaceInitialLevelApplied) return;
        towerPlaceInitialLevelApplied = true;
        int levelBonus = RougeTowerPlaceEffectRules.GetInitialLevelBonus(towerPlaceEffect);
        if (towerPlaceLevelBonusReceived) levelBonus = 0;
        level = Mathf.Clamp(level + levelBonus, 1, MaxLevel);
        if (levelBonus > 0)
        {
            towerPlaceLevelBonusReceived = true;
            attackTimer = towerType == RougeTowerType.Laser ? 0f : EffectiveAttackInterval * 0.25f;
        }
        targetIndex = -1;
    }

    private int ScaleUpgradeGoldCost(int baseCost)
    {
        return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, baseCost) *
            RougeTowerPlaceEffectRules.GetUpgradeGoldCostMultiplier(towerPlaceEffect)));
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
            if (!towerVisualBaseScaleCached)
            {
                towerVisualBaseScale = billboard.transform.localScale;
                towerVisualBaseScaleCached = true;
            }
        }

        RefreshTowerPlacePresentation();

        Collider towerCollider = GetComponent<Collider>();
        if (towerCollider != null) towerCollider.enabled = false;

        if (collisionRing == null) collisionRing = TowerDefenseVisuals.CreateCircleRenderer("Placement Range", transform);
        if (attackRing == null) attackRing = TowerDefenseVisuals.CreateCircleRenderer("Attack Range", transform);
        SetRangeVisibility(preview);
    }

    private void RefreshTowerPlacePresentation()
    {
        CacheEditHintRadii();
        if (billboard == null) return;
        if (!towerVisualBaseScaleCached)
        {
            towerVisualBaseScale = billboard.transform.localScale;
            towerVisualBaseScaleCached = true;
        }
        billboard.transform.localScale = towerVisualBaseScale;
    }

    private void EnsureEditHintVisuals()
    {
        if (selectedHintEffect == null)
            selectedHintEffect = TowerDefenseVisuals.CreateTowerEditIndicator(
                "Tower Selected Shader Effect", transform, true);
        if (upgradeHintEffect == null)
            upgradeHintEffect = TowerDefenseVisuals.CreateTowerEditIndicator(
                "Tower Upgrade Ready Shader Effect", transform, false);
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
            laserRibbonUvs[vertex] = new Vector2(0f, 0f);
            laserRibbonUvs[vertex + 1] = new Vector2(0f, 1f);
            laserRibbonUvs[vertex + 2] = new Vector2(1f, 0f);
            laserRibbonUvs[vertex + 3] = new Vector2(1f, 1f);
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
        StopAttackSounds();
        ReleaseLaserBeamMesh();
        if (overclockParticleMaterial != null)
        {
            if (Application.isPlaying) Destroy(overclockParticleMaterial);
            else DestroyImmediate(overclockParticleMaterial);
        }
    }

    private static bool GetDefaultTargetedDamage(RougeTowerType type)
    {
        return type != RougeTowerType.Ice && type != RougeTowerType.OrbitSphere &&
               type != RougeTowerType.RocketBarrage &&
               !TowerDefenseVisuals.IsSpecialTowerType(type);
    }
}
