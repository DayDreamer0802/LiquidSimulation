using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class RougeCameraBounds : MonoBehaviour
{
    private BoxCollider _box;

    public Bounds WorldBounds
    {
        get
        {
            if (_box == null) _box = GetComponent<BoxCollider>();
            return _box.bounds;
        }
    }

    private void Reset()
    {
        _box = GetComponent<BoxCollider>();
        _box.isTrigger = true;
        _box.size = new Vector3(100f, 1f, 100f);
    }

    private void OnValidate()
    {
        if (_box == null) _box = GetComponent<BoxCollider>();
        if (_box != null) _box.isTrigger = true;
    }

    private void OnDrawGizmos()
    {
        if (_box == null) _box = GetComponent<BoxCollider>();
        if (_box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.75f);
        Gizmos.DrawWireCube(_box.center, _box.size);
    }
}
