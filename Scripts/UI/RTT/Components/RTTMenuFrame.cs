using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// RTT-based Menu Frame with glass effect, glowing borders, and floating data particles.
/// Migrated from VRMenuFrame to use Render-to-Texture approach.
/// </summary>
public class RTTMenuFrame : RTTCanvasBase
{
    #region Static Instance
    private static RTTMenuFrame _primaryInstance;
    public static RTTMenuFrame PrimaryInstance => _primaryInstance;
    #endregion

    #region Configuration
    [Header("Primary Frame")]
    [Tooltip("Only one RTTMenuFrame can be primary at a time")]
    [SerializeField] private bool isPrimary = true;

    [Header("Panel Size (meters)")]
    [Tooltip("Physical width in meters")]
    [SerializeField] private float panelWidth = 1.6f;

    [Tooltip("Physical height in meters")]
    [SerializeField] private float panelHeight = 0.9f;

    [Header("Logical Size (pixels)")]
    [Tooltip("Logical width for UI layout")]
    [SerializeField] private float logicalWidth = 1920f;

    [Header("Visual Config")]
    [SerializeField] private Color glassColor = new Color(1f, 1f, 1f, 0.098f);

    [Header("Glassmorphism")]
    [SerializeField] private bool enableGlassmorphism = true;
    [Range(0, 40)]
    [SerializeField] private float blurIntensity = 2f;
    [Range(1, 8)]
    [SerializeField] private int blurQuality = 3;
#pragma warning disable 0414 // Reserved for future glassmorphism implementation
    [Range(0, 1)]
    [SerializeField] private float glassOpacity = 0f;
#pragma warning restore 0414
    [Range(0, 1)]
    [SerializeField] private float tintStrength = 0.1f;
    [Range(0, 0.5f)]
    [SerializeField] private float innerGlow = 0f;
    [Range(0.9f, 1.3f)]
    [SerializeField] private float brightness = 1f;
    [Range(0.5f, 1f)]
    [SerializeField] private float saturation = 1f;

    [Header("Glowing Border")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);
#pragma warning disable 0414 // Reserved for shader customization
    [SerializeField] private float glowIntensity = 1.2f;
    [SerializeField] private float borderThickness = 3f;
#pragma warning restore 0414
    [Range(0f, 0.1f)]
    [SerializeField] private float glowExpansion = 0.02f;
#pragma warning disable 0414
    [SerializeField] private float glowSpread = 40f;
#pragma warning restore 0414
    [SerializeField] private float shimmerSpeed = 0.4f;
#pragma warning disable 0414
    [SerializeField] private float hdrBoost = 1.8f;
#pragma warning restore 0414

    [Header("Content Margin (pixels)")]
    [SerializeField] private float contentMarginLeft = 75f;
    [SerializeField] private float contentMarginRight = 75f;
    [SerializeField] private float contentMarginTop = 50f;
    [SerializeField] private float contentMarginBottom = 50f;

    [Header("Floating Data Effect")]
    [SerializeField] private bool enableFloatingData = true;
    [SerializeField] private int particleCount = 20;

    [Header("Main Menu")]
    [Tooltip("Automatically create Main Menu content on init")]
    [SerializeField] private bool initMainMenu = true;
    [SerializeField] private int menuColumns = 3;
#pragma warning disable 0414 // Reserved for future menu layout customization
    [SerializeField] private int menuRows = 2;
#pragma warning restore 0414
    [SerializeField] private Vector2 menuSpacing = new Vector2(50f, 75f);
    [SerializeField] private float menuButtonAspect = 1.4f;
    [SerializeField] private int menuFontSize = 42;
    [SerializeField] private TMP_FontAsset menuFont;
    [SerializeField] private Color[] menuButtonColors = new Color[] {
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.76f, 0.36f, 1.0f, 1.0f), // Purple
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.76f, 0.36f, 1.0f, 1.0f), // Purple
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
    };

    [Header("Menu Icons")]
    [SerializeField] private Sprite iconRemote;
    [SerializeField] private Sprite iconBrowser;
    [SerializeField] private Sprite iconMedia;
    [SerializeField] private Sprite iconFiles;
    [SerializeField] private Sprite iconSettings;
    [SerializeField] private Sprite iconQuit;
    #endregion

    #region Private Fields
    private RectTransform _contentContainer;
    private Material _glassMaterial;
    private Material _borderMaterial;
    private Sprite _pixelSprite;
    private Sprite _roundedMaskSprite;
    private RTTBlurBackgroundCapture _blurCapture;

    // Menu navigation state
    private enum MenuState { MainMenu, RemoteMenu }
    private MenuState _currentMenuState = MenuState.MainMenu;
    private GameObject _currentMenuContent;
    private RTTRemoteMenu _remoteMenuInstance;
    private RTTMainMenu _mainMenuInstance;

    // Calculated values
    private float LogicalHeight => (logicalWidth / panelWidth) * panelHeight;
    private float Aspect => panelWidth / panelHeight;
    #endregion

    #region Properties
    public RectTransform ContentContainer => _contentContainer;
    public float PanelWidth => panelWidth;
    public float PanelHeight => panelHeight;
    public float LogicalWidthValue => logicalWidth;
    #endregion

    #region Lifecycle
    protected override void Awake()
    {
        // Set world size from panel dimensions
        worldWidth = panelWidth;
        worldHeight = panelHeight;

        base.Awake();
    }

    protected override void Start()
    {
        base.Start();

        // Register as primary
        if (isPrimary)
        {
            SetAsPrimary();
        }
        else if (_primaryInstance == null)
        {
            SetAsPrimary();
        }

        // Setup RTT blur capture after initialization
        if (enableGlassmorphism && _isInitialized)
        {
            SetupBlurCapture();
        }
    }

    private void SetupBlurCapture()
    {
        if (_displayQuad == null || _glassMaterial == null) return;

        _blurCapture = gameObject.AddComponent<RTTBlurBackgroundCapture>();
        _blurCapture.BlurRadius = blurIntensity * 0.3f; // Scale down for blur shader
        _blurCapture.BlurIterations = Mathf.Clamp(blurQuality / 2, 1, 4);
        _blurCapture.Initialize(this, _displayQuad);

        // Apply blurred texture to glass material
        _blurCapture.ApplyToMaterial(_glassMaterial);

        // Disable procedural fallback since we have real capture
        _glassMaterial.SetFloat("_UseProcedural", 0f);

        Debug.Log("[RTTMenuFrame] RTT Blur Capture initialized");
    }

    protected override void OnDestroy()
    {
        // Cleanup materials
        if (_glassMaterial != null) Destroy(_glassMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);

        // Clear primary if this was it
        if (_primaryInstance == this)
        {
            _primaryInstance = null;
        }

        base.OnDestroy();
    }

    public override void MarkDirty()
    {
        base.MarkDirty();

        // Also mark blur capture as dirty
        if (_blurCapture != null)
        {
            _blurCapture.MarkDirty();
        }
    }
    #endregion

    #region Primary Instance
    private void SetAsPrimary()
    {
        if (_primaryInstance != null && _primaryInstance != this)
        {
            _primaryInstance.isPrimary = false;
        }
        _primaryInstance = this;
        isPrimary = true;
    }
    #endregion

    #region RTTCanvasBase Overrides
    protected override Vector2Int GetResolution()
    {
        int width = Mathf.RoundToInt(logicalWidth);
        int height = Mathf.RoundToInt(LogicalHeight);
        return new Vector2Int(width, height);
    }

    protected override int GetCameraDepth()
    {
        return -50; // Menu frame renders first
    }

    protected override void BuildUI()
    {
        if (_canvas == null) return;

        var canvasRect = _canvas.GetComponent<RectTransform>();
        float w = logicalWidth;
        float h = LogicalHeight;

        // 1. Create Glass Background with Border
        CreateGlassPanel(canvasRect, w, h);

        // 2. Create Content Container
        CreateContentContainer(canvasRect);

        // 3. Build Main Menu if enabled
        if (initMainMenu)
        {
            LoadMenuIcons();
            BuildMainMenu();
        }

        Debug.Log($"[RTTMenuFrame] UI built: {w}x{h} logical pixels");
    }

    protected override void SetupFallbackWorldSpaceCanvas()
    {
        Debug.LogWarning("[RTTMenuFrame] Falling back to World Space Canvas");

        // Create fallback canvas
        var canvasGO = new GameObject($"FallbackCanvas_{GetType().Name}");
        canvasGO.transform.SetParent(transform);
        canvasGO.transform.localPosition = Vector3.zero;
        canvasGO.transform.localRotation = Quaternion.identity;

        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(logicalWidth, LogicalHeight);

        float scaleFactor = panelWidth / logicalWidth;
        canvasGO.transform.localScale = Vector3.one * scaleFactor;

        _canvasScaler = canvasGO.AddComponent<CanvasScaler>();
        _graphicRaycaster = canvasGO.AddComponent<GraphicRaycaster>();

        // Set layer
        int quadLayer = LayerMask.NameToLayer(quadLayerName);
        if (quadLayer != -1)
        {
            SetLayerRecursively(canvasGO, quadLayer);
        }

        // Add collider
        _quadCollider = canvasGO.AddComponent<BoxCollider>();
        _quadCollider.size = new Vector3(logicalWidth, LogicalHeight, 10f);
        _quadCollider.isTrigger = true;

        _isInitialized = true;

        // Build UI
        BuildUI();
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
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
        float edgePad = glowExpansion > 0 ? glowExpansion / (1f + 2f * glowExpansion) : 0f;
        float aspect = Aspect;
        // Background needs LARGER corner radius to stay INSIDE the border
        float bgCornerRadius = 0.14f;

        // Try glass shader
        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            _glassMaterial = new Material(glassShader);

            _glassMaterial.SetFloat("_CornerRadius", bgCornerRadius);
            _glassMaterial.SetFloat("_EdgePadding", edgePad);
            _glassMaterial.SetFloat("_Aspect", aspect);

            // Transparent gradient: 32% Cyan, 42% Deep Sea Blue, 26% Purple with frosted glass effect
            Color cyanDeepSeaBlue = new Color(0.0f, 0.55f, 0.65f, 0.35f);
            Color deepSeaBluePurple = new Color(0.30f, 0.12f, 0.50f, 0.32f);
            _glassMaterial.SetColor("_ColorA", cyanDeepSeaBlue);
            _glassMaterial.SetColor("_ColorB", deepSeaBluePurple);
            _glassMaterial.SetFloat("_GradientOffset", 0f);
            _glassMaterial.SetFloat("_GradientAngle", -10f);
            _glassMaterial.SetFloat("_CyanRatio", 0.7f);
            _glassMaterial.SetFloat("_GlassAlpha", 0.38f);
            _glassMaterial.SetFloat("_FresnelPower", 2.2f);
            _glassMaterial.SetFloat("_FresnelStrength", 0.12f);

            // Glassmorphism settings
            _glassMaterial.SetFloat("_BlurRadius", blurIntensity);
            _glassMaterial.SetFloat("_BlurIterations", blurQuality);
            // Don't override _GlassOpacity - use shader default (0.25)
            _glassMaterial.SetFloat("_TintStrength", tintStrength);
            _glassMaterial.SetFloat("_InnerGlow", innerGlow);
            _glassMaterial.SetFloat("_Brightness", brightness);
            _glassMaterial.SetFloat("_Saturation", saturation);

            // RTT Blur Mode: Enable blur with background capture
            if (enableGlassmorphism)
            {
                _glassMaterial.SetFloat("_BlurEnabled", 1f);
                _glassMaterial.SetFloat("_UseExternalBlur", 1f);
                // Procedural fallback enabled by default
                _glassMaterial.SetFloat("_UseProcedural", 1f);
                _glassMaterial.SetColor("_ProceduralBaseColor", new Color(0.15f, 0.25f, 0.35f, 1f));
            }
            else
            {
                _glassMaterial.SetFloat("_BlurEnabled", 0f);
            }

            img.material = _glassMaterial;
            img.color = Color.white;
        }
        else
        {
            img.color = new Color(0.0f, 0.4f, 0.5f, 0.35f);
            expansion = 0;
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-expansion, -expansion);
        rt.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();

        // Create glowing border
        CreateGlowingBorder(bgObj.transform, w, h, edgePad * 1.175f);

        // Create floating data effects
        if (enableFloatingData)
        {
            CreateFloatingDataEffects(bgObj.transform, w, h);
        }
    }

    private void CreateGlowingBorder(Transform parent, float w, float h, float edgePad)
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

        float aspect = Aspect;

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);

            _borderMaterial.SetFloat("_StrokeEnabled", 0);
            _borderMaterial.SetFloat("_BorderWidth", 0.02f);
            _borderMaterial.SetFloat("_CornerRadius", 0.12f);
            _borderMaterial.SetFloat("_EdgePadding", edgePad);
            _borderMaterial.SetFloat("_Aspect", aspect);

            _borderMaterial.SetFloat("_Layer1Width", 0.008f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
            _borderMaterial.SetFloat("_Layer2Width", 0.018f);
            _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
            _borderMaterial.SetFloat("_Layer3Width", 0.04f);
            _borderMaterial.SetFloat("_Layer3Alpha", 0.6f);
            _borderMaterial.SetFloat("_Layer4Width", 0.08f);
            _borderMaterial.SetFloat("_Layer4Alpha", 0.3f);

            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
            _borderMaterial.SetColor("_ColorA", cyanColor);
            _borderMaterial.SetColor("_ColorB", purpleColor);
            _borderMaterial.SetFloat("_GradientMode", 2f);
            _borderMaterial.SetFloat("_GradientAngle", -10f);
            _borderMaterial.SetFloat("_GlassAlpha", 0.02f);
            _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            _borderMaterial.SetFloat("_ShimmerSpeed", shimmerSpeed);
            _borderMaterial.SetFloat("_ShimmerIntensity", 0.2f);
            _borderMaterial.SetFloat("_LightSize", 0.008f);
            _borderMaterial.SetFloat("_LightGlow", 0.008f);

            borderImg.material = _borderMaterial;
        }

        borderObj.transform.SetAsLastSibling();
    }

    private void CreateFloatingDataEffects(Transform parent, float w, float h)
    {
        GameObject fxContainer = new GameObject("FX_DataStream");
        fxContainer.transform.SetParent(parent, false);

        RectTransform rt = fxContainer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;

        float expansionPxW = w * glowExpansion;
        float expansionPxH = h * glowExpansion;

        rt.offsetMin = new Vector2(
            expansionPxW + contentMarginLeft,
            expansionPxH + contentMarginBottom
        );
        rt.offsetMax = new Vector2(
            -(expansionPxW + contentMarginRight),
            -(expansionPxH + contentMarginTop)
        );

        // Use Mask for rounded clipping
        Image maskImage = fxContainer.AddComponent<Image>();
        maskImage.sprite = GetRoundedMaskSprite();
        maskImage.type = Image.Type.Sliced;
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;

        Mask mask = fxContainer.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Create particles
        for (int i = 0; i < particleCount; i++)
        {
            CreateDataParticle(fxContainer.transform, w, h, i);
        }
    }

    private void CreateDataParticle(Transform parent, float w, float h, int index)
    {
        GameObject p = new GameObject($"Bit_{index}");
        p.transform.SetParent(parent, false);

        Image pImg = p.AddComponent<Image>();
        pImg.sprite = GetPixelSprite();
        pImg.raycastTarget = false;

        bool cyanOrPurple = Random.value > 0.5f;
        Color baseCol = cyanOrPurple ? Color.cyan : new Color(0.8f, 0f, 1f);
        pImg.color = new Color(baseCol.r, baseCol.g, baseCol.b, Random.Range(0.1f, 0.4f));

        RectTransform pRT = p.GetComponent<RectTransform>();
        float size = Random.Range(10f, 60f);
        pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f));

        float startX = Random.Range(-w / 2f, w / 2f);
        float startY = Random.Range(-h / 2f, h / 2f);
        pRT.anchoredPosition = new Vector2(startX, startY);

        // Add animation component
        var anim = p.AddComponent<FloatingDataAnim>();
        anim.speed = Random.Range(10f, 40f);
        anim.range = new Vector2(w - contentMarginLeft - contentMarginRight,
                                  h - contentMarginTop - contentMarginBottom);

        // Subscribe to animation updates to mark dirty
        anim.OnAnimationUpdate += MarkDirty;
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
    #endregion

    #region Public API
    /// <summary>
    /// Set content to fill the content container
    /// </summary>
    public void SetContent(RectTransform content)
    {
        if (_contentContainer == null)
        {
            Debug.LogWarning("[RTTMenuFrame] ContentContainer not initialized");
            return;
        }

        content.SetParent(_contentContainer, false);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        content.localScale = Vector3.one;

        MarkDirty();
    }

    /// <summary>
    /// Set content from GameObject
    /// </summary>
    public void SetContent(GameObject content)
    {
        RectTransform rt = content.GetComponent<RectTransform>();
        if (rt == null) rt = content.AddComponent<RectTransform>();
        SetContent(rt);
    }

    /// <summary>
    /// Update glow colors dynamically
    /// </summary>
    public void UpdateGlowColors(Color colorA, Color colorB)
    {
        if (_borderMaterial != null)
        {
            _borderMaterial.SetColor("_ColorA", colorA);
            _borderMaterial.SetColor("_ColorB", colorB);
            MarkDirty();
        }
    }

    /// <summary>
    /// Set horizontal separators on the border
    /// </summary>
    public void SetHorizontalSeparators(int count, Vector4 positions, float width = 0.004f,
        float glowWidth = 0.015f, float alpha = 0.8f, float length = 1.0f)
    {
        SetHorizontalSeparators(count, positions, width, glowWidth, alpha,
            new Vector4(length, length, length, length));
    }

    public void SetHorizontalSeparators(int count, Vector4 positions, float width,
        float glowWidth, float alpha, Vector4 lengths)
    {
        if (_borderMaterial == null) return;

        _borderMaterial.SetFloat("_HSeparatorCount", count);
        _borderMaterial.SetVector("_HSeparatorPositions", positions);
        _borderMaterial.SetFloat("_HSeparatorWidth", width);
        _borderMaterial.SetFloat("_HSeparatorGlowWidth", glowWidth);
        _borderMaterial.SetFloat("_HSeparatorAlpha", alpha);
        _borderMaterial.SetVector("_HSeparatorLengths", new Vector4(
            Mathf.Clamp01(lengths.x),
            Mathf.Clamp01(lengths.y),
            Mathf.Clamp01(lengths.z),
            Mathf.Clamp01(lengths.w)
        ));

        MarkDirty();
    }

    /// <summary>
    /// Set vertical separators on the border
    /// </summary>
    public void SetVerticalSeparators(int count, Vector4 positions, float width = 0.004f,
        float glowWidth = 0.015f, float alpha = 0.8f)
    {
        if (_borderMaterial == null) return;

        _borderMaterial.SetFloat("_SeparatorCount", count);
        _borderMaterial.SetVector("_SeparatorPositions", positions);
        _borderMaterial.SetFloat("_SeparatorWidth", width);
        _borderMaterial.SetFloat("_SeparatorGlowWidth", glowWidth);
        _borderMaterial.SetFloat("_SeparatorAlpha", alpha);

        MarkDirty();
    }
    #endregion

    #region Main Menu
    private void LoadMenuIcons()
    {
        if (iconRemote == null) iconRemote = Resources.Load<Sprite>("icon_remote");
        if (iconBrowser == null) iconBrowser = Resources.Load<Sprite>("icon_browser");
        if (iconMedia == null) iconMedia = Resources.Load<Sprite>("icon_media");
        if (iconFiles == null) iconFiles = Resources.Load<Sprite>("icon_files");
        if (iconSettings == null) iconSettings = Resources.Load<Sprite>("icon_settings");
        if (iconQuit == null) iconQuit = Resources.Load<Sprite>("icon_quit");

        Debug.Log($"[RTTMenuFrame] Menu icons loaded - Remote:{iconRemote != null}, Browser:{iconBrowser != null}, Media:{iconMedia != null}, Files:{iconFiles != null}, Settings:{iconSettings != null}, Quit:{iconQuit != null}");
    }

    private void BuildMainMenu()
    {
        if (_contentContainer == null) return;

        // Create menu container
        GameObject menuObj = new GameObject("MainMenu");
        menuObj.transform.SetParent(_contentContainer, false);

        RectTransform menuRT = menuObj.AddComponent<RectTransform>();
        menuRT.anchorMin = Vector2.zero;
        menuRT.anchorMax = Vector2.one;
        menuRT.offsetMin = Vector2.zero;
        menuRT.offsetMax = Vector2.zero;

        // Get container size
        float containerW = _contentContainer.rect.width;
        float containerH = _contentContainer.rect.height;

        // If rect not ready, calculate from logical size
        if (containerW <= 0 || containerH <= 0)
        {
            containerW = logicalWidth - contentMarginLeft - contentMarginRight;
            containerH = LogicalHeight - contentMarginTop - contentMarginBottom;
        }

        // Create RTTMainMenu component
        _mainMenuInstance = menuObj.AddComponent<RTTMainMenu>();
        _mainMenuInstance.CustomFont = menuFont;
        _mainMenuInstance.FontSize = menuFontSize;
        _mainMenuInstance.Columns = menuColumns;
        _mainMenuInstance.Spacing = menuSpacing;
        _mainMenuInstance.ButtonAspect = menuButtonAspect;

        // Add menu items with custom icons and colors
        _mainMenuInstance.AddItem("remote", "Remote Desktop", iconRemote, GetMenuButtonColor(0));
        _mainMenuInstance.AddItem("browser", "Browser", iconBrowser, GetMenuButtonColor(1));
        _mainMenuInstance.AddItem("media", "Media", iconMedia, GetMenuButtonColor(2));
        _mainMenuInstance.AddItem("files", "Files", iconFiles, GetMenuButtonColor(3));
        _mainMenuInstance.AddItem("settings", "Settings", iconSettings, GetMenuButtonColor(4));
        _mainMenuInstance.AddItem("quit", "Quit", iconQuit, GetMenuButtonColor(5));

        // Subscribe to menu item clicks
        _mainMenuInstance.OnMenuItemClicked += OnMainMenuItemClicked;

        // Build the UI
        _mainMenuInstance.BuildUI(_contentContainer, containerW, containerH);

        _currentMenuContent = menuObj;

        Debug.Log($"[RTTMenuFrame] Main Menu built with RTTMainMenu component");
    }

    private void OnMainMenuItemClicked(string itemId)
    {
        switch (itemId)
        {
            case "remote":
                OnRemoteDesktopClicked();
                break;
            case "browser":
                OnBrowserClicked();
                break;
            case "media":
                OnMediaClicked();
                break;
            case "files":
                OnFilesClicked();
                break;
            case "settings":
                OnSettingsClicked();
                break;
            case "quit":
                OnQuitClicked();
                break;
        }
        MarkDirty();
    }

    private Color GetMenuButtonColor(int index)
    {
        if (menuButtonColors != null && index < menuButtonColors.Length)
            return menuButtonColors[index];
        return new Color(0f, 0.9f, 1f); // Default cyan
    }

    // Menu button click handlers
    private void OnRemoteDesktopClicked()
    {
        Debug.Log("[RTTMenuFrame] Remote Desktop clicked");
        SwitchToRemoteMenu();
    }

    private void OnBrowserClicked()
    {
        Debug.Log("[RTTMenuFrame] Browser clicked");
    }

    private void OnMediaClicked()
    {
        Debug.Log("[RTTMenuFrame] Media clicked");
    }

    private void OnFilesClicked()
    {
        Debug.Log("[RTTMenuFrame] Files clicked");
    }

    private void OnSettingsClicked()
    {
        Debug.Log("[RTTMenuFrame] Settings clicked");
    }

    private void OnQuitClicked()
    {
        Debug.Log("[RTTMenuFrame] Quit clicked");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Clear and rebuild main menu
    /// </summary>
    public void RebuildMainMenu()
    {
        // Unsubscribe from existing main menu events
        if (_mainMenuInstance != null)
        {
            _mainMenuInstance.OnMenuItemClicked -= OnMainMenuItemClicked;
        }

        // Find and destroy existing main menu
        Transform existingMenu = _contentContainer?.Find("MainMenu");
        if (existingMenu != null)
        {
            Destroy(existingMenu.gameObject);
        }
        _mainMenuInstance = null;

        LoadMenuIcons();
        BuildMainMenu();
        MarkDirty();
    }
    #endregion

    #region Menu Navigation
    /// <summary>
    /// Switch from Main Menu to Remote Menu
    /// </summary>
    public void SwitchToRemoteMenu()
    {
        if (_currentMenuState == MenuState.RemoteMenu) return;
        if (_contentContainer == null) return;

        // Unsubscribe from main menu events
        if (_mainMenuInstance != null)
        {
            _mainMenuInstance.OnMenuItemClicked -= OnMainMenuItemClicked;
        }

        // Destroy current main menu
        Transform mainMenu = _contentContainer.Find("MainMenu");
        if (mainMenu != null)
        {
            Destroy(mainMenu.gameObject);
        }
        _mainMenuInstance = null;

        // Create Remote Menu container
        GameObject remoteObj = new GameObject("RemoteMenu");
        remoteObj.transform.SetParent(_contentContainer, false);

        RectTransform remoteRT = remoteObj.AddComponent<RectTransform>();
        remoteRT.anchorMin = Vector2.zero;
        remoteRT.anchorMax = Vector2.one;
        remoteRT.offsetMin = Vector2.zero;
        remoteRT.offsetMax = Vector2.zero;

        // Add RTTRemoteMenu component
        _remoteMenuInstance = remoteObj.AddComponent<RTTRemoteMenu>();
        _remoteMenuInstance.themeColor = menuButtonColors != null && menuButtonColors.Length > 0
            ? menuButtonColors[0]
            : new Color(0f, 0.9f, 1f);
        _remoteMenuInstance.accentColor = menuButtonColors != null && menuButtonColors.Length > 2
            ? menuButtonColors[2]
            : new Color(0.76f, 0.36f, 1f);
        _remoteMenuInstance.customFont = menuFont;

        // Get container size
        float containerW = _contentContainer.rect.width;
        float containerH = _contentContainer.rect.height;
        if (containerW <= 0 || containerH <= 0)
        {
            containerW = logicalWidth - contentMarginLeft - contentMarginRight;
            containerH = LogicalHeight - contentMarginTop - contentMarginBottom;
        }

        // Subscribe to events
        _remoteMenuInstance.OnBackClicked += ReturnToMainMenu;
        _remoteMenuInstance.OnConnectClicked += OnRemoteConnectClicked;

        // Build the remote menu UI
        _remoteMenuInstance.BuildUI(remoteObj.transform, containerW, containerH);

        _currentMenuState = MenuState.RemoteMenu;
        _currentMenuContent = remoteObj;

        MarkDirty();
        Debug.Log("[RTTMenuFrame] Switched to Remote Menu");
    }

    private void OnRemoteConnectClicked()
    {
        if (_remoteMenuInstance == null) return;

        Debug.Log($"[RTTMenuFrame] Connect clicked - Host: {_remoteMenuInstance.Host}, Port: {_remoteMenuInstance.Port}");
        // TODO: Implement actual connection logic
    }

    /// <summary>
    /// Return from Remote Menu to Main Menu
    /// </summary>
    public void ReturnToMainMenu()
    {
        if (_currentMenuState == MenuState.MainMenu) return;
        if (_contentContainer == null) return;

        // Unsubscribe from remote menu events
        if (_remoteMenuInstance != null)
        {
            _remoteMenuInstance.OnBackClicked -= ReturnToMainMenu;
            _remoteMenuInstance.OnConnectClicked -= OnRemoteConnectClicked;
        }

        // Destroy current remote menu
        Transform remoteMenu = _contentContainer.Find("RemoteMenu");
        if (remoteMenu != null)
        {
            Destroy(remoteMenu.gameObject);
        }

        _remoteMenuInstance = null;
        _currentMenuContent = null;

        // Rebuild main menu
        LoadMenuIcons();
        BuildMainMenu();

        _currentMenuState = MenuState.MainMenu;

        MarkDirty();
        Debug.Log("[RTTMenuFrame] Returned to Main Menu");
    }

    /// <summary>
    /// Get current menu state
    /// </summary>
    public bool IsMainMenuActive => _currentMenuState == MenuState.MainMenu;
    public bool IsRemoteMenuActive => _currentMenuState == MenuState.RemoteMenu;

    /// <summary>
    /// Get Remote Menu instance (for accessing connection settings)
    /// </summary>
    public RTTRemoteMenu RemoteMenuInstance => _remoteMenuInstance;

    /// <summary>
    /// Get Main Menu instance
    /// </summary>
    public RTTMainMenu MainMenuInstance => _mainMenuInstance;
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

    private Sprite GetRoundedMaskSprite()
    {
        if (_roundedMaskSprite != null) return _roundedMaskSprite;

        int size = 128;
        int radius = 24;
        int border = radius;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float alpha = 1f;

                int cornerX = -1, cornerY = -1;
                if (x < radius && y < radius) { cornerX = radius; cornerY = radius; }
                else if (x >= size - radius && y < radius) { cornerX = size - radius - 1; cornerY = radius; }
                else if (x < radius && y >= size - radius) { cornerX = radius; cornerY = size - radius - 1; }
                else if (x >= size - radius && y >= size - radius) { cornerX = size - radius - 1; cornerY = size - radius - 1; }

                if (cornerX >= 0)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cornerX, cornerY));
                    alpha = Mathf.Clamp01(radius + 0.5f - dist);
                }

                colors[y * size + x] = new Color(1, 1, 1, alpha);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        _roundedMaskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f,
            100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        return _roundedMaskSprite;
    }
    #endregion
}
