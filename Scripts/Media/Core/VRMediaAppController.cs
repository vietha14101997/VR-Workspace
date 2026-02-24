using UnityEngine;
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

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// Main controller for the Media App.
    /// Manages switching between Library mode (browse videos) and Player mode (playback).
    /// Follows the same MVVM pattern as RTTFileManagerController.
    /// </summary>
    public partial class VRMediaAppController : MonoBehaviour, IDataBindable
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

        #region Unity Lifecycle
        private void OnDestroy()
        {
            Cleanup();
        }
        #endregion
    }

}
