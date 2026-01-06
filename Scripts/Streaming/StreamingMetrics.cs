using System;
using System.Collections.Generic;
using System.Linq;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Real-time streaming metrics for monitoring connection quality.
    /// Tracks RTT/ping, jitter (RFC 3550), frame latency, and connection health.
    /// </summary>
    public class StreamingMetrics
    {
        // Constants
        private const int MAX_PING_SAMPLES = 20;
        private const int MAX_LATENCY_SAMPLES = 20;
        private const int MAX_LOSS_SAMPLES = 10;

        // Ping samples (rolling window)
        private readonly List<double> _pingSamples = new List<double>();
        private double _lastPingMs;

        // Latency samples
        private readonly List<double> _latencySamples = new List<double>();

        // Packet loss samples (rolling window)
        private readonly List<float> _lossRateSamples = new List<float>();

        // Counters for health score
        private int _missedPongCount;
        private int _stallCount;
        private int _iceDisconnectCount;

        // === RTT/Ping Properties ===

        /// <summary>Current ping/RTT in milliseconds.</summary>
        public double CurrentPingMs { get; private set; }

        /// <summary>Average ping over recent samples (with outlier filtering).</summary>
        public double AveragePingMs { get; private set; }

        /// <summary>Minimum ping observed.</summary>
        public double MinPingMs { get; private set; } = double.MaxValue;

        /// <summary>Maximum ping observed.</summary>
        public double MaxPingMs { get; private set; }

        // === Jitter (RFC 3550) ===

        /// <summary>Network jitter in milliseconds (RFC 3550 algorithm).</summary>
        public double JitterMs { get; private set; }

        // === Frame Latency ===

        /// <summary>End-to-end frame latency (capture to display) in milliseconds.</summary>
        public double FrameLatencyMs { get; private set; }

        /// <summary>Average frame latency over recent samples.</summary>
        public double AverageFrameLatencyMs { get; private set; }

        // === Packet Loss (for adaptive bitrate) ===

        /// <summary>Current packet loss rate (0.0 to 1.0) from last window.</summary>
        public float PacketLossRate { get; private set; }

        /// <summary>Average packet loss rate over recent windows.</summary>
        public float AveragePacketLossRate { get; private set; }

        // === Effective FPS ===

        /// <summary>Effective FPS (actually decoded frames per second).</summary>
        public float EffectiveFps { get; private set; }

        /// <summary>Target FPS from server configuration.</summary>
        public float TargetFps { get; set; } = 60f;

        // === Buffer Status ===

        /// <summary>Estimated buffer fullness (0.0 = empty, 1.0 = full).</summary>
        public float BufferFullness { get; private set; } = 0.5f;

        /// <summary>Total dropped frames across all monitors (for distinguishing network issues vs static content).</summary>
        public int TotalDroppedFrames { get; private set; }

        /// <summary>Total rendered frames across all monitors.</summary>
        public int TotalRenderedFrames { get; private set; }

        // === Connection Type ===

        /// <summary>Connection type detected during speed test (e.g., "WiFi", "Ethernet", "Unknown").</summary>
        public string ConnectionType { get; set; } = "Unknown";

        /// <summary>True if connection is detected as WiFi (used for threshold adjustment).</summary>
        public bool IsWiFiConnection { get; set; }

        // === USB Mode (RNDIS Tethering) ===

        /// <summary>True if connection is via USB Tethering (RNDIS). Expects very low latency.</summary>
        public bool IsUsbMode { get; private set; }

        /// <summary>USB-specific ICMP latency in milliseconds (typically < 1ms).</summary>
        public double UsbLatencyMs { get; private set; }

        /// <summary>USB interface version: "USB 2.0", "USB 3.0", or null.</summary>
        public string UsbVersion { get; private set; }

        /// <summary>Estimated bandwidth for USB mode in Mbps.</summary>
        public double UsbEstimatedBandwidthMbps { get; private set; }

        // === Connection Health ===

        /// <summary>Connection health score (0-100). Below 30 is critical.</summary>
        public int HealthScore { get; private set; } = 100;

        /// <summary>True if connection health is critical (score below 30).</summary>
        public bool IsHealthCritical => HealthScore < 30;

        // === Events ===

        /// <summary>Fired when metrics are updated.</summary>
        public event Action<StreamingMetrics> OnMetricsUpdated;

        /// <summary>Fired when health score drops below critical threshold.</summary>
        public event Action<int> OnHealthScoreChanged;

        // === Methods ===

        /// <summary>
        /// Record a ping/RTT measurement and update statistics.
        /// </summary>
        /// <param name="rttMs">Round-trip time in milliseconds.</param>
        public void RecordPing(double rttMs)
        {
            if (rttMs <= 0 || rttMs > 10000) return; // Reject invalid values

            // Update current ping
            CurrentPingMs = rttMs;

            // Add to samples
            _pingSamples.Add(rttMs);
            while (_pingSamples.Count > MAX_PING_SAMPLES)
            {
                _pingSamples.RemoveAt(0);
            }

            // Update min/max
            if (rttMs < MinPingMs) MinPingMs = rttMs;
            if (rttMs > MaxPingMs) MaxPingMs = rttMs;

            // Calculate average with outlier filtering (remove top/bottom 10%)
            if (_pingSamples.Count >= 5)
            {
                var sorted = _pingSamples.OrderBy(x => x).ToList();
                int trimCount = Math.Max(1, sorted.Count / 10);
                var trimmed = sorted.Skip(trimCount).Take(sorted.Count - 2 * trimCount).ToList();
                AveragePingMs = trimmed.Count > 0 ? trimmed.Average() : sorted.Average();
            }
            else
            {
                AveragePingMs = _pingSamples.Average();
            }

            // Calculate jitter using RFC 3550 algorithm
            // J(i) = J(i-1) + (|D(i-1,i)| - J(i-1)) / 16
            // where D is the difference in transit time between consecutive packets
            if (_lastPingMs > 0)
            {
                double d = Math.Abs(rttMs - _lastPingMs);
                JitterMs = JitterMs + (d - JitterMs) / 16.0;
            }
            _lastPingMs = rttMs;

            // Reset missed pong counter on successful ping
            _missedPongCount = 0;

            // Notify listeners
            OnMetricsUpdated?.Invoke(this);
        }

        /// <summary>
        /// Record a frame timing measurement for end-to-end latency calculation.
        /// </summary>
        /// <param name="serverTime">Server timestamp when frame timing was sent.</param>
        /// <param name="captureTime">Server timestamp when frame was captured.</param>
        public void RecordFrameTiming(long serverTime, long captureTime)
        {
            if (captureTime <= 0) return;

            // Calculate latency = current time - capture time (adjusted for clock offset)
            // The caller should have already calculated the actual latency
            // This method just stores and averages

            long clientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // One-way delay estimation using ping/2
            double owd = CurrentPingMs > 0 ? CurrentPingMs / 2.0 : 0;

            // Server time offset estimation
            double serverTimeOffset = clientTime - serverTime - owd;

            // Frame latency = time since capture (on server clock) + network delay
            double latency = (serverTime - captureTime) + owd;

            if (latency > 0 && latency < 2000) // Reject outliers
            {
                FrameLatencyMs = latency;

                _latencySamples.Add(latency);
                while (_latencySamples.Count > MAX_LATENCY_SAMPLES)
                {
                    _latencySamples.RemoveAt(0);
                }

                AverageFrameLatencyMs = _latencySamples.Average();
            }

            OnMetricsUpdated?.Invoke(this);
        }

        /// <summary>
        /// Record a missed pong (server didn't respond to ping).
        /// </summary>
        public void RecordMissedPong()
        {
            _missedPongCount++;
            UpdateHealthScore();
        }

        /// <summary>
        /// Record a frame stall event.
        /// </summary>
        public void RecordStall()
        {
            _stallCount++;
            UpdateHealthScore();
        }

        /// <summary>
        /// Record an ICE disconnect event.
        /// </summary>
        public void RecordIceDisconnect()
        {
            _iceDisconnectCount++;
            UpdateHealthScore();
        }

        /// <summary>
        /// Record packet loss rate from RtpDepacketizer.
        /// </summary>
        /// <param name="lossRate">Loss rate from 0.0 to 1.0.</param>
        public void RecordPacketLoss(float lossRate)
        {
            if (lossRate < 0 || lossRate > 1) return;

            PacketLossRate = lossRate;

            // Add to rolling window
            _lossRateSamples.Add(lossRate);
            while (_lossRateSamples.Count > MAX_LOSS_SAMPLES)
            {
                _lossRateSamples.RemoveAt(0);
            }

            // Calculate average
            AveragePacketLossRate = _lossRateSamples.Count > 0 ? _lossRateSamples.Average() : 0f;

            OnMetricsUpdated?.Invoke(this);
        }

        /// <summary>
        /// Record effective FPS (actually decoded frames).
        /// </summary>
        /// <param name="fps">Frames per second actually decoded and displayed.</param>
        public void RecordEffectiveFps(float fps)
        {
            if (fps >= 0 && fps <= 240) // Reasonable range
            {
                EffectiveFps = fps;
                OnMetricsUpdated?.Invoke(this);
            }
        }

        /// <summary>
        /// Set USB mode information received from server.
        /// USB mode has different latency expectations (< 1ms typical).
        /// </summary>
        /// <param name="isUsbMode">True if USB tethering detected.</param>
        /// <param name="usbLatencyMs">USB ICMP latency in ms.</param>
        /// <param name="usbVersion">USB interface version.</param>
        /// <param name="usbBandwidthMbps">Estimated USB bandwidth.</param>
        public void SetUsbMode(bool isUsbMode, double usbLatencyMs = 0, string usbVersion = null, double usbBandwidthMbps = 0)
        {
            IsUsbMode = isUsbMode;
            UsbLatencyMs = usbLatencyMs;
            UsbVersion = usbVersion;
            UsbEstimatedBandwidthMbps = usbBandwidthMbps;

            if (isUsbMode)
            {
                ConnectionType = "USB";
                UnityEngine.Debug.Log($"[StreamingMetrics] USB Mode: latency={usbLatencyMs:F2}ms, version={usbVersion}, bandwidth={usbBandwidthMbps}Mbps");
            }
        }

        /// <summary>
        /// Update buffer status estimation.
        /// </summary>
        /// <param name="fullness">Buffer fullness from 0.0 (empty) to 1.0 (full).</param>
        public void RecordBufferStatus(float fullness)
        {
            BufferFullness = Math.Max(0f, Math.Min(1f, fullness));
        }

        /// <summary>
        /// Record frame counts for dropped frame tracking.
        /// Used to distinguish network issues (high drops) from static content (low drops).
        /// </summary>
        public void RecordFrameCounts(int totalRendered, int totalDropped)
        {
            TotalRenderedFrames = totalRendered;
            TotalDroppedFrames = totalDropped;
        }

        /// <summary>
        /// Get buffer status description based on current metrics.
        /// </summary>
        public string GetBufferStatusDescription()
        {
            if (PacketLossRate > 0.05f) return "lossy";
            if (FrameLatencyMs > 200) return "high_latency";

            // Only report starving if:
            // 1. We have valid FPS data (EffectiveFps > 0)
            // 2. FPS is significantly below target
            // 3. There are actual dropped frames (network issue, not static content)
            // Low FPS with no dropped frames = static content optimization, which is normal
            if (EffectiveFps > 0 && EffectiveFps < TargetFps * 0.7f)
            {
                // Check if there are significant dropped frames (>5% drop rate)
                int totalFrames = TotalRenderedFrames + TotalDroppedFrames;
                bool hasSignificantDrops = totalFrames > 0 &&
                                          TotalDroppedFrames > 0 &&
                                          (float)TotalDroppedFrames / totalFrames > 0.05f;

                if (hasSignificantDrops)
                {
                    return "starving";
                }
                // Low FPS but no drops = static content, report as healthy
            }

            if (BufferFullness > 0.8f) return "overflow";
            return "healthy";
        }

        /// <summary>
        /// Reset a counter when the issue is resolved.
        /// </summary>
        public void ResetMissedPongCount()
        {
            _missedPongCount = 0;
            UpdateHealthScore();
        }

        /// <summary>
        /// Reset stall counter (e.g., after successful reconnect).
        /// </summary>
        public void ResetStallCount()
        {
            _stallCount = 0;
            UpdateHealthScore();
        }

        /// <summary>
        /// Reset ICE disconnect counter.
        /// </summary>
        public void ResetIceDisconnectCount()
        {
            _iceDisconnectCount = 0;
            UpdateHealthScore();
        }

        /// <summary>
        /// Reset all counters (e.g., on fresh connection).
        /// </summary>
        public void ResetAll()
        {
            _pingSamples.Clear();
            _latencySamples.Clear();
            _lossRateSamples.Clear();
            _lastPingMs = 0;
            _missedPongCount = 0;
            _stallCount = 0;
            _iceDisconnectCount = 0;

            CurrentPingMs = 0;
            AveragePingMs = 0;
            MinPingMs = double.MaxValue;
            MaxPingMs = 0;
            JitterMs = 0;
            FrameLatencyMs = 0;
            AverageFrameLatencyMs = 0;
            PacketLossRate = 0;
            AveragePacketLossRate = 0;
            EffectiveFps = 0;
            BufferFullness = 0.5f;
            HealthScore = 100;
        }

        /// <summary>
        /// Update the health score based on current counters.
        /// </summary>
        private void UpdateHealthScore()
        {
            int oldScore = HealthScore;

            // Start at 100, deduct for issues
            int score = 100;
            score -= _missedPongCount * 20;      // Each missed pong = -20
            score -= _stallCount * 15;           // Each stall = -15
            score -= _iceDisconnectCount * 25;   // Each ICE disconnect = -25

            HealthScore = Math.Max(0, Math.Min(100, score));

            // Fire event if score changed significantly or crossed critical threshold
            if (HealthScore != oldScore)
            {
                OnHealthScoreChanged?.Invoke(HealthScore);
            }
        }

        /// <summary>
        /// Get a summary string for logging/debugging.
        /// </summary>
        public override string ToString()
        {
            string usbInfo = IsUsbMode ? $", USB: {UsbLatencyMs:F2}ms ({UsbVersion})" : "";
            return $"Ping: {CurrentPingMs:F1}ms (avg: {AveragePingMs:F1}ms), Jitter: {JitterMs:F1}ms, " +
                   $"Latency: {FrameLatencyMs:F1}ms, Health: {HealthScore}%{usbInfo}";
        }
    }
}