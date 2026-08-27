using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum RougeLevelVictoryConditionType
{
    [InspectorName("击杀 Boss")]
    KillBoss = 0,
    [InspectorName("击杀指定数量敌人")]
    KillEnemies = 1,
    [InspectorName("生存指定时间")]
    SurviveSeconds = 2,
    [InspectorName("消灭全部敌人")]
    KillAllEnemies = 3,
    [InspectorName("累计获得金币")]
    EarnGold = 4
}

public enum RougeTowerPlaceEffect
{
    [InspectorName("0 - 无效果")]
    None = 0,
    [InspectorName("1 - 伤害 Lv+3，范围 Lv-2，升级金币消耗 +25%")]
    DamageAmplifier = 1,
    [InspectorName("2 - 范围 Lv+3，伤害 Lv-1，攻速 Lv-1，升级金币消耗 +25%")]
    RangeAmplifier = 2,
    [InspectorName("3 - 攻速 Lv+3，范围 Lv-2，升级金币消耗 +25%")]
    AttackSpeedAmplifier = 3,
    [InspectorName("4 - 全属性 Lv+2，升级金币消耗 +50%")]
    PremiumAmplifier = 4,
    [InspectorName("5 - 全属性 Lv-1，初始等级 Lv+1，出售无金币")]
    FreeLevelNoRefund = 5,
    [InspectorName("6 - 全属性 Lv-2，击杀金币 +50%")]
    Bounty = 6,
    [InspectorName("7 - 全属性 Lv-1，升级金币消耗 -25%")]
    Discount = 7,
    [InspectorName("8 - 全属性 Lv-1，可花费累计投入的 33% 搬运")]
    Relocation = 8,
    [InspectorName("9 - 全属性 Lv-2，升级金币消耗 +50%，攻击回响")]
    Echo = 9,
    [InspectorName("12 - 全属性 Lv-1，升级金币消耗 -25%，击杀金币每 30 秒按 150% 结算")]
    AccumulatedWealth = 12,
    [InspectorName("13 - 范围 Lv-2，升级金币消耗 +50%，击杀时 3% 概率爆炸")]
    Explosion = 13,
    [InspectorName("14 - 霜寒格：攻击附带减速，冰塔延长对应状态")]
    Frost = 14
}

public static class RougeTowerPlaceEffectRules
{
    public static RougeTowerPlaceEffect NormalizeLegacy(RougeTowerPlaceEffect effect)
    {
        // Legacy value 15 is folded into the single remaining frost effect.
        return (int)effect == 15 ? RougeTowerPlaceEffect.Frost : effect;
    }

    public static RougeTowerBuffLevels GetBuffLevels(RougeTowerPlaceEffect effect)
    {
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier: return new RougeTowerBuffLevels(3, -2, 0);
            case RougeTowerPlaceEffect.RangeAmplifier: return new RougeTowerBuffLevels(-1, 3, -1);
            case RougeTowerPlaceEffect.AttackSpeedAmplifier: return new RougeTowerBuffLevels(0, -2, 3);
            case RougeTowerPlaceEffect.PremiumAmplifier: return new RougeTowerBuffLevels(2, 2, 2);
            case RougeTowerPlaceEffect.FreeLevelNoRefund: return new RougeTowerBuffLevels(-1, -1, -1);
            case RougeTowerPlaceEffect.Bounty: return new RougeTowerBuffLevels(-1, -1, -1);
            case RougeTowerPlaceEffect.Discount: return new RougeTowerBuffLevels(-1, -1, -1);
            case RougeTowerPlaceEffect.Relocation: return new RougeTowerBuffLevels(-1, -1, -1);
            case RougeTowerPlaceEffect.Echo: return new RougeTowerBuffLevels(-2, -2, -2);
            case RougeTowerPlaceEffect.AccumulatedWealth: return new RougeTowerBuffLevels(-1, -1, -1);
            case RougeTowerPlaceEffect.Explosion: return new RougeTowerBuffLevels(0, -2, 0);
            default: return default;
        }
    }

    public static float GetUpgradeGoldCostMultiplier(RougeTowerPlaceEffect effect)
    {
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier:
            case RougeTowerPlaceEffect.RangeAmplifier:
            case RougeTowerPlaceEffect.AttackSpeedAmplifier:
                return 1.25f;
            case RougeTowerPlaceEffect.PremiumAmplifier:
            case RougeTowerPlaceEffect.Echo:
            case RougeTowerPlaceEffect.Explosion:
                return 1.5f;
            case RougeTowerPlaceEffect.Discount:
            case RougeTowerPlaceEffect.AccumulatedWealth:
                return 0.75f;
            default:
                return 1f;
        }
    }

    public static int GetInitialLevelBonus(RougeTowerPlaceEffect effect)
    {
        return effect == RougeTowerPlaceEffect.FreeLevelNoRefund ? 1 : 0;
    }

    public static bool AllowsSellRefund(RougeTowerPlaceEffect effect)
    {
        return effect != RougeTowerPlaceEffect.FreeLevelNoRefund;
    }

    public static int GetKillGoldPercentBonus(RougeTowerPlaceEffect effect)
    {
        return effect == RougeTowerPlaceEffect.Bounty ? 50 : 0;
    }

    public static bool EnablesRelocation(RougeTowerPlaceEffect effect)
    {
        return effect == RougeTowerPlaceEffect.Relocation;
    }

    public static int GetRelocationGoldCost(int investedGold)
    {
        return Mathf.Max(0, Mathf.CeilToInt(Mathf.Max(0, investedGold) * 0.33f));
    }

    public static string GetDisplayName(RougeTowerPlaceEffect effect)
    {
        effect = NormalizeLegacy(effect);
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier: return "效果 1 - 伤害强化";
            case RougeTowerPlaceEffect.RangeAmplifier: return "效果 2 - 范围强化";
            case RougeTowerPlaceEffect.AttackSpeedAmplifier: return "效果 3 - 攻速强化";
            case RougeTowerPlaceEffect.PremiumAmplifier: return "效果 4 - 全面强化";
            case RougeTowerPlaceEffect.FreeLevelNoRefund: return "效果 5 - 免费等级 / 禁止返还金币";
            case RougeTowerPlaceEffect.Bounty: return "效果 6 - 击杀赏金";
            case RougeTowerPlaceEffect.Discount: return "效果 7 - 升级折扣";
            case RougeTowerPlaceEffect.Relocation: return "效果 8 - 搬运格";
            case RougeTowerPlaceEffect.Echo: return "效果 9 - 回响地块";
            case RougeTowerPlaceEffect.AccumulatedWealth: return "效果 12 - 累计财富地块";
            case RougeTowerPlaceEffect.Explosion: return "效果 13 - 爆炸地块";
            case RougeTowerPlaceEffect.Frost: return "效果 14 - 霜寒格";
            default: return "无塔楼格特殊效果";
        }
    }

    public static string GetDescription(RougeTowerPlaceEffect effect)
    {
        effect = NormalizeLegacy(effect);
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier:
                return "伤害 +3，范围 -2；升级费用 +25%。";
            case RougeTowerPlaceEffect.RangeAmplifier:
                return "范围 +3，伤害 -1，攻速 -1；升级费用 +25%。";
            case RougeTowerPlaceEffect.AttackSpeedAmplifier:
                return "攻速 +3，范围 -2；升级费用 +25%。";
            case RougeTowerPlaceEffect.PremiumAmplifier:
                return "伤害、范围、攻速各 +2；升级费用 +50%。";
            case RougeTowerPlaceEffect.FreeLevelNoRefund:
                return "建造后立即提升 1 级；伤害、范围、攻速各 -1；出售不返还金币。";
            case RougeTowerPlaceEffect.Bounty:
                return "伤害、范围、攻速各 -1；击杀金币 +50%。";
            case RougeTowerPlaceEffect.Discount:
                return "伤害、范围、攻速各 -1；升级费用 -25%。";
            case RougeTowerPlaceEffect.Relocation:
                return "伤害、范围、攻速各 -1；花费累计投入金币的 33% 可搬运。";
            case RougeTowerPlaceEffect.Echo:
                return "伤害、范围、攻速各 -2；升级费用 +50%；每次攻击追加一次回响。";
            case RougeTowerPlaceEffect.AccumulatedWealth:
                return "伤害、范围、攻速各 -1；升级费用 -25%；击杀金币每 30 秒按 150% 结算。";
            case RougeTowerPlaceEffect.Explosion:
                return "范围 -2；升级费用 +50%；击杀时有 3% 概率引发爆炸。";
            case RougeTowerPlaceEffect.Frost:
                return "攻击附带减速；冰塔会根据升级路线延长对应状态。";
            default:
                return "此地图格没有特殊效果";
        }
    }

    public static Color GetVisualColor(RougeTowerPlaceEffect effect)
    {
        effect = NormalizeLegacy(effect);
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier: return new Color(1f, 0.22f, 0.08f, 0.38f);
            case RougeTowerPlaceEffect.RangeAmplifier: return new Color(0.08f, 0.78f, 1f, 0.38f);
            case RougeTowerPlaceEffect.AttackSpeedAmplifier: return new Color(1f, 0.78f, 0.08f, 0.38f);
            case RougeTowerPlaceEffect.PremiumAmplifier: return new Color(1f, 0.18f, 0.78f, 0.4f);
            case RougeTowerPlaceEffect.FreeLevelNoRefund: return new Color(0.52f, 0.62f, 0.78f, 0.4f);
            case RougeTowerPlaceEffect.Bounty: return new Color(1f, 0.52f, 0.04f, 0.4f);
            case RougeTowerPlaceEffect.Discount: return new Color(0.12f, 0.9f, 0.34f, 0.38f);
            case RougeTowerPlaceEffect.Relocation: return new Color(0.68f, 0.2f, 1f, 0.4f);
            case RougeTowerPlaceEffect.Echo: return new Color(0.32f, 0.4f, 1f, 0.42f);
            case RougeTowerPlaceEffect.AccumulatedWealth: return new Color(1f, 0.72f, 0.08f, 0.44f);
            case RougeTowerPlaceEffect.Explosion: return new Color(1f, 0.16f, 0.04f, 0.44f);
            case RougeTowerPlaceEffect.Frost: return new Color(0.12f, 0.72f, 1f, 0.42f);
            default: return Color.clear;
        }
    }
}

[CreateAssetMenu(fileName = "TowerDefenseMap", menuName = "Rouge/Tower Defense Map")]
public sealed class RougeTowerDefenseMap : ScriptableObject
{
    public const int MaxMapCells = 32;
    // Navigation/crowd simulation still samples terrain more finely. Tower placement is
    // cell-based and never exposes or consumes these internal simulation subdivisions.
    public const int MicroCellsPerTile = 16;
    [Serializable]
    public sealed class TileDefinition
    {
        public string name = "Tile";
        public GameObject prefab;
        [Tooltip("Use N/E/S/W neighbors to select one of 16 seamless prefab variants.")]
        public bool useAutoTile;
        [Tooltip("Tiles with the same non-empty group connect to each other. Empty means only the same tile index connects.")]
        public string autoTileGroup;
        [Tooltip("Index is neighbor mask: North=1, East=2, South=4, West=8. Missing entries fall back to Prefab.")]
        public GameObject[] autoTilePrefabs = new GameObject[16];
        public Color editorColor = Color.gray;
        public bool blocksNavigation;
        public bool towerPlace;
        [Tooltip("Special effect applied from the single terrain cell under the placed tower's center.")]
        public RougeTowerPlaceEffect towerPlaceEffect;
        [Tooltip("Optional pure-white alpha icon shown at the center of a tower-place tile. " +
                 "When empty, the placement-pad shader keeps its original center reactor circle.")]
        public Texture2D towerPlaceIcon;
        [Min(0.02f)] public float fallbackHeight = 0.2f;
        public float yOffset;
        public Vector3 prefabEulerAngles;
    }

    [Serializable]
    public sealed class EnemySpawn
    {
        public Vector2Int cell;
        [Range(1, 64)] public int spawnCount = 25;
        [Min(0.1f)] public float spawnInterval = 5f;
        [Min(0f)] public float startDelay = 1f;
        public RougeEnemyType enemyType = RougeEnemyType.Standard;
        [Tooltip("When enabled, this spawn point removes itself after the configured number of waves.")]
        public bool limitWaveCount;
        [Min(1)] public int maximumWaves = 1;
    }

    [Serializable]
    public sealed class VictoryCondition
    {
        public RougeLevelVictoryConditionType type = RougeLevelVictoryConditionType.KillBoss;
        [Min(1)] public int targetAmount = 1;
        [Min(0.1f)] public float targetSeconds = 300f;
    }

    [Serializable]
    public sealed class BossEncounter
    {
        [Tooltip("Integer Boss ID from the Tower Defense Balance JSON. IDs remain mod-friendly integers at runtime.")]
        public int bossId;
        [Min(0f), Tooltip("Game minute at which this Boss becomes eligible to spawn.")]
        public float spawnMinute = 15f;
        [Tooltip("Only grants victory when the level also contains the Kill Boss victory condition.")]
        public bool defeatGrantsVictory = true;
    }

    [Header("Grid")]
    [SerializeField, Range(1, MaxMapCells)] private int width = 32;
    [SerializeField, Range(1, MaxMapCells)] private int height = 32;
    [SerializeField, Min(0.1f)] private float cellSize = 8f;
    [SerializeField] private Vector2 origin = new Vector2(-256f, -256f);

    [Header("Tiles (index 0 is Empty)")]
    [FormerlySerializedAs("tileDefinitions"), SerializeField, HideInInspector]
    private List<TileDefinition> legacyTileDefinitions = new List<TileDefinition>();
    [SerializeField, HideInInspector] private int[] tiles = Array.Empty<int>();

    [Header("Map Objects")]
    [SerializeField] private List<EnemySpawn> enemySpawns = new List<EnemySpawn>();
    [SerializeField] private bool hasMainTower;
    [SerializeField] private Vector2Int mainTowerCell = new Vector2Int(32, 32);
    [SerializeField] private GameObject mainTowerPrefab;
    [SerializeField] private bool hasBossSpawn;
    [SerializeField] private Vector2Int bossSpawnCell = new Vector2Int(32, 58);
    [SerializeField] private GameObject bossPrefab;

    [Header("Level Rules")]
    [SerializeField] private List<VictoryCondition> victoryConditions = new List<VictoryCondition>
    {
        new VictoryCondition { type = RougeLevelVictoryConditionType.KillBoss }
    };
    [SerializeField, Tooltip("Raw tower type IDs. The editor shows known IDs as RougeTowerType values, while serialized data stays integer-based.")]
    private List<int> disabledTowerTypeIds = new List<int>();
    [SerializeField, Min(0.01f)] private float enemyHealthMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float enemyMoveSpeedMultiplier = 1f;
    [SerializeField, Min(0f)] private float towerGoldCostMultiplier = 1f;
    [SerializeField, Min(0f)] private float towerDamageMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float towerAttackSpeedMultiplier = 1f;
    [SerializeField, Min(0)] private int startingGold = 2000;
    [SerializeField] private List<BossEncounter> bossEncounters = new List<BossEncounter>
    {
        new BossEncounter()
    };

    [Header("Level Camera Clamp / Zoom")]
    [SerializeField] private bool configureCameraBounds = true;
    [SerializeField] private Vector2 cameraBoundsCenter = Vector2.zero;
    [SerializeField] private Vector2 cameraBoundsSize = new Vector2(180f, 180f);
    [SerializeField, Min(0.01f)] private float minimumCameraZoom = 0.5f;
    [SerializeField, Min(0.01f)] private float maximumCameraZoom = 5f;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public float MicroCellSize => cellSize / MicroCellsPerTile;
    public Vector2 Origin => origin;
    public IReadOnlyList<TileDefinition> TileDefinitions
    {
        get
        {
            RougeTowerDefenseTilePalette palette = RougeTowerDefenseTilePalette.Shared;
            return palette != null && palette.TileDefinitions.Count > 0
                ? palette.TileDefinitions
                : legacyTileDefinitions;
        }
    }
    public IReadOnlyList<EnemySpawn> EnemySpawns => enemySpawns;
    public bool HasMainTower => hasMainTower;
    public Vector2Int MainTowerCell => mainTowerCell;
    public GameObject MainTowerPrefab => mainTowerPrefab;
    public bool HasBossSpawn => hasBossSpawn;
    public Vector2Int BossSpawnCell => bossSpawnCell;
    public GameObject BossPrefab => bossPrefab;
    public IReadOnlyList<VictoryCondition> VictoryConditions => victoryConditions;
    public IReadOnlyList<int> DisabledTowerTypeIds => disabledTowerTypeIds;
    public float EnemyHealthMultiplier => enemyHealthMultiplier;
    public float EnemyMoveSpeedMultiplier => enemyMoveSpeedMultiplier;
    public float TowerGoldCostMultiplier => towerGoldCostMultiplier;
    public float TowerDamageMultiplier => towerDamageMultiplier;
    public float TowerAttackSpeedMultiplier => towerAttackSpeedMultiplier;
    public int StartingGold => startingGold;
    public IReadOnlyList<BossEncounter> BossEncounters => bossEncounters;
    public bool ConfigureCameraBounds => configureCameraBounds;
    public Vector2 CameraBoundsCenter => cameraBoundsCenter;
    public Vector2 CameraBoundsSize => cameraBoundsSize;
    public float MinimumCameraZoom => minimumCameraZoom;
    public float MaximumCameraZoom => maximumCameraZoom;

    public bool Contains(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

    public bool HasVictoryCondition(RougeLevelVictoryConditionType type)
    {
        if (victoryConditions == null) return false;
        for (int i = 0; i < victoryConditions.Count; i++)
        {
            if (victoryConditions[i] != null && victoryConditions[i].type == type) return true;
        }
        return false;
    }

    public bool IsTowerDisabled(int towerTypeId)
    {
        return disabledTowerTypeIds != null && disabledTowerTypeIds.Contains(towerTypeId);
    }

    public int GetTile(Vector2Int cell)
    {
        EnsureStorage();
        return Contains(cell) ? tiles[cell.y * width + cell.x] : 0;
    }

    public TileDefinition GetDefinition(int tileIndex)
    {
        IReadOnlyList<TileDefinition> definitions = TileDefinitions;
        return tileIndex >= 0 && tileIndex < definitions.Count ? definitions[tileIndex] : null;
    }

    public int GetAutoTileMask(Vector2Int cell, int tileIndex)
    {
        TileDefinition definition = GetDefinition(tileIndex);
        if (definition == null) return 0;
        int mask = 0;
        if (AutoTileConnects(definition, tileIndex, cell + Vector2Int.up)) mask |= 1;
        if (AutoTileConnects(definition, tileIndex, cell + Vector2Int.right)) mask |= 2;
        if (AutoTileConnects(definition, tileIndex, cell + Vector2Int.down)) mask |= 4;
        if (AutoTileConnects(definition, tileIndex, cell + Vector2Int.left)) mask |= 8;
        return mask;
    }

    public GameObject ResolveTilePrefab(Vector2Int cell, int tileIndex)
    {
        TileDefinition definition = GetDefinition(tileIndex);
        if (definition == null) return null;
        if (!definition.useAutoTile) return definition.prefab;
        int mask = GetAutoTileMask(cell, tileIndex);
        if (definition.autoTilePrefabs != null && mask < definition.autoTilePrefabs.Length &&
            definition.autoTilePrefabs[mask] != null)
            return definition.autoTilePrefabs[mask];
        return definition.prefab;
    }

    private bool AutoTileConnects(TileDefinition source, int sourceIndex, Vector2Int neighborCell)
    {
        if (!Contains(neighborCell)) return false;
        int neighborIndex = GetTile(neighborCell);
        if (neighborIndex == sourceIndex) return true;
        if (string.IsNullOrWhiteSpace(source.autoTileGroup)) return false;
        TileDefinition neighbor = GetDefinition(neighborIndex);
        return neighbor != null && neighbor.useAutoTile &&
               string.Equals(source.autoTileGroup, neighbor.autoTileGroup, StringComparison.Ordinal);
    }

    public void SetTile(Vector2Int cell, int tileIndex)
    {
        if (!Contains(cell)) return;
        EnsureStorage();
        tiles[cell.y * width + cell.x] = Mathf.Clamp(tileIndex, 0,
            Mathf.Max(0, TileDefinitions.Count - 1));
    }

    public bool PaintBaseTile(Vector2Int cell, int tileIndex)
    {
        TileDefinition nextDefinition = GetDefinition(tileIndex);
        bool remainsGround = tileIndex > 0 && nextDefinition != null &&
                             !nextDefinition.blocksNavigation && !nextDefinition.towerPlace;
        if (hasMainTower && mainTowerCell == cell && !remainsGround) return false;
        SetTile(cell, tileIndex);
        if (!remainsGround) RemoveUpperObjectAt(cell);
        return true;
    }

    public Vector3 CellCenter(Vector2Int cell, float y = 0f)
    {
        return new Vector3(origin.x + (cell.x + 0.5f) * cellSize, y, origin.y + (cell.y + 0.5f) * cellSize);
    }

    public EnemySpawn FindEnemySpawn(Vector2Int cell)
    {
        for (int i = 0; i < enemySpawns.Count; i++)
            if (enemySpawns[i].cell == cell) return enemySpawns[i];
        return null;
    }

    public bool IsGround(Vector2Int cell)
    {
        int tileIndex = GetTile(cell);
        TileDefinition definition = GetDefinition(tileIndex);
        return tileIndex > 0 && definition != null &&
               !definition.blocksNavigation && !definition.towerPlace;
    }

    public bool IsTowerPlace(Vector2Int cell)
    {
        int tileIndex = GetTile(cell);
        TileDefinition definition = GetDefinition(tileIndex);
        return tileIndex > 0 && definition != null && definition.towerPlace;
    }

    public RougeTowerPlaceEffect GetTowerPlaceEffect(Vector2Int cell)
    {
        int tileIndex = GetTile(cell);
        TileDefinition definition = GetDefinition(tileIndex);
        return tileIndex > 0 && definition != null && definition.towerPlace
            ? RougeTowerPlaceEffectRules.NormalizeLegacy(definition.towerPlaceEffect)
            : RougeTowerPlaceEffect.None;
    }

    public bool IsNavigationBlocked(Vector2Int cell)
    {
        if (!Contains(cell)) return true;
        int tileIndex = GetTile(cell);
        TileDefinition definition = GetDefinition(tileIndex);
        return tileIndex == 0 || definition == null || definition.blocksNavigation || definition.towerPlace;
    }

    public bool ContainsMicroCell(Vector2Int microCell)
    {
        return microCell.x >= 0 && microCell.y >= 0 &&
               microCell.x < width * MicroCellsPerTile && microCell.y < height * MicroCellsPerTile;
    }

    public bool IsTowerPlaceMicroCell(Vector2Int microCell)
    {
        if (!ContainsMicroCell(microCell)) return false;
        return IsTowerPlace(new Vector2Int(
            microCell.x / MicroCellsPerTile, microCell.y / MicroCellsPerTile));
    }

    public bool WorldToCell(Vector3 worldPosition, out Vector2Int cell)
    {
        cell = new Vector2Int(
            Mathf.FloorToInt((worldPosition.x - origin.x) / cellSize),
            Mathf.FloorToInt((worldPosition.z - origin.y) / cellSize));
        return Contains(cell);
    }

    public Vector3 FootprintCenter(Vector2Int anchor, int footprintSize, float y = 0f)
    {
        float half = footprintSize * 0.5f;
        return new Vector3(
            origin.x + (anchor.x + half) * cellSize,
            y,
            origin.y + (anchor.y + half) * cellSize);
    }

    public Vector3 MicroFootprintCenter(Vector2Int anchor, Vector2Int footprintSize, float y = 0f)
    {
        return new Vector3(
            origin.x + (anchor.x + footprintSize.x * 0.5f) * MicroCellSize,
            y,
            origin.y + (anchor.y + footprintSize.y * 0.5f) * MicroCellSize);
    }

    public Vector2Int WorldToFootprintAnchor(Vector3 worldPosition, int footprintSize)
    {
        float gridX = (worldPosition.x - origin.x) / cellSize;
        float gridY = (worldPosition.z - origin.y) / cellSize;
        int half = footprintSize / 2;
        return new Vector2Int(Mathf.RoundToInt(gridX) - half, Mathf.RoundToInt(gridY) - half);
    }

    public Vector2Int WorldToMicroFootprintAnchor(Vector3 worldPosition, Vector2Int footprintSize)
    {
        float gridX = (worldPosition.x - origin.x) / MicroCellSize;
        float gridY = (worldPosition.z - origin.y) / MicroCellSize;
        // Subtract the exact half-size before rounding so this is the true inverse
        // of MicroFootprintCenter for both even and odd footprints. Rounding the
        // center first shifts odd sizes such as the 5x5 laser tower by one micro cell.
        return new Vector2Int(
            Mathf.RoundToInt(gridX - footprintSize.x * 0.5f),
            Mathf.RoundToInt(gridY - footprintSize.y * 0.5f));
    }

    public bool WorldToMicroCell(Vector3 worldPosition, out Vector2Int microCell)
    {
        microCell = new Vector2Int(
            Mathf.FloorToInt((worldPosition.x - origin.x) / MicroCellSize),
            Mathf.FloorToInt((worldPosition.z - origin.y) / MicroCellSize));
        return ContainsMicroCell(microCell);
    }

    public bool HasUpperObject(Vector2Int cell)
    {
        return FindEnemySpawn(cell) != null ||
               (hasMainTower && mainTowerCell == cell) ||
               (hasBossSpawn && bossSpawnCell == cell);
    }

    public bool AddEnemySpawn(Vector2Int cell)
    {
        if (!IsGround(cell) || HasUpperObject(cell)) return false;
        enemySpawns.Add(new EnemySpawn { cell = cell });
        return true;
    }

    public bool MoveEnemySpawn(Vector2Int source, Vector2Int destination)
    {
        EnemySpawn spawn = FindEnemySpawn(source);
        if (spawn == null || source == destination) return false;
        if (!IsGround(destination) || HasUpperObject(destination)) return false;
        spawn.cell = destination;
        return true;
    }

    public void RemoveUpperObjectAt(Vector2Int cell)
    {
        enemySpawns.RemoveAll(spawn => spawn.cell == cell);
        if (hasBossSpawn && bossSpawnCell == cell) hasBossSpawn = false;
        // Main tower is intentionally protected from direct upper-layer deletion.
    }

    public bool EraseBaseTile(Vector2Int cell)
    {
        if (!Contains(cell)) return false;
        // Keeping the main tower undeletable also protects the tile supporting it.
        if (hasMainTower && mainTowerCell == cell) return false;
        SetTile(cell, 0);
        RemoveUpperObjectAt(cell);
        return true;
    }

    public bool PlaceMainTower(Vector2Int cell)
    {
        if (!IsGround(cell)) return false;
        if (HasUpperObject(cell) && (!hasMainTower || mainTowerCell != cell)) return false;
        hasMainTower = true;
        mainTowerCell = cell;
        return true;
    }

    public bool PlaceBossSpawn(Vector2Int cell)
    {
        if (!IsGround(cell)) return false;
        if (HasUpperObject(cell) && (!hasBossSpawn || bossSpawnCell != cell)) return false;
        hasBossSpawn = true;
        bossSpawnCell = cell;
        return true;
    }

    public void ResizeGrid(int newWidth, int newHeight, float newCellSize, bool recenter)
    {
        newWidth = Mathf.Clamp(newWidth, 1, MaxMapCells);
        newHeight = Mathf.Clamp(newHeight, 1, MaxMapCells);
        newCellSize = Mathf.Max(0.1f, newCellSize);
        int[] oldTiles = tiles;
        int oldWidth = width;
        int oldHeight = height;
        width = newWidth;
        height = newHeight;
        cellSize = newCellSize;
        tiles = new int[width * height];
        if (oldTiles != null)
        {
            int copyWidth = Mathf.Min(oldWidth, width);
            int copyHeight = Mathf.Min(oldHeight, height);
            for (int y = 0; y < copyHeight; y++)
            {
                int sourceIndex = y * oldWidth;
                if (sourceIndex >= oldTiles.Length) break;
                int available = Mathf.Min(copyWidth, oldTiles.Length - sourceIndex);
                if (available > 0) Array.Copy(oldTiles, sourceIndex, tiles, y * width, available);
            }
        }
        if (recenter) origin = new Vector2(-width * cellSize * 0.5f, -height * cellSize * 0.5f);
        enemySpawns.RemoveAll(spawn => !Contains(spawn.cell));
        mainTowerCell = ClampCell(mainTowerCell);
        bossSpawnCell = ClampCell(bossSpawnCell);
    }

    public void InitializeDefaults()
    {
        enemySpawns.Clear();
        hasMainTower = false;
        hasBossSpawn = false;
        victoryConditions = new List<VictoryCondition>
        {
            new VictoryCondition { type = RougeLevelVictoryConditionType.KillBoss }
        };
        disabledTowerTypeIds = new List<int>();
        enemyHealthMultiplier = 1f;
        enemyMoveSpeedMultiplier = 1f;
        towerGoldCostMultiplier = 1f;
        towerDamageMultiplier = 1f;
        towerAttackSpeedMultiplier = 1f;
        startingGold = 2000;
        bossEncounters = new List<BossEncounter> { new BossEncounter() };
        legacyTileDefinitions.Clear();
        legacyTileDefinitions.Add(new TileDefinition { name = "Empty", editorColor = new Color(0f, 0f, 0f, 0f) });
        legacyTileDefinitions.Add(new TileDefinition { name = "Ground", editorColor = new Color(0.2f, 0.3f, 0.38f, 0.85f), fallbackHeight = 0.08f });
        legacyTileDefinitions.Add(new TileDefinition { name = "Wall", editorColor = new Color(0.85f, 0.22f, 0.16f, 0.9f), blocksNavigation = true, fallbackHeight = 3f });
        legacyTileDefinitions.Add(new TileDefinition { name = "Tower Place", editorColor = new Color(0.18f, 0.8f, 0.42f, 0.9f), towerPlace = true, blocksNavigation = true, fallbackHeight = 0.1f });
        ResizeGrid(MaxMapCells, MaxMapCells, 8f, true);
    }

    private Vector2Int ClampCell(Vector2Int cell)
    {
        return new Vector2Int(Mathf.Clamp(cell.x, 0, width - 1), Mathf.Clamp(cell.y, 0, height - 1));
    }

    private void EnsureStorage()
    {
        if (tiles == null || tiles.Length != width * height) Array.Resize(ref tiles, width * height);
        legacyTileDefinitions ??= new List<TileDefinition>();
        enemySpawns ??= new List<EnemySpawn>();
        victoryConditions ??= new List<VictoryCondition>();
        disabledTowerTypeIds ??= new List<int>();
        bossEncounters ??= new List<BossEncounter>();
    }

    private void OnValidate()
    {
        width = Mathf.Clamp(width, 1, MaxMapCells);
        height = Mathf.Clamp(height, 1, MaxMapCells);
        cellSize = Mathf.Max(0.1f, cellSize);
        cameraBoundsSize.x = Mathf.Max(1f, cameraBoundsSize.x);
        cameraBoundsSize.y = Mathf.Max(1f, cameraBoundsSize.y);
        minimumCameraZoom = Mathf.Max(0.01f, minimumCameraZoom);
        maximumCameraZoom = Mathf.Max(minimumCameraZoom, maximumCameraZoom);
        enemyHealthMultiplier = Mathf.Max(0.01f, enemyHealthMultiplier);
        enemyMoveSpeedMultiplier = Mathf.Max(0.01f, enemyMoveSpeedMultiplier);
        towerGoldCostMultiplier = Mathf.Max(0f, towerGoldCostMultiplier);
        towerDamageMultiplier = Mathf.Max(0f, towerDamageMultiplier);
        towerAttackSpeedMultiplier = Mathf.Max(0.01f, towerAttackSpeedMultiplier);
        startingGold = Mathf.Max(0, startingGold);
        EnsureStorage();
        if (legacyTileDefinitions.Count == 0)
        {
            InitializeDefaults();
        }
        var occupiedUpperCells = new HashSet<Vector2Int>();
        if (hasMainTower)
        {
            if (IsGround(mainTowerCell)) occupiedUpperCells.Add(mainTowerCell);
            else hasMainTower = false;
        }
        if (hasBossSpawn)
        {
            if (!IsGround(bossSpawnCell) || !occupiedUpperCells.Add(bossSpawnCell)) hasBossSpawn = false;
        }
        for (int i = enemySpawns.Count - 1; i >= 0; i--)
        {
            EnemySpawn spawn = enemySpawns[i];
            if (spawn == null)
            {
                enemySpawns.RemoveAt(i);
                continue;
            }
            spawn.cell = ClampCell(spawn.cell);
            if (!IsGround(spawn.cell) || !occupiedUpperCells.Add(spawn.cell))
            {
                enemySpawns.RemoveAt(i);
                continue;
            }
            spawn.spawnCount = Mathf.Clamp(spawn.spawnCount, 1, 64);
            spawn.spawnInterval = Mathf.Max(0.1f, spawn.spawnInterval);
            spawn.startDelay = Mathf.Max(0f, spawn.startDelay);
            spawn.maximumWaves = Mathf.Max(1, spawn.maximumWaves);
        }
        for (int i = victoryConditions.Count - 1; i >= 0; i--)
        {
            VictoryCondition condition = victoryConditions[i];
            if (condition == null)
            {
                victoryConditions.RemoveAt(i);
                continue;
            }
            condition.targetAmount = Mathf.Max(1, condition.targetAmount);
            condition.targetSeconds = Mathf.Max(0.1f, condition.targetSeconds);
        }
        for (int i = bossEncounters.Count - 1; i >= 0; i--)
        {
            BossEncounter encounter = bossEncounters[i];
            if (encounter == null)
            {
                bossEncounters.RemoveAt(i);
                continue;
            }
            encounter.spawnMinute = Mathf.Max(0f, encounter.spawnMinute);
        }
        for (int i = 0; i < legacyTileDefinitions.Count; i++)
        {
            TileDefinition definition = legacyTileDefinitions[i];
            if (definition == null) continue;
            definition.autoTilePrefabs ??= new GameObject[16];
            if (definition.autoTilePrefabs.Length != 16)
                Array.Resize(ref definition.autoTilePrefabs, 16);
        }
    }
}
