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
    
    // Mobile-first frame budget system: bind items until time budget exhausted
    private const float MOBILE_FRAME_BUDGET_MS = 1.5f;  // Mobile: 1.5ms for smooth 60fps
    private const float DESKTOP_FRAME_BUDGET_MS = 4f;   // Desktop: 4ms with more headroom
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
    /// Fired when a video item is hovered.
    /// </summary>
    public event Action<MediaVideoInfo> OnVideoHoverEnter;
    /// <summary>
    /// Fired when hover exits a video item.
    /// </summary>
    public event Action<MediaVideoInfo> OnVideoHoverExit;
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
        _onItemHoverEnter = OnItemHoverEnter;
        _onItemHoverExit = OnItemHoverExit;
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

        // 4. Sticky Header DISABLED - headers now scroll with content
        // CreateStickyHeader();
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
        // HARDCODE: Always use 4 columns per row as per user requirement
        _columnsPerRow = 4;
        _rowHeight = _cellHeight + _spacingY;
        _visibleRowCount = Mathf.CeilToInt(_height / _rowHeight) + 1;

        // Debug.Log($"[RTTMediaGrid] CalculateGridMetrics: width={_width}, height={_height}, columnsPerRow={_columnsPerRow}, rowHeight={_rowHeight}, visibleRowCount={_visibleRowCount}, cellSize={_cellWidth}x{_cellHeight}");
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

        // Debug.Log($"[RTTMediaGrid] SetData: {_allVideos.Count} items, {_groupData.Count} groups, columnsPerRow={_columnsPerRow}, visibleRowCount={_visibleRowCount}, gridHeight={_height}, rowHeight={_rowHeight}");

        // Render visible items (uses progressive loading to prevent frame drops)
        UpdateVisibleItems();

        // Initialize sticky header
        UpdateStickyHeader();

        // Auto-select first item if there are videos and no current selection
        // This fires OnVideoSelected event which shows the action bar
        if (_allVideos.Count > 0 && !_isEditMode)
        {
            Debug.Log($"[RTTMediaGrid] Auto-selecting first item: {_allVideos[0].Title}");
            SelectVideo(_allVideos[0]);
        }
    }

    public void SelectVideo(MediaVideoInfo video)
    {
        SelectedVideo = video;

        // In edit mode, don't apply visual selection effect
        if (!_isEditMode)
        {
            foreach (var kvp in _visibleItems)
            {
                bool selected = kvp.Value.VideoInfo.Path == video.Path;
                kvp.Value.SetItemSelected(selected);
            }
        }

        OnVideoSelected?.Invoke(video);
    }

    public void ClearSelection()
    {
        SelectedVideo = null;

        foreach (var kvp in _visibleItems)
        {
            kvp.Value.SetItemSelected(false);
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
        GoToPage(page, animate: true);
    }
    
    /// <summary>
    /// Go to page instantly without animation. Used when switching categories.
    /// </summary>
    public void GoToPageInstant(int page)
    {
        GoToPage(page, animate: false);
    }
    
    private void GoToPage(int page, bool animate)
    {
        page = Mathf.Clamp(page, 1, TotalPages);
        
        // Smart page jump: for large jumps, use instant scroll to avoid binding intermediate items
        const int LARGE_JUMP_THRESHOLD = 5;
        int pageDistance = Mathf.Abs(page - CurrentPage);
        bool isLargeJump = pageDistance > LARGE_JUMP_THRESHOLD;
        
        // Force instant scroll for large jumps (Mobile optimization)
        bool shouldAnimate = animate && !isLargeJump;
        
        if (page != CurrentPage || !animate)
        {
            // Clear pending binds when doing large jump to avoid wasting CPU on intermediate positions
            if (isLargeJump)
            {
                _pendingBindIndices.Clear();
                if (_progressiveBindCoroutine != null)
                {
                    StopCoroutine(_progressiveBindCoroutine);
                    _progressiveBindCoroutine = null;
                }
            }
            
            CurrentPage = page;
            ScrollToPage(page, shouldAnimate);
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
    /// Calculate page layouts based on new rules:
    /// Step 1: Calculate full scroll height from groups (Header + Grid per group)
    /// Step 2: Divide by viewport height to get page count (round up if remainder)
    /// </summary>
    private void CalculatePageLayout()
    {
        _pageLayouts.Clear();

        if (_allVideos.Count == 0)
        {
            TotalPages = 1;
            return;
        }

        // Step 1: Calculate full scroll height
        float fullScrollHeight = CalculateFullScrollHeight();
        
        // Step 2: Calculate page count
        // Divide by viewport height (_height = body area), round up if remainder
        TotalPages = Mathf.Max(1, Mathf.CeilToInt(fullScrollHeight / _height));
        
        // Generate simple page layouts for scroll positions
        float contentHeight = fullScrollHeight;
        float viewportHeight = _height;
        float scrollRange = Mathf.Max(0, contentHeight - viewportHeight);
        
        for (int i = 0; i < TotalPages; i++)
        {
            float startY;
            if (TotalPages == 1)
            {
                startY = 0;
            }
            else
            {
                // Proportional scroll position for each page
                startY = scrollRange * ((float)i / (TotalPages - 1));
            }
            
            var page = new PageLayoutInfo
            {
                PageNumber = i + 1,
                StartY = startY,
                FirstItemIndex = 0,
                LastItemIndex = _allVideos.Count - 1,
                TotalHeight = viewportHeight
            };
            _pageLayouts.Add(page);
        }
        
        // Debug.Log($"[RTTMediaGrid] CalculatePageLayout: fullScrollHeight={fullScrollHeight}, viewportHeight={_height}, pages={TotalPages}, groups={_groupData.Count}, items={_allVideos.Count}");
    }

    /// <summary>
    /// Calculate full scroll content height based on groups.
    /// Each group = Header height (GROUP_HEADER_HEIGHT) + Grid height (rows based on items)
    /// Uses _columnsPerRow (hardcoded to 4)
    /// </summary>
    private float CalculateFullScrollHeight()
    {
        // Use the class field _columnsPerRow which is set to 4 in CalculateGridMetrics
        
        float totalHeight = _paddingTop;
        
        if (_groupData.Count > 0)
        {
            foreach (var gd in _groupData)
            {
                // Add header height
                totalHeight += GROUP_HEADER_HEIGHT + _spacingY;
                
                // Calculate grid height: items / _columnsPerRow, round up for remainder
                int rowCount = Mathf.CeilToInt((float)gd.Info.Count / _columnsPerRow);
                totalHeight += rowCount * _rowHeight;
            }
        }
        else
        {
            // No groups - simple calculation
            int totalRows = Mathf.CeilToInt((float)_allVideos.Count / _columnsPerRow);
            totalHeight += totalRows * _rowHeight;
        }
        
        totalHeight += _paddingBottom;
        return totalHeight;
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
        UpdateVisibleGroupHeaders();  // FIX: Update group headers when scrolling
        UpdateStickyHeader();
    }

    /// <summary>
    /// Update the sticky header based on current scroll position.
    /// DISABLED: Headers now just scroll with content, no sticky behavior.
    /// </summary>
    private void UpdateStickyHeader()
    {
        // DISABLED - sticky header is no longer used
        if (_stickyHeaderContainer != null)
        {
            _stickyHeaderContainer.gameObject.SetActive(false);
        }
        return;
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
            // Debug.Log($"[RTTMediaGrid] UpdateVisibleItems (ALL): indices=0-{lastVisibleIndex}, total={_allVideos.Count}");
        }
        else
        {
            // For large datasets with groups, use Y-position based virtualization
            // This properly accounts for group headers which affect item positions
            float scrollY = _contentRect.anchoredPosition.y;
            float viewportTop = scrollY;
            float viewportBottom = scrollY + _height;
            float buffer = _rowHeight * _bufferRows;
            
            // Find first visible item by scanning from start
            // Use binary search optimization if needed for very large datasets
            firstVisibleIndex = 0;
            for (int i = 0; i < _allVideos.Count; i++)
            {
                float itemY = Mathf.Abs(GetYPositionForIndex(i)); // Y is negative in Unity UI
                float itemBottom = itemY + _cellHeight;
                
                // Item is visible if its bottom is below viewport top (with buffer)
                if (itemBottom >= viewportTop - buffer)
                {
                    firstVisibleIndex = i;
                    break;
                }
            }
            
            // Find last visible item
            lastVisibleIndex = _allVideos.Count - 1;
            for (int i = firstVisibleIndex; i < _allVideos.Count; i++)
            {
                float itemY = Mathf.Abs(GetYPositionForIndex(i));
                
                // Item is not visible if its top is below viewport bottom (with buffer)
                if (itemY > viewportBottom + buffer)
                {
                    lastVisibleIndex = i - 1;
                    break;
                }
            }
            
            // Clamp to valid range
            firstVisibleIndex = Mathf.Max(0, firstVisibleIndex);
            lastVisibleIndex = Mathf.Min(lastVisibleIndex, _allVideos.Count - 1);
            
            // Debug.Log($"[RTTMediaGrid] UpdateVisibleItems (VIRTUAL): scrollY={scrollY}, viewport={viewportTop}-{viewportBottom}, indices={firstVisibleIndex}-{lastVisibleIndex}, total={_allVideos.Count}");
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
                // Debug.Log($"[RTTMediaGrid] Queuing {addedCount} NEW items for binding (total pending={_pendingBindIndices.Count})");
            }

            // Start progressive binding if not already running
            if (_progressiveBindCoroutine == null)
            {
                _progressiveBindCoroutine = StartCoroutine(ProgressiveBindCoroutine());
            }
        }
        else if (_progressiveBindCoroutine == null)
        {
            // Debug.Log($"[RTTMediaGrid] No items to bind (all {_visibleItems.Count} already visible)");
        }
    }

    /// <summary>
    /// Progressively bind items using frame-time budget system.
    /// Mobile-optimized: stops binding when frame budget exhausted to maintain smooth FPS.
    /// </summary>
    private IEnumerator ProgressiveBindCoroutine()
    {
        int totalBound = 0;
        int totalSkipped = 0;
        
        // Mobile-first: use tighter budget on mobile to prevent thermal throttling
        float frameBudgetMs = Application.isMobilePlatform ? MOBILE_FRAME_BUDGET_MS : DESKTOP_FRAME_BUDGET_MS;

        // Debug.Log($"[RTTMediaGrid] ProgressiveBindCoroutine started, pending={_pendingBindIndices.Count}, budgetMs={frameBudgetMs}");

        // Render group headers immediately (they don't need progressive loading)
        UpdateVisibleGroupHeaders();

        while (_pendingBindIndices.Count > 0)
        {
            float frameStartTime = Time.realtimeSinceStartup * 1000f;

            while (_pendingBindIndices.Count > 0)
            {
                // Check frame budget before each bind operation
                float elapsedMs = Time.realtimeSinceStartup * 1000f - frameStartTime;
                if (elapsedMs >= frameBudgetMs)
                {
                    // Budget exhausted, continue next frame
                    break;
                }
                
                int idx = _pendingBindIndices.Dequeue();

                // Skip if already bound or index out of range
                if (_visibleItems.ContainsKey(idx))
                {
                    totalSkipped++;
                    continue;
                }
                if (idx >= _allVideos.Count)
                {
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
                        totalBound++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[RTTMediaGrid] Error binding index {idx}: {ex.Message}");
                    }
                }
            }

            // Wait for next frame before binding more
            yield return null;
        }

        // Debug.Log($"[RTTMediaGrid] ProgressiveBindCoroutine complete: bound={totalBound}, skipped={totalSkipped}");

        // Validate: check if any items in the expected range are missing
        if (_visibleItems.Count < _allVideos.Count)
        {
            // Find missing indices
            for (int i = 0; i < _allVideos.Count; i++)
            {
                if (!_visibleItems.ContainsKey(i))
                {
                    // Debug.LogWarning($"[RTTMediaGrid] Missing item at index {i}: {_allVideos[i].Title}");
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
        
        // DEBUG: Log group data (commented out for performance)
        // Debug.Log($"[RTTMediaGrid] UpdateVisibleGroupHeaders: groupCount={_groupData.Count}, scrollY={scrollY}, viewportTop={viewportTop}, viewportBottom={viewportBottom}");
        // for (int dbgIdx = 0; dbgIdx < _groupData.Count; dbgIdx++)
        // {
        //     var dbgGd = _groupData[dbgIdx];
        //     Debug.Log($"  Group[{dbgIdx}]: YOffset={dbgGd.YOffset}, ContentHeight={dbgGd.ContentHeight}, items={dbgGd.Info.Count}");
        // }

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

        // Debug.Log($"[RTTMediaGrid] BindItemAtIndex: index={index}, title={video.Title}, col={col}, pos=({x:F0},{y:F0})");

        // Bind data
        item.Bind(video);

        // Restore edit mode and checkbox selection state
        item.SetEditMode(_isEditMode);
        item.SetSelected(_selectedPaths.Contains(video.Path));

        // Restore item selection state (non-edit mode visual selection)
        bool isItemSelected = SelectedVideo.HasValue && SelectedVideo.Value.Path == video.Path;
        item.SetItemSelected(isItemSelected);

        item.gameObject.SetActive(true);
    }

    private Coroutine _scrollCoroutine;

    private void ScrollToPage(int pageIndex, bool animate = true)
    {
        if (_scrollRect == null || _contentRect == null) return;

        Canvas.ForceUpdateCanvases();

        float contentHeight = _contentRect.rect.height;
        float viewportHeight = _scrollRect.viewport.rect.height;
        float maxScrollY = Mathf.Max(0, contentHeight - viewportHeight);

        float targetY;

        // Step 3: Special scroll rules
        if (TotalPages <= 1)
        {
            // Only one page - scroll to top
            targetY = 0;
        }
        else if (pageIndex == 1)
        {
            // First page: scroll to top
            targetY = 0;
        }
        else if (pageIndex >= TotalPages)
        {
            // Last page: scroll to bottom
            targetY = maxScrollY;
        }
        else
        {
            // Middle pages: use pre-calculated layout positions
            if (_pageLayouts.Count > 0 && pageIndex >= 1 && pageIndex <= _pageLayouts.Count)
            {
                targetY = _pageLayouts[pageIndex - 1].StartY;
            }
            else
            {
                // Fallback: proportional scroll
                targetY = maxScrollY * ((float)(pageIndex - 1) / (TotalPages - 1));
            }
        }

        targetY = Mathf.Clamp(targetY, 0, maxScrollY);

        // Update sticky header
        UpdateStickyHeader();

        if (_scrollCoroutine != null) StopCoroutine(_scrollCoroutine);
        
        if (animate)
        {
            _scrollCoroutine = StartCoroutine(SmoothScroll(targetY, 0.3f));
        }
        else
        {
            // Instant scroll - no animation
            _contentRect.anchoredPosition = new Vector2(_contentRect.anchoredPosition.x, targetY);
            _scrollCoroutine = null;
            
            // Force update visible items immediately
            UpdateVisibleItems();
            UpdateVisibleGroupHeaders();
        }
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
    private void OnItemHoverEnter(string path)
    {
        var video = _allVideos.Find(v => v.Path == path);
        if (!string.IsNullOrEmpty(video.Path))
        {
            OnVideoHoverEnter?.Invoke(video);
        }
    }

    private void OnItemHoverExit(string path)
    {
        var video = _allVideos.Find(v => v.Path == path);
        if (!string.IsNullOrEmpty(video.Path))
        {
            OnVideoHoverExit?.Invoke(video);
        }
    }

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

        // When entering edit mode, clear visual selection (but keep SelectedVideo for later)
        if (editMode && SelectedVideo.HasValue)
        {
            foreach (var kvp in _visibleItems)
            {
                if (kvp.Value.VideoInfo.Path == SelectedVideo.Value.Path)
                {
                    kvp.Value.SetItemSelected(false);
                    break;
                }
            }
        }

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

        // Clear checkbox selection when exiting edit mode
        if (!editMode)
        {
            _selectedPaths.Clear();

            // Restore visual selection if there was one
            if (SelectedVideo.HasValue)
            {
                foreach (var kvp in _visibleItems)
                {
                    if (kvp.Value.VideoInfo.Path == SelectedVideo.Value.Path)
                    {
                        kvp.Value.SetItemSelected(true);
                        break;
                    }
                }
            }
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

    /// <summary>
    /// Get the first video in the current data set.
    /// </summary>
    public MediaVideoInfo? GetFirstVideo()
    {
        if (_allVideos.Count > 0)
        {
            return _allVideos[0];
        }
        return null;
    }

    /// <summary>
    /// Find the index of a video by its path.
    /// </summary>
    public int GetVideoIndex(string path)
    {
        if (string.IsNullOrEmpty(path)) return -1;
        return _allVideos.FindIndex(v => v.Path == path);
    }

    /// <summary>
    /// Select a video by its path.
    /// </summary>
    public void SelectVideoByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var video = _allVideos.Find(v => v.Path == path);
        if (!string.IsNullOrEmpty(video.Path))
        {
            SelectVideo(video);
        }
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
