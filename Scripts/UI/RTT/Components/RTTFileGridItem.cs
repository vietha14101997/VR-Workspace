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

    // Thumbnail tracking
    private string _currentFilePath;  // Track current file for thumbnail cancellation
    private MockFile _currentFile;    // Current bound file
    private const int THUMBNAIL_SIZE = 512;  // Grid thumbnail size (high quality)

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
    /// Now accepts full MockFile for thumbnail support.
    /// </summary>
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

        // Update text
        if (_nameText != null)
            _nameText.text = file.Name;

        // Update icon
        if (_iconImage != null)
        {
            if (file.IsFolder)
            {
                SetSprite(file.IsFolderEmpty ? "icon_folder_empty" : "icon_folder_not_empty", "icon_folder");
            }
            else
            {
                var category = FileCategoryHelper.GetCategory(file.Type);

                // Request thumbnail for Image/Video categories
                if (FileCategoryHelper.RequiresThumbnailGeneration(category))
                {
                    // Use loading placeholder while thumbnail is being generated asynchronously
                    string loadingIcon = FileCategoryHelper.GetLoadingPlaceholderIcon(category);
                    SetSprite(loadingIcon, "icon_media_file");
                }
                else
                {
                    // Use default icon for non-thumbnail categories
                    string defaultIcon = FileCategoryHelper.GetDefaultIconName(category);
                    SetSprite(defaultIcon, "icon_file_unknown");
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
        }

        // Reset background
        if (_bgImage != null)
            _bgImage.color = NormalColor;
    }

    /// <summary>
    /// Legacy Bind method for backward compatibility.
    /// </summary>
    public void Bind(string name, bool isFolder, string path, bool isFolderEmpty = false)
    {
        // Create a minimal MockFile for backward compatibility
        var file = new MockFile
        {
            Name = name,
            Path = path,
            IsFolder = isFolder,
            IsFolderEmpty = isFolderEmpty,
            Type = isFolder ? "Folder" : System.IO.Path.GetExtension(name).TrimStart('.').ToLower()
        };
        Bind(file);
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

    /// <summary>
    /// Load and set an icon sprite from Resources.
    /// </summary>
    private void SetSprite(string resourceName, string fallbackIcon = "icon_file_unknown")
    {
        Sprite sprite = Resources.Load<Sprite>(resourceName);
        if (sprite != null)
        {
            _iconImage.sprite = sprite;
        }
        else
        {
            // Try fallback
            sprite = Resources.Load<Sprite>(fallbackIcon);
            if (sprite != null)
            {
                _iconImage.sprite = sprite;
            }
        }
    }
    #endregion
}
