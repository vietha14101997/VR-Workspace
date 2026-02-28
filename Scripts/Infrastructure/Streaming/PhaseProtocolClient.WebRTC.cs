using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    public partial class PhaseProtocolClient
    {
        /// <summary>
        /// Create PeerConnections in background so receive loop can process incoming messages.
        /// </summary>
        private async Task CreatePeerConnectionsInBackgroundAsync(int count)
        {
            try
            {
                await CreatePeerConnectionsAsync(count);
                Debug.Log("[PhaseProtocol] CreatePeerConnectionsAsync completed successfully");
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
            Debug.Log($"[PhaseProtocol] Creating SINGLE PeerConnection with {count} video transceivers (Single-PC Multi-Track mode)");

            _expectedMonitorCount = count;

            // Initialize event-driven answer waiting
            _allAnswersReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Create single PC with N transceivers
            await CreateSinglePCMultiTrackAsync(count);
        }

        /// <summary>
        /// Create a single PeerConnection with N video transceivers.
        /// Produces a single offer with N m= sections.
        /// </summary>
        private async Task CreateSinglePCMultiTrackAsync(int count)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"[PhaseProtocol] CreateSinglePCMultiTrackAsync: Creating PeerConnection for {count} monitors...");

            // EMPTY ICE servers - no STUN for LAN mode
            var cfg = new RTCConfiguration { iceServers = new RTCIceServer[0] };
            var pc = new RTCPeerConnection(ref cfg);
            Debug.Log($"[PhaseProtocol] PeerConnection created: {pc != null}, SignalingState={pc?.SignalingState}");

            // Create wrapper for each track (for texture/frame tracking)
            var trackWrappers = new List<PCWrapper>();
            for (int i = 0; i < count; i++)
            {
                var wrapper = new PCWrapper
                {
                    Index = i,
                    PC = pc, // All wrappers share the same PC
                    AnswerReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                };
                trackWrappers.Add(wrapper);
            }

            // Add N video transceivers (RecvOnly)
            var transceivers = new List<RTCRtpTransceiver>();
            for (int i = 0; i < count; i++)
            {
                var trans = pc.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly });
                SetCodecPreferences(trans, i);
                transceivers.Add(trans);
                Debug.Log($"[PhaseProtocol] Added transceiver {i} for monitor {i}");
            }

            // Client creates "audio" DataChannel (SCTP) to bypass NetEQ jitter buffer.
            // Client-created DC ensures proper SCTP negotiation (libwebrtc manages SCTP natively).
            // Server receives this DC via ondatachannel and sends Opus frames through it.
            _sctpInitChannel = pc.CreateDataChannel("audio");
            _sctpInitChannel.OnMessage = bytes =>
            {
                OnAudioDataReceived?.Invoke(bytes);
            };
            _sctpInitChannel.OnOpen = () => Debug.Log("[PhaseProtocol] Audio DataChannel opened");
            _sctpInitChannel.OnClose = () => Debug.Log("[PhaseProtocol] Audio DataChannel closed");
            Debug.Log("[PhaseProtocol] Audio via DataChannel (client-created, no RTP audio transceiver)");

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
            Debug.Log($"[PhaseProtocol] Single-PC offer sent with {count} m= sections ({sw.ElapsedMilliseconds}ms)");

            // Wait for answer with timeout
            const int ANSWER_TIMEOUT_MS = 10000;
            try
            {
                var timeoutTask = Task.Delay(ANSWER_TIMEOUT_MS);
                var completedTask = await Task.WhenAny(_allAnswersReceivedTcs.Task, timeoutTask);

                if (completedTask == _allAnswersReceivedTcs.Task)
                {
                    Debug.Log($"[PhaseProtocol] Single-PC answer received in {sw.ElapsedMilliseconds}ms");
                }
                else
                {
                    Debug.LogWarning($"[PhaseProtocol] Single-PC answer timeout after {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Answer waiting exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Setup event handlers for Single-PC Multi-Track mode.
        /// Maps tracks to monitors via transceiver index.
        /// </summary>
        private void SetupSinglePCEventHandlers(RTCPeerConnection pc, List<PCWrapper> trackWrappers, List<RTCRtpTransceiver> transceivers)
        {
            Debug.Log($"[PhaseProtocol] Setting up Single-PC event handlers for {trackWrappers.Count} tracks");
            int gen = _pcGeneration; // Capture generation to guard against stale callbacks

            pc.OnIceConnectionChange = s =>
            {
                if (_pcGeneration != gen) return; // Stale PC callback after cleanup
                Debug.Log($"[PhaseProtocol] Single-PC ICE: {s}");

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
                    for (int i = 0; i < trackWrappers.Count; i++)
                        OnMonitorIceComplete?.Invoke(i);
                    CheckAllMonitorsConnected();
                }
            };

            pc.OnConnectionStateChange = s =>
            {
                if (_pcGeneration != gen) return; // Stale PC callback after cleanup
                Debug.Log($"[PhaseProtocol] Single-PC State: {s}");
                if (s == RTCPeerConnectionState.Connected)
                {
                    foreach (var w in trackWrappers)
                    {
                        w.LastConnectedTime = DateTime.UtcNow;
                        w.IsReconnecting = false;
                        w.ReconnectAttempts = 0;
                    }
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    if (_stateMachine.IsStreaming && !trackWrappers[0].IsReconnecting)
                    {
                        trackWrappers[0].IsReconnecting = true;
                        Debug.Log("[PhaseProtocol] Single-PC initiating full reconnect...");
                        _ = ReconnectSinglePCAsync();
                    }
                }
            };

            // ICE candidates - single connection, no monitorIndex
            pc.OnIceCandidate = cand =>
            {
                if (string.IsNullOrEmpty(cand.Candidate))
                {
                    Debug.Log("[PhaseProtocol] Single-PC local ICE gathering complete (empty candidate)");
                    if (trackWrappers[0].OfferSent)
                        _ = SendTextAsync("{\"type\":\"end_of_candidates\",\"monitorIndex\":0}");
                    return;
                }

                string msg = cand.Candidate;
                Debug.Log($"[PhaseProtocol] Single-PC local ICE candidate: {msg.Substring(0, Math.Min(60, msg.Length))}...");

                if (_skipTcpIceCandidates && (msg.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || msg.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.Log("[PhaseProtocol] Skipping TCP candidate");
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
                Debug.Log($"[PhaseProtocol] Single-PC OnTrack: kind={e.Track?.Kind}, enabled={e.Track?.Enabled}");

                if (e.Track is VideoStreamTrack v)
                {
                    // Find which transceiver this track belongs to
                    int trackIndex = -1;
                    for (int i = 0; i < transceivers.Count; i++)
                    {
                        if (transceivers[i] == e.Transceiver)
                        {
                            trackIndex = i;
                            break;
                        }
                    }

                    if (trackIndex < 0 || trackIndex >= trackWrappers.Count)
                    {
                        Debug.LogWarning($"[PhaseProtocol] Received track for unknown transceiver, mid={e.Transceiver?.Mid}");
                        return;
                    }

                    var wrapper = trackWrappers[trackIndex];
                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow;

                    int idx = trackIndex; // Capture for closure
                    v.OnVideoReceived += tex =>
                    {
                        wrapper.Texture = tex;
                        wrapper.LastFrameTime = DateTime.UtcNow;
                        wrapper.FrameCount++;
                        wrapper.RenderedFrameCount++;
                        wrapper.TotalFramesReceived++; // Cumulative counter (never reset)

                        // Track stream start time
                        if (wrapper.StreamStartTime == DateTime.MinValue)
                            wrapper.StreamStartTime = DateTime.UtcNow;

                        if (!_streamingStartedFired)
                        {
                            _streamingStartedFired = true;
                            Debug.Log($"[PhaseProtocol] Track {idx} received first frame, firing OnStreamingStarted");
                            _stateMachine.TryTransition(ConnectionPhase.Streaming);
                            OnStreamingStarted?.Invoke();
                        }

                        OnVideoTextureReceived?.Invoke(idx, tex);
                    };

                    Debug.Log($"[PhaseProtocol] Track {trackIndex} received video, mid={e.Transceiver?.Mid}");
                }
                else if (e.Track is AudioStreamTrack audioTrack)
                {
                    Debug.Log($"[PhaseProtocol] Received audio track, mid={e.Transceiver?.Mid}");
                    OnAudioTrackReceived?.Invoke(audioTrack);
                }
            };

            // Fallback: handle server-created DataChannels (if any)
            pc.OnDataChannel = channel =>
            {
                Debug.Log($"[PhaseProtocol] Server DataChannel received: label={channel.Label}");
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
            Debug.Log($"[PhaseProtocol] All {count} offers sent in parallel ({sw.ElapsedMilliseconds}ms)");

            // Event-driven wait for answers (no polling!) with timeout
            const int TOTAL_ANSWER_TIMEOUT_MS = 10000; // 10 seconds for ALL answers

            try
            {
                // Wait for TCS to be signaled OR timeout
                var timeoutTask = Task.Delay(TOTAL_ANSWER_TIMEOUT_MS);
                var completedTask = await Task.WhenAny(_allAnswersReceivedTcs.Task, timeoutTask);

                if (completedTask == _allAnswersReceivedTcs.Task)
                {
                    Debug.Log($"[PhaseProtocol] All {count} answers received in {sw.ElapsedMilliseconds}ms (event-driven success)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Answer waiting exception: {ex.Message}");
            }

            // Check final state on timeout
            int finalAnswers;
            lock (_lock)
            {
                finalAnswers = _peerConnections.Count(p => p.AnswerSet);
            }

            Debug.LogWarning($"[PhaseProtocol] Parallel timeout after {sw.ElapsedMilliseconds}ms: {finalAnswers}/{count} answers received");
            return finalAnswers >= count;
        }

        /// <summary>
        /// Create a single PC with offer - used by parallel creation.
        /// Fire-and-forget pattern - doesn't wait for answer.
        /// </summary>
        private async Task CreateSinglePCAsync(int idx)
        {
            // EMPTY ICE servers (like browser) - no STUN lookup delay!
            // Browser: new RTCPeerConnection({ iceServers: [], iceCandidatePoolSize: 0 })
            var cfg = new RTCConfiguration { iceServers = new RTCIceServer[0] };
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
            Debug.Log($"[PhaseProtocol] PC{idx} offer sent (parallel)");

            // Mark offer as sent and flush queued candidates
            wrapper.OfferSent = true;
            if (wrapper.QueuedCandidates.Count > 0)
            {
                Debug.Log($"[PhaseProtocol] PC{idx} flushing {wrapper.QueuedCandidates.Count} queued ICE candidates");
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
            Debug.Log($"[PhaseProtocol] Sequential fallback for {count} PCs");

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
                        Debug.Log($"[PhaseProtocol] PC{idx} answer received in {sw.ElapsedMilliseconds}ms (sequential event-driven)");
                    else
                        Debug.LogWarning($"[PhaseProtocol] PC{idx} answer timeout after {sw.ElapsedMilliseconds}ms (sequential)");
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
                    // VP9 → H264 → VP8 → others (skip H265 as not supported)
                    preferredCodecs = vp9.Concat(h264).Concat(vp8).Concat(others).ToArray();
                    break;

                case VideoCodec.VP8:
                    // VP8 → H264 → VP9 → others
                    preferredCodecs = vp8.Concat(h264).Concat(vp9).Concat(others).ToArray();
                    break;

                case VideoCodec.H264:
                default:
                    // H264 → VP9 → VP8 → others
                    preferredCodecs = h264.Concat(vp9).Concat(vp8).Concat(others).ToArray();
                    break;
            }

            Debug.Log($"[PhaseProtocol] PC{idx} codec preferences: {_selectedCodec} first, total {preferredCodecs.Length} codecs");
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
                Debug.Log($"[PhaseProtocol] PC{idx} ICE: {s}");

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
                Debug.Log($"[PhaseProtocol] PC{idx} State: {s}");
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
                        Debug.Log($"[PhaseProtocol] PC{idx} initiating auto-heal...");
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
                    Debug.Log($"[PhaseProtocol] PC{idx} queued ICE candidate (offer not sent yet)");
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
                    Debug.Log($"[PhaseProtocol] PC{idx} OnTrack: mid={mid}, trackId={trackId}");

                    wrapper.VideoTrack = v;
                    wrapper.LastFrameTime = DateTime.UtcNow;

                    // Capture mid for callback logging
                    var capturedMid = mid;
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
                            Debug.Log($"[PhaseProtocol] PC{idx} OnVideoReceived mid={capturedMid}, frame={wrapper.FrameCount}, tex={tex?.width}x{tex?.height}");
                        }

                        // Fire OnStreamingStarted on first frame if not already fired
                        // This is a backup mechanism in case streaming_started message is delayed/lost
                        if (!_streamingStartedFired)
                        {
                            _streamingStartedFired = true;
                            Debug.Log($"[PhaseProtocol] PC{idx} received first frame, firing OnStreamingStarted as backup");
                            _stateMachine.TryTransition(ConnectionPhase.Streaming);
                            OnStreamingStarted?.Invoke();
                        }

                        OnVideoTextureReceived?.Invoke(idx, tex);
                    };
                    Debug.Log($"[PhaseProtocol] PC{idx} received video track, mid={mid}");
                }
                else if (e.Track is AudioStreamTrack audioTrack)
                {
                    Debug.Log($"[PhaseProtocol] PC{idx} received audio track, mid={e.Transceiver?.Mid}");
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
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} raw SDP preview: {rawPreview}");

            var sdp = FixSdp(rawSdp);

            Debug.Log($"[PhaseProtocol] PC{monitorIndex} received answer (SDP: {rawSdp.Length} -> {sdp.Length} bytes)");

            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count)
                {
                    Debug.LogWarning($"[PhaseProtocol] PC{monitorIndex} answer ignored: index out of range");
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
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SignalingState={wrapper.PC.SignalingState}, IceState={wrapper.PC.IceConnectionState}");
                if (wrapper.PC.SignalingState != RTCSignalingState.HaveLocalOffer)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} wrong state: {wrapper.PC.SignalingState}");
                    return;
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} calling SetRemoteDescription (SDP len={sdp.Length})...");
                var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
                var setRemoteOp = wrapper.PC.SetRemoteDescription(ref answer);

                if (setRemoteOp == null)
                {
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription returned null!");
                    return;
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription called, waiting for completion...");

                // Wait for operation to complete (max 5 seconds)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int waitCount = 0;
                while (!setRemoteOp.IsDone && sw.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(10);
                    waitCount++;
                    if (waitCount % 100 == 0) // Log every 1 second
                        Debug.Log($"[PhaseProtocol] PC{monitorIndex} still waiting... {sw.ElapsedMilliseconds}ms, IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError}");
                }

                Debug.Log($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription wait done: IsDone={setRemoteOp.IsDone}, IsError={setRemoteOp.IsError}, elapsed={sw.ElapsedMilliseconds}ms");

                if (!setRemoteOp.IsDone || setRemoteOp.IsError)
                {
                    var errorMsg = setRemoteOp.IsError ? setRemoteOp.Error.message ?? "unknown" : "timeout";
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} SetRemoteDescription failed: {errorMsg}");

                    // Debug: Log full SDP on error for analysis
                    Debug.LogError($"[PhaseProtocol] PC{monitorIndex} Failed SDP (full):\n{sdp}");
                    return;
                }

                wrapper.AnswerSet = true;
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} answer set OK ({sw.ElapsedMilliseconds}ms)");

                // Single-PC mode: Mark ALL wrappers as AnswerSet (they share the same PC)
                // This is safe because all wrappers point to the same PC
                lock (_lock)
                {
                    bool isSinglePCMode = _peerConnections.Count > 1 &&
                                          _peerConnections.All(w => w.PC == wrapper.PC);
                    if (isSinglePCMode)
                    {
                        Debug.Log("[PhaseProtocol] Single-PC mode: marking all wrappers as AnswerSet");
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
                Debug.Log($"[PhaseProtocol] All {_expectedMonitorCount} answers received, signaling TCS");
                _allAnswersReceivedTcs.TrySetResult(true);
            }
        }

        private void HandleCandidate(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            var candStr = json.GetString("candidate") ?? "";

            if (_skipTcpIceCandidates && (candStr.Contains(" tcp ", StringComparison.OrdinalIgnoreCase) || candStr.Contains("tcptype", StringComparison.OrdinalIgnoreCase)))
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} Skipped remote TCP candidate");
                return;
            }

            Debug.Log($"[PhaseProtocol] PC{monitorIndex} received ICE candidate");

            PCWrapper wrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count) return;
                wrapper = _peerConnections[monitorIndex];
            }

            if (wrapper.AnswerSet)
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} adding remote ICE candidate (AnswerSet=true)");
                AddIceCandidate(wrapper, candStr);
            }
            else
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} queuing remote ICE candidate (AnswerSet=false, pending={wrapper.PendingIce.Count + 1})");
                wrapper.PendingIce.Add(candStr);
            }
        }

        private void HandleEndOfCandidates(SimpleJson json)
        {
            var monitorIndex = json.GetInt("monitorIndex");
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} server ICE complete");
            CheckIceComplete();
        }

        /// <summary>
        /// Handle ice_ready message from server - all ICE connections are established.
        /// This provides a reliable server-side confirmation for transitioning to ReadyToStream.
        /// </summary>
        private void HandleIceReady(SimpleJson json)
        {
            var monitorCount = json.GetInt("monitorCount");
            Debug.Log($"[PhaseProtocol] Server confirmed {monitorCount} ICE connections ready");

            if (_stateMachine.CurrentPhase == ConnectionPhase.ICENegotiating)
            {
                Debug.Log("[PhaseProtocol] Transitioning to ReadyToStream (server-initiated via ice_ready)");
                _stateMachine.TryTransition(ConnectionPhase.ReadyToStream);
                _ = SendTextAsync("{\"type\":\"proceed\",\"phase\":3}");
                OnReadyToStream?.Invoke();
            }
            else
            {
                Debug.Log($"[PhaseProtocol] ice_ready received but phase is {_stateMachine.CurrentPhase}, ignoring");
            }
        }

        private void AddIceCandidate(PCWrapper wrapper, string candStr)
        {
            try
            {
                var fullCand = candStr.StartsWith("candidate:", StringComparison.OrdinalIgnoreCase) ? candStr : "candidate:" + candStr;
                wrapper.PC.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = fullCand, sdpMLineIndex = 0, sdpMid = "0" }));
                Debug.Log($"[PhaseProtocol] PC{wrapper.Index} Added ICE candidate");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] PC{wrapper.Index} AddICE error: {ex.Message}");
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
                Debug.Log($"[PhaseProtocol] CheckIceComplete: {answered}/{created} PCs have answers (expected {expected} total), phase={_stateMachine.CurrentPhase}");

                // IMPORTANT: Wait for ALL expected PCs to be created AND have answers
                // This prevents proceeding too early when creating PCs sequentially
                if (created >= expected && created > 0 && _peerConnections.All(p => p.AnswerSet))
                {
                    if (_stateMachine.CurrentPhase == ConnectionPhase.ICENegotiating)
                    {
                        Debug.Log($"[PhaseProtocol] All {expected} PeerConnections ready, transitioning to ReadyToStream");
                        _stateMachine.TryTransition(ConnectionPhase.ReadyToStream);
                        shouldSendProceed = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[PhaseProtocol] All PCs ready but phase is {_stateMachine.CurrentPhase}, not ICENegotiating");
                    }
                }
            }

            // Send proceed message outside of lock
            if (shouldSendProceed)
            {
                Debug.Log("[PhaseProtocol] Sending proceed message for phase 3");
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
                if (_expectedMonitorCount <= 0) return;

                int connected = _peerConnections.Count(p =>
                    p.PC != null &&
                    (p.PC.IceConnectionState == RTCIceConnectionState.Connected ||
                     p.PC.IceConnectionState == RTCIceConnectionState.Completed));

                Debug.Log($"[PhaseProtocol] CheckAllMonitorsConnected: {connected}/{_expectedMonitorCount}");

                if (connected >= _expectedMonitorCount)
                {
                    Debug.Log("[PhaseProtocol] All monitors connected, firing OnAllMonitorsReady");
                    OnAllMonitorsReady?.Invoke();
                }
            }
        }
    }
}
