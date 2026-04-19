using UnityEngine;

[DefaultExecutionOrder(-100)]
public class PlayerBase : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 18f;
    [SerializeField] private float acceleration = 90f;
    [SerializeField] private float braking = 14f;
    [SerializeField] private float aimPlaneHeight = 0f;
    [SerializeField] private Camera aimCamera;

    private Vector3 _velocity;
    private Vector3 _aimDirection = Vector3.forward;

    public Vector3 Velocity => _velocity;
    public Vector3 AimDirection => _aimDirection;
    public Vector2 PlanarPosition => new Vector2(transform.position.x, transform.position.z);

    private void Awake()
    {
        if (aimCamera == null)
        {
            aimCamera = RougeCameraFollow.ResolveCamera();
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        UpdateMovement(dt);
        UpdateAim();
    }

    private void UpdateMovement(float dt)
    {
        Vector2 moveInput = RougeInputManager.Instance.ReadMoveVector();
        Vector3 input = new Vector3(moveInput.x, 0f, moveInput.y);
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        Vector3 targetVelocity = input * moveSpeed;
        _velocity = Vector3.MoveTowards(_velocity, targetVelocity, acceleration * dt);
        if (input.sqrMagnitude < 0.0001f)
        {
            float damping = 1f - Mathf.Exp(-braking * dt);
            _velocity = Vector3.Lerp(_velocity, Vector3.zero, damping);
        }

        Vector3 position = transform.position + _velocity * dt;
        position.y = aimPlaneHeight;
        transform.position = position;
    }

    private void UpdateAim()
    {
        if (aimCamera == null)
        {
            aimCamera = RougeCameraFollow.ResolveCamera();
            if (aimCamera == null)
            {
                return;
            }
        }

        Plane plane = new Plane(Vector3.up, new Vector3(0f, aimPlaneHeight, 0f));
        Vector2 pointerPosition = RougeInputManager.Instance.ReadPointerPosition();
        Ray ray = aimCamera.ScreenPointToRay(new Vector3(pointerPosition.x, pointerPosition.y, 0f));
        if (plane.Raycast(ray, out float distance))
        {
            Vector3 hitPoint = ray.GetPoint(distance);
            Vector3 flatDirection = hitPoint - transform.position;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude > 0.0001f)
            {
                _aimDirection = flatDirection.normalized;
            }
        }

        if (_aimDirection.sqrMagnitude > 0.0001f)
        {
            transform.forward = _aimDirection;
        }
    }
}
