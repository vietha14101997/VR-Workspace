using UnityEngine;
using System.Collections;
using TMPro;
using Unity.WebRTC;
using VRWorkspace.Utils;
using VRWorkspace.Streaming;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;

/// <summary>
/// Manages menu state and navigation between different menu screens.
/// Coordinates RTTMainMenuController, RTTRemoteMenuController, and RTTBrowserController.
/// </summary>
public class RTTMenuManager : MonoBehaviour
{
    #region Types
    public enum MenuState { MainMenu, RemoteMenu, BrowserView }
    #endregion

    #region Configuration
    [Header("References")]
    [SerializeField] private RTTMenuFrame menuFrame;

    [Header("Controllers")]
    [SerializeField] private RTTMainMenuController mainMenuController;
    [SerializeField] private RTTRemoteMenuController remoteMenuController;
    [SerializeField] private RTTBrowserController browserController;

    [Header("Shared Config")]
    [SerializeField] private TMP_FontAsset menuFont;
    [SerializeField] private Color themeColor = new Color(0f, 0.9f, 1f);
    [SerializeField] private Color accentColor = new Color(0.76f, 0.36f, 1f);

    [Header("Auto Init")]
    [Tooltip("Automatically show main menu on start")]
    [SerializeField] private bool autoShowMainMenu = true;

    [Header("Streaming Panels")]
    [Tooltip("Optional panel prefab for cluster rig (if null, default panels will be created)")]
    [SerializeField] private WorldPanelPlus panelPrefab;
    #endregion

    #region Private Fields
    private MenuState _currentState = MenuState.MainMenu;
    private GameObject _currentMenuContent;
    private ConnectionViewModel _viewModel;
    private Coroutine _textureUpdateCoroutine;
    private Coroutine _webrtcUpdateCoroutine;
    private WorldPanelClusterRig _clusterRig;
    #endregion

    #region Events
    public event System.Action<MenuState> OnMenuStateChanged;
    #endregion

    #region Properties
    public MenuState CurrentState => _currentState;
    public bool IsMainMenuActive => _currentState == MenuState.MainMenu;
    public bool IsRemoteMenuActive => _currentState == MenuState.RemoteMenu;
    public bool IsBrowserViewActive => _currentState == MenuState.BrowserView;
    public RTTMenuFrame MenuFrame => menuFrame;
    public RTTMainMenuController MainMenuController => mainMenuController;
    public RTTRemoteMenuController RemoteMenuController => remoteMenuController;
    public RTTBrowserController BrowserController => browserController;
    #endregion

    #region Lifecycle
    private void Awake()
    {
        // Auto-find menu frame if not assigned
        if (menuFrame == null)
            menuFrame = GetComponentInChildren<RTTMenuFrame>();
        if (menuFrame == null)
            menuFrame = FindObjectOfType<RTTMenuFrame>();

        // Auto-find or create controllers
        if (mainMenuController == null)
            mainMenuController = GetComponentInChildren<RTTMainMenuController>();
        if (mainMenuController == null)
        {
            mainMenuController = gameObject.AddComponent<RTTMainMenuController>();
            Debug.Log("[RTTMenuManager] Created RTTMainMenuController");
        }

        if (remoteMenuController == null)
            remoteMenuController = GetComponentInChildren<RTTRemoteMenuController>();
        if (remoteMenuController == null)
        {
            remoteMenuController = gameObject.AddComponent<RTTRemoteMenuController>();
            Debug.Log("[RTTMenuManager] Created RTTRemoteMenuController");
        }

        if (browserController == null)
            browserController = GetComponentInChildren<RTTBrowserController>();
        if (browserController == null)
        {
            browserController = gameObject.AddComponent<RTTBrowserController>();
            Debug.Log("[RTTMenuManager] Created RTTBrowserController");
        }
    }

    private void Start()
    {
        // Subscribe to controller events
        if (mainMenuController != null)
        {
            mainMenuController.OnMenuItemClicked += HandleMainMenuItemClicked;
        }

        if (remoteMenuController != null)
        {
            remoteMenuController.OnBackClicked += ReturnToMainMenu;
            remoteMenuController.OnStartClicked += HandleRemoteStartClicked;
        }

        if (browserController != null)
        {
            browserController.OnBackClicked += ReturnToMainMenu;
        }

        // Auto show main menu if enabled
        if (autoShowMainMenu && menuFrame != null)
        {
            // Wait for RTTMenuFrame to initialize
            StartCoroutine(WaitAndShowMainMenu());
        }
    }

    private IEnumerator WaitAndShowMainMenu()
    {
        // Wait for ContentContainer to be ready
        while (menuFrame.ContentContainer == null)
        {
            yield return null;
        }
        // Additional frame to ensure layout is complete
        yield return null;

        ShowMainMenu();
        Debug.Log("[RTTMenuManager] Auto-showed main menu");
    }

    private void OnDestroy()
    {
        // Stop texture update coroutine
        if (_textureUpdateCoroutine != null)
        {
            StopCoroutine(_textureUpdateCoroutine);
            _textureUpdateCoroutine = null;
        }

        // Stop WebRTC update coroutine
        if (_webrtcUpdateCoroutine != null)
        {
            StopCoroutine(_webrtcUpdateCoroutine);
            _webrtcUpdateCoroutine = null;
        }

        // Destroy dynamically created cluster rig
        if (_clusterRig != null)
        {
            Destroy(_clusterRig.gameObject);
            _clusterRig = null;
        }

        // Unsubscribe from events
        if (mainMenuController != null)
        {
            mainMenuController.OnMenuItemClicked -= HandleMainMenuItemClicked;
        }

        if (remoteMenuController != null)
        {
            remoteMenuController.OnBackClicked -= ReturnToMainMenu;
            remoteMenuController.OnStartClicked -= HandleRemoteStartClicked;
        }

        if (browserController != null)
        {
            browserController.OnBackClicked -= ReturnToMainMenu;
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Initialize the menu manager with a menu frame.
    /// </summary>
    public void Initialize(RTTMenuFrame frame)
    {
        menuFrame = frame;
    }

    /// <summary>
    /// Show the main menu.
    /// </summary>
    public void ShowMainMenu()
    {
        if (menuFrame == null || menuFrame.ContentContainer == null)
        {
            Debug.LogWarning("[RTTMenuManager] MenuFrame or ContentContainer not initialized");
            return;
        }

        // Destroy current content
        DestroyCurrentContent();

        // Create main menu
        if (mainMenuController != null)
        {
            var containerSize = GetContainerSize();
            _currentMenuContent = mainMenuController.CreateMenu(
                menuFrame.ContentContainer,
                containerSize.x,
                containerSize.y
            );
        }

        _currentState = MenuState.MainMenu;
        OnMenuStateChanged?.Invoke(_currentState);

        menuFrame.MarkDirty();
        Debug.Log("[RTTMenuManager] Showing Main Menu");
    }

    /// <summary>
    /// Switch from Main Menu to Remote Menu.
    /// </summary>
    public void SwitchToRemoteMenu()
    {
        if (_currentState == MenuState.RemoteMenu) return;
        if (menuFrame == null || menuFrame.ContentContainer == null) return;

        // Destroy main menu
        DestroyCurrentContent();

        // Create remote menu
        if (remoteMenuController != null)
        {
            var containerSize = GetContainerSize();
            _currentMenuContent = remoteMenuController.CreateMenu(
                menuFrame.ContentContainer,
                containerSize.x,
                containerSize.y,
                menuFont,
                themeColor,
                accentColor
            );
        }

        _currentState = MenuState.RemoteMenu;
        OnMenuStateChanged?.Invoke(_currentState);

        menuFrame.MarkDirty();
        Debug.Log("[RTTMenuManager] Switched to Remote Menu");
    }

    /// <summary>
    /// Return from Remote Menu to Main Menu.
    /// </summary>
    public void ReturnToMainMenu()
    {
        if (_currentState == MenuState.MainMenu) return;

        ShowMainMenu();
        Debug.Log("[RTTMenuManager] Returned to Main Menu");
    }
    #endregion

    #region Private Methods
    private void DestroyCurrentContent()
    {
        if (_currentMenuContent != null)
        {
            // Cleanup controllers
            if (_currentState == MenuState.MainMenu && mainMenuController != null)
            {
                mainMenuController.Cleanup();
            }
            else if (_currentState == MenuState.RemoteMenu && remoteMenuController != null)
            {
                remoteMenuController.Cleanup();
            }
            else if (_currentState == MenuState.BrowserView && browserController != null)
            {
                browserController.Cleanup();
            }

            Destroy(_currentMenuContent);
            _currentMenuContent = null;
        }
    }

    private Vector2 GetContainerSize()
    {
        if (menuFrame == null || menuFrame.ContentContainer == null)
            return new Vector2(1770f, 800f);

        var rect = menuFrame.ContentContainer.rect;
        if (rect.width > 0 && rect.height > 0)
            return new Vector2(rect.width, rect.height);

        // Fallback calculation
        return new Vector2(
            menuFrame.LogicalWidthValue - 150f,  // contentMarginLeft + contentMarginRight
            menuFrame.LogicalWidthValue / menuFrame.PanelWidth * menuFrame.PanelHeight - 100f
        );
    }

    private void HandleMainMenuItemClicked(string itemId)
    {
        switch (itemId)
        {
            case "remote":
                SwitchToRemoteMenu();
                break;
            case "browser":
                Debug.Log("[RTTMenuManager] Browser clicked - opening WebView with webrtc_protocolv2.html");
                OpenBrowserWebView();
                break;
            case "media":
                Debug.Log("[RTTMenuManager] Media clicked");
                break;
            case "files":
                Debug.Log("[RTTMenuManager] Files clicked");
                break;
            case "settings":
                Debug.Log("[RTTMenuManager] Settings clicked");
                break;
            case "quit":
                HandleQuit();
                break;
        }
    }

    private void HandleRemoteStartClicked()
    {
        Debug.Log("[RTTMenuManager] Remote Start clicked - hiding menu and showing cluster panels");

        // Hide menu
        if (menuFrame != null)
        {
            menuFrame.gameObject.SetActive(false);
        }

        // Get ViewModel from ServiceLocator
        if (!ServiceLocator.TryGet<ConnectionViewModel>(out _viewModel))
        {
            Debug.LogWarning("[RTTMenuManager] ConnectionViewModel not found in ServiceLocator");
            return;
        }

        // Check if applied config is available (this is what was actually sent to server)
        if (_viewModel.AppliedConfig.Value == null)
        {
            Debug.LogWarning("[RTTMenuManager] AppliedConfig is null, falling back to SuggestedConfig");
            if (_viewModel.SuggestedConfig.Value == null)
            {
                Debug.LogError("[RTTMenuManager] No config available");
                return;
            }
        }

        // Use AppliedConfig (the actual config sent to server), fallback to SuggestedConfig
        int monitorCount = _viewModel.AppliedConfig.Value?.monitors ?? _viewModel.SuggestedConfig.Value.monitors;
        Debug.Log($"[RTTMenuManager] Creating cluster rig with {monitorCount} panels (from AppliedConfig)");

        // Create WorldPanelClusterRig dynamically
        CreateClusterRig(monitorCount);

        // Start WebRTC update coroutine (REQUIRED for video frame decoding)
        if (_webrtcUpdateCoroutine == null)
        {
            _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
            Debug.Log("[RTTMenuManager] Started WebRTC.Update() coroutine");
        }

        // Start texture update coroutine
        if (_textureUpdateCoroutine != null)
        {
            StopCoroutine(_textureUpdateCoroutine);
        }
        _textureUpdateCoroutine = StartCoroutine(UpdatePanelTextures());
    }

    /// <summary>
    /// Dynamically create a WorldPanelClusterRig with the specified number of panels.
    /// </summary>
    private void CreateClusterRig(int panelCount)
    {
        // Destroy existing cluster rig if any
        if (_clusterRig != null)
        {
            Destroy(_clusterRig.gameObject);
            _clusterRig = null;
        }

        // Create new GameObject for cluster rig
        var clusterGO = new GameObject("StreamingClusterRig");
        _clusterRig = clusterGO.AddComponent<WorldPanelClusterRig>();

        // Assign panel prefab if available
        if (panelPrefab != null)
        {
            _clusterRig.panelPrefab = panelPrefab;
        }

        // Build the cluster with specified panel count
        _clusterRig.BuildWithPanelCount(panelCount);

        Debug.Log($"[RTTMenuManager] Created WorldPanelClusterRig with {panelCount} panels");
    }

    /// <summary>
    /// Coroutine to continuously update panel textures from streaming.
    /// </summary>
    private IEnumerator UpdatePanelTextures()
    {
        Debug.Log("[RTTMenuManager] Starting texture update coroutine");

        // Wait for streaming to actually start (IsStreaming becomes true)
        float waitTimeout = 30f; // Max 30 seconds wait
        float waitElapsed = 0f;
        while (_viewModel != null && !_viewModel.IsStreaming.Value && waitElapsed < waitTimeout)
        {
            waitElapsed += Time.deltaTime;
            if ((int)(waitElapsed * 10) % 10 == 0) // Log every ~1 second
            {
                Debug.Log($"[RTTMenuManager] Waiting for streaming to start... ({waitElapsed:F1}s)");
            }
            yield return null;
        }

        if (_viewModel == null || !_viewModel.IsStreaming.Value)
        {
            Debug.LogWarning($"[RTTMenuManager] Streaming did not start within {waitTimeout}s, coroutine exiting");
            yield break;
        }

        Debug.Log("[RTTMenuManager] Streaming started, beginning texture updates");

        int frameCount = 0;
        int logInterval = 60; // Log every 60 frames (about 1 second)

        while (_viewModel != null && _viewModel.IsStreaming.Value)
        {
            // Poll textures from WebRTC
            _viewModel.PollTextures();

            // Update panel textures
            if (_clusterRig != null && _clusterRig.panels != null)
            {
                for (int i = 0; i < _clusterRig.panels.Count; i++)
                {
                    var panel = _clusterRig.panels[i];
                    if (panel != null)
                    {
                        var texture = _viewModel.GetTexture(i);

                        // Debug log periodically
                        if (frameCount % logInterval == 0 && i == 0)
                        {
                            Debug.Log($"[RTTMenuManager] Panel{i} texture poll: " +
                                $"texture={(texture != null ? $"{texture.width}x{texture.height}" : "null")}, " +
                                $"current={(panel.contentTexture != null ? "set" : "null")}");
                        }

                        if (texture != null && panel.contentTexture != texture)
                        {
                            panel.contentTexture = texture;
                            panel.Apply();
                            Debug.Log($"[RTTMenuManager] Panel{i} texture updated: {texture.width}x{texture.height}");
                        }
                    }
                }
            }

            frameCount++;
            yield return null; // Update every frame
        }

        Debug.Log("[RTTMenuManager] Texture update coroutine stopped (IsStreaming became false)");
    }

    private void HandleQuit()
    {
        Debug.Log("[RTTMenuManager] Quit clicked");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Switch to embedded Browser View with webrtc_protocolv2.html.
    /// </summary>
    private void OpenBrowserWebView()
    {
        SwitchToBrowserView();
    }

    /// <summary>
    /// Switch from current view to embedded Browser View.
    /// </summary>
    public void SwitchToBrowserView()
    {
        if (_currentState == MenuState.BrowserView) return;
        if (menuFrame == null || menuFrame.ContentContainer == null) return;

        // Destroy current content
        DestroyCurrentContent();

        // Create browser view
        if (browserController != null)
        {
            var containerSize = GetContainerSize();
            _currentMenuContent = browserController.CreateBrowser(
                menuFrame.ContentContainer,
                containerSize.x,
                containerSize.y,
                menuFont,
                themeColor,
                accentColor
            );

            // Auto-load webrtc_protocolv2.html
            browserController.LoadWebRTCProtocol();
        }

        _currentState = MenuState.BrowserView;
        OnMenuStateChanged?.Invoke(_currentState);

        menuFrame.MarkDirty();
        Debug.Log("[RTTMenuManager] Switched to Browser View");
    }
    #endregion
}
