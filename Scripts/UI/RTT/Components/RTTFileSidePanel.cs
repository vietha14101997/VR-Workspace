using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Side Panel View for File Manager Navigation.
/// Displays buttons for: Internal, SD Card, Downloads, etc.
/// Includes Header with title and item count.
/// </summary>
public class RTTFileSidePanel : MonoBehaviour
{
    private RTTFileManagerController _controller;
    private float _width;
    private float _height;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private Transform _contentContainer;

    // Track selected item to update visuals
    private List<NavigationItem> _navItems = new List<NavigationItem>();
    private NavigationItem _selectedItem;

    // Selection marker color
    private Color SELECTION_COLOR;

    // Layout constants - synced with RTTFileDetail
    private const float HEADER_HEIGHT = 68f;
    private const float TOP_PADDING = 45f;
    private const float MARKER_LEFT_OFFSET = 30f;
    private const float CONTENT_INDENT = 52f;
    private const float ITEM_HEIGHT = 60f;
    private const float MARKER_WIDTH = 11f;
    private const float MARKER_HEIGHT = 72f;
    private const float ICON_SIZE = 42f;
    private const float ITEM_SPACING = 10f;
    private const float ICON_TEXT_SPACING = 15f;

    private class NavigationItem
    {
        public string Path;
        public string Id;
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
        SELECTION_COLOR = new Color(primary.r, primary.g, primary.b, 0.3f);

        BuildUI();
    }

    private void BuildUI()
    {
        // Main container with no layout - we position manually for precise control
        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        // 1. Header Section
        CreateHeader();

        // 2. Content Container for Nav Items
        GameObject contentObj = new GameObject("NavItemsContainer");
        contentObj.transform.SetParent(transform, false);
        var contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 0);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.offsetMin = new Vector2(0, 0);
        contentRT.offsetMax = new Vector2(0, -HEADER_HEIGHT - TOP_PADDING);

        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(0, 0, 10, 10);
        contentLayout.spacing = ITEM_SPACING;
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        _contentContainer = contentObj.transform;

        // 3. Create Navigation Items (original items)
        CreateNavItem("internal", "Internal Storage", "icon_internal");
        CreateNavItem("sdcard", "SD Card", "icon_sd_card");
        CreateNavItem("downloads", "Downloads", "icon_download_folder");
        CreateNavItem("videos", "Videos", "icon_video_folder");
        CreateNavItem("music", "Music", "icon_music_folder");
        CreateNavItem("recent", "Recent", "icon_recent_folder");

        // Select first item by default
        if (_navItems.Count > 0)
        {
            SelectItem(_navItems[0].Id);
        }
    }

    private void CreateHeader()
    {
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(transform, false);

        var headerRT = headerObj.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.anchoredPosition = new Vector2(0, -TOP_PADDING);
        headerRT.sizeDelta = new Vector2(0, HEADER_HEIGHT);

        // Title Text "File Manager" - centered
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(headerObj.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        var titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "File Manager";
        titleText.font = _font;
        titleText.fontSize = 40;  // Significantly increased
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
    }

    private void CreateNavItem(string id, string label, string iconName)
    {
        // Item container
        GameObject itemObj = new GameObject($"Item_{id}");
        itemObj.transform.SetParent(_contentContainer, false);

        var itemLE = itemObj.AddComponent<LayoutElement>();
        itemLE.minHeight = ITEM_HEIGHT;
        itemLE.preferredHeight = ITEM_HEIGHT;

        // Background for hit detection
        var bgImg = itemObj.AddComponent<Image>();
        bgImg.color = Color.clear;

        // Button Logic
        var btn = itemObj.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        string capturedId = id;
        btn.onClick.AddListener(() => OnItemClicked(capturedId));

        // Selection Marker (positioned at old icon location, 75% height, centered)
        GameObject markerObj = new GameObject("Marker");
        markerObj.transform.SetParent(itemObj.transform, false);
        var markerRT = markerObj.AddComponent<RectTransform>();
        markerRT.anchorMin = new Vector2(0, 0.5f);
        markerRT.anchorMax = new Vector2(0, 0.5f);
        markerRT.pivot = new Vector2(0, 0.5f);
        markerRT.anchoredPosition = new Vector2(MARKER_LEFT_OFFSET, 0);
        markerRT.sizeDelta = new Vector2(MARKER_WIDTH, MARKER_HEIGHT);

        var markerImg = markerObj.AddComponent<Image>();
        markerImg.sprite = GetCapsuleSprite();
        markerImg.type = Image.Type.Sliced;
        markerImg.color = SELECTION_COLOR;
        markerImg.gameObject.SetActive(false);

        // Icon
        Sprite iconSprite = Resources.Load<Sprite>(iconName);
        if (iconSprite == null) iconSprite = Resources.Load<Sprite>("icon_folder");

        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(itemObj.transform, false);
        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0, 0.5f);
        iconRT.anchorMax = new Vector2(0, 0.5f);
        iconRT.pivot = new Vector2(0, 0.5f);
        iconRT.anchoredPosition = new Vector2(CONTENT_INDENT, 0);
        iconRT.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);

        var iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = iconSprite;
        iconImg.preserveAspect = true;
        iconImg.color = new Color(1f, 1f, 1f, 0.7f);

        // Text Label
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(itemObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 0);
        textRT.anchorMax = new Vector2(1, 1);
        textRT.offsetMin = new Vector2(CONTENT_INDENT + ICON_SIZE + ICON_TEXT_SPACING, 0);
        textRT.offsetMax = new Vector2(-CONTENT_INDENT, 0);

        var txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.font = _font;
        txt.fontSize = 32;  // 50% larger
        txt.color = new Color(1f, 1f, 1f, 0.7f);
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;

        // Store reference
        _navItems.Add(new NavigationItem
        {
            Path = label,
            Id = id,
            Root = itemObj,
            SelectionMarker = markerImg,
            Label = txt,
            Icon = iconImg
        });
    }

    private void OnItemClicked(string id)
    {
        SelectItem(id);
        // Notify controller
        _controller?.OnSidePanelItemSelected(id);
    }

    public void SelectItem(string id)
    {
        foreach (var item in _navItems)
        {
            bool isSelected = item.Id == id;

            // Toggle Marker visibility
            if (item.SelectionMarker != null)
                item.SelectionMarker.gameObject.SetActive(isSelected);

            // Update text color
            if (item.Label != null)
                item.Label.color = isSelected ? Color.white : new Color(1f, 1f, 1f, 0.7f);

            // Update icon color
            if (item.Icon != null)
                item.Icon.color = isSelected ? Color.white : new Color(1f, 1f, 1f, 0.7f);

            if (isSelected) _selectedItem = item;
        }
    }

    /// <summary>
    /// Get currently selected item ID
    /// </summary>
    public string GetSelectedId()
    {
        return _selectedItem?.Id;
    }

    #region Rounded Marker Sprite
    private static Sprite _cachedCapsuleSprite;

    /// <summary>
    /// Create a capsule/pill shaped sprite with rounded top and bottom edges
    /// </summary>
    private static Sprite GetCapsuleSprite()
    {
        if (_cachedCapsuleSprite != null) return _cachedCapsuleSprite;

        int width = 32;
        int height = 64;
        int radius = width / 2; // Full radius for capsule ends

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float alpha = 1f;
                float centerX = width / 2f;

                // Top capsule cap
                if (y >= height - radius)
                {
                    float dy = y - (height - radius);
                    float dx = Mathf.Abs(x - centerX + 0.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                }
                // Bottom capsule cap
                else if (y < radius)
                {
                    float dy = radius - y - 1;
                    float dx = Mathf.Abs(x - centerX + 0.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                }
                // Middle section (rectangle)
                else
                {
                    float dx = Mathf.Abs(x - centerX + 0.5f);
                    float dist = dx - radius;
                    alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                }

                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // Create 9-slice sprite with borders for proper stretching
        int border = radius;
        _cachedCapsuleSprite = Sprite.Create(
            tex,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)
        );

        return _cachedCapsuleSprite;
    }
    #endregion
}
