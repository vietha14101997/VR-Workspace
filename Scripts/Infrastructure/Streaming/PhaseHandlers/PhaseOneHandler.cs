using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Handles Phase 1 of the connection protocol:
    ///   - Parsing hardware_info and sending hardware_info_ack with codec capabilities
    ///   - Initiating / skipping the speed test
    ///   - Handling speedtest_start / speedtest_end / network_info from server
    ///   - Parsing suggested_config and deciding the final codec
    ///
    /// The handler does NOT own the WebSocket; it delegates sending to the
    /// provided <see cref="SendAsync"/> delegate supplied at construction time.
    /// All state it produces is surfaced through events or output properties so
    /// PhaseProtocolClient remains the single orchestrator.
    /// </summary>
    internal sealed class PhaseOneHandler
    {
        // ── Dependencies ─────────────────────────────────────────────────────────
        private readonly Func<string, Task> _send;          // WebSocket send delegate
        private readonly bool               _isUsbMode;
        private readonly ConnectionStateMachine _stateMachine;

        // ── Speed test ───────────────────────────────────────────────────────────
        private SpeedTestClient _speedTest;

        // ── Output properties (written during handling, read by orchestrator) ────
        public ServerHardwareInfo  HardwareInfo    { get; private set; }
        public NetworkTestResult   NetworkInfo     { get; private set; }
        public SuggestedStreamConfig SuggestedConfig { get; private set; }
        public VideoCodec          SelectedCodec   { get; private set; } = VideoCodec.H264;

        // ── Events ───────────────────────────────────────────────────────────────
        public event Action<ServerHardwareInfo>   OnHardwareInfoReady;
        public event Action<NetworkTestResult>    OnNetworkInfoReady;
        public event Action<SuggestedStreamConfig> OnSuggestedConfigReady;
        public event Action<string, double, int>  OnSpeedTestProgress; // direction, mbps, percent

        // ── Constructor ──────────────────────────────────────────────────────────
        public PhaseOneHandler(
            Func<string, Task>      send,
            ConnectionStateMachine  stateMachine,
            bool                    isUsbMode)
        {
            _send         = send         ?? throw new ArgumentNullException(nameof(send));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _isUsbMode    = isUsbMode;
        }

        // ── WebSocket integration ─────────────────────────────────────────────

        /// <summary>
        /// Wire up a SpeedTestClient once the WebSocket is open.
        /// Must be called before HandleHardwareInfoAsync.
        /// </summary>
        public void SetSpeedTestClient(SpeedTestClient speedTest)
        {
            _speedTest = speedTest;
        }

        // ── Phase 1 message handlers ──────────────────────────────────────────

        public async Task HandleHardwareInfoAsync(SimpleJson json)
        {
            Debug.Log("[Phase1] Received hardware_info");

            var device      = json.GetObject("device");
            var encoder     = json.GetObject("encoder");
            var monitorsArr = json.GetArray("monitors");

            HardwareInfo = new ServerHardwareInfo
            {
                deviceName      = device?.GetString("name")      ?? "Unknown",
                processor       = device?.GetString("processor") ?? "Unknown",
                gpu             = device?.GetString("gpu")       ?? "Unknown",
                gpuVramGB       = device?.GetInt("gpuVramGB")    ?? 0,
                ramGB           = device?.GetInt("ramGB")        ?? 0,
                os              = device?.GetString("os")        ?? "Unknown",
                encoderType     = encoder?.GetString("type")     ?? "Unknown",
                hwAccelEnabled  = encoder?.GetBool("hwAccel")    ?? false,
                monitors        = ParseMonitors(monitorsArr)
            };

            // Server codec capabilities
            var codecsArr = encoder?.GetArray("supportedCodecs");
            if (codecsArr != null)
            {
                HardwareInfo.supportedCodecs = codecsArr
                    .Select(c => c?.GetString("codec") ?? c?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }
            HardwareInfo.preferredCodec = encoder?.GetString("preferredCodec") ?? "H264";
            HardwareInfo.supportsHevc   = encoder?.GetBool("supportsHevc")     ?? false;

            Debug.Log($"[Phase1] Server: {HardwareInfo.deviceName}, GPU: {HardwareInfo.gpu} ({HardwareInfo.gpuVramGB}GB)");
            Debug.Log($"[Phase1] Encoder: {HardwareInfo.encoderType}, HW: {HardwareInfo.hwAccelEnabled}");

            OnHardwareInfoReady?.Invoke(HardwareInfo);

            // Gather client codec capability and send ack
            var clientCap = GetClientCodecCapability();

            var codecsArray   = string.Join(",", clientCap.supportedCodecs.Select(c => $"\"{c}\""));
            var supportsHevc  = clientCap.supportsHevc.ToString().ToLower();

            // Get client screen resolution for server-side resize decision
            // For VR headsets: Screen.currentResolution gives the device display resolution
            int screenWidth = Screen.currentResolution.width;
            int screenHeight = Screen.currentResolution.height;
            Debug.Log($"[Phase1] Client screen resolution: {screenWidth}x{screenHeight}");

            var ackJson       =
                $"{{\"type\":\"hardware_info_ack\",\"clientCodecs\":{{" +
                $"\"supportedCodecs\":[{codecsArray}]," +
                $"\"preferredCodec\":\"{clientCap.preferredCodec}\"," +
                $"\"supportsHevc\":{supportsHevc}," +
                $"\"deviceModel\":\"{EscapeJson(clientCap.deviceModel)}\"," +
                $"\"apiLevel\":{clientCap.apiLevel}," +
                $"\"screenWidth\":{screenWidth}," +
                $"\"screenHeight\":{screenHeight}" +
                $"}}}}";
            await _send(ackJson);
            Debug.Log("[Phase1] hardware_info_ack sent");

            _stateMachine.TryTransition(ConnectionPhase.SpeedTesting);

            if (_speedTest != null)
            {
                _speedTest.OnSpeedProgress += (dir, mbps, pct) =>
                    OnSpeedTestProgress?.Invoke(dir, mbps, pct);
            }

            _ = RunSpeedTestInBackgroundAsync();
        }

        public async Task HandleSpeedTestStartAsync(SimpleJson json)
        {
            var direction  = json.GetString("direction") ?? "download";
            var chunkSize  = json.GetInt("chunkSize");
            var durationMs = json.GetInt("durationMs");

            Debug.Log($"[Phase1] speedtest_start: dir={direction}, chunk={chunkSize}, dur={durationMs}ms");

            if (_speedTest != null)
                await _speedTest.HandleSpeedTestStartAsync(direction, chunkSize, durationMs);
        }

        public async Task HandleSpeedTestEndAsync(SimpleJson json)
        {
            var direction  = json.GetString("direction") ?? "download";
            var totalBytes = json.GetLong("totalBytes");
            var durationMs = json.GetLong("durationMs");

            Debug.Log($"[Phase1] speedtest_end: dir={direction}, phase={_stateMachine.CurrentPhase}");

            _speedTest?.HandleSpeedTestEnd();
            await (_speedTest?.HandleSpeedTestEndAsync(direction, totalBytes, durationMs) ?? Task.CompletedTask);

            if (direction == "upload")
                _stateMachine.TryTransition(ConnectionPhase.AwaitingNetworkInfo);
        }

        public void HandleNetworkInfo(SimpleJson json)
        {
            Debug.Log($"[Phase1] network_info received, phase={_stateMachine.CurrentPhase}");

            NetworkInfo = new NetworkTestResult
            {
                pingMs          = json.GetDouble("pingMs"),
                jitterMs        = json.GetDouble("jitterMs"),
                bandwidthMbps   = json.GetDouble("bandwidthMbps"),
                connectionType  = json.GetString("connectionType") ?? "Unknown"
            };

            Debug.Log($"[Phase1] Network: {NetworkInfo.connectionType}, Ping: {NetworkInfo.pingMs:F1}ms, BW: {NetworkInfo.bandwidthMbps:F0}Mbps");

            _stateMachine.TryTransition(ConnectionPhase.AwaitingSuggestedConfig);
            OnNetworkInfoReady?.Invoke(NetworkInfo);
        }

        public void HandleSuggestedConfig(SimpleJson json)
        {
            Debug.Log($"[Phase1] suggested_config received, phase={_stateMachine.CurrentPhase}");

            var resolution     = json.GetObject("resolution");
            string connType    = json.GetString("connectionType") ?? "Unknown";

            SuggestedConfig = new SuggestedStreamConfig
            {
                monitors        = json.GetInt("monitors"),
                resolutionWidth  = resolution?.GetInt("w") ?? 1920,
                resolutionHeight = resolution?.GetInt("h") ?? 1080,
                bitrateKbps     = json.GetInt("bitrateKbps"),
                fps             = json.GetInt("fps"),
                refreshRate     = json.GetInt("refreshRate"),
                reason          = json.GetString("reason") ?? "",
                selectedCodec   = json.GetString("selectedCodec") ?? "H264",
                connectionType  = connType,
                maxNativeHeight = json.GetInt("maxNativeHeight")
            };

            // Merge server-provided network info into our NetworkInfo object
            if (NetworkInfo != null && !string.IsNullOrEmpty(connType) && connType != "Unknown")
            {
                NetworkInfo.connectionType = connType;

                var netObj = json.GetObject("networkInfo");
                if (netObj != null)
                {
                    double pingMs       = netObj.GetDouble("pingMs");
                    double jitterMs     = netObj.GetDouble("jitterMs");
                    double bwMbps       = netObj.GetDouble("bandwidthMbps");
                    bool   isUsb        = netObj.GetBool("isUsbMode");
                    double usbLatencyMs = netObj.GetDouble("usbLatencyMs");
                    string usbVersion   = netObj.GetString("usbVersion");
                    double usbBwMbps    = netObj.GetDouble("usbEstimatedBandwidthMbps");

                    if (pingMs   > 0)  NetworkInfo.pingMs        = pingMs;
                    if (jitterMs >= 0) NetworkInfo.jitterMs       = jitterMs;
                    if (bwMbps   > 0)  NetworkInfo.bandwidthMbps  = bwMbps;

                    if (isUsb)
                    {
                        NetworkInfo.isUsbMode                  = true;
                        NetworkInfo.usbLatencyMs               = usbLatencyMs;
                        NetworkInfo.usbVersion                 = usbVersion;
                        NetworkInfo.usbEstimatedBandwidthMbps  = usbBwMbps;

                        Debug.Log($"[Phase1] USB mode activated: latency={usbLatencyMs:F2}ms, ver={usbVersion}, bw={usbBwMbps:F0}Mbps");
                    }
                }
                else
                {
                    Debug.LogWarning("[Phase1] networkInfo object is null in suggested_config");
                }

                OnNetworkInfoReady?.Invoke(NetworkInfo);
            }

            // Resolve codec enum from server string
            SelectedCodec = SuggestedConfig.selectedCodec.ToUpperInvariant() switch
            {
                "H265" => VideoCodec.H265,
                "VP9"  => VideoCodec.VP9,
                "VP8"  => VideoCodec.VP8,
                _      => VideoCodec.H264
            };

            // H265 capability gate: if server suggests H265 but device can't decode it, override to H264
            if (SelectedCodec == VideoCodec.H265 && !H265CapabilityTest.IsDeviceCapable())
            {
                Debug.LogWarning("[Phase1] Server suggested H265 but device failed capability test — overriding to H264");
                SelectedCodec = VideoCodec.H264;
                SuggestedConfig.selectedCodec = "H264";
            }

            Debug.Log($"[Phase1] Suggested: {SuggestedConfig.monitors}mon @ {SuggestedConfig.resolutionWidth}x{SuggestedConfig.resolutionHeight}, {SuggestedConfig.fps}fps, {SuggestedConfig.bitrateKbps}kbps, codec={SuggestedConfig.selectedCodec}, connType={connType}");

            // Handle race: suggested_config may arrive while still in SpeedTesting
            var phase = _stateMachine.CurrentPhase;
            if (phase == ConnectionPhase.SpeedTesting || phase == ConnectionPhase.AwaitingNetworkInfo)
                _stateMachine.ForceTransition(ConnectionPhase.ConfiguringSettings, "suggested_config received");
            else
                _stateMachine.TryTransition(ConnectionPhase.ConfiguringSettings);

            OnSuggestedConfigReady?.Invoke(SuggestedConfig);
        }

        // ── Forward binary-chunk count to SpeedTestClient ───────────────────────
        public void RecordBinaryBytes(int byteCount) =>
            _speedTest?.RecordBytesReceived(byteCount);

        // ── Helpers ──────────────────────────────────────────────────────────────

        private async Task RunSpeedTestInBackgroundAsync()
        {
            try
            {
                if (_isUsbMode)
                {
                    Debug.Log("[Phase1] USB Mode: skipping WebSocket speedtest, using server values");

                    NetworkInfo = new NetworkTestResult
                    {
                        pingMs                    = -1,
                        jitterMs                  = -1,
                        bandwidthMbps             = -1,
                        connectionType            = "USB",
                        isUsbMode                 = true,
                        usbLatencyMs              = -1,
                        usbEstimatedBandwidthMbps = -1
                    };

                    await SendSpeedTestResultAsync(0.5, 0.1, 480);
                    OnNetworkInfoReady?.Invoke(NetworkInfo);
                    _stateMachine.TryTransition(ConnectionPhase.AwaitingSuggestedConfig);
                    return;
                }

                // Standard WiFi/LAN speed test
                var result      = await _speedTest.RunSpeedTestAsync();
                var networkType = DetectNetworkAdapterType();

                // Fallback: classify by measured metrics when adapter detection fails
                if (networkType == "Unknown")
                    networkType = ClassifyByMetrics(result.PingMs, result.BandwidthMbps);

                NetworkInfo = new NetworkTestResult
                {
                    pingMs          = result.PingMs,
                    jitterMs        = result.JitterMs,
                    bandwidthMbps   = result.BandwidthMbps,
                    connectionType  = networkType
                };

                OnNetworkInfoReady?.Invoke(NetworkInfo);
                Debug.Log($"[Phase1] Speed test done: {NetworkInfo.bandwidthMbps:F1}Mbps, {NetworkInfo.pingMs:F1}ms ping, type={networkType}");
                _stateMachine.TryTransition(ConnectionPhase.AwaitingSuggestedConfig);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Phase1] Speed test failed: {ex.Message}");
            }
        }

        private async Task SendSpeedTestResultAsync(double pingMs, double jitterMs, double bwMbps)
        {
            var ci       = System.Globalization.CultureInfo.InvariantCulture;
            var json     =
                "{\"type\":\"speedtest_result\",\"bandwidthMbps\":" + bwMbps.ToString("F1", ci) +
                ",\"pingMs\":"   + pingMs.ToString("F1", ci) +
                ",\"jitterMs\":" + jitterMs.ToString("F1", ci) + "}";
            await _send(json);
            Debug.Log($"[Phase1] speedtest_result sent: {bwMbps:F1}Mbps, {pingMs:F1}ms");
        }

        private static MonitorInfo[] ParseMonitors(System.Collections.Generic.List<SimpleJson> arr)
        {
            if (arr == null) return Array.Empty<MonitorInfo>();
            return arr.Select(m => new MonitorInfo
            {
                id        = m.GetInt("id"),
                name      = m.GetString("name") ?? "",
                width     = m.GetInt("w"),
                height    = m.GetInt("h"),
                isVirtual = m.GetBool("isVirtual")
            }).ToArray();
        }

        private static string DetectNetworkAdapterType()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)  continue;
                    if (ni.GetIPProperties().UnicastAddresses.Count == 0) continue;

                    switch (ni.NetworkInterfaceType)
                    {
                        case System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211: return "Wi-Fi";
                        case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet:
                        case System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet:
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT:
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx: return "Ethernet";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Phase1] Network adapter detection failed: {ex.Message}");
            }
            return "Unknown";
        }

        /// <summary>
        /// Fallback classification when adapter detection fails (Android/VR headsets).
        /// Uses measured ping and bandwidth to infer connection type.
        /// </summary>
        private static string ClassifyByMetrics(double pingMs, double bandwidthMbps)
        {
            if (pingMs < 3 && bandwidthMbps > 500) return "Ethernet";
            if (pingMs < 30 && bandwidthMbps > 30) return "Wi-Fi";
            return "Internet";
        }

        /// <summary>
        /// Discover client codec capabilities.
        /// Checks HevcDecoderPlugin.IsAvailable() to detect H265 hardware decoder.
        /// </summary>
        private static ClientCodecCapability GetClientCodecCapability()
        {
            // Check if HEVC hardware decoder is available via HevcDecoder.aar
            bool hevcAvailable = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                hevcAvailable = Native.HevcDecoderPlugin.IsAvailable();
                Debug.Log($"[Phase1] HEVC hardware decoder available: {hevcAvailable}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Phase1] HEVC availability check failed: {ex.Message}");
            }
#endif

            var codecs = hevcAvailable
                ? new[] { "H264", "H265", "VP9", "VP8" }
                : new[] { "H264", "VP9", "VP8" };

            var cap = new ClientCodecCapability
            {
                supportedCodecs = codecs,
                preferredCodec  = hevcAvailable ? "H265" : "H264",
                supportsHevc    = hevcAvailable,
                supportsVP9     = true,
                supportsVP8     = true,
                deviceModel     = UnityEngine.SystemInfo.deviceModel,
                apiLevel        = 0
            };

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var ver = new UnityEngine.AndroidJavaClass("android.os.Build$VERSION"))
                    cap.apiLevel = ver.GetStatic<int>("SDK_INT");
                Debug.Log($"[Phase1] Android API {cap.apiLevel}, device: {cap.deviceModel}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Phase1] Failed to get Android device info: {ex.Message}");
            }
#endif
            return cap;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
