using UnityEngine;

/// <summary>
/// RTTBootstrapper - Automatically creates RTTMenuFrame and RTTTaskbar on app start.
/// Attach this to VirtualObjects or any parent object.
/// Creates RTTMenuFrame as primary, RTTTaskbar follows the menu frame.
/// </summary>
public class RTTBootstrapper : MonoBehaviour
{
    #region Configuration
    [Header("Menu Frame Settings")]
    [SerializeField] private float menuWidth = 1.6f;
    [SerializeField] private float menuHeight = 0.9f;
    [SerializeField] private float menuLogicalWidth = 1920f;

    [Header("Taskbar Settings")]
    [SerializeField] private int section1Capacity = 4;
    [SerializeField] private int section2Capacity = 4;
    [SerializeField] private float taskbarSpacingMultiplier = 1.1f;

    [Header("Visual")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);

    [Header("Auto Init")]
    [SerializeField] private bool autoInitialize = true;
    [SerializeField] private bool faceCamera = true;

    [Header("Zoom Settings")]
    [Tooltip("Enable zoom controller for VirtualObjects")]
    [SerializeField] private bool enableZoomController = true;
    [Tooltip("Minimum zoom distance from camera")]
    [SerializeField] private float zoomMinDistance = 1.0f;
    [Tooltip("Maximum zoom distance from camera")]
    [SerializeField] private float zoomMaxDistance = 2.0f;
    [Tooltip("Default/initial zoom distance")]
    [SerializeField] private float zoomDefaultDistance = 1.8f;
    #endregion

    #region Private Fields
    private RTTMenu _menu;
    private RTTMenuFrame _menuFrame;
    private RTTMiniFrame _miniFrame;
    private RTTTaskbar _taskbar;
    private VirtualObjectsZoomController _zoomController;
    #endregion

    #region Properties
    public RTTMenu Menu => _menu;
    public RTTMenuFrame MenuFrame => _menuFrame;
    public RTTMiniFrame MiniFrame => _miniFrame;
    public RTTTaskbar Taskbar => _taskbar;
    public VirtualObjectsZoomController ZoomController => _zoomController;
    #endregion

    #region Lifecycle
    private void Start()
    {
        if (autoInitialize)
        {
            Initialize();
        }
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Initialize the RTT UI system. Creates RTTMenu container, MenuFrame, and Taskbar.
    /// Structure: VirtualObjects
    ///              ├── RTTMenu (container)
    ///              │     └── RTTMenuFrame_main
    ///              └── RTTTaskbar (follows RTTMenu)
    /// </summary>
    public void Initialize()
    {
        if (_menu != null)
        {
            Debug.LogWarning("[RTTBootstrapper] Already initialized");
            return;
        }

        CreateMenu();

        // Face camera BEFORE creating taskbar (so taskbar copies correct rotation)
        if (faceCamera)
        {
            FaceCameraImmediate();
        }

        CreateTaskbar();

        // Register with RTTManager if available
        RegisterWithManager();

        // Create ZoomController if enabled
        if (enableZoomController)
        {
            CreateZoomController();
        }

        Debug.Log("[RTTBootstrapper] Initialization complete");
    }

    /// <summary>
    /// Destroy and recreate the UI.
    /// </summary>
    public void Reinitialize()
    {
        Cleanup();
        Initialize();
    }

    /// <summary>
    /// Cleanup created objects.
    /// </summary>
    public void Cleanup()
    {
        // Cleanup ZoomController first
        if (_zoomController != null)
        {
            Destroy(_zoomController);
            _zoomController = null;
        }

        if (_miniFrame != null)
        {
            Destroy(_miniFrame.gameObject);
            _miniFrame = null;
            _taskbar = null;
        }

        if (_menu != null)
        {
            Destroy(_menu.gameObject);
            _menu = null;
            _menuFrame = null;
        }
        else if (_menuFrame != null)
        {
            Destroy(_menuFrame.gameObject);
            _menuFrame = null;
        }
    }
    #endregion

    #region Private Methods
    private void CreateMenu()
    {
        // Create RTTMenu container
        _menu = RTTMenu.Create(transform, "RTTMenu");

        // Create main RTTMenuFrame inside RTTMenu
        _menuFrame = _menu.CreateMainFrame(
            widthMeters: menuWidth,
            heightMeters: menuHeight,
            logicalWidth: menuLogicalWidth
        );

        Debug.Log("[RTTBootstrapper] Created RTTMenu with RTTMenuFrame_main");
    }

    private void CreateTaskbar()
    {
        // Create GameObject for RTTMiniFrame + RTTTaskbar
        GameObject taskbarObj = new GameObject("RTTTaskbar");
        taskbarObj.transform.SetParent(transform, false);

        // Add RTTMiniFrame first (required by RTTTaskbar)
        _miniFrame = taskbarObj.AddComponent<RTTMiniFrame>();

        // Configure MiniFrame
        _miniFrame.Configure(section1Capacity, section2Capacity);

        // Set follow target to RTTMenu container (not individual frame)
        _miniFrame.SetFollowTarget(_menu.transform);
        _miniFrame.SetSpacingMultiplier(taskbarSpacingMultiplier);

        // Add RTTTaskbar (logic controller)
        _taskbar = taskbarObj.AddComponent<RTTTaskbar>();

        Debug.Log($"[RTTBootstrapper] Created RTTTaskbar following RTTMenu");
    }

    private void FaceCameraImmediate()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[RTTBootstrapper] No main camera found for face camera");
            return;
        }

        Vector3 toCamera = cam.transform.position - _menu.transform.position;
        if (toCamera.sqrMagnitude < 0.001f)
        {
            // Camera too close, use camera forward
            toCamera = -cam.transform.forward;
        }

        if (toCamera.sqrMagnitude < 0.001f) return;

        // Face camera fully (including pitch/tilt)
        Quaternion rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        _menu.transform.rotation = rotation;

        Debug.Log("[RTTBootstrapper] RTTMenu oriented towards camera");
    }

    private void RegisterWithManager()
    {
        RTTManager manager = RTTManager.Instance;
        if (manager == null)
        {
            manager = FindObjectOfType<RTTManager>();
        }

        if (manager != null)
        {
            // RTTManager will auto-detect primary RTTMenuFrame via RTTMenuFrame.PrimaryInstance
            // and auto-detect taskbar via FindObjectOfType in its initialization
            Debug.Log("[RTTBootstrapper] RTTManager found, UI will be auto-detected");
        }
        else
        {
            Debug.LogWarning("[RTTBootstrapper] RTTManager not found. Main Menu initialization may need manual setup.");
        }
    }

    private void CreateZoomController()
    {
        // Add ZoomController component to VirtualObjects (this GameObject)
        _zoomController = gameObject.AddComponent<VirtualObjectsZoomController>();

        // Configure zoom settings from RTTBootstrapper inspector values
        _zoomController.Configure(zoomMinDistance, zoomMaxDistance, zoomDefaultDistance);

        // Set references explicitly for reliability
        _zoomController.SetPrimaryFrame(_menuFrame);
        _zoomController.SetTaskbarFrame(_miniFrame);

        // Explicitly call Initialize() now that references are set
        // This ensures initialization happens immediately rather than waiting for Start()
        _zoomController.Initialize();

        Debug.Log($"[RTTBootstrapper] Created VirtualObjectsZoomController (range: {zoomMinDistance}m - {zoomMaxDistance}m, default: {zoomDefaultDistance}m)");
    }
    #endregion

    #region Editor
#if UNITY_EDITOR
    private void OnValidate()
    {
        // Clamp values
        menuWidth = Mathf.Max(0.1f, menuWidth);
        menuHeight = Mathf.Max(0.1f, menuHeight);
        menuLogicalWidth = Mathf.Max(100f, menuLogicalWidth);
        section1Capacity = Mathf.Clamp(section1Capacity, 1, 10);
        section2Capacity = Mathf.Clamp(section2Capacity, 1, 10);
        taskbarSpacingMultiplier = Mathf.Max(0.1f, taskbarSpacingMultiplier);

        // Zoom settings validation
        zoomMinDistance = Mathf.Max(0.1f, zoomMinDistance);
        zoomMaxDistance = Mathf.Max(zoomMinDistance + 0.1f, zoomMaxDistance);
        zoomDefaultDistance = Mathf.Clamp(zoomDefaultDistance, zoomMinDistance, zoomMaxDistance);
    }
#endif
    #endregion
}
