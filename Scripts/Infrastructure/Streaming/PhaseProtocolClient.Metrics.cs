using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    public partial class PhaseProtocolClient
    {
        #region Latency Control

        // === Streaming Pause State ===
        // When paused, server stops sending frames but connection is maintained.
        // Suppress frame gap/freeze detection to avoid log spam.
        private volatile bool _isStreamingPaused = false;

        /// <summary>
        /// Indicates if streaming is paused (server not sending frames).
        /// </summary>
        public bool IsStreamingPaused => _isStreamingPaused;

        // === WiFi Tolerance Configuration ===
        // WiFi connections have higher jitter and occasional packet bursts
        // Adjust thresholds to avoid false positives on WiFi
        private bool _isWiFiConnection = false;

        // Base thresholds (for LAN/Ethernet - low latency, stable connection)
        private const float BASE_FRAME_GAP_THRESHOLD_MS = 500f;
        private const float BASE_MONITOR_DRIFT_THRESHOLD_MS = 500f;
        private const int BASE_DECODER_FREEZE_THRESHOLD_FRAMES = 90;
        private const float BASE_DECODER_FREEZE_CHECK_INTERVAL_MS = 3000f;
        private const float BASE_PREVENTIVE_KEYFRAME_INTERVAL_SECONDS = 15f;

        // Proactive keyframe on packet loss spike (WiFi resilience)
        private float _prevPacketLoss = 0f;
        private DateTime _lastProactiveKeyframeTime = DateTime.MinValue;

        // WiFi thresholds (more tolerant - allow for jitter and burst loss)
        private const float WIFI_FRAME_GAP_THRESHOLD_MS = 800f;             // 0.8s for WiFi (was 1.5s — faster gap detection)
        private const float WIFI_MONITOR_DRIFT_THRESHOLD_MS = 600f;        // 0.6s drift allowed (was 1s)
        private const int WIFI_DECODER_FREEZE_THRESHOLD_FRAMES = 60;       // ~2s at 30fps (was 120/~4s — faster freeze detection)
        private const float WIFI_DECODER_FREEZE_CHECK_INTERVAL_MS = 3000f; // Check every 3s (was 5s)
        private const float WIFI_PREVENTIVE_KEYFRAME_MIN_INTERVAL = 1.5f;  // Minimum 1.5s (was 3s — faster natural recovery)
        private const float WIFI_PREVENTIVE_KEYFRAME_MAX_INTERVAL = 5f;   // Maximum 5s on stable WiFi (was 8s)

        // Active thresholds - computed properties based on connection type
        private float FrameGapThresholdMs => _isWiFiConnection ? WIFI_FRAME_GAP_THRESHOLD_MS : BASE_FRAME_GAP_THRESHOLD_MS;
        private float MonitorDriftThresholdMs => _isWiFiConnection ? WIFI_MONITOR_DRIFT_THRESHOLD_MS : BASE_MONITOR_DRIFT_THRESHOLD_MS;
        private int DecoderFreezeThresholdFrames => _isWiFiConnection ? WIFI_DECODER_FREEZE_THRESHOLD_FRAMES : BASE_DECODER_FREEZE_THRESHOLD_FRAMES;
        private float DecoderFreezeCheckIntervalMs => _isWiFiConnection ? WIFI_DECODER_FREEZE_CHECK_INTERVAL_MS : BASE_DECODER_FREEZE_CHECK_INTERVAL_MS;
        private float PreventiveKeyframeIntervalSeconds => _isWiFiConnection ? CalculateAdaptiveKeyframeInterval() : BASE_PREVENTIVE_KEYFRAME_INTERVAL_SECONDS;

        // Latency tracking for skip_to_live with adaptive cooldown
        private DateTime _lastSkipToLiveTime = DateTime.MinValue;
        private int _consecutiveSkipCount = 0;  // Track consecutive skips for adaptive cooldown
        private DateTime _skipCountResetTime = DateTime.MinValue;
        private const float BASE_SKIP_COOLDOWN_SECONDS = 2.0f;    // Base cooldown
        private const float MAX_SKIP_COOLDOWN_SECONDS = 10.0f;    // Max cooldown when spamming
        private const int SKIP_COUNT_THRESHOLD = 5;               // After 5 skips in 30s, increase cooldown

        private float SkipToLiveCooldownSeconds
        {
            get
            {
                // Reset counter if 30 seconds have passed since last skip
                if ((DateTime.UtcNow - _skipCountResetTime).TotalSeconds > 30.0)
                {
                    _consecutiveSkipCount = 0;
                }
                // Adaptive cooldown: increase if we're skipping too frequently
                if (_consecutiveSkipCount >= SKIP_COUNT_THRESHOLD)
                {
                    // Gradually increase cooldown up to max
                    float multiplier = 1 + (_consecutiveSkipCount - SKIP_COUNT_THRESHOLD) * 0.5f;
                    return Math.Min(MAX_SKIP_COOLDOWN_SECONDS, BASE_SKIP_COOLDOWN_SECONDS * multiplier);
                }
                return BASE_SKIP_COOLDOWN_SECONDS;
            }
        }

        // === RTT-based aggressive skip thresholds ===
        // When RTT is extremely high, client is falling behind and needs immediate recovery
        private const double RTT_EXTREME_THRESHOLD_MS = 5000.0; // 5s RTT = bypass cooldown, skip immediately
        private const double RTT_HIGH_THRESHOLD_MS = 3000.0;    // 3s RTT = trigger skip (with cooldown)
        private const float EFFECTIVE_FPS_CRISIS_RATIO = 0.25f; // Skip if FPS < 25% of target
        private const float PACKET_LOSS_CRISIS_THRESHOLD = 0.20f; // Skip if >20% packet loss
        private DateTime _lastExtremeSkipTime = DateTime.MinValue;
        private const float ExtremeSkipCooldownSeconds = 5.0f; // Don't spam extreme skips

        // === Stable Connection Thresholds ===
        // Apply warmup period to ALL connections to prevent false skip_to_live during stream initialization.
        // Initial metrics (avgRtt, jitter) are often wrong due to stale data or calculation artifacts.
        // Main purpose: prevent false skip_to_live triggers that waste GPU on unnecessary keyframes
        private const float STREAMING_WARMUP_PERIOD_SECONDS = 10.0f;  // Don't check latency for first 10s of stream
        private const int STABLE_MIN_FRAMES_BEFORE_CRISIS = 600;      // ~10 seconds at 60fps before FPS crisis check
        private const float STABLE_FRAME_GAP_THRESHOLD_MS = 3000f;    // 3s gap for stable connections (very tolerant)
        private const float STABLE_MONITOR_DRIFT_THRESHOLD_MS = 2000f; // 2s drift allowed for stable connections
        private const double STABLE_CONNECTION_RTT_THRESHOLD = 100.0; // Consider connection "stable" if RTT < 100ms
        private DateTime _streamingStartTime = DateTime.MinValue; // Track when streaming actually started

        // Decoder freeze detection - compare server frame count with client rendered frames
        // This catches cases where WebRTC texture is valid but decoder has stopped producing new pixels
        private long _lastServerFrame = 0;           // currentFrame from last frameTiming message
        private long _lastServerFrameTime = 0;       // When we received _lastServerFrame
        private int _clientFramesAtLastCheck = 0;    // Total client rendered frames at check time
        private int _realFramesAtLastCheck = 0;      // Real texture pointer changes (not fallback)

        // Preventive keyframe request for fallback mode
        // When texture pointer detection doesn't work, we can't tell if decoder is frozen
        // So we periodically request keyframes to "unstick" a potentially frozen decoder
        private DateTime _lastPreventiveKeyframeTime = DateTime.MinValue;
        private int _freezeCount = 0; // Track consecutive freeze detections for graduated response

        // FPS Feedback constants (for adaptive encoding + server-side stall detection)
        private const float FPS_FEEDBACK_INTERVAL_SECONDS = 0.5f;  // Send feedback every 500ms for ≤1s stall detection
        private const float FPS_CHANGE_THRESHOLD = 5.0f;           // Report if FPS differs by 5+ (logging only)
        private const int FPS_WINDOW_FRAMES = 0;                   // Always send, even with 0 frames (critical for stall detection)

        // Quality feedback timing (for adaptive bitrate decisions)
        private DateTime _lastQualityFeedbackTime = DateTime.MinValue;
        private const float QUALITY_FEEDBACK_INTERVAL_SECONDS = 2.0f; // Send comprehensive feedback every 2 seconds
        private int _pollCount = 0; // For occasional logging

        #endregion

        /// <summary>
        /// Event fired when skip_to_live is acknowledged by server.
        /// </summary>
        public event Action OnSkipToLiveAck;

        /// <summary>
        /// Event fired when server adjusts bitrate based on quality feedback.
        /// </summary>
        public event Action<int, int, string> OnBitrateAdjusted; // monitorIndex, bitrateKbps, reason

        /// <summary>
        /// Event fired when server sends quality recommendation.
        /// </summary>
        public event Action<string, string> OnQualityRecommendation; // recommendation, reason

        /// <summary>
        /// Check for latency issues and request skip_to_live if needed.
        /// Called from PollTextures to detect frame gaps and monitor drift.
        ///
        /// Detection modes (prioritized):
        /// 1. RTT-based: When RTT is extremely high (>5s), skip immediately bypassing cooldown
        /// 2. RTT-based: When RTT is high (>3s), skip with normal cooldown
        /// 3. FPS crisis: When effective FPS < 25% of target, skip
        /// 4. Frame gap: When no frames received for threshold time
        /// 5. Monitor drift: When monitors are out of sync
        /// </summary>
        private void CheckLatencyAndSkip()
        {
            if (!_stateMachine.IsStreaming) return;

            // Skip all latency checks when streaming is paused (server not sending frames)
            if (_isStreamingPaused) return;

            // Initialize streaming start time if not set
            if (_streamingStartTime == DateTime.MinValue)
            {
                _streamingStartTime = DateTime.UtcNow;
                AppLog.Log("[PhaseProtocol] Streaming started - latency checks will begin after warmup period");
            }

            // Calculate time since streaming started
            double streamingDurationSeconds = (DateTime.UtcNow - _streamingStartTime).TotalSeconds;
            bool isInWarmupPeriod = streamingDurationSeconds < STREAMING_WARMUP_PERIOD_SECONDS;

            // Determine connection stability based on current RTT and mode
            // USB mode or low RTT (<100ms) = stable connection
            double currentRtt = _metrics.CurrentPingMs;
            bool isUsbMode = _metrics.IsUsbMode || _isUsbMode;
            bool isStableConnection = isUsbMode || (currentRtt > 0 && currentRtt < STABLE_CONNECTION_RTT_THRESHOLD);

            // === Warmup Period Protection (applies to ALL connections) ===
            // During warmup, only check for extreme RTT (catastrophic failure)
            // This prevents false positives from stale/wrong metrics at stream start
            if (isInWarmupPeriod)
            {
                // Only check for EXTREME RTT during warmup (catastrophic failure, >5 seconds)
                if (currentRtt >= RTT_EXTREME_THRESHOLD_MS)
                {
                    if ((DateTime.UtcNow - _lastExtremeSkipTime).TotalSeconds >= ExtremeSkipCooldownSeconds)
                    {
                        AppLog.LogWarning($"[PhaseProtocol] EXTREME RTT during warmup ({streamingDurationSeconds:F1}s): {currentRtt:F0}ms - skip_to_live");
                        _lastExtremeSkipTime = DateTime.UtcNow;
                        _lastSkipToLiveTime = DateTime.UtcNow;
                        SkipToLiveImmediate(-1);
                    }
                }
                // Skip all other latency checks during warmup
                return;
            }

            // === RTT-based aggressive skip (highest priority) ===
            // When RTT is extremely high, the client is watching frames that are already stale.
            if (currentRtt >= RTT_EXTREME_THRESHOLD_MS)
            {
                // CRITICAL: RTT is 5+ seconds. Client is severely behind.
                if ((DateTime.UtcNow - _lastExtremeSkipTime).TotalSeconds >= ExtremeSkipCooldownSeconds)
                {
                    AppLog.LogWarning($"[PhaseProtocol] EXTREME RTT detected: {currentRtt:F0}ms - IMMEDIATE skip_to_live");
                    _lastExtremeSkipTime = DateTime.UtcNow;
                    _lastSkipToLiveTime = DateTime.UtcNow;
                    SkipToLiveImmediate(-1);
                }
                return;
            }

            // Skip HIGH RTT check for stable connections (USB or low latency WiFi)
            if (!isStableConnection && currentRtt >= RTT_HIGH_THRESHOLD_MS)
            {
                // HIGH RTT: 3-5 seconds. Client is falling behind.
                if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds >= SkipToLiveCooldownSeconds)
                {
                    AppLog.LogWarning($"[PhaseProtocol] HIGH RTT detected: {currentRtt:F0}ms - skip_to_live");
                    SkipToLive(-1);
                }
                return;
            }

            // === Effective FPS crisis detection ===
            // For stable connections: require more frames before triggering (takes longer to stabilize)
            float targetFps = _lastServerTargetFps > 0 ? _lastServerTargetFps : 60f;
            float effectiveFps = _metrics.EffectiveFps;
            int minFramesForCrisis = isStableConnection ? STABLE_MIN_FRAMES_BEFORE_CRISIS : 60;

            if (effectiveFps > 0 && effectiveFps < targetFps * EFFECTIVE_FPS_CRISIS_RATIO)
            {
                int totalRendered = GetTotalRenderedFrames();
                if (totalRendered > minFramesForCrisis)
                {
                    if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds >= SkipToLiveCooldownSeconds)
                    {
                        AppLog.LogWarning($"[PhaseProtocol] FPS CRISIS: {effectiveFps:F1}/{targetFps:F0} fps - skip_to_live");
                        SkipToLive(-1);
                    }
                    return;
                }
            }

            // === Packet loss crisis detection ===
            // Stable connections should have 0% packet loss, so this rarely triggers for them
            float packetLoss = _metrics.AveragePacketLossRate;
            if (packetLoss >= PACKET_LOSS_CRISIS_THRESHOLD)
            {
                if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds >= SkipToLiveCooldownSeconds)
                {
                    AppLog.LogWarning($"[PhaseProtocol] PACKET LOSS CRISIS: {packetLoss:P0} - skip_to_live + keyframe");
                    SkipToLive(-1);
                    RequestKeyframe(-1);
                }
                return;
            }

            // === Frame gap and monitor drift detection ===
            // Use relaxed thresholds for stable connections
            float frameGapThreshold = isStableConnection ? STABLE_FRAME_GAP_THRESHOLD_MS : FrameGapThresholdMs;
            float driftThreshold = isStableConnection ? STABLE_MONITOR_DRIFT_THRESHOLD_MS : MonitorDriftThresholdMs;

            lock (_lock)
            {
                DateTime minFrameTime = DateTime.MaxValue;
                DateTime maxFrameTime = DateTime.MinValue;
                int laggingMonitor = -1;

                foreach (var wrapper in _peerConnections)
                {
                    if (wrapper.LastFrameTime == default) continue;

                    // Frame gap detection (stall)
                    var timeSinceFrame = (DateTime.UtcNow - wrapper.LastFrameTime).TotalMilliseconds;
                    if (timeSinceFrame > frameGapThreshold && wrapper.FrameCount > 10)
                    {
                        // Check if server is still sending frames — if not, desktop is idle (not a stall).
                        // LastNetworkActivityTime is updated when encoded data arrives via DataChannel.
                        var timeSinceNetwork = (DateTime.UtcNow - wrapper.LastNetworkActivityTime).TotalMilliseconds;
                        if (timeSinceNetwork > frameGapThreshold)
                        {
                            // Server not sending data → desktop idle → not a real stall.
                            // Reset frame time to prevent gap from growing forever.
                            wrapper.LastFrameTime = DateTime.UtcNow;
                            continue;
                        }

                        if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds >= SkipToLiveCooldownSeconds)
                        {
                            AppLog.LogWarning($"[PhaseProtocol] PC{wrapper.Index} frame gap {timeSinceFrame:F0}ms (threshold={frameGapThreshold:F0}ms) - skip_to_live");
                        }
                        SkipToLive(wrapper.Index);
                        return;
                    }

                    // Track for drift detection
                    if (wrapper.LastFrameTime < minFrameTime)
                    {
                        minFrameTime = wrapper.LastFrameTime;
                        laggingMonitor = wrapper.Index;
                    }
                    if (wrapper.LastFrameTime > maxFrameTime)
                    {
                        maxFrameTime = wrapper.LastFrameTime;
                    }
                }

                // Monitor drift detection — only when both monitors are actively receiving data.
                // When one monitor is idle (no server data), drift is meaningless.
                if (_peerConnections.Count > 1 && minFrameTime != DateTime.MaxValue && maxFrameTime != DateTime.MinValue)
                {
                    var drift = (maxFrameTime - minFrameTime).TotalMilliseconds;
                    if (drift > driftThreshold && laggingMonitor >= 0)
                    {
                        // Check if the lagging monitor is actually receiving data from server.
                        // If server stopped sending (desktop idle), drift is not a real problem.
                        bool isLaggingIdle = false;
                        var laggingWrapper = _peerConnections.FirstOrDefault(w => w.Index == laggingMonitor);
                        if (laggingWrapper != null)
                        {
                            var timeSinceNetwork = (DateTime.UtcNow - laggingWrapper.LastNetworkActivityTime).TotalMilliseconds;
                            if (timeSinceNetwork > driftThreshold)
                            {
                                // Server not sending data to this monitor → desktop idle → suppress
                                laggingWrapper.LastFrameTime = DateTime.UtcNow;
                                isLaggingIdle = true;
                            }
                        }

                        if (!isLaggingIdle)
                        {
                            if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds >= SkipToLiveCooldownSeconds)
                            {
                                AppLog.LogWarning($"[PhaseProtocol] Monitor drift: PC{laggingMonitor} is {drift:F0}ms behind (threshold={driftThreshold:F0}ms) - skip_to_live");
                            }
                            SkipToLive(laggingMonitor);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Skip to live immediately, bypassing normal cooldown.
        /// Used for critical situations like extreme RTT (>5s).
        /// </summary>
        private async void SkipToLiveImmediate(int monitorIndex)
        {
            if (_ws?.State != System.Net.WebSockets.WebSocketState.Open || !_stateMachine.IsStreaming)
                return;

            try
            {
                string msg = monitorIndex >= 0
                    ? $"{{\"type\":\"skip_to_live\",\"monitor\":{monitorIndex},\"urgent\":true}}"
                    : "{\"type\":\"skip_to_live\",\"urgent\":true}";
                AppLog.LogWarning($"[PhaseProtocol] URGENT skip_to_live (bypassing cooldown)");
                await SendTextAsync(msg);

                // Also request keyframe to ensure clean recovery
                await Task.Delay(50); // Small delay to let server process skip first
                string keyframeMsg = monitorIndex >= 0
                    ? $"{{\"type\":\"request_keyframe\",\"monitorIndex\":{monitorIndex}}}"
                    : "{\"type\":\"request_keyframe\"}";
                await SendTextAsync(keyframeMsg);
                if (VerboseLogging) AppLog.Log($"[PhaseProtocol] Follow-up keyframe request sent");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] SkipToLiveImmediate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate effective FPS and send feedback to server periodically.
        /// Called from PollTextures to enable server-side adaptive encoding.
        /// </summary>
        private void SendFpsFeedbackIfNeeded()
        {
            if (!_stateMachine.IsStreaming) return;

            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    // Skip if not enough time has passed
                    if ((DateTime.UtcNow - wrapper.LastFpsFeedbackSent).TotalSeconds < FPS_FEEDBACK_INTERVAL_SECONDS)
                        continue;

                    // Calculate effective FPS (always send, even with 0 frames for stall detection)
                    double windowSeconds = (DateTime.UtcNow - wrapper.FpsWindowStart).TotalSeconds;
                    if (windowSeconds <= 0) continue;

                    float effectiveFps = (float)(wrapper.RenderedFrameCount / windowSeconds);

                    // Send feedback
                    wrapper.LastFpsFeedbackSent = DateTime.UtcNow;
                    wrapper.LastReportedEffectiveFps = effectiveFps;

                    // Update global metrics with latest effective FPS
                    _metrics.RecordEffectiveFps(effectiveFps);

                    // Use InvariantCulture to ensure decimal separator is always '.' (not ',' on some devices)
                    string fpsStr = effectiveFps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

                    // Include total frames in feedback for pipeline comparison
                    string json = $"{{\"type\":\"fps_feedback\",\"monitorIndex\":{wrapper.Index}," +
                        $"\"effectiveFps\":{fpsStr}," +
                        $"\"renderedFrames\":{wrapper.RenderedFrameCount}," +
                        $"\"totalFrames\":{wrapper.TotalFramesReceived}," +
                        $"\"droppedFrames\":{wrapper.DroppedFrameCount}}}";

                    _ = SendTextAsync(json);

                    // Calculate average FPS since stream start for pipeline comparison
                    double totalSeconds = wrapper.StreamStartTime != DateTime.MinValue
                        ? (DateTime.UtcNow - wrapper.StreamStartTime).TotalSeconds
                        : 0;
                    float avgFps = totalSeconds > 0 ? (float)(wrapper.TotalFramesReceived / totalSeconds) : 0;

                    // TODO: Remove DIAG log after debugging stall recovery
                    if (VerboseLogging) AppLog.Log($"[DIAG:FPS] Mon{wrapper.Index}: {effectiveFps:F1}fps, rendered={wrapper.RenderedFrameCount}, dropped={wrapper.DroppedFrameCount}, total={wrapper.TotalFramesReceived}, window={windowSeconds:F2}s, avg={avgFps:F1}fps");

                    // Reset window
                    ResetFpsWindow(wrapper);
                }
            }
        }

        private void ResetFpsWindow(PCWrapper wrapper)
        {
            wrapper.RenderedFrameCount = 0;
            wrapper.DroppedFrameCount = 0;
            wrapper.FpsWindowStart = DateTime.UtcNow;
        }

        /// <summary>
        /// Send comprehensive quality feedback to server for adaptive bitrate decisions.
        /// Includes: RTT, jitter, packet loss, effective FPS, buffer status, per-monitor frame counts.
        /// Called from PollTextures.
        /// </summary>
        private void SendQualityFeedbackIfNeeded()
        {
            if (!_stateMachine.IsStreaming) return;
            if ((DateTime.UtcNow - _lastQualityFeedbackTime).TotalSeconds < QUALITY_FEEDBACK_INTERVAL_SECONDS) return;

            _lastQualityFeedbackTime = DateTime.UtcNow;
            _pollCount++;

            var culture = System.Globalization.CultureInfo.InvariantCulture;

            // Build monitors array JSON and calculate totals
            var monitorsJson = new StringBuilder();
            monitorsJson.Append("[");
            bool first = true;
            int totalRendered = 0;
            int totalDropped = 0;

            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    if (!first) monitorsJson.Append(",");
                    first = false;

                    monitorsJson.Append($"{{\"index\":{wrapper.Index},");
                    monitorsJson.Append($"\"renderedFrames\":{wrapper.RenderedFrameCount},");
                    monitorsJson.Append($"\"realFrames\":{wrapper.RealFrameCount},");
                    monitorsJson.Append($"\"droppedFrames\":{wrapper.DroppedFrameCount},");
                    monitorsJson.Append($"\"texturePtrWorking\":{(wrapper.TexturePtrDetectionWorking ? "true" : "false")}}}");

                    totalRendered += wrapper.RenderedFrameCount;
                    totalDropped += wrapper.DroppedFrameCount;
                }
            }
            monitorsJson.Append("]");

            // Update metrics with frame counts for buffer status calculation
            _metrics.RecordFrameCounts(totalRendered, totalDropped);

            // Get buffer status description (now considers dropped frames)
            string bufferStatus = _metrics.GetBufferStatusDescription();

            // Build comprehensive feedback JSON
            string feedbackJson = $"{{\"type\":\"quality_feedback\"," +
                $"\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                $"\"rttMs\":{_metrics.CurrentPingMs.ToString("F1", culture)}," +
                $"\"avgRttMs\":{_metrics.AveragePingMs.ToString("F1", culture)}," +
                $"\"jitterMs\":{_metrics.JitterMs.ToString("F1", culture)}," +
                $"\"packetLossRate\":{_metrics.PacketLossRate.ToString("F4", culture)}," +
                $"\"avgPacketLossRate\":{_metrics.AveragePacketLossRate.ToString("F4", culture)}," +
                $"\"effectiveFps\":{_metrics.EffectiveFps.ToString("F1", culture)}," +
                $"\"targetFps\":{_lastServerTargetFps.ToString("F1", culture)}," +
                $"\"frameLatencyMs\":{_metrics.FrameLatencyMs.ToString("F1", culture)}," +
                $"\"bufferStatus\":\"{bufferStatus}\"," +
                $"\"connectionHealth\":{_metrics.HealthScore}," +
                $"\"isWiFi\":{(_isWiFiConnection ? "true" : "false")}," +
                $"\"monitors\":{monitorsJson}}}";

            _ = SendTextAsync(feedbackJson);

            // TODO: Remove DIAG log after debugging stall recovery
            if (VerboseLogging) AppLog.Log($"[DIAG:QF] RTT={_metrics.CurrentPingMs:F0}ms, jitter={_metrics.JitterMs:F1}ms, loss={_metrics.PacketLossRate:P2}, fps={_metrics.EffectiveFps:F1}/{_lastServerTargetFps:F0}, health={_metrics.HealthScore}, buf={bufferStatus}, rendered={totalRendered}, dropped={totalDropped}");
        }

        /// <summary>
        /// Update textures (call from Update loop).
        /// NOTE: LastFrameTime is updated in OnVideoReceived callback, not here.
        /// This method only caches the texture reference and checks for latency issues.
        /// </summary>
        public void PollTextures()
        {
            _pollCount++;

            // Tick H265 Custom Decoders (uploaded textures to GPU)
            // Snapshot to avoid ConcurrentModificationException during fallback disposal
            H265StreamReceiver[] h265Snapshot;
            lock (_lock) { h265Snapshot = _h265Receivers.Values.ToArray(); }
            foreach (var receiver in h265Snapshot)
            {
                receiver.Tick();
            }

            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    try
                    {
                        var track = wrapper.VideoTrack;
                        var tex = track?.Texture;

                        // Debug log periodically (every ~60 polls for first PC only) - disabled by default
                        if (VerboseLogging && wrapper.Index == 0 && _pollCount % 60 == 1)
                        {
                            var timeSinceFrame = wrapper.LastFrameTime != default
                                ? (DateTime.UtcNow - wrapper.LastFrameTime).TotalMilliseconds
                                : -1;
                            AppLog.Log($"[PhaseProtocol] PollTextures PC{wrapper.Index}: " +
                                $"track={(track != null ? "valid" : "null")}, " +
                                $"tex={(tex != null ? $"{tex.width}x{tex.height}" : "null")}, " +
                                $"cached={(wrapper.Texture != null ? "set" : "null")}, " +
                                $"frames={wrapper.FrameCount}, lastFrame={timeSinceFrame:F0}ms ago");
                        }

                        if (tex != null && tex.width > 0)
                        {
                            // WORKAROUND: Unity WebRTC may not trigger OnVideoReceived for every frame
                            // Detect texture change by comparing native texture pointer
                            var currentPtr = tex.GetNativeTexturePtr();
                            if (currentPtr != IntPtr.Zero && currentPtr != wrapper.LastTexturePtr)
                            {
                                // Texture pointer changed - frame was updated internally
                                if (wrapper.LastTexturePtr != IntPtr.Zero)
                                {
                                    // Only count as new frame if we had a previous pointer (not first frame)
                                    wrapper.FrameCount++;
                                    wrapper.RenderedFrameCount++;
                                    wrapper.RealFrameCount++; // Real frame for freeze detection
                                    wrapper.TotalFramesReceived++; // Cumulative counter for pipeline loss calculation
                                    wrapper.TexturePtrDetectionWorking = true; // Detection method confirmed working

                                    // Debug log first 10 frames and every 300 frames after - disabled by default
                                    if (VerboseLogging && (wrapper.FrameCount <= 10 || wrapper.FrameCount % 300 == 0))
                                    {
                                        AppLog.Log($"[PhaseProtocol] PC{wrapper.Index} texture ptr CHANGED: {wrapper.LastTexturePtr:X} -> {currentPtr:X}, frames={wrapper.FrameCount}");
                                    }
                                }
                                else
                                {
                                    if (VerboseLogging)
                                        AppLog.Log($"[PhaseProtocol] PC{wrapper.Index} first texture ptr: {currentPtr:X}");
                                    wrapper.FirstTextureTime = DateTime.UtcNow;
                                }
                                wrapper.LastTexturePtr = currentPtr;
                                wrapper.LastFrameTime = DateTime.UtcNow;
                            }
                            else if (wrapper.LastTexturePtr != IntPtr.Zero && track != null)
                            {
                                // Texture pointer unchanged - check if detection method is working
                                var timeSinceFirstTexture = DateTime.UtcNow - wrapper.FirstTextureTime;

                                // IMPORTANT: Grace period (2s) must be LESS than STALL_THRESHOLD_MS (3s)
                                // Otherwise stall detection triggers before fallback activates!
                                if (!wrapper.TexturePtrDetectionWorking && timeSinceFirstTexture.TotalMilliseconds > 2000)
                                {
                                    // 2 seconds passed but never detected a ptr change
                                    // Fallback: Unity WebRTC reuses texture pointer, assume frames are coming
                                    //
                                    // IMPORTANT: Only update LastFrameTime if the WebRTC decoder is
                                    // actually producing frames (framesDecoded advancing). Otherwise
                                    // this unconditional update masks real stalls from the stall monitor.
                                    bool decoderActive;
                                    if (wrapper.LastWebRTCFramesDecoded >= 0)
                                    {
                                        // WebRTC stats available: trust decoder advance time
                                        var timeSinceDecoderActive = (DateTime.UtcNow - wrapper.LastDecoderAdvanceTime).TotalMilliseconds;
                                        decoderActive = timeSinceDecoderActive < 3000;
                                    }
                                    else
                                    {
                                        // Stats not yet available (grace period): assume active
                                        decoderActive = true;
                                    }

                                    if (decoderActive)
                                    {
                                        wrapper.LastFrameTime = DateTime.UtcNow;
                                    }

                                    // Frame counting in fallback mode: consume WebRTC framesDecoded delta
                                    // as ground truth instead of _pollCount % 6 (which capped at ~10fps).
                                    // Done here under _lock to avoid race with ResetFpsWindow/SendFpsFeedback.
                                    if (wrapper.LastWebRTCFramesDecoded >= 0)
                                    {
                                        long baseline = wrapper.LastFallbackFramesAccounted < 0
                                            ? wrapper.LastWebRTCFramesDecoded
                                            : wrapper.LastFallbackFramesAccounted;
                                        long delta = wrapper.LastWebRTCFramesDecoded - baseline;
                                        if (delta > 0 && delta < 1000) // sanity: skip impossible jumps
                                        {
                                            wrapper.RenderedFrameCount += (int)delta;
                                            wrapper.FrameCount += (int)delta;
                                            wrapper.TotalFramesReceived += delta;
                                        }
                                        wrapper.LastFallbackFramesAccounted = wrapper.LastWebRTCFramesDecoded;
                                    }
                                }
                                // else: TexturePtrDetectionWorking=true means we expect ptr changes
                                // Don't update LastFrameTime - let stall detection work
                            }

                            // H265: Do NOT overwrite wrapper.Texture here — it was already set
                            // correctly by the OnTextureReady callback (from H265StreamReceiver).
                            // The WebRTC track texture (tex) is empty/black in H265 mode because
                            // Encoded Transform intercepts frames before they reach the WebRTC decoder.
                            // Overwriting wrapper.Texture with tex would cause a black screen.
                            bool isH265WithCustomDecoder = _selectedCodec == VideoCodec.H265
                                && _h265Receivers.ContainsKey(wrapper.Index);
                            if (!isH265WithCustomDecoder)
                            {
                                wrapper.Texture = tex;
                            }
                            else if (wrapper.Texture != null && _pollCount <= 5)
                            {
                                string webRtcTexInfo = tex != null ? tex.width + "x" + tex.height : "null";
                                AppLog.Log($"[PhaseProtocol] PC{wrapper.Index} H265: preserving custom decoder texture {wrapper.Texture.width}x{wrapper.Texture.height}, WebRTC tex={webRtcTexInfo}");
                            }

                        }

                        // H265 Override: Update frame counts based on custom decoder metrics.
                        // IMPORTANT: This runs OUTSIDE the (tex != null) check because in DC-only
                        // H265 mode, the WebRTC track texture is null (Encoded Transform drops all
                        // data, so the native WebRTC decoder never produces a texture). Frame
                        // counting and LastFrameTime must still update based on our custom decoder.
                        if (_selectedCodec == VideoCodec.H265 && _h265Receivers.TryGetValue(wrapper.Index, out var receiver))
                        {
                            long currentDecoded = receiver.DecodedFrameCount;

                            if (wrapper.LastH265DecodedCount >= 0)
                            {
                                int delta = (int)(currentDecoded - wrapper.LastH265DecodedCount);
                                if (delta > 0)
                                {
                                    wrapper.FrameCount += delta;
                                    wrapper.RenderedFrameCount += delta;
                                    wrapper.RealFrameCount += delta;
                                    wrapper.TotalFramesReceived += delta;
                                    wrapper.LastFrameTime = DateTime.UtcNow;
                                    wrapper.LastNetworkActivityTime = DateTime.UtcNow; // H265 DC: DataChannel IS the network
                                    wrapper.TexturePtrDetectionWorking = true;
                                }
                            }

                            wrapper.LastH265DecodedCount = currentDecoded;
                        }
                    }
                    catch { }
                }
            }

            // Check for latency issues and request skip_to_live if needed
            CheckLatencyAndSkip();

            // Proactive keyframe burst on packet loss spike (WiFi only)
            if (_isWiFiConnection) CheckPacketLossAndRequestKeyframe();

            // TODO: Remove DIAG summary after debugging stall recovery
            // Periodic state summary every ~5s (300 polls at 60fps)
            if (_pollCount % 300 == 0 && _stateMachine.IsStreaming)
            {
                long totalFrames = 0;
                int totalDropped = 0;
                long totalDecoded = 0;
                bool anyFallback = false;
                lock (_lock)
                {
                    foreach (var w in _peerConnections)
                    {
                        totalFrames += w.TotalFramesReceived;
                        totalDropped += w.DroppedFrameCount;
                        // For H265 DC mode, use custom decoder count; otherwise WebRTC stats
                        if (_selectedCodec == VideoCodec.H265 && _h265Receivers.TryGetValue(w.Index, out var diagReceiver))
                            totalDecoded += diagReceiver.DecodedFrameCount;
                        else
                            totalDecoded += Math.Max(0, w.LastWebRTCFramesDecoded);
                        if (!w.TexturePtrDetectionWorking && w.Texture != null)
                            anyFallback = true;
                    }
                }
                AppLog.Log($"[DIAG:STATE] polls={_pollCount}, totalFrames={totalFrames}, dropped={totalDropped}, " +
                    $"fps={_metrics.EffectiveFps:F1}/{_lastServerTargetFps:F0}, RTT={_metrics.CurrentPingMs:F0}ms, " +
                    $"loss={_metrics.PacketLossRate:P2}, health={_metrics.HealthScore}, wifi={_isWiFiConnection}, " +
                    $"decoderFrames={totalDecoded}, fallback={anyFallback}");
            }

            // Send FPS feedback to server for adaptive encoding
            SendFpsFeedbackIfNeeded();

            // Send comprehensive quality feedback for adaptive bitrate
            SendQualityFeedbackIfNeeded();
        }

        /// <summary>
        /// Detect sudden packet loss spikes and proactively request keyframe burst
        /// before the decoder stalls. This prevents stalls on WiFi.
        /// </summary>
        private void CheckPacketLossAndRequestKeyframe()
        {
            float loss = _metrics.PacketLossRate;
            if (loss > 0.03f && _prevPacketLoss < 0.01f &&
                (DateTime.UtcNow - _lastProactiveKeyframeTime).TotalSeconds >= 2.0)
            {
                AppLog.LogWarning($"[DIAG:LOSS] Loss spike {_prevPacketLoss:P1}\u2192{loss:P1}, proactive keyframe burst");
                _ = SendTextAsync("{\"type\":\"request_keyframe_burst\",\"count\":3}");
                _lastProactiveKeyframeTime = DateTime.UtcNow;
            }
            _prevPacketLoss = loss;
        }

        /// <summary>
        /// Monitor for frame stalls - detect when video frames stop arriving even though
        /// the WebRTC connection appears healthy. This catches cases where the decoder
        /// freezes but the connection state doesn't change.
        /// </summary>
        private async Task FrameStallMonitorAsync(CancellationToken ct)
        {
            const int CHECK_INTERVAL_MS = 500;          // Wired: check every 0.5s
            const int WIFI_CHECK_INTERVAL_MS = 300;    // WiFi: check every 0.3s for faster detection
            const int INITIAL_GRACE_PERIOD_MS = 5000;

            AppLog.Log("[PhaseProtocol] Frame stall monitor started");

            await Task.Delay(INITIAL_GRACE_PERIOD_MS, ct);

            while (!ct.IsCancellationRequested && _stateMachine.IsStreaming)
            {
                try
                {
                    List<PCWrapper> wrappers;
                    lock (_lock)
                    {
                        wrappers = _peerConnections.ToList();
                    }

                    // Network stall threshold:
                    // - USB: SCTP slow-start causes periodic ~1.5-2s gaps after IDR bursts.
                    //   USB is a direct cable — actual network failures are impossible.
                    //   Use a generous threshold to avoid triggering destructive reconnect
                    //   cycles that reset SCTP congestion window and make things worse.
                    // - WiFi/Wired: 1.5s catches real network issues promptly.
                    int NETWORK_STALL_THRESHOLD_MS = _isUsbMode ? 5000 : 1500;
                    int BOOTSTRAP_TIMEOUT_MS = _isUsbMode ? 10000 : 5000;

                    // Skip all stall detection when streaming is paused.
                    // Server stops sending frames during pause, so no-frame condition is expected.
                    // Without this, pause triggers false DECODER STALL → H265→H264 fallback.
                    if (_isStreamingPaused)
                    {
                        // Reset stall state so resume doesn't immediately trigger stall
                        foreach (var w in wrappers)
                        {
                            w.LastNetworkActivityTime = DateTime.UtcNow;
                            w.LastDecoderStallRecoveryTime = DateTime.UtcNow;
                        }
                        int pauseInterval = _isWiFiConnection ? WIFI_CHECK_INTERVAL_MS : CHECK_INTERVAL_MS;
                        await Task.Delay(pauseInterval, ct);
                        continue;
                    }

                    foreach (var wrapper in wrappers)
                    {
                        if (wrapper.PC == null) continue;
                        if (wrapper.IsReconnecting) continue; // Skip if already recovering

                        // 1. Check Network Stall (Path A)
                        var timeSinceNetwork = (DateTime.UtcNow - wrapper.LastNetworkActivityTime).TotalMilliseconds;
                        bool isNetworkStalled = timeSinceNetwork > NETWORK_STALL_THRESHOLD_MS;
                        
                        // 2. Check Bootstrap (Initial connection)
                        if (wrapper.WaitingForFirstFrame)
                        {
                            // H265 DC mode: timeSinceNetwork is meaningless (RTP bytes don't flow).
                            // Use LastFrameTime instead — if decoder already produced frames,
                            // bootstrap is implicitly complete (UpdateDecoderStallCheck handles it).
                            bool h265DcBootstrap = _selectedCodec == VideoCodec.H265
                                && _h265Receivers.ContainsKey(wrapper.Index);

                            // Per-track mode: video PC may not be set up yet (waiting for video_offer
                            // from server). Don't trigger bootstrap timeout until video PC exists.
                            bool perTrackWaiting = _perTrackPcMode
                                && !_videoPcs.ContainsKey(wrapper.Index);

                            if (!h265DcBootstrap && !perTrackWaiting && timeSinceNetwork > BOOTSTRAP_TIMEOUT_MS)
                            {
                                AppLog.LogWarning($"[PhaseProtocol] PC{wrapper.Index} BOOTSTRAP TIMEOUT ({timeSinceNetwork:F0}ms) - triggering reconnect");
                                wrapper.IsReconnecting = true;
                                _ = AutoHealMonitorAsync(wrapper.Index);
                            }
                            // Always update decoder stats to see if we've started receiving frames
                            await UpdateDecoderStallCheck(wrapper);
                            continue;
                        }

                        // 3. Update Decoder Stats and Check Decoder Stall (Path B)
                        // UpdateDecoderStallCheck will trigger recovery if it detects a stall
                        await UpdateDecoderStallCheck(wrapper);
                        
                        // 4. Handle Network Stall (Path A recovery)
                        // SKIP for H265 DataChannel mode: all video frames arrive via SCTP DataChannel,
                        // NOT RTP. The RTP bytesReceived counter never increases → timeSinceNetwork is
                        // always stale → false positive NETWORK STALL. The H265 decoder stall check
                        // (UpdateDecoderStallCheck) already handles actual decoder stalls correctly.
                        bool isH265DcMode = _selectedCodec == VideoCodec.H265
                            && _h265Receivers.ContainsKey(wrapper.Index);
                        if (isNetworkStalled && !wrapper.IsInGraduatedRecovery && !isH265DcMode)
                        {
                             var timeSinceDecoderRecovery = (DateTime.UtcNow - wrapper.LastDecoderStallRecoveryTime).TotalMilliseconds;
                             if (timeSinceDecoderRecovery > 3000) // 3s grace after decoder recovery
                             {
                                  AppLog.LogWarning($"[PhaseProtocol] PC{wrapper.Index} NETWORK STALL ({timeSinceNetwork:F0}ms silence) - triggering repair");
                                  // GraduatedRecovery handles WiFi silence better than direct reconnect
                                  _ = GraduatedRecoveryAsync(wrapper.Index);
                             }
                        }
                    }

                    int checkInterval = _isWiFiConnection ? WIFI_CHECK_INTERVAL_MS : CHECK_INTERVAL_MS;
                    await Task.Delay(checkInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLog.LogWarning($"[PhaseProtocol] Frame stall monitor error: {ex.Message}");
                    int checkInterval = _isWiFiConnection ? WIFI_CHECK_INTERVAL_MS : CHECK_INTERVAL_MS;
                    await Task.Delay(checkInterval, ct);
                }
            }

            AppLog.Log("[PhaseProtocol] Frame stall monitor stopped");
        }

        /// <summary>
        /// Poll WebRTC stats to detect decoder stalls via framesDecoded counter.
        /// This is the ground truth for whether the decoder is producing frames,
        /// independent of texture pointer behavior or fallback mode.
        /// </summary>
    private async Task UpdateDecoderStallCheck(PCWrapper wrapper)
    {
        // Path B: Decoder Stall Detection
        // H265 via DataChannel: SCTP slow-start causes initial congestion after IDR burst.
        // The congestion window needs several seconds to open up, so use a generous threshold
        // to avoid false stall detection during SCTP ramp-up.
        // WiFi: Further relax to avoid false positives on jittery connections.
        bool isH265DC = _selectedCodec == VideoCodec.H265;
        // USB H264: 1500ms - jitter buffer + SCTP slow-start after reconnect causes 500-800ms gaps
        int decoderThreshold = isH265DC ? 5000 : (_isWiFiConnection ? 500 : 1500);
        const int RECOVERY_COOLDOWN_MS = 2500;
        const int ESCALATE_TO_RECONNECT_MS = 15000;

            try
            {
                var op = wrapper.PC.GetStats();
                while (!op.IsDone) await Task.Yield();
                if (op.IsError) return;

                var report = op.Value;
                try
                {
                    foreach (var pair in report.Stats)
                    {
                        if (pair.Value is RTCInboundRTPStreamStats inbound && inbound.kind == "video")
                        {
                            // Single-PC Multi-Track mode: Multiple video stats exist in one report.
                            // We MUST match the stat to the correct wrapper via Mid (Media ID).
                            bool isCorrectTrack = true;
                            if (!string.IsNullOrEmpty(wrapper.Mid))
                            {
                                // Unity WebRTC RTCInboundRTPStreamStats has 'mid' property
                                isCorrectTrack = (inbound.mid == wrapper.Mid);
                            }
                            else if (wrapper.VideoTrack != null)
                            {
                                // Fallback: Match track identifier if Mid is not set or not available in stats
                                isCorrectTrack = (inbound.trackIdentifier == wrapper.VideoTrack.Id);
                            }

                            if (!isCorrectTrack) continue;

                            long decoded = (long)inbound.framesDecoded;
                            long received = (long)inbound.framesReceived;
                            long bytes = (long)inbound.bytesReceived;

                            // H265 Override: If custom decoder is active, use its decoded count
                            if (_selectedCodec == VideoCodec.H265 && _h265Receivers.TryGetValue(wrapper.Index, out var receiver))
                            {
                                decoded = receiver.DecodedFrameCount;
                            }

                            // 1. Initial/Bootstrap Logic
                            if (wrapper.LastWebRTCBytesReceived < 0)
                            {
                                wrapper.LastWebRTCBytesReceived = bytes;
                                wrapper.LastWebRTCFramesDecoded = decoded;
                                wrapper.LastNetworkActivityTime = DateTime.UtcNow;
                                wrapper.LastDecoderAdvanceTime = DateTime.UtcNow;
                                return;
                            }

                            // 2. Track Network Activity (Path A)
                            if (bytes > wrapper.LastWebRTCBytesReceived)
                            {
                                wrapper.LastWebRTCBytesReceived = bytes;
                                wrapper.LastNetworkActivityTime = DateTime.UtcNow;
                            }

                            // 3. Track Decoder Progress
                            if (decoded > wrapper.LastWebRTCFramesDecoded)
                            {
                                long decodeBurst = decoded - wrapper.LastWebRTCFramesDecoded;
                                wrapper.LastWebRTCFramesDecoded = decoded;
                                wrapper.LastDecoderAdvanceTime = DateTime.UtcNow;

                                // Reset freeze count on sustained decode progress (burst of 3+ frames)
                                // Prevents false H265 fallback from transient stalls
                                if (decodeBurst >= 3 && _freezeCount > 0)
                                {
                                    _freezeCount = 0;
                                }

                                // First frame decoded - exit bootstrap window
                                if (wrapper.WaitingForFirstFrame)
                                {
                                    AppLog.Log($"[PhaseProtocol] PC{wrapper.Index} bootstrap complete (first frame decoded)");
                                    wrapper.WaitingForFirstFrame = false;
                                }
                            }
                            else if (!wrapper.WaitingForFirstFrame)
                            {
                                // 4. Check for Decoder Stall (Path B)
                                var timeSinceAdvance = (DateTime.UtcNow - wrapper.LastDecoderAdvanceTime).TotalMilliseconds;
                                var timeSinceNetwork = (DateTime.UtcNow - wrapper.LastNetworkActivityTime).TotalMilliseconds;
                                var timeSinceRecovery = (DateTime.UtcNow - wrapper.LastDecoderStallRecoveryTime).TotalMilliseconds;

                                // CONDITION: Decoder stopped but Network still active
                                // For H265 DC mode: RTP bytesReceived is irrelevant (all frames come via DataChannel).
                                // Skip the "network active" check — SCTP congestion after IDR burst can stall DC
                                // delivery for seconds while RTP bytes still flow normally.
                                bool networkActive = isH265DC || timeSinceNetwork < 500;
                                if (timeSinceAdvance > decoderThreshold &&
                                    networkActive &&
                                    timeSinceRecovery > RECOVERY_COOLDOWN_MS)
                                {
                                    AppLog.LogWarning($"[PhaseProtocol] PC{wrapper.Index} DECODER STALL (Path B)! " +
                                        $"framesDecoded={decoded} (last={wrapper.LastWebRTCFramesDecoded}) frozen for {timeSinceAdvance:F0}ms" +
                                        (isH265DC ? " (H265 DC mode)" : $" while bytes are flowing ({timeSinceNetwork:F0}ms)") + ".");

                                    // High-confidence decoder/sync issue: Request keyframe immediately
                                    wrapper.LastDecoderStallRecoveryTime = DateTime.UtcNow;
                                    HandleDecoderStallOrFreeze(wrapper.Index, $"Aggressive decoder stall (dec={decoded}" +
                                        (isH265DC ? ", H265-DC" : $", net={timeSinceNetwork:F0}ms") + ")");
                                }

                                // 5. Escalation
                                if (timeSinceAdvance > ESCALATE_TO_RECONNECT_MS && !wrapper.IsReconnecting)
                                {
                                     AppLog.LogWarning($"[PhaseProtocol] PC{wrapper.Index} persistent stall ({timeSinceAdvance/1000:F1}s), triggering auto-heal");
                                     HandleDecoderStallOrFreeze(wrapper.Index, "Persistent stall escalation");
                                }
                            }
                            break; 
                        }
                    }
                }
                finally
                {
                    report.Dispose();
                }
            }
            catch (Exception ex)
            {
                if (VerboseLogging)
                    AppLog.LogWarning($"[PhaseProtocol] Stall check error PC{wrapper.Index}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle pong message from server for RTT calculation.
        /// Supports both simple "pong" and sequenced "pong:timestamp" formats.
        /// </summary>
        private void HandlePongMessage(string text)
        {
            var pongTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _lastPongReceivedTime = pongTime;
            _missedPongCount = 0;
            _metrics.ResetMissedPongCount();

            // Check for sequenced pong: "pong:timestamp" or JSON with timestamp
            if (text.Contains(":"))
            {
                // Try to parse from text format "pong:timestamp"
                var parts = text.Split(':');
                if (parts.Length >= 2 && long.TryParse(parts[1].Trim(), out long sentTime))
                {
                    double rttMs = pongTime - sentTime;
                    if (rttMs > 0 && rttMs < 10000) // Sanity check
                    {
                        _metrics.RecordPing(rttMs);
                    }
                }
            }
            else if (_lastPingSentTime > 0)
            {
                // Use last sent ping time for simple pong
                double rttMs = pongTime - _lastPingSentTime;
                if (rttMs > 0 && rttMs < 10000)
                {
                    _metrics.RecordPing(rttMs);
                }
            }
        }

        /// <summary>
        /// Handle frame timing message from server for clock synchronization.
        /// Used to calculate latency and sync client/server clocks.
        /// </summary>
        /// <summary>
        /// Global handler for decoder freezes and stalls.
        /// Escalates through graduated recovery steps and eventually triggers H.264 fallback if needed.
        /// </summary>
        private void HandleDecoderStallOrFreeze(int monitorIndex, string reason)
        {
            _freezeCount++;
            _h265StallStrikes++; // Persistent cumulative count across recoveries
            
            AppLog.LogWarning($"[PhaseProtocol] {reason} (strike #{_freezeCount}, total strikes={_h265StallStrikes})!");

            // Update stats
            _metrics?.RecordStall();

            // Fallback check for H265 (relaxed: 5 sustained stalls or 15 total strikes)
            if (_selectedCodec == VideoCodec.H265 && (_freezeCount >= 5 || _h265StallStrikes >= 15))
            {
                string triggerReason = _freezeCount >= 3 ? $"sustained stall (strike {_freezeCount})" : $"flakey instability ({_h265StallStrikes} total strikes)";
                Debug.LogError($"[PhaseProtocol] H265 fallback threshold reached: {triggerReason} — triggering H264 fallback");
                
                OnH265DecoderFailed(monitorIndex);
                _freezeCount = 0; 
                return;
            }

            // Standard recovery: trigger GraduatedRecoveryAsync
            // This will handle Step 1 (Keyframe), Step 2 (Skip+Keyframe), Step 3 (Reconnect)
            // It manages its own state via wrapper.IsInGraduatedRecovery.
            _ = GraduatedRecoveryAsync(monitorIndex);

            // If we've hit many strikes even in H264, reset count but GraduatedRecovery 
            // will eventually trigger a reconnect in Step 3 if it keeps stalling.
            if (_freezeCount >= 5) 
            {
                _freezeCount = 0;
            }
        }

        private void HandleFrameTiming(SimpleJson json)
        {
            long serverTime = json.GetLong("serverTime");
            if (serverTime <= 0) return;

            long clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _serverClockOffset = clientTime - serverTime;

            // Calculate frame latency using recentFrames data
            var recentFrames = json.GetArray("recentFrames");
            if (recentFrames != null && recentFrames.Count > 0)
            {
                try
                {
                    // Get the latest frame from the array (returns SimpleJson)
                    var latest = recentFrames[recentFrames.Count - 1];
                    if (latest != null)
                    {
                        long captureTime = latest.GetLong("captureTime");
                        if (captureTime > 0)
                        {
                            // Frame latency = (server processing time) + (network delay)
                            // server processing time = serverTime - captureTime
                            // network delay = estimated as ping/2 (one-way delay)
                            double owd = _metrics.CurrentPingMs > 0 ? _metrics.CurrentPingMs / 2.0 : 0;
                            double frameLatency = (serverTime - captureTime) + owd;

                            if (frameLatency > 0 && frameLatency < 2000) // Sanity check
                            {
                                _metrics.RecordFrameTiming(serverTime, captureTime);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.LogWarning($"[PhaseProtocol] Error parsing frame timing: {ex.Message}");
                }
            }

            // === Decoder freeze detection ===
            // Skip freeze detection when streaming is paused (server not sending frames)
            if (_isStreamingPaused)
            {
                OnFrameTimingReceived?.Invoke(serverTime, _serverClockOffset);
                return;
            }

            // Two detection modes:
            // 1. When TexturePtrDetection works: Compare real frames (texture pointer changes) with server frames
            // 2. Fallback mode: Can't detect real frames, so send preventive keyframes periodically
            long currentFrame = json.GetLong("currentFrame");
            if (currentFrame > 0)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                bool inFallbackMode = IsAnyMonitorInFallbackMode();

                // Initialize on first frameTiming
                if (_lastServerFrame == 0)
                {
                    _lastServerFrame = currentFrame;
                    _lastServerFrameTime = now;
                    _clientFramesAtLastCheck = GetTotalRenderedFrames();
                    _realFramesAtLastCheck = GetTotalRealFrames();
                }
                else if (now - _lastServerFrameTime >= DecoderFreezeCheckIntervalMs)
                {
                    // Time to check for freeze
                    long serverFrameAdvance = currentFrame - _lastServerFrame;
                    int currentRealFrames = GetTotalRealFrames();
                    int realFrameAdvance = currentRealFrames - _realFramesAtLastCheck;

                    if (!inFallbackMode)
                    {
                        // Mode 1: TexturePtrDetection works - use real frames for reliable freeze detection
                        if (VerboseLogging)
                            AppLog.Log($"[PhaseProtocol] Freeze check (ptr mode): server +{serverFrameAdvance}, client real +{realFrameAdvance}, loss={_metrics.PacketLossRate:P1}");

                        // Freeze detected: server advanced many frames but client decoded none
                        if (serverFrameAdvance >= DecoderFreezeThresholdFrames && realFrameAdvance < 5)
                        {
                            // WiFi-aware: Skip freeze handling if packet loss is very high
                            // The network will recover naturally once adaptive bitrate reduces quality
                            if (_isWiFiConnection && _metrics.PacketLossRate > 0.30f)
                            {
                                if (VerboseLogging)
                                    AppLog.Log($"[PhaseProtocol] Freeze check SKIP: High packet loss ({_metrics.PacketLossRate:P0}), waiting for ABR adjustment");
                            }
                            else
                            {
                                HandleDecoderStallOrFreeze(-1, $"Decoder freeze (server +{serverFrameAdvance}, client +{realFrameAdvance})");
                            }
                        }
                        else if (realFrameAdvance >= 10)
                        {
                            // Good frame flow, reset freeze count
                            if (_freezeCount > 0)
                            {
                                if (VerboseLogging) AppLog.Log($"[PhaseProtocol] Freeze recovery confirmed, resetting freeze count (was {_freezeCount})");
                                _freezeCount = 0;
                            }
                        }
                    }
                    else
                    {
                        // Mode 2: Fallback mode - can't detect real frames, use preventive keyframes
                        if (VerboseLogging)
                            AppLog.Log($"[PhaseProtocol] Freeze check (fallback mode): server +{serverFrameAdvance}, interval={PreventiveKeyframeIntervalSeconds:F0}s");

                        var timeSinceLastPreventive = (DateTime.UtcNow - _lastPreventiveKeyframeTime).TotalSeconds;
                        if (timeSinceLastPreventive >= PreventiveKeyframeIntervalSeconds)
                        {
                            if (VerboseLogging) AppLog.Log($"[PhaseProtocol] Fallback mode: Sending preventive keyframe request (last was {timeSinceLastPreventive:F0}s ago)");
                            RequestKeyframe(-1); // Request keyframe for all monitors
                            _lastPreventiveKeyframeTime = DateTime.UtcNow;
                        }
                    }

                    // Reset check state
                    _lastServerFrame = currentFrame;
                    _lastServerFrameTime = now;
                    _clientFramesAtLastCheck = GetTotalRenderedFrames();
                    _realFramesAtLastCheck = currentRealFrames;
                }
            }

            OnFrameTimingReceived?.Invoke(serverTime, _serverClockOffset);
        }

        /// <summary>
        /// Get total rendered frames across all monitors (for freeze detection).
        /// </summary>
        private int GetTotalRenderedFrames()
        {
            int total = 0;
            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    total += wrapper.RenderedFrameCount;
                }
            }
            return total;
        }

        /// <summary>
        /// Get total REAL frames (texture pointer changes only, no fallback) for reliable freeze detection.
        /// </summary>
        private int GetTotalRealFrames()
        {
            int total = 0;
            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    total += wrapper.RealFrameCount;
                }
            }
            return total;
        }

        /// <summary>
        /// Check if any monitor is in fallback mode (can't detect real frames).
        /// </summary>
        private bool IsAnyMonitorInFallbackMode()
        {
            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    // In fallback if: has texture but TexturePtrDetectionWorking is false after grace period
                    if (wrapper.Texture != null && !wrapper.TexturePtrDetectionWorking)
                    {
                        var timeSinceFirstTexture = DateTime.UtcNow - wrapper.FirstTextureTime;
                        if (timeSinceFirstTexture.TotalMilliseconds > 2000)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Handle FPS adjusted message from server.
        /// Server sends this in response to fps_feedback to indicate encoding FPS was changed.
        /// </summary>
        private void HandleFpsAdjusted(SimpleJson json)
        {
            int monitorIndex = json.GetInt("monitorIndex");
            double targetFpsD = json.GetDouble("targetFps");
            float targetFps = targetFpsD > 0 ? (float)targetFpsD : 60f;

            _lastServerTargetFps = targetFps;
            if (VerboseLogging) AppLog.Log($"[PhaseProtocol] Server adjusted FPS: monitor {monitorIndex} → {targetFps:F1} fps");

            OnFpsAdjusted?.Invoke(monitorIndex, targetFps);
        }

        /// <summary>
        /// Handle bitrate adjustment notification from server.
        /// Server sends this in response to quality_feedback when bitrate was changed.
        /// </summary>
        private void HandleBitrateAdjusted(SimpleJson json)
        {
            int monitorIndex = json.GetInt("monitorIndex");
            int bitrateKbps = json.GetInt("bitrateKbps");
            string reason = json.GetString("reason") ?? "adaptive";

            AppLog.Log($"[DIAG:BITRATE] Server adjusted: mon{monitorIndex} → {bitrateKbps}kbps ({reason})");

            // Fire event for UI update if needed
            OnBitrateAdjusted?.Invoke(monitorIndex, bitrateKbps, reason);
        }

        /// <summary>
        /// Handle quality recommendation from server.
        /// Server may suggest resolution/fps changes based on sustained poor quality.
        /// </summary>
        private void HandleQualityRecommendation(SimpleJson json)
        {
            string recommendation = json.GetString("recommendation") ?? ""; // e.g., "reduce_fps", "reduce_resolution", "reduce_bitrate"
            string reason = json.GetString("reason") ?? "";

            AppLog.Log($"[PhaseProtocol] Server quality recommendation: {recommendation} - {reason}");

            // Fire event for UI/settings to handle
            OnQualityRecommendation?.Invoke(recommendation, reason);
        }

        /// <summary>
        /// Update connection type and adjust thresholds accordingly.
        /// Called after network_info message received.
        /// </summary>
        private void UpdateConnectionType(string connectionType, double jitterMs)
        {
            // Detect WiFi based on connection type string OR high jitter
            // High jitter (>10ms) typically indicates WiFi even if type is unknown
            // Note: Android returns "Wi-Fi" (with hyphen), so strip non-alpha before matching
            var normalizedType = connectionType?.ToLower().Replace("-", "").Replace("_", "").Replace(" ", "") ?? "";
            _isWiFiConnection = normalizedType.Contains("wifi") ||
                                normalizedType.Contains("wireless") ||
                                jitterMs > 10.0;

            // Update metrics with connection info
            _metrics.ConnectionType = connectionType ?? "Unknown";
            _metrics.IsWiFiConnection = _isWiFiConnection;

            AppLog.Log($"[PhaseProtocol] Connection type: {connectionType}, WiFi mode: {_isWiFiConnection}");
            AppLog.Log($"[PhaseProtocol] Active thresholds - FrameGap: {FrameGapThresholdMs}ms, MonitorDrift: {MonitorDriftThresholdMs}ms, FreezeThreshold: {DecoderFreezeThresholdFrames} frames");
        }

        /// <summary>
        /// Calculate adaptive preventive keyframe interval based on connection quality.
        /// Better connection = longer interval (less wasteful).
        /// </summary>
        private float CalculateAdaptiveKeyframeInterval()
        {
            float interval = WIFI_PREVENTIVE_KEYFRAME_MAX_INTERVAL;

            // Reduce interval if packet loss is high
            // (values must be < MAX_INTERVAL to have effect)
            if (_metrics.PacketLossRate > 0.05f)
                interval = Math.Min(interval, 3f);
            if (_metrics.PacketLossRate > 0.1f)
                interval = Math.Min(interval, 2f);

            // Reduce interval if jitter is high
            if (_metrics.JitterMs > 30)
                interval = Math.Min(interval, 3f);
            if (_metrics.JitterMs > 50)
                interval = Math.Min(interval, 2f);

            // Reduce interval if health is low
            if (_metrics.HealthScore < 50)
                interval = Math.Min(interval, 3f);
            if (_metrics.HealthScore < 30)
                interval = Math.Min(interval, 2f);

            // Use base interval for LAN connections
            if (!_isWiFiConnection)
                interval = BASE_PREVENTIVE_KEYFRAME_INTERVAL_SECONDS;

            return Math.Max(WIFI_PREVENTIVE_KEYFRAME_MIN_INTERVAL, interval);
        }
    }
}
