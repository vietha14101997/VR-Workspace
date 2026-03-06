using System;
using Concentus;
using Concentus.Enums;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Low-latency audio player that receives Opus frames via DataChannel,
    /// decodes with Concentus, and plays via OnAudioFilterRead with a minimal ring buffer.
    /// Bypasses WebRTC's NetEQ jitter buffer (~250ms) for ~20-40ms total latency.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DataChannelAudioPlayer : MonoBehaviour
    {
        private const int SAMPLE_RATE = 48000;
        private const int CHANNELS = 2;
        private const int FRAME_SAMPLES = 480; // 10ms at 48kHz

        // Ring buffer: ~85ms capacity at 48kHz stereo (enough for 8 Opus frames + margin)
        private const int RING_CAPACITY = 4096 * CHANNELS;
        private float[] _ring;
        private volatile int _writePos;
        private volatile int _readPos;

        // Opus decoder (Concentus pure C#)
        private IOpusDecoder _decoder;
        private short[] _decodePcm;

        private AudioSource _audioSource;
        private bool _started;
        private bool _loggedDspInfo;
        private int _cachedOutputRate;

        void Awake()
        {
            _ring = new float[RING_CAPACITY];
            _decoder = OpusCodecFactory.CreateDecoder(SAMPLE_RATE, CHANNELS);
            _decodePcm = new short[FRAME_SAMPLES * CHANNELS];

            _audioSource = GetComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;
            _audioSource.playOnAwake = false;

            // Force 48kHz DSP to match Opus decoder + low-latency buffer
            var audioConfig = AudioSettings.GetConfiguration();
            Debug.Log($"[DataChannelAudioPlayer] DSP before: rate={audioConfig.sampleRate}, buf={audioConfig.dspBufferSize}, speakers={audioConfig.speakerMode}");
            audioConfig.dspBufferSize = 256;
            audioConfig.sampleRate = SAMPLE_RATE; // Must match Opus decoder (48kHz)
            AudioSettings.Reset(audioConfig);
            var newConfig = AudioSettings.GetConfiguration();
            Debug.Log($"[DataChannelAudioPlayer] DSP after: rate={newConfig.sampleRate}, buf={newConfig.dspBufferSize}, speakers={newConfig.speakerMode}");
            _cachedOutputRate = AudioSettings.outputSampleRate; // Cache for audio thread
        }

        /// <summary>
        /// Start the audio output. Creates a procedural silent clip to keep OnAudioFilterRead active.
        /// </summary>
        public void StartPlayback()
        {
            if (_started) return;
            _started = true;

            // Procedural clip keeps OnAudioFilterRead callback alive
            var clip = AudioClip.Create("dc_audio", SAMPLE_RATE, CHANNELS, SAMPLE_RATE, true,
                (float[] data) => { Array.Clear(data, 0, data.Length); });
            _audioSource.clip = clip;
            _audioSource.loop = true;
            _audioSource.Play();

            Debug.Log("[DataChannelAudioPlayer] Playback started");
        }

        /// <summary>
        /// Feed a raw Opus frame for decoding and playback.
        /// Called from main thread (DataChannel OnMessage handler).
        /// </summary>
        /// <param name="opusData">Buffer containing Opus encoded data</param>
        /// <param name="offset">Start offset in the buffer</param>
        /// <param name="length">Length of Opus data</param>
        public void OnOpusFrame(byte[] opusData, int offset, int length)
        {
            if (_decoder == null || length <= 0) return;

            try
            {
                int decoded = _decoder.Decode(
                    opusData.AsSpan(offset, length),
                    _decodePcm.AsSpan(),
                    FRAME_SAMPLES);

                if (decoded <= 0) return;

                int samplesToWrite = decoded * CHANNELS;

                // Convert short → float and write to ring buffer
                int wp = _writePos;
                for (int i = 0; i < samplesToWrite; i++)
                {
                    _ring[(wp + i) % RING_CAPACITY] = _decodePcm[i] / 32768f;
                }
                // Atomic update after all samples written (volatile write)
                _writePos = (wp + samplesToWrite) % RING_CAPACITY;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DataChannelAudioPlayer] Decode error: {ex.Message}");
            }
        }

        /// <summary>
        /// Unity audio thread callback. Reads decoded samples from ring buffer.
        /// Underrun → silence (zeros). Overrun handled by dropping oldest samples.
        /// </summary>
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_loggedDspInfo)
            {
                _loggedDspInfo = true;
                Debug.Log($"[DataChannelAudioPlayer] OnAudioFilterRead: data.Length={data.Length}, channels={channels}, outputRate={_cachedOutputRate}");
            }

            int available = AvailableSamples();
            int needed = data.Length;

            if (available <= 0)
            {
                // Underrun: output silence
                Array.Clear(data, 0, data.Length);
                return;
            }

            int toRead = Math.Min(available, needed);
            int rp = _readPos;

            for (int i = 0; i < toRead; i++)
            {
                data[i] = _ring[(rp + i) % RING_CAPACITY];
            }
            // Clear remainder if not enough samples
            if (toRead < needed)
            {
                Array.Clear(data, toRead, needed - toRead);
            }

            // Atomic update after all samples read (volatile write)
            _readPos = (rp + toRead) % RING_CAPACITY;
        }

        private int AvailableSamples()
        {
            int w = _writePos;
            int r = _readPos;
            int avail = w - r;
            if (avail < 0) avail += RING_CAPACITY;
            return avail;
        }

        public void SetVolume(float volume) => _audioSource.volume = Mathf.Clamp01(volume);
        public void SetMute(bool muted) => _audioSource.mute = muted;
        public bool IsPlaying => _started && _audioSource != null && _audioSource.isPlaying;

        public void StopAudio()
        {
            _started = false;
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.clip = null;
            }
            _writePos = 0;
            _readPos = 0;
            Debug.Log("[DataChannelAudioPlayer] Stopped");
        }

        void OnDestroy()
        {
            StopAudio();
            _decoder = null;
        }
    }
}
