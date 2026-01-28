using System.Collections.Generic;

/// <summary>
/// Represents a single page in the paginated media grid.
/// Pre-calculated for efficient page navigation with group awareness.
/// </summary>
public class PageLayoutInfo
{
    /// <summary>
    /// 1-based page number.
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Y scroll position where this page starts.
    /// </summary>
    public float StartY { get; set; }

    /// <summary>
    /// Total visual height of content on this page.
    /// </summary>
    public float TotalHeight { get; set; }

    /// <summary>
    /// Group slices visible on this page.
    /// A slice represents a portion of a group (or the entire group if it fits).
    /// </summary>
    public List<PageGroupSlice> GroupSlices { get; set; } = new List<PageGroupSlice>();

    /// <summary>
    /// First item global index on this page.
    /// </summary>
    public int FirstItemIndex { get; set; }

    /// <summary>
    /// Last item global index on this page.
    /// </summary>
    public int LastItemIndex { get; set; }

    /// <summary>
    /// Total number of items on this page.
    /// </summary>
    public int ItemCount => LastItemIndex - FirstItemIndex + 1;
}

/// <summary>
/// Represents a slice of a group that appears on a specific page.
/// Handles cases where a group spans multiple pages.
/// </summary>
public class PageGroupSlice
{
    /// <summary>
    /// Reference to the full group info.
    /// </summary>
    public MediaGroupInfo Group { get; set; }

    /// <summary>
    /// Index of this group in the groups list.
    /// </summary>
    public int GroupIndex { get; set; }

    /// <summary>
    /// First item index within the group (0-based relative to group start).
    /// </summary>
    public int StartIndexInGroup { get; set; }

    /// <summary>
    /// Last item index within the group (0-based relative to group start).
    /// </summary>
    public int EndIndexInGroup { get; set; }

    /// <summary>
    /// Whether to show the group header for this slice.
    /// True for first slice of a group, may also be true for continued slices with sticky header.
    /// </summary>
    public bool ShowHeader { get; set; }

    /// <summary>
    /// Whether this slice is a continuation from previous page.
    /// Used to display "(continued)" or sticky header.
    /// </summary>
    public bool IsContinued { get; set; }

    /// <summary>
    /// Y offset of this slice within the page.
    /// </summary>
    public float YOffsetInPage { get; set; }

    /// <summary>
    /// Height of this slice (header + items).
    /// </summary>
    public float Height { get; set; }

    /// <summary>
    /// Number of items in this slice.
    /// </summary>
    public int ItemCount => EndIndexInGroup - StartIndexInGroup + 1;

    /// <summary>
    /// Number of rows in this slice.
    /// </summary>
    public int GetRowCount(int columnsPerRow)
    {
        return UnityEngine.Mathf.CeilToInt((float)ItemCount / columnsPerRow);
    }
}
