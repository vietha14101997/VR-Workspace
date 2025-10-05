using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class PCStreamClient : MonoBehaviour
{
    [Header("Signal")]
    public string signalUrl = "ws://127.0.0.1:8288/signal";

    [Header("World Panel")]
    public WorldPanelPlus worldPanel;
    public RawImage fallbackRawImage;

    // ---- WebRTC / Signal ----
    private RTCPeerConnection _pc;
    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private VideoStreamTrack _remoteVideoTrack;
    private Texture _remoteTexture;
    private OnVideoReceived _onVideoReceived;
    private readonly System.Collections.Generic.List<string> _pendingLocalCands = new();

    // ---- Cursor / Binding ----
    private bool _cursorBound;
    private Texture _appliedTexture;

    // ---- Single-sender guard (owner) ----
    static PCStreamClient s_InputOwner;
    bool _isInputOwner;

#if ENABLE_INPUT_SYSTEM
    // Debounce keyboard: tập VK đang bị giữ
    private readonly System.Collections.Generic.HashSet<int> _keysHeldVK = new();
#endif

    // =========================== LIFECYCLE ===========================
    void OnEnable()
    {
        // chọn owner gửi input (chỉ 1 instance)
        if (s_InputOwner == null)
        {
            s_InputOwner = this;
            _isInputOwner = true;
            Debug.Log("[PCStreamClient] This instance is INPUT OWNER");
        }
        else
        {
            _isInputOwner = ReferenceEquals(s_InputOwner, this);
            if (!_isInputOwner)
                Debug.Log("[PCStreamClient] Input disabled on this instance (another owner exists)");
        }
    }

    async void Start()
    {
        StartCoroutine(WebRTC.Update());
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        _cts = new CancellationTokenSource();
#if ENABLE_INPUT_SYSTEM
        // Bắt text từ IME/physical keyboard trên Android
        Keyboard.current.onTextInput += ch =>
        {
            // gói unicode (UTF-16) sang JSON "text"
            SendText(ch.ToString());
        };
#endif

        var cfg = new RTCConfiguration
        {
            iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } }
        };
        _pc = new RTCPeerConnection(ref cfg);

        _pc.OnIceConnectionChange = s => Debug.Log("[PCStreamClient] ICE = " + s);
        _pc.OnConnectionStateChange = s => Debug.Log("[PCStreamClient] PC  = " + s);

        _pc.OnIceCandidate = cand =>
        {
            var txt = string.IsNullOrEmpty(cand.Candidate) ? "end-of-candidates" : cand.Candidate;
            _ws?.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(txt)),
                           WebSocketMessageType.Text, true, _cts.Token);
        };

        // Nhận video
        var trans = _pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
        var caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);
        var h264 = caps.codecs.Where(c => (c.mimeType ?? "").IndexOf("H264", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        var others = caps.codecs.Where(c => h264.All(h => h.mimeType != c.mimeType || h.clockRate != c.clockRate)).ToArray();
        trans.SetCodecPreferences(h264.Concat(others).ToArray());

        _pc.OnTrack = e =>
        {
            if (e.Track is VideoStreamTrack v)
            {
                _remoteVideoTrack = v;
                _onVideoReceived = tex => { _remoteTexture = tex; };
                _remoteVideoTrack.OnVideoReceived += _onVideoReceived;
                Debug.Log("[PCStreamClient] Video track bound");
            }
        };

        await ConnectAndSignalOffer();
    }

    void Update()
    {
        // ------- Apply texture to panel / fallback -------
        if (_remoteTexture != null)
        {
            if (worldPanel != null && !ReferenceEquals(_appliedTexture, _remoteTexture))
            {
                worldPanel.contentTexture = _remoteTexture;
                worldPanel.Apply();
                _appliedTexture = _remoteTexture;
            }
            if (fallbackRawImage != null) fallbackRawImage.texture = _remoteTexture;

            // Bind cursor + forwarders đúng 1 lần (chỉ owner mới bind để gửi input trái)
            if (_isInputOwner && worldPanel != null && worldPanel.cursor != null && !_cursorBound)
            {
                worldPanel.cursor.SetVisible(true);
                BindCursorForwardersOnce();
                _cursorBound = true;
            }
        }

        // ------- ONLY owner sends input (wheel / right / middle / keyboard) -------
        if (_isInputOwner)
        {
#if ENABLE_INPUT_SYSTEM
            // Wheel + Right/Middle
            if (Mouse.current != null)
            {
                var s = Mouse.current.scroll.ReadValue(); // x = horizontal, y = vertical
                int v = (int)Mathf.Round(s.y); if (v != 0) SendWheel(v, false);
                int h = (int)Mathf.Round(s.x); if (h != 0) SendWheel(h, true);

                if (worldPanel != null && worldPanel.cursor != null && worldPanel.cursor.gameObject.activeInHierarchy)
                {
                    if (Mouse.current.rightButton.wasPressedThisFrame) SendMouseDown("right");
                    if (Mouse.current.rightButton.wasReleasedThisFrame) SendMouseUp("right");
                    if (Mouse.current.middleButton.wasPressedThisFrame) SendMouseDown("middle");
                    if (Mouse.current.middleButton.wasReleasedThisFrame) SendMouseUp("middle");
                }
            }

            // Keyboard: debounce
            if (Keyboard.current != null)
            {
                foreach (var kc in Keyboard.current.allKeys)
                {
                    int vk = KeyToVK_NewInput(kc.keyCode);
                    if (vk < 0) continue;

                    if (kc.wasPressedThisFrame)
                    {
                        if (_keysHeldVK.Add(vk)) SendKey(vk, true);
                    }
                    if (kc.wasReleasedThisFrame)
                    {
                        if (_keysHeldVK.Remove(vk)) SendKey(vk, false);
                    }
                }
            }
#endif
#if !ENABLE_INPUT_SYSTEM
            // Legacy fallback (nếu đang dùng Old Input)
            int v2 = Mathf.RoundToInt(Input.mouseScrollDelta.y * 120f);
            if (v2 != 0) SendWheel(v2, false);
            foreach (var kv in _legacyWatchKeys)
            {
                if (Input.GetKeyDown(kv.Key)) SendKey(kv.Value, true);
                if (Input.GetKeyUp(kv.Key))   SendKey(kv.Value, false);
            }
#endif
        }
    }

    void OnDisable()
    {
        if (_isInputOwner && ReferenceEquals(s_InputOwner, this)) s_InputOwner = null;
        Cleanup();
    }
    void OnDestroy()
    {
        if (_isInputOwner && ReferenceEquals(s_InputOwner, this)) s_InputOwner = null;
        Cleanup();
    }

    // =========================== SIGNAL / WEBRTC ===========================
    async Task ConnectAndSignalOffer()
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(signalUrl), _cts.Token);

        var offerOp = _pc.CreateOffer(); while (!offerOp.IsDone) await Task.Yield();
        if (offerOp.IsError) throw new Exception("CreateOffer failed: " + offerOp.Error.message);
        var offer = offerOp.Desc;

        var setLocalOp = _pc.SetLocalDescription(ref offer); while (!setLocalOp.IsDone) await Task.Yield();
        if (setLocalOp.IsError) throw new Exception("SetLocalDescription failed: " + setLocalOp.Error.message);

        await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("offer:" + offer.sdp)),
                            WebSocketMessageType.Text, true, _cts.Token);

        foreach (var c in _pendingLocalCands)
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(c)),
                                WebSocketMessageType.Text, true, _cts.Token);
        _pendingLocalCands.Clear();

        var buf = new byte[256 * 1024];
        while (true)
        {
            var ms = new System.IO.MemoryStream();
            while (true)
            {
                var res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                if (res.MessageType == WebSocketMessageType.Close) throw new Exception("Signal closed");
                ms.Write(buf, 0, res.Count);
                if (res.EndOfMessage) break;
            }
            var text = Encoding.UTF8.GetString(ms.ToArray());

            if (text.StartsWith("answer:", StringComparison.OrdinalIgnoreCase))
            {
                var sdp = text.Substring("answer:".Length);
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var setRemoteOp2 = _pc.SetRemoteDescription(ref answer); while (!setRemoteOp2.IsDone) await Task.Yield();
                if (setRemoteOp2.IsError) throw new Exception("SetRemoteDescription failed: " + setRemoteOp2.Error.message);
                Debug.Log("[PCStreamClient] SetRemoteDescription(answer) OK");
            }
            else if (text.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = text.Substring("candidate:".Length).Trim();
                if (raw.Contains(':')) continue;                    // bỏ IPv6
                if (raw.Contains(" tcp ") || raw.Contains("tcptype")) continue; // bỏ TCP

                var full = "candidate:" + raw;
                _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                {
                    candidate = full,
                    sdpMLineIndex = 0
                }));
                Debug.Log("[PCStreamClient] add remote cand (UDP): " + full);
            }
            else if (text.StartsWith("end-of-candidates", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[PCStreamClient] remote end-of-candidates");
            }
            else
            {
                Debug.Log("[PCStreamClient] WS msg: " + text);
            }
        }
    }

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
#if ENABLE_INPUT_SYSTEM
        _keysHeldVK.Clear();
#endif
    }

    // =========================== FORWARDERS (cursor) ===========================
    void BindCursorForwardersOnce()
    {
        if (worldPanel == null || worldPanel.cursor == null || _ws == null) return;

        worldPanel.cursor.onMovedUV += (u, v) =>
        {
            string su = u.ToString("F6", CultureInfo.InvariantCulture);
            string sv = v.ToString("F6", CultureInfo.InvariantCulture);
            var json = $"{{\"input\":\"move_uv\",\"u\":{su},\"v\":{sv}}}";
            Debug.Log($"[PC->SRV] move_uv u={su} v={sv}");
            var bytes = Encoding.UTF8.GetBytes(json);
            try { _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token); } catch { }
        };

        // Left click từ cursor
        worldPanel.cursor.onClickDown += () => SendMouseDown("left");
        worldPanel.cursor.onClickUp += () => SendMouseUp("left");
    }

    // =========================== SEND JSON HELPERS ===========================
    static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < ' ') sb.AppendFormat("\\u{0:X4}", (int)ch);
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    void SendText(string s)
    {
        if (_ws == null || string.IsNullOrEmpty(s)) return;
        var payload = $"{{\"input\":\"text\",\"text\":\"{EscapeJson(s)}\"}}";
        Debug.Log($"[PC->SRV] text '{s}' (len={s.Length})");
        var bytes = Encoding.UTF8.GetBytes(payload);
        try { _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token); } catch { }
    }

    void SendKey(int vk, bool down)
    {
        if (_ws == null) return;
        Debug.Log($"[PC->SRV] key vk=0x{vk:X2} {(down ? "DOWN" : "UP")}");
        var json = $"{{\"input\":\"key\",\"vk\":{vk},\"down\":{(down ? "true" : "false")}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        try { _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token); } catch { }
    }

    void SendWheel(int delta, bool horizontal)
    {
        if (_ws == null) return;
        Debug.Log($"[PC->SRV] wheel {(horizontal ? "H" : "V")} delta={delta}");
        var json = horizontal
            ? $"{{\"input\":\"wheel\",\"delta\":{delta},\"h\":true}}"
            : $"{{\"input\":\"wheel\",\"delta\":{delta}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        try { _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token); } catch { }
    }

    void SendMouseDown(string btn)   // "left" | "right" | "middle"
    {
        if (_ws == null) return;
        Debug.Log($"[PC->SRV] mouse {btn} DOWN");
        var bytes = Encoding.UTF8.GetBytes($"{{\"input\":\"down\",\"btn\":\"{btn}\"}}");
        try { _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token); } catch { }
    }
    void SendMouseUp(string btn)
    {
        if (_ws == null) return;
        Debug.Log($"[PC->SRV] mouse {btn} UP");
        var bytes = Encoding.UTF8.GetBytes($"{{\"input\":\"up\",\"btn\":\"{btn}\"}}");
        try { _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token); } catch { }
    }

    // =========================== VK MAP ===========================
#if ENABLE_INPUT_SYSTEM
    static int KeyToVK_NewInput(UnityEngine.InputSystem.Key k)
    {
        // A..Z
        if (k >= UnityEngine.InputSystem.Key.A && k <= UnityEngine.InputSystem.Key.Z)
            return 0x41 + ((int)k - (int)UnityEngine.InputSystem.Key.A);

        // Dãy số hàng trên (Digit0..Digit9)
        switch (k)
        {
            case UnityEngine.InputSystem.Key.Digit0: return 0x30;
            case UnityEngine.InputSystem.Key.Digit1: return 0x31;
            case UnityEngine.InputSystem.Key.Digit2: return 0x32;
            case UnityEngine.InputSystem.Key.Digit3: return 0x33;
            case UnityEngine.InputSystem.Key.Digit4: return 0x34;
            case UnityEngine.InputSystem.Key.Digit5: return 0x35;
            case UnityEngine.InputSystem.Key.Digit6: return 0x36;
            case UnityEngine.InputSystem.Key.Digit7: return 0x37;
            case UnityEngine.InputSystem.Key.Digit8: return 0x38;
            case UnityEngine.InputSystem.Key.Digit9: return 0x39;
        }

        // Keypad 0..9
        switch (k)
        {
            case UnityEngine.InputSystem.Key.Numpad0: return 0x60;
            case UnityEngine.InputSystem.Key.Numpad1: return 0x61;
            case UnityEngine.InputSystem.Key.Numpad2: return 0x62;
            case UnityEngine.InputSystem.Key.Numpad3: return 0x63;
            case UnityEngine.InputSystem.Key.Numpad4: return 0x64;
            case UnityEngine.InputSystem.Key.Numpad5: return 0x65;
            case UnityEngine.InputSystem.Key.Numpad6: return 0x66;
            case UnityEngine.InputSystem.Key.Numpad7: return 0x67;
            case UnityEngine.InputSystem.Key.Numpad8: return 0x68;
            case UnityEngine.InputSystem.Key.Numpad9: return 0x69;
        }

        // F1..F12
        if (k >= UnityEngine.InputSystem.Key.F1 && k <= UnityEngine.InputSystem.Key.F12)
            return 0x70 + ((int)k - (int)UnityEngine.InputSystem.Key.F1);

        // Các phím còn lại (giữ nguyên bản của bạn)
        switch (k)
        {
            case UnityEngine.InputSystem.Key.Enter: return 0x0D;
            case UnityEngine.InputSystem.Key.Escape: return 0x1B;
            case UnityEngine.InputSystem.Key.Backspace: return 0x08;
            case UnityEngine.InputSystem.Key.Tab: return 0x09;
            case UnityEngine.InputSystem.Key.Space: return 0x20;

            case UnityEngine.InputSystem.Key.LeftArrow: return 0x25;
            case UnityEngine.InputSystem.Key.UpArrow: return 0x26;
            case UnityEngine.InputSystem.Key.RightArrow: return 0x27;
            case UnityEngine.InputSystem.Key.DownArrow: return 0x28;

            case UnityEngine.InputSystem.Key.Insert: return 0x2D;
            case UnityEngine.InputSystem.Key.Delete: return 0x2E;
            case UnityEngine.InputSystem.Key.Home: return 0x24;
            case UnityEngine.InputSystem.Key.End: return 0x23;
            case UnityEngine.InputSystem.Key.PageUp: return 0x21;
            case UnityEngine.InputSystem.Key.PageDown: return 0x22;

            case UnityEngine.InputSystem.Key.Minus: return 0xBD;
            case UnityEngine.InputSystem.Key.Equals: return 0xBB;
            case UnityEngine.InputSystem.Key.LeftBracket: return 0xDB;
            case UnityEngine.InputSystem.Key.RightBracket: return 0xDD;
            case UnityEngine.InputSystem.Key.Semicolon: return 0xBA;
            case UnityEngine.InputSystem.Key.Quote: return 0xDE;
            case UnityEngine.InputSystem.Key.Comma: return 0xBC;
            case UnityEngine.InputSystem.Key.Period: return 0xBE;
            case UnityEngine.InputSystem.Key.Slash: return 0xBF;
            case UnityEngine.InputSystem.Key.Backslash: return 0xDC;

            case UnityEngine.InputSystem.Key.LeftShift: return 0xA0;
            case UnityEngine.InputSystem.Key.RightShift: return 0xA1;
            case UnityEngine.InputSystem.Key.LeftCtrl: return 0xA2;
            case UnityEngine.InputSystem.Key.RightCtrl: return 0xA3;
            case UnityEngine.InputSystem.Key.LeftAlt: return 0xA4;
            case UnityEngine.InputSystem.Key.RightAlt: return 0xA5;
            case UnityEngine.InputSystem.Key.LeftWindows: return 0x5B;
            case UnityEngine.InputSystem.Key.RightWindows: return 0x5C;
        }
        return -1;
    }
#endif

#if !ENABLE_INPUT_SYSTEM
    static readonly System.Collections.Generic.Dictionary<KeyCode, int> _legacyWatchKeys =
        new System.Collections.Generic.Dictionary<KeyCode, int>
        {
            { KeyCode.A,0x41},{ KeyCode.B,0x42},{ KeyCode.C,0x43},{ KeyCode.D,0x44},
            { KeyCode.E,0x45},{ KeyCode.F,0x46},{ KeyCode.G,0x47},{ KeyCode.H,0x48},
            { KeyCode.I,0x49},{ KeyCode.J,0x4A},{ KeyCode.K,0x4B},{ KeyCode.L,0x4C},
            { KeyCode.M,0x4D},{ KeyCode.N,0x4E},{ KeyCode.O,0x4F},{ KeyCode.P,0x50},
            { KeyCode.Q,0x51},{ KeyCode.R,0x52},{ KeyCode.S,0x53},{ KeyCode.T,0x54},
            { KeyCode.U,0x55},{ KeyCode.V,0x56},{ KeyCode.W,0x57},{ KeyCode.X,0x58},
            { KeyCode.Y,0x59},{ KeyCode.Z,0x5A},

            { KeyCode.Alpha0,0x30},{ KeyCode.Alpha1,0x31},{ KeyCode.Alpha2,0x32},{ KeyCode.Alpha3,0x33},
            { KeyCode.Alpha4,0x34},{ KeyCode.Alpha5,0x35},{ KeyCode.Alpha6,0x36},{ KeyCode.Alpha7,0x37},
            { KeyCode.Alpha8,0x38},{ KeyCode.Alpha9,0x39},

            { KeyCode.Return,0x0D},{ KeyCode.Escape,0x1B},{ KeyCode.Backspace,0x08},{ KeyCode.Tab,0x09},{ KeyCode.Space,0x20},
            { KeyCode.LeftArrow,0x25},{ KeyCode.UpArrow,0x26},{ KeyCode.RightArrow,0x27},{ KeyCode.DownArrow,0x28},
        };
#endif
}
