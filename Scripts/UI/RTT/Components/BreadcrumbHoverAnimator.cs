using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Animates hover effect for breadcrumb buttons.
/// Supports both GlassGradientBackgroundWide (first button) and ChevronBackground (other buttons) shaders.
/// </summary>
public class BreadcrumbHoverAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Image _bgImage;
    private Material _material;
    private bool _isFirstButton;

    // Colors
    private Color _normalColor;
    private Color _hoverColor;
    private float _normalAlpha;
    private float _hoverAlpha;

    // Animation state
    private float _currentProgress = 0f;
    private float _targetProgress = 0f;
    private const float ANIMATION_DURATION = 0.15f;

    public void Initialize(Image bgImage, Color normalColor, Color hoverColor, float normalAlpha, float hoverAlpha, bool isFirstButton)
    {
        _bgImage = bgImage;
        _normalColor = normalColor;
        _hoverColor = hoverColor;
        _normalAlpha = normalAlpha;
        _hoverAlpha = hoverAlpha;
        _isFirstButton = isFirstButton;

        if (_bgImage != null)
        {
            _material = _bgImage.material;
        }

        // Apply initial state
        ApplyColors(0f);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _targetProgress = 1f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _targetProgress = 0f;
    }

    private void Update()
    {
        if (Mathf.Approximately(_currentProgress, _targetProgress)) return;

        float speed = 1f / ANIMATION_DURATION;
        _currentProgress = Mathf.MoveTowards(_currentProgress, _targetProgress, Time.unscaledDeltaTime * speed);
        ApplyColors(_currentProgress);
    }

    private void ApplyColors(float progress)
    {
        if (_material == null) return;

        // Lerp between normal and hover colors
        Color currentColor = Color.Lerp(_normalColor, _hoverColor, progress);
        float currentAlpha = Mathf.Lerp(_normalAlpha, _hoverAlpha, progress);

        if (_isFirstButton)
        {
            // GlassGradientBackgroundWide shader
            Color colorA = new Color(currentColor.r, currentColor.g, currentColor.b, currentAlpha * 1.5f);
            Color colorB = new Color(currentColor.r, currentColor.g, currentColor.b, currentAlpha * 0.5f);
            _material.SetColor("_ColorA", colorA);
            _material.SetColor("_ColorB", colorB);
            _material.SetFloat("_GlassAlpha", currentAlpha);
        }
        else
        {
            // ChevronBackground shader
            _material.SetColor("_BackgroundColor", currentColor);
            _material.SetFloat("_BackgroundAlpha", currentAlpha);
        }
    }
}
