using UnityEngine;
using TMPro;
using VRWorkspace.UI;
using VRWorkspace.Streaming;

/// <summary>
/// Controls the browser view lifecycle including creation, event handling,
/// and integration with RTTMenuManager.
/// </summary>
public class RTTBrowserController : MonoBehaviour
{
    #region Private Fields
    private RTTBrowserView _browserViewInstance;
    private GameObject _browserObject;
    #endregion

    #region Events
    public event System.Action OnBackClicked;
    public event System.Action<string> OnUrlChanged;
    #endregion

    #region Properties
    public RTTBrowserView BrowserViewInstance => _browserViewInstance;
    public bool IsActive => _browserObject != null && _browserObject.activeSelf;
    #endregion

    #region Public API
    /// <summary>
    /// Create the browser view in the specified container.
    /// </summary>
    public GameObject CreateBrowser(RectTransform container, float containerW, float containerH,
        TMP_FontAsset font, Color themeColor, Color accentColor)
    {
        if (container == null)
        {
            Debug.LogWarning("[RTTBrowserController] Container is null");
            return null;
        }

        // Create browser container
        _browserObject = new GameObject("BrowserView");
        _browserObject.transform.SetParent(container, false);

        RectTransform browserRT = _browserObject.AddComponent<RectTransform>();
        browserRT.anchorMin = Vector2.zero;
        browserRT.anchorMax = Vector2.one;
        browserRT.offsetMin = Vector2.zero;
        browserRT.offsetMax = Vector2.zero;

        // Add RTTBrowserView component
        _browserViewInstance = _browserObject.AddComponent<RTTBrowserView>();
        _browserViewInstance.themeColor = themeColor;
        _browserViewInstance.accentColor = accentColor;
        _browserViewInstance.customFont = font;

        // Subscribe to events
        _browserViewInstance.OnBackClicked += HandleBackClicked;
        _browserViewInstance.OnUrlChanged += HandleUrlChanged;

        // Build the browser UI
        _browserViewInstance.BuildUI(_browserObject.transform, containerW, containerH);

        Debug.Log("[RTTBrowserController] Browser view created");
        return _browserObject;
    }

    /// <summary>
    /// Load webrtc_protocolv2.html with connection parameters.
    /// Gets host/port from RemotePreferences.
    /// </summary>
    public void LoadWebRTCProtocol()
    {
        if (_browserViewInstance == null) return;

        // Get connection settings from preferences
        string host = "192.168.1.7";
        int port = 5100;

        var prefs = RemotePreferences.Load();
        if (!string.IsNullOrEmpty(prefs.lastHost))
            host = prefs.lastHost;
        if (!string.IsNullOrEmpty(prefs.lastPort) && int.TryParse(prefs.lastPort, out int parsedPort))
            port = parsedPort;

        Debug.Log($"[RTTBrowserController] Loading WebRTC protocol: {host}:{port}");
        _browserViewInstance.LoadWebRTCProtocol(host, port);
    }

    /// <summary>
    /// Load a specific URL.
    /// </summary>
    public void LoadUrl(string url)
    {
        _browserViewInstance?.LoadUrl(url);
    }

    /// <summary>
    /// Cleanup resources and unsubscribe from events.
    /// </summary>
    public void Cleanup()
    {
        if (_browserViewInstance != null)
        {
            _browserViewInstance.OnBackClicked -= HandleBackClicked;
            _browserViewInstance.OnUrlChanged -= HandleUrlChanged;
            _browserViewInstance = null;
        }

        if (_browserObject != null)
        {
            Destroy(_browserObject);
            _browserObject = null;
        }

        Debug.Log("[RTTBrowserController] Browser view cleaned up");
    }
    #endregion

    #region Private Methods
    private void HandleBackClicked()
    {
        Debug.Log("[RTTBrowserController] Back clicked");
        OnBackClicked?.Invoke();
    }

    private void HandleUrlChanged(string url)
    {
        Debug.Log($"[RTTBrowserController] URL changed: {url}");
        OnUrlChanged?.Invoke(url);
    }
    #endregion
}
