using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Media.Data;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibraryController partial: IDataBindable implementation, state caching, and background data binding.
    /// </summary>
    public partial class RTTMediaLibraryController
    {
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

            // Wait for library service cache to load (or immediate if disabled)
            while (_libraryService != null && _libraryService.IsCacheLoading)
            {
                yield return null;
            }

            // Get data from service
            if (_libraryService != null && _libraryService.IsCacheLoaded)
            {
                if (_libraryService.AllVideos.Count > 0)
                {
                    // Cache has data - use it
                    _preparedDataBuffer = new List<MediaVideoInfo>(_libraryService.AllVideos);
                    Debug.Log($"[RTTMediaLibraryController] Using cached data ({_preparedDataBuffer.Count} items)");
                }
                else
                {
                    // Cache empty - will scan on load
                    _preparedDataBuffer = new List<MediaVideoInfo>();
                    Debug.Log("[RTTMediaLibraryController] Cache empty, will scan on load");
                }
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

            // OPTIMIZATION: Trigger background scan when app becomes visible
            // This ensures the library stays up-to-date with new/deleted files
            if (_libraryService != null && !_libraryService.IsScanning)
            {
                Debug.Log("[RTTMediaLibraryController] OnAppShown: Starting background scan to detect changes");
                _libraryService.StartBackgroundScan();
            }
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

        #endregion

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
            PageSize = 8; // Force to current default, view will refine it later via SetPageSize
            SortBy = !string.IsNullOrEmpty(snapshot.sortBy) ? snapshot.sortBy : "Name";
            IsAscending = snapshot.sortAscending;
            GroupBy = !string.IsNullOrEmpty(snapshot.groupBy) ? snapshot.groupBy : "Date Added";

            // Restore cached items with validation
            if (snapshot.items != null && snapshot.items.Count > 0)
            {
                _filteredVideos = new List<MediaVideoInfo>();

                // Get category filter function to ensure cached items match current category
                Func<MediaVideoInfo, bool> categoryFilter = GetCategoryFilter();

                int skippedCount = 0;
                foreach (var cachedItem in snapshot.items)
                {
                    // Validate file still exists before adding
                    if (!System.IO.File.Exists(cachedItem.path))
                    {
                        skippedCount++;
                        continue; // Skip deleted files
                    }

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

                // Invalidate cache if items were skipped (deleted files detected)
                if (skippedCount > 0)
                {
                    Debug.Log($"[RTTMediaLibraryController] TryRestoreCachedState: Skipped {skippedCount} deleted files");
                    AppStateCache.Instance.InvalidateState(APP_ID);
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

                Debug.Log($"[RTTMediaLibraryController] TryRestoreCachedState: Restored {_filteredVideos.Count} items from cache (filtered from {snapshot.items.Count}, skipped {skippedCount} deleted)");
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
    }
}
