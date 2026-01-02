using UnityEngine;
using TMPro;
using System.Collections.Generic;
using Unity.WebRTC;
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
    private Coroutine _webrtcUpdateCoroutine;

    // Cursor tracking
    private int _activeCursorPanelIndex = -1;

    // Instance tracking for debugging
    private static int _instanceCounter = 0;
    private int _instanceId;
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

    #region Unity Lifecycle
    private void Awake()
    {
        _instanceId = ++_instanceCounter;
        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Awake - instance created on {gameObject.name}");
    }

    private void OnEnable()
    {
        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] OnEnable - parent={transform.parent?.name ?? "null"}, " +
            $"parentActive={transform.parent?.gameObject.activeInHierarchy ?? false}");
    }

    private void OnDisable()
    {
        Debug.LogWarning($"[RTTRemoteMenuController#{_instanceId}] OnDisable - CONTROLLER DISABLED! " +
            $"parent={transform.parent?.name ?? "null"}, " +
            $"parentActive={transform.parent?.gameObject.activeInHierarchy ?? false}");
    }

    private void OnDestroy()
    {
        Debug.LogWarning($"[RTTRemoteMenuController#{_instanceId}] OnDestroy - CONTROLLER DESTROYED!");
    }
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
        // NOTE: BuildUI() calls BindToViewModel() which registers ConnectionViewModel with ServiceLocator
        _remoteMenuInstance.BuildUI(_menuObject.transform, containerW, containerH);

        // Get ViewModel AFTER BuildUI() - it's now guaranteed to be registered
        _viewModel = ServiceLocator.Get<ConnectionViewModel>();
        if (_viewModel != null)
        {
            _viewModel.OnStartWithProgress += HandleStartWithProgress;
            _viewModel.OnAllMonitorsReady += HandleAllMonitorsReady;
            _viewModel.ServerSetupProgress.OnChanged += HandleServerSetupProgress;
            _viewModel.MonitorIceProgress.OnChanged += HandleMonitorIceProgress;
            _viewModel.IsStreaming.OnChanged += HandleStreamingStateChanged;
            _viewModel.OnCursorPositionChanged += HandleCursorPosition;
            Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Subscribed to ConnectionViewModel events");
        }
        else
        {
            Debug.LogError($"[RTTRemoteMenuController#{_instanceId}] ConnectionViewModel is NULL after BuildUI!");
        }

        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Remote Menu created");
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
    /// Fully disconnects and resets connection state so reopening starts fresh.
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

        // Stop streaming and disconnect from server
        if (connectionPipeline != null)
        {
            connectionPipeline.StopStreaming();
        }

        // Reset ViewModel state synchronously FIRST (so next menu opens clean)
        // Then fire-and-forget the actual server disconnect (without state reset)
        if (_viewModel != null)
        {
            // Reset state immediately (synchronous) - this ensures next menu opens with clean state
            _viewModel.ResetState();

            // Fire and forget just stopping the client (no state reset to avoid race condition)
            _ = _viewModel.StopClientAsync();

            // Unsubscribe from ViewModel events
            _viewModel.OnStartWithProgress -= HandleStartWithProgress;
            _viewModel.OnAllMonitorsReady -= HandleAllMonitorsReady;
            _viewModel.ServerSetupProgress.OnChanged -= HandleServerSetupProgress;
            _viewModel.MonitorIceProgress.OnChanged -= HandleMonitorIceProgress;
            _viewModel.IsStreaming.OnChanged -= HandleStreamingStateChanged;
            _viewModel.OnCursorPositionChanged -= HandleCursorPosition;
        }

        // Hide cursors before cleanup
        HideAllCursors();
        _activeCursorPanelIndex = -1;

        // Cleanup progress overlays
        CleanupProgressOverlays();
        _isStreamingActive = false;

        // Stop WebRTC update coroutine
        if (_webrtcUpdateCoroutine != null)
        {
            StopCoroutine(_webrtcUpdateCoroutine);
            _webrtcUpdateCoroutine = null;
        }

        // Destroy ClusterRig
        if (_clusterRig != null)
        {
            Destroy(_clusterRig.gameObject);
            _clusterRig = null;
        }

        _menuObject = null;
        Debug.Log("[RTTRemoteMenuController] Cleanup complete - connection state reset");
    }

    /// <summary>
    /// Poll textures from ViewModel and apply to panels when streaming.
    /// Changed to poll earlier (during ICE negotiation) since OnStreamingStarted may not fire reliably.
    /// </summary>
    private int _debugLogCounter = 0;
    private int _updateCallCount = 0;

    void Update()
    {
        try
        {
            _updateCallCount++;

            // Log every 60 frames (~1 second) to check if Update is being called
            if (_updateCallCount % 60 == 1)
            {
                var phaseForLog = _viewModel?.Phase?.Value.ToString() ?? "N/A";
                var panelsInfo = _clusterRig?.panels != null ? $"Count={_clusterRig.panels.Count}" : "NULL";
                Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Update #{_updateCallCount}, " +
                    $"vm={(_viewModel != null ? "OK" : "NULL")}, " +
                    $"rig={(_clusterRig != null ? "OK" : "NULL")}, " +
                    $"phase={phaseForLog}, streaming={_isStreamingActive}, " +
                    $"panels={panelsInfo}, " +
                    $"goActive={gameObject.activeInHierarchy}");
            }

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
            if (panels == null || panels.Count == 0)
            {
                Debug.LogWarning($"[RTTRemoteMenuController#{_instanceId}] Panels null or empty! panels={(panels == null ? "NULL" : $"Count={panels.Count}")}");
                return;
            }

            // Debug log every 60 frames
            _debugLogCounter++;
            bool shouldLog = _debugLogCounter % 60 == 1;

            for (int i = 0; i < panels.Count; i++)
            {
                var panel = panels[i];
                if (panel == null) continue;

                var tex = _viewModel.GetTexture(i);

                // Debug: Log texture status periodically
                if (shouldLog && i == 0)
                {
                    Debug.Log($"[RTTRemoteMenuController#{_instanceId}] TexturePoll: phase={phase}, " +
                        $"tex={tex?.GetType().Name ?? "null"}, " +
                        $"size={(tex != null ? $"{tex.width}x{tex.height}" : "N/A")}, " +
                        $"currentTex={(panel.contentTexture != null ? "set" : "null")}");
                }

                if (tex != null && panel.contentTexture != tex)
                {
                    panel.contentTexture = tex;
                    panel.Apply();

                    // Hide progress overlay when first texture arrives
                    if (i < _progressOverlays.Count && _progressOverlays[i] != null && _progressOverlays[i].gameObject.activeSelf)
                    {
                        _progressOverlays[i].Hide();
                        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Panel {i} received first texture, hiding overlay");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RTTRemoteMenuController#{_instanceId}] Update EXCEPTION: {ex.Message}\n{ex.StackTrace}");
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
        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] StartWithProgress: {config.monitors} monitors");

        // Get or create ClusterRig via RemoteConnectionPipeline
        EnsureConnectionPipeline();
        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] connectionPipeline={(connectionPipeline != null ? "OK" : "NULL")}");

        _clusterRig = connectionPipeline.GetOrCreateClusterRig();
        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] _clusterRig={((_clusterRig != null) ? $"OK ({_clusterRig.name})" : "NULL")}");
        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] _viewModel={((_viewModel != null) ? "OK" : "NULL")}");

        if (_clusterRig == null)
        {
            Debug.LogError($"[RTTRemoteMenuController#{_instanceId}] Failed to create ClusterRig!");
            return;
        }

        // Build cluster with the specified monitor count
        _clusterRig.BuildWithPanelCount(config.monitors);
        Debug.Log($"[RTTRemoteMenuController#{_instanceId}] After BuildWithPanelCount: panels.Count={_clusterRig.panels?.Count ?? -1}");

        // Start WebRTC update loop if not already running
        if (_webrtcUpdateCoroutine == null)
        {
            _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
            Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Started WebRTC.Update() coroutine");
        }

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
            Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Streaming started, _clusterRig={(_clusterRig != null ? "OK" : "NULL")}, " +
                $"panels={(_clusterRig?.panels != null ? $"Count={_clusterRig.panels.Count}" : "NULL")}");

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
            Debug.Log($"[RTTRemoteMenuController#{_instanceId}] Streaming stopped");
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

    /// <summary>
    /// Handle cursor position updates from server.
    /// </summary>
    private void HandleCursorPosition(int monitorIndex, float u, float v, bool visible)
    {
        if (_clusterRig == null || _clusterRig.panels == null) return;

        if (!visible || monitorIndex < 0 || monitorIndex >= _clusterRig.panels.Count)
        {
            HideAllCursors();
            _activeCursorPanelIndex = -1;
            return;
        }

        // Hide cursor on previous panel if switching monitors
        if (_activeCursorPanelIndex != monitorIndex && _activeCursorPanelIndex >= 0 && _activeCursorPanelIndex < _clusterRig.panels.Count)
        {
            var oldPanel = _clusterRig.panels[_activeCursorPanelIndex];
            if (oldPanel?.cursor != null)
                oldPanel.cursor.SetVisible(false);
        }

        var panel = _clusterRig.panels[monitorIndex];
        if (panel == null) return;

        panel.EnsureCursor();
        if (panel.cursor == null) return;

        // Unity V is inverted (0 at bottom, 1 at top)
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
        if (_clusterRig == null || _clusterRig.panels == null) return;
        foreach (var panel in _clusterRig.panels)
        {
            if (panel?.cursor != null)
                panel.cursor.SetVisible(false);
        }
    }
    #endregion
}
