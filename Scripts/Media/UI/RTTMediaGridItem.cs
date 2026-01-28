using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Grid item component for displaying a video in the media library.
/// Shows thumbnail, duration badge, resolution badge, title, and subtitle.
/// Design based on RTTFileGridItem with glass background styling.
/// </summary>
public class RTTMediaGridItem : MonoBehaviour
{
    #region Constants
    public const float ITEM_WIDTH = 395f;
    public const float ITEM_HEIGHT = 320f;
    public const float THUMBNAIL_HEIGHT = 222f;  // 16:9 aspect
    public const float TITLE_HEIGHT = 98f;
    public const float CORNER_RADIUS = 20f;
    public const float CARD_PADDING = 12f;
    #endregion

    #region Events
    public event Action<MediaVideoInfo> OnClicked;
    public event Action<MediaVideoInfo> OnDoubleClicked;
    public event Action<MediaVideoInfo> OnSelected;
    #endregion

    #region Properties
    public MediaVideoInfo VideoInfo { get; private set; }
    public bool IsSelected { get; private set; }
    #endregion

    #region Private Fields
    private Image _cardBackground;
    private Image _thumbnailImage;
    private Image _thumbnailBackground;
    private TextMeshProUGUI _durationText;
    private GameObject _durationBadge;
    private TextMeshProUGUI _resolutionText;
    private GameObject _resolutionBadge;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _subtitleText;
    private Image _selectionBorder;
    private GameObject _checkbox;
    private Image _checkmark;
    private Image _favoriteIcon;

    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private float _lastClickTime;
    private const float DOUBLE_CLICK_TIME = 0.3f;

    private HoverEffectController _hoverController;

    // Cached rounded rect sprite
    private static Sprite _cachedRoundedSprite;
    private static Sprite _cachedBadgeSprite;
    #endregion

    #region Initialization
    public void Initialize(TMP_FontAsset font, Color primary, Color accent)
    {
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;

        BuildUI();
    }

    private void BuildUI()
    {
        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(ITEM_WIDTH, ITEM_HEIGHT);

        // 1. Glass Background for entire card
        CreateCardBackground();

        // 2. Use VerticalLayoutGroup for content
        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 0;
        layout.padding = new RectOffset((int)CARD_PADDING, (int)CARD_PADDING, (int)CARD_PADDING, (int)CARD_PADDING);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 3. Thumbnail Container (top section)
        CreateThumbnailSection();

        // 4. Title Section (bottom section)
        CreateTitleSection();

        // 5. Selection border (covers entire card)
        CreateSelectionBorder();

        // 6. Add interaction components
        CreateInteraction();
    }

    private void CreateCardBackground()
    {
        _cardBackground = gameObject.AddComponent<Image>();
        _cardBackground.sprite = GetRoundedRectSprite(CORNER_RADIUS);
        _cardBackground.type = Image.Type.Sliced;
        _cardBackground.raycastTarget = true;

        // Apply glass shader
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_Aspect", ITEM_WIDTH / ITEM_HEIGHT);
            mat.SetFloat("_CornerRadius", CORNER_RADIUS / ITEM_HEIGHT);
            mat.SetFloat("_EdgePadding", 0.01f);
            mat.SetColor("_ColorA", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.35f));
            mat.SetColor("_ColorB", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.15f));
            mat.SetFloat("_GlassAlpha", 0.25f);
            mat.SetFloat("_FresnelStrength", 0.12f);
            _cardBackground.material = mat;
            _cardBackground.color = Color.white;
        }
        else
        {
            _cardBackground.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.25f);
        }
    }

    private void CreateThumbnailSection()
    {
        float innerCornerRadius = CORNER_RADIUS - CARD_PADDING;
        float thumbnailHeight = THUMBNAIL_HEIGHT - CARD_PADDING;

        GameObject thumbContainer = new GameObject("ThumbnailContainer");
        thumbContainer.transform.SetParent(transform, false);

        var thumbRT = thumbContainer.AddComponent<RectTransform>();

        var thumbLE = thumbContainer.AddComponent<LayoutElement>();
        thumbLE.preferredHeight = thumbnailHeight;
        thumbLE.flexibleWidth = 1;

        // Rounded background for thumbnail area
        _thumbnailBackground = thumbContainer.AddComponent<Image>();
        _thumbnailBackground.sprite = GetRoundedRectSprite(innerCornerRadius);
        _thumbnailBackground.type = Image.Type.Sliced;
        _thumbnailBackground.color = new Color(0.08f, 0.08f, 0.1f, 1f);

        // Add RectMask2D for rounded clipping
        var mask = thumbContainer.AddComponent<RectMask2D>();
        mask.softness = new Vector2Int(2, 2);

        // Thumbnail Image
        GameObject thumbObj = new GameObject("Thumbnail");
        thumbObj.transform.SetParent(thumbContainer.transform, false);

        var thumbImgRT = thumbObj.AddComponent<RectTransform>();
        thumbImgRT.anchorMin = Vector2.zero;
        thumbImgRT.anchorMax = Vector2.one;
        thumbImgRT.offsetMin = Vector2.zero;
        thumbImgRT.offsetMax = Vector2.zero;

        _thumbnailImage = thumbObj.AddComponent<Image>();
        _thumbnailImage.color = new Color(0.15f, 0.15f, 0.18f, 1f);
        _thumbnailImage.preserveAspect = false; // Fill the area

        // Duration Badge (top-left) - like target design
        CreateDurationBadge(thumbContainer.transform);

        // Resolution Badge (bottom-right) - like "1080p" in target
        CreateResolutionBadge(thumbContainer.transform);

        // Favorite icon (top-right)
        CreateFavoriteIcon(thumbContainer.transform);
    }

    private void CreateDurationBadge(Transform parent)
    {
        // Duration badge in top-left corner (like "1m 25s" in target)
        _durationBadge = new GameObject("DurationBadge");
        _durationBadge.transform.SetParent(parent, false);

        var badgeRT = _durationBadge.AddComponent<RectTransform>();
        badgeRT.anchorMin = new Vector2(0, 1);
        badgeRT.anchorMax = new Vector2(0, 1);
        badgeRT.pivot = new Vector2(0, 1);
        badgeRT.anchoredPosition = new Vector2(10, -10);
        badgeRT.sizeDelta = new Vector2(75, 28);

        // Rounded background
        var badgeBg = _durationBadge.AddComponent<Image>();
        badgeBg.sprite = GetBadgeSprite();
        badgeBg.type = Image.Type.Sliced;
        badgeBg.color = new Color(0f, 0f, 0f, 0.7f);

        // Duration text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_durationBadge.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8, 0);
        textRT.offsetMax = new Vector2(-8, 0);

        _durationText = textObj.AddComponent<TextMeshProUGUI>();
        _durationText.text = "0:00";
        _durationText.font = _font;
        _durationText.fontSize = 18;
        _durationText.fontStyle = FontStyles.Bold;
        _durationText.color = Color.white;
        _durationText.alignment = TextAlignmentOptions.Center;
    }

    private void CreateResolutionBadge(Transform parent)
    {
        // Resolution badge in bottom-right corner (like "1080p" in target)
        _resolutionBadge = new GameObject("ResolutionBadge");
        _resolutionBadge.transform.SetParent(parent, false);

        var badgeRT = _resolutionBadge.AddComponent<RectTransform>();
        badgeRT.anchorMin = new Vector2(1, 0);
        badgeRT.anchorMax = new Vector2(1, 0);
        badgeRT.pivot = new Vector2(1, 0);
        badgeRT.anchoredPosition = new Vector2(-10, 10);
        badgeRT.sizeDelta = new Vector2(65, 26);

        // Semi-transparent background with accent color
        var badgeBg = _resolutionBadge.AddComponent<Image>();
        badgeBg.sprite = GetBadgeSprite();
        badgeBg.type = Image.Type.Sliced;
        badgeBg.color = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.85f);

        // Resolution text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_resolutionBadge.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(6, 0);
        textRT.offsetMax = new Vector2(-6, 0);

        _resolutionText = textObj.AddComponent<TextMeshProUGUI>();
        _resolutionText.text = "1080p";
        _resolutionText.font = _font;
        _resolutionText.fontSize = 16;
        _resolutionText.fontStyle = FontStyles.Bold;
        _resolutionText.color = Color.white;
        _resolutionText.alignment = TextAlignmentOptions.Center;

        // Hide by default (only show if resolution info available)
        _resolutionBadge.SetActive(false);
    }

    private void CreateFavoriteIcon(Transform parent)
    {
        GameObject favObj = new GameObject("Favorite");
        favObj.transform.SetParent(parent, false);

        var favRT = favObj.AddComponent<RectTransform>();
        favRT.anchorMin = new Vector2(1, 1);
        favRT.anchorMax = new Vector2(1, 1);
        favRT.pivot = new Vector2(1, 1);
        favRT.anchoredPosition = new Vector2(-10, -10);
        favRT.sizeDelta = new Vector2(28, 28);

        _favoriteIcon = favObj.AddComponent<Image>();
        _favoriteIcon.color = new Color(1f, 0.85f, 0.2f);  // Gold/Yellow

        // Load star icon
        var starSprite = Resources.Load<Sprite>("icon_star");
        if (starSprite != null)
        {
            _favoriteIcon.sprite = starSprite;
            _favoriteIcon.preserveAspect = true;
        }

        favObj.SetActive(false);  // Hidden by default
    }

    private void CreateSelectionBorder()
    {
        // Selection border covers the entire card
        GameObject borderObj = new GameObject("SelectionBorder");
        borderObj.transform.SetParent(transform, false);

        var borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-3, -3);
        borderRT.offsetMax = new Vector2(3, 3);

        // Ignore layout so it stays as overlay
        var layoutIgnorer = borderObj.AddComponent<LayoutElement>();
        layoutIgnorer.ignoreLayout = true;

        _selectionBorder = borderObj.AddComponent<Image>();
        _selectionBorder.sprite = GetRoundedRectSprite(CORNER_RADIUS + 3);
        _selectionBorder.type = Image.Type.Sliced;
        _selectionBorder.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.8f);
        _selectionBorder.fillCenter = false; // Only show border, not fill
        _selectionBorder.raycastTarget = false;

        borderObj.SetActive(false);
    }

    private void CreateTitleSection()
    {
        float titleHeight = TITLE_HEIGHT - CARD_PADDING;

        GameObject titleContainer = new GameObject("TitleContainer");
        titleContainer.transform.SetParent(transform, false);

        var titleRT = titleContainer.AddComponent<RectTransform>();

        var titleLE = titleContainer.AddComponent<LayoutElement>();
        titleLE.preferredHeight = titleHeight;
        titleLE.flexibleWidth = 1;

        // No background - the card background shows through

        // Use vertical layout for title + subtitle
        var layout = titleContainer.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = 4f;
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Title Text (video name)
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(titleContainer.transform, false);

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Video Title";
        _titleText.font = _font;
        _titleText.fontSize = 24;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = Color.white;
        _titleText.alignment = TextAlignmentOptions.TopLeft;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;
        _titleText.enableWordWrapping = true;
        _titleText.maxVisibleLines = 2;

        var titleTextLE = titleObj.AddComponent<LayoutElement>();
        titleTextLE.preferredHeight = 52f;
        titleTextLE.flexibleWidth = 1;

        // Subtitle Text (duration info)
        GameObject subtitleObj = new GameObject("Subtitle");
        subtitleObj.transform.SetParent(titleContainer.transform, false);

        _subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
        _subtitleText.text = "0 m 0s";
        _subtitleText.font = _font;
        _subtitleText.fontSize = 18;
        _subtitleText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        _subtitleText.alignment = TextAlignmentOptions.TopLeft;

        var subtitleLE = subtitleObj.AddComponent<LayoutElement>();
        subtitleLE.preferredHeight = 22f;
        subtitleLE.flexibleWidth = 1;
    }

    private void CreateInteraction()
    {
        // Button
        var button = gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(HandleClick);

        // BoxCollider for VR
        var collider = gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(ITEM_WIDTH, ITEM_HEIGHT, 10);
        collider.center = new Vector3(ITEM_WIDTH / 2, -ITEM_HEIGHT / 2, -5);

        // Hover Effects
        _hoverController = gameObject.AddComponent<HoverEffectController>();
        _hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f));
        _hoverController.AddEffect(new ZPopHoverEffect().WithPopAmount(0.02f));
    }
    #endregion

    #region Public Methods
    public void SetData(MediaVideoInfo video)
    {
        VideoInfo = video;

        // Title
        _titleText.text = video.Title;

        // Duration badge (top-left of thumbnail)
        _durationText.text = video.FormattedDuration;

        // Subtitle (below title) - shows formatted duration
        _subtitleText.text = video.FormattedDuration;

        // Resolution badge - show if we have resolution info
        UpdateResolutionBadge(video);

        // Favorite icon
        if (_favoriteIcon != null)
        {
            _favoriteIcon.gameObject.SetActive(video.IsFavorite);
        }

        // Thumbnail - will be loaded async by FileThumbnailService
        _thumbnailImage.sprite = null;
        _thumbnailImage.color = new Color(0.15f, 0.15f, 0.18f, 1f);
    }

    private void UpdateResolutionBadge(MediaVideoInfo video)
    {
        if (_resolutionBadge == null || _resolutionText == null) return;

        // Try to determine resolution from video info
        string resText = "";

        if (video.Width > 0 && video.Height > 0)
        {
            // Determine resolution label based on height
            if (video.Height >= 2160)
                resText = "4K";
            else if (video.Height >= 1440)
                resText = "1440p";
            else if (video.Height >= 1080)
                resText = "1080p";
            else if (video.Height >= 720)
                resText = "720p";
            else if (video.Height >= 480)
                resText = "480p";
            else
                resText = $"{video.Height}p";
        }

        if (!string.IsNullOrEmpty(resText))
        {
            _resolutionText.text = resText;
            _resolutionBadge.SetActive(true);
        }
        else
        {
            _resolutionBadge.SetActive(false);
        }
    }

    public void SetThumbnail(Sprite thumbnail)
    {
        if (thumbnail != null)
        {
            _thumbnailImage.sprite = thumbnail;
            _thumbnailImage.color = Color.white;
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (_selectionBorder != null)
        {
            _selectionBorder.gameObject.SetActive(selected);
        }
    }

    public void SetEditMode(bool editMode, bool isChecked = false)
    {
        // TODO: Show/hide checkbox for multi-select in edit mode
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Generate a rounded rectangle sprite for backgrounds
    /// </summary>
    private static Sprite GetRoundedRectSprite(float radius)
    {
        if (_cachedRoundedSprite != null) return _cachedRoundedSprite;

        int size = 64;
        int cornerRadius = Mathf.RoundToInt(radius * 64f / ITEM_WIDTH);
        cornerRadius = Mathf.Clamp(cornerRadius, 4, 24);

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - halfSize + 0.5f);
                float dy = Mathf.Abs(y - halfSize + 0.5f);

                float innerHalfX = halfSize - cornerRadius;
                float innerHalfY = halfSize - cornerRadius;

                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - cornerRadius;

                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        int border = cornerRadius + 2;
        _cachedRoundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            Vector2.one * 0.5f,
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)
        );

        return _cachedRoundedSprite;
    }

    /// <summary>
    /// Generate a small rounded sprite for badges
    /// </summary>
    private static Sprite GetBadgeSprite()
    {
        if (_cachedBadgeSprite != null) return _cachedBadgeSprite;

        int size = 32;
        int cornerRadius = 8;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - halfSize + 0.5f);
                float dy = Mathf.Abs(y - halfSize + 0.5f);

                float innerHalfX = halfSize - cornerRadius;
                float innerHalfY = halfSize - cornerRadius;

                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - cornerRadius;

                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        int border = cornerRadius + 2;
        _cachedBadgeSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            Vector2.one * 0.5f,
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)
        );

        return _cachedBadgeSprite;
    }
    #endregion

    #region Event Handlers
    private void HandleClick()
    {
        float currentTime = Time.time;

        if (currentTime - _lastClickTime < DOUBLE_CLICK_TIME)
        {
            // Double click
            OnDoubleClicked?.Invoke(VideoInfo);
        }
        else
        {
            // Single click
            OnClicked?.Invoke(VideoInfo);
            OnSelected?.Invoke(VideoInfo);
        }

        _lastClickTime = currentTime;
    }
    #endregion
}
