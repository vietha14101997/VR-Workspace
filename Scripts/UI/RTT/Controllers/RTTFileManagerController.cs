using UnityEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// Controller for the File Manager app.
/// Handles business logic, data fetching, and state management.
/// Follows MVVM pattern where this controls the RTTFileManager View.
/// </summary>
public class RTTFileManagerController : MonoBehaviour
{
    #region Private Fields
    private RTTFileManager _view;
    private GameObject _viewObject;
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
    private int _pageSize = 10; // Default: ~2 rows x 5 cols for grid view (will be recalculated by view)

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
        Debug.Log("[Controller] View is ready. Loading initial path.");
        NavigateTo("root");
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

    private void NavigateToInternal(string path, bool restorePage, string clickedFolderPath)
    {
        Debug.Log($"[Controller] NavigateToInternal: path='{path}', restorePage={restorePage}, clickedFolder='{clickedFolderPath}'");
        Debug.Log($"[Controller] Current state: _currentPath='{_currentPath}', _currentPage={_currentPage}, _pageSize={_pageSize}");

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
            // This gives more accurate position when navigating back
            if (!string.IsNullOrEmpty(clickedFolderPath))
            {
                int folderIndex = _filteredFiles.FindIndex(f => f.Path == clickedFolderPath);
                itemIndexToSave = folderIndex >= 0 ? folderIndex : (_currentPage - 1) * _pageSize;
                Debug.Log($"[Controller] Clicked folder index in list: {folderIndex}");
            }
            else
            {
                // Default: use first item of current page
                itemIndexToSave = (_currentPage - 1) * _pageSize;
            }

            // Use normalized path as key for consistent save/restore
            _itemIndexHistory[normalizedCurrentPath] = itemIndexToSave;
            Debug.Log($"[Controller] SAVED: _itemIndexHistory['{normalizedCurrentPath}'] = {itemIndexToSave}");
        }
        else if (normalizedCurrentPath == normalizedDestPath)
        {
            Debug.Log($"[Controller] Same path (normalized), skipping save");
        }

        _currentPath = path;
        _currentSearchQuery = ""; // Reset search state

        // Load all files for this path
        _currentDirectoryFiles = FileSystemService.GetFiles(path);

        // Reset filter to show all
        _filteredFiles = new List<MockFile>(_currentDirectoryFiles);

        // Apply current sort
        ApplySort();

        // Restore page from item index or reset to page 1
        // Use normalized path as key for consistent save/restore
        if (restorePage && _itemIndexHistory.TryGetValue(normalizedDestPath, out int savedItemIndex))
        {
            // Calculate page from saved item index using current pageSize
            // page = (itemIndex / pageSize) + 1
            int calculatedPage = (savedItemIndex / Mathf.Max(1, _pageSize)) + 1;
            int totalPages = CalculateTotalPages();
            _currentPage = Mathf.Clamp(calculatedPage, 1, totalPages);
            Debug.Log($"[Controller] RESTORED: page={_currentPage} from _itemIndexHistory['{normalizedDestPath}']={savedItemIndex}, pageSize={_pageSize}, total={totalPages}");
        }
        else
        {
            _currentPage = 1;
            if (restorePage)
            {
                Debug.Log($"[Controller] NO HISTORY for '{normalizedDestPath}' (path='{path}'), reset to page 1. History keys: [{string.Join(", ", _itemIndexHistory.Keys)}]");
            }
            else
            {
                Debug.Log($"[Controller] restorePage=false, reset to page 1");
            }
        }

        // Reset selection/hover on nav
        _selectedFile = null;
        _hoveredFile = null;

        UpdateView(true); // true = full reload
        UpdateDetailView(); // Update detail to show current folder
        _view.UpdateBreadcrumbs(_currentPath);
    }

    public void RefreshCurrentFolder()
    {
        Debug.Log("[Controller] Refreshing current folder...");
        // Reload files (Mock logic: just re-fetch)
        _currentDirectoryFiles = FileSystemService.GetFiles(_currentPath);
        SearchFiles(_currentSearchQuery); // Re-apply search/filter
    }

    /// <summary>
    /// Refresh current folder while keeping the current page position.
    /// Used when switching between Grid/List view.
    /// </summary>
    public void RefreshCurrentFolderKeepPage()
    {
        Debug.Log("[Controller] Refreshing current folder (keep page)...");
        // Reload files
        _currentDirectoryFiles = FileSystemService.GetFiles(_currentPath);

        // Re-apply filter without resetting page
        if (string.IsNullOrEmpty(_currentSearchQuery))
        {
            _filteredFiles = new List<MockFile>(_currentDirectoryFiles);
        }
        else
        {
            _filteredFiles = _currentDirectoryFiles.FindAll(f => f.Name.IndexOf(_currentSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        ApplySort();

        // Clamp page to valid range (in case folder content changed)
        int totalPages = CalculateTotalPages();
        _currentPage = Mathf.Clamp(_currentPage, 1, totalPages);

        UpdateView(true); // Full reload to update Grid/List
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
            Debug.Log($"[Controller] SetPageSize: no change (already {_pageSize})");
            return;
        }

        // Calculate current item index before changing page size
        int currentItemIndex = (_currentPage - 1) * _pageSize;

        Debug.Log($"[Controller] Page size changed: {_pageSize} -> {itemsPerPage}, currentItemIndex: {currentItemIndex}");
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
        if (_view != null)
        {
            _view.UpdatePagination(_currentPage, CalculateTotalPages());
            _view.SetScrollPosition(1f); // 1 = top
        }
    }

    /// <summary>
    /// Scroll to the very end (bottom) of the content.
    /// </summary>
    public void ScrollToEnd()
    {
        int totalPages = CalculateTotalPages();
        _currentPage = totalPages;
        if (_view != null)
        {
            _view.UpdatePagination(_currentPage, totalPages);
            _view.SetScrollPosition(0f); // 0 = bottom
        }
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

    private void UpdateView(bool fullReload)
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

                // Update item count in header
                _view.UpdateItemCount(_filteredFiles.Count);
            }

            _view.UpdatePagination(_currentPage, totalPages);
            _view.ScrollToPage(_currentPage);
        }
    }

    public void SelectFile(string path)
    {
         Debug.Log($"[Controller] Selected: {path}");
         _selectedFile = _currentDirectoryFiles.Find(f => f.Path == path);
         UpdateDetailView();
    }
    
    public void HoverFile(string path)
    {
         _hoveredFile = _currentDirectoryFiles.Find(f => f.Path == path);
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
            // Get folder's actual modified date from file system
            DateTime folderModified = DateTime.MinValue;
            try
            {
                if (Directory.Exists(_currentPath))
                {
                    folderModified = Directory.GetLastWriteTime(_currentPath);
                }
            }
            catch { }

            var folderInfo = new MockFile
            {
                Name = System.IO.Path.GetFileName(_currentPath),
                Path = _currentPath,
                IsFolder = true,
                Modified = folderModified
            };
            if (string.IsNullOrEmpty(folderInfo.Name)) folderInfo.Name = "Root";
            _view.UpdateDetail(folderInfo, true);
        }
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
            StartCategoryScan(FileCategory.Video, "All Videos");
            return;
        }
        else if (id == "music")
        {
            StartCategoryScan(FileCategory.Music, "All Music");
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
    private void StartCategoryScan(FileCategory category, string displayName)
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

        // Update breadcrumb to show filter mode
        _view?.UpdateBreadcrumb(displayName, false);

        // Show loading indicator
        _view?.ShowScanningIndicator(true, $"Scanning {displayName}...", 0);

        // Start async scan
        _scanCoroutine = StartCoroutine(FileSystemService.ScanAllFilesByCategory(
            category,
            onProgress: (progress, count) =>
            {
                // Update progress UI
                _view?.ShowScanningIndicator(true, $"Found {count} files...", progress);
            },
            onComplete: (results) =>
            {
                _isScanning = false;
                _scanCoroutine = null;

                // Store results
                _currentDirectoryFiles = results;
                _filteredFiles = new List<MockFile>(results);

                // Apply default sort (by modified date, newest first for media)
                _sortBy = "Modified";
                _sortAscending = false;
                ApplySort();

                // Hide loading and update view
                _view?.ShowScanningIndicator(false, "", 1f);
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

    /// <summary>
    /// Get current path for clipboard operations.
    /// </summary>
    public string GetCurrentPath()
    {
        return _currentPath;
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

                    // Check if folder is empty (has no visible children)
                    bool isFolderEmpty = true;
                    try
                    {
                        var entries = dirInfo.EnumerateFileSystemInfos();
                        foreach (var entry in entries)
                        {
                            // Skip hidden/system entries
                            if ((entry.Attributes & FileAttributes.Hidden) == 0 &&
                                (entry.Attributes & FileAttributes.System) == 0)
                            {
                                isFolderEmpty = false;
                                break;
                            }
                        }
                    }
                    catch { isFolderEmpty = true; } // If can't enumerate, treat as empty

                    list.Add(new MockFile
                    {
                        Name = dirInfo.Name,
                        Path = dirPath,
                        IsFolder = true,
                        IsFolderEmpty = isFolderEmpty,
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

                    list.Add(new MockFile
                    {
                        Name = fileInfo.Name,
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
                    // Skip files we can't access
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
    /// <param name="onProgress">Progress callback (0-1)</param>
    /// <param name="onComplete">Completion callback with results</param>
    public static System.Collections.IEnumerator ScanAllFilesByCategory(
        FileCategory category,
        Action<float, int> onProgress,
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

            // Yield every 50 folders or 100ms to prevent freezing
            if (totalFoldersScanned % 50 == 0 || sw.ElapsedMilliseconds > 100)
            {
                sw.Restart();
                onProgress?.Invoke(-1f, filesFound); // -1 means indeterminate progress
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
                            results.Add(new MockFile
                            {
                                Name = fileInfo.Name,
                                Path = filePath,
                                IsFolder = false,
                                Type = ext,
                                Created = fileInfo.CreationTime,
                                Modified = fileInfo.LastWriteTime,
                                Size = fileInfo.Length,
                                Duration = TimeSpan.Zero
                            });
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

        Debug.Log($"[FileSystemService] Category scan complete: {filesFound} {category} files found in {totalFoldersScanned} folders");
        onProgress?.Invoke(1f, filesFound);
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
}
