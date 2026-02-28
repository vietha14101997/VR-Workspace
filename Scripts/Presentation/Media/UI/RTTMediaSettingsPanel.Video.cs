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
        #region Sub-page: Video Adjustments
        private void CreateVideoAdjPage(Transform parent)
        {
            _videoAdjContainer = new GameObject("VideoAdjustments");
            _videoAdjContainer.transform.SetParent(parent, false);
            StretchFill(_videoAdjContainer);
            _videoAdjContainer.SetActive(false);

            var scrollContent = CreateScrollableContent(_videoAdjContainer.transform);

            // === Common controls (both Flat and Immersive) ===

            // 3D Toggle (with icon)
            _3dToggle = CreateToggleRow(scrollContent, "3D", true,
                (v) => On3DChanged?.Invoke(v), ICON_3D);

            // LR Inverse Toggle (with icon)
            _lrInverseToggle = CreateToggleRow(scrollContent, "LR Inverse", false,
                (v) => OnLRInverseChanged?.Invoke(v), ICON_REVERSE);

            // Speed segment buttons
            CreateSpeedRow(scrollContent);

            // === Flat-only content ===
            _videoAdjFlatContent = new GameObject("FlatContent");
            _videoAdjFlatContent.transform.SetParent(scrollContent, false);
            var flatLE = _videoAdjFlatContent.AddComponent<LayoutElement>();
            flatLE.minHeight = 0;
            flatLE.preferredHeight = 0;
            // Flat has no extra controls beyond 3D/LR/Speed

            // === Immersive-only content ===
            _videoAdjImmersiveContent = new GameObject("ImmersiveContent");
            _videoAdjImmersiveContent.transform.SetParent(scrollContent, false);

            var immLayout = _videoAdjImmersiveContent.AddComponent<VerticalLayoutGroup>();
            immLayout.spacing = _rowSpacing;
            immLayout.padding = new RectOffset(0, 0, 10, 0);
            immLayout.childControlWidth = true;
            immLayout.childControlHeight = false;
            immLayout.childForceExpandWidth = true;
            immLayout.childForceExpandHeight = false;

            var immCSF = _videoAdjImmersiveContent.AddComponent<ContentSizeFitter>();
            immCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Tilt slider (-90 to 90, default 0) → display normalized -1.1 to 1.1
            (_tiltSlider, _tiltValueLabel) = CreateSliderRow(
                _videoAdjImmersiveContent.transform, "Tilt", -90f, 90f, 0f,
                (v) => OnTiltChanged?.Invoke(v),
                (v) => (v / 90f * 1.1f).ToString("0.#"));

            // Yaw slider (-180 to 180, default 0) → display normalized -1.1 to 1.1
            (_yawSlider, _yawValueLabel) = CreateSliderRow(
                _videoAdjImmersiveContent.transform, "Yaw", -180f, 180f, 0f,
                (v) => OnYawChanged?.Invoke(v),
                (v) => (v / 180f * 1.1f).ToString("0.#"));

            // Roll slider (-180 to 180, default 0) → display normalized -1.1 to 1.1
            (_rollSlider, _rollValueLabel) = CreateSliderRow(
                _videoAdjImmersiveContent.transform, "Roll", -180f, 180f, 0f,
                (v) => OnRollChanged?.Invoke(v),
                (v) => (v / 180f * 1.1f).ToString("0.#"));

            // Zoom slider (180 to 420, default 300) → display normalized -1.1 to 1.1
            (_zoomSlider, _zoomValueLabel) = CreateSliderRow(
                _videoAdjImmersiveContent.transform, "Zoom", 180f, 420f, 300f,
                (v) => OnZoomChanged?.Invoke(v),
                (v) => ((v - 300f) / 120f * 1.1f).ToString("0.#"));

            // Height slider (-1 to 1, default 0) → display normalized -1.1 to 1.1
            (_immHeightSlider, _immHeightValueLabel) = CreateSliderRow(
                _videoAdjImmersiveContent.transform, "Height", -1f, 1f, 0f,
                (v) => OnHeightChanged?.Invoke(v),
                (v) => (v * 1.1f).ToString("0.#"));

            // Horizontal balance slider (-1 to 1, default 0) → display 3 decimal places
            (_hBalanceSlider, _hBalanceValueLabel) = CreateSliderRow(
                _videoAdjImmersiveContent.transform, "H. Balance", -1f, 1f, 0f,
                (v) => OnHorizontalBalanceChanged?.Invoke(v),
                (v) => v.ToString("0.###"),
                0.001f);

            // Set initial mode
            _videoAdjFlatContent.SetActive(!_isImmersive);
            _videoAdjImmersiveContent.SetActive(_isImmersive);
        }

        private void CreateSpeedRow(Transform parent)
        {
            // Outer container with VLG and ContentSizeFitter for auto height
            GameObject rowObj = new GameObject("SpeedRow");
            rowObj.transform.SetParent(parent, false);

            var rowLayout = rowObj.AddComponent<VerticalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.padding = new RectOffset(0, 0, 10, 0);
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            var rowCSF = rowObj.AddComponent<ContentSizeFitter>();
            rowCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Label
            GameObject labelObj = new GameObject("SpeedLabel");
            labelObj.transform.SetParent(rowObj.transform, false);

            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.minHeight = 45f;
            labelLE.preferredHeight = 45f;

            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.font = _font;
            labelText.text = "Speed";
            labelText.fontSize = 35;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = TEXT_COLOR;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;

            // Segment container with background
            float segHeight = _menuItemHeight * 0.49f;
            GameObject segContainer = new GameObject("SpeedSegments");
            segContainer.transform.SetParent(rowObj.transform, false);

            var segLE = segContainer.AddComponent<LayoutElement>();
            segLE.minHeight = segHeight;
            segLE.preferredHeight = segHeight;

            var segBg = segContainer.AddComponent<Image>();
            segBg.sprite = GetSizedPillSprite(segHeight);
            segBg.type = Image.Type.Sliced;
            segBg.color = new Color(0.20f, 0.20f, 0.22f, 1.0f);

            var segLayout = segContainer.AddComponent<HorizontalLayoutGroup>();
            segLayout.spacing = 0;
            segLayout.padding = new RectOffset(0, 0, 0, 0);
            segLayout.childAlignment = TextAnchor.MiddleCenter;
            segLayout.childControlWidth = true;
            segLayout.childControlHeight = true;
            segLayout.childForceExpandWidth = true;
            segLayout.childForceExpandHeight = true;

            // Speed options
            float[] speeds = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
            _speedButtons.Clear();

            foreach (float speed in speeds)
            {
                CreateSpeedButton(segContainer.transform, speed, segHeight);
            }

            UpdateSpeedButtonSelection();
        }

        private static readonly Color SPEED_SELECTED_BG = new Color(0.15f, 0.15f, 0.17f, 0.7f);
        private static readonly Color SPEED_HOVER_BG = new Color(0.28f, 0.28f, 0.30f, 0.6f);

        private void CreateSpeedButton(Transform parent, float speed, float btnHeight)
        {
            string label = speed == 1f ? "1" :
                           speed < 1f ? speed.ToString("0.##").Replace('.', ',') :
                           speed.ToString("0.##").Replace('.', ',');

            GameObject btnObj = new GameObject($"Speed_{label}");
            btnObj.transform.SetParent(parent, false);

            // Background (pill sprite, Sliced mode fills cell width with rounded ends)
            var bgImage = btnObj.AddComponent<Image>();
            bgImage.sprite = GetSizedPillSprite(btnHeight);
            bgImage.type = Image.Type.Sliced;
            bgImage.color = Color.clear;

            var button = btnObj.AddComponent<Button>();
            button.targetGraphic = bgImage;
            button.transition = Selectable.Transition.None;

            float capturedSpeed = speed;
            button.onClick.AddListener(() =>
            {
                _currentSpeed = capturedSpeed;
                UpdateSpeedButtonSelection();
                OnSpeedChanged?.Invoke(capturedSpeed);
            });

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

            // Hover effect (background only, not text)
            var hoverController = btnObj.AddComponent<HoverEffectController>();
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")
                .WithHoverColor(SPEED_HOVER_BG));

            // Collider
            var col = btnObj.AddComponent<BoxCollider>();
            col.size = new Vector3(btnHeight, btnHeight, 10);
            col.center = new Vector3(0, 0, -5);

            _speedButtons.Add(button);
        }

        private void UpdateSpeedButtonSelection()
        {
            Color selectedTextColor = new Color(
                Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
                Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
                Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f), 1f);

            float[] speeds = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
            for (int i = 0; i < _speedButtons.Count && i < speeds.Length; i++)
            {
                bool isSelected = Mathf.Approximately(speeds[i], _currentSpeed);
                var bg = _speedButtons[i].GetComponent<Image>();
                var text = _speedButtons[i].GetComponentInChildren<TextMeshProUGUI>();

                // Selected: dark transparent bg + colored text; Unselected: clear bg + white text
                Color bgColor = isSelected ? SPEED_SELECTED_BG : Color.clear;
                if (bg != null) bg.color = bgColor;
                if (text != null) text.color = isSelected ? selectedTextColor : TEXT_COLOR;

                // Update hover effect original color so pointer exit restores correct state
                var hoverCtrl = _speedButtons[i].GetComponent<HoverEffectController>();
                if (hoverCtrl != null)
                {
                    var colorEffect = hoverCtrl.GetEffect("color") as ColorHoverEffect;
                    colorEffect?.SetOriginalColor(bgColor);
                }
            }
        }
        #endregion

        #region Sub-page: Screen Settings
        private void CreateScreenSettingsPage(Transform parent)
        {
            _screenSettingsContainer = new GameObject("ScreenSettings");
            _screenSettingsContainer.transform.SetParent(parent, false);
            StretchFill(_screenSettingsContainer);
            _screenSettingsContainer.SetActive(false);

            var scrollContent = CreateScrollableContent(_screenSettingsContainer.transform);

            // Aspect ratio segment buttons
            CreateAspectRatioRow(scrollContent);

            // Depth slider (1.0 - 3.0, default 1.8, step 0.1)
            (_depthSlider, _depthValueLabel) = CreateSliderRow(
                scrollContent, "Depth", 1.0f, 3.0f, 1.8f,
                (v) => OnScreenDepthChanged?.Invoke(v),
                (v) => v.ToString("0.#"),
                0.1f);

            // Scale slider (0.5 - 3.0, default 1.0) → display 1 decimal, no unit
            (_scaleSlider, _scaleValueLabel) = CreateSliderRow(
                scrollContent, "Scale", 0.5f, 3.0f, 1.0f,
                (v) => OnScreenScaleChanged?.Invoke(v),
                (v) => v.ToString("0.#"));

            // Vertical move slider (-1.0 - 1.0, default 0) → display 3 decimal, no unit
            (_verticalMoveSlider, _verticalMoveValueLabel) = CreateSliderRow(
                scrollContent, "Vertical move", -1.0f, 1.0f, 0f,
                (v) => OnVerticalMoveChanged?.Invoke(v),
                (v) => v.ToString("0.###"),
                0.025f);

            // Spacer
            CreateFixedSpacer(scrollContent, 20f);

            // Reset button
            CreateActionButton(scrollContent, "Reset to defaults", () =>
            {
                OnScreenSettingsReset?.Invoke();
                ResetScreenSliders();
            });
        }

        private void ResetScreenSliders()
        {
            _depthSlider?.SetValueWithoutNotify(1.8f);
            _scaleSlider?.SetValueWithoutNotify(1.0f);
            _verticalMoveSlider?.SetValueWithoutNotify(0f);

            FormatValueLabel(_depthValueLabel, _depthSlider, 1.8f);
            FormatValueLabel(_scaleValueLabel, _scaleSlider, 1.0f);
            FormatValueLabel(_verticalMoveValueLabel, _verticalMoveSlider, 0f);

            _currentAspect = "default";
            UpdateAspectButtonSelection();
        }

        private void CreateAspectRatioRow(Transform parent)
        {
            float gridSpacing = Mathf.Round(_width / 24f);
            float contentWidth = _width - 2f * _sideMargin;
            float btnWidth = Mathf.Round((contentWidth - 2f * gridSpacing) / 3f);
            float btnHeight = Mathf.Round(btnWidth / 3f);
            float labelHeight = 45f;
            float gridHeight = btnHeight * 2f + gridSpacing;
            float totalHeight = labelHeight + gridSpacing + gridHeight;

            GameObject rowObj = new GameObject("AspectRatioRow");
            rowObj.transform.SetParent(parent, false);

            var rowLE = rowObj.AddComponent<LayoutElement>();
            rowLE.minHeight = totalHeight;
            rowLE.preferredHeight = totalHeight;

            var rowLayout = rowObj.AddComponent<VerticalLayoutGroup>();
            rowLayout.spacing = gridSpacing;
            rowLayout.padding = new RectOffset(0, 0, 0, 0);
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            // Force self-sizing so parent VLG (childControlHeight=false) gets correct height
            var rowCSF = rowObj.AddComponent<ContentSizeFitter>();
            rowCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Label
            GameObject labelObj = new GameObject("AspectLabel");
            labelObj.transform.SetParent(rowObj.transform, false);

            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.minHeight = labelHeight;
            labelLE.preferredHeight = labelHeight;

            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.font = _font;
            labelText.text = "Screen by aspect ratio";
            labelText.fontSize = 35;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = TEXT_COLOR;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;

            // Grid container (3 columns, auto rows)
            GameObject gridObj = new GameObject("AspectGrid");
            gridObj.transform.SetParent(rowObj.transform, false);

            var gridLE = gridObj.AddComponent<LayoutElement>();
            gridLE.minHeight = gridHeight;
            gridLE.preferredHeight = gridHeight;

            var grid = gridObj.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(btnWidth, btnHeight);
            grid.spacing = new Vector2(gridSpacing, gridSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperLeft;

            // Create buttons inside grid
            CreateAspectButton(gridObj.transform, "Default", "default", btnWidth, btnHeight);
            CreateAspectButton(gridObj.transform, "3 : 2", "3:2", btnWidth, btnHeight);
            CreateAspectButton(gridObj.transform, "4 : 3", "4:3", btnWidth, btnHeight);
            CreateAspectButton(gridObj.transform, "9 : 16", "9:16", btnWidth, btnHeight);
            CreateAspectButton(gridObj.transform, "16 : 9", "16:9", btnWidth, btnHeight);
            CreateAspectButton(gridObj.transform, "21 : 9", "21:9", btnWidth, btnHeight);

            UpdateAspectButtonSelection();
        }

        private void CreateAspectButton(Transform parent, string label, string aspectValue, float btnWidth, float btnHeight)
        {
            GameObject btnObj = new GameObject($"Aspect_{aspectValue}");
            btnObj.transform.SetParent(parent, false);

            // Rounded rect background
            var bgImage = btnObj.AddComponent<Image>();
            bgImage.sprite = GetRoundedRectSprite();
            bgImage.type = Image.Type.Sliced;
            bgImage.color = ITEM_BG;

            var button = btnObj.AddComponent<Button>();
            button.targetGraphic = bgImage;
            button.transition = Selectable.Transition.None;

            string capturedValue = aspectValue;
            button.onClick.AddListener(() =>
            {
                _currentAspect = capturedValue;
                UpdateAspectButtonSelection();
                OnAspectRatioChanged?.Invoke(capturedValue);
            });

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

            // Hover effects
            var hoverController = btnObj.AddComponent<HoverEffectController>();
            hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f).WithTransitionDuration(0.06f));
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")
                .WithHoverColor(ITEM_HOVER_BG));

            // Collider
            var col = btnObj.AddComponent<BoxCollider>();
            col.size = new Vector3(btnWidth, btnHeight, 10);
            col.center = new Vector3(0, 0, -5);

            _aspectButtons.Add(button);
        }

        private void UpdateAspectButtonSelection()
        {
            Color selectedTextColor = new Color(
                Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
                Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
                Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f),
                1f);

            string[] values = { "default", "3:2", "4:3", "9:16", "16:9", "21:9" };
            for (int i = 0; i < _aspectButtons.Count && i < values.Length; i++)
            {
                bool isSelected = values[i] == _currentAspect;
                var bg = _aspectButtons[i].GetComponent<Image>();
                var text = _aspectButtons[i].GetComponentInChildren<TextMeshProUGUI>();

                // Selected: text color changes, bg stays same; not clickable, no hover
                if (bg != null) bg.color = ITEM_BG;
                if (text != null) text.color = isSelected ? selectedTextColor : TEXT_COLOR;
                _aspectButtons[i].interactable = !isSelected;

                var hoverCtrl = _aspectButtons[i].GetComponent<HoverEffectController>();
                if (hoverCtrl != null) hoverCtrl.enabled = !isSelected;
            }
        }
        #endregion
    }
}
