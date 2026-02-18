using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Settings side panel for VR video player.
/// Displayed inside SideControlsFrame, toggled with Queue panel.
/// Provides navigation: main menu → sub-pages (back button returns).
/// Content adapts based on Flat/Immersive mode.
/// </summary>
public class RTTMediaSettingsPanel : MonoBehaviour
{
    #region Constants
    // Layout
    private const float HEADER_RATIO = 0.10f;
    private const float SIDE_MARGIN_RATIO = 0.055f;
    private const float MENU_ITEM_HEIGHT_RATIO = 0.15625f; // Each item = 15.625% of total height (125% of original)
    private const float MENU_SPACING_RATIO = 0.025f; // Spacing = 2.5% of total height

    // Colors (matching RTTMediaQueuePanel)
    private static readonly Color ITEM_BG = new Color(0.14f, 0.14f, 0.16f, 0.6f);
    private static readonly Color ITEM_HOVER_BG = new Color(0.18f, 0.18f, 0.20f, 0.7f);
    private static readonly Color TEXT_COLOR = Color.white;
    private static readonly Color CHEVRON_COLOR = new Color(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Color THEME_COLOR = new Color(1f, 0.2f, 0.2f, 1f);
    private static readonly Color SEPARATOR_COLOR = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color BG_TOP = ITEM_BG;
    private static readonly Color BG_BOTTOM = new Color(0.173f, 0.173f, 0.173f, 0.75f);
    private static readonly Color HEADER_BTN_BG = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color HEADER_BG = new Color(0.12f, 0.12f, 0.14f, 0.90f);

    // Sub-page content (row heights and spacing computed dynamically in Initialize)
    private const float SUB_PAGE_PADDING_TOP = 60f;

    // Slider row layout
    private const float SLIDER_LABEL_MIN_WIDTH = 195f;
    private const float PLUS_MINUS_BTN_SIZE = 38f;
    private static readonly Color PILL_BG_COLOR = new Color(0.1f, 0.1f, 0.12f, 0.85f);

    // Hover
    private const float HOVER_SCALE = 1.03f;

    // Icons
    private const string ICON_BACK = "icon_arrow_left";
    private const string ICON_PICTURE = "icon_picture";
    private const string ICON_VIDEO = "icon_video";
    private const string ICON_SCREEN = "icon_move";
    private const string ICON_UI = "icon_ui_settings";
    private const string ICON_REFRESH = "icon_refresh";
    private const string ICON_RESET = "icon_reset";
    private const string ICON_MINUS = "icon_minus";
    private const string ICON_PLUS = "icon_plus";
    private const string ICON_3D = "icon_cube";
    private const string ICON_REVERSE = "icon_reverse";
    private const string ICON_ARROW = "icon_arrow_right";
    #endregion

    #region Events - Picture Adjustments
    public event Action<float> OnSharpnessChanged;
    public event Action<float> OnBrightnessChanged;
    public event Action<float> OnSaturationChanged;
    public event Action<float> OnContrastChanged;
    public event Action<float> OnTintChanged;
    public event Action<float> OnTemperatureChanged;
    public event Action OnPictureSaveDefaults;
    public event Action OnPictureResetDefaults;
    #endregion

    #region Events - Video Adjustments
    public event Action<bool> On3DChanged;
    public event Action<bool> OnLRInverseChanged;
    public event Action<float> OnSpeedChanged;
    #endregion

    #region Events - Immersive Adjustments
    public event Action<float> OnTiltChanged;
    public event Action<float> OnYawChanged;
    public event Action<float> OnZoomChanged;
    public event Action<float> OnHeightChanged;
    public event Action<float> OnHorizontalBalanceChanged;
    #endregion

    #region Events - Screen Settings (Flat)
    public event Action<string> OnAspectRatioChanged;
    public event Action<float> OnScreenDepthChanged;
    public event Action<float> OnScreenScaleChanged;
    public event Action<float> OnVerticalMoveChanged;
    public event Action OnScreenSettingsReset;
    #endregion

    #region Events - UI Settings
    public event Action OnUISettingsRequested;
    #endregion


    #region Navigation
    private enum Page
    {
        MainMenu,
        PictureAdjustments,
        VideoAdjustments,
        ScreenSettings
    }

    private static readonly Dictionary<Page, string> PAGE_TITLES = new Dictionary<Page, string>
    {
        { Page.MainMenu, "Settings" },
        { Page.PictureAdjustments, "Picture adjustments" },
        { Page.VideoAdjustments, "Video adjustments" },
        { Page.ScreenSettings, "Screen settings" }
    };
    #endregion

    #region Private Fields
    private float _width;
    private float _height;
    private float _headerHeight;
    private float _bodyHeight;
    private float _sideMargin;
    private float _menuItemHeight;
    private float _rowHeight; // 12.5% of panel height, used for all sub-page rows
    private float _rowSpacing; // 2.5% of panel height, spacing between rows
    private TMP_FontAsset _font;
    private bool _isImmersive = false;

    // UI references
    private RectTransform _contentRoot;
    private TextMeshProUGUI _titleText;
    private GameObject _backButton;

    // Page containers
    private GameObject _mainMenuContainer;
    private GameObject _pictureAdjContainer;
    private GameObject _videoAdjContainer;
    private GameObject _screenSettingsContainer;

    // Menu items (for mode switching)
    private GameObject _screenSettingsMenuItem;
    private GameObject _uiSettingsMenuItem;

    // Navigation
    private Stack<Page> _navStack = new Stack<Page>();
    private Page _currentPage = Page.MainMenu;

    // Cached sprites
    private static Sprite _roundedRectSprite;
    private static Sprite _topRoundedRectSprite;
    private static Sprite _circleSprite;
    private static Sprite _pillSprite;
    private float _menuSpacing;

    // Sub-page UI references - Video Adjustments
    private GameObject _videoAdjFlatContent;
    private GameObject _videoAdjImmersiveContent;
    private Toggle _3dToggle;
    private Toggle _lrInverseToggle;
    private List<Button> _speedButtons = new List<Button>();
    private float _currentSpeed = 1.0f;
    // Immersive sliders
    private VRSliderControl _tiltSlider;
    private VRSliderControl _yawSlider;
    private VRSliderControl _zoomSlider;
    private VRSliderControl _immHeightSlider;
    private VRSliderControl _hBalanceSlider;

    // Sub-page UI references - Screen Settings
    private List<Button> _aspectButtons = new List<Button>();
    private string _currentAspect = "default";
    private VRSliderControl _depthSlider;
    private VRSliderControl _scaleSlider;
    private VRSliderControl _verticalMoveSlider;

    // Sub-page UI references - Picture Adjustments
    private VRSliderControl _sharpnessSlider;
    private VRSliderControl _brightnessSlider;
    private VRSliderControl _saturationSlider;
    private VRSliderControl _contrastSlider;
    private VRSliderControl _tintSlider;
    private VRSliderControl _temperatureSlider;

    // Value labels for sliders
    private TextMeshProUGUI _sharpnessValueLabel;
    private TextMeshProUGUI _brightnessValueLabel;
    private TextMeshProUGUI _saturationValueLabel;
    private TextMeshProUGUI _contrastValueLabel;
    private TextMeshProUGUI _tintValueLabel;
    private TextMeshProUGUI _temperatureValueLabel;
    private TextMeshProUGUI _tiltValueLabel;
    private TextMeshProUGUI _yawValueLabel;
    private TextMeshProUGUI _zoomValueLabel;
    private TextMeshProUGUI _immHeightValueLabel;
    private TextMeshProUGUI _hBalanceValueLabel;
    private TextMeshProUGUI _depthValueLabel;
    private TextMeshProUGUI _scaleValueLabel;
    private TextMeshProUGUI _verticalMoveValueLabel;
    #endregion

    #region Public API
    /// <summary>
    /// Initialize the settings panel.
    /// </summary>
    public void Initialize(float width, float height, TMP_FontAsset font)
    {
        _width = width;
        _height = height;
        _font = font;

        _headerHeight = Mathf.Round(_height * HEADER_RATIO);
        _bodyHeight = _height - _headerHeight;
        _sideMargin = Mathf.Round(_width * SIDE_MARGIN_RATIO);
        _menuItemHeight = Mathf.Round(_height * MENU_ITEM_HEIGHT_RATIO);
        _menuSpacing = Mathf.Round(_height * MENU_SPACING_RATIO);
        _rowHeight = Mathf.Round(_height * 0.15625f);
        _rowSpacing = Mathf.Round(_height * 0.025f);

        BuildUI();
        NavigateToPage(Page.MainMenu);
    }

    /// <summary>
    /// Set display mode. Updates menu items and video adjustments content.
    /// </summary>
    public void SetMode(bool isImmersive)
    {
        _isImmersive = isImmersive;

        // Show/hide Screen settings menu item and its separator
        if (_screenSettingsMenuItem != null)
            _screenSettingsMenuItem.SetActive(!isImmersive);
        // Toggle Flat/Immersive content in VideoAdjustments
        if (_videoAdjFlatContent != null)
            _videoAdjFlatContent.SetActive(!isImmersive);
        if (_videoAdjImmersiveContent != null)
            _videoAdjImmersiveContent.SetActive(isImmersive);

        // If currently viewing screen settings in immersive mode, go back
        if (isImmersive && _currentPage == Page.ScreenSettings)
            NavigateBack();
    }

    /// <summary>
    /// Set the "Open UI settings" row to forced hover state (when UI Settings popup is open).
    /// </summary>
    public void SetUISettingsRowForceHover(bool force)
    {
        if (_uiSettingsMenuItem == null) return;
        var hoverCtrl = _uiSettingsMenuItem.GetComponent<HoverEffectController>();
        hoverCtrl?.SetForceHover(force);
    }

    /// <summary>
    /// Navigate to main menu. Called when settings panel is shown.
    /// </summary>
    public void NavigateToMainMenu()
    {
        _navStack.Clear();
        NavigateToPage(Page.MainMenu);
    }

    /// <summary>
    /// Set current values for all settings controls.
    /// </summary>
    public void SetCurrentValues(SettingsSnapshot snapshot)
    {
        if (snapshot == null) return;

        // Picture adjustments
        _sharpnessSlider?.SetValueWithoutNotify(snapshot.Sharpness);
        _brightnessSlider?.SetValueWithoutNotify(snapshot.Brightness);
        _saturationSlider?.SetValueWithoutNotify(snapshot.Saturation);
        _contrastSlider?.SetValueWithoutNotify(snapshot.Contrast);
        _tintSlider?.SetValueWithoutNotify(snapshot.Tint);
        _temperatureSlider?.SetValueWithoutNotify(snapshot.Temperature);

        UpdateValueLabel(_sharpnessValueLabel, snapshot.Sharpness);
        UpdateValueLabel(_brightnessValueLabel, snapshot.Brightness);
        UpdateValueLabel(_saturationValueLabel, snapshot.Saturation);
        UpdateValueLabel(_contrastValueLabel, snapshot.Contrast);
        UpdateValueLabel(_tintValueLabel, snapshot.Tint);
        UpdateValueLabel(_temperatureValueLabel, snapshot.Temperature);

        // Video adjustments
        if (_3dToggle != null) _3dToggle.isOn = snapshot.Is3D;
        if (_lrInverseToggle != null) _lrInverseToggle.isOn = snapshot.IsLRInverse;
        _currentSpeed = snapshot.Speed;
        UpdateSpeedButtonSelection();

        // Immersive sliders
        _tiltSlider?.SetValueWithoutNotify(snapshot.Tilt);
        _yawSlider?.SetValueWithoutNotify(snapshot.Yaw);
        _zoomSlider?.SetValueWithoutNotify(snapshot.Zoom);
        _immHeightSlider?.SetValueWithoutNotify(snapshot.ImmHeight);
        _hBalanceSlider?.SetValueWithoutNotify(snapshot.HorizontalBalance);

        UpdateValueLabel(_tiltValueLabel, snapshot.Tilt, "°");
        UpdateValueLabel(_yawValueLabel, snapshot.Yaw, "°");
        UpdateValueLabel(_zoomValueLabel, snapshot.Zoom, "°");
        UpdateValueLabel(_immHeightValueLabel, snapshot.ImmHeight);
        UpdateValueLabel(_hBalanceValueLabel, snapshot.HorizontalBalance);

        // Screen settings
        _depthSlider?.SetValueWithoutNotify(snapshot.ScreenDepth);
        _scaleSlider?.SetValueWithoutNotify(snapshot.ScreenScale);
        _verticalMoveSlider?.SetValueWithoutNotify(snapshot.VerticalMove);

        UpdateValueLabel(_depthValueLabel, snapshot.ScreenDepth, "m");
        UpdateValueLabel(_scaleValueLabel, snapshot.ScreenScale, "x");
        UpdateValueLabel(_verticalMoveValueLabel, snapshot.VerticalMove, "m");

        _currentAspect = snapshot.AspectRatio ?? "default";
        UpdateAspectButtonSelection();
    }
    #endregion

    #region UI Building
    private void BuildUI()
    {
        _contentRoot = gameObject.GetComponent<RectTransform>();
        if (_contentRoot == null)
            _contentRoot = gameObject.AddComponent<RectTransform>();

        _contentRoot.anchorMin = Vector2.zero;
        _contentRoot.anchorMax = Vector2.one;
        _contentRoot.offsetMin = Vector2.zero;
        _contentRoot.offsetMax = Vector2.zero;

        CreateGradientBackground();
        CreateHeader();
        CreateBodyContainer();
    }

    private void CreateGradientBackground()
    {
        GameObject gradObj = new GameObject("GradientBg");
        gradObj.transform.SetParent(_contentRoot, false);

        var gradRT = gradObj.AddComponent<RectTransform>();
        gradRT.anchorMin = Vector2.zero;
        gradRT.anchorMax = Vector2.one;
        gradRT.offsetMin = Vector2.zero;
        gradRT.offsetMax = Vector2.zero;

        int texW = 64;
        int texH = 128;
        int radius = 12;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

        for (int y = 0; y < texH; y++)
        {
            float t = y / (float)(texH - 1);
            Color gradColor = Color.Lerp(BG_BOTTOM, BG_TOP, t);

            for (int x = 0; x < texW; x++)
            {
                float alpha = gradColor.a;
                Vector2 corner = Vector2.zero;
                bool isCorner = false;

                if (x < radius && y < radius)
                { corner = new Vector2(radius, radius); isCorner = true; }
                else if (x >= texW - radius && y < radius)
                { corner = new Vector2(texW - radius - 1, radius); isCorner = true; }
                else if (x < radius && y >= texH - radius)
                { corner = new Vector2(radius, texH - radius - 1); isCorner = true; }
                else if (x >= texW - radius && y >= texH - radius)
                { corner = new Vector2(texW - radius - 1, texH - radius - 1); isCorner = true; }

                if (isCorner)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), corner);
                    alpha *= Mathf.Clamp01(radius - dist + 0.5f);
                }

                tex.SetPixel(x, y, new Color(gradColor.r, gradColor.g, gradColor.b, alpha));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector4 border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
        var gradImage = gradObj.AddComponent<Image>();
        gradImage.sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
            Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        gradImage.type = Image.Type.Sliced;
        gradImage.color = Color.white;
        gradImage.raycastTarget = false;
    }

    private void CreateHeader()
    {
        GameObject headerObj = new GameObject("SettingsHeader");
        headerObj.transform.SetParent(_contentRoot, false);

        var headerRT = headerObj.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.offsetMin = new Vector2(0, -_headerHeight);
        headerRT.offsetMax = new Vector2(0, 0);

        var headerBg = headerObj.AddComponent<Image>();
        headerBg.sprite = GetTopRoundedRectSprite();
        headerBg.type = Image.Type.Sliced;
        headerBg.color = HEADER_BG;
        headerBg.raycastTarget = false;

        var headerLayout = headerObj.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        headerLayout.spacing = 8f;
        headerLayout.padding = new RectOffset((int)(_sideMargin * 0.85f), (int)_sideMargin, 0, 0);

        // Back button
        CreateBackButton(headerObj.transform);

        // Title text (absolute positioned, centered, ignores HLG)
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(headerObj.transform, false);

        var titleLE = titleObj.AddComponent<LayoutElement>();
        titleLE.ignoreLayout = true;

        var titleRT = titleObj.GetComponent<RectTransform>();
        if (titleRT == null) titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.font = _font;
        _titleText.text = "Settings";
        _titleText.fontSize = 40;
        _titleText.color = TEXT_COLOR;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.raycastTarget = false;
    }

    private void CreateBackButton(Transform parent)
    {
        float btnSize = Mathf.Round(_headerHeight * 0.5f);

        _backButton = new GameObject("Btn_Back");
        _backButton.transform.SetParent(parent, false);

        var btnLE = _backButton.AddComponent<LayoutElement>();
        btnLE.minWidth = btnSize;
        btnLE.minHeight = btnSize;
        btnLE.preferredWidth = btnSize;
        btnLE.preferredHeight = btnSize;

        var bgImage = _backButton.AddComponent<Image>();
        bgImage.color = Color.clear;
        bgImage.raycastTarget = true;

        var button = _backButton.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(NavigateBack);

        // Icon
        GameObject iconObj = new GameObject("IconImage");
        iconObj.transform.SetParent(_backButton.transform, false);
        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.2f, 0.2f);
        iconRT.anchorMax = new Vector2(0.8f, 0.8f);
        iconRT.offsetMin = Vector2.zero;
        iconRT.offsetMax = Vector2.zero;

        var iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>(ICON_BACK);
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        // Hover effect
        var hoverController = _backButton.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f));

        var collider = _backButton.AddComponent<BoxCollider>();
        collider.size = new Vector3(btnSize, btnSize, 10);
        collider.center = new Vector3(0, 0, -5);

        _backButton.SetActive(false); // Hidden on main menu
    }

    private void CreateBodyContainer()
    {
        // Clip container below header
        GameObject clipObj = new GameObject("BodyClip");
        clipObj.transform.SetParent(_contentRoot, false);

        var clipRT = clipObj.AddComponent<RectTransform>();
        clipRT.anchorMin = Vector2.zero;
        clipRT.anchorMax = Vector2.one;
        clipRT.offsetMin = Vector2.zero;
        clipRT.offsetMax = new Vector2(0, -_headerHeight);
        clipObj.AddComponent<RectMask2D>();

        // Create page containers inside body
        CreateMainMenuPage(clipObj.transform);
        CreatePictureAdjPage(clipObj.transform);
        CreateVideoAdjPage(clipObj.transform);
        CreateScreenSettingsPage(clipObj.transform);
    }
    #endregion

    #region Main Menu Page
    private void CreateMainMenuPage(Transform parent)
    {
        _mainMenuContainer = new GameObject("MainMenu");
        _mainMenuContainer.transform.SetParent(parent, false);
        StretchFill(_mainMenuContainer);

        // Use scrollable content for 7 items
        var scrollContent = CreateScrollableContent(_mainMenuContainer.transform);

        // Override padding for menu items
        var contentLayout = scrollContent.GetComponent<VerticalLayoutGroup>();
        if (contentLayout != null)
        {
            contentLayout.spacing = (int)_menuSpacing;
            contentLayout.padding = new RectOffset((int)_sideMargin, (int)_sideMargin, (int)_menuSpacing, 15);
        }

        // Menu items: Picture, Video, Screen, UI
        CreateMenuItem(scrollContent, ICON_PICTURE, "Picture adjustments",
            () => NavigateToPage(Page.PictureAdjustments));

        CreateMenuItem(scrollContent, ICON_VIDEO, "Video adjustments",
            () => NavigateToPage(Page.VideoAdjustments));

        _screenSettingsMenuItem = CreateMenuItem(scrollContent, ICON_SCREEN, "Screen settings",
            () => NavigateToPage(Page.ScreenSettings));

        _uiSettingsMenuItem = CreateMenuItem(scrollContent, ICON_UI, "Open UI settings",
            () => OnUISettingsRequested?.Invoke());
    }

    private GameObject CreateMenuItem(Transform parent, string iconName, string label, Action onClick)
    {
        GameObject itemObj = new GameObject($"MenuItem_{label.Replace(" ", "")}");
        itemObj.transform.SetParent(parent, false);

        var itemLE = itemObj.AddComponent<LayoutElement>();
        itemLE.minHeight = _menuItemHeight;
        itemLE.preferredHeight = _menuItemHeight;

        // Background (opaque gray, for hover)
        var bgImage = itemObj.AddComponent<Image>();
        bgImage.color = new Color(0.18f, 0.18f, 0.20f, 0.85f);
        bgImage.sprite = GetRoundedRectSprite();
        bgImage.type = Image.Type.Sliced;
        bgImage.raycastTarget = true;

        var button = itemObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onClick?.Invoke());

        var itemLayout = itemObj.AddComponent<HorizontalLayoutGroup>();
        itemLayout.spacing = 25f;
        itemLayout.padding = new RectOffset(25, 20, 0, 0);
        itemLayout.childAlignment = TextAnchor.MiddleLeft;
        itemLayout.childControlWidth = true;
        itemLayout.childControlHeight = false;
        itemLayout.childForceExpandWidth = false;
        itemLayout.childForceExpandHeight = false;

        // Icon (75% of original 0.35 ratio = 25% reduction)
        float iconSize = Mathf.Round(_menuItemHeight * 0.2625f);
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(itemObj.transform, false);

        var iconLE = iconObj.AddComponent<LayoutElement>();
        iconLE.minWidth = iconSize;
        iconLE.minHeight = iconSize;
        iconLE.preferredWidth = iconSize;
        iconLE.preferredHeight = iconSize;

        var iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>(iconName);
        iconImage.color = TEXT_COLOR;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(itemObj.transform, false);

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 1f;
        labelLE.minHeight = _menuItemHeight;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = label;
        labelText.fontSize = 35;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Arrow icon
        float arrowSize = Mathf.Round(_menuItemHeight * 0.20f);
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(itemObj.transform, false);

        var arrowLE = arrowObj.AddComponent<LayoutElement>();
        arrowLE.minWidth = arrowSize;
        arrowLE.minHeight = arrowSize;
        arrowLE.preferredWidth = arrowSize;
        arrowLE.preferredHeight = arrowSize;

        var arrowImage = arrowObj.AddComponent<Image>();
        arrowImage.sprite = Resources.Load<Sprite>(ICON_ARROW);
        arrowImage.color = Color.white;
        arrowImage.preserveAspect = true;
        arrowImage.raycastTarget = false;

        // Hover effects
        var hoverController = itemObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(HOVER_SCALE).WithTransitionDuration(0.08f));
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("")
            .WithHoverColor(ITEM_HOVER_BG));

        // Collider for VR raycast
        var collider = itemObj.AddComponent<BoxCollider>();
        float contentWidth = _width - _sideMargin * 2;
        collider.size = new Vector3(contentWidth, _menuItemHeight, 10);
        collider.center = new Vector3(0, 0, -5);

        return itemObj;
    }

    private GameObject CreateSeparator(Transform parent)
    {
        GameObject sepObj = new GameObject("Separator");
        sepObj.transform.SetParent(parent, false);

        var sepLE = sepObj.AddComponent<LayoutElement>();
        sepLE.minHeight = 1;
        sepLE.preferredHeight = 1;

        var sepImage = sepObj.AddComponent<Image>();
        sepImage.color = SEPARATOR_COLOR;
        sepImage.raycastTarget = false;

        return sepObj;
    }
    #endregion

    #region Sub-page: Picture Adjustments
    private void CreatePictureAdjPage(Transform parent)
    {
        _pictureAdjContainer = new GameObject("PictureAdjustments");
        _pictureAdjContainer.transform.SetParent(parent, false);
        StretchFill(_pictureAdjContainer);
        _pictureAdjContainer.SetActive(false);

        // Scrollable content
        var scrollContent = CreateScrollableContent(_pictureAdjContainer.transform);

        // Sharpen slider (0 - 2, default 0.5)
        (_sharpnessSlider, _sharpnessValueLabel) = CreateSliderRow(
            scrollContent, "Sharpen", 0f, 2f, 0.5f,
            (v) => { OnSharpnessChanged?.Invoke(v); UpdateValueLabel(_sharpnessValueLabel, v); });

        // Brightness slider (0 - 2, default 1.0)
        (_brightnessSlider, _brightnessValueLabel) = CreateSliderRow(
            scrollContent, "Brightness", 0f, 2f, 1.0f,
            (v) => { OnBrightnessChanged?.Invoke(v); UpdateValueLabel(_brightnessValueLabel, v); });

        // Saturation slider (0 - 2, default 1.0)
        (_saturationSlider, _saturationValueLabel) = CreateSliderRow(
            scrollContent, "Saturation", 0f, 2f, 1.0f,
            (v) => { OnSaturationChanged?.Invoke(v); UpdateValueLabel(_saturationValueLabel, v); });

        // Contrast slider (0 - 2, default 1.0)
        (_contrastSlider, _contrastValueLabel) = CreateSliderRow(
            scrollContent, "Contrast", 0f, 2f, 1.0f,
            (v) => { OnContrastChanged?.Invoke(v); UpdateValueLabel(_contrastValueLabel, v); });

        // Tint slider (-1 - 1, default 0)
        (_tintSlider, _tintValueLabel) = CreateSliderRow(
            scrollContent, "Tint", -1f, 1f, 0f,
            (v) => { OnTintChanged?.Invoke(v); UpdateValueLabel(_tintValueLabel, v); });

        // Temperature slider (-1 - 1, default 0)
        (_temperatureSlider, _temperatureValueLabel) = CreateSliderRow(
            scrollContent, "Temperature", -1f, 1f, 0f,
            (v) => { OnTemperatureChanged?.Invoke(v); UpdateValueLabel(_temperatureValueLabel, v); });

        // Spacer
        CreateFixedSpacer(scrollContent, 20f);

        // Save as defaults button
        CreateActionButton(scrollContent, "Save as defaults", () => OnPictureSaveDefaults?.Invoke());

        // Reset to defaults button
        CreateActionButton(scrollContent, "Reset to defaults", () =>
        {
            OnPictureResetDefaults?.Invoke();
            // Reset slider visuals to factory defaults
            ResetPictureSliders();
        });
    }

    private void ResetPictureSliders()
    {
        _sharpnessSlider?.SetValueWithoutNotify(0.5f);
        _brightnessSlider?.SetValueWithoutNotify(1.0f);
        _saturationSlider?.SetValueWithoutNotify(1.0f);
        _contrastSlider?.SetValueWithoutNotify(1.0f);
        _tintSlider?.SetValueWithoutNotify(0f);
        _temperatureSlider?.SetValueWithoutNotify(0f);

        UpdateValueLabel(_sharpnessValueLabel, 0.5f);
        UpdateValueLabel(_brightnessValueLabel, 1.0f);
        UpdateValueLabel(_saturationValueLabel, 1.0f);
        UpdateValueLabel(_contrastValueLabel, 1.0f);
        UpdateValueLabel(_tintValueLabel, 0f);
        UpdateValueLabel(_temperatureValueLabel, 0f);
    }
    #endregion

    #region Sub-page: Video Adjustments
    private void CreateVideoAdjPage(Transform parent)
    {
        _videoAdjContainer = new GameObject("VideoAdjustments");
        _videoAdjContainer.transform.SetParent(parent, false);
        StretchFill(_videoAdjContainer);
        _videoAdjContainer.SetActive(false);

        var scrollContent = CreateScrollableContent(_videoAdjContainer.transform);

        // === Common controls (both Flat and Immersive) ===

        // 3D Toggle (with icon)
        _3dToggle = CreateToggleRow(scrollContent, "3D", false,
            (v) => On3DChanged?.Invoke(v), ICON_3D);

        // LR Inverse Toggle (with icon)
        _lrInverseToggle = CreateToggleRow(scrollContent, "LR Inverse", false,
            (v) => OnLRInverseChanged?.Invoke(v), ICON_REVERSE);

        // Speed segment buttons
        CreateSpeedRow(scrollContent);

        // === Flat-only content ===
        _videoAdjFlatContent = new GameObject("FlatContent");
        _videoAdjFlatContent.transform.SetParent(scrollContent, false);
        var flatLE = _videoAdjFlatContent.AddComponent<LayoutElement>();
        flatLE.minHeight = 0;
        flatLE.preferredHeight = 0;
        // Flat has no extra controls beyond 3D/LR/Speed

        // === Immersive-only content ===
        _videoAdjImmersiveContent = new GameObject("ImmersiveContent");
        _videoAdjImmersiveContent.transform.SetParent(scrollContent, false);

        var immLayout = _videoAdjImmersiveContent.AddComponent<VerticalLayoutGroup>();
        immLayout.spacing = _rowSpacing;
        immLayout.padding = new RectOffset(0, 0, 10, 0);
        immLayout.childControlWidth = true;
        immLayout.childControlHeight = false;
        immLayout.childForceExpandWidth = true;
        immLayout.childForceExpandHeight = false;

        var immCSF = _videoAdjImmersiveContent.AddComponent<ContentSizeFitter>();
        immCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Tilt slider (-90 to 90, default 0)
        (_tiltSlider, _tiltValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Tilt", -90f, 90f, 0f,
            (v) => { OnTiltChanged?.Invoke(v); UpdateValueLabel(_tiltValueLabel, v, "°"); }, "°");

        // Yaw slider (-180 to 180, default 0)
        (_yawSlider, _yawValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Yaw", -180f, 180f, 0f,
            (v) => { OnYawChanged?.Invoke(v); UpdateValueLabel(_yawValueLabel, v, "°"); }, "°");

        // Zoom slider (180 to 420, default 300)
        (_zoomSlider, _zoomValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Zoom", 180f, 420f, 300f,
            (v) => { OnZoomChanged?.Invoke(v); UpdateValueLabel(_zoomValueLabel, v, "°"); }, "°");

        // Height slider (-1 to 1, default 0)
        (_immHeightSlider, _immHeightValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Height", -1f, 1f, 0f,
            (v) => { OnHeightChanged?.Invoke(v); UpdateValueLabel(_immHeightValueLabel, v); });

        // Horizontal balance slider (-1 to 1, default 0)
        (_hBalanceSlider, _hBalanceValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "H. Balance", -1f, 1f, 0f,
            (v) => { OnHorizontalBalanceChanged?.Invoke(v); UpdateValueLabel(_hBalanceValueLabel, v); });

        // Set initial mode
        _videoAdjFlatContent.SetActive(!_isImmersive);
        _videoAdjImmersiveContent.SetActive(_isImmersive);
    }

    private void CreateSpeedRow(Transform parent)
    {
        // Outer container with VLG and ContentSizeFitter for auto height
        GameObject rowObj = new GameObject("SpeedRow");
        rowObj.transform.SetParent(parent, false);

        var rowLayout = rowObj.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.padding = new RectOffset(0, 0, 10, 0);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var rowCSF = rowObj.AddComponent<ContentSizeFitter>();
        rowCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Label
        GameObject labelObj = new GameObject("SpeedLabel");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.minHeight = 45f;
        labelLE.preferredHeight = 45f;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = "Speed";
        labelText.fontSize = 35;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Segment container with background
        float segHeight = _menuItemHeight * 0.49f;
        GameObject segContainer = new GameObject("SpeedSegments");
        segContainer.transform.SetParent(rowObj.transform, false);

        var segLE = segContainer.AddComponent<LayoutElement>();
        segLE.minHeight = segHeight;
        segLE.preferredHeight = segHeight;

        var segBg = segContainer.AddComponent<Image>();
        segBg.sprite = GetPillSprite();
        segBg.type = Image.Type.Sliced;
        segBg.pixelsPerUnitMultiplier = 64f / segHeight;
        segBg.color = new Color(0.20f, 0.20f, 0.22f, 1.0f);

        int pillPad = Mathf.RoundToInt(segHeight * 0.25f);
        var segLayout = segContainer.AddComponent<HorizontalLayoutGroup>();
        segLayout.spacing = 0;
        segLayout.padding = new RectOffset(pillPad, pillPad, 0, 0);
        segLayout.childAlignment = TextAnchor.MiddleCenter;
        segLayout.childControlWidth = true;
        segLayout.childControlHeight = true;
        segLayout.childForceExpandWidth = true;
        segLayout.childForceExpandHeight = true;

        // Speed options
        float[] speeds = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
        _speedButtons.Clear();

        foreach (float speed in speeds)
        {
            CreateSpeedButton(segContainer.transform, speed, segHeight);
        }

        UpdateSpeedButtonSelection();
    }

    private static readonly Color SPEED_SELECTED_BG = new Color(0.15f, 0.15f, 0.17f, 0.7f);
    private static readonly Color SPEED_HOVER_BG = new Color(0.28f, 0.28f, 0.30f, 0.6f);

    private void CreateSpeedButton(Transform parent, float speed, float btnHeight)
    {
        string label = speed == 1f ? "1" :
                       speed < 1f ? speed.ToString("0.##") :
                       speed.ToString("0.##");

        GameObject btnObj = new GameObject($"Speed_{label}");
        btnObj.transform.SetParent(parent, false);

        // Background (circle sprite, preserveAspect keeps it circular)
        var bgImage = btnObj.AddComponent<Image>();
        bgImage.sprite = GetCircleSprite();
        bgImage.color = Color.clear;
        bgImage.type = Image.Type.Simple;
        bgImage.preserveAspect = true;

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;

        float capturedSpeed = speed;
        button.onClick.AddListener(() =>
        {
            _currentSpeed = capturedSpeed;
            UpdateSpeedButtonSelection();
            OnSpeedChanged?.Invoke(capturedSpeed);
        });

        // Label
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = label;
        tmp.fontSize = 35;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = TEXT_COLOR;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // Hover effect (background only, not text)
        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("")
            .WithHoverColor(SPEED_HOVER_BG));

        // Collider
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(btnHeight, btnHeight, 10);
        col.center = new Vector3(0, 0, -5);

        _speedButtons.Add(button);
    }

    private void UpdateSpeedButtonSelection()
    {
        Color selectedTextColor = new Color(
            Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
            Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
            Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f), 1f);

        float[] speeds = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
        for (int i = 0; i < _speedButtons.Count && i < speeds.Length; i++)
        {
            bool isSelected = Mathf.Approximately(speeds[i], _currentSpeed);
            var bg = _speedButtons[i].GetComponent<Image>();
            var text = _speedButtons[i].GetComponentInChildren<TextMeshProUGUI>();

            // Selected: dark transparent bg + colored text; Unselected: clear bg + white text
            Color bgColor = isSelected ? SPEED_SELECTED_BG : Color.clear;
            if (bg != null) bg.color = bgColor;
            if (text != null) text.color = isSelected ? selectedTextColor : TEXT_COLOR;

            // Update hover effect original color so pointer exit restores correct state
            var hoverCtrl = _speedButtons[i].GetComponent<HoverEffectController>();
            if (hoverCtrl != null)
            {
                var colorEffect = hoverCtrl.GetEffect("color") as ColorHoverEffect;
                colorEffect?.SetOriginalColor(bgColor);
            }
        }
    }
    #endregion

    #region Sub-page: Screen Settings
    private void CreateScreenSettingsPage(Transform parent)
    {
        _screenSettingsContainer = new GameObject("ScreenSettings");
        _screenSettingsContainer.transform.SetParent(parent, false);
        StretchFill(_screenSettingsContainer);
        _screenSettingsContainer.SetActive(false);

        var scrollContent = CreateScrollableContent(_screenSettingsContainer.transform);

        // Aspect ratio segment buttons
        CreateAspectRatioRow(scrollContent);

        // Depth slider (1.0 - 5.0, default 2.0)
        (_depthSlider, _depthValueLabel) = CreateSliderRow(
            scrollContent, "Depth", 1.0f, 5.0f, 2.0f,
            (v) => { OnScreenDepthChanged?.Invoke(v); UpdateValueLabel(_depthValueLabel, v, "m"); }, "m");

        // Scale slider (0.5 - 3.0, default 1.0)
        (_scaleSlider, _scaleValueLabel) = CreateSliderRow(
            scrollContent, "Scale", 0.5f, 3.0f, 1.0f,
            (v) => { OnScreenScaleChanged?.Invoke(v); UpdateValueLabel(_scaleValueLabel, v, "x"); }, "x");

        // Vertical move slider (-1.0 - 1.0, default 0)
        (_verticalMoveSlider, _verticalMoveValueLabel) = CreateSliderRow(
            scrollContent, "Vertical move", -1.0f, 1.0f, 0f,
            (v) => { OnVerticalMoveChanged?.Invoke(v); UpdateValueLabel(_verticalMoveValueLabel, v, "m"); }, "m");

        // Spacer
        CreateFixedSpacer(scrollContent, 20f);

        // Reset button
        CreateActionButton(scrollContent, "Reset to defaults", () =>
        {
            OnScreenSettingsReset?.Invoke();
            ResetScreenSliders();
        });
    }

    private void ResetScreenSliders()
    {
        _depthSlider?.SetValueWithoutNotify(2.0f);
        _scaleSlider?.SetValueWithoutNotify(1.0f);
        _verticalMoveSlider?.SetValueWithoutNotify(0f);

        UpdateValueLabel(_depthValueLabel, 2.0f, "m");
        UpdateValueLabel(_scaleValueLabel, 1.0f, "x");
        UpdateValueLabel(_verticalMoveValueLabel, 0f, "m");

        _currentAspect = "default";
        UpdateAspectButtonSelection();
    }

    private void CreateAspectRatioRow(Transform parent)
    {
        float gridSpacing = Mathf.Round(_width / 24f);
        float contentWidth = _width - 2f * _sideMargin;
        float btnWidth = Mathf.Round((contentWidth - 2f * gridSpacing) / 3f);
        float btnHeight = Mathf.Round(btnWidth / 3f);
        float labelHeight = 45f;
        float gridHeight = btnHeight * 2f + gridSpacing;
        float totalHeight = labelHeight + gridSpacing + gridHeight;

        GameObject rowObj = new GameObject("AspectRatioRow");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = totalHeight;
        rowLE.preferredHeight = totalHeight;

        var rowLayout = rowObj.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = gridSpacing;
        rowLayout.padding = new RectOffset(0, 0, 0, 0);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        // Force self-sizing so parent VLG (childControlHeight=false) gets correct height
        var rowCSF = rowObj.AddComponent<ContentSizeFitter>();
        rowCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Label
        GameObject labelObj = new GameObject("AspectLabel");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.minHeight = labelHeight;
        labelLE.preferredHeight = labelHeight;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = "Screen by aspect ratio";
        labelText.fontSize = 35;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Grid container (3 columns, auto rows)
        GameObject gridObj = new GameObject("AspectGrid");
        gridObj.transform.SetParent(rowObj.transform, false);

        var gridLE = gridObj.AddComponent<LayoutElement>();
        gridLE.minHeight = gridHeight;
        gridLE.preferredHeight = gridHeight;

        var grid = gridObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(btnWidth, btnHeight);
        grid.spacing = new Vector2(gridSpacing, gridSpacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;

        // Create buttons inside grid
        CreateAspectButton(gridObj.transform, "Default", "default", btnWidth, btnHeight);
        CreateAspectButton(gridObj.transform, "4:3", "4:3", btnWidth, btnHeight);
        CreateAspectButton(gridObj.transform, "3:2", "3:2", btnWidth, btnHeight);
        CreateAspectButton(gridObj.transform, "16:9", "16:9", btnWidth, btnHeight);
        CreateAspectButton(gridObj.transform, "2:1", "2:1", btnWidth, btnHeight);

        UpdateAspectButtonSelection();
    }

    private void CreateAspectButton(Transform parent, string label, string aspectValue, float btnWidth, float btnHeight)
    {
        GameObject btnObj = new GameObject($"Aspect_{aspectValue}");
        btnObj.transform.SetParent(parent, false);

        // Rounded rect background
        var bgImage = btnObj.AddComponent<Image>();
        bgImage.sprite = GetRoundedRectSprite();
        bgImage.type = Image.Type.Sliced;
        bgImage.color = ITEM_BG;

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;

        string capturedValue = aspectValue;
        button.onClick.AddListener(() =>
        {
            _currentAspect = capturedValue;
            UpdateAspectButtonSelection();
            OnAspectRatioChanged?.Invoke(capturedValue);
        });

        // Label
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = label;
        tmp.fontSize = 35;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = TEXT_COLOR;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // Hover effects
        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f).WithTransitionDuration(0.06f));
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("")
            .WithHoverColor(ITEM_HOVER_BG));

        // Collider
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(btnWidth, btnHeight, 10);
        col.center = new Vector3(0, 0, -5);

        _aspectButtons.Add(button);
    }

    private void UpdateAspectButtonSelection()
    {
        Color selectedTextColor = new Color(
            Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
            Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
            Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f),
            1f);

        string[] values = { "default", "4:3", "3:2", "16:9", "2:1" };
        for (int i = 0; i < _aspectButtons.Count && i < values.Length; i++)
        {
            bool isSelected = values[i] == _currentAspect;
            var bg = _aspectButtons[i].GetComponent<Image>();
            var text = _aspectButtons[i].GetComponentInChildren<TextMeshProUGUI>();

            // Selected: text color changes, bg stays same; not clickable, no hover
            if (bg != null) bg.color = ITEM_BG;
            if (text != null) text.color = isSelected ? selectedTextColor : TEXT_COLOR;
            _aspectButtons[i].interactable = !isSelected;

            var hoverCtrl = _aspectButtons[i].GetComponent<HoverEffectController>();
            if (hoverCtrl != null) hoverCtrl.enabled = !isSelected;
        }
    }
    #endregion

    #region Shared UI Components

    /// <summary>
    /// Creates a scrollable VerticalLayoutGroup inside a parent container.
    /// Returns the content transform to add children to.
    /// </summary>
    private Transform CreateScrollableContent(Transform parent)
    {
        // ScrollView
        GameObject scrollObj = new GameObject("ScrollView");
        scrollObj.transform.SetParent(parent, false);
        StretchFill(scrollObj);

        var scrollRect = scrollObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.elasticity = 0.1f;
        scrollRect.scrollSensitivity = 30f;

        // Viewport
        GameObject viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(scrollObj.transform, false);
        StretchFill(viewportObj);
        viewportObj.AddComponent<RectMask2D>();

        var viewportImage = viewportObj.AddComponent<Image>();
        viewportImage.color = Color.clear;

        scrollRect.viewport = viewportObj.GetComponent<RectTransform>();

        // Content
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);

        var contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.offsetMin = new Vector2(0, 0);
        contentRT.offsetMax = new Vector2(0, 0);

        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = _rowSpacing;
        contentLayout.padding = new RectOffset((int)_sideMargin, (int)_sideMargin, (int)SUB_PAGE_PADDING_TOP, 20);
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var contentCSF = contentObj.AddComponent<ContentSizeFitter>();
        contentCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = contentRT;

        return contentObj.transform;
    }

    /// <summary>
    /// Create a slider row with absolute positioning:
    /// [Label] [PillArea: MinusBtn | Slider | PlusBtn] [ResetBtn]
    /// Pill and +/- buttons shown on hover. Value text below seeker, outside pill.
    /// </summary>
    private (VRSliderControl, TextMeshProUGUI) CreateSliderRow(
        Transform parent, string label,
        float min, float max, float defaultValue,
        Action<float> onChanged, string suffix = "")
    {
        // === Layout dimensions (proportional to panel width) ===
        float pillWidth = _width * 0.532f;
        float pillLeft = _width * 0.3f - _sideMargin; // from row left edge
        float sliderWidth = _width * 0.35f;
        float btnAreaWidth = (pillWidth - sliderWidth) / 2f;
        float resetBtnSize = 56f;
        float labelWidth = _width * 0.25f;

        // === Row container (LayoutElement for parent VLG, no inner layout) ===
        GameObject rowObj = new GameObject($"SliderRow_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowRT = rowObj.GetComponent<RectTransform>();
        if (rowRT == null) rowRT = rowObj.AddComponent<RectTransform>();

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = _rowHeight;
        rowLE.preferredHeight = _rowHeight;

        // === Label (left-aligned, full height) ===
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);
        var labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(0f, 1f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.offsetMin = new Vector2(0f, 0f);
        labelRT.offsetMax = new Vector2(labelWidth, 0f);

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = label;
        labelText.fontSize = 35;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.enableWordWrapping = true;
        labelText.raycastTarget = false;

        // === Pill area (absolute positioned, contains bg + buttons + slider) ===
        GameObject pillAreaObj = new GameObject("PillArea");
        pillAreaObj.transform.SetParent(rowObj.transform, false);
        var pillAreaRT = pillAreaObj.AddComponent<RectTransform>();
        pillAreaRT.anchorMin = new Vector2(0f, 0f);
        pillAreaRT.anchorMax = new Vector2(0f, 1f);
        pillAreaRT.pivot = new Vector2(0f, 0.5f);
        pillAreaRT.offsetMin = new Vector2(pillLeft, 0f);
        pillAreaRT.offsetMax = new Vector2(pillLeft + pillWidth, 0f);

        // --- Pill background (stretch fill with vertical inset, hidden by default) ---
        GameObject pillObj = new GameObject("PillBg");
        pillObj.transform.SetParent(pillAreaObj.transform, false);
        var pillRT = pillObj.AddComponent<RectTransform>();
        pillRT.anchorMin = new Vector2(0f, 0.1f);
        pillRT.anchorMax = new Vector2(1f, 0.9f);
        pillRT.offsetMin = Vector2.zero;
        pillRT.offsetMax = Vector2.zero;
        var pillImage = pillObj.AddComponent<Image>();
        pillImage.sprite = GetPillSprite();
        pillImage.type = Image.Type.Sliced;
        pillImage.color = PILL_BG_COLOR;
        pillImage.raycastTarget = false;
        pillObj.SetActive(false);

        // --- Minus button (centered in left btnArea, hidden by default) ---
        float stepSize = (max - min) / 20f;
        GameObject minusBtnObj = CreatePlusMinusButton(pillAreaObj.transform, false);
        var minusBtnRT = minusBtnObj.GetComponent<RectTransform>();
        minusBtnRT.anchorMin = new Vector2(0f, 0.5f);
        minusBtnRT.anchorMax = new Vector2(0f, 0.5f);
        minusBtnRT.pivot = new Vector2(0.5f, 0.5f);
        minusBtnRT.anchoredPosition = new Vector2(btnAreaWidth / 2f, 0f);
        minusBtnRT.sizeDelta = new Vector2(PLUS_MINUS_BTN_SIZE, PLUS_MINUS_BTN_SIZE);
        minusBtnObj.SetActive(false);

        // --- Slider (centered in pill area) ---
        var slider = VRSliderFactory.CreateSlider(
            pillAreaObj.transform,
            sliderWidth, 40f,
            _font, THEME_COLOR,
            VRSliderFactory.SliderStyle.Setting,
            min, max);
        slider.SetValueWithoutNotify(defaultValue);
        slider.PreviewEnabled = false;

        // Increase track and fill thickness by 1.5x (8f → 12f)
        var trackBgChild = slider.transform.Find("SliderArea/TrackBackground");
        if (trackBgChild != null)
        {
            var trt = trackBgChild.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(trt.sizeDelta.x, 12f);
        }
        var fillChild = slider.transform.Find("SliderArea/Fill");
        if (fillChild != null)
        {
            var frt = fillChild.GetComponent<RectTransform>();
            frt.sizeDelta = new Vector2(frt.sizeDelta.x, 12f);
        }

        // Override slider RT to center it in the pill area
        var sliderRT = slider.GetComponent<RectTransform>();
        sliderRT.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRT.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRT.pivot = new Vector2(0.5f, 0.5f);
        sliderRT.anchoredPosition = Vector2.zero;
        // sizeDelta already set by factory to (sliderWidth, 40)

        // --- Plus button (centered in right btnArea, hidden by default) ---
        GameObject plusBtnObj = CreatePlusMinusButton(pillAreaObj.transform, true);
        var plusBtnRT = plusBtnObj.GetComponent<RectTransform>();
        plusBtnRT.anchorMin = new Vector2(1f, 0.5f);
        plusBtnRT.anchorMax = new Vector2(1f, 0.5f);
        plusBtnRT.pivot = new Vector2(0.5f, 0.5f);
        plusBtnRT.anchoredPosition = new Vector2(-btnAreaWidth / 2f, 0f);
        plusBtnRT.sizeDelta = new Vector2(PLUS_MINUS_BTN_SIZE, PLUS_MINUS_BTN_SIZE);
        plusBtnObj.SetActive(false);

        // Wire up +/- click handlers
        float capturedStep = stepSize;
        VRSliderControl sliderRef = slider;
        minusBtnObj.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (sliderRef != null) sliderRef.SetValue(sliderRef.Value - capturedStep);
        });
        plusBtnObj.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (sliderRef != null) sliderRef.SetValue(sliderRef.Value + capturedStep);
        });

        // Min/max button state locking
        var minusButton = minusBtnObj.GetComponent<Button>();
        var plusButton = plusBtnObj.GetComponent<Button>();
        var minusIcon = minusBtnObj.transform.Find("Icon")?.GetComponent<Image>();
        var plusIcon = plusBtnObj.transform.Find("Icon")?.GetComponent<Image>();
        Action<float> updateBtnStates = (v) =>
        {
            bool atMin = v <= min;
            bool atMax = v >= max;
            if (minusButton != null) minusButton.interactable = !atMin;
            if (plusButton != null) plusButton.interactable = !atMax;
            if (minusIcon != null) minusIcon.color = atMin ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
            if (plusIcon != null) plusIcon.color = atMax ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
        };
        updateBtnStates(defaultValue);
        slider.OnValueChanged += updateBtnStates;

        // --- Value text (child of handle, below seeker, outside pill bg) ---
        var handleRT = slider.HandleTransform;
        GameObject valueObj = new GameObject("HoverValue");
        valueObj.transform.SetParent(handleRT, false);
        var valueRT = valueObj.AddComponent<RectTransform>();
        valueRT.anchoredPosition = new Vector2(0, -72f);
        valueRT.sizeDelta = new Vector2(160, 45);

        var valueText = valueObj.AddComponent<TextMeshProUGUI>();
        valueText.font = _font;
        valueText.text = FormatValue(defaultValue, suffix);
        valueText.fontSize = 35;
        valueText.fontStyle = FontStyles.Bold;
        valueText.color = Color.white;
        valueText.alignment = TextAlignmentOptions.Center;
        valueText.overflowMode = TextOverflowModes.Overflow;
        valueText.raycastTarget = false;
        valueObj.SetActive(false);

        // === Hover group management ===
        var hoverGroup = pillAreaObj.AddComponent<SettingsSliderHoverGroup>();
        hoverGroup.Setup(pillObj, minusBtnObj, plusBtnObj, valueObj);

        // Connect slider hover events
        slider.OnHoverEnter += hoverGroup.OnChildHoverEnter;
        slider.OnHoverExit += hoverGroup.OnChildHoverExit;

        // Connect +/- button hover events
        var minusNotifier = minusBtnObj.GetComponent<HoverNotifier>();
        if (minusNotifier != null)
        {
            minusNotifier.onEnter += hoverGroup.OnChildHoverEnter;
            minusNotifier.onExit += hoverGroup.OnChildHoverExit;
        }
        var plusNotifier = plusBtnObj.GetComponent<HoverNotifier>();
        if (plusNotifier != null)
        {
            plusNotifier.onEnter += hoverGroup.OnChildHoverEnter;
            plusNotifier.onExit += hoverGroup.OnChildHoverExit;
        }

        // === Value change callback ===
        string capturedSuffix = suffix;
        slider.OnValueChanged += (v) =>
        {
            onChanged?.Invoke(v);
            UpdateValueLabel(valueText, v, capturedSuffix);
        };

        // === Reset button (right-aligned in row) ===
        GameObject resetBtnObj = new GameObject("ResetBtn");
        resetBtnObj.transform.SetParent(rowObj.transform, false);
        var resetBtnRT = resetBtnObj.AddComponent<RectTransform>();
        resetBtnRT.anchorMin = new Vector2(1f, 0.5f);
        resetBtnRT.anchorMax = new Vector2(1f, 0.5f);
        resetBtnRT.pivot = new Vector2(1f, 0.5f);
        resetBtnRT.sizeDelta = new Vector2(resetBtnSize, resetBtnSize);
        resetBtnRT.anchoredPosition = Vector2.zero;

        var resetBg = resetBtnObj.AddComponent<Image>();
        resetBg.color = Color.clear;
        resetBg.raycastTarget = true;

        var resetBtn = resetBtnObj.AddComponent<Button>();
        resetBtn.targetGraphic = resetBg;
        resetBtn.transition = Selectable.Transition.None;

        float capturedDefault = defaultValue;
        VRSliderControl capturedSlider = slider;
        TextMeshProUGUI capturedValueText = valueText;
        resetBtn.onClick.AddListener(() =>
        {
            capturedSlider.SetValue(capturedDefault);
            UpdateValueLabel(capturedValueText, capturedDefault, capturedSuffix);
        });

        // Reset icon
        GameObject resetIconObj = new GameObject("Icon");
        resetIconObj.transform.SetParent(resetBtnObj.transform, false);
        var resetIconRT = resetIconObj.AddComponent<RectTransform>();
        resetIconRT.anchorMin = new Vector2(0.15f, 0.15f);
        resetIconRT.anchorMax = new Vector2(0.85f, 0.85f);
        resetIconRT.offsetMin = Vector2.zero;
        resetIconRT.offsetMax = Vector2.zero;

        var resetIcon = resetIconObj.AddComponent<Image>();
        resetIcon.sprite = Resources.Load<Sprite>(ICON_RESET);
        resetIcon.color = Color.white;
        resetIcon.preserveAspect = true;
        resetIcon.raycastTarget = false;

        // Hover
        var resetHover = resetBtnObj.AddComponent<HoverEffectController>();
        resetHover.AddEffect(new ScaleHoverEffect().WithHoverScale(1.2f).WithTransitionDuration(0.06f));

        var resetCol = resetBtnObj.AddComponent<BoxCollider>();
        resetCol.size = new Vector3(resetBtnSize * 1.5f, resetBtnSize * 1.5f, 10);
        resetCol.center = new Vector3(0, 0, -5);

        // Row-level hover detection via transparent Image (GraphicRaycaster requires Graphic)
        var rowImage = rowObj.AddComponent<Image>();
        rowImage.color = Color.clear;
        rowImage.raycastTarget = true;
        var rowNotifier = rowObj.AddComponent<HoverNotifier>();
        rowNotifier.onEnter += hoverGroup.OnChildHoverEnter;
        rowNotifier.onExit += hoverGroup.OnChildHoverExit;

        return (slider, valueText);
    }

    /// <summary>
    /// Create a +/- button with icon sprite for the slider hover area.
    /// Uses icon_plus or icon_minus sprites. Click handler wired by caller.
    /// </summary>
    private GameObject CreatePlusMinusButton(Transform parent, bool isPlus)
    {
        string name = isPlus ? "PlusBtn" : "MinusBtn";
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        // RectTransform will be configured by caller for absolute positioning
        var btnRT = btnObj.AddComponent<RectTransform>();

        // Background (transparent for interaction)
        var bgImage = btnObj.AddComponent<Image>();
        bgImage.color = Color.clear;
        bgImage.raycastTarget = true;

        // Icon
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(btnObj.transform, false);
        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.15f, 0.15f);
        iconRT.anchorMax = new Vector2(0.85f, 0.85f);
        iconRT.offsetMin = Vector2.zero;
        iconRT.offsetMax = Vector2.zero;

        var iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>(isPlus ? ICON_PLUS : ICON_MINUS);
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        // Button component (click handler wired up by caller)
        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;

        // Hover notifier for delayed-hide group
        btnObj.AddComponent<HoverNotifier>();

        // Hover scale effect
        var hoverCtrl = btnObj.AddComponent<HoverEffectController>();
        hoverCtrl.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f).WithTransitionDuration(0.06f));

        // Collider for VR interaction
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(PLUS_MINUS_BTN_SIZE * 1.3f, PLUS_MINUS_BTN_SIZE * 1.3f, 10);
        col.center = new Vector3(0, 0, -5);

        return btnObj;
    }

    /// <summary>
    /// Create a toggle row with optional icon, label, and custom toggle switch.
    /// </summary>
    private Toggle CreateToggleRow(Transform parent, string label, bool defaultValue, Action<bool> onChanged, string iconName = null)
    {
        GameObject rowObj = new GameObject($"ToggleRow_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = _menuItemHeight;
        rowLE.preferredHeight = _menuItemHeight;

        // Background (synced with menu items)
        var bgImage = rowObj.AddComponent<Image>();
        bgImage.sprite = GetRoundedRectSprite();
        bgImage.type = Image.Type.Sliced;
        bgImage.color = new Color(0.18f, 0.18f, 0.20f, 0.85f);
        bgImage.raycastTarget = true;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 25f;
        rowLayout.padding = new RectOffset(25, 20, 0, 0);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        // Icon (optional, displayed before label)
        if (!string.IsNullOrEmpty(iconName))
        {
            float iconSize = Mathf.Round(_menuItemHeight * 0.196875f);
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(rowObj.transform, false);

            var iconLE = iconObj.AddComponent<LayoutElement>();
            iconLE.minWidth = iconSize;
            iconLE.minHeight = iconSize;
            iconLE.preferredWidth = iconSize;
            iconLE.preferredHeight = iconSize;

            var iconImage = iconObj.AddComponent<Image>();
            iconImage.sprite = Resources.Load<Sprite>(iconName);
            iconImage.color = TEXT_COLOR;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelLE2 = labelObj.AddComponent<LayoutElement>();
        labelLE2.flexibleWidth = 1f;
        labelLE2.minHeight = _menuItemHeight;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = label;
        labelText.fontSize = 35;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Toggle switch (iOS-style: white pill track, black circular knob)
        float trackH = Mathf.Round(_menuItemHeight * 0.2f);
        float trackW = Mathf.Round(trackH * 1.75f);
        float thumbPad = Mathf.Max(2f, Mathf.Round(trackH * 0.1f));
        float thumbSize = trackH - thumbPad * 2f;

        GameObject toggleObj = new GameObject("Toggle");
        toggleObj.transform.SetParent(rowObj.transform, false);

        var toggleLE = toggleObj.AddComponent<LayoutElement>();
        toggleLE.minWidth = trackW;
        toggleLE.minHeight = trackH;
        toggleLE.preferredWidth = trackW;
        toggleLE.preferredHeight = trackH;

        // Track background (pill sprite with proper semicircle ends)
        var trackImage = toggleObj.AddComponent<Image>();
        trackImage.sprite = GetPillSprite();
        trackImage.type = Image.Type.Sliced;
        trackImage.pixelsPerUnitMultiplier = 64f / trackH;
        trackImage.color = Color.white;

        // Thumb (black circular knob)
        GameObject thumbObj = new GameObject("Thumb");
        thumbObj.transform.SetParent(toggleObj.transform, false);

        var thumbRT = thumbObj.AddComponent<RectTransform>();
        thumbRT.sizeDelta = new Vector2(thumbSize, thumbSize);
        thumbRT.anchorMin = new Vector2(0, 0.5f);
        thumbRT.anchorMax = new Vector2(0, 0.5f);
        thumbRT.pivot = new Vector2(0.5f, 0.5f);
        float offX = thumbPad + thumbSize / 2f;
        thumbRT.anchoredPosition = new Vector2(defaultValue ? (trackW - offX) : offX, 0);

        var thumbImage = thumbObj.AddComponent<Image>();
        thumbImage.sprite = GetCircleSprite();
        thumbImage.preserveAspect = true;
        thumbImage.color = Color.black;
        thumbImage.raycastTarget = false;

        // Unity Toggle component
        var toggle = toggleObj.AddComponent<Toggle>();
        toggle.isOn = defaultValue;
        toggle.targetGraphic = trackImage;
        toggle.graphic = null;
        toggle.transition = Selectable.Transition.None;

        toggle.onValueChanged.AddListener((val) =>
        {
            trackImage.color = val ? THEME_COLOR : Color.white;
            thumbRT.anchoredPosition = new Vector2(val ? (trackW - offX) : offX, 0);
            onChanged?.Invoke(val);
        });

        if (defaultValue)
            trackImage.color = THEME_COLOR;

        // Row-level button so clicking anywhere on the row toggles the switch
        var rowButton = rowObj.AddComponent<Button>();
        rowButton.targetGraphic = bgImage;
        rowButton.transition = Selectable.Transition.None;
        Toggle capturedToggle = toggle;
        rowButton.onClick.AddListener(() => capturedToggle.isOn = !capturedToggle.isOn);

        // Collider covers entire row
        float contentWidth = _width - _sideMargin * 2;
        var col = rowObj.AddComponent<BoxCollider>();
        col.size = new Vector3(contentWidth, _menuItemHeight, 10);
        col.center = new Vector3(0, 0, -5);

        // Hover effect on row background (no scale hover for Video adjustments rows)
        var hoverController = rowObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("")
            .WithHoverColor(ITEM_HOVER_BG));

        return toggle;
    }

    /// <summary>
    /// Create an action button (Save defaults, Reset, etc.).
    /// </summary>
    private void CreateActionButton(Transform parent, string label, Action onClick)
    {
        float btnHeight = Mathf.Round(_rowHeight * 0.8f);

        GameObject btnObj = new GameObject($"Btn_{label.Replace(" ", "")}");
        btnObj.transform.SetParent(parent, false);

        var btnLE = btnObj.AddComponent<LayoutElement>();
        btnLE.minHeight = btnHeight;
        btnLE.preferredHeight = btnHeight;

        var bgImage = btnObj.AddComponent<Image>();
        bgImage.sprite = GetPillSprite();
        bgImage.type = Image.Type.Sliced;
        bgImage.color = HEADER_BG;

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onClick?.Invoke());

        // Label
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = label;
        tmp.fontSize = 35;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = TEXT_COLOR;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // Hover
        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f).WithTransitionDuration(0.06f));
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("")
            .WithHoverColor(new Color(0.18f, 0.18f, 0.20f, 0.95f)));

        // Collider
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(_width - _sideMargin * 2, btnHeight, 10);
        col.center = new Vector3(0, 0, -5);
    }

    private void CreateFixedSpacer(Transform parent, float height)
    {
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(parent, false);

        var le = spacer.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }
    #endregion

    #region Navigation
    private void NavigateToPage(Page page)
    {
        if (_currentPage == page && _navStack.Count > 0) return;

        // Hide current page
        SetPageActive(_currentPage, false);

        // Push current to stack (if not main menu navigating to main menu)
        if (_currentPage != page)
        {
            _navStack.Push(_currentPage);
        }

        _currentPage = page;

        // Show new page
        SetPageActive(page, true);

        // Update header
        _titleText.text = PAGE_TITLES.ContainsKey(page) ? PAGE_TITLES[page] : "Settings";
        _backButton.SetActive(page != Page.MainMenu);
    }

    private void NavigateBack()
    {
        if (_navStack.Count == 0)
        {
            NavigateToPage(Page.MainMenu);
            return;
        }

        SetPageActive(_currentPage, false);
        _currentPage = _navStack.Pop();
        SetPageActive(_currentPage, true);

        _titleText.text = PAGE_TITLES.ContainsKey(_currentPage) ? PAGE_TITLES[_currentPage] : "Settings";
        _backButton.SetActive(_currentPage != Page.MainMenu);
    }

    private void SetPageActive(Page page, bool active)
    {
        switch (page)
        {
            case Page.MainMenu:
                if (_mainMenuContainer != null) _mainMenuContainer.SetActive(active);
                break;
            case Page.PictureAdjustments:
                if (_pictureAdjContainer != null) _pictureAdjContainer.SetActive(active);
                break;
            case Page.VideoAdjustments:
                if (_videoAdjContainer != null) _videoAdjContainer.SetActive(active);
                break;
            case Page.ScreenSettings:
                if (_screenSettingsContainer != null) _screenSettingsContainer.SetActive(active);
                break;
        }
    }
    #endregion

    #region Utilities
    private void StretchFill(GameObject obj)
    {
        var rt = obj.GetComponent<RectTransform>();
        if (rt == null) rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static string FormatValue(float value, string suffix = "")
    {
        return $"{value:F2}{suffix}";
    }

    private static void UpdateValueLabel(TextMeshProUGUI label, float value, string suffix = "")
    {
        if (label != null) label.text = FormatValue(value, suffix);
    }

    private static Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;

        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        float center = size / 2f;
        float radius = size / 2f - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;

        _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
        return _circleSprite;
    }

    private static Sprite GetRoundedRectSprite()
    {
        if (_roundedRectSprite != null) return _roundedRectSprite;

        int texW = 64, texH = 64;
        int radius = 16;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                float alpha = 1f;
                Vector2 corner = Vector2.zero;
                bool isCorner = false;

                if (x < radius && y < radius)
                { corner = new Vector2(radius, radius); isCorner = true; }
                else if (x >= texW - radius && y < radius)
                { corner = new Vector2(texW - radius - 1, radius); isCorner = true; }
                else if (x < radius && y >= texH - radius)
                { corner = new Vector2(radius, texH - radius - 1); isCorner = true; }
                else if (x >= texW - radius && y >= texH - radius)
                { corner = new Vector2(texW - radius - 1, texH - radius - 1); isCorner = true; }

                if (isCorner)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), corner);
                    alpha = Mathf.Clamp01(radius - dist + 0.5f);
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector4 border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
        _roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
            Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        return _roundedRectSprite;
    }

    /// <summary>
    /// Rounded rect with only top-left and top-right corners rounded (bottom corners sharp).
    /// </summary>
    private static Sprite GetTopRoundedRectSprite()
    {
        if (_topRoundedRectSprite != null) return _topRoundedRectSprite;

        int texW = 64, texH = 64;
        int radius = 12;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                float alpha = 1f;
                Vector2 corner = Vector2.zero;
                bool isCorner = false;

                // Only top corners are rounded (top = high y in texture)
                if (x < radius && y >= texH - radius)
                { corner = new Vector2(radius, texH - radius - 1); isCorner = true; }
                else if (x >= texW - radius && y >= texH - radius)
                { corner = new Vector2(texW - radius - 1, texH - radius - 1); isCorner = true; }

                if (isCorner)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), corner);
                    alpha = Mathf.Clamp01(radius - dist + 0.5f);
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        // Border: top corners have radius, bottom corners have 1 (sharp)
        Vector4 border = new Vector4(radius + 1, 1, radius + 1, radius + 1);
        _topRoundedRectSprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
            Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        return _topRoundedRectSprite;
    }

    /// <summary>
    /// Pill-shaped sprite with fully rounded left/right ends (radius = height/2).
    /// </summary>
    private static Sprite GetPillSprite()
    {
        if (_pillSprite != null) return _pillSprite;

        int texW = 128, texH = 64;
        int radius = texH / 2; // Full semicircle ends (32px)
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                float alpha = 1f;
                Vector2 corner = Vector2.zero;
                bool isCorner = false;

                // Bottom-left
                if (x < radius && y < radius)
                { corner = new Vector2(radius, radius); isCorner = true; }
                // Bottom-right
                else if (x >= texW - radius && y < radius)
                { corner = new Vector2(texW - radius - 1, radius); isCorner = true; }
                // Top-left
                else if (x < radius && y >= texH - radius)
                { corner = new Vector2(radius, texH - radius - 1); isCorner = true; }
                // Top-right
                else if (x >= texW - radius && y >= texH - radius)
                { corner = new Vector2(texW - radius - 1, texH - radius - 1); isCorner = true; }

                if (isCorner)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), corner);
                    alpha = Mathf.Clamp01(radius - dist + 0.5f);
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector4 border = new Vector4(radius + 1, radius, radius + 1, radius);
        _pillSprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
            Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        return _pillSprite;
    }
    #endregion
}

/// <summary>
/// Snapshot of all settings values for syncing UI state.
/// </summary>
public class SettingsSnapshot
{
    // Picture adjustments
    public float Sharpness = 0.5f;
    public float Brightness = 1.0f;
    public float Saturation = 1.0f;
    public float Contrast = 1.0f;
    public float Tint = 0f;
    public float Temperature = 0f;

    // Video adjustments
    public bool Is3D = false;
    public bool IsLRInverse = false;
    public float Speed = 1.0f;

    // Immersive adjustments
    public float Tilt = 0f;
    public float Yaw = 0f;
    public float Zoom = 300f;
    public float ImmHeight = 0f;
    public float HorizontalBalance = 0f;

    // Screen settings
    public string AspectRatio = "default";
    public float ScreenDepth = 2.0f;
    public float ScreenScale = 1.0f;
    public float VerticalMove = 0f;
}

/// <summary>
/// Simple hover event forwarder for UI elements (e.g. +/- buttons).
/// Attach to any GameObject with a collider to detect hover enter/exit.
/// </summary>
public class HoverNotifier : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public event Action onEnter;
    public event Action onExit;

    public void OnPointerEnter(PointerEventData eventData) => onEnter?.Invoke();
    public void OnPointerExit(PointerEventData eventData) => onExit?.Invoke();
}

/// <summary>
/// Manages hover state for a settings slider row.
/// Uses polling (RTTRaycastManager.HoveredObject) to check if reticle is within the row hierarchy.
/// This avoids enter/exit timing issues when child elements appear/disappear dynamically.
/// </summary>
public class SettingsSliderHoverGroup : MonoBehaviour
{
    private const float HIDE_DELAY = 0.15f;

    private float _hideTimer = -1f;
    private bool _isVisible = false;
    private GameObject _pillBg;
    private GameObject _minusBtn;
    private GameObject _plusBtn;
    private GameObject _valueText;
    private Transform _rowTransform;

    public void Setup(GameObject pillBg, GameObject minusBtn, GameObject plusBtn, GameObject valueText)
    {
        _pillBg = pillBg;
        _minusBtn = minusBtn;
        _plusBtn = plusBtn;
        _valueText = valueText;
        _rowTransform = transform.parent; // PillArea is direct child of row
    }

    /// <summary>Immediate show (called from enter events for same-frame responsiveness)</summary>
    public void OnChildHoverEnter()
    {
        _hideTimer = -1f;
        if (!_isVisible) SetVisible(true);
    }

    /// <summary>No-op — hiding is handled by Update via row-level hit check</summary>
    public void OnChildHoverExit() { }

    private void Update()
    {
        if (_rowTransform == null) return;

        var manager = RTTRaycastManager.Instance;
        bool isHoveringRow = false;

        if (manager != null)
        {
            var hoveredObj = manager.HoveredObject;
            isHoveringRow = hoveredObj != null && hoveredObj.transform.IsChildOf(_rowTransform);
        }

        if (isHoveringRow)
        {
            _hideTimer = -1f;
            if (!_isVisible) SetVisible(true);
        }
        else if (_isVisible)
        {
            if (_hideTimer < 0f)
                _hideTimer = HIDE_DELAY;

            _hideTimer -= Time.unscaledDeltaTime;
            if (_hideTimer <= 0f)
            {
                _hideTimer = -1f;
                SetVisible(false);
            }
        }
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (_pillBg != null) _pillBg.SetActive(visible);
        if (_minusBtn != null) _minusBtn.SetActive(visible);
        if (_plusBtn != null) _plusBtn.SetActive(visible);
        if (_valueText != null) _valueText.SetActive(visible);
    }
}
