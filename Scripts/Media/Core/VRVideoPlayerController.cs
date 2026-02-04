using UnityEngine;
using System;

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
    private RTTMediaSettingsPopup _settingsPopup;
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
            _controlsPanel.OnSettingsClicked += ShowSettings;
        }
    }

    /// <summary>
    /// Set the settings popup reference.
    /// </summary>
    public void SetSettingsPopup(RTTMediaSettingsPopup popup)
    {
        if (_settingsPopup != null)
        {
            UnwireSettingsEvents();
        }

        _settingsPopup = popup;

        if (_settingsPopup != null)
        {
            WireSettingsEvents();
        }
    }

    private void WireSettingsEvents()
    {
        if (_settingsPopup == null) return;

        _settingsPopup.OnProjectionChanged += HandleProjectionChanged;
        _settingsPopup.OnScreenDistanceChanged += SetScreenDistance;
        _settingsPopup.OnScreenScaleChanged += SetScreenScale;
        _settingsPopup.OnScreenCurvatureChanged += SetScreenCurvature;
        _settingsPopup.OnLightsToggled += SetLightsEnabled;
        _settingsPopup.OnCloseRequested += HideSettings;
    }

    private void UnwireSettingsEvents()
    {
        if (_settingsPopup == null) return;

        _settingsPopup.OnProjectionChanged -= HandleProjectionChanged;
        _settingsPopup.OnScreenDistanceChanged -= SetScreenDistance;
        _settingsPopup.OnScreenScaleChanged -= SetScreenScale;
        _settingsPopup.OnScreenCurvatureChanged -= SetScreenCurvature;
        _settingsPopup.OnLightsToggled -= SetLightsEnabled;
        _settingsPopup.OnCloseRequested -= HideSettings;
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

        // Detect projection and stereo mode
        var projectionType = video.Projection;
        var stereoMode = ProjectionDetector.DetectStereoMode(projectionType, video.Path);

        Debug.Log($"[VRVideoPlayerController] Playing: {video.Title}, Projection: {projectionType}, Stereo: {stereoMode}");

        // Configure projection system
        if (_projectionSystem != null)
        {
            // Choose appropriate display settings
            _displaySettings = ProjectionDetector.SupportsScreenSettings(projectionType)
                ? DisplaySettings.Default
                : DisplaySettings.Immersive;

            _projectionSystem.SetProjection(projectionType, stereoMode);
            _projectionSystem.UpdateDisplay(_displaySettings);
            _projectionSystem.RecenterView(); // Ensure screen is in front of user
            
            Debug.Log($"[VRVideoPlayerController] Projection configured: distance={_displaySettings.Distance}, scale={_displaySettings.Scale}");
        }
        else
        {
            Debug.LogError("[VRVideoPlayerController] ProjectionSystem is null!");
        }

        // Update environment based on projection type
        if (_environmentController != null)
        {
            _environmentController.SetProjectionType(projectionType);
        }

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
            _controlsPanel.Show();
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

        // Reset environment to default state
        _environmentController?.Reset();
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
        _displaySettings.Distance = Mathf.Clamp(meters, 1f, 5f);
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
    /// Show settings popup.
    /// </summary>
    public void ShowSettings()
    {
        if (_settingsPopup == null) return;

        // Update settings popup with current state
        if (_currentVideo.HasValue)
        {
            _settingsPopup.SetProjection(_currentVideo.Value.Projection);
        }
        _settingsPopup.SetScreenSettings(_displaySettings.Distance, _displaySettings.Scale, _displaySettings.Curvature);
        _settingsPopup.SetLightsState(AreLightsEnabled());

        _settingsPopup.Show();
    }

    /// <summary>
    /// Hide settings popup.
    /// </summary>
    public void HideSettings()
    {
        _settingsPopup?.Hide();
    }

    /// <summary>
    /// Notify user interaction for auto-hide reset.
    /// </summary>
    public void OnUserInteraction()
    {
        _controlsPanel?.OnUserInteraction();
    }
    #endregion

    #region Event Handlers
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

        // Update controls with duration
        if (_controlsPanel != null && _playbackEngine != null)
        {
            _controlsPanel.SetDuration((float)_playbackEngine.Duration);
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
            _controlsPanel.SetPlayState(false);
            _controlsPanel.Show();
        }

        if (_currentVideo.HasValue)
        {
            OnVideoEnded?.Invoke(_currentVideo.Value);
        }
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

    private void HandleProjectionChanged(VideoProjectionType newProjection)
    {
        if (!_currentVideo.HasValue) return;

        var video = _currentVideo.Value;
        video.Projection = newProjection;
        _currentVideo = video;

        var stereoMode = ProjectionDetector.DetectStereoMode(newProjection, video.Path);

        // Update projection system
        if (_projectionSystem != null)
        {
            _displaySettings = ProjectionDetector.SupportsScreenSettings(newProjection)
                ? DisplaySettings.Default
                : DisplaySettings.Immersive;

            _projectionSystem.SetProjection(newProjection, stereoMode);
            _projectionSystem.UpdateDisplay(_displaySettings);
        }

        // Update environment
        _environmentController?.SetProjectionType(newProjection);

        Debug.Log($"[VRVideoPlayerController] Projection changed to {newProjection}");
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
            _controlsPanel.OnSettingsClicked -= ShowSettings;
        }

        UnwireSettingsEvents();
    }
    #endregion
}
