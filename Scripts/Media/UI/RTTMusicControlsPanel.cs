using UnityEngine;
using System;

/// <summary>
/// UI controls panel for VR Music Player.
/// Provides playback controls, track info, and visualization options.
/// </summary>
public class RTTMusicControlsPanel : MonoBehaviour
{
    #region Events
    public event Action OnPlayPause;
    public event Action OnPrevious;
    public event Action OnNext;
    public event Action<float> OnSeek;
    public event Action<float> OnVolumeChanged;
    public event Action OnRepeatToggle;
    public event Action OnShuffleToggle;
    public event Action OnVisualizationToggle;
    public event Action OnBackClicked;
    #endregion

    #region Properties
    public bool IsVisible { get; private set; }
    #endregion

    #region Private Fields
    private bool _isPlaying;
    private float _currentTime;
    private float _duration;
    private float _volume = 1f;
    private VRMusicPlayerController.RepeatMode _repeatMode;
    private bool _shuffleEnabled;
    private MediaAudioInfo? _currentTrack;
    #endregion

    #region Public API
    /// <summary>
    /// Initialize the controls panel
    /// </summary>
    public void Initialize(float width, float height, TMPro.TMP_FontAsset font, Color primaryColor, Color accentColor)
    {
        // TODO: Build UI elements
        Debug.Log("[RTTMusicControlsPanel] Initialized (placeholder)");
    }

    /// <summary>
    /// Show the controls panel
    /// </summary>
    public void Show()
    {
        IsVisible = true;
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Hide the controls panel
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Set track info for display
    /// </summary>
    public void SetTrackInfo(MediaAudioInfo trackInfo)
    {
        _currentTrack = trackInfo;
        // TODO: Update UI with track info (title, artist, album art)
    }

    /// <summary>
    /// Set play/pause state
    /// </summary>
    public void SetPlayState(bool isPlaying)
    {
        _isPlaying = isPlaying;
        // TODO: Update play/pause button icon
    }

    /// <summary>
    /// Set current playback time
    /// </summary>
    public void SetCurrentTime(float time)
    {
        _currentTime = time;
        // TODO: Update progress bar and time display
    }

    /// <summary>
    /// Set track duration
    /// </summary>
    public void SetDuration(float duration)
    {
        _duration = duration;
        // TODO: Update duration display
    }

    /// <summary>
    /// Set volume level
    /// </summary>
    public void SetVolume(float volume)
    {
        _volume = volume;
        // TODO: Update volume slider
    }

    /// <summary>
    /// Set repeat mode
    /// </summary>
    public void SetRepeatMode(VRMusicPlayerController.RepeatMode mode)
    {
        _repeatMode = mode;
        // TODO: Update repeat button icon
    }

    /// <summary>
    /// Set shuffle mode
    /// </summary>
    public void SetShuffleMode(bool enabled)
    {
        _shuffleEnabled = enabled;
        // TODO: Update shuffle button state
    }

    /// <summary>
    /// Show error message
    /// </summary>
    public void ShowError(string message)
    {
        Debug.LogError($"[RTTMusicControlsPanel] {message}");
        // TODO: Show error UI
    }
    #endregion

    #region Internal - Button Handlers
    // These will be wired to UI buttons when built

    internal void HandlePlayPauseClick()
    {
        OnPlayPause?.Invoke();
    }

    internal void HandlePreviousClick()
    {
        OnPrevious?.Invoke();
    }

    internal void HandleNextClick()
    {
        OnNext?.Invoke();
    }

    internal void HandleSeekChange(float value)
    {
        OnSeek?.Invoke(value);
    }

    internal void HandleVolumeChange(float value)
    {
        OnVolumeChanged?.Invoke(value);
    }

    internal void HandleRepeatClick()
    {
        OnRepeatToggle?.Invoke();
    }

    internal void HandleShuffleClick()
    {
        OnShuffleToggle?.Invoke();
    }

    internal void HandleVisualizationClick()
    {
        OnVisualizationToggle?.Invoke();
    }

    internal void HandleBackClick()
    {
        OnBackClicked?.Invoke();
    }
    #endregion
}
