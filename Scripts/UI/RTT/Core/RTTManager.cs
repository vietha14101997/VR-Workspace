using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.WebRTC;
using VRWorkspace.Utils;
using VRWorkspace.Streaming;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;
#if UNITY_ANDROID && !UNITY_EDITOR
using Google.XR.Cardboard;
#endif

/// <summary>
/// Unified RTT Manager - consolidates RTTManager, RTTMenuManager, and RTTAppManager.
/// Handles:
/// - Panel registration and tracking
/// - Camera depth assignment for render ordering
/// - Quality level management and memory monitoring
/// - Menu state and navigation
/// - Multi-app lifecycle management
/// - Theme management (centralized colors)
/// - Font management (centralized typography)
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
            if (_applicationQuitting) return null;

            if (_instance == null)
            {
                _instance = FindObjectOfType<RTTManager>();

                if (_instance == null)
                {
                    var go = new GameObject("RTTManager");
                    _instance = go.AddComponent<RTTManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    #endregion

    #region Configuration Assets
    [Header("Configuration Assets")]
    [SerializeField] private RTTConfig rttConfig;
    [SerializeField] private RTTThemeConfig themeConfig;
    [SerializeField] private RTTAppRegistry appRegistry;

    [Header("Typography")]
    [SerializeField] private TMP_FontAsset primaryFont;
    #endregion

    #region Panel Management Config
    [Header("Panel Management")]
    [Tooltip("Maximum number of panels that can render in a single frame")]
#pragma warning disable CS0414 // Field is assigned but never used - exposed for Inspector configuration
    [SerializeField] private int maxConcurrentRenders = 3;
#pragma warning restore CS0414

    [Tooltip("Enable performance logging to console")]
    [SerializeField] private bool enablePerformanceLogging = false;

    [Tooltip("Starting camera depth for RTT cameras")]
    [SerializeField] private int startingCameraDepth = -100;
    #endregion

    #region Menu/App References
    [Header("Menu References")]
    [SerializeField] private RTTMenu menu;
    [SerializeField] private RTTMenuFrame mainMenuFrame;
    [SerializeField] private RTTTaskbar taskbar;

    [Header("Controllers")]
    [SerializeField] private RTTMainMenuController mainMenuController;
    [SerializeField] private RTTRemoteMenuController remoteMenuController;

    [Header("Frame Spawning")]
    [SerializeField] private Transform frameParent;
    [SerializeField] private WorldPanelPlus panelPrefab;

    [Header("Transition Animation")]
    [SerializeField] private float transitionOutDuration = 0.1f;
    [SerializeField] private float transitionInDuration = 0.15f;
    [SerializeField] private bool useFadeTransition = true;
    [SerializeField] private bool useScaleTransition = false;

    [Header("Auto Init")]
    [SerializeField] private bool autoShowMainMenu = true;

    [Header("Auto Recenter")]
    [Tooltip("Automatically recenter objects in front of user when app starts")]
    [SerializeField] private bool autoRecenterOnStart = true;
    [Tooltip("Delay in seconds before auto-recenter (allows camera tracking to stabilize)")]
    [SerializeField] private float autoRecenterDelay = 0.5f;
    #endregion

    #region Panel Management Fields
    private List<RTTCanvasBase> _registeredPanels = new List<RTTCanvasBase>();
    private Queue<RTTCanvasBase> _renderQueue = new Queue<RTTCanvasBase>();
    private int _currentCameraDepth;
    private RTTQualityLevel _currentQualityLevel = RTTQualityLevel.High;
    private float _lastFrameRenderTime;
    private int _rendersThisFrame;
    private float _totalMemoryMB;
    private float _lastMemoryCheck;
    private const float MEMORY_CHECK_INTERVAL = 1.0f;
    #endregion

    #region Menu State Fields
    public enum MenuState { MainMenu, RemoteMenu }
    private MenuState _currentMenuState = MenuState.MainMenu;
    private GameObject _currentMenuContent;

    // Persistent Main Menu - created once, never destroyed
    private GameObject _mainMenuContent;
    private bool _mainMenuInitialized = false;
    private bool _hasAutoRecentered = false;
    #endregion

    #region App Lifecycle Fields
    private Dictionary<string, RTTAppInstance> _activeApps = new Dictionary<string, RTTAppInstance>();
    private Dictionary<string, RTTAppInstance> _preparingApps = new Dictionary<string, RTTAppInstance>();
    private string _currentVisibleAppId = null;
    private string _pendingOpenAppId = null;
    private bool _isTransitioning = false;
    #endregion

    #region Events
    /// <summary>Fired when theme configuration changes</summary>
    public event Action OnThemeChanged;

    /// <summary>Fired when font changes</summary>
    public event Action OnFontChanged;

    /// <summary>Fired when menu state changes</summary>
    public event Action<MenuState> OnMenuStateChanged;
    #endregion

    #region Properties - Configuration
    public RTTConfig DefaultConfig => rttConfig;
    public RTTThemeConfig Theme => themeConfig;
    public RTTAppRegistry AppRegistry => appRegistry;

    /// <summary>
    /// Get the configured font. Priority: Theme font > Primary font fallback.
    /// </summary>
    public TMP_FontAsset Font => themeConfig?.font != null ? themeConfig.font : primaryFont;

    // Theme color shortcuts
    public Color PrimaryColor => themeConfig?.primaryColor ?? new Color(0f, 0.9f, 1f);
    public Color AccentColor => themeConfig?.accentColor ?? new Color(0.76f, 0.36f, 1f);
    #endregion

    #region Properties - Panel Management
    public RTTQualityLevel CurrentQualityLevel => _currentQualityLevel;
    public int RegisteredPanelCount => _registeredPanels.Count;
    public int VisiblePanelCount => _registeredPanels.FindAll(p => p != null && p.IsVisible).Count;
    #endregion

    #region Properties - Menu State
    public MenuState CurrentMenuState => _currentMenuState;
    public bool IsMainMenuActive => _currentMenuState == MenuState.MainMenu;
    public bool IsRemoteMenuActive => _currentMenuState == MenuState.RemoteMenu;
    public RTTMenu Menu => menu;
    public RTTMenuFrame MenuFrame => mainMenuFrame;
    public RTTMainMenuController MainMenuController => mainMenuController;
    public RTTRemoteMenuController RemoteMenuController => remoteMenuController;
    #endregion

    #region Properties - App Lifecycle
    public RTTMenuFrame MainMenuFrame => mainMenuFrame;
    public bool IsMainMenuVisible => _currentVisibleAppId == null;
    public string CurrentVisibleAppId => _currentVisibleAppId;
    public int MaxOpenApps => appRegistry?.maxOpenApps ?? 3;

    public RTTAppInstance CurrentApp =>
        _currentVisibleAppId != null && _activeApps.ContainsKey(_currentVisibleAppId)
            ? _activeApps[_currentVisibleAppId]
            : null;
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

        // Move to root if not already (DontDestroyOnLoad only works for root GameObjects)
        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);

        _currentCameraDepth = startingCameraDepth;

        // Load configs from Resources if not assigned
        LoadConfigsFromResources();

        // Subscribe to theme property changes for runtime updates
        RTTThemeConfig.OnAnyThemePropertyChanged += HandleThemePropertyChanged;
    }

    private void Start()
    {
        // Auto-find references
        AutoFindReferences();

        // Subscribe to controller events
        SubscribeToControllerEvents();

        // Auto show main menu
        if (autoShowMainMenu && mainMenuFrame != null)
        {
            StartCoroutine(WaitAndShowMainMenu());
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

    private void OnDestroy()
    {
        // Unsubscribe from theme property changes
        RTTThemeConfig.OnAnyThemePropertyChanged -= HandleThemePropertyChanged;

        UnsubscribeFromControllerEvents();

        // Cleanup active apps
        foreach (var appId in new List<string>(_activeApps.Keys))
        {
            CloseAppInternal(appId, skipSwitchToHome: true);
        }

        if (_instance == this) _instance = null;
    }

    private void OnApplicationQuit()
    {
        _applicationQuitting = true;
    }
    #endregion

    #region Initialization Helpers
    private void LoadConfigsFromResources()
    {
        if (rttConfig == null)
            rttConfig = Resources.Load<RTTConfig>("RTTConfig");
        if (themeConfig == null)
            themeConfig = Resources.Load<RTTThemeConfig>("RTTTheme");
        if (appRegistry == null)
            appRegistry = Resources.Load<RTTAppRegistry>("RTTAppRegistry");
    }

    private void AutoFindReferences()
    {
        // Find RTTMenu container first
        if (menu == null)
            menu = RTTMenu.Instance ?? FindObjectOfType<RTTMenu>();

        if (mainMenuFrame == null)
        {
            // Try to get from RTTMenu first
            if (menu != null)
                mainMenuFrame = menu.MainFrame;
            // Fallback to static instance or FindObjectOfType
            if (mainMenuFrame == null)
                mainMenuFrame = RTTMenuFrame.PrimaryInstance ?? FindObjectOfType<RTTMenuFrame>();
        }

        if (taskbar == null)
            taskbar = RTTTaskbar.Instance ?? FindObjectOfType<RTTTaskbar>();

        if (mainMenuController == null)
        {
            mainMenuController = GetComponentInChildren<RTTMainMenuController>();
            if (mainMenuController == null)
                mainMenuController = gameObject.AddComponent<RTTMainMenuController>();
        }

        // Note: Don't auto-create RTTRemoteMenuController here
        // It will be created per-app in CreateRemoteMenuContent() when needed
        // This prevents having an uninitialized controller running Update()
        if (remoteMenuController == null)
        {
            remoteMenuController = GetComponentInChildren<RTTRemoteMenuController>();
            // Don't AddComponent here - let CreateRemoteMenuContent handle it
        }

        // Set frameParent to RTTMenu if available, otherwise fallback to mainMenuFrame's parent
        if (frameParent == null)
        {
            if (menu != null)
                frameParent = menu.transform;
            else if (mainMenuFrame != null)
                frameParent = mainMenuFrame.transform.parent;
        }
    }

    private void SubscribeToControllerEvents()
    {
        if (mainMenuController != null)
            mainMenuController.OnMenuItemClicked += HandleMainMenuItemClicked;

        // Note: Per-app controllers are created dynamically in CreateRemoteMenuContent
        // and handle their own events via RTTRemoteMenuController
    }

    private void UnsubscribeFromControllerEvents()
    {
        if (mainMenuController != null)
            mainMenuController.OnMenuItemClicked -= HandleMainMenuItemClicked;
    }

    private IEnumerator WaitAndShowMainMenu()
    {
        while (mainMenuFrame.ContentContainer == null)
            yield return null;
        yield return null;

        // Create the Main Menu once - it will never be destroyed
        CreatePersistentMainMenu();
        Debug.Log("[RTTManager] Main Menu initialized (persistent, cannot be closed)");

        // Auto-recenter after camera stabilizes
        if (autoRecenterOnStart && !_hasAutoRecentered)
        {
            StartCoroutine(AutoRecenterRoutine());
        }
    }

    /// <summary>
    /// Auto-recenter routine that waits for camera to stabilize then recenters all objects.
    /// No visual countdown - performs instant recenter.
    /// </summary>
    private IEnumerator AutoRecenterRoutine()
    {
        // Wait for camera tracking to stabilize
        yield return new WaitForSeconds(autoRecenterDelay);

        // Perform instant recenter
        PerformInstantRecenter();
        _hasAutoRecentered = true;

        Debug.Log("[RTTManager] Auto-recenter completed");
    }

    /// <summary>
    /// Perform instant recenter without animation.
    /// Moves all VirtualObjects to face the camera.
    /// Also calls Cardboard API Recenter on Android to reset headset tracking.
    /// </summary>
    public void PerformInstantRecenter()
    {
        // Call Cardboard API Recenter on Android to reset headset tracking
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            Api.Recenter();
            Debug.Log("[RTTManager] Cardboard API Recenter called");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RTTManager] Cardboard Recenter failed: {e.Message}");
        }
#endif

        Camera cam = Camera.main;
        if (cam == null) return;

        // Find VirtualObjects parent
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
        if (virtualObjectsParent == null)
        {
            Debug.LogWarning("[RTTManager] VirtualObjects parent not found for recenter");
            return;
        }

        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null)
        {
            Debug.LogWarning("[RTTManager] No primary RTTMenuFrame found for recenter");
            return;
        }

        // Store pivot point (primary's position and rotation)
        Vector3 pivotPos = primary.transform.position;
        Quaternion pivotRot = primary.transform.rotation;

        // Collect all children and their relative transforms
        var children = new List<Transform>();
        var relativePositions = new List<Vector3>();
        var relativeRotations = new List<Quaternion>();

        foreach (Transform child in virtualObjectsParent.transform)
        {
            children.Add(child);
            // Calculate position relative to pivot
            Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
            relativePositions.Add(relPos);
            // Calculate rotation relative to pivot
            Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
            relativeRotations.Add(relRot);
        }

        // Calculate new pivot position and rotation (facing camera)
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        // Maintain horizontal distance from camera
        float hDist = Vector2.Distance(
            new Vector2(pivotPos.x, pivotPos.z),
            new Vector2(camPos.x, camPos.z)
        );

        Vector3 newPivotPos = camPos + camForward * hDist;
        newPivotPos.y = pivotPos.y; // Preserve Y position
        Quaternion newPivotRot = Quaternion.LookRotation(camForward);

        // Apply new transforms to all children
        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            // Restore relative position and rotation with new pivot
            child.position = newPivotPos + newPivotRot * relativePositions[i];
            child.rotation = newPivotRot * relativeRotations[i];
        }

        Debug.Log($"[RTTManager] Instant recenter: moved {children.Count} objects to face camera");
    }

    /// <summary>
    /// Create the persistent Main Menu. Called once during initialization.
    /// The Main Menu will never be destroyed during the application lifecycle.
    /// </summary>
    private void CreatePersistentMainMenu()
    {
        if (_mainMenuInitialized)
        {
            Debug.LogWarning("[RTTManager] Main Menu already initialized - cannot create another");
            return;
        }

        if (mainMenuFrame == null || mainMenuFrame.ContentContainer == null)
        {
            Debug.LogWarning("[RTTManager] MenuFrame or ContentContainer not initialized");
            return;
        }

        if (mainMenuController != null)
        {
            var containerSize = GetContainerSize();
            _mainMenuContent = mainMenuController.CreateMenu(
                mainMenuFrame.ContentContainer,
                containerSize.x,
                containerSize.y
            );
        }

        _mainMenuInitialized = true;
        _currentMenuState = MenuState.MainMenu;
        OnMenuStateChanged?.Invoke(_currentMenuState);
        mainMenuFrame.MarkDirty();
    }
    #endregion

    #region Panel Registration
    public void RegisterPanel(RTTCanvasBase panel)
    {
        if (panel == null) return;

        if (!_registeredPanels.Contains(panel))
        {
            _registeredPanels.Add(panel);
            panel.OnRTTDestroyed += () => UnregisterPanel(panel);

            if (enablePerformanceLogging)
                Debug.Log($"[RTTManager] Registered: {panel.GetType().Name} (Total: {_registeredPanels.Count})");
        }
    }

    public void UnregisterPanel(RTTCanvasBase panel)
    {
        if (panel == null) return;

        if (_registeredPanels.Remove(panel))
        {
            if (enablePerformanceLogging)
                Debug.Log($"[RTTManager] Unregistered: {panel.GetType().Name} (Total: {_registeredPanels.Count})");
        }
    }

    public int AssignCameraDepth() => _currentCameraDepth--;

    public IReadOnlyList<RTTCanvasBase> GetRegisteredPanels() => _registeredPanels.AsReadOnly();

    public T FindPanel<T>() where T : RTTCanvasBase
    {
        foreach (var panel in _registeredPanels)
        {
            if (panel is T typedPanel) return typedPanel;
        }
        return null;
    }
    #endregion

    #region Quality Management
    public void SetQualityLevel(RTTQualityLevel level)
    {
        if (_currentQualityLevel == level) return;

        _currentQualityLevel = level;
        var preset = GetQualityPreset(level);

        if (enablePerformanceLogging)
            Debug.Log($"[RTTManager] Quality changed to {level}: {preset.width}x{preset.height} AA={preset.antiAliasing}");

        foreach (var panel in _registeredPanels)
        {
            if (panel == null) continue;
            var resolution = panel.CurrentResolution;
            int newWidth = Mathf.RoundToInt(resolution.x * preset.renderScale);
            int newHeight = Mathf.RoundToInt(resolution.y * preset.renderScale);
            panel.ResizeRenderTexture(newWidth, newHeight);
        }
    }

    public RTTQualityPreset GetQualityPreset(RTTQualityLevel level)
    {
        if (rttConfig != null) return rttConfig.GetPreset(level);

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

    public void TryScaleUp()
    {
        if (_currentQualityLevel == RTTQualityLevel.High) return;
        float threshold = rttConfig != null ? rttConfig.maxTextureMemoryMB * 0.6f : 80f;
        if (_totalMemoryMB < threshold)
        {
            var newLevel = _currentQualityLevel == RTTQualityLevel.Low ? RTTQualityLevel.Medium : RTTQualityLevel.High;
            SetQualityLevel(newLevel);
        }
    }

    public void TryScaleDown()
    {
        if (_currentQualityLevel == RTTQualityLevel.Low) return;
        var newLevel = _currentQualityLevel == RTTQualityLevel.High ? RTTQualityLevel.Medium : RTTQualityLevel.Low;
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
        if (rttConfig == null || !rttConfig.enableAutoScaling) return;

        if (_totalMemoryMB > rttConfig.maxTextureMemoryMB)
            TryScaleDown();
        else if (_totalMemoryMB < rttConfig.maxTextureMemoryMB * 0.5f)
            TryScaleUp();
    }

    private long CalculateTotalTextureMemory()
    {
        long total = 0;
        foreach (var panel in _registeredPanels)
        {
            if (panel == null) continue;
            var rt = panel.GetRenderTexture();
            if (rt != null)
                total += (long)rt.width * rt.height * 4 * rt.antiAliasing;
        }
        return total;
    }
    #endregion

    #region Performance Statistics
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

    public void LogPerformanceStats()
    {
        var stats = GetPerformanceStats();
        Debug.Log($"[RTTManager] {stats}");
    }
    #endregion

    #region Panel Utility
    public void MarkAllDirty()
    {
        foreach (var panel in _registeredPanels)
            panel?.MarkDirty();
    }

    public void HideAll()
    {
        foreach (var panel in _registeredPanels)
            panel?.Hide();
    }

    public void ShowAll()
    {
        foreach (var panel in _registeredPanels)
            panel?.Show();
    }

    public void CleanupDestroyedPanels()
    {
        _registeredPanels.RemoveAll(p => p == null);
    }
    #endregion

    #region Theme Management
    public void SetTheme(RTTThemeConfig newTheme)
    {
        if (newTheme == null) return;
        themeConfig = newTheme;
        OnThemeChanged?.Invoke();
        Debug.Log("[RTTManager] Theme changed, notifying components");
    }

    public void RefreshAllThemes()
    {
        OnThemeChanged?.Invoke();
    }

    /// <summary>
    /// Handle runtime theme property changes from RTTThemeConfig.
    /// Called when individual properties change via SetPrimaryColor(), SetAccentColor(), etc.
    /// </summary>
    private void HandleThemePropertyChanged()
    {
        // Propagate to all subscribed components
        OnThemeChanged?.Invoke();

        if (enablePerformanceLogging)
            Debug.Log("[RTTManager] Theme property changed at runtime, notifying components");
    }

    /// <summary>
    /// Get alternating color for menu items (primary/accent pattern)
    /// </summary>
    public Color GetAlternatingColor(int index)
    {
        return themeConfig?.GetAlternatingColor(index) ?? (index % 2 == 0 ? PrimaryColor : AccentColor);
    }
    #endregion

    #region Font Management
    public void SetFont(TMP_FontAsset font)
    {
        if (font == null) return;
        primaryFont = font;
        OnFontChanged?.Invoke();
        Debug.Log("[RTTManager] Font changed, notifying components");
    }

    public void RefreshAllFonts()
    {
        OnFontChanged?.Invoke();
    }
    #endregion

    #region Menu Navigation
    /// <summary>
    /// Show the persistent Main Menu. Does not recreate - only shows existing menu.
    /// The Main Menu is created once and never destroyed.
    /// </summary>
    public void ShowMainMenu()
    {
        if (mainMenuFrame == null || mainMenuFrame.ContentContainer == null)
        {
            Debug.LogWarning("[RTTManager] MenuFrame or ContentContainer not initialized");
            return;
        }

        // Initialize Main Menu if not done yet
        if (!_mainMenuInitialized)
        {
            CreatePersistentMainMenu();
        }

        // Destroy any non-MainMenu content (e.g., Remote Menu content in the same frame)
        DestroyCurrentMenuContent();

        // Show the persistent Main Menu content
        if (_mainMenuContent != null)
        {
            _mainMenuContent.SetActive(true);
        }

        // Ensure mainMenuFrame is active and visible
        if (mainMenuFrame != null)
        {
            mainMenuFrame.gameObject.SetActive(true);
            mainMenuFrame.SetAsPrimaryFrame();
        }

        _currentMenuState = MenuState.MainMenu;
        OnMenuStateChanged?.Invoke(_currentMenuState);
        mainMenuFrame.MarkDirty();
        Debug.Log("[RTTManager] Showing Main Menu (persistent)");
    }

    public void SwitchToRemoteMenu()
    {
        if (_currentMenuState == MenuState.RemoteMenu) return;
        if (mainMenuFrame == null || mainMenuFrame.ContentContainer == null) return;

        // Hide the persistent Main Menu content (don't destroy)
        HideMainMenuContent();

        // Destroy any other non-MainMenu content
        DestroyCurrentMenuContent();

        if (remoteMenuController != null)
        {
            var containerSize = GetContainerSize();
            _currentMenuContent = remoteMenuController.CreateMenu(
                mainMenuFrame.ContentContainer,
                containerSize.x,
                containerSize.y,
                Font,  // Uses theme font with fallback
                PrimaryColor,
                AccentColor
            );
        }

        _currentMenuState = MenuState.RemoteMenu;
        OnMenuStateChanged?.Invoke(_currentMenuState);
        mainMenuFrame.MarkDirty();
        Debug.Log("[RTTManager] Switched to Remote Menu");
    }

    /// <summary>
    /// Hide the persistent Main Menu content without destroying it.
    /// </summary>
    private void HideMainMenuContent()
    {
        if (_mainMenuContent != null)
        {
            _mainMenuContent.SetActive(false);
        }
    }

    public void ReturnToMainMenu()
    {
        if (_currentMenuState == MenuState.MainMenu) return;
        ShowMainMenu();
        Debug.Log("[RTTManager] Returned to Main Menu");
    }

    /// <summary>
    /// Destroy non-MainMenu content. Main Menu is never destroyed.
    /// </summary>
    private void DestroyCurrentMenuContent()
    {
        // Never destroy the persistent Main Menu content
        if (_currentMenuContent != null && _currentMenuContent != _mainMenuContent)
        {
            // Only cleanup non-MainMenu content
            if (_currentMenuState == MenuState.RemoteMenu)
                remoteMenuController?.Cleanup();

            Destroy(_currentMenuContent);
            _currentMenuContent = null;
        }
    }

    private Vector2 GetContainerSize()
    {
        if (mainMenuFrame == null || mainMenuFrame.ContentContainer == null)
            return new Vector2(1770f, 800f);

        var rect = mainMenuFrame.ContentContainer.rect;
        if (rect.width > 0 && rect.height > 0)
            return new Vector2(rect.width, rect.height);

        return new Vector2(
            mainMenuFrame.LogicalWidthValue - 150f,
            mainMenuFrame.LogicalWidthValue / mainMenuFrame.PanelWidth * mainMenuFrame.PanelHeight - 100f
        );
    }
    #endregion

    #region Menu Event Handlers
    private void HandleMainMenuItemClicked(string itemId)
    {
        // Check app registry for app type
        if (appRegistry != null)
        {
            var appType = appRegistry.GetAppType(itemId);
            if (appType == RTTAppRegistry.AppType.Quit)
            {
                HandleQuit();
                return;
            }
        }
        else if (itemId == "quit")
        {
            HandleQuit();
            return;
        }

        // Open app
        OpenApp(itemId);
    }

    private void HandleQuit()
    {
        Debug.Log("[RTTManager] Quit clicked");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    #endregion

    #region App Lifecycle - Public API
    public RTTAppInstance OpenApp(string appId)
    {
        if (_activeApps.ContainsKey(appId))
        {
            SwitchToApp(appId);
            return _activeApps[appId];
        }

        if (_preparingApps.ContainsKey(appId))
        {
            StartCoroutine(WaitAndSwitchToPreparedApp(appId));
            return _preparingApps[appId];
        }

        int slotIndex = GetNextAvailableSlot();
        if (slotIndex == -1)
        {
            Debug.LogWarning("[RTTManager] No available app slots");
            return null;
        }

        Sprite icon = GetAppIcon(appId);
        var instance = new RTTAppInstance(appId)
        {
            TaskbarSlotIndex = slotIndex,
            Icon = icon
        };

        _activeApps[appId] = instance;
        StartCoroutine(CreateAppFrameAndContent(instance));

        Debug.Log($"[RTTManager] Opening app: {appId} in slot {slotIndex}");
        return instance;
    }

    public void PrepareApp(string appId)
    {
        _pendingOpenAppId = appId;

        if (_activeApps.ContainsKey(appId) || _preparingApps.ContainsKey(appId))
            return;

        CancelAllPreparations();

        int slotIndex = GetNextAvailableSlot();
        if (slotIndex == -1)
        {
            Debug.LogWarning("[RTTManager] No available app slots for prepare");
            return;
        }

        Sprite icon = GetAppIcon(appId);
        var instance = new RTTAppInstance(appId)
        {
            TaskbarSlotIndex = slotIndex,
            Icon = icon
        };

        _preparingApps[appId] = instance;
        StartCoroutine(PrepareAppFrameAsync(instance));

        Debug.Log($"[RTTManager] Preparing app: {appId} in slot {slotIndex}");
    }

    public bool IsAppPrepared(string appId)
    {
        if (_activeApps.ContainsKey(appId)) return true;
        if (!_preparingApps.ContainsKey(appId)) return false;
        var instance = _preparingApps[appId];
        // Use IsPrepared flag to ensure preparation is fully complete (including SetActive(false))
        return instance.IsPrepared;
    }

    public void OpenPreparedApp(string appId)
    {
        if (_pendingOpenAppId != appId)
        {
            Debug.Log($"[RTTManager] Skipping open for {appId} - user clicked {_pendingOpenAppId} instead");
            return;
        }

        _pendingOpenAppId = null;

        if (_activeApps.ContainsKey(appId))
        {
            SwitchToApp(appId);
            return;
        }

        if (_preparingApps.ContainsKey(appId))
        {
            StartCoroutine(WaitAndSwitchToPreparedApp(appId));
            return;
        }

        OpenApp(appId);
    }

    public void SwitchToApp(string appId)
    {
        if (!_activeApps.ContainsKey(appId))
        {
            Debug.LogWarning($"[RTTManager] App not open: {appId}");
            return;
        }

        if (_isTransitioning)
        {
            SwitchToAppImmediate(appId);
            return;
        }

        StartCoroutine(SwitchToAppWithTransition(appId));
    }

    public void SwitchToHome()
    {
        if (_currentVisibleAppId == null)
        {
            taskbar?.SelectSlot(0);
            return;
        }

        if (_isTransitioning)
        {
            SwitchToHomeImmediate();
            return;
        }

        StartCoroutine(SwitchToHomeWithTransition());
    }

    public void CloseApp(string appId)
    {
        if (!_activeApps.ContainsKey(appId)) return;

        if (_currentVisibleAppId == appId && !_isTransitioning)
            StartCoroutine(CloseAppWithTransition(appId));
        else
            CloseAppInternal(appId, skipSwitchToHome: false);
    }

    public bool IsAppOpen(string appId) => _activeApps.ContainsKey(appId);

    public RTTAppInstance GetApp(string appId) =>
        _activeApps.ContainsKey(appId) ? _activeApps[appId] : null;

    public IReadOnlyCollection<string> GetOpenAppIds() => _activeApps.Keys;
    #endregion

    #region App Lifecycle - Internal
    private void CancelAllPreparations()
    {
        foreach (var kvp in new Dictionary<string, RTTAppInstance>(_preparingApps))
        {
            // Destroy controller first (it's not a child of frame anymore)
            if (kvp.Value.Controller != null && kvp.Value.Controller.gameObject != null)
                Destroy(kvp.Value.Controller.gameObject);
            if (kvp.Value.Frame != null)
            {
                if (menu != null)
                    menu.DestroyFrame(kvp.Value.Frame);
                else
                    Destroy(kvp.Value.Frame.gameObject);
            }
            Debug.Log($"[RTTManager] Cancelled preparation for: {kvp.Key}");
        }
        _preparingApps.Clear();
    }

    private void HideCurrentView()
    {
        if (_currentVisibleAppId == null)
        {
            if (mainMenuFrame != null)
                mainMenuFrame.gameObject.SetActive(false);
        }
        else if (_activeApps.ContainsKey(_currentVisibleAppId))
        {
            var currentApp = _activeApps[_currentVisibleAppId];
            if (currentApp.Frame != null)
                currentApp.Frame.gameObject.SetActive(false);
            currentApp.IsVisible = false;
        }
    }

    private void CloseAppInternal(string appId, bool skipSwitchToHome)
    {
        if (!_activeApps.ContainsKey(appId)) return;

        var app = _activeApps[appId];

        if (!skipSwitchToHome && _currentVisibleAppId == appId)
        {
            if (app.Frame != null)
                app.Frame.gameObject.SetActive(false);
            app.IsVisible = false;

            if (mainMenuFrame != null)
            {
                mainMenuFrame.gameObject.SetActive(true);
                mainMenuFrame.SetAsPrimaryFrame();
            }

            // Show the persistent Main Menu content
            if (_mainMenuContent != null)
            {
                _mainMenuContent.SetActive(true);
            }

            _currentVisibleAppId = null;
            _currentMenuState = MenuState.MainMenu;
            taskbar?.SelectSlot(0);
        }

        // Cleanup controller
        if (app.Controller != null)
        {
            var cleanupMethod = app.Controller.GetType().GetMethod("Cleanup");
            if (cleanupMethod != null)
            {
                try { cleanupMethod.Invoke(app.Controller, null); }
                catch (Exception e) { Debug.LogWarning($"[RTTManager] Cleanup failed: {e.Message}"); }
            }
            // Destroy controller GameObject (it's not a child of frame anymore)
            if (app.Controller.gameObject != null)
                Destroy(app.Controller.gameObject);
        }

        if (app.Frame != null)
        {
            // Use RTTMenu.DestroyFrame if available to properly track frame removal
            if (menu != null)
                menu.DestroyFrame(app.Frame);
            else
                Destroy(app.Frame.gameObject);
        }

        if (taskbar != null && app.TaskbarSlotIndex > 0)
            taskbar.UnregisterApp(app.TaskbarSlotIndex);

        _activeApps.Remove(appId);
        Debug.Log($"[RTTManager] Closed app: {appId}");
    }

    private int GetNextAvailableSlot()
    {
        int maxSlots = appRegistry?.maxOpenApps ?? 3;
        for (int i = 1; i <= maxSlots; i++)
        {
            bool slotUsed = false;
            foreach (var app in _activeApps.Values)
            {
                if (app.TaskbarSlotIndex == i) { slotUsed = true; break; }
            }
            if (!slotUsed)
            {
                foreach (var app in _preparingApps.Values)
                {
                    if (app.TaskbarSlotIndex == i) { slotUsed = true; break; }
                }
            }
            if (!slotUsed) return i;
        }
        return -1;
    }

    private Sprite GetAppIcon(string appId)
    {
        // Try app registry first
        if (appRegistry != null)
        {
            var icon = appRegistry.GetIcon(appId);
            if (icon != null) return icon;
        }

        // Fallback to Resources
        return Resources.Load<Sprite>($"icon_{appId}");
    }
    #endregion

    #region App Lifecycle - Coroutines
    private IEnumerator WaitAndSwitchToPreparedApp(string appId)
    {
        while (_preparingApps.ContainsKey(appId) && !IsAppPrepared(appId))
            yield return null;

        if (_preparingApps.ContainsKey(appId))
        {
            var instance = _preparingApps[appId];
            _preparingApps.Remove(appId);
            _activeApps[appId] = instance;
            StartCoroutine(SwitchToPreparedAppWithTransition(instance));
        }
    }

    private IEnumerator CreateAppFrameAndContent(RTTAppInstance instance)
    {
        if (mainMenuFrame == null)
        {
            Debug.LogError("[RTTManager] MainMenuFrame is null");
            yield break;
        }

        _isTransitioning = true;

        // Animate out
        if (useFadeTransition && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(mainMenuFrame, 1f, 0f, transitionOutDuration, true));
        else if (useScaleTransition && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(mainMenuFrame.transform, 1f, 0.9f, transitionOutDuration, true));

        // Create frame with unique name based on app ID
        if (menu != null)
        {
            instance.Frame = menu.CreateAppFrame(
                instance.AppId,
                mainMenuFrame.PanelWidth,
                mainMenuFrame.PanelHeight,
                mainMenuFrame.LogicalWidthValue
            );
        }
        else
        {
            // Fallback for when RTTMenu is not available
            instance.Frame = RTTMenuFrame.Create(
                frameParent,
                mainMenuFrame.PanelWidth,
                mainMenuFrame.PanelHeight,
                mainMenuFrame.LogicalWidthValue,
                name: $"RTTMenuFrame_{instance.AppId}"
            );
        }

        instance.Frame.transform.position = mainMenuFrame.transform.position;
        instance.Frame.transform.rotation = mainMenuFrame.transform.rotation;
        instance.Frame.transform.localScale = Vector3.one;

        // Wait for ContentContainer
        int waitFrames = 0;
        while (instance.Frame.ContentContainer == null && waitFrames < 60)
        {
            waitFrames++;
            yield return null;
        }

        if (instance.Frame.ContentContainer == null)
        {
            Debug.LogError($"[RTTManager] ContentContainer not ready for {instance.AppId}");
            ResetFrameAlpha(mainMenuFrame);
            _isTransitioning = false;
            yield break;
        }

        yield return null;

        // Create content
        CreateAppContent(instance);

        // Hide MainMenu, show new frame
        mainMenuFrame.gameObject.SetActive(false);
        ResetFrameAlpha(mainMenuFrame);

        instance.Frame.SetVisible(true); // Ensure DisplayQuad is visible
        ResetFrameAlpha(instance.Frame); // Reset material alpha in case of previous fade
        instance.Frame.SetAsPrimaryFrame();
        instance.IsVisible = true;
        _currentVisibleAppId = instance.AppId;

        // Animate in
        if (useFadeTransition && transitionInDuration > 0)
        {
            var newQuad = instance.Frame.GetDisplayQuad();
            if (newQuad?.material != null)
                newQuad.material.color = new Color(1f, 1f, 1f, 0f);
            yield return StartCoroutine(AnimateFrameFade(instance.Frame, 0f, 1f, transitionInDuration, false));
        }
        else if (useScaleTransition && transitionInDuration > 0)
        {
            instance.Frame.transform.localScale = Vector3.one * 0.9f;
            yield return StartCoroutine(AnimateFrameScale(instance.Frame.transform, 0.9f, 1f, transitionInDuration, false));
        }

        // Register with taskbar
        if (taskbar != null)
        {
            taskbar.RegisterApp(instance.TaskbarSlotIndex, instance.Icon, () => SwitchToApp(instance.AppId));
            taskbar.SelectSlot(instance.TaskbarSlotIndex);
        }

        _isTransitioning = false;
        Debug.Log($"[RTTManager] App {instance.AppId} opened");
    }

    private IEnumerator PrepareAppFrameAsync(RTTAppInstance instance)
    {
        if (mainMenuFrame == null)
        {
            _preparingApps.Remove(instance.AppId);
            yield break;
        }

        // Create frame with unique name based on app ID
        if (menu != null)
        {
            instance.Frame = menu.CreateAppFrame(
                instance.AppId,
                mainMenuFrame.PanelWidth,
                mainMenuFrame.PanelHeight,
                mainMenuFrame.LogicalWidthValue
            );
        }
        else
        {
            // Fallback for when RTTMenu is not available
            instance.Frame = RTTMenuFrame.Create(
                frameParent,
                mainMenuFrame.PanelWidth,
                mainMenuFrame.PanelHeight,
                mainMenuFrame.LogicalWidthValue,
                name: $"RTTMenuFrame_{instance.AppId}"
            );
        }

        instance.Frame.transform.position = mainMenuFrame.transform.position + Vector3.up * 1000f;
        instance.Frame.transform.rotation = mainMenuFrame.transform.rotation;
        instance.Frame.transform.localScale = Vector3.one;

        int waitFrames = 0;
        while (instance.Frame.ContentContainer == null && waitFrames < 60)
        {
            waitFrames++;
            yield return null;
        }

        if (instance.Frame.ContentContainer == null)
        {
            if (instance.Frame != null)
            {
                if (menu != null)
                    menu.DestroyFrame(instance.Frame);
                else
                    Destroy(instance.Frame.gameObject);
            }
            _preparingApps.Remove(instance.AppId);
            yield break;
        }

        yield return null;

        CreateAppContent(instance);

        // Wait for child components (including side panel RTTMenuFrames) to initialize
        // Side panels are created in CreateAppContent → RTTRemoteMenu.BuildUI → CreateSidePanels
        // They need their Start() to be called before we can disable the frame
        // Start() is called on the next frame after Awake(), so we need to wait
        for (int i = 0; i < 5; i++)
        {
            yield return null;
        }

        // Ensure frame is fully initialized before disabling
        if (!instance.Frame.IsInitialized)
        {
            Debug.LogWarning($"[RTTManager] Frame not initialized after 5 frames, calling EnsureInitialized()");
            instance.Frame.EnsureInitialized();
        }

        instance.Frame.transform.position = mainMenuFrame.transform.position;

        // Reset alpha to 1 before disabling (in case any fade was applied)
        ResetFrameAlpha(instance.Frame);

        instance.Frame.gameObject.SetActive(false);
        instance.Frame.MarkDirty();

        // Mark as fully prepared AFTER SetActive(false) to prevent race condition
        instance.IsPrepared = true;

        Debug.Log($"[RTTManager] App {instance.AppId} prepared");
    }

    private void SwitchToAppImmediate(string appId)
    {
        HideCurrentView();
        var targetApp = _activeApps[appId];
        if (targetApp.Frame != null)
        {
            targetApp.Frame.gameObject.SetActive(true);
            targetApp.Frame.SetVisible(true); // Ensure DisplayQuad is visible
            ResetFrameAlpha(targetApp.Frame); // Reset material alpha in case of previous fade
            targetApp.Frame.SetAsPrimaryFrame();
            targetApp.IsVisible = true;
        }
        _currentVisibleAppId = appId;
        taskbar?.SelectSlot(targetApp.TaskbarSlotIndex);
    }

    private IEnumerator SwitchToAppWithTransition(string appId)
    {
        _isTransitioning = true;
        var targetApp = _activeApps[appId];

        RTTMenuFrame currentFrame = _currentVisibleAppId == null
            ? mainMenuFrame
            : (_activeApps.ContainsKey(_currentVisibleAppId) ? _activeApps[_currentVisibleAppId].Frame : null);

        if (useFadeTransition && currentFrame != null && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(currentFrame, 1f, 0f, transitionOutDuration * 0.5f, true));
        else if (useScaleTransition && currentFrame != null && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(currentFrame.transform, 1f, 0.95f, transitionOutDuration * 0.5f, true));

        HideCurrentView();
        ResetFrameAlpha(currentFrame);
        if (currentFrame != null) currentFrame.transform.localScale = Vector3.one;

        if (targetApp.Frame != null)
        {
            targetApp.Frame.gameObject.SetActive(true);
            targetApp.Frame.SetVisible(true); // Ensure DisplayQuad is visible
            ResetFrameAlpha(targetApp.Frame); // Reset material alpha in case of previous fade
            targetApp.Frame.SetAsPrimaryFrame();
            targetApp.IsVisible = true;

            if (useFadeTransition)
            {
                var quad = targetApp.Frame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, 0f);
            }
            else if (useScaleTransition)
            {
                targetApp.Frame.transform.localScale = Vector3.one * 0.95f;
            }
        }

        _currentVisibleAppId = appId;

        if (useFadeTransition && targetApp.Frame != null && transitionInDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(targetApp.Frame, 0f, 1f, transitionInDuration * 0.5f, false));
        else if (useScaleTransition && targetApp.Frame != null && transitionInDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(targetApp.Frame.transform, 0.95f, 1f, transitionInDuration * 0.5f, false));

        taskbar?.SelectSlot(targetApp.TaskbarSlotIndex);

        _isTransitioning = false;
    }

    private void SwitchToHomeImmediate()
    {
        HideCurrentView();
        if (mainMenuFrame != null)
        {
            mainMenuFrame.gameObject.SetActive(true);
            mainMenuFrame.SetVisible(true); // Ensure DisplayQuad is visible
            mainMenuFrame.SetAsPrimaryFrame();
        }
        // Show the persistent Main Menu content
        if (_mainMenuContent != null)
        {
            _mainMenuContent.SetActive(true);
        }
        _currentVisibleAppId = null;
        _currentMenuState = MenuState.MainMenu;
        taskbar?.SelectSlot(0);
    }

    private IEnumerator SwitchToHomeWithTransition()
    {
        _isTransitioning = true;

        RTTMenuFrame currentFrame = _activeApps.ContainsKey(_currentVisibleAppId)
            ? _activeApps[_currentVisibleAppId].Frame : null;

        if (useFadeTransition && currentFrame != null && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(currentFrame, 1f, 0f, transitionOutDuration * 0.5f, true));
        else if (useScaleTransition && currentFrame != null && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(currentFrame.transform, 1f, 0.95f, transitionOutDuration * 0.5f, true));

        HideCurrentView();
        ResetFrameAlpha(currentFrame);
        if (currentFrame != null) currentFrame.transform.localScale = Vector3.one;

        if (mainMenuFrame != null)
        {
            mainMenuFrame.gameObject.SetActive(true);
            mainMenuFrame.SetVisible(true); // Ensure DisplayQuad is visible
            mainMenuFrame.SetAsPrimaryFrame();

            if (useFadeTransition)
            {
                var quad = mainMenuFrame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, 0f);
            }
            else if (useScaleTransition)
            {
                mainMenuFrame.transform.localScale = Vector3.one * 0.95f;
            }
        }

        // Show the persistent Main Menu content
        if (_mainMenuContent != null)
        {
            _mainMenuContent.SetActive(true);
        }

        _currentVisibleAppId = null;
        _currentMenuState = MenuState.MainMenu;

        if (useFadeTransition && mainMenuFrame != null && transitionInDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(mainMenuFrame, 0f, 1f, transitionInDuration * 0.5f, false));
        else if (useScaleTransition && mainMenuFrame != null && transitionInDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(mainMenuFrame.transform, 0.95f, 1f, transitionInDuration * 0.5f, false));

        taskbar?.SelectSlot(0);
        _isTransitioning = false;
    }

    private IEnumerator SwitchToPreparedAppWithTransition(RTTAppInstance instance)
    {
        _isTransitioning = true;

        if (useFadeTransition && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(mainMenuFrame, 1f, 0f, transitionOutDuration, true));
        else if (useScaleTransition && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(mainMenuFrame.transform, 1f, 0.9f, transitionOutDuration, true));

        mainMenuFrame.gameObject.SetActive(false);
        ResetFrameAlpha(mainMenuFrame);

        instance.Frame.gameObject.SetActive(true);

        // Force rebuild Canvas layout after re-enabling
        var canvas = instance.Frame.GetCanvas();
        if (canvas != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(canvas.GetComponent<RectTransform>());
        }

        instance.Frame.SetVisible(true); // Ensure DisplayQuad is visible
        ResetFrameAlpha(instance.Frame); // Reset material alpha in case of previous fade
        instance.Frame.MarkDirty(); // Force re-render
        instance.Frame.SetAsPrimaryFrame();
        instance.IsVisible = true;
        _currentVisibleAppId = instance.AppId;

        if (useFadeTransition && transitionInDuration > 0)
        {
            var newQuad = instance.Frame.GetDisplayQuad();
            if (newQuad?.material != null)
                newQuad.material.color = new Color(1f, 1f, 1f, 0f);
            yield return StartCoroutine(AnimateFrameFade(instance.Frame, 0f, 1f, transitionInDuration, false));
        }
        else if (useScaleTransition && transitionInDuration > 0)
        {
            instance.Frame.transform.localScale = Vector3.one * 0.9f;
            yield return StartCoroutine(AnimateFrameScale(instance.Frame.transform, 0.9f, 1f, transitionInDuration, false));
        }

        if (taskbar != null)
        {
            taskbar.RegisterApp(instance.TaskbarSlotIndex, instance.Icon, () => SwitchToApp(instance.AppId));
            taskbar.SelectSlot(instance.TaskbarSlotIndex);
        }

        _isTransitioning = false;
    }

    private IEnumerator CloseAppWithTransition(string appId)
    {
        if (!_activeApps.ContainsKey(appId)) yield break;

        _isTransitioning = true;
        var app = _activeApps[appId];

        if (useFadeTransition && app.Frame != null && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(app.Frame, 1f, 0f, transitionOutDuration, true));
        else if (useScaleTransition && app.Frame != null && transitionOutDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(app.Frame.transform, 1f, 0.9f, transitionOutDuration, true));

        if (app.Frame != null)
        {
            app.Frame.gameObject.SetActive(false);
            ResetFrameAlpha(app.Frame);
        }
        app.IsVisible = false;

        if (mainMenuFrame != null)
        {
            mainMenuFrame.gameObject.SetActive(true);
            mainMenuFrame.SetVisible(true); // Ensure DisplayQuad is visible
            mainMenuFrame.SetAsPrimaryFrame();

            if (useFadeTransition)
            {
                var quad = mainMenuFrame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, 0f);
            }
            else if (useScaleTransition)
            {
                mainMenuFrame.transform.localScale = Vector3.one * 0.9f;
            }
        }

        // Show the persistent Main Menu content
        if (_mainMenuContent != null)
        {
            _mainMenuContent.SetActive(true);
        }

        _currentVisibleAppId = null;
        _currentMenuState = MenuState.MainMenu;

        if (useFadeTransition && mainMenuFrame != null && transitionInDuration > 0)
            yield return StartCoroutine(AnimateFrameFade(mainMenuFrame, 0f, 1f, transitionInDuration, false));
        else if (useScaleTransition && mainMenuFrame != null && transitionInDuration > 0)
            yield return StartCoroutine(AnimateFrameScale(mainMenuFrame.transform, 0.9f, 1f, transitionInDuration, false));

        taskbar?.SelectSlot(0);
        _isTransitioning = false;

        CloseAppInternal(appId, skipSwitchToHome: true);
    }
    #endregion

    #region App Content Creation
    private void CreateAppContent(RTTAppInstance instance)
    {
        var appType = appRegistry?.GetAppType(instance.AppId) ?? GetFallbackAppType(instance.AppId);

        switch (appType)
        {
            case RTTAppRegistry.AppType.Remote:
                CreateRemoteMenuContent(instance);
                break;
            case RTTAppRegistry.AppType.Files:
                CreateFilesMenuContent(instance);
                break;
            case RTTAppRegistry.AppType.Browser:
            case RTTAppRegistry.AppType.Media:
            case RTTAppRegistry.AppType.Settings:
                Debug.Log($"[RTTManager] {appType} app not yet implemented");
                break;
            default:
                Debug.LogWarning($"[RTTManager] Unknown app: {instance.AppId}");
                break;
        }
    }

    private RTTAppRegistry.AppType GetFallbackAppType(string appId)
    {
        switch (appId)
        {
            case "remote": return RTTAppRegistry.AppType.Remote;
            case "browser": return RTTAppRegistry.AppType.Browser;
            case "media": return RTTAppRegistry.AppType.Media;
            case "files": return RTTAppRegistry.AppType.Files;
            case "settings": return RTTAppRegistry.AppType.Settings;
            case "quit": return RTTAppRegistry.AppType.Quit;
            default: return RTTAppRegistry.AppType.NotImplemented;
        }
    }

    private void CreateRemoteMenuContent(RTTAppInstance instance)
    {
        Debug.Log($"[RTTManager] Creating RemoteMenu content...");

        // Create controller as sibling to frame, NOT child
        // This ensures controller stays active when frame is hidden during streaming
        GameObject controllerObj = new GameObject($"RemoteMenuController_{instance.AppId}");
        controllerObj.transform.SetParent(this.transform); // Parent to RTTManager, not frame
        var controller = controllerObj.AddComponent<RTTRemoteMenuController>();

        var containerSize = instance.Frame.GetContentSize();

        instance.MenuContent = controller.CreateMenu(
            instance.Frame.ContentContainer,
            containerSize.x,
            containerSize.y,
            Font,  // Uses theme font with fallback
            PrimaryColor,
            AccentColor
        );

        instance.Controller = controller;
        controller.OnBackClicked += () => CloseApp(instance.AppId);

        instance.Frame.MarkDirty();
    }

    private void CreateFilesMenuContent(RTTAppInstance instance)
    {
        Debug.Log($"[RTTManager] Creating FileManager content...");

        // Create controller
        GameObject controllerObj = new GameObject($"FileManagerController_{instance.AppId}");
        controllerObj.transform.SetParent(this.transform);
        var controller = controllerObj.AddComponent<RTTFileManagerController>();

        var containerSize = instance.Frame.GetContentSize();

        // Create View via Controller
        instance.MenuContent = controller.CreateMenu(
            instance.Frame.ContentContainer,
            containerSize.x,
            containerSize.y,
            Font,  // Uses theme font with fallback
            PrimaryColor,
            AccentColor
        );

        instance.Controller = controller;
        controller.OnBackClicked += () => CloseApp(instance.AppId);

        instance.Frame.MarkDirty();
    }
    #endregion

    #region Animation Helpers
    private IEnumerator AnimateFrameScale(Transform target, float fromScale, float toScale, float duration, bool fadeOut)
    {
        float elapsed = 0f;
        Vector3 from = Vector3.one * fromScale;
        Vector3 to = Vector3.one * toScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = fadeOut ? t * t : 1f - (1f - t) * (1f - t);
            target.localScale = Vector3.Lerp(from, to, easedT);
            yield return null;
        }

        target.localScale = to;
    }

    private IEnumerator AnimateFrameFade(RTTMenuFrame frame, float fromAlpha, float toAlpha, float duration, bool isFadeOut)
    {
        if (frame == null) yield break;

        var displayQuad = frame.GetDisplayQuad();
        if (displayQuad == null || displayQuad.material == null) yield break;

        Material mat = displayQuad.material;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = isFadeOut ? t * t : 1f - (1f - t) * (1f - t);
            float alpha = Mathf.Lerp(fromAlpha, toAlpha, easedT);
            mat.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        mat.color = new Color(1f, 1f, 1f, toAlpha);
    }

    private void ResetFrameAlpha(RTTMenuFrame frame)
    {
        if (frame == null) return;
        var displayQuad = frame.GetDisplayQuad();
        if (displayQuad?.material != null)
            displayQuad.material.color = new Color(1f, 1f, 1f, 1f);
    }
    #endregion
}
