using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Controller for VR video playback.
/// Connects VideoPlaybackEngine, VRVideoProjectionSystem, and RTTMediaControlsPanel.
/// </summary>
public class VRVideoPlayerController : MonoBehaviour
{
    #region Events
    public event Action OnBackToLibrary;
    public event Action<MediaVideoInfo> OnVideoEnded;
    #endregion

    #region Properties
    public bool IsPlaying => _playbackEngine?.IsPlaying ?? false;
    public double CurrentTime => _playbackEngine?.CurrentTime ?? 0;
    public double Duration => _playbackEngine?.Duration ?? 0;
    public MediaVideoInfo? CurrentVideo { get; private set; }
    #endregion

    #region Private Fields
    private VideoPlaybackEngine _playbackEngine;
    private VRVideoProjectionSystem _projectionSystem;
    private RTTMediaControlsPanel _controlsPanel;
    private RTTMediaProjectionPopup _projectionPopup;
    private RTTMediaProjectionPopup _environmentPopup;
    private MediaEnvironmentController _environmentController;

    private MediaVideoInfo? _currentVideo;
    private DisplaySettings _displaySettings;
    private bool _isInitialized = false;
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize the player controller with required components.
    /// </summary>
    public void Initialize(
        VideoPlaybackEngine playbackEngine,
        VRVideoProjectionSystem projectionSystem,
        RTTMediaControlsPanel controlsPanel)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[VRVideoPlayerController] Already initialized");
            return;
        }

        _playbackEngine = playbackEngine;
        _projectionSystem = projectionSystem;
        _controlsPanel = controlsPanel;
        _displaySettings = DisplaySettings.Default;

        // Setup environment controller - optional, player can work without it
        try
        {
            _environmentController = MediaEnvironmentController.Instance;
            _environmentController.Initialize();
            Debug.Log("[VRVideoPlayerController] Environment controller initialized");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VRVideoPlayerController] Environment control disabled: {ex.Message}");
            _environmentController = null;
        }

        WireEvents();
        
        // Sync initial state and subscribe to changes
        if (_environmentController != null)
        {
            _environmentController.OnLightsChanged += _ => SyncEnvironmentStateFromController();
            _environmentController.OnEnvironmentVisibilityChanged += _ => SyncEnvironmentStateFromController();
            SyncEnvironmentStateFromController();
        }

        _isInitialized = true;

        Debug.Log("[VRVideoPlayerController] Initialized");
    }

    private void WireEvents()
    {
        // Playback engine events
        if (_playbackEngine != null)
        {
            _playbackEngine.OnPrepareCompleted += HandleVideoPrepared;
            _playbackEngine.OnPlaybackEnded += HandlePlaybackEnded;
            _playbackEngine.OnError += HandlePlaybackError;
            _playbackEngine.OnStateChanged += HandleStateChanged;
            _playbackEngine.OnTimeUpdate += HandleTimeUpdate;
        }

        // Controls panel events
        if (_controlsPanel != null)
        {
            _controlsPanel.OnPlayPause += TogglePlayPause;
            _controlsPanel.OnSeek += Seek;
            _controlsPanel.OnVolumeChanged += SetVolume;
            _controlsPanel.OnSpeedChanged += SetPlaybackSpeed;
            _controlsPanel.OnBackClicked += HandleBackClicked;
            _controlsPanel.OnPrevious += PlayPreviousVideo;
            _controlsPanel.OnNext += PlayNextVideo;
            _controlsPanel.OnVRModeClicked += HandleVRModeClicked;
            _controlsPanel.OnHeadsetModeClicked += HandleHeadsetModeClicked;
            _controlsPanel.OnRecenterClicked += HandleRecenter;
            _controlsPanel.OnEnvironmentClicked += ShowEnvironmentPopup;
        }
    }



    /// <summary>
    /// Set the projection popup reference.
    /// </summary>
    public void SetProjectionPopup(RTTMediaProjectionPopup popup)
    {
        if (_projectionPopup != null)
        {
            UnwireProjectionEvents();
        }

        _projectionPopup = popup;

        if (_projectionPopup != null)
        {
            WireProjectionEvents();
        }
    }

    private void WireProjectionEvents()
    {
        if (_projectionPopup == null) return;
        _projectionPopup.OnSettingsChanged += HandleProjectionSettingsChanged;
        _projectionPopup.OnCloseRequested += HideProjectionPopup;
    }

    private void UnwireProjectionEvents()
    {
        if (_projectionPopup == null) return;
        _projectionPopup.OnSettingsChanged -= HandleProjectionSettingsChanged;
        _projectionPopup.OnCloseRequested -= HideProjectionPopup;
    }

    /// <summary>
    /// Set the environment popup reference.
    /// </summary>
    public void SetEnvironmentPopup(RTTMediaProjectionPopup popup)
    {
        if (_environmentPopup != null)
        {
            UnwireEnvironmentEvents();
        }

        _environmentPopup = popup;

        if (_environmentPopup != null)
        {
            WireEnvironmentEvents();
        }
    }

    private void WireEnvironmentEvents()
    {
        if (_environmentPopup == null) return;
        _environmentPopup.OnMonitorTypeChanged += HandleMonitorTypeChanged;
        _environmentPopup.OnEnvironmentChanged += HandleEnvironmentSettingsChanged;
        _environmentPopup.OnCloseRequested += HideEnvironmentPopup;
    }

    private void UnwireEnvironmentEvents()
    {
        if (_environmentPopup == null) return;
        _environmentPopup.OnMonitorTypeChanged -= HandleMonitorTypeChanged;
        _environmentPopup.OnEnvironmentChanged -= HandleEnvironmentSettingsChanged;
        _environmentPopup.OnCloseRequested -= HideEnvironmentPopup;
    }


    #endregion

    #region Public API
    /// <summary>
    /// Load and play a video.
    /// </summary>
    public void PlayVideo(MediaVideoInfo video)
    {
        if (!_isInitialized)
        {
            Debug.LogError("[VRVideoPlayerController] Not initialized");
            return;
        }

        _currentVideo = video;
        CurrentVideo = video;

        // Detect projection and stereo mode automatically
        var projectionType = ProjectionDetector.DetectProjection(video.Path, video.Width, video.Height);
        var stereoMode = ProjectionDetector.DetectStereoMode(projectionType, video.Path);

        Debug.Log($"[VRVideoPlayerController] Playing: {video.Title}, Detected Projection: {projectionType}, Stereo: {stereoMode}");

        // Configure projection system
        ApplyProjectionSettings(projectionType, stereoMode);

        // Load video
        if (_playbackEngine != null)
        {
            _playbackEngine.PrepareLocal(video.Path);
        }

        // Update controls
        if (_controlsPanel != null)
        {
            _controlsPanel.SetPlayState(false);
            _controlsPanel.SetCurrentTime(0);
            _controlsPanel.SetDuration(0);
            _controlsPanel.SetTitle(video.Title);
            _controlsPanel.Show();

            SetVolume(_controlsPanel.Volume);
        }

        SyncEnvironmentStateFromController();
    }

    private void ApplyProjectionSettings(VideoProjectionType projectionType, StereoMode stereoMode)
    {
        if (_projectionSystem == null) return;

        // Choose appropriate display settings
        _displaySettings = ProjectionDetector.SupportsScreenSettings(projectionType)
            ? DisplaySettings.Default
            : DisplaySettings.Immersive;

        // Flat projection: Distance=0 so screen sits at _projectionRoot position
        if (ProjectionDetector.SupportsScreenSettings(projectionType))
        {
            _displaySettings.Distance = 0f;
        }

        _projectionSystem.SetProjection(projectionType, stereoMode);
        _projectionSystem.UpdateDisplay(_displaySettings);
        _projectionSystem.RecenterView();

        // Update environment based on projection type
        if (_environmentController != null)
        {
            _environmentController.SetProjectionType(projectionType);
        }

        // Update popups if they are active
        _projectionPopup?.SetState(projectionType, ConvertToUIStereo(stereoMode));
    }

    private RTTMediaProjectionPopup.StereoMode ConvertToUIStereo(StereoMode mode)
    {
        switch (mode)
        {
            case StereoMode.SideBySide: return RTTMediaProjectionPopup.StereoMode.SideBySide;
            case StereoMode.OverUnder: return RTTMediaProjectionPopup.StereoMode.OverUnder;
            default: return RTTMediaProjectionPopup.StereoMode.Mono;
        }
    }

    private StereoMode ConvertFromUIStereo(RTTMediaProjectionPopup.StereoMode mode)
    {
        switch (mode)
        {
            case RTTMediaProjectionPopup.StereoMode.SideBySide: return StereoMode.SideBySide;
            case RTTMediaProjectionPopup.StereoMode.OverUnder: return StereoMode.OverUnder;
            default: return StereoMode.Mono;
        }
    }

    /// <summary>
    /// Toggle play/pause.
    /// </summary>
    public void TogglePlayPause()
    {
        if (_playbackEngine == null) return;

        if (_playbackEngine.IsPlaying)
        {
            _playbackEngine.Pause();
        }
        else
        {
            _playbackEngine.Play();
        }
    }

    /// <summary>
    /// Play video.
    /// </summary>
    public void Play()
    {
        _playbackEngine?.Play();
    }

    /// <summary>
    /// Pause video.
    /// </summary>
    public void Pause()
    {
        _playbackEngine?.Pause();
    }

    /// <summary>
    /// Stop playback.
    /// </summary>
    public void Stop()
    {
        _playbackEngine?.Stop();
        _projectionSystem?.Hide();

        // Reset environment to default state (preserve user preferences)
        _environmentController?.Reset(true);
        
        HideProjectionPopup();
        HideEnvironmentPopup();
    }

    /// <summary>
    /// Play next video in queue.
    /// </summary>
    public void PlayNextVideo()
    {
        var service = MediaPlaylistService.Instance;
        if (service == null) return;

        string nextPath = service.GetNextVideo();
        if (!string.IsNullOrEmpty(nextPath))
        {
            PlayVideoSimple(nextPath);
        }
    }

    /// <summary>
    /// Play previous video in queue.
    /// </summary>
    public void PlayPreviousVideo()
    {
        var service = MediaPlaylistService.Instance;
        if (service == null) return;

        string prevPath = service.GetPreviousVideo();
        if (!string.IsNullOrEmpty(prevPath))
        {
            PlayVideoSimple(prevPath);
        }
    }

    private void PlayVideoSimple(string path)
    {
        var video = new MediaVideoInfo
        {
            Path = path,
            Title = System.IO.Path.GetFileNameWithoutExtension(path)
        };
        PlayVideo(video);
    }

    /// <summary>
    /// Toggle room lights on/off.
    /// Only works in Flat/Mono mode.
    /// </summary>
    public void ToggleLights()
    {
        _environmentController?.ToggleLights();
    }

    /// <summary>
    /// Set room lights enabled state.
    /// Only works in Flat/Mono mode.
    /// </summary>
    public void SetLightsEnabled(bool enabled)
    {
        _environmentController?.SetLightsEnabled(enabled);
    }

    /// <summary>
    /// Get current lights state.
    /// </summary>
    public bool AreLightsEnabled()
    {
        return _environmentController?.LightsEnabled ?? true;
    }

    /// <summary>
    /// Seek to time in seconds.
    /// </summary>
    public void Seek(float timeSeconds)
    {
        _playbackEngine?.Seek((double)timeSeconds);
    }

    /// <summary>
    /// Seek by delta time.
    /// </summary>
    public void SeekRelative(float deltaSeconds)
    {
        if (_playbackEngine != null)
        {
            double newTime = System.Math.Max(0, System.Math.Min(_playbackEngine.CurrentTime + deltaSeconds, _playbackEngine.Duration));
            _playbackEngine.Seek(newTime);
        }
    }

    /// <summary>
    /// Set volume (0-1).
    /// </summary>
    public void SetVolume(float volume)
    {
        if (_playbackEngine != null)
            _playbackEngine.Volume = volume;
    }

    /// <summary>
    /// Set playback speed.
    /// </summary>
    public void SetPlaybackSpeed(float speed)
    {
        if (_playbackEngine != null)
            _playbackEngine.PlaybackSpeed = speed;
        _controlsPanel?.SetSpeed(speed);
    }

    /// <summary>
    /// Set screen distance (for flat projection).
    /// </summary>
    public void SetScreenDistance(float meters)
    {
        _displaySettings.Distance = Mathf.Clamp(meters, 0f, 5f);
        _projectionSystem?.UpdateDisplay(_displaySettings);
    }

    /// <summary>
    /// Set screen scale.
    /// </summary>
    public void SetScreenScale(float scale)
    {
        _displaySettings.Scale = Mathf.Clamp(scale, 0.5f, 3f);
        _projectionSystem?.UpdateDisplay(_displaySettings);
    }

    /// <summary>
    /// Set screen curvature (for flat projection).
    /// </summary>
    public void SetScreenCurvature(float curvature)
    {
        _displaySettings.Curvature = Mathf.Clamp01(curvature);
        _projectionSystem?.UpdateDisplay(_displaySettings);
    }

    /// <summary>
    /// Recenter the view.
    /// </summary>
    public void RecenterView()
    {
        _projectionSystem?.RecenterView();
    }

    /// <summary>
    /// Show player controls.
    /// </summary>
    public void ShowControls()
    {
        _controlsPanel?.Show();
    }

    /// <summary>
    /// Hide player controls.
    /// </summary>
    public void HideControls()
    {
        _controlsPanel?.Hide();
    }



    /// <summary>
    /// Show projection settings popup.
    /// </summary>
    public void ShowProjectionSettings()
    {
        if (_projectionPopup == null || _projectionSystem == null) return;

        _projectionPopup.SetState(_projectionSystem.CurrentProjection, ConvertToUIStereo(_projectionSystem.CurrentStereoMode));
        _projectionPopup.Show();
    }

    private void HandleProjectionSettingsChanged(VideoProjectionType projection, RTTMediaProjectionPopup.StereoMode uiStereo)
    {
        var stereo = ConvertFromUIStereo(uiStereo);
        Debug.Log($"[VRVideoPlayerController] Manual projection change: {projection}, {stereo}");
        ApplyProjectionSettings(projection, stereo);
    }

    private void HandleProjectionChanged(VideoProjectionType newProjection)
    {
        var stereoMode = ProjectionDetector.DetectStereoMode(newProjection, _currentVideo?.Path ?? "");
        ApplyProjectionSettings(newProjection, stereoMode);
    }

    /// <summary>
    /// Hide projection settings popup.
    /// </summary>
    public void HideProjectionPopup()
    {
        _projectionPopup?.Hide();
    }

    /// <summary>
    /// Show environment settings popup.
    /// </summary>
    public void ShowEnvironmentPopup()
    {
        if (_environmentPopup == null) return;

        // Update with current state from controller
        SyncEnvironmentStateFromController();
        _environmentPopup.SetEnvironmentState(_currentMonitor, _currentEnv);
        _environmentPopup.Show();
    }

    /// <summary>
    /// Hide environment settings popup.
    /// </summary>
    public void HideEnvironmentPopup()
    {
        _environmentPopup?.Hide();
    }


#endregion

#region Event Handlers
    // Monitory Type Logic
    private RTTMediaProjectionPopup.MonitorType _currentMonitor = RTTMediaProjectionPopup.MonitorType.Flat;
    private void HandleMonitorTypeChanged(RTTMediaProjectionPopup.MonitorType type)
    {
        _currentMonitor = type;
        float curvature = (type == RTTMediaProjectionPopup.MonitorType.Curved) ? 0.25f : 0f;
        SetScreenCurvature(curvature);
        Debug.Log($"[VRVideoPlayerController] Monitor type changed to {type}, curvature set to {curvature}");
    }

    // Environment Settings Logic
    private RTTMediaProjectionPopup.EnvironmentType _currentEnv = RTTMediaProjectionPopup.EnvironmentType.Room;
    private void HandleEnvironmentSettingsChanged(RTTMediaProjectionPopup.EnvironmentType type)
    {
        _currentEnv = type;
        switch (type)
        {
            case RTTMediaProjectionPopup.EnvironmentType.Room:
                _environmentController?.ShowEnvironment();
                _environmentController?.SetLightsEnabled(true);
                break;
            case RTTMediaProjectionPopup.EnvironmentType.Cinema:
                _environmentController?.ShowEnvironment();
                // Cinema mode: Lights OFF but don't hide environment (decoupled)
                _environmentController?.SetLightsEnabled(false, false);
                break;
            case RTTMediaProjectionPopup.EnvironmentType.LightOff:
                // Clear Manual blocker, and let SetLightsEnabled(false) handle immersion hiding via "Lights" blocker
                _environmentController?.ShowEnvironment();
                _environmentController?.SetLightsEnabled(false);
                break;
        }
        Debug.Log($"[VRVideoPlayerController] Environment changed to {type}");
    }

    private void SyncEnvironmentStateFromController()
    {
        if (_environmentController == null) return;

        bool lightsOn = _environmentController.LightsEnabled;
        bool manualBlocked = _environmentController.IsSourceBlockingVisibility("Manual");
        bool lightsBlocked = _environmentController.IsSourceBlockingVisibility("Lights");

        if (manualBlocked || lightsBlocked)
        {
            _currentEnv = RTTMediaProjectionPopup.EnvironmentType.LightOff;
        }
        else if (!lightsOn)
        {
            _currentEnv = RTTMediaProjectionPopup.EnvironmentType.Cinema;
        }
        else
        {
            _currentEnv = RTTMediaProjectionPopup.EnvironmentType.Room;
        }

        if (_environmentPopup != null && _environmentPopup.isActiveAndEnabled)
        {
            _environmentPopup.SetEnvironmentState(_currentMonitor, _currentEnv);
        }

        Debug.Log($"[VRVideoPlayerController] Synced UI state to {_currentEnv} (Lights: {lightsOn}, Blocked: {manualBlocked || lightsBlocked})");
    }
    private void HandleVideoPrepared()
    {
        Debug.Log("[VRVideoPlayerController] Video prepared");

        // Set video texture to projection
        if (_playbackEngine != null && _projectionSystem != null)
        {
            var outputTexture = _playbackEngine.OutputTexture;
            Debug.Log($"[VRVideoPlayerController] OutputTexture: {(outputTexture != null ? $"{outputTexture.width}x{outputTexture.height}" : "null")}");
            
            if (_playbackEngine.UseNV12Output)
            {
                Debug.Log("[VRVideoPlayerController] Using NV12 output");
                _projectionSystem.SetTextureNV12(
                    _playbackEngine.YPlaneTexture,
                    _playbackEngine.UVPlaneTexture
                );
            }
            else
            {
                Debug.Log("[VRVideoPlayerController] Using standard texture output");
                _projectionSystem.SetTexture(_playbackEngine.OutputTexture);
            }

            Debug.Log("[VRVideoPlayerController] Calling ProjectionSystem.Show()");
            _projectionSystem.Show();
            
            Debug.Log($"[VRVideoPlayerController] Projection visible: {_projectionSystem.IsVisible}, ActiveRenderer: {_projectionSystem.ActiveRenderer?.GetType().Name ?? "null"}");
        }
        else
        {
            Debug.LogError($"[VRVideoPlayerController] Cannot set texture - PlaybackEngine: {_playbackEngine != null}, ProjectionSystem: {_projectionSystem != null}");
        }

        // Update controls with duration and aspect ratio
        if (_controlsPanel != null && _playbackEngine != null)
        {
            _controlsPanel.SetDuration((float)_playbackEngine.Duration);
            
            // Set aspect ratio for preview frame
            float ratio = 16f / 9f; // Default
            if (_playbackEngine.UseNV12Output && _playbackEngine.YPlaneTexture != null)
            {
                ratio = (float)_playbackEngine.YPlaneTexture.width / _playbackEngine.YPlaneTexture.height;
            }
            else if (_playbackEngine.OutputTexture != null)
            {
                ratio = (float)_playbackEngine.OutputTexture.width / _playbackEngine.OutputTexture.height;
            }
            
            if (ratio > 0)
            {
                _controlsPanel.SetPreviewAspectRatio(ratio);
            }
            
            // Set the main video texture as the preview (mirrors playback)
            // Note: NV12 textures might need shader conversion for RawImage, 
            // but OutputTexture (RGB) usually works for preview if available.
            if (_playbackEngine.OutputTexture != null)
            {
                _controlsPanel.SetPreviewTexture(_playbackEngine.OutputTexture);
            }
        }

        // Auto-play
        Debug.Log("[VRVideoPlayerController] Starting auto-play");
        _playbackEngine?.Play();
    }

    private void HandlePlaybackEnded()
    {
        Debug.Log("[VRVideoPlayerController] Playback ended");

        if (_controlsPanel != null)
        {
            _controlsPanel.SetCurrentTime((float)(_playbackEngine?.Duration ?? 0));
            _controlsPanel.SetPlayState(false);
            _controlsPanel.Show();
        }

        if (_currentVideo.HasValue)
        {
            OnVideoEnded?.Invoke(_currentVideo.Value);
        }

        // Auto-advance to next video
        PlayNextVideo();
    }

    private void HandlePlaybackError(string error)
    {
        Debug.LogError($"[VRVideoPlayerController] Playback error: {error}");
        Stop();
        OnBackToLibrary?.Invoke();
    }

    private void HandleStateChanged(VideoPlaybackEngine.PlaybackState state)
    {
        bool isPlaying = state == VideoPlaybackEngine.PlaybackState.Playing;
        _controlsPanel?.SetPlayState(isPlaying);
    }

    private void HandleTimeUpdate(double currentTime)
    {
        _controlsPanel?.SetCurrentTime((float)currentTime);
    }

    private void HandleBackClicked()
    {
        Stop();
        OnBackToLibrary?.Invoke();
    }




    private void HandleVRModeClicked()
    {
        Debug.Log("[VRVideoPlayerController] VR Mode clicked - Showing Projection Popup");
        ShowProjectionSettings();
    }

    private void HandleHeadsetModeClicked()
    {
        Debug.Log("[VRVideoPlayerController] Headset Mode clicked (Not implemented)");
    }


    private void HandleRecenter()
    {
        Debug.Log("[VRVideoPlayerController] HandleRecenter called - starting VR recenter");
        StartCoroutine(RecenterRoutine());
    }

    private System.Collections.IEnumerator RecenterRoutine()
    {
        VRGazeReticle reticle = VRGazeReticle.Instance;
        if (reticle == null) reticle = FindObjectOfType<VRGazeReticle>();

        // Load recenter icon from Resources (icon files are directly in Resources folder)
        Sprite recenterIcon = Resources.Load<Sprite>("icon_recenter");
        Debug.Log($"[VRVideoPlayerController] RecenterRoutine - reticle: {reticle != null}, icon: {recenterIcon != null}");

        if (reticle != null && recenterIcon != null)
        {
            Debug.Log("[VRVideoPlayerController] Calling EnterRecenterMode");
            reticle.EnterRecenterMode(recenterIcon);
        }

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            if (reticle != null)
            {
                reticle.UpdateRecenterProgress(progress);
            }

            yield return null;
        }

        // Recenter virtual objects and projection
        Camera cam = Camera.main;
        if (cam != null)
        {
            RecenterAllVirtualObjects(cam);
        }

        // Also recenter video projection if it exists
        if (_projectionSystem != null)
        {
            _projectionSystem.RecenterView();
        }

        // Recenter VideoControlsContainer (detached from VirtualObjects to avoid Zoom)
        if (_controlsPanel != null)
        {
            // Traverse up to find VideoControlsContainer
            // Hierarchy: VideoControlsContainer -> VideoControlsFrame -> ContentContainer -> ControlsPanel
            Transform controlsContainer = _controlsPanel.transform.root; 
            // Better to find by name or known structure to be safe, or just use the root of the panel prefab if it's the container
            // In VRMediaAppController: _controlsContainer -> VideoControlsFrame -> Content -> ControlsPanel
            
            // Try to find the container via parent traversal
            Transform current = _controlsPanel.transform;
            while (current.parent != null && current.name != "VideoControlsContainer")
            {
                current = current.parent;
            }

            if (current.name == "VideoControlsContainer")
            {
                RecenterObject(current, cam);
            }
        }

        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }

        Debug.Log("[VRVideoPlayerController] VR recenter complete.");
    }

    private void RecenterAllVirtualObjects(Camera cam)
    {
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
        if (virtualObjectsParent == null)
        {
            Debug.LogWarning("[VRVideoPlayerController] VirtualObjects parent not found");
            return;
        }

        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null)
        {
            Debug.LogWarning("[VRVideoPlayerController] No primary RTTMenuFrame found");
            return;
        }

        Vector3 pivotPos = primary.transform.position;
        Quaternion pivotRot = primary.transform.rotation;

        List<Transform> children = new List<Transform>();
        List<Vector3> relativePositions = new List<Vector3>();
        List<Quaternion> relativeRotations = new List<Quaternion>();

        foreach (Transform child in virtualObjectsParent.transform)
        {
            children.Add(child);
            Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
            relativePositions.Add(relPos);
            Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
            relativeRotations.Add(relRot);
        }

        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(
            new Vector2(pivotPos.x, pivotPos.z),
            new Vector2(camPos.x, camPos.z)
        );

        Vector3 newPivotPos = camPos + camForward * hDist;
        newPivotPos.y = pivotPos.y;
        Quaternion newPivotRot = Quaternion.LookRotation(camForward);

        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            child.position = newPivotPos + newPivotRot * relativePositions[i];
            child.rotation = newPivotRot * relativeRotations[i];
        }
    }
    #endregion

    #region Unity Lifecycle
    private void Update()
    {
        // Update video texture to projection (for frame updates)
        if (_playbackEngine != null && _playbackEngine.IsPlaying && _projectionSystem != null)
        {
            if (_playbackEngine.UseNV12Output)
            {
                _projectionSystem.SetTextureNV12(
                    _playbackEngine.YPlaneTexture,
                    _playbackEngine.UVPlaneTexture
                );
            }
            else
            {
                _projectionSystem.SetTexture(_playbackEngine.OutputTexture);
            }
        }
    }

    private void OnDestroy()
    {
        // Unwire events
        if (_playbackEngine != null)
        {
            _playbackEngine.OnPrepareCompleted -= HandleVideoPrepared;
            _playbackEngine.OnPlaybackEnded -= HandlePlaybackEnded;
            _playbackEngine.OnError -= HandlePlaybackError;
            _playbackEngine.OnStateChanged -= HandleStateChanged;
            _playbackEngine.OnTimeUpdate -= HandleTimeUpdate;
        }

        if (_controlsPanel != null)
        {
            _controlsPanel.OnPlayPause -= TogglePlayPause;
            _controlsPanel.OnSeek -= Seek;
            _controlsPanel.OnVolumeChanged -= SetVolume;
            _controlsPanel.OnSpeedChanged -= SetPlaybackSpeed;
            _controlsPanel.OnBackClicked -= HandleBackClicked;
            _controlsPanel.OnPrevious -= PlayPreviousVideo;
            _controlsPanel.OnNext -= PlayNextVideo;
            _controlsPanel.OnVRModeClicked -= HandleVRModeClicked;
            _controlsPanel.OnHeadsetModeClicked -= HandleHeadsetModeClicked;
            _controlsPanel.OnRecenterClicked -= HandleRecenter;
        }

        UnwireProjectionEvents();
        UnwireEnvironmentEvents();
    }
    #endregion

    private void RecenterObject(Transform objective, Camera cam)
    {
        if (objective == null || cam == null) return;

        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        
        // Calculate new position based on current distance
        // Maintain height (y) and distance from camera
        Vector3 currentPos = objective.position;
        float dist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));
        
        Vector3 newPos = camPos + camForward * dist;
        newPos.y = currentPos.y; // Keep height

        objective.position = newPos;

    // Custom logic for VideoControlsContainer
    if (objective.name == "VideoControlsContainer")
    {
        // Rotate container to face the camera so children keep their relative positions
        objective.rotation = Quaternion.LookRotation(camForward);

        // Reset child frames to local identity
        Transform frame = objective.Find("VideoControlsFrame");
        if (frame != null)
        {
            frame.localRotation = Quaternion.identity;
        }

        Transform overlay = objective.Find("DismissOverlayFrame");
        if (overlay != null)
        {
            overlay.localRotation = Quaternion.identity;
        }
    }
    else
    {
        // Standard behavior for other objects
        objective.rotation = Quaternion.LookRotation(camForward);
    }
}
}
