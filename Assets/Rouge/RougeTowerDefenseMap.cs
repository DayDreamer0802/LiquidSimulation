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
    [InspectorName("14 - 霜寒格：直接伤害附加减速，范围伤害效果减半")]
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
                return "直接伤害附加减速，范围伤害效果减半。";
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

[Serializable]
public struct RougeTiltShiftSettings
{
    [Range(0.2f, 0.8f)] public float focusCenterY;
    [Range(0.02f, 0.48f)] public float upperClearRange;
    [Range(0.02f, 0.48f)] public float upperTransitionWidth;
    [Range(0f, 1f)] public float upperBlurStrength;
    public bool anchorLowerBlurToUi;
    [Range(0.02f, 0.48f)] public float lowerClearRange;
    [Range(0.02f, 0.48f)] public float lowerTransitionWidth;
    [Range(-0.12f, 0.2f)] public float lowerUiEdgeOffset;
    [Range(0f, 1f)] public float lowerBlurStrength;
    [Range(1f, 26f)] public float blurRadius;
    [Range(0.5f, 1.5f)] public float contrast;
    [Range(0f, 2f)] public float saturation;

    public static RougeTiltShiftSettings CreateDefault()
    {
        return new RougeTiltShiftSettings
        {
            focusCenterY = 0.5f,
            upperClearRange = 0.27f,
            upperTransitionWidth = 0.19f,
            upperBlurStrength = 0.86f,
            anchorLowerBlurToUi = true,
            lowerClearRange = 0.25f,
            lowerTransitionWidth = 0.20f,
            lowerUiEdgeOffset = 0f,
            lowerBlurStrength = 0.95f,
            blurRadius = 4.5f,
            contrast = 1.01f,
            saturation = 1f
        };
    }

    public RougeTiltShiftSettings Sanitized()
    {
        RougeTiltShiftSettings value = blurRadius >= 1f &&
                                        upperClearRange > 0f && lowerClearRange > 0f
            ? this
            : CreateDefault();
        value.focusCenterY = Mathf.Clamp(value.focusCenterY, 0.2f, 0.8f);
        value.upperClearRange = Mathf.Clamp(value.upperClearRange, 0.02f, 0.48f);
        value.upperTransitionWidth = Mathf.Clamp(value.upperTransitionWidth, 0.02f, 0.48f);
        value.upperBlurStrength = Mathf.Clamp01(value.upperBlurStrength);
        value.lowerClearRange = Mathf.Clamp(value.lowerClearRange, 0.02f, 0.48f);
        value.lowerTransitionWidth = Mathf.Clamp(value.lowerTransitionWidth, 0.02f, 0.48f);
        value.lowerUiEdgeOffset = Mathf.Clamp(value.lowerUiEdgeOffset, -0.12f, 0.2f);
        value.lowerBlurStrength = Mathf.Clamp01(value.lowerBlurStrength);
        value.blurRadius = Mathf.Clamp(value.blurRadius, 1f, 26f);
        value.contrast = Mathf.Clamp(value.contrast, 0.5f, 1.5f);
        value.saturation = Mathf.Clamp(value.saturation, 0f, 2f);
        return value;
    }
}

public enum RougeCameraPresetMode
{
    Default,
    Free,
    TiltShift,
    TopDown
}

[Serializable]
public struct RougeCameraViewPreset
{
    [SerializeField, HideInInspector] private bool configured;
    [SerializeField] private Vector3 position;
    [SerializeField] private Vector3 eulerAngles;
    [SerializeField] private bool orthographic;
    [SerializeField, Range(1f, 179f)] private float fieldOfView;
    [SerializeField, Min(0.01f)] private float orthographicSize;
    [SerializeField, Min(0.001f)] private float nearClipPlane;
    [SerializeField, Min(0.01f)] private float farClipPlane;

    public bool Configured => configured;
    public Vector3 Position => position;
    public Vector3 EulerAngles => eulerAngles;
    public bool Orthographic => orthographic;
    public float FieldOfView => fieldOfView;
    public float OrthographicSize => orthographicSize;
    public float NearClipPlane => nearClipPlane;
    public float FarClipPlane => farClipPlane;

    public static RougeCameraViewPreset Capture(Camera camera)
    {
        if (camera == null) return default;
        return new RougeCameraViewPreset
        {
            configured = true,
            position = camera.transform.position,
            eulerAngles = camera.transform.eulerAngles,
            orthographic = camera.orthographic,
            fieldOfView = camera.fieldOfView,
            orthographicSize = camera.orthographicSize,
            nearClipPlane = camera.nearClipPlane,
            farClipPlane = camera.farClipPlane
        }.Sanitized();
    }

    public RougeCameraFollow.ViewState ToViewState()
    {
        RougeCameraViewPreset value = Sanitized();
        return new RougeCameraFollow.ViewState
        {
            Position = value.position,
            Rotation = Quaternion.Euler(value.eulerAngles),
            Orthographic = value.orthographic,
            FieldOfView = value.fieldOfView,
            OrthographicSize = value.orthographicSize,
            NearClipPlane = value.nearClipPlane,
            FarClipPlane = value.farClipPlane
        };
    }

    public RougeCameraViewPreset Sanitized()
    {
        RougeCameraViewPreset value = this;
        value.fieldOfView = Mathf.Clamp(value.fieldOfView > 0f ? value.fieldOfView : 60f,
            1f, 179f);
        value.orthographicSize = Mathf.Max(0.01f,
            value.orthographicSize > 0f ? value.orthographicSize : 5f);
        value.nearClipPlane = Mathf.Max(0.001f,
            value.nearClipPlane > 0f ? value.nearClipPlane : 0.3f);
        value.farClipPlane = Mathf.Max(value.nearClipPlane + 0.01f,
            value.farClipPlane > 0f ? value.farClipPlane : 1000f);
        return value;
    }
}

[CreateAssetMenu(fileName = "TowerDefenseMap", menuName = "Rouge/Tower Defense Map")]
public sealed class RougeTowerDefenseMap : ScriptableObject
{
    public const int MaxMapCells = 32;
    // Navigation/crowd simulation still samples terrain more finely. Tower placement is
    // cell-based and never exposes or consumes these internal simulation subdivisions.
    public const int MicroCellsPerTile = 16;
    public const float DefaultEliteSpawnDelaySeconds = 180f;
    public const int DefaultGameplaySeed = 1337;
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
    public sealed class ScoringRules
    {
        [Min(0)] public int mainTowerFullHealthPoints = 100000;
        [Min(0f)] public float remainingGoldPointsPerGold = 2f;
        [Min(0f)] public float killPointsPerEnemy = 1f;
        [Min(0)] public int bossDefeatPoints = 50000;
        [Min(0.0001f)] public float damagePerPoint = 100f;
        [Min(0)] public int gradeSThreshold = 1000000;
        [Min(0)] public int gradeAThreshold = 800000;
        [Min(0)] public int gradeBThreshold = 600000;
        [Min(0)] public int gradeCThreshold = 400000;

        public void Sanitize()
        {
            mainTowerFullHealthPoints = Mathf.Max(0, mainTowerFullHealthPoints);
            remainingGoldPointsPerGold = Mathf.Max(0f, remainingGoldPointsPerGold);
            killPointsPerEnemy = Mathf.Max(0f, killPointsPerEnemy);
            bossDefeatPoints = Mathf.Max(0, bossDefeatPoints);
            damagePerPoint = Mathf.Max(0.0001f, damagePerPoint);
            gradeCThreshold = Mathf.Max(0, gradeCThreshold);
            gradeBThreshold = Mathf.Max(gradeCThreshold, gradeBThreshold);
            gradeAThreshold = Mathf.Max(gradeBThreshold, gradeAThreshold);
            gradeSThreshold = Mathf.Max(gradeAThreshold, gradeSThreshold);
        }

        public long GetMainTowerHealthPoints(float healthRatio)
        {
            return (long)Math.Round(Mathf.Clamp01(healthRatio) *
                mainTowerFullHealthPoints, MidpointRounding.AwayFromZero);
        }

        public long GetRemainingGoldPoints(int remainingGold)
        {
            return ScaleWholeUnits(remainingGold, remainingGoldPointsPerGold);
        }

        public long GetKillPoints(int kills)
        {
            return ScaleWholeUnits(kills, killPointsPerEnemy);
        }

        public long GetDamagePoints(double damage)
        {
            return (long)Math.Floor(Math.Max(0d, damage) /
                Math.Max(0.0001d, damagePerPoint));
        }

        public long GetBossDefeatPoints(bool defeated)
        {
            return defeated ? bossDefeatPoints : 0L;
        }

        public string GetGrade(long score)
        {
            if (score >= gradeSThreshold) return "S";
            if (score >= gradeAThreshold) return "A";
            if (score >= gradeBThreshold) return "B";
            if (score >= gradeCThreshold) return "C";
            return "D";
        }

        private static long ScaleWholeUnits(int amount, float pointsPerUnit)
        {
            return (long)Math.Floor(Math.Max(0, amount) *
                Math.Max(0d, pointsPerUnit));
        }
    }

    [Serializable]
    public sealed class BossEncounter
    {
        [Tooltip("Integer Boss ID from the Tower Defense Balance JSON. IDs remain mod-friendly integers at runtime.")]
        public int bossId;
        [Min(0f), Tooltip("Game minute at which this Boss becomes eligible to spawn.")]
        public float spawnMinute = 12f;
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
    [SerializeField, Min(0f), Tooltip("Game time in seconds during which random enemies cannot become elites.")]
    private float eliteSpawnDelaySeconds = DefaultEliteSpawnDelaySeconds;
    [SerializeField, Min(0f)] private float towerGoldCostMultiplier = 1f;
    [SerializeField, Min(0f)] private float towerDamageMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float towerAttackSpeedMultiplier = 1f;
    [SerializeField, Min(0)] private int startingGold = 2000;
    [SerializeField, Tooltip("Fixed seed for all gameplay-affecting randomness in this level. Visual and dialogue randomness use separate, non-deterministic streams.")]
    private int gameplaySeed = DefaultGameplaySeed;
    [SerializeField] private ScoringRules scoreRules = new ScoringRules();
    [SerializeField] private List<BossEncounter> bossEncounters = new List<BossEncounter>
    {
        new BossEncounter()
    };
    [SerializeField] private List<RougeLevelEventDefinition> levelEventDefinitions =
        new List<RougeLevelEventDefinition>();
    [SerializeField] private List<RougeLevelEventTrigger> levelEventTimeline =
        new List<RougeLevelEventTrigger>();

    [Header("Level Camera Clamp / Zoom")]
    [SerializeField] private bool configureCameraBounds = true;
    [SerializeField] private Vector2 cameraBoundsCenter = Vector2.zero;
    [SerializeField] private Vector2 cameraBoundsSize = new Vector2(180f, 180f);
    [SerializeField, Range(0.5f, 2f)] private float minimumCameraZoom = 0.5f;
    [SerializeField, Range(0.5f, 2f)] private float maximumCameraZoom = 2f;
    [SerializeField, HideInInspector]
    private Vector2 defaultCameraPositionXZ = new Vector2(0f, -25f);
    [SerializeField, HideInInspector]
    private Vector2 tiltShiftCameraPositionXZ = new Vector2(0f, -85f);
    [Header("Four Camera Mode Presets")]
    [SerializeField] private RougeCameraViewPreset defaultCameraView;
    [SerializeField] private RougeCameraViewPreset freeCameraView;
    [SerializeField] private RougeCameraViewPreset tiltShiftCameraView;
    [SerializeField] private RougeCameraViewPreset topDownCameraView;
    [Header("F2 Tilt-Shift Observation")]
    [SerializeField] private RougeTiltShiftSettings tiltShiftSettings =
        RougeTiltShiftSettings.CreateDefault();

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
    public float EliteSpawnDelaySeconds => eliteSpawnDelaySeconds;
    public float TowerGoldCostMultiplier => towerGoldCostMultiplier;
    public float TowerDamageMultiplier => towerDamageMultiplier;
    public float TowerAttackSpeedMultiplier => towerAttackSpeedMultiplier;
    public int StartingGold => startingGold;
    public int GameplaySeed => gameplaySeed;
    public ScoringRules ScoreRules
    {
        get
        {
            scoreRules ??= new ScoringRules();
            return scoreRules;
        }
    }
    public IReadOnlyList<BossEncounter> BossEncounters => bossEncounters;
    public IReadOnlyList<RougeLevelEventDefinition> LevelEventDefinitions =>
        levelEventDefinitions;
    public IReadOnlyList<RougeLevelEventTrigger> LevelEventTimeline =>
        levelEventTimeline;
    public bool ConfigureCameraBounds => configureCameraBounds;
    public Vector2 CameraBoundsCenter => cameraBoundsCenter;
    public Vector2 CameraBoundsSize => cameraBoundsSize;
    public float MinimumCameraZoom => minimumCameraZoom;
    public float MaximumCameraZoom => maximumCameraZoom;
    public Vector2 DefaultCameraPositionXZ => defaultCameraPositionXZ;
    public Vector2 TiltShiftCameraPositionXZ => tiltShiftCameraPositionXZ;
    public RougeCameraViewPreset DefaultCameraView => defaultCameraView.Sanitized();
    public RougeCameraViewPreset FreeCameraView => freeCameraView.Sanitized();
    public RougeCameraViewPreset TiltShiftCameraView => tiltShiftCameraView.Sanitized();
    public RougeCameraViewPreset TopDownCameraView => topDownCameraView.Sanitized();
    public RougeTiltShiftSettings TiltShiftSettings => tiltShiftSettings.Sanitized();
    public Bounds WorldBounds => new Bounds(
        new Vector3(origin.x + width * cellSize * 0.5f, 0f,
            origin.y + height * cellSize * 0.5f),
        new Vector3(width * cellSize, 0f, height * cellSize));

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

    public Vector3 MapPointToWorld(Vector2 mapPoint, float y = 0f)
    {
        Vector2 point = ClampMapPoint(mapPoint);
        return new Vector3(origin.x + (point.x + 0.5f) * cellSize, y,
            origin.y + (point.y + 0.5f) * cellSize);
    }

    public Vector2 WorldToMapPoint(Vector3 worldPoint)
    {
        return ClampMapPoint(new Vector2(
            (worldPoint.x - origin.x) / cellSize - 0.5f,
            (worldPoint.z - origin.y) / cellSize - 0.5f));
    }

    public void SetDefaultCameraPositionXZ(Vector2 worldXZ)
    {
        defaultCameraPositionXZ = worldXZ;
    }

    public void SetTiltShiftCameraPositionXZ(Vector2 worldXZ)
    {
        tiltShiftCameraPositionXZ = worldXZ;
    }

    public void SetTiltShiftSettings(RougeTiltShiftSettings settings)
    {
        tiltShiftSettings = settings.Sanitized();
    }

    public RougeCameraViewPreset GetCameraPreset(RougeCameraPresetMode mode)
    {
        switch (mode)
        {
            case RougeCameraPresetMode.Free: return FreeCameraView;
            case RougeCameraPresetMode.TiltShift: return TiltShiftCameraView;
            case RougeCameraPresetMode.TopDown: return TopDownCameraView;
            default: return DefaultCameraView;
        }
    }

    public void SetCameraPreset(RougeCameraPresetMode mode, RougeCameraViewPreset preset)
    {
        preset = preset.Sanitized();
        switch (mode)
        {
            case RougeCameraPresetMode.Free:
                freeCameraView = preset;
                break;
            case RougeCameraPresetMode.TiltShift:
                tiltShiftCameraView = preset;
                break;
            case RougeCameraPresetMode.TopDown:
                topDownCameraView = preset;
                break;
            default:
                defaultCameraView = preset;
                break;
        }
    }

    public void SetCameraZoomSettings(float minimum, float maximum)
    {
        minimumCameraZoom = Mathf.Clamp(minimum, 0.5f, 2f);
        maximumCameraZoom = Mathf.Clamp(maximum, minimumCameraZoom, 2f);
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
        eliteSpawnDelaySeconds = DefaultEliteSpawnDelaySeconds;
        towerGoldCostMultiplier = 1f;
        towerDamageMultiplier = 1f;
        towerAttackSpeedMultiplier = 1f;
        startingGold = 2000;
        gameplaySeed = DefaultGameplaySeed;
        scoreRules = new ScoringRules();
        bossEncounters = new List<BossEncounter> { new BossEncounter() };
        levelEventDefinitions = new List<RougeLevelEventDefinition>();
        levelEventTimeline = new List<RougeLevelEventTrigger>();
        legacyTileDefinitions.Clear();
        legacyTileDefinitions.Add(new TileDefinition { name = "Empty", editorColor = new Color(0f, 0f, 0f, 0f) });
        legacyTileDefinitions.Add(new TileDefinition { name = "Ground", editorColor = new Color(0.2f, 0.3f, 0.38f, 0.85f), fallbackHeight = 0.08f });
        legacyTileDefinitions.Add(new TileDefinition { name = "Wall", editorColor = new Color(0.85f, 0.22f, 0.16f, 0.9f), blocksNavigation = true, fallbackHeight = 3f });
        legacyTileDefinitions.Add(new TileDefinition { name = "Tower Place", editorColor = new Color(0.18f, 0.8f, 0.42f, 0.9f), towerPlace = true, blocksNavigation = true, fallbackHeight = 0.1f });
        ResizeGrid(MaxMapCells, MaxMapCells, 8f, true);
        minimumCameraZoom = 0.5f;
        maximumCameraZoom = 2f;
        defaultCameraPositionXZ = new Vector2(0f, -25f);
        tiltShiftCameraPositionXZ = new Vector2(0f, -85f);
        defaultCameraView = default;
        freeCameraView = default;
        tiltShiftCameraView = default;
        topDownCameraView = default;
        tiltShiftSettings = RougeTiltShiftSettings.CreateDefault();
    }

    private Vector2Int ClampCell(Vector2Int cell)
    {
        return new Vector2Int(Mathf.Clamp(cell.x, 0, width - 1), Mathf.Clamp(cell.y, 0, height - 1));
    }

    private Vector2 ClampMapPoint(Vector2 point)
    {
        return new Vector2(
            Mathf.Clamp(point.x, -0.5f, width - 0.5f),
            Mathf.Clamp(point.y, -0.5f, height - 0.5f));
    }

    private void EnsureStorage()
    {
        if (tiles == null || tiles.Length != width * height) Array.Resize(ref tiles, width * height);
        legacyTileDefinitions ??= new List<TileDefinition>();
        enemySpawns ??= new List<EnemySpawn>();
        victoryConditions ??= new List<VictoryCondition>();
        disabledTowerTypeIds ??= new List<int>();
        scoreRules ??= new ScoringRules();
        bossEncounters ??= new List<BossEncounter>();
        levelEventDefinitions ??= new List<RougeLevelEventDefinition>();
        levelEventTimeline ??= new List<RougeLevelEventTrigger>();
    }

    private void OnValidate()
    {
        width = Mathf.Clamp(width, 1, MaxMapCells);
        height = Mathf.Clamp(height, 1, MaxMapCells);
        cellSize = Mathf.Max(0.1f, cellSize);
        cameraBoundsSize.x = Mathf.Max(1f, cameraBoundsSize.x);
        cameraBoundsSize.y = Mathf.Max(1f, cameraBoundsSize.y);
        minimumCameraZoom = Mathf.Clamp(minimumCameraZoom, 0.5f, 2f);
        maximumCameraZoom = Mathf.Clamp(maximumCameraZoom, minimumCameraZoom, 2f);
        defaultCameraView = defaultCameraView.Sanitized();
        freeCameraView = freeCameraView.Sanitized();
        tiltShiftCameraView = tiltShiftCameraView.Sanitized();
        topDownCameraView = topDownCameraView.Sanitized();
        tiltShiftSettings = tiltShiftSettings.Sanitized();
        enemyHealthMultiplier = Mathf.Max(0.01f, enemyHealthMultiplier);
        enemyMoveSpeedMultiplier = Mathf.Max(0.01f, enemyMoveSpeedMultiplier);
        eliteSpawnDelaySeconds = Mathf.Max(0f, eliteSpawnDelaySeconds);
        towerGoldCostMultiplier = Mathf.Max(0f, towerGoldCostMultiplier);
        towerDamageMultiplier = Mathf.Max(0f, towerDamageMultiplier);
        towerAttackSpeedMultiplier = Mathf.Max(0.01f, towerAttackSpeedMultiplier);
        startingGold = Mathf.Max(0, startingGold);
        EnsureStorage();
        scoreRules.Sanitize();
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
        for (int i = levelEventDefinitions.Count - 1; i >= 0; i--)
        {
            RougeLevelEventDefinition definition = levelEventDefinitions[i];
            if (definition == null)
            {
                levelEventDefinitions.RemoveAt(i);
                continue;
            }
            definition.eventId = string.IsNullOrWhiteSpace(definition.eventId)
                ? $"event_{i + 1}"
                : definition.eventId.Trim();
            definition.title = string.IsNullOrWhiteSpace(definition.title)
                ? "战场事件"
                : definition.title.Trim();
            definition.effects ??= new List<RougeLevelEventEffect>();
            if (definition.durationSeconds >= 0f)
                definition.durationSeconds = Mathf.Max(0.1f,
                    definition.durationSeconds);
            for (int effectIndex = definition.effects.Count - 1;
                 effectIndex >= 0; effectIndex--)
            {
                if (definition.effects[effectIndex] == null)
                    definition.effects.RemoveAt(effectIndex);
            }
        }
        for (int i = levelEventTimeline.Count - 1; i >= 0; i--)
        {
            RougeLevelEventTrigger trigger = levelEventTimeline[i];
            if (trigger == null)
            {
                levelEventTimeline.RemoveAt(i);
                continue;
            }
            trigger.triggerMinute = Mathf.Max(0f, trigger.triggerMinute);
            trigger.candidateEventIds ??= new List<string>();
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
