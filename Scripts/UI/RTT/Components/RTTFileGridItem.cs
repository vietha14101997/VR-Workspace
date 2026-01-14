using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Represents a single file or folder item in the Grid View.
/// </summary>
public class RTTFileGridItem : MonoBehaviour
{
    private Image _iconImage;
    private TextMeshProUGUI _nameText;
    private Button _button;
    
    public string FilePath { get; private set; }
    public bool IsFolder { get; private set; }
    
    private System.Action<RTTFileGridItem> _onClickCallback;
    private System.Action<RTTFileGridItem, bool> _onHoverCallback;

    public void Initialize(string name, bool isFolder, string path, 
                           System.Action<RTTFileGridItem> onClick, 
                           System.Action<RTTFileGridItem, bool> onHover)
    {
        FilePath = path;
        IsFolder = isFolder;
        _onClickCallback = onClick;
        _onHoverCallback = onHover;
        
        // Setup Visuals
        BuildUI(name, isFolder);
    }

    private void BuildUI(string name, bool isFolder)
    {
        // 1. Setup Layout
        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 30f; // Increased spacing (3x)
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = true; // FORCE layout group to control children height (applying LayoutElements)
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 2. Icon
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(transform, false);
        _iconImage = iconObj.AddComponent<Image>();
        _iconImage.preserveAspect = true;
        
        // Load Icon based on type
        if (isFolder)
        {
            SetSprite("icon_folder");
        }
        else
        {
            if (IsImageFile(name))
            {
               // Try to load preview, fallback to generic file
               // For mock purposes, if we can't load real path, we stick to file icon or special media icon
               // In a real app, this would be async.
               SetSprite("icon_image"); // Use a specific image icon if available, or preview
               // TODO: Actual file loading logic if paths were real
            }
            else
            {
                SetSprite("icon_file");
            }
        }

        // Icon Layout Constraint
        var iconLE = iconObj.AddComponent<LayoutElement>();
        iconLE.preferredHeight = 150f; // Increased 1.5x (was 100)
        iconLE.preferredWidth = 150f;
        iconLE.flexibleHeight = 0;

        // 3. Name Text
        GameObject textObj = new GameObject("Name");
        textObj.transform.SetParent(transform, false);
        _nameText = textObj.AddComponent<TextMeshProUGUI>();
        _nameText.text = name;
        _nameText.alignment = TextAlignmentOptions.Top;
        _nameText.fontSize = 32; // Increased to 32
        _nameText.fontStyle = FontStyles.Bold; // Bold
        _nameText.color = Color.white;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;
        _nameText.enableWordWrapping = true;
        
        // Text Layout Constraint
        var textLE = textObj.AddComponent<LayoutElement>();
        textLE.preferredHeight = 90f; // Increased for 32px font (approx 2.5 lines)
        textLE.flexibleHeight = 0;

        // 4. Button Interaction
        _button = gameObject.AddComponent<Button>();
        _button.onClick.AddListener(OnClick);
        
        // Transparent background for hit area
        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.05f); // Very faint background
        _button.targetGraphic = bg;
        
        // 5. Hover Effect (Optional, using VRButtonAnimation if available or simple scale)
        // For now simple scale script or similar is good, but let's stick to basic functionality first.
    }

    private void OnClick()
    {
        _onClickCallback?.Invoke(this);
    }

    public void SetSelected(bool selected)
    {
        // Highlight logic
        var bg = GetComponent<Image>();
        if (bg != null)
        {
            bg.color = selected ? new Color(1f, 1f, 1f, 0.2f) : new Color(1f, 1f, 1f, 0.05f);
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
             // Fallback if specific image icon missing, use file
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
