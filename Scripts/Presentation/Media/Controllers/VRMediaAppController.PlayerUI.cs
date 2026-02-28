// Extracted to: Assets/VR-Workspace/Scripts/Presentation/Media/Controllers/MediaPlayerUIBuilder.cs
//
// This partial file is retained as the glue layer that:
//   - Calls MediaPlayerUIBuilder.BuildPlayerUI() and stores the result fields
//   - Owns the show/hide logic for library and player UI
//   - Owns the LateUpdate camera-facing loop
//   - Owns position helpers (PositionMenuButton*, SetupControlsFollowCamera, etc.)
//   - Owns the fade alpha helpers (SetAllPlayerFramesAlpha, etc.)
//   - Wires all event handlers from the builder result

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.RTT;
using Debug = UnityEngine.Debug;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.UI;
using VRWorkspace.Media.Utils;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.Presentation.Media.Controllers;

namespace VRWorkspace.Media.Core
{
    public partial class VRMediaAppController
    {
        #region Library UI

        private void BuildLibraryUI()
        {
            _parentMenuFrame = _viewObject.GetComponentInParent<RTTMenuFrame>();
            if (_parentMenuFrame == null)
                Debug.LogError("[VRMediaAppController] No parent RTTMenuFrame found!");

            var libraryObj = new GameObject("MediaLibrary");
            libraryObj.transform.SetParent(_viewObject.transform, false);

            var libraryRT = libraryObj.AddComponent<RectTransform>();
            libraryRT.anchorMin = Vector2.zero;
            libraryRT.anchorMax = Vector2.one;
            libraryRT.offsetMin = Vector2.zero;
            libraryRT.offsetMax = Vector2.zero;

            _libraryView = libraryObj.AddComponent<RTTMediaLibrary>();
            _libraryController = libraryObj.AddComponent<RTTMediaLibraryController>();
            _libraryController.Initialize(_libraryView);

            _libraryView.Initialize(_libraryController, _parentMenuFrame,
                _containerWidth, _containerHeight, _font, _primaryColor, _accentColor);

            _libraryController.OnVideoPlayRequested += HandleLibraryPlayRequested;
            _libraryController.OnCloseRequested += HandleLibraryCloseRequested;

            Debug.Log("[VRMediaAppController] Library UI built");
        }

        private void ShowLibraryUI()
        {
            Debug.Log("[VRMediaAppController] ShowLibraryUI");

            _controlsContainer?.SetActive(false);
            _menuButtonFrameObject?.SetActive(false);
            ProjectionSystem?.Hide();

            // Ensure error dialog is hidden when returning to library
            if (_errorDialog != null)
                _errorDialog.gameObject.SetActive(false);

            _libraryView?.gameObject.SetActive(true);

            _allMenuFrames = GetAllFrames();
            foreach (var frame in _allMenuFrames)
                frame?.gameObject?.SetActive(true);

            _parentMenuFrame?.gameObject.SetActive(true);
        }

        private void HideLibraryUI()
        {
            Debug.Log("[VRMediaAppController] HideLibraryUI");

            if (_parentMenuFrame != null)
            {
                _menuFramePosition = _parentMenuFrame.transform.position;
                _menuFrameRotation = _parentMenuFrame.transform.rotation;
                _menuFrameScale = _parentMenuFrame.transform.localScale;
            }

            _libraryView?.gameObject.SetActive(false);

            _allMenuFrames = GetAllFrames();
            foreach (var frame in _allMenuFrames)
                frame?.gameObject?.SetActive(false);

            _parentMenuFrame?.gameObject.SetActive(false);
        }

        #endregion

        #region Player UI

        private void BuildPlayerUI()
        {
            // Rebuild the builder with current container reference in case it changed
            _uiBuilder = new MediaPlayerUIBuilder(
                transform, _container, _containerWidth, _font, _primaryColor, _accentColor);

            var r = _uiBuilder.BuildPlayerUI(_sideControlsSide);

            // Store all result fields into controller state
            _controlsContainer = r.ControlsContainer;
            _playerControlsGroup = r.PlayerControlsGroup;
            _overlayFrameObject = r.OverlayFrameObject;
            _controlsFrameObject = r.ControlsFrameObject;
            _controlsCanvasBase = r.ControlsCanvasBase;
            _controlsPanel = r.ControlsPanel;
            _sideControlsFrameObject = r.SideControlsFrameObject;
            _queuePanel = r.QueuePanel;
            _queuePagination = r.QueuePagination;
            _settingsFrameObject = r.SettingsFrameObject;
            _settingsPanel = r.SettingsPanel;
            _menuButtonFrameObject = r.MenuButtonFrameObject;
            _menuButtonQuadOriginalScale = r.MenuButtonQuadOriginalScale;
            _errorDialog = r.ErrorDialog;
            _sideControlsBaseX = r.SideControlsBaseX;
            _sideControlsBaseY = r.SideControlsBaseY;
            _sidePhysicalW = r.SidePhysicalW;
            _sidePhysicalH = r.SidePhysicalH;
            _settingsPhysicalW = r.SettingsPhysicalW;
            _paginationWorldH = r.PaginationWorldH;
            _uiSettingsPopup = r.UISettingsPopup;
            _uiSettingsPopupFrame = r.UISettingsPopupFrame;
            _uiSettingsBlocker = r.UISettingsBlocker;

            // Wire dismiss overlay button
            if (r.DismissButton != null)
            {
                r.DismissButton.onClick.AddListener(() =>
                {
                    _controlsPanel?.Hide();
                    _playerController?.HideProjectionPopup();
                    _playerController?.HideEnvironmentPopup();
                });
            }

            // Wire queue events
            if (_queuePanel != null)
            {
                _queuePanel.OnItemClicked += HandleQueueItemClicked;
                _queuePanel.OnShuffleClicked += HandleQueueShuffleClicked;
            }

            // Pass external frame references to controls panel
            _controlsPanel.SetExternalFrames(_overlayFrameObject, _menuButtonFrameObject);
            _controlsPanel.SetSideControlsFrame(_sideControlsFrameObject);
            _controlsPanel.SetQueuePagination(_queuePagination);
            if (_settingsPanel != null && _queuePanel != null)
                _controlsPanel.SetSettingsPanel(
                    _settingsPanel, _queuePanel.gameObject, _settingsFrameObject, _sideControlsFrameObject);

            // Hide UI settings popup when controls panel hides
            _controlsPanel.OnVisibilityChanged += (visible) =>
            {
                if (!visible) HideUISettingsPopup();
            };

            // Wire menu show button
            if (r.MenuShowButton != null)
                r.MenuShowButton.onClick.AddListener(() => _controlsPanel?.Show());

            // Wire blocker close button
            if (r.BlockerButton != null)
            {
                r.BlockerButton.onClick.AddListener(() =>
                {
                    _uiSettingsPopupFrame?.SetActive(false);
                    _uiSettingsBlocker?.SetActive(false);
                    _settingsPanel?.SetUISettingsRowForceHover(false);
                });
            }

            // === Player Controller ===
            var playerObj = new GameObject("PlayerController");
            playerObj.transform.SetParent(transform);

            _playerController = playerObj.AddComponent<VRVideoPlayerController>();
            _playerController.Initialize(PlaybackEngine, ProjectionSystem, _controlsPanel);
            _playerController.OnBackToLibrary += SwitchToLibrary;
            _playerController.OnPlaybackFailed += HandlePlaybackFailed;
            _playerController.OnProjectionSettingsUpdated += HandleProjectionSettingsUpdated;
            _playerController.OnVideoChanged += HandleVideoChanged;

            _playerController.SetProjectionPopup(r.ProjectionPopup);
            _playerController.SetEnvironmentPopup(r.EnvironmentPopup);

            // Hide UI settings when using playback controls
            _controlsPanel.OnPlayPause += HideUISettingsPopup;
            _controlsPanel.OnSeek += (_) => HideUISettingsPopup();
            _controlsPanel.OnVolumeChanged += (_) => HideUISettingsPopup();
            _controlsPanel.OnEnvironmentClicked += HideUISettingsPopup;
            _controlsPanel.OnVRModeClicked += HideUISettingsPopup;
            _controlsPanel.OnHeadsetModeClicked += HideUISettingsPopup;
            _controlsPanel.OnRecenterClicked += HideUISettingsPopup;
            _controlsPanel.OnSettingsClicked += HideUISettingsPopup;

            // Wire error dialog actions
            r.ErrorDialog.OnBackClicked += () => { r.ErrorDialog.Hide(); SwitchToLibrary(); };
            r.ErrorDialog.OnDismissed += () => { r.ErrorDialog.Hide(); SwitchToLibrary(); };
            r.ErrorDialog.OnOpenExternalClicked += () => _ffmpegHandler.HandleOpenInExternalPlayer();
            r.ErrorDialog.OnRetryClicked += () => _ffmpegHandler.HandleRetryOrConvert();

            // Wire settings panel events (via builder helper)
            _uiBuilder.WireSettingsPanelEvents(
                _settingsPanel, _playerController,
                _currentPicture, HandleUISettingsRequested);

            // Wire UI settings popup events (via builder helper)
            _uiBuilder.WireUISettingsPopupEvents(
                _uiSettingsPopup,
                _uiSettingsPopupFrame,
                _uiSettingsBlocker,
                _settingsPanel,
                _playerControlsGroup,
                setDepthOffset: (v) =>
                {
                    // Map 0-1 slider to 1-3m screen distance (v=0→1m, v=0.4→1.8m, v=1→3m)
                    float distance = 1.0f + v * 2.0f;
                    _playerController?.ProjectionSystem?.SetScreenDistance(distance);
                },
                setHeightOffset: (v) => _uiHeightOffset = v,
                ApplyUISettingsToControlsGroup);
        }

        private void ShowPlayerUI()
        {
            if (_controlsPanel == null)
            {
                BuildPlayerUI();
                var snapshot = _uiBuilder.LoadSavedSettings(
                    _playerController, _settingsPanel, _uiSettingsPopup, _playerControlsGroup,
                    out _uiDepthOffset, out _uiHeightOffset);
                _currentPicture = snapshot;

                // Apply loaded depth as screen distance (1-3m mapping)
                float screenDistance = 1.0f + _uiDepthOffset * 2.0f;
                _playerController?.ProjectionSystem?.SetScreenDistance(screenDistance);

                ApplyUISettingsToControlsGroup();
            }

            _controlsContainer?.SetActive(true);

            if (_controlsPanel != null)
            {
                _controlsPanel.gameObject.SetActive(true);
                _controlsPanel.ResetToQueueView();
                _controlsPanel.Show();
            }
        }

        private void HidePlayerUI()
        {
            _uiSettingsPopupFrame?.SetActive(false);
            _uiSettingsBlocker?.SetActive(false);
            _controlsFollowCamera = false;

            _controlsPanel?.gameObject.SetActive(false);
            _controlsContainer?.SetActive(false);
        }

        #endregion

        #region Event Handlers

        private void HandleLibraryPlayRequested(MediaVideoInfo video) => SwitchToPlayer(video);
        private void HandleLibraryCloseRequested() => OnBackClicked?.Invoke();

        private void HandleQueueItemClicked(int index)
        {
            var service = MediaPlaylistService.Instance;
            if (service == null) return;
            string path = service.JumpToIndex(index);
            if (!string.IsNullOrEmpty(path))
                _playerController?.PlayVideoSimple(path);
        }

        private void HandleQueueShuffleClicked()
        {
            var service = MediaPlaylistService.Instance;
            if (service == null) return;
            service.SetShuffle(true);
            if (_queuePanel != null)
                _queuePanel.SetQueue(service.GetPlaybackQueue(), service.CurrentQueueIndex);
        }

        private void HandleVideoChanged(string path)
        {
            if (_queuePanel != null)
            {
                var queue = MediaPlaylistService.Instance.GetPlaybackQueue();
                int idx = queue.IndexOf(path);
                if (idx >= 0) _queuePanel.SetCurrentIndex(idx);
            }
        }

        private void HandlePlaybackFailed(string error, bool isCodecError, string codecName, string containerFormat)
        {
            _ffmpegHandler.HandlePlaybackFailed(error, isCodecError, codecName, containerFormat);
        }

        private void HandleProjectionSettingsUpdated(VideoProjectionType projection, StereoMode stereo)
        {
            bool isImmersive = !ProjectionDetector.SupportsScreenSettings(projection);

            if (_menuButtonFrameObject != null)
            {
                if (isImmersive && Camera.main != null) PositionMenuButtonImmersive(Camera.main);
                else PositionMenuButtonFlat();
            }

            if (isImmersive && Camera.main != null && _controlsContainer != null)
                SetupControlsFollowCamera(Camera.main);
            else
                _controlsFollowCamera = false;

            PositionSideControlsForProjection(isImmersive);
            _settingsPanel?.SetMode(isImmersive);
        }

        private void HandleUISettingsRequested()
        {
            if (_uiSettingsPopupFrame == null) return;
            bool showPopup = !_uiSettingsPopupFrame.activeSelf;
            if (showPopup) _playerController?.HideAllPopups();
            _uiSettingsPopupFrame.SetActive(showPopup);
            _uiSettingsBlocker?.SetActive(showPopup);
            _settingsPanel?.SetUISettingsRowForceHover(showPopup);
        }

        private void HideUISettingsPopup()
        {
            if (_uiSettingsPopup != null && _uiSettingsPopup.IsVisible)
            {
                _uiSettingsPopupFrame?.SetActive(false);
                _uiSettingsBlocker?.SetActive(false);
                _settingsPanel?.SetUISettingsRowForceHover(false);
            }
        }

        #endregion

        #region Positioning Helpers

        private void ApplyUISettingsToControlsGroup()
        {
            if (_playerControlsGroup == null) return;
            _playerControlsGroup.transform.localPosition =
                _playerControlsBaseLocalPos + new Vector3(0, _uiHeightOffset, -_uiDepthOffset);
        }

        private void ScaleMenuButtonQuad(float scaleFactor)
        {
            if (_menuButtonFrameObject == null) return;
            var menuFrame = _menuButtonFrameObject.GetComponent<RTTMenuFrame>();
            var quad = menuFrame?.GetDisplayQuad();
            if (quad == null) return;
            quad.transform.localScale = new Vector3(
                _menuButtonQuadOriginalScale.x * scaleFactor,
                _menuButtonQuadOriginalScale.y * scaleFactor,
                _menuButtonQuadOriginalScale.z);
        }

        private void PositionMenuButtonImmersive(Camera cam)
        {
            if (_menuButtonFrameObject == null) return;

            Vector3 camForward = cam.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            Vector3 leftDir = Quaternion.AngleAxis(-60f, Vector3.up) * camForward;
            float distance = 2.0f;

            Vector3 pos = cam.transform.position + leftDir * distance;
            pos.y = cam.transform.position.y;
            _menuButtonFrameObject.transform.position = pos;
            _menuButtonFrameObject.transform.rotation = Quaternion.LookRotation(-leftDir, Vector3.up);
            ScaleMenuButtonQuad(1.0f);

            _menuButtonFollowCamera = true;
            _menuButtonOffsetDir = leftDir;
            _menuButtonOffsetDist = distance;
            _menuButtonOffsetY = 0f;

            var menuFrame = _menuButtonFrameObject.GetComponent<RTTMenuFrame>();
            var quad = menuFrame?.GetDisplayQuad();
            if (quad != null)
            {
                var col = quad.GetComponent<BoxCollider>();
                if (col != null) col.size = new Vector3(3f, 3f, 0.01f);
            }
        }

        private void PositionMenuButtonFlat()
        {
            if (_menuButtonFrameObject == null) return;

            float menuBtnPhysical = 90f / 1200f;
            Vector3 pos = _menuFramePosition + new Vector3(0, -0.5f - menuBtnPhysical * 1.5f, 0);
            _menuButtonFrameObject.transform.position = pos;
            _menuButtonFrameObject.transform.rotation = _menuFrameRotation;
            ScaleMenuButtonQuad(1.0f);
            _menuButtonFollowCamera = false;

            var menuFrame = _menuButtonFrameObject.GetComponent<RTTMenuFrame>();
            var quad = menuFrame?.GetDisplayQuad();
            if (quad != null)
            {
                var col = quad.GetComponent<BoxCollider>();
                if (col != null) col.size = new Vector3(3f, 2.5f, 0.01f);
            }
        }

        private void SetupControlsFollowCamera(Camera cam)
        {
            Vector3 horizontal = _controlsContainer.transform.position - cam.transform.position;
            float yOffset = horizontal.y;
            horizontal.y = 0;
            float dist = horizontal.magnitude;
            Vector3 dir = dist > 0.001f ? horizontal / dist : cam.transform.forward;

            _controlsFollowCamera = true;
            _controlsOffsetDir = dir;
            _controlsOffsetDist = dist;
            _controlsOffsetY = yOffset;
        }

        private void UpdateSideControlsFacing()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            FaceToCamera(_sideControlsFrameObject, cam, yOnly: true);
            FaceToCamera(_settingsFrameObject, cam, yOnly: true);

            if (_queuePagination != null)
            {
                Vector3 toCamera = cam.transform.position - _queuePagination.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _queuePagination.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }

        private void PositionSideControlsForProjection(bool isImmersive)
        {
            if (_sideControlsFrameObject != null)
            {
                _sideControlsFrameObject.transform.localPosition = new Vector3(_sideControlsBaseX, _sideControlsBaseY, 0);

                if (_queuePagination != null)
                {
                    float pagY = _sideControlsBaseY - (_sidePhysicalH / 2f) - _paginationGap - (_paginationWorldH / 2f);
                    _queuePagination.transform.localPosition = new Vector3(_sideControlsBaseX, pagY, 0);
                }
            }

            if (_settingsFrameObject != null)
            {
                float controlsPhysicalW = (_containerWidth * 1.1f) / 1200f;
                float gapMeters = 0.02f;
                float settingsX = (controlsPhysicalW / 2f + gapMeters + _settingsPhysicalW / 2f) * _sideControlsSide;
                float settingsY = _sideControlsBaseY - (_paginationGap + _paginationWorldH) / 2f;
                _settingsFrameObject.transform.localPosition = new Vector3(settingsX, settingsY, 0);
            }
        }

        private static void FaceToCamera(GameObject obj, Camera cam, bool yOnly = false)
        {
            if (obj == null) return;
            Vector3 toCamera = cam.transform.position - obj.transform.position;
            if (yOnly) toCamera.y = 0;
            if (toCamera.sqrMagnitude > 0.001f)
                obj.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }

        #endregion

        #region Fade Alpha Helpers

        private void SetRTTFrameAlpha(GameObject frameObj, float alpha)
        {
            if (frameObj == null) return;
            var quad = frameObj.GetComponent<RTTMenuFrame>()?.GetDisplayQuad();
            if (quad?.material != null) quad.material.color = new Color(1f, 1f, 1f, alpha);
        }

        private void SetProjectionAlpha(float alpha)
        {
            if (ProjectionSystem?.ActiveRenderer == null) return;
            if (ProjectionSystem.ActiveRenderer is FlatProjectionRenderer flat) flat.SetBoardAlpha(alpha);
            else if (ProjectionSystem.ActiveRenderer is ImmersiveSphereRenderer sphere) sphere.SetBrightness(alpha);
        }

        private void SetAllPlayerFramesAlpha(float alpha)
        {
            SetRTTFrameAlpha(_controlsFrameObject, alpha);
            SetRTTFrameAlpha(_overlayFrameObject, alpha);
            SetRTTFrameAlpha(_sideControlsFrameObject, alpha);
            SetRTTFrameAlpha(_menuButtonFrameObject, alpha);

            if (_queuePagination != null)
            {
                var quad = _queuePagination.GetDisplayQuad();
                if (quad?.material != null) quad.material.color = new Color(1f, 1f, 1f, alpha);
            }

            SetProjectionAlpha(alpha);
        }

        private void SetAllLibraryFramesAlpha(float alpha)
        {
            if (_parentMenuFrame != null)
            {
                var quad = _parentMenuFrame.GetDisplayQuad();
                if (quad?.material != null) quad.material.color = new Color(1f, 1f, 1f, alpha);
            }

            foreach (var frame in GetAllFrames())
            {
                if (frame == null) continue;
                var quad = frame.GetDisplayQuad();
                if (quad?.material != null) quad.material.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        #endregion

        #region LateUpdate

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // Controls container follow camera (immersive mode)
            if (_controlsFollowCamera && _controlsContainer != null && _controlsContainer.activeInHierarchy)
            {
                Vector3 pos = cam.transform.position + _controlsOffsetDir * _controlsOffsetDist;
                pos.y = cam.transform.position.y + _controlsOffsetY;
                _controlsContainer.transform.position = pos;
            }

            // Controls frame face-to-camera
            if (_controlsFrameObject != null && _controlsFrameObject.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _controlsFrameObject.transform.position;
                if (toCamera.sqrMagnitude > 0.001f)
                    _controlsFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // Menu button follow + face-to-camera
            if (_menuButtonFrameObject != null && _menuButtonFrameObject.activeInHierarchy)
            {
                if (_menuButtonFollowCamera)
                {
                    Vector3 pos = cam.transform.position + _menuButtonOffsetDir * _menuButtonOffsetDist;
                    pos.y = cam.transform.position.y + _menuButtonOffsetY;
                    _menuButtonFrameObject.transform.position = pos;
                }

                Vector3 toCamera = cam.transform.position - _menuButtonFrameObject.transform.position;
                if (toCamera.sqrMagnitude > 0.001f)
                    _menuButtonFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // Side controls, settings, queue pagination face-to-camera
            if (_sideControlsFrameObject != null && _sideControlsFrameObject.activeInHierarchy)
                FaceToCamera(_sideControlsFrameObject, cam, yOnly: true);

            if (_settingsFrameObject != null && _settingsFrameObject.activeInHierarchy)
                FaceToCamera(_settingsFrameObject, cam, yOnly: true);

            if (_queuePagination != null && _queuePagination.gameObject.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _queuePagination.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _queuePagination.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // UI Settings popup face-to-camera
            if (_uiSettingsPopupFrame != null && _uiSettingsPopupFrame.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _uiSettingsPopupFrame.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _uiSettingsPopupFrame.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // UI Settings blocker face-to-camera
            if (_uiSettingsBlocker != null && _uiSettingsBlocker.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _uiSettingsBlocker.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _uiSettingsBlocker.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }

        #endregion
    }
}
