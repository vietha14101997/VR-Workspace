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

            // ICE candidates - send with index prefix
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate)) return;
                string msg = cand.Candidate;
                if (skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                    return;
                if (!msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
                    msg = "candidate:" + msg;
                SendWs($"candidate:{idx}:{msg.Substring(10)}"); // Strip "candidate:" prefix, server will add it
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
        Debug.Log($"[MultiPC] Connecting to {signalUrl}");
        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await _ws.ConnectAsync(new Uri(signalUrl), _cts.Token);
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
                    if (skipTcpIceCandidates && (candStr.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || candStr.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    
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
            var fullCand = candStr.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) ? candStr : "candidate:" + candStr;
            wrapper.PC.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = fullCand, sdpMLineIndex = 0, sdpMid = "0" }));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiPC] PC{wrapper.Index} AddICE error: {ex.Message}");
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
        sdp = sdp.Replace("UDP/TLS/RTP/SAVP", "UDP/TLS/RTP/SAVPF");
        if (sdp.Contains("IP4 0.0.0.0")) sdp = sdp.Replace("IP4 0.0.0.0", "IP4 127.0.0.1");
        var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(l => !l.TrimStart().StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase));
        return string.Join("\r\n", lines) + "\r\n";
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
