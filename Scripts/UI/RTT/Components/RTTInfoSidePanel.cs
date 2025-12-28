using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRWorkspace.Streaming;

/// <summary>
/// Side panel for displaying hardware or network info.
/// Creates a world-space glass panel with text content.
/// Uses same visual styling as RTTMenuFrame for consistency.
/// Used by RTTRemoteMenu to show server info when connected.
/// </summary>
public class RTTInfoSidePanel : MonoBehaviour
{
    public enum PanelType
    {
        HardwareInfo,
        NetworkInfo
    }

    [Header("Panel Type")]
    [SerializeField] private PanelType panelType = PanelType.HardwareInfo;

    [Header("Visual Settings")]
    [SerializeField] private float panelWidth = 0.53f;   // 1/3 of main panel
    [SerializeField] private float panelHeight = 0.9f;   // Same height as main panel
    [SerializeField] private Color themeColor = new Color(0f, 0.9f, 1f);
    [SerializeField] private Color accentColor = new Color(0.8f, 0.4f, 1f);
    [SerializeField] private TMP_FontAsset customFont;

    [Header("Content Settings")]
    [SerializeField] private int titleFontSize = 48;
    [SerializeField] private int labelFontSize = 36;
    [SerializeField] private int valueFontSize = 40;
    [SerializeField] private float lineSpacing = 80f; // Increased for better readability

    [Header("Floating Data Effect")]
    [SerializeField] private bool enableFloatingData = true;
    [SerializeField] private int particleCount = 12;

    // Runtime references
    private RenderTexture _renderTexture;
    private Camera _uiCamera;
    private Canvas _canvas;
    private MeshRenderer _displayQuad;
    private Material _glassMaterial;
    private Material _borderMaterial;
    private GameObject _contentRoot;
    private Sprite _pixelSprite;
    private Sprite _roundedMaskSprite;

    // Speed test UI references (for live updates)
    private System.Collections.Generic.Dictionary<string, TextMeshProUGUI> _valueTexts = new System.Collections.Generic.Dictionary<string, TextMeshProUGUI>();

    // Resolution for RTT (calculated from panel dimensions)
    private int _rttWidth = 640;
    private int _rttHeight = 1080;

    #region Public Methods
    /// <summary>
    /// Initialize the side panel with size matching main panel.
    /// </summary>
    public void Initialize(PanelType type, Color theme, Color accent, TMP_FontAsset font,
        float width = 0.53f, float height = 0.9f)
    {
        Debug.Log($"[RTTInfoSidePanel] Initialize START for {type}, name={gameObject.name}");

        panelType = type;
        themeColor = theme;
        accentColor = accent;
        customFont = font;
        panelWidth = width;
        panelHeight = height;

        // Calculate RTT resolution maintaining aspect ratio
        float aspect = width / height;
        _rttHeight = 1080;
        _rttWidth = Mathf.RoundToInt(_rttHeight * aspect);

        Debug.Log($"[RTTInfoSidePanel] Creating RTT: {_rttWidth}x{_rttHeight}");

        CreateRenderTexture();
        SetupUICamera();
        SetupCanvas();  // Creates GlassPanel with FX_DataStream and GlowingBorder
        SetupDisplayQuad();

        Debug.Log($"[RTTInfoSidePanel] Initialize setup done, _contentRoot null: {_contentRoot == null}, _displayQuad null: {_displayQuad == null}");

        // Initial render to texture
        MarkDirty();

        gameObject.SetActive(false); // Hidden by default
        Debug.Log($"[RTTInfoSidePanel] Initialize DONE for {type}");
    }

    /// <summary>
    /// Show panel with loading state (before data arrives).
    /// </summary>
    public void ShowWithLoadingState()
    {
        Debug.Log($"[RTTInfoSidePanel] ShowWithLoadingState START for {panelType}, gameObject={gameObject.name}, active={gameObject.activeSelf}");

        if (_contentRoot == null)
        {
            Debug.LogError($"[RTTInfoSidePanel] ShowWithLoadingState: _contentRoot is NULL for {panelType}! Reinitializing canvas...");
            // Try to reinitialize if _contentRoot is missing
            return;
        }

        gameObject.SetActive(true);
        Debug.Log($"[RTTInfoSidePanel] SetActive(true) done for {panelType}");

        ClearContent();
        Debug.Log($"[RTTInfoSidePanel] ClearContent done for {panelType}, children before adding: {_contentRoot.transform.childCount}");

        if (panelType == PanelType.HardwareInfo)
        {
            AddTitle("SERVER INFO");
            AddInfoRow("Device", "Loading...");
            AddInfoRow("CPU", "...");
            AddInfoRow("VGA", "...");
            AddInfoRow("RAM", "...");
            AddInfoRow("OS", "...");
        }
        else
        {
            AddTitle("NETWORK INFO");
            AddInfoRow("Ping", "...");
            AddInfoRow("Jitter", "...");
            AddInfoRow("Bandwidth", "...");
            AddInfoRow("Type", "...");
            AddInfoRow("Quality", "...");
        }

        Debug.Log($"[RTTInfoSidePanel] Content added for {panelType}, children: {_contentRoot.transform.childCount}");

        MarkDirty();
        Debug.Log($"[RTTInfoSidePanel] ShowWithLoadingState DONE for {panelType}, children: {_contentRoot.transform.childCount}, displayQuad active: {_displayQuad?.gameObject.activeSelf}");
    }

    /// <summary>
    /// Show panel with hardware info data.
    /// </summary>
    public void SetHardwareInfo(ServerHardwareInfo info)
    {
        Debug.Log($"[RTTInfoSidePanel] SetHardwareInfo called, info null: {info == null}, panelType: {panelType}");

        if (info == null)
        {
            Debug.LogWarning("[RTTInfoSidePanel] SetHardwareInfo: info is null");
            return;
        }
        if (panelType != PanelType.HardwareInfo)
        {
            Debug.LogWarning($"[RTTInfoSidePanel] SetHardwareInfo: wrong panel type {panelType}");
            return;
        }

        gameObject.SetActive(true);
        ClearContent();

        AddTitle("SERVER INFO");
        AddInfoRow("Device", info.deviceName ?? "Unknown");
        AddInfoRow("CPU", info.processor ?? "Unknown");
        // VGA + VRAM combined
        string gpuInfo = $"{info.gpu ?? "Unknown"} - {info.gpuVramGB}GB";
        AddInfoRow("VGA", gpuInfo);
        AddInfoRow("RAM", $"{info.ramGB} GB");
        AddInfoRow("OS", info.os ?? "Unknown");

        MarkDirty();
        Debug.Log($"[RTTInfoSidePanel] SetHardwareInfo completed: {info.deviceName}");
    }

    /// <summary>
    /// Show panel with network info data.
    /// </summary>
    public void SetNetworkInfo(NetworkTestResult info)
    {
        Debug.Log($"[RTTInfoSidePanel] SetNetworkInfo called, info null: {info == null}, panelType: {panelType}, _contentRoot null: {_contentRoot == null}");

        if (info == null)
        {
            Debug.LogWarning("[RTTInfoSidePanel] SetNetworkInfo: info is null");
            return;
        }
        if (panelType != PanelType.NetworkInfo)
        {
            Debug.LogWarning($"[RTTInfoSidePanel] SetNetworkInfo: wrong panel type {panelType}");
            return;
        }
        if (_contentRoot == null)
        {
            Debug.LogError($"[RTTInfoSidePanel] SetNetworkInfo: _contentRoot is NULL!");
            return;
        }

        gameObject.SetActive(true);
        Debug.Log($"[RTTInfoSidePanel] SetNetworkInfo: SetActive done, clearing content");

        ClearContent();
        Debug.Log($"[RTTInfoSidePanel] SetNetworkInfo: ClearContent done, children: {_contentRoot.transform.childCount}");

        AddTitle("NETWORK INFO");
        AddInfoRow("Ping", $"{info.pingMs:F1} ms");
        AddInfoRow("Jitter", $"{info.jitterMs:F1} ms");
        AddInfoRow("Bandwidth", $"{info.bandwidthMbps:F0} Mbps");
        AddInfoRow("Type", info.connectionType ?? "Unknown");

        // Add quality indicator
        string quality = GetNetworkQuality(info);
        AddInfoRow("Quality", quality, GetQualityColor(quality));

        Debug.Log($"[RTTInfoSidePanel] SetNetworkInfo: content added, children: {_contentRoot.transform.childCount}");

        MarkDirty();
        Debug.Log($"[RTTInfoSidePanel] SetNetworkInfo DONE: {info.pingMs:F1}ms, children: {_contentRoot.transform.childCount}");
    }
    #endregion

    #region Setup Methods
    private void CreateRenderTexture()
    {
        _renderTexture = new RenderTexture(_rttWidth, _rttHeight, 24, RenderTextureFormat.ARGB32);
        _renderTexture.antiAliasing = 4;
        _renderTexture.filterMode = FilterMode.Bilinear;
        _renderTexture.Create();
    }

    private void SetupUICamera()
    {
        GameObject camObj = new GameObject("SidePanelCamera");
        camObj.transform.SetParent(transform, false);
        camObj.transform.localPosition = new Vector3(0, 0, -10);

        _uiCamera = camObj.AddComponent<Camera>();
        _uiCamera.clearFlags = CameraClearFlags.SolidColor;
        _uiCamera.backgroundColor = new Color(0, 0, 0, 0);
        _uiCamera.orthographic = true;
        _uiCamera.orthographicSize = _rttHeight / 2f;
        _uiCamera.nearClipPlane = 0.1f;
        _uiCamera.farClipPlane = 100f;
        _uiCamera.targetTexture = _renderTexture;
        _uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        _uiCamera.depth = -30; // Lower depth to avoid conflicts
        _uiCamera.enabled = false; // Disable continuous rendering - we render manually
    }

    private void SetupCanvas()
    {
        try
        {
            Debug.Log($"[RTTInfoSidePanel] SetupCanvas START for {panelType}");

            // Create a container for RTT rendering - positioned far from main scene
            // to avoid interference with other UI cameras
            GameObject rttContainer = new GameObject("RTTContainer");
            rttContainer.transform.SetParent(transform, false);
            // Position far away from main scene to avoid culling conflicts
            float uniqueOffset = GetInstanceID() % 10000;
            rttContainer.transform.localPosition = new Vector3(uniqueOffset, 10000, 0);

            // Camera follows container
            _uiCamera.transform.SetParent(rttContainer.transform, false);
            _uiCamera.transform.localPosition = new Vector3(0, 0, -10);

            GameObject canvasObj = new GameObject("Canvas");
            canvasObj.transform.SetParent(rttContainer.transform, false);
            canvasObj.layer = LayerMask.NameToLayer("UI");

            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = _uiCamera;

            RectTransform canvasRT = _canvas.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(_rttWidth, _rttHeight);
            canvasRT.localPosition = Vector3.zero;
            canvasRT.localScale = Vector3.one;

            canvasObj.AddComponent<GraphicRaycaster>();

            // Create GlassBackground (like RTTMenuFrame) - contains FX_DataStream and GlowingBorder
            CreateGlassPanel(canvasRT);

            // Content container (sibling of GlassBackground, like RTTMenuFrame)
            _contentRoot = new GameObject("ContentContainer");
            _contentRoot.transform.SetParent(canvasObj.transform, false);
            _contentRoot.layer = LayerMask.NameToLayer("UI");

            RectTransform contentRT = _contentRoot.AddComponent<RectTransform>();
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.offsetMin = new Vector2(40, 40);
            contentRT.offsetMax = new Vector2(-40, -40);

            // Add vertical layout
            var layout = _contentRoot.AddComponent<VerticalLayoutGroup>();
            layout.spacing = lineSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(20, 20, 40, 40);

            Debug.Log($"[RTTInfoSidePanel] SetupCanvas DONE for {panelType}, _contentRoot: {_contentRoot != null}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RTTInfoSidePanel] SetupCanvas EXCEPTION for {panelType}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void CreateGlassPanel(RectTransform parent)
    {
        float w = _rttWidth;
        float h = _rttHeight;
        float aspect = (float)_rttWidth / _rttHeight;
        float edgePad = 0.0075f;
        float bgCornerRadius = 0.04f;

        // GlassBackground - exactly like RTTMenuFrame
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        bgObj.layer = LayerMask.NameToLayer("UI");

        Image img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = true;

        // Use glass shader like RTTMenuFrame
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            _glassMaterial = new Material(glassShader);

            _glassMaterial.SetFloat("_CornerRadius", bgCornerRadius);
            _glassMaterial.SetFloat("_EdgePadding", edgePad);
            _glassMaterial.SetFloat("_Aspect", aspect);

            // Color based on panel type
            Color colorA, colorB;
            if (panelType == PanelType.HardwareInfo)
            {
                colorA = new Color(0.0f, 0.55f, 0.70f, 0.35f);
                colorB = new Color(0.0f, 0.45f, 0.60f, 0.32f);
            }
            else
            {
                colorA = new Color(0.35f, 0.15f, 0.55f, 0.35f);
                colorB = new Color(0.28f, 0.10f, 0.45f, 0.32f);
            }
            _glassMaterial.SetColor("_ColorA", colorA);
            _glassMaterial.SetColor("_ColorB", colorB);
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
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();

        // Create floating data effects as child of GlassBackground (like RTTMenuFrame)
        if (enableFloatingData)
        {
            CreateFloatingDataEffects(bgObj.transform, w, h);
        }

        // Create glowing border as child of GlassBackground (like RTTMenuFrame)
        CreateGlowingBorder2D(bgObj.transform, w, h, edgePad * 1.175f);
    }

    private void CreateFloatingDataEffects(Transform parent, float w, float h)
    {
        // Exactly like RTTMenuFrame
        GameObject fxContainer = new GameObject("FX_DataStream");
        fxContainer.transform.SetParent(parent, false);
        fxContainer.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = fxContainer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;

        float margin = 40f;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);

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
        p.layer = LayerMask.NameToLayer("UI");

        Image pImg = p.AddComponent<Image>();
        pImg.sprite = GetPixelSprite();
        pImg.raycastTarget = false;

        // Color based on panel type
        Color baseCol = (panelType == PanelType.HardwareInfo) ? Color.cyan : new Color(0.8f, 0f, 1f);
        pImg.color = new Color(baseCol.r, baseCol.g, baseCol.b, Random.Range(0.1f, 0.4f));

        RectTransform pRT = p.GetComponent<RectTransform>();
        float size = Random.Range(10f, 60f);
        pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f));

        float startX = Random.Range(-w / 2f, w / 2f);
        float startY = Random.Range(-h / 2f, h / 2f);
        pRT.anchoredPosition = new Vector2(startX, startY);

        var anim = p.AddComponent<FloatingDataAnim>();
        anim.speed = Random.Range(10f, 40f);
        anim.range = new Vector2(w - 80f, h - 80f);
        anim.OnAnimationUpdate += MarkDirty;
    }

    private void CreateGlowingBorder2D(Transform parent, float w, float h, float edgePad)
    {
        // 2D border like RTTMenuFrame (not 3D quad)
        GameObject borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);
        borderObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;
        borderImg.sprite = GetPixelSprite();

        float aspect = (float)_rttWidth / _rttHeight;

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);

            _borderMaterial.SetFloat("_StrokeEnabled", 0);
            _borderMaterial.SetFloat("_BorderWidth", 0.025f);
            _borderMaterial.SetFloat("_CornerRadius", 0.04f);
            _borderMaterial.SetFloat("_EdgePadding", edgePad);
            _borderMaterial.SetFloat("_Aspect", aspect);

            // Glow layers like RTTMenuFrame
            _borderMaterial.SetFloat("_Layer1Width", 0.01f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
            _borderMaterial.SetFloat("_Layer2Width", 0.02f);
            _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
            _borderMaterial.SetFloat("_Layer3Width", 0.045f);
            _borderMaterial.SetFloat("_Layer3Alpha", 0.6f);
            _borderMaterial.SetFloat("_Layer4Width", 0.09f);
            _borderMaterial.SetFloat("_Layer4Alpha", 0.3f);

            // Border colors based on panel type
            Color borderColorA, borderColorB;
            if (panelType == PanelType.HardwareInfo)
            {
                borderColorA = new Color(0.3f, 1f, 1f, 1f);
                borderColorB = new Color(0.2f, 0.9f, 0.95f, 1f);
            }
            else
            {
                borderColorA = new Color(1f, 0.4f, 1f, 1f);
                borderColorB = new Color(0.9f, 0.3f, 0.95f, 1f);
            }
            _borderMaterial.SetColor("_ColorA", borderColorA);
            _borderMaterial.SetColor("_ColorB", borderColorB);
            _borderMaterial.SetFloat("_GradientMode", 2f);
            _borderMaterial.SetFloat("_GradientAngle", -10f);
            _borderMaterial.SetFloat("_GlassAlpha", 0.02f);
            _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));

            borderImg.material = _borderMaterial;
        }

        borderObj.transform.SetAsLastSibling();
    }

    private void SetupDisplayQuad()
    {
        // Simple quad to display the render texture (glass effect is in canvas now)
        GameObject quadObj = new GameObject("DisplayQuad");
        quadObj.transform.SetParent(transform, false);
        quadObj.transform.localPosition = Vector3.zero;
        quadObj.layer = LayerMask.NameToLayer("VirtualObjects");

        MeshFilter meshFilter = quadObj.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateQuadMesh();

        _displayQuad = quadObj.AddComponent<MeshRenderer>();

        // Simple unlit material to display the RTT
        Shader unlitShader = Shader.Find("Unlit/Transparent");
        if (unlitShader == null) unlitShader = Shader.Find("UI/Default");

        Material displayMat = new Material(unlitShader);
        displayMat.mainTexture = _renderTexture;

        _displayQuad.material = displayMat;
        _displayQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _displayQuad.receiveShadows = false;

        // Add collider for interaction
        BoxCollider collider = quadObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(panelWidth, panelHeight, 0.01f);
    }

    private Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();

        float hw = panelWidth / 2f;
        float hh = panelHeight / 2f;

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-hw, -hh, 0),
            new Vector3(hw, -hh, 0),
            new Vector3(-hw, hh, 0),
            new Vector3(hw, hh, 0)
        };

        Vector2[] uvs = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        int[] triangles = new int[] { 0, 2, 1, 2, 3, 1 };

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }
    #endregion

    #region Content Building
    private void ClearContent()
    {
        if (_contentRoot == null) return;

        foreach (Transform child in _contentRoot.transform)
        {
            Destroy(child.gameObject);
        }
    }

    private void AddTitle(string title)
    {
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(_contentRoot.transform, false);
        titleObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = titleObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, titleFontSize + 20);

        TextMeshProUGUI txt = titleObj.AddComponent<TextMeshProUGUI>();
        txt.text = title;
        txt.fontSize = titleFontSize;
        txt.color = themeColor;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        if (customFont != null) txt.font = customFont;

        // Add separator line below title
        AddSeparator();
    }

    private void AddSeparator()
    {
        GameObject sepObj = new GameObject("Separator");
        sepObj.transform.SetParent(_contentRoot.transform, false);
        sepObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = sepObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 4);

        Image img = sepObj.AddComponent<Image>();
        img.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.5f);
        img.raycastTarget = false;

        var layoutElem = sepObj.AddComponent<LayoutElement>();
        layoutElem.preferredHeight = 4;
        layoutElem.flexibleWidth = 1;
    }

    private void AddInfoRow(string label, string value, Color? valueColor = null)
    {
        GameObject rowObj = new GameObject($"Row_{label}");
        rowObj.transform.SetParent(_contentRoot.transform, false);
        rowObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = rowObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, valueFontSize + 16);

        // Horizontal layout
        HorizontalLayoutGroup hlg = rowObj.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);
        labelObj.layer = LayerMask.NameToLayer("UI");

        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.sizeDelta = new Vector2(200, 0);

        TextMeshProUGUI labelTxt = labelObj.AddComponent<TextMeshProUGUI>();
        labelTxt.text = label;
        labelTxt.fontSize = labelFontSize;
        labelTxt.color = new Color(0.7f, 0.7f, 0.7f);
        labelTxt.alignment = TextAlignmentOptions.Left;
        labelTxt.raycastTarget = false;
        if (customFont != null) labelTxt.font = customFont;

        var labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;

        // Value
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(rowObj.transform, false);
        valueObj.layer = LayerMask.NameToLayer("UI");

        RectTransform valueRT = valueObj.AddComponent<RectTransform>();
        valueRT.sizeDelta = new Vector2(350, 0);

        TextMeshProUGUI valueTxt = valueObj.AddComponent<TextMeshProUGUI>();
        valueTxt.text = value;
        valueTxt.fontSize = valueFontSize;
        valueTxt.color = valueColor ?? Color.white;
        valueTxt.alignment = TextAlignmentOptions.Left;
        valueTxt.fontStyle = FontStyles.Bold;
        valueTxt.raycastTarget = false;
        valueTxt.enableWordWrapping = true;
        valueTxt.overflowMode = TextOverflowModes.Overflow;
        if (customFont != null) valueTxt.font = customFont;

        var valueLayout = valueObj.AddComponent<LayoutElement>();
        valueLayout.flexibleWidth = 1;

        // Store reference for live updates
        _valueTexts[label] = valueTxt;
    }

    /// <summary>
    /// Update speed test progress (call during speed test for live updates).
    /// </summary>
    public void UpdateSpeedTestProgress(string direction, double currentMbps, int progress)
    {
        if (panelType != PanelType.NetworkInfo) return;

        // Only handle bandwidth (download) - upload test removed
        if (direction != "bandwidth") return;

        string value = progress < 100
            ? $"{currentMbps:F1} Mbps ({progress}%)"
            : $"{currentMbps:F1} Mbps";

        if (_valueTexts.TryGetValue("Bandwidth", out var txt))
        {
            txt.text = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Show network panel with speed test loading state.
    /// </summary>
    public void ShowSpeedTestLoadingState()
    {
        if (panelType != PanelType.NetworkInfo) return;
        if (_contentRoot == null) return;

        gameObject.SetActive(true);
        ClearContent();
        _valueTexts.Clear();

        AddTitle("NETWORK INFO");
        AddInfoRow("Ping", "Measuring...");
        AddInfoRow("Jitter", "Waiting...");
        AddInfoRow("Bandwidth", "Waiting...");
        AddInfoRow("Quality", "Testing...");

        MarkDirty();
    }

    /// <summary>
    /// Update ping values immediately.
    /// </summary>
    public void UpdatePingValues(double pingMs, double jitterMs)
    {
        if (panelType != PanelType.NetworkInfo) return;

        if (_valueTexts.TryGetValue("Ping", out var pingTxt))
        {
            pingTxt.text = $"{pingMs:F1} ms";
        }
        if (_valueTexts.TryGetValue("Jitter", out var jitterTxt))
        {
            jitterTxt.text = $"{jitterMs:F1} ms";
        }
        MarkDirty();
    }

    private string GetNetworkQuality(NetworkTestResult info)
    {
        if (info.pingMs < 20 && info.bandwidthMbps > 100)
            return "Excellent";
        if (info.pingMs < 50 && info.bandwidthMbps > 50)
            return "Good";
        if (info.pingMs < 100 && info.bandwidthMbps > 20)
            return "Fair";
        return "Poor";
    }

    private Color GetQualityColor(string quality)
    {
        return quality switch
        {
            "Excellent" => new Color(0.2f, 1f, 0.4f),
            "Good" => new Color(0.5f, 1f, 0.3f),
            "Fair" => new Color(1f, 0.8f, 0.2f),
            _ => new Color(1f, 0.4f, 0.3f)
        };
    }

    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "N/A";
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength - 3) + "...";
    }
    #endregion

    #region Rendering
    private void MarkDirty()
    {
        if (_uiCamera != null)
        {
            _uiCamera.Render();
        }
        else
        {
            Debug.LogWarning($"[RTTInfoSidePanel] MarkDirty: _uiCamera is NULL for {panelType}!");
        }
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

    #region Cleanup
    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }

        if (_glassMaterial != null)
        {
            Destroy(_glassMaterial);
        }

        if (_borderMaterial != null)
        {
            Destroy(_borderMaterial);
        }
    }
    #endregion
}
