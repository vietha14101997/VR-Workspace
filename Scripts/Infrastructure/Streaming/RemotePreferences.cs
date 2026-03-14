using System;
using System.IO;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// User preferences for remote streaming configuration.
    /// Saved to JSON file in persistentDataPath.
    /// </summary>
    [Serializable]
    public class RemotePreferences
    {
        public int monitors = -1;           // -1 = use suggested (0-4: index into MONITOR_OPTIONS)
        public string mode = "";            // "Classic" or "Spatial"
        public string resolution = "";      // "720p", "1080p", "1440p"
        public string fps = "";
        public string lastHost = "";
        public string lastPort = "8288";    // default port
        public string transportMode = "LAN"; // "LAN", "USB", "Internet"
        public bool usbMode = false;        // [Deprecated] kept for backward compat — migrated to transportMode

        private static string FilePath => Path.Combine(
            Application.persistentDataPath, "remote_preferences.json");

        /// <summary>
        /// Load preferences from JSON file.
        /// Returns default preferences if file doesn't exist.
        /// </summary>
        public static RemotePreferences Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var prefs = JsonUtility.FromJson<RemotePreferences>(json);
                    if (prefs != null)
                    {
                        // Backward compat: migrate old "resolution" field to "mode"
                        if (string.IsNullOrEmpty(prefs.mode) && !string.IsNullOrEmpty(prefs.resolution))
                        {
                            prefs.resolution = "1080p";
                        }

                        // Backward compat: migrate old usbMode bool to transportMode string
                        if (string.IsNullOrEmpty(prefs.transportMode) || prefs.transportMode == "LAN")
                        {
                            if (prefs.usbMode)
                                prefs.transportMode = "USB";
                        }

                        Debug.Log($"[RemotePreferences] Loaded: monitors={prefs.monitors}, mode={prefs.mode}, resolution={prefs.resolution}, fps={prefs.fps}");
                        return prefs;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemotePreferences] Failed to load: {ex.Message}");
            }

            return new RemotePreferences();
        }

        /// <summary>
        /// Save preferences to JSON file.
        /// </summary>
        public void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(FilePath, json);
                Debug.Log($"[RemotePreferences] Saved to {FilePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RemotePreferences] Failed to save: {ex.Message}");
            }
        }

        /// <summary>Check if user has saved monitor preference.</summary>
        public bool HasMonitorPreference => monitors >= 0;

        /// <summary>Check if user has saved mode preference.</summary>
        public bool HasModePreference => !string.IsNullOrEmpty(mode);

        /// <summary>Check if user has saved resolution preference.</summary>
        public bool HasResolutionPreference => !string.IsNullOrEmpty(resolution);

        /// <summary>Check if user has saved fps preference.</summary>
        public bool HasFpsPreference => !string.IsNullOrEmpty(fps);

        /// <summary>
        /// Remove " (Recommended)" and " (Locked)" suffixes from a value.
        /// </summary>
        public static string CleanValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value
                .Replace(" (Recommended)", "")
                .Replace(" (Locked)", "")
                .Trim();
        }
    }
}
