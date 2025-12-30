using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using VRWorkspace.Core;
using VRWorkspace.Streaming;

namespace VRWorkspace.ViewModels
{
    /// <summary>
    /// ViewModel for connection/streaming functionality.
    /// Wraps PhaseProtocolClient and provides observable properties for UI binding.
    ///
    /// All property changes are automatically marshalled to the main thread.
    ///
    /// Usage:
    /// var vm = ServiceLocator.Get&lt;ConnectionViewModel&gt;();
    /// vm.Phase.OnChanged += UpdatePhaseUI;
    /// vm.HardwareInfo.OnChanged += UpdateHardwarePanel;
    /// await vm.ConnectAsync("192.168.1.100", 9998);
    /// </summary>
    public class ConnectionViewModel : IDisposable
    {
        #region Observable Properties

        /// <summary>Current connection phase</summary>
        public ObservableProperty<ConnectionPhase> Phase { get; } = new(ConnectionPhase.Disconnected);

        /// <summary>Is currently connected (Phase >= AwaitingHardwareInfo)</summary>
        public ObservableProperty<bool> IsConnected { get; } = new(false);

        /// <summary>Is currently streaming</summary>
        public ObservableProperty<bool> IsStreaming { get; } = new(false);

        /// <summary>Last error message</summary>
        public ObservableProperty<string> ErrorMessage { get; } = new(string.Empty);

        /// <summary>Server hardware information (Phase 1)</summary>
        public ObservableProperty<ServerHardwareInfo> HardwareInfo { get; } = new();

        /// <summary>Network test results (Phase 1)</summary>
        public ObservableProperty<NetworkTestResult> NetworkInfo { get; } = new();

        /// <summary>Server's suggested streaming config</summary>
        public ObservableProperty<SuggestedStreamConfig> SuggestedConfig { get; } = new();

        /// <summary>Actually applied streaming config (what was sent to server)</summary>
        public ObservableProperty<StreamingConfig> AppliedConfig { get; } = new();

        /// <summary>Current config progress message</summary>
        public ObservableProperty<string> ConfigProgress { get; } = new(string.Empty);

        /// <summary>Speed test progress (0-100)</summary>
        public ObservableProperty<int> SpeedTestProgress { get; } = new(0);

        /// <summary>Current speed test bandwidth in Mbps</summary>
        public ObservableProperty<double> CurrentBandwidth { get; } = new(0);

        /// <summary>Available monitors from server</summary>
        public ObservableList<MonitorInfo> Monitors { get; } = new();

        /// <summary>Video textures per monitor</summary>
        public ObservableProperty<Dictionary<int, Texture>> VideoTextures { get; } = new(new Dictionary<int, Texture>());

        #endregion

        #region Events

        /// <summary>
        /// Cursor position update from server.
        /// Parameters: monitorIndex, u (0-1), v (0-1), visible
        /// </summary>
        public event Action<int, float, float, bool> OnCursorPositionChanged;

        #endregion

        #region Commands

        public ObservableCommand ConnectCommand { get; }
        public ObservableCommand DisconnectCommand { get; }
        public ObservableCommand AcceptConfigCommand { get; }
        public ObservableCommand StartStreamingCommand { get; }

        #endregion

        #region Private Fields

        private PhaseProtocolClient _client;
        private string _currentHost;
        private int _currentPort;
        private readonly Dictionary<int, Texture> _textures = new Dictionary<int, Texture>();
        private bool _disposed;

        #endregion

        #region Constructor

        public ConnectionViewModel()
        {
            // Initialize commands
            ConnectCommand = new ObservableCommand(
                async () => await ConnectInternalAsync(),
                () => Phase.Value == ConnectionPhase.Disconnected || Phase.Value == ConnectionPhase.Error
            );

            DisconnectCommand = new ObservableCommand(
                async () => await DisconnectAsync(),
                () => Phase.Value != ConnectionPhase.Disconnected
            );

            AcceptConfigCommand = new ObservableCommand(
                async () => await AcceptConfigAsync(),
                () => Phase.Value == ConnectionPhase.ConfiguringSettings
            );

            StartStreamingCommand = new ObservableCommand(
                async () => await StartStreamingAsync(),
                () => Phase.Value == ConnectionPhase.ReadyToStream
            );
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Connect to the streaming server.
        /// </summary>
        public async Task ConnectAsync(string host, int port)
        {
            _currentHost = host;
            _currentPort = port;
            await ConnectCommand.ExecuteAsync();
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_client == null) return;

            try
            {
                await _client.StopAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionViewModel] Disconnect error: {ex.Message}");
            }
            finally
            {
                CleanupClient();
                Phase.Value = ConnectionPhase.Disconnected;
                IsConnected.Value = false;
                IsStreaming.Value = false;
            }
        }

        /// <summary>
        /// Accept the suggested configuration and proceed to Phase 2.
        /// </summary>
        public async Task AcceptConfigAsync()
        {
            if (_client == null || SuggestedConfig.Value == null) return;

            var config = new StreamingConfig
            {
                monitors = SuggestedConfig.Value.monitors,
                resolutionWidth = SuggestedConfig.Value.resolutionWidth,
                resolutionHeight = SuggestedConfig.Value.resolutionHeight,
                bitrateKbps = SuggestedConfig.Value.bitrateKbps,
                fps = SuggestedConfig.Value.fps,
                refreshRate = SuggestedConfig.Value.refreshRate,
                selectedCodec = SuggestedConfig.Value.selectedCodec
            };

            await _client.ProceedToPhase2Async(config);
        }

        /// <summary>
        /// Accept custom configuration.
        /// </summary>
        public async Task AcceptConfigAsync(StreamingConfig config)
        {
            if (_client == null) return;

            // Store the applied config
            AppliedConfig.Value = config;
            Debug.Log($"[ConnectionViewModel] AppliedConfig set: {config.monitors} monitors @ {config.resolutionWidth}x{config.resolutionHeight}");

            await _client.ProceedToPhase2Async(config);
        }

        /// <summary>
        /// Start streaming (Phase 3).
        /// </summary>
        public async Task StartStreamingAsync()
        {
            if (_client == null) return;
            await _client.StartStreamingAsync();
        }

        /// <summary>
        /// Get current texture for a monitor.
        /// Polls directly from PhaseProtocolClient for latest texture.
        /// </summary>
        public Texture GetTexture(int monitorIndex)
        {
            // First try to get directly from client (polled texture)
            var clientTex = _client?.GetTexture(monitorIndex);
            if (clientTex != null)
            {
                // Update cache
                _textures[monitorIndex] = clientTex;
                return clientTex;
            }

            // Fallback to cached texture from events
            return _textures.TryGetValue(monitorIndex, out var tex) ? tex : null;
        }

        /// <summary>
        /// Poll textures from WebRTC. Call this in Update() for texture updates.
        /// </summary>
        public void PollTextures()
        {
            _client?.PollTextures();
        }

        /// <summary>
        /// Request server to send a keyframe immediately.
        /// Call this when user interacts (click, drag, etc.) for instant visual update.
        /// </summary>
        /// <param name="monitorIndex">Monitor index, or -1 for all monitors</param>
        public void RequestKeyframe(int monitorIndex = -1)
        {
            _client?.RequestKeyframe(monitorIndex);
        }

        /// <summary>
        /// Request server to skip buffered frames and send fresh keyframe.
        /// Used for latency recovery when stream delay accumulates.
        /// Note: This is called automatically by PollTextures() when frame gap is detected,
        /// but can also be called manually.
        /// </summary>
        public void SkipToLive()
        {
            _client?.SkipToLive();
        }

        #endregion

        #region Private Methods

        private async Task ConnectInternalAsync()
        {
            if (string.IsNullOrEmpty(_currentHost)) return;

            // Cleanup previous connection
            CleanupClient();

            // Create new client
            _client = new PhaseProtocolClient();
            SubscribeToEvents();

            Phase.Value = ConnectionPhase.Connecting;
            ErrorMessage.Value = string.Empty;

            try
            {
                var url = $"ws://{_currentHost}:{_currentPort}/signal";
                await _client.ConnectAsync(url);
                IsConnected.Value = true;
            }
            catch (Exception ex)
            {
                ErrorMessage.Value = ex.Message;
                Phase.Value = ConnectionPhase.Error;
                CleanupClient();
            }
        }

        private void SubscribeToEvents()
        {
            if (_client == null) return;

            // All these events fire on background threads
            // ObservableProperty automatically marshals to main thread

            _client.OnHardwareInfoReceived += info =>
            {
                HardwareInfo.Value = info;
                Phase.Value = ConnectionPhase.SpeedTesting;
            };

            _client.OnNetworkInfoReceived += info =>
            {
                NetworkInfo.Value = info;
                SpeedTestProgress.Value = 100;
            };

            _client.OnSuggestedConfigReceived += config =>
            {
                SuggestedConfig.Value = config;
                Phase.Value = ConnectionPhase.ConfiguringSettings;
            };

            _client.OnConfigProgress += (step, progress, message) =>
            {
                ConfigProgress.Value = $"{step}: {message} ({progress}%)";
            };

            _client.OnConfigComplete += monitors =>
            {
                Monitors.Clear();
                foreach (var m in monitors)
                {
                    Monitors.Add(m);
                }
                Phase.Value = ConnectionPhase.ICENegotiating;
            };

            _client.OnReadyToStream += () =>
            {
                Phase.Value = ConnectionPhase.ReadyToStream;
            };

            _client.OnVideoTextureReceived += (idx, tex) =>
            {
                _textures[idx] = tex;
                VideoTextures.SetAndNotify(new Dictionary<int, Texture>(_textures));
            };

            _client.OnStreamingStarted += () =>
            {
                Phase.Value = ConnectionPhase.Streaming;
                IsStreaming.Value = true;
            };

            _client.OnError += message =>
            {
                ErrorMessage.Value = message;
                Debug.LogError($"[ConnectionViewModel] Error: {message}");
            };

            _client.OnDisconnected += () =>
            {
                Phase.Value = ConnectionPhase.Disconnected;
                IsConnected.Value = false;
                IsStreaming.Value = false;
            };

            _client.OnCursorPosition += (monitorIndex, u, v, visible) =>
            {
                // Forward cursor position to UI (already on main thread from PhaseProtocolClient)
                OnCursorPositionChanged?.Invoke(monitorIndex, u, v, visible);
            };

            _client.OnSpeedTestProgress += (direction, mbps, progress) =>
            {
                if (direction == "bandwidth")
                {
                    CurrentBandwidth.Value = mbps;
                    SpeedTestProgress.Value = progress;
                }
            };
        }

        private void CleanupClient()
        {
            if (_client == null) return;

            try
            {
                // Use Dispose() since Cleanup() is private
                _client.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionViewModel] Cleanup error: {ex.Message}");
            }

            _client = null;
            _textures.Clear();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CleanupClient();
        }

        #endregion
    }
}
