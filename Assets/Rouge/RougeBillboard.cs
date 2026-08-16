using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared runtime sprite loading and lightweight camera-facing presentation.</summary>
public static class RougeSpriteAssets
{
    private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

    public static Sprite Load(string resourcePath, float pixelsPerUnit = 100f)
    {
        string key = resourcePath + "@" + pixelsPerUnit;
        if (Sprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
        {
            Debug.LogError($"Missing sprite texture at Resources/{resourcePath}");
            return null;
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        sprite.name = resourcePath.Substring(resourcePath.LastIndexOf('/') + 1);
        Sprites[key] = sprite;
        return sprite;
    }

    public static SpriteRenderer CreateRenderer(
        string name,
        Transform parent,
        Sprite sprite,
        Vector3 localPosition,
        float scale,
        int sortingOrder,
        Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = Vector3.one * scale;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class RougeBillboard : MonoBehaviour
{
    [SerializeField] private Transform rotatingContent;
    private Camera _camera;
    private Vector3 _worldDirection;
    private bool _hasWorldDirection;

    public void SetRotatingContent(Transform content)
    {
        rotatingContent = content;
    }

    public void SetWorldDirection(Vector3 direction)
    {
        direction.y = 0f;
        _hasWorldDirection = direction.sqrMagnitude > 0.000001f;
        if (_hasWorldDirection) _worldDirection = direction.normalized;
    }

    private void LateUpdate()
    {
        if (_camera == null) _camera = RougeCameraFollow.ResolveCamera();
        if (_camera == null) return;

        transform.rotation = Quaternion.LookRotation(-_camera.transform.forward, _camera.transform.up);
        if (rotatingContent == null || !_hasWorldDirection) return;

        Vector3 localDirection = transform.InverseTransformDirection(_worldDirection);
        Vector2 screenDirection = new Vector2(localDirection.x, localDirection.y);
        if (screenDirection.sqrMagnitude <= 0.000001f) return;
        float angle = Mathf.Atan2(-screenDirection.x, screenDirection.y) * Mathf.Rad2Deg;
        rotatingContent.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
