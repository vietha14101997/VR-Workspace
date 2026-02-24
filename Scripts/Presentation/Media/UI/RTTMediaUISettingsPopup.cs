using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;
using VRWorkspace.UI.HoverEffects;

namespace VRWorkspace.Media.UI
{
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
        private static readonly Color TEXT_COLOR = Color.white;
        private static readonly Color THEME_COLOR = new Color(1f, 0.2f, 0.2f, 1f);
        private static readonly Color DETAIL_COLOR = new Color(0.65f, 0.65f, 0.65f, 1f);
        private static readonly Color RESET_BTN_BG = new Color(0.14f, 0.14f, 0.16f, 0.8f);
        private static readonly Color RESET_BTN_HOVER = new Color(0.22f, 0.22f, 0.24f, 0.9f);

        private const float DEFAULT_DEPTH = 0f;
        private const float DEFAULT_HEIGHT = 0.5f;
        private const float DEFAULT_SCALE = 0.5f;

        private const float SLIDER_ROW_HEIGHT = 90f;
        private const float VALUES_TEXT_HEIGHT = 40f;
        private const float RESET_BTN_HEIGHT = 83f; // 55 * 1.25 * 1.2
        private const float PLUS_MINUS_BTN_SIZE = 46f;
        private static readonly Color PILL_BG_COLOR = new Color(0.1f, 0.1f, 0.12f, 0.85f);
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
        private static Sprite _topRoundedRectSprite;
        private static Sprite _pillSprite;
        private static Sprite _bottomRoundedRectSprite;
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
            bg.sprite = GetTopRoundedRectSprite();
            bg.type = Image.Type.Sliced;
            bg.color = HEADER_BG;

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
            titleText.fontSize = 40;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = TEXT_COLOR;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.raycastTarget = false;

            // Close button (top-right, 25% smaller)
            float closeBtnSize = height * 0.4125f;
            GameObject closeObj = new GameObject("CloseBtn");
            closeObj.transform.SetParent(headerObj.transform, false);
            var closeRT = closeObj.AddComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(1, 0.5f);
            closeRT.anchorMax = new Vector2(1, 0.5f);
            closeRT.pivot = new Vector2(1, 0.5f);
            closeRT.sizeDelta = new Vector2(closeBtnSize, closeBtnSize);
            closeRT.anchoredPosition = new Vector2(-25, 0);

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
            bg.sprite = GetBottomRoundedRectSprite();
            bg.type = Image.Type.Sliced;
            bg.color = BODY_BG;

            // Flat layout: sliders → flexible spacer → details
            // childControlHeight=true so VLG actually sizes children
            var layout = bodyObj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(25, 25, 30, 40);
            layout.spacing = 25;
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

            // Scale slider (0 - 1.0, default 0.5)
            _scaleSlider = CreateSliderRow(bodyObj.transform, "Scale", sliderAreaW, 0f, 1.0f, DEFAULT_SCALE,
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
            _valuesText.fontSize = 32;
            _valuesText.color = Color.white;
            _valuesText.fontStyle = FontStyles.Bold;
            _valuesText.alignment = TextAlignmentOptions.Center;
            _valuesText.raycastTarget = false;
            UpdateValuesText();

            // Spacer between values text and reset button
            GameObject btnSpacer = new GameObject("BtnSpacer");
            btnSpacer.transform.SetParent(bodyObj.transform, false);
            var btnSpacerLE = btnSpacer.AddComponent<LayoutElement>();
            btnSpacerLE.preferredHeight = 0f;

            // Reset to defaults button
            CreateResetToDefaultsButton(bodyObj.transform);
        }

        private VRSliderControl CreateSliderRow(Transform parent, string label, float totalWidth,
            float min, float max, float defaultValue, Action<float> onChanged)
        {
            float labelW = 130f;
            float resetBtnW = 56f;
            float rowWidth = totalWidth;
            float pillWidth = rowWidth - labelW - resetBtnW - 20f;
            float sliderWidth = pillWidth * 0.66f;
            float btnAreaWidth = (pillWidth - sliderWidth) / 2f;
            float pillLeft = labelW + 10f;
            float stepSize = (max - min) / 20f;

            // === Row container (no inner layout, absolute positioning) ===
            GameObject rowObj = new GameObject($"Row_{label}");
            rowObj.transform.SetParent(parent, false);

            var rowRT = rowObj.GetComponent<RectTransform>();
            if (rowRT == null) rowRT = rowObj.AddComponent<RectTransform>();

            var rowLE = rowObj.AddComponent<LayoutElement>();
            rowLE.preferredHeight = SLIDER_ROW_HEIGHT;

            // === Label (left-aligned) ===
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(rowObj.transform, false);
            var labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.offsetMin = new Vector2(0f, 0f);
            labelRT.offsetMax = new Vector2(labelW, 0f);

            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.font = _font;
            labelText.text = label;
            labelText.fontSize = 35;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = TEXT_COLOR;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;

            // === Pill area (absolute positioned) ===
            GameObject pillAreaObj = new GameObject("PillArea");
            pillAreaObj.transform.SetParent(rowObj.transform, false);
            var pillAreaRT = pillAreaObj.AddComponent<RectTransform>();
            pillAreaRT.anchorMin = new Vector2(0f, 0f);
            pillAreaRT.anchorMax = new Vector2(0f, 1f);
            pillAreaRT.pivot = new Vector2(0f, 0.5f);
            pillAreaRT.offsetMin = new Vector2(pillLeft, 0f);
            pillAreaRT.offsetMax = new Vector2(pillLeft + pillWidth, 0f);

            // --- Pill background (hidden by default) ---
            GameObject pillObj = new GameObject("PillBg");
            pillObj.transform.SetParent(pillAreaObj.transform, false);
            var pillBgRT = pillObj.AddComponent<RectTransform>();
            pillBgRT.anchorMin = new Vector2(0f, 0.1f);
            pillBgRT.anchorMax = new Vector2(1f, 0.9f);
            pillBgRT.offsetMin = Vector2.zero;
            pillBgRT.offsetMax = Vector2.zero;
            var pillImage = pillObj.AddComponent<Image>();
            pillImage.sprite = GetPillSprite();
            pillImage.type = Image.Type.Sliced;
            pillImage.color = PILL_BG_COLOR;
            pillImage.raycastTarget = false;
            pillObj.SetActive(false);

            // --- Minus button (hidden by default) ---
            GameObject minusBtnObj = CreatePlusMinusButton(pillAreaObj.transform, false);
            var minusBtnRT = minusBtnObj.GetComponent<RectTransform>();
            minusBtnRT.anchorMin = new Vector2(0f, 0.5f);
            minusBtnRT.anchorMax = new Vector2(0f, 0.5f);
            minusBtnRT.pivot = new Vector2(0.5f, 0.5f);
            minusBtnRT.anchoredPosition = new Vector2(btnAreaWidth / 2f, 0f);
            minusBtnRT.sizeDelta = new Vector2(PLUS_MINUS_BTN_SIZE, PLUS_MINUS_BTN_SIZE);
            minusBtnObj.SetActive(false);

            // --- Slider (centered in pill area) ---
            var slider = VRSliderFactory.CreateSlider(
                pillAreaObj.transform,
                sliderWidth, 45f,
                _font, THEME_COLOR,
                VRSliderFactory.SliderStyle.Setting,
                min, max);
            slider.SetValueWithoutNotify(defaultValue);
            slider.PreviewEnabled = false;

            // Increase track and fill thickness by 1.5x (8f → 12f)
            var trackBgChild = slider.transform.Find("SliderArea/TrackBackground");
            if (trackBgChild != null)
            {
                var trt = trackBgChild.GetComponent<RectTransform>();
                trt.sizeDelta = new Vector2(trt.sizeDelta.x, 12f);
            }
            var fillChild = slider.transform.Find("SliderArea/Fill");
            if (fillChild != null)
            {
                var frt = fillChild.GetComponent<RectTransform>();
                frt.sizeDelta = new Vector2(frt.sizeDelta.x, 12f);
            }

            var sliderRT2 = slider.GetComponent<RectTransform>();
            sliderRT2.anchorMin = new Vector2(0.5f, 0.5f);
            sliderRT2.anchorMax = new Vector2(0.5f, 0.5f);
            sliderRT2.pivot = new Vector2(0.5f, 0.5f);
            sliderRT2.anchoredPosition = Vector2.zero;

            // --- Plus button (hidden by default) ---
            GameObject plusBtnObj = CreatePlusMinusButton(pillAreaObj.transform, true);
            var plusBtnRT = plusBtnObj.GetComponent<RectTransform>();
            plusBtnRT.anchorMin = new Vector2(1f, 0.5f);
            plusBtnRT.anchorMax = new Vector2(1f, 0.5f);
            plusBtnRT.pivot = new Vector2(0.5f, 0.5f);
            plusBtnRT.anchoredPosition = new Vector2(-btnAreaWidth / 2f, 0f);
            plusBtnRT.sizeDelta = new Vector2(PLUS_MINUS_BTN_SIZE, PLUS_MINUS_BTN_SIZE);
            plusBtnObj.SetActive(false);

            // Wire up +/- click handlers
            float capturedStep = stepSize;
            VRSliderControl sliderRef = slider;
            minusBtnObj.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (sliderRef != null) sliderRef.SetValue(sliderRef.Value - capturedStep);
            });
            plusBtnObj.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (sliderRef != null) sliderRef.SetValue(sliderRef.Value + capturedStep);
            });

            // Min/max button state locking
            var minusButton = minusBtnObj.GetComponent<Button>();
            var plusButton = plusBtnObj.GetComponent<Button>();
            var minusIcon = minusBtnObj.transform.Find("Icon")?.GetComponent<Image>();
            var plusIcon = plusBtnObj.transform.Find("Icon")?.GetComponent<Image>();
            System.Action<float> updateBtnStates = (v) =>
            {
                bool atMin = v <= min;
                bool atMax = v >= max;
                if (minusButton != null) minusButton.interactable = !atMin;
                if (plusButton != null) plusButton.interactable = !atMax;
                if (minusIcon != null) minusIcon.color = atMin ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
                if (plusIcon != null) plusIcon.color = atMax ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
            };
            updateBtnStates(defaultValue);
            slider.OnValueChanged += updateBtnStates;

            // --- Value text (child of handle, below seeker, outside pill) ---
            var handleRT = slider.HandleTransform;
            GameObject valueObj = new GameObject("HoverValue");
            valueObj.transform.SetParent(handleRT, false);
            var valueRT = valueObj.AddComponent<RectTransform>();
            valueRT.anchoredPosition = new Vector2(0, -72f);
            valueRT.sizeDelta = new Vector2(160, 45);

            var valueText = valueObj.AddComponent<TextMeshProUGUI>();
            valueText.font = _font;
            valueText.text = $"{defaultValue:F2}";
            valueText.fontSize = 35;
            valueText.fontStyle = FontStyles.Bold;
            valueText.color = Color.white;
            valueText.alignment = TextAlignmentOptions.Center;
            valueText.overflowMode = TextOverflowModes.Overflow;
            valueText.raycastTarget = false;
            valueObj.SetActive(false);

            // === Hover group ===
            var hoverGroup = pillAreaObj.AddComponent<SettingsSliderHoverGroup>();
            hoverGroup.Setup(pillObj, minusBtnObj, plusBtnObj, valueObj);

            slider.OnHoverEnter += hoverGroup.OnChildHoverEnter;
            slider.OnHoverExit += hoverGroup.OnChildHoverExit;

            var minusNotifier = minusBtnObj.GetComponent<HoverNotifier>();
            if (minusNotifier != null)
            {
                minusNotifier.onEnter += hoverGroup.OnChildHoverEnter;
                minusNotifier.onExit += hoverGroup.OnChildHoverExit;
            }
            var plusNotifier = plusBtnObj.GetComponent<HoverNotifier>();
            if (plusNotifier != null)
            {
                plusNotifier.onEnter += hoverGroup.OnChildHoverEnter;
                plusNotifier.onExit += hoverGroup.OnChildHoverExit;
            }

            // Value change callback
            slider.OnValueChanged += (v) =>
            {
                onChanged?.Invoke(v);
                if (valueText != null) valueText.text = $"{v:F2}";
            };

            // === Reset button (right-aligned) ===
            GameObject resetBtnObj = new GameObject("ResetBtn");
            resetBtnObj.transform.SetParent(rowObj.transform, false);
            var resetBtnRT2 = resetBtnObj.AddComponent<RectTransform>();
            resetBtnRT2.anchorMin = new Vector2(1f, 0.5f);
            resetBtnRT2.anchorMax = new Vector2(1f, 0.5f);
            resetBtnRT2.pivot = new Vector2(1f, 0.5f);
            resetBtnRT2.sizeDelta = new Vector2(resetBtnW, resetBtnW);
            resetBtnRT2.anchoredPosition = Vector2.zero;

            var resetBg = resetBtnObj.AddComponent<Image>();
            resetBg.color = Color.clear;
            resetBg.raycastTarget = true;

            var resetBtn = resetBtnObj.AddComponent<Button>();
            resetBtn.targetGraphic = resetBg;
            resetBtn.transition = Selectable.Transition.None;

            float capturedDefault = defaultValue;
            VRSliderControl capturedSlider = slider;
            TextMeshProUGUI capturedValueText = valueText;
            resetBtn.onClick.AddListener(() =>
            {
                capturedSlider.SetValue(capturedDefault);
                if (capturedValueText != null) capturedValueText.text = $"{capturedDefault:F2}";
            });

            GameObject resetIconObj = new GameObject("IconImage");
            resetIconObj.transform.SetParent(resetBtnObj.transform, false);
            var resetIconRT = resetIconObj.AddComponent<RectTransform>();
            resetIconRT.anchorMin = new Vector2(0.15f, 0.15f);
            resetIconRT.anchorMax = new Vector2(0.85f, 0.85f);
            resetIconRT.offsetMin = Vector2.zero;
            resetIconRT.offsetMax = Vector2.zero;

            var resetIcon = resetIconObj.AddComponent<Image>();
            resetIcon.sprite = Resources.Load<Sprite>("icon_reset");
            resetIcon.color = Color.white;
            resetIcon.preserveAspect = true;
            resetIcon.raycastTarget = false;

            var resetHover = resetBtnObj.AddComponent<HoverEffectController>();
            resetHover.AddEffect(new ScaleHoverEffect().WithHoverScale(1.2f).WithTransitionDuration(0.06f));

            var resetCol = resetBtnObj.AddComponent<BoxCollider>();
            resetCol.size = new Vector3(resetBtnW * 1.3f, resetBtnW * 1.3f, 10);
            resetCol.center = new Vector3(0, 0, -5);

            // Row-level hover detection via transparent Image (GraphicRaycaster requires Graphic)
            var rowImage = rowObj.AddComponent<Image>();
            rowImage.color = Color.clear;
            rowImage.raycastTarget = true;
            var rowNotifier = rowObj.AddComponent<HoverNotifier>();
            rowNotifier.onEnter += hoverGroup.OnChildHoverEnter;
            rowNotifier.onExit += hoverGroup.OnChildHoverExit;

            return slider;
        }

        /// <summary>
        /// Create a +/- icon button for slider hover area.
        /// </summary>
        private GameObject CreatePlusMinusButton(Transform parent, bool isPlus)
        {
            string name = isPlus ? "PlusBtn" : "MinusBtn";
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            var btnRT = btnObj.AddComponent<RectTransform>();

            var bgImage = btnObj.AddComponent<Image>();
            bgImage.color = Color.clear;
            bgImage.raycastTarget = true;

            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(btnObj.transform, false);
            var iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.15f, 0.15f);
            iconRT.anchorMax = new Vector2(0.85f, 0.85f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            var iconImage = iconObj.AddComponent<Image>();
            iconImage.sprite = Resources.Load<Sprite>(isPlus ? "icon_plus" : "icon_minus");
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            var button = btnObj.AddComponent<Button>();
            button.targetGraphic = bgImage;
            button.transition = Selectable.Transition.None;

            btnObj.AddComponent<HoverNotifier>();

            var hoverCtrl = btnObj.AddComponent<HoverEffectController>();
            hoverCtrl.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f).WithTransitionDuration(0.06f));

            var col = btnObj.AddComponent<BoxCollider>();
            col.size = new Vector3(PLUS_MINUS_BTN_SIZE * 1.3f, PLUS_MINUS_BTN_SIZE * 1.3f, 10);
            col.center = new Vector3(0, 0, -5);

            return btnObj;
        }

        private void CreateResetToDefaultsButton(Transform parent)
        {
            // Wrapper takes full VLG width; button inside is 90% width centered
            GameObject wrapperObj = new GameObject("ResetBtnWrapper");
            wrapperObj.transform.SetParent(parent, false);
            var wrapperLE = wrapperObj.AddComponent<LayoutElement>();
            wrapperLE.preferredHeight = RESET_BTN_HEIGHT;

            float btnWidth = _width * 0.9f;
            GameObject btnObj = new GameObject("ResetDefaultsBtn");
            btnObj.transform.SetParent(wrapperObj.transform, false);
            var btnRT = btnObj.AddComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0.5f, 0f);
            btnRT.anchorMax = new Vector2(0.5f, 1f);
            btnRT.pivot = new Vector2(0.5f, 0.5f);
            btnRT.sizeDelta = new Vector2(btnWidth, 0f);

            var bgImage = btnObj.AddComponent<Image>();
            bgImage.sprite = GetPillSprite();
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
            tmp.fontSize = 30;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = TEXT_COLOR;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            var hoverController = btnObj.AddComponent<HoverEffectController>();
            hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f).WithTransitionDuration(0.06f));
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")
                .WithHoverColor(RESET_BTN_HOVER));

            var col = btnObj.AddComponent<BoxCollider>();
            col.size = new Vector3(btnWidth, RESET_BTN_HEIGHT, 10);
            col.center = new Vector3(0, 0, -5);
        }
        #endregion

        #region Utilities
        private void UpdateValuesText()
        {
            if (_valuesText == null) return;
            _valuesText.text = $"Depth: {_currentDepth.ToString("F2").Replace('.', ',')}  |  Height: {_currentHeight.ToString("F2").Replace('.', ',')}  |  Scale: {_currentScale.ToString("F2").Replace('.', ',')}";
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

        private static Sprite GetTopRoundedRectSprite()
        {
            if (_topRoundedRectSprite != null) return _topRoundedRectSprite;

            int texW = 64, texH = 64;
            int radius = 12;
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float alpha = 1f;
                    Vector2 corner = Vector2.zero;
                    bool isCorner = false;

                    if (x < radius && y >= texH - radius)
                    { corner = new Vector2(radius, texH - radius - 1); isCorner = true; }
                    else if (x >= texW - radius && y >= texH - radius)
                    { corner = new Vector2(texW - radius - 1, texH - radius - 1); isCorner = true; }

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

            Vector4 border = new Vector4(radius + 1, 1, radius + 1, radius + 1);
            _topRoundedRectSprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
            return _topRoundedRectSprite;
        }

        /// <summary>
        /// Rounded rect with only bottom-left and bottom-right corners rounded.
        /// </summary>
        private static Sprite GetBottomRoundedRectSprite()
        {
            if (_bottomRoundedRectSprite != null) return _bottomRoundedRectSprite;

            int texW = 64, texH = 64;
            int radius = 12;
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float alpha = 1f;
                    Vector2 corner = Vector2.zero;
                    bool isCorner = false;

                    // Only bottom corners are rounded (bottom = low y in texture)
                    if (x < radius && y < radius)
                    { corner = new Vector2(radius, radius); isCorner = true; }
                    else if (x >= texW - radius && y < radius)
                    { corner = new Vector2(texW - radius - 1, radius); isCorner = true; }

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

            // Border: bottom corners have radius, top corners have 1 (sharp)
            Vector4 border = new Vector4(radius + 1, radius + 1, radius + 1, 1);
            _bottomRoundedRectSprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
            return _bottomRoundedRectSprite;
        }

        /// <summary>
        /// Pill-shaped sprite (very large radius so short sides become semicircles).
        /// </summary>
        private static Sprite GetPillSprite()
        {
            if (_pillSprite != null) return _pillSprite;

            int texW = 128, texH = 64;
            int radius = texH / 2; // Full semicircle ends (32px)
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    float alpha = 1f;
                    Vector2 corner = Vector2.zero;
                    bool isCorner = false;

                    if (x < radius && y < radius)
                    { corner = new Vector2(radius, radius); isCorner = true; }
                    else if (x >= texW - radius && y < radius)
                    { corner = new Vector2(texW - radius - 1, radius); isCorner = true; }
                    else if (x < radius && y >= texH - radius)
                    { corner = new Vector2(radius, texH - radius - 1); isCorner = true; }
                    else if (x >= texW - radius && y >= texH - radius)
                    { corner = new Vector2(texW - radius - 1, texH - radius - 1); isCorner = true; }

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
            _pillSprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
                Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
            return _pillSprite;
        }
        #endregion
    }

}
