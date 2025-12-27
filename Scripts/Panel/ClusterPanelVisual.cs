using UnityEngine;

/// <summary>
/// Visual component for panels in a WorldPanelCluster.
/// Creates glass background and glowing border using Panel shaders with EdgeMask support.
/// EdgeMask controls which edges show corners/padding/border for seamless cluster appearance.
/// Works with WorldPanelPlus's MeshRenderer-based approach.
/// </summary>
[ExecuteAlways]
public class ClusterPanelVisual : MonoBehaviour
{
    public enum PanelPosition
    {
        Single,     // Standalone panel - all edges visible
        First,      // First in cluster - hide right edge
        Middle,     // Middle in cluster - hide left and right edges
        Last        // Last in cluster - hide left edge
    }

    [Header("Panel Position")]
    [SerializeField] private PanelPosition _position = PanelPosition.Single;
    public PanelPosition Position
    {
        get => _position;
        set
        {
            _position = value;
            UpdateEdgeMask();
        }
    }

    [Header("Visual Settings")]
    [SerializeField] private float cornerRadius = 0.04f;
    [SerializeField] private float edgePadding = 0.009f;
    [SerializeField] private float borderWidth = 0.025f;

    [Header("Content Margin (ratio of panel size)")]
    [Tooltip("Margin ratio - content is inset by this fraction of panel dimensions")]
    [SerializeField] private float contentMarginHorizontal = 0.04f;
    [SerializeField] private float contentMarginVertical = 0.045f;

    [Header("Colors")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0.3f, 1f, 1f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1f, 0.4f, 1f, 1f);
    [SerializeField] private Color glassColorA = new Color(0f, 0.55f, 0.65f, 0.35f);
    [SerializeField] private Color glassColorB = new Color(0.30f, 0.12f, 0.50f, 0.32f);

    [Header("Glass Effect")]
    [SerializeField] private float glassAlpha = 0.65f;
    [SerializeField] private float fresnelStrength = 0.12f;
    [SerializeField] private float fresnelPower = 2.2f;
    [SerializeField] private float gradientAngle = -10f;

    [Header("Border Glow Layers (from RTTMenuFrame)")]
    [SerializeField] private float layer1Width = 0.01f;
    [SerializeField] private float layer1Alpha = 1.5f;
    [SerializeField] private float layer2Width = 0.02f;
    [SerializeField] private float layer2Alpha = 1.0f;
    [SerializeField] private float layer3Width = 0.045f;
    [SerializeField] private float layer3Alpha = 0.6f;
    [SerializeField] private float layer4Width = 0.09f;
    [SerializeField] private float layer4Alpha = 0.3f;

    [Header("Shimmer")]
    [SerializeField] private float shimmerSpeed = 0.4f;
    [SerializeField] private float shimmerIntensity = 0.2f;

    [Header("Layer Offsets")]
    [Tooltip("Z offset for background behind the board")]
    [SerializeField] private float backgroundZOffset = 0.001f;
    [Tooltip("Z offset for border in front of the board")]
    [SerializeField] private float borderZOffset = -0.001f;

    [Header("Glow Expansion")]
    [Tooltip("Extra size (in meters) added to quads for glow overflow")]
    [SerializeField] private float glowExpansion = 0.05f;

    // Runtime references
    private Material _backgroundMaterial;
    private Material _borderMaterial;
    private MeshRenderer _backgroundRenderer;
    private MeshRenderer _borderRenderer;
    private Transform _backgroundQuad;
    private Transform _borderQuad;
    private WorldPanelPlus _panel;

    // Cached edge mask
    private Vector4 _edgeMask = Vector4.one;

    #region Lifecycle

    void Awake()
    {
        _panel = GetComponent<WorldPanelPlus>();
    }

    void OnDestroy()
    {
        CleanupVisuals();
    }

    void OnValidate()
    {
        if (_backgroundMaterial != null || _borderMaterial != null)
        {
            UpdateEdgeMask();
            ApplyVisualSettings();
            ApplyContentMargins();
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Initialize visuals on the panel
    /// </summary>
    public void Initialize()
    {
        if (_panel == null) _panel = GetComponent<WorldPanelPlus>();
        if (_panel == null)
        {
            Debug.LogWarning("[ClusterPanelVisual] No WorldPanelPlus found on this object");
            return;
        }

        CreateBackgroundQuad();
        CreateBorderQuad();
        UpdateEdgeMask();
        ApplyVisualSettings();
        ApplyContentMargins();
    }

    /// <summary>
    /// Apply margins to the content Board quad so it's inset from the outer Background/Border
    /// </summary>
    private void ApplyContentMargins()
    {
        if (_panel == null || _panel.board == null) return;

        // Calculate content size with margins (inset from full panel size)
        float marginH = contentMarginHorizontal; // Horizontal margin ratio
        float marginV = contentMarginVertical;   // Vertical margin ratio

        // Content is smaller by margin on each side
        float contentWidth = _panel.width * (1f - 2f * marginH);
        float contentHeight = _panel.height * (1f - 2f * marginV);

        // Scale the board to the content size
        _panel.board.localScale = new Vector3(contentWidth, contentHeight, 1f);

        // Board stays at local position (0,0,0) - centered within the panel
        _panel.board.localPosition = Vector3.zero;
    }

    /// <summary>
    /// Configure position in cluster (updates EdgeMask automatically)
    /// </summary>
    public void SetClusterPosition(int index, int totalCount)
    {
        if (totalCount <= 1)
        {
            Position = PanelPosition.Single;
        }
        else if (index == 0)
        {
            Position = PanelPosition.First;
        }
        else if (index == totalCount - 1)
        {
            Position = PanelPosition.Last;
        }
        else
        {
            Position = PanelPosition.Middle;
        }
    }

    /// <summary>
    /// Set custom edge mask directly
    /// </summary>
    public void SetEdgeMask(Vector4 mask)
    {
        _edgeMask = mask;
        ApplyEdgeMask();
    }

    /// <summary>
    /// Update glow colors dynamically
    /// </summary>
    public void SetGlowColors(Color colorA, Color colorB)
    {
        glowColorA = colorA;
        glowColorB = colorB;
        ApplyVisualSettings();
    }

    /// <summary>
    /// Update glass colors dynamically
    /// </summary>
    public void SetGlassColors(Color colorA, Color colorB)
    {
        glassColorA = colorA;
        glassColorB = colorB;
        ApplyVisualSettings();
    }

    /// <summary>
    /// Update corner and edge settings
    /// </summary>
    public void SetCornerSettings(float radius, float padding)
    {
        cornerRadius = radius;
        edgePadding = padding;
        ApplyVisualSettings();
    }

    /// <summary>
    /// Update glow expansion (extra quad size for glow overflow)
    /// </summary>
    public void SetGlowExpansion(float expansion)
    {
        glowExpansion = expansion;
        UpdateSize();
    }

    /// <summary>
    /// Update content margins (ratio of panel size)
    /// </summary>
    public void SetContentMargins(float horizontal, float vertical)
    {
        contentMarginHorizontal = horizontal;
        contentMarginVertical = vertical;
        ApplyContentMargins();
    }

    /// <summary>
    /// Rebuild all visuals
    /// </summary>
    public void Rebuild()
    {
        CleanupVisuals();
        Initialize();
    }

    /// <summary>
    /// Update visual size to match panel
    /// </summary>
    public void UpdateSize()
    {
        if (_panel == null) return;

        // Calculate expanded size based on EdgeMask
        // Only expand outward on visible edges to avoid overlap with neighbors
        CalculateExpandedQuadParams(out Vector3 scale, out Vector3 offset);

        if (_backgroundQuad != null)
        {
            _backgroundQuad.localScale = scale;
            _backgroundQuad.localPosition = new Vector3(offset.x, offset.y, backgroundZOffset);
        }

        if (_borderQuad != null)
        {
            _borderQuad.localScale = scale;
            _borderQuad.localPosition = new Vector3(offset.x, offset.y, borderZOffset);
        }

        ApplyVisualSettings();
        ApplyContentMargins();
    }

    /// <summary>
    /// Calculate expanded quad size and position offset based on EdgeMask
    /// </summary>
    private void CalculateExpandedQuadParams(out Vector3 scale, out Vector3 offset)
    {
        float baseWidth = _panel != null ? _panel.width : 1f;
        float baseHeight = _panel != null ? _panel.height : 1f;

        // Expansion per edge (only expand on visible edges)
        float expandLeft = _edgeMask.x > 0.5f ? glowExpansion : 0f;
        float expandRight = _edgeMask.y > 0.5f ? glowExpansion : 0f;
        float expandTop = _edgeMask.z > 0.5f ? glowExpansion : 0f;
        float expandBottom = _edgeMask.w > 0.5f ? glowExpansion : 0f;

        // Total size with expansion
        float totalWidth = baseWidth + expandLeft + expandRight;
        float totalHeight = baseHeight + expandTop + expandBottom;

        // Position offset to keep centered on content
        // Positive offset shifts quad in that direction
        float offsetX = (expandRight - expandLeft) / 2f;
        float offsetY = (expandTop - expandBottom) / 2f;

        scale = new Vector3(totalWidth, totalHeight, 1f);
        offset = new Vector3(offsetX, offsetY, 0f);
    }

    #endregion

    #region Visual Creation

    private void CreateBackgroundQuad()
    {
        if (_panel == null || _panel.board == null) return;

        // Calculate expanded size
        CalculateExpandedQuadParams(out Vector3 scale, out Vector3 offset);

        // Create quad behind the board
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "ClusterBackground";
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(offset.x, offset.y, backgroundZOffset);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;

        // Remove default collider
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        _backgroundQuad = go.transform;
        _backgroundRenderer = go.GetComponent<MeshRenderer>();

        // Create material
        var shader = Shader.Find("Custom/GlassGradientBackgroundPanel");
        if (shader != null)
        {
            _backgroundMaterial = new Material(shader);
            _backgroundRenderer.sharedMaterial = _backgroundMaterial;
            _backgroundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _backgroundRenderer.receiveShadows = false;
        }
        else
        {
            Debug.LogWarning("[ClusterPanelVisual] GlassGradientBackgroundPanel shader not found");
            // Fallback to transparent color
            var fallbackShader = Shader.Find("Unlit/Transparent");
            if (fallbackShader != null)
            {
                _backgroundMaterial = new Material(fallbackShader);
                _backgroundMaterial.color = new Color(0f, 0.4f, 0.5f, 0.35f);
                _backgroundRenderer.sharedMaterial = _backgroundMaterial;
            }
        }

        // Set render queue behind board
        if (_backgroundMaterial != null)
        {
            _backgroundMaterial.renderQueue = 2999;
        }
    }

    private void CreateBorderQuad()
    {
        if (_panel == null || _panel.board == null) return;

        // Calculate expanded size
        CalculateExpandedQuadParams(out Vector3 scale, out Vector3 offset);

        // Create quad in front of the board for border
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "ClusterBorder";
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(offset.x, offset.y, borderZOffset);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;

        // Remove default collider
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        _borderQuad = go.transform;
        _borderRenderer = go.GetComponent<MeshRenderer>();

        // Create material
        var shader = Shader.Find("Custom/GlowingGlassBorderPanel");
        if (shader != null)
        {
            _borderMaterial = new Material(shader);
            // Disable glass in border immediately - critical to avoid square artifact
            _borderMaterial.SetFloat("_GlassAlpha", 0f);
            _borderRenderer.sharedMaterial = _borderMaterial;
            _borderRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _borderRenderer.receiveShadows = false;
        }
        else
        {
            Debug.LogWarning("[ClusterPanelVisual] GlowingGlassBorderPanel shader not found");
        }

        // Set render queue in front of board
        if (_borderMaterial != null)
        {
            _borderMaterial.renderQueue = 3001;
        }
    }

    #endregion

    #region EdgeMask Logic

    private void UpdateEdgeMask()
    {
        // EdgeMask: (Left, Right, Top, Bottom)
        // 1 = show edge (corner, padding, border)
        // 0 = hide edge (extend to boundary, no border)

        switch (_position)
        {
            case PanelPosition.Single:
                _edgeMask = new Vector4(1, 1, 1, 1); // All edges visible
                break;

            case PanelPosition.First:
                _edgeMask = new Vector4(1, 0, 1, 1); // Hide right edge
                break;

            case PanelPosition.Middle:
                _edgeMask = new Vector4(0, 0, 1, 1); // Hide left and right edges
                break;

            case PanelPosition.Last:
                _edgeMask = new Vector4(0, 1, 1, 1); // Hide left edge
                break;
        }

        ApplyEdgeMask();
    }

    private void ApplyEdgeMask()
    {
        if (_backgroundMaterial != null)
        {
            _backgroundMaterial.SetVector("_EdgeMask", _edgeMask);
        }

        if (_borderMaterial != null)
        {
            _borderMaterial.SetVector("_EdgeMask", _edgeMask);
        }
    }

    #endregion

    #region Visual Settings

    private void ApplyVisualSettings()
    {
        // Use the expanded quad's aspect ratio, not the original panel's
        // This ensures shader calculations align with actual quad geometry
        CalculateExpandedQuadParams(out Vector3 scale, out _);
        float aspect = scale.x / scale.y;

        ApplyBackgroundSettings(aspect);
        ApplyBorderSettings(aspect);
    }

    private void ApplyBackgroundSettings(float aspect)
    {
        if (_backgroundMaterial == null) return;

        _backgroundMaterial.SetFloat("_CornerRadius", cornerRadius);
        _backgroundMaterial.SetFloat("_EdgePadding", edgePadding);
        _backgroundMaterial.SetFloat("_Aspect", aspect);
        _backgroundMaterial.SetVector("_EdgeMask", _edgeMask);

        // Glass colors (matching RTTMenuFrame)
        _backgroundMaterial.SetColor("_ColorA", glassColorA);
        _backgroundMaterial.SetColor("_ColorB", glassColorB);

        // Glass effect settings (matching RTTMenuFrame)
        _backgroundMaterial.SetFloat("_GlassAlpha", glassAlpha);
        _backgroundMaterial.SetFloat("_FresnelPower", fresnelPower);
        _backgroundMaterial.SetFloat("_FresnelStrength", fresnelStrength);
        _backgroundMaterial.SetFloat("_GradientOffset", 0f);
        _backgroundMaterial.SetFloat("_GradientAngle", gradientAngle);
        _backgroundMaterial.SetFloat("_CyanRatio", 0.7f);
    }

    private void ApplyBorderSettings(float aspect)
    {
        if (_borderMaterial == null) return;

        _borderMaterial.SetFloat("_CornerRadius", cornerRadius);
        _borderMaterial.SetFloat("_EdgePadding", edgePadding);
        _borderMaterial.SetFloat("_BorderWidth", borderWidth);
        _borderMaterial.SetFloat("_Aspect", aspect);
        _borderMaterial.SetVector("_EdgeMask", _edgeMask);

        // Glow colors (matching RTTMenuFrame)
        _borderMaterial.SetColor("_ColorA", glowColorA);
        _borderMaterial.SetColor("_ColorB", glowColorB);

        // Glow layer widths and alphas (matching RTTMenuFrame)
        _borderMaterial.SetFloat("_Layer1Width", layer1Width);
        _borderMaterial.SetFloat("_Layer1Alpha", layer1Alpha);
        _borderMaterial.SetFloat("_Layer2Width", layer2Width);
        _borderMaterial.SetFloat("_Layer2Alpha", layer2Alpha);
        _borderMaterial.SetFloat("_Layer3Width", layer3Width);
        _borderMaterial.SetFloat("_Layer3Alpha", layer3Alpha);
        _borderMaterial.SetFloat("_Layer4Width", layer4Width);
        _borderMaterial.SetFloat("_Layer4Alpha", layer4Alpha);

        // Disable glass in border - already handled by ClusterBackground
        _borderMaterial.SetFloat("_GlassAlpha", 0f);
        _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 0f));
        _borderMaterial.SetFloat("_GradientAngle", gradientAngle);
    }

    #endregion

    #region Cleanup

    private void CleanupVisuals()
    {
        // Cleanup materials
        if (_backgroundMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_backgroundMaterial);
            else
                DestroyImmediate(_backgroundMaterial);
            _backgroundMaterial = null;
        }

        if (_borderMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_borderMaterial);
            else
                DestroyImmediate(_borderMaterial);
            _borderMaterial = null;
        }

        // Cleanup quads
        if (_backgroundQuad != null)
        {
            if (Application.isPlaying)
                Destroy(_backgroundQuad.gameObject);
            else
                DestroyImmediate(_backgroundQuad.gameObject);
            _backgroundQuad = null;
        }

        if (_borderQuad != null)
        {
            if (Application.isPlaying)
                Destroy(_borderQuad.gameObject);
            else
                DestroyImmediate(_borderQuad.gameObject);
            _borderQuad = null;
        }

        _backgroundRenderer = null;
        _borderRenderer = null;
    }

    #endregion
}
