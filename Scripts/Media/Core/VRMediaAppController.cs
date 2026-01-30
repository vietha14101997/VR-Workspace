using UnityEngine;
using TMPro;
using System;

/// <summary>
/// Main controller for the Media App.
/// Manages switching between Library mode (browse videos) and Player mode (playback).
/// Follows the same MVVM pattern as RTTFileManagerController.
/// </summary>
public class VRMediaAppController : MonoBehaviour
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
    private RTTMediaSettingsPopup _settingsPopup;
    private MediaLoadingOverlay _loadingOverlay;
    private MediaErrorDialog _errorDialog;
    private VRVideoPlayerController _playerController;
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

        // Cleanup error dialog events
        if (_errorDialog != null)
        {
            _errorDialog.OnRetryClicked -= HandleErrorRetry;
            _errorDialog.OnBackClicked -= HandleErrorBack;
            _errorDialog.OnDismissed -= HandleErrorDismissed;
        }

        _controlsPanel = null;
        _settingsPopup = null;
        _loadingOverlay = null;
        _errorDialog = null;

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
        GameObject projectionObj = new GameObject("VRVideoProjectionSystem");
        projectionObj.transform.SetParent(transform);
        ProjectionSystem = projectionObj.AddComponent<VRVideoProjectionSystem>();

        // Initialize with camera rig (or main camera if no rig)
        Transform cameraRig = Camera.main?.transform.parent ?? Camera.main?.transform;
        ProjectionSystem.Initialize(cameraRig);
    }
    #endregion

    #region UI Management
    private void BuildLibraryUI()
    {
        // Get parent RTTMenuFrame for proper RTT rendering
        RTTMenuFrame menuFrame = _viewObject.GetComponentInParent<RTTMenuFrame>();
        if (menuFrame == null)
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
        _libraryView.Initialize(_libraryController, menuFrame, _containerWidth, _containerHeight,
            _font, _primaryColor, _accentColor);

        // Wire events
        _libraryController.OnVideoPlayRequested += HandleLibraryPlayRequested;
        _libraryController.OnCloseRequested += HandleLibraryCloseRequested;

        Debug.Log("[VRMediaAppController] Library UI built");
    }

    private void ShowLibraryUI()
    {
        if (_libraryView != null)
        {
            _libraryView.gameObject.SetActive(true);
        }
    }

    private void HideLibraryUI()
    {
        if (_libraryView != null)
        {
            _libraryView.gameObject.SetActive(false);
        }
    }

    private void BuildPlayerUI()
    {
        // Create Controls Panel
        GameObject controlsObj = new GameObject("ControlsPanel");
        controlsObj.transform.SetParent(_viewObject.transform, false);

        var controlsRT = controlsObj.AddComponent<RectTransform>();
        controlsRT.anchorMin = new Vector2(0.5f, 0);
        controlsRT.anchorMax = new Vector2(0.5f, 0);
        controlsRT.pivot = new Vector2(0.5f, 0);
        controlsRT.anchoredPosition = new Vector2(0, 30);
        controlsRT.sizeDelta = new Vector2(_containerWidth * 0.8f, 120);

        _controlsPanel = controlsObj.AddComponent<RTTMediaControlsPanel>();
        _controlsPanel.Initialize(_containerWidth * 0.8f, 120, _font, _primaryColor, _accentColor);

        // Create Settings Popup
        GameObject settingsObj = new GameObject("SettingsPopup");
        settingsObj.transform.SetParent(_viewObject.transform, false);

        var settingsRT = settingsObj.AddComponent<RectTransform>();
        settingsRT.anchorMin = new Vector2(0.5f, 0.5f);
        settingsRT.anchorMax = new Vector2(0.5f, 0.5f);
        settingsRT.pivot = new Vector2(0.5f, 0.5f);
        settingsRT.anchoredPosition = Vector2.zero;

        _settingsPopup = settingsObj.AddComponent<RTTMediaSettingsPopup>();
        _settingsPopup.Initialize(_font, _primaryColor, _accentColor);

        // Create Loading Overlay
        GameObject loadingObj = new GameObject("LoadingOverlay");
        loadingObj.transform.SetParent(_viewObject.transform, false);

        var loadingRT = loadingObj.AddComponent<RectTransform>();
        loadingRT.anchorMin = Vector2.zero;
        loadingRT.anchorMax = Vector2.one;
        loadingRT.offsetMin = Vector2.zero;
        loadingRT.offsetMax = Vector2.zero;

        _loadingOverlay = loadingObj.AddComponent<MediaLoadingOverlay>();
        _loadingOverlay.Initialize(_font, _primaryColor);

        // Create Error Dialog
        GameObject errorObj = new GameObject("ErrorDialog");
        errorObj.transform.SetParent(_viewObject.transform, false);

        var errorRT = errorObj.AddComponent<RectTransform>();
        errorRT.anchorMin = Vector2.zero;
        errorRT.anchorMax = Vector2.one;
        errorRT.offsetMin = Vector2.zero;
        errorRT.offsetMax = Vector2.zero;

        _errorDialog = errorObj.AddComponent<MediaErrorDialog>();
        _errorDialog.Initialize(_font, _primaryColor, _accentColor);
        _errorDialog.OnRetryClicked += HandleErrorRetry;
        _errorDialog.OnBackClicked += HandleErrorBack;
        _errorDialog.OnDismissed += HandleErrorDismissed;

        // Create Player Controller
        GameObject playerObj = new GameObject("PlayerController");
        playerObj.transform.SetParent(transform);

        _playerController = playerObj.AddComponent<VRVideoPlayerController>();
        _playerController.Initialize(PlaybackEngine, ProjectionSystem, _controlsPanel);
        _playerController.SetSettingsPopup(_settingsPopup);

        // Wire events
        _playerController.OnBackToLibrary += SwitchToLibrary;

        // Start hidden
        controlsObj.SetActive(false);

        Debug.Log("[VRMediaAppController] Player UI built");
    }

    private void HandleErrorRetry()
    {
        _errorDialog.Hide();
        if (CurrentVideo.HasValue)
        {
            StartPlayback(CurrentVideo.Value);
        }
    }

    private void HandleErrorBack()
    {
        _errorDialog.Hide();
        SwitchToLibrary();
    }

    private void HandleErrorDismissed()
    {
        _errorDialog.Hide();
    }

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
    }

    private void HidePlayerUI()
    {
        if (_controlsPanel != null)
        {
            _controlsPanel.gameObject.SetActive(false);
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

    #region Unity Lifecycle
    private void OnDestroy()
    {
        Cleanup();
    }
    #endregion
}
