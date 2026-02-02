using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Reference to a cached item for state restoration.
    /// Contains minimal data needed to restore visual state.
    /// </summary>
    [Serializable]
    public class CachedItemRef
    {
        public string path;              // File/video path
        public string name;              // Display name
        public string thumbnailCacheKey; // Key in ThumbnailCache
        public bool isDirectory;         // For FileManager
        public string category;          // File category (video, image, music, etc.)
        public long fileSize;            // For display
        public string duration;          // For video/audio
    }

    /// <summary>
    /// Serializable state snapshot for an app's UI.
    /// Used to restore exact visual state on next app open.
    /// </summary>
    [Serializable]
    public class AppStateSnapshot
    {
        public string appId;
        public string currentPath;           // FileManager: current folder path
        public int currentPage;
        public int pageSize;
        public string sortBy;
        public bool sortAscending;
        public string groupBy;               // Media: grouping mode
        public string category;              // Media: current category (Videos, Images, etc.)
        public string selectedItemPath;
        public float scrollPosition;
        public List<CachedItemRef> items;    // Visible items with thumbnail refs
        public long timestamp;               // When this state was captured
        public int totalItemCount;           // Total items in current view

        public AppStateSnapshot()
        {
            items = new List<CachedItemRef>();
        }
    }

    /// <summary>
    /// Manages app state persistence with memory and disk caching.
    /// Singleton pattern for global access.
    /// </summary>
    public class AppStateCache
    {
        #region Singleton

        private static AppStateCache _instance;
        public static AppStateCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AppStateCache();
                }
                return _instance;
            }
        }

        #endregion

        #region Configuration

        private readonly string _cachePath;
        private readonly Dictionary<string, AppStateSnapshot> _memoryCache;
        private readonly TimeSpan _maxCacheAge = TimeSpan.FromHours(24);

        #endregion

        #region Constructor

        private AppStateCache()
        {
            _memoryCache = new Dictionary<string, AppStateSnapshot>();
            _cachePath = Path.Combine(Application.persistentDataPath, "AppStateCache");

            // Ensure cache directory exists
            if (!Directory.Exists(_cachePath))
            {
                try
                {
                    Directory.CreateDirectory(_cachePath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AppStateCache] Failed to create cache directory: {ex.Message}");
                }
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Try to get cached state for an app.
        /// Checks memory cache first, then disk cache.
        /// </summary>
        /// <param name="appId">App identifier (e.g., "files", "media")</param>
        /// <param name="state">Output state if found</param>
        /// <returns>True if valid cache exists</returns>
        public bool TryGetState(string appId, out AppStateSnapshot state)
        {
            state = null;

            // Check memory cache first
            if (_memoryCache.TryGetValue(appId, out state))
            {
                if (ValidateCache(state))
                {
                    Debug.Log($"[AppStateCache] Memory cache hit for {appId}");
                    return true;
                }
                else
                {
                    // Cache expired, remove from memory
                    _memoryCache.Remove(appId);
                    state = null;
                }
            }

            // Try disk cache
            state = LoadFromDisk(appId);
            if (state != null && ValidateCache(state))
            {
                // Promote to memory cache
                _memoryCache[appId] = state;
                Debug.Log($"[AppStateCache] Disk cache hit for {appId}");
                return true;
            }

            // Cache miss or invalid
            Debug.Log($"[AppStateCache] Cache miss for {appId}");
            return false;
        }

        /// <summary>
        /// Save state for an app to both memory and disk cache.
        /// </summary>
        /// <param name="appId">App identifier</param>
        /// <param name="state">State to save</param>
        public void SaveState(string appId, AppStateSnapshot state)
        {
            if (state == null)
            {
                Debug.LogWarning($"[AppStateCache] Attempted to save null state for {appId}");
                return;
            }

            state.timestamp = DateTime.Now.Ticks;
            state.appId = appId;

            // Save to memory
            _memoryCache[appId] = state;

            // Save to disk (async would be better but keeping simple for now)
            SaveToDisk(appId, state);

            Debug.Log($"[AppStateCache] Saved state for {appId} with {state.items?.Count ?? 0} items");
        }

        /// <summary>
        /// Invalidate cache for an app.
        /// Removes from both memory and disk.
        /// </summary>
        /// <param name="appId">App identifier</param>
        public void InvalidateState(string appId)
        {
            _memoryCache.Remove(appId);
            DeleteFromDisk(appId);
            Debug.Log($"[AppStateCache] Invalidated cache for {appId}");
        }

        /// <summary>
        /// Clear all cached states.
        /// </summary>
        public void ClearAll()
        {
            _memoryCache.Clear();

            try
            {
                if (Directory.Exists(_cachePath))
                {
                    foreach (var file in Directory.GetFiles(_cachePath, "*.json"))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppStateCache] Failed to clear disk cache: {ex.Message}");
            }

            Debug.Log("[AppStateCache] Cleared all caches");
        }

        /// <summary>
        /// Check if a path has changed since cache was created.
        /// Useful for validating FileManager cache.
        /// </summary>
        /// <param name="path">Directory path to check</param>
        /// <param name="cacheTimestamp">Timestamp when cache was created</param>
        /// <returns>True if path has been modified since cache</returns>
        public bool HasPathChanged(string path, long cacheTimestamp)
        {
            if (string.IsNullOrEmpty(path)) return true;

            try
            {
                if (!Directory.Exists(path)) return true;

                var lastWrite = Directory.GetLastWriteTime(path);
                var cacheTime = new DateTime(cacheTimestamp);

                return lastWrite > cacheTime;
            }
            catch
            {
                return true; // Assume changed if we can't check
            }
        }

        #endregion

        #region Private Methods

        private bool ValidateCache(AppStateSnapshot state)
        {
            if (state == null) return false;

            // Check age
            var cacheTime = new DateTime(state.timestamp);
            var age = DateTime.Now - cacheTime;
            if (age > _maxCacheAge)
            {
                Debug.Log($"[AppStateCache] Cache expired for {state.appId} (age: {age.TotalHours:F1}h)");
                return false;
            }

            return true;
        }

        private string GetCacheFilePath(string appId)
        {
            return Path.Combine(_cachePath, $"{appId}_state.json");
        }

        private AppStateSnapshot LoadFromDisk(string appId)
        {
            var filePath = GetCacheFilePath(appId);

            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonUtility.FromJson<AppStateSnapshot>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppStateCache] Failed to load cache from disk: {ex.Message}");
                return null;
            }
        }

        private void SaveToDisk(string appId, AppStateSnapshot state)
        {
            var filePath = GetCacheFilePath(appId);

            try
            {
                var json = JsonUtility.ToJson(state, false);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AppStateCache] Failed to save cache to disk: {ex.Message}");
            }
        }

        private void DeleteFromDisk(string appId)
        {
            var filePath = GetCacheFilePath(appId);

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AppStateCache] Failed to delete cache from disk: {ex.Message}");
            }
        }

        #endregion
    }
}
