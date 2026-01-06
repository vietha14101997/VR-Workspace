using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VRWorkspace.Core;
using VRWorkspace.Streaming;
using VRWorkspace.ViewModels;

/// <summary>
/// Connection mode for ClusterAutoBinder.
/// </summary>
public enum ConnectionMode
{
    WiFi,   // Connect via WiFi (requires QR code scan or manual IP)
    USB     // Connect via USB Tethering
}

/// <summary>
/// Multi-Track mode implementation using V2 protocol:
/// - Server sends N separate video tracks (one per monitor)
/// - Each panel receives its own stream (1920x1080)
/// - Fixes Android MediaCodec issues with ultra-wide resolutions
/// - Signal path is auto-generated from Stream Configuration
/// - Uses 3-phase connection with hardware info, speed test, and auto-optimization
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

    [Header("Connection Mode")]
    [Tooltip("USB mode uses USB Tethering (more stable, lower latency)")]
    public ConnectionMode connectionMode = ConnectionMode.WiFi;

    [Tooltip("Port for USB mode (default 8288)")]
    public int usbPort = 8288;

    [Header("Protocol")]
    [Tooltip("Deprecated: V2 protocol is now always used")]
    [System.Obsolete("V2 protocol is now always used. This field is kept for API compatibility.")]
    public bool useV2Protocol = true;

    [Header("Layout Info (auto-calculated, not shown)")]
    private int frameWidth = 3840;
    private int frameHeight = 1080;
    private int cellWidth = 1920;
    private int cellHeight = 1080;

    private MultiPCStreamClient _multiPCClient;  // N separate PeerConnections for multi-track mode
    private bool _isStreaming = false;

    // ViewModel for state management (MVVM pattern)
    private ConnectionViewModel _viewModel;

    // V2 Protocol data (proxied from ViewModel)
    public ServerHardwareInfo HardwareInfo => _viewModel?.HardwareInfo.Value;
    public NetworkTestResult NetworkInfo => _viewModel?.NetworkInfo.Value;
    public SuggestedStreamConfig SuggestedConfig => _viewModel?.SuggestedConfig.Value;

    // Events for UI integration
    public event Action<string, int, string> OnConfigProgress; // step, progress%, message
    public event Action<string, double, int> OnSpeedTestProgress; // direction, currentMbps, progress%
    public event Action<string> OnError;

    public bool IsStreaming => _viewModel?.IsStreaming.Value ?? _isStreaming;
    public bool IsConnected => _viewModel?.IsConnected.Value ?? false;

    void Start()
    {
        // Initialize ViewModel via ServiceLocator
        InitializeViewModel();

        if (autoStart)
        {
            // Start phased connection automatically (V2 protocol)
            _ = ConnectToServerAsync();
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

        // Subscribe to ViewModel error events
        _viewModel.ErrorMessage.OnChanged += OnViewModelError;

        Debug.Log("[ClusterAutoBinder] ViewModel initialized");
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
        // Use USB mode if configured
        if (connectionMode == ConnectionMode.USB)
        {
            return await ConnectUSBAsync();
        }

        // Ensure ViewModel is initialized
        if (_viewModel == null) InitializeViewModel();

        var currentPhase = _viewModel.Phase.Value;
        if (currentPhase != ConnectionPhase.Disconnected && currentPhase != ConnectionPhase.Error)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot connect from phase: {currentPhase}");
            return false;
        }

        Debug.Log("[ClusterAutoBinder] Connecting to server via WiFi (V2 protocol)...");

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

            // Connect via ViewModel (WiFi mode)
            await _viewModel.ConnectAsync(host, port);

            // Wait for config phase (Phase 1 complete)
            return await WaitForConfigPhaseAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] Connection failed: {ex.Message}");
            _viewModel.ErrorMessage.Value = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Connect to server via USB Tethering.
    /// More stable and lower latency than WiFi.
    /// NOTE: This requires USB Tethering IP to be set. Use ConnectUSBAsync(usbTetheringIP) instead.
    /// </summary>
    [System.Obsolete("Use ConnectUSBAsync(string usbTetheringIP) instead")]
    public async Task<bool> ConnectUSBAsync()
    {
        Debug.LogError("[ClusterAutoBinder] USB mode requires USB Tethering IP. Use ConnectUSBAsync(usbTetheringIP) instead.");
        return false;
    }

    /// <summary>
    /// Connect to server via USB Tethering.
    /// More stable and lower latency than WiFi.
    /// </summary>
    /// <param name="usbTetheringIP">USB Tethering IP (e.g., 192.168.42.1)</param>
    public async Task<bool> ConnectUSBAsync(string usbTetheringIP)
    {
        if (string.IsNullOrEmpty(usbTetheringIP))
        {
            Debug.LogError("[ClusterAutoBinder] USB Tethering IP is required");
            return false;
        }

        // Ensure ViewModel is initialized
        if (_viewModel == null) InitializeViewModel();

        var currentPhase = _viewModel.Phase.Value;
        if (currentPhase != ConnectionPhase.Disconnected && currentPhase != ConnectionPhase.Error)
        {
            Debug.LogWarning($"[ClusterAutoBinder] Cannot connect from phase: {currentPhase}");
            return false;
        }

        Debug.Log($"[ClusterAutoBinder] Connecting to server via USB Tethering ({usbTetheringIP}:{usbPort})...");

        try
        {
            // Connect via ViewModel (USB Tethering mode)
            await _viewModel.ConnectUSBAsync(usbPort, usbTetheringIP);

            // Wait for config phase (Phase 1 complete)
            return await WaitForConfigPhaseAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ClusterAutoBinder] USB connection failed: {ex.Message}");
            _viewModel.ErrorMessage.Value = $"USB connection failed. Ensure USB Tethering is enabled.\n{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Wait for the connection to reach ConfiguringSettings phase.
    /// </summary>
    private async Task<bool> WaitForConfigPhaseAsync()
    {
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

    private void HandleConfigProgress(string step, int progress, string message)
    {
        Debug.Log($"[ClusterAutoBinder] Config progress: {step} {progress}% - {message}");
        OnConfigProgress?.Invoke(step, progress, message);
    }

    private void HandleSpeedTestProgress(string direction, double currentMbps, int progress)
    {
        OnSpeedTestProgress?.Invoke(direction, currentMbps, progress);
    }

    #endregion

    #region Streaming Control

    /// <summary>
    /// Start streaming with current configuration.
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

        // Cleanup MultiPCStreamClient
        if (_multiPCClient != null)
        {
            // Unsubscribe events
            _multiPCClient.OnConfigProgress -= HandleConfigProgress;
            _multiPCClient.OnSpeedTestProgress -= HandleSpeedTestProgress;

            _ = _multiPCClient.StopV2Async();

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

        // Assign all panels to the multi-PC client
        _multiPCClient.panels = panels.ToArray();

        // Subscribe to events
        _multiPCClient.OnConfigProgress += HandleConfigProgress;
        _multiPCClient.OnSpeedTestProgress += HandleSpeedTestProgress;

        Debug.Log($"[ClusterAutoBinder] MultiPC client created, url={_multiPCClient.signalUrl}");
        Debug.Log($"[ClusterAutoBinder] Bound {panels.Count} panels to {panels.Count} PeerConnections");
    }

    void OnDestroy()
    {
        // Unsubscribe from ViewModel events
        if (_viewModel != null)
        {
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

        // Generate signal path for multi-track mode (V2 protocol)
        signalPath = $"signal?mode=multitrack&monitors={monitorCount}&resW={resolutionWidth}&resH={resolutionHeight}&kbps={bitratePerMonitor}&fps={fps}&zerolat=1&lan=1&protocol=v2";
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
