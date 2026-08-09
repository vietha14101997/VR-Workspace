using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// WiFi display mode options for Section 3.
    /// </summary>
    public enum WifiDisplayMode { Icon, Text, Button, None }

    /// <summary>
    /// RTTMiniFrame - A reusable RTT-based frame with glass background, glowing border, and 3 sections.
    /// Extends RTTCanvasBase for full RTT infrastructure.
    /// Section 1 and 2 are generic containers sized by button capacity.
    /// Section 3 is a fixed status area with clock, battery, and configurable wifi display.
    /// </summary>
    public class RTTMiniFrame : RTTCanvasBase
    {
        #region Configuration
        [Header("Section Capacities")]
        [SerializeField] private int section1Capacity = 4;
        [SerializeField] private int section2Capacity = 4;

        [Header("Button Layout")]
        [SerializeField] private float buttonSize = 90f;
        [SerializeField] private float buttonSpacing = 12f;
        [SerializeField] private float sectionSpacing = 37f;
        [SerializeField] private float frameHeight = 128f;

        [Header("Section 3 - Status")]
        [SerializeField] private float section3Width = 206f;
        [SerializeField] private WifiDisplayMode wifiMode = WifiDisplayMode.Icon;

        [Header("Content Margin (pixels)")]
        [SerializeField] private float contentMarginLeft = 0f;
        [SerializeField] private float contentMarginRight = 10f;
        [SerializeField] private float contentMarginTop = 10f;
        [SerializeField] private float contentMarginBottom = 10f;

        [Header("Glowing Border")]
        [ColorUsage(true, true)]
        [SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
        [ColorUsage(true, true)]
        [SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);
    #pragma warning disable 0414 // Reserved for shader customization
        [Range(0f, 0.1f)]
        [SerializeField] private float glowExpansion = 0.02f;
    #pragma warning restore 0414

        [Header("Font")]
        [SerializeField] private TMP_FontAsset customFont;

        [Header("Icons")]
        [SerializeField] private Sprite iconWifi;
        [SerializeField] private Sprite iconBattery;

        [Header("Position Tracking")]
        [SerializeField] private Transform followTarget;
        // Note: Actual positioning is handled by RTTToolbar parent
        #endregion

        #region Private Fields
        private const float PixelToMeter = 1.6f / 1920f;

        private RectTransform _contentContainer;
        private Material _glassMaterial;
        private Material _borderMaterial;
        private Sprite _pixelSprite;
        private Sprite _batterySprite;
        private Sprite _batteryFillSprite;
        private Sprite _wifiSprite;

        // Section containers
        private RectTransform _section1Container;
        private RectTransform _section2Container;
        private RectTransform _section3Container;

        // Status references
        private TextMeshProUGUI _clockText;
        private TextMeshProUGUI _batteryText;
        private Image _networkIcon;
        private TextMeshProUGUI _networkText;
        private Button _networkButton;
        private Image _batteryFillImage;
        private GameObject _networkElement;

        // Calculated values
        private float _totalWidth;
        private float _section1Width;
        private float _section2Width;

        #endregion

        #region Properties
        public RectTransform Section1Container => _section1Container;
        public RectTransform Section2Container => _section2Container;
        public RectTransform Section3Container => _section3Container;
        public float TotalWidth => _totalWidth;
        public float TotalHeight => frameHeight;
        public float Section1Width => _section1Width;
        public float Section2Width => _section2Width;
        public float ButtonSize => buttonSize;
        public float ButtonSpacing => buttonSpacing;
        public int Section1Capacity => section1Capacity;
        public int Section2Capacity => section2Capacity;

        /// <summary>
        /// Get the network text component for custom content (e.g., latency display).
        /// </summary>
        public TextMeshProUGUI GetNetworkText() => _networkText;
        #endregion

        #region Lifecycle
        private const float StatusUpdateInterval = 1f;
        private float _nextStatusUpdateTime;
        private int _lastClockMinute = -1;
        private int _lastBatteryPercent = int.MinValue;
        private NetworkReachability _lastNetworkReachability = (NetworkReachability)(-1);

        protected override void Awake()
        {
            // Calculate sizes before base.Awake()
            RecalculateSize();

            // Set world size
            worldWidth = _totalWidth * PixelToMeter;
            worldHeight = frameHeight * PixelToMeter;

            base.Awake();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _nextStatusUpdateTime = 0f;
        }

        protected override void OnDestroy()
        {
            if (_glassMaterial != null) Destroy(_glassMaterial);
            if (_borderMaterial != null) Destroy(_borderMaterial);
            base.OnDestroy();
        }

        protected override void LateUpdate()
        {
            if (_isInitialized && _isVisible && Time.unscaledTime >= _nextStatusUpdateTime)
            {
                _nextStatusUpdateTime = Time.unscaledTime + StatusUpdateInterval;
                UpdateClock();
                UpdateBattery();
                UpdateNetwork();
            }

            base.LateUpdate();

            // Note: Position tracking is now handled by RTTToolbar parent
        }
        #endregion

        #region RTTCanvasBase Overrides
        protected override Vector2Int GetResolution()
        {
            RecalculateSize();
            return new Vector2Int(
                Mathf.RoundToInt(_totalWidth),
                Mathf.RoundToInt(frameHeight)
            );
        }

        protected override int GetCameraDepth()
        {
            return -49; // Render after menu frame
        }

        protected override void BuildUI()
        {
            if (_canvas == null) return;

            var canvasRect = _canvas.GetComponent<RectTransform>();
            RecalculateSize();

            // 1. Glass Background
            CreateGlassPanel(canvasRect);

            // 2. Content Container
            CreateContentContainer(canvasRect);

            // 3. Three sections
            float contentHeight = frameHeight - contentMarginTop - contentMarginBottom;
            CreateSection1(contentHeight);

            float section2XPos = _section1Width + sectionSpacing;
            CreateSection2(section2XPos, contentHeight);

            float section3XPos = section2XPos + _section2Width + sectionSpacing / 2;
            CreateSection3(section3XPos, contentHeight);

            Debug.Log($"[RTTMiniFrame] UI built: {_totalWidth}x{frameHeight} pixels");
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Configure frame parameters. Call before Awake or use in editor.
        /// </summary>
        public void Configure(int sec1Capacity, int sec2Capacity, float btnSize = 90f, float btnSpacing = 12f, float height = 128f)
        {
            section1Capacity = sec1Capacity;
            section2Capacity = sec2Capacity;
            buttonSize = btnSize;
            buttonSpacing = btnSpacing;
            frameHeight = height;
            RecalculateSize();
        }

        /// <summary>
        /// Set wifi display mode.
        /// </summary>
        public void SetWifiMode(WifiDisplayMode mode)
        {
            wifiMode = mode;
            UpdateNetworkDisplay();
        }

        /// <summary>
        /// Get Section 1 container for adding content.
        /// </summary>
        public RectTransform GetSection1Container() => _section1Container;

        /// <summary>
        /// Get Section 2 container for adding content.
        /// </summary>
        public RectTransform GetSection2Container() => _section2Container;

        /// <summary>
        /// Get Section 3 container for adding content.
        /// </summary>
        public RectTransform GetSection3Container() => _section3Container;

        /// <summary>
        /// Get Section 2's center X offset from frame center (in world units).
        /// Used for aligning expansion panels with Section 2.
        /// </summary>
        public float GetSection2CenterXOffset()
        {
            // Calculate Section 2's center position from canvas left edge (in pixels)
            // Section 2 starts at: contentMarginLeft + _section1Width + sectionSpacing
            // Section 2's center: + _section2Width / 2
            float section2CenterFromLeft = contentMarginLeft + _section1Width + sectionSpacing + (_section2Width / 2f);

            // Canvas center from left edge
            float canvasCenterFromLeft = _totalWidth / 2f;

            // Offset of Section 2 center from canvas center (in pixels)
            float offsetPixels = section2CenterFromLeft - canvasCenterFromLeft;

            // Convert to world units
            const float PixelToMeter = 1.6f / 1920f;
            return offsetPixels * PixelToMeter;
        }

        /// <summary>
        /// Recalculate frame size based on section capacities.
        /// </summary>
        public void RecalculateSize()
        {
            float sectionPadding = 8f;
            _section1Width = sectionPadding + (section1Capacity * buttonSize) + ((section1Capacity - 1) * buttonSpacing) + sectionPadding;
            _section2Width = sectionPadding + (section2Capacity * buttonSize) + ((section2Capacity - 1) * buttonSpacing) + sectionPadding;
            _totalWidth = contentMarginLeft + _section1Width + sectionSpacing + _section2Width + sectionSpacing / 2 + section3Width + contentMarginRight;
        }

        /// <summary>
        /// Factory method to create a configured RTTMiniFrame.
        /// </summary>
        public static RTTMiniFrame Create(
            Transform parent,
            int section1Capacity,
            int section2Capacity,
            float buttonSize = 90f,
            float buttonSpacing = 12f,
            float frameHeight = 128f,
            string name = null)
        {
            string frameName = string.IsNullOrEmpty(name) ? "RTTMiniFrame" : name;
            GameObject frameObj = new GameObject(frameName);
            frameObj.transform.SetParent(parent, false);
            frameObj.transform.localPosition = Vector3.zero;
            frameObj.transform.localRotation = Quaternion.identity;

            RTTMiniFrame frame = frameObj.AddComponent<RTTMiniFrame>();
            frame.Configure(section1Capacity, section2Capacity, buttonSize, buttonSpacing, frameHeight);

            return frame;
        }
        #endregion

        #region UI Building
        private void CreateGlassPanel(RectTransform parent)
        {
            GameObject bgObj = new GameObject("GlassBackground");
            bgObj.transform.SetParent(parent, false);

            Image img = bgObj.AddComponent<Image>();
            img.type = Image.Type.Simple;
            img.sprite = GetPixelSprite();
            img.raycastTarget = true;

            float edgePad = 0.06f;
            float aspect = _totalWidth / frameHeight;

            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                _glassMaterial = new Material(glassShader);

                _glassMaterial.SetFloat("_CornerRadius", 0.12f);
                _glassMaterial.SetFloat("_EdgePadding", edgePad);
                _glassMaterial.SetFloat("_Aspect", aspect);

                _glassMaterial.SetColor("_ColorA", GetGlassColorA());
                _glassMaterial.SetColor("_ColorB", GetGlassColorB());
                _glassMaterial.SetFloat("_GradientOffset", 0f);
                _glassMaterial.SetFloat("_GradientAngle", -10f);
                _glassMaterial.SetFloat("_CyanRatio", 0.7f);
                _glassMaterial.SetFloat("_GlassAlpha", 0.65f);
                _glassMaterial.SetFloat("_FresnelPower", 2.2f);
                _glassMaterial.SetFloat("_FresnelStrength", 0.12f);

                img.material = _glassMaterial;
                img.color = Color.white;
            }

            RectTransform rt = bgObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.SetAsFirstSibling();

            // Calculate separator positions
            float sep1X = (contentMarginLeft + _section1Width + sectionSpacing / 2f) / _totalWidth;
            float sep2X = (contentMarginLeft + _section1Width + sectionSpacing + _section2Width + sectionSpacing / 4f * 0.5f) / _totalWidth;
            Vector4 separatorPositions = new Vector4(sep1X, sep2X, 0, 0);

            CreateGlowingBorder(bgObj.transform, edgePad, separatorPositions);
        }

        private void CreateGlowingBorder(Transform parent, float edgePad, Vector4 separatorPositions = default)
        {
            GameObject borderObj = new GameObject("GlowingBorder");
            borderObj.transform.SetParent(parent, false);

            RectTransform rt = borderObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.raycastTarget = false;
            borderImg.sprite = GetPixelSprite();

            float aspect = _totalWidth / frameHeight;

            Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
            if (glowShader != null)
            {
                _borderMaterial = new Material(glowShader);

                _borderMaterial.SetFloat("_StrokeEnabled", 0);
                _borderMaterial.SetFloat("_BorderWidth", 0.06f);
                _borderMaterial.SetFloat("_CornerRadius", 0.12f);
                _borderMaterial.SetFloat("_EdgePadding", edgePad);
                _borderMaterial.SetFloat("_Aspect", aspect);

                // Glow layers
                _borderMaterial.SetFloat("_Layer1Width", 0.03f);
                _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
                _borderMaterial.SetFloat("_Layer2Width", 0.06f);
                _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
                _borderMaterial.SetFloat("_Layer3Width", 0.1275f);
                _borderMaterial.SetFloat("_Layer3Alpha", 0.6f);
                _borderMaterial.SetFloat("_Layer4Width", 0.27f);
                _borderMaterial.SetFloat("_Layer4Alpha", 0.3f);

                _borderMaterial.SetColor("_ColorA", GetGlowColorA());
                _borderMaterial.SetColor("_ColorB", GetGlowColorB());
                _borderMaterial.SetFloat("_GradientMode", 2f);
                _borderMaterial.SetFloat("_GradientAngle", -10f);

                _borderMaterial.SetFloat("_GlassAlpha", 0.02f);
                _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));

                _borderMaterial.SetFloat("_ShimmerSpeed", 0.1f);
                _borderMaterial.SetFloat("_ShimmerIntensity", 0.2f);
                _borderMaterial.SetFloat("_LightSize", 0.008f);
                _borderMaterial.SetFloat("_LightGlow", 0.008f);

                // Separators
                int separatorCount = 0;
                if (separatorPositions.x > 0.01f) separatorCount++;
                if (separatorPositions.y > 0.01f) separatorCount++;
                if (separatorPositions.z > 0.01f) separatorCount++;
                if (separatorPositions.w > 0.01f) separatorCount++;

                if (separatorCount > 0)
                {
                    _borderMaterial.SetFloat("_SeparatorCount", separatorCount);
                    _borderMaterial.SetVector("_SeparatorPositions", separatorPositions);
                    _borderMaterial.SetFloat("_SeparatorWidth", 0.003f);
                    _borderMaterial.SetFloat("_SeparatorGlowWidth", 0.012f);
                    _borderMaterial.SetFloat("_SeparatorAlpha", 0.7f);
                }

                borderImg.material = _borderMaterial;
            }

            borderObj.transform.SetAsLastSibling();
        }

        private void CreateContentContainer(RectTransform parent)
        {
            GameObject contentObj = new GameObject("ContentContainer");
            contentObj.transform.SetParent(parent, false);

            _contentContainer = contentObj.AddComponent<RectTransform>();
            _contentContainer.anchorMin = Vector2.zero;
            _contentContainer.anchorMax = Vector2.one;
            _contentContainer.offsetMin = new Vector2(contentMarginLeft, contentMarginBottom);
            _contentContainer.offsetMax = new Vector2(-contentMarginRight, -contentMarginTop);
        }

        private void CreateSection1(float height)
        {
            GameObject section = new GameObject("Section1");
            section.transform.SetParent(_contentContainer, false);

            _section1Container = section.AddComponent<RectTransform>();
            _section1Container.anchorMin = new Vector2(0, 0);
            _section1Container.anchorMax = new Vector2(0, 1);
            _section1Container.pivot = new Vector2(0, 0.5f);
            _section1Container.sizeDelta = new Vector2(_section1Width, 0);
            _section1Container.anchoredPosition = Vector2.zero;

            HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = buttonSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(8, 8, 0, 0);
        }

        private void CreateSection2(float xPos, float height)
        {
            GameObject section = new GameObject("Section2");
            section.transform.SetParent(_contentContainer, false);

            _section2Container = section.AddComponent<RectTransform>();
            _section2Container.anchorMin = new Vector2(0, 0);
            _section2Container.anchorMax = new Vector2(0, 1);
            _section2Container.pivot = new Vector2(0, 0.5f);
            _section2Container.sizeDelta = new Vector2(_section2Width, 0);
            _section2Container.anchoredPosition = new Vector2(xPos, 0);

            HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = buttonSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(8, 8, 0, 0);
        }

        private void CreateSection3(float xPos, float height)
        {
            GameObject section = new GameObject("Section3_Status");
            section.transform.SetParent(_contentContainer, false);

            _section3Container = section.AddComponent<RectTransform>();
            _section3Container.anchorMin = new Vector2(0, 0);
            _section3Container.anchorMax = new Vector2(0, 1);
            _section3Container.pivot = new Vector2(0, 0.5f);
            _section3Container.sizeDelta = new Vector2(section3Width, 0);
            _section3Container.anchoredPosition = new Vector2(xPos, 0);

            // Upper section (2/3 height): Network + Battery
            GameObject upperSection = new GameObject("StatusGroup");
            upperSection.transform.SetParent(_section3Container, false);
            RectTransform upperRT = upperSection.AddComponent<RectTransform>();
            upperRT.anchorMin = new Vector2(0, 0.33f);
            upperRT.anchorMax = new Vector2(1, 1);
            upperRT.offsetMin = Vector2.zero;
            upperRT.offsetMax = Vector2.zero;

            HorizontalLayoutGroup upperLayout = upperSection.AddComponent<HorizontalLayoutGroup>();
            upperLayout.spacing = buttonSpacing * 2.5f;
            upperLayout.childAlignment = TextAnchor.MiddleCenter;
            upperLayout.childControlWidth = false;
            upperLayout.childControlHeight = false;
            upperLayout.childForceExpandWidth = false;
            upperLayout.childForceExpandHeight = false;

            // Network indicator
            CreateNetworkIndicator(upperSection.transform);

            // Battery indicator
            CreateBatteryIndicator(upperSection.transform);

            // Lower section (1/3 height): Clock
            GameObject lowerSection = new GameObject("ClockGroup");
            lowerSection.transform.SetParent(_section3Container, false);
            RectTransform lowerRT = lowerSection.AddComponent<RectTransform>();
            lowerRT.anchorMin = new Vector2(0, 0);
            lowerRT.anchorMax = new Vector2(1, 0.33f);
            lowerRT.offsetMin = Vector2.zero;
            lowerRT.offsetMax = Vector2.zero;

            // Clock display
            CreateClockDisplay(lowerSection.transform);
        }
        #endregion

        #region Status Indicators
        private void CreateNetworkIndicator(Transform parent)
        {
            float iconSize = buttonSize * 0.6f;

            GameObject container = new GameObject("NetworkContainer");
            container.transform.SetParent(parent, false);

            RectTransform containerRT = container.AddComponent<RectTransform>();
            containerRT.sizeDelta = new Vector2(iconSize, iconSize);

            _networkElement = container;

            // Create based on mode
            UpdateNetworkDisplay();
        }

        private void UpdateNetworkDisplay()
        {
            if (_networkElement == null) return;

            _lastNetworkReachability = (NetworkReachability)(-1);

            // Clear existing
            for (int i = _networkElement.transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(_networkElement.transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(_networkElement.transform.GetChild(i).gameObject);
            }
            _networkIcon = null;
            _networkText = null;
            _networkButton = null;

            float iconSize = buttonSize * 0.6f;

            switch (wifiMode)
            {
                case WifiDisplayMode.Icon:
                    CreateNetworkIcon(_networkElement.transform, iconSize);
                    break;
                case WifiDisplayMode.Text:
                    CreateNetworkText(_networkElement.transform);
                    break;
                case WifiDisplayMode.Button:
                    CreateNetworkButton(_networkElement.transform, iconSize);
                    break;
                case WifiDisplayMode.None:
                    break;
            }

            MarkDirty();
        }

        private void CreateNetworkIcon(Transform parent, float iconSize)
        {
            GameObject iconObj = new GameObject("NetworkIcon");
            iconObj.transform.SetParent(parent, false);

            _networkIcon = iconObj.AddComponent<Image>();
            _networkIcon.sprite = iconWifi ?? GetWifiSprite();
            _networkIcon.preserveAspect = true;
            _networkIcon.raycastTarget = false;
            _networkIcon.color = Color.Lerp(glowColorA, Color.white, 0.9f);

            RectTransform iconRT = iconObj.GetComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.sizeDelta = Vector2.zero;

            // Glow effects
            Color glowCol = Color.Lerp(glowColorA, Color.white, 0.7f);
            glowCol.a = 0.4f;
            Shadow shadow1 = iconObj.AddComponent<Shadow>();
            shadow1.effectColor = glowCol;
            shadow1.effectDistance = new Vector2(2f, -2f);
            Shadow shadow2 = iconObj.AddComponent<Shadow>();
            shadow2.effectColor = glowCol;
            shadow2.effectDistance = new Vector2(-2f, 2f);

            Color bloomCol = Color.Lerp(glowColorA, Color.white, 0.8f);
            bloomCol.a = 0.15f;
            Shadow shadow3 = iconObj.AddComponent<Shadow>();
            shadow3.effectColor = bloomCol;
            shadow3.effectDistance = new Vector2(5f, -5f);
            Shadow shadow4 = iconObj.AddComponent<Shadow>();
            shadow4.effectColor = bloomCol;
            shadow4.effectDistance = new Vector2(-5f, 5f);
        }

        private void CreateNetworkText(Transform parent)
        {
            GameObject textObj = new GameObject("NetworkText");
            textObj.transform.SetParent(parent, false);

            _networkText = textObj.AddComponent<TextMeshProUGUI>();
            _networkText.text = "WiFi";
            _networkText.fontSize = 20f;
            _networkText.fontStyle = FontStyles.Bold;
            _networkText.alignment = TextAlignmentOptions.Center;
            _networkText.color = Color.white;
            _networkText.raycastTarget = false;
            if (customFont != null) _networkText.font = customFont;

            RectTransform textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
        }

        private void CreateNetworkButton(Transform parent, float iconSize)
        {
            GameObject btnObj = new GameObject("NetworkButton");
            btnObj.transform.SetParent(parent, false);

            Image btnImg = btnObj.AddComponent<Image>();
            btnImg.sprite = iconWifi ?? GetWifiSprite();
            btnImg.preserveAspect = true;
            btnImg.color = Color.Lerp(glowColorA, Color.white, 0.9f);

            _networkButton = btnObj.AddComponent<Button>();

            RectTransform btnRT = btnObj.GetComponent<RectTransform>();
            btnRT.anchorMin = Vector2.zero;
            btnRT.anchorMax = Vector2.one;
            btnRT.sizeDelta = Vector2.zero;

            _networkIcon = btnImg;
        }

        private void CreateBatteryIndicator(Transform parent)
        {
            float battHeight = 40f;
            float battWidth = battHeight * 2f;

            GameObject container = new GameObject("BatteryContainer");
            container.transform.SetParent(parent, false);

            RectTransform rt = container.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(battWidth, battHeight);

            Sprite batSprite = GetBatterySprite();
            Sprite fillSprite = GetBatteryFillSprite();

            // Background
            GameObject bgObj = new GameObject("Bg");
            bgObj.transform.SetParent(container.transform, false);

            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.sprite = batSprite;
            bgImg.color = new Color(0.8f, 0.8f, 0.8f, 0.5f);
            bgImg.preserveAspect = true;
            bgImg.raycastTarget = false;

            RectTransform bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.sizeDelta = Vector2.zero;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Fill
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(container.transform, false);

            _batteryFillImage = fillObj.AddComponent<Image>();
            _batteryFillImage.sprite = fillSprite;
            _batteryFillImage.type = Image.Type.Filled;
            _batteryFillImage.fillMethod = Image.FillMethod.Horizontal;
            _batteryFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _batteryFillImage.fillAmount = 1f;
            _batteryFillImage.color = Color.white;
            _batteryFillImage.preserveAspect = true;
            _batteryFillImage.raycastTarget = false;

            RectTransform fillRT = fillObj.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.sizeDelta = Vector2.zero;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            // Shadow
            Color battShadowCol = new Color(0f, 0f, 0f, 0.15f);
            Shadow battShadow1 = fillObj.AddComponent<Shadow>();
            battShadow1.effectColor = battShadowCol;
            battShadow1.effectDistance = new Vector2(2f, -2f);
            Shadow battShadow2 = fillObj.AddComponent<Shadow>();
            battShadow2.effectColor = battShadowCol;
            battShadow2.effectDistance = new Vector2(-2f, 2f);

            // Battery text
            GameObject textObj = new GameObject("BatteryText");
            textObj.transform.SetParent(container.transform, false);

            _batteryText = textObj.AddComponent<TextMeshProUGUI>();
            _batteryText.text = "100";
            _batteryText.fontSize = 24f;
            _batteryText.fontStyle = FontStyles.Bold;
            _batteryText.alignment = TextAlignmentOptions.Center;
            _batteryText.color = Color.black;
            _batteryText.raycastTarget = false;
            if (customFont != null) _batteryText.font = customFont;

            // White shadow
            Color txtShadowCol = new Color(1f, 1f, 1f, 0.15f);
            Shadow txtShadow1 = textObj.AddComponent<Shadow>();
            txtShadow1.effectColor = txtShadowCol;
            txtShadow1.effectDistance = new Vector2(2f, -2f);
            Shadow txtShadow2 = textObj.AddComponent<Shadow>();
            txtShadow2.effectColor = txtShadowCol;
            txtShadow2.effectDistance = new Vector2(-2f, 2f);

            RectTransform textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            textRT.offsetMin = new Vector2(0, 0);
            textRT.offsetMax = new Vector2(-5f, 0);
        }

        private void CreateClockDisplay(Transform parent)
        {
            GameObject textObj = new GameObject("ClockText");
            textObj.transform.SetParent(parent, false);

            _clockText = textObj.AddComponent<TextMeshProUGUI>();
            _clockText.text = DateTime.Now.ToString("HH:mm");
            _clockText.fontSize = 32f;
            _clockText.fontStyle = FontStyles.Bold;
            _clockText.alignment = TextAlignmentOptions.Center;
            _clockText.color = Color.white;
            _clockText.raycastTarget = false;
            if (customFont != null) _clockText.font = customFont;

            RectTransform textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            textRT.anchoredPosition = new Vector2(0, 7.5f);
        }
        #endregion

        #region Status Updates
        private void UpdateClock()
        {
            if (_clockText == null) return;

            DateTime now = DateTime.Now;
            int minute = now.Hour * 60 + now.Minute;
            if (_lastClockMinute != minute)
            {
                _lastClockMinute = minute;
                _clockText.text = now.ToString("HH:mm");
                MarkDirty();
            }
        }

        private void UpdateBattery()
        {
            if (_batteryText == null) return;

            float battLevel = SystemInfo.batteryLevel;
            float displayLevel = (battLevel < 0) ? 1.0f : battLevel;
            int batteryPercent = Mathf.FloorToInt(displayLevel * 100);
            if (_lastBatteryPercent != batteryPercent)
            {
                _lastBatteryPercent = batteryPercent;
                _batteryText.text = batteryPercent.ToString();

                if (_batteryFillImage != null)
                {
                    _batteryFillImage.fillAmount = displayLevel;
                }
                MarkDirty();
            }
        }

        private void UpdateNetwork()
        {
            NetworkReachability reachability = Application.internetReachability;
            if (_lastNetworkReachability == reachability) return;

            _lastNetworkReachability = reachability;
            bool hasNetwork = reachability != NetworkReachability.NotReachable;

            if (_networkIcon != null)
            {
                Color iconColor = Color.Lerp(glowColorA, Color.white, 0.9f);
                _networkIcon.color = hasNetwork ? iconColor : new Color(iconColor.r, iconColor.g, iconColor.b, 0.3f);
            }

            if (_networkText != null)
            {
                _networkText.text = hasNetwork ? "WiFi" : "No WiFi";
            }

            MarkDirty();
        }
        #endregion

        #region Position Tracking
        /// <summary>
        /// Set the target transform to follow.
        /// RTTToolbar will use this to determine positioning.
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            followTarget = target;

            // Notify RTTToolbar if it exists
            if (RTTToolbar.Instance != null)
            {
                RTTToolbar.Instance.UpdateFollowTarget();
            }
        }

        /// <summary>
        /// Get the current follow target.
        /// </summary>
        public Transform GetFollowTarget() => followTarget;
        #endregion

        #region Theme Support
        protected override void ApplyCurrentTheme()
        {
            var theme = GetTheme();
            if (theme == null) return;

            if (_glassMaterial != null)
            {
                _glassMaterial.SetColor("_ColorA", theme.glassColorA);
                _glassMaterial.SetColor("_ColorB", theme.glassColorB);
                _glassMaterial.SetFloat("_GlassAlpha", theme.glassAlpha);
            }

            if (_borderMaterial != null)
            {
                _borderMaterial.SetColor("_ColorA", theme.glowColorA);
                _borderMaterial.SetColor("_ColorB", theme.glowColorB);
            }

            MarkDirty();
        }

        private Color GetGlassColorA()
        {
            var theme = GetTheme();
            return theme?.glassColorA ?? new Color(0.0f, 0.55f, 0.65f, 0.35f);
        }

        private Color GetGlassColorB()
        {
            var theme = GetTheme();
            return theme?.glassColorB ?? new Color(0.30f, 0.12f, 0.50f, 0.32f);
        }

        private Color GetGlowColorA()
        {
            var theme = GetTheme();
            return theme?.glowColorA ?? glowColorA;
        }

        private Color GetGlowColorB()
        {
            var theme = GetTheme();
            return theme?.glowColorB ?? glowColorB;
        }
        #endregion

        #region Sprite Helpers
        private Sprite GetPixelSprite()
        {
            if (_pixelSprite != null) return _pixelSprite;

            Texture2D tex = new Texture2D(2, 2);
            tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            return _pixelSprite;
        }

        private Sprite GetBatterySprite()
        {
            if (_batterySprite != null) return _batterySprite;

            int w = 64, h = 32;
            int radius = 4;
            int tipWidth = 4;
            int tipHeight = 12;

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] colors = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float alpha = 0f;

                    if (x < w - tipWidth)
                    {
                        int bx = x, by = y;
                        bool inCorner = false;
                        int cx = 0, cy = 0;

                        if (bx < radius && by < radius) { inCorner = true; cx = radius; cy = radius; }
                        else if (bx >= w - tipWidth - radius && by < radius) { inCorner = true; cx = w - tipWidth - radius - 1; cy = radius; }
                        else if (bx < radius && by >= h - radius) { inCorner = true; cx = radius; cy = h - radius - 1; }
                        else if (bx >= w - tipWidth - radius && by >= h - radius) { inCorner = true; cx = w - tipWidth - radius - 1; cy = h - radius - 1; }

                        if (inCorner)
                        {
                            float dist = Vector2.Distance(new Vector2(bx, by), new Vector2(cx, cy));
                            alpha = Mathf.Clamp01(radius + 0.5f - dist);
                        }
                        else if (bx >= 0 && bx < w - tipWidth && by >= 0 && by < h)
                        {
                            alpha = 1f;
                        }
                    }

                    int tipStartX = w - tipWidth;
                    int tipStartY = (h - tipHeight) / 2;
                    if (x >= tipStartX && y >= tipStartY && y < tipStartY + tipHeight)
                    {
                        alpha = 1f;
                    }

                    colors[y * w + x] = new Color(1, 1, 1, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            _batterySprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.one * 0.5f, 100, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius + tipWidth, radius));
            return _batterySprite;
        }

        private Sprite GetBatteryFillSprite()
        {
            if (_batteryFillSprite != null) return _batteryFillSprite;

            int w = 64, h = 32;
            int radius = 4;
            int tipWidth = 4;

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] colors = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float alpha = 0f;

                    int bodyWidth = w - tipWidth;
                    if (x < bodyWidth)
                    {
                        int bx = x, by = y;
                        bool inCorner = false;
                        int cx = 0, cy = 0;

                        if (bx < radius && by < radius) { inCorner = true; cx = radius; cy = radius; }
                        else if (bx >= bodyWidth - radius && by < radius) { inCorner = true; cx = bodyWidth - radius - 1; cy = radius; }
                        else if (bx < radius && by >= h - radius) { inCorner = true; cx = radius; cy = h - radius - 1; }
                        else if (bx >= bodyWidth - radius && by >= h - radius) { inCorner = true; cx = bodyWidth - radius - 1; cy = h - radius - 1; }

                        if (inCorner)
                        {
                            float dist = Vector2.Distance(new Vector2(bx, by), new Vector2(cx, cy));
                            alpha = Mathf.Clamp01(radius + 0.5f - dist);
                        }
                        else if (bx >= 0 && bx < bodyWidth && by >= 0 && by < h)
                        {
                            alpha = 1f;
                        }
                    }

                    colors[y * w + x] = new Color(1, 1, 1, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            _batteryFillSprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.one * 0.5f, 100, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            return _batteryFillSprite;
        }

        private Sprite GetWifiSprite()
        {
            if (_wifiSprite != null) return _wifiSprite;

            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] colors = new Color[size * size];

            Vector2 center = new Vector2(size / 2f, 8);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = 0f;

                    float[] radii = { 16, 28, 42 };
                    float arcWidth = 6f;

                    foreach (float r in radii)
                    {
                        if (dist >= r - arcWidth / 2 && dist <= r + arcWidth / 2)
                        {
                            Vector2 dir = new Vector2(x - center.x, y - center.y).normalized;
                            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                            if (angle >= 30 && angle <= 150)
                            {
                                alpha = Mathf.Clamp01(1f - Mathf.Abs(dist - r) / (arcWidth / 2));
                            }
                        }
                    }

                    if (dist < 5)
                    {
                        alpha = 1f;
                    }

                    colors[y * size + x] = new Color(1, 1, 1, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            _wifiSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            return _wifiSprite;
        }
        #endregion
    }

}
