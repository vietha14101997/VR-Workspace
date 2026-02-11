using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.Media.Subtitles;

/// <summary>
/// Settings popup for video playback.
/// Allows adjusting projection, screen settings, subtitles, and display options.
/// </summary>
public class RTTMediaSettingsPopup : MonoBehaviour
{
    #region Constants
    private const float POPUP_WIDTH = 450f;
    private const float POPUP_HEIGHT = 600f;
    private const float HEADER_HEIGHT = 60f;
    private const float ROW_HEIGHT = 50f;
    private const float SLIDER_HEIGHT = 40f;
    private const float SECTION_SPACING = 20f;
    private const float ITEM_SPACING = 10f;
    private const float PADDING = 25f;
    #endregion

    #region Events
    public event Action<VideoProjectionType> OnProjectionChanged;
    public event Action<float> OnScreenDistanceChanged;
    public event Action<float> OnScreenScaleChanged;
    public event Action<float> OnScreenCurvatureChanged;
    public event Action<string> OnSubtitleSelected;  // null = off
    public event Action<bool> OnLightsToggled;
    public event Action OnCloseRequested;
    #endregion

    #region Private Fields
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private GameObject _popup;
    private CanvasGroup _canvasGroup;

    // Current values
    private VideoProjectionType _currentProjection = VideoProjectionType.Flat;
    private float _screenDistance = 2.0f;
    private float _screenScale = 1.0f;
    private float _screenCurvature = 0f;
    private bool _lightsOn = true;
    private string _currentSubtitle = null;
    private List<SubtitleFileInfo> _availableSubtitles = new List<SubtitleFileInfo>();

    // UI References
    private TMP_Dropdown _projectionDropdown;
    private VRSliderControl _distanceSlider;
    private VRSliderControl _scaleSlider;
    private VRSliderControl _curvatureSlider;
    private TextMeshProUGUI _distanceValue;
    private TextMeshProUGUI _scaleValue;
    private TextMeshProUGUI _curvatureValue;
    private TMP_Dropdown _subtitleDropdown;
    private Toggle _lightsToggle;
    private GameObject _screenSettingsSection;
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
        // Main popup container
        _popup = new GameObject("SettingsPopup");
        _popup.transform.SetParent(transform, false);

        var popupRT = _popup.AddComponent<RectTransform>();
        popupRT.sizeDelta = new Vector2(POPUP_WIDTH, POPUP_HEIGHT);

        // Background
        var bg = _popup.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);

        // Canvas group for fade
        _canvasGroup = _popup.AddComponent<CanvasGroup>();

        // Header
        CreateHeader();

        // Content scroll area
        CreateContent();
    }

    private void CreateHeader()
    {
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(_popup.transform, false);

        var headerRT = headerObj.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.anchoredPosition = Vector2.zero;
        headerRT.sizeDelta = new Vector2(0, HEADER_HEIGHT);

        // Header background
        var headerBg = headerObj.AddComponent<Image>();
        headerBg.color = new Color(0, 0, 0, 0.3f);

        // Title
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(headerObj.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 0);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.offsetMin = new Vector2(PADDING, 0);
        titleRT.offsetMax = new Vector2(-60, 0);

        var titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "VIDEO SETTINGS";
        titleText.font = _font;
        titleText.fontSize = 28;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;

        // Close button
        CreateCloseButton(headerObj.transform);
    }

    private void CreateCloseButton(Transform parent)
    {
        GameObject buttonObj = new GameObject("CloseButton");
        buttonObj.transform.SetParent(parent, false);

        var buttonRT = buttonObj.AddComponent<RectTransform>();
        buttonRT.anchorMin = new Vector2(1, 0.5f);
        buttonRT.anchorMax = new Vector2(1, 0.5f);
        buttonRT.pivot = new Vector2(1, 0.5f);
        buttonRT.anchoredPosition = new Vector2(-15, 0);
        buttonRT.sizeDelta = new Vector2(40, 40);

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = new Color(1, 1, 1, 0.1f);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.onClick.AddListener(() => OnCloseRequested?.Invoke());

        // X text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "X";
        text.font = _font;
        text.fontSize = 24;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        // VR Collider
        var collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(40, 40, 10);
        collider.center = new Vector3(0, 0, -5);
    }

    private void CreateContent()
    {
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(_popup.transform, false);

        var contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.offsetMin = new Vector2(PADDING, PADDING);
        contentRT.offsetMax = new Vector2(-PADDING, -HEADER_HEIGHT - 10);

        var layout = contentObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = SECTION_SPACING;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Projection Section
        CreateProjectionSection(contentObj.transform);

        // Screen Settings Section (only for Flat/Mono)
        _screenSettingsSection = CreateScreenSettingsSection(contentObj.transform);

        // Subtitles Section
        CreateSubtitleSection(contentObj.transform);

        // Environment Section
        CreateEnvironmentSection(contentObj.transform);

        // Apply/Reset buttons
        CreateActionButtons(contentObj.transform);
    }

    private void CreateProjectionSection(Transform parent)
    {
        GameObject sectionObj = CreateSection(parent, "PROJECTION");

        // Dropdown for projection type
        var dropdown = CreateDropdown(sectionObj.transform, "Type", new List<string>
        {
            "2D Flat",
            "180° VR",
            "360° VR"
        });

        _projectionDropdown = dropdown;
        dropdown.onValueChanged.AddListener(OnProjectionDropdownChanged);
    }

    private GameObject CreateScreenSettingsSection(Transform parent)
    {
        GameObject sectionObj = CreateSection(parent, "SCREEN");

        var sectionLayout = sectionObj.GetComponent<VerticalLayoutGroup>();
        if (sectionLayout == null)
        {
            sectionLayout = sectionObj.AddComponent<VerticalLayoutGroup>();
            sectionLayout.spacing = ITEM_SPACING;
            sectionLayout.childControlWidth = true;
            sectionLayout.childControlHeight = false;
            sectionLayout.childForceExpandWidth = true;
            sectionLayout.childForceExpandHeight = false;
        }

        // Distance slider
        _distanceSlider = CreateSliderRow(sectionObj.transform, "Distance", 1.0f, 5.0f, _screenDistance, out _distanceValue);
        _distanceSlider.OnValueChanged += (v) =>
        {
            _screenDistance = v;
            _distanceValue.text = $"{v:F1}m";
            OnScreenDistanceChanged?.Invoke(v);
        };

        // Scale slider
        _scaleSlider = CreateSliderRow(sectionObj.transform, "Scale", 0.5f, 2.0f, _screenScale, out _scaleValue);
        _scaleSlider.OnValueChanged += (v) =>
        {
            _screenScale = v;
            _scaleValue.text = $"{v:F1}x";
            OnScreenScaleChanged?.Invoke(v);
        };

        // Curvature slider
        _curvatureSlider = CreateSliderRow(sectionObj.transform, "Curvature", 0f, 1.0f, _screenCurvature, out _curvatureValue);
        _curvatureSlider.OnValueChanged += (v) =>
        {
            _screenCurvature = v;
            _curvatureValue.text = $"{(int)(v * 100)}%";
            OnScreenCurvatureChanged?.Invoke(v);
        };

        return sectionObj;
    }

    private void CreateSubtitleSection(Transform parent)
    {
        GameObject sectionObj = CreateSection(parent, "SUBTITLES");

        // Subtitle dropdown
        var dropdown = CreateDropdown(sectionObj.transform, "Track", new List<string> { "Off" });
        _subtitleDropdown = dropdown;
        dropdown.onValueChanged.AddListener(OnSubtitleDropdownChanged);
    }

    private void CreateEnvironmentSection(Transform parent)
    {
        GameObject sectionObj = CreateSection(parent, "ENVIRONMENT");

        // Lights toggle
        GameObject rowObj = new GameObject("LightsRow");
        rowObj.transform.SetParent(sectionObj.transform, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = ROW_HEIGHT;
        rowLE.preferredHeight = ROW_HEIGHT;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = "Room Lights";
        labelText.font = _font;
        labelText.fontSize = 24;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 1;

        // Toggle
        GameObject toggleObj = new GameObject("Toggle");
        toggleObj.transform.SetParent(rowObj.transform, false);

        var toggleLE = toggleObj.AddComponent<LayoutElement>();
        toggleLE.minWidth = 80;
        toggleLE.preferredWidth = 80;

        var toggleBg = toggleObj.AddComponent<Image>();
        toggleBg.color = new Color(1, 1, 1, 0.15f);

        _lightsToggle = toggleObj.AddComponent<Toggle>();
        _lightsToggle.isOn = _lightsOn;
        _lightsToggle.onValueChanged.AddListener((isOn) =>
        {
            _lightsOn = isOn;
            OnLightsToggled?.Invoke(isOn);
        });

        // Toggle text
        GameObject toggleTextObj = new GameObject("Text");
        toggleTextObj.transform.SetParent(toggleObj.transform, false);

        var toggleTextRT = toggleTextObj.AddComponent<RectTransform>();
        toggleTextRT.anchorMin = Vector2.zero;
        toggleTextRT.anchorMax = Vector2.one;
        toggleTextRT.offsetMin = Vector2.zero;
        toggleTextRT.offsetMax = Vector2.zero;

        var toggleText = toggleTextObj.AddComponent<TextMeshProUGUI>();
        toggleText.text = _lightsOn ? "ON" : "OFF";
        toggleText.font = _font;
        toggleText.fontSize = 22;
        toggleText.fontStyle = FontStyles.Bold;
        toggleText.color = _primaryColor;
        toggleText.alignment = TextAlignmentOptions.Center;

        _lightsToggle.onValueChanged.AddListener((isOn) =>
        {
            toggleText.text = isOn ? "ON" : "OFF";
        });

        // VR Collider
        var collider = toggleObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(80, ROW_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);
    }

    private void CreateActionButtons(Transform parent)
    {
        GameObject rowObj = new GameObject("ActionButtons");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = 60;
        rowLE.preferredHeight = 60;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 15;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        // Reset button
        CreateActionButton(rowObj.transform, "Reset", ResetToDefaults, false);

        // Apply button
        CreateActionButton(rowObj.transform, "Apply", ApplySettings, true);
    }

    private Button CreateActionButton(Transform parent, string label, Action onClick, bool isPrimary)
    {
        GameObject buttonObj = new GameObject($"Btn_{label}");
        buttonObj.transform.SetParent(parent, false);

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = isPrimary ? _primaryColor : new Color(1, 1, 1, 0.15f);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.onClick.AddListener(() => onClick?.Invoke());

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = _font;
        text.fontSize = 24;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        // VR Collider
        var collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(150, 50, 10);
        collider.center = new Vector3(0, 0, -5);

        return button;
    }
    #endregion

    #region Helper Methods
    private GameObject CreateSection(Transform parent, string title)
    {
        GameObject sectionObj = new GameObject($"Section_{title}");
        sectionObj.transform.SetParent(parent, false);

        var layout = sectionObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = ITEM_SPACING;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Section header
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(sectionObj.transform, false);

        var headerLE = headerObj.AddComponent<LayoutElement>();
        headerLE.minHeight = 30;
        headerLE.preferredHeight = 30;

        var headerText = headerObj.AddComponent<TextMeshProUGUI>();
        headerText.text = title;
        headerText.font = _font;
        headerText.fontSize = 20;
        headerText.fontStyle = FontStyles.Bold;
        headerText.color = new Color(1, 1, 1, 0.6f);
        headerText.alignment = TextAlignmentOptions.MidlineLeft;

        return sectionObj;
    }

    private TMP_Dropdown CreateDropdown(Transform parent, string label, List<string> options)
    {
        GameObject rowObj = new GameObject($"Dropdown_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = ROW_HEIGHT;
        rowLE.preferredHeight = ROW_HEIGHT;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.font = _font;
        labelText.fontSize = 24;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 0.4f;

        // Dropdown container
        GameObject dropdownObj = new GameObject("Dropdown");
        dropdownObj.transform.SetParent(rowObj.transform, false);

        var dropdownLE = dropdownObj.AddComponent<LayoutElement>();
        dropdownLE.flexibleWidth = 0.6f;

        var dropdownBg = dropdownObj.AddComponent<Image>();
        dropdownBg.color = new Color(1, 1, 1, 0.1f);

        var dropdown = dropdownObj.AddComponent<TMP_Dropdown>();
        dropdown.ClearOptions();
        dropdown.AddOptions(options);

        // Caption text
        GameObject captionObj = new GameObject("Caption");
        captionObj.transform.SetParent(dropdownObj.transform, false);

        var captionRT = captionObj.AddComponent<RectTransform>();
        captionRT.anchorMin = Vector2.zero;
        captionRT.anchorMax = Vector2.one;
        captionRT.offsetMin = new Vector2(10, 0);
        captionRT.offsetMax = new Vector2(-30, 0);

        var captionText = captionObj.AddComponent<TextMeshProUGUI>();
        captionText.font = _font;
        captionText.fontSize = 22;
        captionText.color = _primaryColor;
        captionText.alignment = TextAlignmentOptions.MidlineLeft;

        dropdown.captionText = captionText;

        // VR Collider
        var collider = dropdownObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(200, ROW_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);

        return dropdown;
    }

    private VRSliderControl CreateSliderRow(Transform parent, string label, float min, float max, float initial, out TextMeshProUGUI valueText)
    {
        GameObject rowObj = new GameObject($"Slider_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = SLIDER_HEIGHT;
        rowLE.preferredHeight = SLIDER_HEIGHT;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.minWidth = 100;
        labelLE.preferredWidth = 100;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.font = _font;
        labelText.fontSize = 22;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        // Slider
        var slider = VRSliderFactory.CreateSlider(
            rowObj.transform, 180, SLIDER_HEIGHT, _font, _primaryColor,
            VRSliderFactory.SliderStyle.Setting, min, max);

        var sliderLE = slider.gameObject.AddComponent<LayoutElement>();
        sliderLE.minWidth = 180;
        sliderLE.preferredWidth = 180;
        sliderLE.flexibleWidth = 1;

        slider.SetValue(initial, false);

        // Value text
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(rowObj.transform, false);

        var valueLE = valueObj.AddComponent<LayoutElement>();
        valueLE.minWidth = 60;
        valueLE.preferredWidth = 60;

        valueText = valueObj.AddComponent<TextMeshProUGUI>();
        valueText.font = _font;
        valueText.fontSize = 22;
        valueText.color = _primaryColor;
        valueText.alignment = TextAlignmentOptions.MidlineRight;

        return slider;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Show the settings popup.
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
        _popup.SetActive(true);
    }

    /// <summary>
    /// Hide the settings popup.
    /// </summary>
    public void Hide()
    {
        _popup.SetActive(false);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Set current projection type.
    /// </summary>
    public void SetProjection(VideoProjectionType projection)
    {
        _currentProjection = projection;

        if (_projectionDropdown != null)
        {
            _projectionDropdown.SetValueWithoutNotify((int)projection);
        }

        // Show/hide screen settings based on projection
        UpdateScreenSettingsVisibility();
    }

    /// <summary>
    /// Set available subtitles.
    /// </summary>
    public void SetAvailableSubtitles(List<SubtitleFileInfo> subtitles)
    {
        _availableSubtitles = subtitles ?? new List<SubtitleFileInfo>();

        if (_subtitleDropdown != null)
        {
            _subtitleDropdown.ClearOptions();

            var options = new List<string> { "Off" };
            foreach (var sub in _availableSubtitles)
            {
                options.Add($"{sub.Language} ({sub.FileName})");
            }

            _subtitleDropdown.AddOptions(options);
        }
    }

    /// <summary>
    /// Set screen settings values.
    /// </summary>
    public void SetScreenSettings(float distance, float scale, float curvature)
    {
        _screenDistance = distance;
        _screenScale = scale;
        _screenCurvature = curvature;

        if (_distanceSlider != null)
        {
            _distanceSlider.SetValueWithoutNotify(distance);
            _distanceValue.text = $"{distance:F1}m";
        }

        if (_scaleSlider != null)
        {
            _scaleSlider.SetValueWithoutNotify(scale);
            _scaleValue.text = $"{scale:F1}x";
        }

        if (_curvatureSlider != null)
        {
            _curvatureSlider.SetValueWithoutNotify(curvature);
            _curvatureValue.text = $"{(int)(curvature * 100)}%";
        }
    }

    /// <summary>
    /// Set lights state.
    /// </summary>
    public void SetLightsState(bool isOn)
    {
        _lightsOn = isOn;
        if (_lightsToggle != null)
        {
            _lightsToggle.SetIsOnWithoutNotify(isOn);
        }
    }
    #endregion

    #region Event Handlers
    private void OnProjectionDropdownChanged(int index)
    {
        _currentProjection = (VideoProjectionType)index;
        OnProjectionChanged?.Invoke(_currentProjection);
        UpdateScreenSettingsVisibility();
    }

    private void OnSubtitleDropdownChanged(int index)
    {
        if (index == 0)
        {
            _currentSubtitle = null;
            OnSubtitleSelected?.Invoke(null);
        }
        else if (index - 1 < _availableSubtitles.Count)
        {
            _currentSubtitle = _availableSubtitles[index - 1].FilePath;
            OnSubtitleSelected?.Invoke(_currentSubtitle);
        }
    }

    private void UpdateScreenSettingsVisibility()
    {
        if (_screenSettingsSection != null)
        {
            // Only show screen settings for Flat projection
            bool showScreenSettings = _currentProjection == VideoProjectionType.Flat;

            _screenSettingsSection.SetActive(showScreenSettings);
        }
    }

    private void ResetToDefaults()
    {
        SetScreenSettings(2.0f, 1.0f, 0f);
        SetLightsState(true);

        OnScreenDistanceChanged?.Invoke(2.0f);
        OnScreenScaleChanged?.Invoke(1.0f);
        OnScreenCurvatureChanged?.Invoke(0f);
        OnLightsToggled?.Invoke(true);
    }

    private void ApplySettings()
    {
        // Settings are applied in real-time via events
        Hide();
    }
    #endregion
}
