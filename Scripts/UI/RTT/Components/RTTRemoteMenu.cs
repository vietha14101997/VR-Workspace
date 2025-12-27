using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using VRWorkspace.Streaming;

/// <summary>
/// RTT-based Remote Desktop connection menu.
/// Features: Host/Port inputs, Monitors/Resolution/Bitrate/FPS dropdowns, QR scanner.
/// Easy to use: Set themeColor, subscribe to events, call BuildUI().
///
/// V2 Protocol: Supports phased connection flow with button state changes:
/// - "Connect" → "Setup Remote" → "Start Remote"
/// </summary>
public class RTTRemoteMenu : MonoBehaviour
{
    #region Configuration
    [Header("Theme")]
    public Color themeColor = new Color(0f, 0.9f, 1f);
    public Color accentColor = new Color(0.8f, 0.4f, 1f);
    public TMP_FontAsset customFont;

    [Header("V2 Protocol")]
    [Tooltip("Reference to ClusterAutoBinder for V2 protocol phased connection")]
    public ClusterAutoBinder clusterBinder;
    #endregion

    #region Events
    public event Action OnBackClicked;
    public event Action OnConnectClicked;
    public event Action OnQRClicked;
    public event Action OnSetupClicked;
    public event Action OnStartClicked;
    public event Action<ClusterAutoBinder.ConnectionState> OnConnectionStateChanged;
    #endregion

    #region Private Fields
    // UI References
    private GameObject _hostInput;
    private GameObject _portInput;
    private GameObject _monitorsDropdown;
    private GameObject _resolutionDropdown;
    private GameObject _bitrateDropdown;
    private GameObject _fpsDropdown;
    private GameObject _bodyContainer;

    // Connect button reference for dynamic text change
    private GameObject _connectButton;
    private TextMeshProUGUI _connectButtonText;
    private ClusterAutoBinder.ConnectionState _currentState = ClusterAutoBinder.ConnectionState.Disconnected;

    // QR Scanner
    private QRScannerManager _qrScannerManager;

    // Side Panels for hardware/network info
    private RTTInfoSidePanel _hardwareInfoPanel;
    private RTTInfoSidePanel _networkInfoPanel;

    // Cached suggested config for "(Recommended)" suffix
    private SuggestedStreamConfig _cachedSuggestedConfig;

    // Parent references
    private RTTMenuFrame _menuFrame;
    private float _containerWidth;
    private float _containerHeight;

    // Font sizes
    private const int LABEL_FONT_SIZE = 40;
    private const int INPUT_FONT_SIZE = 42;
    private const int DROPDOWN_LABEL_FONT_SIZE = 40;
    private const int DROPDOWN_VALUE_FONT_SIZE = 42;

    // Default dropdown options (without Recommended suffix)
    private static readonly string[] MONITOR_OPTIONS = { "1 Monitor", "2 Monitors", "3 Monitors" };
    private static readonly string[] RESOLUTION_OPTIONS = { "1280 x 720", "1366 x 768", "1600 x 900", "1920 x 1080" };
    private static readonly string[] BITRATE_OPTIONS = { "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps" };
    private static readonly string[] FPS_OPTIONS = { "30 FPS", "45 FPS", "60 FPS" };
    #endregion

    #region Public Accessors (Easy Form Data Access)
    public string Host => VRInputFieldFactory.GetValue(_hostInput);
    public string Port => VRInputFieldFactory.GetValue(_portInput);
    public int MonitorIndex => VRDropdownFactory.GetSelectedIndex(_monitorsDropdown);
    public string Resolution => VRDropdownFactory.GetSelectedValue(_resolutionDropdown);
    public string Bitrate => VRDropdownFactory.GetSelectedValue(_bitrateDropdown);
    public string FPS => VRDropdownFactory.GetSelectedValue(_fpsDropdown);
    #endregion

    #region Build UI
    /// <summary>
    /// Build the Remote Menu UI inside the given container.
    /// </summary>
    public void BuildUI(Transform parent, float containerWidth, float containerHeight)
    {
        _containerWidth = containerWidth;
        _containerHeight = containerHeight;

        // Try to find RTTMenuFrame parent
        _menuFrame = GetComponentInParent<RTTMenuFrame>();

        // Setup RectTransform
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        // Build layout
        float contentW = containerWidth * 0.875f;

        // Calculate heights from font sizes
        float inputH = VRInputFieldFactory.CalculateHeight(INPUT_FONT_SIZE, true);
        float dropdownH = VRDropdownFactory.CalculateHeight(DROPDOWN_VALUE_FONT_SIZE);

        // Layout heights
        float headerH = containerHeight * 0.1f;
        float connectButtonH = containerHeight * 0.125f;
        float gap = containerHeight * 0.04f;
        float gridH = dropdownH * 2 + gap;

        float y = containerHeight;

        // Header (Back button, Title, QR button)
        y -= headerH;
        CreateHeader(transform, 0, y, containerWidth, headerH);

        // Separator 1 position
        float sep1Y = y - gap * 1.25f;

        // Body container
        float bodyContainerHeight = containerHeight - 2f * headerH;
        y -= 2.25f * gap + bodyContainerHeight;
        _bodyContainer = CreateContainer(transform, "BodyContainer", 0, y, containerWidth, bodyContainerHeight);

        // Input Row (Host / Port)
        float bodyY = bodyContainerHeight;
        bodyY -= inputH;
        CreateInputRow(_bodyContainer.transform, 0, bodyY, contentW, inputH, gap * 1.5f);

        // Separator 2 position
        float sep2Y = y + bodyY - gap * 1.5f;

        // Grid 2x2 (Monitors, Resolution, Bitrate, FPS)
        bodyY -= gap * 2.5f + gridH;
        CreateGrid(_bodyContainer.transform, 0, bodyY, contentW, 0.04f * contentW, dropdownH, gap * 1.5f, gap);

        // Connect Button
        CreateConnectButton(_bodyContainer.transform, contentW * 0.625f, connectButtonH);

        // Configure horizontal separators if RTTMenuFrame available
        if (_menuFrame != null)
        {
            float separatorLength = contentW / containerWidth;
            ConfigureHorizontalSeparators(containerHeight, sep1Y, sep2Y, separatorLength);
        }

        Debug.Log("[RTTRemoteMenu] UI built");

        // Initialize dropdowns as disabled (show "----")
        InitializeDropdownsDisabled();

        // Create side panels (hidden by default)
        CreateSidePanels();

        // Load saved host/port from preferences
        LoadSavedHostPort();

        // Subscribe to ClusterAutoBinder events if available
        BindToClusterBinder();
    }

    /// <summary>
    /// Bind to ClusterAutoBinder events for V2 protocol.
    /// </summary>
    public void BindToClusterBinder()
    {
        if (clusterBinder == null) return;

        // Unsubscribe first in case of rebinding
        clusterBinder.OnStateChanged -= HandleConnectionStateChanged;
        clusterBinder.OnSuggestedConfigReceived -= ApplySuggestedConfig;
        clusterBinder.OnHardwareInfoReceived -= HandleHardwareInfoReceived;
        clusterBinder.OnNetworkInfoReceived -= HandleNetworkInfoReceived;

        // Subscribe
        clusterBinder.OnStateChanged += HandleConnectionStateChanged;
        clusterBinder.OnSuggestedConfigReceived += ApplySuggestedConfig;
        clusterBinder.OnHardwareInfoReceived += HandleHardwareInfoReceived;
        clusterBinder.OnNetworkInfoReceived += HandleNetworkInfoReceived;

        // Update current state
        HandleConnectionStateChanged(clusterBinder.CurrentState);

        Debug.Log("[RTTRemoteMenu] Bound to ClusterAutoBinder");
    }
    #endregion

    #region Header
    private void CreateHeader(Transform parent, float x, float y, float w, float h)
    {
        var header = CreateContainer(parent, "Header", x, y, w, h);

        // Back button
        float backW = 280f;
        CreateButton(header.transform, 0, 0, backW, h, "Back", LoadIcon("back"), themeColor,
            () => OnBackClicked?.Invoke());

        // Title - centered
        CreateLabel(header.transform, 0, 0, w, h, "Remote Desktop", 48, Color.white, true, TextAlignmentOptions.Center);

        // QR button
        float qrSize = 100f;
        CreateButton(header.transform, w - qrSize, (h - qrSize) / 2f, qrSize, qrSize, "", LoadIcon("qr"), accentColor,
            () => ShowQRScanner());
    }
    #endregion

    #region Input Row
    private void CreateInputRow(Transform parent, float x, float y, float w, float h, float gapX)
    {
        var row = CreateContainer(parent, "InputRow", x, y, w, h);

        float hostW = (w - gapX) * 0.5f;
        float portW = (w - gapX) * 0.5f;

        // Host Input
        _hostInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, hostW,
            "Host", "192.168.1.8", accentColor,
            onEndEdit: (value) => Debug.Log("Host: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont);
        PositionElement(_hostInput, 0, 0);

        // Port Input
        _portInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, portW,
            "Port", "8288", themeColor,
            onEndEdit: (value) => Debug.Log("Port: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont,
            contentType: TMP_InputField.ContentType.IntegerNumber);
        PositionElement(_portInput, hostW + gapX, 0);
    }
    #endregion

    #region Grid (Dropdowns)
    private void CreateGrid(Transform parent, float x, float y, float w, float h, float dropdownH, float gapX, float gapY)
    {
        var grid = CreateContainer(parent, "Grid", x, y, w, h);

        float cellW = (w - gapX) / 2f;

        // Dropdown options
        var monitorOptions = new List<string> { "1 Monitor", "2 Monitors", "3 Monitors" };
        var resolutionOptions = new List<string> { "1280 x 720", "1366 x 768", "1600 x 900", "1920 x 1080" };
        var bitrateOptions = new List<string> { "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps" };
        var fpsOptions = new List<string> { "30 FPS", "45 FPS", "60 FPS" };

        // Row 1 (top)
        float row1Y = dropdownH + gapY;

        _monitorsDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Monitors", LoadIcon("monitor"), themeColor,
            monitorOptions, 1,
            onValueChanged: (index, value) => Debug.Log("Monitor: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_monitorsDropdown, 0, row1Y);

        _resolutionDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Resolution", LoadIcon("resolution"), accentColor,
            resolutionOptions, 3,
            onValueChanged: (index, value) => Debug.Log("Resolution: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_resolutionDropdown, cellW + gapX, row1Y);

        // Row 2 (bottom)
        _bitrateDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Bitrate", LoadIcon("bitrate"), accentColor,
            bitrateOptions, 2,
            onValueChanged: (index, value) => Debug.Log("Bitrate: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_bitrateDropdown, 0, 0);

        _fpsDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "FPS", LoadIcon("fps"), themeColor,
            fpsOptions, 2,
            onValueChanged: (index, value) => Debug.Log("FPS: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_fpsDropdown, cellW + gapX, 0);
    }
    #endregion

    #region Connect Button
    private void CreateConnectButton(Transform parent, float w, float h)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = "CONNECT",
            themeColor = themeColor,
            width = w,
            height = h,
            fontSize = 48,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.85f,
            cornerRadius = 0.15f,
            edgePadding = 0.08f,
            enablePulse = true,
            pulseSpeed = 1.8f,
            popAmount = 0.05f,
            useConnectButtonShader = true,
            connectColorA = themeColor,
            connectColorB = new Color(0.1f, 0.5f, 0.85f),
            connectColorC = accentColor
        };

        _connectButton = VRButtonFactory.CreateButton(parent, config, OnConnectButtonClicked);

        // Find and save reference to button text
        _connectButtonText = _connectButton.GetComponentInChildren<TextMeshProUGUI>();

        RectTransform rt = _connectButton.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Handle connect button click based on current state.
    /// </summary>
    private async void OnConnectButtonClicked()
    {
        Debug.Log($"[RTTRemoteMenu] Button clicked! clusterBinder={clusterBinder != null}, useV2={clusterBinder?.useV2Protocol}, currentState={_currentState}");

        // If no binder or not using V2, use legacy event
        if (clusterBinder == null || !clusterBinder.useV2Protocol)
        {
            Debug.Log("[RTTRemoteMenu] Using legacy event (no binder or V2 disabled)");
            OnConnectClicked?.Invoke();
            return;
        }

        switch (_currentState)
        {
            case ClusterAutoBinder.ConnectionState.Disconnected:
                // Step 1: Connect
                Debug.Log("[RTTRemoteMenu] Connecting...");
                UpdateButtonText("CONNECTING...");

                // Apply form values to binder
                ApplyFormToBinder();

                bool connected = await clusterBinder.ConnectToServerAsync();
                if (!connected)
                {
                    UpdateButtonText("CONNECT");
                    Debug.LogError("[RTTRemoteMenu] Connection failed");
                }
                // State change will trigger button text update via event
                break;

            case ClusterAutoBinder.ConnectionState.Connected:
                // Step 2: Setup
                Debug.Log("[RTTRemoteMenu] State is Connected, calling SetupRemoteAsync...");
                UpdateButtonText("SETTING UP...");

                // Save user selections before sending to server
                SaveCurrentSelections();

                // Apply selected values (not suggested) to binder
                ApplyFormToBinder();

                OnSetupClicked?.Invoke();

                Debug.Log("[RTTRemoteMenu] Awaiting SetupRemoteAsync...");
                bool setup = await clusterBinder.SetupRemoteAsync();
                Debug.Log($"[RTTRemoteMenu] SetupRemoteAsync returned: {setup}");
                if (!setup)
                {
                    UpdateButtonText("SETUP REMOTE");
                    Debug.LogError("[RTTRemoteMenu] Setup failed");
                }
                break;

            case ClusterAutoBinder.ConnectionState.Ready:
                // Step 3: Start
                Debug.Log("[RTTRemoteMenu] Starting remote...");
                UpdateButtonText("STARTING...");
                OnStartClicked?.Invoke();

                bool started = await clusterBinder.StartRemoteAsync();
                if (started)
                {
                    // Hide menu after successful start
                    // This will be handled by external code via OnStartClicked event
                }
                break;

            case ClusterAutoBinder.ConnectionState.Streaming:
                // Already streaming - could offer disconnect
                Debug.Log("[RTTRemoteMenu] Already streaming");
                break;

            case ClusterAutoBinder.ConnectionState.Error:
                // Reset and try again
                clusterBinder.StopStreaming();
                UpdateButtonText("CONNECT");
                break;
        }
    }

    /// <summary>
    /// Apply form values to ClusterAutoBinder.
    /// Cleans "(Recommended)" suffix from dropdown values.
    /// </summary>
    private void ApplyFormToBinder()
    {
        if (clusterBinder == null) return;

        // Host and Port
        string host = Host;
        string port = Port;
        if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port))
        {
            clusterBinder.serverBase = $"http://{host}:{port}";
        }

        // Monitors
        clusterBinder.monitorCount = MonitorIndex + 1;

        // Resolution - clean "(Recommended)" suffix
        var resolution = RemotePreferences.CleanValue(Resolution);
        if (!string.IsNullOrEmpty(resolution))
        {
            var parts = resolution.Replace(" ", "").Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                clusterBinder.resolutionWidth = w;
                clusterBinder.resolutionHeight = h;
            }
        }

        // Bitrate - clean "(Recommended)" suffix
        var bitrate = RemotePreferences.CleanValue(Bitrate);
        if (!string.IsNullOrEmpty(bitrate))
        {
            var numStr = bitrate.Replace(" ", "").Replace("Mbps", "").Replace("mbps", "");
            if (int.TryParse(numStr, out int mbps))
            {
                clusterBinder.bitrateKbps = mbps * 1000;
            }
        }

        // FPS - clean "(Recommended)" suffix
        var fps = RemotePreferences.CleanValue(FPS);
        if (!string.IsNullOrEmpty(fps))
        {
            var numStr = fps.Replace(" ", "").Replace("FPS", "").Replace("fps", "");
            if (int.TryParse(numStr, out int fpsVal))
            {
                clusterBinder.fps = fpsVal;
            }
        }

        Debug.Log($"[RTTRemoteMenu] Applied to binder: {clusterBinder.serverBase}, {clusterBinder.monitorCount}mon, {clusterBinder.resolutionWidth}x{clusterBinder.resolutionHeight}, {clusterBinder.bitrateKbps}kbps, {clusterBinder.fps}fps");
    }

    /// <summary>
    /// Update button text.
    /// </summary>
    public void UpdateButtonText(string text)
    {
        if (_connectButtonText != null)
        {
            _connectButtonText.text = text;
        }
    }

    /// <summary>
    /// Handle connection state change from ClusterAutoBinder.
    /// </summary>
    public void HandleConnectionStateChanged(ClusterAutoBinder.ConnectionState state)
    {
        Debug.Log($"[RTTRemoteMenu] HandleConnectionStateChanged: {_currentState} -> {state}");
        _currentState = state;
        OnConnectionStateChanged?.Invoke(state);

        switch (state)
        {
            case ClusterAutoBinder.ConnectionState.Disconnected:
                UpdateButtonText("CONNECT");
                // Reset to disabled state
                InitializeDropdownsDisabled();
                HideSidePanels();
                break;

            case ClusterAutoBinder.ConnectionState.Connecting:
                UpdateButtonText("CONNECTING...");
                break;

            case ClusterAutoBinder.ConnectionState.Connected:
                UpdateButtonText("SETUP REMOTE");
                // Show side panels (data already set via events)
                ShowSidePanels();
                break;

            case ClusterAutoBinder.ConnectionState.SettingUp:
                UpdateButtonText("SETTING UP...");
                break;

            case ClusterAutoBinder.ConnectionState.Ready:
                UpdateButtonText("START REMOTE");
                break;

            case ClusterAutoBinder.ConnectionState.Streaming:
                UpdateButtonText("STREAMING");
                break;

            case ClusterAutoBinder.ConnectionState.Error:
                UpdateButtonText("RETRY");
                HideSidePanels();
                break;
        }
    }

    /// <summary>
    /// Handle hardware info received from server.
    /// </summary>
    private void HandleHardwareInfoReceived(ServerHardwareInfo info)
    {
        if (_hardwareInfoPanel != null && info != null)
        {
            _hardwareInfoPanel.SetHardwareInfo(info);
            Debug.Log($"[RTTRemoteMenu] Updated hardware info panel: {info.deviceName}");
        }
    }

    /// <summary>
    /// Handle network info received from server.
    /// </summary>
    private void HandleNetworkInfoReceived(NetworkTestResult info)
    {
        if (_networkInfoPanel != null && info != null)
        {
            _networkInfoPanel.SetNetworkInfo(info);
            Debug.Log($"[RTTRemoteMenu] Updated network info panel: {info.pingMs:F1}ms, {info.bandwidthMbps:F0}Mbps");
        }
    }

    /// <summary>
    /// Update dropdowns with suggested config from server.
    /// Adds "(Recommended)" suffix to server-suggested options.
    /// Uses saved preferences if available, otherwise uses suggested values.
    /// </summary>
    public void ApplySuggestedConfig(SuggestedStreamConfig config)
    {
        if (config == null) return;

        _cachedSuggestedConfig = config;

        // Load saved preferences
        var prefs = RemotePreferences.Load();

        // Enable all dropdowns first
        EnableDropdowns();

        // === Monitors ===
        int suggestedMonitorIndex = Mathf.Clamp(config.monitors - 1, 0, 2);
        var monitorOptions = BuildOptionsWithRecommended(MONITOR_OPTIONS, suggestedMonitorIndex);
        int selectedMonitorIndex = prefs.HasMonitorPreference ? prefs.monitors : suggestedMonitorIndex;
        selectedMonitorIndex = Mathf.Clamp(selectedMonitorIndex, 0, monitorOptions.Count - 1);
        VRDropdownFactory.SetOptions(_monitorsDropdown, monitorOptions, selectedMonitorIndex);

        // === Resolution ===
        string suggestedResolution = $"{config.resolutionWidth} x {config.resolutionHeight}";
        int suggestedResIndex = FindOptionIndex(RESOLUTION_OPTIONS, suggestedResolution);
        if (suggestedResIndex < 0) suggestedResIndex = 3; // Default to 1920x1080
        var resolutionOptions = BuildOptionsWithRecommended(RESOLUTION_OPTIONS, suggestedResIndex);
        int selectedResIndex = prefs.HasResolutionPreference
            ? FindOptionIndexClean(RESOLUTION_OPTIONS, prefs.resolution)
            : suggestedResIndex;
        if (selectedResIndex < 0) selectedResIndex = suggestedResIndex;
        VRDropdownFactory.SetOptions(_resolutionDropdown, resolutionOptions, selectedResIndex);

        // === Bitrate ===
        int bitrateMbps = config.bitrateKbps / 1000;
        string suggestedBitrate = $"{bitrateMbps} Mbps";
        int suggestedBitrateIndex = FindOptionIndex(BITRATE_OPTIONS, suggestedBitrate);
        if (suggestedBitrateIndex < 0) suggestedBitrateIndex = 2; // Default to 20 Mbps
        var bitrateOptions = BuildOptionsWithRecommended(BITRATE_OPTIONS, suggestedBitrateIndex);
        int selectedBitrateIndex = prefs.HasBitratePreference
            ? FindOptionIndexClean(BITRATE_OPTIONS, prefs.bitrate)
            : suggestedBitrateIndex;
        if (selectedBitrateIndex < 0) selectedBitrateIndex = suggestedBitrateIndex;
        VRDropdownFactory.SetOptions(_bitrateDropdown, bitrateOptions, selectedBitrateIndex);

        // === FPS ===
        string suggestedFps = $"{config.fps} FPS";
        int suggestedFpsIndex = FindOptionIndex(FPS_OPTIONS, suggestedFps);
        if (suggestedFpsIndex < 0) suggestedFpsIndex = 2; // Default to 60 FPS
        var fpsOptions = BuildOptionsWithRecommended(FPS_OPTIONS, suggestedFpsIndex);
        int selectedFpsIndex = prefs.HasFpsPreference
            ? FindOptionIndexClean(FPS_OPTIONS, prefs.fps)
            : suggestedFpsIndex;
        if (selectedFpsIndex < 0) selectedFpsIndex = suggestedFpsIndex;
        VRDropdownFactory.SetOptions(_fpsDropdown, fpsOptions, selectedFpsIndex);

        Debug.Log($"[RTTRemoteMenu] Applied suggested config with (Recommended): {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}, {config.fps}fps, {config.bitrateKbps}kbps");
    }

    /// <summary>
    /// Build options list with "(Recommended)" suffix on the specified index.
    /// </summary>
    private List<string> BuildOptionsWithRecommended(string[] baseOptions, int recommendedIndex)
    {
        var options = new List<string>();
        for (int i = 0; i < baseOptions.Length; i++)
        {
            if (i == recommendedIndex)
                options.Add(baseOptions[i] + " (Recommended)");
            else
                options.Add(baseOptions[i]);
        }
        return options;
    }

    /// <summary>
    /// Find option index, ignoring " (Recommended)" suffix.
    /// </summary>
    private int FindOptionIndexClean(string[] options, string value)
    {
        if (string.IsNullOrEmpty(value)) return -1;
        string cleanValue = RemotePreferences.CleanValue(value);
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].Replace(" ", "").Equals(cleanValue.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
    #endregion

    #region Dropdown Management
    /// <summary>
    /// Initialize dropdowns as disabled with "----" placeholder.
    /// Called on startup and when disconnected.
    /// </summary>
    private void InitializeDropdownsDisabled()
    {
        var placeholder = new List<string> { "----" };

        VRDropdownFactory.SetOptions(_monitorsDropdown, placeholder, 0);
        VRDropdownFactory.SetOptions(_resolutionDropdown, placeholder, 0);
        VRDropdownFactory.SetOptions(_bitrateDropdown, placeholder, 0);
        VRDropdownFactory.SetOptions(_fpsDropdown, placeholder, 0);

        VRDropdownFactory.SetInteractable(_monitorsDropdown, false);
        VRDropdownFactory.SetInteractable(_resolutionDropdown, false);
        VRDropdownFactory.SetInteractable(_bitrateDropdown, false);
        VRDropdownFactory.SetInteractable(_fpsDropdown, false);
    }

    /// <summary>
    /// Enable all dropdowns for interaction.
    /// </summary>
    private void EnableDropdowns()
    {
        VRDropdownFactory.SetInteractable(_monitorsDropdown, true);
        VRDropdownFactory.SetInteractable(_resolutionDropdown, true);
        VRDropdownFactory.SetInteractable(_bitrateDropdown, true);
        VRDropdownFactory.SetInteractable(_fpsDropdown, true);
    }

    /// <summary>
    /// Load saved host/port from preferences and apply to inputs.
    /// </summary>
    private void LoadSavedHostPort()
    {
        var prefs = RemotePreferences.Load();
        if (!string.IsNullOrEmpty(prefs.lastHost))
            VRInputFieldFactory.SetValue(_hostInput, prefs.lastHost);
        if (!string.IsNullOrEmpty(prefs.lastPort))
            VRInputFieldFactory.SetValue(_portInput, prefs.lastPort);
    }

    /// <summary>
    /// Save current dropdown selections and host/port to preferences.
    /// Called when SETUP REMOTE is clicked.
    /// </summary>
    private void SaveCurrentSelections()
    {
        var prefs = new RemotePreferences
        {
            monitors = MonitorIndex,
            resolution = RemotePreferences.CleanValue(Resolution),
            bitrate = RemotePreferences.CleanValue(Bitrate),
            fps = RemotePreferences.CleanValue(FPS),
            lastHost = Host,
            lastPort = Port
        };
        prefs.Save();
        Debug.Log($"[RTTRemoteMenu] Saved preferences: {prefs.monitors}mon, {prefs.resolution}, {prefs.bitrate}, {prefs.fps}");
    }
    #endregion

    #region Side Panels
    /// <summary>
    /// Create side panels for hardware and network info.
    /// Panels are hidden by default and shown when connected.
    /// Uses arc positioning like WorldPanelClusterRig for proper placement.
    /// </summary>
    private void CreateSidePanels()
    {
        if (_menuFrame == null) return;

        float mainPanelWidth = _menuFrame.PanelWidth;   // ~1.6m
        float mainPanelHeight = _menuFrame.PanelHeight; // ~0.9m
        float sideWidth = mainPanelWidth / 3f;          // ~0.53m
        float sideHeight = mainPanelHeight;             // Same height as main panel

        // Gap between main panel and side panels (in meters)
        float gapMeters = 0.05f;

        // Angle to rotate side panels (facing slightly toward viewer)
        float rotationAngle = 30f;

        // Hardware Info Panel (Left)
        PlaceSidePanelFlat("HardwareInfoPanel", -1, mainPanelWidth, sideWidth, sideHeight,
            gapMeters, rotationAngle, RTTInfoSidePanel.PanelType.HardwareInfo, ref _hardwareInfoPanel);

        // Network Info Panel (Right)
        PlaceSidePanelFlat("NetworkInfoPanel", 1, mainPanelWidth, sideWidth, sideHeight,
            gapMeters, rotationAngle, RTTInfoSidePanel.PanelType.NetworkInfo, ref _networkInfoPanel);

        Debug.Log($"[RTTRemoteMenu] Side panels created ({sideWidth:F2}m x {sideHeight:F2}m) gap={gapMeters}m angle={rotationAngle}° (hidden)");
    }

    /// <summary>
    /// Place a side panel adjacent to the main panel with inner edge at same Z.
    /// </summary>
    /// <param name="side">-1 for left, +1 for right</param>
    private void PlaceSidePanelFlat(string name, int side, float mainWidth, float sideWidth, float sideHeight,
        float gap, float rotationAngle, RTTInfoSidePanel.PanelType type, ref RTTInfoSidePanel panelRef)
    {
        GameObject panelObj = new GameObject(name);

        // Get main panel's world transform
        Vector3 mainPos = _menuFrame.transform.position;
        Quaternion mainRot = _menuFrame.transform.rotation;
        Vector3 mainRight = _menuFrame.transform.right;
        Vector3 mainForward = _menuFrame.transform.forward;

        // Side panel is rotated, so we need to calculate where its inner edge should be
        // Inner edge of side panel should be at: mainPanel edge + gap
        // Side panel center offset from its inner edge depends on rotation

        float rotRad = rotationAngle * Mathf.Deg2Rad;

        // When panel is rotated by angle toward viewer, its center is offset from inner edge by:
        // X offset: (sideWidth/2) * cos(angle)
        // Z offset: -(sideWidth/2) * sin(angle)  (panel rotates toward viewer, center moves forward)

        float halfSide = sideWidth / 2f;
        float centerOffsetX = halfSide * Mathf.Cos(rotRad);
        float centerOffsetZ = -halfSide * Mathf.Sin(rotRad); // Negative: center moves toward viewer

        // Total X offset from main panel center:
        // = half main width + gap + center offset from inner edge
        float totalX = (mainWidth / 2f) + gap + centerOffsetX;

        // Z offset: panel rotates toward viewer, so center moves forward
        float totalZ = centerOffsetZ;

        // Calculate world position
        Vector3 offset = mainRight * (side * totalX) + mainForward * totalZ;
        panelObj.transform.position = mainPos + offset;

        // Rotate panel: left panel rotates negative (faces right), right panel rotates positive (faces left)
        panelObj.transform.rotation = mainRot * Quaternion.Euler(0, side * rotationAngle, 0);

        // Ensure scale is 1:1:1
        panelObj.transform.localScale = Vector3.one;

        // Parent to menuFrame for organization
        panelObj.transform.SetParent(_menuFrame.transform, true);

        // Create and initialize the side panel component
        var panel = panelObj.AddComponent<RTTInfoSidePanel>();
        panel.Initialize(type, themeColor, accentColor, customFont, sideWidth, sideHeight);
        panelRef = panel;
    }

    /// <summary>
    /// Show side panels.
    /// </summary>
    private void ShowSidePanels()
    {
        if (_hardwareInfoPanel != null)
            _hardwareInfoPanel.gameObject.SetActive(true);
        if (_networkInfoPanel != null)
            _networkInfoPanel.gameObject.SetActive(true);

        Debug.Log("[RTTRemoteMenu] Side panels shown");
    }

    /// <summary>
    /// Hide side panels.
    /// </summary>
    private void HideSidePanels()
    {
        if (_hardwareInfoPanel != null)
            _hardwareInfoPanel.gameObject.SetActive(false);
        if (_networkInfoPanel != null)
            _networkInfoPanel.gameObject.SetActive(false);

        Debug.Log("[RTTRemoteMenu] Side panels hidden");
    }
    #endregion

    #region UI Helpers
    private GameObject CreateContainer(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(-w / 2f, y);
        rt.sizeDelta = new Vector2(w, h);
        return go;
    }

    private void PositionElement(GameObject element, float x, float y)
    {
        RectTransform rt = element.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
    }

    private void CreateButton(Transform parent, float x, float y, float w, float h,
        string text, Sprite icon, Color col, UnityEngine.Events.UnityAction onClick)
    {
        bool hasText = !string.IsNullOrEmpty(text);
        bool hasIcon = icon != null;

        var config = new VRButtonFactory.ButtonConfig
        {
            label = hasText ? text : (icon != null ? icon.name : "Button"),
            icon = icon,
            themeColor = col,
            width = w,
            height = h,
            fontSize = 40,
            font = customFont,
            iconOnly = hasIcon && !hasText,
            textOnly = hasText && !hasIcon,
            horizontalLayout = hasIcon && hasText,
            iconSize = hasIcon && !hasText ? 44f : 44f,
            iconPadding = hasIcon && hasText ? 64f : 28f,
            backgroundAlpha = 0.08f,
            borderWidth = 0.04f,
            popAmount = 0.0125f
        };

        var btn = VRButtonFactory.CreateButton(parent, config, onClick);

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
    }

    private GameObject CreateLabel(Transform parent, float x, float y, float w, float h,
        string text, int fontSize, Color col, bool bold,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.color = col;
        txt.alignment = align;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont) txt.font = customFont;

        return go;
    }

    private Sprite LoadIcon(string name)
    {
        Sprite icon = Resources.Load<Sprite>($"icon_{name}");
        if (icon == null) icon = Resources.Load<Sprite>($"Icons/{name}");
        if (icon == null) icon = Resources.Load<Sprite>(name);
        return icon;
    }
    #endregion

    #region Horizontal Separators
    private void ConfigureHorizontalSeparators(float containerHeight, float sep1Y, float sep2Y, float sep2Length)
    {
        if (_menuFrame == null) return;

        float frameLogicalHeight = _menuFrame.LogicalWidthValue * (_menuFrame.PanelHeight / _menuFrame.PanelWidth);
        float marginBottom = 50f; // Default content margin
        float marginTop = 50f;
        float contentHeight = frameLogicalHeight - marginTop - marginBottom;

        // Calculate UV positions
        float sep1FrameY = marginBottom + sep1Y;
        float sep2FrameY = marginBottom + sep2Y;

        float sep1UV = sep1FrameY / frameLogicalHeight;
        float sep2UV = sep2FrameY / frameLogicalHeight;

        // Set horizontal separators
        Vector4 positions = new Vector4(sep1UV, sep2UV, 0, 0);
        Vector4 lengths = new Vector4(1f, sep2Length, 1f, 1f);
        _menuFrame.SetHorizontalSeparators(2, positions, 0.003f, 0.012f, 0.9f, lengths);
    }

    private void DisableHorizontalSeparators()
    {
        if (_menuFrame == null) return;
        _menuFrame.SetHorizontalSeparators(0, Vector4.zero);
    }
    #endregion

    #region QR Scanner
    /// <summary>
    /// Show QR Scanner view.
    /// </summary>
    public void ShowQRScanner()
    {
        OnQRClicked?.Invoke();

        // If scanner already active, close it
        if (_qrScannerManager != null)
        {
            CloseQRScanner();
            return;
        }

        // Find RTTTaskbar
        RTTTaskbar taskbar = FindObjectOfType<RTTTaskbar>();

        // Create QRScannerManager
        GameObject managerObj = new GameObject("QRScannerManager");
        _qrScannerManager = managerObj.AddComponent<QRScannerManager>();
        _qrScannerManager.themeColor = themeColor;
        _qrScannerManager.accentColor = accentColor;
        _qrScannerManager.customFont = customFont;

        // Subscribe events
        _qrScannerManager.OnQRScanned += OnQRCodeScanned;
        _qrScannerManager.OnCancelled += OnQRScanCancelled;

        // Start scanning with RTTMenuFrame
        if (_menuFrame != null)
        {
            _qrScannerManager.StartScanning(_menuFrame, taskbar);
        }
    }

    private void CloseQRScanner()
    {
        if (_qrScannerManager != null)
        {
            _qrScannerManager.StopScanning(); // This also calls ShowOriginalUI()
            Destroy(_qrScannerManager.gameObject);
            _qrScannerManager = null;
        }
    }

    private void OnQRScanCancelled()
    {
        _qrScannerManager = null;
    }

    private void OnQRCodeScanned(QRScannerConfig config)
    {
        Debug.Log($"[RTTRemoteMenu] QR Scanned: {config}");
        _qrScannerManager = null;
        SetConfig(config);
    }
    #endregion

    #region Set Config (Fill Form)
    /// <summary>
    /// Fill the form with values from a QRScannerConfig.
    /// </summary>
    public void SetConfig(QRScannerConfig config)
    {
        if (config == null) return;

        // Host & Port
        if (!string.IsNullOrEmpty(config.host))
            VRInputFieldFactory.SetValue(_hostInput, config.host);

        if (!string.IsNullOrEmpty(config.port))
            VRInputFieldFactory.SetValue(_portInput, config.port);

        // Resolution
        if (!string.IsNullOrEmpty(config.resolution))
        {
            int index = FindOptionIndex(
                new[] { "1920 x 1080", "1600 x 900", "1366 x 768", "1280 x 720" },
                config.resolution);
            if (index >= 0)
                VRDropdownFactory.SetSelectedIndex(_resolutionDropdown, index);
        }

        // Bitrate
        if (!string.IsNullOrEmpty(config.bitrate))
        {
            int index = FindOptionIndex(
                new[] { "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps" },
                config.bitrate);
            if (index >= 0)
                VRDropdownFactory.SetSelectedIndex(_bitrateDropdown, index);
        }

        // FPS
        if (!string.IsNullOrEmpty(config.fps))
        {
            int index = FindOptionIndex(
                new[] { "30 FPS", "45 FPS", "60 FPS" },
                config.fps);
            if (index >= 0)
                VRDropdownFactory.SetSelectedIndex(_fpsDropdown, index);
        }

        // Monitors
        if (config.monitors >= 1 && config.monitors <= 3)
        {
            VRDropdownFactory.SetSelectedIndex(_monitorsDropdown, config.monitors - 1);
        }
    }

    private int FindOptionIndex(string[] options, string value)
    {
        string normalized = value.Replace("x", " x ").Replace("  ", " ").Trim();
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                options[i].Replace(" ", "").Equals(value.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        DisableHorizontalSeparators();

        if (_qrScannerManager != null)
        {
            _qrScannerManager.OnQRScanned -= OnQRCodeScanned;
            _qrScannerManager.OnCancelled -= OnQRScanCancelled;
        }

        // Unsubscribe from ClusterAutoBinder events
        if (clusterBinder != null)
        {
            clusterBinder.OnStateChanged -= HandleConnectionStateChanged;
            clusterBinder.OnSuggestedConfigReceived -= ApplySuggestedConfig;
            clusterBinder.OnHardwareInfoReceived -= HandleHardwareInfoReceived;
            clusterBinder.OnNetworkInfoReceived -= HandleNetworkInfoReceived;
        }

        // Destroy side panels
        if (_hardwareInfoPanel != null)
            Destroy(_hardwareInfoPanel.gameObject);
        if (_networkInfoPanel != null)
            Destroy(_networkInfoPanel.gameObject);
    }
    #endregion
}
