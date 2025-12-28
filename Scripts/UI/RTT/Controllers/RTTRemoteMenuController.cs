using UnityEngine;
using TMPro;

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
    private ClusterAutoBinder _clusterBinder;
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

        // Setup V2 protocol with pipeline
        if (connectionPipeline != null)
        {
            _clusterBinder = connectionPipeline.SetupV2Protocol(transform);
            if (_clusterBinder != null)
            {
                _remoteMenuInstance.clusterBinder = _clusterBinder;
                Debug.Log("[RTTRemoteMenuController] ClusterAutoBinder assigned for V2 protocol");
            }
        }

        // Subscribe to events
        _remoteMenuInstance.OnBackClicked += HandleBackClicked;
        _remoteMenuInstance.OnConnectClicked += HandleConnectClicked;
        _remoteMenuInstance.OnStartClicked += HandleStartClicked;

        // Build the remote menu UI
        _remoteMenuInstance.BuildUI(_menuObject.transform, containerW, containerH);

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

        _clusterBinder = null;
        _menuObject = null;
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
    #endregion
}
