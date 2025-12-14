using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;

/// <summary>
/// Multi-PC WebRTC stream client: Creates N PeerConnections (one per monitor).
/// Uses multiplexed signaling over single WebSocket:
/// - offer:N:sdp, answer:N:sdp, candidate:N:candidate
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

    public int MonitorCount => _pcs.Count;
    public Texture GetTexture(int index) => index >= 0 && index < _pcs.Count ? _pcs[index].Texture : null;

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
        StartCoroutine(WebRTC.Update());
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        _cts = new CancellationTokenSource();

        int monitors = GetExpectedMonitors();
        Debug.Log($"[MultiPC] Creating {monitors} PeerConnections");

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
            pc.OnConnectionStateChange = s => Debug.Log($"[MultiPC] PC{idx} State: {s}");

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
                }
            };
        }

        await ConnectAndSignal();
    }

    void Update()
    {
        // Poll textures and apply to panels
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
        }
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
        try { _cts?.Cancel(); } catch { }
        foreach (var w in _pcs)
        {
            try { w.PC?.Close(); w.PC?.Dispose(); } catch { }
        }
        _pcs.Clear();
        try { _ws?.Abort(); _ws?.Dispose(); } catch { }
    }
}
