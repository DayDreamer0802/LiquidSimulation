using UnityEngine;

[DisallowMultipleComponent]
public sealed class RougeBossSpawnPoint : MonoBehaviour
{
    [Min(0.5f)] public float previewRadius = 5f;

    private void OnDrawGizmos()
    {
        Vector3 center = transform.position + Vector3.up * previewRadius;
        Gizmos.color = new Color(0.85f, 0.12f, 1f, 0.22f);
        Gizmos.DrawSphere(center, previewRadius);
        Gizmos.color = new Color(1f, 0.3f, 1f, 1f);
        Gizmos.DrawWireSphere(center, previewRadius);
#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.magenta;
        UnityEditor.Handles.Label(center + Vector3.up * (previewRadius + 1f), "BOSS SPAWN");
#endif
    }
}
