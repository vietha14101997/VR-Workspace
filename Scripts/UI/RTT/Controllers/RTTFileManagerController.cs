using UnityEngine;
using System;
using System.Collections.Generic;
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
    private int _pageSize = 10; // 2 Rows x 5 Cols (10 items) - Matches RTTFileManager ScrollToPage(2)

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

    private List<MockFile> SortList(List<MockFile> list)
    {
        switch (_sortBy)
        {
            case "Name":
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                break;
            case "Type":
                list.Sort((a, b) => string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase));
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
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
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
        return Mathf.CeilToInt((float)totalFiles / size);
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
            var folderInfo = new MockFile { Name = System.IO.Path.GetFileName(_currentPath), Path = _currentPath, IsFolder = true };
            if (string.IsNullOrEmpty(folderInfo.Name)) folderInfo.Name = "Root";
            _view.UpdateDetail(folderInfo, true);
        }
    }
    #endregion
}

public struct MockFile
{
    public string Name;
    public string Path;
    public bool IsFolder;
    public string Type;           // File extension or "Folder"
    public DateTime Created;
    public DateTime Modified;
    public long Size;             // Bytes
    public TimeSpan Duration;     // For media files
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

                    list.Add(new MockFile
                    {
                        Name = dirInfo.Name,
                        Path = dirPath,
                        IsFolder = true,
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
}
