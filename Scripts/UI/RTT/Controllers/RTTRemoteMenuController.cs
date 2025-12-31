using UnityEngine;
using TMPro;
using System.Collections.Generic;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;
using VRWorkspace.Streaming;

/// <summary>
/// Controls the remote menu lifecycle including creation, event handling,
/// and coordination with RemoteConnectionPipeline.
/// </summary>
public class RTTRemoteMenuController : MonoBehaviour
{
    #region Configuration
    [Header("Connection Pipeline")]
    [SerializeField] private RemoteConnectionPipeline connectionPipeline;
    #endregion

    #region Private Fields
    private RTTRemoteMenu _remoteMenuInstance;
    private GameObject _menuObject;

    // Progress flow fields
    private ConnectionViewModel _viewModel;
    private WorldPanelClusterRig _clusterRig;
    private List<PanelProgressOverlay> _progressOverlays = new List<PanelProgressOverlay>();
    private bool _isStreamingActive = false;
    #endregion

    #region Events
    public event System.Action OnBackClicked;
    public event System.Action OnConnectClicked;
    public event System.Action OnStartClicked;
    #endregion

    #region Properties
    public RTTRemoteMenu RemoteMenuInstance => _remoteMenuInstance;
    public RemoteConnectionPipeline ConnectionPipeline => connectionPipeline;
    #endregion

    #region Public API
    /// <summary>
    /// Create the remote menu in the specified container.
    /// </summary>
    public GameObject CreateMenu(RectTransform container, float containerW, float containerH,
        TMP_FontAsset font, Color themeColor, Color accentColor)
    {
        if (container == null)
        {
            Debug.LogWarning("[RTTRemoteMenuController] Container is null");
            return null;
        }

        // Auto-create connection pipeline if not assigned
        EnsureConnectionPipeline();

        // Create menu container
        _menuObject = new GameObject("RemoteMenu");
        _menuObject.transform.SetParent(container, false);

        RectTransform remoteRT = _menuObject.AddComponent<RectTransform>();
        remoteRT.anchorMin = Vector2.zero;
        remoteRT.anchorMax = Vector2.one;
        remoteRT.offsetMin = Vector2.zero;
        remoteRT.offsetMax = Vector2.zero;

        // Add RTTRemoteMenu component
        _remoteMenuInstance = _menuObject.AddComponent<RTTRemoteMenu>();
        _remoteMenuInstance.themeColor = themeColor;
        _remoteMenuInstance.accentColor = accentColor;
        _remoteMenuInstance.customFont = font;

        // Subscribe to events
        _remoteMenuInstance.OnBackClicked += HandleBackClicked;
        _remoteMenuInstance.OnConnectClicked += HandleConnectClicked;
        _remoteMenuInstance.OnStartClicked += HandleStartClicked;

        // Build the remote menu UI
        _remoteMenuInstance.BuildUI(_menuObject.transform, containerW, containerH);

        // Get ViewModel and subscribe to progress flow events
        _viewModel = ServiceLocator.Get<ConnectionViewModel>();
        if (_viewModel != null)
        {
            _viewModel.OnStartWithProgress += HandleStartWithProgress;
            _viewModel.OnAllMonitorsReady += HandleAllMonitorsReady;
            _viewModel.ServerSetupProgress.OnChanged += HandleServerSetupProgress;
            _viewModel.MonitorIceProgress.OnChanged += HandleMonitorIceProgress;
            _viewModel.IsStreaming.OnChanged += HandleStreamingStateChanged;
        }

        Debug.Log("[RTTRemoteMenuController] Remote Menu created");
        return _menuObject;
    }

    /// <summary>
    /// Ensure connection pipeline exists, create if needed.
    /// </summary>
    private void EnsureConnectionPipeline()
    {
        if (connectionPipeline != null) return;

        // Try to find existing
        connectionPipeline = FindObjectOfType<RemoteConnectionPipeline>();

        // Create if not found
        if (connectionPipeline == null)
        {
            connectionPipeline = gameObject.AddComponent<RemoteConnectionPipeline>();
            Debug.Log("[RTTRemoteMenuController] Created RemoteConnectionPipeline");
        }
    }

    /// <summary>
    /// Cleanup resources and unsubscribe from events.
    /// </summary>
    public void Cleanup()
    {
        if (_remoteMenuInstance != null)
        {
            _remoteMenuInstance.OnBackClicked -= HandleBackClicked;
            _remoteMenuInstance.OnConnectClicked -= HandleConnectClicked;
            _remoteMenuInstance.OnStartClicked -= HandleStartClicked;
            _remoteMenuInstance = null;
        }

        // Unsubscribe from ViewModel events
        if (_viewModel != null)
        {
            _viewModel.OnStartWithProgress -= HandleStartWithProgress;
            _viewModel.OnAllMonitorsReady -= HandleAllMonitorsReady;
            _viewModel.ServerSetupProgress.OnChanged -= HandleServerSetupProgress;
            _viewModel.MonitorIceProgress.OnChanged -= HandleMonitorIceProgress;
            _viewModel.IsStreaming.OnChanged -= HandleStreamingStateChanged;
        }

        // Cleanup progress overlays
        CleanupProgressOverlays();
        _isStreamingActive = false;

        _menuObject = null;
    }

    /// <summary>
    /// Poll textures from ViewModel and apply to panels when streaming.
    /// Changed to poll earlier (during ICE negotiation) since OnStreamingStarted may not fire reliably.
    /// </summary>
    void Update()
    {
        if (_viewModel == null || _clusterRig == null) return;

        // Poll when ICE is connected or later, not just when streaming flag is set
        // This ensures textures are applied as soon as they're available from WebRTC
        var phase = _viewModel.Phase.Value;
        bool shouldPoll = _isStreamingActive ||
            phase == ConnectionPhase.ICENegotiating ||
            phase == ConnectionPhase.ReadyToStream ||
            phase == ConnectionPhase.StartingStream ||
            phase == ConnectionPhase.Streaming;

        if (!shouldPoll) return;

        // Poll textures from WebRTC
        _viewModel.PollTextures();

        // Apply textures to panels
        var panels = _clusterRig.panels;
        if (panels == null) return;

        for (int i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            if (panel == null) continue;

            var tex = _viewModel.GetTexture(i);
            if (tex != null && panel.contentTexture != tex)
            {
                panel.contentTexture = tex;
                panel.Apply();

                // Hide progress overlay when first texture arrives
                if (i < _progressOverlays.Count && _progressOverlays[i] != null && _progressOverlays[i].gameObject.activeSelf)
                {
                    _progressOverlays[i].Hide();
                    Debug.Log($"[RTTRemoteMenuController] Panel {i} received first texture, hiding overlay");
                }
            }
        }
    }

    /// <summary>
    /// Set the connection pipeline.
    /// </summary>
    public void SetConnectionPipeline(RemoteConnectionPipeline pipeline)
    {
        connectionPipeline = pipeline;
    }

    /// <summary>
    /// Get connection settings from the remote menu.
    /// </summary>
    public (string host, string port, int monitors, string resolution, string bitrate, string fps) GetConnectionSettings()
    {
        if (_remoteMenuInstance == null)
            return ("localhost", "8080", 1, "1920 x 1080", "20 Mbps", "60 FPS");

        return (
            _remoteMenuInstance.Host,
            _remoteMenuInstance.Port,
            _remoteMenuInstance.MonitorIndex + 1,
            _remoteMenuInstance.Resolution,
            _remoteMenuInstance.Bitrate,
            _remoteMenuInstance.FPS
        );
    }

    /// <summary>
    /// Start streaming with current settings.
    /// </summary>
    public void StartStreaming()
    {
        if (connectionPipeline == null)
        {
            Debug.LogWarning("[RTTRemoteMenuController] ConnectionPipeline not assigned");
            return;
        }

        var settings = GetConnectionSettings();
        connectionPipeline.StartStreaming(
            settings.host,
            settings.port,
            settings.monitors,
            settings.resolution,
            settings.bitrate,
            settings.fps
        );
    }
    #endregion

    #region Private Methods
    private void HandleBackClicked()
    {
        Debug.Log("[RTTRemoteMenuController] Back clicked");
        OnBackClicked?.Invoke();
    }

    private void HandleConnectClicked()
    {
        Debug.Log("[RTTRemoteMenuController] Connect clicked");

        // Start streaming with current settings
        StartStreaming();

        OnConnectClicked?.Invoke();
    }

    private void HandleStartClicked()
    {
        Debug.Log("[RTTRemoteMenuController] Start clicked");
        OnStartClicked?.Invoke();
    }

    /// <summary>
    /// Handle new flow: Create ClusterRig and attach progress overlays.
    /// </summary>
    private void HandleStartWithProgress(StreamingConfig config)
    {
        Debug.Log($"[RTTRemoteMenuController] StartWithProgress: {config.monitors} monitors");

        // Get or create ClusterRig via RemoteConnectionPipeline
        EnsureConnectionPipeline();
        _clusterRig = connectionPipeline.GetOrCreateClusterRig();

        if (_clusterRig == null)
        {
            Debug.LogError("[RTTRemoteMenuController] Failed to create ClusterRig!");
            return;
        }

        // Build cluster with the specified monitor count
        _clusterRig.BuildWithPanelCount(config.monitors);

        // Configure ClusterAutoBinder for V2 protocol
        var binder = _clusterRig.GetComponent<ClusterAutoBinder>();
        if (binder == null)
        {
            binder = _clusterRig.gameObject.AddComponent<ClusterAutoBinder>();
        }

        // Configure binder with settings from menu
        var settings = GetConnectionSettings();
        binder.autoStart = false;
        binder.useV2Protocol = true;
        binder.serverBase = $"http://{settings.host}:{settings.port}";
        binder.rig = _clusterRig;
        binder.monitorCount = config.monitors;
        binder.resolutionWidth = config.resolutionWidth;
        binder.resolutionHeight = config.resolutionHeight;
        binder.bitrateKbps = config.bitrateKbps;
        binder.fps = config.fps;

        // Attach progress overlays to each panel
        CleanupProgressOverlays();
        foreach (var panel in _clusterRig.panels)
        {
            var overlay = panel.gameObject.AddComponent<PanelProgressOverlay>();
            overlay.Initialize(panel);
            _progressOverlays.Add(overlay);
        }

        Debug.Log($"[RTTRemoteMenuController] Created {_progressOverlays.Count} progress overlays");
    }

    /// <summary>
    /// Handle server setup progress updates.
    /// </summary>
    private void HandleServerSetupProgress(int progress)
    {
        // Update all overlays with shared server progress
        foreach (var overlay in _progressOverlays)
        {
            if (overlay != null)
            {
                overlay.SetServerProgress(progress);
            }
        }
    }

    /// <summary>
    /// Handle per-monitor ICE progress updates.
    /// </summary>
    private void HandleMonitorIceProgress(Dictionary<int, int> progressDict)
    {
        for (int i = 0; i < _progressOverlays.Count; i++)
        {
            if (_progressOverlays[i] != null && progressDict.TryGetValue(i, out int progress))
            {
                _progressOverlays[i].SetIceProgress(progress);
            }
        }
    }

    /// <summary>
    /// Handle all monitors ready - show "Connecting..." state.
    /// </summary>
    private void HandleAllMonitorsReady()
    {
        Debug.Log("[RTTRemoteMenuController] All monitors ready, showing Connecting...");

        foreach (var overlay in _progressOverlays)
        {
            if (overlay != null)
            {
                overlay.ShowConnecting();
            }
        }
    }

    /// <summary>
    /// Handle streaming state change - hide overlays when streaming starts.
    /// </summary>
    private void HandleStreamingStateChanged(bool isStreaming)
    {
        _isStreamingActive = isStreaming;

        if (isStreaming)
        {
            Debug.Log("[RTTRemoteMenuController] Streaming started, hiding progress overlays and starting texture polling");

            foreach (var overlay in _progressOverlays)
            {
                if (overlay != null)
                {
                    overlay.Hide();
                }
            }
        }
        else
        {
            Debug.Log("[RTTRemoteMenuController] Streaming stopped");
        }
    }

    /// <summary>
    /// Cleanup all progress overlays.
    /// </summary>
    private void CleanupProgressOverlays()
    {
        foreach (var overlay in _progressOverlays)
        {
            if (overlay != null)
            {
                Destroy(overlay);
            }
        }
        _progressOverlays.Clear();
    }
    #endregion
}
