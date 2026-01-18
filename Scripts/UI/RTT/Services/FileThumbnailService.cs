using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Request structure for thumbnail loading.
/// </summary>
public struct ThumbnailRequest
{
    public string FilePath;
    public FileCategory Category;
    public int TargetSize;
    public Action<Sprite> OnComplete;
    public Action OnFailed;
    public int Priority;  // Lower = higher priority
    public long FileModifiedTicks;
}

/// <summary>
/// Singleton service for managing file thumbnail loading.
/// Handles async loading for images and videos with caching support.
/// </summary>
public class FileThumbnailService : MonoBehaviour
{
    #region Singleton
    private static FileThumbnailService _instance;
    public static FileThumbnailService Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("FileThumbnailService");
                _instance = go.AddComponent<FileThumbnailService>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    #endregion

    #region Configuration
    [SerializeField] private int _maxCacheEntries = 200;  // Increased for larger thumbnails
    [SerializeField] private int _concurrentLoadLimit = 3;
    #endregion

    #region Private Fields
    private ThumbnailCache _cache;
    private List<ThumbnailRequest> _requestQueue;
    private Dictionary<string, List<Action<Sprite>>> _pendingCallbacks;
    private HashSet<string> _processingPaths;
    private int _currentLoadingCount = 0;
    private VideoFrameExtractor _videoExtractor;
    private Sprite _videoOverlayIcon;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        Initialize();
    }

    private void Initialize()
    {
        _cache = new ThumbnailCache(_maxCacheEntries, enableDiskCache: true);
        _requestQueue = new List<ThumbnailRequest>();
        _pendingCallbacks = new Dictionary<string, List<Action<Sprite>>>();
        _processingPaths = new HashSet<string>();

        // Load video overlay icon
        _videoOverlayIcon = Resources.Load<Sprite>("icon_media");
    }

    private void OnDestroy()
    {
        if (_cache != null)
        {
            _cache.SaveMetadata();
            _cache.Clear();
        }

        if (_videoExtractor != null)
        {
            Destroy(_videoExtractor.gameObject);
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && _cache != null)
        {
            _cache.SaveMetadata();
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Request a thumbnail for a file.
    /// </summary>
    /// <param name="file">The MockFile to generate thumbnail for</param>
    /// <param name="size">Target thumbnail size in pixels</param>
    /// <param name="onSuccess">Callback when thumbnail is ready</param>
    /// <param name="onFailed">Optional callback when loading fails</param>
    /// <param name="priority">Lower values = higher priority</param>
    public void RequestThumbnail(MockFile file, int size, Action<Sprite> onSuccess, Action onFailed = null, int priority = 0)
    {
        if (string.IsNullOrEmpty(file.Path) || file.IsFolder)
        {
            onFailed?.Invoke();
            return;
        }

        var category = FileCategoryHelper.GetCategory(file.Type);
        if (!FileCategoryHelper.RequiresThumbnailGeneration(category))
        {
            onFailed?.Invoke();
            return;
        }

        long modifiedTicks = file.Modified.Ticks;
        string cacheKey = _cache.GenerateCacheKey(file.Path, modifiedTicks, size);

        // Check memory cache first
        if (_cache.TryGet(cacheKey, out Sprite cachedSprite))
        {
            onSuccess?.Invoke(cachedSprite);
            return;
        }

        // Check disk cache
        if (_cache.TryLoadFromDisk(cacheKey, out Sprite diskSprite))
        {
            // Add to memory cache
            _cache.Set(cacheKey, diskSprite, modifiedTicks, file.Path);
            onSuccess?.Invoke(diskSprite);
            return;
        }

        // Add to pending callbacks if already processing this file
        if (_processingPaths.Contains(file.Path))
        {
            if (!_pendingCallbacks.ContainsKey(file.Path))
            {
                _pendingCallbacks[file.Path] = new List<Action<Sprite>>();
            }
            _pendingCallbacks[file.Path].Add(onSuccess);
            return;
        }

        // Queue new request
        var request = new ThumbnailRequest
        {
            FilePath = file.Path,
            Category = category,
            TargetSize = size,
            OnComplete = onSuccess,
            OnFailed = onFailed,
            Priority = priority,
            FileModifiedTicks = modifiedTicks
        };

        _requestQueue.Add(request);
        _requestQueue.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Start processing if not at limit
        TryProcessNextRequest();
    }

    /// <summary>
    /// Cancel a pending thumbnail request.
    /// </summary>
    public void CancelRequest(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        // Remove from queue
        _requestQueue.RemoveAll(r => r.FilePath == filePath);

        // Remove pending callbacks
        _pendingCallbacks.Remove(filePath);
    }

    /// <summary>
    /// Cancel all pending requests.
    /// </summary>
    public void CancelAllRequests()
    {
        _requestQueue.Clear();
        _pendingCallbacks.Clear();
    }

    /// <summary>
    /// Clear memory cache only.
    /// </summary>
    public void ClearMemoryCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Clear all cached thumbnails including disk cache.
    /// Use this to force regeneration of all thumbnails (e.g., after quality change).
    /// </summary>
    public void ClearAllCache()
    {
        _cache.ClearAll();
        Debug.Log("[FileThumbnailService] All cache cleared (memory + disk)");
    }

    /// <summary>
    /// Clean up cache entries for files that no longer exist.
    /// Call this when navigating to a new folder.
    /// </summary>
    public void CleanupOrphanedCache()
    {
        _cache.CleanupOrphanedCache();
    }

    /// <summary>
    /// Get cache statistics for debugging.
    /// </summary>
    public string GetCacheStats()
    {
        return $"Memory: {_cache.MemoryCacheCount} entries, ~{_cache.EstimatedMemoryUsage / 1024}KB | Queue: {_requestQueue.Count} | Processing: {_currentLoadingCount}";
    }
    #endregion

    #region Request Processing
    private void TryProcessNextRequest()
    {
        if (_currentLoadingCount >= _concurrentLoadLimit || _requestQueue.Count == 0)
            return;

        var request = _requestQueue[0];
        _requestQueue.RemoveAt(0);

        // Check if file still exists
        if (!File.Exists(request.FilePath))
        {
            request.OnFailed?.Invoke();
            TryProcessNextRequest();
            return;
        }

        _processingPaths.Add(request.FilePath);
        _currentLoadingCount++;

        if (request.Category == FileCategory.Image)
        {
            StartCoroutine(LoadImageThumbnail(request));
        }
        else if (request.Category == FileCategory.Video)
        {
            StartCoroutine(LoadVideoThumbnail(request));
        }
        else
        {
            _currentLoadingCount--;
            _processingPaths.Remove(request.FilePath);
            request.OnFailed?.Invoke();
            TryProcessNextRequest();
        }
    }

    private IEnumerator LoadImageThumbnail(ThumbnailRequest request)
    {
        Sprite result = null;
        byte[] fileData = null;
        bool fileReadComplete = false;
        Exception readException = null;

        // Read file on background thread to avoid blocking main thread
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                fileData = File.ReadAllBytes(request.FilePath);
            }
            catch (Exception ex)
            {
                readException = ex;
            }
            finally
            {
                fileReadComplete = true;
            }
        });

        // Wait for file read to complete without blocking
        while (!fileReadComplete)
        {
            yield return null;
        }

        // Check for read errors
        if (readException != null)
        {
            Debug.LogWarning($"[FileThumbnailService] Failed to read file: {readException.Message}");
            CompleteRequest(request, null);
            yield break;
        }

        if (fileData == null || fileData.Length == 0)
        {
            CompleteRequest(request, null);
            yield break;
        }

        // Yield before texture processing to spread work
        yield return null;

        // Step 1: Load texture from bytes (no yield inside try-catch)
        Texture2D texture = null;
        bool loadSuccess = false;
        try
        {
            // Enable mipmaps for stable rendering at different scales
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 16; // Max anisotropic filtering for sharp angles
            texture.wrapMode = TextureWrapMode.Clamp;
            loadSuccess = texture.LoadImage(fileData);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileThumbnailService] Failed to load image: {ex.Message}");
            if (texture != null) Destroy(texture);
            CompleteRequest(request, null);
            yield break;
        }

        if (!loadSuccess)
        {
            if (texture != null) Destroy(texture);
            CompleteRequest(request, null);
            yield break;
        }

        // Yield after loading large textures
        yield return null;

        // Step 2: Resize texture
        Texture2D resized = null;
        try
        {
            resized = ResizeTexture(texture, request.TargetSize);
            if (resized != texture)
            {
                Destroy(texture);
                texture = resized;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileThumbnailService] Failed to resize image: {ex.Message}");
            if (texture != null) Destroy(texture);
            CompleteRequest(request, null);
            yield break;
        }

        // Yield after resize
        yield return null;

        // Step 3: Create sprite and cache
        try
        {
            result = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f
            );

            // Cache the result (include size in key for quality-specific caching)
            string cacheKey = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, request.TargetSize);
            _cache.Set(cacheKey, result, request.FileModifiedTicks, request.FilePath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileThumbnailService] Failed to create sprite: {ex.Message}");
            if (texture != null) Destroy(texture);
        }

        // Complete request
        CompleteRequest(request, result);
    }

    private IEnumerator LoadVideoThumbnail(ThumbnailRequest request)
    {
        // Ensure video extractor exists
        if (_videoExtractor == null)
        {
            GameObject go = new GameObject("VideoFrameExtractor");
            go.transform.SetParent(transform);
            _videoExtractor = go.AddComponent<VideoFrameExtractor>();
        }

        Sprite result = null;
        bool extractionComplete = false;
        Texture2D extractedFrame = null;

        _videoExtractor.ExtractFrame(
            request.FilePath,
            request.TargetSize,
            request.TargetSize,
            (texture) => {
                extractedFrame = texture;
                extractionComplete = true;
            },
            () => {
                extractionComplete = true;
            }
        );

        // Wait for extraction to complete
        while (!extractionComplete)
        {
            yield return null;
        }

        if (extractedFrame != null)
        {
            // Composite with video overlay
            result = _videoExtractor.CompositeWithOverlay(extractedFrame, _videoOverlayIcon, request.TargetSize);

            if (result != null)
            {
                // Cache the result (include size in key for quality-specific caching)
                string cacheKey = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, request.TargetSize);
                _cache.Set(cacheKey, result, request.FileModifiedTicks, request.FilePath);
            }

            Destroy(extractedFrame);
        }

        CompleteRequest(request, result);
    }

    private void CompleteRequest(ThumbnailRequest request, Sprite result)
    {
        _currentLoadingCount--;
        _processingPaths.Remove(request.FilePath);

        if (result != null)
        {
            request.OnComplete?.Invoke(result);

            // Notify pending callbacks
            if (_pendingCallbacks.TryGetValue(request.FilePath, out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    callback?.Invoke(result);
                }
                _pendingCallbacks.Remove(request.FilePath);
            }
        }
        else
        {
            request.OnFailed?.Invoke();
        }

        // Process next request
        TryProcessNextRequest();
    }
    #endregion

    #region Texture Processing
    /// <summary>
    /// Resize a texture to fit within the target size while maintaining aspect ratio.
    /// </summary>
    private Texture2D ResizeTexture(Texture2D source, int maxSize)
    {
        int width = source.width;
        int height = source.height;

        // Calculate new dimensions maintaining aspect ratio
        if (width > maxSize || height > maxSize)
        {
            float ratio = (float)width / height;
            if (width > height)
            {
                width = maxSize;
                height = Mathf.RoundToInt(maxSize / ratio);
            }
            else
            {
                height = maxSize;
                width = Mathf.RoundToInt(maxSize * ratio);
            }
        }
        else
        {
            // Image is smaller than max size, no resize needed
            return source;
        }

        // Use RenderTexture for GPU-accelerated resize
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        rt.filterMode = FilterMode.Trilinear;
        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        // Enable mipmaps for stable rendering at different scales
        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, true);
        result.filterMode = FilterMode.Trilinear;
        result.anisoLevel = 16; // Max anisotropic filtering
        result.wrapMode = TextureWrapMode.Clamp;
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply(true); // updateMipmaps = true

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }
    #endregion
}
