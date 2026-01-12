using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;
using VRWorkspace.Streaming;

/// <summary>
/// RTTRemoteTaskbar - Taskbar for Remote Desktop streaming mode.
/// Follows WorldPanelClusterRig and displays streaming metrics.
///
/// Section 1: Back, Bitrate (display), FPS (display), Passthrough, Recenter, Zoom (display)
/// Section 2: Monitor 1-3 (dynamic display)
/// Section 3: Latency text (replaces WiFi icon)
/// </summary>
[RequireComponent(typeof(RTTMiniFrame))]
public class RTTRemoteTaskbar : MonoBehaviour
{
    #region Static Instance
    private static RTTRemoteTaskbar _instance;
    public static RTTRemoteTaskbar Instance => _instance;
    #endregion

    #region Configuration
    [Header("Icons")]
    [SerializeField] private Sprite iconBack;
    [SerializeField] private Sprite iconPassthrough;
    [SerializeField] private Sprite iconRecenter;
    [SerializeField] private Sprite iconZoom;
    [SerializeField] private Sprite iconMonitor;

    // Dynamic icons for bitrate (10, 15, 20, 25, 30 Mbps)
    private Dictionary<int, Sprite> _bitrateIcons = new Dictionary<int, Sprite>();

    // Screen icons (screen_1, screen_2, screen_3)
    private Dictionary<int, Sprite> _screenIcons = new Dictionary<int, Sprite>();

    // Screen button states (all ON by default)
    private Dictionary<int, bool> _screenStates = new Dictionary<int, bool>();
    // Dynamic icons for FPS (30, 45, 60)
    private Dictionary<int, Sprite> _fpsIcons = new Dictionary<int, Sprite>();
    #endregion

    #region Events
    public event Action OnMenuRequested;
    public event Action OnDisconnectRequested;
    #endregion

    #region Private Fields
    private RTTMiniFrame _miniFrame;
    private ConnectionViewModel _viewModel;

    // Section 1 references
    private GameObject _backButton;
    private GameObject _bitrateButton;
    private GameObject _fpsButton;
    private GameObject _passthroughButton;
    private int _currentBitrateMbps = 20;
    private int _currentFps = 60;

    // Section 2 references
    private GameObject _zoomButton;
    private List<GameObject> _monitorSlots = new List<GameObject>();

    // Section 3 reference
    private TextMeshProUGUI _latencyText;

    // Expansion panel
    private RTTTaskbarExpansion _expansionPanel;

    // ClusterRig reference for panel enable/disable
    private WorldPanelClusterRig _clusterRig;

    // State
    private bool _isPassthroughOn = false;

    // Colors
    private readonly Color _cyanColor = new Color(0f, 0.9f, 1f);
    private readonly Color _purpleColor = new Color(0.9f, 0.3f, 1f);
    #endregion

    #region Lifecycle
    private void Awake()
    {
        _miniFrame = GetComponent<RTTMiniFrame>();
        LoadIcons();
    }

    private void Start()
    {
        _instance = this;
        StartCoroutine(InitializeAfterFrame());
    }

    private IEnumerator InitializeAfterFrame()
    {
        // Wait for RTTMiniFrame to build UI
        yield return null;
        yield return null;

        // Configure Section 3 to show text instead of WiFi icon
        _miniFrame.SetWifiMode(WifiDisplayMode.Text);

        // Build sections
        AddSection1Buttons();
        AddSection2Slots();
        SetupLatencyDisplay();

        // Create expansion panel
        CreateExpansionPanel();

        // Bind to ViewModel
        BindToViewModel();

        // Sync passthrough state
        SyncPassthroughWithModeController();

        _miniFrame.MarkDirty();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        UnbindFromViewModel();
        CleanupExpansionPanel();
    }

    private void LateUpdate()
    {
        UpdateMetricsDisplay();
    }
    #endregion

    #region ViewModel Binding
    private void BindToViewModel()
    {
        _viewModel = ServiceLocator.Get<ConnectionViewModel>();
        if (_viewModel == null)
        {
            Debug.LogWarning("[RTTRemoteTaskbar] ConnectionViewModel not found in ServiceLocator");
            return;
        }

        // Subscribe to current bitrate/fps changes (these update when user changes settings)
        _viewModel.CurrentBitrateKbps.OnChanged += OnBitrateChanged;
        _viewModel.CurrentFps.OnChanged += OnFpsChanged;

        // Subscribe to config changes for monitor count
        _viewModel.AppliedConfig.OnChanged += OnConfigChanged;

        // Initial update
        UpdateBitrateIcon(_viewModel.CurrentBitrateKbps.Value);
        UpdateFpsIcon(_viewModel.CurrentFps.Value);
        if (_viewModel.AppliedConfig.Value != null)
        {
            UpdateMonitorSlotVisibility(_viewModel.AppliedConfig.Value.monitors);
        }
    }

    private void UnbindFromViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.CurrentBitrateKbps.OnChanged -= OnBitrateChanged;
            _viewModel.CurrentFps.OnChanged -= OnFpsChanged;
            _viewModel.AppliedConfig.OnChanged -= OnConfigChanged;
        }
    }

    private void OnBitrateChanged(int bitrateKbps)
    {
        UpdateBitrateIcon(bitrateKbps);

        // Sync expansion panel
        if (_expansionPanel != null)
        {
            _expansionPanel.SetCurrentBitrate(bitrateKbps / 1000);
        }

        _miniFrame?.MarkDirty();
    }

    private void OnFpsChanged(int fps)
    {
        UpdateFpsIcon(fps);

        // Sync expansion panel
        if (_expansionPanel != null)
        {
            _expansionPanel.SetCurrentFps(fps);
        }

        _miniFrame?.MarkDirty();
    }

    private void OnConfigChanged(StreamingConfig config)
    {
        if (config == null) return;

        // Only update monitor count from config (bitrate/fps come from CurrentBitrateKbps/CurrentFps)
        UpdateMonitorSlotVisibility(config.monitors);
        _miniFrame?.MarkDirty();
    }
    #endregion

    #region Section 1 - Control Buttons
    private void AddSection1Buttons()
    {
        var section1 = _miniFrame.GetSection1Container();
        if (section1 == null) return;

        float buttonSize = _miniFrame.ButtonSize;

        // 1. Back button
        _backButton = CreateIconButton(section1, iconBack, "Back", _cyanColor, buttonSize, OnBackClicked);

        // 2. Bitrate button (clickable to show expansion panel)
        var defaultBitrateIcon = GetBitrateIcon(_currentBitrateMbps);
        _bitrateButton = CreateIconButton(section1, defaultBitrateIcon, "Bitrate", _cyanColor, buttonSize, OnBitrateClicked);

        // 3. FPS button (clickable to show expansion panel)
        var defaultFpsIcon = GetFpsIcon(_currentFps);
        _fpsButton = CreateIconButton(section1, defaultFpsIcon, "FPS", _cyanColor, buttonSize, OnFpsClicked);

        // 4. Zoom display button (icon only)
        _zoomButton = CreateDisplayIconButton(section1, iconZoom, "Zoom", _cyanColor, buttonSize);

        // 5. Passthrough toggle
        _passthroughButton = CreateIconButton(section1, iconPassthrough, "Passthrough", _cyanColor, buttonSize, TogglePassthrough);

        // 6. Recenter
        CreateIconButton(section1, iconRecenter, "Recenter", _cyanColor, buttonSize, RecenterObject);
    }

    private void OnBackClicked()
    {
        Debug.Log("[RTTRemoteTaskbar] Back pressed - showing menu");
        HideExpansionPanel();
        OnMenuRequested?.Invoke();
        _miniFrame.MarkDirty();
    }

    private void OnBitrateClicked()
    {
        if (_expansionPanel == null) return;

        // Toggle behavior: if already showing Bitrate options, hide it
        if (_expansionPanel.IsVisible && _expansionPanel.CurrentType == RTTTaskbarExpansion.ExpansionType.Bitrate)
        {
            Debug.Log("[RTTRemoteTaskbar] Bitrate clicked - hiding expansion panel (toggle)");
            _expansionPanel.Hide();
        }
        else
        {
            // Show bitrate options (will auto-close if showing FPS options)
            Debug.Log("[RTTRemoteTaskbar] Bitrate clicked - showing expansion panel");
            Vector3? buttonWorldPos = GetButtonWorldPosition(_bitrateButton);
            _expansionPanel.ShowBitrateOptions(_currentBitrateMbps, buttonWorldPos);
        }
        _miniFrame.MarkDirty();
    }

    private void OnFpsClicked()
    {
        if (_expansionPanel == null) return;

        // Toggle behavior: if already showing FPS options, hide it
        if (_expansionPanel.IsVisible && _expansionPanel.CurrentType == RTTTaskbarExpansion.ExpansionType.Fps)
        {
            Debug.Log("[RTTRemoteTaskbar] FPS clicked - hiding expansion panel (toggle)");
            _expansionPanel.Hide();
        }
        else
        {
            // Show FPS options (will auto-close if showing Bitrate options)
            Debug.Log("[RTTRemoteTaskbar] FPS clicked - showing expansion panel");
            Vector3? buttonWorldPos = GetButtonWorldPosition(_fpsButton);
            _expansionPanel.ShowFpsOptions(_currentFps, buttonWorldPos);
        }
        _miniFrame.MarkDirty();
    }

    /// <summary>
    /// Get world position of a button in the RTT canvas.
    /// Uses RectTransformUtility to get accurate bounds even with layout groups.
    /// </summary>
    private Vector3? GetButtonWorldPosition(GameObject button)
    {
        if (button == null || _miniFrame == null) return null;

        var buttonRT = button.GetComponent<RectTransform>();
        if (buttonRT == null) return null;

        // Get miniframe's canvas to calculate relative bounds
        var miniFrameCanvas = _miniFrame.GetCanvas();
        if (miniFrameCanvas == null) return null;

        var canvasRT = miniFrameCanvas.GetComponent<RectTransform>();
        if (canvasRT == null) return null;

        // Get bounds of button relative to canvas center
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRT, buttonRT);

        // Get miniframe's world size and resolution
        Vector2 worldSize = _miniFrame.GetWorldSize();
        Vector2 resolution = new Vector2(_miniFrame.TotalWidth, _miniFrame.TotalHeight);

        // Convert pixels to meters (relative to miniframe center)
        float pixelToMeter = worldSize.x / resolution.x;
        float localX = bounds.center.x * pixelToMeter;
        float localY = bounds.center.y * pixelToMeter;

        // Transform to world space
        Vector3 localRight = _miniFrame.transform.TransformDirection(Vector3.right);
        Vector3 localUp = _miniFrame.transform.TransformDirection(Vector3.up);

        Vector3 worldPos = _miniFrame.transform.position + localRight * localX + localUp * localY;
        return worldPos;
    }

    /// <summary>
    /// Call this when menu is dismissed to reset any state.
    /// </summary>
    public void OnMenuDismissed()
    {
        // Reserved for future use
    }
    #endregion

    #region Expansion Panel
    private void CreateExpansionPanel()
    {
        // Create expansion panel as sibling to taskbar
        GameObject expansionObj = new GameObject("RTTTaskbarExpansion");
        expansionObj.transform.SetParent(transform.parent, false);

        _expansionPanel = expansionObj.AddComponent<RTTTaskbarExpansion>();
        _expansionPanel.SetFollowTarget(transform);

        // Subscribe to events
        _expansionPanel.OnBitrateSelected += OnExpansionBitrateSelected;
        _expansionPanel.OnFpsSelected += OnExpansionFpsSelected;

        Debug.Log("[RTTRemoteTaskbar] Expansion panel created");
    }

    private void CleanupExpansionPanel()
    {
        if (_expansionPanel != null)
        {
            _expansionPanel.OnBitrateSelected -= OnExpansionBitrateSelected;
            _expansionPanel.OnFpsSelected -= OnExpansionFpsSelected;

            if (Application.isPlaying)
                Destroy(_expansionPanel.gameObject);
            else
                DestroyImmediate(_expansionPanel.gameObject);

            _expansionPanel = null;
        }
    }

    private void OnExpansionBitrateSelected(int mbps)
    {
        Debug.Log($"[RTTRemoteTaskbar] Bitrate selected from expansion: {mbps} Mbps");

        // Update ViewModel via UpdateConfigAsync
        if (_viewModel != null)
        {
            int bitrateKbps = mbps * 1000;
            // Fire and forget - the async update will trigger OnBitrateChanged
            _ = _viewModel.UpdateConfigAsync(null, bitrateKbps);
        }

        _miniFrame.MarkDirty();
    }

    private void OnExpansionFpsSelected(int fps)
    {
        Debug.Log($"[RTTRemoteTaskbar] FPS selected from expansion: {fps}");

        // Update ViewModel via UpdateConfigAsync
        if (_viewModel != null)
        {
            // Fire and forget - the async update will trigger OnFpsChanged
            _ = _viewModel.UpdateConfigAsync(fps, null);
        }

        _miniFrame.MarkDirty();
    }

    /// <summary>
    /// Get the expansion panel reference.
    /// </summary>
    public RTTTaskbarExpansion GetExpansionPanel() => _expansionPanel;

    /// <summary>
    /// Hide the expansion panel if visible.
    /// </summary>
    public void HideExpansionPanel()
    {
        if (_expansionPanel != null && _expansionPanel.IsVisible)
        {
            _expansionPanel.Hide();
        }
    }
    #endregion

    #region Section 2 - Screen Toggle Buttons
    private void AddSection2Slots()
    {
        var section2 = _miniFrame.GetSection2Container();
        if (section2 == null) return;

        float buttonSize = _miniFrame.ButtonSize;

        // Screen toggle buttons (dynamic visibility based on monitor count)
        _monitorSlots.Clear();
        _screenStates.Clear();

        for (int i = 0; i < 3; i++)
        {
            int screenNumber = i + 1;
            _screenStates[screenNumber] = true; // All ON by default

            var btn = CreateScreenToggleButton(section2, screenNumber, buttonSize);
            _monitorSlots.Add(btn);

            // Initially hidden (will be shown based on monitor count)
            var cg = btn.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 0f;
        }
    }

    private GameObject CreateScreenToggleButton(Transform parent, int screenNumber, float buttonSize)
    {
        // Get screen icon for this number
        Sprite screenIcon = _screenIcons.TryGetValue(screenNumber, out var icon) ? icon : iconMonitor;

        // Default state is ON (purple color)
        bool isOn = _screenStates.TryGetValue(screenNumber, out var state) ? state : true;
        Color btnColor = isOn ? _purpleColor : _cyanColor;

        int capturedNumber = screenNumber;

        // Create toggle button using VRButtonFactory
        var btn = VRButtonFactory.CreateBareIconButton(
            parent, buttonSize, screenIcon, btnColor,
            () => OnScreenToggleClicked(capturedNumber),
            0.05f, 0.6f
        );

        btn.name = $"ScreenBtn_{screenNumber}";

        // Add CanvasGroup for visibility control
        var cg = btn.GetComponent<CanvasGroup>();
        if (cg == null) cg = btn.AddComponent<CanvasGroup>();

        SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));
        return btn;
    }

    private void OnScreenToggleClicked(int screenNumber)
    {
        // Toggle state
        bool newState = !(_screenStates.TryGetValue(screenNumber, out var current) ? current : true);
        _screenStates[screenNumber] = newState;

        // Update button appearance
        UpdateScreenButtonAppearance(screenNumber);

        Debug.Log($"[RTTRemoteTaskbar] Screen {screenNumber} toggled: {(newState ? "ON" : "OFF")}");

        // screenNumber is 1-based, panelIndex is 0-based
        int panelIndex = screenNumber - 1;

        // Notify ClusterRig to show/hide this monitor panel
        if (_clusterRig != null)
        {
            _clusterRig.SetPanelEnabled(panelIndex, newState);
        }

        // Notify server to pause/resume this monitor's stream
        if (_viewModel != null)
        {
            if (newState)
            {
                // Screen ON - resume streaming for this monitor
                _ = _viewModel.ResumeMonitorAsync(panelIndex);
            }
            else
            {
                // Screen OFF - pause streaming for this monitor
                _ = _viewModel.PauseMonitorAsync(panelIndex);
            }
        }

        _miniFrame?.MarkDirty();
    }

    private void UpdateScreenButtonAppearance(int screenNumber)
    {
        int index = screenNumber - 1;
        if (index < 0 || index >= _monitorSlots.Count) return;

        var btn = _monitorSlots[index];
        if (btn == null) return;

        bool isOn = _screenStates.TryGetValue(screenNumber, out var state) ? state : true;
        Color targetColor = isOn ? _purpleColor : _cyanColor;

        // Update glow color
        VRButtonFactory.SetBareIconButtonGlowColor(btn, targetColor);
    }

    private void UpdateMonitorSlotVisibility(int monitorCount)
    {
        for (int i = 0; i < _monitorSlots.Count; i++)
        {
            var slot = _monitorSlots[i];
            if (slot == null) continue;

            int screenNumber = i + 1;
            bool isVisible = (i < monitorCount);

            var cg = slot.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = isVisible ? 1f : 0f;
            }

            // Reset state to ON when becoming visible
            if (isVisible && !_screenStates.ContainsKey(screenNumber))
            {
                _screenStates[screenNumber] = true;
                UpdateScreenButtonAppearance(screenNumber);
            }
        }
    }

    /// <summary>
    /// Get the current state of a screen button.
    /// </summary>
    public bool IsScreenOn(int screenNumber)
    {
        return _screenStates.TryGetValue(screenNumber, out var state) ? state : true;
    }

    /// <summary>
    /// Set the state of a screen button and update panel visibility.
    /// </summary>
    public void SetScreenState(int screenNumber, bool isOn)
    {
        _screenStates[screenNumber] = isOn;
        UpdateScreenButtonAppearance(screenNumber);

        // screenNumber is 1-based, panelIndex is 0-based
        int panelIndex = screenNumber - 1;

        // Update ClusterRig panel visibility
        if (_clusterRig != null)
        {
            _clusterRig.SetPanelEnabled(panelIndex, isOn);
        }

        // Notify server to pause/resume this monitor's stream
        if (_viewModel != null)
        {
            if (isOn)
            {
                _ = _viewModel.ResumeMonitorAsync(panelIndex);
            }
            else
            {
                _ = _viewModel.PauseMonitorAsync(panelIndex);
            }
        }
    }
    #endregion

    #region Section 3 - Latency Display
    private void SetupLatencyDisplay()
    {
        // Get network text reference from RTTMiniFrame
        _latencyText = _miniFrame.GetNetworkText();

        if (_latencyText != null)
        {
            _latencyText.text = "-- ms";
            _latencyText.fontSize = 24f;
            _latencyText.enableWordWrapping = false;  // Single line
            _latencyText.overflowMode = TMPro.TextOverflowModes.Overflow;
        }
    }

    private void UpdateLatencyDisplay(double pingMs)
    {
        if (_latencyText == null) return;

        if (pingMs > 0)
        {
            _latencyText.text = $"{Mathf.RoundToInt((float)pingMs)} ms";
            _latencyText.color = GetLatencyColor(pingMs);
        }
        else
        {
            _latencyText.text = "-- ms";
            _latencyText.color = Color.gray;
        }
    }

    private Color GetLatencyColor(double pingMs)
    {
        if (pingMs < 20) return new Color(0.3f, 1f, 0.3f);   // Green - Excellent
        if (pingMs < 50) return new Color(1f, 1f, 0.3f);    // Yellow - Good
        if (pingMs < 100) return new Color(1f, 0.6f, 0.2f); // Orange - Fair
        return new Color(1f, 0.3f, 0.3f);                    // Red - Poor
    }
    #endregion

    #region Metrics Update
    private void UpdateMetricsDisplay()
    {
        if (_viewModel == null) return;

        // Update latency from streaming metrics via ViewModel
        var metrics = _viewModel.GetMetrics();
        if (metrics != null)
        {
            UpdateLatencyDisplay(metrics.CurrentPingMs);
        }
    }

    private void UpdateBitrateIcon(int bitrateKbps)
    {
        int mbps = bitrateKbps / 1000;
        if (mbps == _currentBitrateMbps) return;

        _currentBitrateMbps = mbps;
        var icon = GetBitrateIcon(mbps);
        if (icon != null)
        {
            SetBareIconButtonIcon(_bitrateButton, icon);
        }
    }

    private void UpdateFpsIcon(int fps)
    {
        if (fps == _currentFps) return;

        _currentFps = fps;
        var icon = GetFpsIcon(fps);
        if (icon != null)
        {
            SetBareIconButtonIcon(_fpsButton, icon);
        }
    }

    private Sprite GetBitrateIcon(int mbps)
    {
        // Snap to nearest valid option: 10, 15, 20, 25, 30
        int[] validOptions = { 10, 15, 20, 25, 30 };
        int nearest = validOptions[0];
        int minDiff = Mathf.Abs(mbps - nearest);

        foreach (int opt in validOptions)
        {
            int diff = Mathf.Abs(mbps - opt);
            if (diff < minDiff)
            {
                minDiff = diff;
                nearest = opt;
            }
        }

        if (_bitrateIcons.TryGetValue(nearest, out Sprite icon))
        {
            return icon;
        }
        return _bitrateIcons.ContainsKey(20) ? _bitrateIcons[20] : null;
    }

    private Sprite GetFpsIcon(int fps)
    {
        // Snap to nearest valid option: 30, 45, 60
        int[] validOptions = { 30, 45, 60 };
        int nearest = validOptions[0];
        int minDiff = Mathf.Abs(fps - nearest);

        foreach (int opt in validOptions)
        {
            int diff = Mathf.Abs(fps - opt);
            if (diff < minDiff)
            {
                minDiff = diff;
                nearest = opt;
            }
        }

        if (_fpsIcons.TryGetValue(nearest, out Sprite icon))
        {
            return icon;
        }
        return _fpsIcons.ContainsKey(60) ? _fpsIcons[60] : null;
    }
    #endregion

    #region Passthrough
    private void TogglePassthrough()
    {
        _isPassthroughOn = !_isPassthroughOn;
        Debug.Log($"[RTTRemoteTaskbar] Passthrough: {(_isPassthroughOn ? "ON" : "OFF")}");

        UpdatePassthroughButtonColor();

        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
        }

        _miniFrame.MarkDirty();
    }

    private void SyncPassthroughWithModeController()
    {
        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            _isPassthroughOn = modeController.mode == ViewMode.RealWorld;
            UpdatePassthroughButtonColor();
        }
    }

    private void UpdatePassthroughButtonColor()
    {
        if (_passthroughButton == null) return;

        Color targetColor = _isPassthroughOn ? _purpleColor : _cyanColor;
        SetBareIconButtonColor(_passthroughButton, targetColor);
    }

    public void SetPassthrough(bool isOn)
    {
        if (_isPassthroughOn != isOn)
        {
            _isPassthroughOn = isOn;
            UpdatePassthroughButtonColor();

            var modeController = FindObjectOfType<ModeController>();
            if (modeController != null)
            {
                modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
            }

            _miniFrame.MarkDirty();
        }
    }
    #endregion

    #region Recenter
    private void RecenterObject()
    {
        Debug.Log("[RTTRemoteTaskbar] Recenter clicked");
        StartCoroutine(RecenterRoutine());
    }

    private IEnumerator RecenterRoutine()
    {
        VRGazeReticle reticle = VRGazeReticle.Instance;
        if (reticle == null) reticle = FindObjectOfType<VRGazeReticle>();

        if (reticle != null)
        {
            reticle.EnterRecenterMode(iconRecenter);
        }

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            if (reticle != null)
            {
                reticle.UpdateRecenterProgress(progress);
            }

            yield return null;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            RecenterAllVirtualObjects(cam);
        }

        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }

        _miniFrame.MarkDirty();
        Debug.Log("[RTTRemoteTaskbar] Recenter complete.");
    }

    private void RecenterAllVirtualObjects(Camera cam)
    {
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
        if (virtualObjectsParent == null)
        {
            Debug.LogWarning("[RTTRemoteTaskbar] VirtualObjects parent not found");
            RecenterFollowTarget(cam);
            return;
        }

        // Find primary menu frame as pivot
        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null)
        {
            RecenterFollowTarget(cam);
            return;
        }

        Vector3 pivotPos = primary.transform.position;
        Quaternion pivotRot = primary.transform.rotation;

        List<Transform> children = new List<Transform>();
        List<Vector3> relativePositions = new List<Vector3>();
        List<Quaternion> relativeRotations = new List<Quaternion>();

        foreach (Transform child in virtualObjectsParent.transform)
        {
            children.Add(child);
            Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
            relativePositions.Add(relPos);
            Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
            relativeRotations.Add(relRot);
        }

        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(
            new Vector2(pivotPos.x, pivotPos.z),
            new Vector2(camPos.x, camPos.z)
        );

        Vector3 newPivotPos = camPos + camForward * hDist;
        newPivotPos.y = pivotPos.y;
        Quaternion newPivotRot = Quaternion.LookRotation(camForward);

        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            child.position = newPivotPos + newPivotRot * relativePositions[i];
            child.rotation = newPivotRot * relativeRotations[i];
        }
    }

    private void RecenterFollowTarget(Camera cam)
    {
        var followTarget = _miniFrame.GetFollowTarget();
        if (followTarget != null)
        {
            RecenterTransform(followTarget, cam);
        }
    }

    private void RecenterTransform(Transform target, Camera cam)
    {
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 currentPos = target.position;
        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

        Vector3 newPos = camPos + camForward * hDist;
        newPos.y = currentPos.y;

        target.position = newPos;
        target.rotation = Quaternion.LookRotation(camForward);
    }
    #endregion

    #region Button Factory
    private GameObject CreateIconButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize, Action onClick)
    {
        var btn = VRButtonFactory.CreateBareIconButton(
            parent, buttonSize, icon, glowColor,
            () =>
            {
                onClick?.Invoke();
                _miniFrame.MarkDirty();
            },
            0.05f, 0.6f
        );

        btn.name = $"Btn_{name}";
        SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

        return btn;
    }

    /// <summary>
    /// Create a BareIconButton for display-only purposes (no click action).
    /// Icon is positioned at top, value text will be added below.
    /// </summary>
    private GameObject CreateDisplayIconButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize)
    {
        var btn = VRButtonFactory.CreateBareIconButton(
            parent, buttonSize, icon, glowColor,
            null,  // No click action - display only
            0.05f, 0.6f
        );

        btn.name = $"Display_{name}";
        SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

        return btn;
    }

    private GameObject CreateIconInButton(GameObject button, Sprite icon, float buttonSize)
    {
        if (icon == null) return null;

        float iconSize = buttonSize * 0.5f;

        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(button.transform, false);

        RectTransform rt = iconObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(iconSize, iconSize);
        rt.anchorMin = new Vector2(0.5f, 0.6f);
        rt.anchorMax = new Vector2(0.5f, 0.6f);
        rt.anchoredPosition = Vector2.zero;

        Image img = iconObj.AddComponent<Image>();
        img.sprite = icon;
        img.color = Color.Lerp(_cyanColor, Color.white, 0.9f);
        img.raycastTarget = false;

        SetLayerRecursively(iconObj, LayerMask.NameToLayer("UI"));
        return iconObj;
    }

    private TextMeshProUGUI GetOrCreateValueText(GameObject button, string initialText)
    {
        // Check if text already exists
        var existingText = button.GetComponentInChildren<TextMeshProUGUI>();
        if (existingText != null)
        {
            existingText.text = initialText;
            return existingText;
        }

        GameObject textObj = new GameObject("ValueText");
        textObj.transform.SetParent(button.transform, false);

        RectTransform rt = textObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0.45f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = initialText;
        tmp.fontSize = 20f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        SetLayerRecursively(textObj, LayerMask.NameToLayer("UI"));
        return tmp;
    }

    private void SetBareIconButtonColor(GameObject container, Color color)
    {
        if (container == null) return;

        // Find icon in VRButtonFactory structure: container > HitArea > Visuals > Content > Icon
        Transform iconTransform = container.transform.Find("HitArea/Visuals/Content/Icon");
        if (iconTransform == null) return;

        Image iconImg = iconTransform.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.color = Color.Lerp(color, Color.white, 0.9f);

            Shadow[] shadows = iconTransform.GetComponents<Shadow>();
            if (shadows.Length >= 2)
            {
                Color glowCol = Color.Lerp(color, Color.white, 0.7f);
                glowCol.a = 0.4f;
                shadows[0].effectColor = glowCol;
                shadows[1].effectColor = glowCol;
            }
        }
    }

    /// <summary>
    /// Update the icon sprite of a BareIconButton.
    /// Structure: Wrapper > HitArea > Visuals > Content > Icon
    /// </summary>
    private void SetBareIconButtonIcon(GameObject container, Sprite newIcon)
    {
        if (container == null || newIcon == null) return;

        // Find icon in VRButtonFactory structure: container > HitArea > Visuals > Content > Icon
        Transform iconTransform = container.transform.Find("HitArea/Visuals/Content/Icon");

        if (iconTransform == null)
        {
            Debug.LogWarning($"[RTTRemoteTaskbar] SetBareIconButtonIcon: Icon not found in {container.name}");
            return;
        }

        Image iconImg = iconTransform.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.sprite = newIcon;
        }
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer == -1) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    #endregion

    #region Public API
    public bool IsPassthroughOn => _isPassthroughOn;

    /// <summary>
    /// Set the follow target (typically WorldPanelClusterRig).
    /// </summary>
    public void SetFollowTarget(Transform target)
    {
        if (_miniFrame != null && target != null)
        {
            _miniFrame.SetFollowTarget(target);

            // Try to get ClusterRig reference for panel enable/disable
            if (_clusterRig == null && target != null)
            {
                _clusterRig = target.GetComponent<WorldPanelClusterRig>();
            }
        }
    }

    /// <summary>
    /// Set the follow target (WorldPanelClusterRig overload).
    /// Also stores reference for panel enable/disable functionality.
    /// </summary>
    public void SetFollowTarget(WorldPanelClusterRig clusterRig)
    {
        _clusterRig = clusterRig;
        if (clusterRig != null)
        {
            SetFollowTarget(clusterRig.transform);
        }
    }

    /// <summary>
    /// Hide the taskbar.
    /// </summary>
    public void Hide()
    {
        if (_miniFrame != null)
            _miniFrame.Hide();
    }

    /// <summary>
    /// Show the taskbar.
    /// </summary>
    public void Show()
    {
        if (_miniFrame != null)
            _miniFrame.Show();
    }
    #endregion

    #region Icons
    private void LoadIcons()
    {
        // Static icons
        if (iconBack == null) iconBack = LoadIcon("back");
        if (iconPassthrough == null) iconPassthrough = LoadIcon("passthrough");
        if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
        if (iconZoom == null) iconZoom = LoadIcon("zoom");
        if (iconMonitor == null) iconMonitor = LoadIcon("monitor");

        // Dynamic bitrate icons (10, 15, 20, 25, 30 Mbps)
        // Naming: icon_{bitrate}_mbps (e.g., icon_10_mbps, icon_20_mbps)
        int[] bitrateOptions = { 10, 15, 20, 25, 30 };
        foreach (int mbps in bitrateOptions)
        {
            var icon = LoadIcon($"{mbps}_mbps");
            if (icon != null)
            {
                _bitrateIcons[mbps] = icon;
            }
        }

        // Dynamic FPS icons (30, 45, 60)
        // Naming: icon_{fps}_fps (e.g., icon_30_fps, icon_60_fps)
        int[] fpsOptions = { 30, 45, 60 };
        foreach (int fps in fpsOptions)
        {
            var icon = LoadIcon($"{fps}_fps");
            if (icon != null)
            {
                _fpsIcons[fps] = icon;
            }
        }

        // Screen icons (1, 2, 3)
        // Naming: icon_screen_{number} (e.g., icon_screen_1, icon_screen_2)
        for (int i = 1; i <= 3; i++)
        {
            var icon = LoadIcon($"screen_{i}");
            if (icon != null)
            {
                _screenIcons[i] = icon;
            }
        }

        Debug.Log($"[RTTRemoteTaskbar] Icons loaded - Back:{iconBack != null}, Passthrough:{iconPassthrough != null}, Recenter:{iconRecenter != null}, Zoom:{iconZoom != null}, Monitor:{iconMonitor != null}");
        Debug.Log($"[RTTRemoteTaskbar] Bitrate icons: {_bitrateIcons.Count}/5, FPS icons: {_fpsIcons.Count}/3, Screen icons: {_screenIcons.Count}/3");
    }

    private static Sprite LoadIcon(string name)
    {
        var sprite = Resources.Load<Sprite>($"icon_{name}");
        if (sprite == null)
        {
            Debug.LogWarning($"[RTTRemoteTaskbar] Failed to load icon: icon_{name} from Resources");
        }
        return sprite;
    }
    #endregion
}
