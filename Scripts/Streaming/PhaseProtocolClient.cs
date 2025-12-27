using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;

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
            Debug.Log("[PhaseProtocol] Sending proceed message (phase 2)");
            await SendJsonAsync(new
            {
                type = "proceed",
                phase = 2
            });
            Debug.Log("[PhaseProtocol] Proceed message sent");

            // Send display config
            Debug.Log("[PhaseProtocol] Sending display_config message");
            await SendJsonAsync(new
            {
                type = "display_config",
                monitors = config.monitors,
                resolution = new { w = config.resolutionWidth, h = config.resolutionHeight },
                refreshRate = config.refreshRate,
                bitrateKbps = config.bitrateKbps,
                fps = config.fps,
                preferGpu = config.preferGpu
            });
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

            await SendJsonAsync(new { type = "start_streaming" });
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
                    await SendJsonAsync(new { type = "stop_streaming" });
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
            // Debug: Log received message (truncated)
            Debug.Log($"[PhaseProtocol] Received: {text.Substring(0, Math.Min(100, text.Length))}...");

            // Try to parse as JSON
            if (text.StartsWith("{"))
            {
                try
                {
                    var json = SimpleJson.Parse(text);
                    // Handle both "type" (camelCase) and "Type" (PascalCase) from server
                    var type = json.GetString("type") ?? json.GetString("Type");
                    Debug.Log($"[PhaseProtocol] Parsed type='{type}' from message len={text.Length}");

                    switch (type)
                    {
                        case "hardware_info":
                            await HandleHardwareInfoAsync(json);
                            break;

                        case "speedtest_start":
                            await HandleSpeedTestStartAsync(json);
                            break;

                        case "speedtest_end":
                            await HandleSpeedTestEndAsync(json);
                            break;

                        case "network_info":
                            HandleNetworkInfo(json);
                            break;

                        case "suggested_config":
                            HandleSuggestedConfig(json);
                            break;

                        case "config_progress":
                            HandleConfigProgress(json);
                            break;

                        case "config_complete":
                            await HandleConfigCompleteAsync(json);
                            break;

                        case "answer":
                            await HandleAnswerAsync(json);
                            break;

                        case "candidate":
                            HandleCandidate(json);
                            break;

                        case "end_of_candidates":
                            HandleEndOfCandidates(json);
                            break;

                        case "streaming_started":
                            HandleStreamingStarted(json);
                            break;

                        case "error":
                            HandleError(json);
                            break;

                        case "pong":
                            // Ignore keepalive response
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
                // Ignore
            }
            else
            {
                // Legacy format handling (offer:N:sdp, answer:N:sdp, candidate:N:candidate)
                await HandleLegacyMessageAsync(text);
            }
        }

        // === Phase 1 Handlers ===

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
                gpuVramMB = device?.GetLong("gpuVramMB") ?? 0,
                ramMB = device?.GetLong("ramMB") ?? 0,
                os = device?.GetString("os") ?? "Unknown",
                encoderType = encoder?.GetString("type") ?? "Unknown",
                hwAccelEnabled = encoder?.GetBool("hwAccel") ?? false,
                monitors = ParseMonitors(monitorsArr)
            };

            Debug.Log($"[PhaseProtocol] Server: {_hardwareInfo.deviceName}, GPU: {_hardwareInfo.gpu} ({_hardwareInfo.gpuVramMB}MB)");
            Debug.Log($"[PhaseProtocol] Encoder: {_hardwareInfo.encoderType}, HW: {_hardwareInfo.hwAccelEnabled}");

            _stateMachine.TryTransition(ConnectionPhase.SpeedTesting);
            OnHardwareInfoReceived?.Invoke(_hardwareInfo);

            // Send acknowledgment to server before speed test starts
            Debug.Log("[PhaseProtocol] Sending hardware_info_ack");
            await SendTextAsync("{\"type\":\"hardware_info_ack\"}");
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
                reason = json.GetString("reason") ?? ""
            };

            Debug.Log($"[PhaseProtocol] Suggested: {_suggestedConfig.monitors}mon @ {_suggestedConfig.resolutionWidth}x{_suggestedConfig.resolutionHeight}, {_suggestedConfig.fps}fps, {_suggestedConfig.bitrateKbps}kbps");
            Debug.Log($"[PhaseProtocol] Reason: {_suggestedConfig.reason}");

            bool transitioned = _stateMachine.TryTransition(ConnectionPhase.ConfiguringSettings);
            Debug.Log($"[PhaseProtocol] Transition to ConfiguringSettings: {transitioned}, new phase: {_stateMachine.CurrentPhase}");

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

        private async Task HandleConfigCompleteAsync(SimpleJson json)
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

            // Create PeerConnections and send offers
            Debug.Log("[PhaseProtocol] Starting CreatePeerConnectionsAsync...");
            await CreatePeerConnectionsAsync(_configuredMonitors.Count);
            Debug.Log("[PhaseProtocol] CreatePeerConnectionsAsync completed, receive loop should continue");
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

                // Add video transceiver
                var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
                var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
                var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
                trans.SetCodecPreferences(h264.Concat(caps.codecs.Except(h264)).ToArray());

                pc.OnIceConnectionChange = s => Debug.Log($"[PhaseProtocol] PC{idx} ICE: {s}");
                pc.OnConnectionStateChange = s =>
                {
                    Debug.Log($"[PhaseProtocol] PC{idx} State: {s}");
                    if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
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
                        _ = SendJsonAsync(new { type = "end_of_candidates", monitorIndex = idx });
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
                    _ = SendJsonAsync(new { type = "candidate", monitorIndex = idx, candidate = rawCandidate });
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

                // Create and send offer
                var offerOp = pc.CreateOffer();
                while (!offerOp.IsDone) await Task.Yield();
                if (offerOp.IsError)
                {
                    Debug.LogError($"[PhaseProtocol] PC{idx} CreateOffer failed");
                    continue;
                }

                var offer = offerOp.Desc;
                var setLocalOp = pc.SetLocalDescription(ref offer);
                while (!setLocalOp.IsDone) await Task.Yield();
                if (setLocalOp.IsError)
                {
                    Debug.LogError($"[PhaseProtocol] PC{idx} SetLocal failed");
                    continue;
                }

                await SendJsonAsync(new { type = "offer", monitorIndex = idx, sdp = offer.sdp });
                Debug.Log($"[PhaseProtocol] PC{idx} offer sent");
            }
        }

        private async Task HandleAnswerAsync(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            var sdp = FixSdp(json.GetString("sdp") ?? "");

            Debug.Log($"[PhaseProtocol] PC{monitorIndex} received answer, _peerConnections.Count={_peerConnections.Count}");

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

            var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} calling SetRemoteDescription...");

            // Call SetRemoteDescription - Unity WebRTC processes async internally
            // Don't wait for completion - WebRTC will handle it and fire ICE/connection events
            wrapper.PC.SetRemoteDescription(ref answer);

            // Mark as set immediately
            wrapper.AnswerSet = true;
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} answer set (async)");

            // Process pending ICE
            foreach (var cand in wrapper.PendingIce)
                AddIceCandidate(wrapper, cand);
            wrapper.PendingIce.Clear();

            // Check if all PCs have answers
            CheckIceComplete();
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
                _ = SendJsonAsync(new { type = "proceed", phase = 3 });
                OnReadyToStream?.Invoke();
            }
        }

        // === Phase 3 Handlers ===

        private void HandleStreamingStarted(SimpleJson json)
        {
            Debug.Log("[PhaseProtocol] Streaming started!");
            _stateMachine.TryTransition(ConnectionPhase.Streaming);
            OnStreamingStarted?.Invoke();
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

        private async Task SendJsonAsync(object obj)
        {
            var json = SimpleJson.Serialize(obj);
            await SendTextAsync(json);
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

        private string FixSdp(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return sdp;

            sdp = sdp.Replace("UDP/TLS/RTP/SAVP", "UDP/TLS/RTP/SAVPF");

            if (sdp.Contains("IP4 0.0.0.0"))
            {
                Debug.Log("[PhaseProtocol] Fixing SDP: IP4 0.0.0.0 -> IP4 127.0.0.1");
                sdp = sdp.Replace("IP4 0.0.0.0", "IP4 127.0.0.1");
            }

            var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var filtered = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                // KEEP ICE candidates from server SDP - they are needed for connection!
                // if (line.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("a=ice-options:", StringComparison.OrdinalIgnoreCase) && line.Contains("ice2"))
                {
                    line = line.Replace("ice2,", "").Replace(",ice2", "").Replace("ice2", "trickle");
                }
                filtered.Add(line);
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

            // Handle anonymous types and objects
            var sb = new StringBuilder();
            sb.Append("{");
            bool first = true;

            foreach (var prop in type.GetProperties())
            {
                if (!first) sb.Append(",");
                first = false;

                var value = prop.GetValue(obj);
                var name = char.ToLower(prop.Name[0]) + prop.Name.Substring(1); // camelCase

                sb.Append($"\"{name}\":");

                if (value == null)
                    sb.Append("null");
                else if (value is string s)
                    sb.Append($"\"{EscapeString(s)}\"");
                else if (value is bool b)
                    sb.Append(b ? "true" : "false");
                else if (value.GetType().IsPrimitive || value.GetType() == typeof(decimal))
                    sb.Append(value.ToString());
                else
                    sb.Append(Serialize(value));
            }

            sb.Append("}");
            return sb.ToString();
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
                        if (readingKey) currentKey.Append(c);
                        else currentValue.Append(c);
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        if (readingKey) currentKey.Append(c);
                        else currentValue.Append(c);
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
