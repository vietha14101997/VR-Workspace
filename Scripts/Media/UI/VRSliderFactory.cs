using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;

/// <summary>
/// Factory for creating VR-optimized slider controls.
/// Sliders have larger touch areas and visual feedback for VR interaction.
/// </summary>
public static class VRSliderFactory
{
    #region Constants
    private const float DEFAULT_WIDTH = 400f;
    private const float DEFAULT_HEIGHT = 48f;
    private const float HANDLE_SIZE = 32f;
    private const float TRACK_HEIGHT = 8f;
    private const float TOUCH_AREA_HEIGHT = 80f;  // Larger touch area for VR
    #endregion

    #region Slider Styles
    public enum SliderStyle
    {
        Default,     // Standard slider
        Progress,    // Timeline/progress style (like video seek bar)
        Volume,      // Volume control style
        Setting      // Settings slider style
    }
    #endregion

    #region Factory Methods
    /// <summary>
    /// Create a VR-optimized slider.
    /// </summary>
    public static VRSliderControl CreateSlider(
        Transform parent,
        float width,
        float height,
        TMP_FontAsset font,
        Color primaryColor,
        SliderStyle style = SliderStyle.Default,
        float minValue = 0f,
        float maxValue = 1f,
        bool showLabel = false,
        string label = "",
        float touchExpand = 0.025f)
    {
        GameObject sliderObj = new GameObject("VRSlider");
        sliderObj.transform.SetParent(parent, false);

        var sliderRT = sliderObj.AddComponent<RectTransform>();
        sliderRT.sizeDelta = new Vector2(width, height);

        // Create slider component
        var sliderControl = sliderObj.AddComponent<VRSliderControl>();

        // Build UI based on style
        switch (style)
        {
            case SliderStyle.Progress:
                BuildProgressSlider(sliderObj, width, height, font, primaryColor, touchExpand);
                break;
            case SliderStyle.Volume:
                BuildVolumeSlider(sliderObj, width, height, font, primaryColor, touchExpand);
                break;
            case SliderStyle.Setting:
                BuildSettingSlider(sliderObj, width, height, font, primaryColor, showLabel, label, touchExpand);
                break;
            default:
                BuildDefaultSlider(sliderObj, width, height, font, primaryColor, touchExpand);
                break;
        }

        // Configure slider values
        sliderControl.SetRange(minValue, maxValue);

        return sliderControl;
    }

    /// <summary>
    /// Create a timeline/seek bar slider for video playback.
    /// </summary>
    public static VRSliderControl CreateTimelineSlider(
        Transform parent,
        float width,
        TMP_FontAsset font,
        Color primaryColor,
        float previewHeight = 0f,
        float touchExpand = 0.025f)
    {
        GameObject sliderObj = new GameObject("TimelineSlider");
        sliderObj.transform.SetParent(parent, false);

        var sliderRT = sliderObj.AddComponent<RectTransform>();
        sliderRT.sizeDelta = new Vector2(width, DEFAULT_HEIGHT);

        var sliderControl = sliderObj.AddComponent<VRSliderControl>();

        // Track background
        GameObject trackBgObj = new GameObject("TrackBackground");
        trackBgObj.transform.SetParent(sliderObj.transform, false);

        var trackBgRT = trackBgObj.AddComponent<RectTransform>();
        trackBgRT.anchorMin = new Vector2(0, 0.5f);
        trackBgRT.anchorMax = new Vector2(1, 0.5f);
        trackBgRT.pivot = new Vector2(0.5f, 0.5f);
        trackBgRT.anchoredPosition = Vector2.zero;
        trackBgRT.sizeDelta = new Vector2(0, TRACK_HEIGHT);

        var trackBgImage = trackBgObj.AddComponent<Image>();
        trackBgImage.color = new Color(1, 1, 1, 0.2f);

        // Buffer/loaded progress (for streaming - optional)
        GameObject bufferObj = new GameObject("BufferProgress");
        bufferObj.transform.SetParent(sliderObj.transform, false);

        var bufferRT = bufferObj.AddComponent<RectTransform>();
        bufferRT.anchorMin = new Vector2(0, 0.5f);
        bufferRT.anchorMax = new Vector2(0, 0.5f);
        bufferRT.pivot = new Vector2(0, 0.5f);
        bufferRT.anchoredPosition = Vector2.zero;
        bufferRT.sizeDelta = new Vector2(0, TRACK_HEIGHT);

        var bufferImage = bufferObj.AddComponent<Image>();
        bufferImage.color = new Color(1, 1, 1, 0.3f);

        // Progress fill
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(sliderObj.transform, false);

        var fillRT = fillObj.AddComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0, 0.5f);
        fillRT.anchorMax = new Vector2(0, 0.5f);
        fillRT.pivot = new Vector2(0, 0.5f);
        fillRT.anchoredPosition = Vector2.zero;
        fillRT.sizeDelta = new Vector2(0, TRACK_HEIGHT);

        var fillImage = fillObj.AddComponent<Image>();
        fillImage.color = primaryColor;

        // Handle
        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(sliderObj.transform, false);

        var handleRT = handleObj.AddComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0, 0.5f);
        handleRT.anchorMax = new Vector2(0, 0.5f);
        handleRT.pivot = new Vector2(0.5f, 0.5f);
        handleRT.anchoredPosition = Vector2.zero;
        handleRT.sizeDelta = new Vector2(HANDLE_SIZE, HANDLE_SIZE);

        var handleImage = handleObj.AddComponent<Image>();
        handleImage.color = Color.white;

        // Make handle circular
        handleImage.sprite = CreateCircleSprite();
        handleImage.type = Image.Type.Simple;

        // Touch area (invisible, but larger)
        GameObject touchObj = new GameObject("TouchArea");
        touchObj.transform.SetParent(sliderObj.transform, false);

        var touchRT = touchObj.AddComponent<RectTransform>();
        // Expand horizontally by 2.5% on each side using anchors (scales with dynamic width)
        touchRT.anchorMin = new Vector2(-touchExpand, 0f);
        touchRT.anchorMax = new Vector2(1.0f + touchExpand, 1f);
        // Expand vertically using offsets (fixed pixel amount)
        touchRT.offsetMin = new Vector2(0, -(TOUCH_AREA_HEIGHT - DEFAULT_HEIGHT) / 2);
        touchRT.offsetMax = new Vector2(0, (TOUCH_AREA_HEIGHT - DEFAULT_HEIGHT) / 2);

        // Raycast target for touch area
        var touchImage = touchObj.AddComponent<Image>();
        touchImage.color = Color.clear;

        // BoxCollider for VR interaction
        var collider = touchObj.AddComponent<BoxCollider>();
        // Use dynamic width to match the visual extension
        collider.size = new Vector3(width * (1f + touchExpand * 2f), TOUCH_AREA_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);

        // Configure slider control
        sliderControl.Initialize(fillRT, handleRT, bufferRT, width, previewHeight);

        return sliderControl;
    }

    /// <summary>
    /// Create a volume slider.
    /// </summary>
    public static VRSliderControl CreateVolumeSlider(
        Transform parent,
        float width,
        TMP_FontAsset font,
        Color primaryColor,
        float touchExpand = 0.025f)
    {
        return CreateSlider(parent, width, DEFAULT_HEIGHT, font, primaryColor, SliderStyle.Volume, 0f, 1f, false, "", touchExpand);
    }
    #endregion

    #region Build Methods
    private static void BuildDefaultSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor, float touchExpand)
    {
        // Track background
        GameObject trackBgObj = new GameObject("TrackBackground");
        trackBgObj.transform.SetParent(sliderObj.transform, false);

        var trackBgRT = trackBgObj.AddComponent<RectTransform>();
        trackBgRT.anchorMin = new Vector2(0, 0.5f);
        trackBgRT.anchorMax = new Vector2(1, 0.5f);
        trackBgRT.pivot = new Vector2(0.5f, 0.5f);
        trackBgRT.anchoredPosition = Vector2.zero;
        trackBgRT.sizeDelta = new Vector2(0, TRACK_HEIGHT);

        var trackBgImage = trackBgObj.AddComponent<Image>();
        trackBgImage.color = new Color(1, 1, 1, 0.2f);

        // Fill
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(sliderObj.transform, false);

        var fillRT = fillObj.AddComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0, 0.5f);
        fillRT.anchorMax = new Vector2(0, 0.5f);
        fillRT.pivot = new Vector2(0, 0.5f);
        fillRT.anchoredPosition = Vector2.zero;
        fillRT.sizeDelta = new Vector2(0, TRACK_HEIGHT);

        var fillImage = fillObj.AddComponent<Image>();
        fillImage.color = primaryColor;

        // Handle
        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(sliderObj.transform, false);

        var handleRT = handleObj.AddComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0, 0.5f);
        handleRT.anchorMax = new Vector2(0, 0.5f);
        handleRT.pivot = new Vector2(0.5f, 0.5f);
        handleRT.anchoredPosition = Vector2.zero;
        handleRT.sizeDelta = new Vector2(HANDLE_SIZE, HANDLE_SIZE);

        var handleImage = handleObj.AddComponent<Image>();
        handleImage.color = Color.white;
        handleImage.sprite = CreateCircleSprite();

        // Touch area
        GameObject touchObj = new GameObject("TouchArea");
        touchObj.transform.SetParent(sliderObj.transform, false);

        var touchRT = touchObj.AddComponent<RectTransform>();
        // Expand horizontally by touchExpand on each side
        touchRT.anchorMin = new Vector2(-touchExpand, 0f);
        touchRT.anchorMax = new Vector2(1.0f + touchExpand, 1f);
        touchRT.offsetMin = Vector2.zero;
        touchRT.offsetMax = Vector2.zero;

        var touchImage = touchObj.AddComponent<Image>();
        touchImage.color = Color.clear;

        var collider = touchObj.AddComponent<BoxCollider>();
        // Use dynamic width to match the visual extension
        collider.size = new Vector3(width * (1f + touchExpand * 2f), height, 10);
        collider.center = new Vector3(0, 0, -5);

        // Configure
        var sliderControl = sliderObj.GetComponent<VRSliderControl>();
        sliderControl.Initialize(fillRT, handleRT, null, width);
    }

    private static void BuildProgressSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor, float touchExpand)
    {
        // Same as timeline slider but without buffer
        BuildDefaultSlider(sliderObj, width, height, font, primaryColor, touchExpand);
    }

    private static void BuildVolumeSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor, float touchExpand)
    {
        // Similar to default but with volume-specific styling
        BuildDefaultSlider(sliderObj, width, height, font, primaryColor, touchExpand);
    }

    private static void BuildSettingSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor, bool showLabel, string label, float touchExpand)
    {
        float labelWidth = showLabel ? 120f : 0f;
        float sliderWidth = width - labelWidth;

        if (showLabel && !string.IsNullOrEmpty(label))
        {
            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(sliderObj.transform, false);

            var labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0, 1);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = Vector2.zero;
            labelRT.sizeDelta = new Vector2(labelWidth, 0);

            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.font = font;
            labelText.fontSize = 24;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        // Slider area
        GameObject sliderAreaObj = new GameObject("SliderArea");
        sliderAreaObj.transform.SetParent(sliderObj.transform, false);

        var sliderAreaRT = sliderAreaObj.AddComponent<RectTransform>();
        sliderAreaRT.anchorMin = new Vector2(labelWidth / width, 0);
        sliderAreaRT.anchorMax = Vector2.one;
        sliderAreaRT.offsetMin = Vector2.zero;
        sliderAreaRT.offsetMax = Vector2.zero;

        // Track background
        GameObject trackBgObj = new GameObject("TrackBackground");
        trackBgObj.transform.SetParent(sliderAreaObj.transform, false);

        var trackBgRT = trackBgObj.AddComponent<RectTransform>();
        trackBgRT.anchorMin = new Vector2(0, 0.5f);
        trackBgRT.anchorMax = new Vector2(1, 0.5f);
        trackBgRT.pivot = new Vector2(0.5f, 0.5f);
        trackBgRT.anchoredPosition = Vector2.zero;
        trackBgRT.sizeDelta = new Vector2(0, TRACK_HEIGHT);

        var trackBgImage = trackBgObj.AddComponent<Image>();
        trackBgImage.color = new Color(1, 1, 1, 0.2f);

        // Fill
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(sliderAreaObj.transform, false);

        var fillRT = fillObj.AddComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0, 0.5f);
        fillRT.anchorMax = new Vector2(0, 0.5f);
        fillRT.pivot = new Vector2(0, 0.5f);
        fillRT.anchoredPosition = Vector2.zero;
        fillRT.sizeDelta = new Vector2(0, TRACK_HEIGHT);

        var fillImage = fillObj.AddComponent<Image>();
        fillImage.color = primaryColor;

        // Handle
        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(sliderAreaObj.transform, false);

        var handleRT = handleObj.AddComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0, 0.5f);
        handleRT.anchorMax = new Vector2(0, 0.5f);
        handleRT.pivot = new Vector2(0.5f, 0.5f);
        handleRT.anchoredPosition = Vector2.zero;
        handleRT.sizeDelta = new Vector2(HANDLE_SIZE, HANDLE_SIZE);

        var handleImage = handleObj.AddComponent<Image>();
        handleImage.color = Color.white;
        handleImage.sprite = CreateCircleSprite();

        // Touch area
        GameObject touchObj = new GameObject("TouchArea");
        touchObj.transform.SetParent(sliderAreaObj.transform, false);

        var touchRT = touchObj.AddComponent<RectTransform>();
        // Expand horizontally by touchExpand on each side (consistent default for all VR sliders)
        touchRT.anchorMin = new Vector2(-touchExpand, 0f);
        touchRT.anchorMax = new Vector2(1.0f + touchExpand, 1f);
        touchRT.offsetMin = Vector2.zero;
        touchRT.offsetMax = Vector2.zero;

        var touchImage = touchObj.AddComponent<Image>();
        touchImage.color = Color.clear;

        var collider = touchObj.AddComponent<BoxCollider>();
        // Use dynamic width to match the visual extension
        collider.size = new Vector3(sliderWidth * (1f + touchExpand * 2f), height, 10);
        collider.center = new Vector3(0, 0, -5);

        // Configure
        var sliderControl = sliderObj.GetComponent<VRSliderControl>();
        sliderControl.Initialize(fillRT, handleRT, null, sliderWidth);
    }
    #endregion

    #region Helpers
    private static Sprite CreateCircleSprite()
    {
        // Create a simple circle texture for handle
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        float radius = size / 2f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                if (dist < radius - 1)
                {
                    tex.SetPixel(x, y, Color.white);
                }
                else if (dist < radius)
                {
                    // Anti-aliased edge
                    float alpha = 1f - (dist - (radius - 1));
                    tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
    #endregion
}

/// <summary>
/// VR-optimized slider control component.
/// Supports VR gaze click and hover preview.
/// </summary>
public class VRSliderControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    #region Events
    public event Action<float> OnValueChanged;
    public event Action OnDragStarted;
    public event Action OnDragEnded;
    /// <summary>
    /// Event fired when hover position changes. Parameter is normalized position (0-1).
    /// </summary>
    public event Action<float> OnHoverPositionChanged; // Normalized position (0-1)
    public Func<float, string> OnFormatPreview; // Custom formatter for preview text
    /// <summary>
    /// Fired when pointer enters the slider area.
    /// </summary>
    public event Action OnHoverEnter;
    /// <summary>
    /// Fired when pointer exits the slider area.
    /// </summary>
    public event Action OnHoverExit;
    #endregion

    #region Properties
    public float Value
    {
        get => _value;
        set => SetValue(value, true);
    }

    public float NormalizedValue => (_value - _minValue) / (_maxValue - _minValue);
    public bool IsDragging { get; private set; }
    public bool PreviewEnabled { get; set; } = true;
    public RectTransform HandleTransform => _handleRT;
    #endregion

    #region Private Fields
    private float _value = 0f;
    private float _minValue = 0f;
    private float _maxValue = 1f;
    private float _step = 0f; // 0 = continuous (no snapping)
    private float _width;

    private RectTransform _fillRT;
    private RectTransform _handleRT;
    private RectTransform _bufferRT;  // Optional

    // Hover preview
    private bool _isHovering = false;
    private float _hoverNormalizedPosition = 0f;
    private GameObject _previewContainer;
    private Image _previewSeekBar;
    private TextMeshProUGUI _previewValueText;
    private RectTransform _previewRT;
    private Color _previewColor = new Color(1f, 1f, 1f, 0.4f); // White semi-transparent
    private RawImage _previewImage; // Added for thumbnail
    private float _previewHeight = 0f; // Height of the preview frame
    #endregion

    #region Initialization
    public void Initialize(RectTransform fillRT, RectTransform handleRT, RectTransform bufferRT, float width, float previewHeight = 0f)
    {
        _fillRT = fillRT;
        _handleRT = handleRT;
        _bufferRT = bufferRT;
        _width = width;
        _previewHeight = previewHeight;
    }

    public void SetRange(float min, float max)
    {
        _minValue = min;
        _maxValue = max;
    }

    /// <summary>
    /// Set minimum step size. Value will snap to nearest step.
    /// Set to 0 for continuous (no snapping).
    /// </summary>
    public void SetStep(float step)
    {
        _step = Mathf.Max(0f, step);
    }

    /// <summary>
    /// Set preview thumbnail image.
    /// </summary>
    public void SetPreviewImage(Texture texture)
    {
        _cachedPreviewTexture = texture;

        if (_previewImage != null)
        {
            _previewImage.texture = texture;
            _previewImage.color = texture != null ? Color.white : Color.black;

            // Hide the frame container if no texture is available (e.g. volume slider)
            if (_previewImage.transform.parent != null)
            {
                _previewImage.transform.parent.gameObject.SetActive(texture != null);
            }
        }
    }
    #endregion

    #region Public Methods
    public void SetValue(float value, bool notify = true)
    {
        float newValue = Mathf.Clamp(value, _minValue, _maxValue);

        // Snap to step if configured
        if (_step > 0f)
        {
            newValue = Mathf.Round(newValue / _step) * _step;
            newValue = Mathf.Clamp(newValue, _minValue, _maxValue);
        }

        if (Mathf.Approximately(newValue, _value)) return;

        _value = newValue;
        UpdateVisuals();

        if (notify)
        {
            OnValueChanged?.Invoke(_value);
        }
    }

    public void SetValueWithoutNotify(float value)
    {
        SetValue(value, false);
    }

    /// <summary>
    /// Set buffer/loaded progress (0-1).
    /// </summary>
    public void SetBufferProgress(float progress)
    {
        if (_bufferRT != null)
        {
            var rt = GetComponent<RectTransform>();
            float actualWidth = (rt != null && rt.rect.width > 0) ? rt.rect.width : _width;
            _bufferRT.sizeDelta = new Vector2(actualWidth * Mathf.Clamp01(progress), _bufferRT.sizeDelta.y);
        }
    }
    #endregion

    #region Pointer Events
    public void OnPointerDown(PointerEventData eventData)
    {
        IsDragging = true;
        OnDragStarted?.Invoke();
        UpdateValueFromPointer(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsDragging)
        {
            UpdateValueFromPointer(eventData);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsDragging = false;
        OnDragEnded?.Invoke();
    }

    private void UpdateValueFromPointer(PointerEventData eventData)
    {
        RectTransform rt = GetComponent<RectTransform>();
        Vector2 localPoint;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out localPoint))
        {
            // Calculate normalized position
            float normalized = (localPoint.x + rt.rect.width / 2) / rt.rect.width;
            normalized = Mathf.Clamp01(normalized);

            // Convert to value
            float newValue = Mathf.Lerp(_minValue, _maxValue, normalized);
            SetValue(newValue);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"[VRSliderControl] OnPointerClick called on {gameObject.name}, hoverNormPos={_hoverNormalizedPosition:F3}");
        // VR gaze click - jump to clicked position using the hover position we calculated
        if (!IsDragging)
        {
            // Use the hover normalized position instead of calculating from eventData
            // because RTT panels use render texture coordinates, not screen coordinates
            if (_hoverNormalizedPosition >= 0)
            {
                float newValue = Mathf.Lerp(_minValue, _maxValue, _hoverNormalizedPosition);
                Debug.Log($"[VRSliderControl] Setting value to {newValue} (normalized: {_hoverNormalizedPosition:F3})");
                SetValue(newValue);
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Debug.Log($"[VRSliderControl] OnPointerEnter called on {gameObject.name}");
        _isHovering = true;
        ShowPreview();
        OnHoverEnter?.Invoke();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Debug.Log($"[VRSliderControl] OnPointerExit called on {gameObject.name}");
        _isHovering = false;
        HidePreview();
        OnHoverExit?.Invoke();
    }

    private void Update()
    {
        if (_isHovering && _previewContainer != null && _previewContainer.activeSelf)
        {
            UpdatePreviewFromGaze();
        }
    }

    private void UpdatePreviewFromGaze()
    {
        // Get current reticle position from RTTRaycastManager
        if (RTTRaycastManager.Instance == null) return;

        var hit = RTTRaycastManager.Instance.CurrentHit;
        if (!hit.isValid) return;

        // Check if this hit is actually on our slider (or a child of it)
        if (hit.hitUIElement == null) return;
        
        // Verify the hit element is part of this slider
        Transform hitTransform = hit.hitUIElement.transform;
        bool isOurSlider = hitTransform == transform || hitTransform.IsChildOf(transform);
        if (!isOurSlider) return;

        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) return;

        // Get the panel that was hit to access its render texture resolution
        if (hit.panel == null) return;
        
        // Convert screen position (in render texture space) to slider local position
        // screenPosition is already in canvas pixel coordinates
        Vector2 canvasPosition = hit.screenPosition;
        
        // Get the canvas to convert screen position to local position
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        
        Camera uiCamera = canvas.worldCamera;
        
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, canvasPosition, uiCamera, out localPoint))
        {
            // Calculate normalized position based on local point within slider rect
            float normalized = (localPoint.x + rt.rect.width * rt.pivot.x) / rt.rect.width;
            normalized = Mathf.Clamp01(normalized);

            _hoverNormalizedPosition = normalized;
            UpdatePreviewPosition(normalized);
            OnHoverPositionChanged?.Invoke(normalized);
        }
    }

    private void UpdatePreviewPosition(float normalized)
    {
        if (_previewSeekBar == null || _previewValueText == null) return;

        RectTransform rt = GetComponent<RectTransform>();
        float width = (rt != null && rt.rect.width > 0) ? rt.rect.width : _width;

        // Update seek bar width (from fill end to hover position)
        float fillNormalized = NormalizedValue;
        float previewWidth = Mathf.Max(0, (normalized - fillNormalized) * width);
        float startX = fillNormalized * width;

        var seekBarRT = _previewSeekBar.GetComponent<RectTransform>();
        seekBarRT.anchoredPosition = new Vector2(startX, 0);
        seekBarRT.sizeDelta = new Vector2(previewWidth, seekBarRT.sizeDelta.y);

        // Update tooltip position and text
        var tooltipRT = _previewValueText.GetComponent<RectTransform>();
        tooltipRT.anchoredPosition = new Vector2(normalized * width, tooltipRT.anchoredPosition.y);

        // Update preview frame position (if exists)
        if (_previewImage != null && _previewImage.transform.parent != null)
        {
            var frameRT = _previewImage.transform.parent.GetComponent<RectTransform>();
            if (frameRT != null)
            {
                frameRT.anchoredPosition = new Vector2(normalized * width, frameRT.anchoredPosition.y);
            }
        }

        // Calculate and display value
        float hoverValue = Mathf.Lerp(_minValue, _maxValue, normalized);
        _previewValueText.text = FormatPreviewValue(hoverValue);

        // Adjust Y position based on slider type
        // Timeline (identified by buffer or preview height): Position below (-25f) to avoid handle overlap
        // Volume/Settings: Position above (30f)
        bool isTimeline = _bufferRT != null || _previewHeight > 0;
        float textY = isTimeline ? -5f : 30f;
        
        tooltipRT.anchoredPosition = new Vector2(normalized * width, textY);
    }

    /// <summary>
    /// Format the preview value. Override-friendly for custom formatting.
    /// </summary>
    protected virtual string FormatPreviewValue(float value)
    {
        // Use custom formatter if provided
        if (OnFormatPreview != null)
        {
            return OnFormatPreview(value);
        }

        // Default heuristic: Check if this looks like a time value (0-N seconds/minutes)
        if (_maxValue > 60f)
        {
            // Format as time (mm:ss)
            int totalSeconds = Mathf.RoundToInt(value);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes}:{seconds:D2}";
        }
        else if (_maxValue <= 1f)
        {
            // Percentage (for volume)
            return $"{Mathf.RoundToInt(value * 100)}%";
        }
        else
        {
            return value.ToString("F1");
        }
    }
    #endregion

    #region Preview Methods
    private void ShowPreview()
    {
        if (!PreviewEnabled) return;
        if (_previewContainer == null)
        {
            CreatePreviewUI();
        }
        _previewContainer?.SetActive(true);
    }

    private void HidePreview()
    {
        _previewContainer?.SetActive(false);
    }

    /// <summary>
    /// Set preview frame aspect ratio. 
    /// If _previewHeight is set, width is calculated (height * ratio).
    /// Otherwise width is fixed at 160, height is width / ratio.
    /// </summary>
    public void SetPreviewAspectRatio(float ratio)
    {
        _cachedAspectRatio = ratio;

        if (_previewImage != null && _previewImage.transform.parent != null)
        {
            var frameRT = _previewImage.transform.parent.GetComponent<RectTransform>();
            if (frameRT != null && ratio > 0)
            {
                float width, height;
                if (_previewHeight > 0)
                {
                    // Fixed height (match panel), variable width
                    height = _previewHeight;
                    width = height * ratio;
                }
                else
                {
                    // Default: Fixed width, variable height
                    width = 160f;
                    height = width / ratio;
                }
                frameRT.sizeDelta = new Vector2(width, height);
            }
        }
    }

    private void CreatePreviewUI()
    {
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) return;

        // Preview container
        _previewContainer = new GameObject("PreviewContainer");
        _previewContainer.transform.SetParent(transform, false);

        _previewRT = _previewContainer.AddComponent<RectTransform>();
        _previewRT.anchorMin = Vector2.zero;
        _previewRT.anchorMax = Vector2.one;
        _previewRT.offsetMin = Vector2.zero;
        _previewRT.offsetMax = Vector2.zero;

        // Preview seek bar (semi-transparent fill from left to hover position)
        GameObject seekBarObj = new GameObject("PreviewSeekBar");
        seekBarObj.transform.SetParent(_previewContainer.transform, false);

        var seekBarRT = seekBarObj.AddComponent<RectTransform>();
        seekBarRT.anchorMin = new Vector2(0, 0.5f);
        seekBarRT.anchorMax = new Vector2(0, 0.5f);
        seekBarRT.pivot = new Vector2(0, 0.5f);
        seekBarRT.anchoredPosition = Vector2.zero;
        seekBarRT.sizeDelta = new Vector2(0, 8f); // Slightly smaller than main track

        _previewSeekBar = seekBarObj.AddComponent<Image>();
        _previewSeekBar.color = _previewColor;
        _previewSeekBar.raycastTarget = false;

        // Preview frame (video thumbnail) above slider - DISABLED
        /*
        GameObject frameObj = new GameObject("PreviewFrame");
        frameObj.transform.SetParent(_previewContainer.transform, false);

        var frameRT = frameObj.AddComponent<RectTransform>();
        frameRT.anchorMin = new Vector2(0, 0); // Left-aligned hook
        frameRT.anchorMax = new Vector2(0, 0);
        frameRT.pivot = new Vector2(0.5f, 0f); // Pivot bottom-center so it grows upwards
        frameRT.anchoredPosition = new Vector2(0, 15f); // Gap above slider (increased slightly)
        
        // Use provided height or default to 16:9
        float pHeight = _previewHeight > 0 ? _previewHeight : 90f;
        float pWidth = _previewHeight > 0 ? pHeight * (16f/9f) : 160f; // Default ratio 16:9
        frameRT.sizeDelta = new Vector2(pWidth, pHeight); 

        // Dark background for frame with Rounded Corners
        var frameImage = frameObj.AddComponent<Image>();
        frameImage.color = new Color(0, 0, 0, 0.9f); // Slightly darker
        frameImage.sprite = RTTMediaControlsPanel.CreateRoundedRectSprite(10f); // 10% corner radius
        frameImage.type = Image.Type.Sliced;
        frameImage.raycastTarget = false;

        // Mask to clip thumbnail to rounded corners
        var mask = frameObj.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        // Actual thumbnail image (RawImage for texture)
        GameObject thumbObj = new GameObject("ThumbnailImage");
        thumbObj.transform.SetParent(frameObj.transform, false);
        
        var thumbRT = thumbObj.AddComponent<RectTransform>();
        thumbRT.anchorMin = Vector2.zero;
        thumbRT.anchorMax = Vector2.one;
        thumbRT.offsetMin = new Vector2(4, 4); // 4px padding for border effect
        thumbRT.offsetMax = new Vector2(-4, -4);
        
        _previewImage = thumbObj.AddComponent<RawImage>();
        _previewImage.color = Color.black; // Default black to avoid white flash
        _previewImage.raycastTarget = false;
        */

        // Preview value tooltip (below the slider)
        GameObject tooltipObj = new GameObject("PreviewValueText");
        tooltipObj.transform.SetParent(_previewContainer.transform, false);

        var tooltipRT = tooltipObj.AddComponent<RectTransform>();
        tooltipRT.anchorMin = new Vector2(0, 0); // Left-aligned hook
        tooltipRT.anchorMax = new Vector2(0, 0);
        tooltipRT.pivot = new Vector2(0.5f, 1f); // Pivot top-center so it grows downwards
        tooltipRT.anchoredPosition = new Vector2(0, -5f); // Reduced gap
        tooltipRT.sizeDelta = new Vector2(160, 50); 

        _previewValueText = tooltipObj.AddComponent<TextMeshProUGUI>();
        _previewValueText.fontSize = 36; 
        _previewValueText.fontStyle = FontStyles.Bold; 
        _previewValueText.color = Color.white;
        _previewValueText.alignment = TextAlignmentOptions.Center;
        _previewValueText.raycastTarget = false;

        _previewContainer.SetActive(false);

        // Apply cached values if available
        if (_cachedAspectRatio > 0)
        {
            SetPreviewAspectRatio(_cachedAspectRatio);
        }

        // Apply cached preview texture (this updates frame visibility)
        SetPreviewImage(_cachedPreviewTexture);
    }
    #endregion

    #region Private Methods
    // Cached values for lazy initialization
    private Texture _cachedPreviewTexture;
    private float _cachedAspectRatio = 0f;

    private void UpdateVisuals()
    {
        float normalized = NormalizedValue;

        // Use actual RectTransform width (supports layout-driven sizing)
        var rt = GetComponent<RectTransform>();
        float actualWidth = (rt != null && rt.rect.width > 0) ? rt.rect.width : _width;

        // Update fill
        if (_fillRT != null)
        {
            _fillRT.sizeDelta = new Vector2(actualWidth * normalized, _fillRT.sizeDelta.y);
        }

        // Update handle position
        if (_handleRT != null)
        {
            _handleRT.anchoredPosition = new Vector2(actualWidth * normalized, _handleRT.anchoredPosition.y);
        }
    }
    #endregion
}
