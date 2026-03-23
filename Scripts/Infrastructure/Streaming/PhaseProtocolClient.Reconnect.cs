#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    public partial class PhaseProtocolClient
    {
        /// <summary>
        /// Reconnect a specific monitor by recreating PeerConnection and sending new offer.
        /// Called when server requests reconnect due to connection loss.
        /// Per-track mode: only reconnects the video PC for this monitor (main PC untouched).
        /// Legacy mode: recreates the full per-monitor PeerConnection.
        /// </summary>
        private async Task ReconnectMonitorAsync(int monitorIndex)
        {
            // Don't reconnect if application is shutting down
            if (_cts == null || _cts.IsCancellationRequested) return;

            // ── Per-Track Mode: reconnect only the video PC for this monitor ──────
            if (_perTrackPcMode)
            {
                await ReconnectVideoOnlyAsync(monitorIndex);
                return;
            }

            PCWrapper oldWrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count)
                {
                    AppLog.LogWarning($"[PhaseProtocol] Reconnect ignored: invalid monitor index {monitorIndex}");
                    return;
                }
                oldWrapper = _peerConnections[monitorIndex];
            }

            AppLog.Log($"[PhaseProtocol] Reconnecting PC{monitorIndex} (legacy mode)...");

            // Close old PC
            try { oldWrapper.PC?.Close(); oldWrapper.PC?.Dispose(); } catch { }

            // Create new PeerConnection with STUN servers for better stability
            var iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer { urls = new[] { "stun:stun1.l.google.com:19302" } },
            };
            var cfg = new RTCConfiguration { iceServers = iceServers };
            var pc = new RTCPeerConnection(ref cfg);
            var wrapper = new PCWrapper { Index = monitorIndex, PC = pc };

            int idx = monitorIndex;

            // Add video transceiver with codec preference based on negotiated codec
            var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });

            // Use the shared codec preferences method
            SetCodecPreferences(trans, idx);
            wrapper.Mid = trans.Mid; // Store MID for stats matching

            // Re-create DataChannels (must match initial connection in PhaseProtocolClient.WebRTC.cs)
            // Without these, server has no audio/cursor channels after reconnect
            _sctpInitChannel = pc.CreateDataChannel("audio");
            _sctpInitChannel.OnMessage = bytes => { OnAudioDataReceived?.Invoke(bytes); };
            _sctpInitChannel.OnOpen = () => AppLog.Log("[PhaseProtocol] Audio DataChannel opened (reconnect)");
            _sctpInitChannel.OnClose = () => AppLog.Log("[PhaseProtocol] Audio DataChannel closed (reconnect)");

            _cursorChannel = pc.CreateDataChannel("cursor");
            _cursorChannel.OnMessage = bytes => HandleCursorFromDataChannel(bytes);
            _cursorChannel.OnOpen = () => AppLog.Log("[PhaseProtocol] Cursor DataChannel opened (reconnect)");
            _cursorChannel.OnClose = () => AppLog.Log("[PhaseProtocol] Cursor DataChannel closed (reconnect)");

            _inputChannel = pc.CreateDataChannel("input");
            _inputChannel.OnOpen = () => AppLog.Log("[PhaseProtocol] Input DataChannel opened (reconnect)");
            _inputChannel.OnClose = () => AppLog.Log("[PhaseProtocol] Input DataChannel closed (reconnect)");

            // Re-create per-track H265 video DataChannels
            var h265VideoInit = new RTCDataChannelInit { ordered = false, maxRetransmits = 0 };
            _h265VideoChannels.Clear();
            for (int t = 0; t < _expectedMonitorCount; t++)
            {
                string label = $"h265video-{t}";
                var ch = pc.CreateDataChannel(label, h265VideoInit);
                int capturedTrack = t;
                ch.OnMessage = bytes => HandleH265VideoFromDataChannel(bytes);
                ch.OnOpen = () => AppLog.Log($"[PhaseProtocol] H265 Video DC opened: {label} (reconnect)");
                ch.OnClose = () => AppLog.Log($"[PhaseProtocol] H265 Video DC closed: {label} (reconnect)");
                _h265VideoChannels[t] = ch;
            }
            AppLog.Log($"[PhaseProtocol] PC{idx} DataChannels created (audio+cursor+{_expectedMonitorCount}x h265video) on reconnect");

            // Connection state handlers
            pc.OnIceConnectionChange = s =>
            {
                AppLog.Log($"[PhaseProtocol] PC{idx} ICE (reconnected): {s}");
            };
            pc.OnConnectionStateChange = s =>
            {
                AppLog.Log($"[PhaseProtocol] PC{idx} State (reconnected): {s}");
                if (s == RTCPeerConnectionState.Connected)
                {
                    AppLog.Log($"[PhaseProtocol] PC{idx} reconnect successful!");
                    wrapper.LastConnectedTime = DateTime.UtcNow;
                    wrapper.IsReconnecting = false;
                    wrapper.ReconnectAttempts = 0; // Reset on successful reconnection
                    wrapper.LastDecoderStallRecoveryTime = DateTime.UtcNow; // Grace period for stall detection
                    wrapper.WaitingForFirstFrame = true; // Suppress stall detection until first frame
                    _metrics.ResetStallCount();
                    _metrics.ResetIceDisconnectCount();
                    ResetStallStrikes();

                    // Send reconnect acknowledgment to server
                    _ = SendTextAsync($"{{\"type\":\"reconnect_ack\",\"monitorIndex\":{idx}}}");
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    AppLog.LogWarning($"[PhaseProtocol] PC{idx} connection lost again after reconnect");
                    _metrics.RecordIceDisconnect();

                    // Auto-heal again if still streaming
                    if (_stateMachine.IsStreaming && !wrapper.IsReconnecting)
                    {
                        wrapper.IsReconnecting = true;
                        AppLog.Log($"[PhaseProtocol] PC{idx} re-initiating auto-heal...");
                        _ = AutoHealMonitorAsync(idx);
                    }
                }
            };

            // ICE candidates - queue until offer is sent (same fix as initial connection)
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate))
                {
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

                if (!wrapper.OfferSent)
                {
                    wrapper.QueuedCandidates.Add(candidateJson);
                    AppLog.Log($"[PhaseProtocol] PC{idx} queued ICE candidate on reconnect (offer not sent yet)");
                }
                else
                {
                    _ = SendTextAsync(candidateJson);
                }
            };

            // Track received
            pc.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack v)
                {
                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow; // Initialize

                    if (_selectedCodec == VideoCodec.H265 || _selectedCodec == VideoCodec.H264)
                    {
                        bool isH264 = _selectedCodec == VideoCodec.H264;
                        string codecName = isH264 ? "H264" : "H265";
                        AppLog.Log($"[PhaseProtocol] PC{idx} using {codecName} custom decoder pipeline via DataChannel (reconnect)");

                        int w = _userConfig?.resolutionWidth  ?? 1920;
                        int h = _userConfig?.resolutionHeight ?? 1080;

                        // Cleanup old receiver/handler if they exist for this index
                        if (_h265Receivers.TryGetValue(idx, out var oldReceiver))
                        {
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
                            receiver.OnTextureReady += (monIdx, tex) =>
                            {
                                wrapper.Texture = tex;
                                wrapper.LastFrameTime = DateTime.UtcNow;

                                if (wrapper.StreamStartTime == DateTime.MinValue)
                                    wrapper.StreamStartTime = DateTime.UtcNow;

                                if (!_streamingStartedFired)
                                {
                                    AppLog.Log($"[PhaseProtocol] PC{idx} received first frame ({codecName} reconnect), firing OnStreamingStarted");
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

                            // Encoded Transform: H265 only
                            if (!isH264)
                            {
                                try
                                {
                                    var handler = new H265EncodedFrameHandler(receiver);
                                    _h265Handlers[idx] = handler;
                                    e.Transceiver.Receiver.Transform = handler.Transform;
                                    AppLog.Log($"[PhaseProtocol] PC{idx} hooked H265 custom decoder via Encoded Transform (reconnect)");
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogError($"[PhaseProtocol] PC{idx} failed to hook H265 Transform (reconnect): {ex.Message}");
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

                            // Fire OnStreamingStarted on first frame if not already fired
                            if (!_streamingStartedFired)
                            {
                                _streamingStartedFired = true;
                                AppLog.Log($"[PhaseProtocol] PC{idx} received first frame (reconnected), firing OnStreamingStarted");
                                _stateMachine.TryTransition(ConnectionPhase.Streaming);
                                OnStreamingStarted?.Invoke();
                            }

                            OnVideoTextureReceived?.Invoke(idx, tex);
                        };
                    }
                    AppLog.Log($"[PhaseProtocol] PC{idx} received video track (reconnected)");
                }
            };

            // Replace wrapper
            lock (_lock)
            {
                _peerConnections[monitorIndex] = wrapper;
            }

            // Reset state for new offer
            wrapper.OfferSent = false;
            wrapper.QueuedCandidates.Clear();

            // Create and send new offer
            var offerOp = pc.CreateOffer();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!offerOp.IsDone && sw.ElapsedMilliseconds < 5000)
                await Task.Delay(10);

            if (!offerOp.IsDone || offerOp.IsError)
            {
                Debug.LogError($"[PhaseProtocol] PC{idx} CreateOffer failed on reconnect");
                return;
            }

            var offer = offerOp.Desc;
            var setLocalOp = pc.SetLocalDescription(ref offer);
            sw.Restart();
            while (!setLocalOp.IsDone && sw.ElapsedMilliseconds < 5000)
                await Task.Delay(10);

            if (!setLocalOp.IsDone || setLocalOp.IsError)
            {
                Debug.LogError($"[PhaseProtocol] PC{idx} SetLocal failed on reconnect");
                return;
            }

            // Send offer first
            await SendTextAsync($"{{\"type\":\"offer\",\"monitorIndex\":{idx},\"sdp\":\"{EscapeJsonString(offer.sdp)}\"}}");
            AppLog.Log($"[PhaseProtocol] PC{idx} reconnect offer sent");

            // Mark offer as sent and flush queued candidates
            wrapper.OfferSent = true;
            if (wrapper.QueuedCandidates.Count > 0)
            {
                AppLog.Log($"[PhaseProtocol] PC{idx} flushing {wrapper.QueuedCandidates.Count} queued ICE candidates on reconnect");
                foreach (var candJson in wrapper.QueuedCandidates)
                {
                    _ = SendTextAsync(candJson);
                }
                wrapper.QueuedCandidates.Clear();
            }
        }

        /// <summary>
        /// Per-track mode Level 2 reconnect: close only the video PC for the given monitor,
        /// then ask server to re-offer for that monitor.
        /// Main PC (audio + cursor) and other monitors' video PCs are untouched.
        /// </summary>
        private async Task ReconnectVideoOnlyAsync(int monitorIndex)
        {
            if (_cts == null || _cts.IsCancellationRequested) return;
            AppLog.Log($"[PhaseProtocol] ReconnectVideoOnly: monitor {monitorIndex} (per-track mode)");

            // Close the existing video PC for this monitor
            if (_videoPcs.TryGetValue(monitorIndex, out var oldVpc))
            {
                try { oldVpc.Close(); oldVpc.Dispose(); } catch { }
                _videoPcs.Remove(monitorIndex);
                AppLog.Log($"[PhaseProtocol] ReconnectVideoOnly: closed old video PC for monitor {monitorIndex}");
            }

            // Ask server to send a new video_offer for this monitor.
            // The existing video_offer handler (HandleVideoOfferMessageAsync) will process the response.
            await SendTextAsync($"{{\"type\":\"reconnect_video\",\"monitorIndex\":{monitorIndex}}}");
            AppLog.Log($"[PhaseProtocol] ReconnectVideoOnly: sent reconnect_video for monitor {monitorIndex}, awaiting new video_offer");
        }

        /// <summary>
        /// Reconnect Single-PC Multi-Track mode by recreating the PeerConnection with all transceivers.
        /// Called when the shared PeerConnection fails or disconnects during streaming.
        /// </summary>
        private async Task ReconnectSinglePCAsync()
        {
            // Don't reconnect if application is shutting down
            if (_cts == null || _cts.IsCancellationRequested) return;

            // Get current monitor count
            int count;
            RTCPeerConnection? oldPc;
            lock (_lock)
            {
                count = _peerConnections.Count;
                if (count == 0)
                {
                    AppLog.LogWarning("[PhaseProtocol] ReconnectSinglePC: No monitors to reconnect");
                    return;
                }
                oldPc = _peerConnections[0].PC;
            }

            // Increment reconnect generation to invalidate any in-flight answers from previous reconnects
            int gen = ++_reconnectGeneration;
            AppLog.Log($"[PhaseProtocol] ReconnectSinglePC: Reconnecting {count} monitors (gen={gen})...");

            // Close old PeerConnection
            try
            {
                oldPc?.Close();
                oldPc?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.LogWarning($"[PhaseProtocol] Error closing old PC: {ex.Message}");
            }

            // Per-track mode: also close all video PCs for full re-signaling
            if (_perTrackPcMode)
            {
                CloseAllVideoPcs();
                AppLog.Log($"[PhaseProtocol] ReconnectSinglePC (per-track): closed all video PCs, awaiting new video_offers");
            }

            // Check if a newer reconnect superseded us during disposal
            if (_reconnectGeneration != gen)
            {
                AppLog.LogWarning($"[PhaseProtocol] ReconnectSinglePC gen={gen} superseded by gen={_reconnectGeneration}, aborting");
                return;
            }

            // Reset answer TCS for new session
            _allAnswersReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Recreate everything using the same method as initial connection
            // Per-track mode: CreateSinglePCMultiTrackAsync creates main PC (audio+cursor only);
            // video PCs will be created via HandleVideoOfferMessageAsync when server sends new video_offers.
            await CreateSinglePCMultiTrackAsync(count);

            // Check again after async operation — a newer reconnect may have started
            if (_reconnectGeneration != gen)
            {
                AppLog.LogWarning($"[PhaseProtocol] ReconnectSinglePC gen={gen} superseded after PC creation, aborting");
                return;
            }

            AppLog.Log("[PhaseProtocol] ReconnectSinglePC: Reconnection initiated, waiting for answer...");
        }

        /// <summary>
        /// Graduated recovery for WiFi stalls. Escalates through 3 steps:
        /// 1. Keyframe burst (3 consecutive I-frames) — resolves most WiFi stalls
        /// 2. Skip-to-live + keyframe burst — handles accumulated decoder delay
        /// 3. Full PeerConnection reconnect — last resort
        /// </summary>
        private async Task GraduatedRecoveryAsync(int monitorIndex)
        {
            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count) return;
                wrapper = _peerConnections[monitorIndex];
                if (wrapper.IsInGraduatedRecovery || wrapper.IsReconnecting) return;
                wrapper.IsInGraduatedRecovery = true;
            }

            try
            {
                // USB: single keyframe only — SCTP congestion window is small after reconnect,
                // flooding with IDR bursts (30-50KB each) makes congestion WORSE and causes
                // a destructive stall→reconnect→stall cycle.
                // WiFi: more aggressive burst (5 I-frames for redundancy against packet loss)
                int burstCount = _isUsbMode ? 1 : (_isWiFiConnection ? 5 : 3);

                // STEP 1: Keyframe burst
                wrapper.GraduatedRecoveryStep = 1;
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} graduated step 1: keyframe burst (count={burstCount})");
                await SendTextAsync($"{{\"type\":\"request_keyframe_burst\",\"monitorIndex\":{monitorIndex},\"count\":{burstCount}}}");

                // Wait for frames to resume (USB needs longer for SCTP to deliver)
                int step1Delay = _isUsbMode ? 1500 : (_isWiFiConnection ? 600 : 300);
                await Task.Delay(step1Delay, _cts!.Token);

                // Verify recovery using GROUND TRUTH (Decoder advance time)
                var timeSinceDecoderAdvance = (DateTime.UtcNow - wrapper.LastDecoderAdvanceTime).TotalMilliseconds;
                // 500ms window: new SSRC after reconnect needs time for jitter buffer init
                if (timeSinceDecoderAdvance < 500)
                {
                    AppLog.Log($"[PhaseProtocol] PC{monitorIndex} recovered at step 1 (decoder advanced {timeSinceDecoderAdvance:F0}ms ago)");
                    return;
                }

                // STEP 2: Skip-to-live + keyframe burst (handles accumulated buffer delay)
                wrapper.GraduatedRecoveryStep = 2;
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} graduated step 2: skip + keyframe burst (count={burstCount})");
                SkipToLiveImmediate(monitorIndex);
                await Task.Delay(50);
                await SendTextAsync($"{{\"type\":\"request_keyframe_burst\",\"monitorIndex\":{monitorIndex},\"count\":{burstCount}}}");
                int step2Delay = _isUsbMode ? 2000 : (_isWiFiConnection ? 800 : 400);
                await Task.Delay(step2Delay, _cts.Token);

                timeSinceDecoderAdvance = (DateTime.UtcNow - wrapper.LastDecoderAdvanceTime).TotalMilliseconds;
                if (timeSinceDecoderAdvance < 500)
                {
                    AppLog.Log($"[PhaseProtocol] PC{monitorIndex} recovered at step 2!");
                    return;
                }

                // STEP 3: Full reconnect (last resort)
                wrapper.GraduatedRecoveryStep = 3;
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} graduated step 2 failed, triggering reconnection...");
                
                bool isSinglePC = false;
                lock (_lock) { isSinglePC = _peerConnections.Count > 1 && _peerConnections.All(w => w.PC == wrapper.PC); }

                if (isSinglePC)
                {
                    AppLog.Log($"[PhaseProtocol] PC{monitorIndex} graduated step 3: full Single-PC reconnect");
                    wrapper.IsReconnecting = true;
                    _ = ReconnectSinglePCAsync();
                }
                else
                {
                    AppLog.Log($"[PhaseProtocol] PC{monitorIndex} graduated step 3: monitor reconnect");
                    wrapper.IsReconnecting = true;
                    await AutoHealMonitorAsync(monitorIndex);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                wrapper.IsInGraduatedRecovery = false;
                wrapper.GraduatedRecoveryStep = 0;
            }
        }

        /// <summary>
        /// Client-side auto-heal: Detect disconnection and automatically attempt reconnect.
        /// This runs independently of server's reconnect request for faster recovery.
        /// Uses exponential backoff: 2s, 4s, 8s, 16s, 32s between attempts.
        /// </summary>
        private async Task AutoHealMonitorAsync(int monitorIndex)
        {
            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count) return;
                wrapper = _peerConnections[monitorIndex];
            }

            // Check max attempts - trigger dialog instead of just giving up
            if (wrapper.ReconnectAttempts >= PCWrapper.MaxReconnectAttempts)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} auto-heal: max attempts ({PCWrapper.MaxReconnectAttempts}) reached");
                wrapper.IsReconnecting = false;

                // Instead of just giving up, trigger dialog for user to decide
                OnReconnectFailed?.Invoke(new[] { "Retry", "Restart Session", "Disconnect" });
                return;
            }

            // Fast linear backoff for VR: 500ms, 1s, 1.5s, 2s, 2.5s (VR needs fast recovery)
            int delayMs = 500 + (wrapper.ReconnectAttempts * 500);
            wrapper.ReconnectAttempts++;
            AppLog.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: attempt {wrapper.ReconnectAttempts}/{PCWrapper.MaxReconnectAttempts}, waiting {delayMs}ms...");
            await Task.Delay(delayMs);

            // Check if we're still streaming and need reconnect
            if (_cts == null || _cts.IsCancellationRequested) return;
            if (!_stateMachine.IsStreaming)
            {
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: no longer streaming, skip reconnect");
                wrapper.IsReconnecting = false;
                return;
            }

            // Check if frames are now flowing (frame stall recovered naturally)
            var timeSinceFrame = DateTime.UtcNow - wrapper.LastFrameTime;
            if (timeSinceFrame.TotalMilliseconds < 2000) // Frames flowing within last 2s
            {
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: frames recovered ({timeSinceFrame.TotalMilliseconds:F0}ms since last frame), skip reconnect");
                wrapper.IsReconnecting = false;
                wrapper.ReconnectAttempts = 0; // Reset on success
                return;
            }

            // Check PC connection state.
            // Per-track mode: check the video PC for this monitor, not the shared main PC.
            RTCPeerConnectionState state;
            if (_perTrackPcMode && _videoPcs.TryGetValue(monitorIndex, out var videoPC))
                state = videoPC.ConnectionState;
            else
                state = wrapper.PC?.ConnectionState ?? RTCPeerConnectionState.Closed;

            // If PC is connected but frames stalled, we still need to reconnect
            // This handles the case where WebRTC connection is fine but video stopped
            if (state == RTCPeerConnectionState.Connected)
            {
                AppLog.LogWarning($"[PhaseProtocol] PC{monitorIndex} auto-heal: PC Connected but frames stalled for {timeSinceFrame.TotalMilliseconds:F0}ms - forcing reconnect");
            }
            else
            {
                AppLog.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: PC state={state}, initiating reconnect...");
            }

            // Per-track mode: always reconnect video PC only (Level 2), never full PC
            if (_perTrackPcMode)
            {
                try
                {
                    AppLog.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal (per-track): reconnecting video PC only");
                    await ReconnectVideoOnlyAsync(monitorIndex);
                    // reconnect_video has been sent; server will respond with video_offer.
                    // Reset flag so wrapper doesn't get stuck — video PC connection state
                    // is tracked separately in _videoPcs[monitorIndex].
                    wrapper.IsReconnecting = false;
                    wrapper.ReconnectAttempts = 0;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} auto-heal (per-track) failed: {ex.Message}");
                    wrapper.IsReconnecting = false;
                }
                return;
            }

            // If multiple monitors share same PC (Single-PC mode), we MUST reconnect the whole PC
            // because server ignores per-monitor offers (index > 0) in Phase 3.
            bool isSinglePC = false;
            lock (_lock)
            {
                isSinglePC = _peerConnections.Count > 1 && _peerConnections.All(w => w.PC == wrapper.PC);
            }

            try
            {
                if (isSinglePC)
                {
                    AppLog.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: Triggering full Single-PC multi-track reconnect");
                    await ReconnectSinglePCAsync();
                }
                else
                {
                    await ReconnectMonitorAsync(monitorIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} auto-heal failed: {ex.Message}");
                wrapper.IsReconnecting = false;
            }
        }

        /// <summary>
        /// Reconnect entire session - preserves user config but re-runs Phase 2 (ICE negotiation).
        /// Called when individual track reconnects have failed and user chooses "Restart Session",
        /// or when server requests a reconnect with a specific codec (e.g. H265 -> H264 fallback).
        /// </summary>
        public async Task ReconnectSessionAsync(string? suggestedCodec = null)
        {
            AppLog.Log($"[PhaseProtocol] Starting full session reconnect (suggestedCodec={suggestedCodec ?? "none"})...");

            // 1. Close all PeerConnections (main + per-track video PCs)
            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    try
                    {
                        wrapper.PC?.Close();
                        wrapper.PC?.Dispose();
                    }
                    catch { }
                }
                _peerConnections.Clear();
            }

            if (_perTrackPcMode)
            {
                CloseAllVideoPcs();
                SetPerTrackPcMode(false); // Reset — re-enabled when config_complete arrives during Phase 2 restart
                AppLog.Log("[PhaseProtocol] ReconnectSession: closed all video PCs, reset perTrackPcMode");
            }

            // 2. Reset metrics
            _metrics.ResetAll();
            _streamingStartedFired = false;

            // 3. Transition to reconnecting state
            _stateMachine.TryTransition(ConnectionPhase.Reconnecting);

            // 4. Re-run Phase 2 (ICE negotiation) with preserved config
            if (_userConfig != null && _ws?.State == WebSocketState.Open)
            {
                AppLog.Log("[PhaseProtocol] Requesting Phase 2 restart with existing config");

                // Send restart request to server
                await SendTextAsync("{\"type\":\"restart_phase2\"}");

                // Server will respond with setup_complete, then we do ICE again
                OnSessionReconnectRequested?.Invoke();
            }
            else
            {
                Debug.LogError("[PhaseProtocol] Cannot reconnect - no config or WebSocket closed");
                _stateMachine.ForceTransition(ConnectionPhase.Error, "Reconnect failed - no connection");
                OnError?.Invoke("Cannot reconnect - connection lost");
            }
        }

        /// <summary>
        /// Retry track reconnect after user clicks "Retry" in dialog.
        /// Resets attempt counters and restarts auto-heal for all monitors.
        /// </summary>
        public void RetryReconnect()
        {
            AppLog.Log("[PhaseProtocol] User requested retry - resetting reconnect counters");

            lock (_lock)
            {
                foreach (var wrapper in _peerConnections)
                {
                    wrapper.ReconnectAttempts = 0;
                    wrapper.IsReconnecting = false;
                }
            }

            // Trigger auto-heal for any disconnected monitors
            List<PCWrapper> wrappers;
            lock (_lock)
            {
                wrappers = _peerConnections.ToList();
            }

            foreach (var wrapper in wrappers)
            {
                var state = wrapper.PC?.ConnectionState ?? RTCPeerConnectionState.Closed;
                if (state != RTCPeerConnectionState.Connected && !wrapper.IsReconnecting)
                {
                    wrapper.IsReconnecting = true;
                    _ = AutoHealMonitorAsync(wrapper.Index);
                }
            }
        }
    }
}
