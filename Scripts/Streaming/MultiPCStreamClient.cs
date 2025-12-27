using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Streaming;

/// <summary>
/// Multi-PC WebRTC stream client: Creates N PeerConnections (one per monitor).
/// Uses multiplexed signaling over single WebSocket:
/// - offer:N:sdp, answer:N:sdp, candidate:N:candidate
///
/// Supports two protocols:
/// - V1 (legacy): Direct WebRTC signaling
/// - V2 (new): 3-phase connection with hardware info, speed test, and suggested config
/// </summary>
[DisallowMultipleComponent]
public class MultiPCStreamClient : MonoBehaviour
{
    [Header("Signal")]
    public string signalUrl = "ws://192.168.1.9:8288/signal?mode=multitrack&monitors=2&resW=1920&resH=1080&kbps=4000&fps=30";

    [Header("Panels")]
    public WorldPanelPlus[] panels;

    [Header("ICE")]
    public bool skipTcpIceCandidates = true;

    [Header("LAN Optimization")]
    [Tooltip("Auto-detect LAN connection and add &lan=1 parameter")]
    public bool autoDetectLAN = true;

    [Header("Protocol")]
    [Tooltip("Use V2 protocol with 3-phase connection (hardware info, speed test, suggested config)")]
    public bool useV2Protocol = false;

    [Tooltip("Auto-accept suggested config in V2 mode (skip user review)")]
    public bool autoAcceptSuggestedConfig = true;

    [Tooltip("If true, automatically start connection in Start(). Set to false for manual control via ConnectV2Async().")]
    public bool autoStartConnection = true;

    // V2 Protocol client
    private PhaseProtocolClient _v2Client;

    // V2 Protocol events (for external UI integration)
    public event Action<ServerHardwareInfo> OnHardwareInfoReceived;
    public event Action<NetworkTestResult> OnNetworkInfoReceived;
    public event Action<SuggestedStreamConfig> OnSuggestedConfigReceived;
    public event Action<string, int, string> OnConfigProgress;
    public event Action<string> OnConnectionError;
    public event Action OnStreamingStarted;

    // V2 Protocol state (read-only)
    public ConnectionStateMachine StateMachine => _v2Client?.StateMachine;
    public ServerHardwareInfo HardwareInfo => _v2Client?.HardwareInfo;
    public NetworkTestResult NetworkInfo => _v2Client?.NetworkInfo;
    public SuggestedStreamConfig SuggestedConfig => _v2Client?.SuggestedConfig;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private readonly List<PCWrapper> _pcs = new();
    
    private class PCWrapper
    {
        public int Index;
        public RTCPeerConnection PC;
        public VideoStreamTrack VideoTrack;
        public Texture Texture;
        public bool AnswerSet;
        public List<string> PendingIce = new();
    }

    public int MonitorCount => useV2Protocol ? (_v2Client?.MonitorCount ?? 0) : _pcs.Count;
    public Texture GetTexture(int index)
    {
        if (useV2Protocol)
            return _v2Client?.GetTexture(index);
        return index >= 0 && index < _pcs.Count ? _pcs[index].Texture : null;
    }

    /// <summary>
    /// Check if currently streaming (V2 only).
    /// </summary>
    public bool IsStreaming => _v2Client?.IsStreaming ?? false;

    /// <summary>
    /// Check if connected (V2 only).
    /// </summary>
    public bool IsConnected => useV2Protocol ? (_v2Client?.IsConnected ?? false) : (_ws?.State == WebSocketState.Open);

    static bool IsPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(host.Trim(), @"^(\d+)\.(\d+)\.(\d+)\.(\d+)$"))
        {
            var parts = host.Trim().Split('.');
            if (parts.Length == 4 && int.TryParse(parts[0], out int first) && int.TryParse(parts[1], out int second))
            {
                if (first == 10) return true;
                if (first == 192 && second == 168) return true;
                if (first == 172 && second >= 16 && second <= 31) return true;
            }
        }
        return false;
    }

    string BuildOptimizedSignalUrl()
    {
        string url = signalUrl;
        if (autoDetectLAN && !string.IsNullOrEmpty(url))
        {
            try
            {
                var uri = new Uri(url);
                if (IsPrivateHost(uri.Host) && !url.Contains("lan="))
                {
                    url += url.Contains("?") ? "&lan=1" : "?lan=1";
                    Debug.Log($"[MultiPC] LAN optimization: added lan=1 for {uri.Host}");
                }
            }
            catch { }
        }
        return url;
    }

    int GetExpectedMonitors()
    {
        try
        {
            var uri = new Uri(signalUrl);
            var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (int.TryParse(qs.Get("monitors"), out int m)) return Math.Max(1, m);
        }
        catch { }
        return panels?.Length > 0 ? panels.Length : 2;
    }

    async void Start()
    {
        Application.runInBackground = true; // Prevent throttling when not focused (critical for same-machine testing)
        StartCoroutine(WebRTC.Update());
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120; // Cap at 120 to prevent GPU starvation (vs unlimited) on single-device setup
        _cts = new CancellationTokenSource();

        // V2 Protocol: Wait for manual connection if autoStartConnection is false
        if (useV2Protocol)
        {
            if (!autoStartConnection)
            {
                Debug.Log("[MultiPC] V2 protocol - waiting for manual ConnectV2Async() call");
                return;
            }
            Debug.Log("[MultiPC] Using V2 protocol (3-phase connection)");
            await StartV2ProtocolAsync();
            return;
        }

        // V1 Protocol (legacy)
        int monitors = GetExpectedMonitors();
        Debug.Log($"[MultiPC] Using V1 protocol, creating {monitors} PeerConnections");

        // Create N PeerConnections
        for (int i = 0; i < monitors; i++)
        {
            var cfg = new RTCConfiguration { iceServers = Array.Empty<RTCIceServer>() };
            var pc = new RTCPeerConnection(ref cfg);
            var wrapper = new PCWrapper { Index = i, PC = pc };
            _pcs.Add(wrapper);

            int idx = i; // Capture for closures

            // Add video transceiver
            var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
            var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
            var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
            trans.SetCodecPreferences(h264.Concat(caps.codecs.Except(h264)).ToArray());

            pc.OnIceConnectionChange = s => Debug.Log($"[MultiPC] PC{idx} ICE: {s}");
            pc.OnConnectionStateChange = s => 
            {
                Debug.Log($"[MultiPC] PC{idx} State: {s}");
                
                // Stop if we are shutting down intentionally
                if (_cts.IsCancellationRequested) return;

                if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    Debug.Log($"[MultiPC] PC{idx} connection lost ({s}). Triggering client-side auto-reconnect...");
                    _ = ReconnectMonitor(idx);
                }
            };

            // ICE candidates - send with index prefix (matching PCStreamClient format)
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate)) 
                {
                    Debug.Log($"[MultiPC] PC{idx} ICE gathering complete");
                    return;
                }
                
                string msg = cand.Candidate;
                
                // Skip TCP candidates (matching browser behavior)
                if (skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.Log($"[MultiPC] PC{idx} Skipped TCP candidate");
                    return;
                }
                
                // Normalize candidate format
                if (!msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
                    msg = "candidate:" + msg;
                
                // Handle double-prefix edge case
                if (msg.StartsWith("candidate:candidate:", StringComparison.OrdinalIgnoreCase))
                    msg = msg.Substring("candidate:".Length);
                
                // Extract raw candidate (without "candidate:" prefix)
                string rawCandidate = msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) 
                    ? msg.Substring("candidate:".Length) 
                    : msg;
                
                // Send format: candidate:{monitorIndex}:{rawCandidate}
                string toSend = $"candidate:{idx}:{rawCandidate}";
                Debug.Log($"[MultiPC] PC{idx} Sending ICE: {toSend.Substring(0, Math.Min(80, toSend.Length))}...");
                SendWs(toSend);
            };

            // Track received
            pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                {
                    wrapper.VideoTrack = v;
                    v.OnVideoReceived += tex => wrapper.Texture = tex;
                    Debug.Log($"[MultiPC] PC{idx} received video track");

                    // Jitter Buffer Control for Low Latency
                    // Find the receiver for this track and set delay hint
                    foreach (var receiver in pc.GetReceivers())
                    {
                        if (receiver.Track != null && receiver.Track.Id == v.Id)
                        {
                            // Error CS1061: JitterBufferDelayHint not available in this Unity WebRTC version.
                            // receiver.JitterBufferDelayHint = 0.05; // 50ms target delay
                            // Debug.Log($"[MultiPC] PC{idx} Set JitterBufferDelayHint=0.05s for track {v.Id}");
                            break;
                        }
                    }
                }
            };
        }

        await ConnectAndSignal();
    }

    void Update()
    {
        if (useV2Protocol)
        {
            UpdateV2();
            return;
        }

        // V1 Protocol: Poll textures and apply to panels
        for (int i = 0; i < _pcs.Count; i++)
        {
            var wrapper = _pcs[i];

            // Poll texture directly (Android workaround)
            try
            {
                var tex = wrapper.VideoTrack?.Texture;
                if (tex != null && tex.width > 0) wrapper.Texture = tex;
            }
            catch { }

            if (wrapper.Texture == null) continue;

            // Apply to panel
            if (panels != null && i < panels.Length && panels[i] != null)
            {
                panels[i].contentTexture = wrapper.Texture;
                panels[i].Apply();
            }
        }
    }

    void UpdateV2()
    {
        if (_v2Client == null) return;

        // Poll textures
        _v2Client.PollTextures();

        // Apply textures to panels
        int count = _v2Client.MonitorCount;
        for (int i = 0; i < count; i++)
        {
            var tex = _v2Client.GetTexture(i);
            if (tex == null) continue;

            if (panels != null && i < panels.Length && panels[i] != null)
            {
                panels[i].contentTexture = tex;
                panels[i].Apply();
            }
        }
    }

    /// <summary>
    /// Connect using V2 protocol (for manual connection control).
    /// Call this after setting signalUrl and subscribing to events when autoStartConnection is false.
    /// </summary>
    public async Task ConnectV2Async()
    {
        if (_v2Client != null && _v2Client.IsConnected)
        {
            Debug.LogWarning("[MultiPC-V2] Already connected");
            return;
        }

        Debug.Log("[MultiPC] Manually starting V2 protocol connection");
        await StartV2ProtocolAsync();
    }

    /// <summary>
    /// Start V2 protocol connection flow.
    /// </summary>
    async Task StartV2ProtocolAsync()
    {
        _v2Client = new PhaseProtocolClient();

        // Subscribe to events
        _v2Client.OnHardwareInfoReceived += hw =>
        {
            Debug.Log($"[MultiPC-V2] Hardware: {hw.deviceName}, GPU: {hw.gpu} ({hw.gpuVramGB}GB)");
            OnHardwareInfoReceived?.Invoke(hw);
        };

        _v2Client.OnNetworkInfoReceived += net =>
        {
            Debug.Log($"[MultiPC-V2] Network: {net.connectionType}, Ping: {net.pingMs:F1}ms, BW: {net.bandwidthMbps:F0}Mbps");
            OnNetworkInfoReceived?.Invoke(net);
        };

        _v2Client.OnSuggestedConfigReceived += cfg =>
        {
            Debug.Log($"[MultiPC-V2] Suggested: {cfg.monitors}mon @ {cfg.resolutionWidth}x{cfg.resolutionHeight}, {cfg.fps}fps");
            OnSuggestedConfigReceived?.Invoke(cfg);

            // Auto-accept if enabled
            if (autoAcceptSuggestedConfig)
            {
                Debug.Log("[MultiPC-V2] Auto-accepting suggested config");
                _ = AcceptSuggestedConfigAsync();
            }
        };

        _v2Client.OnConfigProgress += (step, progress, message) =>
        {
            Debug.Log($"[MultiPC-V2] Config: {step} {progress}% - {message}");
            OnConfigProgress?.Invoke(step, progress, message);
        };

        _v2Client.OnVideoTextureReceived += (idx, tex) =>
        {
            Debug.Log($"[MultiPC-V2] Monitor {idx} texture received: {tex.width}x{tex.height}");
        };

        _v2Client.OnStreamingStarted += () =>
        {
            Debug.Log("[MultiPC-V2] Streaming started!");
            OnStreamingStarted?.Invoke();
        };

        _v2Client.OnError += err =>
        {
            Debug.LogError($"[MultiPC-V2] Error: {err}");
            OnConnectionError?.Invoke(err);
        };

        _v2Client.OnDisconnected += () =>
        {
            Debug.Log("[MultiPC-V2] Disconnected");
        };

        // Connect
        string url = BuildOptimizedSignalUrl();
        await _v2Client.ConnectAsync(url);
    }

    /// <summary>
    /// Accept suggested config and proceed to Phase 2 (V2 protocol).
    /// </summary>
    public async Task AcceptSuggestedConfigAsync()
    {
        if (_v2Client == null || _v2Client.SuggestedConfig == null)
        {
            Debug.LogWarning("[MultiPC-V2] No suggested config available");
            return;
        }

        var config = StreamingConfig.FromSuggested(_v2Client.SuggestedConfig);
        await _v2Client.ProceedToPhase2Async(config);

        // Wait for ICE to complete and start streaming
        while (_v2Client.StateMachine.CurrentPhase != ConnectionPhase.ReadyToStream &&
               _v2Client.StateMachine.CurrentPhase != ConnectionPhase.Error)
        {
            await Task.Delay(100);
        }

        if (_v2Client.StateMachine.CurrentPhase == ConnectionPhase.ReadyToStream)
        {
            await _v2Client.StartStreamingAsync();
        }
    }

    /// <summary>
    /// Apply custom config and proceed to Phase 2 (V2 protocol).
    /// </summary>
    public async Task ApplyConfigAsync(StreamingConfig config)
    {
        Debug.Log("[MultiPC-V2] ApplyConfigAsync called");

        if (_v2Client == null)
        {
            Debug.LogWarning("[MultiPC-V2] Not connected (_v2Client is null)");
            return;
        }

        Debug.Log("[MultiPC-V2] Calling _v2Client.ProceedToPhase2Async...");
        await _v2Client.ProceedToPhase2Async(config);
        Debug.Log("[MultiPC-V2] ProceedToPhase2Async returned");
    }

    /// <summary>
    /// Start streaming after ICE is complete (V2 protocol).
    /// </summary>
    public async Task StartStreamingV2Async()
    {
        if (_v2Client == null)
        {
            Debug.LogWarning("[MultiPC-V2] Not connected");
            return;
        }

        await _v2Client.StartStreamingAsync();
    }

    /// <summary>
    /// Stop V2 connection.
    /// </summary>
    public async Task StopV2Async()
    {
        if (_v2Client != null)
        {
            await _v2Client.StopAsync();
            _v2Client.Dispose();
            _v2Client = null;
        }
    }

    async Task ConnectAndSignal()
    {
        string url = BuildOptimizedSignalUrl();
        Debug.Log($"[MultiPC] Connecting to {url}");
        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await _ws.ConnectAsync(new Uri(url), _cts.Token);
        Debug.Log("[MultiPC] WebSocket connected");

        // Send offers for all PCs
        foreach (var wrapper in _pcs)
        {
            var offerOp = wrapper.PC.CreateOffer();
            while (!offerOp.IsDone) await Task.Yield();
            if (offerOp.IsError) { Debug.LogError($"[MultiPC] PC{wrapper.Index} CreateOffer failed"); continue; }

            var offer = offerOp.Desc;
            var setLocalOp = wrapper.PC.SetLocalDescription(ref offer);
            while (!setLocalOp.IsDone) await Task.Yield();
            if (setLocalOp.IsError) { Debug.LogError($"[MultiPC] PC{wrapper.Index} SetLocal failed"); continue; }

            SendWs($"offer:{wrapper.Index}:{offer.sdp}");
            Debug.Log($"[MultiPC] Sent offer for PC{wrapper.Index}");
        }

        // Start ping keepalive
        StartCoroutine(PingKeepalive());

        // RX loop
        var buf = new byte[256 * 1024];
        while (_ws.State == WebSocketState.Open)
        {
            var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult res;
            do
            {
                res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                if (res.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buf, 0, res.Count);
            } while (!res.EndOfMessage);

            var text = Encoding.UTF8.GetString(ms.ToArray());
            if (string.IsNullOrWhiteSpace(text)) continue;

            // answer:N:sdp
            if (text.StartsWith("answer:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = text.Substring(7);
                var colonIdx = rest.IndexOf(':');
                if (colonIdx > 0 && int.TryParse(rest.Substring(0, colonIdx), out int monIdx))
                {
                    var sdp = FixSdp(rest.Substring(colonIdx + 1));
                    if (monIdx >= 0 && monIdx < _pcs.Count)
                    {
                        var wrapper = _pcs[monIdx];
                        var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                        var setRemoteOp = wrapper.PC.SetRemoteDescription(ref answer);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (!setRemoteOp.IsDone && sw.ElapsedMilliseconds < 5000) await Task.Delay(10);
                        
                        if (!setRemoteOp.IsDone || setRemoteOp.IsError)
                        {
                            Debug.LogError($"[MultiPC] PC{monIdx} SetRemote failed");
                            continue;
                        }
                        
                        wrapper.AnswerSet = true;
                        Debug.Log($"[MultiPC] PC{monIdx} answer set");

                        // Process pending ICE
                        foreach (var cand in wrapper.PendingIce)
                            AddIce(wrapper, cand);
                        wrapper.PendingIce.Clear();
                    }
                }
                continue;
            }

            // candidate:N:candidate
            if (text.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = text.Substring(10);
                var colonIdx = rest.IndexOf(':');
                if (colonIdx > 0 && int.TryParse(rest.Substring(0, colonIdx), out int monIdx))
                {
                    var candStr = rest.Substring(colonIdx + 1);
                    
                    // Skip TCP candidates (matching browser behavior)
                    if (skipTcpIceCandidates && (candStr.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || candStr.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                    {
                        Debug.Log($"[MultiPC] PC{monIdx} Skipped remote TCP candidate");
                        continue;
                    }
                    
                    // Log received candidate
                    bool isHost = candStr.Contains(" typ host ", StringComparison.OrdinalIgnoreCase);
                    Debug.Log($"[MultiPC] PC{monIdx} Received {(isHost ? "HOST" : "SRFLX")} ICE: {candStr.Substring(0, Math.Min(60, candStr.Length))}...");
                    
                    if (monIdx >= 0 && monIdx < _pcs.Count)
                    {
                        var wrapper = _pcs[monIdx];
                        if (wrapper.AnswerSet)
                            AddIce(wrapper, candStr);
                        else
                            wrapper.PendingIce.Add(candStr);
                    }
                }
                continue;
            }

            if (text.Equals("pong", StringComparison.OrdinalIgnoreCase)) continue;

            // reconnect:N - Server requests re-offer for monitor N (abnormal close recovery)
            if (text.StartsWith("reconnect:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = text.Substring(10);
                if (int.TryParse(rest.Trim(), out int monIdx) && monIdx >= 0 && monIdx < _pcs.Count)
                {
                    Debug.Log($"[MultiPC] Server requested reconnect for PC{monIdx}");
                    await ReconnectMonitor(monIdx);
                }
                continue;
            }
        }
    }

    /// <summary>
    /// Reconnect a specific monitor by recreating PeerConnection and sending new offer
    /// </summary>
    async Task ReconnectMonitor(int monitorIndex)
    {
        // Don't reconnect if application is quoting/disconnecting
        if (_cts == null || _cts.IsCancellationRequested) return;

        if (monitorIndex < 0 || monitorIndex >= _pcs.Count) return;
        
        var oldWrapper = _pcs[monitorIndex];
        Debug.Log($"[MultiPC] Reconnecting PC{monitorIndex}...");
        
        // Close old PC
        try { oldWrapper.PC?.Close(); oldWrapper.PC?.Dispose(); } catch { }
        
        // Create new PeerConnection
        var cfg = new RTCConfiguration { iceServers = Array.Empty<RTCIceServer>() };
        var pc = new RTCPeerConnection(ref cfg);
        var wrapper = new PCWrapper { Index = monitorIndex, PC = pc };
        
        int idx = monitorIndex;
        
        // Add video transceiver
        var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
        var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
        var h264 = caps.codecs.Where(c => (c.mimeType ?? "").Contains("H264", StringComparison.OrdinalIgnoreCase)).ToArray();
        trans.SetCodecPreferences(h264.Concat(caps.codecs.Except(h264)).ToArray());

        pc.OnIceConnectionChange = s => Debug.Log($"[MultiPC] PC{idx} ICE: {s}");
        pc.OnConnectionStateChange = s => Debug.Log($"[MultiPC] PC{idx} State: {s}");

        // ICE candidates
        pc.OnIceCandidate = cand =>
        {
            if (string.IsNullOrEmpty(cand.Candidate)) return;
            
            string msg = cand.Candidate;
            if (skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                return;
            
            if (!msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
                msg = "candidate:" + msg;
            if (msg.StartsWith("candidate:candidate:", StringComparison.OrdinalIgnoreCase))
                msg = msg.Substring("candidate:".Length);
            
            string rawCandidate = msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) 
                ? msg.Substring("candidate:".Length) : msg;
            
            SendWs($"candidate:{idx}:{rawCandidate}");
        };

        // Track received
        pc.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack v)
            {
                wrapper.VideoTrack = v;
                v.OnVideoReceived += tex => wrapper.Texture = tex;
                Debug.Log($"[MultiPC] PC{idx} received video track (reconnected)");
            }
        };
        
        // Replace wrapper
        _pcs[monitorIndex] = wrapper;
        
        // Create and send new offer
        var offerOp = pc.CreateOffer();
        while (!offerOp.IsDone) await Task.Yield();
        if (offerOp.IsError) { Debug.LogError($"[MultiPC] PC{idx} CreateOffer failed on reconnect"); return; }

        var offer = offerOp.Desc;
        var setLocalOp = pc.SetLocalDescription(ref offer);
        while (!setLocalOp.IsDone) await Task.Yield();
        if (setLocalOp.IsError) { Debug.LogError($"[MultiPC] PC{idx} SetLocal failed on reconnect"); return; }

        SendWs($"offer:{idx}:{offer.sdp}");
        Debug.Log($"[MultiPC] PC{idx} reconnect offer sent");
    }

    void AddIce(PCWrapper wrapper, string candStr)
    {
        try
        {
            // Normalize: remove double-prefix if present
            if (candStr.StartsWith("candidate:candidate:", StringComparison.OrdinalIgnoreCase))
                candStr = candStr.Substring("candidate:".Length);
            
            // Ensure proper format
            var fullCand = candStr.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) ? candStr : "candidate:" + candStr;
            
            wrapper.PC.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = fullCand, sdpMLineIndex = 0, sdpMid = "0" }));
            Debug.Log($"[MultiPC] PC{wrapper.Index} Added ICE candidate");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiPC] PC{wrapper.Index} AddICE error: {ex.Message}");
            
            // Fallback: try without prefix
            try
            {
                var rawCand = candStr.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) ? candStr.Substring("candidate:".Length) : candStr;
                wrapper.PC.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = rawCand, sdpMLineIndex = 0, sdpMid = "0" }));
                Debug.Log($"[MultiPC] PC{wrapper.Index} Added ICE candidate (fallback format)");
            }
            catch { }
        }
    }

    void SendWs(string msg)
    {
        if (_ws?.State == WebSocketState.Open)
        {
            try { _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, _cts.Token); }
            catch { }
        }
    }

    string FixSdp(string sdp)
    {
        if (string.IsNullOrEmpty(sdp)) return sdp;
        
        // 1. Fix SAVP -> SAVPF
        sdp = sdp.Replace("UDP/TLS/RTP/SAVP", "UDP/TLS/RTP/SAVPF");
        
        // 2. Fix 0.0.0.0 in connection line
        if (sdp.Contains("IP4 0.0.0.0")) 
        {
            Debug.Log("[MultiPC] Fixing SDP: IP4 0.0.0.0 -> IP4 127.0.0.1");
            sdp = sdp.Replace("IP4 0.0.0.0", "IP4 127.0.0.1");
        }
        
        // 3. Remove embedded candidates and fix ice-options
        var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var filtered = new List<string>();
        
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            // Skip embedded candidates
            if (line.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase))
                continue;
            
            // Fix ice-options: remove 'ice2' 
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

    System.Collections.IEnumerator PingKeepalive()
    {
        yield return new WaitForSeconds(2);
        while (_ws?.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
        {
            SendWs("ping");
            yield return new WaitForSeconds(5);
        }
    }

    void OnDisable() => Cleanup();
    void OnDestroy() => Cleanup();

    void Cleanup()
    {
        // V2 Protocol cleanup
        if (_v2Client != null)
        {
            try { _v2Client.Dispose(); } catch { }
            _v2Client = null;
        }

        // V1 Protocol cleanup
        try { _cts?.Cancel(); } catch { }
        foreach (var w in _pcs)
        {
            try { w.PC?.Close(); w.PC?.Dispose(); } catch { }
        }
        _pcs.Clear();
        try { _ws?.Abort(); _ws?.Dispose(); } catch { }
    }
}
