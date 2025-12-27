using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Multi-Track mode only implementation:
/// - Server sends N separate video tracks (one per monitor)
/// - Each panel receives its own stream (1920x1080)
/// - Fixes Android MediaCodec issues with ultra-wide resolutions
/// - Signal path is auto-generated from Stream Configuration
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
    public bool autoStart = true;

    [Header("Layout Info (auto-calculated, not shown)")]
    private int frameWidth = 3840;
    private int frameHeight = 1080;
    private int cellWidth = 1920;
    private int cellHeight = 1080;

    private MultiPCStreamClient _multiPCClient;  // N separate PeerConnections for multi-track mode
    private bool _isStreaming = false;

    public bool IsStreaming => _isStreaming;

    void Start()
    {
        if (autoStart)
        {
            StartStreaming();
        }
    }

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

    /// <summary>
    /// Stop streaming and cleanup.
    /// </summary>
    public void StopStreaming()
    {
        if (!_isStreaming) return;

        if (_multiPCClient != null)
        {
            Destroy(_multiPCClient);
            _multiPCClient = null;
        }

        _isStreaming = false;
        Debug.Log("[ClusterAutoBinder] Streaming stopped");
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

        Debug.Log($"[ClusterAutoBinder] MultiPC client created, url={_multiPCClient.signalUrl}");
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
