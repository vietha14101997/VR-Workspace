using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Detail View for RTT File Manager.
/// Displays information about the currently Hovered or Selected file/folder.
/// Fallback: Displays info about the current folder.
/// </summary>
public class RTTFileDetail : MonoBehaviour
{
    private Image _iconImage;
    private TextMeshProUGUI _nameText;
    private TextMeshProUGUI _infoText; // Type, Size, Date
    private GameObject _actionsContainer;

    // Configuration
    private Color _primaryColor = Color.white;
    private Color _accentColor = new Color(0f, 0.9f, 1f);
    private TMP_FontAsset _font;

    public void Initialize(Color primary, Color accent, TMP_FontAsset font = null)
    {
        _primaryColor = primary;
        _accentColor = accent;
        _font = font;
        BuildUI();
    }

    private void BuildUI()
    {
        // Vertical Layout
        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 30f; // Increased spacing
        layout.padding = new RectOffset(30, 30, 60, 30); // More padding top
        layout.childControlHeight = false;
        layout.childControlWidth = true;

        // 1. Large Icon
        GameObject iconObj = new GameObject("DetailIcon");
        iconObj.transform.SetParent(transform, false);
        _iconImage = iconObj.AddComponent<Image>();
        _iconImage.preserveAspect = true;
        
        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.sizeDelta = new Vector2(200f, 200f); // Larger preview

        // 2. File Name
        GameObject nameObj = new GameObject("DetailName");
        nameObj.transform.SetParent(transform, false);
        _nameText = nameObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) _nameText.font = _font;
        _nameText.fontSize = 32; // Larger font
        _nameText.fontWeight = FontWeight.Bold;
        _nameText.alignment = TextAlignmentOptions.Top;
        _nameText.color = _primaryColor;
        _nameText.enableWordWrapping = true;

        RectTransform nameRT = nameObj.GetComponent<RectTransform>();
        nameRT.sizeDelta = new Vector2(0, 100f);

        // 3. Metadata Info
        GameObject infoObj = new GameObject("DetailInfo");
        infoObj.transform.SetParent(transform, false);
        _infoText = infoObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) _infoText.font = _font;
        _infoText.fontSize = 24;
        _infoText.alignment = TextAlignmentOptions.Top;
        _infoText.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.7f);
        
        RectTransform infoRT = infoObj.GetComponent<RectTransform>();
        infoRT.sizeDelta = new Vector2(0, 100f);

        // 4. Actions (Mock)
        _actionsContainer = new GameObject("Actions");
        _actionsContainer.transform.SetParent(transform, false);
        var actionLayout = _actionsContainer.AddComponent<HorizontalLayoutGroup>();
        actionLayout.childAlignment = TextAnchor.MiddleCenter;
        actionLayout.spacing = 15f;
        
        RectTransform actionRT = _actionsContainer.GetComponent<RectTransform>();
        actionRT.sizeDelta = new Vector2(0, 60f);

        // Create Mock Buttons
        CreateActionButton("Open");
        CreateActionButton("Delete");
    }

    private void CreateActionButton(string label)
    {
        // Simple mock button
        var btn = VRButtonFactory.CreateTextButton(
            _actionsContainer.transform, 
            100f, 
            45f, 
            label, 
            _accentColor, 
            () => Debug.Log($"[RTTFileDetail] {label} clicked"), 
            20
        );
    }

    public void UpdateInfo(MockFile file, bool isCurrentFolder = false)
    {
        if (_nameText != null) _nameText.text = file.Name;
        
        // Update Icon (Mock logic)
        if (_iconImage != null)
        {
            // Set color based on type
            _iconImage.color = file.IsFolder ? new Color(1f, 0.8f, 0.2f) : _primaryColor;
            // Try to load icon, fallback to settings for files and home for folders
            _iconImage.sprite = RTTTaskbar.LoadIcon(file.IsFolder ? "folder" : "file")
                ?? RTTTaskbar.LoadIcon(file.IsFolder ? "home" : "settings");
        }

        // Expanded Info
        string typeStr = file.IsFolder ? "Folder" : "File";
        string sizeStr = file.IsFolder ? "-" : "2.5 MB"; // Mock
        
        if (isCurrentFolder)
        {
             _infoText.text = $"Current Location\n{file.Path}";
             if (_actionsContainer != null) _actionsContainer.SetActive(false); // No actions on current folder view usually?
        }
        else
        {
             _infoText.text = $"{typeStr}\n{sizeStr}\n{System.DateTime.Now:yyyy-MM-dd}";
             if (_actionsContainer != null) _actionsContainer.SetActive(true);
        }
    }
}
