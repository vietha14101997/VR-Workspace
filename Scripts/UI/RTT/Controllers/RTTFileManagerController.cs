using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VRWorkspace.UI.RTT;

/// <summary>
/// Controller for the File Manager app.
/// Handles business logic, data fetching, and state management.
/// Follows MVVM pattern where this controls the RTTFileManager View.
/// </summary>
public class RTTFileManagerController : MonoBehaviour, IPaginationController, IDataBindable
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

    // Coroutine for async refresh
    private Coroutine _refreshCoroutine = null;

    public void RefreshCurrentFolder()
    {
        Debug.Log("[Controller] Refreshing current folder...");

        // Cancel any ongoing refresh
        if (_refreshCoroutine != null)
        {
            StopCoroutine(_refreshCoroutine);
            _refreshCoroutine = null;
        }

        _refreshCoroutine = StartCoroutine(RefreshCurrentFolderAsync(false));
    }

    /// <summary>
    /// Refresh current folder while keeping the current page position.
    /// Used when switching between Grid/List view.
    /// </summary>
    public void RefreshCurrentFolderKeepPage()
    {
        Debug.Log("[Controller] Refreshing current folder (keep page)...");

        // Cancel any ongoing refresh
        if (_refreshCoroutine != null)
        {
            StopCoroutine(_refreshCoroutine);
            _refreshCoroutine = null;
        }

        _refreshCoroutine = StartCoroutine(RefreshCurrentFolderAsync(true));
    }

    private IEnumerator RefreshCurrentFolderAsync(bool keepPage)
    {
        bool useFade = _view != null;

        // Store old file paths to detect removed files
        var oldFilePaths = new HashSet<string>(_currentDirectoryFiles.Select(f => f.Path));

        // === PARALLEL: Start fade out AND data loading simultaneously ===
        bool fadeOutComplete = !useFade;
        bool dataLoadComplete = false;
        List<MockFile> loadedFiles = null;

        // Start fade out animation
        if (useFade)
        {
            _view.FadeOutContent(() => { fadeOutComplete = true; });
        }

        // Start loading files async
        StartCoroutine(FileSystemService.GetFilesAsync(
            _currentPath,
            onQuickCount: null, // Refresh doesn't need quick count
            onFirstBatch: null, // Refresh doesn't need first batch
            onProgress: null,
            onComplete: (files) =>
            {
                loadedFiles = files;
                dataLoadComplete = true;
            }
        ));

        // Wait for BOTH fade out AND data load to complete
        while (!fadeOutComplete || !dataLoadComplete)
        {
            yield return null;
        }

        _currentDirectoryFiles = loadedFiles ?? new List<MockFile>();

        // Detect removed files and clean up their thumbnails
        var newFilePaths = new HashSet<string>(_currentDirectoryFiles.Select(f => f.Path));
        var removedPaths = oldFilePaths.Where(p => !newFilePaths.Contains(p)).ToList();
        if (removedPaths.Count > 0)
        {
            Debug.Log($"[Controller] Refresh detected {removedPaths.Count} removed files");
            FileThumbnailService.Instance?.RemoveThumbnailsForPaths(removedPaths);
        }

        // Re-apply filter
        if (string.IsNullOrEmpty(_currentSearchQuery))
        {
            _filteredFiles = new List<MockFile>(_currentDirectoryFiles);
        }
        else
        {
            _filteredFiles = _currentDirectoryFiles.FindAll(f => f.Name.IndexOf(_currentSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        ApplySort();

        if (!keepPage)
        {
            _currentPage = 1;
        }
        else
        {
            // Clamp page to valid range (in case folder content changed)
            int totalPages = CalculateTotalPages();
            _currentPage = Mathf.Clamp(_currentPage, 1, totalPages);
        }

        // Update view while alpha is 0
        UpdateView(true);

        // Fade in the new content
        if (useFade)
        {
            _view.FadeInContent();
        }

        _refreshCoroutine = null;
    }

    /// <summary>
    /// Check if folder creation is allowed in the current path.
    /// </summary>
    public bool CanCreateFolderHere()
    {
        Debug.Log($"[Controller] CanCreateFolderHere called, _currentPath: '{_currentPath}'");
        bool result = FileSystemService.CanCreateFolderInPath(_currentPath);
        Debug.Log($"[Controller] CanCreateFolderHere result: {result}");
        return result;
    }

    public void SearchFiles(string query)
    {
        _currentSearchQuery = query;

        if (string.IsNullOrEmpty(query))
        {
            _filteredFiles = new List<MockFile>(_currentDirectoryFiles);
        }
        else
        {
            _filteredFiles = _currentDirectoryFiles.FindAll(f => f.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        ApplySort(); // Apply current sort after filtering
        _currentPage = 1;
        UpdateView(true);
    }

    public void SetSortOptions(string sortBy, bool ascending)
    {
        Debug.Log($"[Controller] Sort by: {sortBy}, Ascending: {ascending}");
        _sortBy = sortBy;
        _sortAscending = ascending;

        ApplySort();
        _currentPage = 1;
        UpdateView(true);
    }

    /// <summary>
    /// Set sort options without triggering a refresh (for initialization)
    /// </summary>
    public void SetSortOptionsNoRefresh(string sortBy, bool ascending)
    {
        Debug.Log($"[Controller] SetSortOptionsNoRefresh: sortBy={sortBy}, ascending={ascending}");
        _sortBy = sortBy;
        _sortAscending = ascending;
    }

    public void SetPageSize(int itemsPerPage)
    {
        if (_pageSize == itemsPerPage)
        {
            return;
        }

        // Emergency fix: if something is trying to set 10 (old design), force it to 8 for Grid
        if (itemsPerPage == 10) 
        {
            Debug.LogWarning($"[Controller] Something attempted to set pageSize to 10. Forcing to 8. StackTrace: {StackTraceUtility.ExtractStackTrace()}");
            itemsPerPage = 8;
            if (_pageSize == 8) return;
        }

        // Calculate current item index before changing page size
        int currentItemIndex = (_currentPage - 1) * _pageSize;

        Debug.Log($"[Controller] SetPageSize: {_pageSize} -> {itemsPerPage}, currentItemIndex: {currentItemIndex}");
        _pageSize = Mathf.Max(1, itemsPerPage);

        // Calculate new page from item index with new page size
        int newPage = (currentItemIndex / _pageSize) + 1;
        int totalPages = CalculateTotalPages();
        _currentPage = Mathf.Clamp(newPage, 1, totalPages);

        Debug.Log($"[Controller] New page: {_currentPage} (total: {totalPages})");
        UpdateView(false); // Recalculate pagination
    }

    private void ApplySort()
    {
        // Always put folders first, then sort within each group
        var folders = _filteredFiles.FindAll(f => f.IsFolder);
        var files = _filteredFiles.FindAll(f => !f.IsFolder);

        folders = SortList(folders);
        files = SortList(files);

        _filteredFiles.Clear();
        _filteredFiles.AddRange(folders);
        _filteredFiles.AddRange(files);
    }

    // Vietnamese culture for proper diacritics sorting (Đ with D, etc.)
    private static readonly CultureInfo VietnameseCulture = new CultureInfo("vi-VN");
    private static readonly StringComparer VietnameseComparer = StringComparer.Create(VietnameseCulture, ignoreCase: true);

    private List<MockFile> SortList(List<MockFile> list)
    {
        switch (_sortBy)
        {
            case "Name":
                list.Sort((a, b) => VietnameseComparer.Compare(a.Name, b.Name));
                break;
            case "Type":
                list.Sort((a, b) => VietnameseComparer.Compare(a.Type, b.Type));
                break;
            case "Created":
                list.Sort((a, b) => a.Created.CompareTo(b.Created));
                break;
            case "Modified":
                list.Sort((a, b) => a.Modified.CompareTo(b.Modified));
                break;
            case "Size":
                list.Sort((a, b) => a.Size.CompareTo(b.Size));
                break;
            case "Duration":
                list.Sort((a, b) => a.Duration.CompareTo(b.Duration));
                break;
            default:
                list.Sort((a, b) => VietnameseComparer.Compare(a.Name, b.Name));
                break;
        }

        if (!_sortAscending)
        {
            list.Reverse();
        }

        return list;
    }
    
    public void ChangePage(int delta)
    {
        int totalPages = CalculateTotalPages();
        int newPage = _currentPage + delta;
        
        if (newPage >= 1 && newPage <= totalPages)
        {
            _currentPage = newPage;
            UpdateView(false);
        }
    }

    public void GoToPage(int pageNumber)
    {
        int totalPages = CalculateTotalPages();
        if (pageNumber >= 1 && pageNumber <= totalPages)
        {
            _currentPage = pageNumber;
            UpdateView(false);
        }
    }

    /// <summary>
    /// Scroll to the very beginning (top) of the content.
    /// </summary>
    public void ScrollToStart()
    {
        _currentPage = 1;
        UpdateView(false); // Use UpdateView for proper scroll animation
    }

    /// <summary>
    /// Scroll to the very end (bottom) of the content.
    /// </summary>
    public void ScrollToEnd()
    {
        int totalPages = CalculateTotalPages();
        _currentPage = totalPages;
        UpdateView(false); // Use UpdateView for proper scroll animation
    }

    private int CalculateTotalPages()
    {
        int totalFiles = _filteredFiles.Count;
        if (totalFiles == 0) return 1;
        int size = Mathf.Max(1, _pageSize);
        int totalPages = Mathf.CeilToInt((float)totalFiles / size);
        Debug.Log($"[Controller] CalculateTotalPages: totalFiles={totalFiles}, pageSize={size}, totalPages={totalPages}");
        return totalPages;
    }

    private void UpdateView(bool fullReload, bool skipPagination = false)
    {
        int totalPages = CalculateTotalPages();
        _currentPage = Mathf.Clamp(_currentPage, 1, totalPages);

        if (_view != null)
        {
            if (fullReload)
            {
                // Update Grid with selection state
                string selectedPath = _selectedFile.HasValue ? _selectedFile.Value.Path : "";
                _view.UpdateGrid(_filteredFiles, selectedPath);

                // Update item count in header (skip if we don't have full data yet)
                if (!skipPagination)
                {
                    _view.UpdateItemCount(_filteredFiles.Count);
                }
            }

            // Update pagination and scroll (skip if loading first batch)
            if (!skipPagination)
            {
                _view.UpdatePagination(_currentPage, totalPages);
            }
            _view.ScrollToPage(_currentPage);
        }
    }

    public void SelectFile(string path)
    {
         Debug.Log($"[Controller] Selected: {path}");
         _selectedFile = _currentDirectoryFiles.Find(f => f.Path == path);
         // Unlock detail panel when user explicitly selects an item
         _lockDetailToCurrentFolder = false;
         UpdateDetailView();
    }

    /// <summary>
    /// Clear the selected file state.
    /// Used when entering edit mode to ensure detail panel shows current folder when not hovering.
    /// </summary>
    public void ClearSelectedFile()
    {
        _selectedFile = null;
    }

    public void HoverFile(string path)
    {
         // Unlock detail panel when hovering (allows preview on hover)
         _lockDetailToCurrentFolder = false;
         _hoveredFile = _currentDirectoryFiles.Find(f => f.Path == path);
         UpdateDetailView();
    }

    /// <summary>
    /// Lock/unlock detail panel to current folder info.
    /// Used by view when entering/exiting edit mode or clipboard mode.
    /// </summary>
    public void SetDetailLocked(bool locked)
    {
        _lockDetailToCurrentFolder = locked;
        if (locked)
        {
            _hoveredFile = null;
            _selectedFile = null;
        }
        UpdateDetailView();
    }
    
    public void UnhoverFile(string path)
    {
         if (_hoveredFile.HasValue && _hoveredFile.Value.Path == path)
         {
             _hoveredFile = null;
             UpdateDetailView();
         }
    }
    
    private void UpdateDetailView()
    {
        if (_view == null) return;

        // When locked to current folder, ignore hover and show folder info
        if (_lockDetailToCurrentFolder)
        {
            ShowCurrentFolderInDetail();
            return;
        }

        if (_hoveredFile.HasValue)
        {
            _view.UpdateDetail(_hoveredFile.Value, false);
        }
        else if (_selectedFile.HasValue)
        {
            _view.UpdateDetail(_selectedFile.Value, false);
        }
        else
        {
            ShowCurrentFolderInDetail();
        }
    }

    private void ShowCurrentFolderInDetail()
    {
        if (_view == null) return;

        // Get folder's actual modified date from file system
        // Convert "root" to actual file system path for Directory operations
        string absolutePath = FileSystemService.GetAbsolutePath(_currentPath);
        DateTime folderModified = DateTime.MinValue;
        try
        {
            if (Directory.Exists(absolutePath))
            {
                folderModified = Directory.GetLastWriteTime(absolutePath);
            }
        }
        catch { }

        var folderInfo = new MockFile
        {
            Name = TextEncodingHelper.FixString(System.IO.Path.GetFileName(absolutePath)),
            Path = _currentPath,
            IsFolder = true,
            Modified = folderModified
        };
        if (string.IsNullOrEmpty(folderInfo.Name)) folderInfo.Name = "Root";
        _view.UpdateDetail(folderInfo, true);
    }

    /// <summary>
    /// Called when a side panel navigation item is selected
    /// </summary>
    public void OnSidePanelItemSelected(string id)
    {
        Debug.Log($"[Controller] Side panel item selected: {id}");

        // Cancel any ongoing scan
        if (_scanCoroutine != null)
        {
            StopCoroutine(_scanCoroutine);
            _scanCoroutine = null;
            _isScanning = false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // On Android: Videos and Music trigger full storage scan
        if (id == "videos")
        {
            StartCategoryScan(FileCategory.Video, "Videos", "videos");
            return;
        }
        else if (id == "music")
        {
            StartCategoryScan(FileCategory.Music, "Music", "music");
            return;
        }
#endif

        // Exit filter mode when navigating to a folder
        _isFilterMode = false;
        _filterCategory = FileCategory.Unknown;

        string targetPath = id switch
        {
            "internal" => FileSystemService.RootPath,
            "sdcard" => FileSystemService.GetSDCardPath(),
            "downloads" => FileSystemService.GetDownloadsPath(),
            "videos" => FileSystemService.GetVideosPath(),
            "music" => FileSystemService.GetMusicPath(),
            "recent" => "recent", // Special handling for recent files
            _ => FileSystemService.RootPath
        };

        if (id == "recent")
        {
            // TODO: Load recent files
            Debug.Log("[Controller] Loading recent files (not implemented)");
        }
        else
        {
            NavigateTo(targetPath);
        }
    }

    /// <summary>
    /// Start scanning all files of a category from storage.
    /// </summary>
    private void StartCategoryScan(FileCategory category, string displayName, string sidePanelId)
    {
        Debug.Log($"[Controller] Starting category scan for {category}");

        _isFilterMode = true;
        _filterCategory = category;
        _isScanning = true;

        // Clear current files and show scanning state
        _currentDirectoryFiles.Clear();
        _filteredFiles.Clear();
        _currentPath = $"filter:{category}";
        _currentPage = 1;

        // Update breadcrumb to show filter mode (pass sidePanelId for highlighting)
        _view?.UpdateBreadcrumb(displayName, false, sidePanelId);

        // Clear the file view immediately
        _view?.ClearFileView();

        // Show animated dots indicator
        _view?.ShowScanningIndicator(true, "", 0);

        // Start async scan with incremental updates
        _scanCoroutine = StartCoroutine(FileSystemService.ScanAllFilesByCategory(
            category,
            onProgress: (count) =>
            {
                // Progress is now shown via item count in header, no need to update indicator text
            },
            onFilesFound: (newFiles) =>
            {
                // Add new files incrementally
                _currentDirectoryFiles.AddRange(newFiles);
                _filteredFiles.AddRange(newFiles);

                // Update view with current files and item count
                UpdateView(true);
            },
            onComplete: (results) =>
            {
                _isScanning = false;
                _scanCoroutine = null;

                // Apply sort after scan completes
                _sortBy = "Modified";
                _sortAscending = false;
                ApplySort();

                // Hide animated dots and final update
                _view?.ShowScanningIndicator(false, "", 0);
                _currentPage = 1;
                UpdateView(true);

                Debug.Log($"[Controller] Category scan complete: {results.Count} {category} files");
            }
        ));
    }

    /// <summary>
    /// Check if currently in filter mode (showing all files of a category).
    /// </summary>
    public bool IsFilterMode => _isFilterMode;

    /// <summary>
    /// Check if currently scanning for files.
    /// </summary>
    public bool IsScanning => _isScanning;

    /// <summary>
    /// Get current filter category.
    /// </summary>
    public FileCategory FilterCategory => _filterCategory;

    /// <summary>
    /// Create a new folder in the current directory
    /// </summary>
    public void CreateFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            Debug.LogWarning("[Controller] Cannot create folder: name is empty");
            return;
        }

        // Sanitize folder name
        string sanitizedName = SanitizeFolderName(folderName);
        if (string.IsNullOrEmpty(sanitizedName))
        {
            Debug.LogWarning("[Controller] Cannot create folder: invalid name after sanitization");
            return;
        }

        string newFolderPath = System.IO.Path.Combine(_currentPath, sanitizedName);

        try
        {
            if (Directory.Exists(newFolderPath))
            {
                Debug.LogWarning($"[Controller] Folder already exists: {newFolderPath}");
                // TODO: Show error notification to user
                return;
            }

            Directory.CreateDirectory(newFolderPath);
            Debug.Log($"[Controller] Created folder: {newFolderPath}");

            // Refresh current directory to show new folder
            NavigateTo(_currentPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Controller] Failed to create folder: {e.Message}");
            // TODO: Show error notification to user
        }
    }

    private string SanitizeFolderName(string name)
    {
        // Remove invalid characters for file/folder names
        char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
        string result = name;

        foreach (char c in invalidChars)
        {
            result = result.Replace(c.ToString(), "");
        }

        return result.Trim();
    }

    /// <summary>
    /// Rename a file or folder
    /// </summary>
    public void RenameItem(string sourcePath, string newName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(newName))
        {
            Debug.LogWarning("[Controller] Cannot rename: invalid parameters");
            return;
        }

        // Sanitize new name
        string sanitizedName = SanitizeFolderName(newName);
        if (string.IsNullOrEmpty(sanitizedName))
        {
            Debug.LogWarning("[Controller] Cannot rename: invalid name after sanitization");
            return;
        }

        try
        {
            string directory = System.IO.Path.GetDirectoryName(sourcePath);
            string newPath = System.IO.Path.Combine(directory, sanitizedName);

            // Check if source and destination are the same
            if (sourcePath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[Controller] Rename skipped: same name");
                return;
            }

            // Check if destination already exists
            bool isDirectory = Directory.Exists(sourcePath);
            bool isFile = File.Exists(sourcePath);

            if (!isDirectory && !isFile)
            {
                Debug.LogWarning($"[Controller] Source does not exist: {sourcePath}");
                return;
            }

            if (Directory.Exists(newPath) || File.Exists(newPath))
            {
                Debug.LogWarning($"[Controller] Cannot rename: destination already exists: {newPath}");
                // TODO: Show error notification to user
                return;
            }

            if (isDirectory)
            {
                Directory.Move(sourcePath, newPath);
                Debug.Log($"[Controller] Renamed folder: {sourcePath} -> {newPath}");
            }
            else
            {
                // For files, preserve the extension if user didn't provide one
                string sourceExt = System.IO.Path.GetExtension(sourcePath);
                string newExt = System.IO.Path.GetExtension(sanitizedName);
                if (string.IsNullOrEmpty(newExt) && !string.IsNullOrEmpty(sourceExt))
                {
                    newPath = System.IO.Path.Combine(directory, sanitizedName + sourceExt);
                }

                File.Move(sourcePath, newPath);
                Debug.Log($"[Controller] Renamed file: {sourcePath} -> {newPath}");
            }

            // Refresh current directory to show renamed item
            NavigateTo(_currentPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Controller] Failed to rename: {e.Message}");
            // TODO: Show error notification to user
        }
    }

    /// <summary>
    /// Delete multiple files/folders.
    /// </summary>
    public void DeleteItems(List<string> paths)
    {
        if (paths == null || paths.Count == 0)
        {
            Debug.LogWarning("[Controller] No items to delete");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        foreach (string path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // Delete folder recursively
                    Directory.Delete(path, recursive: true);
                    Debug.Log($"[Controller] Deleted folder: {path}");
                    successCount++;
                }
                else if (File.Exists(path))
                {
                    // Delete file
                    File.Delete(path);
                    Debug.Log($"[Controller] Deleted file: {path}");
                    successCount++;
                }
                else
                {
                    Debug.LogWarning($"[Controller] Item not found: {path}");
                    failCount++;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Controller] Failed to delete {path}: {e.Message}");
                failCount++;
            }
        }

        Debug.Log($"[Controller] Delete completed: {successCount} succeeded, {failCount} failed");

        // Refresh current directory to reflect changes
        if (successCount > 0)
        {
            NavigateTo(_currentPath);
        }
    }

    /// <summary>
    /// Copy multiple files/folders to destination.
    /// </summary>
    public void CopyItems(List<string> sourcePaths, string destination, bool overwrite = false)
    {
        if (sourcePaths == null || sourcePaths.Count == 0)
        {
            Debug.LogWarning("[Controller] No items to copy");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        foreach (string sourcePath in sourcePaths)
        {
            try
            {
                string fileName = Path.GetFileName(sourcePath);
                string destPath = Path.Combine(destination, fileName);

                if (Directory.Exists(sourcePath))
                {
                    // Copy folder recursively
                    CopyDirectoryRecursive(sourcePath, destPath, overwrite);
                    Debug.Log($"[Controller] Copied folder: {sourcePath} -> {destPath}");
                    successCount++;
                }
                else if (File.Exists(sourcePath))
                {
                    // Copy file
                    if (overwrite || !File.Exists(destPath))
                    {
                        File.Copy(sourcePath, destPath, overwrite);
                        Debug.Log($"[Controller] Copied file: {sourcePath} -> {destPath}");
                        successCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[Controller] File already exists: {destPath}");
                        failCount++;
                    }
                }
                else
                {
                    Debug.LogWarning($"[Controller] Source not found: {sourcePath}");
                    failCount++;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Controller] Failed to copy {sourcePath}: {e.Message}");
                failCount++;
            }
        }

        Debug.Log($"[Controller] Copy completed: {successCount} succeeded, {failCount} failed");

        // Refresh current directory to show new items
        if (successCount > 0)
        {
            NavigateTo(_currentPath);
        }
    }

    /// <summary>
    /// Move multiple files/folders to destination.
    /// </summary>
    public void MoveItems(List<string> sourcePaths, string destination, bool overwrite = false)
    {
        if (sourcePaths == null || sourcePaths.Count == 0)
        {
            Debug.LogWarning("[Controller] No items to move");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        foreach (string sourcePath in sourcePaths)
        {
            try
            {
                string fileName = Path.GetFileName(sourcePath);
                string destPath = Path.Combine(destination, fileName);

                // Handle overwrite
                if (overwrite)
                {
                    if (Directory.Exists(destPath))
                    {
                        Directory.Delete(destPath, true);
                    }
                    else if (File.Exists(destPath))
                    {
                        File.Delete(destPath);
                    }
                }

                if (Directory.Exists(sourcePath))
                {
                    // Move folder
                    if (!Directory.Exists(destPath))
                    {
                        Directory.Move(sourcePath, destPath);
                        Debug.Log($"[Controller] Moved folder: {sourcePath} -> {destPath}");
                        successCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[Controller] Destination folder already exists: {destPath}");
                        failCount++;
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    // Move file
                    if (!File.Exists(destPath))
                    {
                        File.Move(sourcePath, destPath);
                        Debug.Log($"[Controller] Moved file: {sourcePath} -> {destPath}");
                        successCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[Controller] Destination file already exists: {destPath}");
                        failCount++;
                    }
                }
                else
                {
                    Debug.LogWarning($"[Controller] Source not found: {sourcePath}");
                    failCount++;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Controller] Failed to move {sourcePath}: {e.Message}");
                failCount++;
            }
        }

        Debug.Log($"[Controller] Move completed: {successCount} succeeded, {failCount} failed");

        // Refresh current directory to reflect changes
        if (successCount > 0)
        {
            NavigateTo(_currentPath);
        }
    }

    /// <summary>
    /// Recursively copy a directory.
    /// </summary>
    private void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite)
    {
        // Create destination directory
        Directory.CreateDirectory(destDir);

        // Copy files
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite);
        }

        // Copy subdirectories
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir, overwrite);
        }
    }

    #region Async File Operations

    /// <summary>
    /// Async copy operation with progress reporting.
    /// Runs on background thread to avoid blocking UI.
    /// </summary>
    public async Task<FileOperationService.FileOperationResult> CopyItemsAsync(
        List<string> sourcePaths,
        string destination,
        bool overwrite,
        IProgress<FileOperationService.FileOperationProgress> progress,
        CancellationToken ct,
        FileOperationService.PauseToken pauseToken = null)
    {
        if (sourcePaths == null || sourcePaths.Count == 0)
        {
            Debug.LogWarning("[Controller] No items to copy");
            return new FileOperationService.FileOperationResult();
        }

        Debug.Log($"[Controller] Starting async copy: {sourcePaths.Count} items to {destination}");

        var result = await FileOperationService.CopyAsync(
            sourcePaths, destination, overwrite, progress, ct, pauseToken);

        Debug.Log($"[Controller] Async copy completed: {result.SuccessCount} succeeded, {result.FailCount} failed, cancelled: {result.WasCancelled}");

        // Refresh UI on main thread after completion
        if (result.SuccessCount > 0 && !result.WasCancelled)
        {
            // Use Unity's main thread
            await Task.Yield(); // Ensure we're back on main thread
            NavigateTo(_currentPath);
        }

        return result;
    }

    /// <summary>
    /// Async move operation with progress reporting.
    /// Runs on background thread to avoid blocking UI.
    /// </summary>
    public async Task<FileOperationService.FileOperationResult> MoveItemsAsync(
        List<string> sourcePaths,
        string destination,
        bool overwrite,
        IProgress<FileOperationService.FileOperationProgress> progress,
        CancellationToken ct,
        FileOperationService.PauseToken pauseToken = null)
    {
        if (sourcePaths == null || sourcePaths.Count == 0)
        {
            Debug.LogWarning("[Controller] No items to move");
            return new FileOperationService.FileOperationResult();
        }

        Debug.Log($"[Controller] Starting async move: {sourcePaths.Count} items to {destination}");

        var result = await FileOperationService.MoveAsync(
            sourcePaths, destination, overwrite, progress, ct, pauseToken);

        Debug.Log($"[Controller] Async move completed: {result.SuccessCount} succeeded, {result.FailCount} failed, cancelled: {result.WasCancelled}");

        // Refresh UI on main thread after completion
        if (result.SuccessCount > 0 && !result.WasCancelled)
        {
            await Task.Yield(); // Ensure we're back on main thread
            NavigateTo(_currentPath);
        }

        return result;
    }

    /// <summary>
    /// Async delete operation with progress reporting.
    /// Runs on background thread to avoid blocking UI.
    /// </summary>
    public async Task<FileOperationService.FileOperationResult> DeleteItemsAsync(
        List<string> paths,
        IProgress<FileOperationService.FileOperationProgress> progress,
        CancellationToken ct,
        FileOperationService.PauseToken pauseToken = null)
    {
        if (paths == null || paths.Count == 0)
        {
            Debug.LogWarning("[Controller] No items to delete");
            return new FileOperationService.FileOperationResult();
        }

        Debug.Log($"[Controller] Starting async delete: {paths.Count} items");

        var result = await FileOperationService.DeleteAsync(paths, progress, ct, pauseToken);

        Debug.Log($"[Controller] Async delete completed: {result.SuccessCount} succeeded, {result.FailCount} failed, cancelled: {result.WasCancelled}");

        // Refresh UI on main thread after completion
        if (result.SuccessCount > 0 && !result.WasCancelled)
        {
            await Task.Yield(); // Ensure we're back on main thread
            NavigateTo(_currentPath);
        }

        return result;
    }

    #endregion

    /// <summary>
    /// Get current path for clipboard operations.
    /// </summary>
    public string GetCurrentPath()
    {
        return _currentPath;
    }

    /// <summary>
    /// Get MockFile info for the current folder.
    /// Used to display current folder info in detail panel (e.g., during edit mode).
    /// </summary>
    public MockFile GetCurrentFolderInfo()
    {
        // Convert "root" to actual file system path for Directory operations
        string absolutePath = FileSystemService.GetAbsolutePath(_currentPath);
        DateTime folderModified = DateTime.MinValue;
        try
        {
            if (Directory.Exists(absolutePath))
            {
                folderModified = Directory.GetLastWriteTime(absolutePath);
            }
        }
        catch { }

        var folderInfo = new MockFile
        {
            Name = TextEncodingHelper.FixString(System.IO.Path.GetFileName(absolutePath)),
            Path = _currentPath,
            IsFolder = true,
            Modified = folderModified
        };
        if (string.IsNullOrEmpty(folderInfo.Name)) folderInfo.Name = "Root";

        return folderInfo;
    }

    /// <summary>
    /// Get MockFile info for a specific file/folder by path.
    /// Returns null if the file doesn't exist or can't be found.
    /// </summary>
    public MockFile? GetFileInfo(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // Try to find in current files list first (faster)
        foreach (var file in _filteredFiles)
        {
            if (file.Path == path)
            {
                return file;
            }
        }

        // If not found in current list, create from path
        try
        {
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return new MockFile
                {
                    Name = TextEncodingHelper.FixString(fileInfo.Name),
                    Path = path,
                    IsFolder = false,
                    Type = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
                    Created = fileInfo.CreationTime,
                    Modified = fileInfo.LastWriteTime,
                    Size = fileInfo.Length
                };
            }
            else if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                return new MockFile
                {
                    Name = TextEncodingHelper.FixString(dirInfo.Name),
                    Path = path,
                    IsFolder = true,
                    Type = "Folder",
                    Created = dirInfo.CreationTime,
                    Modified = dirInfo.LastWriteTime
                };
            }
        }
        catch { }

        return null;
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

public struct MockFile
{
    public string Name;
    public string Path;
    public bool IsFolder;
    public bool IsFolderEmpty;    // For folders: true if no children
    public string Type;           // File extension or "Folder"
    public DateTime Created;
    public DateTime Modified;
    public long Size;             // Bytes
    public TimeSpan Duration;     // For media files

    // Image/Video dimensions
    public int Width;
    public int Height;

    // Video metadata
    public float FrameRate;
    public long DataRate;         // bits per second
    public long TotalBitrate;     // bits per second

    // Music metadata
    public string Artist;
    public string Album;
    public string Genre;
    public string Title;
    public int BitRate;           // kbps
}

public static class FileSystemService
{
    // Root path for file browsing
    private static string _rootPath;

    public static string RootPath
    {
        get
        {
            if (string.IsNullOrEmpty(_rootPath))
            {
                _rootPath = GetPlatformRootPath();
                Debug.Log($"[FileSystemService] Root path set to: {_rootPath}");
            }
            return _rootPath;
        }
    }

    private static string GetPlatformRootPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Android - Get internal storage path via Android API
        try
        {
            using (AndroidJavaClass environment = new AndroidJavaClass("android.os.Environment"))
            {
                // Get external storage directory (shared storage accessible to user)
                using (AndroidJavaObject externalDir = environment.CallStatic<AndroidJavaObject>("getExternalStorageDirectory"))
                {
                    string path = externalDir.Call<string>("getAbsolutePath");
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileSystemService] Failed to get Android storage path: {ex.Message}");
        }

        // Fallback for Android - try common paths
        string[] androidPaths = new string[]
        {
            "/storage/emulated/0",  // Primary internal storage
            "/sdcard",               // Legacy path (symlink)
            Application.persistentDataPath
        };

        foreach (string path in androidPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return Application.persistentDataPath;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Windows - use user's home directory
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // macOS - use user's home directory
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#else
        // Fallback to persistent data path
        return Application.persistentDataPath;
#endif
    }

    public static string GetAbsolutePath(string relativePath)
    {
        if (relativePath == "root" || string.IsNullOrEmpty(relativePath))
        {
            return RootPath;
        }

        // If path starts with "root/", replace with actual root
        if (relativePath.StartsWith("root/"))
        {
            return Path.Combine(RootPath, relativePath.Substring(5));
        }

        // If already absolute path, return as is
        if (Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }

        return Path.Combine(RootPath, relativePath);
    }

    public static List<MockFile> GetFiles(string path)
    {
        var list = new List<MockFile>();
        string absolutePath = GetAbsolutePath(path);

        Debug.Log($"[FileSystemService] Reading directory: {absolutePath}");

        try
        {
            if (!Directory.Exists(absolutePath))
            {
                Debug.LogWarning($"[FileSystemService] Directory not found: {absolutePath}");
                return list;
            }

            // Get directories
            string[] directories = Directory.GetDirectories(absolutePath);
            foreach (string dirPath in directories)
            {
                try
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dirPath);

                    // Skip hidden and system directories
                    if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                        (dirInfo.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    // Skip expensive empty check - will be done lazily if needed
                    list.Add(new MockFile
                    {
                        Name = TextEncodingHelper.FixString(dirInfo.Name),
                        Path = dirPath,
                        IsFolder = true,
                        IsFolderEmpty = false, // Default to not empty, check lazily
                        Type = "Folder",
                        Created = dirInfo.CreationTime,
                        Modified = dirInfo.LastWriteTime,
                        Size = 0,
                        Duration = TimeSpan.Zero
                    });
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we can't access
                    continue;
                }
            }

            // Get files
            string[] files = Directory.GetFiles(absolutePath);

            foreach (string filePath in files)
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(filePath);

                    // Skip hidden and system files
                    if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 ||
                        (fileInfo.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    string extension = fileInfo.Extension.TrimStart('.').ToLower();
                    if (string.IsNullOrEmpty(extension)) extension = "file";

                    string displayName = TextEncodingHelper.FixString(fileInfo.Name);

                    list.Add(new MockFile
                    {
                        Name = displayName,
                        Path = filePath,
                        IsFolder = false,
                        Type = extension,
                        Created = fileInfo.CreationTime,
                        Modified = fileInfo.LastWriteTime,
                        Size = fileInfo.Length,
                        Duration = GetMediaDuration(filePath, extension)
                    });
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FileSystemService] Error reading directory: {ex.Message}");
        }

        Debug.Log($"[FileSystemService] Found {list.Count} items ({list.FindAll(f => f.IsFolder).Count} folders, {list.FindAll(f => !f.IsFolder).Count} files)");
        return list;
    }

    /// <summary>
    /// Async version of GetFiles that yields periodically to prevent UI freezing.
    /// Use this for directories that may contain many items.
    /// </summary>
    /// <param name="path">Directory path to read</param>
    /// <param name="onQuickCount">Optional callback with estimated total count (fires immediately, before enumeration)</param>
    /// <param name="onFirstBatch">Optional callback when first 8 items are ready (for immediate display)</param>
    /// <param name="onProgress">Optional callback for progress updates (items loaded so far)</param>
    /// <param name="onComplete">Callback with the final list of files</param>
    public static System.Collections.IEnumerator GetFilesAsync(string path, Action<int> onQuickCount, Action<List<MockFile>> onFirstBatch, Action<int> onProgress, Action<List<MockFile>> onComplete)
    {
        const int BATCH_SIZE = 50; // Yield every 50 items
        const int FIRST_BATCH_SIZE = 8; // Show first 8 items immediately
        bool firstBatchSent = false;
        var list = new List<MockFile>();
        string absolutePath = GetAbsolutePath(path);
        int processed = 0;
        bool shouldYield = false;

        Debug.Log($"[FileSystemService] Reading directory async: {absolutePath}");

        if (!Directory.Exists(absolutePath))
        {
            Debug.LogWarning($"[FileSystemService] Directory not found: {absolutePath}");
            onQuickCount?.Invoke(0);
            onComplete?.Invoke(list);
            yield break;
        }

        // Get directories
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(absolutePath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FileSystemService] Error getting directories: {ex.Message}");
            onQuickCount?.Invoke(0);
            onComplete?.Invoke(list);
            yield break;
        }

        // Get files array for quick count (very fast - just gets file names, no metadata)
        string[] files;
        try
        {
            files = Directory.GetFiles(absolutePath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FileSystemService] Error getting files: {ex.Message}");
            onQuickCount?.Invoke(directories.Length);
            onComplete?.Invoke(list);
            yield break;
        }

        // === QUICK COUNT: Fire immediately with estimated total ===
        // Note: This is an estimate - actual count may be less due to hidden/system files being filtered
        int estimatedTotal = directories.Length + files.Length;
        Debug.Log($"[FileSystemService] Quick count: {estimatedTotal} items (dirs={directories.Length}, files={files.Length})");
        onQuickCount?.Invoke(estimatedTotal);

        foreach (string dirPath in directories)
        {
            // Process directory in try block, set yield flag outside
            MockFile? dirFile = null;
            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(dirPath);

                // Skip hidden and system directories
                if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                    (dirInfo.Attributes & FileAttributes.System) != 0)
                {
                    continue;
                }

                // Skip expensive empty check - default to not empty
                dirFile = new MockFile
                {
                    Name = TextEncodingHelper.FixString(dirInfo.Name),
                    Path = dirPath,
                    IsFolder = true,
                    IsFolderEmpty = false,
                    Type = "Folder",
                    Created = dirInfo.CreationTime,
                    Modified = dirInfo.LastWriteTime,
                    Size = 0,
                    Duration = TimeSpan.Zero
                };
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // Add and check yield outside try-catch
            if (dirFile.HasValue)
            {
                list.Add(dirFile.Value);
                processed++;

                // Send first batch immediately for fast UI display
                if (!firstBatchSent && list.Count >= FIRST_BATCH_SIZE)
                {
                    firstBatchSent = true;
                    onFirstBatch?.Invoke(new List<MockFile>(list)); // Copy to avoid mutation
                }

                if (processed % BATCH_SIZE == 0)
                {
                    shouldYield = true;
                }
            }

            // Yield outside try-catch block
            if (shouldYield)
            {
                onProgress?.Invoke(list.Count);
                yield return null;
                shouldYield = false;
            }
        }

        // Process files (files array already obtained for quick count above)
        foreach (string filePath in files)
        {
            // Process file in try block
            MockFile? fileItem = null;
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);

                // Skip hidden and system files
                if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 ||
                    (fileInfo.Attributes & FileAttributes.System) != 0)
                {
                    continue;
                }

                string extension = fileInfo.Extension.TrimStart('.').ToLower();
                if (string.IsNullOrEmpty(extension)) extension = "file";

                string displayName = TextEncodingHelper.FixString(fileInfo.Name);

                fileItem = new MockFile
                {
                    Name = displayName,
                    Path = filePath,
                    IsFolder = false,
                    Type = extension,
                    Created = fileInfo.CreationTime,
                    Modified = fileInfo.LastWriteTime,
                    Size = fileInfo.Length,
                    Duration = GetMediaDuration(filePath, extension)
                };
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (Exception)
            {
                continue;
            }

            // Add and check yield outside try-catch
            if (fileItem.HasValue)
            {
                list.Add(fileItem.Value);
                processed++;

                // Send first batch immediately for fast UI display
                if (!firstBatchSent && list.Count >= FIRST_BATCH_SIZE)
                {
                    firstBatchSent = true;
                    onFirstBatch?.Invoke(new List<MockFile>(list)); // Copy to avoid mutation
                }

                if (processed % BATCH_SIZE == 0)
                {
                    shouldYield = true;
                }
            }

            // Yield outside try-catch block
            if (shouldYield)
            {
                onProgress?.Invoke(list.Count);
                yield return null;
                shouldYield = false;
            }
        }

        // If we finished but never sent first batch (less than 8 items), send what we have
        if (!firstBatchSent && list.Count > 0)
        {
            onFirstBatch?.Invoke(new List<MockFile>(list));
        }

        Debug.Log($"[FileSystemService] Found {list.Count} items async ({list.FindAll(f => f.IsFolder).Count} folders, {list.FindAll(f => !f.IsFolder).Count} files)");
        onComplete?.Invoke(list);
    }

    private static TimeSpan GetMediaDuration(string filePath, string extension)
    {
        // Media duration would require additional libraries
        // For now, return zero - can be extended later with NAudio, FFmpeg, etc.
        return TimeSpan.Zero;
    }

    public static string GetParentPath(string path)
    {
        string absolutePath = GetAbsolutePath(path);

        // Don't go above root
        if (absolutePath == RootPath || string.IsNullOrEmpty(absolutePath))
        {
            return "root";
        }

        string parentPath = Directory.GetParent(absolutePath)?.FullName;

        if (string.IsNullOrEmpty(parentPath) || parentPath == RootPath)
        {
            return "root";
        }

        return parentPath;
    }

    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    public static string GetSDCardPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Try common SD card paths on Android
        string[] sdcardPaths = new string[]
        {
            "/storage/sdcard1",
            "/storage/extSdCard",
            "/storage/external_SD"
        };

        foreach (string path in sdcardPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
#endif
        // Fallback to root path (no SD card)
        return RootPath;
    }

    public static string GetDownloadsPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string downloadsPath = Path.Combine(RootPath, "Download");
        if (Directory.Exists(downloadsPath))
        {
            return downloadsPath;
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string downloadsPath = Path.Combine(RootPath, "Downloads");
        if (Directory.Exists(downloadsPath))
        {
            return downloadsPath;
        }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        string downloadsPath = Path.Combine(RootPath, "Downloads");
        if (Directory.Exists(downloadsPath))
        {
            return downloadsPath;
        }
#endif
        return RootPath;
    }

    public static string GetVideosPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Try DCIM and Movies folders
        string dcimPath = Path.Combine(RootPath, "DCIM");
        string moviesPath = Path.Combine(RootPath, "Movies");

        if (Directory.Exists(moviesPath))
        {
            return moviesPath;
        }
        if (Directory.Exists(dcimPath))
        {
            return dcimPath;
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (!string.IsNullOrEmpty(videosPath) && Directory.Exists(videosPath))
        {
            return videosPath;
        }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        string moviesPath = Path.Combine(RootPath, "Movies");
        if (Directory.Exists(moviesPath))
        {
            return moviesPath;
        }
#endif
        return RootPath;
    }

    public static string GetMusicPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string musicPath = Path.Combine(RootPath, "Music");
        if (Directory.Exists(musicPath))
        {
            return musicPath;
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (!string.IsNullOrEmpty(musicPath) && Directory.Exists(musicPath))
        {
            return musicPath;
        }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        string musicPath = Path.Combine(RootPath, "Music");
        if (Directory.Exists(musicPath))
        {
            return musicPath;
        }
#endif
        return RootPath;
    }

    #region Recursive Category Scan

    /// <summary>
    /// Scan all files of a specific category recursively from root storage.
    /// This is an async operation that yields periodically to prevent freezing.
    /// </summary>
    /// <param name="category">File category to filter (Video, Music, Image)</param>
    /// <param name="onProgress">Progress callback (filesFound count)</param>
    /// <param name="onFilesFound">Incremental callback when new files are found (for real-time UI update)</param>
    /// <param name="onComplete">Completion callback with all results</param>
    public static System.Collections.IEnumerator ScanAllFilesByCategory(
        FileCategory category,
        Action<int> onProgress,
        Action<List<MockFile>> onFilesFound,
        Action<List<MockFile>> onComplete)
    {
        var results = new List<MockFile>();
        var extensions = FileCategoryHelper.GetExtensionsForCategory(category);

        if (extensions.Count == 0)
        {
            onComplete?.Invoke(results);
            yield break;
        }

        // Folders to scan (start from root and common media locations)
        var foldersToScan = new Queue<string>();
        var scannedFolders = new HashSet<string>();

        // Add root path
        foldersToScan.Enqueue(RootPath);

        // Add SD card if available
        string sdCardPath = GetSDCardPath();
        if (sdCardPath != RootPath && Directory.Exists(sdCardPath))
        {
            foldersToScan.Enqueue(sdCardPath);
        }

        int totalFoldersScanned = 0;
        int filesFound = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var batchFiles = new List<MockFile>(); // Batch for incremental updates

        // Folders to skip (system folders, hidden folders, etc.)
        var skipFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Android", ".android", ".thumbnails", ".cache",
            "lost+found", "System Volume Information", "$RECYCLE.BIN"
        };

        while (foldersToScan.Count > 0)
        {
            string currentFolder = foldersToScan.Dequeue();

            // Skip if already scanned (avoid loops from symlinks)
            if (scannedFolders.Contains(currentFolder))
                continue;

            scannedFolders.Add(currentFolder);
            totalFoldersScanned++;

            // Yield and report progress every 50 folders or 100ms
            if (totalFoldersScanned % 50 == 0 || sw.ElapsedMilliseconds > 100)
            {
                sw.Restart();
                onProgress?.Invoke(filesFound);

                // Send batch of found files for incremental UI update
                if (batchFiles.Count > 0)
                {
                    onFilesFound?.Invoke(new List<MockFile>(batchFiles));
                    batchFiles.Clear();
                }

                yield return null;
            }

            try
            {
                // Get files in current folder
                string[] files;
                try
                {
                    files = Directory.GetFiles(currentFolder);
                }
                catch { files = new string[0]; }

                foreach (string filePath in files)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(filePath);

                        // Skip hidden/system files
                        if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 ||
                            (fileInfo.Attributes & FileAttributes.System) != 0)
                            continue;

                        string ext = fileInfo.Extension.TrimStart('.').ToLower();

                        // Check if extension matches category
                        if (extensions.Contains(ext))
                        {
                            var file = new MockFile
                            {
                                Name = TextEncodingHelper.FixString(fileInfo.Name),
                                Path = filePath,
                                IsFolder = false,
                                Type = ext,
                                Created = fileInfo.CreationTime,
                                Modified = fileInfo.LastWriteTime,
                                Size = fileInfo.Length,
                                Duration = TimeSpan.Zero
                            };
                            results.Add(file);
                            batchFiles.Add(file);
                            filesFound++;
                        }
                    }
                    catch { /* Skip inaccessible files */ }
                }

                // Add subdirectories to scan queue
                string[] subdirs;
                try
                {
                    subdirs = Directory.GetDirectories(currentFolder);
                }
                catch { subdirs = new string[0]; }

                foreach (string subdir in subdirs)
                {
                    try
                    {
                        DirectoryInfo dirInfo = new DirectoryInfo(subdir);

                        // Skip hidden/system directories
                        if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                            (dirInfo.Attributes & FileAttributes.System) != 0)
                            continue;

                        // Skip known system folders
                        if (skipFolders.Contains(dirInfo.Name))
                            continue;

                        // Skip folders starting with '.'
                        if (dirInfo.Name.StartsWith("."))
                            continue;

                        foldersToScan.Enqueue(subdir);
                    }
                    catch { /* Skip inaccessible directories */ }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we can't access
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileSystemService] Error scanning {currentFolder}: {ex.Message}");
            }
        }

        // Send any remaining batched files
        if (batchFiles.Count > 0)
        {
            onFilesFound?.Invoke(new List<MockFile>(batchFiles));
            batchFiles.Clear();
        }

        Debug.Log($"[FileSystemService] Category scan complete: {filesFound} {category} files found in {totalFoldersScanned} folders");
        onProgress?.Invoke(filesFound);
        onComplete?.Invoke(results);
    }

    #endregion

    /// <summary>
    /// Check if folder creation is allowed in the given path.
    /// Returns false for:
    /// - Virtual paths like "root" (can't create in device root listing)
    /// - Device root path (Internal Storage root - for cleanliness)
    /// - Paths that don't exist
    /// - Paths without write permission
    /// </summary>
    public static bool CanCreateFolderInPath(string path)
    {
        Debug.Log($"[FileSystemService] CanCreateFolderInPath called with path: '{path}'");

        // Can't create folders in virtual "root" path (device listing)
        if (path == "root" || string.IsNullOrEmpty(path))
        {
            Debug.Log($"[FileSystemService] Path is 'root' or empty, returning false");
            return false;
        }

        string absolutePath = GetAbsolutePath(path);
        Debug.Log($"[FileSystemService] absolutePath: '{absolutePath}'");

        // Can't create folders directly in device root (Internal Storage)
        // Normalize paths for comparison (remove trailing slashes)
        string normalizedAbsolute = absolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedRoot = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Debug.Log($"[FileSystemService] Comparing normalizedAbsolute: '{normalizedAbsolute}' with normalizedRoot: '{normalizedRoot}'");

        bool isDeviceRoot = string.Equals(normalizedAbsolute, normalizedRoot, StringComparison.OrdinalIgnoreCase);
        Debug.Log($"[FileSystemService] isDeviceRoot: {isDeviceRoot}");

        if (isDeviceRoot)
        {
            Debug.Log($"[FileSystemService] Folder creation not allowed in device root: {absolutePath}");
            return false;
        }

        // Path must exist
        if (!Directory.Exists(absolutePath))
        {
            Debug.Log($"[FileSystemService] Path does not exist: {absolutePath}");
            return false;
        }

        // Check write permission by attempting to create a temp directory
        try
        {
            string testPath = Path.Combine(absolutePath, ".vrworkspace_write_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(testPath);
            Directory.Delete(testPath);
            Debug.Log($"[FileSystemService] Write permission OK for: {absolutePath}");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Debug.Log($"[FileSystemService] No write permission for: {absolutePath}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.Log($"[FileSystemService] Cannot write to {absolutePath}: {ex.Message}");
            return false;
        }
    }

    #region Text File Reading with Encoding Detection

    /// <summary>
    /// Read text file content with automatic encoding detection and mojibake fixing.
    /// </summary>
    /// <param name="path">File path (can be relative or absolute)</param>
    /// <returns>Properly decoded text content</returns>
    public static string ReadTextFileContent(string path)
    {
        string absolutePath = GetAbsolutePath(path);
        return TextEncodingHelper.ReadTextFile(absolutePath);
    }

    /// <summary>
    /// Read text file content with size limit and automatic encoding detection.
    /// Useful for previewing large files.
    /// </summary>
    /// <param name="path">File path (can be relative or absolute)</param>
    /// <param name="maxBytes">Maximum bytes to read (0 = no limit)</param>
    /// <returns>Properly decoded text content</returns>
    public static string ReadTextFileContentWithLimit(string path, int maxBytes = 10240)
    {
        string absolutePath = GetAbsolutePath(path);
        return TextEncodingHelper.ReadTextFileWithLimit(absolutePath, maxBytes);
    }

    /// <summary>
    /// Get the detected encoding of a text file.
    /// </summary>
    public static string GetFileEncoding(string path)
    {
        string absolutePath = GetAbsolutePath(path);
        return TextEncodingHelper.GetEncodingName(absolutePath);
    }

    /// <summary>
    /// Check if a file is likely a text file based on extension.
    /// </summary>
    public static bool IsTextFile(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;

        string ext = extension.TrimStart('.').ToLower();
        string[] textExtensions = {
            "txt", "md", "markdown", "json", "xml", "html", "htm", "css", "js",
            "ts", "cs", "java", "py", "rb", "php", "c", "cpp", "h", "hpp",
            "yaml", "yml", "ini", "cfg", "conf", "log", "sh", "bat", "ps1",
            "sql", "csv", "tsv", "rtf", "tex", "rst", "org", "wiki",
            "gradle", "properties", "gitignore", "dockerignore", "editorconfig"
        };

        return Array.Exists(textExtensions, e => e == ext);
    }

    #endregion
}
