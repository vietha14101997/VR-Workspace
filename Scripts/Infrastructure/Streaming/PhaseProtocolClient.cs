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
        Unknown     = 0,
        Arrow       = 1,
        IBeam       = 2,
        Wait        = 3,
        Cross       = 4,
        SizeNWSE    = 5,
        SizeNESW    = 6,
        SizeWE      = 7,
        SizeNS      = 8,
        SizeAll     = 9,
        No          = 10,
        Hand        = 11,
        AppStarting = 12,
        Help        = 13,
        UpArrow     = 14,
        Custom      = 99
    }

    /// <summary>
    /// Slim orchestrator for the 3-phase connection protocol.
    ///
    /// Responsibilities kept here:
    ///   - Owning the ClientWebSocket / CancellationTokenSource
    ///   - Running ReceiveLoopAsync and routing messages to phase handlers
    ///   - Owning the ConnectionStateMachine
    ///   - Delegating Phase 1 to <see cref="PhaseOneHandler"/>
    ///   - Delegating Phase 2 to <see cref="PhaseTwoHandler"/>
    ///   - Delegating Phase 3 to <see cref="PhaseThreeHandler"/>
    ///   - Exposing public API that callers use (ConnectAsync, ProceedToPhase2Async, etc.)
    ///   - Keeping all partial file methods intact (Cursor, Metrics, Reconnect, WebRTC)
    /// </summary>
    public partial class PhaseProtocolClient : IDisposable
    {
        // ── Logging ──────────────────────────────────────────────────────────────
        public static bool VerboseLogging = false;

        // ── WebSocket / cancellation ──────────────────────────────────────────
        private ClientWebSocket         _ws;
        private CancellationTokenSource _cts;
        private readonly object         _lock = new object();

        // ── State machine ─────────────────────────────────────────────────────
        private readonly ConnectionStateMachine _stateMachine = new ConnectionStateMachine();

        // ── Phase handlers (created on ConnectAsync) ──────────────────────────
        private PhaseOneHandler   _phase1;
        private PhaseTwoHandler   _phase2;
        private PhaseThreeHandler _phase3;

        // ── Phase 1 data (surfaced from handler for external consumers) ───────
        private ServerHardwareInfo   _hardwareInfo;
        private NetworkTestResult    _networkInfo;
        private SuggestedStreamConfig _suggestedConfig;

        // ── Phase 2 data ──────────────────────────────────────────────────────
        private StreamingConfig      _userConfig;
        private List<MonitorInfo>    _configuredMonitors = new List<MonitorInfo>();

        // ── Speed test ────────────────────────────────────────────────────────
        private SpeedTestClient _speedTest;

        // ── WebRTC ────────────────────────────────────────────────────────────
        private readonly List<PCWrapper> _peerConnections = new List<PCWrapper>();
        private bool   _skipTcpIceCandidates = true;
        private int    _expectedMonitorCount;
        private TaskCompletionSource<bool> _allAnswersReceivedTcs;
        private bool   _streamingStartedFired;
        private RTCDataChannel _sctpInitChannel; // Kept alive to maintain SCTP transport for audio DataChannel
        private int _pcGeneration; // Incremented on cleanup to guard stale PC callbacks

        // ── Frame timing / FPS ────────────────────────────────────────────────
        private long  _serverClockOffset;
        private float _lastServerTargetFps = 60f;

        // ── Streaming metrics ──────────────────────────────────────────────────
        private readonly StreamingMetrics _metrics = new StreamingMetrics();
        private long _lastPingSentTime;
        private long _lastPongReceivedTime;
        private int  _missedPongCount;

        // ── USB mode ──────────────────────────────────────────────────────────
        private bool   _isUsbMode    = false;
        private string _usbServerIP  = null;

        // ── Codec ─────────────────────────────────────────────────────────────
        private VideoCodec _selectedCodec = VideoCodec.H264;
        public  VideoCodec SelectedCodec  => _selectedCodec;

        // ── Receive loop internals ─────────────────────────────────────────────
        private int   _msgCounter            = 0;
        private const int RECEIVE_TIMEOUT_MS = 30000;

        // ── Events ───────────────────────────────────────────────────────────
        public event Action<ServerHardwareInfo>                OnHardwareInfoReceived;
        public event Action<NetworkTestResult>                 OnNetworkInfoReceived;
        public event Action<SuggestedStreamConfig>             OnSuggestedConfigReceived;
        public event Action<string, int, string>               OnConfigProgress;       // step, progress, message
        public event Action<List<MonitorInfo>>                 OnConfigComplete;
        public event Action                                    OnReadyToStream;
        public event Action<int, Texture>                      OnVideoTextureReceived; // monitorIndex, texture
        public event Action<AudioStreamTrack>                  OnAudioTrackReceived;   // remote audio track (RTP, legacy)
        public event Action<byte[]>                            OnAudioDataReceived;    // DataChannel audio (low-latency)
        public event Action                                    OnStreamingStarted;
        public event Action<string>                            OnError;
        public event Action                                    OnDisconnected;
        public event Action<int, float, float, bool, CursorType, long> OnCursorPosition;
        public event Action<long, CursorType, Texture2D, int, int>     OnCursorImageReceived;
        public event Action<int, float>                        OnFpsAdjusted;
        public event Action<long, long>                        OnFrameTimingReceived;
        public event Action<int>                               OnServerSetupProgress;
        public event Action<int, int>                          OnMonitorIceProgress;
        public event Action<int>                               OnMonitorIceComplete;
        public event Action                                    OnAllMonitorsReady;
        public event Action                                    OnConnectionHealthCritical;
        public event Action<string[]>                          OnReconnectFailed;
        public event Action                                    OnSessionReconnectRequested;
        public event Action<string, double, int>               OnSpeedTestProgress;

        // NOTE: OnSkipToLiveAck, OnBitrateAdjusted, OnQualityRecommendation are declared
        // in PhaseProtocolClient.Metrics.cs (that partial also owns SkipToLiveImmediate).

        // ── PCWrapper ─────────────────────────────────────────────────────────
        private class PCWrapper
        {
            public int   Index;
            public RTCPeerConnection PC;
            public VideoStreamTrack  VideoTrack;
            public Texture           Texture;
            public bool  AnswerSet;
            public bool  OfferSent;
            public List<string> PendingIce        = new List<string>();
            public List<string> QueuedCandidates  = new List<string>();
            public bool  IsReconnecting;
            public DateTime LastConnectedTime;
            public int   ReconnectAttempts;
            public const int MaxReconnectAttempts = 5;
            public DateTime LastFrameTime;
            public int   FrameCount;
            public IntPtr LastTexturePtr;
            public bool  TexturePtrDetectionWorking;
            public DateTime FirstTextureTime;
            public int   RealFrameCount;
            public TaskCompletionSource<bool> AnswerReceivedTcs;

            // FPS feedback tracking
            public int      RenderedFrameCount;
            public int      DroppedFrameCount;
            public DateTime FpsWindowStart      = DateTime.UtcNow;
            public DateTime LastFpsFeedbackSent;
            public float    LastReportedEffectiveFps;

            // Cumulative counters
            public long     TotalFramesReceived;
            public DateTime StreamStartTime;

            // Graduated recovery state (WiFi resilience)
            public bool IsInGraduatedRecovery;
            public int  GraduatedRecoveryStep;
        }

        // ── Public properties ─────────────────────────────────────────────────
        public ConnectionStateMachine StateMachine    => _stateMachine;
        public ServerHardwareInfo     HardwareInfo    => _hardwareInfo;
        public NetworkTestResult      NetworkInfo      => _networkInfo;
        public SuggestedStreamConfig  SuggestedConfig => _suggestedConfig;
        public StreamingConfig        UserConfig       => _userConfig;
        public int                    MonitorCount     => _peerConnections.Count;
        public bool                   IsConnected      => _ws?.State == WebSocketState.Open;
        public bool                   IsStreaming       => _stateMachine.IsStreaming;
        public long                   ServerClockOffset => _serverClockOffset;
        public float                  ServerTargetFps   => _lastServerTargetFps;
        public StreamingMetrics       Metrics           => _metrics;
        public bool                   IsUsbMode         => _isUsbMode;

        // ── USB helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Enable/disable USB-only ICE candidate filtering.
        /// </summary>
        public void SetUsbMode(bool isUsb, string serverIP = null)
        {
            _isUsbMode   = isUsb;
            _usbServerIP = serverIP;
            Debug.Log($"[PhaseProtocol] USB Mode: {_isUsbMode}, Server IP: {_usbServerIP ?? "null"}");
        }

        private bool ShouldSendIceCandidate(string candidateStr)
        {
            if (!_isUsbMode) return true;

            if (string.IsNullOrEmpty(_usbServerIP))
            {
                Debug.LogWarning("[PhaseProtocol] USB mode but no server IP set, sending candidate anyway");
                return true;
            }

            var parts = candidateStr.Split(' ');
            if (parts.Length < 5) return false;

            string candidateIP   = parts[4];
            string serverSubnet  = GetSubnet24(_usbServerIP);
            string candidateSubnet = GetSubnet24(candidateIP);
            bool   sameSubnet    = serverSubnet == candidateSubnet;

            if (!sameSubnet)
                Debug.Log($"[PhaseProtocol] USB Mode: Filtering non-USB candidate: {candidateIP} (server subnet: {serverSubnet})");
            else
                Debug.Log($"[PhaseProtocol] USB Mode: Allowing USB candidate: {candidateIP}");

            return sameSubnet;
        }

        private static string GetSubnet24(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return "";
            int lastDot = ip.LastIndexOf('.');
            return lastDot <= 0 ? ip : ip.Substring(0, lastDot);
        }

        // ── Texture access ────────────────────────────────────────────────────

        public Texture GetTexture(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _peerConnections.Count)
                    return _peerConnections[index].Texture;
                return null;
            }
        }

        // ── Connect / stop ─────────────────────────────────────────────────────

        /// <summary>
        /// Connect to the server and begin Phase 1 handshake.
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

            if (!serverUrl.Contains("protocol="))
                serverUrl += serverUrl.Contains("?") ? "&protocol=v2" : "?protocol=v2";

            Debug.Log($"[PhaseProtocol] Connecting to {serverUrl}");
            _stateMachine.TryTransition(ConnectionPhase.Connecting);

            try
            {
                _ws = new ClientWebSocket();
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await _ws.ConnectAsync(new Uri(serverUrl), ct);

                Debug.Log("[PhaseProtocol] WebSocket connected, waiting for hardware_info");
                _stateMachine.TryTransition(ConnectionPhase.AwaitingHardwareInfo);

                // Speed test client (shared between WebSocket and Phase1Handler)
                _speedTest = new SpeedTestClient(_ws, ct);
                _speedTest.OnPingJitterResult += (ping, jitter) =>
                {
                    if (_networkInfo == null) _networkInfo = new NetworkTestResult();
                    _networkInfo.pingMs   = ping;
                    _networkInfo.jitterMs = jitter;
                    if (string.IsNullOrEmpty(_networkInfo.connectionType))
                        _networkInfo.connectionType = _isUsbMode ? "USB" : "WiFi";
                    OnNetworkInfoReceived?.Invoke(_networkInfo);
                };

                // ── Create phase handlers ──────────────────────────────────────
                _phase1 = new PhaseOneHandler(SendTextAsync, _stateMachine, _isUsbMode);
                _phase1.SetSpeedTestClient(_speedTest);
                _phase1.OnHardwareInfoReady    += info  => { _hardwareInfo   = info;  OnHardwareInfoReceived?.Invoke(info); };
                _phase1.OnNetworkInfoReady     += info  => { _networkInfo    = info;  UpdateConnectionType(info.connectionType, info.jitterMs); OnNetworkInfoReceived?.Invoke(info); };
                _phase1.OnSuggestedConfigReady += cfg   => { _suggestedConfig = cfg;  _selectedCodec = _phase1.SelectedCodec; OnSuggestedConfigReceived?.Invoke(cfg); };
                _phase1.OnSpeedTestProgress    += (d, m, p) => OnSpeedTestProgress?.Invoke(d, m, p);

                _phase2 = new PhaseTwoHandler(SendTextAsync, _stateMachine, CreatePeerConnectionsInBackgroundAsync);
                _phase2.OnConfigProgress      += (s, p, m) => { OnConfigProgress?.Invoke(s, p, m); OnServerSetupProgress?.Invoke(p); };
                _phase2.OnConfigComplete      += monitors  => { _configuredMonitors = monitors; OnConfigComplete?.Invoke(monitors); };
                _phase2.OnServerSetupProgress += p         => OnServerSetupProgress?.Invoke(p);

                _phase3 = new PhaseThreeHandler(SendTextAsync, _stateMachine);
                _phase3.OnStreamingStarted       += HandleStreamingStartedInternal;
                _phase3.OnFpsAdjusted            += (idx, fps) => { _lastServerTargetFps = fps; OnFpsAdjusted?.Invoke(idx, fps); };
                _phase3.OnBitrateAdjusted        += (idx, kbps, reason) => OnBitrateAdjusted?.Invoke(idx, kbps, reason);
                _phase3.OnQualityRecommendation  += (rec, reason) => OnQualityRecommendation?.Invoke(rec, reason);
                _phase3.OnSkipToLiveAck          += () => OnSkipToLiveAck?.Invoke();

                // ── Start loops ───────────────────────────────────────────────
                _ = ReceiveLoopAsync(ct);
                _ = KeepaliveLoopAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Connection failed: {ex.Message}");
                _stateMachine.ForceTransition(ConnectionPhase.Error, ex.Message);
                OnError?.Invoke(ex.Message);
            }
        }

        // ── Streaming started (internal, called by Phase3 event) ──────────────

        private void HandleStreamingStartedInternal()
        {
            // Acquire Android power locks
            try
            {
                AndroidStreamingHelper.Instance?.AcquireLocks();
                AndroidStreamingHelper.Instance?.SetKeepScreenOn(true);
            }
            catch { }

            // Start frame stall monitor
            _ = FrameStallMonitorAsync(_cts.Token);

            if (!_streamingStartedFired)
            {
                _streamingStartedFired = true;
                OnStreamingStarted?.Invoke();
            }
        }

        // ── Phase transitions ─────────────────────────────────────────────────

        /// <summary>
        /// Proceed from Phase 1 to Phase 2 with user-selected config.
        /// </summary>
        public async Task ProceedToPhase2Async(StreamingConfig config)
        {
            Debug.Log($"[PhaseProtocol] ProceedToPhase2Async, phase={_stateMachine.CurrentPhase}");

            if (!_stateMachine.IsInPhase(ConnectionPhase.ConfiguringSettings))
            {
                Debug.LogWarning($"[PhaseProtocol] Cannot proceed to Phase 2 from {_stateMachine.CurrentPhase}");
                return;
            }

            _userConfig = config;
            await _phase2.SendConfigAsync(config);
        }

        /// <summary>
        /// Start streaming (Phase 3).
        /// </summary>
        public async Task StartStreamingAsync() => await StartStreamingAsync(false);

        /// <param name="force">Bypass phase check (used for auto-start after ICE ready)</param>
        public async Task StartStreamingAsync(bool force)
        {
            var phase    = _stateMachine.CurrentPhase;
            bool canStart = phase == ConnectionPhase.ReadyToStream ||
                            phase == ConnectionPhase.ICENegotiating;

            if (!canStart && !force)
            {
                Debug.LogWarning($"[PhaseProtocol] Cannot start streaming from {phase}");
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

        // ── Streaming control (delegates to Phase3Handler) ─────────────────────

        public async Task PauseStreamingAsync()
        {
            if (_ws?.State != WebSocketState.Open) { Debug.LogWarning("[PhaseProtocol] PauseStreaming skipped: WebSocket not open"); return; }
            await _phase3.PauseStreamingAsync();
            _isStreamingPaused = true;
        }

        public async Task ResumeStreamingAsync()
        {
            if (_ws?.State != WebSocketState.Open) { Debug.LogWarning("[PhaseProtocol] ResumeStreaming skipped: WebSocket not open"); return; }
            _isStreamingPaused = false;
            await _phase3.ResumeStreamingAsync();
        }

        public async Task PauseMonitorAsync(int monitorIndex)
        {
            if (_ws?.State != WebSocketState.Open) { Debug.LogWarning("[PhaseProtocol] PauseMonitor skipped: WebSocket not open"); return; }
            await _phase3.PauseMonitorAsync(monitorIndex);
        }

        public async Task ResumeMonitorAsync(int monitorIndex)
        {
            if (_ws?.State != WebSocketState.Open) { Debug.LogWarning("[PhaseProtocol] ResumeMonitor skipped: WebSocket not open"); return; }
            await _phase3.ResumeMonitorAsync(monitorIndex);
        }

        public async void RequestKeyframe(int monitorIndex = -1)
        {
            if (_ws?.State != WebSocketState.Open) { Debug.LogWarning($"[PhaseProtocol] RequestKeyframe skipped: ws={_ws?.State}"); return; }
            await _phase3.RequestKeyframeAsync(monitorIndex);
        }

        public async void SkipToLive(int monitorIndex = -1)
        {
            if (_ws?.State != WebSocketState.Open) { Debug.LogWarning($"[PhaseProtocol] SkipToLive skipped: ws={_ws?.State}"); return; }

            // Cooldown guard
            if ((DateTime.UtcNow - _lastSkipToLiveTime).TotalSeconds < SkipToLiveCooldownSeconds)
                return;

            _lastSkipToLiveTime = DateTime.UtcNow;
            _consecutiveSkipCount++;
            _skipCountResetTime = DateTime.UtcNow;

            if (_consecutiveSkipCount >= SKIP_COUNT_THRESHOLD)
                Debug.LogWarning($"[PhaseProtocol] skip_to_live frequency high ({_consecutiveSkipCount} in 30s), cooldown={SkipToLiveCooldownSeconds:F1}s");

            await _phase3.SkipToLiveAsync(monitorIndex);
        }

        // SkipToLiveImmediate is defined in PhaseProtocolClient.Metrics.cs partial.

        public async Task UpdateConfigAsync(int? fps, int? bitrateKbps)
        {
            if (_ws?.State != WebSocketState.Open) { Debug.LogWarning($"[PhaseProtocol] UpdateConfigAsync skipped: ws={_ws?.State}"); return; }
            await _phase3.UpdateConfigAsync(fps, bitrateKbps);
        }

        // ── Receive loop ──────────────────────────────────────────────────────

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[256 * 1024];

            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult first;

                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(RECEIVE_TIMEOUT_MS);
                        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        first = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), linked.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        Debug.LogWarning("[PhaseProtocol] Receive timeout – checking connection health");
                        if (_missedPongCount > 0)
                        {
                            Debug.LogError("[PhaseProtocol] Connection appears dead – triggering reconnect");
                            OnConnectionHealthCritical?.Invoke();
                        }
                        continue;
                    }

                    if (first.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[PhaseProtocol] WebSocket closed by server");
                        _stateMachine.ForceTransition(ConnectionPhase.Disconnected);
                        OnDisconnected?.Invoke();
                        return;
                    }

                    _msgCounter++;

                    // Binary – speed test bytes, no alloc
                    if (first.MessageType == WebSocketMessageType.Binary)
                    {
                        int total = first.Count;
                        while (!first.EndOfMessage)
                        {
                            first  = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                            total += first.Count;
                        }
                        _speedTest?.RecordBytesReceived(total);
                        _phase1?.RecordBinaryBytes(total);
                        continue;
                    }

                    // Text
                    var ms = new System.IO.MemoryStream();
                    ms.Write(buffer, 0, first.Count);
                    while (!first.EndOfMessage)
                    {
                        first = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        ms.Write(buffer, 0, first.Count);
                    }

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    if (!string.IsNullOrWhiteSpace(text))
                        await HandleTextMessageAsync(text);
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

        // ── Message dispatch ──────────────────────────────────────────────────

        private async Task HandleTextMessageAsync(string text)
        {
            if (!text.Contains("cursor_position"))
                Debug.Log($"[PhaseProtocol] Received: {text.Substring(0, Math.Min(100, text.Length))}...");

            // cursor_image has a raw-parse fast path (avoid base64 corruption via SimpleJson)
            if (text.Contains("\"type\":\"cursor_image\"") || text.Contains("\"Type\":\"cursor_image\""))
            {
                Debug.Log($"[PhaseProtocol] cursor_image RAW message length: {text.Length}");
                HandleCursorImageRaw(text);
                return;
            }

            if (text.StartsWith("{"))
            {
                try
                {
                    var json = SimpleJson.Parse(text);
                    var type = json.GetString("type") ?? json.GetString("Type");

                    switch (type)
                    {
                        // ── Phase 1 ──────────────────────────────────────────
                        case "hardware_info":
                            await _phase1.HandleHardwareInfoAsync(json);
                            break;

                        case "speedtest_start":
                            await _phase1.HandleSpeedTestStartAsync(json);
                            break;

                        case "speedtest_end":
                            await _phase1.HandleSpeedTestEndAsync(json);
                            break;

                        case "network_info":
                            _phase1.HandleNetworkInfo(json);
                            break;

                        case "suggested_config":
                            _phase1.HandleSuggestedConfig(json);
                            break;

                        // ── Phase 2 ──────────────────────────────────────────
                        case "config_progress":
                            _phase2.HandleConfigProgress(json);
                            break;

                        case "config_complete":
                            await _phase2.HandleConfigCompleteAsync(json);
                            break;

                        // ── WebRTC (partial file) ─────────────────────────────
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

                        // ── Phase 3 ──────────────────────────────────────────
                        case "streaming_started":
                            _phase3.HandleStreamingStarted(json);
                            break;

                        case "fps_adjusted":
                            _phase3.HandleFpsAdjusted(json);
                            break;

                        case "bitrate_adjusted":
                            _phase3.HandleBitrateAdjusted(json);
                            break;

                        case "quality_recommendation":
                            _phase3.HandleQualityRecommendation(json);
                            break;

                        case "skip_to_live_ack":
                            _phase3.HandleSkipToLiveAck();
                            break;

                        // ── Cursor (partial file) ─────────────────────────────
                        case "cursor_position":
                            HandleCursorPosition(json);
                            break;

                        case "cursor_image":
                            HandleCursorImage(json);
                            break;

                        // ── Ping / pong ───────────────────────────────────────
                        case "ping":
                            _ = SendTextAsync("{\"type\":\"pong\"}");
                            break;

                        case "pong":
                            HandlePongMessage(text);
                            _speedTest?.HandlePong();
                            break;

                        // ── Metrics (partial file) ────────────────────────────
                        case "frameTiming":
                            HandleFrameTiming(json);
                            break;

                        // ── Error / reconnect ─────────────────────────────────
                        case "error":
                            HandleError(json);
                            break;

                        case "reconnect_required":
                            Debug.LogWarning("[PhaseProtocol] Server requested reconnect");
                            _ = ReconnectSessionAsync();
                            break;

                        case null:
                        case "":
                            Debug.LogWarning($"[PhaseProtocol] Empty/null type! Raw: {text.Substring(0, Math.Min(300, text.Length))}");
                            break;

                        default:
                            Debug.LogWarning($"[PhaseProtocol] Unknown message type: '{type}'");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PhaseProtocol] JSON parse failed: {ex.Message}");
                }
            }
            else if (text.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                _ = SendTextAsync("pong");
            }
            else if (text.Equals("pong", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("pong:", StringComparison.OrdinalIgnoreCase))
            {
                HandlePongMessage(text);
                _speedTest?.HandlePong();
            }
            else
            {
                await HandleLegacyMessageAsync(text);
            }
        }

        // ── Error handler ─────────────────────────────────────────────────────

        private void HandleError(SimpleJson json)
        {
            var phase   = json.GetInt("phase");
            var code    = json.GetString("code")    ?? "UNKNOWN";
            var message = json.GetString("message") ?? "Unknown error";

            Debug.LogError($"[PhaseProtocol] Error in Phase {phase}: [{code}] {message}");
            _stateMachine.ForceTransition(ConnectionPhase.Error, message);
            OnError?.Invoke(message);
        }

        // ── Legacy message handler ─────────────────────────────────────────────

        private async Task HandleLegacyMessageAsync(string text)
        {
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

            if (text.StartsWith("answer:", StringComparison.OrdinalIgnoreCase))
            {
                var rest     = text.Substring(7);
                var colonIdx = rest.IndexOf(':');
                if (colonIdx > 0 && int.TryParse(rest.Substring(0, colonIdx), out int monIdx))
                {
                    var sdp = rest.Substring(colonIdx + 1);
                    await HandleAnswerAsync(new SimpleJson(new Dictionary<string, object>
                    {
                        { "monitorIndex", monIdx },
                        { "sdp",          sdp    }
                    }));
                }
            }
            else if (text.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
            {
                var rest     = text.Substring(10);
                var colonIdx = rest.IndexOf(':');
                if (colonIdx > 0 && int.TryParse(rest.Substring(0, colonIdx), out int monIdx))
                {
                    var candStr = rest.Substring(colonIdx + 1);
                    HandleCandidate(new SimpleJson(new Dictionary<string, object>
                    {
                        { "monitorIndex", monIdx    },
                        { "candidate",    candStr   }
                    }));
                }
            }
        }

        // ── Keepalive loop ─────────────────────────────────────────────────────

        private async Task KeepaliveLoopAsync(CancellationToken ct)
        {
            const int PING_INTERVAL_MS = 2000;
            const int PONG_TIMEOUT_MS  = 6000;
            const int MAX_MISSED_PONGS = 3;

            _lastPongReceivedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await Task.Delay(1000, ct);

            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var now              = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var timeSinceLastPong = now - _lastPongReceivedTime;

                    if (_lastPongReceivedTime > 0 && timeSinceLastPong > PONG_TIMEOUT_MS)
                    {
                        _missedPongCount++;
                        _metrics.RecordMissedPong();
                        Debug.LogWarning($"[PhaseProtocol] Pong timeout! {timeSinceLastPong}ms, missed={_missedPongCount}");

                        if (_missedPongCount >= MAX_MISSED_PONGS)
                        {
                            Debug.LogError("[PhaseProtocol] Server not responding – triggering reconnect");
                            OnConnectionHealthCritical?.Invoke();

                            List<PCWrapper> wrappers;
                            lock (_lock) { wrappers = _peerConnections.ToList(); }
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

        // ── Core send (used by handlers and partial files via delegate) ─────────

        private async Task SendTextAsync(string text)
        {
            if (_ws?.State != WebSocketState.Open) return;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await _ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Send error: {ex.Message}");
            }
        }

        // ── JSON utilities (used by partial files) ─────────────────────────────

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        // ── SDP fix (used by WebRTC partial) ───────────────────────────────────

        private string FixSdp(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return sdp;

            sdp = sdp.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = sdp.Split('\n');

            var processed  = new List<string>();
            int removedCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var trimmed = line.TrimEnd();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("a=candidate:"))
                {
                    removedCount++;
                    continue;
                }

                string fixedLine = trimmed;
                if (trimmed.StartsWith("m=") &&
                    trimmed.Contains("UDP/TLS/RTP/SAVP") &&
                    !trimmed.Contains("UDP/TLS/RTP/SAVPF"))
                {
                    fixedLine = trimmed.Replace("UDP/TLS/RTP/SAVP", "UDP/TLS/RTP/SAVPF");
                }

                processed.Add(fixedLine);
            }

            if (removedCount > 0)
                Debug.Log($"[PhaseProtocol] FixSdp: Removed {removedCount} embedded ICE candidates");

            var result = string.Join("\r\n", processed);
            if (!result.EndsWith("\r\n")) result += "\r\n";

            Debug.Log($"[PhaseProtocol] FixSdp: {processed.Count} lines, {result.Length} chars");
            return result;
        }

        // ── Cleanup / dispose ─────────────────────────────────────────────────

        private void Cleanup()
        {
            try { _cts?.Cancel(); } catch { }

            lock (_lock)
            {
                _pcGeneration++; // Invalidate stale PC callbacks before disposing
                foreach (var w in _peerConnections)
                {
                    try { w.PC?.Close(); w.PC?.Dispose(); } catch { }
                }
                _peerConnections.Clear();
                _expectedMonitorCount = 0;
            }

            _sctpInitChannel = null;
            _streamingStartedFired = false;
            _streamingStartTime    = DateTime.MinValue;
            _isStreamingPaused     = false;

            _metrics.ResetAll();

            try { _ws?.Abort(); _ws?.Dispose(); } catch { }
            _ws = null;
        }

        public void Dispose() => Cleanup();
    }
}
