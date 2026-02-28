using UnityEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibraryController partial: Sorting, grouping, pagination, and view refresh.
    /// </summary>
    public partial class RTTMediaLibraryController
    {
        /// <summary>
        /// Set sort field.
        /// </summary>
        public void SetSortBy(string sortBy)
        {
            if (SortBy != sortBy)
            {
                SortBy = sortBy;
                ApplySort();
                CurrentPage = 1;
                RefreshView();
                // Debug.Log($"[RTTMediaLibraryController] Sort by: {SortBy}");
            }
        }

        /// <summary>
        /// Set ascending/descending order.
        /// </summary>
        public void SetAscending(bool ascending)
        {
            if (IsAscending != ascending)
            {
                IsAscending = ascending;
                ApplySort();
                CurrentPage = 1;
                RefreshView();
                // Debug.Log($"[RTTMediaLibraryController] Ascending: {IsAscending}");
            }
        }

        /// <summary>
        /// Set group by field.
        /// </summary>
        public void SetGroupBy(string groupBy)
        {
            if (GroupBy != groupBy)
            {
                GroupBy = groupBy;
                ApplyGrouping();
                CurrentPage = 1;
                RefreshView();
                // Debug.Log($"[RTTMediaLibraryController] Group by: {GroupBy}");
            }
        }

        private void RecalculatePagination()
        {
            // Get TotalPages from grid which now uses group-aware pagination
            if (_view?.Grid != null)
            {
                TotalPages = _view.Grid.TotalPages;
            }
            else
            {
                // Fallback to simple calculation if grid not available
                TotalPages = Mathf.Max(1, Mathf.CeilToInt((float)_filteredVideos.Count / PageSize));
            }
            CurrentPage = Mathf.Clamp(CurrentPage, 1, TotalPages);
        }

        private void ApplySort()
        {
            // Use in-place sort to avoid creating new lists (better GC performance)
            int direction = IsAscending ? 1 : -1;

            switch (SortBy.ToLower())
            {
                case "name":
                    // Use Vietnamese comparer for proper diacritics sorting (synced with RTTFileManagerController)
                    _filteredVideos.Sort((a, b) => direction * VietnameseComparer.Compare(a.Title, b.Title));
                    break;
                case "type":
                    // Use Vietnamese comparer for type names as well
                    _filteredVideos.Sort((a, b) => direction * VietnameseComparer.Compare(
                        System.IO.Path.GetExtension(a.Path),
                        System.IO.Path.GetExtension(b.Path)));
                    break;
                case "created":
                    _filteredVideos.Sort((a, b) => direction * a.DateAdded.CompareTo(b.DateAdded));
                    break;
                case "modified":
                    _filteredVideos.Sort((a, b) => direction * a.DateModified.CompareTo(b.DateModified));
                    break;
                case "duration":
                    _filteredVideos.Sort((a, b) => direction * a.Duration.CompareTo(b.Duration));
                    break;
                case "size":
                    _filteredVideos.Sort((a, b) => direction * a.FileSizeBytes.CompareTo(b.FileSizeBytes));
                    break;
            }
        }

        /// <summary>
        /// Apply grouping to cluster items by selected field.
        /// This re-sorts items to group them visually.
        /// </summary>
        private void ApplyGrouping()
        {
            if (string.IsNullOrEmpty(GroupBy))
            {
                GroupBy = "Date Added";  // Default
            }

            // Group by sorting to cluster items visually
            switch (GroupBy.ToLower().Replace(" ", ""))
            {
                case "dateadded":
                    // Group by date, most recent first
                    _filteredVideos.Sort((a, b) =>
                    {
                        int dateCompare = b.DateAdded.Date.CompareTo(a.DateAdded.Date);
                        if (dateCompare != 0) return dateCompare;
                        return VietnameseComparer.Compare(a.Title, b.Title);
                    });
                    break;

                case "duration":
                    // Group by duration category (Short <5m, Medium 5-20m, Long 20-60m, Extended >60m)
                    _filteredVideos.Sort((a, b) =>
                    {
                        int catA = GetDurationCategory(a.Duration);
                        int catB = GetDurationCategory(b.Duration);
                        int catCompare = catA.CompareTo(catB);
                        if (catCompare != 0) return catCompare;
                        return a.Duration.CompareTo(b.Duration);
                    });
                    break;

                case "resolution":
                    // Group by resolution (4K, 1440p, 1080p, 720p, SD)
                    _filteredVideos.Sort((a, b) =>
                    {
                        int resA = GetResolutionCategory(a.Height);
                        int resB = GetResolutionCategory(b.Height);
                        int resCompare = resB.CompareTo(resA);  // Higher resolution first
                        if (resCompare != 0) return resCompare;
                        return VietnameseComparer.Compare(a.Title, b.Title);
                    });
                    break;

                case "format":
                    // Group by video format/extension
                    _filteredVideos.Sort((a, b) =>
                    {
                        string extA = System.IO.Path.GetExtension(a.Path)?.ToLower() ?? "";
                        string extB = System.IO.Path.GetExtension(b.Path)?.ToLower() ?? "";
                        int extCompare = extA.CompareTo(extB);
                        if (extCompare != 0) return extCompare;
                        return VietnameseComparer.Compare(a.Title, b.Title);
                    });
                    break;
            }

            // Generate group info for display
            _groups = MediaGroupHelper.CreateGroups(_filteredVideos, GroupBy);
            // Debug.Log($"[RTTMediaLibraryController] Created {_groups.Count} groups for {_filteredVideos.Count} items");
        }

        private int GetDurationCategory(TimeSpan duration)
        {
            double minutes = duration.TotalMinutes;
            if (minutes < 5) return 0;       // Short
            if (minutes < 20) return 1;      // Medium
            if (minutes < 60) return 2;      // Long
            return 3;                         // Extended
        }

        private int GetResolutionCategory(int height)
        {
            if (height >= 2160) return 4;    // 4K
            if (height >= 1440) return 3;    // 1440p
            if (height >= 1080) return 2;    // 1080p
            if (height >= 720) return 1;     // 720p
            return 0;                         // SD
        }

        private void RefreshView()
        {
            _view?.SetVideos(_filteredVideos, _groups);
            // Update pagination after SetVideos (grid computes TotalPages)
            RecalculatePagination();
            _view?.UpdatePagination();
        }

        /// <summary>
        /// Set page size (items per page).
        /// </summary>
        public void SetPageSize(int size)
        {
            // Emergency fix: if something is trying to set 6 (old design), force it to 8
            if (size == 6)
            {
                Debug.LogWarning("[RTTMediaLibraryController] Something attempted to set PageSize to 6 (old design). Forcing to 8.");
                size = 8;
            }

            if (PageSize == size) return;
            PageSize = Mathf.Max(1, size);
            RecalculatePagination();
            // Debug.Log($"[RTTMediaLibraryController] PageSize set to {PageSize}");
        }

        /// <summary>
        /// Change page by delta (-1 for prev, +1 for next).
        /// </summary>
        public void ChangePage(int delta)
        {
            GoToPage(CurrentPage + delta);
        }

        /// <summary>
        /// Go to specific page number.
        /// </summary>
        public void GoToPage(int pageNumber)
        {
            int newPage = Mathf.Clamp(pageNumber, 1, TotalPages);
            if (newPage != CurrentPage)
            {
                CurrentPage = newPage;
                // Forward to grid to actually scroll
                _view?.Grid?.GoToPage(newPage);
                // Debug.Log($"[RTTMediaLibraryController] Page changed to {CurrentPage}/{TotalPages}");
            }
        }

        /// <summary>
        /// Scroll to first page.
        /// </summary>
        public void ScrollToStart()
        {
            GoToPage(1);
        }

        /// <summary>
        /// Scroll to last page.
        /// </summary>
        public void ScrollToEnd()
        {
            GoToPage(TotalPages);
        }
    }
}
