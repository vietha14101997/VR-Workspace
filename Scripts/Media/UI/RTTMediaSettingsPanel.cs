using UnityEngine;
using UnityEngine.UI;
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
    private const float MENU_ITEM_HEIGHT_RATIO = 0.125f; // Each item = 12.5% of total height
    private const float MENU_SPACING_RATIO = 0.025f; // Spacing = 2.5% of total height

    // Colors (matching RTTMediaQueuePanel)
    private static readonly Color ITEM_BG = new Color(0.14f, 0.14f, 0.16f, 0.6f);
    private static readonly Color ITEM_HOVER_BG = new Color(0.18f, 0.18f, 0.20f, 0.7f);
    private static readonly Color TEXT_COLOR = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color CHEVRON_COLOR = new Color(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Color THEME_COLOR = new Color(1f, 0.2f, 0.2f, 1f);
    private static readonly Color SEPARATOR_COLOR = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color BG_TOP = ITEM_BG;
    private static readonly Color BG_BOTTOM = new Color(0.173f, 0.173f, 0.173f, 0.75f);
    private static readonly Color HEADER_BTN_BG = new Color(0f, 0f, 0f, 0.75f);

    // Sub-page content
    private const float SLIDER_ROW_HEIGHT = 70f;
    private const float TOGGLE_ROW_HEIGHT = 65f;
    private const float SECTION_HEADER_HEIGHT = 45f;
    private const float BUTTON_ROW_HEIGHT = 55f;
    private const float SEGMENT_ROW_HEIGHT = 80f;
    private const float SUB_PAGE_PADDING_TOP = 15f;
    private const float SUB_PAGE_SPACING = 8f;

    // Hover
    private const float HOVER_SCALE = 1.03f;

    // Icons
    private const string ICON_BACK = "icon_back";
    private const string ICON_PICTURE = "icon_settings_2";
    private const string ICON_VIDEO = "icon_media";
    private const string ICON_PASSTHROUGH = "icon_passthrough";
    private const string ICON_SCREEN = "icon_monitor";
    private const string ICON_UI = "icon_move";
    private const string ICON_HOTKEYS = "icon_remote";
    private const string ICON_PLAYER = "icon_settings";
    private const string ICON_REFRESH = "icon_refresh";
    private const string ICON_3D = "icon_cube";
    private const string ICON_SWAP = "icon_shuffle";
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

    #region Events - Additional Menu Items
    public event Action OnPassthroughClicked;
    public event Action OnHotkeysSettingsClicked;
    public event Action OnPlayerSettingsClicked;
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
        headerBg.color = new Color(0.12f, 0.12f, 0.14f, 0.90f);
        headerBg.raycastTarget = false;

        var headerLayout = headerObj.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        headerLayout.spacing = 8f;

        // Back button
        CreateBackButton(headerObj.transform);

        // Title text
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(headerObj.transform, false);

        var titleLE = titleObj.AddComponent<LayoutElement>();
        titleLE.flexibleWidth = 1f;

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
        float btnSize = Mathf.Round(_headerHeight * 0.7f);

        _backButton = new GameObject("Btn_Back");
        _backButton.transform.SetParent(parent, false);

        var btnLE = _backButton.AddComponent<LayoutElement>();
        btnLE.minWidth = btnSize;
        btnLE.minHeight = btnSize;
        btnLE.preferredWidth = btnSize;
        btnLE.preferredHeight = btnSize;

        var bgImage = _backButton.AddComponent<Image>();
        bgImage.sprite = GetCircleSprite();
        bgImage.color = HEADER_BTN_BG;
        bgImage.type = Image.Type.Simple;

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

        // Menu items (order matches reference: Picture, Video, Passthrough, Screen, UI, Hotkeys, Player)
        CreateMenuItem(scrollContent, ICON_PICTURE, "Picture adjustments",
            () => NavigateToPage(Page.PictureAdjustments));

        CreateMenuItem(scrollContent, ICON_VIDEO, "Video adjustments",
            () => NavigateToPage(Page.VideoAdjustments));

        CreateMenuItem(scrollContent, ICON_PASSTHROUGH, "Passthrough",
            () => OnPassthroughClicked?.Invoke());

        _screenSettingsMenuItem = CreateMenuItem(scrollContent, ICON_SCREEN, "Screen settings",
            () => NavigateToPage(Page.ScreenSettings));

        _uiSettingsMenuItem = CreateMenuItem(scrollContent, ICON_UI, "Open UI settings",
            () => OnUISettingsRequested?.Invoke());

        CreateMenuItem(scrollContent, ICON_HOTKEYS, "Hotkeys settings",
            () => OnHotkeysSettingsClicked?.Invoke());

        CreateMenuItem(scrollContent, ICON_PLAYER, "Player settings",
            () => OnPlayerSettingsClicked?.Invoke());
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
        itemLayout.spacing = 15f;
        itemLayout.padding = new RectOffset(25, 20, 0, 0);
        itemLayout.childAlignment = TextAnchor.MiddleLeft;
        itemLayout.childControlWidth = true;
        itemLayout.childControlHeight = false;
        itemLayout.childForceExpandWidth = false;
        itemLayout.childForceExpandHeight = false;

        // Icon
        float iconSize = Mathf.Round(_menuItemHeight * 0.35f);
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
        labelText.fontSize = 28;
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

        float sliderWidth = _width - _sideMargin * 2 - 60f; // Leave room for reset button

        // Sharpen slider (0 - 2, default 0.5)
        (_sharpnessSlider, _sharpnessValueLabel) = CreateSliderRow(
            scrollContent, "Sharpen", sliderWidth, 0f, 2f, 0.5f,
            (v) => { OnSharpnessChanged?.Invoke(v); UpdateValueLabel(_sharpnessValueLabel, v); });

        // Brightness slider (0 - 2, default 1.0)
        (_brightnessSlider, _brightnessValueLabel) = CreateSliderRow(
            scrollContent, "Brightness", sliderWidth, 0f, 2f, 1.0f,
            (v) => { OnBrightnessChanged?.Invoke(v); UpdateValueLabel(_brightnessValueLabel, v); });

        // Saturation slider (0 - 2, default 1.0)
        (_saturationSlider, _saturationValueLabel) = CreateSliderRow(
            scrollContent, "Saturation", sliderWidth, 0f, 2f, 1.0f,
            (v) => { OnSaturationChanged?.Invoke(v); UpdateValueLabel(_saturationValueLabel, v); });

        // Contrast slider (0 - 2, default 1.0)
        (_contrastSlider, _contrastValueLabel) = CreateSliderRow(
            scrollContent, "Contrast", sliderWidth, 0f, 2f, 1.0f,
            (v) => { OnContrastChanged?.Invoke(v); UpdateValueLabel(_contrastValueLabel, v); });

        // Tint slider (-1 - 1, default 0)
        (_tintSlider, _tintValueLabel) = CreateSliderRow(
            scrollContent, "Tint", sliderWidth, -1f, 1f, 0f,
            (v) => { OnTintChanged?.Invoke(v); UpdateValueLabel(_tintValueLabel, v); });

        // Temperature slider (-1 - 1, default 0)
        (_temperatureSlider, _temperatureValueLabel) = CreateSliderRow(
            scrollContent, "Temperature", sliderWidth, -1f, 1f, 0f,
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
            (v) => OnLRInverseChanged?.Invoke(v), ICON_SWAP);

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
        immLayout.spacing = SUB_PAGE_SPACING;
        immLayout.padding = new RectOffset(0, 0, 10, 0);
        immLayout.childControlWidth = true;
        immLayout.childControlHeight = false;
        immLayout.childForceExpandWidth = true;
        immLayout.childForceExpandHeight = false;

        var immCSF = _videoAdjImmersiveContent.AddComponent<ContentSizeFitter>();
        immCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        float sliderWidth = _width - _sideMargin * 2 - 60f;

        // Tilt slider (-90 to 90, default 0)
        (_tiltSlider, _tiltValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Tilt", sliderWidth, -90f, 90f, 0f,
            (v) => { OnTiltChanged?.Invoke(v); UpdateValueLabel(_tiltValueLabel, v, "°"); });

        // Yaw slider (-180 to 180, default 0)
        (_yawSlider, _yawValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Yaw", sliderWidth, -180f, 180f, 0f,
            (v) => { OnYawChanged?.Invoke(v); UpdateValueLabel(_yawValueLabel, v, "°"); });

        // Zoom slider (180 to 420, default 300)
        (_zoomSlider, _zoomValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Zoom", sliderWidth, 180f, 420f, 300f,
            (v) => { OnZoomChanged?.Invoke(v); UpdateValueLabel(_zoomValueLabel, v, "°"); });

        // Height slider (-1 to 1, default 0)
        (_immHeightSlider, _immHeightValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "Height", sliderWidth, -1f, 1f, 0f,
            (v) => { OnHeightChanged?.Invoke(v); UpdateValueLabel(_immHeightValueLabel, v); });

        // Horizontal balance slider (-1 to 1, default 0)
        (_hBalanceSlider, _hBalanceValueLabel) = CreateSliderRow(
            _videoAdjImmersiveContent.transform, "H. Balance", sliderWidth, -1f, 1f, 0f,
            (v) => { OnHorizontalBalanceChanged?.Invoke(v); UpdateValueLabel(_hBalanceValueLabel, v); });

        // Set initial mode
        _videoAdjFlatContent.SetActive(!_isImmersive);
        _videoAdjImmersiveContent.SetActive(_isImmersive);
    }

    private void CreateSpeedRow(Transform parent)
    {
        GameObject rowObj = new GameObject("SpeedRow");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = SEGMENT_ROW_HEIGHT;
        rowLE.preferredHeight = SEGMENT_ROW_HEIGHT;

        var rowLayout = rowObj.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.padding = new RectOffset((int)_sideMargin, (int)_sideMargin, 5, 5);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        // Label
        GameObject labelObj = new GameObject("SpeedLabel");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.minHeight = 28;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = "Speed";
        labelText.fontSize = 24;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Segment container
        GameObject segContainer = new GameObject("SpeedSegments");
        segContainer.transform.SetParent(rowObj.transform, false);

        var segLE = segContainer.AddComponent<LayoutElement>();
        segLE.flexibleHeight = 1f;

        var segLayout = segContainer.AddComponent<HorizontalLayoutGroup>();
        segLayout.spacing = 4f;
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
            CreateSpeedButton(segContainer.transform, speed);
        }

        UpdateSpeedButtonSelection();
    }

    private void CreateSpeedButton(Transform parent, float speed)
    {
        string label = speed == 1f ? "1" :
                       speed < 1f ? speed.ToString("0.##") :
                       speed.ToString("0.##");

        GameObject btnObj = new GameObject($"Speed_{label}");
        btnObj.transform.SetParent(parent, false);

        // Background
        var bgImage = btnObj.AddComponent<Image>();
        bgImage.sprite = GetCircleSprite();
        bgImage.color = Color.clear;
        bgImage.type = Image.Type.Simple;

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
        tmp.fontSize = 18;
        tmp.color = TEXT_COLOR;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // Collider
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(50, 40, 10);
        col.center = new Vector3(0, 0, -5);

        _speedButtons.Add(button);
    }

    private void UpdateSpeedButtonSelection()
    {
        float[] speeds = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
        for (int i = 0; i < _speedButtons.Count && i < speeds.Length; i++)
        {
            bool isSelected = Mathf.Approximately(speeds[i], _currentSpeed);
            var bg = _speedButtons[i].GetComponent<Image>();
            var text = _speedButtons[i].GetComponentInChildren<TextMeshProUGUI>();

            if (bg != null) bg.color = isSelected ? THEME_COLOR : Color.clear;
            if (text != null) text.color = isSelected ? Color.white : TEXT_COLOR;
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

        float sliderWidth = _width - _sideMargin * 2 - 60f;

        // Depth slider (1.0 - 5.0, default 2.0)
        (_depthSlider, _depthValueLabel) = CreateSliderRow(
            scrollContent, "Depth", sliderWidth, 1.0f, 5.0f, 2.0f,
            (v) => { OnScreenDepthChanged?.Invoke(v); UpdateValueLabel(_depthValueLabel, v, "m"); });

        // Scale slider (0.5 - 3.0, default 1.0)
        (_scaleSlider, _scaleValueLabel) = CreateSliderRow(
            scrollContent, "Scale", sliderWidth, 0.5f, 3.0f, 1.0f,
            (v) => { OnScreenScaleChanged?.Invoke(v); UpdateValueLabel(_scaleValueLabel, v, "x"); });

        // Vertical move slider (-1.0 - 1.0, default 0)
        (_verticalMoveSlider, _verticalMoveValueLabel) = CreateSliderRow(
            scrollContent, "Vertical", sliderWidth, -1.0f, 1.0f, 0f,
            (v) => { OnVerticalMoveChanged?.Invoke(v); UpdateValueLabel(_verticalMoveValueLabel, v, "m"); });

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
        GameObject rowObj = new GameObject("AspectRatioRow");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = SEGMENT_ROW_HEIGHT * 2; // Two rows
        rowLE.preferredHeight = SEGMENT_ROW_HEIGHT * 2;

        var rowLayout = rowObj.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 6f;
        rowLayout.padding = new RectOffset((int)_sideMargin, (int)_sideMargin, 5, 5);
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        // Label
        GameObject labelObj = new GameObject("AspectLabel");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.minHeight = 28;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = "Aspect ratio";
        labelText.fontSize = 24;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Row 1: Default, 4:3, 3:2
        var row1 = CreateAspectButtonRow(rowObj.transform);
        CreateAspectButton(row1.transform, "Default", "default");
        CreateAspectButton(row1.transform, "4:3", "4:3");
        CreateAspectButton(row1.transform, "3:2", "3:2");

        // Row 2: 16:9, 2:1
        var row2 = CreateAspectButtonRow(rowObj.transform);
        CreateAspectButton(row2.transform, "16:9", "16:9");
        CreateAspectButton(row2.transform, "2:1", "2:1");

        UpdateAspectButtonSelection();
    }

    private GameObject CreateAspectButtonRow(Transform parent)
    {
        GameObject rowObj = new GameObject("AspectBtnRow");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = 40;
        rowLE.preferredHeight = 40;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        return rowObj;
    }

    private void CreateAspectButton(Transform parent, string label, string aspectValue)
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
        tmp.fontSize = 22;
        tmp.color = TEXT_COLOR;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // Collider
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(100, 40, 10);
        col.center = new Vector3(0, 0, -5);

        _aspectButtons.Add(button);
    }

    private void UpdateAspectButtonSelection()
    {
        string[] values = { "default", "4:3", "3:2", "16:9", "2:1" };
        for (int i = 0; i < _aspectButtons.Count && i < values.Length; i++)
        {
            bool isSelected = values[i] == _currentAspect;
            var bg = _aspectButtons[i].GetComponent<Image>();
            var text = _aspectButtons[i].GetComponentInChildren<TextMeshProUGUI>();

            if (bg != null) bg.color = isSelected ? THEME_COLOR : ITEM_BG;
            if (text != null) text.color = isSelected ? Color.white : TEXT_COLOR;
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
        contentLayout.spacing = SUB_PAGE_SPACING;
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
    /// Create a slider row with label, slider, value text, and reset button.
    /// Returns (slider, valueLabel).
    /// </summary>
    private (VRSliderControl, TextMeshProUGUI) CreateSliderRow(
        Transform parent, string label, float sliderWidth,
        float min, float max, float defaultValue, Action<float> onChanged)
    {
        GameObject rowObj = new GameObject($"SliderRow_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = SLIDER_ROW_HEIGHT;
        rowLE.preferredHeight = SLIDER_ROW_HEIGHT;

        // Top part: Label + Value
        // Bottom part: Slider + Reset button
        var rowLayout = rowObj.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 2f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        // --- Label row ---
        GameObject labelRow = new GameObject("LabelRow");
        labelRow.transform.SetParent(rowObj.transform, false);

        var labelRowLE = labelRow.AddComponent<LayoutElement>();
        labelRowLE.minHeight = 26;
        labelRowLE.preferredHeight = 26;

        var labelRowLayout = labelRow.AddComponent<HorizontalLayoutGroup>();
        labelRowLayout.childControlWidth = true;
        labelRowLayout.childControlHeight = true;
        labelRowLayout.childForceExpandWidth = false;
        labelRowLayout.childForceExpandHeight = true;

        // Label text
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(labelRow.transform, false);

        var labelLE2 = labelObj.AddComponent<LayoutElement>();
        labelLE2.flexibleWidth = 1f;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = label;
        labelText.fontSize = 22;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Value label
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(labelRow.transform, false);

        var valueLE = valueObj.AddComponent<LayoutElement>();
        valueLE.minWidth = 80;

        var valueText = valueObj.AddComponent<TextMeshProUGUI>();
        valueText.font = _font;
        valueText.text = FormatValue(defaultValue);
        valueText.fontSize = 20;
        valueText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        valueText.raycastTarget = false;

        // --- Slider row ---
        GameObject sliderRow = new GameObject("SliderRow");
        sliderRow.transform.SetParent(rowObj.transform, false);

        var sliderRowLE = sliderRow.AddComponent<LayoutElement>();
        sliderRowLE.flexibleHeight = 1f;

        var sliderRowLayout = sliderRow.AddComponent<HorizontalLayoutGroup>();
        sliderRowLayout.spacing = 8f;
        sliderRowLayout.childControlWidth = true;
        sliderRowLayout.childControlHeight = false;
        sliderRowLayout.childForceExpandWidth = false;
        sliderRowLayout.childForceExpandHeight = false;
        sliderRowLayout.childAlignment = TextAnchor.MiddleCenter;

        // Slider
        var slider = VRSliderFactory.CreateSlider(
            sliderRow.transform,
            sliderWidth, 40f,
            _font, THEME_COLOR,
            VRSliderFactory.SliderStyle.Setting,
            min, max);
        slider.SetValueWithoutNotify(defaultValue);

        var sliderLE = slider.GetComponent<LayoutElement>();
        if (sliderLE == null) sliderLE = slider.gameObject.AddComponent<LayoutElement>();
        sliderLE.flexibleWidth = 1f;

        slider.OnValueChanged += (v) => onChanged?.Invoke(v);

        // Reset button
        float resetBtnSize = 35f;
        GameObject resetBtnObj = new GameObject("ResetBtn");
        resetBtnObj.transform.SetParent(sliderRow.transform, false);

        var resetLE = resetBtnObj.AddComponent<LayoutElement>();
        resetLE.minWidth = resetBtnSize;
        resetLE.minHeight = resetBtnSize;
        resetLE.preferredWidth = resetBtnSize;
        resetLE.preferredHeight = resetBtnSize;

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
            UpdateValueLabel(capturedValueText, capturedDefault);
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
        resetIcon.sprite = Resources.Load<Sprite>(ICON_REFRESH);
        resetIcon.color = new Color(1f, 0.4f, 0.4f, 0.8f);
        resetIcon.preserveAspect = true;
        resetIcon.raycastTarget = false;

        // Hover
        var resetHover = resetBtnObj.AddComponent<HoverEffectController>();
        resetHover.AddEffect(new ScaleHoverEffect().WithHoverScale(1.2f).WithTransitionDuration(0.06f));

        var resetCol = resetBtnObj.AddComponent<BoxCollider>();
        resetCol.size = new Vector3(resetBtnSize * 1.5f, resetBtnSize * 1.5f, 10);
        resetCol.center = new Vector3(0, 0, -5);

        return (slider, valueText);
    }

    /// <summary>
    /// Create a toggle row with optional icon, label, and custom toggle switch.
    /// </summary>
    private Toggle CreateToggleRow(Transform parent, string label, bool defaultValue, Action<bool> onChanged, string iconName = null)
    {
        GameObject rowObj = new GameObject($"ToggleRow_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = TOGGLE_ROW_HEIGHT;
        rowLE.preferredHeight = TOGGLE_ROW_HEIGHT;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.padding = new RectOffset((int)_sideMargin, (int)_sideMargin, 5, 5);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        // Icon (optional, displayed before label)
        if (!string.IsNullOrEmpty(iconName))
        {
            float iconSize = 32f;
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
        labelLE2.minHeight = TOGGLE_ROW_HEIGHT - 10;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = label;
        labelText.fontSize = 24;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Toggle switch
        float trackW = 55f, trackH = 30f;
        float thumbSize = trackH - 4f;

        GameObject toggleObj = new GameObject("Toggle");
        toggleObj.transform.SetParent(rowObj.transform, false);

        var toggleLE = toggleObj.AddComponent<LayoutElement>();
        toggleLE.minWidth = trackW;
        toggleLE.minHeight = trackH;
        toggleLE.preferredWidth = trackW;
        toggleLE.preferredHeight = trackH;

        // Track background
        var trackImage = toggleObj.AddComponent<Image>();
        trackImage.sprite = GetRoundedRectSprite();
        trackImage.type = Image.Type.Sliced;
        trackImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        // Thumb
        GameObject thumbObj = new GameObject("Thumb");
        thumbObj.transform.SetParent(toggleObj.transform, false);

        var thumbRT = thumbObj.AddComponent<RectTransform>();
        thumbRT.sizeDelta = new Vector2(thumbSize, thumbSize);
        thumbRT.anchorMin = new Vector2(0, 0.5f);
        thumbRT.anchorMax = new Vector2(0, 0.5f);
        thumbRT.pivot = new Vector2(0.5f, 0.5f);
        // Position: off = left (thumbSize/2 + 2), on = right (trackW - thumbSize/2 - 2)
        float offX = thumbSize / 2f + 2f;
        thumbRT.anchoredPosition = new Vector2(defaultValue ? (trackW - offX) : offX, 0);

        var thumbImage = thumbObj.AddComponent<Image>();
        thumbImage.sprite = GetCircleSprite();
        thumbImage.color = Color.white;
        thumbImage.raycastTarget = false;

        // Unity Toggle component
        var toggle = toggleObj.AddComponent<Toggle>();
        toggle.isOn = defaultValue;
        toggle.targetGraphic = trackImage;
        toggle.graphic = null; // We handle visuals manually
        toggle.transition = Selectable.Transition.None;

        // Update visuals on toggle
        toggle.onValueChanged.AddListener((val) =>
        {
            trackImage.color = val ? THEME_COLOR : new Color(0.3f, 0.3f, 0.3f, 1f);
            thumbRT.anchoredPosition = new Vector2(val ? (trackW - offX) : offX, 0);
            onChanged?.Invoke(val);
        });

        // Initial visual state
        if (defaultValue)
            trackImage.color = THEME_COLOR;

        // Collider
        var col = toggleObj.AddComponent<BoxCollider>();
        col.size = new Vector3(trackW * 1.3f, trackH * 1.5f, 10);
        col.center = new Vector3(trackW / 2f, 0, -5);

        return toggle;
    }

    /// <summary>
    /// Create an action button (Save defaults, Reset, etc.).
    /// </summary>
    private void CreateActionButton(Transform parent, string label, Action onClick)
    {
        GameObject btnObj = new GameObject($"Btn_{label.Replace(" ", "")}");
        btnObj.transform.SetParent(parent, false);

        var btnLE = btnObj.AddComponent<LayoutElement>();
        btnLE.minHeight = BUTTON_ROW_HEIGHT;
        btnLE.preferredHeight = BUTTON_ROW_HEIGHT;

        var bgImage = btnObj.AddComponent<Image>();
        bgImage.sprite = GetRoundedRectSprite();
        bgImage.type = Image.Type.Sliced;
        bgImage.color = ITEM_BG;

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
        tmp.fontSize = 24;
        tmp.color = TEXT_COLOR;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        // Hover
        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f).WithTransitionDuration(0.06f));
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("")
            .WithHoverColor(ITEM_HOVER_BG));

        // Collider
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(_width - _sideMargin * 2, BUTTON_ROW_HEIGHT, 10);
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
