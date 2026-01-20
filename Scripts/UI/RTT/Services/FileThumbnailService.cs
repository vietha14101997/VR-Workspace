using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Networking;
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
    public bool SkipOverlay;  // If true, skip video overlay icon (for detail panel)
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

    // UI callback throttling to prevent stutters
    private Queue<Action> _uiCallbackQueue = new Queue<Action>();
    private Queue<Action> _highPriorityUIQueue = new Queue<Action>(); // For detail panel
    private const int MAX_UI_CALLBACKS_PER_FRAME = 4; // Limit UI updates per frame
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
        _uiCallbackQueue = new Queue<Action>();
        _highPriorityUIQueue = new Queue<Action>();

        // Load video overlay icon
        _videoOverlayIcon = Resources.Load<Sprite>("icon_media");

        // Start UI callback processor
        StartCoroutine(ProcessUICallbacksCoroutine());
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
    /// <param name="skipOverlay">If true, skip video overlay icon (for detail panel high-quality preview)</param>
    public void RequestThumbnail(MockFile file, int size, Action<Sprite> onSuccess, Action onFailed = null, int priority = 0, bool skipOverlay = false)
    {
        if (string.IsNullOrEmpty(file.Path) || file.IsFolder)
        {
            // Queue even failure callbacks to maintain consistent timing
            QueueUICallback(() => onFailed?.Invoke());
            return;
        }

        var category = FileCategoryHelper.GetCategory(file.Type);
        if (!FileCategoryHelper.RequiresThumbnailGeneration(category))
        {
            QueueUICallback(() => onFailed?.Invoke());
            return;
        }

        long modifiedTicks = file.Modified.Ticks;
        string cacheKeySuffix = skipOverlay ? "_nooverlay" : "";
        string cacheKey = _cache.GenerateCacheKey(file.Path, modifiedTicks, size) + cacheKeySuffix;

        // Use high priority for detail panel (priority 0)
        bool isHighPriority = priority == 0;

        // Check memory cache first
        if (_cache.TryGet(cacheKey, out Sprite cachedSprite))
        {
            // Queue callback to prevent stutters when many cache hits happen at once
            QueueUICallback(() => onSuccess?.Invoke(cachedSprite), isHighPriority);
            return;
        }

        // Check disk cache
        if (_cache.TryLoadFromDisk(cacheKey, out Sprite diskSprite))
        {
            // Add to memory cache
            _cache.Set(cacheKey, diskSprite, modifiedTicks, file.Path);
            // Queue callback to prevent stutters
            QueueUICallback(() => onSuccess?.Invoke(diskSprite), isHighPriority);
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
            FileModifiedTicks = modifiedTicks,
            SkipOverlay = skipOverlay
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
        Texture2D texture = null;

        // Use UnityWebRequestTexture for non-blocking texture loading
        string fileUrl = "file:///" + request.FilePath.Replace("\\", "/");
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(fileUrl, true)) // nonReadable = true for better performance
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[FileThumbnailService] Failed to load image: {www.error}");
                CompleteRequest(request, null);
                yield break;
            }

            // Get the texture (this is much faster than LoadImage)
            texture = DownloadHandlerTexture.GetContent(www);
            if (texture == null)
            {
                CompleteRequest(request, null);
                yield break;
            }

            // Configure texture settings
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 16;
            texture.wrapMode = TextureWrapMode.Clamp;
        }

        // Yield to spread work
        yield return null;

        // Step 2: Resize texture asynchronously
        Texture2D resized = null;
        bool resizeComplete = false;

        StartCoroutine(ResizeTextureAsync(texture, request.TargetSize, (resizedTexture) =>
        {
            resized = resizedTexture;
            resizeComplete = true;
        }));

        // Wait for async resize to complete
        while (!resizeComplete)
        {
            yield return null;
        }

        if (resized == null)
        {
            Debug.LogWarning($"[FileThumbnailService] Failed to resize image");
            if (texture != null) Destroy(texture);
            CompleteRequest(request, null);
            yield break;
        }

        // Clean up original texture if a new one was created
        if (resized != texture)
        {
            Destroy(texture);
            texture = resized;
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
            if (request.SkipOverlay)
            {
                // No overlay - create sprite directly from extracted frame
                result = Sprite.Create(
                    extractedFrame,
                    new Rect(0, 0, extractedFrame.width, extractedFrame.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );
                // Don't destroy extractedFrame as it's used by the sprite
            }
            else
            {
                // Composite with video overlay
                result = _videoExtractor.CompositeWithOverlay(extractedFrame, _videoOverlayIcon, request.TargetSize);
                Destroy(extractedFrame);
            }

            if (result != null)
            {
                // Cache the result (include size and skipOverlay in key for quality-specific caching)
                string cacheKeySuffix = request.SkipOverlay ? "_nooverlay" : "";
                string cacheKey = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, request.TargetSize) + cacheKeySuffix;
                _cache.Set(cacheKey, result, request.FileModifiedTicks, request.FilePath);
            }
        }

        CompleteRequest(request, result);
    }

    private void CompleteRequest(ThumbnailRequest request, Sprite result)
    {
        _currentLoadingCount--;
        _processingPaths.Remove(request.FilePath);

        // Use high priority for detail panel (priority 0)
        bool isHighPriority = request.Priority == 0;

        if (result != null)
        {
            // Queue UI callback to be executed with throttling
            var onComplete = request.OnComplete;
            QueueUICallback(() => onComplete?.Invoke(result), isHighPriority);

            // Notify pending callbacks (also throttled)
            if (_pendingCallbacks.TryGetValue(request.FilePath, out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    var cb = callback; // Capture for closure
                    QueueUICallback(() => cb?.Invoke(result), isHighPriority);
                }
                _pendingCallbacks.Remove(request.FilePath);
            }
        }
        else
        {
            // Queue failure callback too
            var onFailed = request.OnFailed;
            QueueUICallback(() => onFailed?.Invoke(), isHighPriority);
        }

        // Process next request
        TryProcessNextRequest();
    }
    #endregion

    #region UI Callback Throttling
    /// <summary>
    /// Queue a UI callback to be executed with throttling to prevent frame stutters.
    /// </summary>
    private void QueueUICallback(Action callback, bool highPriority = false)
    {
        if (callback == null) return;

        if (highPriority)
            _highPriorityUIQueue.Enqueue(callback);
        else
            _uiCallbackQueue.Enqueue(callback);
    }

    /// <summary>
    /// Process queued UI callbacks with a frame budget to prevent stutters.
    /// High-priority callbacks (detail panel) are processed first.
    /// </summary>
    private IEnumerator ProcessUICallbacksCoroutine()
    {
        while (true)
        {
            // Process up to MAX_UI_CALLBACKS_PER_FRAME callbacks per frame
            int processed = 0;

            // Process high-priority queue first (detail panel thumbnails)
            while (_highPriorityUIQueue.Count > 0 && processed < MAX_UI_CALLBACKS_PER_FRAME)
            {
                var callback = _highPriorityUIQueue.Dequeue();
                try
                {
                    callback?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileThumbnailService] UI callback error: {ex.Message}");
                }
                processed++;
            }

            // Then process normal queue with remaining budget
            while (_uiCallbackQueue.Count > 0 && processed < MAX_UI_CALLBACKS_PER_FRAME)
            {
                var callback = _uiCallbackQueue.Dequeue();
                try
                {
                    callback?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileThumbnailService] UI callback error: {ex.Message}");
                }
                processed++;
            }

            // Wait for next frame
            yield return null;
        }
    }
    #endregion

    #region Texture Processing
    /// <summary>
    /// Resize a texture to fit within the target size while maintaining aspect ratio.
    /// Uses coroutine with AsyncGPUReadback to avoid blocking main thread.
    /// </summary>
    private IEnumerator ResizeTextureAsync(Texture2D source, int maxSize, Action<Texture2D> onComplete)
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
            onComplete?.Invoke(source);
            yield break;
        }

        // Use RenderTexture for GPU-accelerated resize
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Trilinear;
        Graphics.Blit(source, rt);

        // Yield to let the GPU process
        yield return null;

        // Use AsyncGPUReadback to avoid blocking main thread
        bool readbackComplete = false;
        Texture2D result = null;
        Exception readbackError = null;

        AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, (request) =>
        {
            if (request.hasError)
            {
                readbackError = new Exception("AsyncGPUReadback failed");
            }
            else
            {
                try
                {
                    // Create texture with mipmaps
                    result = new Texture2D(width, height, TextureFormat.RGBA32, true);
                    result.filterMode = FilterMode.Trilinear;
                    result.anisoLevel = 16;
                    result.wrapMode = TextureWrapMode.Clamp;
                    result.LoadRawTextureData(request.GetData<byte>());
                    result.Apply(true); // updateMipmaps = true
                }
                catch (Exception ex)
                {
                    readbackError = ex;
                }
            }
            readbackComplete = true;
        });

        // Wait for async readback to complete (non-blocking)
        while (!readbackComplete)
        {
            yield return null;
        }

        RenderTexture.ReleaseTemporary(rt);

        if (readbackError != null)
        {
            Debug.LogWarning($"[FileThumbnailService] Async resize failed: {readbackError.Message}, falling back to sync");
            // Fallback to synchronous method if async fails
            result = ResizeTextureSync(source, maxSize);
        }

        onComplete?.Invoke(result);
    }

    /// <summary>
    /// Synchronous fallback for texture resize (used when AsyncGPUReadback fails).
    /// </summary>
    private Texture2D ResizeTextureSync(Texture2D source, int maxSize)
    {
        int width = source.width;
        int height = source.height;

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
            return source;
        }

        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        rt.filterMode = FilterMode.Trilinear;
        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, true);
        result.filterMode = FilterMode.Trilinear;
        result.anisoLevel = 16;
        result.wrapMode = TextureWrapMode.Clamp;
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply(true);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }
    #endregion
}
