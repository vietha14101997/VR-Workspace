using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Factory class để tạo VR Dropdown với đầy đủ hiệu ứng:
/// - Glass background với gradient
/// - Glowing border với hover effect
/// - Icon bên trái (1/3 box)
/// - Label nhỏ phía trên + Value bên dưới + Arrow (2/3 box)
/// - Dropdown panel với các option
/// - VRButtonAnimation cho hover effects
/// - BoxCollider cho VR raycast
///
/// Chiều cao và icon size tự động tính từ font size:
/// - Box height = valueFontSize * 3.5
/// - Icon size = valueFontSize * 1.5
/// </summary>
public static class VRDropdownFactory
{
    private static Sprite _pixelSprite;

    // Hằng số layout
    private const float FONT_TO_BOX_RATIO = 4.2f;      // Tỷ lệ font size -> box height (tăng từ 3.5)
    private const float FONT_TO_ICON_RATIO = 2.0f;     // Tỷ lệ font size -> icon size (tăng từ 1.5)
    private const float ICON_ZONE_RATIO = 0.28f;       // 28% cho icon zone (giảm để icon to hơn trong zone)
    private const float CONTENT_PADDING = 12f;          // Padding cho content zone
    private const float ARROW_WIDTH = 35f;              // Chiều rộng arrow

    /// <summary>
    /// Cấu hình cho Dropdown
    /// </summary>
    [System.Serializable]
    public class DropdownConfig
    {
        public string label = "Label";
        public Sprite icon;
        public Color themeColor = new Color(0f, 0.9f, 1f);
        public float width = 300f;
        public int labelFontSize = 24;
        public int valueFontSize = 36;
        public TMP_FontAsset font;

        // Options
        public List<string> options = new List<string>();
        public List<Sprite> optionIcons = new List<Sprite>();
        public int defaultIndex = 0;

        // Visual settings - giá trị chuẩn cho reference height 150px
        private const float REFERENCE_HEIGHT = 150f;
        public float cornerRadius = 0.12f;
        public float edgePadding = 0.12f;
        public float backgroundAlpha = 0.15f;
        public float borderWidth = 0.015f;
        public float glowWidth = 0.03f;
        public float glowIntensity = 2.5f;

        // Tính các giá trị visual được scale theo kích thước thực tế
        public float ScaledCornerRadius => cornerRadius * (REFERENCE_HEIGHT / BoxHeight);
        public float ScaledEdgePadding => edgePadding * (REFERENCE_HEIGHT / BoxHeight);
        public float ScaledBorderWidth => borderWidth * (REFERENCE_HEIGHT / BoxHeight);
        public float ScaledGlowWidth => glowWidth * (REFERENCE_HEIGHT / BoxHeight);

        // Animation
        public float popAmount = 0.005f;

        // Dropdown panel
        public int maxVisibleOptions = 5;

        // Layer
        public string layerName = "VirtualObjects";

        // Tính chiều cao box từ font size
        public float BoxHeight => valueFontSize * FONT_TO_BOX_RATIO;

        // Tính icon size từ font size
        public float IconSize => valueFontSize * FONT_TO_ICON_RATIO;

        // Tính chiều cao option từ font size
        public float OptionHeight => valueFontSize * 2f;

        // Tính icon size trong option từ font size
        public float OptionIconSize => valueFontSize * 1f;
    }

    /// <summary>
    /// Tạo VR Dropdown với đầy đủ hiệu ứng
    /// Chiều cao tự động tính từ font size
    /// </summary>
    public static GameObject CreateDropdown(Transform parent, DropdownConfig config,
        System.Action<int, string> onValueChanged = null)
    {
        float boxHeight = config.BoxHeight;

        // 1. Wrapper
        GameObject wrapper = new GameObject("Dropdown_" + config.label);
        wrapper.transform.SetParent(parent, false);
        RectTransform wrapperRT = wrapper.AddComponent<RectTransform>();
        wrapperRT.sizeDelta = new Vector2(config.width, boxHeight);

        // 2. HitArea - vùng click/collider
        GameObject hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(wrapper.transform, false);
        RectTransform hitRT = hitArea.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = Vector2.zero;
        hitRT.offsetMax = Vector2.zero;

        // Transparent image cho UI raycast
        Image hitImg = hitArea.AddComponent<Image>();
        hitImg.color = Color.clear;

        // BoxCollider cho VR raycast
        BoxCollider col = hitArea.AddComponent<BoxCollider>();
        col.size = new Vector3(config.width, boxHeight, 0.1f);
        col.center = new Vector3(0, 0, -0.1f);

        // Set layer
        int vrLayer = LayerMask.NameToLayer(config.layerName);
        if (vrLayer != -1) hitArea.layer = vrLayer;

        // 3. Visuals - container cho visual elements (bằng kích thước HitArea)
        GameObject visuals = new GameObject("Visuals");
        visuals.transform.SetParent(hitArea.transform, false);
        RectTransform visRT = visuals.AddComponent<RectTransform>();
        visRT.anchorMin = Vector2.zero;
        visRT.anchorMax = Vector2.one;
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // 4. Background
        Image bgImg = CreateBackground(visuals.transform, config);

        // 5. Border với ripple effect
        CreateBorder(visuals.transform, config);

        // 6. Content (Icon + Label + Value + Arrow)
        TextMeshProUGUI valueTxt = CreateContent(visuals.transform, config);

        // 7. Button component
        Button btn = hitArea.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.transition = Selectable.Transition.None;

        // 8. VRButtonAnimation cho hover effects
        VRButtonAnimation anim = hitArea.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visuals.transform;
        anim.popAmount = config.popAmount;

        // 9. VRDropdown component TRƯỚC khi tạo panel
        VRDropdown dropdown = wrapper.AddComponent<VRDropdown>();

        // 10. Dropdown Panel
        GameObject dropdownPanel = CreateDropdownPanel(wrapper.transform, config, valueTxt, onValueChanged);
        dropdownPanel.SetActive(false);

        // 11. Initialize dropdown component
        dropdown.Initialize(config.options, config.defaultIndex, valueTxt, dropdownPanel, onValueChanged);

        // 12. Toggle dropdown on click
        btn.onClick.AddListener(() =>
        {
            dropdownPanel.SetActive(!dropdownPanel.activeSelf);
        });

        return wrapper;
    }

    /// <summary>
    /// Tạo Dropdown với icon (như Monitors, Bitrate trong hình)
    /// Chiều cao tự động tính từ font size
    /// </summary>
    public static GameObject CreateIconDropdown(Transform parent, float width,
        string label, Sprite icon, Color color, List<string> options, int defaultIndex,
        System.Action<int, string> onValueChanged = null,
        int labelFontSize = 24, int valueFontSize = 36, TMP_FontAsset font = null)
    {
        var config = new DropdownConfig
        {
            label = label,
            icon = icon,
            themeColor = color,
            width = width,
            labelFontSize = labelFontSize,
            valueFontSize = valueFontSize,
            font = font,
            options = options,
            defaultIndex = defaultIndex
        };
        return CreateDropdown(parent, config, onValueChanged);
    }

    /// <summary>
    /// Tạo Dropdown đơn giản không có icon
    /// Chiều cao tự động tính từ font size
    /// </summary>
    public static GameObject CreateSimpleDropdown(Transform parent, float width,
        string label, Color color, List<string> options, int defaultIndex,
        System.Action<int, string> onValueChanged = null,
        int labelFontSize = 24, int valueFontSize = 36, TMP_FontAsset font = null)
    {
        var config = new DropdownConfig
        {
            label = label,
            icon = null,
            themeColor = color,
            width = width,
            labelFontSize = labelFontSize,
            valueFontSize = valueFontSize,
            font = font,
            options = options,
            defaultIndex = defaultIndex
        };
        return CreateDropdown(parent, config, onValueChanged);
    }

    /// <summary>
    /// Tính chiều cao của Dropdown dựa trên font size
    /// </summary>
    public static float CalculateHeight(int valueFontSize)
    {
        return valueFontSize * FONT_TO_BOX_RATIO;
    }

    /// <summary>
    /// Lấy VRDropdown component từ wrapper object
    /// </summary>
    public static VRDropdown GetDropdown(GameObject wrapper)
    {
        return wrapper.GetComponent<VRDropdown>();
    }

    /// <summary>
    /// Lấy index được chọn từ wrapper object
    /// </summary>
    public static int GetSelectedIndex(GameObject wrapper)
    {
        var dropdown = GetDropdown(wrapper);
        return dropdown != null ? dropdown.SelectedIndex : -1;
    }

    /// <summary>
    /// Lấy giá trị được chọn từ wrapper object
    /// </summary>
    public static string GetSelectedValue(GameObject wrapper)
    {
        var dropdown = GetDropdown(wrapper);
        return dropdown != null ? dropdown.SelectedValue : "";
    }

    /// <summary>
    /// Đặt giá trị được chọn theo index
    /// </summary>
    public static void SetSelectedIndex(GameObject wrapper, int index)
    {
        var dropdown = GetDropdown(wrapper);
        if (dropdown != null)
        {
            dropdown.SetSelectedIndex(index);
        }
    }

    // ==================== INTERNAL HELPERS ====================

    private static Image CreateBackground(Transform parent, DropdownConfig config)
    {
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(parent, false);
        RectTransform rt = bgObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / config.BoxHeight;
        Color col = config.themeColor;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_CornerRadius", config.ScaledCornerRadius);
            mat.SetFloat("_EdgePadding", 0f);
            mat.SetFloat("_Aspect", aspect);
            mat.SetColor("_ColorA", new Color(col.r, col.g, col.b, config.backgroundAlpha * 1.5f));
            mat.SetColor("_ColorB", new Color(col.r, col.g, col.b, config.backgroundAlpha * 0.5f));
            mat.SetFloat("_GlassAlpha", config.backgroundAlpha);
            img.material = mat;
            img.color = Color.white;
        }
        else
        {
            img.color = new Color(col.r, col.g, col.b, config.backgroundAlpha);
        }

        return img;
    }

    private static void CreateBorder(Transform parent, DropdownConfig config)
    {
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(parent, false);
        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = borderObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / config.BoxHeight;
        Color col = config.themeColor;

        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material mat = new Material(glowShader);
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_EdgePadding", 0f);
            mat.SetFloat("_CornerRadius", config.ScaledCornerRadius);

            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            mat.SetColor("_GlowColor", borderGlowCol);

            mat.SetFloat("_BorderWidth", config.ScaledBorderWidth);
            mat.SetFloat("_GlowWidth", config.ScaledGlowWidth);
            mat.SetFloat("_GlowIntensity", config.glowIntensity);
            mat.SetFloat("_PulseEnabled", 0f);

            img.material = mat;

            // VRButtonRipple cho ripple effect
            borderObj.AddComponent<VRButtonRipple>().Initialize(mat, img);
        }
    }

    private static TextMeshProUGUI CreateContent(Transform parent, DropdownConfig config)
    {
        GameObject content = new GameObject("Content");
        content.transform.SetParent(parent, false);
        RectTransform cRT = content.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.offsetMin = Vector2.zero;
        cRT.offsetMax = Vector2.zero;

        bool hasIcon = config.icon != null;
        float iconSize = config.IconSize;

        // Layout: 1/3 trái cho icon, 2/3 phải cho content
        float iconZoneRatio = hasIcon ? ICON_ZONE_RATIO : 0f;

        // ==================== KHU VỰC ICON (1/3 bên trái) ====================
        if (hasIcon)
        {
            // Icon zone container
            GameObject iconZone = new GameObject("IconZone");
            iconZone.transform.SetParent(content.transform, false);
            RectTransform iconZoneRT = iconZone.AddComponent<RectTransform>();
            iconZoneRT.anchorMin = Vector2.zero;
            iconZoneRT.anchorMax = new Vector2(iconZoneRatio, 1f);
            iconZoneRT.offsetMin = Vector2.zero;
            iconZoneRT.offsetMax = Vector2.zero;

            // Icon đặt ở chính giữa khu vực icon
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(iconZone.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.5f, 0.5f);
            iconRT.anchorMax = new Vector2(0.5f, 0.5f);
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.anchoredPosition = Vector2.zero;
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);

            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = config.icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.color = Color.Lerp(config.themeColor, Color.white, 0.9f);

            // Icon glow effects
            Color glowCol = Color.Lerp(config.themeColor, Color.white, 0.7f);
            glowCol.a = 0.4f;
            float s1 = 2f;

            Shadow shadow1 = iconObj.AddComponent<Shadow>();
            shadow1.effectColor = glowCol;
            shadow1.effectDistance = new Vector2(s1, -s1);

            Shadow shadow2 = iconObj.AddComponent<Shadow>();
            shadow2.effectColor = glowCol;
            shadow2.effectDistance = new Vector2(-s1, s1);

            Color bloomCol = Color.Lerp(config.themeColor, Color.white, 0.8f);
            bloomCol.a = 0.15f;
            float s2 = 5f;

            Shadow shadow3 = iconObj.AddComponent<Shadow>();
            shadow3.effectColor = bloomCol;
            shadow3.effectDistance = new Vector2(s2, -s2);

            Shadow shadow4 = iconObj.AddComponent<Shadow>();
            shadow4.effectColor = bloomCol;
            shadow4.effectDistance = new Vector2(-s2, s2);
        }

        // ==================== KHU VỰC CONTENT (2/3 bên phải) ====================
        GameObject contentZone = new GameObject("ContentZone");
        contentZone.transform.SetParent(content.transform, false);
        RectTransform contentZoneRT = contentZone.AddComponent<RectTransform>();
        contentZoneRT.anchorMin = new Vector2(iconZoneRatio, 0f);
        contentZoneRT.anchorMax = Vector2.one;
        contentZoneRT.offsetMin = new Vector2(CONTENT_PADDING, 0f);
        contentZoneRT.offsetMax = new Vector2(-CONTENT_PADDING, 0f);

        // Nửa trên: Title (Label)
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(contentZone.transform, false);
        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.5f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(0f, 5f);
        labelRT.offsetMax = new Vector2(0f, -10f);

        TextMeshProUGUI labelTxt = labelObj.AddComponent<TextMeshProUGUI>();
        labelTxt.text = config.label;
        labelTxt.fontSize = config.labelFontSize;
        labelTxt.color = new Color(1f, 1f, 1f, 0.6f);
        labelTxt.alignment = TextAlignmentOptions.BottomLeft;
        labelTxt.verticalAlignment = VerticalAlignmentOptions.Bottom;
        labelTxt.raycastTarget = false;
        labelTxt.enableWordWrapping = false;
        labelTxt.overflowMode = TextOverflowModes.Ellipsis;
        if (config.font != null) labelTxt.font = config.font;

        // Nửa dưới: Container cho Value + Arrow
        GameObject bottomRow = new GameObject("BottomRow");
        bottomRow.transform.SetParent(contentZone.transform, false);
        RectTransform bottomRowRT = bottomRow.AddComponent<RectTransform>();
        bottomRowRT.anchorMin = Vector2.zero;
        bottomRowRT.anchorMax = new Vector2(1f, 0.5f);
        bottomRowRT.offsetMin = new Vector2(0f, 10f);
        bottomRowRT.offsetMax = new Vector2(0f, -5f);

        // Value text - chiếm hầu hết nửa dưới, chừa chỗ cho arrow
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(bottomRow.transform, false);
        RectTransform valueRT = valueObj.AddComponent<RectTransform>();
        valueRT.anchorMin = Vector2.zero;
        valueRT.anchorMax = Vector2.one;
        valueRT.offsetMin = Vector2.zero;
        valueRT.offsetMax = new Vector2(-ARROW_WIDTH, 0f);

        TextMeshProUGUI valueTxt = valueObj.AddComponent<TextMeshProUGUI>();
        string defaultValue = config.options.Count > config.defaultIndex ? config.options[config.defaultIndex] : "";
        valueTxt.text = defaultValue;
        valueTxt.fontSize = config.valueFontSize;
        valueTxt.color = Color.white;
        valueTxt.fontStyle = FontStyles.Bold;
        valueTxt.alignment = TextAlignmentOptions.Left;
        valueTxt.verticalAlignment = VerticalAlignmentOptions.Top;
        valueTxt.raycastTarget = false;
        valueTxt.enableWordWrapping = false;
        valueTxt.overflowMode = TextOverflowModes.Ellipsis;
        if (config.font != null) valueTxt.font = config.font;

        // Arrow indicator - cố định ở cạnh phải
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(bottomRow.transform, false);
        RectTransform arrowRT = arrowObj.AddComponent<RectTransform>();
        arrowRT.anchorMin = new Vector2(1f, 0f);
        arrowRT.anchorMax = new Vector2(1f, 1f);
        arrowRT.pivot = new Vector2(1f, 0.5f);
        arrowRT.anchoredPosition = Vector2.zero;
        arrowRT.sizeDelta = new Vector2(ARROW_WIDTH, 0f);

        TextMeshProUGUI arrowTxt = arrowObj.AddComponent<TextMeshProUGUI>();
        arrowTxt.text = "▼";
        arrowTxt.fontSize = config.valueFontSize * 0.6f;
        arrowTxt.color = new Color(1f, 1f, 1f, 0.6f);
        arrowTxt.alignment = TextAlignmentOptions.Center;
        arrowTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
        arrowTxt.raycastTarget = false;
        if (config.font != null) arrowTxt.font = config.font;

        return valueTxt;
    }

    private static GameObject CreateDropdownPanel(Transform parent, DropdownConfig config,
        TextMeshProUGUI valueTxt, System.Action<int, string> onValueChanged)
    {
        float optionHeight = config.OptionHeight;

        // Panel container
        GameObject panel = new GameObject("DropdownPanel");
        panel.transform.SetParent(parent, false);
        RectTransform panelRT = panel.AddComponent<RectTransform>();

        int visibleCount = Mathf.Min(config.options.Count, config.maxVisibleOptions);
        float panelHeight = visibleCount * optionHeight + 20f;

        panelRT.anchorMin = new Vector2(0f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(0.5f, 1f);
        panelRT.anchoredPosition = new Vector2(0, -5f);
        panelRT.sizeDelta = new Vector2(0, panelHeight);

        // Panel background
        Image panelBg = panel.AddComponent<Image>();
        panelBg.sprite = GetPixelSprite();

        float aspect = config.width / panelHeight;
        Color col = config.themeColor;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_CornerRadius", 0.06f);
            mat.SetFloat("_EdgePadding", 0.04f);
            mat.SetFloat("_Aspect", aspect);
            mat.SetColor("_ColorA", new Color(col.r * 0.3f, col.g * 0.3f, col.b * 0.3f, 0.95f));
            mat.SetColor("_ColorB", new Color(col.r * 0.1f, col.g * 0.1f, col.b * 0.1f, 0.9f));
            mat.SetFloat("_GlassAlpha", 0.9f);
            panelBg.material = mat;
            panelBg.color = Color.white;
        }
        else
        {
            panelBg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        }

        // BoxCollider cho VR raycast
        BoxCollider panelCol = panel.AddComponent<BoxCollider>();
        panelCol.size = new Vector3(config.width, panelHeight, 0.1f);
        panelCol.center = new Vector3(0, -panelHeight / 2f, -0.1f);

        int vrLayer = LayerMask.NameToLayer(config.layerName);
        if (vrLayer != -1) panel.layer = vrLayer;

        // Scroll View
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(panel.transform, false);
        RectTransform viewportRT = viewport.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = new Vector2(10, 10);
        viewportRT.offsetMax = new Vector2(-10, -10);

        Image viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = Color.clear;
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Options container
        GameObject optionsContainer = new GameObject("Options");
        optionsContainer.transform.SetParent(viewport.transform, false);
        RectTransform optionsRT = optionsContainer.AddComponent<RectTransform>();
        optionsRT.anchorMin = new Vector2(0, 1);
        optionsRT.anchorMax = new Vector2(1, 1);
        optionsRT.pivot = new Vector2(0.5f, 1);
        optionsRT.anchoredPosition = Vector2.zero;
        optionsRT.sizeDelta = new Vector2(0, config.options.Count * optionHeight);

        VerticalLayoutGroup vlg = optionsContainer.AddComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.spacing = 2;

        // Tạo options
        VRDropdown dropdownComp = parent.GetComponentInParent<VRDropdown>();
        for (int i = 0; i < config.options.Count; i++)
        {
            CreateOptionItem(optionsContainer.transform, config, i, valueTxt, panel, onValueChanged, dropdownComp);
        }

        // ScrollRect (nếu nhiều options)
        if (config.options.Count > config.maxVisibleOptions)
        {
            ScrollRect scrollRect = panel.AddComponent<ScrollRect>();
            scrollRect.content = optionsRT;
            scrollRect.viewport = viewportRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
        }

        return panel;
    }

    private static void CreateOptionItem(Transform parent, DropdownConfig config, int index,
        TextMeshProUGUI valueTxt, GameObject panel, System.Action<int, string> onValueChanged,
        VRDropdown dropdownComponent)
    {
        string optionText = config.options[index];
        bool isSelected = index == config.defaultIndex;
        Sprite optionIcon = (config.optionIcons != null && index < config.optionIcons.Count)
            ? config.optionIcons[index] : config.icon;

        float optionHeight = config.OptionHeight;
        float optionIconSize = config.OptionIconSize;

        GameObject option = new GameObject("Option_" + index);
        option.transform.SetParent(parent, false);

        RectTransform optRT = option.AddComponent<RectTransform>();
        optRT.sizeDelta = new Vector2(0, optionHeight);

        // Background for hover effect
        Image optBg = option.AddComponent<Image>();
        optBg.color = isSelected ? new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.2f) : Color.clear;

        // Button
        Button optBtn = option.AddComponent<Button>();
        optBtn.targetGraphic = optBg;

        ColorBlock colors = optBtn.colors;
        colors.normalColor = isSelected ? new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.2f) : Color.clear;
        colors.highlightedColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.35f);
        colors.pressedColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.5f);
        colors.selectedColor = colors.highlightedColor;
        optBtn.colors = colors;

        // BoxCollider cho VR raycast
        BoxCollider optCol = option.AddComponent<BoxCollider>();
        optCol.size = new Vector3(config.width - 20f, optionHeight, 0.1f);
        optCol.center = new Vector3(0, 0, -0.1f);

        int vrLayer = LayerMask.NameToLayer(config.layerName);
        if (vrLayer != -1) option.layer = vrLayer;

        // Layout: Checkmark | Icon | Text
        float checkmarkWidth = 40f;
        float iconWidth = optionIcon != null ? optionIconSize + 15f : 0f;
        float textStartX = checkmarkWidth + iconWidth;

        // Checkmark
        GameObject checkObj = new GameObject("Checkmark");
        checkObj.transform.SetParent(option.transform, false);
        RectTransform checkRT = checkObj.AddComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0f, 0f);
        checkRT.anchorMax = new Vector2(0f, 1f);
        checkRT.pivot = new Vector2(0f, 0.5f);
        checkRT.anchoredPosition = new Vector2(10f, 0f);
        checkRT.sizeDelta = new Vector2(24f, 24f);

        TextMeshProUGUI checkTxt = checkObj.AddComponent<TextMeshProUGUI>();
        checkTxt.text = "✓";
        checkTxt.fontSize = config.valueFontSize * 0.8f;
        checkTxt.color = isSelected ? config.themeColor : Color.clear;
        checkTxt.alignment = TextAlignmentOptions.Center;
        checkTxt.raycastTarget = false;
        if (config.font != null) checkTxt.font = config.font;

        // Icon
        if (optionIcon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(option.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0f, 0.5f);
            iconRT.anchorMax = new Vector2(0f, 0.5f);
            iconRT.pivot = new Vector2(0f, 0.5f);
            iconRT.anchoredPosition = new Vector2(checkmarkWidth, 0f);
            iconRT.sizeDelta = new Vector2(optionIconSize, optionIconSize);

            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = optionIcon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.color = Color.Lerp(config.themeColor, Color.white, 0.85f);
        }

        // Text
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(option.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(textStartX, 0f);
        txtRT.offsetMax = new Vector2(-10f, 0f);

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = optionText;
        txt.fontSize = config.valueFontSize * 0.8f;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Left;
        txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        txt.raycastTarget = false;
        if (config.font != null) txt.font = config.font;

        // Click handler
        int capturedIndex = index;
        optBtn.onClick.AddListener(() =>
        {
            valueTxt.text = optionText;
            panel.SetActive(false);

            if (dropdownComponent != null)
            {
                dropdownComponent.UpdateSelection(capturedIndex);
            }

            onValueChanged?.Invoke(capturedIndex, optionText);
        });

        // Register option
        if (dropdownComponent != null)
        {
            dropdownComponent.RegisterOption(index, optBg, checkTxt, config.themeColor);
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
}

/// <summary>
/// Component quản lý state của VR Dropdown
/// </summary>
public class VRDropdown : MonoBehaviour
{
    private List<string> _options;
    private int _selectedIndex;
    private TextMeshProUGUI _valueTxt;
    private GameObject _dropdownPanel;
    private System.Action<int, string> _onValueChanged;

    private class OptionRef
    {
        public Image background;
        public TextMeshProUGUI checkmark;
        public Color themeColor;
    }
    private Dictionary<int, OptionRef> _optionRefs = new Dictionary<int, OptionRef>();

    public int SelectedIndex => _selectedIndex;
    public string SelectedValue => _options != null && _selectedIndex >= 0 && _selectedIndex < _options.Count
        ? _options[_selectedIndex] : "";

    public void Initialize(List<string> options, int defaultIndex, TextMeshProUGUI valueTxt,
        GameObject dropdownPanel, System.Action<int, string> onValueChanged)
    {
        _options = options;
        _selectedIndex = defaultIndex;
        _valueTxt = valueTxt;
        _dropdownPanel = dropdownPanel;
        _onValueChanged = onValueChanged;
    }

    public void RegisterOption(int index, Image background, TextMeshProUGUI checkmark, Color themeColor)
    {
        _optionRefs[index] = new OptionRef
        {
            background = background,
            checkmark = checkmark,
            themeColor = themeColor
        };
    }

    public void UpdateSelection(int newIndex)
    {
        foreach (var kvp in _optionRefs)
        {
            if (kvp.Value.background != null)
            {
                kvp.Value.background.color = Color.clear;
                var btn = kvp.Value.background.GetComponent<Button>();
                if (btn != null)
                {
                    var colors = btn.colors;
                    colors.normalColor = Color.clear;
                    btn.colors = colors;
                }
            }
            if (kvp.Value.checkmark != null)
            {
                kvp.Value.checkmark.color = Color.clear;
            }
        }

        _selectedIndex = newIndex;
        if (_optionRefs.TryGetValue(newIndex, out var optRef))
        {
            Color selectedBgColor = new Color(optRef.themeColor.r, optRef.themeColor.g, optRef.themeColor.b, 0.2f);
            if (optRef.background != null)
            {
                optRef.background.color = selectedBgColor;
                var btn = optRef.background.GetComponent<Button>();
                if (btn != null)
                {
                    var colors = btn.colors;
                    colors.normalColor = selectedBgColor;
                    btn.colors = colors;
                }
            }
            if (optRef.checkmark != null)
            {
                optRef.checkmark.color = optRef.themeColor;
            }
        }
    }

    public void SetSelectedIndex(int index)
    {
        if (_options == null || index < 0 || index >= _options.Count) return;

        UpdateSelection(index);
        if (_valueTxt != null)
        {
            _valueTxt.text = _options[index];
        }
        _onValueChanged?.Invoke(index, _options[index]);
    }

    public void SetOptions(List<string> options, int selectedIndex = 0)
    {
        _options = options;
        SetSelectedIndex(selectedIndex);
    }

    public void CloseDropdown()
    {
        if (_dropdownPanel != null)
        {
            _dropdownPanel.SetActive(false);
        }
    }

    public void OpenDropdown()
    {
        if (_dropdownPanel != null)
        {
            _dropdownPanel.SetActive(true);
        }
    }

    public void ToggleDropdown()
    {
        if (_dropdownPanel != null)
        {
            _dropdownPanel.SetActive(!_dropdownPanel.activeSelf);
        }
    }

    private void OnDisable()
    {
        CloseDropdown();
    }
}
