using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Services;
using VRWorkspace.Utilities;

namespace VRWorkspace.UI.RTT.Controllers
{
    public partial class RTTFileManagerController
    {
        // Vietnamese culture for proper diacritics sorting (Đ with D, etc.)
        private static readonly CultureInfo VietnameseCulture = new CultureInfo("vi-VN");
        private static readonly StringComparer VietnameseComparer = StringComparer.Create(VietnameseCulture, ignoreCase: true);

        private static readonly HashSet<string> _videoExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "mkv", "avi", "webm", "mov", "wmv", "m4v", "flv"
        };

        public List<string> GetVideoFilePaths()
        {
            var paths = new List<string>();
            foreach (var file in _filteredFiles)
            {
                if (!file.IsFolder && _videoExtensions.Contains(file.Type))
                    paths.Add(file.Path);
            }
            return paths;
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
                if (System.IO.Directory.Exists(absolutePath))
                {
                    folderModified = System.IO.Directory.GetLastWriteTime(absolutePath);
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
    }
}
