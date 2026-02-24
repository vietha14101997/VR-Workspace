using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using TMPro;
using System;
using VRWorkspace.UI.Utilities;
using VRWorkspace.UI.RTT.Controllers;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// Virtualized List View for File Manager.
    /// Displays files in rows with columns: Name, Type, Created, Modified, Duration, Size
    /// Uses object pooling for performance.
    /// Pure display component - interaction logic will be added separately.
    /// </summary>
    public class RTTFileList : MonoBehaviour
    {
        private RTTFileManagerController _controller;
        private float _width;
        private float _height;
        private TMP_FontAsset _font;
        private Color _primaryColor;
        private Color _accentColor;

        private ScrollRect _scrollRect;
        private RectTransform _contentRect;
        private RectTransform _viewportRect;
        private RectTransform _headerRect;

        // Data
        private List<MockFile> _allFiles = new List<MockFile>();

        // Item Selection State (non-edit mode visual selection)
        private string _selectedFilePath = "";

        // Pool of reusable items
        private List<RTTFileListItem> _itemPool = new List<RTTFileListItem>();
        private Dictionary<int, RTTFileListItem> _visibleItems = new Dictionary<int, RTTFileListItem>();

        // List configuration
        private float _rowHeight = 60f;
        private float _headerHeight = 68f;
        private float _topGap = 20f;
        private float _paddingTop = 5f;
        private float _paddingBottom = 5f;

        // Calculated values
        private int _visibleRowCount;
        private int _bufferRows = 3;

        // Reference to scroll container
        private RectTransform _scrollContainerRT;

        // Sort callback
        private Action<string> _onSortColumnClicked;

        // Interaction callbacks
        private Action<string> _onItemHoverEnter;
        private Action<string> _onItemHoverExit;
        private Action<string, bool> _onItemClick; // path, isFolder

        // Current sort state (for header arrows)
        private string _currentSortBy = "Name";
        private bool _isAscending = true;

        // Header column references for sort arrows
        private Dictionary<string, Image> _sortArrows = new Dictionary<string, Image>();

        public void Initialize(RTTFileManagerController controller, float w, float h, TMP_FontAsset font, Color primaryColor, Color accentColor, Action<string> onSortColumnClicked)
        {
            _controller = controller;
            _width = w;
            _height = h;
            _font = font;
            _primaryColor = primaryColor;
            _accentColor = accentColor;
            _onSortColumnClicked = onSortColumnClicked;

            BuildUI();
            CalculateMetrics();
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

        private void BuildUI()
        {
            // Main container setup
            RectTransform mainRT = GetComponent<RectTransform>();
            mainRT.anchorMin = Vector2.zero;
            mainRT.anchorMax = Vector2.one;
            mainRT.offsetMin = Vector2.zero;
            mainRT.offsetMax = Vector2.zero;

            // 1. Create ScrollRect container FIRST (so it renders behind header)
            GameObject scrollContainer = new GameObject("ScrollContainer");
            scrollContainer.transform.SetParent(transform, false);

            _scrollContainerRT = scrollContainer.AddComponent<RectTransform>();
            _scrollContainerRT.anchorMin = Vector2.zero;
            _scrollContainerRT.anchorMax = Vector2.one;
            _scrollContainerRT.offsetMin = Vector2.zero;
            _scrollContainerRT.offsetMax = new Vector2(0, -_topGap - _headerHeight);

            // Setup ScrollRect
            _scrollRect = scrollContainer.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.scrollSensitivity = 20f;
            _scrollRect.movementType = ScrollRect.MovementType.Elastic;
            _scrollRect.onValueChanged.AddListener(OnScrollChanged);

            // Viewport
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollContainer.transform, false);
            _viewportRect = viewport.AddComponent<RectTransform>();
            _viewportRect.anchorMin = Vector2.zero;
            _viewportRect.anchorMax = Vector2.one;
            _viewportRect.offsetMin = Vector2.zero;
            _viewportRect.offsetMax = Vector2.zero;

            viewport.AddComponent<RectMask2D>();

            _scrollRect.viewport = _viewportRect;

            // Content
            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            _contentRect = content.AddComponent<RectTransform>();
            _contentRect.anchorMin = new Vector2(0, 1);
            _contentRect.anchorMax = new Vector2(1, 1);
            _contentRect.pivot = new Vector2(0.5f, 1);
            _contentRect.sizeDelta = Vector2.zero;

            _scrollRect.content = _contentRect;

            // 2. Create Header LAST (so it renders on top of scroll content)
            CreateHeader();
        }

        private void CreateHeader()
        {
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(transform, false);

            _headerRect = headerObj.AddComponent<RectTransform>();
            _headerRect.anchorMin = new Vector2(0, 1);
            _headerRect.anchorMax = new Vector2(1, 1);
            _headerRect.pivot = new Vector2(0.5f, 1);
            _headerRect.anchoredPosition = new Vector2(0, -_topGap);
            _headerRect.sizeDelta = new Vector2(0, _headerHeight);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0f, 0f, 0f, 0.3f);

            string[] columnNames = { "Name", "Type", "Created", "Modified", "Duration", "Size" };
            float usableWidth = _width - RTTFileListItem.RowPadding * 2;
            float currentX = RTTFileListItem.RowPadding;

            for (int i = 0; i < columnNames.Length; i++)
            {
                float colWidth = usableWidth * RTTFileListItem.ColumnWidths[i];
                bool isFirst = (i == 0);
                bool isLast = (i == columnNames.Length - 1);

                float bgStartX = currentX;
                float bgWidth = colWidth;

                if (isFirst)
                {
                    bgStartX = 0;
                    bgWidth = currentX + colWidth;
                }

                if (isLast)
                {
                    bgWidth = _width - currentX;
                }

                float textLeftPadding = 5f;
                if (isFirst)
                {
                    textLeftPadding = RTTFileListItem.IconWidth + 10f + 5f - currentX;
                }

                CreateHeaderColumn(columnNames[i], bgStartX, bgWidth, textLeftPadding, isLast);
                currentX += colWidth;
            }

            // Bottom border line
            GameObject borderObj = new GameObject("BorderLine");
            borderObj.transform.SetParent(headerObj.transform, false);

            RectTransform borderRT = borderObj.AddComponent<RectTransform>();
            borderRT.anchorMin = new Vector2(0, 0);
            borderRT.anchorMax = new Vector2(1, 0);
            borderRT.pivot = new Vector2(0.5f, 0);
            borderRT.anchoredPosition = Vector2.zero;
            borderRT.sizeDelta = new Vector2(0, 1f);

            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.color = new Color(1f, 1f, 1f, 0.2f);
        }

        private void CreateHeaderColumn(string columnName, float bgStartX, float bgWidth, float textLeftPadding, bool isLast)
        {
            GameObject colObj = new GameObject($"Header_{columnName}");
            colObj.transform.SetParent(_headerRect, false);

            RectTransform colRT = colObj.AddComponent<RectTransform>();
            colRT.anchorMin = new Vector2(0, 0);
            colRT.anchorMax = new Vector2(0, 1);
            colRT.pivot = new Vector2(0, 0.5f);
            colRT.sizeDelta = new Vector2(bgWidth, 0);
            colRT.anchoredPosition = new Vector2(bgStartX, 0);

            Image colBg = colObj.AddComponent<Image>();
            colBg.color = Color.clear;
            colBg.raycastTarget = true;

            Button colBtn = colObj.AddComponent<Button>();
            colBtn.targetGraphic = colBg;
            colBtn.transition = Selectable.Transition.None;

            string colName = columnName;
            colBtn.onClick.AddListener(() => OnHeaderColumnClicked(colName));

            // Hover effect
            EventTrigger trigger = colObj.AddComponent<EventTrigger>();

            EventTrigger.Entry enterEntry = new EventTrigger.Entry();
            enterEntry.eventID = EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((data) => { colBg.color = new Color(1f, 1f, 1f, 0.1f); });
            trigger.triggers.Add(enterEntry);

            EventTrigger.Entry exitEntry = new EventTrigger.Entry();
            exitEntry.eventID = EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((data) => { colBg.color = Color.clear; });
            trigger.triggers.Add(exitEntry);

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(colObj.transform, false);

            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(textLeftPadding, 0);
            textRT.offsetMax = new Vector2(-5f, 0);

            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = columnName;
            text.font = _font;
            text.fontSize = 22;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            // Sort arrow (positioned right after text)
            GameObject arrowObj = new GameObject("Arrow");
            arrowObj.transform.SetParent(colObj.transform, false);

            RectTransform arrowRT = arrowObj.AddComponent<RectTransform>();
            arrowRT.sizeDelta = new Vector2(14f, 14f);

            // Estimate text width for positioning arrow right after text
            float estimatedTextWidth;
            switch (columnName)
            {
                case "Name": estimatedTextWidth = 80f; break;
                case "Type": estimatedTextWidth = 65f; break;
                case "Created": estimatedTextWidth = 100f; break;
                case "Modified": estimatedTextWidth = 110f; break;
                case "Duration": estimatedTextWidth = 105f; break;
                case "Size": estimatedTextWidth = 55f; break;
                default: estimatedTextWidth = columnName.Length * 16f; break;
            }

            // Text area is shifted by textLeftPadding on left and -5 on right
            // Text visual center offset from column center = (textLeftPadding - 5) / 2
            float textCenterOffset = (textLeftPadding - 5f) / 2f;

            // Arrow anchored to column center, positioned right after centered text
            // Use center pivot (0.5, 0.5) so rotation doesn't shift visual position
            arrowRT.anchorMin = new Vector2(0.5f, 0.5f);
            arrowRT.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRT.pivot = new Vector2(0.5f, 0.5f);
            // Position: text center offset + half text width + gap + half arrow width
            float arrowX = textCenterOffset + (estimatedTextWidth / 2f) + 12f + 7f;
            arrowRT.anchoredPosition = new Vector2(arrowX, 0);

            Image arrowImg = arrowObj.AddComponent<Image>();
            arrowImg.sprite = SpriteUtility.GetArrowSprite();
            arrowImg.color = Color.white;
            arrowImg.raycastTarget = false;

            _sortArrows[columnName] = arrowImg;
            arrowImg.enabled = (columnName == _currentSortBy);

            // NOTE: No BoxCollider needed - RTT uses GraphicRaycaster via panel's DisplayQuad collider
        }

        private void OnHeaderColumnClicked(string columnName)
        {
            _onSortColumnClicked?.Invoke(columnName);
        }

        public void UpdateSortState(string sortBy, bool ascending)
        {
            _currentSortBy = sortBy;
            _isAscending = ascending;

            foreach (var kvp in _sortArrows)
            {
                if (kvp.Key == sortBy)
                {
                    kvp.Value.enabled = true;
                    kvp.Value.rectTransform.localEulerAngles = new Vector3(0, 0, ascending ? 180f : 0f);
                }
                else
                {
                    kvp.Value.enabled = false;
                }
            }
        }

        private void CalculateMetrics()
        {
            float contentHeight = _height - _topGap - _headerHeight;
            _rowHeight = contentHeight / 6.5f;
            _visibleRowCount = Mathf.CeilToInt(contentHeight / _rowHeight) + 1;
        }

        /// <summary>
        /// Returns the number of complete rows that fit in the viewport for pagination purposes.
        /// </summary>
        public int GetVisibleRowsForPagination()
        {
            float contentHeight = _height - _topGap - _headerHeight - _paddingTop - _paddingBottom;
            int visibleRows = Mathf.Max(1, Mathf.FloorToInt(contentHeight / _rowHeight));
            Debug.Log($"[RTTFileList] GetVisibleRowsForPagination: height={_height}, contentHeight={contentHeight}, rowHeight={_rowHeight}, visibleRows={visibleRows}");
            return visibleRows;
        }

        private void CreateItemPool()
        {
            int poolSize = _visibleRowCount + _bufferRows * 2;

            for (int i = 0; i < poolSize; i++)
            {
                var item = CreatePooledItem();
                item.gameObject.SetActive(false);
                _itemPool.Add(item);
            }
        }

        private RTTFileListItem CreatePooledItem()
        {
            GameObject itemObj = new GameObject("PooledListItem");
            itemObj.transform.SetParent(_contentRect, false);

            var rect = itemObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(_width, _rowHeight);

            var listItem = itemObj.AddComponent<RTTFileListItem>();
            listItem.Initialize(_font);

            // Set callbacks if already configured
            if (_onItemHoverEnter != null || _onItemHoverExit != null || _onItemClick != null)
            {
                listItem.SetCallbacks(_onItemHoverEnter, _onItemHoverExit, _onItemClick);
            }

            // Set selection callback for edit mode
            listItem.SetSelectionCallback(OnItemSelectionChanged);

            return listItem;
        }

        public void Populate(List<MockFile> files, string selectedPath = "")
        {
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

            // Render visible items
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
            float contentHeight = _paddingTop + _allFiles.Count * _rowHeight + _paddingBottom;
            float availableHeight = _height - _headerHeight;
            contentHeight = Mathf.Max(contentHeight, availableHeight);
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
            lastVisibleRow = Mathf.Min(lastVisibleRow, _allFiles.Count - 1);

            // Find items no longer visible
            List<int> toRemove = new List<int>();
            foreach (var kvp in _visibleItems)
            {
                if (kvp.Key < firstVisibleRow || kvp.Key > lastVisibleRow)
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

            // Show items that should be visible
            for (int i = firstVisibleRow; i <= lastVisibleRow && i < _allFiles.Count; i++)
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

        private RTTFileListItem GetPooledItem()
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

        private void BindItemAtIndex(RTTFileListItem item, int index)
        {
            var file = _allFiles[index];

            float y = -_paddingTop - index * _rowHeight - _rowHeight / 2f;

            var rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1);
            rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_width, _rowHeight);
            rect.anchoredPosition = new Vector2(0, y);

            item.Bind(file);

            // Restore edit mode and checkbox selection state
            item.SetEditMode(_isEditMode);
            item.SetSelected(_selectedPaths.Contains(file.Path));

            // Restore item selection state (non-edit mode visual selection)
            bool isItemSelected = !string.IsNullOrEmpty(_selectedFilePath) && _selectedFilePath == file.Path;
            item.SetItemSelected(isItemSelected);

            item.gameObject.SetActive(true);
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
        /// Get the visible item at a specific index (for external interaction handling)
        /// </summary>
        public RTTFileListItem GetVisibleItemAtIndex(int index)
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
        public IEnumerable<RTTFileListItem> GetVisibleItems()
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

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }

}
