using System;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Video codec types.
    /// </summary>
    public enum VideoCodec
    {
        H264,   // AVC - Hardware accelerated
        H265,   // HEVC - Better compression
        VP9,    // libvpx-vp9 (fallback)
        VP8     // libvpx (final fallback)
    }

    /// <summary>
    /// Server hardware info received in Phase 1.
    /// </summary>
    [Serializable]
    public class ServerHardwareInfo
    {
        public string deviceName;
        public string processor;
        public string gpu;
        public int gpuVramGB;
        public int ramGB;
        public string os;
        public string encoderType;
        public bool hwAccelEnabled;
        public MonitorInfo[] monitors;

        // Codec capability fields
        public string[] supportedCodecs;    // ["H264", "H265", "VP9", "VP8"]
        public string preferredCodec;       // Preferred codec
        public bool supportsHevc;           // H.265 support
        public bool supportsVP9;            // VP9 support (libvpx-vp9)
        public bool supportsVP8;            // VP8 support (libvpx)
    }

    /// <summary>
    /// Client codec capabilities sent to server.
    /// </summary>
    [Serializable]
    public class ClientCodecCapability
    {
        public string[] supportedCodecs;    // Codecs client can decode (H264, VP9, VP8)
        public string preferredCodec;       // Client's preferred codec
        public bool supportsHevc;           // HEVC hardware decoder available
        public bool supportsVP9;            // VP9 decoding support (Unity WebRTC native)
        public bool supportsVP8;            // VP8 decoding support (Unity WebRTC native)
        public string deviceModel;          // Android device model
        public int apiLevel;                // Android API level
    }

    /// <summary>
    /// Monitor info from server.
    /// </summary>
    [Serializable]
    public class MonitorInfo
    {
        public int id;
        public string name;
        public int width;
        public int height;
        public bool isVirtual;
    }

    /// <summary>
    /// Network test results from Phase 1.
    /// </summary>
    [Serializable]
    public class NetworkTestResult
    {
        public double pingMs;
        public double jitterMs;
        public double bandwidthMbps;
        public string connectionType; // LAN, WiFi, Internet, USB

        // USB Tethering (RNDIS) specific fields
        /// <summary>True if connection is via USB Tethering.</summary>
        public bool isUsbMode;

        /// <summary>USB-specific ICMP latency in ms (typically < 1ms).</summary>
        public double usbLatencyMs;

        /// <summary>USB interface version: "USB 2.0" or "USB 3.0".</summary>
        public string usbVersion;

        /// <summary>Estimated bandwidth for USB mode in Mbps.</summary>
        public double usbEstimatedBandwidthMbps;
    }

    /// <summary>
    /// Suggested streaming configuration from server.
    /// </summary>
    [Serializable]
    public class SuggestedStreamConfig
    {
        public int monitors = 3;
        public int resolutionWidth = 1920;
        public int resolutionHeight = 1080;
        public int bitrateKbps = 20000;
        public int fps = 60;
        public int refreshRate = 60;
        public string reason;
        public string selectedCodec = "H264";  // Codec negotiated for streaming
        public string connectionType = "Unknown";  // USB, WiFi, LAN, Internet
    }

    /// <summary>
    /// User's chosen streaming configuration to send to server.
    /// </summary>
    [Serializable]
    public class StreamingConfig
    {
        public int monitors = 3;
        public int resolutionWidth = 1920;
        public int resolutionHeight = 1080;
        public int refreshRate = 60;
        public int bitrateKbps = 20000;
        public int fps = 60;
        public string preferGpu;
        public string selectedCodec = "H264";  // Negotiated codec for streaming

        /// <summary>
        /// Create config from suggested config.
        /// </summary>
        public static StreamingConfig FromSuggested(SuggestedStreamConfig suggested)
        {
            return new StreamingConfig
            {
                monitors = suggested.monitors,
                resolutionWidth = suggested.resolutionWidth,
                resolutionHeight = suggested.resolutionHeight,
                refreshRate = suggested.refreshRate,
                bitrateKbps = suggested.bitrateKbps,
                fps = suggested.fps,
                selectedCodec = suggested.selectedCodec ?? "H264"
            };
        }

        /// <summary>
        /// Create default config.
        /// </summary>
        public static StreamingConfig CreateDefault()
        {
            return new StreamingConfig
            {
                monitors = 3,
                resolutionWidth = 1920,
                resolutionHeight = 1080,
                refreshRate = 60,
                bitrateKbps = 20000,
                fps = 60
            };
        }
    }

    /// <summary>
    /// Optimizes streaming configuration based on hardware and network conditions.
    /// Matches server-side StreamingOptimizer logic.
    /// </summary>
    public static class StreamingOptimizer
    {
        // Resolution presets
        public static readonly (int w, int h)[] ResolutionPresets = new[]
        {
            (1920, 1080),   // Full HD
            (1600, 900),    // HD+
            (1366, 768),    // HD
            (1280, 720),    // 720p
            (2560, 1440),   // 1440p (high-end)
        };

        // FPS presets
        public static readonly int[] FpsPresets = new[] { 30, 45, 60, 90, 120 };

        // Bitrate presets (Kbps)
        public static readonly int[] BitratePresets = new[] { 5000, 10000, 15000, 20000, 25000, 30000 };

        /// <summary>
        /// Calculate optimal configuration based on server hardware and network.
        /// This mirrors the server-side CalculateSuggestedConfig logic.
        /// </summary>
        public static SuggestedStreamConfig CalculateOptimalConfig(
            ServerHardwareInfo hardware,
            NetworkTestResult network)
        {
            var config = new SuggestedStreamConfig();

            // Resolution based on GPU VRAM (now in GB)
            if (hardware.gpuVramGB >= 8) // 8GB+
            {
                config.resolutionWidth = 1920;
                config.resolutionHeight = 1080;
            }
            else if (hardware.gpuVramGB >= 4) // 4GB
            {
                config.resolutionWidth = 1600;
                config.resolutionHeight = 900;
            }
            else // <4GB
            {
                config.resolutionWidth = 1366;
                config.resolutionHeight = 768;
            }

            // Calculate available bandwidth per monitor (70% of measured, divided by 3)
            double availableBandwidth = network.bandwidthMbps > 0 ? network.bandwidthMbps : 100;
            double bitratePerMonitor = availableBandwidth * 1000 * 0.7 / 3;

            // Clamp bitrate to reasonable range (higher minimum for text clarity)
            config.bitrateKbps = (int)Math.Clamp(bitratePerMonitor, 15000, 40000);

            // LAN quality override: maximize quality for local connections
            if ((network.connectionType == "Ethernet" || network.connectionType == "LAN")
                && network.bandwidthMbps > 500)
            {
                config.bitrateKbps = 40000;  // Max quality for LAN
            }

            // FPS based on encoder capability and ping
            if (hardware.hwAccelEnabled && network.pingMs < 20)
            {
                config.fps = 60;
                config.refreshRate = 60;
            }
            else if (hardware.hwAccelEnabled && network.pingMs < 50)
            {
                config.fps = 45;
                config.refreshRate = 60;
            }
            else
            {
                config.fps = 30;
                config.refreshRate = 60;
            }

            // Monitor count based on total available bandwidth
            double totalRequired = config.bitrateKbps * 3;
            double totalAvailable = availableBandwidth * 1000 * 0.7;

            if (totalAvailable >= totalRequired)
                config.monitors = 3;
            else if (totalAvailable >= totalRequired * 2 / 3)
                config.monitors = 2;
            else
                config.monitors = 1;

            // Build reason string
            config.reason = BuildReasonString(hardware, network, config);

            Debug.Log($"[StreamingOptimizer] Calculated config: {config.resolutionWidth}x{config.resolutionHeight} @ {config.fps}fps, {config.bitrateKbps}kbps, {config.monitors} monitors");
            Debug.Log($"[StreamingOptimizer] Reason: {config.reason}");

            return config;
        }

        private static string BuildReasonString(
            ServerHardwareInfo hardware,
            NetworkTestResult network,
            SuggestedStreamConfig config)
        {
            var reasons = new System.Collections.Generic.List<string>();

            // Resolution reason
            if (hardware.gpuVramGB >= 8)
                reasons.Add($"1080p (VRAM: {hardware.gpuVramGB}GB)");
            else if (hardware.gpuVramGB >= 4)
                reasons.Add($"900p (VRAM: {hardware.gpuVramGB}GB)");
            else
                reasons.Add($"768p (VRAM limited)");

            // Bitrate reason
            reasons.Add($"{config.bitrateKbps / 1000}Mbps (BW: {network.bandwidthMbps:F0}Mbps)");

            // FPS reason
            reasons.Add($"{config.fps}fps ({hardware.encoderType}, ping: {network.pingMs:F0}ms)");

            return string.Join(" | ", reasons);
        }

        /// <summary>
        /// Validate user configuration against hardware/network limits.
        /// Returns warning messages if config may cause issues.
        /// </summary>
        public static string[] ValidateConfig(
            StreamingConfig config,
            ServerHardwareInfo hardware,
            NetworkTestResult network)
        {
            var warnings = new System.Collections.Generic.List<string>();

            // Check bandwidth requirements
            double totalBitrateNeeded = config.bitrateKbps * config.monitors;
            double availableBandwidth = network.bandwidthMbps * 1000 * 0.8; // 80% safety margin

            if (totalBitrateNeeded > availableBandwidth)
            {
                warnings.Add($"Bitrate ({totalBitrateNeeded / 1000:F0}Mbps total) may exceed bandwidth ({network.bandwidthMbps:F0}Mbps)");
            }

            // Check VRAM for resolution (now in GB)
            int pixelCount = config.resolutionWidth * config.resolutionHeight * config.monitors;
            long estimatedVramGB = pixelCount * 4 / (1024 * 1024 * 1024); // Rough estimate in GB
            if (hardware.gpuVramGB > 0 && estimatedVramGB > hardware.gpuVramGB * 0.5)
            {
                warnings.Add($"High resolution may strain GPU VRAM ({hardware.gpuVramGB}GB)");
            }

            // Check FPS with software encoder
            if (!hardware.hwAccelEnabled && config.fps > 30)
            {
                warnings.Add("Software encoder: 60fps may cause high CPU usage");
            }

            // Check high latency with high FPS
            if (network.pingMs > 50 && config.fps > 45)
            {
                warnings.Add($"High ping ({network.pingMs:F0}ms): 60fps may feel choppy");
            }

            return warnings.ToArray();
        }

        /// <summary>
        /// Get connection quality rating based on network test.
        /// </summary>
        public static string GetConnectionQuality(NetworkTestResult network)
        {
            // USB connection is always excellent quality (stable, high bandwidth potential)
            // TCP speedtest can't measure true USB 3.0 bandwidth, but UDP streaming can use it
            if (network.connectionType == "USB")
                return "Excellent (USB)";

            if (network.pingMs < 5 && network.bandwidthMbps > 500)
                return "Excellent (LAN)";
            if (network.pingMs < 20 && network.bandwidthMbps > 100)
                return "Very Good";
            if (network.pingMs < 50 && network.bandwidthMbps > 50)
                return "Good";
            if (network.pingMs < 100 && network.bandwidthMbps > 20)
                return "Fair";
            return "Poor";
        }

        /// <summary>
        /// Estimate expected latency based on config and network.
        /// </summary>
        public static double EstimateLatencyMs(StreamingConfig config, NetworkTestResult network)
        {
            // Base latency = network RTT + encoding + decoding
            double baseLatency = network.pingMs;

            // Encoding latency estimate (lower for hardware encoder, higher FPS = lower frame time)
            double encodingLatency = 1000.0 / config.fps; // One frame time

            // Higher bitrate = potentially more buffering
            double bufferLatency = config.bitrateKbps > 20000 ? 10 : 5;

            return baseLatency + encodingLatency + bufferLatency;
        }

        /// <summary>
        /// Format hardware info for display.
        /// </summary>
        public static string FormatHardwareInfo(ServerHardwareInfo hw)
        {
            return $@"Device: {hw.deviceName}
CPU: {hw.processor}
GPU: {hw.gpu} ({hw.gpuVramGB}GB VRAM)
RAM: {hw.ramGB}GB
OS: {hw.os}
Encoder: {hw.encoderType} (HW: {(hw.hwAccelEnabled ? "Yes" : "No")})";
        }

        /// <summary>
        /// Format network info for display.
        /// </summary>
        public static string FormatNetworkInfo(NetworkTestResult net)
        {
            return $@"Connection: {net.connectionType}
Ping: {net.pingMs:F1}ms
Jitter: {net.jitterMs:F1}ms
Bandwidth: {net.bandwidthMbps:F0} Mbps
Quality: {GetConnectionQuality(net)}";
        }

        /// <summary>
        /// Format config for display.
        /// </summary>
        public static string FormatConfig(StreamingConfig cfg)
        {
            return $"{cfg.monitors} monitors @ {cfg.resolutionWidth}x{cfg.resolutionHeight}, {cfg.fps}fps, {cfg.bitrateKbps / 1000}Mbps";
        }
    }
}
