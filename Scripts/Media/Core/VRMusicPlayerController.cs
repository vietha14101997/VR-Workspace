using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Controller for VR music playback.
/// Manages audio playback, visualization, and playlist.
/// </summary>
public class VRMusicPlayerController : MonoBehaviour
{
    #region Events
    public event Action OnBackToLibrary;
    public event Action<MediaAudioInfo> OnTrackStarted;
    public event Action<MediaAudioInfo> OnTrackEnded;
    public event Action<int, int> OnTrackChanged; // (currentIndex, totalCount)
    public event Action<bool> OnPlayStateChanged;
    public event Action<RepeatMode> OnRepeatModeChanged;
    public event Action<bool> OnShuffleModeChanged;
    #endregion

    #region Enums
    public enum RepeatMode
    {
        None,       // No repeat
        All,        // Repeat entire playlist
        One         // Repeat current track
    }
    #endregion

    #region Properties
    public bool IsActive { get; private set; }
    public bool IsPlaying => _playbackEngine?.IsPlaying ?? false;
    public bool IsPaused => _playbackEngine?.IsPaused ?? false;
    public MediaAudioInfo? CurrentTrack => _currentTrack;
    public int CurrentIndex => _currentIndex;
    public int TotalTracks => _playlist?.Count ?? 0;
    
    public float CurrentTime => _playbackEngine?.CurrentTime ?? 0f;
    public float Duration => _playbackEngine?.Duration ?? 0f;
    public float Progress => _playbackEngine?.Progress ?? 0f;
    
    public RepeatMode Repeat { get; private set; } = RepeatMode.None;
    public bool Shuffle { get; private set; } = false;
    
    public float Volume
    {
        get => _playbackEngine?.Volume ?? 1f;
        set
        {
            if (_playbackEngine != null)
                _playbackEngine.Volume = value;
            _controlsPanel?.SetVolume(value);
        }
    }
    #endregion

    #region Private Fields
    private AudioPlaybackEngine _playbackEngine;
    private AudioVisualizationSystem _visualizationSystem;
    private RTTMusicControlsPanel _controlsPanel;
    private MediaEnvironmentController _environmentController;

    private List<MediaAudioInfo> _playlist;
    private List<int> _shuffledIndices;
    private MediaAudioInfo? _currentTrack;
    private int _currentIndex;
    private int _shuffleIndex;
    
    private bool _isInitialized;
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize the music player controller
    /// </summary>
    public void Initialize(
        AudioPlaybackEngine playbackEngine,
        AudioVisualizationSystem visualizationSystem,
        RTTMusicControlsPanel controlsPanel)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[VRMusicPlayerController] Already initialized");
            return;
        }

        _playbackEngine = playbackEngine;
        _visualizationSystem = visualizationSystem;
        _controlsPanel = controlsPanel;

        // Initialize visualization with playback engine
        _visualizationSystem?.Initialize(_playbackEngine);

        // Setup environment controller (optional)
        try
        {
            _environmentController = MediaEnvironmentController.Instance;
            _environmentController.Initialize();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VRMusicPlayerController] Environment control disabled: {ex.Message}");
            _environmentController = null;
        }

        WireEvents();
        _isInitialized = true;

        Debug.Log("[VRMusicPlayerController] Initialized");
    }

    private void WireEvents()
    {
        if (_playbackEngine != null)
        {
            _playbackEngine.OnPrepareCompleted += HandlePrepareCompleted;
            _playbackEngine.OnPlaybackStarted += HandlePlaybackStarted;
            _playbackEngine.OnPlaybackPaused += HandlePlaybackPaused;
            _playbackEngine.OnPlaybackEnded += HandlePlaybackEnded;
            _playbackEngine.OnTimeUpdate += HandleTimeUpdate;
            _playbackEngine.OnError += HandleError;
        }

        if (_controlsPanel != null)
        {
            _controlsPanel.OnPlayPause += TogglePlayPause;
            _controlsPanel.OnPrevious += Previous;
            _controlsPanel.OnNext += Next;
            _controlsPanel.OnSeek += Seek;
            _controlsPanel.OnVolumeChanged += SetVolume;
            _controlsPanel.OnRepeatToggle += CycleRepeatMode;
            _controlsPanel.OnShuffleToggle += ToggleShuffle;
            _controlsPanel.OnVisualizationToggle += ToggleVisualization;
            _controlsPanel.OnBackClicked += HandleBackClicked;
        }
    }

    private void UnwireEvents()
    {
        if (_playbackEngine != null)
        {
            _playbackEngine.OnPrepareCompleted -= HandlePrepareCompleted;
            _playbackEngine.OnPlaybackStarted -= HandlePlaybackStarted;
            _playbackEngine.OnPlaybackPaused -= HandlePlaybackPaused;
            _playbackEngine.OnPlaybackEnded -= HandlePlaybackEnded;
            _playbackEngine.OnTimeUpdate -= HandleTimeUpdate;
            _playbackEngine.OnError -= HandleError;
        }

        if (_controlsPanel != null)
        {
            _controlsPanel.OnPlayPause -= TogglePlayPause;
            _controlsPanel.OnPrevious -= Previous;
            _controlsPanel.OnNext -= Next;
            _controlsPanel.OnSeek -= Seek;
            _controlsPanel.OnVolumeChanged -= SetVolume;
            _controlsPanel.OnRepeatToggle -= CycleRepeatMode;
            _controlsPanel.OnShuffleToggle -= ToggleShuffle;
            _controlsPanel.OnVisualizationToggle -= ToggleVisualization;
            _controlsPanel.OnBackClicked -= HandleBackClicked;
        }
    }
    #endregion

    #region Public API - Playback Control
    /// <summary>
    /// Play a single track
    /// </summary>
    public void PlayTrack(MediaAudioInfo track)
    {
        _playlist = new List<MediaAudioInfo> { track };
        _currentIndex = 0;
        LoadAndPlayCurrent();
        Show();
    }

    /// <summary>
    /// Play tracks from a list
    /// </summary>
    public void PlayTracks(List<MediaAudioInfo> tracks, int startIndex = 0)
    {
        if (tracks == null || tracks.Count == 0)
        {
            Debug.LogWarning("[VRMusicPlayerController] No tracks to play");
            return;
        }

        _playlist = new List<MediaAudioInfo>(tracks);
        _currentIndex = Mathf.Clamp(startIndex, 0, tracks.Count - 1);
        
        if (Shuffle)
        {
            GenerateShuffledIndices();
            _shuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (_shuffleIndex < 0) _shuffleIndex = 0;
        }
        
        LoadAndPlayCurrent();
        Show();
    }

    /// <summary>
    /// Add track to queue
    /// </summary>
    public void AddToQueue(MediaAudioInfo track)
    {
        if (_playlist == null)
        {
            PlayTrack(track);
            return;
        }

        _playlist.Add(track);
        
        if (Shuffle)
        {
            _shuffledIndices.Add(_playlist.Count - 1);
        }

        Debug.Log($"[VRMusicPlayerController] Added to queue: {track.Title}");
    }

    /// <summary>
    /// Play
    /// </summary>
    public void Play()
    {
        _playbackEngine?.Play();
    }

    /// <summary>
    /// Pause
    /// </summary>
    public void Pause()
    {
        _playbackEngine?.Pause();
    }

    /// <summary>
    /// Toggle play/pause
    /// </summary>
    public void TogglePlayPause()
    {
        _playbackEngine?.TogglePlayPause();
    }

    /// <summary>
    /// Stop playback
    /// </summary>
    public void Stop()
    {
        _playbackEngine?.Stop();
        _visualizationSystem?.Hide();
    }

    /// <summary>
    /// Play next track
    /// </summary>
    public void Next()
    {
        if (_playlist == null || _playlist.Count == 0) return;

        if (Shuffle)
        {
            _shuffleIndex = (_shuffleIndex + 1) % _shuffledIndices.Count;
            _currentIndex = _shuffledIndices[_shuffleIndex];
        }
        else
        {
            if (_currentIndex < _playlist.Count - 1)
            {
                _currentIndex++;
            }
            else if (Repeat == RepeatMode.All)
            {
                _currentIndex = 0;
            }
            else
            {
                // End of playlist, no repeat
                Stop();
                return;
            }
        }

        LoadAndPlayCurrent();
    }

    /// <summary>
    /// Play previous track
    /// </summary>
    public void Previous()
    {
        if (_playlist == null || _playlist.Count == 0) return;

        // If played more than 3 seconds, restart current track
        if (CurrentTime > 3f)
        {
            Seek(0);
            return;
        }

        if (Shuffle)
        {
            _shuffleIndex = (_shuffleIndex - 1 + _shuffledIndices.Count) % _shuffledIndices.Count;
            _currentIndex = _shuffledIndices[_shuffleIndex];
        }
        else
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
            }
            else if (Repeat == RepeatMode.All)
            {
                _currentIndex = _playlist.Count - 1;
            }
            else
            {
                // Start of playlist, restart current
                Seek(0);
                return;
            }
        }

        LoadAndPlayCurrent();
    }

    /// <summary>
    /// Go to specific track index
    /// </summary>
    public void GoToIndex(int index)
    {
        if (_playlist == null || index < 0 || index >= _playlist.Count) return;

        _currentIndex = index;
        
        if (Shuffle)
        {
            _shuffleIndex = _shuffledIndices.IndexOf(index);
            if (_shuffleIndex < 0) _shuffleIndex = 0;
        }
        
        LoadAndPlayCurrent();
    }

    /// <summary>
    /// Seek to time in seconds
    /// </summary>
    public void Seek(float time)
    {
        _playbackEngine?.Seek(time);
    }

    /// <summary>
    /// Set volume (0-1)
    /// </summary>
    public void SetVolume(float volume)
    {
        Volume = volume;
    }
    #endregion

    #region Public API - Modes
    /// <summary>
    /// Set repeat mode
    /// </summary>
    public void SetRepeatMode(RepeatMode mode)
    {
        Repeat = mode;
        
        if (_playbackEngine != null)
        {
            _playbackEngine.Loop = (mode == RepeatMode.One);
        }
        
        _controlsPanel?.SetRepeatMode(mode);
        OnRepeatModeChanged?.Invoke(mode);

        Debug.Log($"[VRMusicPlayerController] Repeat mode: {mode}");
    }

    /// <summary>
    /// Cycle through repeat modes
    /// </summary>
    public void CycleRepeatMode()
    {
        RepeatMode newMode = Repeat switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.None,
            _ => RepeatMode.None
        };
        SetRepeatMode(newMode);
    }

    /// <summary>
    /// Toggle shuffle mode
    /// </summary>
    public void ToggleShuffle()
    {
        SetShuffleMode(!Shuffle);
    }

    /// <summary>
    /// Set shuffle mode
    /// </summary>
    public void SetShuffleMode(bool enabled)
    {
        Shuffle = enabled;
        
        if (enabled && _playlist != null && _playlist.Count > 1)
        {
            GenerateShuffledIndices();
            _shuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (_shuffleIndex < 0) _shuffleIndex = 0;
        }
        
        _controlsPanel?.SetShuffleMode(enabled);
        OnShuffleModeChanged?.Invoke(enabled);

        Debug.Log($"[VRMusicPlayerController] Shuffle: {enabled}");
    }

    /// <summary>
    /// Toggle visualization
    /// </summary>
    public void ToggleVisualization()
    {
        if (_visualizationSystem != null)
        {
            if (_visualizationSystem.IsActive)
                _visualizationSystem.Hide();
            else
                _visualizationSystem.Show();
        }
    }

    /// <summary>
    /// Cycle visualization type
    /// </summary>
    public void NextVisualization()
    {
        _visualizationSystem?.NextVisualization();
    }
    #endregion

    #region Public API - Display
    /// <summary>
    /// Show the music player
    /// </summary>
    public void Show()
    {
        IsActive = true;
        _controlsPanel?.Show();
        _visualizationSystem?.Show();

        // Dim environment for immersive experience
        _environmentController?.SetLightsEnabled(false);
    }

    /// <summary>
    /// Hide the music player
    /// </summary>
    public void Hide()
    {
        IsActive = false;
        _controlsPanel?.Hide();
        _visualizationSystem?.Hide();

        // Restore environment
        _environmentController?.Reset();
    }

    /// <summary>
    /// Cleanup and stop
    /// </summary>
    public void Cleanup()
    {
        Stop();
        Hide();
        _playlist = null;
        _shuffledIndices = null;
        _currentTrack = null;
        _currentIndex = 0;
    }
    #endregion

    #region Private Methods
    private void LoadAndPlayCurrent()
    {
        if (_playlist == null || _currentIndex >= _playlist.Count) return;

        _currentTrack = _playlist[_currentIndex];
        
        // Load the track
        _playbackEngine?.PrepareFromPath(_currentTrack.Value.Path);
        
        // Update UI
        _controlsPanel?.SetTrackInfo(_currentTrack.Value);
        OnTrackChanged?.Invoke(_currentIndex, _playlist.Count);

        Debug.Log($"[VRMusicPlayerController] Loading: {_currentTrack.Value.Title}");
    }

    private void GenerateShuffledIndices()
    {
        if (_playlist == null) return;

        _shuffledIndices = new List<int>();
        for (int i = 0; i < _playlist.Count; i++)
        {
            _shuffledIndices.Add(i);
        }

        // Fisher-Yates shuffle
        for (int i = _shuffledIndices.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int temp = _shuffledIndices[i];
            _shuffledIndices[i] = _shuffledIndices[j];
            _shuffledIndices[j] = temp;
        }

        // Make sure current track is at the start
        if (_currentIndex >= 0 && _currentIndex < _shuffledIndices.Count)
        {
            int currentPos = _shuffledIndices.IndexOf(_currentIndex);
            if (currentPos > 0)
            {
                _shuffledIndices[currentPos] = _shuffledIndices[0];
                _shuffledIndices[0] = _currentIndex;
            }
        }
    }
    #endregion

    #region Event Handlers
    private void HandlePrepareCompleted()
    {
        // Auto-play when ready
        _playbackEngine?.Play();
        
        if (_currentTrack.HasValue)
        {
            OnTrackStarted?.Invoke(_currentTrack.Value);
        }
    }

    private void HandlePlaybackStarted()
    {
        _controlsPanel?.SetPlayState(true);
        OnPlayStateChanged?.Invoke(true);
    }

    private void HandlePlaybackPaused()
    {
        _controlsPanel?.SetPlayState(false);
        OnPlayStateChanged?.Invoke(false);
    }

    private void HandlePlaybackEnded()
    {
        if (_currentTrack.HasValue)
        {
            OnTrackEnded?.Invoke(_currentTrack.Value);
        }

        // Handle repeat/next
        if (Repeat == RepeatMode.One)
        {
            // Will auto-repeat via AudioSource.loop
        }
        else
        {
            // Play next track
            Next();
        }
    }

    private void HandleTimeUpdate(float time)
    {
        _controlsPanel?.SetCurrentTime(time);
    }

    private void HandleError(string error)
    {
        Debug.LogError($"[VRMusicPlayerController] Playback error: {error}");
        _controlsPanel?.ShowError(error);
        
        // Try next track
        if (_playlist != null && _playlist.Count > 1)
        {
            Next();
        }
    }

    private void HandleBackClicked()
    {
        Cleanup();
        OnBackToLibrary?.Invoke();
    }
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        Cleanup();
        UnwireEvents();
    }
    #endregion
}
