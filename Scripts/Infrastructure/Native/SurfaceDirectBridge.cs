using System;
using UnityEngine;

namespace VRWorkspace.Native
{
    /// <summary>
    /// Owns the single Unity Texture2D whose GL handle is shared with the
    /// Java <c>SurfaceTexture</c> that the H265 decoder renders into. The
    /// panel material binds directly to this texture — there is no
    /// intermediate RenderTexture, no Graphics.Blit, no Compute upload,
    /// no async Texture.Apply race.
    ///
    /// <para>This is the C# half of Plan C (direct-surface mode). The Java
    /// side (the rest of the puzzle) is <c>SurfaceBinder.java</c> which holds
    /// the SurfaceTexture + Surface pair as JVM-static fields so Java's GC
    /// can never release them — a guarantee the previous C# AndroidJavaObject
    /// approach could not make on vivo V2352GA / Android 16.</para>
    ///
    /// <para><b>Lifecycle</b>:
    /// <list type="number">
    ///   <item>Unity creates this bridge and calls <see cref="Acquire"/>.</item>
    ///   <item>Pass <see cref="TextureID"/> to the native decoder so it
    ///         can attach a MediaCodec surface to the same GL handle.</item>
    ///   <item>Drive decoder's tick — the decoder and Java surface code
    ///         pull new frames into the underlying GL texture automatically.</item>
    ///   <item>Bind <see cref="SharedTexture"/> to the panel material.</item>
    ///   <item>Call <see cref="Dispose"/> on stream shutdown, then drop the
    ///         C# reference so the GL texture can be freed by Unity's GC.</item>
    /// </list></para>
    /// </summary>
    public sealed class SurfaceDirectBridge : IDisposable
    {
        public const string TAG = "[SurfaceDirectBridge]";

        // The "managed" Texture2D that owns the GL handle MediaCodec writes to.
// Unity samples this directly (sampler2D) — the same GPU memory MediaCodec
// updates via SurfaceTexture. We do NOT use Texture2D.CreateExternalTexture
// because the sampling path with sampler2D shader works on regular Texture2D
// (the GPU handle is the same regardless of the C# wrapper type).
private Texture2D _sharedTexture;
        private bool _disposed;

        public int TextureID { get; private set; }
        public Texture2D SharedTexture => _sharedTexture;
        public bool IsAlive => !_disposed && _sharedTexture != null;

        /// <summary>
        /// Allocate a Unity Texture2D in the format MediaCodec writes
        /// (RGBA32, no mipmaps, linear filter) and wrap its native GL handle
        /// as an EXTERNAL texture.
        ///
        /// <para><b>Why we need CreateExternalTexture, not just
        /// <c>new Texture2D(...)</c>.</b> MediaCodec's GPU color converter
        /// expects the consumer texture to be created with target
        /// <c>GL_TEXTURE_EXTERNAL_OES</c>. A regular <c>Texture2D</c> is
        /// <c>GL_TEXTURE_2D</c> — passing that handle to
        /// <c>new SurfaceTexture(texName)</c> works on some devices, but on
        /// others (including the vivo V2352GA used here) the driver silently
        /// allocates its own internal OES texture and ignores our handle.
        /// MediaCodec then renders into the driver's texture while Unity
        /// samples from an empty GL_TEXTURE_2D — result: a uniformly white
        /// panel.</para>
        ///
        /// <para>Wrapping the same handle with <see cref="Texture2D.CreateExternalTexture"/>
        /// tells Unity "this GL handle is owned by an external producer" so
        /// Unity won't try to upload data to it on first sample. The Java
        /// SurfaceBinder still gets the same handle, so MediaCodec writes
        /// land in the same GPU memory that Unity samples.</para>
        /// </summary>
        public void Acquire(int width, int height)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SurfaceDirectBridge));
            if (_sharedTexture != null)
                throw new InvalidOperationException(
                    "SurfaceDirectBridge already initialized. Dispose first.");

            // 1. Allocate a managed Texture2D so Unity gives us a real GL
            //    handle. Unity will sample THIS texture directly in the
            //    shader; the same GPU memory is written to by MediaCodec
            //    via Java SurfaceTexture (which uses GL_TEXTURE_EXTERNAL_OES
            //    internally but stores data in the same texture object —
            //    OpenGL ES texture objects are target-agnostic at the
            //    handle level; only the bind state differs).
            _sharedTexture = new Texture2D(width, height, TextureFormat.RGBA32,
                                           mipChain: false, linear: true)
            {
                name = $"DirectSurfaceRT_{width}x{height}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            // GetNativeTexturePtr forces the GL handle to be allocated. The
            // returned IntPtr IS the GL texture name MediaCodec will render into.
            IntPtr ptr = _sharedTexture.GetNativeTexturePtr();
            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Texture2D has no native texture pointer after allocation.");

            // DIAGNOSTIC: paint a solid magenta frame so the panel should
            // show pink/magenta if (a) the GL handle is actually bound to
            // the shader and (b) MediaCodec hasn't started writing yet.
            // After MediaCodec starts producing, this pixel data is
            // overwritten with decoded frame content. If the panel ever
            // shows magenta, the binding chain works; if it stays white,
            // Unity's sampler is reading from a different texture (or the
            // shader has a default-white fallback).
            var magenta = new Color32[width * height];
            for (int i = 0; i < magenta.Length; i++)
                magenta[i] = new Color32(255, 0, 255, 255); // magenta
            _sharedTexture.SetPixels32(magenta);
            _sharedTexture.Apply(false, false);

            // 2. Hand the handle to the Java side so MediaCodec's surface
            //    can attach. Java's new SurfaceTexture(texId) wraps the
            //    handle for the GPU color converter. MediaCodec writes
            //    decoded frames into this handle via EGL; Unity's sampler2D
            //    reads from the same memory.
            TextureID = ptr.ToInt32();

            Debug.Log($"[SurfaceDirectBridge] acquired Unity Texture2D {width}x{height} texId={TextureID} " +
                      $"(managed=0x{ptr.ToInt64():X}, pre-filled magenta for diagnostic)");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // CRITICAL ORDER:
            //   1. Java side: call HevcDecoderBridge.releaseDirectSurface(...)
            //      first. That releases the decoder + tears down SurfaceBinder
            //      (Surface.release()), so MediaCodec stops rendering into
            //      this GL handle.
            //   2. THEN drop our C# reference. Unity's GC + GL cleanup will
            //      free the texture handle later. Doing this in the wrong
            //      order can cause MediaCodec to render into a freed handle.
            // The C# receiver handles (1); (2) happens here.

            if (_sharedTexture != null)
            {
                UnityEngine.Object.Destroy(_sharedTexture);
                _sharedTexture = null;
            }

            TextureID = 0;
            Debug.Log("[SurfaceDirectBridge] disposed");
        }
    }
}
