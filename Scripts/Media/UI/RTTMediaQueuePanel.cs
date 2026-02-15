using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;

/// <summary>
/// Video queue panel displayed inside SideControlsFrame.
/// Shows scrollable list of queued videos as card-style items matching Media Library grid.
/// </summary>
public class RTTMediaQueuePanel : MonoBehaviour
{
    #region Constants
    private const float HEADER_HEIGHT = 50f;
    private const float PADDING = 12f;
    private const float ITEM_SPACING = 8f;
    private const float ACCENT_BAR_WIDTH = 4f;

    // Card dimensions (adapted from RTTMediaGridItem for single-column layout)
    // Panel width 400 - padding 12*2 = 376 usable width
    private const float ITEM_HEIGHT = 280f;
    private const float THUMB_TOP_PADDING = 8f;
    private const float THUMBNAIL_HEIGHT = 211f;  // ~376 / 1.777 (16:9)
    private const float TEXT_AREA_HEIGHT = 61f;    // ITEM_HEIGHT - THUMB_TOP_PADDING - THUMBNAIL_HEIGHT
    private const int THUMBNAIL_SIZE = 512;

    // Badge dimensions (matching RTTMediaGridItem)
    private const float BADGE_WIDTH = 87f;
    private const float BADGE_HEIGHT = 33f;
    private const float BADGE_FONT_SIZE = 21f;
    private const float BADGE_MARGIN = 9f;

    private static readonly Color ITEM_BG = new Color(0.12f, 0.12f, 0.14f, 0.6f);
    private static readonly Color ITEM_ACTIVE_BG = new Color(0.25f, 0.12f, 0.12f, 0.8f);
    private static readonly Color ACCENT = new Color(1f, 0.2f, 0.2f, 1f);
    private static readonly Color TEXT_COLOR = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color THUMB_PLACEHOLDER = new Color(0.12f, 0.12f, 0.14f, 1f);
    private static readonly Color BADGE_BG = new Color(0f, 0f, 0f, 0.7f);
    #endregion

    #region Events
    /// <summary>Fired when a queue item is clicked. Parameter is the index in the queue.</summary>
    public event Action<int> OnItemClicked;
    #endregion

    #region Private Fields
    private float _width;
    private float _height;
    private TMP_FontAsset _font;

    private RectTransform _contentRoot;
    private ScrollRect _scrollRect;
    private RectTransform _scrollContent;

    private List<string> _queuePaths = new List<string>();
    private int _currentIndex = -1;
    private List<QueueItemUI> _items = new List<QueueItemUI>();

    // Cached sprites
    private static Sprite _roundedRectSprite;
    private static Sprite _badgeSprite;
    #endregion

    #region Nested Types
    private class QueueItemUI
    {
        public GameObject Root;
        public Image Background;
        public Image AccentBar;
        public Image ThumbnailImage;
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI DurationText;
        public GameObject DurationBadge;
        public int Index;
    }
    #endregion

    #region Public API
    public void Initialize(float width, float height, TMP_FontAsset font)
    {
        _width = width;
        _height = height;
        _font = font;

        BuildUI();
    }

    /// <summary>
    /// Set the queue contents and highlight the current index.
    /// </summary>
    public void SetQueue(List<string> paths, int currentIndex)
    {
        _queuePaths = paths ?? new List<string>();
        _currentIndex = currentIndex;

        RebuildItems();
        ScrollToCurrentItem();
    }

    /// <summary>
    /// Update current playing index without rebuilding the list.
    /// </summary>
    public void SetCurrentIndex(int index)
    {
        int oldIndex = _currentIndex;
        _currentIndex = index;

        if (oldIndex >= 0 && oldIndex < _items.Count)
            UpdateItemHighlight(_items[oldIndex], false);
        if (index >= 0 && index < _items.Count)
            UpdateItemHighlight(_items[index], true);

        ScrollToCurrentItem();
    }

    /// <summary>
    /// Scroll to make the current item visible.
    /// </summary>
    public void ScrollToCurrentItem()
    {
        if (_scrollRect == null || _scrollContent == null) return;
        if (_currentIndex < 0 || _currentIndex >= _items.Count) return;

        float totalHeight = _queuePaths.Count * (ITEM_HEIGHT + ITEM_SPACING);
        float viewportHeight = _height - HEADER_HEIGHT - PADDING * 2;
        if (totalHeight <= viewportHeight) return;

        float itemTop = _currentIndex * (ITEM_HEIGHT + ITEM_SPACING);
        float normalizedPos = 1f - Mathf.Clamp01(itemTop / (totalHeight - viewportHeight));
        _scrollRect.verticalNormalizedPosition = normalizedPos;
    }
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

        CreateHeader();
        CreateScrollArea();
    }

    private void CreateHeader()
    {
        GameObject headerObj = new GameObject("QueueHeader");
        headerObj.transform.SetParent(_contentRoot, false);

        var headerRT = headerObj.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.offsetMin = new Vector2(PADDING, -HEADER_HEIGHT);
        headerRT.offsetMax = new Vector2(-PADDING, 0);

        var headerText = headerObj.AddComponent<TextMeshProUGUI>();
        headerText.font = _font;
        headerText.text = "Queue";
        headerText.fontSize = 28;
        headerText.color = TEXT_COLOR;
        headerText.alignment = TextAlignmentOptions.MidlineLeft;
        headerText.fontStyle = FontStyles.Bold;
        headerText.raycastTarget = false;
    }

    private void CreateScrollArea()
    {
        // Viewport
        GameObject viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(_contentRoot, false);

        var viewportRT = viewportObj.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = new Vector2(PADDING, PADDING);
        viewportRT.offsetMax = new Vector2(-PADDING, -HEADER_HEIGHT);

        var viewportImg = viewportObj.AddComponent<Image>();
        viewportImg.color = Color.clear;
        viewportObj.AddComponent<Mask>().showMaskGraphic = false;

        // Content container
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);

        _scrollContent = contentObj.AddComponent<RectTransform>();
        _scrollContent.anchorMin = new Vector2(0, 1);
        _scrollContent.anchorMax = new Vector2(1, 1);
        _scrollContent.pivot = new Vector2(0.5f, 1);
        _scrollContent.offsetMin = new Vector2(0, 0);
        _scrollContent.offsetMax = new Vector2(0, 0);

        var layout = contentObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = ITEM_SPACING;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 0, 0);

        var fitter = contentObj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ScrollRect
        _scrollRect = viewportObj.AddComponent<ScrollRect>();
        _scrollRect.content = _scrollContent;
        _scrollRect.viewport = viewportRT;
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.scrollSensitivity = 30f;
        _scrollRect.movementType = ScrollRect.MovementType.Elastic;
        _scrollRect.elasticity = 0.1f;
    }
    #endregion

    #region Item Management
    private void RebuildItems()
    {
        foreach (var item in _items)
        {
            if (item.Root != null) Destroy(item.Root);
        }
        _items.Clear();

        if (_scrollContent == null) return;

        for (int i = 0; i < _queuePaths.Count; i++)
        {
            var item = CreateQueueItem(i, _queuePaths[i]);
            _items.Add(item);
        }
    }

    private QueueItemUI CreateQueueItem(int index, string path)
    {
        var item = new QueueItemUI { Index = index };
        bool isActive = (index == _currentIndex);

        // === Root ===
        item.Root = new GameObject($"QueueItem_{index}");
        item.Root.transform.SetParent(_scrollContent, false);

        var rootRT = item.Root.AddComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(0, ITEM_HEIGHT);

        var le = item.Root.AddComponent<LayoutElement>();
        le.minHeight = ITEM_HEIGHT;
        le.preferredHeight = ITEM_HEIGHT;

        // === Background (rounded rect) ===
        item.Background = item.Root.AddComponent<Image>();
        item.Background.color = isActive ? ITEM_ACTIVE_BG : ITEM_BG;
        item.Background.sprite = GetRoundedRect();
        item.Background.type = Image.Type.Sliced;
        item.Background.raycastTarget = true;

        // === Button for click ===
        var btn = item.Root.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        int capturedIndex = index;
        btn.onClick.AddListener(() => OnItemClicked?.Invoke(capturedIndex));

        // === Collider for VR raycast ===
        var col = item.Root.AddComponent<BoxCollider>();
        float itemWidth = _width - PADDING * 2;
        col.size = new Vector3(itemWidth, ITEM_HEIGHT, 10);
        col.center = new Vector3(0, 0, -5);

        // === Accent bar (left edge, visible for current item) ===
        GameObject accentObj = new GameObject("AccentBar");
        accentObj.transform.SetParent(item.Root.transform, false);

        var accentRT = accentObj.AddComponent<RectTransform>();
        accentRT.anchorMin = new Vector2(0, 0);
        accentRT.anchorMax = new Vector2(0, 1);
        accentRT.pivot = new Vector2(0, 0.5f);
        accentRT.offsetMin = new Vector2(0, 4);
        accentRT.offsetMax = new Vector2(ACCENT_BAR_WIDTH, -4);

        item.AccentBar = accentObj.AddComponent<Image>();
        item.AccentBar.color = ACCENT;
        item.AccentBar.raycastTarget = false;
        accentObj.SetActive(isActive);

        // === Thumbnail Container (with RectMask2D for crop + rounded corners) ===
        GameObject thumbContainer = new GameObject("ThumbnailContainer");
        thumbContainer.transform.SetParent(item.Root.transform, false);

        var thumbContainerRT = thumbContainer.AddComponent<RectTransform>();
        thumbContainerRT.anchorMin = new Vector2(0, 1);
        thumbContainerRT.anchorMax = new Vector2(1, 1);
        thumbContainerRT.pivot = new Vector2(0.5f, 1);
        thumbContainerRT.anchoredPosition = new Vector2(0, -THUMB_TOP_PADDING);
        thumbContainerRT.sizeDelta = new Vector2(0, THUMBNAIL_HEIGHT);

        // Background for container
        Image containerBg = thumbContainer.AddComponent<Image>();
        containerBg.color = Color.clear;
        containerBg.raycastTarget = false;

        // RectMask2D for clipping thumbnail overflow
        thumbContainer.AddComponent<RectMask2D>();

        // === Thumbnail Image with AspectRatioFitter ===
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

        var aspectFitter = thumbObj.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        aspectFitter.aspectRatio = 16f / 9f;

        // Load thumbnail
        LoadThumbnail(path, item.ThumbnailImage);

        // === Duration Badge (bottom-right of thumbnail area, outside mask) ===
        CreateDurationBadge(item, path);

        // === Title Text (below thumbnail) ===
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(item.Root.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 0);
        titleRT.anchorMax = new Vector2(1, 0);
        titleRT.pivot = new Vector2(0, 0);
        // Position from bottom, leaving some padding
        titleRT.offsetMin = new Vector2(10, 6);
        titleRT.offsetMax = new Vector2(-10, TEXT_AREA_HEIGHT - 6);

        item.TitleText = titleObj.AddComponent<TextMeshProUGUI>();
        item.TitleText.font = _font;
        item.TitleText.text = Path.GetFileNameWithoutExtension(path) ?? Path.GetFileName(path);
        item.TitleText.fontSize = 26;
        item.TitleText.color = TEXT_COLOR;
        item.TitleText.alignment = TextAlignmentOptions.TopLeft;
        item.TitleText.overflowMode = TextOverflowModes.Ellipsis;
        item.TitleText.maxVisibleLines = 2;
        item.TitleText.enableWordWrapping = true;
        item.TitleText.raycastTarget = false;

        return item;
    }

    private void CreateDurationBadge(QueueItemUI item, string path)
    {
        item.DurationBadge = new GameObject("DurationBadge");
        item.DurationBadge.transform.SetParent(item.Root.transform, false);

        // Position at bottom-right of thumbnail area
        float thumbnailBottom = -(THUMB_TOP_PADDING + THUMBNAIL_HEIGHT);
        RectTransform rt = item.DurationBadge.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-BADGE_MARGIN, thumbnailBottom + BADGE_MARGIN);
        rt.sizeDelta = new Vector2(BADGE_WIDTH, BADGE_HEIGHT);

        // Badge background
        Image bg = item.DurationBadge.AddComponent<Image>();
        bg.sprite = GetBadge();
        bg.type = Image.Type.Sliced;
        bg.color = BADGE_BG;
        bg.raycastTarget = false;

        // Badge text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(item.DurationBadge.transform, false);

        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(6, 0);
        textRT.offsetMax = new Vector2(-6, 0);

        item.DurationText = textObj.AddComponent<TextMeshProUGUI>();
        item.DurationText.font = _font;
        item.DurationText.fontSize = BADGE_FONT_SIZE;
        item.DurationText.fontStyle = FontStyles.Bold;
        item.DurationText.color = Color.white;
        item.DurationText.alignment = TextAlignmentOptions.Center;
        item.DurationText.raycastTarget = false;

        // Show placeholder, then fetch actual duration
        item.DurationText.text = "--:--";
        FetchDuration(path, item.DurationText);
    }

    private void FetchDuration(string path, TextMeshProUGUI durationText)
    {
        if (FileMetadataService.Instance == null) return;

        FileMetadataService.Instance.GetVideoMetadata(path, (metadata) =>
        {
            if (durationText == null) return;
            if (metadata.Duration.TotalSeconds > 0)
            {
                if (metadata.Duration.TotalHours >= 1)
                    durationText.text = $"{(int)metadata.Duration.TotalHours}:{metadata.Duration.Minutes:D2}:{metadata.Duration.Seconds:D2}";
                else
                    durationText.text = $"{metadata.Duration.Minutes}:{metadata.Duration.Seconds:D2}";
            }
        });
    }

    private void UpdateItemHighlight(QueueItemUI item, bool isActive)
    {
        if (item == null) return;
        if (item.Background != null)
            item.Background.color = isActive ? ITEM_ACTIVE_BG : ITEM_BG;
        if (item.AccentBar != null)
            item.AccentBar.gameObject.SetActive(isActive);
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
                    thumbImage.color = Color.white; // Remove placeholder tint

                    // Update AspectRatioFitter based on actual sprite dimensions
                    var aspectFitter = thumbImage.GetComponent<AspectRatioFitter>();
                    if (aspectFitter != null && sprite.texture != null)
                    {
                        float spriteAspect = (float)sprite.texture.width / sprite.texture.height;
                        aspectFitter.aspectRatio = spriteAspect;
                    }
                }
            },
            priority: 5
        );
    }
    #endregion

    #region Sprite Helpers
    private static Sprite GetRoundedRect()
    {
        if (_roundedRectSprite == null)
            _roundedRectSprite = RTTMediaControlsPanel.CreateRoundedRectSprite(10f);
        return _roundedRectSprite;
    }

    private static Sprite GetBadge()
    {
        if (_badgeSprite == null)
            _badgeSprite = RTTMediaControlsPanel.CreateRoundedRectSprite(6f);
        return _badgeSprite;
    }
    #endregion
}
