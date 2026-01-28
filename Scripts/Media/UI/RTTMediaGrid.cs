using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Grid view for displaying video items in the media library.
/// Supports pagination and item pooling for performance.
/// </summary>
public class RTTMediaGrid : MonoBehaviour
{
    #region Constants
    private const float GRID_PADDING = 20f;
    private const float ITEM_SPACING_X = 20f;
    private const float ITEM_SPACING_Y = 20f;
    private const int COLUMNS = 3;
    private const int ROWS_VISIBLE = 2;
    #endregion

    #region Events
    public event Action<MediaVideoInfo> OnVideoSelected;
    public event Action<MediaVideoInfo> OnVideoDoubleClicked;
    public event Action<int, int> OnPageChanged;  // (currentPage, totalPages)
    #endregion

    #region Properties
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public int ItemsPerPage => COLUMNS * ROWS_VISIBLE;
    public MediaVideoInfo? SelectedVideo { get; private set; }
    #endregion

    #region Private Fields
    private RTTMediaLibraryController _controller;
    private float _width;
    private float _height;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private Transform _gridContainer;
    private List<RTTMediaGridItem> _items = new List<RTTMediaGridItem>();
    private List<MediaVideoInfo> _currentData = new List<MediaVideoInfo>();

    // Object pooling
    private Queue<RTTMediaGridItem> _itemPool = new Queue<RTTMediaGridItem>();
    #endregion

    #region Initialization
    public void Initialize(RTTMediaLibraryController controller, float w, float h,
        TMP_FontAsset font, Color primary, Color accent)
    {
        _controller = controller;
        _width = w;
        _height = h;
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;

        BuildUI();
    }

    private void BuildUI()
    {
        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        // Grid Container with GridLayoutGroup
        GameObject gridObj = new GameObject("GridContainer");
        gridObj.transform.SetParent(transform, false);

        var gridRT = gridObj.AddComponent<RectTransform>();
        gridRT.anchorMin = Vector2.zero;
        gridRT.anchorMax = Vector2.one;
        gridRT.offsetMin = new Vector2(GRID_PADDING, GRID_PADDING);
        gridRT.offsetMax = new Vector2(-GRID_PADDING, -GRID_PADDING);

        var gridLayout = gridObj.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(RTTMediaGridItem.ITEM_WIDTH, RTTMediaGridItem.ITEM_HEIGHT);
        gridLayout.spacing = new Vector2(ITEM_SPACING_X, ITEM_SPACING_Y);
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = COLUMNS;

        _gridContainer = gridObj.transform;

        // Pre-create pool of items
        for (int i = 0; i < ItemsPerPage + 2; i++)
        {
            var item = CreateGridItem();
            item.gameObject.SetActive(false);
            _itemPool.Enqueue(item);
        }
    }

    private RTTMediaGridItem CreateGridItem()
    {
        GameObject itemObj = new GameObject("GridItem");
        itemObj.transform.SetParent(_gridContainer, false);

        var item = itemObj.AddComponent<RTTMediaGridItem>();
        item.Initialize(_font, _primaryColor, _accentColor);

        // Wire events
        item.OnClicked += HandleItemClicked;
        item.OnDoubleClicked += HandleItemDoubleClicked;
        item.OnSelected += HandleItemSelected;

        return item;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Set data to display in grid.
    /// </summary>
    public void SetData(List<MediaVideoInfo> videos)
    {
        _currentData = videos ?? new List<MediaVideoInfo>();
        TotalPages = Mathf.CeilToInt((float)_currentData.Count / ItemsPerPage);
        TotalPages = Mathf.Max(1, TotalPages);

        // Ensure current page is valid
        CurrentPage = Mathf.Clamp(CurrentPage, 1, TotalPages);

        RefreshDisplay();
    }

    /// <summary>
    /// Go to specific page.
    /// </summary>
    public void GoToPage(int page)
    {
        page = Mathf.Clamp(page, 1, TotalPages);
        if (page != CurrentPage)
        {
            CurrentPage = page;
            RefreshDisplay();
            OnPageChanged?.Invoke(CurrentPage, TotalPages);
        }
    }

    /// <summary>
    /// Go to next page.
    /// </summary>
    public void NextPage()
    {
        GoToPage(CurrentPage + 1);
    }

    /// <summary>
    /// Go to previous page.
    /// </summary>
    public void PreviousPage()
    {
        GoToPage(CurrentPage - 1);
    }

    /// <summary>
    /// Select a video item.
    /// </summary>
    public void SelectVideo(MediaVideoInfo video)
    {
        SelectedVideo = video;

        // Update visual selection
        foreach (var item in _items)
        {
            bool selected = item.VideoInfo.Path == video.Path;
            item.SetSelected(selected);
        }

        OnVideoSelected?.Invoke(video);
    }

    /// <summary>
    /// Clear selection.
    /// </summary>
    public void ClearSelection()
    {
        SelectedVideo = null;

        foreach (var item in _items)
        {
            item.SetSelected(false);
        }
    }

    /// <summary>
    /// Refresh display without changing data.
    /// </summary>
    public void Refresh()
    {
        RefreshDisplay();
    }
    #endregion

    #region Private Methods
    private void RefreshDisplay()
    {
        // Return current items to pool
        foreach (var item in _items)
        {
            item.gameObject.SetActive(false);
            _itemPool.Enqueue(item);
        }
        _items.Clear();

        // Calculate page range
        int startIndex = (CurrentPage - 1) * ItemsPerPage;
        int endIndex = Mathf.Min(startIndex + ItemsPerPage, _currentData.Count);

        // Create items for current page - GridLayoutGroup handles positioning
        for (int i = startIndex; i < endIndex; i++)
        {
            RTTMediaGridItem item;

            if (_itemPool.Count > 0)
            {
                item = _itemPool.Dequeue();
            }
            else
            {
                item = CreateGridItem();
            }

            var video = _currentData[i];
            item.SetData(video);
            item.SetSelected(SelectedVideo?.Path == video.Path);
            item.gameObject.SetActive(true);
            item.transform.SetAsLastSibling(); // Ensure correct order in GridLayoutGroup

            _items.Add(item);

            // Request thumbnail async
            RequestThumbnail(item, video.Path);
        }
    }

    private void RequestThumbnail(RTTMediaGridItem item, string path)
    {
        // Use FileThumbnailService to load thumbnail
        var thumbnailService = FindObjectOfType<FileThumbnailService>();
        if (thumbnailService != null)
        {
            StartCoroutine(LoadThumbnailCoroutine(item, path, thumbnailService));
        }
    }

    private IEnumerator LoadThumbnailCoroutine(RTTMediaGridItem item, string path, FileThumbnailService service)
    {
        // Check if item is still valid (same path)
        if (item == null || item.VideoInfo.Path != path)
            yield break;

        // Create MockFile for the thumbnail service
        var fileInfo = new System.IO.FileInfo(path);
        var mockFile = new MockFile
        {
            Path = path,
            Name = fileInfo.Name,
            Type = fileInfo.Extension.TrimStart('.').ToLower(),
            IsFolder = false,
            Modified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now
        };

        // Request from service (it handles caching)
        Sprite thumbnail = null;
        bool loaded = false;

        service.RequestThumbnail(
            mockFile,
            256,
            (sprite) =>
            {
                thumbnail = sprite;
                loaded = true;
            },
            () => loaded = true  // On failed
        );

        // Wait for callback
        float timeout = 5f;
        while (!loaded && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Apply thumbnail if item still matches
        if (item != null && item.VideoInfo.Path == path && thumbnail != null)
        {
            item.SetThumbnail(thumbnail);
        }
    }
    #endregion

    #region Event Handlers
    private void HandleItemClicked(MediaVideoInfo video)
    {
        SelectVideo(video);
    }

    private void HandleItemDoubleClicked(MediaVideoInfo video)
    {
        OnVideoDoubleClicked?.Invoke(video);
    }

    private void HandleItemSelected(MediaVideoInfo video)
    {
        // Same as clicked for now
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        // Cleanup items
        foreach (var item in _items)
        {
            if (item != null)
            {
                item.OnClicked -= HandleItemClicked;
                item.OnDoubleClicked -= HandleItemDoubleClicked;
                item.OnSelected -= HandleItemSelected;
            }
        }

        while (_itemPool.Count > 0)
        {
            var item = _itemPool.Dequeue();
            if (item != null)
            {
                item.OnClicked -= HandleItemClicked;
                item.OnDoubleClicked -= HandleItemDoubleClicked;
                item.OnSelected -= HandleItemSelected;
            }
        }
    }
    #endregion
}
