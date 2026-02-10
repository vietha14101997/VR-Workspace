using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Popup for selecting video projection and stereo mode.
/// Style: Segmented Control (Dark Gray container with Pink selected pill).
/// Layout: Header (25%), Body (75%) with 2 Rows spaced evenly.
/// </summary>
public class RTTMediaProjectionPopup : MonoBehaviour
{
    #region Constants
    private const float POPUP_WIDTH = 360f;
    private const float POPUP_HEIGHT = 240f; 
    
    // Height Ratios
    private const float HEADER_RATIO = 0.25f;
    private const float BODY_RATIO = 0.75f;
    private const float ROW_RATIO_OF_BODY = 0.25f;

    // Calculated Dimensions
    private float HeaderHeight => POPUP_HEIGHT * HEADER_RATIO; // 60
    private float BodyHeight => POPUP_HEIGHT * BODY_RATIO; // 180
    private float RowHeight => BodyHeight * ROW_RATIO_OF_BODY; // 45
    
    // Spacing Calculation:
    // Body Height = 180. 
    // Content = 2 Rows * 45 = 90.
    // Remaining = 90.
    // 3 Spaces (Top, Middle, Bottom) -> 30 each.
    private const float BODY_PADDING_TOP = 30f;
    private const float BODY_SPACING = 30f;
    
    private const float PADDING_X = 20f;
    
    // Colors
    private readonly Color POPUP_BG_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f); // Dark Grey Panel
    private readonly Color ROW_BG_COLOR = new Color(0.1f, 0.1f, 0.1f, 1.0f);     // Darker Row Background
    private readonly Color SELECTED_COLOR = new Color(1f, 0.0f, 0.4f, 1f);       // Hot Pink
    private readonly Color TEXT_COLOR_NORMAL = new Color(0.9f, 0.9f, 0.9f, 1f);
    private readonly Color TEXT_COLOR_SELECTED = Color.white;
    #endregion

    #region Events
    public event Action<VideoProjectionType, StereoMode> OnSettingsChanged;
    public event Action OnCloseRequested;
    #endregion

    #region Private Fields
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private GameObject _popup;
    private CanvasGroup _canvasGroup;

    // Current state
    private VideoProjectionType _currentProjection = VideoProjectionType.Flat;
    private StereoMode _currentStereo = StereoMode.Mono;

    // UI References
    private Dictionary<VideoProjectionType, Button> _projectionButtons = new Dictionary<VideoProjectionType, Button>();
    private Dictionary<StereoMode, Button> _stereoButtons = new Dictionary<StereoMode, Button>();
    #endregion

    #region Initialization
    public void Initialize(TMP_FontAsset font, Color primary, Color accent)
    {
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;

        BuildUI();
        Hide();
    }

    private void BuildUI()
    {
        // Cleanup existing
        foreach (Transform child in transform) Destroy(child.gameObject);

        // Main popup container
        _popup = new GameObject("ProjectionPopup");
        _popup.transform.SetParent(transform, false);

        var popupRT = _popup.AddComponent<RectTransform>();
        popupRT.sizeDelta = new Vector2(POPUP_WIDTH, POPUP_HEIGHT);

        // Background (Rounded Panel)
        var bg = _popup.AddComponent<Image>();
        bg.color = POPUP_BG_COLOR;
        
        // Canvas group
        _canvasGroup = _popup.AddComponent<CanvasGroup>();

        // Main Vertical Layout (Header + Body)
        var mainLayout = _popup.AddComponent<VerticalLayoutGroup>();
        mainLayout.padding = new RectOffset(0, 0, 0, 0); // RectOffset.zero not available
        mainLayout.spacing = 0;
        mainLayout.childControlHeight = true; // MUST be true to drive LayoutElement heights
        mainLayout.childForceExpandHeight = false;
        mainLayout.childAlignment = TextAnchor.UpperCenter;

        // 1. Header Section
        CreateHeaderSection();

        // 2. Body Section
        CreateBodySection();
    }
    
    // ... [Inside GetPillSprite] ...
    private static Sprite _pillSprite;
    private static Sprite GetPillSprite()
    {
        if (_pillSprite != null) return _pillSprite;

        int size = 32; 
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        float center = size / 2f;
        float radius = size / 2f; 

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();

        Vector4 border = new Vector4(size / 2, size / 2, size / 2, size / 2);
        _pillSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        
        return _pillSprite;
    }

    private void CreateHeaderSection()
    {
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(_popup.transform, false);

        var le = headerObj.AddComponent<LayoutElement>();
        le.minHeight = HeaderHeight;
        le.preferredHeight = HeaderHeight;
        le.flexibleHeight = 0;

        // Text
        var textObj = new GameObject("Title");
        textObj.transform.SetParent(headerObj.transform, false);
        
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        
        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "Projection";
        text.font = _font;
        text.fontSize = 20; // Slightly larger for header
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    private void CreateBodySection()
    {
        GameObject bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(_popup.transform, false);

        var le = bodyObj.AddComponent<LayoutElement>();
        le.minHeight = BodyHeight;
        le.preferredHeight = BodyHeight;
        le.flexibleHeight = 0;

        var vlg = bodyObj.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)PADDING_X, (int)PADDING_X, (int)BODY_PADDING_TOP, (int)BODY_PADDING_TOP); // Top/Bottom padding
        vlg.spacing = BODY_SPACING;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        // Rows
        CreateProjectionRow(bodyObj.transform);
        CreateStereoRow(bodyObj.transform);
    }

    private void CreateProjectionRow(Transform parent)
    {
        // Container for the segmented control
        GameObject rowObj = new GameObject("ProjectionRow");
        rowObj.transform.SetParent(parent, false);

        // Background for the row (Dark Pill)
        var bg = rowObj.AddComponent<Image>();
        bg.color = ROW_BG_COLOR;
        
        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        // Increased padding to 8 to "shrink" the inner buttons slightly as requested
        rowLayout.padding = new RectOffset(8, 8, 8, 8); 
        rowLayout.spacing = 5;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandHeight = true;

        var le = rowObj.AddComponent<LayoutElement>();
        le.minHeight = RowHeight;
        le.preferredHeight = RowHeight;

        // Buttons
        CreateSegmentedButton(rowObj.transform, "FLAT", () => SetProjection(VideoProjectionType.Flat), _projectionButtons, VideoProjectionType.Flat);
        CreateSegmentedButton(rowObj.transform, "180", () => SetProjection(VideoProjectionType.Dome180), _projectionButtons, VideoProjectionType.Dome180);
        CreateSegmentedButton(rowObj.transform, "360", () => SetProjection(VideoProjectionType.Sphere360), _projectionButtons, VideoProjectionType.Sphere360);
        CreateSegmentedButton(rowObj.transform, "FISH", null, null, (VideoProjectionType)999); // Dummy visual
    }

    private void CreateStereoRow(Transform parent)
    {
        GameObject rowObj = new GameObject("StereoRow");
        rowObj.transform.SetParent(parent, false);

        var bg = rowObj.AddComponent<Image>();
        bg.color = ROW_BG_COLOR;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(8, 8, 8, 8);
        rowLayout.spacing = 5;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandHeight = true;

        var le = rowObj.AddComponent<LayoutElement>();
        le.minHeight = RowHeight;
        le.preferredHeight = RowHeight;

        // Buttons (Lowercase as per image)
        CreateSegmentedButton(rowObj.transform, "mono", () => SetStereo(StereoMode.Mono), _stereoButtons, StereoMode.Mono);
        CreateSegmentedButton(rowObj.transform, "sbs", () => SetStereo(StereoMode.SideBySide), _stereoButtons, StereoMode.SideBySide);
        CreateSegmentedButton(rowObj.transform, "ou", () => SetStereo(StereoMode.OverUnder), _stereoButtons, StereoMode.OverUnder);
    }

    private Button CreateSegmentedButton<T>(Transform parent, string label, Action onClick, Dictionary<T, Button> dict, T key)
    {
        GameObject btnObj = new GameObject(label);
        btnObj.transform.SetParent(parent, false);

        // Background (Transparent by default, Pink when selected)
        var bg = btnObj.AddComponent<Image>();
        bg.sprite = GetPillSprite();
        bg.type = Image.Type.Sliced;
        bg.color = Color.clear; // Start clear

        var btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        if (onClick != null)
        {
            btn.onClick.AddListener(() => onClick.Invoke());
        }

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = _font;
        text.fontSize = 16;
        text.color = TEXT_COLOR_NORMAL;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;

        // BoxCollider for VR raycast
        var collider = btnObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(60, 30, 1); // Approximate, will be controlled by layout but collider needs size

        if (dict != null)
        {
            dict[key] = btn;
        }

        return btn;
    }
    #endregion

    #region Public Methods
    public void Show()
    {
        gameObject.SetActive(true);
        _popup.SetActive(true);
        UpdateUI();
    }

    public void Hide()
    {
        _popup.SetActive(false);
        gameObject.SetActive(false);
    }

    public void SetState(VideoProjectionType projection, StereoMode stereo)
    {
        _currentProjection = projection;
        _currentStereo = stereo;
        UpdateUI();
    }
    #endregion

    #region Internal Logic
    private void SetProjection(VideoProjectionType type)
    {
        _currentProjection = type;
        UpdateUI();
        NotifyChanged();
    }

    private void SetStereo(StereoMode mode)
    {
        _currentStereo = mode;
        UpdateUI();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnSettingsChanged?.Invoke(_currentProjection, _currentStereo);
    }

    private void UpdateUI()
    {
        // Update Projection Buttons
        foreach (var kvp in _projectionButtons)
        {
            var btn = kvp.Value;
            bool isActive = kvp.Key == _currentProjection;
            
            // Visual Update
            // Active: Pink Background, White Text
            // Inactive: Clear Background, Light Grey Text
            btn.targetGraphic.color = isActive ? SELECTED_COLOR : Color.clear;
            
            var text = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (text)
            {
                text.color = isActive ? TEXT_COLOR_SELECTED : TEXT_COLOR_NORMAL;
            }
        }

        // Update Stereo Buttons
        foreach (var kvp in _stereoButtons)
        {
            var btn = kvp.Value;
            bool isActive = kvp.Key == _currentStereo;

            btn.targetGraphic.color = isActive ? SELECTED_COLOR : Color.clear;

            var text = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (text)
            {
                text.color = isActive ? TEXT_COLOR_SELECTED : TEXT_COLOR_NORMAL;
            }
        }
    }
    #endregion

    public enum StereoMode
    {
        Mono,
        SideBySide,
        OverUnder
    }
}
