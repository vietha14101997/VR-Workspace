using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

public class PCStreamClient : MonoBehaviour
{
    public string signalUrl;
    public WorldPanelPlus worldPanel;
    public RenderTexture targetRT;       // nếu không cần, có thể bỏ và gán trực tiếp Texture
    public UnityEngine.UI.RawImage fallbackRawImage;

    private RTCPeerConnection _pc;
    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;

    private VideoStreamTrack _remoteVideoTrack;
    private Texture _remoteTexture;
    private OnVideoReceived _onVideoReceived;
    private readonly System.Collections.Generic.List<string> _pendingLocalCands = new();
    int _rxFrames = 0;

    async void Start()
    {
        StartCoroutine(WebRTC.Update());
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;

        _cts = new CancellationTokenSource();

        // (1) BẬT STUN Ở CLIENT
        var cfg = new RTCConfiguration
        {
            iceServers = new[]
            {
                new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
            }
        };
        _pc = new RTCPeerConnection(ref cfg);

        _pc.OnIceConnectionChange = s => Debug.Log("[PCStreamClient] ICE = " + s);
        _pc.OnConnectionStateChange = s => Debug.Log("[PCStreamClient] PC = " + s);

        // (2) TRICKLE OUT: gửi ICE candidate từ client lên server
        _pc.OnIceCandidate = cand =>
        {
            if (!string.IsNullOrEmpty(cand.Candidate))
                _ws?.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(cand.Candidate)),
                            WebSocketMessageType.Text, true, _cts.Token);
            else
                _ws?.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("end-of-candidates")),
                            WebSocketMessageType.Text, true, _cts.Token);
        };

        var trans = _pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });

        var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
        foreach (var c in caps.codecs)
            Debug.Log($"[PCStreamClient] cap codec: {c.mimeType}@{c.clockRate}");
        var h264 = caps.codecs.Where(c => c.mimeType != null && c.mimeType.IndexOf("H264", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        var others = caps.codecs.Where(c => h264.All(h => h.mimeType != c.mimeType || h.clockRate != c.clockRate)).ToArray();
        var prefer = h264.Concat(others).ToArray();

        trans.SetCodecPreferences(prefer);

        _pc.OnTrack = e =>
        {
            Debug.Log($"[PCStreamClient] OnTrack kind={e.Track.Kind}");
            if (e.Track is VideoStreamTrack v)
            {
                _remoteVideoTrack = v;
                _onVideoReceived = tex => { _remoteTexture = tex; _rxFrames++; };
                _remoteVideoTrack.OnVideoReceived += _onVideoReceived;
                Debug.Log("[PCStreamClient] Video track bound (OnVideoReceived subscribed)");
            }
        };

        if (string.IsNullOrEmpty(signalUrl))
            signalUrl = "ws://127.0.0.1:8288/signal";

        await ConnectAndSignalOffer();
    }

    void Update()
    {
        if (_remoteTexture != null)
        {
            if (worldPanel != null)
            {
                worldPanel.contentTexture = _remoteTexture;
                worldPanel.Apply();
            }
            if (fallbackRawImage != null)
                fallbackRawImage.texture = _remoteTexture;
        }
    }

    async Task ConnectAndSignalOffer()
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(signalUrl), _cts.Token);

        // Tạo offer + set local
        var offerOp = _pc.CreateOffer(); while (!offerOp.IsDone) await Task.Yield();
        if (offerOp.IsError) throw new Exception("CreateOffer failed: " + offerOp.Error.message);
        var offer = offerOp.Desc;

        var setLocalOp = _pc.SetLocalDescription(ref offer); while (!setLocalOp.IsDone) await Task.Yield();
        if (setLocalOp.IsError) throw new Exception("SetLocalDescription failed: " + setLocalOp.Error.message);

        // Gửi OFFER
        var msg = Encoding.UTF8.GetBytes("offer:" + offer.sdp);
        await _ws.SendAsync(new ArraySegment<byte>(msg), WebSocketMessageType.Text, true, _cts.Token);

        // bật cờ + xả các candidate đã queue
        foreach (var line in _pendingLocalCands)
        {
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(line)), WebSocketMessageType.Text, true, _cts.Token);
        }
        _pendingLocalCands.Clear();

        // VÒNG LẶP NHẬN WS: xử lý cả answer lẫn candidate
        var buf = new byte[256 * 1024];
        while (true)
        {
            var ms = new System.IO.MemoryStream();
            while (true)
            {
                var res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                if (res.MessageType == WebSocketMessageType.Close) throw new Exception("Signal closed.");
                ms.Write(buf, 0, res.Count);
                if (res.EndOfMessage) break;
            }
            var text = Encoding.UTF8.GetString(ms.ToArray());

            if (text.StartsWith("answer:", StringComparison.OrdinalIgnoreCase))
            {
                // Nhận answer và set remote
                var sdp = text.Substring("answer:".Length);
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var setRemoteOp2 = _pc.SetRemoteDescription(ref answer); while (!setRemoteOp2.IsDone) await Task.Yield();
                if (setRemoteOp2.IsError) throw new Exception("SetRemoteDescription failed: " + setRemoteOp2.Error.message);
                Debug.Log("[PCStreamClient] SetRemoteDescription(answer) OK");
                // KHÔNG break — tiếp tục nghe các candidate trickle-in từ server
            }
            else if (text.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = text.Substring("candidate:".Length).Trim();

                // Chỉ bỏ TCP & IPv6
                if (raw.Contains(':')) continue;
                if (raw.Contains(" tcp ") || raw.Contains("tcptype")) continue;

                var full = "candidate:" + raw;
                _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                {
                    candidate = full,
                    // xem mục bên dưới về sdpMid
                    sdpMLineIndex = 0
                }));
                Debug.Log("[PCStreamClient] add remote cand (UDP): " + full);
            }
            else if (text.StartsWith("end-of-candidates", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[PCStreamClient] remote end-of-candidates");
                // optional: không cần làm gì thêm
            }
            else
            {
                Debug.Log("[PCStreamClient] WS msg: " + text);
            }
        }
    }

    void OnDisable() { Cleanup(); }
    void OnDestroy() { Cleanup(); }

    void Cleanup()
    {
        try
        {
            if (_remoteVideoTrack != null && _onVideoReceived != null)
                _remoteVideoTrack.OnVideoReceived -= _onVideoReceived;
        }
        catch { }
        try { _pc?.Close(); _pc?.Dispose(); } catch { }
        try { _cts?.Cancel(); _ws?.Dispose(); } catch { }
        _remoteVideoTrack = null; _remoteTexture = null; _onVideoReceived = null;
    }
}
