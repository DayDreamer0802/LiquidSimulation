using UnityEngine;

/// <summary>
/// 挂到任意物体上，让它在 XZ 平面绕指定中心点做圆周运动。
/// 速度由“绕一圈所需秒数”控制。
/// </summary>
[DisallowMultipleComponent]
public sealed class RougeOrbitMover : MonoBehaviour
{
    [Tooltip("优先使用的中心点 Transform。为空时使用下方 World Center。")]
    [SerializeField] private Transform centerTransform;

    [Tooltip("当 Center Transform 为空时，使用这个世界坐标作为中心点。")]
    [SerializeField] private Vector3 worldCenter = Vector3.zero;

    [Tooltip("绕圈半径。")]
    [SerializeField] private float radius = 3f;

    [Tooltip("绕一圈所需秒数。值越小转得越快。")]
    [SerializeField] private float revolutionDuration = 2f;

    [Tooltip("相对中心点的高度偏移。")]
    [SerializeField] private float heightOffset;

    [Tooltip("初始角度，单位为度。0 度表示中心点右侧。")]
    [SerializeField] private float startAngleDeg;

    [Tooltip("是否顺时针旋转。")]
    [SerializeField] private bool clockwise;

    private float _currentAngleDeg;

    private void OnEnable()
    {
        _currentAngleDeg = startAngleDeg;
        ApplyPosition();
    }

    private void Update()
    {
        float safeDuration = Mathf.Max(0.01f, revolutionDuration);
        float degreesPerSecond = 360f / safeDuration;
        _currentAngleDeg += (clockwise ? -degreesPerSecond : degreesPerSecond) * Time.deltaTime;
        ApplyPosition();
    }

    private void OnValidate()
    {
        radius = Mathf.Max(0f, radius);
        revolutionDuration = Mathf.Max(0.01f, revolutionDuration);

        if (!Application.isPlaying)
        {
            _currentAngleDeg = startAngleDeg;
            ApplyPosition();
        }
    }

    private void ApplyPosition()
    {
        Vector3 center = centerTransform != null ? centerTransform.position : worldCenter;
        float radians = _currentAngleDeg * Mathf.Deg2Rad;

        transform.position = new Vector3(
            center.x + Mathf.Cos(radians) * radius,
            center.y + heightOffset,
            center.z + Mathf.Sin(radians) * radius);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 center = centerTransform != null ? centerTransform.position : worldCenter;
        center.y += heightOffset;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);

        const int segments = 48;
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 current = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }
#endif
}