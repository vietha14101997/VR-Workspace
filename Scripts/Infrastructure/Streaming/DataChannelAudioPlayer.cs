using System;
using Concentus;
using Concentus.Enums;
using UnityEngine;
using VRWorkspace.Core;

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

        // Ring buffer: ~200ms capacity at 48kHz stereo
        // Must absorb TCP batch bursts (20ms) + WiFi HOL delay without overflow
        private const int RING_CAPACITY = 9600 * CHANNELS;

        // Latency target for adaptive drift correction (~40ms)
        // Smooth resampling keeps buffer near this level without audible artifacts.
        // 40ms buffer + ~60ms Android audio + ~25ms network ≈ 125ms total.
        private const int TARGET_LATENCY_SAMPLES = 1920 * CHANNELS; // 40ms at 48kHz stereo

        // Emergency skip threshold: only hard-skip when buffer exceeds 3x target (120ms)
        private const int EMERGENCY_SKIP_SAMPLES = TARGET_LATENCY_SAMPLES * 3;
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
            AppLog.Log($"[DataChannelAudioPlayer] DSP before: rate={audioConfig.sampleRate}, buf={audioConfig.dspBufferSize}, speakers={audioConfig.speakerMode}");
            audioConfig.dspBufferSize = 256;
            audioConfig.sampleRate = SAMPLE_RATE; // Must match Opus decoder (48kHz)
            AudioSettings.Reset(audioConfig);
            var newConfig = AudioSettings.GetConfiguration();
            AppLog.Log($"[DataChannelAudioPlayer] DSP after: rate={newConfig.sampleRate}, buf={newConfig.dspBufferSize}, speakers={newConfig.speakerMode}");
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

            AppLog.Log("[DataChannelAudioPlayer] Playback started");
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

                // No latency control here — only audio thread modifies _readPos.
                // This prevents race condition between main thread and audio thread
                // that caused crackling/distortion artifacts.

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
                AppLog.LogWarning($"[DataChannelAudioPlayer] Decode error: {ex.Message}");
            }
        }

        /// <summary>
        /// Unity audio thread callback. Uses adaptive resampling to keep buffer
        /// near TARGET_LATENCY without audible artifacts.
        /// When buffer grows: speed up playback slightly (consume faster).
        /// When buffer shrinks: slow down slightly (conserve data).
        /// Only hard-skips in emergency (>3x target).
        /// </summary>
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_loggedDspInfo)
            {
                _loggedDspInfo = true;
                AppLog.Log($"[DataChannelAudioPlayer] OnAudioFilterRead: data.Length={data.Length}, channels={channels}, outputRate={_cachedOutputRate}");
            }

            int available = AvailableSamples();

            if (available <= 0)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            int rp = _readPos;
            int ch = channels; // expected 2 (stereo)

            // Emergency skip: only when way too far behind (>120ms)
            if (available > EMERGENCY_SKIP_SAMPLES)
            {
                int skip = available - TARGET_LATENCY_SAMPLES;
                // Align skip to frame boundary (ch samples = 1 stereo frame)
                skip = (skip / ch) * ch;
                rp = (rp + skip) % RING_CAPACITY;
                available -= skip;
            }

            // Adaptive playback rate based on buffer level
            float bufferRatio = (float)available / TARGET_LATENCY_SAMPLES;
            float playbackRate;

            if (bufferRatio > 1.5f)
                playbackRate = 1.08f;      // well above target: 8% faster
            else if (bufferRatio > 1.15f)
                playbackRate = 1.03f;      // slightly above: 3% faster
            else if (bufferRatio < 0.4f)
                playbackRate = 0.95f;      // running low: 5% slower
            else if (bufferRatio < 0.7f)
                playbackRate = 0.98f;      // slightly low: 2% slower
            else
                playbackRate = 1.0f;       // near target: normal speed

            // Read with linear interpolation, advancing by stereo frames
            int framesNeeded = data.Length / ch;
            int framesAvailable = available / ch;
            float readFrame = 0f;

            for (int f = 0; f < framesNeeded; f++)
            {
                int idx = (int)readFrame;

                if (idx + 1 >= framesAvailable)
                {
                    // Underrun: fill rest with silence
                    for (int i = f * ch; i < data.Length; i++)
                        data[i] = 0f;
                    readFrame = idx;
                    break;
                }

                float frac = readFrame - idx;
                for (int c = 0; c < ch; c++)
                {
                    int pos0 = (rp + idx * ch + c) % RING_CAPACITY;
                    int pos1 = (rp + (idx + 1) * ch + c) % RING_CAPACITY;
                    data[f * ch + c] = _ring[pos0] + (_ring[pos1] - _ring[pos0]) * frac;
                }

                readFrame += playbackRate;
            }

            int framesConsumed = (int)readFrame;
            _readPos = (rp + framesConsumed * ch) % RING_CAPACITY;
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
            AppLog.Log("[DataChannelAudioPlayer] Stopped");
        }

        void OnDestroy()
        {
            StopAudio();
            _decoder = null;
        }
    }
}
