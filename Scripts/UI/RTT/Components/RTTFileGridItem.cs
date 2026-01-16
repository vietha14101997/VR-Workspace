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

    // Animation
    private const float HOVER_ANIMATION_DURATION = 0.15f;
    private float _currentColorAlpha = 0f;
    private float _targetColorAlpha = 0f;

    public void Initialize(System.Action<RTTFileGridItem> onClick,
                           System.Action<RTTFileGridItem> onDoubleClick,
                           System.Action<RTTFileGridItem, bool> onHover)
    {
        _onClickCallback = onClick;
        _onDoubleClickCallback = onDoubleClick;
        _onHoverCallback = onHover;

        // Setup Visuals (empty initially)
        BuildUI("", true);
    }

    /// <summary>
    /// Bind new data to this item (for virtualization/pooling).
    /// Reuses existing UI components without rebuilding.
    /// </summary>
    public void Bind(string name, bool isFolder, string path)
    {
        FilePath = path;
        IsFolder = isFolder;

        // Update text
        if (_nameText != null)
            _nameText.text = name;

        // Update icon
        if (_iconImage != null)
        {
            if (isFolder)
                SetSprite("icon_folder");
            else
                SetSprite(IsImageFile(name) ? "icon_image" : "icon_file");
        }

        // Reset selection state
        _isSelected = false;
        _isHovered = false;
        _lastClickTime = 0f;

        // Reset animation state immediately (no animation when rebinding)
        _targetColorAlpha = 0f;
        _currentColorAlpha = 0f;
        ApplyBackgroundColor();
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

        // 4. Background for interaction (rounded rectangle with 9-slice)
        _bgImage = gameObject.AddComponent<Image>();
        _bgImage.sprite = GetRoundedRectSprite();
        _bgImage.type = Image.Type.Sliced;
        _bgImage.color = Color.clear;
        _bgImage.raycastTarget = true;
    }

    // Cached rounded rectangle sprite for background rendering
    private static Sprite _cachedRoundedSprite;
    private const int ROUNDED_RECT_SIZE = 64;
    private const int CORNER_RADIUS = 16;

    private static Sprite GetRoundedRectSprite()
    {
        if (_cachedRoundedSprite != null) return _cachedRoundedSprite;

        int size = ROUNDED_RECT_SIZE;
        int radius = CORNER_RADIUS;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;
        float innerRadius = radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Distance from center
                float dx = Mathf.Abs(x - halfSize + 0.5f);
                float dy = Mathf.Abs(y - halfSize + 0.5f);

                // Inner rect (without corners)
                float innerHalfX = halfSize - innerRadius;
                float innerHalfY = halfSize - innerRadius;

                // Calculate signed distance to rounded rect
                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - innerRadius;

                // Anti-aliasing (smooth edge over 1.5 pixels)
                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // Create sliced sprite (9-slice) so corners don't stretch
        int border = radius + 2;
        _cachedRoundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            Vector2.one * 0.5f,
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border) // left, bottom, right, top borders
        );

        return _cachedRoundedSprite;
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

    private void Update()
    {
        // Animate color alpha
        if (!Mathf.Approximately(_currentColorAlpha, _targetColorAlpha))
        {
            float speed = 1f / HOVER_ANIMATION_DURATION;
            _currentColorAlpha = Mathf.MoveTowards(_currentColorAlpha, _targetColorAlpha, Time.unscaledDeltaTime * speed);
            ApplyBackgroundColor();
        }
    }

    private void UpdateVisuals()
    {
        if (_bgImage == null) return;

        if (_isSelected || _isHovered)
        {
            // Hover & Selected share the same dark transparent background
            _targetColorAlpha = HighlightColor.a;
        }
        else
        {
            // Idle State: Transparent
            _targetColorAlpha = 0f;
        }
    }

    private void ApplyBackgroundColor()
    {
        if (_bgImage == null) return;
        _bgImage.color = new Color(HighlightColor.r, HighlightColor.g, HighlightColor.b, _currentColorAlpha);
    }

    /// <summary>
    /// Set visual state immediately without animation (for pooling reset)
    /// </summary>
    public void SetVisualStateImmediate(bool highlighted)
    {
        _targetColorAlpha = highlighted ? HighlightColor.a : 0f;
        _currentColorAlpha = _targetColorAlpha;
        ApplyBackgroundColor();
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
