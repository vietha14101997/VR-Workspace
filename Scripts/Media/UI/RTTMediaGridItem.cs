using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Grid item component for displaying a video in the media library.
/// Shows thumbnail, projection badge, duration, and title.
/// </summary>
public class RTTMediaGridItem : MonoBehaviour
{
    #region Constants
    public const float ITEM_WIDTH = 395f;
    public const float ITEM_HEIGHT = 320f;
    public const float THUMBNAIL_HEIGHT = 222f;  // 16:9 aspect
    public const float TITLE_HEIGHT = 98f;
    public const float BADGE_SIZE = 50f;
    public const float DURATION_HEIGHT = 30f;
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
    private Image _thumbnailImage;
    private Image _projectionBadge;
    private TextMeshProUGUI _badgeText;
    private TextMeshProUGUI _durationText;
    private TextMeshProUGUI _titleText;
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

        // Main container
        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 0;

        // 1. Thumbnail Container
        CreateThumbnailSection();

        // 2. Title Section
        CreateTitleSection();

        // 3. Add interaction components
        CreateInteraction();
    }

    private void CreateThumbnailSection()
    {
        GameObject thumbContainer = new GameObject("ThumbnailContainer");
        thumbContainer.transform.SetParent(transform, false);

        var thumbRT = thumbContainer.AddComponent<RectTransform>();

        var thumbLE = thumbContainer.AddComponent<LayoutElement>();
        thumbLE.preferredHeight = THUMBNAIL_HEIGHT;

        // Background (dark)
        var thumbBg = thumbContainer.AddComponent<Image>();
        thumbBg.color = new Color(0.1f, 0.1f, 0.12f, 1f);

        // Thumbnail Image
        GameObject thumbObj = new GameObject("Thumbnail");
        thumbObj.transform.SetParent(thumbContainer.transform, false);

        var thumbImgRT = thumbObj.AddComponent<RectTransform>();
        thumbImgRT.anchorMin = Vector2.zero;
        thumbImgRT.anchorMax = Vector2.one;
        thumbImgRT.offsetMin = Vector2.zero;
        thumbImgRT.offsetMax = Vector2.zero;

        _thumbnailImage = thumbObj.AddComponent<Image>();
        _thumbnailImage.color = Color.white;
        _thumbnailImage.preserveAspect = true;

        // Projection Badge (bottom-left)
        CreateProjectionBadge(thumbContainer.transform);

        // Duration (bottom-right)
        CreateDurationLabel(thumbContainer.transform);

        // Favorite icon (top-right)
        CreateFavoriteIcon(thumbContainer.transform);

        // Selection border
        CreateSelectionBorder(thumbContainer.transform);
    }

    private void CreateProjectionBadge(Transform parent)
    {
        GameObject badgeObj = new GameObject("ProjectionBadge");
        badgeObj.transform.SetParent(parent, false);

        var badgeRT = badgeObj.AddComponent<RectTransform>();
        badgeRT.anchorMin = new Vector2(0, 0);
        badgeRT.anchorMax = new Vector2(0, 0);
        badgeRT.pivot = new Vector2(0, 0);
        badgeRT.anchoredPosition = new Vector2(10, 10);
        badgeRT.sizeDelta = new Vector2(BADGE_SIZE, 28);

        _projectionBadge = badgeObj.AddComponent<Image>();
        _projectionBadge.color = new Color(1f, 0.5f, 0f, 0.9f);  // Orange default

        // Round corners could be done with shader

        // Badge text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(badgeObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        _badgeText = textObj.AddComponent<TextMeshProUGUI>();
        _badgeText.text = "360";
        _badgeText.font = _font;
        _badgeText.fontSize = 18;
        _badgeText.fontStyle = FontStyles.Bold;
        _badgeText.color = Color.white;
        _badgeText.alignment = TextAlignmentOptions.Center;

        // Hide by default
        badgeObj.SetActive(false);
    }

    private void CreateDurationLabel(Transform parent)
    {
        GameObject durObj = new GameObject("Duration");
        durObj.transform.SetParent(parent, false);

        var durRT = durObj.AddComponent<RectTransform>();
        durRT.anchorMin = new Vector2(1, 0);
        durRT.anchorMax = new Vector2(1, 0);
        durRT.pivot = new Vector2(1, 0);
        durRT.anchoredPosition = new Vector2(-10, 10);
        durRT.sizeDelta = new Vector2(80, DURATION_HEIGHT);

        // Background
        var durBg = durObj.AddComponent<Image>();
        durBg.color = new Color(0, 0, 0, 0.7f);

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(durObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(5, 0);
        textRT.offsetMax = new Vector2(-5, 0);

        _durationText = textObj.AddComponent<TextMeshProUGUI>();
        _durationText.text = "0:00";
        _durationText.font = _font;
        _durationText.fontSize = 20;
        _durationText.color = Color.white;
        _durationText.alignment = TextAlignmentOptions.Center;
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
        favRT.sizeDelta = new Vector2(30, 30);

        _favoriteIcon = favObj.AddComponent<Image>();
        _favoriteIcon.color = new Color(1f, 0.8f, 0f);  // Gold

        // Load star icon
        var starSprite = Resources.Load<Sprite>("icon_star");
        if (starSprite != null)
        {
            _favoriteIcon.sprite = starSprite;
        }

        favObj.SetActive(false);  // Hidden by default
    }

    private void CreateSelectionBorder(Transform parent)
    {
        GameObject borderObj = new GameObject("SelectionBorder");
        borderObj.transform.SetParent(parent, false);

        var borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-3, -3);
        borderRT.offsetMax = new Vector2(3, 3);

        _selectionBorder = borderObj.AddComponent<Image>();
        _selectionBorder.color = _primaryColor;
        _selectionBorder.type = Image.Type.Sliced;

        // Use outline-style rendering
        var outline = borderObj.AddComponent<Outline>();
        outline.effectColor = _primaryColor;
        outline.effectDistance = new Vector2(3, 3);

        borderObj.SetActive(false);
    }

    private void CreateTitleSection()
    {
        GameObject titleContainer = new GameObject("TitleContainer");
        titleContainer.transform.SetParent(transform, false);

        var titleLE = titleContainer.AddComponent<LayoutElement>();
        titleLE.preferredHeight = TITLE_HEIGHT;

        var titleRT = titleContainer.AddComponent<RectTransform>();

        // Background (slightly lighter)
        var titleBg = titleContainer.AddComponent<Image>();
        titleBg.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);

        // Title Text
        GameObject textObj = new GameObject("Title");
        textObj.transform.SetParent(titleContainer.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(15, 10);
        textRT.offsetMax = new Vector2(-15, -10);

        _titleText = textObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Video Title";
        _titleText.font = _font;
        _titleText.fontSize = 26;
        _titleText.color = Color.white;
        _titleText.alignment = TextAlignmentOptions.TopLeft;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;
        _titleText.maxVisibleLines = 2;
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

        // Duration
        _durationText.text = video.FormattedDuration;

        // Projection badge
        string badge = video.ProjectionBadge;
        if (!string.IsNullOrEmpty(badge))
        {
            _badgeText.text = badge;
            _projectionBadge.color = ProjectionDetector.GetProjectionBadgeColor(video.Projection);
            _projectionBadge.transform.parent.gameObject.SetActive(true);
        }
        else
        {
            _projectionBadge.transform.parent.gameObject.SetActive(false);
        }

        // Favorite
        _favoriteIcon.transform.parent.gameObject.SetActive(video.IsFavorite);

        // Thumbnail - will be loaded async by FileThumbnailService
        _thumbnailImage.sprite = null;
        _thumbnailImage.color = new Color(0.2f, 0.2f, 0.22f);
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
        _selectionBorder.transform.parent.gameObject.SetActive(selected);
    }

    public void SetEditMode(bool editMode, bool isChecked = false)
    {
        // TODO: Show/hide checkbox for multi-select
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
