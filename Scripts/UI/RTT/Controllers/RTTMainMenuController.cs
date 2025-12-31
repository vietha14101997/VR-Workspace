using UnityEngine;
using TMPro;

/// <summary>
/// Controls the main menu lifecycle including creation, event handling, and destruction.
/// Now supports RTTAppRegistry for app definitions and RTTThemeConfig for colors.
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

    [Header("Fallback Colors (used when no theme available)")]
    [SerializeField] private Color[] menuButtonColors = new Color[] {
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.76f, 0.36f, 1.0f, 1.0f), // Purple
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
        new Color(0.76f, 0.36f, 1.0f, 1.0f), // Purple
        new Color(0.0f, 0.9f, 1.0f, 1.0f),  // Cyan
    };

    [Header("Fallback Icons (used when not in registry)")]
    [SerializeField] private Sprite iconRemote;
    [SerializeField] private Sprite iconBrowser;
    [SerializeField] private Sprite iconMedia;
    [SerializeField] private Sprite iconFiles;
    [SerializeField] private Sprite iconSettings;
    [SerializeField] private Sprite iconQuit;

    [Header("Use Registry")]
    [Tooltip("If true, use RTTAppRegistry for app definitions. If false, use hardcoded items.")]
    [SerializeField] private bool useAppRegistry = true;
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
    /// Uses RTTAppRegistry if available, otherwise falls back to hardcoded items.
    /// </summary>
    public GameObject CreateMenu(RectTransform container, float containerW, float containerH)
    {
        if (container == null)
        {
            Debug.LogWarning("[RTTMainMenuController] Container is null");
            return null;
        }

        // Load icons if not assigned (fallback)
        LoadMenuIcons();

        // Create menu container
        _menuObject = new GameObject("MainMenu");
        _menuObject.transform.SetParent(container, false);

        RectTransform menuRT = _menuObject.AddComponent<RectTransform>();
        menuRT.anchorMin = Vector2.zero;
        menuRT.anchorMax = Vector2.one;
        menuRT.offsetMin = Vector2.zero;
        menuRT.offsetMax = Vector2.zero;

        // Get font from RTTManager if available
        var manager = RTTManager.Instance;
        var font = manager?.Font ?? menuFont;

        // Get layout from AppRegistry if available
        var registry = manager?.AppRegistry;
        int cols = registry?.menuColumns ?? menuColumns;
        float spacing = registry?.menuSpacing ?? menuSpacing.x;
        float aspect = registry?.buttonAspectRatio ?? menuButtonAspect;

        // Create RTTMainMenu component
        _mainMenuInstance = _menuObject.AddComponent<RTTMainMenu>();
        _mainMenuInstance.CustomFont = font;
        _mainMenuInstance.FontSize = menuFontSize;
        _mainMenuInstance.Columns = cols;
        _mainMenuInstance.Spacing = new Vector2(spacing, spacing * 1.5f);
        _mainMenuInstance.ButtonAspect = aspect;

        // Add menu items from Registry or fallback
        // Use registry only if it exists AND has enabled apps
        bool hasRegistryApps = useAppRegistry && registry != null && registry.EnabledAppCount > 0;
        if (hasRegistryApps)
        {
            AddItemsFromRegistry(registry, manager?.Theme);
        }
        else
        {
            AddHardcodedItems();
            Debug.Log("[RTTMainMenuController] Using hardcoded items (registry empty or disabled)");
        }

        // Subscribe to menu item clicks
        _mainMenuInstance.OnMenuItemClicked += HandleMenuItemClicked;

        // Build the UI
        _mainMenuInstance.BuildUI(container, containerW, containerH);

        Debug.Log($"[RTTMainMenuController] Main Menu created (useRegistry={hasRegistryApps}, itemCount={_mainMenuInstance.MenuItems.Count})");
        return _menuObject;
    }

    /// <summary>
    /// Add menu items from RTTAppRegistry.
    /// </summary>
    private void AddItemsFromRegistry(RTTAppRegistry registry, RTTThemeConfig theme)
    {
        var apps = registry.GetSortedEnabledApps();
        for (int i = 0; i < apps.Count; i++)
        {
            var app = apps[i];

            // Get icon from registry or fallback
            Sprite icon = app.icon ?? registry.GetIcon(app.id) ?? GetFallbackIcon(app.id);

            // Get color from theme or app custom color
            Color color;
            if (app.useThemeColor && theme != null)
            {
                color = theme.GetAlternatingColor(i);
            }
            else if (!app.useThemeColor)
            {
                color = app.customColor;
            }
            else
            {
                color = GetMenuButtonColor(i);
            }

            _mainMenuInstance.AddItem(app.id, app.displayName, icon, color);
        }
    }

    /// <summary>
    /// Add hardcoded menu items (fallback when no registry).
    /// </summary>
    private void AddHardcodedItems()
    {
        _mainMenuInstance.AddItem("remote", "Remote Desktop", iconRemote, GetMenuButtonColor(0));
        _mainMenuInstance.AddItem("browser", "Browser", iconBrowser, GetMenuButtonColor(1));
        _mainMenuInstance.AddItem("media", "Media", iconMedia, GetMenuButtonColor(2));
        _mainMenuInstance.AddItem("files", "Files", iconFiles, GetMenuButtonColor(3));
        _mainMenuInstance.AddItem("settings", "Settings", iconSettings, GetMenuButtonColor(4));
        _mainMenuInstance.AddItem("quit", "Quit", iconQuit, GetMenuButtonColor(5));
    }

    /// <summary>
    /// Get fallback icon by app ID.
    /// </summary>
    private Sprite GetFallbackIcon(string appId)
    {
        switch (appId)
        {
            case "remote": return iconRemote;
            case "browser": return iconBrowser;
            case "media": return iconMedia;
            case "files": return iconFiles;
            case "settings": return iconSettings;
            case "quit": return iconQuit;
            default: return null;
        }
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

        // Note: _menuObject is destroyed by RTTManager
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

        Debug.Log($"[RTTMainMenuController] Icons loaded - Remote:{iconRemote != null}, Browser:{iconBrowser != null}, Media:{iconMedia != null}, Files:{iconFiles != null}");
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
