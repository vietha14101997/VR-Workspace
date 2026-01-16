using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

/// <summary>
/// Represents a single file or folder item in the Grid View.
/// Handles hover/click interactions and visual highlighting.
/// </summary>
public class RTTFileGridItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private Image _iconImage;
    private TextMeshProUGUI _nameText;
    private Image _bgImage;

    public string FilePath { get; private set; }
    public bool IsFolder { get; private set; }

    // Callbacks
    private Action<string> _onHoverEnter;
    private Action<string> _onHoverExit;
    private Action<string, bool> _onClick; // path, isFolder

    // Visual state
    private static readonly Color HoverColor = new Color(0f, 0f, 0f, 0.3f);
    private static readonly Color NormalColor = Color.clear;

    // Font
    private TMP_FontAsset _font;

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

    /// <summary>
    /// Bind new data to this item (for virtualization/pooling).
    /// </summary>
    public void Bind(string name, bool isFolder, string path, bool isFolderEmpty = false)
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
                SetSprite(isFolderEmpty ? "icon_folder_empty" : "icon_folder_not_empty");
            else
                SetSprite(IsImageFile(name) ? "icon_image" : "icon_file");
        }

        // Reset background
        if (_bgImage != null)
            _bgImage.color = NormalColor;
    }

    private void BuildUI()
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
        _iconImage.raycastTarget = false;

        SetSprite("icon_folder_not_empty");

        var iconLE = iconObj.AddComponent<LayoutElement>();
        iconLE.preferredHeight = 150f;
        iconLE.preferredWidth = 150f;
        iconLE.flexibleHeight = 0;

        // 3. Name Text
        GameObject textObj = new GameObject("Name");
        textObj.transform.SetParent(transform, false);
        _nameText = textObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) _nameText.font = _font;
        _nameText.raycastTarget = false;
        _nameText.text = "";
        _nameText.alignment = TextAlignmentOptions.Top;
        _nameText.fontSize = 32;
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.color = Color.white;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;
        _nameText.enableWordWrapping = true;

        var textLE = textObj.AddComponent<LayoutElement>();
        textLE.preferredHeight = 90f;
        textLE.flexibleHeight = 0;

        // 4. Background (for highlighting)
        _bgImage = gameObject.AddComponent<Image>();
        _bgImage.sprite = GetRoundedRectSprite();
        _bgImage.type = Image.Type.Sliced;
        _bgImage.color = NormalColor;
        _bgImage.raycastTarget = true;

        // NOTE: No BoxCollider needed - RTT uses GraphicRaycaster via panel's DisplayQuad collider
        // Adding BoxColliders to individual items causes raycast issues when items are in buffer zone
        // (outside visible RectMask2D area but still active for smooth scrolling)
    }

    #region Pointer Events
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_bgImage != null)
            _bgImage.color = HoverColor;

        _onHoverEnter?.Invoke(FilePath);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_bgImage != null)
            _bgImage.color = NormalColor;

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
        if (_bgImage != null)
        {
            _bgImage.color = color;
        }
    }

    /// <summary>
    /// Force clear hover state (used when item is recycled)
    /// </summary>
    public void ClearHoverState()
    {
        if (_bgImage != null)
            _bgImage.color = NormalColor;
    }
    #endregion

    #region Helper Methods
    // Cached rounded rectangle sprite
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
                float dx = Mathf.Abs(x - halfSize + 0.5f);
                float dy = Mathf.Abs(y - halfSize + 0.5f);

                float innerHalfX = halfSize - innerRadius;
                float innerHalfY = halfSize - innerRadius;

                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - innerRadius;

                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        int border = radius + 2;
        _cachedRoundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            Vector2.one * 0.5f,
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)
        );

        return _cachedRoundedSprite;
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
    #endregion
}
