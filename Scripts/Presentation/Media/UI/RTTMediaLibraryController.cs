using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VRWorkspace.UI.RTT;
using VRWorkspace.Media.Core;
using VRWorkspace.Media.Data;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// Controller for Media Library - manages data flow between MediaLibraryService and UI.
    /// Handles filtering, searching, category selection, and user actions.
    /// Implements IDataBindable for smooth transition with background data loading.
    /// </summary>
    public partial class RTTMediaLibraryController : MonoBehaviour, IPaginationController, IDataBindable
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
        public int PageSize { get; private set; } = 8;
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

        private bool _fadeOutComplete = false;
        private Coroutine _categoryChangeCoroutine = null;

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
            // IMPORTANT: Create a copy to avoid sharing the same reference as MediaLibraryService.AllVideos.
            // Sharing the reference causes double-add bugs when background scan modifies AllVideos
            // and HandleNewItemsDetected also adds to _allVideos (which would be the same list).
            _allVideos = new List<MediaVideoInfo>(videos);

            // Update prepared data buffer so BindPreparedData uses fresh data
            // (prevents stale empty buffer from pre-init clearing scan results)
            if (videos.Count > 0)
            {
                _preparedDataBuffer = new List<MediaVideoInfo>(videos);
                _isDataReady = true;
            }

            ApplyFilters();

            // Cache state after scan completes
            if (SupportsStateCaching)
            {
                CacheCurrentState();
            }
        }

        /// <summary>
        /// Handle new items detected during background scan.
        /// Updates local data and refreshes display without interrupting user.
        /// </summary>
        private void HandleNewItemsDetected(List<MediaVideoInfo> newItems)
        {
            if (newItems == null || newItems.Count == 0) return;

            Debug.Log($"[RTTMediaLibraryController] New items detected: {newItems.Count}");

            // Add new items to local list (safe because _allVideos is always a separate copy)
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

            // Clean up thumbnail cache for removed files
            FileThumbnailService.Instance?.RemoveThumbnailsForPaths(removedPaths);

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

            // Only use cached binding if we have data
            if (cachedCount > 0 && _view?.Grid != null)
            {
                // Has cached data - bind first, then scan in background
                _waitingForInitialBinding = true;
                _view.Grid.OnBindingComplete += OnInitialBindingComplete;
                Debug.Log($"[RTTMediaLibraryController] Will bind {cachedCount} cached items");
            }
            else
            {
                // No cached data - scan immediately
                _waitingForInitialBinding = false;
                Debug.Log("[RTTMediaLibraryController] No cached data, will scan immediately");
            }

            // Load data
            RefreshLibrary();

            // If no cached items, start scan immediately
            // Note: RefreshLibrary() above already calls ScanMediaLibrary() when _allVideos is empty,
            // so this is a safety net. ScanMediaLibrary blocks duplicate calls via IsScanning check.
            if (cachedCount == 0)
            {
                Debug.Log("[RTTMediaLibraryController] Starting fresh scan (no cache)");
                _libraryService?.ScanMediaLibrary();
            }
            else if (!_waitingForInitialBinding)
            {
                // Have cache but not waiting - start background scan
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

        #region Event Handlers
        private void HandleVideoPlayRequested(MediaVideoInfo video)
        {
            // Debug.Log($"[RTTMediaLibraryController] Play requested: {video.Title}");
            RecordPlayback(video.Path);

            // Set playback queue from current filtered list
            var paths = _filteredVideos.Select(v => v.Path).ToList();
            int startIndex = paths.IndexOf(video.Path);
            if (startIndex < 0) startIndex = 0;
            MediaPlaylistService.Instance.SetPlaybackQueueDirect(paths, startIndex);

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

            // Check if this is an actual category change (not initial load)
            bool isCategoryChange = !string.IsNullOrEmpty(CurrentCategory) && CurrentCategory != categoryId;

            CurrentCategory = categoryId;

            // Trigger scan if no videos loaded yet
            if (_allVideos.Count == 0 && _libraryService != null && !_libraryService.IsScanning)
            {
                // Debug.Log("[RTTMediaLibraryController] No videos loaded, triggering scan from category selection...");
                _libraryService.ScanMediaLibrary();
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

            // Start category change with parallel fade and data loading
            if (isCategoryChange && _view != null)
            {
                // Cancel any existing category change coroutine
                if (_categoryChangeCoroutine != null)
                {
                    StopCoroutine(_categoryChangeCoroutine);
                }
                _categoryChangeCoroutine = StartCoroutine(CategoryChangeCoroutine());
            }
            else
            {
                // Initial load or same category - no fade needed
                ApplyFilters();
            }
            // Note: Navigation happens in FinalizeAndUpdateView() after data is loaded
        }

        /// <summary>
        /// Coroutine that handles category change with parallel fade and data loading.
        /// FadeIn starts immediately after FadeOut, data updates continuously in background.
        /// </summary>
        private IEnumerator CategoryChangeCoroutine()
        {
            // Reset flags
            _fadeOutComplete = false;

            // === PARALLEL: Start fade out AND data filtering simultaneously ===

            // Start fade out animation (non-blocking)
            _view.FadeOutContent(() => { _fadeOutComplete = true; });

            // Start filtering data in background (runs in parallel with fade)
            // Data will update view automatically when ready via FinalizeAndUpdateView
            ApplyFilters();

            // Wait for fade out only - don't wait for filtering
            while (!_fadeOutComplete)
            {
                yield return null;
            }

            // Fade out complete - start fade in immediately
            // Data may still be loading but will update view seamlessly
            _view?.FadeInContent();

            _categoryChangeCoroutine = null;
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Rename a media file.
        /// </summary>
        public void RenameItem(string sourcePath, string newName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(newName))
            {
                Debug.LogWarning("[RTTMediaLibraryController] Cannot rename: invalid parameters");
                return;
            }

            // Sanitize new name - remove invalid characters
            string sanitizedName = newName.Trim();
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                sanitizedName = sanitizedName.Replace(c.ToString(), "");
            }

            if (string.IsNullOrEmpty(sanitizedName))
            {
                Debug.LogWarning("[RTTMediaLibraryController] Cannot rename: invalid name after sanitization");
                return;
            }

            try
            {
                string directory = System.IO.Path.GetDirectoryName(sourcePath);

                // Preserve file extension if user didn't provide one
                string sourceExt = System.IO.Path.GetExtension(sourcePath);
                string newExt = System.IO.Path.GetExtension(sanitizedName);
                if (string.IsNullOrEmpty(newExt) && !string.IsNullOrEmpty(sourceExt))
                {
                    sanitizedName = sanitizedName + sourceExt;
                }

                string newPath = System.IO.Path.Combine(directory, sanitizedName);

                // Check if source and destination are the same
                if (sourcePath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[RTTMediaLibraryController] Rename skipped: same name");
                    return;
                }

                // Check if file exists
                if (!System.IO.File.Exists(sourcePath))
                {
                    Debug.LogWarning($"[RTTMediaLibraryController] Source does not exist: {sourcePath}");
                    return;
                }

                // Check if destination already exists
                if (System.IO.File.Exists(newPath))
                {
                    Debug.LogWarning($"[RTTMediaLibraryController] Cannot rename: destination already exists: {newPath}");
                    return;
                }

                // Perform rename
                System.IO.File.Move(sourcePath, newPath);
                Debug.Log($"[RTTMediaLibraryController] Renamed file: {sourcePath} -> {newPath}");

                // Update thumbnail cache key
                FileThumbnailService.Instance?.RenameThumbnailCache(sourcePath, newPath);

                // Update in local data
                for (int i = 0; i < _allVideos.Count; i++)
                {
                    if (_allVideos[i].Path == sourcePath)
                    {
                        var video = _allVideos[i];
                        video.Path = newPath;
                        video.Title = System.IO.Path.GetFileNameWithoutExtension(newPath);
                        _allVideos[i] = video;
                        break;
                    }
                }

                // Refresh display
                ApplyFilters();

                // Invalidate app state cache
                AppStateCache.Instance.InvalidateState(APP_ID);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RTTMediaLibraryController] Failed to rename: {e.Message}");
            }
        }

        /// <summary>
        /// Delete multiple media files.
        /// </summary>
        public void DeleteItems(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                Debug.LogWarning("[RTTMediaLibraryController] No items to delete");
                return;
            }

            int successCount = 0;
            int failCount = 0;
            var deletedPaths = new List<string>();

            foreach (string path in paths)
            {
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        // Delete file
                        System.IO.File.Delete(path);
                        Debug.Log($"[RTTMediaLibraryController] Deleted file: {path}");
                        deletedPaths.Add(path);
                        successCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[RTTMediaLibraryController] File not found: {path}");
                        failCount++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RTTMediaLibraryController] Failed to delete {path}: {e.Message}");
                    failCount++;
                }
            }

            Debug.Log($"[RTTMediaLibraryController] Delete completed: {successCount} succeeded, {failCount} failed");

            if (successCount > 0)
            {
                // Remove from local lists
                var deletedSet = new HashSet<string>(deletedPaths);
                _allVideos.RemoveAll(v => deletedSet.Contains(v.Path));
                _filteredVideos.RemoveAll(v => deletedSet.Contains(v.Path));

                // Clear selection if selected item was deleted
                if (_selectedVideo.HasValue && deletedSet.Contains(_selectedVideo.Value.Path))
                {
                    _selectedVideo = null;
                    _view?.ClearDetailPanel();
                }

                // Clean up thumbnail cache for deleted files
                FileThumbnailService.Instance?.RemoveThumbnailsForPaths(deletedPaths);

                // Notify library service of removed items (so cache is updated)
                _libraryService?.NotifyItemsRemoved(deletedPaths);

                // Refresh display
                ApplyFilters();

                // Invalidate app state cache
                AppStateCache.Instance.InvalidateState(APP_ID);
            }
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
}
