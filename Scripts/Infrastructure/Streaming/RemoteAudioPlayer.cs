using Unity.WebRTC;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Plays remote audio received via WebRTC AudioStreamTrack.
    /// Attach to a GameObject with an AudioSource component.
    /// Subscribe to PhaseProtocolClient.OnAudioTrackReceived to set the track.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RemoteAudioPlayer : MonoBehaviour
    {
        private AudioSource _audioSource;
        private AudioStreamTrack _currentTrack;

        void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.loop = false;
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D audio (non-spatial, full volume both ears)

            // Reduce Unity audio DSP buffer for lower latency.
            // Default is 1024 samples (~21ms). 256 samples = ~5ms per buffer.
            var audioConfig = AudioSettings.GetConfiguration();
            audioConfig.dspBufferSize = 256;
            AudioSettings.Reset(audioConfig);
            Debug.Log($"[RemoteAudioPlayer] DSP buffer set to 256 for low latency");
        }

        /// <summary>
        /// Set the remote audio track for playback.
        /// Call from OnAudioTrackReceived event handler.
        /// </summary>
        public void SetTrack(AudioStreamTrack track)
        {
            if (track == null)
            {
                StopAudio();
                return;
            }

            _currentTrack = track;

            // Unity.WebRTC 3.x: AudioStreamTrack received via OnTrack
            // is automatically decoded. We need to set it as output to our AudioSource.
            _audioSource.SetTrack(track);
            _audioSource.loop = false;
            _audioSource.Play();

            Debug.Log("[RemoteAudioPlayer] Audio track set and playing");
        }

        /// <summary>
        /// Stop audio playback and clear the track.
        /// </summary>
        public void StopAudio()
        {
            _audioSource.Stop();
            _audioSource.clip = null;
            _currentTrack = null;
            Debug.Log("[RemoteAudioPlayer] Audio stopped");
        }

        /// <summary>
        /// Set volume (0.0 to 1.0).
        /// </summary>
        public void SetVolume(float volume)
        {
            _audioSource.volume = Mathf.Clamp01(volume);
        }

        /// <summary>
        /// Mute/unmute audio.
        /// </summary>
        public void SetMute(bool muted)
        {
            _audioSource.mute = muted;
        }

        public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;
        public bool HasTrack => _currentTrack != null;

        void OnDestroy()
        {
            StopAudio();
        }
    }
}
