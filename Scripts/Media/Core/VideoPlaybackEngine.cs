using UnityEngine;
using UnityEngine.Video;
using System;
using System.Collections;

/// <summary>
/// Video playback engine - wraps Unity VideoPlayer and provides playback control.
/// Supports both Unity VideoPlayer and HEVC hardware decoding.
/// </summary>
public class VideoPlaybackEngine : MonoBehaviour
{
    #region Enums
    public enum DecoderMode
    {
        UnityVideoPlayer,
        HevcHardware
    }

    public enum PlaybackState
    {
        Idle,
        Loading,
        Ready,
        Buffering,
        Playing,
        Paused,
        Seeking,
        Ended,
        Error
    }
    #endregion

    #region Events
    /// <summary>Fired when video is prepared and ready to play</summary>
    public event Action OnPrepareCompleted;

    /// <summary>Fired when playback reaches the end</summary>
    public event Action OnPlaybackEnded;

    /// <summary>Fired when an error occurs</summary>
    public event Action<string> OnError;

    /// <summary>Fired when state changes</summary>
    public event Action<PlaybackState> OnStateChanged;

    /// <summary>Fired when time updates (approximately 4 times per second)</summary>
    public event Action<double> OnTimeUpdate;

    /// <summary>Fired when a seek operation completes</summary>
    public event Action OnSeekCompleted;

    /// <summary>Fired when buffering completes and first frame is ready on RenderTexture</summary>
    public event Action OnBufferingCompleted;
    #endregion

    #region Properties
    /// <summary>Current decoder mode</summary>
    public DecoderMode CurrentDecoderMode { get; private set; } = DecoderMode.UnityVideoPlayer;

    /// <summary>Current playback state</summary>
    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    /// <summary>Output render texture (for Unity VideoPlayer mode)</summary>
    public RenderTexture OutputTexture { get; private set; }

    /// <summary>Y plane texture (for HEVC NV12 mode)</summary>
    public Texture2D YPlaneTexture { get; private set; }

    /// <summary>UV plane texture (for HEVC NV12 mode)</summary>
    public Texture2D UVPlaneTexture { get; private set; }

    /// <summary>Whether output is in NV12 format (HEVC mode)</summary>
    public bool UseNV12Output => CurrentDecoderMode == DecoderMode.HevcHardware;

    /// <summary>Video duration in seconds</summary>
    public double Duration => _videoPlayer?.length ?? 0;

    /// <summary>Current playback time in seconds</summary>
    public double CurrentTime
    {
        get => _videoPlayer?.time ?? 0;
        set
        {
            if (_videoPlayer != null && _videoPlayer.isPrepared)
            {
                _videoPlayer.time = Mathf.Clamp((float)value, 0, (float)Duration);
            }
        }
    }

    /// <summary>Video frame rate</summary>
    public float FrameRate => _videoPlayer?.frameRate ?? 0;

    /// <summary>Video resolution</summary>
    public Vector2Int Resolution => _videoPlayer != null && _videoPlayer.isPrepared
        ? new Vector2Int((int)_videoPlayer.width, (int)_videoPlayer.height)
        : Vector2Int.zero;

    /// <summary>Is video currently playing</summary>
    public bool IsPlaying => _videoPlayer?.isPlaying ?? false;

    /// <summary>Is video prepared and ready</summary>
    public bool IsPrepared => _videoPlayer?.isPrepared ?? false;

    /// <summary>Current volume (0-1)</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Mathf.Clamp01(value);
            ApplyVolume();
        }
    }

    /// <summary>Is audio muted</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            ApplyVolume();
        }
    }

    /// <summary>Playback speed (0.5 - 2.0)</summary>
    public float PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = Mathf.Clamp(value, 0.5f, 2.0f);
            if (_videoPlayer != null)
                _videoPlayer.playbackSpeed = _playbackSpeed;
        }
    }

    /// <summary>Current file path</summary>
    public string CurrentPath { get; private set; }
    #endregion

    #region Private Fields
    private VideoPlayer _videoPlayer;
    private AudioSource _audioSource;
    private float _volume = 1.0f;
    private bool _isMuted = false;
    private float _playbackSpeed = 1.0f;
    private double _lastReportedTime = -1;
    private Coroutine _timeUpdateCoroutine;
    private PlaybackState _preSeekState = PlaybackState.Idle;
    private Coroutine _seekCoroutine;
    private Coroutine _bufferingCoroutine;
    private int _prepareSequenceId = 0;
    private bool _frameReadyReceived = false;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        SetupComponents();
    }

    private void OnDestroy()
    {
        Dispose();
    }

    private void SetupComponents()
    {
        // Setup AudioSource
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0; // 2D audio

        // Setup VideoPlayer
        _videoPlayer = gameObject.AddComponent<VideoPlayer>();
        _videoPlayer.playOnAwake = false;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _videoPlayer.SetTargetAudioSource(0, _audioSource);
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.isLooping = false;
        _videoPlayer.aspectRatio = VideoAspectRatio.NoScaling;
        _videoPlayer.waitForFirstFrame = true;

        // Event handlers
        _videoPlayer.prepareCompleted += OnVideoPrepared;
        _videoPlayer.loopPointReached += OnVideoEnded;
        _videoPlayer.errorReceived += OnVideoError;
        _videoPlayer.seekCompleted += OnVideoSeekCompleted;
        _videoPlayer.frameReady += OnFrameReady;
    }
    #endregion

    #region Public API
    /// <summary>
    /// Prepare a local video file for playback
    /// </summary>
    public void PrepareLocal(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            SetError("File path is empty");
            return;
        }

        CancelBuffering();
        _prepareSequenceId++;

        CurrentPath = filePath;
        SetState(PlaybackState.Loading);

        // Clean up previous resources
        CleanupRenderTexture();

        // Determine decoder mode
        CurrentDecoderMode = ShouldUseHevcDecoder(filePath)
            ? DecoderMode.HevcHardware
            : DecoderMode.UnityVideoPlayer;

        if (CurrentDecoderMode == DecoderMode.HevcHardware)
        {
            // TODO: Implement HEVC hardware decoder initialization
            // For now, fall back to Unity VideoPlayer
            CurrentDecoderMode = DecoderMode.UnityVideoPlayer;
        }

        // Prepare with Unity VideoPlayer
        _videoPlayer.url = "file://" + filePath;
        _videoPlayer.Prepare();
    }

    /// <summary>
    /// Start playback
    /// </summary>
    public void Play()
    {
        if (!IsPrepared)
        {
            Debug.LogWarning("[VideoPlaybackEngine] Cannot play - video not prepared");
            return;
        }

        CancelBuffering();
        _videoPlayer.Play();
        SetState(PlaybackState.Playing);
        StartTimeUpdateCoroutine();
    }

    /// <summary>
    /// Pause playback
    /// </summary>
    public void Pause()
    {
        if (IsPlaying)
        {
            _videoPlayer.Pause();
            SetState(PlaybackState.Paused);
            StopTimeUpdateCoroutine();
        }
    }

    /// <summary>
    /// Toggle play/pause
    /// </summary>
    public void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    /// <summary>
    /// Stop playback and reset to beginning
    /// </summary>
    public void Stop()
    {
        CancelBuffering();
        _videoPlayer.Stop();
        _videoPlayer.time = 0;
        SetState(PlaybackState.Ready);
        StopTimeUpdateCoroutine();
    }

    /// <summary>
    /// Seek to specific time in seconds
    /// </summary>
    public void Seek(double timeSeconds)
    {
        if (!IsPrepared) return;

        // Cancel any pending seek timeout
        if (_seekCoroutine != null)
        {
            StopCoroutine(_seekCoroutine);
            _seekCoroutine = null;
        }

        // Only capture pre-seek state if not already mid-seek
        // (preserves original state during rapid seek chains)
        if (State != PlaybackState.Seeking)
        {
            _preSeekState = State;
        }

        SetState(PlaybackState.Seeking);
        _videoPlayer.time = Mathf.Clamp((float)timeSeconds, 0, (float)Duration);

        // seekCompleted callback will handle state restoration.
        // Fallback timeout ensures recovery if seekCompleted never fires.
        _seekCoroutine = StartCoroutine(SeekTimeoutFallback());
    }

    /// <summary>
    /// Seek by delta seconds (positive or negative)
    /// </summary>
    public void SeekRelative(double deltaSeconds)
    {
        Seek(CurrentTime + deltaSeconds);
    }

    /// <summary>
    /// Clean up all resources
    /// </summary>
    public void Dispose()
    {
        CancelBuffering();
        StopTimeUpdateCoroutine();

        if (_videoPlayer != null)
        {
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.loopPointReached -= OnVideoEnded;
            _videoPlayer.errorReceived -= OnVideoError;
            _videoPlayer.seekCompleted -= OnVideoSeekCompleted;
            _videoPlayer.frameReady -= OnFrameReady;
            _videoPlayer.Stop();
        }

        CleanupRenderTexture();

        CurrentPath = null;
        SetState(PlaybackState.Idle);
    }
    #endregion

    #region Private Methods
    private void SetState(PlaybackState newState)
    {
        if (State != newState)
        {
            State = newState;
            OnStateChanged?.Invoke(newState);
        }
    }

    private void SetError(string message)
    {
        CancelBuffering();
        Debug.LogError($"[VideoPlaybackEngine] Error: {message}");
        SetState(PlaybackState.Error);
        OnError?.Invoke(message);
    }

    private void ApplyVolume()
    {
        if (_audioSource != null)
        {
            _audioSource.volume = _isMuted ? 0 : _volume;
        }
    }

    private void CleanupRenderTexture()
    {
        if (OutputTexture != null)
        {
            _videoPlayer.targetTexture = null;
            OutputTexture.Release();
            Destroy(OutputTexture);
            OutputTexture = null;
        }
    }

    private void CreateRenderTexture(int width, int height)
    {
        CleanupRenderTexture();

        OutputTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        OutputTexture.filterMode = FilterMode.Trilinear;
        OutputTexture.wrapMode = TextureWrapMode.Clamp;
        OutputTexture.Create();

        _videoPlayer.targetTexture = OutputTexture;
    }

    private bool ShouldUseHevcDecoder(string filePath)
    {
        // Check if HEVC decoder is available and file might be HEVC
        // For now, always use Unity VideoPlayer
#if UNITY_ANDROID && !UNITY_EDITOR
        // Could check file extension or probe the file
        // return HevcDecoderPlugin.IsAvailable();
#endif
        return false;
    }

    private void StartTimeUpdateCoroutine()
    {
        StopTimeUpdateCoroutine();
        _timeUpdateCoroutine = StartCoroutine(TimeUpdateCoroutine());
    }

    private void StopTimeUpdateCoroutine()
    {
        if (_timeUpdateCoroutine != null)
        {
            StopCoroutine(_timeUpdateCoroutine);
            _timeUpdateCoroutine = null;
        }
    }

    private IEnumerator TimeUpdateCoroutine()
    {
        var wait = new WaitForSeconds(0.25f); // Update 4 times per second
        while (true)
        {
            if (IsPlaying && CurrentTime != _lastReportedTime)
            {
                _lastReportedTime = CurrentTime;
                OnTimeUpdate?.Invoke(CurrentTime);
            }
            yield return wait;
        }
    }

    private void OnFrameReady(VideoPlayer source, long frameIdx)
    {
        _frameReadyReceived = true;
    }

    private void StartBuffering()
    {
        CancelBuffering();
        _prepareSequenceId++;
        _bufferingCoroutine = StartCoroutine(BufferingCoroutine(_prepareSequenceId));
    }

    private void CancelBuffering()
    {
        if (_bufferingCoroutine != null)
        {
            StopCoroutine(_bufferingCoroutine);
            _bufferingCoroutine = null;
        }
        if (_videoPlayer != null)
            _videoPlayer.sendFrameReadyEvents = false;
    }

    private IEnumerator BufferingCoroutine(int sequenceId)
    {
        SetState(PlaybackState.Buffering);

        // Enable frameReady events for reliable first-frame detection
        // (proven pattern from VideoFrameExtractor)
        _frameReadyReceived = false;
        _videoPlayer.sendFrameReadyEvents = true;

        // Play to trigger decoder pipeline
        _videoPlayer.Play();

        // Wait for first frame to render to RenderTexture
        float timeout = 5.0f;
        float elapsed = 0f;

        while (!_frameReadyReceived && elapsed < timeout)
        {
            if (_prepareSequenceId != sequenceId || State == PlaybackState.Error)
                yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Extra frame for GPU to finalize render-to-texture
        yield return null;

        if (_prepareSequenceId != sequenceId) yield break;

        // Disable frameReady events (performance impact during normal playback)
        _videoPlayer.sendFrameReadyEvents = false;

        // Pause - first frame is now on RenderTexture
        _videoPlayer.Pause();

        // Seek back to beginning so playback starts from frame 0
        _videoPlayer.time = 0;
        yield return null;

        if (_prepareSequenceId != sequenceId) yield break;

        _bufferingCoroutine = null;
        SetState(PlaybackState.Ready);
        OnBufferingCompleted?.Invoke();

        Debug.Log($"[VideoPlaybackEngine] Buffering complete in {elapsed:F2}s - first frame ready");
    }

    private void OnVideoSeekCompleted(VideoPlayer source)
    {
        // Cancel timeout fallback
        if (_seekCoroutine != null)
        {
            StopCoroutine(_seekCoroutine);
            _seekCoroutine = null;
        }

        RestorePreSeekState();
    }

    private IEnumerator SeekTimeoutFallback()
    {
        // Safety timeout: if seekCompleted hasn't fired after 3 seconds,
        // restore state anyway (handles edge cases like seek to same position).
        yield return new WaitForSeconds(3.0f);

        Debug.LogWarning("[VideoPlaybackEngine] Seek timeout - restoring state via fallback");
        _seekCoroutine = null;
        RestorePreSeekState();
    }

    private void RestorePreSeekState()
    {
        if (State != PlaybackState.Seeking) return; // Already restored

        if (_preSeekState == PlaybackState.Playing)
        {
            SetState(PlaybackState.Playing);
        }
        else
        {
            SetState(PlaybackState.Paused);
        }

        OnSeekCompleted?.Invoke();
    }
    #endregion

    #region Video Player Callbacks
    private void OnVideoPrepared(VideoPlayer source)
    {
        int width = (int)source.width;
        int height = (int)source.height;

        if (width <= 0 || height <= 0)
        {
            SetError("Invalid video dimensions");
            return;
        }

        // Create render texture with video dimensions
        CreateRenderTexture(width, height);

        SetState(PlaybackState.Ready);
        OnPrepareCompleted?.Invoke();

        // Start buffering to prime decoder and render first frame to RenderTexture
        StartBuffering();

        Debug.Log($"[VideoPlaybackEngine] Video prepared: {width}x{height}, {Duration:F1}s, {FrameRate:F1}fps");
    }

    private void OnVideoEnded(VideoPlayer source)
    {
        SetState(PlaybackState.Ended);
        StopTimeUpdateCoroutine();
        OnPlaybackEnded?.Invoke();
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        SetError(message);
    }
    #endregion
}
