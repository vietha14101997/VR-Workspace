using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Controllers;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// Virtualized Grid View for File Manager.
    /// Only renders visible items + buffer to maintain performance with large directories.
    /// Uses object pooling to reuse RTTFileGridItem instances.
    /// Handles hover/click interactions through callbacks.
    /// </summary>
    public class RTTFileGrid : MonoBehaviour
    {
        private RTTFileManagerController _controller;
        private float _width;
        private float _height;
        private TMP_FontAsset _font;

        private ScrollRect _scrollRect;
        private RectTransform _contentRect;
        private RectTransform _viewportRect;

        // Data
        private List<MockFile> _allFiles = new List<MockFile>();

        // Item Selection State (non-edit mode visual selection)
        private string _selectedFilePath = "";

        // Pool of reusable items
        private List<RTTFileGridItem> _itemPool = new List<RTTFileGridItem>();
        private Dictionary<int, RTTFileGridItem> _visibleItems = new Dictionary<int, RTTFileGridItem>();

        // Interaction callbacks
        private Action<string> _onItemHoverEnter;
        private Action<string> _onItemHoverExit;
        private Action<string, bool> _onItemClick; // path, isFolder

        // Grid configuration
        private float _cellWidth = 395f;
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

        // Progressive loading - bind items gradually to prevent frame lag
        private Coroutine _progressiveBindCoroutine;
        private Queue<int> _pendingBindIndices = new Queue<int>();
        private const float FRAME_BUDGET_MS = 6f;          // Increased budget (was 3ms)
        private const int INITIAL_SYNC_BIND_COUNT = 12;    // Bind 12 items sync to fill initial view (3 rows)

        public void Initialize(RTTFileManagerController controller, float w, float h, TMP_FontAsset font = null)
        {
            _controller = controller;
            _width = w;
            _height = h;
            _font = font;

            BuildUI();
            CalculateGridMetrics();
            CreateItemPool();
        }

        /// <summary>
        /// Set callbacks for item interactions
        /// </summary>
        public void SetItemCallbacks(Action<string> onHoverEnter, Action<string> onHoverExit, Action<string, bool> onClick)
        {
            _onItemHoverEnter = onHoverEnter;
            _onItemHoverExit = onHoverExit;
            _onItemClick = onClick;

            // Update existing items with callbacks
            foreach (var item in _itemPool)
            {
                item.SetCallbacks(_onItemHoverEnter, _onItemHoverExit, _onItemClick);
            }
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
        /// Returns the number of rows that fit in the viewport for pagination purposes.
        /// Uses round instead of floor to handle borderline cases where rows almost fit.
        /// </summary>
        public int GetVisibleRowsForPagination()
        {
            // Don't subtract bottom padding - it's for scroll aesthetics, not viewport capacity
            float availableHeight = _height - _paddingTop;
            float rowsFloat = availableHeight / _rowHeight;
            // Use Round to count partially visible rows (>=50% visible counts as full row)
            int visibleRows = Mathf.Max(1, Mathf.RoundToInt(rowsFloat));
            Debug.Log($"[RTTFileGrid] GetVisibleRowsForPagination: height={_height}, availableHeight={availableHeight}, rowHeight={_rowHeight}, rowsFloat={rowsFloat}, visibleRows={visibleRows}");
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
            gridItem.Initialize(_font);

            // Set callbacks if already configured
            if (_onItemHoverEnter != null || _onItemHoverExit != null || _onItemClick != null)
            {
                gridItem.SetCallbacks(_onItemHoverEnter, _onItemHoverExit, _onItemClick);
            }

            // Set selection callback for edit mode
            gridItem.SetSelectionCallback(OnItemSelectionChanged);

            return gridItem;
        }

        public void Populate(List<MockFile> files, string selectedPath = "")
        {
            // Stop any pending progressive binding (prevents binding stale data)
            if (_progressiveBindCoroutine != null)
            {
                StopCoroutine(_progressiveBindCoroutine);
                _progressiveBindCoroutine = null;
            }
            _pendingBindIndices.Clear();

            _allFiles = files ?? new List<MockFile>();

            // Store selected path for item selection
            _selectedFilePath = selectedPath ?? "";

            // Hide all visible items and cancel their thumbnail requests
            foreach (var kvp in _visibleItems)
            {
                kvp.Value.OnRecycle();
                kvp.Value.gameObject.SetActive(false);
            }
            _visibleItems.Clear();

            // Clean up orphaned cache when loading a new folder
            FileThumbnailService.Instance?.CleanupOrphanedCache();

            // Update content size
            UpdateContentSize();

            // Reset scroll position
            _scrollRect.verticalNormalizedPosition = 1f;

            // Render visible items (uses progressive loading)
            UpdateVisibleItems();
        }

        /// <summary>
        /// Select a file visually (non-edit mode).
        /// The selected item will have forced hover effects and won't respond to hover/click.
        /// In edit mode, visual selection is disabled.
        /// </summary>
        public void SelectFile(string path)
        {
            string previousPath = _selectedFilePath;
            _selectedFilePath = path ?? "";

            // In edit mode or clipboard mode, don't apply visual selection effect
            if (_isEditMode || _isClipboardMode) return;

            // Update previous selected item (if visible)
            if (!string.IsNullOrEmpty(previousPath))
            {
                foreach (var kvp in _visibleItems)
                {
                    if (kvp.Value.FilePath == previousPath)
                    {
                        kvp.Value.SetItemSelected(false);
                        break;
                    }
                }
            }

            // Update new selected item (if visible)
            if (!string.IsNullOrEmpty(_selectedFilePath))
            {
                foreach (var kvp in _visibleItems)
                {
                    if (kvp.Value.FilePath == _selectedFilePath)
                    {
                        kvp.Value.SetItemSelected(true);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Clear file selection (non-edit mode).
        /// </summary>
        public void ClearFileSelection()
        {
            SelectFile("");
        }

        /// <summary>
        /// Get the currently selected file path.
        /// </summary>
        public string GetSelectedFilePath()
        {
            return _selectedFilePath;
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
                    // Cancel any pending thumbnail requests when recycling
                    kvp.Value.OnRecycle();
                    kvp.Value.gameObject.SetActive(false);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var idx in toRemove)
            {
                _visibleItems.Remove(idx);
            }

            // Collect items that need to be bound
            List<int> toBind = new List<int>();
            for (int i = firstVisibleIndex; i <= lastVisibleIndex && i < _allFiles.Count; i++)
            {
                if (!_visibleItems.ContainsKey(i))
                {
                    toBind.Add(i);
                }
            }

            if (toBind.Count > 0)
            {
                // Bind first few items SYNCHRONOUSLY for immediate visibility
                int syncCount = Mathf.Min(INITIAL_SYNC_BIND_COUNT, toBind.Count);
                for (int i = 0; i < syncCount; i++)
                {
                    int idx = toBind[i];
                    var item = GetPooledItem();
                    if (item != null)
                    {
                        BindItemAtIndex(item, idx);
                        _visibleItems[idx] = item;
                    }
                }

                // Queue remaining items for progressive binding (reduces initial lag)
                HashSet<int> alreadyQueued = new HashSet<int>(_pendingBindIndices);
                for (int i = syncCount; i < toBind.Count; i++)
                {
                    int idx = toBind[i];
                    if (!alreadyQueued.Contains(idx) && !_visibleItems.ContainsKey(idx))
                    {
                        _pendingBindIndices.Enqueue(idx);
                    }
                }

                // Start progressive binding if not already running (and gameObject is active)
                if (_pendingBindIndices.Count > 0 && _progressiveBindCoroutine == null && gameObject.activeInHierarchy)
                {
                    _progressiveBindCoroutine = StartCoroutine(ProgressiveBindCoroutine());
                }
            }
        }

        /// <summary>
        /// Progressively bind items using frame-time budget system.
        /// Binds items until frame budget is exhausted, then continues next frame.
        /// </summary>
        private IEnumerator ProgressiveBindCoroutine()
        {
            int totalToBind = _pendingBindIndices.Count;
            int boundCount = 0;
            Debug.Log($"[RTTFileGrid] Progressive binding started: {totalToBind} items queued");

            while (_pendingBindIndices.Count > 0)
            {
                float frameStartTime = Time.realtimeSinceStartup * 1000f;
                int boundThisFrame = 0;

                while (_pendingBindIndices.Count > 0)
                {
                    // Check frame budget before each bind
                    float elapsedMs = Time.realtimeSinceStartup * 1000f - frameStartTime;
                    if (elapsedMs >= FRAME_BUDGET_MS)
                    {
                        // Budget exhausted, continue next frame
                        break;
                    }

                    int idx = _pendingBindIndices.Dequeue();

                    // Skip if already bound or out of range
                    if (_visibleItems.ContainsKey(idx) || idx >= _allFiles.Count)
                    {
                        continue;
                    }

                    var item = GetPooledItem();
                    if (item != null)
                    {
                        BindItemAtIndex(item, idx);
                        _visibleItems[idx] = item;
                        boundCount++;
                        boundThisFrame++;
                    }
                }

                if (boundThisFrame > 0)
                {
                    Debug.Log($"[RTTFileGrid] Progressive bind frame: {boundThisFrame} items, {_pendingBindIndices.Count} remaining");
                }

                yield return null;
            }

            Debug.Log($"[RTTFileGrid] Progressive binding complete: {boundCount} items bound total");
            _progressiveBindCoroutine = null;
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

            // Activate first so coroutines (like in MarqueeText) can run correctly
            item.gameObject.SetActive(true);

            // Bind data - pass full MockFile for thumbnail support
            item.Bind(file);

            // Restore edit mode and checkbox selection state
            item.SetEditMode(_isEditMode);
            item.SetSelected(_selectedPaths.Contains(file.Path));

            // Restore item selection state (non-edit mode visual selection)
            bool isItemSelected = !string.IsNullOrEmpty(_selectedFilePath) && _selectedFilePath == file.Path;
            item.SetItemSelected(isItemSelected);
        }

        public void ScrollToPage(int pageIndex, int rowsPerPage)
        {
            if (_scrollRect == null || _contentRect == null) return;

            // Skip if gameObject is inactive (can't start coroutine)
            if (!gameObject.activeInHierarchy) return;

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

        /// <summary>
        /// Get current scroll position (normalized 0-1, where 1 = top)
        /// </summary>
        public float GetScrollPosition()
        {
            if (_scrollRect == null) return 1f;
            return _scrollRect.verticalNormalizedPosition;
        }

        /// <summary>
        /// Set scroll position (normalized 0-1, where 1 = top)
        /// </summary>
        public void SetScrollPosition(float normalizedPosition)
        {
            if (_scrollRect == null) return;

            // Stop any ongoing scroll animation
            if (_scrollCoroutine != null)
            {
                StopCoroutine(_scrollCoroutine);
                _scrollCoroutine = null;
            }

            _scrollRect.verticalNormalizedPosition = normalizedPosition;
            UpdateVisibleItems();
        }
        #endregion

        #region Edit Mode
        private bool _isEditMode = false;
        private bool _isClipboardMode = false;
        private HashSet<string> _selectedPaths = new HashSet<string>();
        private Action _onSelectionChanged;

        public void SetSelectionChangedCallback(Action callback)
        {
            _onSelectionChanged = callback;
        }

        public void SetEditMode(bool editMode)
        {
            _isEditMode = editMode;

            // When entering edit mode, clear visual selection (but keep _selectedFilePath for later)
            if (editMode && !string.IsNullOrEmpty(_selectedFilePath))
            {
                foreach (var kvp in _visibleItems)
                {
                    if (kvp.Value.FilePath == _selectedFilePath)
                    {
                        kvp.Value.SetItemSelected(false);
                        break;
                    }
                }
            }

            // Update all pooled items
            foreach (var item in _itemPool)
            {
                item.SetEditMode(editMode);
            }

            // Clear checkbox selection when exiting edit mode
            if (!editMode)
            {
                _selectedPaths.Clear();

                // Restore visual selection if there was one (only if not in clipboard mode)
                if (!_isClipboardMode && !string.IsNullOrEmpty(_selectedFilePath))
                {
                    foreach (var kvp in _visibleItems)
                    {
                        if (kvp.Value.FilePath == _selectedFilePath)
                        {
                            kvp.Value.SetItemSelected(true);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Set clipboard mode state.
        /// In clipboard mode, visual selection is disabled (similar to edit mode).
        /// </summary>
        public void SetClipboardMode(bool clipboardMode)
        {
            _isClipboardMode = clipboardMode;

            // When entering clipboard mode, clear visual selection (but keep _selectedFilePath for later)
            if (clipboardMode && !string.IsNullOrEmpty(_selectedFilePath))
            {
                foreach (var kvp in _visibleItems)
                {
                    if (kvp.Value.FilePath == _selectedFilePath)
                    {
                        kvp.Value.SetItemSelected(false);
                        break;
                    }
                }
            }

            // When exiting clipboard mode, restore visual selection if there was one (only if not in edit mode)
            if (!clipboardMode && !_isEditMode && !string.IsNullOrEmpty(_selectedFilePath))
            {
                foreach (var kvp in _visibleItems)
                {
                    if (kvp.Value.FilePath == _selectedFilePath)
                    {
                        kvp.Value.SetItemSelected(true);
                        break;
                    }
                }
            }
        }

        public bool AreAllSelected()
        {
            if (_allFiles.Count == 0) return false;
            return _selectedPaths.Count == _allFiles.Count;
        }

        public void SetAllSelected(bool selected)
        {
            _selectedPaths.Clear();

            if (selected)
            {
                foreach (var file in _allFiles)
                {
                    _selectedPaths.Add(file.Path);
                }
            }

            // Update visible items
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

            // Update visible item if it exists
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

        #region Cleanup
        private void OnDestroy()
        {
            // Stop progressive binding coroutine
            if (_progressiveBindCoroutine != null)
            {
                StopCoroutine(_progressiveBindCoroutine);
                _progressiveBindCoroutine = null;
            }
            _pendingBindIndices.Clear();

            // Stop scroll animation
            if (_scrollCoroutine != null)
            {
                StopCoroutine(_scrollCoroutine);
                _scrollCoroutine = null;
            }
        }
        #endregion
    }

}
