using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Core;
using VRWorkspace.Streaming;

namespace VRWorkspace.ViewModels
{
    /// <summary>
    /// Transport mode for connection (USB vs WiFi).
    /// </summary>
    public enum TransportMode
    {
        WiFi,   // Default: Connect via WiFi network
        USB     // Connect via USB Tethering
    }

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

        /// <summary>Server setup progress (0-100)</summary>
        public ObservableProperty<int> ServerSetupProgress { get; } = new(0);

        /// <summary>Per-monitor ICE progress (monitorIndex -> 0-100)</summary>
        public ObservableProperty<Dictionary<int, int>> MonitorIceProgress { get; } = new(new Dictionary<int, int>());

        /// <summary>Number of monitors that are fully ready</summary>
        public ObservableProperty<int> ReadyMonitorCount { get; } = new(0);

        /// <summary>Total number of monitors expected</summary>
        public ObservableProperty<int> TotalMonitorCount { get; } = new(0);

        /// <summary>Current resolution height (updated when config changes)</summary>
        public ObservableProperty<int> CurrentResolutionHeight { get; } = new(1080);

        /// <summary>Current FPS (updated when config changes)</summary>
        public ObservableProperty<int> CurrentFps { get; } = new(60);

        #endregion

        #region Events

        /// <summary>
        /// Cursor position update from server.
        /// Parameters: monitorIndex, u (0-1), v (0-1), visible, cursorType, cursorId
        /// </summary>
        public event Action<int, float, float, bool, CursorType, long> OnCursorPositionChanged;

        /// <summary>
        /// Cursor image received from server.
        /// Parameters: cursorId, cursorType, texture, hotspotX, hotspotY
        /// </summary>
        public event Action<long, CursorType, Texture2D, int, int> OnCursorImageReceived;

        /// <summary>
        /// Fired when START button is clicked with new flow.
        /// Creates ClusterRig and shows progress UI immediately.
        /// </summary>
        public event Action<StreamingConfig> OnStartWithProgress;

        /// <summary>
        /// Fired when all monitors are ready (ICE connected).
        /// UI should show "Connecting..." and wait for auto-start.
        /// </summary>
        public event Action OnAllMonitorsReady;
        /// <summary>Fired when ALL monitors have decoded their first frame — safe to hide menu.</summary>
        public event Action OnAllMonitorsFirstFrame;

        /// <summary>
        /// Fired when a remote audio track is received from the server.
        /// Subscribe to this to set up audio playback (e.g., via RemoteAudioPlayer).
        /// </summary>
        public event Action<AudioStreamTrack> OnRemoteAudioTrackReceived;

        /// <summary>
        /// Fired when DataChannel audio data is received (low-latency path, bypasses NetEQ).
        /// Binary format: [type(1)][timestamp(8)][opus_data]
        /// </summary>
        public event Action<byte[]> OnDCAudioData;

        /// <summary>
        /// VR Mode status change event from server.
        /// </summary>
        public event Action<bool> OnVrModeChanged;

        /// <summary>
        /// Cached audio track for late subscribers (OnTrack fires before RemoteAudioPlayer exists).
        /// </summary>
        public AudioStreamTrack CachedAudioTrack => _cachedAudioTrack;

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
        private TransportMode _transportMode = TransportMode.WiFi;
        private string _authToken;
        private string _tunnelUrl;
        private readonly Dictionary<int, Texture> _textures = new Dictionary<int, Texture>();
        private bool _disposed;

        /// <summary>
        /// Cached audio track received from server.
        /// OnAudioTrackReceived fires during WebRTC negotiation (Phase 2),
        /// but RemoteAudioPlayer subscribes later (Phase 3). Cache ensures
        /// late subscribers still get the track.
        /// </summary>
        private AudioStreamTrack _cachedAudioTrack;

        /// <summary>
        /// Generation counter to invalidate stale event handlers from old clients.
        /// Incremented each time a new client is created.
        /// </summary>
        private int _clientGeneration = 0;

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
        public async Task ConnectAsync(string host, int port, string token = null, string tunnelUrl = null)
        {
            _currentHost = host;
            _currentPort = port;
            _transportMode = TransportMode.WiFi;
            _authToken = token;
            _tunnelUrl = tunnelUrl;
            await ConnectCommand.ExecuteAsync();
        }

        /// <summary>
        /// Connect to server via USB Tethering (full TCP+UDP over USB cable).
        /// </summary>
        /// <param name="port">Server port (default 8288)</param>
        /// <param name="usbTetheringIP">USB Tethering IP (required)</param>
        public async Task ConnectUSBAsync(int port = 8288, string usbTetheringIP = null)
        {
            // Try ADB reverse first (localhost → USB pipe → PC, no RNDIS needed)
            // If ADB reverse is active on PC, Android can reach server via localhost
            bool adbSuccess = false;
            try
            {
                Debug.Log($"[ConnectionViewModel] Trying ADB reverse (localhost:{port})...");
                using var tcpCheck = new System.Net.Sockets.TcpClient();
                var connectTask = tcpCheck.ConnectAsync("127.0.0.1", port);
                if (await System.Threading.Tasks.Task.WhenAny(connectTask, System.Threading.Tasks.Task.Delay(1000)) == connectTask
                    && tcpCheck.Connected)
                {
                    adbSuccess = true;
                    _currentHost = "127.0.0.1";
                    _currentPort = port;
                    _transportMode = TransportMode.USB;
                    Debug.Log($"[ConnectionViewModel] ADB reverse detected! Connecting via localhost:{port}");
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[ConnectionViewModel] ADB reverse not available: {ex.Message}");
            }

            // Fallback to USB Tethering (RNDIS)
            if (!adbSuccess)
            {
                if (string.IsNullOrEmpty(usbTetheringIP))
                {
                    Debug.LogError("[ConnectionViewModel] No USB connection available (ADB reverse failed, no tethering IP)");
                    ErrorMessage.Value = "USB cable not detected. Plug in USB cable and try again.";
                    Phase.Value = ConnectionPhase.Error;
                    return;
                }
                _currentHost = usbTetheringIP;
                _currentPort = port;
                _transportMode = TransportMode.USB;
                Debug.Log($"[ConnectionViewModel] Connecting via USB Tethering ({usbTetheringIP}:{port})");
            }

            await ConnectCommand.ExecuteAsync();
        }

        /// <summary>
        /// Current transport mode (USB or WiFi).
        /// </summary>
        public TransportMode CurrentTransport => _transportMode;

        /// <summary>
        /// Disconnect from the server and reset all state.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_client == null)
            {
                // Even without client, reset state to ensure clean slate
                ResetAllState();
                return;
            }

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
                ResetAllState();
            }
        }

        /// <summary>
        /// Pause streaming - server stops capture/encode but keeps connection.
        /// Use when going back to menu during streaming.
        /// </summary>
        public async Task PauseStreamingAsync()
        {
            if (_client == null)
            {
                Debug.LogWarning("[ConnectionViewModel] PauseStreaming: No client");
                return;
            }

            await _client.PauseStreamingAsync();
        }

        /// <summary>
        /// Resume streaming - server restarts capture/encode.
        /// Use when returning from menu to continue streaming.
        /// </summary>
        public async Task ResumeStreamingAsync()
        {
            if (_client == null)
            {
                Debug.LogWarning("[ConnectionViewModel] ResumeStreaming: No client");
                return;
            }

            await _client.ResumeStreamingAsync();
        }

        /// <summary>
        /// Pause streaming for a specific monitor - server stops capture/encode for this monitor only.
        /// Use when toggling off a monitor button in taskbar.
        /// </summary>
        /// <param name="monitorIndex">Zero-based monitor index</param>
        public async Task PauseMonitorAsync(int monitorIndex)
        {
            if (_client == null)
            {
                Debug.LogWarning("[ConnectionViewModel] PauseMonitor: No client");
                return;
            }

            await _client.PauseMonitorAsync(monitorIndex);
        }

        /// <summary>
        /// Resume streaming for a specific monitor - server restarts capture/encode for this monitor.
        /// Use when toggling on a monitor button in taskbar.
        /// </summary>
        /// <param name="monitorIndex">Zero-based monitor index</param>
        public async Task ResumeMonitorAsync(int monitorIndex)
        {
            if (_client == null)
            {
                Debug.LogWarning("[ConnectionViewModel] ResumeMonitor: No client");
                return;
            }

            await _client.ResumeMonitorAsync(monitorIndex);
        }

        /// <summary>
        /// Force disconnect due to unrecoverable connection failure.
        /// Called when auto-reconnect exhausts all attempts or weak network is detected.
        /// Fires OnConnectionLost so UI can return to menu.
        /// </summary>
        public event Action<string> OnConnectionLost;

        public async void ForceDisconnect(string reason)
        {
            Debug.LogError($"[ConnectionViewModel] ForceDisconnect: {reason}");
            OnConnectionLost?.Invoke(reason);
            ResetAllState();
            await StopClientAsync();
        }

        /// <summary>
        /// Reset all observable properties to initial state.
        /// Call this synchronously when closing app to ensure clean slate.
        /// </summary>
        public void ResetState()
        {
            ResetAllState();
        }

        /// <summary>
        /// Stop the client connection without resetting state.
        /// Use this when state has already been reset synchronously via ResetState().
        /// This prevents race conditions where async disconnect overwrites new connection state.
        /// </summary>
        public async Task StopClientAsync()
        {
            // Capture the client reference NOW to avoid race conditions
            // If ConnectInternalAsync creates a new client while we're awaiting,
            // we should only stop/dispose the OLD client, not the new one
            var clientToStop = _client;
            if (clientToStop == null) return;

            // Clear _client immediately so ConnectInternalAsync doesn't see stale reference
            _client = null;

            try
            {
                await clientToStop.StopAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionViewModel] StopClient error: {ex.Message}");
            }
            finally
            {
                // Dispose the captured client, NOT _client (which may be new)
                try
                {
                    clientToStop.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ConnectionViewModel] StopClient dispose error: {ex.Message}");
                }
                _textures.Clear();
                // Note: Do NOT reset state here - caller already did ResetState() synchronously
            }
        }

        /// <summary>
        /// Reset all observable properties to initial state.
        /// Called on disconnect to ensure clean slate when reopening app.
        /// </summary>
        private void ResetAllState()
        {
            // Connection state
            Phase.Value = ConnectionPhase.Disconnected;
            IsConnected.Value = false;
            IsStreaming.Value = false;
            ErrorMessage.Value = string.Empty;
            _cachedAudioTrack = null;

            // Phase 1 data
            HardwareInfo.Value = null;
            NetworkInfo.Value = null;
            SpeedTestProgress.Value = 0;
            CurrentBandwidth.Value = 0;

            // Config data
            SuggestedConfig.Value = null;
            AppliedConfig.Value = null;
            ConfigProgress.Value = string.Empty;

            // Monitor data
            Monitors.Clear();
            VideoTextures.Value = new Dictionary<int, Texture>();

            // Progress tracking
            ServerSetupProgress.Value = 0;
            MonitorIceProgress.Value = new Dictionary<int, int>();
            ReadyMonitorCount.Value = 0;
            TotalMonitorCount.Value = 0;

            // Current streaming config
            CurrentResolutionHeight.Value = 1080;
            CurrentFps.Value = 60;

            Debug.Log("[ConnectionViewModel] All state reset to initial values");
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
            await StartStreamingAsync(false);
        }

        /// <summary>
        /// Start streaming (Phase 3).
        /// </summary>
        /// <param name="force">If true, bypass phase check (used for auto-start)</param>
        public async Task StartStreamingAsync(bool force)
        {
            if (_client == null) return;
            await _client.StartStreamingAsync(force);
        }

        /// <summary>
        /// New flow: Start with progress tracking.
        /// 1. Fires OnStartWithProgress to create ClusterRig immediately
        /// 2. Sends display_config (Phase 2)
        /// 3. Progress tracked via events
        /// 4. Auto-starts streaming when all monitors ready
        /// </summary>
        public async Task StartWithProgressAsync(StreamingConfig config)
        {
            if (_client == null) return;

            // Store the applied config
            AppliedConfig.Value = config;
            TotalMonitorCount.Value = config.monitors;

            // Set current resolution/fps from config
            CurrentResolutionHeight.Value = config.resolutionHeight;
            CurrentFps.Value = config.fps;

            // Reset progress
            ServerSetupProgress.Value = 0;
            MonitorIceProgress.Value = new Dictionary<int, int>();
            ReadyMonitorCount.Value = 0;

            Debug.Log($"[ConnectionViewModel] StartWithProgressAsync: {config.monitors} monitors, {config.bitrateKbps}kbps, {config.fps}fps");

            // Fire event to create ClusterRig and show progress UI
            OnStartWithProgress?.Invoke(config);

            // Start Phase 2 (sends display_config, does ICE negotiation)
            await _client.ProceedToPhase2Async(config);

            // Progress updates come via events from PhaseProtocolClient
            // Auto-start is handled by OnAllMonitorsReady subscription in SubscribeToEvents()
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

        // ── Remote Input Forwarding (BT mouse/keyboard → server) ──────────

        public void SendMouseMove(short dx, short dy) => _client?.SendMouseMove(dx, dy);
        public void SendMouseButton(byte button, bool down) => _client?.SendMouseButton(button, down);
        public void SendMouseWheel(short deltaY, short deltaX = 0) => _client?.SendMouseWheel(deltaY, deltaX);
        public void SendKey(ushort vk, bool down) => _client?.SendKey(vk, down);
        public void SendText(string text) => _client?.SendText(text);
        public void SendWarpCursor(int monitorIndex, float u, float v) => _client?.SendWarpCursor(monitorIndex, u, v);
        public void SendGamepadState(ushort buttons, byte lt, byte rt, short lx, short ly, short rx, short ry) =>
            _client?.SendGamepadState(buttons, lt, rt, lx, ly, rx, ry);

        /// <summary>
        /// Get streaming metrics (latency, jitter, FPS, etc.).
        /// Returns null if not connected.
        /// </summary>
        public StreamingMetrics GetMetrics()
        {
            return _client?.Metrics;
        }

        /// <summary>
        /// Update streaming configuration during Phase 3 (Streaming).
        /// Sends update_config message to server for dynamic FPS/Resolution changes.
        /// </summary>
        /// <param name="fps">New target FPS (null = no change)</param>
        /// <param name="resolutionHeight">New target resolution height (null = no change)</param>
        public async Task UpdateConfigAsync(int? fps, int? resolutionHeight)
        {
            if (_client == null || Phase.Value != ConnectionPhase.Streaming)
            {
                Debug.LogWarning($"[ConnectionViewModel] UpdateConfigAsync: Not streaming (phase={Phase.Value})");
                return;
            }
            await _client.UpdateConfigAsync(fps, resolutionHeight);

            // Update current values after sending to server
            if (resolutionHeight.HasValue)
            {
                CurrentResolutionHeight.Value = resolutionHeight.Value;
                Debug.Log($"[ConnectionViewModel] Updated CurrentResolutionHeight: {resolutionHeight.Value}");
            }
            if (fps.HasValue)
            {
                CurrentFps.Value = fps.Value;
                Debug.Log($"[ConnectionViewModel] Updated CurrentFps: {fps.Value}");
            }
        }

        #endregion


        #region Private Methods

        private async Task ConnectInternalAsync()
        {
            if (string.IsNullOrEmpty(_currentHost)) return;

            // Cleanup previous connection
            CleanupClient();

            // Increment generation to invalidate old event handlers
            _clientGeneration++;

            // Create new client
            _client = new PhaseProtocolClient();

            // Set USB Mode BEFORE connecting - this affects ICE candidate filtering
            // When USB Mode is ON via tethering, only ICE candidates from the USB Tethering subnet are sent
            // When USB Mode is ON via ADB reverse (localhost), ICE filtering is disabled because
            // WebRTC media flows through localhost → ADB pipe, not through a USB network adapter
            if (_transportMode == TransportMode.USB && _currentHost != "127.0.0.1")
            {
                _client.SetUsbMode(true, _currentHost);
                Debug.Log($"[ConnectionViewModel] USB Mode (tethering): ICE filtering to subnet of {_currentHost}");
            }
            else
            {
                _client.SetUsbMode(false, null);
                if (_transportMode == TransportMode.USB)
                    Debug.Log("[ConnectionViewModel] USB Mode (ADB reverse): ICE filtering disabled (localhost)");
            }

            SubscribeToEvents();

            Phase.Value = ConnectionPhase.Connecting;
            ErrorMessage.Value = string.Empty;

            try
            {
                // Build URL with transport parameter for USB mode and optional auth token
                var transportParam = _transportMode == TransportMode.USB ? "&transport=usb" : "";
                var tokenParam = !string.IsNullOrEmpty(_authToken) ? $"&token={_authToken}" : "";
                string url;
                if (!string.IsNullOrEmpty(_tunnelUrl))
                {
                    // Tunnel mode: use wss:// through Cloudflare Tunnel
                    var baseUrl = _tunnelUrl.TrimEnd('/').Replace("https://", "wss://");
                    url = $"{baseUrl}/signal?protocol=v2{transportParam}{tokenParam}";
                }
                else
                {
                    url = $"ws://{_currentHost}:{_currentPort}/signal?protocol=v2{transportParam}{tokenParam}";
                }
                Debug.Log($"[ConnectionViewModel] Connecting to {url} (transport={_transportMode})");
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

            // Capture current generation to detect stale events from old clients
            int subscribedGeneration = _clientGeneration;

            // All these events fire on background threads
            // ObservableProperty automatically marshals to main thread

            _client.OnHardwareInfoReceived += info =>
            {
                if (_clientGeneration != subscribedGeneration) return; // Stale event
                HardwareInfo.Value = info;
                Phase.Value = ConnectionPhase.SpeedTesting;
            };

            _client.OnNetworkInfoReceived += info =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                // Use SetAndNotify instead of Value = because the same object reference
                // may be passed multiple times with updated properties (e.g., USB latency update)
                // EqualityComparer sees same reference as equal and won't fire OnChanged
                NetworkInfo.SetAndNotify(info);
                SpeedTestProgress.Value = 100;
            };

            _client.OnSuggestedConfigReceived += config =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                SuggestedConfig.Value = config;
                Phase.Value = ConnectionPhase.ConfiguringSettings;
            };

            _client.OnConfigProgress += (step, progress, message) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                ConfigProgress.Value = $"{step}: {message} ({progress}%)";
            };

            _client.OnConfigComplete += monitors =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                Monitors.Clear();
                foreach (var m in monitors)
                {
                    Monitors.Add(m);
                }
                Phase.Value = ConnectionPhase.ICENegotiating;
            };

            _client.OnReadyToStream += () =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                Phase.Value = ConnectionPhase.ReadyToStream;
            };

            _client.OnVideoTextureReceived += (idx, tex) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                _textures[idx] = tex;
                VideoTextures.SetAndNotify(new Dictionary<int, Texture>(_textures));
            };

            _client.OnAudioTrackReceived += (audioTrack) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                _cachedAudioTrack = audioTrack;
                Debug.Log("[ConnectionViewModel] Audio track received and cached");
                OnRemoteAudioTrackReceived?.Invoke(audioTrack);
            };

            // DataChannel audio: forward raw Opus frames for low-latency playback
            _client.OnAudioDataReceived += (data) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                OnDCAudioData?.Invoke(data);
            };

            _client.OnVrModeChanged += (enabled) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                OnVrModeChanged?.Invoke(enabled);
            };

            _client.OnStreamingStarted += () =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                Phase.Value = ConnectionPhase.Streaming;
                IsStreaming.Value = true;
            };

            _client.OnError += message =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                ErrorMessage.Value = message;
                Phase.Value = ConnectionPhase.Error;
                Debug.LogError($"[ConnectionViewModel] Error: {message}");
            };

            _client.OnDisconnected += () =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                // If we were streaming, this is an unexpected disconnect — force cleanup
                bool wasStreaming = IsStreaming.Value;
                Phase.Value = ConnectionPhase.Disconnected;
                IsConnected.Value = false;
                IsStreaming.Value = false;
                if (wasStreaming)
                {
                    AppLog.LogWarning("[ConnectionViewModel] Unexpected disconnect during streaming");
                    OnConnectionLost?.Invoke("Connection lost");
                }
            };

            _client.OnReconnectFailed += (options) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                Debug.LogError($"[ConnectionViewModel] Reconnect failed after all attempts");
                ForceDisconnect("Auto-reconnect failed after multiple attempts");
            };

            _client.OnCursorPosition += (monitorIndex, u, v, visible, cursorType, cursorId) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                // Forward cursor position to UI (already on main thread from PhaseProtocolClient)
                OnCursorPositionChanged?.Invoke(monitorIndex, u, v, visible, cursorType, cursorId);
            };

            _client.OnCursorImageReceived += (cursorId, cursorType, texture, hotspotX, hotspotY) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                // Forward cursor image to UI
                OnCursorImageReceived?.Invoke(cursorId, cursorType, texture, hotspotX, hotspotY);
            };

            _client.OnSpeedTestProgress += (direction, mbps, progress) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                if (direction == "bandwidth")
                {
                    CurrentBandwidth.Value = mbps;
                    SpeedTestProgress.Value = progress;
                }
            };

            // Progress tracking events for new flow
            _client.OnServerSetupProgress += progress =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                ServerSetupProgress.Value = progress;
            };

            _client.OnMonitorIceProgress += (idx, progress) =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                var dict = new Dictionary<int, int>(MonitorIceProgress.Value);
                dict[idx] = progress;
                MonitorIceProgress.SetAndNotify(dict);
            };

            _client.OnMonitorIceComplete += idx =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                // Cap at TotalMonitorCount to prevent 3/2, 4/2 on ICE renegotiation
                int total = TotalMonitorCount.Value;
                int current = ReadyMonitorCount.Value;
                if (current < total)
                {
                    ReadyMonitorCount.Value = current + 1;
                    Debug.Log($"[ConnectionViewModel] Monitor {idx} ICE complete. Ready: {ReadyMonitorCount.Value}/{total}");
                }
            };

            _client.OnAllMonitorsFirstFrame += () =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                Debug.Log("[ConnectionViewModel] All monitors first frame decoded, firing OnAllMonitorsFirstFrame");
                OnAllMonitorsFirstFrame?.Invoke();
            };

            _client.OnAllMonitorsReady += async () =>
            {
                if (_clientGeneration != subscribedGeneration) return;
                Debug.Log("[ConnectionViewModel] All monitors ready, firing OnAllMonitorsReady");
                OnAllMonitorsReady?.Invoke();

                // Wait for phase to become ReadyToStream (server confirms ICE ready)
                // This ensures proper state transitions
                int waitCount = 0;
                const int maxWait = 30; // 3 seconds max
                while (Phase.Value != ConnectionPhase.ReadyToStream && waitCount < maxWait)
                {
                    await Task.Delay(100);
                    if (_clientGeneration != subscribedGeneration) return; // Check after await
                    waitCount++;
                }

                if (_clientGeneration != subscribedGeneration) return; // Stale after wait

                bool useForce = Phase.Value != ConnectionPhase.ReadyToStream;
                if (useForce)
                {
                    Debug.LogWarning($"[ConnectionViewModel] Timeout waiting for ReadyToStream, current phase: {Phase.Value}, using force start");
                }

                // Delay 1 second for stability before auto-starting
                Debug.Log("[ConnectionViewModel] Delaying 1 second for stability...");
                await Task.Delay(1000);
                if (_clientGeneration != subscribedGeneration) return;

                // Auto-start streaming (server already streaming via early capture)
                Debug.Log($"[ConnectionViewModel] Auto-starting streaming (force={useForce})...");
                await StartStreamingAsync(useForce);
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
