using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Handles Phase 2 of the connection protocol:
    ///   - Sending the proceed + display_config messages when the user confirms settings
    ///   - Handling config_progress notifications from the server
    ///   - Handling config_complete and kicking off PeerConnection creation
    ///
    /// All WebSocket sending is done through the <see cref="SendAsync"/> delegate
    /// supplied at construction. PC creation is delegated via a callback to keep
    /// WebRTC logic inside PhaseProtocolClient / its WebRTC partial.
    /// </summary>
    internal sealed class PhaseTwoHandler
    {
        // ── Dependencies ─────────────────────────────────────────────────────────
        private readonly Func<string, Task>              _send;
        private readonly ConnectionStateMachine          _stateMachine;

        // ── Callbacks ─────────────────────────────────────────────────────────
        // Called by HandleConfigCompleteAsync so the orchestrator triggers PC creation.
        // Signature: monitorCount → Task
        private readonly Func<int, Task> _onCreatePeerConnections;

        // ── State ────────────────────────────────────────────────────────────────
        public List<MonitorInfo> ConfiguredMonitors { get; private set; } = new List<MonitorInfo>();

        // ── Events ───────────────────────────────────────────────────────────────
        public event Action<string, int, string>  OnConfigProgress;   // step, percent, message
        public event Action<int>                  OnServerSetupProgress; // 0-100
        public event Action<List<MonitorInfo>>    OnConfigComplete;

        // ── Constructor ──────────────────────────────────────────────────────────
        public PhaseTwoHandler(
            Func<string, Task>     send,
            ConnectionStateMachine stateMachine,
            Func<int, Task>        onCreatePeerConnections)
        {
            _send                   = send                   ?? throw new ArgumentNullException(nameof(send));
            _stateMachine           = stateMachine           ?? throw new ArgumentNullException(nameof(stateMachine));
            _onCreatePeerConnections = onCreatePeerConnections ?? throw new ArgumentNullException(nameof(onCreatePeerConnections));
        }

        // ── Public actions ────────────────────────────────────────────────────

        /// <summary>
        /// Called by the orchestrator when the user confirms settings.
        /// Sends proceed + display_config and advances the state machine.
        /// </summary>
        public async Task SendConfigAsync(StreamingConfig config)
        {
            AppLog.Log($"[Phase2] Sending config: {StreamingOptimizer.FormatConfig(config)}");

            _stateMachine.TryTransition(ConnectionPhase.SendingDisplayConfig);

            await _send("{\"type\":\"proceed\",\"phase\":2}");
            AppLog.Log("[Phase2] proceed(2) sent");

            var preferGpuStr    = string.IsNullOrEmpty(config.preferGpu)
                                    ? "null"
                                    : $"\"{EscapeJson(config.preferGpu)}\"";

            var monitorTypeStr = string.IsNullOrEmpty(config.monitorType)
                                    ? "standard"
                                    : config.monitorType;

            var displayConfigJson =
                $"{{\"type\":\"display_config\"," +
                $"\"monitors\":{config.monitors}," +
                $"\"resolution\":{{\"w\":{config.resolutionWidth},\"h\":{config.resolutionHeight}}}," +
                $"\"refreshRate\":{config.refreshRate}," +
                $"\"bitrateKbps\":{config.bitrateKbps}," +
                $"\"fps\":{config.fps}," +
                $"\"monitorType\":\"{EscapeJson(monitorTypeStr)}\"," +
                $"\"preferGpu\":{preferGpuStr}}}";

            await _send(displayConfigJson);
            AppLog.Log("[Phase2] display_config sent");

            _stateMachine.TryTransition(ConnectionPhase.AwaitingSetupComplete);
        }

        // ── Message handlers ──────────────────────────────────────────────────

        public void HandleConfigProgress(SimpleJson json)
        {
            var step     = json.GetString("step")    ?? "";
            var progress = json.GetInt("progress");
            var message  = json.GetString("message") ?? "";

            AppLog.Log($"[Phase2] Config progress: {step} {progress}% – {message}");

            OnConfigProgress?.Invoke(step, progress, message);
            OnServerSetupProgress?.Invoke(progress);
        }

        public Task HandleConfigCompleteAsync(SimpleJson json)
        {
            AppLog.Log("[Phase2] config_complete received, starting ICE negotiation");

            var monitorsArr     = json.GetArray("monitors");
            ConfiguredMonitors  = monitorsArr?.Select(m => new MonitorInfo
            {
                id        = m.GetInt("id"),
                name      = m.GetString("name") ?? "",
                width     = m.GetInt("w"),
                height    = m.GetInt("h"),
                isVirtual = m.GetBool("isVirtual")
            }).ToList() ?? new List<MonitorInfo>();

            OnConfigComplete?.Invoke(ConfiguredMonitors);

            _stateMachine.TryTransition(ConnectionPhase.ICENegotiating);

            // Fire-and-forget: receive loop must stay unblocked while PCs are created
            AppLog.Log("[Phase2] Launching CreatePeerConnections in background...");
            _ = _onCreatePeerConnections(ConfiguredMonitors.Count);

            return Task.CompletedTask;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
