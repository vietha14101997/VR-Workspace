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
    private static readonly string[] VIDEO_EXTENSIONS = { ".mp4", ".mkv", ".avi", ".webm", ".mov", ".wmv" };
    private const string FAVORITES_KEY = "MediaLibrary_Favorites";
    private const string HISTORY_KEY = "MediaLibrary_History";
    private const int MAX_HISTORY = 50;
    #endregion

    #region Events
    public event Action<int, int> OnScanProgress;  // (current, total)
    public event Action<List<MediaVideoInfo>> OnScanComplete;
#pragma warning disable CS0067 // Event reserved for error handling during scan
    public event Action<string> OnScanError;
#pragma warning restore CS0067
    #endregion

    #region Properties
    public List<MediaVideoInfo> AllVideos { get; private set; } = new List<MediaVideoInfo>();
    public bool IsScanning { get; private set; }
    public int TotalVideoCount => AllVideos.Count;
    #endregion

    #region Private Fields
    private HashSet<string> _favorites = new HashSet<string>();
    private List<string> _playbackHistory = new List<string>();
    private Coroutine _scanCoroutine;
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

    private IEnumerator ScanCoroutine(Action<List<MediaVideoInfo>> onComplete)
    {
        IsScanning = true;
        AllVideos.Clear();

        var foundFiles = new List<string>();
        var scanRoots = GetScanRoots();

        Debug.Log($"[MediaLibraryService] Starting scan in {scanRoots.Count} locations");

        // Collect all video files
        foreach (var root in scanRoots)
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => VIDEO_EXTENSIONS.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                foundFiles.AddRange(files);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MediaLibraryService] Error scanning {root}: {ex.Message}");
            }

            yield return null; // Yield between roots
        }

        Debug.Log($"[MediaLibraryService] Found {foundFiles.Count} video files");

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

        // Sort by name
        AllVideos = AllVideos.OrderBy(v => v.Title).ToList();

        IsScanning = false;
        OnScanProgress?.Invoke(total, total);
        OnScanComplete?.Invoke(AllVideos);
        onComplete?.Invoke(AllVideos);

        Debug.Log($"[MediaLibraryService] Scan complete: {AllVideos.Count} videos");
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
        // Editor/Desktop - scan common video folders
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        roots.Add(Path.Combine(userProfile, "Videos"));
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
    #endregion
}
