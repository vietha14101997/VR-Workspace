# Kế Hoạch Chi Tiết v2.0: Chuyển Đổi VR UI sang Cấu Trúc Render-to-Texture

> **Phiên bản**: 2.0 (Cải thiện từ v1.0)
> **Ngày cập nhật**: 2025-12-25

---

## 📋 Đánh Giá Bản Kế Hoạch v1.0

### Điểm Mạnh ✅

| Khía Cạnh | Đánh Giá |
|-----------|----------|
| **Phân tích cấu trúc hiện tại** | Rất chi tiết, liệt kê đầy đủ components, shaders, thông số |
| **Bảo toàn thông số** | Tốt - ghi nhận kích thước, màu sắc, margins cần giữ nguyên |
| **Xác định hiệu ứng tương thích** | Phân loại rõ ràng shader-based vs runtime effects |
| **Cấu trúc RTT cơ bản** | Có đề xuất hierarchy hợp lý |

### Điểm Cần Cải Thiện ⚠️

| Khía Cạnh | Vấn Đề | Giải Pháp |
|-----------|--------|-----------|
| **RTTRaycastManager** | Chỉ có pseudocode, thiếu edge cases | Cần implementation chi tiết |
| **Memory Management** | Không đề cập lifecycle của RenderTexture | Cần strategy release/recreate |
| **Dirty Flag Optimization** | Chỉ đề cập, không chi tiết | Cần cơ chế cụ thể |
| **Multi-RTT Coordination** | Không đề cập thứ tự render | Cần camera depth management |
| **Fallback Strategy** | Thiếu kế hoạch khi RTT fail | Cần graceful degradation |
| **Testing Strategy** | Chỉ có checklist, không có test cases | Cần automated + manual tests |
| **Migration Path** | Thiếu rollback plan | Cần feature toggle approach |
| **GrabPass Alternative** | Đề xuất "fake blur" nhưng không chi tiết | Cần giải pháp cụ thể |

---

## 🏗️ Phase 1: RTT Infrastructure (Cải Thiện)

### 1.1 RTTConfig ScriptableObject

Tách configuration ra khỏi code để dễ tuning:

```csharp
// RTTConfig.cs
[CreateAssetMenu(fileName = "RTTConfig", menuName = "VR-Workspace/RTT Config")]
public class RTTConfig : ScriptableObject
{
    [Header("Render Texture Settings")]
    public int defaultWidth = 1920;
    public int defaultHeight = 1080;

    [Range(1, 8)]
    public int antiAliasing = 4;

    public RenderTextureFormat format = RenderTextureFormat.ARGB32;
    public FilterMode filterMode = FilterMode.Bilinear;

    [Header("Quality Presets")]
    public RTTQualityPreset lowQuality;
    public RTTQualityPreset mediumQuality;
    public RTTQualityPreset highQuality;

    [Header("Performance")]
    [Tooltip("Chỉ render lại khi UI thay đổi")]
    public bool useDirtyFlag = true;

    [Tooltip("Số frame tối đa giữa các lần render (0 = luôn render)")]
    [Range(0, 10)]
    public int maxFrameSkip = 2;

    [Header("Debug")]
    public bool showDebugGizmos = false;
    public bool logPerformanceMetrics = false;
}

[System.Serializable]
public class RTTQualityPreset
{
    public string name;
    public int width;
    public int height;
    public int antiAliasing;
    public float renderScale = 1.0f;
}
```

### 1.2 RTTCanvasBase (Abstract Base Class - Chi Tiết)

```csharp
// RTTCanvasBase.cs
using UnityEngine;
using UnityEngine.UI;
using System;

public abstract class RTTCanvasBase : MonoBehaviour
{
    #region Serialized Fields
    [Header("RTT Configuration")]
    [SerializeField] protected RTTConfig config;

    [Header("World Display")]
    [SerializeField] protected float worldWidth = 1.6f;
    [SerializeField] protected float worldHeight = 0.9f;

    [Header("Layer Settings")]
    [SerializeField] protected string uiLayerName = "UI_RTT";
    [SerializeField] protected string quadLayerName = "VirtualObjects";
    #endregion

    #region Protected Fields
    protected RenderTexture _renderTexture;
    protected Camera _uiCamera;
    protected Canvas _canvas;
    protected CanvasScaler _canvasScaler;
    protected GraphicRaycaster _graphicRaycaster;
    protected MeshRenderer _displayQuad;
    protected BoxCollider _quadCollider;

    // Dirty flag system
    protected bool _isDirty = true;
    protected int _framesSinceLastRender = 0;

    // State tracking
    protected bool _isInitialized = false;
    protected bool _isVisible = true;
    #endregion

    #region Events
    public event Action OnRTTCreated;
    public event Action OnRTTDestroyed;
    public event Action<bool> OnVisibilityChanged;
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
        SetVisible(true);
    }

    protected virtual void OnDisable()
    {
        SetVisible(false);
    }

    protected virtual void LateUpdate()
    {
        if (!_isInitialized || !_isVisible) return;

        HandleDirtyRendering();
    }
    #endregion

    #region Initialization
    protected virtual void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            CreateRenderTexture();
            SetupUICamera();
            SetupCanvas();
            SetupDisplayQuad();
            BuildUI();

            _isInitialized = true;
            OnRTTCreated?.Invoke();

            if (config.logPerformanceMetrics)
                Debug.Log($"[RTT] {GetType().Name} initialized: {GetResolution().x}x{GetResolution().y}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[RTT] Failed to initialize {GetType().Name}: {e.Message}");
            HandleInitializationFailure();
        }
    }

    protected virtual void ValidateConfiguration()
    {
        if (config == null)
        {
            Debug.LogWarning($"[RTT] {GetType().Name}: RTTConfig not assigned, using defaults");
            config = ScriptableObject.CreateInstance<RTTConfig>();
        }
    }
    #endregion

    #region RenderTexture Management
    protected virtual void CreateRenderTexture()
    {
        var resolution = GetResolution();

        _renderTexture = new RenderTexture(resolution.x, resolution.y, 24, config.format);
        _renderTexture.antiAliasing = config.antiAliasing;
        _renderTexture.filterMode = config.filterMode;
        _renderTexture.useMipMap = false;
        _renderTexture.autoGenerateMips = false;
        _renderTexture.anisoLevel = 0;
        _renderTexture.name = $"RTT_{GetType().Name}";

        if (!_renderTexture.Create())
        {
            throw new Exception("Failed to create RenderTexture");
        }
    }

    protected virtual Vector2Int GetResolution()
    {
        return new Vector2Int(config.defaultWidth, config.defaultHeight);
    }

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

        // Create new
        _renderTexture = new RenderTexture(newWidth, newHeight, 24, config.format);
        _renderTexture.antiAliasing = config.antiAliasing;
        _renderTexture.filterMode = config.filterMode;
        _renderTexture.Create();

        // Update references
        if (_uiCamera != null)
            _uiCamera.targetTexture = _renderTexture;

        if (_displayQuad != null)
            _displayQuad.material.mainTexture = _renderTexture;

        MarkDirty();
    }
    #endregion

    #region Camera Setup
    protected virtual void SetupUICamera()
    {
        var cameraGO = new GameObject($"UICamera_{GetType().Name}");
        cameraGO.transform.SetParent(transform);
        cameraGO.transform.localPosition = Vector3.zero;

        _uiCamera = cameraGO.AddComponent<Camera>();
        _uiCamera.clearFlags = CameraClearFlags.SolidColor;
        _uiCamera.backgroundColor = new Color(0, 0, 0, 0);
        _uiCamera.orthographic = true;
        _uiCamera.orthographicSize = GetResolution().y / 2f;
        _uiCamera.nearClipPlane = 0.1f;
        _uiCamera.farClipPlane = 100f;
        _uiCamera.depth = GetCameraDepth();
        _uiCamera.targetTexture = _renderTexture;

        // Layer mask - chỉ render UI layer cụ thể
        int uiLayer = LayerMask.NameToLayer(uiLayerName);
        if (uiLayer == -1)
        {
            Debug.LogWarning($"[RTT] Layer '{uiLayerName}' not found, using default UI layer");
            uiLayer = LayerMask.NameToLayer("UI");
        }
        _uiCamera.cullingMask = 1 << uiLayer;

        // Disable initially if using dirty flag
        if (config.useDirtyFlag)
        {
            _uiCamera.enabled = false;
        }
    }

    protected virtual int GetCameraDepth()
    {
        // Override để set thứ tự render giữa các RTT panels
        return -10;
    }
    #endregion

    #region Canvas Setup
    protected virtual void SetupCanvas()
    {
        var canvasGO = new GameObject($"Canvas_{GetType().Name}");
        canvasGO.transform.SetParent(_uiCamera.transform);
        canvasGO.transform.localPosition = new Vector3(0, 0, 10);

        int uiLayer = LayerMask.NameToLayer(uiLayerName);
        if (uiLayer == -1) uiLayer = LayerMask.NameToLayer("UI");
        canvasGO.layer = uiLayer;

        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _uiCamera;
        _canvas.planeDistance = 10f;

        _canvasScaler = canvasGO.AddComponent<CanvasScaler>();
        _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _canvasScaler.referenceResolution = GetResolution();
        _canvasScaler.matchWidthOrHeight = 0.5f;

        _graphicRaycaster = canvasGO.AddComponent<GraphicRaycaster>();
    }
    #endregion

    #region Display Quad Setup
    protected virtual void SetupDisplayQuad()
    {
        var quadGO = new GameObject($"DisplayQuad_{GetType().Name}");
        quadGO.transform.SetParent(transform);
        quadGO.transform.localPosition = Vector3.zero;
        quadGO.transform.localRotation = Quaternion.identity;

        int quadLayer = LayerMask.NameToLayer(quadLayerName);
        if (quadLayer == -1) quadLayer = LayerMask.NameToLayer("Default");
        quadGO.layer = quadLayer;

        // Mesh
        var mf = quadGO.AddComponent<MeshFilter>();
        mf.mesh = CreateQuadMesh();

        // Renderer
        _displayQuad = quadGO.AddComponent<MeshRenderer>();
        var mat = CreateQuadMaterial();
        mat.mainTexture = _renderTexture;
        _displayQuad.material = mat;
        _displayQuad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _displayQuad.receiveShadows = false;

        // Scale to world size
        quadGO.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);

        // Collider for raycast
        _quadCollider = quadGO.AddComponent<BoxCollider>();
        _quadCollider.size = new Vector3(1, 1, 0.01f);
        _quadCollider.isTrigger = true;
    }

    protected virtual Mesh CreateQuadMesh()
    {
        var mesh = new Mesh();
        mesh.name = "RTTQuadMesh";

        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0),
            new Vector3(0.5f, 0.5f, 0)
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    protected virtual Material CreateQuadMaterial()
    {
        // Sử dụng shader unlit transparent hoặc custom shader
        var shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            shader = Shader.Find("UI/Default");

        var mat = new Material(shader);
        mat.renderQueue = 3000; // Render after opaque
        return mat;
    }
    #endregion

    #region Dirty Flag System
    public virtual void MarkDirty()
    {
        _isDirty = true;
    }

    protected virtual void HandleDirtyRendering()
    {
        if (!config.useDirtyFlag)
        {
            // Always render
            _uiCamera.enabled = true;
            return;
        }

        _framesSinceLastRender++;

        bool shouldRender = _isDirty ||
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

    // Gọi từ UI events để trigger re-render
    protected void OnUIElementChanged()
    {
        MarkDirty();
    }
    #endregion

    #region Visibility
    public virtual void SetVisible(bool visible)
    {
        if (_isVisible == visible) return;

        _isVisible = visible;

        if (_displayQuad != null)
            _displayQuad.enabled = visible;

        if (_uiCamera != null && !visible)
            _uiCamera.enabled = false;

        OnVisibilityChanged?.Invoke(visible);

        if (visible)
            MarkDirty();
    }

    public bool IsVisible => _isVisible;
    #endregion

    #region Cleanup
    protected virtual void Cleanup()
    {
        OnRTTDestroyed?.Invoke();

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        _isInitialized = false;
    }
    #endregion

    #region Fallback
    protected virtual void HandleInitializationFailure()
    {
        // Fallback: Chuyển về World Space Canvas thông thường
        Debug.LogWarning($"[RTT] {GetType().Name}: Falling back to World Space Canvas");

        // Cleanup partial initialization
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }

        if (_uiCamera != null)
            Destroy(_uiCamera.gameObject);

        if (_displayQuad != null)
            Destroy(_displayQuad.gameObject);

        // Setup fallback world space canvas
        SetupFallbackWorldSpaceCanvas();
    }

    protected virtual void SetupFallbackWorldSpaceCanvas()
    {
        // Override trong subclass để implement fallback
    }
    #endregion

    #region Public API
    public RenderTexture GetRenderTexture() => _renderTexture;
    public Canvas GetCanvas() => _canvas;
    public GraphicRaycaster GetGraphicRaycaster() => _graphicRaycaster;
    public MeshRenderer GetDisplayQuad() => _displayQuad;
    public BoxCollider GetQuadCollider() => _quadCollider;
    #endregion

    #region Abstract Methods
    /// <summary>
    /// Override để build UI content trong canvas
    /// </summary>
    protected abstract void BuildUI();
    #endregion

    #region Debug
    protected virtual void OnDrawGizmos()
    {
        if (!config?.showDebugGizmos ?? true) return;

        if (_displayQuad != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                _displayQuad.transform.position,
                new Vector3(worldWidth, worldHeight, 0.01f)
            );
        }
    }
    #endregion
}
```

### 1.3 RTTManager (Singleton Coordinator)

```csharp
// RTTManager.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Quản lý tất cả RTT panels, xử lý thứ tự render và resource pooling
/// </summary>
public class RTTManager : MonoBehaviour
{
    #region Singleton
    private static RTTManager _instance;
    public static RTTManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("RTTManager");
                _instance = go.AddComponent<RTTManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    #endregion

    #region Fields
    [Header("Configuration")]
    [SerializeField] private RTTConfig defaultConfig;

    [Header("Performance")]
    [SerializeField] private int maxConcurrentRenders = 3;
    [SerializeField] private bool enablePerformanceLogging = false;

    private List<RTTCanvasBase> _registeredPanels = new List<RTTCanvasBase>();
    private Queue<RTTCanvasBase> _renderQueue = new Queue<RTTCanvasBase>();
    private int _currentCameraDepth = -100;

    // Performance tracking
    private float _lastFrameRenderTime;
    private int _rendersThisFrame;
    #endregion

    #region Registration
    public void RegisterPanel(RTTCanvasBase panel)
    {
        if (!_registeredPanels.Contains(panel))
        {
            _registeredPanels.Add(panel);
            panel.OnRTTDestroyed += () => UnregisterPanel(panel);

            if (enablePerformanceLogging)
                Debug.Log($"[RTTManager] Registered: {panel.GetType().Name}");
        }
    }

    public void UnregisterPanel(RTTCanvasBase panel)
    {
        _registeredPanels.Remove(panel);
    }

    public int AssignCameraDepth()
    {
        return _currentCameraDepth--;
    }
    #endregion

    #region Quality Management
    public void SetQualityLevel(RTTQualityLevel level)
    {
        var preset = GetQualityPreset(level);

        foreach (var panel in _registeredPanels)
        {
            panel.ResizeRenderTexture(preset.width, preset.height);
        }
    }

    private RTTQualityPreset GetQualityPreset(RTTQualityLevel level)
    {
        switch (level)
        {
            case RTTQualityLevel.Low:
                return defaultConfig.lowQuality;
            case RTTQualityLevel.Medium:
                return defaultConfig.mediumQuality;
            case RTTQualityLevel.High:
            default:
                return defaultConfig.highQuality;
        }
    }
    #endregion

    #region Performance Stats
    public RTTPerformanceStats GetPerformanceStats()
    {
        return new RTTPerformanceStats
        {
            totalPanels = _registeredPanels.Count,
            visiblePanels = _registeredPanels.FindAll(p => p.IsVisible).Count,
            totalTextureMemoryMB = CalculateTotalTextureMemory() / (1024f * 1024f),
            averageRenderTimeMs = _lastFrameRenderTime
        };
    }

    private long CalculateTotalTextureMemory()
    {
        long total = 0;
        foreach (var panel in _registeredPanels)
        {
            var rt = panel.GetRenderTexture();
            if (rt != null)
            {
                // Estimate: width * height * 4 bytes (ARGB) * AA samples
                total += rt.width * rt.height * 4 * rt.antiAliasing;
            }
        }
        return total;
    }
    #endregion
}

public enum RTTQualityLevel
{
    Low,
    Medium,
    High
}

[System.Serializable]
public struct RTTPerformanceStats
{
    public int totalPanels;
    public int visiblePanels;
    public float totalTextureMemoryMB;
    public float averageRenderTimeMs;
}
```

---

## 🎯 Phase 2: RTTRaycastManager (Chi Tiết Đầy Đủ)

### 2.1 RTTRaycastManager Implementation

```csharp
// RTTRaycastManager.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Xử lý raycast từ gaze/controller vào RTT panels và chuyển đổi thành UI events
/// </summary>
public class RTTRaycastManager : MonoBehaviour
{
    #region Singleton
    private static RTTRaycastManager _instance;
    public static RTTRaycastManager Instance => _instance;
    #endregion

    #region Configuration
    [Header("Raycast Settings")]
    [SerializeField] private float maxRaycastDistance = 10f;
    [SerializeField] private LayerMask raycastLayerMask;

    [Header("Debug")]
    [SerializeField] private bool showDebugRays = false;
    [SerializeField] private bool logHitInfo = false;
    #endregion

    #region State
    private Dictionary<RTTCanvasBase, RTTPanelRaycastData> _panelDataCache
        = new Dictionary<RTTCanvasBase, RTTPanelRaycastData>();

    private RTTHitResult _currentHit;
    private RTTHitResult _previousHit;
    private GameObject _currentHoveredObject;
    private GameObject _previousHoveredObject;

    // Pointer event data reuse
    private PointerEventData _pointerEventData;
    private List<RaycastResult> _raycastResults = new List<RaycastResult>();
    #endregion

    #region Lifecycle
    private void Awake()
    {
        _instance = this;
        _pointerEventData = new PointerEventData(EventSystem.current);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
    #endregion

    #region Public API
    /// <summary>
    /// Thực hiện raycast từ một ray vào tất cả RTT panels
    /// </summary>
    public RTTHitResult Raycast(Ray ray)
    {
        _previousHit = _currentHit;
        _currentHit = new RTTHitResult();

        // Step 1: Physics raycast để tìm DisplayQuad
        if (!Physics.Raycast(ray, out RaycastHit physicsHit, maxRaycastDistance, raycastLayerMask))
        {
            HandleNoHit();
            return _currentHit;
        }

        // Step 2: Tìm RTTCanvasBase component
        var panel = FindRTTPanel(physicsHit.collider);
        if (panel == null)
        {
            HandleNoHit();
            return _currentHit;
        }

        // Step 3: Lấy hoặc tạo cache data cho panel
        var panelData = GetOrCreatePanelData(panel);

        // Step 4: Chuyển đổi UV → Screen coordinates
        Vector2 screenPos = UVToScreenPosition(physicsHit.textureCoord, panelData);

        // Step 5: Raycast trong UI space
        _raycastResults.Clear();
        _pointerEventData.position = screenPos;
        panelData.graphicRaycaster.Raycast(_pointerEventData, _raycastResults);

        // Step 6: Xử lý kết quả
        if (_raycastResults.Count > 0)
        {
            _currentHit = new RTTHitResult
            {
                isValid = true,
                panel = panel,
                worldHitPoint = physicsHit.point,
                worldHitNormal = physicsHit.normal,
                uvCoordinate = physicsHit.textureCoord,
                screenPosition = screenPos,
                hitUIElement = _raycastResults[0].gameObject,
                raycastResult = _raycastResults[0],
                distance = physicsHit.distance
            };

            if (logHitInfo)
                Debug.Log($"[RTTRaycast] Hit: {_currentHit.hitUIElement.name} at UV({physicsHit.textureCoord})");
        }
        else
        {
            // Hit panel nhưng không hit UI element
            _currentHit = new RTTHitResult
            {
                isValid = true,
                panel = panel,
                worldHitPoint = physicsHit.point,
                worldHitNormal = physicsHit.normal,
                uvCoordinate = physicsHit.textureCoord,
                screenPosition = screenPos,
                hitUIElement = null,
                distance = physicsHit.distance
            };
        }

        // Step 7: Handle hover state changes
        HandleHoverStateChanges();

        if (showDebugRays)
            Debug.DrawLine(ray.origin, physicsHit.point, Color.green);

        return _currentHit;
    }

    /// <summary>
    /// Gửi click event đến UI element đang được hover
    /// </summary>
    public bool SendClick()
    {
        if (!_currentHit.isValid || _currentHit.hitUIElement == null)
            return false;

        var button = _currentHit.hitUIElement.GetComponent<Button>();
        if (button != null && button.interactable)
        {
            // Trigger button click
            ExecuteEvents.Execute(button.gameObject, _pointerEventData, ExecuteEvents.pointerClickHandler);

            // Trigger VRButtonRipple if exists
            var ripple = _currentHit.hitUIElement.GetComponent<VRButtonRipple>();
            ripple?.TriggerRipple(_currentHit.screenPosition);

            // Mark panel dirty for re-render
            _currentHit.panel?.MarkDirty();

            return true;
        }

        // Handle other clickable elements
        var clickHandler = _currentHit.hitUIElement.GetComponent<IPointerClickHandler>();
        if (clickHandler != null)
        {
            ExecuteEvents.Execute(_currentHit.hitUIElement, _pointerEventData, ExecuteEvents.pointerClickHandler);
            _currentHit.panel?.MarkDirty();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gửi scroll event
    /// </summary>
    public void SendScroll(Vector2 scrollDelta)
    {
        if (!_currentHit.isValid || _currentHit.hitUIElement == null)
            return;

        _pointerEventData.scrollDelta = scrollDelta;
        ExecuteEvents.Execute(_currentHit.hitUIElement, _pointerEventData, ExecuteEvents.scrollHandler);
        _currentHit.panel?.MarkDirty();
    }

    /// <summary>
    /// Lấy kết quả hit hiện tại
    /// </summary>
    public RTTHitResult GetCurrentHit() => _currentHit;

    /// <summary>
    /// Kiểm tra xem có đang hover trên UI element không
    /// </summary>
    public bool IsHoveringUI() => _currentHit.isValid && _currentHit.hitUIElement != null;
    #endregion

    #region Private Methods
    private RTTCanvasBase FindRTTPanel(Collider collider)
    {
        // Check cache first
        foreach (var kvp in _panelDataCache)
        {
            if (kvp.Value.quadCollider == collider)
                return kvp.Key;
        }

        // Search in hierarchy
        var panel = collider.GetComponentInParent<RTTCanvasBase>();
        return panel;
    }

    private RTTPanelRaycastData GetOrCreatePanelData(RTTCanvasBase panel)
    {
        if (_panelDataCache.TryGetValue(panel, out var data))
            return data;

        data = new RTTPanelRaycastData
        {
            panel = panel,
            renderTexture = panel.GetRenderTexture(),
            graphicRaycaster = panel.GetGraphicRaycaster(),
            quadCollider = panel.GetQuadCollider()
        };

        _panelDataCache[panel] = data;

        // Cleanup when panel destroyed
        panel.OnRTTDestroyed += () => _panelDataCache.Remove(panel);

        return data;
    }

    private Vector2 UVToScreenPosition(Vector2 uv, RTTPanelRaycastData panelData)
    {
        var rt = panelData.renderTexture;
        if (rt == null)
            return Vector2.zero;

        // UV origin is bottom-left, screen origin is top-left
        float screenX = uv.x * rt.width;
        float screenY = (1f - uv.y) * rt.height;

        return new Vector2(screenX, screenY);
    }

    private void HandleHoverStateChanges()
    {
        _previousHoveredObject = _currentHoveredObject;
        _currentHoveredObject = _currentHit.hitUIElement;

        // Object changed
        if (_currentHoveredObject != _previousHoveredObject)
        {
            // Exit previous
            if (_previousHoveredObject != null)
            {
                ExecuteEvents.Execute(_previousHoveredObject, _pointerEventData, ExecuteEvents.pointerExitHandler);

                // Update VRButtonAnimation
                var prevAnim = _previousHoveredObject.GetComponent<VRButtonAnimation>();
                prevAnim?.OnPointerExit(null);
            }

            // Enter current
            if (_currentHoveredObject != null)
            {
                ExecuteEvents.Execute(_currentHoveredObject, _pointerEventData, ExecuteEvents.pointerEnterHandler);

                // Update VRButtonAnimation
                var currAnim = _currentHoveredObject.GetComponent<VRButtonAnimation>();
                currAnim?.OnPointerEnter(null);
            }

            // Mark panels dirty
            _previousHit.panel?.MarkDirty();
            _currentHit.panel?.MarkDirty();
        }
    }

    private void HandleNoHit()
    {
        // Exit previous hover
        if (_currentHoveredObject != null)
        {
            ExecuteEvents.Execute(_currentHoveredObject, _pointerEventData, ExecuteEvents.pointerExitHandler);

            var anim = _currentHoveredObject.GetComponent<VRButtonAnimation>();
            anim?.OnPointerExit(null);

            _previousHit.panel?.MarkDirty();
        }

        _currentHoveredObject = null;

        if (showDebugRays)
            Debug.DrawRay(transform.position, transform.forward * maxRaycastDistance, Color.red);
    }
    #endregion

    #region Cleanup
    public void ClearCache()
    {
        _panelDataCache.Clear();
    }
    #endregion
}

#region Data Structures
[System.Serializable]
public struct RTTHitResult
{
    public bool isValid;
    public RTTCanvasBase panel;
    public Vector3 worldHitPoint;
    public Vector3 worldHitNormal;
    public Vector2 uvCoordinate;
    public Vector2 screenPosition;
    public GameObject hitUIElement;
    public RaycastResult raycastResult;
    public float distance;
}

public class RTTPanelRaycastData
{
    public RTTCanvasBase panel;
    public RenderTexture renderTexture;
    public GraphicRaycaster graphicRaycaster;
    public BoxCollider quadCollider;
}
#endregion
```

### 2.2 Tích Hợp với VRGazeReticle

```csharp
// VRGazeReticle_RTT_Integration.cs
// Thêm vào VRGazeReticle.cs hoặc tạo partial class

public partial class VRGazeReticle
{
    [Header("RTT Integration")]
    [SerializeField] private bool useRTTRaycast = true;

    private RTTRaycastManager _rttRaycastManager;

    private void InitializeRTTIntegration()
    {
        _rttRaycastManager = RTTRaycastManager.Instance;
    }

    // Thay thế phần CheckGaze() hiện tại
    private void CheckGaze_RTT()
    {
        if (!useRTTRaycast || _rttRaycastManager == null)
        {
            // Fallback về physics raycast cũ
            CheckGaze_Legacy();
            return;
        }

        Ray gazeRay = new Ray(
            mainCamera.transform.position,
            mainCamera.transform.forward
        );

        var hitResult = _rttRaycastManager.Raycast(gazeRay);

        if (hitResult.isValid)
        {
            // Update reticle position
            UpdateReticlePosition(hitResult.worldHitPoint, hitResult.distance);

            if (hitResult.hitUIElement != null)
            {
                // Có UI element - xử lý dwell
                currentHitObject = hitResult.hitUIElement;
                isHoveringInteractable = true;

                ProcessDwellClick_RTT(hitResult);
            }
            else
            {
                // Hit panel nhưng không hit element
                currentHitObject = null;
                isHoveringInteractable = false;
                ResetDwell();
            }
        }
        else
        {
            // Không hit gì
            currentHitObject = null;
            isHoveringInteractable = false;
            ResetDwell();
            UpdateReticleToDefaultPosition();
        }
    }

    private void ProcessDwellClick_RTT(RTTHitResult hitResult)
    {
        // Kiểm tra movement threshold
        float movement = Vector3.Angle(
            mainCamera.transform.forward,
            lastGazeDirection
        );

        if (movement > movementThreshold)
        {
            ResetDwell();
            lastGazeDirection = mainCamera.transform.forward;
            return;
        }

        // Update dwell timer
        if (!isDwelling)
        {
            stillnessTimer += Time.deltaTime;

            if (stillnessTimer >= stillnessDelay)
            {
                StartDwell();
            }
        }
        else
        {
            dwellTimer += Time.deltaTime;
            UpdateDwellRing(dwellTimer / dwellDuration);

            if (dwellTimer >= dwellDuration)
            {
                // Execute click
                bool clicked = _rttRaycastManager.SendClick();

                if (clicked)
                {
                    PlayClickFeedback();
                }

                ResetDwell();
            }
        }
    }
}
```

---

## 🔧 Phase 3: Component Migration (Chi Tiết)

### 3.1 RTTMenuFrame (Migrate từ VRMenuFrame)

```csharp
// RTTMenuFrame.cs
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RTT version của VRMenuFrame - giữ nguyên tất cả visual effects
/// </summary>
public class RTTMenuFrame : RTTCanvasBase
{
    #region Configuration (Giữ nguyên từ VRMenuFrame)
    [Header("Panel Settings")]
    [SerializeField] private float panelWidth = 1.6f;
    [SerializeField] private float panelHeight = 0.9f;
    [SerializeField] private float logicalWidth = 1920f;

    [Header("Content Margins")]
    [SerializeField] private float marginLeft = 75f;
    [SerializeField] private float marginRight = 75f;
    [SerializeField] private float marginTop = 50f;
    [SerializeField] private float marginBottom = 50f;

    [Header("Glass Effect")]
    [SerializeField] private Color glassColor = new Color(1f, 1f, 1f, 0.098f);
    [SerializeField] private float cornerRadius = 0.12f;

    [Header("Glow Colors")]
    [SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);
    [SerializeField] private float glowExpansion = 0.02f;

    [Header("Separators")]
    [SerializeField] private float[] horizontalSeparators = new float[0];
    [SerializeField] private float[] verticalSeparators = new float[0];

    [Header("Effects")]
    [SerializeField] private bool enableFloatingData = true;
    [SerializeField] private float shimmerSpeed = 0.1f;
    #endregion

    #region Private Fields
    private RectTransform _glassBackground;
    private RectTransform _glowingBorder;
    private RectTransform _contentContainer;
    private FloatingDataAnim _floatingDataAnim;

    // Materials
    private Material _glassMaterial;
    private Material _borderMaterial;
    #endregion

    #region Primary Instance (giữ nguyên pattern)
    private static RTTMenuFrame _primaryInstance;
    public static RTTMenuFrame PrimaryInstance => _primaryInstance;

    [SerializeField] private bool isPrimary = true;
    public bool IsPrimary => isPrimary;
    #endregion

    #region Override Methods
    protected override void Awake()
    {
        // Set world dimensions
        worldWidth = panelWidth;
        worldHeight = panelHeight;

        base.Awake();
    }

    protected override void Start()
    {
        base.Start();

        if (isPrimary)
        {
            _primaryInstance = this;
        }
    }

    protected override Vector2Int GetResolution()
    {
        // Tính resolution dựa trên logical width và aspect ratio
        float aspectRatio = panelWidth / panelHeight;
        int height = Mathf.RoundToInt(logicalWidth / aspectRatio);
        return new Vector2Int((int)logicalWidth, height);
    }

    protected override int GetCameraDepth()
    {
        // Menu frame render trước
        return -50;
    }

    protected override void BuildUI()
    {
        CreateGlassBackground();
        CreateGlowingBorder();
        CreateContentContainer();

        if (enableFloatingData)
        {
            CreateFloatingDataEffect();
        }

        SetupMaterialProperties();
    }

    protected override void SetupFallbackWorldSpaceCanvas()
    {
        // Fallback: Tạo World Space Canvas như VRMenuFrame cũ
        Debug.LogWarning("[RTTMenuFrame] Using fallback World Space Canvas");

        var canvasGO = new GameObject("FallbackCanvas");
        canvasGO.transform.SetParent(transform);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(logicalWidth, logicalWidth / (panelWidth / panelHeight));

        float scaleFactor = panelWidth / logicalWidth;
        canvasGO.transform.localScale = Vector3.one * scaleFactor;

        // Build UI với canvas này
        _canvas = canvas;
        BuildUI();
    }
    #endregion

    #region UI Building
    private void CreateGlassBackground()
    {
        var go = new GameObject("GlassBackground");
        go.transform.SetParent(_canvas.transform, false);
        go.layer = _canvas.gameObject.layer;

        var image = go.AddComponent<Image>();
        _glassMaterial = new Material(Shader.Find("VRWorkspace/GlassGradientBackground"));
        image.material = _glassMaterial;

        _glassBackground = go.GetComponent<RectTransform>();
        _glassBackground.anchorMin = Vector2.zero;
        _glassBackground.anchorMax = Vector2.one;
        _glassBackground.offsetMin = Vector2.zero;
        _glassBackground.offsetMax = Vector2.zero;
    }

    private void CreateGlowingBorder()
    {
        var go = new GameObject("GlowingBorder");
        go.transform.SetParent(_glassBackground, false);
        go.layer = _canvas.gameObject.layer;

        var image = go.AddComponent<Image>();
        _borderMaterial = new Material(Shader.Find("VRWorkspace/GlowingGlassBorder"));
        image.material = _borderMaterial;

        _glowingBorder = go.GetComponent<RectTransform>();

        // Expand slightly for glow
        float expansion = glowExpansion * logicalWidth;
        _glowingBorder.anchorMin = Vector2.zero;
        _glowingBorder.anchorMax = Vector2.one;
        _glowingBorder.offsetMin = new Vector2(-expansion, -expansion);
        _glowingBorder.offsetMax = new Vector2(expansion, expansion);
    }

    private void CreateContentContainer()
    {
        var go = new GameObject("ContentContainer");
        go.transform.SetParent(_glassBackground, false);
        go.layer = _canvas.gameObject.layer;

        _contentContainer = go.AddComponent<RectTransform>();
        _contentContainer.anchorMin = Vector2.zero;
        _contentContainer.anchorMax = Vector2.one;
        _contentContainer.offsetMin = new Vector2(marginLeft, marginBottom);
        _contentContainer.offsetMax = new Vector2(-marginRight, -marginTop);
    }

    private void CreateFloatingDataEffect()
    {
        var go = new GameObject("FX_DataStream");
        go.transform.SetParent(_glassBackground, false);
        go.layer = _canvas.gameObject.layer;

        // Mask để clip particles trong panel
        var mask = go.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var maskImage = go.AddComponent<Image>();
        maskImage.color = Color.clear;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Add FloatingDataAnim component
        _floatingDataAnim = go.AddComponent<FloatingDataAnim>();

        // Subscribe to animation changes for dirty flag
        _floatingDataAnim.OnAnimationUpdate += MarkDirty;
    }

    private void SetupMaterialProperties()
    {
        if (_glassMaterial != null)
        {
            _glassMaterial.SetColor("_GlassColor", glassColor);
            _glassMaterial.SetFloat("_CornerRadius", cornerRadius);
            _glassMaterial.SetColor("_GlowColorA", glowColorA);
            _glassMaterial.SetColor("_GlowColorB", glowColorB);
        }

        if (_borderMaterial != null)
        {
            _borderMaterial.SetColor("_GlowColorA", glowColorA);
            _borderMaterial.SetColor("_GlowColorB", glowColorB);
            _borderMaterial.SetFloat("_ShimmerSpeed", shimmerSpeed);
            _borderMaterial.SetFloat("_CornerRadius", cornerRadius);

            // Setup separators
            SetupSeparators();
        }
    }

    private void SetupSeparators()
    {
        // Pack separator positions into shader
        // ... (giữ nguyên logic từ VRMenuFrame)
    }
    #endregion

    #region Public API
    public RectTransform GetContentContainer() => _contentContainer;

    public void SetContent(GameObject content)
    {
        content.transform.SetParent(_contentContainer, false);
        MarkDirty();
    }

    public void UpdateGlowColors(Color colorA, Color colorB)
    {
        glowColorA = colorA;
        glowColorB = colorB;

        _glassMaterial?.SetColor("_GlowColorA", colorA);
        _glassMaterial?.SetColor("_GlowColorB", colorB);
        _borderMaterial?.SetColor("_GlowColorA", colorA);
        _borderMaterial?.SetColor("_GlowColorB", colorB);

        MarkDirty();
    }
    #endregion

    #region Cleanup
    protected override void Cleanup()
    {
        if (_primaryInstance == this)
            _primaryInstance = null;

        if (_glassMaterial != null)
            Destroy(_glassMaterial);
        if (_borderMaterial != null)
            Destroy(_borderMaterial);

        base.Cleanup();
    }
    #endregion
}
```

### 3.2 RTTTaskbar (Migrate từ VRTaskbar)

```csharp
// RTTTaskbar.cs - Skeleton với key points
public class RTTTaskbar : RTTCanvasBase
{
    #region Configuration
    [Header("Taskbar Settings")]
    [SerializeField] private float logicalHeight = 80f;
    [SerializeField] private float buttonSize = 44f;
    [SerializeField] private float iconSize = 28f;
    [SerializeField] private float spacing = 16f;

    [Header("Positioning")]
    [SerializeField] private float spacingFromMenu = 20f;
    [SerializeField] private RTTMenuFrame targetMenuFrame;
    #endregion

    #region Override
    protected override Vector2Int GetResolution()
    {
        // Match menu frame width, fixed height
        if (targetMenuFrame != null)
        {
            var menuRes = targetMenuFrame.GetResolution();
            return new Vector2Int(menuRes.x, (int)logicalHeight);
        }
        return new Vector2Int(1920, (int)logicalHeight);
    }

    protected override int GetCameraDepth()
    {
        // Render sau menu frame
        return -49;
    }

    protected override void BuildUI()
    {
        // Giữ nguyên logic từ VRTaskbar.BuildUI()
        CreateThreeSections();
        CreateControlButtons();    // Section 1
        CreateAppButtons();        // Section 2
        CreateStatusDisplay();     // Section 3
    }
    #endregion

    #region Position Relative to Menu
    private void LateUpdate()
    {
        base.LateUpdate();
        UpdatePositionRelativeToMenu();
    }

    private void UpdatePositionRelativeToMenu()
    {
        if (targetMenuFrame == null) return;

        var menuQuad = targetMenuFrame.GetDisplayQuad();
        if (menuQuad == null) return;

        // Position below menu
        Vector3 menuPos = menuQuad.transform.position;
        float menuHeight = targetMenuFrame.panelHeight;
        float taskbarHeight = worldHeight;

        Vector3 newPos = menuPos;
        newPos.y -= (menuHeight / 2f) + (spacingFromMenu / 1000f) + (taskbarHeight / 2f);

        _displayQuad.transform.position = newPos;
        _displayQuad.transform.rotation = menuQuad.transform.rotation;
    }
    #endregion
}
```

---

## 🧪 Phase 4: Testing Strategy

### 4.1 Unit Tests

```csharp
// RTTTests.cs
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[TestFixture]
public class RTTTests
{
    [Test]
    public void RenderTexture_CreatesCorrectDimensions()
    {
        var rt = new RenderTexture(1920, 1080, 24);
        Assert.AreEqual(1920, rt.width);
        Assert.AreEqual(1080, rt.height);
        rt.Release();
    }

    [Test]
    public void UVToScreen_ConvertsCorrectly()
    {
        // UV (0,0) = bottom-left → Screen (0, height)
        Vector2 uv = new Vector2(0, 0);
        Vector2 screen = new Vector2(uv.x * 1920, (1 - uv.y) * 1080);
        Assert.AreEqual(0, screen.x);
        Assert.AreEqual(1080, screen.y);

        // UV (1,1) = top-right → Screen (width, 0)
        uv = new Vector2(1, 1);
        screen = new Vector2(uv.x * 1920, (1 - uv.y) * 1080);
        Assert.AreEqual(1920, screen.x);
        Assert.AreEqual(0, screen.y);

        // UV (0.5, 0.5) = center → Screen (width/2, height/2)
        uv = new Vector2(0.5f, 0.5f);
        screen = new Vector2(uv.x * 1920, (1 - uv.y) * 1080);
        Assert.AreEqual(960, screen.x);
        Assert.AreEqual(540, screen.y);
    }
}
```

### 4.2 Integration Test Checklist

| Test Case | Expected Result | Priority |
|-----------|-----------------|----------|
| **RTT Creation** |||
| Create RTTMenuFrame | RenderTexture 1920x1080 created | P0 |
| Create RTTTaskbar | RenderTexture matches menu width | P0 |
| Multiple RTT panels | Each has unique RenderTexture | P0 |
| **Raycast** |||
| Gaze at button center | Correct button highlighted | P0 |
| Gaze at button edge | Still hits button | P1 |
| Gaze between buttons | No button highlighted | P0 |
| Gaze at panel edge | Hits panel, no element | P1 |
| **Click** |||
| Dwell click on button | Button callback triggered | P0 |
| Dwell click on InputField | Keyboard opens | P0 |
| Dwell click on Dropdown | Dropdown expands | P0 |
| **Visual** |||
| Glass gradient visible | Cyan-purple gradient | P0 |
| Border shimmer animating | Shimmer moves smoothly | P1 |
| Hover glow increases | 4x border width on hover | P0 |
| Floating data particles | Moving particles visible | P2 |
| **Performance** |||
| Dirty flag prevents render | Camera disabled when idle | P1 |
| Memory usage stable | No leak after 5 min | P0 |

### 4.3 Performance Benchmarks

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| Frame time impact | < 2ms per RTT panel | Unity Profiler |
| Memory per panel | < 16MB (1920x1080 ARGB + AA) | Memory Profiler |
| Raycast time | < 0.1ms | Stopwatch in code |
| UI update time | < 0.5ms | Unity Profiler |

---

## 📊 Phase 5: Performance Optimization

### 5.1 Memory Management

```csharp
// RTTMemoryManager.cs
public class RTTMemoryManager : MonoBehaviour
{
    [Header("Memory Limits")]
    [SerializeField] private float maxTextureMemoryMB = 128f;
    [SerializeField] private float warningThresholdMB = 100f;

    [Header("Auto-scaling")]
    [SerializeField] private bool enableAutoScaling = true;
    [SerializeField] private float scaleDownThresholdMB = 110f;
    [SerializeField] private float scaleUpThresholdMB = 80f;

    private RTTQualityLevel _currentQuality = RTTQualityLevel.High;

    private void Update()
    {
        if (!enableAutoScaling) return;

        var stats = RTTManager.Instance.GetPerformanceStats();

        if (stats.totalTextureMemoryMB > scaleDownThresholdMB &&
            _currentQuality != RTTQualityLevel.Low)
        {
            ScaleDown();
        }
        else if (stats.totalTextureMemoryMB < scaleUpThresholdMB &&
                 _currentQuality != RTTQualityLevel.High)
        {
            ScaleUp();
        }
    }

    private void ScaleDown()
    {
        _currentQuality = _currentQuality == RTTQualityLevel.High
            ? RTTQualityLevel.Medium
            : RTTQualityLevel.Low;
        RTTManager.Instance.SetQualityLevel(_currentQuality);
        Debug.Log($"[RTTMemory] Scaled down to {_currentQuality}");
    }

    private void ScaleUp()
    {
        _currentQuality = _currentQuality == RTTQualityLevel.Low
            ? RTTQualityLevel.Medium
            : RTTQualityLevel.High;
        RTTManager.Instance.SetQualityLevel(_currentQuality);
        Debug.Log($"[RTTMemory] Scaled up to {_currentQuality}");
    }
}
```

### 5.2 Dirty Flag Implementation Chi Tiết

```csharp
// Trong RTTCanvasBase, bổ sung:

public class DirtyFlagTracker
{
    private bool _layoutDirty;
    private bool _visualDirty;
    private bool _contentDirty;
    private float _lastDirtyTime;

    public bool IsDirty => _layoutDirty || _visualDirty || _contentDirty;

    public void MarkLayoutDirty()
    {
        _layoutDirty = true;
        _lastDirtyTime = Time.time;
    }

    public void MarkVisualDirty()
    {
        _visualDirty = true;
        _lastDirtyTime = Time.time;
    }

    public void MarkContentDirty()
    {
        _contentDirty = true;
        _lastDirtyTime = Time.time;
    }

    public void Clear()
    {
        _layoutDirty = false;
        _visualDirty = false;
        _contentDirty = false;
    }

    public float TimeSinceLastDirty => Time.time - _lastDirtyTime;
}

// Usage:
// - Button hover/unhover → MarkVisualDirty()
// - Text change → MarkContentDirty()
// - Window resize → MarkLayoutDirty()
```

---

## 🔄 Phase 6: Migration Path

### 6.1 Feature Toggle Approach

```csharp
// RTTFeatureToggle.cs
public static class RTTFeatureToggle
{
    private const string PREF_KEY = "VRWorkspace_UseRTT";

    public static bool UseRTT
    {
        get => PlayerPrefs.GetInt(PREF_KEY, 0) == 1;
        set => PlayerPrefs.SetInt(PREF_KEY, value ? 1 : 0);
    }
}

// Trong VRMenuFrame.cs:
private void Start()
{
    if (RTTFeatureToggle.UseRTT)
    {
        // Disable self, enable RTT version
        var rttFrame = GetComponent<RTTMenuFrame>();
        if (rttFrame != null)
        {
            rttFrame.enabled = true;
            this.enabled = false;
        }
    }
}
```

### 6.2 Rollback Plan

| Trigger | Action |
|---------|--------|
| RTT initialization fails | Auto-fallback to World Space |
| Memory > 200MB | Auto-fallback to World Space |
| Frame rate < 72 FPS for 5s | Alert user, suggest fallback |
| User reports issues | Toggle off via Settings menu |

### 6.3 Phased Rollout

| Phase | Component | Feature Flag | Rollback |
|-------|-----------|--------------|----------|
| Alpha | RTTMenuFrame only | `RTT_MENU_ALPHA` | World Space VRMenuFrame |
| Beta | + RTTTaskbar | `RTT_TASKBAR_BETA` | World Space VRTaskbar |
| RC | + RTTKeyboard | `RTT_KEYBOARD_RC` | World Space VRKeyboard |
| Release | All components | `RTT_ENABLED` | Full World Space stack |

---

## ⚠️ Xử Lý GrabPass/Glassmorphism

### Vấn Đề

RTT camera render vào texture riêng → không có scene phía sau để grab → glassmorphism blur không hoạt động.

### Giải Pháp Đề Xuất

#### Option A: Pre-rendered Background (Recommended)

```csharp
// RTTBackgroundCapture.cs
public class RTTBackgroundCapture : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private RenderTexture backgroundCapture;
    [SerializeField] private Material blurMaterial;
    [SerializeField] private int blurIterations = 4;

    private RenderTexture _blurredBackground;

    public void CaptureAndBlur()
    {
        // 1. Capture scene từ main camera (không có UI)
        mainCamera.targetTexture = backgroundCapture;
        mainCamera.Render();
        mainCamera.targetTexture = null;

        // 2. Apply blur
        _blurredBackground = ApplyGaussianBlur(backgroundCapture, blurIterations);

        // 3. Pass to RTT glass shader
        Shader.SetGlobalTexture("_BlurredBackground", _blurredBackground);
    }

    private RenderTexture ApplyGaussianBlur(RenderTexture source, int iterations)
    {
        // Gaussian blur implementation
        var temp = RenderTexture.GetTemporary(source.descriptor);
        Graphics.Blit(source, temp);

        for (int i = 0; i < iterations; i++)
        {
            var temp2 = RenderTexture.GetTemporary(source.descriptor);
            Graphics.Blit(temp, temp2, blurMaterial, 0); // Horizontal
            RenderTexture.ReleaseTemporary(temp);
            temp = temp2;

            temp2 = RenderTexture.GetTemporary(source.descriptor);
            Graphics.Blit(temp, temp2, blurMaterial, 1); // Vertical
            RenderTexture.ReleaseTemporary(temp);
            temp = temp2;
        }

        return temp;
    }
}
```

#### Option B: Static Noise Texture

Sử dụng pre-generated noise texture thay vì dynamic blur:
- Pros: Zero runtime cost
- Cons: Không phản ánh scene thực tế

#### Option C: Hybrid Approach

- Capture background mỗi 0.5s (không real-time)
- Blur một lần khi capture
- Update khi user di chuyển đáng kể

---

## 📁 File Structure (Final)

```
📁 Scripts/UI/RTT/
├── Core/
│   ├── RTTCanvasBase.cs           ← Abstract base class
│   ├── RTTConfig.cs               ← ScriptableObject config
│   ├── RTTManager.cs              ← Singleton coordinator
│   └── RTTMemoryManager.cs        ← Memory auto-scaling
│
├── Components/
│   ├── RTTMenuFrame.cs            ← Migrated VRMenuFrame
│   ├── RTTTaskbar.cs              ← Migrated VRTaskbar
│   ├── RTTMobileKeyboard.cs       ← Migrated VRMobileKeyboard
│   └── RTTKeyboard.cs             ← Migrated VRKeyboard
│
├── Input/
│   ├── RTTRaycastManager.cs       ← UV-based raycast
│   └── RTTPointerEventHandler.cs  ← Pointer event dispatch
│
├── Effects/
│   ├── RTTBackgroundCapture.cs    ← Glassmorphism workaround
│   └── RTTDirtyFlagTracker.cs     ← Dirty flag system
│
└── Debug/
    ├── RTTDebugOverlay.cs         ← Performance stats display
    └── RTTGizmoDrawer.cs          ← Editor visualization
```

---

## ✅ Checklist Tổng Hợp

### Pre-Implementation
- [ ] Tạo Layer "UI_RTT" trong project
- [ ] Tạo RTTConfig ScriptableObject
- [ ] Setup quality presets (Low/Medium/High)

### Phase 1: Infrastructure
- [ ] RTTCanvasBase implemented
- [ ] RTTManager singleton working
- [ ] RTTMemoryManager tracking memory

### Phase 2: Raycast
- [ ] RTTRaycastManager implemented
- [ ] UV→Screen conversion accurate
- [ ] Hover state changes working
- [ ] Click events dispatching

### Phase 3: Components
- [ ] RTTMenuFrame rendering correctly
- [ ] RTTTaskbar positioned correctly
- [ ] RTTMobileKeyboard functional
- [ ] All visual effects preserved

### Phase 4: Testing
- [ ] Unit tests passing
- [ ] Integration tests passing
- [ ] Performance benchmarks met
- [ ] Memory stable over time

### Phase 5: Optimization
- [ ] Dirty flag reducing renders
- [ ] Memory auto-scaling working
- [ ] No frame drops in VR

### Phase 6: Release
- [ ] Feature toggle implemented
- [ ] Rollback tested
- [ ] Documentation updated
- [ ] User testing complete

---

## 📝 Ghi Chú Bổ Sung

### Compatibility Notes

| Unity Version | Status | Notes |
|--------------|--------|-------|
| 2021.3 LTS | ✅ Supported | Primary target |
| 2022.3 LTS | ✅ Supported | Tested |
| 2023.x | ⚠️ Untested | May need adjustments |

### Known Limitations

1. **Text Sharpness**: RTT có thể làm text hơi mờ → cần MSAA 4x+
2. **Dynamic Resolution**: Không support VRS (Variable Rate Shading) trên RTT
3. **Transparency Sorting**: Nhiều RTT panels có thể gây issues → cần manage render order

### Resources

- [Unity RenderTexture Best Practices](https://docs.unity3d.com/Manual/class-RenderTexture.html)
- [VR UI Design Guidelines](https://developer.oculus.com/resources/design-ui/)
- [Screen Space UI in VR](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.0/manual/ui-setup.html)
