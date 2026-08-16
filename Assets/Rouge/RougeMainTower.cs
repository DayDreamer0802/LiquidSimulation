using UnityEngine;

[DisallowMultipleComponent]
public sealed class RougeMainTower : MonoBehaviour
{
    [Header("Health")]
    [Min(1f)] public float maxHealth = 1500f;
    [Min(0.1f)] public float contactRadius = 3.2f;
    [Min(0f)] public float damagePerEnemy = 18f;
    [Header("Hit AOE")]
    [Min(0f)] public float hitAoeRadius = 9f;
    [Min(0f)] public float hitAoeDamage = 22f;
    [Min(0f)] public float hitAoeKnockback = 38f;
    [Min(0f)] public float hitAoeCooldown = 0.35f;
    [SerializeField, HideInInspector] private float currentHealth;
    [System.NonSerialized] internal float aoeCooldownRemaining;

    public float CurrentHealth => currentHealth;
    public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
    public bool IsDestroyed => currentHealth <= 0f;

    internal void ResetHealth()
    {
        currentHealth = Mathf.Max(1f, maxHealth);
        aoeCooldownRemaining = 0f;
    }

    internal bool ApplyEnemyContacts(int count)
    {
        if (count <= 0 || IsDestroyed) return false;
        currentHealth = Mathf.Max(0f, currentHealth - damagePerEnemy * count);
        if (aoeCooldownRemaining > 0f) return false;
        aoeCooldownRemaining = hitAoeCooldown;
        return true;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        contactRadius = Mathf.Max(0.1f, contactRadius);
        damagePerEnemy = Mathf.Max(0f, damagePerEnemy);
        hitAoeRadius = Mathf.Max(0f, hitAoeRadius);
        hitAoeDamage = Mathf.Max(0f, hitAoeDamage);
        hitAoeKnockback = Mathf.Max(0f, hitAoeKnockback);
        hitAoeCooldown = Mathf.Max(0f, hitAoeCooldown);
        if (!Application.isPlaying) currentHealth = maxHealth;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.12f, 0.72f, 1f, 0.28f);
        Gizmos.DrawCube(transform.position + Vector3.up * 5f, new Vector3(5f, 10f, 5f));
        Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, contactRadius);
        Gizmos.color = new Color(1f, 0.7f, 0.12f, 0.75f);
        Gizmos.DrawWireSphere(transform.position, hitAoeRadius);
    }
}
