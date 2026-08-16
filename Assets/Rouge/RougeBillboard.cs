using System;
using System.Collections;
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

//射击时候子弹从哪里发出的trans ,因为这个跟动画有关所以放在这里
        [SerializeField] private Transform shootPoint;
    //旋转攻击方向的content
    [SerializeField] private Transform rotatingContent;
    //上下浮动的trans
    [SerializeField] private Transform floatingContent;
    [SerializeField] private float floatingTargetY =0.22f; //浮动到目标的y值,默认一定是0->y->0->-y->0反复
     [SerializeField] private float flaotingTargetTime = 2f;//浮动一次来回耗时(sin360),浮动是sin函数
    //发射子弹时候后坐力的content,如果这个不为null,那么需要在后座之后才会发射子弹
     [SerializeField] private Transform shootMoveContent;

    [SerializeField]  private float shootMoveY = -0.22f; //发射子弹后座位移Y
      [SerializeField] private float shootMoveYTime1 = 0.1f; //后坐用时

     [SerializeField] private float shootMoveYTime2 = 0.1f; //还原用时
      //发射子弹时候缩放的content 如果这个不为null,那么需要在y缩小到最小之后才会发射子弹(也就是step2时候)
      [SerializeField] private Transform shootScaleContent;
      [SerializeField] private Vector2 shootScale1 = new Vector2(0.9f,1.1f); //发射时候一般是先x变小y变大,
       [SerializeField] private float shootScale1Time =0.15f;//到scale1用时

       [SerializeField] private Vector2 shootScale2 = new Vector2(1.1f,0.8f); //发射时候一般是先x变小y变大,
       [SerializeField] private float shootScale2Time =0.3f;//到scale2用时

       [SerializeField] private float shootScale3Time =0.2f;//还原scale用时
   
    private Camera _camera;
    private Vector3 _worldDirection;
    private bool _hasWorldDirection;
    private Vector3 _floatingBasePosition;
    private Vector3 _shootMoveBasePosition;
    private Vector3 _shootScaleBaseScale;
    private bool _hasFloatingBase;
    private bool _hasShootMoveBase;
    private bool _hasShootScaleBase;
    private Coroutine _shootAnimation;
    private float _animationStartTime;

    /// <summary>The world-space origin authored in the tower prefab for projectiles and beams.</summary>
    public Vector3 ShootPosition => shootPoint != null ? shootPoint.position : transform.position;

    private void Awake()
    {
        CacheAnimationBases();
    }

    private void OnEnable()
    {
        _animationStartTime = Time.time;
    }

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

    /// <summary>
    /// Plays the prefab-authored firing motion and invokes <paramref name="onShotFired"/> at
    /// the release frame. Towers with no firing motion release immediately. Rapid-fire towers
    /// keep their gameplay cadence while the currently visible recoil completes.
    /// </summary>
    public void PlayShootAnimation(Action onShotFired)
    {
        CacheAnimationBases();
        bool hasMove = shootMoveContent != null;
        bool hasScale = shootScaleContent != null;
        if (!hasMove && !hasScale)
        {
            InvokeShot(onShotFired);
            return;
        }

        // A machine gun may fire multiple times within a single visual recoil. Do not queue or
        // discard those shots: only the first one owns the visual, later shots keep the DPS timing.
        if (_shootAnimation != null)
        {
            InvokeShot(onShotFired);
            return;
        }
        _shootAnimation = StartCoroutine(PlayShootAnimationRoutine(onShotFired, hasMove, hasScale));
    }

    private IEnumerator PlayShootAnimationRoutine(Action onShotFired, bool hasMove, bool hasScale)
    {
        Vector3 recoilPosition = _shootMoveBasePosition + Vector3.up * shootMoveY;
        Vector3 scaleOne = GetScaledShootScale(shootScale1);
        Vector3 scaleTwo = GetScaledShootScale(shootScale2);

        float phaseOneDuration = Mathf.Max(hasMove ? Mathf.Max(0f, shootMoveYTime1) : 0f,
            hasScale ? Mathf.Max(0f, shootScale1Time) : 0f);
        yield return AnimatePhase(phaseOneDuration, t =>
        {
            if (hasMove) shootMoveContent.localPosition = Vector3.LerpUnclamped(_shootMoveBasePosition, recoilPosition, t);
            if (hasScale) shootScaleContent.localScale = Vector3.LerpUnclamped(_shootScaleBaseScale, scaleOne, t);
        });

        // A recoil-only tower fires at the end of its recoil. Scale-driven towers fire after the
        // second squash phase, when the authored vertical scale reaches its minimum.
        if (!hasScale) InvokeShot(onShotFired);

        float phaseTwoDuration = Mathf.Max(hasMove ? Mathf.Max(0f, shootMoveYTime2) : 0f,
            hasScale ? Mathf.Max(0f, shootScale2Time) : 0f);
        yield return AnimatePhase(phaseTwoDuration, t =>
        {
            if (hasMove) shootMoveContent.localPosition = Vector3.LerpUnclamped(recoilPosition, _shootMoveBasePosition, t);
            if (hasScale) shootScaleContent.localScale = Vector3.LerpUnclamped(scaleOne, scaleTwo, t);
        });

        if (hasScale) InvokeShot(onShotFired);

        if (hasScale)
        {
            yield return AnimatePhase(Mathf.Max(0f, shootScale3Time), t =>
                shootScaleContent.localScale = Vector3.LerpUnclamped(scaleTwo, _shootScaleBaseScale, t));
        }

        RestoreShootState();
        _shootAnimation = null;
    }

    private IEnumerator AnimatePhase(float duration, Action<float> apply)
    {
        if (duration <= 0f)
        {
            apply(1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            apply(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
    }

    private Vector3 GetScaledShootScale(Vector2 multiplier)
    {
        return new Vector3(_shootScaleBaseScale.x * multiplier.x,
            _shootScaleBaseScale.y * multiplier.y, _shootScaleBaseScale.z);
    }

    private void CacheAnimationBases()
    {
        if (floatingContent != null && !_hasFloatingBase)
        {
            _floatingBasePosition = floatingContent.localPosition;
            _hasFloatingBase = true;
        }
        if (shootMoveContent != null && !_hasShootMoveBase)
        {
            _shootMoveBasePosition = shootMoveContent.localPosition;
            _hasShootMoveBase = true;
        }
        if (shootScaleContent != null && !_hasShootScaleBase)
        {
            _shootScaleBaseScale = shootScaleContent.localScale;
            _hasShootScaleBase = true;
        }
    }

    private void RestoreShootState()
    {
        if (shootMoveContent != null) shootMoveContent.localPosition = _shootMoveBasePosition;
        if (shootScaleContent != null) shootScaleContent.localScale = _shootScaleBaseScale;
    }

    private static void InvokeShot(Action onShotFired)
    {
        try
        {
            onShotFired?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private void OnDisable()
    {
        if (_shootAnimation != null) StopCoroutine(_shootAnimation);
        _shootAnimation = null;
        RestoreShootState();
    }

    private void LateUpdate()
    {
        if (_camera == null) _camera = RougeCameraFollow.ResolveCamera();
        if (_camera == null) return;

        transform.rotation = Quaternion.LookRotation(-_camera.transform.forward, _camera.transform.up);
        if (floatingContent != null && flaotingTargetTime > 0.0001f)
        {
            float phase = (Time.time - _animationStartTime) * Mathf.PI * 2f / flaotingTargetTime;
            floatingContent.localPosition = _floatingBasePosition + Vector3.up * (Mathf.Sin(phase) * floatingTargetY);
        }
        if (rotatingContent == null || !_hasWorldDirection) return;

        Vector3 localDirection = transform.InverseTransformDirection(_worldDirection);
        Vector2 screenDirection = new Vector2(localDirection.x, localDirection.y);
        if (screenDirection.sqrMagnitude <= 0.000001f) return;
        float angle = Mathf.Atan2(-screenDirection.x, screenDirection.y) * Mathf.Rad2Deg;
        rotatingContent.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
