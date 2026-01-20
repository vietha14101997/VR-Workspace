using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Cache entry containing the thumbnail sprite and metadata.
/// </summary>
public class ThumbnailCacheEntry
{
    public Sprite Thumbnail;
    public DateTime LastAccessed;
    public long FileModifiedTicks;  // For cache invalidation when file changes
    public string OriginalFilePath; // Path to the original file for orphan cleanup
}

/// <summary>
/// Memory and disk cache for file thumbnails with LRU eviction.
/// Supports garbage collection to clean up cache entries for deleted files.
/// </summary>
public class ThumbnailCache
{
    #region Configuration
    private readonly int _maxMemoryCacheSize;
    private readonly string _diskCachePath;
    private readonly bool _enableDiskCache;
    #endregion

    #region Memory Cache
    private readonly Dictionary<string, ThumbnailCacheEntry> _memoryCache;
    private readonly LinkedList<string> _lruList;  // Most recently used at front
    private readonly Dictionary<string, string> _aliasMap;  // Maps alias key -> real key
    #endregion

    #region Constructor
    /// <summary>
    /// Create a new ThumbnailCache instance.
    /// </summary>
    /// <param name="maxMemoryCacheSize">Maximum number of sprites to keep in memory</param>
    /// <param name="enableDiskCache">Whether to persist thumbnails to disk</param>
    public ThumbnailCache(int maxMemoryCacheSize = 100, bool enableDiskCache = true)
    {
        _maxMemoryCacheSize = maxMemoryCacheSize;
        _enableDiskCache = enableDiskCache;
        _memoryCache = new Dictionary<string, ThumbnailCacheEntry>();
        _lruList = new LinkedList<string>();
        _aliasMap = new Dictionary<string, string>();

        if (_enableDiskCache)
        {
            _diskCachePath = Path.Combine(Application.persistentDataPath, "ThumbnailCache");
            if (!Directory.Exists(_diskCachePath))
            {
                Directory.CreateDirectory(_diskCachePath);
            }
        }
    }
    #endregion

    #region Public API - Memory Cache
    /// <summary>
    /// Try to get a cached thumbnail from memory.
    /// Supports alias keys that point to the real cache entry.
    /// </summary>
    public bool TryGet(string key, out Sprite sprite)
    {
        sprite = null;

        // Check if this is an alias key
        string realKey = key;
        if (_aliasMap.TryGetValue(key, out string aliasTarget))
        {
            realKey = aliasTarget;
        }

        if (_memoryCache.TryGetValue(realKey, out var entry))
        {
            // Update LRU order using the real key
            _lruList.Remove(realKey);
            _lruList.AddFirst(realKey);
            entry.LastAccessed = DateTime.Now;
            sprite = entry.Thumbnail;
            return sprite != null;
        }

        return false;
    }

    /// <summary>
    /// Set an alias key that points to another key's cache entry.
    /// This allows the same sprite to be accessed via multiple keys without duplication.
    /// The alias does NOT count toward cache size limit.
    /// </summary>
    public void SetAlias(string aliasKey, string targetKey)
    {
        if (string.IsNullOrEmpty(aliasKey) || string.IsNullOrEmpty(targetKey)) return;
        if (aliasKey == targetKey) return;  // No self-alias

        _aliasMap[aliasKey] = targetKey;
    }

    /// <summary>
    /// Add or update a thumbnail in the cache.
    /// </summary>
    public void Set(string key, Sprite sprite, long fileModifiedTicks, string originalFilePath)
    {
        if (sprite == null) return;

        // Remove existing if present
        if (_memoryCache.ContainsKey(key))
        {
            _lruList.Remove(key);
        }

        // Evict if at capacity
        while (_memoryCache.Count >= _maxMemoryCacheSize && _lruList.Count > 0)
        {
            EvictLRU();
        }

        // Add new entry
        var entry = new ThumbnailCacheEntry
        {
            Thumbnail = sprite,
            LastAccessed = DateTime.Now,
            FileModifiedTicks = fileModifiedTicks,
            OriginalFilePath = originalFilePath
        };

        _memoryCache[key] = entry;
        _lruList.AddFirst(key);

        // Persist to disk if enabled
        if (_enableDiskCache)
        {
            SaveToDisk(key, sprite);
        }
    }

    /// <summary>
    /// Remove a specific entry from cache.
    /// </summary>
    public void Remove(string key)
    {
        // Resolve alias if this is an alias key
        string realKey = key;
        if (_aliasMap.TryGetValue(key, out string aliasTarget))
        {
            realKey = aliasTarget;
            _aliasMap.Remove(key);
        }

        if (_memoryCache.TryGetValue(realKey, out var entry))
        {
            // Destroy the sprite and texture to free memory
            if (entry.Thumbnail != null)
            {
                if (entry.Thumbnail.texture != null)
                {
                    UnityEngine.Object.Destroy(entry.Thumbnail.texture);
                }
                UnityEngine.Object.Destroy(entry.Thumbnail);
            }
            _memoryCache.Remove(realKey);
            _lruList.Remove(realKey);

            // Remove any aliases pointing to this key
            var aliasesToRemove = new List<string>();
            foreach (var kvp in _aliasMap)
            {
                if (kvp.Value == realKey)
                {
                    aliasesToRemove.Add(kvp.Key);
                }
            }
            foreach (var alias in aliasesToRemove)
            {
                _aliasMap.Remove(alias);
            }
        }

        // Also remove from disk
        if (_enableDiskCache)
        {
            DeleteFromDisk(realKey);
        }
    }

    /// <summary>
    /// Clear all cached entries and free memory.
    /// </summary>
    public void Clear()
    {
        foreach (var entry in _memoryCache.Values)
        {
            if (entry.Thumbnail != null)
            {
                if (entry.Thumbnail.texture != null)
                {
                    UnityEngine.Object.Destroy(entry.Thumbnail.texture);
                }
                UnityEngine.Object.Destroy(entry.Thumbnail);
            }
        }
        _memoryCache.Clear();
        _lruList.Clear();
        _aliasMap.Clear();
    }

    /// <summary>
    /// Clear all cached entries including disk cache.
    /// Use this to force regeneration of all thumbnails.
    /// </summary>
    public void ClearAll()
    {
        // Clear memory cache
        Clear();

        // Clear disk cache
        if (_enableDiskCache && Directory.Exists(_diskCachePath))
        {
            try
            {
                string[] files = Directory.GetFiles(_diskCachePath);
                foreach (string file in files)
                {
                    File.Delete(file);
                }
                Debug.Log($"[ThumbnailCache] Cleared {files.Length} files from disk cache");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ThumbnailCache] Failed to clear disk cache: {ex.Message}");
            }
        }
    }
    #endregion

    #region Public API - Disk Cache
    /// <summary>
    /// Try to load a thumbnail from disk cache.
    /// Supports alias keys - will try both alias and real key paths.
    /// </summary>
    public bool TryLoadFromDisk(string key, out Sprite sprite)
    {
        sprite = null;

        if (!_enableDiskCache) return false;

        // First try with the given key
        string filePath = GetDiskCachePath(key);

        // If key is an alias, also try the real key path
        if (!File.Exists(filePath) && _aliasMap.TryGetValue(key, out string realKey))
        {
            filePath = GetDiskCachePath(realKey);
        }

        if (!File.Exists(filePath)) return false;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            byte[] data = File.ReadAllBytes(filePath);
            Debug.Log($"[Cache] DISK READ: {sw.ElapsedMilliseconds}ms ({data.Length/1024}KB)");
            sw.Restart();

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;

            if (texture.LoadImage(data))
            {
                Debug.Log($"[Cache] DISK DECODE: {sw.ElapsedMilliseconds}ms ({texture.width}x{texture.height})");
                sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );
                return true;
            }
            else
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ThumbnailCache] Failed to load from disk: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Save a thumbnail to disk cache.
    /// </summary>
    public void SaveToDisk(string key, Sprite sprite)
    {
        if (!_enableDiskCache || sprite == null || sprite.texture == null) return;

        try
        {
            string filePath = GetDiskCachePath(key);

            // Create a readable copy of the texture
            RenderTexture rt = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height);
            Graphics.Blit(sprite.texture, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readableTexture = new Texture2D(sprite.texture.width, sprite.texture.height, TextureFormat.RGBA32, false);
            readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readableTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            byte[] data = readableTexture.EncodeToPNG();
            UnityEngine.Object.Destroy(readableTexture);

            File.WriteAllBytes(filePath, data);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ThumbnailCache] Failed to save to disk: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a thumbnail from disk cache.
    /// </summary>
    public void DeleteFromDisk(string key)
    {
        if (!_enableDiskCache) return;

        try
        {
            string filePath = GetDiskCachePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ThumbnailCache] Failed to delete from disk: {ex.Message}");
        }
    }
    #endregion

    #region Garbage Collection
    /// <summary>
    /// Clean up cache entries for files that no longer exist.
    /// Should be called periodically or when navigating to a new folder.
    /// </summary>
    public void CleanupOrphanedCache()
    {
        // Cleanup memory cache
        var keysToRemove = new List<string>();

        foreach (var kvp in _memoryCache)
        {
            if (!string.IsNullOrEmpty(kvp.Value.OriginalFilePath))
            {
                if (!File.Exists(kvp.Value.OriginalFilePath))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            Remove(key);
            Debug.Log($"[ThumbnailCache] Removed orphaned cache entry: {key}");
        }

        // Cleanup disk cache
        if (_enableDiskCache && Directory.Exists(_diskCachePath))
        {
            CleanupDiskCache();
        }
    }

    /// <summary>
    /// Clean up disk cache files for thumbnails that are no longer needed.
    /// </summary>
    private void CleanupDiskCache()
    {
        try
        {
            string metadataPath = Path.Combine(_diskCachePath, "metadata.txt");
            if (!File.Exists(metadataPath)) return;

            var validEntries = new List<string>();
            var lines = File.ReadAllLines(metadataPath);

            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 2)
                {
                    string cacheKey = parts[0];
                    string originalPath = parts[1];

                    if (File.Exists(originalPath))
                    {
                        validEntries.Add(line);
                    }
                    else
                    {
                        // Delete orphaned cache file
                        string cacheFilePath = GetDiskCachePath(cacheKey);
                        if (File.Exists(cacheFilePath))
                        {
                            File.Delete(cacheFilePath);
                            Debug.Log($"[ThumbnailCache] Deleted orphaned disk cache: {cacheKey}");
                        }
                    }
                }
            }

            // Rewrite metadata with only valid entries
            File.WriteAllLines(metadataPath, validEntries);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ThumbnailCache] Failed to cleanup disk cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Save metadata mapping cache keys to original file paths for orphan detection.
    /// </summary>
    public void SaveMetadata()
    {
        if (!_enableDiskCache) return;

        try
        {
            string metadataPath = Path.Combine(_diskCachePath, "metadata.txt");
            var lines = new List<string>();

            foreach (var kvp in _memoryCache)
            {
                if (!string.IsNullOrEmpty(kvp.Value.OriginalFilePath))
                {
                    lines.Add($"{kvp.Key}|{kvp.Value.OriginalFilePath}|{kvp.Value.FileModifiedTicks}");
                }
            }

            File.WriteAllLines(metadataPath, lines);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ThumbnailCache] Failed to save metadata: {ex.Message}");
        }
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Generate a unique cache key based on file path, modification time, and size.
    /// </summary>
    public string GenerateCacheKey(string filePath, long modifiedTicks, int size = 0)
    {
        string input = $"{filePath}_{modifiedTicks}_{size}";

        using (MD5 md5 = MD5.Create())
        {
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Get the disk cache file path for a cache key.
    /// </summary>
    private string GetDiskCachePath(string key)
    {
        return Path.Combine(_diskCachePath, $"{key}.png");
    }

    /// <summary>
    /// Evict the least recently used entry from memory cache.
    /// </summary>
    private void EvictLRU()
    {
        if (_lruList.Count == 0) return;

        string oldestKey = _lruList.Last.Value;
        _lruList.RemoveLast();

        if (_memoryCache.TryGetValue(oldestKey, out var entry))
        {
            // Destroy the sprite and texture to free memory
            if (entry.Thumbnail != null)
            {
                if (entry.Thumbnail.texture != null)
                {
                    UnityEngine.Object.Destroy(entry.Thumbnail.texture);
                }
                UnityEngine.Object.Destroy(entry.Thumbnail);
            }
            _memoryCache.Remove(oldestKey);

            // Remove any aliases pointing to this key
            var aliasesToRemove = new List<string>();
            foreach (var kvp in _aliasMap)
            {
                if (kvp.Value == oldestKey)
                {
                    aliasesToRemove.Add(kvp.Key);
                }
            }
            foreach (var alias in aliasesToRemove)
            {
                _aliasMap.Remove(alias);
            }
        }
    }

    /// <summary>
    /// Get the current number of entries in memory cache.
    /// </summary>
    public int MemoryCacheCount => _memoryCache.Count;

    /// <summary>
    /// Get estimated memory usage in bytes.
    /// </summary>
    public long EstimatedMemoryUsage
    {
        get
        {
            long total = 0;
            foreach (var entry in _memoryCache.Values)
            {
                if (entry.Thumbnail != null && entry.Thumbnail.texture != null)
                {
                    var tex = entry.Thumbnail.texture;
                    // Rough estimate: width * height * 4 bytes (RGBA32)
                    total += tex.width * tex.height * 4;
                }
            }
            return total;
        }
    }
    #endregion
}
