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
    private static Sprite _arrowSprite;
    private static Sprite _checkmarkSprite;

    // Hằng số layout
    private const float FONT_TO_BOX_RATIO = 4.2f;      // Tỷ lệ font size -> box height (tăng từ 3.5)
    private const float FONT_TO_ICON_RATIO = 2.5f;     // Tỷ lệ font size -> icon size (tăng từ 1.5)
    private const float ICON_ZONE_RATIO = 0.28f;       // 28% cho icon zone (giảm để icon to hơn trong zone)
    private const float CONTENT_PADDING = 12f;          // Padding cho content zone
    private const float ARROW_WIDTH = 70f;              // Chiều rộng arrow
    private const float CONTENT_LEFT_OFFSET = 0.05f;    // 5% lùi content sang phải
    private const float ARROW_RIGHT_OFFSET = 0.05f;     // 5% lùi arrow sang trái

    /// <summary>
    /// Cấu hình cho Dropdown
    /// </summary>
    [System.Serializable]
    public class DropdownConfig
    {
        public string label = "Label";
        public Sprite icon;
        public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
        public float width = 300f;
        public int labelFontSize = 32;
        public int valueFontSize = 36;
        public TMP_FontAsset font;

        // Options
        public List<string> options = new List<string>();
        public List<Sprite> optionIcons = new List<Sprite>();
        public List<float> optionIconSizeMultipliers = new List<float>(); // Multiplier for each option's icon size
        public int defaultIndex = 0;

        // Visual settings
        public float cornerRadius = 0.12f;
        public float edgePadding = 0.12f;
        public float backgroundAlpha = 0.08f;
        public float borderWidth = 0.04f;
        public float glowWidth = 0.04f;
        public float glowIntensity = 2.5f;

        // Glassmorphism settings (uses GlassGradientBackgroundOverlay with higher Queue)
        public bool enableGlassmorphism = true;
        public float blurIntensity = 6;
        public int blurQuality = 4;
        public float glassOpacity = 0f;
        public float tintStrength = 0.1f;
        public float innerGlow = 0f;
        public float brightness = 1f;
        public float saturation = 1f;

        // Animation
        public float popAmount = 0.005f;

        // Dropdown panel
        public int maxVisibleOptions = 5;

        // Layer
        public string layerName = "VirtualObjects";

        // Tính chiều cao box từ font size
        public float BoxHeight => valueFontSize * FONT_TO_BOX_RATIO;

        // Tính icon size từ font size
        public float IconSize => valueFontSize * FONT_TO_ICON_RATIO * 0.95f;

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

        // 3. Visuals - container cho visual elements với expansion (giống VRButtonFactory)
        GameObject visuals = new GameObject("Visuals");
        visuals.transform.SetParent(hitArea.transform, false);
        RectTransform visRT = visuals.AddComponent<RectTransform>();
        float expansion = config.edgePadding;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
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

        // 12. Toggle dropdown on click (use ToggleDropdown to handle force hover)
        btn.onClick.AddListener(() =>
        {
            dropdown.ToggleDropdown();
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
    /// Tạo Dropdown với icon và optionIcons riêng cho từng option
    /// Chiều cao tự động tính từ font size
    /// </summary>
    public static GameObject CreateIconDropdownWithOptionIcons(Transform parent, float width,
        string label, Sprite icon, Color color, List<string> options, List<Sprite> optionIcons, int defaultIndex,
        System.Action<int, string> onValueChanged = null,
        int labelFontSize = 24, int valueFontSize = 36, TMP_FontAsset font = null,
        List<float> optionIconSizeMultipliers = null)
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
            optionIcons = optionIcons,
            optionIconSizeMultipliers = optionIconSizeMultipliers ?? new List<float>(),
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

        // Use GlassGradientBackground with Glassmorphism (like VRMenuFrame)
        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_CornerRadius", config.cornerRadius);
            mat.SetFloat("_EdgePadding", config.edgePadding);
            mat.SetFloat("_Aspect", aspect);

            // Gradient colors based on theme color
            Color colorA = new Color(col.r * 0.8f, col.g * 0.9f, col.b, config.backgroundAlpha * 1.5f);
            Color colorB = new Color(col.r, col.g * 0.7f, col.b * 0.9f, config.backgroundAlpha * 1.2f);
            mat.SetColor("_ColorA", colorA);
            mat.SetColor("_ColorB", colorB);
            mat.SetFloat("_GradientOffset", 0f);
            mat.SetFloat("_GradientAngle", -10f);
            mat.SetFloat("_CyanRatio", 0.7f);
            mat.SetFloat("_GlassAlpha", config.backgroundAlpha);
            mat.SetFloat("_FresnelPower", 2.2f);
            mat.SetFloat("_FresnelStrength", 0.12f);

            // Glassmorphism settings (like VRMenuFrame)
            mat.SetFloat("_BlurEnabled", 0f);
            // mat.SetFloat("_BlurRadius", config.blurIntensity);
            // mat.SetFloat("_BlurIterations", config.blurQuality);
            // mat.SetFloat("_GlassOpacity", config.glassOpacity);
            // mat.SetFloat("_TintStrength", config.tintStrength);
            // mat.SetFloat("_InnerGlow", config.innerGlow);
            // mat.SetFloat("_Brightness", config.brightness);
            // mat.SetFloat("_Saturation", config.saturation);

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
            mat.SetFloat("_EdgePadding", config.edgePadding);
            mat.SetFloat("_CornerRadius", config.cornerRadius);

            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            mat.SetColor("_GlowColor", borderGlowCol);

            mat.SetFloat("_BorderWidth", config.borderWidth);
            mat.SetFloat("_GlowWidth", config.glowWidth);
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

        // Compensate for Visuals expansion để Content nằm đúng vị trí HitArea gốc
        float e = config.edgePadding;
        float totalSize = 1f + 2f * e; // Visuals size ratio
        float normalizedMin = e / totalSize;
        float normalizedMax = (1f + e) / totalSize;
        cRT.anchorMin = new Vector2(normalizedMin, normalizedMin);
        cRT.anchorMax = new Vector2(normalizedMax, normalizedMax);
        cRT.offsetMin = Vector2.zero;
        cRT.offsetMax = Vector2.zero;

        bool hasIcon = config.icon != null;
        float iconSize = config.IconSize;

        // Layout: 1/3 trái cho icon, 2/3 phải cho content
        float iconZoneRatio = hasIcon ? ICON_ZONE_RATIO : 0f;

        // ==================== KHU VỰC ICON (1/3 bên trái) ====================
        if (hasIcon)
        {
            // Icon zone container - lùi sang phải 10%
            GameObject iconZone = new GameObject("IconZone");
            iconZone.transform.SetParent(content.transform, false);
            RectTransform iconZoneRT = iconZone.AddComponent<RectTransform>();
            iconZoneRT.anchorMin = new Vector2(CONTENT_LEFT_OFFSET, 0f);
            iconZoneRT.anchorMax = new Vector2(iconZoneRatio + CONTENT_LEFT_OFFSET, 1f);
            iconZoneRT.offsetMin = Vector2.zero;
            iconZoneRT.offsetMax = Vector2.zero;

            // Icon đặt ở chính giữa khu vực icon
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(iconZone.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.5f, 0.5f);
            iconRT.anchorMax = new Vector2(0.5f, 0.5f);
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.anchoredPosition = new Vector2(0, iconSize * 0.06f);
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

        // ==================== KHU VỰC CONTENT (2/3 bên phải) - lùi sang phải 10% ====================
        GameObject contentZone = new GameObject("ContentZone");
        contentZone.transform.SetParent(content.transform, false);
        RectTransform contentZoneRT = contentZone.AddComponent<RectTransform>();
        contentZoneRT.anchorMin = new Vector2(iconZoneRatio + CONTENT_LEFT_OFFSET, 0f);
        contentZoneRT.anchorMax = new Vector2(1f - ARROW_RIGHT_OFFSET, 1f);
        contentZoneRT.offsetMin = new Vector2(CONTENT_PADDING, 0f);
        contentZoneRT.offsetMax = new Vector2(-CONTENT_PADDING, 0f);

        // Nửa trên: Title (Label) - đẩy lên trên để cách xa value
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(contentZone.transform, false);
        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0.55f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(0f, 0f);
        labelRT.offsetMax = new Vector2(0f, -8f);

        TextMeshProUGUI labelTxt = labelObj.AddComponent<TextMeshProUGUI>();
        labelTxt.text = config.label;
        labelTxt.fontSize = config.labelFontSize;
        labelTxt.color = new Color(1f, 1f, 1f, 1f);
        labelTxt.alignment = TextAlignmentOptions.BottomLeft;
        labelTxt.verticalAlignment = VerticalAlignmentOptions.Bottom;
        labelTxt.fontStyle = FontStyles.Bold;
        labelTxt.raycastTarget = false;
        labelTxt.enableWordWrapping = false;
        labelTxt.overflowMode = TextOverflowModes.Ellipsis;
        if (config.font != null) labelTxt.font = config.font;

        // Nửa dưới: Container cho Value + Arrow - đẩy xuống để cách xa label
        GameObject bottomRow = new GameObject("BottomRow");
        bottomRow.transform.SetParent(contentZone.transform, false);
        RectTransform bottomRowRT = bottomRow.AddComponent<RectTransform>();
        bottomRowRT.anchorMin = Vector2.zero;
        bottomRowRT.anchorMax = new Vector2(1f, 0.45f);
        bottomRowRT.offsetMin = new Vector2(0f, 8f);
        bottomRowRT.offsetMax = new Vector2(0f, 0f);

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
        // valueTxt.fontStyle = FontStyles.Bold;
        valueTxt.alignment = TextAlignmentOptions.Left;
        valueTxt.verticalAlignment = VerticalAlignmentOptions.Top;
        valueTxt.raycastTarget = false;
        valueTxt.enableWordWrapping = false;
        valueTxt.overflowMode = TextOverflowModes.Ellipsis;
        if (config.font != null) valueTxt.font = config.font;

        // Arrow indicator - cố định ở cạnh phải, sát cạnh trên của BottomRow
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(bottomRow.transform, false);
        RectTransform arrowRT = arrowObj.AddComponent<RectTransform>();
        arrowRT.anchorMin = new Vector2(1f, 1f);
        arrowRT.anchorMax = new Vector2(1f, 1f);
        arrowRT.pivot = new Vector2(1f, 1f);
        arrowRT.anchoredPosition = Vector2.zero;
        arrowRT.sizeDelta = new Vector2(ARROW_WIDTH, 0f);

        // Sử dụng arrow sprite màu trắng được generate để có thể tint hoàn toàn
        float arrowSize = config.valueFontSize;

        Image arrowImg = arrowObj.AddComponent<Image>();
        arrowImg.sprite = GetArrowSprite();
        arrowImg.preserveAspect = true;
        arrowImg.raycastTarget = false;

        // Màu arrow = màu border (theme color lerp với white để sáng và nổi bật)
        Color arrowColor = Color.Lerp(config.themeColor, Color.white, 0.85f);
        arrowImg.color = arrowColor;

        // Set kích thước và vị trí arrow (pivot ở góc trên phải)
        arrowRT.sizeDelta = new Vector2(arrowSize, arrowSize);
        arrowRT.anchoredPosition = new Vector2(-ARROW_WIDTH / 2f + arrowSize / 2f, 0f);

        // Glow effect để nổi bật khỏi nền
        Color arrowGlowCol = Color.Lerp(config.themeColor, Color.white, 0.6f);
        arrowGlowCol.a = 0.4f;
        float arrowGlowDist = 2f;

        Shadow arrowShadow1 = arrowObj.AddComponent<Shadow>();
        arrowShadow1.effectColor = arrowGlowCol;
        arrowShadow1.effectDistance = new Vector2(arrowGlowDist, -arrowGlowDist);

        Shadow arrowShadow2 = arrowObj.AddComponent<Shadow>();
        arrowShadow2.effectColor = arrowGlowCol;
        arrowShadow2.effectDistance = new Vector2(-arrowGlowDist, arrowGlowDist);

        // Bloom layer để thêm độ sáng
        Color arrowBloomCol = config.themeColor;
        arrowBloomCol.a = 0.2f;
        float arrowBloomDist = 4f;

        Shadow arrowShadow3 = arrowObj.AddComponent<Shadow>();
        arrowShadow3.effectColor = arrowBloomCol;
        arrowShadow3.effectDistance = new Vector2(arrowBloomDist, -arrowBloomDist);

        Shadow arrowShadow4 = arrowObj.AddComponent<Shadow>();
        arrowShadow4.effectColor = arrowBloomCol;
        arrowShadow4.effectDistance = new Vector2(-arrowBloomDist, arrowBloomDist);

        return valueTxt;
    }

    private static GameObject CreateDropdownPanel(Transform parent, DropdownConfig config,
        TextMeshProUGUI valueTxt, System.Action<int, string> onValueChanged)
    {
        float optionHeight = config.OptionHeight;

        // Panel container - NO background here, background goes to Viewport
        GameObject panel = new GameObject("DropdownPanel");
        panel.transform.SetParent(parent, false);
        RectTransform panelRT = panel.AddComponent<RectTransform>();

        int visibleCount = Mathf.Min(config.options.Count, config.maxVisibleOptions);
        float panelHeight = visibleCount * optionHeight + 20f;

        panelRT.anchorMin = new Vector2(0f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(0.5f, 1f);
        panelRT.anchoredPosition = new Vector2(0, -25f);
        panelRT.sizeDelta = new Vector2(0, panelHeight);

        // Add Canvas FIRST to handle sorting without breaking VR raycast
        Canvas panelCanvas = panel.AddComponent<Canvas>();
        panelCanvas.overrideSorting = true;
        panelCanvas.sortingOrder = 100;
        // Add GraphicRaycaster for UI events (works alongside BoxCollider for VR)
        GraphicRaycaster raycaster = panel.AddComponent<GraphicRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        // NO Image on panel - background is now on Viewport

        // BoxCollider cho VR raycast
        BoxCollider panelCol = panel.AddComponent<BoxCollider>();
        panelCol.size = new Vector3(config.width, panelHeight, 0.1f);
        panelCol.center = new Vector3(0, -panelHeight / 2f, -0.05f);

        int vrLayer = LayerMask.NameToLayer(config.layerName);
        if (vrLayer != -1) panel.layer = vrLayer;

        // Viewport - contains everything
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(panel.transform, false);
        if (vrLayer != -1) viewport.layer = vrLayer;
        RectTransform viewportRT = viewport.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;

        // Visuals container - expanded beyond Viewport for border effect
        // Scale expansion based on height ratio to maintain visual consistency
        float heightRatio = config.BoxHeight / panelHeight;
        float adjustedExpansion = config.edgePadding * heightRatio;

        GameObject viewportVisuals = new GameObject("Visuals");
        viewportVisuals.transform.SetParent(viewport.transform, false);
        RectTransform viewportVisualsRT = viewportVisuals.AddComponent<RectTransform>();
        viewportVisualsRT.anchorMin = new Vector2(-adjustedExpansion, -adjustedExpansion);
        viewportVisualsRT.anchorMax = new Vector2(1f + adjustedExpansion, 1f + adjustedExpansion);
        viewportVisualsRT.offsetMin = Vector2.zero;
        viewportVisualsRT.offsetMax = Vector2.zero;

        // Background for Visuals
        CreateViewportBackground(viewportVisuals.transform, config, panelHeight);

        // Border for Visuals
        CreateViewportBorder(viewportVisuals.transform, config, panelHeight);

        // Content container - NOW INSIDE VISUALS for easier HoverBorder calculation
        // Uses nested Canvas with higher sorting order so content renders AFTER glassmorphism background
        GameObject viewportContent = new GameObject("Content");
        viewportContent.transform.SetParent(viewportVisuals.transform, false);
        RectTransform viewportContentRT = viewportContent.AddComponent<RectTransform>();
        viewportContentRT.anchorMin = Vector2.zero;
        viewportContentRT.anchorMax = Vector2.one;
        // Padding compensates for expansion so content stays within original Viewport area
        float expansionPixelsX = adjustedExpansion * config.width;
        float expansionPixelsY = adjustedExpansion * panelHeight;
        viewportContentRT.offsetMin = new Vector2(expansionPixelsX + 10, expansionPixelsY + 10);
        viewportContentRT.offsetMax = new Vector2(-expansionPixelsX - 10, -expansionPixelsY - 10);

        // Nested Canvas to ensure content renders AFTER the glassmorphism GrabPass
        Canvas contentCanvas = viewportContent.AddComponent<Canvas>();
        contentCanvas.overrideSorting = true;
        contentCanvas.sortingOrder = 110; // Higher than panel's 100, after Overlay shader Queue
        viewportContent.AddComponent<GraphicRaycaster>();

        // Use RectMask2D on Content for scrolling
        RectMask2D rectMask = viewportContent.AddComponent<RectMask2D>();
        rectMask.padding = Vector4.zero;

        // Options container - inside Content (which is inside Visuals)
        GameObject optionsContainer = new GameObject("Options");
        optionsContainer.transform.SetParent(viewportContent.transform, false);
        if (vrLayer != -1) optionsContainer.layer = vrLayer;
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
        vlg.childControlHeight = true;
        vlg.spacing = 2;
        vlg.padding = new RectOffset(0, 0, 0, 0);

        // Add ContentSizeFitter to ensure proper sizing
        ContentSizeFitter fitter = optionsContainer.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Tạo options
        VRDropdown dropdownComp = parent.GetComponentInParent<VRDropdown>();
        for (int i = 0; i < config.options.Count; i++)
        {
            CreateOptionItem(optionsContainer.transform, config, i, valueTxt, panel, onValueChanged, dropdownComp, panelHeight, adjustedExpansion);
        }

        // ScrollRect (nếu nhiều options)
        if (config.options.Count > config.maxVisibleOptions)
        {
            ScrollRect scrollRect = panel.AddComponent<ScrollRect>();
            scrollRect.content = optionsRT;
            scrollRect.viewport = viewportContentRT;  // Use viewportContent as scroll viewport
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
        }

        return panel;
    }

    /// <summary>
    /// Tạo background cho Viewport giống với Dropdown
    /// Điều chỉnh cornerRadius và edgePadding theo tỷ lệ chiều cao để visual giống nhau
    /// </summary>
    private static void CreateViewportBackground(Transform parent, DropdownConfig config, float panelHeight)
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

        float aspect = config.width / panelHeight;
        Color col = config.themeColor;

        // Scale parameters based on height ratio to maintain visual consistency
        float heightRatio = config.BoxHeight / panelHeight;
        float adjustedCornerRadius = config.cornerRadius * heightRatio;
        float adjustedEdgePadding = config.edgePadding * heightRatio;

        // Use GlassGradientBackgroundOverlay for dropdown panel (higher render queue)
        // This ensures GrabPass captures VRMenuFrame and other UI behind it
        Shader overlayShader = Shader.Find("Custom/GlassGradientBackgroundOverlay");
        if (overlayShader != null)
        {
            Material mat = new Material(overlayShader);
            mat.SetFloat("_CornerRadius", adjustedCornerRadius);
            mat.SetFloat("_EdgePadding", adjustedEdgePadding);
            mat.SetFloat("_Aspect", aspect);

            // Gradient colors based on theme color
            Color colorA = new Color(col.r * 0.8f, col.g * 0.9f, col.b, config.backgroundAlpha * 1.8f);
            Color colorB = new Color(col.r, col.g * 0.7f, col.b * 0.9f, config.backgroundAlpha * 1.5f);
            mat.SetColor("_ColorA", colorA);
            mat.SetColor("_ColorB", colorB);
            mat.SetFloat("_GradientOffset", 0f);
            mat.SetFloat("_GradientAngle", -10f);
            mat.SetFloat("_CyanRatio", 0.7f);
            mat.SetFloat("_GlassAlpha", config.backgroundAlpha * 1.2f);
            mat.SetFloat("_FresnelPower", 2.2f);
            mat.SetFloat("_FresnelStrength", 0.12f);

            // Glassmorphism settings
            mat.SetFloat("_BlurEnabled", config.enableGlassmorphism ? 1f : 0f);
            mat.SetFloat("_BlurRadius", config.blurIntensity);
            mat.SetFloat("_BlurIterations", config.blurQuality);
            mat.SetFloat("_GlassOpacity", config.glassOpacity);
            mat.SetFloat("_TintStrength", config.tintStrength);
            mat.SetFloat("_InnerGlow", config.innerGlow);
            mat.SetFloat("_Brightness", config.brightness);
            mat.SetFloat("_Saturation", config.saturation);

            img.material = mat;
            img.color = Color.white;
        }
        else
        {
            // Fallback to regular shader
            Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
            if (glassShader != null)
            {
                Material mat = new Material(glassShader);
                mat.SetFloat("_CornerRadius", adjustedCornerRadius);
                mat.SetFloat("_EdgePadding", adjustedEdgePadding);
                mat.SetFloat("_Aspect", aspect);
                mat.SetColor("_ColorA", new Color(col.r, col.g, col.b, config.backgroundAlpha * 1.5f));
                mat.SetColor("_ColorB", new Color(col.r, col.g, col.b, config.backgroundAlpha));
                mat.SetFloat("_GlassAlpha", config.backgroundAlpha);
                mat.SetFloat("_BlurEnabled", 0f);
                img.material = mat;
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(col.r, col.g, col.b, config.backgroundAlpha);
            }
        }
    }

    /// <summary>
    /// Tạo border cho Viewport giống với Dropdown
    /// Điều chỉnh cornerRadius, borderWidth, glowWidth theo tỷ lệ chiều cao để visual giống nhau
    /// </summary>
    private static void CreateViewportBorder(Transform parent, DropdownConfig config, float panelHeight)
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

        float aspect = config.width / panelHeight;
        Color col = config.themeColor;

        // Scale parameters based on height ratio to maintain visual consistency
        float heightRatio = config.BoxHeight / panelHeight;
        float adjustedCornerRadius = config.cornerRadius * heightRatio;
        float adjustedEdgePadding = config.edgePadding * heightRatio;
        float adjustedBorderWidth = config.borderWidth * heightRatio;
        float adjustedGlowWidth = config.glowWidth * heightRatio;

        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material mat = new Material(glowShader);
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_EdgePadding", adjustedEdgePadding);
            mat.SetFloat("_CornerRadius", adjustedCornerRadius);

            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            mat.SetColor("_GlowColor", borderGlowCol);

            mat.SetFloat("_BorderWidth", adjustedBorderWidth);
            mat.SetFloat("_GlowWidth", adjustedGlowWidth);
            mat.SetFloat("_GlowIntensity", config.glowIntensity);
            mat.SetFloat("_PulseEnabled", 0f);

            img.material = mat;
        }
    }

    private static void CreateOptionItem(Transform parent, DropdownConfig config, int index,
        TextMeshProUGUI valueTxt, GameObject panel, System.Action<int, string> onValueChanged,
        VRDropdown dropdownComponent, float panelHeight, float adjustedExpansion)
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

        // Add LayoutElement for proper sizing in VerticalLayoutGroup
        LayoutElement layoutElement = option.AddComponent<LayoutElement>();
        layoutElement.minHeight = optionHeight;
        layoutElement.preferredHeight = optionHeight;
        layoutElement.flexibleWidth = 1f;

        // Background for hover effect (now more subtle, border handles hover visual)
        Image optBg = option.AddComponent<Image>();
        optBg.color = isSelected ? new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.15f) : Color.clear;

        // Button
        Button optBtn = option.AddComponent<Button>();
        optBtn.targetGraphic = optBg;

        // More subtle color transitions since we have glowing border
        ColorBlock colors = optBtn.colors;
        colors.normalColor = isSelected ? new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.15f) : Color.clear;
        colors.highlightedColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.25f);
        colors.pressedColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.4f);
        colors.selectedColor = colors.highlightedColor;
        optBtn.colors = colors;

        // === GLOWING BORDER for hover effect ===
        // Create border container that matches panel border edges exactly
        //
        // Layout hierarchy: Panel > Viewport > Visuals (expanded) > Content (10px padding) > Options > Option
        // Border needs to span the full Visuals width regardless of Option size
        //
        // Visuals width = config.width * (1 + 2*adjustedExpansion)
        // We use center anchor and set fixed size to match Visuals exactly

        // Border dimensions should exactly match Visuals dimensions horizontally
        // Visuals width = config.width * (1 + 2*adjustedExpansion)
        float visualsWidth = config.width * (1f + 2f * adjustedExpansion);
        float borderWidth = visualsWidth;

        // Calculate border height based on option's aspect ratio
        // Option width is approximately config.width (fills Content container)
        // Border height should maintain the same width:height ratio expansion
        // borderHeight / optionHeight = borderWidth / optionWidth
        float optionWidth = config.width;
        float borderHeight = borderWidth * optionHeight / optionWidth;

        // First option needs extra 5px on top to align with panel border
        float firstOptionExtraTop = (index == 0) ? 5f : 0f;
        float adjustedBorderHeight = borderHeight + firstOptionExtraTop;

        GameObject borderObj = new GameObject("HoverBorder");
        borderObj.transform.SetParent(option.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();

        // Use center anchor with fixed size matching Visuals
        // Position at center of Option (which should align with center of panel)
        borderRT.anchorMin = new Vector2(0.5f, 0.5f);
        borderRT.anchorMax = new Vector2(0.5f, 0.5f);
        borderRT.pivot = new Vector2(0.5f, 0.5f);
        borderRT.sizeDelta = new Vector2(borderWidth, adjustedBorderHeight);

        // Shift border up by half of extra height so the extra is on top
        float yOffset = firstOptionExtraTop / 2f;
        borderRT.anchoredPosition = new Vector2(0f, yOffset);

        // Border image with GlowingGlassBorder shader
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetPixelSprite();
        borderImg.raycastTarget = false;
        borderImg.color = Color.clear; // Start invisible, VROptionHoverEffect will control alpha

        // Add VROptionHoverEffect component for hover animation
        // Pass config values for consistent border styling with panel
        VROptionHoverEffect hoverEffect = option.AddComponent<VROptionHoverEffect>();
        hoverEffect.borderImage = borderImg;
        hoverEffect.glowColor = config.themeColor;

        // Calculate shader parameters to match panel border visually
        // Panel border uses parameters scaled by heightRatio
        float heightRatio = config.BoxHeight / panelHeight;
        float adjustedEdgePadding = config.edgePadding * heightRatio;

        // CRITICAL: Use SAME edgePadding as panel for horizontal alignment
        // In shader, edge position = 0.5 - padding (independent of aspect)
        // So same edgePadding = same horizontal edge position
        hoverEffect.edgePadding = adjustedEdgePadding;

        // For cornerRadius, borderWidth, glowWidth - scale based on height ratio
        // to maintain proportional appearance for the shorter option height
        float optionHeightRatio = optionHeight / panelHeight;
        hoverEffect.cornerRadius = config.cornerRadius * heightRatio / optionHeightRatio;
        hoverEffect.borderWidth = config.borderWidth * heightRatio / optionHeightRatio;
        hoverEffect.glowWidth = config.glowWidth * heightRatio / optionHeightRatio;
        hoverEffect.glowIntensity = config.glowIntensity;

        // Clamp cornerRadius to prevent visual issues with short options
        float maxCornerRadius = 0.4f; // Max 40% of height
        hoverEffect.cornerRadius = Mathf.Min(hoverEffect.cornerRadius, maxCornerRadius);

        // If selected, show subtle border
        if (isSelected)
        {
            hoverEffect.SetSelected(true);
        }

        // BoxCollider cho VR raycast
        BoxCollider optCol = option.AddComponent<BoxCollider>();
        optCol.size = new Vector3(config.width - 20f, optionHeight, 0.1f);
        optCol.center = new Vector3(0, 0, -0.1f);

        int vrLayer = LayerMask.NameToLayer(config.layerName);
        if (vrLayer != -1) option.layer = vrLayer;

        // Layout: Checkmark | Icon | Text
        // Checkmark size first (needed for icon position calculation)
        float checkSize = config.valueFontSize * 0.7f;

        // Calculate positions based on percentage of option width
        float checkmarkX = config.width * 0.05f;  // 5% from left
        float iconX = checkmarkX + config.width * 0.025f + checkSize / 2f;  // Checkmark + 2.5% gap + half checkmark width

        // Calculate textStartX to align with main dropdown's value text position
        // Main value text starts at: (ICON_ZONE_RATIO + CONTENT_LEFT_OFFSET) * width + CONTENT_PADDING
        // But option is inside Content which has ~10px left padding from panel edge
        float mainValueTextX = config.width * (ICON_ZONE_RATIO + CONTENT_LEFT_OFFSET) + CONTENT_PADDING;
        float contentLeftPadding = 10f; // Content has 10px padding from panel edge
        float textStartX = mainValueTextX - contentLeftPadding;

        // Checkmark - sử dụng Image với checkmark sprite
        GameObject checkObj = new GameObject("Checkmark");
        checkObj.transform.SetParent(option.transform, false);
        RectTransform checkRT = checkObj.AddComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0f, 0.5f);
        checkRT.anchorMax = new Vector2(0f, 0.5f);
        checkRT.pivot = new Vector2(0.5f, 0.5f);
        checkRT.anchoredPosition = new Vector2(checkmarkX, 0f);
        checkRT.sizeDelta = new Vector2(checkSize, checkSize);

        Image checkImg = checkObj.AddComponent<Image>();
        checkImg.sprite = GetCheckmarkSprite();
        checkImg.preserveAspect = true;
        checkImg.raycastTarget = false;
        checkImg.color = isSelected ? Color.white : Color.clear; // Full white when selected

        // Glow effect for checkmark when selected
        if (isSelected)
        {
            Color glowCol = Color.Lerp(config.themeColor, Color.white, 0.8f);
            glowCol.a = 0.7f;
            Shadow checkShadow = checkObj.AddComponent<Shadow>();
            checkShadow.effectColor = glowCol;
            checkShadow.effectDistance = new Vector2(2f, -2f);

            // Second shadow for stronger glow
            Shadow checkShadow2 = checkObj.AddComponent<Shadow>();
            checkShadow2.effectColor = new Color(glowCol.r, glowCol.g, glowCol.b, 0.4f);
            checkShadow2.effectDistance = new Vector2(-1.5f, 1.5f);
        }

        // Icon
        if (optionIcon != null)
        {
            // Get size multiplier for this option (default 1.0)
            float iconSizeMultiplier = (config.optionIconSizeMultipliers != null && index < config.optionIconSizeMultipliers.Count)
                ? config.optionIconSizeMultipliers[index] : 1f;
            float actualIconSize = optionIconSize * iconSizeMultiplier;

            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(option.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0f, 0.5f);
            iconRT.anchorMax = new Vector2(0f, 0.5f);
            iconRT.pivot = new Vector2(0f, 0.5f);
            iconRT.anchoredPosition = new Vector2(iconX, 0f);
            iconRT.sizeDelta = new Vector2(actualIconSize, actualIconSize);

            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = optionIcon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            iconImg.color = Color.white; // Full white for maximum visibility

            // Glow effect for icon visibility
            Color iconGlowCol = Color.Lerp(config.themeColor, Color.white, 0.7f);
            iconGlowCol.a = 0.6f;
            Shadow iconShadow = iconObj.AddComponent<Shadow>();
            iconShadow.effectColor = iconGlowCol;
            iconShadow.effectDistance = new Vector2(1.5f, -1.5f);
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
        txt.fontSize = config.valueFontSize * 0.85f;
        txt.color = Color.white;
        txt.fontStyle = FontStyles.Bold; // Bold for better visibility
        txt.alignment = TextAlignmentOptions.Left;
        txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        txt.raycastTarget = false;
        if (config.font != null) txt.font = config.font;

        // Text glow for contrast against blurred background
        Shadow txtShadow = txtObj.AddComponent<Shadow>();
        txtShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        txtShadow.effectDistance = new Vector2(1f, -1f);

        // Click handler
        int capturedIndex = index;
        optBtn.onClick.AddListener(() =>
        {
            valueTxt.text = optionText;

            if (dropdownComponent != null)
            {
                dropdownComponent.UpdateSelection(capturedIndex);
                dropdownComponent.CloseDropdown();  // Use CloseDropdown to release force hover
            }
            else
            {
                panel.SetActive(false);
            }

            onValueChanged?.Invoke(capturedIndex, optionText);
        });

        // Register option with hover effect
        if (dropdownComponent != null)
        {
            dropdownComponent.RegisterOption(index, optBg, checkImg, config.themeColor, hoverEffect);
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

    /// <summary>
    /// Tạo sprite mũi tên xuống màu trắng để có thể tint với bất kỳ màu nào
    /// </summary>
    private static Sprite GetArrowSprite()
    {
        if (_arrowSprite != null) return _arrowSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        // Khởi tạo transparent
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.clear;

        // Vẽ tam giác mũi tên xuống với anti-aliasing
        Vector2 top1 = new Vector2(8, size - 16);      // Góc trái trên
        Vector2 top2 = new Vector2(size - 8, size - 16); // Góc phải trên
        Vector2 bottom = new Vector2(size / 2f, 12);    // Đỉnh dưới

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float alpha = PointInTriangle(p, top1, top2, bottom);
                if (alpha > 0)
                {
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        _arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
        return _arrowSprite;
    }

    /// <summary>
    /// Tính alpha cho điểm trong tam giác với anti-aliasing
    /// </summary>
    private static float PointInTriangle(Vector2 p, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        // Signed area method
        float d1 = Sign(p, v1, v2);
        float d2 = Sign(p, v2, v3);
        float d3 = Sign(p, v3, v1);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        if (!(hasNeg && hasPos))
        {
            // Inside triangle
            return 1f;
        }

        // Anti-aliasing: check distance to edges
        float edgeDist = Mathf.Min(
            DistanceToLine(p, v1, v2),
            Mathf.Min(DistanceToLine(p, v2, v3), DistanceToLine(p, v3, v1))
        );

        if (edgeDist < 1.5f)
        {
            return Mathf.Clamp01(1.5f - edgeDist);
        }

        return 0f;
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    private static float DistanceToLine(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        Vector2 ap = p - a;
        float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab));
        Vector2 closest = a + t * ab;
        return Vector2.Distance(p, closest);
    }

    /// <summary>
    /// Tạo sprite checkmark màu trắng để có thể tint với bất kỳ màu nào
    /// </summary>
    private static Sprite GetCheckmarkSprite()
    {
        if (_checkmarkSprite != null) return _checkmarkSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        // Khởi tạo transparent
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.clear;

        // Vẽ checkmark với 2 đường thẳng tạo thành hình chữ V
        // Điểm bắt đầu (trái trên), điểm giữa (dưới), điểm kết thúc (phải trên)
        Vector2 start = new Vector2(8, size - 24);      // Góc trái trên
        Vector2 mid = new Vector2(24, 12);               // Điểm giữa (đáy)
        Vector2 end = new Vector2(size - 8, size - 12);  // Góc phải trên

        float lineWidth = 7f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                // Khoảng cách đến 2 đường thẳng của checkmark
                float dist1 = DistanceToLine(p, start, mid);
                float dist2 = DistanceToLine(p, mid, end);
                float minDist = Mathf.Min(dist1, dist2);

                // Anti-aliased line
                if (minDist < lineWidth)
                {
                    float alpha = Mathf.Clamp01(1f - (minDist - lineWidth + 1.5f) / 1.5f);
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        _checkmarkSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
        return _checkmarkSprite;
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
    private VRButtonAnimation _buttonAnimation;  // Reference to button animation for hover state

    // Static reference to currently open dropdown (for VRGazeReticle to check)
    public static VRDropdown CurrentlyOpenDropdown { get; private set; }

    private class OptionRef
    {
        public Image background;
        public Image checkmark;
        public Color themeColor;
        public VROptionHoverEffect hoverEffect;
    }
    private Dictionary<int, OptionRef> _optionRefs = new Dictionary<int, OptionRef>();

    public int SelectedIndex => _selectedIndex;
    public GameObject DropdownPanel => _dropdownPanel;
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

        // Find VRButtonAnimation in HitArea child
        var hitArea = transform.Find("HitArea");
        if (hitArea != null)
        {
            _buttonAnimation = hitArea.GetComponent<VRButtonAnimation>();
        }
    }

    public void RegisterOption(int index, Image background, Image checkmark, Color themeColor, VROptionHoverEffect hoverEffect = null)
    {
        _optionRefs[index] = new OptionRef
        {
            background = background,
            checkmark = checkmark,
            themeColor = themeColor,
            hoverEffect = hoverEffect
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
            // Clear selected state for hover effect
            if (kvp.Value.hoverEffect != null)
            {
                kvp.Value.hoverEffect.SetSelected(false);
            }
        }

        _selectedIndex = newIndex;
        if (_optionRefs.TryGetValue(newIndex, out var optRef))
        {
            Color selectedBgColor = new Color(optRef.themeColor.r, optRef.themeColor.g, optRef.themeColor.b, 0.15f);
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
                // Full white for maximum visibility
                optRef.checkmark.color = Color.white;
            }
            // Set selected state for hover effect
            if (optRef.hoverEffect != null)
            {
                optRef.hoverEffect.SetSelected(true);
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
        // Release force hover when panel closes
        if (_buttonAnimation != null)
        {
            _buttonAnimation.SetForceHover(false);
        }
        // Clear static reference
        if (CurrentlyOpenDropdown == this)
        {
            CurrentlyOpenDropdown = null;
        }
    }

    public void OpenDropdown()
    {
        // Close any other open dropdown first
        if (CurrentlyOpenDropdown != null && CurrentlyOpenDropdown != this)
        {
            CurrentlyOpenDropdown.CloseDropdown();
        }

        if (_dropdownPanel != null)
        {
            _dropdownPanel.SetActive(true);
        }
        // Force hover when panel opens
        if (_buttonAnimation != null)
        {
            _buttonAnimation.SetForceHover(true);
        }
        // Set static reference
        CurrentlyOpenDropdown = this;
    }

    public void ToggleDropdown()
    {
        if (_dropdownPanel != null)
        {
            bool willBeActive = !_dropdownPanel.activeSelf;
            if (willBeActive)
            {
                OpenDropdown();
            }
            else
            {
                CloseDropdown();
            }
        }
    }

    /// <summary>
    /// Check if a GameObject is part of this dropdown's panel (option items)
    /// </summary>
    public bool IsPartOfDropdownPanel(GameObject obj)
    {
        if (_dropdownPanel == null || obj == null) return false;

        // Check if obj is a child of the dropdown panel
        Transform current = obj.transform;
        while (current != null)
        {
            if (current.gameObject == _dropdownPanel)
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private void OnDisable()
    {
        CloseDropdown();
    }
}
