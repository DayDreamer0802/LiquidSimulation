using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class RougeEnemySpawnWeight
{
    [Min(0)] public int enemyTypeIndex;
    [Range(0f, 100f)] public float weightPercent = 100f;
}

[DisallowMultipleComponent]
public sealed class RougeEnemySpawnPoint : MonoBehaviour
{
    private const float MaxSpawnCountGrowthPercent = 300f;
    private const float MaxSpawnSpeedGrowthPercent = 200f;

    [Header("Wave")]
    [Min(1)] public int spawnCount = 25;
    [Min(0.1f)] public float spawnInterval = 5f;
    [Min(0f)] public float startDelay = 1f;
    [Header("Enemy Types")]
    [Tooltip("旧场景的单敌人类型。混合列表为空时仍按该类型 100% 生成。")]
    [HideInInspector, Min(0)] public int enemyTypeIndex;
    [Tooltip("该出生点可生成的敌人类型及其相对权重百分比。列表为空时兼容旧的单类型配置。")]
    [SerializeField] private List<RougeEnemySpawnWeight> enemyTypeWeights = new List<RougeEnemySpawnWeight>();
    [Tooltip("初始精英出现率（百分比）。")]
    [Range(0f, 100f)] public float eliteChancePercent = 0.1f;
    [Tooltip("每15秒增加的精英出现率百分点。比如 0.2 表示 0.1% -> 0.3%。")]
    [Range(0f, 100f)] public float eliteChanceIncreasePercentPer15Seconds;
    [Header("Growth / every 15 seconds")]
    [Tooltip("每15秒单波生成数量提升百分比。")]
    [Range(0f, MaxSpawnCountGrowthPercent)] public float spawnCountGrowthPercent = 5f;
    [Tooltip("每15秒生成速度提升百分比；速度越高，波次间隔越短。")]
    [Range(0f, MaxSpawnSpeedGrowthPercent)] public float spawnSpeedGrowthPercent = 2.5f;
    [Header("Spawn Area")]
    [Min(0.1f)] public float spawnRadius = 14f;
    [Min(0f)] public float minimumRadius;
    [System.NonSerialized] internal float timer;

    public int GetCurrentWaveEnemyCount(float elapsedGameTime)
    {
        int steps = Mathf.Max(0, Mathf.FloorToInt(elapsedGameTime / 15f));
        // Wave growth is expressed as percentage points of the original value.
        // It is deliberately not compounded: +5%/15s reaches the +100% cap after
        // 20 steps instead of growing exponentially forever.
        float growthPercent = Mathf.Min(MaxSpawnCountGrowthPercent, steps * spawnCountGrowthPercent);
        float multiplier = 1f + growthPercent * 0.01f;
        return Mathf.Max(1, Mathf.RoundToInt(spawnCount * multiplier));
    }

    public float GetCurrentWaveInterval(float elapsedGameTime)
    {
        int steps = Mathf.Max(0, Mathf.FloorToInt(elapsedGameTime / 15f));
        float growthPercent = Mathf.Min(MaxSpawnSpeedGrowthPercent, steps * spawnSpeedGrowthPercent);
        float speedMultiplier = 1f + growthPercent * 0.01f;
        return Mathf.Max(0.05f, spawnInterval / Mathf.Max(1f, speedMultiplier));
    }

    public float GetCurrentEliteChance01(float elapsedGameTime)
    {
        int steps = Mathf.Max(0, Mathf.FloorToInt(elapsedGameTime / 15f));
        return Mathf.Clamp01((eliteChancePercent + steps * eliteChanceIncreasePercentPer15Seconds) * 0.01f);
    }

    public int RollEnemyTypeIndex(int enemyTypeCount)
    {
        int validTypeCount = Mathf.Max(1, enemyTypeCount);
        float totalWeight = 0f;
        if (enemyTypeWeights != null)
        {
            for (int i = 0; i < enemyTypeWeights.Count; i++)
            {
                RougeEnemySpawnWeight entry = enemyTypeWeights[i];
                if (entry == null || entry.enemyTypeIndex < 0 || entry.enemyTypeIndex >= validTypeCount) continue;
                totalWeight += Mathf.Max(0f, entry.weightPercent);
            }
        }

        if (totalWeight <= 0f) return Mathf.Clamp(enemyTypeIndex, 0, validTypeCount - 1);

        float roll = Random.value * totalWeight;
        int lastValidType = Mathf.Clamp(enemyTypeIndex, 0, validTypeCount - 1);
        for (int i = 0; i < enemyTypeWeights.Count; i++)
        {
            RougeEnemySpawnWeight entry = enemyTypeWeights[i];
            if (entry == null || entry.enemyTypeIndex < 0 || entry.enemyTypeIndex >= validTypeCount ||
                entry.weightPercent <= 0f) continue;
            lastValidType = entry.enemyTypeIndex;
            roll -= entry.weightPercent;
            if (roll <= 0f) return entry.enemyTypeIndex;
        }
        return lastValidType;
    }

    private void OnEnable()
    {
        timer = Mathf.Max(0f, startDelay);
    }

    internal void ResetWaves()
    {
        timer = Mathf.Max(0f, startDelay);
    }

    internal void CompleteWave(float elapsedGameTime)
    {
        timer += GetCurrentWaveInterval(elapsedGameTime);
    }
    private void OnValidate()
    {
        spawnCount = Mathf.Max(1, spawnCount);
        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        enemyTypeIndex = Mathf.Max(0, enemyTypeIndex);
        if (enemyTypeWeights != null)
        {
            for (int i = 0; i < enemyTypeWeights.Count; i++)
            {
                enemyTypeWeights[i] ??= new RougeEnemySpawnWeight();
                enemyTypeWeights[i].enemyTypeIndex = Mathf.Max(0, enemyTypeWeights[i].enemyTypeIndex);
                enemyTypeWeights[i].weightPercent = Mathf.Clamp(enemyTypeWeights[i].weightPercent, 0f, 100f);
            }
        }
        eliteChancePercent = Mathf.Clamp(eliteChancePercent, 0f, 100f);
        eliteChanceIncreasePercentPer15Seconds = Mathf.Clamp(eliteChanceIncreasePercentPer15Seconds, 0f, 100f);
        spawnCountGrowthPercent = Mathf.Clamp(spawnCountGrowthPercent, 0f, MaxSpawnCountGrowthPercent);
        spawnSpeedGrowthPercent = Mathf.Clamp(spawnSpeedGrowthPercent, 0f, MaxSpawnSpeedGrowthPercent);
        spawnRadius = Mathf.Max(0.1f, spawnRadius);
        minimumRadius = Mathf.Clamp(minimumRadius, 0f, spawnRadius);
    }

    private void OnDrawGizmos()
    {
        Vector3 center = transform.position + Vector3.up * 0.08f;
        Gizmos.color = new Color(1f, 0.12f, 0.08f, 0.16f);
        Gizmos.DrawSphere(center, Mathf.Max(0.25f, spawnRadius));
        Gizmos.color = new Color(1f, 0.24f, 0.12f, 0.95f);
        Gizmos.DrawWireSphere(center, spawnRadius);
        if (minimumRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(center, minimumRadius);
        }
#if UNITY_EDITOR
        UnityEditor.Handles.color = new Color(1f, 0.35f, 0.12f, 1f);
        UnityEditor.Handles.Label(center + Vector3.up * 3f,
            $"Enemy Spawn / {GetEnemyMixLabel()}\nWave 1: {spawnCount} every {spawnInterval:0.##}s\n" +
            $"Every 15s: count +{spawnCountGrowthPercent:0.##}%, speed +{spawnSpeedGrowthPercent:0.##}%\n" +
            $"Elite {eliteChancePercent:0.###}% + {eliteChanceIncreasePercentPer15Seconds:0.###} points/15s\nRadius {spawnRadius:0.#}");
#endif
    }

    private string GetEnemyMixLabel()
    {
        if (enemyTypeWeights == null || enemyTypeWeights.Count == 0) return $"Type {enemyTypeIndex}: 100%";
        string label = string.Empty;
        for (int i = 0; i < enemyTypeWeights.Count; i++)
        {
            RougeEnemySpawnWeight entry = enemyTypeWeights[i];
            if (entry == null || entry.weightPercent <= 0f) continue;
            if (label.Length > 0) label += " / ";
            label += $"T{entry.enemyTypeIndex} {entry.weightPercent:0.#}%";
        }
        return string.IsNullOrEmpty(label) ? $"Type {enemyTypeIndex}: 100% (fallback)" : label;
    }
}
