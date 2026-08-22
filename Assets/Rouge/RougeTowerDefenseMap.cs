using System;
using System.Collections.Generic;
using UnityEngine;

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
    [InspectorName("1 - 伤害 +2，范围 -1，金币消耗 +25%")]
    DamageAmplifier = 1,
    [InspectorName("2 - 范围 +2，攻速 -1，金币消耗 +25%")]
    RangeAmplifier = 2,
    [InspectorName("3 - 攻速 +2，伤害 -1，金币消耗 +25%")]
    AttackSpeedAmplifier = 3,
    [InspectorName("4 - 全属性 +1，金币消耗 +50%")]
    PremiumAmplifier = 4,
    [InspectorName("5 - 全属性 -1，初始等级 +1，出售无金币")]
    FreeLevelNoRefund = 5,
    [InspectorName("6 - 全属性 -2，击杀金币 +1")]
    Bounty = 6,
    [InspectorName("7 - 全属性 -1，金币消耗 -25%")]
    Discount = 7
}

public static class RougeTowerPlaceEffectRules
{
    public static RougeTowerBuffLevels GetBuffLevels(RougeTowerPlaceEffect effect)
    {
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier: return new RougeTowerBuffLevels(2, -1, 0);
            case RougeTowerPlaceEffect.RangeAmplifier: return new RougeTowerBuffLevels(0, 2, -1);
            case RougeTowerPlaceEffect.AttackSpeedAmplifier: return new RougeTowerBuffLevels(-1, 0, 2);
            case RougeTowerPlaceEffect.PremiumAmplifier: return new RougeTowerBuffLevels(1, 1, 1);
            case RougeTowerPlaceEffect.FreeLevelNoRefund: return new RougeTowerBuffLevels(-1, -1, -1);
            case RougeTowerPlaceEffect.Bounty: return new RougeTowerBuffLevels(-2, -2, -2);
            case RougeTowerPlaceEffect.Discount: return new RougeTowerBuffLevels(-1, -1, -1);
            default: return default;
        }
    }

    public static float GetGoldCostMultiplier(RougeTowerPlaceEffect effect)
    {
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier:
            case RougeTowerPlaceEffect.RangeAmplifier:
            case RougeTowerPlaceEffect.AttackSpeedAmplifier:
                return 1.25f;
            case RougeTowerPlaceEffect.PremiumAmplifier:
                return 1.5f;
            case RougeTowerPlaceEffect.Discount:
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

    public static int GetKillGoldBonus(RougeTowerPlaceEffect effect)
    {
        return effect == RougeTowerPlaceEffect.Bounty ? 1 : 0;
    }

    public static string GetDisplayName(RougeTowerPlaceEffect effect)
    {
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier: return "效果 1 - 伤害强化";
            case RougeTowerPlaceEffect.RangeAmplifier: return "效果 2 - 范围强化";
            case RougeTowerPlaceEffect.AttackSpeedAmplifier: return "效果 3 - 攻速强化";
            case RougeTowerPlaceEffect.PremiumAmplifier: return "效果 4 - 全面强化";
            case RougeTowerPlaceEffect.FreeLevelNoRefund: return "效果 5 - 免费等级 / 禁止返还金币";
            case RougeTowerPlaceEffect.Bounty: return "效果 6 - 击杀赏金";
            case RougeTowerPlaceEffect.Discount: return "效果 7 - 金币折扣";
            default: return "无塔楼格特殊效果";
        }
    }

    public static string GetDescription(RougeTowerPlaceEffect effect)
    {
        switch (effect)
        {
            case RougeTowerPlaceEffect.DamageAmplifier:
                return "伤害 +2   范围 -1   建造/升级金币消耗 +25%";
            case RougeTowerPlaceEffect.RangeAmplifier:
                return "范围 +2   攻速 -1   建造/升级金币消耗 +25%";
            case RougeTowerPlaceEffect.AttackSpeedAmplifier:
                return "攻速 +2   伤害 -1   建造/升级金币消耗 +25%";
            case RougeTowerPlaceEffect.PremiumAmplifier:
                return "伤害 +1   范围 +1   攻速 +1   建造/升级金币消耗 +50%";
            case RougeTowerPlaceEffect.FreeLevelNoRefund:
                return "伤害 -1   范围 -1   攻速 -1   初始等级 +1   出售金币 0";
            case RougeTowerPlaceEffect.Bounty:
                return "伤害 -2   范围 -2   攻速 -2   击杀金币 +1";
            case RougeTowerPlaceEffect.Discount:
                return "伤害 -1   范围 -1   攻速 -1   建造/升级金币消耗 -25%";
            default:
                return "此地图格没有特殊效果";
        }
    }
}

[CreateAssetMenu(fileName = "TowerDefenseMap", menuName = "Rouge/Tower Defense Map")]
public sealed class RougeTowerDefenseMap : ScriptableObject
{
    public const int MaxMapCells = 32;
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
    [SerializeField] private List<TileDefinition> tileDefinitions = new List<TileDefinition>();
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
    public IReadOnlyList<TileDefinition> TileDefinitions => tileDefinitions;
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
        return tileIndex >= 0 && tileIndex < tileDefinitions.Count ? tileDefinitions[tileIndex] : null;
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
        tiles[cell.y * width + cell.x] = Mathf.Clamp(tileIndex, 0, Mathf.Max(0, tileDefinitions.Count - 1));
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
            ? definition.towerPlaceEffect
            : RougeTowerPlaceEffect.None;
    }

    public float GetMinimumTowerGoldCostMultiplier()
    {
        float minimum = 1f;
        for (int i = 0; i < tileDefinitions.Count; i++)
        {
            TileDefinition definition = tileDefinitions[i];
            if (definition == null || !definition.towerPlace) continue;
            minimum = Mathf.Min(minimum,
                RougeTowerPlaceEffectRules.GetGoldCostMultiplier(definition.towerPlaceEffect));
        }
        return minimum;
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
        return new Vector2Int(Mathf.RoundToInt(gridX) - footprintSize.x / 2,
            Mathf.RoundToInt(gridY) - footprintSize.y / 2);
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
        tileDefinitions.Clear();
        tileDefinitions.Add(new TileDefinition { name = "Empty", editorColor = new Color(0f, 0f, 0f, 0f) });
        tileDefinitions.Add(new TileDefinition { name = "Ground", editorColor = new Color(0.2f, 0.3f, 0.38f, 0.85f), fallbackHeight = 0.08f });
        tileDefinitions.Add(new TileDefinition { name = "Wall", editorColor = new Color(0.85f, 0.22f, 0.16f, 0.9f), blocksNavigation = true, fallbackHeight = 3f });
        tileDefinitions.Add(new TileDefinition { name = "Tower Place", editorColor = new Color(0.18f, 0.8f, 0.42f, 0.9f), towerPlace = true, blocksNavigation = true, fallbackHeight = 0.1f });
        ResizeGrid(MaxMapCells, MaxMapCells, 8f, true);
    }

    private Vector2Int ClampCell(Vector2Int cell)
    {
        return new Vector2Int(Mathf.Clamp(cell.x, 0, width - 1), Mathf.Clamp(cell.y, 0, height - 1));
    }

    private void EnsureStorage()
    {
        if (tiles == null || tiles.Length != width * height) Array.Resize(ref tiles, width * height);
        tileDefinitions ??= new List<TileDefinition>();
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
        if (tileDefinitions.Count == 0)
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
        for (int i = 0; i < tileDefinitions.Count; i++)
        {
            TileDefinition definition = tileDefinitions[i];
            if (definition == null) continue;
            definition.autoTilePrefabs ??= new GameObject[16];
            if (definition.autoTilePrefabs.Length != 16)
                Array.Resize(ref definition.autoTilePrefabs, 16);
        }
    }
}
