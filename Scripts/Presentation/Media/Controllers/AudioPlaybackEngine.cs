using UnityEngine;
using System;
using System.Collections;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// Engine for audio playback in VR Music Player.
    /// Wraps Unity's AudioSource with additional features.
    /// </summary>
    public class AudioPlaybackEngine : MonoBehaviour
    {
        #region Events
        public event Action OnPrepareCompleted;
        public event Action OnPlaybackStarted;
        public event Action OnPlaybackPaused;
        public event Action OnPlaybackEnded;
        public event Action OnPlaybackStopped;
        public event Action<float> OnTimeUpdate;
        public event Action<string> OnError;
        #endregion

        #region Enums
        public enum PlaybackState
        {
            Idle,
            Preparing,
            Ready,
            Playing,
            Paused,
            Stopped
        }
        #endregion

        #region Properties
        public PlaybackState State { get; private set; } = PlaybackState.Idle;
        public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;
        public bool IsPaused => State == PlaybackState.Paused;
        public bool IsReady => State == PlaybackState.Ready || State == PlaybackState.Playing || State == PlaybackState.Paused;

        public float CurrentTime => _audioSource?.time ?? 0f;
        public float Duration => _audioClip?.length ?? 0f;
        public float Progress => Duration > 0 ? CurrentTime / Duration : 0f;

        public float Volume
        {
            get => _audioSource?.volume ?? 1f;
            set
            {
                if (_audioSource != null)
                    _audioSource.volume = Mathf.Clamp01(value);
            }
        }

        public float Pitch
        {
            get => _audioSource?.pitch ?? 1f;
            set
            {
                if (_audioSource != null)
                    _audioSource.pitch = Mathf.Clamp(value, 0.5f, 2f);
            }
        }

        public bool IsMuted
        {
            get => _audioSource?.mute ?? false;
            set
            {
                if (_audioSource != null)
                    _audioSource.mute = value;
            }
        }

        public bool Loop
        {
            get => _audioSource?.loop ?? false;
            set
            {
                if (_audioSource != null)
                    _audioSource.loop = value;
            }
        }

        /// <summary>FFT spectrum data for visualization</summary>
        public float[] SpectrumData => _spectrumData;
        #endregion

        #region Settings
        [Header("Audio Settings")]
        [SerializeField] private bool _spatialize = true;
        [SerializeField] private float _spatialBlend = 0.5f; // 0 = 2D, 1 = 3D

        [Header("Spectrum Analysis")]
        [SerializeField] private int _spectrumSamples = 256;
        [SerializeField] private FFTWindow _fftWindow = FFTWindow.Blackman;
        #endregion

        #region Private Fields
        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private string _currentPath;
        private Coroutine _loadCoroutine;
        private Coroutine _updateCoroutine;
        private float[] _spectrumData;
        private float _lastUpdateTime;
        private const float TIME_UPDATE_INTERVAL = 0.1f;
        #endregion

        #region Initialization
        private void Awake()
        {
            CreateAudioSource();
            _spectrumData = new float[_spectrumSamples];
        }

        private void CreateAudioSource()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.volume = 1f;
            _audioSource.pitch = 1f;
            _audioSource.spatialBlend = _spatialBlend;
            _audioSource.spatialize = _spatialize;

            // For VR, set to high priority
            _audioSource.priority = 0;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Prepare audio from file path
        /// </summary>
        public void PrepareFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                OnError?.Invoke("Invalid audio path");
                return;
            }

            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
            }

            _currentPath = path;
            State = PlaybackState.Preparing;
            _loadCoroutine = StartCoroutine(LoadAudioCoroutine(path));
        }

        /// <summary>
        /// Play audio
        /// </summary>
        public void Play()
        {
            if (!IsReady && State != PlaybackState.Paused)
            {
                Debug.LogWarning("[AudioPlaybackEngine] Not ready to play");
                return;
            }

            if (_audioSource != null && _audioClip != null)
            {
                _audioSource.Play();
                State = PlaybackState.Playing;
                StartTimeUpdates();
                OnPlaybackStarted?.Invoke();
            }
        }

        /// <summary>
        /// Pause playback
        /// </summary>
        public void Pause()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Pause();
                State = PlaybackState.Paused;
                StopTimeUpdates();
                OnPlaybackPaused?.Invoke();
            }
        }

        /// <summary>
        /// Resume from pause
        /// </summary>
        public void Resume()
        {
            if (State == PlaybackState.Paused)
            {
                _audioSource.UnPause();
                State = PlaybackState.Playing;
                StartTimeUpdates();
                OnPlaybackStarted?.Invoke();
            }
        }

        /// <summary>
        /// Toggle play/pause
        /// </summary>
        public void TogglePlayPause()
        {
            if (IsPlaying)
                Pause();
            else if (State == PlaybackState.Paused)
                Resume();
            else
                Play();
        }

        /// <summary>
        /// Stop playback
        /// </summary>
        public void Stop()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.time = 0;
            }

            State = PlaybackState.Stopped;
            StopTimeUpdates();
            OnPlaybackStopped?.Invoke();
        }

        /// <summary>
        /// Seek to time in seconds
        /// </summary>
        public void Seek(float time)
        {
            if (_audioSource != null && _audioClip != null)
            {
                _audioSource.time = Mathf.Clamp(time, 0, _audioClip.length);
                OnTimeUpdate?.Invoke(_audioSource.time);
            }
        }

        /// <summary>
        /// Seek by delta time
        /// </summary>
        public void SeekRelative(float deltaSeconds)
        {
            if (_audioSource != null)
            {
                Seek(_audioSource.time + deltaSeconds);
            }
        }

        /// <summary>
        /// Skip forward 10 seconds
        /// </summary>
        public void SkipForward()
        {
            SeekRelative(10f);
        }

        /// <summary>
        /// Skip backward 10 seconds
        /// </summary>
        public void SkipBackward()
        {
            SeekRelative(-10f);
        }

        /// <summary>
        /// Get spectrum data for visualization (already populated in SpectrumData property)
        /// </summary>
        public void UpdateSpectrumData()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.GetSpectrumData(_spectrumData, 0, _fftWindow);
            }
            else
            {
                // Clear spectrum when not playing
                Array.Clear(_spectrumData, 0, _spectrumData.Length);
            }
        }

        /// <summary>
        /// Get frequency band averages for visualization
        /// </summary>
        public void GetFrequencyBands(out float bass, out float mid, out float treble)
        {
            UpdateSpectrumData();

            if (!IsPlaying)
            {
                bass = mid = treble = 0;
                return;
            }

            // Split spectrum into bands
            // Bass: 0-250Hz (indices 0-7 approximately)
            // Mid: 250-4000Hz (indices 8-127 approximately)  
            // Treble: 4000Hz+ (indices 128-255)

            bass = GetAverageRange(0, 8);
            mid = GetAverageRange(8, 128);
            treble = GetAverageRange(128, _spectrumSamples);
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Stop();

            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            if (_audioClip != null)
            {
                Destroy(_audioClip);
                _audioClip = null;
            }
        }
        #endregion

        #region Private Methods
        private IEnumerator LoadAudioCoroutine(string path)
        {
            // Determine audio type from extension
            AudioType audioType = GetAudioType(path);

            string fileUrl = "file:///" + path.Replace("\\", "/");

            using (var request = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(fileUrl, audioType))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    State = PlaybackState.Idle;
                    OnError?.Invoke($"Failed to load audio: {request.error}");
                    yield break;
                }

                // Dispose previous clip
                if (_audioClip != null)
                {
                    Destroy(_audioClip);
                }

                _audioClip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(request);

                if (_audioClip == null)
                {
                    State = PlaybackState.Idle;
                    OnError?.Invoke("Failed to decode audio");
                    yield break;
                }

                _audioSource.clip = _audioClip;
                State = PlaybackState.Ready;

                Debug.Log($"[AudioPlaybackEngine] Loaded: {path} ({_audioClip.length:F1}s, {_audioClip.channels}ch, {_audioClip.frequency}Hz)");

                OnPrepareCompleted?.Invoke();
            }
        }

        private AudioType GetAudioType(string path)
        {
            string ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".mp3" => AudioType.MPEG,
                ".ogg" or ".oga" => AudioType.OGGVORBIS,
                ".wav" => AudioType.WAV,
                ".aiff" or ".aif" => AudioType.AIFF,
                _ => AudioType.UNKNOWN
            };
        }

        private void StartTimeUpdates()
        {
            StopTimeUpdates();
            _updateCoroutine = StartCoroutine(TimeUpdateCoroutine());
        }

        private void StopTimeUpdates()
        {
            if (_updateCoroutine != null)
            {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }
        }

        private IEnumerator TimeUpdateCoroutine()
        {
            while (State == PlaybackState.Playing)
            {
                if (_audioSource != null)
                {
                    OnTimeUpdate?.Invoke(_audioSource.time);

                    // Check if playback ended
                    if (!_audioSource.isPlaying && _audioSource.time >= _audioClip.length - 0.1f)
                    {
                        State = PlaybackState.Stopped;
                        OnPlaybackEnded?.Invoke();
                        yield break;
                    }
                }

                yield return new WaitForSeconds(TIME_UPDATE_INTERVAL);
            }
        }

        private float GetAverageRange(int startIndex, int endIndex)
        {
            if (_spectrumData == null) return 0;

            float sum = 0;
            int count = 0;

            for (int i = startIndex; i < endIndex && i < _spectrumData.Length; i++)
            {
                sum += _spectrumData[i];
                count++;
            }

            return count > 0 ? sum / count : 0;
        }
        #endregion

        #region Unity Lifecycle
        private void Update()
        {
            // Update spectrum data every frame for smooth visualization
            if (IsPlaying)
            {
                UpdateSpectrumData();
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }
        #endregion
    }

}
