using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.RTT;

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
    /// Clean up all resources when app is closed.
    /// </summary>
    public void Cleanup()
    {
        StopPlayback();

        // Unwire library events
        if (_libraryController != null)
        {
            _libraryController.OnVideoPlayRequested -= HandleLibraryPlayRequested;
            _libraryController.OnCloseRequested -= HandleLibraryCloseRequested;
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
            Destroy(_playerController.gameObject);
            _playerController = null;
        }
        
        // Cleanup controls frame
        if (_controlsFrameObject != null)
        {
            Destroy(_controlsFrameObject);
            _controlsFrameObject = null;
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
        if (CurrentMode == AppMode.Library) return;

        StopPlayback();
        CurrentMode = AppMode.Library;

        // Show library UI, hide player
        ShowLibraryUI();
        HidePlayerUI();

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

        // Hide library UI, show player
        HideLibraryUI();
        ShowPlayerUI();

        // Start playback
        StartPlayback(video);

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
        
        // Position controls frame below the video screen
        if (_controlsFrameObject != null && _menuFramePosition != Vector3.zero)
        {
            // Calculate position below menu frame (offset downward in world space)
            Vector3 controlsPos = _menuFramePosition + _menuFrameRotation * new Vector3(0, -0.5f, 0);
            _controlsFrameObject.transform.position = controlsPos;
            _controlsFrameObject.transform.rotation = _menuFrameRotation;
            _controlsFrameObject.SetActive(true);
            Debug.Log($"[VRMediaAppController] Controls frame positioned at: {controlsPos}");
        }

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
        projectionObj.transform.SetParent(projectionParent);
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
        
        // Hide player controls frame
        if (_controlsFrameObject != null)
        {
            _controlsFrameObject.SetActive(false);
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
        // Find VirtualObjects for creating controls frame
        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects == null)
        {
            Debug.LogError("[VRMediaAppController] VirtualObjects not found! Cannot create player controls.");
            return;
        }
        
        // Create a new RTTMenuFrame for controls - positioned below video screen
        GameObject controlsFrameObj = new GameObject("VideoControlsFrame");
        controlsFrameObj.transform.SetParent(virtualObjects.transform);
        
        // Add RTTMenuFrame component for proper RTT rendering
        var controlsFrame = controlsFrameObj.AddComponent<RTTMenuFrame>();
        
        // Calculate controls frame size (same width as video, smaller height)
        float controlsWidth = _containerWidth;
        float controlsHeight = 150f;
        
        // Initialize the frame
        // Calculate physical dimensions based on 1200 pixels/meter density (standard for 1920px = 1.6m)
        float density = 1200f; 
        float physicalWidth = controlsWidth / density;
        float physicalHeight = controlsHeight / density;
        
        controlsFrame.Configure(physicalWidth, physicalHeight, controlsWidth);
        controlsFrame.ForceInitialize();
        
        // Get the canvas container for adding UI
        var container = controlsFrame.ContentContainer;
        if (container == null)
        {
            Debug.LogError("[VRMediaAppController] Cannot find container in controls frame");
            return;
        }
        
        // Create Controls Panel inside the frame
        GameObject controlsObj = new GameObject("ControlsPanel");
        controlsObj.transform.SetParent(container, false);

        var controlsRT = controlsObj.AddComponent<RectTransform>();
        controlsRT.anchorMin = Vector2.zero;
        controlsRT.anchorMax = Vector2.one;
        controlsRT.offsetMin = Vector2.zero;
        controlsRT.offsetMax = Vector2.zero;

        _controlsPanel = controlsObj.AddComponent<RTTMediaControlsPanel>();
        _controlsPanel.Initialize(controlsWidth, controlsHeight, _font, _primaryColor, _accentColor);

        // Position controls frame at bottom of menu frame position
        // (Will be repositioned in ShowPlayerUI based on video screen position)
        _controlsFrameObject = controlsFrameObj;
        
        // Create Settings Popup (inside existing viewObject since we need it in UI space)
        // For now keep it minimal - settings can be added later
        
        // Create Player Controller
        GameObject playerObj = new GameObject("PlayerController");
        playerObj.transform.SetParent(transform);

        _playerController = playerObj.AddComponent<VRVideoPlayerController>();
        _playerController.Initialize(PlaybackEngine, ProjectionSystem, _controlsPanel);

        // Wire events
        _playerController.OnBackToLibrary += SwitchToLibrary;

        Debug.Log("[VRMediaAppController] Player UI built with controls frame");
    }
    
    // Store controls frame reference
    private GameObject _controlsFrameObject;

    private void ShowPlayerUI()
    {
        if (_controlsPanel == null)
        {
            BuildPlayerUI();
        }

        if (_controlsPanel != null)
        {
            _controlsPanel.gameObject.SetActive(true);
            _controlsPanel.Show();
        }
        
        // Ensure controls frame is visible if it exists
        if (_controlsFrameObject != null)
        {
            _controlsFrameObject.SetActive(true);
        }
    }

    private void HidePlayerUI()
    {
        if (_controlsPanel != null)
        {
            _controlsPanel.gameObject.SetActive(false);
        }
        
        if (_controlsFrameObject != null)
        {
            _controlsFrameObject.SetActive(false);
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
