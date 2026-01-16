using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

/// <summary>
/// Row item for File Manager List View.
/// Displays file info in columns: Icon+Name, Type, Created, Modified, Duration, Size
/// Handles hover/click interactions and visual highlighting.
/// </summary>
public class RTTFileListItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
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
    private bool _isHovered = false;
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
        _nameText.fontSize = 26;
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
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = true;
        text.raycastTarget = false;

        return text;
    }

    public void Bind(MockFile file)
    {
        FilePath = file.Path;
        IsFolder = file.IsFolder;

        // Name
        _nameText.text = file.Name;

        // Icon
        string iconName;
        if (file.IsFolder)
        {
            iconName = file.IsFolderEmpty ? "icon_folder_empty" : "icon_folder_not_empty";
        }
        else
        {
            iconName = GetFileIcon(file.Type);
        }

        Sprite icon = Resources.Load<Sprite>(iconName);
        if (icon != null)
        {
            _iconImage.sprite = icon;
            _iconImage.color = Color.white;
        }
        else
        {
            _iconImage.sprite = Resources.Load<Sprite>(file.IsFolder ? "icon_folder" : "icon_file");
            _iconImage.color = Color.white;
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

        // Reset background and hover state
        _isHovered = false;
        _background.color = NormalColor;
    }

    #region Pointer Events
    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovered = true;
        if (_background != null)
            _background.color = HoverColor;

        _onHoverEnter?.Invoke(FilePath);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;
        if (_background != null)
            _background.color = NormalColor;

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
        _isHovered = false;
        if (_background != null)
            _background.color = NormalColor;
    }
    #endregion

    #region Helper Methods
    private string GetFileIcon(string type)
    {
        switch (type.ToLower())
        {
            case "jpg":
            case "jpeg":
            case "png":
            case "gif":
            case "bmp":
            case "webp":
                return "icon_image";
            case "mp4":
            case "avi":
            case "mkv":
            case "mov":
            case "wmv":
                return "icon_video";
            case "mp3":
            case "wav":
            case "flac":
            case "aac":
            case "ogg":
                return "icon_audio";
            case "pdf":
                return "icon_pdf";
            case "doc":
            case "docx":
                return "icon_doc";
            case "txt":
            case "log":
                return "icon_text";
            case "zip":
            case "rar":
            case "7z":
            case "tar":
            case "gz":
                return "icon_archive";
            default:
                return "icon_file";
        }
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
