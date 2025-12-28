using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VRWorkspace.Streaming;

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

    // V2 Protocol - Phased connection state
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

    private ConnectionState _connectionState = ConnectionState.Disconnected;
    public ConnectionState CurrentState => _connectionState;

    // V2 Protocol data (received from server)
    public ServerHardwareInfo HardwareInfo { get; private set; }
    public NetworkTestResult NetworkInfo { get; private set; }
    public SuggestedStreamConfig SuggestedConfig { get; private set; }

    // Events for UI integration
    public event Action<ConnectionState> OnStateChanged;
    public event Action<ServerHardwareInfo> OnHardwareInfoReceived;
    public event Action<NetworkTestResult> OnNetworkInfoReceived;
    public event Action<SuggestedStreamConfig> OnSuggestedConfigReceived;
    public event Action<string, int, string> OnConfigProgress; // step, progress%, message
    public event Action<string, double, int> OnSpeedTestProgress; // direction, currentMbps, progress%
    public event Action<string> OnError;

    public bool IsStreaming => _isStreaming;
    public bool IsConnected => _connectionState >= ConnectionState.Connected;

    void Start()
    {
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

    #region V2 Protocol - Phased Connection

    /// <summary>
    /// Quick validation: HTTP ping to check if server is alive (100-500ms timeout)
    /// </summary>
    public async Task<bool> ValidateServerAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(2); // Max 2 giây

            var url = $"{serverBase}/ping";
            Debug.Log($"[ClusterAutoBinder] Validating server at {url}...");

            var response = await http.GetAsync(url);
            bool valid = response.IsSuccessStatusCode;

            Debug.Log($"[ClusterAutoBinder] Server validation: {(valid ? "OK" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Server validation failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Step 1: Connect to server (Phase 1 - receive hardware info).
    /// Button: "Connect" → After complete, button shows "Setup Remote"
    /// </summary>
    public async Task<bool> ConnectToServerAsync()
    {
        if (_connectionState != ConnectionState.Disconnected)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot connect from state: {_connectionState}");
            return false;
        }

        SetState(ConnectionState.Connecting);
        Debug.Log("[ClusterAutoBinder] Connecting to server (V2 protocol)...");

        // Quick validation first
        bool serverValid = await ValidateServerAsync();
        if (!serverValid)
        {
            Debug.LogError("[ClusterAutoBinder] Server validation failed - cannot reach server");
            SetState(ConnectionState.Error);
            OnError?.Invoke("Cannot reach server. Check host and port.");
            return false;
        }

        try
        {
            // Create MultiPCStreamClient with V2 protocol
            // IMPORTANT: Do NOT parent to this transform because WorldPanelClusterRig.KillChildren()
            // will destroy all children when rebuilding panels, which would kill the WebSocket connection!
            if (_multiPCClient == null)
            {
                var go = new GameObject("MultiPCStreamClient");
                // Keep at scene root to survive panel rebuilds
                _multiPCClient = go.AddComponent<MultiPCStreamClient>();
            }

            _multiPCClient.useV2Protocol = true;
            _multiPCClient.autoAcceptSuggestedConfig = false; // We want manual control
            _multiPCClient.autoStartConnection = false; // We'll connect manually after setting up

            // Subscribe to V2 events
            _multiPCClient.OnHardwareInfoReceived += HandleHardwareInfo;
            _multiPCClient.OnNetworkInfoReceived += HandleNetworkInfo;
            _multiPCClient.OnSuggestedConfigReceived += HandleSuggestedConfig;
            _multiPCClient.OnConfigProgress += HandleConfigProgress;
            _multiPCClient.OnStreamingStarted += HandleStreamingStarted;
            _multiPCClient.OnConnectionError += HandleError;
            _multiPCClient.OnSpeedTestProgress += HandleSpeedTestProgress;
            _multiPCClient.OnCursorPosition += HandleCursorPosition;

            // Build signal URL
            var wsBase = serverBase.Replace("http://", "ws://").Replace("https://", "wss://");
            GenerateSignalPath();
            _multiPCClient.signalUrl = $"{wsBase}/{signalPath}";

            Debug.Log($"[ClusterAutoBinder] Signal URL: {_multiPCClient.signalUrl}");

            // Manually start connection (after events are subscribed)
            await _multiPCClient.ConnectV2Async();

            // Wait for hardware info to be received (state changes to Connected)
            float timeout = 30f;
            float elapsed = 0f;
            while (_connectionState == ConnectionState.Connecting && elapsed < timeout)
            {
                await Task.Delay(100);
                elapsed += 0.1f;
            }

            return _connectionState == ConnectionState.Connected;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] Connection failed: {ex.Message}");
            SetState(ConnectionState.Error);
            OnError?.Invoke(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Step 2: Setup remote (Phase 2 - configure server and ICE).
    /// Button: "Setup Remote" → After complete, button shows "Start Remote"
    /// </summary>
    public async Task<bool> SetupRemoteAsync()
    {
        Debug.Log($"[ClusterAutoBinder] SetupRemoteAsync called, current state: {_connectionState}");

        if (_connectionState != ConnectionState.Connected)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot setup from state: {_connectionState}");
            return false;
        }

        if (_multiPCClient == null)
        {
            Debug.LogError("[ClusterAutoBinder] _multiPCClient is null!");
            return false;
        }

        SetState(ConnectionState.SettingUp);
        Debug.Log("[ClusterAutoBinder] Setting up remote...");

        try
        {
            // Sử dụng giá trị từ properties (đã được set từ dropdown qua ApplyFormToBinder)
            // bitrateKbps từ dropdown là bitrate MỖI MONITOR, nhân với số monitor để có tổng
            int totalBitrateKbps = bitrateKbps * monitorCount;

            var config = new StreamingConfig
            {
                monitors = monitorCount,
                resolutionWidth = resolutionWidth,
                resolutionHeight = resolutionHeight,
                refreshRate = 60,
                bitrateKbps = totalBitrateKbps,  // Total bitrate = per-monitor * monitors
                fps = fps
            };

            Debug.Log($"[ClusterAutoBinder] Applying config: {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}, {config.fps}fps, {bitrateKbps}kbps/mon = {totalBitrateKbps}kbps total");
            Debug.Log("[ClusterAutoBinder] Calling _multiPCClient.ApplyConfigAsync...");

            await _multiPCClient.ApplyConfigAsync(config);
            Debug.Log("[ClusterAutoBinder] ApplyConfigAsync returned");

            // Wait for ICE to complete (state becomes Ready)
            float timeout = 60f;
            float elapsed = 0f;
            while (_connectionState == ConnectionState.SettingUp && elapsed < timeout)
            {
                // Check if state machine reached ReadyToStream
                if (_multiPCClient.StateMachine?.CurrentPhase == ConnectionPhase.ReadyToStream)
                {
                    SetState(ConnectionState.Ready);
                    break;
                }
                await Task.Delay(100);
                elapsed += 0.1f;
            }

            return _connectionState == ConnectionState.Ready;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] Setup failed: {ex.Message}");
            SetState(ConnectionState.Error);
            OnError?.Invoke(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Step 3: Start streaming (Phase 3).
    /// Button: "Start Remote" → Hide menu, create rig, start receiving frames
    /// </summary>
    public async Task<bool> StartRemoteAsync()
    {
        if (_connectionState != ConnectionState.Ready)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot start from state: {_connectionState}");
            return false;
        }

        Debug.Log("[ClusterAutoBinder] Starting remote streaming...");

        try
        {
            // Build rig if needed
            if (!rig) rig = GetComponent<WorldPanelClusterRig>();
            if (rig != null && (rig.panels == null || rig.panels.Count != monitorCount))
            {
                CalculateLayoutInfo();
                rig.BuildWithPanelCount(monitorCount);
            }

            // Assign panels to MultiPCStreamClient
            if (rig != null && rig.panels != null)
            {
                _multiPCClient.panels = rig.panels.ToArray();
            }

            // Start streaming
            await _multiPCClient.StartStreamingV2Async();

            // State will change to Streaming via event handler
            _isStreaming = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] Start streaming failed: {ex.Message}");
            SetState(ConnectionState.Error);
            OnError?.Invoke(ex.Message);
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

    private void SetState(ConnectionState newState)
    {
        if (_connectionState == newState) return;
        var oldState = _connectionState;
        _connectionState = newState;
        Debug.Log($"[ClusterAutoBinder] State: {oldState} -> {newState}");
        OnStateChanged?.Invoke(newState);
    }

    private void HandleHardwareInfo(ServerHardwareInfo info)
    {
        HardwareInfo = info;
        Debug.Log($"[ClusterAutoBinder] Received hardware info: {info.deviceName}, {info.gpu}");
        OnHardwareInfoReceived?.Invoke(info);
    }

    private void HandleNetworkInfo(NetworkTestResult info)
    {
        NetworkInfo = info;
        Debug.Log($"[ClusterAutoBinder] Received network info: {info.connectionType}, {info.pingMs:F1}ms, {info.bandwidthMbps:F0}Mbps");
        OnNetworkInfoReceived?.Invoke(info);
    }

    private void HandleSuggestedConfig(SuggestedStreamConfig config)
    {
        SuggestedConfig = config;
        Debug.Log($"[ClusterAutoBinder] Received suggested config: {config.monitors}mon @ {config.resolutionWidth}x{config.resolutionHeight}");

        // Phase 1 complete - we have all info
        SetState(ConnectionState.Connected);
        OnSuggestedConfigReceived?.Invoke(config);
    }

    private void HandleConfigProgress(string step, int progress, string message)
    {
        Debug.Log($"[ClusterAutoBinder] Config progress: {step} {progress}% - {message}");
        OnConfigProgress?.Invoke(step, progress, message);
    }

    private void HandleStreamingStarted()
    {
        Debug.Log("[ClusterAutoBinder] Streaming started!");
        SetState(ConnectionState.Streaming);
        _isStreaming = true;
    }

    private void HandleError(string error)
    {
        Debug.LogError($"[ClusterAutoBinder] Error: {error}");
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
    /// </summary>
    public void StopStreaming()
    {
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
        SetState(ConnectionState.Disconnected);
        HardwareInfo = null;
        NetworkInfo = null;
        SuggestedConfig = null;

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
