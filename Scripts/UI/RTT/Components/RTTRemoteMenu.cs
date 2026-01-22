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
#pragma warning disable CS0067 // Event is never used - exposed for external subscribers
    public event Action OnStartClicked;
#pragma warning restore CS0067
    public event Action<ConnectionPhase> OnConnectionStateChanged;
    public event Action OnDisconnectClicked;
    public event Action OnResumeClicked;
    #endregion

    #region Private Fields
    // UI References
    private GameObject _hostInput;
    private GameObject _usbModeToggle;  // USB mode checkbox (replaces port input)
    private bool _isUsbMode = false;    // Current USB mode state
    private string _usbTetheringIP = null;  // USB Tethering IP from QR scan (for full USB streaming)
    private const int DEFAULT_PORT = 8288;
    private GameObject _monitorsDropdown;
    private GameObject _styleDropdown;
    private GameObject _bitrateDropdown;
    private GameObject _fpsDropdown;
    private GameObject _bodyContainer;

    // Connect button reference for dynamic text change
    private GameObject _connectButton;
    private TextMeshProUGUI _connectButtonText;
    private ConnectionPhase _currentPhase = ConnectionPhase.Disconnected;

    // Streaming mode buttons (shown when streaming, hidden otherwise)
    private GameObject _streamingButtonsContainer;
    private GameObject _disconnectButton;
    private GameObject _resumeButton;

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
    private float _contentW;   // Content width (same as grid width)
    private float _gridGapX;   // Store grid gapX for button alignment

    // Font sizes
    private const int LABEL_FONT_SIZE = 40;
    private const int INPUT_FONT_SIZE = 42;
    private const int DROPDOWN_LABEL_FONT_SIZE = 40;
    private const int DROPDOWN_VALUE_FONT_SIZE = 42;

    // Default dropdown options (without Recommended suffix)
    private static readonly string[] MONITOR_OPTIONS = { "1 Monitor", "2 Monitors", "3 Monitors" };
    private static readonly string[] STYLE_OPTIONS = { "Flat Planar", "Curved Surround" };
    private static readonly string[] BITRATE_OPTIONS = { "10 Mbps", "15 Mbps", "20 Mbps", "25 Mbps", "30 Mbps" };
    private static readonly string[] FPS_OPTIONS = { "30 FPS", "45 FPS", "60 FPS" };
    #endregion

    #region Public Accessors (Easy Form Data Access)
    /// <summary>
    /// Get connection host based on mode:
    /// - USB Mode: Use USB Tethering IP from QR scan
    /// - WiFi Mode: Use manual host input
    /// </summary>
    public string Host {
        get {
            // USB Mode uses USB Tethering IP from QR scan
            if (_isUsbMode && HasUsbTetheringIP) return _usbTetheringIP;
            // WiFi Mode uses manual host input
            return VRInputFieldFactory.GetValue(_hostInput);
        }
    }
    public string Port => DEFAULT_PORT.ToString();
    public bool IsUsbMode => _isUsbMode;
    public string UsbTetheringIP => _usbTetheringIP;
    public bool HasUsbTetheringIP => !string.IsNullOrEmpty(_usbTetheringIP);
    public int MonitorIndex => VRDropdownFactory.GetSelectedIndex(_monitorsDropdown);
    public string Style => VRDropdownFactory.GetSelectedValue(_styleDropdown);
    public int StyleIndex => VRDropdownFactory.GetSelectedIndex(_styleDropdown);
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
        _contentW = contentW; // Store for streaming buttons alignment

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

        // Subscribe to progress events for button text updates
        _viewModel.ServerSetupProgress.OnChanged += HandleServerSetupProgressForButton;
        _viewModel.MonitorIceProgress.OnChanged += HandleMonitorIceProgressForButton;
        _viewModel.OnAllMonitorsReady += HandleAllMonitorsReadyForButton;

        // Update UI with current state
        HandlePhaseChanged(_viewModel.Phase.Value);

        Debug.Log("[RTTRemoteMenu] Bound to ConnectionViewModel");
    }
    #endregion

    #region Header
    private void CreateHeader(Transform parent, float x, float y, float w, float h)
    {
        var header = CreateContainer(parent, "Header", x, y, w, h);

        // Close button (BareIconButton - no background/border)
        float closeSize = 75f;
        var closeBtn = VRButtonFactory.CreateBareIconButton(header.transform, closeSize, LoadIcon("close"), themeColor, () => OnBackClicked?.Invoke());
        var closeRT = closeBtn.GetComponent<RectTransform>();
        closeRT.anchorMin = closeRT.anchorMax = Vector2.zero;
        closeRT.pivot = Vector2.zero;
        closeRT.anchoredPosition = new Vector2(0, (h - closeSize) / 2f);

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

        float hostW = (w - gapX) * 0.6f;  // Host takes more space now
        float toggleW = (w - gapX) * 0.4f;

        // Host Input
        _hostInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, hostW,
            "Host", "192.168.1.7", accentColor,
            onEndEdit: null,
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont);
        PositionElement(_hostInput, 0, 0);

        // USB Mode Toggle (replaces Port input)
        _usbModeToggle = CreateUsbModeToggle(row.transform, toggleW, h);
        PositionElement(_usbModeToggle, hostW + gapX, 0);
    }

    /// <summary>
    /// Create USB Mode toggle checkbox with label.
    /// When enabled, connects via USB Tethering IP.
    /// </summary>
    private GameObject CreateUsbModeToggle(Transform parent, float width, float height)
    {
        var container = new GameObject("UsbModeToggle");
        container.transform.SetParent(parent, false);
        var rt = container.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        // Layout: Label on top, toggle button below
        float labelH = height * 0.35f;
        float toggleH = height * 0.65f;

        // Label "USB Mode"
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(container.transform, false);
        var labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0.65f);
        labelRT.anchorMax = new Vector2(1, 1);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        var labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
        labelTMP.text = "USB Mode";
        labelTMP.fontSize = LABEL_FONT_SIZE;
        labelTMP.color = themeColor;
        labelTMP.alignment = TextAlignmentOptions.Left;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.raycastTarget = false;
        if (customFont) labelTMP.font = customFont;

        // Toggle button container
        var toggleContainer = new GameObject("ToggleContainer");
        toggleContainer.transform.SetParent(container.transform, false);
        var toggleContainerRT = toggleContainer.AddComponent<RectTransform>();
        toggleContainerRT.anchorMin = new Vector2(0, 0);
        toggleContainerRT.anchorMax = new Vector2(1, 0.6f);
        toggleContainerRT.offsetMin = Vector2.zero;
        toggleContainerRT.offsetMax = Vector2.zero;

        // Checkbox button (toggle style)
        float checkboxSize = toggleH * 0.7f;
        var checkboxConfig = new VRButtonFactory.ButtonConfig
        {
            label = "",
            themeColor = themeColor,
            width = checkboxSize,
            height = checkboxSize,
            iconOnly = true,
            iconSize = checkboxSize * 0.6f,
            cornerRadius = 0.15f,
            backgroundAlpha = 0.15f,
            borderWidth = 0.04f,
            popAmount = 0.02f
        };

        var checkboxBtn = VRButtonFactory.CreateButton(toggleContainer.transform, checkboxConfig, OnUsbModeToggleClicked);
        var checkboxRT = checkboxBtn.GetComponent<RectTransform>();
        checkboxRT.anchorMin = new Vector2(0, 0.5f);
        checkboxRT.anchorMax = new Vector2(0, 0.5f);
        checkboxRT.pivot = new Vector2(0, 0.5f);
        checkboxRT.anchoredPosition = Vector2.zero;

        // Checkmark indicator (icon, hidden by default)
        var checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(checkboxBtn.transform, false);
        var checkmarkRT = checkmark.AddComponent<RectTransform>();
        checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkmarkRT.offsetMin = Vector2.zero;
        checkmarkRT.offsetMax = Vector2.zero;

        var checkmarkImg = checkmark.AddComponent<Image>();
        checkmarkImg.sprite = Resources.Load<Sprite>("icon_check_mark");
        checkmarkImg.color = Color.white;  // Use white to show icon at full brightness
        checkmarkImg.preserveAspect = true;
        checkmarkImg.raycastTarget = false;
        checkmark.SetActive(false); // Hidden by default

        // Status label next to checkbox
        var statusObj = new GameObject("Status");
        statusObj.transform.SetParent(toggleContainer.transform, false);
        var statusRT = statusObj.AddComponent<RectTransform>();
        statusRT.anchorMin = new Vector2(0, 0);
        statusRT.anchorMax = new Vector2(1, 1);
        statusRT.offsetMin = new Vector2(checkboxSize + 15f, 0);
        statusRT.offsetMax = Vector2.zero;

        var statusTMP = statusObj.AddComponent<TextMeshProUGUI>();
        statusTMP.text = "USB Tethering";
        statusTMP.fontSize = INPUT_FONT_SIZE * 0.85f;
        statusTMP.color = themeColor;
        statusTMP.alignment = TextAlignmentOptions.Left;
        statusTMP.verticalAlignment = VerticalAlignmentOptions.Middle;
        statusTMP.raycastTarget = false;
        if (customFont) statusTMP.font = customFont;

        return container;
    }

    /// <summary>
    /// Handle USB mode toggle click.
    /// </summary>
    private void OnUsbModeToggleClicked()
    {
        _isUsbMode = !_isUsbMode;
        UpdateUsbModeToggleVisual();

        // USB mode requires QR scan - disable manual host input
        VRInputFieldFactory.SetInteractable(_hostInput, !_isUsbMode);

        Debug.Log($"[RTTRemoteMenu] USB Mode: {_isUsbMode}");
    }

    /// <summary>
    /// Update USB mode toggle visual state.
    /// </summary>
    private void UpdateUsbModeToggleVisual()
    {
        if (_usbModeToggle == null) return;

        // Find checkmark and update visibility (direct child of Btn_)
        var checkmark = _usbModeToggle.transform.Find("ToggleContainer/Btn_/Checkmark");
        if (checkmark != null)
        {
            checkmark.gameObject.SetActive(_isUsbMode);
        }

        // Update status text color
        var statusTMP = _usbModeToggle.transform.Find("ToggleContainer/Status")?.GetComponent<TextMeshProUGUI>();
        if (statusTMP != null)
        {
            statusTMP.color = themeColor;  // Always bright
        }

        // Update background alpha based on state
        var visuals = _usbModeToggle.transform.Find("ToggleContainer/Btn_/HitArea/Visuals/Background");
        if (visuals != null)
        {
            var img = visuals.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                img.material.SetFloat("_GlassAlpha", _isUsbMode ? 0.35f : 0.15f);
            }
        }
    }

    /// <summary>
    /// Update USB mode label to show USB Tethering IP.
    /// </summary>
    private void UpdateUsbModeLabel()
    {
        if (_usbModeToggle == null) return;

        var statusTMP = _usbModeToggle.transform.Find("ToggleContainer/Status")?.GetComponent<TextMeshProUGUI>();
        if (statusTMP != null)
        {
            if (!string.IsNullOrEmpty(_usbTetheringIP))
            {
                statusTMP.text = $"USB: {_usbTetheringIP}";
                statusTMP.color = _isUsbMode ? themeColor : new Color(0.4f, 0.8f, 0.4f);  // Green tint
            }
            else
            {
                statusTMP.text = "USB Tethering";
                statusTMP.color = themeColor;  // Always bright
            }
        }
    }
    #endregion

    #region Grid (Dropdowns)
    private void CreateGrid(Transform parent, float x, float y, float w, float h, float dropdownH, float gapX, float gapY)
    {
        var grid = CreateContainer(parent, "Grid", x, y, w, h);

        // Store gapX for button alignment in CreateStreamingButtons
        _gridGapX = gapX;

        float cellW = (w - gapX) / 2f;

        // Dropdown options
        var monitorOptions = new List<string> { "1 Monitor", "2 Monitors", "3 Monitors" };
        var styleOptions = new List<string> { "Flat Planar", "Curved Surround" };
        var bitrateOptions = new List<string> { "10 Mbps", "15 Mbps", "20 Mbps", "25 Mbps", "30 Mbps" };
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

        // Style dropdown (replaces Resolution): Flat Planar = FixedThreeSlot + flat, Curved Surround = Dynamic + curved
        _styleDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Style", LoadIcon("resolution"), accentColor,  // Reusing resolution icon for now
            styleOptions, 0,  // Default: Flat Planar
            onValueChanged: HandleStyleChanged,
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_styleDropdown, cellW + gapX, row1Y);

        // Row 2 (bottom) - Total Bitrate (distributed across all monitors)
        _bitrateDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Total Bitrate", LoadIcon("bitrate"), accentColor,
            bitrateOptions, 2,
            onValueChanged: HandleBitrateChanged,
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_bitrateDropdown, 0, 0);

        _fpsDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "FPS", LoadIcon("fps"), themeColor,
            fpsOptions, 2,
            onValueChanged: HandleFpsChanged,
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_fpsDropdown, cellW + gapX, 0);
    }
    #endregion

    #region Connect Button
    private void CreateConnectButton(Transform parent, float w, float h)
    {
        // Main connect button
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

        // Create streaming buttons container (hidden by default)
        CreateStreamingButtons(parent, w, h);
    }

    /// <summary>
    /// Create DISCONNECT and RESUME buttons for streaming mode.
    /// These are hidden by default and shown when streaming is active.
    /// Buttons are positioned to align with Bitrate and FPS dropdowns.
    /// </summary>
    private void CreateStreamingButtons(Transform parent, float totalWidth, float h)
    {
        // Container for streaming buttons - use same width as grid (contentW)
        _streamingButtonsContainer = new GameObject("StreamingButtons");
        _streamingButtonsContainer.transform.SetParent(parent, false);

        // Use _contentW (same as grid width) for proper alignment
        float containerWidth = _contentW;

        RectTransform containerRT = _streamingButtonsContainer.AddComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.5f, 0);
        containerRT.anchorMax = new Vector2(0.5f, 0);
        containerRT.pivot = new Vector2(0.5f, 0);
        containerRT.anchoredPosition = Vector2.zero;
        containerRT.sizeDelta = new Vector2(containerWidth, h);

        // Calculate cell positions exactly like CreateGrid
        float gapX = _gridGapX;
        float cellW = (containerWidth - gapX) / 2f;
        float buttonWidth = cellW; // Same width as dropdown

        // Calculate center positions relative to container center
        float containerCenterX = containerWidth / 2f;
        float bitrateCenterX = cellW / 2f;  // Center of left cell
        float fpsCenterX = cellW + gapX + cellW / 2f;  // Center of right cell

        // DISCONNECT button (under Bitrate) - Light deep sea blue
        Color lightDeepSeaBlue = new Color(0.3f, 0.6f, 0.8f); // Light deep sea blue
        var disconnectConfig = new VRButtonFactory.ButtonConfig
        {
            label = "DISCONNECT",
            themeColor = lightDeepSeaBlue,
            width = buttonWidth,
            height = h,
            fontSize = 36,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.85f,
            cornerRadius = 0.15f,
            edgePadding = 0.08f,
            popAmount = 0.05f,
            useConnectButtonShader = true,
            connectColorA = lightDeepSeaBlue,
            connectColorB = new Color(0.15f, 0.4f, 0.6f), // Darker deep sea blue
            connectColorC = new Color(0.5f, 0.75f, 0.95f) // Lighter deep sea blue
        };

        _disconnectButton = VRButtonFactory.CreateButton(_streamingButtonsContainer.transform, disconnectConfig, OnDisconnectButtonClicked);
        RectTransform disconnectRT = _disconnectButton.GetComponent<RectTransform>();
        disconnectRT.anchorMin = new Vector2(0.5f, 0.5f);
        disconnectRT.anchorMax = new Vector2(0.5f, 0.5f);
        disconnectRT.pivot = new Vector2(0.5f, 0.5f);
        disconnectRT.anchoredPosition = new Vector2(bitrateCenterX - containerCenterX, 0);

        // RESUME button (under FPS) - Same colors as CONNECT button
        var resumeConfig = new VRButtonFactory.ButtonConfig
        {
            label = "RESUME",
            themeColor = themeColor,
            width = buttonWidth,
            height = h,
            fontSize = 36,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.85f,
            cornerRadius = 0.15f,
            edgePadding = 0.08f,
            popAmount = 0.05f,
            useConnectButtonShader = true,
            connectColorA = themeColor,
            connectColorB = new Color(0.1f, 0.5f, 0.85f), // Same as CONNECT
            connectColorC = accentColor
        };

        _resumeButton = VRButtonFactory.CreateButton(_streamingButtonsContainer.transform, resumeConfig, OnResumeButtonClicked);
        RectTransform resumeRT = _resumeButton.GetComponent<RectTransform>();
        resumeRT.anchorMin = new Vector2(0.5f, 0.5f);
        resumeRT.anchorMax = new Vector2(0.5f, 0.5f);
        resumeRT.pivot = new Vector2(0.5f, 0.5f);
        resumeRT.anchoredPosition = new Vector2(fpsCenterX - containerCenterX, 0);

        // Hide streaming buttons by default
        _streamingButtonsContainer.SetActive(false);
    }

    /// <summary>
    /// Handle DISCONNECT button click.
    /// Disconnects from server and resets menu to initial state.
    /// </summary>
    private async void OnDisconnectButtonClicked()
    {
        Debug.Log("[RTTRemoteMenu] DISCONNECT clicked");

        // Lock Resume button (Dim + Lock)
        if (_resumeButton != null) VRButtonFactory.SetInteractable(_resumeButton, false);

        // Lock Disconnect button BUT keep it bright (lit)
        // We only disable the Button component to prevent clicks, avoiding VRButtonFactory.SetInteractable which dims it
        var disconnectBtnComp = _disconnectButton?.GetComponentInChildren<Button>();
        if (disconnectBtnComp != null) disconnectBtnComp.interactable = false;

        // Change text to DISCONNECTING...
        var disconnectText = _disconnectButton?.GetComponentInChildren<TextMeshProUGUI>();
        string originalText = "DISCONNECT";
        if (disconnectText != null) disconnectText.text = "DISCONNECTING...";

        // Fire event for controller to handle
        OnDisconnectClicked?.Invoke();

        // Disconnect via ViewModel
        if (_viewModel != null)
        {
            await _viewModel.DisconnectAsync();
        }

        // Change text to DISCONNECTED
        if (disconnectText != null) disconnectText.text = "DISCONNECTED";

        // Short delay to show the success state
        await System.Threading.Tasks.Task.Delay(1000);

        // Reset state for next time
        if (disconnectText != null) disconnectText.text = originalText;
        
        // Restore buttons
        if (_resumeButton != null) VRButtonFactory.SetInteractable(_resumeButton, true);
        if (_disconnectButton != null) 
        {
            // Restore interactivity fully (ensure alpha is 1 in case it was modified elsewhere)
            VRButtonFactory.SetInteractable(_disconnectButton, true); 
        }

        // Switch back to connect button
        ShowConnectButton();
    }

    /// <summary>
    /// Handle RESUME button click.
    /// Returns to the ClusterRig/streaming view.
    /// </summary>
    private void OnResumeButtonClicked()
    {
        Debug.Log("[RTTRemoteMenu] RESUME clicked");

        // Fire event for controller to handle (hide menu, show ClusterRig)
        OnResumeClicked?.Invoke();
    }

    /// <summary>
    /// Show streaming buttons (DISCONNECT + RESUME) and hide connect button.
    /// Called when streaming is active and menu is shown.
    /// </summary>
    public void ShowStreamingButtons()
    {
        if (_connectButton != null)
            _connectButton.SetActive(false);

        if (_streamingButtonsContainer != null)
            _streamingButtonsContainer.SetActive(true);
    }

    /// <summary>
    /// Show connect button and hide streaming buttons.
    /// Called when disconnected or not streaming.
    /// </summary>
    public void ShowConnectButton()
    {
        if (_streamingButtonsContainer != null)
            _streamingButtonsContainer.SetActive(false);

        if (_connectButton != null)
            _connectButton.SetActive(true);
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
                // USB Mode: Require QR scan to get USB Tethering IP
                if (_isUsbMode)
                {
                    // USB mode requires QR scan to get the USB Tethering IP
                    if (!HasUsbTetheringIP)
                    {
                        Debug.LogWarning("[RTTRemoteMenu] USB mode: Please scan QR code to get USB Tethering IP.");
                        ShowTemporaryButtonText("SCAN QR CODE!", 2f);
                        return;
                    }

                    Debug.Log($"[RTTRemoteMenu] Connecting via USB ({_usbTetheringIP})...");
                    UpdateButtonText("CONNECTING...");

                    // Lock inputs during connection
                    VRInputFieldFactory.SetInteractable(_hostInput, false);
                    SetUsbToggleInteractable(false);
                    VRButtonFactory.SetInteractable(_qrButton, false);

                    await _viewModel.ConnectUSBAsync(DEFAULT_PORT, _usbTetheringIP);
                    break;
                }

                // WiFi Mode: Validate host and port
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
                Debug.Log("[RTTRemoteMenu] Connecting via WiFi...");
                UpdateButtonText("CONNECTING...");

                // Lock inputs during connection
                VRInputFieldFactory.SetInteractable(_hostInput, false);
                SetUsbToggleInteractable(false);
                VRButtonFactory.SetInteractable(_qrButton, false);

                await _viewModel.ConnectAsync(host, portNum);
                break;

            case ConnectionPhase.ConfiguringSettings:
                // NEW FLOW: Start button clicked - show progress on button, don't create ClusterRig yet
                Debug.Log("[RTTRemoteMenu] Starting with button progress...");

                // Build config from form dropdowns BEFORE locking inputs
                // This ensures we read the correct values from dropdowns
                var config = BuildConfigFromForm();

                // Save user selections
                SaveCurrentSelections();

                // Lock all inputs but keep menu visible
                LockAllInputs();

                // Reset and show initial progress immediately
                ResetButtonProgress();
                UpdateButtonText("Server Setup... 0%");

                if (config != null)
                {
                    OnSetupClicked?.Invoke();
                    // Start connection - progress will be shown on button text
                    // ClusterRig will be created later when streaming is ready
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
    /// Note: Resolution is now fixed at server's suggested value (server handles capture/resize).
    /// </summary>
    private StreamingConfig BuildConfigFromForm()
    {
        if (_viewModel == null || _viewModel.SuggestedConfig.Value == null) return null;

        // Get current suggested config as fallback
        var suggested = _viewModel.SuggestedConfig.Value;

        // Resolution is fixed from server (server captures at native and resizes to max 1440x810)
        int resW = suggested.resolutionWidth;
        int resH = suggested.resolutionHeight;

        // Parse Total Bitrate from dropdown (this is total for all monitors)
        int bitrateKbps = suggested.bitrateKbps;
        var bitrateRaw = Bitrate;
        var bitrate = RemotePreferences.CleanValue(bitrateRaw);
        Debug.Log($"[RTTRemoteMenu] BuildConfigFromForm: Bitrate raw='{bitrateRaw}', cleaned='{bitrate}', suggested={suggested.bitrateKbps}");
        if (!string.IsNullOrEmpty(bitrate))
        {
            var numStr = bitrate.Replace(" ", "").Replace("Mbps", "").Replace("mbps", "");
            if (int.TryParse(numStr, out int mbps))
            {
                bitrateKbps = mbps * 1000;
                Debug.Log($"[RTTRemoteMenu] BuildConfigFromForm: Parsed bitrate={mbps} Mbps -> {bitrateKbps} kbps");
            }
            else
            {
                Debug.LogWarning($"[RTTRemoteMenu] BuildConfigFromForm: Failed to parse bitrate from '{numStr}', using suggested={suggested.bitrateKbps}");
            }
        }
        else
        {
            Debug.LogWarning($"[RTTRemoteMenu] BuildConfigFromForm: Bitrate empty, using suggested={suggested.bitrateKbps}");
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
        // bitrateKbps is TOTAL for all monitors - server will divide by monitor count
        var config = new StreamingConfig
        {
            monitors = MonitorIndex + 1,
            resolutionWidth = resW,
            resolutionHeight = resH,
            bitrateKbps = bitrateKbps,  // TOTAL bitrate for all monitors
            fps = fpsVal,
            refreshRate = suggested.refreshRate,
            selectedCodec = suggested.selectedCodec
        };

        Debug.Log($"[RTTRemoteMenu] BuildConfigFromForm: {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}, {config.fps}fps, {config.bitrateKbps}kbps (total)");
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
    /// Show temporary button text, then revert to original after delay.
    /// </summary>
    private async void ShowTemporaryButtonText(string text, float duration)
    {
        if (_connectButtonText == null) return;

        string originalText = _connectButtonText.text;
        _connectButtonText.text = text;

        await System.Threading.Tasks.Task.Delay((int)(duration * 1000));

        // Only revert if text hasn't changed
        if (_connectButtonText != null && _connectButtonText.text == text)
        {
            _connectButtonText.text = originalText;
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
                // Show connect button (hide streaming buttons)
                ShowConnectButton();
                UpdateButtonText("CONNECT");
                InitializeDropdownsDisabled();
                VRInputFieldFactory.SetInteractable(_hostInput, !_isUsbMode);  // Disabled in USB mode (requires QR scan)
                SetUsbToggleInteractable(true);
                VRButtonFactory.SetInteractable(_qrButton, true);
                HideSidePanels();
                // Clear cached data to prevent stale data on next connection
                _cachedHardwareInfo = null;
                _cachedNetworkInfo = null;
                _cachedSuggestedConfig = null;
                // Reset button progress tracking
                ResetButtonProgress();
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
            case ConnectionPhase.ICENegotiating:
                // Don't set fixed text here - let progress handlers update with percentages
                // Progress handlers will show: "Server Setup... X%" or "Connecting... X%"
                LockAllInputs();
                break;

            case ConnectionPhase.ReadyToStream:
                UpdateButtonText("START REMOTE");
                break;

            case ConnectionPhase.StartingStream:
                UpdateButtonText("STARTING...");
                break;

            case ConnectionPhase.Streaming:
                // Show streaming buttons (DISCONNECT + RESUME) instead of connect button
                ShowStreamingButtons();
                // Hide side panels when streaming starts (menu is hidden by controller)
                HideSidePanels();
                break;

            case ConnectionPhase.Reconnecting:
                UpdateButtonText("RECONNECTING...");
                break;

            case ConnectionPhase.Error:
                // Treat Error same as Disconnected - show CONNECT, not RETRY
                ShowConnectButton();
                UpdateButtonText("CONNECT");
                InitializeDropdownsDisabled();
                VRInputFieldFactory.SetInteractable(_hostInput, !_isUsbMode);  // Disabled in USB mode (requires QR scan)
                SetUsbToggleInteractable(true);
                VRButtonFactory.SetInteractable(_qrButton, true);
                HideSidePanels();
                // Clear cached data on error
                _cachedHardwareInfo = null;
                _cachedNetworkInfo = null;
                _cachedSuggestedConfig = null;
                // Reset button progress tracking
                ResetButtonProgress();
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

    #region Button Progress Handlers
    private int _buttonServerProgress = 0;
    private int _buttonIceProgress = 0;

    /// <summary>
    /// Handle server setup progress for button text updates.
    /// Server progress maps to 0-50% of display.
    /// </summary>
    private void HandleServerSetupProgressForButton(int progress)
    {
        _buttonServerProgress = Mathf.Clamp(progress, 0, 100);
        UpdateButtonProgressText();
    }

    /// <summary>
    /// Handle ICE progress for button text updates.
    /// ICE progress maps to 50-100% of display.
    /// </summary>
    private void HandleMonitorIceProgressForButton(Dictionary<int, int> progressDict)
    {
        // Calculate average ICE progress across all monitors
        if (progressDict.Count == 0) return;

        int totalProgress = 0;
        foreach (var kvp in progressDict)
        {
            totalProgress += kvp.Value;
        }
        _buttonIceProgress = totalProgress / progressDict.Count;
        UpdateButtonProgressText();
    }

    /// <summary>
    /// Handle all monitors ready - show "CONNECTED" on button.
    /// </summary>
    private void HandleAllMonitorsReadyForButton()
    {
        UpdateButtonText("CONNECTED");
    }

    /// <summary>
    /// Update button text with current progress.
    /// Progress: 0-50% = Server Setup, 50-100% = ICE/PC
    /// Note: ConfiguringSettings is included because ConnectionViewModel doesn't set
    /// SendingDisplayConfig/AwaitingSetupComplete phases - it jumps from ConfiguringSettings to ICENegotiating.
    /// </summary>
    private void UpdateButtonProgressText()
    {
        // Only update if we're in the right phase (after START is clicked)
        // Note: Phase goes ConfiguringSettings -> ICENegotiating (skips SendingDisplayConfig/AwaitingSetupComplete)
        if (_currentPhase != ConnectionPhase.ConfiguringSettings &&
            _currentPhase != ConnectionPhase.SendingDisplayConfig &&
            _currentPhase != ConnectionPhase.AwaitingSetupComplete &&
            _currentPhase != ConnectionPhase.ICENegotiating)
        {
            return;
        }

        int displayProgress;
        string status;

        if (_buttonServerProgress < 100)
        {
            // Server setup phase: 0-50%
            displayProgress = _buttonServerProgress / 2;
            status = "Server Setup";
        }
        else
        {
            // ICE phase: 50-100%
            displayProgress = 50 + (_buttonIceProgress / 2);
            status = _buttonIceProgress < 100 ? "Connecting" : "Ready";
        }

        UpdateButtonText($"{status}... {displayProgress}%");
    }

    /// <summary>
    /// Reset button progress tracking.
    /// </summary>
    private void ResetButtonProgress()
    {
        _buttonServerProgress = 0;
        _buttonIceProgress = 0;
    }
    #endregion

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

        // === Style ===
        // Style dropdown uses simple 2 options, no "Recommended" logic needed
        // Just ensure it's enabled with the options already set in CreateGrid
        var styleOptions = new List<string>(STYLE_OPTIONS);
        VRDropdownFactory.SetOptions(_styleDropdown, styleOptions, 0);  // Default: Flat Planar

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
        Debug.Log($"[RTTRemoteMenu] Selected indices: Mon={suggestedMonitorIndex}, Bitrate={suggestedBitrateIndex}, FPS={suggestedFpsIndex}");
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
    /// Recalculates suggested config for Bitrate, FPS based on new monitor count.
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
    /// Handle Style dropdown selection changed.
    /// Applies Flat Planar or Curved Surround style to WorldPanelClusterRig immediately.
    /// </summary>
    private void HandleStyleChanged(int index, string value)
    {
        Debug.Log($"[RTTRemoteMenu] Style changed: index={index}, value={value}");

        bool isCurvedSurround = index == 1;  // 0=Flat Planar, 1=Curved Surround

        // Find WorldPanelClusterRig and apply style change
        var clusterRig = FindObjectOfType<WorldPanelClusterRig>();
        if (clusterRig != null)
        {
            clusterRig.SetStyle(isCurvedSurround);
            Debug.Log($"[RTTRemoteMenu] Applied style: {(isCurvedSurround ? "Curved Surround" : "Flat Planar")}");
        }
        else
        {
            Debug.Log("[RTTRemoteMenu] WorldPanelClusterRig not found - style will apply on next stream start");
        }
    }

    /// <summary>
    /// Handle FPS dropdown selection changed.
    /// Sends update_config to server if currently streaming.
    /// </summary>
    private void HandleFpsChanged(int index, string value)
    {
        Debug.Log($"[RTTRemoteMenu] FPS changed: index={index}, value={value}");

        // Parse FPS value
        int fpsVal = 60; // default
        if (!string.IsNullOrEmpty(value))
        {
            var numStr = value.Replace(" ", "").Replace("FPS", "").Replace("fps", "");
            if (int.TryParse(numStr, out int f))
            {
                fpsVal = f;
            }
        }

        // If streaming, send update_config
        if (_currentPhase == ConnectionPhase.Streaming && _viewModel != null)
        {
            _ = _viewModel.UpdateConfigAsync(fpsVal, null);
            Debug.Log($"[RTTRemoteMenu] Sent update_config: fps={fpsVal}");
        }
    }

    /// <summary>
    /// Handle Bitrate dropdown selection changed.
    /// Sends update_config to server if currently streaming.
    /// </summary>
    private void HandleBitrateChanged(int index, string value)
    {
        Debug.Log($"[RTTRemoteMenu] Bitrate changed: index={index}, value={value}");

        // Parse Bitrate value (total for all monitors)
        int bitrateKbps = 20000; // default
        if (!string.IsNullOrEmpty(value))
        {
            var numStr = value.Replace(" ", "").Replace("Mbps", "").Replace("mbps", "");
            if (int.TryParse(numStr, out int mbps))
            {
                bitrateKbps = mbps * 1000;
            }
        }

        // If streaming, send update_config
        if (_currentPhase == ConnectionPhase.Streaming && _viewModel != null)
        {
            _ = _viewModel.UpdateConfigAsync(null, bitrateKbps);
            Debug.Log($"[RTTRemoteMenu] Sent update_config: bitrateKbps={bitrateKbps} (total)");
        }
    }


    /// <summary>
    /// Recalculate suggested config based on selected monitor count.
    /// Updates Bitrate, FPS dropdowns with new "(Recommended)" positions.
    /// </summary>
    private void RecalculateSuggestionsForMonitorCount(int monitorCount)
    {
        if (_cachedNetworkInfo == null || _cachedHardwareInfo == null) return;

        // Get current selections before updating options (only bitrate and FPS need recalculation)
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

        // === Calculate recommended FPS based on ping and VRAM ===
        // More monitors = potentially lower FPS to reduce encoder load
        // Lower VRAM = lower FPS
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


        // Bitrate - auto-select recommended
        var bitrateOptions = BuildOptionsWithRecommended(BITRATE_OPTIONS, suggestedBitrateIndex);
        VRDropdownFactory.SetOptions(_bitrateDropdown, bitrateOptions, suggestedBitrateIndex);

        // FPS - auto-select recommended
        var fpsOptions = BuildOptionsWithRecommended(FPS_OPTIONS, suggestedFpsIndex);
        VRDropdownFactory.SetOptions(_fpsDropdown, fpsOptions, suggestedFpsIndex);

        Debug.Log($"[RTTRemoteMenu] Auto-selected for {monitorCount} monitors: Bitrate={BITRATE_OPTIONS[suggestedBitrateIndex]}, FPS={FPS_OPTIONS[suggestedFpsIndex]}");
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
        VRDropdownFactory.SetOptions(_styleDropdown, new List<string> { "Flat Planar", "Curved Surround" }, 0);
        VRDropdownFactory.SetOptions(_bitrateDropdown, placeholder, 0);
        VRDropdownFactory.SetOptions(_fpsDropdown, placeholder, 0);

        VRDropdownFactory.SetInteractable(_monitorsDropdown, false);
        VRDropdownFactory.SetInteractable(_styleDropdown, false);  // Style follows same lock logic
        VRDropdownFactory.SetInteractable(_bitrateDropdown, false);
        VRDropdownFactory.SetInteractable(_fpsDropdown, false);
    }


    /// <summary>
    /// Enable all dropdowns for interaction.
    /// </summary>
    private void EnableDropdowns()
    {
        VRDropdownFactory.SetInteractable(_monitorsDropdown, true);
        VRDropdownFactory.SetInteractable(_styleDropdown, true);
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
        VRDropdownFactory.SetInteractable(_styleDropdown, false);
        VRDropdownFactory.SetInteractable(_bitrateDropdown, false);
        VRDropdownFactory.SetInteractable(_fpsDropdown, false);

        // Khóa host input và USB toggle
        VRInputFieldFactory.SetInteractable(_hostInput, false);
        SetUsbToggleInteractable(false);

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
        VRDropdownFactory.SetInteractable(_styleDropdown, true);
        VRDropdownFactory.SetInteractable(_bitrateDropdown, true);
        VRDropdownFactory.SetInteractable(_fpsDropdown, true);

        // Mở khóa host input (only if not USB mode) và USB toggle
        VRInputFieldFactory.SetInteractable(_hostInput, !_isUsbMode);
        SetUsbToggleInteractable(true);

        // Mở khóa QR button
        VRButtonFactory.SetInteractable(_qrButton, true);

        Debug.Log("[RTTRemoteMenu] All inputs unlocked");
    }

    /// <summary>
    /// Set USB toggle interactable state.
    /// </summary>
    private void SetUsbToggleInteractable(bool interactable)
    {
        if (_usbModeToggle == null) return;

        // Find checkbox button inside toggle
        var checkboxBtn = _usbModeToggle.transform.Find("ToggleContainer/Btn_");
        if (checkboxBtn != null)
        {
            VRButtonFactory.SetInteractable(checkboxBtn.gameObject, interactable);
        }
    }

    /// <summary>
    /// Load saved host and USB mode from preferences and apply to inputs.
    /// </summary>
    private void LoadSavedHostPort()
    {
        var prefs = RemotePreferences.Load();
        if (!string.IsNullOrEmpty(prefs.lastHost))
            VRInputFieldFactory.SetValue(_hostInput, prefs.lastHost);

        // Load USB mode state
        _isUsbMode = prefs.usbMode;
        UpdateUsbModeToggleVisual();

        // USB mode requires QR scan - disable manual host input
        VRInputFieldFactory.SetInteractable(_hostInput, !_isUsbMode);
    }

    /// <summary>
    /// Save current dropdown selections and host/USB mode to preferences.
    /// Called when START is clicked (previously SETUP REMOTE).
    /// </summary>
    private void SaveCurrentSelections()
    {
        var prefs = new RemotePreferences
        {
            monitors = MonitorIndex,
            resolution = Style,  // Now stores style ("Flat Planar" or "Curved Surround")
            bitrate = RemotePreferences.CleanValue(Bitrate),
            fps = RemotePreferences.CleanValue(FPS),
            lastHost = VRInputFieldFactory.GetValue(_hostInput),  // Save actual host input value
            lastPort = Port,
            usbMode = _isUsbMode
        };
        prefs.Save();
        Debug.Log($"[RTTRemoteMenu] Saved preferences: {prefs.monitors}mon, style={prefs.resolution}, {prefs.bitrate}, {prefs.fps}, USB={prefs.usbMode}");
    }
    #endregion

    #region Side Panels
    /// <summary>
    /// Create side panels for hardware and network info.
    /// Panels are hidden by default and shown when connected.
    /// Uses sphere positioning: panels placed on sphere surface with camera as center,
    /// radius = distance from camera to main panel, facing camera.
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

        Debug.Log($"[RTTRemoteMenu] Creating Hardware Info Panel (left) with sphere positioning...");
        // Hardware Info Panel (Left) - sphere positioning
        PlaceSidePanelOnSphere("HardwareInfoPanel", -1, mainPanelWidth, sideWidth, sideHeight,
            gapMeters, RTTInfoSidePanel.PanelType.HardwareInfo, ref _hardwareFrame);
        Debug.Log($"[RTTRemoteMenu] Hardware frame created: {_hardwareFrame != null}");

        Debug.Log($"[RTTRemoteMenu] Creating Network Info Panel (right) with sphere positioning...");
        // Network Info Panel (Right) - sphere positioning
        PlaceSidePanelOnSphere("NetworkInfoPanel", 1, mainPanelWidth, sideWidth, sideHeight,
            gapMeters, RTTInfoSidePanel.PanelType.NetworkInfo, ref _networkFrame);
        Debug.Log($"[RTTRemoteMenu] Network frame created: {_networkFrame != null}");

        Debug.Log($"[RTTRemoteMenu] Side panels created ({sideWidth:F2}m x {sideHeight:F2}m) gap={gapMeters}m with sphere positioning (hidden)");
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

        // Create RTTMenuFrame for the side panel with unique name
        frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
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
    /// Place a side panel on sphere surface with camera as center.
    /// Sphere radius = distance from camera to main panel center.
    /// Panel faces camera (vector from panel to camera is perpendicular to panel surface).
    /// </summary>
    /// <param name="side">-1 for left, +1 for right</param>
    private void PlaceSidePanelOnSphere(string name, int side, float mainWidth, float sideWidth, float sideHeight,
        float gap, RTTInfoSidePanel.PanelType type, ref RTTMenuFrame frameRef)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[RTTRemoteMenu] PlaceSidePanelOnSphere: No main camera found!");
            return;
        }

        if (_menuFrame == null)
        {
            Debug.LogError("[RTTRemoteMenu] PlaceSidePanelOnSphere: No app frame (_menuFrame) found!");
            return;
        }

        // Step 1: Calculate where the INNER edge of side panel should be
        // Inner edge lies on the same plane as main panel (Z=0 in main panel's local space)
        // Inner edge = main panel edge + gap
        Vector3 mainRight = _menuFrame.transform.right;
        float innerEdgeOffset = (mainWidth / 2f) + gap;
        Vector3 innerEdgePos = _menuFrame.transform.position + mainRight * innerEdgeOffset * side;

        // Step 2: Calculate rotation FIRST (face camera from inner edge position)
        Vector3 toCameraHorizontal = cam.transform.position - innerEdgePos;
        toCameraHorizontal.y = 0; // Project to horizontal plane for upright panel
        if (toCameraHorizontal.sqrMagnitude < 0.001f)
        {
            toCameraHorizontal = -cam.transform.forward;
            toCameraHorizontal.y = 0;
        }
        Quaternion panelRotation = Quaternion.LookRotation(-toCameraHorizontal.normalized, Vector3.up);

        // Step 3: Calculate center position from inner edge
        // Center = inner edge + panel's right * (sideWidth/2) * side
        // Panel's right points AWAY from main panel (toward outer edge)
        Vector3 panelRight = panelRotation * Vector3.right;
        Vector3 panelPos = innerEdgePos + panelRight * (sideWidth / 2f) * side;

        // Keep same Y as main panel
        panelPos.y = _menuFrame.transform.position.y;

        // Calculate logical width based on aspect ratio (same resolution density as main panel)
        float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

        // Create RTTMenuFrame for the side panel with unique name
        frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
        frameRef.transform.position = panelPos;
        frameRef.transform.rotation = panelRotation;
        frameRef.transform.localScale = Vector3.one;

        // IMMEDIATELY set invisible BEFORE Start() runs
        frameRef.SetVisible(false);

        // Configure frame appearance (smaller margins for side panels)
        frameRef.SetContentMargins(40f, 40f, 30f, 30f);
        frameRef.SetFloatingDataEnabled(true, 10); // Fewer particles for smaller panel

        // Register with ZoomController for updates on zoom change
        var zoomController = VirtualObjectsZoomController.Instance;
        if (zoomController != null)
        {
            zoomController.RegisterSidePanel(frameRef, _menuFrame.transform, side, mainWidth, sideWidth, gap);
            Debug.Log($"[RTTRemoteMenu] Registered {name} with VirtualObjectsZoomController");
        }

        Debug.Log($"[RTTRemoteMenu] PlaceSidePanel: mainPos={_menuFrame.transform.position}, panelPos={panelPos}");

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

            // Set alpha to match main menu (0.15f) instead of default (0.65f)
            frame.SetGlassAlpha(0.15f);

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
        // Don't show side panels if disconnected or error
        if (_currentPhase == ConnectionPhase.Disconnected || _currentPhase == ConnectionPhase.Error)
        {
            return;
        }
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
        // Don't show side panels if disconnected or error
        if (_currentPhase == ConnectionPhase.Disconnected || _currentPhase == ConnectionPhase.Error)
        {
            Debug.Log($"[RTTRemoteMenu] ShowSidePanels blocked due to phase {_currentPhase}");
            return;
        }

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
            spacing = hasIcon && hasText ? 36f : 18f,
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
    /// Bind IP based on current USB Mode state.
    /// </summary>
    public void SetConfig(QRScannerConfig config)
    {
        if (config == null) return;

        Debug.Log($"[RTTRemoteMenu] QR Config received: {config}");

        // Store USB Tethering IP if available
        if (config.HasUsbIP)
        {
            _usbTetheringIP = config.usbIP;
            UpdateUsbModeLabel();
            Debug.Log($"[RTTRemoteMenu] USB IP available: {_usbTetheringIP}");
        }

        // Set Host based on USB Mode state
        // If USB Mode is ON and USB IP available → use USB IP
        // Otherwise → use WiFi IP
        string hostToUse = (_isUsbMode && config.HasUsbIP) ? config.usbIP : config.ip;

        if (!string.IsNullOrEmpty(hostToUse))
        {
            VRInputFieldFactory.SetValue(_hostInput, hostToUse);
            Debug.Log($"[RTTRemoteMenu] Host set to: {hostToUse} (USB Mode: {_isUsbMode})");
        }

        // Note: Port from QR is ignored - using fixed DEFAULT_PORT (8288)
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
        // Normal behavior during preparation - coroutines will be paused and resumed on enable
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

            // Unsubscribe button progress events
            _viewModel.ServerSetupProgress.OnChanged -= HandleServerSetupProgressForButton;
            _viewModel.MonitorIceProgress.OnChanged -= HandleMonitorIceProgressForButton;
            _viewModel.OnAllMonitorsReady -= HandleAllMonitorsReadyForButton;
        }

        // Unregister side panels from ZoomController
        var zoomController = VirtualObjectsZoomController.Instance;
        if (zoomController != null)
        {
            if (_hardwareFrame != null)
                zoomController.UnregisterSidePanel(_hardwareFrame);
            if (_networkFrame != null)
                zoomController.UnregisterSidePanel(_networkFrame);
        }

        // Destroy side panels
        if (_hardwareInfoPanel != null)
            Destroy(_hardwareInfoPanel.gameObject);
        if (_networkInfoPanel != null)
            Destroy(_networkInfoPanel.gameObject);
    }
    #endregion
}
