using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Side Panel View for File Manager Navigation.
/// Displays buttons for: Internal, SD Card, Downloads, etc.
/// Includes Header with Back button and App Title.
/// </summary>
public class RTTFileSidePanel : MonoBehaviour
{
    private RTTFileManagerController _controller;
    private float _width;
    private float _height;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;
    
    private VerticalLayoutGroup _mainLayout;
    private Transform _contentContainer;
    
    // Track selected item to update visuals
    private List<NavigationItem> _navItems = new List<NavigationItem>();
    private NavigationItem _selectedItem;

    private class NavigationItem
    {
        public string Path;
        public GameObject Root;
        public Image SelectionMarker;
        public TextMeshProUGUI Label;
        public Image Icon;
    }

    public void Initialize(RTTFileManagerController controller, float w, float h, TMP_FontAsset font, Color primary, Color accent)
    {
        _controller = controller;
        _width = w;
        _height = h;
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;
        
        BuildUI();
    }

    private void BuildUI()
    {
        // 1. Main Vertical Layout
        _mainLayout = gameObject.AddComponent<VerticalLayoutGroup>();
        _mainLayout.padding = new RectOffset(20, 20, 30, 30);
        _mainLayout.spacing = 20f;
        _mainLayout.childAlignment = TextAnchor.UpperCenter;
        _mainLayout.childControlWidth = true;
        _mainLayout.childControlHeight = false;
        _mainLayout.childForceExpandWidth = true;
        _mainLayout.childForceExpandHeight = false;

        // 2. Header Block (Back Button + App Title)
        CreateHeader();

        // 3. Separator / Spacer
        CreateSpacer(20f);

        // 4. Content Container for Nav Items
        GameObject contentObj = new GameObject("NavItemsContainer");
        contentObj.transform.SetParent(transform, false);
        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 10f; // Spacing between items
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        _contentContainer = contentObj.transform;

        // 5. Create Navigation Items
        CreateNavItem("Internal Storage", "icon_internal");
        CreateNavItem("SD Card", "icon_sd_card");
        CreateNavItem("Downloads", "icon_download_folder");
        CreateNavItem("Videos", "icon_video_folder");
        CreateNavItem("Music", "icon_music_folder");
        CreateNavItem("Recent", "icon_recent_folder");
        
        // Select first item by default
        if (_navItems.Count > 0)
        {
            SelectPath(_navItems[0].Path);
        }
    }

    private void CreateHeader()
    {
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(transform, false);
        
        // Horizontal Layout for Header
        var hLayout = headerObj.AddComponent<HorizontalLayoutGroup>();
        hLayout.childAlignment = TextAnchor.MiddleLeft;
        hLayout.spacing = 15f;
        hLayout.childControlWidth = false;
        hLayout.childControlHeight = false;
        hLayout.childForceExpandWidth = false;
        hLayout.childForceExpandHeight = false;

        // Back Button (Icon Only or Small Text)
        // Using CreateBareIconButton for minimal look, or specialized Back button
        // User requested "Back Button... next is app name"
        var backBtn = VRButtonFactory.CreateButton(headerObj.transform, new VRButtonFactory.ButtonConfig
        {
            label = "BACK",
            themeColor = _primaryColor,
            width = 80f,
            height = 50f,
            fontSize = 24,
            font = _font,
            textOnly = true, // Or use icon if available
            backgroundAlpha = 0.2f, // Subtle background
            cornerRadius = 0.1f
        }, () => _controller?.HandleBack());

        // App Title
        GameObject titleObj = new GameObject("AppTitle");
        titleObj.transform.SetParent(headerObj.transform, false);
        var titleTxt = titleObj.AddComponent<TextMeshProUGUI>();
        titleTxt.text = "File Manager";
        titleTxt.font = _font;
        titleTxt.fontSize = 32;
        titleTxt.color = Color.white;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.alignment = TextAlignmentOptions.Left;
        
        // Auto-size title container
        var csf = titleObj.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void CreateSpacer(float height)
    {
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(transform, false);
        var le = spacer.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }

    private void CreateNavItem(string label, string iconName)
    {
        // Wrapper for the item
        GameObject itemObj = new GameObject($"Item_{label}");
        itemObj.transform.SetParent(_contentContainer, false);
        
        // Layout: Horizontal (Marker | Icon | Text)
        var layout = itemObj.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 15f;
        layout.padding = new RectOffset(5, 5, 5, 5);
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        
        // Background for hit detection (transparent)
        var img = itemObj.AddComponent<Image>();
        img.color = Color.clear;
        
        // Button Logic
        var btn = itemObj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => OnItemClicked(label));
        
        // Selection Marker (Left Bar)
        GameObject markerObj = new GameObject("Marker");
        markerObj.transform.SetParent(itemObj.transform, false);
        var markerLayout = markerObj.AddComponent<LayoutElement>();
        markerLayout.minWidth = 4f;
        markerLayout.preferredWidth = 4f;
        markerLayout.minHeight = 40f;
        markerLayout.preferredHeight = 40f;
        
        var markerImg = markerObj.AddComponent<Image>();
        markerImg.color = _accentColor; // Active color
        markerImg.gameObject.SetActive(false); // Hidden by default

        // Icon
        Sprite iconSprite = Resources.Load<Sprite>(iconName);
        if (iconSprite == null) iconSprite = Resources.Load<Sprite>("icon_folder");
        
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(itemObj.transform, false);
        var iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = iconSprite;
        iconImg.preserveAspect = true;
        iconImg.color = new Color(1f, 1f, 1f, 0.5f); // Dimmed default
        
        var iconLayout = iconObj.AddComponent<LayoutElement>();
        iconLayout.minWidth = 30f;
        iconLayout.preferredWidth = 30f;
        iconLayout.minHeight = 30f;
        iconLayout.preferredHeight = 30f;

        // Text
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(itemObj.transform, false);
        var txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.font = _font;
        txt.fontSize = 28;
        txt.color = new Color(1f, 1f, 1f, 0.5f); // Dimmed default
        txt.alignment = TextAlignmentOptions.Left;
        
        var textLayout = textObj.AddComponent<LayoutElement>();
        textLayout.flexibleWidth = 1f; // Take remaining space
        
        // Store Ref
        _navItems.Add(new NavigationItem
        {
            Path = label, // Simplify path as label for now
            Root = itemObj,
            SelectionMarker = markerImg,
            Label = txt,
            Icon = iconImg
        });
    }

    private void OnItemClicked(string path)
    {
        SelectPath(path);
        // TODO: Call Controller
    }

    private void SelectPath(string path)
    {
        foreach (var item in _navItems)
        {
            bool isSelected = item.Path == path;
            
            // Toggle Marker
            if (item.SelectionMarker != null)
                item.SelectionMarker.gameObject.SetActive(isSelected);
            
            // Highlight Text & Icon
            if (item.Label != null)
                item.Label.color = isSelected ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                
            if (item.Icon != null)
                item.Icon.color = isSelected ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                
            if (isSelected) _selectedItem = item;
        }
    }
}
