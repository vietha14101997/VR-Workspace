using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Video queue panel displayed inside SideControlsFrame.
/// Shows scrollable list of queued videos as card-style items matching Media Library grid.
/// </summary>
public class RTTMediaQueuePanel : MonoBehaviour
{
    #region Constants
    // Layout ratios
    private const float HEADER_RATIO = 0.10f;        // Header = 10% chiều cao Queue
    private const float ITEM_RATIO = 0.40f;           // Mỗi item = 40% chiều cao body
    private const float IMAGE_RATIO = 5f / 7f;        // Image = 5/7 chiều cao item
    private const float IMAGE_WIDTH_RATIO = 0.90f;    // Image max width = 90% queue width

    // Margins riêng từng thành phần
    private const float HEADER_MARGIN_H = 20f;
    private const float TITLE_MARGIN_H = 12f;

    private const float TOP_GAP_RATIO = 1f / 12f;    // 1/12 item height = gap trên image

    // Fixed sizes
    private const float HEADER_BTN_SIZE = 44f;
    private const float PLAYING_ICON_SIZE = 48f;
    private const int THUMBNAIL_SIZE = 512;


    // Hover scale
    private const float HOVER_SCALE = 1.03f;

    // Colors
    private static readonly Color ITEM_BG = new Color(0.14f, 0.14f, 0.16f, 0.6f);
    private static readonly Color TEXT_COLOR = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color THUMB_PLACEHOLDER = new Color(0.12f, 0.12f, 0.14f, 1f);

    private const string ICON_SHUFFLE = "icon_shuffle";
    private const string ICON_PLAYING = "icon_playing";

    // Queue background gradient (top → bottom)
    private static readonly Color QUEUE_BG_TOP = ITEM_BG;
    private static readonly Color QUEUE_BG_BOTTOM = new Color(0.173f, 0.173f, 0.173f, 0.75f);
    #endregion

    #region Events
    /// <summary>Fired when a queue item is clicked. Parameter is the index in the queue.</summary>
    public event Action<int> OnItemClicked;
    /// <summary>Fired when shuffle button is clicked.</summary>
    public event Action OnShuffleClicked;
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
    private TMP_FontAsset _font;

    private RectTransform _contentRoot;
    private ScrollRect _scrollRect;
    private RectTransform _scrollContent;

    private List<string> _queuePaths = new List<string>();
    private int _currentIndex = -1;
    private List<QueueItemUI> _items = new List<QueueItemUI>();

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

        float viewportHeight = _bodyHeight - _topGap; // viewport có bottom gap
        float totalHeight = _queuePaths.Count * _itemHeight;
        if (totalHeight <= viewportHeight) return;

        float itemTop = _currentIndex * _itemHeight;
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

        CreateGradientBackground();
        CreateHeader();
        CreateScrollArea();
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
        headerRT.offsetMin = new Vector2(HEADER_MARGIN_H, -_headerHeight);
        headerRT.offsetMax = new Vector2(-HEADER_MARGIN_H, 0);

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
        headerText.fontSize = 28;
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
        btnLE.minWidth = HEADER_BTN_SIZE;
        btnLE.minHeight = HEADER_BTN_SIZE;
        btnLE.preferredWidth = HEADER_BTN_SIZE;
        btnLE.preferredHeight = HEADER_BTN_SIZE;

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
        col.size = new Vector3(HEADER_BTN_SIZE, HEADER_BTN_SIZE, 10);
        col.center = new Vector3(0, 0, -5);
    }

    private void CreateScrollArea()
    {
        // Viewport - full width, ngay dưới header
        GameObject viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(_contentRoot, false);

        var viewportRT = viewportObj.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = new Vector2(0, _topGap);              // bottom gap = _topGap
        viewportRT.offsetMax = new Vector2(0, -_headerHeight);    // top = dưới header

        viewportObj.AddComponent<RectMask2D>();

        // Content container
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);

        _scrollContent = contentObj.AddComponent<RectTransform>();
        _scrollContent.anchorMin = new Vector2(0, 1);
        _scrollContent.anchorMax = new Vector2(1, 1);
        _scrollContent.pivot = new Vector2(0.5f, 1);
        _scrollContent.offsetMin = Vector2.zero;
        _scrollContent.offsetMax = Vector2.zero;

        var layout = contentObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 0f;
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

        // === Root (full width, height = _itemHeight) ===
        item.Root = new GameObject($"QueueItem_{index}");
        item.Root.transform.SetParent(_scrollContent, false);

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
        item.Background.raycastTarget = true;

        // === Button for click ===
        item.Button = item.Root.AddComponent<Button>();
        item.Button.transition = Selectable.Transition.None;
        int capturedIndex = index;
        item.Button.onClick.AddListener(() => OnItemClicked?.Invoke(capturedIndex));

        // === Collider for VR raycast (full width) ===
        var col = item.Root.AddComponent<BoxCollider>();
        col.size = new Vector3(_width, _itemHeight, 10);
        col.center = new Vector3(0, 0, -5);

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
        item.PlayingIcon = CreatePlayingIcon(item.Root.transform, _topGap, _imageHeight);
        item.PlayingIcon.SetActive(isActive);

        // === Title Text (bottom, margin riêng) ===
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(item.Root.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 0);
        titleRT.anchorMax = new Vector2(1, 0);
        titleRT.pivot = new Vector2(0, 0);
        titleRT.offsetMin = new Vector2(TITLE_MARGIN_H, 0);
        titleRT.offsetMax = new Vector2(-TITLE_MARGIN_H, _textHeight);

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

        return item;
    }

    private GameObject CreatePlayingIcon(Transform rootTransform, float topGap, float imageHeight)
    {
        float bgSize = PLAYING_ICON_SIZE * 1.875f;

        // Container
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

        // Icon sprite (centered, original size)
        var spriteObj = new GameObject("Icon");
        spriteObj.transform.SetParent(iconObj.transform, false);

        var spriteRT = spriteObj.AddComponent<RectTransform>();
        spriteRT.anchorMin = new Vector2(0.5f, 0.5f);
        spriteRT.anchorMax = new Vector2(0.5f, 0.5f);
        spriteRT.pivot = new Vector2(0.5f, 0.5f);
        spriteRT.sizeDelta = new Vector2(PLAYING_ICON_SIZE, PLAYING_ICON_SIZE);

        var iconImage = spriteObj.AddComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>(ICON_PLAYING);
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
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
