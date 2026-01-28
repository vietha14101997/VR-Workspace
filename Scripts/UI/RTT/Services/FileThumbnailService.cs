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
    [SerializeField] private int _concurrentLoadLimit = 6;  // Increased for faster loading
    #endregion

    #region Private Fields
    private ThumbnailCache _cache;
    private List<ThumbnailRequest> _requestQueue;
    private Dictionary<string, List<PendingCallbackInfo>> _pendingCallbacks;
    private HashSet<string> _processingPaths;
    private int _currentLoadingCount = 0;
    private VideoFrameExtractor _videoExtractor;
    private Sprite _videoOverlayIcon;

    // Info for pending callbacks to retrieve correct sprite from cache
    private class PendingCallbackInfo
    {
        public int Size;
        public bool SkipOverlay;
        public Action<Sprite> Callback;
        public long FileModifiedTicks;
    }

    // UI callback throttling to prevent stutters
    private Queue<Action> _uiCallbackQueue = new Queue<Action>();
    private Queue<Action> _highPriorityUIQueue = new Queue<Action>(); // For detail panel
    private const int MAX_UI_CALLBACKS_PER_FRAME = 8; // Limit UI updates per frame (increased for faster grid updates)
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
        _pendingCallbacks = new Dictionary<string, List<PendingCallbackInfo>>();
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
        string fileName = System.IO.Path.GetFileName(file.Path);

        // Check memory cache first
        if (_cache.TryGet(cacheKey, out Sprite cachedSprite))
        {
            Debug.Log($"[Thumb] MEMORY HIT: {fileName} size={size} skip={skipOverlay}");
            QueueUICallback(() => onSuccess?.Invoke(cachedSprite), isHighPriority);
            return;
        }

        // Check disk cache
        if (_cache.TryLoadFromDisk(cacheKey, out Sprite diskSprite))
        {
            Debug.Log($"[Thumb] DISK HIT: {fileName} size={size} skip={skipOverlay}");
            _cache.Set(cacheKey, diskSprite, modifiedTicks, file.Path);
            QueueUICallback(() => onSuccess?.Invoke(diskSprite), isHighPriority);
            return;
        }

        // Add to pending callbacks if already processing this file
        if (_processingPaths.Contains(file.Path))
        {
            Debug.Log($"[Thumb] PENDING: {fileName} size={size} skip={skipOverlay}");
            if (!_pendingCallbacks.ContainsKey(file.Path))
            {
                _pendingCallbacks[file.Path] = new List<PendingCallbackInfo>();
            }
            _pendingCallbacks[file.Path].Add(new PendingCallbackInfo
            {
                Size = size,
                SkipOverlay = skipOverlay,
                Callback = onSuccess,
                FileModifiedTicks = modifiedTicks
            });
            return;
        }

        Debug.Log($"[Thumb] MISS - LOAD: {fileName} size={size} skip={skipOverlay}");

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
        else if (request.Category == FileCategory.Music)
        {
            StartCoroutine(LoadAudioThumbnail(request));
        }
        else
        {
            _currentLoadingCount--;
            _processingPaths.Remove(request.FilePath);
            request.OnFailed?.Invoke();
            TryProcessNextRequest();
        }
    }

    // Standard sizes for pre-caching
    private const int DETAIL_THUMBNAIL_SIZE = 512;
    private const int LIST_THUMBNAIL_SIZE = 256;

    private IEnumerator LoadImageThumbnail(ThumbnailRequest request)
    {
        Sprite result = null;
        Texture2D texture = null;
        string fileName = System.IO.Path.GetFileName(request.FilePath);
        var totalStart = System.Diagnostics.Stopwatch.StartNew();
        var stepWatch = System.Diagnostics.Stopwatch.StartNew();

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

        Debug.Log($"[Thumb] {fileName} LOAD: {stepWatch.ElapsedMilliseconds}ms ({texture.width}x{texture.height})");
        stepWatch.Restart();

        // Yield to spread work
        yield return null;

        // Step 2: Always resize to 512px first (for Detail panel cache)
        // This ensures Detail panel never needs to reload 4K images
        Texture2D resized512 = null;
        bool resize512Complete = false;

        StartCoroutine(ResizeTextureAsync(texture, DETAIL_THUMBNAIL_SIZE, (resizedTexture) =>
        {
            resized512 = resizedTexture;
            resize512Complete = true;
        }));

        while (!resize512Complete)
        {
            yield return null;
        }

        Debug.Log($"[Thumb] {fileName} RESIZE512: {stepWatch.ElapsedMilliseconds}ms");
        stepWatch.Restart();

        if (resized512 == null)
        {
            Debug.LogWarning($"[FileThumbnailService] Failed to resize image to 512");
            if (texture != null) Destroy(texture);
            CompleteRequest(request, null);
            yield break;
        }

        // Clean up original texture
        if (resized512 != texture)
        {
            Destroy(texture);
        }

        yield return null;

        // Step 3: Cache 512px version (for Grid and Detail panel)
        Sprite sprite512 = null;
        try
        {
            sprite512 = Sprite.Create(
                resized512,
                new Rect(0, 0, resized512.width, resized512.height),
                new Vector2(0.5f, 0.5f),
                100f
            );

            string cacheKey512 = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, DETAIL_THUMBNAIL_SIZE);
            _cache.Set(cacheKey512, sprite512, request.FileModifiedTicks, request.FilePath);
            // Alias for nooverlay (images don't have overlay, same sprite for both)
            _cache.SetAlias(cacheKey512 + "_nooverlay", cacheKey512);

            Debug.Log($"[Thumb] {fileName} CACHE512: {stepWatch.ElapsedMilliseconds}ms");
            stepWatch.Restart();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileThumbnailService] Failed to create 512 sprite: {ex.Message}");
            if (resized512 != null) Destroy(resized512);
            CompleteRequest(request, null);
            yield break;
        }

        // Step 4: If request needs 256px (List view), also create that version
        if (request.TargetSize <= LIST_THUMBNAIL_SIZE)
        {
            Texture2D resized256 = null;
            bool resize256Complete = false;

            StartCoroutine(ResizeTextureAsync(resized512, LIST_THUMBNAIL_SIZE, (resizedTexture) =>
            {
                resized256 = resizedTexture;
                resize256Complete = true;
            }));

            while (!resize256Complete)
            {
                yield return null;
            }

            if (resized256 != null && resized256 != resized512)
            {
                try
                {
                    Sprite sprite256 = Sprite.Create(
                        resized256,
                        new Rect(0, 0, resized256.width, resized256.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );

                    string cacheKey256 = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, LIST_THUMBNAIL_SIZE);
                    _cache.Set(cacheKey256, sprite256, request.FileModifiedTicks, request.FilePath);
                    _cache.SetAlias(cacheKey256 + "_nooverlay", cacheKey256);

                    result = sprite256;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileThumbnailService] Failed to create 256 sprite: {ex.Message}");
                    // Fall back to 512 sprite
                    if (resized256 != null) Destroy(resized256);
                    result = sprite512;
                }
            }
            else
            {
                result = sprite512;
            }
        }
        else
        {
            // Request was for 512 or larger, use the 512 sprite
            result = sprite512;
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

        // Always extract at 512px to pre-cache for Detail panel
        _videoExtractor.ExtractFrame(
            request.FilePath,
            DETAIL_THUMBNAIL_SIZE,
            DETAIL_THUMBNAIL_SIZE,
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
            // Step 1: Cache 512px versions (for Grid and Detail panel)
            string cacheKey512 = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, DETAIL_THUMBNAIL_SIZE);

            // Create no-overlay version (used by detail panel)
            Sprite noOverlaySprite512 = Sprite.Create(
                extractedFrame,
                new Rect(0, 0, extractedFrame.width, extractedFrame.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            _cache.Set(cacheKey512 + "_nooverlay", noOverlaySprite512, request.FileModifiedTicks, request.FilePath);

            // Create overlay version (used by grid)
            Sprite overlaySprite512 = _videoExtractor.CompositeWithOverlay(extractedFrame, _videoOverlayIcon, DETAIL_THUMBNAIL_SIZE);
            if (overlaySprite512 != null)
            {
                _cache.Set(cacheKey512, overlaySprite512, request.FileModifiedTicks, request.FilePath);
            }

            // Step 2: If request needs 256px (List view), also create those versions
            if (request.TargetSize <= LIST_THUMBNAIL_SIZE)
            {
                Texture2D resized256 = null;
                bool resize256Complete = false;

                StartCoroutine(ResizeTextureAsync(extractedFrame, LIST_THUMBNAIL_SIZE, (resizedTexture) =>
                {
                    resized256 = resizedTexture;
                    resize256Complete = true;
                }));

                while (!resize256Complete)
                {
                    yield return null;
                }

                if (resized256 != null)
                {
                    string cacheKey256 = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, LIST_THUMBNAIL_SIZE);

                    // Create no-overlay version for 256
                    Sprite noOverlaySprite256 = Sprite.Create(
                        resized256,
                        new Rect(0, 0, resized256.width, resized256.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    _cache.Set(cacheKey256 + "_nooverlay", noOverlaySprite256, request.FileModifiedTicks, request.FilePath);

                    // Create overlay version for 256
                    Sprite overlaySprite256 = _videoExtractor.CompositeWithOverlay(resized256, _videoOverlayIcon, LIST_THUMBNAIL_SIZE);
                    if (overlaySprite256 != null)
                    {
                        _cache.Set(cacheKey256, overlaySprite256, request.FileModifiedTicks, request.FilePath);
                    }

                    // Return 256px version
                    result = request.SkipOverlay ? noOverlaySprite256 : overlaySprite256;
                }
                else
                {
                    // Fall back to 512 version
                    result = request.SkipOverlay ? noOverlaySprite512 : overlaySprite512;
                }
            }
            else
            {
                // Request was for 512 or larger
                result = request.SkipOverlay ? noOverlaySprite512 : overlaySprite512;
            }
        }

        CompleteRequest(request, result);
    }

    private IEnumerator LoadAudioThumbnail(ThumbnailRequest request)
    {
        Sprite result = null;
        string fileName = System.IO.Path.GetFileName(request.FilePath);

        // Try to extract album art from audio metadata
        byte[] albumArtData = null;
        bool extractionComplete = false;

        // Run extraction on a background thread (only file I/O, no Unity API)
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                albumArtData = AudioMetadataExtractor.TryExtractAlbumArtData(request.FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileThumbnailService] Audio metadata extraction failed: {ex.Message}");
            }
            extractionComplete = true;
        });

        // Wait for extraction to complete
        float timeout = 5f;
        float elapsed = 0f;
        while (!extractionComplete && elapsed < timeout)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }

        // Create texture on main thread
        Texture2D albumArt = null;
        if (albumArtData != null && albumArtData.Length > 0)
        {
            try
            {
                albumArt = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                albumArt.filterMode = FilterMode.Trilinear;
                albumArt.anisoLevel = 16;
                albumArt.wrapMode = TextureWrapMode.Clamp;

                if (!albumArt.LoadImage(albumArtData))
                {
                    Destroy(albumArt);
                    albumArt = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileThumbnailService] Failed to create album art texture: {ex.Message}");
                if (albumArt != null)
                {
                    Destroy(albumArt);
                    albumArt = null;
                }
            }
        }

        if (albumArt != null)
        {
            Debug.Log($"[Thumb] {fileName} AUDIO: Album art found ({albumArt.width}x{albumArt.height})");

            // Resize to standard thumbnail sizes
            Texture2D resized512 = null;
            bool resize512Complete = false;

            StartCoroutine(ResizeTextureAsync(albumArt, DETAIL_THUMBNAIL_SIZE, (resizedTexture) =>
            {
                resized512 = resizedTexture;
                resize512Complete = true;
            }));

            while (!resize512Complete)
            {
                yield return null;
            }

            if (resized512 != null)
            {
                // Clean up original if resized
                if (resized512 != albumArt)
                {
                    Destroy(albumArt);
                }

                // Cache 512px version
                Sprite sprite512 = Sprite.Create(
                    resized512,
                    new Rect(0, 0, resized512.width, resized512.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );

                string cacheKey512 = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, DETAIL_THUMBNAIL_SIZE);
                _cache.Set(cacheKey512, sprite512, request.FileModifiedTicks, request.FilePath);
                _cache.SetAlias(cacheKey512 + "_nooverlay", cacheKey512);

                // Create 256px version if needed
                if (request.TargetSize <= LIST_THUMBNAIL_SIZE)
                {
                    Texture2D resized256 = null;
                    bool resize256Complete = false;

                    StartCoroutine(ResizeTextureAsync(resized512, LIST_THUMBNAIL_SIZE, (resizedTexture) =>
                    {
                        resized256 = resizedTexture;
                        resize256Complete = true;
                    }));

                    while (!resize256Complete)
                    {
                        yield return null;
                    }

                    if (resized256 != null && resized256 != resized512)
                    {
                        Sprite sprite256 = Sprite.Create(
                            resized256,
                            new Rect(0, 0, resized256.width, resized256.height),
                            new Vector2(0.5f, 0.5f),
                            100f
                        );

                        string cacheKey256 = _cache.GenerateCacheKey(request.FilePath, request.FileModifiedTicks, LIST_THUMBNAIL_SIZE);
                        _cache.Set(cacheKey256, sprite256, request.FileModifiedTicks, request.FilePath);
                        _cache.SetAlias(cacheKey256 + "_nooverlay", cacheKey256);

                        result = sprite256;
                    }
                    else
                    {
                        result = sprite512;
                    }
                }
                else
                {
                    result = sprite512;
                }
            }
        }
        else
        {
            Debug.Log($"[Thumb] {fileName} AUDIO: No album art found");
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

            // Notify pending callbacks - each gets the correct sprite from cache based on their settings
            if (_pendingCallbacks.TryGetValue(request.FilePath, out var pendingList))
            {
                foreach (var pending in pendingList)
                {
                    // Get the correct sprite from cache for this pending request
                    string cacheKey = _cache.GenerateCacheKey(request.FilePath, pending.FileModifiedTicks, pending.Size);
                    if (pending.SkipOverlay) cacheKey += "_nooverlay";

                    if (_cache.TryGet(cacheKey, out Sprite cachedSprite))
                    {
                        var cb = pending.Callback;
                        QueueUICallback(() => cb?.Invoke(cachedSprite), isHighPriority);
                    }
                    else
                    {
                        // Fallback to result if cache miss (shouldn't happen normally)
                        var cb = pending.Callback;
                        QueueUICallback(() => cb?.Invoke(result), isHighPriority);
                    }
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
    /// High priority callbacks (detail panel) are executed immediately for responsiveness.
    /// </summary>
    private void QueueUICallback(Action callback, bool highPriority = false)
    {
        if (callback == null) return;

        if (highPriority)
        {
            // Execute high priority callbacks immediately for responsive detail panel
            try
            {
                callback.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileThumbnailService] High priority callback error: {ex.Message}");
            }
        }
        else
        {
            _uiCallbackQueue.Enqueue(callback);
        }
    }

    /// <summary>
    /// Process queued UI callbacks with a frame budget to prevent stutters.
    /// Only processes normal priority callbacks (grid/list thumbnails).
    /// High-priority callbacks are executed immediately in QueueUICallback.
    /// </summary>
    private IEnumerator ProcessUICallbacksCoroutine()
    {
        while (true)
        {
            // Process up to MAX_UI_CALLBACKS_PER_FRAME callbacks per frame
            int processed = 0;

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

            // Ensure dimensions are multiples of 4 for GPU alignment
            width = (width + 3) & ~3;
            height = (height + 3) & ~3;
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

        // Use AsyncGPUReadback WITHOUT specifying format - let Unity handle conversion
        bool readbackComplete = false;
        Texture2D result = null;
        Exception readbackError = null;

        AsyncGPUReadback.Request(rt, 0, (request) =>
        {
            if (request.hasError)
            {
                readbackError = new Exception("AsyncGPUReadback failed");
            }
            else
            {
                try
                {
                    // Create texture with the same format as returned by readback
                    var data = request.GetData<byte>();
                    result = new Texture2D(width, height, TextureFormat.ARGB32, true);
                    result.filterMode = FilterMode.Trilinear;
                    result.anisoLevel = 16;
                    result.wrapMode = TextureWrapMode.Clamp;
                    result.LoadRawTextureData(data);
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

            // Ensure dimensions are multiples of 4 for GPU alignment
            width = (width + 3) & ~3;
            height = (height + 3) & ~3;
        }
        else
        {
            return source;
        }

        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Trilinear;
        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(width, height, TextureFormat.ARGB32, true);
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
