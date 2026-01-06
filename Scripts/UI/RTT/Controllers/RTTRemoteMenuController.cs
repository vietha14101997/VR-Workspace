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
