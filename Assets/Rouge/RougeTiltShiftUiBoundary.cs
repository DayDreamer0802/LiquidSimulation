using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[DefaultExecutionOrder(1200)]
public sealed class RougeTiltShiftUiBoundary : MonoBehaviour
{
    private readonly Vector3[] _worldCorners = new Vector3[4];
    private RectTransform _rectTransform;
    private int _lastScreenHeight;
    private float _lastTop = -1f;

    private void OnEnable()
    {
        _rectTransform = GetComponent<RectTransform>();
        UpdateBoundary(true);
    }

    private void LateUpdate()
    {
        UpdateBoundary(false);
    }

    private void UpdateBoundary(bool force)
    {
        if (_rectTransform == null || Screen.height <= 0) return;
        _rectTransform.GetWorldCorners(_worldCorners);
        float top = Mathf.Clamp01(Mathf.Max(_worldCorners[1].y,
            _worldCorners[2].y) / Screen.height);
        if (!force && _lastScreenHeight == Screen.height &&
            Mathf.Abs(_lastTop - top) <= 0.0005f) return;
        _lastScreenHeight = Screen.height;
        _lastTop = top;
        RougeTiltShiftCamera.SetUiTopNormalized(top);
    }

    private void OnDisable()
    {
        RougeTiltShiftCamera.ClearUiTopBoundary();
    }
}
