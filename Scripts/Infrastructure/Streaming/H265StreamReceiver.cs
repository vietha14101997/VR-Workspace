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

            // Use PushEncodedFrame to pass the explicit isKeyFrame flag (detected by NAL parsing)
            _decoder.PushEncodedFrame(encodedData, presentationTimeUs > 0 ? presentationTimeUs : GetTimestampUs(), isKeyFrame);
            
            if (isKeyFrame && EncodedFramesReceived < 20)
            {
                Debug.Log($"{TAG} PC{MonitorIndex} Keyframe pushed to decoder: {encodedData.Length} bytes");
            }
        }

        // ──────────── Update (call from Unity main thread) ────────────

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

            if (!gotFrame) return;

            // Get Y plane data
            byte[] yData  = _decoder.GetYPlaneData();
            byte[] uvData = _decoder.GetUVPlaneData();

            if (yData == null || uvData == null) return;

            int yStride  = _decoder.YStride;
            int uvStride = _decoder.UVStride;
            int fWidth   = _decoder.FrameWidth;
            int fHeight  = _decoder.FrameHeight;

            // Upload Y plane (strip stride padding if needed)
            if (yStride == fWidth)
            {
                _yTex.LoadRawTextureData(yData);
            }
            else
            {
                // Has stride padding — copy row by row into packed buffer
                if (_yBuf == null || _yBuf.Length < fWidth * fHeight)
                    _yBuf = new byte[fWidth * fHeight];

                for (int row = 0; row < fHeight; row++)
                    Buffer.BlockCopy(yData, row * yStride, _yBuf, row * fWidth, fWidth);
                _yTex.LoadRawTextureData(_yBuf);
            }
            _yTex.Apply(false, false);

            // Upload UV plane (RG16: 2 bytes per pixel, width/2, height/2)
            int uvWidth  = fWidth  / 2;
            int uvHeight = fHeight / 2;
            int uvRowBytes = uvWidth * 2; // RG16

            if (uvStride == uvWidth * 2)
            {
                _uvTex.LoadRawTextureData(uvData);
            }
            else
            {
                if (_uvBuf == null || _uvBuf.Length < uvRowBytes * uvHeight)
                    _uvBuf = new byte[uvRowBytes * uvHeight];

                for (int row = 0; row < uvHeight; row++)
                    Buffer.BlockCopy(uvData, row * uvStride, _uvBuf, row * uvRowBytes, uvRowBytes);
                _uvTex.LoadRawTextureData(_uvBuf);
            }
            _uvTex.Apply(false, false);

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
