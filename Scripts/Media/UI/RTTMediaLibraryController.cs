using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VRWorkspace.UI.RTT;

/// <summary>
/// Controller for Media Library - manages data flow between MediaLibraryService and UI.
/// Handles filtering, searching, category selection, and user actions.
/// Implements IDataBindable for smooth transition with background data loading.
/// </summary>
public class RTTMediaLibraryController : MonoBehaviour, IPaginationController, IDataBindable
{
    #region Events
    public event Action<MediaVideoInfo> OnVideoPlayRequested;
    public event Action OnCloseRequested;
    public event Action OnDataPrepared; // IDataBindable event
    #endregion

    #region State Caching Constants
    private const string APP_ID = "media";
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
    public string GroupBy { get; private set; } = "Date Added";
    public List<MediaGroupInfo> Groups => _groups;
    #endregion

    #region Private Fields
    private RTTMediaLibrary _view;
    private MediaLibraryService _libraryService;
    private List<MediaVideoInfo> _allVideos = new List<MediaVideoInfo>();
    private List<MediaVideoInfo> _filteredVideos = new List<MediaVideoInfo>();
    private List<MediaGroupInfo> _groups = new List<MediaGroupInfo>();  // Grouped items for display
    private bool _isInitialized = false;
    private bool _isFirstLoad = true; // Use sync filtering for first load to ensure immediate display
    private bool _waitingForInitialBinding = false; // Track if we're waiting for grid binding before starting scan
    private bool _waitingForCacheLoad = false; // Track if we're waiting for async cache load
    private Coroutine _filterCoroutine;
    private const int FILTER_BATCH_SIZE = 200; // Items to process per frame

    // Category page position cache - remembers page for each category
    private Dictionary<string, int> _categoryPageCache = new Dictionary<string, int>();

    // Category selected item cache - remembers selected item path for each category
    private Dictionary<string, string> _categorySelectedItemCache = new Dictionary<string, string>();

    // Pending page to navigate after data loads (for category switching)
    private int? _pendingPageNavigation = null;

    // Selection/Hover State (like FileManager)
    private MediaVideoInfo? _selectedVideo = null;
    private MediaVideoInfo? _hoveredVideo = null;

    // Vietnamese culture for proper diacritics sorting (Đ with D, etc.) - synced with RTTFileManagerController
    private static readonly CultureInfo VietnameseCulture = new CultureInfo("vi-VN");
    private static readonly StringComparer VietnameseComparer = StringComparer.Create(VietnameseCulture, ignoreCase: true);

    // Loading spinner for IDataBindable
    private RTTLoadingSpinner _loadingSpinner;
    private bool _isPreparingData = false;
    private bool _isDataReady = false;
    private List<MediaVideoInfo> _preparedDataBuffer = null;
    #endregion

    #region IDataBindable Implementation
    /// <summary>
    /// Whether the data is ready to be bound to UI.
    /// </summary>
    public bool IsDataReady => _isDataReady || (_libraryService != null && _libraryService.IsCacheLoaded);

    /// <summary>
    /// Whether data is currently being prepared in background.
    /// </summary>
    public bool IsPreparingData => _isPreparingData || (_libraryService != null && _libraryService.IsCacheLoading);

    /// <summary>
    /// Start preparing data in background without blocking UI.
    /// For Media app, cache is already loading from MediaLibraryService.Awake().
    /// </summary>
    public void PrepareDataAsync()
    {
        // MediaLibraryService already starts loading in Awake()
        // Just mark that we're preparing
        _isPreparingData = true;
        _isDataReady = false;

        // Start coroutine to wait for cache and fire event
        StartCoroutine(PrepareDataCoroutine());
    }

    private IEnumerator PrepareDataCoroutine()
    {
        Debug.Log("[RTTMediaLibraryController] PrepareDataAsync started");

        // Wait for library service cache to load
        while (_libraryService != null && _libraryService.IsCacheLoading)
        {
            yield return null;
        }

        // Get data from service
        if (_libraryService != null && _libraryService.IsCacheLoaded)
        {
            _preparedDataBuffer = new List<MediaVideoInfo>(_libraryService.AllVideos);
        }
        else
        {
            _preparedDataBuffer = new List<MediaVideoInfo>();
        }

        _isPreparingData = false;
        _isDataReady = true;

        Debug.Log($"[RTTMediaLibraryController] PrepareDataAsync completed: {_preparedDataBuffer?.Count ?? 0} videos");

        // Fire event to notify RTTAppManager
        OnDataPrepared?.Invoke();
    }

    /// <summary>
    /// Bind data to UI safely with frame budget to prevent lag.
    /// Called BEFORE fade-in animation starts, so grid is populated when visible.
    /// Shows loading spinner if data not ready yet.
    /// </summary>
    public IEnumerator BindDataSafely()
    {
        // Check if data was already bound by OnViewReady
        // (which happens during frame preparation)
        bool dataAlreadyBound = _view?.Grid != null &&
                                _filteredVideos != null &&
                                _filteredVideos.Count > 0;

        if (dataAlreadyBound)
        {
            // Data already displayed from OnViewReady, nothing to do
            _isPreparingData = false;
            yield break;
        }

        // Show loading spinner
        ShowLoadingSpinner();

        // Wait for cache to load (with timeout)
        float timeout = 5f;
        float elapsed = 0f;
        while (!IsDataReady && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Hide spinner
        HideLoadingSpinner();

        // If data is ready now, trigger refresh
        if (IsDataReady && (_filteredVideos == null || _filteredVideos.Count == 0))
        {
            RefreshLibraryInternal();
        }

        _isPreparingData = false;
    }

    /// <summary>
    /// Show loading spinner in the content area.
    /// </summary>
    public void ShowLoadingSpinner()
    {
        if (_loadingSpinner != null)
        {
            _loadingSpinner.Show();
            return;
        }

        // Create spinner if not exists
        if (_view != null)
        {
            // Find content container to parent spinner
            Transform spinnerParent = _view.Grid?.transform?.parent ?? _view.transform;
            _loadingSpinner = RTTLoadingSpinner.Create(spinnerParent, Color.white);
            _loadingSpinner.Show();
        }
    }

    /// <summary>
    /// Hide loading spinner.
    /// </summary>
    public void HideLoadingSpinner()
    {
        _loadingSpinner?.Hide();
    }

    /// <summary>
    /// Called when app is fully visible after transition.
    /// Side panels now fade in with main frame via coordinated animation.
    /// </summary>
    public void OnAppShown()
    {
        Debug.Log($"[RTTMediaLibraryController] OnAppShown called, _view={((_view != null) ? "exists" : "null")}");
        // Side panels now fade in with main frame via coordinated animation in RTTAppManager
        // _view?.ShowSidePanels(); // No longer needed
    }

    /// <summary>
    /// Get all frames (main + side panels) for coordinated fade animation.
    /// </summary>
    public List<RTTMenuFrame> GetAllFrames()
    {
        return _view?.GetAllFrames() ?? new List<RTTMenuFrame>();
    }

    /// <summary>
    /// Bind cached data immediately (non-blocking).
    /// If cache not loaded yet, shows empty grid.
    /// Background process will update when data is ready via OnBackgroundDataReady.
    /// </summary>
    public void BindCachedDataOrEmpty()
    {
        if (_libraryService == null || _view == null)
        {
            Debug.Log("[RTTMediaLibraryController] BindCachedDataOrEmpty: service or view is null");
            return;
        }

        if (_libraryService.IsCacheLoaded && _libraryService.AllVideos.Count > 0)
        {
            // Cache is ready - bind immediately
            _allVideos = new List<MediaVideoInfo>(_libraryService.AllVideos);
            ApplyFiltersSync(); // Synchronous version
            RefreshView();
            Debug.Log($"[RTTMediaLibraryController] BindCachedDataOrEmpty: Bound {_filteredVideos.Count} items from cache");
        }
        else
        {
            // No cache yet - show empty grid (background will update later)
            _allVideos.Clear();
            _filteredVideos.Clear();
            _groups.Clear();
            _view?.SetVideos(_filteredVideos, _groups);
            Debug.Log("[RTTMediaLibraryController] BindCachedDataOrEmpty: No cache, showing empty grid");
        }

        _isPreparingData = false;
    }

    /// <summary>
    /// Called when background data loading completes.
    /// Updates UI if app is visible.
    /// </summary>
    public void OnBackgroundDataReady()
    {
        if (_view == null || !_view.gameObject.activeInHierarchy)
        {
            Debug.Log("[RTTMediaLibraryController] OnBackgroundDataReady: view not active, skipping");
            return;
        }

        // Reload from service and refresh
        if (_libraryService != null && _libraryService.IsCacheLoaded)
        {
            _allVideos = new List<MediaVideoInfo>(_libraryService.AllVideos);
            Debug.Log($"[RTTMediaLibraryController] OnBackgroundDataReady: Updating UI with {_allVideos.Count} items");
            StartCoroutine(ApplyFiltersAsync());
        }
    }

    #region State Caching Support

    /// <summary>
    /// MediaLibrary supports state caching for fast app switching.
    /// </summary>
    public bool SupportsStateCaching => true;

    /// <summary>
    /// Try to restore UI state from cache.
    /// Returns true if valid cache exists and was restored.
    /// </summary>
    public bool TryRestoreCachedState()
    {
        if (!AppStateCache.Instance.TryGetState(APP_ID, out var snapshot))
        {
            Debug.Log("[RTTMediaLibraryController] TryRestoreCachedState: No cache found");
            return false;
        }

        // Restore state properties
        CurrentCategory = !string.IsNullOrEmpty(snapshot.category) ? snapshot.category : "videos";
        CurrentPage = snapshot.currentPage > 0 ? snapshot.currentPage : 1;
        PageSize = snapshot.pageSize > 0 ? snapshot.pageSize : 6;
        SortBy = !string.IsNullOrEmpty(snapshot.sortBy) ? snapshot.sortBy : "Name";
        IsAscending = snapshot.sortAscending;
        GroupBy = !string.IsNullOrEmpty(snapshot.groupBy) ? snapshot.groupBy : "Date Added";

        // Restore cached items
        if (snapshot.items != null && snapshot.items.Count > 0)
        {
            _filteredVideos = new List<MediaVideoInfo>();

            // Get category filter function to ensure cached items match current category
            Func<MediaVideoInfo, bool> categoryFilter = GetCategoryFilter();

            foreach (var cachedItem in snapshot.items)
            {
                var video = new MediaVideoInfo
                {
                    Title = cachedItem.name,
                    Path = cachedItem.path,
                    FileSizeBytes = cachedItem.fileSize
                };

                // Restore DateAdded from ticks (prevents Jan 1 1970 display)
                if (cachedItem.dateAddedTicks > 0)
                {
                    video.DateAdded = new DateTime(cachedItem.dateAddedTicks);
                }
                else
                {
                    // Fallback: use file modification time or current time
                    video.DateAdded = DateTime.Now;
                }

                // Try to parse duration if available
                if (!string.IsNullOrEmpty(cachedItem.duration) && TimeSpan.TryParse(cachedItem.duration, out var duration))
                {
                    video.Duration = duration;
                }

                // Only add items that match the current category filter
                if (categoryFilter(video))
                {
                    _filteredVideos.Add(video);
                }
            }

            // Generate groups from filtered items
            _groups = MediaGroupHelper.CreateGroups(_filteredVideos, GroupBy);

            // Update view with filtered cached data
            if (_view != null)
            {
                _view.SetVideos(_filteredVideos, _groups);
                RecalculatePagination();
                _view.UpdatePagination();

                // Update sidebar to match restored category
                _view.SelectCategory(CurrentCategory);
            }

            Debug.Log($"[RTTMediaLibraryController] TryRestoreCachedState: Restored {_filteredVideos.Count} items from cache (filtered from {snapshot.items.Count})");
            return true;
        }

        Debug.Log("[RTTMediaLibraryController] TryRestoreCachedState: Cache has no items");
        return false;
    }

    /// <summary>
    /// Save current UI state to cache.
    /// </summary>
    public void CacheCurrentState()
    {
        if (_filteredVideos == null || _filteredVideos.Count == 0)
        {
            Debug.Log("[RTTMediaLibraryController] CacheCurrentState: No videos to cache");
            return;
        }

        var snapshot = new AppStateSnapshot
        {
            appId = APP_ID,
            category = CurrentCategory,
            currentPage = CurrentPage,
            pageSize = PageSize,
            sortBy = SortBy,
            sortAscending = IsAscending,
            groupBy = GroupBy,
            selectedItemPath = _selectedVideo?.Path,
            totalItemCount = _filteredVideos.Count,
            items = new List<CachedItemRef>()
        };

        // Cache visible items (current page + surrounding pages for smooth scrolling)
        int startIndex = Mathf.Max(0, (CurrentPage - 2) * PageSize);
        int endIndex = Mathf.Min(_filteredVideos.Count, (CurrentPage + 2) * PageSize);

        for (int i = startIndex; i < endIndex; i++)
        {
            var video = _filteredVideos[i];
            var cachedRef = new CachedItemRef
            {
                path = video.Path,
                name = video.Title,
                isDirectory = false,
                category = "video",
                fileSize = video.FileSizeBytes,
                duration = video.Duration.ToString(),
                dateAddedTicks = video.DateAdded.Ticks
            };

            // Store thumbnail cache key if available
            cachedRef.thumbnailCacheKey = ThumbnailCacheKeyHelper.GetCacheKey(video.Path);

            snapshot.items.Add(cachedRef);
        }

        AppStateCache.Instance.SaveState(APP_ID, snapshot);
        Debug.Log($"[RTTMediaLibraryController] CacheCurrentState: Cached {snapshot.items.Count} items");
    }

    /// <summary>
    /// Get the data buffer prepared by background thread.
    /// </summary>
    public object GetPreparedDataBuffer()
    {
        return _preparedDataBuffer;
    }

    /// <summary>
    /// Bind prepared data buffer directly to UI.
    /// Applies current category filter to show correct data.
    /// </summary>
    public void BindPreparedData(object dataBuffer)
    {
        if (dataBuffer is List<MediaVideoInfo> videos)
        {
            _allVideos = videos;

            // CRITICAL: Apply category filter to _filteredVideos
            // Without this, all media items are shown regardless of category selection
            Func<MediaVideoInfo, bool> categoryFilter = GetCategoryFilter();
            _filteredVideos = videos.FindAll(v => categoryFilter(v));

            ApplySort();
            ApplyGrouping();
            CurrentPage = 1;

            if (_view != null)
            {
                _view.SetVideos(_filteredVideos, _groups);
                RecalculatePagination();
                _view.UpdatePagination();
                RestoreOrSelectFirstItem();
            }

            Debug.Log($"[RTTMediaLibraryController] BindPreparedData: Bound {_filteredVideos.Count} videos (filtered from {videos.Count} by category '{CurrentCategory}')");
        }
    }

    #endregion
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
            _libraryService.OnNewItemsDetected += HandleNewItemsDetected;
            _libraryService.OnItemsRemoved += HandleItemsRemoved;
            _libraryService.OnLibraryCacheLoaded += HandleCacheLoaded;
        }

        // Note: Side panel events are now wired in OnViewReady() because panels are created asynchronously

        _isInitialized = true;
        // Debug.Log($"[RTTMediaLibraryController] Initialized, libraryService={((_libraryService != null) ? "OK" : "NULL")}");

        // Note: Don't load data yet - wait for OnViewReady() when all panels are created
    }

    private void HandleScanProgress(int current, int total)
    {
        // Update UI during scan
        _view?.UpdateItemCount(current);
    }

    /// <summary>
    /// Handle async cache load completion.
    /// </summary>
    private void HandleCacheLoaded()
    {
        Debug.Log("[RTTMediaLibraryController] HandleCacheLoaded called");

        // If waiting for initial load, do the first refresh
        if (_waitingForCacheLoad)
        {
            _waitingForCacheLoad = false;
            RefreshLibraryInternal();
            return;
        }

        // If app is visible (already opened), update with new data
        if (_view != null && _view.gameObject.activeInHierarchy)
        {
            OnBackgroundDataReady();
        }
    }

    private void HandleScanComplete(List<MediaVideoInfo> videos)
    {
        // Debug.Log($"[RTTMediaLibraryController] HandleScanComplete: {videos.Count} files");
        _allVideos = videos;
        ApplyFilters();
    }

    /// <summary>
    /// Handle new items detected during background scan.
    /// Updates local data and refreshes display without interrupting user.
    /// </summary>
    private void HandleNewItemsDetected(List<MediaVideoInfo> newItems)
    {
        if (newItems == null || newItems.Count == 0) return;

        Debug.Log($"[RTTMediaLibraryController] New items detected: {newItems.Count}");

        // Add new items to local list
        _allVideos.AddRange(newItems);

        // Re-apply filters to include new items (will update display)
        ApplyFilters();
    }

    /// <summary>
    /// Handle items removed (files no longer exist) during background scan.
    /// Removes from local data and refreshes display.
    /// </summary>
    private void HandleItemsRemoved(List<string> removedPaths)
    {
        if (removedPaths == null || removedPaths.Count == 0) return;

        Debug.Log($"[RTTMediaLibraryController] Items removed: {removedPaths.Count}");

        // Convert to HashSet for O(1) lookup
        var removedSet = new HashSet<string>(removedPaths);

        // Remove from local lists
        _allVideos.RemoveAll(v => removedSet.Contains(v.Path));
        _filteredVideos.RemoveAll(v => removedSet.Contains(v.Path));

        // Clear selection if the selected video was removed
        if (_selectedVideo.HasValue && removedSet.Contains(_selectedVideo.Value.Path))
        {
            _selectedVideo = null;
            _view?.ClearDetailPanel();
        }

        // Clear hover if the hovered video was removed
        if (_hoveredVideo.HasValue && removedSet.Contains(_hoveredVideo.Value.Path))
        {
            _hoveredVideo = null;
        }

        // Re-apply filters and refresh display
        ApplyFilters();

        // Invalidate app state cache since data changed
        AppStateCache.Instance.InvalidateState(APP_ID);
    }

    /// <summary>
    /// Called by RTTMediaLibrary when all panels (including async side panels) are ready.
    /// </summary>
    public void OnViewReady()
    {
        // Debug.Log("[RTTMediaLibraryController] View ready, loading data...");

        // IMPORTANT: Reset _isFirstLoad to ensure sync filtering for immediate display
        // This is needed because side panel may have already triggered category selection
        // before OnViewReady, consuming the flag and causing async filtering to clear the view
        _isFirstLoad = true;

        // Check if cache is still loading (async optimization)
        if (_libraryService != null && _libraryService.IsCacheLoading)
        {
            // Wait for cache to finish loading
            _waitingForCacheLoad = true;
            // Debug.Log("[RTTMediaLibraryController] Waiting for cache to load...");
            return;
        }

        RefreshLibraryInternal();
    }

    /// <summary>
    /// Internal method to load library after cache is ready.
    /// </summary>
    private void RefreshLibraryInternal()
    {
        // Check if we have cached data before subscribing to binding event
        int cachedCount = _libraryService?.GetAllVideos()?.Count ?? 0;

        if (cachedCount > 0 && _view?.Grid != null)
        {
            // Subscribe to grid binding complete event - start scan AFTER all cached items are displayed
            _waitingForInitialBinding = true;
            _view.Grid.OnBindingComplete += OnInitialBindingComplete;
            // Debug.Log($"[RTTMediaLibraryController] Waiting for {cachedCount} cached items to bind before scanning...");
        }
        else
        {
            // No cached items - start scan immediately after loading
            _waitingForInitialBinding = false;
            // Debug.Log("[RTTMediaLibraryController] No cached items, will scan immediately after load...");
        }

        // Load initial data from cache (will trigger binding if has data, then scan)
        RefreshLibrary();

        // If no cached items, start background scan now (no binding to wait for)
        if (!_waitingForInitialBinding)
        {
            // Debug.Log("[RTTMediaLibraryController] Starting background scan (no binding to wait for)...");
            _libraryService?.StartBackgroundScan();
        }
    }

    /// <summary>
    /// Called when grid finishes binding all items from cache.
    /// Now safe to start background scan without interfering with initial display.
    /// </summary>
    private void OnInitialBindingComplete()
    {
        if (!_waitingForInitialBinding) return;
        _waitingForInitialBinding = false;

        // Unsubscribe to avoid multiple triggers
        if (_view?.Grid != null)
        {
            _view.Grid.OnBindingComplete -= OnInitialBindingComplete;
        }

        // Debug.Log("[RTTMediaLibraryController] Initial binding complete, starting background scan...");

        // Now safe to start background scan
        _libraryService?.StartBackgroundScan();
    }

    /// <summary>
    /// Select a category from the side panel.
    /// Called by RTTMediaLibrary when side panel fires OnCategorySelected.
    /// </summary>
    public void SelectCategory(string categoryId)
    {
        HandleCategorySelected(categoryId);

        // Trigger background scan when switching categories to detect new files
        _libraryService?.StartBackgroundScan();
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Refresh library data from storage.
    /// </summary>
    public void RefreshLibrary()
    {
        // Debug.Log($"[RTTMediaLibraryController] RefreshLibrary called, _libraryService={((_libraryService != null) ? "OK" : "NULL")}");

        if (_libraryService == null)
        {
            Debug.LogError("[RTTMediaLibraryController] LibraryService not available!");
            return;
        }

        // Check if media has been scanned
        _allVideos = _libraryService.GetAllVideos();
        // Debug.Log($"[RTTMediaLibraryController] GetAllVideos returned {_allVideos.Count} items, IsScanning={_libraryService.IsScanning}");

        if (_allVideos.Count == 0 && !_libraryService.IsScanning)
        {
            // No videos and not scanning - trigger scan
            // Debug.Log("[RTTMediaLibraryController] Starting media scan...");
            _libraryService.ScanMediaLibrary(OnScanComplete);
        }
        else if (_libraryService.IsScanning)
        {
            // Debug.Log("[RTTMediaLibraryController] Scan already in progress, waiting...");
            // UI will be updated via OnScanComplete event
        }
        else
        {
            // Debug.Log($"[RTTMediaLibraryController] Using cached data: {_allVideos.Count} items");
            ApplyFilters();
        }
    }

    /// <summary>
    /// Force a full rescan of the media library to detect new/removed files.
    /// Clears the library cache first then triggers a fresh scan.
    /// </summary>
    public void ForceRescan()
    {
        // Debug.Log("[RTTMediaLibraryController] ForceRescan called - clearing cache and rescanning...");

        if (_libraryService == null)
        {
            Debug.LogError("[RTTMediaLibraryController] LibraryService not available!");
            return;
        }

        if (_libraryService.IsScanning)
        {
            // Debug.Log("[RTTMediaLibraryController] Scan already in progress, please wait...");
            return;
        }

        // Save current page to restore after rescan
        _pendingPageNavigation = CurrentPage;

        // Clear the library cache to force fresh scan
        _libraryService.ClearLibraryCache();

        // Clear local data
        _allVideos.Clear();
        _filteredVideos.Clear();

        // Update UI to show scanning state
        _view?.UpdateItemCount(0);

        // Start fresh scan
        _libraryService.ScanMediaLibrary(OnScanComplete);
    }

    /// <summary>
    /// Called when media scan completes.
    /// </summary>
    private void OnScanComplete(List<MediaVideoInfo> videos)
    {
        _allVideos = videos;
        // Debug.Log($"[RTTMediaLibraryController] Scan complete: {_allVideos.Count} videos");
        ApplyFilters();

        // Cache state after scan completes
        if (SupportsStateCaching)
        {
            CacheCurrentState();
        }
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

        // Favourite button state is handled by RTTMediaLibrary's action buttons
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

    /// <summary>
    /// Select a video item and update the detail panel.
    /// </summary>
    /// <param name="video">Video to select</param>
    /// <param name="forceRefresh">Force refresh detail panel even if same path (used after data reload)</param>
    public void SelectVideo(MediaVideoInfo video, bool forceRefresh = false)
    {
        _selectedVideo = video;

        // Save selected item for current category
        if (!string.IsNullOrEmpty(CurrentCategory))
        {
            _categorySelectedItemCache[CurrentCategory] = video.Path;
        }

        UpdateDetailView(forceRefresh);
    }

    /// <summary>
    /// Clear the selected video state.
    /// Used when entering edit mode to ensure detail panel shows empty when not hovering.
    /// </summary>
    public void ClearSelectedVideo()
    {
        _selectedVideo = null;
    }

    /// <summary>
    /// Handle hover enter on a video item.
    /// </summary>
    public void HoverVideo(MediaVideoInfo video)
    {
        _hoveredVideo = video;
        UpdateDetailView();
    }

    /// <summary>
    /// Handle hover exit from a video item.
    /// </summary>
    public void UnhoverVideo(MediaVideoInfo video)
    {
        if (_hoveredVideo.HasValue && _hoveredVideo.Value.Path == video.Path)
        {
            _hoveredVideo = null;
            UpdateDetailView();
        }
    }

    /// <summary>
    /// Update the detail panel based on hover/selection state.
    /// Shows hovered item if hovering, otherwise shows selected item.
    /// </summary>
    /// <param name="forceRefresh">Force refresh even if same path (used after data reload)</param>
    private void UpdateDetailView(bool forceRefresh = false)
    {
        if (_view == null) return;

        if (_hoveredVideo.HasValue)
        {
            _view.UpdateDetailPanel(_hoveredVideo.Value, forceRefresh);
        }
        else if (_selectedVideo.HasValue)
        {
            _view.UpdateDetailPanel(_selectedVideo.Value, forceRefresh);
        }
        else
        {
            _view.ClearDetailPanel();
        }
    }

    /// <summary>
    /// Auto-select the first item in the filtered list.
    /// </summary>
    /// <param name="forceRefresh">Force refresh detail panel (used after data reload)</param>
    private void AutoSelectFirstItem(bool forceRefresh = true)
    {
        if (_filteredVideos.Count > 0 && _view?.Grid != null)
        {
            var firstVideo = _filteredVideos[0];
            _view.Grid.SelectVideo(firstVideo);
            SelectVideo(firstVideo, forceRefresh);
            // Notify view so it can show action bar
            _view.NotifyVideoAutoSelected(firstVideo);
        }
    }

    /// <summary>
    /// Restore cached selected item for current category, or select first item.
    /// Called after data reload, so always forces detail panel refresh.
    /// </summary>
    private void RestoreOrSelectFirstItem()
    {
        if (_filteredVideos.Count == 0) return;

        // Try to restore cached selection
        if (_categorySelectedItemCache.TryGetValue(CurrentCategory, out string cachedPath))
        {
            var video = _filteredVideos.Find(v => v.Path == cachedPath);
            if (!string.IsNullOrEmpty(video.Path))
            {
                _view?.Grid?.SelectVideo(video);
                // Force refresh because data may have changed (new metadata, etc.)
                SelectVideo(video, forceRefresh: true);
                // Notify view so it can show action bar
                _view?.NotifyVideoAutoSelected(video);
                return;
            }
        }

        // No cached selection or item no longer exists, select first
        AutoSelectFirstItem(forceRefresh: true);
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
        // Debug.Log($"[RTTMediaLibraryController] PageSize set to {PageSize}");
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
            // Debug.Log($"[RTTMediaLibraryController] Page changed to {CurrentPage}/{TotalPages}");
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
            CurrentPage = 1;
            RefreshView();
            // Debug.Log($"[RTTMediaLibraryController] Sort by: {SortBy}");
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
            // Debug.Log($"[RTTMediaLibraryController] Ascending: {IsAscending}");
        }
    }

    /// <summary>
    /// Set group by field.
    /// </summary>
    public void SetGroupBy(string groupBy)
    {
        if (GroupBy != groupBy)
        {
            GroupBy = groupBy;
            ApplyGrouping();
            CurrentPage = 1;
            RefreshView();
            // Debug.Log($"[RTTMediaLibraryController] Group by: {GroupBy}");
        }
    }

    private void RecalculatePagination()
    {
        // Get TotalPages from grid which now uses group-aware pagination
        if (_view?.Grid != null)
        {
            TotalPages = _view.Grid.TotalPages;
        }
        else
        {
            // Fallback to simple calculation if grid not available
            TotalPages = Mathf.Max(1, Mathf.CeilToInt((float)_filteredVideos.Count / PageSize));
        }
        CurrentPage = Mathf.Clamp(CurrentPage, 1, TotalPages);
    }

    private void ApplySort()
    {
        // Use in-place sort to avoid creating new lists (better GC performance)
        int direction = IsAscending ? 1 : -1;

        switch (SortBy.ToLower())
        {
            case "name":
                // Use Vietnamese comparer for proper diacritics sorting (synced with RTTFileManagerController)
                _filteredVideos.Sort((a, b) => direction * VietnameseComparer.Compare(a.Title, b.Title));
                break;
            case "type":
                // Use Vietnamese comparer for type names as well
                _filteredVideos.Sort((a, b) => direction * VietnameseComparer.Compare(
                    System.IO.Path.GetExtension(a.Path),
                    System.IO.Path.GetExtension(b.Path)));
                break;
            case "created":
                _filteredVideos.Sort((a, b) => direction * a.DateAdded.CompareTo(b.DateAdded));
                break;
            case "modified":
                _filteredVideos.Sort((a, b) => direction * a.DateModified.CompareTo(b.DateModified));
                break;
            case "duration":
                _filteredVideos.Sort((a, b) => direction * a.Duration.CompareTo(b.Duration));
                break;
            case "size":
                _filteredVideos.Sort((a, b) => direction * a.FileSizeBytes.CompareTo(b.FileSizeBytes));
                break;
        }
    }

    /// <summary>
    /// Apply grouping to cluster items by selected field.
    /// This re-sorts items to group them visually.
    /// </summary>
    private void ApplyGrouping()
    {
        if (string.IsNullOrEmpty(GroupBy))
        {
            GroupBy = "Date Added";  // Default
        }

        // Group by sorting to cluster items visually
        switch (GroupBy.ToLower().Replace(" ", ""))
        {
            case "dateadded":
                // Group by date, most recent first
                _filteredVideos.Sort((a, b) =>
                {
                    int dateCompare = b.DateAdded.Date.CompareTo(a.DateAdded.Date);
                    if (dateCompare != 0) return dateCompare;
                    return VietnameseComparer.Compare(a.Title, b.Title);
                });
                break;

            case "duration":
                // Group by duration category (Short <5m, Medium 5-20m, Long 20-60m, Extended >60m)
                _filteredVideos.Sort((a, b) =>
                {
                    int catA = GetDurationCategory(a.Duration);
                    int catB = GetDurationCategory(b.Duration);
                    int catCompare = catA.CompareTo(catB);
                    if (catCompare != 0) return catCompare;
                    return a.Duration.CompareTo(b.Duration);
                });
                break;

            case "resolution":
                // Group by resolution (4K, 1440p, 1080p, 720p, SD)
                _filteredVideos.Sort((a, b) =>
                {
                    int resA = GetResolutionCategory(a.Height);
                    int resB = GetResolutionCategory(b.Height);
                    int resCompare = resB.CompareTo(resA);  // Higher resolution first
                    if (resCompare != 0) return resCompare;
                    return VietnameseComparer.Compare(a.Title, b.Title);
                });
                break;

            case "format":
                // Group by video format/extension
                _filteredVideos.Sort((a, b) =>
                {
                    string extA = System.IO.Path.GetExtension(a.Path)?.ToLower() ?? "";
                    string extB = System.IO.Path.GetExtension(b.Path)?.ToLower() ?? "";
                    int extCompare = extA.CompareTo(extB);
                    if (extCompare != 0) return extCompare;
                    return VietnameseComparer.Compare(a.Title, b.Title);
                });
                break;
        }

        // Generate group info for display
        _groups = MediaGroupHelper.CreateGroups(_filteredVideos, GroupBy);
        // Debug.Log($"[RTTMediaLibraryController] Created {_groups.Count} groups for {_filteredVideos.Count} items");
    }

    private int GetDurationCategory(TimeSpan duration)
    {
        double minutes = duration.TotalMinutes;
        if (minutes < 5) return 0;       // Short
        if (minutes < 20) return 1;      // Medium
        if (minutes < 60) return 2;      // Long
        return 3;                         // Extended
    }

    private int GetResolutionCategory(int height)
    {
        if (height >= 2160) return 4;    // 4K
        if (height >= 1440) return 3;    // 1440p
        if (height >= 1080) return 2;    // 1080p
        if (height >= 720) return 1;     // 720p
        return 0;                         // SD
    }

    private void RefreshView()
    {
        _view?.SetVideos(_filteredVideos, _groups);
        // Update pagination after SetVideos (grid computes TotalPages)
        RecalculatePagination();
        _view?.UpdatePagination();
    }
    #endregion

    #region Private Methods
    private void ApplyFilters()
    {
        // Cancel any existing filter operation
        if (_filterCoroutine != null)
        {
            StopCoroutine(_filterCoroutine);
            _filterCoroutine = null;
        }

        // IMPORTANT: Use sync filtering for first load to ensure data displays immediately
        // Async filtering clears the view first, which causes "0 items" on startup
        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            ApplyFiltersSync();
            return;
        }

        // For small datasets or special categories, filter synchronously for instant response
        bool useAsync = _allVideos.Count > FILTER_BATCH_SIZE &&
                       (CurrentCategory == "all" || CurrentCategory == "videos" ||
                        CurrentCategory == "images" || CurrentCategory == "audio");

        if (useAsync)
        {
            _filterCoroutine = StartCoroutine(ApplyFiltersAsync());
        }
        else
        {
            ApplyFiltersSync();
        }
    }

    /// <summary>
    /// Synchronous filter for small datasets or special categories (recent, favorites, etc.)
    /// </summary>
    private void ApplyFiltersSync()
    {
        List<MediaVideoInfo> result = GetFilteredByCategory();

        // Apply additional filters
        result = ApplyAdditionalFilters(result);

        _filteredVideos = result;

        // Apply sort
        ApplySort();

        // Finalize and update view
        FinalizeAndUpdateView();
    }

    /// <summary>
    /// Async filter for large datasets - spreads work across multiple frames
    /// </summary>
    private IEnumerator ApplyFiltersAsync()
    {
        // NOTE: Do NOT clear the view here - keep showing old data until new data is ready
        // Clearing causes "0 items" flash which is a poor user experience
        // The view will be updated atomically in FinalizeAndUpdateView()

        List<MediaVideoInfo> result = new List<MediaVideoInfo>();
        Func<MediaVideoInfo, bool> categoryFilter = GetCategoryFilter();
        int processed = 0;

        // Process in batches to avoid frame drops
        for (int i = 0; i < _allVideos.Count; i++)
        {
            if (categoryFilter(_allVideos[i]))
            {
                result.Add(_allVideos[i]);
            }

            processed++;

            // Yield every FILTER_BATCH_SIZE items to keep UI responsive
            if (processed >= FILTER_BATCH_SIZE)
            {
                processed = 0;
                yield return null; // Wait one frame
            }
        }

        // Apply additional filters (these are usually fast since result is already filtered)
        result = ApplyAdditionalFilters(result);

        _filteredVideos = result;

        // Apply sort
        ApplySort();

        // Finalize and update view
        FinalizeAndUpdateView();

        _filterCoroutine = null;
    }

    /// <summary>
    /// Get filter function based on current category
    /// </summary>
    private Func<MediaVideoInfo, bool> GetCategoryFilter()
    {
        switch (CurrentCategory)
        {
            case "all":
                return v => true;
            case "videos":
                return v => IsVideoFile(v.Path);
            case "images":
                return v => IsImageFile(v.Path);
            case "audio":
                return v => IsAudioFile(v.Path);
            default:
                return v => true;
        }
    }

    /// <summary>
    /// Get filtered list by category (for sync path or special categories)
    /// </summary>
    private List<MediaVideoInfo> GetFilteredByCategory()
    {
        switch (CurrentCategory)
        {
            case "all":
                return new List<MediaVideoInfo>(_allVideos);

            case "videos":
                return _allVideos.FindAll(v => IsVideoFile(v.Path));

            case "images":
                return _allVideos.FindAll(v => IsImageFile(v.Path));

            case "audio":
                return _allVideos.FindAll(v => IsAudioFile(v.Path));

            case "recent":
                return _libraryService?.GetRecentVideos(50) ?? new List<MediaVideoInfo>();

            case "favorites":
                return _libraryService?.GetFavorites() ?? new List<MediaVideoInfo>();

            case "playlists":
                return new List<MediaVideoInfo>(_allVideos);

            default:
                if (CurrentCategory.StartsWith("playlist_"))
                {
                    // TODO: Get videos from specific playlist
                    return new List<MediaVideoInfo>(_allVideos);
                }
                return new List<MediaVideoInfo>(_allVideos);
        }
    }

    /// <summary>
    /// Apply search and additional filters
    /// </summary>
    private List<MediaVideoInfo> ApplyAdditionalFilters(List<MediaVideoInfo> result)
    {
        // Apply search filter
        if (!string.IsNullOrEmpty(CurrentSearchQuery))
        {
            var query = CurrentSearchQuery.ToLowerInvariant();
            result = result.FindAll(v =>
                v.Title.ToLowerInvariant().Contains(query) ||
                v.Path.ToLowerInvariant().Contains(query));
        }

        // Apply projection filter
        if (CurrentProjectionFilter.HasValue)
        {
            result = result.FindAll(v => v.Projection == CurrentProjectionFilter.Value);
        }

        // Apply format filter
        if (CurrentFormatFilter.HasValue)
        {
            result = result.FindAll(v => v.Format == CurrentFormatFilter.Value);
        }

        // Apply duration filter
        if (CurrentDurationFilter.HasValue && CurrentDurationFilter.Value != DurationRange.All)
        {
            result = result.FindAll(v => v.DurationCategory == CurrentDurationFilter.Value);
        }

        return result;
    }

    /// <summary>
    /// Finalize filtering and update the view
    /// </summary>
    private void FinalizeAndUpdateView()
    {
        // Apply grouping to generate group headers
        ApplyGrouping();

        // Update view with groups (grid calculates page layout internally)
        _view?.SetVideos(_filteredVideos, _groups);

        // Recalculate pagination AFTER SetVideos (grid now has computed TotalPages)
        RecalculatePagination();

        // Navigate to pending page if set (from category switch)
        if (_pendingPageNavigation.HasValue)
        {
            int targetPage = Mathf.Clamp(_pendingPageNavigation.Value, 1, TotalPages);
            _pendingPageNavigation = null;

            // Navigate instantly after data is ready
            _view?.Grid?.GoToPageInstant(targetPage);
            // Debug.Log($"[RTTMediaLibraryController] Navigated to pending page {targetPage}");
        }

        // Restore cached selected item or select first item
        RestoreOrSelectFirstItem();

        // Debug.Log($"[RTTMediaLibraryController] Showing {_filteredVideos.Count} videos in {_groups.Count} groups (Category: {CurrentCategory}, Search: '{CurrentSearchQuery}', Page: {CurrentPage}/{TotalPages})");
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
        // Debug.Log($"[RTTMediaLibraryController] Play requested: {video.Title}");
        RecordPlayback(video.Path);
        OnVideoPlayRequested?.Invoke(video);
    }

    private void HandleCloseRequested()
    {
        OnCloseRequested?.Invoke();
    }

    private void HandleCategorySelected(string categoryId)
    {
        // Debug.Log($"[RTTMediaLibraryController] Category selected: {categoryId}");
        
        // Save current page for the OLD category before switching
        if (!string.IsNullOrEmpty(CurrentCategory))
        {
            _categoryPageCache[CurrentCategory] = CurrentPage;
        }
        
        CurrentCategory = categoryId;

        // Trigger scan if no videos loaded yet
        if (_allVideos.Count == 0 && _libraryService != null && !_libraryService.IsScanning)
        {
            // Debug.Log("[RTTMediaLibraryController] No videos loaded, triggering scan from category selection...");
            _libraryService.ScanMediaLibrary(OnScanComplete);
            return;
        }

        // Restore cached page for the NEW category, or default to page 1
        if (_categoryPageCache.TryGetValue(categoryId, out int cachedPage))
        {
            CurrentPage = cachedPage;
            _pendingPageNavigation = cachedPage; // Will navigate after data loads
            // Debug.Log($"[RTTMediaLibraryController] Will restore page {cachedPage} for category {categoryId} after data loads");
        }
        else
        {
            CurrentPage = 1;
            _pendingPageNavigation = 1;
        }

        ApplyFilters();
        // Note: Navigation happens in FinalizeAndUpdateView() after data is loaded
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
        // Cancel any pending filter operation
        if (_filterCoroutine != null)
        {
            StopCoroutine(_filterCoroutine);
            _filterCoroutine = null;
        }

        // Cleanup loading spinner
        if (_loadingSpinner != null)
        {
            Destroy(_loadingSpinner.gameObject);
            _loadingSpinner = null;
        }

        if (_view != null)
        {
            _view.OnVideoPlayRequested -= HandleVideoPlayRequested;
            _view.OnCloseRequested -= HandleCloseRequested;

            // Unsubscribe from grid binding event
            if (_view.Grid != null)
            {
                _view.Grid.OnBindingComplete -= OnInitialBindingComplete;
            }
        }

        if (_libraryService != null)
        {
            _libraryService.OnScanProgress -= HandleScanProgress;
            _libraryService.OnScanComplete -= HandleScanComplete;
            _libraryService.OnNewItemsDetected -= HandleNewItemsDetected;
            _libraryService.OnItemsRemoved -= HandleItemsRemoved;
            _libraryService.OnLibraryCacheLoaded -= HandleCacheLoaded;
        }

        // Note: Side panel events are now managed by RTTMediaLibrary
    }
    #endregion
}
