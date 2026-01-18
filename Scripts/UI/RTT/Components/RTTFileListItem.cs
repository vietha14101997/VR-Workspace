using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Row item for File Manager List View.
/// Displays file info in columns: Icon+Name, Type, Created, Modified, Duration, Size
/// Handles hover/click interactions and visual highlighting.
/// </summary>
public class RTTFileListItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    #region Hover Effect
    private const float HOVER_SCALE = 1.02f;
    private const float HOVER_ANIMATION_SPEED = 8f;
    private HoverEffectController _hoverController;
    #endregion

    #region Data
    public string FilePath { get; private set; }
    public bool IsFolder { get; private set; }
    #endregion

    #region References
    private Image _background;
    private Image _iconImage;
    private TextMeshProUGUI _nameText;
    private TextMeshProUGUI _typeText;
    private TextMeshProUGUI _createdText;
    private TextMeshProUGUI _modifiedText;
    private TextMeshProUGUI _durationText;
    private TextMeshProUGUI _sizeText;
    #endregion

    #region State
    private TMP_FontAsset _font;
    private string _currentFilePath;  // Track current file for thumbnail cancellation
    private MockFile _currentFile;    // Current bound file
    #endregion

    #region Callbacks
    private Action<string> _onHoverEnter;
    private Action<string> _onHoverExit;
    private Action<string, bool> _onClick; // path, isFolder
    #endregion

    #region Colors
    private static readonly Color NormalColor = Color.clear;
    private static readonly Color HoverColor = new Color(0f, 0f, 0f, 0.3f);
    #endregion

    #region Column Widths (percentages of total width)
    // Name: 35%, Type: 10%, Created: 15%, Modified: 15%, Duration: 15%, Size: 10%
    public static readonly float[] ColumnWidths = { 0.35f, 0.10f, 0.15f, 0.15f, 0.15f, 0.10f };
    public const float IconWidth = 90f;
    public const float RowPadding = 20f;
    private const int THUMBNAIL_SIZE = 256;  // List thumbnail generation size (high quality)
    #endregion

    public void Initialize(TMP_FontAsset font = null)
    {
        _font = font;
        BuildUI();
    }

    /// <summary>
    /// Set callbacks for interaction events
    /// </summary>
    public void SetCallbacks(Action<string> onHoverEnter, Action<string> onHoverExit, Action<string, bool> onClick)
    {
        _onHoverEnter = onHoverEnter;
        _onHoverExit = onHoverExit;
        _onClick = onClick;
    }

    private void BuildUI()
    {
        RectTransform rt = GetComponent<RectTransform>();
        float totalWidth = rt.sizeDelta.x;
        float height = rt.sizeDelta.y;

        // Background (for highlighting)
        _background = gameObject.AddComponent<Image>();
        _background.color = NormalColor;
        _background.raycastTarget = true;

        // Add Hover Effect Controller with Scale Effect
        _hoverController = gameObject.AddComponent<HoverEffectController>();
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(HOVER_SCALE)
            .WithTransitionDuration(1f / HOVER_ANIMATION_SPEED);
        _hoverController.AddEffect(scaleEffect);

        // Calculate column positions
        float usableWidth = totalWidth - RowPadding * 2;
        float currentX = RowPadding;

        // Column 1: Icon + Name
        float nameColWidth = usableWidth * ColumnWidths[0];
        CreateIconAndName(currentX, nameColWidth, height);
        currentX += nameColWidth;

        // Column 2: Type
        float typeColWidth = usableWidth * ColumnWidths[1];
        _typeText = CreateColumnText("Type", currentX, typeColWidth, height);
        currentX += typeColWidth;

        // Column 3: Created
        float createdColWidth = usableWidth * ColumnWidths[2];
        _createdText = CreateColumnText("Created", currentX, createdColWidth, height);
        currentX += createdColWidth;

        // Column 4: Modified
        float modifiedColWidth = usableWidth * ColumnWidths[3];
        _modifiedText = CreateColumnText("Modified", currentX, modifiedColWidth, height);
        currentX += modifiedColWidth;

        // Column 5: Duration
        float durationColWidth = usableWidth * ColumnWidths[4];
        _durationText = CreateColumnText("Duration", currentX, durationColWidth, height);
        currentX += durationColWidth;

        // Column 6: Size
        float sizeColWidth = usableWidth * ColumnWidths[5];
        _sizeText = CreateColumnText("Size", currentX, sizeColWidth, height);

        // NOTE: No BoxCollider needed - RTT uses GraphicRaycaster via panel's DisplayQuad collider
        // Adding BoxColliders to individual items causes raycast issues when items are in buffer zone
        // (outside visible RectMask2D area but still active for smooth scrolling)
    }

    private void CreateIconAndName(float startX, float colWidth, float height)
    {
        // Icon
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(transform, false);

        RectTransform iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0, 0.5f);
        iconRT.anchorMax = new Vector2(0, 0.5f);
        iconRT.pivot = new Vector2(0, 0.5f);
        iconRT.sizeDelta = new Vector2(IconWidth, IconWidth);
        iconRT.anchoredPosition = new Vector2(startX, 0);

        _iconImage = iconObj.AddComponent<Image>();
        _iconImage.raycastTarget = false;

        // Name text (after icon)
        float nameStartX = startX + IconWidth + 10f;
        float nameWidth = colWidth - IconWidth - 10f;

        GameObject nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(transform, false);

        RectTransform nameRT = nameObj.AddComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0);
        nameRT.anchorMax = new Vector2(0, 1);
        nameRT.pivot = new Vector2(0, 0.5f);
        nameRT.sizeDelta = new Vector2(nameWidth, 0);
        nameRT.anchoredPosition = new Vector2(nameStartX, 0);

        _nameText = nameObj.AddComponent<TextMeshProUGUI>();
        _nameText.font = _font;
        _nameText.fontSize = 32;
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.color = Color.white;
        _nameText.alignment = TextAlignmentOptions.MidlineLeft;
        _nameText.enableWordWrapping = false;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;
        _nameText.raycastTarget = false;
    }

    private TextMeshProUGUI CreateColumnText(string name, float startX, float colWidth, float height)
    {
        GameObject textObj = new GameObject(name + "Text");
        textObj.transform.SetParent(transform, false);

        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 0);
        textRT.anchorMax = new Vector2(0, 1);
        textRT.pivot = new Vector2(0, 0.5f);
        textRT.sizeDelta = new Vector2(colWidth, 0);
        textRT.anchoredPosition = new Vector2(startX, 0);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.fontSize = 26;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = true;
        text.raycastTarget = false;

        return text;
    }

    public void Bind(MockFile file)
    {
        // Cancel any pending thumbnail request for previous file
        if (!string.IsNullOrEmpty(_currentFilePath) && _currentFilePath != file.Path)
        {
            FileThumbnailService.Instance?.CancelRequest(_currentFilePath);
        }

        FilePath = file.Path;
        IsFolder = file.IsFolder;
        _currentFilePath = file.Path;
        _currentFile = file;

        // Name
        _nameText.text = file.Name;

        // Icon
        if (file.IsFolder)
        {
            string iconName = file.IsFolderEmpty ? "icon_folder_empty" : "icon_folder_not_empty";
            LoadIcon(iconName, "icon_folder");
        }
        else
        {
            var category = FileCategoryHelper.GetCategory(file.Type);

            // Request thumbnail for Image/Video categories
            if (FileCategoryHelper.RequiresThumbnailGeneration(category))
            {
                // Use loading placeholder while thumbnail is being generated asynchronously
                string loadingIcon = FileCategoryHelper.GetLoadingPlaceholderIcon(category);
                LoadIcon(loadingIcon, "icon_media_file");
            }
            else
            {
                // Use default icon for non-thumbnail categories
                string defaultIcon = FileCategoryHelper.GetDefaultIconName(category);
                LoadIcon(defaultIcon, "icon_file_unknown");
            }

            // Request async thumbnail generation for Image/Video
            if (FileCategoryHelper.RequiresThumbnailGeneration(category))
            {
                FileThumbnailService.Instance?.RequestThumbnail(
                    file,
                    THUMBNAIL_SIZE,
                    onSuccess: (sprite) => {
                        // Verify still same file before updating
                        if (_currentFilePath == file.Path && sprite != null)
                        {
                            _iconImage.sprite = sprite;
                        }
                    },
                    onFailed: null,  // Keep placeholder icon
                    priority: 0
                );
            }
        }

        // Type
        _typeText.text = "<b>" + (file.IsFolder ? "Folder" : file.Type.ToUpper()) + "</b>";

        // Created
        _createdText.text = FormatDateTime(file.Created);

        // Modified
        _modifiedText.text = FormatDateTime(file.Modified);

        // Duration
        if (file.Duration.TotalSeconds > 0)
        {
            _durationText.text = "<b>" + FormatDuration(file.Duration) + "</b>";
        }
        else
        {
            _durationText.text = "<b>-</b>";
        }

        // Size
        if (file.IsFolder)
        {
            _sizeText.text = "<b>-</b>";
        }
        else
        {
            _sizeText.text = "<b>" + FormatFileSize(file.Size) + "</b>";
        }

        // Reset background
        _background.color = NormalColor;
    }

    #region Pointer Events
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_background != null)
            _background.color = HoverColor;

        // Note: HoverEffectController handles scale effect automatically via IPointerEnterHandler

        _onHoverEnter?.Invoke(FilePath);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_background != null)
            _background.color = NormalColor;

        // Note: HoverEffectController handles scale effect automatically via IPointerExitHandler

        _onHoverExit?.Invoke(FilePath);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        _onClick?.Invoke(FilePath, IsFolder);
    }
    #endregion

    #region Public API for parent to control visuals
    public void SetBackgroundColor(Color color)
    {
        if (_background != null)
        {
            _background.color = color;
        }
    }

    /// <summary>
    /// Force clear hover state (used when item is recycled)
    /// </summary>
    public void ClearHoverState()
    {
        if (_background != null)
            _background.color = NormalColor;

        // Reset scale hover effect immediately (snap to default state)
        _hoverController?.ResetHoverState(immediate: true);
    }

    /// <summary>
    /// Called when item is recycled in the pool.
    /// Cancels any pending thumbnail requests.
    /// </summary>
    public void OnRecycle()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            FileThumbnailService.Instance?.CancelRequest(_currentFilePath);
        }
        ClearHoverState();
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Load and set an icon sprite from Resources.
    /// </summary>
    private void LoadIcon(string iconName, string fallbackIcon = "icon_file_unknown")
    {
        Sprite icon = Resources.Load<Sprite>(iconName);
        if (icon != null)
        {
            _iconImage.sprite = icon;
            _iconImage.color = Color.white;
        }
        else
        {
            // Try fallback
            icon = Resources.Load<Sprite>(fallbackIcon);
            if (icon != null)
            {
                _iconImage.sprite = icon;
                _iconImage.color = Color.white;
            }
        }
    }

    /// <summary>
    /// Get icon name for a file type using FileCategoryHelper.
    /// </summary>
    private string GetFileIcon(string type)
    {
        var category = FileCategoryHelper.GetCategory(type);
        return FileCategoryHelper.GetDefaultIconName(category);
    }

    private string FormatDateTime(DateTime dt)
    {
        return "<b>" + dt.ToString("MMM d, yyyy") + "</b>\n" + dt.ToString("hh:mm tt");
    }

    private string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }
        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    private string FormatFileSize(long bytes)
    {
        return FileSystemService.FormatFileSize(bytes);
    }
    #endregion
}
