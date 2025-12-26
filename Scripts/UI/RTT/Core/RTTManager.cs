using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton manager for all RTT (Render-to-Texture) panels.
/// Handles:
/// - Panel registration and tracking
/// - Camera depth assignment for render ordering
/// - Quality level management
/// - Performance statistics
/// - Memory monitoring
/// </summary>
public class RTTManager : MonoBehaviour
{
    #region Singleton
    private static RTTManager _instance;
    private static bool _applicationQuitting = false;

    public static RTTManager Instance
    {
        get
        {
            if (_applicationQuitting)
                return null;

            if (_instance == null)
            {
                // Try to find existing instance
                _instance = FindObjectOfType<RTTManager>();

                if (_instance == null)
                {
                    // Create new instance
                    var go = new GameObject("RTTManager");
                    _instance = go.AddComponent<RTTManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    #endregion

    #region Configuration
    [Header("Configuration")]
    [SerializeField] private RTTConfig defaultConfig;

    [Header("Performance")]
#pragma warning disable 0414 // Reserved for render throttling feature
    [Tooltip("Maximum number of panels that can render in a single frame")]
    [SerializeField] private int maxConcurrentRenders = 3;
#pragma warning restore 0414

    [Tooltip("Enable performance logging to console")]
    [SerializeField] private bool enablePerformanceLogging = false;

    [Header("Camera Depth")]
    [Tooltip("Starting camera depth for RTT cameras (decrements for each panel)")]
    [SerializeField] private int startingCameraDepth = -100;
    #endregion

    #region Private Fields
    private List<RTTCanvasBase> _registeredPanels = new List<RTTCanvasBase>();
    private Queue<RTTCanvasBase> _renderQueue = new Queue<RTTCanvasBase>();
    private int _currentCameraDepth;

    // Quality management
    private RTTQualityLevel _currentQualityLevel = RTTQualityLevel.High;

    // Performance tracking
    private float _lastFrameRenderTime;
    private int _rendersThisFrame;
    private float _totalMemoryMB;

    // Memory management
    private float _lastMemoryCheck;
    private const float MEMORY_CHECK_INTERVAL = 1.0f;
    #endregion

    #region Properties
    /// <summary>Current quality level</summary>
    public RTTQualityLevel CurrentQualityLevel => _currentQualityLevel;

    /// <summary>Number of registered panels</summary>
    public int RegisteredPanelCount => _registeredPanels.Count;

    /// <summary>Number of visible panels</summary>
    public int VisiblePanelCount => _registeredPanels.FindAll(p => p != null && p.IsVisible).Count;

    /// <summary>Default RTT configuration</summary>
    public RTTConfig DefaultConfig => defaultConfig;
    #endregion

    #region Lifecycle
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        _currentCameraDepth = startingCameraDepth;

        // Load default config if not assigned
        if (defaultConfig == null)
        {
            defaultConfig = Resources.Load<RTTConfig>("RTTConfig");
        }
    }

    private void Update()
    {
        // Periodic memory check
        if (Time.unscaledTime - _lastMemoryCheck >= MEMORY_CHECK_INTERVAL)
        {
            _lastMemoryCheck = Time.unscaledTime;
            UpdateMemoryStats();
            CheckMemoryThresholds();
        }
    }

    private void OnApplicationQuit()
    {
        _applicationQuitting = true;
    }
    #endregion

    #region Panel Registration
    /// <summary>
    /// Register a panel with the manager
    /// </summary>
    public void RegisterPanel(RTTCanvasBase panel)
    {
        if (panel == null) return;

        if (!_registeredPanels.Contains(panel))
        {
            _registeredPanels.Add(panel);

            // Subscribe to destruction event for auto-cleanup
            panel.OnRTTDestroyed += () => UnregisterPanel(panel);

            if (enablePerformanceLogging)
            {
                Debug.Log($"[RTTManager] Registered: {panel.GetType().Name} (Total: {_registeredPanels.Count})");
            }
        }
    }

    /// <summary>
    /// Unregister a panel from the manager
    /// </summary>
    public void UnregisterPanel(RTTCanvasBase panel)
    {
        if (panel == null) return;

        if (_registeredPanels.Remove(panel))
        {
            if (enablePerformanceLogging)
            {
                Debug.Log($"[RTTManager] Unregistered: {panel.GetType().Name} (Total: {_registeredPanels.Count})");
            }
        }
    }

    /// <summary>
    /// Assign a unique camera depth for render ordering
    /// </summary>
    public int AssignCameraDepth()
    {
        return _currentCameraDepth--;
    }

    /// <summary>
    /// Get all registered panels
    /// </summary>
    public IReadOnlyList<RTTCanvasBase> GetRegisteredPanels()
    {
        return _registeredPanels.AsReadOnly();
    }

    /// <summary>
    /// Find a panel by type
    /// </summary>
    public T FindPanel<T>() where T : RTTCanvasBase
    {
        foreach (var panel in _registeredPanels)
        {
            if (panel is T typedPanel)
                return typedPanel;
        }
        return null;
    }
    #endregion

    #region Quality Management
    /// <summary>
    /// Set the quality level for all panels
    /// </summary>
    public void SetQualityLevel(RTTQualityLevel level)
    {
        if (_currentQualityLevel == level) return;

        _currentQualityLevel = level;
        var preset = GetQualityPreset(level);

        if (enablePerformanceLogging)
        {
            Debug.Log($"[RTTManager] Quality changed to {level}: {preset.width}x{preset.height} AA={preset.antiAliasing}");
        }

        // Apply to all panels
        foreach (var panel in _registeredPanels)
        {
            if (panel == null) continue;

            var resolution = panel.CurrentResolution;
            int newWidth = Mathf.RoundToInt(resolution.x * preset.renderScale);
            int newHeight = Mathf.RoundToInt(resolution.y * preset.renderScale);
            panel.ResizeRenderTexture(newWidth, newHeight);
        }
    }

    /// <summary>
    /// Get quality preset for a level
    /// </summary>
    public RTTQualityPreset GetQualityPreset(RTTQualityLevel level)
    {
        if (defaultConfig != null)
        {
            return defaultConfig.GetPreset(level);
        }

        // Fallback defaults
        switch (level)
        {
            case RTTQualityLevel.Low:
                return new RTTQualityPreset { name = "Low", width = 1280, height = 720, antiAliasing = 2, renderScale = 0.75f };
            case RTTQualityLevel.Medium:
                return new RTTQualityPreset { name = "Medium", width = 1600, height = 900, antiAliasing = 4, renderScale = 0.875f };
            default:
                return new RTTQualityPreset { name = "High", width = 1920, height = 1080, antiAliasing = 4, renderScale = 1.0f };
        }
    }

    /// <summary>
    /// Scale up quality if memory allows
    /// </summary>
    public void TryScaleUp()
    {
        if (_currentQualityLevel == RTTQualityLevel.High) return;

        float threshold = defaultConfig != null ? defaultConfig.maxTextureMemoryMB * 0.6f : 80f;

        if (_totalMemoryMB < threshold)
        {
            var newLevel = _currentQualityLevel == RTTQualityLevel.Low
                ? RTTQualityLevel.Medium
                : RTTQualityLevel.High;
            SetQualityLevel(newLevel);
        }
    }

    /// <summary>
    /// Scale down quality to reduce memory
    /// </summary>
    public void TryScaleDown()
    {
        if (_currentQualityLevel == RTTQualityLevel.Low) return;

        var newLevel = _currentQualityLevel == RTTQualityLevel.High
            ? RTTQualityLevel.Medium
            : RTTQualityLevel.Low;
        SetQualityLevel(newLevel);
    }
    #endregion

    #region Memory Management
    private void UpdateMemoryStats()
    {
        _totalMemoryMB = CalculateTotalTextureMemory() / (1024f * 1024f);
    }

    private void CheckMemoryThresholds()
    {
        if (defaultConfig == null || !defaultConfig.enableAutoScaling) return;

        if (_totalMemoryMB > defaultConfig.maxTextureMemoryMB)
        {
            TryScaleDown();
        }
        else if (_totalMemoryMB < defaultConfig.maxTextureMemoryMB * 0.5f)
        {
            TryScaleUp();
        }
    }

    private long CalculateTotalTextureMemory()
    {
        long total = 0;
        foreach (var panel in _registeredPanels)
        {
            if (panel == null) continue;

            var rt = panel.GetRenderTexture();
            if (rt != null)
            {
                // Estimate: width * height * 4 bytes (ARGB) * AA samples
                total += (long)rt.width * rt.height * 4 * rt.antiAliasing;
            }
        }
        return total;
    }
    #endregion

    #region Performance Statistics
    /// <summary>
    /// Get current performance statistics
    /// </summary>
    public RTTPerformanceStats GetPerformanceStats()
    {
        return new RTTPerformanceStats
        {
            totalPanels = _registeredPanels.Count,
            visiblePanels = VisiblePanelCount,
            totalTextureMemoryMB = _totalMemoryMB,
            averageRenderTimeMs = _lastFrameRenderTime,
            currentQuality = _currentQualityLevel
        };
    }

    /// <summary>
    /// Log current performance stats to console
    /// </summary>
    public void LogPerformanceStats()
    {
        var stats = GetPerformanceStats();
        Debug.Log($"[RTTManager] {stats}");
    }
    #endregion

    #region Utility
    /// <summary>
    /// Mark all panels as dirty (force re-render)
    /// </summary>
    public void MarkAllDirty()
    {
        foreach (var panel in _registeredPanels)
        {
            panel?.MarkDirty();
        }
    }

    /// <summary>
    /// Hide all panels
    /// </summary>
    public void HideAll()
    {
        foreach (var panel in _registeredPanels)
        {
            panel?.Hide();
        }
    }

    /// <summary>
    /// Show all panels
    /// </summary>
    public void ShowAll()
    {
        foreach (var panel in _registeredPanels)
        {
            panel?.Show();
        }
    }

    /// <summary>
    /// Cleanup destroyed panels from list
    /// </summary>
    public void CleanupDestroyedPanels()
    {
        _registeredPanels.RemoveAll(p => p == null);
    }
    #endregion
}
