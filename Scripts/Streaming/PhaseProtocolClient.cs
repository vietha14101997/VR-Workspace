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
    /// Cursor type enum matching Windows system cursors.
    /// Values must match server-side CursorType enum.
    /// </summary>
    public enum CursorType
    {
        Unknown = 0,
        Arrow = 1,
        IBeam = 2,
        Wait = 3,
        Cross = 4,
        SizeNWSE = 5,
        SizeNESW = 6,
        SizeWE = 7,
        SizeNS = 8,
        SizeAll = 9,
        No = 10,
        Hand = 11,
        AppStarting = 12,
        Help = 13,
        UpArrow = 14,
        Custom = 99
    }

    /// <summary>
    /// Client-side handler for 3-phase connection protocol.
    /// Manages WebSocket connection, phase transitions, and WebRTC setup.
    /// </summary>
    public partial class PhaseProtocolClient : IDisposable
    {
        // Logging control - set to false to reduce RAM usage from frequent logs
        public static bool VerboseLogging = false;

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
        private bool _streamingStartedFired; // Track if OnStreamingStarted was already fired

        // Frame timing sync (for latency calculation)
        private long _serverClockOffset; // Difference between client and server time (ms)
        private float _lastServerTargetFps = 60f; // Last known target FPS from server

        // Streaming metrics for real-time monitoring
        private readonly StreamingMetrics _metrics = new StreamingMetrics();
        private long _lastPingSentTime;
        private long _lastPongReceivedTime;
        private int _missedPongCount;

        // USB Mode - when enabled, only use USB Tethering interface for ICE
        private bool _isUsbMode = false;
        private string _usbServerIP = null;  // Server's USB Tethering IP (for subnet filtering)

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
        public event Action<int, float, float, bool, CursorType, long> OnCursorPosition; // monitorIndex, u, v, visible, cursorType, cursorId
        public event Action<long, CursorType, Texture2D, int, int> OnCursorImageReceived; // cursorId, cursorType, texture, hotspotX, hotspotY
        public event Action<int, float> OnFpsAdjusted; // monitorIndex, targetFps - server adjusted encoding FPS
        public event Action<long, long> OnFrameTimingReceived; // serverTime, clockOffset - for latency calculation

        // Progress tracking events for UI
        public event Action<int> OnServerSetupProgress;         // 0-100 server setup progress
        public event Action<int, int> OnMonitorIceProgress;     // monitorIndex, progress (0-100)
        public event Action<int> OnMonitorIceComplete;          // monitorIndex when ICE connected
        public event Action OnAllMonitorsReady;                 // all monitors ICE connected

        // Connection health and reconnection events
        public event Action OnConnectionHealthCritical;         // Server not responding, trigger reconnect
        public event Action<string[]> OnReconnectFailed;        // Max attempts reached, show dialog with options
        public event Action OnSessionReconnectRequested;        // Full session restart requested

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
            public IntPtr LastTexturePtr; // Track texture pointer for change detection
            public bool TexturePtrDetectionWorking; // True if we've detected texture ptr changes
            public DateTime FirstTextureTime; // When we first got a texture (for grace period)
            public int RealFrameCount; // Count ONLY when texture pointer actually changes (for freeze detection)
            public TaskCompletionSource<bool> AnswerReceivedTcs; // For event-driven sequential mode

            // FPS feedback tracking (for adaptive encoding)
            public int RenderedFrameCount;           // Frames actually rendered this window
            public int DroppedFrameCount;            // Frames dropped (received but not rendered)
            public DateTime FpsWindowStart = DateTime.UtcNow; // Start of measurement window
            public DateTime LastFpsFeedbackSent;     // Throttle feedback sending
            public float LastReportedEffectiveFps;   // For change detection

            // Cumulative counters for pipeline comparison (never reset)
            public long TotalFramesReceived;         // Total frames from OnVideoReceived (decode output)
            public DateTime StreamStartTime;         // When first frame received
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
        public long ServerClockOffset => _serverClockOffset; // For external latency calculations
        public float ServerTargetFps => _lastServerTargetFps; // Current target FPS from server
        public StreamingMetrics Metrics => _metrics; // Real-time streaming metrics
        public bool IsUsbMode => _isUsbMode; // Current USB mode state

        /// <summary>
        /// Set USB Mode for ICE candidate filtering.
        /// When USB Mode is enabled, only ICE candidates from the USB Tethering subnet are sent.
        /// This ensures WebRTC media goes exclusively over the USB cable.
        /// </summary>
        /// <param name="isUsb">True to enable USB-only mode</param>
        /// <param name="serverIP">Server's USB Tethering IP (e.g., "192.168.110.168")</param>
        public void SetUsbMode(bool isUsb, string serverIP = null)
        {
            _isUsbMode = isUsb;
            _usbServerIP = serverIP;
            Debug.Log($"[PhaseProtocol] USB Mode: {_isUsbMode}, Server IP: {_usbServerIP ?? "null"}");
        }

        /// <summary>
        /// Check if an ICE candidate should be sent based on USB mode.
        /// In USB mode, only candidates from the same subnet as the server are allowed.
        /// </summary>
        private bool ShouldSendIceCandidate(string candidateStr)
        {
            if (!_isUsbMode)
                return true; // WiFi mode: send all candidates

            if (string.IsNullOrEmpty(_usbServerIP))
            {
                Debug.LogWarning("[PhaseProtocol] USB mode but no server IP set, sending candidate anyway");
                return true;
            }

            // Parse IP from ICE candidate string
            // Format: "candidate:foundation component protocol priority ip port typ type ..."
            // Example: "3193877933 1 udp 2122260223 192.168.110.118 47799 typ host"
            var parts = candidateStr.Split(' ');
            if (parts.Length < 5)
                return false;

            string candidateIP = parts[4]; // IP is the 5th field (index 4)

            // Check if candidate IP is in the same /24 subnet as server
            // This ensures we only use the USB Tethering interface
            string serverSubnet = GetSubnet24(_usbServerIP);
            string candidateSubnet = GetSubnet24(candidateIP);

            bool isSameSubnet = serverSubnet == candidateSubnet;

            if (!isSameSubnet)
            {
                Debug.Log($"[PhaseProtocol] USB Mode: Filtering out non-USB candidate: {candidateIP} (server subnet: {serverSubnet})");
            }
            else
            {
                Debug.Log($"[PhaseProtocol] USB Mode: Allowing USB candidate: {candidateIP}");
            }

            return isSameSubnet;
        }

        /// <summary>
        /// Get /24 subnet prefix from IP address (e.g., "192.168.110.168" → "192.168.110")
        /// </summary>
        private string GetSubnet24(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return "";

            int lastDot = ip.LastIndexOf('.');
            if (lastDot <= 0)
                return ip;

            return ip.Substring(0, lastDot);
        }

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

                // Forward ping results immediately for fresher UI
                _speedTest.OnPingJitterResult += (ping, jitter) =>
                {
                   if (_networkInfo == null) _networkInfo = new NetworkTestResult();
                   _networkInfo.pingMs = ping;
                   _networkInfo.jitterMs = jitter;

                   if (string.IsNullOrEmpty(_networkInfo.connectionType))
                   {
                       _networkInfo.connectionType = _isUsbMode ? "USB" : "WiFi";
                   }

                   OnNetworkInfoReceived?.Invoke(_networkInfo);
                };

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
            await StartStreamingAsync(false);
        }

        /// <summary>
        /// Proceed from Phase 2 to Phase 3 (start streaming).
        /// </summary>
        /// <param name="force">If true, bypass phase check (used for auto-start after ICE ready)</param>
        public async Task StartStreamingAsync(bool force)
        {
            var currentPhase = _stateMachine.CurrentPhase;
            bool canStart = currentPhase == ConnectionPhase.ReadyToStream ||
                           currentPhase == ConnectionPhase.ICENegotiating;

            if (!canStart && !force)
            {
                Debug.LogWarning($"[PhaseProtocol] Cannot start streaming from {currentPhase}");
                return;
            }

            if (force && currentPhase != ConnectionPhase.ReadyToStream)
            {
                Debug.Log($"[PhaseProtocol] Force starting streaming from {currentPhase}");
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
        /// Pause streaming - server stops capture/encode but keeps connection.
        /// Use when going back to menu during streaming.
        /// </summary>
        public async Task PauseStreamingAsync()
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Debug.LogWarning("[PhaseProtocol] PauseStreaming skipped: WebSocket not open");
                return;
            }

            if (!_stateMachine.IsStreaming)
            {
                Debug.LogWarning($"[PhaseProtocol] PauseStreaming skipped: not streaming (state={_stateMachine?.CurrentPhase})");
                return;
            }

            try
            {
                Debug.Log("[PhaseProtocol] Sending pause_streaming");
                await SendTextAsync("{\"type\":\"pause_streaming\"}");
                _isStreamingPaused = true;
                Debug.Log("[PhaseProtocol] pause_streaming sent - frame detection suppressed");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] PauseStreaming failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Resume streaming - server restarts capture/encode.
        /// Use when returning from menu to continue streaming.
        /// </summary>
        public async Task ResumeStreamingAsync()
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Debug.LogWarning("[PhaseProtocol] ResumeStreaming skipped: WebSocket not open");
                return;
            }

            if (!_stateMachine.IsStreaming)
            {
                Debug.LogWarning($"[PhaseProtocol] ResumeStreaming skipped: not streaming (state={_stateMachine?.CurrentPhase})");
                return;
            }

            try
            {
                _isStreamingPaused = false;
                Debug.Log("[PhaseProtocol] Sending resume_streaming");
                await SendTextAsync("{\"type\":\"resume_streaming\"}");
                Debug.Log("[PhaseProtocol] resume_streaming sent - frame detection resumed");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] ResumeStreaming failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Pause a specific monitor's streaming.
        /// Server stops encoding for that monitor but keeps connection alive.
        /// </summary>
        /// <param name="monitorIndex">Index of the monitor to pause (0-based)</param>
        public async Task PauseMonitorAsync(int monitorIndex)
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Debug.LogWarning("[PhaseProtocol] PauseMonitor skipped: WebSocket not open");
                return;
            }

            if (!_stateMachine.IsStreaming)
            {
                Debug.LogWarning($"[PhaseProtocol] PauseMonitor skipped: not streaming (state={_stateMachine?.CurrentPhase})");
                return;
            }

            try
            {
                string json = $"{{\"type\":\"pause_monitor\",\"monitorIndex\":{monitorIndex}}}";
                Debug.Log($"[PhaseProtocol] Sending pause_monitor: index={monitorIndex}");
                await SendTextAsync(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] PauseMonitor failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Resume a specific monitor's streaming.
        /// Server restarts encoding for that monitor and sends keyframe.
        /// </summary>
        /// <param name="monitorIndex">Index of the monitor to resume (0-based)</param>
        public async Task ResumeMonitorAsync(int monitorIndex)
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Debug.LogWarning("[PhaseProtocol] ResumeMonitor skipped: WebSocket not open");
                return;
            }

            if (!_stateMachine.IsStreaming)
            {
                Debug.LogWarning($"[PhaseProtocol] ResumeMonitor skipped: not streaming (state={_stateMachine?.CurrentPhase})");
                return;
            }

            try
            {
                string json = $"{{\"type\":\"resume_monitor\",\"monitorIndex\":{monitorIndex}}}";
                Debug.Log($"[PhaseProtocol] Sending resume_monitor: index={monitorIndex}");
                await SendTextAsync(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] ResumeMonitor failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Request server to send a keyframe immediately.
        /// Call this when user interacts (click, drag, etc.) for instant visual update.
        /// </summary>
        /// <param name="monitorIndex">Monitor index, or -1 for all monitors</param>
        public async void RequestKeyframe(int monitorIndex = -1)
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Debug.LogWarning($"[PhaseProtocol] RequestKeyframe skipped: ws={_ws?.State}");
                return;
            }
            if (!_stateMachine.IsStreaming)
            {
                Debug.LogWarning($"[PhaseProtocol] RequestKeyframe skipped: not streaming (state={_stateMachine?.CurrentPhase})");
                return;
            }

            try
            {
                string json = monitorIndex >= 0
                    ? $"{{\"type\":\"request_keyframe\",\"monitorIndex\":{monitorIndex}}}"
                    : "{\"type\":\"request_keyframe\"}";

                Debug.Log($"[PhaseProtocol] Sending request_keyframe: {json}");
                await SendTextAsync(json);
                Debug.Log($"[PhaseProtocol] request_keyframe SENT successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] RequestKeyframe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Request server to skip buffered frames and send fresh keyframe.
        /// Use when detecting accumulated latency.
        /// </summary>
        /// <param name="monitorIndex">Monitor index to sync, or -1 for all monitors</param>
        public async void SkipToLive(int monitorIndex = -1)
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Debug.LogWarning($"[PhaseProtocol] SkipToLive skipped: ws={_ws?.State}");
                return;
            }
            if (!_stateMachine.IsStreaming)
            {
                Debug.LogWarning($"[PhaseProtocol] SkipToLive skipped: not streaming");
                return;
            }

            // Cooldown to avoid spamming (adaptive based on skip frequency)
            if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds < SkipToLiveCooldownSeconds)
                return;

            _lastSkipToLiveTime = DateTime.UtcNow;
            _consecutiveSkipCount++;
            _skipCountResetTime = DateTime.UtcNow;

            // Log if cooldown is increased due to spam
            if (_consecutiveSkipCount >= SKIP_COUNT_THRESHOLD)
            {
                Debug.LogWarning($"[PhaseProtocol] skip_to_live frequency high ({_consecutiveSkipCount} in 30s), cooldown now {SkipToLiveCooldownSeconds:F1}s");
            }

            try
            {
                string msg = monitorIndex >= 0
                    ? $"{{\"type\":\"skip_to_live\",\"monitor\":{monitorIndex}}}"
                    : "{\"type\":\"skip_to_live\"}";
                Debug.Log($"[PhaseProtocol] Sending skip_to_live (monitor={monitorIndex}) for latency recovery");
                await SendTextAsync(msg);
                Debug.Log($"[PhaseProtocol] skip_to_live SENT successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] SkipToLive failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Update streaming configuration dynamically during Phase 3.
        /// Sends update_config message to server for FPS/Bitrate changes.
        /// </summary>
        /// <param name="fps">New target FPS (null = no change)</param>
        /// <param name="bitrateKbps">New TOTAL bitrate in kbps for all monitors (null = no change)</param>
        public async Task UpdateConfigAsync(int? fps, int? bitrateKbps)
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Debug.LogWarning($"[PhaseProtocol] UpdateConfigAsync skipped: ws={_ws?.State}");
                return;
            }
            if (!_stateMachine.IsStreaming)
            {
                Debug.LogWarning($"[PhaseProtocol] UpdateConfigAsync skipped: not streaming");
                return;
            }

            try
            {
                // Build JSON with nullable fields
                var parts = new List<string> { "\"type\":\"update_config\"" };
                if (fps.HasValue)
                    parts.Add($"\"fps\":{fps.Value}");
                if (bitrateKbps.HasValue)
                    parts.Add($"\"bitrateKbps\":{bitrateKbps.Value}");

                string json = "{" + string.Join(",", parts) + "}";
                Debug.Log($"[PhaseProtocol] Sending update_config: {json}");
                await SendTextAsync(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] UpdateConfigAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Main receive loop for WebSocket messages.
        /// Includes timeout detection for hung connections.
        /// </summary>
        private int _msgCounter = 0;
        private const int RECEIVE_TIMEOUT_MS = 30000; // 30s timeout for receive operations

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[256 * 1024]; // 256KB buffer for speed test chunks

            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult firstResult;

                    try
                    {
                        // Add timeout to receive operation
                        using var timeoutCts = new CancellationTokenSource(RECEIVE_TIMEOUT_MS);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                        // First receive to determine message type
                        firstResult = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), linkedCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Timeout occurred - check connection health
                        Debug.LogWarning("[PhaseProtocol] WebSocket receive timeout - checking connection health");

                        if (_missedPongCount > 0)
                        {
                            Debug.LogError("[PhaseProtocol] Connection appears dead - triggering reconnect");
                            OnConnectionHealthCritical?.Invoke();
                        }

                        // Continue loop to retry receive
                        continue;
                    }

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

            // SPECIAL HANDLING for cursor_image - bypass SimpleJson to avoid base64 corruption
            if (text.Contains("\"type\":\"cursor_image\"") || text.Contains("\"Type\":\"cursor_image\""))
            {
                // Log raw message length to detect WebSocket-level corruption
                Debug.Log($"[PhaseProtocol] cursor_image RAW message length: {text.Length} chars");
                HandleCursorImageRaw(text);
                return;
            }

            // Try to parse as JSON
            if (text.StartsWith("{"))
            {
                try
                {
                    var json = SimpleJson.Parse(text);
                    // Handle both "type" (camelCase) and "Type" (PascalCase) from server
                    var type = json.GetString("type") ?? json.GetString("Type");

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
                            _ = HandleAnswerAsync(json);
                            break;

                        case "candidate":
                            HandleCandidate(json);
                            break;

                        case "end_of_candidates":
                            HandleEndOfCandidates(json);
                            break;

                        case "ice_ready":
                            HandleIceReady(json);
                            break;

                        case "streaming_started":
                            HandleStreamingStarted(json);
                            break;

                        case "cursor_position":
                            HandleCursorPosition(json);
                            break;

                        case "cursor_image":
                            // Should not reach here anymore - handled by raw parser above
                            HandleCursorImage(json);
                            break;

                        case "error":
                            HandleError(json);
                            break;

                        case "ping":
                            // Server sent JSON ping - respond immediately with pong
                            _ = SendTextAsync("{\"type\":\"pong\"}");
                            break;

                        case "pong":
                            // Handle pong for RTT calculation and keepalive
                            HandlePongMessage(text);
                            // Also notify speed test client for ping measurement
                            _speedTest?.HandlePong();
                            break;

                        case "frameTiming":
                            // Server frame timing for clock sync and latency calculation
                            HandleFrameTiming(json);
                            break;

                        case "fps_adjusted":
                            // Server response to fps_feedback - indicates encoding FPS was adjusted
                            HandleFpsAdjusted(json);
                            break;

                        case "bitrate_adjusted":
                            // Server response to quality_feedback - bitrate was changed for adaptive streaming
                            HandleBitrateAdjusted(json);
                            break;

                        case "quality_recommendation":
                            // Server recommendation based on sustained quality analysis
                            HandleQualityRecommendation(json);
                            break;

                        case "skip_to_live_ack":
                            // Server acknowledged skip_to_live request - stream should recover now
                            Debug.Log("[PhaseProtocol] skip_to_live acknowledged by server");
                            OnSkipToLiveAck?.Invoke();
                            break;

                        case "reconnect_required":
                            // Server detected connection failure (e.g., DTLS timeout) and requires full reconnect
                            Debug.LogWarning("[PhaseProtocol] Server requested reconnect - DTLS/connection failed");
                            // Trigger full session reconnect
                            _ = ReconnectSessionAsync();
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
            else if (text.Equals("pong", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("pong:", StringComparison.OrdinalIgnoreCase))
            {
                // Handle pong for RTT calculation (supports "pong" and "pong:timestamp")
                HandlePongMessage(text);
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
        /// Unity WebRTC supports H.264, VP9, and VP8 decoding natively.
        /// H.265/HEVC is NOT supported by Unity WebRTC VideoStreamTrack.
        /// </summary>
        private ClientCodecCapability GetClientCodecCapability()
        {
            // Unity WebRTC supports H264, VP9, VP8 natively
            // Priority: H264 (hardware) > VP9 (better quality) > VP8 (fallback)
            var capability = new ClientCodecCapability
            {
                supportedCodecs = new[] { "H264", "VP9", "VP8" },
                preferredCodec = "H264",
                supportsHevc = false,  // Unity WebRTC does NOT support H.265 decoding
                supportsVP9 = true,    // Unity WebRTC supports VP9 natively
                supportsVP8 = true,    // Unity WebRTC supports VP8 natively
                deviceModel = SystemInfo.deviceModel,
                apiLevel = 0
            };

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Get Android API level for diagnostics
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    capability.apiLevel = version.GetStatic<int>("SDK_INT");
                }
                Debug.Log($"[PhaseProtocol] Android API level: {capability.apiLevel}, device: {capability.deviceModel}");

                // NOTE: HevcDecoderPlugin checks MediaCodec HEVC support, but Unity WebRTC
                // has its own built-in decoder that only supports H.264, VP9, VP8.
                // We keep this check for diagnostics only.
                bool pluginAvailable = false;
                try
                {
                    pluginAvailable = HevcDecoderPlugin.IsAvailable();
                    Debug.Log($"[PhaseProtocol] HevcDecoderPlugin.IsAvailable() = {pluginAvailable} (not used by WebRTC)");
                }
                catch (Exception pluginEx)
                {
                    Debug.LogWarning($"[PhaseProtocol] HevcDecoderPlugin check failed: {pluginEx.Message}");
                }

                Debug.Log($"[PhaseProtocol] Client codecs: H264, VP9, VP8 (Unity WebRTC native). MediaCodec HEVC={pluginAvailable}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Failed to get device info: {ex.Message}");
            }
#else
            Debug.Log("[PhaseProtocol] Client codecs: H264, VP9, VP8 (Unity WebRTC native)");
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
        /// USB Mode: Skip speedtest entirely - server will measure USB latency via ICMP.
        /// </summary>
        private async Task RunSpeedTestInBackgroundAsync()
        {
            try
            {
                // USB Mode: Skip WebSocket speedtest - it gives misleading results over USB
                // Server will measure actual USB latency using ICMP ping to gateway
                if (_isUsbMode)
                {
                    Debug.Log("[PhaseProtocol] USB Mode: Skipping WebSocket speedtest, waiting for server values [BUILD 2026-01-06 v4]");

                    // Use INVALID values (-1) to indicate "waiting for server"
                    // UI will show "--" until real values arrive from server
                    _networkInfo = new NetworkTestResult
                    {
                        pingMs = -1,              // Invalid - will be set by server
                        jitterMs = -1,            // Invalid - will be set by server
                        bandwidthMbps = -1,       // Invalid - will be set by server
                        connectionType = "USB",
                        isUsbMode = true,
                        usbLatencyMs = -1,        // Invalid - will be set by server
                        usbVersion = null,        // Null - will be set by server
                        usbEstimatedBandwidthMbps = -1  // Invalid - will be set by server
                    };

                    // Send speedtest result to server (use 480 as placeholder for server calculation)
                    await SendSpeedTestResultAsync(0.5, 0.1, 480);

                    // Fire event for UI to show "waiting" state
                    OnNetworkInfoReceived?.Invoke(_networkInfo);
                    Debug.Log("[PhaseProtocol] USB Mode: Sent speedtest, waiting for server USB measurement");

                    // Wait for suggested_config from Server (Server will measure USB latency)
                    _stateMachine.TryTransition(ConnectionPhase.AwaitingSuggestedConfig);
                    return;
                }

                // Standard WiFi/LAN: Run full speedtest
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
        /// Send speedtest result to server.
        /// </summary>
        private async Task SendSpeedTestResultAsync(double pingMs, double jitterMs, double bandwidthMbps)
        {
            // Use explicit ToString with InvariantCulture for each value
            // This is more reliable on Android IL2CPP than string.Format with format specifiers
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var bwStr = bandwidthMbps.ToString("F1", culture);
            var pingStr = pingMs.ToString("F1", culture);
            var jitterStr = jitterMs.ToString("F1", culture);

            var json = "{\"type\":\"speedtest_result\",\"bandwidthMbps\":" + bwStr +
                       ",\"pingMs\":" + pingStr +
                       ",\"jitterMs\":" + jitterStr + "}";
            await SendTextAsync(json);
            Debug.Log($"[PhaseProtocol] Sent speedtest_result: {bwStr}Mbps, {pingStr}ms, jitter={jitterStr}ms");
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

            // Detect WiFi connection and update thresholds
            UpdateConnectionType(_networkInfo.connectionType, _networkInfo.jitterMs);

            bool transitioned = _stateMachine.TryTransition(ConnectionPhase.AwaitingSuggestedConfig);
            Debug.Log($"[PhaseProtocol] Transition to AwaitingSuggestedConfig: {transitioned}, new phase: {_stateMachine.CurrentPhase}");

            OnNetworkInfoReceived?.Invoke(_networkInfo);
        }

        private void HandleSuggestedConfig(SimpleJson json)
        {
            Debug.Log($"[PhaseProtocol] Received suggested_config, current phase: {_stateMachine.CurrentPhase}");

            var resolution = json.GetObject("resolution");

            // Parse connectionType from server (USB, WiFi, LAN, Internet)
            string connectionType = json.GetString("connectionType") ?? "Unknown";

            _suggestedConfig = new SuggestedStreamConfig
            {
                monitors = json.GetInt("monitors"),
                resolutionWidth = resolution?.GetInt("w") ?? 1920,
                resolutionHeight = resolution?.GetInt("h") ?? 1080,
                bitrateKbps = json.GetInt("bitrateKbps"),
                fps = json.GetInt("fps"),
                refreshRate = json.GetInt("refreshRate"),
                reason = json.GetString("reason") ?? "",
                selectedCodec = json.GetString("selectedCodec") ?? "H264",
                connectionType = connectionType
            };

            // Update _networkInfo.connectionType with server's transport type
            // Server knows if we're on USB tethering, which TCP speedtest can't detect
            if (_networkInfo != null && !string.IsNullOrEmpty(connectionType) && connectionType != "Unknown")
            {
                _networkInfo.connectionType = connectionType;
                Debug.Log($"[PhaseProtocol] Updated networkInfo.connectionType to: {connectionType}");

                // Also update network info from server if provided (more accurate)
                var networkInfo = json.GetObject("networkInfo");
                if (networkInfo != null)
                {
                    double pingMs = networkInfo.GetDouble("pingMs");
                    double jitterMs = networkInfo.GetDouble("jitterMs");
                    double bandwidthMbps = networkInfo.GetDouble("bandwidthMbps");

                    // Parse USB-specific fields
                    bool isUsbMode = networkInfo.GetBool("isUsbMode");
                    double usbLatencyMs = networkInfo.GetDouble("usbLatencyMs");
                    string usbVersion = networkInfo.GetString("usbVersion");
                    double usbEstimatedBandwidthMbps = networkInfo.GetDouble("usbEstimatedBandwidthMbps");

                    Debug.Log($"[PhaseProtocol] *** PARSED networkInfo from server ***");
                    Debug.Log($"[PhaseProtocol]   pingMs={pingMs}, jitterMs={jitterMs}, bandwidthMbps={bandwidthMbps}");
                    Debug.Log($"[PhaseProtocol]   isUsbMode={isUsbMode}, usbLatencyMs={usbLatencyMs}, usbVersion={usbVersion}, usbEstimatedBandwidthMbps={usbEstimatedBandwidthMbps}");

                    if (pingMs > 0) _networkInfo.pingMs = pingMs;
                    if (jitterMs >= 0) _networkInfo.jitterMs = jitterMs;
                    if (bandwidthMbps > 0) _networkInfo.bandwidthMbps = bandwidthMbps;

                    Debug.Log($"[PhaseProtocol] Updated networkInfo from server: {pingMs:F1}ms, {bandwidthMbps:F1}Mbps");

                    // Handle USB Mode - set metrics with USB-specific latency info
                    if (isUsbMode)
                    {
                        Debug.Log($"[PhaseProtocol] *** USB Mode ACTIVATED from server ***");
                        Debug.Log($"[PhaseProtocol]   ICMP Latency: {usbLatencyMs:F2}ms (vs WebSocket ping: {pingMs:F1}ms)");
                        Debug.Log($"[PhaseProtocol]   USB Version: {usbVersion}");
                        Debug.Log($"[PhaseProtocol]   Estimated Bandwidth: {usbEstimatedBandwidthMbps:F0}Mbps");

                        _metrics.SetUsbMode(true, usbLatencyMs, usbVersion, usbEstimatedBandwidthMbps);

                        // Store USB info in network result for UI
                        _networkInfo.isUsbMode = true;
                        _networkInfo.usbLatencyMs = usbLatencyMs;
                        _networkInfo.usbVersion = usbVersion;
                        _networkInfo.usbEstimatedBandwidthMbps = usbEstimatedBandwidthMbps;

                        Debug.Log($"[PhaseProtocol] *** _networkInfo AFTER USB update ***");
                        Debug.Log($"[PhaseProtocol]   isUsbMode={_networkInfo.isUsbMode}");
                        Debug.Log($"[PhaseProtocol]   usbLatencyMs={_networkInfo.usbLatencyMs}");
                        Debug.Log($"[PhaseProtocol]   usbVersion={_networkInfo.usbVersion}");
                        Debug.Log($"[PhaseProtocol]   usbEstimatedBandwidthMbps={_networkInfo.usbEstimatedBandwidthMbps}");
                    }
                }
                else
                {
                    Debug.LogWarning("[PhaseProtocol] networkInfo object is NULL in suggested_config!");
                }

                // Fire event again so UI can update with new connectionType
                Debug.Log($"[PhaseProtocol] *** FIRING OnNetworkInfoReceived event with USB data ***");
                OnNetworkInfoReceived?.Invoke(_networkInfo);
            }
            else
            {
                Debug.LogWarning($"[PhaseProtocol] Skipping networkInfo update: _networkInfo={_networkInfo != null}, connectionType={connectionType}");
            }

            // Update selected codec based on server's decision
            _selectedCodec = _suggestedConfig.selectedCodec.ToUpperInvariant() switch
            {
                "H265" => VideoCodec.H265,
                "VP9" => VideoCodec.VP9,
                "VP8" => VideoCodec.VP8,
                _ => VideoCodec.H264  // Default to H264
            };

            Debug.Log($"[PhaseProtocol] Suggested: {_suggestedConfig.monitors}mon @ {_suggestedConfig.resolutionWidth}x{_suggestedConfig.resolutionHeight}, {_suggestedConfig.fps}fps, {_suggestedConfig.bitrateKbps}kbps");
            Debug.Log($"[PhaseProtocol] Selected codec: {_suggestedConfig.selectedCodec}");
            Debug.Log($"[PhaseProtocol] Connection type: {connectionType}");
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

            // Fire server setup progress for UI (0-100)
            OnServerSetupProgress?.Invoke(progress);
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

        // === Phase 3 Handlers ===

        private void HandleStreamingStarted(SimpleJson json)
        {
            Debug.Log("[PhaseProtocol] Streaming started (server message)!");
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

            // Only fire event if not already fired (could be triggered by first frame)
            if (!_streamingStartedFired)
            {
                _streamingStartedFired = true;
                OnStreamingStarted?.Invoke();
            }
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

        /// <summary>
        /// Aggressive keepalive loop: 2s ping interval, 6s timeout.
        /// Detects server disconnection and triggers reconnect.
        /// </summary>
        private async Task KeepaliveLoopAsync(CancellationToken ct)
        {
            const int PING_INTERVAL_MS = 2000;  // Aggressive: 2s
            const int PONG_TIMEOUT_MS = 6000;   // 6s timeout
            const int MAX_MISSED_PONGS = 3;     // After 3 missed pongs, trigger critical

            // Initialize timestamp
            _lastPongReceivedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await Task.Delay(1000, ct);

            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    // Check for missed pongs
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var timeSinceLastPong = now - _lastPongReceivedTime;

                    if (_lastPongReceivedTime > 0 && timeSinceLastPong > PONG_TIMEOUT_MS)
                    {
                        _missedPongCount++;
                        _metrics.RecordMissedPong();
                        Debug.LogWarning($"[PhaseProtocol] Pong timeout! {timeSinceLastPong}ms since last pong, missed: {_missedPongCount}");

                        if (_missedPongCount >= MAX_MISSED_PONGS)
                        {
                            Debug.LogError("[PhaseProtocol] Server not responding - triggering reconnect");
                            OnConnectionHealthCritical?.Invoke();

                            // Try to trigger auto-heal for all monitors
                            List<PCWrapper> wrappers;
                            lock (_lock)
                            {
                                wrappers = _peerConnections.ToList();
                            }
                            foreach (var wrapper in wrappers)
                            {
                                if (!wrapper.IsReconnecting)
                                {
                                    wrapper.IsReconnecting = true;
                                    _ = AutoHealMonitorAsync(wrapper.Index);
                                }
                            }
                        }
                    }

                    // Send ping with timestamp for RTT calculation
                    _lastPingSentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await SendTextAsync($"ping:{_lastPingSentTime}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PhaseProtocol] Keepalive error: {ex.Message}");
                }

                await Task.Delay(PING_INTERVAL_MS, ct);
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

            // Reset streaming state
            _streamingStartedFired = false;
            _streamingStartTime = DateTime.MinValue; // Reset for next session warmup
            _isStreamingPaused = false; // Reset pause state for next session

            // Reset metrics to avoid stale data affecting next session
            _metrics.ResetAll();

            try { _ws?.Abort(); _ws?.Dispose(); } catch { }
            _ws = null;
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
