using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Controller for VR video playback.
/// Connects VideoPlaybackEngine, VRVideoProjectionSystem, and RTTMediaControlsPanel.
/// </summary>
public class VRVideoPlayerController : MonoBehaviour
{
    #region Events
    public event Action OnBackToLibrary;
    public event Action<MediaVideoInfo> OnVideoEnded;
    /// <summary>
    /// Fired when playback fails with error details.
    /// Parameters: (errorMessage, isCodecError, codecName, containerFormat)
    /// containerFormat is non-null when the file is NOT a standard MP4.
    /// </summary>
    public event Action<string, bool, string, string> OnPlaybackFailed;
    /// <summary>
    /// Fired when user manually changes projection/stereo settings via the popup.
    /// Params: (projectionType, stereoMode)
    /// </summary>
    public event Action<VideoProjectionType, StereoMode> OnProjectionSettingsUpdated;
    /// <summary>
    /// Fired when the current video changes (play, next, previous, queue jump).
    /// Parameter: video file path.
    /// </summary>
    public event Action<string> OnVideoChanged;
    #endregion

    #region Properties
    public bool IsPlaying => _playbackEngine?.IsPlaying ?? false;
    public double CurrentTime => _playbackEngine?.CurrentTime ?? 0;
    public double Duration => _playbackEngine?.Duration ?? 0;
    public MediaVideoInfo? CurrentVideo { get; private set; }
    public VideoPlaybackEngine PlaybackEngine => _playbackEngine;
    public VRVideoProjectionSystem ProjectionSystem => _projectionSystem;
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

    // Original DisplayQuad scales for controls/overlay frames (for immersive scaling)
    private Dictionary<Transform, Vector3> _controlsQuadOriginalScales = new Dictionary<Transform, Vector3>();

    // Stereo UI shader for controls panel (supports per-eye offset, used even with offset=0)
    private const string STEREO_UI_SHADER = "VRWorkspace/UI/StereoUIPanel";

    // Per-video settings cache: auto-save every 10s, restore on re-open
    private Coroutine _autoSaveCoroutine;
    private VideoSettingsEntry _cachedEntry;  // pending restore (consumed after HandleVideoPrepared)
    private double _resumePosition = 0;
    private bool _isStopped = true; // Guard against double-Stop() corrupting saved settings

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
            _playbackEngine.OnSeekCompleted += HandleSeekCompleted;
            _playbackEngine.OnBufferingCompleted += HandleBufferingCompleted;
        }

        // Controls panel events
        if (_controlsPanel != null)
        {
            _controlsPanel.OnPlayPause += TogglePlayPause;
            _controlsPanel.OnSeek += Seek;
            _controlsPanel.OnVolumeChanged += SetVolume;
            _controlsPanel.OnSpeedChanged += SetPlaybackSpeed;
            _controlsPanel.OnBackClicked += HandleBackClicked;
            // Forward/Backward buttons now seek ±10s directly via OnSeek
            _controlsPanel.OnVRModeClicked += HandleVRModeClicked;
            _controlsPanel.OnHeadsetModeClicked += HandleHeadsetModeClicked;
            _controlsPanel.OnRecenterClicked += HandleRecenter;
            _controlsPanel.OnEnvironmentClicked += ShowEnvironmentPopup;
            _controlsPanel.OnVisibilityChanged += HandleControlsVisibilityChanged;

            // Close popups when interacting with main controls
            _controlsPanel.OnPlayPause += HideAllPopups;
            _controlsPanel.OnSeek += (_) => HideAllPopups();
            _controlsPanel.OnVolumeChanged += (_) => HideAllPopups();
            _controlsPanel.OnSettingsClicked += HideAllPopups;
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

    /// <summary>
    /// Hide all transient popups (Projection, Environment).
    /// </summary>
    public void HideAllPopups()
    {
        HideProjectionPopup();
        HideEnvironmentPopup();
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

        // Save current video settings before switching
        SaveCurrentVideoSettings();
        StopAutoSaveTimer();

        _currentVideo = video;
        CurrentVideo = video;
        _isStopped = false; // Mark as active (allows Stop() to save settings once)
        OnVideoChanged?.Invoke(video.Path);

        // Hide projection during loading (will Show after all settings applied)
        _projectionSystem?.Hide();

        VideoProjectionType projectionType;
        StereoMode stereoMode;

        // Priority 1: Per-video cache (user's previous settings for this video)
        var cached = VideoSettingsCache.Get(video.Path);
        if (cached != null)
        {
            projectionType = (VideoProjectionType)cached.Projection;
            stereoMode = (StereoMode)cached.Stereo;
            _currentMonitor = (RTTMediaProjectionPopup.MonitorType)cached.Monitor;
            _currentEnv = (RTTMediaProjectionPopup.EnvironmentType)cached.Environment;
            _resumePosition = cached.PlaybackPosition;
            _cachedEntry = cached; // will restore picture/immersive settings after prepare
            Debug.Log($"[VRVideoPlayerController] Playing: {video.Title}, Cache → {projectionType}, {stereoMode}, Monitor={_currentMonitor}, Resume={_resumePosition:F1}s");
        }
        else
        {
            // Priority 2: Try metadata detection (fast, synchronous)
            var metadata = VideoSphericalMetadataReader.ReadMetadata(video.Path);
            if (metadata.HasMetadata)
            {
                projectionType = ProjectionDetector.InterpretProjection(metadata, video.Width, video.Height);
                stereoMode = ProjectionDetector.InterpretStereo(metadata);
                Debug.Log($"[VRVideoPlayerController] Playing: {video.Title}, Metadata → {projectionType}, {stereoMode}");
            }
            else
            {
                // Priority 3: Default Flat + Mono
                projectionType = VideoProjectionType.Flat;
                stereoMode = StereoMode.Mono;
                Debug.Log($"[VRVideoPlayerController] Playing: {video.Title}, No metadata → Flat/Mono");
            }
            _resumePosition = 0;
            _cachedEntry = null;

            // Reset per-video settings to defaults (prevent leaking from previous video)
            _currentMonitor = RTTMediaProjectionPopup.MonitorType.Flat;
            _currentEnv = RTTMediaProjectionPopup.EnvironmentType.Room;
        }

        ApplyProjectionSettings(projectionType, stereoMode);
        bool isImmersive = !ProjectionDetector.SupportsScreenSettings(projectionType);
        RepositionControlsForProjection(isImmersive, forceReposition: true);
        OnProjectionSettingsUpdated?.Invoke(projectionType, stereoMode);

        // Load video
        if (_playbackEngine != null)
        {
            _playbackEngine.PrepareLocal(video.Path);
        }

        StartAutoSaveTimer();

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

        bool isImmersive = !ProjectionDetector.SupportsScreenSettings(projectionType);

        // Capture previous state BEFORE changing projection
        bool wasImmersive = _projectionSystem.IsImmersiveProjection();

        // Capture picture settings from the CURRENT renderer before switching.
        // The new renderer may be reused from a previous video with stale shader values.
        float prevBrightness = GetCurrentShaderFloat("_Brightness", 1f);
        float prevContrast = GetCurrentShaderFloat("_Contrast", 1f);
        float prevSaturation = GetCurrentShaderFloat("_Saturation", 1f);
        float prevTint = GetCurrentShaderFloat("_Tint", 0f);
        float prevTemperature = GetCurrentShaderFloat("_Temperature", 0f);
        float prevSharpness = GetCurrentShaderFloat("_Sharpness", 0.5f);

        // Choose appropriate display settings
        _displaySettings = isImmersive ? DisplaySettings.Immersive : DisplaySettings.Default;

        // Flat projection: Distance=0 so screen sits at _projectionRoot position
        if (!isImmersive)
        {
            _displaySettings.Distance = 0f;

            // Preserve current monitor type curvature (user may have selected Curved)
            if (_currentMonitor == RTTMediaProjectionPopup.MonitorType.Curved)
                _displaySettings.Curvature = 0.25f;
        }

        _projectionSystem.SetProjection(projectionType, stereoMode);
        _projectionSystem.UpdateDisplay(_displaySettings);
        // NOTE: No RecenterView() here — sphere center syncs to flat screen direction
        // automatically in SetProjection(). Explicit recenter only via RecenterRoutine().

        // Transfer picture settings to the NEW renderer.
        // This prevents stale values from a previous video bleeding through.
        ApplyShaderFloat("_Brightness", prevBrightness);
        ApplyShaderFloat("_Contrast", prevContrast);
        ApplyShaderFloat("_Saturation", prevSaturation);
        ApplyShaderFloat("_Tint", prevTint);
        ApplyShaderFloat("_Temperature", prevTemperature);
        ApplyShaderFloat("_Sharpness", prevSharpness);

        // Reset immersive-specific adjustments to defaults when switching TO immersive.
        // These are per-video settings that should not carry over from a previous immersive video.
        if (isImmersive && _projectionSystem.ActiveRenderer is ImmersiveSphereRenderer imm)
        {
            imm.SetShaderFloat("_Tilt", 0f);
            imm.SetShaderFloat("_VerticalShift", 0f);
            imm.SetShaderFloat("_HorizontalShift", 0f);
            imm.SetShaderFloat("_LRInverse", 0f);
            imm.ResetFOVZoom();
        }

        // Reset flat renderer board alpha when switching TO flat.
        // During SwitchToPlayer, SetAllPlayerFramesAlpha(0f) may have set boardAlpha=0
        // on the flat renderer before the system switched to immersive. The fade-in
        // animation only restores brightness on the immersive renderer (the active one),
        // leaving flat's boardAlpha stuck at 0. This makes the flat screen invisible
        // when the user later switches back to Flat via the projection popup.
        if (!isImmersive && _projectionSystem.ActiveRenderer is FlatProjectionRenderer flat)
        {
            flat.SetBoardAlpha(1f);
        }

        // Update environment based on projection type
        if (_environmentController != null)
        {
            _environmentController.SetProjectionType(projectionType);
        }

        // Update popups if they are active
        _projectionPopup?.SetState(projectionType, ConvertToUIStereo(stereoMode));

        // NOTE: Controls are NOT repositioned here — position stays stable during mode switches.
        // Repositioning only happens in PlayVideo() (initial) and HandleRecenter() (explicit).

        // Stereo depth offset: always 0 (no per-eye shift on controls).
        // Vergence conflict is handled by force-mono on the video instead.
        SetControlsStereoDepthOffset(0f);

        // Stereo strength sync: if controls are visible during projection change,
        // set stereo strength to 0 (mono) on the new renderer to prevent vergence conflict.
        bool isStereo = stereoMode != StereoMode.Mono;
        bool controlsVisible = _controlsPanel != null && _controlsPanel.IsVisible;
        _projectionSystem.ActiveRenderer?.SetStereoStrength(isStereo && controlsVisible ? 0f : 1f);

        // Setup/teardown immersive zoom override
        SetupZoomOverride(isImmersive);

        // Re-apply video texture to the new active renderer.
        // SetProjection() switches to a renderer with no texture assigned.
        // Without this, paused videos show black since Update() only pushes
        // texture when IsPlaying is true.
        if (_playbackEngine != null && _playbackEngine.IsPrepared && _projectionSystem != null)
        {
            if (_playbackEngine.UseNV12Output)
            {
                _projectionSystem.SetTextureNV12(
                    _playbackEngine.YPlaneTexture,
                    _playbackEngine.UVPlaneTexture);
            }
            else
            {
                _projectionSystem.SetTexture(_playbackEngine.OutputTexture);
            }
        }
    }

    /// <summary>
    /// Reposition the controls container to be centered in front of the video.
    /// For immersive (180/360): centers in front of camera at 2m distance.
    /// For flat: centers below the flat screen position.
    /// </summary>
    private void RepositionControlsForProjection(bool isImmersive, bool forceReposition = true)
    {
        Camera cam = Camera.main;
        if (cam == null || _controlsPanel == null) return;

        // Find VideoControlsContainer by traversing up from controls panel
        Transform container = FindControlsContainer();
        if (container == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 newPos = container.position;
        Vector3 facingDir = camForward;

        bool keptPosition = false;

        if (isImmersive)
        {
            if (!forceReposition)
            {
                // Flat→immersive transition: keep current world position (no visible jump)
                Vector3 currentPos = container.position;
                Vector3 toControls = currentPos - camPos;
                toControls.y = 0;
                float dist = toControls.magnitude;

                if (dist > 0.5f)
                {
                    newPos = currentPos;
                    facingDir = toControls.normalized;
                    keptPosition = true;
                }
            }

            if (!keptPosition)
            {
                // Force reposition or no valid current position:
                // use saved flat position direction so controls align with video content center
                Vector3 contentDir = camForward;
                if (_projectionSystem != null && _projectionSystem.HasSavedFlatTransform)
                {
                    Vector3 toContent = _projectionSystem.SavedFlatPosition - camPos;
                    toContent.y = 0;
                    if (toContent.sqrMagnitude > 0.001f)
                        contentDir = toContent.normalized;
                }

                // Distance 2.0m — same as flat mode; 3D effect is disabled when controls are visible
                newPos = camPos + contentDir * 2.0f;
                newPos.y = camPos.y - 0.625f;
                facingDir = contentDir;
            }
        }
        else
        {
            // Flat: position controls below the screen, facing same direction as screen
            Vector3 screenPos = _projectionSystem != null
                ? _projectionSystem.ProjectionPosition
                : container.position;

            newPos = screenPos;
            newPos.y = camPos.y - 0.625f;

            // Face direction = from camera toward screen (horizontal)
            Vector3 toScreen = screenPos - camPos;
            toScreen.y = 0;
            if (toScreen.sqrMagnitude < 0.001f) toScreen = camForward;
            toScreen.Normalize();
            facingDir = toScreen;
        }

        container.position = newPos;
        container.rotation = Quaternion.LookRotation(facingDir);

        // Scale: always 1.0 — immersive now at same 2.0m distance as flat
        float scaleFactor = 1.0f;

        Transform frame = container.Find("VideoControlsFrame");
        if (frame != null)
        {
            frame.localRotation = Quaternion.identity;
            ScaleFrameQuad(frame, scaleFactor);
        }

        Transform overlay = container.Find("DismissOverlayFrame");
        if (overlay != null)
        {
            overlay.localRotation = Quaternion.identity;
            ScaleFrameQuad(overlay, scaleFactor);
        }

        Transform sideFrame = container.Find("SideControlsFrame");
        if (sideFrame != null)
        {
            ScaleFrameQuad(sideFrame, scaleFactor);
        }

        Debug.Log($"[VRVideoPlayerController] Controls repositioned (immersive={isImmersive}, force={forceReposition}, scale={scaleFactor:F2}) at {newPos}");
    }

    /// <summary>
    /// Find the VideoControlsContainer transform by traversing up from controls panel.
    /// </summary>
    private Transform FindControlsContainer()
    {
        if (_controlsPanel == null) return null;

        Transform current = _controlsPanel.transform;
        while (current.parent != null && current.name != "VideoControlsContainer")
        {
            current = current.parent;
        }
        return current.name == "VideoControlsContainer" ? current : null;
    }

    /// <summary>
    /// Scale an RTTMenuFrame's DisplayQuad to maintain angular size at different distances.
    /// scaleFactor=1.0 for standard 2m distance, 1.25 for 2.5m immersive distance.
    /// </summary>
    private void ScaleFrameQuad(Transform frameTransform, float scaleFactor)
    {
        var menuFrame = frameTransform.GetComponent<RTTMenuFrame>();
        if (menuFrame == null) return;

        var quad = menuFrame.GetDisplayQuad();
        if (quad == null) return;

        // Store original scale on first access
        if (!_controlsQuadOriginalScales.ContainsKey(frameTransform))
        {
            _controlsQuadOriginalScales[frameTransform] = quad.transform.localScale;
        }

        Vector3 orig = _controlsQuadOriginalScales[frameTransform];
        quad.transform.localScale = new Vector3(
            orig.x * scaleFactor,
            orig.y * scaleFactor,
            orig.z
        );
    }

    /// <summary>
    /// Handle controls panel visibility changes.
    /// When controls are visible in stereo mode: instantly set mono rendering
    /// to eliminate vergence-accommodation conflict. Works for both immersive and flat modes.
    /// Note: Animation was tested but causes worse dizziness (sustained rotation from UV shift).
    /// Instant switch produces only a brief "pop" which the brain dismisses easily.
    /// </summary>
    private void HandleControlsVisibilityChanged(bool visible)
    {
        if (_projectionSystem?.ActiveRenderer == null) return;

        bool isStereo = _projectionSystem.CurrentStereoMode != StereoMode.Mono;
        if (!isStereo) return;

        _projectionSystem.ActiveRenderer.SetStereoStrength(visible ? 0f : 1f);
    }

    /// <summary>
    /// Ensure controls panel uses StereoUIPanel shader with given stereo offset.
    /// Currently always called with offset=0 (vergence handled by force-mono on video).
    /// </summary>
    private void SetControlsStereoDepthOffset(float offset)
    {
        Transform container = FindControlsContainer();
        if (container == null) return;

        // Always use StereoUIPanel shader — avoid shader swapping artifacts.
        // With offset=0, StereoUIPanel renders identically to Sprites/Default.
        Shader stereoShader = Shader.Find(STEREO_UI_SHADER);

        Transform frame = container.Find("VideoControlsFrame");
        if (frame != null) ApplyStereoShader(frame, stereoShader, offset);

        Transform overlay = container.Find("DismissOverlayFrame");
        if (overlay != null) ApplyStereoShader(overlay, stereoShader, offset);

        Transform sideFrame = container.Find("SideControlsFrame");
        if (sideFrame != null) ApplyStereoShader(sideFrame, stereoShader, offset);
    }

    private void ApplyStereoShader(Transform frameTransform, Shader stereoShader, float offset)
    {
        var menuFrame = frameTransform.GetComponent<RTTMenuFrame>();
        if (menuFrame == null) return;
        var quad = menuFrame.GetDisplayQuad();
        if (quad?.material == null) return;

        // Swap to StereoUIPanel shader once (subsequent calls skip if already applied)
        if (stereoShader != null && quad.material.shader != stereoShader)
        {
            Texture tex = quad.material.mainTexture;
            Color color = quad.material.color;
            int queue = quad.material.renderQueue;
            quad.material.shader = stereoShader;
            quad.material.mainTexture = tex;
            quad.material.color = color;
            quad.material.renderQueue = queue;
        }

        if (quad.material.HasProperty("_StereoOffset"))
            quad.material.SetFloat("_StereoOffset", offset);
    }

    /// <summary>
    /// Setup zoom override so zoom inside video player never affects VirtualObjects.
    /// Flat: zoom adjusts screen scale. Immersive: zoom adjusts FOV.
    /// </summary>
    private void SetupZoomOverride(bool isImmersive)
    {
        var zoomController = VirtualObjectsZoomController.Instance;
        if (zoomController == null) return;

        if (isImmersive && _projectionSystem != null)
        {
            zoomController.SetZoomOverride(
                () => _projectionSystem.ZoomImmersive(-10f),  // zoom in = decrease FOV
                () => _projectionSystem.ZoomImmersive(10f)    // zoom out = increase FOV
            );
        }
        else
        {
            // Flat mode: zoom adjusts screen scale (isolated from VirtualObjects)
            zoomController.SetZoomOverride(
                () => SetScreenScale(_displaySettings.Scale + 0.1f),  // zoom in = bigger
                () => SetScreenScale(_displaySettings.Scale - 0.1f)   // zoom out = smaller
            );
        }
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
        // Only save settings once (prevents second Stop() from overwriting good data with stale engine state)
        if (!_isStopped)
        {
            SaveCurrentVideoSettings();
            _isStopped = true;
        }
        StopAutoSaveTimer();

        _playbackEngine?.Stop();
        _projectionSystem?.Hide();

        // Clear immersive zoom override
        VirtualObjectsZoomController.Instance?.ClearZoomOverride();

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

    public void PlayVideoSimple(string path)
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
    /// Set a picture adjustment shader parameter on the active renderer.
    /// flatParam = shader param name for WorldPanelBoard (flat), immParam = for VideoImmersive (immersive).
    /// </summary>
    public void SetPictureAdjustment(string flatParam, string immParam, float value)
    {
        if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer immersive)
        {
            immersive.SetShaderFloat(immParam, value);
        }
        else if (_projectionSystem?.ActiveRenderer is FlatProjectionRenderer flat)
        {
            flat.SetBoardShaderFloat(flatParam, value);
        }
    }

    /// <summary>
    /// Enable/disable stereo 3D mode.
    /// </summary>
    public void SetStereoEnabled(bool enabled)
    {
        if (_projectionPopup != null)
        {
            // Update via projection popup which handles the full projection change
            var currentStereo = enabled ? StereoMode.SideBySide : StereoMode.Mono;
            // Directly set on renderer for immediate feedback
            _projectionSystem?.ActiveRenderer?.SetStereoMode(currentStereo);
        }
    }

    /// <summary>
    /// Set LR Inverse (swap left/right eye for SBS stereo).
    /// </summary>
    public void SetLRInverse(bool inverse)
    {
        if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer immersive)
        {
            immersive.SetShaderFloat("_LRInverse", inverse ? 1f : 0f);
        }
        else if (_projectionSystem?.ActiveRenderer is FlatProjectionRenderer flat)
        {
            flat.SetBoardShaderFloat("_LRInverse", inverse ? 1f : 0f);
        }
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

        if (_projectionPopup.IsVisible)
        {
            _projectionPopup.Hide();
            return;
        }

        HideEnvironmentPopup();
        _projectionPopup.SetState(_projectionSystem.CurrentProjection, ConvertToUIStereo(_projectionSystem.CurrentStereoMode));
        _projectionPopup.Show();
    }

    private void HandleProjectionSettingsChanged(VideoProjectionType projection, RTTMediaProjectionPopup.StereoMode uiStereo)
    {
        var stereo = ConvertFromUIStereo(uiStereo);
        Debug.Log($"[VRVideoPlayerController] Manual projection change: {projection}, {stereo}");

        // Detect stereo-only changes (same projection type, different stereo mode)
        bool isStereoOnlyChange = _projectionSystem != null
            && projection == _projectionSystem.CurrentProjection
            && stereo != _projectionSystem.CurrentStereoMode;
        bool isCurrentlyFlat = _projectionSystem != null && !_projectionSystem.IsImmersiveProjection();

        if (isStereoOnlyChange && isCurrentlyFlat && _projectionSystem.ActiveRenderer is FlatProjectionRenderer flatRenderer)
        {
            // Flat stereo-only: animated scale transition, no repositioning
            flatRenderer.SetStereoModeAnimated(stereo);
            _projectionSystem.UpdateStereoModeOnly(stereo);
            _projectionPopup?.SetState(projection, ConvertToUIStereo(stereo));
            SetupZoomOverride(false);
            // NOTE: No stereo offset or force-mono changes in flat mode — no vergence issue.
        }
        else if (isStereoOnlyChange && !isCurrentlyFlat)
        {
            // Immersive stereo-only: lightweight update without full SetProjection()
            // Avoids re-running sphere alignment which can cause subtle position shifts.
            _projectionSystem.ActiveRenderer?.SetStereoMode(stereo);
            _projectionSystem.UpdateStereoModeOnly(stereo);
            _projectionPopup?.SetState(projection, ConvertToUIStereo(stereo));
            // NOTE: No SetControlsStereoDepthOffset — offset stays 0 to keep controls stable.
            // Sync stereo strength with controls visibility for new stereo mode
            bool controlsVisible = _controlsPanel != null && _controlsPanel.IsVisible;
            bool isStereoMode = stereo != StereoMode.Mono;
            _projectionSystem.ActiveRenderer?.SetStereoStrength(isStereoMode && controlsVisible ? 0f : 1f);
        }
        else
        {
            // Full projection type change
            ApplyProjectionSettings(projection, stereo);
        }

        // Notify VRMediaAppController to reposition menu button for new projection/stereo mode
        OnProjectionSettingsUpdated?.Invoke(projection, stereo);
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

        if (_environmentPopup.IsVisible)
        {
            _environmentPopup.Hide();
            return;
        }

        HideProjectionPopup();
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

        // Reset renderer aspect ratio before setting new texture (renderer is reused, not recreated)
        _projectionSystem?.SetAspectRatioOverride("default");

        // Step 1: Set video texture (renderer is hidden via Hide() in PlayVideo)
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
        }
        else
        {
            Debug.LogError($"[VRVideoPlayerController] Cannot set texture - PlaybackEngine: {_playbackEngine != null}, ProjectionSystem: {_projectionSystem != null}");
        }

        // Step 2: Apply ALL settings BEFORE showing projection
        // Reset environment for videos without cache
        if (_cachedEntry == null && _environmentController != null)
        {
            _environmentController.ShowEnvironment();
            _environmentController.SetLightsEnabled(true);
        }

        // Restore cached picture/immersive/display/aspect settings
        RestoreCachedSettings();

        // Step 3: Update controls
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
            if (_playbackEngine.OutputTexture != null)
            {
                _controlsPanel.SetPreviewTexture(_playbackEngine.OutputTexture);
            }
        }

        // Step 4: Show projection AFTER all settings applied
        if (_projectionSystem != null)
        {
            _projectionSystem.Show();
            Debug.Log($"[VRVideoPlayerController] Projection visible: {_projectionSystem.IsVisible}, ActiveRenderer: {_projectionSystem.ActiveRenderer?.GetType().Name ?? "null"}");
        }

        Debug.Log("[VRVideoPlayerController] Buffering first frame...");
    }

    private void HandleBufferingCompleted()
    {
        // Resume from cached position if available
        if (_resumePosition > 1.0 && _playbackEngine != null)
        {
            // Don't resume if position is near or past the end
            double duration = _playbackEngine.Duration;
            if (_resumePosition < duration - 2.0)
            {
                Debug.Log($"[VRVideoPlayerController] Resuming from cached position: {_resumePosition:F1}s");
                _playbackEngine.Seek(_resumePosition);
            }
            _resumePosition = 0;
        }

        Debug.Log("[VRVideoPlayerController] Buffering complete - starting playback");
        _playbackEngine?.Play();
    }

    private void HandlePlaybackEnded()
    {
        // Save settings first, then reset position to 0 (video completed, don't resume at end)
        SaveCurrentVideoSettings();
        if (_currentVideo.HasValue)
        {
            var entry = VideoSettingsCache.Get(_currentVideo.Value.Path);
            if (entry != null)
            {
                entry.PlaybackPosition = 0;
                VideoSettingsCache.FlushToDisk();
            }
        }
        _isStopped = true; // Prevent Stop() from overwriting position=0 with end-of-video time
        StopAutoSaveTimer();
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

        string filePath = _currentVideo.HasValue ? _currentVideo.Value.Path : null;
        string containerFormat = null;
        bool isCodecError = false;
        string codecName = null;

        // Step 1: Check if file uses a non-MP4 container (MPEG-TS, MKV, etc.)
        if (!string.IsNullOrEmpty(filePath))
        {
            containerFormat = DetectContainerFormat(filePath);
        }

        // Step 2: If it's a standard MP4, check for codec issues
        if (containerFormat == null)
        {
            isCodecError = error.Contains("0xc00d36c4") ||
                           error.Contains("byte stream type") ||
                           error.Contains("Cannot read file") ||
                           (error.Contains("unsupported") && error.Contains("format"));

            if (isCodecError && !string.IsNullOrEmpty(filePath))
            {
                codecName = FileMetadataService.TryGetCodecFromHeaders(filePath);
            }
        }

        Debug.Log($"[VRVideoPlayerController] Error analysis - container: {containerFormat ?? "MP4"}, isCodecError: {isCodecError}, codec: {codecName ?? "unknown"}");

        // Fire event for UI layer to show error dialog
        if (OnPlaybackFailed != null)
        {
            OnPlaybackFailed.Invoke(error, isCodecError, codecName, containerFormat);
        }
        else
        {
            OnBackToLibrary?.Invoke();
        }
    }

    /// <summary>
    /// Detect the actual container format by reading file magic bytes.
    /// Returns null if the file is standard MP4, otherwise returns format name.
    /// </summary>
    private static string DetectContainerFormat(string filePath)
    {
        try
        {
            using (var fs = File.OpenRead(filePath))
            {
                if (fs.Length < 12) return null;

                byte[] header = new byte[512];
                int bytesRead = fs.Read(header, 0, Math.Min(512, (int)fs.Length));
                if (bytesRead < 8) return null;

                // Check for standard MP4/MOV: ftyp atom at offset 4
                if (header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
                    return null; // Standard MP4

                // Check for MKV/WebM: EBML signature 1A 45 DF A3
                for (int i = 0; i < bytesRead - 4; i++)
                {
                    if (header[i] == 0x1A && header[i + 1] == 0x45 && header[i + 2] == 0xDF && header[i + 3] == 0xA3)
                        return "MKV/WebM";
                }

                // Check for MPEG-TS: sync byte 0x47 (search in first 256 bytes)
                if (bytesRead >= 188)
                {
                    for (int i = 0; i < bytesRead - 188; i++)
                    {
                        if (header[i] == 0x47 && header[i + 188] == 0x47)
                            return "MPEG-TS";
                    }
                }

                // Check for AVI: RIFF....AVI
                if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46)
                    return "AVI";

                // Check for FLV: FLV signature
                if (header[0] == 0x46 && header[1] == 0x4C && header[2] == 0x56)
                    return "FLV";

                // Not recognized - might be MP4 with unusual structure or corrupted
                return "Unknown";
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VRVideoPlayerController] Failed to detect container format: {ex.Message}");
            return null;
        }
    }

    private void HandleStateChanged(VideoPlaybackEngine.PlaybackState state)
    {
        bool isPlaying = state == VideoPlaybackEngine.PlaybackState.Playing;
        _controlsPanel?.SetPlayState(isPlaying);

        // Show loading status during preparation/buffering
        if (state == VideoPlaybackEngine.PlaybackState.Loading)
        {
            _controlsPanel?.SetTitle("Preparing...");
        }
        else if (state == VideoPlaybackEngine.PlaybackState.Buffering)
        {
            _controlsPanel?.SetTitle("Buffering...");
        }
        else if (state == VideoPlaybackEngine.PlaybackState.Playing && _currentVideo.HasValue)
        {
            _controlsPanel?.SetTitle(_currentVideo.Value.Title);
        }
    }

    private void HandleTimeUpdate(double currentTime)
    {
        _controlsPanel?.SetCurrentTime((float)currentTime);
    }

    private void HandleSeekCompleted()
    {
        _controlsPanel?.OnSeekCompleted();
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
        if (reticle == null) reticle = FindFirstObjectByType<VRGazeReticle>();

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

            // Update saved flat transform to camera forward — so controls and sphere alignment
            // point toward the new forward direction instead of the old flat screen position
            if (cam != null)
            {
                Vector3 camFwd = cam.transform.forward;
                camFwd.y = 0;
                if (camFwd.sqrMagnitude < 0.001f) camFwd = Vector3.forward;
                camFwd.Normalize();
                Vector3 newPos = cam.transform.position + camFwd * 2.0f;
                newPos.y = cam.transform.position.y;
                _projectionSystem.UpdateSavedFlatTransform(newPos, Quaternion.LookRotation(camFwd));
            }
        }

        // Recenter VideoControlsContainer (now uses updated saved flat transform → camera forward)
        if (_controlsPanel != null && _projectionSystem != null)
        {
            bool isImmersive = !ProjectionDetector.SupportsScreenSettings(_projectionSystem.CurrentProjection);
            RepositionControlsForProjection(isImmersive);
        }

        // Notify VRMediaAppController to reposition menu button in the new forward direction
        if (_projectionSystem != null)
        {
            OnProjectionSettingsUpdated?.Invoke(
                _projectionSystem.CurrentProjection,
                _projectionSystem.CurrentStereoMode);
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

    private void OnApplicationQuit()
    {
        // Save settings on app quit (safety net for Android where OnDestroy may not fire)
        if (!_isStopped)
        {
            SaveCurrentVideoSettings();
            _isStopped = true;
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
            _playbackEngine.OnSeekCompleted -= HandleSeekCompleted;
            _playbackEngine.OnBufferingCompleted -= HandleBufferingCompleted;
        }

        if (_controlsPanel != null)
        {
            _controlsPanel.OnPlayPause -= TogglePlayPause;
            _controlsPanel.OnSeek -= Seek;
            _controlsPanel.OnVolumeChanged -= SetVolume;
            _controlsPanel.OnSpeedChanged -= SetPlaybackSpeed;
            _controlsPanel.OnBackClicked -= HandleBackClicked;
            // Forward/Backward buttons now seek ±10s directly via OnSeek
            _controlsPanel.OnVRModeClicked -= HandleVRModeClicked;
            _controlsPanel.OnHeadsetModeClicked -= HandleHeadsetModeClicked;
            _controlsPanel.OnRecenterClicked -= HandleRecenter;
            _controlsPanel.OnVisibilityChanged -= HandleControlsVisibilityChanged;
        }

        UnwireProjectionEvents();
        UnwireEnvironmentEvents();

        // Clear zoom override
        VirtualObjectsZoomController.Instance?.ClearZoomOverride();
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

    #region Per-Video Settings Cache

    private void StartAutoSaveTimer()
    {
        StopAutoSaveTimer();
        _autoSaveCoroutine = StartCoroutine(AutoSaveLoop());
    }

    private void StopAutoSaveTimer()
    {
        if (_autoSaveCoroutine != null)
        {
            StopCoroutine(_autoSaveCoroutine);
            _autoSaveCoroutine = null;
        }
    }

    private IEnumerator AutoSaveLoop()
    {
        var wait = new WaitForSeconds(10f);
        while (true)
        {
            yield return wait;
            SaveCurrentVideoSettings();
        }
    }

    private void SaveCurrentVideoSettings()
    {
        if (_isStopped) return; // Already saved during Stop(), engine state is stale
        if (_currentVideo == null || string.IsNullOrEmpty(_currentVideo.Value.Path)) return;

        var entry = new VideoSettingsEntry
        {
            FilePath = _currentVideo.Value.Path,
            Projection = (int)(_projectionSystem?.CurrentProjection ?? VideoProjectionType.Flat),
            Stereo = (int)(_projectionSystem?.CurrentStereoMode ?? StereoMode.Mono),
            Monitor = (int)_currentMonitor,
            Environment = (int)_currentEnv,
            PlaybackPosition = _playbackEngine?.CurrentTime ?? 0,
            PlaybackSpeed = _playbackEngine?.PlaybackSpeed ?? 1f,
            Brightness = GetCurrentShaderFloat("_Brightness", 1f),
            Contrast = GetCurrentShaderFloat("_Contrast", 1f),
            Saturation = GetCurrentShaderFloat("_Saturation", 1f),
            Sharpness = GetCurrentShaderFloat("_Sharpness", 0.5f),
            Tint = GetCurrentShaderFloat("_Tint", 0f),
            Temperature = GetCurrentShaderFloat("_Temperature", 0f),
            ScreenDistance = _displaySettings.Distance,
            ScreenScale = _displaySettings.Scale,
            ScreenCurvature = _displaySettings.Curvature,
            AspectRatio = _projectionSystem?.GetAspectRatioOverride() ?? "default",
            FOVZoom = GetImmersiveFOV(),
            ImmTilt = GetCurrentShaderFloat("_Tilt", 0f),
            ImmYaw = GetCurrentShaderFloat("_YawOffset", 0f),
            VerticalShift = GetCurrentShaderFloat("_VerticalShift", 0f),
            HorizontalShift = GetCurrentShaderFloat("_HorizontalShift", 0f),
            LRInverse = GetCurrentShaderFloat("_LRInverse", 0f) > 0.5f,
            LastAccessedTicks = System.DateTime.UtcNow.Ticks
        };

        VideoSettingsCache.Set(entry.FilePath, entry);
        VideoSettingsCache.FlushToDisk();
        Debug.Log($"[VRVideoPlayerController] Saved settings: Position={entry.PlaybackPosition:F1}s, Brightness={entry.Brightness:F2}, AR={entry.AspectRatio}");
    }

    private void RestoreCachedSettings()
    {
        if (_cachedEntry == null) return;

        // Restore picture adjustments
        ApplyShaderFloat("_Brightness", _cachedEntry.Brightness);
        ApplyShaderFloat("_Contrast", _cachedEntry.Contrast);
        ApplyShaderFloat("_Saturation", _cachedEntry.Saturation);
        ApplyShaderFloat("_Sharpness", _cachedEntry.Sharpness);
        ApplyShaderFloat("_Tint", _cachedEntry.Tint);
        ApplyShaderFloat("_Temperature", _cachedEntry.Temperature);

        // Restore display settings (flat mode only)
        if (ProjectionDetector.SupportsScreenSettings((VideoProjectionType)_cachedEntry.Projection))
        {
            _displaySettings.Distance = _cachedEntry.ScreenDistance;
            _displaySettings.Scale = _cachedEntry.ScreenScale;
            _projectionSystem?.UpdateDisplay(_displaySettings);

            // Restore aspect ratio
            _projectionSystem?.SetAspectRatioOverride(_cachedEntry.AspectRatio ?? "default");
        }

        // Restore immersive settings
        if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
        {
            if (_cachedEntry.FOVZoom > 0f) imm.SetFieldOfView(_cachedEntry.FOVZoom);
            imm.SetShaderFloat("_Tilt", _cachedEntry.ImmTilt);
            imm.SetShaderFloat("_VerticalShift", _cachedEntry.VerticalShift);
            imm.SetShaderFloat("_HorizontalShift", _cachedEntry.HorizontalShift);
            if (_cachedEntry.LRInverse) imm.SetShaderFloat("_LRInverse", 1f);
        }

        // Restore playback speed
        if (_playbackEngine != null && _cachedEntry.PlaybackSpeed > 0f)
            _playbackEngine.PlaybackSpeed = _cachedEntry.PlaybackSpeed;

        // Restore environment: set lights based on cached env type
        if (_environmentController != null)
        {
            switch ((RTTMediaProjectionPopup.EnvironmentType)_cachedEntry.Environment)
            {
                case RTTMediaProjectionPopup.EnvironmentType.Cinema:
                    _environmentController.SetLightsEnabled(false);
                    break;
                case RTTMediaProjectionPopup.EnvironmentType.LightOff:
                    _environmentController.SetLightsEnabled(false);
                    _environmentController.HideEnvironment();
                    break;
                case RTTMediaProjectionPopup.EnvironmentType.Room:
                    _environmentController.SetLightsEnabled(true);
                    _environmentController.ShowEnvironment();
                    break;
            }
        }

        Debug.Log($"[VRVideoPlayerController] Restored cached settings: Brightness={_cachedEntry.Brightness:F2}, " +
            $"Speed={_cachedEntry.PlaybackSpeed:F2}, FOV={_cachedEntry.FOVZoom:F0}");

        _cachedEntry = null; // consumed
    }

    private void ApplyShaderFloat(string param, float value)
    {
        if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
            imm.SetShaderFloat(param, value);
        else if (_projectionSystem?.ActiveRenderer is FlatProjectionRenderer flat)
            flat.SetBoardShaderFloat(param, value);
    }

    private float GetCurrentShaderFloat(string param, float defaultVal)
    {
        if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
            return imm.GetShaderFloat(param, defaultVal);
        if (_projectionSystem?.ActiveRenderer is FlatProjectionRenderer flat)
            return flat.GetBoardShaderFloat(param, defaultVal);
        return defaultVal;
    }

    private float GetImmersiveFOV()
    {
        if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
            return imm.CurrentFOV;
        return 0f;
    }

    #endregion
}
