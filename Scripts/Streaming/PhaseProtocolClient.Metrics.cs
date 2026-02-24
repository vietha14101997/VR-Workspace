using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

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

        // WiFi thresholds (more tolerant - allow for jitter and burst loss)
        private const float WIFI_FRAME_GAP_THRESHOLD_MS = 1500f;           // 1.5s for WiFi (was 500ms)
        private const float WIFI_MONITOR_DRIFT_THRESHOLD_MS = 1000f;       // 1s drift allowed (was 500ms)
        private const int WIFI_DECODER_FREEZE_THRESHOLD_FRAMES = 120;      // ~4s at 30fps (was 90)
        private const float WIFI_DECODER_FREEZE_CHECK_INTERVAL_MS = 5000f; // Check every 5s (was 3s)
        private const float WIFI_PREVENTIVE_KEYFRAME_MIN_INTERVAL = 10f;   // Minimum 10s
        private const float WIFI_PREVENTIVE_KEYFRAME_MAX_INTERVAL = 30f;   // Maximum 30s on stable

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

        // FPS Feedback constants (for adaptive encoding)
        private const float FPS_FEEDBACK_INTERVAL_SECONDS = 1.0f;  // Send feedback every 1s
        private const float FPS_CHANGE_THRESHOLD = 5.0f;           // Report if FPS differs by 5+
        private const int FPS_WINDOW_FRAMES = 30;                  // Minimum frames before calculating

        // Quality feedback timing (for adaptive bitrate decisions)
        private DateTime _lastQualityFeedbackTime = DateTime.MinValue;
        private const float QUALITY_FEEDBACK_INTERVAL_SECONDS = 3.0f; // Send comprehensive feedback every 3 seconds
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
                Debug.Log("[PhaseProtocol] Streaming started - latency checks will begin after warmup period");
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
                        Debug.LogWarning($"[PhaseProtocol] EXTREME RTT during warmup ({streamingDurationSeconds:F1}s): {currentRtt:F0}ms - skip_to_live");
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
                    Debug.LogWarning($"[PhaseProtocol] EXTREME RTT detected: {currentRtt:F0}ms - IMMEDIATE skip_to_live");
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
                    Debug.LogWarning($"[PhaseProtocol] HIGH RTT detected: {currentRtt:F0}ms - skip_to_live");
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
                        Debug.LogWarning($"[PhaseProtocol] FPS CRISIS: {effectiveFps:F1}/{targetFps:F0} fps - skip_to_live");
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
                    Debug.LogWarning($"[PhaseProtocol] PACKET LOSS CRISIS: {packetLoss:P0} - skip_to_live + keyframe");
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
                        if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds >= SkipToLiveCooldownSeconds)
                        {
                            Debug.LogWarning($"[PhaseProtocol] PC{wrapper.Index} frame gap {timeSinceFrame:F0}ms (threshold={frameGapThreshold:F0}ms) - skip_to_live");
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

                // Monitor drift detection
                if (_peerConnections.Count > 1 && minFrameTime != DateTime.MaxValue && maxFrameTime != DateTime.MinValue)
                {
                    var drift = (maxFrameTime - minFrameTime).TotalMilliseconds;
                    if (drift > driftThreshold && laggingMonitor >= 0)
                    {
                        if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds >= SkipToLiveCooldownSeconds)
                        {
                            Debug.LogWarning($"[PhaseProtocol] Monitor drift: PC{laggingMonitor} is {drift:F0}ms behind (threshold={driftThreshold:F0}ms) - skip_to_live");
                        }
                        SkipToLive(laggingMonitor);
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
                Debug.Log($"[PhaseProtocol] URGENT skip_to_live (bypassing cooldown)");
                await SendTextAsync(msg);

                // Also request keyframe to ensure clean recovery
                await Task.Delay(50); // Small delay to let server process skip first
                string keyframeMsg = monitorIndex >= 0
                    ? $"{{\"type\":\"request_keyframe\",\"monitorIndex\":{monitorIndex}}}"
                    : "{\"type\":\"request_keyframe\"}";
                await SendTextAsync(keyframeMsg);
                Debug.Log($"[PhaseProtocol] Follow-up keyframe request sent");
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

                    // Skip if not enough frames to calculate
                    if (wrapper.RenderedFrameCount < FPS_WINDOW_FRAMES)
                        continue;

                    // Calculate effective FPS
                    double windowSeconds = (DateTime.UtcNow - wrapper.FpsWindowStart).TotalSeconds;
                    if (windowSeconds <= 0) continue;

                    float effectiveFps = (float)(wrapper.RenderedFrameCount / windowSeconds);

                    // Only send if FPS changed significantly (avoid spam)
                    if (Math.Abs(effectiveFps - wrapper.LastReportedEffectiveFps) < FPS_CHANGE_THRESHOLD
                        && wrapper.LastReportedEffectiveFps > 0)
                    {
                        // Reset window but don't send
                        ResetFpsWindow(wrapper);
                        continue;
                    }

                    // Send feedback
                    wrapper.LastFpsFeedbackSent = DateTime.UtcNow;
                    wrapper.LastReportedEffectiveFps = effectiveFps;

                    // Update global metrics with effective FPS (use max across all monitors for buffer status)
                    if (effectiveFps > _metrics.EffectiveFps || _metrics.EffectiveFps == 0)
                    {
                        _metrics.RecordEffectiveFps(effectiveFps);
                    }

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

                    if (VerboseLogging)
                        Debug.Log($"[Decode FPS] Mon{wrapper.Index}: {effectiveFps:F1} fps (window), total={wrapper.TotalFramesReceived} frames in {totalSeconds:F1}s = {avgFps:F1} avg fps");

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

            // Log occasionally for debugging (every ~20 polls = ~60 seconds)
            if (_pollCount % 20 == 0)
            {
                Debug.Log($"[PhaseProtocol] Quality feedback: RTT={_metrics.CurrentPingMs:F0}ms, " +
                    $"jitter={_metrics.JitterMs:F1}ms, loss={_metrics.PacketLossRate:P1}, " +
                    $"fps={_metrics.EffectiveFps:F0}/{_lastServerTargetFps:F0}, health={_metrics.HealthScore}, " +
                    $"buffer={bufferStatus}");
            }
        }

        /// <summary>
        /// Update textures (call from Update loop).
        /// NOTE: LastFrameTime is updated in OnVideoReceived callback, not here.
        /// This method only caches the texture reference and checks for latency issues.
        /// </summary>
        public void PollTextures()
        {
            _pollCount++;

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
                            Debug.Log($"[PhaseProtocol] PollTextures PC{wrapper.Index}: " +
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
                                    wrapper.TexturePtrDetectionWorking = true; // Detection method confirmed working

                                    // Debug log first 10 frames and every 300 frames after - disabled by default
                                    if (VerboseLogging && (wrapper.FrameCount <= 10 || wrapper.FrameCount % 300 == 0))
                                    {
                                        Debug.Log($"[PhaseProtocol] PC{wrapper.Index} texture ptr CHANGED: {wrapper.LastTexturePtr:X} -> {currentPtr:X}, frames={wrapper.FrameCount}");
                                    }
                                }
                                else
                                {
                                    if (VerboseLogging)
                                        Debug.Log($"[PhaseProtocol] PC{wrapper.Index} first texture ptr: {currentPtr:X}");
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
                                    wrapper.LastFrameTime = DateTime.UtcNow;
                                    if (_pollCount % 6 == 0)
                                    {
                                        wrapper.FrameCount++;
                                        wrapper.RenderedFrameCount++;
                                    }
                                }
                                // else: TexturePtrDetectionWorking=true means we expect ptr changes
                                // Don't update LastFrameTime - let stall detection work
                            }

                            wrapper.Texture = tex;
                        }
                    }
                    catch { }
                }
            }

            // Check for latency issues and request skip_to_live if needed
            CheckLatencyAndSkip();

            // Send FPS feedback to server for adaptive encoding
            SendFpsFeedbackIfNeeded();

            // Send comprehensive quality feedback for adaptive bitrate
            SendQualityFeedbackIfNeeded();
        }

        /// <summary>
        /// Monitor for frame stalls - detect when video frames stop arriving even though
        /// the WebRTC connection appears healthy. This catches cases where the decoder
        /// freezes but the connection state doesn't change.
        /// </summary>
        private async Task FrameStallMonitorAsync(CancellationToken ct)
        {
            const int CHECK_INTERVAL_MS = 1000; // Check every 1 second (was 2s)
            const int STALL_THRESHOLD_MS = 3000; // Consider stalled if no frames for 3 seconds (was 5s)
            const int INITIAL_GRACE_PERIOD_MS = 5000; // Wait 5 seconds before monitoring (was 10s)

            Debug.Log("[PhaseProtocol] Frame stall monitor started");

            // Initial grace period to let streams stabilize
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

                    foreach (var wrapper in wrappers)
                    {
                        if (wrapper.PC == null) continue;
                        if (wrapper.IsReconnecting) continue; // Already reconnecting

                        var timeSinceFrame = DateTime.UtcNow - wrapper.LastFrameTime;
                        var pcState = wrapper.PC.ConnectionState;

                        // Only check for stalls if PC appears connected
                        if (pcState == RTCPeerConnectionState.Connected &&
                            wrapper.LastFrameTime != default &&
                            timeSinceFrame.TotalMilliseconds > STALL_THRESHOLD_MS)
                        {
                            Debug.LogWarning($"[PhaseProtocol] PC{wrapper.Index} FRAME STALL detected! No frames for {timeSinceFrame.TotalSeconds:F1}s (frames received: {wrapper.FrameCount})");

                            // Trigger reconnect
                            if (!wrapper.IsReconnecting)
                            {
                                wrapper.IsReconnecting = true;
                                Debug.Log($"[PhaseProtocol] PC{wrapper.Index} triggering reconnect due to frame stall");
                                _ = AutoHealMonitorAsync(wrapper.Index);
                            }
                        }
                    }

                    await Task.Delay(CHECK_INTERVAL_MS, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PhaseProtocol] Frame stall monitor error: {ex.Message}");
                    await Task.Delay(CHECK_INTERVAL_MS, ct);
                }
            }

            Debug.Log("[PhaseProtocol] Frame stall monitor stopped");
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
                    Debug.LogWarning($"[PhaseProtocol] Error parsing frame timing: {ex.Message}");
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
                            Debug.Log($"[PhaseProtocol] Freeze check (ptr mode): server +{serverFrameAdvance}, client real +{realFrameAdvance}, loss={_metrics.PacketLossRate:P1}");

                        // Freeze detected: server advanced many frames but client decoded none
                        if (serverFrameAdvance >= DecoderFreezeThresholdFrames && realFrameAdvance < 5)
                        {
                            // WiFi-aware: Skip freeze handling if packet loss is very high
                            // The network will recover naturally once adaptive bitrate reduces quality
                            if (_isWiFiConnection && _metrics.PacketLossRate > 0.30f)
                            {
                                if (VerboseLogging)
                                    Debug.Log($"[PhaseProtocol] Freeze check SKIP: High packet loss ({_metrics.PacketLossRate:P0}), waiting for ABR adjustment");
                            }
                            else
                            {
                                _freezeCount++;
                                Debug.LogWarning($"[PhaseProtocol] DECODER FREEZE #{_freezeCount}! Server sent {serverFrameAdvance} frames but client decoded only {realFrameAdvance}");

                                // Graduated response:
                                // First freeze -> SkipToLive (lighter, just sync to latest)
                                // Repeated freeze within short time -> RequestKeyframe (force fresh IDR)
                                if (_freezeCount <= 1)
                                {
                                    if (VerboseLogging) Debug.Log($"[PhaseProtocol] Response: SkipToLive (light recovery)");
                                    SkipToLive(-1);
                                }
                                else
                                {
                                    if (VerboseLogging) Debug.Log($"[PhaseProtocol] Response: RequestKeyframe (heavy recovery, freeze #{_freezeCount})");
                                    RequestKeyframe(-1);
                                    // Reset freeze count after heavy recovery
                                    if (_freezeCount >= 3)
                                        _freezeCount = 0;
                                }
                                _metrics.RecordStall();
                            }
                        }
                        else if (realFrameAdvance >= 10)
                        {
                            // Good frame flow, reset freeze count
                            if (_freezeCount > 0)
                            {
                                if (VerboseLogging) Debug.Log($"[PhaseProtocol] Freeze recovery confirmed, resetting freeze count (was {_freezeCount})");
                                _freezeCount = 0;
                            }
                        }
                    }
                    else
                    {
                        // Mode 2: Fallback mode - can't detect real frames, use preventive keyframes
                        if (VerboseLogging)
                            Debug.Log($"[PhaseProtocol] Freeze check (fallback mode): server +{serverFrameAdvance}, interval={PreventiveKeyframeIntervalSeconds:F0}s");

                        var timeSinceLastPreventive = (DateTime.UtcNow - _lastPreventiveKeyframeTime).TotalSeconds;
                        if (timeSinceLastPreventive >= PreventiveKeyframeIntervalSeconds)
                        {
                            if (VerboseLogging) Debug.Log($"[PhaseProtocol] Fallback mode: Sending preventive keyframe request (last was {timeSinceLastPreventive:F0}s ago)");
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
            Debug.Log($"[PhaseProtocol] Server adjusted FPS: monitor {monitorIndex} → {targetFps:F1} fps");

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

            Debug.Log($"[PhaseProtocol] Server adjusted bitrate: monitor {monitorIndex} → {bitrateKbps} kbps ({reason})");

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

            Debug.Log($"[PhaseProtocol] Server quality recommendation: {recommendation} - {reason}");

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
            _isWiFiConnection = connectionType?.ToLower().Contains("wifi") == true ||
                                connectionType?.ToLower().Contains("wireless") == true ||
                                jitterMs > 10.0;

            // Update metrics with connection info
            _metrics.ConnectionType = connectionType ?? "Unknown";
            _metrics.IsWiFiConnection = _isWiFiConnection;

            Debug.Log($"[PhaseProtocol] Connection type: {connectionType}, WiFi mode: {_isWiFiConnection}");
            Debug.Log($"[PhaseProtocol] Active thresholds - FrameGap: {FrameGapThresholdMs}ms, MonitorDrift: {MonitorDriftThresholdMs}ms, FreezeThreshold: {DecoderFreezeThresholdFrames} frames");
        }

        /// <summary>
        /// Calculate adaptive preventive keyframe interval based on connection quality.
        /// Better connection = longer interval (less wasteful).
        /// </summary>
        private float CalculateAdaptiveKeyframeInterval()
        {
            float interval = WIFI_PREVENTIVE_KEYFRAME_MAX_INTERVAL;

            // Reduce interval if packet loss is high
            if (_metrics.PacketLossRate > 0.05f)
                interval = Math.Min(interval, 15f);
            if (_metrics.PacketLossRate > 0.1f)
                interval = Math.Min(interval, 10f);

            // Reduce interval if jitter is high
            if (_metrics.JitterMs > 30)
                interval = Math.Min(interval, 15f);
            if (_metrics.JitterMs > 50)
                interval = Math.Min(interval, 10f);

            // Reduce interval if health is low
            if (_metrics.HealthScore < 50)
                interval = Math.Min(interval, 12f);
            if (_metrics.HealthScore < 30)
                interval = Math.Min(interval, 8f);

            // Use base interval for LAN connections
            if (!_isWiFiConnection)
                interval = BASE_PREVENTIVE_KEYFRAME_INTERVAL_SECONDS;

            return Math.Max(WIFI_PREVENTIVE_KEYFRAME_MIN_INTERVAL, interval);
        }
    }
}
