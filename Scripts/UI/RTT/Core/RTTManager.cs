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
using VRWorkspace.UI.RTT;
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
    [SerializeField] private float transitionOutDuration = 0.15f;  // Increased for smoother transition
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
    // Legacy fields removed - now managed by extracted managers
    private Queue<RTTCanvasBase> _renderQueue = new Queue<RTTCanvasBase>();
    private float _lastFrameRenderTime;
    private int _rendersThisFrame;

    // Extracted Managers
    private RTTPanelManager _panelManager;
    private RTTQualityManager _qualityManager;
    private RTTAppManager _appManager;
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
    public RTTQualityLevel CurrentQualityLevel => _qualityManager?.CurrentQualityLevel ?? RTTQualityLevel.High;
    public int RegisteredPanelCount => _panelManager?.RegisteredPanelCount ?? 0;
    public int VisiblePanelCount => _panelManager?.VisiblePanelCount ?? 0;
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
    public bool IsMainMenuVisible => _appManager?.IsMainMenuVisible ?? true;
    public string CurrentVisibleAppId => _appManager?.CurrentVisibleAppId;
    public int MaxOpenApps => appRegistry?.maxOpenApps ?? 3;
    public bool IsTransitioning => _appManager?.IsTransitioning ?? false;
    public int OpenAppCount => _appManager?.OpenAppCount ?? 0;
    public RTTAppInstance CurrentApp => _appManager?.CurrentApp;
    #endregion

    #region Properties - Zoom
    /// <summary>Get current zoom distance from camera</summary>
    public float ZoomDistance => VirtualObjectsZoomController.Instance?.CurrentDistance ?? 1.8f;

    /// <summary>Get minimum zoom distance</summary>
    public float ZoomMinDistance => VirtualObjectsZoomController.Instance?.MinDistance ?? 1.0f;

    /// <summary>Get maximum zoom distance</summary>
    public float ZoomMaxDistance => VirtualObjectsZoomController.Instance?.MaxDistance ?? 2.0f;

    /// <summary>Whether zoom controller is available</summary>
    public bool IsZoomAvailable => VirtualObjectsZoomController.Instance != null;
    #endregion

    #region Zoom API
    /// <summary>
    /// Set zoom distance (move VirtualObjects closer/farther from camera).
    /// </summary>
    /// <param name="distance">Distance in meters (clamped to min/max)</param>
    public void SetZoom(float distance)
    {
        VirtualObjectsZoomController.Instance?.SetZoomDistance(distance);
    }

    /// <summary>
    /// Set zoom using normalized value (0 = closest, 1 = farthest).
    /// </summary>
    public void SetNormalizedZoom(float normalized01)
    {
        VirtualObjectsZoomController.Instance?.SetNormalizedZoom(normalized01);
    }

    /// <summary>
    /// Get normalized zoom value (0 = closest, 1 = farthest).
    /// </summary>
    public float GetNormalizedZoom()
    {
        return VirtualObjectsZoomController.Instance?.GetNormalizedZoom() ?? 0.5f;
    }

    /// <summary>
    /// Zoom in by one step (move closer to camera).
    /// </summary>
    public void ZoomIn()
    {
        VirtualObjectsZoomController.Instance?.ZoomIn();
    }

    /// <summary>
    /// Zoom out by one step (move farther from camera).
    /// </summary>
    public void ZoomOut()
    {
        VirtualObjectsZoomController.Instance?.ZoomOut();
    }

    /// <summary>
    /// Reset zoom to default distance.
    /// </summary>
    public void ResetZoom()
    {
        VirtualObjectsZoomController.Instance?.ResetZoom();
    }
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

        // Load configs from Resources if not assigned
        LoadConfigsFromResources();

        // Initialize extracted managers
        InitializeManagers();

        // Subscribe to theme property changes for runtime updates
        RTTThemeConfig.OnAnyThemePropertyChanged += HandleThemePropertyChanged;
    }

    private void Start()
    {
        // Auto-find references
        AutoFindReferences();

        // Initialize app manager with references
        InitializeAppManager();

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
        // Delegate periodic memory check to quality manager
        _qualityManager?.PeriodicUpdate();
    }

    private void OnDestroy()
    {
        // Unsubscribe from theme property changes
        RTTThemeConfig.OnAnyThemePropertyChanged -= HandleThemePropertyChanged;

        UnsubscribeFromControllerEvents();

        // Cleanup active apps (handled by RTTAppManager.OnDestroy)

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

    /// <summary>
    /// Initialize extracted manager classes for cleaner architecture.
    /// These managers handle specific responsibilities while RTTManager coordinates them.
    /// </summary>
    private void InitializeManagers()
    {
        // Panel Manager - handles panel registration and camera depth
        _panelManager = new RTTPanelManager(startingCameraDepth, enablePerformanceLogging);

        // Quality Manager - handles quality levels and memory monitoring
        _qualityManager = new RTTQualityManager(rttConfig, _panelManager, enablePerformanceLogging);

        // App Manager - handles app lifecycle (MonoBehaviour)
        _appManager = gameObject.AddComponent<RTTAppManager>();
    }

    /// <summary>
    /// Initialize RTTAppManager with references after AutoFindReferences.
    /// </summary>
    private void InitializeAppManager()
    {
        if (_appManager == null) return;

        _appManager.Initialize(
            menu,
            mainMenuFrame,
            taskbar,
            appRegistry,
            frameParent,
            transitionOutDuration,
            transitionInDuration,
            useFadeTransition,
            useScaleTransition
        );

        _appManager.SetCreateContentCallback(CreateAppContent);
        _appManager.SetMenuStateCallback(state => {
            _currentMenuState = state;
            OnMenuStateChanged?.Invoke(state);
        });

        // Subscribe to app manager events
        _appManager.OnAppOpened += (appId, instance) => {
            if (enablePerformanceLogging)
                Debug.Log($"[RTTManager] App opened: {appId}");
        };
        _appManager.OnAppClosed += appId => {
            if (enablePerformanceLogging)
                Debug.Log($"[RTTManager] App closed: {appId}");
        };
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

        // Notify ZoomController to recalculate distance after recenter
        VirtualObjectsZoomController.Instance?.OnRecenter();
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
        _panelManager?.RegisterPanel(panel);
    }

    public void UnregisterPanel(RTTCanvasBase panel)
    {
        if (panel == null) return;
        _panelManager?.UnregisterPanel(panel);
    }

    public int AssignCameraDepth() => _panelManager?.AssignCameraDepth() ?? -100;

    public IReadOnlyList<RTTCanvasBase> GetRegisteredPanels() => _panelManager?.RegisteredPanels ?? new List<RTTCanvasBase>().AsReadOnly();

    public T FindPanel<T>() where T : RTTCanvasBase => _panelManager?.FindPanel<T>();
    #endregion

    #region Quality Management
    public void SetQualityLevel(RTTQualityLevel level) => _qualityManager?.SetQualityLevel(level);
    public RTTQualityPreset GetQualityPreset(RTTQualityLevel level) => _qualityManager?.GetQualityPreset(level) ?? rttConfig?.GetPreset(level);
    public void TryScaleUp() => _qualityManager?.TryScaleUp();
    public void TryScaleDown() => _qualityManager?.TryScaleDown();
    #endregion

    #region Memory Management
    public float TotalMemoryMB => _qualityManager?.TotalMemoryMB ?? 0f;
    public float MaxTextureMemoryMB => _qualityManager?.MaxTextureMemoryMB ?? 150f;
    #endregion

    #region Performance Statistics
    public RTTPerformanceStats GetPerformanceStats() => _qualityManager?.GetPerformanceStats() ?? new RTTPerformanceStats();
    public void LogPerformanceStats() => _qualityManager?.LogPerformanceStats();
    #endregion

    #region Panel Utility
    public void MarkAllDirty() => _panelManager?.MarkAllDirty();
    public void HideAll() => _panelManager?.HideAll();
    public void ShowAll() => _panelManager?.ShowAll();
    public void CleanupDestroyedPanels() => _panelManager?.CleanupDestroyedPanels();
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

    #region App Lifecycle - Public API (Delegated to RTTAppManager)
    public RTTAppInstance OpenApp(string appId) => _appManager?.OpenApp(appId);
    public void PrepareApp(string appId) => _appManager?.PrepareApp(appId);
    public bool IsAppPrepared(string appId) => _appManager?.IsAppPrepared(appId) ?? false;
    public void OpenPreparedApp(string appId) => _appManager?.OpenPreparedApp(appId);
    public void SwitchToApp(string appId) => _appManager?.SwitchToApp(appId);
    public void SwitchToHome() => _appManager?.SwitchToHome();
    public void CloseApp(string appId) => _appManager?.CloseApp(appId);
    public bool IsAppOpen(string appId) => _appManager?.IsAppOpen(appId) ?? false;
    public RTTAppInstance GetApp(string appId) => _appManager?.GetApp(appId);
    public IReadOnlyCollection<string> GetOpenAppIds() => _appManager?.GetOpenAppIds() ?? new List<string>();
    #endregion

    // App Lifecycle code (Internal + Coroutines) moved to RTTAppManager

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
            case RTTAppRegistry.AppType.Media:
                CreateMediaContent(instance);
                break;
            case RTTAppRegistry.AppType.Browser:
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

    private void CreateMediaContent(RTTAppInstance instance)
    {
        Debug.Log($"[RTTManager] Creating Media content...");

        // Create controller
        GameObject controllerObj = new GameObject($"MediaController_{instance.AppId}");
        controllerObj.transform.SetParent(this.transform);
        var controller = controllerObj.AddComponent<VRMediaAppController>();

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

    // Animation Helpers moved to RTTAppManager
}
