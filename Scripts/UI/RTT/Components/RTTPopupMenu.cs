using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// RTTPopupMenu - A reusable popup menu component using RTTMenuFrame style.
/// Supports two types of content sections:
/// 1) Section Block: Title label + Grid of VRButtonFactory buttons
/// 2) Full-Width Button: Single button spanning full popup width
/// 
/// Features:
/// - Configurable width, auto-calculated height based on content
/// - Glass background with glow border (RTTMenuFrame style)
/// - Uniform button height across all buttons
/// - Consistent spacing between elements
/// </summary>
public class RTTPopupMenu : MonoBehaviour
{
    #region Nested Classes
    
    /// <summary>
    /// Configuration for the popup
    /// </summary>
    [System.Serializable]
    public class PopupConfig
    {
        public float width = 260f;
        public float buttonHeight = 40f;
        public float sideSpacing = 10f;   // Spacing between sections and padding
        public float rowSpacing = 8f;     // Spacing between button rows in grid
        public float labelHeight = 18f;
        public int fontSize = 15;
        public float iconSize = 20f;
        public Color backgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.96f);
        public Color primaryColor = new Color(0f, 0.9f, 1f);
        public Color accentColor = new Color(0.76f, 0.36f, 1f);
        public TMP_FontAsset font;
        public string layerName = "UI";
    }
    
    /// <summary>
    /// Data for a single button
    /// </summary>
    [System.Serializable]
    public class ButtonData
    {
        public string text;
        public Sprite icon;           // null = text only
        public Color? color;          // null = use primaryColor
        public bool isSelected;
        public UnityAction onClick;
        
        public ButtonData() { }
        
        public ButtonData(string text, UnityAction onClick, Sprite icon = null, bool isSelected = false, Color? color = null)
        {
            this.text = text;
            this.onClick = onClick;
            this.icon = icon;
            this.isSelected = isSelected;
            this.color = color;
        }
    }
    
    /// <summary>
    /// Section type enum
    /// </summary>
    public enum PopupSectionType
    {
        SectionBlock,     // Type 1: Title + Grid
        FullWidthButton   // Type 2: Single button full width
    }
    
    /// <summary>
    /// Internal section tracking
    /// </summary>
    private class PopupSection
    {
        public PopupSectionType type;
        public string title;
        public List<ButtonData> buttons;
        public int columns;
    }
    
    #endregion
    
    #region Private Fields
    
    private PopupConfig _config;
    private GameObject _popupObject;
    private RectTransform _popupRT;
    private VerticalLayoutGroup _contentLayout;
    private List<PopupSection> _sections = new List<PopupSection>();
    private bool _isBuilt = false;
    private PopupSectionType _lastSectionType = PopupSectionType.SectionBlock;
    
    private static Sprite _pixelSprite;
    private const float kBorderInset = 6f; // Visual border thickness offset
    
    #endregion
    
    #region Public Properties
    
    public bool IsVisible => _popupObject != null && _popupObject.activeSelf;
    public RectTransform PopupRectTransform => _popupRT;
    
    #endregion
    
    #region Factory Method
    
    /// <summary>
    /// Create a new RTTPopupMenu
    /// </summary>
    /// <param name="parent">Parent transform for the popup</param>
    /// <param name="config">Popup configuration</param>
    /// <returns>RTTPopupMenu instance</returns>
    public static RTTPopupMenu Create(Transform parent, PopupConfig config)
    {
        GameObject menuObj = new GameObject("RTTPopupMenu");
        menuObj.transform.SetParent(parent, false);
        
        RTTPopupMenu menu = menuObj.AddComponent<RTTPopupMenu>();
        menu._config = config ?? new PopupConfig();
        menu.BuildPopupContainer();
        
        return menu;
    }
    
    /// <summary>
    /// Create with default config
    /// </summary>
    public static RTTPopupMenu Create(Transform parent, float width, Color primaryColor, TMP_FontAsset font = null)
    {
        var config = new PopupConfig
        {
            width = width,
            primaryColor = primaryColor,
            font = font
        };
        return Create(parent, config);
    }
    
    #endregion
    
    #region Public API - Add Sections
    
    /// <summary>
    /// Add a Section Block (Type 1): Title + Grid of buttons
    /// </summary>
    /// <param name="title">Section title label</param>
    /// <param name="buttons">List of button data</param>
    /// <param name="columns">Number of columns in grid</param>
    public RTTPopupMenu AddSectionBlock(string title, List<ButtonData> buttons, int columns = 2)
    {
        _sections.Add(new PopupSection
        {
            type = PopupSectionType.SectionBlock,
            title = title,
            buttons = buttons,
            columns = columns
        });
        return this;
    }
    
    /// <summary>
    /// Add a Full-Width Button (Type 2): Single button spanning full width
    /// </summary>
    /// <param name="button">Button data</param>
    public RTTPopupMenu AddFullWidthButton(ButtonData button)
    {
        _sections.Add(new PopupSection
        {
            type = PopupSectionType.FullWidthButton,
            buttons = new List<ButtonData> { button },
            columns = 1
        });
        return this;
    }
    
    /// <summary>
    /// Add a separator line
    /// </summary>
    public RTTPopupMenu AddSeparator()
    {
        // Will be rendered as a thin horizontal line
        _sections.Add(new PopupSection
        {
            type = PopupSectionType.FullWidthButton,
            buttons = null, // null buttons = separator
            columns = 0
        });
        return this;
    }
    
    #endregion
    
    #region Public API - Build & Lifecycle
    
    /// <summary>
    /// Build the popup UI from added sections
    /// Call this after adding all sections
    /// </summary>
    public RTTPopupMenu Build()
    {
        if (_isBuilt)
        {
            // Clear existing content
            ClearContent();
        }
        
        float totalHeight = CalculateTotalHeight();
        _popupRT.sizeDelta = new Vector2(_config.width, totalHeight);
        
        // Update background material aspect ratio
        UpdateBackgroundAspect(totalHeight);
        
        // Build sections
        foreach (var section in _sections)
        {
            BuildSection(section);
            _lastSectionType = section.type;
        }
        
        _isBuilt = true;
        return this;
    }
    
    /// <summary>
    /// Show the popup
    /// </summary>
    public void Show()
    {
        if (!_isBuilt)
        {
            Build();
        }
        
        if (_popupObject != null)
        {
            _popupObject.SetActive(true);
        }
    }
    
    /// <summary>
    /// Hide the popup
    /// </summary>
    public void Hide()
    {
        if (_popupObject != null)
        {
            _popupObject.SetActive(false);
        }
    }
    
    /// <summary>
    /// Toggle popup visibility
    /// </summary>
    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }
    
    /// <summary>
    /// Clear all sections and rebuild
    /// </summary>
    public void Clear()
    {
        _sections.Clear();
        ClearContent();
        _isBuilt = false;
    }
    
    /// <summary>
    /// Set popup position (anchored)
    /// </summary>
    public void SetPosition(Vector2 anchoredPosition)
    {
        if (_popupRT != null)
        {
            _popupRT.anchoredPosition = anchoredPosition;
        }
    }
    
    /// <summary>
    /// Set anchor and pivot
    /// </summary>
    public void SetAnchor(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        if (_popupRT != null)
        {
            _popupRT.anchorMin = anchorMin;
            _popupRT.anchorMax = anchorMax;
            _popupRT.pivot = pivot;
        }
    }
    
    #endregion
    
    #region Private - Build Methods
    
    private void BuildPopupContainer()
    {
        // Main popup container
        _popupObject = new GameObject("PopupContainer");
        _popupObject.transform.SetParent(transform, false);
        
        // Set layer
        int layer = LayerMask.NameToLayer(_config.layerName);
        if (layer != -1) _popupObject.layer = layer;
        
        _popupRT = _popupObject.AddComponent<RectTransform>();
        _popupRT.anchorMin = new Vector2(0, 1);
        _popupRT.anchorMax = new Vector2(0, 1);
        _popupRT.pivot = new Vector2(0, 1);
        _popupRT.sizeDelta = new Vector2(_config.width, 100f); // Placeholder height
        
        // Canvas for sorting
        Canvas canvas = _popupObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 200;
        _popupObject.AddComponent<GraphicRaycaster>();
        
        // Background with RTTMenuFrame style
        CreateBackground();
        
        // Glow Border (RTTMenuFrame style)
        CreateGlowBorder();
        
        // Content Container (separate from background)
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(_popupObject.transform, false);
        RectTransform contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        
        // Content Layout - Configured padding per user request
        VerticalLayoutGroup vlg = contentObj.AddComponent<VerticalLayoutGroup>();
        
        // Add border inset to ensure content is not covered by the glow border
        // Increase margins: Horizontal 75%, Vertical 50%
        float horizontalPadding = ((_config.rowSpacing * 0.75f) + kBorderInset) * 1.75f;
        float verticalPadding = (_config.rowSpacing + kBorderInset) * 1.5f;
        
        vlg.padding = new RectOffset(
            (int)horizontalPadding, 
            (int)horizontalPadding, 
            (int)verticalPadding, 
            (int)verticalPadding
        );
        vlg.spacing = _config.sideSpacing; // Spacing between sections
        vlg.childControlWidth = true;
        vlg.childControlHeight = true; // Fix: Control height to respect LayoutElements
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        _contentLayout = vlg;
        
        // Store content container reference
        _contentContainer = contentObj;
        
        // Hidden by default
        _popupObject.SetActive(false);
    }
    
    private GameObject _contentContainer;
    
    private void CreateBackground()
    {
        Image bgImg = _popupObject.AddComponent<Image>();
        bgImg.sprite = GetPixelSprite();
        
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_CornerRadius", 0.04f);
            mat.SetFloat("_EdgePadding", 0.01f);
            mat.SetFloat("_Aspect", _config.width / 100f);
            
            // RTTMenuFrame style glass colors
            Color glassColorA = new Color(0.0f, 0.55f, 0.65f, 0.35f);
            Color glassColorB = new Color(0.30f, 0.12f, 0.50f, 0.32f);
            mat.SetColor("_ColorA", glassColorA);
            mat.SetColor("_ColorB", glassColorB);
            mat.SetFloat("_GlassAlpha", 0.75f);
            mat.SetFloat("_GradientOffset", 0f);
            mat.SetFloat("_GradientAngle", -10f);
            mat.SetFloat("_CyanRatio", 0.7f);
            mat.SetFloat("_FresnelPower", 2.2f);
            mat.SetFloat("_FresnelStrength", 0.12f);
            
            bgImg.material = mat;
            bgImg.color = Color.white;
        }
        else
        {
            bgImg.color = _config.backgroundColor;
        }
    }
    
    private void CreateGlowBorder()
    {
        GameObject borderObj = new GameObject("GlowBorder");
        borderObj.transform.SetParent(_popupObject.transform, false);
        
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;
        
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetPixelSprite();
        borderImg.raycastTarget = false;
        
        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            Material mat = new Material(glowShader);
            mat.SetFloat("_StrokeEnabled", 0);
            mat.SetFloat("_BorderWidth", 0.028f); // Increased 40% (was 0.02f)
            mat.SetFloat("_CornerRadius", 0.04f);
            mat.SetFloat("_EdgePadding", 0.01f);
            mat.SetFloat("_Aspect", _config.width / 100f);
            
            // Glow layers - constrained to stay within border bounds
            mat.SetFloat("_Layer1Width", 0.006f);
            mat.SetFloat("_Layer1Alpha", 1.2f);
            mat.SetFloat("_Layer2Width", 0.01f);
            mat.SetFloat("_Layer2Alpha", 0.8f);
            mat.SetFloat("_Layer3Width", 0.015f);
            mat.SetFloat("_Layer3Alpha", 0.4f);
            mat.SetFloat("_Layer4Width", 0.02f);
            mat.SetFloat("_Layer4Alpha", 0.2f);
            
            // RTTMenuFrame glow colors
            Color glowColorA = new Color(0.3f, 1f, 1f, 1f);
            Color glowColorB = new Color(1f, 0.4f, 1f, 1f);
            mat.SetColor("_ColorA", glowColorA);
            mat.SetColor("_ColorB", glowColorB);
            mat.SetFloat("_GradientMode", 2f);
            mat.SetFloat("_GradientAngle", -10f);
            mat.SetFloat("_GlassAlpha", 0.02f);
            mat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            mat.SetFloat("_ShimmerSpeed", 0.4f);
            mat.SetFloat("_ShimmerIntensity", 0.2f);
            mat.SetFloat("_LightSize", 0.008f);
            mat.SetFloat("_LightGlow", 0.008f);
            
            borderImg.material = mat;
            _borderMaterial = mat;
        }
    }
    
    private Material _borderMaterial;
    
    private void UpdateBackgroundAspect(float height)
    {
        Image bgImg = _popupObject.GetComponent<Image>();
        if (bgImg != null && bgImg.material != null)
        {
            bgImg.material.SetFloat("_Aspect", _config.width / height);
        }
        
        // Also update border aspect
        if (_borderMaterial != null)
        {
            _borderMaterial.SetFloat("_Aspect", _config.width / height);
        }
    }
    
    private void BuildSection(PopupSection section)
    {
        if (section.type == PopupSectionType.SectionBlock)
        {
            BuildSectionBlock(section);
        }
        else if (section.type == PopupSectionType.FullWidthButton)
        {
            if (section.buttons == null)
            {
                // Separator
                BuildSeparator();
            }
            else
            {
                BuildFullWidthButton(section.buttons[0]);
            }
        }
    }
    
    private void BuildSectionBlock(PopupSection section)
    {
        // Container for label + grid (to control spacing between label and grid)
        GameObject sectionObj = new GameObject("Section_" + section.title);
        sectionObj.transform.SetParent(_contentContainer.transform, false);
        SetLayerRecursively(sectionObj, _popupObject.layer);
        
        VerticalLayoutGroup sectionVLG = sectionObj.AddComponent<VerticalLayoutGroup>();
        sectionVLG.spacing = _config.rowSpacing; // spacing between label and grid = spacing between button rows
        sectionVLG.childControlWidth = true;
        sectionVLG.childControlHeight = true; // Fix: Control height to respect LayoutElements
        sectionVLG.childForceExpandWidth = true;
        sectionVLG.childForceExpandHeight = false;
        
        // Calculate section height
        int rowCount = Mathf.CeilToInt((float)section.buttons.Count / section.columns);
        float gridHeight = (rowCount * _config.buttonHeight) + ((rowCount - 1) * _config.rowSpacing);
        // Fix: Ensure sectionHeight calculation is totally accurate
        float sectionHeight = _config.labelHeight + _config.rowSpacing + gridHeight;
        
        LayoutElement sectionLE = sectionObj.AddComponent<LayoutElement>();
        sectionLE.preferredHeight = sectionHeight;
        
        // Title Label
        CreateSectionLabel(sectionObj.transform, section.title);
        
        // Grid container
        GameObject gridObj = new GameObject("Grid_" + section.title);
        gridObj.transform.SetParent(sectionObj.transform, false);
        SetLayerRecursively(gridObj, _popupObject.layer);
        
        GridLayoutGroup glg = gridObj.AddComponent<GridLayoutGroup>();
        float cellWidth = CalculateCellWidth(section.columns);
        glg.cellSize = new Vector2(cellWidth, _config.buttonHeight);
        glg.spacing = new Vector2(_config.rowSpacing, _config.rowSpacing);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = section.columns;
        glg.childAlignment = TextAnchor.UpperCenter;
        
        LayoutElement gridLE = gridObj.AddComponent<LayoutElement>();
        gridLE.preferredHeight = gridHeight;
        
        // Create buttons
        foreach (var btnData in section.buttons)
        {
            CreateButton(gridObj.transform, btnData, cellWidth);
        }
    }
    
    private void BuildFullWidthButton(ButtonData btnData)
    {
        float horizontalPadding = ((_config.rowSpacing * 0.75f) + kBorderInset) * 1.75f;
        float buttonWidth = _config.width - (horizontalPadding * 2);
        CreateButton(_contentContainer.transform, btnData, buttonWidth, true);
    }
    
    private void BuildSeparator()
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(_contentContainer.transform, false);
        SetLayerRecursively(sep, _popupObject.layer);
        
        Image sepImg = sep.AddComponent<Image>();
        sepImg.color = new Color(0.5f, 0.7f, 0.8f, 0.4f);
        
        LayoutElement sepLE = sep.AddComponent<LayoutElement>();
        sepLE.preferredHeight = 1f;
    }
    
    private void CreateSectionLabel(Transform parent, string text)
    {
        GameObject labelObj = new GameObject("Label_" + text);
        labelObj.transform.SetParent(parent, false);
        SetLayerRecursively(labelObj, _popupObject.layer);
        
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 18; // Increased size to match Name word
        label.font = _config.font;
        label.color = Color.white; // Pure white as requested
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Left;
        label.margin = new Vector4(4f, 0, 0, 0); // Fix alignment with grid
        label.raycastTarget = false;
        
        LayoutElement le = labelObj.AddComponent<LayoutElement>();
        le.preferredHeight = _config.labelHeight;
    }
    
    private void CreateButton(Transform parent, ButtonData data, float width, bool isFullWidth = false)
    {
        Color btnColor = data.color ?? (data.isSelected ? _config.accentColor : _config.primaryColor);
        
        GameObject btn;
        
        // Text only button base (Text is always centered)
        btn = VRButtonFactory.CreateTextButton(
            parent as RectTransform, width, _config.buttonHeight, 
            data.text, btnColor,
            data.onClick, _config.fontSize, _config.font
        );

        // Add Icon manually if present (Absolute positioning, left aligned)
        if (data.icon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(btn.transform, false);
            
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = data.icon;
            iconImg.color = Color.white; // Keep original icon color
            iconImg.raycastTarget = false;

            RectTransform iconRT = iconObj.GetComponent<RectTransform>();
            // Anchor Left-Center
            iconRT.anchorMin = new Vector2(0, 0.5f);
            iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            
            // Use config.iconSize as requested to match Edit button scale
            float size = _config.iconSize; 
            iconRT.sizeDelta = new Vector2(size, size);
            
            // Adjust left padding for larger icon to avoid corner clipping
            iconRT.anchoredPosition = new Vector2(16f, 0); 
        }
        
        // Ensure consistent sizing
        LayoutElement le = btn.GetComponent<LayoutElement>();
        if (le == null) le = btn.AddComponent<LayoutElement>();
        
        le.preferredWidth = width;
        le.preferredHeight = _config.buttonHeight;
        
        if (!isFullWidth)
        {
            le.flexibleWidth = 0;
            le.minWidth = width;
        }
        else
        {
            le.flexibleWidth = 1;
        }
        
        SetLayerRecursively(btn, _popupObject.layer);
    }
    
    #endregion
    
    #region Private - Calculation Methods
    
    private float CalculateTotalHeight()
    {
        float verticalPadding = (_config.rowSpacing + kBorderInset) * 1.5f;
        float height = verticalPadding * 2; // Top + Bottom padding
        
        for (int i = 0; i < _sections.Count; i++)
        {
            var section = _sections[i];
            
            if (section.type == PopupSectionType.SectionBlock)
            {
                // Label height
                height += _config.labelHeight;
                height += _config.rowSpacing; // Spacing after label (same as row spacing)
                
                // Grid height
                int rowCount = Mathf.CeilToInt((float)section.buttons.Count / section.columns);
                float gridHeight = (rowCount * _config.buttonHeight) + ((rowCount - 1) * _config.rowSpacing);
                height += gridHeight;
            }
            else if (section.type == PopupSectionType.FullWidthButton)
            {
                if (section.buttons == null)
                {
                    // Separator
                    height += 1f;
                }
                else
                {
                    // Full width button
                    height += _config.buttonHeight;
                }
            }
            
            // Add spacing between sections (except for last)
            if (i < _sections.Count - 1)
            {
                height += _config.sideSpacing;
            }
        }
        
        return height;
    }
    
    private float CalculateCellWidth(int columns)
    {
        float horizontalPadding = ((_config.rowSpacing * 0.75f) + kBorderInset) * 1.75f;
        float availableWidth = _config.width - (horizontalPadding * 2);
        float totalSpacing = (columns - 1) * _config.rowSpacing;
        return (availableWidth - totalSpacing) / columns;
    }
    
    #endregion
    
    #region Private - Utility Methods
    
    private void ClearContent()
    {
        if (_contentContainer == null) return;
        
        // Clear only content children, preserve background and border
        for (int i = _contentContainer.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(_contentContainer.transform.GetChild(i).gameObject);
        }
    }
    
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    
    private static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }
    
    #endregion
    
    #region Unity Lifecycle
    
    private void OnDestroy()
    {
        if (_popupObject != null)
        {
            // Clean up material
            Image bgImg = _popupObject.GetComponent<Image>();
            if (bgImg != null && bgImg.material != null)
            {
                Destroy(bgImg.material);
            }
        }
    }
    
    #endregion
}
