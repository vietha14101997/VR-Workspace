using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Detail Panel for Media Library - displays selected video info and actions.
/// Shows thumbnail preview, metadata, play button, and action buttons.
/// </summary>
public class RTTMediaDetail : MonoBehaviour
{
    #region Constants
    private const float PANEL_PADDING = 30f;
    private const float PREVIEW_HEIGHT = 280f;
    private const float TITLE_HEIGHT = 80f;
    private const float PLAY_BUTTON_HEIGHT = 70f;
    private const float METADATA_ROW_HEIGHT = 36f;
    private const float ACTION_BUTTON_HEIGHT = 50f;
    private const float SECTION_SPACING = 20f;
    private const float ITEM_SPACING = 10f;
    #endregion

    #region Events
    public event Action<MediaVideoInfo> OnPlayRequested;
    public event Action<MediaVideoInfo> OnToggleFavorite;
    public event Action<MediaVideoInfo> OnAddToPlaylist;
    #endregion

    #region Private Fields
    private RTTMediaLibraryController _controller;
    private float _width;
    private float _height;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private MediaVideoInfo? _currentVideo;

    // UI Elements
    private GameObject _contentContainer;
    private GameObject _emptyState;
    private Image _previewImage;
    private Image _projectionBadge;
    private TextMeshProUGUI _badgeText;
    private TextMeshProUGUI _titleText;
    private Button _playButton;
    private TextMeshProUGUI _playButtonText;
    private Image _favoriteIcon;
    private Button _favoriteButton;
    private Button _addToPlaylistButton;

    // Metadata labels
    private TextMeshProUGUI _formatValue;
    private TextMeshProUGUI _resolutionValue;
    private TextMeshProUGUI _projectionValue;
    private TextMeshProUGUI _durationValue;
    private TextMeshProUGUI _sizeValue;
    private TextMeshProUGUI _dateValue;
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
        ShowEmptyState();
    }

    private void BuildUI()
    {
        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        // Empty state (shown when no video selected)
        CreateEmptyState();

        // Content container (shown when video selected)
        CreateContentContainer();
    }

    private void CreateEmptyState()
    {
        _emptyState = new GameObject("EmptyState");
        _emptyState.transform.SetParent(transform, false);

        var emptyRT = _emptyState.AddComponent<RectTransform>();
        emptyRT.anchorMin = Vector2.zero;
        emptyRT.anchorMax = Vector2.one;
        emptyRT.offsetMin = Vector2.zero;
        emptyRT.offsetMax = Vector2.zero;

        // Center text
        GameObject textObj = new GameObject("EmptyText");
        textObj.transform.SetParent(_emptyState.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0.5f, 0.5f);
        textRT.anchorMax = new Vector2(0.5f, 0.5f);
        textRT.sizeDelta = new Vector2(_width - 60, 100);

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "Select a video to view details";
        text.font = _font;
        text.fontSize = 28;
        text.color = new Color(1, 1, 1, 0.5f);
        text.alignment = TextAlignmentOptions.Center;
    }

    private void CreateContentContainer()
    {
        _contentContainer = new GameObject("ContentContainer");
        _contentContainer.transform.SetParent(transform, false);

        var contentRT = _contentContainer.AddComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.offsetMin = new Vector2(PANEL_PADDING, PANEL_PADDING);
        contentRT.offsetMax = new Vector2(-PANEL_PADDING, -PANEL_PADDING);

        // Vertical layout
        var layout = _contentContainer.AddComponent<VerticalLayoutGroup>();
        layout.spacing = SECTION_SPACING;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 1. Preview Image
        CreatePreviewSection(_contentContainer.transform);

        // 2. Title
        CreateTitleSection(_contentContainer.transform);

        // 3. Play Button
        CreatePlayButton(_contentContainer.transform);

        // 4. Metadata Section
        CreateMetadataSection(_contentContainer.transform);

        // 5. Action Buttons
        CreateActionButtons(_contentContainer.transform);
    }

    private void CreatePreviewSection(Transform parent)
    {
        GameObject previewContainer = new GameObject("PreviewContainer");
        previewContainer.transform.SetParent(parent, false);

        var containerLE = previewContainer.AddComponent<LayoutElement>();
        containerLE.minHeight = PREVIEW_HEIGHT;
        containerLE.preferredHeight = PREVIEW_HEIGHT;

        // Preview background
        var bgImage = previewContainer.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.5f);

        // Preview image
        GameObject previewObj = new GameObject("PreviewImage");
        previewObj.transform.SetParent(previewContainer.transform, false);

        var previewRT = previewObj.AddComponent<RectTransform>();
        previewRT.anchorMin = Vector2.zero;
        previewRT.anchorMax = Vector2.one;
        previewRT.offsetMin = Vector2.zero;
        previewRT.offsetMax = Vector2.zero;

        _previewImage = previewObj.AddComponent<Image>();
        _previewImage.color = Color.white;
        _previewImage.preserveAspect = true;

        // Projection badge overlay
        CreateProjectionBadge(previewContainer.transform);
    }

    private void CreateProjectionBadge(Transform parent)
    {
        GameObject badgeObj = new GameObject("ProjectionBadge");
        badgeObj.transform.SetParent(parent, false);

        var badgeRT = badgeObj.AddComponent<RectTransform>();
        badgeRT.anchorMin = new Vector2(0, 1);
        badgeRT.anchorMax = new Vector2(0, 1);
        badgeRT.pivot = new Vector2(0, 1);
        badgeRT.anchoredPosition = new Vector2(10, -10);
        badgeRT.sizeDelta = new Vector2(60, 30);

        _projectionBadge = badgeObj.AddComponent<Image>();
        _projectionBadge.color = new Color(1, 0.5f, 0, 1); // Default orange

        // Badge text
        GameObject textObj = new GameObject("BadgeText");
        textObj.transform.SetParent(badgeObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        _badgeText = textObj.AddComponent<TextMeshProUGUI>();
        _badgeText.font = _font;
        _badgeText.fontSize = 18;
        _badgeText.fontStyle = FontStyles.Bold;
        _badgeText.color = Color.white;
        _badgeText.alignment = TextAlignmentOptions.Center;
    }

    private void CreateTitleSection(Transform parent)
    {
        GameObject titleContainer = new GameObject("TitleContainer");
        titleContainer.transform.SetParent(parent, false);

        var containerLE = titleContainer.AddComponent<LayoutElement>();
        containerLE.minHeight = TITLE_HEIGHT;
        containerLE.preferredHeight = TITLE_HEIGHT;

        // Title text
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(titleContainer.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.font = _font;
        _titleText.fontSize = 32;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = Color.white;
        _titleText.alignment = TextAlignmentOptions.TopLeft;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;
        _titleText.maxVisibleLines = 2;
    }

    private void CreatePlayButton(Transform parent)
    {
        GameObject buttonContainer = new GameObject("PlayButtonContainer");
        buttonContainer.transform.SetParent(parent, false);

        var containerLE = buttonContainer.AddComponent<LayoutElement>();
        containerLE.minHeight = PLAY_BUTTON_HEIGHT;
        containerLE.preferredHeight = PLAY_BUTTON_HEIGHT;

        // Button background
        GameObject buttonObj = new GameObject("PlayButton");
        buttonObj.transform.SetParent(buttonContainer.transform, false);

        var buttonRT = buttonObj.AddComponent<RectTransform>();
        buttonRT.anchorMin = Vector2.zero;
        buttonRT.anchorMax = Vector2.one;
        buttonRT.offsetMin = Vector2.zero;
        buttonRT.offsetMax = Vector2.zero;

        var buttonBg = buttonObj.AddComponent<Image>();
        buttonBg.color = _primaryColor;

        _playButton = buttonObj.AddComponent<Button>();
        _playButton.targetGraphic = buttonBg;
        _playButton.onClick.AddListener(OnPlayButtonClicked);

        // Button text
        GameObject textObj = new GameObject("ButtonText");
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        _playButtonText = textObj.AddComponent<TextMeshProUGUI>();
        _playButtonText.text = "PLAY";
        _playButtonText.font = _font;
        _playButtonText.fontSize = 32;
        _playButtonText.fontStyle = FontStyles.Bold;
        _playButtonText.color = Color.white;
        _playButtonText.alignment = TextAlignmentOptions.Center;

        // Hover effect
        var hoverController = buttonObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f));

        // VR Collider
        var collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(_width - PANEL_PADDING * 2, PLAY_BUTTON_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);
    }

    private void CreateMetadataSection(Transform parent)
    {
        GameObject metadataContainer = new GameObject("MetadataContainer");
        metadataContainer.transform.SetParent(parent, false);

        var layout = metadataContainer.AddComponent<VerticalLayoutGroup>();
        layout.spacing = ITEM_SPACING;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Create metadata rows
        _formatValue = CreateMetadataRow(metadataContainer.transform, "Format");
        _resolutionValue = CreateMetadataRow(metadataContainer.transform, "Resolution");
        _projectionValue = CreateMetadataRow(metadataContainer.transform, "Projection");
        _durationValue = CreateMetadataRow(metadataContainer.transform, "Duration");
        _sizeValue = CreateMetadataRow(metadataContainer.transform, "Size");
        _dateValue = CreateMetadataRow(metadataContainer.transform, "Added");
    }

    private TextMeshProUGUI CreateMetadataRow(Transform parent, string label)
    {
        GameObject rowObj = new GameObject($"Meta_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = METADATA_ROW_HEIGHT;
        rowLE.preferredHeight = METADATA_ROW_HEIGHT;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = label + ":";
        labelText.font = _font;
        labelText.fontSize = 24;
        labelText.color = new Color(1, 1, 1, 0.6f);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var labelLE = labelObj.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 1;

        // Value
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(rowObj.transform, false);

        var valueText = valueObj.AddComponent<TextMeshProUGUI>();
        valueText.text = "-";
        valueText.font = _font;
        valueText.fontSize = 24;
        valueText.color = Color.white;
        valueText.alignment = TextAlignmentOptions.MidlineRight;

        var valueLE = valueObj.AddComponent<LayoutElement>();
        valueLE.flexibleWidth = 1.5f;

        return valueText;
    }

    private void CreateActionButtons(Transform parent)
    {
        GameObject actionContainer = new GameObject("ActionContainer");
        actionContainer.transform.SetParent(parent, false);

        var containerLE = actionContainer.AddComponent<LayoutElement>();
        containerLE.minHeight = ACTION_BUTTON_HEIGHT + ITEM_SPACING + ACTION_BUTTON_HEIGHT;

        var layout = actionContainer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 15;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        // Favorite button
        _favoriteButton = CreateActionButton(actionContainer.transform, "Favorite", OnFavoriteClicked, out _favoriteIcon);

        // Add to playlist button
        Image dummyIcon;
        _addToPlaylistButton = CreateActionButton(actionContainer.transform, "+ Playlist", OnAddToPlaylistClicked, out dummyIcon);
    }

    private Button CreateActionButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, out Image iconImage)
    {
        GameObject buttonObj = new GameObject($"Btn_{label}");
        buttonObj.transform.SetParent(parent, false);

        var buttonLE = buttonObj.AddComponent<LayoutElement>();
        buttonLE.minHeight = ACTION_BUTTON_HEIGHT;
        buttonLE.preferredHeight = ACTION_BUTTON_HEIGHT;
        buttonLE.flexibleWidth = 1;

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = new Color(1, 1, 1, 0.1f);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.onClick.AddListener(onClick);

        // Icon placeholder (left side)
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(buttonObj.transform, false);

        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0, 0.5f);
        iconRT.anchorMax = new Vector2(0, 0.5f);
        iconRT.pivot = new Vector2(0, 0.5f);
        iconRT.anchoredPosition = new Vector2(15, 0);
        iconRT.sizeDelta = new Vector2(30, 30);

        iconImage = iconObj.AddComponent<Image>();
        iconImage.color = Color.white;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(50, 0);
        textRT.offsetMax = new Vector2(-10, 0);

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = _font;
        text.fontSize = 22;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        // Hover effect
        var hoverController = buttonObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.03f));

        // VR Collider
        var collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(150, ACTION_BUTTON_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);

        return button;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Set video to display in detail panel.
    /// </summary>
    public void SetVideo(MediaVideoInfo video)
    {
        _currentVideo = video;
        _emptyState.SetActive(false);
        _contentContainer.SetActive(true);

        UpdateDisplay();
        RequestThumbnail(video.Path);
    }

    /// <summary>
    /// Clear detail panel (show empty state).
    /// </summary>
    public void ClearVideo()
    {
        _currentVideo = null;
        ShowEmptyState();
    }

    /// <summary>
    /// Update favorite state display.
    /// </summary>
    public void UpdateFavoriteState(bool isFavorite)
    {
        if (_favoriteIcon != null)
        {
            _favoriteIcon.color = isFavorite ? new Color(1, 0.8f, 0, 1) : Color.white;
        }
    }
    #endregion

    #region Private Methods
    private void ShowEmptyState()
    {
        _emptyState.SetActive(true);
        _contentContainer.SetActive(false);
    }

    private void UpdateDisplay()
    {
        if (!_currentVideo.HasValue) return;

        var video = _currentVideo.Value;

        // Title
        _titleText.text = video.Title;

        // Metadata
        _formatValue.text = video.Format.ToString().ToUpper();
        _resolutionValue.text = $"{video.Width} x {video.Height}";
        _projectionValue.text = GetProjectionDisplayName(video.Projection);
        _durationValue.text = video.FormattedDuration;
        _sizeValue.text = video.FormattedSize;
        _dateValue.text = video.DateAdded.ToString("MMM dd, yyyy");

        // Projection badge
        UpdateProjectionBadge(video.Projection);

        // Favorite state
        UpdateFavoriteState(video.IsFavorite);
    }

    private void UpdateProjectionBadge(VideoProjectionType projection)
    {
        string badge = ProjectionDetector.GetProjectionBadge(projection);
        Color badgeColor = ProjectionDetector.GetProjectionBadgeColor(projection);

        if (string.IsNullOrEmpty(badge))
        {
            _projectionBadge.gameObject.SetActive(false);
        }
        else
        {
            _projectionBadge.gameObject.SetActive(true);
            _projectionBadge.color = badgeColor;
            _badgeText.text = badge;

            // Adjust badge width based on text
            var badgeRT = _projectionBadge.GetComponent<RectTransform>();
            badgeRT.sizeDelta = new Vector2(badge.Length * 15 + 20, 30);
        }
    }

    private string GetProjectionDisplayName(VideoProjectionType projection)
    {
        switch (projection)
        {
            case VideoProjectionType.Flat: return "2D Flat";
            case VideoProjectionType.Dome180: return "180° VR";
            case VideoProjectionType.Sphere360: return "360° VR";
            case VideoProjectionType.SideBySide3D: return "3D Side-by-Side";
            case VideoProjectionType.OverUnder3D: return "3D Over-Under";
            case VideoProjectionType.VR180Stereo: return "VR180 3D";
            default: return projection.ToString();
        }
    }

    private void RequestThumbnail(string path)
    {
        var thumbnailService = FindObjectOfType<FileThumbnailService>();
        if (thumbnailService != null)
        {
            StartCoroutine(LoadThumbnailCoroutine(path, thumbnailService));
        }
    }

    private IEnumerator LoadThumbnailCoroutine(string path, FileThumbnailService service)
    {
        // Clear current preview
        _previewImage.sprite = null;
        _previewImage.color = new Color(0.2f, 0.2f, 0.2f, 1);

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

        Sprite thumbnail = null;
        bool loaded = false;

        // Request larger thumbnail for detail view (skip overlay for clean preview)
        service.RequestThumbnail(
            mockFile,
            512,
            (sprite) =>
            {
                thumbnail = sprite;
                loaded = true;
            },
            () => loaded = true,  // On failed
            0,  // High priority for detail panel
            true  // Skip overlay for clean preview
        );

        // Wait for callback
        float timeout = 5f;
        while (!loaded && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Apply thumbnail if still showing same video
        if (_currentVideo.HasValue && _currentVideo.Value.Path == path && thumbnail != null)
        {
            _previewImage.sprite = thumbnail;
            _previewImage.color = Color.white;
        }
    }
    #endregion

    #region Event Handlers
    private void OnPlayButtonClicked()
    {
        if (_currentVideo.HasValue)
        {
            OnPlayRequested?.Invoke(_currentVideo.Value);
        }
    }

    private void OnFavoriteClicked()
    {
        if (_currentVideo.HasValue)
        {
            OnToggleFavorite?.Invoke(_currentVideo.Value);
        }
    }

    private void OnAddToPlaylistClicked()
    {
        if (_currentVideo.HasValue)
        {
            OnAddToPlaylist?.Invoke(_currentVideo.Value);
        }
    }
    #endregion
}
