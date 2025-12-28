using UnityEngine;
using System.Collections;
using TMPro;

/// <summary>
/// Manages menu state and navigation between different menu screens.
/// Coordinates RTTMainMenuController and RTTRemoteMenuController.
/// </summary>
public class RTTMenuManager : MonoBehaviour
{
    #region Types
    public enum MenuState { MainMenu, RemoteMenu }
    #endregion

    #region Configuration
    [Header("References")]
    [SerializeField] private RTTMenuFrame menuFrame;

    [Header("Controllers")]
    [SerializeField] private RTTMainMenuController mainMenuController;
    [SerializeField] private RTTRemoteMenuController remoteMenuController;

    [Header("Shared Config")]
    [SerializeField] private TMP_FontAsset menuFont;
    [SerializeField] private Color themeColor = new Color(0f, 0.9f, 1f);
    [SerializeField] private Color accentColor = new Color(0.76f, 0.36f, 1f);

    [Header("Auto Init")]
    [Tooltip("Automatically show main menu on start")]
    [SerializeField] private bool autoShowMainMenu = true;
    #endregion

    #region Private Fields
    private MenuState _currentState = MenuState.MainMenu;
    private GameObject _currentMenuContent;
    #endregion

    #region Events
    public event System.Action<MenuState> OnMenuStateChanged;
    #endregion

    #region Properties
    public MenuState CurrentState => _currentState;
    public bool IsMainMenuActive => _currentState == MenuState.MainMenu;
    public bool IsRemoteMenuActive => _currentState == MenuState.RemoteMenu;
    public RTTMenuFrame MenuFrame => menuFrame;
    public RTTMainMenuController MainMenuController => mainMenuController;
    public RTTRemoteMenuController RemoteMenuController => remoteMenuController;
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
                Debug.Log("[RTTMenuManager] Browser clicked");
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
        Debug.Log("[RTTMenuManager] Remote Start clicked - hiding menu");
        if (menuFrame != null)
        {
            menuFrame.gameObject.SetActive(false);
        }
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
    #endregion
}
