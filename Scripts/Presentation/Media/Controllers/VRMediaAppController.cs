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
using VRWorkspace.Presentation.Media.Controllers;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// Slim coordinator for the Media App.
    /// Manages switching between Library mode (browse videos) and Player mode (playback).
    /// Delegates UI construction to <see cref="MediaPlayerUIBuilder"/>,
    /// mode transitions/fades to <see cref="MediaPlaybackCoordinator"/>,
    /// and FFmpeg operations to <see cref="MediaFFmpegHandler"/>.
    /// </summary>
    public partial class VRMediaAppController : MonoBehaviour,
        IDataBindable,
        IPlaybackCoordinatorContext,
        IFFmpegHandlerContext
    {
        #region Enums
        public enum AppMode
        {
            Library,
            Player
        }
        #endregion

        #region Events
        public event Action OnBackClicked;
        public event Action<AppMode> OnModeChanged;
        public event Action<MediaVideoInfo> OnVideoStarted;
        #endregion

        #region Properties
        public AppMode CurrentMode { get; private set; } = AppMode.Library;
        public MediaVideoInfo? CurrentVideo { get; private set; }
        public VideoPlaybackEngine PlaybackEngine { get; private set; }
        public VRVideoProjectionSystem ProjectionSystem { get; private set; }
        public RTTMenuFrame SideControlsFrame => _sideControlsFrameObject?.GetComponent<RTTMenuFrame>();
        #endregion

        #region Private Fields – layout / scene
        private GameObject _viewObject;
        private RectTransform _container;
        private float _containerWidth;
        private float _containerHeight;
        private TMP_FontAsset _font;
        private Color _primaryColor;
        private Color _accentColor;

        // Library
        private RTTMediaLibrary _libraryView;
        private RTTMediaLibraryController _libraryController;
        private RTTMediaControlsPanel _controlsPanel;
        private VRVideoPlayerController _playerController;

        // Menu frame
        private RTTMenuFrame _parentMenuFrame;
        private List<RTTMenuFrame> _allMenuFrames;
        private Vector3 _menuFramePosition;
        private Quaternion _menuFrameRotation;
        private Vector3 _menuFrameScale;

        // Player UI game objects
        private GameObject _controlsContainer;
        private GameObject _controlsFrameObject;
        private GameObject _overlayFrameObject;
        private MediaErrorDialog _errorDialog;
        private string _lastFailedVideoPath;
        private string _lastFailedContainerFormat;

        private GameObject _menuButtonFrameObject;
        private Vector3 _menuButtonQuadOriginalScale;

        // VCS surface for mediaPlayer cursor bounds (Phase 1)
        private VRWorkspace.Presentation.Input.VCS.MediaPlayerHubSurfaceController _hubSurfaceController;

        // Immersive follow-camera state
        private bool _menuButtonFollowCamera;
        private Vector3 _menuButtonOffsetDir;
        private float _menuButtonOffsetDist;
        private float _menuButtonOffsetY;
        private bool _controlsFollowCamera;
        private Vector3 _controlsOffsetDir;
        private float _controlsOffsetDist;
        private float _controlsOffsetY;

        private RTTCanvasBase _controlsCanvasBase;

        // Side / Settings / Queue
        private GameObject _sideControlsFrameObject;
        private GameObject _settingsFrameObject;
        private int _sideControlsSide = 1;
        private RTTMediaQueuePanel _queuePanel;
        private RTTMediaSettingsPanel _settingsPanel;
        private RTTMediaUISettingsPopup _uiSettingsPopup;
        private GameObject _uiSettingsPopupFrame;
        private GameObject _uiSettingsBlocker;
        private GameObject _playerControlsGroup;
        private Vector3 _playerControlsBaseLocalPos;
        private float _uiDepthOffset;
        private float _uiHeightOffset;
        private RTTFilePagination _queuePagination;

        // Layout metrics (set from builder result)
        private float _sideControlsBaseX;
        private float _sideControlsBaseY;
        private float _sidePhysicalW;
        private float _sidePhysicalH;
        private float _settingsPhysicalW;
        private float _paginationWorldH;
        private float _paginationGap = 0.015f;

        // Picture settings tracking
        private SettingsSnapshot _currentPicture = new SettingsSnapshot();

        // Direct play mode
        private bool _isDirectPlay;
        private Action _onDirectPlayExit;
        private static VRMediaAppController _activeDirectPlayer;

        // Fade transition
        private Coroutine _transitionCoroutine;

        // Delegates to extracted helpers
        private MediaPlayerUIBuilder _uiBuilder;
        private MediaPlaybackCoordinator _playbackCoordinator;
        private MediaFFmpegHandler _ffmpegHandler;
        #endregion

        #region Public API

        public GameObject CreateMenu(RectTransform container, float containerW, float containerH,
            TMP_FontAsset font, Color primaryColor, Color accentColor)
        {
            _container = container;
            _containerWidth = containerW;
            _containerHeight = containerH;
            _font = font;
            _primaryColor = primaryColor;
            _accentColor = accentColor;

            _viewObject = new GameObject("VRMediaApp");
            _viewObject.transform.SetParent(container, false);

            RectTransform rt = _viewObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            InitializeHelpers();
            InitializePlaybackSystem();
            InitializeProjectionSystem();
            BuildLibraryUI();

            Debug.Log("[VRMediaAppController] Media app created");
            return _viewObject;
        }

        public void InitializeForDirectPlay(float width, float height,
            TMP_FontAsset font, Color primaryColor, Color accentColor,
            Vector3 framePosition, Quaternion frameRotation, Action onExit)
        {
            if (_activeDirectPlayer != null && _activeDirectPlayer != this)
                _activeDirectPlayer.ExitDirectPlay();
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

            InitializeHelpers();
            InitializePlaybackSystem();
            InitializeProjectionSystem();

            Debug.Log("[VRMediaAppController] Initialized for direct play");
        }

        public void PlayVideoDirectly(string path)
        {
            var videoInfo = MediaVideoInfo.FromPath(path);
            videoInfo.Projection = ProjectionDetector.DetectProjection(path, videoInfo.Width, videoInfo.Height);

            CurrentVideo = videoInfo;
            CurrentMode = AppMode.Player;

            RTTManager.Instance?.EnterImmersiveMode();
            ShowPlayerUI();

            if (_queuePanel != null)
            {
                var queue = MediaPlaylistService.Instance.GetPlaybackQueue();
                _queuePanel.SetQueue(queue, MediaPlaylistService.Instance.CurrentQueueIndex);
            }

            SetAllPlayerFramesAlpha(0f);
            StartPlayback(videoInfo);

            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = _playbackCoordinator.FadeInAfterDirectPlay(videoInfo, () =>
            {
                _transitionCoroutine = null;
                OnModeChanged?.Invoke(AppMode.Player);
                OnVideoStarted?.Invoke(videoInfo);
            });
        }

        public void ExitDirectPlay()
        {
            Debug.Log("[VRMediaAppController] Exiting direct play mode");
            StopPlayback();
            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = _playbackCoordinator.FadeOutAndCleanupDirectPlay(() =>
            {
                RTTManager.Instance?.ExitImmersiveMode();
                // Show the caller's UI (e.g. File Manager) FIRST, so its RTTMenuFrame surface
                // is already visible in VCS by the time HidePlayerUI() below unregisters the
                // player surface and re-homes the cursor. Doing it in the other order (as
                // this used to) left the cursor snapping onto a surface that hadn't flipped
                // visible yet, stranding it invisible with nowhere usable to land — same
                // class of bug SwitchToLibrary() avoids by calling ShowLibraryUI() first.
                _onDirectPlayExit?.Invoke();
                _onDirectPlayExit = null;
                HidePlayerUI();
                Cleanup();
                if (_activeDirectPlayer == this) _activeDirectPlayer = null;
                Destroy(gameObject);
            });
        }

        public void Cleanup()
        {
            StopPlayback();

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

            if (_libraryView != null) { _libraryView.Cleanup(); _libraryView = null; }
            _libraryController = null;

            if (_playerController != null)
            {
                _playerController.OnBackToLibrary -= SwitchToLibrary;
                _playerController.OnPlaybackFailed -= HandlePlaybackFailed;
                _playerController.OnProjectionSettingsUpdated -= HandleProjectionSettingsUpdated;
                _playerController.OnVideoChanged -= HandleVideoChanged;
                Destroy(_playerController.gameObject);
                _playerController = null;
            }

            if (_controlsContainer != null)
            {
                Destroy(_controlsContainer);
                _controlsContainer = null;
                _controlsFrameObject = null;
                _overlayFrameObject = null;
                _controlsCanvasBase = null;
            }

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

        public void HandleBack()
        {
            if (_isDirectPlay) { ExitDirectPlay(); return; }
            if (CurrentMode == AppMode.Player) SwitchToLibrary();
            else OnBackClicked?.Invoke();
        }

        public void SwitchToLibrary()
        {
            if (_isDirectPlay) { ExitDirectPlay(); return; }
            if (CurrentMode == AppMode.Library) return;

            StopPlayback();
            CurrentMode = AppMode.Library;

            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = _playbackCoordinator.SwitchToLibraryWithFade(() =>
            {
                _transitionCoroutine = null;
                OnModeChanged?.Invoke(AppMode.Library);
            });
        }

        public void SwitchToPlayer(MediaVideoInfo video)
        {
            CurrentVideo = video;
            CurrentMode = AppMode.Player;

            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = _playbackCoordinator.SwitchToPlayerWithFade(video, () =>
            {
                _transitionCoroutine = null;
                OnModeChanged?.Invoke(AppMode.Player);
                OnVideoStarted?.Invoke(video);
            });
        }

        public void PlayVideo(string path)
        {
            var videoInfo = MediaVideoInfo.FromPath(path);
            videoInfo.Projection = ProjectionDetector.DetectProjection(path, videoInfo.Width, videoInfo.Height);
            SwitchToPlayer(videoInfo);
        }

        #endregion

        #region IDataBindable Implementation

        public bool IsDataReady => _libraryController?.IsDataReady ?? false;
        public bool IsPreparingData => _libraryController?.IsPreparingData ?? false;

        public void PrepareDataAsync() => _libraryController?.PrepareDataAsync();

        public List<RTTMenuFrame> GetAllFrames() =>
            _libraryController?.GetAllFrames() ?? new List<RTTMenuFrame>();

        public void BindCachedDataOrEmpty() => _libraryController?.BindCachedDataOrEmpty();
        public void OnBackgroundDataReady() => _libraryController?.OnBackgroundDataReady();
        public void ShowLoadingSpinner() => _libraryController?.ShowLoadingSpinner();
        public void HideLoadingSpinner() => _libraryController?.HideLoadingSpinner();
        public void OnAppShown() => _libraryController?.OnAppShown();

        public bool SupportsStateCaching => _libraryController?.SupportsStateCaching ?? false;
        public bool TryRestoreCachedState() => _libraryController?.TryRestoreCachedState() ?? false;
        public void CacheCurrentState() => _libraryController?.CacheCurrentState();
        public object GetPreparedDataBuffer() => _libraryController?.GetPreparedDataBuffer();
        public void BindPreparedData(object dataBuffer) => _libraryController?.BindPreparedData(dataBuffer);

        public event Action OnDataPrepared
        {
            add { if (_libraryController != null) _libraryController.OnDataPrepared += value; }
            remove { if (_libraryController != null) _libraryController.OnDataPrepared -= value; }
        }

        #endregion

        #region IPlaybackCoordinatorContext

        VRVideoProjectionSystem IPlaybackCoordinatorContext.ProjectionSystem => ProjectionSystem;
        Vector3 IPlaybackCoordinatorContext.MenuFramePosition => _menuFramePosition;
        Quaternion IPlaybackCoordinatorContext.MenuFrameRotation => _menuFrameRotation;
        GameObject IPlaybackCoordinatorContext.ControlsContainer => _controlsContainer;
        GameObject IPlaybackCoordinatorContext.PlayerControlsGroup => _playerControlsGroup;
        GameObject IPlaybackCoordinatorContext.ControlsFrameObject => _controlsFrameObject;
        GameObject IPlaybackCoordinatorContext.OverlayFrameObject => _overlayFrameObject;
        GameObject IPlaybackCoordinatorContext.SideControlsFrameObject => _sideControlsFrameObject;
        GameObject IPlaybackCoordinatorContext.MenuButtonFrameObject => _menuButtonFrameObject;
        RTTMenuFrame IPlaybackCoordinatorContext.ParentMenuFrame => _parentMenuFrame;
        RTTFilePagination IPlaybackCoordinatorContext.QueuePagination => _queuePagination;
        RTTMediaQueuePanel IPlaybackCoordinatorContext.QueuePanel => _queuePanel;

        Vector3 IPlaybackCoordinatorContext.PlayerControlsBaseLocalPos
        {
            get => _playerControlsBaseLocalPos;
            set => _playerControlsBaseLocalPos = value;
        }

        bool IPlaybackCoordinatorContext.ControlsFollowCamera
        {
            get => _controlsFollowCamera;
            set => _controlsFollowCamera = value;
        }

        void IPlaybackCoordinatorContext.ApplyUISettingsToControlsGroup() => ApplyUISettingsToControlsGroup();
        void IPlaybackCoordinatorContext.UpdateSideControlsFacing() => UpdateSideControlsFacing();
        void IPlaybackCoordinatorContext.PositionMenuButtonImmersive(Camera cam) => PositionMenuButtonImmersive(cam);
        void IPlaybackCoordinatorContext.PositionMenuButtonFlat() => PositionMenuButtonFlat();
        void IPlaybackCoordinatorContext.SetupControlsFollowCamera(Camera cam) => SetupControlsFollowCamera(cam);
        void IPlaybackCoordinatorContext.PositionSideControlsForProjection(bool isImmersive) => PositionSideControlsForProjection(isImmersive);
        void IPlaybackCoordinatorContext.ShowPlayerUI() => ShowPlayerUI();
        void IPlaybackCoordinatorContext.HidePlayerUI() => HidePlayerUI();
        void IPlaybackCoordinatorContext.ShowLibraryUI() => ShowLibraryUI();
        void IPlaybackCoordinatorContext.HideLibraryUI() => HideLibraryUI();
        void IPlaybackCoordinatorContext.StartPlaybackInternal(MediaVideoInfo video) => StartPlayback(video);
        void IPlaybackCoordinatorContext.SetAllPlayerFramesAlpha(float alpha) => SetAllPlayerFramesAlpha(alpha);
        void IPlaybackCoordinatorContext.SetAllLibraryFramesAlpha(float alpha) => SetAllLibraryFramesAlpha(alpha);
        List<RTTMenuFrame> IPlaybackCoordinatorContext.GetAllLibraryFrames() => GetAllFrames();

        #endregion

        #region IFFmpegHandlerContext

        MediaErrorDialog IFFmpegHandlerContext.ErrorDialog => _errorDialog;
        VRVideoPlayerController IFFmpegHandlerContext.PlayerController => _playerController;

        string IFFmpegHandlerContext.LastFailedVideoPath
        {
            get => _lastFailedVideoPath;
            set => _lastFailedVideoPath = value;
        }

        string IFFmpegHandlerContext.LastFailedContainerFormat
        {
            get => _lastFailedContainerFormat;
            set => _lastFailedContainerFormat = value;
        }

        void IFFmpegHandlerContext.SwitchToLibrary() => SwitchToLibrary();

        #endregion

        #region Unity Lifecycle
        private void OnDestroy() => Cleanup();
        #endregion

        #region Initialization Helpers

        private void InitializeHelpers()
        {
            _uiBuilder = new MediaPlayerUIBuilder(
                transform, _container, _containerWidth, _font, _primaryColor, _accentColor);
            _playbackCoordinator = new MediaPlaybackCoordinator(this, this);
            _ffmpegHandler = new MediaFFmpegHandler(this, this);
        }

        private void InitializePlaybackSystem()
        {
            var engineObj = new GameObject("VideoPlaybackEngine");
            engineObj.transform.SetParent(transform);
            PlaybackEngine = engineObj.AddComponent<VideoPlaybackEngine>();
        }

        private void InitializeProjectionSystem()
        {
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            Transform projectionParent = virtualObjects != null ? virtualObjects.transform : transform;

            if (virtualObjects != null)
                Debug.Log("[VRMediaAppController] Projection parented to VirtualObjects for zoom support");
            else
                Debug.LogWarning("[VRMediaAppController] VirtualObjects not found, zoom may not work");

            var projectionObj = new GameObject("VRVideoProjectionSystem");
            projectionObj.transform.SetParent(projectionParent, false);
            ProjectionSystem = projectionObj.AddComponent<VRVideoProjectionSystem>();

            Transform cameraRig = Camera.main?.transform.parent ?? Camera.main?.transform;
            if (cameraRig == null)
            {
                Debug.LogWarning("[VRMediaAppController] Camera rig not found, using this transform");
                cameraRig = transform;
            }
            else
            {
                Debug.Log($"[VRMediaAppController] Found camera rig: {cameraRig.name}");
            }

            ProjectionSystem.Initialize(cameraRig);
        }

        #endregion
    }
}
