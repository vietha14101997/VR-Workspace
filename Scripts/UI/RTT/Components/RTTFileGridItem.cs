using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Represents a single file or folder item in the Grid View.
/// Handles hover/click interactions and visual highlighting.
/// </summary>
public class RTTFileGridItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private Image _iconImage;
    private RectTransform _iconRect;
    private TextMeshProUGUI _nameText;
    private Image _bgImage;
    private LayoutElement _iconContainerLE;
    private RectTransform _iconContainerRect;

    // Edit Mode Checkbox
    private GameObject _checkbox;
    private Image _checkmarkIcon;
    private HoverEffectController _checkboxHoverController;
    private bool _isSelected = false;
    private bool _isEditMode = false;
    private Action<string, bool> _onSelectionChanged; // path, isSelected

    public string FilePath { get; private set; }
    public bool IsFolder { get; private set; }
    public bool IsSelected => _isSelected;

    // Callbacks
    private Action<string> _onHoverEnter;
    private Action<string> _onHoverExit;
    private Action<string, bool> _onClick; // path, isFolder

    // Visual state
    private static readonly Color HoverColor = new Color(0f, 0f, 0f, 0.3f);
    private static readonly Color NormalColor = Color.clear;

    // Hover effect
    private HoverEffectController _hoverController;
    private const float HOVER_SCALE = 1.05f;
    private const float HOVER_ANIMATION_SPEED = 12f;

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
            bool usesThumbnail = false;

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
                    usesThumbnail = true;
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

                                // Update AspectRatioFitter based on actual sprite aspect ratio
                                var aspectFitter = _iconImage.GetComponent<AspectRatioFitter>();
                                if (aspectFitter != null && sprite.texture != null)
                                {
                                    float spriteAspect = (float)sprite.texture.width / sprite.texture.height;
                                    const float targetAspect = 16f / 9f;

                                    // If image is wider than 16:9 → crop horizontally (EnvelopeParent)
                                    // If image is narrower (like 1:1 square) → show full image (FitInParent)
                                    if (spriteAspect >= targetAspect)
                                    {
                                        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                                    }
                                    else
                                    {
                                        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                                    }
                                    aspectFitter.aspectRatio = spriteAspect;

                                    // Force layout rebuild to apply AspectRatioFitter changes immediately
                                    LayoutRebuilder.ForceRebuildLayoutImmediate(_iconRect);
                                }
                            }
                        },
                        onFailed: null,  // Keep placeholder icon
                        priority: 0
                    );
                }
            }

            // Adjust icon size and AspectRatioFitter mode
            if (_iconRect != null)
            {
                var aspectFitter = _iconImage?.GetComponent<AspectRatioFitter>();

                if (usesThumbnail)
                {
                    // Thumbnails: center-crop to fill container (same as RTTMediaGridItem2)
                    _iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                    _iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                    _iconRect.anchoredPosition = Vector2.zero;

                    // Enable AspectRatioFitter with EnvelopeParent for center-crop behavior
                    if (aspectFitter != null)
                    {
                        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                        aspectFitter.aspectRatio = 16f / 9f;  // Default, will update when thumbnail loads
                    }
                }
                else
                {
                    // Resource icons: fixed smaller size, centered
                    _iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                    _iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                    _iconRect.sizeDelta = new Vector2(180f, 180f);
                    _iconRect.anchoredPosition = Vector2.zero;

                    // Disable AspectRatioFitter for regular icons
                    if (aspectFitter != null)
                    {
                        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.None;
                    }
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
        // 1. Setup Layout - icon container expands, text fixed at bottom
        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 10f;
        layout.padding = new RectOffset(10, 10, 15, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 2. Icon Container - fixed size, expands to fill space
        GameObject iconContainer = new GameObject("IconContainer");
        iconContainer.transform.SetParent(transform, false);
        _iconContainerRect = iconContainer.AddComponent<RectTransform>();

        _iconContainerLE = iconContainer.AddComponent<LayoutElement>();
        _iconContainerLE.preferredHeight = 200f;  // Reduced to make room for 2-line text
        _iconContainerLE.preferredWidth = 200f;
        _iconContainerLE.flexibleHeight = 1;

        // Add RectMask2D for thumbnail cropping (center-crop overflow)
        iconContainer.AddComponent<RectMask2D>();

        // 3. Icon Image - inside container, centered
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(iconContainer.transform, false);
        _iconRect = iconObj.AddComponent<RectTransform>();
        _iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        _iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        _iconRect.pivot = new Vector2(0.5f, 0.5f);
        _iconRect.anchoredPosition = Vector2.zero;
        _iconRect.sizeDelta = new Vector2(200f, 200f);

        _iconImage = iconObj.AddComponent<Image>();
        _iconImage.preserveAspect = true;
        _iconImage.raycastTarget = false;

        // Add AspectRatioFitter for thumbnail center-crop (same as RTTMediaGridItem2)
        var aspectFitter = iconObj.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.None;  // Disabled by default, enabled for thumbnails
        aspectFitter.aspectRatio = 1f;

        SetSprite("icon_folder_not_empty");

        // 3. Name Text - two lines at bottom for longer filenames
        GameObject textObj = new GameObject("Name");
        textObj.transform.SetParent(transform, false);

        // Add RectTransform first to ensure proper sizing
        RectTransform textRect = textObj.AddComponent<RectTransform>();

        _nameText = textObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) _nameText.font = _font;
        _nameText.raycastTarget = false;
        _nameText.text = "";
        _nameText.alignment = TextAlignmentOptions.Center;
        _nameText.fontSize = 32;  // Match List view font size
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.color = Color.white;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;
        _nameText.enableWordWrapping = true;  // Enable word wrap
        _nameText.maxVisibleLines = 2;  // Allow 2 lines

        var textLE = textObj.AddComponent<LayoutElement>();
        textLE.preferredHeight = 80f;  // Increased for 2 lines (32px * 2.5)
        textLE.flexibleHeight = 0;
        textLE.minHeight = 80f;  // Ensure minimum height

        // 4. Background (for highlighting)
        _bgImage = gameObject.AddComponent<Image>();
        _bgImage.sprite = GetRoundedRectSprite();
        _bgImage.type = Image.Type.Sliced;
        _bgImage.color = NormalColor;
        _bgImage.raycastTarget = true;

        // 5. Add Hover Effect Controller with Scale Effect
        _hoverController = gameObject.AddComponent<HoverEffectController>();
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(HOVER_SCALE)
            .WithTransitionDuration(1f / HOVER_ANIMATION_SPEED);
        _hoverController.AddEffect(scaleEffect);

        // 6. Create Edit Mode Checkbox (top-left corner, hidden by default)
        CreateCheckbox();

        // NOTE: No BoxCollider needed - RTT uses GraphicRaycaster via panel's DisplayQuad collider
        // Adding BoxColliders to individual items causes raycast issues when items are in buffer zone
        // (outside visible RectMask2D area but still active for smooth scrolling)
    }

    private void CreateCheckbox()
    {
        float checkboxSize = 45f;
        float offset = 8f;

        _checkbox = new GameObject("Checkbox");
        _checkbox.transform.SetParent(transform, false);

        // Position at top-left corner (outside VerticalLayoutGroup control)
        RectTransform checkboxRT = _checkbox.AddComponent<RectTransform>();
        checkboxRT.anchorMin = new Vector2(0, 1);
        checkboxRT.anchorMax = new Vector2(0, 1);
        checkboxRT.pivot = new Vector2(0, 1);
        checkboxRT.sizeDelta = new Vector2(checkboxSize, checkboxSize);
        checkboxRT.anchoredPosition = new Vector2(offset, -offset);

        // Ignore layout so it stays in corner
        var layoutIgnorer = _checkbox.AddComponent<LayoutElement>();
        layoutIgnorer.ignoreLayout = true;

        // Background (glass style)
        Image checkboxBg = _checkbox.AddComponent<Image>();
        checkboxBg.raycastTarget = true;
        Color primaryColor = new Color(0f, 0.9f, 1f); // Default cyan
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_Aspect", 1f);
            mat.SetFloat("_CornerRadius", 0.25f);
            mat.SetFloat("_EdgePadding", 0.02f);
            mat.SetColor("_ColorA", new Color(primaryColor.r, primaryColor.g, primaryColor.b, 0.4f));
            mat.SetColor("_ColorB", new Color(primaryColor.r, primaryColor.g, primaryColor.b, 0.15f));
            mat.SetFloat("_GlassAlpha", 0.25f);
            mat.SetFloat("_FresnelStrength", 0.15f);
            checkboxBg.material = mat;
            checkboxBg.color = Color.white;
        }

        // Checkmark icon (hidden by default)
        GameObject checkmarkObj = new GameObject("Checkmark");
        checkmarkObj.transform.SetParent(_checkbox.transform, false);
        RectTransform checkmarkRT = checkmarkObj.AddComponent<RectTransform>();
        checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkmarkRT.offsetMin = checkmarkRT.offsetMax = Vector2.zero;
        _checkmarkIcon = checkmarkObj.AddComponent<Image>();
        _checkmarkIcon.sprite = Resources.Load<Sprite>("icon_check_mark");
        _checkmarkIcon.color = Color.white;
        _checkmarkIcon.preserveAspect = true;
        _checkmarkIcon.raycastTarget = false;
        checkmarkObj.SetActive(false);

        // Button component for checkbox click
        Button checkboxButton = _checkbox.AddComponent<Button>();
        checkboxButton.transition = Selectable.Transition.None;
        checkboxButton.onClick.AddListener(OnCheckboxClicked);

        // Hover effect for checkbox
        _checkboxHoverController = _checkbox.AddComponent<HoverEffectController>();
        _checkboxHoverController.TargetVisuals = _checkbox.transform;
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(1.15f)
            .WithTransitionDuration(0.1f);
        _checkboxHoverController.AddEffect(scaleEffect);

        // Hidden by default (only shown in Edit Mode)
        _checkbox.SetActive(false);
    }

    #region Pointer Events
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_bgImage != null)
            _bgImage.color = HoverColor;

        // Forward hover to checkbox in edit mode
        if (_isEditMode && _checkboxHoverController != null)
            _checkboxHoverController.SetForceHover(true);

        _onHoverEnter?.Invoke(FilePath);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_bgImage != null)
            _bgImage.color = NormalColor;

        // Forward hover exit to checkbox in edit mode
        if (_isEditMode && _checkboxHoverController != null)
            _checkboxHoverController.SetForceHover(false);

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
    /// Cancels any pending thumbnail requests and resets visual state.
    /// </summary>
    public void OnRecycle()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            FileThumbnailService.Instance?.CancelRequest(_currentFilePath);
        }
        ClearHoverState();

        // Reset hover state for recycled items
        if (_hoverController != null)
        {
            _hoverController.ResetHoverState(immediate: true);
        }
    }
    #endregion

    #region Edit Mode
    public void SetSelectionCallback(Action<string, bool> onSelectionChanged)
    {
        _onSelectionChanged = onSelectionChanged;
    }

    public void SetEditMode(bool editMode)
    {
        _isEditMode = editMode;
        if (_checkbox != null)
            _checkbox.SetActive(editMode);

        // Clear selection when exiting edit mode
        if (!editMode)
        {
            _isSelected = false;
            UpdateCheckmarkVisual();
        }
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateCheckmarkVisual();
    }

    private void OnCheckboxClicked()
    {
        _isSelected = !_isSelected;
        UpdateCheckmarkVisual();
        _onSelectionChanged?.Invoke(FilePath, _isSelected);
    }

    private void UpdateCheckmarkVisual()
    {
        if (_checkmarkIcon != null)
            _checkmarkIcon.gameObject.SetActive(_isSelected);
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
