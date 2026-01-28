using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Virtualized Grid View for Media Library.
/// Cloned from RTTFileGrid with modifications for MediaVideoInfo.
/// Only renders visible items + buffer to maintain performance.
/// Uses object pooling to reuse RTTMediaGridItem instances.
/// </summary>
public class RTTMediaGrid : MonoBehaviour
{
    #region Configuration
    private RTTMediaLibraryController _controller;
    private float _width;
    private float _height;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;
    #endregion

    #region UI References
    private ScrollRect _scrollRect;
    private RectTransform _contentRect;
    private RectTransform _viewportRect;
    #endregion

    #region Data
    private List<MediaVideoInfo> _allVideos = new List<MediaVideoInfo>();

    /// <summary>
    /// Runtime data for each group with cached Y offsets for O(1) position lookups.
    /// </summary>
    private class GroupRuntimeData
    {
        public MediaGroupInfo Info;
        public float YOffset;           // Cached Y position of header top
        public float ContentHeight;     // Height of all item rows in this group
    }
    private List<GroupRuntimeData> _groupData = new List<GroupRuntimeData>();

    /// <summary>
    /// Pre-calculated page layouts for group-aware pagination.
    /// </summary>
    private List<PageLayoutInfo> _pageLayouts = new List<PageLayoutInfo>();
    #endregion

    #region Object Pool
    private List<RTTMediaGridItem> _itemPool = new List<RTTMediaGridItem>();
    private Dictionary<int, RTTMediaGridItem> _visibleItems = new Dictionary<int, RTTMediaGridItem>();

    // Group Header Pool
    private List<RTTMediaGroupHeader> _headerPool = new List<RTTMediaGroupHeader>();
    private Dictionary<int, RTTMediaGroupHeader> _visibleHeaders = new Dictionary<int, RTTMediaGroupHeader>();
    private const float GROUP_HEADER_HEIGHT = 60f;

    // Sticky Header
    private RTTMediaGroupHeader _stickyHeader;
    private RectTransform _stickyHeaderContainer;
    private int _currentStickyGroupIndex = -1;
    #endregion

    #region Grid Configuration
    // Synced with RTTFileGrid dimensions
    private float _cellWidth = RTTMediaGridItem.CELL_WIDTH;
    private float _cellHeight = RTTMediaGridItem.CELL_HEIGHT;
    private float _spacingX = 30f;
    private float _spacingY = 30f;
    private float _paddingLeft = 20f;
    private float _paddingRight = 20f;
    private float _paddingTop = 20f;
    private float _paddingBottom = 60f;
    #endregion

    #region Calculated Values
    private int _columnsPerRow;
    private float _rowHeight;
    private int _visibleRowCount;
    private int _bufferRows = 2;
    #endregion

    #region Progressive Loading
    private Coroutine _progressiveBindCoroutine;
    private Queue<int> _pendingBindIndices = new Queue<int>();
    private const int BINDS_PER_FRAME = 2; // Load 2 items per frame to spread I/O
    private const int BINDS_PER_FRAME_SMALL = 50; // For small datasets, bind all at once to avoid race conditions
    #endregion

    #region Callbacks
    private Action<string> _onItemHoverEnter;
    private Action<string> _onItemHoverExit;
    private Action<MediaVideoInfo> _onItemClick;
    private Action<MediaVideoInfo> _onItemDoubleClick;
    #endregion

    #region Events
    public event Action<MediaVideoInfo> OnVideoSelected;
    public event Action<MediaVideoInfo> OnVideoDoubleClicked;
    public event Action<int, int> OnPageChanged;
    /// <summary>
    /// Fired when all items have been bound to the grid (initial load complete).
    /// </summary>
    public event Action OnBindingComplete;
    #endregion

    #region Properties
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int ItemsPerPage => _columnsPerRow * GetVisibleRowsForPagination();
    public MediaVideoInfo? SelectedVideo { get; private set; }
    #endregion

    #region Initialization
    public void Initialize(RTTMediaLibraryController controller, float w, float h,
        TMP_FontAsset font, Color primaryColor, Color accentColor)
    {
        _controller = controller;
        _width = w;
        _height = h;
        _font = font;
        _primaryColor = primaryColor;
        _accentColor = accentColor;

        // Setup internal callbacks BEFORE CreateItemPool so pool items get valid callbacks
        _onItemClick = OnItemClicked;
        _onItemDoubleClick = OnItemDoubleClicked;

        BuildUI();
        CalculateGridMetrics();
        CreateItemPool();
    }

    private void BuildUI()
    {
        // 1. Setup ScrollRect
        _scrollRect = gameObject.AddComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.scrollSensitivity = 20f;
        _scrollRect.movementType = ScrollRect.MovementType.Elastic;
        _scrollRect.onValueChanged.AddListener(OnScrollChanged);

        // 2. Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(transform, false);
        _viewportRect = viewport.AddComponent<RectTransform>();
        _viewportRect.anchorMin = Vector2.zero;
        _viewportRect.anchorMax = Vector2.one;
        _viewportRect.offsetMin = Vector2.zero;
        _viewportRect.offsetMax = Vector2.zero;

        viewport.AddComponent<RectMask2D>();

        _scrollRect.viewport = _viewportRect;

        // 3. Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _contentRect = content.AddComponent<RectTransform>();
        _contentRect.anchorMin = new Vector2(0, 1);
        _contentRect.anchorMax = new Vector2(1, 1);
        _contentRect.pivot = new Vector2(0.5f, 1);
        _contentRect.sizeDelta = Vector2.zero;

        _scrollRect.content = _contentRect;

        // 4. Sticky Header Container (rendered on top of viewport)
        CreateStickyHeader();
    }

    private void CreateStickyHeader()
    {
        // Container for sticky header - sits on top of viewport
        GameObject stickyContainer = new GameObject("StickyHeaderContainer");
        stickyContainer.transform.SetParent(transform, false);
        _stickyHeaderContainer = stickyContainer.AddComponent<RectTransform>();
        _stickyHeaderContainer.anchorMin = new Vector2(0, 1);
        _stickyHeaderContainer.anchorMax = new Vector2(1, 1);
        _stickyHeaderContainer.pivot = new Vector2(0.5f, 1);
        _stickyHeaderContainer.anchoredPosition = new Vector2(0, -_paddingTop);
        _stickyHeaderContainer.sizeDelta = new Vector2(0, GROUP_HEADER_HEIGHT);

        // Add background to mask content behind
        var bgImage = stickyContainer.AddComponent<Image>();
        bgImage.color = new Color(0.1f, 0.1f, 0.15f, 0.95f); // Semi-transparent dark background

        // Create the sticky header
        GameObject headerObj = new GameObject("StickyHeader");
        headerObj.transform.SetParent(stickyContainer.transform, false);

        var headerRect = headerObj.AddComponent<RectTransform>();
        headerRect.anchorMin = Vector2.zero;
        headerRect.anchorMax = Vector2.one;
        headerRect.offsetMin = new Vector2(_paddingLeft, 0);
        headerRect.offsetMax = new Vector2(-_paddingRight, 0);

        _stickyHeader = headerObj.AddComponent<RTTMediaGroupHeader>();
        _stickyHeader.Initialize(_font, _primaryColor, _accentColor);
        _stickyHeader.SetSelectionCallback(OnGroupSelectionChanged);

        // Initially hidden
        _stickyHeaderContainer.gameObject.SetActive(false);
    }

    private void CalculateGridMetrics()
    {
        float availableWidth = _width - _paddingLeft - _paddingRight;
        _columnsPerRow = Mathf.Max(1, Mathf.FloorToInt((availableWidth + _spacingX) / (_cellWidth + _spacingX)));
        _rowHeight = _cellHeight + _spacingY;
        _visibleRowCount = Mathf.CeilToInt(_height / _rowHeight) + 1;

        Debug.Log($"[RTTMediaGrid] CalculateGridMetrics: width={_width}, height={_height}, columnsPerRow={_columnsPerRow}, rowHeight={_rowHeight}, visibleRowCount={_visibleRowCount}, cellSize={_cellWidth}x{_cellHeight}");
    }

    private void CreateItemPool()
    {
        int poolSize = (_visibleRowCount + _bufferRows * 2) * _columnsPerRow;

        for (int i = 0; i < poolSize; i++)
        {
            var item = CreatePooledItem();
            item.gameObject.SetActive(false);
            _itemPool.Add(item);
        }
    }

    private RTTMediaGridItem CreatePooledItem()
    {
        GameObject itemObj = new GameObject("PooledItem");
        itemObj.transform.SetParent(_contentRect, false);

        var rect = itemObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(_cellWidth, _cellHeight);

        var gridItem = itemObj.AddComponent<RTTMediaGridItem>();
        gridItem.Initialize(_font, _primaryColor, _accentColor);
        gridItem.SetCallbacks(_onItemHoverEnter, _onItemHoverExit, _onItemClick, _onItemDoubleClick);
        gridItem.SetSelectionCallback(OnItemSelectionChanged);

        return gridItem;
    }
    #endregion

    #region Public Methods
    public void SetData(List<MediaVideoInfo> videos, List<MediaGroupInfo> groups = null)
    {
        // Stop any pending progressive binding (prevents binding stale data)
        if (_progressiveBindCoroutine != null)
        {
            StopCoroutine(_progressiveBindCoroutine);
            _progressiveBindCoroutine = null;
        }
        _pendingBindIndices.Clear();

        _allVideos = videos ?? new List<MediaVideoInfo>();

        // Build group runtime data with cached Y offsets
        var groupList = groups ?? new List<MediaGroupInfo>();
        _groupData.Clear();
        foreach (var g in groupList)
        {
            _groupData.Add(new GroupRuntimeData { Info = g });
        }
        CalculateGroupOffsets();

        // Hide all visible items
        foreach (var kvp in _visibleItems)
        {
            kvp.Value.OnRecycle();
            kvp.Value.gameObject.SetActive(false);
        }
        _visibleItems.Clear();

        // Hide all visible group headers
        foreach (var kvp in _visibleHeaders)
        {
            kvp.Value.gameObject.SetActive(false);
        }
        _visibleHeaders.Clear();

        // NOTE: Don't call CleanupOrphanedCache here - it's expensive and causes lag
        // Cache cleanup should be done periodically or when app closes, not on every category switch

        // Update content size (includes group headers) - must be before CalculatePageLayout
        UpdateContentSize();

        // Calculate group-aware pagination
        CalculatePageLayout();
        CurrentPage = Mathf.Clamp(CurrentPage, 1, TotalPages);

        // Reset scroll
        _scrollRect.verticalNormalizedPosition = 1f;

        Debug.Log($"[RTTMediaGrid] SetData: {_allVideos.Count} items, {_groupData.Count} groups, columnsPerRow={_columnsPerRow}, visibleRowCount={_visibleRowCount}, gridHeight={_height}, rowHeight={_rowHeight}");

        // Render visible items (uses progressive loading to prevent frame drops)
        UpdateVisibleItems();

        // Initialize sticky header
        UpdateStickyHeader();
    }

    public void SelectVideo(MediaVideoInfo video)
    {
        SelectedVideo = video;

        foreach (var kvp in _visibleItems)
        {
            bool selected = kvp.Value.VideoInfo.Path == video.Path;
            kvp.Value.SetSelected(selected);
        }

        OnVideoSelected?.Invoke(video);
    }

    public void ClearSelection()
    {
        SelectedVideo = null;

        foreach (var kvp in _visibleItems)
        {
            kvp.Value.SetSelected(false);
        }
    }

    public void Refresh()
    {
        UpdateVisibleItems();
    }

    public int GetColumnsPerRow()
    {
        return _columnsPerRow;
    }

    public void GoToPage(int page)
    {
        page = Mathf.Clamp(page, 1, TotalPages);
        if (page != CurrentPage)
        {
            CurrentPage = page;
            ScrollToPage(page);
            OnPageChanged?.Invoke(CurrentPage, TotalPages);
        }
    }

    public void NextPage()
    {
        GoToPage(CurrentPage + 1);
    }

    public void PreviousPage()
    {
        GoToPage(CurrentPage - 1);
    }

    public int GetVisibleRowsForPagination()
    {
        // Fixed 2 rows per page as per user request
        return 2;
    }

    public int GetItemsPerPage(int rowsPerPage)
    {
        return rowsPerPage * _columnsPerRow;
    }
    #endregion

    #region Private Methods
    private void UpdateContentSize()
    {
        int totalRows = Mathf.CeilToInt((float)_allVideos.Count / _columnsPerRow);

        // Calculate total content height including group headers
        float contentHeight = _paddingTop;

        if (_groupData.Count > 0)
        {
            // Use cached group data - sum of header heights + content heights
            foreach (var gd in _groupData)
            {
                contentHeight += GROUP_HEADER_HEIGHT + _spacingY + gd.ContentHeight;
            }
        }
        else
        {
            // No groups - simple calculation
            contentHeight += totalRows * _rowHeight - _spacingY;
        }

        contentHeight += _paddingBottom;
        contentHeight = Mathf.Max(contentHeight, _height);
        _contentRect.sizeDelta = new Vector2(0, contentHeight);
    }

    /// <summary>
    /// Get the Y position for an item at the given index, accounting for group headers.
    /// Uses cached group offsets for O(log n) lookup via binary search.
    /// </summary>
    private float GetYPositionForIndex(int index)
    {
        if (_groupData.Count == 0)
        {
            // No groups - simple row calculation
            int row = index / _columnsPerRow;
            return -_paddingTop - row * _rowHeight - _cellHeight / 2f;
        }

        // Binary search to find group containing this index
        var (groupIndex, indexInGroup) = FindGroupForIndex(index);
        if (groupIndex < 0 || groupIndex >= _groupData.Count)
        {
            // Fallback (shouldn't happen)
            int fallbackRow = index / _columnsPerRow;
            return -_paddingTop - fallbackRow * _rowHeight - _cellHeight / 2f;
        }

        var gd = _groupData[groupIndex];
        int rowInGroup = indexInGroup / _columnsPerRow;

        // Y = header YOffset + header height + spacing + row offset + half cell
        return -gd.YOffset - GROUP_HEADER_HEIGHT - _spacingY - rowInGroup * _rowHeight - _cellHeight / 2f;
    }

    /// <summary>
    /// Pre-calculate Y offsets for all groups. Called once in SetData().
    /// </summary>
    private void CalculateGroupOffsets()
    {
        float yOffset = _paddingTop;

        foreach (var gd in _groupData)
        {
            gd.YOffset = yOffset;
            yOffset += GROUP_HEADER_HEIGHT + _spacingY;

            int rows = Mathf.CeilToInt((float)gd.Info.Count / _columnsPerRow);
            gd.ContentHeight = rows * _rowHeight;
            yOffset += gd.ContentHeight;
        }
    }

    /// <summary>
    /// Calculate page layouts based on groups and available viewport height.
    /// Groups are kept together when possible, split with sticky headers when too large.
    /// </summary>
    private void CalculatePageLayout()
    {
        _pageLayouts.Clear();

        if (_allVideos.Count == 0)
        {
            TotalPages = 1;
            return;
        }

        // Available height per page (excluding sticky header space at top)
        float availableHeight = _height - _paddingTop - _paddingBottom;
        float stickyHeaderHeight = GROUP_HEADER_HEIGHT + _spacingY;

        // If no groups, fall back to simple row-based pagination
        if (_groupData.Count == 0)
        {
            CalculateSimplePageLayout(availableHeight);
            return;
        }

        int pageNumber = 1;
        float usedHeight = 0f;
        int globalItemIndex = 0;
        PageLayoutInfo currentPage = new PageLayoutInfo
        {
            PageNumber = pageNumber,
            StartY = 0f,
            FirstItemIndex = 0
        };

        for (int groupIdx = 0; groupIdx < _groupData.Count; groupIdx++)
        {
            var gd = _groupData[groupIdx];
            var group = gd.Info;

            // Calculate full group height (header + all items)
            float headerHeight = GROUP_HEADER_HEIGHT + _spacingY;
            float groupHeight = headerHeight + gd.ContentHeight;

            // Case 1: Group fits entirely on current page
            if (usedHeight + groupHeight <= availableHeight)
            {
                var slice = new PageGroupSlice
                {
                    Group = group,
                    GroupIndex = groupIdx,
                    StartIndexInGroup = 0,
                    EndIndexInGroup = group.Count - 1,
                    ShowHeader = true,
                    IsContinued = false,
                    YOffsetInPage = usedHeight,
                    Height = groupHeight
                };
                currentPage.GroupSlices.Add(slice);
                usedHeight += groupHeight;
                globalItemIndex += group.Count;
            }
            // Case 2: Group fits on a new page (but not current)
            else if (groupHeight <= availableHeight && usedHeight > 0)
            {
                // Finish current page
                currentPage.LastItemIndex = globalItemIndex - 1;
                currentPage.TotalHeight = usedHeight;
                _pageLayouts.Add(currentPage);

                // Start new page
                pageNumber++;
                currentPage = new PageLayoutInfo
                {
                    PageNumber = pageNumber,
                    StartY = gd.YOffset,
                    FirstItemIndex = globalItemIndex
                };
                usedHeight = 0f;

                var slice = new PageGroupSlice
                {
                    Group = group,
                    GroupIndex = groupIdx,
                    StartIndexInGroup = 0,
                    EndIndexInGroup = group.Count - 1,
                    ShowHeader = true,
                    IsContinued = false,
                    YOffsetInPage = 0f,
                    Height = groupHeight
                };
                currentPage.GroupSlices.Add(slice);
                usedHeight = groupHeight;
                globalItemIndex += group.Count;
            }
            // Case 3: Group needs to be split across multiple pages
            else
            {
                int remainingItems = group.Count;
                int startIndexInGroup = 0;
                bool isFirstSlice = true;

                while (remainingItems > 0)
                {
                    // If current page has content, finish it first (unless it's the first slice)
                    if (!isFirstSlice && usedHeight > 0)
                    {
                        currentPage.LastItemIndex = globalItemIndex - 1;
                        currentPage.TotalHeight = usedHeight;
                        _pageLayouts.Add(currentPage);

                        pageNumber++;
                        currentPage = new PageLayoutInfo
                        {
                            PageNumber = pageNumber,
                            StartY = gd.YOffset + headerHeight + (startIndexInGroup / _columnsPerRow) * _rowHeight,
                            FirstItemIndex = globalItemIndex
                        };
                        usedHeight = 0f;
                    }

                    // Calculate how many items fit on this page
                    // Account for sticky header on continued pages
                    float effectiveAvailable = availableHeight - usedHeight;
                    if (!isFirstSlice || usedHeight > 0)
                    {
                        // Reserve space for sticky header on continued slices
                        effectiveAvailable -= stickyHeaderHeight;
                    }
                    else
                    {
                        // First slice needs full header
                        effectiveAvailable -= headerHeight;
                    }

                    int rowsThatFit = Mathf.Max(1, Mathf.FloorToInt(effectiveAvailable / _rowHeight));
                    int itemsThatFit = Mathf.Min(rowsThatFit * _columnsPerRow, remainingItems);

                    float sliceHeight = isFirstSlice ? headerHeight : stickyHeaderHeight;
                    sliceHeight += Mathf.CeilToInt((float)itemsThatFit / _columnsPerRow) * _rowHeight;

                    var slice = new PageGroupSlice
                    {
                        Group = group,
                        GroupIndex = groupIdx,
                        StartIndexInGroup = startIndexInGroup,
                        EndIndexInGroup = startIndexInGroup + itemsThatFit - 1,
                        ShowHeader = true, // Always show header (original or sticky)
                        IsContinued = !isFirstSlice,
                        YOffsetInPage = usedHeight,
                        Height = sliceHeight
                    };
                    currentPage.GroupSlices.Add(slice);

                    usedHeight += sliceHeight;
                    startIndexInGroup += itemsThatFit;
                    globalItemIndex += itemsThatFit;
                    remainingItems -= itemsThatFit;
                    isFirstSlice = false;

                    // If page is full and there's more, start new page
                    if (remainingItems > 0)
                    {
                        currentPage.LastItemIndex = globalItemIndex - 1;
                        currentPage.TotalHeight = usedHeight;
                        _pageLayouts.Add(currentPage);

                        pageNumber++;
                        currentPage = new PageLayoutInfo
                        {
                            PageNumber = pageNumber,
                            StartY = gd.YOffset + headerHeight + (startIndexInGroup / _columnsPerRow) * _rowHeight,
                            FirstItemIndex = globalItemIndex
                        };
                        usedHeight = 0f;
                    }
                }
            }
        }

        // Add the last page
        if (currentPage.GroupSlices.Count > 0)
        {
            currentPage.LastItemIndex = _allVideos.Count - 1;
            currentPage.TotalHeight = usedHeight;
            _pageLayouts.Add(currentPage);
        }

        TotalPages = _pageLayouts.Count;
        Debug.Log($"[RTTMediaGrid] CalculatePageLayout: {TotalPages} pages, {_groupData.Count} groups, {_allVideos.Count} items");
    }

    /// <summary>
    /// Simple page layout when no groups are present.
    /// </summary>
    private void CalculateSimplePageLayout(float availableHeight)
    {
        int rowsPerPage = Mathf.Max(1, Mathf.FloorToInt(availableHeight / _rowHeight));
        int itemsPerPage = rowsPerPage * _columnsPerRow;
        int totalItems = _allVideos.Count;

        int pageNumber = 1;
        int itemIndex = 0;

        while (itemIndex < totalItems)
        {
            int itemsOnPage = Mathf.Min(itemsPerPage, totalItems - itemIndex);
            int rowsOnPage = Mathf.CeilToInt((float)itemsOnPage / _columnsPerRow);

            var page = new PageLayoutInfo
            {
                PageNumber = pageNumber,
                StartY = _paddingTop + (pageNumber - 1) * rowsPerPage * _rowHeight,
                FirstItemIndex = itemIndex,
                LastItemIndex = itemIndex + itemsOnPage - 1,
                TotalHeight = rowsOnPage * _rowHeight
            };
            _pageLayouts.Add(page);

            itemIndex += itemsOnPage;
            pageNumber++;
        }

        TotalPages = _pageLayouts.Count;
    }

    /// <summary>
    /// Get the page layout for a specific page number.
    /// </summary>
    public PageLayoutInfo GetPageLayout(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > _pageLayouts.Count)
            return null;
        return _pageLayouts[pageNumber - 1];
    }

    /// <summary>
    /// Get the current group being displayed (for sticky header).
    /// Returns the first group visible on the current page.
    /// </summary>
    public MediaGroupInfo GetCurrentStickyGroup()
    {
        if (_pageLayouts.Count == 0 || CurrentPage < 1 || CurrentPage > _pageLayouts.Count)
            return null;

        var page = _pageLayouts[CurrentPage - 1];
        if (page.GroupSlices.Count > 0)
            return page.GroupSlices[0].Group;

        return null;
    }

    /// <summary>
    /// Find group containing the given global index using binary search. O(log n).
    /// Returns (groupIndex, indexInGroup).
    /// </summary>
    private (int groupIndex, int indexInGroup) FindGroupForIndex(int globalIndex)
    {
        if (_groupData.Count == 0 || globalIndex < 0)
            return (-1, globalIndex);

        // Binary search through StartIndex
        int lo = 0, hi = _groupData.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_groupData[mid].Info.StartIndex <= globalIndex)
                lo = mid;
            else
                hi = mid - 1;
        }

        return (lo, globalIndex - _groupData[lo].Info.StartIndex);
    }

    /// <summary>
    /// Get group info for an item at the given index.
    /// Returns null if no groups or index out of range.
    /// </summary>
    private MediaGroupInfo GetGroupForIndex(int index)
    {
        var (groupIndex, _) = FindGroupForIndex(index);
        if (groupIndex < 0 || groupIndex >= _groupData.Count) return null;
        return _groupData[groupIndex].Info;
    }

    /// <summary>
    /// Get the Y position for a group header using cached offset. O(1).
    /// </summary>
    private float GetGroupHeaderYPosition(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groupData.Count)
            return -_paddingTop - GROUP_HEADER_HEIGHT / 2f;

        return -_groupData[groupIndex].YOffset - GROUP_HEADER_HEIGHT / 2f;
    }

    private void OnScrollChanged(Vector2 normalizedPos)
    {
        UpdateVisibleItems();
        UpdateStickyHeader();
    }

    /// <summary>
    /// Update the sticky header based on current scroll position.
    /// Shows the group header for the topmost visible group.
    /// </summary>
    private void UpdateStickyHeader()
    {
        if (_stickyHeader == null || _stickyHeaderContainer == null) return;

        // Don't show sticky header if no groups
        if (_groupData.Count == 0)
        {
            _stickyHeaderContainer.gameObject.SetActive(false);
            _currentStickyGroupIndex = -1;
            return;
        }

        float scrollY = _contentRect.anchoredPosition.y;
        float stickyThreshold = _paddingTop + GROUP_HEADER_HEIGHT;

        // Find which group should be sticky
        int stickyGroupIndex = -1;
        for (int i = 0; i < _groupData.Count; i++)
        {
            var gd = _groupData[i];
            float groupTop = gd.YOffset;
            float groupBottom = gd.YOffset + GROUP_HEADER_HEIGHT + _spacingY + gd.ContentHeight;

            // Group is sticky if its header has scrolled past the top
            // and its content is still visible
            if (scrollY >= groupTop - _paddingTop && scrollY < groupBottom - GROUP_HEADER_HEIGHT)
            {
                stickyGroupIndex = i;
                break;
            }
        }

        // Update sticky header if group changed
        if (stickyGroupIndex != _currentStickyGroupIndex)
        {
            _currentStickyGroupIndex = stickyGroupIndex;

            if (stickyGroupIndex >= 0 && stickyGroupIndex < _groupData.Count)
            {
                var group = _groupData[stickyGroupIndex].Info;
                _stickyHeader.Bind(group);
                _stickyHeader.SetEditMode(_isEditMode);
                _stickyHeaderContainer.gameObject.SetActive(true);
            }
            else
            {
                _stickyHeaderContainer.gameObject.SetActive(false);
            }
        }

        // Show sticky header only when original header is scrolled out of view
        if (stickyGroupIndex >= 0)
        {
            var gd = _groupData[stickyGroupIndex];
            bool shouldShow = scrollY > gd.YOffset;
            _stickyHeaderContainer.gameObject.SetActive(shouldShow);
        }
    }

    private void UpdateVisibleItems()
    {
        if (_allVideos.Count == 0) return;

        int firstVisibleIndex;
        int lastVisibleIndex;

        // For small datasets, render ALL items to avoid virtualization edge cases
        const int SMALL_DATASET_THRESHOLD = 50;
        if (_allVideos.Count <= SMALL_DATASET_THRESHOLD)
        {
            firstVisibleIndex = 0;
            lastVisibleIndex = _allVideos.Count - 1;
            Debug.Log($"[RTTMediaGrid] UpdateVisibleItems (ALL): indices=0-{lastVisibleIndex}, total={_allVideos.Count}");
        }
        else
        {
            // For large datasets, use virtualization based on scroll position
            float scrollY = _contentRect.anchoredPosition.y;
            int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt((scrollY - _paddingTop) / _rowHeight) - _bufferRows);
            int lastVisibleRow = firstVisibleRow + _visibleRowCount + _bufferRows * 2;

            firstVisibleIndex = firstVisibleRow * _columnsPerRow;
            lastVisibleIndex = Mathf.Min((lastVisibleRow + 1) * _columnsPerRow - 1, _allVideos.Count - 1);
            Debug.Log($"[RTTMediaGrid] UpdateVisibleItems (VIRTUAL): scrollY={scrollY}, rows={firstVisibleRow}-{lastVisibleRow}, indices={firstVisibleIndex}-{lastVisibleIndex}, total={_allVideos.Count}");
        }

        // Find items no longer visible
        List<int> toRemove = new List<int>();
        foreach (var kvp in _visibleItems)
        {
            if (kvp.Key < firstVisibleIndex || kvp.Key > lastVisibleIndex)
            {
                kvp.Value.OnRecycle();
                kvp.Value.gameObject.SetActive(false);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var idx in toRemove)
        {
            _visibleItems.Remove(idx);
        }

        // Collect items that need to be bound (progressive loading)
        List<int> toBind = new List<int>();
        for (int i = firstVisibleIndex; i <= lastVisibleIndex && i < _allVideos.Count; i++)
        {
            if (!_visibleItems.ContainsKey(i))
            {
                toBind.Add(i);
            }
        }

        // If we have items to bind, use progressive loading
        if (toBind.Count > 0)
        {
            // DON'T clear the queue - just add items that aren't already queued
            // This prevents race conditions where scroll events clear items before they're bound
            HashSet<int> alreadyQueued = new HashSet<int>(_pendingBindIndices);
            int addedCount = 0;

            foreach (var idx in toBind)
            {
                if (!alreadyQueued.Contains(idx))
                {
                    _pendingBindIndices.Enqueue(idx);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                Debug.Log($"[RTTMediaGrid] Queuing {addedCount} NEW items for binding (total pending={_pendingBindIndices.Count})");
            }

            // Start progressive binding if not already running
            if (_progressiveBindCoroutine == null)
            {
                _progressiveBindCoroutine = StartCoroutine(ProgressiveBindCoroutine());
            }
        }
        else if (_progressiveBindCoroutine == null)
        {
            Debug.Log($"[RTTMediaGrid] No items to bind (all {_visibleItems.Count} already visible)");
        }
    }

    /// <summary>
    /// Progressively bind items to spread disk I/O across multiple frames.
    /// Prevents frame drops when loading many thumbnails at once.
    /// For small datasets, binds all at once to avoid race conditions with page changes.
    /// </summary>
    private IEnumerator ProgressiveBindCoroutine()
    {
        int totalBound = 0;
        int totalSkipped = 0;

        // For small datasets, bind all at once to avoid race conditions where
        // page changes can interrupt binding before all items are rendered
        const int SMALL_DATASET_THRESHOLD = 50;
        int maxBindsPerFrame = (_allVideos.Count <= SMALL_DATASET_THRESHOLD)
            ? BINDS_PER_FRAME_SMALL
            : BINDS_PER_FRAME;

        Debug.Log($"[RTTMediaGrid] ProgressiveBindCoroutine started, pending={_pendingBindIndices.Count}, bindsPerFrame={maxBindsPerFrame}");

        // Render group headers immediately (they don't need progressive loading)
        UpdateVisibleGroupHeaders();

        while (_pendingBindIndices.Count > 0)
        {
            int bindCount = 0;

            while (_pendingBindIndices.Count > 0 && bindCount < maxBindsPerFrame)
            {
                int idx = _pendingBindIndices.Dequeue();

                // Skip if already bound or index out of range
                if (_visibleItems.ContainsKey(idx))
                {
                    Debug.Log($"[RTTMediaGrid] Skip index {idx}: already bound");
                    totalSkipped++;
                    continue;
                }
                if (idx >= _allVideos.Count)
                {
                    Debug.LogWarning($"[RTTMediaGrid] Skip index {idx}: out of range (count={_allVideos.Count})");
                    totalSkipped++;
                    continue;
                }

                var item = GetPooledItem();
                if (item != null)
                {
                    try
                    {
                        BindItemAtIndex(item, idx);
                        _visibleItems[idx] = item;
                        bindCount++;
                        totalBound++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[RTTMediaGrid] Error binding index {idx}: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[RTTMediaGrid] GetPooledItem returned null for index {idx}");
                }
            }

            // Wait for next frame before binding more
            yield return null;
        }

        Debug.Log($"[RTTMediaGrid] ProgressiveBindCoroutine complete: bound={totalBound}, skipped={totalSkipped}, visibleItems={_visibleItems.Count}, totalItems={_allVideos.Count}");

        // Validate: check if any items in the expected range are missing
        if (_visibleItems.Count < _allVideos.Count)
        {
            // Find missing indices
            for (int i = 0; i < _allVideos.Count; i++)
            {
                if (!_visibleItems.ContainsKey(i))
                {
                    Debug.LogWarning($"[RTTMediaGrid] Missing item at index {i}: {_allVideos[i].Title}");
                }
            }
        }

        _progressiveBindCoroutine = null;

        // Notify listeners that binding is complete (e.g., to start background scan)
        OnBindingComplete?.Invoke();
    }

    private RTTMediaGridItem GetPooledItem()
    {
        foreach (var item in _itemPool)
        {
            if (!item.gameObject.activeSelf)
            {
                return item;
            }
        }

        var newItem = CreatePooledItem();
        _itemPool.Add(newItem);
        return newItem;
    }

    private RTTMediaGroupHeader GetPooledHeader()
    {
        foreach (var header in _headerPool)
        {
            if (!header.gameObject.activeSelf)
            {
                return header;
            }
        }

        var newHeader = CreateGroupHeader();
        _headerPool.Add(newHeader);
        return newHeader;
    }

    private RTTMediaGroupHeader CreateGroupHeader()
    {
        GameObject headerObj = new GameObject("GroupHeader");
        headerObj.transform.SetParent(_contentRect, false);

        var rect = headerObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(_width - _paddingLeft - _paddingRight, GROUP_HEADER_HEIGHT);

        var header = headerObj.AddComponent<RTTMediaGroupHeader>();
        header.Initialize(_font, _primaryColor, _accentColor);
        header.SetSelectionCallback(OnGroupSelectionChanged);

        return header;
    }

    private void BindGroupHeader(RTTMediaGroupHeader header, int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groupData.Count) return;

        var group = _groupData[groupIndex].Info;
        header.Bind(group);

        // Calculate position - left-aligned
        float y = GetGroupHeaderYPosition(groupIndex);

        var rect = header.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 0.5f);  // Left-center pivot
        rect.sizeDelta = new Vector2(_width - _paddingLeft - _paddingRight, GROUP_HEADER_HEIGHT);
        rect.anchoredPosition = new Vector2(_paddingLeft, y);  // Left-aligned

        // Restore edit mode
        header.SetEditMode(_isEditMode);

        header.gameObject.SetActive(true);
    }

    private void OnGroupSelectionChanged(MediaGroupInfo group, bool isSelected)
    {
        if (group == null) return;

        // Select/deselect all items in the group
        foreach (var video in group.Items)
        {
            if (isSelected)
            {
                _selectedPaths.Add(video.Path);
            }
            else
            {
                _selectedPaths.Remove(video.Path);
            }

            // Update visible item checkboxes
            foreach (var kvp in _visibleItems)
            {
                if (kvp.Value.FilePath == video.Path)
                {
                    kvp.Value.SetSelected(isSelected);
                }
            }
        }

        _onSelectionChanged?.Invoke();
    }

    /// <summary>
    /// Update visible group headers based on scroll position.
    /// Virtualized: only renders headers in viewport + buffer.
    /// </summary>
    private void UpdateVisibleGroupHeaders()
    {
        if (_groupData.Count == 0) return;

        float scrollY = _contentRect.anchoredPosition.y;
        float viewportTop = scrollY;
        float viewportBottom = scrollY + _height;
        float headerBuffer = GROUP_HEADER_HEIGHT * 2;  // Buffer zone

        // Hide headers outside viewport
        List<int> toRemove = new List<int>();
        foreach (var kvp in _visibleHeaders)
        {
            if (kvp.Key >= _groupData.Count)
            {
                // Invalid index
                kvp.Value.OnRecycle();
                kvp.Value.gameObject.SetActive(false);
                toRemove.Add(kvp.Key);
                continue;
            }

            var gd = _groupData[kvp.Key];
            float headerTop = gd.YOffset;
            float groupBottom = gd.YOffset + GROUP_HEADER_HEIGHT + _spacingY + gd.ContentHeight;

            // Hide if group is completely outside viewport (with buffer)
            if (groupBottom < viewportTop - headerBuffer || headerTop > viewportBottom + headerBuffer)
            {
                kvp.Value.OnRecycle();
                kvp.Value.gameObject.SetActive(false);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var idx in toRemove)
        {
            _visibleHeaders.Remove(idx);
        }

        // Show headers in viewport
        for (int i = 0; i < _groupData.Count; i++)
        {
            if (_visibleHeaders.ContainsKey(i)) continue;

            var gd = _groupData[i];
            float headerTop = gd.YOffset;
            float groupBottom = gd.YOffset + GROUP_HEADER_HEIGHT + _spacingY + gd.ContentHeight;

            // Show if any part of group is in viewport (with buffer)
            if (groupBottom >= viewportTop - headerBuffer && headerTop <= viewportBottom + headerBuffer)
            {
                var header = GetPooledHeader();
                BindGroupHeader(header, i);
                _visibleHeaders[i] = header;
            }
        }
    }

    private void BindItemAtIndex(RTTMediaGridItem item, int index)
    {
        var video = _allVideos[index];

        // Calculate column position (within group if grouped) using binary search
        int col;
        if (_groupData.Count > 0)
        {
            var (_, indexInGroup) = FindGroupForIndex(index);
            col = indexInGroup % _columnsPerRow;
        }
        else
        {
            col = index % _columnsPerRow;
        }

        float x = _paddingLeft + col * (_cellWidth + _spacingX) + _cellWidth / 2f;
        float y = GetYPositionForIndex(index);

        // Center the grid horizontally
        float totalGridWidth = _columnsPerRow * _cellWidth + (_columnsPerRow - 1) * _spacingX;
        float offsetX = (_width - totalGridWidth - _paddingLeft - _paddingRight) / 2f;
        x += offsetX;

        var rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);

        Debug.Log($"[RTTMediaGrid] BindItemAtIndex: index={index}, title={video.Title}, col={col}, pos=({x:F0},{y:F0})");

        // Bind data
        item.Bind(video);

        // Restore edit mode and selection state
        item.SetEditMode(_isEditMode);
        item.SetSelected(_selectedPaths.Contains(video.Path));

        item.gameObject.SetActive(true);
    }

    private Coroutine _scrollCoroutine;

    private void ScrollToPage(int pageIndex)
    {
        if (_scrollRect == null || _contentRect == null) return;

        Canvas.ForceUpdateCanvases();

        float targetY;

        // Use pre-calculated page layout if available
        if (_pageLayouts.Count > 0 && pageIndex >= 1 && pageIndex <= _pageLayouts.Count)
        {
            var pageLayout = _pageLayouts[pageIndex - 1];
            targetY = pageLayout.StartY;

            // Update sticky header when page changes
            UpdateStickyHeader();
        }
        else
        {
            // Fallback to simple row-based calculation
            int rowsPerPage = GetVisibleRowsForPagination();
            int targetRow = (pageIndex - 1) * rowsPerPage;
            targetY = _paddingTop + targetRow * _rowHeight;
        }

        if (pageIndex == 1) targetY = 0;

        float contentHeight = _contentRect.rect.height;
        float viewportHeight = _scrollRect.viewport.rect.height;
        float maxScrollY = Mathf.Max(0, contentHeight - viewportHeight);

        targetY = Mathf.Clamp(targetY, 0, maxScrollY);

        if (_scrollCoroutine != null) StopCoroutine(_scrollCoroutine);
        _scrollCoroutine = StartCoroutine(SmoothScroll(targetY, 0.3f));
    }

    private IEnumerator SmoothScroll(float targetY, float duration)
    {
        float time = 0;
        float startY = _contentRect.anchoredPosition.y;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;
            t = 1f - Mathf.Pow(1f - t, 3);

            float newY = Mathf.Lerp(startY, targetY, t);
            _contentRect.anchoredPosition = new Vector2(_contentRect.anchoredPosition.x, newY);
            yield return null;
        }

        _contentRect.anchoredPosition = new Vector2(_contentRect.anchoredPosition.x, targetY);
        _scrollCoroutine = null;
    }
    #endregion

    #region Event Handlers
    private void OnItemClicked(MediaVideoInfo video)
    {
        SelectVideo(video);
    }

    private void OnItemDoubleClicked(MediaVideoInfo video)
    {
        OnVideoDoubleClicked?.Invoke(video);
    }
    #endregion

    #region Edit Mode
    private bool _isEditMode = false;
    private HashSet<string> _selectedPaths = new HashSet<string>();
    private Action _onSelectionChanged;

    public void SetSelectionChangedCallback(Action callback)
    {
        _onSelectionChanged = callback;
    }

    public void SetEditMode(bool editMode)
    {
        _isEditMode = editMode;

        foreach (var item in _itemPool)
        {
            item.SetEditMode(editMode);
        }

        // Also update group headers
        foreach (var header in _headerPool)
        {
            header.SetEditMode(editMode);
        }

        // Update sticky header
        if (_stickyHeader != null)
        {
            _stickyHeader.SetEditMode(editMode);
        }

        if (!editMode)
        {
            _selectedPaths.Clear();
        }
    }

    public bool AreAllSelected()
    {
        if (_allVideos.Count == 0) return false;
        return _selectedPaths.Count == _allVideos.Count;
    }

    public void SetAllSelected(bool selected)
    {
        _selectedPaths.Clear();

        if (selected)
        {
            foreach (var video in _allVideos)
            {
                _selectedPaths.Add(video.Path);
            }
        }

        foreach (var kvp in _visibleItems)
        {
            kvp.Value.SetSelected(selected);
        }

        _onSelectionChanged?.Invoke();
    }

    public HashSet<string> GetSelectedPaths()
    {
        return new HashSet<string>(_selectedPaths);
    }

    public void ToggleItemSelection(string path)
    {
        bool isSelected = _selectedPaths.Contains(path);
        if (isSelected)
            _selectedPaths.Remove(path);
        else
            _selectedPaths.Add(path);

        foreach (var kvp in _visibleItems)
        {
            if (kvp.Value.FilePath == path)
            {
                kvp.Value.SetSelected(!isSelected);
                break;
            }
        }

        _onSelectionChanged?.Invoke();
    }

    private void OnItemSelectionChanged(string path, bool isSelected)
    {
        if (isSelected)
            _selectedPaths.Add(path);
        else
            _selectedPaths.Remove(path);

        _onSelectionChanged?.Invoke();
    }
    #endregion

    #region Public API
    public RTTMediaGridItem GetVisibleItemAtIndex(int index)
    {
        if (_visibleItems.TryGetValue(index, out var item))
        {
            return item;
        }
        return null;
    }

    public IEnumerable<RTTMediaGridItem> GetVisibleItems()
    {
        return _visibleItems.Values;
    }

    public MediaVideoInfo? GetVideoAtIndex(int index)
    {
        if (index >= 0 && index < _allVideos.Count)
        {
            return _allVideos[index];
        }
        return null;
    }

    public float GetScrollPosition()
    {
        if (_scrollRect == null) return 1f;
        return _scrollRect.verticalNormalizedPosition;
    }

    public void SetScrollPosition(float normalizedPosition)
    {
        if (_scrollRect == null) return;

        if (_scrollCoroutine != null)
        {
            StopCoroutine(_scrollCoroutine);
            _scrollCoroutine = null;
        }

        _scrollRect.verticalNormalizedPosition = normalizedPosition;
        UpdateVisibleItems();
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        if (_progressiveBindCoroutine != null)
        {
            StopCoroutine(_progressiveBindCoroutine);
            _progressiveBindCoroutine = null;
        }
        _pendingBindIndices.Clear();
    }
    #endregion
}
