using UnityEngine;
using System;
using System.Collections.Generic;

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
        _currentDirectoryFiles = MockDataService.GetFiles(path);
        
        // Reset filter to show all
        _filteredFiles = new List<MockFile>(_currentDirectoryFiles);
        
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
        _currentDirectoryFiles = MockDataService.GetFiles(_currentPath);
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
        
        _currentPage = 1;
        UpdateView(true);
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
}

public static class MockDataService
{
    public static List<MockFile> GetFiles(string path)
    {
        var list = new List<MockFile>();
        list.Add(new MockFile { Name = "DCIM", Path = path + "/DCIM", IsFolder = true });
        list.Add(new MockFile { Name = "Documents", Path = path + "/Documents", IsFolder = true });
        list.Add(new MockFile { Name = "Download", Path = path + "/Download", IsFolder = true });
        list.Add(new MockFile { Name = "Music", Path = path + "/Music", IsFolder = true });

        for (int i = 1; i <= 150; i++)
        {
            list.Add(new MockFile { Name = $"Image_{i:00}.jpg", Path = path + $"/Image_{i:00}.jpg", IsFolder = false });
        }
        return list;
    }
}
