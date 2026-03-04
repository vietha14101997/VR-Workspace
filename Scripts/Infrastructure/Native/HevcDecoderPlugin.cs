using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VRWorkspace.Native
{
    /// <summary>
    /// Unity C# wrapper for native HEVC/H.265 hardware decoder.
    /// Uses Android MediaCodec through JNI for hardware-accelerated decoding.
    /// Usage:
    /// 1. Check HevcDecoderPlugin.IsAvailable() before creating instance
    /// 2. Create instance and call Initialize(width, height)
    /// 3. Feed NAL units with DecodeNal(nalData)
    /// 4. Update textures with UpdateTexture(yTexture, uvTexture) when frame ready
    /// 5. Dispose() when done
    /// </summary>
    public class HevcDecoderPlugin : IDisposable
    {
        private const string TAG = "[HevcDecoder]";
        private static readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _decoderBridge;
        private static AndroidJavaClass _bridgeClass;
        
        // Cached JNI method IDs to avoid AndroidJNIHelper.GetSignature warnings
        private IntPtr _decodeMethodId = IntPtr.Zero;
        private IntPtr _pushFrameMethodId = IntPtr.Zero;
        private IntPtr _bridgeRawObject = IntPtr.Zero;
#endif

        private bool _initialized;
        private bool _disposed;
        private int _width;
        private int _height;

        // Frame data cached from native
        private byte[] _yPlaneBuffer;
        private byte[] _uvPlaneBuffer;
        private bool _hasFrame;
        private int _frameWidth;
        private int _frameHeight;
        private int _yStride;
        private int _uvStride;

        /// <summary>
        /// Check if hardware HEVC decoding is available on this device.
        /// </summary>
        public static bool IsAvailable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_bridgeClass == null)
                {
                    _bridgeClass = new AndroidJavaClass("com.vrworkspace.hevc.HevcDecoderBridge");
                }
                return _bridgeClass.CallStatic<bool>("isAvailable");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} IsAvailable exception: {ex.Message}");
                return false;
            }
#else
            Debug.LogWarning($"{TAG} HEVC decoder only available on Android");
            return false;
#endif
        }

        /// <summary>
        /// Initialize the decoder with video dimensions.
        /// </summary>
        /// <param name="width">Video width</param>
        /// <param name="height">Video height</param>
        /// <returns>True if initialization successful</returns>
        public bool Initialize(int width, int height)
        {
            if (_disposed)
            {
                Debug.LogError($"{TAG} Cannot initialize disposed decoder");
                return false;
            }

            if (_initialized)
            {
                Debug.LogWarning($"{TAG} Already initialized, releasing first");
                ReleaseDecoder(true);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _decoderBridge = new AndroidJavaObject("com.vrworkspace.hevc.HevcDecoderBridge");
                bool result = _decoderBridge.Call<bool>("initialize", width, height);

                if (!result)
                {
                    Debug.LogError($"{TAG} Native initialize failed");
                    _decoderBridge?.Dispose();
                    _decoderBridge = null;
                    return false;
                }

                _width = width;
                _height = height;
                _initialized = true;

                // Pre-allocate buffers for Y and UV planes (NV12 format).
                // IMPORTANT: Use 1.5x multiplier for UV to handle stride-aligned buffers.
                // Android MediaCodec aligns stride to 128 bytes, so for 1920-wide video:
                //   yStride = 1920, uvStride = 1920 (but sometimes 2048 on some GPUs)
                // We over-allocate to prevent overflow and resize dynamically in TryGetFrame.
                int ySize = width * height;
                // UV: allocate with extra padding for potential stride alignment
                // uvStride can be up to alignUp(width, 128). For 1920: max is 1920 itself.
                // For safety allocate using stride=alignUp(width, 128)
                int strideAlign = ((width + 127) / 128) * 128;
                int uvSize = (height / 2) * strideAlign;
                _yPlaneBuffer = new byte[ySize];
                _uvPlaneBuffer = new byte[uvSize];

                Debug.Log($"{TAG} Initialized {width}x{height}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} Initialize exception: {ex.Message}");
                return false;
            }
#else
            Debug.LogWarning($"{TAG} HEVC decoder only available on Android");
            return false;
#endif
        }

        /// <summary>
        /// Decode a NAL unit.
        /// </summary>
        /// <param name="nalData">NAL unit data including start code (0x00000001)</param>
        /// <returns>True if NAL was queued successfully</returns>
        public bool DecodeNal(byte[] nalData)
        {
            return DecodeNal(nalData, GetTimestampUs());
        }

        /// <summary>
        /// Decode a NAL unit with specific timestamp.
        /// </summary>
        /// <param name="nalData">NAL unit data including start code</param>
        /// <param name="presentationTimeUs">Presentation timestamp in microseconds</param>
        /// <returns>True if NAL was queued successfully</returns>
        public bool DecodeNal(byte[] nalData, long presentationTimeUs)
        {
            if (!_initialized || _disposed)
            {
                return false;
            }

            if (nalData == null || nalData.Length == 0)
            {
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            lock (_lock)
            {
                if (!_initialized || _disposed || _decoderBridge == null) return false;
                try
                {
                    EnsureJniMethodIds();
                    
                    // Use raw JNI to avoid AndroidJNIHelper.GetSignature byte/sbyte warnings
                    sbyte[] signedNalData = (sbyte[])(Array)nalData;
                    IntPtr jByteArray = AndroidJNI.ToSByteArray(signedNalData);
                    try
                    {
                        jvalue[] args = new jvalue[2];
                        args[0].l = jByteArray;
                        args[1].j = presentationTimeUs;
                        return AndroidJNI.CallBooleanMethod(_bridgeRawObject, _decodeMethodId, args);
                    }
                    finally
                    {
                        AndroidJNI.DeleteLocalRef(jByteArray);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{TAG} DecodeNal exception: {ex.Message}");
                    return false;
                }
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Push an encoded H.265 frame to the decoder bridge.
        /// This is used by the H265StreamReceiver pipeline.
        /// </summary>
        public void PushEncodedFrame(byte[] nalData, long timestamp, bool isKeyFrame)
        {
            if (!_initialized || _disposed || nalData == null) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            lock (_lock)
            {
                if (!_initialized || _disposed || _decoderBridge == null) return;
                try
                {
                    EnsureJniMethodIds();
                    
                    // Use raw JNI to avoid AndroidJNIHelper.GetSignature byte/sbyte warnings
                    sbyte[] signedNalData = (sbyte[])(Array)nalData;
                    IntPtr jByteArray = AndroidJNI.ToSByteArray(signedNalData);
                    try
                    {
                        jvalue[] args = new jvalue[3];
                        args[0].l = jByteArray;
                        args[1].j = timestamp;
                        args[2].z = isKeyFrame;
                        AndroidJNI.CallVoidMethod(_bridgeRawObject, _pushFrameMethodId, args);
                    }
                    finally
                    {
                        AndroidJNI.DeleteLocalRef(jByteArray);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{TAG} PushEncodedFrame exception: {ex.Message}");
                }
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Cache JNI method IDs on first use. This avoids AndroidJNIHelper reflection
        /// which triggers byte/sbyte deprecation warnings on every call.
        /// </summary>
        private void EnsureJniMethodIds()
        {
            if (_bridgeRawObject != IntPtr.Zero) return;
            
            _bridgeRawObject = _decoderBridge.GetRawObject();
            IntPtr classRef = AndroidJNI.GetObjectClass(_bridgeRawObject);
            try
            {
                _decodeMethodId = AndroidJNI.GetMethodID(classRef, "decode", "([BJ)Z");
                _pushFrameMethodId = AndroidJNI.GetMethodID(classRef, "pushEncodedFrame", "([BJZ)V");
                Debug.Log($"{TAG} JNI method IDs cached successfully");
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(classRef);
            }
        }
#endif

        /// <summary>
        /// Try to get a decoded frame and copy to managed buffers.
        /// </summary>
        /// <returns>True if a frame is available</returns>
        public unsafe bool TryGetFrame()
        {
            if (!_initialized || _disposed)
            {
                _hasFrame = false;
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            lock (_lock)
            {
                if (!_initialized || _disposed || _decoderBridge == null)
                {
                    _hasFrame = false;
                    return false;
                }

                try
                {
                    if (!_decoderBridge.Call<bool>("getFrame"))
                    {
                        _hasFrame = false;
                        return false;
                    }

                    // Get frame info
                    _frameWidth = _decoderBridge.Call<int>("getFrameWidth");
                    _frameHeight = _decoderBridge.Call<int>("getFrameHeight");
                    _yStride = _decoderBridge.Call<int>("getYStride");
                    _uvStride = _decoderBridge.Call<int>("getUVStride");
                    
                    if (_stopwatch.ElapsedMilliseconds % 2000 < 20) // Throttle log
                    {
                        Debug.Log($"{TAG} Frame decoded: {_frameWidth}x{_frameHeight}, stride={_yStride}");
                    }

                    // Get Y plane buffer
                    using (AndroidJavaObject yBuffer = _decoderBridge.Call<AndroidJavaObject>("getYPlane"))
                    {
                        if (yBuffer != null)
                        {
                            IntPtr yPtr = (IntPtr)AndroidJNI.GetDirectBufferAddress(yBuffer.GetRawObject());
                            if (yPtr != IntPtr.Zero)
                            {
                                int ySize = _frameHeight * _yStride;
                                if (_yPlaneBuffer == null || _yPlaneBuffer.Length < ySize)
                                {
                                    _yPlaneBuffer = new byte[ySize];
                                }
                                Marshal.Copy(yPtr, _yPlaneBuffer, 0, Math.Min(ySize, _yPlaneBuffer.Length));
                            }
                        }
                    }

                    // Get UV plane buffer
                    using (AndroidJavaObject uvBuffer = _decoderBridge.Call<AndroidJavaObject>("getUVPlane"))
                    {
                        if (uvBuffer != null)
                        {
                            IntPtr uvPtr = (IntPtr)AndroidJNI.GetDirectBufferAddress(uvBuffer.GetRawObject());
                            if (uvPtr != IntPtr.Zero)
                            {
                                // IMPORTANT: Use actual uvStride for size, not width-based calculation.
                                // Android MediaCodec may use aligned stride (e.g., 2048 for 1920-wide).
                                // Under-allocating here causes buffer overflow and memory corruption.
                                int uvSize = (_frameHeight / 2) * _uvStride;
                                if (_uvPlaneBuffer == null || _uvPlaneBuffer.Length < uvSize)
                                {
                                    Debug.LogWarning($"{TAG} UV buffer resize: {_uvPlaneBuffer?.Length ?? 0} -> {uvSize} (stride={_uvStride}, w={_frameWidth}, h={_frameHeight})");
                                    _uvPlaneBuffer = new byte[uvSize];
                                }
                                // Safe copy: copy exactly uvSize bytes (no overflow)
                                Marshal.Copy(uvPtr, _uvPlaneBuffer, 0, uvSize);
                            }
                        }
                    }

                    _hasFrame = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{TAG} TryGetFrame exception: {ex.Message}");
                    _hasFrame = false;
                    return false;
                }
            }
#else
            _hasFrame = false;
            return false;
#endif
        }

        /// <summary>
        /// Update Unity textures with decoded frame data.
        /// Call TryGetFrame() first to get the latest frame.
        /// </summary>
        /// <param name="yTexture">Texture for Y plane (R8 or Alpha8 format recommended)</param>
        /// <param name="uvTexture">Texture for UV plane (RG16 format recommended)</param>
        /// <returns>True if textures were updated</returns>
        public bool UpdateTextures(Texture2D yTexture, Texture2D uvTexture)
        {
            if (!_hasFrame || _yPlaneBuffer == null || _uvPlaneBuffer == null)
            {
                return false;
            }

            try
            {
                // Update Y texture
                if (yTexture != null)
                {
                    // Handle stride vs width mismatch
                    if (_yStride == _frameWidth)
                    {
                        // No padding, direct load
                        yTexture.LoadRawTextureData(_yPlaneBuffer);
                    }
                    else
                    {
                        // Has padding, need to remove it
                        byte[] yData = new byte[_frameWidth * _frameHeight];
                        for (int y = 0; y < _frameHeight; y++)
                        {
                            Array.Copy(_yPlaneBuffer, y * _yStride, yData, y * _frameWidth, _frameWidth);
                        }
                        yTexture.LoadRawTextureData(yData);
                    }
                    yTexture.Apply(false, false);
                }

                // Update UV texture
                if (uvTexture != null)
                {
                    int uvHeight = _frameHeight / 2;
                    int uvWidth = _frameWidth; // UV is interleaved, same width

                    if (_uvStride == uvWidth)
                    {
                        uvTexture.LoadRawTextureData(_uvPlaneBuffer);
                    }
                    else
                    {
                        byte[] uvData = new byte[uvWidth * uvHeight];
                        for (int y = 0; y < uvHeight; y++)
                        {
                            Array.Copy(_uvPlaneBuffer, y * _uvStride, uvData, y * uvWidth, uvWidth);
                        }
                        uvTexture.LoadRawTextureData(uvData);
                    }
                    uvTexture.Apply(false, false);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} UpdateTextures exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get Y plane data buffer directly.
        /// </summary>
        public byte[] GetYPlaneData() => _hasFrame ? _yPlaneBuffer : null;

        /// <summary>
        /// Get UV plane data buffer directly.
        /// </summary>
        public byte[] GetUVPlaneData() => _hasFrame ? _uvPlaneBuffer : null;

        /// <summary>
        /// Get the frame width from last decoded frame.
        /// </summary>
        public int FrameWidth => _hasFrame ? _frameWidth : _width;

        /// <summary>
        /// Get the frame height from last decoded frame.
        /// </summary>
        public int FrameHeight => _hasFrame ? _frameHeight : _height;

        /// <summary>
        /// Get the Y stride from last decoded frame.
        /// </summary>
        public int YStride => _yStride;

        /// <summary>
        /// Get the UV stride from last decoded frame.
        /// </summary>
        public int UVStride => _uvStride;

        /// <summary>
        /// Check if decoder is initialized.
        /// </summary>
        public bool IsInitialized => _initialized && !_disposed;

        /// <summary>
        /// Check if a frame is available.
        /// </summary>
        public bool HasFrame => _hasFrame;

        /// <summary>
        /// Flush the decoder (clear pending frames).
        /// Call on seek or stream discontinuity.
        /// </summary>
        public void Flush()
        {
            if (!_initialized || _disposed)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            lock (_lock)
            {
                if (!_initialized || _disposed || _decoderBridge == null) return;
                try
                {
                    _decoderBridge.Call("flush");
                    _hasFrame = false;
                    Debug.Log($"{TAG} Flushed");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{TAG} Flush exception: {ex.Message}");
                }
            }
#endif
        }

        private readonly object _lock = new object();

        private void ReleaseDecoder(bool disposing)
        {
            lock (_lock)
            {
                if (!_initialized) return;

#if UNITY_ANDROID && !UNITY_EDITOR
                // CRITICAL: AndroidJNI/AndroidJavaObject CANNOT be used on the Finalizer thread (GC thread).
                // Only release native resources if we are disposing explicitly from the main thread.
                if (disposing && _decoderBridge != null)
                {
                    try
                    {
                        _decoderBridge.Call("release");
                        _decoderBridge.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"{TAG} ReleaseDecoder JNI exception: {ex.Message}");
                    }
                    finally
                    {
                        _decoderBridge = null;
                    }
                }
#endif
                _initialized = false;
                _hasFrame = false;
                _yPlaneBuffer = null;
                _uvPlaneBuffer = null;
            }
        }

        /// <summary>
        /// Dispose of the decoder and free all resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            ReleaseDecoder(disposing);
            
            if (disposing)
            {
                Debug.Log($"{TAG} Disposed explicitly");
            }
        }

        private static long GetTimestampUs()
        {
            return _stopwatch.ElapsedTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;
        }

        ~HevcDecoderPlugin()
        {
            if (!_disposed)
            {
                // NEVER call Dispose() or anything touching JNI here.
                // Just set flags and cleanup managed resources.
                Dispose(false);
            }
        }
    }
}
