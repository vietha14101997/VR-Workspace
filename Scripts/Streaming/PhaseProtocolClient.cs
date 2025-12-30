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
        private int _expectedMonitorCount; // Track expected count for CheckIceComplete
        private TaskCompletionSource<bool> _allAnswersReceivedTcs; // Event-driven answer waiting

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
            public bool OfferSent; // Flag to track if offer has been sent (for ICE candidate ordering)
            public List<string> PendingIce = new List<string>(); // ICE candidates received before answer
            public List<string> QueuedCandidates = new List<string>(); // Local candidates queued before offer sent
            public bool IsReconnecting; // Prevent duplicate reconnect attempts
            public DateTime LastConnectedTime;
            public int ReconnectAttempts; // Track consecutive reconnect attempts
            public const int MaxReconnectAttempts = 5; // Max attempts before giving up
            public DateTime LastFrameTime; // Track when last video frame was received
            public int FrameCount; // Count frames for monitoring
            public TaskCompletionSource<bool> AnswerReceivedTcs; // For event-driven sequential mode
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

            // Release Android power locks
            try
            {
                AndroidStreamingHelper.Instance?.ReleaseLocks();
                AndroidStreamingHelper.Instance?.SetKeepScreenOn(false);
            }
            catch { }

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
                    // First receive to determine message type
                    var firstResult = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (firstResult.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[PhaseProtocol] WebSocket closed by server");
                        _stateMachine.ForceTransition(ConnectionPhase.Disconnected);
                        OnDisconnected?.Invoke();
                        return;
                    }

                    _msgCounter++;

                    // OPTIMIZATION: Binary messages (speed test) - just count bytes, no copy
                    if (firstResult.MessageType == WebSocketMessageType.Binary)
                    {
                        int totalBytes = firstResult.Count;

                        // Continue receiving if message not complete
                        while (!firstResult.EndOfMessage)
                        {
                            firstResult = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                            totalBytes += firstResult.Count;
                        }

                        // Pass only byte count - no allocation!
                        _speedTest?.RecordBytesReceived(totalBytes);
                        continue;
                    }

                    // Text messages - use MemoryStream (needed for JSON parsing)
                    var ms = new System.IO.MemoryStream();
                    ms.Write(buffer, 0, firstResult.Count);

                    while (!firstResult.EndOfMessage)
                    {
                        firstResult = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        ms.Write(buffer, 0, firstResult.Count);
                    }

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        await HandleTextMessageAsync(text);
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
                            // Fire-and-forget (like browser) - don't block message loop
                            _ = HandleAnswerAsync(json);
                            break;

                        case "candidate":
                            // Synchronous, minimal logging
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

                        case "ping":
                            // Server sent JSON ping - respond immediately with pong
                            _ = SendTextAsync("{\"type\":\"pong\"}");
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
                // Send pong immediately - don't await, fire-and-forget for lowest latency
                // This matches browser behavior which sends pong synchronously
                _ = SendTextAsync("pong");
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

        /// <summary>
        /// Create PeerConnections using PARALLEL pattern (like browser).
        /// Falls back to sequential if parallel fails (for Android compatibility).
        /// REWRITTEN to match browser performance.
        /// </summary>
        private async Task CreatePeerConnectionsAsync(int count)
        {
            Debug.Log($"[PhaseProtocol] Creating {count} PeerConnections (parallel mode)");

            _expectedMonitorCount = count;

#if UNITY_ANDROID && !UNITY_EDITOR
            Debug.Log("[PhaseProtocol] Android detected - using parallel with fallback");
#endif

            // Try parallel first (like browser) - all PCs created at once
            var parallelSuccess = await TryParallelPCCreationAsync(count);

            if (!parallelSuccess)
            {
                Debug.LogWarning("[PhaseProtocol] Parallel creation incomplete, falling back to sequential...");
                await CreatePeerConnectionsSequentialAsync(count);
            }
        }

        /// <summary>
        /// Create all PCs in parallel like browser does.
        /// Returns true if all PCs got answers within timeout.
        /// Uses event-driven TaskCompletionSource instead of polling for lower latency.
        /// </summary>
        private async Task<bool> TryParallelPCCreationAsync(int count)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Initialize event-driven answer waiting
            _allAnswersReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = new List<Task>();

            // Create all PCs simultaneously (like browser's tight loop)
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                tasks.Add(CreateSinglePCAsync(idx));
            }

            // Wait for all PC creation tasks to complete (offer sent)
            await Task.WhenAll(tasks);
            Debug.Log($"[PhaseProtocol] All {count} offers sent in parallel ({sw.ElapsedMilliseconds}ms)");

            // Event-driven wait for answers (no polling!) with timeout
            const int TOTAL_ANSWER_TIMEOUT_MS = 10000; // 10 seconds for ALL answers

            try
            {
                // Wait for TCS to be signaled OR timeout
                var timeoutTask = Task.Delay(TOTAL_ANSWER_TIMEOUT_MS);
                var completedTask = await Task.WhenAny(_allAnswersReceivedTcs.Task, timeoutTask);

                if (completedTask == _allAnswersReceivedTcs.Task)
                {
                    Debug.Log($"[PhaseProtocol] All {count} answers received in {sw.ElapsedMilliseconds}ms (event-driven success)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Answer waiting exception: {ex.Message}");
            }

            // Check final state on timeout
            int finalAnswers;
            lock (_lock)
            {
                finalAnswers = _peerConnections.Count(p => p.AnswerSet);
            }

            Debug.LogWarning($"[PhaseProtocol] Parallel timeout after {sw.ElapsedMilliseconds}ms: {finalAnswers}/{count} answers received");
            return finalAnswers >= count;
        }

        /// <summary>
        /// Create a single PC with offer - used by parallel creation.
        /// Fire-and-forget pattern - doesn't wait for answer.
        /// </summary>
        private async Task CreateSinglePCAsync(int idx)
        {
            // EMPTY ICE servers (like browser) - no STUN lookup delay!
            // Browser: new RTCPeerConnection({ iceServers: [], iceCandidatePoolSize: 0 })
            var cfg = new RTCConfiguration { iceServers = new RTCIceServer[0] };
            var pc = new RTCPeerConnection(ref cfg);
            var wrapper = new PCWrapper
            {
                Index = idx,
                PC = pc,
                AnswerReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            // Add video transceiver
            var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
            SetCodecPreferences(trans, idx);

            // Event handlers
            SetupPCEventHandlers(pc, wrapper, idx);

            lock (_lock) { _peerConnections.Add(wrapper); }

            // Create offer (don't use polling - use await pattern)
            var offerOp = pc.CreateOffer();
            while (!offerOp.IsDone)
                await Task.Yield();

            if (offerOp.IsError)
            {
                Debug.LogError($"[PhaseProtocol] PC{idx} CreateOffer failed");
                return;
            }

            var offer = offerOp.Desc;
            var setLocalOp = pc.SetLocalDescription(ref offer);
            while (!setLocalOp.IsDone)
                await Task.Yield();

            if (setLocalOp.IsError)
            {
                Debug.LogError($"[PhaseProtocol] PC{idx} SetLocal failed");
                return;
            }

            // Send offer first
            await SendTextAsync($"{{\"type\":\"offer\",\"monitorIndex\":{idx},\"sdp\":\"{EscapeJsonString(offer.sdp)}\"}}");
            Debug.Log($"[PhaseProtocol] PC{idx} offer sent (parallel)");

            // Mark offer as sent and flush queued candidates
            wrapper.OfferSent = true;
            if (wrapper.QueuedCandidates.Count > 0)
            {
                Debug.Log($"[PhaseProtocol] PC{idx} flushing {wrapper.QueuedCandidates.Count} queued ICE candidates");
                foreach (var candJson in wrapper.QueuedCandidates)
                {
                    _ = SendTextAsync(candJson);
                }
                wrapper.QueuedCandidates.Clear();
            }
        }

        /// <summary>
        /// Sequential fallback for Android or when parallel fails.
        /// Uses event-driven TaskCompletionSource instead of polling for lower latency.
        /// </summary>
        private async Task CreatePeerConnectionsSequentialAsync(int count)
        {
            Debug.Log($"[PhaseProtocol] Sequential fallback for {count} PCs");

            // Clear any partial results from parallel attempt
            lock (_lock)
            {
                foreach (var w in _peerConnections)
                {
                    try { w.PC?.Dispose(); } catch { }
                }
                _peerConnections.Clear();
            }

            for (int i = 0; i < count; i++)
            {
                int idx = i;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await CreateSinglePCAsync(idx);

                // Get wrapper for this PC
                PCWrapper wrapper;
                lock (_lock)
                {
                    wrapper = _peerConnections.FirstOrDefault(p => p.Index == idx);
                }

                if (wrapper?.AnswerReceivedTcs != null)
                {
                    // Event-driven wait for answer (no polling!) with 5s timeout
                    var timeoutTask = Task.Delay(5000);
                    var completedTask = await Task.WhenAny(wrapper.AnswerReceivedTcs.Task, timeoutTask);

                    if (completedTask == wrapper.AnswerReceivedTcs.Task)
                        Debug.Log($"[PhaseProtocol] PC{idx} answer received in {sw.ElapsedMilliseconds}ms (sequential event-driven)");
                    else
                        Debug.LogWarning($"[PhaseProtocol] PC{idx} answer timeout after {sw.ElapsedMilliseconds}ms (sequential)");
                }
            }
        }

        /// <summary>
        /// Set codec preferences for a transceiver.
        /// </summary>
        private void SetCodecPreferences(RTCRtpTransceiver trans, int idx)
        {
            var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
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
        }

        /// <summary>
        /// Setup event handlers for a PeerConnection.
        /// </summary>
        private void SetupPCEventHandlers(RTCPeerConnection pc, PCWrapper wrapper, int idx)
        {
            pc.OnIceConnectionChange = s =>
            {
                Debug.Log($"[PhaseProtocol] PC{idx} ICE: {s}");
            };

            pc.OnConnectionStateChange = s =>
            {
                Debug.Log($"[PhaseProtocol] PC{idx} State: {s}");
                if (s == RTCPeerConnectionState.Connected)
                {
                    wrapper.LastConnectedTime = DateTime.UtcNow;
                    wrapper.IsReconnecting = false;
                    wrapper.ReconnectAttempts = 0;
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    if (_stateMachine.IsStreaming && !wrapper.IsReconnecting)
                    {
                        wrapper.IsReconnecting = true;
                        Debug.Log($"[PhaseProtocol] PC{idx} initiating auto-heal...");
                        _ = AutoHealMonitorAsync(idx);
                    }
                }
            };

            // ICE candidates - queue until offer is sent (fixes candidate-before-offer bug)
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate))
                {
                    // Only send end_of_candidates if offer was already sent
                    if (wrapper.OfferSent)
                        _ = SendTextAsync($"{{\"type\":\"end_of_candidates\",\"monitorIndex\":{idx}}}");
                    return;
                }

                string msg = cand.Candidate;
                if (_skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                    return;

                string rawCandidate = msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase)
                    ? msg.Substring("candidate:".Length)
                    : msg;

                string candidateJson = $"{{\"type\":\"candidate\",\"monitorIndex\":{idx},\"candidate\":\"{EscapeJsonString(rawCandidate)}\"}}";

                // Queue candidate if offer not sent yet, otherwise send immediately
                if (!wrapper.OfferSent)
                {
                    wrapper.QueuedCandidates.Add(candidateJson);
                    Debug.Log($"[PhaseProtocol] PC{idx} queued ICE candidate (offer not sent yet)");
                }
                else
                {
                    _ = SendTextAsync(candidateJson);
                }
            };

            // Track received
            pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                {
                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow;
                    v.OnVideoReceived += tex =>
                    {
                        wrapper.Texture = tex;
                        wrapper.LastFrameTime = DateTime.UtcNow;
                        wrapper.FrameCount++;
                        OnVideoTextureReceived?.Invoke(idx, tex);
                    };
                    Debug.Log($"[PhaseProtocol] PC{idx} received video track");
                }
            };
        }

        private async Task HandleAnswerAsync(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            var rawSdp = json.GetString("sdp") ?? "";

            // Debug: Show raw SDP info (first 200 chars, escape control chars for visibility)
            var rawPreview = rawSdp.Length > 200 ? rawSdp.Substring(0, 200) : rawSdp;
            rawPreview = rawPreview.Replace("\r", "\\r").Replace("\n", "\\n");
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} raw SDP preview: {rawPreview}");

            var sdp = FixSdp(rawSdp);

            Debug.Log($"[PhaseProtocol] PC{monitorIndex} received answer (SDP: {rawSdp.Length} -> {sdp.Length} bytes)");

            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count)
                {
                    Debug.LogWarning($"[PhaseProtocol] PC{monitorIndex} answer ignored: index out of range");
                    return;
                }
                wrapper = _peerConnections[monitorIndex];
            }

            if (wrapper?.PC == null)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} wrapper or PC is NULL!");
                return;
            }

            try
            {
                // Check PC is in correct state
                if (wrapper.PC.SignalingState != RTCSignalingState.HaveLocalOffer)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} wrong state: {wrapper.PC.SignalingState}");
                    return;
                }

                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var setRemoteOp = wrapper.PC.SetRemoteDescription(ref answer);

                if (setRemoteOp == null)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription returned null!");
                    return;
                }

                // Wait for operation to complete (max 5 seconds)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!setRemoteOp.IsDone && sw.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(10);
                }

                if (!setRemoteOp.IsDone || setRemoteOp.IsError)
                {
                    var errorMsg = setRemoteOp.IsError ? setRemoteOp.Error.message ?? "unknown" : "timeout";
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription failed: {errorMsg}");

                    // Debug: Log full SDP on error for analysis
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} Failed SDP (full):\n{sdp}");
                    return;
                }

                wrapper.AnswerSet = true;
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} answer set OK ({sw.ElapsedMilliseconds}ms)");

                // Signal per-wrapper TCS for sequential mode (immediate response)
                wrapper.AnswerReceivedTcs?.TrySetResult(true);

                // Signal event-driven waiting if all answers received (parallel mode)
                SignalIfAllAnswersReceived();

                // Process pending ICE candidates
                foreach (var cand in wrapper.PendingIce)
                    AddIceCandidate(wrapper, cand);
                wrapper.PendingIce.Clear();

                CheckIceComplete();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} HandleAnswerAsync error: {ex.Message}");
                // Log full SDP for debugging
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} Exception SDP (full):\n{sdp}");
            }
        }

        /// <summary>
        /// Signal TaskCompletionSource when all expected answers are received.
        /// Called from HandleAnswerAsync for event-driven answer waiting (eliminates polling latency).
        /// </summary>
        private void SignalIfAllAnswersReceived()
        {
            if (_allAnswersReceivedTcs == null || _allAnswersReceivedTcs.Task.IsCompleted)
                return;

            int answersReceived;
            lock (_lock)
            {
                answersReceived = _peerConnections.Count(p => p.AnswerSet);
            }

            if (answersReceived >= _expectedMonitorCount)
            {
                Debug.Log($"[PhaseProtocol] All {_expectedMonitorCount} answers received, signaling TCS");
                _allAnswersReceivedTcs.TrySetResult(true);
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
                int created = _peerConnections.Count;
                int answered = _peerConnections.Count(p => p.AnswerSet);
                int expected = _expectedMonitorCount;
                Debug.Log($"[PhaseProtocol] CheckIceComplete: {answered}/{created} PCs have answers (expected {expected} total), phase={_stateMachine.CurrentPhase}");

                // IMPORTANT: Wait for ALL expected PCs to be created AND have answers
                // This prevents proceeding too early when creating PCs sequentially
                if (created >= expected && created > 0 && _peerConnections.All(p => p.AnswerSet))
                {
                    allReady = true;
                    if (_stateMachine.CurrentPhase == ConnectionPhase.ICENegotiating)
                    {
                        Debug.Log($"[PhaseProtocol] All {expected} PeerConnections ready, transitioning to ReadyToStream");
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

            // Create new PeerConnection with STUN servers for better stability
            var iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer { urls = new[] { "stun:stun1.l.google.com:19302" } },
            };
            var cfg = new RTCConfiguration { iceServers = iceServers };
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
                if (s == RTCPeerConnectionState.Connected)
                {
                    Debug.Log($"[PhaseProtocol] PC{idx} reconnect successful!");
                    wrapper.LastConnectedTime = DateTime.UtcNow;
                    wrapper.IsReconnecting = false;
                    wrapper.ReconnectAttempts = 0; // Reset on successful reconnection
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    Debug.LogWarning($"[PhaseProtocol] PC{idx} connection lost again after reconnect");
                    // Auto-heal again if still streaming
                    if (_stateMachine.IsStreaming && !wrapper.IsReconnecting)
                    {
                        wrapper.IsReconnecting = true;
                        Debug.Log($"[PhaseProtocol] PC{idx} re-initiating auto-heal...");
                        _ = AutoHealMonitorAsync(idx);
                    }
                }
            };

            // ICE candidates - queue until offer is sent (same fix as initial connection)
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate))
                {
                    if (wrapper.OfferSent)
                        _ = SendTextAsync($"{{\"type\":\"end_of_candidates\",\"monitorIndex\":{idx}}}");
                    return;
                }

                string msg = cand.Candidate;
                if (_skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                    return;

                string rawCandidate = msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase)
                    ? msg.Substring("candidate:".Length)
                    : msg;

                string candidateJson = $"{{\"type\":\"candidate\",\"monitorIndex\":{idx},\"candidate\":\"{EscapeJsonString(rawCandidate)}\"}}";

                if (!wrapper.OfferSent)
                {
                    wrapper.QueuedCandidates.Add(candidateJson);
                    Debug.Log($"[PhaseProtocol] PC{idx} queued ICE candidate on reconnect (offer not sent yet)");
                }
                else
                {
                    _ = SendTextAsync(candidateJson);
                }
            };

            // Track received
            pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                {
                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow; // Initialize
                    v.OnVideoReceived += tex =>
                    {
                        wrapper.Texture = tex;
                        wrapper.LastFrameTime = DateTime.UtcNow;
                        wrapper.FrameCount++;
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

            // Reset state for new offer
            wrapper.OfferSent = false;
            wrapper.QueuedCandidates.Clear();

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

            // Send offer first
            await SendTextAsync($"{{\"type\":\"offer\",\"monitorIndex\":{idx},\"sdp\":\"{EscapeJsonString(offer.sdp)}\"}}");
            Debug.Log($"[PhaseProtocol] PC{idx} reconnect offer sent");

            // Mark offer as sent and flush queued candidates
            wrapper.OfferSent = true;
            if (wrapper.QueuedCandidates.Count > 0)
            {
                Debug.Log($"[PhaseProtocol] PC{idx} flushing {wrapper.QueuedCandidates.Count} queued ICE candidates on reconnect");
                foreach (var candJson in wrapper.QueuedCandidates)
                {
                    _ = SendTextAsync(candJson);
                }
                wrapper.QueuedCandidates.Clear();
            }
        }

        /// <summary>
        /// Client-side auto-heal: Detect disconnection and automatically attempt reconnect.
        /// This runs independently of server's reconnect request for faster recovery.
        /// Uses exponential backoff: 2s, 4s, 8s, 16s, 32s between attempts.
        /// </summary>
        private async Task AutoHealMonitorAsync(int monitorIndex)
        {
            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count) return;
                wrapper = _peerConnections[monitorIndex];
            }

            // Check max attempts
            if (wrapper.ReconnectAttempts >= PCWrapper.MaxReconnectAttempts)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} auto-heal: max attempts ({PCWrapper.MaxReconnectAttempts}) reached, giving up");
                wrapper.IsReconnecting = false;
                OnError?.Invoke($"Monitor {monitorIndex} reconnect failed after {PCWrapper.MaxReconnectAttempts} attempts");
                return;
            }

            // Exponential backoff: 2s, 4s, 8s, 16s, 32s
            int delayMs = 2000 * (1 << wrapper.ReconnectAttempts);
            wrapper.ReconnectAttempts++;
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: attempt {wrapper.ReconnectAttempts}/{PCWrapper.MaxReconnectAttempts}, waiting {delayMs}ms...");
            await Task.Delay(delayMs);

            // Check if we're still streaming and need reconnect
            if (_cts == null || _cts.IsCancellationRequested) return;
            if (!_stateMachine.IsStreaming)
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: no longer streaming, skip reconnect");
                wrapper.IsReconnecting = false;
                return;
            }

            // Check if PC is still disconnected
            var state = wrapper.PC?.ConnectionState ?? RTCPeerConnectionState.Closed;
            if (state == RTCPeerConnectionState.Connected)
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: already recovered, skip reconnect");
                wrapper.IsReconnecting = false;
                wrapper.ReconnectAttempts = 0; // Reset on success
                return;
            }

            Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: PC still {state}, initiating reconnect...");

            try
            {
                await ReconnectMonitorAsync(monitorIndex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} auto-heal failed: {ex.Message}");
                wrapper.IsReconnecting = false;
            }
        }

        // === Phase 3 Handlers ===

        private void HandleStreamingStarted(SimpleJson json)
        {
            Debug.Log("[PhaseProtocol] Streaming started!");
            _stateMachine.TryTransition(ConnectionPhase.Streaming);

            // Acquire Android power locks for stable streaming
            try
            {
                AndroidStreamingHelper.Instance?.AcquireLocks();
                AndroidStreamingHelper.Instance?.SetKeepScreenOn(true);
            }
            catch { }

            // Start frame stall monitor to detect frozen streams
            _ = FrameStallMonitorAsync(_cts.Token);

            OnStreamingStarted?.Invoke();
        }

        /// <summary>
        /// Monitor for frame stalls - detect when video frames stop arriving even though
        /// the WebRTC connection appears healthy. This catches cases where the decoder
        /// freezes but the connection state doesn't change.
        /// </summary>
        private async Task FrameStallMonitorAsync(CancellationToken ct)
        {
            const int CHECK_INTERVAL_MS = 2000; // Check every 2 seconds
            const int STALL_THRESHOLD_MS = 5000; // Consider stalled if no frames for 5 seconds
            const int INITIAL_GRACE_PERIOD_MS = 10000; // Wait 10 seconds before monitoring

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
                // More aggressive keepalive (3s) for better connection stability on mobile
                await Task.Delay(3000, ct);
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

        /// <summary>
        /// Fix SDP to be compatible with Unity WebRTC.
        /// SIMPLIFIED to match browser implementation - only 2 essential transformations:
        /// 1. Normalize line endings to \r\n (SDP standard RFC 4566)
        /// 2. SAVP → SAVPF (required for DTLS-SRTP)
        /// 3. Remove embedded ICE candidates (server sends them separately via trickle ICE)
        /// </summary>
        private string FixSdp(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return sdp;

            // Fix 0: Normalize line endings to \r\n (SDP standard)
            // Unity WebRTC is strict about line endings - must be consistent CRLF
            sdp = sdp.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = sdp.Split('\n');

            // Fix 1: SAVP → SAVPF (browser does this too)
            // Use regex-like replacement with word boundary to avoid SAVPFF bug
            var processedLines = new List<string>();
            int removedCount = 0;

            foreach (var line in lines)
            {
                // Skip empty lines (SDP shouldn't have empty lines)
                if (string.IsNullOrEmpty(line))
                    continue;

                // Trim trailing whitespace but preserve line content
                var trimmedLine = line.TrimEnd();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Fix 2: Remove embedded ICE candidates (browser does this too)
                // Match browser behavior: check startsWith directly
                if (trimmedLine.StartsWith("a=candidate:"))
                {
                    removedCount++;
                    continue;
                }

                // Fix SAVP → SAVPF for m= lines
                string fixedLine = trimmedLine;
                if (trimmedLine.StartsWith("m=") && trimmedLine.Contains("UDP/TLS/RTP/SAVP") && !trimmedLine.Contains("UDP/TLS/RTP/SAVPF"))
                {
                    fixedLine = trimmedLine.Replace("UDP/TLS/RTP/SAVP", "UDP/TLS/RTP/SAVPF");
                }

                processedLines.Add(fixedLine);
            }

            if (removedCount > 0)
            {
                Debug.Log($"[PhaseProtocol] FixSdp: Removed {removedCount} embedded ICE candidates");
            }

            // Join with CRLF as per RFC 4566 (SDP standard)
            // Unity WebRTC may be stricter than browser about line endings
            var result = string.Join("\r\n", processedLines);

            // Ensure trailing CRLF (some WebRTC implementations require it)
            if (!result.EndsWith("\r\n"))
            {
                result += "\r\n";
            }

            // Debug: Log first few lines of fixed SDP
            var previewLines = processedLines.Take(5);
            Debug.Log($"[PhaseProtocol] FixSdp result preview: {string.Join(" | ", previewLines)}");
            Debug.Log($"[PhaseProtocol] FixSdp total lines: {processedLines.Count}, result length: {result.Length}");

            return result;
        }

        private int _pollCount = 0;

        /// <summary>
        /// Update textures (call from Update loop).
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

                        // Debug log periodically (every ~60 polls for first PC only)
                        if (wrapper.Index == 0 && _pollCount % 60 == 1)
                        {
                            Debug.Log($"[PhaseProtocol] PollTextures PC{wrapper.Index}: " +
                                $"track={(track != null ? "valid" : "null")}, " +
                                $"tex={(tex != null ? $"{tex.width}x{tex.height}" : "null")}, " +
                                $"cached={(wrapper.Texture != null ? "set" : "null")}, " +
                                $"frames={wrapper.FrameCount}, polls={_pollCount}");
                        }

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
                _expectedMonitorCount = 0;
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
