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
        Debug.Log($"[Controller] Navigating to: {path}");
        _currentPath = path;
        _currentSearchQuery = ""; // Reset search state

        // Load all files for this path
        _currentDirectoryFiles = FileSystemService.GetFiles(path);

        // Reset filter to show all
        _filteredFiles = new List<MockFile>(_currentDirectoryFiles);

        // Apply current sort
        ApplySort();

        // Reset to page 1
        _currentPage = 1;

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

        Debug.Log($"[Controller] Page size changed: {_pageSize} -> {itemsPerPage}");
        _pageSize = Mathf.Max(1, itemsPerPage);
        _currentPage = 1;
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
}
