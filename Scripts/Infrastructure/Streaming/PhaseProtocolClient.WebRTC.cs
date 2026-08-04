using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Core;
using VRWorkspace.Native;

namespace VRWorkspace.Streaming
{
    public partial class PhaseProtocolClient
    {
        private bool _allMonitorsReadyFired;

        // ── Per-Track Video PeerConnections (Multi-PC mode) ───────────────────
        // When _perTrackPcMode = true, each monitor gets its own RTCPeerConnection
        // for video (server-created offer). Main PC retains audio + cursor DCs only.
        // When _perTrackPcMode = false, ALL behavior is IDENTICAL to legacy code path.
        private readonly Dictionary<int, RTCPeerConnection> _videoPcs = new Dictionary<int, RTCPeerConnection>();
        private bool _perTrackPcMode;
        public bool PerTrackPcMode => _perTrackPcMode;

        /// <summary>
        /// Fires when a per-track video PC generates a local ICE candidate.
        /// Parameter: (monitorIndex, candidate)
        /// Only fires when _perTrackPcMode = true.
        /// </summary>
        public event Action<int, RTCIceCandidate> OnVideoIceCandidate;

        /// <summary>
        /// Enable or disable per-track video PeerConnection mode.
        /// Must be called BEFORE CreatePeerConnectionsAsync.
        /// When false (default): legacy single-PC multi-track path (unchanged).
        /// When true: main PC carries audio+cursor only; each monitor gets its own video PC.
        /// </summary>
        public void SetPerTrackPcMode(bool enabled)
        {
            _perTrackPcMode = enabled;
            AppLog.Log($"[PhaseProtocol] PerTrackPcMode set to: {enabled}");
        }

        /// <summary>
        /// Handle a video offer from the server for a specific monitor in per-track mode.
        /// Creates a new RTCPeerConnection, sets the remote offer, creates and returns an answer SDP.
        /// The answer SDP should be sent back to the server via WebSocket.
        /// Also stores the PC in _videoPcs[monitorIndex] and updates the wrapper's VideoPc field.
        /// </summary>
        public async Task<string> HandleVideoOfferAsync(int monitorIndex, string offerSdp)
        {
            if (!_perTrackPcMode)
            {
                Debug.LogError("[PhaseProtocol] HandleVideoOfferAsync called but _perTrackPcMode=false");
                return null;
            }

            try
            {
                AppLog.Log($"[PhaseProtocol] HandleVideoOfferAsync: monitorIndex={monitorIndex}, sdp.Length={offerSdp?.Length ?? 0}");

                // Close existing video PC for this monitor if any
                if (_videoPcs.TryGetValue(monitorIndex, out var oldVpc))
                {
                    try { oldVpc.Close(); oldVpc.Dispose(); } catch { }
                    _videoPcs.Remove(monitorIndex);
                }

                // Create new PeerConnection using server-provided ICE configuration
                var cfg = GetRTCConfiguration();
                var videoPc = new RTCPeerConnection(ref cfg);
                AppLog.Log($"[PhaseProtocol] Video PC created for monitor {monitorIndex}");

                // Wire ICE candidate → fire OnVideoIceCandidate event
                int capturedMonitor = monitorIndex;
                videoPc.OnIceCandidate = cand =>
                {
                    if (cand == null) return;
                    OnVideoIceCandidate?.Invoke(capturedMonitor, cand);
                };

                videoPc.OnIceConnectionChange = s =>
                {
                    if (s == RTCIceConnectionState.Failed || s == RTCIceConnectionState.Disconnected)
                        AppLog.LogWarning($"[PhaseProtocol] Video PC{capturedMonitor} ICE state: {s}");
                };

                // ── Per-track H265 receiver: create BEFORE DC opens so frames are never lost ──
                // In per-track mode, video PCs have NO RTP tracks (only DCs), so the OnTrack
                // callback never fires. We must create H265StreamReceiver here instead.
                PCWrapper perTrackWrapper = null;
                lock (_lock)
                {
                    if (capturedMonitor < _peerConnections.Count)
                        perTrackWrapper = _peerConnections[capturedMonitor];
                }

                if ((_selectedCodec == VideoCodec.H265 || _selectedCodec == VideoCodec.H264) && perTrackWrapper != null)
                {
                    bool isH264 = _selectedCodec == VideoCodec.H264;
                    int w = _userConfig?.resolutionWidth ?? 1920;
                    int h = _userConfig?.resolutionHeight ?? 1080;

                    // Cleanup old receiver if exists
                    if (_h265Receivers.TryGetValue(capturedMonitor, out var oldRx))
                    {
                        oldRx.Dispose();
                        _h265Receivers.Remove(capturedMonitor);
                    }

                    var ptReceiver = new H265StreamReceiver(capturedMonitor, w, h, isH264);
                    WireFirstFrameTracking(ptReceiver);
                    if (ptReceiver.Start())
                    {
                        _h265Receivers[capturedMonitor] = ptReceiver;
                        var ptWrapper = perTrackWrapper; // capture for closure
                        ptReceiver.OnTextureReady += (monIdx, tex) =>
                        {
                            ptWrapper.Texture = tex;
                            ptWrapper.LastFrameTime = DateTime.UtcNow;

                            if (ptWrapper.StreamStartTime == DateTime.MinValue)
                                ptWrapper.StreamStartTime = DateTime.UtcNow;

                            if (ptWrapper.WaitingForFirstFrame)
                                ptWrapper.WaitingForFirstFrame = false;

                            if (!_streamingStartedFired)
                            {
                                _streamingStartedFired = true;
                                AppLog.Log($"[PhaseProtocol] Video PC{monIdx} first frame (per-track H265), firing OnStreamingStarted");
                                _stateMachine.TryTransition(ConnectionPhase.Streaming);
                                HandleStreamingStartedInternal();
                            }

                            OnVideoTextureReceived?.Invoke(monIdx, tex);
                        };
                        ptReceiver.OnKeyframeNeeded += monIdx => RequestKeyframe(monIdx);
                        ptReceiver.OnCorruptionDetected += monIdx =>
                        {
                            TaintTrack(monIdx, "luminance corruption detected by per-track decoder");
                        };
                        Debug.Log($"[PhaseProtocol] Video PC{capturedMonitor} H265 receiver created (per-track mode, {w}x{h})");
                    }
                    else
                    {
                        Debug.LogError($"[PhaseProtocol] Video PC{capturedMonitor} H265 receiver failed to start");
                    }
                }

                // Wire OnDataChannel — server creates the h265video DC on its side (it holds the offer)
                videoPc.OnDataChannel = channel =>
                {
                    Debug.Log($"[PhaseProtocol] Video PC{capturedMonitor} server DataChannel: label={channel.Label}");

                    string expectedLabel = $"h265video-{capturedMonitor}";
                    if (channel.Label == expectedLabel)
                    {
                        channel.OnOpen = () =>
                        {
                            Debug.Log($"[PhaseProtocol] Video PC{capturedMonitor} h265video DC opened (per-track mode)");
                            // Initialize LastFrameTime so auto-heal doesn't fire prematurely
                            if (perTrackWrapper != null)
                            {
                                perTrackWrapper.LastFrameTime = DateTime.UtcNow;
                                perTrackWrapper.LastNetworkActivityTime = DateTime.UtcNow;
                            }
                            // Request initial frame from server — ensures display even on idle desktops.
                            // Server resets InitialFrameSent + forces keyframe for this monitor.
                            _ = SendTextAsync($"{{\"type\":\"request_initial_frame\",\"monitorIndex\":{capturedMonitor}}}");
                            Debug.Log($"[PhaseProtocol] Video PC{capturedMonitor} requested initial frame from server");
                        };
                        channel.OnClose = () =>
                            Debug.Log($"[PhaseProtocol] Video PC{capturedMonitor} h265video DC closed (per-track mode)");
                        channel.OnMessage = bytes =>
                            HandleH265VideoFromDataChannel(bytes);

                        // Also store in _h265VideoChannels for consistency
                        _h265VideoChannels[capturedMonitor] = channel;
                        Debug.Log($"[PhaseProtocol] Video PC{capturedMonitor} h265video DC wired (per-track mode)");

                        // Request initial frame immediately when DC is wired (not OnOpen).
                        // Unity WebRTC may fire OnOpen BEFORE OnDataChannel callback completes,
                        // causing OnOpen handler to be missed. Sending here guarantees delivery.
                        _ = SendTextAsync($"{{\"type\":\"request_initial_frame\",\"monitorIndex\":{capturedMonitor}}}");
                        Debug.Log($"[PhaseProtocol] Video PC{capturedMonitor} requested initial frame from server");
                    }
                    else
                    {
                        Debug.LogWarning($"[PhaseProtocol] Video PC{capturedMonitor} unexpected DC label: {channel.Label} (expected {expectedLabel})");
                    }
                };

                // Set remote description (server's offer)
                var fixedSdp = FixSdp(offerSdp);
                var remoteOffer = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = fixedSdp };
                var setRemoteOp = videoPc.SetRemoteDescription(ref remoteOffer);
                while (!setRemoteOp.IsDone)
                    await Task.Yield();

                if (setRemoteOp.IsError)
                {
                    Debug.LogError($"[PhaseProtocol] Video PC{monitorIndex} SetRemoteDescription failed: {setRemoteOp.Error.message}");
                    videoPc.Close();
                    videoPc.Dispose();
                    return null;
                }
                AppLog.Log($"[PhaseProtocol] Video PC{monitorIndex} remote offer set OK");

                // Create answer
                var answerOp = videoPc.CreateAnswer();
                while (!answerOp.IsDone)
                    await Task.Yield();

                if (answerOp.IsError)
                {
                    Debug.LogError($"[PhaseProtocol] Video PC{monitorIndex} CreateAnswer failed");
                    videoPc.Close();
                    videoPc.Dispose();
                    return null;
                }

                // Set local description
                var answer = answerOp.Desc;
                var setLocalOp = videoPc.SetLocalDescription(ref answer);
                while (!setLocalOp.IsDone)
                    await Task.Yield();

                if (setLocalOp.IsError)
                {
                    Debug.LogError($"[PhaseProtocol] Video PC{monitorIndex} SetLocalDescription failed");
                    videoPc.Close();
                    videoPc.Dispose();
                    return null;
                }
                AppLog.Log($"[PhaseProtocol] Video PC{monitorIndex} local answer set OK");

                // Store video PC — _videoPcs[monitorIndex] is the authoritative reference.
                // The PCWrapper.PC field continues to point to the shared main PC (audio+cursor).
                // Callers needing the video PC for monitor N should use _videoPcs[N].
                _videoPcs[monitorIndex] = videoPc;
                AppLog.Log($"[PhaseProtocol] Video PC{monitorIndex} stored in _videoPcs dictionary");

                // Flush any ICE candidates that arrived before the video PC was created
                if (_pendingVideoIceCandidates.TryGetValue(monitorIndex, out var pendingCands))
                {
                    AppLog.Log($"[PhaseProtocol] Video PC{monitorIndex} flushing {pendingCands.Count} pending ICE candidates");
                    foreach (var pCand in pendingCands)
                    {
                        try { videoPc.AddIceCandidate(pCand); }
                        catch (Exception ex) { AppLog.LogWarning($"[PhaseProtocol] Video PC{monitorIndex} pending ICE failed: {ex.Message}"); }
                    }
                    _pendingVideoIceCandidates.Remove(monitorIndex);
                }

                AppLog.Log($"[PhaseProtocol] Video PC{monitorIndex} ready, returning answer SDP ({answer.sdp?.Length ?? 0} chars)");
                return answer.sdp;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] HandleVideoOfferAsync failed for monitor {monitorIndex}: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Add a remote ICE candidate to the video PeerConnection for the specified monitor.
        /// Only valid in per-track mode. Queues candidates if video PC isn't created yet.
        /// </summary>
        // Pending ICE candidates for video PCs that haven't been created yet
        private readonly Dictionary<int, List<RTCIceCandidate>> _pendingVideoIceCandidates = new Dictionary<int, List<RTCIceCandidate>>();

        public void AddVideoIceCandidate(int monitorIndex, RTCIceCandidate candidate)
        {
            if (!_perTrackPcMode) return;

            if (_videoPcs.TryGetValue(monitorIndex, out var vpc))
            {
                try
                {
                    vpc.AddIceCandidate(candidate);
                }
                catch (Exception ex)
                {
                    AppLog.LogWarning($"[PhaseProtocol] Video PC{monitorIndex} AddIceCandidate failed: {ex.Message}");
                }
            }
            else
            {
                // Queue candidate — video PC is still being created by HandleVideoOfferAsync
                if (!_pendingVideoIceCandidates.TryGetValue(monitorIndex, out var pending))
                {
                    pending = new List<RTCIceCandidate>();
                    _pendingVideoIceCandidates[monitorIndex] = pending;
                }
                pending.Add(candidate);
            }
        }

        /// <summary>
        /// Close and dispose all per-track video PeerConnections.
        /// Called during cleanup or session teardown.
        /// </summary>
        public void CloseAllVideoPcs()
        {
            foreach (var kvp in _videoPcs)
            {
                try
                {
                    kvp.Value.Close();
                    kvp.Value.Dispose();
                    AppLog.Log($"[PhaseProtocol] Video PC{kvp.Key} closed");
                }
                catch (Exception ex)
                {
                    AppLog.LogWarning($"[PhaseProtocol] Error closing video PC{kvp.Key}: {ex.Message}");
                }
            }
            _videoPcs.Clear();
            AppLog.Log("[PhaseProtocol] All video PCs closed");
        }

        /// <summary>
        /// Create PeerConnections in background so receive loop can process incoming messages.
        /// </summary>
        private async Task CreatePeerConnectionsInBackgroundAsync(int count)
        {
            try
            {
                await CreatePeerConnectionsAsync(count);
                AppLog.Log("[PhaseProtocol] CreatePeerConnectionsAsync completed successfully");
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
            AppLog.Log($"[PhaseProtocol] Creating SINGLE PeerConnection with {count} video transceivers (Single-PC Multi-Track mode)");

            _expectedMonitorCount = count;

            // Initialize event-driven answer waiting
            _allAnswersReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Create single PC with N transceivers
            await CreateSinglePCMultiTrackAsync(count);
        }

        /// <summary>
        /// Create a single PeerConnection with N video transceivers.
        /// Produces a single offer with N m= sections.
        /// When _perTrackPcMode = true: creates main PC with audio+cursor only (no video transceivers,
        /// no h265video DCs). Video PCs are created separately via HandleVideoOfferAsync.
        /// When _perTrackPcMode = false: IDENTICAL to legacy behavior.
        /// </summary>
        private async Task CreateSinglePCMultiTrackAsync(int count)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string modeLabel = _perTrackPcMode ? "Per-Track Multi-PC" : "Single-PC Multi-Track";
            AppLog.Log($"[PhaseProtocol] CreateSinglePCMultiTrackAsync: Creating PeerConnection for {count} monitors ({modeLabel} mode)...");

            // Create PeerConnection using server-provided ICE configuration
            var cfg = GetRTCConfiguration();
            var pc = new RTCPeerConnection(ref cfg);
            AppLog.Log($"[PhaseProtocol] PeerConnection created: {pc != null}, SignalingState={pc?.SignalingState}");

            // Create wrapper for each track (for texture/frame tracking)
            var trackWrappers = new List<PCWrapper>();
            for (int i = 0; i < count; i++)
            {
                var wrapper = new PCWrapper
                {
                    Index = i,
                    PC = pc, // All wrappers share the same main PC
                    AnswerReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                };
                trackWrappers.Add(wrapper);
            }

            // ── Legacy mode: add video transceivers to main PC ───────────────────
            // Per-track mode: video PCs created via HandleVideoOfferAsync — skip this block.
            var transceivers = new List<RTCRtpTransceiver>();
            if (!_perTrackPcMode)
            {
                // Add N video transceivers (RecvOnly)
                for (int i = 0; i < count; i++)
                {
                    var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
                    SetCodecPreferences(trans, i);
                    transceivers.Add(trans);
                    trackWrappers[i].Mid = trans.Mid; // Store MID for stats matching
                    AppLog.Log($"[PhaseProtocol] Added transceiver {i} for monitor {i}, mid={trans.Mid}");
                }
            }
            else
            {
                AppLog.Log("[PhaseProtocol] Per-track mode: skipping video transceivers on main PC (video handled by per-monitor video PCs)");
            }

            // Add RecvOnly audio transceiver to receive Opus RTP from server.
            // Audio goes through RTP/UDP on the same ICE connection as video — no SCTP involvement,
            // no head-of-line blocking from H.265 video DataChannel traffic.
            var audioTrans = pc.AddTransceiver(TrackKind.Audio);
            audioTrans.Direction = RTCRtpTransceiverDirection.RecvOnly;
            AppLog.Log("[PhaseProtocol] Added RecvOnly audio transceiver to main PC (RTP Opus)");

            // DataChannel for audio (fallback if RTP audio negotiation fails)
            _sctpInitChannel = pc.CreateDataChannel("audio");
            _sctpInitChannel.OnMessage = bytes =>
            {
                OnAudioDataReceived?.Invoke(bytes);
            };
            _sctpInitChannel.OnOpen = () => AppLog.Log("[PhaseProtocol] Audio DataChannel opened");
            _sctpInitChannel.OnClose = () => AppLog.Log("[PhaseProtocol] Audio DataChannel closed");
            AppLog.Log("[PhaseProtocol] Audio DataChannel created (fallback)");

            // Client creates "cursor" DataChannel for low-latency cursor position updates.
            // Server sends binary cursor position (19 bytes) through this channel (UDP-like latency).
            _cursorChannel = pc.CreateDataChannel("cursor");
            _cursorChannel.OnMessage = bytes => HandleCursorFromDataChannel(bytes);
            _cursorChannel.OnOpen = () => AppLog.Log("[PhaseProtocol] Cursor DataChannel opened");
            _cursorChannel.OnClose = () => AppLog.Log("[PhaseProtocol] Cursor DataChannel closed");
            AppLog.Log("[PhaseProtocol] Cursor via DataChannel (low-latency binary)");

            // Client→Server input DC for BT mouse/keyboard forwarding
            _inputChannel = pc.CreateDataChannel("input");
            _inputChannel.OnOpen = () => AppLog.Log("[PhaseProtocol] Input DataChannel opened (mouse/keyboard forwarding)");
            _inputChannel.OnClose = () => AppLog.Log("[PhaseProtocol] Input DataChannel closed");
            AppLog.Log("[PhaseProtocol] Input DataChannel created");

            // ── Legacy mode: create per-track h265video DCs on main PC ───────────
            // Per-track mode: h265video DCs live on the per-monitor video PCs — skip this block.
            if (!_perTrackPcMode)
            {
                // Per-track DataChannels for H.265 video frames.
                // Each track gets its own DC → own SCTP buffer → no cross-track congestion.
                // e.g., Track 1 (browser video) can't starve Track 0 (VSCode) by filling shared buffer.
                var h265VideoInit = new RTCDataChannelInit
                {
                    ordered = false,
                    maxRetransmits = 0
                };
                _h265VideoChannels.Clear();
                for (int t = 0; t < count; t++)
                {
                    string label = $"h265video-{t}";
                    var ch = pc.CreateDataChannel(label, h265VideoInit);
                    int capturedTrack = t; // capture for closure
                    ch.OnMessage = bytes => HandleH265VideoFromDataChannel(bytes);
                    ch.OnOpen = () =>
                    {
                        AppLog.Log($"[PhaseProtocol] H265 Video DataChannel opened: {label} (unreliable, unordered)");
                        // BUGFIX (single-PC mode 1-monitor stuck): without this, the server's
                        // DC `ondatachannel` may fire its `RequestKeyframe` AFTER the capture
                        // loop has already started. If the desktop is idle, the capture loop
                        // skips encode (FrameChangeDecision), and the next "desktop changed"
                        // event may not arrive for a long time on a static screen → client
                        // never receives an IDR → OnStreamingStarted never fires → cluster rig
                        // never created → "stuck after connected" symptom.
                        // Per-track PC mode already does this at HandleVideoOfferAsync; mirror it here.
                        PCWrapper wrapperForDc = null;
                        lock (_lock)
                        {
                            if (capturedTrack < _peerConnections.Count)
                                wrapperForDc = _peerConnections[capturedTrack];
                        }
                        if (wrapperForDc != null)
                        {
                            wrapperForDc.LastFrameTime = DateTime.UtcNow;
                            wrapperForDc.LastNetworkActivityTime = DateTime.UtcNow;
                        }
                        _ = SendTextAsync($"{{\"type\":\"request_initial_frame\",\"monitorIndex\":{capturedTrack}}}");
                        AppLog.Log($"[PhaseProtocol] PC{capturedTrack} requested initial frame from server (single-PC mode)");
                    };
                    ch.OnClose = () => AppLog.Log($"[PhaseProtocol] H265 Video DataChannel closed: {label}");
                    _h265VideoChannels[t] = ch;

                    // Request initial frame immediately when DC is wired (not OnOpen).
                    // Unity WebRTC may fire OnOpen BEFORE the OnMessage/OnOpen callbacks are
                    // fully installed, causing OnOpen handler to be missed. Sending here
                    // guarantees the server receives the request even if OnOpen races.
                    // Server-side handler (Phase3.cs:536-562) resets InitialFrameSent and
                    // calls RequestKeyframe(force=true), so the next captured frame becomes an IDR.
                    _ = SendTextAsync($"{{\"type\":\"request_initial_frame\",\"monitorIndex\":{t}}}");
                    AppLog.Log($"[PhaseProtocol] PC{t} requested initial frame from server (single-PC mode, immediate)");
                }
                AppLog.Log($"[PhaseProtocol] Created {count} per-track H265 Video DataChannels (unreliable, unordered)");
            }
            else
            {
                AppLog.Log("[PhaseProtocol] Per-track mode: h265video DCs will be wired by HandleVideoOfferAsync per monitor");
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
            AppLog.Log($"[PhaseProtocol] Single-PC offer sent with {count} m= sections ({sw.ElapsedMilliseconds}ms)");

            // Audio now goes through main PC as RTP track (not separate Audio PC).
            // RTP audio on the same ICE connection is NOT affected by SCTP congestion
            // from H.265 video DataChannel — RTP and SCTP are independent transports.

            // Wait for answer with timeout
            const int ANSWER_TIMEOUT_MS = 10000;
            try
            {
                var timeoutTask = Task.Delay(ANSWER_TIMEOUT_MS);
                var completedTask = await Task.WhenAny(_allAnswersReceivedTcs.Task, timeoutTask);

                if (completedTask == _allAnswersReceivedTcs.Task)
                {
                    AppLog.Log($"[PhaseProtocol] Single-PC answer received in {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    AppLog.LogWarning($"[PhaseProtocol] Single-PC answer timeout after {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[PhaseProtocol] Answer waiting exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a separate PeerConnection dedicated to audio DataChannel.
        /// This gives audio its own SCTP association, completely isolated from
        /// H.265 video DataChannel traffic on the main PC. Eliminates SCTP
        /// congestion-induced audio delay (~1s → ~30ms).
        /// </summary>
        private async Task CreateAudioPeerConnectionAsync()
        {
            try
            {
                var cfg = GetRTCConfiguration();
                _audioPc = new RTCPeerConnection(ref cfg);
                AppLog.Log("[PhaseProtocol] Audio PeerConnection created (RTP Opus transport)");

                // Add recvonly audio transceiver to receive Opus RTP from server.
                // Server sends Opus via _audioPc.SendAudio() — goes through ICE/DTLS/UDP,
                // completely bypassing SCTP (which is broken on Unity WebRTC for audio PC).
                var transceiver = _audioPc.AddTransceiver(TrackKind.Audio);
                transceiver.Direction = RTCRtpTransceiverDirection.RecvOnly;
                AppLog.Log("[PhaseProtocol] Audio PC: added recvonly audio transceiver for Opus RTP");

                // OnTrack: receive audio MediaStreamTrack from server
                _audioPc.OnTrack = e =>
                {
                    if (e.Track is AudioStreamTrack audioTrack)
                    {
                        AppLog.Log($"[PhaseProtocol] Audio PC OnTrack: received audio track (RTP Opus, dedicated PC)");
                        OnAudioTrackReceived?.Invoke(audioTrack);
                    }
                    else
                    {
                        AppLog.LogWarning($"[PhaseProtocol] Audio PC OnTrack: unexpected track kind={e.Track?.Kind}");
                    }
                };

                // ICE candidate handling
                _audioPc.OnIceCandidate = cand =>
                {
                    if (cand == null || string.IsNullOrEmpty(cand.Candidate)) return;
                    var raw = cand.Candidate;
                    if (!raw.StartsWith("candidate:")) raw = "candidate:" + raw;
                    _ = SendTextAsync($"{{\"type\":\"audio_candidate\",\"candidate\":\"{EscapeJsonString(raw)}\"}}");
                };

                _audioPc.OnIceConnectionChange = state =>
                {
                    AppLog.Log($"[PhaseProtocol] Audio PC ICE state: {state}");
                };

                // Create and send offer
                var offerOp = _audioPc.CreateOffer();
                while (!offerOp.IsDone) await Task.Yield();
                if (offerOp.IsError) { Debug.LogError("[PhaseProtocol] Audio PC CreateOffer failed"); return; }

                var offer = offerOp.Desc;
                var setLocalOp = _audioPc.SetLocalDescription(ref offer);
                while (!setLocalOp.IsDone) await Task.Yield();
                if (setLocalOp.IsError) { Debug.LogError("[PhaseProtocol] Audio PC SetLocal failed"); return; }

                await SendTextAsync($"{{\"type\":\"audio_offer\",\"sdp\":\"{EscapeJsonString(offer.sdp)}\"}}");
                AppLog.Log("[PhaseProtocol] Audio PC offer sent (RTP Opus transport)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Audio PC creation failed (non-fatal, fallback to main PC audio): {ex.Message}");
            }
        }

        /// <summary>
        /// Handle answer for the dedicated audio PeerConnection.
        /// </summary>
        private async Task HandleAudioAnswerAsync(SimpleJson json)
        {
            if (_audioPc == null) return;
            try
            {
                string sdp = json.GetString("sdp");
                if (string.IsNullOrEmpty(sdp)) { Debug.LogError("[PhaseProtocol] Audio answer has empty SDP"); return; }

                // Log raw SDP for debugging
                AppLog.Log($"[PhaseProtocol] Audio answer SDP ({sdp.Length} chars):\n{sdp}");

                // Use the same FixSdp() as main PC to normalize line endings,
                // remove empty lines, and ensure trailing \r\n — required by libwebrtc
                sdp = FixSdp(sdp);

                // Ensure setup:active (not actpass) for answerer per RFC 5763
                sdp = sdp.Replace("a=setup:actpass", "a=setup:active");

                AppLog.Log($"[PhaseProtocol] Audio answer fixed SDP ({sdp.Length} chars):\n{sdp}");

                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var op = _audioPc.SetRemoteDescription(ref answer);
                while (!op.IsDone) await Task.Yield();

                if (op.IsError)
                {
                    Debug.LogError("[PhaseProtocol] Audio PC SetRemoteDescription failed");
                }
                else
                {
                    AppLog.Log("[PhaseProtocol] Audio PC answer applied successfully");

                    // Flush any audio ICE candidates that arrived before answer was applied
                    _audioAnswerApplied = true;
                    List<RTCIceCandidateInit> pending;
                    lock (_pendingAudioRemoteCandidates)
                    {
                        pending = new List<RTCIceCandidateInit>(_pendingAudioRemoteCandidates);
                        _pendingAudioRemoteCandidates.Clear();
                    }
                    if (pending.Count > 0)
                    {
                        AppLog.Log($"[PhaseProtocol] Flushing {pending.Count} buffered audio ICE candidates");
                        foreach (var init in pending)
                        {
                            _audioPc.AddIceCandidate(new RTCIceCandidate(init));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Audio answer handling failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle ICE candidate for the dedicated audio PeerConnection.
        /// </summary>
        private void HandleAudioCandidate(SimpleJson json)
        {
            if (_audioPc == null) return;
            try
            {
                string candStr = json.GetString("candidate");
                if (string.IsNullOrEmpty(candStr)) return;
                if (!candStr.StartsWith("candidate:")) candStr = "candidate:" + candStr;

                var init = new RTCIceCandidateInit
                {
                    candidate = candStr,
                    sdpMLineIndex = 0,
                    sdpMid = "0"
                };

                if (!_audioAnswerApplied)
                {
                    // Buffer until SetRemoteDescription completes (fire-and-forget race)
                    lock (_pendingAudioRemoteCandidates)
                    {
                        _pendingAudioRemoteCandidates.Add(init);
                    }
                    AppLog.Log($"[PhaseProtocol] Audio ICE candidate buffered (answer pending, {_pendingAudioRemoteCandidates.Count} queued)");
                    return;
                }

                _audioPc.AddIceCandidate(new RTCIceCandidate(init));
                AppLog.Log($"[PhaseProtocol] Audio ICE candidate added: {candStr.Substring(0, Math.Min(60, candStr.Length))}...");
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[PhaseProtocol] Audio ICE candidate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Setup event handlers for Single-PC Multi-Track mode.
        /// Maps tracks to monitors via transceiver index.
        /// </summary>
        private void SetupSinglePCEventHandlers(RTCPeerConnection pc, List<PCWrapper> trackWrappers, List<RTCRtpTransceiver> transceivers)
        {
            AppLog.Log($"[PhaseProtocol] Setting up Single-PC event handlers for {trackWrappers.Count} tracks");
            int gen = _pcGeneration; // Capture generation to guard against stale callbacks

            pc.OnIceConnectionChange = s =>
            {
                if (_pcGeneration != gen) return; // Stale PC callback after cleanup
                AppLog.Log($"[PhaseProtocol] Single-PC ICE: {s}");

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
                    // Only fire ICE complete once — ICE may re-enter Connected/Completed
                    // state on consent checks, which would duplicate-fire the event.
                    if (!_allMonitorsReadyFired)
                    {
                        for (int i = 0; i < trackWrappers.Count; i++)
                            OnMonitorIceComplete?.Invoke(i);
                        CheckAllMonitorsConnected();
                    }
                }
                else if (s == RTCIceConnectionState.Disconnected || s == RTCIceConnectionState.Failed)
                {
                    if (_isIntentionalDisconnect) return; // User pressed Disconnect — don't auto-reconnect

                    // ICE dropped — OnConnectionStateChange may fire late or not at all.
                    // Give ICE 3s to recover, then trigger reconnect directly.
                    int capturedGen = _pcGeneration;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        if (_isIntentionalDisconnect) return; // User disconnected during grace period
                        if (_pcGeneration != capturedGen) return; // PC was already replaced
                        if (!_stateMachine.IsStreaming) return;
                        var currentIce = pc.IceConnectionState;
                        if (currentIce == RTCIceConnectionState.Connected || currentIce == RTCIceConnectionState.Completed)
                            return; // ICE recovered on its own
                        if (trackWrappers[0].IsReconnecting) return; // Already reconnecting

                        AppLog.LogWarning($"[PhaseProtocol] Single-PC ICE still {currentIce} after 3s grace, triggering reconnect");
                        trackWrappers[0].IsReconnecting = true;
                        _ = ReconnectSinglePCAsync();
                    });
                }
            };

            pc.OnConnectionStateChange = s =>
            {
                if (_pcGeneration != gen) return; // Stale PC callback after cleanup
                AppLog.Log($"[PhaseProtocol] Single-PC State: {s}");
                if (s == RTCPeerConnectionState.Connected)
                {
                    foreach (var w in trackWrappers)
                    {
                        w.LastConnectedTime = DateTime.UtcNow;
                        w.IsReconnecting = false;
                        w.ReconnectAttempts = 0;
                        w.LastDecoderStallRecoveryTime = DateTime.UtcNow; // Grace period for stall detection
                        w.WaitingForFirstFrame = true; // Suppress stall detection until first frame
                    }
                    _metrics.ResetStallCount();
                    ResetStallStrikes();
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    if (_stateMachine.IsStreaming && !trackWrappers[0].IsReconnecting)
                    {
                        trackWrappers[0].IsReconnecting = true;
                        AppLog.Log("[PhaseProtocol] Single-PC initiating full reconnect...");
                        _ = ReconnectSinglePCAsync();
                    }
                }
            };

            // ICE candidates - single connection, no monitorIndex
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate))
                {
                    AppLog.Log("[PhaseProtocol] Single-PC local ICE gathering complete (empty candidate)");
                    if (trackWrappers[0].OfferSent)
                        _ = SendTextAsync("{\"type\":\"end_of_candidates\",\"monitorIndex\":0}");
                    return;
                }

                string msg = cand.Candidate;
                AppLog.Log($"[PhaseProtocol] Single-PC local ICE candidate: {msg.Substring(0, Math.Min(60, msg.Length))}...");

                if (_skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                {
                    AppLog.Log("[PhaseProtocol] Skipping TCP candidate");
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
                var mid = e.Transceiver?.Mid ?? "null";
                AppLog.Log($"[PhaseProtocol] Single-PC OnTrack: kind={e.Track?.Kind}, enabled={e.Track?.Enabled}, mid={mid}");

                if (e.Track is VideoStreamTrack v)
                {
                    // Find which transceiver this track belongs to
                    int trackIndex = -1;
                    for (int i = 0; i < transceivers.Count; i++)
                    {
                        if (transceivers[i] == e.Transceiver)
                        {
                            trackIndex = i;
                            trackWrappers[i].Mid = mid; // Store MID for stats matching
                            break;
                        }
                    }

                    if (trackIndex < 0 || trackIndex >= trackWrappers.Count)
                    {
                        AppLog.LogWarning($"[PhaseProtocol] Received track for unknown transceiver, mid={mid}");
                        return;
                    }

                    var wrapper = trackWrappers[trackIndex];
                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow;

                    int idx = trackIndex; // Capture for closure

                    if (_selectedCodec == VideoCodec.H265 || _selectedCodec == VideoCodec.H264)
                    {
                        bool isH264 = _selectedCodec == VideoCodec.H264;
                        string codecName = isH264 ? "H264" : "H265";
                        AppLog.Log($"[PhaseProtocol] PC{idx} using {codecName} custom decoder pipeline via DataChannel (Single-PC mode)");

                        // Initialize receiver with current config dimensions
                        int w = _userConfig?.resolutionWidth ?? 1920;
                        int h = _userConfig?.resolutionHeight ?? 1080;

                        // Cleanup old receiver/handler if they exist for this index
                        if (_h265Receivers.TryGetValue(idx, out var oldReceiver))
                        {
                            AppLog.Log($"[PhaseProtocol] Cleaning up old receiver for PC{idx}");
                            oldReceiver.Dispose();
                            _h265Receivers.Remove(idx);
                        }
                        if (_h265Handlers.TryGetValue(idx, out var oldHandler))
                        {
                            oldHandler.Dispose();
                            _h265Handlers.Remove(idx);
                        }

                        var receiver = new H265StreamReceiver(idx, w, h, isH264);
                        WireFirstFrameTracking(receiver);
                        if (receiver.Start())
                        {
                            _h265Receivers[idx] = receiver;
                            receiver.OnTextureReady += (monIdx, tex) => {
                                wrapper.Texture = tex;
                                wrapper.LastFrameTime = DateTime.UtcNow;

                                if (wrapper.StreamStartTime == DateTime.MinValue)
                                    wrapper.StreamStartTime = DateTime.UtcNow;

                                if (!_streamingStartedFired)
                                {
                                    AppLog.Log($"[PhaseProtocol] PC{idx} received first frame ({codecName}), firing OnStreamingStarted");
                                    _stateMachine.TryTransition(ConnectionPhase.Streaming);
                                    HandleStreamingStartedInternal();
                                }

                                OnVideoTextureReceived?.Invoke(monIdx, tex);
                            };
                            receiver.OnKeyframeNeeded += monIdx => RequestKeyframe(monIdx);
                            receiver.OnCorruptionDetected += monIdx =>
                            {
                                TaintTrack(monIdx, "luminance corruption detected by decoder");
                            };

                            // Hook into Encoded Transform (H265 only — Unity WebRTC Encoded Transform
                            // doesn't fire for standard H264 RTP, and H264 video goes via DC anyway)
                            if (!isH264)
                            {
                                try {
                                    var handler = new H265EncodedFrameHandler(receiver);
                                    _h265Handlers[idx] = handler;
                                    e.Transceiver.Receiver.Transform = handler.Transform;

                                    AppLog.Log($"[PhaseProtocol] PC{idx} hooked H265 custom decoder via Encoded Transform (Transform set: {e.Transceiver.Receiver.Transform != null})");
                                } catch (Exception ex) {
                                    Debug.LogError($"[PhaseProtocol] PC{idx} failed to hook H265 Transform: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Standard WebRTC pipeline
                        v.OnVideoReceived += tex =>
                        {
                            wrapper.Texture = tex;
                            wrapper.LastFrameTime = DateTime.UtcNow;
                            wrapper.FrameCount++;
                            wrapper.RenderedFrameCount++;
                            wrapper.TotalFramesReceived++;

                            if (wrapper.StreamStartTime == DateTime.MinValue)
                                wrapper.StreamStartTime = DateTime.UtcNow;

                            if (!_streamingStartedFired)
                            {
                                _streamingStartedFired = true;
                                AppLog.Log($"[PhaseProtocol] Track {idx} received first frame, firing OnStreamingStarted");
                                _stateMachine.TryTransition(ConnectionPhase.Streaming);
                                OnStreamingStarted?.Invoke();
                            }

                            OnVideoTextureReceived?.Invoke(idx, tex);
                        };
                        AppLog.Log($"[PhaseProtocol] Track {trackIndex} attached to standard OnVideoReceived callback");
                    }
                }
                else if (e.Track is AudioStreamTrack audioTrack)
                {
                    AppLog.Log($"[PhaseProtocol] Received audio track, mid={mid}");
                    OnAudioTrackReceived?.Invoke(audioTrack);
                }
            };

            // Fallback: handle server-created DataChannels (if any)
            pc.OnDataChannel = channel =>
            {
                AppLog.Log($"[PhaseProtocol] Server DataChannel received: label={channel.Label}");
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
            AppLog.Log($"[PhaseProtocol] All {count} offers sent in parallel ({sw.ElapsedMilliseconds}ms)");

            // Event-driven wait for answers (no polling!) with timeout
            const int TOTAL_ANSWER_TIMEOUT_MS = 10000; // 10 seconds for ALL answers

            try
            {
                // Wait for TCS to be signaled OR timeout
                var timeoutTask = Task.Delay(TOTAL_ANSWER_TIMEOUT_MS);
                var completedTask = await Task.WhenAny(_allAnswersReceivedTcs.Task, timeoutTask);

                if (completedTask == _allAnswersReceivedTcs.Task)
                {
                    AppLog.Log($"[PhaseProtocol] All {count} answers received in {sw.ElapsedMilliseconds}ms (event-driven success)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[PhaseProtocol] Answer waiting exception: {ex.Message}");
            }

            // Check final state on timeout
            int finalAnswers;
            lock (_lock)
            {
                finalAnswers = _peerConnections.Count(p => p.AnswerSet);
            }

            AppLog.LogWarning($"[PhaseProtocol] Parallel timeout after {sw.ElapsedMilliseconds}ms: {finalAnswers}/{count} answers received");
            return finalAnswers >= count;
        }

        /// <summary>
        /// Create a single PC with offer - used by parallel creation.
        /// Fire-and-forget pattern - doesn't wait for answer.
        /// </summary>
        private async Task CreateSinglePCAsync(int idx)
        {
            // Create PeerConnection using server-provided ICE configuration
            var cfg = GetRTCConfiguration();
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
            AppLog.Log($"[PhaseProtocol] PC{idx} offer sent (parallel)");

            // Mark offer as sent and flush queued candidates
            wrapper.OfferSent = true;
            if (wrapper.QueuedCandidates.Count > 0)
            {
                AppLog.Log($"[PhaseProtocol] PC{idx} flushing {wrapper.QueuedCandidates.Count} queued ICE candidates");
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
            AppLog.Log($"[PhaseProtocol] Sequential fallback for {count} PCs");

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
                        AppLog.Log($"[PhaseProtocol] PC{idx} answer received in {sw.ElapsedMilliseconds}ms (sequential event-driven)");
                    else
                        AppLog.LogWarning($"[PhaseProtocol] PC{idx} answer timeout after {sw.ElapsedMilliseconds}ms (sequential)");
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
                    // VP9 → H264 → H265 → VP8 → others
                    preferredCodecs = vp9.Concat(h264).Concat(h265).Concat(vp8).Concat(others).ToArray();
                    break;

                case VideoCodec.VP8:
                    // VP8 → H264 → VP9 → H265 → others
                    preferredCodecs = vp8.Concat(h264).Concat(vp9).Concat(h265).Concat(others).ToArray();
                    break;

                case VideoCodec.H264:
                default:
                    // H264 → H265 → VP9 → VP8 → others
                    preferredCodecs = h264.Concat(h265).Concat(vp9).Concat(vp8).Concat(others).ToArray();
                    break;
            }

            AppLog.Log($"[PhaseProtocol] PC{idx} codec preferences: {_selectedCodec} first, total {preferredCodecs.Length} codecs");
            trans.SetCodecPreferences(preferredCodecs);
        }

        /// <summary>
        /// Setup event handlers for a PeerConnection.
        /// </summary>
        private void SetupPCEventHandlers(RTCPeerConnection pc, PCWrapper wrapper, int idx)
        {
            int gen = _pcGeneration; // Capture generation to guard against stale callbacks

            pc.OnIceConnectionChange = s =>
            {
                if (_pcGeneration != gen) return; // Stale PC callback after cleanup
                AppLog.Log($"[PhaseProtocol] PC{idx} ICE: {s}");

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
                if (_pcGeneration != gen) return; // Stale PC callback after cleanup
                AppLog.Log($"[PhaseProtocol] PC{idx} State: {s}");
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
                        AppLog.Log($"[PhaseProtocol] PC{idx} initiating auto-heal...");
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
                    AppLog.Log($"[PhaseProtocol] PC{idx} queued ICE candidate (offer not sent yet)");
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
                    AppLog.Log($"[PhaseProtocol] PC{idx} OnTrack: mid={mid}, trackId={trackId}");

                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow;

                    // Capture mid for callback logging
                    var capturedMid = mid;

                    if (_selectedCodec == VideoCodec.H265 || _selectedCodec == VideoCodec.H264)
                    {
                        bool isH264 = _selectedCodec == VideoCodec.H264;
                        string codecName = isH264 ? "H264" : "H265";
                        AppLog.Log($"[PhaseProtocol] PC{idx} using {codecName} custom decoder pipeline via DataChannel");

                        // Initialize receiver with current config dimensions
                        int w = _userConfig?.resolutionWidth ?? 1920;
                        int h = _userConfig?.resolutionHeight ?? 1080;
                        var receiver = new H265StreamReceiver(idx, w, h, isH264);
                        WireFirstFrameTracking(receiver);
                        if (receiver.Start())
                        {
                            _h265Receivers[idx] = receiver;
                            receiver.OnTextureReady += (monIdx, tex) => {
                                wrapper.Texture = tex;
                                wrapper.LastFrameTime = DateTime.UtcNow;

                                if (wrapper.StreamStartTime == DateTime.MinValue)
                                    wrapper.StreamStartTime = DateTime.UtcNow;

                                if (!_streamingStartedFired)
                                {
                                    _streamingStartedFired = true;
                                    AppLog.Log($"[PhaseProtocol] PC{idx} received first frame ({codecName}), firing OnStreamingStarted as backup");
                                    _stateMachine.TryTransition(ConnectionPhase.Streaming);
                                    OnStreamingStarted?.Invoke();
                                }

                                OnVideoTextureReceived?.Invoke(monIdx, tex);
                            };
                            receiver.OnKeyframeNeeded += monIdx => RequestKeyframe(monIdx);
                            receiver.OnCorruptionDetected += monIdx =>
                            {
                                TaintTrack(monIdx, "luminance corruption detected by decoder");
                            };

                            // Encoded Transform: H265 only (not needed for H264 DC path)
                            if (!isH264)
                            {
                                try {
                                    var handler = new H265EncodedFrameHandler(receiver);
                                    _h265Handlers[idx] = handler;
                                    e.Transceiver.Receiver.Transform = handler.Transform;

                                    AppLog.Log($"[PhaseProtocol] PC{idx} hooked H265 custom decoder via Encoded Transform");
                                } catch (Exception ex) {
                                    Debug.LogError($"[PhaseProtocol] PC{idx} failed to hook H265 Transform: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Standard WebRTC pipeline
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
                                AppLog.Log($"[PhaseProtocol] PC{idx} OnVideoReceived mid={capturedMid}, frame={wrapper.FrameCount}, tex={tex?.width}x{tex?.height}");
                            }

                            // Fire OnStreamingStarted on first frame if not already fired
                            if (!_streamingStartedFired)
                            {
                                _streamingStartedFired = true;
                                AppLog.Log($"[PhaseProtocol] PC{idx} received first frame, firing OnStreamingStarted as backup");
                                _stateMachine.TryTransition(ConnectionPhase.Streaming);
                                OnStreamingStarted?.Invoke();
                            }

                            OnVideoTextureReceived?.Invoke(idx, tex);
                        };
                    }
                    AppLog.Log($"[PhaseProtocol] PC{idx} received video track, mid={mid}");
                }
                else if (e.Track is AudioStreamTrack audioTrack)
                {
                    AppLog.Log($"[PhaseProtocol] PC{idx} received audio track, mid={e.Transceiver?.Mid}");
                    OnAudioTrackReceived?.Invoke(audioTrack);
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
            AppLog.Log($"[PhaseProtocol] PC{monitorIndex} raw SDP preview: {rawPreview}");

            var sdp = FixSdp(rawSdp);

            AppLog.Log($"[PhaseProtocol] PC{monitorIndex} received answer (SDP: {rawSdp.Length} -> {sdp.Length} bytes)");

            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count)
                {
                    AppLog.LogWarning($"[PhaseProtocol] PC{monitorIndex} answer ignored: index out of range");
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
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} SignalingState={wrapper.PC.SignalingState}, IceState={wrapper.PC.IceConnectionState}");
                if (wrapper.PC.SignalingState != RTCSignalingState.HaveLocalOffer)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} wrong state: {wrapper.PC.SignalingState}");
                    return;
                }

                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} calling SetRemoteDescription (SDP len={sdp.Length})...");
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var setRemoteOp = wrapper.PC.SetRemoteDescription(ref answer);

                if (setRemoteOp == null)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription returned null!");
                    return;
                }

                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription called, waiting for completion...");

                // Wait for operation to complete (max 5 seconds)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int waitCount = 0;
                while (!setRemoteOp.IsDone && sw.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(10);
                    waitCount++;
                    if (waitCount % 100 == 0) // Log every 1 second
                        AppLog.Log($"[PhaseProtocol] PC{monitorIndex} still waiting... {sw.ElapsedMilliseconds}ms, IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError}");
                }

                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription wait done: IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError}, elapsed={sw.ElapsedMilliseconds}ms");

                if (!setRemoteOp.IsDone || setRemoteOp.IsError)
                {
                    var errorMsg = setRemoteOp.IsError ? setRemoteOp.Error.message ?? "unknown" : "timeout";
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription failed: {errorMsg}");

                    // Debug: Log full SDP on error for analysis
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} Failed SDP (full):\n{sdp}");
                    return;
                }

                wrapper.AnswerSet = true;
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} answer set OK ({sw.ElapsedMilliseconds}ms)");

                // Single-PC mode: Mark ALL wrappers as AnswerSet (they share the same PC)
                // This is safe because all wrappers point to the same PC
                lock (_lock)
                {
                    bool isSinglePCMode = _peerConnections.Count > 1 &&
                                          _peerConnections.All(w => w.PC == wrapper.PC);
                    if (isSinglePCMode)
                    {
                        AppLog.Log("[PhaseProtocol] Single-PC mode: marking all wrappers as AnswerSet");
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
                AppLog.Log($"[PhaseProtocol] All {_expectedMonitorCount} answers received, signaling TCS");
                _allAnswersReceivedTcs.TrySetResult(true);
            }
        }

        private void HandleCandidate(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            var candStr = json.GetString("candidate") ?? "";

            if (_skipTcpIceCandidates && (candStr.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || candStr.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
            {
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} Skipped remote TCP candidate");
                return;
            }

            AppLog.Log($"[PhaseProtocol] PC{monitorIndex} received ICE candidate");

            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count) return;
                wrapper = _peerConnections[monitorIndex];
            }

            if (wrapper.AnswerSet)
            {
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} adding remote ICE candidate (AnswerSet=true)");
                AddIceCandidate(wrapper, candStr);
            }
            else
            {
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} queuing remote ICE candidate (AnswerSet=false, pending={wrapper.PendingIce.Count + 1})");
                wrapper.PendingIce.Add(candStr);
            }
        }

        private void HandleEndOfCandidates(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            AppLog.Log($"[PhaseProtocol] PC{monitorIndex} server ICE complete");
            CheckIceComplete();
        }

        /// <summary>
        /// Handle ice_ready message from server - all ICE connections are established.
        /// This provides a reliable server-side confirmation for transitioning to ReadyToStream.
        /// </summary>
        private void HandleIceReady(SimpleJson json)
        {
            var monitorCount = json.GetInt("monitorCount");
            AppLog.Log($"[PhaseProtocol] Server confirmed {monitorCount} ICE connections ready");

            if (_stateMachine.CurrentPhase == ConnectionPhase.ICENegotiating)
            {
                AppLog.Log("[PhaseProtocol] Transitioning to ReadyToStream (server-initiated via ice_ready)");
                _stateMachine.TryTransition(ConnectionPhase.ReadyToStream);
                _ = SendTextAsync("{\"type\":\"proceed\",\"phase\":3}");
                OnReadyToStream?.Invoke();
            }
            else if (_stateMachine.CurrentPhase == ConnectionPhase.Streaming)
            {
                AppLog.Log("[PhaseProtocol] ice_ready received during Streaming phase (reconnect).");
                // The server is already in Phase 3 or expects a signal if it reset its state.
                // We should make sure we're synchronized. We could send a proceed phase 3 just in case,
                // but we might not need to if the server is already streaming.
                // However, sending start_streaming might be necessary if the server went back to waiting.
                _ = SendTextAsync("{\"type\":\"reconnect_ack\"}"); 
            }
            else
            {
                AppLog.Log($"[PhaseProtocol] ice_ready received but phase is {_stateMachine.CurrentPhase}, ignoring");
            }
        }

        private void AddIceCandidate(PCWrapper wrapper, string candStr)
        {
            try
            {
                var fullCand = candStr.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) ? candStr : "candidate:" + candStr;
                wrapper.PC.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = fullCand, sdpMLineIndex = 0, sdpMid = "0" }));
                AppLog.Log($"[PhaseProtocol] PC{wrapper.Index} Added ICE candidate");
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[PhaseProtocol] PC{wrapper.Index} AddICE error: {ex.Message}");
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
                AppLog.Log($"[PhaseProtocol] CheckIceComplete: {answered}/{created} PCs have answers (expected {expected} total), phase={_stateMachine.CurrentPhase}");

                // IMPORTANT: Wait for ALL expected PCs to be created AND have answers
                // This prevents proceeding too early when creating PCs sequentially
                if (created >= expected && created > 0 && _peerConnections.All(p => p.AnswerSet))
                {
                    if (_stateMachine.CurrentPhase == ConnectionPhase.ICENegotiating)
                    {
                        AppLog.Log($"[PhaseProtocol] All {expected} PeerConnections ready, transitioning to ReadyToStream");
                        _stateMachine.TryTransition(ConnectionPhase.ReadyToStream);
                        shouldSendProceed = true;
                    }
                    else
                    {
                        AppLog.LogWarning($"[PhaseProtocol] All PCs ready but phase is {_stateMachine.CurrentPhase}, not ICENegotiating");
                    }
                }
            }

            // Send proceed message outside of lock
            if (shouldSendProceed)
            {
                AppLog.Log("[PhaseProtocol] Sending proceed message for phase 3");
                _ = SendTextAsync("{\"type\":\"proceed\",\"phase\":3}");
                OnReadyToStream?.Invoke();
            }
        }

        /// <summary>
        /// Check if all monitors are connected and fire OnAllMonitorsReady event.
        /// </summary>
        private void CheckAllMonitorsConnected()
        {
            lock (_lock)
            {
                if (_expectedMonitorCount <= 0 || _allMonitorsReadyFired) return;

                int connected = _peerConnections.Count(p =>
                    p.PC != null &&
                    (p.PC.IceConnectionState == RTCIceConnectionState.Connected ||
                     p.PC.IceConnectionState == RTCIceConnectionState.Completed));

                AppLog.Log($"[PhaseProtocol] CheckAllMonitorsConnected: {connected}/{_expectedMonitorCount}");

                if (connected >= _expectedMonitorCount)
                {
                    _allMonitorsReadyFired = true;
                    AppLog.Log("[PhaseProtocol] All monitors connected, firing OnAllMonitorsReady");
                    OnAllMonitorsReady?.Invoke();
                }
            }
        }

        /// <summary>
        /// Reads ICE servers from _phase2.IceServers and returns a full RTCConfiguration,
        /// falling back to an empty array for LAN/USB modes.
        /// </summary>
        private RTCConfiguration GetRTCConfiguration()
        {
            var servers = (_phase2 != null && _phase2.IceServers != null)
                ? _phase2.IceServers.ToArray()
                : new RTCIceServer[0];

            return new RTCConfiguration { iceServers = servers };
        }
    }
}
