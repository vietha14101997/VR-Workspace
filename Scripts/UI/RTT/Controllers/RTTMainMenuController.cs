using UnityEngine;
using TMPro;

/// <summary>
/// Controls the main menu lifecycle including creation, event handling, and destruction.
/// </summary>
public class RTTMainMenuController : MonoBehaviour
{
    #region Configuration
    [Header("Menu Layout")]
    [SerializeField] private int menuColumns = 3;
    [SerializeField] private Vector2 menuSpacing = new Vector2(50f, 75f);
    [SerializeField] private float menuButtonAspect = 1.4f;
    [SerializeField] private int menuFontSize = 42;

    [Header("Menu Font")]
    [SerializeField] private TMP_FontAsset menuFont;

    [Header("Menu Colors")]
    [SerializeField] private Color[] menuButtonColors = new Color[] {
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.76f, 0.36f, 1.0f, 1.0f), // Purple
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.76f, 0.36f, 1.0f, 1.0f), // Purple
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
    };

    [Header("Menu Icons")]
    [SerializeField] private Sprite iconRemote;
    [SerializeField] private Sprite iconBrowser;
    [SerializeField] private Sprite iconMedia;
    [SerializeField] private Sprite iconFiles;
    [SerializeField] private Sprite iconSettings;
    [SerializeField] private Sprite iconQuit;
    #endregion

    #region Private Fields
    private RTTMainMenu _mainMenuInstance;
    private GameObject _menuObject;
    #endregion

    #region Events
    public event System.Action<string> OnMenuItemClicked;
    #endregion

    #region Properties
    public RTTMainMenu MainMenuInstance => _mainMenuInstance;
    public TMP_FontAsset MenuFont => menuFont;
    public Color[] MenuButtonColors => menuButtonColors;
    #endregion

    #region Public API
    /// <summary>
    /// Create the main menu in the specified container.
    /// </summary>
    public GameObject CreateMenu(RectTransform container, float containerW, float containerH)
    {
        if (container == null)
        {
            Debug.LogWarning("[RTTMainMenuController] Container is null");
            return null;
        }

        // Load icons if not assigned
        LoadMenuIcons();

        // Create menu container
        _menuObject = new GameObject("MainMenu");
        _menuObject.transform.SetParent(container, false);

        RectTransform menuRT = _menuObject.AddComponent<RectTransform>();
        menuRT.anchorMin = Vector2.zero;
        menuRT.anchorMax = Vector2.one;
        menuRT.offsetMin = Vector2.zero;
        menuRT.offsetMax = Vector2.zero;

        // Create RTTMainMenu component
        _mainMenuInstance = _menuObject.AddComponent<RTTMainMenu>();
        _mainMenuInstance.CustomFont = menuFont;
        _mainMenuInstance.FontSize = menuFontSize;
        _mainMenuInstance.Columns = menuColumns;
        _mainMenuInstance.Spacing = menuSpacing;
        _mainMenuInstance.ButtonAspect = menuButtonAspect;

        // Add menu items
        _mainMenuInstance.AddItem("remote", "Remote Desktop", iconRemote, GetMenuButtonColor(0));
        _mainMenuInstance.AddItem("browser", "Browser", iconBrowser, GetMenuButtonColor(1));
        _mainMenuInstance.AddItem("media", "Media", iconMedia, GetMenuButtonColor(2));
        _mainMenuInstance.AddItem("files", "Files", iconFiles, GetMenuButtonColor(3));
        _mainMenuInstance.AddItem("settings", "Settings", iconSettings, GetMenuButtonColor(4));
        _mainMenuInstance.AddItem("quit", "Quit", iconQuit, GetMenuButtonColor(5));

        // Subscribe to menu item clicks
        _mainMenuInstance.OnMenuItemClicked += HandleMenuItemClicked;

        // Build the UI
        _mainMenuInstance.BuildUI(container, containerW, containerH);

        Debug.Log("[RTTMainMenuController] Main Menu created");
        return _menuObject;
    }

    /// <summary>
    /// Rebuild the main menu.
    /// </summary>
    public void RebuildMenu(RectTransform container, float containerW, float containerH)
    {
        Cleanup();
        CreateMenu(container, containerW, containerH);
    }

    /// <summary>
    /// Cleanup resources and unsubscribe from events.
    /// </summary>
    public void Cleanup()
    {
        if (_mainMenuInstance != null)
        {
            _mainMenuInstance.OnMenuItemClicked -= HandleMenuItemClicked;
            _mainMenuInstance = null;
        }

        // Note: _menuObject is destroyed by RTTMenuManager
        _menuObject = null;
    }

    /// <summary>
    /// Set custom font for the menu.
    /// </summary>
    public void SetFont(TMP_FontAsset font)
    {
        menuFont = font;
    }

    /// <summary>
    /// Set custom button colors.
    /// </summary>
    public void SetButtonColors(Color[] colors)
    {
        menuButtonColors = colors;
    }
    #endregion

    #region Private Methods
    private void LoadMenuIcons()
    {
        if (iconRemote == null) iconRemote = Resources.Load<Sprite>("icon_remote");
        if (iconBrowser == null) iconBrowser = Resources.Load<Sprite>("icon_browser");
        if (iconMedia == null) iconMedia = Resources.Load<Sprite>("icon_media");
        if (iconFiles == null) iconFiles = Resources.Load<Sprite>("icon_files");
        if (iconSettings == null) iconSettings = Resources.Load<Sprite>("icon_settings");
        if (iconQuit == null) iconQuit = Resources.Load<Sprite>("icon_quit");

        Debug.Log($"[RTTMainMenuController] Icons loaded - Remote:{iconRemote != null}, Browser:{iconBrowser != null}, Media:{iconMedia != null}");
    }

    private Color GetMenuButtonColor(int index)
    {
        if (menuButtonColors != null && index < menuButtonColors.Length)
            return menuButtonColors[index];
        return new Color(0f, 0.9f, 1f); // Default cyan
    }

    private void HandleMenuItemClicked(string itemId)
    {
        Debug.Log($"[RTTMainMenuController] Menu item clicked: {itemId}");
        OnMenuItemClicked?.Invoke(itemId);
    }
    #endregion
}
