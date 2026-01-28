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
/// Uses object pooling to reuse RTTMediaGridItem2 instances.
/// </summary>
public class RTTMediaGrid2 : MonoBehaviour
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
    #endregion

    #region Object Pool
    private List<RTTMediaGridItem2> _itemPool = new List<RTTMediaGridItem2>();
    private Dictionary<int, RTTMediaGridItem2> _visibleItems = new Dictionary<int, RTTMediaGridItem2>();
    #endregion

    #region Grid Configuration
    // Synced with RTTFileGrid dimensions
    private float _cellWidth = RTTMediaGridItem2.CELL_WIDTH;
    private float _cellHeight = RTTMediaGridItem2.CELL_HEIGHT;
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
    }

    private void CalculateGridMetrics()
    {
        float availableWidth = _width - _paddingLeft - _paddingRight;
        _columnsPerRow = Mathf.Max(1, Mathf.FloorToInt((availableWidth + _spacingX) / (_cellWidth + _spacingX)));
        _rowHeight = _cellHeight + _spacingY;
        _visibleRowCount = Mathf.CeilToInt(_height / _rowHeight) + 1;

        Debug.Log($"[RTTMediaGrid2] CalculateGridMetrics: width={_width}, height={_height}, columnsPerRow={_columnsPerRow}, rowHeight={_rowHeight}, visibleRowCount={_visibleRowCount}, cellSize={_cellWidth}x{_cellHeight}");
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

    private RTTMediaGridItem2 CreatePooledItem()
    {
        GameObject itemObj = new GameObject("PooledItem");
        itemObj.transform.SetParent(_contentRect, false);

        var rect = itemObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(_cellWidth, _cellHeight);

        var gridItem = itemObj.AddComponent<RTTMediaGridItem2>();
        gridItem.Initialize(_font, _primaryColor, _accentColor);
        gridItem.SetCallbacks(_onItemHoverEnter, _onItemHoverExit, _onItemClick, _onItemDoubleClick);
        gridItem.SetSelectionCallback(OnItemSelectionChanged);

        return gridItem;
    }
    #endregion

    #region Public Methods
    public void SetData(List<MediaVideoInfo> videos)
    {
        // Stop any pending progressive binding (prevents binding stale data)
        if (_progressiveBindCoroutine != null)
        {
            StopCoroutine(_progressiveBindCoroutine);
            _progressiveBindCoroutine = null;
        }
        _pendingBindIndices.Clear();

        _allVideos = videos ?? new List<MediaVideoInfo>();

        // Hide all visible items
        foreach (var kvp in _visibleItems)
        {
            kvp.Value.OnRecycle();
            kvp.Value.gameObject.SetActive(false);
        }
        _visibleItems.Clear();

        // NOTE: Don't call CleanupOrphanedCache here - it's expensive and causes lag
        // Cache cleanup should be done periodically or when app closes, not on every category switch

        // Calculate pagination
        int itemsPerPage = ItemsPerPage;
        TotalPages = Mathf.Max(1, Mathf.CeilToInt((float)_allVideos.Count / itemsPerPage));
        CurrentPage = Mathf.Clamp(CurrentPage, 1, TotalPages);

        // Update content size
        UpdateContentSize();

        // Reset scroll
        _scrollRect.verticalNormalizedPosition = 1f;

        Debug.Log($"[RTTMediaGrid2] SetData: {_allVideos.Count} items, columnsPerRow={_columnsPerRow}, visibleRowCount={_visibleRowCount}, itemsPerPage={itemsPerPage}, totalPages={TotalPages}, gridHeight={_height}, rowHeight={_rowHeight}");

        // Render visible items (uses progressive loading to prevent frame drops)
        UpdateVisibleItems();
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
        float contentHeight = _paddingTop + totalRows * _rowHeight - _spacingY + _paddingBottom;
        contentHeight = Mathf.Max(contentHeight, _height);
        _contentRect.sizeDelta = new Vector2(0, contentHeight);
    }

    private void OnScrollChanged(Vector2 normalizedPos)
    {
        UpdateVisibleItems();
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
            Debug.Log($"[RTTMediaGrid2] UpdateVisibleItems (ALL): indices=0-{lastVisibleIndex}, total={_allVideos.Count}");
        }
        else
        {
            // For large datasets, use virtualization based on scroll position
            float scrollY = _contentRect.anchoredPosition.y;
            int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt((scrollY - _paddingTop) / _rowHeight) - _bufferRows);
            int lastVisibleRow = firstVisibleRow + _visibleRowCount + _bufferRows * 2;

            firstVisibleIndex = firstVisibleRow * _columnsPerRow;
            lastVisibleIndex = Mathf.Min((lastVisibleRow + 1) * _columnsPerRow - 1, _allVideos.Count - 1);
            Debug.Log($"[RTTMediaGrid2] UpdateVisibleItems (VIRTUAL): scrollY={scrollY}, rows={firstVisibleRow}-{lastVisibleRow}, indices={firstVisibleIndex}-{lastVisibleIndex}, total={_allVideos.Count}");
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
                Debug.Log($"[RTTMediaGrid2] Queuing {addedCount} NEW items for binding (total pending={_pendingBindIndices.Count})");
            }

            // Start progressive binding if not already running
            if (_progressiveBindCoroutine == null)
            {
                _progressiveBindCoroutine = StartCoroutine(ProgressiveBindCoroutine());
            }
        }
        else if (_progressiveBindCoroutine == null)
        {
            Debug.Log($"[RTTMediaGrid2] No items to bind (all {_visibleItems.Count} already visible)");
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

        Debug.Log($"[RTTMediaGrid2] ProgressiveBindCoroutine started, pending={_pendingBindIndices.Count}, bindsPerFrame={maxBindsPerFrame}");

        while (_pendingBindIndices.Count > 0)
        {
            int bindCount = 0;

            while (_pendingBindIndices.Count > 0 && bindCount < maxBindsPerFrame)
            {
                int idx = _pendingBindIndices.Dequeue();

                // Skip if already bound or index out of range
                if (_visibleItems.ContainsKey(idx))
                {
                    Debug.Log($"[RTTMediaGrid2] Skip index {idx}: already bound");
                    totalSkipped++;
                    continue;
                }
                if (idx >= _allVideos.Count)
                {
                    Debug.LogWarning($"[RTTMediaGrid2] Skip index {idx}: out of range (count={_allVideos.Count})");
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
                        Debug.LogError($"[RTTMediaGrid2] Error binding index {idx}: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[RTTMediaGrid2] GetPooledItem returned null for index {idx}");
                }
            }

            // Wait for next frame before binding more
            yield return null;
        }

        Debug.Log($"[RTTMediaGrid2] ProgressiveBindCoroutine complete: bound={totalBound}, skipped={totalSkipped}, visibleItems={_visibleItems.Count}, totalItems={_allVideos.Count}");

        // Validate: check if any items in the expected range are missing
        if (_visibleItems.Count < _allVideos.Count)
        {
            // Find missing indices
            for (int i = 0; i < _allVideos.Count; i++)
            {
                if (!_visibleItems.ContainsKey(i))
                {
                    Debug.LogWarning($"[RTTMediaGrid2] Missing item at index {i}: {_allVideos[i].Title}");
                }
            }
        }

        _progressiveBindCoroutine = null;

        // Notify listeners that binding is complete (e.g., to start background scan)
        OnBindingComplete?.Invoke();
    }

    private RTTMediaGridItem2 GetPooledItem()
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

    private void BindItemAtIndex(RTTMediaGridItem2 item, int index)
    {
        var video = _allVideos[index];

        // Calculate position
        int row = index / _columnsPerRow;
        int col = index % _columnsPerRow;

        float x = _paddingLeft + col * (_cellWidth + _spacingX) + _cellWidth / 2f;
        float y = -_paddingTop - row * _rowHeight - _cellHeight / 2f;

        // Center the grid horizontally
        float totalGridWidth = _columnsPerRow * _cellWidth + (_columnsPerRow - 1) * _spacingX;
        float offsetX = (_width - totalGridWidth - _paddingLeft - _paddingRight) / 2f;
        x += offsetX;

        var rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);

        Debug.Log($"[RTTMediaGrid2] BindItemAtIndex: index={index}, title={video.Title}, row={row}, col={col}, pos=({x:F0},{y:F0})");

        // Bind data
        item.Bind(video);

        // Restore edit mode and selection state
        item.SetEditMode(_isEditMode);
        item.SetSelected(_selectedPaths.Contains(video.Path));

        item.gameObject.SetActive(true);
    }

    private void ScrollToPage(int pageIndex)
    {
        if (_scrollRect == null || _contentRect == null) return;

        Canvas.ForceUpdateCanvases();

        int rowsPerPage = GetVisibleRowsForPagination();
        int targetRow = (pageIndex - 1) * rowsPerPage;
        float targetY = _paddingTop + targetRow * _rowHeight;

        if (pageIndex == 1) targetY = 0;

        float contentHeight = _contentRect.rect.height;
        float viewportHeight = _scrollRect.viewport.rect.height;
        float maxScrollY = Mathf.Max(0, contentHeight - viewportHeight);

        targetY = Mathf.Clamp(targetY, 0, maxScrollY);

        if (_scrollCoroutine != null) StopCoroutine(_scrollCoroutine);
        _scrollCoroutine = StartCoroutine(SmoothScroll(targetY, 0.3f));
    }

    private Coroutine _scrollCoroutine;

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
    public RTTMediaGridItem2 GetVisibleItemAtIndex(int index)
    {
        if (_visibleItems.TryGetValue(index, out var item))
        {
            return item;
        }
        return null;
    }

    public IEnumerable<RTTMediaGridItem2> GetVisibleItems()
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
