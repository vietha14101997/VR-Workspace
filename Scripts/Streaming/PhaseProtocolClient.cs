using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Native;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Client-side handler for 3-phase connection protocol.
    /// Manages WebSocket connection, phase transitions, and WebRTC setup.
    /// </summary>
    public class PhaseProtocolClient : IDisposable
    {
        // Connection
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly object _lock = new object();

        // State
        private readonly ConnectionStateMachine _stateMachine = new ConnectionStateMachine();
        private SpeedTestClient _speedTest;

        // Phase 1 data
        private ServerHardwareInfo _hardwareInfo;
        private NetworkTestResult _networkInfo;
        private SuggestedStreamConfig _suggestedConfig;

        // Phase 2 data
        private StreamingConfig _userConfig;
        private List<MonitorInfo> _configuredMonitors = new List<MonitorInfo>();

        // WebRTC
        private readonly List<PCWrapper> _peerConnections = new List<PCWrapper>();
        private bool _skipTcpIceCandidates = true;

        // Events
        public event Action<ServerHardwareInfo> OnHardwareInfoReceived;
        public event Action<NetworkTestResult> OnNetworkInfoReceived;
        public event Action<SuggestedStreamConfig> OnSuggestedConfigReceived;
        public event Action<string, int, string> OnConfigProgress; // step, progress, message
        public event Action<List<MonitorInfo>> OnConfigComplete;
        public event Action OnReadyToStream;
        public event Action<int, Texture> OnVideoTextureReceived; // monitorIndex, texture
        public event Action OnStreamingStarted;
        public event Action<string> OnError;
        public event Action OnDisconnected;
        public event Action<int, float, float, bool> OnCursorPosition; // monitorIndex, u, v, visible

        private class PCWrapper
        {
            public int Index;
            public RTCPeerConnection PC;
            public VideoStreamTrack VideoTrack;
            public Texture Texture;
            public bool AnswerSet;
            public List<string> PendingIce = new List<string>();
        }

        // Public properties
        public ConnectionStateMachine StateMachine => _stateMachine;
        public ServerHardwareInfo HardwareInfo => _hardwareInfo;
        public NetworkTestResult NetworkInfo => _networkInfo;
        public SuggestedStreamConfig SuggestedConfig => _suggestedConfig;
        public StreamingConfig UserConfig => _userConfig;
        public int MonitorCount => _peerConnections.Count;
        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public bool IsStreaming => _stateMachine.IsStreaming;

        /// <summary>
        /// Get video texture for a specific monitor.
        /// </summary>
        public Texture GetTexture(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _peerConnections.Count)
                    return _peerConnections[index].Texture;
                return null;
            }
        }

        /// <summary>
        /// Connect to server and begin Phase 1.
        /// </summary>
        public async Task ConnectAsync(string serverUrl)
        {
            if (_stateMachine.IsConnected)
            {
                Debug.LogWarning("[PhaseProtocol] Already connected");
                return;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Ensure protocol=v2 is in URL
            if (!serverUrl.Contains("protocol="))
            {
                serverUrl += serverUrl.Contains("?") ? "&protocol=v2" : "?protocol=v2";
            }

            Debug.Log($"[PhaseProtocol] Connecting to {serverUrl}");
            _stateMachine.TryTransition(ConnectionPhase.Connecting);

            try
            {
                _ws = new ClientWebSocket();
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await _ws.ConnectAsync(new Uri(serverUrl), ct);

                Debug.Log("[PhaseProtocol] WebSocket connected, waiting for hardware_info");
                _stateMachine.TryTransition(ConnectionPhase.AwaitingHardwareInfo);

                // Create speed test handler
                _speedTest = new SpeedTestClient(_ws, ct);

                // Start receive loop
                _ = ReceiveLoopAsync(ct);

                // Start keepalive
                _ = KeepaliveLoopAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Connection failed: {ex.Message}");
                _stateMachine.ForceTransition(ConnectionPhase.Error, ex.Message);
                OnError?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// Proceed from Phase 1 to Phase 2 with user configuration.
        /// </summary>
        public async Task ProceedToPhase2Async(StreamingConfig config)
        {
            Debug.Log($"[PhaseProtocol] ProceedToPhase2Async called, current phase: {_stateMachine.CurrentPhase}");

            if (!_stateMachine.IsInPhase(ConnectionPhase.ConfiguringSettings))
            {
                Debug.LogWarning($"[PhaseProtocol] Cannot proceed to Phase 2 from {_stateMachine.CurrentPhase}");
                return;
            }

            _userConfig = config;
            Debug.Log($"[PhaseProtocol] Proceeding to Phase 2 with config: {StreamingOptimizer.FormatConfig(config)}");

            _stateMachine.TryTransition(ConnectionPhase.SendingDisplayConfig);

            // Send proceed message
            // NOTE: Build JSON manually because SimpleJson.Serialize doesn't work with anonymous objects on Android IL2CPP
            Debug.Log("[PhaseProtocol] Sending proceed message (phase 2)");
            await SendTextAsync("{\"type\":\"proceed\",\"phase\":2}");
            Debug.Log("[PhaseProtocol] Proceed message sent");

            // Send display config
            // NOTE: Build JSON manually because SimpleJson.Serialize doesn't work with anonymous objects on Android IL2CPP
            Debug.Log("[PhaseProtocol] Sending display_config message");
            var preferGpuStr = string.IsNullOrEmpty(config.preferGpu) ? "null" : $"\"{EscapeJsonString(config.preferGpu)}\"";
            var displayConfigJson = $"{{\"type\":\"display_config\",\"monitors\":{config.monitors}," +
                $"\"resolution\":{{\"w\":{config.resolutionWidth},\"h\":{config.resolutionHeight}}}," +
                $"\"refreshRate\":{config.refreshRate},\"bitrateKbps\":{config.bitrateKbps}," +
                $"\"fps\":{config.fps},\"preferGpu\":{preferGpuStr}}}";
            await SendTextAsync(displayConfigJson);
            Debug.Log("[PhaseProtocol] Display config sent");

            _stateMachine.TryTransition(ConnectionPhase.AwaitingSetupComplete);
        }

        /// <summary>
        /// Proceed from Phase 2 to Phase 3 (start streaming).
        /// </summary>
        public async Task StartStreamingAsync()
        {
            if (!_stateMachine.IsInPhase(ConnectionPhase.ReadyToStream))
            {
                Debug.LogWarning($"[PhaseProtocol] Cannot start streaming from {_stateMachine.CurrentPhase}");
                return;
            }

            Debug.Log("[PhaseProtocol] Starting streaming (Phase 3)");
            _stateMachine.TryTransition(ConnectionPhase.StartingStream);

            await SendTextAsync("{\"type\":\"start_streaming\"}");
        }

        /// <summary>
        /// Stop streaming and disconnect.
        /// </summary>
        public async Task StopAsync()
        {
            Debug.Log("[PhaseProtocol] Stopping...");

            if (_ws?.State == WebSocketState.Open)
            {
                try
                {
                    await SendTextAsync("{\"type\":\"stop_streaming\"}");
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stop", CancellationToken.None);
                }
                catch { }
            }

            Cleanup();
            _stateMachine.Reset();
            OnDisconnected?.Invoke();
        }

        /// <summary>
        /// Main receive loop for WebSocket messages.
        /// </summary>
        private int _msgCounter = 0;

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[256 * 1024]; // 256KB buffer for speed test chunks

            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Debug.Log("[PhaseProtocol] WebSocket closed by server");
                            _stateMachine.ForceTransition(ConnectionPhase.Disconnected);
                            OnDisconnected?.Invoke();
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    _msgCounter++;

                    // Handle message
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        // Speed test binary data
                        Debug.Log($"[PhaseProtocol] MSG#{_msgCounter} Binary data, len={ms.Length}");
                        _speedTest?.HandleBinaryData(ms.ToArray(), (int)ms.Length);
                    }
                    else
                    {
                        var text = Encoding.UTF8.GetString(ms.ToArray());
                        Debug.Log($"[PhaseProtocol] MSG#{_msgCounter} Text, len={text.Length}, phase={_stateMachine.CurrentPhase}");
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            await HandleTextMessageAsync(text);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[PhaseProtocol] Receive loop cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Receive error: {ex.Message}");
                _stateMachine.ForceTransition(ConnectionPhase.Error, ex.Message);
                OnError?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// Handle incoming text message.
        /// </summary>
        private async Task HandleTextMessageAsync(string text)
        {
            // Debug: Log received message (truncated), skip noisy cursor_position
            if (!text.Contains("cursor_position"))
                Debug.Log($"[PhaseProtocol] Received: {text.Substring(0, Math.Min(100, text.Length))}...");

            // Try to parse as JSON
            if (text.StartsWith("{"))
            {
                try
                {
                    var json = SimpleJson.Parse(text);
                    // Handle both "type" (camelCase) and "Type" (PascalCase) from server
                    var type = json.GetString("type") ?? json.GetString("Type");
                    if (type != "cursor_position")
                        Debug.Log($"[PhaseProtocol] Parsed type='{type}' from message len={text.Length}");

                    switch (type)
                    {
                        case "hardware_info":
                            Debug.Log("[PhaseProtocol] >>> Handling hardware_info");
                            await HandleHardwareInfoAsync(json);
                            break;

                        case "speedtest_start":
                            Debug.Log("[PhaseProtocol] >>> Handling speedtest_start");
                            await HandleSpeedTestStartAsync(json);
                            break;

                        case "speedtest_end":
                            Debug.Log("[PhaseProtocol] >>> Handling speedtest_end");
                            await HandleSpeedTestEndAsync(json);
                            break;

                        case "network_info":
                            Debug.Log("[PhaseProtocol] >>> Handling network_info");
                            HandleNetworkInfo(json);
                            break;

                        case "suggested_config":
                            Debug.Log("[PhaseProtocol] >>> Handling suggested_config");
                            HandleSuggestedConfig(json);
                            break;

                        case "config_progress":
                            Debug.Log("[PhaseProtocol] >>> Handling config_progress");
                            HandleConfigProgress(json);
                            break;

                        case "config_complete":
                            Debug.Log("[PhaseProtocol] >>> Handling config_complete");
                            await HandleConfigCompleteAsync(json);
                            Debug.Log("[PhaseProtocol] <<< Finished config_complete");
                            break;

                        case "answer":
                            Debug.Log($"[PhaseProtocol] >>> Handling answer for monitor {json.GetInt("monitorIndex")}");
                            await HandleAnswerAsync(json);
                            Debug.Log($"[PhaseProtocol] <<< Finished answer handler");
                            break;

                        case "candidate":
                            Debug.Log($"[PhaseProtocol] >>> Handling candidate for monitor {json.GetInt("monitorIndex")}");
                            HandleCandidate(json);
                            break;

                        case "end_of_candidates":
                            Debug.Log("[PhaseProtocol] >>> Handling end_of_candidates");
                            HandleEndOfCandidates(json);
                            break;

                        case "ice_ready":
                            Debug.Log("[PhaseProtocol] >>> Handling ice_ready");
                            HandleIceReady(json);
                            break;

                        case "streaming_started":
                            Debug.Log("[PhaseProtocol] >>> Handling streaming_started");
                            HandleStreamingStarted(json);
                            break;

                        case "cursor_position":
                            // Don't log every cursor update (too noisy)
                            HandleCursorPosition(json);
                            break;

                        case "error":
                            Debug.Log("[PhaseProtocol] >>> Handling error");
                            HandleError(json);
                            break;

                        case "pong":
                            // Notify speed test client for ping measurement
                            _speedTest?.HandlePong();
                            break;

                        case "frameTiming":
                            // Diagnostic message from server - ignore (or could be used for frame timing analysis)
                            break;

                        case null:
                        case "":
                            Debug.LogWarning($"[PhaseProtocol] Empty/null type! Raw message: {text.Substring(0, Math.Min(300, text.Length))}");
                            break;

                        default:
                            Debug.LogWarning($"[PhaseProtocol] Unknown message type: '{type}' from message: {text.Substring(0, Math.Min(200, text.Length))}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PhaseProtocol] Failed to parse JSON: {ex.Message}");
                }
            }
            else if (text.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                await _speedTest?.SendPongAsync();
            }
            else if (text.Equals("pong", StringComparison.OrdinalIgnoreCase))
            {
                // Notify speed test client for ping measurement
                _speedTest?.HandlePong();
            }
            else
            {
                // Legacy format handling (offer:N:sdp, answer:N:sdp, candidate:N:candidate)
                await HandleLegacyMessageAsync(text);
            }
        }

        // === Phase 1 Handlers ===

        // Event for speed test progress (direction, currentMbps, progress%)
        public event Action<string, double, int> OnSpeedTestProgress;

        // Selected codec for this session
        private VideoCodec _selectedCodec = VideoCodec.H264;
        public VideoCodec SelectedCodec => _selectedCodec;

        /// <summary>
        /// Get client codec capabilities for negotiation.
        /// </summary>
        private ClientCodecCapability GetClientCodecCapability()
        {
            var capability = new ClientCodecCapability
            {
                supportedCodecs = new[] { "H264" },
                preferredCodec = "H264",
                supportsHevc = false,
                deviceModel = SystemInfo.deviceModel,
                apiLevel = 0
            };

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Get Android API level
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    capability.apiLevel = version.GetStatic<int>("SDK_INT");
                }
                Debug.Log($"[PhaseProtocol] Android API level: {capability.apiLevel}, device: {capability.deviceModel}");

                // Check HEVC decoder availability via native plugin
                bool pluginAvailable = false;
                try
                {
                    pluginAvailable = HevcDecoderPlugin.IsAvailable();
                    Debug.Log($"[PhaseProtocol] HevcDecoderPlugin.IsAvailable() = {pluginAvailable}");
                }
                catch (Exception pluginEx)
                {
                    Debug.LogWarning($"[PhaseProtocol] HevcDecoderPlugin check failed: {pluginEx.Message}");
                }

                // HEVC detection with fallback:
                // 1. If native plugin reports available -> use HEVC
                // 2. If plugin fails but Android API >= 21 -> assume HEVC available (MediaCodec supports it)
                // Note: Android 5.0 (API 21) introduced hardware HEVC decoding in MediaCodec
                if (pluginAvailable)
                {
                    capability.supportsHevc = true;
                    capability.supportedCodecs = new[] { "H265", "H264" };
                    capability.preferredCodec = "H265";
                    Debug.Log("[PhaseProtocol] Client supports HEVC via native plugin");
                }
                else if (capability.apiLevel >= 21)
                {
                    // Fallback: Android 5.0+ has MediaCodec HEVC support
                    // Use system MediaCodec check as fallback
                    bool mediaCodecHevc = CheckMediaCodecHevcSupport();
                    if (mediaCodecHevc)
                    {
                        capability.supportsHevc = true;
                        capability.supportedCodecs = new[] { "H265", "H264" };
                        capability.preferredCodec = "H265";
                        Debug.Log($"[PhaseProtocol] Client supports HEVC via MediaCodec fallback (API {capability.apiLevel})");
                    }
                    else
                    {
                        Debug.Log($"[PhaseProtocol] MediaCodec HEVC not available despite API {capability.apiLevel}");
                    }
                }
                else
                {
                    Debug.Log($"[PhaseProtocol] HEVC not available: plugin={pluginAvailable}, API={capability.apiLevel}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Failed to check HEVC capability: {ex.Message}");
            }
#else
            Debug.Log("[PhaseProtocol] Non-Android platform, using H.264 only");
#endif

            return capability;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Check if Android MediaCodec supports HEVC decoding.
        /// This is a fallback when native plugin is unavailable.
        /// Uses fast path: check for known HEVC decoder directly instead of iterating all codecs.
        /// </summary>
        private bool CheckMediaCodecHevcSupport()
        {
            try
            {
                Debug.Log("[PhaseProtocol] CheckMediaCodecHevcSupport: Starting fast path check...");

                // Fast path: Try to create HEVC decoder directly (much faster than iterating all codecs)
                using (var mediaCodec = new AndroidJavaClass("android.media.MediaCodec"))
                {
                    try
                    {
                        // Try to create decoder for video/hevc - if it succeeds, HEVC is supported
                        using (var decoder = mediaCodec.CallStatic<AndroidJavaObject>("createDecoderByType", "video/hevc"))
                        {
                            if (decoder != null)
                            {
                                string name = decoder.Call<string>("getName") ?? "unknown";
                                Debug.Log($"[PhaseProtocol] HEVC decoder available: {name}");
                                decoder.Call("release");
                                return true;
                            }
                        }
                    }
                    catch (Exception createEx)
                    {
                        Debug.Log($"[PhaseProtocol] No HEVC decoder available: {createEx.Message}");
                    }
                }

                Debug.Log("[PhaseProtocol] HEVC decoder not found via fast path");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] MediaCodec HEVC check failed: {ex.Message}");
                // On error, assume HEVC is supported if API >= 21 (most modern devices have it)
                Debug.Log("[PhaseProtocol] Assuming HEVC support due to error (API >= 21 fallback)");
                return true;
            }
        }
#endif

        private async Task HandleHardwareInfoAsync(SimpleJson json)
        {
            Debug.Log("[PhaseProtocol] Received hardware_info");

            var device = json.GetObject("device");
            var encoder = json.GetObject("encoder");
            var monitorsArr = json.GetArray("monitors");

            _hardwareInfo = new ServerHardwareInfo
            {
                deviceName = device?.GetString("name") ?? "Unknown",
                processor = device?.GetString("processor") ?? "Unknown",
                gpu = device?.GetString("gpu") ?? "Unknown",
                gpuVramGB = device?.GetInt("gpuVramGB") ?? 0,
                ramGB = device?.GetInt("ramGB") ?? 0,
                os = device?.GetString("os") ?? "Unknown",
                encoderType = encoder?.GetString("type") ?? "Unknown",
                hwAccelEnabled = encoder?.GetBool("hwAccel") ?? false,
                monitors = ParseMonitors(monitorsArr)
            };

            // Parse server codec capabilities
            var supportedCodecsArr = encoder?.GetArray("supportedCodecs");
            if (supportedCodecsArr != null)
            {
                _hardwareInfo.supportedCodecs = supportedCodecsArr
                    .Select(c => c?.GetString("codec") ?? c?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }
            _hardwareInfo.preferredCodec = encoder?.GetString("preferredCodec") ?? "H264";
            _hardwareInfo.supportsHevc = encoder?.GetBool("supportsHevc") ?? false;

            Debug.Log($"[PhaseProtocol] Server: {_hardwareInfo.deviceName}, GPU: {_hardwareInfo.gpu} ({_hardwareInfo.gpuVramGB}GB)");
            Debug.Log($"[PhaseProtocol] Encoder: {_hardwareInfo.encoderType}, HW: {_hardwareInfo.hwAccelEnabled}");
            Debug.Log($"[PhaseProtocol] Server codecs: [{string.Join(", ", _hardwareInfo.supportedCodecs ?? new[] { "H264" })}], prefers: {_hardwareInfo.preferredCodec}, HEVC: {_hardwareInfo.supportsHevc}");

            // Fire event immediately so UI can show hardware info
            OnHardwareInfoReceived?.Invoke(_hardwareInfo);

            // Get client codec capabilities
            Debug.Log("[PhaseProtocol] Getting client codec capabilities...");
            ClientCodecCapability clientCapability;
            try
            {
                clientCapability = GetClientCodecCapability();
                Debug.Log($"[PhaseProtocol] Client codecs: [{string.Join(", ", clientCapability.supportedCodecs)}], prefers: {clientCapability.preferredCodec}, HEVC: {clientCapability.supportsHevc}");
            }
            catch (Exception capEx)
            {
                Debug.LogError($"[PhaseProtocol] GetClientCodecCapability failed: {capEx.Message}");
                // Fallback to H264 only
                clientCapability = new ClientCodecCapability
                {
                    supportedCodecs = new[] { "H264" },
                    preferredCodec = "H264",
                    supportsHevc = false,
                    deviceModel = SystemInfo.deviceModel,
                    apiLevel = 0
                };
            }

            // Send acknowledgment with client codec capabilities
            // NOTE: Build JSON manually because SimpleJson.Serialize doesn't work with anonymous objects on Android IL2CPP
            Debug.Log("[PhaseProtocol] Sending hardware_info_ack with codec capabilities");
            try
            {
                var codecsArray = string.Join(",", clientCapability.supportedCodecs.Select(c => $"\"{c}\""));
                var supportsHevcStr = clientCapability.supportsHevc.ToString().ToLower();
                var hardwareAckJson = $"{{\"type\":\"hardware_info_ack\",\"clientCodecs\":{{" +
                    $"\"supportedCodecs\":[{codecsArray}]," +
                    $"\"preferredCodec\":\"{clientCapability.preferredCodec}\"," +
                    $"\"supportsHevc\":{supportsHevcStr}," +
                    $"\"deviceModel\":\"{EscapeJsonString(clientCapability.deviceModel)}\"," +
                    $"\"apiLevel\":{clientCapability.apiLevel}" +
                    $"}}}}";
                Debug.Log($"[PhaseProtocol] hardware_info_ack JSON: {hardwareAckJson}");
                await SendTextAsync(hardwareAckJson);
                Debug.Log("[PhaseProtocol] hardware_info_ack sent successfully");
            }
            catch (Exception sendEx)
            {
                Debug.LogError($"[PhaseProtocol] Failed to send hardware_info_ack: {sendEx.Message}");
                throw;
            }

            // NEW FLOW: Client initiates speed test
            _stateMachine.TryTransition(ConnectionPhase.SpeedTesting);

            // Subscribe to speed test progress for UI updates
            _speedTest.OnSpeedProgress += (direction, mbps, progress) =>
            {
                OnSpeedTestProgress?.Invoke(direction, mbps, progress);
            };

            // IMPORTANT: Run speed test as fire-and-forget to avoid blocking receive loop
            // Binary data from server needs to be received while speed test is running
            Debug.Log("[PhaseProtocol] Starting client-initiated speed test (non-blocking)...");
            _ = RunSpeedTestInBackgroundAsync();
        }

        /// <summary>
        /// Run speed test in background so receive loop can continue processing binary data.
        /// </summary>
        private async Task RunSpeedTestInBackgroundAsync()
        {
            try
            {
                var speedResult = await _speedTest.RunSpeedTestAsync();

                // Detect network adapter type
                string networkType = DetectNetworkAdapterType();

                // Create network info from speed test results
                _networkInfo = new NetworkTestResult
                {
                    pingMs = speedResult.PingMs,
                    jitterMs = speedResult.JitterMs,
                    bandwidthMbps = speedResult.BandwidthMbps,
                    connectionType = networkType
                };

                // Fire event for UI to update network info
                OnNetworkInfoReceived?.Invoke(_networkInfo);
                Debug.Log($"[PhaseProtocol] Speed test complete: {_networkInfo.bandwidthMbps:F1}Mbps, {_networkInfo.pingMs:F1}ms ping, type={networkType}");

                // Wait for suggested_config from Server (Server calculates based on our speed test result)
                _stateMachine.TryTransition(ConnectionPhase.AwaitingSuggestedConfig);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Speed test failed: {ex.Message}");
                OnError?.Invoke($"Speed test failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Detect the type of network adapter (Ethernet or Wi-Fi).
        /// </summary>
        private string DetectNetworkAdapterType()
        {
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    // Skip non-operational interfaces
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;

                    // Skip loopback and tunnel interfaces
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                        continue;

                    // Check if this interface has an IP address
                    var props = ni.GetIPProperties();
                    if (props.UnicastAddresses.Count == 0)
                        continue;

                    // Determine type
                    switch (ni.NetworkInterfaceType)
                    {
                        case System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211:
                            return "Wi-Fi";
                        case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet:
                        case System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet:
                            return "Ethernet";
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT:
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx:
                            return "Ethernet";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Failed to detect network type: {ex.Message}");
            }

            return "Unknown";
        }

        private MonitorInfo[] ParseMonitors(List<SimpleJson> arr)
        {
            if (arr == null) return new MonitorInfo[0];

            return arr.Select(m => new MonitorInfo
            {
                id = m.GetInt("id"),
                name = m.GetString("name") ?? "",
                width = m.GetInt("w"),
                height = m.GetInt("h"),
                isVirtual = m.GetBool("isVirtual")
            }).ToArray();
        }

        private async Task HandleSpeedTestStartAsync(SimpleJson json)
        {
            var direction = json.GetString("direction") ?? "download";
            var chunkSize = json.GetInt("chunkSize");
            var durationMs = json.GetInt("durationMs");

            Debug.Log($"[PhaseProtocol] Speed test start: direction={direction}, chunk={chunkSize}, duration={durationMs}ms");

            if (_speedTest != null)
            {
                // Use async version to handle upload test
                await _speedTest.HandleSpeedTestStartAsync(direction, chunkSize, durationMs);
            }
        }

        private async Task HandleSpeedTestEndAsync(SimpleJson json)
        {
            var direction = json.GetString("direction") ?? "download";
            var totalBytes = json.GetLong("totalBytes");
            var durationMs = json.GetLong("durationMs");

            Debug.Log($"[PhaseProtocol] Speed test end: direction={direction}, current phase: {_stateMachine.CurrentPhase}");

            // Signal the client-initiated speed test that data transfer is complete
            // This is used by the improved bandwidth measurement
            _speedTest?.HandleSpeedTestEnd();

            // Legacy handler for server-initiated speed test
            await _speedTest?.HandleSpeedTestEndAsync(direction, totalBytes, durationMs);

            if (direction == "upload")
            {
                bool transitioned = _stateMachine.TryTransition(ConnectionPhase.AwaitingNetworkInfo);
                Debug.Log($"[PhaseProtocol] Transition to AwaitingNetworkInfo: {transitioned}, new phase: {_stateMachine.CurrentPhase}");
            }
        }

        private void HandleNetworkInfo(SimpleJson json)
        {
            Debug.Log($"[PhaseProtocol] Received network_info, current phase: {_stateMachine.CurrentPhase}");

            _networkInfo = new NetworkTestResult
            {
                pingMs = json.GetDouble("pingMs"),
                jitterMs = json.GetDouble("jitterMs"),
                bandwidthMbps = json.GetDouble("bandwidthMbps"),
                connectionType = json.GetString("connectionType") ?? "Unknown"
            };

            Debug.Log($"[PhaseProtocol] Network: {_networkInfo.connectionType}, Ping: {_networkInfo.pingMs:F1}ms, BW: {_networkInfo.bandwidthMbps:F0}Mbps");

            bool transitioned = _stateMachine.TryTransition(ConnectionPhase.AwaitingSuggestedConfig);
            Debug.Log($"[PhaseProtocol] Transition to AwaitingSuggestedConfig: {transitioned}, new phase: {_stateMachine.CurrentPhase}");

            OnNetworkInfoReceived?.Invoke(_networkInfo);
        }

        private void HandleSuggestedConfig(SimpleJson json)
        {
            Debug.Log($"[PhaseProtocol] Received suggested_config, current phase: {_stateMachine.CurrentPhase}");

            var resolution = json.GetObject("resolution");

            _suggestedConfig = new SuggestedStreamConfig
            {
                monitors = json.GetInt("monitors"),
                resolutionWidth = resolution?.GetInt("w") ?? 1920,
                resolutionHeight = resolution?.GetInt("h") ?? 1080,
                bitrateKbps = json.GetInt("bitrateKbps"),
                fps = json.GetInt("fps"),
                refreshRate = json.GetInt("refreshRate"),
                reason = json.GetString("reason") ?? "",
                selectedCodec = json.GetString("selectedCodec") ?? "H264"
            };

            // Update selected codec based on server's decision
            _selectedCodec = _suggestedConfig.selectedCodec.Equals("H265", StringComparison.OrdinalIgnoreCase)
                ? VideoCodec.H265
                : VideoCodec.H264;

            Debug.Log($"[PhaseProtocol] Suggested: {_suggestedConfig.monitors}mon @ {_suggestedConfig.resolutionWidth}x{_suggestedConfig.resolutionHeight}, {_suggestedConfig.fps}fps, {_suggestedConfig.bitrateKbps}kbps");
            Debug.Log($"[PhaseProtocol] Selected codec: {_suggestedConfig.selectedCodec}");
            Debug.Log($"[PhaseProtocol] Reason: {_suggestedConfig.reason}");

            // Handle race condition: suggested_config may arrive while still in SpeedTesting phase
            // (server sends immediately after receiving speedtest_result)
            var currentPhase = _stateMachine.CurrentPhase;
            if (currentPhase == ConnectionPhase.SpeedTesting ||
                currentPhase == ConnectionPhase.AwaitingNetworkInfo)
            {
                // Skip intermediate states - server already has our speed test result
                Debug.Log($"[PhaseProtocol] Forcing transition from {currentPhase} to ConfiguringSettings (race condition fix)");
                _stateMachine.ForceTransition(ConnectionPhase.ConfiguringSettings, "suggested_config received");
            }
            else
            {
                bool transitioned = _stateMachine.TryTransition(ConnectionPhase.ConfiguringSettings);
                Debug.Log($"[PhaseProtocol] Transition to ConfiguringSettings: {transitioned}, new phase: {_stateMachine.CurrentPhase}");
            }

            OnSuggestedConfigReceived?.Invoke(_suggestedConfig);
        }

        // === Phase 2 Handlers ===

        private void HandleConfigProgress(SimpleJson json)
        {
            var step = json.GetString("step") ?? "";
            var progress = json.GetInt("progress");
            var message = json.GetString("message") ?? "";

            Debug.Log($"[PhaseProtocol] Config progress: {step} {progress}% - {message}");
            OnConfigProgress?.Invoke(step, progress, message);
        }

        private Task HandleConfigCompleteAsync(SimpleJson json)
        {
            Debug.Log("[PhaseProtocol] Config complete, starting ICE negotiation");

            var monitorsArr = json.GetArray("monitors");
            _configuredMonitors = monitorsArr?.Select(m => new MonitorInfo
            {
                id = m.GetInt("id"),
                name = m.GetString("name") ?? "",
                width = m.GetInt("w"),
                height = m.GetInt("h"),
                isVirtual = m.GetBool("isVirtual")
            }).ToList() ?? new List<MonitorInfo>();

            OnConfigComplete?.Invoke(_configuredMonitors);

            _stateMachine.TryTransition(ConnectionPhase.ICENegotiating);

            // IMPORTANT: Run PeerConnection creation in background (fire-and-forget)
            // This allows the receive loop to continue processing incoming messages (answers, ICE candidates)
            // while we're still creating and setting up PeerConnections
            Debug.Log("[PhaseProtocol] Starting CreatePeerConnectionsAsync in background...");
            _ = CreatePeerConnectionsInBackgroundAsync(_configuredMonitors.Count);

            // Return immediately to unblock receive loop
            return Task.CompletedTask;
        }

        /// <summary>
        /// Create PeerConnections in background so receive loop can process incoming messages.
        /// </summary>
        private async Task CreatePeerConnectionsInBackgroundAsync(int count)
        {
            try
            {
                await CreatePeerConnectionsAsync(count);
                Debug.Log("[PhaseProtocol] CreatePeerConnectionsAsync completed successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] CreatePeerConnectionsAsync FAILED: {ex.Message}");
                Debug.LogError($"[PhaseProtocol] Stack trace: {ex.StackTrace}");
            }
        }

        private async Task CreatePeerConnectionsAsync(int count)
        {
            Debug.Log($"[PhaseProtocol] Creating {count} PeerConnections");

            for (int i = 0; i < count; i++)
            {
                var cfg = new RTCConfiguration { iceServers = Array.Empty<RTCIceServer>() };
                var pc = new RTCPeerConnection(ref cfg);
                var wrapper = new PCWrapper { Index = i, PC = pc };

                int idx = i; // Capture for closures

                // Add video transceiver with codec preference based on negotiated codec
                var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
                var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);

                // Set codec preferences based on selected codec
                RTCRtpCodecCapability[] preferredCodecs;
                if (_selectedCodec == VideoCodec.H265)
                {
                    // H.265 preferred: HEVC first, then H.264
                    var h265 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H265", StringComparison.OrdinalIgnoreCase) ||
                                                       (c.mimeType ?? "").Contains("HEVC", StringComparison.OrdinalIgnoreCase)).ToArray();
                    var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
                    preferredCodecs = h265.Concat(h264).Concat(caps.codecs.Except(h265).Except(h264)).ToArray();
                    Debug.Log($"[PhaseProtocol] PC{idx} using HEVC codec preference ({h265.Length} HEVC codecs found)");
                }
                else
                {
                    // H.264 preferred
                    var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
                    preferredCodecs = h264.Concat(caps.codecs.Except(h264)).ToArray();
                    Debug.Log($"[PhaseProtocol] PC{idx} using H.264 codec preference");
                }
                trans.SetCodecPreferences(preferredCodecs);

                pc.OnIceConnectionChange = s =>
                {
                    Debug.Log($"[PhaseProtocol] PC{idx} ICE: {s}");
                    if (s == RTCIceConnectionState.Connected)
                    {
                        Debug.Log($"[PhaseProtocol] PC{idx} ICE connected");
                    }
                };
                pc.OnConnectionStateChange = s =>
                {
                    Debug.Log($"[PhaseProtocol] PC{idx} State: {s}");
                    if (s == RTCPeerConnectionState.Connected)
                    {
                        Debug.Log($"[PhaseProtocol] PC{idx} PC connected");
                    }
                    else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                    {
                        Debug.LogWarning($"[PhaseProtocol] PC{idx} connection lost");
                    }
                };

                // ICE candidates
                pc.OnIceCandidate = cand =>
                {
                    if (string.IsNullOrEmpty(cand.Candidate))
                    {
                        Debug.Log($"[PhaseProtocol] PC{idx} ICE gathering complete");
                        _ = SendTextAsync($"{{\"type\":\"end_of_candidates\",\"monitorIndex\":{idx}}}");
                        return;
                    }

                    string msg = cand.Candidate;
                    if (_skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                    {
                        Debug.Log($"[PhaseProtocol] PC{idx} Skipped TCP candidate");
                        return;
                    }

                    // Normalize format
                    string rawCandidate = msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase)
                        ? msg.Substring("candidate:".Length)
                        : msg;

                    Debug.Log($"[PhaseProtocol] PC{idx} Sending ICE: {rawCandidate.Substring(0, Math.Min(60, rawCandidate.Length))}...");
                    _ = SendTextAsync($"{{\"type\":\"candidate\",\"monitorIndex\":{idx},\"candidate\":\"{EscapeJsonString(rawCandidate)}\"}}");
                };

                // Track received
                pc.OnTrack = e =>
                {
                    if (e.Track is VideoStreamTrack v)
                    {
                        wrapper.VideoTrack = v;
                        v.OnVideoReceived += tex =>
                        {
                            wrapper.Texture = tex;
                            OnVideoTextureReceived?.Invoke(idx, tex);
                        };
                        Debug.Log($"[PhaseProtocol] PC{idx} received video track");
                    }
                };

                lock (_lock) { _peerConnections.Add(wrapper); }

                // Create and send offer - use Task.Delay(10) like v1 for consistent async behavior
                var offerOp = pc.CreateOffer();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!offerOp.IsDone && sw.ElapsedMilliseconds < 5000)
                    await Task.Delay(10);

                if (!offerOp.IsDone || offerOp.IsError)
                {
                    Debug.LogError($"[PhaseProtocol] PC{idx} CreateOffer failed");
                    continue;
                }

                var offer = offerOp.Desc;
                var setLocalOp = pc.SetLocalDescription(ref offer);
                sw.Restart();
                while (!setLocalOp.IsDone && sw.ElapsedMilliseconds < 5000)
                    await Task.Delay(10);

                if (!setLocalOp.IsDone || setLocalOp.IsError)
                {
                    Debug.LogError($"[PhaseProtocol] PC{idx} SetLocal failed");
                    continue;
                }

                await SendTextAsync($"{{\"type\":\"offer\",\"monitorIndex\":{idx},\"sdp\":\"{EscapeJsonString(offer.sdp)}\"}}");
                Debug.Log($"[PhaseProtocol] PC{idx} offer sent");
            }
        }

        private async Task HandleAnswerAsync(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            var rawSdp = json.GetString("sdp") ?? "";
            var sdp = FixSdp(rawSdp);

            Debug.Log($"[PhaseProtocol] PC{monitorIndex} received answer, _peerConnections.Count={_peerConnections.Count}");
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} raw SDP length={rawSdp.Length}, fixed SDP length={sdp.Length}");

            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count)
                {
                    Debug.LogWarning($"[PhaseProtocol] PC{monitorIndex} answer ignored: index out of range (count={_peerConnections.Count})");
                    return;
                }
                wrapper = _peerConnections[monitorIndex];
            }

            // Validate wrapper and PC
            if (wrapper == null)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} wrapper is NULL!");
                return;
            }
            if (wrapper.PC == null)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} wrapper.PC is NULL!");
                return;
            }

            // Use EXACT same pattern as working v1 code (MultiPCStreamClient line 534-550)
            try
            {
                // Log PC state before attempting SetRemoteDescription
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SignalingState={wrapper.PC.SignalingState}, ConnectionState={wrapper.PC.ConnectionState}, IsClosed={wrapper.PC.ConnectionState == RTCPeerConnectionState.Closed}");

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} creating RTCSessionDescription...");
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} RTCSessionDescription created, sdp length={sdp.Length}");

                // Check that PC is in correct state for SetRemoteDescription
                if (wrapper.PC.SignalingState != RTCSignalingState.HaveLocalOffer)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} WRONG STATE: SignalingState={wrapper.PC.SignalingState}, expected HaveLocalOffer");
                    return;
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} calling wrapper.PC.SetRemoteDescription...");
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} FULL SDP ({sdp.Length} chars):\n{sdp}");
                // Log individual lines for debugging
                var sdpLines = sdp.Split('\n');
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SDP has {sdpLines.Length} lines");
                RTCSetSessionDescriptionAsyncOperation setRemoteOp = null;
                try
                {
                    setRemoteOp = wrapper.PC.SetRemoteDescription(ref answer);
                    Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription call returned (setRemoteOp null? {setRemoteOp == null})");
                }
                catch (Exception innerEx)
                {
                    Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription EXCEPTION: {innerEx.GetType().Name}: {innerEx.Message}");
                    Debug.Log($"[PhaseProtocol] PC{monitorIndex} Stack: {innerEx.StackTrace}");
                    return;
                }

                // Debug: Log that we passed try-catch
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} Passed try-catch, setRemoteOp null? {setRemoteOp == null}");

                if (setRemoteOp == null)
                {
                    Debug.Log($"[PhaseProtocol] PC{monitorIndex} ERROR: SetRemoteDescription returned NULL operation!");
                    return;
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription returned op, IsDone={setRemoteOp.IsDone}, waiting...");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                int loopCount = 0;
                while (!setRemoteOp.IsDone && sw.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(10);
                    loopCount++;
                    if (loopCount % 100 == 0) // Log every 1 second
                    {
                        Debug.Log($"[PhaseProtocol] PC{monitorIndex} waiting... {sw.ElapsedMilliseconds}ms, IsDone={setRemoteOp.IsDone}");
                    }
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} loop exited: loops={loopCount}, elapsed={sw.ElapsedMilliseconds}ms, IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError}");

                if (!setRemoteOp.IsDone || setRemoteOp.IsError)
                {
                    Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription FAILED (IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError})");
                    if (setRemoteOp.IsError)
                    {
                        try
                        {
                            Debug.Log($"[PhaseProtocol] PC{monitorIndex} Error detail: {setRemoteOp.Error.message}");
                        }
                        catch (Exception errEx)
                        {
                            Debug.Log($"[PhaseProtocol] PC{monitorIndex} Could not get error detail: {errEx.Message}");
                        }
                    }
                    return;
                }

                wrapper.AnswerSet = true;
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} ✓ answer set OK, AnswerSet=true");

                // Process pending ICE
                foreach (var cand in wrapper.PendingIce)
                    AddIceCandidate(wrapper, cand);
                wrapper.PendingIce.Clear();

                // Check if all PCs have answers
                CheckIceComplete();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} HandleAnswerAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private void HandleCandidate(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            var candStr = json.GetString("candidate") ?? "";

            if (_skipTcpIceCandidates && (candStr.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || candStr.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} Skipped remote TCP candidate");
                return;
            }

            Debug.Log($"[PhaseProtocol] PC{monitorIndex} received ICE candidate");

            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count) return;
                wrapper = _peerConnections[monitorIndex];
            }

            if (wrapper.AnswerSet)
                AddIceCandidate(wrapper, candStr);
            else
                wrapper.PendingIce.Add(candStr);
        }

        private void HandleEndOfCandidates(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} server ICE complete");
            CheckIceComplete();
        }

        /// <summary>
        /// Handle ice_ready message from server - all ICE connections are established.
        /// This provides a reliable server-side confirmation for transitioning to ReadyToStream.
        /// </summary>
        private void HandleIceReady(SimpleJson json)
        {
            var monitorCount = json.GetInt("monitorCount");
            Debug.Log($"[PhaseProtocol] Server confirmed {monitorCount} ICE connections ready");

            if (_stateMachine.CurrentPhase == ConnectionPhase.ICENegotiating)
            {
                Debug.Log("[PhaseProtocol] Transitioning to ReadyToStream (server-initiated via ice_ready)");
                _stateMachine.TryTransition(ConnectionPhase.ReadyToStream);
                _ = SendTextAsync("{\"type\":\"proceed\",\"phase\":3}");
                OnReadyToStream?.Invoke();
            }
            else
            {
                Debug.Log($"[PhaseProtocol] ice_ready received but phase is {_stateMachine.CurrentPhase}, ignoring");
            }
        }

        private void AddIceCandidate(PCWrapper wrapper, string candStr)
        {
            try
            {
                var fullCand = candStr.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) ? candStr : "candidate:" + candStr;
                wrapper.PC.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = fullCand, sdpMLineIndex = 0, sdpMid = "0" }));
                Debug.Log($"[PhaseProtocol] PC{wrapper.Index} Added ICE candidate");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] PC{wrapper.Index} AddICE error: {ex.Message}");
            }
        }

        private void CheckIceComplete()
        {
            bool allReady = false;
            bool shouldSendProceed = false;

            lock (_lock)
            {
                int total = _peerConnections.Count;
                int answered = _peerConnections.Count(p => p.AnswerSet);
                Debug.Log($"[PhaseProtocol] CheckIceComplete: {answered}/{total} PCs have answers, phase={_stateMachine.CurrentPhase}");

                if (_peerConnections.All(p => p.AnswerSet))
                {
                    allReady = true;
                    if (_stateMachine.CurrentPhase == ConnectionPhase.ICENegotiating)
                    {
                        Debug.Log("[PhaseProtocol] All PeerConnections ready, transitioning to ReadyToStream");
                        _stateMachine.TryTransition(ConnectionPhase.ReadyToStream);
                        shouldSendProceed = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[PhaseProtocol] All PCs ready but phase is {_stateMachine.CurrentPhase}, not ICENegotiating");
                    }
                }
            }

            // Send proceed message outside of lock
            if (shouldSendProceed)
            {
                Debug.Log("[PhaseProtocol] Sending proceed message for phase 3");
                _ = SendTextAsync("{\"type\":\"proceed\",\"phase\":3}");
                OnReadyToStream?.Invoke();
            }
        }

        // === Reconnect Handler ===

        /// <summary>
        /// Reconnect a specific monitor by recreating PeerConnection and sending new offer.
        /// Called when server requests reconnect due to connection loss.
        /// </summary>
        private async Task ReconnectMonitorAsync(int monitorIndex)
        {
            // Don't reconnect if application is shutting down
            if (_cts == null || _cts.IsCancellationRequested) return;

            PCWrapper oldWrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count)
                {
                    Debug.LogWarning($"[PhaseProtocol] Reconnect ignored: invalid monitor index {monitorIndex}");
                    return;
                }
                oldWrapper = _peerConnections[monitorIndex];
            }

            Debug.Log($"[PhaseProtocol] Reconnecting PC{monitorIndex}...");

            // Close old PC
            try { oldWrapper.PC?.Close(); oldWrapper.PC?.Dispose(); } catch { }

            // Create new PeerConnection
            var cfg = new RTCConfiguration { iceServers = Array.Empty<RTCIceServer>() };
            var pc = new RTCPeerConnection(ref cfg);
            var wrapper = new PCWrapper { Index = monitorIndex, PC = pc };

            int idx = monitorIndex;

            // Add video transceiver with codec preference based on negotiated codec
            var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
            var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);

            // Set codec preferences based on selected codec
            RTCRtpCodecCapability[] preferredCodecs;
            if (_selectedCodec == VideoCodec.H265)
            {
                var h265 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H265", StringComparison.OrdinalIgnoreCase) ||
                                                   (c.mimeType ?? "").Contains("HEVC", StringComparison.OrdinalIgnoreCase)).ToArray();
                var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
                preferredCodecs = h265.Concat(h264).Concat(caps.codecs.Except(h265).Except(h264)).ToArray();
            }
            else
            {
                var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
                preferredCodecs = h264.Concat(caps.codecs.Except(h264)).ToArray();
            }
            trans.SetCodecPreferences(preferredCodecs);

            // Connection state handlers
            pc.OnIceConnectionChange = s =>
            {
                Debug.Log($"[PhaseProtocol] PC{idx} ICE (reconnected): {s}");
            };
            pc.OnConnectionStateChange = s =>
            {
                Debug.Log($"[PhaseProtocol] PC{idx} State (reconnected): {s}");
                if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    Debug.LogWarning($"[PhaseProtocol] PC{idx} connection lost again after reconnect");
                }
            };

            // ICE candidates
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate)) return;

                string msg = cand.Candidate;
                if (_skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                    return;

                string rawCandidate = msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase)
                    ? msg.Substring("candidate:".Length)
                    : msg;

                _ = SendTextAsync($"{{\"type\":\"candidate\",\"monitorIndex\":{idx},\"candidate\":\"{EscapeJsonString(rawCandidate)}\"}}");
            };

            // Track received
            pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                {
                    wrapper.VideoTrack = v;
                    v.OnVideoReceived += tex =>
                    {
                        wrapper.Texture = tex;
                        OnVideoTextureReceived?.Invoke(idx, tex);
                    };
                    Debug.Log($"[PhaseProtocol] PC{idx} received video track (reconnected)");
                }
            };

            // Replace wrapper
            lock (_lock)
            {
                _peerConnections[monitorIndex] = wrapper;
            }

            // Create and send new offer
            var offerOp = pc.CreateOffer();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!offerOp.IsDone && sw.ElapsedMilliseconds < 5000)
                await Task.Delay(10);

            if (!offerOp.IsDone || offerOp.IsError)
            {
                Debug.LogError($"[PhaseProtocol] PC{idx} CreateOffer failed on reconnect");
                return;
            }

            var offer = offerOp.Desc;
            var setLocalOp = pc.SetLocalDescription(ref offer);
            sw.Restart();
            while (!setLocalOp.IsDone && sw.ElapsedMilliseconds < 5000)
                await Task.Delay(10);

            if (!setLocalOp.IsDone || setLocalOp.IsError)
            {
                Debug.LogError($"[PhaseProtocol] PC{idx} SetLocal failed on reconnect");
                return;
            }

            await SendTextAsync($"{{\"type\":\"offer\",\"monitorIndex\":{idx},\"sdp\":\"{EscapeJsonString(offer.sdp)}\"}}");
            Debug.Log($"[PhaseProtocol] PC{idx} reconnect offer sent");
        }

        // === Phase 3 Handlers ===

        private void HandleStreamingStarted(SimpleJson json)
        {
            Debug.Log("[PhaseProtocol] Streaming started!");
            _stateMachine.TryTransition(ConnectionPhase.Streaming);
            OnStreamingStarted?.Invoke();
        }

        /// <summary>
        /// Handle cursor position update from server.
        /// </summary>
        private void HandleCursorPosition(SimpleJson json)
        {
            int monitorIndex = json.GetInt("monitorIndex");
            float u = json.GetFloat("u");
            float v = json.GetFloat("v");
            bool visible = json.GetBool("visible");

            // Invoke event for ClusterAutoBinder to handle
            OnCursorPosition?.Invoke(monitorIndex, u, v, visible);
        }

        // === Error Handler ===

        private void HandleError(SimpleJson json)
        {
            var phase = json.GetInt("phase");
            var code = json.GetString("code") ?? "UNKNOWN";
            var message = json.GetString("message") ?? "Unknown error";

            Debug.LogError($"[PhaseProtocol] Error in Phase {phase}: [{code}] {message}");
            _stateMachine.ForceTransition(ConnectionPhase.Error, message);
            OnError?.Invoke(message);
        }

        // === Legacy Message Handler ===

        private async Task HandleLegacyMessageAsync(string text)
        {
            // Handle reconnect request from server
            if (text.StartsWith("reconnect:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = text.Substring(10);
                if (int.TryParse(rest.Trim(), out int monIdx))
                {
                    Debug.Log($"[PhaseProtocol] Server requested reconnect for monitor {monIdx}");
                    await ReconnectMonitorAsync(monIdx);
                }
                return;
            }

            // Handle legacy format: answer:N:sdp, candidate:N:candidate
            if (text.StartsWith("answer:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = text.Substring(7);
                var colonIdx = rest.IndexOf(':');
                if (colonIdx > 0 && int.TryParse(rest.Substring(0, colonIdx), out int monIdx))
                {
                    var sdp = rest.Substring(colonIdx + 1);
                    await HandleAnswerAsync(new SimpleJson(new Dictionary<string, object>
                    {
                        { "monitorIndex", monIdx },
                        { "sdp", sdp }
                    }));
                }
            }
            else if (text.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = text.Substring(10);
                var colonIdx = rest.IndexOf(':');
                if (colonIdx > 0 && int.TryParse(rest.Substring(0, colonIdx), out int monIdx))
                {
                    var candStr = rest.Substring(colonIdx + 1);
                    HandleCandidate(new SimpleJson(new Dictionary<string, object>
                    {
                        { "monitorIndex", monIdx },
                        { "candidate", candStr }
                    }));
                }
            }
        }

        // === Utilities ===

        private async Task KeepaliveLoopAsync(CancellationToken ct)
        {
            await Task.Delay(2000, ct);
            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    await SendTextAsync("ping");
                }
                catch { }
                await Task.Delay(5000, ct);
            }
        }

        private async Task SendTextAsync(string text)
        {
            if (_ws?.State != WebSocketState.Open) return;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Send error: {ex.Message}");
            }
        }

        /// <summary>
        /// Escape special characters in a string for JSON.
        /// </summary>
        private string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private string FixSdp(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return sdp;

            // CRITICAL: Unescape literal \r\n strings to actual CRLF characters
            // JSON may contain escaped sequences that weren't properly unescaped
            if (sdp.Contains("\\r\\n"))
            {
                Debug.Log("[PhaseProtocol] FixSdp: Unescaping literal \\r\\n to actual CRLF");
                sdp = sdp.Replace("\\r\\n", "\r\n");
            }
            else if (sdp.Contains("\\n"))
            {
                Debug.Log("[PhaseProtocol] FixSdp: Unescaping literal \\n to actual LF");
                sdp = sdp.Replace("\\n", "\n");
            }

            // Only fix SAVP -> SAVPF if not already SAVPF (avoid SAVPFF bug)
            if (!sdp.Contains("UDP/TLS/RTP/SAVPF"))
            {
                Debug.Log("[PhaseProtocol] FixSdp: Converting SAVP -> SAVPF");
                sdp = sdp.Replace("UDP/TLS/RTP/SAVP", "UDP/TLS/RTP/SAVPF");
            }

            // Verify no double-F bug
            if (sdp.Contains("SAVPFF"))
            {
                Debug.LogError("[PhaseProtocol] FixSdp BUG: SDP contains SAVPFF!");
            }

            if (sdp.Contains("IP4 0.0.0.0"))
            {
                Debug.Log("[PhaseProtocol] Fixing SDP: IP4 0.0.0.0 -> IP4 127.0.0.1");
                sdp = sdp.Replace("IP4 0.0.0.0", "IP4 127.0.0.1");
            }

            // CRITICAL: Answer SDP must have setup:active or setup:passive, NOT actpass!
            // SIPSorcery sends actpass but Unity WebRTC requires active/passive for answers
            if (sdp.Contains("a=setup:actpass"))
            {
                Debug.Log("[PhaseProtocol] FixSdp: Converting a=setup:actpass -> a=setup:active (required for answer)");
                sdp = sdp.Replace("a=setup:actpass", "a=setup:active");
            }

            var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var filtered = new List<string>();
            int removedCandidates = 0;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // CRITICAL FIX: Remove embedded ICE candidates from answer SDP!
                // V1 does this and it works. Keeping them causes SetRemoteDescription to hang.
                // ICE candidates are sent separately via "candidate" messages (trickle ICE).
                if (line.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase))
                {
                    removedCandidates++;
                    continue;
                }

                if (line.StartsWith("a=ice-options:", StringComparison.OrdinalIgnoreCase) && line.Contains("ice2"))
                {
                    line = line.Replace("ice2,", "").Replace(",ice2", "").Replace("ice2", "trickle");
                }
                filtered.Add(line);
            }

            if (removedCandidates > 0)
            {
                Debug.Log($"[PhaseProtocol] FixSdp: Removed {removedCandidates} embedded ICE candidates (using trickle ICE)");
            }

            sdp = string.Join("\r\n", filtered);
            if (!sdp.EndsWith("\r\n")) sdp += "\r\n";
            return sdp;
        }

        /// <summary>
        /// Update textures (call from Update loop).
        /// </summary>
        public void PollTextures()
        {
            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    try
                    {
                        var tex = wrapper.VideoTrack?.Texture;
                        if (tex != null && tex.width > 0) wrapper.Texture = tex;
                    }
                    catch { }
                }
            }
        }

        private void Cleanup()
        {
            try { _cts?.Cancel(); } catch { }

            lock (_lock)
            {
                foreach (var w in _peerConnections)
                {
                    try { w.PC?.Close(); w.PC?.Dispose(); } catch { }
                }
                _peerConnections.Clear();
            }

            try { _ws?.Abort(); _ws?.Dispose(); } catch { }
            _ws = null;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }

    /// <summary>
    /// Simple JSON parser for Unity (no external dependencies).
    /// Handles nested objects and arrays manually since Unity's JsonUtility doesn't support Dictionary.
    /// </summary>
    public class SimpleJson
    {
        private readonly Dictionary<string, object> _data;

        public SimpleJson() { _data = new Dictionary<string, object>(); }
        public SimpleJson(Dictionary<string, object> data) { _data = data ?? new Dictionary<string, object>(); }

        public static SimpleJson Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new SimpleJson();
            return new SimpleJson(ParseJsonObject(json.Trim()));
        }

        public string GetString(string key) => _data.TryGetValue(key, out var v) ? v?.ToString() : null;
        public int GetInt(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;
        public long GetLong(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : 0;
        public double GetDouble(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v) : 0;
        public float GetFloat(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToSingle(v) : 0f;
        public bool GetBool(string key) => _data.TryGetValue(key, out var v) && v != null && Convert.ToBoolean(v);
        public SimpleJson GetObject(string key) => _data.TryGetValue(key, out var v) && v is Dictionary<string, object> d ? new SimpleJson(d) : null;
        public List<SimpleJson> GetArray(string key)
        {
            if (_data.TryGetValue(key, out var v) && v is List<object> list)
                return list.Select(o => new SimpleJson(o as Dictionary<string, object>)).ToList();
            return null;
        }

        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            var type = obj.GetType();

            if (type == typeof(string))
                return $"\"{EscapeString((string)obj)}\"";

            if (type == typeof(bool))
                return (bool)obj ? "true" : "false";

            if (type.IsPrimitive || type == typeof(decimal))
                return obj.ToString();

            // Handle arrays
            if (type.IsArray)
            {
                var array = (Array)obj;
                var sb = new StringBuilder();
                sb.Append("[");
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(Serialize(array.GetValue(i)));
                }
                sb.Append("]");
                return sb.ToString();
            }

            // Handle IEnumerable (lists, etc.) but not string or dictionary
            if (obj is System.Collections.IEnumerable enumerable && !(obj is string) && !(obj is System.Collections.IDictionary))
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(Serialize(item));
                }
                sb.Append("]");
                return sb.ToString();
            }

            // Handle anonymous types and objects
            var objSb = new StringBuilder();
            objSb.Append("{");
            bool firstProp = true;

            foreach (var prop in type.GetProperties())
            {
                // Skip indexers and internal properties
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.Name == "SyncRoot" || prop.Name == "IsReadOnly" || prop.Name == "IsFixedSize" || prop.Name == "IsSynchronized") continue;

                try
                {
                    var value = prop.GetValue(obj);
                    var name = char.ToLower(prop.Name[0]) + prop.Name.Substring(1); // camelCase

                    if (!firstProp) objSb.Append(",");
                    firstProp = false;

                    objSb.Append($"\"{name}\":");

                    if (value == null)
                        objSb.Append("null");
                    else if (value is string s)
                        objSb.Append($"\"{EscapeString(s)}\"");
                    else if (value is bool b)
                        objSb.Append(b ? "true" : "false");
                    else if (value.GetType().IsPrimitive || value.GetType() == typeof(decimal))
                        objSb.Append(value.ToString());
                    else
                        objSb.Append(Serialize(value));
                }
                catch
                {
                    // Skip properties that throw exceptions
                }
            }

            objSb.Append("}");
            return objSb.ToString();
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private static Dictionary<string, object> ParseJsonObject(string json)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(json) || !json.StartsWith("{")) return result;

            try
            {
                int depth = 0;
                bool inString = false;
                bool escaped = false;
                var currentKey = new StringBuilder();
                var currentValue = new StringBuilder();
                bool readingKey = true;
                bool keyReady = false;

                for (int i = 1; i < json.Length - 1; i++)
                {
                    char c = json[i];

                    if (escaped)
                    {
                        escaped = false;
                        // CRITICAL FIX: Properly unescape JSON escape sequences
                        char unescaped = c switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            '/' => '/',
                            _ => c
                        };
                        if (readingKey) currentKey.Append(unescaped);
                        else currentValue.Append(unescaped);
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        // Don't append backslash yet - wait for next char to determine escape sequence
                        continue;
                    }

                    if (c == '"' && depth == 0)
                    {
                        inString = !inString;
                        if (!inString && readingKey) keyReady = true;
                        continue;
                    }

                    if (!inString)
                    {
                        if (c == '{' || c == '[')
                        {
                            depth++;
                            if (!readingKey) currentValue.Append(c);
                            continue;
                        }
                        if (c == '}' || c == ']')
                        {
                            depth--;
                            if (!readingKey) currentValue.Append(c);
                            continue;
                        }
                        if (depth == 0 && c == ':' && keyReady)
                        {
                            readingKey = false;
                            continue;
                        }
                        if (depth == 0 && c == ',')
                        {
                            if (currentKey.Length > 0)
                            {
                                var key = currentKey.ToString().Trim();
                                var val = currentValue.ToString().Trim();
                                result[key] = ParseValue(val);
                            }
                            currentKey.Clear();
                            currentValue.Clear();
                            readingKey = true;
                            keyReady = false;
                            continue;
                        }
                        if (depth == 0 && char.IsWhiteSpace(c)) continue;
                    }

                    if (readingKey && inString)
                        currentKey.Append(c);
                    else if (!readingKey)
                        currentValue.Append(c);
                }

                if (currentKey.Length > 0)
                {
                    var key = currentKey.ToString().Trim();
                    var val = currentValue.ToString().Trim();
                    result[key] = ParseValue(val);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SimpleJson] Parse error: {ex.Message}");
            }

            return result;
        }

        private static object ParseValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Trim();
            if (value == "null") return null;
            if (value == "true") return true;
            if (value == "false") return false;
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                var s = value.Substring(1, value.Length - 2);
                return UnescapeString(s);
            }
            if (value.StartsWith("{")) return ParseJsonObject(value);
            if (value.StartsWith("[")) return ParseJsonArray(value);
            if (long.TryParse(value, out long l)) return l;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
            return value;
        }

        private static string UnescapeString(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static List<object> ParseJsonArray(string json)
        {
            var result = new List<object>();
            if (string.IsNullOrEmpty(json) || !json.StartsWith("[")) return result;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            var current = new StringBuilder();

            for (int i = 1; i < json.Length - 1; i++)
            {
                char c = json[i];

                if (escaped)
                {
                    escaped = false;
                    current.Append(c);
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    current.Append(c);
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    current.Append(c);
                    continue;
                }

                if (!inString)
                {
                    if (c == '{' || c == '[') depth++;
                    if (c == '}' || c == ']') depth--;
                    if (depth == 0 && c == ',')
                    {
                        var val = current.ToString().Trim();
                        if (!string.IsNullOrEmpty(val))
                            result.Add(ParseValue(val));
                        current.Clear();
                        continue;
                    }
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                var val = current.ToString().Trim();
                if (!string.IsNullOrEmpty(val))
                    result.Add(ParseValue(val));
            }

            return result;
        }
    }
}
