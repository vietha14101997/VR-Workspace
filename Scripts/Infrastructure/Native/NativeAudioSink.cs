using System;
using UnityEngine;

namespace VRWorkspace.Native
{
    /// <summary>
    /// C# wrapper around the native AAudio-backed PCM16 audio sink living in
    /// the HevcDecoder Android plugin. On non-Android platforms the wrapper
    /// is a no-op so editor / standalone play mode does not break.
    ///
    /// All JNI calls go through raw <see cref="AndroidJNI"/> (cached method IDs +
    /// explicit <c>sbyte[]</c>) to avoid AndroidJNIHelper's "byte parameters
    /// obsolete" warning spam — that helper triggers two warnings per PCM frame
    /// on Unity 6.
    ///
    /// Lifecycle:
    ///   using var sink = new NativeAudioSink(48000, 2);
    ///   if (sink.Open()) {
    ///       sink.PushPcm(bytes, 0, length);
    ///       sink.SetVolume(0.8f);
    ///   }
    /// </summary>
    public sealed class NativeAudioSink : IDisposable
    {
        private const string TAG = "[NativeAudioSink]";

        private long _handle;
        private bool _disposed;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaClass _bridgeClass;

        // Cached static method IDs (resolved on first Open()).
        private static IntPtr sAudioCreateId   = IntPtr.Zero; // (III)J
        private static IntPtr sAudioOpenId     = IntPtr.Zero; // (J)Z
        private static IntPtr sAudioCloseId    = IntPtr.Zero; // (J)V
        private static IntPtr sAudioReleaseId  = IntPtr.Zero; // (J)V
        private static IntPtr sAudioPushPcmId  = IntPtr.Zero; // (J[BII)I
        private static IntPtr sAudioSetVolumeId = IntPtr.Zero; // (JF)V
        private static IntPtr sAudioSetMuteId  = IntPtr.Zero; // (JZ)V

        // Scratch buffer reused across PushPcm calls to avoid per-frame allocations.
        private sbyte[] _scratchSByte;
#endif

        public int SampleRate { get; }
        public int Channels { get; }
        public bool IsOpen => _handle != 0 && !_disposed;

        public bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    if (_bridgeClass == null)
                        _bridgeClass = new AndroidJavaClass("com.vrworkspace.hevc.HevcDecoderBridge");
                    return _bridgeClass.CallStatic<bool>("isAvailable");
                }
                catch
                {
                    return false;
                }
#else
                return false;
#endif
            }
        }

        public NativeAudioSink(int sampleRate, int channels)
        {
            SampleRate = sampleRate;
            Channels = channels;
        }

        /// <summary>
        /// Create and open the native sink. Returns false if the native side
        /// failed (no library, device rejected AAudio, etc.).
        /// </summary>
        public bool Open()
        {
            if (_disposed) return false;
            if (_handle != 0) return true;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_bridgeClass == null)
                    _bridgeClass = new AndroidJavaClass("com.vrworkspace.hevc.HevcDecoderBridge");

                EnsureStaticMethodIds();

                // audioCreate(int sampleRate, int channels, int framesPerBuffer) -> long
                jvalue[] createArgs = AllocArgs(3);
                try
                {
                    createArgs[0].i = SampleRate;
                    createArgs[1].i = Channels;
                    createArgs[2].i = 0;
                    _handle = AndroidJNI.CallStaticLongMethod(_bridgeClass.GetRawClass(), sAudioCreateId, createArgs);
                }
                finally { RecycleArgs(createArgs); }

                if (_handle == 0)
                {
                    Debug.LogError($"{TAG} audioCreate returned 0 (sr={SampleRate} ch={Channels})");
                    return false;
                }

                // audioOpen(long handle) -> boolean
                jvalue[] openArgs = AllocArgs(1);
                bool ok;
                try
                {
                    openArgs[0].j = _handle;
                    ok = AndroidJNI.CallStaticBooleanMethod(_bridgeClass.GetRawClass(), sAudioOpenId, openArgs);
                }
                finally { RecycleArgs(openArgs); }

                if (!ok)
                {
                    Debug.LogError($"{TAG} audioOpen failed");
                    jvalue[] relArgs = AllocArgs(1);
                    try
                    {
                        relArgs[0].j = _handle;
                        AndroidJNI.CallStaticVoidMethod(_bridgeClass.GetRawClass(), sAudioReleaseId, relArgs);
                    }
                    finally { RecycleArgs(relArgs); }
                    _handle = 0;
                    return false;
                }

                // Register with the device router so plugging/unplugging headphones
                // (or Bluetooth connect/disconnect) reopens the AAudio stream on
                // the new output device.
                RegisterDeviceRouter(_handle);

                Debug.Log($"{TAG} opened: sr={SampleRate} ch={Channels} handle={_handle}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} Open exception: {ex.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Push raw PCM16 little-endian interleaved samples. Safe to call from
        /// any thread. Returns the number of bytes consumed (== length on
        /// success) or -1 if the frame was dropped.
        /// </summary>
        public int PushPcm(byte[] data, int offset, int length)
        {
            if (_disposed || _handle == 0 || data == null || length <= 0) return -1;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Ensure the current thread is attached to the JavaVM. AttachCurrentThread
                // returns 0 (JNI_OK) on first attach, or JNI_EEXIST (-3) if already attached.
                // Either way the call is cheap and safe to do every push.
                AndroidJNI.AttachCurrentThread();
                if (sAudioPushPcmId == IntPtr.Zero) EnsureStaticMethodIds();

                int copyLen = Math.Min(length, data.Length - offset);
                if (copyLen <= 0) return 0;

                // Grow scratch buffer if needed.
                if (_scratchSByte == null || _scratchSByte.Length < copyLen)
                    _scratchSByte = new sbyte[Math.Max(copyLen, 4096)];

                // Copy the slice we need into the scratch sbyte[] so the JNI side
                // only sees the bytes that matter (no offset-overflow risk).
                int end = offset + copyLen;
                for (int i = offset, j = 0; i < end; i++, j++)
                    _scratchSByte[j] = unchecked((sbyte)data[i]);

                IntPtr jByteArray = AndroidJNI.ToSByteArray(_scratchSByte);
                jvalue[] args = AllocArgs(4);
                try
                {
                    args[0].j = _handle;
                    args[1].l = jByteArray;
                    args[2].i = 0;
                    args[3].i = copyLen;
                    return AndroidJNI.CallStaticIntMethod(_bridgeClass.GetRawClass(), sAudioPushPcmId, args);
                }
                finally
                {
                    RecycleArgs(args);
                    AndroidJNI.DeleteLocalRef(jByteArray);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{TAG} PushPcm exception: {ex.Message}");
                return -1;
            }
#else
            return -1;
#endif
        }

        public void SetVolume(float gain)
        {
            if (_disposed || _handle == 0) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (sAudioSetVolumeId == IntPtr.Zero) EnsureStaticMethodIds();
                jvalue[] args = AllocArgs(2);
                try
                {
                    args[0].j = _handle;
                    args[1].f = Mathf.Clamp(gain, 0f, 4f);
                    AndroidJNI.CallStaticVoidMethod(_bridgeClass.GetRawClass(), sAudioSetVolumeId, args);
                }
                finally { RecycleArgs(args); }
            }
            catch (Exception ex) { Debug.LogWarning($"{TAG} SetVolume: {ex.Message}"); }
#endif
        }

        public void SetMute(bool muted)
        {
            if (_disposed || _handle == 0) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (sAudioSetMuteId == IntPtr.Zero) EnsureStaticMethodIds();
                jvalue[] args = AllocArgs(2);
                try
                {
                    args[0].j = _handle;
                    args[1].z = muted;
                    AndroidJNI.CallStaticVoidMethod(_bridgeClass.GetRawClass(), sAudioSetMuteId, args);
                }
                finally { RecycleArgs(args); }
            }
            catch (Exception ex) { Debug.LogWarning($"{TAG} SetMute: {ex.Message}"); }
#endif
        }

        public void Close()
        {
            if (_disposed || _handle == 0) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            UnregisterDeviceRouter(_handle);
            try
            {
                if (sAudioCloseId == IntPtr.Zero) EnsureStaticMethodIds();
                jvalue[] args = AllocArgs(1);
                try
                {
                    args[0].j = _handle;
                    AndroidJNI.CallStaticVoidMethod(_bridgeClass.GetRawClass(), sAudioCloseId, args);
                }
                finally { RecycleArgs(args); }
            }
            catch (Exception ex) { Debug.LogWarning($"{TAG} Close: {ex.Message}"); }
#endif
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != 0)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                UnregisterDeviceRouter(_handle);
                try
                {
                    if (sAudioReleaseId == IntPtr.Zero) EnsureStaticMethodIds();
                    jvalue[] args = AllocArgs(1);
                    try
                    {
                        args[0].j = _handle;
                        AndroidJNI.CallStaticVoidMethod(_bridgeClass.GetRawClass(), sAudioReleaseId, args);
                    }
                    finally { RecycleArgs(args); }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{TAG} Dispose release: {ex.Message}");
                }
#endif
                _handle = 0;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void EnsureStaticMethodIds()
        {
            if (_bridgeClass == null)
                _bridgeClass = new AndroidJavaClass("com.vrworkspace.hevc.HevcDecoderBridge");
            if (sAudioCreateId != IntPtr.Zero) return;

            // CRITICAL: use GetRawClass() (jclass), NOT GetRawObject()->GetObjectClass().
            // For static methods, GetStaticMethodID takes a jclass pointer directly.
            // GetRawObject() on an AndroidJavaClass returns IntPtr.Zero which crashes JNI.
            IntPtr classRef = _bridgeClass.GetRawClass();
            try
            {
                sAudioCreateId    = AndroidJNI.GetStaticMethodID(classRef, "audioCreate",    "(III)J");
                sAudioOpenId      = AndroidJNI.GetStaticMethodID(classRef, "audioOpen",      "(J)Z");
                sAudioCloseId     = AndroidJNI.GetStaticMethodID(classRef, "audioClose",     "(J)V");
                sAudioReleaseId   = AndroidJNI.GetStaticMethodID(classRef, "audioRelease",   "(J)V");
                sAudioPushPcmId   = AndroidJNI.GetStaticMethodID(classRef, "audioPushPcm",   "(J[BII)I");
                sAudioSetVolumeId = AndroidJNI.GetStaticMethodID(classRef, "audioSetVolume", "(JF)V");
                sAudioSetMuteId   = AndroidJNI.GetStaticMethodID(classRef, "audioSetMute",   "(JZ)V");
                Debug.Log($"{TAG} JNI static method IDs cached");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} EnsureStaticMethodIds failed: {ex.Message}");
                throw;
            }
            // NOTE: do NOT call DeleteLocalRef on a jclass obtained via GetRawClass() —
            // the AndroidJavaClass owns that reference and freeing it here will leave
            // a dangling pointer for subsequent JNI calls.
        }

        private static jvalue[] AllocArgs(int count)
        {
            if (_scratchArgsCache == null || _scratchArgsCache.Length < count)
                _scratchArgsCache = new jvalue[Math.Max(count, 4)];
            // Clear the slots we will use so we never read stale data.
            for (int i = 0; i < count; i++) _scratchArgsCache[i] = default;
            return _scratchArgsCache;
        }

        private static void RecycleArgs(jvalue[] args) { /* pool reused above */ }

        [ThreadStatic] private static jvalue[] _scratchArgsCache;

        // ---- Device router wiring ----
        // The router lives in Java (NativeAudioDeviceRouter.java). It listens to
        // AudioManager.OnAudioDeviceChangeListener and calls HevcDecoderBridge
        // .audioRestart(handle) when headphones / Bluetooth / USB DAC appear or
        // disappear. We invoke the router through AndroidJavaObject so we don't
        // add a new JNI native method just for plumbing.
        private static AndroidJavaObject _routerClass;

        private static void RegisterDeviceRouter(long handle)
        {
            Debug.Log($"{TAG} RegisterDeviceRouter(handle={handle})");
            try
            {
                if (_routerClass == null)
                    _routerClass = new AndroidJavaObject("com.vrworkspace.hevc.NativeAudioDeviceRouter");
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var appContext = activity.Call<AndroidJavaObject>("getApplicationContext"))
                {
                    // init(Context) — idempotent, only first call wins.
                    _routerClass.CallStatic("init", appContext);
                    _routerClass.CallStatic("start", handle);
                    Debug.Log($"{TAG} RegisterDeviceRouter OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{TAG} RegisterDeviceRouter failed: {ex.Message}");
            }
        }

        private static void UnregisterDeviceRouter(long handle)
        {
            Debug.Log($"{TAG} UnregisterDeviceRouter(handle={handle})");
            try
            {
                if (_routerClass == null)
                    _routerClass = new AndroidJavaObject("com.vrworkspace.hevc.NativeAudioDeviceRouter");
                _routerClass.CallStatic("stop", handle);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{TAG} UnregisterDeviceRouter failed: {ex.Message}");
            }
        }
#endif
    }
}
