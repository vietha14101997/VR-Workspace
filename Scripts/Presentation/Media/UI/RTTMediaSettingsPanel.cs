using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.RTT.Input;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// Settings side panel for VR video player.
    /// Displayed inside SideControlsFrame, toggled with Queue panel.
    /// Provides navigation: main menu → sub-pages (back button returns).
    /// Content adapts based on Flat/Immersive mode.
    /// </summary>
    public partial class RTTMediaSettingsPanel : MonoBehaviour
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
        private const float PLUS_MINUS_BTN_SIZE = 46f;
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
        public event Action<float> OnRollChanged;
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
        private static Dictionary<int, Sprite> _sizedPillCache = new Dictionary<int, Sprite>();
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
        private VRSliderControl _rollSlider;
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
        private TextMeshProUGUI _rollValueLabel;
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

            FormatValueLabel(_sharpnessValueLabel, _sharpnessSlider, snapshot.Sharpness);
            FormatValueLabel(_brightnessValueLabel, _brightnessSlider, snapshot.Brightness);
            FormatValueLabel(_saturationValueLabel, _saturationSlider, snapshot.Saturation);
            FormatValueLabel(_contrastValueLabel, _contrastSlider, snapshot.Contrast);
            FormatValueLabel(_tintValueLabel, _tintSlider, snapshot.Tint);
            FormatValueLabel(_temperatureValueLabel, _temperatureSlider, snapshot.Temperature);

            // Video adjustments
            if (_3dToggle != null) _3dToggle.isOn = snapshot.Is3D;
            if (_lrInverseToggle != null) _lrInverseToggle.isOn = snapshot.IsLRInverse;
            _currentSpeed = snapshot.Speed;
            UpdateSpeedButtonSelection();

            // Immersive sliders
            _tiltSlider?.SetValueWithoutNotify(snapshot.Tilt);
            _yawSlider?.SetValueWithoutNotify(snapshot.Yaw);
            _rollSlider?.SetValueWithoutNotify(snapshot.Roll);
            _zoomSlider?.SetValueWithoutNotify(snapshot.Zoom);
            _immHeightSlider?.SetValueWithoutNotify(snapshot.ImmHeight);
            _hBalanceSlider?.SetValueWithoutNotify(snapshot.HorizontalBalance);

            FormatValueLabel(_tiltValueLabel, _tiltSlider, snapshot.Tilt);
            FormatValueLabel(_yawValueLabel, _yawSlider, snapshot.Yaw);
            FormatValueLabel(_rollValueLabel, _rollSlider, snapshot.Roll);
            FormatValueLabel(_zoomValueLabel, _zoomSlider, snapshot.Zoom);
            FormatValueLabel(_immHeightValueLabel, _immHeightSlider, snapshot.ImmHeight);
            FormatValueLabel(_hBalanceValueLabel, _hBalanceSlider, snapshot.HorizontalBalance);

            // Screen settings
            _depthSlider?.SetValueWithoutNotify(snapshot.ScreenDepth);
            _scaleSlider?.SetValueWithoutNotify(snapshot.ScreenScale);
            _verticalMoveSlider?.SetValueWithoutNotify(snapshot.VerticalMove);

            FormatValueLabel(_depthValueLabel, _depthSlider, snapshot.ScreenDepth);
            FormatValueLabel(_scaleValueLabel, _scaleSlider, snapshot.ScreenScale);
            FormatValueLabel(_verticalMoveValueLabel, _verticalMoveSlider, snapshot.VerticalMove);

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

        private static void FormatValueLabel(TextMeshProUGUI label, VRSliderControl slider, float value)
        {
            if (label == null) return;
            label.text = slider?.OnFormatPreview != null ? slider.OnFormatPreview(value) : value.ToString("F2");
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

        /// <summary>
        /// Pill sprite sized for a specific element height. Texture height = target height,
        /// so 9-slice borders naturally match without pixelsPerUnitMultiplier scaling.
        /// </summary>
        private static Sprite GetSizedPillSprite(float targetHeight)
        {
            int h = Mathf.Max(4, Mathf.RoundToInt(targetHeight));
            if (_sizedPillCache.TryGetValue(h, out var cached) && cached != null) return cached;

            int texH = h;
            int texW = Mathf.Max(h * 2, 16);
            int radius = texH / 2;

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

            Vector4 border = new Vector4(radius + 1, radius, radius + 1, radius);
            var sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
            _sizedPillCache[h] = sprite;
            return sprite;
        }
        #endregion
    }

    /// <summary>
    /// Snapshot of all settings values for syncing UI state.
    /// </summary>
    public class SettingsSnapshot
    {
        // Picture adjustments
        public float Sharpness = 0.0f;
        public float Brightness = 1.0f;
        public float Saturation = 1.0f;
        public float Contrast = 1.0f;
        public float Tint = 0f;
        public float Temperature = 0f;

        // Video adjustments
        public bool Is3D = true;
        public bool IsLRInverse = false;
        public float Speed = 1.0f;

        // Immersive adjustments
        public float Tilt = 0f;
        public float Yaw = 0f;
        public float Roll = 0f;
        public float Zoom = 300f;
        public float ImmHeight = 0f;
        public float HorizontalBalance = 0f;

        // Screen settings
        public string AspectRatio = "default";
        public float ScreenDepth = 1.8f;
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

}
