using UnityEngine;
using System;
using System.Collections.Generic;
using VRWorkspace.Streaming;
using VRWorkspace.ViewModels;
using VRWorkspace.Panel;
using VRWorkspace.UI.Components;

namespace VRWorkspace.UI.RTT.Components
{
    public partial class RTTRemoteMenu
    {
        #region Apply Suggested Config
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
        #endregion

        #region Dropdown Change Handlers
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
            var clusterRig = FindFirstObjectByType<WorldPanelClusterRig>();
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
        /// Called when state changes to Ready (STARTING...).
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

        #region Find Option Index
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
    }
}
