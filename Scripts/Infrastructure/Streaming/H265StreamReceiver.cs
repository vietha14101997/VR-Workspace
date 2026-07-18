using System;
using System.Collections;
using UnityEngine;
using VRWorkspace.Native;
using VRWorkspace.Core;

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
        public readonly bool IsH264;
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

        /// <summary>
        /// When true, server reports desktop content is unchanged (user reading, no mouse movement).
        /// Stall detection and decode polling are suppressed to save GPU/CPU and reduce thermal load.
        /// The last decoded frame remains displayed on the output RenderTexture.
        /// </summary>
        public volatile bool IsDesktopIdle;

        /// <summary>Fired once when the first frame is successfully decoded for this monitor.</summary>
        public event Action<int> OnFirstFrameDecoded;

        // Desktop idle detection: track if server stopped sending new encoded frames
        private long _lastEncodedCountForStall;

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

        // ──────────── Scene Change Detection ────────────
        // A single large sustained luminance delta (e.g. browser tab switch, fullscreen
        // window open/close) is a host scene change. The HEVC encoder keeps sending P-frames
        // with motion vectors against the previous content, which decodes correctly on
        // Android MediaCodec but corrupts on Unity because our CPU readback + texture upload
        // + mipmap pipeline can drop intermediate frames during the burst, leaving the
        // decoder with stale references. Request an IDR the moment we see the jump so the
        // server force-encodes a fresh keyframe and the decoder resets its reference chain.
        private const float SCENE_CHANGE_THRESHOLD = 0.35f;       // 35% absolute luma delta in one frame
        private const int SCENE_CHANGE_COOLDOWN_MS = 500;          // per-monitor throttle
        private const float SCENE_CHANGE_SETTLE_DELTA = 0.10f;     // consecutive-frame delta that counts as "settled"
        private long _lastSceneChangeKeyframeRequestTicks;
        private bool _sceneChangeInProgress;
        private DateTime _sceneChangeStartedAt = DateTime.MinValue;

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

        public H265StreamReceiver(int monitorIndex, int width, int height, bool isH264 = false)
        {
            MonitorIndex = monitorIndex;
            Width        = width;
            Height       = height;
            IsH264       = isH264;
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
            bool ok = _decoder.Initialize(Width, Height, IsH264);
            if (!ok)
            {
                Debug.LogError($"{TAG} PC{MonitorIndex} Failed to initialize {(IsH264 ? "H264" : "HEVC")} DecoderPlugin {Width}x{Height}");
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

            // Output RenderTexture (full RGBA) with mipmaps for anti-shimmer in VR.
            _outputRt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            _outputRt.useMipMap = true;
            _outputRt.autoGenerateMips = false;
            _outputRt.filterMode = FilterMode.Trilinear;
            _outputRt.anisoLevel = 16; // Maximize anisotropic filtering for VR quad stability
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
            AppLog.Log($"{TAG} PC{MonitorIndex} Initialized {Width}x{Height}");
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


            // IDR gate: after flush, drop P-frames until a keyframe restores the reference chain
            if (_waitingForCleanIdr)
            {
                if (isKeyFrame)
                {
                    _waitingForCleanIdr = false;
                    AppLog.Log($"{TAG} PC{MonitorIndex} Clean IDR received — reference chain restored, accepting P-frames again");
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
                AppLog.Log($"{TAG} PC{MonitorIndex} Keyframe pushed to decoder {(pushed ? "successfully" : "FAILED")}: {encodedData.Length} bytes");
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

                // Fire first-frame event (once) so UI can wait for all monitors to have content
                if (_decodedCount == 1)
                    OnFirstFrameDecoded?.Invoke(MonitorIndex);

                // Throttled diagnostic logging to confirm frame output
                if (_decodedCount % 300 == 0 || _decodedCount < 10)
                {
                    AppLog.Log($"{TAG} PC{MonitorIndex} Frame decoded: #{_decodedCount}, size={_decoder.FrameWidth}x{_decoder.FrameHeight}");
                }
            }

            if (!gotFrame)
            {
                // When streaming is paused OR desktop is idle, no frames are expected.
                // Reset stall counter to prevent false DECODER FAILURE.
                if (IsDesktopIdle)
                {
                    _noFrameTicks = 0;
                    _keyframeRequested = false;

                    return;
                }

                _noFrameTicks++;

                // Desktop idle detection: if server stopped sending frames (EncodedFramesReceived
                // hasn't increased), the desktop is static — no stall, no keyframe request needed.
                // The last decoded frame remains displayed correctly.
                if (_decodedCount > 0 && EncodedFramesReceived == _lastEncodedCountForStall)
                {
                    // Server not sending new frames → desktop idle → suppress stall detection
                    _noFrameTicks = 0;
                    _keyframeRequested = false;
                    return;
                }
                _lastEncodedCountForStall = EncodedFramesReceived;

                // Request keyframe after ~0.75s stall (45 ticks at 60fps) — fast recovery for corruption
                if (_noFrameTicks == 45 && !_keyframeRequested && _decodedCount > 0)
                {
                    _keyframeRequested = true;
                    // Push stall reference forward: give server time to respond to keyframe request
                    // before triggering DECODER FAILURE fallback. Server may be draining DC buffer.

                    AppLog.LogWarning($"{TAG} PC{MonitorIndex} Short stall detected ({_noFrameTicks} ticks, ~{_noFrameTicks / 60f:F1}s), requesting keyframe");
                    OnKeyframeNeeded?.Invoke(MonitorIndex);
                }

                // Self-recovery: flush decoder + request keyframe at 1.5s (90 ticks).
                // Android MediaCodec can hang on a single instance while others work fine.
                // Flushing clears the stuck frame and restores the decode pipeline.
                if (_noFrameTicks == 90 && _decodedCount > 0)
                {
                    Debug.LogWarning($"{TAG} PC{MonitorIndex} Decoder stall 3s — flushing decoder for self-recovery");
                    Flush();
                    _keyframeRequested = false; // Allow fresh keyframe request after flush
                    OnKeyframeNeeded?.Invoke(MonitorIndex);
                }

                // Log warning every ~3s (180 ticks at 60fps)
                if (_noFrameTicks == 180 || _noFrameTicks == 600 || _noFrameTicks == 1200)
                {
                    float elapsed = _noFrameTicks / 60f;
                    AppLog.LogWarning($"{TAG} PC{MonitorIndex} SUSTAINED STALL: No frame from decoder for {_noFrameTicks} ticks ({elapsed:F1}s). " +
                        $"Stats: decoded={_decodedCount}, encoded={EncodedFramesReceived} (gap={EncodedFramesReceived - _decodedCount}). " +
                        $"Plugin: initialized={_decoder.IsInitialized}, strides={_decoder.YStride}/{_decoder.UVStride}, size={_decoder.FrameWidth}x{_decoder.FrameHeight}");
                }

                return;
            }

            _noFrameTicks = 0; // Reset on successful frame
            _keyframeRequested = false; // Allow new keyframe request on next stall
            // Get Y plane data
            byte[] yData  = _decoder.GetYPlaneData();
            byte[] uvData = _decoder.GetUVPlaneData();

            if (yData == null || uvData == null) return;

            int yStride  = _decoder.YStride;
            int uvStride = _decoder.UVStride;
            int fWidth   = _decoder.FrameWidth;
            int fHeight  = _decoder.FrameHeight;

            // Handle resolution changes by reallocating textures and buffers.
            if (fWidth != Width || fHeight != Height)
            {
                AppLog.LogWarning($"{TAG} PC{MonitorIndex} Resolution changed dynamically: {Width}x{Height} -> {fWidth}x{fHeight}. Reallocating textures.");
                
                // CRITICAL: Stop frame processing for one tick to allow re-allocation
                Width = fWidth;
                Height = fHeight;
                
                if (_yTex != null) { UnityEngine.Object.Destroy(_yTex); }
                if (_uvTex != null) { UnityEngine.Object.Destroy(_uvTex); }
                if (_outputRt != null) { _outputRt.Release(); UnityEngine.Object.Destroy(_outputRt); }
                
                // Recreate textures with the new dimensions
                _yTex = new Texture2D(Width, Height, TextureFormat.R8, false, true);
                _yTex.filterMode = FilterMode.Bilinear;
                _yTex.wrapMode = TextureWrapMode.Clamp;
                _yTex.name = $"H265_Y_Mon{MonitorIndex}";

                _uvTex = new Texture2D(Width / 2, Height / 2, TextureFormat.RG16, false, true);
                _uvTex.filterMode = FilterMode.Bilinear;
                _uvTex.wrapMode = TextureWrapMode.Clamp;
                _uvTex.name = $"H265_UV_Mon{MonitorIndex}";

                _outputRt = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
                _outputRt.useMipMap = true;
                _outputRt.autoGenerateMips = false;
                _outputRt.filterMode = FilterMode.Trilinear;
                _outputRt.anisoLevel = 16;
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

                // Notify UI that the texture object has changed
                OnTextureReady?.Invoke(MonitorIndex, _outputRt);
                return; // Wait for next tick to upload data
            }

            // Upload Y plane: always use stride-copy to produce exactly fWidth*fHeight bytes.
            {
                int yNeeded = fWidth * fHeight;
                if (_yBuf == null || _yBuf.Length < yNeeded)
                    _yBuf = new byte[yNeeded];

                if (yStride == fWidth)
                {
                    Buffer.BlockCopy(yData, 0, _yBuf, 0, yNeeded);
                }
                else
                {
                    for (int row = 0; row < fHeight; row++)
                        Buffer.BlockCopy(yData, row * yStride, _yBuf, row * fWidth, fWidth);
                }
                _yTex.LoadRawTextureData(_yBuf);
                _yTex.Apply(false, false);
            }

            // Upload UV plane
            {
                int uvWidth    = fWidth  / 2;
                int uvHeight   = fHeight / 2;
                int uvRowBytes = uvWidth * 2;
                int uvNeeded   = uvRowBytes * uvHeight;

                if (_uvBuf == null || _uvBuf.Length < uvNeeded)
                    _uvBuf = new byte[uvNeeded];

                if (uvStride == uvRowBytes)
                {
                    Buffer.BlockCopy(uvData, 0, _uvBuf, 0, uvNeeded);
                }
                else
                {
                    for (int row = 0; row < uvHeight; row++)
                        Buffer.BlockCopy(uvData, row * uvStride, _uvBuf, row * uvRowBytes, uvRowBytes);
                }
                _uvTex.LoadRawTextureData(_uvBuf);
                _uvTex.Apply(false, false);
            }

            // Blit NV12 → RGBA output RenderTexture
            Graphics.Blit(null, _outputRt, _nv12Material);

            // Generate sharp mipmaps (Lanczos-2 kernel) for anti-shimmer in VR.
            // Limited to 6 levels to ensure coverage even at distance.
            SharpMipGenerator.Generate(_outputRt, sharpness: 0.1f, maxMipLevels: 6);

            // ── Scene change detection (single sustained luma jump) ──
            // Runs before oscillation check so the threshold gate owns the response and we
            // don't double-fire via the corruption path while the new scene settles.
            HandleSceneChange(yData, fWidth, fHeight, yStride);

            // ── Corruption detection (Move after blit to avoid blocking data flow) ──
            if (CheckFrameCorruption(yData, fWidth, fHeight, yStride))
            {
                if (!_corruptionSuspected || (DateTime.UtcNow - _lastCorruptionTime).TotalSeconds > 2.0)
                {
                    _corruptionSuspected = true;
                    _lastCorruptionTime = DateTime.UtcNow;
                    AppLog.LogWarning($"{TAG} PC{MonitorIndex} CORRUPTION DETECTED: rapid luminance oscillation.");

                    // Fire corruption event — TaintTrack handles flush + keyframe + P-frame gating
                    OnCorruptionDetected?.Invoke(MonitorIndex);
                }
            }
            else if (_corruptionSuspected && (DateTime.UtcNow - _lastCorruptionTime).TotalSeconds > 3.0)
            {
                _corruptionSuspected = false;
                AppLog.Log($"{TAG} PC{MonitorIndex} Corruption resolved (3s clean)");
            }

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
                AppLog.Log($"{TAG} PC{MonitorIndex} Flushed — waiting for clean IDR before accepting P-frames");
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
            // While a scene change is settling, the oscillation gate owns nothing — the
            // scene-change handler already asked for a keyframe, so any oscillation here is
            // just the new scene stabilising, not real corruption.
            if (_sceneChangeInProgress) return false;

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

        /// <summary>
        /// Detect a single-frame large luminance jump (host scene change such as browser tab
        /// switch or fullscreen window open/close). On detection, request an IDR keyframe from
        /// the server so the Unity decode pipeline can recover its reference chain before
        /// corruption becomes visible. Per-monitor throttle prevents request spam when many
        /// frames are dropped back-to-back during the burst.
        /// </summary>
        private void HandleSceneChange(byte[] yData, int width, int height, int stride)
        {
            if (_prevAvgLuminance < 0f) return; // First sample: nothing to compare against

            float avgLum = SampleAverageLuminance(yData, width, height, stride);
            float delta = Mathf.Abs(avgLum - _prevAvgLuminance);

            if (_sceneChangeInProgress)
            {
                // Settle: either the next frame is calm, or we time out after 1s. Either way
                // we let the oscillation gate take over for the rest of the stream.
                if (delta <= SCENE_CHANGE_SETTLE_DELTA ||
                    (DateTime.UtcNow - _sceneChangeStartedAt).TotalSeconds > 1.0)
                {
                    _sceneChangeInProgress = false;
                }
                return;
            }

            if (delta < SCENE_CHANGE_THRESHOLD) return;

            long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            long cooldownTicks = SCENE_CHANGE_COOLDOWN_MS * System.Diagnostics.Stopwatch.Frequency / 1000;
            if (nowTicks - _lastSceneChangeKeyframeRequestTicks < cooldownTicks) return;

            _lastSceneChangeKeyframeRequestTicks = nowTicks;
            _sceneChangeInProgress = true;
            _sceneChangeStartedAt = DateTime.UtcNow;
            _luminanceJumpCount = 0;

            AppLog.LogWarning(
                $"{TAG} PC{MonitorIndex} SCENE CHANGE detected (Δluma={delta:F2}); requesting keyframe");

            // OnKeyframeNeeded routes to PhaseProtocolClient.RequestKeyframe → server force IDR.
            OnKeyframeNeeded?.Invoke(MonitorIndex);
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
            AppLog.Log($"{TAG} PC{MonitorIndex} Disposed (decoded={_decodedCount})");
        }

        // ──────────── Helpers ────────────

        private static long GetTimestampUs()
            => _stopwatch.ElapsedTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

        public override string ToString()
            => $"H265StreamReceiver[mon={MonitorIndex}, {Width}x{Height}, decoded={_decodedCount}]";
    }
}
