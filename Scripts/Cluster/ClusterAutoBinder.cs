using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VRWorkspace.Core;
using VRWorkspace.Streaming;
using VRWorkspace.ViewModels;

/// <summary>
/// Multi-Track mode only implementation:
/// - Server sends N separate video tracks (one per monitor)
/// - Each panel receives its own stream (1920x1080)
/// - Fixes Android MediaCodec issues with ultra-wide resolutions
/// - Signal path is auto-generated from Stream Configuration
///
/// Supports two connection modes:
/// - Legacy (V1): Direct WebRTC connection
/// - Phased (V2): 3-phase connection with hardware info, speed test, and auto-optimization
/// </summary>
public class ClusterAutoBinder : MonoBehaviour
{
    public string serverBase = "http://192.168.1.9:8288";
    public WorldPanelClusterRig rig;

    // Signal path is auto-generated from configuration
    private string signalPath;

    [Header("Stream Configuration")]
    [Tooltip("Number of monitors")]
    public int monitorCount = 2;
    [Tooltip("Resolution width")]
    public int resolutionWidth = 1920;
    [Tooltip("Resolution height")]
    public int resolutionHeight = 1080;
    [Tooltip("Total bitrate in kbps (will be distributed per monitor in multi-track mode)")]
    public int bitrateKbps = 8000;
    [Tooltip("Frames per second")]
    public int fps = 30;

    [Header("Auto Start")]
    [Tooltip("If true, automatically start streaming on Start(). If false, call StartStreaming() manually.")]
    public bool autoStart = false;

    [Header("Protocol")]
    [Tooltip("Use V2 protocol with 3-phase connection")]
    public bool useV2Protocol = true;

    [Header("Layout Info (auto-calculated, not shown)")]
    private int frameWidth = 3840;
    private int frameHeight = 1080;
    private int cellWidth = 1920;
    private int cellHeight = 1080;

    private MultiPCStreamClient _multiPCClient;  // N separate PeerConnections for multi-track mode
    private bool _isStreaming = false;

    // Cursor tracking - only ONE cursor should be visible across all panels
    private int _activeCursorPanelIndex = -1;

    // V2 Protocol - Phased connection state (kept for backward compatibility)
    [Obsolete("Use ConnectionPhase from VRWorkspace.Streaming instead")]
    public enum ConnectionState
    {
        Disconnected,
        Connecting,          // Connecting to server
        Connected,           // Phase 1 complete - received hardware info
        SettingUp,           // Phase 2 in progress - configuring server
        Ready,               // Phase 2 complete - ICE done, ready to stream
        Streaming,           // Phase 3 - actively streaming
        Error
    }

    // ViewModel for state management (MVVM pattern)
    private ConnectionViewModel _viewModel;

    // Legacy state (mapped from ViewModel)
    private ConnectionState _connectionState = ConnectionState.Disconnected;
    public ConnectionState CurrentState => _connectionState;

    // V2 Protocol data (now proxied from ViewModel)
    public ServerHardwareInfo HardwareInfo => _viewModel?.HardwareInfo.Value;
    public NetworkTestResult NetworkInfo => _viewModel?.NetworkInfo.Value;
    public SuggestedStreamConfig SuggestedConfig => _viewModel?.SuggestedConfig.Value;

    // Legacy events for UI integration (kept for backward compatibility)
    [Obsolete("Subscribe to ConnectionViewModel.Phase.OnChanged instead")]
    public event Action<ConnectionState> OnStateChanged;
    [Obsolete("Subscribe to ConnectionViewModel.HardwareInfo.OnChanged instead")]
    public event Action<ServerHardwareInfo> OnHardwareInfoReceived;
    [Obsolete("Subscribe to ConnectionViewModel.NetworkInfo.OnChanged instead")]
    public event Action<NetworkTestResult> OnNetworkInfoReceived;
    [Obsolete("Subscribe to ConnectionViewModel.SuggestedConfig.OnChanged instead")]
    public event Action<SuggestedStreamConfig> OnSuggestedConfigReceived;
    public event Action<string, int, string> OnConfigProgress; // step, progress%, message
    public event Action<string, double, int> OnSpeedTestProgress; // direction, currentMbps, progress%
    public event Action<string> OnError;

    public bool IsStreaming => _viewModel?.IsStreaming.Value ?? _isStreaming;
    public bool IsConnected => _viewModel?.IsConnected.Value ?? (_connectionState >= ConnectionState.Connected);

    void Start()
    {
        // Initialize ViewModel via ServiceLocator
        InitializeViewModel();

        if (autoStart)
        {
            if (useV2Protocol)
            {
                // V2: Start phased connection automatically
                _ = ConnectToServerAsync();
            }
            else
            {
                // V1: Start streaming immediately
                StartStreaming();
            }
        }
    }

    /// <summary>
    /// Initialize ConnectionViewModel and subscribe to its events.
    /// </summary>
    private void InitializeViewModel()
    {
        // Get or create ViewModel via ServiceLocator
        if (!ServiceLocator.TryGet<ConnectionViewModel>(out _viewModel))
        {
            _viewModel = new ConnectionViewModel();
            ServiceLocator.Register(_viewModel);
        }

        // Subscribe to ViewModel events to forward to legacy events
        _viewModel.Phase.OnChanged += OnViewModelPhaseChanged;
        _viewModel.HardwareInfo.OnChanged += info => OnHardwareInfoReceived?.Invoke(info);
        _viewModel.NetworkInfo.OnChanged += info => OnNetworkInfoReceived?.Invoke(info);
        _viewModel.SuggestedConfig.OnChanged += config => OnSuggestedConfigReceived?.Invoke(config);
        _viewModel.ErrorMessage.OnChanged += OnViewModelError;

        Debug.Log("[ClusterAutoBinder] ViewModel initialized");
    }

    /// <summary>
    /// Handle phase changes from ViewModel and map to legacy ConnectionState.
    /// </summary>
    private void OnViewModelPhaseChanged(ConnectionPhase phase)
    {
        // Map ConnectionPhase to legacy ConnectionState
        var newState = phase switch
        {
            ConnectionPhase.Disconnected => ConnectionState.Disconnected,
            ConnectionPhase.Connecting => ConnectionState.Connecting,
            ConnectionPhase.AwaitingHardwareInfo => ConnectionState.Connecting,
            ConnectionPhase.SpeedTesting => ConnectionState.Connecting,
            ConnectionPhase.AwaitingNetworkInfo => ConnectionState.Connecting,
            ConnectionPhase.AwaitingSuggestedConfig => ConnectionState.Connecting,
            ConnectionPhase.ConfiguringSettings => ConnectionState.Connected,
            ConnectionPhase.SendingDisplayConfig => ConnectionState.SettingUp,
            ConnectionPhase.AwaitingSetupComplete => ConnectionState.SettingUp,
            ConnectionPhase.ICENegotiating => ConnectionState.SettingUp,
            ConnectionPhase.ReadyToStream => ConnectionState.Ready,
            ConnectionPhase.StartingStream => ConnectionState.Ready,
            ConnectionPhase.Streaming => ConnectionState.Streaming,
            ConnectionPhase.Reconnecting => ConnectionState.Connecting,
            ConnectionPhase.Error => ConnectionState.Error,
            _ => ConnectionState.Disconnected
        };

        if (_connectionState != newState)
        {
            _connectionState = newState;
            OnStateChanged?.Invoke(newState);
            Debug.Log($"[ClusterAutoBinder] State: {phase} -> {newState}");
        }
    }

    /// <summary>
    /// Handle error from ViewModel.
    /// </summary>
    private void OnViewModelError(string error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            OnError?.Invoke(error);
        }
    }

    #region V2 Protocol - Phased Connection

    /// <summary>
    /// Quick validation: HTTP ping to check if server is alive (500ms timeout - reduced for speed)
    /// </summary>
    public async Task<bool> ValidateServerAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromMilliseconds(500); // Reduced from 2s to 500ms for speed

            var url = $"{serverBase}/ping";
            var response = await http.GetAsync(url);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            // Don't log - validation failure is expected on slow/unreachable servers
            return false;
        }
    }

    /// <summary>
    /// Step 1: Connect to server (Phase 1 - receive hardware info).
    /// Uses ConnectionViewModel for state management.
    /// Button: "Connect" → After complete, button shows "Setup Remote"
    /// </summary>
    public async Task<bool> ConnectToServerAsync()
    {
        // Ensure ViewModel is initialized
        if (_viewModel == null) InitializeViewModel();

        var currentPhase = _viewModel.Phase.Value;
        if (currentPhase != ConnectionPhase.Disconnected && currentPhase != ConnectionPhase.Error)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot connect from phase: {currentPhase}");
            return false;
        }

        Debug.Log("[ClusterAutoBinder] Connecting to server (V2 protocol via ViewModel)...");

        // Quick validation first
        bool serverValid = await ValidateServerAsync();
        if (!serverValid)
        {
            Debug.LogError("[ClusterAutoBinder] Server validation failed - cannot reach server");
            _viewModel.ErrorMessage.Value = "Cannot reach server. Check host and port.";
            return false;
        }

        try
        {
            // Parse host and port from serverBase
            var uri = new Uri(serverBase);
            string host = uri.Host;
            int port = uri.Port;

            // Connect via ViewModel
            await _viewModel.ConnectAsync(host, port);

            // Wait for config phase (Phase 1 complete)
            float timeout = 30f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                var phase = _viewModel.Phase.Value;
                if (phase == ConnectionPhase.ConfiguringSettings ||
                    phase == ConnectionPhase.Error ||
                    phase == ConnectionPhase.Disconnected)
                {
                    break;
                }
                await Task.Delay(100);
                elapsed += 0.1f;
            }

            return _viewModel.Phase.Value == ConnectionPhase.ConfiguringSettings;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] Connection failed: {ex.Message}");
            _viewModel.ErrorMessage.Value = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Step 2: Setup remote (Phase 2 - configure server and ICE).
    /// Uses ConnectionViewModel for state management.
    /// Button: "Setup Remote" → After complete, button shows "Start Remote"
    /// </summary>
    public async Task<bool> SetupRemoteAsync()
    {
        if (_viewModel == null)
        {
            Debug.LogError("[ClusterAutoBinder] ViewModel is null!");
            return false;
        }

        var currentPhase = _viewModel.Phase.Value;
        Debug.Log($"[ClusterAutoBinder] SetupRemoteAsync called, current phase: {currentPhase}");

        if (currentPhase != ConnectionPhase.ConfiguringSettings)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot setup from phase: {currentPhase}");
            return false;
        }

        Debug.Log("[ClusterAutoBinder] Setting up remote via ViewModel...");

        try
        {
            // Build config from current settings
            int totalBitrateKbps = bitrateKbps * monitorCount;

            var config = new StreamingConfig
            {
                monitors = monitorCount,
                resolutionWidth = resolutionWidth,
                resolutionHeight = resolutionHeight,
                refreshRate = 60,
                bitrateKbps = totalBitrateKbps,
                fps = fps
            };

            Debug.Log($"[ClusterAutoBinder] Applying config: {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}, {config.fps}fps, {totalBitrateKbps}kbps total");

            // Accept config via ViewModel
            await _viewModel.AcceptConfigAsync(config);

            // Wait for ReadyToStream phase
            float timeout = 60f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                var phase = _viewModel.Phase.Value;
                if (phase == ConnectionPhase.ReadyToStream ||
                    phase == ConnectionPhase.Error ||
                    phase == ConnectionPhase.Disconnected)
                {
                    break;
                }
                await Task.Delay(100);
                elapsed += 0.1f;
            }

            return _viewModel.Phase.Value == ConnectionPhase.ReadyToStream;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] Setup failed: {ex.Message}");
            _viewModel.ErrorMessage.Value = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Step 3: Start streaming (Phase 3).
    /// Uses ConnectionViewModel for state management.
    /// Button: "Start Remote" → Hide menu, create rig, start receiving frames
    /// </summary>
    public async Task<bool> StartRemoteAsync()
    {
        if (_viewModel == null)
        {
            Debug.LogError("[ClusterAutoBinder] ViewModel is null!");
            return false;
        }

        var currentPhase = _viewModel.Phase.Value;
        if (currentPhase != ConnectionPhase.ReadyToStream)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot start from phase: {currentPhase}");
            return false;
        }

        Debug.Log("[ClusterAutoBinder] Starting remote streaming via ViewModel...");

        try
        {
            // Build rig if needed
            if (!rig) rig = GetComponent<WorldPanelClusterRig>();
            if (rig != null && (rig.panels == null || rig.panels.Count != monitorCount))
            {
                CalculateLayoutInfo();
                rig.BuildWithPanelCount(monitorCount);
            }

            // Start streaming via ViewModel
            await _viewModel.StartStreamingAsync();

            // State will change to Streaming via ViewModel
            _isStreaming = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] Start streaming failed: {ex.Message}");
            _viewModel.ErrorMessage.Value = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Apply suggested config values from server to local settings.
    /// </summary>
    public void ApplySuggestedConfig()
    {
        if (SuggestedConfig == null) return;

        monitorCount = SuggestedConfig.monitors;
        resolutionWidth = SuggestedConfig.resolutionWidth;
        resolutionHeight = SuggestedConfig.resolutionHeight;
        fps = SuggestedConfig.fps;
        bitrateKbps = SuggestedConfig.bitrateKbps;

        Debug.Log($"[ClusterAutoBinder] Applied suggested config: {monitorCount}mon @ {resolutionWidth}x{resolutionHeight}, {fps}fps, {bitrateKbps}kbps");
    }

    /// <summary>
    /// Legacy state setter. State is now managed by ViewModel.
    /// </summary>
    [Obsolete("State is now managed by ConnectionViewModel")]
    private void SetState(ConnectionState newState)
    {
        if (_connectionState == newState) return;
        var oldState = _connectionState;
        _connectionState = newState;
        Debug.Log($"[ClusterAutoBinder] Legacy SetState: {oldState} -> {newState}");
        OnStateChanged?.Invoke(newState);
    }

    // Legacy handlers - kept for backward compatibility with MultiPCStreamClient direct usage
    // These are only used in V1 legacy mode

    [Obsolete("Use ViewModel event handlers instead")]
    private void HandleHardwareInfo(ServerHardwareInfo info)
    {
        Debug.Log($"[ClusterAutoBinder] Legacy: Received hardware info: {info.deviceName}, {info.gpu}");
        OnHardwareInfoReceived?.Invoke(info);
    }

    [Obsolete("Use ViewModel event handlers instead")]
    private void HandleNetworkInfo(NetworkTestResult info)
    {
        Debug.Log($"[ClusterAutoBinder] Legacy: Received network info: {info.connectionType}, {info.pingMs:F1}ms, {info.bandwidthMbps:F0}Mbps");
        OnNetworkInfoReceived?.Invoke(info);
    }

    [Obsolete("Use ViewModel event handlers instead")]
    private void HandleSuggestedConfig(SuggestedStreamConfig config)
    {
        Debug.Log($"[ClusterAutoBinder] Legacy: Received suggested config: {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}");
        SetState(ConnectionState.Connected);
        OnSuggestedConfigReceived?.Invoke(config);
    }

    private void HandleConfigProgress(string step, int progress, string message)
    {
        Debug.Log($"[ClusterAutoBinder] Config progress: {step} {progress}% - {message}");
        OnConfigProgress?.Invoke(step, progress, message);
    }

    [Obsolete("Use ViewModel event handlers instead")]
    private void HandleStreamingStarted()
    {
        Debug.Log("[ClusterAutoBinder] Legacy: Streaming started!");
        SetState(ConnectionState.Streaming);
        _isStreaming = true;
    }

    [Obsolete("Use ViewModel event handlers instead")]
    private void HandleError(string error)
    {
        Debug.LogError($"[ClusterAutoBinder] Legacy error: {error}");
        SetState(ConnectionState.Error);
        OnError?.Invoke(error);
    }

    private void HandleSpeedTestProgress(string direction, double currentMbps, int progress)
    {
        OnSpeedTestProgress?.Invoke(direction, currentMbps, progress);
    }

    /// <summary>
    /// Handle cursor position update from server and sync with panel cursor.
    /// Only ONE cursor is visible across all panels at any time.
    /// </summary>
    private void HandleCursorPosition(int monitorIndex, float u, float v, bool visible)
    {
        if (rig == null || rig.panels == null)
            return;

        // If cursor is not visible or monitorIndex is invalid, hide all cursors
        if (!visible || monitorIndex < 0 || monitorIndex >= rig.panels.Count)
        {
            HideAllCursors();
            _activeCursorPanelIndex = -1;
            return;
        }

        // If cursor moved to a different panel, hide cursor on old panel
        if (_activeCursorPanelIndex != monitorIndex && _activeCursorPanelIndex >= 0 && _activeCursorPanelIndex < rig.panels.Count)
        {
            var oldPanel = rig.panels[_activeCursorPanelIndex];
            if (oldPanel != null && oldPanel.cursor != null)
            {
                oldPanel.cursor.SetVisible(false);
            }
        }

        // Update cursor on new panel
        var panel = rig.panels[monitorIndex];
        if (panel == null) return;

        panel.EnsureCursor();
        if (panel.cursor == null) return;

        // Convert from top-left origin (Windows) to bottom-left origin (Unity UV)
        // Server sends v where 0 = top, 1 = bottom
        // Unity cursor expects v where 0 = bottom, 1 = top
        float unityV = 1f - v;
        panel.cursor.SetUV(u, unityV, silent: true);
        panel.cursor.SetVisible(true);

        _activeCursorPanelIndex = monitorIndex;
    }

    /// <summary>
    /// Hide cursors on all panels.
    /// </summary>
    private void HideAllCursors()
    {
        if (rig == null || rig.panels == null) return;
        foreach (var panel in rig.panels)
        {
            if (panel != null && panel.cursor != null)
            {
                panel.cursor.SetVisible(false);
            }
        }
    }

    #endregion

    #region V1 Protocol - Legacy Direct Connection

    /// <summary>
    /// Start streaming with current configuration (V1 legacy mode).
    /// Can be called manually after configuring the binder properties.
    /// </summary>
    public void StartStreaming()
    {
        if (_isStreaming)
        {
            Debug.LogWarning("[ClusterAutoBinder] Already streaming!");
            return;
        }

        if (!rig) rig = GetComponent<WorldPanelClusterRig>();
        if (!rig)
        {
            Debug.LogError("[ClusterAutoBinder] No WorldPanelClusterRig found!");
            return;
        }

        // Generate signal path from configuration
        GenerateSignalPath();

        // Calculate layout based on configuration
        CalculateLayoutInfo();

        Debug.Log($"[ClusterAutoBinder] MULTI-TRACK mode: {monitorCount} monitors, {resolutionWidth}x{resolutionHeight}, {bitrateKbps}kbps, {fps}fps");
        Debug.Log($"[ClusterAutoBinder] Signal: {signalPath}");
        Debug.Log($"[ClusterAutoBinder] Layout: frame={frameWidth}x{frameHeight}, cell={cellWidth}x{cellHeight}");

        // Build panels based on monitor count before binding (if not already built)
        if (rig.panels == null || rig.panels.Count != monitorCount)
        {
            rig.BuildWithPanelCount(monitorCount);
        }

        // Multi-track mode is now default and only option
        BindPanelsMultiTrack();

        _isStreaming = true;
        SetState(ConnectionState.Streaming);
    }

    #endregion

    /// <summary>
    /// Stop streaming and cleanup.
    /// Uses ViewModel for proper state management.
    /// </summary>
    public async void StopStreaming()
    {
        // Stop via ViewModel if available
        if (_viewModel != null)
        {
            await _viewModel.DisconnectAsync();
        }

        // Legacy cleanup for MultiPCStreamClient
        if (_multiPCClient != null)
        {
            // Unsubscribe events
            _multiPCClient.OnHardwareInfoReceived -= HandleHardwareInfo;
            _multiPCClient.OnNetworkInfoReceived -= HandleNetworkInfo;
            _multiPCClient.OnSuggestedConfigReceived -= HandleSuggestedConfig;
            _multiPCClient.OnConfigProgress -= HandleConfigProgress;
            _multiPCClient.OnStreamingStarted -= HandleStreamingStarted;
            _multiPCClient.OnConnectionError -= HandleError;
            _multiPCClient.OnSpeedTestProgress -= HandleSpeedTestProgress;
            _multiPCClient.OnCursorPosition -= HandleCursorPosition;

            if (_multiPCClient.useV2Protocol)
            {
                _ = _multiPCClient.StopV2Async();
            }

            Destroy(_multiPCClient.gameObject);
            _multiPCClient = null;
        }

        _isStreaming = false;
        Debug.Log("[ClusterAutoBinder] Streaming stopped");
    }

    /// <summary>
    /// Disconnect without stopping (for reconnection).
    /// </summary>
    public void Disconnect()
    {
        StopStreaming();
    }

    void BindPanelsMultiTrack()
    {
        var panels = rig.panels;
        if (panels == null || panels.Count == 0)
        {
            Debug.LogError("[ClusterAutoBinder] No panels found in rig!");
            return;
        }

        // Create MultiPCStreamClient (Option B: N separate PeerConnections)
        var centerPanel = panels[panels.Count / 2];
        _multiPCClient = centerPanel.gameObject.AddComponent<MultiPCStreamClient>();

        var wsBase = serverBase.Replace("http://", "ws://").Replace("https://", "wss://");
        _multiPCClient.signalUrl = $"{wsBase}/{signalPath}";
        _multiPCClient.useV2Protocol = useV2Protocol;

        // Assign all panels to the multi-PC client
        _multiPCClient.panels = panels.ToArray();

        Debug.Log($"[ClusterAutoBinder] MultiPC client created, url={_multiPCClient.signalUrl}, V2={useV2Protocol}");
        Debug.Log($"[ClusterAutoBinder] Bound {panels.Count} panels to {panels.Count} PeerConnections");
    }

    void OnDestroy()
    {
        // Unsubscribe from ViewModel events
        if (_viewModel != null)
        {
            _viewModel.Phase.OnChanged -= OnViewModelPhaseChanged;
            _viewModel.ErrorMessage.OnChanged -= OnViewModelError;
        }

        StopStreaming();
    }

    void GenerateSignalPath()
    {
        // Calculate bitrate per monitor for multi-track mode
        int bitratePerMonitor = bitrateKbps;
        if (monitorCount > 1)
        {
            bitratePerMonitor = bitrateKbps / monitorCount;
            // Ensure minimum bitrate per monitor
            bitratePerMonitor = Mathf.Max(bitratePerMonitor, 2000);
        }

        // Generate signal path for multi-track mode
        signalPath = $"signal?mode=multitrack&monitors={monitorCount}&resW={resolutionWidth}&resH={resolutionHeight}&kbps={bitratePerMonitor}&fps={fps}&zerolat=1&lan=1";

        // Add protocol version for V2
        if (useV2Protocol)
        {
            signalPath += "&protocol=v2";
        }
    }

    void CalculateLayoutInfo()
    {
        // Calculate layout based on monitor count and resolution
        // For side-by-side layout (horizontal arrangement):
        
        // Each monitor (cell) has the original resolution
        cellWidth = resolutionWidth;
        cellHeight = resolutionHeight;
        
        // Frame is the combined width of all monitors side-by-side
        frameWidth = resolutionWidth * monitorCount;
        frameHeight = resolutionHeight;
    }
}
