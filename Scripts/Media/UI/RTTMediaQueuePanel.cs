using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Video queue panel displayed inside SideControlsFrame.
/// Shows paginated list of queued videos as card-style items matching Media Library grid.
/// Implements IPaginationController for external RTTFilePagination component.
/// </summary>
public class RTTMediaQueuePanel : MonoBehaviour, IPaginationController
{
    #region Constants
    // Layout ratios
    private const float HEADER_RATIO = 0.10f;        // Header = 10% chiều cao Queue
    private const float ITEM_RATIO = 0.40f;           // Mỗi item = 40% chiều cao body
    private const float IMAGE_RATIO = 5f / 7f;        // Image = 5/7 chiều cao item
    private const float IMAGE_WIDTH_RATIO = 0.90f;    // Image max width = 90% queue width

    // Side margin ratio (5.5% of Queue width)
    private const float SIDE_MARGIN_RATIO = 0.055f;

    private const float TOP_GAP_RATIO = 1f / 12f;    // 1/12 item height = gap trên image

    // Fixed sizes
    private const float HEADER_BTN_SIZE = 44f;
    private const float PLAYING_ICON_SIZE = 48f;
    private const int THUMBNAIL_SIZE = 512;


    // Hover scale
    private const float HOVER_SCALE = 1.03f;

    // Pagination
    private const int ITEMS_PER_PAGE = 2;

    // Colors
    private static readonly Color ITEM_BG = new Color(0.14f, 0.14f, 0.16f, 0.6f);
    private static readonly Color TEXT_COLOR = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color THUMB_PLACEHOLDER = new Color(0.12f, 0.12f, 0.14f, 1f);

    private const string ICON_SHUFFLE = "icon_shuffle";
    private const string ICON_PLAYING = "icon_playing";
    private const float PLAYING_SCROLL_SPEED = 25f; // UI pixels/sec for playing icon scroll

    // Queue background gradient (top → bottom)
    private static readonly Color QUEUE_BG_TOP = ITEM_BG;
    private static readonly Color QUEUE_BG_BOTTOM = new Color(0.173f, 0.173f, 0.173f, 0.75f);
    #endregion

    #region Events
    /// <summary>Fired when a queue item is clicked. Parameter is the index in the queue.</summary>
    public event Action<int> OnItemClicked;
    /// <summary>Fired when shuffle button is clicked.</summary>
    public event Action OnShuffleClicked;
    /// <summary>Fired when page changes. Parameters: currentPage (1-based), totalPages.</summary>
    public event Action<int, int> OnPageChanged;
    #endregion

    #region Private Fields
    private float _width;
    private float _height;
    private float _headerHeight;
    private float _bodyHeight;
    private float _itemHeight;
    private float _topGap;
    private float _imageHeight;
    private float _textHeight;
    private float _imageMaxWidth;
    private float _sideMargin;
    private float _shuffleBtnSize;
    private TMP_FontAsset _font;

    private RectTransform _contentRoot;
    private RectTransform _itemsContainer;

    private List<string> _queuePaths = new List<string>();
    private int _currentIndex = -1;
    private List<QueueItemUI> _items = new List<QueueItemUI>();

    // Pagination state
    private int _currentPage = 1;
    private int _totalPages = 1;

    // Scroll animation
    private const float SCROLL_DURATION = 0.2f;
    private RectTransform _bodyClipContainer; // fixed clip parent (RectMask2D)
    private Coroutine _scrollAnim;
    private int _scrollDirection; // -1 = scroll up (next page), +1 = scroll down (prev page)

    // Playing icon scrolling animation
    private static Sprite _scrollingPlayingSprite;
    private Coroutine _playingAnim;

    // Cached sprites
    private static Sprite _roundedRectSprite;
    private static Sprite _circleSprite;
    #endregion

    #region Nested Types
    private class QueueItemUI
    {
        public GameObject Root;
        public Image Background;
        public Image ThumbnailImage;
        public GameObject PlayingIcon;
        public Image PlayingIconImage;
        public TextMeshProUGUI TitleText;
        public Button Button;
        public HoverEffectController HoverController;
        public int Index;
    }
    #endregion

    #region Public API
    public void Initialize(float width, float height, TMP_FontAsset font)
    {
        _width = width;
        _height = height;
        _font = font;

        // Tính toán layout từ tỉ lệ
        _headerHeight = Mathf.Round(_height * HEADER_RATIO);
        _bodyHeight = _height - _headerHeight;
        _itemHeight = Mathf.Round(_bodyHeight * ITEM_RATIO);
        _topGap = Mathf.Round(_itemHeight * TOP_GAP_RATIO);
        _imageHeight = Mathf.Round(_itemHeight * IMAGE_RATIO) - _topGap;
        _textHeight = _itemHeight - _imageHeight - _topGap;
        _imageMaxWidth = Mathf.Round(_width * IMAGE_WIDTH_RATIO);
        _sideMargin = Mathf.Round(_width * SIDE_MARGIN_RATIO);
        _shuffleBtnSize = Mathf.Round(HEADER_BTN_SIZE * 1.3f);

        BuildUI();
    }

    /// <summary>
    /// Set the queue contents and highlight the current index.
    /// </summary>
    public void SetQueue(List<string> paths, int currentIndex)
    {
        _queuePaths = paths ?? new List<string>();
        _currentIndex = currentIndex;
        _totalPages = Mathf.Max(1, Mathf.CeilToInt(_queuePaths.Count / (float)ITEMS_PER_PAGE));

        // Navigate to the page containing the current item
        GoToCurrentItemPage(forceRebuild: true);
    }

    /// <summary>
    /// Update current playing index without rebuilding the list.
    /// </summary>
    public void SetCurrentIndex(int index)
    {
        int oldIndex = _currentIndex;
        _currentIndex = index;

        int newPage = GetPageForIndex(index);
        if (newPage != _currentPage)
        {
            // Different page - rebuild (GoToCurrentItemPage sets scroll direction)
            GoToCurrentItemPage(forceRebuild: true);
        }
        else
        {
            // Same page - just update highlights
            int pageStartIndex = (_currentPage - 1) * ITEMS_PER_PAGE;
            int oldLocal = oldIndex - pageStartIndex;
            int newLocal = index - pageStartIndex;

            if (oldLocal >= 0 && oldLocal < _items.Count)
                UpdateItemHighlight(_items[oldLocal], false);
            if (newLocal >= 0 && newLocal < _items.Count)
                UpdateItemHighlight(_items[newLocal], true);
        }
    }

    /// <summary>
    /// Navigate to the page containing the current playing item.
    /// </summary>
    private void GoToCurrentItemPage(bool forceRebuild = false)
    {
        int targetPage = GetPageForIndex(_currentIndex);
        if (targetPage != _currentPage || forceRebuild)
        {
            _scrollDirection = targetPage > _currentPage ? -1 : (targetPage < _currentPage ? 1 : 0);
            _currentPage = targetPage;
            RebuildItems();
        }
    }

    /// <summary>
    /// Get the page number (1-based) that contains the given queue index.
    /// </summary>
    private int GetPageForIndex(int index)
    {
        if (index < 0 || _queuePaths.Count == 0) return 1;
        return Mathf.Clamp((index / ITEMS_PER_PAGE) + 1, 1, _totalPages);
    }

    /// <summary>
    /// Change page by delta (-1 = previous, +1 = next).
    /// </summary>
    public void ChangePage(int delta)
    {
        int newPage = Mathf.Clamp(_currentPage + delta, 1, _totalPages);
        if (newPage == _currentPage) return;

        _scrollDirection = delta > 0 ? -1 : 1; // next page = items slide up, prev = slide down
        _currentPage = newPage;
        RebuildItems();
    }

    /// <summary>
    /// Notify external pagination component of page state change.
    /// </summary>
    private void NotifyPageChanged()
    {
        OnPageChanged?.Invoke(_currentPage, _totalPages);
    }

    #region IPaginationController
    /// <summary>Go to specific page number (1-based).</summary>
    public void GoToPage(int pageNumber)
    {
        int newPage = Mathf.Clamp(pageNumber, 1, _totalPages);
        if (newPage == _currentPage) return;

        _scrollDirection = newPage > _currentPage ? -1 : 1;
        _currentPage = newPage;
        RebuildItems();
    }

    /// <summary>Scroll to first page.</summary>
    public void ScrollToStart()
    {
        GoToPage(1);
    }

    /// <summary>Scroll to last page.</summary>
    public void ScrollToEnd()
    {
        GoToPage(_totalPages);
    }
    #endregion
    #endregion

    #region UI Building
    private void BuildUI()
    {
        _contentRoot = gameObject.GetComponent<RectTransform>();
        if (_contentRoot == null)
            _contentRoot = gameObject.AddComponent<RectTransform>();

        _contentRoot.anchorMin = Vector2.zero;
        _contentRoot.anchorMax = Vector2.one;
        _contentRoot.offsetMin = Vector2.zero;
        _contentRoot.offsetMax = Vector2.zero;

        CreateGradientBackground();
        CreateHeader();
        CreateItemsContainer();
    }

    private void CreateGradientBackground()
    {
        GameObject gradObj = new GameObject("GradientBg");
        gradObj.transform.SetParent(_contentRoot, false);

        var gradRT = gradObj.AddComponent<RectTransform>();
        gradRT.anchorMin = Vector2.zero;
        gradRT.anchorMax = Vector2.one;
        gradRT.offsetMin = Vector2.zero;
        gradRT.offsetMax = Vector2.zero;

        // Create gradient texture with rounded corners baked in (Sliced sprite)
        // No Mask needed - corners are transparent in the texture itself
        int texW = 64;
        int texH = 128;
        int radius = 12;
        var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

        for (int y = 0; y < texH; y++)
        {
            float t = y / (float)(texH - 1); // 0=bottom, 1=top
            Color gradColor = Color.Lerp(QUEUE_BG_BOTTOM, QUEUE_BG_TOP, t);

            for (int x = 0; x < texW; x++)
            {
                float alpha = gradColor.a;
                Vector2 corner = Vector2.zero;
                bool isCorner = false;

                if (x < radius && y < radius)
                { corner = new Vector2(radius, radius); isCorner = true; }
                else if (x >= texW - radius && y < radius)
                { corner = new Vector2(texW - radius - 1, radius); isCorner = true; }
                else if (x < radius && y >= texH - radius)
                { corner = new Vector2(radius, texH - radius - 1); isCorner = true; }
                else if (x >= texW - radius && y >= texH - radius)
                { corner = new Vector2(texW - radius - 1, texH - radius - 1); isCorner = true; }

                if (isCorner)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), corner);
                    alpha *= Mathf.Clamp01(radius - dist + 0.5f);
                }

                tex.SetPixel(x, y, new Color(gradColor.r, gradColor.g, gradColor.b, alpha));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector4 border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
        var gradImage = gradObj.AddComponent<Image>();
        gradImage.sprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
            Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        gradImage.type = Image.Type.Sliced;
        gradImage.color = Color.white; // Colors baked in texture
        gradImage.raycastTarget = false;
    }

    private void CreateHeader()
    {
        // Header container with HorizontalLayoutGroup (margin riêng)
        GameObject headerObj = new GameObject("QueueHeader");
        headerObj.transform.SetParent(_contentRoot, false);

        var headerRT = headerObj.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.offsetMin = new Vector2(_sideMargin, -_headerHeight);
        headerRT.offsetMax = new Vector2(-_sideMargin, 0);

        var headerLayout = headerObj.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        headerLayout.spacing = 8f;

        // Title text
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(headerObj.transform, false);

        var titleLE = titleObj.AddComponent<LayoutElement>();
        titleLE.flexibleWidth = 1f;

        var headerText = titleObj.AddComponent<TextMeshProUGUI>();
        headerText.font = _font;
        headerText.text = "Queue";
        headerText.fontSize = 35;
        headerText.color = TEXT_COLOR;
        headerText.alignment = TextAlignmentOptions.MidlineLeft;
        headerText.fontStyle = FontStyles.Bold;
        headerText.raycastTarget = false;

        // Shuffle button
        CreateShuffleButton(headerObj.transform);
    }

    private void CreateShuffleButton(Transform parent)
    {
        GameObject btnObj = new GameObject("Btn_Shuffle");
        btnObj.transform.SetParent(parent, false);

        var btnLE = btnObj.AddComponent<LayoutElement>();
        btnLE.minWidth = _shuffleBtnSize;
        btnLE.minHeight = _shuffleBtnSize;
        btnLE.preferredWidth = _shuffleBtnSize;
        btnLE.preferredHeight = _shuffleBtnSize;

        // No background - transparent raycast target
        var bgImage = btnObj.AddComponent<Image>();
        bgImage.color = Color.clear;
        bgImage.raycastTarget = true;

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => OnShuffleClicked?.Invoke());

        // Icon (fills the button area)
        GameObject iconObj = new GameObject("IconImage");
        iconObj.transform.SetParent(btnObj.transform, false);
        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.15f, 0.15f);
        iconRT.anchorMax = new Vector2(0.85f, 0.85f);
        iconRT.offsetMin = Vector2.zero;
        iconRT.offsetMax = Vector2.zero;

        var iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>(ICON_SHUFFLE);
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        // Hover effect
        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f).WithTransitionDuration(0.08f));

        // Collider for VR raycast
        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(_shuffleBtnSize, _shuffleBtnSize, 10);
        col.center = new Vector3(0, 0, -5);
    }

    private void CreateItemsContainer()
    {
        // Fixed clip container - below header, clips children during scroll animation
        GameObject clipObj = new GameObject("BodyClip");
        clipObj.transform.SetParent(_contentRoot, false);

        _bodyClipContainer = clipObj.AddComponent<RectTransform>();
        _bodyClipContainer.anchorMin = new Vector2(0, 0);
        _bodyClipContainer.anchorMax = new Vector2(1, 1);
        // Lấy 1 nửa khoảng cách phía trên (topGap) đưa cho phía dưới
        float redistribute = Mathf.Round(_topGap);
        _bodyClipContainer.offsetMin = new Vector2(0, redistribute);
        _bodyClipContainer.offsetMax = new Vector2(0, -_headerHeight + redistribute);
        clipObj.AddComponent<RectMask2D>();

        // Animated items container inside clip - this one moves during scroll
        GameObject containerObj = new GameObject("ItemsContainer");
        containerObj.transform.SetParent(clipObj.transform, false);

        _itemsContainer = containerObj.AddComponent<RectTransform>();
        _itemsContainer.anchorMin = Vector2.zero;
        _itemsContainer.anchorMax = Vector2.one;
        _itemsContainer.offsetMin = Vector2.zero;
        _itemsContainer.offsetMax = Vector2.zero;

        var layout = containerObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 0f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 0, 0);
    }

    #endregion

    #region Item Management
    private void RebuildItems()
    {
        // Stop any running scroll animation
        if (_scrollAnim != null)
        {
            StopCoroutine(_scrollAnim);
            _scrollAnim = null;
        }

        foreach (var item in _items)
        {
            if (item.Root != null) Destroy(item.Root);
        }
        _items.Clear();

        if (_itemsContainer == null) return;

        // Calculate page range
        int startIndex = (_currentPage - 1) * ITEMS_PER_PAGE;
        int endIndex = Mathf.Min(startIndex + ITEMS_PER_PAGE, _queuePaths.Count);

        for (int i = startIndex; i < endIndex; i++)
        {
            var item = CreateQueueItem(i, _queuePaths[i]);
            _items.Add(item);
        }

        // Peek item: show partial next item (clipped by RectMask2D) to hint more content
        int peekIndex = endIndex;
        if (peekIndex < _queuePaths.Count)
        {
            var peekItem = CreateQueueItem(peekIndex, _queuePaths[peekIndex]);
            _items.Add(peekItem);
        }

        // Animate scroll transition
        if (_scrollDirection != 0 && gameObject.activeInHierarchy)
        {
            _scrollAnim = StartCoroutine(AnimateScroll(_scrollDirection));
            _scrollDirection = 0;
        }
        else
        {
            _itemsContainer.anchoredPosition = Vector2.zero;
        }

        NotifyPageChanged();

        // Restart playing icon animation for current page's active item
        StartPlayingAnimation();
    }

    private IEnumerator AnimateScroll(int direction)
    {
        // direction: -1 = slide up (next page), +1 = slide down (prev page)
        float offset = direction * _bodyHeight * 0.5f;
        float elapsed = 0f;

        _itemsContainer.anchoredPosition = new Vector2(0, offset);

        while (elapsed < SCROLL_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / SCROLL_DURATION);
            // Ease out cubic: 1 - (1-t)^3
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            _itemsContainer.anchoredPosition = new Vector2(0, Mathf.Lerp(offset, 0f, eased));
            yield return null;
        }

        _itemsContainer.anchoredPosition = Vector2.zero;
        _scrollAnim = null;
    }

    private void StartPlayingAnimation()
    {
        StopPlayingAnimation();
        if (gameObject.activeInHierarchy)
            _playingAnim = StartCoroutine(PlayingIconAnimCoroutine());
    }

    private void StopPlayingAnimation()
    {
        if (_playingAnim != null)
        {
            StopCoroutine(_playingAnim);
            _playingAnim = null;
        }
    }

    private IEnumerator PlayingIconAnimCoroutine()
    {
        while (true)
        {
            foreach (var item in _items)
            {
                if (item.PlayingIconImage != null && item.PlayingIcon != null && item.PlayingIcon.activeSelf)
                {
                    var rt = item.PlayingIconImage.rectTransform;
                    float scrollRange = rt.sizeDelta.x - PLAYING_ICON_SIZE;
                    if (scrollRange <= 0f) continue;
                    float x = rt.anchoredPosition.x - PLAYING_SCROLL_SPEED * Time.deltaTime;
                    if (x < -scrollRange) x += scrollRange;
                    rt.anchoredPosition = new Vector2(x, 0f);
                }
            }
            yield return null;
        }
    }

    private QueueItemUI CreateQueueItem(int index, string path, bool isPeek = false)
    {
        var item = new QueueItemUI { Index = index };
        bool isActive = (index == _currentIndex);

        // === Root (full width, height = _itemHeight) ===
        item.Root = new GameObject($"QueueItem_{index}");
        item.Root.transform.SetParent(_itemsContainer, false);

        var rootRT = item.Root.AddComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(0, _itemHeight);

        var le = item.Root.AddComponent<LayoutElement>();
        le.minHeight = _itemHeight;
        le.preferredHeight = _itemHeight;

        // === Background (rounded rect, transparent by default, color on hover) ===
        item.Background = item.Root.AddComponent<Image>();
        item.Background.color = Color.clear;
        item.Background.sprite = GetRoundedRect();
        item.Background.type = Image.Type.Sliced;
        item.Background.raycastTarget = !isPeek;

        if (!isPeek)
        {
            // === Button for click ===
            item.Button = item.Root.AddComponent<Button>();
            item.Button.transition = Selectable.Transition.None;
            int capturedIndex = index;
            item.Button.onClick.AddListener(() => OnItemClicked?.Invoke(capturedIndex));

            // === Collider for VR raycast (full width) ===
            var col = item.Root.AddComponent<BoxCollider>();
            col.size = new Vector3(_width, _itemHeight, 10);
            col.center = new Vector3(0, 0, -5);
        }

        // === Thumbnail Container (top, centered 90% width, offset down by _topGap) ===
        GameObject thumbContainer = new GameObject("ThumbnailContainer");
        thumbContainer.transform.SetParent(item.Root.transform, false);

        var thumbContainerRT = thumbContainer.AddComponent<RectTransform>();
        float sideMargin = (1f - IMAGE_WIDTH_RATIO) / 2f; // 0.05 = 5% mỗi bên
        thumbContainerRT.anchorMin = new Vector2(sideMargin, 1);
        thumbContainerRT.anchorMax = new Vector2(1f - sideMargin, 1);
        thumbContainerRT.pivot = new Vector2(0.5f, 1);
        thumbContainerRT.sizeDelta = new Vector2(0, _imageHeight);
        thumbContainerRT.anchoredPosition = new Vector2(0, -_topGap); // gap trên image

        Image containerBg = thumbContainer.AddComponent<Image>();
        containerBg.color = Color.clear;
        containerBg.raycastTarget = false;
        thumbContainer.AddComponent<RectMask2D>();

        // === Thumbnail Image (FitInParent - hiển thị toàn bộ ảnh, không crop) ===
        GameObject thumbObj = new GameObject("Thumbnail");
        thumbObj.transform.SetParent(thumbContainer.transform, false);

        var thumbRT = thumbObj.AddComponent<RectTransform>();
        thumbRT.anchorMin = new Vector2(0.5f, 0.5f);
        thumbRT.anchorMax = new Vector2(0.5f, 0.5f);
        thumbRT.pivot = new Vector2(0.5f, 0.5f);
        thumbRT.anchoredPosition = Vector2.zero;

        item.ThumbnailImage = thumbObj.AddComponent<Image>();
        item.ThumbnailImage.preserveAspect = true;
        item.ThumbnailImage.raycastTarget = false;
        item.ThumbnailImage.color = THUMB_PLACEHOLDER;
        item.ThumbnailImage.material = RoundedCorners.SharedMaterial;

        var roundedCorners = thumbObj.AddComponent<RoundedCorners>();
        roundedCorners.Radius = 16f;
        roundedCorners.UseParentRect = false;

        var aspectFitter = thumbObj.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspectFitter.aspectRatio = 16f / 9f;

        LoadThumbnail(path, item.ThumbnailImage);

        // === Playing Icon (overlay trên thumbnail, child of Root to avoid RectMask2D/material interference) ===
        item.PlayingIcon = CreatePlayingIcon(item.Root.transform, _topGap, _imageHeight, out var playingImg);
        item.PlayingIconImage = playingImg;
        item.PlayingIcon.SetActive(isActive);

        // === Title Text (bottom, margin riêng) ===
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(item.Root.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 0);
        titleRT.anchorMax = new Vector2(1, 0);
        titleRT.pivot = new Vector2(0, 0);
        titleRT.offsetMin = new Vector2(_sideMargin, 0);
        titleRT.offsetMax = new Vector2(-_sideMargin, _textHeight);

        item.TitleText = titleObj.AddComponent<TextMeshProUGUI>();
        item.TitleText.font = _font;
        item.TitleText.text = Path.GetFileNameWithoutExtension(path) ?? Path.GetFileName(path);
        item.TitleText.fontSize = 28;
        item.TitleText.color = TEXT_COLOR;
        item.TitleText.alignment = TextAlignmentOptions.MidlineLeft;
        item.TitleText.overflowMode = TextOverflowModes.Ellipsis;
        item.TitleText.maxVisibleLines = 1;
        item.TitleText.enableWordWrapping = false;
        item.TitleText.raycastTarget = false;

        if (!isPeek)
        {
            // === Hover Effects (scale + background color) ===
            item.HoverController = item.Root.AddComponent<HoverEffectController>();
            item.HoverController.AddEffect(new ScaleHoverEffect()
                .WithHoverScale(HOVER_SCALE)
                .WithTransitionDuration(0.1f));
            item.HoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")
                .WithHoverColor(ITEM_BG)
                .WithTransitionDuration(0.1f));

            // Disable interaction on currently playing item
            if (isActive)
            {
                item.Button.interactable = false;
                item.HoverController.enabled = false;
            }
        }

        return item;
    }

    private GameObject CreatePlayingIcon(Transform rootTransform, float topGap, float imageHeight, out Image iconImage)
    {
        float bgSize = PLAYING_ICON_SIZE * 1.875f;

        // Container with circle background
        GameObject iconObj = new GameObject("PlayingIcon");
        iconObj.transform.SetParent(rootTransform, false);

        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 1f);
        iconRT.anchorMax = new Vector2(0.5f, 1f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(bgSize, bgSize);
        iconRT.anchoredPosition = new Vector2(0f, -(topGap + imageHeight * 0.5f));

        // Dark transparent circle background
        var bgImage = iconObj.AddComponent<Image>();
        bgImage.sprite = GetCircle();
        bgImage.color = new Color(0f, 0f, 0f, 0.45f);
        bgImage.raycastTarget = false;

        // Viewport clips the scrolling strip to original icon size
        var viewportObj = new GameObject("ScrollViewport");
        viewportObj.transform.SetParent(iconObj.transform, false);
        var vpRT = viewportObj.AddComponent<RectTransform>();
        vpRT.anchorMin = new Vector2(0.5f, 0.5f);
        vpRT.anchorMax = new Vector2(0.5f, 0.5f);
        vpRT.pivot = new Vector2(0.5f, 0.5f);
        vpRT.sizeDelta = new Vector2(PLAYING_ICON_SIZE, PLAYING_ICON_SIZE);
        viewportObj.AddComponent<RectMask2D>();

        // Wide scrolling strip inside viewport
        var stripObj = new GameObject("ScrollStrip");
        stripObj.transform.SetParent(viewportObj.transform, false);
        var stripRT = stripObj.AddComponent<RectTransform>();
        stripRT.anchorMin = new Vector2(0f, 0.5f);
        stripRT.anchorMax = new Vector2(0f, 0.5f);
        stripRT.pivot = new Vector2(0f, 0.5f);

        var scrollSprite = GetScrollingPlayingSprite();
        float stripAspect = (float)scrollSprite.texture.width / scrollSprite.texture.height;
        float stripDisplayW = PLAYING_ICON_SIZE * stripAspect;
        stripRT.sizeDelta = new Vector2(stripDisplayW, PLAYING_ICON_SIZE);
        stripRT.anchoredPosition = Vector2.zero;

        iconImage = stripObj.AddComponent<Image>();
        iconImage.sprite = scrollSprite;
        iconImage.color = Color.white;
        iconImage.preserveAspect = false;
        iconImage.raycastTarget = false;

        return iconObj;
    }

    private void UpdateItemHighlight(QueueItemUI item, bool isActive)
    {
        if (item == null) return;
        if (item.PlayingIcon != null)
            item.PlayingIcon.SetActive(isActive);
        if (item.Button != null)
            item.Button.interactable = !isActive;
        if (item.HoverController != null)
        {
            item.HoverController.enabled = !isActive;
            if (isActive)
                item.HoverController.ResetHoverState(immediate: true);
        }
    }

    private void LoadThumbnail(string path, Image thumbImage)
    {
        if (FileThumbnailService.Instance == null) return;

        var mockFile = new MockFile
        {
            Path = path,
            Name = Path.GetFileName(path),
            Type = Path.GetExtension(path)?.TrimStart('.').ToLower(),
            IsFolder = false
        };

        FileThumbnailService.Instance.RequestThumbnail(
            mockFile,
            THUMBNAIL_SIZE,
            sprite =>
            {
                if (thumbImage != null && sprite != null)
                {
                    thumbImage.sprite = sprite;
                    thumbImage.color = Color.white;

                    var aspectFitter = thumbImage.GetComponent<AspectRatioFitter>();
                    if (aspectFitter != null && sprite.texture != null)
                    {
                        float spriteAspect = (float)sprite.texture.width / sprite.texture.height;
                        aspectFitter.aspectRatio = spriteAspect;
                    }
                }
            },
            priority: 5,
            skipOverlay: true
        );
    }
    #endregion

    #region Sprite Helpers
    /// <summary>
    /// Create a seamless scrolling strip from icon_playing:
    /// 1. Double the source image side by side (24 parts)
    /// 2. Crossfade parts 12+13 (middle junction)
    /// 3. Crossfade parts 24+1 (loop junction)
    /// Result: 22-part wide strip that loops seamlessly when scrolled.
    /// </summary>
    private static Sprite GetScrollingPlayingSprite()
    {
        if (_scrollingPlayingSprite != null) return _scrollingPlayingSprite;

        var sourceSprite = Resources.Load<Sprite>(ICON_PLAYING);
        var sourceTex = sourceSprite.texture;
        int W = sourceTex.width;
        int H = sourceTex.height;
        int partW = Mathf.Max(1, W / 12);

        // Read pixels via RenderTexture (handles non-readable textures)
        var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(sourceTex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(W, H, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] src = readable.GetPixels();

        // Strip = 22 parts (doubled 24 minus 2 overlaps)
        int stripW = 22 * partW;
        Color[] strip = new Color[stripW * H];

        for (int y = 0; y < H; y++)
        {
            // Parts 1-11: direct copy from source
            for (int x = 0; x < 11 * partW && x < W; x++)
                strip[y * stripW + x] = src[y * W + x];

            // Part 12+13 blend: crossfade end of first copy → start of second copy
            for (int lx = 0; lx < partW; lx++)
            {
                float t = (partW > 1) ? lx / (float)(partW - 1) : 0.5f;
                int x12 = Mathf.Min(11 * partW + lx, W - 1);
                int x13 = lx;
                strip[y * stripW + 11 * partW + lx] = Color.Lerp(src[y * W + x12], src[y * W + x13], t);
            }

            // Parts 14-23: parts 2-11 from source (second copy after overlap)
            for (int p = 0; p < 10; p++)
            {
                for (int lx = 0; lx < partW; lx++)
                {
                    int srcX = Mathf.Min((p + 1) * partW + lx, W - 1);
                    strip[y * stripW + (12 + p) * partW + lx] = src[y * W + srcX];
                }
            }
        }

        // Blend part 24+1 for seamless loop (last partW crossfades to first partW)
        for (int y = 0; y < H; y++)
        {
            for (int lx = 0; lx < partW; lx++)
            {
                float t = (partW > 1) ? lx / (float)(partW - 1) : 0.5f;
                int endIdx = y * stripW + (stripW - partW + lx);
                int startIdx = y * stripW + lx;
                strip[endIdx] = Color.Lerp(strip[endIdx], strip[startIdx], t);
            }
        }

        var stripTex = new Texture2D(stripW, H, TextureFormat.RGBA32, false);
        stripTex.SetPixels(strip);
        stripTex.Apply();
        stripTex.wrapMode = TextureWrapMode.Clamp;
        stripTex.filterMode = FilterMode.Bilinear;

        _scrollingPlayingSprite = Sprite.Create(
            stripTex, new Rect(0, 0, stripW, H),
            new Vector2(0f, 0.5f), 100f);

        UnityEngine.Object.Destroy(readable);
        return _scrollingPlayingSprite;
    }

    private static Sprite GetRoundedRect()
    {
        if (_roundedRectSprite == null)
            _roundedRectSprite = RTTMediaControlsPanel.CreateRoundedRectSprite(10f);
        return _roundedRectSprite;
    }

    private static Sprite GetCircle()
    {
        if (_circleSprite == null)
        {
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var colors = new Color[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    colors[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(center - dist));
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
        return _circleSprite;
    }
    #endregion
}
