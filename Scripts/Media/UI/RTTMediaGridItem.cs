using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Represents a single video item in the Media Library Grid View.
/// Cloned from RTTFileGridItem with modifications for video display:
/// - Thumbnail with duration badge (top-left)
/// - Resolution badge (bottom-right)
/// - Title and subtitle text
/// </summary>
public class RTTMediaGridItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    #region Constants
    // Card dimensions - synced with RTTFileGrid (395x320)
    public const float CELL_WIDTH = 395f;
    public const float CELL_HEIGHT = 320f;
    public const float THUMBNAIL_HEIGHT = 222f;  // 395 / 1.777 = 222 (16:9 aspect ratio)
    private const float TEXT_HEIGHT = 94f;       // Title area (320 - 222 - 4 gap)

    private const int THUMBNAIL_SIZE = 512;
    private const float HOVER_SCALE = 1.03f;
    private const float HOVER_ANIMATION_SPEED = 12f;
    private const float DOUBLE_CLICK_TIME = 0.3f;
    #endregion

    #region Private Fields
    // UI Elements
    private Image _thumbnailImage;
    private RectTransform _thumbnailRect;
    private RectTransform _thumbnailContainerRect;
    private TextMeshProUGUI _titleText;
    private Image _bgImage;

    // Duration Badge (bottom-right of thumbnail)
    private GameObject _durationBadge;
    private TextMeshProUGUI _durationText;

    // Resolution Badge (bottom-right of thumbnail, left of duration)
    private GameObject _resolutionBadge;
    private TextMeshProUGUI _resolutionText;

    // Favorite Icon (top-right of thumbnail)
    private GameObject _favoriteIcon;

    // Edit Mode Checkbox
    private GameObject _checkbox;
    private Image _checkmarkIcon;
    private HoverEffectController _checkboxHoverController;
    private bool _isSelected = false;
    private bool _isEditMode = false;
    private Action<string, bool> _onSelectionChanged;

    // Hover effect
    private HoverEffectController _hoverController;

    // Font and colors
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    // Current data
    private MediaVideoInfo _currentVideo;
    private string _currentFilePath;

    // Double click detection
    private float _lastClickTime;
    #endregion

    #region Properties
    public string FilePath => _currentVideo.Path;
    public MediaVideoInfo VideoInfo => _currentVideo;
    public bool IsSelected => _isSelected;
    #endregion

    #region Callbacks
    private Action<string> _onHoverEnter;
    private Action<string> _onHoverExit;
    private Action<MediaVideoInfo> _onClick;
    private Action<MediaVideoInfo> _onDoubleClick;
    #endregion

    #region Visual State
    private static readonly Color HoverColor = new Color(0f, 0f, 0f, 0.3f);
    private static readonly Color NormalColor = Color.clear;
    #endregion

    #region Initialization
    public void Initialize(TMP_FontAsset font, Color primaryColor, Color accentColor)
    {
        _font = font;
        _primaryColor = primaryColor;
        _accentColor = accentColor;
        BuildUI();
    }

    public void SetCallbacks(Action<string> onHoverEnter, Action<string> onHoverExit,
        Action<MediaVideoInfo> onClick, Action<MediaVideoInfo> onDoubleClick)
    {
        _onHoverEnter = onHoverEnter;
        _onHoverExit = onHoverExit;
        _onClick = onClick;
        _onDoubleClick = onDoubleClick;
    }

    public void SetSelectionCallback(Action<string, bool> onSelectionChanged)
    {
        _onSelectionChanged = onSelectionChanged;
    }
    #endregion

    #region Build UI
    private void BuildUI()
    {
        // 1. Background (for highlighting)
        _bgImage = gameObject.AddComponent<Image>();
        _bgImage.sprite = GetRoundedRectSprite();
        _bgImage.type = Image.Type.Sliced;
        _bgImage.color = NormalColor;
        _bgImage.raycastTarget = true;

        // 2. Thumbnail Container - positioned at top, full width
        CreateThumbnailContainer();

        // 3. Text Container - positioned below thumbnail
        CreateTextContainer();

        // 4. Add Hover Effect Controller
        _hoverController = gameObject.AddComponent<HoverEffectController>();
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(HOVER_SCALE)
            .WithTransitionDuration(1f / HOVER_ANIMATION_SPEED);
        _hoverController.AddEffect(scaleEffect);

        // 5. Create Edit Mode Checkbox
        CreateCheckbox();
    }

    private void CreateThumbnailContainer()
    {
        GameObject container = new GameObject("ThumbnailContainer");
        container.transform.SetParent(transform, false);
        _thumbnailContainerRect = container.AddComponent<RectTransform>();

        // Position at top, stretch full width, fixed height
        _thumbnailContainerRect.anchorMin = new Vector2(0, 1);
        _thumbnailContainerRect.anchorMax = new Vector2(1, 1);
        _thumbnailContainerRect.pivot = new Vector2(0.5f, 1);
        _thumbnailContainerRect.anchoredPosition = Vector2.zero;
        _thumbnailContainerRect.sizeDelta = new Vector2(0, THUMBNAIL_HEIGHT); // Width from anchors, fixed height

        // Background for thumbnail area
        Image containerBg = container.AddComponent<Image>();
        containerBg.sprite = GetRoundedRectSprite();
        containerBg.type = Image.Type.Sliced;
        containerBg.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        containerBg.raycastTarget = false;

        // Mask for rounded corners clipping and crop overflow
        var mask = container.AddComponent<RectMask2D>();

        // Thumbnail Image - center crop to fill container (maintains aspect ratio)
        GameObject thumbObj = new GameObject("Thumbnail");
        thumbObj.transform.SetParent(container.transform, false);
        _thumbnailRect = thumbObj.AddComponent<RectTransform>();
        // Center anchored, will be sized by AspectRatioFitter
        _thumbnailRect.anchorMin = new Vector2(0.5f, 0.5f);
        _thumbnailRect.anchorMax = new Vector2(0.5f, 0.5f);
        _thumbnailRect.pivot = new Vector2(0.5f, 0.5f);
        _thumbnailRect.anchoredPosition = Vector2.zero;

        _thumbnailImage = thumbObj.AddComponent<Image>();
        _thumbnailImage.preserveAspect = true;  // Maintain aspect ratio
        _thumbnailImage.raycastTarget = false;
        _thumbnailImage.color = new Color(0.12f, 0.12f, 0.14f, 1f);

        // AspectRatioFitter with EnvelopeParent = cover/crop mode
        var aspectFitter = thumbObj.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        aspectFitter.aspectRatio = 16f / 9f;  // Default 16:9, will update when thumbnail loads

        // Duration Badge (top-left)
        CreateDurationBadge(container.transform);

        // Resolution Badge (bottom-right)
        CreateResolutionBadge(container.transform);

        // Favorite Icon (top-right)
        CreateFavoriteIcon(container.transform);
    }

    private void CreateDurationBadge(Transform parent)
    {
        _durationBadge = new GameObject("DurationBadge");
        _durationBadge.transform.SetParent(parent, false);

        // Position at bottom-right of thumbnail
        RectTransform rt = _durationBadge.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-6, 6);
        rt.sizeDelta = new Vector2(58, 22);

        // Background - semi-transparent dark
        Image bg = _durationBadge.AddComponent<Image>();
        bg.sprite = GetBadgeSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        bg.raycastTarget = false;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_durationBadge.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(4, 0);
        textRT.offsetMax = new Vector2(-4, 0);

        _durationText = textObj.AddComponent<TextMeshProUGUI>();
        _durationText.text = "0:00";
        if (_font != null) _durationText.font = _font;
        _durationText.fontSize = 14;
        _durationText.fontStyle = FontStyles.Bold;
        _durationText.color = Color.white;
        _durationText.alignment = TextAlignmentOptions.Center;
        _durationText.raycastTarget = false;
    }

    private void CreateResolutionBadge(Transform parent)
    {
        _resolutionBadge = new GameObject("ResolutionBadge");
        _resolutionBadge.transform.SetParent(parent, false);

        RectTransform rt = _resolutionBadge.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-68, 6); // Left of duration badge (6 + 58 + 4 gap)
        rt.sizeDelta = new Vector2(50, 22);

        // Background with accent color (cyan/teal like target)
        Image bg = _resolutionBadge.AddComponent<Image>();
        bg.sprite = GetBadgeSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.2f, 0.8f, 0.9f, 0.95f); // Cyan color like target
        bg.raycastTarget = false;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_resolutionBadge.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(3, 0);
        textRT.offsetMax = new Vector2(-3, 0);

        _resolutionText = textObj.AddComponent<TextMeshProUGUI>();
        _resolutionText.text = "1080p";
        if (_font != null) _resolutionText.font = _font;
        _resolutionText.fontSize = 12;
        _resolutionText.fontStyle = FontStyles.Bold;
        _resolutionText.color = Color.white;
        _resolutionText.alignment = TextAlignmentOptions.Center;
        _resolutionText.raycastTarget = false;

        _resolutionBadge.SetActive(false);
    }

    private void CreateFavoriteIcon(Transform parent)
    {
        _favoriteIcon = new GameObject("FavoriteIcon");
        _favoriteIcon.transform.SetParent(parent, false);

        RectTransform rt = _favoriteIcon.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-6, -6);
        rt.sizeDelta = new Vector2(20, 20);

        Image icon = _favoriteIcon.AddComponent<Image>();
        icon.sprite = Resources.Load<Sprite>("icon_star");
        icon.color = new Color(1f, 0.85f, 0.2f);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        _favoriteIcon.SetActive(false);
    }

    private void CreateTextContainer()
    {
        GameObject container = new GameObject("TextContainer");
        container.transform.SetParent(transform, false);
        RectTransform containerRT = container.AddComponent<RectTransform>();

        // Position below thumbnail, stretch full width
        containerRT.anchorMin = new Vector2(0, 0);
        containerRT.anchorMax = new Vector2(1, 0);
        containerRT.pivot = new Vector2(0.5f, 0);
        containerRT.anchoredPosition = new Vector2(0, 0);
        containerRT.sizeDelta = new Vector2(0, TEXT_HEIGHT); // Width from anchors, fixed height

        // Title Text - fills the entire text container (no subtitle)
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(container.transform, false);
        RectTransform titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.pivot = new Vector2(0.5f, 0.5f);
        titleRT.offsetMin = new Vector2(8, 4);   // Left and bottom padding
        titleRT.offsetMax = new Vector2(-8, -4); // Right and top padding

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) _titleText.font = _font;
        _titleText.text = "Video Title";
        _titleText.fontSize = 24;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = Color.white;
        _titleText.alignment = TextAlignmentOptions.MidlineLeft;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;
        _titleText.enableWordWrapping = false;
        _titleText.maxVisibleLines = 1;
        _titleText.raycastTarget = false;
    }

    private void CreateCheckbox()
    {
        float checkboxSize = 32f;
        float offset = 6f;

        _checkbox = new GameObject("Checkbox");
        _checkbox.transform.SetParent(transform, false);

        RectTransform checkboxRT = _checkbox.AddComponent<RectTransform>();
        checkboxRT.anchorMin = new Vector2(0, 1);
        checkboxRT.anchorMax = new Vector2(0, 1);
        checkboxRT.pivot = new Vector2(0, 1);
        checkboxRT.sizeDelta = new Vector2(checkboxSize, checkboxSize);
        checkboxRT.anchoredPosition = new Vector2(offset, -offset);

        var layoutIgnorer = _checkbox.AddComponent<LayoutElement>();
        layoutIgnorer.ignoreLayout = true;

        // Background (glass style)
        Image checkboxBg = _checkbox.AddComponent<Image>();
        checkboxBg.raycastTarget = true;
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_Aspect", 1f);
            mat.SetFloat("_CornerRadius", 0.25f);
            mat.SetFloat("_EdgePadding", 0.02f);
            mat.SetColor("_ColorA", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.4f));
            mat.SetColor("_ColorB", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.15f));
            mat.SetFloat("_GlassAlpha", 0.25f);
            mat.SetFloat("_FresnelStrength", 0.15f);
            checkboxBg.material = mat;
            checkboxBg.color = Color.white;
        }

        // Checkmark icon
        GameObject checkmarkObj = new GameObject("Checkmark");
        checkmarkObj.transform.SetParent(_checkbox.transform, false);
        RectTransform checkmarkRT = checkmarkObj.AddComponent<RectTransform>();
        checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkmarkRT.offsetMin = checkmarkRT.offsetMax = Vector2.zero;

        _checkmarkIcon = checkmarkObj.AddComponent<Image>();
        _checkmarkIcon.sprite = Resources.Load<Sprite>("icon_check_mark");
        _checkmarkIcon.color = Color.white;
        _checkmarkIcon.preserveAspect = true;
        _checkmarkIcon.raycastTarget = false;
        checkmarkObj.SetActive(false);

        // Button for checkbox click
        Button checkboxButton = _checkbox.AddComponent<Button>();
        checkboxButton.transition = Selectable.Transition.None;
        checkboxButton.onClick.AddListener(OnCheckboxClicked);

        // Hover effect
        _checkboxHoverController = _checkbox.AddComponent<HoverEffectController>();
        _checkboxHoverController.TargetVisuals = _checkbox.transform;
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(1.15f)
            .WithTransitionDuration(0.1f);
        _checkboxHoverController.AddEffect(scaleEffect);

        _checkbox.SetActive(false);
    }
    #endregion

    #region Data Binding
    public void Bind(MediaVideoInfo video)
    {
        // Cancel previous thumbnail request
        if (!string.IsNullOrEmpty(_currentFilePath) && _currentFilePath != video.Path)
        {
            FileThumbnailService.Instance?.CancelRequest(_currentFilePath);
        }

        _currentVideo = video;
        _currentFilePath = video.Path;

        // Update title - show filename with extension
        if (_titleText != null)
        {
            string fileName = System.IO.Path.GetFileName(video.Path);
            if (string.IsNullOrEmpty(fileName))
                fileName = video.Title ?? "Untitled";
            _titleText.text = fileName;
        }

        // Determine media type from extension
        string ext = System.IO.Path.GetExtension(video.Path)?.ToLowerInvariant() ?? "";
        bool isVideo = ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".webm" || ext == ".mov" || ext == ".wmv" || ext == ".m4v" || ext == ".flv";
        bool isAudio = ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".aac" || ext == ".ogg" || ext == ".m4a" || ext == ".wma";
        bool isImage = !isVideo && !isAudio;

        // Update duration badge - only show for video/audio with duration > 0
        if (_durationBadge != null)
        {
            bool showDuration = (isVideo || isAudio) && video.Duration.TotalSeconds > 0;
            _durationBadge.SetActive(showDuration);
            if (showDuration && _durationText != null)
                _durationText.text = video.FormattedDuration;
        }

        // Update resolution badge - only show for video with resolution
        UpdateResolutionBadge(video, isVideo);

        // Update favorite icon
        if (_favoriteIcon != null)
            _favoriteIcon.SetActive(video.IsFavorite);

        // Reset thumbnail to placeholder
        if (_thumbnailImage != null)
        {
            _thumbnailImage.sprite = null;
            _thumbnailImage.color = new Color(0.15f, 0.15f, 0.18f, 1f);
        }

        // Request thumbnail
        RequestThumbnail(video);

        // Reset background
        if (_bgImage != null)
            _bgImage.color = NormalColor;
    }

    private void UpdateResolutionBadge(MediaVideoInfo video, bool isVideo = true)
    {
        if (_resolutionBadge == null || _resolutionText == null) return;

        // Only show resolution badge for videos with valid resolution
        if (!isVideo || video.Height <= 0)
        {
            _resolutionBadge.SetActive(false);
            return;
        }

        string resText = "";
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

        _resolutionText.text = resText;
        _resolutionBadge.SetActive(true);
    }

    private void RequestThumbnail(MediaVideoInfo video)
    {
        var thumbnailService = FileThumbnailService.Instance;
        if (thumbnailService == null) return;

        // Create MockFile for thumbnail service
        var mockFile = new MockFile
        {
            Path = video.Path,
            Name = video.Title,
            Type = System.IO.Path.GetExtension(video.Path).TrimStart('.').ToLower(),
            IsFolder = false,
            Modified = video.DateModified
        };

        thumbnailService.RequestThumbnail(
            mockFile,
            THUMBNAIL_SIZE,
            onSuccess: (sprite) =>
            {
                if (_currentFilePath == video.Path && sprite != null && _thumbnailImage != null)
                {
                    _thumbnailImage.sprite = sprite;
                    _thumbnailImage.color = Color.white;

                    // Update AspectRatioFitter based on actual sprite aspect ratio
                    var aspectFitter = _thumbnailImage.GetComponent<AspectRatioFitter>();
                    if (aspectFitter != null && sprite.texture != null)
                    {
                        float spriteAspect = (float)sprite.texture.width / sprite.texture.height;
                        const float targetAspect = 16f / 9f;

                        // If image is wider than 16:9 → crop horizontally (EnvelopeParent)
                        // If image is narrower (like 1:1 square album art) → show full image (FitInParent)
                        if (spriteAspect >= targetAspect)
                        {
                            aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                        }
                        else
                        {
                            aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                        }
                        aspectFitter.aspectRatio = spriteAspect;

                        // Force layout rebuild to apply AspectRatioFitter changes immediately
                        LayoutRebuilder.ForceRebuildLayoutImmediate(_thumbnailRect);
                    }
                }
            },
            onFailed: null,
            priority: 0,
            skipOverlay: false  // Show media icon overlay on thumbnails
        );
    }
    #endregion

    #region Pointer Events
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_bgImage != null)
            _bgImage.color = HoverColor;

        if (_isEditMode && _checkboxHoverController != null)
            _checkboxHoverController.SetForceHover(true);

        _onHoverEnter?.Invoke(_currentFilePath);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_bgImage != null)
            _bgImage.color = NormalColor;

        if (_isEditMode && _checkboxHoverController != null)
            _checkboxHoverController.SetForceHover(false);

        _onHoverExit?.Invoke(_currentFilePath);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        float currentTime = Time.time;

        if (currentTime - _lastClickTime < DOUBLE_CLICK_TIME)
        {
            _onDoubleClick?.Invoke(_currentVideo);
        }
        else
        {
            _onClick?.Invoke(_currentVideo);
        }

        _lastClickTime = currentTime;
    }
    #endregion

    #region Edit Mode
    public void SetEditMode(bool editMode)
    {
        _isEditMode = editMode;
        if (_checkbox != null)
            _checkbox.SetActive(editMode);

        if (!editMode)
        {
            _isSelected = false;
            UpdateCheckmarkVisual();
        }
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateCheckmarkVisual();
    }

    private void OnCheckboxClicked()
    {
        _isSelected = !_isSelected;
        UpdateCheckmarkVisual();
        _onSelectionChanged?.Invoke(_currentFilePath, _isSelected);
    }

    private void UpdateCheckmarkVisual()
    {
        if (_checkmarkIcon != null)
            _checkmarkIcon.gameObject.SetActive(_isSelected);
    }
    #endregion

    #region Public API
    public void ClearHoverState()
    {
        if (_bgImage != null)
            _bgImage.color = NormalColor;
    }

    public void OnRecycle()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            FileThumbnailService.Instance?.CancelRequest(_currentFilePath);
        }
        ClearHoverState();

        if (_hoverController != null)
        {
            _hoverController.ResetHoverState(immediate: true);
        }
    }
    #endregion

    #region Helper Methods
    private static Sprite _cachedRoundedSprite;
    private static Sprite _cachedBadgeSprite;

    private static Sprite GetRoundedRectSprite()
    {
        if (_cachedRoundedSprite != null) return _cachedRoundedSprite;

        int size = 64;
        int radius = 16;
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

                float innerHalfX = halfSize - radius;
                float innerHalfY = halfSize - radius;

                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - radius;

                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        int border = radius + 2;
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

    private static Sprite GetBadgeSprite()
    {
        if (_cachedBadgeSprite != null) return _cachedBadgeSprite;

        int size = 32;
        int radius = 8;
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

                float innerHalfX = halfSize - radius;
                float innerHalfY = halfSize - radius;

                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - radius;

                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        int border = radius + 2;
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
}
