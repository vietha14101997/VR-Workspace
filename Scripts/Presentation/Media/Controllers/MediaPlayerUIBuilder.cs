using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.HoverEffects;
using Debug = UnityEngine.Debug;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.UI;
using VRWorkspace.Media.Utils;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Media.Controllers
{
    /// <summary>
    /// Responsible for constructing and wiring all Player-mode UI elements:
    /// the controls frame, side panels, settings frame, menu button, popups,
    /// and the UI-settings popup. Also handles saving/loading picture defaults.
    ///
    /// Plain C# class (not MonoBehaviour). Receives layout parameters via
    /// constructor and exposes a BuildPlayerUI() factory method that returns
    /// the root <see cref="PlayerUIResult"/> with all created GameObjects.
    /// </summary>
    public class MediaPlayerUIBuilder
    {
        // ------------------------------------------------------------------ //
        //  Construction parameters                                             //
        // ------------------------------------------------------------------ //

        private readonly Transform _ownerTransform;
        private readonly RectTransform _libraryContainer;
        private readonly float _containerWidth;
        private readonly TMP_FontAsset _font;
        private readonly Color _primaryColor;
        private readonly Color _accentColor;

        // ------------------------------------------------------------------ //
        //  Cached rounded rect sprite (shared across all builder instances)    //
        // ------------------------------------------------------------------ //

        private static Sprite _cachedRoundedRectSprite;

        // ------------------------------------------------------------------ //
        //  Constructor                                                          //
        // ------------------------------------------------------------------ //

        public MediaPlayerUIBuilder(
            Transform ownerTransform,
            RectTransform libraryContainer,
            float containerWidth,
            TMP_FontAsset font,
            Color primaryColor,
            Color accentColor)
        {
            _ownerTransform = ownerTransform;
            _libraryContainer = libraryContainer;
            _containerWidth = containerWidth;
            _font = font;
            _primaryColor = primaryColor;
            _accentColor = accentColor;
        }

        // ------------------------------------------------------------------ //
        //  Public API                                                           //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Build all player-mode UI GameObjects and return them bundled in a
        /// <see cref="PlayerUIResult"/>. The caller is responsible for wiring
        /// event handlers and storing the result fields.
        /// </summary>
        public PlayerUIResult BuildPlayerUI(int sideControlsSide = 1)
        {
            var result = new PlayerUIResult();

            VideoSettingsCache.LoadFromDisk();

            int vLayer = LayerMask.NameToLayer("VirtualObjects");
            if (vLayer < 0) vLayer = 0;

            GameObject virtualObjects = GameObject.Find("VirtualObjects");

            float density = 1200f;

            // ====================================================== //
            // 1. Root container (NOT under VirtualObjects)            //
            // ====================================================== //
            result.ControlsContainer = new GameObject("VideoControlsContainer");
            result.ControlsContainer.transform.SetParent(_ownerTransform, false);

            // 1b. Player Controls Group
            result.PlayerControlsGroup = new GameObject("PlayerControlsGroup");
            result.PlayerControlsGroup.transform.SetParent(result.ControlsContainer.transform, false);

            // ====================================================== //
            // 2. Dismiss overlay frame                                //
            // ====================================================== //
            result.OverlayFrameObject = new GameObject("DismissOverlayFrame");
            result.OverlayFrameObject.transform.SetParent(result.PlayerControlsGroup.transform);
            result.OverlayFrameObject.transform.localPosition = new Vector3(0, 0, 0.5f);
            result.OverlayFrameObject.layer = vLayer;

            var overlayFrame = result.OverlayFrameObject.AddComponent<RTTMenuFrame>();
            float overlaySize = 5f;
            float overlayPixels = 64f;
            overlayFrame.Configure(overlaySize, overlaySize, overlayPixels);
            overlayFrame.SetGlassBackgroundEnabled(false);
            overlayFrame.SetFloatingDataEnabled(false);
            overlayFrame.SetContentMargins(0, 0, 0, 0);
            overlayFrame.ForceInitialize();

            var overlayQuad = overlayFrame.GetDisplayQuad();
            if (overlayQuad?.material != null)
                overlayQuad.material.renderQueue = 3050;

            var overlayContainer = overlayFrame.ContentContainer;
            if (overlayContainer != null)
            {
                // Store panel ref for the dismiss listener below – populated later in result
                var dismissObj = new GameObject("DismissButton");
                dismissObj.transform.SetParent(overlayContainer, false);
                var dismissRT = dismissObj.AddComponent<RectTransform>();
                dismissRT.anchorMin = Vector2.zero;
                dismissRT.anchorMax = Vector2.one;
                dismissRT.offsetMin = Vector2.zero;
                dismissRT.offsetMax = Vector2.zero;

                var dismissImg = dismissObj.AddComponent<Image>();
                dismissImg.color = Color.clear;
                dismissImg.raycastTarget = true;

                var dismissBtn = dismissObj.AddComponent<Button>();
                dismissBtn.transition = Selectable.Transition.None;
                // Wire after result is assembled (caller owns this via result.ControlsPanel)
                result.DismissButton = dismissBtn;

                var col = dismissObj.AddComponent<BoxCollider>();
                col.size = new Vector3(overlayPixels, overlayPixels, 10);
                col.center = new Vector3(0, 0, 5);
            }
            result.OverlayFrameObject.SetActive(false);

            // ====================================================== //
            // 3. Controls frame                                       //
            // ====================================================== //
            float padding = _containerWidth * 0.05f;
            float expandedWidth = _containerWidth + (padding * 2f);

            float controlsWidth = expandedWidth;
            float controlsHeight = 700f;
            float physicalWidth = controlsWidth / density;
            float physicalHeight = controlsHeight / density;

            var controlsFrameObj = new GameObject("VideoControlsFrame");
            controlsFrameObj.transform.SetParent(result.PlayerControlsGroup.transform, false);
            controlsFrameObj.transform.localPosition = Vector3.zero;
            controlsFrameObj.transform.localRotation = Quaternion.identity;
            controlsFrameObj.layer = vLayer;

            var controlsFrame = controlsFrameObj.AddComponent<RTTMenuFrame>();
            result.ControlsCanvasBase = controlsFrame;

            controlsFrame.Configure(physicalWidth, physicalHeight, controlsWidth);
            controlsFrame.SetGlassBackgroundEnabled(false);
            controlsFrame.SetFloatingDataEnabled(false);
            controlsFrame.SetContentMargins(0, 0, 0, 0);
            controlsFrame.ForceInitialize();

            var controlsQuad = controlsFrame.GetDisplayQuad();
            if (controlsQuad?.material != null)
                controlsQuad.material.renderQueue = 3100;

            var controlsContent = controlsFrame.ContentContainer;
            if (controlsContent == null)
            {
                Debug.LogError("[MediaPlayerUIBuilder] Cannot find container in controls frame");
                return result;
            }

            var controlsObj = new GameObject("ControlsPanel");
            controlsObj.transform.SetParent(controlsContent, false);

            var controlsRT = controlsObj.AddComponent<RectTransform>();
            controlsRT.anchorMin = Vector2.zero;
            controlsRT.anchorMax = Vector2.one;
            controlsRT.offsetMin = new Vector2(padding, 0);
            controlsRT.offsetMax = new Vector2(-padding, 0);

            result.ControlsPanel = controlsObj.AddComponent<RTTMediaControlsPanel>();
            result.ControlsPanel.Initialize(_containerWidth, controlsHeight, _font, _primaryColor, _accentColor);

            result.ControlsFrameObject = controlsFrameObj;

            // ====================================================== //
            // 3b. Side Controls Frame (Queue panel)                   //
            // ====================================================== //
            result.SideControlsFrameObject = new GameObject("SideControlsFrame");
            result.SideControlsFrameObject.transform.SetParent(result.PlayerControlsGroup.transform, false);
            result.SideControlsFrameObject.layer = vLayer;

            var sideFrame = result.SideControlsFrameObject.AddComponent<RTTMenuFrame>();
            float sideLogicalWidth = 741f;
            float sideLogicalHeight = 1351f;
            float sidePhysicalW = sideLogicalWidth / density;
            float sidePhysicalH = sideLogicalHeight / density;
            result.SidePhysicalW = sidePhysicalW;
            result.SidePhysicalH = sidePhysicalH;

            sideFrame.Configure(sidePhysicalW, sidePhysicalH, sideLogicalWidth);
            sideFrame.SetGlassBackgroundEnabled(false);
            sideFrame.SetFloatingDataEnabled(false);
            sideFrame.SetContentMargins(0, 0, 0, 0);
            sideFrame.ForceInitialize();

            var sideQuad = sideFrame.GetDisplayQuad();
            if (sideQuad?.material != null)
                sideQuad.material.renderQueue = 3100;

            var sideContainer = sideFrame.ContentContainer;
            if (sideContainer != null)
            {
                var queueObj = new GameObject("QueuePanel");
                queueObj.transform.SetParent(sideContainer, false);
                var queueRT = queueObj.AddComponent<RectTransform>();
                queueRT.anchorMin = Vector2.zero;
                queueRT.anchorMax = Vector2.one;
                queueRT.offsetMin = Vector2.zero;
                queueRT.offsetMax = Vector2.zero;

                result.QueuePanel = queueObj.AddComponent<RTTMediaQueuePanel>();
                result.QueuePanel.Initialize(sideLogicalWidth, sideLogicalHeight, _font);
            }

            float controlsPhysicalW = expandedWidth / density;
            float gapMeters = 0.02f;
            float xOffset = (controlsPhysicalW / 2f + gapMeters + sidePhysicalW / 2f) * sideControlsSide;
            float oneItemHeight = sidePhysicalH * 0.9f * 0.4f;
            float yOffset = 0.625f + oneItemHeight * 0.5f;
            result.SideControlsBaseX = xOffset;
            result.SideControlsBaseY = yOffset;
            result.SideControlsFrameObject.transform.localPosition = new Vector3(xOffset, yOffset, 0);
            result.SideControlsFrameObject.SetActive(false);

            // ====================================================== //
            // 3b2. Settings Frame                                      //
            // ====================================================== //
            float paginationPixelToMeter = 1.6f / 1920f;
            result.PaginationWorldH = 115f * paginationPixelToMeter;
            float paginationGap = 0.015f;

            {
                float settingsTotalH = sidePhysicalH + paginationGap + result.PaginationWorldH;
                float settingsLogicalH = settingsTotalH * density;
                float settingsLogicalW = settingsLogicalH / 1.5f;
                float settingsPhysicalW = settingsLogicalW / density;
                result.SettingsPhysicalW = settingsPhysicalW;

                result.SettingsFrameObject = new GameObject("SettingsFrame");
                result.SettingsFrameObject.transform.SetParent(result.PlayerControlsGroup.transform, false);
                result.SettingsFrameObject.layer = vLayer;

                var settingsMenuFrame = result.SettingsFrameObject.AddComponent<RTTMenuFrame>();
                settingsMenuFrame.Configure(settingsPhysicalW, settingsTotalH, settingsLogicalW);
                settingsMenuFrame.SetGlassBackgroundEnabled(false);
                settingsMenuFrame.SetFloatingDataEnabled(false);
                settingsMenuFrame.SetContentMargins(0, 0, 0, 0);
                settingsMenuFrame.ForceInitialize();

                var settingsFrameQuad = settingsMenuFrame.GetDisplayQuad();
                if (settingsFrameQuad?.material != null)
                    settingsFrameQuad.material.renderQueue = 3100;

                var settingsContainer = settingsMenuFrame.ContentContainer;
                if (settingsContainer != null)
                {
                    var settingsObj = new GameObject("SettingsPanel");
                    settingsObj.transform.SetParent(settingsContainer, false);
                    var settingsRT = settingsObj.AddComponent<RectTransform>();
                    settingsRT.anchorMin = Vector2.zero;
                    settingsRT.anchorMax = Vector2.one;
                    settingsRT.offsetMin = Vector2.zero;
                    settingsRT.offsetMax = Vector2.zero;

                    result.SettingsPanel = settingsObj.AddComponent<RTTMediaSettingsPanel>();
                    result.SettingsPanel.Initialize(settingsLogicalW, settingsTotalH * density, _font);
                }

                float settingsX = (controlsPhysicalW / 2f + gapMeters + settingsPhysicalW / 2f) * sideControlsSide;
                float settingsY = yOffset - (paginationGap + result.PaginationWorldH) / 2f;
                result.SettingsFrameObject.transform.localPosition = new Vector3(settingsX, settingsY, 0);
                result.SettingsFrameObject.SetActive(false);
            }

            // ====================================================== //
            // 3c. Queue Pagination                                     //
            // ====================================================== //
            if (result.QueuePanel != null)
            {
                var paginationObj = new GameObject("QueuePagination");
                paginationObj.transform.SetParent(result.PlayerControlsGroup.transform, false);
                paginationObj.layer = vLayer;

                result.QueuePagination = paginationObj.AddComponent<RTTFilePagination>();

                float queuePixelW = sidePhysicalW / paginationPixelToMeter;
                float btnSize = Mathf.Round(Mathf.Clamp(queuePixelW * 0.16f, 50f, 90f));
                float paginationFrameW = queuePixelW + 2f * btnSize;
                result.QueuePagination.Initialize((IPaginationController)result.QueuePanel, paginationFrameW, 3);

                result.QueuePagination.SetGlassColors(
                    new Color(0f, 0f, 0f, 0.45f),
                    new Color(0f, 0f, 0f, 0.45f),
                    0.5f, cyanRatio: 0f, fresnelStrength: 0f);
                result.QueuePagination.SetSelectedTextColor(new Color(1f, 0.32f, 0.32f, 1f));

                var paginationQuad = result.QueuePagination.GetDisplayQuad();
                if (paginationQuad?.material != null)
                    paginationQuad.material.renderQueue = 3100;

                float paginationY = yOffset - (sidePhysicalH / 2f) - paginationGap - (result.PaginationWorldH / 2f);
                paginationObj.transform.localPosition = new Vector3(xOffset, paginationY, 0);

                result.QueuePanel.OnPageChanged += (current, total) =>
                {
                    result.QueuePagination?.SetPage(current, total);
                };
            }

            // ====================================================== //
            // 4. Menu button frame (in VirtualObjects)                //
            // ====================================================== //
            result.MenuButtonFrameObject = new GameObject("MenuButtonFrame");
            if (virtualObjects != null)
                result.MenuButtonFrameObject.transform.SetParent(virtualObjects.transform);
            result.MenuButtonFrameObject.layer = vLayer;

            var menuFrame = result.MenuButtonFrameObject.AddComponent<RTTMenuFrame>();
            float menuBtnPixels = 90f;
            float menuFramePixels = 120f;
            float menuFramePhysical = menuFramePixels / density;
            menuFrame.Configure(menuFramePhysical, menuFramePhysical, menuFramePixels);
            menuFrame.SetGlassBackgroundEnabled(false);
            menuFrame.SetFloatingDataEnabled(false);
            menuFrame.SetContentMargins(0, 0, 0, 0);
            menuFrame.ForceInitialize();

            var menuQuad = menuFrame.GetDisplayQuad();
            if (menuQuad != null)
            {
                result.MenuButtonQuadOriginalScale = menuQuad.transform.localScale;
                if (menuQuad.material != null)
                    menuQuad.material.renderQueue = 3100;
                menuQuad.gameObject.layer = vLayer;
            }

            var menuContainer = menuFrame.ContentContainer;
            if (menuContainer != null)
            {
                var btnObj = new GameObject("MenuButton");
                btnObj.transform.SetParent(menuContainer, false);
                var btnRT = btnObj.AddComponent<RectTransform>();
                btnRT.anchorMin = new Vector2(0.5f, 0.5f);
                btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot = new Vector2(0.5f, 0.5f);
                btnRT.sizeDelta = new Vector2(menuBtnPixels, menuBtnPixels);

                var btnBg = btnObj.AddComponent<Image>();
                btnBg.sprite = CreateRoundedRectSprite();
                btnBg.color = new Color(0f, 0f, 0f, 0.75f);
                btnBg.type = Image.Type.Sliced;
                btnBg.raycastTarget = true;

                var iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(btnObj.transform, false);
                var iconRT = iconObj.AddComponent<RectTransform>();
                float pad = menuBtnPixels * 0.22f;
                iconRT.anchorMin = Vector2.zero;
                iconRT.anchorMax = Vector2.one;
                iconRT.offsetMin = new Vector2(pad, pad);
                iconRT.offsetMax = new Vector2(-pad, -pad);

                var iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = Resources.Load<Sprite>("icon_menu");
                iconImg.preserveAspect = true;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;

                var btn = btnObj.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                result.MenuShowButton = btn;

                Color themeColor = new Color(1f, 0.2f, 0.2f, 1f);
                var hoverCtrl = btnObj.AddComponent<HoverEffectController>();
                hoverCtrl.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f));
                hoverCtrl.AddEffect(new ColorHoverEffect()
                    .WithTargetChild("Icon")
                    .WithHoverColor(new Color(
                        Mathf.Lerp(themeColor.r, 1f, 0.15f),
                        Mathf.Lerp(themeColor.g, 1f, 0.15f),
                        Mathf.Lerp(themeColor.b, 1f, 0.15f),
                        1f)));

                var btnCol = btnObj.AddComponent<BoxCollider>();
                btnCol.size = new Vector3(menuBtnPixels, menuBtnPixels, 10);
                btnCol.center = new Vector3(0, 0, -5);
            }
            result.MenuButtonFrameObject.SetActive(false);

            // ====================================================== //
            // 5. Error Dialog                                         //
            // ====================================================== //
            var errorDialogObj = new GameObject("MediaErrorDialog");
            errorDialogObj.transform.SetParent(_libraryContainer, false);

            var errorDialogRT = errorDialogObj.AddComponent<RectTransform>();
            errorDialogRT.anchorMin = Vector2.zero;
            errorDialogRT.anchorMax = Vector2.one;
            errorDialogRT.offsetMin = Vector2.zero;
            errorDialogRT.offsetMax = Vector2.zero;

            result.ErrorDialog = errorDialogObj.AddComponent<MediaErrorDialog>();
            result.ErrorDialog.Initialize(_font, _primaryColor, _accentColor);
            errorDialogObj.SetActive(false); // Start hidden; Show() will activate it

            // ====================================================== //
            // 6. Projection & Environment popups                      //
            // ====================================================== //
            float bottomOffset = RTTMediaControlsPanel.GetZoneBBottomOffset();

            var projectionObj = new GameObject("ProjectionPopup_Root");
            projectionObj.transform.SetParent(_libraryContainer, false);
            var projectionRT = projectionObj.AddComponent<RectTransform>();
            projectionRT.anchorMin = Vector2.zero;
            projectionRT.anchorMax = Vector2.one;
            projectionRT.offsetMin = Vector2.zero;
            projectionRT.offsetMax = Vector2.zero;

            result.ProjectionPopup = projectionObj.AddComponent<RTTMediaProjectionPopup>();
            result.ProjectionPopup.Initialize(_font, _primaryColor, _accentColor, padding, bottomOffset, RTTMediaProjectionPopup.PopupMode.Projection);

            var envPopupObj = new GameObject("EnvironmentPopup_Root");
            envPopupObj.transform.SetParent(_libraryContainer, false);
            var envPopupRT = envPopupObj.AddComponent<RectTransform>();
            envPopupRT.anchorMin = Vector2.zero;
            envPopupRT.anchorMax = Vector2.one;
            envPopupRT.offsetMin = Vector2.zero;
            envPopupRT.offsetMax = Vector2.zero;

            result.EnvironmentPopup = envPopupObj.AddComponent<RTTMediaProjectionPopup>();
            result.EnvironmentPopup.Initialize(_font, _primaryColor, _accentColor, padding, bottomOffset, RTTMediaProjectionPopup.PopupMode.Environment);

            // ====================================================== //
            // 7a. UI Settings Blocker                                 //
            // ====================================================== //
            result.UISettingsBlocker = new GameObject("UISettingsBlocker");
            result.UISettingsBlocker.transform.SetParent(result.ControlsContainer.transform, false);
            result.UISettingsBlocker.transform.localPosition = new Vector3(0, 0, 0.01f);
            result.UISettingsBlocker.layer = vLayer;

            float blockerSize = 5f;
            float blockerPixels = 64f;
            var blockerFrame = result.UISettingsBlocker.AddComponent<RTTMenuFrame>();
            blockerFrame.Configure(blockerSize, blockerSize, blockerPixels);
            blockerFrame.SetGlassBackgroundEnabled(false);
            blockerFrame.SetFloatingDataEnabled(false);
            blockerFrame.SetContentMargins(0, 0, 0, 0);
            blockerFrame.ForceInitialize();

            var blockerQuad = blockerFrame.GetDisplayQuad();
            if (blockerQuad?.material != null)
                blockerQuad.material.renderQueue = 3150;

            var blockerContainer = blockerFrame.ContentContainer;
            if (blockerContainer != null)
            {
                var blockerBtn = new GameObject("BlockerButton");
                blockerBtn.transform.SetParent(blockerContainer, false);
                var blockerBtnRT = blockerBtn.AddComponent<RectTransform>();
                blockerBtnRT.anchorMin = Vector2.zero;
                blockerBtnRT.anchorMax = Vector2.one;
                blockerBtnRT.offsetMin = Vector2.zero;
                blockerBtnRT.offsetMax = Vector2.zero;

                var blockerImg = blockerBtn.AddComponent<Image>();
                blockerImg.color = Color.clear;
                blockerImg.raycastTarget = true;

                var blockerButton = blockerBtn.AddComponent<Button>();
                blockerButton.transition = Selectable.Transition.None;
                result.BlockerButton = blockerButton;

                var blockerCol = blockerBtn.AddComponent<BoxCollider>();
                blockerCol.size = new Vector3(blockerPixels, blockerPixels, 10);
                blockerCol.center = new Vector3(0, 0, 5);
            }
            result.UISettingsBlocker.SetActive(false);

            // ====================================================== //
            // 7b. UI Settings Popup                                   //
            // ====================================================== //
            float uiPopupLogicalW = Mathf.Round(expandedWidth * 0.42f);
            float uiPopupLogicalH = Mathf.Round(uiPopupLogicalW / 1.065f);
            float uiPopupPhysW = uiPopupLogicalW / density;
            float uiPopupPhysH = uiPopupLogicalH / density;

            result.UISettingsPopupFrame = new GameObject("UISettingsPopupFrame");
            result.UISettingsPopupFrame.transform.SetParent(result.ControlsContainer.transform, false);
            result.UISettingsPopupFrame.transform.localPosition = new Vector3(0, yOffset, 0);
            result.UISettingsPopupFrame.transform.localRotation = Quaternion.identity;
            result.UISettingsPopupFrame.layer = vLayer;

            var uiPopupFrame = result.UISettingsPopupFrame.AddComponent<RTTMenuFrame>();
            uiPopupFrame.Configure(uiPopupPhysW, uiPopupPhysH, uiPopupLogicalW);
            uiPopupFrame.SetGlassBackgroundEnabled(false);
            uiPopupFrame.SetFloatingDataEnabled(false);
            uiPopupFrame.SetContentMargins(0, 0, 0, 0);
            uiPopupFrame.ForceInitialize();

            var uiPopupQuad = uiPopupFrame.GetDisplayQuad();
            if (uiPopupQuad?.material != null)
                uiPopupQuad.material.renderQueue = 3200;

            var uiPopupContainer = uiPopupFrame.ContentContainer;
            if (uiPopupContainer != null)
            {
                result.UISettingsPopup = result.UISettingsPopupFrame.AddComponent<RTTMediaUISettingsPopup>();
                // Depth default 0.4 → maps to 1.8m screen distance (1.0 + 0.4 * 2.0)
                result.UISettingsPopup.Initialize(uiPopupContainer, uiPopupLogicalW, uiPopupLogicalH, _font, _primaryColor,
                    defaultDepth: 0.4f);
            }
            result.UISettingsPopupFrame.SetActive(false);

            result.Padding = padding;
            result.ExpandedWidth = expandedWidth;
            result.Density = density;

            Debug.Log("[MediaPlayerUIBuilder] Player UI built with controls container");
            return result;
        }

        // ------------------------------------------------------------------ //
        //  Settings helpers                                                    //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Wire settings panel events to the player controller and projection system.
        /// Caller provides the live references since they may not exist at build time.
        /// </summary>
        public void WireSettingsPanelEvents(
            RTTMediaSettingsPanel settingsPanel,
            VRWorkspace.Media.Core.VRVideoPlayerController playerController,
            SettingsSnapshot currentPicture,
            Action onUISettingsRequested)
        {
            if (settingsPanel == null) return;

            settingsPanel.OnSharpnessChanged += (v) => { currentPicture.Sharpness = v; playerController?.SetPictureAdjustment("_Sharpness", "_Sharpen", v); };
            settingsPanel.OnBrightnessChanged += (v) => { currentPicture.Brightness = v; playerController?.SetPictureAdjustment("_Brightness", "_Brightness", v); };
            settingsPanel.OnSaturationChanged += (v) => { currentPicture.Saturation = v; playerController?.SetPictureAdjustment("_Saturation", "_Saturation", v); };
            settingsPanel.OnContrastChanged += (v) => { currentPicture.Contrast = v; playerController?.SetPictureAdjustment("_Contrast", "_Contrast", v); };
            settingsPanel.OnTintChanged += (v) => { currentPicture.Tint = v; playerController?.SetPictureAdjustment("_Tint", "_Tint", v); };
            settingsPanel.OnTemperatureChanged += (v) => { currentPicture.Temperature = v; playerController?.SetPictureAdjustment("_Temperature", "_Temperature", v); };

            settingsPanel.OnPictureSaveDefaults += () => HandlePictureSaveDefaults(currentPicture);
            settingsPanel.OnPictureResetDefaults += () => HandlePictureResetDefaults(playerController, settingsPanel);

            settingsPanel.On3DChanged += (v) => playerController?.SetStereoEnabled(v);
            settingsPanel.OnLRInverseChanged += (v) => playerController?.SetLRInverse(v);
            settingsPanel.OnSpeedChanged += (v) =>
            {
                if (playerController?.PlaybackEngine != null)
                    playerController.PlaybackEngine.PlaybackSpeed = v;
                PlayerPrefs.SetFloat("MediaPlayer_Speed", v);
            };

            settingsPanel.OnTiltChanged += (v) => playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetTilt(v);
            settingsPanel.OnYawChanged += (v) => playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetYawOffset(v);
            settingsPanel.OnRollChanged += (v) => playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetRollOffset(v);
            settingsPanel.OnZoomChanged += (v) => playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetFieldOfView(v);
            settingsPanel.OnHeightChanged += (v) => playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetVerticalShift(v);
            settingsPanel.OnHorizontalBalanceChanged += (v) => playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetHorizontalShift(v);

            settingsPanel.OnAspectRatioChanged += (v) => playerController?.ProjectionSystem?.SetAspectRatioOverride(v);
            settingsPanel.OnScreenDepthChanged += (v) => playerController?.ProjectionSystem?.SetScreenDistance(v);
            settingsPanel.OnScreenScaleChanged += (v) => playerController?.ProjectionSystem?.SetScreenScale(v);
            settingsPanel.OnVerticalMoveChanged += (v) => playerController?.ProjectionSystem?.SetVerticalOffset(v);
            settingsPanel.OnScreenSettingsReset += () => HandleScreenSettingsReset(playerController, settingsPanel);

            settingsPanel.OnUISettingsRequested += onUISettingsRequested;
        }

        /// <summary>
        /// Wire UI Settings popup events (depth/height/scale sliders + reset).
        /// Uses Action&lt;float&gt; setters so the controller's fields are updated correctly
        /// through lambda captures rather than problematic ref parameters.
        /// </summary>
        public void WireUISettingsPopupEvents(
            RTTMediaUISettingsPopup uiSettingsPopup,
            GameObject uiSettingsPopupFrame,
            GameObject uiSettingsBlocker,
            RTTMediaSettingsPanel settingsPanel,
            GameObject playerControlsGroup,
            Action<float> setDepthOffset,
            Action<float> setHeightOffset,
            Action applyUISettings)
        {
            if (uiSettingsPopup == null) return;

            uiSettingsPopup.OnCloseRequested += () =>
            {
                uiSettingsPopupFrame?.SetActive(false);
                uiSettingsBlocker?.SetActive(false);
                settingsPanel?.SetUISettingsRowForceHover(false);
            };

            uiSettingsPopup.OnUIDepthChanged += (v) =>
            {
                setDepthOffset?.Invoke(v);
                applyUISettings?.Invoke();
                PlayerPrefs.SetFloat("MediaPlayer_UIDepth", v);
            };

            uiSettingsPopup.OnUIHeightChanged += (v) =>
            {
                setHeightOffset?.Invoke(v - 0.5f);
                applyUISettings?.Invoke();
                PlayerPrefs.SetFloat("MediaPlayer_UIHeight", v);
            };

            uiSettingsPopup.OnUIScaleChanged += (v) =>
            {
                float scale = Mathf.Max(0.1f, v * 2f);
                if (playerControlsGroup != null)
                    playerControlsGroup.transform.localScale = Vector3.one * scale;
                PlayerPrefs.SetFloat("MediaPlayer_UIScale", v);
            };

            uiSettingsPopup.OnUISettingsReset += () =>
            {
                // Individual slider handlers already fire via SetValue in the popup's reset logic.
                // Just flush PlayerPrefs after all handlers have saved their values.
                PlayerPrefs.Save();
                Debug.Log("[MediaPlayerUIBuilder] UI settings reset to defaults");
            };
        }

        /// <summary>
        /// Load saved picture/UI defaults from PlayerPrefs and apply them.
        /// Returns the loaded snapshot so the caller can cache it.
        /// </summary>
        public SettingsSnapshot LoadSavedSettings(
            VRWorkspace.Media.Core.VRVideoPlayerController playerController,
            RTTMediaSettingsPanel settingsPanel,
            RTTMediaUISettingsPopup uiSettingsPopup,
            GameObject playerControlsGroup,
            out float uiDepthOffset,
            out float uiHeightOffset)
        {
            float sharpen = PlayerPrefs.GetFloat("MediaPlayer_PictureSharpen", 0.5f);
            float brightness = PlayerPrefs.GetFloat("MediaPlayer_PictureBrightness", 1.0f);
            float saturation = PlayerPrefs.GetFloat("MediaPlayer_PictureSaturation", 1.0f);
            float contrast = PlayerPrefs.GetFloat("MediaPlayer_PictureContrast", 1.0f);
            float tint = PlayerPrefs.GetFloat("MediaPlayer_PictureTint", 0f);
            float temperature = PlayerPrefs.GetFloat("MediaPlayer_PictureTemperature", 0f);

            playerController?.SetPictureAdjustment("_Sharpness", "_Sharpen", sharpen);
            playerController?.SetPictureAdjustment("_Brightness", "_Brightness", brightness);
            playerController?.SetPictureAdjustment("_Saturation", "_Saturation", saturation);
            playerController?.SetPictureAdjustment("_Contrast", "_Contrast", contrast);
            playerController?.SetPictureAdjustment("_Tint", "_Tint", tint);
            playerController?.SetPictureAdjustment("_Temperature", "_Temperature", temperature);

            const int UI_SETTINGS_VER = 4;
            if (PlayerPrefs.GetInt("MediaPlayer_UISettingsVer", 0) < UI_SETTINGS_VER)
            {
                PlayerPrefs.DeleteKey("MediaPlayer_UIDepth");
                PlayerPrefs.DeleteKey("MediaPlayer_UIHeight");
                PlayerPrefs.DeleteKey("MediaPlayer_UIScale");
                PlayerPrefs.SetInt("MediaPlayer_UISettingsVer", UI_SETTINGS_VER);
                PlayerPrefs.Save();
                Debug.Log("[MediaPlayerUIBuilder] UI settings migrated to v" + UI_SETTINGS_VER);
            }

            uiDepthOffset = Mathf.Clamp(PlayerPrefs.GetFloat("MediaPlayer_UIDepth", 0.4f), 0f, 1.0f);
            float uiHeightRaw = Mathf.Clamp(PlayerPrefs.GetFloat("MediaPlayer_UIHeight", 0.5f), 0f, 1.0f);
            uiHeightOffset = uiHeightRaw - 0.5f;
            float uiScale = Mathf.Clamp(PlayerPrefs.GetFloat("MediaPlayer_UIScale", 0.5f), 0.2f, 1.0f);

            uiSettingsPopup?.SetValues(uiDepthOffset, uiHeightRaw, uiScale);

            if (playerControlsGroup != null)
                playerControlsGroup.transform.localScale = Vector3.one * Mathf.Max(0.1f, uiScale * 2f);

            float speed = PlayerPrefs.GetFloat("MediaPlayer_Speed", 1.0f);

            var snapshot = new SettingsSnapshot
            {
                Sharpness = sharpen,
                Brightness = brightness,
                Saturation = saturation,
                Contrast = contrast,
                Tint = tint,
                Temperature = temperature,
                Speed = speed
            };
            settingsPanel?.SetCurrentValues(snapshot);

            Debug.Log("[MediaPlayerUIBuilder] Loaded saved settings from PlayerPrefs");
            return snapshot;
        }

        // ------------------------------------------------------------------ //
        //  Private helpers                                                     //
        // ------------------------------------------------------------------ //

        private static void HandlePictureSaveDefaults(SettingsSnapshot current)
        {
            PlayerPrefs.SetFloat("MediaPlayer_PictureSharpen", current.Sharpness);
            PlayerPrefs.SetFloat("MediaPlayer_PictureBrightness", current.Brightness);
            PlayerPrefs.SetFloat("MediaPlayer_PictureSaturation", current.Saturation);
            PlayerPrefs.SetFloat("MediaPlayer_PictureContrast", current.Contrast);
            PlayerPrefs.SetFloat("MediaPlayer_PictureTint", current.Tint);
            PlayerPrefs.SetFloat("MediaPlayer_PictureTemperature", current.Temperature);
            PlayerPrefs.Save();
            Debug.Log("[MediaPlayerUIBuilder] Picture defaults saved to PlayerPrefs");
        }

        private static void HandlePictureResetDefaults(
            VRWorkspace.Media.Core.VRVideoPlayerController playerController,
            RTTMediaSettingsPanel settingsPanel)
        {
            float sharpen = PlayerPrefs.GetFloat("MediaPlayer_PictureSharpen", 0.5f);
            float brightness = PlayerPrefs.GetFloat("MediaPlayer_PictureBrightness", 1.0f);
            float saturation = PlayerPrefs.GetFloat("MediaPlayer_PictureSaturation", 1.0f);
            float contrast = PlayerPrefs.GetFloat("MediaPlayer_PictureContrast", 1.0f);
            float tint = PlayerPrefs.GetFloat("MediaPlayer_PictureTint", 0f);
            float temperature = PlayerPrefs.GetFloat("MediaPlayer_PictureTemperature", 0f);

            playerController?.SetPictureAdjustment("_Sharpness", "_Sharpen", sharpen);
            playerController?.SetPictureAdjustment("_Brightness", "_Brightness", brightness);
            playerController?.SetPictureAdjustment("_Saturation", "_Saturation", saturation);
            playerController?.SetPictureAdjustment("_Contrast", "_Contrast", contrast);
            playerController?.SetPictureAdjustment("_Tint", "_Tint", tint);
            playerController?.SetPictureAdjustment("_Temperature", "_Temperature", temperature);

            settingsPanel?.SetCurrentValues(new SettingsSnapshot
            {
                Sharpness = sharpen,
                Brightness = brightness,
                Saturation = saturation,
                Contrast = contrast,
                Tint = tint,
                Temperature = temperature
            });
            Debug.Log("[MediaPlayerUIBuilder] Picture adjustments reset to saved defaults");
        }

        private static void HandleScreenSettingsReset(
            VRWorkspace.Media.Core.VRVideoPlayerController playerController,
            RTTMediaSettingsPanel settingsPanel)
        {
            float depth = 1.8f;
            float scale = 1.0f;
            float verticalMove = 0f;
            const string aspect = "default";

            playerController?.ProjectionSystem?.SetScreenDistance(depth);
            playerController?.ProjectionSystem?.SetScreenScale(scale);
            playerController?.ProjectionSystem?.SetVerticalOffset(verticalMove);
            playerController?.ProjectionSystem?.SetAspectRatioOverride(aspect);

            settingsPanel?.SetCurrentValues(new SettingsSnapshot
            {
                ScreenDepth = depth,
                ScreenScale = scale,
                VerticalMove = verticalMove,
                AspectRatio = aspect
            });
            Debug.Log("[MediaPlayerUIBuilder] Screen settings reset to defaults");
        }

        private static Sprite CreateRoundedRectSprite()
        {
            if (_cachedRoundedRectSprite != null) return _cachedRoundedRectSprite;

            int size = 64;
            int radius = 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 1f;
                    Vector2 corner = Vector2.zero;
                    bool isCorner = false;
                    if (x < radius && y < radius) { corner = new Vector2(radius, radius); isCorner = true; }
                    else if (x >= size - radius && y < radius) { corner = new Vector2(size - radius - 1, radius); isCorner = true; }
                    else if (x < radius && y >= size - radius) { corner = new Vector2(radius, size - radius - 1); isCorner = true; }
                    else if (x >= size - radius && y >= size - radius) { corner = new Vector2(size - radius - 1, size - radius - 1); isCorner = true; }
                    if (isCorner)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), corner);
                        alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    }
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(colors);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            Vector4 border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
            _cachedRoundedRectSprite = Sprite.Create(
                tex, new Rect(0, 0, size, size), Vector2.one * 0.5f,
                100f, 0, SpriteMeshType.FullRect, border);
            return _cachedRoundedRectSprite;
        }
    }

    // ---------------------------------------------------------------------- //
    //  Result DTO                                                              //
    // ---------------------------------------------------------------------- //

    /// <summary>
    /// All GameObjects and components created by <see cref="MediaPlayerUIBuilder.BuildPlayerUI"/>.
    /// </summary>
    public class PlayerUIResult
    {
        public GameObject ControlsContainer;
        public GameObject PlayerControlsGroup;
        public GameObject OverlayFrameObject;
        public Button DismissButton;
        public GameObject ControlsFrameObject;
        public RTTCanvasBase ControlsCanvasBase;
        public RTTMediaControlsPanel ControlsPanel;
        public GameObject SideControlsFrameObject;
        public RTTMediaQueuePanel QueuePanel;
        public RTTFilePagination QueuePagination;
        public GameObject SettingsFrameObject;
        public RTTMediaSettingsPanel SettingsPanel;
        public GameObject MenuButtonFrameObject;
        public Vector3 MenuButtonQuadOriginalScale;
        public Button MenuShowButton;
        public MediaErrorDialog ErrorDialog;
        public RTTMediaProjectionPopup ProjectionPopup;
        public RTTMediaProjectionPopup EnvironmentPopup;
        public GameObject UISettingsBlocker;
        public Button BlockerButton;
        public GameObject UISettingsPopupFrame;
        public RTTMediaUISettingsPopup UISettingsPopup;

        // Layout metrics needed by VRMediaAppController for positioning
        public float SideControlsBaseX;
        public float SideControlsBaseY;
        public float SidePhysicalW;
        public float SidePhysicalH;
        public float SettingsPhysicalW;
        public float PaginationWorldH;
        public float Padding;
        public float ExpandedWidth;
        public float Density;
    }
}
