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

            // === Resolution ===
            // Calculate a suggested resolution based on previous default bitrate logic, or default to 1080p.
            int suggestedResolutionIndex = 1; // Default to 1080p
            var resolutionOptions = BuildOptionsWithRecommended(RESOLUTION_OPTIONS, suggestedResolutionIndex);
            int selectedResolutionIndex = suggestedResolutionIndex;
            if (prefs.HasResolutionPreference)
            {
                int savedIdx = FindOptionIndexClean(RESOLUTION_OPTIONS, prefs.resolution);
                if (savedIdx >= 0) selectedResolutionIndex = savedIdx;
            }

            // Lock 1440p option if server's physical monitors are below 1440p
            bool lock1440p = config.maxNativeHeight > 0 && config.maxNativeHeight < 1440;
            if (lock1440p && selectedResolutionIndex == 2)
                selectedResolutionIndex = 1; // Fall back to 1080p if 1440p was selected but locked

            VRDropdownFactory.SetOptions(_resolutionDropdown, resolutionOptions, selectedResolutionIndex);

            if (lock1440p)
            {
                VRDropdownFactory.SetOptionLocked(_resolutionDropdown, 2, true); // Lock 1440p (index 2)
                Debug.Log($"[RTTRemoteMenu] 1440p locked: server max native height = {config.maxNativeHeight}p");
            }

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

            bool usingSaved = prefs.HasMonitorPreference || prefs.HasResolutionPreference || prefs.HasFpsPreference;
            Debug.Log($"[RTTRemoteMenu] Applied config: {(usingSaved ? "SAVED prefs" : "suggested")} — Mon={selectedMonitorIndex + 1}, Resolution={RESOLUTION_OPTIONS[selectedResolutionIndex]}, FPS={FPS_OPTIONS[selectedFpsIndex]}");
            if (usingSaved)
                Debug.Log($"[RTTRemoteMenu] Server suggested: {config.monitors}mon, {config.resolutionHeight}p, {config.fps}fps (shown as Recommended)");
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

            if (_preferenceSaveEnabled) SaveCurrentSelections();
        }

        /// <summary>
        /// Auto-apply Flat Planar or Curved Surround based on monitor selection.
        /// Standard (1/2/3 Monitors) → Flat Planar, Ultrawide/Super Ultrawide → Curved Surround.
        /// </summary>
        private void ApplyAutoStyleForMonitor(int monitorIndex)
        {
            bool isCurvedSurround = monitorIndex >= 3; // Ultrawide or Super Ultrawide

            var clusterRig = FindAnyObjectByType<WorldPanelClusterRig>();
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
        /// Handle Resolution dropdown selection changed.
        /// Sends update_config to server if currently streaming.
        /// </summary>
        private void HandleResolutionChanged(int index, string value)
        {
            Debug.Log($"[RTTRemoteMenu] Resolution changed: index={index}, value={value}");

            // Parse Resolution value
            int resolutionHeight = 1080; // default
            if (!string.IsNullOrEmpty(value))
            {
                var numStr = value.Replace(" ", "").Replace("p", "");
                if (int.TryParse(numStr, out int height))
                {
                    resolutionHeight = height;
                }
            }

            // If streaming, send update_config
            if (_currentPhase == ConnectionPhase.Streaming && _viewModel != null)
            {
                _ = _viewModel.UpdateConfigAsync(null, resolutionHeight);
                Debug.Log($"[RTTRemoteMenu] Sent update_config: resolutionHeight={resolutionHeight}");
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
            int currentResolutionIndex = VRDropdownFactory.GetSelectedIndex(_resolutionDropdown);
            int currentFpsIndex = VRDropdownFactory.GetSelectedIndex(_fpsDropdown);

            // === Calculate recommended Resolution based on hardware and network ===
            double availableBandwidth = _cachedNetworkInfo.bandwidthMbps > 0 ? _cachedNetworkInfo.bandwidthMbps : 100;

            int suggestedResolutionIndex;
            if (_cachedHardwareInfo.gpuVramGB >= 8 && availableBandwidth > 100)
                suggestedResolutionIndex = 2; // 1440p
            else if (_cachedHardwareInfo.gpuVramGB >= 4 && availableBandwidth > 50)
                suggestedResolutionIndex = 1; // 1080p
            else
                suggestedResolutionIndex = 0; // 720p

            int suggestedFpsIndex;
            if (_cachedHardwareInfo.hwAccelEnabled && _cachedNetworkInfo.pingMs < 20)
                suggestedFpsIndex = 2;
            else if (_cachedHardwareInfo.hwAccelEnabled && _cachedNetworkInfo.pingMs < 50)
                suggestedFpsIndex = 1;
            else
                suggestedFpsIndex = 0;

            // Check if user has saved preferences — keep their choice, only update "(Recommended)" labels
            var prefs = RemotePreferences.Load();

            // Resolution: update options with new Recommended label, but keep user's selection if saved
            var resolutionOptions = BuildOptionsWithRecommended(RESOLUTION_OPTIONS, suggestedResolutionIndex);
            int selectedResolutionIndex = (prefs.HasResolutionPreference && currentResolutionIndex >= 0)
                ? currentResolutionIndex  // Keep user's current selection
                : suggestedResolutionIndex;
            // Lock 1440p if server's physical monitors are below 1440p
            bool lock1440p = _cachedSuggestedConfig != null && _cachedSuggestedConfig.maxNativeHeight > 0 && _cachedSuggestedConfig.maxNativeHeight < 1440;
            if (lock1440p && suggestedResolutionIndex == 2)
                suggestedResolutionIndex = 1; // Clamp suggestion to 1080p
            if (lock1440p && selectedResolutionIndex == 2)
                selectedResolutionIndex = 1; // Clamp selection to 1080p

            VRDropdownFactory.SetOptions(_resolutionDropdown, resolutionOptions, Mathf.Clamp(selectedResolutionIndex, 0, RESOLUTION_OPTIONS.Length - 1));

            if (lock1440p)
                VRDropdownFactory.SetOptionLocked(_resolutionDropdown, 2, true);

            // FPS: same logic
            var fpsOptions = BuildOptionsWithRecommended(FPS_OPTIONS, suggestedFpsIndex);
            int selectedFpsIndex = (prefs.HasFpsPreference && currentFpsIndex >= 0)
                ? currentFpsIndex
                : suggestedFpsIndex;
            VRDropdownFactory.SetOptions(_fpsDropdown, fpsOptions, Mathf.Clamp(selectedFpsIndex, 0, FPS_OPTIONS.Length - 1));

            Debug.Log($"[RTTRemoteMenu] Recalculated for {monitorCount} monitors: Recommended={RESOLUTION_OPTIONS[suggestedResolutionIndex]}/{FPS_OPTIONS[suggestedFpsIndex]}, Selected={RESOLUTION_OPTIONS[Mathf.Clamp(selectedResolutionIndex, 0, RESOLUTION_OPTIONS.Length - 1)]}/{FPS_OPTIONS[Mathf.Clamp(selectedFpsIndex, 0, FPS_OPTIONS.Length - 1)]}");
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
            VRDropdownFactory.SetOptions(_resolutionDropdown, placeholder, 0);
            VRDropdownFactory.SetOptions(_fpsDropdown, placeholder, 0);

            VRDropdownFactory.SetInteractable(_monitorsDropdown, false);
            VRDropdownFactory.SetInteractable(_modeDropdown, false);
            VRDropdownFactory.SetInteractable(_resolutionDropdown, false);
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
            VRDropdownFactory.SetInteractable(_resolutionDropdown, true);
            VRDropdownFactory.SetInteractable(_fpsDropdown, true);

            // Always lock "Spatial" option (index 1) in Mode dropdown
            VRDropdownFactory.SetOptionLocked(_modeDropdown, 1, true);

            // Re-lock 1440p if server's physical monitors are below 1440p
            if (_cachedSuggestedConfig != null && _cachedSuggestedConfig.maxNativeHeight > 0 && _cachedSuggestedConfig.maxNativeHeight < 1440)
                VRDropdownFactory.SetOptionLocked(_resolutionDropdown, 2, true);
        }

        /// <summary>
        /// Lock all dropdowns, input fields, and QR button (disable interaction).
        /// Called when state changes to Ready (STARTING...).
        /// </summary>
        private void LockAllInputs()
        {
            VRDropdownFactory.SetInteractable(_monitorsDropdown, false);
            VRDropdownFactory.SetInteractable(_modeDropdown, false);
            VRDropdownFactory.SetInteractable(_resolutionDropdown, false);
            VRDropdownFactory.SetInteractable(_fpsDropdown, false);

            VRInputFieldFactory.SetInteractable(_hostInput, false);
            SetTransportRadioInteractable(false);
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
            VRDropdownFactory.SetInteractable(_resolutionDropdown, true);
            VRDropdownFactory.SetInteractable(_fpsDropdown, true);

            // Re-lock Spatial after enabling Mode dropdown
            VRDropdownFactory.SetOptionLocked(_modeDropdown, 1, true);

            // Re-lock 1440p if server's physical monitors are below 1440p
            if (_cachedSuggestedConfig != null && _cachedSuggestedConfig.maxNativeHeight > 0 && _cachedSuggestedConfig.maxNativeHeight < 1440)
                VRDropdownFactory.SetOptionLocked(_resolutionDropdown, 2, true);

            VRInputFieldFactory.SetInteractable(_hostInput, true);
            SetTransportRadioInteractable(true);
            VRButtonFactory.SetInteractable(_qrButton, true);

            Debug.Log("[RTTRemoteMenu] All inputs unlocked");
        }

        // SetUsbToggleInteractable / SetInternetToggleInteractable replaced by SetTransportRadioInteractable in RTTRemoteMenu.cs

        /// <summary>
        /// Load saved host and USB mode from preferences and apply to inputs.
        /// </summary>
        private void LoadSavedHostPort()
        {
            var prefs = RemotePreferences.Load();
            if (!string.IsNullOrEmpty(prefs.lastHost))
                VRInputFieldFactory.SetValue(_hostInput, prefs.lastHost);

            // Load transport mode
            switch (prefs.transportMode)
            {
                case "USB": _selectedTransport = 1; break;
                case "Internet": _selectedTransport = 2; break;
                default: _selectedTransport = 0; break;
            }
            UpdateTransportRadioVisuals();
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
            prefs.transportMode = TRANSPORT_LABELS[_selectedTransport];
            prefs.Save();
            Debug.Log($"[RTTRemoteMenu] Saved host preference: host={prefs.lastHost}, transport={prefs.transportMode}");
        }

        /// <summary>
        /// Save current dropdown selections and transport mode to preferences.
        /// Called when START is clicked.
        /// </summary>
        private void SaveCurrentSelections()
        {
            var prefs = new RemotePreferences
            {
                monitors = MonitorIndex,
                mode = RemotePreferences.CleanValue(Mode),
                resolution = RemotePreferences.CleanValue(Resolution),
                fps = RemotePreferences.CleanValue(FPS),
                lastHost = VRInputFieldFactory.GetValue(_hostInput),
                lastPort = Port,
                transportMode = TRANSPORT_LABELS[_selectedTransport]
            };
            prefs.Save();
            Debug.Log($"[RTTRemoteMenu] Saved preferences: {prefs.monitors}mon, mode={prefs.mode}, {prefs.resolution}, {prefs.fps}, transport={prefs.transportMode}");
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
