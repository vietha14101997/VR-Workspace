using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.Components;
using VRWorkspace.Media.Core;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Services;
using VRWorkspace.Utilities;
using VRWorkspace.Presentation.Input.VCS;

namespace VRWorkspace.UI.RTT.Components
{
    public partial class RTTFileManager
    {
        private void CreateSidePanels()
        {
            float mainPanelWidth = _menuFrame.PanelWidth;
            float mainPanelHeight = _menuFrame.PanelHeight;
            float sideWidth = mainPanelWidth / 3f;
            float sideHeight = mainPanelHeight;
            float gapMeters = 0.05f;

            // Left Panel (Navigation) - created with alpha=0, will fade in with main frame
            PlaceSidePanelOnSphere("FileNavigationPanel", -1, mainPanelWidth, sideWidth, sideHeight, gapMeters, ref _leftFrame);
            if (_leftFrame != null)
            {
                // Display quad enabled but transparent - ready for coordinated fade
                _leftFrame.SetVisible(true);
                SetFrameAlpha(_leftFrame, 0f);
                // Initialize Side Panel Content
                StartCoroutine(CreateLeftPanelContent());
            }

            // Right Panel (Detail) - created with alpha=0, will fade in with main frame
            PlaceSidePanelOnSphere("FileDetailPanel", 1, mainPanelWidth, sideWidth, sideHeight, gapMeters, ref _rightFrame);
            if (_rightFrame != null)
            {
                // Display quad enabled but transparent - ready for coordinated fade
                _rightFrame.SetVisible(true);
                SetFrameAlpha(_rightFrame, 0f);
                // Initialize Right Panel Content (Placeholder)
                StartCoroutine(CreateRightPanelContent());
            }
        }

        private void PlaceSidePanelFlat(string name, int side, float mainWidth, float sideWidth, float sideHeight,
            float gap, float rotationAngle, ref RTTMenuFrame frameRef)
        {
            // Get main panel's world transform
            Vector3 mainPos = _menuFrame.transform.position;
            Quaternion mainRot = _menuFrame.transform.rotation;
            Vector3 mainRight = _menuFrame.transform.right;
            Vector3 mainForward = _menuFrame.transform.forward;

            // Calculate side panel position using arc placement (like WorldPanelClusterRig Flat Planar)
            float rotRad = rotationAngle * Mathf.Deg2Rad;
            float halfSide = sideWidth / 2f;
            float centerOffsetX = halfSide * Mathf.Cos(rotRad);
            float centerOffsetZ = -halfSide * Mathf.Sin(rotRad);

            float totalX = (mainWidth / 2f) + gap + centerOffsetX;
            float totalZ = centerOffsetZ;

            Vector3 offset = mainRight * (side * totalX) + mainForward * totalZ;
            Vector3 panelPos = mainPos + offset;

            // Flat Planar orientation: content faces toward camera (like WorldPanelClusterRig.panelsFaceCamera = true)
            // Panel's forward points AWAY from camera, so content (rendered on back) faces toward camera
            Camera cam = Camera.main;
            Quaternion panelRot;
            if (cam != null)
            {
                // Calculate direction from camera to panel (away from camera)
                Vector3 awayFromCamera = panelPos - cam.transform.position;
                awayFromCamera.y = 0; // Keep panel upright (yaw only, like faceCameraYawOnly)

                if (awayFromCamera.sqrMagnitude > 0.001f)
                {
                    // Panel forward points away from camera, content faces toward camera
                    panelRot = Quaternion.LookRotation(awayFromCamera.normalized, Vector3.up);
                }
                else
                {
                    // Fallback: use main panel's rotation with angle offset
                    panelRot = mainRot * Quaternion.Euler(0, side * rotationAngle, 0);
                }
            }
            else
            {
                // No camera, fallback to original angle-based rotation
                panelRot = mainRot * Quaternion.Euler(0, side * rotationAngle, 0);
            }

            float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

            // Create RTTMenuFrame
            // Note: Creating as child of _menuFrame.transform can cause issues if parent scales/hides.
            // But RTTRemoteMenu does it this way initially, then RTTRemoteMenuController manages it.
            // Here we create it as child of main frame for hierarchy organization,
            // but physically positioned in world space.
            frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
            frameRef.transform.position = panelPos;
            frameRef.transform.rotation = panelRot;
            frameRef.transform.localScale = Vector3.one;

            frameRef.SetContentMargins(20f, 20f, 20f, 20f);
            frameRef.SetFloatingDataEnabled(true, 5);
        }

        /// <summary>
        /// Place a side panel on sphere surface with camera as center.
        /// Uses PRIMARY menu frame (not app frame) for positioning reference.
        /// Sphere radius = zoom distance from VirtualObjectsZoomController or distance to primary frame.
        /// Panel faces camera (vector from panel to camera is perpendicular to panel surface).
        /// </summary>
        /// <param name="side">-1 for left, +1 for right</param>
        private void PlaceSidePanelOnSphere(string name, int side, float mainWidth, float sideWidth, float sideHeight,
            float gap, ref RTTMenuFrame frameRef)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[RTTFileManager] PlaceSidePanelOnSphere: No main camera found!");
                return;
            }

            if (_menuFrame == null)
            {
                Debug.LogError("[RTTFileManager] PlaceSidePanelOnSphere: No app frame (_menuFrame) found!");
                return;
            }

            // Mathematical solution to satisfy BOTH conditions:
            // 1. Inner edge lies exactly on main panel's plane
            // 2. Panel surface is perpendicular to vector (center -> camera)
            //
            // Solution: r = sqrt(|E-C|² - w²), θ = atan2(b,a) + arcsin(w*side/|E-C|)

            // Step 1: Calculate inner edge position on main panel's plane
            Vector3 mainRight = _menuFrame.transform.right;
            float innerEdgeOffset = (mainWidth / 2f) + gap;
            Vector3 innerEdgePos = _menuFrame.transform.position + mainRight * innerEdgeOffset * side;

            // Step 2: Calculate in horizontal plane (XZ)
            float w = sideWidth / 2f; // half width
            Vector3 cameraPos = cam.transform.position;

            // Vector from camera to inner edge (horizontal only)
            float a = innerEdgePos.x - cameraPos.x;
            float b = innerEdgePos.z - cameraPos.z;
            float distSq = a * a + b * b;
            float dist = Mathf.Sqrt(distSq);

            Vector3 panelPos;
            Quaternion panelRotation;

            // Edge case: camera too close to inner edge
            if (dist < 0.001f)
            {
                panelPos = innerEdgePos + mainRight * w * side;
                panelPos.y = _menuFrame.transform.position.y;
                panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
            }
            else
            {
                // Step 3: Calculate distance from camera to panel center
                float rSq = distSq - w * w;
                if (rSq < 0.0001f) rSq = 0.0001f;
                float r = Mathf.Sqrt(rSq);

                // Step 4: Calculate direction angle θ
                // θ = atan2(b, a) - arcsin(w * side / dist)
                float alpha = Mathf.Atan2(b, a);
                float sinArg = Mathf.Clamp((w * side) / dist, -1f, 1f);
                float theta = alpha - Mathf.Asin(sinArg);

                // Step 5: Calculate panel center position
                float dx = Mathf.Cos(theta);
                float dz = Mathf.Sin(theta);
                panelPos = new Vector3(
                    cameraPos.x + dx * r,
                    _menuFrame.transform.position.y,
                    cameraPos.z + dz * r
                );

                // Step 6: Calculate rotation to face camera from center
                Vector3 toCamera = new Vector3(cameraPos.x - panelPos.x, 0, cameraPos.z - panelPos.z);
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    panelRotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
                else
                {
                    panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
                }
            }

            // Calculate logical width based on aspect ratio (same resolution density as main panel)
            float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

            // Create RTTMenuFrame for the side panel with unique name
            frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
            frameRef.transform.position = panelPos;
            frameRef.transform.rotation = panelRotation;
            frameRef.transform.localScale = Vector3.one;

            Debug.Log($"[RTTFileManager] PlaceSidePanel: innerEdge={innerEdgePos}, panelPos={panelPos}");

            // Configure frame appearance
            frameRef.SetContentMargins(20f, 20f, 20f, 20f);
            frameRef.SetFloatingDataEnabled(true, 5);

            // Register with ZoomController for updates on zoom change
            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController != null)
            {
                zoomController.RegisterSidePanel(frameRef, _menuFrame.transform, side, mainWidth, sideWidth, gap);
                Debug.Log($"[RTTFileManager] Registered {name} with VirtualObjectsZoomController");
            }

            Debug.Log($"[RTTFileManager] Placed {name}: pos={panelPos}");
        }

        private IEnumerator CreateLeftPanelContent()
        {
            // Wait for ContentContainer
            while (_leftFrame.ContentContainer == null) yield return null;

            var containerSize = _leftFrame.GetContentSize();

            GameObject contentObj = new GameObject("SidePanelContent");
            contentObj.transform.SetParent(_leftFrame.ContentContainer, false);
            RectTransform rt = contentObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _sidePanel = contentObj.AddComponent<RTTFileSidePanel>();
            _sidePanel.Initialize(_controller, containerSize.x, containerSize.y, _font, _primaryColor, _accentColor);
        }

        private RTTFileDetail _fileDetail;

        public void UpdateDetail(MockFile file, bool isCurrentFolder)
        {
            if (_fileDetail != null)
            {
                _fileDetail.UpdateInfo(file, isCurrentFolder);
            }

            // Store current displayed file for action bar
            _currentDisplayedFile = file;

            // Only show action bar when there's an actual selected file (not just hover)
            // AND not in edit mode or clipboard mode
            if (_fileActionBar != null)
            {
                bool showActionBar = _hasSelectedFile && !_isEditMode && !_isClipboardMode;
                _fileActionBar.SetVisible(showActionBar);

                if (showActionBar)
                {
                    // Update button states based on file type
                    bool canOpen = true;  // Can always open (navigate into folder or open file)
                    bool canRename = !isCurrentFolder;  // Can't rename current folder
                    bool canDelete = !isCurrentFolder;  // Can't delete current folder

                    _fileActionBar.UpdateButtonStates(canOpen, canRename, canDelete);
                }
            }
        }

        private IEnumerator CreateRightPanelContent()
        {
            // Wait for ContentContainer
            while (_rightFrame.ContentContainer == null) yield return null;

            GameObject contentObj = new GameObject("DetailContent");
            contentObj.transform.SetParent(_rightFrame.ContentContainer, false);
            RectTransform rt = contentObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _fileDetail = contentObj.AddComponent<RTTFileDetail>();
            _fileDetail.Initialize(_primaryColor, _accentColor, _font);

            // Create action bar below the panel
            CreateFileActionBar();
        }

        /// <summary>
        /// Create RTTFileActionBar that follows the detail panel.
        /// </summary>
        private void CreateFileActionBar()
        {
            if (_rightFrame == null) return;

            Vector2 panelSize = _rightFrame.GetWorldSize();
            float targetHeight = panelSize.y;
            float panelWidth = panelSize.x;

            _fileActionBar = RTTFileActionBar.Create(
                _rightFrame.transform,
                targetHeight,
                panelWidth,
                _primaryColor,
                _accentColor,
                _font
            );

            // Register the action bar as a VCS surface so the cursor can traverse
            // from the Right Side Panel downward into the bar (mirrors Main → Pagination).
            float frameHeight = RTTToolbar.Instance != null && RTTToolbar.Instance.TaskbarHeight > 0
                ? RTTToolbar.Instance.TaskbarHeight
                : 0.12f;
            var barCtrl = ActionBarSurfaceController.Attach(
                _fileActionBar.gameObject,
                panelWidth,
                frameHeight,
                _rightFrame);
            // Mirror bar visibility into VCS
            _fileActionBar.OnVisibilityChanged += visible => barCtrl?.NotifyVisible(visible);

            // Wire up events
            _fileActionBar.OnOpenClicked += OnActionBarOpenClicked;
            _fileActionBar.OnRenameClicked += OnActionBarRenameClicked;
            _fileActionBar.OnDeleteClicked += OnActionBarDeleteClicked;

            // Hide initially until a file is selected
            _fileActionBar.SetVisible(false);

            Debug.Log("[RTTFileManager] File action bar created (hidden initially)");
        }

        private static readonly HashSet<string> _videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "mkv", "avi", "webm", "mov", "wmv", "m4v", "flv"
        };

        private void OnActionBarOpenClicked()
        {
            if (_currentDisplayedFile.Path == null) return;

            if (_currentDisplayedFile.IsFolder)
            {
                _controller?.NavigateTo(_currentDisplayedFile.Path);
            }
            else if (_videoExtensions.Contains(_currentDisplayedFile.Type))
            {
                OpenVideoInPlayer(_currentDisplayedFile.Path);
            }
            else
            {
                Debug.Log($"[RTTFileManager] Open file requested: {_currentDisplayedFile.Path}");
            }
        }

        private void OpenVideoInPlayer(string videoPath)
        {
            var videoPaths = _controller?.GetVideoFilePaths();
            if (videoPaths == null || videoPaths.Count == 0) return;

            int startIndex = videoPaths.IndexOf(videoPath);
            if (startIndex < 0) startIndex = 0;
            MediaPlaylistService.Instance?.SetPlaybackQueueDirect(videoPaths, startIndex);

            var manager = RTTManager.Instance;
            if (manager == null) return;

            // Capture state before fade (position may change during animation)
            Vector3 framePos = _menuFrame != null ? _menuFrame.transform.position : Vector3.zero;
            Quaternion frameRot = _menuFrame != null ? _menuFrame.transform.rotation : Quaternion.identity;
            TMP_FontAsset font = manager.Font;
            Color primaryColor = manager.PrimaryColor;
            Color accentColor = manager.AccentColor;
            float width = _containerWidth;
            float height = _containerHeight;

            // Hide action bar immediately (has its own CanvasGroup fade)
            if (_fileActionBar != null) _fileActionBar.HideImmediate();

            // Fade out all File Manager frames, then create player
            FadeOutAllFrames(() =>
            {
                // Deactivate main frame — triggers OnDisable which handles
                // side frames, pagination.Hide(), and actionbar.HideImmediate()
                foreach (var frame in GetAllFrames())
                {
                    if (frame != null && frame.gameObject != null)
                        frame.gameObject.SetActive(false);
                }

                // Create standalone video player (completely independent from Media app)
                var playerGO = new GameObject("DirectVideoPlayer");
                playerGO.transform.SetParent(manager.transform);
                var playerController = playerGO.AddComponent<VRMediaAppController>();

                playerController.InitializeForDirectPlay(
                    width, height, font, primaryColor, accentColor,
                    framePos, frameRot,
                    onExit: () =>
                    {
                        // Re-show all File Manager frames at alpha=0, then fade in
                        // Set alpha=0 on materials before activating (materials exist even when inactive)
                        foreach (var frame in GetAllFrames())
                        {
                            if (frame != null && frame.gameObject != null)
                                SetFrameAlpha(frame, 0f);
                        }

                        // Activate main frame — triggers OnEnable which handles:
                        // - Side frames activation
                        // - _pagination.Show() (own fade-in)
                        // - _fileActionBar.SetVisible() if file selected
                        foreach (var frame in GetAllFrames())
                        {
                            if (frame != null && frame.gameObject != null)
                                frame.gameObject.SetActive(true);
                        }

                        // Fade in the frame quads
                        FadeInAllFrames();
                    }
                );

                // Start playing the video directly (includes player fade-in)
                playerController.PlayVideoDirectly(videoPath);
            });

            Debug.Log($"[RTTFileManager] Opened direct video player for: {videoPath}");
        }

        #region Direct Play Fade Animation
        private const float DIRECT_PLAY_FADE_OUT_DURATION = 0.15f;
        private const float DIRECT_PLAY_FADE_IN_DURATION = 0.2f;
        private Coroutine _directPlayFadeCoroutine;

        private void FadeOutAllFrames(Action onComplete)
        {
            if (_directPlayFadeCoroutine != null)
                StopCoroutine(_directPlayFadeCoroutine);
            _directPlayFadeCoroutine = StartCoroutine(FadeAllFramesCoroutine(1f, 0f, DIRECT_PLAY_FADE_OUT_DURATION, onComplete));
        }

        private void FadeInAllFrames(Action onComplete = null)
        {
            if (_directPlayFadeCoroutine != null)
                StopCoroutine(_directPlayFadeCoroutine);
            _directPlayFadeCoroutine = StartCoroutine(FadeAllFramesCoroutine(0f, 1f, DIRECT_PLAY_FADE_IN_DURATION, onComplete));
        }

        private IEnumerator FadeAllFramesCoroutine(float from, float to, float duration, Action onComplete)
        {
            // Collect all valid materials from RTTMenuFrame quads (main + side panels)
            // Note: pagination is NOT included here — it uses its own Show/Hide fade
            // triggered by OnEnable/OnDisable to avoid concurrent material writes
            var materials = new List<Material>();
            foreach (var frame in GetAllFrames())
            {
                if (frame == null) continue;
                var quad = frame.GetDisplayQuad();
                if (quad?.material != null)
                    materials.Add(quad.material);
            }

            // Animate
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(from, to, t);

                foreach (var mat in materials)
                    mat.color = new Color(1f, 1f, 1f, alpha);

                yield return null;
            }

            // Ensure final value
            foreach (var mat in materials)
                mat.color = new Color(1f, 1f, 1f, to);

            _directPlayFadeCoroutine = null;
            onComplete?.Invoke();
        }
        #endregion

        private void OnActionBarRenameClicked()
        {
            if (_currentDisplayedFile.Path == null) return;

            // Set the rename target path and show popup
            _renameTargetPath = _currentDisplayedFile.Path;
            ShowRenamePopup();
        }

        private void OnActionBarDeleteClicked()
        {
            if (_currentDisplayedFile.Path == null) return;

            // Temporarily select this file for deletion
            _pendingDeletePaths = new List<string> { _currentDisplayedFile.Path };
            ShowDeleteConfirmPopup();
        }

        #region Create Folder Popup

        /// <summary>
        /// Update the New Folder button's interactable state based on folder creation permission.
        /// Called when navigating to a new directory.
        /// </summary>
        private void UpdateNewFolderButtonState()
        {
            if (_newFolderButton == null || _newFolderCanvasGroup == null)
            {
                Debug.Log("[RTTFileManager] UpdateNewFolderButtonState skipped - button not yet created");
                return;
            }

            bool canCreate = _controller != null && _controller.CanCreateFolderHere();

            Debug.Log($"[RTTFileManager] UpdateNewFolderButtonState: canCreate={canCreate}");

            _newFolderButton.interactable = canCreate;
            _newFolderCanvasGroup.alpha = canCreate ? 1f : 0.4f;
            _newFolderCanvasGroup.interactable = canCreate; // Block Unity UI raycast
            _newFolderCanvasGroup.blocksRaycasts = canCreate; // Block graphic raycasts

            // VRButtonFactory creates Button and BoxCollider on the same HitArea object
            // Since _newFolderButton is on HitArea, get BoxCollider from same GameObject
            var collider = _newFolderButton.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.enabled = canCreate;
                Debug.Log($"[RTTFileManager] BoxCollider.enabled set to {canCreate}");
            }
            else
            {
                Debug.LogWarning("[RTTFileManager] BoxCollider not found on Button's GameObject!");
            }

            Debug.Log($"[RTTFileManager] New Folder button {(canCreate ? "ENABLED" : "DISABLED")} - alpha={_newFolderCanvasGroup.alpha}");
        }

        private void CreateFolderPopup()
        {
            if (_createFolderPopup != null) return;

            var config = new RTTPopupInputable.PopupConfig
            {
                title = "New Folder",
                inputLabel = "Folder Name",
                inputPlaceholder = "Enter folder name",
                buttonText = "Create",
                width = 575f,        // +15% (was 500f)
                padding = 33f,       // +10% (was 30f)
                titleFontSize = 31,  // +10% (was 28)
                labelFontSize = 24,  // +10% (was 22)
                inputFontSize = 29,  // +10% (was 26)
                buttonFontSize = 26, // +10% (was 24)
                buttonHeight = 72f,  // +10% (was 65f)
                inputHeight = 72f,   // +10% (was 65f)
                titleHeight = 55f,   // +10% (was 50f)
                closeButtonSize = 50f, // +10% (was 45f)
                spacing = 22f,       // +10% (was 20f)
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, 0.4f), // Lighter overlay
                font = _font,
                layerName = "VirtualObjects" // Use VirtualObjects layer for world-space interaction
            };

            // Create popup in world-space mode - fixed position in front of RTTFileManager
            // Pass _menuFrame.transform as reference so popup positions relative to it
            _createFolderPopup = RTTPopupInputable.CreateWorldSpace(config, _menuFrame.transform);
        }

        private void ShowCreateFolderPopup()
        {
            // Double-check permission (button should already be disabled, but safeguard)
            if (_controller == null || !_controller.CanCreateFolderHere())
            {
                Debug.LogWarning("[RTTFileManager] Cannot create folder in current path");
                return;
            }

            // Create popup if not exists
            if (_createFolderPopup == null)
            {
                CreateFolderPopup();
            }

            // Show with callbacks
            _createFolderPopup.Show(
                onConfirm: OnCreateFolderConfirmed,
                onCancel: OnCreateFolderCancelled
            );
        }

        private void OnCreateFolderConfirmed(string folderName)
        {
            Debug.Log($"[RTTFileManager] Create folder: {folderName}");

            // Request controller to create the folder
            if (_controller != null)
            {
                _controller.CreateFolder(folderName);
            }
        }

        private void OnCreateFolderCancelled()
        {
            Debug.Log("[RTTFileManager] Create folder cancelled");
        }

        #endregion

        #region Rename Item

        private void CreateRenamePopup()
        {
            if (_renamePopup != null) return;

            var config = new RTTPopupInputable.PopupConfig
            {
                title = "Rename",
                inputLabel = "New Name",
                inputPlaceholder = "Enter new name",
                buttonText = "Rename",
                width = 575f,
                padding = 33f,
                titleFontSize = 31,
                labelFontSize = 24,
                inputFontSize = 29,
                buttonFontSize = 26,
                buttonHeight = 72f,
                inputHeight = 72f,
                titleHeight = 55f,
                closeButtonSize = 50f,
                spacing = 22f,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, 0.4f),
                font = _font,
                layerName = "VirtualObjects"
            };

            _renamePopup = RTTPopupInputable.CreateWorldSpace(config, _menuFrame.transform);
        }

        private void ShowRenamePopup()
        {
            if (string.IsNullOrEmpty(_renameTargetPath))
            {
                Debug.LogWarning("[RTTFileManager] No target path for rename");
                return;
            }

            // Create popup if not exists
            if (_renamePopup == null)
            {
                CreateRenamePopup();
            }

            // Get current name from path
            string currentName = System.IO.Path.GetFileName(_renameTargetPath);

            // Set default value to current name
            _renamePopup.SetDefaultValue(currentName);

            // Show with callbacks
            _renamePopup.Show(
                onConfirm: OnRenameConfirmed,
                onCancel: OnRenameCancelled
            );
        }

        private void OnRenameConfirmed(string newName)
        {
            Debug.Log($"[RTTFileManager] Rename '{_renameTargetPath}' to '{newName}'");

            if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(_renameTargetPath))
            {
                Debug.LogWarning("[RTTFileManager] Invalid rename parameters");
                return;
            }

            // Request controller to rename the item
            if (_controller != null)
            {
                _controller.RenameItem(_renameTargetPath, newName);
            }

            // Exit edit mode after rename (this will also clear selection)
            if (_isEditMode)
            {
                ToggleEditMode();
            }

            _renameTargetPath = null;
        }

        private void OnRenameCancelled()
        {
            Debug.Log("[RTTFileManager] Rename cancelled");
            _renameTargetPath = null;
        }

        #endregion
    }
}
