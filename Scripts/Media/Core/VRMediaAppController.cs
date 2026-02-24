using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using UnityEngine.Networking;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.HoverEffects;
using Debug = UnityEngine.Debug;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.UI;
using VRWorkspace.Media.Utils;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// Main controller for the Media App.
    /// Manages switching between Library mode (browse videos) and Player mode (playback).
    /// Follows the same MVVM pattern as RTTFileManagerController.
    /// </summary>
    public class VRMediaAppController : MonoBehaviour, IDataBindable
    {
        #region Enums
        public enum AppMode
        {
            Library,    // Browsing media library
            Player      // Playing video
        }
        #endregion

        #region Events
        /// <summary>Fired when user clicks back button</summary>
        public event Action OnBackClicked;

        /// <summary>Fired when app mode changes</summary>
        public event Action<AppMode> OnModeChanged;

        /// <summary>Fired when a video starts playing</summary>
        public event Action<MediaVideoInfo> OnVideoStarted;
        #endregion

        #region Properties
        /// <summary>Current app mode</summary>
        public AppMode CurrentMode { get; private set; } = AppMode.Library;

        /// <summary>Currently selected/playing video</summary>
        public MediaVideoInfo? CurrentVideo { get; private set; }

        /// <summary>Video playback engine</summary>
        public VideoPlaybackEngine PlaybackEngine { get; private set; }

        /// <summary>Projection system for video display</summary>
        public VRVideoProjectionSystem ProjectionSystem { get; private set; }

        /// <summary>Side controls frame for content injection (queue, settings, etc.)</summary>
        public RTTMenuFrame SideControlsFrame => _sideControlsFrameObject?.GetComponent<RTTMenuFrame>();
        #endregion

        #region Private Fields
        private GameObject _viewObject;
        private RectTransform _container;
        private float _containerWidth;
        private float _containerHeight;
        private TMP_FontAsset _font;
        private Color _primaryColor;
        private Color _accentColor;

        // UI Components
        private RTTMediaLibrary _libraryView;
        private RTTMediaLibraryController _libraryController;
        private RTTMediaControlsPanel _controlsPanel;
        private VRVideoPlayerController _playerController;

        // Parent frame reference for hiding during video playback
        private RTTMenuFrame _parentMenuFrame;
        private List<RTTMenuFrame> _allMenuFrames;

        // Store menu frame transform for positioning video screen
        private Vector3 _menuFramePosition;
        private Quaternion _menuFrameRotation;
        private Vector3 _menuFrameScale;

        // Controls container (NOT in VirtualObjects → not affected by Zoom)
        private GameObject _controlsContainer;
        private GameObject _controlsFrameObject;
        private GameObject _overlayFrameObject;
        private MediaErrorDialog _errorDialog;
        private string _lastFailedVideoPath;
        private string _lastFailedContainerFormat;

        // Menu button frame (IN VirtualObjects → follows video screen with Zoom)
        private GameObject _menuButtonFrameObject;
        private Vector3 _menuButtonQuadOriginalScale;

        // Immersive mode: menu button follows camera to stay fixed on the video sphere
        private bool _menuButtonFollowCamera;
        private Vector3 _menuButtonOffsetDir;  // unit direction from camera to button
        private float _menuButtonOffsetDist;   // distance from camera
        private float _menuButtonOffsetY;      // Y offset from camera eye height

        // Immersive mode: controls container follows camera to stay fixed in video space
        private bool _controlsFollowCamera;
        private Vector3 _controlsOffsetDir;   // unit direction from camera to controls
        private float _controlsOffsetDist;    // distance from camera
        private float _controlsOffsetY;       // Y offset from camera eye height

        // Controls frame reference for hover detection
        private RTTCanvasBase _controlsCanvasBase;

        // Side controls panel (generic container beside controls)
        private GameObject _sideControlsFrameObject;
        private GameObject _settingsFrameObject;
        private int _sideControlsSide = 1; // 1=right, -1=left
        private RTTMediaQueuePanel _queuePanel;
        private RTTMediaSettingsPanel _settingsPanel;
        private RTTMediaUISettingsPopup _uiSettingsPopup;
        private GameObject _uiSettingsPopupFrame;
        private GameObject _uiSettingsBlocker;
        private GameObject _playerControlsGroup; // Sub-container for controls/side/overlay (UI Settings adjusts this, not _controlsContainer)
        private Vector3 _playerControlsBaseLocalPos; // Base local position after world positioning (before UI settings)
        private float _uiDepthOffset = 0f;  // Current UI depth slider value
        private float _uiHeightOffset = 0f; // Current UI height offset (slider - 0.5)
        private RTTFilePagination _queuePagination;

        // Tracked picture adjustment values (for Save as defaults)
        private SettingsSnapshot _currentPicture = new SettingsSnapshot();
        private float _sideControlsBaseX;  // base X offset from BuildPlayerUI (unscaled)
        private float _sideControlsBaseY;  // base Y from BuildPlayerUI (flat mode)
        private float _sidePhysicalW;      // side controls frame physical width (meters)
        private float _sidePhysicalH;      // side controls frame physical height (meters)
        private float _settingsPhysicalW;  // settings frame physical width (meters)
        private float _paginationWorldH;   // pagination quad world height (meters)
        private float _paginationGap = 0.015f; // gap between side controls bottom and pagination top

        // Cached rounded rect sprite for menu button
        private static Sprite _cachedRoundedRectSprite;

        // Direct play mode (standalone player from File Manager, no Library)
        private bool _isDirectPlay = false;
        private Action _onDirectPlayExit;
        private static VRMediaAppController _activeDirectPlayer = null;

        // Fade animation
        private const float FADE_OUT_DURATION = 0.15f;
        private const float FADE_IN_DURATION = 0.2f;
        private Coroutine _transitionCoroutine;
        #endregion

        #region Public API
        /// <summary>
        /// Create the Media app UI within the given container.
        /// Called by RTTManager when opening the Media app.
        /// </summary>
        public GameObject CreateMenu(RectTransform container, float containerW, float containerH,
            TMP_FontAsset font, Color primaryColor, Color accentColor)
        {
            _container = container;
            _containerWidth = containerW;
            _containerHeight = containerH;
            _font = font;
            _primaryColor = primaryColor;
            _accentColor = accentColor;

            // Create main view object
            _viewObject = new GameObject("VRMediaApp");
            _viewObject.transform.SetParent(container, false);

            RectTransform rt = _viewObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Initialize core systems
            InitializePlaybackSystem();
            InitializeProjectionSystem();

            // Build initial UI (Library mode)
            BuildLibraryUI();

            Debug.Log("[VRMediaAppController] Media app created");
            return _viewObject;
        }

        /// <summary>
        /// Initialize for direct video playback (standalone, no Library UI).
        /// Used by File Manager to open videos without creating the Media app.
        /// </summary>
        public void InitializeForDirectPlay(float width, float height,
            TMP_FontAsset font, Color primaryColor, Color accentColor,
            Vector3 framePosition, Quaternion frameRotation, Action onExit)
        {
            // Close any existing direct player first
            if (_activeDirectPlayer != null && _activeDirectPlayer != this)
            {
                _activeDirectPlayer.ExitDirectPlay();
            }
            _activeDirectPlayer = this;

            _isDirectPlay = true;
            _onDirectPlayExit = onExit;
            _containerWidth = width;
            _containerHeight = height;
            _font = font;
            _primaryColor = primaryColor;
            _accentColor = accentColor;
            _menuFramePosition = framePosition;
            _menuFrameRotation = frameRotation;

            // Initialize playback + projection only (no Library)
            InitializePlaybackSystem();
            InitializeProjectionSystem();

            Debug.Log("[VRMediaAppController] Initialized for direct play (standalone, no Library)");
        }

        /// <summary>
        /// Play a video directly (standalone mode). Enters immersive, builds player UI, starts playback.
        /// </summary>
        public void PlayVideoDirectly(string path)
        {
            var videoInfo = MediaVideoInfo.FromPath(path);

            // Detect projection type
            videoInfo.Projection = ProjectionDetector.DetectProjection(
                path, videoInfo.Width, videoInfo.Height);

            CurrentVideo = videoInfo;
            CurrentMode = AppMode.Player;

            // Enter Immersive Mode (hides Taskbar and MenuFrame)
            RTTManager.Instance?.EnterImmersiveMode();

            // Build player UI on demand and show it
            ShowPlayerUI();

            // Populate queue
            if (_queuePanel != null)
            {
                var queue = MediaPlaylistService.Instance.GetPlaybackQueue();
                int currentIdx = MediaPlaylistService.Instance.CurrentQueueIndex;
                _queuePanel.SetQueue(queue, currentIdx);
            }

            // Start playback with player at alpha=0, then fade in
            SetAllPlayerFramesAlpha(0f);
            StartPlayback(videoInfo);

            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(FadeInPlayerAfterDirectPlay(videoInfo));
        }

        private IEnumerator FadeInPlayerAfterDirectPlay(MediaVideoInfo videoInfo)
        {
            yield return StartCoroutine(AnimatePlayerFade(0f, 1f, FADE_IN_DURATION));

            _transitionCoroutine = null;
            OnModeChanged?.Invoke(AppMode.Player);
            OnVideoStarted?.Invoke(videoInfo);
            Debug.Log($"[VRMediaAppController] Direct play started: {videoInfo.Title}");
        }

        /// <summary>
        /// Exit direct play mode - stops playback, restores caller state, destroys self.
        /// </summary>
        public void ExitDirectPlay()
        {
            Debug.Log("[VRMediaAppController] Exiting direct play mode");

            StopPlayback();

            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(FadeOutAndCleanupDirectPlay());
        }

        private IEnumerator FadeOutAndCleanupDirectPlay()
        {
            // Fade out player
            yield return StartCoroutine(AnimatePlayerFade(1f, 0f, FADE_OUT_DURATION));

            RTTManager.Instance?.ExitImmersiveMode();
            HidePlayerUI();

            // Invoke callback to re-show the caller (File Manager)
            _onDirectPlayExit?.Invoke();
            _onDirectPlayExit = null;

            // Cleanup and destroy
            Cleanup();

            if (_activeDirectPlayer == this)
                _activeDirectPlayer = null;

            Destroy(gameObject);
        }

        /// <summary>
        /// Clean up all resources when app is closed.
        /// </summary>
        public void Cleanup()
        {
            StopPlayback();

            // Unwire events
            if (_libraryController != null)
            {
                _libraryController.OnVideoPlayRequested -= HandleLibraryPlayRequested;
                _libraryController.OnCloseRequested -= HandleLibraryCloseRequested;
            }

            if (_queuePanel != null)
            {
                _queuePanel.OnItemClicked -= HandleQueueItemClicked;
                _queuePanel.OnShuffleClicked -= HandleQueueShuffleClicked;
            }

            if (PlaybackEngine != null)
            {
                PlaybackEngine.Dispose();
                Destroy(PlaybackEngine.gameObject);
                PlaybackEngine = null;
            }

            if (ProjectionSystem != null)
            {
                ProjectionSystem.Dispose();
                Destroy(ProjectionSystem.gameObject);
                ProjectionSystem = null;
            }

            // Cleanup library view (destroys ActionBar, side panels, etc.)
            if (_libraryView != null)
            {
                _libraryView.Cleanup();
                _libraryView = null;
            }
            _libraryController = null;

            // Cleanup player controller
            if (_playerController != null)
            {
                _playerController.OnBackToLibrary -= SwitchToLibrary;
                _playerController.OnPlaybackFailed -= HandlePlaybackFailed;
                _playerController.OnProjectionSettingsUpdated -= HandleProjectionSettingsUpdated;
                _playerController.OnVideoChanged -= HandleVideoChanged;
                Destroy(_playerController.gameObject);
                _playerController = null;
            }

            // Cleanup controls container (includes controls frame + overlay frame)
            if (_controlsContainer != null)
            {
                Destroy(_controlsContainer);
                _controlsContainer = null;
                _controlsFrameObject = null;
                _overlayFrameObject = null;
                _controlsCanvasBase = null;
            }

            // Cleanup menu button frame (separate, in VirtualObjects)
            if (_menuButtonFrameObject != null)
            {
                Destroy(_menuButtonFrameObject);
                _menuButtonFrameObject = null;
            }

            _controlsPanel = null;

            if (_viewObject != null)
            {
                Destroy(_viewObject);
                _viewObject = null;
            }

            Debug.Log("[VRMediaAppController] Cleanup completed");
        }

        /// <summary>
        /// Handle back button press.
        /// In Player mode: return to Library.
        /// In Library mode: close the app.
        /// </summary>
        public void HandleBack()
        {
            if (_isDirectPlay) { ExitDirectPlay(); return; }
            if (CurrentMode == AppMode.Player)
            {
                SwitchToLibrary();
            }
            else
            {
                OnBackClicked?.Invoke();
            }
        }

        /// <summary>
        /// Switch to Library mode (browse videos).
        /// </summary>
        public void SwitchToLibrary()
        {
            if (_isDirectPlay) { ExitDirectPlay(); return; }
            if (CurrentMode == AppMode.Library) return;

            StopPlayback();
            CurrentMode = AppMode.Library;

            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(SwitchToLibraryWithFade());
        }

        private IEnumerator SwitchToLibraryWithFade()
        {
            // Fade out player
            yield return StartCoroutine(AnimatePlayerFade(1f, 0f, FADE_OUT_DURATION));

            // Exit Immersive Mode (restores Taskbar and MenuFrame)
            RTTManager.Instance?.ExitImmersiveMode();

            // Show library UI at alpha=0, hide player
            ShowLibraryUI();
            HidePlayerUI();
            SetAllLibraryFramesAlpha(0f);

            // Fade in library frames
            yield return StartCoroutine(AnimateLibraryFade(0f, 1f, FADE_IN_DURATION));

            _transitionCoroutine = null;
            OnModeChanged?.Invoke(AppMode.Library);
            Debug.Log("[VRMediaAppController] Switched to Library mode");
        }

        /// <summary>
        /// Switch to Player mode and start playing video.
        /// </summary>
        public void SwitchToPlayer(MediaVideoInfo video)
        {
            CurrentVideo = video;
            CurrentMode = AppMode.Player;

            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(SwitchToPlayerWithFade(video));
        }

        private IEnumerator SwitchToPlayerWithFade(MediaVideoInfo video)
        {
            // Fade out library frames
            yield return StartCoroutine(AnimateLibraryFade(1f, 0f, FADE_OUT_DURATION));

            // Hide library UI (saves position)
            HideLibraryUI();

            // Enter Immersive Mode (hides Taskbar and MenuFrame)
            RTTManager.Instance?.EnterImmersiveMode();

            ShowPlayerUI();

            // Populate queue AFTER ShowPlayerUI (items need active parent for HoverEffectController.Awake)
            // but BEFORE StartPlayback so RTT frame renders correct content
            if (_queuePanel != null)
            {
                var queue = MediaPlaylistService.Instance.GetPlaybackQueue();
                int currentIdx = MediaPlaylistService.Instance.CurrentQueueIndex;
                _queuePanel.SetQueue(queue, currentIdx);
            }

            // Start playback with player at alpha=0
            SetAllPlayerFramesAlpha(0f);
            StartPlayback(video);

            // Fade in player
            yield return StartCoroutine(AnimatePlayerFade(0f, 1f, FADE_IN_DURATION));

            _transitionCoroutine = null;
            OnModeChanged?.Invoke(AppMode.Player);
            OnVideoStarted?.Invoke(video);
            Debug.Log($"[VRMediaAppController] Switched to Player mode: {video.Title}");
        }

        /// <summary>
        /// Play a video file by path.
        /// </summary>
        public void PlayVideo(string path)
        {
            var videoInfo = MediaVideoInfo.FromPath(path);

            // Detect projection type
            videoInfo.Projection = ProjectionDetector.DetectProjection(
                path,
                videoInfo.Width,
                videoInfo.Height
            );

            SwitchToPlayer(videoInfo);
        }

        #endregion

        #region Playback Control
        /// <summary>
        /// Start video playback using the player controller.
        /// </summary>
        private void StartPlayback(MediaVideoInfo video)
        {
            // Ensure player UI is built
            if (_playerController == null)
            {
                BuildPlayerUI();
            }

            // Position projection at menu frame location (if we have saved position)
            if (ProjectionSystem != null && _menuFramePosition != Vector3.zero)
            {
                ProjectionSystem.SetTargetPosition(_menuFramePosition, _menuFrameRotation);
            }

            // Position controls below the video screen (EXACT same as original)
            if (_controlsContainer != null && _menuFramePosition != Vector3.zero)
            {
                Vector3 controlsPos = _menuFramePosition + _menuFrameRotation * new Vector3(0, -0.625f, 0);
                _controlsContainer.transform.position = controlsPos;
                _controlsContainer.transform.rotation = Quaternion.identity;

                // _playerControlsGroup stays at local zero; UI Settings offsets applied on top
                if (_playerControlsGroup != null)
                {
                    _playerControlsBaseLocalPos = Vector3.zero;
                    ApplyUISettingsToControlsGroup();
                }

                // VideoControlsFrame faces the camera
                if (_controlsFrameObject != null)
                {
                    _controlsFrameObject.transform.rotation = _menuFrameRotation;
                }

                // SideControlsFrame faces camera independently
                UpdateSideControlsFacing();

                _controlsContainer.SetActive(true);
            }

            // Detect projection type for positioning
            ProjectionDetector.DetectProjectionAndStereo(
                video.Path, video.Width, video.Height,
                out var projType, out _);
            bool willBeImmersive = !ProjectionDetector.SupportsScreenSettings(projType);

            // Position menu button below the video screen (in VirtualObjects, follows zoom)
            if (_menuButtonFrameObject != null && _menuFramePosition != Vector3.zero)
            {
                if (willBeImmersive && Camera.main != null)
                {
                    PositionMenuButtonImmersive(Camera.main);
                }
                else
                {
                    PositionMenuButtonFlat();
                }
            }

            // Immersive mode: controls container follows camera position each frame
            if (willBeImmersive && Camera.main != null && _controlsContainer != null)
            {
                SetupControlsFollowCamera(Camera.main);
            }
            else
            {
                _controlsFollowCamera = false;
            }

            // Adjust side controls + pagination Y for immersive mode (raise to eye level)
            // This must run regardless of _menuButtonFrameObject state
            PositionSideControlsForProjection(willBeImmersive);

            // Use player controller to handle playback
            if (_playerController != null)
            {
                _playerController.PlayVideo(video);
            }
        }

        /// <summary>
        /// Stop current playback.
        /// </summary>
        private void StopPlayback()
        {
            if (_playerController != null)
            {
                _playerController.Stop();
            }

            CurrentVideo = null;
        }
        #endregion

        #region Initialization
        private void InitializePlaybackSystem()
        {
            GameObject engineObj = new GameObject("VideoPlaybackEngine");
            engineObj.transform.SetParent(transform);
            PlaybackEngine = engineObj.AddComponent<VideoPlaybackEngine>();
        }

        private void InitializeProjectionSystem()
        {
            // Find VirtualObjects to parent the projection for zoom support
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            Transform projectionParent = virtualObjects != null ? virtualObjects.transform : transform;

            if (virtualObjects != null)
            {
                Debug.Log("[VRMediaAppController] Projection will be parented to VirtualObjects for zoom support");
            }
            else
            {
                Debug.LogWarning("[VRMediaAppController] VirtualObjects not found, zoom may not work");
            }

            GameObject projectionObj = new GameObject("VRVideoProjectionSystem");
            projectionObj.transform.SetParent(projectionParent, false);
            ProjectionSystem = projectionObj.AddComponent<VRVideoProjectionSystem>();

            // Initialize with camera rig (or main camera if no rig)
            Transform cameraRig = Camera.main?.transform.parent ?? Camera.main?.transform;

            if (cameraRig == null)
            {
                Debug.LogWarning("[VRMediaAppController] Camera rig not found, using this transform as parent");
                cameraRig = transform;
            }
            else
            {
                Debug.Log($"[VRMediaAppController] Found camera rig: {cameraRig.name}");
            }

            ProjectionSystem.Initialize(cameraRig);
        }
        #endregion

        #region UI Management
        private void BuildLibraryUI()
        {
            // Get parent RTTMenuFrame for proper RTT rendering
            _parentMenuFrame = _viewObject.GetComponentInParent<RTTMenuFrame>();
            if (_parentMenuFrame == null)
            {
                Debug.LogError("[VRMediaAppController] No parent RTTMenuFrame found! UI will not render correctly.");
            }

            // Create Library View
            GameObject libraryObj = new GameObject("MediaLibrary");
            libraryObj.transform.SetParent(_viewObject.transform, false);

            RectTransform libraryRT = libraryObj.AddComponent<RectTransform>();
            libraryRT.anchorMin = Vector2.zero;
            libraryRT.anchorMax = Vector2.one;
            libraryRT.offsetMin = Vector2.zero;
            libraryRT.offsetMax = Vector2.zero;

            // Create View component first (but don't initialize yet)
            _libraryView = libraryObj.AddComponent<RTTMediaLibrary>();

            // Create and initialize Controller BEFORE view initialization
            // (so controller is ready when view's coroutines call OnViewReady)
            _libraryController = libraryObj.AddComponent<RTTMediaLibraryController>();
            _libraryController.Initialize(_libraryView);

            // Now initialize View - this starts coroutines that may call back to controller
            _libraryView.Initialize(_libraryController, _parentMenuFrame, _containerWidth, _containerHeight,
                _font, _primaryColor, _accentColor);

            // Wire events
            _libraryController.OnVideoPlayRequested += HandleLibraryPlayRequested;
            _libraryController.OnCloseRequested += HandleLibraryCloseRequested;

            Debug.Log("[VRMediaAppController] Library UI built");
        }

        private void ShowLibraryUI()
        {
            Debug.Log("[VRMediaAppController] ShowLibraryUI");

            // Hide controls container (includes both controls frame and overlay)
            if (_controlsContainer != null)
            {
                _controlsContainer.SetActive(false);
            }

            // Hide menu button frame
            if (_menuButtonFrameObject != null)
            {
                _menuButtonFrameObject.SetActive(false);
            }

            // Hide video projection
            if (ProjectionSystem != null)
            {
                ProjectionSystem.Hide();
            }

            // Show main library view
            if (_libraryView != null)
            {
                _libraryView.gameObject.SetActive(true);
            }

            // Show all menu frames (main + side panels)
            _allMenuFrames = GetAllFrames();
            foreach (var frame in _allMenuFrames)
            {
                if (frame != null && frame.gameObject != null)
                {
                    frame.gameObject.SetActive(true);
                }
            }

            // Show parent menu frame
            if (_parentMenuFrame != null)
            {
                _parentMenuFrame.gameObject.SetActive(true);
            }
        }

        private void HideLibraryUI()
        {
            Debug.Log("[VRMediaAppController] HideLibraryUI - hiding all menu frames for video playback");

            // Store menu frame position before hiding (for positioning video screen)
            if (_parentMenuFrame != null)
            {
                _menuFramePosition = _parentMenuFrame.transform.position;
                _menuFrameRotation = _parentMenuFrame.transform.rotation;
                _menuFrameScale = _parentMenuFrame.transform.localScale;
                Debug.Log($"[VRMediaAppController] Saved menu frame position: {_menuFramePosition}, rotation: {_menuFrameRotation.eulerAngles}");
            }

            // Hide main library view
            if (_libraryView != null)
            {
                _libraryView.gameObject.SetActive(false);
            }

            // Hide all menu frames (main + side panels) so video is visible
            _allMenuFrames = GetAllFrames();
            foreach (var frame in _allMenuFrames)
            {
                if (frame != null && frame.gameObject != null)
                {
                    frame.gameObject.SetActive(false);
                }
            }

            // Also hide parent menu frame
            if (_parentMenuFrame != null)
            {
                _parentMenuFrame.gameObject.SetActive(false);
            }
        }

        private void BuildPlayerUI()
        {
            // Initialize per-video settings cache (no-op if already loaded)
            VideoSettingsCache.LoadFromDisk();

            int vLayer = LayerMask.NameToLayer("VirtualObjects");
            if (vLayer < 0) vLayer = 0;

            GameObject virtualObjects = GameObject.Find("VirtualObjects");

            // === 1. Root container (NOT under VirtualObjects so it ignores Zoom) ===
            // Note: This means Recenter must be handled manually in VRVideoPlayerController
            _controlsContainer = new GameObject("VideoControlsContainer");
            _controlsContainer.transform.SetParent(transform, false);


            // === 1b. Player Controls Group (sub-container for controls/side/overlay) ===
            // UI Settings adjusts this group's position/scale, keeping _controlsContainer at origin
            // so UISettingsPopup (sibling) stays centered
            _playerControlsGroup = new GameObject("PlayerControlsGroup");
            _playerControlsGroup.transform.SetParent(_controlsContainer.transform, false);

            // === 2. Dismiss overlay frame (large transparent click-to-dismiss) ===
            _overlayFrameObject = new GameObject("DismissOverlayFrame");
            _overlayFrameObject.transform.SetParent(_playerControlsGroup.transform);
            // Offset overlay behind controls frame so raycast hits controls first
            _overlayFrameObject.transform.localPosition = new Vector3(0, 0, 0.5f);
            _overlayFrameObject.layer = vLayer;

            var overlayFrame = _overlayFrameObject.AddComponent<RTTMenuFrame>();
            float overlaySize = 5f;
            float overlayPixels = 64f;
            overlayFrame.Configure(overlaySize, overlaySize, overlayPixels);
            overlayFrame.SetGlassBackgroundEnabled(false);
            overlayFrame.SetFloatingDataEnabled(false);
            overlayFrame.SetContentMargins(0, 0, 0, 0);
            overlayFrame.ForceInitialize();

            var overlayQuad = overlayFrame.GetDisplayQuad();
            if (overlayQuad != null && overlayQuad.material != null)
                overlayQuad.material.renderQueue = 3050;

            var overlayContainer = overlayFrame.ContentContainer;
            if (overlayContainer != null)
            {
                GameObject dismissObj = new GameObject("DismissButton");
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
                dismissBtn.onClick.AddListener(() => {
                    _controlsPanel?.Hide();
                    _playerController?.HideProjectionPopup();
                    _playerController?.HideEnvironmentPopup();
                });

                var col = dismissObj.AddComponent<BoxCollider>();
                col.size = new Vector3(overlayPixels, overlayPixels, 10);
                col.center = new Vector3(0, 0, 5);
            }
            _overlayFrameObject.SetActive(false);

            // === 3. Controls frame (RTTMenuFrame with higher render priority) ===
            GameObject controlsFrameObj = new GameObject("VideoControlsFrame");
            controlsFrameObj.transform.SetParent(_playerControlsGroup.transform, false); // Use false to keep local transform
            controlsFrameObj.transform.localPosition = Vector3.zero; // Explicitly reset to zero
            controlsFrameObj.transform.localRotation = Quaternion.identity;
            controlsFrameObj.layer = vLayer;

            var controlsFrame = controlsFrameObj.AddComponent<RTTMenuFrame>();
            _controlsCanvasBase = controlsFrame; // Store for hover detection

            // Expand frame width by 5% on each side (10% total) to prevent button hover clipping
            float padding = _containerWidth * 0.05f;
            float expandedWidth = _containerWidth + (padding * 2);

            float controlsWidth = expandedWidth;
            float controlsHeight = 700f;
            float density = 1200f;
            float physicalWidth = controlsWidth / density;
            float physicalHeight = controlsHeight / density;

            controlsFrame.Configure(physicalWidth, physicalHeight, controlsWidth);
            controlsFrame.SetGlassBackgroundEnabled(false);
            controlsFrame.SetFloatingDataEnabled(false);
            controlsFrame.SetContentMargins(0, 0, 0, 0);
            controlsFrame.ForceInitialize();

            var controlsQuad = controlsFrame.GetDisplayQuad();
            if (controlsQuad != null && controlsQuad.material != null)
                controlsQuad.material.renderQueue = 3100;

            var container = controlsFrame.ContentContainer;
            if (container == null)
            {
                Debug.LogError("[VRMediaAppController] Cannot find container in controls frame");
                return;
            }

            GameObject controlsObj = new GameObject("ControlsPanel");
            controlsObj.transform.SetParent(container, false);

            var controlsRT = controlsObj.AddComponent<RectTransform>();
            controlsRT.anchorMin = Vector2.zero;
            controlsRT.anchorMax = Vector2.one;
            // Apply padding so panel stays original width centered
            controlsRT.offsetMin = new Vector2(padding, 0);
            controlsRT.offsetMax = new Vector2(-padding, 0);

            _controlsPanel = controlsObj.AddComponent<RTTMediaControlsPanel>();
            // Initialize with original width so internal layout is correct
            _controlsPanel.Initialize(_containerWidth, controlsHeight, _font, _primaryColor, _accentColor);

            _controlsFrameObject = controlsFrameObj;

            // === 3b. Side Controls Frame (generic container beside controls) ===
            _sideControlsFrameObject = new GameObject("SideControlsFrame");
            _sideControlsFrameObject.transform.SetParent(_playerControlsGroup.transform, false);
            _sideControlsFrameObject.layer = vLayer;

            var sideFrame = _sideControlsFrameObject.AddComponent<RTTMenuFrame>();
            float sideLogicalWidth = 741f;
            float sideLogicalHeight = 1351f;
            float sidePhysicalW = sideLogicalWidth / density;
            float sidePhysicalH = sideLogicalHeight / density;
            _sidePhysicalW = sidePhysicalW;
            _sidePhysicalH = sidePhysicalH;

            sideFrame.Configure(sidePhysicalW, sidePhysicalH, sideLogicalWidth);
            sideFrame.SetGlassBackgroundEnabled(false);
            sideFrame.SetFloatingDataEnabled(false);
            sideFrame.SetContentMargins(0, 0, 0, 0);
            sideFrame.ForceInitialize();

            var sideQuad = sideFrame.GetDisplayQuad();
            if (sideQuad?.material != null)
                sideQuad.material.renderQueue = 3100;

            // Queue panel provides its own gradient background
            var sideContainer = sideFrame.ContentContainer;

            // Create queue panel inside side controls frame
            if (sideContainer != null)
            {
                GameObject queueObj = new GameObject("QueuePanel");
                queueObj.transform.SetParent(sideContainer, false);
                var queueRT = queueObj.AddComponent<RectTransform>();
                queueRT.anchorMin = Vector2.zero;
                queueRT.anchorMax = Vector2.one;
                queueRT.offsetMin = Vector2.zero;
                queueRT.offsetMax = Vector2.zero;

                _queuePanel = queueObj.AddComponent<RTTMediaQueuePanel>();
                _queuePanel.Initialize(sideLogicalWidth, sideLogicalHeight, _font);
                _queuePanel.OnItemClicked += HandleQueueItemClicked;
                _queuePanel.OnShuffleClicked += HandleQueueShuffleClicked;
            }

            // Position beside controls frame
            float controlsPhysicalW = expandedWidth / density;
            float gapMeters = 0.02f;
            float xOffset = (controlsPhysicalW / 2f + gapMeters + sidePhysicalW / 2f) * _sideControlsSide;
            float oneItemHeight = sidePhysicalH * 0.9f * 0.4f; // body(90%) × itemRatio(40%)
            float yOffset = 0.625f + oneItemHeight * 0.5f;
            _sideControlsBaseX = xOffset;
            _sideControlsBaseY = yOffset;
            _sideControlsFrameObject.transform.localPosition = new Vector3(xOffset, yOffset, 0);

            _sideControlsFrameObject.SetActive(false);

            // Pre-calculate pagination world height (needed for Settings frame sizing below)
            float paginationPixelToMeter = 1.6f / 1920f;
            _paginationWorldH = 115f * paginationPixelToMeter; // RTTFilePagination default height

            // === 3b2. Settings Frame (own RTTMenuFrame, 1:1.5 ratio, spans Queue top to Pagination bottom) ===
            {
                float settingsTotalH = sidePhysicalH + _paginationGap + _paginationWorldH;
                float settingsLogicalH = settingsTotalH * density;
                float settingsLogicalW = settingsLogicalH / 1.5f;
                float settingsPhysicalW = settingsLogicalW / density;
                float settingsPhysicalH = settingsTotalH;
                _settingsPhysicalW = settingsPhysicalW;

                _settingsFrameObject = new GameObject("SettingsFrame");
                _settingsFrameObject.transform.SetParent(_playerControlsGroup.transform, false);
                _settingsFrameObject.layer = vLayer;

                var settingsMenuFrame = _settingsFrameObject.AddComponent<RTTMenuFrame>();
                settingsMenuFrame.Configure(settingsPhysicalW, settingsPhysicalH, settingsLogicalW);
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
                    GameObject settingsObj = new GameObject("SettingsPanel");
                    settingsObj.transform.SetParent(settingsContainer, false);
                    var settingsRT = settingsObj.AddComponent<RectTransform>();
                    settingsRT.anchorMin = Vector2.zero;
                    settingsRT.anchorMax = Vector2.one;
                    settingsRT.offsetMin = Vector2.zero;
                    settingsRT.offsetMax = Vector2.zero;

                    _settingsPanel = settingsObj.AddComponent<RTTMediaSettingsPanel>();
                    _settingsPanel.Initialize(settingsLogicalW, settingsLogicalH, _font);

                    WireSettingsPanelEvents();
                }

                float settingsX = (controlsPhysicalW / 2f + gapMeters + settingsPhysicalW / 2f) * _sideControlsSide;
                float settingsY = _sideControlsBaseY - (_paginationGap + _paginationWorldH) / 2f;
                _settingsFrameObject.transform.localPosition = new Vector3(settingsX, settingsY, 0);

                _settingsFrameObject.SetActive(false);
            }

            // === 3c. Queue Pagination (floating glass panel below SideControlsFrame) ===
            if (_queuePanel != null)
            {
                GameObject paginationObj = new GameObject("QueuePagination");
                paginationObj.transform.SetParent(_playerControlsGroup.transform, false);
                paginationObj.layer = vLayer;

                _queuePagination = paginationObj.AddComponent<RTTFilePagination>();

                // Pagination width = Queue width + 2 arrow buttons
                float queuePixelW = sidePhysicalW / paginationPixelToMeter; // Queue width in RTT pixels
                float btnSize = Mathf.Round(Mathf.Clamp(queuePixelW * 0.16f, 50f, 90f));
                float paginationFrameW = queuePixelW + 2f * btnSize;
                _queuePagination.Initialize((IPaginationController)_queuePanel, paginationFrameW, 3);

                // Dark transparent background matching Queue theme
                _queuePagination.SetGlassColors(
                    new Color(0f, 0f, 0f, 0.45f),
                    new Color(0f, 0f, 0f, 0.45f),
                    0.5f, cyanRatio: 0f, fresnelStrength: 0f);

                // Selected page text = controls panel hover color (pastel red)
                _queuePagination.SetSelectedTextColor(new Color(1f, 0.32f, 0.32f, 1f));

                // Match render queue with SideControlsFrame for consistent z-ordering
                var paginationQuad = _queuePagination.GetDisplayQuad();
                if (paginationQuad?.material != null)
                    paginationQuad.material.renderQueue = 3100;

                // Position below SideControlsFrame
                float paginationY = yOffset - (sidePhysicalH / 2f) - _paginationGap - (_paginationWorldH / 2f);
                paginationObj.transform.localPosition = new Vector3(xOffset, paginationY, 0);

                // Wire up page change notifications
                _queuePanel.OnPageChanged += (current, total) =>
                {
                    if (_queuePagination != null)
                        _queuePagination.SetPage(current, total);
                };
            }

            // === 4. Menu button frame (in VirtualObjects → follows video screen with Zoom) ===
            _menuButtonFrameObject = new GameObject("MenuButtonFrame");
            if (virtualObjects != null)
                _menuButtonFrameObject.transform.SetParent(virtualObjects.transform);
            _menuButtonFrameObject.layer = vLayer;

            var menuFrame = _menuButtonFrameObject.AddComponent<RTTMenuFrame>();
            float menuBtnPixels = 90f;
            float menuFramePixels = 120f; // Larger than button so scale hover has room
            float menuFramePhysical = menuFramePixels / density;
            menuFrame.Configure(menuFramePhysical, menuFramePhysical, menuFramePixels);
            menuFrame.SetGlassBackgroundEnabled(false);
            menuFrame.SetFloatingDataEnabled(false);
            menuFrame.SetContentMargins(0, 0, 0, 0);
            menuFrame.ForceInitialize();

            var menuQuad = menuFrame.GetDisplayQuad();
            if (menuQuad != null)
            {
                _menuButtonQuadOriginalScale = menuQuad.transform.localScale;
                if (menuQuad.material != null)
                    menuQuad.material.renderQueue = 3100;
                menuQuad.gameObject.layer = vLayer; // Ensure raycast detection on VirtualObjects layer
            }

            var menuContainer = menuFrame.ContentContainer;
            if (menuContainer != null)
            {
                // Background
                GameObject btnObj = new GameObject("MenuButton");
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

                // Icon
                GameObject iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(btnObj.transform, false);
                var iconRT = iconObj.AddComponent<RectTransform>();
                float pad = menuBtnPixels * 0.22f; // Icon ≈ 56% of button height
                iconRT.anchorMin = Vector2.zero;
                iconRT.anchorMax = Vector2.one;
                iconRT.offsetMin = new Vector2(pad, pad);
                iconRT.offsetMax = new Vector2(-pad, -pad);

                var iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = Resources.Load<Sprite>("icon_menu");
                iconImg.preserveAspect = true;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;

                // Button
                var btn = btnObj.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() =>
                {
                    _controlsPanel?.Show();
                });

                // Hover effects - scale + subtle color tint on icon (white → light pastel accent)
                var hoverCtrl = btnObj.AddComponent<HoverEffectController>();
                hoverCtrl.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f));
                // Use Recenter Circle magenta as theme color
                Color themeColor = new Color(1f, 0.2f, 0.2f, 1f);
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
            _menuButtonFrameObject.SetActive(false); // Starts hidden (panel starts visible)

            // Pass external frame references to panel for visibility toggling
            _controlsPanel.SetExternalFrames(_overlayFrameObject, _menuButtonFrameObject);
            _controlsPanel.SetSideControlsFrame(_sideControlsFrameObject);
            _controlsPanel.SetQueuePagination(_queuePagination);
            // Pass settings panel + queue panel references for toggle logic
            if (_settingsPanel != null && _queuePanel != null)
                _controlsPanel.SetSettingsPanel(_settingsPanel, _queuePanel.gameObject, _settingsFrameObject, _sideControlsFrameObject);

            // Hide UI Settings popup and blocker when controls panel hides
            _controlsPanel.OnVisibilityChanged += (visible) =>
            {
                if (!visible)
                {
                    if (_uiSettingsPopup != null && _uiSettingsPopup.IsVisible)
                        _uiSettingsPopup.Hide();
                    _uiSettingsPopupFrame?.SetActive(false);
                    _uiSettingsBlocker?.SetActive(false);
                    _settingsPanel?.SetUISettingsRowForceHover(false);
                }
            };

            // === 5. Player Controller ===
            GameObject playerObj = new GameObject("PlayerController");
            playerObj.transform.SetParent(transform);

            _playerController = playerObj.AddComponent<VRVideoPlayerController>();
            _playerController.Initialize(PlaybackEngine, ProjectionSystem, _controlsPanel);
            _playerController.OnBackToLibrary += SwitchToLibrary;
            _playerController.OnPlaybackFailed += HandlePlaybackFailed;
            _playerController.OnProjectionSettingsUpdated += HandleProjectionSettingsUpdated;
            _playerController.OnVideoChanged += HandleVideoChanged;

            // Hide UI Settings popup when interacting with playback controls
            _controlsPanel.OnPlayPause += HideUISettingsPopup;
            _controlsPanel.OnSeek += (_) => HideUISettingsPopup();
            _controlsPanel.OnVolumeChanged += (_) => HideUISettingsPopup();
            _controlsPanel.OnEnvironmentClicked += HideUISettingsPopup;
            _controlsPanel.OnVRModeClicked += HideUISettingsPopup;
            _controlsPanel.OnHeadsetModeClicked += HideUISettingsPopup;
            _controlsPanel.OnRecenterClicked += HideUISettingsPopup;
            _controlsPanel.OnSettingsClicked += HideUISettingsPopup;

            // === 5b. Error Dialog ===
            GameObject errorDialogObj = new GameObject("MediaErrorDialog");
            errorDialogObj.transform.SetParent(container, false);

            var errorDialogRT = errorDialogObj.AddComponent<RectTransform>();
            errorDialogRT.anchorMin = Vector2.zero;
            errorDialogRT.anchorMax = Vector2.one;
            errorDialogRT.offsetMin = Vector2.zero;
            errorDialogRT.offsetMax = Vector2.zero;

            _errorDialog = errorDialogObj.AddComponent<MediaErrorDialog>();
            _errorDialog.Initialize(_font, _primaryColor, _accentColor);
            _errorDialog.OnBackClicked += () =>
            {
                _errorDialog.Hide();
                SwitchToLibrary();
            };
            _errorDialog.OnDismissed += () =>
            {
                _errorDialog.Hide();
                SwitchToLibrary();
            };
            _errorDialog.OnOpenExternalClicked += HandleOpenInExternalPlayer;
            _errorDialog.OnRetryClicked += HandleRetryOrConvert;

            // === 6. Popups (Projection & Environment) ===
            // Create them inside the controls frame container so they render on top of controls

            // Projection Settings Popup
            GameObject projectionObj = new GameObject("ProjectionPopup_Root");
            projectionObj.transform.SetParent(container, false);

            var projectionRT = projectionObj.AddComponent<RectTransform>();
            projectionRT.anchorMin = Vector2.zero;
            projectionRT.anchorMax = Vector2.one;
            projectionRT.offsetMin = Vector2.zero;
            projectionRT.offsetMax = Vector2.zero;

            float bottomOffset = RTTMediaControlsPanel.GetZoneBBottomOffset();
            var projectionPopup = projectionObj.AddComponent<RTTMediaProjectionPopup>();
            projectionPopup.Initialize(_font, _primaryColor, _accentColor, padding, bottomOffset, RTTMediaProjectionPopup.PopupMode.Projection);
            _playerController.SetProjectionPopup(projectionPopup);

            // Environment Popup
            GameObject envPopupObj = new GameObject("EnvironmentPopup_Root");
            envPopupObj.transform.SetParent(container, false);

            var envPopupRT = envPopupObj.AddComponent<RectTransform>();
            envPopupRT.anchorMin = Vector2.zero;
            envPopupRT.anchorMax = Vector2.one;
            envPopupRT.offsetMin = Vector2.zero;
            envPopupRT.offsetMax = Vector2.zero;

            var environmentPopup = envPopupObj.AddComponent<RTTMediaProjectionPopup>();
            environmentPopup.Initialize(_font, _primaryColor, _accentColor, padding, bottomOffset, RTTMediaProjectionPopup.PopupMode.Environment);
            _playerController.SetEnvironmentPopup(environmentPopup);

            // === 7a. UI Settings Blocker (invisible overlay to block interaction with other UI) ===
            _uiSettingsBlocker = new GameObject("UISettingsBlocker");
            _uiSettingsBlocker.transform.SetParent(_controlsContainer.transform, false);
            _uiSettingsBlocker.transform.localPosition = new Vector3(0, 0, 0.01f); // Slightly behind popup
            _uiSettingsBlocker.layer = vLayer;

            float blockerSize = 5f;
            float blockerPixels = 64f;
            var blockerFrame = _uiSettingsBlocker.AddComponent<RTTMenuFrame>();
            blockerFrame.Configure(blockerSize, blockerSize, blockerPixels);
            blockerFrame.SetGlassBackgroundEnabled(false);
            blockerFrame.SetFloatingDataEnabled(false);
            blockerFrame.SetContentMargins(0, 0, 0, 0);
            blockerFrame.ForceInitialize();

            var blockerQuad = blockerFrame.GetDisplayQuad();
            if (blockerQuad?.material != null)
                blockerQuad.material.renderQueue = 3150; // Between controls (3100) and popup (3200)

            var blockerContainer = blockerFrame.ContentContainer;
            if (blockerContainer != null)
            {
                GameObject blockerBtn = new GameObject("BlockerButton");
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
                blockerButton.onClick.AddListener(() =>
                {
                    _uiSettingsPopupFrame?.SetActive(false);
                    _uiSettingsBlocker?.SetActive(false);
                    _settingsPanel?.SetUISettingsRowForceHover(false);
                });

                var blockerCol = blockerBtn.AddComponent<BoxCollider>();
                blockerCol.size = new Vector3(blockerPixels, blockerPixels, 10);
                blockerCol.center = new Vector3(0, 0, 5);
            }
            _uiSettingsBlocker.SetActive(false);

            // === 7b. UI Settings Popup (beside SideControlsFrame, same Y level) ===
            float uiPopupLogicalW = Mathf.Round(expandedWidth * 0.42f);
            float uiPopupLogicalH = Mathf.Round(uiPopupLogicalW / 1.065f); // w:h = 1.065:1
            float uiPopupPhysW = uiPopupLogicalW / density;
            float uiPopupPhysH = uiPopupLogicalH / density;

            _uiSettingsPopupFrame = new GameObject("UISettingsPopupFrame");
            // Parent to _controlsContainer (sibling of _playerControlsGroup) so UI Settings
            // sliders (depth/height/scale) don't affect the popup itself
            _uiSettingsPopupFrame.transform.SetParent(_controlsContainer.transform, false);
            // Position set in LateUpdate relative to _playerControlsGroup base position
            _uiSettingsPopupFrame.transform.localPosition = new Vector3(0, _sideControlsBaseY, 0);
            _uiSettingsPopupFrame.transform.localRotation = Quaternion.identity;
            _uiSettingsPopupFrame.layer = vLayer;

            var uiPopupFrame = _uiSettingsPopupFrame.AddComponent<RTTMenuFrame>();
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
                _uiSettingsPopup = _uiSettingsPopupFrame.AddComponent<RTTMediaUISettingsPopup>();
                _uiSettingsPopup.Initialize(uiPopupContainer, uiPopupLogicalW, uiPopupLogicalH, _font, _primaryColor);
                WireUISettingsPopupEvents();
            }
            _uiSettingsPopupFrame.SetActive(false);

            Debug.Log("[VRMediaAppController] Player UI built with controls container");
        }

        private void ShowPlayerUI()
        {
            if (_controlsPanel == null)
            {
                BuildPlayerUI();
                LoadSavedSettings();
            }

            if (_controlsContainer != null)
            {
                _controlsContainer.SetActive(true);
            }

            if (_controlsPanel != null)
            {
                _controlsPanel.gameObject.SetActive(true);
                _controlsPanel.ResetToQueueView();
                _controlsPanel.Show();
            }
        }

        private void HidePlayerUI()
        {
            // Hide UI settings popup and blocker if open
            _uiSettingsPopupFrame?.SetActive(false);
            _uiSettingsBlocker?.SetActive(false);

            _controlsFollowCamera = false;

            if (_controlsPanel != null)
            {
                _controlsPanel.gameObject.SetActive(false);
            }

            if (_controlsContainer != null)
            {
                _controlsContainer.SetActive(false);
            }
        }

        private void HandleLibraryPlayRequested(MediaVideoInfo video)
        {
            SwitchToPlayer(video);
        }

        private void HandleLibraryCloseRequested()
        {
            OnBackClicked?.Invoke();
        }

        private void HandleQueueItemClicked(int index)
        {
            var service = MediaPlaylistService.Instance;
            if (service == null) return;

            string path = service.JumpToIndex(index);
            if (!string.IsNullOrEmpty(path))
            {
                _playerController?.PlayVideoSimple(path);
            }
        }

        private void HandleQueueShuffleClicked()
        {
            var service = MediaPlaylistService.Instance;
            if (service == null) return;

            service.SetShuffle(true);

            // Refresh queue display
            if (_queuePanel != null)
            {
                var queue = service.GetPlaybackQueue();
                int currentIdx = service.CurrentQueueIndex;
                _queuePanel.SetQueue(queue, currentIdx);
            }
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

        /// <summary>
        /// Wire all settings panel events to player controller and projection system.
        /// </summary>
        private void WireSettingsPanelEvents()
        {
            if (_settingsPanel == null) return;

            // Picture adjustments → shader params + track values
            _settingsPanel.OnSharpnessChanged += (v) => { _currentPicture.Sharpness = v; _playerController?.SetPictureAdjustment("_Sharpness", "_Sharpen", v); };
            _settingsPanel.OnBrightnessChanged += (v) => { _currentPicture.Brightness = v; _playerController?.SetPictureAdjustment("_Brightness", "_Brightness", v); };
            _settingsPanel.OnSaturationChanged += (v) => { _currentPicture.Saturation = v; _playerController?.SetPictureAdjustment("_Saturation", "_Saturation", v); };
            _settingsPanel.OnContrastChanged += (v) => { _currentPicture.Contrast = v; _playerController?.SetPictureAdjustment("_Contrast", "_Contrast", v); };
            _settingsPanel.OnTintChanged += (v) => { _currentPicture.Tint = v; _playerController?.SetPictureAdjustment("_Tint", "_Tint", v); };
            _settingsPanel.OnTemperatureChanged += (v) => { _currentPicture.Temperature = v; _playerController?.SetPictureAdjustment("_Temperature", "_Temperature", v); };

            _settingsPanel.OnPictureSaveDefaults += HandlePictureSaveDefaults;
            _settingsPanel.OnPictureResetDefaults += HandlePictureResetDefaults;

            // Video adjustments
            _settingsPanel.On3DChanged += (v) => _playerController?.SetStereoEnabled(v);
            _settingsPanel.OnLRInverseChanged += (v) => _playerController?.SetLRInverse(v);
            _settingsPanel.OnSpeedChanged += (v) =>
            {
                if (_playerController?.PlaybackEngine != null)
                    _playerController.PlaybackEngine.PlaybackSpeed = v;
                _controlsPanel?.SetSpeed(v);
                PlayerPrefs.SetFloat("MediaPlayer_Speed", v);
            };

            // Immersive adjustments
            _settingsPanel.OnTiltChanged += (v) => _playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetTilt(v);
            _settingsPanel.OnYawChanged += (v) => _playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetYawOffset(v);
            _settingsPanel.OnRollChanged += (v) => _playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetRollOffset(v);
            _settingsPanel.OnZoomChanged += (v) => _playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetFieldOfView(v);
            _settingsPanel.OnHeightChanged += (v) => _playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetVerticalShift(v);
            _settingsPanel.OnHorizontalBalanceChanged += (v) => _playerController?.ProjectionSystem?.GetImmersiveRenderer()?.SetHorizontalShift(v);

            // Screen settings (Flat)
            _settingsPanel.OnAspectRatioChanged += (v) => _playerController?.ProjectionSystem?.SetAspectRatioOverride(v);
            _settingsPanel.OnScreenDepthChanged += (v) => _playerController?.ProjectionSystem?.SetScreenDistance(v);
            _settingsPanel.OnScreenScaleChanged += (v) => _playerController?.ProjectionSystem?.SetScreenScale(v);
            _settingsPanel.OnVerticalMoveChanged += (v) => _playerController?.ProjectionSystem?.SetVerticalOffset(v);
            _settingsPanel.OnScreenSettingsReset += HandleScreenSettingsReset;

            // UI settings popup
            _settingsPanel.OnUISettingsRequested += HandleUISettingsRequested;

        }

        private void HandlePictureSaveDefaults()
        {
            // Get current slider values from the settings panel snapshot
            var snapshot = GetCurrentPictureSnapshot();
            PlayerPrefs.SetFloat("MediaPlayer_PictureSharpen", snapshot.Sharpness);
            PlayerPrefs.SetFloat("MediaPlayer_PictureBrightness", snapshot.Brightness);
            PlayerPrefs.SetFloat("MediaPlayer_PictureSaturation", snapshot.Saturation);
            PlayerPrefs.SetFloat("MediaPlayer_PictureContrast", snapshot.Contrast);
            PlayerPrefs.SetFloat("MediaPlayer_PictureTint", snapshot.Tint);
            PlayerPrefs.SetFloat("MediaPlayer_PictureTemperature", snapshot.Temperature);
            PlayerPrefs.Save();
            Debug.Log("[VRMediaAppController] Picture defaults saved to PlayerPrefs");
        }

        private void HandlePictureResetDefaults()
        {
            // Load saved defaults from PlayerPrefs (or factory defaults if none saved)
            float sharpen = PlayerPrefs.GetFloat("MediaPlayer_PictureSharpen", 0.5f);
            float brightness = PlayerPrefs.GetFloat("MediaPlayer_PictureBrightness", 1.0f);
            float saturation = PlayerPrefs.GetFloat("MediaPlayer_PictureSaturation", 1.0f);
            float contrast = PlayerPrefs.GetFloat("MediaPlayer_PictureContrast", 1.0f);
            float tint = PlayerPrefs.GetFloat("MediaPlayer_PictureTint", 0f);
            float temperature = PlayerPrefs.GetFloat("MediaPlayer_PictureTemperature", 0f);

            // Apply to renderer
            _playerController?.SetPictureAdjustment("_Sharpness", "_Sharpen", sharpen);
            _playerController?.SetPictureAdjustment("_Brightness", "_Brightness", brightness);
            _playerController?.SetPictureAdjustment("_Saturation", "_Saturation", saturation);
            _playerController?.SetPictureAdjustment("_Contrast", "_Contrast", contrast);
            _playerController?.SetPictureAdjustment("_Tint", "_Tint", tint);
            _playerController?.SetPictureAdjustment("_Temperature", "_Temperature", temperature);

            // Update UI sliders
            var snapshot = new SettingsSnapshot
            {
                Sharpness = sharpen,
                Brightness = brightness,
                Saturation = saturation,
                Contrast = contrast,
                Tint = tint,
                Temperature = temperature
            };
            _settingsPanel?.SetCurrentValues(snapshot);
            Debug.Log("[VRMediaAppController] Picture adjustments reset to saved defaults");
        }

        private void HandleScreenSettingsReset()
        {
            float depth = 2.0f;
            float scale = 1.0f;
            float verticalMove = 0f;
            string aspect = "default";

            _playerController?.ProjectionSystem?.SetScreenDistance(depth);
            _playerController?.ProjectionSystem?.SetScreenScale(scale);
            _playerController?.ProjectionSystem?.SetVerticalOffset(verticalMove);
            _playerController?.ProjectionSystem?.SetAspectRatioOverride(aspect);

            // Update UI sliders
            var snapshot = new SettingsSnapshot
            {
                ScreenDepth = depth,
                ScreenScale = scale,
                VerticalMove = verticalMove,
                AspectRatio = aspect
            };
            _settingsPanel?.SetCurrentValues(snapshot);
            Debug.Log("[VRMediaAppController] Screen settings reset to defaults");
        }

        /// <summary>
        /// Get current picture adjustment values (tracked from last slider changes).
        /// </summary>
        private SettingsSnapshot GetCurrentPictureSnapshot()
        {
            return new SettingsSnapshot
            {
                Sharpness = _currentPicture.Sharpness,
                Brightness = _currentPicture.Brightness,
                Saturation = _currentPicture.Saturation,
                Contrast = _currentPicture.Contrast,
                Tint = _currentPicture.Tint,
                Temperature = _currentPicture.Temperature
            };
        }

        /// <summary>
        /// Load saved picture defaults from PlayerPrefs and apply to renderer + UI.
        /// Called after BuildPlayerUI when starting playback.
        /// </summary>
        private void LoadSavedSettings()
        {
            // Picture adjustments (saved defaults)
            float sharpen = PlayerPrefs.GetFloat("MediaPlayer_PictureSharpen", 0.5f);
            float brightness = PlayerPrefs.GetFloat("MediaPlayer_PictureBrightness", 1.0f);
            float saturation = PlayerPrefs.GetFloat("MediaPlayer_PictureSaturation", 1.0f);
            float contrast = PlayerPrefs.GetFloat("MediaPlayer_PictureContrast", 1.0f);
            float tint = PlayerPrefs.GetFloat("MediaPlayer_PictureTint", 0f);
            float temperature = PlayerPrefs.GetFloat("MediaPlayer_PictureTemperature", 0f);

            _currentPicture.Sharpness = sharpen;
            _currentPicture.Brightness = brightness;
            _currentPicture.Saturation = saturation;
            _currentPicture.Contrast = contrast;
            _currentPicture.Tint = tint;
            _currentPicture.Temperature = temperature;

            // Apply to renderer
            _playerController?.SetPictureAdjustment("_Sharpness", "_Sharpen", sharpen);
            _playerController?.SetPictureAdjustment("_Brightness", "_Brightness", brightness);
            _playerController?.SetPictureAdjustment("_Saturation", "_Saturation", saturation);
            _playerController?.SetPictureAdjustment("_Contrast", "_Contrast", contrast);
            _playerController?.SetPictureAdjustment("_Tint", "_Tint", tint);
            _playerController?.SetPictureAdjustment("_Temperature", "_Temperature", temperature);

            // UI settings (global) - midpoint defaults: depth=0, height=0.5, scale=0.5
            // Version migration: clear stale values from old slider ranges
            const int UI_SETTINGS_VER = 3;
            if (PlayerPrefs.GetInt("MediaPlayer_UISettingsVer", 0) < UI_SETTINGS_VER)
            {
                PlayerPrefs.DeleteKey("MediaPlayer_UIDepth");
                PlayerPrefs.DeleteKey("MediaPlayer_UIHeight");
                PlayerPrefs.DeleteKey("MediaPlayer_UIScale");
                PlayerPrefs.SetInt("MediaPlayer_UISettingsVer", UI_SETTINGS_VER);
                PlayerPrefs.Save();
                Debug.Log("[VRMediaAppController] UI settings migrated to v" + UI_SETTINGS_VER + ", reset to defaults");
            }

            float uiDepth = Mathf.Clamp(PlayerPrefs.GetFloat("MediaPlayer_UIDepth", 0f), 0f, 1.0f);
            float uiHeight = Mathf.Clamp(PlayerPrefs.GetFloat("MediaPlayer_UIHeight", 0.5f), 0f, 1.0f);
            float uiScale = Mathf.Clamp(PlayerPrefs.GetFloat("MediaPlayer_UIScale", 0.5f), 0.2f, 1.0f);

            _uiSettingsPopup?.SetValues(uiDepth, uiHeight, uiScale);

            // Apply UI settings to player controls group (combined with base position)
            // Height: offset = (slider - 0.5), Scale: multiplier = slider * 2 (0.5 → 1.0x)
            _uiDepthOffset = uiDepth;
            _uiHeightOffset = uiHeight - 0.5f;
            ApplyUISettingsToControlsGroup();
            if (_playerControlsGroup != null)
            {
                float scaleMul = Mathf.Max(0.1f, uiScale * 2f);
                _playerControlsGroup.transform.localScale = Vector3.one * scaleMul;
            }

            // Speed (persist across sessions)
            float speed = PlayerPrefs.GetFloat("MediaPlayer_Speed", 1.0f);

            // Build and apply snapshot to settings panel
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
            _settingsPanel?.SetCurrentValues(snapshot);

            Debug.Log("[VRMediaAppController] Loaded saved settings from PlayerPrefs");
        }

        private void HandleUISettingsRequested()
        {
            if (_uiSettingsPopupFrame == null) return;

            bool isActive = _uiSettingsPopupFrame.activeSelf;
            bool showPopup = !isActive;

            if (showPopup)
            {
                _playerController?.HideAllPopups();
            }

            _uiSettingsPopupFrame.SetActive(showPopup);
            _uiSettingsBlocker?.SetActive(showPopup);
            _settingsPanel?.SetUISettingsRowForceHover(showPopup);
            Debug.Log("[VRMediaAppController] UI Settings popup toggled");
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

        private void WireUISettingsPopupEvents()
        {
            if (_uiSettingsPopup == null) return;

            // Close button → hide the popup frame and blocker
            _uiSettingsPopup.OnCloseRequested += () =>
            {
                _uiSettingsPopupFrame?.SetActive(false);
                _uiSettingsBlocker?.SetActive(false);
                _settingsPanel?.SetUISettingsRowForceHover(false);
            };

            // Depth: slider 0-1, push player controls group back by Z
            _uiSettingsPopup.OnUIDepthChanged += (v) =>
            {
                _uiDepthOffset = v;
                ApplyUISettingsToControlsGroup();
                PlayerPrefs.SetFloat("MediaPlayer_UIDepth", v);
            };

            // Height: slider 0-1, midpoint 0.5 = current (no change)
            _uiSettingsPopup.OnUIHeightChanged += (v) =>
            {
                _uiHeightOffset = v - 0.5f;
                ApplyUISettingsToControlsGroup();
                PlayerPrefs.SetFloat("MediaPlayer_UIHeight", v);
            };

            // Scale: slider 0.2-1.0, midpoint 0.5 = current size (1.0x)
            // scale = value * 2 → 0.2=0.4x, 0.5=1.0x, 1.0=2.0x
            _uiSettingsPopup.OnUIScaleChanged += (v) =>
            {
                float scale = Mathf.Max(0.1f, v * 2f);
                if (_playerControlsGroup != null)
                {
                    _playerControlsGroup.transform.localScale = Vector3.one * scale;
                }
                PlayerPrefs.SetFloat("MediaPlayer_UIScale", v);
            };

            _uiSettingsPopup.OnUISettingsReset += () =>
            {
                _uiDepthOffset = 0f;
                _uiHeightOffset = 0f;
                ApplyUISettingsToControlsGroup();
                if (_playerControlsGroup != null)
                {
                    _playerControlsGroup.transform.localScale = Vector3.one;
                }
                _uiSettingsPopup.SetValues(0f, 0.5f, 0.5f);
                PlayerPrefs.SetFloat("MediaPlayer_UIDepth", 0f);
                PlayerPrefs.SetFloat("MediaPlayer_UIHeight", 0.5f);
                PlayerPrefs.SetFloat("MediaPlayer_UIScale", 0.5f);
                PlayerPrefs.Save();
                Debug.Log("[VRMediaAppController] UI settings reset to defaults");
            };
        }

        private void ApplyUISettingsToControlsGroup()
        {
            if (_playerControlsGroup == null) return;
            _playerControlsGroup.transform.localPosition = _playerControlsBaseLocalPos + new Vector3(0, _uiHeightOffset, -_uiDepthOffset);
        }

        /// <summary>
        /// Scale the menu button's display quad without affecting the RTT canvas/camera.
        /// Scaling the frame object breaks RTT rendering (pixelation, lost corners).
        /// Scaling only the display quad preserves rendering quality while changing world size.
        /// </summary>
        private void ScaleMenuButtonQuad(float scaleFactor)
        {
            if (_menuButtonFrameObject == null) return;

            var menuFrame = _menuButtonFrameObject.GetComponent<RTTMenuFrame>();
            if (menuFrame == null) return;

            var quad = menuFrame.GetDisplayQuad();
            if (quad == null) return;

            quad.transform.localScale = new Vector3(
                _menuButtonQuadOriginalScale.x * scaleFactor,
                _menuButtonQuadOriginalScale.y * scaleFactor,
                _menuButtonQuadOriginalScale.z
            );
        }

        /// <summary>
        /// Position menu button to the LEFT of the video front direction in immersive mode.
        /// Button follows camera position each frame (via LateUpdate) so it appears
        /// fixed on the video sphere — no sliding across the video background.
        /// </summary>
        private void PositionMenuButtonImmersive(Camera cam)
        {
            if (_menuButtonFrameObject == null) return;

            Vector3 camForward = cam.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            // Left = rotate forward -60° around Y axis (not full 90° — visible in peripheral vision)
            Vector3 leftDir = Quaternion.AngleAxis(-60f, Vector3.up) * camForward;
            float distance = 2.0f;
            float scaleFactor = 1.0f; // same distance as flat — no scaling needed

            Vector3 menuBtnPos = cam.transform.position + leftDir * distance;
            menuBtnPos.y = cam.transform.position.y; // eye height
            _menuButtonFrameObject.transform.position = menuBtnPos;
            _menuButtonFrameObject.transform.rotation = Quaternion.LookRotation(-leftDir, Vector3.up);
            ScaleMenuButtonQuad(scaleFactor);

            // Store offset for LateUpdate camera-follow (keeps button fixed on video sphere)
            _menuButtonFollowCamera = true;
            _menuButtonOffsetDir = leftDir;
            _menuButtonOffsetDist = distance;
            _menuButtonOffsetY = 0f; // eye height = no Y offset

            // Expand DisplayQuad collider for easier reticle targeting in VR
            var menuFrame = _menuButtonFrameObject.GetComponent<RTTMenuFrame>();
            if (menuFrame != null)
            {
                var quad = menuFrame.GetDisplayQuad();
                if (quad != null)
                {
                    var col = quad.GetComponent<BoxCollider>();
                    if (col != null) col.size = new Vector3(3f, 3f, 0.01f);
                }
            }
        }

        private void PositionMenuButtonFlat()
        {
            if (_menuButtonFrameObject == null) return;

            float menuBtnPhysical = 90f / 1200f;
            Vector3 menuBtnPos = _menuFramePosition + new Vector3(0, -0.5f - menuBtnPhysical * 1.5f, 0);
            _menuButtonFrameObject.transform.position = menuBtnPos;
            _menuButtonFrameObject.transform.rotation = _menuFrameRotation;
            ScaleMenuButtonQuad(1.0f);

            _menuButtonFollowCamera = false;

            // Expand DisplayQuad collider for easier reticle targeting (same as immersive)
            var menuFrame = _menuButtonFrameObject.GetComponent<RTTMenuFrame>();
            if (menuFrame != null)
            {
                var quad = menuFrame.GetDisplayQuad();
                if (quad != null)
                {
                    var col = quad.GetComponent<BoxCollider>();
                    if (col != null) col.size = new Vector3(3f, 2.5f, 0.01f);
                }
            }
        }

        /// <summary>
        /// Set up controls container to follow camera position in immersive mode.
        /// Calculates offset from camera to current controls position so LateUpdate
        /// can maintain it each frame — keeps controls fixed in video space.
        /// </summary>
        private void SetupControlsFollowCamera(Camera cam)
        {
            Vector3 controlsPos = _controlsContainer.transform.position;
            Vector3 camPos = cam.transform.position;

            // Horizontal offset direction (Y=0 plane)
            Vector3 horizontal = controlsPos - camPos;
            float yOffset = horizontal.y;
            horizontal.y = 0;
            float dist = horizontal.magnitude;
            Vector3 dir = dist > 0.001f ? horizontal / dist : cam.transform.forward;

            _controlsFollowCamera = true;
            _controlsOffsetDir = dir;
            _controlsOffsetDist = dist;
            _controlsOffsetY = yOffset;
        }

        /// <summary>
        /// Update SideControlsFrame rotation to face camera horizontally.
        /// Called after container positioning to ensure correct facing direction.
        /// </summary>
        private void UpdateSideControlsFacing()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (_sideControlsFrameObject != null)
            {
                Vector3 toCamera = cam.transform.position - _sideControlsFrameObject.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _sideControlsFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            if (_settingsFrameObject != null)
            {
                Vector3 toCamera = cam.transform.position - _settingsFrameObject.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _settingsFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            if (_queuePagination != null)
            {
                Vector3 toCamera = cam.transform.position - _queuePagination.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _queuePagination.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// Reposition side controls and pagination to their base positions.
        /// Since immersive and flat modes now use the same 2.0m distance with no
        /// quad scaling, positions are constant (baseX, baseY) regardless of mode.
        /// </summary>
        private void PositionSideControlsForProjection(bool isImmersive)
        {
            if (_sideControlsFrameObject != null)
            {
                _sideControlsFrameObject.transform.localPosition = new Vector3(
                    _sideControlsBaseX, _sideControlsBaseY, 0);

                if (_queuePagination != null)
                {
                    float pagY = _sideControlsBaseY - (_sidePhysicalH / 2f) - _paginationGap - (_paginationWorldH / 2f);
                    _queuePagination.transform.localPosition = new Vector3(
                        _sideControlsBaseX, pagY, 0);
                }
            }

            if (_settingsFrameObject != null)
            {
                float controlsPhysicalW = (_containerWidth * 1.1f) / 1200f; // expandedWidth / density
                float gapMeters = 0.02f;
                float settingsX = (controlsPhysicalW / 2f + gapMeters + _settingsPhysicalW / 2f) * _sideControlsSide;
                float settingsY = _sideControlsBaseY - (_paginationGap + _paginationWorldH) / 2f;
                _settingsFrameObject.transform.localPosition = new Vector3(settingsX, settingsY, 0);
            }
        }

        private void HandleProjectionSettingsUpdated(VideoProjectionType projection, StereoMode stereo)
        {
            bool isImmersive = !ProjectionDetector.SupportsScreenSettings(projection);

            if (_menuButtonFrameObject != null)
            {
                if (isImmersive && Camera.main != null)
                {
                    PositionMenuButtonImmersive(Camera.main);
                }
                else
                {
                    PositionMenuButtonFlat();
                }
            }

            // Controls container: follow camera in immersive, stop following in flat
            if (isImmersive && Camera.main != null && _controlsContainer != null)
            {
                SetupControlsFollowCamera(Camera.main);
            }
            else
            {
                _controlsFollowCamera = false;
            }

            // Reposition side controls for the new projection type
            PositionSideControlsForProjection(isImmersive);

            // Update settings panel mode (shows/hides Screen settings, toggles Video adj content)
            _settingsPanel?.SetMode(isImmersive);
        }

        private void HandlePlaybackFailed(string error, bool isCodecError, string codecName, string containerFormat)
        {
            if (_errorDialog == null)
            {
                SwitchToLibrary();
                return;
            }

            // Store info for action buttons
            _lastFailedVideoPath = _playerController?.CurrentVideo?.Path;
            _lastFailedContainerFormat = containerFormat;

            bool needsRemux = !string.IsNullOrEmpty(containerFormat) || isCodecError;

            if (needsRemux)
            {
                bool canRemux = !string.IsNullOrEmpty(FindFFmpeg());

                if (canRemux && !string.IsNullOrEmpty(_lastFailedVideoPath))
                {
                    // FFmpeg available - auto-remux immediately without user interaction
                    Debug.Log($"[VRMediaAppController] Auto-remuxing: container={containerFormat ?? "MP4"}, codec={codecName ?? "unknown"}");
                    StartCoroutine(RemuxAndPlayCoroutine(_lastFailedVideoPath));
                    return;
                }

                // FFmpeg not found - show dialog with appropriate options
                string formatInfo = !string.IsNullOrEmpty(containerFormat)
                    ? $"Format: {containerFormat}"
                    : $"Codec: {GetFriendlyCodecName(codecName ?? "unknown")}";

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                // Windows: offer to download FFmpeg
                _errorDialog.ShowError(
                    !string.IsNullOrEmpty(containerFormat) ? MediaErrorDialog.ErrorType.UnsupportedContainer : MediaErrorDialog.ErrorType.CodecNotSupported,
                    $"{formatInfo}\n\nFFmpeg is needed to convert this video.\nIt will be downloaded automatically (~80 MB).");
                _errorDialog.SetRetryLabel("Download & Convert");
    #else
                // Mobile/other: no download option, show Open in Player
                _errorDialog.ShowError(
                    !string.IsNullOrEmpty(containerFormat) ? MediaErrorDialog.ErrorType.UnsupportedContainer : MediaErrorDialog.ErrorType.CodecNotSupported,
                    formatInfo);
    #endif
            }
            else
            {
                _errorDialog.Show("Playback Error", error, showRetry: false, showOpenExternal: true);
            }
        }

        private void HandleRetryOrConvert()
        {
            if (string.IsNullOrEmpty(_lastFailedVideoPath))
            {
                _errorDialog?.Hide();
                SwitchToLibrary();
                return;
            }

            // If FFmpeg is available, remux directly
            if (!string.IsNullOrEmpty(FindFFmpeg()))
            {
                _errorDialog?.Hide();
                StartCoroutine(RemuxAndPlayCoroutine(_lastFailedVideoPath));
                return;
            }

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // FFmpeg not found - download it first, then remux
            StartCoroutine(DownloadAndConvertCoroutine(_lastFailedVideoPath));
    #else
            _errorDialog?.Hide();
            SwitchToLibrary();
    #endif
        }

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private IEnumerator DownloadAndConvertCoroutine(string videoPath)
        {
            // Show the dialog as progress display (keep it visible)
            _errorDialog?.Show("Preparing...", "Setting up video converter...", showRetry: false, showOpenExternal: false);

            bool downloadSuccess = false;
            yield return StartCoroutine(DownloadFFmpegCoroutine(success => downloadSuccess = success));

            if (downloadSuccess && !string.IsNullOrEmpty(FindFFmpeg()))
            {
                _errorDialog?.Hide();
                StartCoroutine(RemuxAndPlayCoroutine(videoPath));
            }
            else
            {
                _errorDialog?.Show("Download Failed",
                    "Could not download FFmpeg.\nCheck your internet connection and try again.",
                    showRetry: true, showOpenExternal: true);
                _errorDialog?.SetRetryLabel("Retry Download");
            }
        }
    #endif

        private IEnumerator RemuxAndPlayCoroutine(string sourcePath)
        {
            string ffmpegPath = FindFFmpeg();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                Debug.LogError("[VRMediaAppController] FFmpeg not found");
                SwitchToLibrary();
                yield break;
            }

            // Create temp output path (same dir, .remuxed.mp4)
            string dir = Path.GetDirectoryName(sourcePath);
            string nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
            string outputPath = Path.Combine(dir, $"{nameNoExt}.remuxed.mp4");

            // Skip remux if already done
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024)
            {
                Debug.Log($"[VRMediaAppController] Using existing remuxed file: {outputPath}");
                PlayRemuxedVideo(outputPath, sourcePath);
                yield break;
            }

            Debug.Log($"[VRMediaAppController] Starting FFmpeg remux: {sourcePath} -> {outputPath}");

            // Show a simple progress indication
            _errorDialog?.Show("Converting...", "Remuxing video to MP4 format.\nThis should be fast (no re-encoding).", showRetry: false, showOpenExternal: false);

            // Run FFmpeg: copy all streams to MP4 container
            bool processComplete = false;
            bool processSuccess = false;
            string processError = null;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-i \"{sourcePath}\" -c copy -movflags faststart -y \"{outputPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        string stderr = process.StandardError.ReadToEnd();
                        process.WaitForExit(120000); // 2 minute timeout

                        if (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024)
                        {
                            processSuccess = true;
                        }
                        else
                        {
                            processError = $"FFmpeg exit code: {process.ExitCode}";
                            if (stderr.Length > 200)
                                processError += $"\n{stderr.Substring(stderr.Length - 200)}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    processError = ex.Message;
                }
                processComplete = true;
            });

            // Wait for FFmpeg to finish
            float remuxStart = Time.time;
            while (!processComplete)
            {
                if (Time.time - remuxStart > 130f) // 130s safety timeout
                {
                    processError = "Remux timeout";
                    break;
                }
                yield return null;
            }

            if (processSuccess)
            {
                Debug.Log($"[VRMediaAppController] Remux complete in {Time.time - remuxStart:F1}s: {outputPath}");
                _errorDialog?.Hide();
                PlayRemuxedVideo(outputPath, sourcePath);
            }
            else
            {
                Debug.LogError($"[VRMediaAppController] Remux failed: {processError}");
                // Show error and offer "Open in Player" instead
                _errorDialog?.Show("Conversion Failed", $"FFmpeg could not convert this video.\n\n{processError}", showRetry: false, showOpenExternal: true);
            }
        }

        private void PlayRemuxedVideo(string remuxedPath, string originalPath)
        {
            if (_playerController == null)
            {
                Debug.LogError("[VRMediaAppController] PlayerController is null, cannot play remuxed video");
                SwitchToLibrary();
                return;
            }

            // Create a MediaVideoInfo for the remuxed file based on the original
            var video = _playerController.CurrentVideo;
            if (video.HasValue)
            {
                var remuxedVideo = video.Value;
                remuxedVideo.Path = remuxedPath;

                Debug.Log($"[VRMediaAppController] Playing remuxed video: {remuxedPath}");
                _playerController.PlayVideo(remuxedVideo);
            }
            else
            {
                var remuxedVideo = MediaVideoInfo.FromPath(remuxedPath);
                _playerController.PlayVideo(remuxedVideo);
            }
        }

        private void HandleOpenInExternalPlayer()
        {
            _errorDialog?.Hide();

            if (!string.IsNullOrEmpty(_lastFailedVideoPath))
            {
                Debug.Log($"[VRMediaAppController] Opening in system player: {_lastFailedVideoPath}");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _lastFailedVideoPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRMediaAppController] Failed to open external player: {ex.Message}");
                }
            }

            SwitchToLibrary();
        }

        private static string GetFriendlyCodecName(string fourcc)
        {
            if (string.IsNullOrEmpty(fourcc)) return "Unknown";

            switch (fourcc.ToLowerInvariant())
            {
                case "hev1":
                case "hvc1":
                    return "HEVC (H.265)";
                case "av01":
                    return "AV1";
                case "avc1":
                case "avc3":
                    return "H.264";
                case "vp09":
                    return "VP9";
                default:
                    return fourcc.ToUpperInvariant();
            }
        }

        private static string _cachedFFmpegPath = null;
        private static bool _ffmpegSearched = false;

        /// <summary>
        /// Find FFmpeg executable in PATH or common locations.
        /// Searches auto-download location, system PATH, common install dirs,
        /// package managers (Scoop, Chocolatey), and sibling project directories.
        /// </summary>
        private static string FindFFmpeg()
        {
            if (_ffmpegSearched) return _cachedFFmpegPath;
            _ffmpegSearched = true;

            var searchPaths = new List<string>
            {
                // Auto-downloaded FFmpeg (highest priority - known good)
                Path.Combine(Application.persistentDataPath, "ffmpeg", "ffmpeg.exe"),
                // System PATH
                "ffmpeg",
                // Common install locations
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                // Unity project locations
                Path.Combine(Application.streamingAssetsPath, "ffmpeg.exe"),
                Path.Combine(Application.dataPath, "..", "ffmpeg", "ffmpeg.exe"),
                // Chocolatey
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
            };

            // Scoop (user-specific)
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                searchPaths.Add(Path.Combine(userProfile, "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe"));
            }

            // Sibling RemotePlayServer project (development environment)
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string remotePlayDir = Path.Combine(projectRoot, "RemotePlayServer", "bin");
                if (Directory.Exists(remotePlayDir))
                {
                    foreach (string config in new[] { "Debug", "Release" })
                    {
                        string configDir = Path.Combine(remotePlayDir, config);
                        if (Directory.Exists(configDir))
                        {
                            // Search in net* subdirectories (e.g. net9.0-windows10.0.26100.0)
                            foreach (string netDir in Directory.GetDirectories(configDir, "net*"))
                            {
                                string candidate = Path.Combine(netDir, "bin", "ffmpeg.exe");
                                searchPaths.Add(candidate);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors searching sibling projects
            }

            foreach (string path in searchPaths)
            {
                try
                {
                    // For file paths, check existence first to avoid slow process spawn
                    if (path != "ffmpeg" && !File.Exists(path))
                        continue;

                    var psi = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "-version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        process.WaitForExit(3000);
                        if (process.ExitCode == 0)
                        {
                            _cachedFFmpegPath = path;
                            Debug.Log($"[VRMediaAppController] Found FFmpeg at: {path}");
                            return path;
                        }
                    }
                }
                catch
                {
                    // Not found at this path, try next
                }
            }

            Debug.LogWarning("[VRMediaAppController] FFmpeg not found in PATH or common locations");
            return null;
        }

        /// <summary>
        /// Reset FFmpeg search cache so next FindFFmpeg() call re-searches.
        /// Call after auto-downloading FFmpeg.
        /// </summary>
        private static void ResetFFmpegCache()
        {
            _ffmpegSearched = false;
            _cachedFFmpegPath = null;
        }

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const string FFMPEG_DOWNLOAD_URL = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        private bool _isDownloadingFFmpeg = false;

        /// <summary>
        /// Download FFmpeg binary and save to persistentDataPath.
        /// Shows progress in the error dialog.
        /// </summary>
        private IEnumerator DownloadFFmpegCoroutine(System.Action<bool> onComplete)
        {
            if (_isDownloadingFFmpeg)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            _isDownloadingFFmpeg = true;

            string destDir = Path.Combine(Application.persistentDataPath, "ffmpeg");
            string destPath = Path.Combine(destDir, "ffmpeg.exe");

            // Already downloaded?
            if (File.Exists(destPath))
            {
                ResetFFmpegCache();
                _isDownloadingFFmpeg = false;
                onComplete?.Invoke(true);
                yield break;
            }

            _errorDialog?.UpdateProgress("Downloading FFmpeg...\nThis is a one-time download (~80 MB).");

            string zipPath = Path.Combine(Application.temporaryCachePath, "ffmpeg-download.zip");

            // Download
            using (var request = UnityWebRequest.Get(FFMPEG_DOWNLOAD_URL))
            {
                request.downloadHandler = new DownloadHandlerFile(zipPath) { removeFileOnAbort = true };
                var op = request.SendWebRequest();

                while (!op.isDone)
                {
                    float progress = request.downloadProgress;
                    if (progress >= 0)
                    {
                        int pct = Mathf.RoundToInt(progress * 100f);
                        _errorDialog?.UpdateProgress($"Downloading FFmpeg... {pct}%\nThis is a one-time download (~80 MB).");
                    }
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VRMediaAppController] FFmpeg download failed: {request.error}");
                    _isDownloadingFFmpeg = false;
                    onComplete?.Invoke(false);
                    yield break;
                }
            }

            _errorDialog?.UpdateProgress("Extracting FFmpeg...");
            yield return null;

            // Extract ffmpeg.exe from zip in background thread
            bool extractSuccess = false;
            string extractError = null;
            bool extractDone = false;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                                && entry.Length > 0)
                            {
                                entry.ExtractToFile(destPath, overwrite: true);
                                extractSuccess = true;
                                break;
                            }
                        }
                    }

                    // Clean up zip
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    extractError = ex.Message;
                }
                extractDone = true;
            });

            while (!extractDone) yield return null;

            _isDownloadingFFmpeg = false;

            if (extractSuccess)
            {
                Debug.Log($"[VRMediaAppController] FFmpeg downloaded to: {destPath}");
                ResetFFmpegCache();
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError($"[VRMediaAppController] FFmpeg extraction failed: {extractError}");
                onComplete?.Invoke(false);
            }
        }
    #endif
        #endregion

        #region IDataBindable Implementation
        /// <summary>
        /// Delegate to library controller - check if data is ready
        /// </summary>
        public bool IsDataReady => _libraryController?.IsDataReady ?? false;

        /// <summary>
        /// Delegate to library controller - check if preparing data
        /// </summary>
        public bool IsPreparingData => _libraryController?.IsPreparingData ?? false;

        /// <summary>
        /// Start preparing data in background - delegate to library controller
        /// </summary>
        public void PrepareDataAsync()
        {
            _libraryController?.PrepareDataAsync();
        }

        /// <summary>
        /// Get all frames (main + side panels) for coordinated fade animation.
        /// </summary>
        public List<RTTMenuFrame> GetAllFrames()
        {
            return _libraryController?.GetAllFrames() ?? new List<RTTMenuFrame>();
        }

        /// <summary>
        /// Bind cached data immediately (non-blocking) - delegate to library controller.
        /// </summary>
        public void BindCachedDataOrEmpty()
        {
            _libraryController?.BindCachedDataOrEmpty();
        }

        /// <summary>
        /// Called when background data loading completes - delegate to library controller.
        /// </summary>
        public void OnBackgroundDataReady()
        {
            _libraryController?.OnBackgroundDataReady();
        }

        /// <summary>
        /// Show loading spinner - delegate to library controller
        /// </summary>
        public void ShowLoadingSpinner()
        {
            _libraryController?.ShowLoadingSpinner();
        }

        /// <summary>
        /// Hide loading spinner - delegate to library controller
        /// </summary>
        public void HideLoadingSpinner()
        {
            _libraryController?.HideLoadingSpinner();
        }

        /// <summary>
        /// Called when app is fully visible after transition
        /// </summary>
        public void OnAppShown()
        {
            _libraryController?.OnAppShown();
        }

        #region State Caching Support (delegated to library controller)

        /// <summary>
        /// Whether this app supports state caching - delegate to library controller
        /// </summary>
        public bool SupportsStateCaching => _libraryController?.SupportsStateCaching ?? false;

        /// <summary>
        /// Try to restore UI state from cache - delegate to library controller
        /// </summary>
        public bool TryRestoreCachedState()
        {
            return _libraryController?.TryRestoreCachedState() ?? false;
        }

        /// <summary>
        /// Save current UI state to cache - delegate to library controller
        /// </summary>
        public void CacheCurrentState()
        {
            _libraryController?.CacheCurrentState();
        }

        /// <summary>
        /// Get prepared data buffer - delegate to library controller
        /// </summary>
        public object GetPreparedDataBuffer()
        {
            return _libraryController?.GetPreparedDataBuffer();
        }

        /// <summary>
        /// Bind prepared data buffer - delegate to library controller
        /// </summary>
        public void BindPreparedData(object dataBuffer)
        {
            _libraryController?.BindPreparedData(dataBuffer);
        }

        /// <summary>
        /// Event fired when background data preparation completes.
        /// Forwards from library controller.
        /// </summary>
        public event Action OnDataPrepared
        {
            add
            {
                if (_libraryController != null)
                    _libraryController.OnDataPrepared += value;
            }
            remove
            {
                if (_libraryController != null)
                    _libraryController.OnDataPrepared -= value;
            }
        }

        #endregion
        #endregion

        #region Helpers
        private static Sprite CreateRoundedRectSprite()
        {
            if (_cachedRoundedRectSprite != null) return _cachedRoundedRectSprite;
            int size = 64;
            int radius = 8; // ~12% corner radius — subtle rounding
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
            _cachedRoundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
            return _cachedRoundedRectSprite;
        }
        #endregion

        #region Unity Lifecycle
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // Controls container: follow camera position in immersive mode (stays fixed in video space)
            if (_controlsFollowCamera && _controlsContainer != null && _controlsContainer.activeInHierarchy)
            {
                Vector3 pos = cam.transform.position + _controlsOffsetDir * _controlsOffsetDist;
                pos.y = cam.transform.position.y + _controlsOffsetY;
                _controlsContainer.transform.position = pos;
            }

            // Face-to-camera for VideoControlsFrame (child of the identity-rotated container)
            if (_controlsFrameObject != null && _controlsFrameObject.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _controlsFrameObject.transform.position;
                if (toCamera.sqrMagnitude > 0.001f)
                    _controlsFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // Menu button: follow camera position in immersive mode (appears fixed on video sphere)
            if (_menuButtonFrameObject != null && _menuButtonFrameObject.activeInHierarchy)
            {
                if (_menuButtonFollowCamera)
                {
                    // Immersive: keep button at fixed angular offset from camera center,
                    // matching the video sphere which also follows camera position.
                    Vector3 pos = cam.transform.position + _menuButtonOffsetDir * _menuButtonOffsetDist;
                    pos.y = cam.transform.position.y + _menuButtonOffsetY;
                    _menuButtonFrameObject.transform.position = pos;
                }

                // Face-to-camera rotation (both flat and immersive)
                Vector3 toCamera = cam.transform.position - _menuButtonFrameObject.transform.position;
                if (toCamera.sqrMagnitude > 0.001f)
                    _menuButtonFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // Face-to-camera for SideControlsFrame (fixes rotation on Android/Quest)
            if (_sideControlsFrameObject != null && _sideControlsFrameObject.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _sideControlsFrameObject.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    _sideControlsFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }

            // Face-to-camera for SettingsFrame (separate RTTMenuFrame for settings panel)
            if (_settingsFrameObject != null && _settingsFrameObject.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _settingsFrameObject.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    _settingsFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }

            // Face-to-camera for QueuePagination (uses own position for accurate facing)
            if (_queuePagination != null && _queuePagination.gameObject.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _queuePagination.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    _queuePagination.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }

            // UISettingsPopup: face-to-camera rotation only (position from localPosition in _controlsContainer)
            if (_uiSettingsPopupFrame != null && _uiSettingsPopupFrame.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _uiSettingsPopupFrame.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _uiSettingsPopupFrame.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            // UISettingsBlocker: face-to-camera (same as popup)
            if (_uiSettingsBlocker != null && _uiSettingsBlocker.activeInHierarchy)
            {
                Vector3 toCamera = cam.transform.position - _uiSettingsBlocker.transform.position;
                toCamera.y = 0;
                if (toCamera.sqrMagnitude > 0.001f)
                    _uiSettingsBlocker.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }


        }

        private void OnDestroy()
        {
            Cleanup();
        }
        #endregion

        #region Fade Animation
        private void SetRTTFrameAlpha(GameObject frameObj, float alpha)
        {
            if (frameObj == null) return;
            var frame = frameObj.GetComponent<RTTMenuFrame>();
            if (frame == null) return;
            var quad = frame.GetDisplayQuad();
            if (quad?.material != null)
                quad.material.color = new Color(1f, 1f, 1f, alpha);
        }

        private void CollectFrameMaterial(GameObject frameObj, List<Material> materials)
        {
            if (frameObj == null) return;
            var frame = frameObj.GetComponent<RTTMenuFrame>();
            if (frame == null) return;
            var quad = frame.GetDisplayQuad();
            if (quad?.material != null)
                materials.Add(quad.material);
        }

        private void SetProjectionAlpha(float alpha)
        {
            if (ProjectionSystem == null || ProjectionSystem.ActiveRenderer == null) return;

            if (ProjectionSystem.ActiveRenderer is FlatProjectionRenderer flatRenderer)
            {
                flatRenderer.SetBoardAlpha(alpha);
            }
            else if (ProjectionSystem.ActiveRenderer is ImmersiveSphereRenderer sphereRenderer)
            {
                sphereRenderer.SetBrightness(alpha);
            }
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
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, alpha);
            }

            SetProjectionAlpha(alpha);
        }

        private void SetAllLibraryFramesAlpha(float alpha)
        {
            if (_parentMenuFrame != null)
            {
                var quad = _parentMenuFrame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, alpha);
            }

            foreach (var frame in GetAllFrames())
            {
                if (frame == null) continue;
                var quad = frame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        private IEnumerator AnimatePlayerFade(float from, float to, float duration)
        {
            var materials = new List<Material>();
            CollectFrameMaterial(_controlsFrameObject, materials);
            CollectFrameMaterial(_overlayFrameObject, materials);
            CollectFrameMaterial(_sideControlsFrameObject, materials);
            CollectFrameMaterial(_menuButtonFrameObject, materials);

            if (_queuePagination != null)
            {
                var quad = _queuePagination.GetDisplayQuad();
                if (quad?.material != null)
                    materials.Add(quad.material);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(from, to, t);

                foreach (var mat in materials)
                    mat.color = new Color(1f, 1f, 1f, alpha);

                SetProjectionAlpha(alpha);
                yield return null;
            }

            foreach (var mat in materials)
                mat.color = new Color(1f, 1f, 1f, to);
            SetProjectionAlpha(to);
        }

        private IEnumerator AnimateLibraryFade(float from, float to, float duration)
        {
            var materials = new List<Material>();

            if (_parentMenuFrame != null)
            {
                var quad = _parentMenuFrame.GetDisplayQuad();
                if (quad?.material != null)
                    materials.Add(quad.material);
            }

            foreach (var frame in GetAllFrames())
            {
                if (frame == null) continue;
                var quad = frame.GetDisplayQuad();
                if (quad?.material != null)
                    materials.Add(quad.material);
            }

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

            foreach (var mat in materials)
                mat.color = new Color(1f, 1f, 1f, to);
        }
        #endregion
    }

}
