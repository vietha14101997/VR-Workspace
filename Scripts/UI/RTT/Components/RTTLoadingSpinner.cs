using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Reusable loading spinner component for RTT apps.
/// Shows a rotating spinner with fade in/out animations.
/// </summary>
public class RTTLoadingSpinner : MonoBehaviour
{
    #region Constants
    private const float SPIN_SPEED = 360f;  // Degrees per second (1 rotation/sec)
    private const float FADE_DURATION = 0.15f;
    private const float SPINNER_SIZE = 80f;
    private const int SPINNER_SEGMENTS = 12;
    #endregion

    #region Private Fields
    private RectTransform _rectTransform;
    private CanvasGroup _canvasGroup;
    private Image _spinnerImage;
    private bool _isSpinning;
    private Coroutine _spinCoroutine;
    private Coroutine _fadeCoroutine;
    #endregion

    #region Factory
    /// <summary>
    /// Create a loading spinner centered in the parent container.
    /// </summary>
    public static RTTLoadingSpinner Create(Transform parent, Color? color = null)
    {
        GameObject spinnerObj = new GameObject("LoadingSpinner");
        spinnerObj.transform.SetParent(parent, false);

        var spinner = spinnerObj.AddComponent<RTTLoadingSpinner>();
        spinner.Initialize(color ?? Color.white);

        return spinner;
    }
    #endregion

    #region Initialization
    private void Initialize(Color color)
    {
        // Setup RectTransform - centered
        _rectTransform = gameObject.AddComponent<RectTransform>();
        _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _rectTransform.sizeDelta = new Vector2(SPINNER_SIZE, SPINNER_SIZE);
        _rectTransform.anchoredPosition = Vector2.zero;

        // Add CanvasGroup for fading
        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        // Create spinner visual
        CreateSpinnerVisual(color);

        // Start hidden
        gameObject.SetActive(false);
    }

    private void CreateSpinnerVisual(Color color)
    {
        // Try to load spinner icon from Resources
        Sprite spinnerSprite = Resources.Load<Sprite>("icon_spinner");
        if (spinnerSprite == null)
            spinnerSprite = Resources.Load<Sprite>("Icons/icon_spinner");

        if (spinnerSprite != null)
        {
            // Use sprite-based spinner
            _spinnerImage = gameObject.AddComponent<Image>();
            _spinnerImage.sprite = spinnerSprite;
            _spinnerImage.color = color;
            _spinnerImage.raycastTarget = false;
        }
        else
        {
            // Create procedural spinner (segmented circle)
            CreateProceduralSpinner(color);
        }
    }

    private void CreateProceduralSpinner(Color color)
    {
        // Create a simple ring spinner using multiple segments
        for (int i = 0; i < SPINNER_SEGMENTS; i++)
        {
            float angle = (float)i / SPINNER_SEGMENTS * 360f;
            float alpha = (float)i / SPINNER_SEGMENTS;  // Gradient alpha

            GameObject segment = new GameObject($"Segment_{i}");
            segment.transform.SetParent(transform, false);

            var segmentRT = segment.AddComponent<RectTransform>();
            segmentRT.anchorMin = new Vector2(0.5f, 0.5f);
            segmentRT.anchorMax = new Vector2(0.5f, 0.5f);
            segmentRT.pivot = new Vector2(0.5f, 0f);
            segmentRT.sizeDelta = new Vector2(6f, SPINNER_SIZE * 0.35f);

            // Position around center
            segmentRT.localRotation = Quaternion.Euler(0, 0, -angle);
            segmentRT.anchoredPosition = Vector2.zero;

            var segmentImage = segment.AddComponent<Image>();
            segmentImage.color = new Color(color.r, color.g, color.b, alpha * 0.8f);
            segmentImage.raycastTarget = false;

            // Round the segments
            segmentImage.sprite = GetRoundedSprite();
            segmentImage.type = Image.Type.Sliced;
        }
    }

    private static Sprite _cachedRoundedSprite;
    private static Sprite GetRoundedSprite()
    {
        if (_cachedRoundedSprite != null) return _cachedRoundedSprite;

        int size = 16;
        int radius = 4;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - halfSize + 0.5f);
                float dy = Mathf.Abs(y - halfSize + 0.5f);

                float innerHalfX = halfSize - radius;
                float innerHalfY = halfSize - radius;

                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - radius;

                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        _cachedRoundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            Vector2.one * 0.5f,
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)
        );

        return _cachedRoundedSprite;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Show the spinner with fade in animation.
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
        _isSpinning = true;

        // Stop any running fade
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        // Start fade in and spin
        _fadeCoroutine = StartCoroutine(FadeIn());

        if (_spinCoroutine == null)
            _spinCoroutine = StartCoroutine(Spin());
    }

    /// <summary>
    /// Hide the spinner with fade out animation.
    /// </summary>
    public void Hide()
    {
        if (!gameObject.activeSelf) return;

        // Stop any running fade
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeOutAndHide());
    }

    /// <summary>
    /// Immediately hide without animation.
    /// </summary>
    public void HideImmediate()
    {
        _isSpinning = false;

        if (_spinCoroutine != null)
        {
            StopCoroutine(_spinCoroutine);
            _spinCoroutine = null;
        }

        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }
    #endregion

    #region Animation Coroutines
    private IEnumerator Spin()
    {
        while (_isSpinning)
        {
            transform.Rotate(0, 0, -SPIN_SPEED * Time.deltaTime);
            yield return null;
        }
        _spinCoroutine = null;
    }

    private IEnumerator FadeIn()
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / FADE_DURATION;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, t);
            yield return null;
        }

        _canvasGroup.alpha = 1f;
        _fadeCoroutine = null;
    }

    private IEnumerator FadeOutAndHide()
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / FADE_DURATION;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }

        _isSpinning = false;
        if (_spinCoroutine != null)
        {
            StopCoroutine(_spinCoroutine);
            _spinCoroutine = null;
        }

        _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
        _fadeCoroutine = null;
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        if (_spinCoroutine != null)
            StopCoroutine(_spinCoroutine);
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);
    }
    #endregion
}
