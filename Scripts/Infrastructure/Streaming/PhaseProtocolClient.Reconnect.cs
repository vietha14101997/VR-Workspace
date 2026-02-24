using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    public partial class PhaseProtocolClient
    {
        /// <summary>
        /// Reconnect a specific monitor by recreating PeerConnection and sending new offer.
        /// Called when server requests reconnect due to connection loss.
        /// </summary>
        private async Task ReconnectMonitorAsync(int monitorIndex)
        {
            // Don't reconnect if application is shutting down
            if (_cts == null || _cts.IsCancellationRequested) return;

            PCWrapper oldWrapper;
            lock (_lock)
            {
                if (monitorIndex < 0 || monitorIndex >= _peerConnections.Count)
                {
                    Debug.LogWarning($"[PhaseProtocol] Reconnect ignored: invalid monitor index {monitorIndex}");
                    return;
                }
                oldWrapper = _peerConnections[monitorIndex];
            }

            Debug.Log($"[PhaseProtocol] Reconnecting PC{monitorIndex}...");

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

            // Connection state handlers
            pc.OnIceConnectionChange = s =>
            {
                Debug.Log($"[PhaseProtocol] PC{idx} ICE (reconnected): {s}");
            };
            pc.OnConnectionStateChange = s =>
            {
                Debug.Log($"[PhaseProtocol] PC{idx} State (reconnected): {s}");
                if (s == RTCPeerConnectionState.Connected)
                {
                    Debug.Log($"[PhaseProtocol] PC{idx} reconnect successful!");
                    wrapper.LastConnectedTime = DateTime.UtcNow;
                    wrapper.IsReconnecting = false;
                    wrapper.ReconnectAttempts = 0; // Reset on successful reconnection
                    _metrics.ResetStallCount();
                    _metrics.ResetIceDisconnectCount();

                    // Send reconnect acknowledgment to server
                    _ = SendTextAsync($"{{\"type\":\"reconnect_ack\",\"monitorIndex\":{idx}}}");
                }
                else if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Disconnected)
                {
                    Debug.LogWarning($"[PhaseProtocol] PC{idx} connection lost again after reconnect");
                    _metrics.RecordIceDisconnect();

                    // Auto-heal again if still streaming
                    if (_stateMachine.IsStreaming && !wrapper.IsReconnecting)
                    {
                        wrapper.IsReconnecting = true;
                        Debug.Log($"[PhaseProtocol] PC{idx} re-initiating auto-heal...");
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
                    Debug.Log($"[PhaseProtocol] PC{idx} queued ICE candidate on reconnect (offer not sent yet)");
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
                            Debug.Log($"[PhaseProtocol] PC{idx} received first frame (reconnected), firing OnStreamingStarted");
                            _stateMachine.TryTransition(ConnectionPhase.Streaming);
                            OnStreamingStarted?.Invoke();
                        }

                        OnVideoTextureReceived?.Invoke(idx, tex);
                    };
                    Debug.Log($"[PhaseProtocol] PC{idx} received video track (reconnected)");
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
            Debug.Log($"[PhaseProtocol] PC{idx} reconnect offer sent");

            // Mark offer as sent and flush queued candidates
            wrapper.OfferSent = true;
            if (wrapper.QueuedCandidates.Count > 0)
            {
                Debug.Log($"[PhaseProtocol] PC{idx} flushing {wrapper.QueuedCandidates.Count} queued ICE candidates on reconnect");
                foreach (var candJson in wrapper.QueuedCandidates)
                {
                    _ = SendTextAsync(candJson);
                }
                wrapper.QueuedCandidates.Clear();
            }
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
            RTCPeerConnection oldPc;
            lock (_lock)
            {
                count = _peerConnections.Count;
                if (count == 0)
                {
                    Debug.LogWarning("[PhaseProtocol] ReconnectSinglePC: No monitors to reconnect");
                    return;
                }
                oldPc = _peerConnections[0].PC;
            }

            Debug.Log($"[PhaseProtocol] ReconnectSinglePC: Reconnecting {count} monitors...");

            // Close old PeerConnection
            try
            {
                oldPc?.Close();
                oldPc?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhaseProtocol] Error closing old PC: {ex.Message}");
            }

            // Reset answer TCS for new session
            _allAnswersReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Recreate everything using the same method as initial connection
            await CreateSinglePCMultiTrackAsync(count);

            Debug.Log("[PhaseProtocol] ReconnectSinglePC: Reconnection initiated, waiting for answer...");
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
            Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: attempt {wrapper.ReconnectAttempts}/{PCWrapper.MaxReconnectAttempts}, waiting {delayMs}ms...");
            await Task.Delay(delayMs);

            // Check if we're still streaming and need reconnect
            if (_cts == null || _cts.IsCancellationRequested) return;
            if (!_stateMachine.IsStreaming)
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: no longer streaming, skip reconnect");
                wrapper.IsReconnecting = false;
                return;
            }

            // Check if frames are now flowing (frame stall recovered naturally)
            var timeSinceFrame = DateTime.UtcNow - wrapper.LastFrameTime;
            if (timeSinceFrame.TotalMilliseconds < 2000) // Frames flowing within last 2s
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: frames recovered ({timeSinceFrame.TotalMilliseconds:F0}ms since last frame), skip reconnect");
                wrapper.IsReconnecting = false;
                wrapper.ReconnectAttempts = 0; // Reset on success
                return;
            }

            // Check PC connection state
            var state = wrapper.PC?.ConnectionState ?? RTCPeerConnectionState.Closed;

            // If PC is connected but frames stalled, we still need to reconnect
            // This handles the case where WebRTC connection is fine but video stopped
            if (state == RTCPeerConnectionState.Connected)
            {
                Debug.LogWarning($"[PhaseProtocol] PC{monitorIndex} auto-heal: PC Connected but frames stalled for {timeSinceFrame.TotalMilliseconds:F0}ms - forcing reconnect");
            }
            else
            {
                Debug.Log($"[PhaseProtocol] PC{monitorIndex} auto-heal: PC state={state}, initiating reconnect...");
            }

            try
            {
                await ReconnectMonitorAsync(monitorIndex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] PC{monitorIndex} auto-heal failed: {ex.Message}");
                wrapper.IsReconnecting = false;
            }
        }

        /// <summary>
        /// Reconnect entire session - preserves user config but re-runs Phase 2 (ICE negotiation).
        /// Called when individual track reconnects have failed and user chooses "Restart Session".
        /// </summary>
        public async Task ReconnectSessionAsync()
        {
            Debug.Log("[PhaseProtocol] Starting full session reconnect...");

            // 1. Close all PeerConnections
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

            // 2. Reset metrics
            _metrics.ResetAll();
            _streamingStartedFired = false;

            // 3. Transition to reconnecting state
            _stateMachine.TryTransition(ConnectionPhase.Reconnecting);

            // 4. Re-run Phase 2 (ICE negotiation) with preserved config
            if (_userConfig != null && _ws?.State == WebSocketState.Open)
            {
                Debug.Log("[PhaseProtocol] Requesting Phase 2 restart with existing config");

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
            Debug.Log("[PhaseProtocol] User requested retry - resetting reconnect counters");

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
