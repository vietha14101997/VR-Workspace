using UnityEngine;

/// <summary>
/// Manages seamless flat planar visual for WorldPanelClusterRig.
/// Creates a single mesh with flat panel faces and curved folds at junctions.
/// Provides seamless appearance without per-panel visual artifacts.
///
/// Key difference from ClusterVisualCurved:
/// - NO content layer - individual panel boards remain visible and render content
/// - Only creates Background and Border layers using the flat planar mesh
/// </summary>
[ExecuteAlways]
public class ClusterVisualFlatPlanar : MonoBehaviour
{
    [Header("Mesh Settings")]
    [Tooltip("Segments per fold curve (higher = smoother fold)")]
    [SerializeField] private int foldSegments = 6;
    [Tooltip("Vertical segments")]
    [SerializeField] private int verticalSegments = 2;

    [Header("Visual Settings")]
    [SerializeField] private float cornerRadius = 0.04f;
    [SerializeField] private float edgePadding = 0.009f;
    [SerializeField] private float glowExpansion = 0.05f;


    [Header("Glass Colors")]
    [SerializeField] private Color glassColorA = new Color(0f, 0.55f, 0.65f, 0.35f);
    [SerializeField] private Color glassColorB = new Color(0.30f, 0.12f, 0.50f, 0.32f);
    [SerializeField] private float glassAlpha = 0.15f;
    [SerializeField] private float fresnelPower = 2.2f;
    [SerializeField] private float fresnelStrength = 0.12f;
    [SerializeField] private float gradientAngle = -10f;

    [Header("Glow Colors")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0.3f, 1f, 1f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1f, 0.4f, 1f, 1f);

    [Header("Glow Layers")]
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

    [Header("Z Offsets")]
    [SerializeField] private float backgroundZOffset = 0.002f;
    [SerializeField] private float borderZOffset = -0.002f;

    // Runtime references
    private WorldPanelClusterRig _clusterRig;
    private Mesh _flatPlanarMesh;
    private Mesh _expandedMesh;

    private GameObject _backgroundObject;
    private GameObject _borderObject;
    // NOTE: No _contentObject - boards render content directly!

    private MeshFilter _backgroundMeshFilter;
    private MeshFilter _borderMeshFilter;

    private MeshRenderer _backgroundRenderer;
    private MeshRenderer _borderRenderer;

    private Material _backgroundMaterial;
    private Material _borderMaterial;

    // Cached cluster parameters
    private int _cachedPanelCount = -1;
    private float _cachedPanelWidth;
    private float _cachedPanelHeight;
    private float _cachedArcRadius;

    #region Lifecycle

    void Awake()
    {
        _clusterRig = GetComponent<WorldPanelClusterRig>();
    }

    void OnEnable()
    {
        if (RTTManager.Instance != null)
        {
            RTTManager.Instance.OnThemeChanged += OnThemeChanged;
        }
    }

    void OnDisable()
    {
        if (RTTManager.Instance != null)
        {
            RTTManager.Instance.OnThemeChanged -= OnThemeChanged;
        }
    }

    void OnDestroy()
    {
        Cleanup();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Initialize the flat planar visual system.
    /// </summary>
    public void Initialize()
    {
        Debug.Log("[ClusterVisualFlatPlanar] Initialize() called");

        if (_clusterRig == null)
            _clusterRig = GetComponent<WorldPanelClusterRig>();

        if (_clusterRig == null || _clusterRig.panels.Count == 0)
        {
            Debug.LogWarning("[ClusterVisualFlatPlanar] No cluster rig or panels found");
            return;
        }

        Debug.Log($"[ClusterVisualFlatPlanar] Initializing for {_clusterRig.panels.Count} panels");

        Cleanup();
        GenerateMeshes();
        CreateVisualLayers();
        ApplyMaterials();

        Debug.Log("[ClusterVisualFlatPlanar] Initialize() completed successfully");
    }

    /// <summary>
    /// Rebuild the visual when cluster configuration changes.
    /// </summary>
    public void Rebuild()
    {
        Debug.Log("[ClusterVisualFlatPlanar] Rebuild() called");
        Initialize();
    }

    /// <summary>
    /// Apply theme colors from RTTManager.
    /// </summary>
    public void ApplyThemeColors()
    {
        var theme = RTTManager.Instance?.Theme;
        if (theme == null) return;

        glassColorA = theme.glassColorA;
        glassColorB = theme.glassColorB;
        glassAlpha = 0.15f; // Sync with RTTMenuFrame (0.15f) instead of theme.glassAlpha
        fresnelPower = theme.glassFresnelPower;
        fresnelStrength = theme.glassFresnelStrength;
        gradientAngle = theme.glassGradientAngle;

        glowColorA = theme.glowColorA;
        glowColorB = theme.glowColorB;

        layer1Width = theme.glowLayer1Width;
        layer1Alpha = theme.glowLayer1Alpha;
        layer2Width = theme.glowLayer2Width;
        layer2Alpha = theme.glowLayer2Alpha;
        layer3Width = theme.glowLayer3Width;
        layer3Alpha = theme.glowLayer3Alpha;
        layer4Width = theme.glowLayer4Width;
        layer4Alpha = theme.glowLayer4Alpha;

        ApplyMaterials();
    }

    /// <summary>
    /// Set glow colors.
    /// </summary>
    public void SetGlowColors(Color colorA, Color colorB)
    {
        glowColorA = colorA;
        glowColorB = colorB;
        ApplyBorderMaterial();
    }

    /// <summary>
    /// Set glass colors.
    /// </summary>
    public void SetGlassColors(Color colorA, Color colorB)
    {
        glassColorA = colorA;
        glassColorB = colorB;
        ApplyBackgroundMaterial();
    }

    #endregion

    #region Mesh Generation

    private void GenerateMeshes()
    {
        if (_clusterRig == null || _clusterRig.panels.Count == 0) return;

        // Get enabled panel count and reference panel
        int enabledCount = _clusterRig.GetEnabledPanelCount();
        if (enabledCount == 0)
        {
            Debug.LogWarning("[ClusterVisualFlatPlanar] GenerateMeshes: No enabled panels, skipping");
            return;
        }

        var enabledIndices = _clusterRig.GetEnabledPanelIndices();
        var panels = _clusterRig.panels;

        // Get reference panel from enabled panels (middle one)
        int refIndex = enabledIndices[enabledIndices.Count / 2];
        var refPanel = panels[refIndex];

        int panelCount = enabledCount;
        float panelWidth = refPanel.width;
        float panelHeight = refPanel.height;
        float arcRadius = _clusterRig.distanceFromCamera;

        Debug.Log($"[ClusterVisualFlatPlanar] GenerateMeshes: totalPanels={panels.Count}, enabledCount={enabledCount}, " +
            $"enabledIndices=[{string.Join(",", enabledIndices)}], panelWidth={panelWidth:F3}m");

        // IMPORTANT: For Flat Planar mode, use ZERO margins for mesh generation
        // This makes the visual mesh match full panel dimensions (not reduced by margins)
        // Panels are also positioned using full width, so edges touch edge-to-edge
        float meshMarginH = 0f;  // No horizontal margin reduction
        float meshMarginV = 0f;  // No vertical margin reduction

        // Generate main mesh using FlatPlanarMeshGenerator (full panel size)
        _flatPlanarMesh = FlatPlanarMeshGenerator.Generate(
            panelCount,
            panelWidth,
            panelHeight,
            arcRadius,
            foldSegments,
            verticalSegments,
            meshMarginH,
            meshMarginV
        );

        // Generate expanded mesh for border (full panel size + glow expansion)
        _expandedMesh = FlatPlanarMeshGenerator.GenerateWithGlowExpansion(
            panelCount,
            panelWidth,
            panelHeight,
            arcRadius,
            glowExpansion,
            foldSegments,
            verticalSegments,
            meshMarginH,
            meshMarginV
        );

        // Cache parameters
        _cachedPanelCount = panelCount;
        _cachedPanelWidth = panelWidth;
        _cachedPanelHeight = panelHeight;
        _cachedArcRadius = arcRadius;

        // Debug: Log mesh generation info (using full panel width, no margin reduction)
        float boardAngleRad = 2f * Mathf.Atan(panelWidth / 2f / arcRadius);
        float boardAngleDeg = boardAngleRad * Mathf.Rad2Deg;
        Debug.Log($"[ClusterVisualFlatPlanar] Mesh Gen - panelWidth: {panelWidth:F3}m, panelHeight: {panelHeight:F3}m, " +
            $"panelCount: {panelCount}, arcRadius: {arcRadius:F2}m, meshMarginH: {meshMarginH} (full width)");
        Debug.Log($"[ClusterVisualFlatPlanar] Yaw angle per panel: {boardAngleDeg:F1}°, foldSegments: {foldSegments}");
    }

    #endregion

    #region Visual Layer Creation

    private void CreateVisualLayers()
    {
        if (_expandedMesh == null)
        {
            Debug.LogError("[ClusterVisualFlatPlanar] CreateVisualLayers: _expandedMesh is null!");
            return;
        }

        Debug.Log($"[ClusterVisualFlatPlanar] CreateVisualLayers: expandedMesh.bounds={_expandedMesh.bounds}");

        // Mesh is already generated in ClusterRig local space matching exact panel positions
        // No offset needed - just place at origin with Z offsets for layering
        Vector3 noOffset = Vector3.zero;

        // Background layer (behind content boards)
        _backgroundObject = CreateLayerObject("ClusterBackground_FlatPlanar", backgroundZOffset, noOffset);
        _backgroundMeshFilter = _backgroundObject.GetComponent<MeshFilter>();
        _backgroundRenderer = _backgroundObject.GetComponent<MeshRenderer>();
        _backgroundMeshFilter.sharedMesh = _expandedMesh;

        // NOTE: No content layer - panel boards render content directly!
        // This is the key difference from ClusterVisualCurved

        // Border layer (in front of content boards)
        _borderObject = CreateLayerObject("ClusterBorder_FlatPlanar", borderZOffset, noOffset);
        _borderMeshFilter = _borderObject.GetComponent<MeshFilter>();
        _borderRenderer = _borderObject.GetComponent<MeshRenderer>();
        _borderMeshFilter.sharedMesh = _expandedMesh;

        Debug.Log($"[ClusterVisualFlatPlanar] Created visual layers: background={_backgroundObject.name}, border={_borderObject.name}");
    }

    /// <summary>
    /// Calculate offset to center mesh on actual panel positions (only enabled panels).
    /// The mesh is generated centered at angle=0, but panels might be offset (e.g., FixedThreeSlot mode).
    /// </summary>
    private Vector3 CalculatePanelCenterOffset()
    {
        if (_clusterRig == null || _clusterRig.panels.Count == 0)
            return Vector3.zero;

        var enabledIndices = _clusterRig.GetEnabledPanelIndices();
        if (enabledIndices.Count == 0)
            return Vector3.zero;

        var panels = _clusterRig.panels;

        // Calculate center of enabled panels only in local space
        Vector3 localCenter = Vector3.zero;
        foreach (int idx in enabledIndices)
        {
            var panel = panels[idx];
            if (panel != null)
            {
                // Get panel position relative to cluster rig
                Vector3 localPos = transform.InverseTransformPoint(panel.transform.position);
                localCenter += localPos;
            }
        }
        localCenter /= enabledIndices.Count;

        // The mesh is centered at origin, so offset it to match panel center
        // Only use X offset (horizontal centering), keep Y and Z at 0
        return new Vector3(localCenter.x, 0, 0);
    }

    private GameObject CreateLayerObject(string name, float zOffset, Vector3 centerOffset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(centerOffset.x, centerOffset.y, zOffset);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // Set layer to match parent
        go.layer = gameObject.layer;

        return go;
    }

    #endregion

    #region Material Application

    /// <summary>
    /// Calculate dimensions for shader.
    /// Uses unrolled width similar to arc length calculation.
    /// IMPORTANT: Flat Planar mode uses margin = 0 (full panel size), matching mesh generation.
    /// </summary>
    private void CalculateFlatPlanarDimensions(bool expanded, out float clusterWidth, out float clusterHeight)
    {
        // In Flat Planar mode, we use full panel width (margin = 0)
        // This matches the mesh generation in GenerateMeshes() where meshMarginH = 0
        float meshMarginH = 0f;

        
        float boardWidth = _cachedPanelWidth * (1f - 2f * meshMarginH); // = _cachedPanelWidth

        if (expanded)
        {
            // For expanded mesh, add glow expansion to board width per panel
            boardWidth += (glowExpansion * 2f / _cachedPanelCount);
        }

        // Calculate total unrolled width (panel widths + fold arc lengths)
        FlatPlanarMeshGenerator.CalculateClusterDimensions(
            _cachedPanelCount,
            expanded ? _cachedPanelWidth + (glowExpansion * 2f / _cachedPanelCount) : _cachedPanelWidth,
            _cachedPanelHeight,
            _cachedArcRadius,
            meshMarginH,  // Use 0 margin to match mesh generation
            out clusterWidth,
            out float _
        );

        // Height doesn't change (perpendicular to arc plane)
        // Use full panel height (no margin reduction)
        clusterHeight = expanded ? _cachedPanelHeight + glowExpansion * 2f : _cachedPanelHeight;
    }

    private void ApplyMaterials()
    {
        ApplyBackgroundMaterial();
        ApplyBorderMaterial();
    }

    private void ApplyBackgroundMaterial()
    {
        if (_backgroundRenderer == null)
        {
            Debug.LogError("[ClusterVisualFlatPlanar] ApplyBackgroundMaterial: _backgroundRenderer is null!");
            return;
        }

        // Reuse ClusterBackgroundCurved shader (compatible with flat planar UV mapping)
        var shader = Shader.Find("Custom/ClusterBackgroundCurved");
        if (shader == null)
        {
            Debug.LogError("[ClusterVisualFlatPlanar] ClusterBackgroundCurved shader not found! Using fallback.");
            // Try standard shader as fallback
            shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[ClusterVisualFlatPlanar] Even Standard shader not found!");
                return;
            }
        }

        if (_backgroundMaterial == null)
        {
            _backgroundMaterial = new Material(shader);
        }

        // Calculate dimensions (expanded mesh for background)
        CalculateFlatPlanarDimensions(expanded: true, out float clusterWidth, out float clusterHeight);

        _backgroundMaterial.SetFloat("_ClusterWidth", clusterWidth);
        _backgroundMaterial.SetFloat("_ClusterHeight", clusterHeight);
        _backgroundMaterial.SetFloat("_ArcRadius", _cachedArcRadius);

        _backgroundMaterial.SetFloat("_CornerRadius", cornerRadius);
        _backgroundMaterial.SetFloat("_EdgePadding", edgePadding);

        _backgroundMaterial.SetColor("_ColorA", glassColorA);
        _backgroundMaterial.SetColor("_ColorB", glassColorB);
        _backgroundMaterial.SetFloat("_GradientAngle", gradientAngle);
        _backgroundMaterial.SetFloat("_CyanRatio", 0.7f);

        _backgroundMaterial.SetFloat("_GlassAlpha", glassAlpha);
        _backgroundMaterial.SetFloat("_FresnelPower", fresnelPower);
        _backgroundMaterial.SetFloat("_FresnelStrength", fresnelStrength);

        // Flat Planar mode uses margin = 0 (full panel size)
        _backgroundMaterial.SetFloat("_MarginH", 0f);
        _backgroundMaterial.SetFloat("_MarginV", 0f);

        _backgroundMaterial.renderQueue = 2999;
        _backgroundRenderer.sharedMaterial = _backgroundMaterial;

        Debug.Log($"[ClusterVisualFlatPlanar] Background material applied: shader={_backgroundMaterial.shader.name}, " +
            $"clusterWidth={_backgroundMaterial.GetFloat("_ClusterWidth"):F2}, clusterHeight={_backgroundMaterial.GetFloat("_ClusterHeight"):F2}");
    }

    private void ApplyBorderMaterial()
    {
        if (_borderRenderer == null)
        {
            Debug.LogError("[ClusterVisualFlatPlanar] ApplyBorderMaterial: _borderRenderer is null!");
            return;
        }

        // Reuse ClusterBorderCurved shader
        var shader = Shader.Find("Custom/ClusterBorderCurved");
        if (shader == null)
        {
            Debug.LogWarning("[ClusterVisualFlatPlanar] ClusterBorderCurved shader not found");
            return;
        }

        if (_borderMaterial == null)
        {
            _borderMaterial = new Material(shader);
        }

        // Calculate dimensions (expanded mesh for border)
        CalculateFlatPlanarDimensions(expanded: true, out float clusterWidth, out float clusterHeight);

        _borderMaterial.SetFloat("_ClusterWidth", clusterWidth);
        _borderMaterial.SetFloat("_ClusterHeight", clusterHeight);

        _borderMaterial.SetFloat("_CornerRadius", cornerRadius);
        _borderMaterial.SetFloat("_EdgePadding", edgePadding);

        _borderMaterial.SetFloat("_Layer1Width", layer1Width);
        _borderMaterial.SetFloat("_Layer1Alpha", layer1Alpha);
        _borderMaterial.SetFloat("_Layer2Width", layer2Width);
        _borderMaterial.SetFloat("_Layer2Alpha", layer2Alpha);
        _borderMaterial.SetFloat("_Layer3Width", layer3Width);
        _borderMaterial.SetFloat("_Layer3Alpha", layer3Alpha);
        _borderMaterial.SetFloat("_Layer4Width", layer4Width);
        _borderMaterial.SetFloat("_Layer4Alpha", layer4Alpha);

        _borderMaterial.SetColor("_ColorA", glowColorA);
        _borderMaterial.SetColor("_ColorB", glowColorB);
        _borderMaterial.SetFloat("_GradientAngle", gradientAngle);

        _borderMaterial.SetFloat("_ShimmerSpeed", shimmerSpeed);
        _borderMaterial.SetFloat("_ShimmerIntensity", shimmerIntensity);
        _borderMaterial.SetFloat("_LightSize", 0.15f);

        _borderMaterial.renderQueue = 3001;
        _borderRenderer.sharedMaterial = _borderMaterial;

        Debug.Log($"[ClusterVisualFlatPlanar] Border material applied: shader={_borderMaterial.shader.name}");
    }

    #endregion

    #region Event Handlers

    private void OnThemeChanged()
    {
        ApplyThemeColors();
    }

    #endregion

    #region Cleanup

    private void Cleanup()
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

        // Cleanup meshes
        if (_flatPlanarMesh != null)
        {
            if (Application.isPlaying)
                Destroy(_flatPlanarMesh);
            else
                DestroyImmediate(_flatPlanarMesh);
            _flatPlanarMesh = null;
        }

        if (_expandedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(_expandedMesh);
            else
                DestroyImmediate(_expandedMesh);
            _expandedMesh = null;
        }

        // Cleanup objects
        if (_backgroundObject != null)
        {
            if (Application.isPlaying)
                Destroy(_backgroundObject);
            else
                DestroyImmediate(_backgroundObject);
            _backgroundObject = null;
        }

        if (_borderObject != null)
        {
            if (Application.isPlaying)
                Destroy(_borderObject);
            else
                DestroyImmediate(_borderObject);
            _borderObject = null;
        }

        _backgroundMeshFilter = null;
        _borderMeshFilter = null;
        _backgroundRenderer = null;
        _borderRenderer = null;

        _cachedPanelCount = -1;
    }

    #endregion
}
