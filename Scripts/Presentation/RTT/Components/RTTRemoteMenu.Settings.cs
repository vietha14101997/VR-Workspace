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

            // Disable preference saving during programmatic dropdown setup
            _preferenceSaveEnabled = false;

            // Enable all dropdowns first
            EnableDropdowns();

            // Load saved preferences — user's previous selections take priority
            var prefs = RemotePreferences.Load();

            // === Monitors ===
            int suggestedMonitorIndex = Mathf.Clamp(config.monitors - 1, 0, 2);
            var monitorOptions = BuildOptionsWithRecommended(MONITOR_OPTIONS, suggestedMonitorIndex);
            int selectedMonitorIndex = prefs.HasMonitorPreference
                ? Mathf.Clamp(prefs.monitors, 0, 2)
                : suggestedMonitorIndex;
            VRDropdownFactory.SetOptions(_monitorsDropdown, monitorOptions, selectedMonitorIndex);

            // === Style ===
            var styleOptions = new List<string>(STYLE_OPTIONS);
            int selectedStyleIndex = 0;
            if (prefs.HasResolutionPreference)
            {
                int savedIdx = FindOptionIndex(STYLE_OPTIONS, prefs.resolution);
                if (savedIdx >= 0) selectedStyleIndex = savedIdx;
            }
            VRDropdownFactory.SetOptions(_styleDropdown, styleOptions, selectedStyleIndex);

            // === Bitrate ===
            int bitrateMbps = config.bitrateKbps / 1000;
            string suggestedBitrate = $"{bitrateMbps} Mbps";
            int suggestedBitrateIndex = FindOptionIndex(BITRATE_OPTIONS, suggestedBitrate);
            if (suggestedBitrateIndex < 0)
            {
                if (bitrateMbps >= 25) suggestedBitrateIndex = 4;
                else if (bitrateMbps >= 17) suggestedBitrateIndex = 3;
                else if (bitrateMbps >= 12) suggestedBitrateIndex = 2;
                else if (bitrateMbps >= 7) suggestedBitrateIndex = 1;
                else suggestedBitrateIndex = 0;
            }
            var bitrateOptions = BuildOptionsWithRecommended(BITRATE_OPTIONS, suggestedBitrateIndex);
            int selectedBitrateIndex = suggestedBitrateIndex;
            if (prefs.HasBitratePreference)
            {
                int savedIdx = FindOptionIndexClean(BITRATE_OPTIONS, prefs.bitrate);
                if (savedIdx >= 0) selectedBitrateIndex = savedIdx;
            }
            VRDropdownFactory.SetOptions(_bitrateDropdown, bitrateOptions, selectedBitrateIndex);

            // === FPS ===
            string suggestedFps = $"{config.fps} FPS";
            int suggestedFpsIndex = FindOptionIndex(FPS_OPTIONS, suggestedFps);
            if (suggestedFpsIndex < 0) suggestedFpsIndex = 2; // Default to 60 FPS
            var fpsOptions = BuildOptionsWithRecommended(FPS_OPTIONS, suggestedFpsIndex);
            int selectedFpsIndex = suggestedFpsIndex;
            if (prefs.HasFpsPreference)
            {
                int savedIdx = FindOptionIndexClean(FPS_OPTIONS, prefs.fps);
                if (savedIdx >= 0) selectedFpsIndex = savedIdx;
            }
            VRDropdownFactory.SetOptions(_fpsDropdown, fpsOptions, selectedFpsIndex);

            // Enable preference saving now that dropdowns have valid values
            _preferenceSaveEnabled = true;

            bool usingSaved = prefs.HasMonitorPreference || prefs.HasBitratePreference || prefs.HasFpsPreference;
            Debug.Log($"[RTTRemoteMenu] Applied config: {(usingSaved ? "SAVED prefs" : "suggested")} — Mon={selectedMonitorIndex + 1}, Bitrate={BITRATE_OPTIONS[selectedBitrateIndex]}, FPS={FPS_OPTIONS[selectedFpsIndex]}");
            if (usingSaved)
                Debug.Log($"[RTTRemoteMenu] Server suggested: {config.monitors}mon, {config.bitrateKbps}kbps, {config.fps}fps (shown as Recommended)");
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
            if (_preferenceSaveEnabled) SaveCurrentSelections();
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
            if (_preferenceSaveEnabled) SaveCurrentSelections();
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
            if (_preferenceSaveEnabled) SaveCurrentSelections();
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
            if (_preferenceSaveEnabled) SaveCurrentSelections();
        }

        /// <summary>
        /// Recalculate suggested config based on selected monitor count.
        /// Updates Bitrate, FPS dropdowns with new "(Recommended)" positions.
        /// </summary>
        private void RecalculateSuggestionsForMonitorCount(int monitorCount)
        {
            if (_cachedNetworkInfo == null || _cachedHardwareInfo == null) return;

            // Get current selections before updating options
            int currentBitrateIndex = VRDropdownFactory.GetSelectedIndex(_bitrateDropdown);
            int currentFpsIndex = VRDropdownFactory.GetSelectedIndex(_fpsDropdown);

            // === Calculate recommended Bitrate based on resolution and network ===
            double availableBandwidth = _cachedNetworkInfo.bandwidthMbps > 0 ? _cachedNetworkInfo.bandwidthMbps : 100;

            int baseBitrateKbps;
            if (_cachedHardwareInfo.gpuVramGB >= 8) baseBitrateKbps = 15000;
            else if (_cachedHardwareInfo.gpuVramGB >= 4) baseBitrateKbps = 12000;
            else baseBitrateKbps = 10000;

            if (_cachedNetworkInfo.pingMs < 10 && availableBandwidth > 500)
                baseBitrateKbps = (int)(baseBitrateKbps * 1.3f);
            else if (_cachedNetworkInfo.pingMs < 20 && availableBandwidth > 200)
                baseBitrateKbps = (int)(baseBitrateKbps * 1.15f);

            double maxBitratePerMonitor = availableBandwidth * 0.6 / monitorCount * 1000;
            int suggestedBitrateKbps = (int)Math.Clamp(Math.Min(baseBitrateKbps, maxBitratePerMonitor), 5000, 30000);

            int suggestedBitrateIndex;
            if (suggestedBitrateKbps >= 25000) suggestedBitrateIndex = 4;
            else if (suggestedBitrateKbps >= 17500) suggestedBitrateIndex = 3;
            else if (suggestedBitrateKbps >= 12500) suggestedBitrateIndex = 2;
            else if (suggestedBitrateKbps >= 7500) suggestedBitrateIndex = 1;
            else suggestedBitrateIndex = 0;

            int suggestedFpsIndex;
            if (_cachedHardwareInfo.hwAccelEnabled && _cachedNetworkInfo.pingMs < 20)
                suggestedFpsIndex = 2;
            else if (_cachedHardwareInfo.hwAccelEnabled && _cachedNetworkInfo.pingMs < 50)
                suggestedFpsIndex = 1;
            else
                suggestedFpsIndex = 0;

            // Check if user has saved preferences — keep their choice, only update "(Recommended)" labels
            var prefs = RemotePreferences.Load();

            // Bitrate: update options with new Recommended label, but keep user's selection if saved
            var bitrateOptions = BuildOptionsWithRecommended(BITRATE_OPTIONS, suggestedBitrateIndex);
            int selectedBitrateIndex = (prefs.HasBitratePreference && currentBitrateIndex >= 0)
                ? currentBitrateIndex  // Keep user's current selection
                : suggestedBitrateIndex;
            VRDropdownFactory.SetOptions(_bitrateDropdown, bitrateOptions, Mathf.Clamp(selectedBitrateIndex, 0, BITRATE_OPTIONS.Length - 1));

            // FPS: same logic
            var fpsOptions = BuildOptionsWithRecommended(FPS_OPTIONS, suggestedFpsIndex);
            int selectedFpsIndex = (prefs.HasFpsPreference && currentFpsIndex >= 0)
                ? currentFpsIndex
                : suggestedFpsIndex;
            VRDropdownFactory.SetOptions(_fpsDropdown, fpsOptions, Mathf.Clamp(selectedFpsIndex, 0, FPS_OPTIONS.Length - 1));

            Debug.Log($"[RTTRemoteMenu] Recalculated for {monitorCount} monitors: Recommended={BITRATE_OPTIONS[suggestedBitrateIndex]}/{FPS_OPTIONS[suggestedFpsIndex]}, Selected={BITRATE_OPTIONS[Mathf.Clamp(selectedBitrateIndex, 0, BITRATE_OPTIONS.Length - 1)]}/{FPS_OPTIONS[Mathf.Clamp(selectedFpsIndex, 0, FPS_OPTIONS.Length - 1)]}");
        }
        #endregion

        #region Dropdown Management
        /// <summary>
        /// Initialize dropdowns as disabled with "----" placeholder.
        /// Called on startup and when disconnected.
        /// </summary>
        private void InitializeDropdownsDisabled()
        {
            _preferenceSaveEnabled = false;  // Prevent handlers from saving placeholder values
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
        /// Save only host/port/USB mode to preferences.
        /// Called immediately after successful connection (AwaitingHardwareInfo phase).
        /// </summary>
        private void SaveHostPreference()
        {
            var prefs = RemotePreferences.Load();
            prefs.lastHost = VRInputFieldFactory.GetValue(_hostInput);
            prefs.lastPort = Port;
            prefs.usbMode = _isUsbMode;
            prefs.Save();
            Debug.Log($"[RTTRemoteMenu] Saved host preference: host={prefs.lastHost}, port={prefs.lastPort}, USB={prefs.usbMode}");
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
