using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VRWorkspace.Media.Data
{
    /// <summary>
    /// Per-video settings entry. Stores all adjustable state for a single video.
    /// Serialized to JSON for persistence across sessions.
    /// </summary>
    [Serializable]
    public class VideoSettingsEntry
    {
        // Identity
        public string FilePath;

        // Projection & stereo
        public int Projection;      // VideoProjectionType as int
        public int Stereo;          // StereoMode as int
        public int Monitor;         // RTTMediaProjectionPopup.MonitorType as int
        public int Environment;     // RTTMediaProjectionPopup.EnvironmentType as int

        // Playback
        public double PlaybackPosition;
        public float PlaybackSpeed = 1f;

        // Picture adjustments
        public float Brightness = 1f;
        public float Contrast = 1f;
        public float Saturation = 1f;
        public float Sharpness = 0.5f;
        public float Tint = 0f;
        public float Temperature = 0f;

        // Display settings (Flat mode)
        public float ScreenDistance = 2f;
        public float ScreenScale = 1f;
        public float ScreenCurvature = 0f;
        public string AspectRatio = "default";

        // Immersive settings (180/360 mode)
        public float FOVZoom = 0f;       // 0 = use default for projection mode
        public float ImmTilt = 0f;
        public float ImmYaw = 0f;
        public float VerticalShift = 0f;
        public float HorizontalShift = 0f;
        public bool LRInverse = false;

        // Timestamp (for LRU eviction)
        public long LastAccessedTicks;
    }

    [Serializable]
    public class VideoSettingsCacheData
    {
        public List<VideoSettingsEntry> Entries = new List<VideoSettingsEntry>();
    }

    /// <summary>
    /// Static utility class for managing per-video settings cache.
    /// Persists to JSON file. Uses LRU eviction when cache exceeds MAX_ENTRIES.
    /// Auto-loaded on first access; call FlushToDisk() to persist changes.
    /// </summary>
    public static class VideoSettingsCache
    {
        private static Dictionary<string, VideoSettingsEntry> _cache;
        private static bool _dirty = false;
        private static string _cachePath;
        private const int MAX_ENTRIES = 200;

        /// <summary>
        /// Load cache from disk. Called once at startup.
        /// Safe to call multiple times (no-op after first load).
        /// </summary>
        public static void LoadFromDisk()
        {
            if (_cache != null) return; // already loaded

            _cache = new Dictionary<string, VideoSettingsEntry>(StringComparer.OrdinalIgnoreCase);
            _cachePath = Path.Combine(Application.persistentDataPath, "video_settings_cache.json");

            if (!File.Exists(_cachePath))
            {
                Debug.Log("[VideoSettingsCache] No cache file found, starting fresh");
                return;
            }

            try
            {
                string json = File.ReadAllText(_cachePath);
                var data = JsonUtility.FromJson<VideoSettingsCacheData>(json);
                if (data?.Entries != null)
                {
                    foreach (var entry in data.Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.FilePath))
                            _cache[entry.FilePath] = entry;
                    }
                }
                Debug.Log($"[VideoSettingsCache] Loaded {_cache.Count} entries from disk");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VideoSettingsCache] Failed to load cache: {e.Message}");
                _cache.Clear();
            }
        }

        /// <summary>
        /// Get cached settings for a video. Returns null if not found.
        /// Updates LastAccessedTicks on access.
        /// </summary>
        public static VideoSettingsEntry Get(string filePath)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(filePath)) return null;

            if (_cache.TryGetValue(filePath, out var entry))
            {
                entry.LastAccessedTicks = DateTime.UtcNow.Ticks;
                _dirty = true;
                return entry;
            }
            return null;
        }

        /// <summary>
        /// Save/update settings for a video. Triggers LRU eviction if needed.
        /// </summary>
        public static void Set(string filePath, VideoSettingsEntry entry)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(filePath) || entry == null) return;

            entry.FilePath = filePath;
            entry.LastAccessedTicks = DateTime.UtcNow.Ticks;
            _cache[filePath] = entry;
            _dirty = true;

            if (_cache.Count > MAX_ENTRIES)
                EvictOldest();
        }

        /// <summary>
        /// Flush cache to disk if dirty. Safe to call frequently.
        /// </summary>
        public static void FlushToDisk()
        {
            if (!_dirty || _cache == null) return;

            try
            {
                var data = new VideoSettingsCacheData
                {
                    Entries = _cache.Values.ToList()
                };
                string json = JsonUtility.ToJson(data, false);
                File.WriteAllText(_cachePath, json);
                _dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VideoSettingsCache] Failed to flush: {e.Message}");
            }
        }

        /// <summary>
        /// Remove a specific video from cache.
        /// </summary>
        public static void Remove(string filePath)
        {
            EnsureLoaded();
            if (_cache.Remove(filePath))
                _dirty = true;
        }

        /// <summary>
        /// Clear all cached entries.
        /// </summary>
        public static void Clear()
        {
            EnsureLoaded();
            _cache.Clear();
            _dirty = true;
        }

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return _cache.Count;
            }
        }

        private static void EnsureLoaded()
        {
            if (_cache == null)
                LoadFromDisk();
        }

        private static void EvictOldest()
        {
            // Remove oldest entries until we're at MAX_ENTRIES
            int toRemove = _cache.Count - MAX_ENTRIES;
            if (toRemove <= 0) return;

            var oldest = _cache.OrderBy(kv => kv.Value.LastAccessedTicks)
                               .Take(toRemove)
                               .Select(kv => kv.Key)
                               .ToList();

            foreach (var key in oldest)
                _cache.Remove(key);

            Debug.Log($"[VideoSettingsCache] Evicted {oldest.Count} oldest entries, {_cache.Count} remaining");
        }
    }

}
