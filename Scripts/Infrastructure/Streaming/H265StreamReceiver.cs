using System;
using System.Collections;
using UnityEngine;
using VRWorkspace.Native;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// H265 custom decode pipeline coordinator.
    ///
    /// Bridges unity-webrtc encoded H265 frames → HevcDecoderPlugin (MediaCodec NDK) → Unity RenderTexture.
    ///
    /// Architecture:
    ///   WebRTC RTCRtpReceiver.Transform
    ///     └─► OnEncodedFrameReceived()
    ///              └─► HevcDecoderPlugin.DecodeNal()
    ///                       └─► Update() polls decoded frames
    ///                                └─► Blit NV12→RGBA via shader
    ///                                         └─► OnTextureReady event
    ///
    /// Usage:
    ///   var receiver = new H265StreamReceiver(monitorIdx, width, height);
    ///   receiver.OnTextureReady += (idx, tex) => { ... };
    ///   receiver.Start();
    ///   // From WebRTC encoded frame callback:
    ///   receiver.OnEncodedFrameReceived(encodedData, isKeyFrame, timestampUs);
    ///   // Each Unity Update():
    ///   receiver.Tick();
    ///   // On disconnect:
    ///   receiver.Dispose();
    /// </summary>
    public class H265StreamReceiver : IDisposable
    {
        private const string TAG = "[H265Receiver]";

        // ──────────── Configuration ────────────
        public readonly int MonitorIndex;
        public int Width { get; private set; }
        public int Height { get; private set; }

        // ──────────── State ────────────
        private HevcDecoderPlugin _decoder;
        private bool _initialized;
        private bool _disposed;
        private bool _flipY = true;      // true fixes upside-down reports
        private bool _fullRange = false;  // Hardware MediaCodec outputs limited-range YUV (Y:16-235)

        // Thread-safe timing
        private static readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // ──────────── Textures ────────────
        private Texture2D _yTex;
        private Texture2D _uvTex;
        private RenderTexture _outputRt;
        private Material _nv12Material;

        // ──────────── Stats ────────────
        public long EncodedFramesReceived { get; private set; }
        public long DecodedFrameCount     => _decodedCount;
        private long _decodedCount;

        // ──────────── Fallback Detection ────────────
        // If we receive encoded frames but decode nothing for FALLBACK_TRIGGER_SECONDS,
        // fire OnDecoderFailed so the client can request H265→H264 codec downgrade.
        // Phase 1 (quick probe): Fast fallback if decoder never outputs a single frame
        // Phase 2 (sustained): Slower fallback for mid-stream stalls (decoder worked then stopped)
        private const float FALLBACK_PROBE_SECONDS = 3f;         // 3s quick probe — fast fail if decoder can't start
        private const float FALLBACK_TRIGGER_SECONDS = 5f;       // 5s sustained stall after initial success
        private const int   FALLBACK_MIN_ENCODED_FRAMES = 15;    // Require at least 15 encoded frames received first
        private DateTime    _firstEncodedFrameTime = DateTime.MinValue;
        private DateTime    _lastDecodedFrameTime = DateTime.MinValue;  // Track last successful decode
        private bool        _fallbackFired;

        /// <summary>
        /// Fires when the H265 decoder consistently fails to produce frames.
        /// Signals that the client should request H265→H264 fallback from the server.
        /// Parameters: monitorIndex
        /// </summary>
        public event Action<int> OnDecoderFailed;

        /// <summary>
        /// Fires when a short stall is detected — request keyframe before full fallback.
        /// Parameters: monitorIndex
        /// </summary>
        public event Action<int> OnKeyframeNeeded;
        private bool _keyframeRequested; // Prevent spamming keyframe requests

        // Y/UV byte buffers — reused to avoid GC pressure
        private byte[] _yBuf;
        private byte[] _uvBuf;

        // ──────────── Corruption Detection ────────────
        // Track Y-plane luminance to detect inter-frame prediction corruption.
        // When P-frames are lost, decoder produces frames with wildly wrong colors.
        // Detect by monitoring average luminance jumps between consecutive frames.
        private float _prevAvgLuminance = -1f;
        private int _luminanceJumpCount = 0;
        private const float LUMINANCE_JUMP_THRESHOLD = 0.25f;  // 25% absolute jump in avg Y
        private const int LUMINANCE_JUMP_TRIGGER = 3;           // 3 rapid jumps = likely corruption
        private DateTime _lastLuminanceJumpTime = DateTime.MinValue;
        private const float LUMINANCE_JUMP_WINDOW_SECONDS = 1.0f; // Reset jump count after 1s calm

        // Corruption recovery state
        private bool _corruptionSuspected;
        private DateTime _lastCorruptionTime = DateTime.MinValue;

        // ── IDR gate: after flush, only accept keyframes until reference chain is restored ──
        private bool _waitingForCleanIdr;

        /// <summary>
        /// Fires when frame corruption is detected (e.g., from inter-frame prediction errors).
        /// Parameters: monitorIndex. Receiver should request keyframe + decoder flush.
        /// </summary>
        public event Action<int> OnCorruptionDetected;

        // ──────────── Events ────────────
        /// <summary>
        /// Fires on Unity main thread each time a new decoded frame is ready.
        /// Parameters: monitorIndex, texture (RGBA RenderTexture)
        /// </summary>
        public event Action<int, Texture> OnTextureReady;

        // ──────────── Constructor ────────────

        public H265StreamReceiver(int monitorIndex, int width, int height)
        {
            MonitorIndex = monitorIndex;
            Width        = width;
            Height       = height;
        }

        // ──────────── Lifecycle ────────────

        /// <summary>
        /// Initialize decoder and textures. Must be called on Unity main thread.
        /// </summary>
        public bool Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(H265StreamReceiver));

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!HevcDecoderPlugin.IsAvailable())
            {
                Debug.LogError($"{TAG} PC{MonitorIndex} HEVC hardware decoder not available on this device");
                return false;
            }
#endif
            _decoder = new HevcDecoderPlugin();
            bool ok = _decoder.Initialize(Width, Height);
            if (!ok)
            {
                Debug.LogError($"{TAG} PC{MonitorIndex} Failed to initialize HevcDecoderPlugin {Width}x{Height}");
                _decoder.Dispose();
                _decoder = null;
                return false;
            }

            // Y plane: R8 texture (full resolution)
            _yTex = new Texture2D(Width, Height, TextureFormat.R8, false, true);
            _yTex.filterMode = FilterMode.Bilinear;
            _yTex.name = $"H265_Y_Mon{MonitorIndex}";

            // UV plane: RG16 texture (half resolution, interleaved)
            _uvTex = new Texture2D(Width / 2, Height / 2, TextureFormat.RG16, false, true);
            _uvTex.filterMode = FilterMode.Bilinear;
            _uvTex.name = $"H265_UV_Mon{MonitorIndex}";

            // Output RenderTexture (full RGBA)
            _outputRt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            _outputRt.filterMode = FilterMode.Trilinear;
            _outputRt.anisoLevel = 8;
            _outputRt.name = $"H265_Output_Mon{MonitorIndex}";
            _outputRt.Create();

            // NV12→RGBA blit material
            var shader = Shader.Find("VRWorkspace/NV12ToRGBA");
            if (shader == null)
            {
                Debug.LogError($"{TAG} Shader 'VRWorkspace/NV12ToRGBA' not found! Make sure it is in the Shaders folder.");
                return false;
            }
            _nv12Material = new Material(shader) { name = "NV12ToRGBA_Mat" };
            _nv12Material.SetTexture("_YTex",  _yTex);
            _nv12Material.SetTexture("_UVTex", _uvTex);
            _nv12Material.SetFloat("_FlipY", _flipY ? 1f : 0f);
            _nv12Material.SetFloat("_FullRange", _fullRange ? 1f : 0f);

            // Pre-allocate CPU buffers
            _yBuf  = new byte[Width  * Height];
            _uvBuf = new byte[(Width / 2) * (Height / 2) * 2]; // RG16 = 2 bytes/pixel

            _initialized = true;
            Debug.Log($"{TAG} PC{MonitorIndex} Initialized {Width}x{Height}");
            return true;
        }

        // ──────────── Encoded Frame Input ────────────

        /// <summary>
        /// Feed a complete H265 encoded frame (Annex-B NAL unit or complete frame with multiple NALs).
        /// Safe to call from any thread — data is queued for next Tick().
        /// </summary>
        public void OnEncodedFrameReceived(byte[] encodedData, bool isKeyFrame, long presentationTimeUs = 0)
        {
            if (!_initialized || _disposed || _decoder == null) return;
            if (encodedData == null || encodedData.Length == 0) return;

            EncodedFramesReceived++;

            // Start fallback timer on first encoded frame received
            if (_firstEncodedFrameTime == DateTime.MinValue)
                _firstEncodedFrameTime = DateTime.UtcNow;

            // IDR gate: after flush, drop P-frames until a keyframe restores the reference chain
            if (_waitingForCleanIdr)
            {
                if (isKeyFrame)
                {
                    _waitingForCleanIdr = false;
                    Debug.Log($"{TAG} PC{MonitorIndex} Clean IDR received — reference chain restored, accepting P-frames again");
                }
                else
                {
                    return; // Drop P-frame — no valid reference after flush
                }
            }

            // Use PushEncodedFrame to pass the explicit isKeyFrame flag (detected by NAL parsing)
            bool pushed = _decoder.PushEncodedFrame(encodedData, presentationTimeUs > 0 ? presentationTimeUs : GetTimestampUs(), isKeyFrame);
            
            if (isKeyFrame && EncodedFramesReceived < 20)
            {
                Debug.Log($"{TAG} PC{MonitorIndex} Keyframe pushed to decoder {(pushed ? "successfully" : "FAILED")}: {encodedData.Length} bytes");
            }
        }

        // ──────────── Update (call from Unity main thread) ────────────

        private int _noFrameTicks;  // Consecutive ticks with no decoded frame (for stall diagnostics)

        /// <summary>
        /// Poll decoded frames and upload texture. Call this from MonoBehaviour.Update().
        /// </summary>
        public void Tick()
        {
            if (!_initialized || _disposed || _decoder == null) return;

            // Poll all available decoded frames (may have multiple queued)
            bool gotFrame = false;
            int maxFramesPerTick = 3; // Avoid spending too long in one frame
            for (int i = 0; i < maxFramesPerTick; i++)
            {
                if (!_decoder.TryGetFrame()) break;
                gotFrame = true;
                _decodedCount++;

                // Throttled diagnostic logging to confirm frame output
                if (_decodedCount % 300 == 0 || _decodedCount < 10)
                {
                    Debug.Log($"{TAG} PC{MonitorIndex} Frame decoded: #{_decodedCount}, size={_decoder.FrameWidth}x{_decoder.FrameHeight}");
                }
            }

            if (!gotFrame)
            {
                _noFrameTicks++;
                // Request keyframe after ~0.75s stall (45 ticks at 60fps) — fast recovery for corruption
                if (_noFrameTicks == 45 && !_keyframeRequested && _decodedCount > 0)
                {
                    _keyframeRequested = true;
                    // Push stall reference forward: give server time to respond to keyframe request
                    // before triggering DECODER FAILURE fallback. Server may be draining DC buffer.
                    _lastDecodedFrameTime = DateTime.UtcNow;
                    Debug.LogWarning($"{TAG} PC{MonitorIndex} Short stall detected ({_noFrameTicks} ticks, ~{_noFrameTicks / 60f:F1}s), requesting keyframe");
                    OnKeyframeNeeded?.Invoke(MonitorIndex);
                }

                // Log warning every ~3s (180 ticks at 60fps) if decoder has never stalled before
                if (_noFrameTicks == 180 || _noFrameTicks == 600 || _noFrameTicks == 1200)
                {
                    float elapsedSinceStart = (float)(DateTime.UtcNow - _firstEncodedFrameTime).TotalSeconds;
                    Debug.LogWarning($"{TAG} PC{MonitorIndex} SUSTAINED STALL: No frame from decoder for {_noFrameTicks} ticks ({elapsedSinceStart:F1}s). " +
                        $"Stats: decoded={_decodedCount}, encoded={EncodedFramesReceived} (gap={EncodedFramesReceived - _decodedCount}). " +
                        $"Plugin: initialized={_decoder.IsInitialized}, strides={_decoder.YStride}/{_decoder.UVStride}, size={_decoder.FrameWidth}x{_decoder.FrameHeight}");
                }

                // ── Fallback detection (2-phase) ─────────────────────────────────
                // Phase 1 (quick probe): If decoder NEVER produced a frame, fail fast (3s)
                // Phase 2 (sustained stall): If decoder worked then stopped, use longer timeout (5s)
                // Skip when deliberately waiting for IDR (P-frames are being dropped intentionally)
                if (!_fallbackFired
                    && !_waitingForCleanIdr
                    && EncodedFramesReceived >= FALLBACK_MIN_ENCODED_FRAMES
                    && _firstEncodedFrameTime != DateTime.MinValue)
                {
                    bool neverDecoded = _decodedCount == 0;
                    float timeout = neverDecoded ? FALLBACK_PROBE_SECONDS : FALLBACK_TRIGGER_SECONDS;

                    var referenceTime = _lastDecodedFrameTime != DateTime.MinValue
                        ? _lastDecodedFrameTime
                        : _firstEncodedFrameTime;

                    if ((DateTime.UtcNow - referenceTime).TotalSeconds >= timeout)
                    {
                        _fallbackFired = true;
                        string phase = neverDecoded ? "QUICK PROBE" : "SUSTAINED STALL";
                        Debug.LogError($"{TAG} PC{MonitorIndex} DECODER FAILURE ({phase}): received {EncodedFramesReceived} encoded frames, " +
                            $"decoded {_decodedCount}, stalled for {timeout}s. Triggering H265→H264 fallback!");
                        OnDecoderFailed?.Invoke(MonitorIndex);
                    }
                }

                return;
            }

            _noFrameTicks = 0; // Reset on successful frame
            _keyframeRequested = false; // Allow new keyframe request on next stall
            _lastDecodedFrameTime = DateTime.UtcNow; // Track last successful decode for stall detection

            // Get Y plane data
            byte[] yData  = _decoder.GetYPlaneData();
            byte[] uvData = _decoder.GetUVPlaneData();

            if (yData == null || uvData == null) return;

            int yStride  = _decoder.YStride;
            int uvStride = _decoder.UVStride;
            int fWidth   = _decoder.FrameWidth;
            int fHeight  = _decoder.FrameHeight;

            // Handle resolution changes by reallocating textures and buffers.
            // If the decoder starts outputting a lower/higher resolution than configured
            // (e.g. Adaptive Bitrate downscaling), we must recreate textures to prevent
            // UnityException (LoadRawTextureData: not enough data provided).
            if (fWidth != Width || fHeight != Height)
            {
                Debug.LogWarning($"{TAG} PC{MonitorIndex} Resolution changed dynamically: {Width}x{Height} -> {fWidth}x{fHeight}. Reallocating textures.");
                
                Width = fWidth;
                Height = fHeight;
                
                if (_yTex != null) { UnityEngine.Object.Destroy(_yTex); }
                if (_uvTex != null) { UnityEngine.Object.Destroy(_uvTex); }
                if (_outputRt != null) { _outputRt.Release(); UnityEngine.Object.Destroy(_outputRt); }
                
                // Recreate textures with the new dimensions
                _yTex = new Texture2D(Width, Height, TextureFormat.R8, false, true);
                _yTex.filterMode = FilterMode.Bilinear;
                _yTex.name = $"H265_Y_Mon{MonitorIndex}";

                _uvTex = new Texture2D(Width / 2, Height / 2, TextureFormat.RG16, false, true);
                _uvTex.filterMode = FilterMode.Bilinear;
                _uvTex.name = $"H265_UV_Mon{MonitorIndex}";

                _outputRt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
                _outputRt.filterMode = FilterMode.Trilinear;
                _outputRt.anisoLevel = 8;
                _outputRt.name = $"H265_Output_Mon{MonitorIndex}";
                _outputRt.Create();
                
                // Re-bind to the material
                if (_nv12Material != null)
                {
                    _nv12Material.SetTexture("_YTex", _yTex);
                    _nv12Material.SetTexture("_UVTex", _uvTex);
                }
                
                // Re-allocate the byte buffers
                _yBuf = new byte[Width * Height];
                _uvBuf = new byte[(Width / 2) * (Height / 2) * 2];
            }

            // Upload Y plane: always use stride-copy to produce exactly fWidth*fHeight bytes.
            // Passing the raw buffer (which may be larger due to MediaCodec stride alignment)
            // to LoadRawTextureData would throw an exception or produce a black texture.
            {
                int yNeeded = fWidth * fHeight;
                if (_yBuf == null || _yBuf.Length < yNeeded)
                    _yBuf = new byte[yNeeded];

                if (yStride == fWidth)
                {
                    // No padding: fast copy entire buffer (only the exact needed bytes)
                    Buffer.BlockCopy(yData, 0, _yBuf, 0, yNeeded);
                }
                else
                {
                    // Stride padding present: copy row by row
                    for (int row = 0; row < fHeight; row++)
                        Buffer.BlockCopy(yData, row * yStride, _yBuf, row * fWidth, fWidth);
                }
                _yTex.LoadRawTextureData(_yBuf);
                _yTex.Apply(false, false);
            }

            // ── Corruption detection (cheap: 16 sample points from Y plane) ──
            if (CheckFrameCorruption(yData, fWidth, fHeight, yStride))
            {
                if (!_corruptionSuspected || (DateTime.UtcNow - _lastCorruptionTime).TotalSeconds > 2.0)
                {
                    _corruptionSuspected = true;
                    _lastCorruptionTime = DateTime.UtcNow;
                    Debug.LogWarning($"{TAG} PC{MonitorIndex} CORRUPTION DETECTED: rapid luminance oscillation (decoded={_decodedCount}).");

                    // Fire corruption event — TaintTrack handles flush + keyframe + P-frame gating
                    OnCorruptionDetected?.Invoke(MonitorIndex);
                    return; // Skip displaying this corrupted frame
                }
            }
            else if (_corruptionSuspected && (DateTime.UtcNow - _lastCorruptionTime).TotalSeconds > 3.0)
            {
                // Corruption resolved (3s of clean frames)
                _corruptionSuspected = false;
                Debug.Log($"{TAG} PC{MonitorIndex} Corruption resolved (3s clean)");
            }

            // Upload UV plane: always use stride-copy to produce exactly uvWidth*uvHeight*2 bytes.
            // Android NV12 UV plane is interleaved (CbCr), RG16 format = 2 bytes per pixel.
            {
                int uvWidth    = fWidth  / 2;
                int uvHeight   = fHeight / 2;
                int uvRowBytes = uvWidth * 2; // RG16: 2 bytes per UV pixel
                int uvNeeded   = uvRowBytes * uvHeight;

                if (_uvBuf == null || _uvBuf.Length < uvNeeded)
                    _uvBuf = new byte[uvNeeded];

                if (uvStride == uvRowBytes)
                {
                    // No padding: fast copy exact needed bytes
                    Buffer.BlockCopy(uvData, 0, _uvBuf, 0, uvNeeded);
                }
                else
                {
                    // Stride padding present: copy row by row
                    for (int row = 0; row < uvHeight; row++)
                        Buffer.BlockCopy(uvData, row * uvStride, _uvBuf, row * uvRowBytes, uvRowBytes);
                }
                _uvTex.LoadRawTextureData(_uvBuf);
                _uvTex.Apply(false, false);
            }

            // Blit NV12 → RGBA output RenderTexture
            Graphics.Blit(null, _outputRt, _nv12Material);

            // Fire callback (on main thread already)
            OnTextureReady?.Invoke(MonitorIndex, _outputRt);
        }

        // ──────────── Control ────────────

        /// <summary>
        /// Flush decoder on stream discontinuity (e.g., server restart, seek).
        /// After flush, only keyframes are accepted until the reference chain is restored.
        /// </summary>
        public void Flush()
        {
            if (_initialized && !_disposed)
            {
                _decoder?.Flush();
                _waitingForCleanIdr = true;
                Debug.Log($"{TAG} PC{MonitorIndex} Flushed — waiting for clean IDR before accepting P-frames");
            }
        }

        /// <summary>
        /// Sample average luminance from Y-plane data (cheap: 16 sample points in 4x4 grid).
        /// Y plane in NV12 is raw luminance: 0=black, 255=white.
        /// </summary>
        private float SampleAverageLuminance(byte[] yData, int width, int height, int stride)
        {
            if (yData == null || width == 0 || height == 0) return 0.5f;

            float sum = 0f;
            int samples = 0;

            // Sample 4x4 grid (16 points) — very fast, avoids reading entire plane
            for (int row = 1; row <= 4; row++)
            {
                int y = height * row / 5;
                for (int col = 1; col <= 4; col++)
                {
                    int x = width * col / 5;
                    int idx = y * stride + x;
                    if (idx < yData.Length)
                    {
                        sum += yData[idx];
                        samples++;
                    }
                }
            }

            return samples > 0 ? (sum / samples) / 255f : 0.5f;
        }

        /// <summary>
        /// Check for frame corruption by detecting rapid luminance jumps.
        /// Returns true if corruption is suspected.
        /// </summary>
        private bool CheckFrameCorruption(byte[] yData, int width, int height, int stride)
        {
            float avgLum = SampleAverageLuminance(yData, width, height, stride);

            if (_prevAvgLuminance < 0f)
            {
                _prevAvgLuminance = avgLum;
                return false;
            }

            float delta = Mathf.Abs(avgLum - _prevAvgLuminance);
            _prevAvgLuminance = avgLum;

            if (delta > LUMINANCE_JUMP_THRESHOLD)
            {
                var now = DateTime.UtcNow;

                // Reset jump count if too much time has passed (legitimate scene change)
                if ((now - _lastLuminanceJumpTime).TotalSeconds > LUMINANCE_JUMP_WINDOW_SECONDS)
                    _luminanceJumpCount = 0;

                _luminanceJumpCount++;
                _lastLuminanceJumpTime = now;

                // Multiple rapid jumps = corruption (legitimate changes are usually sustained, not oscillating)
                if (_luminanceJumpCount >= LUMINANCE_JUMP_TRIGGER)
                {
                    _luminanceJumpCount = 0;
                    return true;
                }
            }

            return false;
        }

        public void SetFlipY(bool flip)
        {
            _flipY = flip;
            if (_nv12Material != null)
                _nv12Material.SetFloat("_FlipY", _flipY ? 1f : 0f);
        }

        public void SetFullRange(bool fullRange)
        {
            _fullRange = fullRange;
            if (_nv12Material != null)
                _nv12Material.SetFloat("_FullRange", _fullRange ? 1f : 0f);
        }

        // ──────────── IDisposable ────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _decoder?.Dispose();
            _decoder = null;

            if (_yTex   != null) { UnityEngine.Object.Destroy(_yTex);   _yTex   = null; }
            if (_uvTex  != null) { UnityEngine.Object.Destroy(_uvTex);  _uvTex  = null; }
            if (_outputRt != null) { _outputRt.Release(); UnityEngine.Object.Destroy(_outputRt); _outputRt = null; }
            if (_nv12Material != null) { UnityEngine.Object.Destroy(_nv12Material); _nv12Material = null; }

            _initialized = false;
            Debug.Log($"{TAG} PC{MonitorIndex} Disposed (decoded={_decodedCount})");
        }

        // ──────────── Helpers ────────────

        private static long GetTimestampUs()
            => _stopwatch.ElapsedTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

        public override string ToString()
            => $"H265StreamReceiver[mon={MonitorIndex}, {Width}x{Height}, decoded={_decodedCount}]";
    }
}
