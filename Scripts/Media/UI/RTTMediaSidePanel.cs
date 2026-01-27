using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Side Panel View for Media Library Navigation.
/// Displays hierarchical navigation:
/// - All Media (base category, expandable)
///   - Videos (sub-item)
///   - Images (sub-item)
///   - Audio (sub-item)
/// - Recent
/// - Favorites
/// - Playlists
/// Based on RTTFileSidePanel pattern.
/// </summary>
public class RTTMediaSidePanel : MonoBehaviour
{
    #region Events
    public event Action<string> OnCategorySelected;  // "all", "videos", "images", "audio", "recent", "favorites", "playlists"
    #endregion

    #region Private Fields
    private RTTMediaLibraryController _controller;
    private float _width;
    private float _height;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private Transform _contentContainer;
    private List<NavigationItem> _navItems = new List<NavigationItem>();
    private NavigationItem _selectedItem;
    private Color SELECTION_COLOR;

    // Layout constants - synced with RTTFileSidePanel
    private const float HEADER_HEIGHT = 75f;
    private const float TOP_PADDING = 45f;
    private const float CONTENT_TOP_OFFSET = 142f;
    private const float MARKER_LEFT_OFFSET = 30f;
    private const float CONTENT_INDENT = 52f;
    private const float ITEM_HEIGHT = 60f;
    private const float MARKER_WIDTH = 11f;
    private const float MARKER_HEIGHT = 72f;
    private const float ICON_SIZE = 42f;
    private const float ITEM_SPACING = 10f;
    private const float ICON_TEXT_SPACING = 15f;

    // Hover effect constants
    private const float HOVER_SCALE = 1.03f;
    private const float HOVER_ANIMATION_SPEED = 12f;

    // Hierarchy constants
    private const float INDENT_STEP = 30f;  // Additional indent for sub-items
    #endregion

    #region Navigation Item Class
    private class NavigationItem
    {
        public string Id;
        public string Label;
        public int IndentLevel;
        public GameObject Root;
        public Image SelectionMarker;
        public TextMeshProUGUI LabelText;
        public Image Icon;
        public HoverEffectController HoverController;
        public Button Button;
    }
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
        SELECTION_COLOR = new Color(primary.r, primary.g, primary.b, 0.3f);

        BuildUI();
    }
    #endregion

    #region UI Building
    private void BuildUI()
    {
        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        // 1. Header Section
        CreateHeader();

        // 2. Content Container for Nav Items
        CreateContentContainer();

        // 3. Create Navigation Items with hierarchy
        CreateNavigationItems();

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

        // Title Text "Media Library" - centered
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(headerObj.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        var titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "Media Library";
        titleText.font = _font;
        titleText.fontSize = 40;
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
    }

    private void CreateContentContainer()
    {
        GameObject contentObj = new GameObject("NavItemsContainer");
        contentObj.transform.SetParent(transform, false);

        var contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 0);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.offsetMin = new Vector2(0, 0);
        contentRT.offsetMax = new Vector2(0, -CONTENT_TOP_OFFSET);

        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(0, 0, 0, 10);
        contentLayout.spacing = ITEM_SPACING;
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        _contentContainer = contentObj.transform;
    }

    private void CreateNavigationItems()
    {
        // All Media - base category (indent: 0)
        CreateNavItem("all", "All Media", "icon_media_folder", 0);

        // Sub-items under All Media (indent: 1)
        CreateNavItem("videos", "Videos", "icon_video_folder", 1);
        CreateNavItem("images", "Images", "icon_image_folder", 1);
        CreateNavItem("audio", "Audio", "icon_music_folder", 1);

        // Other categories (indent: 0)
        CreateNavItem("recent", "Recent", "icon_recent_folder", 0);
        CreateNavItem("favorites", "Favorites", "icon_star", 0);
        CreateNavItem("playlists", "Playlists", "icon_playlist", 0);
    }

    private void CreateNavItem(string id, string label, string iconName, int indentLevel = 0)
    {
        // Calculate indent offset
        float indentOffset = CONTENT_INDENT + (indentLevel * INDENT_STEP);

        // Item container
        GameObject itemObj = new GameObject($"Item_{id}");
        itemObj.transform.SetParent(_contentContainer, false);

        var itemRT = itemObj.AddComponent<RectTransform>();

        var itemLE = itemObj.AddComponent<LayoutElement>();
        itemLE.minHeight = ITEM_HEIGHT;
        itemLE.preferredHeight = ITEM_HEIGHT;

        // Invisible hit area on item for button click
        var hitImg = itemObj.AddComponent<Image>();
        hitImg.color = Color.clear;

        // Background child with margins (for hover effect)
        float bgMarginH = 15f;
        float bgMarginV = 2f;
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(itemObj.transform, false);
        var bgRT = bgObj.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = new Vector2(bgMarginH, bgMarginV);
        bgRT.offsetMax = new Vector2(-bgMarginH, -bgMarginV);

        var bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = GetRoundedSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = Color.clear;

        // Button Logic (use hitImg for wider click area)
        var btn = itemObj.AddComponent<Button>();
        btn.targetGraphic = hitImg;
        string capturedId = id;
        btn.onClick.AddListener(() => OnItemClicked(capturedId));

        // Add Hover Effect Controller with Scale + Color Effects
        var hoverController = itemObj.AddComponent<HoverEffectController>();

        // Scale effect
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(HOVER_SCALE)
            .WithTransitionDuration(1f / HOVER_ANIMATION_SPEED);
        hoverController.AddEffect(scaleEffect);

        // Background color effect on the Background child (with margins)
        var colorEffect = new ColorHoverEffect()
            .WithTargetChild("Background")
            .WithTargetType(ColorHoverEffect.TargetType.Image)
            .WithHoverColor(new Color(0f, 0f, 0f, 0.3f))
            .WithTransitionDuration(1f / HOVER_ANIMATION_SPEED);
        hoverController.AddEffect(colorEffect);

        // Selection Marker (positioned at left edge)
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
        iconRT.anchoredPosition = new Vector2(indentOffset, 0);
        iconRT.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);

        var iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = iconSprite;
        iconImg.preserveAspect = true;
        iconImg.color = new Color(1f, 1f, 1f, 0.85f);

        // Text Label
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(itemObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 0);
        textRT.anchorMax = new Vector2(1, 1);
        textRT.offsetMin = new Vector2(indentOffset + ICON_SIZE + ICON_TEXT_SPACING, 0);
        textRT.offsetMax = new Vector2(-CONTENT_INDENT, 0);

        var txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.font = _font;
        txt.fontSize = 32;
        txt.fontStyle = FontStyles.Bold;
        txt.color = new Color(1f, 1f, 1f, 0.85f);
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;

        // Store reference
        _navItems.Add(new NavigationItem
        {
            Id = id,
            Label = label,
            IndentLevel = indentLevel,
            Root = itemObj,
            SelectionMarker = markerImg,
            LabelText = txt,
            Icon = iconImg,
            HoverController = hoverController,
            Button = btn
        });
    }
    #endregion

    #region Selection
    private void OnItemClicked(string id)
    {
        SelectItem(id);
        OnCategorySelected?.Invoke(id);
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
            if (item.LabelText != null)
                item.LabelText.color = isSelected ? Color.white : new Color(1f, 1f, 1f, 0.7f);

            // Update icon color
            if (item.Icon != null)
                item.Icon.color = isSelected ? Color.white : new Color(1f, 1f, 1f, 0.7f);

            // Disable button and hover for selected item (no interaction needed)
            if (item.Button != null)
                item.Button.interactable = !isSelected;

            if (item.HoverController != null)
                item.HoverController.enabled = !isSelected;

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

    /// <summary>
    /// Get currently selected item label (for breadcrumbs)
    /// </summary>
    public string GetSelectedLabel()
    {
        return _selectedItem?.Label;
    }

    /// <summary>
    /// Clear all selections.
    /// </summary>
    public void ClearSelection()
    {
        foreach (var item in _navItems)
        {
            if (item.SelectionMarker != null)
                item.SelectionMarker.gameObject.SetActive(false);

            if (item.LabelText != null)
                item.LabelText.color = new Color(1f, 1f, 1f, 0.7f);

            if (item.Icon != null)
                item.Icon.color = new Color(1f, 1f, 1f, 0.7f);

            // Re-enable button and hover for all items
            if (item.Button != null)
                item.Button.interactable = true;

            if (item.HoverController != null)
                item.HoverController.enabled = true;
        }
        _selectedItem = null;
    }
    #endregion

    #region Public Methods
    public void UpdateVideoCount(int count)
    {
        // Could update header or selected item to show count
    }

    public void RefreshPlaylists(List<MediaPlaylist> playlists)
    {
        // TODO: Add/update playlist sub-items under "Playlists"
    }
    #endregion

    #region Capsule Sprite
    private static Sprite _cachedCapsuleSprite;

    /// <summary>
    /// Create a capsule/pill shaped sprite with rounded top and bottom edges
    /// </summary>
    private static Sprite GetCapsuleSprite()
    {
        if (_cachedCapsuleSprite != null) return _cachedCapsuleSprite;

        int width = 32;
        int height = 64;
        int radius = width / 2;

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

    #region Rounded Sprite
    private static Sprite _cachedRoundedSprite;
    private const int ROUNDED_SIZE = 64;
    private const int ROUNDED_CORNER = 16;

    /// <summary>
    /// Create a rounded rectangle sprite for hover background
    /// </summary>
    private static Sprite GetRoundedSprite()
    {
        if (_cachedRoundedSprite != null) return _cachedRoundedSprite;

        Texture2D tex = new Texture2D(ROUNDED_SIZE, ROUNDED_SIZE);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[ROUNDED_SIZE * ROUNDED_SIZE];
        float radius = ROUNDED_CORNER;

        for (int y = 0; y < ROUNDED_SIZE; y++)
        {
            for (int x = 0; x < ROUNDED_SIZE; x++)
            {
                float alpha = 1f;

                // Check each corner
                float dx = 0, dy = 0;

                if (x < radius && y < radius)
                {
                    // Bottom-left corner
                    dx = radius - x - 0.5f;
                    dy = radius - y - 0.5f;
                }
                else if (x >= ROUNDED_SIZE - radius && y < radius)
                {
                    // Bottom-right corner
                    dx = x - (ROUNDED_SIZE - radius) + 0.5f;
                    dy = radius - y - 0.5f;
                }
                else if (x < radius && y >= ROUNDED_SIZE - radius)
                {
                    // Top-left corner
                    dx = radius - x - 0.5f;
                    dy = y - (ROUNDED_SIZE - radius) + 0.5f;
                }
                else if (x >= ROUNDED_SIZE - radius && y >= ROUNDED_SIZE - radius)
                {
                    // Top-right corner
                    dx = x - (ROUNDED_SIZE - radius) + 0.5f;
                    dy = y - (ROUNDED_SIZE - radius) + 0.5f;
                }

                if (dx > 0 || dy > 0)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                }

                pixels[y * ROUNDED_SIZE + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        _cachedRoundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, ROUNDED_SIZE, ROUNDED_SIZE),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(ROUNDED_CORNER, ROUNDED_CORNER, ROUNDED_CORNER, ROUNDED_CORNER)
        );

        return _cachedRoundedSprite;
    }
    #endregion
}
