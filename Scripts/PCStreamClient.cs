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
using System.Globalization;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class PCStreamClient : MonoBehaviour
{
    [Header("Signal")]
    public string signalUrl = "ws://192.168.1.9:8288/signal?mode=cluster&monitors=2&resW=1920&resH=1080&kbps=8000&fps=30&client=unity";

    [Header("ICE")]
    [Tooltip("Match the browser test: do NOT drop TCP candidates. Enable only if you know your network supports UDP reliably.")]
    public bool skipTcpIceCandidates = false;

    [Tooltip("RemotePlayServer streams H264-only. If this client offer doesn't contain H264, Unity may hang on SetRemoteDescription(answer).")]
    public bool abortIfOfferMissingH264 = true;

    [Tooltip("Unsafe workaround: inject/override a video payload type in the local offer SDP to advertise H264, even if Unity didn't offer it. Use only for testing.")]
    public bool forceInjectH264IntoOfferSdp = false;

    [Header("World Panel")]
    public WorldPanelPlus worldPanel;
    public RawImage fallbackRawImage;

    [Header("UV Crop (Grid Cell)")]
    public bool useUVCrop = false;
    public int gridCol = 0;
    public int gridRow = 0;
    public int gridCols = 3;
    public int gridRows = 2;
    public int frameWidth = 4082;
    public int frameHeight = 1532;
    public int cellWidth = 1360;
    public int cellHeight = 765;
    public int gapPixels = 1;

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

    // ---- Connection state cache (avoid depending on RTCPeerConnection.ConnectionState property across plugin versions) ----
    private RTCPeerConnectionState _lastPcState = RTCPeerConnectionState.New;

    // ---- UV Crop ----
    private RenderTexture _croppedRT;

    static bool SdpContainsH264(string sdp)
        => !string.IsNullOrEmpty(sdp) && sdp.IndexOf("H264", StringComparison.OrdinalIgnoreCase) >= 0;

    static string GetFirstRtpmapLines(string sdp, int maxLines)
    {
        if (string.IsNullOrEmpty(sdp) || maxLines <= 0) return string.Empty;
        var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var picked = new List<string>(Math.Min(maxLines, 16));
        foreach (var l in lines)
        {
            if (!l.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase)) continue;
            picked.Add(l);
            if (picked.Count >= maxLines) break;
        }
        return string.Join("\n", picked);
    }

    static string FindRtpmapLine(string sdp, int pt)
    {
        if (string.IsNullOrEmpty(sdp)) return string.Empty;
        var needle = "a=rtpmap:" + pt + " ";
        var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var l in lines)
            if (l.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                return l;
        return string.Empty;
    }

    static string InjectH264IntoOfferSdp(string offerSdp)
    {
        if (string.IsNullOrEmpty(offerSdp)) return offerSdp;

        var lines = offerSdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        int mVideoIdx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("m=video ", StringComparison.OrdinalIgnoreCase)) { mVideoIdx = i; break; }
        }
        if (mVideoIdx < 0) return offerSdp;

        // Choose a payload type from the m=video line (prefer 127 if present, else first).
        var parts = lines[mVideoIdx].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int chosenPt = -1;
        for (int i = 3; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var pt)) continue;
            if (pt == 127) { chosenPt = 127; break; }
            if (chosenPt < 0) chosenPt = pt;
        }
        if (chosenPt < 0) return offerSdp;

        // Remove existing rtpmap/fmtp lines for chosen PT within the video section.
        for (int i = mVideoIdx + 1; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("m=", StringComparison.OrdinalIgnoreCase)) break;
            if (lines[i].StartsWith($"a=rtpmap:{chosenPt}", StringComparison.OrdinalIgnoreCase) ||
                lines[i].StartsWith($"a=fmtp:{chosenPt}", StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
                i--;
            }
        }

        // Insert minimal H264 mapping right after m=video.
        int insertAt = mVideoIdx + 1;
        lines.Insert(insertAt++, $"a=rtpmap:{chosenPt} H264/90000");
        lines.Insert(insertAt++, $"a=fmtp:{chosenPt} packetization-mode=1;level-asymmetry-allowed=1;profile-level-id=42e01f");

        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// Trả về texture gốc (chưa crop) để các UVCropReceiver khác sử dụng
    /// </summary>
    public Texture GetRawTexture() => _remoteTexture;

    // ---- Single-sender guard (owner) ----
    static PCStreamClient s_InputOwner;
    bool _isInputOwner;

#if ENABLE_INPUT_SYSTEM
    // Debounce keyboard: tập VK đang bị giữ
    private readonly System.Collections.Generic.HashSet<int> _keysHeldVK = new();
#endif

    // =========================== ICE CONNECTION MONITORING ===========================
    private static string EnsureSavpf(string sdp)
    {
        if (string.IsNullOrEmpty(sdp)) return sdp;

        const string needle = "UDP/TLS/RTP/SAVP";
        int i = 0;
        while (true)
        {
            int idx = sdp.IndexOf(needle, i, StringComparison.Ordinal);
            if (idx < 0) return sdp;

            int after = idx + needle.Length;

            // Already SAVPF.
            if (after < sdp.Length && sdp[after] == 'F')
            {
                i = after + 1;
                continue;
            }

            // Insert missing 'F' to make SAVPF.
            sdp = sdp.Substring(0, after) + "F" + sdp.Substring(after);
            i = after + 1;
        }
    }

    private System.Collections.IEnumerator CheckICEConnectionAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (_pc != null)
        {
            var iceState = _pc.IceConnectionState;
            var pcState = _lastPcState;
            
            Debug.Log($"[PCStreamClient] ICE Check after {delay}s: iceState={iceState}, pcState={pcState}");
            
            if (iceState == RTCIceConnectionState.New || iceState == RTCIceConnectionState.Checking)
            {
                Debug.LogWarning("[PCStreamClient] ⚠️ ICE connection still in progress. This may indicate a compatibility issue.");
                Debug.Log("[PCStreamClient] 🔧 Attempting ICE restart to kickstart connection...");
                
                // Try to trigger ICE restart by creating new offer
                StartCoroutine(RestartICEConnection());
            }
            else if (iceState == RTCIceConnectionState.Connected)
            {
                Debug.Log("[PCStreamClient] 🎉 ICE connection successful!");
            }
        }
    }

    private System.Collections.IEnumerator RestartICEConnection()
    {
        if (_pc == null) yield break;
        
        Debug.Log("[PCStreamClient] Attempting ICE connection restart...");
        
        // Create a new offer which may trigger ICE restart
        var offerOp = _pc.CreateOffer();
        while (!offerOp.IsDone) yield return null;
        
        if (offerOp.IsError)
        {
            Debug.LogError($"[PCStreamClient] Failed to create restart offer: {offerOp.Error.message}");
            yield break;
        }
        
        var offer = offerOp.Desc;
        var setLocalOp = _pc.SetLocalDescription(ref offer);
        while (!setLocalOp.IsDone) yield return null;
        
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[PCStreamClient] Failed to set local restart offer: {setLocalOp.Error.message}");
            yield break;
        }
        
        try
        {
            string offerSdp = offer.sdp;
            // Fire-and-forget. Avoid blocking Unity main thread.
            _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("offer:" + offerSdp)),
                              WebSocketMessageType.Text, true, _cts.Token);
            Debug.Log("[PCStreamClient] Sent restart offer to server");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PCStreamClient] Failed to send restart offer: {ex.Message}");
        }
    }

    // =========================== HELPER METHODS ===========================
    void RunOnMainThread(System.Action action)
    {
        // Simple approach - just run the action directly for now
        // In Unity, coroutines already run on main thread
        action();
    }

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
            iceServers = Array.Empty<RTCIceServer>(),
            iceCandidatePoolSize = 0  // Không sử dụng candidate pool để đơn giản
        };
        Debug.Log($"[PCStreamClient] Creating RTCPeerConnection with signalUrl: {signalUrl}");
        _pc = new RTCPeerConnection(ref cfg);
        Debug.Log($"[PCStreamClient] RTCPeerConnection created successfully");

        _pc.OnIceConnectionChange = s => 
        { 
            string iceState = s.ToString();
            string pcState = _lastPcState.ToString();
            
            Debug.Log($"[PCStreamClient] 🔍 ICE STATE CHANGE: {s} -> {iceState} (PCState={pcState})");
            
            // Log detailed ICE state changes for debugging
            if (s == RTCIceConnectionState.Connected)
            {
                Debug.Log("[PCStreamClient] 🎉 ICE CONNECTED - Video should start flowing!");
            }
            else if (s == RTCIceConnectionState.Failed)
            {
                Debug.LogError("[PCStreamClient] ❌ ICE FAILED - Connection failed");
            }
            else if (s == RTCIceConnectionState.Disconnected)
            {
                Debug.LogWarning("[PCStreamClient] ⚠️ ICE DISCONNECTED");
            }
            else if (s == RTCIceConnectionState.Checking)
            {
                Debug.Log("[PCStreamClient] 🔍 ICE CHECKING - Attempting to connect...");
            }
            else if (s == RTCIceConnectionState.New)
            {
                Debug.Log("[PCStreamClient] 🆕 ICE NEW - Initial state");
            }
        };
        
        _pc.OnConnectionStateChange = s => 
        { 
            _lastPcState = s;
            Debug.Log($"[PCStreamClient] PC state = {s}");
            
            // Additional logging for connection states
            if (s == RTCPeerConnectionState.Connected)
            {
                Debug.Log("[PCStreamClient] PeerConnection fully connected");
            }
            else if (s == RTCPeerConnectionState.Failed)
            {
                Debug.LogError("[PCStreamClient] PeerConnection failed");
            }
        };

        _pc.OnIceCandidate = cand =>
        {
            string msg;
            if (string.IsNullOrEmpty(cand.Candidate))
            {
                msg = "end-of-candidates";
            }
            else
            {
                // RemotePlayServer expects ICE candidate messages prefixed with "candidate:".
                // (See Web/webrtc_test.html: ws.send(ev.candidate.candidate))
                msg = cand.Candidate;
                if (!msg.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) &&
                    !msg.StartsWith("a=candidate:", StringComparison.OrdinalIgnoreCase))
                {
                    msg = "candidate:" + msg;
                }

                // Normalize double-prefix cases (Unity/interop edge-case).
                if (msg.StartsWith("candidate:candidate:", StringComparison.OrdinalIgnoreCase))
                    msg = msg.Substring("candidate:".Length);
            }
            
            // Log candidate format for debugging
            Debug.Log($"[PCStreamClient] 📤 SENDING CANDIDATE: '{msg}' (length={msg.Length})");
            Debug.Log($"[PCStreamClient] 📤 CANDIDATE FIRST 100: '{msg.Substring(0, Math.Min(100, msg.Length))}...'");
            
            // If WebSocket is ready, send immediately; otherwise queue for later
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)),
                                  WebSocketMessageType.Text, true, _cts.Token);
                    Debug.Log($"[PCStreamClient] Sent ICE immediately");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PCStreamClient] Send ICE failed: {ex.Message}, queuing...");
                    _pendingLocalCands.Add(msg);
                }
            }
            else
            {
                // Queue for sending after WS connects
                _pendingLocalCands.Add(msg);
                Debug.Log($"[PCStreamClient] Queued ICE (WS not ready), queue size = {_pendingLocalCands.Count}");
            }
        };

        // Nhận video
        var trans = _pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
        // Configure codecs giống web client
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
            Texture textureToApply = _remoteTexture;

            // Nếu bật UV crop, tạo RenderTexture chỉ chứa phần cell cần hiển thị
            if (useUVCrop)
            {
                textureToApply = GetCroppedTexture(_remoteTexture);
            }

            if (worldPanel != null && !ReferenceEquals(_appliedTexture, _remoteTexture))
            {
                worldPanel.contentTexture = textureToApply;
                worldPanel.Apply();
                _appliedTexture = _remoteTexture;
            }
            if (fallbackRawImage != null) fallbackRawImage.texture = textureToApply;

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
        Debug.Log($"[PCStreamClient] Connecting to signal server: {signalUrl}");
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(signalUrl), _cts.Token);
        Debug.Log($"[PCStreamClient] WebSocket connected successfully to {signalUrl}");

        var offerOp = _pc.CreateOffer(); while (!offerOp.IsDone) await Task.Yield();
        if (offerOp.IsError) throw new Exception("CreateOffer failed: " + offerOp.Error.message);
        var offer = offerOp.Desc;

        Debug.Log($"[PCStreamClient] Created offer with SDP type: {offer.type}");
        Debug.Log($"[PCStreamClient] Offer SDP (first 300 chars): {offer.sdp.Substring(0, Math.Min(300, offer.sdp.Length))}...");
        Debug.Log($"[PCStreamClient] Full Offer SDP length: {offer.sdp.Length}");
        bool offerHasH264 = SdpContainsH264(offer.sdp);
        Debug.Log($"[PCStreamClient] Offer contains H264? {(offerHasH264 ? "YES" : "NO")}");
        Debug.Log($"[PCStreamClient] Offer rtpmap (first 8):\n{GetFirstRtpmapLines(offer.sdp, 8)}");
        var rtpmap127 = FindRtpmapLine(offer.sdp, 127);
        if (!string.IsNullOrEmpty(rtpmap127)) Debug.Log($"[PCStreamClient] Offer rtpmap:127 => {rtpmap127}");

        if (!offerHasH264 && forceInjectH264IntoOfferSdp)
        {
            Debug.LogWarning("[PCStreamClient] ⚠️ Offer does not include H264. Applying unsafe SDP injection to advertise H264...");
            offer.sdp = InjectH264IntoOfferSdp(offer.sdp);
            offerHasH264 = SdpContainsH264(offer.sdp);
            Debug.Log($"[PCStreamClient] After injection, offer contains H264? {(offerHasH264 ? "YES" : "NO")}");
        }

        if (!offerHasH264 && abortIfOfferMissingH264)
        {
            Debug.LogError("[PCStreamClient] ❌ This Unity WebRTC build did not offer H264. RemotePlayServer streams H264-only, so connection cannot succeed. " +
                           "Either use a Unity WebRTC build/package with H264 decode support, or change the server to a codec Unity offers (e.g., VP8). Aborting.");
            try { _ws.Abort(); } catch { }
            return;
        }

        var setLocalOp = _pc.SetLocalDescription(ref offer); while (!setLocalOp.IsDone) await Task.Yield();
        if (setLocalOp.IsError) throw new Exception("SetLocalDescription failed: " + setLocalOp.Error.message);
        
        Debug.Log($"[PCStreamClient] SetLocalDescription completed successfully");

        await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("offer:" + offer.sdp)),
                            WebSocketMessageType.Text, true, _cts.Token);

        foreach (var c in _pendingLocalCands)
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(c)),
                                WebSocketMessageType.Text, true, _cts.Token);
        _pendingLocalCands.Clear();

        var buf = new byte[256 * 1024];
        var pendingRemoteCandidates = new List<string>(); // Queue ICE until answer is set
        bool answerSet = false;
        
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
            Debug.Log($"[PCStreamClient] RAW WS RECV ({text.Length} chars): {text.Substring(0, Mathf.Min(100, text.Length))}...");
            
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (text.StartsWith("answer:", StringComparison.OrdinalIgnoreCase))
            {
                if (_pc == null) 
                {
                    Debug.LogWarning("[PCStreamClient] PeerConnection is null, ignoring Answer");
                    return;
                }
                
                var sdp = text.Substring("answer:".Length).Trim();
                // Server sometimes replies with UDP/TLS/RTP/SAVP (without F). Browser test fixes this.
                // Do the same to avoid SetRemoteDescription failures/hangs.
                sdp = EnsureSavpf(sdp);
                Debug.Log($"[PCStreamClient] Received Answer SDP with length: {sdp.Length}");
                Debug.Log($"[PCStreamClient] Answer SDP sample (first 200 chars): {sdp.Substring(0, Math.Min(200, sdp.Length))}...");
                
                try
                {
                    var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                    Debug.Log($"[PCStreamClient] Setting remote Answer SDP...");
                    
                    var setRemoteOp2 = _pc.SetRemoteDescription(ref answer); 

                    // IMPORTANT: Do NOT use Time.deltaTime for async timeouts.
                    // It may remain 0 in this context and cause an infinite loop.
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var lastBeatMs = 0L;
                    var timeoutMs = 5000;
                    while (!setRemoteOp2.IsDone && sw.ElapsedMilliseconds < timeoutMs)
                    {
                        if (sw.ElapsedMilliseconds - lastBeatMs >= 1000)
                        {
                            lastBeatMs = sw.ElapsedMilliseconds;
                            Debug.Log($"[PCStreamClient] Waiting for SetRemoteDescription... {sw.ElapsedMilliseconds}ms");
                        }
                        await Task.Delay(10);
                    }

                    if (!setRemoteOp2.IsDone)
                    {
                        Debug.LogError($"[PCStreamClient] SetRemoteDescription TIMED OUT after {timeoutMs}ms (still not done). Aborting connection.");
                        return;
                    }
                    
                    if (setRemoteOp2.IsError)
                    {
                        Debug.LogError("[PCStreamClient] SetRemoteDescription failed: " + setRemoteOp2.Error.message);
                    }
                    else
                    {
                        Debug.Log($"[PCStreamClient] SetRemoteDescription(answer) OK, signalState={_pc.SignalingState}");
                        Debug.Log($"[PCStreamClient] Current ICE state after SetRemoteDescription: {_pc.IceConnectionState}");
                        Debug.Log($"[PCStreamClient] Current PeerConnection state (cached): {_lastPcState}");
                        answerSet = true;
                        
                        // Force immediate ICE restart if still in NEW state - using UnityMainThreadDispatcher
                        if (_pc.IceConnectionState == RTCIceConnectionState.New)
                        {
                            Debug.LogWarning("[PCStreamClient] ⚠️ ICE still in NEW state after SetRemoteDescription - forcing restart now");
                            RunOnMainThread(() => StartCoroutine(RestartICEConnection()));
                        }
                        else
                        {
                            // Force ICE state check after a short delay for other cases
                            RunOnMainThread(() => StartCoroutine(CheckICEConnectionAfterDelay(3.0f)));
                        }
                        
                        Debug.Log($"[PCStreamClient] ✅ ICE processing completed successfully!");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PCStreamClient] Exception setting remote description: {ex.Message}");
                    return;
                }
                
                if (answerSet)
                {
                    if (pendingRemoteCandidates.Count > 0)
                    {
                        Debug.Log($"[PCStreamClient] Adding {pendingRemoteCandidates.Count} pending remote ICE candidates");
                        foreach (var cand in pendingRemoteCandidates)
                        {
                            _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                            {
                                candidate = cand,
                                sdpMLineIndex = 0,
                                sdpMid = "0"
                            }));
                            Debug.Log($"[PCStreamClient] Added pending cand: {cand.Substring(0, Mathf.Min(60, cand.Length))}...");
                        }
                        pendingRemoteCandidates.Clear();
                        Debug.Log($"[PCStreamClient] After adding pending candidates: iceState={_pc.IceConnectionState}");
                        
                        // Force ICE connection establishment if still in NEW state
                        if (_pc.IceConnectionState == RTCIceConnectionState.New)
                        {
                            Debug.LogWarning("[PCStreamClient] ⚠️ Forcing ICE connection establishment after adding candidates");
                            StartCoroutine(CheckICEConnectionAfterDelay(1.0f));
                        }
                    }
                }
            }
            else if (text.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[PCStreamClient] 📥 RECEIVED CANDIDATE FROM SERVER: '{text}' (length={text.Length})");
                Debug.Log($"[PCStreamClient] 📥 CANDIDATE FIRST 100: '{text.Substring(0, Math.Min(100, text.Length))}...'");
                
                // Server sends candidate with "candidate:" prefix
                // Try both formats - with and without prefix for Unity WebRTC
                var raw = text.Substring("candidate:".Length).Trim();

                // Normalize potential double-prefix cases.
                if (raw.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase))
                    raw = raw.Substring("candidate:".Length).Trim();

                // Optionally skip TCP candidates (default OFF to match browser test).
                if (skipTcpIceCandidates &&
                    (raw.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) ||
                     raw.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.Log("[PCStreamClient] skip TCP candidate (skipTcpIceCandidates=true)");
                    continue;
                }

                // Full format (most compatible): candidate:<...>
                var fullCandidate = "candidate:" + raw;
                bool added = false;
                
                try
                {
                    if (answerSet)
                    {
                        Debug.Log($"[PCStreamClient] Trying full format candidate: {fullCandidate.Substring(0, Mathf.Min(60, fullCandidate.Length))}...");
                        _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                        {
                            candidate = fullCandidate,
                            sdpMLineIndex = 0,
                            sdpMid = "0"
                        }));
                        Debug.Log($"[PCStreamClient] ✅ Full format candidate added");
                        added = true;
                    }
                    else
                    {
                        pendingRemoteCandidates.Add(fullCandidate);
                        Debug.Log($"[PCStreamClient] Queued full format candidate: {fullCandidate.Substring(0, Mathf.Min(50, fullCandidate.Length))}...");
                        added = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PCStreamClient] Failed to add full format candidate: {ex.Message}");
                    added = false;
                }
                
                // Fallback: try raw format without prefix
                if (!added)
                {
                    try
                    {
                        if (answerSet)
                        {
                            Debug.Log($"[PCStreamClient] Trying raw format fallback: {raw.Substring(0, Mathf.Min(60, raw.Length))}...");
                            _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit
                            {
                                candidate = raw,
                                sdpMLineIndex = 0,
                                sdpMid = "0"
                            }));
                            Debug.Log($"[PCStreamClient] ✅ Raw format candidate added");
                        }
                        else
                        {
                            pendingRemoteCandidates.Add(raw);
                            Debug.Log($"[PCStreamClient] Queued raw format candidate: {raw.Substring(0, Mathf.Min(50, raw.Length))}...");
                        }
                    }
                    catch (Exception ex2)
                    {
                        Debug.LogError($"[PCStreamClient] Both formats failed: {ex2.Message}");
                    }
                }
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

        // Cleanup UV crop resources
        if (_croppedRT != null)
        {
            _croppedRT.Release();
            Destroy(_croppedRT);
            _croppedRT = null;
        }
    }

    // =========================== UV CROP ===========================
    Texture GetCroppedTexture(Texture source)
    {
        if (source == null) return null;

        // Tính vị trí pixel của cell trong frame
        int xStart = gridCol * (cellWidth + gapPixels);
        int yStart = gridRow * (cellHeight + gapPixels);

        // Tính UV scale và offset
        float scaleX = (float)cellWidth / frameWidth;
        float scaleY = (float)cellHeight / frameHeight;

        // Offset: UV origin (0,0) = bottom-left, image (0,0) = top-left
        float offsetX = (float)xStart / frameWidth;
        float offsetY = 1f - (float)(yStart + cellHeight) / frameHeight;

        // Tạo RenderTexture nếu cần
        if (_croppedRT == null || _croppedRT.width != cellWidth || _croppedRT.height != cellHeight)
        {
            if (_croppedRT != null)
            {
                _croppedRT.Release();
                Destroy(_croppedRT);
            }
            _croppedRT = new RenderTexture(cellWidth, cellHeight, 0, RenderTextureFormat.ARGB32);
            _croppedRT.filterMode = FilterMode.Bilinear;
            _croppedRT.Create();
        }

        // Dùng Graphics.Blit với scale/offset trực tiếp
        Graphics.Blit(source, _croppedRT, new Vector2(scaleX, scaleY), new Vector2(offsetX, offsetY));

        return _croppedRT;
    }

    // =========================== FORWARDERS (cursor) ===========================
    void BindCursorForwardersOnce()
    {
        if (worldPanel == null || worldPanel.cursor == null || _ws == null) return;

        worldPanel.cursor.onMovedUV += (u, v) =>
        {
            // Chuyển đổi UV cục bộ của cell sang UV toàn frame
            float frameU = u, frameV = v;
            if (useUVCrop)
            {
                (frameU, frameV) = CellUVToFrameUV(u, v);
            }

            string su = frameU.ToString("F6", CultureInfo.InvariantCulture);
            string sv = frameV.ToString("F6", CultureInfo.InvariantCulture);
            var json = $"{{\"input\":\"move_uv\",\"u\":{su},\"v\":{sv}}}";
            Debug.Log($"[PC->SRV] move_uv u={su} v={sv} (cell {gridCol},{gridRow})");
            var bytes = Encoding.UTF8.GetBytes(json);
            try { _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token); } catch { }
        };

        // Left click từ cursor
        worldPanel.cursor.onClickDown += () => SendMouseDown("left");
        worldPanel.cursor.onClickUp += () => SendMouseUp("left");
    }

    // Chuyển đổi UV cục bộ của cell (0-1) sang UV toàn frame (0-1)
    (float frameU, float frameV) CellUVToFrameUV(float cellU, float cellV)
    {
        // Vị trí pixel bắt đầu của cell
        int xStart = gridCol * (cellWidth + gapPixels);
        int yStart = gridRow * (cellHeight + gapPixels);

        // Pixel position trong frame
        float pixelX = xStart + cellU * cellWidth;
        float pixelY = yStart + (1f - cellV) * cellHeight; // cellV=1 là top của cell = yStart

        // Chuyển sang UV của frame
        // frameU: pixelX / frameWidth
        // frameV: 1 - pixelY / frameHeight (vì UV v=1 là top)
        float frameU = pixelX / frameWidth;
        float frameV = 1f - pixelY / frameHeight;

        return (Mathf.Clamp01(frameU), Mathf.Clamp01(frameV));
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
        if (_ws == null || _cts == null) return;
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
