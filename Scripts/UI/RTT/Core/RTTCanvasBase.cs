using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Abstract base class for all RTT (Render-to-Texture) UI panels.
/// Handles RenderTexture lifecycle, UI Camera setup, Canvas configuration,
/// Display Quad for world-space rendering, and dirty flag optimization.
///
/// Subclasses must implement BuildUI() to create their specific UI content.
/// </summary>
public abstract class RTTCanvasBase : MonoBehaviour
{
    #region Serialized Fields
    [Header("RTT Configuration")]
    [SerializeField] protected RTTConfig config;

    [Header("World Display")]
    [Tooltip("Width of the display quad in world units (meters)")]
    [SerializeField] protected float worldWidth = 1.6f;

    [Tooltip("Height of the display quad in world units (meters)")]
    [SerializeField] protected float worldHeight = 0.9f;

    [Header("Layer Settings")]
    [Tooltip("Layer name for the UI elements (rendered by RTT camera)")]
    [SerializeField] protected string uiLayerName = "UI";

    [Tooltip("Layer name for the display quad (visible in world)")]
    [SerializeField] protected string quadLayerName = "VirtualObjects";
    #endregion

    #region Protected Fields
    protected RenderTexture _renderTexture;
    protected Camera _uiCamera;
    protected Canvas _canvas;
    protected CanvasScaler _canvasScaler;
    protected GraphicRaycaster _graphicRaycaster;
    protected MeshRenderer _displayQuad;
    protected MeshFilter _displayQuadMeshFilter;
    protected BoxCollider _quadCollider;
    protected Material _quadMaterial;

    // Dirty flag system
    protected bool _isDirty = true;
    protected int _framesSinceLastRender = 0;
    protected float _lastDirtyTime;
    protected bool _continuousRender = false; // For animated content that needs constant updates

    // State tracking
    protected bool _isInitialized = false;
    protected bool _isVisible = true;

    // Camera depth assigned by RTTManager
    protected int _assignedCameraDepth = -10;
    #endregion

    #region Events
    /// <summary>Invoked when RenderTexture is successfully created</summary>
    public event Action OnRTTCreated;

    /// <summary>Invoked when RTT is being destroyed</summary>
    public event Action OnRTTDestroyed;

    /// <summary>Invoked when visibility changes (bool = isVisible)</summary>
    public event Action<bool> OnVisibilityChanged;
    #endregion

    #region Properties
    /// <summary>Is the RTT system initialized and ready</summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>Is the panel currently visible</summary>
    public bool IsVisible => _isVisible;

    /// <summary>Current RenderTexture resolution</summary>
    public Vector2Int CurrentResolution => _renderTexture != null
        ? new Vector2Int(_renderTexture.width, _renderTexture.height)
        : Vector2Int.zero;

    /// <summary>
    /// Force initialization if not already initialized.
    /// Use this when Start() may not have been called (e.g., GameObject was disabled before first frame).
    /// </summary>
    public void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            Initialize();
        }
    }
    #endregion

    #region Lifecycle
    protected virtual void Awake()
    {
        ValidateConfiguration();
    }

    protected virtual void Start()
    {
        Initialize();
    }

    protected virtual void OnDestroy()
    {
        Cleanup();
    }

    protected virtual void OnEnable()
    {
        if (_isInitialized)
        {
            SetVisible(true);
        }

        // Subscribe to theme changes
        if (RTTManager.Instance != null)
        {
            RTTManager.Instance.OnThemeChanged += OnThemeChanged;
            RTTManager.Instance.OnFontChanged += OnFontChanged;
            // Apply current theme when enabled
            ApplyCurrentTheme();
        }
    }

    protected virtual void OnDisable()
    {
        if (_isInitialized)
        {
            SetVisible(false);
        }

        // Unsubscribe from theme changes
        if (RTTManager.Instance != null)
        {
            RTTManager.Instance.OnThemeChanged -= OnThemeChanged;
            RTTManager.Instance.OnFontChanged -= OnFontChanged;
        }
    }

    protected virtual void LateUpdate()
    {
        if (!_isInitialized || !_isVisible) return;

        HandleDirtyRendering();
    }
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize the RTT system: create RenderTexture, Camera, Canvas, and Display Quad
    /// </summary>
    protected virtual void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            // Request camera depth from RTTManager
            if (RTTManager.Instance != null)
            {
                _assignedCameraDepth = RTTManager.Instance.AssignCameraDepth();
            }

            CreateRenderTexture();
            SetupUICamera();
            SetupCanvas();
            SetupDisplayQuad();

            // Call subclass implementation to build UI content
            BuildUI();

            _isInitialized = true;

            // Register with RTTManager
            RTTManager.Instance?.RegisterPanel(this);

            OnRTTCreated?.Invoke();

            if (config != null && config.logPerformanceMetrics)
            {
                Debug.Log($"[RTT] {GetType().Name} initialized: {GetResolution().x}x{GetResolution().y}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[RTT] Failed to initialize {GetType().Name}: {e.Message}\n{e.StackTrace}");
            HandleInitializationFailure();
        }
    }

    /// <summary>
    /// Validate configuration and create default if missing
    /// </summary>
    protected virtual void ValidateConfiguration()
    {
        if (config == null)
        {
            // Try to load from Resources
            config = Resources.Load<RTTConfig>("RTTConfig");

            if (config == null)
            {
                Debug.LogWarning($"[RTT] {GetType().Name}: RTTConfig not assigned and not found in Resources, using defaults");
                config = ScriptableObject.CreateInstance<RTTConfig>();
            }
        }
    }
    #endregion

    #region RenderTexture Management
    /// <summary>
    /// Create the RenderTexture for this panel
    /// </summary>
    protected virtual void CreateRenderTexture()
    {
        var resolution = GetResolution();

        // Use mobile-compatible settings on Android/iOS
        int depthBits = GetMobileCompatibleDepthBits();
        int antiAliasing = GetMobileCompatibleAntiAliasing();
        RenderTextureFormat format = GetMobileCompatibleFormat();

        _renderTexture = new RenderTexture(resolution.x, resolution.y, depthBits, format);
        _renderTexture.antiAliasing = antiAliasing;
        _renderTexture.filterMode = config.filterMode;
        _renderTexture.useMipMap = false;
        _renderTexture.autoGenerateMips = false;
        _renderTexture.anisoLevel = 0;
        _renderTexture.name = $"RTT_{GetType().Name}_{GetInstanceID()}";

        if (!_renderTexture.Create())
        {
            throw new Exception("Failed to create RenderTexture");
        }
    }

    /// <summary>
    /// Get mobile-compatible depth buffer bits
    /// </summary>
    protected int GetMobileCompatibleDepthBits()
    {
#if UNITY_ANDROID || UNITY_IOS
        return 16; // Use 16-bit depth on mobile for better compatibility
#else
        return 24;
#endif
    }

    /// <summary>
    /// Get mobile-compatible anti-aliasing level
    /// </summary>
    protected int GetMobileCompatibleAntiAliasing()
    {
#if UNITY_ANDROID || UNITY_IOS
        // Disable MSAA on mobile - can cause issues with RenderTextures
        return 1;
#else
        return config.antiAliasing;
#endif
    }

    /// <summary>
    /// Get mobile-compatible RenderTexture format
    /// </summary>
    protected RenderTextureFormat GetMobileCompatibleFormat()
    {
#if UNITY_ANDROID || UNITY_IOS
        // Use ARGB32 which is universally supported
        return RenderTextureFormat.ARGB32;
#else
        return config.format;
#endif
    }

    /// <summary>
    /// Get the resolution for this panel. Override to customize.
    /// </summary>
    protected virtual Vector2Int GetResolution()
    {
        return new Vector2Int(config.defaultWidth, config.defaultHeight);
    }

    /// <summary>
    /// Resize the RenderTexture to new dimensions
    /// </summary>
    public virtual void ResizeRenderTexture(int newWidth, int newHeight)
    {
        if (_renderTexture != null &&
            _renderTexture.width == newWidth &&
            _renderTexture.height == newHeight)
            return;

        // Release old
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }

        // Use mobile-compatible settings
        int depthBits = GetMobileCompatibleDepthBits();
        int antiAliasing = GetMobileCompatibleAntiAliasing();
        RenderTextureFormat format = GetMobileCompatibleFormat();

        // Create new
        _renderTexture = new RenderTexture(newWidth, newHeight, depthBits, format);
        _renderTexture.antiAliasing = antiAliasing;
        _renderTexture.filterMode = config.filterMode;
        _renderTexture.useMipMap = false;
        _renderTexture.name = $"RTT_{GetType().Name}_{GetInstanceID()}";
        _renderTexture.Create();

        // Update references
        if (_uiCamera != null)
            _uiCamera.targetTexture = _renderTexture;

        if (_quadMaterial != null)
            _quadMaterial.mainTexture = _renderTexture;

        // Update canvas scaler
        if (_canvasScaler != null)
            _canvasScaler.referenceResolution = new Vector2(newWidth, newHeight);

        // Update camera orthographic size
        if (_uiCamera != null)
            _uiCamera.orthographicSize = newHeight / 2f;

        MarkDirty();

        if (config != null && config.logPerformanceMetrics)
        {
            Debug.Log($"[RTT] {GetType().Name} resized to {newWidth}x{newHeight}");
        }
    }
    #endregion

    #region Camera Setup
    /// <summary>
    /// Setup the UI camera that renders to the RenderTexture
    /// </summary>
    protected virtual void SetupUICamera()
    {
        var cameraGO = new GameObject($"UICamera_{GetType().Name}");
        cameraGO.transform.SetParent(transform);
        cameraGO.transform.localPosition = Vector3.zero;
        cameraGO.transform.localRotation = Quaternion.identity;

        _uiCamera = cameraGO.AddComponent<Camera>();
        _uiCamera.clearFlags = CameraClearFlags.SolidColor;
        _uiCamera.backgroundColor = new Color(0, 0, 0, 0); // Transparent
        _uiCamera.orthographic = true;
        _uiCamera.orthographicSize = GetResolution().y / 2f;
        _uiCamera.nearClipPlane = 0.1f;
        _uiCamera.farClipPlane = 100f;
        _uiCamera.depth = GetCameraDepth();
        _uiCamera.targetTexture = _renderTexture;
        _uiCamera.allowHDR = false;
        _uiCamera.allowMSAA = true;

        // Layer mask - render only UI layer
        int uiLayer = LayerMask.NameToLayer(uiLayerName);
        if (uiLayer == -1)
        {
            Debug.LogWarning($"[RTT] Layer '{uiLayerName}' not found, using default UI layer");
            uiLayer = LayerMask.NameToLayer("UI");
        }
        _uiCamera.cullingMask = 1 << uiLayer;

        // Disable camera initially if using dirty flag (will enable on demand)
        if (config != null && config.useDirtyFlag)
        {
            _uiCamera.enabled = false;
        }
    }

    /// <summary>
    /// Get the camera depth for this panel. Override to customize render order.
    /// Lower depth = renders first.
    /// </summary>
    protected virtual int GetCameraDepth()
    {
        return _assignedCameraDepth;
    }
    #endregion

    #region Canvas Setup
    /// <summary>
    /// Setup the Canvas for UI elements
    /// </summary>
    protected virtual void SetupCanvas()
    {
        var canvasGO = new GameObject($"Canvas_{GetType().Name}");
        canvasGO.transform.SetParent(_uiCamera.transform);
        canvasGO.transform.localPosition = new Vector3(0, 0, 10); // In front of camera
        canvasGO.transform.localRotation = Quaternion.identity;
        canvasGO.transform.localScale = Vector3.one;

        // Set layer for UI rendering
        int uiLayer = LayerMask.NameToLayer(uiLayerName);
        if (uiLayer == -1) uiLayer = LayerMask.NameToLayer("UI");
        canvasGO.layer = uiLayer;

        // Canvas component
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _uiCamera;
        _canvas.planeDistance = 10f;

        // Canvas Scaler
        _canvasScaler = canvasGO.AddComponent<CanvasScaler>();
        _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _canvasScaler.referenceResolution = GetResolution();
        _canvasScaler.matchWidthOrHeight = 0.5f;

        // Graphic Raycaster for UI interaction
        _graphicRaycaster = canvasGO.AddComponent<GraphicRaycaster>();
    }
    #endregion

    #region Display Quad Setup
    /// <summary>
    /// Setup the display quad that shows the RenderTexture in world space
    /// </summary>
    protected virtual void SetupDisplayQuad()
    {
        var quadGO = new GameObject($"DisplayQuad_{GetType().Name}");
        quadGO.transform.SetParent(transform);
        quadGO.transform.localPosition = Vector3.zero;
        quadGO.transform.localRotation = Quaternion.identity;

        // Set layer
        int quadLayer = LayerMask.NameToLayer(quadLayerName);
        if (quadLayer == -1) quadLayer = LayerMask.NameToLayer("Default");
        quadGO.layer = quadLayer;

        // Mesh Filter
        _displayQuadMeshFilter = quadGO.AddComponent<MeshFilter>();
        _displayQuadMeshFilter.mesh = CreateQuadMesh();

        // Mesh Renderer
        _displayQuad = quadGO.AddComponent<MeshRenderer>();
        _quadMaterial = CreateQuadMaterial();
        _quadMaterial.mainTexture = _renderTexture;
        _displayQuad.material = _quadMaterial;
        _displayQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _displayQuad.receiveShadows = false;
        _displayQuad.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _displayQuad.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // Scale to world size
        quadGO.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);

        // Box Collider for raycast interaction
        _quadCollider = quadGO.AddComponent<BoxCollider>();
        _quadCollider.size = new Vector3(1, 1, 0.01f);
        _quadCollider.center = Vector3.zero;
        _quadCollider.isTrigger = true;

        // Respect pre-set visibility (if SetVisible(false) was called before Initialize)
        _displayQuad.enabled = _isVisible;
        _quadCollider.enabled = _isVisible;
    }

    /// <summary>
    /// Create the quad mesh for displaying the RenderTexture
    /// </summary>
    protected virtual Mesh CreateQuadMesh()
    {
        var mesh = new Mesh();
        mesh.name = $"RTTQuadMesh_{GetType().Name}";

        // Vertices centered at origin
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0),
            new Vector3(0.5f, 0.5f, 0)
        };

        // UVs
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        // Triangles (front-facing)
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Create the material for the display quad
    /// </summary>
    protected virtual Material CreateQuadMaterial()
    {
        // Use Sprites/Default shader which supports color alpha multiplication for fade transitions
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("UI/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");

        var mat = new Material(shader);
        mat.name = $"RTTQuadMaterial_{GetType().Name}";
        mat.renderQueue = 3000; // Render after opaque objects
        return mat;
    }
    #endregion

    #region Dirty Flag System
    /// <summary>
    /// Mark the panel as dirty, requiring a re-render
    /// </summary>
    public virtual void MarkDirty()
    {
        _isDirty = true;
        _lastDirtyTime = Time.unscaledTime;
    }

    /// <summary>
    /// Enable/disable continuous rendering for animated content.
    /// When enabled, the panel renders every frame regardless of dirty state.
    /// Use this for panels with constant animation instead of marking dirty every frame.
    /// </summary>
    public virtual void SetContinuousRender(bool continuous)
    {
        _continuousRender = continuous;
        if (continuous) MarkDirty();
    }

    /// <summary>
    /// Check if continuous render is enabled
    /// </summary>
    public bool IsContinuousRender => _continuousRender;

    /// <summary>
    /// Handle dirty flag rendering logic in LateUpdate
    /// </summary>
    protected virtual void HandleDirtyRendering()
    {
        if (config == null || !config.useDirtyFlag)
        {
            // Always render if dirty flag disabled
            _uiCamera.enabled = true;
            return;
        }

        _framesSinceLastRender++;

        // Check if we should render
        // Continuous render mode always renders (for animated content)
        bool shouldRender = _isDirty || _continuousRender ||
            (config.maxFrameSkip > 0 && _framesSinceLastRender >= config.maxFrameSkip);

        if (shouldRender)
        {
            // Enable camera for one frame
            _uiCamera.enabled = true;
            _isDirty = false;
            _framesSinceLastRender = 0;
        }
        else
        {
            _uiCamera.enabled = false;
        }
    }

    /// <summary>
    /// Call this from UI events to trigger re-render
    /// </summary>
    protected void OnUIElementChanged()
    {
        MarkDirty();
    }
    #endregion

    #region Visibility
    /// <summary>
    /// Set the visibility of the panel
    /// </summary>
    public virtual void SetVisible(bool visible)
    {
        // Ensure initialized if trying to show (auto-initialize if needed)
        if (visible && !_isInitialized)
        {
            EnsureInitialized();
        }

        // Always apply visibility to fix race conditions
        // This ensures DisplayQuad is always in correct state even if _isVisible flag is wrong
        bool wasVisible = _isVisible;
        _isVisible = visible;

        if (_displayQuad != null)
        {
            _displayQuad.enabled = visible;
        }

        if (_quadCollider != null)
            _quadCollider.enabled = visible;

        // Enable/disable UICamera based on visibility
        if (_uiCamera != null)
        {
            if (visible)
            {
                // Force enable camera to render at least one frame
                _uiCamera.enabled = true;
            }
            else
            {
                _uiCamera.enabled = false;
            }
        }

        if (wasVisible != visible)
            OnVisibilityChanged?.Invoke(visible);

        if (visible)
            MarkDirty();
    }

    /// <summary>
    /// Show the panel
    /// </summary>
    public void Show()
    {
        SetVisible(true);
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Hide the panel
    /// </summary>
    public void Hide()
    {
        SetVisible(false);
    }
    #endregion

    #region Cleanup
    /// <summary>
    /// Cleanup all RTT resources
    /// </summary>
    protected virtual void Cleanup()
    {
        OnRTTDestroyed?.Invoke();

        // Unregister from manager
        RTTManager.Instance?.UnregisterPanel(this);

        // Release RenderTexture
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        // Destroy material
        if (_quadMaterial != null)
        {
            Destroy(_quadMaterial);
            _quadMaterial = null;
        }

        // Destroy mesh
        if (_displayQuadMeshFilter != null && _displayQuadMeshFilter.mesh != null)
        {
            Destroy(_displayQuadMeshFilter.mesh);
        }

        _isInitialized = false;
    }
    #endregion

    #region Fallback
    /// <summary>
    /// Handle initialization failure by falling back to World Space Canvas
    /// </summary>
    protected virtual void HandleInitializationFailure()
    {
        Debug.LogWarning($"[RTT] {GetType().Name}: Falling back to World Space Canvas");

        // Cleanup partial initialization
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        if (_uiCamera != null)
            Destroy(_uiCamera.gameObject);

        if (_displayQuad != null)
            Destroy(_displayQuad.gameObject);

        // Setup fallback
        SetupFallbackWorldSpaceCanvas();
    }

    /// <summary>
    /// Setup a fallback World Space Canvas when RTT fails.
    /// Override in subclasses to implement specific fallback behavior.
    /// </summary>
    protected virtual void SetupFallbackWorldSpaceCanvas()
    {
        // Default implementation - create basic World Space Canvas
        var canvasGO = new GameObject($"FallbackCanvas_{GetType().Name}");
        canvasGO.transform.SetParent(transform);
        canvasGO.transform.localPosition = Vector3.zero;
        canvasGO.transform.localRotation = Quaternion.identity;

        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvasGO.GetComponent<RectTransform>();
        var resolution = GetResolution();
        rt.sizeDelta = new Vector2(resolution.x, resolution.y);

        // Scale to world size
        float scaleFactor = worldWidth / resolution.x;
        canvasGO.transform.localScale = Vector3.one * scaleFactor;

        _canvasScaler = canvasGO.AddComponent<CanvasScaler>();
        _graphicRaycaster = canvasGO.AddComponent<GraphicRaycaster>();

        // Set layer
        int quadLayer = LayerMask.NameToLayer(quadLayerName);
        if (quadLayer != -1)
        {
            SetLayerRecursively(canvasGO, quadLayer);
        }

        // Add collider for interaction
        _quadCollider = canvasGO.AddComponent<BoxCollider>();
        _quadCollider.size = new Vector3(resolution.x, resolution.y, 10f);
        _quadCollider.isTrigger = true;

        _isInitialized = true;

        // Build UI content
        BuildUI();
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    #endregion

    #region Public API
    /// <summary>Get the RenderTexture</summary>
    public RenderTexture GetRenderTexture() => _renderTexture;

    /// <summary>Get the Canvas</summary>
    public Canvas GetCanvas() => _canvas;

    /// <summary>Get the GraphicRaycaster</summary>
    public GraphicRaycaster GetGraphicRaycaster() => _graphicRaycaster;

    /// <summary>Get the Display Quad MeshRenderer</summary>
    public MeshRenderer GetDisplayQuad() => _displayQuad;

    /// <summary>Get the Quad Collider</summary>
    public BoxCollider GetQuadCollider() => _quadCollider;

    /// <summary>Get the UI Camera</summary>
    public Camera GetUICamera() => _uiCamera;

    /// <summary>Get world dimensions</summary>
    public Vector2 GetWorldSize() => new Vector2(worldWidth, worldHeight);
    #endregion

    #region Abstract Methods
    /// <summary>
    /// Override to build UI content within the canvas.
    /// Called after canvas is set up.
    /// </summary>
    protected abstract void BuildUI();
    #endregion

    #region Theme Support
    /// <summary>
    /// Called when theme changes. Override in subclasses to apply theme colors.
    /// </summary>
    protected virtual void OnThemeChanged()
    {
        ApplyCurrentTheme();
    }

    /// <summary>
    /// Called when font changes. Override in subclasses to apply font.
    /// </summary>
    protected virtual void OnFontChanged()
    {
        ApplyCurrentFont();
    }

    /// <summary>
    /// Apply current theme colors to this panel.
    /// Override in subclasses to implement theme application.
    /// </summary>
    protected virtual void ApplyCurrentTheme()
    {
        // Base implementation does nothing.
        // Subclasses should override to apply theme colors to their materials/UI elements.
        // Example:
        // var theme = RTTManager.Instance?.Theme;
        // if (theme == null) return;
        // _glassMaterial?.SetColor("_ColorA", theme.glassColorA);
        // MarkDirty();
    }

    /// <summary>
    /// Apply current font to this panel.
    /// Override in subclasses to implement font application.
    /// </summary>
    protected virtual void ApplyCurrentFont()
    {
        // Base implementation does nothing.
        // Subclasses should override to apply font to their text elements.
    }

    /// <summary>
    /// Helper to get current theme config with null safety.
    /// </summary>
    protected RTTThemeConfig GetTheme()
    {
        return RTTManager.Instance?.Theme;
    }

    /// <summary>
    /// Helper to get current font with null safety.
    /// </summary>
    protected TMPro.TMP_FontAsset GetFont()
    {
        return RTTManager.Instance?.Font;
    }
    #endregion

    #region Debug
    protected virtual void OnDrawGizmos()
    {
        if (config == null || !config.showDebugGizmos) return;

        if (_displayQuad != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = _displayQuad.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1, 1, 0.01f));
        }
        else
        {
            // Draw expected bounds when not initialized
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(worldWidth, worldHeight, 0.01f));
        }
    }
    #endregion
}
