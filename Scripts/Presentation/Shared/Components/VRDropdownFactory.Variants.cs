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
    /// Dropdown variant builders: background, border, content, panel, viewport visuals,
    /// option items, animation, styling helpers, and separator sprite generation.
    /// </summary>
    public static partial class VRDropdownFactory
    {
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
}
