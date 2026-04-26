using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 运行时动态障碍物：挂在任何会移动 / 出生 / 销毁的 GameObject 上。
/// OnEnable 自动注册到 RougeGameManager，OnDisable 自动注销。
/// 每帧由 RougeGameManager 统一拍快照成 RougeObstacle 喂给 flow field。
/// 不依赖物理引擎，纯 Transform 取位置，对寻路开销极低。
/// </summary>
[DisallowMultipleComponent]
public class RougeDynamicObstacle : MonoBehaviour
{
    public enum Shape
    {
        Circle = 0,
        Box = 1
    }

    [SerializeField] private bool useAttachedCollider = true;

    [SerializeField] private Shape shape = Shape.Circle;

    [Tooltip("Circle 形状的半径（米）")]
    [SerializeField] private float radius = 1f;

    [Tooltip("手动 Box 形状的 XZ 半长（米）")]
    [SerializeField] private Vector2 boxHalfExtents = new Vector2(1f, 1f);

    [Tooltip("额外寻路 padding（仅用于 flow field 与 collision avoidance）")]
    [SerializeField] private float padding = 0.4f;

    public Shape ObstacleShape => shape;
    public bool UseAttachedCollider => useAttachedCollider;
    public float Radius => radius;
    public Vector2 BoxHalfExtents => boxHalfExtents;
    public float Padding => padding;

    private void OnEnable()
    {
        RougeGameManager.RegisterDynamicObstacle(this);
    }

    private void OnDisable()
    {
        RougeGameManager.UnregisterDynamicObstacle(this);
    }

    /// <summary>
    /// 把当前 Transform 状态拍成寻路用的 RougeObstacle。
    /// 由 RougeGameManager 在 ScheduleSimulation 之前调用。
    /// </summary>
    public RougeObstacle Snapshot()
    {
        if (useAttachedCollider && TryCreateObstacleFromCollider(GetComponent<Collider>(), padding, out RougeObstacle colliderObstacle))
        {
            return colliderObstacle;
        }

        Transform t = transform;
        Vector3 worldPos = t.position;
        Vector3 lossy = t.lossyScale;

        if (shape == Shape.Circle)
        {
            float scaledRadius = radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
            return RougeObstacleMath.CreateCircle(new float2(worldPos.x, worldPos.z), scaledRadius, padding);
        }

        float2 center = new float2(worldPos.x, worldPos.z);
        float2 half = new float2(
            math.max(boxHalfExtents.x * math.abs(lossy.x), 0.05f),
            math.max(boxHalfExtents.y * math.abs(lossy.z), 0.05f));

        return RougeObstacleMath.CreateBox(center, new float2(1f, 0f), new float2(0f, 1f), half, padding);
    }

    public static bool TryCreateObstacleFromCollider(Collider collider, float padding, out RougeObstacle obstacle)
    {
        obstacle = default;
        if (collider == null || !collider.enabled)
        {
            return false;
        }

        if (collider is SphereCollider sphere)
        {
            float2 center = ToFloat2XZ(sphere.transform.TransformPoint(sphere.center));
            Bounds bounds = sphere.bounds;
            float radiusFromBounds = Mathf.Max(bounds.extents.x, bounds.extents.z);
            obstacle = RougeObstacleMath.CreateCircle(center, radiusFromBounds, padding);
            return true;
        }

        if (collider is CapsuleCollider capsule)
        {
            float2 center = ToFloat2XZ(capsule.transform.TransformPoint(capsule.center));
            Bounds bounds = capsule.bounds;
            float radiusFromBounds = Mathf.Max(bounds.extents.x, bounds.extents.z);
            obstacle = RougeObstacleMath.CreateCircle(center, radiusFromBounds, padding);
            return true;
        }

        if (collider is BoxCollider box)
        {
            Transform boxTransform = box.transform;
            float2 center = ToFloat2XZ(boxTransform.TransformPoint(box.center));
            float2 halfAxisX = ToFloat2XZ(boxTransform.TransformVector(new Vector3(box.size.x * 0.5f, 0f, 0f)));
            float2 halfAxisY = ToFloat2XZ(boxTransform.TransformVector(new Vector3(0f, 0f, box.size.z * 0.5f)));
            float2 halfExtents = new float2(math.length(halfAxisX), math.length(halfAxisY));
            obstacle = RougeObstacleMath.CreateBox(center, halfAxisX, halfAxisY, halfExtents, padding);
            return true;
        }

        return false;
    }

    private static float2 ToFloat2XZ(Vector3 value)
    {
        return new float2(value.x, value.z);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.65f);
        RougeObstacle obstacle = Snapshot();
        Vector3 p = new Vector3(obstacle.Center.x, transform.position.y, obstacle.Center.y);
        if (obstacle.Type == RougeObstacle.CircleType)
        {
            float r = obstacle.CircleRadius;
            const int segments = 32;
            Vector3 prev = p + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 cur = p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }
        else
        {
            float2 axisX = obstacle.BoxAxisX * obstacle.BoxHalfExtents.x;
            float2 axisY = obstacle.BoxAxisY * obstacle.BoxHalfExtents.y;
            Vector3 c0 = new Vector3(obstacle.Center.x - axisX.x - axisY.x, p.y, obstacle.Center.y - axisX.y - axisY.y);
            Vector3 c1 = new Vector3(obstacle.Center.x + axisX.x - axisY.x, p.y, obstacle.Center.y + axisX.y - axisY.y);
            Vector3 c2 = new Vector3(obstacle.Center.x + axisX.x + axisY.x, p.y, obstacle.Center.y + axisX.y + axisY.y);
            Vector3 c3 = new Vector3(obstacle.Center.x - axisX.x + axisY.x, p.y, obstacle.Center.y - axisX.y + axisY.y);
            Gizmos.DrawLine(c0, c1);
            Gizmos.DrawLine(c1, c2);
            Gizmos.DrawLine(c2, c3);
            Gizmos.DrawLine(c3, c0);
        }
    }
#endif
}
