using System;
using Concentus;
using Concentus.Enums;
using UnityEngine;
using VRWorkspace.Native;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// H265/H264 stream receiver — Plan C direct-surface mode.
    ///
    /// <para>This is the simplest possible pipeline:
    /// <list type="bullet">
    ///   <item>One Unity Texture2D whose GL handle MediaCodec writes to.</item>
    ///   <item>No Texture2D.Apply async upload, no Blit, no Compute dispatch,
    ///         no mipmap chain. MediaCodec's GPU color converter renders directly
    ///         into the texture we sample. Producer and consumer share the
    ///         same memory region.</item>
    ///   <item>Java's <see cref="SurfaceBinder"/> holds the SurfaceTexture and
    ///         Surface as JVM-static fields so Java GC cannot release them
    ///         (which was the cause of the previous Surface-mode crash on
    ///         vivo V2352GA / Android 16).</item>
    /// </list></para>
    ///
    /// <para>Per-frame flow on Unity main thread:
    /// <code>
    ///   WebRTC RTCRtpReceiver
    ///     └─► OnEncodedFrameReceived()              (background thread)
    ///              └─► HevcDecoderPlugin.PushEncodedFrame()
    ///                       └─► Tick() polls decoded frames
    ///                                └─► decoder.TickFrame()
    ///                                          └─► SurfaceTexture.updateTexImage() (Java)
    ///                                                  └─► OnTextureReady(sharedTexture)
    /// </code></para>
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
        private bool _flipY = true;

        private static readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // ── Shared texture exposed by the decoder (Plan C) ──
        // The decoder allocates the GL_TEXTURE_EXTERNAL_OES handle on the
        // Java side, wraps it via Texture2D.CreateExternalTexture, and
        // returns it as SharedSurfaceTexture. We hand this directly to the
        // panel material — no intermediate RenderTexture / Blit needed.
        private Texture2D _sharedTexture => _decoder != null ? _decoder.SharedSurfaceTexture : null;

        // ─── Corruption detection (Y-plane luminance oscillation) ───
        // Reserved for future use if we hook a fallback byte-buffer path.
        // In direct-surface mode the luminance data is on GPU only, so we
        // rely on host-side scene-change heuristics (host inserts IDR on
        // user activity) rather than CPU-side luma thresholds.
        private float _prevAvgLuminance = -1f;

        // Stats
        public long EncodedFramesReceived { get; private set; }
        public long DecodedFrameCount     => _decodedCount;
        private long _decodedCount;

        public volatile bool IsDesktopIdle;

        // Stall detector state — used to nudge the host for a keyframe.
        private int _noFrameTicks;
        private bool _keyframeRequested;
        private long _lastEncodedCountForStall;

        private const int KEYFRAME_NUDGE_THRESHOLD = 45;   // ~0.75s @ 60Hz
        private const int KEYFRAME_STALL_THRESHOLD  = 90;   // ~1.5s

        // ── IDR-age watchdog ──
        // Bug: tab-switch on the host produces P-frame-only updates for up to GOP seconds.
        // P-frames reference the OLD keyframe (e.g. Messenger), so the client decoder
        // reconstructs "Messenger layout + YouTube video area" until the next IDR arrives.
        //
        // PRIMARY FIX is now on the host: when client sends input (Tab / click / Ctrl-key),
        // the host proactively forces an IDR within ~1 frame. So this watchdog is only a
        // safety net for cases where the host missed an input (DC packet loss, reconnect
        // after desync, etc.) — it's intentionally relaxed so it doesn't spam the host
        // with `request_keyframe` when input-driven IDRs are already arriving on time.
        //
        // BUGFIX: 2500ms was too aggressive for IDLE desktop scenarios. With AMF HEVC's
        // GOP_SIZE configured in FRAMES (not seconds), an idle track encoding at 2 fps
        // gets an IDR only every ~5 seconds (60 frames at 2fps). The watchdog at 2.5s
        // would fire BETWEEN actual IDRs → false alarm → request_keyframe storm.
        // Bumped to 5000ms so idle tracks don't trigger spurious requests.
        private long _lastIdrReceiveMs;
        private long _lastIdrRequestMs;
        private const int IDR_STALE_AGE_MS       = 5000;  // request IDR if none in 5s (was 2.5s)
        private const int IDR_REQUEST_COOLDOWN_MS = 3000;  // throttle to <= 1/3s (was 2s)

        // ──────────── Events ────────────
        public event Action<int> OnFirstFrameDecoded;
        public event Action<int> OnKeyframeNeeded;
        /// <summary>No-op in surface mode; kept for API compat.</summary>
#pragma warning disable CS0067
        public event Action<int> OnCorruptionDetected;
#pragma warning restore CS0067
        /// <summary>Fires on Unity main thread each time a frame is drained.
        /// The Texture argument is the same instance across frames; bind it
        /// to the panel material directly.</summary>
        public event Action<int, Texture> OnTextureReady;

        public H265StreamReceiver(int monitorIndex, int width, int height, bool isH264 = false)
        {
            MonitorIndex = monitorIndex;
            Width        = width;
            Height       = height;
            IsH264       = isH264;
        }

        // ──────────── Lifecycle ────────────

        public bool Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(H265StreamReceiver));
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!HevcDecoderPlugin.IsAvailable())
            {
                Debug.LogError($"{TAG} PC{MonitorIndex} HEVC hardware decoder not available");
                return false;
            }
#endif
            // The decoder allocates its own GL_TEXTURE_EXTERNAL_OES handle on
            // the Java side (Android SurfaceTexture requires OES-target texture
            // objects; a Unity Texture2D handle is GL_TEXTURE_2D and silently
            // fails to bind as OES on Adreno GPUs). The decoder wraps the OES
            // handle via Texture2D.CreateExternalTexture so the panel can
            // sample it with sampler2D — the same GPU memory MediaCodec writes.
            _decoder = new HevcDecoderPlugin();
            bool ok = _decoder.StartDirectSurface(Width, Height, IsH264);
            if (!ok)
            {
                Debug.LogError($"{TAG} PC{MonitorIndex} Decoder (surface mode) init failed {Width}x{Height}");
                _decoder.Dispose();
                _decoder = null;
                return false;
            }
            _initialized = true;
            int oesHandle = 0;
            try { oesHandle = (_decoder.SharedSurfaceTexture != null) ? _decoder.SharedSurfaceTexture.GetNativeTexturePtr().ToInt32() : 0; } catch { }
            Debug.Log($"[H265Receiver] PIPELINE=DIRECT_SURFACE_PLAN_C texId={oesHandle} {Width}x{Height} build={Application.buildGUID}");
            return true;
        }

        // ──────────── Encoded Frame Input ────────────

        public void OnEncodedFrameReceived(byte[] encodedData, bool isKeyFrame, long presentationTimeUs = 0)
        {
            if (!_initialized || _disposed || _decoder == null) return;
            if (encodedData == null || encodedData.Length == 0) return;
            EncodedFramesReceived++;

            const int BUFFER_FLAG_KEY_FRAME = 1;
            const int BUFFER_FLAG_CODEC_CONFIG = 16;

            int flags = 0;
            if (isKeyFrame) flags |= BUFFER_FLAG_KEY_FRAME;

            // Record IDR arrival time for the IDR-age watchdog (see _lastIdrReceiveMs).
            // We update ONLY on the first IDR after bootstrap or after a long IDR gap —
            // duplicate IDRs from a keyframe burst (host DC resync) shouldn't reset the
            // watchdog clock prematurely.
            if (isKeyFrame)
            {
                long nowMs = NowMs();
                if (_lastIdrReceiveMs == 0 || nowMs - _lastIdrReceiveMs > 200)
                    _lastIdrReceiveMs = nowMs;
            }

            bool pushed = _decoder.PushEncodedFrame(encodedData,
                presentationTimeUs > 0 ? presentationTimeUs : GetTimestampUs(),
                isKeyFrame);
            if (isKeyFrame && EncodedFramesReceived < 20)
                Debug.Log($"{TAG} PC{MonitorIndex} Keyframe pushed {(pushed ? "OK" : "FAILED")}: {encodedData.Length} bytes");
        }

        // ──────────── Tick (main thread) ────────────

        public void Tick()
        {
            if (!_initialized || _disposed || _decoder == null) return;

            int drained = _decoder.TickFrame(0);   // non-blocking
            // Track decoded count for stall detection / logs.
            for (int i = 0; i < drained; i++)
            {
                _decodedCount++;
                if (_decodedCount == 1) OnFirstFrameDecoded?.Invoke(MonitorIndex);
                if (_decodedCount % 300 == 0 || _decodedCount < 10)
                    Debug.Log($"{TAG} PC{MonitorIndex} Frame drained: #{_decodedCount}");
            }

            if (drained > 0)
            {
                // Frame arrived — fire texture-ready immediately. Panel binds to
                // the SHARED texture (single GL handle, no ping-pong needed).
                OnTextureReady?.Invoke(MonitorIndex, _sharedTexture);
                _noFrameTicks = 0;
                _keyframeRequested = false;

                // ── IDR-age watchdog ──
                // If we have been receiving frames for a while but haven't seen an
                // IDR in IDR_STALE_AGE_MS, the host's GOP-pacing or scenecut may
                // be stuck. Force an IDR to break out of a possible "P-frame referencing
                // stale keyframe" loop (the original tab-switch bug).
                if (_lastIdrReceiveMs > 0 && _decodedCount > 0)
                {
                    long nowMs = NowMs();
                    if (nowMs - _lastIdrReceiveMs > IDR_STALE_AGE_MS &&
                        nowMs - _lastIdrRequestMs > IDR_REQUEST_COOLDOWN_MS)
                    {
                        _lastIdrRequestMs = nowMs;
                        Debug.LogWarning($"{TAG} PC{MonitorIndex} No IDR in {(nowMs - _lastIdrReceiveMs) / 1000f:F1}s — requesting keyframe (tab-switch safeguard)");
                        OnKeyframeNeeded?.Invoke(MonitorIndex);
                    }
                }

                return;
            }

            // ── Stall detection ──
            if (IsDesktopIdle) { _noFrameTicks = 0; _keyframeRequested = false; return; }
            _noFrameTicks++;
            if (_decodedCount > 0 && EncodedFramesReceived == _lastEncodedCountForStall)
            {
                _noFrameTicks = 0;
                _keyframeRequested = false;
                return;
            }
            _lastEncodedCountForStall = EncodedFramesReceived;

            if (_noFrameTicks == KEYFRAME_NUDGE_THRESHOLD && !_keyframeRequested && _decodedCount > 0)
            {
                _keyframeRequested = true;
                Debug.LogWarning($"{TAG} PC{MonitorIndex} Short stall {(_noFrameTicks / 60f):F1}s — nudging keyframe");
                OnKeyframeNeeded?.Invoke(MonitorIndex);
            }
            if (_noFrameTicks == KEYFRAME_STALL_THRESHOLD && _decodedCount > 0)
            {
                Debug.LogWarning($"{TAG} PC{MonitorIndex} Stall 1.5s — flushing decoder");
                _decoder.Flush();
                _keyframeRequested = false;
                OnKeyframeNeeded?.Invoke(MonitorIndex);
            }
        }

        public void Flush()
        {
            if (_initialized && !_disposed) _decoder?.Flush();
        }

        public void SetFlipY(bool flip) { _flipY = flip; }
        public void SetFullRange(bool fr) { /* surface mode ignores */ }

        // ──────────── IDisposable ────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Dispose the decoder last — it owns both the Java Surface +
            // SurfaceTexture refs and the externally-wrapped Unity Texture2D.
            _decoder?.Dispose();
            _decoder = null;

            _initialized = false;
            Debug.Log($"{TAG} PC{MonitorIndex} Disposed (decoded={_decodedCount})");
        }

        // ──────────── Helpers ────────────

        private static long GetTimestampUs()
            => _stopwatch.ElapsedTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

        private static long NowMs()
            => _stopwatch.ElapsedMilliseconds;

        public override string ToString()
            => $"H265StreamReceiver PC{MonitorIndex} {Width}x{Height} {(IsH264 ? "H264" : "HEVC")} directSurface";
    }
}
