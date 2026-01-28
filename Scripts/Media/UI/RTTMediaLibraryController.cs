using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Controller for Media Library - manages data flow between MediaLibraryService and UI.
/// Handles filtering, searching, category selection, and user actions.
/// </summary>
public class RTTMediaLibraryController : MonoBehaviour, IPaginationController
{
    #region Events
    public event Action<MediaVideoInfo> OnVideoPlayRequested;
    public event Action OnCloseRequested;
    #endregion

    #region Properties
    public string CurrentCategory { get; private set; } = "videos";
    public string CurrentSearchQuery { get; private set; } = "";
    public VideoProjectionType? CurrentProjectionFilter { get; private set; } = null;
    public VideoFormat? CurrentFormatFilter { get; private set; } = null;
    public DurationRange? CurrentDurationFilter { get; private set; } = null;
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int PageSize { get; private set; } = 6;
    public string SortBy { get; private set; } = "Name";
    public bool IsAscending { get; private set; } = true;
    #endregion

    #region Private Fields
    private RTTMediaLibrary _view;
    private MediaLibraryService _libraryService;
    private List<MediaVideoInfo> _allVideos = new List<MediaVideoInfo>();
    private List<MediaVideoInfo> _filteredVideos = new List<MediaVideoInfo>();
    private bool _isInitialized = false;
    #endregion

    #region Initialization
    public void Initialize(RTTMediaLibrary view)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[RTTMediaLibraryController] Already initialized");
            return;
        }

        _view = view;
        _libraryService = MediaLibraryService.Instance;

        // Wire view events
        _view.OnVideoPlayRequested += HandleVideoPlayRequested;
        _view.OnCloseRequested += HandleCloseRequested;

        // Subscribe to library service events
        if (_libraryService != null)
        {
            _libraryService.OnScanProgress += HandleScanProgress;
            _libraryService.OnScanComplete += HandleScanComplete;
        }

        // Note: Side panel events are now wired in OnViewReady() because panels are created asynchronously

        _isInitialized = true;
        Debug.Log($"[RTTMediaLibraryController] Initialized, libraryService={((_libraryService != null) ? "OK" : "NULL")}");

        // Note: Don't load data yet - wait for OnViewReady() when all panels are created
    }

    private void HandleScanProgress(int current, int total)
    {
        // Update UI during scan
        _view?.UpdateItemCount(current);
    }

    private void HandleScanComplete(List<MediaVideoInfo> videos)
    {
        Debug.Log($"[RTTMediaLibraryController] HandleScanComplete: {videos.Count} files");
        _allVideos = videos;
        ApplyFilters();
    }

    /// <summary>
    /// Called by RTTMediaLibrary when all panels (including async side panels) are ready.
    /// </summary>
    public void OnViewReady()
    {
        Debug.Log("[RTTMediaLibraryController] View ready, loading data...");

        // Load initial data
        RefreshLibrary();
    }

    /// <summary>
    /// Select a category from the side panel.
    /// Called by RTTMediaLibrary when side panel fires OnCategorySelected.
    /// </summary>
    public void SelectCategory(string categoryId)
    {
        HandleCategorySelected(categoryId);
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Refresh library data from storage.
    /// </summary>
    public void RefreshLibrary()
    {
        Debug.Log($"[RTTMediaLibraryController] RefreshLibrary called, _libraryService={((_libraryService != null) ? "OK" : "NULL")}");

        if (_libraryService == null)
        {
            Debug.LogError("[RTTMediaLibraryController] LibraryService not available!");
            return;
        }

        // Check if media has been scanned
        _allVideos = _libraryService.GetAllVideos();
        Debug.Log($"[RTTMediaLibraryController] GetAllVideos returned {_allVideos.Count} items, IsScanning={_libraryService.IsScanning}");

        if (_allVideos.Count == 0 && !_libraryService.IsScanning)
        {
            // No videos and not scanning - trigger scan
            Debug.Log("[RTTMediaLibraryController] Starting media scan...");
            _libraryService.ScanMediaLibrary(OnScanComplete);
        }
        else if (_libraryService.IsScanning)
        {
            Debug.Log("[RTTMediaLibraryController] Scan already in progress, waiting...");
            // UI will be updated via OnScanComplete event
        }
        else
        {
            Debug.Log($"[RTTMediaLibraryController] Using cached data: {_allVideos.Count} items");
            ApplyFilters();
        }
    }

    /// <summary>
    /// Called when media scan completes.
    /// </summary>
    private void OnScanComplete(List<MediaVideoInfo> videos)
    {
        _allVideos = videos;
        Debug.Log($"[RTTMediaLibraryController] Scan complete: {_allVideos.Count} videos");
        ApplyFilters();
    }

    /// <summary>
    /// Search videos by query.
    /// </summary>
    public void Search(string query)
    {
        CurrentSearchQuery = query?.Trim() ?? "";
        ApplyFilters();
    }

    /// <summary>
    /// Toggle favorite status for video.
    /// </summary>
    public void ToggleFavorite(MediaVideoInfo video)
    {
        if (_libraryService == null) return;

        bool newState = !video.IsFavorite;

        if (newState)
        {
            _libraryService.AddToFavorites(video.Path);
        }
        else
        {
            _libraryService.RemoveFromFavorites(video.Path);
        }

        // Update local data
        UpdateVideoFavoriteState(video.Path, newState);

        // Refresh display
        ApplyFilters();

        // Update detail panel if same video
        if (_view.DetailPanel != null)
        {
            _view.DetailPanel.UpdateFavoriteState(newState);
        }
    }

    /// <summary>
    /// Set projection filter.
    /// </summary>
    public void SetProjectionFilter(VideoProjectionType? projection)
    {
        CurrentProjectionFilter = projection;
        ApplyFilters();
    }

    /// <summary>
    /// Set format filter.
    /// </summary>
    public void SetFormatFilter(VideoFormat? format)
    {
        CurrentFormatFilter = format;
        ApplyFilters();
    }

    /// <summary>
    /// Set duration filter.
    /// </summary>
    public void SetDurationFilter(DurationRange? duration)
    {
        CurrentDurationFilter = duration;
        ApplyFilters();
    }

    /// <summary>
    /// Clear all filters.
    /// </summary>
    public void ClearFilters()
    {
        CurrentSearchQuery = "";
        CurrentProjectionFilter = null;
        CurrentFormatFilter = null;
        CurrentDurationFilter = null;
        ApplyFilters();
    }

    /// <summary>
    /// Add video to playback history.
    /// </summary>
    public void RecordPlayback(string videoPath)
    {
        _libraryService?.RecordPlayback(videoPath);
    }
    #endregion

    #region Pagination Methods (for RTTFilePagination)
    /// <summary>
    /// Set page size (items per page).
    /// </summary>
    public void SetPageSize(int size)
    {
        PageSize = Mathf.Max(1, size);
        RecalculatePagination();
        Debug.Log($"[RTTMediaLibraryController] PageSize set to {PageSize}");
    }

    /// <summary>
    /// Change page by delta (-1 for prev, +1 for next).
    /// </summary>
    public void ChangePage(int delta)
    {
        GoToPage(CurrentPage + delta);
    }

    /// <summary>
    /// Go to specific page number.
    /// </summary>
    public void GoToPage(int pageNumber)
    {
        int newPage = Mathf.Clamp(pageNumber, 1, TotalPages);
        if (newPage != CurrentPage)
        {
            CurrentPage = newPage;
            // Forward to grid to actually scroll
            _view?.Grid?.GoToPage(newPage);
            Debug.Log($"[RTTMediaLibraryController] Page changed to {CurrentPage}/{TotalPages}");
        }
    }

    /// <summary>
    /// Scroll to first page.
    /// </summary>
    public void ScrollToStart()
    {
        GoToPage(1);
    }

    /// <summary>
    /// Scroll to last page.
    /// </summary>
    public void ScrollToEnd()
    {
        GoToPage(TotalPages);
    }

    /// <summary>
    /// Set sort field.
    /// </summary>
    public void SetSortBy(string sortBy)
    {
        if (SortBy != sortBy)
        {
            SortBy = sortBy;
            ApplySort();
            CurrentPage = 1; // Reset to first page
            RefreshView();
            Debug.Log($"[RTTMediaLibraryController] Sort by: {SortBy}");
        }
    }

    /// <summary>
    /// Set ascending/descending order.
    /// </summary>
    public void SetAscending(bool ascending)
    {
        if (IsAscending != ascending)
        {
            IsAscending = ascending;
            ApplySort();
            CurrentPage = 1;
            RefreshView();
            Debug.Log($"[RTTMediaLibraryController] Ascending: {IsAscending}");
        }
    }

    private void RecalculatePagination()
    {
        TotalPages = Mathf.Max(1, Mathf.CeilToInt((float)_filteredVideos.Count / PageSize));
        CurrentPage = Mathf.Clamp(CurrentPage, 1, TotalPages);
    }

    private void ApplySort()
    {
        switch (SortBy.ToLower())
        {
            case "name":
                _filteredVideos = IsAscending
                    ? _filteredVideos.OrderBy(v => v.Title).ToList()
                    : _filteredVideos.OrderByDescending(v => v.Title).ToList();
                break;
            case "type":
                // Sort by file extension/format
                _filteredVideos = IsAscending
                    ? _filteredVideos.OrderBy(v => System.IO.Path.GetExtension(v.Path)).ToList()
                    : _filteredVideos.OrderByDescending(v => System.IO.Path.GetExtension(v.Path)).ToList();
                break;
            case "created":
                _filteredVideos = IsAscending
                    ? _filteredVideos.OrderBy(v => v.DateAdded).ToList()
                    : _filteredVideos.OrderByDescending(v => v.DateAdded).ToList();
                break;
            case "modified":
                _filteredVideos = IsAscending
                    ? _filteredVideos.OrderBy(v => v.DateModified).ToList()
                    : _filteredVideos.OrderByDescending(v => v.DateModified).ToList();
                break;
            case "duration":
                _filteredVideos = IsAscending
                    ? _filteredVideos.OrderBy(v => v.Duration).ToList()
                    : _filteredVideos.OrderByDescending(v => v.Duration).ToList();
                break;
            case "size":
                _filteredVideos = IsAscending
                    ? _filteredVideos.OrderBy(v => v.FileSizeBytes).ToList()
                    : _filteredVideos.OrderByDescending(v => v.FileSizeBytes).ToList();
                break;
        }
    }

    private void RefreshView()
    {
        _view?.SetVideos(_filteredVideos);
        _view?.UpdatePagination();
    }
    #endregion

    #region Private Methods
    private void ApplyFilters()
    {
        List<MediaVideoInfo> result;

        // Start with category-based data
        switch (CurrentCategory)
        {
            case "all":
                result = new List<MediaVideoInfo>(_allVideos);
                break;

            case "videos":
                // Filter to video files only
                result = _allVideos.FindAll(v => IsVideoFile(v.Path));
                break;

            case "images":
                // Filter to image files only (if supported)
                result = _allVideos.FindAll(v => IsImageFile(v.Path));
                break;

            case "audio":
                // Filter to audio files only (if supported)
                result = _allVideos.FindAll(v => IsAudioFile(v.Path));
                break;

            case "recent":
                result = _libraryService?.GetRecentVideos(50) ?? new List<MediaVideoInfo>();
                break;

            case "favorites":
                result = _libraryService?.GetFavorites() ?? new List<MediaVideoInfo>();
                break;

            case "playlists":
                // Show all videos for now (playlist sub-items not implemented yet)
                result = new List<MediaVideoInfo>(_allVideos);
                break;

            default:
                // Check if it's a playlist ID
                if (CurrentCategory.StartsWith("playlist_"))
                {
                    // TODO: Get videos from specific playlist
                    result = new List<MediaVideoInfo>(_allVideos);
                }
                else
                {
                    result = new List<MediaVideoInfo>(_allVideos);
                }
                break;
        }

        // Apply search filter locally
        if (!string.IsNullOrEmpty(CurrentSearchQuery))
        {
            var query = CurrentSearchQuery.ToLowerInvariant();
            result = result.Where(v =>
                v.Title.ToLowerInvariant().Contains(query) ||
                v.Path.ToLowerInvariant().Contains(query)
            ).ToList();
        }

        // Apply additional filters locally
        if (CurrentProjectionFilter.HasValue)
        {
            result = result.Where(v => v.Projection == CurrentProjectionFilter.Value).ToList();
        }

        if (CurrentFormatFilter.HasValue)
        {
            result = result.Where(v => v.Format == CurrentFormatFilter.Value).ToList();
        }

        if (CurrentDurationFilter.HasValue && CurrentDurationFilter.Value != DurationRange.All)
        {
            result = result.Where(v => v.DurationCategory == CurrentDurationFilter.Value).ToList();
        }

        _filteredVideos = result;

        // Apply sort
        ApplySort();

        // Recalculate pagination
        RecalculatePagination();

        // Update view
        _view?.SetVideos(_filteredVideos);

        Debug.Log($"[RTTMediaLibraryController] Showing {_filteredVideos.Count} videos (Category: {CurrentCategory}, Search: '{CurrentSearchQuery}', Page: {CurrentPage}/{TotalPages})");
    }

    /// <summary>
    /// Check if file is a video file based on extension.
    /// </summary>
    private bool IsVideoFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" ||
               ext == ".wmv" || ext == ".webm" || ext == ".m4v" || ext == ".flv";
    }

    /// <summary>
    /// Check if file is an image file based on extension.
    /// </summary>
    private bool IsImageFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" ||
               ext == ".bmp" || ext == ".webp" || ext == ".tiff" || ext == ".tif";
    }

    /// <summary>
    /// Check if file is an audio file based on extension.
    /// </summary>
    private bool IsAudioFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".aac" ||
               ext == ".ogg" || ext == ".m4a" || ext == ".wma";
    }

    private void UpdateVideoFavoriteState(string path, bool isFavorite)
    {
        for (int i = 0; i < _allVideos.Count; i++)
        {
            if (_allVideos[i].Path == path)
            {
                var video = _allVideos[i];
                video.IsFavorite = isFavorite;
                _allVideos[i] = video;
                break;
            }
        }
    }
    #endregion

    #region Event Handlers
    private void HandleVideoPlayRequested(MediaVideoInfo video)
    {
        Debug.Log($"[RTTMediaLibraryController] Play requested: {video.Title}");
        RecordPlayback(video.Path);
        OnVideoPlayRequested?.Invoke(video);
    }

    private void HandleCloseRequested()
    {
        OnCloseRequested?.Invoke();
    }

    private void HandleCategorySelected(string categoryId)
    {
        Debug.Log($"[RTTMediaLibraryController] Category selected: {categoryId}");
        CurrentCategory = categoryId;

        // Trigger scan if no videos loaded yet
        if (_allVideos.Count == 0 && _libraryService != null && !_libraryService.IsScanning)
        {
            Debug.Log("[RTTMediaLibraryController] No videos loaded, triggering scan from category selection...");
            _libraryService.ScanMediaLibrary(OnScanComplete);
            return;
        }

        ApplyFilters();
    }

    private void HandleProjectionFilterChanged(VideoProjectionType? projection)
    {
        SetProjectionFilter(projection);
    }

    private void HandleFormatFilterChanged(VideoFormat? format)
    {
        SetFormatFilter(format);
    }

    private void HandleDurationFilterChanged(DurationRange? duration)
    {
        SetDurationFilter(duration);
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        if (_view != null)
        {
            _view.OnVideoPlayRequested -= HandleVideoPlayRequested;
            _view.OnCloseRequested -= HandleCloseRequested;
        }

        if (_libraryService != null)
        {
            _libraryService.OnScanProgress -= HandleScanProgress;
            _libraryService.OnScanComplete -= HandleScanComplete;
        }

        // Note: Side panel events are now managed by RTTMediaLibrary
    }
    #endregion
}
