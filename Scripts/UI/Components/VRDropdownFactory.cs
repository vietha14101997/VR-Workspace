using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Utilities;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.Components
{
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
        private static Sprite _horizontalFadeSprite;

        // Hằng số layout - now using UIConstants where possible
        private const float FONT_TO_BOX_RATIO = UIConstants.DropdownFontToBoxRatio;
        private const float FONT_TO_ICON_RATIO = UIConstants.DropdownFontToIconRatio;
        private const float ICON_ZONE_RATIO = UIConstants.DropdownIconZoneRatio;
        private const float CONTENT_PADDING = UIConstants.DropdownContentPadding;
        private const float ARROW_WIDTH = UIConstants.DropdownArrowWidth;
        private const float CONTENT_LEFT_OFFSET = UIConstants.DropdownContentLeftOffset;
        private const float ARROW_RIGHT_OFFSET = UIConstants.DropdownArrowRightOffset;

        /// <summary>
        /// Cấu hình cho Dropdown
        /// </summary>
        [System.Serializable]
        public class DropdownConfig
        {
            public string label = "Label";
            public Sprite icon;
            public Color themeColor = UIConstants.DefaultPrimaryColor;
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
            public float cornerRadius = UIConstants.ButtonCornerRadius;
            public float edgePadding = UIConstants.ButtonEdgePadding;
            public float backgroundAlpha = UIConstants.ButtonBackgroundAlpha;
            public float borderWidth = UIConstants.DropdownBorderWidth;
            public float glowWidth = UIConstants.ButtonGlowWidth;
            public float glowIntensity = UIConstants.ButtonGlowIntensity;

            // Glassmorphism settings (uses GlassGradientBackgroundOverlay with higher Queue)
            public bool enableGlassmorphism = true;
            public float blurIntensity = 6;
            public int blurQuality = 8;
            public float glassOpacity = 0f;
            public float tintStrength = 0.1f;
            public float innerGlow = 0f;
            public float brightness = 1f;
            public float saturation = 1f;

            // Animation
            public float popAmount = UIConstants.DropdownPopAmount;

            // Dropdown panel
            public int maxVisibleOptions = UIConstants.DropdownMaxVisibleOptions;

            // Layer
            public string layerName = UIConstants.VirtualObjectsLayer;

            // Tính chiều cao box từ font size
            public float BoxHeight => valueFontSize * (string.IsNullOrEmpty(label) ? 1.8f : FONT_TO_BOX_RATIO);

            // Tính icon size từ font size
            public float IconSize => valueFontSize * FONT_TO_ICON_RATIO * 0.95f;

            // Tính chiều cao option = 0.75 chiều cao dropdown
            public float OptionHeight => BoxHeight * 0.75f;

            // Tính icon size trong option (tương ứng với chiều cao option)
            public float OptionIconSize => OptionHeight * 0.5f;
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
            RectTransform hitRT = UIElementBuilder.CreateFullStretch(hitArea, wrapper.transform);

            // Setup hit area with utilities
            var (hitImg, col) = UIElementBuilder.SetupHitArea(hitArea, config.width, boxHeight, config.layerName);

            // 3. Visuals - container cho visual elements
            GameObject visuals = new GameObject("Visuals");
            UIElementBuilder.CreateFullStretch(visuals, hitArea.transform);

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

            // 8. Hover effects - using unified HoverEffectController
            HoverEffectController hoverController = hitArea.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = visuals.transform;

            // Add glow border and background effects
            hoverController.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true)
                .WithBorderMultiplier(1f));

            hoverController.AddEffect(new BackgroundHoverEffect());

            if (config.popAmount > 0)
            {
                hoverController.AddEffect(new ZPopHoverEffect()
                    .WithPopAmount(config.popAmount));
            }

            // 9. VRButtonAnimation for ripple click effect
            VRButtonAnimation anim = hitArea.AddComponent<VRButtonAnimation>();
            anim.targetVisuals = visuals.transform;

            // 10. VRDropdown component TRƯỚC khi tạo panel
            VRDropdown dropdown = wrapper.AddComponent<VRDropdown>();

            // 11. Dropdown Panel
            GameObject dropdownPanel = CreateDropdownPanel(wrapper.transform, config, valueTxt, onValueChanged);
            dropdownPanel.SetActive(false);

            // 12. Initialize dropdown component
            dropdown.Initialize(config.options, config.defaultIndex, valueTxt, dropdownPanel, onValueChanged);

            // 13. Toggle dropdown on click (use ToggleDropdown to handle force hover)
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

        /// <summary>
        /// Đặt danh sách options mới cho dropdown
        /// </summary>
        public static void SetOptions(GameObject wrapper, List<string> options, int selectedIndex = 0)
        {
            var dropdown = GetDropdown(wrapper);
            if (dropdown != null)
            {
                dropdown.SetOptions(options, selectedIndex);
            }
        }

        /// <summary>
        /// Enable/disable dropdown interaction với visual feedback
        /// </summary>
        public static void SetInteractable(GameObject wrapper, bool interactable)
        {
            if (wrapper == null) return;

            // Find HitArea button
            var hitArea = wrapper.transform.Find("HitArea");
            if (hitArea != null)
            {
                var button = hitArea.GetComponent<Button>();
                if (button != null) button.interactable = interactable;
            }

            // Visual feedback - dim when disabled
            var canvasGroup = wrapper.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = wrapper.AddComponent<CanvasGroup>();
            canvasGroup.alpha = interactable ? 1f : 0.5f;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
        }

        // ==================== INTERNAL HELPERS ====================

        private static Image CreateBackground(Transform parent, DropdownConfig config)
        {
            GameObject bgObj = new GameObject("Background");
            RectTransform rt = UIElementBuilder.CreateFullStretch(bgObj, parent);

            Image img = bgObj.AddComponent<Image>();
            img.sprite = SpriteUtility.GetPixelSprite();
            img.raycastTarget = false;

            float aspect = MaterialFactory.CalculateAspect(config.width, config.BoxHeight);
            Color col = config.themeColor;

            // Use MaterialFactory to create glass background with gradient
            Material mat = MaterialFactory.CreateGlassBackgroundWithGradient(
                config.cornerRadius,
                config.edgePadding,
                aspect,
                col,
                config.backgroundAlpha,
                gradientOffset: 0f,
                gradientAngle: -10f,
                cyanRatio: 0.7f,
                fresnelPower: 2.2f,
                fresnelStrength: 0.12f
            );

            if (mat != null)
            {
                img.material = mat;
                img.color = Color.white;
            }
            else
            {
                // Fallback
                img.color = new Color(col.r, col.g, col.b, config.backgroundAlpha);
            }

            return img;
        }

        private static void CreateBorder(Transform parent, DropdownConfig config)
        {
            GameObject borderObj = new GameObject("Border");
            RectTransform rt = UIElementBuilder.CreateFullStretch(borderObj, parent);

            Image img = borderObj.AddComponent<Image>();
            img.sprite = SpriteUtility.GetPixelSprite();
            img.raycastTarget = false;

            float aspect = MaterialFactory.CalculateAspect(config.width, config.BoxHeight);
            Color col = config.themeColor;

            // Use MaterialFactory to create glow border
            Material mat = MaterialFactory.CreateGlowBorder(
                aspect,
                config.cornerRadius,
                config.edgePadding,
                col,
                config.borderWidth,
                config.glowWidth,
                config.glowIntensity,
                enablePulse: false
            );

            if (mat != null)
            {
                img.material = mat;

                // VRButtonRipple cho ripple effect
                borderObj.AddComponent<VRButtonRipple>().Initialize(mat, img);
            }
        }

        private static TextMeshProUGUI CreateContent(Transform parent, DropdownConfig config)
        {
            GameObject content = new GameObject("Content");
            RectTransform cRT = UIElementBuilder.CreateFullStretch(content, parent);

            bool hasIcon = config.icon != null;
            bool hasLabel = !string.IsNullOrEmpty(config.label);
            float iconSize = config.IconSize;

            // Layout: 1/3 trái cho icon, 2/3 phải cho content
            float iconZoneRatio = hasIcon ? ICON_ZONE_RATIO : 0f;

            // ==================== KHU VỰC ICON (1/3 bên trái) ====================
            if (hasIcon)
            {
                // Icon zone container - lùi sang phải 10%
                GameObject iconZone = new GameObject("IconZone");
                RectTransform iconZoneRT = UIElementBuilder.CreateAnchored(iconZone, content.transform,
                    new Vector2(CONTENT_LEFT_OFFSET, 0f),
                    new Vector2(iconZoneRatio + CONTENT_LEFT_OFFSET, 1f),
                    Vector2.zero,
                    Vector2.zero);

                // Icon đặt ở chính giữa khu vực icon
                GameObject iconObj = new GameObject("Icon");
                RectTransform iconRT = UIElementBuilder.CreateAnchoredPosition(iconObj, iconZone.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(iconSize, iconSize),
                    new Vector2(0, hasLabel ? iconSize * 0.06f : 0)); // Center if no label

                Image iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = config.icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                iconImg.color = UIGlowEffects.CreateIconTintColor(config.themeColor, UIConstants.IconTintWhiteMix);

                // Icon glow effects using UIGlowEffects utility
                UIGlowEffects.AddIconBloomGlow(iconObj, config.themeColor,
                    innerDistance: UIConstants.GlowInnerDistance,
                    outerDistance: UIConstants.GlowOuterDistance,
                    innerAlpha: UIConstants.GlowInnerAlpha,
                    outerAlpha: UIConstants.GlowOuterAlpha);
            }

            // ==================== KHU VỰC CONTENT (2/3 bên phải) - lùi sang phải 10% ====================
            GameObject contentZone = new GameObject("ContentZone");
            RectTransform contentZoneRT = UIElementBuilder.CreateAnchored(contentZone, content.transform,
                new Vector2(iconZoneRatio + CONTENT_LEFT_OFFSET, 0f),
                new Vector2(1f - ARROW_RIGHT_OFFSET, 1f),
                new Vector2(CONTENT_PADDING, 0f),
                new Vector2(-CONTENT_PADDING, 0f));

            if (hasLabel)
            {
                // Nửa trên: Title (Label) - đẩy lên trên để cách xa value
                GameObject labelObj = new GameObject("Label");
                RectTransform labelRT = UIElementBuilder.CreateAnchored(labelObj, contentZone.transform,
                    new Vector2(0f, 0.55f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 0f),
                    new Vector2(0f, -8f));

                TextMeshProUGUI labelTxt = labelObj.AddComponent<TextMeshProUGUI>();
                labelTxt.text = config.label;
                labelTxt.fontSize = config.labelFontSize;
                labelTxt.color = new Color(1f, 1f, 1f, 1f);
                labelTxt.alignment = TextAlignmentOptions.BottomLeft;
                labelTxt.verticalAlignment = VerticalAlignmentOptions.Bottom;
                labelTxt.fontStyle = FontStyles.Bold;
                labelTxt.raycastTarget = false;
                labelTxt.textWrappingMode = TextWrappingModes.NoWrap;
                labelTxt.overflowMode = TextOverflowModes.Ellipsis;
                if (config.font != null) labelTxt.font = config.font;
            }

            // Nửa dưới (hoặc Full nếu no label): Container cho Value + Arrow
            GameObject bottomRow = new GameObject("BottomRow");
            bottomRow.transform.SetParent(contentZone.transform, false);
            RectTransform bottomRowRT = bottomRow.AddComponent<RectTransform>();

            if (hasLabel)
            {
                bottomRowRT.anchorMin = Vector2.zero;
                bottomRowRT.anchorMax = new Vector2(1f, 0.45f);
                bottomRowRT.offsetMin = new Vector2(0f, 8f);
                bottomRowRT.offsetMax = new Vector2(0f, 0f);
            }
            else
            {
                // Full height, centered
                UIElementBuilder.ApplyFullStretch(bottomRowRT);
            }

            // Value text - chiếm hầu hết nửa dưới, chừa chỗ cho arrow
            GameObject valueObj = new GameObject("Value");
            RectTransform valueRT = UIElementBuilder.CreateAnchored(valueObj, bottomRow.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                new Vector2(-ARROW_WIDTH, 0f));

            TextMeshProUGUI valueTxt = valueObj.AddComponent<TextMeshProUGUI>();
            string defaultValue = config.options.Count > config.defaultIndex ? config.options[config.defaultIndex] : "";
            valueTxt.text = defaultValue;
            valueTxt.fontSize = config.valueFontSize;
            valueTxt.color = Color.white;
            valueTxt.fontStyle = FontStyles.Bold;
            if (hasLabel)
            {
                valueTxt.alignment = TextAlignmentOptions.Left;
                valueTxt.verticalAlignment = VerticalAlignmentOptions.Top;
            }
            else
            {
                valueTxt.alignment = TextAlignmentOptions.Left;
                valueTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
            }
            valueTxt.raycastTarget = false;
            valueTxt.textWrappingMode = TextWrappingModes.NoWrap;
            valueTxt.overflowMode = TextOverflowModes.Ellipsis;
            if (config.font != null) valueTxt.font = config.font;

            // Arrow indicator - cố định ở cạnh phải
            GameObject arrowObj = new GameObject("Arrow");
            arrowObj.transform.SetParent(bottomRow.transform, false);
            RectTransform arrowRT = arrowObj.AddComponent<RectTransform>();

            if (hasLabel)
            {
                // Top-right alignment for 2-row layout
                arrowRT.anchorMin = new Vector2(1f, 1f);
                arrowRT.anchorMax = new Vector2(1f, 1f);
                arrowRT.pivot = new Vector2(1f, 1f);
            }
            else
            {
                // Middle-right alignment for single-row layout
                arrowRT.anchorMin = new Vector2(1f, 0.5f);
                arrowRT.anchorMax = new Vector2(1f, 0.5f);
                arrowRT.pivot = new Vector2(1f, 0.5f);
            }

            arrowRT.anchoredPosition = Vector2.zero;
            arrowRT.sizeDelta = new Vector2(ARROW_WIDTH, 0f);

            // Sử dụng arrow sprite màu trắng được generate để có thể tint hoàn toàn
            float arrowSize = config.valueFontSize;

            Image arrowImg = arrowObj.AddComponent<Image>();
            arrowImg.sprite = SpriteUtility.GetArrowSprite();
            arrowImg.preserveAspect = true;
            arrowImg.raycastTarget = false;

            // Màu arrow = màu border (theme color lerp với white để sáng và nổi bật)
            Color arrowColor = Color.Lerp(config.themeColor, Color.white, 0.85f);
            arrowImg.color = arrowColor;

            // Set kích thước và vị trí arrow (pivot ở góc trên phải)
            arrowRT.sizeDelta = new Vector2(arrowSize, arrowSize);
            arrowRT.anchoredPosition = new Vector2(-ARROW_WIDTH / 2f + arrowSize / 2f, 0f);

            // Glow effect để nổi bật khỏi nền using UIGlowEffects utility
            UIGlowEffects.AddIconBloomGlow(arrowObj, config.themeColor,
                innerDistance: 2f,
                outerDistance: 4f,
                innerAlpha: 0.4f,
                outerAlpha: 0.2f);

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

            panelRT.anchorMin = new Vector2(0.5f, 0f);
            panelRT.anchorMax = new Vector2(0.5f, 0f);
            panelRT.pivot = new Vector2(0.5f, 1f);
            panelRT.anchoredPosition = new Vector2(0, -15f); // Reduced gap slightly

            // Ensure panel is wide enough for options (e.g. "Date Modified")
            // Even if the button is narrow (220f).
            float panelWidth = Mathf.Max(config.width, 320f);
            panelRT.sizeDelta = new Vector2(panelWidth, panelHeight);

            // Add Canvas FIRST to handle sorting without breaking VR raycast
            Canvas panelCanvas = panel.AddComponent<Canvas>();
            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = 100;
            // Add GraphicRaycaster for RTT support
            // RTT mode uses RTTRaycastManager which needs GraphicRaycaster on nested Canvases
            // to properly raycast into dropdown options
            panel.AddComponent<GraphicRaycaster>();

            // NO Image on panel - background is now on Viewport

            // BoxCollider cho VR raycast (non-RTT mode)
            // Note: In RTT mode, BoxCollider is not used - RTTRaycastManager uses GraphicRaycaster instead
            BoxCollider panelCol = panel.AddComponent<BoxCollider>();
            panelCol.size = new Vector3(config.width, panelHeight, UIConstants.ColliderDepth);
            panelCol.center = new Vector3(0, -panelHeight / 2f, -0.05f);

            // Set layer to match parent Canvas for RTT compatibility
            // RTT Camera only renders UI layer - must inherit from parent, not use VirtualObjects
            // New GameObjects default to layer 0 (Default), so we need to explicitly set it
            Canvas parentCanvas = parent.GetComponentInParent<Canvas>();
            int renderLayer = parentCanvas != null ? parentCanvas.gameObject.layer : panel.layer;
            panel.layer = renderLayer;

            // Viewport - contains everything
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(panel.transform, false);
            viewport.layer = renderLayer;
            RectTransform viewportRT = UIElementBuilder.CreateFullStretch(viewport, panel.transform);

            // Visuals container - expanded beyond Viewport for border effect
            // Scale expansion based on height ratio to maintain visual consistency
            float heightRatio = config.BoxHeight / panelHeight;
            float adjustedExpansion = config.edgePadding * heightRatio;

            GameObject viewportVisuals = new GameObject("Visuals");
            viewportVisuals.transform.SetParent(viewport.transform, false);
            viewportVisuals.layer = renderLayer;
            RectTransform viewportVisualsRT = UIElementBuilder.CreateAnchored(viewportVisuals, viewport.transform,
                new Vector2(-adjustedExpansion, -adjustedExpansion),
                new Vector2(1f + adjustedExpansion, 1f + adjustedExpansion),
                Vector2.zero,
                Vector2.zero);

            // Background for Visuals
            CreateViewportBackground(viewportVisuals.transform, config, panelHeight);

            // Border for Visuals
            CreateViewportBorder(viewportVisuals.transform, config, panelHeight);

            // Content container - NOW INSIDE VISUALS for easier HoverBorder calculation
            // Uses nested Canvas with higher sorting order so content renders AFTER background
            GameObject viewportContent = new GameObject("Content");
            viewportContent.transform.SetParent(viewportVisuals.transform, false);
            viewportContent.layer = renderLayer;
            RectTransform viewportContentRT = UIElementBuilder.CreateFullStretch(viewportContent, viewportVisuals.transform);

            // Padding compensates for expansion so content stays within original Viewport area
            float expansionPixelsX = adjustedExpansion * config.width;
            float expansionPixelsY = adjustedExpansion * panelHeight;
            viewportContentRT.offsetMin = new Vector2(expansionPixelsX + 10, expansionPixelsY + 10);
            viewportContentRT.offsetMax = new Vector2(-expansionPixelsX - 10, -expansionPixelsY - 10);

            // Nested Canvas to ensure content renders AFTER background
            Canvas contentCanvas = viewportContent.AddComponent<Canvas>();
            contentCanvas.overrideSorting = true;
            contentCanvas.sortingOrder = 110; // Higher than panel's 100, after Overlay shader Queue
            // Add GraphicRaycaster for RTT support - RTTRaycastManager needs this to raycast into options
            viewportContent.AddComponent<GraphicRaycaster>();

            // Use RectMask2D on Content for scrolling
            RectMask2D rectMask = viewportContent.AddComponent<RectMask2D>();
            rectMask.padding = Vector4.zero;

            // Options container - inside Content (which is inside Visuals)
            GameObject optionsContainer = new GameObject("Options");
            optionsContainer.transform.SetParent(viewportContent.transform, false);
            optionsContainer.layer = renderLayer;
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
                CreateOptionItem(optionsContainer.transform, config, i, valueTxt, panel, onValueChanged, dropdownComp, panelHeight, adjustedExpansion, renderLayer);
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
            bgObj.layer = parent.gameObject.layer; // Inherit layer from parent for RTT compatibility
            RectTransform rt = UIElementBuilder.CreateFullStretch(bgObj, parent);
            // Ensure same Z position as border to avoid parallax issues at different viewing angles
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);

            Image img = bgObj.AddComponent<Image>();
            img.sprite = SpriteUtility.GetPixelSprite();
            img.raycastTarget = false;

            float aspect = MaterialFactory.CalculateAspect(config.width, panelHeight);
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
                // Disable Fresnel effect to prevent view-angle-dependent appearance
                // Fresnel causes background to look different from different viewing angles
                mat.SetFloat("_FresnelPower", 1f);
                mat.SetFloat("_FresnelStrength", 0f);

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
                // Fallback to MaterialFactory for standard glass background
                Material mat = MaterialFactory.CreateGlassBackgroundWithGradient(
                    adjustedCornerRadius,
                    adjustedEdgePadding,
                    aspect,
                    col,
                    config.backgroundAlpha * 1.5f
                );

                if (mat != null)
                {
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
            borderObj.layer = parent.gameObject.layer; // Inherit layer from parent for RTT compatibility
            RectTransform rt = UIElementBuilder.CreateFullStretch(borderObj, parent);
            // Ensure same Z position as background to avoid parallax issues at different viewing angles
            rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);

            Image img = borderObj.AddComponent<Image>();
            img.sprite = SpriteUtility.GetPixelSprite();
            img.raycastTarget = false;

            float aspect = MaterialFactory.CalculateAspect(config.width, panelHeight);
            Color col = config.themeColor;

            // Scale parameters based on height ratio to maintain visual consistency
            float heightRatio = config.BoxHeight / panelHeight;
            float adjustedCornerRadius = config.cornerRadius * heightRatio;
            float adjustedEdgePadding = config.edgePadding * heightRatio;
            float adjustedBorderWidth = config.borderWidth * heightRatio;
            float adjustedGlowWidth = config.glowWidth * heightRatio;

            // Use MaterialFactory to create glow border
            Material mat = MaterialFactory.CreateGlowBorder(
                aspect,
                adjustedCornerRadius,
                adjustedEdgePadding,
                col,
                adjustedBorderWidth,
                adjustedGlowWidth,
                config.glowIntensity,
                enablePulse: false
            );

            if (mat != null)
            {
                img.material = mat;
            }
        }

        private static void CreateOptionItem(Transform parent, DropdownConfig config, int index,
            TextMeshProUGUI valueTxt, GameObject panel, System.Action<int, string> onValueChanged,
            VRDropdown dropdownComponent, float panelHeight, float adjustedExpansion, int renderLayer)
        {
            string optionText = config.options[index];
            bool isSelected = index == config.defaultIndex;
            Sprite optionIcon = (config.optionIcons != null && index < config.optionIcons.Count)
                ? config.optionIcons[index] : config.icon;

            float optionHeight = config.OptionHeight;
            float optionIconSize = config.OptionIconSize;

            GameObject option = new GameObject("Option_" + index);
            option.transform.SetParent(parent, false);
            option.layer = renderLayer;

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

            // Strong color transitions for visible hover feedback
            ColorBlock colors = optBtn.colors;
            colors.normalColor = isSelected ? new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.3f) : Color.clear;
            colors.highlightedColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.5f);
            colors.pressedColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.65f);
            colors.selectedColor = colors.highlightedColor;
            optBtn.colors = colors;

            // === SEPARATOR LINE between options ===
            // Add separator at bottom of each option (except last)
            // Separator fades at both ends for a clean look
            bool isLastOption = (index == config.options.Count - 1);
            if (!isLastOption)
            {
                GameObject separatorObj = new GameObject("Separator");
                separatorObj.transform.SetParent(option.transform, false);
                separatorObj.layer = renderLayer;
                RectTransform separatorRT = UIElementBuilder.CreateAnchored(separatorObj, option.transform,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(10f, -1.5f),
                    new Vector2(-10f, 1.5f));
                separatorRT.pivot = new Vector2(0.5f, 0.5f);
                separatorRT.anchoredPosition = new Vector2(0f, 0f);

                Image separatorImg = separatorObj.AddComponent<Image>();
                separatorImg.sprite = GetHorizontalFadeSprite();
                separatorImg.raycastTarget = false;
                // Same color as panel border
                Color separatorColor = Color.Lerp(config.themeColor, Color.white, 0.75f);
                separatorColor.a = 0.7f; // Semi-transparent
                separatorImg.color = separatorColor;
            }

            // Add HoverEffectController for hover animation
            HoverEffectController hoverController = option.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = option.transform;

            // Add background color effect for hover feedback (stronger intensity)
            Color bgHoverColor = new Color(config.themeColor.r, config.themeColor.g, config.themeColor.b, 0.55f);
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")  // Empty = target TargetVisuals directly (option has Image)
                .WithHoverColor(bgHoverColor));

            // If selected, highlight background
            if (isSelected)
            {
                hoverController.SetForceHover(true);
            }

            // BoxCollider cho VR raycast (non-RTT mode)
            BoxCollider optCol = option.AddComponent<BoxCollider>();
            optCol.size = new Vector3(config.width - 20f, optionHeight, UIConstants.ColliderDepth);
            optCol.center = new Vector3(0, 0, UIConstants.ColliderZOffset);

            // Layout: Checkmark | Icon | Text
            // Checkmark size first (needed for icon position calculation)
            float checkSize = optionHeight * 0.35f; // Proportional to option height

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
            RectTransform checkRT = UIElementBuilder.CreateAnchoredPosition(checkObj, option.transform,
                new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(checkSize, checkSize),
                new Vector2(checkmarkX, 0f));
            checkObj.layer = renderLayer;

            Image checkImg = checkObj.AddComponent<Image>();
            checkImg.sprite = SpriteUtility.GetCheckmarkSprite();
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
                RectTransform iconRT = UIElementBuilder.CreateAnchoredPosition(iconObj, option.transform,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(actualIconSize, actualIconSize),
                    new Vector2(iconX, 0f));
                iconObj.layer = renderLayer;

                Image iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = optionIcon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                iconImg.color = Color.white; // Full white for maximum visibility

                // Glow effect for icon visibility using UIGlowEffects utility
                UIGlowEffects.AddIconGlow(iconObj, config.themeColor,
                    distance: 1.5f,
                    glowAlpha: 0.6f);
            }

            // Text
            GameObject txtObj = new GameObject("Text");
            RectTransform txtRT = UIElementBuilder.CreateAnchored(txtObj, option.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(textStartX, 0f),
                new Vector2(-10f, 0f));
            txtObj.layer = renderLayer;

            TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
            txt.text = optionText;
            txt.fontSize = config.valueFontSize; // Same as main value text
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
                // Strip "(Recommended)" suffix from displayed value
                string displayValue = optionText.Replace(" (Recommended)", "").Trim();
                valueTxt.text = displayValue;

                if (dropdownComponent != null)
                {
                    dropdownComponent.UpdateSelection(capturedIndex);
                    dropdownComponent.CloseDropdown();  // Use CloseDropdown to release force hover
                }
                else
                {
                    panel.SetActive(false);
                }

                // Callback with clean value (no "(Recommended)")
                onValueChanged?.Invoke(capturedIndex, displayValue);
            });

            // Register option with hover effect
            if (dropdownComponent != null)
            {
                dropdownComponent.RegisterOption(index, optBg, checkImg, config.themeColor, hoverController);
            }
        }

        /// <summary>
        /// Tạo sprite ngang với gradient fade ở 2 đầu (cho separator lines)
        /// </summary>
        public static Sprite GetHorizontalFadeSprite()
        {
            if (_horizontalFadeSprite != null) return _horizontalFadeSprite;

            int width = 128;
            int height = 4;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] colors = new Color[width * height];

            // Fade region at both ends (25% each side)
            float fadeRatio = 0.25f;
            int fadePixels = Mathf.RoundToInt(width * fadeRatio);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float alpha = 1f;

                    // Left fade
                    if (x < fadePixels)
                    {
                        alpha = (float)x / fadePixels;
                    }
                    // Right fade
                    else if (x > width - fadePixels)
                    {
                        alpha = (float)(width - x) / fadePixels;
                    }

                    // Smooth easing
                    alpha = alpha * alpha * (3f - 2f * alpha); // Smoothstep

                    colors[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            _horizontalFadeSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            return _horizontalFadeSprite;
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
        private HoverEffectController _hoverController;  // Reference to hover controller for dropdown button

        // For RTT mode: create a world-space floating panel to avoid RenderTexture clipping
        private RTTCanvasBase _rttCanvasBase;
        private GameObject _worldSpaceDropdownRoot;  // Root object for world-space dropdown
        private Canvas _worldSpaceCanvas;
        private bool _isRTTMode = false;
        private bool _rttModeChecked = false;

        // Static reference to currently open dropdown (for VRGazeReticle to check)
        public static VRDropdown CurrentlyOpenDropdown { get; private set; }

        private class OptionRef
        {
            public Image background;
            public Image checkmark;
            public Color themeColor;
            public HoverEffectController hoverController;
        }
        private Dictionary<int, OptionRef> _optionRefs = new Dictionary<int, OptionRef>();

        public int SelectedIndex => _selectedIndex;
        public GameObject DropdownPanel => _isRTTMode && _worldSpaceDropdownRoot != null ? _worldSpaceDropdownRoot : _dropdownPanel;
        public bool IsOpen => _isRTTMode ? _worldSpaceDropdownRoot != null : (_dropdownPanel != null && _dropdownPanel.activeSelf);
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

            // Find HoverEffectController in HitArea child
            var hitArea = transform.Find("HitArea");
            if (hitArea != null)
            {
                _hoverController = hitArea.GetComponent<HoverEffectController>();
            }
        }

        public void RegisterOption(int index, Image background, Image checkmark, Color themeColor, HoverEffectController hoverController = null)
        {
            _optionRefs[index] = new OptionRef
            {
                background = background,
                checkmark = checkmark,
                themeColor = themeColor,
                hoverController = hoverController
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
                if (kvp.Value.hoverController != null)
                {
                    kvp.Value.hoverController.SetForceHover(false);
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
                if (optRef.hoverController != null)
                {
                    optRef.hoverController.SetForceHover(true);
                }
            }
        }

        public void SetSelectedIndex(int index)
        {
            if (_options == null || index < 0 || index >= _options.Count) return;

            UpdateSelection(index);
            // Strip "(Recommended)" suffix from displayed value
            string displayValue = _options[index].Replace(" (Recommended)", "").Trim();
            if (_valueTxt != null)
            {
                _valueTxt.text = displayValue;
            }
            _onValueChanged?.Invoke(index, displayValue);
        }

        public void SetOptions(List<string> options, int selectedIndex = 0)
        {
            _options = options;

            // Update option labels in the dropdown panel
            UpdateOptionLabels();

            SetSelectedIndex(selectedIndex);
        }

        /// <summary>
        /// Update the text labels in the dropdown panel to match current _options list.
        /// This ensures "(Recommended)" suffix is displayed correctly after SetOptions is called.
        /// </summary>
        private void UpdateOptionLabels()
        {
            if (_dropdownPanel == null || _options == null) return;

            // Find all option items in the dropdown panel
            // Structure: DropdownPanel > Viewport > Visuals > Content > Options > Option_X > Text
            for (int i = 0; i < _options.Count; i++)
            {
                // Find Option_X by name
                Transform optionTransform = FindChildRecursive(_dropdownPanel.transform, "Option_" + i);
                if (optionTransform != null)
                {
                    // Find Text child
                    Transform textTransform = optionTransform.Find("Text");
                    if (textTransform != null)
                    {
                        TextMeshProUGUI txt = textTransform.GetComponent<TextMeshProUGUI>();
                        if (txt != null)
                        {
                            txt.text = _options[i];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively find a child Transform by name
        /// </summary>
        private Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }

            return null;
        }

        public void CloseDropdown()
        {
            // Cleanup world-space dropdown if in RTT mode
            if (_isRTTMode)
            {
                CleanupWorldSpaceDropdown();
            }
            else if (_dropdownPanel != null)
            {
                _dropdownPanel.SetActive(false);
            }

            // Release force hover when panel closes
            if (_hoverController != null)
            {
                _hoverController.SetForceHover(false);
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
                // Check if we're in RTT mode
                CheckRTTMode();

                if (_isRTTMode)
                {
                    // Create world-space dropdown to avoid RenderTexture clipping
                    CreateWorldSpaceDropdown();
                }
                else
                {
                    _dropdownPanel.SetActive(true);
                }
            }
            // Force hover when panel opens
            if (_hoverController != null)
            {
                _hoverController.SetForceHover(true);
            }
            // Set static reference
            CurrentlyOpenDropdown = this;
        }

        /// <summary>
        /// Check if we're inside an RTT (Render-to-Texture) canvas
        /// </summary>
        private void CheckRTTMode()
        {
            if (_rttModeChecked) return;
            _rttModeChecked = true;

            // Look for RTTCanvasBase in parents
            _rttCanvasBase = GetComponentInParent<RTTCanvasBase>();
            _isRTTMode = _rttCanvasBase != null;
        }

        /// <summary>
        /// Create a world-space Canvas for the dropdown panel that floats in front of the RTT DisplayQuad.
        /// This completely bypasses the RenderTexture clipping issue.
        /// </summary>
        private void CreateWorldSpaceDropdown()
        {
            if (_rttCanvasBase == null) return;

            // Get the DisplayQuad to position our world-space dropdown
            MeshRenderer displayQuad = _rttCanvasBase.GetDisplayQuad();
            if (displayQuad == null) return;

            // Get dropdown button's RectTransform
            RectTransform dropdownRT = GetComponent<RectTransform>();
            if (dropdownRT == null) return;

            // Get panel dimensions from the original panel
            RectTransform originalPanelRT = _dropdownPanel.GetComponent<RectTransform>();
            float panelWidth = originalPanelRT.rect.width;
            float panelHeight = originalPanelRT.rect.height;

            // If width is 0 (stretch anchors), use dropdown width
            if (panelWidth <= 0)
            {
                panelWidth = dropdownRT.rect.width;
            }
            if (panelHeight <= 0)
            {
                panelHeight = originalPanelRT.sizeDelta.y;
            }

            // Lấy UI Camera từ RTT
            Camera uiCamera = _rttCanvasBase.GetUICamera();
            if (uiCamera == null) return;

            // Lấy world corners của dropdown button
            Vector3[] worldCorners = new Vector3[4];
            dropdownRT.GetWorldCorners(worldCorners);
            // 0=bottom-left, 1=top-left, 2=top-right, 3=bottom-right

            // Tính bottom-center trong world space của UI Camera
            Vector3 bottomCenterWorld = (worldCorners[0] + worldCorners[3]) / 2f;

            // Chuyển sang viewport coordinates (0-1) của UI Camera
            Vector3 viewportPos = uiCamera.WorldToViewportPoint(bottomCenterWorld);

            // Viewport coordinates chính là normalized position trên DisplayQuad
            Vector2 bottomCenterNorm = new Vector2(viewportPos.x, viewportPos.y);

            // Get RTT resolution and world size
            Vector2Int rttResolution = _rttCanvasBase.CurrentResolution;
            Vector2 worldSize = _rttCanvasBase.GetWorldSize();

            // Calculate pixels per world unit
            float pixelsPerWorldUnitX = rttResolution.x / worldSize.x;
            float pixelsPerWorldUnitY = rttResolution.y / worldSize.y;

            // Calculate panel world dimensions
            float worldPanelWidth = panelWidth / pixelsPerWorldUnitX;
            float worldPanelHeight = panelHeight / pixelsPerWorldUnitY;

            // Map normalized canvas position to DisplayQuad world position
            // DisplayQuad is centered, so we map from (-0.5 to 0.5) * worldSize
            Vector3 quadCenter = displayQuad.transform.position;
            Vector3 quadRight = displayQuad.transform.right;
            Vector3 quadUp = displayQuad.transform.up;
            Vector3 quadForward = displayQuad.transform.forward;

            // Calculate bottom-center position on the quad in world space
            Vector3 dropdownBottomCenter = quadCenter
                + quadRight * (bottomCenterNorm.x - 0.5f) * worldSize.x
                + quadUp * (bottomCenterNorm.y - 0.5f) * worldSize.y;

            // Create world-space root object
            if (_worldSpaceDropdownRoot != null)
            {
                Object.Destroy(_worldSpaceDropdownRoot);
            }

            _worldSpaceDropdownRoot = new GameObject("WorldSpaceDropdown_" + gameObject.name);

            // Parent to VirtualObjects for better hierarchy organization
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            if (virtualObjects != null)
            {
                _worldSpaceDropdownRoot.transform.SetParent(virtualObjects.transform, true);
            }

            // Fix Issue 2: Calculate proper gap from original panel's anchoredPosition (-25f pixels)
            // Original panel has anchoredPosition = (0, -25f), need to convert this to world space
            float panelGapPixels = 25f; // From CreateDropdownPanel: anchoredPosition = new Vector2(0, -25f)
            float worldPanelGap = panelGapPixels / pixelsPerWorldUnitY;

            // Set layer to VirtualObjects for VR raycast
            int vrLayer = LayerMask.NameToLayer(UIConstants.VirtualObjectsLayer);
            if (vrLayer == -1) vrLayer = LayerMask.NameToLayer("Default");
            _worldSpaceDropdownRoot.layer = vrLayer;

            // Create World Space Canvas FIRST so we can set pivot before positioning
            _worldSpaceCanvas = _worldSpaceDropdownRoot.AddComponent<Canvas>();
            _worldSpaceCanvas.renderMode = RenderMode.WorldSpace;

            // Setup RectTransform for canvas with TOP-CENTER pivot (like original panel)
            // Original panel has pivot = (0.5, 1) which means position refers to top-center
            RectTransform worldCanvasRT = _worldSpaceDropdownRoot.GetComponent<RectTransform>();
            worldCanvasRT.pivot = new Vector2(0.5f, 1f); // Top-center pivot
            worldCanvasRT.sizeDelta = new Vector2(panelWidth, panelHeight);

            // Scale canvas to match world dimensions
            float scaleX = worldPanelWidth / panelWidth;
            float scaleY = worldPanelHeight / panelHeight;
            _worldSpaceDropdownRoot.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            // Position with TOP-CENTER pivot: position is where top-center of panel will be
            // dropdownBottomCenter is the bottom of dropdown button
            // We want panel's top to be at dropdownBottomCenter - gap (slightly below dropdown)
            Vector3 panelPosition = dropdownBottomCenter - quadForward * 0.005f; // 5mm in front
            panelPosition -= quadUp * worldPanelGap; // Only gap, no half-height since pivot is at top

            _worldSpaceDropdownRoot.transform.position = panelPosition;
            _worldSpaceDropdownRoot.transform.rotation = displayQuad.transform.rotation;

            // Add CanvasScaler for consistent sizing
            CanvasScaler scaler = _worldSpaceDropdownRoot.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100f;

            // Add GraphicRaycaster for UI interaction
            _worldSpaceDropdownRoot.AddComponent<GraphicRaycaster>();

            // Note: No BoxCollider on the root - each option already has its own BoxCollider for VR raycast

            // Clone the dropdown panel content to this world-space canvas
            GameObject clonedPanel = Object.Instantiate(_dropdownPanel, _worldSpaceDropdownRoot.transform);
            clonedPanel.name = "DropdownPanelClone";

            // Setup cloned panel RectTransform
            RectTransform clonedRT = clonedPanel.GetComponent<RectTransform>();
            clonedRT.anchorMin = Vector2.zero;
            clonedRT.anchorMax = Vector2.one;
            clonedRT.offsetMin = Vector2.zero;
            clonedRT.offsetMax = Vector2.zero;
            clonedRT.localPosition = Vector3.zero;
            clonedRT.localRotation = Quaternion.identity;
            clonedRT.localScale = Vector3.one;

            // Set layer recursively
            SetLayerRecursively(_worldSpaceDropdownRoot, vrLayer);

            // Activate the cloned panel
            clonedPanel.SetActive(true);

            // Re-register option click handlers for the cloned panel
            ReconnectClonedPanelHandlers(clonedPanel);
        }

        /// <summary>
        /// Reconnect click handlers for cloned dropdown panel options
        /// </summary>
        private void ReconnectClonedPanelHandlers(GameObject clonedPanel)
        {
            // Find all buttons in the cloned panel and reconnect their handlers
            Button[] buttons = clonedPanel.GetComponentsInChildren<Button>(true);

            int optionIndex = 0;
            foreach (Button btn in buttons)
            {
                // Skip if this is a scroll rect or other non-option button
                if (!btn.gameObject.name.StartsWith("Option_")) continue;

                // Parse index from name
                string indexStr = btn.gameObject.name.Replace("Option_", "");
                if (int.TryParse(indexStr, out int idx))
                {
                    int capturedIndex = idx;
                    string optionText = _options != null && capturedIndex < _options.Count ? _options[capturedIndex] : "";

                    // Clear existing listeners and add new one
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        // Strip "(Recommended)" suffix from displayed value
                        string displayValue = optionText.Replace(" (Recommended)", "").Trim();
                        if (_valueTxt != null)
                        {
                            _valueTxt.text = displayValue;
                        }
                        UpdateSelection(capturedIndex);
                        CloseDropdown();
                        _onValueChanged?.Invoke(capturedIndex, displayValue);
                    });

                    // Re-add hover effects to cloned HoverEffectController
                    // When panel is cloned, _activeEffects (runtime list) is lost
                    HoverEffectController hoverController = btn.GetComponent<HoverEffectController>();
                    if (hoverController != null)
                    {
                        // Get theme color from button's highlightedColor (was set during CreateOptionItem)
                        Color themeColor = btn.colors.highlightedColor;
                        // Reconstruct the full color with stronger hover intensity
                        Color bgHoverColor = new Color(themeColor.r, themeColor.g, themeColor.b, 0.55f);

                        // Re-add background color effect
                        hoverController.AddEffect(new ColorHoverEffect()
                            .WithTargetChild("")
                            .WithHoverColor(bgHoverColor));

                        // Set selected state for the currently selected option
                        bool isSelected = (capturedIndex == _selectedIndex);
                        hoverController.SetForceHover(isSelected);
                    }

                    optionIndex++;
                }
            }
        }

        /// <summary>
        /// Cleanup world-space dropdown when closing
        /// </summary>
        private void CleanupWorldSpaceDropdown()
        {
            if (_worldSpaceDropdownRoot != null)
            {
                Object.Destroy(_worldSpaceDropdownRoot);
                _worldSpaceDropdownRoot = null;
                _worldSpaceCanvas = null;
            }
        }

        /// <summary>
        /// Tính vị trí center của một RectTransform trên Canvas (trong Canvas local space)
        /// Đi ngược hierarchy từ element lên đến canvas để tính tổng offset
        /// </summary>
        private Vector2 GetPositionOnCanvas(RectTransform element, RectTransform canvas)
        {
            // Sử dụng TransformPoint để chuyển đổi từ local space của element sang world space
            // Sau đó chuyển sang local space của canvas
            Vector3 worldPos = element.TransformPoint(Vector3.zero); // Center của element trong world
            Vector3 canvasLocalPos = canvas.InverseTransformPoint(worldPos);
            return new Vector2(canvasLocalPos.x, canvasLocalPos.y);
        }

        /// <summary>
        /// Recursively set layer for GameObject and all children
        /// </summary>
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        public void ToggleDropdown()
        {
            // Use IsOpen property which handles both RTT and non-RTT modes
            if (IsOpen)
            {
                CloseDropdown();
            }
            else
            {
                OpenDropdown();
            }
        }

        /// <summary>
        /// Check if a GameObject is part of this dropdown's panel (option items)
        /// </summary>
        public bool IsPartOfDropdownPanel(GameObject obj)
        {
            if (obj == null) return false;

            // Check if obj is a child of the dropdown panel (non-RTT mode)
            if (_dropdownPanel != null)
            {
                Transform current = obj.transform;
                while (current != null)
                {
                    if (current.gameObject == _dropdownPanel)
                    {
                        return true;
                    }
                    current = current.parent;
                }
            }

            // Check if obj is a child of the world-space dropdown (RTT mode)
            if (_worldSpaceDropdownRoot != null)
            {
                Transform current = obj.transform;
                while (current != null)
                {
                    if (current.gameObject == _worldSpaceDropdownRoot)
                    {
                        return true;
                    }
                    current = current.parent;
                }
            }

            return false;
        }

        private void OnDisable()
        {
            CloseDropdown();
        }
    }

}
