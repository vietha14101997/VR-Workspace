using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Core;
using VRWorkspace.Streaming;
using VRWorkspace.ViewModels;

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

    #endregion

    #region Events
    public event Action OnBackClicked;
    public event Action OnConnectClicked;
    public event Action OnQRClicked;
    public event Action OnSetupClicked;
    public event Action OnStartClicked;
    public event Action<ConnectionPhase> OnConnectionStateChanged;
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
    private ConnectionPhase _currentPhase = ConnectionPhase.Disconnected;

    // ViewModel reference (via ServiceLocator or created locally)
    private ConnectionViewModel _viewModel;

    // QR button reference for locking
    private GameObject _qrButton;

    // QR Scanner
    private QRScannerManager _qrScannerManager;

    // Side Panels for hardware/network info
    private RTTInfoSidePanel _hardwareInfoPanel;
    private RTTInfoSidePanel _networkInfoPanel;
    private RTTMenuFrame _hardwareFrame;
    private RTTMenuFrame _networkFrame;

    // Cached suggested config for "(Recommended)" suffix
    private SuggestedStreamConfig _cachedSuggestedConfig;

    // Cached hardware and network info for recalculating suggestions
    private ServerHardwareInfo _cachedHardwareInfo;
    private NetworkTestResult _cachedNetworkInfo;

    // Speed test - only store final result
    private double _lastReportedMbps = -1;

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
    private static readonly string[] BITRATE_OPTIONS = { "5 Mbps", "10 Mbps", "15 Mbps", "20 Mbps", "30 Mbps" };
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

        // Bind to ViewModel for MVVM-based state management
        BindToViewModel();
    }

    /// <summary>
    /// Bind to ConnectionViewModel for MVVM-based state management.
    /// All events fire on main thread via ObservableProperty.
    /// </summary>
    public void BindToViewModel()
    {
        // Get or create ViewModel via ServiceLocator
        if (!ServiceLocator.TryGet<ConnectionViewModel>(out _viewModel))
        {
            _viewModel = new ConnectionViewModel();
            ServiceLocator.Register(_viewModel);
        }

        // Subscribe to observable properties (auto main thread marshalling)
        _viewModel.Phase.OnChanged += HandlePhaseChanged;
        _viewModel.HardwareInfo.OnChanged += HandleHardwareInfoReceived;
        _viewModel.NetworkInfo.OnChanged += HandleNetworkInfoReceived;
        _viewModel.SuggestedConfig.OnChanged += ApplySuggestedConfig;
        _viewModel.SpeedTestProgress.OnChanged += HandleSpeedTestProgressValue;
        _viewModel.CurrentBandwidth.OnChanged += HandleBandwidthChanged;
        _viewModel.ErrorMessage.OnChanged += HandleErrorMessage;

        // Update UI with current state
        HandlePhaseChanged(_viewModel.Phase.Value);

        Debug.Log("[RTTRemoteMenu] Bound to ConnectionViewModel");
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
        _qrButton = CreateButton(header.transform, w - qrSize, (h - qrSize) / 2f, qrSize, qrSize, "", LoadIcon("qr"), accentColor,
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
            "Host", "192.168.1.7", accentColor,
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
            onValueChanged: HandleMonitorSelectionChanged,
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
    /// Handle connect button click based on current phase.
    /// Uses ConnectionViewModel for state management.
    /// </summary>
    private async void OnConnectButtonClicked()
    {
        Debug.Log($"[RTTRemoteMenu] Button clicked! viewModel={_viewModel != null}, currentPhase={_currentPhase}");

        // If no ViewModel, use legacy event
        if (_viewModel == null)
        {
            Debug.Log("[RTTRemoteMenu] Using legacy event (no ViewModel)");
            OnConnectClicked?.Invoke();
            return;
        }

        switch (_currentPhase)
        {
            case ConnectionPhase.Disconnected:
            case ConnectionPhase.Error:
                // Validate host and port first
                string host = Host;
                string port = Port;

                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
                {
                    Debug.LogWarning("[RTTRemoteMenu] Host or port is empty");
                    return;
                }

                if (!int.TryParse(port, out int portNum) || portNum <= 0 || portNum > 65535)
                {
                    Debug.LogWarning($"[RTTRemoteMenu] Invalid port: {port}");
                    return;
                }

                // Validate host format (IP address or hostname)
                if (!IsValidHost(host))
                {
                    Debug.LogWarning($"[RTTRemoteMenu] Invalid host format: {host}");
                    return;
                }

                // Step 1: Connect
                Debug.Log("[RTTRemoteMenu] Connecting...");
                UpdateButtonText("CONNECTING...");

                // Lock inputs during connection
                VRInputFieldFactory.SetInteractable(_hostInput, false);
                VRInputFieldFactory.SetInteractable(_portInput, false);
                VRButtonFactory.SetInteractable(_qrButton, false);

                await _viewModel.ConnectAsync(host, portNum);
                break;

            case ConnectionPhase.ConfiguringSettings:
                // NEW FLOW: Start button clicked - create ClusterRig and show progress
                Debug.Log("[RTTRemoteMenu] Starting with progress UI...");
                UpdateButtonText("STARTING...");

                // Save user selections
                SaveCurrentSelections();

                // Hide menu immediately
                HideMenu();

                // Build config from form dropdowns and start new flow
                var config = BuildConfigFromForm();
                if (config != null)
                {
                    OnSetupClicked?.Invoke();
                    // Use new flow that creates ClusterRig and shows progress
                    await _viewModel.StartWithProgressAsync(config);
                }
                break;

            case ConnectionPhase.ReadyToStream:
                // This case is no longer used - auto-start handles streaming
                // Kept for backward compatibility
                Debug.Log("[RTTRemoteMenu] ReadyToStream - auto-start handles this now");
                break;

            case ConnectionPhase.Streaming:
                // Already streaming - could offer disconnect
                Debug.Log("[RTTRemoteMenu] Already streaming");
                break;
        }
    }

    /// <summary>
    /// Build StreamingConfig from form dropdown values.
    /// Returns config with user-selected values, falling back to suggested values if needed.
    /// </summary>
    private StreamingConfig BuildConfigFromForm()
    {
        if (_viewModel == null || _viewModel.SuggestedConfig.Value == null) return null;

        // Get current suggested config as fallback
        var suggested = _viewModel.SuggestedConfig.Value;

        // Parse Resolution from dropdown
        var resolution = RemotePreferences.CleanValue(Resolution);
        int resW = suggested.resolutionWidth;
        int resH = suggested.resolutionHeight;
        if (!string.IsNullOrEmpty(resolution))
        {
            var parts = resolution.Replace(" ", "").Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                resW = w;
                resH = h;
            }
        }

        // Parse Bitrate from dropdown
        int bitrateKbps = suggested.bitrateKbps;
        var bitrate = RemotePreferences.CleanValue(Bitrate);
        if (!string.IsNullOrEmpty(bitrate))
        {
            var numStr = bitrate.Replace(" ", "").Replace("Mbps", "").Replace("mbps", "");
            if (int.TryParse(numStr, out int mbps))
            {
                bitrateKbps = mbps * 1000;
            }
        }

        // Parse FPS
        int fpsVal = suggested.fps;
        var fps = RemotePreferences.CleanValue(FPS);
        if (!string.IsNullOrEmpty(fps))
        {
            var numStr = fps.Replace(" ", "").Replace("FPS", "").Replace("fps", "");
            if (int.TryParse(numStr, out int f))
            {
                fpsVal = f;
            }
        }

        // Create config from user selections
        var config = new StreamingConfig
        {
            monitors = MonitorIndex + 1,
            resolutionWidth = resW,
            resolutionHeight = resH,
            bitrateKbps = bitrateKbps,
            fps = fpsVal,
            refreshRate = suggested.refreshRate,
            selectedCodec = suggested.selectedCodec
        };

        Debug.Log($"[RTTRemoteMenu] BuildConfigFromForm: {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}, {config.fps}fps, {config.bitrateKbps}kbps");
        return config;
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
    /// Hide the menu frame and side panels.
    /// Called when START is clicked in new flow.
    /// </summary>
    public void HideMenu()
    {
        if (_menuFrame != null)
        {
            _menuFrame.gameObject.SetActive(false);
        }
        HideSidePanels();
        Debug.Log("[RTTRemoteMenu] Menu hidden");
    }

    /// <summary>
    /// Handle phase change from ConnectionViewModel.
    /// All calls are guaranteed to be on main thread via ObservableProperty.
    /// </summary>
    private void HandlePhaseChanged(ConnectionPhase phase)
    {
        Debug.Log($"[RTTRemoteMenu] HandlePhaseChanged: {_currentPhase} -> {phase}");
        _currentPhase = phase;
        OnConnectionStateChanged?.Invoke(phase);

        switch (phase)
        {
            case ConnectionPhase.Disconnected:
                UpdateButtonText("CONNECT");
                InitializeDropdownsDisabled();
                VRInputFieldFactory.SetInteractable(_hostInput, true);
                VRInputFieldFactory.SetInteractable(_portInput, true);
                VRButtonFactory.SetInteractable(_qrButton, true);
                HideSidePanels();
                // Clear cached data to prevent stale data on next connection
                _cachedHardwareInfo = null;
                _cachedNetworkInfo = null;
                _cachedSuggestedConfig = null;
                break;

            case ConnectionPhase.Connecting:
                UpdateButtonText("CONNECTING...");
                // Side panels will show when hardware_info is received via HandleHardwareInfoReceived
                break;

            case ConnectionPhase.AwaitingHardwareInfo:
            case ConnectionPhase.SpeedTesting:
            case ConnectionPhase.AwaitingNetworkInfo:
            case ConnectionPhase.AwaitingSuggestedConfig:
                UpdateButtonText("CONNECTING...");
                break;

            case ConnectionPhase.ConfiguringSettings:
                UpdateButtonText("START");
                break;

            case ConnectionPhase.SendingDisplayConfig:
            case ConnectionPhase.AwaitingSetupComplete:
                UpdateButtonText("SETTING UP...");
                LockAllInputs();
                break;

            case ConnectionPhase.ICENegotiating:
                UpdateButtonText("NEGOTIATING...");
                break;

            case ConnectionPhase.ReadyToStream:
                UpdateButtonText("START REMOTE");
                break;

            case ConnectionPhase.StartingStream:
                UpdateButtonText("STARTING...");
                break;

            case ConnectionPhase.Streaming:
                UpdateButtonText("STREAMING");
                break;

            case ConnectionPhase.Reconnecting:
                UpdateButtonText("RECONNECTING...");
                break;

            case ConnectionPhase.Error:
                // Treat Error same as Disconnected - show CONNECT, not RETRY
                UpdateButtonText("CONNECT");
                InitializeDropdownsDisabled();
                VRInputFieldFactory.SetInteractable(_hostInput, true);
                VRInputFieldFactory.SetInteractable(_portInput, true);
                VRButtonFactory.SetInteractable(_qrButton, true);
                HideSidePanels();
                // Clear cached data on error
                _cachedHardwareInfo = null;
                _cachedNetworkInfo = null;
                _cachedSuggestedConfig = null;
                break;
        }
    }

    /// <summary>
    /// Validate host format (IP address or hostname).
    /// </summary>
    private bool IsValidHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        // Check for valid IP address
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return true;
        }

        // Check for valid hostname (alphanumeric, dots, hyphens only)
        // Must not start or end with dot/hyphen
        if (host.StartsWith(".") || host.StartsWith("-") ||
            host.EndsWith(".") || host.EndsWith("-"))
        {
            return false;
        }

        foreach (char c in host)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Handle error message from ViewModel.
    /// </summary>
    private void HandleErrorMessage(string error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"[RTTRemoteMenu] Error: {error}");
            // Could show error UI here
        }
    }

    /// <summary>
    /// Handle speed test progress value change.
    /// </summary>
    private void HandleSpeedTestProgressValue(int progress)
    {
        if (_networkInfoPanel == null) return;

        if (progress >= 100)
        {
            double bandwidth = _viewModel?.CurrentBandwidth.Value ?? 0;
            _networkInfoPanel.UpdateSpeedTestProgress("bandwidth", bandwidth, progress);
        }
    }

    /// <summary>
    /// Handle bandwidth change.
    /// </summary>
    private void HandleBandwidthChanged(double mbps)
    {
        // Update is handled in HandleSpeedTestProgressValue
        _lastReportedMbps = mbps;
    }

    /// <summary>
    /// Handle hardware info received from server.
    /// </summary>
    private void HandleHardwareInfoReceived(ServerHardwareInfo info)
    {
        Debug.Log($"[RTTRemoteMenu] HandleHardwareInfoReceived called, info null: {info == null}, panel null: {_hardwareInfoPanel == null}");

        // Cache for later recalculation
        _cachedHardwareInfo = info;

        // Show both panels when first info arrives (network panel with loading state)
        EnsureBothPanelsVisible();

        if (_hardwareInfoPanel != null && info != null)
        {
            _hardwareInfoPanel.SetHardwareInfo(info);
            Debug.Log($"[RTTRemoteMenu] Updated hardware info panel: {info.deviceName}");
        }
        else
        {
            Debug.LogWarning($"[RTTRemoteMenu] HandleHardwareInfoReceived skipped: panel={_hardwareInfoPanel != null}, info={info != null}");
        }
    }

    /// <summary>
    /// Handle network info received from server.
    /// </summary>
    private void HandleNetworkInfoReceived(NetworkTestResult info)
    {
        Debug.Log($"[RTTRemoteMenu] HandleNetworkInfoReceived called, info null: {info == null}, panel null: {_networkInfoPanel == null}");

        // Cache for later recalculation
        _cachedNetworkInfo = info;

        // Show both panels when first info arrives (hardware panel with loading state)
        EnsureBothPanelsVisible();

        if (_networkInfoPanel != null && info != null)
        {
            _networkInfoPanel.SetNetworkInfo(info);
            Debug.Log($"[RTTRemoteMenu] Updated network info panel: {info.pingMs:F1}ms, {info.bandwidthMbps:F0}Mbps");
        }
        else
        {
            Debug.LogWarning($"[RTTRemoteMenu] HandleNetworkInfoReceived skipped: panel={_networkInfoPanel != null}, info={info != null}");
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

        // Enable all dropdowns first
        EnableDropdowns();

        // === Monitors ===
        // Server suggests based on bandwidth calculation
        int suggestedMonitorIndex = Mathf.Clamp(config.monitors - 1, 0, 2);
        var monitorOptions = BuildOptionsWithRecommended(MONITOR_OPTIONS, suggestedMonitorIndex);
        VRDropdownFactory.SetOptions(_monitorsDropdown, monitorOptions, suggestedMonitorIndex);

        // === Resolution ===
        string suggestedResolution = $"{config.resolutionWidth} x {config.resolutionHeight}";
        int suggestedResIndex = FindOptionIndex(RESOLUTION_OPTIONS, suggestedResolution);
        if (suggestedResIndex < 0)
        {
            Debug.LogWarning($"[RTTRemoteMenu] Resolution '{suggestedResolution}' not found, defaulting to 1920x1080");
            suggestedResIndex = 3; // Default to 1920x1080
        }
        var resolutionOptions = BuildOptionsWithRecommended(RESOLUTION_OPTIONS, suggestedResIndex);
        VRDropdownFactory.SetOptions(_resolutionDropdown, resolutionOptions, suggestedResIndex);

        // === Bitrate ===
        int bitrateMbps = config.bitrateKbps / 1000;
        string suggestedBitrate = $"{bitrateMbps} Mbps";
        int suggestedBitrateIndex = FindOptionIndex(BITRATE_OPTIONS, suggestedBitrate);
        if (suggestedBitrateIndex < 0)
        {
            // Find nearest option: 5, 10, 15, 20, 30 Mbps
            if (bitrateMbps >= 25) suggestedBitrateIndex = 4;      // 30 Mbps
            else if (bitrateMbps >= 17) suggestedBitrateIndex = 3; // 20 Mbps
            else if (bitrateMbps >= 12) suggestedBitrateIndex = 2; // 15 Mbps
            else if (bitrateMbps >= 7) suggestedBitrateIndex = 1;  // 10 Mbps
            else suggestedBitrateIndex = 0;                        // 5 Mbps
            Debug.LogWarning($"[RTTRemoteMenu] Bitrate '{suggestedBitrate}' not found, using nearest: {BITRATE_OPTIONS[suggestedBitrateIndex]}");
        }
        var bitrateOptions = BuildOptionsWithRecommended(BITRATE_OPTIONS, suggestedBitrateIndex);
        VRDropdownFactory.SetOptions(_bitrateDropdown, bitrateOptions, suggestedBitrateIndex);

        // === FPS ===
        string suggestedFps = $"{config.fps} FPS";
        int suggestedFpsIndex = FindOptionIndex(FPS_OPTIONS, suggestedFps);
        if (suggestedFpsIndex < 0)
        {
            Debug.LogWarning($"[RTTRemoteMenu] FPS '{suggestedFps}' not found, defaulting to 60 FPS");
            suggestedFpsIndex = 2; // Default to 60 FPS
        }
        var fpsOptions = BuildOptionsWithRecommended(FPS_OPTIONS, suggestedFpsIndex);
        VRDropdownFactory.SetOptions(_fpsDropdown, fpsOptions, suggestedFpsIndex);

        Debug.Log($"[RTTRemoteMenu] Applied suggested config: {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}, {config.fps}fps, {config.bitrateKbps}kbps");
        Debug.Log($"[RTTRemoteMenu] Selected indices: Mon={suggestedMonitorIndex}, Res={suggestedResIndex}, Bitrate={suggestedBitrateIndex}, FPS={suggestedFpsIndex}");
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

    /// <summary>
    /// Handle Monitors dropdown selection changed.
    /// Recalculates suggested config for Resolution, Bitrate, FPS based on new monitor count.
    /// </summary>
    private void HandleMonitorSelectionChanged(int index, string value)
    {
        Debug.Log($"[RTTRemoteMenu] Monitor selection changed: index={index}, value={value}");

        // Only recalculate if we have cached info
        if (_cachedNetworkInfo == null || _cachedHardwareInfo == null)
        {
            Debug.Log("[RTTRemoteMenu] No cached info, skipping recalculation");
            return;
        }

        int selectedMonitors = index + 1; // 1, 2, or 3 monitors
        RecalculateSuggestionsForMonitorCount(selectedMonitors);
    }

    /// <summary>
    /// Recalculate suggested config based on selected monitor count.
    /// Updates Resolution, Bitrate, FPS dropdowns with new "(Recommended)" positions.
    /// </summary>
    private void RecalculateSuggestionsForMonitorCount(int monitorCount)
    {
        if (_cachedNetworkInfo == null || _cachedHardwareInfo == null) return;

        // Get current selections before updating options
        int currentResIndex = VRDropdownFactory.GetSelectedIndex(_resolutionDropdown);
        int currentBitrateIndex = VRDropdownFactory.GetSelectedIndex(_bitrateDropdown);
        int currentFpsIndex = VRDropdownFactory.GetSelectedIndex(_fpsDropdown);

        // === Calculate recommended Bitrate based on resolution and network ===
        double availableBandwidth = _cachedNetworkInfo.bandwidthMbps > 0 ? _cachedNetworkInfo.bandwidthMbps : 100;

        // Base bitrate recommendation based on resolution (same logic as server)
        int baseBitrateKbps;
        if (_cachedHardwareInfo.gpuVramGB >= 8) // 1080p
            baseBitrateKbps = 15000;
        else if (_cachedHardwareInfo.gpuVramGB >= 4) // 900p
            baseBitrateKbps = 12000;
        else // 768p
            baseBitrateKbps = 10000;

        // Scale up for excellent network (low ping, high bandwidth)
        if (_cachedNetworkInfo.pingMs < 10 && availableBandwidth > 500)
            baseBitrateKbps = (int)(baseBitrateKbps * 1.3f);
        else if (_cachedNetworkInfo.pingMs < 20 && availableBandwidth > 200)
            baseBitrateKbps = (int)(baseBitrateKbps * 1.15f);

        // Max bitrate per monitor based on bandwidth
        double maxBitratePerMonitor = availableBandwidth * 0.6 / monitorCount * 1000;
        int suggestedBitrateKbps = (int)Math.Clamp(Math.Min(baseBitrateKbps, maxBitratePerMonitor), 5000, 30000);

        // Map to bitrate option index: 5, 10, 15, 20, 30 Mbps
        int suggestedBitrateIndex;
        if (suggestedBitrateKbps >= 25000) suggestedBitrateIndex = 4; // 30 Mbps
        else if (suggestedBitrateKbps >= 17500) suggestedBitrateIndex = 3; // 20 Mbps
        else if (suggestedBitrateKbps >= 12500) suggestedBitrateIndex = 2; // 15 Mbps
        else if (suggestedBitrateKbps >= 7500) suggestedBitrateIndex = 1; // 10 Mbps
        else suggestedBitrateIndex = 0; // 5 Mbps

        // === Calculate recommended Resolution based on GPU VRAM and monitor count ===
        // More monitors = potentially lower resolution to reduce GPU load
        int suggestedResIndex;
        if (_cachedHardwareInfo.gpuVramGB >= 8 && monitorCount <= 2)
        {
            suggestedResIndex = 3; // 1920x1080
        }
        else if (_cachedHardwareInfo.gpuVramGB >= 6 || (_cachedHardwareInfo.gpuVramGB >= 4 && monitorCount == 1))
        {
            suggestedResIndex = 2; // 1600x900
        }
        else if (_cachedHardwareInfo.gpuVramGB >= 4 || monitorCount == 1)
        {
            suggestedResIndex = 1; // 1366x768
        }
        else
        {
            suggestedResIndex = 0; // 1280x720
        }

        // === Calculate recommended FPS based on encoder and ping ===
        int suggestedFpsIndex;
        if (_cachedHardwareInfo.hwAccelEnabled && _cachedNetworkInfo.pingMs < 20)
        {
            suggestedFpsIndex = 2; // 60 FPS
        }
        else if (_cachedHardwareInfo.hwAccelEnabled && _cachedNetworkInfo.pingMs < 50)
        {
            suggestedFpsIndex = 1; // 45 FPS
        }
        else
        {
            suggestedFpsIndex = 0; // 30 FPS
        }

        // === Update dropdowns with new "(Recommended)" values ===
        // AUTO-SELECT recommended values when Monitor count changes

        // Resolution - auto-select recommended
        var resolutionOptions = BuildOptionsWithRecommended(RESOLUTION_OPTIONS, suggestedResIndex);
        VRDropdownFactory.SetOptions(_resolutionDropdown, resolutionOptions, suggestedResIndex);

        // Bitrate - auto-select recommended
        var bitrateOptions = BuildOptionsWithRecommended(BITRATE_OPTIONS, suggestedBitrateIndex);
        VRDropdownFactory.SetOptions(_bitrateDropdown, bitrateOptions, suggestedBitrateIndex);

        // FPS - auto-select recommended
        var fpsOptions = BuildOptionsWithRecommended(FPS_OPTIONS, suggestedFpsIndex);
        VRDropdownFactory.SetOptions(_fpsDropdown, fpsOptions, suggestedFpsIndex);

        Debug.Log($"[RTTRemoteMenu] Auto-selected for {monitorCount} monitors: Res={RESOLUTION_OPTIONS[suggestedResIndex]}, Bitrate={BITRATE_OPTIONS[suggestedBitrateIndex]}, FPS={FPS_OPTIONS[suggestedFpsIndex]}");
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
    /// Lock all dropdowns, input fields, and QR button (disable interaction).
    /// Called when state changes to Ready (START REMOTE).
    /// </summary>
    private void LockAllInputs()
    {
        // Khóa 4 dropdowns
        VRDropdownFactory.SetInteractable(_monitorsDropdown, false);
        VRDropdownFactory.SetInteractable(_resolutionDropdown, false);
        VRDropdownFactory.SetInteractable(_bitrateDropdown, false);
        VRDropdownFactory.SetInteractable(_fpsDropdown, false);

        // Khóa 2 input fields
        VRInputFieldFactory.SetInteractable(_hostInput, false);
        VRInputFieldFactory.SetInteractable(_portInput, false);

        // Khóa QR button
        VRButtonFactory.SetInteractable(_qrButton, false);

        Debug.Log("[RTTRemoteMenu] All inputs locked (Ready state)");
    }

    /// <summary>
    /// Unlock all dropdowns, input fields, and QR button (enable interaction).
    /// </summary>
    private void UnlockAllInputs()
    {
        // Mở khóa 4 dropdowns
        VRDropdownFactory.SetInteractable(_monitorsDropdown, true);
        VRDropdownFactory.SetInteractable(_resolutionDropdown, true);
        VRDropdownFactory.SetInteractable(_bitrateDropdown, true);
        VRDropdownFactory.SetInteractable(_fpsDropdown, true);

        // Mở khóa 2 input fields
        VRInputFieldFactory.SetInteractable(_hostInput, true);
        VRInputFieldFactory.SetInteractable(_portInput, true);

        // Mở khóa QR button
        VRButtonFactory.SetInteractable(_qrButton, true);

        Debug.Log("[RTTRemoteMenu] All inputs unlocked");
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
    /// Called when START is clicked (previously SETUP REMOTE).
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
        Debug.Log($"[RTTRemoteMenu] CreateSidePanels called, _menuFrame null: {_menuFrame == null}");

        if (_menuFrame == null)
        {
            Debug.LogError("[RTTRemoteMenu] CreateSidePanels: _menuFrame is NULL, cannot create side panels!");
            return;
        }

        float mainPanelWidth = _menuFrame.PanelWidth;   // ~1.6m
        float mainPanelHeight = _menuFrame.PanelHeight; // ~0.9m
        float sideWidth = mainPanelWidth / 3f;          // ~0.53m
        float sideHeight = mainPanelHeight;             // Same height as main panel

        // Gap between main panel and side panels (in meters)
        float gapMeters = 0.05f;

        // Angle to rotate side panels (facing slightly toward viewer)
        float rotationAngle = 30f;

        Debug.Log($"[RTTRemoteMenu] Creating Hardware Info Panel (left)...");
        // Hardware Info Panel (Left)
        PlaceSidePanelFlat("HardwareInfoPanel", -1, mainPanelWidth, sideWidth, sideHeight,
            gapMeters, rotationAngle, RTTInfoSidePanel.PanelType.HardwareInfo, ref _hardwareFrame);
        Debug.Log($"[RTTRemoteMenu] Hardware frame created: {_hardwareFrame != null}");

        Debug.Log($"[RTTRemoteMenu] Creating Network Info Panel (right)...");
        // Network Info Panel (Right)
        PlaceSidePanelFlat("NetworkInfoPanel", 1, mainPanelWidth, sideWidth, sideHeight,
            gapMeters, rotationAngle, RTTInfoSidePanel.PanelType.NetworkInfo, ref _networkFrame);
        Debug.Log($"[RTTRemoteMenu] Network frame created: {_networkFrame != null}");

        Debug.Log($"[RTTRemoteMenu] Side panels created ({sideWidth:F2}m x {sideHeight:F2}m) gap={gapMeters}m angle={rotationAngle}° (hidden)");
    }

    /// <summary>
    /// Place a side panel adjacent to the main panel with inner edge at same Z.
    /// Uses RTTMenuFrame for frame rendering and RTTInfoSidePanel for content.
    /// </summary>
    /// <param name="side">-1 for left, +1 for right</param>
    private void PlaceSidePanelFlat(string name, int side, float mainWidth, float sideWidth, float sideHeight,
        float gap, float rotationAngle, RTTInfoSidePanel.PanelType type, ref RTTMenuFrame frameRef)
    {
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
        Vector3 panelPos = mainPos + offset;

        // Rotate panel: left panel rotates negative (faces right), right panel rotates positive (faces left)
        Quaternion panelRot = mainRot * Quaternion.Euler(0, side * rotationAngle, 0);

        // Calculate logical width based on aspect ratio (same resolution density as main panel)
        float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

        // Create RTTMenuFrame for the side panel
        frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, false);
        frameRef.gameObject.name = name;
        frameRef.transform.position = panelPos;
        frameRef.transform.rotation = panelRot;
        frameRef.transform.localScale = Vector3.one;

        // IMMEDIATELY set invisible BEFORE Start() runs
        // This sets _isVisible = false, so when SetupDisplayQuad() creates the quad, it will be hidden
        frameRef.SetVisible(false);

        // Configure frame appearance (smaller margins for side panels)
        frameRef.SetContentMargins(40f, 40f, 30f, 30f);
        frameRef.SetFloatingDataEnabled(true, 10); // Fewer particles for smaller panel

        // Frame will stay hidden until ShowSidePanels() is called on successful connect

        // Wait for frame to initialize, then create content
        _sidePanelCoroutinesStarted = true;
        StartCoroutine(CreateSidePanelContent(frameRef, type));
    }

    /// <summary>
    /// Coroutine to create side panel content after frame is initialized.
    /// IMPORTANT: Content must be created WHILE frame is active, then frame can be hidden.
    /// </summary>
    private IEnumerator CreateSidePanelContent(RTTMenuFrame frame, RTTInfoSidePanel.PanelType type)
    {
        Debug.Log($"[RTTRemoteMenu] CreateSidePanelContent started for {type}");

        // Check if frame reference is valid
        if (frame == null)
        {
            Debug.LogError($"[RTTRemoteMenu] Frame reference is NULL for {type} at coroutine start!");
            yield break;
        }

        // Wait one frame to allow Start() to be called normally
        yield return null;

        // If frame is still not initialized after first frame, force initialize it
        // This handles cases where the frame's GameObject was disabled before Start() could run
        if (!frame.IsInitialized)
        {
            Debug.Log($"[RTTRemoteMenu] Frame not initialized for {type}, calling EnsureInitialized()");
            frame.EnsureInitialized();
        }

        // Wait for ContentContainer to be valid (should be immediate after Initialize)
        int maxWait = 60; // ~1 second timeout
        int waitCount = 0;

        while (frame.ContentContainer == null && waitCount < maxWait)
        {
            if (waitCount % 30 == 0)
            {
                Debug.Log($"[RTTRemoteMenu] Waiting for {type} ContentContainer: IsInit={frame.IsInitialized}, wait={waitCount}");
            }
            yield return null;
            waitCount++;

            // Check if frame was destroyed during wait
            if (frame == null)
            {
                Debug.LogError($"[RTTRemoteMenu] Frame was DESTROYED while waiting for {type} at frame {waitCount}!");
                yield break;
            }
        }

        // Verify ContentContainer is valid
        if (frame.ContentContainer == null)
        {
            Debug.LogError($"[RTTRemoteMenu] ContentContainer is NULL for {type} after {waitCount} frames! IsInitialized={frame.IsInitialized}");
            yield break;
        }

        Debug.Log($"[RTTRemoteMenu] Frame initialized for {type} after {waitCount} frames, ContentContainer valid");

        // Frame is already hidden via SetVisible(false) in PlaceSidePanelFlat
        // Wait extra frames for layout to settle
        yield return null;
        yield return null;

        // Double-check ContentContainer is still valid after yield
        if (frame.ContentContainer == null)
        {
            Debug.LogError($"[RTTRemoteMenu] ContentContainer became NULL after yield for {type}!");
            yield break;
        }

        // Create RTTInfoSidePanel as content WHILE frame is still active
        // This ensures UI components are properly initialized
        // IMPORTANT: Create as child of ContentContainer directly to avoid parenting issues
        GameObject contentObj = null;
        RTTInfoSidePanel panel = null;

        try
        {
            contentObj = new GameObject($"InfoPanel_{type}");

            // Set parent IMMEDIATELY after creation before adding any components
            contentObj.transform.SetParent(frame.ContentContainer, false);

            Debug.Log($"[RTTRemoteMenu] Created InfoPanel_{type}, parent set to ContentContainer");

            panel = contentObj.AddComponent<RTTInfoSidePanel>();
            panel.ThemeColor = themeColor;
            panel.CustomFont = customFont;

            // Build panel content inside frame's container
            // Note: BuildUI will call SetParent again but that's OK since parent is already correct
            panel.BuildUI(frame.ContentContainer, type, themeColor, customFont);

            // Verify panel was built correctly
            if (contentObj.transform.parent != frame.ContentContainer)
            {
                Debug.LogError($"[RTTRemoteMenu] InfoPanel_{type} has wrong parent after BuildUI! Expected ContentContainer, got {contentObj.transform.parent?.name ?? "null"}");
                // Force correct parent
                contentObj.transform.SetParent(frame.ContentContainer, false);
            }

            Debug.Log($"[RTTRemoteMenu] InfoPanel_{type} BuildUI completed, parent={contentObj.transform.parent?.name}");

            // Force layout rebuild to ensure content is positioned correctly
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(frame.ContentContainer);

            // Subscribe to content changes to trigger RTT re-render
            panel.OnContentChanged += () => frame.MarkDirty();

            // Assign to the appropriate field based on type and show loading state immediately
            if (type == RTTInfoSidePanel.PanelType.HardwareInfo)
            {
                _hardwareInfoPanel = panel;
                // Apply cached data if it arrived before panel was ready
                if (_cachedHardwareInfo != null)
                {
                    panel.SetHardwareInfo(_cachedHardwareInfo);
                    Debug.Log($"[RTTRemoteMenu] Applied cached hardware info to panel");
                }
                else
                {
                    // Show loading state immediately so user sees placeholder text
                    panel.ShowLoadingState();
                }
            }
            else
            {
                _networkInfoPanel = panel;
                // Apply cached data if it arrived before panel was ready
                if (_cachedNetworkInfo != null)
                {
                    panel.SetNetworkInfo(_cachedNetworkInfo);
                    Debug.Log($"[RTTRemoteMenu] Applied cached network info to panel");
                }
                else
                {
                    // Show speed test loading state for network panel
                    panel.ShowSpeedTestLoadingState();
                }
            }

            // Force another layout rebuild after content is set
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(frame.ContentContainer);

            // Mark dirty to render the content
            frame.MarkDirty();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RTTRemoteMenu] Exception creating InfoPanel_{type}: {ex.Message}\n{ex.StackTrace}");
            if (contentObj != null) Destroy(contentObj);
            yield break;
        }

        // Wait multiple frames to ensure rendering completes
        yield return null; // Let render happen
        yield return null; // Extra safety frame

        // Only hide if NOT currently connecting/connected
        // If user already clicked Connect, keep the frame visible
        bool shouldHide = _currentPhase == ConnectionPhase.Disconnected ||
                          _currentPhase == ConnectionPhase.Error;

        if (shouldHide)
        {
            frame.gameObject.SetActive(false);
            Debug.Log($"[RTTRemoteMenu] Side panel content created for {type}, frame hidden (not connecting)");
        }
        else
        {
            // Keep visible since we're in connecting state
            frame.SetVisible(true);

            // Show appropriate content based on cached data
            if (type == RTTInfoSidePanel.PanelType.HardwareInfo)
            {
                if (_cachedHardwareInfo != null)
                    panel.SetHardwareInfo(_cachedHardwareInfo);
                else
                    panel.ShowLoadingState();
            }
            else
            {
                if (_cachedNetworkInfo != null)
                    panel.SetNetworkInfo(_cachedNetworkInfo);
                else
                    panel.ShowSpeedTestLoadingState();
            }

            Debug.Log($"[RTTRemoteMenu] Side panel content created for {type}, keeping visible (connecting)");
        }

        Debug.Log($"[RTTRemoteMenu] Side panel content created for {type}. ContentContainer children count: {frame.ContentContainer.childCount}");
    }

    /// <summary>
    /// Ensure both panels are visible with cached data or loading state.
    /// Called when any info is received to ensure both panels show together.
    /// </summary>
    private void EnsureBothPanelsVisible()
    {
        // Show hardware frame if not already active
        if (_hardwareFrame != null && !_hardwareFrame.gameObject.activeSelf)
        {
            Debug.Log("[RTTRemoteMenu] EnsureBothPanelsVisible: Showing hardware frame");
            _hardwareFrame.gameObject.SetActive(true);
            _hardwareFrame.SetVisible(true); // Re-enable DisplayQuad
            if (_hardwareInfoPanel != null)
            {
                // Use cached data if available
                if (_cachedHardwareInfo != null)
                {
                    _hardwareInfoPanel.SetHardwareInfo(_cachedHardwareInfo);
                }
                else
                {
                    _hardwareInfoPanel.ShowLoadingState();
                }
            }
        }

        // Show network frame if not already active
        if (_networkFrame != null && !_networkFrame.gameObject.activeSelf)
        {
            Debug.Log("[RTTRemoteMenu] EnsureBothPanelsVisible: Showing network frame");
            _networkFrame.gameObject.SetActive(true);
            _networkFrame.SetVisible(true); // Re-enable DisplayQuad
            if (_networkInfoPanel != null)
            {
                // Use cached data if available
                if (_cachedNetworkInfo != null)
                {
                    _networkInfoPanel.SetNetworkInfo(_cachedNetworkInfo);
                }
                else
                {
                    _networkInfoPanel.ShowSpeedTestLoadingState();
                }
            }
        }
    }

    /// <summary>
    /// Show side panels with current data or loading state.
    /// Uses cached data if available, otherwise shows loading state.
    /// </summary>
    private void ShowSidePanels()
    {
        Debug.Log($"[RTTRemoteMenu] ShowSidePanels called, hardware frame: {_hardwareFrame != null}, network frame: {_networkFrame != null}");

        if (_hardwareFrame != null)
        {
            Debug.Log("[RTTRemoteMenu] Activating hardware frame");
            _hardwareFrame.gameObject.SetActive(true);
            _hardwareFrame.SetVisible(true); // Re-enable DisplayQuad
            if (_hardwareInfoPanel != null)
            {
                // Use cached data if available, otherwise show loading state
                if (_cachedHardwareInfo != null)
                {
                    _hardwareInfoPanel.SetHardwareInfo(_cachedHardwareInfo);
                    Debug.Log("[RTTRemoteMenu] Applied cached hardware info");
                }
                else
                {
                    _hardwareInfoPanel.ShowLoadingState();
                }
            }
        }
        else
        {
            Debug.LogError("[RTTRemoteMenu] Hardware frame is NULL!");
        }

        if (_networkFrame != null)
        {
            Debug.Log("[RTTRemoteMenu] Activating network frame");
            _networkFrame.gameObject.SetActive(true);
            _networkFrame.SetVisible(true); // Re-enable DisplayQuad
            if (_networkInfoPanel != null)
            {
                // Use cached data if available, otherwise show loading state
                if (_cachedNetworkInfo != null)
                {
                    _networkInfoPanel.SetNetworkInfo(_cachedNetworkInfo);
                    Debug.Log("[RTTRemoteMenu] Applied cached network info");
                }
                else
                {
                    _networkInfoPanel.ShowSpeedTestLoadingState();
                }
            }
        }
        else
        {
            Debug.LogError("[RTTRemoteMenu] Network frame is NULL!");
        }

        Debug.Log("[RTTRemoteMenu] Side panels shown");
    }

    /// <summary>
    /// Handle speed test progress from client.
    /// Only updates UI on completion to reduce measurement time.
    /// </summary>
    public void HandleSpeedTestProgress(string direction, double currentMbps, int progress)
    {
        if (_networkInfoPanel == null) return;

        // Only update UI when test is complete - skip progress updates to reduce time
        bool isComplete = progress >= 100;

        if (isComplete)
        {
            _lastReportedMbps = currentMbps;
            _networkInfoPanel.UpdateSpeedTestProgress(direction, currentMbps, progress);
        }
    }

    /// <summary>
    /// Hide side panels (hides the frames which contain the panels).
    /// Uses SetVisible(false) to hide DisplayQuad while keeping GameObject active.
    /// </summary>
    private void HideSidePanels()
    {
        if (_hardwareFrame != null)
        {
            _hardwareFrame.SetVisible(false);
            _hardwareFrame.gameObject.SetActive(false);
            Debug.Log("[RTTRemoteMenu] Hardware frame hidden");
        }

        if (_networkFrame != null)
        {
            _networkFrame.SetVisible(false);
            _networkFrame.gameObject.SetActive(false);
            Debug.Log("[RTTRemoteMenu] Network frame hidden");
        }

        Debug.Log("[RTTRemoteMenu] HideSidePanels completed");
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

    private GameObject CreateButton(Transform parent, float x, float y, float w, float h,
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

        return btn;
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
        if (string.IsNullOrEmpty(value)) return -1;

        // Normalize: replace all double-spaces with single space
        string normalized = value.Replace("x", " x ");
        while (normalized.Contains("  "))
            normalized = normalized.Replace("  ", " ");
        normalized = normalized.Trim();

        // Also create a no-space version for comparison
        string noSpaceValue = value.Replace(" ", "");

        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                options[i].Replace(" ", "").Equals(noSpaceValue, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        Debug.Log($"[RTTRemoteMenu] FindOptionIndex: '{value}' not found in options. Normalized: '{normalized}'");
        return -1;
    }
    #endregion

    #region Lifecycle
    private bool _sidePanelCoroutinesStarted = false;

    private void OnDisable()
    {
        Debug.LogWarning("[RTTRemoteMenu] OnDisable called - coroutines will be paused!");
    }

    private void OnEnable()
    {
        Debug.Log("[RTTRemoteMenu] OnEnable called");

        // Restart side panel coroutines if they were interrupted before completion
        // Coroutines don't automatically resume after disable/enable in Unity
        if (_sidePanelCoroutinesStarted && _hardwareFrame != null && _hardwareInfoPanel == null)
        {
            Debug.Log("[RTTRemoteMenu] Restarting Hardware panel coroutine (was interrupted)");
            StartCoroutine(CreateSidePanelContent(_hardwareFrame, RTTInfoSidePanel.PanelType.HardwareInfo));
        }

        if (_sidePanelCoroutinesStarted && _networkFrame != null && _networkInfoPanel == null)
        {
            Debug.Log("[RTTRemoteMenu] Restarting Network panel coroutine (was interrupted)");
            StartCoroutine(CreateSidePanelContent(_networkFrame, RTTInfoSidePanel.PanelType.NetworkInfo));
        }
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        Debug.Log("[RTTRemoteMenu] OnDestroy called - this will stop all coroutines including CreateSidePanelContent!");

        DisableHorizontalSeparators();

        if (_qrScannerManager != null)
        {
            _qrScannerManager.OnQRScanned -= OnQRCodeScanned;
            _qrScannerManager.OnCancelled -= OnQRScanCancelled;
        }

        // Unsubscribe from ViewModel events
        if (_viewModel != null)
        {
            _viewModel.Phase.OnChanged -= HandlePhaseChanged;
            _viewModel.HardwareInfo.OnChanged -= HandleHardwareInfoReceived;
            _viewModel.NetworkInfo.OnChanged -= HandleNetworkInfoReceived;
            _viewModel.SuggestedConfig.OnChanged -= ApplySuggestedConfig;
            _viewModel.SpeedTestProgress.OnChanged -= HandleSpeedTestProgressValue;
            _viewModel.CurrentBandwidth.OnChanged -= HandleBandwidthChanged;
            _viewModel.ErrorMessage.OnChanged -= HandleErrorMessage;
        }

        // Destroy side panels
        if (_hardwareInfoPanel != null)
            Destroy(_hardwareInfoPanel.gameObject);
        if (_networkInfoPanel != null)
            Destroy(_networkInfoPanel.gameObject);
    }
    #endregion
}
