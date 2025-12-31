using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// RTT-based Taskbar with glass effect.
/// Follows the primary RTTMenuFrame and provides control buttons, app slots, and status display.
/// Migrated from VRTaskbar to use Render-to-Texture approach.
/// </summary>
public class RTTTaskbar : RTTCanvasBase
{
    #region Static Instance
    private static RTTTaskbar _instance;
    public static RTTTaskbar Instance => _instance;
    #endregion

    #region Configuration
    [Header("Logical Size (pixels)")]
    [SerializeField] private float logicalWidth = 1060f;
    [SerializeField] private float logicalHeight = 128f;

    [Header("Glowing Border")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);
    [Range(0f, 0.1f)]
    [SerializeField] private float glowExpansion = 0.02f;
#pragma warning disable 0414 // Reserved for shader customization
    [SerializeField] private float shimmerSpeed = 0.4f;
#pragma warning restore 0414

    [Header("Content Margin (pixels)")]
    [SerializeField] private float contentMarginLeft = 0f;
    [SerializeField] private float contentMarginRight = 10f;
    [SerializeField] private float contentMarginTop = 10f;
    [SerializeField] private float contentMarginBottom = 10f;

    [Header("Section Layout")]
    [SerializeField] private float section1Width = 400f;
    [SerializeField] private float section2Width = 398f;
    [SerializeField] private float section3Width = 206f;
    [SerializeField] private float sectionSpacing = 37f;
    [SerializeField] private float buttonSize = 90f;
    [SerializeField] private float buttonSpacing = 12f;
    [SerializeField] private int maxAppButtons = 4;

    [Header("Style Resources")]
    [SerializeField] private TMP_FontAsset customFont;
    [SerializeField] private Sprite iconQuit;
    [SerializeField] private Sprite iconSettings;
    [SerializeField] private Sprite iconPassthrough;
    [SerializeField] private Sprite iconRecenter;
    [SerializeField] private Sprite iconHome;
    [SerializeField] private Sprite iconWifi;
    [SerializeField] private Sprite iconBattery;

    [Header("Position Tracking")]
    [SerializeField] private bool followPrimaryFrame = true;
    [SerializeField] private float spacingMultiplier = 1.5f;
    [SerializeField] private bool faceOnInit = true;
    #endregion

    #region Private Fields
    // Scale factor matching VRMenuFrame
    private const float PixelToMeter = 1.6f / 1920f;

    private RectTransform _contentContainer;
    private Material _glassMaterial;
    private Material _borderMaterial;
    private Sprite _pixelSprite;
    private Sprite _batterySprite;
    private Sprite _batteryFillSprite;  // Separate fill sprite without tip
    private Sprite _wifiSprite;

    // Status references
    private TextMeshProUGUI _clockText;
    private TextMeshProUGUI _batteryText;
    private Image _networkIcon;
    private Image _batteryFillImage;

    // Button references
    private GameObject _passthroughButton;
    private List<GameObject> _appButtons = new List<GameObject>();
    private int _activeAppButtonIndex = 0;
    private Dictionary<int, Action> _appSlotCallbacks = new Dictionary<int, Action>();

    // State
    private bool _isPassthroughOn = false;
    private bool _hasInitializedOrientation = false;

    // Calculated values
    private float Aspect => logicalWidth / logicalHeight;
    #endregion

    #region Lifecycle
    protected override void Awake()
    {
        // Calculate world size from logical size
        worldWidth = logicalWidth * PixelToMeter;
        worldHeight = logicalHeight * PixelToMeter;

        // Load icons BEFORE base.Awake() so they're available when BuildUI() is called
        LoadIcons();

        base.Awake();
    }

    protected override void Start()
    {
        base.Start();

        _instance = this;

        // Sync passthrough button state with ModeController
        SyncPassthroughWithModeController();

        // Note: OrientTowardsCamera is called in LateUpdate after position is set
    }

    /// <summary>
    /// Sync passthrough button state with ModeController on initialization.
    /// </summary>
    private void SyncPassthroughWithModeController()
    {
        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            _isPassthroughOn = modeController.mode == ViewMode.RealWorld;
            UpdatePassthroughButtonColor();
        }
    }

    protected override void OnDestroy()
    {
        if (_glassMaterial != null) Destroy(_glassMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);

        if (_instance == this) _instance = null;

        base.OnDestroy();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();

        // Update position relative to primary
        UpdatePositionRelativeToPrimary();

        // Orient towards camera AFTER position is set (only once on init)
        // Only orient when position has been established (primary exists or not following primary)
        if (faceOnInit && !_hasInitializedOrientation)
        {
            bool positionReady = !followPrimaryFrame || RTTMenuFrame.PrimaryInstance != null;
            if (positionReady)
            {
                OrientTowardsCamera();
            }
        }

        // Update status displays
        UpdateClock();
        UpdateBattery();
        UpdateNetwork();
    }
    #endregion

    #region RTTCanvasBase Overrides
    protected override Vector2Int GetResolution()
    {
        return new Vector2Int(
            Mathf.RoundToInt(logicalWidth),
            Mathf.RoundToInt(logicalHeight)
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
        float w = logicalWidth;
        float h = logicalHeight;

        // 1. Glass Background
        CreateGlassPanel(canvasRect, w, h);

        // 2. Content Container
        CreateContentContainer(canvasRect);

        // 3. Three sections
        float contentHeight = h - contentMarginTop - contentMarginBottom;
        CreateSection1(_contentContainer, contentHeight);

        float section2XPos = section1Width + sectionSpacing;
        CreateSection2(_contentContainer, section2XPos, section2Width, contentHeight);

        float section3XPos = section2XPos + section2Width + sectionSpacing / 2;
        CreateSection3(_contentContainer, section3XPos, contentHeight);

        Debug.Log($"[RTTTaskbar] UI built: {w}x{h} logical pixels");
    }
    #endregion

    #region UI Building
    private void CreateGlassPanel(RectTransform parent, float w, float h)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);

        Image img = bgObj.AddComponent<Image>();
        img.type = Image.Type.Simple;
        img.sprite = GetPixelSprite();
        img.raycastTarget = true;

        float expansion = glowExpansion;
        // Main container fills entire canvas - no edge padding needed
        float edgePad = 0.06f;
        float aspect = Aspect;

        // Use Wide shader for very wide aspect ratios (taskbar is ~8:1)
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            _glassMaterial = new Material(glassShader);

            _glassMaterial.SetFloat("_CornerRadius", 0.12f);  // Match Space key for consistency
            _glassMaterial.SetFloat("_EdgePadding", edgePad);
            _glassMaterial.SetFloat("_Aspect", aspect);

            // Use theme colors with fallback
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
        // Keep background within canvas bounds - edge padding in shader handles visual margin
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.SetAsFirstSibling();

        // Calculate separator positions in UV space (0-1) - matching VRTaskbar
        float sep1X = (contentMarginLeft + section1Width + sectionSpacing / 2f) / w;
        float sep2X = (contentMarginLeft + section1Width + sectionSpacing + section2Width + sectionSpacing / 4f * 0.5f) / w;
        Vector4 separatorPositions = new Vector4(sep1X, sep2X, 0, 0);

        CreateGlowingBorder(bgObj.transform, w, h, edgePad, separatorPositions);
    }

    private void CreateGlowingBorder(Transform parent, float w, float h, float edgePad, Vector4 separatorPositions = default)
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

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);

            _borderMaterial.SetFloat("_StrokeEnabled", 0);

            // Border settings - corner radius matches background for alignment
            _borderMaterial.SetFloat("_BorderWidth", 0.06f);
            _borderMaterial.SetFloat("_CornerRadius", 0.12f);  // Match background corner radius
            _borderMaterial.SetFloat("_EdgePadding", edgePad);
            _borderMaterial.SetFloat("_Aspect", Aspect);

            // Glow layer widths matching VRTaskbar
            _borderMaterial.SetFloat("_Layer1Width", 0.03f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
            _borderMaterial.SetFloat("_Layer2Width", 0.06f);
            _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
            _borderMaterial.SetFloat("_Layer3Width", 0.1275f);
            _borderMaterial.SetFloat("_Layer3Alpha", 0.6f);
            _borderMaterial.SetFloat("_Layer4Width", 0.27f);
            _borderMaterial.SetFloat("_Layer4Alpha", 0.3f);

            // Use theme colors with fallback
            _borderMaterial.SetColor("_ColorA", GetGlowColorA());
            _borderMaterial.SetColor("_ColorB", GetGlowColorB());
            _borderMaterial.SetFloat("_GradientMode", 2f);
            _borderMaterial.SetFloat("_GradientAngle", -10f);

            // Glass effect matching VRTaskbar
            _borderMaterial.SetFloat("_GlassAlpha", 0.02f);
            _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));

            // Shimmer and light effects matching VRTaskbar
            _borderMaterial.SetFloat("_ShimmerSpeed", 0.1f);
            _borderMaterial.SetFloat("_ShimmerIntensity", 0.2f);
            _borderMaterial.SetFloat("_LightSize", 0.008f);
            _borderMaterial.SetFloat("_LightGlow", 0.008f);

            // Separator settings (vertical lines between sections)
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

    private void CreateSection1(RectTransform parent, float height)
    {
        GameObject section = new GameObject("Section1_Left");
        section.transform.SetParent(parent, false);

        RectTransform rt = section.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.sizeDelta = new Vector2(section1Width, 0);
        rt.anchoredPosition = Vector2.zero;

        HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = buttonSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(8, 8, 0, 0);

        Color cyanColor = new Color(0f, 0.9f, 1f);

        // Quit
        CreateIconButton(section.transform, iconQuit, "Quit", cyanColor, () =>
        {
            Debug.Log("[RTTTaskbar] Quit clicked");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });

        // Settings
        CreateIconButton(section.transform, iconSettings, "Settings", cyanColor, () =>
        {
            Debug.Log("[RTTTaskbar] Settings clicked");
        });

        // Passthrough
        _passthroughButton = CreateIconButton(section.transform, iconPassthrough, "Passthrough", cyanColor, TogglePassthrough);

        // Recenter
        CreateIconButton(section.transform, iconRecenter, "Recenter", cyanColor, RecenterObject);
    }

    private void CreateSection2(RectTransform parent, float xPos, float width, float height)
    {
        GameObject section = new GameObject("Section2_Apps");
        section.transform.SetParent(parent, false);

        RectTransform rt = section.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.sizeDelta = new Vector2(width, 0);
        rt.anchoredPosition = new Vector2(xPos, 0);

        HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = buttonSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(8, 8, 0, 0);

        _appButtons.Clear();
        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);

        for (int i = 0; i < maxAppButtons; i++)
        {
            Sprite slotIcon = (i == 0) ? iconHome : null;
            string slotName = (i == 0) ? "Home" : $"AppSlot_{i}";
            bool isActive = (i == _activeAppButtonIndex);
            Color btnColor = isActive ? purpleColor : cyanColor;
            int capturedIndex = i;

            var btn = CreateAppButton(section.transform, slotIcon, slotName, btnColor, capturedIndex);
            _appButtons.Add(btn);

            if (i == 0)
            {
                // Home button: Wire to HandleHomeClick for RTTManager integration
                RewireHomeButton(btn);
            }
            else
            {
                // App slots: Hidden by default, will be shown when RegisterApp() is called
                var cg = btn.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 0f;
            }
        }
    }

    private void CreateSection3(RectTransform parent, float xPos, float height)
    {
        GameObject section = new GameObject("Section3_Status");
        section.transform.SetParent(parent, false);

        RectTransform rt = section.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.sizeDelta = new Vector2(section3Width, 0);
        rt.anchoredPosition = new Vector2(xPos, 0);

        // Upper section (2/3 height): Network + Battery
        GameObject upperSection = new GameObject("StatusGroup");
        upperSection.transform.SetParent(section.transform, false);
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

        // Network icon in upper section
        CreateNetworkIndicator(upperSection.transform);

        // Battery in upper section
        CreateBatteryIndicator(upperSection.transform);

        // Lower section (1/3 height): Clock
        GameObject lowerSection = new GameObject("ClockGroup");
        lowerSection.transform.SetParent(section.transform, false);
        RectTransform lowerRT = lowerSection.AddComponent<RectTransform>();
        lowerRT.anchorMin = new Vector2(0, 0);
        lowerRT.anchorMax = new Vector2(1, 0.33f);
        lowerRT.offsetMin = Vector2.zero;
        lowerRT.offsetMax = Vector2.zero;

        // Clock in lower section
        CreateClockDisplay(lowerSection.transform);
    }
    #endregion

    #region Button Creation
    private GameObject CreateIconButton(Transform parent, Sprite icon, string name, Color glowColor, Action onClick)
    {
        var btn = VRButtonFactory.CreateBareIconButton(
            parent, buttonSize, icon, glowColor,
            () =>
            {
                onClick?.Invoke();
                MarkDirty();
            },
            0.05f, 0.6f
        );

        // Rename for clarity
        btn.name = $"Btn_{name}";

        // Fix RTT raycast: Set button layer to UI to avoid collision with RTTRaycastManager
        // RTTRaycastManager raycasts VirtualObjects layer, so buttons need to be on different layer
        SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

        return btn;
    }

    /// <summary>
    /// Recursively set layer for all children (needed for RTT to avoid raycast conflicts)
    /// </summary>
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer == -1) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private GameObject CreateAppButton(Transform parent, Sprite icon, string name, Color glowColor, int index)
    {
        GameObject btn = new GameObject($"AppBtn_{name}");
        btn.transform.SetParent(parent, false);

        RectTransform rt = btn.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);

        CanvasGroup cg = btn.AddComponent<CanvasGroup>();

        if (icon != null)
        {
            var iconBtn = VRButtonFactory.CreateBareIconButton(
                btn.transform, buttonSize, icon, glowColor,
                () =>
                {
                    SelectAppButton(index);
                    MarkDirty();
                },
                0.05f, 0.6f
            );
            iconBtn.transform.SetAsFirstSibling();

            // Fix RTT raycast: Set button layer to UI
            SetLayerRecursively(iconBtn, LayerMask.NameToLayer("UI"));
        }

        // Also set parent container to UI layer
        SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

        return btn;
    }
    #endregion

    #region Status Indicators
    private void CreateNetworkIndicator(Transform parent)
    {
        float iconSize = buttonSize * 0.6f;

        GameObject iconObj = new GameObject("NetworkIcon");
        iconObj.transform.SetParent(parent, false);

        _networkIcon = iconObj.AddComponent<Image>();
        _networkIcon.sprite = iconWifi ?? GetWifiSprite();
        _networkIcon.preserveAspect = true;
        _networkIcon.raycastTarget = false;
        _networkIcon.color = Color.Lerp(glowColorA, Color.white, 0.9f);

        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.sizeDelta = new Vector2(iconSize, iconSize);

        // Glow effects for NetworkIcon (matching VRTaskbar style)
        Color netGlowCol = Color.Lerp(glowColorA, Color.white, 0.7f);
        netGlowCol.a = 0.4f;
        Shadow netShadow1 = iconObj.AddComponent<Shadow>();
        netShadow1.effectColor = netGlowCol;
        netShadow1.effectDistance = new Vector2(2f, -2f);
        Shadow netShadow2 = iconObj.AddComponent<Shadow>();
        netShadow2.effectColor = netGlowCol;
        netShadow2.effectDistance = new Vector2(-2f, 2f);

        Color netBloomCol = Color.Lerp(glowColorA, Color.white, 0.8f);
        netBloomCol.a = 0.15f;
        Shadow netShadow3 = iconObj.AddComponent<Shadow>();
        netShadow3.effectColor = netBloomCol;
        netShadow3.effectDistance = new Vector2(5f, -5f);
        Shadow netShadow4 = iconObj.AddComponent<Shadow>();
        netShadow4.effectColor = netBloomCol;
        netShadow4.effectDistance = new Vector2(-5f, 5f);
    }

    private void CreateBatteryIndicator(Transform parent)
    {
        // Battery dimensions (matching VRTaskbar: height * 2 for width)
        float battHeight = 40f;
        float battWidth = battHeight * 2f;

        GameObject container = new GameObject("BatteryContainer");
        container.transform.SetParent(parent, false);

        RectTransform rt = container.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(battWidth, battHeight);

        Sprite batSprite = GetBatterySprite();
        Sprite fillSprite = GetBatteryFillSprite();

        // Battery icon background (with tip)
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

        // Battery fill (without tip - uses separate sprite)
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(container.transform, false);

        _batteryFillImage = fillObj.AddComponent<Image>();
        _batteryFillImage.sprite = fillSprite;  // Use fill-only sprite (no tip)
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

        // Black shadow for Battery Fill
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

        // White shadow for Battery Text
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
        // Clock text directly in parent (fills the lower section)
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
        if (_clockText != null)
        {
            string newTime = DateTime.Now.ToString("HH:mm");
            if (_clockText.text != newTime)
            {
                _clockText.text = newTime;
                MarkDirty();
            }
        }
    }

    private void UpdateBattery()
    {
        if (_batteryText != null)
        {
            float battLevel = SystemInfo.batteryLevel;
            float displayLevel = (battLevel < 0) ? 1.0f : battLevel;
            string battStr = Mathf.FloorToInt(displayLevel * 100).ToString();

            if (_batteryText.text != battStr)
            {
                _batteryText.text = battStr;

                if (_batteryFillImage != null)
                {
                    _batteryFillImage.fillAmount = displayLevel;
                }
                MarkDirty();
            }
        }
    }

    private void UpdateNetwork()
    {
        if (_networkIcon != null)
        {
            Color iconColor = Color.Lerp(glowColorA, Color.white, 0.9f);
            bool hasNetwork = Application.internetReachability != NetworkReachability.NotReachable;
            _networkIcon.color = hasNetwork ? iconColor : new Color(iconColor.r, iconColor.g, iconColor.b, 0.3f);
        }
    }
    #endregion

    #region Position Tracking
    private void UpdatePositionRelativeToPrimary()
    {
        if (!followPrimaryFrame) return;

        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null) return;

        Vector3 primaryPos = primary.transform.position;
        float primaryHalfHeight = primary.PanelHeight / 2f;
        float taskbarPhysicalHeight = logicalHeight * PixelToMeter;

        Vector3 newPos = transform.position;
        newPos.x = primaryPos.x;
        newPos.y = primaryPos.y - primaryHalfHeight - (taskbarPhysicalHeight * spacingMultiplier);
        newPos.z = primaryPos.z;

        transform.position = newPos;

        if (!faceOnInit)
        {
            transform.rotation = primary.transform.rotation;
        }
    }

    private void OrientTowardsCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 toCamera = cam.transform.position - transform.position;
        if (toCamera.sqrMagnitude < 1e-6f) return;

        transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        _hasInitializedOrientation = true;
    }
    #endregion

    #region Button Actions
    private void TogglePassthrough()
    {
        _isPassthroughOn = !_isPassthroughOn;
        Debug.Log($"[RTTTaskbar] Passthrough: {(_isPassthroughOn ? "ON" : "OFF")}");

        // Update button visual color
        UpdatePassthroughButtonColor();

        // Find and set mode using ModeController
        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
        }

        MarkDirty();
    }

    /// <summary>
    /// Update passthrough button icon color based on current state.
    /// ON = Purple, OFF = Cyan
    /// </summary>
    private void UpdatePassthroughButtonColor()
    {
        if (_passthroughButton == null) return;

        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);
        Color targetColor = _isPassthroughOn ? purpleColor : cyanColor;

        // Find Icon image in the button hierarchy (VRButtonFactory creates HitArea/Visuals/Content/Icon)
        Transform iconTransform = _passthroughButton.transform.Find("HitArea/Visuals/Content/Icon");
        if (iconTransform != null)
        {
            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.color = Color.Lerp(targetColor, Color.white, 0.9f);

                // Update shadow glow colors
                Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                if (shadows.Length >= 4)
                {
                    // Layer 1 - Sharp inner halo
                    Color glowCol = Color.Lerp(targetColor, Color.white, 0.7f);
                    glowCol.a = 0.4f;
                    shadows[0].effectColor = glowCol;
                    shadows[1].effectColor = glowCol;

                    // Layer 2 - Soft outer bloom
                    Color bloomCol = Color.Lerp(targetColor, Color.white, 0.8f);
                    bloomCol.a = 0.15f;
                    shadows[2].effectColor = bloomCol;
                    shadows[3].effectColor = bloomCol;
                }
            }
        }
    }

    /// <summary>
    /// Set passthrough state programmatically (for external control).
    /// </summary>
    public void SetPassthrough(bool isOn)
    {
        if (_isPassthroughOn != isOn)
        {
            _isPassthroughOn = isOn;
            UpdatePassthroughButtonColor();

            var modeController = FindObjectOfType<ModeController>();
            if (modeController != null)
            {
                modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
            }

            MarkDirty();
            Debug.Log($"[RTTTaskbar] Passthrough set to {(_isPassthroughOn ? "ON" : "OFF")}");
        }
    }

    /// <summary>
    /// Recenter the primary RTTMenuFrame and all objects in VirtualObjects to face the camera.
    /// Shows visual feedback via VRGazeReticle during the recenter process (2-second countdown).
    /// </summary>
    private void RecenterObject()
    {
        Debug.Log("[RTTTaskbar] Recenter clicked");
        StartCoroutine(RecenterRoutine());
    }

    private System.Collections.IEnumerator RecenterRoutine()
    {
        // Get VRGazeReticle for visual feedback
        VRGazeReticle reticle = VRGazeReticle.Instance;
        if (reticle == null) reticle = FindObjectOfType<VRGazeReticle>();

        // Enter recenter mode with icon
        if (reticle != null)
        {
            reticle.EnterRecenterMode(iconRecenter);
        }

        // 2-second progress animation
        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            if (reticle != null)
            {
                reticle.UpdateRecenterProgress(progress);
            }

            yield return null;
        }

        // Perform the actual recenter
        Camera cam = Camera.main;
        if (cam != null)
        {
            RecenterAllVirtualObjects(cam);
        }

        // Exit recenter mode
        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }

        MarkDirty();
        Debug.Log("[RTTTaskbar] Recenter complete.");
    }

    /// <summary>
    /// Recenter all objects in VirtualObjects parent.
    /// Uses the primary RTTMenuFrame as the pivot point.
    /// All other objects maintain their relative positions and rotations.
    /// </summary>
    private void RecenterAllVirtualObjects(Camera cam)
    {
        // Find VirtualObjects parent
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
        if (virtualObjectsParent == null)
        {
            Debug.LogWarning("[RTTTaskbar] VirtualObjects parent not found, falling back to primary only");
            RecenterPrimaryOnly(cam);
            return;
        }

        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null)
        {
            Debug.LogWarning("[RTTTaskbar] No primary RTTMenuFrame found");
            return;
        }

        // Store pivot point (primary's position and rotation)
        Vector3 pivotPos = primary.transform.position;
        Quaternion pivotRot = primary.transform.rotation;

        // Collect all children and their relative transforms
        List<Transform> children = new List<Transform>();
        List<Vector3> relativePositions = new List<Vector3>();
        List<Quaternion> relativeRotations = new List<Quaternion>();

        foreach (Transform child in virtualObjectsParent.transform)
        {
            children.Add(child);
            // Calculate position relative to pivot
            Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
            relativePositions.Add(relPos);
            // Calculate rotation relative to pivot
            Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
            relativeRotations.Add(relRot);
        }

        // Calculate new pivot position and rotation (facing camera)
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        // Maintain horizontal distance from camera
        float hDist = Vector2.Distance(
            new Vector2(pivotPos.x, pivotPos.z),
            new Vector2(camPos.x, camPos.z)
        );

        Vector3 newPivotPos = camPos + camForward * hDist;
        newPivotPos.y = pivotPos.y; // Preserve Y position
        Quaternion newPivotRot = Quaternion.LookRotation(camForward);

        // Apply new transforms to all children
        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            // Restore relative position and rotation with new pivot
            child.position = newPivotPos + newPivotRot * relativePositions[i];
            child.rotation = newPivotRot * relativeRotations[i];
        }
    }

    /// <summary>
    /// Fallback: recenter only the primary RTTMenuFrame (old behavior)
    /// </summary>
    private void RecenterPrimaryOnly(Camera cam)
    {
        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary != null)
        {
            RecenterTransform(primary.transform, cam);
        }

        if (!followPrimaryFrame)
        {
            RecenterTransform(transform, cam);
        }
    }

    /// <summary>
    /// Recenter a transform to face the camera while maintaining horizontal distance.
    /// </summary>
    private void RecenterTransform(Transform target, Camera cam)
    {
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 currentPos = target.position;
        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

        Vector3 newPos = camPos + camForward * hDist;
        newPos.y = currentPos.y;

        target.position = newPos;
        target.rotation = Quaternion.LookRotation(camForward);
    }

    private void SelectAppButton(int index)
    {
        if (index == _activeAppButtonIndex) return;

        _activeAppButtonIndex = index;
        UpdateAllAppButtonColors();
        Debug.Log($"[RTTTaskbar] App button {index} selected");

        MarkDirty();
    }

    private void UpdateAllAppButtonColors()
    {
        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);

        for (int i = 0; i < _appButtons.Count; i++)
        {
            bool isActive = (i == _activeAppButtonIndex);
            var btn = _appButtons[i];
            if (btn == null) continue;

            // For bare icon buttons (frameless), change icon color directly
            // Structure: Container > Btn_icon > HitArea > Visuals > Content > Icon
            SetBareIconButtonColor(btn, isActive ? purpleColor : cyanColor);
        }

        // Re-render RTT canvas after color changes
        MarkDirty();
    }

    /// <summary>
    /// Set color for a bare icon button (changes icon and glow shadows).
    /// </summary>
    private void SetBareIconButtonColor(GameObject container, Color color)
    {
        // Find Icon image in the button hierarchy
        // Structure: Container > Btn_X > HitArea > Visuals > Content > Icon
        Transform iconTransform = null;

        // Try to find through hierarchy
        foreach (Transform child in container.transform)
        {
            // Child is the actual button (Btn_X)
            var hitArea = child.Find("HitArea");
            if (hitArea != null)
            {
                var visuals = hitArea.Find("Visuals");
                if (visuals != null)
                {
                    var content = visuals.Find("Content");
                    if (content != null)
                    {
                        iconTransform = content.Find("Icon");
                        break;
                    }
                }
            }
        }

        if (iconTransform == null) return;

        // Update icon color
        Image iconImg = iconTransform.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.color = Color.Lerp(color, Color.white, 0.9f);

            // Update shadow glow colors
            Shadow[] shadows = iconTransform.GetComponents<Shadow>();
            if (shadows.Length >= 4)
            {
                // Layer 1 - Sharp inner halo
                Color glowCol = Color.Lerp(color, Color.white, 0.7f);
                glowCol.a = 0.4f;
                shadows[0].effectColor = glowCol;
                shadows[1].effectColor = glowCol;

                // Layer 2 - Soft outer bloom
                Color bloomCol = Color.Lerp(color, Color.white, 0.8f);
                bloomCol.a = 0.15f;
                shadows[2].effectColor = bloomCol;
                shadows[3].effectColor = bloomCol;
            }
        }
    }

    // --- PUBLIC API (matching VRTaskbar) ---

    /// <summary>
    /// Returns whether passthrough mode is currently enabled.
    /// </summary>
    public bool IsPassthroughOn => _isPassthroughOn;

    /// <summary>
    /// Returns the currently active app button index.
    /// </summary>
    public int ActiveAppButtonIndex => _activeAppButtonIndex;

    /// <summary>
    /// Returns whether Home button is currently active.
    /// </summary>
    public bool IsHomeActive => _activeAppButtonIndex == 0;

    /// <summary>
    /// Select Home button (convenience method).
    /// </summary>
    public void SelectHome()
    {
        SelectAppButton(0);
    }

    /// <summary>
    /// Select an app button by index (public API).
    /// </summary>
    public void SelectAppButtonPublic(int index)
    {
        SelectAppButton(index);
    }
    #endregion

    #region App Slot Management
    /// <summary>
    /// Register an app in a specific slot with icon and click callback.
    /// Used by RTTManager to show app icons in taskbar.
    /// </summary>
    /// <param name="slotIndex">Slot index (1-3, 0 is reserved for Home)</param>
    /// <param name="icon">App icon to display</param>
    /// <param name="onClick">Callback when slot is clicked</param>
    public void RegisterApp(int slotIndex, Sprite icon, Action onClick)
    {
        Debug.Log($"[RTTTaskbar] RegisterApp called - slotIndex={slotIndex}, icon={(icon != null ? icon.name : "NULL")}, _appButtons.Count={_appButtons.Count}");

        if (slotIndex < 1 || slotIndex >= _appButtons.Count)
        {
            Debug.LogWarning($"[RTTTaskbar] Invalid slot index: {slotIndex} (must be 1 to {_appButtons.Count - 1})");
            return;
        }

        var slot = _appButtons[slotIndex];
        if (slot == null)
        {
            Debug.LogWarning($"[RTTTaskbar] _appButtons[{slotIndex}] is null!");
            return;
        }

        // Store callback
        _appSlotCallbacks[slotIndex] = onClick;

        // Show the slot
        var cg = slot.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            Debug.Log($"[RTTTaskbar] Set slot {slotIndex} alpha to 1");
        }
        else
        {
            Debug.LogWarning($"[RTTTaskbar] Slot {slotIndex} has no CanvasGroup!");
        }

        // Set icon by recreating the button content
        SetSlotIcon(slot, icon, slotIndex);

        MarkDirty();
        Debug.Log($"[RTTTaskbar] Registered app in slot {slotIndex} complete");
    }

    /// <summary>
    /// Unregister an app from a slot, hiding it.
    /// </summary>
    /// <param name="slotIndex">Slot index to unregister</param>
    public void UnregisterApp(int slotIndex)
    {
        if (slotIndex < 1 || slotIndex >= _appButtons.Count)
            return;

        var slot = _appButtons[slotIndex];
        if (slot == null) return;

        // Remove callback
        _appSlotCallbacks.Remove(slotIndex);

        // Hide the slot
        var cg = slot.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = 0f;

        // If this slot was selected, go home
        if (_activeAppButtonIndex == slotIndex)
        {
            SelectAppButton(0);
            // Trigger home callback
            RTTManager appManager = RTTManager.Instance;
            if (appManager != null)
            {
                appManager.SwitchToHome();
            }
        }

        MarkDirty();
        Debug.Log($"[RTTTaskbar] Unregistered app from slot {slotIndex}");
    }

    /// <summary>
    /// Change the active slot selection (purple highlight).
    /// </summary>
    /// <param name="index">Slot index to select</param>
    public void SelectSlot(int index)
    {
        Debug.Log($"[RTTTaskbar] SelectSlot({index}) called, current active: {_activeAppButtonIndex}");
        SelectAppButton(index);
    }

    /// <summary>
    /// Set or update the icon for a slot.
    /// </summary>
    private void SetSlotIcon(GameObject slot, Sprite icon, int slotIndex)
    {
        Debug.Log($"[RTTTaskbar] SetSlotIcon - slot={slot.name}, icon={(icon != null ? icon.name : "NULL")}, slotIndex={slotIndex}");

        // Remove existing button content (use DestroyImmediate to avoid timing issues)
        int oldChildCount = slot.transform.childCount;
        for (int i = slot.transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(slot.transform.GetChild(i).gameObject);
            else
                DestroyImmediate(slot.transform.GetChild(i).gameObject);
        }
        Debug.Log($"[RTTTaskbar] SetSlotIcon - removed {oldChildCount} old children");

        if (icon == null)
        {
            Debug.LogWarning($"[RTTTaskbar] SetSlotIcon - icon is NULL, returning early");
            return;
        }

        Color cyanColor = new Color(0f, 0.9f, 1f);

        // Create new icon button
        var iconBtn = VRButtonFactory.CreateBareIconButton(
            slot.transform, buttonSize, icon, cyanColor,
            () =>
            {
                // Call registered callback or default to selection
                if (_appSlotCallbacks.ContainsKey(slotIndex))
                {
                    _appSlotCallbacks[slotIndex]?.Invoke();
                }
                else
                {
                    SelectAppButton(slotIndex);
                }
                MarkDirty();
            },
            0.05f, 0.6f
        );
        iconBtn.transform.SetAsFirstSibling();

        // Fix RTT raycast: Set button layer to UI
        SetLayerRecursively(iconBtn, LayerMask.NameToLayer("UI"));

        Debug.Log($"[RTTTaskbar] SetSlotIcon - created iconBtn={iconBtn.name}, slot.childCount now={slot.transform.childCount}");

        // Apply correct color based on active state
        Color purpleColor = new Color(0.9f, 0.3f, 1f);
        if (slotIndex == _activeAppButtonIndex)
        {
            SetBareIconButtonColor(slot, purpleColor);
        }
    }

    /// <summary>
    /// Handle Home button click - switches to MainMenu via RTTManager.
    /// </summary>
    private void HandleHomeClick()
    {
        RTTManager appManager = RTTManager.Instance;
        if (appManager != null)
        {
            appManager.SwitchToHome();
        }
        else
        {
            // Fallback if AppManager not initialized
            SelectAppButton(0);
        }
        MarkDirty();
    }

    /// <summary>
    /// Rewire Home button to use HandleHomeClick instead of default behavior.
    /// </summary>
    private void RewireHomeButton(GameObject homeBtn)
    {
        var button = homeBtn.GetComponentInChildren<UnityEngine.UI.Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                HandleHomeClick();
            });
        }
    }
    #endregion

    #region Icons
    private void LoadIcons()
    {
        if (iconQuit == null) iconQuit = LoadIcon("quit");
        if (iconSettings == null) iconSettings = LoadIcon("settings");
        if (iconPassthrough == null) iconPassthrough = LoadIcon("passthrough");
        if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
        if (iconHome == null) iconHome = LoadIcon("home");
        if (iconWifi == null) iconWifi = LoadIcon("wifi");
        if (iconBattery == null) iconBattery = LoadIcon("battery");

        // Debug: Log loaded icons
        Debug.Log($"[RTTTaskbar] Icons loaded - Quit:{iconQuit != null}, Settings:{iconSettings != null}, Passthrough:{iconPassthrough != null}, Recenter:{iconRecenter != null}, Home:{iconHome != null}");
    }

    public static Sprite LoadIcon(string name)
    {
        var sprite = Resources.Load<Sprite>($"icon_{name}");
        if (sprite == null)
        {
            Debug.LogWarning($"[RTTTaskbar] Failed to load icon: icon_{name} from Resources");
        }
        return sprite;
    }
    #endregion

    #region Theme Support
    /// <summary>
    /// Apply current theme colors to glass and border materials.
    /// </summary>
    protected override void ApplyCurrentTheme()
    {
        var theme = GetTheme();
        if (theme == null) return;

        // Apply glass colors
        if (_glassMaterial != null)
        {
            _glassMaterial.SetColor("_ColorA", theme.glassColorA);
            _glassMaterial.SetColor("_ColorB", theme.glassColorB);
            _glassMaterial.SetFloat("_GlassAlpha", theme.glassAlpha);
        }

        // Apply border colors
        if (_borderMaterial != null)
        {
            _borderMaterial.SetColor("_ColorA", theme.glowColorA);
            _borderMaterial.SetColor("_ColorB", theme.glowColorB);
        }

        MarkDirty();
    }

    /// <summary>
    /// Get glass color A with fallback to theme.
    /// </summary>
    private Color GetGlassColorA()
    {
        var theme = GetTheme();
        return theme?.glassColorA ?? new Color(0.0f, 0.55f, 0.65f, 0.35f);
    }

    /// <summary>
    /// Get glass color B with fallback to theme.
    /// </summary>
    private Color GetGlassColorB()
    {
        var theme = GetTheme();
        return theme?.glassColorB ?? new Color(0.30f, 0.12f, 0.50f, 0.32f);
    }

    /// <summary>
    /// Get glow color A with fallback to theme.
    /// </summary>
    private Color GetGlowColorA()
    {
        var theme = GetTheme();
        return theme?.glowColorA ?? new Color(0.3f, 1f, 1f, 1f);
    }

    /// <summary>
    /// Get glow color B with fallback to theme.
    /// </summary>
    private Color GetGlowColorB()
    {
        var theme = GetTheme();
        return theme?.glowColorB ?? new Color(1f, 0.4f, 1f, 1f);
    }

    /// <summary>
    /// Get primary color with fallback.
    /// </summary>
    private Color GetPrimaryColor()
    {
        var theme = GetTheme();
        return theme?.primaryColor ?? new Color(0f, 0.9f, 1f);
    }

    /// <summary>
    /// Get accent color with fallback.
    /// </summary>
    private Color GetAccentColor()
    {
        var theme = GetTheme();
        return theme?.accentColor ?? new Color(0.9f, 0.3f, 1f);
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

                // Main body
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

                // Tip
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

    /// <summary>
    /// Creates battery fill sprite WITHOUT the tip (positive terminal).
    /// This prevents the "extra vertical line" when using Type.Filled.
    /// </summary>
    private Sprite GetBatteryFillSprite()
    {
        if (_batteryFillSprite != null) return _batteryFillSprite;

        int w = 64, h = 32;
        int radius = 4;
        int tipWidth = 4;  // Same as main sprite, but we won't draw the tip

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] colors = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float alpha = 0f;

                // Main body only (no tip) - body ends at w - tipWidth
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

                // NO TIP - that's the key difference from GetBatterySprite()

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

                // Three arcs
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

                // Center dot
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
