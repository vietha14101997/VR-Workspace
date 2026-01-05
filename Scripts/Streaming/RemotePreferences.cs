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
        public int monitors = -1;           // -1 = use suggested
        public string resolution = "";      // empty = use suggested
        public string bitrate = "";
        public string fps = "";
        public string lastHost = "";
        public string lastPort = "8288";    // default port
        public bool usbMode = false;        // USB connection mode (via ADB reverse)

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
                        Debug.Log($"[RemotePreferences] Loaded: monitors={prefs.monitors}, res={prefs.resolution}, bitrate={prefs.bitrate}, fps={prefs.fps}");
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

        /// <summary>
        /// Check if user has saved monitor preference.
        /// </summary>
        public bool HasMonitorPreference => monitors >= 0;

        /// <summary>
        /// Check if user has saved resolution preference.
        /// </summary>
        public bool HasResolutionPreference => !string.IsNullOrEmpty(resolution);

        /// <summary>
        /// Check if user has saved bitrate preference.
        /// </summary>
        public bool HasBitratePreference => !string.IsNullOrEmpty(bitrate);

        /// <summary>
        /// Check if user has saved fps preference.
        /// </summary>
        public bool HasFpsPreference => !string.IsNullOrEmpty(fps);

        /// <summary>
        /// Remove " (Recommended)" suffix from a value.
        /// </summary>
        public static string CleanValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Replace(" (Recommended)", "").Trim();
        }
    }
}
