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
    public class PhaseProtocolClient : IDisposable
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

        /// <summary>
        /// Event fired when skip_to_live is acknowledged by server.
        /// </summary>
        public event Action OnSkipToLiveAck;

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
            if (_ws?.State != WebSocketState.Open || !_stateMachine.IsStreaming)
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

        #region Quality Feedback for Adaptive Bitrate

        // Quality feedback timing (for adaptive bitrate decisions)
        private DateTime _lastQualityFeedbackTime = DateTime.MinValue;
        private const float QUALITY_FEEDBACK_INTERVAL_SECONDS = 3.0f; // Send comprehensive feedback every 3 seconds
        private int _pollCount = 0; // For occasional logging

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
        /// Event fired when server adjusts bitrate based on quality feedback.
        /// </summary>
        public event Action<int, int, string> OnBitrateAdjusted; // monitorIndex, bitrateKbps, reason

        /// <summary>
        /// Event fired when server sends quality recommendation.
        /// </summary>
        public event Action<string, string> OnQualityRecommendation; // recommendation, reason

        #endregion

        #endregion

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
        /// Create PeerConnections - now uses SINGLE-PC MULTI-TRACK mode.
        /// Creates ONE PeerConnection with N video transceivers (one per monitor).
        /// Benefits: 1 ICE negotiation, 1 DTLS handshake, better bandwidth sharing.
        /// </summary>
        private async Task CreatePeerConnectionsAsync(int count)
        {
            Debug.Log($"[PhaseProtocol] Creating SINGLE PeerConnection with {count} video transceivers (Single-PC Multi-Track mode)");

            _expectedMonitorCount = count;

            // Initialize event-driven answer waiting
            _allAnswersReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Create single PC with N transceivers
            await CreateSinglePCMultiTrackAsync(count);
        }

        /// <summary>
        /// Create a single PeerConnection with N video transceivers.
        /// Produces a single offer with N m= sections.
        /// </summary>
        private async Task CreateSinglePCMultiTrackAsync(int count)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"[PhaseProtocol] CreateSinglePCMultiTrackAsync: Creating PeerConnection for {count} monitors...");

            // EMPTY ICE servers - no STUN for LAN mode
            var cfg = new RTCConfiguration { iceServers = new RTCIceServer[0] };
            var pc = new RTCPeerConnection(ref cfg);
            Debug.Log($"[PhaseProtocol] PeerConnection created: {pc != null}, SignalingState={pc?.SignalingState}");

            // Create wrapper for each track (for texture/frame tracking)
            var trackWrappers = new List<PCWrapper>();
            for (int i = 0; i < count; i++)
            {
                var wrapper = new PCWrapper
                {
                    Index = i,
                    PC = pc, // All wrappers share the same PC
                    AnswerReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                };
                trackWrappers.Add(wrapper);
            }

            // Add N video transceivers (RecvOnly)
            var transceivers = new List<RTCRtpTransceiver>();
            for (int i = 0; i < count; i++)
            {
                var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
                SetCodecPreferences(trans, i);
                transceivers.Add(trans);
                Debug.Log($"[PhaseProtocol] Added transceiver {i} for monitor {i}");
            }

            // Setup event handlers for single PC
            SetupSinglePCEventHandlers(pc, trackWrappers, transceivers);

            // Store wrappers
            lock (_lock)
            {
                _peerConnections.Clear();
                _peerConnections.AddRange(trackWrappers);
            }

            // Create offer (contains N m= sections)
            var offerOp = pc.CreateOffer();
            while (!offerOp.IsDone)
                await Task.Yield();

            if (offerOp.IsError)
            {
                Debug.LogError("[PhaseProtocol] Single-PC CreateOffer failed");
                return;
            }

            var offer = offerOp.Desc;
            var setLocalOp = pc.SetLocalDescription(ref offer);
            while (!setLocalOp.IsDone)
                await Task.Yield();

            if (setLocalOp.IsError)
            {
                Debug.LogError("[PhaseProtocol] Single-PC SetLocal failed");
                return;
            }

            // Mark all wrappers as offer sent
            foreach (var w in trackWrappers)
                w.OfferSent = true;

            // Send SINGLE offer (no monitorIndex)
            await SendTextAsync($"{{\"type\":\"offer\",\"monitorIndex\":0,\"sdp\":\"{EscapeJsonString(offer.sdp)}\"}}");
            Debug.Log($"[PhaseProtocol] Single-PC offer sent with {count} m= sections ({sw.ElapsedMilliseconds}ms)");

            // Wait for answer with timeout
            const int ANSWER_TIMEOUT_MS = 10000;
            try
            {
                var timeoutTask = Task.Delay(ANSWER_TIMEOUT_MS);
                var completedTask = await Task.WhenAny(_allAnswersReceivedTcs.Task, timeoutTask);

                if (completedTask == _allAnswersReceivedTcs.Task)
                {
                    Debug.Log($"[PhaseProtocol] Single-PC answer received in {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    Debug.LogWarning($"[PhaseProtocol] Single-PC answer timeout after {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Answer waiting exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Setup event handlers for Single-PC Multi-Track mode.
        /// Maps tracks to monitors via transceiver index.
        /// </summary>
        private void SetupSinglePCEventHandlers(RTCPeerConnection pc, List<PCWrapper> trackWrappers, List<RTCRtpTransceiver> transceivers)
        {
            Debug.Log($"[PhaseProtocol] Setting up Single-PC event handlers for {trackWrappers.Count} tracks");

            pc.OnIceConnectionChange = s =>
            {
                Debug.Log($"[PhaseProtocol] Single-PC ICE: {s}");

                // Fire progress for all monitors
                int progress = s switch
                {
                    RTCIceConnectionState.New => 0,
                    RTCIceConnectionState.Checking => 30,
                    RTCIceConnectionState.Connected => 100,
                    RTCIceConnectionState.Completed => 100,
                    _ => 0
                };
                for (int i = 0; i < trackWrappers.Count; i++)
                    OnMonitorIceProgress?.Invoke(i, progress);

                if (s == RTCIceConnectionState.Connected || s == RTCIceConnectionState.Completed)
                {
                    for (int i = 0; i < trackWrappers.Count; i++)
                        OnMonitorIceComplete?.Invoke(i);
                    CheckAllMonitorsConnected();
                }
            };

            pc.OnConnectionStateChange = s =>
            {
                Debug.Log($"[PhaseProtocol] Single-PC State: {s}");
                if (s == RTCPeerConnectionState.Connected)
                {
                    foreach (var w in trackWrappers)
                    {
                        w.LastConnectedTime = DateTime.UtcNow;
                        w.IsReconnecting = false;
                        w.ReconnectAttempts = 0;
                    }
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    if (_stateMachine.IsStreaming && !trackWrappers[0].IsReconnecting)
                    {
                        trackWrappers[0].IsReconnecting = true;
                        Debug.Log("[PhaseProtocol] Single-PC initiating full reconnect...");
                        _ = ReconnectSinglePCAsync();
                    }
                }
            };

            // ICE candidates - single connection, no monitorIndex
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate))
                {
                    Debug.Log("[PhaseProtocol] Single-PC local ICE gathering complete (empty candidate)");
                    if (trackWrappers[0].OfferSent)
                        _ = SendTextAsync("{\"type\":\"end_of_candidates\",\"monitorIndex\":0}");
                    return;
                }

                string msg = cand.Candidate;
                Debug.Log($"[PhaseProtocol] Single-PC local ICE candidate: {msg.Substring(0, Math.Min(60, msg.Length))}...");

                if (_skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.Log("[PhaseProtocol] Skipping TCP candidate");
                    return;
                }

                string rawCandidate = msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase)
                    ? msg.Substring("candidate:".Length)
                    : msg;

                // USB Mode: Filter out non-USB candidates (only allow same subnet as server)
                if (!ShouldSendIceCandidate(rawCandidate))
                    return;

                _ = SendTextAsync($"{{\"type\":\"candidate\",\"monitorIndex\":0,\"candidate\":\"{EscapeJsonString(rawCandidate)}\"}}");
            };

            // Track received - map to monitor via transceiver
            pc.OnTrack = e =>
            {
                Debug.Log($"[PhaseProtocol] Single-PC OnTrack: kind={e.Track?.Kind}, enabled={e.Track?.Enabled}");

                if (e.Track is VideoStreamTrack v)
                {
                    // Find which transceiver this track belongs to
                    int trackIndex = -1;
                    for (int i = 0; i < transceivers.Count; i++)
                    {
                        if (transceivers[i] == e.Transceiver)
                        {
                            trackIndex = i;
                            break;
                        }
                    }

                    if (trackIndex < 0 || trackIndex >= trackWrappers.Count)
                    {
                        Debug.LogWarning($"[PhaseProtocol] Received track for unknown transceiver, mid={e.Transceiver?.Mid}");
                        return;
                    }

                    var wrapper = trackWrappers[trackIndex];
                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow;

                    int idx = trackIndex; // Capture for closure
                    v.OnVideoReceived += tex =>
                    {
                        wrapper.Texture = tex;
                        wrapper.LastFrameTime = DateTime.UtcNow;
                        wrapper.FrameCount++;
                        wrapper.RenderedFrameCount++;
                        wrapper.TotalFramesReceived++; // Cumulative counter (never reset)

                        // Track stream start time
                        if (wrapper.StreamStartTime == DateTime.MinValue)
                            wrapper.StreamStartTime = DateTime.UtcNow;

                        if (!_streamingStartedFired)
                        {
                            _streamingStartedFired = true;
                            Debug.Log($"[PhaseProtocol] Track {idx} received first frame, firing OnStreamingStarted");
                            _stateMachine.TryTransition(ConnectionPhase.Streaming);
                            OnStreamingStarted?.Invoke();
                        }

                        OnVideoTextureReceived?.Invoke(idx, tex);
                    };

                    Debug.Log($"[PhaseProtocol] Track {trackIndex} received video, mid={e.Transceiver?.Mid}");
                }
            };
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
        /// Priority: Selected codec → H264 → VP9 → VP8 → others
        /// </summary>
        private void SetCodecPreferences(RTCRtpTransceiver trans, int idx)
        {
            var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);

            // Get all codec groups
            var h265 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H265", StringComparison.OrdinalIgnoreCase) ||
                                               (c.mimeType ?? "").Contains("HEVC", StringComparison.OrdinalIgnoreCase)).ToArray();
            var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
            var vp9 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("VP9", StringComparison.OrdinalIgnoreCase)).ToArray();
            var vp8 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("VP8", StringComparison.OrdinalIgnoreCase)).ToArray();
            var others = caps.codecs.Except(h265).Except(h264).Except(vp9).Except(vp8).ToArray();

            RTCRtpCodecCapability[] preferredCodecs;

            switch (_selectedCodec)
            {
                case VideoCodec.H265:
                    // H265 → H264 → VP9 → VP8 → others
                    preferredCodecs = h265.Concat(h264).Concat(vp9).Concat(vp8).Concat(others).ToArray();
                    break;

                case VideoCodec.VP9:
                    // VP9 → H264 → VP8 → others (skip H265 as not supported)
                    preferredCodecs = vp9.Concat(h264).Concat(vp8).Concat(others).ToArray();
                    break;

                case VideoCodec.VP8:
                    // VP8 → H264 → VP9 → others
                    preferredCodecs = vp8.Concat(h264).Concat(vp9).Concat(others).ToArray();
                    break;

                case VideoCodec.H264:
                default:
                    // H264 → VP9 → VP8 → others
                    preferredCodecs = h264.Concat(vp9).Concat(vp8).Concat(others).ToArray();
                    break;
            }

            Debug.Log($"[PhaseProtocol] PC{idx} codec preferences: {_selectedCodec} first, total {preferredCodecs.Length} codecs");
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

                // Fire ICE progress events for UI
                int progress = s switch
                {
                    RTCIceConnectionState.New => 0,
                    RTCIceConnectionState.Checking => 30,
                    RTCIceConnectionState.Connected => 100,
                    RTCIceConnectionState.Completed => 100,
                    _ => 0
                };
                OnMonitorIceProgress?.Invoke(idx, progress);

                // Fire complete event when connected
                if (s == RTCIceConnectionState.Connected || s == RTCIceConnectionState.Completed)
                {
                    OnMonitorIceComplete?.Invoke(idx);
                    CheckAllMonitorsConnected();
                }
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

                // USB Mode: Filter out non-USB candidates (only allow same subnet as server)
                if (!ShouldSendIceCandidate(rawCandidate))
                    return;

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

            // Track received - may be called multiple times for multi-track Single-PC mode
            pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                {
                    var mid = e.Transceiver?.Mid ?? "null";
                    var trackId = v.Id ?? "unknown";
                    Debug.Log($"[PhaseProtocol] PC{idx} OnTrack: mid={mid}, trackId={trackId}");

                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow;

                    // Capture mid for callback logging
                    var capturedMid = mid;
                    v.OnVideoReceived += tex =>
                    {
                        wrapper.Texture = tex;
                        wrapper.LastFrameTime = DateTime.UtcNow;
                        wrapper.FrameCount++;
                        wrapper.RenderedFrameCount++; // For adaptive FPS feedback
                        wrapper.TotalFramesReceived++; // Cumulative counter (never reset)

                        // Track stream start time
                        if (wrapper.StreamStartTime == DateTime.MinValue)
                            wrapper.StreamStartTime = DateTime.UtcNow;

                        // Debug: Log callback trigger (first few frames only)
                        if (wrapper.FrameCount <= 3)
                        {
                            Debug.Log($"[PhaseProtocol] PC{idx} OnVideoReceived mid={capturedMid}, frame={wrapper.FrameCount}, tex={tex?.width}x{tex?.height}");
                        }

                        // Fire OnStreamingStarted on first frame if not already fired
                        // This is a backup mechanism in case streaming_started message is delayed/lost
                        if (!_streamingStartedFired)
                        {
                            _streamingStartedFired = true;
                            Debug.Log($"[PhaseProtocol] PC{idx} received first frame, firing OnStreamingStarted as backup");
                            _stateMachine.TryTransition(ConnectionPhase.Streaming);
                            OnStreamingStarted?.Invoke();
                        }

                        OnVideoTextureReceived?.Invoke(idx, tex);
                    };
                    Debug.Log($"[PhaseProtocol] PC{idx} received video track, mid={mid}");
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
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SignalingState={wrapper.PC.SignalingState}, IceState={wrapper.PC.IceConnectionState}");
                if (wrapper.PC.SignalingState != RTCSignalingState.HaveLocalOffer)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} wrong state: {wrapper.PC.SignalingState}");
                    return;
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} calling SetRemoteDescription (SDP len={sdp.Length})...");
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var setRemoteOp = wrapper.PC.SetRemoteDescription(ref answer);

                if (setRemoteOp == null)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription returned null!");
                    return;
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription called, waiting for completion...");

                // Wait for operation to complete (max 5 seconds)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int waitCount = 0;
                while (!setRemoteOp.IsDone && sw.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(10);
                    waitCount++;
                    if (waitCount % 100 == 0) // Log every 1 second
                        Debug.Log($"[PhaseProtocol] PC{monitorIndex} still waiting... {sw.ElapsedMilliseconds}ms, IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError}");
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription wait done: IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError}, elapsed={sw.ElapsedMilliseconds}ms");

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

                // Single-PC mode: Mark ALL wrappers as AnswerSet (they share the same PC)
                // This is safe because all wrappers point to the same PC
                lock (_lock)
                {
                    bool isSinglePCMode = _peerConnections.Count > 1 &&
                                          _peerConnections.All(w => w.PC == wrapper.PC);
                    if (isSinglePCMode)
                    {
                        Debug.Log("[PhaseProtocol] Single-PC mode: marking all wrappers as AnswerSet");
                        foreach (var w in _peerConnections)
                        {
                            w.AnswerSet = true;
                            w.AnswerReceivedTcs?.TrySetResult(true);
                        }
                    }
                    else
                    {
                        // Legacy per-PC mode
                        wrapper.AnswerReceivedTcs?.TrySetResult(true);
                    }
                }

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
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} adding remote ICE candidate (AnswerSet=true)");
                AddIceCandidate(wrapper, candStr);
            }
            else
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} queuing remote ICE candidate (AnswerSet=false, pending={wrapper.PendingIce.Count + 1})");
                wrapper.PendingIce.Add(candStr);
            }
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

            // Use the shared codec preferences method
            SetCodecPreferences(trans, idx);

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
                    _metrics.ResetStallCount();
                    _metrics.ResetIceDisconnectCount();

                    // Send reconnect acknowledgment to server
                    _ = SendTextAsync($"{{\"type\":\"reconnect_ack\",\"monitorIndex\":{idx}}}");
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    Debug.LogWarning($"[PhaseProtocol] PC{idx} connection lost again after reconnect");
                    _metrics.RecordIceDisconnect();

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

                // USB Mode: Filter out non-USB candidates (only allow same subnet as server)
                if (!ShouldSendIceCandidate(rawCandidate))
                    return;

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
                        wrapper.RenderedFrameCount++; // For adaptive FPS feedback
                        wrapper.TotalFramesReceived++; // Cumulative counter (never reset)

                        // Track stream start time
                        if (wrapper.StreamStartTime == DateTime.MinValue)
                            wrapper.StreamStartTime = DateTime.UtcNow;

                        // Fire OnStreamingStarted on first frame if not already fired
                        if (!_streamingStartedFired)
                        {
                            _streamingStartedFired = true;
                            Debug.Log($"[PhaseProtocol] PC{idx} received first frame (reconnected), firing OnStreamingStarted");
                            _stateMachine.TryTransition(ConnectionPhase.Streaming);
                            OnStreamingStarted?.Invoke();
                        }

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
        /// Reconnect Single-PC Multi-Track mode by recreating the PeerConnection with all transceivers.
        /// Called when the shared PeerConnection fails or disconnects during streaming.
        /// </summary>
        private async Task ReconnectSinglePCAsync()
        {
            // Don't reconnect if application is shutting down
            if (_cts == null || _cts.IsCancellationRequested) return;

            // Get current monitor count
            int count;
            RTCPeerConnection oldPc;
            lock (_lock)
            {
                count = _peerConnections.Count;
                if (count == 0)
                {
                    Debug.LogWarning("[PhaseProtocol] ReconnectSinglePC: No monitors to reconnect");
                    return;
                }
                oldPc = _peerConnections[0].PC;
            }

            Debug.Log($"[PhaseProtocol] ReconnectSinglePC: Reconnecting {count} monitors...");

            // Close old PeerConnection
            try
            {
                oldPc?.Close();
                oldPc?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Error closing old PC: {ex.Message}");
            }

            // Reset answer TCS for new session
            _allAnswersReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Recreate everything using the same method as initial connection
            await CreateSinglePCMultiTrackAsync(count);

            Debug.Log("[PhaseProtocol] ReconnectSinglePC: Reconnection initiated, waiting for answer...");
        }

        /// <summary>
        /// Check if all monitors are connected and fire OnAllMonitorsReady event.
        /// </summary>
        private void CheckAllMonitorsConnected()
        {
            lock (_lock)
            {
                if (_expectedMonitorCount <= 0) return;

                int connected = _peerConnections.Count(p =>
                    p.PC != null &&
                    (p.PC.IceConnectionState == RTCIceConnectionState.Connected ||
                     p.PC.IceConnectionState == RTCIceConnectionState.Completed));

                Debug.Log($"[PhaseProtocol] CheckAllMonitorsConnected: {connected}/{_expectedMonitorCount}");

                if (connected >= _expectedMonitorCount)
                {
                    Debug.Log("[PhaseProtocol] All monitors connected, firing OnAllMonitorsReady");
                    OnAllMonitorsReady?.Invoke();
                }
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

            // Check max attempts - trigger dialog instead of just giving up
            if (wrapper.ReconnectAttempts >= PCWrapper.MaxReconnectAttempts)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} auto-heal: max attempts ({PCWrapper.MaxReconnectAttempts}) reached");
                wrapper.IsReconnecting = false;

                // Instead of just giving up, trigger dialog for user to decide
                OnReconnectFailed?.Invoke(new[] { "Retry", "Restart Session", "Disconnect" });
                return;
            }

            // Fast linear backoff for VR: 500ms, 1s, 1.5s, 2s, 2.5s (VR needs fast recovery)
            int delayMs = 500 + (wrapper.ReconnectAttempts * 500);
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

            // Check if frames are now flowing (frame stall recovered naturally)
            var timeSinceFrame = DateTime.UtcNow - wrapper.LastFrameTime;
            if (timeSinceFrame.TotalMilliseconds < 2000) // Frames flowing within last 2s
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: frames recovered ({timeSinceFrame.TotalMilliseconds:F0}ms since last frame), skip reconnect");
                wrapper.IsReconnecting = false;
                wrapper.ReconnectAttempts = 0; // Reset on success
                return;
            }

            // Check PC connection state
            var state = wrapper.PC?.ConnectionState ?? RTCPeerConnectionState.Closed;

            // If PC is connected but frames stalled, we still need to reconnect
            // This handles the case where WebRTC connection is fine but video stopped
            if (state == RTCPeerConnectionState.Connected)
            {
                Debug.LogWarning($"[PhaseProtocol] PC{monitorIndex} auto-heal: PC Connected but frames stalled for {timeSinceFrame.TotalMilliseconds:F0}ms - forcing reconnect");
            }
            else
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: PC state={state}, initiating reconnect...");
            }

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

        /// <summary>
        /// Reconnect entire session - preserves user config but re-runs Phase 2 (ICE negotiation).
        /// Called when individual track reconnects have failed and user chooses "Restart Session".
        /// </summary>
        public async Task ReconnectSessionAsync()
        {
            Debug.Log("[PhaseProtocol] Starting full session reconnect...");

            // 1. Close all PeerConnections
            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    try
                    {
                        wrapper.PC?.Close();
                        wrapper.PC?.Dispose();
                    }
                    catch { }
                }
                _peerConnections.Clear();
            }

            // 2. Reset metrics
            _metrics.ResetAll();
            _streamingStartedFired = false;

            // 3. Transition to reconnecting state
            _stateMachine.TryTransition(ConnectionPhase.Reconnecting);

            // 4. Re-run Phase 2 (ICE negotiation) with preserved config
            if (_userConfig != null && _ws?.State == WebSocketState.Open)
            {
                Debug.Log("[PhaseProtocol] Requesting Phase 2 restart with existing config");

                // Send restart request to server
                await SendTextAsync("{\"type\":\"restart_phase2\"}");

                // Server will respond with setup_complete, then we do ICE again
                OnSessionReconnectRequested?.Invoke();
            }
            else
            {
                Debug.LogError("[PhaseProtocol] Cannot reconnect - no config or WebSocket closed");
                _stateMachine.ForceTransition(ConnectionPhase.Error, "Reconnect failed - no connection");
                OnError?.Invoke("Cannot reconnect - connection lost");
            }
        }

        /// <summary>
        /// Retry track reconnect after user clicks "Retry" in dialog.
        /// Resets attempt counters and restarts auto-heal for all monitors.
        /// </summary>
        public void RetryReconnect()
        {
            Debug.Log("[PhaseProtocol] User requested retry - resetting reconnect counters");

            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    wrapper.ReconnectAttempts = 0;
                    wrapper.IsReconnecting = false;
                }
            }

            // Trigger auto-heal for any disconnected monitors
            List<PCWrapper> wrappers;
            lock (_lock)
            {
                wrappers = _peerConnections.ToList();
            }

            foreach (var wrapper in wrappers)
            {
                var state = wrapper.PC?.ConnectionState ?? RTCPeerConnectionState.Closed;
                if (state != RTCPeerConnectionState.Connected && !wrapper.IsReconnecting)
                {
                    wrapper.IsReconnecting = true;
                    _ = AutoHealMonitorAsync(wrapper.Index);
                }
            }
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
        /// Handle cursor position update from server.
        /// </summary>
        private void HandleCursorPosition(SimpleJson json)
        {
            int monitorIndex = json.GetInt("monitorIndex");
            float u = json.GetFloat("u");
            float v = json.GetFloat("v");
            bool visible = json.GetBool("visible");
            int cursorTypeInt = json.GetInt("cursorType", 1); // Default: Arrow
            long cursorId = json.GetLong("cursorId", 0);
            CursorType cursorType = (CursorType)cursorTypeInt;

            // Invoke event for ConnectionViewModel to handle
            OnCursorPosition?.Invoke(monitorIndex, u, v, visible, cursorType, cursorId);
        }

        /// <summary>
        /// Handle cursor image from server.
        /// Decodes PNG data and creates texture for cursor rendering.
        /// </summary>
        private void HandleCursorImage(SimpleJson json)
        {
            try
            {
                long cursorId = json.GetLong("cursorId");
                int cursorTypeInt = json.GetInt("cursorType");
                int width = json.GetInt("width");
                int height = json.GetInt("height");
                int hotspotX = json.GetInt("hotspotX");
                int hotspotY = json.GetInt("hotspotY");
                string imageBase64 = json.GetString("imageBase64");
                CursorType cursorType = (CursorType)cursorTypeInt;

                Debug.Log($"[PhaseProtocol] Received cursor_image: id={cursorId}, type={cursorType}, size={width}x{height}, base64Len={imageBase64?.Length ?? 0}");

                if (string.IsNullOrEmpty(imageBase64))
                {
                    Debug.LogError("[PhaseProtocol] cursor_image has empty imageBase64!");
                    return;
                }

                // Decode raw RGBA on main thread
                VRWorkspace.Core.MainThreadDispatcher.Enqueue(() =>
                {
                    try
                    {
                        byte[] rgbaData = Convert.FromBase64String(imageBase64);
                        int expectedSize = width * height * 4;

                        Debug.Log($"[PhaseProtocol] Cursor {cursorId} decoded: {rgbaData.Length} bytes, expected {expectedSize}");

                        // Handle size mismatch - truncate if larger, error if smaller
                        if (rgbaData.Length < expectedSize)
                        {
                            Debug.LogError($"[PhaseProtocol] RGBA too small: got {rgbaData.Length}, need {expectedSize} for {width}x{height}");
                            return;
                        }

                        // If larger, truncate to expected size
                        if (rgbaData.Length > expectedSize)
                        {
                            Debug.LogWarning($"[PhaseProtocol] RGBA larger than expected ({rgbaData.Length} > {expectedSize}), truncating");
                            var truncated = new byte[expectedSize];
                            Array.Copy(rgbaData, truncated, expectedSize);
                            rgbaData = truncated;
                        }

                        // Create texture with exact dimensions
                        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        texture.filterMode = FilterMode.Point; // Crisp cursor edges

                        // Load raw RGBA data directly
                        texture.LoadRawTextureData(rgbaData);
                        texture.Apply();

                        Debug.Log($"[PhaseProtocol] Cursor texture loaded: {texture.width}x{texture.height}, format={texture.format}");

                        OnCursorImageReceived?.Invoke(cursorId, cursorType, texture, hotspotX, hotspotY);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PhaseProtocol] Failed to decode cursor image: {ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Failed to parse cursor_image: {ex.Message}");
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
        public int GetInt(string key, int defaultValue) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : defaultValue;
        public long GetLong(string key) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : 0;
        public long GetLong(string key, long defaultValue) => _data.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : defaultValue;
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
