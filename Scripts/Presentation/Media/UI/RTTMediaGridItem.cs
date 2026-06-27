using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.Media.Data;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.RTT.Controllers;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.Media.UI
{
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

        // Layout calculation:
        // Bottom area (below thumbnail) = 320 - 222 = 98px
        // Text height (single line, fontSize 32) = 60px (line height ≈ 40px + padding)
        // Remaining spacing = 98 - 60 = 38px
        // Divide spacing into 3 parts: 38 / 3 ≈ 13px
        // TOP_PADDING = 1 part = 13px, Bottom spacing = 2 parts = 25px
        private const float ACTUAL_TEXT_HEIGHT = 60f;  // Single line text height (fontSize 32)
        private const float BOTTOM_AREA = 98f;         // CELL_HEIGHT - THUMBNAIL_HEIGHT
        private const float SPACING = 38f;             // BOTTOM_AREA - ACTUAL_TEXT_HEIGHT
        private const float TOP_PADDING = 13f;         // SPACING / 3 (1 part for top)
        private const float TEXT_HEIGHT = 85f;         // ACTUAL_TEXT_HEIGHT + 2 parts (60 + 25)

        private const int THUMBNAIL_SIZE = 512;
        private const float HOVER_SCALE = 1.03f;
        private const float HOVER_ANIMATION_SPEED = 12f;
        private const float DOUBLE_CLICK_TIME = 0.3f;
        private const float MARQUEE_SCROLL_SPEED = 80f;  // Faster than default 50
        #endregion

        #region Private Fields
        // UI Elements
        private Image _thumbnailImage;
        private RectTransform _thumbnailRect;
        private RectTransform _thumbnailContainerRect;
        private RoundedCorners _roundedCorners;
        private TextMeshProUGUI _titleText;
        private MarqueeText _titleMarquee;  // For hover scrolling
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
        private bool _isSelected = false;  // For edit mode checkbox
        private bool _isEditMode = false;
        private Action<string, bool> _onSelectionChanged;

        // Item Selection State (separate from edit mode)
        private bool _isItemSelected = false;

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
        public bool IsSelected => _isSelected;  // Edit mode checkbox selection
        public bool IsItemSelected => _isItemSelected;  // Item selection (non-edit mode)
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

            // 2b. Create badges outside mask (after thumbnail container)
            CreateBadges();

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

            // Position at top with padding, stretch full width, fixed height
            _thumbnailContainerRect.anchorMin = new Vector2(0, 1);
            _thumbnailContainerRect.anchorMax = new Vector2(1, 1);
            _thumbnailContainerRect.pivot = new Vector2(0.5f, 1);
            _thumbnailContainerRect.anchoredPosition = new Vector2(0, -TOP_PADDING); // Offset from top
            _thumbnailContainerRect.sizeDelta = new Vector2(0, THUMBNAIL_HEIGHT); // Width from anchors, fixed height

            // Background for thumbnail area
            Image containerBg = container.AddComponent<Image>();
            containerBg.sprite = GetRoundedRectSprite();
            containerBg.type = Image.Type.Sliced;
            containerBg.color = Color.clear;
            containerBg.raycastTarget = false;

            // RectMask2D for rectangular overflow clipping
            container.AddComponent<RectMask2D>();

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
            _thumbnailImage.material = RoundedCorners.SharedMaterial;

            _roundedCorners = thumbObj.AddComponent<RoundedCorners>();
            _roundedCorners.Radius = 16f;
            _roundedCorners.UseParentRect = true;  // Clip to container edges

            // AspectRatioFitter with EnvelopeParent = cover/crop mode
            var aspectFitter = thumbObj.AddComponent<AspectRatioFitter>();
            aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspectFitter.aspectRatio = 16f / 9f;  // Default 16:9, will update when thumbnail loads

            // Note: Duration and Resolution badges are created OUTSIDE the mask
            // They will be positioned relative to the thumbnail container but not clipped
            // (CreateBadges is called separately after this)
        }

        /// <summary>
        /// Create badges outside the mask so they render properly.
        /// Must be called after CreateThumbnailContainer.
        /// </summary>
        private void CreateBadges()
        {
            if (_thumbnailContainerRect == null) return;

            // Duration Badge (bottom-right of thumbnail area)
            CreateDurationBadge(transform);

            // Resolution Badge (left of duration badge)
            CreateResolutionBadge(transform);
        }

        private void CreateDurationBadge(Transform parent)
        {
            _durationBadge = new GameObject("DurationBadge");
            _durationBadge.transform.SetParent(parent, false);

            // Position at bottom-right of thumbnail area (1.5x size)
            // Thumbnail area: top-aligned with TOP_PADDING offset and THUMBNAIL_HEIGHT height
            // So thumbnail bottom is at Y = -(TOP_PADDING + THUMBNAIL_HEIGHT) from item top
            RectTransform rt = _durationBadge.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);  // Anchor to top-right of item
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 0);      // Pivot at bottom-right of badge
            // Position: 9px from right, at bottom of thumbnail + 9px padding (1.5x of original 6px)
            float thumbnailBottom = -(TOP_PADDING + THUMBNAIL_HEIGHT);
            rt.anchoredPosition = new Vector2(-9, thumbnailBottom + 9);
            rt.sizeDelta = new Vector2(87, 33);  // 1.5x of 58x22

            // Background - dark semi-transparent (like hover effect)
            Image bg = _durationBadge.AddComponent<Image>();
            bg.sprite = GetBadgeSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0f, 0f, 0f, 0.7f);  // Dark semi-transparent
            bg.raycastTarget = false;

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(_durationBadge.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(6, 0);  // 1.5x of 4
            textRT.offsetMax = new Vector2(-6, 0);

            _durationText = textObj.AddComponent<TextMeshProUGUI>();
            _durationText.text = "0:00";
            _durationText.font = GetValidFont();
            _durationText.fontSize = 21;  // 1.5x of 14
            _durationText.fontStyle = FontStyles.Bold;
            _durationText.color = Color.white;
            _durationText.alignment = TextAlignmentOptions.Center;
            _durationText.raycastTarget = false;
        }

        private void CreateResolutionBadge(Transform parent)
        {
            _resolutionBadge = new GameObject("ResolutionBadge");
            _resolutionBadge.transform.SetParent(parent, false);

            // Position at bottom-LEFT of thumbnail (symmetrical to duration on right) (1.5x size)
            RectTransform rt = _resolutionBadge.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);  // Anchor to top-left of item
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0);      // Pivot at bottom-left of badge
            float thumbnailBottom = -(TOP_PADDING + THUMBNAIL_HEIGHT);
            rt.anchoredPosition = new Vector2(9, thumbnailBottom + 9);  // 9px from left, same vertical as duration
            rt.sizeDelta = new Vector2(75, 33);  // 1.5x of 50x22

            // Background - dark semi-transparent (same as duration badge)
            Image bg = _resolutionBadge.AddComponent<Image>();
            bg.sprite = GetBadgeSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0f, 0f, 0f, 0.7f);  // Dark semi-transparent like duration
            bg.raycastTarget = false;

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(_resolutionBadge.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(5, 0);  // 1.5x of ~3
            textRT.offsetMax = new Vector2(-5, 0);

            _resolutionText = textObj.AddComponent<TextMeshProUGUI>();
            _resolutionText.text = "1080p";
            _resolutionText.font = GetValidFont();
            _resolutionText.fontSize = 18;  // 1.5x of 12
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

            // Load favorite icon - use icon_favorite which exists in Resources
            Sprite starSprite = Resources.Load<Sprite>("icon_favorite");
            if (starSprite == null)
                starSprite = Resources.Load<Sprite>("icon_star");
            if (starSprite == null)
                starSprite = Resources.Load<Sprite>("Icons/icon_favorite");

            if (starSprite != null)
            {
                icon.sprite = starSprite;
                icon.color = new Color(1f, 0.85f, 0.2f); // Gold color
            }
            else
            {
                // No sprite found - hide the icon completely instead of showing yellow square
                Debug.LogWarning("[RTTMediaGridItem] Could not load star icon - favorite indicator will be hidden");
                _favoriteIcon.SetActive(false);
                return;
            }

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
            _titleText.font = GetValidFont();
            _titleText.text = "Video Title";
            _titleText.fontSize = 32;  // Synced with RTTFileGridItem
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.color = Color.white;
            _titleText.alignment = TextAlignmentOptions.MidlineLeft;  // Changed from Center to MidlineLeft for correct Marquee positioning
            _titleText.overflowMode = TextOverflowModes.Ellipsis;
            _titleText.textWrappingMode = TextWrappingModes.NoWrap;
            _titleText.maxVisibleLines = 1;
            _titleText.raycastTarget = false;

            // Setup marquee for hover scrolling
            // Use explicit height since this uses anchor-based layout (not LayoutGroup)
            _titleMarquee = MarqueeText.Setup(_titleText, MARQUEE_SCROLL_SPEED, centerWhenFits: true, explicitHeight: ACTUAL_TEXT_HEIGHT);
            if (_titleMarquee != null)
            {
                _titleMarquee.SetHoverMode(true);  // Only scroll on hover
            }
        }

        private void CreateCheckbox()
        {
            float checkboxSize = 32f;
            float inset = 6f;  // Small inset from thumbnail edges

            _checkbox = new GameObject("Checkbox");
            _checkbox.transform.SetParent(transform, false);

            RectTransform checkboxRT = _checkbox.AddComponent<RectTransform>();
            checkboxRT.anchorMin = new Vector2(0, 1);
            checkboxRT.anchorMax = new Vector2(0, 1);
            checkboxRT.pivot = new Vector2(0, 1);
            checkboxRT.sizeDelta = new Vector2(checkboxSize, checkboxSize);
            // Position inset from top-left of thumbnail (thumbnail starts at TOP_PADDING from card top)
            checkboxRT.anchoredPosition = new Vector2(inset, -(TOP_PADDING + inset));

            var layoutIgnorer = _checkbox.AddComponent<LayoutElement>();
            layoutIgnorer.ignoreLayout = true;

            // Background (solid dark color matching item hover)
            Image checkboxBg = _checkbox.AddComponent<Image>();
            checkboxBg.raycastTarget = true;
            checkboxBg.sprite = GetCheckboxSprite();
            checkboxBg.type = Image.Type.Sliced;
            checkboxBg.color = HoverColor;  // Same as item hover: new Color(0f, 0f, 0f, 0.3f)

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
            // Use marquee if available for proper positioning
            string fileName = System.IO.Path.GetFileName(video.Path);
            if (string.IsNullOrEmpty(fileName))
                fileName = video.Title ?? "Untitled";

            if (_titleMarquee != null)
                _titleMarquee.SetText(fileName);
            else if (_titleText != null)
                _titleText.text = fileName;

            // Mark layout for deferred rebuild (avoids expensive ForceUpdateCanvases per item)
            if (_thumbnailRect != null) LayoutRebuilder.MarkLayoutForRebuild(_thumbnailRect);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);

            // Determine media type from extension
            string ext = System.IO.Path.GetExtension(video.Path)?.ToLowerInvariant() ?? "";
            bool isVideo = ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".webm" || ext == ".mov" || ext == ".wmv" || ext == ".m4v" || ext == ".flv";
            bool isAudio = ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".aac" || ext == ".ogg" || ext == ".m4a" || ext == ".wma";
            bool isImage = !isVideo && !isAudio;

            // Update duration badge - show for all videos/audio (show placeholder if no duration)
            if (_durationBadge != null)
            {
                bool showBadge = isVideo || isAudio;
                _durationBadge.SetActive(showBadge);

                if (showBadge && _durationText != null)
                {
                    if (video.Duration.TotalSeconds > 0)
                        _durationText.text = video.FormattedDuration;
                    else
                    {
                        _durationText.text = "--:--";  // Placeholder when duration unknown
                        // Fetch metadata to get duration
                        FetchDurationMetadata(video.Path, isVideo);
                    }

                    Debug.Log($"[RTTMediaGridItem] Duration badge for '{video.Title}': show={showBadge}, duration={video.Duration.TotalSeconds}s, text='{_durationText.text}', font={((_durationText.font != null) ? _durationText.font.name : "NULL")}");
                }
            }
            else
            {
                Debug.LogWarning($"[RTTMediaGridItem] _durationBadge is NULL for '{video.Title}'");
            }

            // Update resolution badge - only show for video with resolution
            UpdateResolutionBadge(video, isVideo);

            // Update favorite icon
            // Favorite icon removed from grid items
            // if (_favoriteIcon != null)
            //     _favoriteIcon.SetActive(video.IsFavorite);

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

        /// <summary>
        /// Fetch duration metadata for video/audio files asynchronously.
        /// Updates the duration badge when metadata is received.
        /// </summary>
        private void FetchDurationMetadata(string filePath, bool isVideo)
        {
            if (FileMetadataService.Instance == null) return;

            if (isVideo)
            {
                FileMetadataService.Instance.GetVideoMetadata(filePath, (metadata) =>
                {
                    // Verify still showing same file
                    if (_currentFilePath != filePath) return;
                    if (this == null || _durationText == null) return;

                    if (metadata.Duration.TotalSeconds > 0)
                    {
                        _durationText.text = FormatDuration(metadata.Duration);
                        // Update cached video info
                        _currentVideo.Duration = metadata.Duration;
                        _currentVideo.Width = metadata.Width;
                        _currentVideo.Height = metadata.Height;
                        // Also update resolution badge now that we have dimensions
                        UpdateResolutionBadge(_currentVideo, true);
                    }
                });
            }
            else // Audio
            {
                FileMetadataService.Instance.GetAudioMetadata(filePath, (metadata) =>
                {
                    // Verify still showing same file
                    if (_currentFilePath != filePath) return;
                    if (this == null || _durationText == null) return;

                    if (metadata.Duration.TotalSeconds > 0)
                    {
                        _durationText.text = FormatDuration(metadata.Duration);
                        _currentVideo.Duration = metadata.Duration;
                    }
                });
            }
        }

        private string FormatDuration(System.TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            else
                return $"{duration.Minutes}:{duration.Seconds:D2}";
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
                    // Debug: Log all conditions
                    bool pathMatch = (_currentFilePath == video.Path);
                    bool spriteValid = (sprite != null);
                    bool imageValid = (_thumbnailImage != null);

                    if (!pathMatch)
                    {
                        Debug.LogWarning($"[RTTMediaGridItem] Thumbnail arrived but path mismatch: expected={video.Path}, current={_currentFilePath}");
                    }

                    if (pathMatch && spriteValid && imageValid)
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
                                if (_roundedCorners != null) _roundedCorners.UseParentRect = true;
                            }
                            else
                            {
                                aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                                if (_roundedCorners != null) _roundedCorners.UseParentRect = false;
                            }
                            aspectFitter.aspectRatio = spriteAspect;

                            // Mark for deferred layout rebuild - avoids blocking main thread
                            // Canvas will batch these updates naturally
                            LayoutRebuilder.MarkLayoutForRebuild(_thumbnailRect);
                        }
                    }
                },
                onFailed: () =>
                {
                    // Log thumbnail failure for debugging
                    Debug.LogWarning($"[RTTMediaGridItem] Thumbnail failed for {_currentFilePath}");
                },
                priority: 0,
                skipOverlay: false  // Show media icon overlay on thumbnails
            );
        }
        #endregion

        #region Pointer Events
        public void OnPointerEnter(PointerEventData eventData)
        {
            // Skip hover interaction if this item is selected (but allow in edit mode)
            if (_isItemSelected && !_isEditMode) return;

            if (_bgImage != null)
                _bgImage.color = HoverColor;

            if (_isEditMode && _checkboxHoverController != null)
                _checkboxHoverController.SetForceHover(true);

            // Start marquee scroll on hover
            _titleMarquee?.StartScroll();

            _onHoverEnter?.Invoke(_currentFilePath);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Skip hover interaction if this item is selected (but allow in edit mode)
            if (_isItemSelected && !_isEditMode) return;

            if (_bgImage != null)
                _bgImage.color = NormalColor;

            if (_isEditMode && _checkboxHoverController != null)
                _checkboxHoverController.SetForceHover(false);

            // Stop marquee scroll on exit
            _titleMarquee?.StopScroll();

            _onHoverExit?.Invoke(_currentFilePath);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Skip click interaction if this item is already selected (but allow in edit mode)
            if (_isItemSelected && !_isEditMode) return;

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

        #region Item Selection (Non-Edit Mode)
        /// <summary>
        /// Set item selection state (not edit mode checkbox).
        /// When selected: activates hover effects and blocks pointer interactions.
        /// </summary>
        public void SetItemSelected(bool selected)
        {
            _isItemSelected = selected;

            // Force hover state on main hover controller
            if (_hoverController != null)
            {
                _hoverController.SetForceHover(selected);
            }

            // Update background visual
            if (_bgImage != null)
            {
                _bgImage.color = selected ? HoverColor : NormalColor;
            }

            // Start/stop marquee based on selection
            if (selected)
            {
                _titleMarquee?.StartScroll();
            }
            else
            {
                _titleMarquee?.StopScroll();
            }
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

            // Reset item selection state to ensure clean state when recycled
            _isItemSelected = false;

            if (_hoverController != null)
            {
                _hoverController.ResetHoverState(immediate: true);
            }
        }
        #endregion

        #region Helper Methods
        private static Sprite _cachedRoundedSprite;
        private static Sprite _cachedCheckboxSprite;
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

        private static Sprite GetCheckboxSprite()
        {
            if (_cachedCheckboxSprite != null) return _cachedCheckboxSprite;

            int size = 64;
            int radius = 6;  // Small radius for square-ish checkbox with slight rounding
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
            _cachedCheckboxSprite = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                Vector2.one * 0.5f,
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border)
            );

            return _cachedCheckboxSprite;
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

        // Static cached font for fallback
        private static TMP_FontAsset _cachedFallbackFont;
        private static bool _fontLookupDone = false;

        /// <summary>
        /// Get a valid TMP font - use _font if available, otherwise use cached fallback.
        /// </summary>
        private TMP_FontAsset GetValidFont()
        {
            if (_font != null) return _font;

            // Return cached fallback if already found
            if (_fontLookupDone && _cachedFallbackFont != null)
                return _cachedFallbackFont;

            // Try multiple fallback options
            TMP_FontAsset defaultFont = null;

            // Option 1: TMP Settings default font
            defaultFont = TMP_Settings.defaultFontAsset;
            if (defaultFont != null)
            {
                _cachedFallbackFont = defaultFont;
                _fontLookupDone = true;
                return defaultFont;
            }

            // Option 2: Try common font paths
            string[] fontPaths = new string[]
            {
                "Fonts & Materials/LiberationSans SDF",
                "Fonts/LiberationSans SDF",
                "LiberationSans SDF",
                "Fonts/Roboto-Regular SDF",
                "Fonts/Arial SDF"
            };

            foreach (var path in fontPaths)
            {
                defaultFont = Resources.Load<TMP_FontAsset>(path);
                if (defaultFont != null)
                {
                    _cachedFallbackFont = defaultFont;
                    _fontLookupDone = true;
                    Debug.Log($"[RTTMediaGridItem] Using fallback font from: {path}");
                    return defaultFont;
                }
            }

            // Option 3: Find any TMP font in scene
            var existingTMP = FindAnyObjectByType<TextMeshProUGUI>();
            if (existingTMP != null && existingTMP.font != null)
            {
                _cachedFallbackFont = existingTMP.font;
                _fontLookupDone = true;
                Debug.Log($"[RTTMediaGridItem] Using font from existing TMP: {existingTMP.font.name}");
                return existingTMP.font;
            }

            _fontLookupDone = true;
            Debug.LogError("[RTTMediaGridItem] No TMP font available! Text will not render. Please assign font in RTTManager.");
            return null;
        }
        #endregion
    }

}
