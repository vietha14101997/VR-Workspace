using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Factory class để tạo VR Input Field với đầy đủ hiệu ứng:
/// - Glass background với gradient
/// - Glowing border với hover effect
/// - Label nhỏ phía trên trái (bên ngoài box)
/// - Input text bên trong box
/// - VRButtonAnimation cho hover effects
/// - BoxCollider cho VR raycast
/// - VRKeyboard integration cho virtual keyboard trong VR
///
/// Chiều cao tự động tính từ font size:
/// - Box height = inputFontSize * 2.2
/// - Total height = box height + label height (nếu có)
/// </summary>
public static class VRInputFieldFactory
{
    private static Sprite _pixelSprite;

    // Hằng số layout
    private const float FONT_TO_BOX_RATIO = 2.2f;      // Tỷ lệ font size -> box height
    private const float LABEL_HEIGHT = 32f;            // Chiều cao label cố định
    private const float HORIZONTAL_PADDING = 60f;      // Padding trái/phải cho text
    private const float VERTICAL_PADDING = 8f;         // Padding trên/dưới cho text

    /// <summary>
    /// Cấu hình cho Input Field
    /// </summary>
    [System.Serializable]
    public class InputFieldConfig
    {
        public string label = "Label";
        public string placeholder = "Enter text...";
        public string defaultValue = "";
        public Color themeColor = new Color(0f, 0.9f, 1f);
        public float width = 300f;
        public int labelFontSize = 24;
        public int inputFontSize = 36;
        public TMP_FontAsset font;

        // Input settings
        public TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard;
        public int characterLimit = 0;

        // Visual settings
        public float cornerRadius = 0.12f;
        public float edgePadding = 0.06f;  // Match Space key value for Android compatibility
        public float backgroundAlpha = 0.08f;
        public float borderWidth = 0.09f;
        public float glowWidth = 0.04f;
        public float glowIntensity = 2.5f;

        // Animation
        public float popAmount = 0.005f;

        // Layer
        public string layerName = "VirtualObjects";

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
        hitArea.transform.SetParent(inputBox.transform, false);
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

        // 5. Visuals - container cho visual elements
        // Keep within bounds - edge padding in shader handles visual margin
        GameObject visuals = new GameObject("Visuals");
        visuals.transform.SetParent(hitArea.transform, false);
        RectTransform visRT = visuals.AddComponent<RectTransform>();
        visRT.anchorMin = Vector2.zero;
        visRT.anchorMax = Vector2.one;
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // 6. Background
        CreateBackground(visuals.transform, config, boxHeight);

        // 7. Border
        CreateBorder(visuals.transform, config, boxHeight);

        // 8. Content (InputField)
        TMP_InputField inputField = CreateInputContent(visuals.transform, config);

        // 9. VRButtonAnimation cho hover effects
        VRButtonAnimation anim = hitArea.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visuals.transform;
        anim.popAmount = config.popAmount;
        anim.hoverBorderMultiplier = 2f; // Input fields have thicker border when hovering

        // 10. Setup callbacks
        if (onValueChanged != null)
        {
            inputField.onValueChanged.AddListener((value) => onValueChanged(value));
        }
        if (onEndEdit != null)
        {
            inputField.onEndEdit.AddListener((value) => onEndEdit(value));
        }

        // 11. Setup VR Keyboard integration
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
        bgObj.transform.SetParent(parent, false);
        RectTransform rt = bgObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / boxHeight;
        Color col = config.themeColor;

        // Use Wide shader for better Android GPU compatibility
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_CornerRadius", config.cornerRadius);
            mat.SetFloat("_EdgePadding", config.edgePadding);
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
    }

    private static void CreateBorder(Transform parent, InputFieldConfig config, float boxHeight)
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

        float aspect = config.width / boxHeight;
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

    private static TMP_InputField CreateInputContent(Transform parent, InputFieldConfig config)
    {
        GameObject content = new GameObject("Content");
        content.transform.SetParent(parent, false);
        RectTransform cRT = content.AddComponent<RectTransform>();

        // Content fills Visuals (no expansion compensation needed)
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.offsetMin = Vector2.zero;
        cRT.offsetMax = Vector2.zero;

        // InputField container - padding cố định từ các cạnh
        GameObject inputContainer = new GameObject("InputField");
        inputContainer.transform.SetParent(content.transform, false);
        RectTransform inputContainerRT = inputContainer.AddComponent<RectTransform>();
        inputContainerRT.anchorMin = Vector2.zero;
        inputContainerRT.anchorMax = Vector2.one;
        inputContainerRT.offsetMin = new Vector2(HORIZONTAL_PADDING, VERTICAL_PADDING);
        inputContainerRT.offsetMax = new Vector2(-HORIZONTAL_PADDING, -VERTICAL_PADDING);

        // Text Area - fill hết InputContainer với RectMask2D
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputContainer.transform, false);
        RectTransform textAreaRT = textArea.AddComponent<RectTransform>();
        textAreaRT.anchorMin = Vector2.zero;
        textAreaRT.anchorMax = Vector2.one;
        textAreaRT.offsetMin = Vector2.zero;
        textAreaRT.offsetMax = Vector2.zero;
        textArea.AddComponent<RectMask2D>();

        // Placeholder
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        RectTransform placeholderRT = placeholderObj.AddComponent<RectTransform>();
        placeholderRT.anchorMin = Vector2.zero;
        placeholderRT.anchorMax = Vector2.one;
        placeholderRT.offsetMin = Vector2.zero;
        placeholderRT.offsetMax = Vector2.zero;

        TextMeshProUGUI placeholderTxt = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderTxt.text = config.placeholder;
        placeholderTxt.fontSize = config.inputFontSize;
        placeholderTxt.color = new Color(1f, 1f, 1f, 0.3f);
        placeholderTxt.alignment = TextAlignmentOptions.Left;
        placeholderTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
        placeholderTxt.raycastTarget = false;
        placeholderTxt.overflowMode = TextOverflowModes.Ellipsis;
        placeholderTxt.enableWordWrapping = false;
        if (config.font != null) placeholderTxt.font = config.font;

        // Input Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        TextMeshProUGUI inputTxt = textObj.AddComponent<TextMeshProUGUI>();
        inputTxt.text = config.defaultValue;
        inputTxt.fontSize = config.inputFontSize;
        inputTxt.color = Color.white;
        inputTxt.alignment = TextAlignmentOptions.Left;
        inputTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
        inputTxt.raycastTarget = false;
        inputTxt.overflowMode = TextOverflowModes.Overflow;
        inputTxt.enableWordWrapping = false;
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

        return inputField;
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
