using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.HoverEffects;

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

    // Controls container (NOT in VirtualObjects → not affected by Zoom)
    private GameObject _controlsContainer;
    private GameObject _controlsFrameObject;
    private GameObject _overlayFrameObject;

    // Menu button frame (IN VirtualObjects → follows video screen with Zoom)
    private GameObject _menuButtonFrameObject;

    // Controls frame reference for hover detection
    private RTTCanvasBase _controlsCanvasBase;

    // Cached rounded rect sprite for menu button
    private static Sprite _cachedRoundedRectSprite;
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

        // Exit Immersive Mode (restores Taskbar and MenuFrame)
        RTTManager.Instance?.ExitImmersiveMode();

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

        // Hide library UI (saves position), show player
        HideLibraryUI();

        // Enter Immersive Mode (hides Taskbar and MenuFrame)
        RTTManager.Instance?.EnterImmersiveMode();

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
        
        // Position controls container below the video screen
        if (_controlsContainer != null && _menuFramePosition != Vector3.zero)
        {
            Vector3 controlsPos = _menuFramePosition + _menuFrameRotation * new Vector3(0, -0.625f, 0);
            _controlsContainer.transform.position = controlsPos;
            _controlsContainer.transform.rotation = _menuFrameRotation;
            _controlsContainer.SetActive(true);
        }

        // Position menu button below the video screen (in VirtualObjects, follows zoom)
        if (_menuButtonFrameObject != null && _menuFramePosition != Vector3.zero)
        {
            // Video screen bottom = _menuFramePosition - 0.5m (half of 1m screen height)
            // Menu button center below that with a small gap
            float menuBtnPhysical = 90f / 1200f; // 90px at density 1200
            Vector3 menuBtnPos = _menuFramePosition + _menuFrameRotation * new Vector3(0, -0.5f - menuBtnPhysical * 1.5f, 0);
            _menuButtonFrameObject.transform.position = menuBtnPos;
            _menuButtonFrameObject.transform.rotation = _menuFrameRotation;
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
        int vLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vLayer < 0) vLayer = 0;

        GameObject virtualObjects = GameObject.Find("VirtualObjects");

        // === 1. Root container (NOT under VirtualObjects so it ignores Zoom) ===
        // Note: This means Recenter must be handled manually in VRVideoPlayerController
        _controlsContainer = new GameObject("VideoControlsContainer");
        _controlsContainer.transform.SetParent(transform, false);


        // === 2. Dismiss overlay frame (large transparent click-to-dismiss) ===
        _overlayFrameObject = new GameObject("DismissOverlayFrame");
        _overlayFrameObject.transform.SetParent(_controlsContainer.transform);
        // Offset overlay behind controls frame so raycast hits controls first
        _overlayFrameObject.transform.localPosition = new Vector3(0, 0, 0.01f);
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
            dismissBtn.onClick.AddListener(() => _controlsPanel?.Hide());

            var col = dismissObj.AddComponent<BoxCollider>();
            col.size = new Vector3(overlayPixels, overlayPixels, 10);
            col.center = new Vector3(0, 0, -5);
        }
        _overlayFrameObject.SetActive(false);

        // === 3. Controls frame (RTTMenuFrame with higher render priority) ===
        GameObject controlsFrameObj = new GameObject("VideoControlsFrame");
        controlsFrameObj.transform.SetParent(_controlsContainer.transform, false); // Use false to keep local transform
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
                _controlsPanel?.ResetAutoHideTimer();
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

        // === 5. Player Controller ===
        GameObject playerObj = new GameObject("PlayerController");
        playerObj.transform.SetParent(transform);

        _playerController = playerObj.AddComponent<VRVideoPlayerController>();
        _playerController.Initialize(PlaybackEngine, ProjectionSystem, _controlsPanel);
        _playerController.OnBackToLibrary += SwitchToLibrary;

        // === 6. Popups (Settings & Queue) ===
        // Create them inside the controls frame container so they render on top of controls
        
        // Settings Popup
        GameObject settingsObj = new GameObject("SettingsPopup_Root");
        settingsObj.transform.SetParent(container, false);
        
        var settingsRT = settingsObj.AddComponent<RectTransform>();
        settingsRT.anchorMin = Vector2.zero;
        settingsRT.anchorMax = Vector2.one;
        settingsRT.offsetMin = Vector2.zero;
        settingsRT.offsetMax = Vector2.zero;
        
        var settingsPopup = settingsObj.AddComponent<RTTMediaSettingsPopup>();
        settingsPopup.Initialize(_font, _primaryColor, _accentColor);
        _playerController.SetSettingsPopup(settingsPopup);
        
        // Queue Popup
        GameObject queueObj = new GameObject("QueuePopup_Root");
        queueObj.transform.SetParent(container, false);
        
        var queueRT = queueObj.AddComponent<RectTransform>();
        queueRT.anchorMin = Vector2.zero;
        queueRT.anchorMax = Vector2.one;
        queueRT.offsetMin = Vector2.zero;
        queueRT.offsetMax = Vector2.zero;
        
        var queuePopup = queueObj.AddComponent<RTTMediaQueuePopup>();
        queuePopup.Initialize(_font, _primaryColor, _accentColor);
        _playerController.SetQueuePopup(queuePopup);

        Debug.Log("[VRMediaAppController] Player UI built with controls container");
    }

    private void ShowPlayerUI()
    {
        if (_controlsPanel == null)
        {
            BuildPlayerUI();
        }

        if (_controlsContainer != null)
        {
            _controlsContainer.SetActive(true);
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

        // Face-to-camera for controls container
        if (_controlsContainer != null && _controlsContainer.activeSelf)
        {
            Vector3 toCamera = cam.transform.position - _controlsContainer.transform.position;
            if (toCamera.sqrMagnitude > 0.001f)
                _controlsContainer.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }

        // Face-to-camera for menu button (in VirtualObjects, follows video screen)
        if (_menuButtonFrameObject != null && _menuButtonFrameObject.activeSelf)
        {
            Vector3 toCamera = cam.transform.position - _menuButtonFrameObject.transform.position;
            if (toCamera.sqrMagnitude > 0.001f)
                _menuButtonFrameObject.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }

        // Reset auto-hide when reticle is hovering the controls frame
        if (_controlsPanel != null && _controlsPanel.IsVisible && _controlsCanvasBase != null)
        {
            var raycastMgr = RTTRaycastManager.Instance;
            if (raycastMgr != null && raycastMgr.CurrentHit.isValid && raycastMgr.CurrentHit.panel == _controlsCanvasBase)
            {
                _controlsPanel.ResetAutoHideTimer();
            }
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }
    #endregion
}
