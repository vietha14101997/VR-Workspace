using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Service quản lý thư viện video - scanning, search, metadata, favorites.
/// </summary>
public class MediaLibraryService : MonoBehaviour
{
    #region Singleton
    private static MediaLibraryService _instance;
    public static MediaLibraryService Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("MediaLibraryService");
                _instance = go.AddComponent<MediaLibraryService>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    #endregion

    #region Constants
    private static readonly string[] VIDEO_EXTENSIONS = { ".mp4", ".mkv", ".avi", ".webm", ".mov", ".wmv", ".m4v", ".flv" };
    private static readonly string[] IMAGE_EXTENSIONS = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
    private static readonly string[] AUDIO_EXTENSIONS = { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma" };
    private const string FAVORITES_KEY = "MediaLibrary_Favorites";
    private const string HISTORY_KEY = "MediaLibrary_History";
    private const string LIBRARY_CACHE_KEY = "MediaLibrary_Cache";
    private const int MAX_HISTORY = 50;
    #endregion

    #region Events
    public event Action<int, int> OnScanProgress;  // (current, total)
    public event Action<List<MediaVideoInfo>> OnScanComplete;
#pragma warning disable CS0067 // Event reserved for error handling during scan
    public event Action<string> OnScanError;
#pragma warning restore CS0067
    /// <summary>
    /// Fired when library cache has been loaded (async).
    /// </summary>
    public event Action OnLibraryCacheLoaded;
    /// <summary>
    /// Fired when background metadata refresh completes (DateAdded, FileSize updated).
    /// UI can optionally refresh to reflect updated sorting.
    /// </summary>
    public event Action OnMetadataRefreshComplete;
    #endregion

    #region Properties
    public List<MediaVideoInfo> AllVideos { get; private set; } = new List<MediaVideoInfo>();
    public bool IsScanning { get; private set; }
    public bool IsCacheLoaded { get; private set; }
    public bool IsCacheLoading { get; private set; }
    public int TotalVideoCount => AllVideos.Count;
    #endregion

    #region Private Fields
    private HashSet<string> _favorites = new HashSet<string>();
    private List<string> _playbackHistory = new List<string>();
    private Coroutine _scanCoroutine;
    private Coroutine _cacheLoadCoroutine;
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
        DontDestroyOnLoad(gameObject);

        LoadFavorites();
        LoadHistory();
        // Load cache asynchronously to avoid blocking main thread
        _cacheLoadCoroutine = StartCoroutine(LoadLibraryCacheAsync());
    }
    #endregion

    #region Scanning
    /// <summary>
    /// Scan toàn bộ storage để tìm video files.
    /// </summary>
    public void ScanMediaLibrary(Action<List<MediaVideoInfo>> onComplete = null)
    {
        if (IsScanning)
        {
            Debug.LogWarning("[MediaLibraryService] Already scanning");
            return;
        }

        if (_scanCoroutine != null)
        {
            StopCoroutine(_scanCoroutine);
        }

        _scanCoroutine = StartCoroutine(ScanCoroutine(onComplete));
    }

    /// <summary>
    /// Stop current scan.
    /// </summary>
    public void StopScan()
    {
        if (_scanCoroutine != null)
        {
            StopCoroutine(_scanCoroutine);
            _scanCoroutine = null;
        }
        IsScanning = false;
    }

    #region Incremental Background Scan
    /// <summary>
    /// Event fired when new items are detected during background scan.
    /// </summary>
    public event Action<List<MediaVideoInfo>> OnNewItemsDetected;

    private Coroutine _backgroundScanCoroutine;
    private bool _isBackgroundScanning = false;

    /// <summary>
    /// Perform a background incremental scan to detect new files.
    /// Does NOT interrupt current display - only adds new items if found.
    /// </summary>
    public void StartBackgroundScan()
    {
        if (_isBackgroundScanning || IsScanning)
        {
            Debug.Log("[MediaLibraryService] Background scan skipped - already scanning");
            return;
        }

        if (_backgroundScanCoroutine != null)
        {
            StopCoroutine(_backgroundScanCoroutine);
        }

        _backgroundScanCoroutine = StartCoroutine(BackgroundScanCoroutine());
    }

    private IEnumerator BackgroundScanCoroutine()
    {
        _isBackgroundScanning = true;
        var newItems = new List<MediaVideoInfo>();
        var existingPaths = new HashSet<string>(AllVideos.Select(v => v.Path));

        Debug.Log($"[MediaLibraryService] Starting background scan (existing: {existingPaths.Count} items)");
        var startTime = System.Diagnostics.Stopwatch.StartNew();

#if UNITY_ANDROID && !UNITY_EDITOR
        // Use MediaStore for fast query
        var mediaItems = AndroidMediaStoreHelper.QueryAllMedia();

        foreach (var item in mediaItems)
        {
            if (!existingPaths.Contains(item.Path))
            {
                try
                {
                    var videoInfo = CreateVideoInfoFromMediaStore(item);
                    newItems.Add(videoInfo);
                }
                catch { }
            }
        }

        yield return null;
#else
        // Use directory scan for Editor/Desktop
        var scanRoots = GetScanRoots();
        var allExtensions = VIDEO_EXTENSIONS.Concat(IMAGE_EXTENSIONS).Concat(AUDIO_EXTENSIONS).ToHashSet();

        foreach (var root in scanRoots)
        {
            if (!Directory.Exists(root)) continue;

            string[] files = null;
            try
            {
                files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            }
            catch { continue; }

            foreach (var filePath in files)
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (!allExtensions.Contains(ext)) continue;

                if (!existingPaths.Contains(filePath))
                {
                    try
                    {
                        var videoInfo = CreateVideoInfo(filePath);
                        newItems.Add(videoInfo);
                    }
                    catch { }
                }
            }

            yield return null; // Yield between roots to keep UI responsive
        }
#endif

        _isBackgroundScanning = false;

        if (newItems.Count > 0)
        {
            Debug.Log($"[MediaLibraryService] Background scan found {newItems.Count} new items in {startTime.ElapsedMilliseconds}ms");

            // Add new items to AllVideos
            AllVideos.AddRange(newItems);
            AllVideos = AllVideos.OrderBy(v => v.Title).ToList();

            // Update cache
            SaveLibraryCache();

            // Notify listeners
            OnNewItemsDetected?.Invoke(newItems);
        }
        else
        {
            Debug.Log($"[MediaLibraryService] Background scan complete - no new items ({startTime.ElapsedMilliseconds}ms)");
        }
    }
    #endregion

    private IEnumerator ScanCoroutine(Action<List<MediaVideoInfo>> onComplete)
    {
        IsScanning = true;
        AllVideos.Clear();

#if UNITY_ANDROID && !UNITY_EDITOR
        // Use Android MediaStore for fast queries (instant vs seconds/minutes of scanning)
        yield return ScanUsingMediaStore();
#else
        // Fallback to directory scanning for Editor/Desktop
        yield return ScanUsingDirectories();
#endif

        // Sort by name
        AllVideos = AllVideos.OrderBy(v => v.Title).ToList();

        // Save to cache for faster loading next time
        SaveLibraryCache();

        IsScanning = false;
        OnScanProgress?.Invoke(AllVideos.Count, AllVideos.Count);
        OnScanComplete?.Invoke(AllVideos);
        onComplete?.Invoke(AllVideos);

        Debug.Log($"[MediaLibraryService] Scan complete: {AllVideos.Count} media files (cached)");
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Scan using Android MediaStore API - much faster than directory scanning.
    /// MediaStore is a system-maintained database that indexes all media files.
    /// </summary>
    private IEnumerator ScanUsingMediaStore()
    {
        Debug.Log("[MediaLibraryService] Using Android MediaStore for fast media discovery...");
        var startTime = System.Diagnostics.Stopwatch.StartNew();

        // Query all media from MediaStore (this is very fast - typically < 100ms)
        var mediaItems = AndroidMediaStoreHelper.QueryAllMedia();

        Debug.Log($"[MediaLibraryService] MediaStore query returned {mediaItems.Count} items in {startTime.ElapsedMilliseconds}ms");

        int processed = 0;
        int total = mediaItems.Count;

        foreach (var item in mediaItems)
        {
            try
            {
                // Convert MediaStoreItem to MediaVideoInfo
                var videoInfo = CreateVideoInfoFromMediaStore(item);
                AllVideos.Add(videoInfo);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MediaLibraryService] Error processing MediaStore item {item.Path}: {ex.Message}");
            }

            processed++;

            // Report progress and yield every 50 items (MediaStore is fast, so we can batch more)
            if (processed % 50 == 0)
            {
                OnScanProgress?.Invoke(processed, total);
                yield return null;
            }
        }

        Debug.Log($"[MediaLibraryService] MediaStore scan complete in {startTime.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Create MediaVideoInfo from AndroidMediaStoreHelper.MediaStoreItem.
    /// </summary>
    private MediaVideoInfo CreateVideoInfoFromMediaStore(AndroidMediaStoreHelper.MediaStoreItem item)
    {
        var info = new MediaVideoInfo
        {
            Path = item.Path,
            Title = !string.IsNullOrEmpty(item.Title) ? item.Title :
                    (!string.IsNullOrEmpty(item.DisplayName) ? Path.GetFileNameWithoutExtension(item.DisplayName) : "Unknown"),
            FileSizeBytes = item.SizeBytes,
            DateAdded = item.DateAdded,
            DateModified = item.DateModified,
            Duration = TimeSpan.FromMilliseconds(item.DurationMs),
            Width = item.Width,
            Height = item.Height,
            IsFavorite = _favorites.Contains(item.Path),
            Format = MediaVideoInfo.DetectFormat(item.Path),
            PlaylistIds = new List<string>()
        };

        // Detect projection from filename and dimensions
        info.Projection = ProjectionDetector.DetectProjection(item.Path, item.Width, item.Height);

        return info;
    }
#endif

    /// <summary>
    /// Scan using traditional directory traversal (for Editor/Desktop).
    /// </summary>
    private IEnumerator ScanUsingDirectories()
    {
        var foundFiles = new List<string>();
        var scanRoots = GetScanRoots();

        Debug.Log($"[MediaLibraryService] Starting directory scan in {scanRoots.Count} locations: {string.Join(", ", scanRoots)}");

        // Combine all supported extensions
        var allExtensions = VIDEO_EXTENSIONS.Concat(IMAGE_EXTENSIONS).Concat(AUDIO_EXTENSIONS).ToHashSet();

        // Collect all media files
        foreach (var root in scanRoots)
        {
            if (!Directory.Exists(root))
            {
                Debug.Log($"[MediaLibraryService] Skipping non-existent root: {root}");
                continue;
            }
            Debug.Log($"[MediaLibraryService] Scanning: {root}");

            try
            {
                var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => allExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                foundFiles.AddRange(files);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MediaLibraryService] Error scanning {root}: {ex.Message}");
            }

            yield return null; // Yield between roots
        }

        Debug.Log($"[MediaLibraryService] Found {foundFiles.Count} media files");

        // Process files and create MediaVideoInfo
        int processed = 0;
        int total = foundFiles.Count;

        foreach (var filePath in foundFiles)
        {
            try
            {
                var videoInfo = CreateVideoInfo(filePath);
                AllVideos.Add(videoInfo);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MediaLibraryService] Error processing {filePath}: {ex.Message}");
            }

            processed++;

            // Report progress every 10 files
            if (processed % 10 == 0)
            {
                OnScanProgress?.Invoke(processed, total);
                yield return null;
            }
        }
    }

    private List<string> GetScanRoots()
    {
        var roots = new List<string>();

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android external storage
        string externalStorage = "/storage/emulated/0";
        if (Directory.Exists(externalStorage))
        {
            roots.Add(Path.Combine(externalStorage, "Movies"));
            roots.Add(Path.Combine(externalStorage, "Download"));
            roots.Add(Path.Combine(externalStorage, "DCIM"));
            roots.Add(Path.Combine(externalStorage, "Video"));
            roots.Add(Path.Combine(externalStorage, "VR"));
        }

        // SD Card
        string sdCard = "/storage/sdcard1";
        if (Directory.Exists(sdCard))
        {
            roots.Add(sdCard);
        }
#else
        // Editor/Desktop - scan common media folders
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        roots.Add(Path.Combine(userProfile, "Videos"));
        roots.Add(Path.Combine(userProfile, "Pictures"));
        roots.Add(Path.Combine(userProfile, "Music"));
        roots.Add(Path.Combine(userProfile, "Downloads"));

        // Also scan project's StreamingAssets for testing
        if (Directory.Exists(Application.streamingAssetsPath))
        {
            roots.Add(Application.streamingAssetsPath);
        }
#endif

        return roots.Where(Directory.Exists).ToList();
    }

    private MediaVideoInfo CreateVideoInfo(string filePath)
    {
        var info = MediaVideoInfo.FromPath(filePath);

        // Check favorites
        info.IsFavorite = _favorites.Contains(filePath);

        // Detect projection from filename (resolution will be updated when video is loaded)
        info.Projection = ProjectionDetector.DetectProjection(filePath, 0, 0);

        return info;
    }
    #endregion

    #region Query Methods
    /// <summary>
    /// Get all videos.
    /// </summary>
    public List<MediaVideoInfo> GetAllVideos()
    {
        return AllVideos.ToList();
    }

    /// <summary>
    /// Get recently played videos.
    /// </summary>
    public List<MediaVideoInfo> GetRecentVideos(int count = 50)
    {
        var recent = new List<MediaVideoInfo>();

        foreach (var path in _playbackHistory.Take(count))
        {
            var video = AllVideos.FirstOrDefault(v => v.Path == path);
            if (video.Path != null)
            {
                recent.Add(video);
            }
        }

        return recent;
    }

    /// <summary>
    /// Get favorite videos.
    /// </summary>
    public List<MediaVideoInfo> GetFavorites()
    {
        return AllVideos.Where(v => v.IsFavorite).ToList();
    }

    /// <summary>
    /// Filter by projection type.
    /// </summary>
    public List<MediaVideoInfo> FilterByProjection(VideoProjectionType type)
    {
        if (type == VideoProjectionType.Flat)
        {
            // Flat includes standard 2D videos
            return AllVideos.Where(v => v.Projection == VideoProjectionType.Flat).ToList();
        }

        return AllVideos.Where(v => v.Projection == type).ToList();
    }

    /// <summary>
    /// Filter by format.
    /// </summary>
    public List<MediaVideoInfo> FilterByFormat(VideoFormat format)
    {
        return AllVideos.Where(v => v.Format == format).ToList();
    }

    /// <summary>
    /// Filter by duration range.
    /// </summary>
    public List<MediaVideoInfo> FilterByDuration(DurationRange range)
    {
        return AllVideos.Where(v => v.DurationCategory == range).ToList();
    }

    /// <summary>
    /// Search videos by title.
    /// </summary>
    public List<MediaVideoInfo> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return AllVideos.ToList();

        query = query.ToLowerInvariant();
        return AllVideos.Where(v =>
            v.Title.ToLowerInvariant().Contains(query) ||
            v.Path.ToLowerInvariant().Contains(query)
        ).ToList();
    }

    /// <summary>
    /// Get video by path.
    /// </summary>
    public MediaVideoInfo? GetVideoByPath(string path)
    {
        var video = AllVideos.FirstOrDefault(v => v.Path == path);
        return video.Path != null ? video : (MediaVideoInfo?)null;
    }

    /// <summary>
    /// Apply multiple filters.
    /// </summary>
    public List<MediaVideoInfo> ApplyFilters(
        VideoProjectionType? projection = null,
        VideoFormat? format = null,
        DurationRange? duration = null,
        string searchQuery = null)
    {
        IEnumerable<MediaVideoInfo> result = AllVideos;

        if (projection.HasValue)
            result = result.Where(v => v.Projection == projection.Value);

        if (format.HasValue)
            result = result.Where(v => v.Format == format.Value);

        if (duration.HasValue && duration.Value != DurationRange.All)
            result = result.Where(v => v.DurationCategory == duration.Value);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var query = searchQuery.ToLowerInvariant();
            result = result.Where(v => v.Title.ToLowerInvariant().Contains(query));
        }

        return result.ToList();
    }
    #endregion

    #region Favorites
    /// <summary>
    /// Set favorite status for a video.
    /// </summary>
    public void SetFavorite(string path, bool isFavorite)
    {
        if (isFavorite)
        {
            _favorites.Add(path);
        }
        else
        {
            _favorites.Remove(path);
        }

        // Update in AllVideos list
        for (int i = 0; i < AllVideos.Count; i++)
        {
            if (AllVideos[i].Path == path)
            {
                var video = AllVideos[i];
                video.IsFavorite = isFavorite;
                AllVideos[i] = video;
                break;
            }
        }

        SaveFavorites();
    }

    /// <summary>
    /// Toggle favorite status.
    /// </summary>
    public bool ToggleFavorite(string path)
    {
        bool newState = !_favorites.Contains(path);
        SetFavorite(path, newState);
        return newState;
    }

    /// <summary>
    /// Check if video is favorite.
    /// </summary>
    public bool IsFavorite(string path)
    {
        return _favorites.Contains(path);
    }

    /// <summary>
    /// Add video to favorites.
    /// </summary>
    public void AddToFavorites(string path)
    {
        SetFavorite(path, true);
    }

    /// <summary>
    /// Remove video from favorites.
    /// </summary>
    public void RemoveFromFavorites(string path)
    {
        SetFavorite(path, false);
    }

    private void LoadFavorites()
    {
        string json = PlayerPrefs.GetString(FAVORITES_KEY, "[]");
        try
        {
            var list = JsonUtility.FromJson<StringListWrapper>(json);
            _favorites = new HashSet<string>(list?.items ?? new List<string>());
        }
        catch
        {
            _favorites = new HashSet<string>();
        }
    }

    private void SaveFavorites()
    {
        var wrapper = new StringListWrapper { items = _favorites.ToList() };
        string json = JsonUtility.ToJson(wrapper);
        PlayerPrefs.SetString(FAVORITES_KEY, json);
        PlayerPrefs.Save();
    }
    #endregion

    #region Playback History
    /// <summary>
    /// Record video playback in history.
    /// </summary>
    public void RecordPlayback(string path)
    {
        // Remove if already in history
        _playbackHistory.Remove(path);

        // Add to front
        _playbackHistory.Insert(0, path);

        // Trim to max
        if (_playbackHistory.Count > MAX_HISTORY)
        {
            _playbackHistory.RemoveRange(MAX_HISTORY, _playbackHistory.Count - MAX_HISTORY);
        }

        // Update LastPlayed in AllVideos
        for (int i = 0; i < AllVideos.Count; i++)
        {
            if (AllVideos[i].Path == path)
            {
                var video = AllVideos[i];
                video.LastPlayed = DateTime.Now;
                AllVideos[i] = video;
                break;
            }
        }

        SaveHistory();
    }

    /// <summary>
    /// Get playback history.
    /// </summary>
    public List<MediaVideoInfo> GetPlaybackHistory(int count = 50)
    {
        return GetRecentVideos(count);
    }

    private void LoadHistory()
    {
        string json = PlayerPrefs.GetString(HISTORY_KEY, "[]");
        try
        {
            var list = JsonUtility.FromJson<StringListWrapper>(json);
            _playbackHistory = list?.items ?? new List<string>();
        }
        catch
        {
            _playbackHistory = new List<string>();
        }
    }

    private void SaveHistory()
    {
        var wrapper = new StringListWrapper { items = _playbackHistory };
        string json = JsonUtility.ToJson(wrapper);
        PlayerPrefs.SetString(HISTORY_KEY, json);
        PlayerPrefs.Save();
    }
    #endregion

    #region Library Cache
    /// <summary>
    /// Load cached library data asynchronously to avoid blocking main thread.
    /// Uses batched processing with yield to maintain 60fps.
    /// </summary>
    private IEnumerator LoadLibraryCacheAsync()
    {
        IsCacheLoading = true;
        IsCacheLoaded = false;

        string json = PlayerPrefs.GetString(LIBRARY_CACHE_KEY, "");
        if (string.IsNullOrEmpty(json))
        {
            Debug.Log("[MediaLibraryService] No library cache found");
            IsCacheLoading = false;
            IsCacheLoaded = true;
            OnLibraryCacheLoaded?.Invoke();
            yield break;
        }

        LibraryCacheWrapper cache = null;
        try
        {
            cache = JsonUtility.FromJson<LibraryCacheWrapper>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MediaLibraryService] Failed to parse library cache: {ex.Message}");
            IsCacheLoading = false;
            IsCacheLoaded = true;
            OnLibraryCacheLoaded?.Invoke();
            yield break;
        }

        if (cache?.paths == null || cache.paths.Count == 0)
        {
            Debug.Log("[MediaLibraryService] Library cache is empty");
            IsCacheLoading = false;
            IsCacheLoaded = true;
            OnLibraryCacheLoaded?.Invoke();
            yield break;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log($"[MediaLibraryService] Loading {cache.paths.Count} items from cache (async)...");

        // Clear existing
        AllVideos.Clear();

        // OPTIMIZATION: Skip File.Exists() during initial load
        // Files will be validated lazily when accessed
        // This saves ~0.5-1ms per file = 500-1000ms for 1000+ files
        const int BATCH_SIZE = 100;  // Process 100 items per frame
        int processedCount = 0;

        // Check if cache has metadata (backward compatibility)
        bool hasMetadata = cache.fileSizes != null && cache.fileSizes.Count == cache.paths.Count;

        for (int i = 0; i < cache.paths.Count; i++)
        {
            string path = cache.paths[i];

            // Use quick version that doesn't do File.Exists or FileInfo I/O
            // Pass cached metadata if available
            long fileSize = hasMetadata ? cache.fileSizes[i] : 0;
            long dateAddedTicks = hasMetadata ? cache.dateAddedTicks[i] : 0;
            long dateModifiedTicks = hasMetadata ? cache.dateModifiedTicks[i] : 0;

            var info = CreateVideoInfoQuick(path, fileSize, dateAddedTicks, dateModifiedTicks);
            AllVideos.Add(info);
            processedCount++;

            // Yield every BATCH_SIZE items to avoid blocking main thread
            if (processedCount % BATCH_SIZE == 0)
            {
                yield return null;
            }
        }

        // Sort once at the end
        AllVideos = AllVideos.OrderBy(v => v.Title).ToList();

        sw.Stop();
        Debug.Log($"[MediaLibraryService] Loaded {AllVideos.Count} items from cache in {sw.ElapsedMilliseconds}ms (async, no File.Exists)");

        IsCacheLoading = false;
        IsCacheLoaded = true;
        OnLibraryCacheLoaded?.Invoke();

        // If cache didn't have metadata, refresh it in background
        if (!hasMetadata && AllVideos.Count > 0)
        {
            Debug.Log("[MediaLibraryService] Cache missing metadata, starting background refresh...");
            StartCoroutine(RefreshMetadataInBackground());
        }
    }

    /// <summary>
    /// Refresh file metadata (DateAdded, FileSize) in background.
    /// Updates AllVideos and saves to cache when complete.
    /// </summary>
    private IEnumerator RefreshMetadataInBackground()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int BATCH_SIZE = 50;  // Fewer per frame to minimize impact
        int processedCount = 0;
        int updatedCount = 0;

        for (int i = 0; i < AllVideos.Count; i++)
        {
            var video = AllVideos[i];

            // Only update if metadata is missing
            if (video.DateModified == DateTime.MinValue || video.FileSizeBytes == 0)
            {
                try
                {
                    if (File.Exists(video.Path))
                    {
                        var fileInfo = new FileInfo(video.Path);
                        video.FileSizeBytes = fileInfo.Length;
                        video.DateModified = fileInfo.LastWriteTime;
                        video.DateAdded = fileInfo.CreationTime;
                        AllVideos[i] = video;
                        updatedCount++;
                    }
                }
                catch { }
            }

            processedCount++;

            // Yield every BATCH_SIZE to avoid blocking
            if (processedCount % BATCH_SIZE == 0)
            {
                yield return null;
            }
        }

        sw.Stop();
        Debug.Log($"[MediaLibraryService] Background metadata refresh complete: {updatedCount}/{AllVideos.Count} items in {sw.ElapsedMilliseconds}ms");

        // Save updated cache
        SaveLibraryCache();

        // Notify listeners that metadata is now available
        OnMetadataRefreshComplete?.Invoke();
    }

    /// <summary>
    /// Quick version of CreateVideoInfo that doesn't perform disk I/O.
    /// Uses cached metadata if available, otherwise lazy validation needed.
    /// </summary>
    private MediaVideoInfo CreateVideoInfoQuick(string filePath, long fileSize = 0, long dateAddedTicks = 0, long dateModifiedTicks = 0)
    {
        var info = new MediaVideoInfo
        {
            Path = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            Format = MediaVideoInfo.DetectFormat(filePath),
            PlaylistIds = new List<string>(),
            IsFavorite = _favorites.Contains(filePath),
            // Use cached metadata if available
            DateAdded = dateAddedTicks > 0 ? new DateTime(dateAddedTicks) : DateTime.MinValue,
            DateModified = dateModifiedTicks > 0 ? new DateTime(dateModifiedTicks) : DateTime.MinValue,
            FileSizeBytes = fileSize
        };

        // Detect projection from filename (no I/O needed)
        info.Projection = ProjectionDetector.DetectProjection(filePath, 0, 0);

        return info;
    }

    /// <summary>
    /// Validate and update file metadata for a MediaVideoInfo.
    /// Call this lazily when file details are actually needed.
    /// Returns false if file doesn't exist.
    /// </summary>
    public bool ValidateAndUpdateFileInfo(ref MediaVideoInfo info)
    {
        if (string.IsNullOrEmpty(info.Path)) return false;

        try
        {
            if (!File.Exists(info.Path)) return false;

            // Only update if not already populated
            if (info.DateModified == DateTime.MinValue || info.FileSizeBytes == 0)
            {
                var fileInfo = new FileInfo(info.Path);
                info.FileSizeBytes = fileInfo.Length;
                info.DateModified = fileInfo.LastWriteTime;
                info.DateAdded = fileInfo.CreationTime;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Save library data to cache for faster startup.
    /// Includes metadata (DateAdded, FileSize) to enable sorting without disk I/O.
    /// </summary>
    private void SaveLibraryCache()
    {
        try
        {
            var cache = new LibraryCacheWrapper();
            foreach (var video in AllVideos)
            {
                cache.paths.Add(video.Path);
                cache.fileSizes.Add(video.FileSizeBytes);
                cache.dateAddedTicks.Add(video.DateAdded.Ticks);
                cache.dateModifiedTicks.Add(video.DateModified.Ticks);
            }
            string json = JsonUtility.ToJson(cache);
            PlayerPrefs.SetString(LIBRARY_CACHE_KEY, json);
            PlayerPrefs.Save();
            Debug.Log($"[MediaLibraryService] Saved {cache.paths.Count} items to cache (with metadata)");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MediaLibraryService] Failed to save library cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear the library cache (forces rescan on next load).
    /// </summary>
    public void ClearLibraryCache()
    {
        PlayerPrefs.DeleteKey(LIBRARY_CACHE_KEY);
        PlayerPrefs.Save();
        AllVideos.Clear();
        Debug.Log("[MediaLibraryService] Library cache cleared");
    }
    #endregion

    #region Sorting
    /// <summary>
    /// Sort videos by property.
    /// </summary>
    public List<MediaVideoInfo> Sort(List<MediaVideoInfo> videos, string sortBy, bool ascending = true)
    {
        IOrderedEnumerable<MediaVideoInfo> sorted;

        switch (sortBy.ToLowerInvariant())
        {
            case "name":
            case "title":
                sorted = ascending
                    ? videos.OrderBy(v => v.Title)
                    : videos.OrderByDescending(v => v.Title);
                break;

            case "date":
            case "modified":
                sorted = ascending
                    ? videos.OrderBy(v => v.DateModified)
                    : videos.OrderByDescending(v => v.DateModified);
                break;

            case "size":
                sorted = ascending
                    ? videos.OrderBy(v => v.FileSizeBytes)
                    : videos.OrderByDescending(v => v.FileSizeBytes);
                break;

            case "duration":
                sorted = ascending
                    ? videos.OrderBy(v => v.Duration)
                    : videos.OrderByDescending(v => v.Duration);
                break;

            case "type":
            case "projection":
                sorted = ascending
                    ? videos.OrderBy(v => v.Projection)
                    : videos.OrderByDescending(v => v.Projection);
                break;

            default:
                sorted = videos.OrderBy(v => v.Title);
                break;
        }

        return sorted.ToList();
    }
    #endregion

    #region Helper Classes
    [Serializable]
    private class StringListWrapper
    {
        public List<string> items = new List<string>();
    }

    [Serializable]
    private class LibraryCacheWrapper
    {
        public List<string> paths = new List<string>();
        // Cached metadata to avoid disk I/O on load
        public List<long> fileSizes = new List<long>();
        public List<long> dateAddedTicks = new List<long>();
        public List<long> dateModifiedTicks = new List<long>();
    }
    #endregion
}
