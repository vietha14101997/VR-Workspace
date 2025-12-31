using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RTT-based Menu Frame with glass effect, glowing borders, and floating data particles.
/// This class is responsible ONLY for creating the visual frame UI.
/// Menu logic is handled by RTTManager and its controllers.
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
    #endregion

    #region Private Fields
    private RectTransform _contentContainer;
    private Material _glassMaterial;
    private Material _borderMaterial;
    private Sprite _pixelSprite;
    private Sprite _roundedMaskSprite;

    // Calculated values
    private float LogicalHeight => (logicalWidth / panelWidth) * panelHeight;
    private float Aspect => panelWidth / panelHeight;
    #endregion

    #region Properties
    public RectTransform ContentContainer => _contentContainer;
    public float PanelWidth => panelWidth;
    public float PanelHeight => panelHeight;
    public float LogicalWidthValue => logicalWidth;
    public float LogicalHeightValue => LogicalHeight;
    public float ContentMarginLeft => contentMarginLeft;
    public float ContentMarginRight => contentMarginRight;
    public float ContentMarginTop => contentMarginTop;
    public float ContentMarginBottom => contentMarginBottom;
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
        // Small edge padding to fill more of the canvas
        float edgePad = 0.0075f;
        float aspect = Aspect;
        // Background corner radius - smaller than taskbar due to different aspect ratio
        float bgCornerRadius = 0.04f;

        // Use Wide shader for better Android compatibility (Space key uses this and works)
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            _glassMaterial = new Material(glassShader);

            _glassMaterial.SetFloat("_CornerRadius", bgCornerRadius);
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
        else
        {
            img.color = new Color(0.0f, 0.4f, 0.5f, 0.35f);
            expansion = 0;
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        // Keep background within canvas bounds - edge padding in shader handles visual margin
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();

        // Create floating data effects
        if (enableFloatingData)
        {
            CreateFloatingDataEffects(bgObj.transform, w, h);
        }

        // Create glowing border
        CreateGlowingBorder(bgObj.transform, w, h, edgePad * 1.175f);
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
            // Border settings - thinner for MenuFrame's squarer aspect ratio
            _borderMaterial.SetFloat("_BorderWidth", 0.025f);
            _borderMaterial.SetFloat("_CornerRadius", 0.04f);  // Match background
            _borderMaterial.SetFloat("_EdgePadding", edgePad);
            _borderMaterial.SetFloat("_Aspect", aspect);

            // Glow layer widths - scaled down for MenuFrame
            _borderMaterial.SetFloat("_Layer1Width", 0.01f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
            _borderMaterial.SetFloat("_Layer2Width", 0.02f);
            _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
            _borderMaterial.SetFloat("_Layer3Width", 0.045f);
            _borderMaterial.SetFloat("_Layer3Alpha", 0.6f);
            _borderMaterial.SetFloat("_Layer4Width", 0.09f);
            _borderMaterial.SetFloat("_Layer4Alpha", 0.3f);

            // Use theme colors with fallback
            _borderMaterial.SetColor("_ColorA", GetGlowColorA());
            _borderMaterial.SetColor("_ColorB", GetGlowColorB());
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
    /// Configure frame size before initialization.
    /// Call this immediately after AddComponent before Awake runs.
    /// </summary>
    /// <param name="widthMeters">Physical width in meters</param>
    /// <param name="heightMeters">Physical height in meters</param>
    /// <param name="logicalWidthPixels">Logical width for UI layout (default 1920)</param>
    public void Configure(float widthMeters, float heightMeters, float logicalWidthPixels = 1920f)
    {
        panelWidth = widthMeters;
        panelHeight = heightMeters;
        logicalWidth = logicalWidthPixels;
        worldWidth = widthMeters;
        worldHeight = heightMeters;
    }

    /// <summary>
    /// Configure frame size and colors before initialization.
    /// </summary>
    public void Configure(float widthMeters, float heightMeters, float logicalWidthPixels,
        Color borderColorA, Color borderColorB)
    {
        Configure(widthMeters, heightMeters, logicalWidthPixels);
        glowColorA = borderColorA;
        glowColorB = borderColorB;
    }

    /// <summary>
    /// Configure all visual parameters before initialization.
    /// </summary>
    public void Configure(float widthMeters, float heightMeters, float logicalWidthPixels,
        Color borderColorA, Color borderColorB, Color glassCol,
        float marginLeft = 75f, float marginRight = 75f, float marginTop = 50f, float marginBottom = 50f)
    {
        Configure(widthMeters, heightMeters, logicalWidthPixels, borderColorA, borderColorB);
        glassColor = glassCol;
        contentMarginLeft = marginLeft;
        contentMarginRight = marginRight;
        contentMarginTop = marginTop;
        contentMarginBottom = marginBottom;
    }

    /// <summary>
    /// Set whether this frame is primary (only one primary at a time).
    /// </summary>
    public void SetPrimary(bool primary)
    {
        isPrimary = primary;
        if (primary)
        {
            SetAsPrimary();
        }
    }

    /// <summary>
    /// Enable or disable floating data particle effects.
    /// </summary>
    public void SetFloatingDataEnabled(bool enabled, int particles = 20)
    {
        enableFloatingData = enabled;
        particleCount = particles;
    }

    /// <summary>
    /// Set content margins.
    /// </summary>
    public void SetContentMargins(float left, float right, float top, float bottom)
    {
        contentMarginLeft = left;
        contentMarginRight = right;
        contentMarginTop = top;
        contentMarginBottom = bottom;
    }

    /// <summary>
    /// Factory method to create a configured RTTMenuFrame.
    /// </summary>
    public static RTTMenuFrame Create(Transform parent, float widthMeters, float heightMeters,
        float logicalWidthPixels = 1920f, bool isPrimaryFrame = false)
    {
        GameObject frameObj = new GameObject("RTTMenuFrame");
        frameObj.transform.SetParent(parent, false);
        frameObj.transform.localPosition = Vector3.zero;
        frameObj.transform.localRotation = Quaternion.identity;

        RTTMenuFrame frame = frameObj.AddComponent<RTTMenuFrame>();
        frame.Configure(widthMeters, heightMeters, logicalWidthPixels);
        frame.SetPrimary(isPrimaryFrame);

        return frame;
    }

    /// <summary>
    /// Factory method with color configuration.
    /// </summary>
    public static RTTMenuFrame Create(Transform parent, float widthMeters, float heightMeters,
        float logicalWidthPixels, Color borderColorA, Color borderColorB, bool isPrimaryFrame = false)
    {
        RTTMenuFrame frame = Create(parent, widthMeters, heightMeters, logicalWidthPixels, isPrimaryFrame);
        frame.glowColorA = borderColorA;
        frame.glowColorB = borderColorB;
        return frame;
    }

    /// <summary>
    /// Set content to fill the content container.
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
    /// Set content from GameObject.
    /// </summary>
    public void SetContent(GameObject content)
    {
        RectTransform rt = content.GetComponent<RectTransform>();
        if (rt == null) rt = content.AddComponent<RectTransform>();
        SetContent(rt);
    }

    /// <summary>
    /// Clear all content from the content container.
    /// </summary>
    public void ClearContent()
    {
        if (_contentContainer == null) return;

        for (int i = _contentContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(_contentContainer.GetChild(i).gameObject);
        }

        MarkDirty();
    }

    /// <summary>
    /// Update glow colors dynamically.
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
    /// Set horizontal separators on the border.
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
    /// Set vertical separators on the border.
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

    /// <summary>
    /// Get the content container size.
    /// </summary>
    public Vector2 GetContentSize()
    {
        if (_contentContainer != null && _contentContainer.rect.width > 0)
        {
            return new Vector2(_contentContainer.rect.width, _contentContainer.rect.height);
        }

        // Fallback calculation
        return new Vector2(
            logicalWidth - contentMarginLeft - contentMarginRight,
            LogicalHeight - contentMarginTop - contentMarginBottom
        );
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
            _glassMaterial.SetFloat("_GradientAngle", theme.glassGradientAngle);
            _glassMaterial.SetFloat("_CyanRatio", theme.glassGradientRatio);
            _glassMaterial.SetFloat("_FresnelPower", theme.glassFresnelPower);
            _glassMaterial.SetFloat("_FresnelStrength", theme.glassFresnelStrength);
        }

        // Apply border colors
        if (_borderMaterial != null)
        {
            _borderMaterial.SetColor("_ColorA", theme.glowColorA);
            _borderMaterial.SetColor("_ColorB", theme.glowColorB);
            _borderMaterial.SetFloat("_Layer1Width", theme.glowLayer1Width);
            _borderMaterial.SetFloat("_Layer1Alpha", theme.glowLayer1Alpha);
            _borderMaterial.SetFloat("_Layer2Width", theme.glowLayer2Width);
            _borderMaterial.SetFloat("_Layer2Alpha", theme.glowLayer2Alpha);
            _borderMaterial.SetFloat("_Layer3Width", theme.glowLayer3Width);
            _borderMaterial.SetFloat("_Layer3Alpha", theme.glowLayer3Alpha);
            _borderMaterial.SetFloat("_Layer4Width", theme.glowLayer4Width);
            _borderMaterial.SetFloat("_Layer4Alpha", theme.glowLayer4Alpha);
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
