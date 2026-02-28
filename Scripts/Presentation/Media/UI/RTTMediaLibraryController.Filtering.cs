using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibraryController partial: Filtering, searching, library refresh, and view finalization.
    /// </summary>
    public partial class RTTMediaLibraryController
    {
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

            // Get current data
            _allVideos = _libraryService.GetAllVideos();

            // If empty and not scanning, always trigger scan
            if (_allVideos.Count == 0 && !_libraryService.IsScanning)
            {
                Debug.Log("[RTTMediaLibraryController] No data available, starting scan...");
                // Don't pass OnScanComplete as callback - we already subscribe to the OnScanComplete EVENT
                // via HandleScanComplete. Passing both causes double ApplyFilters() on scan completion.
                _libraryService.ScanMediaLibrary();
            }
            else if (_libraryService.IsScanning)
            {
                Debug.Log("[RTTMediaLibraryController] Scan already in progress, waiting...");
                // UI will be updated via OnScanComplete event
            }
            else
            {
                Debug.Log($"[RTTMediaLibraryController] Using existing data: {_allVideos.Count} items");
                ApplyFilters();
            }
        }

        /// <summary>
        /// Smart refresh - performs incremental scan to detect new/removed files.
        /// Unlike ForceRescan, this keeps existing thumbnails and only cleans up removed files.
        /// </summary>
        public void SmartRefresh()
        {
            Debug.Log("[RTTMediaLibraryController] SmartRefresh called - performing incremental scan...");

            if (_libraryService == null)
            {
                Debug.LogError("[RTTMediaLibraryController] LibraryService not available!");
                return;
            }

            // Use incremental background scan - detects new and removed files
            // Removed files trigger HandleItemsRemoved which cleans up their thumbnails
            _libraryService.StartBackgroundScan();
        }

        /// <summary>
        /// Force a full rescan of the media library to detect new/removed files.
        /// Clears the library cache first then triggers a fresh scan.
        /// Note: Prefer SmartRefresh() for normal refresh operations.
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

            // Start fresh scan (HandleScanComplete event will update UI when done)
            _libraryService.ScanMediaLibrary();
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
    }
}
