using System;
using Concentus;
using Concentus.Enums;
using UnityEngine;
using VRWorkspace.Core;
using VRWorkspace.Native;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Low-latency audio player that receives PCM16 frames via the audio DataChannel
    /// and forwards them to a native AAudio sink inside the HevcDecoder plugin.
    ///
    /// On Android the native sink owns the output stream and Unity's audio mixer is
    /// bypassed entirely, eliminating the OnAudioFilterRead ring buffer + DSP resampler
    /// path that previously made Remote Desktop audio sound thin compared to RemotePlay.
    ///
    /// In the Editor (or on any non-Android target) we fall back to a small in-memory
    /// ring + OnAudioFilterRead so play mode still produces sound for testing.
    ///
    /// The host (SIPSorceryStreamer.Audio.cs) sends raw PCM16 over the audio DataChannel.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DataChannelAudioPlayer : MonoBehaviour
    {
        private const int SAMPLE_RATE = 48000;
        private const int CHANNELS = 2;
        private const int FRAME_SAMPLES = 480; // 10ms at 48kHz

#if !UNITY_ANDROID || UNITY_EDITOR
        // Editor fallback ring (~200ms capacity at 48kHz stereo).
        private const int RING_CAPACITY = 9600 * CHANNELS;
        private const int TARGET_LATENCY_SAMPLES = 1920 * CHANNELS;
        private const int EMERGENCY_SKIP_SAMPLES = TARGET_LATENCY_SAMPLES * 3;
        private float[] _ring;
        private volatile int _writePos;
        private volatile int _readPos;
        private readonly object _ringLock = new object();
#endif

        // Opus decoder (Concentus pure C#) — kept for legacy callers of OnOpusFrame; not used
        // by the normal DC PCM path. Allocated lazily.
        private IOpusDecoder _decoder;
        private short[] _decodePcm;

        // Native AAudio sink (Android only). Created on StartPlayback, disposed on Stop.
        private NativeAudioSink _nativeSink;
        private bool _started;

#if !UNITY_ANDROID || UNITY_EDITOR
        private AudioSource _audioSource;
        private bool _loggedDspInfo;
#endif

        void Awake()
        {
#if !UNITY_ANDROID || UNITY_EDITOR
            _ring = new float[RING_CAPACITY];
            _audioSource = GetComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;
            _audioSource.playOnAwake = false;
            _audioSource.bypassEffects = true;
            _audioSource.bypassListenerEffects = true;
            _audioSource.bypassReverbZones = true;
            _audioSource.dopplerLevel = 0f;
            _audioSource.reverbZoneMix = 0f;
#endif
        }

        /// <summary>
        /// Start the audio output. On Android this opens the native AAudio sink.
        /// In the editor it boots the procedural clip that drives OnAudioFilterRead.
        /// </summary>
        public void StartPlayback()
        {
            if (_started) return;
            _started = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            AppLog.Log("[DataChannelAudioPlayer] StartPlayback: opening native AAudio sink");
            try
            {
                _nativeSink = new NativeAudioSink(SAMPLE_RATE, CHANNELS);
                if (!_nativeSink.Open())
                {
                    AppLog.LogWarning("[DataChannelAudioPlayer] Native sink unavailable; audio disabled");
                    _nativeSink?.Dispose();
                    _nativeSink = null;
                }
                else
                {
                    AppLog.Log("[DataChannelAudioPlayer] Native AAudio sink started");
                }
            }
            catch (Exception ex)
            {
                AppLog.LogError($"[DataChannelAudioPlayer] Native sink open threw: {ex}");
                _nativeSink?.Dispose();
                _nativeSink = null;
            }
#else
            var clip = AudioClip.Create("dc_audio", SAMPLE_RATE, CHANNELS, SAMPLE_RATE, true,
                (float[] data) => { Array.Clear(data, 0, data.Length); });
            _audioSource.clip = clip;
            _audioSource.loop = true;
            _audioSource.Play();
            AppLog.Log("[DataChannelAudioPlayer] Editor playback started");
#endif
        }

        /// <summary>
        /// Feed a raw Opus frame for decoding and playback.
        /// Kept for legacy compatibility; the normal DC path uses OnPCMFrame.
        /// </summary>
        public void OnOpusFrame(byte[] opusData, int offset, int length)
        {
            if (length <= 0) return;

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

                int bytesToWrite = decoded * CHANNELS * sizeof(short);
                WritePcmBytes(_decodePcm, decoded * CHANNELS, bytesToWrite);
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[DataChannelAudioPlayer] Decode error: {ex.Message}");
            }
        }

        /// <summary>
        /// Unity audio thread callback (Editor / non-Android fallback only).
        /// On Android this is never invoked because the native sink owns the output device.
        /// </summary>
#if !UNITY_ANDROID || UNITY_EDITOR
        void OnAudioFilterRead(float[] data, int channels)
        {
            int ch = channels;
            int framesNeeded = data.Length / ch;
            int samplesNeeded = framesNeeded * ch;

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
                for (int i = copySamples; i < samplesNeeded; i++) data[i] = 0f;
                _readPos = (rp + copySamples) % RING_CAPACITY;
            }
        }
#endif

        /// <summary>
        /// Feed raw PCM16 audio data (little-endian, interleaved stereo).
        /// Called from the WebSocket / DataChannel receive thread.
        /// </summary>
        public void OnPCMFrame(byte[] pcmData, int offset, int length)
        {
            if (pcmData == null || length <= 0) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Native fast path: hand the bytes straight to AAudio.
            if (_nativeSink != null)
            {
                _nativeSink.PushPcm(pcmData, offset, length);
            }
#else
            int sampleCount = length / 2;
            if (sampleCount <= 0) return;
            lock (_ringLock)
            {
                int wp = _writePos;
                for (int i = 0; i < sampleCount; i++)
                {
                    int byteIdx = offset + i * 2;
                    short sample = (short)(pcmData[byteIdx] | (pcmData[byteIdx + 1] << 8));
                    _ring[(wp + i) % RING_CAPACITY] = sample / 32768f;
                }
                _writePos = (wp + sampleCount) % RING_CAPACITY;
            }
#endif
        }

        /// <summary>
        /// Used by the Opus path after decoding into _decodePcm. Converts short[] to
        /// little-endian bytes and dispatches to either the native sink or the editor ring.
        /// </summary>
        private void WritePcmBytes(short[] samples, int sampleCount, int totalBytes)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_nativeSink == null) return;
            // Re-pack to little-endian into a small reusable buffer. The decoder output is
            // already interleaved stereo so we can copy byte-by-byte.
            if (_decodeBuffer == null || _decodeBuffer.Length < totalBytes)
                _decodeBuffer = new byte[Math.Max(totalBytes, 4096)];
            for (int i = 0; i < sampleCount; i++)
            {
                int v = samples[i];
                _decodeBuffer[i * 2]     = (byte)(v & 0xFF);
                _decodeBuffer[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            _nativeSink.PushPcm(_decodeBuffer, 0, totalBytes);
#else
            lock (_ringLock)
            {
                int wp = _writePos;
                for (int i = 0; i < sampleCount; i++)
                {
                    _ring[(wp + i) % RING_CAPACITY] = samples[i] / 32768f;
                }
                _writePos = (wp + sampleCount) % RING_CAPACITY;
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private byte[] _decodeBuffer;
#endif

        public void SetVolume(float volume)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_nativeSink != null) _nativeSink.SetVolume(Mathf.Clamp01(volume));
#else
            if (_audioSource != null) _audioSource.volume = Mathf.Clamp01(volume);
#endif
        }

        public void SetMute(bool muted)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_nativeSink != null) _nativeSink.SetMute(muted);
#else
            if (_audioSource != null) _audioSource.mute = muted;
#endif
        }

        public bool IsPlaying => _started
#if UNITY_ANDROID && !UNITY_EDITOR
            && _nativeSink != null && _nativeSink.IsOpen;
#else
            && _audioSource != null && _audioSource.isPlaying;
#endif

        public void StopAudio()
        {
            _started = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_nativeSink != null)
            {
                _nativeSink.Close();
                _nativeSink.Dispose();
                _nativeSink = null;
            }
#else
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
#endif
            _decoder = null;
            AppLog.Log("[DataChannelAudioPlayer] Stopped");
        }

        void OnDestroy()
        {
            StopAudio();
        }
    }
}
