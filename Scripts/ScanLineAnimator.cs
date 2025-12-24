using UnityEngine;

/// <summary>
/// Animation component cho scan line di chuyển lên xuống trong QR Scanner
/// </summary>
public class ScanLineAnimator : MonoBehaviour
{
    [Header("Animation Settings")]
    public float duration = 2f;
    public float startY = 0.95f;
    public float endY = 0.05f;

    private RectTransform _rt;
    private float _progress;
    private bool _isAnimating = true;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
    }

    void Update()
    {
        if (!_isAnimating || _rt == null) return;

        _progress += Time.deltaTime / duration;
        if (_progress > 1f) _progress = 0f;

        // Animate Y position từ top xuống bottom
        float yAnchor = Mathf.Lerp(startY, endY, _progress);
        _rt.anchorMin = new Vector2(_rt.anchorMin.x, yAnchor);
        _rt.anchorMax = new Vector2(_rt.anchorMax.x, yAnchor);
    }

    /// <summary>
    /// Dừng animation
    /// </summary>
    public void Stop()
    {
        _isAnimating = false;
    }

    /// <summary>
    /// Tiếp tục animation
    /// </summary>
    public void Resume()
    {
        _isAnimating = true;
    }

    /// <summary>
    /// Reset về vị trí ban đầu
    /// </summary>
    public void Reset()
    {
        _progress = 0f;
        if (_rt != null)
        {
            _rt.anchorMin = new Vector2(_rt.anchorMin.x, startY);
            _rt.anchorMax = new Vector2(_rt.anchorMax.x, startY);
        }
    }
}
