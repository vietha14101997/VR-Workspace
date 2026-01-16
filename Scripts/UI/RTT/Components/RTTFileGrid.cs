using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Virtualized Grid View for File Manager.
/// Only renders visible items + buffer to maintain performance with large directories.
/// Uses object pooling to reuse RTTFileGridItem instances.
/// Pure display component - interaction logic will be added separately.
/// </summary>
public class RTTFileGrid : MonoBehaviour
{
    private RTTFileManagerController _controller;
    private float _width;
    private float _height;

    private ScrollRect _scrollRect;
    private RectTransform _contentRect;
    private RectTransform _viewportRect;

    // Data
    private List<MockFile> _allFiles = new List<MockFile>();

    // Pool of reusable items
    private List<RTTFileGridItem> _itemPool = new List<RTTFileGridItem>();
    private Dictionary<int, RTTFileGridItem> _visibleItems = new Dictionary<int, RTTFileGridItem>();

    // Grid configuration
    private float _cellWidth = 310f;
    private float _cellHeight = 320f;
    private float _spacingX = 30f;
    private float _spacingY = 30f;
    private float _paddingLeft = 20f;
    private float _paddingRight = 20f;
    private float _paddingTop = 20f;
    private float _paddingBottom = 60f;

    // Calculated values
    private int _columnsPerRow;
    private float _rowHeight;
    private int _visibleRowCount;
    private int _bufferRows = 2;

    public void Initialize(RTTFileManagerController controller, float w, float h)
    {
        _controller = controller;
        _width = w;
        _height = h;

        BuildUI();
        CalculateGridMetrics();
        CreateItemPool();
    }

    /// <summary>
    /// Returns items per page based on actual columns per row.
    /// </summary>
    public int GetItemsPerPage(int rowsPerPage)
    {
        int itemsPerPage = rowsPerPage * _columnsPerRow;
        Debug.Log($"[RTTFileGrid] GetItemsPerPage: rowsPerPage={rowsPerPage}, columnsPerRow={_columnsPerRow}, result={itemsPerPage}");
        return itemsPerPage;
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

        Debug.Log($"[RTTFileGrid] CalculateGridMetrics: width={_width}, availableWidth={availableWidth}, columnsPerRow={_columnsPerRow}, cellWidth={_cellWidth}, spacingX={_spacingX}");
    }

    /// <summary>
    /// Returns the calculated columns per row.
    /// </summary>
    public int GetColumnsPerRow()
    {
        return _columnsPerRow;
    }

    /// <summary>
    /// Returns the number of complete rows that fit in the viewport for pagination purposes.
    /// This excludes buffer rows and uses floor calculation for accurate page counting.
    /// </summary>
    public int GetVisibleRowsForPagination()
    {
        float availableHeight = _height - _paddingTop - _paddingBottom;
        int visibleRows = Mathf.Max(1, Mathf.FloorToInt(availableHeight / _rowHeight));
        Debug.Log($"[RTTFileGrid] GetVisibleRowsForPagination: height={_height}, availableHeight={availableHeight}, rowHeight={_rowHeight}, visibleRows={visibleRows}");
        return visibleRows;
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

    private RTTFileGridItem CreatePooledItem()
    {
        GameObject itemObj = new GameObject("PooledItem");
        itemObj.transform.SetParent(_contentRect, false);

        var rect = itemObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(_cellWidth, _cellHeight);

        var gridItem = itemObj.AddComponent<RTTFileGridItem>();
        gridItem.Initialize();

        return gridItem;
    }

    public void Populate(List<MockFile> files, string selectedPath = "")
    {
        _allFiles = files ?? new List<MockFile>();

        // Hide all visible items
        foreach (var kvp in _visibleItems)
        {
            kvp.Value.gameObject.SetActive(false);
        }
        _visibleItems.Clear();

        // Update content size
        UpdateContentSize();

        // Reset scroll position
        _scrollRect.verticalNormalizedPosition = 1f;

        // Render visible items
        UpdateVisibleItems();
    }

    private void UpdateContentSize()
    {
        int totalRows = Mathf.CeilToInt((float)_allFiles.Count / _columnsPerRow);
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
        if (_allFiles.Count == 0) return;

        float scrollY = _contentRect.anchoredPosition.y;
        int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt((scrollY - _paddingTop) / _rowHeight) - _bufferRows);
        int lastVisibleRow = firstVisibleRow + _visibleRowCount + _bufferRows * 2;

        int firstVisibleIndex = firstVisibleRow * _columnsPerRow;
        int lastVisibleIndex = Mathf.Min((lastVisibleRow + 1) * _columnsPerRow - 1, _allFiles.Count - 1);

        // Find items no longer visible
        List<int> toRemove = new List<int>();
        foreach (var kvp in _visibleItems)
        {
            if (kvp.Key < firstVisibleIndex || kvp.Key > lastVisibleIndex)
            {
                kvp.Value.gameObject.SetActive(false);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var idx in toRemove)
        {
            _visibleItems.Remove(idx);
        }

        // Show items that should be visible
        for (int i = firstVisibleIndex; i <= lastVisibleIndex && i < _allFiles.Count; i++)
        {
            if (!_visibleItems.ContainsKey(i))
            {
                var item = GetPooledItem();
                if (item != null)
                {
                    BindItemAtIndex(item, i);
                    _visibleItems[i] = item;
                }
            }
        }
    }

    private RTTFileGridItem GetPooledItem()
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

    private void BindItemAtIndex(RTTFileGridItem item, int index)
    {
        var file = _allFiles[index];

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

        // Bind data
        item.Bind(file.Name, file.IsFolder, file.Path, file.IsFolderEmpty);
        item.gameObject.SetActive(true);
    }

    public void ScrollToPage(int pageIndex, int rowsPerPage)
    {
        if (_scrollRect == null || _contentRect == null) return;

        Canvas.ForceUpdateCanvases();

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

    private System.Collections.IEnumerator SmoothScroll(float targetY, float duration)
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

    #region Public API for accessing items
    /// <summary>
    /// Get the visible item at a specific index
    /// </summary>
    public RTTFileGridItem GetVisibleItemAtIndex(int index)
    {
        if (_visibleItems.TryGetValue(index, out var item))
        {
            return item;
        }
        return null;
    }

    /// <summary>
    /// Get all currently visible items
    /// </summary>
    public IEnumerable<RTTFileGridItem> GetVisibleItems()
    {
        return _visibleItems.Values;
    }

    /// <summary>
    /// Get the file at a specific index
    /// </summary>
    public MockFile? GetFileAtIndex(int index)
    {
        if (index >= 0 && index < _allFiles.Count)
        {
            return _allFiles[index];
        }
        return null;
    }
    #endregion
}
