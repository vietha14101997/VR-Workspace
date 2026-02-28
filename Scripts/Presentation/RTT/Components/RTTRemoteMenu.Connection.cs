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
    public partial class RTTRemoteMenu
    {
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

            // For ultrawide modes: 1 virtual monitor, override resolution
            bool isUltrawide = IsUltrawide;
            int monitors = isUltrawide ? 1 : MonitorIndex + 1;

            if (isUltrawide)
            {
                // Ultrawide: 2560x1080, Super Ultrawide: 3840x1080
                resW = MonitorIndex == 3 ? 2560 : 3840;
                resH = 1080;
            }

            // Create config from user selections
            // bitrateKbps is TOTAL for all monitors - server will divide by monitor count
            var config = new StreamingConfig
            {
                monitors = monitors,
                resolutionWidth = resW,
                resolutionHeight = resH,
                bitrateKbps = bitrateKbps,  // TOTAL bitrate for all monitors
                fps = fpsVal,
                refreshRate = suggested.refreshRate,
                selectedCodec = suggested.selectedCodec,
                monitorType = MonitorType
            };

            Debug.Log($"[RTTRemoteMenu] BuildConfigFromForm: {config.monitors}mon ({config.monitorType}) @ {config.resolutionWidth}x{config.resolutionHeight}, {config.fps}fps, {config.bitrateKbps}kbps (total)");
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
                    UpdateButtonText("CONNECTING...");
                    // Connect succeeded — save host/port/usbMode immediately
                    SaveHostPreference();
                    break;

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
                    UpdateButtonText("STARTING...");
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
        #endregion

        #region Button Progress Handlers
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

        #region Hardware / Network Info Handlers
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

            // Mathematical solution to satisfy BOTH conditions:
            // 1. Inner edge lies exactly on main panel's plane
            // 2. Panel surface is perpendicular to vector (center -> camera)
            //
            // Solution: r = sqrt(|E-C|² - w²), θ = atan2(b,a) + arcsin(w*side/|E-C|)

            // Step 1: Calculate inner edge position on main panel's plane
            Vector3 mainRight = _menuFrame.transform.right;
            float innerEdgeOffset = (mainWidth / 2f) + gap;
            Vector3 innerEdgePos = _menuFrame.transform.position + mainRight * innerEdgeOffset * side;

            // Step 2: Calculate in horizontal plane (XZ)
            float w = sideWidth / 2f; // half width
            Vector3 cameraPos = cam.transform.position;

            // Vector from camera to inner edge (horizontal only)
            float a = innerEdgePos.x - cameraPos.x;
            float b = innerEdgePos.z - cameraPos.z;
            float distSq = a * a + b * b;
            float dist = Mathf.Sqrt(distSq);

            Vector3 panelPos;
            Quaternion panelRotation;

            // Edge case: camera too close to inner edge
            if (dist < 0.001f)
            {
                panelPos = innerEdgePos + mainRight * w * side;
                panelPos.y = _menuFrame.transform.position.y;
                panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
            }
            else
            {
                // Step 3: Calculate distance from camera to panel center
                float rSq = distSq - w * w;
                if (rSq < 0.0001f) rSq = 0.0001f;
                float r = Mathf.Sqrt(rSq);

                // Step 4: Calculate direction angle θ
                // θ = atan2(b, a) - arcsin(w * side / dist)
                float alpha = Mathf.Atan2(b, a);
                float sinArg = Mathf.Clamp((w * side) / dist, -1f, 1f);
                float theta = alpha - Mathf.Asin(sinArg);

                // Step 5: Calculate panel center position
                float dx = Mathf.Cos(theta);
                float dz = Mathf.Sin(theta);
                panelPos = new Vector3(
                    cameraPos.x + dx * r,
                    _menuFrame.transform.position.y,
                    cameraPos.z + dz * r
                );

                // Step 6: Calculate rotation to face camera from center
                Vector3 toCamera = new Vector3(cameraPos.x - panelPos.x, 0, cameraPos.z - panelPos.z);
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    panelRotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
                else
                {
                    panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
                }
            }

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
            RTTTaskbar taskbar = FindFirstObjectByType<RTTTaskbar>();

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
        #endregion
    }
}
