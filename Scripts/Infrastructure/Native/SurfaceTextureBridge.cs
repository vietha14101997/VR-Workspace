using System;
using UnityEngine;

namespace VRWorkspace.Native
{
    /// <summary>
    /// Bridges a Unity-side GL texture to an Android <c>SurfaceTexture</c> +
    /// <c>Surface</c> pair so the MediaCodec decoder can render directly into
    /// our texture (zero-copy path).
    ///
    /// <para>The flow on the Java side is roughly:</para>
    /// <code>
    ///   int texID = Unity-side texture native handle;
    ///   SurfaceTexture st = new SurfaceTexture(texID);
    ///   Surface s = new Surface(st);
    ///   MediaCodec.configure(..., s, ...);
    ///   // each frame:
    ///   AMediaCodec.releaseOutputBufferAtTime(codec, idx, renderNs);
    ///   st.updateTexImage();   // pull decoded frame into Unity texture
    /// </code>
    ///
    /// <para><b>GC safety:</b> the Java <c>SurfaceTexture</c> and <c>Surface</c>
    /// are AndroidJNI wrappers around global refs. They must be kept alive
    /// (held in fields) while MediaCodec holds a reference to the Surface,
    /// otherwise the surface's producer (the decoder) will write to a freed
    /// object and crash. This class holds the references and disposes them
    /// only on <see cref="Dispose"/>. Do not create temporary instances.</para>
    /// </summary>
    public sealed class SurfaceTextureBridge : IDisposable
    {
        public const string TAG = "[SurfaceTextureBridge]";

#if UNITY_ANDROID && !UNITY_EDITOR
        // Cached JNI globals — do not collect these.
        private AndroidJavaObject _surfaceTexture;
        private AndroidJavaObject _surface;
        // Explicit JNI global-ref handles for SurfaceTexture and Surface.
        // Even though AndroidJavaObject claims to promote local refs to globals,
        // on Unity 6 + Android API 36 the promotion is not 100% reliable when
        // the Java Surface constructor runs on Unity's GL render thread (the
        // Java GC observes the SurfaceTexture as unreferenced and releases it
        // before the cross-thread JNI call resolves, producing the
        // "SurfaceTexture has already been released" exception).
        // Holding the global ref directly guarantees the Java objects stay
        // alive until we explicitly call DeleteGlobalRef in Dispose().
        private IntPtr _surfaceTextureGlobalRef = IntPtr.Zero;
        private IntPtr _surfaceGlobalRef = IntPtr.Zero;
#endif

        private IntPtr _nativeTexturePtr;
        private int _width;
        private int _height;
        private bool _disposed;
        // CRITICAL: hold a strong reference to the Unity Texture that owns the
        // GL handle backing SurfaceTexture. If Unity GC collects the Texture,
        // its GL handle is deleted, which makes SurfaceTexture's native ptr
        // dangling and MediaCodec crashes when it tries to render into it.
        // The ctor captures this so the bridge owns the lifetime of the texture.
        private Texture _textureRef;
        private Texture _componentTexture; // Unity-side wrapper for the OES texture

        public IntPtr NativeTexturePtr => _nativeTexturePtr;
        public int Width => _width;
        public int Height => _height;
        public bool IsAlive => !_disposed && _nativeTexturePtr != IntPtr.Zero
#if UNITY_ANDROID && !UNITY_EDITOR
            && _surfaceTexture != null
            && _surfaceTextureGlobalRef != IntPtr.Zero
#endif
            ;

        /// <summary>
        /// Returns the Unity-side <see cref="Texture"/> that the panel should
        /// bind. The texture's native ptr is the same GL handle MediaCodec writes
        /// into (via SurfaceTexture). Reading it in a Material sampler yields the
        /// most recently decoded frame.
        /// </summary>
        public Texture GetComponentTexture()
        {
            if (_componentTexture == null && _nativeTexturePtr != IntPtr.Zero)
            {
                // Wrap the existing native GL handle in a Unity Texture. This
                // doesn't allocate new GPU memory — it shares MediaCodec's
                // texture as its backing storage.
                _componentTexture = Texture2D.CreateExternalTexture(
                    _width, _height, TextureFormat.RGBA32, false, false,
                    _nativeTexturePtr);
                if (_componentTexture != null)
                    _componentTexture.wrapMode = TextureWrapMode.Clamp;
            }
            return _componentTexture;
        }

        public SurfaceTextureBridge(Texture externalTexture)
        {
            if (externalTexture == null)
                throw new ArgumentNullException(nameof(externalTexture));

            // ─── Pin the source Texture so Unity cannot GC it out from under us. ───
            // When the Texture's C# wrapper is collected, its GL handle is freed
            // and SurfaceTexture's stored native ptr becomes a dangling pointer.
            // The bridge owns the lifetime from this point until Dispose().
            _textureRef = externalTexture;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Get the native GL texture handle and construct SurfaceTexture from it.
            // The Android side will bind subsequent frame writes to this texture.
            _nativeTexturePtr = externalTexture.GetNativeTexturePtr();
            if (_nativeTexturePtr == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Texture has no native texture pointer. " +
                    "Use a Texture2D with format=RGBA32 / mipmap=false / linear=true, " +
                    "or a RenderTexture with enableRandomWrite=false.");

            // Cache dimensions for GetComponentTexture().
            // Texture2D / RenderTexture exposes width/height; fall back to 1920×1080
            // only if the texture has not yet been allocated (shouldn't happen here).
            _width  = externalTexture.width  > 0 ? externalTexture.width  : 1920;
            _height = externalTexture.height > 0 ? externalTexture.height : 1080;

            _surfaceTexture = new AndroidJavaObject("android.graphics.SurfaceTexture", _nativeTexturePtr);
            // Promote to a hard JNI global ref so Java GC cannot release the
            // SurfaceTexture while MediaCodec and Unity WebRTC still need it.
            // GetRawObject() returns the underlying jobject (which itself is a
            // JNI local in Unity's AndroidJavaObject impl), so we promote it.
            IntPtr stLocal = _surfaceTexture.GetRawObject();
            if (stLocal != IntPtr.Zero)
            {
                _surfaceTextureGlobalRef = AndroidJNI.NewGlobalRef(stLocal);
                AndroidJNI.DeleteLocalRef(stLocal);
            }

            _surface = new AndroidJavaObject("android.view.Surface", _surfaceTexture);
            // Same global-ref promotion for the Surface object.
            IntPtr sLocal = _surface.GetRawObject();
            if (sLocal != IntPtr.Zero)
            {
                _surfaceGlobalRef = AndroidJNI.NewGlobalRef(sLocal);
                AndroidJNI.DeleteLocalRef(sLocal);
            }

            Debug.Log($"{TAG} created Surface+SurfaceTexture at native ptr=0x{_nativeTexturePtr:X} ({_width}x{_height}); " +
                      $"globalRefs st=0x{_surfaceTextureGlobalRef:X} s=0x{_surfaceGlobalRef:X}");
#else
            throw new PlatformNotSupportedException(
                "SurfaceTextureBridge is Android-only. Editor / Standalone hosts should keep byte-buffer mode.");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Returns the live Android Surface to pass into MediaCodec.configure.
        /// Always available (returns null on non-Android platforms) so callers
        /// can write platform-agnostic code.
        /// </summary>
        public AndroidJavaObject GetAndroidSurface() => _surface;
#else
        /// <summary>No-op stub on non-Android platforms. Returns null.</summary>
        public AndroidJavaObject GetAndroidSurface() => null;
#endif

        /// <summary>
        /// Pull the latest decoded frame from SurfaceTexture's queue into the
        /// underlying GL texture. Call this on the Unity main thread once per
        /// <c>OnFrameAvailable</c> event (or poll in Update).
        /// </summary>
        public void UpdateTexImage()
        {
            if (_disposed) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _surfaceTexture.Call("updateTexImage"); }
            catch (Exception ex) { Debug.LogWarning($"{TAG} updateTexImage failed: {ex.Message}"); }
#endif
        }

        /// <summary>
        /// Current presentation timestamp in nanoseconds, or 0 if not available.
        /// </summary>
        public long GetTimestampNs()
        {
            if (_disposed) return 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                return _surfaceTexture.Call<long>("getTimestamp");
            }
            catch (Exception)
            {
                return 0;
            }
#else
            return 0;
#endif
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _surface?.Call("release"); } catch { /* ignored */ }
            try { _surfaceTexture?.Call("release"); } catch { /* ignored */ }
            _surface?.Dispose();
            _surfaceTexture?.Dispose();
            _surface = null;
            _surfaceTexture = null;

            // Order matters: delete the JNI global refs AFTER disposing the
            // AndroidJavaObject wrappers so Unity can't have already reclaimed
            // the same underlying ref via its own GC path.
            if (_surfaceGlobalRef != IntPtr.Zero)
            {
                AndroidJNI.DeleteGlobalRef(_surfaceGlobalRef);
                _surfaceGlobalRef = IntPtr.Zero;
            }
            if (_surfaceTextureGlobalRef != IntPtr.Zero)
            {
                AndroidJNI.DeleteGlobalRef(_surfaceTextureGlobalRef);
                _surfaceTextureGlobalRef = IntPtr.Zero;
            }
#endif
            // Drop the source Texture reference last so its GL handle outlives
            // any rendering operation that might still be in flight.
            _textureRef = null;
            _componentTexture = null;
            Debug.Log($"{TAG} disposed");
        }
    }
}
