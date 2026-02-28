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

            // === Monitors (5 options: 1 Mon, 2 Mon, 3 Mon, Ultrawide, Super Ultrawide) ===
            int suggestedMonitorIndex = Mathf.Clamp(config.monitors - 1, 0, MONITOR_OPTIONS.Length - 1);
            var monitorOptions = BuildOptionsWithRecommended(MONITOR_OPTIONS, suggestedMonitorIndex);
            int selectedMonitorIndex = prefs.HasMonitorPreference
                ? Mathf.Clamp(prefs.monitors, 0, MONITOR_OPTIONS.Length - 1)
                : suggestedMonitorIndex;
            VRDropdownFactory.SetOptions(_monitorsDropdown, monitorOptions, selectedMonitorIndex);

            // === Mode (Classic / Spatial) — Spatial is locked ===
            var modeOptions = new List<string>(MODE_OPTIONS);
            int selectedModeIndex = 0; // Default: Classic
            if (prefs.HasModePreference)
            {
                int savedIdx = FindOptionIndex(MODE_OPTIONS, prefs.mode);
                if (savedIdx >= 0) selectedModeIndex = savedIdx;
            }
            VRDropdownFactory.SetOptions(_modeDropdown, modeOptions, selectedModeIndex);
            // Lock "Spatial" option (index 1) — future feature
            VRDropdownFactory.SetOptionLocked(_modeDropdown, 1, true);

            // === Auto-apply style based on monitor selection ===
            ApplyAutoStyleForMonitor(selectedMonitorIndex);

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
        /// Auto-applies Flat Planar / Curved Surround based on monitor type.
        /// For Ultrawide/Super Ultrawide: treats as 1 monitor for bitrate/FPS calculation.
        /// </summary>
        private void HandleMonitorSelectionChanged(int index, string value)
        {
            Debug.Log($"[RTTRemoteMenu] Monitor selection changed: index={index}, value={value}");

            // Auto-apply style based on monitor type
            ApplyAutoStyleForMonitor(index);

            // Only recalculate if we have cached info
            if (_cachedNetworkInfo == null || _cachedHardwareInfo == null)
            {
                Debug.Log("[RTTRemoteMenu] No cached info, skipping recalculation");
                if (_preferenceSaveEnabled) SaveCurrentSelections();
                return;
            }

            // Ultrawide/Super Ultrawide = 1 virtual monitor, Standard = 1/2/3 physical
            int selectedMonitors = index >= 3 ? 1 : index + 1;
            RecalculateSuggestionsForMonitorCount(selectedMonitors);
            if (_preferenceSaveEnabled) SaveCurrentSelections();
        }

        /// <summary>
        /// Auto-apply Flat Planar or Curved Surround based on monitor selection.
        /// Standard (1/2/3 Monitors) → Flat Planar, Ultrawide/Super Ultrawide → Curved Surround.
        /// </summary>
        private void ApplyAutoStyleForMonitor(int monitorIndex)
        {
            bool isCurvedSurround = monitorIndex >= 3; // Ultrawide or Super Ultrawide

            var clusterRig = FindFirstObjectByType<WorldPanelClusterRig>();
            if (clusterRig != null)
            {
                clusterRig.SetStyle(isCurvedSurround);
                Debug.Log($"[RTTRemoteMenu] Auto-style: {(isCurvedSurround ? "Curved Surround (Ultrawide)" : "Flat Planar (Standard)")}");
            }
        }

        /// <summary>
        /// Handle Mode dropdown selection changed.
        /// Currently only "Classic" is selectable (Spatial is locked for future use).
        /// </summary>
        private void HandleModeChanged(int index, string value)
        {
            Debug.Log($"[RTTRemoteMenu] Mode changed: index={index}, value={value}");

            // Spatial mode is locked — only Classic mode is functional
            // Future: Spatial mode will trigger different VR rendering path
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
            VRDropdownFactory.SetOptions(_modeDropdown, new List<string> { "Classic", "Spatial" }, 0);
            VRDropdownFactory.SetOptions(_bitrateDropdown, placeholder, 0);
            VRDropdownFactory.SetOptions(_fpsDropdown, placeholder, 0);

            VRDropdownFactory.SetInteractable(_monitorsDropdown, false);
            VRDropdownFactory.SetInteractable(_modeDropdown, false);
            VRDropdownFactory.SetInteractable(_bitrateDropdown, false);
            VRDropdownFactory.SetInteractable(_fpsDropdown, false);
        }

        /// <summary>
        /// Enable all dropdowns for interaction.
        /// Lock "Spatial" option in Mode dropdown after enabling.
        /// </summary>
        private void EnableDropdowns()
        {
            VRDropdownFactory.SetInteractable(_monitorsDropdown, true);
            VRDropdownFactory.SetInteractable(_modeDropdown, true);
            VRDropdownFactory.SetInteractable(_bitrateDropdown, true);
            VRDropdownFactory.SetInteractable(_fpsDropdown, true);

            // Always lock "Spatial" option (index 1) in Mode dropdown
            VRDropdownFactory.SetOptionLocked(_modeDropdown, 1, true);
        }

        /// <summary>
        /// Lock all dropdowns, input fields, and QR button (disable interaction).
        /// Called when state changes to Ready (STARTING...).
        /// </summary>
        private void LockAllInputs()
        {
            VRDropdownFactory.SetInteractable(_monitorsDropdown, false);
            VRDropdownFactory.SetInteractable(_modeDropdown, false);
            VRDropdownFactory.SetInteractable(_bitrateDropdown, false);
            VRDropdownFactory.SetInteractable(_fpsDropdown, false);

            VRInputFieldFactory.SetInteractable(_hostInput, false);
            SetUsbToggleInteractable(false);
            VRButtonFactory.SetInteractable(_qrButton, false);

            Debug.Log("[RTTRemoteMenu] All inputs locked (Ready state)");
        }

        /// <summary>
        /// Unlock all dropdowns, input fields, and QR button (enable interaction).
        /// </summary>
        private void UnlockAllInputs()
        {
            VRDropdownFactory.SetInteractable(_monitorsDropdown, true);
            VRDropdownFactory.SetInteractable(_modeDropdown, true);
            VRDropdownFactory.SetInteractable(_bitrateDropdown, true);
            VRDropdownFactory.SetInteractable(_fpsDropdown, true);

            // Re-lock Spatial after enabling Mode dropdown
            VRDropdownFactory.SetOptionLocked(_modeDropdown, 1, true);

            VRInputFieldFactory.SetInteractable(_hostInput, !_isUsbMode);
            SetUsbToggleInteractable(true);
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
                mode = RemotePreferences.CleanValue(Mode),
                bitrate = RemotePreferences.CleanValue(Bitrate),
                fps = RemotePreferences.CleanValue(FPS),
                lastHost = VRInputFieldFactory.GetValue(_hostInput),
                lastPort = Port,
                usbMode = _isUsbMode
            };
            prefs.Save();
            Debug.Log($"[RTTRemoteMenu] Saved preferences: {prefs.monitors}mon, mode={prefs.mode}, {prefs.bitrate}, {prefs.fps}, USB={prefs.usbMode}");
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
