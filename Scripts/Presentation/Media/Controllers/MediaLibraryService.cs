using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using VRWorkspace.Domain.Media;
using VRWorkspace.Infrastructure.Media;
using VRWorkspace.Infrastructure.Media.Scanning;
using VRWorkspace.Media.Data;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// Coordinator / facade for the media library subsystem.
    /// Delegates scanning, caching, favorites and history to dedicated services.
    /// Implements IMediaLibraryService for DI compatibility (Phase 5 will remove singleton).
    /// </summary>
    public class MediaLibraryService : MonoBehaviour, IMediaLibraryService
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
        /// <summary>Set false to skip cache loading and scan on demand instead.</summary>
        private const bool ENABLE_CACHE_LOADING = false;
        #endregion

        #region IMediaLibraryService Events
        /// <inheritdoc/>
        public event Action OnLibraryUpdated;

        // Explicit implementation of interface's float-based progress event.
        // Concrete callers should use OnScanProgress (int, int) below.
        private event Action<float> _onScanProgressFloat;
        event Action<float> IMediaLibraryService.OnScanProgress
        {
            add    { _onScanProgressFloat += value; }
            remove { _onScanProgressFloat -= value; }
        }
        #endregion

        #region Legacy / Additional Events (kept for backward-compatibility)
        /// <summary>Fired with (current, total) item counts during scanning (legacy API).</summary>
        public event Action<int, int> OnScanProgress;

        /// <summary>Fired when a full scan completes. Arg: the complete video list.</summary>
        public event Action<List<MediaVideoInfo>> OnScanComplete;

#pragma warning disable CS0067 // Reserved for future error-reporting
        public event Action<string> OnScanError;
#pragma warning restore CS0067

        /// <summary>Fired when the library cache has been loaded asynchronously.</summary>
        public event Action OnLibraryCacheLoaded;

        /// <summary>Fired when background metadata refresh completes.</summary>
        public event Action OnMetadataRefreshComplete;

        /// <summary>Fired when new items are detected during a background scan.</summary>
        public event Action<List<MediaVideoInfo>> OnNewItemsDetected;

        /// <summary>Fired when deleted items are removed during a background scan.</summary>
        public event Action<List<string>> OnItemsRemoved;

        // Removed: OnScanProgressDetailed - merged into OnScanProgress (int, int) above.
        #endregion

        #region IMediaLibraryService Properties
        /// <inheritdoc/>
        public List<MediaVideoInfo> AllVideos { get; private set; } = new List<MediaVideoInfo>();

        /// <inheritdoc/>
        public List<MediaAudioInfo> AllAudio { get; private set; } = new List<MediaAudioInfo>();

        /// <inheritdoc/>
        public List<MediaImageInfo> AllImages { get; private set; } = new List<MediaImageInfo>();

        /// <inheritdoc/>
        public bool IsScanning => _scanner?.IsScanning ?? false;

        /// <inheritdoc/>
        public bool IsLibraryLoaded => IsCacheLoaded;
        #endregion

        #region Additional Properties
        public bool IsCacheLoaded  => _cacheService?.IsCacheLoaded  ?? false;
        public bool IsCacheLoading => _cacheService?.IsCacheLoading ?? false;
        public int  TotalVideoCount => AllVideos.Count;
        #endregion

        #region Delegated Services
        private MediaLibraryScanner  _scanner;
        private MediaCacheService    _cacheService;
        private MediaFavoritesService _favoritesService;
        private MediaHistoryService  _historyService;
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

            // Instantiate services
            _favoritesService = new MediaFavoritesService();
            _historyService   = new MediaHistoryService();
            _scanner          = new MediaLibraryScanner(this, () => _favoritesService.GetFavoritePaths());
            _cacheService     = new MediaCacheService(this, _scanner);

            // Wire scanner events
            _scanner.OnScanCompleted      += OnScannerCompleted;
            _scanner.OnScanProgress       += OnScannerProgress;
            _scanner.OnNewItemsDetected   += OnScannerNewItems;
            _scanner.OnItemsRemoved       += OnScannerItemsRemoved;

            // Wire cache events
            _cacheService.OnCacheLoaded           += OnCacheServiceLoaded;
            _cacheService.OnMetadataRefreshComplete += () => OnMetadataRefreshComplete?.Invoke();

#pragma warning disable CS0162 // ENABLE_CACHE_LOADING is a compile-time constant
            if (ENABLE_CACHE_LOADING)
            {
                _cacheService.LoadAsync(AllVideos, _favoritesService.GetFavoritePaths());
            }
            else
            {
                _cacheService.MarkLoadedImmediately();
                Debug.Log("[MediaLibraryService] Cache loading disabled - will scan on demand");
            }
#pragma warning restore CS0162
        }
        #endregion

        #region Scanning (IMediaLibraryService + Legacy API)
        /// <inheritdoc/>
        public void StartScan() => ScanMediaLibrary();

        /// <summary>Scan the full media storage and populate AllVideos.</summary>
        public void ScanMediaLibrary(Action<List<MediaVideoInfo>> onComplete = null)
        {
            _scanner.StartScan(result =>
            {
                AllVideos = result.Videos;
                _cacheService.Save(AllVideos);
                PreloadMetadata();
                onComplete?.Invoke(AllVideos);
            });
        }

        /// <inheritdoc/>
        public void StopScan() => _scanner.StopScan();

        /// <summary>
        /// Perform a background incremental scan without interrupting the current display.
        /// </summary>
        public void StartBackgroundScan() => _scanner.StartBackgroundScan(AllVideos);
        #endregion

        #region Query Methods (IMediaLibraryService + Legacy)
        /// <summary>Return a snapshot of all videos.</summary>
        public List<MediaVideoInfo> GetAllVideos() => AllVideos.ToList();

        /// <summary>Return recently played videos (up to <paramref name="count"/>).</summary>
        public List<MediaVideoInfo> GetRecentVideos(int count = 50)
            => _historyService.GetHistoryVideos(AllVideos, count);

        /// <inheritdoc/>
        public List<MediaVideoInfo> GetFavorites()
            => _favoritesService.GetFavoriteVideos(AllVideos);

        /// <summary>Filter by projection type.</summary>
        public List<MediaVideoInfo> FilterByProjection(VideoProjectionType type)
            => AllVideos.Where(v => v.Projection == type).ToList();

        /// <summary>Filter by video format.</summary>
        public List<MediaVideoInfo> FilterByFormat(VideoFormat format)
            => AllVideos.Where(v => v.Format == format).ToList();

        /// <summary>Filter by duration range.</summary>
        public List<MediaVideoInfo> FilterByDuration(DurationRange range)
            => AllVideos.Where(v => v.DurationCategory == range).ToList();

        /// <inheritdoc/>
        public List<MediaVideoInfo> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return AllVideos.ToList();
            var q = query.ToLowerInvariant();
            return AllVideos.Where(v =>
                v.Title.ToLowerInvariant().Contains(q) ||
                v.Path.ToLowerInvariant().Contains(q)
            ).ToList();
        }

        /// <summary>Get a single video by path. Returns null if not found.</summary>
        public MediaVideoInfo? GetVideoByPath(string path)
        {
            var video = AllVideos.FirstOrDefault(v => v.Path == path);
            return video.Path != null ? video : (MediaVideoInfo?)null;
        }

        /// <summary>Apply multiple filters in a single pass.</summary>
        public List<MediaVideoInfo> ApplyFilters(
            VideoProjectionType? projection  = null,
            VideoFormat?         format      = null,
            DurationRange?       duration    = null,
            string               searchQuery = null)
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
                var q = searchQuery.ToLowerInvariant();
                result = result.Where(v => v.Title.ToLowerInvariant().Contains(q));
            }

            return result.ToList();
        }
        #endregion

        #region Favorites (IMediaLibraryService + Legacy)
        /// <inheritdoc/>
        public void ToggleFavorite(string path)
        {
            bool newState = !_favoritesService.IsFavorite(path);
            _favoritesService.SetFavorite(path, newState, AllVideos);
            OnLibraryUpdated?.Invoke();
        }

        /// <inheritdoc/>
        public bool IsFavorite(string path) => _favoritesService.IsFavorite(path);

        /// <summary>Set favorite status explicitly.</summary>
        public void SetFavorite(string path, bool isFavorite)
        {
            _favoritesService.SetFavorite(path, isFavorite, AllVideos);
            OnLibraryUpdated?.Invoke();
        }

        /// <summary>Add to favorites.</summary>
        public void AddToFavorites(string path) => SetFavorite(path, true);

        /// <summary>Remove from favorites.</summary>
        public void RemoveFromFavorites(string path) => SetFavorite(path, false);
        #endregion

        #region Playback History
        /// <summary>Record a video play and move it to the top of history.</summary>
        public void RecordPlayback(string path)
        {
            _historyService.RecordPlay(path, AllVideos);
        }

        /// <summary>Return playback history as resolved MediaVideoInfo list.</summary>
        public List<MediaVideoInfo> GetPlaybackHistory(int count = 50)
            => GetRecentVideos(count);
        #endregion

        #region Cache Management
        /// <summary>Clear the library cache and empty AllVideos.</summary>
        public void ClearLibraryCache() => _cacheService.Clear(AllVideos);

        /// <summary>
        /// Validate and refresh file metadata for a single item.
        /// Returns false when the file no longer exists.
        /// </summary>
        public bool ValidateAndUpdateFileInfo(ref MediaVideoInfo info)
            => _cacheService.ValidateAndUpdateFileInfo(ref info);

        /// <summary>
        /// Notify the service that items were removed externally (e.g. by the user via UI).
        /// Updates the in-memory library and persists the updated cache.
        /// </summary>
        public void NotifyItemsRemoved(List<string> removedPaths)
        {
            if (removedPaths == null || removedPaths.Count == 0) return;
            var removedSet  = new HashSet<string>(removedPaths);
            int removedCount = AllVideos.RemoveAll(v => removedSet.Contains(v.Path));
            if (removedCount > 0)
            {
                _cacheService.Save(AllVideos);
                Debug.Log($"[MediaLibraryService] NotifyItemsRemoved: removed {removedCount} items from cache");
            }
        }
        #endregion

        #region Sorting
        /// <summary>Sort a list of videos by a named property.</summary>
        public List<MediaVideoInfo> Sort(List<MediaVideoInfo> videos, string sortBy, bool ascending = true)
        {
            IOrderedEnumerable<MediaVideoInfo> sorted;
            switch (sortBy.ToLowerInvariant())
            {
                case "name":
                case "title":
                    sorted = ascending ? videos.OrderBy(v => v.Title)         : videos.OrderByDescending(v => v.Title);
                    break;
                case "date":
                case "modified":
                    sorted = ascending ? videos.OrderBy(v => v.DateModified)  : videos.OrderByDescending(v => v.DateModified);
                    break;
                case "size":
                    sorted = ascending ? videos.OrderBy(v => v.FileSizeBytes) : videos.OrderByDescending(v => v.FileSizeBytes);
                    break;
                case "duration":
                    sorted = ascending ? videos.OrderBy(v => v.Duration)      : videos.OrderByDescending(v => v.Duration);
                    break;
                case "type":
                case "projection":
                    sorted = ascending ? videos.OrderBy(v => v.Projection)    : videos.OrderByDescending(v => v.Projection);
                    break;
                default:
                    sorted = videos.OrderBy(v => v.Title);
                    break;
            }
            return sorted.ToList();
        }
        #endregion

        #region Private: Scanner Callbacks
        private void OnScannerCompleted(ScanResult result)
        {
            // AllVideos was already set inside ScanMediaLibrary's callback -
            // but scanner also fires this event, so we guard against double-assignment.
            if (!ReferenceEquals(AllVideos, result.Videos))
                AllVideos = result.Videos;

            int count = AllVideos.Count;
            OnScanProgress?.Invoke(count, count);       // legacy (int, int)
            _onScanProgressFloat?.Invoke(1f);           // IMediaLibraryService
            OnScanComplete?.Invoke(AllVideos);
            OnLibraryUpdated?.Invoke();
        }

        private void OnScannerProgress(float fraction)
        {
            _onScanProgressFloat?.Invoke(fraction);     // IMediaLibraryService
            int approx = Mathf.RoundToInt(fraction * Mathf.Max(AllVideos.Count, 1));
            OnScanProgress?.Invoke(approx, AllVideos.Count); // legacy (int, int)
        }

        private void OnScannerNewItems(List<MediaVideoInfo> newItems)
        {
            _cacheService.Save(AllVideos);
            PreloadMetadata();
            OnNewItemsDetected?.Invoke(newItems);
            OnLibraryUpdated?.Invoke();
        }

        private void OnScannerItemsRemoved(List<string> removedPaths)
        {
            _cacheService.Save(AllVideos);
            OnItemsRemoved?.Invoke(removedPaths);
            OnLibraryUpdated?.Invoke();
        }
        #endregion

        #region Private: Cache Callbacks
        private void OnCacheServiceLoaded()
        {
            // Apply current favorites to cached items
            _favoritesService.ApplyFavoritesToList(AllVideos);
            OnLibraryCacheLoaded?.Invoke();
            OnLibraryUpdated?.Invoke();

            if (AllVideos.Count > 0)
                PreloadMetadata();
        }
        #endregion

        #region Private: Metadata Preload
        private void PreloadMetadata()
        {
            var allPaths = AllVideos.Select(v => v.Path).ToList();
            if (allPaths.Count > 0)
            {
                Debug.Log($"[MediaLibraryService] Triggering metadata preload for {allPaths.Count} files");
                FileMetadataService.Instance.PreloadMetadata(allPaths);
            }
        }
        #endregion
    }
}
