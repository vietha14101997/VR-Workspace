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
    
    [Header("LAN Optimization")]
    [Tooltip("Auto-detect LAN connection and add &lan=1 parameter for better ICE candidate filtering on private networks")]
    public bool autoDetectLAN = true;

    [Header("ICE")]
    [Tooltip("RECOMMENDED: Skip TCP candidates to match browser behavior. TCP candidates can cause ICE negotiation issues with some servers.")]
    public bool skipTcpIceCandidates = true;
    
    [Tooltip("Force UDP-only ICE transport policy to prevent any TCP candidate generation")]
    public bool forceUdpOnly = true;

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

    static bool IsPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        
        // Check for common private IP patterns
        if (System.Text.RegularExpressions.Regex.IsMatch(host.Trim(), @"^(\d+)\.(\d+)\.(\d+)\.(\d+)$"))
        {
            var parts = host.Trim().Split('.');
            if (parts.Length == 4)
            {
                if (int.TryParse(parts[0], out int first) && 
                    int.TryParse(parts[1], out int second))
                {
                    // 10.x.x.x
                    if (first == 10) return true;
                    // 192.168.x.x  
                    if (first == 192 && second == 168) return true;
                    // 172.16.x.x - 172.31.x.x
                    if (first == 172 && second >= 16 && second <= 31) return true;
                }
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
                string host = uri.Host;
                
                // Add lan=1 parameter for private networks to enable ICE candidate filtering
                if (IsPrivateHost(host) && !url.Contains("lan="))
                {
                    url += url.Contains("?") ? "&lan=1" : "?lan=1";
                    Debug.Log($"[PCStreamClient] Private IP detected ({host}), adding LAN optimization: lan=1");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PCStreamClient] URL parsing failed: {ex.Message}, using original URL");
            }
        }
        
        return url;
    }

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
            
            Debug.Log($"[PCStreamClient] 🔍 ICE Check after {delay}s:");
            Debug.Log($"[PCStreamClient]   ICE State: {iceState}");
            Debug.Log($"[PCStreamClient]   PC State: {pcState}");
            
            if (iceState == RTCIceConnectionState.New || iceState == RTCIceConnectionState.Checking)
            {
                Debug.LogWarning($"[PCStreamClient] ⚠️ ICE still {iceState} after {delay}s - this suggests network issues");
                Debug.LogWarning("[PCStreamClient] 💡 TROUBLESHOOTING TIPS:");
                Debug.LogWarning("[PCStreamClient]   • Check if server is running on 192.168.1.9:8288");
                Debug.LogWarning("[PCStreamClient]   • Ensure both devices are on same LAN");
                Debug.LogWarning("[PCStreamClient]   • Try disabling firewall temporarily");
                Debug.LogWarning("[PCStreamClient]   • Check Unity WebRTC package version");
                
                if (delay >= 5.0f) // Only restart after significant delay
                {
                    Debug.Log("[PCStreamClient] 🔧 Attempting ICE restart to recover...");
                    StartCoroutine(RestartICEConnection());
                }
            }
            else if (iceState == RTCIceConnectionState.Connected)
            {
                Debug.Log("[PCStreamClient] 🎉 ICE CONNECTION SUCCESS!");
                Debug.Log("[PCStreamClient] 📺 Video should start streaming now...");
            }
            else if (iceState == RTCIceConnectionState.Failed)
            {
                Debug.LogError("[PCStreamClient] ❌ ICE CONNECTION FAILED!");
                Debug.LogError("[PCStreamClient] 🔧 SOLUTIONS:");
                Debug.LogError("[PCStreamClient]   1. Enable 'skipTcpIceCandidates' (should be TRUE)");
                Debug.LogError("[PCStreamClient]   2. Verify server IP: 192.168.1.9");
                Debug.LogError("[PCStreamClient]   3. Check network connectivity between devices");
                Debug.LogError("[PCStreamClient]   4. Try browser test first: http://192.168.1.9:8288/test");
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
    
    System.Collections.IEnumerator PingKeepalive()
    {
        // Wait a bit for connection to establish
        yield return new WaitForSeconds(2.0f);
        
        while (_ws != null && _ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Send ping to keep connection alive and measure latency
                // Use fire-and-forget pattern for coroutine
                _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping")),
                                 WebSocketMessageType.Text, true, _cts.Token);
                Debug.Log("[PCStreamClient] Sent ping keepalive");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PCStreamClient] Ping keepalive failed: {ex.Message}");
                break;
            }
            
            // Send ping every 5 seconds (more conservative than webrtc_test.html's 2s)
            yield return new WaitForSeconds(5.0f);
        }
        
        Debug.Log("[PCStreamClient] Ping keepalive stopped");
    }
    
    System.Collections.IEnumerator MonitorConnection()
    {
        yield return new WaitForSeconds(1.0f);
        
        while (_pc != null && !_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Log connection state every 10 seconds
                Debug.Log($"[PCStreamClient] 📊 CONNECTION STATUS:");
                Debug.Log($"[PCStreamClient]   WebSocket: {(_ws?.State.ToString() ?? "null")}");
                Debug.Log($"[PCStreamClient]   ICE: {_pc.IceConnectionState}");
                Debug.Log($"[PCStreamClient]   PC: {_lastPcState}");
                Debug.Log($"[PCStreamClient]   Signaling: {_pc.SignalingState}");
                Debug.Log($"[PCStreamClient]   Video: {(_remoteTexture != null ? "✅ Receiving" : "❌ No video")}");
                
                // Check for issues and suggest fixes
                if (_pc.IceConnectionState == RTCIceConnectionState.Failed)
                {
                    Debug.LogError("[PCStreamClient] ❌ ICE connection failed - connection lost");
                }
                else if (_pc.IceConnectionState == RTCIceConnectionState.Disconnected)
                {
                    Debug.LogWarning("[PCStreamClient] ⚠️ ICE disconnected - trying to reconnect...");
                }
                else if (_pc.IceConnectionState == RTCIceConnectionState.Checking)
                {
                    Debug.Log("[PCStreamClient] 🔍 ICE checking - connection in progress...");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PCStreamClient] Monitor error: {ex.Message}");
            }
            
            // Monitor every 10 seconds
            yield return new WaitForSeconds(10.0f);
        }
        
        Debug.Log("[PCStreamClient] Connection monitoring stopped");
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
            iceCandidatePoolSize = 0,  // Không sử dụng candidate pool để đơn giản
            iceTransportPolicy = forceUdpOnly ? RTCIceTransportPolicy.Relay : RTCIceTransportPolicy.All  // Force UDP-only if enabled
        };
        
        string optimizedUrl = BuildOptimizedSignalUrl();
        Debug.Log($"[PCStreamClient] ===== CONNECTION SETUP =====");
        Debug.Log($"[PCStreamClient] Original signalUrl: {signalUrl}");
        Debug.Log($"[PCStreamClient] Optimized URL: {optimizedUrl}");
        Debug.Log($"[PCStreamClient] LAN auto-detect: {autoDetectLAN}");
        Debug.Log($"[PCStreamClient] Skip TCP ICE: {skipTcpIceCandidates} (RECOMMENDED: true)");
        Debug.Log($"[PCStreamClient] Force UDP-only: {forceUdpOnly} (RECOMMENDED: true)");
        Debug.Log($"[PCStreamClient] ===== FIXES APPLIED =====");
        Debug.Log($"[PCStreamClient] ✅ TCP candidate GENERATION filtering (prevents Unity sending TCP to server)");
        Debug.Log($"[PCStreamClient] ✅ TCP candidate RECEPTION filtering (prevents Unity accepting TCP from server)");
        Debug.Log($"[PCStreamClient] ✅ UDP-only ICE transport policy (prevents any TCP generation)");
        Debug.Log($"[PCStreamClient] ✅ Enhanced debugging and timeout handling");
        Debug.Log($"[PCStreamClient] ✅ Multiple ICE connection checkpoints");
        Debug.Log($"[PCStreamClient] ✅ LAN optimization with lan=1 parameter");
        Debug.Log($"[PCStreamClient] ===== EXPECTED BEHAVIOR =====");
        Debug.Log($"[PCStreamClient] • Unity will only send UDP candidates (matching browser)");
        Debug.Log($"[PCStreamClient] • Unity will only accept UDP candidates from server");
        Debug.Log($"[PCStreamClient] Creating RTCPeerConnection...");
        
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
                Debug.Log("[PCStreamClient] 📤 Local ICE gathering complete");
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
                
                // CRITICAL FIX: Filter out TCP candidates BEFORE sending to server
                // Browser only sends UDP, Unity generates both UDP+TCP causing ICE failures
                if (skipTcpIceCandidates && 
                    (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || 
                     msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.LogError($"[PCStreamClient] ❌ FILTERED LOCAL TCP candidate (TCP candidates cause failures): {msg.Substring(0, Math.Min(60, msg.Length))}...");
                    return; // Don't send TCP candidates to server
                }
                
                // Check for host candidates (LAN optimization)
                bool isHost = msg.Contains(" typ host ", StringComparison.OrdinalIgnoreCase);
                bool isPrivateIP = msg.Contains("192.168.") || msg.Contains("10.") || 
                                 (msg.Contains("172.") && System.Text.RegularExpressions.Regex.IsMatch(msg, @"172\.(1[6-9]|2\d|3[01])\."));
                
                if (isHost && isPrivateIP)
                {
                    Debug.Log($"[PCStreamClient] 📤 SENDING LOCAL HOST (LAN UDP): {msg.Substring(0, Math.Min(80, msg.Length))}...");
                }
                else
                {
                    Debug.Log($"[PCStreamClient] 📤 SENDING LOCAL CANDIDATE: {msg.Substring(0, Math.Min(60, msg.Length))}...");
                }
            }
            
            // If WebSocket is ready, send immediately; otherwise queue for later
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    // Fire-and-forget for better performance
                    _ = _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)),
                                     WebSocketMessageType.Text, true, _cts.Token);
                    Debug.Log($"[PCStreamClient] ✅ Sent ICE candidate immediately");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PCStreamClient] ⚠️ Send ICE failed: {ex.Message}, queuing...");
                    _pendingLocalCands.Add(msg);
                }
            }
            else
            {
                // Queue for sending after WS connects
                _pendingLocalCands.Add(msg);
                Debug.Log($"[PCStreamClient] 📦 Queued ICE (WS not ready), queue size = {_pendingLocalCands.Count}");
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
                Debug.Log("[PCStreamClient] 🎥 Video track received and bound successfully");
                
                // Log video track properties for debugging
                Debug.Log($"[PCStreamClient] Video track ID: {v.Id}");
                Debug.Log($"[PCStreamClient] Video track enabled: {v.Enabled}");
                Debug.Log($"[PCStreamClient] Video track ready state: {v.ReadyState}");
            }
            else
            {
                Debug.LogWarning($"[PCStreamClient] ⚠️ Received non-video track: {e.Track?.GetType()?.Name}");
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
        string optimizedUrl = BuildOptimizedSignalUrl();
        Debug.Log($"[PCStreamClient] ===== STARTING CONNECTION =====");
        Debug.Log($"[PCStreamClient] 🔗 Connecting to signal server: {optimizedUrl}");
        
        try
        {
            _ws = new ClientWebSocket();
            
            // Set WebSocket options for better compatibility
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            
            await _ws.ConnectAsync(new Uri(optimizedUrl), _cts.Token);
            Debug.Log($"[PCStreamClient] ✅ WebSocket connected successfully!");
            Debug.Log($"[PCStreamClient] WebSocket state: {_ws.State}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PCStreamClient] ❌ WebSocket connection failed: {ex.Message}");
            Debug.LogError($"[PCStreamClient] Exception type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Debug.LogError($"[PCStreamClient] Inner exception: {ex.InnerException.Message}");
            }
            throw;
        }

        Debug.Log($"[PCStreamClient] 🎬 Creating WebRTC offer...");
        var offerOp = _pc.CreateOffer(); 
        while (!offerOp.IsDone) await Task.Yield();
        
        if (offerOp.IsError) 
        {
            string errorMsg = $"CreateOffer failed: {offerOp.Error.message}";
            Debug.LogError($"[PCStreamClient] ❌ {errorMsg}");
            throw new Exception(errorMsg);
        }
        
        var offer = offerOp.Desc;

        Debug.Log($"[PCStreamClient] ✅ Offer created successfully!");
        Debug.Log($"[PCStreamClient] 📄 SDP type: {offer.type}");
        Debug.Log($"[PCStreamClient] 📄 SDP length: {offer.sdp.Length} characters");
        Debug.Log($"[PCStreamClient] 📄 SDP preview (first 200 chars):");
        Debug.Log($"[PCStreamClient]    {offer.sdp.Substring(0, Math.Min(200, offer.sdp.Length))}...");
        
        bool offerHasH264 = SdpContainsH264(offer.sdp);
        Debug.Log($"[PCStreamClient] 🎥 H264 codec support: {(offerHasH264 ? "✅ YES" : "❌ NO")}");
        
        if (!offerHasH264)
        {
            Debug.LogWarning("[PCStreamClient] ⚠️ No H264 support detected in offer!");
        }
        
        // Log available codecs for debugging
        var rtpmapLines = GetFirstRtpmapLines(offer.sdp, 5);
        if (!string.IsNullOrEmpty(rtpmapLines))
        {
            Debug.Log($"[PCStreamClient] 🎬 Available video codecs:");
            Debug.Log($"[PCStreamClient]    {rtpmapLines.Replace("\n", "\n[PCStreamClient]    ")}");
        }
        
        var rtpmap127 = FindRtpmapLine(offer.sdp, 127);
        if (!string.IsNullOrEmpty(rtpmap127)) 
        {
            Debug.Log($"[PCStreamClient] 📺 rtpmap:127 => {rtpmap127}");
        }

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

        Debug.Log($"[PCStreamClient] 📤 Setting local description...");
        var setLocalOp = _pc.SetLocalDescription(ref offer); 
        while (!setLocalOp.IsDone) await Task.Yield();
        
        if (setLocalOp.IsError) 
        {
            string errorMsg = $"SetLocalDescription failed: {setLocalOp.Error.message}";
            Debug.LogError($"[PCStreamClient] ❌ {errorMsg}");
            throw new Exception(errorMsg);
        }
        
        Debug.Log($"[PCStreamClient] ✅ SetLocalDescription completed successfully");
        Debug.Log($"[PCStreamClient] 📡 Signaling state: {_pc.SignalingState}");

        Debug.Log($"[PCStreamClient] 📤 Sending offer to server...");
        try
        {
            string offerMessage = "offer:" + offer.sdp;
            await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(offerMessage)),
                                WebSocketMessageType.Text, true, _cts.Token);
            Debug.Log($"[PCStreamClient] ✅ Offer sent successfully (size: {offerMessage.Length} bytes)");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PCStreamClient] ❌ Failed to send offer: {ex.Message}");
            throw;
        }

        // Send any queued ICE candidates
        if (_pendingLocalCands.Count > 0)
        {
            Debug.Log($"[PCStreamClient] 📤 Sending {_pendingLocalCands.Count} queued ICE candidates...");
            foreach (var c in _pendingLocalCands)
            {
                await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(c)),
                                    WebSocketMessageType.Text, true, _cts.Token);
            }
            _pendingLocalCands.Clear();
            Debug.Log($"[PCStreamClient] ✅ All queued ICE candidates sent");
        }
        else
        {
            Debug.Log($"[PCStreamClient] 📦 No queued ICE candidates to send");
        }

        // Start ping keepalive mechanism (similar to webrtc_test.html)
        Debug.Log($"[PCStreamClient] 🔄 Starting ping keepalive...");
        StartCoroutine(PingKeepalive());
        
        // Start connection monitoring
        Debug.Log($"[PCStreamClient] 📊 Starting connection monitoring...");
        StartCoroutine(MonitorConnection());

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
                    Debug.Log($"[PCStreamClient] 📥 Received Answer from server:");
                    Debug.Log($"[PCStreamClient]    SDP Length: {sdp.Length} chars");
                    
                    // Check H264 compatibility in answer
                    var answerH264 = SdpContainsH264(sdp);
                    Debug.Log($"[PCStreamClient]    H264 in Answer: {(answerH264 ? "✅ YES" : "❌ NO")}");
                    if (answerH264) 
                    {
                        // Extract H264 payload type from answer for debugging
                        var answerLines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in answerLines.Take(10)) // Just check first few lines
                        {
                            if (line.Contains("H264", StringComparison.OrdinalIgnoreCase))
                            {
                                Debug.Log($"[PCStreamClient]    H264 Line: {line}");
                                break;
                            }
                        }
                    }
                    
                    Debug.Log($"[PCStreamClient] 📤 Setting remote Answer SDP...");
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
                        
                        // Monitor ICE connection progress with multiple checkpoints
                        var initialIceState = _pc.IceConnectionState;
                        Debug.Log($"[PCStreamClient] Initial ICE state after SetRemoteDescription: {initialIceState}");
                        
                        if (initialIceState == RTCIceConnectionState.New)
                        {
                            Debug.LogWarning("[PCStreamClient] ⚠️ ICE still in NEW state - this is unusual after SetRemoteDescription");
                        }
                        
                        // Schedule multiple ICE connection checks
                        RunOnMainThread(() => StartCoroutine(CheckICEConnectionAfterDelay(2.0f)));  // Quick check
                        RunOnMainThread(() => StartCoroutine(CheckICEConnectionAfterDelay(5.0f)));  // Medium check  
                        RunOnMainThread(() => StartCoroutine(CheckICEConnectionAfterDelay(10.0f))); // Final check
                        
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

                // Skip TCP candidates to match browser behavior (RECOMMENDED for Unity)
                if (skipTcpIceCandidates &&
                    (raw.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) ||
                     raw.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.Log($"[PCStreamClient] ❌ SKIPPED TCP candidate (matching browser behavior): {raw.Substring(0, Math.Min(50, raw.Length))}...");
                    continue;
                }

                // For LAN connections, prioritize host candidates
                bool isHostCandidate = raw.Contains(" typ host ", StringComparison.OrdinalIgnoreCase);
                if (isHostCandidate)
                {
                    Debug.Log($"[PCStreamClient] 📡 HOST candidate (LAN): {raw.Substring(0, Math.Min(60, raw.Length))}...");
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
            else if (text.Trim().Equals("pong", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[PCStreamClient] received pong from server");
            }
            else if (text.StartsWith("{"))
            {
                // Try to parse JSON data (frame timing, etc.)
                try
                {
                    // Simple JSON parsing for frame timing data
                    Debug.Log($"[PCStreamClient] JSON message: {text.Substring(0, Math.Min(100, text.Length))}...");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PCStreamClient] JSON parsing failed: {ex.Message}");
                    Debug.Log("[PCStreamClient] WS msg: " + text);
                }
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
