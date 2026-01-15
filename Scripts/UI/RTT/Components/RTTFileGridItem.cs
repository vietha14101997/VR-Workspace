using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Represents a single file or folder item in the Grid View.
/// Handles visuals for Idle (Transparent), Hover (Semi-transparent), and Selected (Highlighted) states.
/// </summary>
public class RTTFileGridItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private Image _iconImage;
    private TextMeshProUGUI _nameText;
    private Image _bgImage;

    public string FilePath { get; private set; }
    public bool IsFolder { get; private set; }

    private System.Action<RTTFileGridItem> _onClickCallback;
    private System.Action<RTTFileGridItem> _onDoubleClickCallback;
    private System.Action<RTTFileGridItem, bool> _onHoverCallback;

    private bool _isSelected = false;
    private bool _isHovered = false;

    private float _lastClickTime = 0f;
    private const float DOUBLE_CLICK_THRESHOLD = 0.3f;

    public void Initialize(string name, bool isFolder, string path,
                           System.Action<RTTFileGridItem> onClick,
                           System.Action<RTTFileGridItem> onDoubleClick,
                           System.Action<RTTFileGridItem, bool> onHover)
    {
        FilePath = path;
        IsFolder = isFolder;
        _onClickCallback = onClick;
        _onDoubleClickCallback = onDoubleClick;
        _onHoverCallback = onHover;

        // Setup Visuals
        BuildUI(name, isFolder);
    }

    private void BuildUI(string name, bool isFolder)
    {
        // 1. Setup Layout
        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 30f; 
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = true; 
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 2. Icon
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(transform, false);
        _iconImage = iconObj.AddComponent<Image>();
        _iconImage.preserveAspect = true;
        _iconImage.raycastTarget = false; // Prevent blocking parent raycast

        // Load Icon
        if (isFolder)
            SetSprite("icon_folder");
        else
            SetSprite(IsImageFile(name) ? "icon_image" : "icon_file");

        var iconLE = iconObj.AddComponent<LayoutElement>();
        iconLE.preferredHeight = 150f;
        iconLE.preferredWidth = 150f;
        iconLE.flexibleHeight = 0;

        // 3. Name Text
        GameObject textObj = new GameObject("Name");
        textObj.transform.SetParent(transform, false);
        _nameText = textObj.AddComponent<TextMeshProUGUI>();
        _nameText.raycastTarget = false; 
        _nameText.text = name;
        _nameText.alignment = TextAlignmentOptions.Top;
        _nameText.fontSize = 32;
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.color = Color.white;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;
        _nameText.enableWordWrapping = true;
        
        var textLE = textObj.AddComponent<LayoutElement>();
        textLE.preferredHeight = 90f;
        textLE.flexibleHeight = 0;

        // 4. Background for interaction
        _bgImage = gameObject.AddComponent<Image>();
        _bgImage.color = Color.clear;
        _bgImage.raycastTarget = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        float currentTime = Time.time;
        float timeSinceLastClick = currentTime - _lastClickTime;

        if (timeSinceLastClick <= DOUBLE_CLICK_THRESHOLD)
        {
            // Double click - navigate into folder or open file
            _onDoubleClickCallback?.Invoke(this);
            _lastClickTime = 0f; // Reset to prevent triple-click
        }
        else
        {
            // Single click - select item
            _onClickCallback?.Invoke(this);
            _lastClickTime = currentTime;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovered = true;
        UpdateVisuals();
        _onHoverCallback?.Invoke(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;
        UpdateVisuals();
        _onHoverCallback?.Invoke(this, false);
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateVisuals();
    }

    private static readonly Color HighlightColor = new Color(0f, 0f, 0f, 0.27f);

    private void UpdateVisuals()
    {
        if (_bgImage == null) return;

        if (_isSelected || _isHovered)
        {
            // Hover & Selected share the same dark transparent background
            _bgImage.color = HighlightColor;
        }
        else
        {
            // Idle State: Transparent
            _bgImage.color = Color.clear;
        }
    }

    private void SetSprite(string resourceName)
    {
        Sprite sprite = Resources.Load<Sprite>(resourceName);
        if (sprite != null) 
        {
            _iconImage.sprite = sprite;
        }
        else if (resourceName == "icon_image")
        {
             sprite = Resources.Load<Sprite>("icon_file");
             if (sprite != null) _iconImage.sprite = sprite;
        }
    }

    private bool IsImageFile(string fileName)
    {
        string lower = fileName.ToLower();
        return lower.EndsWith(".jpg") || lower.EndsWith(".png") || lower.EndsWith(".jpeg") || lower.EndsWith(".bmp");
    }
}
