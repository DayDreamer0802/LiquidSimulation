using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class RougeTowerDefenseMapLoader
{
    private sealed class FailureTileVisual
    {
        public TileVisualState visual;
        public float normalizedDistance;
        public float twist;
        public Vector3 direction;
    }

    public IEnumerator PlayFailureDisintegration(Vector3 epicenter,
        float duration)
    {
        if (_runtimeRoot == null || _tileVisuals.Count == 0) yield break;
        duration = Mathf.Max(0.25f, duration);

        var tiles = new List<FailureTileVisual>(_tileVisuals.Count);
        float farthestDistance = 0.01f;
        foreach (TileVisualState visual in _tileVisuals.Values)
        {
            if (visual?.root == null) continue;
            Vector3 delta = visual.root.position - epicenter;
            delta.y = 0f;
            float distance = delta.magnitude;
            farthestDistance = Mathf.Max(farthestDistance, distance);
            tiles.Add(new FailureTileVisual
            {
                visual = visual,
                normalizedDistance = distance,
                direction = distance > 0.01f ? delta / distance : Vector3.forward,
                twist = Mathf.Sin(visual.root.position.x * 1.73f +
                                  visual.root.position.z * 2.11f)
            });
        }
        tiles.Sort((left, right) =>
            left.normalizedDistance.CompareTo(right.normalizedDistance));
        for (int i = 0; i < tiles.Count; i++)
            tiles[i].normalizedDistance = Mathf.Clamp01(
                tiles[i].normalizedDistance / farthestDistance);

        float elapsed = 0f;
        while (elapsed < duration && _runtimeRoot != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float wave = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < tiles.Count; i++)
            {
                FailureTileVisual tile = tiles[i];
                Transform root = tile.visual.root;
                if (root == null) continue;

                // Each ring starts only when the expanding blast reaches it.
                float local = Mathf.Clamp01((wave -
                    tile.normalizedDistance * 0.72f) / 0.28f);
                if (local <= 0f) continue;
                float eased = 1f - Mathf.Pow(1f - local, 3f);
                root.localScale = tile.visual.originalLocalScale *
                                  Mathf.Max(0f, 1f - eased);
                root.localPosition = tile.visual.originalLocalPosition +
                                     tile.direction * (eased * 1.4f) +
                                     Vector3.down * (eased * eased * 2.8f);
                root.localRotation = Quaternion.Euler(eased * 42f * tile.twist,
                    eased * 76f * tile.twist, eased * 24f);
                SetFailureTileAlpha(tile.visual, 1f - eased);
                if (local >= 0.995f) root.gameObject.SetActive(false);
            }
            yield return null;
        }

        if (_runtimeRoot != null) _runtimeRoot.SetActive(false);
    }

    private static void SetFailureTileAlpha(TileVisualState visual, float alpha)
    {
        if (visual?.renderers == null) return;
        for (int i = 0; i < visual.renderers.Length; i++)
        {
            Renderer renderer = visual.renderers[i];
            if (renderer == null) continue;
            if (renderer is SpriteRenderer sprite)
            {
                Color color = i < visual.spriteColors.Length
                    ? visual.spriteColors[i]
                    : sprite.color;
                color.a *= Mathf.Clamp01(alpha);
                sprite.color = color;
            }
            else
            {
                renderer.enabled = alpha > 0.02f;
            }
        }
    }
}
