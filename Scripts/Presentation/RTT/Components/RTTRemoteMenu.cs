using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Core;
using VRWorkspace.Streaming;
using VRWorkspace.ViewModels;
using VRWorkspace.Panel;
using VRWorkspace.QRScanner;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// RTT-based Remote Desktop connection menu.
    /// Features: Host/Port inputs, Monitors/Resolution/Bitrate/FPS dropdowns, QR scanner.
    /// Easy to use: Set themeColor, subscribe to events, call BuildUI().
    ///
    /// V2 Protocol: Supports phased connection flow with button state changes:
    /// - "Connect" → "Setup Remote" → "Start Remote"
    /// </summary>
    public partial class RTTRemoteMenu : MonoBehaviour
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
        private GameObject _hostInput;  // Hidden, only shown in Internet mode for manual IP
        private GameObject _transportRadioGroup; // LAN / USB / Internet radio buttons
        private GameObject[] _radioButtons = new GameObject[3]; // 0=LAN, 1=USB, 2=Internet
        private int _selectedTransport = 0; // 0=LAN, 1=USB, 2=Internet
        private RTTPopupInputable _tokenPopup; // Token input popup for internet mode
        private RTTPopupInputable _serverPopup; // Server selection popup for multi-server LAN discovery
        private bool _preferenceSaveEnabled = false;  // Guard: only save when dropdowns are properly configured
        private string _usbTetheringIP = null;  // USB Tethering IP from QR scan (for full USB streaming)
        private string _tunnelUrl = null;  // Cloudflare Tunnel URL from QR scan (for zero-config internet)
        private const int DEFAULT_PORT = 8288;
        private GameObject _monitorsDropdown;
        private GameObject _modeDropdown;
        private GameObject _resolutionDropdown;
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
        private static readonly string[] MONITOR_OPTIONS = { "1 Monitor", "2 Monitors", "3 Monitors", "Ultrawide", "Super Ultrawide" };
        private static readonly string[] MODE_OPTIONS = { "Classic", "Spatial" };
        private static readonly string[] RESOLUTION_OPTIONS = { "720p", "1080p", "1440p" };
        private static readonly string[] FPS_OPTIONS = { "30 FPS", "60 FPS", "120 FPS" };

        // Button progress tracking (used in Button Progress Handlers region in Connection.cs)
        private int _buttonServerProgress = 0;
        private int _buttonIceProgress = 0;
        #endregion

        #region Public Accessors (Easy Form Data Access)
        /// <summary>
        /// Get connection host based on transport mode:
        /// - LAN: empty (auto-discovery) or manual input
        /// - USB: USB Tethering IP from QR scan
        /// - Internet: manual host input or tunnel URL
        /// </summary>
        public string Host {
            get {
                if (_selectedTransport == 1 && HasUsbTetheringIP) return _usbTetheringIP;
                return VRInputFieldFactory.GetValue(_hostInput);
            }
        }
        public string Port => DEFAULT_PORT.ToString();
        // Backward compat properties — derived from _selectedTransport
        private bool _isUsbMode => _selectedTransport == 1;
        private bool _isInternetMode => _selectedTransport == 2;
        public bool IsUsbMode => _selectedTransport == 1;
        public bool IsInternetMode => _selectedTransport == 2;
        public string UsbTetheringIP => _usbTetheringIP;
        public bool HasUsbTetheringIP => !string.IsNullOrEmpty(_usbTetheringIP);
        public int MonitorIndex => VRDropdownFactory.GetSelectedIndex(_monitorsDropdown);
        public string Mode => VRDropdownFactory.GetSelectedValue(_modeDropdown);
        public int ModeIndex => VRDropdownFactory.GetSelectedIndex(_modeDropdown);
        public string Resolution => VRDropdownFactory.GetSelectedValue(_resolutionDropdown);
        public string FPS => VRDropdownFactory.GetSelectedValue(_fpsDropdown);

        /// <summary>
        /// Monitor type based on selected monitor option.
        /// "standard" for 1/2/3 Monitors, "ultrawide" for Ultrawide, "super_ultrawide" for Super Ultrawide.
        /// </summary>
        public string MonitorType => MonitorIndex switch {
            3 => "ultrawide",
            4 => "super_ultrawide",
            _ => "standard"
        };

        /// <summary>True if Ultrawide or Super Ultrawide is selected.</summary>
        public bool IsUltrawide => MonitorIndex >= 3;
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

            // Grid 2x2 (Monitors, Resolution, Resolution, FPS)
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
            // TODO: Replace with VContainer [Inject] when LifetimeScopes are wired in scene
#pragma warning disable CS0618
            if (!ServiceLocator.TryGet<ConnectionViewModel>(out _viewModel))
            {
                _viewModel = new ConnectionViewModel();
                ServiceLocator.Register(_viewModel);
            }
#pragma warning restore CS0618

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

        #region Transport Mode Row (LAN / USB / Internet)
        private static readonly string[] TRANSPORT_LABELS = { "LAN", "USB", "Internet" };

        private void CreateInputRow(Transform parent, float x, float y, float w, float h, float gapX)
        {
            var row = CreateContainer(parent, "InputRow", x, y, w, h);

            // Title label "Connection" at very top
            var labelObj = new GameObject("TransportLabel");
            labelObj.transform.SetParent(row.transform, false);
            var labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0.8f);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
            labelTMP.text = "Connection";
            labelTMP.fontSize = LABEL_FONT_SIZE;
            labelTMP.color = themeColor;
            labelTMP.alignment = TextAlignmentOptions.Left;
            labelTMP.fontStyle = FontStyles.Bold;
            labelTMP.raycastTarget = false;
            if (customFont) labelTMP.font = customFont;

            // Checkbox row — anchored to bottom 40% (3x gap from title vs old 60%)
            var checkboxRow = new GameObject("CheckboxRow");
            checkboxRow.transform.SetParent(row.transform, false);
            var checkboxRowRT = checkboxRow.AddComponent<RectTransform>();
            checkboxRowRT.anchorMin = new Vector2(0, 0);
            checkboxRowRT.anchorMax = new Vector2(1, 0.4f);
            checkboxRowRT.offsetMin = Vector2.zero;
            checkboxRowRT.offsetMax = Vector2.zero;

            // Divide row into 3 equal columns
            float checkboxH = h * 0.4f;
            float checkboxSize = checkboxH * 0.85f * 1.4f; // +40% bigger checkboxes

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                float colMin = i / 3f;
                float colMax = (i + 1) / 3f;
                _radioButtons[i] = CreateTransportToggle(checkboxRow.transform, checkboxSize, checkboxH, idx);
                var btnRT = _radioButtons[i].GetComponent<RectTransform>();
                btnRT.anchorMin = new Vector2(colMin, 0);
                btnRT.anchorMax = new Vector2(colMax, 1);
                btnRT.offsetMin = Vector2.zero;
                btnRT.offsetMax = Vector2.zero;
            }

            // Hidden host input (stores IP for Internet mode / QR scan)
            _hostInput = VRInputFieldFactory.CreateLabeledInputField(
                row.transform, 0.01f,
                "", "", accentColor,
                onEndEdit: null,
                labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont);
            _hostInput.SetActive(false);

            UpdateTransportRadioVisuals();
        }

        /// <summary>
        /// Create a transport toggle: old-style VRButtonFactory checkbox + label text beside it.
        /// Container fills its anchor-based column; hover scales the inner content group.
        /// </summary>
        private GameObject CreateTransportToggle(Transform parent, float checkboxSize, float height, int index)
        {
            string label = TRANSPORT_LABELS[index];
            int idx = index;

            // Container fills anchor-based column (set by caller)
            var container = new GameObject($"Transport_{label}");
            container.transform.SetParent(parent, false);
            container.AddComponent<RectTransform>();

            // === Checkbox (VRButtonFactory style) ===
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
                popAmount = 0f,
                hoverScaleAmount = 0.08f
            };

            var checkboxBtn = VRButtonFactory.CreateButton(container.transform, checkboxConfig, () => SelectTransport(idx));
            var checkboxBtnRT = checkboxBtn.GetComponent<RectTransform>();
            checkboxBtnRT.anchorMin = new Vector2(0, 0.5f);
            checkboxBtnRT.anchorMax = new Vector2(0, 0.5f);
            checkboxBtnRT.pivot = new Vector2(0, 0.5f);
            checkboxBtnRT.anchoredPosition = Vector2.zero;

            // Checkmark inside checkbox (hidden by default)
            var checkmark = new GameObject("Checkmark");
            checkmark.transform.SetParent(checkboxBtn.transform, false);
            var checkmarkRT = checkmark.AddComponent<RectTransform>();
            checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkmarkRT.offsetMin = Vector2.zero;
            checkmarkRT.offsetMax = Vector2.zero;
            var checkmarkImg = checkmark.AddComponent<Image>();
            checkmarkImg.sprite = Resources.Load<Sprite>("icon_check_mark");
            checkmarkImg.color = Color.white;
            checkmarkImg.preserveAspect = true;
            checkmarkImg.raycastTarget = false;
            checkmark.SetActive(false);

            // === Label — sized to text content, not full column ===
            // Estimate width: ~INPUT_FONT_SIZE * 0.6 per char + padding
            float estimatedTextW = label.Length * INPUT_FONT_SIZE * 0.65f + 16f;

            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(container.transform, false);
            var labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0.5f);
            labelRT.anchorMax = new Vector2(0, 0.5f);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.anchoredPosition = new Vector2(checkboxSize + 24f, 0);
            labelRT.sizeDelta = new Vector2(estimatedTextW, checkboxSize);

            // TMP text — raycastTarget = TRUE so GraphicRaycaster can hit it
            var textTMP = labelObj.AddComponent<TextMeshProUGUI>();
            textTMP.text = label;
            textTMP.enableAutoSizing = true;
            textTMP.fontSizeMin = 14f;
            textTMP.fontSizeMax = INPUT_FONT_SIZE;
            textTMP.color = themeColor;
            textTMP.alignment = TextAlignmentOptions.Left;
            textTMP.verticalAlignment = VerticalAlignmentOptions.Middle;
            textTMP.fontStyle = FontStyles.Bold;
            textTMP.enableWordWrapping = false;
            textTMP.overflowMode = TextOverflowModes.Ellipsis;
            textTMP.raycastTarget = true; // Catches GraphicRaycaster hits
            if (customFont) textTMP.font = customFont;

            // Click handler on label
            var labelBtn = labelObj.AddComponent<UnityEngine.UI.Button>();
            labelBtn.transition = UnityEngine.UI.Selectable.Transition.None;
            labelBtn.onClick.AddListener(() => SelectTransport(idx));

            // Hover effect: color brighten (no scale)
            Color hoverTextColor = themeColor * 1.4f; // Brighter
            hoverTextColor.a = 1f;
            var labelHover = labelObj.AddComponent<VRWorkspace.UI.HoverEffects.HoverEffectController>();
            labelHover.AddEffect(new VRWorkspace.UI.HoverEffects.ColorHoverEffect()
                .WithTargetChild("")  // Target self (the TMP on this object)
                .WithTargetType(VRWorkspace.UI.HoverEffects.ColorHoverEffect.TargetType.Text)
                .WithHoverColor(hoverTextColor));

            // === Cross-link hover: checkbox ↔ label ===
            var checkboxHover = checkboxBtn.transform.Find("HitArea")?.GetComponent<VRWorkspace.UI.HoverEffects.HoverEffectController>();
            if (checkboxHover != null)
            {
                // Hover checkbox → also hover label
                checkboxHover.OnHoverStateChanged += (hovered) =>
                    labelHover.SetForceHover(hovered);
                // Hover label → also hover checkbox
                labelHover.OnHoverStateChanged += (hovered) =>
                    checkboxHover.SetForceHover(hovered);
            }

            return container;
        }

        /// <summary>
        /// Select a transport mode (0=LAN, 1=USB, 2=Internet). Single-select radio behavior.
        /// </summary>
        private void SelectTransport(int index)
        {
            if (index == _selectedTransport) return;
            _selectedTransport = index;
            UpdateTransportRadioVisuals();
            Debug.Log($"[RTTRemoteMenu] Transport: {TRANSPORT_LABELS[index]}");
        }

        /// <summary>
        /// Update all transport toggles: show checkmark on selected, dim unselected.
        /// </summary>
        private void UpdateTransportRadioVisuals()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_radioButtons[i] == null) continue;
                bool isSelected = i == _selectedTransport;

                // Checkmark visibility
                var checkmark = _radioButtons[i].transform.Find("Btn_/Checkmark");
                if (checkmark != null)
                    checkmark.gameObject.SetActive(isSelected);

                // Checkbox background alpha
                var bg = _radioButtons[i].transform.Find("Btn_/HitArea/Visuals/Background");
                if (bg != null)
                {
                    var img = bg.GetComponent<Image>();
                    if (img != null && img.material != null)
                        img.material.SetFloat("_GlassAlpha", isSelected ? 0.35f : 0.15f);
                }

                // Disable/enable click only (no visual dimming)
                var cbBtnComp = _radioButtons[i].transform.Find("Btn_/HitArea")?.GetComponent<UnityEngine.UI.Button>();
                if (cbBtnComp != null)
                    cbBtnComp.interactable = !isSelected;

                var lblBtnComp = _radioButtons[i].transform.Find("Label")?.GetComponent<UnityEngine.UI.Button>();
                if (lblBtnComp != null)
                    lblBtnComp.interactable = !isSelected;

                // Force hover ON permanently for selected, release for unselected
                var cbHover = _radioButtons[i].transform.Find("Btn_/HitArea")?.GetComponent<VRWorkspace.UI.HoverEffects.HoverEffectController>();
                var lblHover = _radioButtons[i].transform.Find("Label")?.GetComponent<VRWorkspace.UI.HoverEffects.HoverEffectController>();
                cbHover?.SetForceHover(isSelected);
                lblHover?.SetForceHover(isSelected);
            }
        }

        /// <summary>
        /// Set all transport toggles interactable/non-interactable.
        /// </summary>
        private void SetTransportRadioInteractable(bool interactable)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_radioButtons[i] == null) continue;

                bool isSelected = i == _selectedTransport;

                // Checkbox — disable click only (no visual dimming), selected stays disabled
                var cbBtn = _radioButtons[i].transform.Find("Btn_/HitArea")?.GetComponent<UnityEngine.UI.Button>();
                if (cbBtn != null)
                    cbBtn.interactable = interactable && !isSelected;

                // Label — selected stays disabled
                var lblBtn = _radioButtons[i].transform.Find("Label")?.GetComponent<UnityEngine.UI.Button>();
                if (lblBtn != null)
                    lblBtn.interactable = interactable && !isSelected;
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
            var monitorOptions = new List<string> { "1 Monitor", "2 Monitors", "3 Monitors", "Ultrawide", "Super Ultrawide" };
            var modeOptions = new List<string> { "Classic", "Spatial" };
            var resolutionOptions = new List<string> { "720p", "1080p", "1440p" };
            var fpsOptions = new List<string> { "30 FPS", "60 FPS", "120 FPS" };

            // Row 1 (top)
            float row1Y = dropdownH + gapY;

            _monitorsDropdown = VRDropdownFactory.CreateIconDropdown(
                grid.transform, cellW,
                "Monitors", LoadIcon("monitor"), themeColor,
                monitorOptions, 1,
                onValueChanged: HandleMonitorSelectionChanged,
                labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
            PositionElement(_monitorsDropdown, 0, row1Y);

            // Mode dropdown: Classic (active) / Spatial (locked for future)
            _modeDropdown = VRDropdownFactory.CreateIconDropdown(
                grid.transform, cellW,
                "Mode", LoadIcon("resolution"), accentColor,
                modeOptions, 0,  // Default: Classic
                onValueChanged: HandleModeChanged,
                labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
            PositionElement(_modeDropdown, cellW + gapX, row1Y);

            // Row 2 (bottom) - Resolution
            _resolutionDropdown = VRDropdownFactory.CreateIconDropdown(
                grid.transform, cellW,
                "Resolution", LoadIcon("resolution"), accentColor,
                resolutionOptions, 1, // Default 1080p
                onValueChanged: HandleResolutionChanged,
                labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
            PositionElement(_resolutionDropdown, 0, 0);

            _fpsDropdown = VRDropdownFactory.CreateIconDropdown(
                grid.transform, cellW,
                "FPS", LoadIcon("fps"), themeColor,
                fpsOptions, 1, // Default 60 FPS
                onValueChanged: HandleFpsChanged,
                labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
            PositionElement(_fpsDropdown, cellW + gapX, 0);
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
            txt.textWrappingMode = TextWrappingModes.NoWrap;
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

}
