using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRWorkspace.Media.Core;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Utils;

namespace VRWorkspace.Infrastructure.Media.Scanning
{
    /// <summary>
    /// Handles all file-system and MediaStore scanning logic.
    /// Plain C# class - requires a MonoBehaviour host to drive coroutines.
    /// </summary>
    public class MediaLibraryScanner
    {
        #region Constants
        private static readonly string[] VIDEO_EXTENSIONS  = { ".mp4", ".mkv", ".avi", ".webm", ".mov", ".wmv", ".m4v", ".flv" };
        private static readonly string[] IMAGE_EXTENSIONS  = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
        private static readonly string[] AUDIO_EXTENSIONS  = { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma" };
        #endregion

        #region Events
        /// <summary>Fired at the very start of a scan.</summary>
        public event Action OnScanStarted;

        /// <summary>Fired periodically during a scan. Arg: 0.0 - 1.0 progress fraction.</summary>
        public event Action<float> OnScanProgress;

        /// <summary>Fired when the full scan coroutine finishes.</summary>
        public event Action<ScanResult> OnScanCompleted;

        /// <summary>Fired when new items are found during an incremental background scan.</summary>
        public event Action<List<MediaVideoInfo>> OnNewItemsDetected;

        /// <summary>Fired when stale items (deleted files) are removed during a background scan.</summary>
        public event Action<List<string>> OnItemsRemoved;
        #endregion

        #region Properties
        public bool IsScanning { get; private set; }
        public bool IsBackgroundScanning { get; private set; }
        #endregion

        #region Private Fields
        private readonly MonoBehaviour _host;
        private readonly Func<HashSet<string>> _getFavorites;
        private Coroutine _scanCoroutine;
        private Coroutine _backgroundScanCoroutine;
        #endregion

        /// <summary>
        /// Create a new scanner.
        /// </summary>
        /// <param name="host">MonoBehaviour used to start coroutines (typically MediaLibraryService).</param>
        /// <param name="getFavorites">Delegate that returns the current set of favorite paths so scanned items can be pre-marked.</param>
        public MediaLibraryScanner(MonoBehaviour host, Func<HashSet<string>> getFavorites)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _getFavorites = getFavorites ?? throw new ArgumentNullException(nameof(getFavorites));
        }

        #region Public API
        /// <summary>
        /// Start a full library scan. Results are delivered via <see cref="OnScanCompleted"/>.
        /// </summary>
        /// <param name="onComplete">Optional one-shot callback (same data as OnScanCompleted).</param>
        public void StartScan(Action<ScanResult> onComplete = null)
        {
            if (IsScanning)
            {
                Debug.LogWarning("[MediaLibraryScanner] Already scanning");
                return;
            }

            StopScan();
            _scanCoroutine = _host.StartCoroutine(ScanCoroutine(onComplete));
        }

        /// <summary>Stop any running full scan.</summary>
        public void StopScan()
        {
            if (_scanCoroutine != null)
            {
                _host.StopCoroutine(_scanCoroutine);
                _scanCoroutine = null;
            }
            IsScanning = false;
        }

        /// <summary>
        /// Perform a background incremental scan. Does NOT replace the existing library -
        /// only appends new items and fires removal events for deleted files.
        /// </summary>
        /// <param name="existingVideos">Reference to the current AllVideos list (modified in-place).</param>
        public void StartBackgroundScan(List<MediaVideoInfo> existingVideos)
        {
            if (IsBackgroundScanning || IsScanning)
            {
                Debug.Log("[MediaLibraryScanner] Background scan skipped - already scanning");
                return;
            }

            if (_backgroundScanCoroutine != null)
            {
                _host.StopCoroutine(_backgroundScanCoroutine);
            }

            _backgroundScanCoroutine = _host.StartCoroutine(BackgroundScanCoroutine(existingVideos));
        }
        #endregion

        #region Full Scan Coroutine
        private IEnumerator ScanCoroutine(Action<ScanResult> onComplete)
        {
            IsScanning = true;
            OnScanStarted?.Invoke();
            OnScanProgress?.Invoke(0f);

            var result = new ScanResult();

#if UNITY_ANDROID && !UNITY_EDITOR
            yield return ScanUsingMediaStore(result);
#else
            yield return ScanUsingDirectories(result);
#endif

            // Deduplicate by path
            int beforeDedup = result.Videos.Count;
            var seenPaths = new HashSet<string>();
            result.Videos = result.Videos.Where(v => seenPaths.Add(v.Path)).ToList();
            if (result.Videos.Count < beforeDedup)
            {
                Debug.Log($"[MediaLibraryScanner] Deduplicated: {beforeDedup} -> {result.Videos.Count}");
            }

            // Sort by name
            result.Videos = result.Videos.OrderBy(v => v.Title).ToList();

            IsScanning = false;
            OnScanProgress?.Invoke(1f);
            OnScanCompleted?.Invoke(result);
            onComplete?.Invoke(result);

            Debug.Log($"[MediaLibraryScanner] Scan complete: {result.Videos.Count} media files");
        }
        #endregion

        #region Android MediaStore Scan
#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator ScanUsingMediaStore(ScanResult result)
        {
            Debug.Log("[MediaLibraryScanner] Using Android MediaStore for fast media discovery...");
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            var mediaItems = AndroidMediaStoreHelper.QueryAllMedia();
            Debug.Log($"[MediaLibraryScanner] MediaStore query returned {mediaItems.Count} items in {startTime.ElapsedMilliseconds}ms");

            int processed = 0;
            int total = mediaItems.Count;
            var favorites = _getFavorites();

            foreach (var item in mediaItems)
            {
                try
                {
                    var videoInfo = CreateVideoInfoFromMediaStore(item, favorites);
                    result.Videos.Add(videoInfo);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MediaLibraryScanner] Error processing MediaStore item {item.Path}: {ex.Message}");
                }

                processed++;
                if (processed % 50 == 0)
                {
                    OnScanProgress?.Invoke((float)processed / total);
                    yield return null;
                }
            }

            Debug.Log($"[MediaLibraryScanner] MediaStore scan complete in {startTime.ElapsedMilliseconds}ms");
        }

        private MediaVideoInfo CreateVideoInfoFromMediaStore(AndroidMediaStoreHelper.MediaStoreItem item, HashSet<string> favorites)
        {
            var info = new MediaVideoInfo
            {
                Path          = item.Path,
                Title         = !string.IsNullOrEmpty(item.Title)       ? item.Title :
                                !string.IsNullOrEmpty(item.DisplayName) ? Path.GetFileNameWithoutExtension(item.DisplayName) : "Unknown",
                FileSizeBytes = item.SizeBytes,
                DateAdded     = item.DateAdded,
                DateModified  = item.DateModified,
                Duration      = TimeSpan.FromMilliseconds(item.DurationMs),
                Width         = item.Width,
                Height        = item.Height,
                IsFavorite    = favorites.Contains(item.Path),
                Format        = MediaVideoInfo.DetectFormat(item.Path),
                PlaylistIds   = new System.Collections.Generic.List<string>()
            };
            info.Projection = ProjectionDetector.DetectProjection(item.Path, item.Width, item.Height);
            return info;
        }
#else
        private IEnumerator ScanUsingMediaStore(ScanResult result)
        {
            // Stub for non-Android builds - falls through to directory scan
            yield return ScanUsingDirectories(result);
        }
#endif
        #endregion

        #region Directory Scan
        private IEnumerator ScanUsingDirectories(ScanResult result)
        {
            var foundFiles  = new List<string>();
            var scanRoots   = GetScanRoots();
            var allExtensions = VIDEO_EXTENSIONS.Concat(IMAGE_EXTENSIONS).Concat(AUDIO_EXTENSIONS).ToHashSet();
            var favorites   = _getFavorites();

            Debug.Log($"[MediaLibraryScanner] Starting directory scan in {scanRoots.Count} locations: {string.Join(", ", scanRoots)}");

            foreach (var root in scanRoots)
            {
                if (!Directory.Exists(root))
                {
                    Debug.Log($"[MediaLibraryScanner] Skipping non-existent root: {root}");
                    continue;
                }
                Debug.Log($"[MediaLibraryScanner] Scanning: {root}");

                try
                {
                    var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                        .Where(f => allExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();
                    foundFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MediaLibraryScanner] Error scanning {root}: {ex.Message}");
                }

                yield return null;
            }

            Debug.Log($"[MediaLibraryScanner] Found {foundFiles.Count} media files");

            int processed = 0;
            int total = foundFiles.Count;

            foreach (var filePath in foundFiles)
            {
                try
                {
                    var videoInfo = CreateVideoInfo(filePath, favorites);
                    result.Videos.Add(videoInfo);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MediaLibraryScanner] Error processing {filePath}: {ex.Message}");
                }

                processed++;
                if (processed % 10 == 0)
                {
                    OnScanProgress?.Invoke((float)processed / total);
                    yield return null;
                }
            }
        }
        #endregion

        #region Background Incremental Scan
        private IEnumerator BackgroundScanCoroutine(List<MediaVideoInfo> allVideos)
        {
            IsBackgroundScanning = true;
            var newItems      = new List<MediaVideoInfo>();
            var removedPaths  = new List<string>();
            var existingPaths = new HashSet<string>(allVideos.Select(v => v.Path));
            var favorites     = _getFavorites();

            Debug.Log($"[MediaLibraryScanner] Starting background scan (existing: {existingPaths.Count} items)");
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            // STEP 1: Validate existing - remove deleted files
            const int VALIDATE_BATCH = 50;
            int validateCount = 0;
            for (int i = allVideos.Count - 1; i >= 0; i--)
            {
                var video = allVideos[i];
                if (!File.Exists(video.Path))
                {
                    removedPaths.Add(video.Path);
                    allVideos.RemoveAt(i);
                }
                validateCount++;
                if (validateCount % VALIDATE_BATCH == 0) yield return null;
            }

            existingPaths = new HashSet<string>(allVideos.Select(v => v.Path));

            // STEP 2: Scan for new items
#if UNITY_ANDROID && !UNITY_EDITOR
            var mediaItems = AndroidMediaStoreHelper.QueryAllMedia();
            foreach (var item in mediaItems)
            {
                if (!existingPaths.Contains(item.Path))
                {
                    try { newItems.Add(CreateVideoInfoFromMediaStore(item, favorites)); } catch { }
                }
            }
            yield return null;
#else
            var scanRoots     = GetScanRoots();
            var allExtensions = VIDEO_EXTENSIONS.Concat(IMAGE_EXTENSIONS).Concat(AUDIO_EXTENSIONS).ToHashSet();

            foreach (var root in scanRoots)
            {
                if (!Directory.Exists(root)) continue;

                string[] files = null;
                try { files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var filePath in files)
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (!allExtensions.Contains(ext)) continue;
                    if (!existingPaths.Contains(filePath))
                    {
                        try { newItems.Add(CreateVideoInfo(filePath, favorites)); } catch { }
                    }
                }

                yield return null;
            }
#endif

            IsBackgroundScanning = false;

            bool hasChanges = newItems.Count > 0 || removedPaths.Count > 0;
            if (hasChanges)
            {
                if (newItems.Count > 0) allVideos.AddRange(newItems);

                // Re-sort in-place
                var sorted = allVideos.OrderBy(v => v.Title).ToList();
                allVideos.Clear();
                allVideos.AddRange(sorted);

                Debug.Log($"[MediaLibraryScanner] Background scan complete: +{newItems.Count} new, -{removedPaths.Count} removed ({startTime.ElapsedMilliseconds}ms)");

                if (newItems.Count > 0) OnNewItemsDetected?.Invoke(newItems);
                if (removedPaths.Count > 0) OnItemsRemoved?.Invoke(removedPaths);
            }
            else
            {
                Debug.Log($"[MediaLibraryScanner] Background scan complete - no changes ({startTime.ElapsedMilliseconds}ms)");
            }
        }
        #endregion

        #region Factory Helpers
        /// <summary>
        /// Create a MediaVideoInfo from a file path (performs FileInfo I/O).
        /// </summary>
        public MediaVideoInfo CreateVideoInfo(string filePath, HashSet<string> favorites = null)
        {
            var info = MediaVideoInfo.FromPath(filePath);
            info.IsFavorite  = favorites != null && favorites.Contains(filePath);
            info.Projection  = ProjectionDetector.DetectProjection(filePath, 0, 0);
            return info;
        }

        /// <summary>
        /// Create a MediaVideoInfo from cached data without any disk I/O.
        /// </summary>
        public MediaVideoInfo CreateVideoInfoQuick(
            string filePath,
            long fileSize        = 0,
            long dateAddedTicks  = 0,
            long dateModifiedTicks = 0,
            int  width           = 0,
            int  height          = 0,
            HashSet<string> favorites = null)
        {
            var info = new MediaVideoInfo
            {
                Path          = filePath,
                Title         = Path.GetFileNameWithoutExtension(filePath),
                Format        = MediaVideoInfo.DetectFormat(filePath),
                PlaylistIds   = new List<string>(),
                IsFavorite    = favorites != null && favorites.Contains(filePath),
                DateAdded     = dateAddedTicks    > 0 ? new DateTime(dateAddedTicks)    : DateTime.MinValue,
                DateModified  = dateModifiedTicks > 0 ? new DateTime(dateModifiedTicks) : DateTime.MinValue,
                FileSizeBytes = fileSize
            };
            info.Projection = ProjectionDetector.DetectProjection(filePath, width, height);
            return info;
        }
        #endregion

        #region Scan Roots
        private List<string> GetScanRoots()
        {
            var roots = new List<string>();

#if UNITY_ANDROID && !UNITY_EDITOR
            string externalStorage = "/storage/emulated/0";
            if (Directory.Exists(externalStorage))
            {
                roots.Add(Path.Combine(externalStorage, "Movies"));
                roots.Add(Path.Combine(externalStorage, "Download"));
                roots.Add(Path.Combine(externalStorage, "DCIM"));
                roots.Add(Path.Combine(externalStorage, "Video"));
                roots.Add(Path.Combine(externalStorage, "VR"));
            }
            string sdCard = "/storage/sdcard1";
            if (Directory.Exists(sdCard)) roots.Add(sdCard);
#else
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(userProfile, "Videos"));
            roots.Add(Path.Combine(userProfile, "Pictures"));
            roots.Add(Path.Combine(userProfile, "Music"));
            roots.Add(Path.Combine(userProfile, "Downloads"));
            if (Directory.Exists(Application.streamingAssetsPath))
                roots.Add(Application.streamingAssetsPath);
#endif

            return roots.Where(Directory.Exists).ToList();
        }
        #endregion

        #region Extension Helpers
        public static bool IsVideoExtension(string ext)  => Array.IndexOf(VIDEO_EXTENSIONS, ext) >= 0;
        public static bool IsImageExtension(string ext)  => Array.IndexOf(IMAGE_EXTENSIONS, ext) >= 0;
        public static bool IsAudioExtension(string ext)  => Array.IndexOf(AUDIO_EXTENSIONS, ext) >= 0;
        #endregion
    }

    /// <summary>
    /// Result returned by a full library scan.
    /// </summary>
    public class ScanResult
    {
        public List<MediaVideoInfo>  Videos { get; set; } = new List<MediaVideoInfo>();
        public List<MediaAudioInfo>  Audio  { get; set; } = new List<MediaAudioInfo>();
        public List<MediaImageInfo>  Images { get; set; } = new List<MediaImageInfo>();
    }
}
