using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.UI.HoverEffects;

namespace VRWorkspace.Media.UI
{
    public partial class RTTMediaSettingsPanel
    {
        #region Sub-page: Picture Adjustments
        private void CreatePictureAdjPage(Transform parent)
        {
            _pictureAdjContainer = new GameObject("PictureAdjustments");
            _pictureAdjContainer.transform.SetParent(parent, false);
            StretchFill(_pictureAdjContainer);
            _pictureAdjContainer.SetActive(false);

            // Scrollable content
            var scrollContent = CreateScrollableContent(_pictureAdjContainer.transform);

            // Sharpen slider (0 - 2, default 0.0) → display as integer 0-20
            (_sharpnessSlider, _sharpnessValueLabel) = CreateSliderRow(
                scrollContent, "Sharpen", 0f, 2f, 0.0f,
                (v) => OnSharpnessChanged?.Invoke(v),
                (v) => Mathf.RoundToInt(v * 10f).ToString());

            // Brightness slider (0 - 2, default 1.0) → display centered at 0, range -1.1 to 1.1
            (_brightnessSlider, _brightnessValueLabel) = CreateSliderRow(
                scrollContent, "Brightness", 0f, 2f, 1.0f,
                (v) => OnBrightnessChanged?.Invoke(v),
                (v) => ((v - 1f) * 1.1f).ToString("0.#"));

            // Saturation slider (0 - 2, default 1.0) → display centered at 0, range -1.1 to 1.1
            (_saturationSlider, _saturationValueLabel) = CreateSliderRow(
                scrollContent, "Saturation", 0f, 2f, 1.0f,
                (v) => OnSaturationChanged?.Invoke(v),
                (v) => ((v - 1f) * 1.1f).ToString("0.#"));

            // Contrast slider (0 - 2, default 1.0) → display centered at 0, range -1.1 to 1.1
            (_contrastSlider, _contrastValueLabel) = CreateSliderRow(
                scrollContent, "Contrast", 0f, 2f, 1.0f,
                (v) => OnContrastChanged?.Invoke(v),
                (v) => ((v - 1f) * 1.1f).ToString("0.#"));

            // Tint slider (-1 - 1, default 0) → display range -1.1 to 1.1
            (_tintSlider, _tintValueLabel) = CreateSliderRow(
                scrollContent, "Tint", -1f, 1f, 0f,
                (v) => OnTintChanged?.Invoke(v),
                (v) => (v * 1.1f).ToString("0.#"));

            // Temperature slider (-1 - 1, default 0) → display range -1.1 to 1.1
            (_temperatureSlider, _temperatureValueLabel) = CreateSliderRow(
                scrollContent, "Temperature", -1f, 1f, 0f,
                (v) => OnTemperatureChanged?.Invoke(v),
                (v) => (v * 1.1f).ToString("0.#"));

            // Spacer
            CreateFixedSpacer(scrollContent, 20f);

            // Save as defaults button
            CreateActionButton(scrollContent, "Save as defaults", () => OnPictureSaveDefaults?.Invoke());

            // Reset to defaults button
            CreateActionButton(scrollContent, "Reset to defaults", () =>
            {
                OnPictureResetDefaults?.Invoke();
                // Reset slider visuals to factory defaults
                ResetPictureSliders();
            });
        }

        private void ResetPictureSliders()
        {
            _sharpnessSlider?.SetValueWithoutNotify(0.0f);
            _brightnessSlider?.SetValueWithoutNotify(1.0f);
            _saturationSlider?.SetValueWithoutNotify(1.0f);
            _contrastSlider?.SetValueWithoutNotify(1.0f);
            _tintSlider?.SetValueWithoutNotify(0f);
            _temperatureSlider?.SetValueWithoutNotify(0f);

            FormatValueLabel(_sharpnessValueLabel, _sharpnessSlider, 0.0f);
            FormatValueLabel(_brightnessValueLabel, _brightnessSlider, 1.0f);
            FormatValueLabel(_saturationValueLabel, _saturationSlider, 1.0f);
            FormatValueLabel(_contrastValueLabel, _contrastSlider, 1.0f);
            FormatValueLabel(_tintValueLabel, _tintSlider, 0f);
            FormatValueLabel(_temperatureValueLabel, _temperatureSlider, 0f);
        }
        #endregion

        #region Shared UI Components

        /// <summary>
        /// Creates a scrollable VerticalLayoutGroup inside a parent container.
        /// Returns the content transform to add children to.
        /// </summary>
        private Transform CreateScrollableContent(Transform parent)
        {
            // ScrollView
            GameObject scrollObj = new GameObject("ScrollView");
            scrollObj.transform.SetParent(parent, false);
            StretchFill(scrollObj);

            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.elasticity = 0.1f;
            scrollRect.scrollSensitivity = 30f;

            // Viewport
            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollObj.transform, false);
            StretchFill(viewportObj);
            viewportObj.AddComponent<RectMask2D>();

            var viewportImage = viewportObj.AddComponent<Image>();
            viewportImage.color = Color.clear;

            scrollRect.viewport = viewportObj.GetComponent<RectTransform>();

            // Content
            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform, false);

            var contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.offsetMin = new Vector2(0, 0);
            contentRT.offsetMax = new Vector2(0, 0);

            var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = _rowSpacing;
            contentLayout.padding = new RectOffset((int)_sideMargin, (int)_sideMargin, (int)SUB_PAGE_PADDING_TOP, 20);
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentCSF = contentObj.AddComponent<ContentSizeFitter>();
            contentCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRT;

            return contentObj.transform;
        }

        /// <summary>
        /// Create a slider row with absolute positioning:
        /// [Label] [PillArea: MinusBtn | Slider | PlusBtn] [ResetBtn]
        /// Pill and +/- buttons shown on hover. Value text below seeker, outside pill.
        /// </summary>
        private (VRSliderControl, TextMeshProUGUI) CreateSliderRow(
            Transform parent, string label,
            float min, float max, float defaultValue,
            Action<float> onChanged, Func<float, string> formatFunc = null,
            float customStep = 0f)
        {
            // === Layout dimensions (proportional to panel width) ===
            float pillWidth = _width * 0.532f;
            float pillLeft = _width * 0.3f - _sideMargin; // from row left edge
            float sliderWidth = _width * 0.35f;
            float btnAreaWidth = (pillWidth - sliderWidth) / 2f;
            float resetBtnSize = 56f;
            float labelWidth = _width * 0.25f;

            // === Row container (LayoutElement for parent VLG, no inner layout) ===
            GameObject rowObj = new GameObject($"SliderRow_{label}");
            rowObj.transform.SetParent(parent, false);

            var rowRT = rowObj.GetComponent<RectTransform>();
            if (rowRT == null) rowRT = rowObj.AddComponent<RectTransform>();

            var rowLE = rowObj.AddComponent<LayoutElement>();
            rowLE.minHeight = _rowHeight;
            rowLE.preferredHeight = _rowHeight;

            // === Label (left-aligned, full height) ===
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(rowObj.transform, false);
            var labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.offsetMin = new Vector2(0f, 0f);
            labelRT.offsetMax = new Vector2(labelWidth, 0f);

            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.font = _font;
            labelText.text = label;
            labelText.fontSize = 35;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = TEXT_COLOR;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.textWrappingMode = TextWrappingModes.Normal;
            labelText.raycastTarget = false;

            // === Pill area (absolute positioned, contains bg + buttons + slider) ===
            GameObject pillAreaObj = new GameObject("PillArea");
            pillAreaObj.transform.SetParent(rowObj.transform, false);
            var pillAreaRT = pillAreaObj.AddComponent<RectTransform>();
            pillAreaRT.anchorMin = new Vector2(0f, 0f);
            pillAreaRT.anchorMax = new Vector2(0f, 1f);
            pillAreaRT.pivot = new Vector2(0f, 0.5f);
            pillAreaRT.offsetMin = new Vector2(pillLeft, 0f);
            pillAreaRT.offsetMax = new Vector2(pillLeft + pillWidth, 0f);

            // --- Pill background (stretch fill with vertical inset, hidden by default) ---
            GameObject pillObj = new GameObject("PillBg");
            pillObj.transform.SetParent(pillAreaObj.transform, false);
            var pillRT = pillObj.AddComponent<RectTransform>();
            pillRT.anchorMin = new Vector2(0f, 0.1f);
            pillRT.anchorMax = new Vector2(1f, 0.9f);
            pillRT.offsetMin = Vector2.zero;
            pillRT.offsetMax = Vector2.zero;
            var pillImage = pillObj.AddComponent<Image>();
            pillImage.sprite = GetPillSprite();
            pillImage.type = Image.Type.Sliced;
            pillImage.color = PILL_BG_COLOR;
            pillImage.raycastTarget = false;
            pillObj.SetActive(false);

            // --- Minus button (centered in left btnArea, hidden by default) ---
            float stepSize = customStep > 0f ? customStep : (max - min) / 20f;
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
                sliderWidth, 40f,
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

            // Override slider RT to center it in the pill area
            var sliderRT = slider.GetComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0.5f, 0.5f);
            sliderRT.anchorMax = new Vector2(0.5f, 0.5f);
            sliderRT.pivot = new Vector2(0.5f, 0.5f);
            sliderRT.anchoredPosition = Vector2.zero;
            // sizeDelta already set by factory to (sliderWidth, 40)

            // --- Plus button (centered in right btnArea, hidden by default) ---
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
            Action<float> updateBtnStates = (v) =>
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

            // --- Value text (child of handle, below seeker, outside pill bg) ---
            var handleRT = slider.HandleTransform;
            GameObject valueObj = new GameObject("HoverValue");
            valueObj.transform.SetParent(handleRT, false);
            var valueRT = valueObj.AddComponent<RectTransform>();
            valueRT.anchoredPosition = new Vector2(0, -72f);
            valueRT.sizeDelta = new Vector2(160, 45);

            var valueText = valueObj.AddComponent<TextMeshProUGUI>();
            valueText.font = _font;
            formatFunc ??= (v) => v.ToString("F2");
            valueText.text = formatFunc(defaultValue);
            valueText.fontSize = 35;
            valueText.fontStyle = FontStyles.Bold;
            valueText.color = Color.white;
            valueText.alignment = TextAlignmentOptions.Center;
            valueText.overflowMode = TextOverflowModes.Overflow;
            valueText.raycastTarget = false;
            valueObj.SetActive(false);

            // === Hover group management ===
            var hoverGroup = pillAreaObj.AddComponent<SettingsSliderHoverGroup>();
            hoverGroup.Setup(pillObj, minusBtnObj, plusBtnObj, valueObj);

            // Connect slider hover events
            slider.OnHoverEnter += hoverGroup.OnChildHoverEnter;
            slider.OnHoverExit += hoverGroup.OnChildHoverExit;

            // Connect +/- button hover events
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

            // Store format function on slider for reuse in reset methods
            slider.OnFormatPreview = formatFunc;

            // === Value change callback ===
            Func<float, string> capturedFormat = formatFunc;
            slider.OnValueChanged += (v) =>
            {
                onChanged?.Invoke(v);
                if (valueText != null) valueText.text = capturedFormat(v);
            };

            // === Reset button (right-aligned in row) ===
            GameObject resetBtnObj = new GameObject("ResetBtn");
            resetBtnObj.transform.SetParent(rowObj.transform, false);
            var resetBtnRT = resetBtnObj.AddComponent<RectTransform>();
            resetBtnRT.anchorMin = new Vector2(1f, 0.5f);
            resetBtnRT.anchorMax = new Vector2(1f, 0.5f);
            resetBtnRT.pivot = new Vector2(1f, 0.5f);
            resetBtnRT.sizeDelta = new Vector2(resetBtnSize, resetBtnSize);
            resetBtnRT.anchoredPosition = Vector2.zero;

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
                if (capturedValueText != null) capturedValueText.text = capturedFormat(capturedDefault);
            });

            // Reset icon
            GameObject resetIconObj = new GameObject("Icon");
            resetIconObj.transform.SetParent(resetBtnObj.transform, false);
            var resetIconRT = resetIconObj.AddComponent<RectTransform>();
            resetIconRT.anchorMin = new Vector2(0.15f, 0.15f);
            resetIconRT.anchorMax = new Vector2(0.85f, 0.85f);
            resetIconRT.offsetMin = Vector2.zero;
            resetIconRT.offsetMax = Vector2.zero;

            var resetIcon = resetIconObj.AddComponent<Image>();
            resetIcon.sprite = Resources.Load<Sprite>(ICON_RESET);
            resetIcon.color = Color.white;
            resetIcon.preserveAspect = true;
            resetIcon.raycastTarget = false;

            // Hover
            var resetHover = resetBtnObj.AddComponent<HoverEffectController>();
            resetHover.AddEffect(new ScaleHoverEffect().WithHoverScale(1.2f).WithTransitionDuration(0.06f));

            var resetCol = resetBtnObj.AddComponent<BoxCollider>();
            resetCol.size = new Vector3(resetBtnSize * 1.5f, resetBtnSize * 1.5f, 10);
            resetCol.center = new Vector3(0, 0, -5);

            // Row-level hover detection via transparent Image (GraphicRaycaster requires Graphic)
            var rowImage = rowObj.AddComponent<Image>();
            rowImage.color = Color.clear;
            rowImage.raycastTarget = true;
            var rowNotifier = rowObj.AddComponent<HoverNotifier>();
            rowNotifier.onEnter += hoverGroup.OnChildHoverEnter;
            rowNotifier.onExit += hoverGroup.OnChildHoverExit;

            return (slider, valueText);
        }

        /// <summary>
        /// Create a +/- button with icon sprite for the slider hover area.
        /// Uses icon_plus or icon_minus sprites. Click handler wired by caller.
        /// </summary>
        private GameObject CreatePlusMinusButton(Transform parent, bool isPlus)
        {
            string name = isPlus ? "PlusBtn" : "MinusBtn";
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            // RectTransform will be configured by caller for absolute positioning
            var btnRT = btnObj.AddComponent<RectTransform>();

            // Background (transparent for interaction)
            var bgImage = btnObj.AddComponent<Image>();
            bgImage.color = Color.clear;
            bgImage.raycastTarget = true;

            // Icon
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(btnObj.transform, false);
            var iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.15f, 0.15f);
            iconRT.anchorMax = new Vector2(0.85f, 0.85f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            var iconImage = iconObj.AddComponent<Image>();
            iconImage.sprite = Resources.Load<Sprite>(isPlus ? ICON_PLUS : ICON_MINUS);
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            // Button component (click handler wired up by caller)
            var button = btnObj.AddComponent<Button>();
            button.targetGraphic = bgImage;
            button.transition = Selectable.Transition.None;

            // Hover notifier for delayed-hide group
            btnObj.AddComponent<HoverNotifier>();

            // Hover scale effect
            var hoverCtrl = btnObj.AddComponent<HoverEffectController>();
            hoverCtrl.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f).WithTransitionDuration(0.06f));

            // Collider for VR interaction
            var col = btnObj.AddComponent<BoxCollider>();
            col.size = new Vector3(PLUS_MINUS_BTN_SIZE * 1.3f, PLUS_MINUS_BTN_SIZE * 1.3f, 10);
            col.center = new Vector3(0, 0, -5);

            return btnObj;
        }

        /// <summary>
        /// Create a toggle row with optional icon, label, and custom toggle switch.
        /// </summary>
        private Toggle CreateToggleRow(Transform parent, string label, bool defaultValue, Action<bool> onChanged, string iconName = null)
        {
            GameObject rowObj = new GameObject($"ToggleRow_{label}");
            rowObj.transform.SetParent(parent, false);

            var rowLE = rowObj.AddComponent<LayoutElement>();
            rowLE.minHeight = _menuItemHeight;
            rowLE.preferredHeight = _menuItemHeight;

            // Background (synced with menu items)
            var bgImage = rowObj.AddComponent<Image>();
            bgImage.sprite = GetRoundedRectSprite();
            bgImage.type = Image.Type.Sliced;
            bgImage.color = new Color(0.18f, 0.18f, 0.20f, 0.85f);
            bgImage.raycastTarget = true;

            var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 25f;
            rowLayout.padding = new RectOffset(25, 20, 0, 0);
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            // Icon (optional, displayed before label)
            if (!string.IsNullOrEmpty(iconName))
            {
                float iconSize = Mathf.Round(_menuItemHeight * 0.196875f);
                GameObject iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(rowObj.transform, false);

                var iconLE = iconObj.AddComponent<LayoutElement>();
                iconLE.minWidth = iconSize;
                iconLE.minHeight = iconSize;
                iconLE.preferredWidth = iconSize;
                iconLE.preferredHeight = iconSize;

                var iconImage = iconObj.AddComponent<Image>();
                iconImage.sprite = Resources.Load<Sprite>(iconName);
                iconImage.color = TEXT_COLOR;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(rowObj.transform, false);

            var labelLE2 = labelObj.AddComponent<LayoutElement>();
            labelLE2.flexibleWidth = 1f;
            labelLE2.minHeight = _menuItemHeight;

            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.font = _font;
            labelText.text = label;
            labelText.fontSize = 35;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = TEXT_COLOR;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;

            // Toggle switch (iOS-style: white pill track, black circular knob)
            float trackH = Mathf.Round(_menuItemHeight * 0.2f);
            float trackW = Mathf.Round(trackH * 1.75f);
            float thumbPad = Mathf.Max(2f, Mathf.Round(trackH * 0.1f));
            float thumbSize = trackH - thumbPad * 2f;

            GameObject toggleObj = new GameObject("Toggle");
            toggleObj.transform.SetParent(rowObj.transform, false);

            var toggleLE = toggleObj.AddComponent<LayoutElement>();
            toggleLE.minWidth = trackW;
            toggleLE.minHeight = trackH;
            toggleLE.preferredWidth = trackW;
            toggleLE.preferredHeight = trackH;

            // Track background (sized pill sprite — texture height matches trackH for perfect semicircles)
            var trackImage = toggleObj.AddComponent<Image>();
            trackImage.sprite = GetSizedPillSprite(trackH);
            trackImage.type = Image.Type.Sliced;
            trackImage.color = Color.white;

            // Explicit RectTransform size (must be after AddComponent<Image> which creates the RectTransform)
            var toggleRT = toggleObj.GetComponent<RectTransform>();
            toggleRT.sizeDelta = new Vector2(trackW, trackH);

            // Thumb (black circular knob)
            GameObject thumbObj = new GameObject("Thumb");
            thumbObj.transform.SetParent(toggleObj.transform, false);

            var thumbRT = thumbObj.AddComponent<RectTransform>();
            thumbRT.sizeDelta = new Vector2(thumbSize, thumbSize);
            thumbRT.anchorMin = new Vector2(0, 0.5f);
            thumbRT.anchorMax = new Vector2(0, 0.5f);
            thumbRT.pivot = new Vector2(0.5f, 0.5f);
            float offX = thumbPad + thumbSize / 2f;
            thumbRT.anchoredPosition = new Vector2(defaultValue ? (trackW - offX) : offX, 0);

            var thumbImage = thumbObj.AddComponent<Image>();
            thumbImage.sprite = GetCircleSprite();
            thumbImage.preserveAspect = true;
            thumbImage.color = Color.black;
            thumbImage.raycastTarget = false;

            // Unity Toggle component
            var toggle = toggleObj.AddComponent<Toggle>();
            toggle.isOn = defaultValue;
            toggle.targetGraphic = trackImage;
            toggle.graphic = null;
            toggle.transition = Selectable.Transition.None;

            Color toggleOnColor = new Color(
                Mathf.Lerp(THEME_COLOR.r, 1f, 0.35f),
                Mathf.Lerp(THEME_COLOR.g, 1f, 0.35f),
                Mathf.Lerp(THEME_COLOR.b, 1f, 0.35f), 1f);

            toggle.onValueChanged.AddListener((val) =>
            {
                trackImage.color = val ? toggleOnColor : Color.white;
                thumbRT.anchoredPosition = new Vector2(val ? (trackW - offX) : offX, 0);
                onChanged?.Invoke(val);
            });

            if (defaultValue)
                trackImage.color = toggleOnColor;

            // Row-level button so clicking anywhere on the row toggles the switch
            var rowButton = rowObj.AddComponent<Button>();
            rowButton.targetGraphic = bgImage;
            rowButton.transition = Selectable.Transition.None;
            Toggle capturedToggle = toggle;
            rowButton.onClick.AddListener(() => capturedToggle.isOn = !capturedToggle.isOn);

            // Collider covers entire row
            float contentWidth = _width - _sideMargin * 2;
            var col = rowObj.AddComponent<BoxCollider>();
            col.size = new Vector3(contentWidth, _menuItemHeight, 10);
            col.center = new Vector3(0, 0, -5);

            // Hover effect on row background (no scale hover for Video adjustments rows)
            var hoverController = rowObj.AddComponent<HoverEffectController>();
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")
                .WithHoverColor(ITEM_HOVER_BG));

            return toggle;
        }

        /// <summary>
        /// Create an action button (Save defaults, Reset, etc.).
        /// </summary>
        private void CreateActionButton(Transform parent, string label, Action onClick)
        {
            float btnHeight = Mathf.Round(_rowHeight * 0.8f);

            GameObject btnObj = new GameObject($"Btn_{label.Replace(" ", "")}");
            btnObj.transform.SetParent(parent, false);

            var btnLE = btnObj.AddComponent<LayoutElement>();
            btnLE.minHeight = btnHeight;
            btnLE.preferredHeight = btnHeight;

            var bgImage = btnObj.AddComponent<Image>();
            bgImage.sprite = GetPillSprite();
            bgImage.type = Image.Type.Sliced;
            bgImage.color = HEADER_BG;

            var button = btnObj.AddComponent<Button>();
            button.targetGraphic = bgImage;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick?.Invoke());

            // Label
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            var textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.font = _font;
            tmp.text = label;
            tmp.fontSize = 35;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = TEXT_COLOR;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            // Hover
            var hoverController = btnObj.AddComponent<HoverEffectController>();
            hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f).WithTransitionDuration(0.06f));
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")
                .WithHoverColor(new Color(0.18f, 0.18f, 0.20f, 0.95f)));

            // Collider
            var col = btnObj.AddComponent<BoxCollider>();
            col.size = new Vector3(_width - _sideMargin * 2, btnHeight, 10);
            col.center = new Vector3(0, 0, -5);
        }

        private void CreateFixedSpacer(Transform parent, float height)
        {
            GameObject spacer = new GameObject("Spacer");
            spacer.transform.SetParent(parent, false);

            var le = spacer.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }
        #endregion
    }
}
