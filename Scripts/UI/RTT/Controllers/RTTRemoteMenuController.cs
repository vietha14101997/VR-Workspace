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

    // Remote taskbar (follows ClusterRig during streaming)
    private RTTRemoteTaskbar _remoteTaskbar;

    // Cursor tracking
    private int _activeCursorPanelIndex = -1;

    // Mipmap textures for anti-aliasing at distance
    private RenderTexture[] _mipmapTextures;
    private float _lastMipmapDebugTime;
    private const float MIPMAP_DEBUG_INTERVAL = 2f;
    #endregion

    #region Events
    public event System.Action OnBackClicked;
    public event System.Action OnConnectClicked;
    public event System.Action OnStartClicked;
    #endregion

    #region Properties
    public RTTRemoteMenu RemoteMenuInstance => _remoteMenuInstance;
    public RemoteConnectionPipeline ConnectionPipeline => connectionPipeline;
    public RTTRemoteTaskbar RemoteTaskbar => _remoteTaskbar;
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
        }
        else
        {
            Debug.LogError("[RTTRemoteMenuController] ConnectionViewModel is NULL after BuildUI!");
        }

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
            _viewModel.ResetState();
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

        // Cleanup remote taskbar
        CleanupRemoteTaskbar();

        // Cleanup mipmap textures
        CleanupMipmapTextures();

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

        // Show main menu and taskbar after cleanup
        ShowMainMenuAndTaskbar();

        _menuObject = null;
    }

    /// <summary>
    /// Poll textures from ViewModel and apply to panels when streaming.
    /// Uses mipmap textures to eliminate aliasing artifacts at distance.
    /// </summary>
    void Update()
    {
        if (_viewModel == null || _clusterRig == null) return;

        // Poll when ICE is connected or later
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
        if (panels == null || panels.Count == 0) return;

        // Debug log disabled to reduce RAM usage
        // Set PhaseProtocolClient.VerboseLogging = true to enable
        bool shouldLog = false; // Time.time - _lastMipmapDebugTime > MIPMAP_DEBUG_INTERVAL;

        for (int i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            if (panel == null) continue;

            var tex = _viewModel.GetTexture(i);
            if (tex == null) continue;

            // Generate mipmap texture for anti-aliasing at distance
            var mipmapTex = GetMipmapTexture(i, tex);
            var finalTex = mipmapTex != null ? (Texture)mipmapTex : tex;

            if (shouldLog)
            {
                Debug.Log($"[RTTRemote-MIPMAP] Panel {i}: src={tex.GetType().Name} {tex.width}x{tex.height}, mipmap={(mipmapTex != null ? "OK" : "null")}");
            }

            if (panel.contentTexture != finalTex)
            {
                panel.contentTexture = finalTex;
                panel.Apply();

                // Hide progress overlay when first texture arrives
                if (i < _progressOverlays.Count && _progressOverlays[i] != null && _progressOverlays[i].gameObject.activeSelf)
                {
                    _progressOverlays[i].Hide();
                }
            }
        }
    }

    /// <summary>
    /// Get or create a RenderTexture with mipmaps for anti-aliasing.
    /// This eliminates moire/aliasing artifacts when viewing panels at distance.
    /// </summary>
    private RenderTexture GetMipmapTexture(int index, Texture source)
    {
        if (source == null || source.width <= 0 || source.height <= 0)
        {
            return null;
        }

        // Lazy init array
        if (_mipmapTextures == null)
            _mipmapTextures = new RenderTexture[16]; // Max 16 monitors

        if (index < 0 || index >= _mipmapTextures.Length)
            return null;

        // Check if we need to create or resize
        var rt = _mipmapTextures[index];
        if (rt == null || rt.width != source.width || rt.height != source.height)
        {
            // Cleanup old
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }

            // Create new RenderTexture with mipmaps
            rt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            rt.useMipMap = true;
            rt.autoGenerateMips = false; // Manual generation for reliability
            rt.filterMode = FilterMode.Trilinear;
            rt.anisoLevel = 8;
            rt.Create();

            _mipmapTextures[index] = rt;
            Debug.Log($"[RTTRemote-MIPMAP] Created mipmap RT for panel {index}: {source.width}x{source.height}, sourceType={source.GetType().Name}");
        }

        // Copy source to mipmap texture
        try
        {
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            RenderTexture.active = prevRT;

            // Generate mipmaps
            rt.GenerateMips();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RTTRemote-MIPMAP] Failed to copy texture for panel {index}: {ex.Message}");
            return null;
        }

        return rt;
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
    public (string host, string port, int monitors, string style, string bitrate, string fps) GetConnectionSettings()
    {
        if (_remoteMenuInstance == null)
            return ("localhost", "8080", 1, "Flat Planar", "20 Mbps", "60 FPS");

        return (
            _remoteMenuInstance.Host,
            _remoteMenuInstance.Port,
            _remoteMenuInstance.MonitorIndex + 1,
            _remoteMenuInstance.Style,
            _remoteMenuInstance.Bitrate,
            _remoteMenuInstance.FPS
        );
    }


    #endregion

    #region Private Methods
    private void HandleBackClicked()
    {
        OnBackClicked?.Invoke();
    }

    private void HandleConnectClicked()
    {
        // Streaming is handled by RTTRemoteMenu → ConnectionViewModel → PhaseProtocolClient
        OnConnectClicked?.Invoke();
    }

    private void HandleStartClicked()
    {
        OnStartClicked?.Invoke();
    }

    /// <summary>
    /// Handle new flow: Create ClusterRig and attach progress overlays.
    /// </summary>
    private void HandleStartWithProgress(StreamingConfig config)
    {
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

        // Apply style from RTTRemoteMenu (Flat Planar or Curved Surround)
        if (_remoteMenuInstance != null)
        {
            int styleIndex = _remoteMenuInstance.StyleIndex; // 0=Flat Planar, 1=Curved Surround
            bool isCurvedSurround = (styleIndex == 1);
            _clusterRig.SetStyle(isCurvedSurround);
            Debug.Log($"[RTTRemoteMenuController] Applied style: {(isCurvedSurround ? "Curved Surround" : "Flat Planar")}");
        }

        // Start WebRTC update loop if not already running
        if (_webrtcUpdateCoroutine == null)
        {
            _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
        }

        // Attach progress overlays to each panel
        CleanupProgressOverlays();
        foreach (var panel in _clusterRig.panels)
        {
            var overlay = panel.gameObject.AddComponent<PanelProgressOverlay>();
            overlay.Initialize(panel);
            _progressOverlays.Add(overlay);
        }

        // Create Remote Taskbar that follows ClusterRig
        CreateRemoteTaskbar();

        // Hide main menu and taskbar when ClusterRig appears
        HideMainMenuAndTaskbar();
    }

    /// <summary>
    /// Hide RTTMenuFrame and RTTTaskbar when entering streaming mode.
    /// </summary>
    private void HideMainMenuAndTaskbar()
    {
        // Hide RTTMenuFrame (primary instance)
        RTTMenuFrame menuFrame = RTTMenuFrame.PrimaryInstance;
        if (menuFrame != null)
        {
            menuFrame.Hide();
            Debug.Log("[RTTRemoteMenuController] Hidden RTTMenuFrame");
        }

        // Hide RTTTaskbar
        RTTTaskbar taskbar = RTTTaskbar.Instance;
        if (taskbar != null)
        {
            taskbar.Hide();
            Debug.Log("[RTTRemoteMenuController] Hidden RTTTaskbar");
        }
    }

    /// <summary>
    /// Show RTTMenuFrame and RTTTaskbar when exiting streaming mode.
    /// </summary>
    private void ShowMainMenuAndTaskbar()
    {
        // Show RTTMenuFrame
        RTTMenuFrame menuFrame = RTTMenuFrame.PrimaryInstance;
        if (menuFrame != null)
        {
            menuFrame.Show();
            Debug.Log("[RTTRemoteMenuController] Shown RTTMenuFrame");
        }

        // Show RTTTaskbar
        RTTTaskbar taskbar = RTTTaskbar.Instance;
        if (taskbar != null)
        {
            taskbar.Show();
            Debug.Log("[RTTRemoteMenuController] Shown RTTTaskbar");
        }
    }

    /// <summary>
    /// Create RTTRemoteTaskbar that follows the ClusterRig.
    /// </summary>
    private void CreateRemoteTaskbar()
    {
        if (_clusterRig == null) return;

        // Cleanup existing taskbar
        CleanupRemoteTaskbar();

        // Create taskbar object
        GameObject taskbarObj = new GameObject("RTTRemoteTaskbar");

        // Parent to VirtualObjects if exists, otherwise to ClusterRig parent
        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects != null)
        {
            taskbarObj.transform.SetParent(virtualObjects.transform, false);
        }
        else
        {
            taskbarObj.transform.SetParent(_clusterRig.transform.parent, false);
        }

        // Add RTTMiniFrame first (required component)
        RTTMiniFrame frame = taskbarObj.AddComponent<RTTMiniFrame>();
        frame.Configure(
            sec1Capacity: 5,    // Back, Bitrate, FPS, Passthrough, Recenter
            sec2Capacity: 4,    // Zoom + 3 monitor slots
            btnSize: 90f,
            btnSpacing: 12f,
            height: 128f
        );

        // Add RTTRemoteTaskbar controller
        _remoteTaskbar = taskbarObj.AddComponent<RTTRemoteTaskbar>();
        _remoteTaskbar.SetFollowTarget(_clusterRig);

        // Subscribe to taskbar events
        _remoteTaskbar.OnMenuRequested += HandleTaskbarMenuRequest;
        _remoteTaskbar.OnDisconnectRequested += HandleTaskbarDisconnect;

        Debug.Log("[RTTRemoteMenuController] Created RTTRemoteTaskbar following ClusterRig");
    }

    /// <summary>
    /// Handle taskbar menu request (first back press).
    /// Hides ClusterRig and RTTRemoteTaskbar, shows RTTMenuFrame and RTTTaskbar.
    /// Does NOT fire OnBackClicked to avoid closing the app.
    /// </summary>
    private void HandleTaskbarMenuRequest()
    {
        Debug.Log("[RTTRemoteMenuController] Taskbar menu requested - returning to RemoteMenu");

        // Hide RTTRemoteTaskbar
        if (_remoteTaskbar != null)
        {
            _remoteTaskbar.Hide();
        }

        // Hide ClusterRig
        if (_clusterRig != null)
        {
            _clusterRig.gameObject.SetActive(false);
        }

        // Show RTTMenuFrame and RTTTaskbar
        ShowMainMenuAndTaskbar();

        // Note: Do NOT fire OnBackClicked here - it would close the app
        // OnBackClicked is only for when user actually wants to exit RemoteDesktop
    }

    /// <summary>
    /// Handle taskbar disconnect request.
    /// Disconnects and returns to main menu.
    /// </summary>
    private void HandleTaskbarDisconnect()
    {
        Debug.Log("[RTTRemoteMenuController] Taskbar disconnect requested");

        // Cleanup and disconnect (ShowMainMenuAndTaskbar is called in Cleanup)
        Cleanup();

        // Switch to home menu
        RTTManager appManager = RTTManager.Instance;
        if (appManager != null)
        {
            appManager.SwitchToHome();
        }
    }

    /// <summary>
    /// Resume streaming from RTTRemoteMenu overlay.
    /// Hides menu and taskbar, shows ClusterRig and RTTRemoteTaskbar again.
    /// </summary>
    public void ResumeStreaming()
    {
        Debug.Log("[RTTRemoteMenuController] Resuming streaming");

        // Hide RTTMenuFrame
        RTTMenuFrame menuFrame = RTTMenuFrame.PrimaryInstance;
        if (menuFrame != null)
        {
            menuFrame.Hide();
        }

        // Hide RTTTaskbar
        RTTTaskbar taskbar = RTTTaskbar.Instance;
        if (taskbar != null)
        {
            taskbar.Hide();
        }

        // Show ClusterRig
        if (_clusterRig != null)
        {
            _clusterRig.gameObject.SetActive(true);
        }

        // Show RTTRemoteTaskbar
        if (_remoteTaskbar != null)
        {
            _remoteTaskbar.Show();
            _remoteTaskbar.OnMenuDismissed(); // Reset back button state
        }
    }

    /// <summary>
    /// Cleanup remote taskbar.
    /// </summary>
    private void CleanupRemoteTaskbar()
    {
        if (_remoteTaskbar != null)
        {
            _remoteTaskbar.OnMenuRequested -= HandleTaskbarMenuRequest;
            _remoteTaskbar.OnDisconnectRequested -= HandleTaskbarDisconnect;
            Destroy(_remoteTaskbar.gameObject);
            _remoteTaskbar = null;
        }
    }

    /// <summary>
    /// Handle server setup progress updates.
    /// </summary>
    private void HandleServerSetupProgress(int progress)
    {
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
            foreach (var overlay in _progressOverlays)
            {
                if (overlay != null)
                {
                    overlay.Hide();
                }
            }
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
    /// Cleanup mipmap RenderTextures.
    /// </summary>
    private void CleanupMipmapTextures()
    {
        if (_mipmapTextures == null) return;

        foreach (var rt in _mipmapTextures)
        {
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
        }
        _mipmapTextures = null;
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
