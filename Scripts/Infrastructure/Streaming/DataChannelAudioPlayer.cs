using System;
using Concentus;
using Concentus.Enums;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Low-latency audio player that receives PCM16 frames via the audio DataChannel,
    /// plays them through a procedural clip + ring buffer with OnAudioFilterRead.
    /// Bypasses WebRTC's NetEQ jitter buffer (~250ms) for ~20-40ms total latency.
    ///
    /// The primary sink for host audio on Unity: host (SIPSorceryStreamer.Audio.cs) sends
    /// raw PCM16 over the audio DataChannel, this player writes it into the ring buffer,
    /// and the Unity audio thread reads it back via OnAudioFilterRead.
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

        // Guards the _ring array + _writePos / _readPos. Volatile alone is not enough because
        // the audio thread can read a partially-updated array slot while the producer is mid-write,
        // producing torn samples (audible as "rè" crackling). Lock briefly on both ends.
        private readonly object _ringLock = new object();

        // Opus decoder (Concentus pure C#) — kept for legacy callers of OnOpusFrame; not used
        // by the normal DC PCM path. Allocated lazily to avoid the per-instance setup cost if
        // the host only ever sends PCM.
        private IOpusDecoder _decoder;
        private short[] _decodePcm;

        private AudioSource _audioSource;
        private bool _started;
        private bool _loggedDspInfo;
        private int _cachedOutputRate;
        private static bool _dspLockedAt48k; // only lock once per process

        void Awake()
        {
            _ring = new float[RING_CAPACITY];

            _audioSource = GetComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;        // 2D — disable spatialization
            _audioSource.playOnAwake = false;
            // Bypass every Unity internal effect on this source. Native audioTrack on Android
            // goes straight to the hardware DAC; matching that path means letting Unity copy
            // our procedural-clip samples to the output without any mixer/HRTF/reverb stage.
            _audioSource.bypassEffects = true;
            _audioSource.bypassListenerEffects = true;
            _audioSource.bypassReverbZones = true;
            _audioSource.dopplerLevel = 0f;
            _audioSource.reverbZoneMix = 0f;

            // Force the audio DSP to 48 kHz so Unity does not internally resample between
            // our 48 kHz source and whatever the device's native rate happens to be. Without
            // this, on a 44.1 kHz device the system resampler can introduce subtle pitch and
            // noise artifacts on top of the DC audio. We lock once globally; other audio
            // sources in the app are resampled by Unity's high-quality path, which is fine
            // for non-realtime content (UI sounds, music player).
            if (!_dspLockedAt48k)
            {
                try
                {
                    var cfg = AudioSettings.GetConfiguration();
                    if (cfg.sampleRate != SAMPLE_RATE)
                    {
                        cfg.sampleRate = SAMPLE_RATE;
                        // dspBufferSize defaults are platform-specific; only override if missing
                        if (cfg.dspBufferSize <= 0) cfg.dspBufferSize = 1024;
                        cfg.speakerMode = AudioSpeakerMode.Stereo;
                        if (AudioSettings.Reset(cfg))
                        {
                            AppLog.Log($"[DataChannelAudioPlayer] DSP locked to {SAMPLE_RATE} Hz, dspBufferSize={cfg.dspBufferSize}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.LogWarning($"[DataChannelAudioPlayer] AudioSettings.Reset failed (non-fatal): {ex.Message}");
                }
                _dspLockedAt48k = true;
            }
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
        /// Kept for legacy compatibility; the normal DC path now uses OnPCMFrame because the
        /// host always sends raw PCM16 (SIPSorceryStreamer.Audio.cs). Decoder is allocated
        /// lazily so we don't pay setup cost when only PCM is in use.
        /// </summary>
        /// <param name="opusData">Buffer containing Opus encoded data</param>
        /// <param name="offset">Start offset in the buffer</param>
        /// <param name="length">Length of Opus data</param>
        public void OnOpusFrame(byte[] opusData, int offset, int length)
        {
            if (length <= 0) return;

            // Lazy decoder init
            if (_decoder == null)
            {
                try { _decoder = OpusCodecFactory.CreateDecoder(SAMPLE_RATE, CHANNELS); }
                catch (Exception ex) { AppLog.LogWarning($"[DataChannelAudioPlayer] Opus decoder init failed: {ex.Message}"); return; }
                _decodePcm = new short[FRAME_SAMPLES * CHANNELS];
            }

            try
            {
                int decoded = _decoder.Decode(
                    opusData.AsSpan(offset, length),
                    _decodePcm.AsSpan(),
                    FRAME_SAMPLES);

                if (decoded <= 0) return;

                int samplesToWrite = decoded * CHANNELS;

                // Same lock discipline as OnPCMFrame so the audio thread can't tear samples.
                lock (_ringLock)
                {
                    int wp = _writePos;
                    for (int i = 0; i < samplesToWrite; i++)
                    {
                        _ring[(wp + i) % RING_CAPACITY] = _decodePcm[i] / 32768f;
                    }
                    _writePos = (wp + samplesToWrite) % RING_CAPACITY;
                }
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[DataChannelAudioPlayer] Decode error: {ex.Message}");
            }
        }

        /// <summary>
        /// Unity audio thread callback. DSP is locked at 48 kHz and the producer delivers
        /// 48 kHz PCM, so we read ring-buffer samples directly at 1:1 with no resampling or
        /// interpolation. The path is intentionally bare:
        ///   - no DC blocker (host PCM shouldn't carry significant offset; the previous IIR ate bass)
        ///   - no adaptive rate (any deviation = audible pitch wobble)
        ///   - no linear interpolation at fractional read positions (bandlimits highs)
        ///   - only the lock for race safety and a zero-fill on underrun
        /// This matches the Android native path (AudioTrack.write of raw PCM16) closely.
        /// </summary>
        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_loggedDspInfo)
            {
                _loggedDspInfo = true;
                AppLog.Log($"[DataChannelAudioPlayer] OnAudioFilterRead: data.Length={data.Length}, channels={channels}, outputRate={_cachedOutputRate}");
            }

            int ch = channels; // expected 2 (stereo)
            int framesNeeded = data.Length / ch;
            int samplesNeeded = framesNeeded * ch;

            // Snapshot ring buffer state under lock so the producer thread can't tear samples
            // mid-read. Emergency skip keeps us from drifting past 3× target.
            int available;
            int rp;
            lock (_ringLock)
            {
                available = _writePos - _readPos;
                if (available < 0) available += RING_CAPACITY;
                rp = _readPos;

                if (available > EMERGENCY_SKIP_SAMPLES)
                {
                    int skip = available - TARGET_LATENCY_SAMPLES;
                    skip = (skip / ch) * ch;
                    rp = (rp + skip) % RING_CAPACITY;
                    available -= skip;
                }
            }

            int samplesAvailable = available;
            int copySamples = Math.Min(samplesNeeded, samplesAvailable);

            // Direct 1:1 copy under the same lock so the producer can't tear samples. For a
            // healthy stream samplesAvailable >= samplesNeeded and the entire output buffer is
            // filled with PCM data — no interpolation, no resampling, no DSP.
            lock (_ringLock)
            {
                if (copySamples > 0)
                {
                    int firstChunk = Math.Min(copySamples, RING_CAPACITY - rp);
                    Array.Copy(_ring, rp, data, 0, firstChunk);
                    if (firstChunk < copySamples)
                    {
                        Array.Copy(_ring, 0, data, firstChunk, copySamples - firstChunk);
                    }
                }

                // Zero the rest if we underran this block.
                for (int i = copySamples; i < samplesNeeded; i++) data[i] = 0f;

                _readPos = (rp + copySamples) % RING_CAPACITY;
            }
        }

        private void SoftMuteUp()
        {
            // Quickly recover to full gain when we have valid data so the listener doesn't
            // perceive a quiet period after a short underrun.
            // Reserved for future click-reduction work; the current audio thread is direct
            // copy + zero-fill, so this method is unused. Kept so any external call site
            // (or future underrun envelope) keeps compiling.
        }

        private void SoftMuteDown()
        {
            // Reserved (see SoftMuteUp note above).
        }

        private static void ApplyGain(float[] data, float gain)
        {
            // Reserved helper for future click-reduction work; currently unused.
            if (gain <= 0f) { Array.Clear(data, 0, data.Length); return; }
            if (gain >= 1f) return;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }

        private int AvailableSamples()
        {
            int w = _writePos;
            int r = _readPos;
            int avail = w - r;
            if (avail < 0) avail += RING_CAPACITY;
            return avail;
        }

        /// <summary>
        /// Feed raw PCM16 audio data directly (bypasses Opus decode).
        /// Used by the host audio DataChannel and by media relay fallback, both of which
        /// send uncompressed 16-bit signed little-endian PCM.
        /// Called from the WebSocket / DataChannel receive thread.
        /// </summary>
        /// <param name="pcmData">Buffer containing PCM16 samples (little-endian, interleaved stereo)</param>
        /// <param name="offset">Start offset in the buffer</param>
        /// <param name="length">Length of PCM data in bytes (must be even)</param>
        public void OnPCMFrame(byte[] pcmData, int offset, int length)
        {
            if (pcmData == null || length <= 0) return;

            // Each PCM16 sample = 2 bytes
            int sampleCount = length / 2;
            if (sampleCount <= 0) return;

            // Hold the ring lock across the entire write loop + position publish so the audio
            // thread cannot observe a partially-written frame. Volatile on the position alone
            // is insufficient because the audio thread reads array slots inside the loop.
            lock (_ringLock)
            {
                int wp = _writePos;
                for (int i = 0; i < sampleCount; i++)
                {
                    int byteIdx = offset + i * 2;
                    // Little-endian 16-bit signed → float
                    short sample = (short)(pcmData[byteIdx] | (pcmData[byteIdx + 1] << 8));
                    _ring[(wp + i) % RING_CAPACITY] = sample / 32768f;
                }
                _writePos = (wp + sampleCount) % RING_CAPACITY;
            }
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
            lock (_ringLock)
            {
                _writePos = 0;
                _readPos = 0;
            }
            AppLog.Log("[DataChannelAudioPlayer] Stopped");
        }

        void OnDestroy()
        {
            StopAudio();
            _decoder = null;
        }
    }
}
