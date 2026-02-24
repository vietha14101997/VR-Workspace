using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRWorkspace.Infrastructure.Media.Scanning;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Infrastructure.Media
{
    /// <summary>
    /// Handles persistence and retrieval of the scanned media library cache.
    /// Uses PlayerPrefs (JSON) for storage. Plain C# class; coroutines driven by host MonoBehaviour.
    /// </summary>
    public class MediaCacheService
    {
        #region Constants
        private const string LIBRARY_CACHE_KEY = "MediaLibrary_Cache";
        private const int BATCH_SIZE_LOAD  = 100;
        private const int BATCH_SIZE_REFRESH = 50;
        #endregion

        #region Events
        /// <summary>Fired when cache loading is complete (success or empty).</summary>
        public event Action OnCacheLoaded;

        /// <summary>Fired when background metadata refresh is complete.</summary>
        public event Action OnMetadataRefreshComplete;
        #endregion

        #region Properties
        public bool IsCacheLoaded  { get; private set; }
        public bool IsCacheLoading { get; private set; }
        #endregion

        #region Private Fields
        private readonly MonoBehaviour _host;
        private readonly MediaLibraryScanner _scanner;
        private Coroutine _cacheLoadCoroutine;
        #endregion

        public MediaCacheService(MonoBehaviour host, MediaLibraryScanner scanner)
        {
            _host    = host    ?? throw new ArgumentNullException(nameof(host));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        }

        #region Public API
        /// <summary>
        /// Begin async cache loading. Results are populated into <paramref name="allVideos"/>.
        /// Fires <see cref="OnCacheLoaded"/> when done.
        /// </summary>
        public void LoadAsync(List<MediaVideoInfo> allVideos, HashSet<string> favorites)
        {
            if (_cacheLoadCoroutine != null)
                _host.StopCoroutine(_cacheLoadCoroutine);

            _cacheLoadCoroutine = _host.StartCoroutine(LoadLibraryCacheAsync(allVideos, favorites));
        }

        /// <summary>
        /// Mark the cache as immediately loaded (used when cache loading is disabled).
        /// </summary>
        public void MarkLoadedImmediately()
        {
            IsCacheLoading = false;
            IsCacheLoaded  = true;
        }

        /// <summary>
        /// Save the current library to the cache.
        /// </summary>
        public void Save(List<MediaVideoInfo> allVideos)
        {
            try
            {
                var cache = new LibraryCacheWrapper();
                foreach (var video in allVideos)
                {
                    cache.paths.Add(video.Path);
                    cache.fileSizes.Add(video.FileSizeBytes);
                    cache.dateAddedTicks.Add(video.DateAdded.Ticks);
                    cache.dateModifiedTicks.Add(video.DateModified.Ticks);
                    cache.widths.Add(video.Width);
                    cache.heights.Add(video.Height);
                }
                string json = JsonUtility.ToJson(cache);
                PlayerPrefs.SetString(LIBRARY_CACHE_KEY, json);
                PlayerPrefs.Save();
                Debug.Log($"[MediaCacheService] Saved {cache.paths.Count} items to cache (with metadata & dimensions)");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MediaCacheService] Failed to save library cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear the persisted cache and empty the library list.
        /// </summary>
        public void Clear(List<MediaVideoInfo> allVideos)
        {
            PlayerPrefs.DeleteKey(LIBRARY_CACHE_KEY);
            PlayerPrefs.Save();
            allVideos?.Clear();
            Debug.Log("[MediaCacheService] Library cache cleared");
        }

        /// <summary>
        /// Validate file metadata for a single item. Returns false if file no longer exists.
        /// </summary>
        public bool ValidateAndUpdateFileInfo(ref MediaVideoInfo info)
        {
            if (string.IsNullOrEmpty(info.Path)) return false;
            try
            {
                if (!File.Exists(info.Path)) return false;
                if (info.DateModified == DateTime.MinValue || info.FileSizeBytes == 0)
                {
                    var fileInfo = new FileInfo(info.Path);
                    info.FileSizeBytes = fileInfo.Length;
                    info.DateModified  = fileInfo.LastWriteTime;
                    info.DateAdded     = fileInfo.CreationTime;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Cache Loading Coroutine
        private IEnumerator LoadLibraryCacheAsync(List<MediaVideoInfo> allVideos, HashSet<string> favorites)
        {
            IsCacheLoading = true;
            IsCacheLoaded  = false;

            string json = PlayerPrefs.GetString(LIBRARY_CACHE_KEY, "");
            if (string.IsNullOrEmpty(json))
            {
                Debug.Log("[MediaCacheService] No library cache found");
                FinishLoading();
                yield break;
            }

            LibraryCacheWrapper cache = null;
            try
            {
                cache = JsonUtility.FromJson<LibraryCacheWrapper>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MediaCacheService] Failed to parse library cache: {ex.Message}");
                FinishLoading();
                yield break;
            }

            if (cache?.paths == null || cache.paths.Count == 0)
            {
                Debug.Log("[MediaCacheService] Library cache is empty");
                FinishLoading();
                yield break;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"[MediaCacheService] Loading {cache.paths.Count} items from cache (async)...");

            allVideos.Clear();

            bool hasMetadata   = cache.fileSizes     != null && cache.fileSizes.Count     == cache.paths.Count;
            bool hasDimensions = cache.widths        != null && cache.widths.Count         == cache.paths.Count;
            int  processedCount = 0;

            for (int i = 0; i < cache.paths.Count; i++)
            {
                string path             = cache.paths[i];
                long   fileSize         = hasMetadata   ? cache.fileSizes[i]      : 0;
                long   dateAddedTicks   = hasMetadata   ? cache.dateAddedTicks[i] : 0;
                long   dateModifiedTicks= hasMetadata   ? cache.dateModifiedTicks[i] : 0;
                int    width            = hasDimensions ? cache.widths[i]  : 0;
                int    height           = hasDimensions ? cache.heights[i] : 0;

                var info = _scanner.CreateVideoInfoQuick(path, fileSize, dateAddedTicks, dateModifiedTicks, width, height, favorites);
                allVideos.Add(info);
                processedCount++;

                if (processedCount % BATCH_SIZE_LOAD == 0)
                    yield return null;
            }

            allVideos.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

            sw.Stop();
            Debug.Log($"[MediaCacheService] Loaded {allVideos.Count} items from cache in {sw.ElapsedMilliseconds}ms");

            FinishLoading();

            // Background refresh if metadata was missing from an older cache
            if (!hasMetadata && allVideos.Count > 0)
            {
                Debug.Log("[MediaCacheService] Cache missing metadata, starting background refresh...");
                _host.StartCoroutine(RefreshMetadataInBackground(allVideos));
            }
        }

        private void FinishLoading()
        {
            IsCacheLoading = false;
            IsCacheLoaded  = true;
            OnCacheLoaded?.Invoke();
        }
        #endregion

        #region Background Metadata Refresh
        private IEnumerator RefreshMetadataInBackground(List<MediaVideoInfo> allVideos)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int processedCount = 0;
            int updatedCount   = 0;

            for (int i = 0; i < allVideos.Count; i++)
            {
                var video = allVideos[i];
                if (video.DateModified == DateTime.MinValue || video.FileSizeBytes == 0)
                {
                    try
                    {
                        if (File.Exists(video.Path))
                        {
                            var fileInfo = new FileInfo(video.Path);
                            video.FileSizeBytes = fileInfo.Length;
                            video.DateModified  = fileInfo.LastWriteTime;
                            video.DateAdded     = fileInfo.CreationTime;
                            allVideos[i]        = video;
                            updatedCount++;
                        }
                    }
                    catch { }
                }

                processedCount++;
                if (processedCount % BATCH_SIZE_REFRESH == 0)
                    yield return null;
            }

            sw.Stop();
            Debug.Log($"[MediaCacheService] Background metadata refresh: {updatedCount}/{allVideos.Count} items in {sw.ElapsedMilliseconds}ms");

            Save(allVideos);
            OnMetadataRefreshComplete?.Invoke();
        }
        #endregion

        #region Serialization Classes
        [Serializable]
        private class LibraryCacheWrapper
        {
            public List<string> paths            = new List<string>();
            public List<long>   fileSizes        = new List<long>();
            public List<long>   dateAddedTicks   = new List<long>();
            public List<long>   dateModifiedTicks= new List<long>();
            public List<int>    widths           = new List<int>();
            public List<int>    heights          = new List<int>();
        }
        #endregion
    }
}
