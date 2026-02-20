using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Utilities;
using VRWorkspace.UI.Config;

/// <summary>
/// Factory class để tạo VR Input Field với đầy đủ hiệu ứng:
/// - Glass background với gradient
/// - Glowing border với hover effect
/// - Label nhỏ phía trên trái (bên ngoài box)
/// - Input text bên trong box
/// - HoverEffectController cho hover effects (glow, z-pop, cursor change)
/// - BoxCollider cho VR raycast
/// - VRKeyboard integration cho virtual keyboard trong VR
///
/// Chiều cao tự động tính từ font size:
/// - Box height = inputFontSize * 2.2
/// - Total height = box height + label height (nếu có)
/// </summary>
public static class VRInputFieldFactory
{
    // Hằng số layout - use UIConstants values
    private const float FONT_TO_BOX_RATIO = UIConstants.InputFontToBoxRatio;
    private const float LABEL_HEIGHT = UIConstants.InputLabelHeight;
    private const float HORIZONTAL_PADDING = UIConstants.InputHorizontalPadding;
    private const float VERTICAL_PADDING = UIConstants.InputVerticalPadding;

    /// <summary>
    /// Cấu hình cho Input Field
    /// </summary>
    [System.Serializable]
    public class InputFieldConfig
    {
        public string label = "Label";
        public string placeholder = "Enter text...";
        public string defaultValue = "";
        public Color themeColor = UIConstants.DefaultPrimaryColor;
        public float width = 300f;
        public int labelFontSize = UIConstants.InputDefaultLabelFontSize;
        public int inputFontSize = UIConstants.InputDefaultFontSize;
        public TMP_FontAsset font;

        // Input settings
        public TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard;
        public int characterLimit = 0;

        // Visual settings
        public float cornerRadius = UIConstants.ButtonCornerRadius;
        public float edgePadding = UIConstants.ButtonEdgePadding;  // Match Space key value for Android compatibility
        public float backgroundAlpha = UIConstants.ButtonBackgroundAlpha;
        public float borderWidth = UIConstants.InputBorderWidth;
        public float glowWidth = UIConstants.ButtonGlowWidth;
        public float glowIntensity = UIConstants.ButtonGlowIntensity;

        // Animation
        public float popAmount = UIConstants.InputPopAmount;

        // Layer
        public string layerName = UIConstants.VirtualObjectsLayer;

        // Tính chiều cao box từ font size
        public float BoxHeight => inputFontSize * FONT_TO_BOX_RATIO;

        // Tính tổng chiều cao (bao gồm label nếu có)
        public float TotalHeight => BoxHeight + (string.IsNullOrEmpty(label) ? 0f : LABEL_HEIGHT) + 24f;
    }

    /// <summary>
    /// Tạo VR Input Field với đầy đủ hiệu ứng
    /// Label nằm BÊN NGOÀI phía trên input box
    /// Chiều cao tự động tính từ font size
    /// </summary>
    public static GameObject CreateInputField(Transform parent, InputFieldConfig config,
        System.Action<string> onValueChanged = null, System.Action<string> onEndEdit = null)
    {
        bool hasLabel = !string.IsNullOrEmpty(config.label);
        float boxHeight = config.BoxHeight;
        float totalHeight = config.TotalHeight;

        // 1. Wrapper (chứa Label bên ngoài + InputBox)
        GameObject wrapper = new GameObject("Input_" + config.label);
        wrapper.transform.SetParent(parent, false);
        RectTransform wrapperRT = wrapper.AddComponent<RectTransform>();
        wrapperRT.sizeDelta = new Vector2(config.width, totalHeight);

        // 2. Label bên ngoài (phía trên)
        if (hasLabel)
        {
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(wrapper.transform, false);
            RectTransform labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 1f);
            labelRT.anchorMax = new Vector2(1f, 1f);
            labelRT.pivot = new Vector2(0f, 1f);
            labelRT.anchoredPosition = new Vector2(HORIZONTAL_PADDING / 2f, -LABEL_HEIGHT * 0.5f);
            labelRT.sizeDelta = new Vector2(-10f, LABEL_HEIGHT);

            TextMeshProUGUI labelTxt = labelObj.AddComponent<TextMeshProUGUI>();
            labelTxt.text = config.label;
            labelTxt.fontSize = config.labelFontSize;
            labelTxt.fontStyle = FontStyles.Bold;
            labelTxt.color = Color.white;
            labelTxt.alignment = TextAlignmentOptions.BottomLeft;
            labelTxt.raycastTarget = false;
            if (config.font != null) labelTxt.font = config.font;
        }

        // 3. InputBox container - anchor ở bottom, chiều cao cố định
        GameObject inputBox = new GameObject("InputBox");
        inputBox.transform.SetParent(wrapper.transform, false);
        RectTransform inputBoxRT = inputBox.AddComponent<RectTransform>();
        inputBoxRT.anchorMin = Vector2.zero;
        inputBoxRT.anchorMax = new Vector2(1f, 0f);
        inputBoxRT.pivot = new Vector2(0.5f, 0f);
        inputBoxRT.anchoredPosition = Vector2.zero;
        inputBoxRT.sizeDelta = new Vector2(0f, boxHeight);

        // 4. HitArea - vùng click/collider
        GameObject hitArea = new GameObject("HitArea");
        UIElementBuilder.CreateFullStretch(hitArea, inputBox.transform);

        // Nearly transparent image for UI raycast (needs minimal alpha to ensure raycasting works)
        Image hitImg = hitArea.AddComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0.004f); // Minimal alpha, visually invisible but raycastable

        // BoxCollider for VR raycast
        BoxCollider col = hitArea.AddComponent<BoxCollider>();
        col.size = new Vector3(config.width, boxHeight, UIConstants.ColliderDepth);
        col.center = new Vector3(0, 0, UIConstants.ColliderZOffset);

        // Set layer
        int vrLayer = LayerMask.NameToLayer(config.layerName);
        if (vrLayer != -1) hitArea.layer = vrLayer;

        // 5. Visuals - container for visual elements
        // Keep within bounds - edge padding in shader handles visual margin
        GameObject visuals = new GameObject("Visuals");
        UIElementBuilder.CreateFullStretch(visuals, hitArea.transform);

        // 6. Background
        CreateBackground(visuals.transform, config, boxHeight);

        // 7. Border
        CreateBorder(visuals.transform, config, boxHeight);

        // 8. Content (InputField)
        TMP_InputField inputField = CreateInputContent(visuals.transform, config);

        // 9. Hover effects - using unified HoverEffectController
        HoverEffectController hoverController = hitArea.AddComponent<HoverEffectController>();
        hoverController.TargetVisuals = visuals.transform;

        // Add glow border effect (same multiplier as buttons for subtle effect)
        hoverController.AddEffect(new GlowBorderHoverEffect()
            .WithShaderSwap(true)
            .WithBorderMultiplier(1f));

        hoverController.AddEffect(new BackgroundHoverEffect());

        if (config.popAmount > 0)
        {
            hoverController.AddEffect(new ZPopHoverEffect()
                .WithPopAmount(config.popAmount));
        }

        // Add cursor change effect for text input
        hoverController.AddEffect(new CursorChangeHoverEffect()
            .WithResourcePath("icon_text_cursor"));

        // 10. VRButtonAnimation for ripple click effect
        VRButtonAnimation anim = hitArea.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visuals.transform;

        // 11. Setup callbacks
        if (onValueChanged != null)
        {
            inputField.onValueChanged.AddListener((value) => onValueChanged(value));
        }
        if (onEndEdit != null)
        {
            inputField.onEndEdit.AddListener((value) => onEndEdit(value));
        }

        // 12. Setup VR Keyboard integration
        // Add click handler to show VR keyboard when input field is clicked
        SetupVRKeyboardIntegration(hitArea, inputField);

        return wrapper;
    }

    /// <summary>
    /// Tạo Input Field đơn giản (chỉ có input, không có label)
    /// Chiều cao tự động tính từ font size
    /// </summary>
    public static GameObject CreateSimpleInputField(Transform parent, float width,
        string placeholder, Color color, System.Action<string> onEndEdit = null,
        int fontSize = 36, TMP_FontAsset font = null)
    {
        var config = new InputFieldConfig
        {
            label = "",
            placeholder = placeholder,
            themeColor = color,
            width = width,
            inputFontSize = fontSize,
            font = font
        };
        return CreateInputField(parent, config, null, onEndEdit);
    }

    /// <summary>
    /// Tạo Input Field với label (như Host, Port trong hình)
    /// Chiều cao tự động tính từ font size
    /// </summary>
    public static GameObject CreateLabeledInputField(Transform parent, float width,
        string label, string defaultValue, Color color, System.Action<string> onEndEdit = null,
        int labelFontSize = 24, int inputFontSize = 36, TMP_FontAsset font = null,
        TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard)
    {
        var config = new InputFieldConfig
        {
            label = label,
            defaultValue = defaultValue,
            placeholder = "",
            themeColor = color,
            width = width,
            labelFontSize = labelFontSize,
            inputFontSize = inputFontSize,
            font = font,
            contentType = contentType
        };
        return CreateInputField(parent, config, null, onEndEdit);
    }

    /// <summary>
    /// Tính chiều cao của InputField dựa trên font size
    /// </summary>
    public static float CalculateHeight(int inputFontSize, bool hasLabel)
    {
        float boxHeight = inputFontSize * FONT_TO_BOX_RATIO;
        return boxHeight + (hasLabel ? LABEL_HEIGHT : 0f);
    }

    /// <summary>
    /// Lấy TMP_InputField component từ wrapper object
    /// </summary>
    public static TMP_InputField GetInputField(GameObject wrapper)
    {
        return wrapper.GetComponentInChildren<TMP_InputField>();
    }

    /// <summary>
    /// Lấy giá trị text từ wrapper object
    /// </summary>
    public static string GetValue(GameObject wrapper)
    {
        var inputField = GetInputField(wrapper);
        return inputField != null ? inputField.text : "";
    }

    /// <summary>
    /// Đặt giá trị text cho wrapper object
    /// </summary>
    public static void SetValue(GameObject wrapper, string value)
    {
        var inputField = GetInputField(wrapper);
        if (inputField != null)
        {
            inputField.text = value;
        }
    }

    /// <summary>
    /// Bật/tắt khả năng tương tác của input field
    /// </summary>
    public static void SetInteractable(GameObject wrapper, bool interactable)
    {
        if (wrapper == null) return;

        var inputField = GetInputField(wrapper);
        if (inputField != null)
        {
            inputField.interactable = interactable;
        }

        // Sử dụng CanvasGroup để block raycast khi disabled (giống VRDropdownFactory)
        var canvasGroup = wrapper.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = wrapper.AddComponent<CanvasGroup>();
        canvasGroup.alpha = interactable ? 1f : 0.5f;
        canvasGroup.interactable = interactable;
        canvasGroup.blocksRaycasts = interactable;
    }

    // ==================== VR KEYBOARD INTEGRATION ====================

    /// <summary>
    /// Setup VR keyboard integration for an input field.
    /// When the input field is clicked, the VR keyboard will appear.
    /// </summary>
    private static void SetupVRKeyboardIntegration(GameObject hitArea, TMP_InputField inputField)
    {
        // Add VRInputFieldTrigger component to handle click events
        VRInputFieldTrigger trigger = hitArea.AddComponent<VRInputFieldTrigger>();
        trigger.Initialize(inputField);
    }

    // ==================== INTERNAL HELPERS ====================

    private static void CreateBackground(Transform parent, InputFieldConfig config, float boxHeight)
    {
        GameObject bgObj = new GameObject("Background");
        UIElementBuilder.CreateFullStretch(bgObj, parent);

        Image img = bgObj.AddComponent<Image>();
        img.sprite = SpriteUtility.GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / boxHeight;

        // Use MaterialFactory to create glass background
        Material mat = MaterialFactory.CreateGlassBackground(
            config.cornerRadius,
            config.edgePadding,
            aspect,
            config.themeColor,
            config.backgroundAlpha
        );

        if (mat != null)
        {
            img.material = mat;
            img.color = Color.white;
        }
        else
        {
            img.color = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, config.backgroundAlpha);
        }
    }

    private static void CreateBorder(Transform parent, InputFieldConfig config, float boxHeight)
    {
        GameObject borderObj = new GameObject("Border");
        UIElementBuilder.CreateFullStretch(borderObj, parent);

        Image img = borderObj.AddComponent<Image>();
        img.sprite = SpriteUtility.GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / boxHeight;

        // Use MaterialFactory to create glow border
        Material mat = MaterialFactory.CreateGlowBorder(
            aspect,
            config.cornerRadius,
            config.edgePadding,
            config.themeColor,
            config.borderWidth,
            config.glowWidth,
            config.glowIntensity,
            false  // No pulse animation for input fields
        );

        if (mat != null)
        {
            img.material = mat;

            // VRButtonRipple for ripple effect
            borderObj.AddComponent<VRButtonRipple>().Initialize(mat, img);
        }
    }

    private static TMP_InputField CreateInputContent(Transform parent, InputFieldConfig config)
    {
        GameObject content = new GameObject("Content");
        UIElementBuilder.CreateFullStretch(content, parent);

        // InputField container - fixed padding from edges
        GameObject inputContainer = new GameObject("InputField");
        RectTransform inputContainerRT = UIElementBuilder.CreateAnchored(
            inputContainer,
            content.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(HORIZONTAL_PADDING, VERTICAL_PADDING),
            new Vector2(-HORIZONTAL_PADDING, -VERTICAL_PADDING)
        );

        // Text Area - fills InputContainer with RectMask2D
        GameObject textArea = new GameObject("Text Area");
        RectTransform textAreaRT = UIElementBuilder.CreateFullStretch(textArea, inputContainer.transform);
        textArea.AddComponent<RectMask2D>();

        // Placeholder
        GameObject placeholderObj = new GameObject("Placeholder");
        UIElementBuilder.CreateFullStretch(placeholderObj, textArea.transform);

        TextMeshProUGUI placeholderTxt = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderTxt.text = config.placeholder;
        placeholderTxt.fontSize = config.inputFontSize;
        placeholderTxt.color = new Color(1f, 1f, 1f, 0.7f);
        placeholderTxt.alignment = TextAlignmentOptions.Left;
        placeholderTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
        placeholderTxt.raycastTarget = false;
        placeholderTxt.overflowMode = TextOverflowModes.Ellipsis;
        placeholderTxt.textWrappingMode = TextWrappingModes.NoWrap;
        if (config.font != null) placeholderTxt.font = config.font;

        // Input Text
        GameObject textObj = new GameObject("Text");
        UIElementBuilder.CreateFullStretch(textObj, textArea.transform);

        TextMeshProUGUI inputTxt = textObj.AddComponent<TextMeshProUGUI>();
        inputTxt.text = config.defaultValue;
        inputTxt.fontSize = config.inputFontSize;
        inputTxt.color = Color.white;
        inputTxt.alignment = TextAlignmentOptions.Left;
        inputTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
        inputTxt.raycastTarget = false;
        inputTxt.overflowMode = TextOverflowModes.Overflow;
        inputTxt.textWrappingMode = TextWrappingModes.NoWrap;
        if (config.font != null) inputTxt.font = config.font;

        // TMP_InputField component
        TMP_InputField inputField = inputContainer.AddComponent<TMP_InputField>();
        inputField.textViewport = textAreaRT;
        inputField.textComponent = inputTxt;
        inputField.placeholder = placeholderTxt;
        inputField.text = config.defaultValue;
        inputField.contentType = config.contentType;
        inputField.characterLimit = config.characterLimit;
        inputField.caretColor = config.themeColor;
        inputField.selectionColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.3f);

        // Raycast Overlay - invisible image on top of content to ensure consistent hover detection
        // TMP_InputField's internal Caret can intercept raycasts, so we add this overlay
        // to guarantee the raycast always hits Content (child of HitArea) for proper hover effects
        GameObject raycastOverlay = new GameObject("RaycastOverlay");
        UIElementBuilder.CreateFullStretch(raycastOverlay, content.transform);

        Image overlayImg = raycastOverlay.AddComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0f); // Completely transparent
        overlayImg.raycastTarget = true; // Captures all raycasts in the content area

        return inputField;
    }
}
