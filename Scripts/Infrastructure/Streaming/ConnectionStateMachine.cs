using System;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Connection phases for the 3-phase protocol.
    /// </summary>
    public enum ConnectionPhase
    {
        Disconnected,           // Initial state
        Connecting,             // WebSocket connecting
        AwaitingHardwareInfo,   // Waiting for hardware_info from server
        SpeedTesting,           // Running speed test
        AwaitingNetworkInfo,    // Waiting for network_info
        AwaitingSuggestedConfig,// Waiting for suggested_config
        ConfiguringSettings,    // User reviewing/editing config
        SendingDisplayConfig,   // Sending display_config to server
        AwaitingSetupComplete,  // Waiting for config_complete
        ICENegotiating,         // WebRTC ICE exchange in progress
        ReadyToStream,          // ICE complete, ready for Phase 3
        StartingStream,         // Sent start_streaming, waiting for streaming_started
        Streaming,              // Active streaming
        Reconnecting,           // Connection lost, attempting reconnect
        Error                   // Error state
    }

    /// <summary>
    /// Event args for phase change events.
    /// </summary>
    public class PhaseChangedEventArgs : EventArgs
    {
        public ConnectionPhase OldPhase { get; }
        public ConnectionPhase NewPhase { get; }
        public string Message { get; }

        public PhaseChangedEventArgs(ConnectionPhase oldPhase, ConnectionPhase newPhase, string message = null)
        {
            OldPhase = oldPhase;
            NewPhase = newPhase;
            Message = message;
        }
    }

    /// <summary>
    /// State machine for managing 3-phase connection protocol.
    /// Tracks current phase and validates state transitions.
    /// </summary>
    public class ConnectionStateMachine
    {
        private ConnectionPhase _currentPhase = ConnectionPhase.Disconnected;
        private readonly object _lock = new object();
        private string _lastError;

        /// <summary>
        /// Current connection phase.
        /// </summary>
        public ConnectionPhase CurrentPhase
        {
            get { lock (_lock) return _currentPhase; }
        }

        /// <summary>
        /// Last error message if in Error state.
        /// </summary>
        public string LastError
        {
            get { lock (_lock) return _lastError; }
        }

        /// <summary>
        /// Event fired when phase changes.
        /// </summary>
        public event EventHandler<PhaseChangedEventArgs> PhaseChanged;

        /// <summary>
        /// Try to transition to a new phase.
        /// Returns true if transition is valid and successful.
        /// </summary>
        public bool TryTransition(ConnectionPhase newPhase, string message = null)
        {
            ConnectionPhase oldPhase;
            EventHandler<PhaseChangedEventArgs> handler;

            lock (_lock)
            {
                if (!IsValidTransition(_currentPhase, newPhase))
                {
                    AppLog.LogWarning($"[StateMachine] Invalid transition: {_currentPhase} -> {newPhase}");
                    return false;
                }

                oldPhase = _currentPhase;
                _currentPhase = newPhase;

                if (newPhase == ConnectionPhase.Error)
                {
                    _lastError = message ?? "Unknown error";
                }
                else if (newPhase == ConnectionPhase.Disconnected)
                {
                    _lastError = null;
                }

                AppLog.Log($"[StateMachine] {oldPhase} -> {newPhase}" + (message != null ? $": {message}" : ""));

                handler = PhaseChanged;
            }

            // Fire event outside lock to prevent deadlocks
            if (handler != null)
            {
                try
                {
                    handler.Invoke(this, new PhaseChangedEventArgs(oldPhase, newPhase, message));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[StateMachine] Event handler error: {ex.Message}");
                }
            }

            return true;
        }

        /// <summary>
        /// Force transition to a phase (for error recovery).
        /// </summary>
        public void ForceTransition(ConnectionPhase newPhase, string message = null)
        {
            ConnectionPhase oldPhase;
            EventHandler<PhaseChangedEventArgs> handler;

            lock (_lock)
            {
                oldPhase = _currentPhase;
                _currentPhase = newPhase;

                if (newPhase == ConnectionPhase.Error)
                {
                    _lastError = message ?? "Unknown error";
                }

                AppLog.Log($"[StateMachine] FORCE: {oldPhase} -> {newPhase}" + (message != null ? $": {message}" : ""));

                handler = PhaseChanged;
            }

            // Fire event outside lock to prevent deadlocks
            handler?.Invoke(this, new PhaseChangedEventArgs(oldPhase, newPhase, message));
        }

        /// <summary>
        /// Reset state machine to disconnected.
        /// </summary>
        public void Reset()
        {
            ForceTransition(ConnectionPhase.Disconnected, "Reset");
        }

        /// <summary>
        /// Check if currently in one of the specified phases.
        /// </summary>
        public bool IsInPhase(params ConnectionPhase[] phases)
        {
            lock (_lock)
            {
                foreach (var phase in phases)
                {
                    if (_currentPhase == phase) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Check if connection is active (not disconnected/error).
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _currentPhase != ConnectionPhase.Disconnected &&
                           _currentPhase != ConnectionPhase.Error;
                }
            }
        }

        /// <summary>
        /// Check if currently streaming.
        /// </summary>
        public bool IsStreaming => CurrentPhase == ConnectionPhase.Streaming;

        /// <summary>
        /// Check if in Phase 1 (hardware/network info exchange).
        /// </summary>
        public bool IsInPhase1
        {
            get
            {
                var phase = CurrentPhase;
                return phase == ConnectionPhase.Connecting ||
                       phase == ConnectionPhase.AwaitingHardwareInfo ||
                       phase == ConnectionPhase.SpeedTesting ||
                       phase == ConnectionPhase.AwaitingNetworkInfo ||
                       phase == ConnectionPhase.AwaitingSuggestedConfig ||
                       phase == ConnectionPhase.ConfiguringSettings;
            }
        }

        /// <summary>
        /// Check if in Phase 2 (display config + ICE).
        /// </summary>
        public bool IsInPhase2
        {
            get
            {
                var phase = CurrentPhase;
                return phase == ConnectionPhase.SendingDisplayConfig ||
                       phase == ConnectionPhase.AwaitingSetupComplete ||
                       phase == ConnectionPhase.ICENegotiating ||
                       phase == ConnectionPhase.ReadyToStream;
            }
        }

        /// <summary>
        /// Check if in Phase 3 (streaming).
        /// </summary>
        public bool IsInPhase3
        {
            get
            {
                var phase = CurrentPhase;
                return phase == ConnectionPhase.StartingStream ||
                       phase == ConnectionPhase.Streaming;
            }
        }

        /// <summary>
        /// Validate if a transition from one phase to another is allowed.
        /// </summary>
        private bool IsValidTransition(ConnectionPhase from, ConnectionPhase to)
        {
            // Always allow transition to Error or Disconnected
            if (to == ConnectionPhase.Error || to == ConnectionPhase.Disconnected)
                return true;

            // Always allow transition from Disconnected to Connecting
            if (from == ConnectionPhase.Disconnected && to == ConnectionPhase.Connecting)
                return true;

            // Allow reconnecting from most states
            if (to == ConnectionPhase.Reconnecting)
            {
                return from != ConnectionPhase.Disconnected && from != ConnectionPhase.Connecting;
            }

            // From Reconnecting, can go back to Connecting
            if (from == ConnectionPhase.Reconnecting && to == ConnectionPhase.Connecting)
                return true;

            // Phase 1 transitions
            switch (from)
            {
                case ConnectionPhase.Connecting:
                    return to == ConnectionPhase.AwaitingHardwareInfo;

                case ConnectionPhase.AwaitingHardwareInfo:
                    return to == ConnectionPhase.SpeedTesting;

                case ConnectionPhase.SpeedTesting:
                    return to == ConnectionPhase.AwaitingNetworkInfo;

                case ConnectionPhase.AwaitingNetworkInfo:
                    return to == ConnectionPhase.AwaitingSuggestedConfig;

                case ConnectionPhase.AwaitingSuggestedConfig:
                    return to == ConnectionPhase.ConfiguringSettings;

                case ConnectionPhase.ConfiguringSettings:
                    return to == ConnectionPhase.SendingDisplayConfig;

                // Phase 2 transitions
                case ConnectionPhase.SendingDisplayConfig:
                    return to == ConnectionPhase.AwaitingSetupComplete;

                case ConnectionPhase.AwaitingSetupComplete:
                    return to == ConnectionPhase.ICENegotiating;

                case ConnectionPhase.ICENegotiating:
                    return to == ConnectionPhase.ReadyToStream;

                case ConnectionPhase.ReadyToStream:
                    return to == ConnectionPhase.StartingStream;

                // Phase 3 transitions
                case ConnectionPhase.StartingStream:
                    return to == ConnectionPhase.Streaming;

                case ConnectionPhase.Streaming:
                    return to == ConnectionPhase.Reconnecting;
            }

            return false;
        }

        /// <summary>
        /// Get human-readable description of current phase.
        /// </summary>
        public string GetPhaseDescription()
        {
            return CurrentPhase switch
            {
                ConnectionPhase.Disconnected => "Disconnected",
                ConnectionPhase.Connecting => "Connecting to server...",
                ConnectionPhase.AwaitingHardwareInfo => "Receiving server info...",
                ConnectionPhase.SpeedTesting => "Testing connection speed...",
                ConnectionPhase.AwaitingNetworkInfo => "Analyzing network...",
                ConnectionPhase.AwaitingSuggestedConfig => "Getting recommendations...",
                ConnectionPhase.ConfiguringSettings => "Review settings",
                ConnectionPhase.SendingDisplayConfig => "Applying configuration...",
                ConnectionPhase.AwaitingSetupComplete => "Server configuring displays...",
                ConnectionPhase.ICENegotiating => "Establishing connection...",
                ConnectionPhase.ReadyToStream => "Ready to stream",
                ConnectionPhase.StartingStream => "Starting stream...",
                ConnectionPhase.Streaming => "Streaming",
                ConnectionPhase.Reconnecting => "Reconnecting...",
                ConnectionPhase.Error => $"Error: {LastError}",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get progress percentage (0-100) for current phase.
        /// </summary>
        public int GetProgressPercent()
        {
            return CurrentPhase switch
            {
                ConnectionPhase.Disconnected => 0,
                ConnectionPhase.Connecting => 5,
                ConnectionPhase.AwaitingHardwareInfo => 15,
                ConnectionPhase.SpeedTesting => 25,
                ConnectionPhase.AwaitingNetworkInfo => 35,
                ConnectionPhase.AwaitingSuggestedConfig => 45,
                ConnectionPhase.ConfiguringSettings => 50,
                ConnectionPhase.SendingDisplayConfig => 55,
                ConnectionPhase.AwaitingSetupComplete => 65,
                ConnectionPhase.ICENegotiating => 80,
                ConnectionPhase.ReadyToStream => 90,
                ConnectionPhase.StartingStream => 95,
                ConnectionPhase.Streaming => 100,
                ConnectionPhase.Reconnecting => 50,
                ConnectionPhase.Error => 0,
                _ => 0
            };
        }
    }
}
