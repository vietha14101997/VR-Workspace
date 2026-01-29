using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Marquee text component that auto-scrolls text horizontally when it overflows.
/// Shows "..." when text doesn't fit, then scrolls automatically.
/// </summary>
public class MarqueeText : MonoBehaviour
{
    private TextMeshProUGUI _text;
    private RectTransform _textRT;
    private RectTransform _maskRT;
    private RectMask2D _mask;

    // Scroll settings
    private float _scrollSpeed = 50f;      // Pixels per second
    private float _pauseDuration = 2f;     // Pause at start/end
    private float _endPauseDuration = 1f;  // Pause at end before reset

    // State
    private bool _isOverflowing = false;
    private float _scrollOffset = 0f;
    private float _maxScrollOffset = 0f;
    private float _pauseTimer = 0f;
    private bool _pausingAtEnd = false;
    private bool _centerWhenFits = false;

    public TextMeshProUGUI TextComponent => _text;
    public bool IsScrolling { get; private set; }

    /// <summary>
    /// Setup marquee text with existing TextMeshProUGUI.
    /// </summary>
    /// <param name="textComponent">The TextMeshProUGUI to wrap</param>
    /// <param name="scrollSpeed">Scroll speed in pixels per second</param>
    /// <param name="centerWhenFits">If true, center text when it fits; if false, align left</param>
    public static MarqueeText Setup(TextMeshProUGUI textComponent, float scrollSpeed = 50f, bool centerWhenFits = false)
    {
        return Setup(textComponent, scrollSpeed, centerWhenFits, -1f);
    }

    /// <summary>
    /// Setup with explicit height for LayoutGroup compatibility.
    /// </summary>
    /// <param name="textComponent">The TextMeshProUGUI to wrap</param>
    /// <param name="scrollSpeed">Scroll speed in pixels per second</param>
    /// <param name="centerWhenFits">If true, center text when it fits; if false, align left</param>
    /// <param name="explicitHeight">Explicit height for the mask container. Use -1 to auto-detect.</param>
    public static MarqueeText Setup(TextMeshProUGUI textComponent, float scrollSpeed, bool centerWhenFits, float explicitHeight)
    {
        if (textComponent == null) return null;

        // Create mask container as parent
        GameObject maskObj = new GameObject("MarqueeMask");
        RectTransform originalParent = textComponent.transform.parent as RectTransform;
        GameObject textGameObject = textComponent.gameObject;
        maskObj.transform.SetParent(originalParent, false);

        // Copy text's RectTransform settings to mask
        RectTransform textRT = textComponent.rectTransform;
        RectTransform maskRT = maskObj.AddComponent<RectTransform>();
        maskRT.anchorMin = textRT.anchorMin;
        maskRT.anchorMax = textRT.anchorMax;
        maskRT.pivot = textRT.pivot;
        maskRT.anchoredPosition = textRT.anchoredPosition;
        maskRT.sizeDelta = textRT.sizeDelta;
        maskRT.offsetMin = textRT.offsetMin;
        maskRT.offsetMax = textRT.offsetMax;

        // Copy LayoutElement from text to mask if it exists (for LayoutGroup compatibility)
        var textLayoutElement = textGameObject.GetComponent<UnityEngine.UI.LayoutElement>();
        if (textLayoutElement != null)
        {
            var maskLayoutElement = maskObj.AddComponent<UnityEngine.UI.LayoutElement>();
            maskLayoutElement.minWidth = textLayoutElement.minWidth;
            maskLayoutElement.minHeight = textLayoutElement.minHeight;
            maskLayoutElement.preferredWidth = textLayoutElement.preferredWidth;
            maskLayoutElement.preferredHeight = textLayoutElement.preferredHeight;
            maskLayoutElement.flexibleWidth = textLayoutElement.flexibleWidth;
            maskLayoutElement.flexibleHeight = textLayoutElement.flexibleHeight;
            maskLayoutElement.ignoreLayout = textLayoutElement.ignoreLayout;
            
            // Remove LayoutElement from text since mask now handles layout
            UnityEngine.Object.Destroy(textLayoutElement);
        }
        else if (explicitHeight > 0)
        {
            // Add LayoutElement with explicit height if specified
            var maskLayoutElement = maskObj.AddComponent<UnityEngine.UI.LayoutElement>();
            maskLayoutElement.preferredHeight = explicitHeight;
            maskLayoutElement.minHeight = explicitHeight;
            maskLayoutElement.flexibleHeight = 0;
        }

        // Add mask component
        RectMask2D mask = maskObj.AddComponent<RectMask2D>();

        // Reparent text under mask
        textComponent.transform.SetParent(maskObj.transform, false);

        // Reset text position within mask - stretch vertically, expand horizontally
        textRT.anchorMin = new Vector2(0, 0);
        textRT.anchorMax = new Vector2(0, 1);
        textRT.pivot = new Vector2(0, 0.5f);
        textRT.anchoredPosition = Vector2.zero;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        // Width needs to be large enough to show full text
        textRT.sizeDelta = new Vector2(2000f, 0); // Large width, height from anchors

        // Disable built-in overflow handling
        textComponent.enableWordWrapping = false;
        textComponent.overflowMode = TextOverflowModes.Overflow;

        // Add marquee component
        MarqueeText marquee = maskObj.AddComponent<MarqueeText>();
        marquee._text = textComponent;
        marquee._textRT = textRT;
        marquee._maskRT = maskRT;
        marquee._mask = mask;
        marquee._scrollSpeed = scrollSpeed;
        marquee._centerWhenFits = centerWhenFits;

        return marquee;
    }

    private void OnEnable()
    {
        // Re-check overflow when enabled, as layout might have changed or was invalid
        if (_text != null)
        {
            SetText(_text.text);
        }
    }

    /// <summary>
    /// Set text and check if scrolling is needed.
    /// </summary>
    public void SetText(string text)
    {
        if (_text == null) return;

        _text.text = text;

        // Force layout update to get accurate text width
        _text.ForceMeshUpdate();

        // Check overflow after layout - may need to retry if layout not ready
        Canvas.ForceUpdateCanvases();
        
        // If container has no width yet, delay the check
        if (_maskRT != null && _maskRT.rect.width <= 0)
        {
            // Start coroutine to check after layout is ready
            StartCoroutine(DelayedCheckOverflow());
        }
        else
        {
            CheckOverflow();
        }
    }

    private System.Collections.IEnumerator DelayedCheckOverflow()
    {
        // Wait up to 5 frames for valid width
        int visualFrames = 0;
        while (_maskRT != null && _maskRT.rect.width <= 0 && visualFrames < 5)
        {
            yield return null; // Wait for Update
            Canvas.ForceUpdateCanvases(); // Try to force update
            visualFrames++;
        }
        
        CheckOverflow();
    }

    private void CheckOverflow()
    {
        if (_text == null || _maskRT == null) return;

        float textWidth = _text.preferredWidth;
        float containerWidth = _maskRT.rect.width;

        _isOverflowing = textWidth > containerWidth;
        _maxScrollOffset = Mathf.Max(0, textWidth - containerWidth + 20f); // +20 for padding

        if (_isOverflowing)
        {
            // Start with pause before scrolling
            _scrollOffset = 0f;
            _pauseTimer = _pauseDuration;
            IsScrolling = false;
            _pausingAtEnd = false;
        }
        else
        {
            // No overflow, reset position
            _scrollOffset = 0f;
            IsScrolling = false;
        }

        UpdateTextPosition();
    }

    private void Update()
    {
        if (!_isOverflowing || _text == null || !_isActive) return;

        if (_pauseTimer > 0)
        {
            _pauseTimer -= Time.deltaTime;
            return;
        }

        if (_pausingAtEnd)
        {
            // Reset to start
            _scrollOffset = 0f;
            _pauseTimer = _pauseDuration;
            _pausingAtEnd = false;
            IsScrolling = false;
            UpdateTextPosition();
            return;
        }

        // Scroll
        IsScrolling = true;
        _scrollOffset += _scrollSpeed * Time.deltaTime;

        if (_scrollOffset >= _maxScrollOffset)
        {
            _scrollOffset = _maxScrollOffset;
            _pauseTimer = _endPauseDuration;
            _pausingAtEnd = true;
            IsScrolling = false;
        }

        UpdateTextPosition();
    }

    private void UpdateTextPosition()
    {
        if (_textRT == null || _maskRT == null) return;

        float containerWidth = _maskRT.rect.width;
        
        // Safety: If container has no width yet, position at 0 (will be recalculated later)
        if (containerWidth <= 0)
        {
            _textRT.anchoredPosition = Vector2.zero;
            return;
        }

        if (_isOverflowing)
        {
            // Scrolling mode - always from left
            _textRT.anchoredPosition = new Vector2(-_scrollOffset, 0);
        }
        else if (_centerWhenFits)
        {
            // Center text when it fits
            float textWidth = _text.preferredWidth;
            float centerOffset = Mathf.Max(0, (containerWidth - textWidth) / 2f);
            _textRT.anchoredPosition = new Vector2(centerOffset, 0);
        }
        else
        {
            // Left aligned
            _textRT.anchoredPosition = new Vector2(0, 0);
        }
    }

    /// <summary>
    /// Reset scroll position to start.
    /// </summary>
    public void ResetScroll()
    {
        _scrollOffset = 0f;
        _pauseTimer = _pauseDuration;
        IsScrolling = false;
        _pausingAtEnd = false;
        _isActive = true; // Keep active by default for existing usage
        UpdateTextPosition();
    }

    #region Hover-Triggered Scrolling
    private bool _isActive = true;  // Controls whether scrolling is active
    private bool _hoverMode = false; // If true, only scroll when active (on hover)

    /// <summary>
    /// Enable hover mode - scrolling only happens when StartScroll() is called.
    /// </summary>
    public void SetHoverMode(bool enabled)
    {
        _hoverMode = enabled;
        if (enabled)
        {
            _isActive = false;
            ResetScrollPosition();
        }
    }

    /// <summary>
    /// Set scroll speed.
    /// </summary>
    public void SetScrollSpeed(float speed)
    {
        _scrollSpeed = speed;
    }

    /// <summary>
    /// Start scrolling (for hover mode).
    /// </summary>
    public void StartScroll()
    {
        if (!_hoverMode) return;
        _isActive = true;
        _scrollOffset = 0f;
        _pausingAtEnd = false;
        
        CheckOverflow();
        
        // Override pause timer set by CheckOverflow to start immediately
        if (_isOverflowing)
        {
            _pauseTimer = 0f;
        }
    }

    /// <summary>
    /// Stop scrolling and reset (for hover mode).
    /// </summary>
    public void StopScroll()
    {
        if (!_hoverMode) return;
        _isActive = false;
        ResetScrollPosition();
    }

    private void ResetScrollPosition()
    {
        _scrollOffset = 0f;
        IsScrolling = false;
        _pausingAtEnd = false;
        UpdateTextPosition();
    }
    #endregion
}
