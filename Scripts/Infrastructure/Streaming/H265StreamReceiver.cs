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
        public readonly int Width;
        public readonly int Height;

        // ──────────── State ────────────
        private HevcDecoderPlugin _decoder;
        private bool _initialized;
        private bool _disposed;

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
        private const float FALLBACK_TRIGGER_SECONDS = 8f;       // 8s without decoded frame → declare failure
        private const int   FALLBACK_MIN_ENCODED_FRAMES = 30;    // Require at least 30 encoded frames received first
        private DateTime    _firstEncodedFrameTime = DateTime.MinValue;
        private bool        _fallbackFired;

        /// <summary>
        /// Fires when the H265 decoder consistently fails to produce frames.
        /// Signals that the client should request H265→H264 fallback from the server.
        /// Parameters: monitorIndex
        /// </summary>
        public event Action<int> OnDecoderFailed;

        // Y/UV byte buffers — reused to avoid GC pressure
        private byte[] _yBuf;
        private byte[] _uvBuf;

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
                // Log warning every ~3s (180 ticks at 60fps) if decoder has never stalled before
                if (_noFrameTicks == 180 || _noFrameTicks == 600 || _noFrameTicks == 1200)
                {
                    float elapsedSinceStart = (float)(DateTime.UtcNow - _firstEncodedFrameTime).TotalSeconds;
                    Debug.LogWarning($"{TAG} PC{MonitorIndex} SUSTAINED STALL: No frame from decoder for {_noFrameTicks} ticks ({elapsedSinceStart:F1}s). " +
                        $"Stats: decoded={_decodedCount}, encoded={EncodedFramesReceived} (gap={EncodedFramesReceived - _decodedCount}). " +
                        $"Plugin: initialized={_decoder.IsInitialized}, strides={_decoder.YStride}/{_decoder.UVStride}, size={_decoder.FrameWidth}x{_decoder.FrameHeight}");
                }

                // ── Fallback detection ──────────────────────────────────────────
                // If we received enough encoded frames but NEVER decoded any after the timeout,
                // the hardware H265 decoder is not functional on this device → trigger fallback.
                if (!_fallbackFired
                    && _decodedCount == 0
                    && EncodedFramesReceived >= FALLBACK_MIN_ENCODED_FRAMES
                    && _firstEncodedFrameTime != DateTime.MinValue
                    && (DateTime.UtcNow - _firstEncodedFrameTime).TotalSeconds >= FALLBACK_TRIGGER_SECONDS)
                {
                    _fallbackFired = true;
                    Debug.LogError($"{TAG} PC{MonitorIndex} DECODER FAILURE: received {EncodedFramesReceived} encoded frames " +
                        $"but decoded 0 in {FALLBACK_TRIGGER_SECONDS}s. Triggering H265→H264 fallback!");
                    OnDecoderFailed?.Invoke(MonitorIndex);
                }

                return;
            }

            _noFrameTicks = 0; // Reset on successful frame
            // Also reset fallback timer window so intermittent failures don't re-trigger
            _firstEncodedFrameTime = DateTime.UtcNow; // Sliding window from last success

            // Get Y plane data
            byte[] yData  = _decoder.GetYPlaneData();
            byte[] uvData = _decoder.GetUVPlaneData();

            if (yData == null || uvData == null) return;

            int yStride  = _decoder.YStride;
            int uvStride = _decoder.UVStride;
            int fWidth   = _decoder.FrameWidth;
            int fHeight  = _decoder.FrameHeight;

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
        /// </summary>
        public void Flush()
        {
            if (_initialized && !_disposed)
                _decoder?.Flush();
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
