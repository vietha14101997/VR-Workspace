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
    [SerializeField] private float lineSpacing = 60f;

    // Runtime references
    private RenderTexture _renderTexture;
    private Camera _uiCamera;
    private Canvas _canvas;
    private MeshRenderer _displayQuad;
    private Material _glassMaterial;
    private Material _borderMaterial;
    private GameObject _contentRoot;

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

        CreateRenderTexture();
        SetupUICamera();
        SetupCanvas();
        SetupDisplayQuad();
        BuildContent();

        gameObject.SetActive(false); // Hidden by default
    }

    /// <summary>
    /// Show panel with hardware info data.
    /// </summary>
    public void SetHardwareInfo(ServerHardwareInfo info)
    {
        if (info == null || panelType != PanelType.HardwareInfo) return;

        ClearContent();

        AddTitle("SERVER INFO");
        AddInfoRow("Device", info.deviceName);
        AddInfoRow("CPU", TruncateText(info.processor, 24));
        AddInfoRow("GPU", TruncateText(info.gpu, 24));
        AddInfoRow("VRAM", $"{info.gpuVramGB} GB");
        AddInfoRow("RAM", $"{info.ramGB} GB");
        AddInfoRow("OS", TruncateText(info.os, 24));
        AddInfoRow("Encoder", info.encoderType ?? "N/A");

        MarkDirty();
    }

    /// <summary>
    /// Show panel with network info data.
    /// </summary>
    public void SetNetworkInfo(NetworkTestResult info)
    {
        if (info == null || panelType != PanelType.NetworkInfo) return;

        ClearContent();

        AddTitle("NETWORK INFO");
        AddInfoRow("Ping", $"{info.pingMs:F1} ms");
        AddInfoRow("Jitter", $"{info.jitterMs:F1} ms");
        AddInfoRow("Bandwidth", $"{info.bandwidthMbps:F0} Mbps");
        AddInfoRow("Type", info.connectionType ?? "Unknown");

        // Add quality indicator
        string quality = GetNetworkQuality(info);
        AddInfoRow("Quality", quality, GetQualityColor(quality));

        MarkDirty();
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

        // Content root with margins
        _contentRoot = new GameObject("ContentRoot");
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
    }

    private void SetupDisplayQuad()
    {
        GameObject quadObj = new GameObject("DisplayQuad");
        quadObj.transform.SetParent(transform, false);
        quadObj.transform.localPosition = Vector3.zero;
        quadObj.layer = LayerMask.NameToLayer("VirtualObjects");

        // Create quad mesh
        MeshFilter meshFilter = quadObj.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateQuadMesh();

        _displayQuad = quadObj.AddComponent<MeshRenderer>();

        // Use same glass shader as RTTMenuFrame
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader == null) glassShader = Shader.Find("UI/Default");

        _glassMaterial = new Material(glassShader);
        _glassMaterial.SetTexture("_MainTex", _renderTexture);

        float aspect = panelWidth / panelHeight;

        // Match RTTMenuFrame glass effect settings
        if (glassShader != null && glassShader.name.Contains("Glass"))
        {
            _glassMaterial.SetFloat("_CornerRadius", 0.04f);
            _glassMaterial.SetFloat("_EdgePadding", 0.0075f);
            _glassMaterial.SetFloat("_Aspect", aspect);

            // Same glass colors as RTTMenuFrame
            Color cyanDeepSeaBlue = new Color(0.0f, 0.55f, 0.65f, 0.35f);
            Color deepSeaBluePurple = new Color(0.30f, 0.12f, 0.50f, 0.32f);
            _glassMaterial.SetColor("_ColorA", cyanDeepSeaBlue);
            _glassMaterial.SetColor("_ColorB", deepSeaBluePurple);
            _glassMaterial.SetFloat("_GradientOffset", 0f);
            _glassMaterial.SetFloat("_GradientAngle", -10f);
            _glassMaterial.SetFloat("_CyanRatio", 0.7f);
            _glassMaterial.SetFloat("_GlassAlpha", 0.65f);
            _glassMaterial.SetFloat("_FresnelPower", 2.2f);
            _glassMaterial.SetFloat("_FresnelStrength", 0.12f);
        }

        _displayQuad.material = _glassMaterial;

        // Create glowing border (same as RTTMenuFrame)
        CreateGlowingBorder(quadObj.transform, aspect);

        // Add collider for interaction
        BoxCollider collider = quadObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(panelWidth, panelHeight, 0.01f);
    }

    private void CreateGlowingBorder(Transform parent, float aspect)
    {
        GameObject borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);
        borderObj.transform.localPosition = new Vector3(0, 0, -0.001f); // Slightly in front

        // Create border quad
        MeshFilter meshFilter = borderObj.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateQuadMesh();

        MeshRenderer borderRenderer = borderObj.AddComponent<MeshRenderer>();
        borderObj.layer = LayerMask.NameToLayer("VirtualObjects");

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);

            _borderMaterial.SetFloat("_StrokeEnabled", 0);
            // Border settings - match RTTMenuFrame
            _borderMaterial.SetFloat("_BorderWidth", 0.025f);
            _borderMaterial.SetFloat("_CornerRadius", 0.04f);
            _borderMaterial.SetFloat("_EdgePadding", 0.0075f * 1.175f);
            _borderMaterial.SetFloat("_Aspect", aspect);

            // Glow layer widths - same as RTTMenuFrame
            _borderMaterial.SetFloat("_Layer1Width", 0.01f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
            _borderMaterial.SetFloat("_Layer2Width", 0.02f);
            _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
            _borderMaterial.SetFloat("_Layer3Width", 0.045f);
            _borderMaterial.SetFloat("_Layer3Alpha", 0.6f);
            _borderMaterial.SetFloat("_Layer4Width", 0.09f);
            _borderMaterial.SetFloat("_Layer4Alpha", 0.3f);

            // Glow colors
            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
            _borderMaterial.SetColor("_ColorA", cyanColor);
            _borderMaterial.SetColor("_ColorB", purpleColor);
            _borderMaterial.SetFloat("_GradientMode", 2f);
            _borderMaterial.SetFloat("_GradientAngle", -10f);
            _borderMaterial.SetFloat("_GlassAlpha", 0.02f);
            _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            _borderMaterial.SetFloat("_ShimmerSpeed", 0.4f);
            _borderMaterial.SetFloat("_ShimmerIntensity", 0.2f);
            _borderMaterial.SetFloat("_LightSize", 0.008f);
            _borderMaterial.SetFloat("_LightGlow", 0.008f);

            borderRenderer.material = _borderMaterial;
        }
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
    private void BuildContent()
    {
        // Add glass background to canvas (for RTT rendering)
        AddGlassBackground();
    }

    private void AddGlassBackground()
    {
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(_canvas.transform, false);
        bgObj.transform.SetAsFirstSibling();
        bgObj.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = bgObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = bgObj.AddComponent<Image>();
        // Transparent background - the 3D quad provides the glass effect
        img.color = new Color(0, 0, 0, 0);
        img.raycastTarget = false;
    }

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
        valueTxt.enableWordWrapping = false;
        valueTxt.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont != null) valueTxt.font = customFont;

        var valueLayout = valueObj.AddComponent<LayoutElement>();
        valueLayout.flexibleWidth = 1;
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
