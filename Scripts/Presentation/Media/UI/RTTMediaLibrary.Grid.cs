using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibrary partial: Grid/list display creation, layout methods, view data binding,
    /// public grid API (SetVideos, pagination, item count, category selection).
    /// </summary>
    public partial class RTTMediaLibrary
    {
        #region Center Grid Creation

        private IEnumerator CreateCenterGrid()
        {
            while (_menuFrame.ContentContainer == null) yield return null;

            foreach (Transform child in _menuFrame.ContentContainer)
            {
                if (child.name == "PlaceholderText") Destroy(child.gameObject);
            }

            Vector2 contentSize = _menuFrame.GetContentSize();
            float panelHeight = contentSize.y;

            float headerBaseHeight = panelHeight * 0.198f;
            _singleRowHeight = headerBaseHeight / 2f;

            if (_singleRowHeight < 88f)
            {
                _singleRowHeight = 88f;
            }

            _rowSpacing = _singleRowHeight * 0.1875f;
            _headerHeight2Rows = (_singleRowHeight * 2f) + _rowSpacing;

            float bottomPadding = panelHeight * 0.02f;

            // 1. Create Body Object (Grid Container)
            GameObject bodyObj = new GameObject("Body");
            bodyObj.transform.SetParent(_menuFrame.ContentContainer, false);
            _bodyRT = bodyObj.AddComponent<RectTransform>();
            _bodyRT.anchorMin = Vector2.zero;
            _bodyRT.anchorMax = Vector2.one;
            _bodyRT.offsetMax = new Vector2(0, -_headerHeight2Rows);
            _bodyRT.offsetMin = new Vector2(0, bottomPadding);

            bodyObj.AddComponent<RectMask2D>();

            // Add CanvasGroup for fade animations during category navigation
            _bodyCanvasGroup = bodyObj.AddComponent<CanvasGroup>();
            _bodyCanvasGroup.alpha = 0f; // Start hidden for smooth fade-in

            // 2. Create Header Container
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(_menuFrame.ContentContainer, false);
            _headerRT = headerObj.AddComponent<RectTransform>();
            _headerRT.anchorMin = new Vector2(0, 1);
            _headerRT.anchorMax = new Vector2(1, 1);
            _headerRT.pivot = new Vector2(0.5f, 1);
            _headerRT.anchoredPosition = Vector2.zero;
            _headerRT.sizeDelta = new Vector2(0, _headerHeight2Rows);

            // 3. Build Header Rows
            CreateHeaderRows(_headerRT);
            Debug.Log($"[RTTMediaLibrary] After CreateHeaderRows: _headerRT={_headerRT != null}, _breadcrumbContainer={_breadcrumbContainer != null}");

            // 4. Create Grid Inside Body
            GameObject gridObj = new GameObject("MediaGrid");
            gridObj.transform.SetParent(_bodyRT, false);
            RectTransform gridRT = gridObj.AddComponent<RectTransform>();
            gridRT.anchorMin = Vector2.zero;
            gridRT.anchorMax = Vector2.one;
            gridRT.offsetMin = Vector2.zero;
            gridRT.offsetMax = Vector2.zero;

            _grid = gridObj.AddComponent<RTTMediaGrid>();
            float bodyHeight = panelHeight - _headerHeight2Rows - bottomPadding;
            _grid.Initialize(_controller, contentSize.x, bodyHeight, _font, _primaryColor, _accentColor);

            // Wire grid events
            _grid.OnVideoSelected += OnGridVideoSelected;
            _grid.OnVideoDoubleClicked += OnGridVideoDoubleClicked;
            _grid.OnVideoHoverEnter += OnGridVideoHoverEnter;
            _grid.OnVideoHoverExit += OnGridVideoHoverExit;
            _grid.OnPageChanged += OnGridPageChanged;
            _grid.SetSelectionChangedCallback(OnSelectionChanged);

            // Render Order
            headerObj.transform.SetAsLastSibling();
            bodyObj.transform.SetAsFirstSibling();

            _viewReady = true;

            // Set initial breadcrumb and category (default to Videos)
            UpdateBreadcrumbForCategory("videos");

            _controller?.OnViewReady();

            StartCoroutine(InitializePageSizeDeferred());

            // Smooth fade-in of content after construction
            FadeInContent();

            Debug.Log("[RTTMediaLibrary] Center grid created");
        }

        private IEnumerator InitializePageSizeDeferred()
        {
            yield return null;

            int itemsPerPage = _grid?.ItemsPerPage ?? 8;
            _controller?.SetPageSize(itemsPerPage);
            Debug.Log($"[RTTMediaLibrary] InitializePageSizeDeferred: itemsPerPage={itemsPerPage}");
        }

        #endregion

        #region Public Grid API

        public void SetVideos(List<MediaVideoInfo> videos, List<MediaGroupInfo> groups = null)
        {
            if (_grid != null)
            {
                _grid.SetData(videos, groups);
            }
            int count = videos?.Count ?? 0;
            UpdateItemCount(count);
            UpdatePagination();
        }

        public void UpdateItemCount(int count)
        {
            if (_itemCountText != null)
            {
                _itemCountText.text = $"{count} item{(count != 1 ? "s" : "")}";
            }
        }

        public void UpdatePagination()
        {
            if (_grid == null || _pagination == null) return;
            _pagination.SetPage(_grid.CurrentPage, _grid.TotalPages);
        }

        public void SelectCategory(string categoryId)
        {
            if (_sidePanel != null)
            {
                _sidePanel.SelectItem(categoryId);
            }
        }

        #endregion
    }
}
