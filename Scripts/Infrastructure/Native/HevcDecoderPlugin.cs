using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

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
        // Instance method on _decoderBridge: boolean pushEncodedFrame(byte[], long, boolean)
        // Used only by the byte-buffer / legacy path (instance mNativeHandle != 0).
        private IntPtr _pushFrameMethodId = IntPtr.Zero;
        private IntPtr _bridgeRawObject = IntPtr.Zero;

        // Static method (J[BJZ)Z on the HevcDecoderBridge class — Plan C surface
        // mode passes the decoder handle explicitly so we don't depend on the
        // Java instance's mNativeHandle (which createWithExternalTexture, being
        // static, never sets).
        private static IntPtr sBridgeClassRef = IntPtr.Zero;
        private static IntPtr sPushFrameStaticId = IntPtr.Zero;

        // Unity native plugin entrypoints — in libvrworkspace_oes_plugin.so which
        // Unity loads as a Unity native plugin from
// Assets/Plugins/Android/libs/<arch>/. UnityPluginLoad is called by Unity
// during plugin initialization, and GL.IssuePluginEvent dispatches
// callbacks to the registered render event handler.
//
// libhevc_decoder.so (the AAR's JNI library) and libvrworkspace_oes_plugin.so
// are two different libraries; we keep them separate so the Unity plugin
// path is fully isolated from the Java JNI path.
        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "GetOesTextureEventCallback")]
        private static extern System.IntPtr NativeGetOesEventCallbackPtr();

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "SetOesPendingAlloc")]
        private static extern void NativeSetOesPendingAlloc(int width, int height);

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "SetOesPendingFree")]
        private static extern void NativeSetOesPendingFree(int oesId, int tex2dId);

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "GetOesResultHandle")]
        private static extern int NativeGetOesResultHandle();

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "GetOesTex2dResultHandle")]
        private static extern int NativeGetOesTex2dResultHandle();

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "GetOesRequestPending")]
        private static extern int NativeGetOesRequestPending();

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "SetOesBlitParams")]
        private static extern void NativeSetOesBlitParams(int srcOesId, int dstTex2dId, int width, int height);

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "SetJavaVM")]
        private static extern void NativeSetJavaVM(System.IntPtr javaVM);

        [System.Runtime.InteropServices.DllImport("vrworkspace_oes_plugin",
            EntryPoint = "PreloadOesPlugin")]
        private static extern void NativePreloadOesPlugin();

        // Function pointer for GL.IssuePluginEvent / CommandBuffer.IssuePluginEvent.
        private static System.IntPtr s_OesEventCallbackPtr = System.IntPtr.Zero;
        private static readonly object s_OesCallbackLock = new object();

        private static void PreloadPluginOnMainThread()
        {
            try
            {
                NativePreloadOesPlugin();
#if UNITY_ANDROID && !UNITY_EDITOR
                System.IntPtr jvm = UnityEngine.AndroidJNI.GetJavaVM();
                if (jvm != System.IntPtr.Zero)
                {
                    NativeSetJavaVM(jvm);
                }
#endif
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{TAG} PreloadOesPlugin warning: {ex.Message}");
            }
        }

        private static System.IntPtr GetOesEventCallbackPtr()
        {
            PreloadPluginOnMainThread();
            if (s_OesEventCallbackPtr != System.IntPtr.Zero) return s_OesEventCallbackPtr;
            lock (s_OesCallbackLock)
            {
                if (s_OesEventCallbackPtr != System.IntPtr.Zero) return s_OesEventCallbackPtr;
                s_OesEventCallbackPtr = NativeGetOesEventCallbackPtr();
                if (s_OesEventCallbackPtr == System.IntPtr.Zero)
                {
                    Debug.LogError($"{TAG} OES plugin callback pointer is NULL " +
                                   "(libvrworkspace_oes_plugin.so may not be loaded)");
                }
                else
                {
                    Debug.Log($"{TAG} OES plugin callback ptr=0x{s_OesEventCallbackPtr.ToInt64():X} (raw native)");
                }
            }
            return s_OesEventCallbackPtr;
        }

        /// <summary>
        /// Issue the native OES+2D allocation callback on Unity's render thread.
        /// Returns (oesId, tex2dId) where oesId is GL_TEXTURE_EXTERNAL_OES (for Java)
        /// and tex2dId is GL_TEXTURE_2D (for Unity sampler2D). Both are 0 on failure.
        /// </summary>
        private static (int oesId, int tex2dId) AllocateOesTextureRenderThread(int width, int height)
        {
            System.IntPtr callbackPtr = GetOesEventCallbackPtr();
            if (callbackPtr == System.IntPtr.Zero)
            {
                Debug.LogError($"{TAG} AllocateOesTexture: callback ptr is NULL (libvrworkspace_oes_plugin.so not loaded)");
                return (0, 0);
            }

            // Write pending request parameters into C++ native memory
            NativeSetOesPendingAlloc(width, height);

            // CommandBuffer runs on the render thread within Unity's render pipeline.
            var cb = new CommandBuffer { name = "VRW_OES_Alloc" };
            cb.IssuePluginEvent(callbackPtr, 1);
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
            GL.Flush();

            // Spin-wait checking C++ native state (g_RequestPending)
            int spin = 0;
            while (NativeGetOesRequestPending() != 0 && spin < 2500)
            {
                System.Threading.Thread.Sleep(0);
                spin++;
            }

            int reqState = NativeGetOesRequestPending();
            if (reqState != 0)
            {
                Debug.LogError($"{TAG} AllocateOesTexture: render thread timed out " +
                               $"(ptr=0x{callbackPtr.ToInt64():X}, request={reqState})");
                return (0, 0);
            }

            int oesId = NativeGetOesResultHandle();
            int tex2dId = NativeGetOesTex2dResultHandle();
            if (oesId == 0 || tex2dId == 0)
            {
                Debug.LogError($"{TAG} AllocateOesTexture: allocation failed (oesId={oesId}, tex2dId={tex2dId})");
                return (0, 0);
            }
            Debug.Log($"{TAG} AllocateOesTexture: OES={oesId} + 2D={tex2dId} for {width}x{height}");
            return (oesId, tex2dId);
        }

        // Free an OES+2D texture pair allocated above. Synchronous: blocks
        // until the render-thread callback completes.
        private static void FreeOesTextureRenderThread(int oesId, int tex2dId)
        {
            if (oesId == 0 && tex2dId == 0) return;
            System.IntPtr callbackPtr = GetOesEventCallbackPtr();
            if (callbackPtr == System.IntPtr.Zero) return;

            NativeSetOesPendingFree(oesId, tex2dId);

            var cb = new CommandBuffer { name = "VRW_OES_Free" };
            cb.IssuePluginEvent(callbackPtr, 2);
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
            GL.Flush();

            int spin = 0;
            while (NativeGetOesRequestPending() != 0 && spin < 2500)
            {
                System.Threading.Thread.Sleep(0);
                spin++;
            }
            if (NativeGetOesRequestPending() != 0)
            {
                Debug.LogWarning($"{TAG} FreeOesTexture: render thread timed out");
            }
        }

        /// <summary>
        /// Issue a GPU blit from OES texture to 2D texture on the render thread.
        /// Non-blocking: the blit executes before scene rendering in the same frame.
        /// </summary>
        private void BlitOesToTex2d()
        {
            if (_surfaceModeOesTexId == 0 || _surfaceModeTex2dId == 0) return;
            System.IntPtr callbackPtr = GetOesEventCallbackPtr();
            if (callbackPtr == System.IntPtr.Zero) return;

            NativeSetOesBlitParams(_surfaceModeOesTexId, _surfaceModeTex2dId, _width, _height);

            var cb = new CommandBuffer { name = "VRW_OES_Blit" };
            cb.IssuePluginEvent(callbackPtr, 3);  // EVT_BLIT_OES
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
        }
#endif

        private bool _initialized;
        private bool _isSurfaceMode; // True iff initialized via InitializeWithSurface
        private bool _disposed;
        private int _width;
        private int _height;

        // Plan C (direct-surface mode): handle returned by Java SurfaceBinder +
        // createWithExternalTexture. Stored here so Dispose can release it via
        // releaseDirectSurface(handle) which atomically tears down the Java
        // Surface too. Set to 0 when inactive. Each receiver instance has its own.
        private long _surfaceModeHandle;
        // Plan C: GL handle of the GL_TEXTURE_EXTERNAL_OES texture given to
        // Java SurfaceTexture — MediaCodec writes decoded frames here.
        private int _surfaceModeOesTexId;
        // Plan C: GL handle of the GL_TEXTURE_2D texture — Unity's sampler2D
        // reads from here. Updated each frame via GPU blit from OES.
        private int _surfaceModeTex2dId;
        // Plan C: the external-wrapped Unity Texture2D (wrapping _surfaceModeTex2dId)
        // that the panel material binds.
        private Texture2D _surfaceModeTexture;

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
        /// Initialize the decoder with video dimensions (defaults to HEVC).
        /// </summary>
        public bool Initialize(int width, int height) => Initialize(width, height, false);

        /// <summary>
        /// Initialize the decoder with video dimensions and codec selection.
        /// </summary>
        /// <param name="width">Video width</param>
        /// <param name="height">Video height</param>
        /// <param name="isH264">true for H.264/AVC, false for H.265/HEVC</param>
        /// <returns>True if initialization successful</returns>
        public bool Initialize(int width, int height, bool isH264)
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
                bool result = _decoderBridge.Call<bool>("initialize", width, height, isH264);

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
        /// Initialize the decoder in Surface (zero-copy) mode. The native plugin
        /// will configure MediaCodec to render decoded frames directly into the
        /// supplied Android Surface, which is backed by the texture backing your
        /// <see cref="SurfaceTextureBridge"/>.
        ///
        /// <para>This eliminates the Texture2D.Apply async-upload race and the
        /// 5-stage GPU pipeline (Blit + SharpMipGenerator etc.) that caused the
        /// "đè trùng" artifact on tab switches.</para>
        /// </summary>
        /// <param name="width">Video width</param>
        /// <param name="height">Video height</param>
        /// <param name="isH264">true for H.264/AVC, false for H.265/HEVC</param>
        /// <param name="androidSurface">Android Surface obtained from a SurfaceTexture</param>
        /// <returns>True if initialization successful</returns>
        public bool InitializeWithSurface(int width, int height, bool isH264, AndroidJavaObject androidSurface)
        {
            if (_disposed)
            {
                Debug.LogError($"{TAG} Cannot initialize disposed decoder");
                return false;
            }
            if (androidSurface == null)
            {
                Debug.LogError($"{TAG} androidSurface is null");
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
                bool result = _decoderBridge.Call<bool>(
                    "initializeWithSurface", width, height, isH264, androidSurface);

                if (!result)
                {
                    Debug.LogError($"{TAG} Native initializeWithSurface failed");
                    _decoderBridge?.Dispose();
                    _decoderBridge = null;
                    return false;
                }

                _width = width;
                _height = height;
                _initialized = true;
                _isSurfaceMode = true;

                // No byte buffers needed in surface mode — the texture behind the
                // SurfaceTexture is updated directly by MediaCodec.
                _yPlaneBuffer = null;
                _uvPlaneBuffer = null;

                Debug.Log($"{TAG} Initialized {width}x{height} (SURFACE zero-copy mode)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} InitializeWithSurface exception: {ex.Message}");
                return false;
            }
#else
            Debug.LogWarning($"{TAG} Surface mode only available on Android");
            return false;
#endif
        }

        /// <summary>True iff this decoder was initialized in Surface (zero-copy) mode.</summary>
        public bool IsSurfaceMode => _isSurfaceMode;

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
        /// <returns>True if frame was queued successfully</returns>
        public bool PushEncodedFrame(byte[] nalData, long timestamp, bool isKeyFrame)
        {
            if (!_initialized || _disposed || nalData == null) return false;

#if UNITY_ANDROID && !UNITY_EDITOR
            lock (_lock)
            {
                if (!_initialized || _disposed) return false;
                try
                {
                    EnsureJniMethodIds();

                    sbyte[] signedNalData = (sbyte[])(Array)nalData;
                    IntPtr jByteArray = AndroidJNI.ToSByteArray(signedNalData);
                    try
                    {
                        // Plan C (direct surface mode) was started via the static
                        // createWithExternalTexture(...) entry point — that path
                        // doesn't touch the Java instance's mNativeHandle, so the
                        // instance method pushEncodedFrame([BJZ)Z would return false
                        // immediately. Route surface-mode pushes through the
                        // dedicated static overload (J[BJZ)Z keyed on the native
                        // handle we already stored in _surfaceModeHandle.
                        if (_surfaceModeHandle != 0L && sPushFrameStaticId != IntPtr.Zero)
                        {
                            jvalue[] staticArgs = new jvalue[4];
                            staticArgs[0].j = _surfaceModeHandle;
                            staticArgs[1].l = jByteArray;
                            staticArgs[2].j = timestamp;
                            staticArgs[3].z = isKeyFrame;
                            bool result = AndroidJNI.CallStaticBooleanMethod(
                                sBridgeClassRef, sPushFrameStaticId, staticArgs);
                            if (!result && EncodedFramePushFailureShouldLog(isKeyFrame))
                            {
                                Debug.LogWarning($"{TAG} Failed to push {(isKeyFrame ? "IDR" : "P")} frame to surface-mode decoder (buffer full or hardware busy)");
                            }
                            return result;
                        }

                        // Legacy / byte-buffer mode: instance method, instance mNativeHandle.
                        if (_decoderBridge == null || _pushFrameMethodId == IntPtr.Zero) return false;
                        jvalue[] args = new jvalue[3];
                        args[0].l = jByteArray;
                        args[1].j = timestamp;
                        args[2].z = isKeyFrame;
                        bool instResult = AndroidJNI.CallBooleanMethod(_bridgeRawObject, _pushFrameMethodId, args);
                        if (!instResult && EncodedFramePushFailureShouldLog(isKeyFrame))
                        {
                            Debug.LogWarning($"{TAG} Failed to push {(isKeyFrame ? "IDR" : "P")} frame to decoder (buffer full or hardware busy)");
                        }
                        return instResult;
                    }
                    finally
                    {
                        AndroidJNI.DeleteLocalRef(jByteArray);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{TAG} PushEncodedFrame exception: {ex.Message}");
                    return false;
                }
            }
#else
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Throttle per-frame push-failure warnings so we don't spam the log when
        // the decoder is busy; keep IDR + first-few-frames messages.
        private int _pushFailLogCount;
        private bool EncodedFramePushFailureShouldLog(bool isKeyFrame)
        {
            if (isKeyFrame) return true;
            _pushFailLogCount++;
            return _pushFailLogCount <= 5 || (_pushFailLogCount % 100) == 0;
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Cache JNI method IDs on first use. This avoids AndroidJNIHelper reflection
        /// which triggers byte/sbyte deprecation warnings on every call.
        /// </summary>
        private void EnsureJniMethodIds()
        {
            if (_bridgeRawObject != IntPtr.Zero && sPushFrameStaticId != IntPtr.Zero) return;

            if (_decoderBridge != null && _bridgeRawObject == IntPtr.Zero)
            {
                _bridgeRawObject = _decoderBridge.GetRawObject();
                IntPtr classRef = AndroidJNI.GetObjectClass(_bridgeRawObject);
                try
                {
                    _decodeMethodId = AndroidJNI.GetMethodID(classRef, "decode", "([BJ)Z");
                    _pushFrameMethodId = AndroidJNI.GetMethodID(classRef, "pushEncodedFrame", "([BJZ)Z");
                }
                finally
                {
                    AndroidJNI.DeleteLocalRef(classRef);
                }
            }

            // Plan C: cache the static (J[BJZ)Z method on the bridge class so
            // PushEncodedFrame can target the right native decoder handle
            // regardless of the Java instance's mNativeHandle field (which is
            // never set by createWithExternalTexture — that path is static).
            if (sBridgeClassRef == IntPtr.Zero)
            {
                if (_bridgeClass == null)
                    _bridgeClass = new AndroidJavaClass("com.vrworkspace.hevc.HevcDecoderBridge");
                sBridgeClassRef = _bridgeClass.GetRawClass();
            }
            if (sPushFrameStaticId == IntPtr.Zero)
            {
                sPushFrameStaticId = AndroidJNI.GetStaticMethodID(
                    sBridgeClassRef, "pushEncodedFrame", "(J[BJZ)Z");
            }
            Debug.Log($"{TAG} JNI method IDs cached (instance + static)");
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
                        // DON'T clear _hasFrame here — previous frame data in
                        // _yPlaneBuffer/_uvPlaneBuffer is still valid and may be
                        // needed by the caller (e.g., Tick() drain loop).
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
                if (!_initialized || _disposed) return;
                try
                {
                    // Plan C: the Java instance's mNativeHandle is never set,
                    // so the instance flush() is a no-op. Call the static
                    // handle-explicit variant instead.
                    if (_surfaceModeHandle != 0L)
                    {
                        if (_bridgeClass == null)
                            _bridgeClass = new AndroidJavaClass("com.vrworkspace.hevc.HevcDecoderBridge");
                        _bridgeClass.CallStatic("flush", _surfaceModeHandle);
                    }
                    else if (_decoderBridge != null)
                    {
                        _decoderBridge.Call("flush");
                    }
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
                if (!_initialized && _surfaceModeHandle == 0L) return;

#if UNITY_ANDROID && !UNITY_EDITOR
                // CRITICAL: AndroidJNI/AndroidJavaObject CANNOT be used on the Finalizer thread (GC thread).
                // Only release native resources if we are disposing explicitly from the main thread.
                if (disposing && _decoderBridge != null)
                {
                    // Plan C: tear down the direct-surface decoder first (Java releases
                    // Surface + SurfaceTexture references too), THEN fall through to
                    // release any legacy decoder handle.
                    try
                    {
                        if (_surfaceModeHandle != 0L)
                        {
                            _decoderBridge.CallStatic("releaseDirectSurface", _surfaceModeHandle);
                            _surfaceModeHandle = 0L;
                        }
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
                _isSurfaceMode = false;
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

        // ====================================================================
        // Plan C: direct-surface mode API
        // ====================================================================

        /// <summary>
        /// Starts a new decoder in surface mode using a Unity-side OpenGL texture
        /// for output. MediaCodec's GPU color converter renders decoded frames
        /// directly into the supplied texture, eliminating every async-upload
        /// race surface present in the byte-buffer / Compute pipelines.
        ///
        /// <para>Lifecycle: on success, a handle > 0 is stored internally.
        /// Call <see cref="TickFrame"/> once per Unity update to drain frames.
        /// The shared texture (panel binding target) is exposed via
        /// <see cref="SharedSurfaceTexture"/>. Call <see cref="Dispose"/> (or
        /// this class's normal Dispose) when done to release both the
        /// decoder and the Java-side Surface.</para>
        /// </summary>
        /// <param name="width">Video width</param>
        /// <param name="height">Video height</param>
        /// <param name="isH264">true for H.264, false for H.265</param>
        /// <returns>true if the decoder was created</returns>
        public bool StartDirectSurface(int width, int height, bool isH264)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HevcDecoderPlugin));
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_decoderBridge == null)
                    _decoderBridge = new AndroidJavaObject("com.vrworkspace.hevc.HevcDecoderBridge");

                if (_surfaceModeHandle != 0)
                {
                    Debug.LogWarning($"{TAG} StartDirectSurface: replacing existing handle {_surfaceModeHandle}");
                    StopDirectSurface();
                }

                // Allocate a dual texture pair on the render thread:
                //   oesId  = GL_TEXTURE_EXTERNAL_OES (SurfaceTexture writes here)
                //   tex2dId = GL_TEXTURE_2D (Unity sampler2D reads here)
                // A GPU blit copies OES → 2D each frame.
                var (oesTexId, tex2dId) = AllocateOesTextureRenderThread(width, height);
                if (oesTexId == 0 || tex2dId == 0)
                {
                    Debug.LogError($"{TAG} StartDirectSurface: dual-texture allocation failed ({width}x{height})");
                    return false;
                }

                // Wrap the GL_TEXTURE_2D handle as a Unity Texture2D.
                // The panel shader (sampler2D) will sample from this texture.
                // The OES texture is given to Java SurfaceTexture — we blit
                // OES → 2D each frame so the shader sees the decoded pixels.
                IntPtr tex2dPtr = new IntPtr(tex2dId);
                var externalTex = Texture2D.CreateExternalTexture(
                    width, height, TextureFormat.RGBA32,
                    mipChain: false, linear: true, tex2dPtr);
                if (externalTex == null)
                {
                    Debug.LogError($"{TAG} StartDirectSurface: CreateExternalTexture(2D={tex2dId}) returned null");
                    FreeOesTextureRenderThread(oesTexId, tex2dId);
                    return false;
                }
                externalTex.name = $"DirectSurfaceRT_{width}x{height}";
                externalTex.wrapMode = TextureWrapMode.Clamp;
                externalTex.filterMode = FilterMode.Bilinear;

                // Pass the OES handle to Java — SurfaceTexture is created from it.
                long handle = _decoderBridge.CallStatic<long>(
                    "createWithExternalTexture", width, height, isH264, oesTexId);
                if (handle == 0L)
                {
                    Debug.LogError($"{TAG} StartDirectSurface: Java returned 0 (oesTexId={oesTexId} {width}x{height})");
                    UnityEngine.Object.Destroy(externalTex);
                    FreeOesTextureRenderThread(oesTexId, tex2dId);
                    return false;
                }

                _surfaceModeHandle    = handle;
                _surfaceModeOesTexId  = oesTexId;
                _surfaceModeTex2dId   = tex2dId;
                _surfaceModeTexture   = externalTex;
                _width  = width;
                _height = height;
                _isSurfaceMode = true;
                _initialized    = true;
                Debug.Log($"[HevcDecoderPlugin] SURFACE_MODE_STARTED handle={handle} oesTexId={oesTexId} tex2dId={tex2dId} {width}x{height} build={Application.buildGUID}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} StartDirectSurface exception: {ex.Message}");
                return false;
            }
#else
            Debug.LogWarning($"{TAG} StartDirectSurface only available on Android");
            return false;
#endif
        }

        /// <summary>
        /// The shared Unity Texture2D whose backing GL handle is the OES
        /// texture MediaCodec writes to. Bind this on the panel material's
        /// main sampler so the panel shows the latest decoded frame. The
        /// returned texture is owned by this decoder instance — do not
        /// Destroy it; it is freed in <see cref="Dispose"/>.
        /// </summary>
        public Texture2D SharedSurfaceTexture => _surfaceModeTexture;

        /// <summary>
        /// Drain one or more decoded frames to the bound GL texture, then refresh
        /// SurfaceTexture so the next panel sample sees the latest decoded data.
        /// Call from Unity Update() each frame.
        /// </summary>
        /// <returns>number of frames drained (0-3). 0 means no frame available.</returns>
        public int TickFrame(int frameTimeoutUs = 0)
        {
            if (_disposed || _surfaceModeHandle == 0L) return 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                int drained = _decoderBridge.CallStatic<int>("tickFrame", _surfaceModeHandle, frameTimeoutUs);
                if (drained > 0)
                {
                    _hasFrame = true;
                    // GPU blit: copy OES → 2D so Unity's sampler2D sees the
                    // latest decoded frame. The blit executes on the render
                    // thread before scene rendering in this same frame.
                    BlitOesToTex2d();
                }
                return drained;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{TAG} TickFrame: {ex.Message}");
                return 0;
            }
#else
            return 0;
#endif
        }

        /// <summary>
        /// Release the decoder handle AND tear down the Java-side Surface.
        /// Idempotent. Call on stream shutdown before disposing the
        /// SurfaceDirectBridge that owned the Unity texture.
        /// </summary>
        public void StopDirectSurface()
        {
            if (_surfaceModeHandle == 0L) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _decoderBridge.CallStatic("releaseDirectSurface", _surfaceModeHandle);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{TAG} StopDirectSurface: {ex.Message}");
            }
#endif
            _surfaceModeHandle = 0L;
            int freedOesId  = _surfaceModeOesTexId;
            int freedTex2dId = _surfaceModeTex2dId;
            _surfaceModeOesTexId = 0;
            _surfaceModeTex2dId  = 0;
            if (_surfaceModeTexture != null)
            {
                UnityEngine.Object.Destroy(_surfaceModeTexture);
                _surfaceModeTexture = null;
            }
            // Free both native textures (OES + 2D) we allocated via the Unity plugin.
            // Done last, after Unity has destroyed its wrapper, so no thread
            // is still sampling the handle.
            if (freedOesId != 0 || freedTex2dId != 0)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { FreeOesTextureRenderThread(freedOesId, freedTex2dId); }
                catch (Exception ex) { Debug.LogWarning($"{TAG} FreeOesTextureRenderThread: {ex.Message}"); }
#endif
            }
            _hasFrame = false;
            _initialized    = false;  // mirror what ReleaseDecoder does for byte-buffer path
        }

        // ====================================================================

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
