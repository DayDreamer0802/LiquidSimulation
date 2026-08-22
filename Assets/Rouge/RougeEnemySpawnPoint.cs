using UnityEngine;

public enum RougeEnemyType
{
    Standard = 0,
    Swift = 1,
    Heavy = 2
}

[DisallowMultipleComponent]
public sealed class RougeEnemySpawnPoint : MonoBehaviour
{
    [Header("Wave")]
    [Range(1, 64)] public int spawnCount = 25;
    [Min(0.1f)] public float spawnInterval = 5f;
    [Min(0f)] public float startDelay = 1f;
    [Tooltip("Remove this spawn point after it has produced the configured number of waves.")]
    public bool limitWaveCount;
    [Min(1)] public int maximumWaves = 1;

    [Header("Enemy")]
    public RougeEnemyType enemyType = RougeEnemyType.Standard;
    [HideInInspector, Min(0)] public int enemyTypeIndex;

    [Header("Spawn Cell")]
    [Min(0.1f)] public float spawnCellSize = 8f;

    [System.NonSerialized] internal float timer;
    [System.NonSerialized] internal int waveIndex;

    public int GetEnemyTypeIndex()
    {
        return (int)enemyType;
    }

    public void ConfigureFromMap(int count, float interval, float delay, float cellSize,
        RougeEnemyType type, bool limitWaves, int maxWaves)
    {
        spawnCount = Mathf.Clamp(count, 1, 64);
        spawnInterval = Mathf.Max(0.1f, interval);
        startDelay = Mathf.Max(0f, delay);
        spawnCellSize = Mathf.Max(0.1f, cellSize);
        enemyType = type;
        limitWaveCount = limitWaves;
        maximumWaves = Mathf.Max(1, maxWaves);
        timer = startDelay;
        waveIndex = 0;
    }

    private void OnEnable()
    {
        timer = Mathf.Max(0f, startDelay);
        waveIndex = 0;
    }

    internal void ResetWaves()
    {
        timer = Mathf.Max(0f, startDelay);
        waveIndex = 0;
    }

    internal void CompleteWave(float spawnSpeedMultiplier)
    {
        timer += Mathf.Max(0.05f, spawnInterval / Mathf.Max(0.01f, spawnSpeedMultiplier));
        waveIndex++;
    }

    internal bool HasReachedWaveLimit()
    {
        return limitWaveCount && waveIndex >= Mathf.Max(1, maximumWaves);
    }

    private void OnValidate()
    {
        spawnCount = Mathf.Clamp(spawnCount, 1, 64);
        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        startDelay = Mathf.Max(0f, startDelay);
        maximumWaves = Mathf.Max(1, maximumWaves);
        enemyTypeIndex = Mathf.Max(0, enemyTypeIndex);
        spawnCellSize = Mathf.Max(0.1f, spawnCellSize);
    }

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;
        Vector3 center = transform.position + Vector3.up * 0.08f;
        Gizmos.color = new Color(1f, 0.12f, 0.08f, 0.16f);
        Gizmos.DrawCube(center, new Vector3(spawnCellSize, 0.1f, spawnCellSize));
        Gizmos.color = new Color(1f, 0.24f, 0.12f, 0.95f);
        Gizmos.DrawWireCube(center, new Vector3(spawnCellSize, 0.1f, spawnCellSize));
#if UNITY_EDITOR
        UnityEditor.Handles.color = new Color(1f, 0.35f, 0.12f, 1f);
        UnityEditor.Handles.Label(center + Vector3.up * 3f,
            $"Enemy Spawn / {enemyType}\n" +
            $"Fixed {spawnCount} every {spawnInterval:0.##}s (before JSON speed curve)\n" +
            (limitWaveCount ? $"Maximum {maximumWaves} waves\n" : "Unlimited waves\n") +
            $"Cell {spawnCellSize:0.#} × {spawnCellSize:0.#}");
#endif
    }
}
