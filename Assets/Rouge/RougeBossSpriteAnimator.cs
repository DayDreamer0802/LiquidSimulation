using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss animation layout matches regular enemies: frames 0..3 move and frames
/// 4..5 idle. Skill use and death both deliberately stay on the idle frames.
/// </summary>
[DisallowMultipleComponent]
public sealed class RougeBossSpriteAnimator : MonoBehaviour
{
    private struct Shard
    {
        public Transform Transform;
        public Vector2 Velocity;
        public float AngularVelocity;
    }

    private readonly List<Sprite> _frames = new List<Sprite>(16);
    private readonly List<Sprite> _shardSprites = new List<Sprite>(16);
    private readonly List<Shard> _shards = new List<Shard>(16);
    private SpriteRenderer _renderer;
    private SpriteRenderer _frozenOverlayRenderer;
    private float _frozenOverlayBaseScale;
    private Texture2D _sheet;
    private Rect _currentFrameRect;
    private int _columns;
    private int _rows;
    private float _frameTimer;
    private float _skillRemaining;
    private float _visualScale = 1f;
    private bool _moving;
    private bool _dying;
    private bool _shattered;

    public static RougeBossSpriteAnimator Create(RougeBossBalanceConfig config, float worldHeight)
    {
        if (config == null) return null;
        Texture2D texture = Resources.Load<Texture2D>(config.spriteResourcePath);
        if (texture == null)
        {
            Debug.LogError($"Missing Boss sprite sheet at Resources/{config.spriteResourcePath}");
            return null;
        }

        GameObject root = new GameObject("Boss Animated Billboard");
        root.AddComponent<RougeBillboard>();
        RougeBossSpriteAnimator animator = root.AddComponent<RougeBossSpriteAnimator>();
        animator.Build(texture, config.spriteSheetColumns, config.spriteSheetRows, worldHeight);
        return animator;
    }

    private void Build(Texture2D texture, int columns, int rows, float worldHeight)
    {
        _sheet = texture;
        // A replacement Boss sheet matching the regular-enemy 3x2 aspect ratio
        // is picked up automatically: 4 movement frames, then 2 idle frames.
        if (columns == 1 && rows == 1 && texture.width >= texture.height * 1.35f)
        {
            columns = 3;
            rows = 2;
        }
        _columns = Mathf.Clamp(columns, 1, 8);
        _rows = Mathf.Clamp(rows, 1, 8);
        float cellWidth = texture.width / (float)_columns;
        float cellHeight = texture.height / (float)_rows;
        const float pixelsPerUnit = 100f;
        for (int row = 0; row < _rows; row++)
        {
            for (int column = 0; column < _columns; column++)
            {
                Rect rect = new Rect(column * cellWidth, texture.height - (row + 1) * cellHeight,
                    cellWidth, cellHeight);
                Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit,
                    0, SpriteMeshType.FullRect);
                sprite.name = $"Boss Frame {row * _columns + column:00}";
                _frames.Add(sprite);
            }
        }

        GameObject image = new GameObject("Boss Sprite");
        image.transform.SetParent(transform, false);
        _renderer = image.AddComponent<SpriteRenderer>();
        _renderer.sprite = _frames[0];
        _currentFrameRect = _frames[0].rect;
        _renderer.sortingOrder = 200;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _visualScale = Mathf.Max(0.1f, worldHeight / Mathf.Max(0.01f, cellHeight / pixelsPerUnit));
        image.transform.localScale = Vector3.one * _visualScale;

        Sprite frozenOverlay = RougeSpriteAssets.Load("Sprites/Effects/enemy_frozen_overlay");
        if (frozenOverlay != null)
        {
            float frozenHeight = frozenOverlay.rect.height /
                                 Mathf.Max(1f, frozenOverlay.pixelsPerUnit);
            _frozenOverlayBaseScale = worldHeight * 1.12f /
                                      Mathf.Max(0.01f, frozenHeight);
            _frozenOverlayRenderer = RougeSpriteAssets.CreateRenderer(
                "Boss Frozen Overlay", transform, frozenOverlay, Vector3.zero,
                _frozenOverlayBaseScale, 202, Color.white);
            _frozenOverlayRenderer.gameObject.SetActive(false);
        }
    }

    public void SetWorldState(Vector3 position, Vector3 velocity)
    {
        transform.position = position;
        _moving = velocity.sqrMagnitude > 0.04f;
    }

    public void PlaySkill(float duration)
    {
        if (_dying) return;
        _skillRemaining = Mathf.Max(0.1f, duration);
        _frameTimer = 0f;
    }

    public void SetFrozenVisual(bool active)
    {
        if (_frozenOverlayRenderer == null) return;
        _frozenOverlayRenderer.gameObject.SetActive(active && !_shattered);
    }

    public void BeginDeath()
    {
        _dying = true;
        SetFrozenVisual(false);
        _skillRemaining = 0f;
        _frameTimer = 0f;
    }

    public void SetDeathShake(float normalizedProgress)
    {
        if (_renderer == null || _shattered) return;
        _dying = true;
        int idleStart = _frames.Count >= 6 ? 4 : 0;
        int idleCount = _frames.Count >= 6 ? 2 : 1;
        SetFrame(idleStart + Mathf.FloorToInt(Time.unscaledTime * 2f) % idleCount);
        float amount = Mathf.Lerp(0.04f, 0.42f, Mathf.Clamp01(normalizedProgress));
        _renderer.transform.localPosition = new Vector3(
            Random.Range(-amount, amount), Random.Range(-amount * 0.55f, amount * 0.55f), 0f);
    }

    public void ExplodeIntoShards(float speed)
    {
        if (_renderer == null || _sheet == null || _shattered) return;
        _shattered = true;
        _renderer.enabled = false;
        SetFrozenVisual(false);

        Rect rect = _currentFrameRect;
        const int shardColumns = 4;
        const int shardRows = 4;
        float width = rect.width / shardColumns;
        float height = rect.height / shardRows;
        for (int row = 0; row < shardRows; row++)
        {
            for (int column = 0; column < shardColumns; column++)
            {
                Rect shardRect = new Rect(rect.x + column * width, rect.y + row * height, width, height);
                Sprite sprite = Sprite.Create(_sheet, shardRect, new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect);
                _shardSprites.Add(sprite);

                GameObject piece = new GameObject($"Boss Shard {row}-{column}");
                piece.transform.SetParent(transform, false);
                piece.transform.localScale = Vector3.one * _visualScale;
                piece.transform.localPosition = new Vector3(
                    ((column + 0.5f) / shardColumns - 0.5f) * rect.width / 100f * _visualScale,
                    ((row + 0.5f) / shardRows - 0.5f) * rect.height / 100f * _visualScale,
                    0f);
                SpriteRenderer shardRenderer = piece.AddComponent<SpriteRenderer>();
                shardRenderer.sprite = sprite;
                shardRenderer.sortingOrder = 201 + row;
                shardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector2 direction = new Vector2(column - (shardColumns - 1) * 0.5f,
                    row - (shardRows - 1) * 0.5f).normalized;
                direction += Random.insideUnitCircle * 0.32f;
                _shards.Add(new Shard
                {
                    Transform = piece.transform,
                    Velocity = direction.normalized * Random.Range(speed * 0.7f, speed * 1.25f),
                    AngularVelocity = Random.Range(-360f, 360f)
                });
            }
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (_frozenOverlayRenderer != null &&
            _frozenOverlayRenderer.gameObject.activeSelf)
        {
            float pulse = 0.985f + Mathf.Sin(Time.unscaledTime * 7f) * 0.025f;
            _frozenOverlayRenderer.transform.localScale = Vector3.one *
                (_frozenOverlayBaseScale * pulse);
            Color color = _frozenOverlayRenderer.color;
            color.a = 0.88f + Mathf.Sin(Time.unscaledTime * 5f) * 0.08f;
            _frozenOverlayRenderer.color = color;
        }
        if (_shattered)
        {
            for (int i = 0; i < _shards.Count; i++)
            {
                Shard shard = _shards[i];
                if (shard.Transform == null) continue;
                shard.Velocity.y -= 9f * dt;
                shard.Transform.localPosition += (Vector3)(shard.Velocity * dt);
                shard.Transform.Rotate(0f, 0f, shard.AngularVelocity * dt);
                _shards[i] = shard;
            }
            return;
        }
        if (_renderer == null || _dying) return;

        _frameTimer += dt;
        if (_skillRemaining > 0f)
        {
            _skillRemaining = Mathf.Max(0f, _skillRemaining - dt);
            int idleStart = _frames.Count >= 6 ? 4 : 0;
            int idleCount = _frames.Count >= 6 ? 2 : 1;
            SetFrame(idleStart + Mathf.FloorToInt(_frameTimer * 2f) % idleCount);
            return;
        }

        _renderer.transform.localScale = Vector3.one * _visualScale;
        _renderer.transform.localRotation = Quaternion.identity;
        _renderer.transform.localPosition = Vector3.zero;
        _renderer.color = Color.white;

        if (_moving && _frames.Count >= 4)
        {
            SetFrame(Mathf.FloorToInt(_frameTimer * 8f) % 4);
        }
        else
        {
            int idleStart = _frames.Count >= 6 ? 4 : 0;
            int idleCount = _frames.Count >= 6 ? 2 : 1;
            SetFrame(idleStart + Mathf.FloorToInt(_frameTimer * 2f) % idleCount);
        }
    }

    private void SetFrame(int index)
    {
        if (_renderer == null || _frames.Count == 0) return;
        index = Mathf.Clamp(index, 0, _frames.Count - 1);
        _renderer.sprite = _frames[index];
        _currentFrameRect = _frames[index].rect;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _frames.Count; i++) if (_frames[i] != null) Destroy(_frames[i]);
        for (int i = 0; i < _shardSprites.Count; i++) if (_shardSprites[i] != null) Destroy(_shardSprites[i]);
    }
}
