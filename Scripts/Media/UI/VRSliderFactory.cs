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
        string label = "")
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
                BuildProgressSlider(sliderObj, width, height, font, primaryColor);
                break;
            case SliderStyle.Volume:
                BuildVolumeSlider(sliderObj, width, height, font, primaryColor);
                break;
            case SliderStyle.Setting:
                BuildSettingSlider(sliderObj, width, height, font, primaryColor, showLabel, label);
                break;
            default:
                BuildDefaultSlider(sliderObj, width, height, font, primaryColor);
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
        Color primaryColor)
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
        touchRT.anchorMin = Vector2.zero;
        touchRT.anchorMax = Vector2.one;
        touchRT.offsetMin = new Vector2(0, -(TOUCH_AREA_HEIGHT - DEFAULT_HEIGHT) / 2);
        touchRT.offsetMax = new Vector2(0, (TOUCH_AREA_HEIGHT - DEFAULT_HEIGHT) / 2);

        // Raycast target for touch area
        var touchImage = touchObj.AddComponent<Image>();
        touchImage.color = Color.clear;

        // BoxCollider for VR interaction
        var collider = touchObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, TOUCH_AREA_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);

        // Configure slider control
        sliderControl.Initialize(fillRT, handleRT, bufferRT, width);

        return sliderControl;
    }

    /// <summary>
    /// Create a volume slider.
    /// </summary>
    public static VRSliderControl CreateVolumeSlider(
        Transform parent,
        float width,
        TMP_FontAsset font,
        Color primaryColor)
    {
        return CreateSlider(parent, width, DEFAULT_HEIGHT, font, primaryColor, SliderStyle.Volume, 0f, 1f);
    }
    #endregion

    #region Build Methods
    private static void BuildDefaultSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor)
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
        touchRT.anchorMin = Vector2.zero;
        touchRT.anchorMax = Vector2.one;
        touchRT.offsetMin = Vector2.zero;
        touchRT.offsetMax = Vector2.zero;

        var touchImage = touchObj.AddComponent<Image>();
        touchImage.color = Color.clear;

        var collider = touchObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, height, 10);
        collider.center = new Vector3(0, 0, -5);

        // Configure
        var sliderControl = sliderObj.GetComponent<VRSliderControl>();
        sliderControl.Initialize(fillRT, handleRT, null, width);
    }

    private static void BuildProgressSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor)
    {
        // Same as timeline slider but without buffer
        BuildDefaultSlider(sliderObj, width, height, font, primaryColor);
    }

    private static void BuildVolumeSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor)
    {
        // Similar to default but with volume-specific styling
        BuildDefaultSlider(sliderObj, width, height, font, primaryColor);
    }

    private static void BuildSettingSlider(GameObject sliderObj, float width, float height,
        TMP_FontAsset font, Color primaryColor, bool showLabel, string label)
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
        touchRT.anchorMin = Vector2.zero;
        touchRT.anchorMax = Vector2.one;
        touchRT.offsetMin = Vector2.zero;
        touchRT.offsetMax = Vector2.zero;

        var touchImage = touchObj.AddComponent<Image>();
        touchImage.color = Color.clear;

        var collider = touchObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(sliderWidth, height, 10);
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
/// </summary>
public class VRSliderControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    #region Events
    public event Action<float> OnValueChanged;
    public event Action OnDragStarted;
    public event Action OnDragEnded;
    #endregion

    #region Properties
    public float Value
    {
        get => _value;
        set => SetValue(value, true);
    }

    public float NormalizedValue => (_value - _minValue) / (_maxValue - _minValue);
    public bool IsDragging { get; private set; }
    #endregion

    #region Private Fields
    private float _value = 0f;
    private float _minValue = 0f;
    private float _maxValue = 1f;
    private float _width;

    private RectTransform _fillRT;
    private RectTransform _handleRT;
    private RectTransform _bufferRT;  // Optional
    #endregion

    #region Initialization
    public void Initialize(RectTransform fillRT, RectTransform handleRT, RectTransform bufferRT, float width)
    {
        _fillRT = fillRT;
        _handleRT = handleRT;
        _bufferRT = bufferRT;
        _width = width;
    }

    public void SetRange(float min, float max)
    {
        _minValue = min;
        _maxValue = max;
    }
    #endregion

    #region Public Methods
    public void SetValue(float value, bool notify = true)
    {
        float newValue = Mathf.Clamp(value, _minValue, _maxValue);
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
    #endregion

    #region Private Methods
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
