using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRWorkspace.UI.RTT;
using VRWorkspace.Core;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.RTT.Services;
using VRWorkspace.Utilities;

namespace VRWorkspace.UI.RTT.Controllers
{
    /// <summary>
    /// Controller for the File Manager app.
    /// Handles business logic, data fetching, and state management.
    /// Follows MVVM pattern where this controls the RTTFileManager View.
    /// </summary>
    public partial class RTTFileManagerController : MonoBehaviour, IPaginationController, IDataBindable
    {
        #region Private Fields
        private RTTFileManager _view;
        private GameObject _viewObject;

        // IDataBindable support
        private RTTLoadingSpinner _loadingSpinner;
        private bool _isPreparingData = false;
        private bool _isDataReady = false;
        private List<MockFile> _preparedDataBuffer = null;

        // State caching
        private const string APP_ID = "files";
        #endregion

        #region Events (IDataBindable)
        public event Action OnDataPrepared;
        #endregion

        #region Events
        public event Action OnBackClicked;
        #endregion

        #region State
        private string _currentPath = "root";
        // Using filtered lists for search support
        private List<MockFile> _currentDirectoryFiles = new List<MockFile>();
        private List<MockFile> _filteredFiles = new List<MockFile>();
        private string _currentSearchQuery = "";

        // Selection/Hover State
        private MockFile? _selectedFile = null;
        private MockFile? _hoveredFile = null;

        private int _currentPage = 1;
        private int _pageSize = 8; // Default: ~2 rows x 4 cols for grid view (will be recalculated by view)

        // Sort State
        private string _sortBy = "Name";
        private bool _sortAscending = true;

        // Navigation history - stores first visible item index for each visited path
        // Key: path, Value: first visible item index (0-based)
        // Using item index instead of page number because Grid and List have different items per page
        private Dictionary<string, int> _itemIndexHistory = new Dictionary<string, int>();

        // Category Filter Mode (for scanning all videos/music from storage)
        private bool _isFilterMode = false;
        private FileCategory _filterCategory = FileCategory.Unknown;
        private Coroutine _scanCoroutine = null;
        private bool _isScanning = false;

        // Track if initial navigation has completed (skip fade on first load)
        private bool _hasNavigatedOnce = false;

        // Lock detail panel to show current folder (ignore hover until user clicks)
        private bool _lockDetailToCurrentFolder = false;
        #endregion

        #region Public API
        public GameObject CreateMenu(RectTransform container, float containerW, float containerH, TMPro.TMP_FontAsset font, Color primaryColor, Color accentColor)
        {
            // 1. Create View Object
            _viewObject = new GameObject("RTTFileManager");
            _viewObject.transform.SetParent(container, false);

            RectTransform rt = _viewObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 2. Add View Component
            _view = _viewObject.AddComponent<RTTFileManager>();

            // 3. Configure View
            _view.Initialize(this, containerW, containerH, font, primaryColor, accentColor);

            // 4. Build Initial UI
            _view.BuildUI();

            Debug.Log("[RTTFileManagerController] Menu created");
            return _viewObject;
        }

        public void Cleanup()
        {
            if (_viewObject != null)
            {
                Destroy(_viewObject);
            }
        }

        public void HandleBack()
        {
            OnBackClicked?.Invoke();
        }

        public void OnViewReady()
        {
            Debug.Log("[Controller] View is ready. Checking storage permissions...");

    #if UNITY_ANDROID && !UNITY_EDITOR
            // Check if AppPermissionManager already handled permissions at startup
            if (AppPermissionManager.Instance != null && AppPermissionManager.Instance.HasAllPermissions)
            {
                Debug.Log("[Controller] Permissions already granted via AppPermissionManager.");
                NavigateTo("root");
                return;
            }

            // Fallback: Check for FULL file access (all file types including documents, text, etc.)
            if (!StoragePermissionHelper.HasFullFileAccess())
            {
                // Check if we have at least some storage permission
                if (StoragePermissionHelper.HasStoragePermission())
                {
                    Debug.Log("[Controller] Has media-only access. Proceeding with limited view.");
                    NavigateTo("root");
                    return;
                }

                Debug.Log("[Controller] No storage access. Requesting...");
                StoragePermissionHelper.RequestFullFileAccess((granted) =>
                {
                    if (granted)
                    {
                        Debug.Log("[Controller] Full file access granted. Loading files...");
                    }
                    else
                    {
                        Debug.LogWarning("[Controller] Full file access denied. Only media files may be visible.");
                    }
                    // Navigate regardless - user can still browse with whatever access they have
                    NavigateTo("root");
                });
                return;
            }
            Debug.Log("[Controller] Full file access already granted.");
    #endif

            NavigateTo("root");
        }

        /// <summary>
        /// Request full file access permission. Called from UI when user wants to see all files.
        /// </summary>
        public void RequestFullFileAccessPermission()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            // Use AppPermissionManager if available
            if (AppPermissionManager.Instance != null)
            {
                AppPermissionManager.Instance.RequestFullFileAccess();
                return;
            }

            // Fallback to StoragePermissionHelper
            StoragePermissionHelper.RequestFullFileAccess((granted) =>
            {
                if (granted)
                {
                    Debug.Log("[Controller] Full file access granted. Refreshing...");
                    RefreshCurrentFolder();
                }
                else
                {
                    Debug.LogWarning("[Controller] Full file access denied.");
                }
            });
    #endif
        }

        public void NavigateTo(string path)
        {
            NavigateToInternal(path, restorePage: false, clickedFolderPath: null);
        }

        /// <summary>
        /// Navigate into a folder that was clicked. Saves the folder's index for accurate page restoration.
        /// </summary>
        public void NavigateToFolder(string folderPath)
        {
            NavigateToInternal(folderPath, restorePage: false, clickedFolderPath: folderPath);
        }

        /// <summary>
        /// Navigate to a path via breadcrumb - restores saved page number if available
        /// </summary>
        public void NavigateBack(string path)
        {
            NavigateToInternal(path, restorePage: true, clickedFolderPath: null);
        }

        // Normalize path for history storage - treat "root" and RootPath as the same
        private string NormalizePathForHistory(string path)
        {
            if (path == "root")
                return FileSystemService.RootPath;
            return path;
        }

        // Coroutine for async navigation
        private Coroutine _navigateCoroutine = null;

        private void NavigateToInternal(string path, bool restorePage, string clickedFolderPath)
        {
            // Cancel any ongoing navigation
            if (_navigateCoroutine != null)
            {
                StopCoroutine(_navigateCoroutine);
                _navigateCoroutine = null;
            }

            // Start async navigation
            _navigateCoroutine = StartCoroutine(NavigateToInternalAsync(path, restorePage, clickedFolderPath));
        }

        private IEnumerator NavigateToInternalAsync(string path, bool restorePage, string clickedFolderPath)
        {
            Debug.Log($"[Controller] NavigateToInternalAsync: path='{path}', restorePage={restorePage}, clickedFolder='{clickedFolderPath}'");

            // Exit filter mode when navigating to a real folder
            if (_isFilterMode)
            {
                _isFilterMode = false;
                _filterCategory = FileCategory.Unknown;
                _view?.ShowScanningIndicator(false, "", 0);
            }

            // Cancel any ongoing scan
            if (_scanCoroutine != null)
            {
                StopCoroutine(_scanCoroutine);
                _scanCoroutine = null;
                _isScanning = false;
            }

            // Normalize paths for comparison and history
            string normalizedCurrentPath = NormalizePathForHistory(_currentPath);
            string normalizedDestPath = NormalizePathForHistory(path);

            // Save current item index before leaving (if we have a valid path and it's different from destination)
            if (!string.IsNullOrEmpty(_currentPath) && normalizedCurrentPath != normalizedDestPath)
            {
                int itemIndexToSave;

                // If we clicked on a folder, use that folder's index in the list
                if (!string.IsNullOrEmpty(clickedFolderPath))
                {
                    int folderIndex = _filteredFiles.FindIndex(f => f.Path == clickedFolderPath);
                    itemIndexToSave = folderIndex >= 0 ? folderIndex : (_currentPage - 1) * _pageSize;
                }
                else
                {
                    itemIndexToSave = (_currentPage - 1) * _pageSize;
                }

                _itemIndexHistory[normalizedCurrentPath] = itemIndexToSave;
            }

            _currentPath = path;
            _currentSearchQuery = ""; // Reset search state

            // Reset selection/hover and lock detail to current folder
            _selectedFile = null;
            _hoveredFile = null;
            _lockDetailToCurrentFolder = true; // Lock until user clicks an item

            // Update breadcrumbs immediately (doesn't need fade)
            _view?.UpdateBreadcrumbs(_currentPath);

            // Check if we should use fade animation (skip on first load)
            bool useFade = _hasNavigatedOnce && _view != null;

            // === PARALLEL: Start fade out AND data loading simultaneously ===
            bool fadeOutComplete = !useFade; // Skip waiting if no fade
            bool firstBatchReady = false;
            bool dataLoadComplete = false;
            int quickCount = -1; // Estimated total count for pagination
            List<MockFile> firstBatchFiles = null;
            List<MockFile> loadedFiles = null;

            // Start fade out animation (non-blocking) - only if not first load
            if (useFade)
            {
                _view.FadeOutContent(() => { fadeOutComplete = true; });
            }

            // Start loading files async (runs in parallel with fade)
            StartCoroutine(FileSystemService.GetFilesAsync(
                path,
                onQuickCount: (count) =>
                {
                    // Quick count fires immediately - use for early pagination display
                    quickCount = count;
                    Debug.Log($"[Controller] Quick count received: {count} items");
                },
                onFirstBatch: (files) =>
                {
                    firstBatchFiles = files;
                    firstBatchReady = true;
                },
                onProgress: null,
                onComplete: (files) =>
                {
                    loadedFiles = files;
                    dataLoadComplete = true;
                }
            ));

            // Wait for fade out AND first batch (show first 8 items immediately)
            while (!fadeOutComplete || !firstBatchReady)
            {
                // If data load finished before first batch ready (small folder), use that
                if (dataLoadComplete && !firstBatchReady)
                {
                    firstBatchFiles = loadedFiles;
                    firstBatchReady = true;
                }
                yield return null;
            }

            // === First batch ready - show immediately while loading continues ===
            // If data load already complete (fast folder), use full data instead of first batch
            if (dataLoadComplete && loadedFiles != null)
            {
                _currentDirectoryFiles = loadedFiles;
                Debug.Log($"[Controller] Using full data (load completed quickly): {loadedFiles.Count} items");
            }
            else
            {
                _currentDirectoryFiles = firstBatchFiles ?? new List<MockFile>();
            }
            _filteredFiles = new List<MockFile>(_currentDirectoryFiles);

            // Apply current sort
            ApplySort();

            // Reset to page 1 for now (will restore later if needed)
            _currentPage = 1;

            // Update view with first batch or full data
            if (!dataLoadComplete && quickCount > 0)
            {
                // Use quick count for early pagination display
                // This shows pagination immediately with estimated total
                UpdateView(true, skipPagination: true);
                UpdateDetailView();

                // Show pagination with estimated count
                int estimatedTotalPages = Mathf.CeilToInt((float)quickCount / _pageSize);
                _view?.UpdatePagination(_currentPage, estimatedTotalPages);
                _view?.UpdateItemCount(quickCount);
                Debug.Log($"[Controller] Showing early pagination with quick count: {quickCount} items, {estimatedTotalPages} pages");
            }
            else
            {
                // Small folder or data already complete - show actual pagination
                UpdateView(true);
                UpdateDetailView();
            }

            // Fade in the new content (only if we used fade out)
            if (useFade)
            {
                _view.FadeInContent();
            }

            // === Background: Wait for full load to complete ===
            if (!dataLoadComplete)
            {
                while (!dataLoadComplete)
                {
                    yield return null;
                }

                // Update with full data
                _currentDirectoryFiles = loadedFiles ?? new List<MockFile>();
                _filteredFiles = new List<MockFile>(_currentDirectoryFiles);
                ApplySort();

                // Restore page from item index if needed
                if (restorePage && _itemIndexHistory.TryGetValue(normalizedDestPath, out int savedItemIndex))
                {
                    int calculatedPage = (savedItemIndex / Mathf.Max(1, _pageSize)) + 1;
                    int totalPages = CalculateTotalPages();
                    _currentPage = Mathf.Clamp(calculatedPage, 1, totalPages);
                }

                // Update view with full data - now show pagination with correct count
                UpdateView(true);
            }
            else
            {
                // Small folder - already have full data, restore page if needed
                if (restorePage && _itemIndexHistory.TryGetValue(normalizedDestPath, out int savedItemIndex))
                {
                    int calculatedPage = (savedItemIndex / Mathf.Max(1, _pageSize)) + 1;
                    int totalPages = CalculateTotalPages();
                    _currentPage = Mathf.Clamp(calculatedPage, 1, totalPages);
                    UpdateView(false); // Just update pagination, don't reload grid
                }
            }

            // Mark that we've navigated at least once
            _hasNavigatedOnce = true;

            _navigateCoroutine = null;
        }
        #endregion

        #region IDataBindable Implementation
        /// <summary>
        /// FileManager is ready when view exists and data has been prepared.
        /// </summary>
        public bool IsDataReady => _isDataReady;

        /// <summary>
        /// True when scanning for files or preparing data.
        /// </summary>
        public bool IsPreparingData => _isPreparingData || _isScanning;

        /// <summary>
        /// Start preparing data in background.
        /// For FileManager, this loads files for current path asynchronously.
        /// </summary>
        public void PrepareDataAsync()
        {
            _isPreparingData = true;
            _isDataReady = false;

            // Start background preparation
            StartCoroutine(PrepareDataCoroutine());
        }

        private IEnumerator PrepareDataCoroutine()
        {
            Debug.Log("[RTTFileManagerController] PrepareDataAsync started");

            // Yield one frame to allow UI to set up
            yield return null;

            // Prepare data buffer with current directory files
            string pathToLoad = _currentPath;
            if (string.IsNullOrEmpty(pathToLoad))
            {
                pathToLoad = "root";
            }

            // Load files asynchronously to prevent UI freeze with large directories
            bool isLoading = true;
            List<MockFile> loadedFiles = null;

            yield return FileSystemService.GetFilesAsync(
                pathToLoad,
                onQuickCount: null, // PrepareDataAsync doesn't need quick count
                onFirstBatch: null, // PrepareDataAsync doesn't need first batch
                onProgress: null,
                onComplete: (files) =>
                {
                    loadedFiles = files;
                    isLoading = false;
                }
            );

            while (isLoading)
            {
                yield return null;
            }

            _preparedDataBuffer = loadedFiles ?? new List<MockFile>();

            _isPreparingData = false;
            _isDataReady = true;

            Debug.Log($"[RTTFileManagerController] PrepareDataAsync completed: {_preparedDataBuffer?.Count ?? 0} files");

            // Fire event to notify RTTAppManager
            OnDataPrepared?.Invoke();
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
        /// FileManager file listing is fast, so we just refresh immediately.
        /// </summary>
        public void BindCachedDataOrEmpty()
        {
            _isPreparingData = false;

            // FileManager data binding is synchronous and fast
            // Just ensure view is updated with current directory
            if (_view != null)
            {
                RefreshCurrentFolder();
                Debug.Log("[RTTFileManagerController] BindCachedDataOrEmpty: Refreshed current folder");
            }
        }

        /// <summary>
        /// Called when background data loading completes.
        /// For FileManager, this could be called after a category scan completes.
        /// </summary>
        public void OnBackgroundDataReady()
        {
            Debug.Log("[RTTFileManagerController] OnBackgroundDataReady called");

            // Bind the prepared data buffer if available
            if (_preparedDataBuffer != null)
            {
                _currentDirectoryFiles = _preparedDataBuffer;
                _filteredFiles = new List<MockFile>(_currentDirectoryFiles);
                ApplySort();
                _currentPage = 1;
                UpdateView(true);
                UpdateDetailView();
                _view?.UpdateBreadcrumbs(_currentPath);
            }
        }

        /// <summary>
        /// Show loading spinner centered in the file grid area.
        /// </summary>
        public void ShowLoadingSpinner()
        {
            if (_loadingSpinner != null)
            {
                _loadingSpinner.Show();
                return;
            }

            // Create spinner centered in view
            if (_viewObject != null)
            {
                _loadingSpinner = RTTLoadingSpinner.Create(_viewObject.transform, Color.white);
                _loadingSpinner.Show();
            }
        }

        /// <summary>
        /// Hide loading spinner with fade out animation.
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
            // Side panels now fade in with main frame via coordinated animation in RTTAppManager
            Debug.Log("[RTTFileManagerController] OnAppShown called");

            // OPTIMIZATION: Refresh current directory if it has changed since last view
            // This ensures the file list is up-to-date with new/deleted files
            if (!string.IsNullOrEmpty(_currentPath) && _currentPath != "root")
            {
                string absolutePath = FileSystemService.GetAbsolutePath(_currentPath);

                // Check if folder has been modified (new/deleted files)
                if (AppStateCache.Instance != null &&
                    AppStateCache.Instance.HasPathChanged(absolutePath, DateTime.Now.AddMinutes(-1).Ticks))
                {
                    Debug.Log($"[RTTFileManagerController] OnAppShown: Directory changed, refreshing: {_currentPath}");
                    NavigateTo(_currentPath);
                }
            }
        }

        #region State Caching Support

        /// <summary>
        /// FileManager supports state caching for fast app switching.
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
                Debug.Log("[RTTFileManagerController] TryRestoreCachedState: No cache found");
                return false;
            }

            // Validate cache is still valid (path exists, not too old)
            if (!string.IsNullOrEmpty(snapshot.currentPath) &&
                snapshot.currentPath != "root" &&
                !Directory.Exists(FileSystemService.GetAbsolutePath(snapshot.currentPath)))
            {
                Debug.Log($"[RTTFileManagerController] TryRestoreCachedState: Path no longer exists: {snapshot.currentPath}");
                AppStateCache.Instance.InvalidateState(APP_ID);
                return false;
            }

            // Check if folder has been modified since cache
            if (AppStateCache.Instance.HasPathChanged(
                FileSystemService.GetAbsolutePath(snapshot.currentPath),
                snapshot.timestamp))
            {
                Debug.Log($"[RTTFileManagerController] TryRestoreCachedState: Folder modified since cache");
                // Don't invalidate - we'll use cache but refresh in background
            }

            // Restore state
            _currentPath = snapshot.currentPath ?? "root";
            _currentPage = snapshot.currentPage > 0 ? snapshot.currentPage : 1;
            _pageSize = 8; // Force to current default, view will refine it later via SetPageSize
            _sortBy = !string.IsNullOrEmpty(snapshot.sortBy) ? snapshot.sortBy : "Name";
            _sortAscending = snapshot.sortAscending;

            // Restore cached items with validation
            if (snapshot.items != null && snapshot.items.Count > 0)
            {
                _currentDirectoryFiles = new List<MockFile>();
                _filteredFiles = new List<MockFile>();
                int skippedCount = 0;

                foreach (var cachedItem in snapshot.items)
                {
                    // Validate file/folder still exists before adding
                    string absolutePath = FileSystemService.GetAbsolutePath(cachedItem.path);
                    bool exists = cachedItem.isDirectory
                        ? Directory.Exists(absolutePath)
                        : File.Exists(absolutePath);

                    if (!exists)
                    {
                        skippedCount++;
                        continue; // Skip deleted files/folders
                    }

                    var file = new MockFile
                    {
                        Name = cachedItem.name,
                        Path = cachedItem.path,
                        IsFolder = cachedItem.isDirectory,
                        Type = cachedItem.isDirectory ? "Folder" : cachedItem.category,
                        Size = cachedItem.fileSize
                    };

                    // Try to parse duration if available
                    if (!string.IsNullOrEmpty(cachedItem.duration) && TimeSpan.TryParse(cachedItem.duration, out var duration))
                    {
                        file.Duration = duration;
                    }

                    _currentDirectoryFiles.Add(file);
                    _filteredFiles.Add(file);
                }

                // If all items were deleted, invalidate cache
                if (_filteredFiles.Count == 0 && skippedCount > 0)
                {
                    Debug.Log($"[RTTFileManagerController] TryRestoreCachedState: All {skippedCount} cached items no longer exist");
                    AppStateCache.Instance.InvalidateState(APP_ID);
                    return false;
                }

                // Update view with validated cached data
                if (_view != null)
                {
                    UpdateView(true);
                    _view.UpdateBreadcrumbs(_currentPath);
                }

                if (skippedCount > 0)
                {
                    Debug.Log($"[RTTFileManagerController] TryRestoreCachedState: Restored {_filteredFiles.Count} items, skipped {skippedCount} deleted items");
                    // Invalidate cache so it gets refreshed with current data
                    AppStateCache.Instance.InvalidateState(APP_ID);
                }
                else
                {
                    Debug.Log($"[RTTFileManagerController] TryRestoreCachedState: Restored {snapshot.items.Count} items from cache");
                }
                return true;
            }

            Debug.Log("[RTTFileManagerController] TryRestoreCachedState: Cache has no items");
            return false;
        }

        /// <summary>
        /// Save current UI state to cache.
        /// </summary>
        public void CacheCurrentState()
        {
            if (_filteredFiles == null || _filteredFiles.Count == 0)
            {
                Debug.Log("[RTTFileManagerController] CacheCurrentState: No files to cache");
                return;
            }

            var snapshot = new AppStateSnapshot
            {
                appId = APP_ID,
                currentPath = _currentPath,
                currentPage = _currentPage,
                pageSize = _pageSize,
                sortBy = _sortBy,
                sortAscending = _sortAscending,
                selectedItemPath = _selectedFile?.Path,
                totalItemCount = _filteredFiles.Count,
                items = new List<CachedItemRef>()
            };

            // Cache visible items (current page + surrounding pages for smooth scrolling)
            int startIndex = Mathf.Max(0, (_currentPage - 2) * _pageSize);
            int endIndex = Mathf.Min(_filteredFiles.Count, (_currentPage + 2) * _pageSize);

            for (int i = startIndex; i < endIndex; i++)
            {
                var file = _filteredFiles[i];
                var cachedRef = new CachedItemRef
                {
                    path = file.Path,
                    name = file.Name,
                    isDirectory = file.IsFolder,
                    category = file.Type,
                    fileSize = file.Size,
                    duration = file.Duration.ToString()
                };

                // Store thumbnail cache key if available
                if (!file.IsFolder)
                {
                    cachedRef.thumbnailCacheKey = ThumbnailCacheKeyHelper.GetCacheKey(file.Path);
                }

                snapshot.items.Add(cachedRef);
            }

            AppStateCache.Instance.SaveState(APP_ID, snapshot);
            Debug.Log($"[RTTFileManagerController] CacheCurrentState: Cached {snapshot.items.Count} items");
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
        /// </summary>
        public void BindPreparedData(object dataBuffer)
        {
            if (dataBuffer is List<MockFile> files)
            {
                _currentDirectoryFiles = files;
                _filteredFiles = new List<MockFile>(files);
                ApplySort();
                _currentPage = 1;

                if (_view != null)
                {
                    UpdateView(true);
                    UpdateDetailView();
                    _view.UpdateBreadcrumbs(_currentPath);
                }

                Debug.Log($"[RTTFileManagerController] BindPreparedData: Bound {files.Count} files");
            }
        }

        #endregion

        private void OnDestroy()
        {
            // Cancel any ongoing scan
            if (_scanCoroutine != null)
            {
                StopCoroutine(_scanCoroutine);
                _scanCoroutine = null;
            }

            // Cleanup loading spinner
            if (_loadingSpinner != null)
            {
                Destroy(_loadingSpinner.gameObject);
                _loadingSpinner = null;
            }
        }
        #endregion
    }
}
