using System;
using System.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Handles Phase 3 of the connection protocol (active streaming):
    ///   - Reacting to streaming_started from the server
    ///   - Sending pause / resume / keyframe / skip-to-live / update_config commands
    ///   - Handling fps_adjusted / bitrate_adjusted / quality_recommendation messages
    ///   - Initiating per-monitor pause / resume
    ///
    /// All WebSocket sending is delegated to the <see cref="SendAsync"/> func
    /// injected at construction. State machine transitions are performed through
    /// the injected <see cref="ConnectionStateMachine"/>.
    /// </summary>
    internal sealed class PhaseThreeHandler
    {
        // ── Dependencies ─────────────────────────────────────────────────────────
        private readonly Func<string, Task>    _send;
        private readonly ConnectionStateMachine _stateMachine;

        // ── Events ───────────────────────────────────────────────────────────────
        public event Action                         OnStreamingStarted;
        public event Action<int, float>             OnFpsAdjusted;          // monitorIndex, targetFps
        public event Action<int, int, string>       OnBitrateAdjusted;      // monitorIndex, kbps, reason
        public event Action<string, string>         OnQualityRecommendation; // recommendation, reason
        public event Action                         OnSkipToLiveAck;

        // ── Constructor ──────────────────────────────────────────────────────────
        public PhaseThreeHandler(
            Func<string, Task>    send,
            ConnectionStateMachine stateMachine)
        {
            _send         = send         ?? throw new ArgumentNullException(nameof(send));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        }

        // ── Server-side message handlers ──────────────────────────────────────

        /// <summary>
        /// Called when the server sends "streaming_started".
        /// Transitions to Streaming state and fires the event.
        /// Android power locks and the frame-stall monitor are handled by the orchestrator.
        /// </summary>
        public void HandleStreamingStarted(SimpleJson json)
        {
            Debug.Log("[Phase3] streaming_started received");
            _stateMachine.TryTransition(ConnectionPhase.Streaming);
            OnStreamingStarted?.Invoke();
        }

        public void HandleFpsAdjusted(SimpleJson json)
        {
            int    monitorIndex = json.GetInt("monitorIndex");
            double targetFpsD  = json.GetDouble("targetFps");
            float  targetFps   = targetFpsD > 0 ? (float)targetFpsD : 60f;

            // fps_adjusted fires every ~250ms — too noisy for default logging
            OnFpsAdjusted?.Invoke(monitorIndex, targetFps);
        }

        public void HandleBitrateAdjusted(SimpleJson json)
        {
            int    monitorIndex = json.GetInt("monitorIndex");
            int    bitrateKbps  = json.GetInt("bitrateKbps");
            string reason       = json.GetString("reason") ?? "adaptive";

            // bitrate_adjusted can fire frequently — keep quiet
            OnBitrateAdjusted?.Invoke(monitorIndex, bitrateKbps, reason);
        }

        public void HandleQualityRecommendation(SimpleJson json)
        {
            string recommendation = json.GetString("recommendation") ?? "";
            string reason         = json.GetString("reason")         ?? "";

            Debug.Log($"[Phase3] quality_recommendation: {recommendation} – {reason}");
            OnQualityRecommendation?.Invoke(recommendation, reason);
        }

        public void HandleSkipToLiveAck()
        {
            // skip_to_live ack fires frequently during recovery — keep quiet
            OnSkipToLiveAck?.Invoke();
        }

        // ── Client-side commands (send to server) ─────────────────────────────

        public async Task PauseStreamingAsync()
        {
            if (!EnsureStreaming("PauseStreaming")) return;
            await _send("{\"type\":\"pause_streaming\"}");
            Debug.Log("[Phase3] pause_streaming sent");
        }

        public async Task ResumeStreamingAsync()
        {
            if (!EnsureStreaming("ResumeStreaming")) return;
            await _send("{\"type\":\"resume_streaming\"}");
            Debug.Log("[Phase3] resume_streaming sent");
        }

        public async Task PauseMonitorAsync(int monitorIndex)
        {
            if (!EnsureStreaming("PauseMonitor")) return;
            await _send($"{{\"type\":\"pause_monitor\",\"monitorIndex\":{monitorIndex}}}");
            Debug.Log($"[Phase3] pause_monitor sent: index={monitorIndex}");
        }

        public async Task ResumeMonitorAsync(int monitorIndex)
        {
            if (!EnsureStreaming("ResumeMonitor")) return;
            await _send($"{{\"type\":\"resume_monitor\",\"monitorIndex\":{monitorIndex}}}");
            Debug.Log($"[Phase3] resume_monitor sent: index={monitorIndex}");
        }

        public async Task RequestKeyframeAsync(int monitorIndex = -1)
        {
            if (!EnsureStreaming("RequestKeyframe")) return;
            string json = monitorIndex >= 0
                ? $"{{\"type\":\"request_keyframe\",\"monitorIndex\":{monitorIndex}}}"
                : "{\"type\":\"request_keyframe\"}";
            await _send(json);
            // request_keyframe fires frequently via preventive + stall recovery — keep quiet
        }

        public async Task SkipToLiveAsync(int monitorIndex = -1)
        {
            if (!EnsureStreaming("SkipToLive")) return;
            string msg = monitorIndex >= 0
                ? $"{{\"type\":\"skip_to_live\",\"monitor\":{monitorIndex}}}"
                : "{\"type\":\"skip_to_live\"}";
            await _send(msg);
            Debug.Log($"[Phase3] skip_to_live sent (monitor={monitorIndex})");
        }

        /// <summary>
        /// Urgent skip that also appends "urgent:true" for the server to prioritise.
        /// Used when RTT is extreme.
        /// </summary>
        public async Task SkipToLiveUrgentAsync(int monitorIndex = -1)
        {
            if (!EnsureStreaming("SkipToLiveUrgent")) return;
            string msg = monitorIndex >= 0
                ? $"{{\"type\":\"skip_to_live\",\"monitor\":{monitorIndex},\"urgent\":true}}"
                : "{\"type\":\"skip_to_live\",\"urgent\":true}";
            await _send(msg);
            Debug.Log($"[Phase3] URGENT skip_to_live sent (monitor={monitorIndex})");

            // Brief pause so server can process skip before we ask for a keyframe
            await Task.Delay(50);
            await RequestKeyframeAsync(monitorIndex);
        }

        /// <summary>
        /// Update dynamic encoding settings (FPS, resolution).
        /// </summary>
        public async Task UpdateConfigAsync(int? fps, int? resolutionHeight)
        {
            if (!EnsureStreaming("UpdateConfig")) return;

            var parts = new System.Collections.Generic.List<string> { "\"type\":\"update_config\"" };
            if (fps.HasValue)              parts.Add($"\"fps\":{fps.Value}");
            if (resolutionHeight.HasValue) parts.Add($"\"resolutionHeight\":{resolutionHeight.Value}");

            string json = "{" + string.Join(",", parts) + "}";
            Debug.Log($"[Phase3] update_config: {json}");
            await _send(json);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private bool EnsureStreaming(string caller)
        {
            if (_stateMachine.IsStreaming) return true;
            Debug.LogWarning($"[Phase3] {caller} skipped: not streaming (state={_stateMachine.CurrentPhase})");
            return false;
        }
    }
}
