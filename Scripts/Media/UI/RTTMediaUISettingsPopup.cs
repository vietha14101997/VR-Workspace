using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// UI Settings popup as a separate world-space RTTMenuFrame panel.
/// Controls: Depth, Height, Scale sliders + Reset button.
/// Show/Hide is managed by toggling the parent frame object.
/// </summary>
public class RTTMediaUISettingsPopup : MonoBehaviour
{
    #region Constants
    private static readonly Color HEADER_BG = new Color(0.12f, 0.12f, 0.12f, 1.0f);
    private static readonly Color BODY_BG = new Color(0.20f, 0.20f, 0.22f, 1.0f);
    private static readonly Color TEXT_COLOR = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color THEME_COLOR = new Color(1f, 0.2f, 0.2f, 1f);
    private static readonly Color DETAIL_COLOR = new Color(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Color RESET_BTN_BG = new Color(0.14f, 0.14f, 0.16f, 0.8f);
    private static readonly Color RESET_BTN_HOVER = new Color(0.22f, 0.22f, 0.24f, 0.9f);

    private const float DEFAULT_DEPTH = 0f;
    private const float DEFAULT_HEIGHT = 0.5f;
    private const float DEFAULT_SCALE = 0.5f;

    private const float SLIDER_ROW_HEIGHT = 75f;
    private const float VALUES_TEXT_HEIGHT = 40f;
    private const float RESET_BTN_HEIGHT = 55f;
    #endregion

    #region Events
    public event Action<float> OnUIDepthChanged;
    public event Action<float> OnUIHeightChanged;
    public event Action<float> OnUIScaleChanged;
    public event Action OnUISettingsReset;
    public event Action OnCloseRequested;
    #endregion

    #region Private Fields
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private float _width, _height;

    private VRSliderControl _depthSlider;
    private VRSliderControl _heightSlider;
    private VRSliderControl _scaleSlider;
    private TextMeshProUGUI _valuesText;

    private float _currentDepth = DEFAULT_DEPTH;
    private float _currentHeight = DEFAULT_HEIGHT;
    private float _currentScale = DEFAULT_SCALE;

    private static Sprite _roundedRectSprite;
    #endregion

    #region Public API
    public void Initialize(Transform contentContainer, float width, float height, TMP_FontAsset font, Color primaryColor)
    {
        _font = font;
        _primaryColor = primaryColor;
        _width = width;
        _height = height;
        BuildUI(contentContainer);
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
    public bool IsVisible => gameObject.activeSelf;

    public void SetValues(float depth, float height, float scale)
    {
        _currentDepth = depth;
        _currentHeight = height;
        _currentScale = scale;
        _depthSlider?.SetValueWithoutNotify(depth);
        _heightSlider?.SetValueWithoutNotify(height);
        _scaleSlider?.SetValueWithoutNotify(scale);
        UpdateValuesText();
    }
    #endregion

    #region UI Building
    private void BuildUI(Transform container)
    {
        // Root fills the RTTMenuFrame content container
        GameObject root = new GameObject("UISettingsRoot");
        root.transform.SetParent(container, false);
        var rootRT = root.AddComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // Use childControlHeight=true so the VLG actually sizes children
        var rootLayout = root.AddComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 0;
        rootLayout.padding = new RectOffset(0, 0, 0, 0);
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childControlWidth = true;
        rootLayout.childForceExpandWidth = true;

        float headerH = _height * 0.15f;

        CreateHeader(root.transform, headerH);
        CreateBody(root.transform);
    }

    private void CreateHeader(Transform parent, float height)
    {
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(parent, false);

        var headerLE = headerObj.AddComponent<LayoutElement>();
        headerLE.preferredHeight = height;

        var bg = headerObj.AddComponent<Image>();
        bg.color = HEADER_BG;

        // Use RectTransform-based children (not HLG) for header content
        // so we have full control over positioning
        // Title centered
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(headerObj.transform, false);
        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero;

        var titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.font = _font;
        titleText.text = "UI settings";
        titleText.fontSize = 32;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = TEXT_COLOR;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.raycastTarget = false;

        // Close button (top-right)
        float closeBtnSize = height * 0.55f;
        GameObject closeObj = new GameObject("CloseBtn");
        closeObj.transform.SetParent(headerObj.transform, false);
        var closeRT = closeObj.AddComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1, 0.5f);
        closeRT.anchorMax = new Vector2(1, 0.5f);
        closeRT.pivot = new Vector2(1, 0.5f);
        closeRT.sizeDelta = new Vector2(closeBtnSize, closeBtnSize);
        closeRT.anchoredPosition = new Vector2(-20, 0);

        var closeBg = closeObj.AddComponent<Image>();
        closeBg.color = Color.clear;
        closeBg.raycastTarget = true;

        var closeBtn = closeObj.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        closeBtn.transition = Selectable.Transition.None;
        closeBtn.onClick.AddListener(() => OnCloseRequested?.Invoke());

        GameObject iconObj = new GameObject("IconImage");
        iconObj.transform.SetParent(closeObj.transform, false);
        var iconRT = iconObj.AddComponent<RectTransform>();
        float iconPad = closeBtnSize * 0.15f;
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.offsetMin = new Vector2(iconPad, iconPad);
        iconRT.offsetMax = new Vector2(-iconPad, -iconPad);

        var iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>("icon_close");
        iconImage.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        var closeHover = closeObj.AddComponent<HoverEffectController>();
        closeHover.AddEffect(new ScaleHoverEffect().WithHoverScale(1.2f).WithTransitionDuration(0.06f));
        closeHover.AddEffect(new ColorHoverEffect()
            .WithTargetChild("IconImage")
            .WithHoverColor(THEME_COLOR));

        var closeCol = closeObj.AddComponent<BoxCollider>();
        closeCol.size = new Vector3(closeBtnSize * 1.5f, closeBtnSize * 1.5f, 10);
        closeCol.center = new Vector3(0, 0, -5);
    }

    private void CreateBody(Transform parent)
    {
        GameObject bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(parent, false);

        // flexibleHeight=1 makes Body take all remaining space after Header
        var bodyLE = bodyObj.AddComponent<LayoutElement>();
        bodyLE.flexibleHeight = 1f;

        var bg = bodyObj.AddComponent<Image>();
        bg.color = BODY_BG;

        // Flat layout: sliders → flexible spacer → details
        // childControlHeight=true so VLG actually sizes children
        var layout = bodyObj.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(25, 25, 30, 20);
        layout.spacing = 20;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        float sliderAreaW = _width - 60f; // padding 30 each side

        // Depth slider (0 - 1.0, default 0)
        _depthSlider = CreateSliderRow(bodyObj.transform, "Depth", sliderAreaW, 0f, 1.0f, DEFAULT_DEPTH,
            (v) => { _currentDepth = v; OnUIDepthChanged?.Invoke(v); UpdateValuesText(); });

        // Height slider (0 - 1.0, default 0.5)
        _heightSlider = CreateSliderRow(bodyObj.transform, "Height", sliderAreaW, 0f, 1.0f, DEFAULT_HEIGHT,
            (v) => { _currentHeight = v; OnUIHeightChanged?.Invoke(v); UpdateValuesText(); });

        // Scale slider (0.2 - 1.0, default 0.5)
        _scaleSlider = CreateSliderRow(bodyObj.transform, "Scale", sliderAreaW, 0.2f, 1.0f, DEFAULT_SCALE,
            (v) => { _currentScale = v; OnUIScaleChanged?.Invoke(v); UpdateValuesText(); });

        // Flexible spacer pushes details to bottom
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(bodyObj.transform, false);
        var spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.flexibleHeight = 1f;

        // Values summary text
        GameObject valuesObj = new GameObject("ValuesText");
        valuesObj.transform.SetParent(bodyObj.transform, false);
        var valuesLE = valuesObj.AddComponent<LayoutElement>();
        valuesLE.preferredHeight = VALUES_TEXT_HEIGHT;

        _valuesText = valuesObj.AddComponent<TextMeshProUGUI>();
        _valuesText.font = _font;
        _valuesText.fontSize = 24;
        _valuesText.color = DETAIL_COLOR;
        _valuesText.alignment = TextAlignmentOptions.Center;
        _valuesText.raycastTarget = false;
        UpdateValuesText();

        // Reset to defaults button
        CreateResetToDefaultsButton(bodyObj.transform);
    }

    private VRSliderControl CreateSliderRow(Transform parent, string label, float totalWidth,
        float min, float max, float defaultValue, Action<float> onChanged)
    {
        float labelW = 130f;
        float resetBtnW = 45f;

        GameObject rowObj = new GameObject($"Row_{label}");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.preferredHeight = SLIDER_ROW_HEIGHT;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;

        // Label (fixed width)
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);
        var labelLE2 = labelObj.AddComponent<LayoutElement>();
        labelLE2.minWidth = labelW;
        labelLE2.preferredWidth = labelW;

        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.font = _font;
        labelText.text = label;
        labelText.fontSize = 28;
        labelText.color = TEXT_COLOR;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        // Slider (flexible width - takes remaining space)
        float estimatedSliderW = totalWidth - labelW - resetBtnW - 30f;
        var slider = VRSliderFactory.CreateSlider(
            rowObj.transform,
            estimatedSliderW, 45f,
            _font, THEME_COLOR,
            VRSliderFactory.SliderStyle.Setting,
            min, max);
        slider.SetValueWithoutNotify(defaultValue);
        slider.OnValueChanged += (v) => onChanged?.Invoke(v);

        var sliderLE = slider.GetComponent<LayoutElement>();
        if (sliderLE == null) sliderLE = slider.gameObject.AddComponent<LayoutElement>();
        sliderLE.flexibleWidth = 1f;

        // Reset button (fixed width)
        CreateResetIconButton(rowObj.transform, resetBtnW, () =>
        {
            slider.SetValue(defaultValue);
        });

        return slider;
    }

    private void CreateResetIconButton(Transform parent, float size, Action onReset)
    {
        GameObject btnObj = new GameObject("ResetBtn");
        btnObj.transform.SetParent(parent, false);

        var btnLE = btnObj.AddComponent<LayoutElement>();
        btnLE.minWidth = size;
        btnLE.preferredWidth = size;

        var bgImage = btnObj.AddComponent<Image>();
        bgImage.color = Color.clear;
        bgImage.raycastTarget = true;

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onReset?.Invoke());

        // Icon (icon_reset sprite)
        GameObject iconObj = new GameObject("IconImage");
        iconObj.transform.SetParent(btnObj.transform, false);
        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.15f, 0.15f);
        iconRT.anchorMax = new Vector2(0.85f, 0.85f);
        iconRT.offsetMin = Vector2.zero;
        iconRT.offsetMax = Vector2.zero;

        var iconImage = iconObj.AddComponent<Image>();
        iconImage.sprite = Resources.Load<Sprite>("icon_reset");
        iconImage.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.2f).WithTransitionDuration(0.06f));
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("IconImage")
            .WithHoverColor(THEME_COLOR));

        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(size * 1.3f, size * 1.3f, 10);
        col.center = new Vector3(0, 0, -5);
    }

    private void CreateResetToDefaultsButton(Transform parent)
    {
        GameObject btnObj = new GameObject("ResetDefaultsBtn");
        btnObj.transform.SetParent(parent, false);

        var btnLE = btnObj.AddComponent<LayoutElement>();
        btnLE.preferredHeight = RESET_BTN_HEIGHT;

        var bgImage = btnObj.AddComponent<Image>();
        bgImage.sprite = GetRoundedRectSprite();
        bgImage.type = Image.Type.Sliced;
        bgImage.color = RESET_BTN_BG;

        var button = btnObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            _depthSlider?.SetValue(DEFAULT_DEPTH);
            _heightSlider?.SetValue(DEFAULT_HEIGHT);
            _scaleSlider?.SetValue(DEFAULT_SCALE);
            _currentDepth = DEFAULT_DEPTH;
            _currentHeight = DEFAULT_HEIGHT;
            _currentScale = DEFAULT_SCALE;
            UpdateValuesText();
            OnUISettingsReset?.Invoke();
        });

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = _font;
        tmp.text = "Reset to defaults";
        tmp.fontSize = 24;
        tmp.color = TEXT_COLOR;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f).WithTransitionDuration(0.06f));
        hoverController.AddEffect(new ColorHoverEffect()
            .WithTargetChild("")
            .WithHoverColor(RESET_BTN_HOVER));

        var col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(_width - 60f, RESET_BTN_HEIGHT, 10);
        col.center = new Vector3(0, 0, -5);
    }
    #endregion

    #region Utilities
    private void UpdateValuesText()
    {
        if (_valuesText == null) return;
        _valuesText.text = $"Depth: {_currentDepth:F2}  |  Height: {_currentHeight:F2}  |  Scale: {_currentScale:F2}";
    }

    private static Sprite GetRoundedRectSprite()
    {
        if (_roundedRectSprite != null) return _roundedRectSprite;

        int size = 64;
        int radius = 12;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float alpha = 1f;
                Vector2 corner = Vector2.zero;
                bool isCorner = false;

                if (x < radius && y < radius)
                { corner = new Vector2(radius, radius); isCorner = true; }
                else if (x >= size - radius && y < radius)
                { corner = new Vector2(size - radius - 1, radius); isCorner = true; }
                else if (x < radius && y >= size - radius)
                { corner = new Vector2(radius, size - radius - 1); isCorner = true; }
                else if (x >= size - radius && y >= size - radius)
                { corner = new Vector2(size - radius - 1, size - radius - 1); isCorner = true; }

                if (isCorner)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), corner);
                    alpha = Mathf.Clamp01(radius - dist + 0.5f);
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector4 border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
        _roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
            Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        return _roundedRectSprite;
    }
    #endregion
}
