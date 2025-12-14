using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Multi-track WebRTC stream client that receives N separate video tracks (one per monitor).
/// This solves Android MediaCodec compatibility issues with ultra-wide resolutions.
/// Each monitor gets its own 1920x1080 stream in a single PeerConnection.
/// </summary>
[DisallowMultipleComponent]
public class MultiTrackStreamClient : MonoBehaviour
{
    [Header("Signal")]
    [Tooltip("Signal URL with mode=multitrack. Example: ws://192.168.1.9:8288/signal?mode=multitrack&monitors=2&resW=1920&resH=1080&kbps=4000&fps=30")]
    public string signalUrl = "ws://192.168.1.9:8288/signal?mode=multitrack&monitors=2&resW=1920&resH=1080&kbps=4000&fps=30&relaxed=1";

    [Header("Panels")]
    [Tooltip("WorldPanelPlus instances to bind video tracks to (one per monitor)")]
    public WorldPanelPlus[] panels;

    [Header("Fallback")]
    [Tooltip("RawImage fallback for each track (optional)")]
    public RawImage[] fallbackRawImages;

    [Header("ICE Settings")]
    public bool skipTcpIceCandidates = true;
    public bool autoDetectLAN = true;

    // WebRTC
    private RTCPeerConnection _pc;
    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private RTCPeerConnectionState _lastPcState = RTCPeerConnectionState.New;
    private readonly List<string> _pendingLocalCands = new();

    // Multi-track video handling
    private readonly List<VideoTrackInfo> _videoTracks = new();
    
    private class VideoTrackInfo
    {
        public int Index;
        public VideoStreamTrack Track;
        public Texture Texture;
        public int FrameCount;
        public OnVideoReceived Callback;
    }

    /// <summary>
    /// Event fired when a new video track is received.
    /// Parameters: trackIndex, VideoStreamTrack
    /// </summary>
    public event Action<int, VideoStreamTrack> OnTrackReceived;

    /// <summary>
    /// Get texture for a specific track index
    /// </summary>
    public Texture GetTexture(int trackIndex)
    {
        if (trackIndex >= 0 && trackIndex < _videoTracks.Count)
            return _videoTracks[trackIndex].Texture;
        return null;
    }

    /// <summary>
    /// Number of video tracks currently received
    /// </summary>
    public int TrackCount => _videoTracks.Count;

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
                    Debug.Log($"[MultiTrackClient] LAN detected ({uri.Host}), adding lan=1");
                }
            }
            catch { }
        }
        return url;
    }

    static string FixSdp(string sdp)
    {
        if (string.IsNullOrEmpty(sdp)) return sdp;

        // Fix SAVP -> SAVPF
        const string needle = "UDP/TLS/RTP/SAVP";
        int i = 0;
        while (true)
        {
            int idx = sdp.IndexOf(needle, i, StringComparison.Ordinal);
            if (idx < 0) break;
            int after = idx + needle.Length;
            if (after < sdp.Length && sdp[after] == 'F') { i = after + 1; continue; }
            sdp = sdp.Substring(0, after) + "F" + sdp.Substring(after);
            i = after + 1;
        }

        // Fix 0.0.0.0
        if (sdp.Contains("IP4 0.0.0.0"))
            sdp = sdp.Replace("IP4 0.0.0.0", "IP4 127.0.0.1");

        // Strip embedded candidates
        var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var filtered = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("a=ice-options:", StringComparison.OrdinalIgnoreCase) && line.Contains("ice2"))
                line = line.Replace("ice2,", "").Replace(",ice2", "").Replace("ice2", "trickle");
            filtered.Add(line);
        }
        sdp = string.Join("\r\n", filtered);
        if (!sdp.EndsWith("\r\n")) sdp += "\r\n";
        return sdp;
    }

    async void Start()
    {
        StartCoroutine(WebRTC.Update());
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        _cts = new CancellationTokenSource();

        var cfg = new RTCConfiguration
        {
            iceServers = Array.Empty<RTCIceServer>(),
            iceCandidatePoolSize = 0,
            iceTransportPolicy = RTCIceTransportPolicy.All
        };

        _pc = new RTCPeerConnection(ref cfg);
        
        _pc.OnIceConnectionChange = s => Debug.Log($"[MultiTrackClient] ICE: {s}");
        _pc.OnConnectionStateChange = s => { _lastPcState = s; Debug.Log($"[MultiTrackClient] PC: {s}"); };

        _pc.OnIceCandidate = cand =>
        {
            string msg = string.IsNullOrEmpty(cand.Candidate) ? "end-of-candidates" : cand.Candidate;
            if (!msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) && !msg.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase))
                msg = "candidate:" + msg;
            if (msg.StartsWith("candidate:candidate:", StringComparison.OrdinalIgnoreCase))
                msg = msg.Substring("candidate:".Length);
            
            // Skip TCP
            if (skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                return;

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try { _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, _cts.Token); }
                catch { _pendingLocalCands.Add(msg); }
            }
            else
            {
                _pendingLocalCands.Add(msg);
            }
        };

        // Multi-track: Add multiple receive-only video transceivers
        int expectedTracks = GetExpectedTrackCount();
        for (int t = 0; t < expectedTracks; t++)
        {
            var trans = _pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
            var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
            var h264 = caps.codecs.Where(c => (c.mimeType ?? "").IndexOf("H264", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            trans.SetCodecPreferences(h264.Concat(caps.codecs.Except(h264)).ToArray());
            Debug.Log($"[MultiTrackClient] Added transceiver {t} for video track");
        }

        // Handle incoming tracks
        _pc.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack v)
            {
                int trackIndex = _videoTracks.Count;
                var info = new VideoTrackInfo
                {
                    Index = trackIndex,
                    Track = v
                };
                info.Callback = tex => { info.Texture = tex; info.FrameCount++; };
                v.OnVideoReceived += info.Callback;
                _videoTracks.Add(info);

                Debug.Log($"[MultiTrackClient] Received video track {trackIndex}: {v.Id}");
                OnTrackReceived?.Invoke(trackIndex, v);
            }
        };

        await ConnectAndSignalOffer();
    }

    int GetExpectedTrackCount()
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

    void Update()
    {
        // Poll textures and apply to panels
        for (int i = 0; i < _videoTracks.Count && _lastPcState == RTCPeerConnectionState.Connected; i++)
        {
            var info = _videoTracks[i];
            
            // Poll texture directly (Android workaround)
            try
            {
                var tex = info.Track?.Texture;
                if (tex != null && tex.width > 0 && tex.height > 0)
                    info.Texture = tex;
            }
            catch { }

            if (info.Texture == null) continue;

            // Apply to panel
            if (panels != null && i < panels.Length && panels[i] != null)
            {
                panels[i].contentTexture = info.Texture;
                panels[i].Apply();
            }

            // Apply to fallback RawImage
            if (fallbackRawImages != null && i < fallbackRawImages.Length && fallbackRawImages[i] != null)
            {
                fallbackRawImages[i].texture = info.Texture;
            }
        }
    }

    async Task ConnectAndSignalOffer()
    {
        string url = BuildOptimizedSignalUrl();
        Debug.Log($"[MultiTrackClient] Connecting to {url}");

        try
        {
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await _ws.ConnectAsync(new Uri(url), _cts.Token);
            Debug.Log($"[MultiTrackClient] WebSocket connected");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MultiTrackClient] WS connect failed: {ex.Message}");
            return;
        }

        // Create offer
        var offerOp = _pc.CreateOffer();
        while (!offerOp.IsDone) await Task.Yield();
        if (offerOp.IsError) { Debug.LogError($"[MultiTrackClient] CreateOffer failed: {offerOp.Error.message}"); return; }

        var offer = offerOp.Desc;
        Debug.Log($"[MultiTrackClient] Offer created, length={offer.sdp.Length}");

        var setLocalOp = _pc.SetLocalDescription(ref offer);
        while (!setLocalOp.IsDone) await Task.Yield();
        if (setLocalOp.IsError) { Debug.LogError($"[MultiTrackClient] SetLocal failed: {setLocalOp.Error.message}"); return; }

        await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("offer:" + offer.sdp)), WebSocketMessageType.Text, true, _cts.Token);
        Debug.Log("[MultiTrackClient] Offer sent");

        // Send queued ICE
        foreach (var c in _pendingLocalCands)
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(c)), WebSocketMessageType.Text, true, _cts.Token);
        _pendingLocalCands.Clear();

        // Start ping keepalive
        StartCoroutine(PingKeepalive());

        // Receive loop
        var buf = new byte[256 * 1024];
        var pendingRemoteCands = new List<string>();
        bool answerSet = false;

        while (true)
        {
            var ms = new System.IO.MemoryStream();
            while (true)
            {
                var res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                if (res.MessageType == WebSocketMessageType.Close) { Debug.Log("[MultiTrackClient] WS closed"); return; }
                ms.Write(buf, 0, res.Count);
                if (res.EndOfMessage) break;
            }
            var text = Encoding.UTF8.GetString(ms.ToArray());
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (text.StartsWith("answer:", StringComparison.OrdinalIgnoreCase))
            {
                var sdp = FixSdp(text.Substring("answer:".Length).Trim());
                Debug.Log($"[MultiTrackClient] Received answer, length={sdp.Length}");
                
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var setRemoteOp = _pc.SetRemoteDescription(ref answer);
                
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!setRemoteOp.IsDone && sw.ElapsedMilliseconds < 5000)
                    await Task.Delay(10);
                
                if (!setRemoteOp.IsDone || setRemoteOp.IsError)
                {
                    Debug.LogError($"[MultiTrackClient] SetRemote failed/timeout");
                    return;
                }

                Debug.Log($"[MultiTrackClient] Answer set, signaling={_pc.SignalingState}");
                answerSet = true;

                // Add pending candidates
                foreach (var cand in pendingRemoteCands)
                {
                    _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = cand, sdpMLineIndex = 0, sdpMid = "0" }));
                }
                pendingRemoteCands.Clear();
            }
            else if (text.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = text.Substring("candidate:".Length).Trim();
                if (raw.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
                    raw = raw.Substring("candidate:".Length).Trim();
                
                // Skip TCP
                if (skipTcpIceCandidates && (raw.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || raw.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var fullCand = "candidate:" + raw;
                if (answerSet)
                {
                    _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = fullCand, sdpMLineIndex = 0, sdpMid = "0" }));
                    Debug.Log($"[MultiTrackClient] Added remote ICE: {raw.Substring(0, Math.Min(50, raw.Length))}...");
                }
                else
                {
                    pendingRemoteCands.Add(fullCand);
                }
            }
            else if (text.Equals("pong", StringComparison.OrdinalIgnoreCase))
            {
                // Keepalive response
            }
        }
    }

    System.Collections.IEnumerator PingKeepalive()
    {
        yield return new WaitForSeconds(2.0f);
        while (_ws != null && _ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
        {
            try { _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping")), WebSocketMessageType.Text, true, _cts.Token); }
            catch { break; }
            yield return new WaitForSeconds(5.0f);
        }
    }

    void OnDisable() => Cleanup();
    void OnDestroy() => Cleanup();

    void Cleanup()
    {
        try { _cts?.Cancel(); } catch { }
        foreach (var info in _videoTracks)
        {
            try { info.Track.OnVideoReceived -= info.Callback; } catch { }
        }
        _videoTracks.Clear();
        try { _pc?.Close(); _pc?.Dispose(); } catch { }
        try { _ws?.Abort(); _ws?.Dispose(); } catch { }
    }
}
