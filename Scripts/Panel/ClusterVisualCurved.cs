using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages seamless curved visual for WorldPanelClusterRig.
/// Creates a single curved mesh with unified background, border, and content layers.
/// Provides true seamless appearance without panel junctions.
/// </summary>
[ExecuteAlways]
public class ClusterVisualCurved : MonoBehaviour
{
    [Header("Mesh Settings")]
    [Tooltip("Horizontal segments per panel (higher = smoother curve)")]
    [SerializeField] private int segmentsPerPanel = 12;
    [Tooltip("Vertical segments")]
    [SerializeField] private int verticalSegments = 2;

    [Header("Visual Settings")]
    [SerializeField] private float cornerRadius = 0.04f;
    [SerializeField] private float edgePadding = 0.009f;
    [SerializeField] private float glowExpansion = 0.05f;
    [SerializeField] private float contentMarginH = 0.04f;
    [SerializeField] private float contentMarginV = 0.045f;
    [SerializeField] private float blendZoneWidth = 0f;

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
    private Mesh _curvedMesh;
    private Mesh _expandedMesh;

    private GameObject _backgroundObject;
    private GameObject _contentObject;
    private GameObject _borderObject;

    private MeshFilter _backgroundMeshFilter;
    private MeshFilter _contentMeshFilter;
    private MeshFilter _borderMeshFilter;

    private MeshRenderer _backgroundRenderer;
    private MeshRenderer _contentRenderer;
    private MeshRenderer _borderRenderer;

    private Material _backgroundMaterial;
    private Material _contentMaterial;
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

    void Update()
    {
        // Update content textures each frame (for streaming video)
        if (Application.isPlaying && _contentMaterial != null)
        {
            UpdateContentTextures();
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Initialize the curved visual system
    /// </summary>
    public void Initialize()
    {
        if (_clusterRig == null)
            _clusterRig = GetComponent<WorldPanelClusterRig>();

        if (_clusterRig == null || _clusterRig.panels.Count == 0)
        {
            Debug.LogWarning("[ClusterVisualCurved] No cluster rig or panels found");
            return;
        }

        Cleanup();
        GenerateMeshes();
        CreateVisualLayers();
        ApplyMaterials();
        UpdateContentTextures();
    }

    /// <summary>
    /// Rebuild the visual when cluster configuration changes
    /// </summary>
    public void Rebuild()
    {
        Debug.Log("[ClusterVisualCurved] Rebuild() called");
        Initialize();
    }

    /// <summary>
    /// Update content textures from panels (only enabled panels)
    /// </summary>
    public void UpdateContentTextures()
    {
        if (_contentMaterial == null || _clusterRig == null) return;

        var enabledIndices = _clusterRig.GetEnabledPanelIndices();
        var panels = _clusterRig.panels;

        // Clear all textures first
        for (int i = 0; i < 6; i++)
        {
            _contentMaterial.SetTexture($"_Content{i}", Texture2D.blackTexture);
        }

        // Set textures for enabled panels only
        for (int i = 0; i < enabledIndices.Count && i < 6; i++)
        {
            int panelIndex = enabledIndices[i];
            var panel = panels[panelIndex];
            if (panel != null)
            {
                // Get content texture directly from panel's contentTexture field
                // This is more reliable than reading from board renderer (which may be hidden)
                Texture contentTex = panel.contentTexture;
                _contentMaterial.SetTexture($"_Content{i}", contentTex ?? Texture2D.blackTexture);
            }
        }

        _contentMaterial.SetInt("_PanelCount", enabledIndices.Count);
    }

    /// <summary>
    /// Apply theme colors from RTTManager
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
    /// Set glow colors
    /// </summary>
    public void SetGlowColors(Color colorA, Color colorB)
    {
        glowColorA = colorA;
        glowColorB = colorB;
        ApplyBorderMaterial();
    }

    /// <summary>
    /// Set glass colors
    /// </summary>
    public void SetGlassColors(Color colorA, Color colorB)
    {
        glassColorA = colorA;
        glassColorB = colorB;
        ApplyBackgroundMaterial();
    }

    /// <summary>
    /// Set corner settings (radius and padding)
    /// </summary>
    public void SetCornerSettings(float radius, float padding)
    {
        cornerRadius = radius;
        edgePadding = padding;
        ApplyMaterials(); // Update all materials
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
            Debug.LogWarning("[ClusterVisualCurved] GenerateMeshes: No enabled panels, skipping");
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

        Debug.Log($"[ClusterVisualCurved] GenerateMeshes: totalPanels={panels.Count}, enabledCount={enabledCount}, " +
            $"enabledIndices=[{string.Join(",", enabledIndices)}], panelWidth={panelWidth:F3}m");

        // Generate main mesh
        _curvedMesh = CurvedClusterMeshGenerator.Generate(
            panelCount,
            panelWidth,
            panelHeight,
            arcRadius,
            segmentsPerPanel,
            verticalSegments,
            contentMarginH,
            contentMarginV
        );

        // Generate expanded mesh for border (with glow expansion)
        _expandedMesh = CurvedClusterMeshGenerator.GenerateWithGlowExpansion(
            panelCount,
            panelWidth,
            panelHeight,
            arcRadius,
            glowExpansion,
            segmentsPerPanel,
            verticalSegments,
            contentMarginH,
            contentMarginV
        );

        // Cache parameters
        _cachedPanelCount = panelCount;
        _cachedPanelWidth = panelWidth;
        _cachedPanelHeight = panelHeight;
        _cachedArcRadius = arcRadius;

        // Debug: Log mesh generation parameters with correct arc angle calculation
        float boardWidth = panelWidth * (1f - 2f * contentMarginH);
        float boardAngleRad = 2f * Mathf.Atan(boardWidth / 2f / arcRadius);
        float totalArcAngleRad = panelCount * boardAngleRad;
        float arcLength = totalArcAngleRad * arcRadius;
        Debug.Log($"[ClusterVisualCurved] Mesh Gen - panelWidth: {panelWidth:F3}m, panelHeight: {panelHeight:F3}m, " +
            $"panelCount: {panelCount}, arcRadius: {arcRadius:F2}m, marginH: {contentMarginH}");
        Debug.Log($"[ClusterVisualCurved] Arc angle per panel: {boardAngleRad * Mathf.Rad2Deg:F1}°, " +
            $"Total arc angle: {totalArcAngleRad * Mathf.Rad2Deg:F1}°, Arc length: {arcLength:F3}m");
    }

    #endregion

    #region Visual Layer Creation

    private void CreateVisualLayers()
    {
        // Calculate offset to center the mesh on actual panel positions
        Vector3 centerOffset = CalculatePanelCenterOffset();

        // Background layer (behind content)
        // Use expanded mesh so background fills up to border line (shader clips via SDF)
        _backgroundObject = CreateLayerObject("ClusterBackground_Curved", backgroundZOffset, centerOffset);
        _backgroundMeshFilter = _backgroundObject.GetComponent<MeshFilter>();
        _backgroundRenderer = _backgroundObject.GetComponent<MeshRenderer>();
        _backgroundMeshFilter.sharedMesh = _expandedMesh;

        // Content layer (middle)
        _contentObject = CreateLayerObject("ClusterContent_Curved", 0f, centerOffset);
        _contentMeshFilter = _contentObject.GetComponent<MeshFilter>();
        _contentRenderer = _contentObject.GetComponent<MeshRenderer>();
        _contentMeshFilter.sharedMesh = _curvedMesh;

        // Border layer (in front, with expanded mesh)
        _borderObject = CreateLayerObject("ClusterBorder_Curved", borderZOffset, centerOffset);
        _borderMeshFilter = _borderObject.GetComponent<MeshFilter>();
        _borderRenderer = _borderObject.GetComponent<MeshRenderer>();
        _borderMeshFilter.sharedMesh = _expandedMesh;
    }

    /// <summary>
    /// Calculate offset to center mesh on actual panel positions (only enabled panels)
    /// The mesh is generated centered at angle=0, but panels might be offset (e.g., FixedThreeSlot mode)
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
    /// Calculate arc-based dimensions for shader.
    /// Must match CurvedClusterMeshGenerator's arc angle calculation.
    /// </summary>
    private void CalculateArcDimensions(bool expanded, out float arcWidth, out float height)
    {
        // UPDATE: Use full panel width to match Generator change.
        // This ensures the shader receives dimensions consistent with the larger mesh.
        float boardWidth = _cachedPanelWidth;

        if (expanded)
        {
            // For expanded mesh, add glow expansion to board width per panel
            boardWidth += (glowExpansion * 2f / _cachedPanelCount);
        }

        // Arc angle per panel: 2 * atan(boardWidth / 2 / radius)
        // This matches WorldPanelClusterRig.LayoutFromCamera() and CurvedClusterMeshGenerator.Generate()
        float boardAngleRad = 2f * Mathf.Atan(boardWidth / 2f / _cachedArcRadius);
        float totalArcAngleRad = _cachedPanelCount * boardAngleRad;

        // Arc length = angle * radius (this is the "unrolled" width of the curved mesh)
        arcWidth = totalArcAngleRad * _cachedArcRadius;

        // Height doesn't change with arc (it's perpendicular to the arc plane)
        height = expanded ? _cachedPanelHeight + glowExpansion * 2f : _cachedPanelHeight;
    }

    private void ApplyMaterials()
    {
        ApplyBackgroundMaterial();
        ApplyContentMaterial();
        ApplyBorderMaterial();
    }

    private void ApplyBackgroundMaterial()
    {
        if (_backgroundRenderer == null) return;

        var shader = Shader.Find("Custom/ClusterBackgroundCurved");
        if (shader == null)
        {
            Debug.LogWarning("[ClusterVisualCurved] ClusterBackgroundCurved shader not found");
            return;
        }

        if (_backgroundMaterial == null)
        {
            _backgroundMaterial = new Material(shader);
        }

        // Calculate arc-based dimensions (expanded mesh for background)
        CalculateArcDimensions(expanded: true, out float clusterWidth, out float clusterHeight);

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

        _backgroundMaterial.SetFloat("_MarginH", contentMarginH);
        _backgroundMaterial.SetFloat("_MarginV", contentMarginV);

        _backgroundMaterial.renderQueue = 2999;
        _backgroundRenderer.sharedMaterial = _backgroundMaterial;
    }

    private void ApplyContentMaterial()
    {
        if (_contentRenderer == null) return;

        var shader = Shader.Find("Custom/ClusterContentCurved");
        if (shader == null)
        {
            Debug.LogWarning("[ClusterVisualCurved] ClusterContentCurved shader not found");
            return;
        }

        if (_contentMaterial == null)
        {
            _contentMaterial = new Material(shader);
        }

        // Calculate arc-based dimensions (non-expanded mesh for content)
        CalculateArcDimensions(expanded: false, out float clusterWidth, out float clusterHeight);

        _contentMaterial.SetInt("_PanelCount", _cachedPanelCount);
        _contentMaterial.SetFloat("_ClusterWidth", clusterWidth);
        _contentMaterial.SetFloat("_ClusterHeight", clusterHeight);
        _contentMaterial.SetFloat("_BlendZone", blendZoneWidth);

        _contentMaterial.SetFloat("_BlendZone", blendZoneWidth);

        // Use minimal corner radius for content - background/border already provide visual corners
        // This prevents content from being clipped too aggressively (e.g. taskbar date/time)
        _contentMaterial.SetFloat("_CornerRadius", 0.01f);
        _contentMaterial.SetFloat("_EdgePadding", 0f); // content fills the board area

        _contentMaterial.SetFloat("_Sharpness", 0.5f);
        _contentMaterial.SetFloat("_SharpnessRadius", 1.0f);
        _contentMaterial.SetFloat("_ChromaSharpness", 0.3f);
        _contentMaterial.SetFloat("_EnableSharpening", 1f);

        _contentMaterial.renderQueue = 3000;
        _contentRenderer.sharedMaterial = _contentMaterial;
    }

    private void ApplyBorderMaterial()
    {
        if (_borderRenderer == null) return;

        var shader = Shader.Find("Custom/ClusterBorderCurved");
        if (shader == null)
        {
            Debug.LogWarning("[ClusterVisualCurved] ClusterBorderCurved shader not found");
            return;
        }

        if (_borderMaterial == null)
        {
            _borderMaterial = new Material(shader);
        }

        // Calculate arc-based dimensions (expanded mesh for border)
        CalculateArcDimensions(expanded: true, out float clusterWidth, out float clusterHeight);

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

        if (_contentMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_contentMaterial);
            else
                DestroyImmediate(_contentMaterial);
            _contentMaterial = null;
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
        if (_curvedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(_curvedMesh);
            else
                DestroyImmediate(_curvedMesh);
            _curvedMesh = null;
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

        if (_contentObject != null)
        {
            if (Application.isPlaying)
                Destroy(_contentObject);
            else
                DestroyImmediate(_contentObject);
            _contentObject = null;
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
        _contentMeshFilter = null;
        _borderMeshFilter = null;
        _backgroundRenderer = null;
        _contentRenderer = null;
        _borderRenderer = null;

        _cachedPanelCount = -1;
    }

    #endregion
}