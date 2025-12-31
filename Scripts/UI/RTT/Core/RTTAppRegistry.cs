using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject for centralized app definitions.
/// Controls available apps, their names, icons, and positions in the menu.
/// </summary>
[CreateAssetMenu(fileName = "RTTAppRegistry", menuName = "VR-Workspace/RTT App Registry")]
public class RTTAppRegistry : ScriptableObject
{
    #region App Definition
    [System.Serializable]
    public class AppDefinition
    {
        [Tooltip("Unique identifier for this app (e.g., 'remote', 'browser')")]
        public string id;

        [Tooltip("Display name shown in menu")]
        public string displayName;

        [Tooltip("App icon (if null, will try to load from Resources/icon_{id})")]
        public Sprite icon;

        [Tooltip("Position in menu grid (0-based)")]
        public int menuPosition;

        [Tooltip("Use theme colors (true) or custom color (false)")]
        public bool useThemeColor = true;

        [Tooltip("Custom accent color (used when useThemeColor is false)")]
        public Color customColor = new Color(0f, 0.9f, 1f);

        [Tooltip("Whether this app is enabled and visible in menu")]
        public bool isEnabled = true;

        [Tooltip("Whether this is a system app (cannot be disabled)")]
        public bool isSystemApp = false;

        [Header("App Behavior")]
        [Tooltip("App type for content creation")]
        public AppType appType = AppType.NotImplemented;

        [Tooltip("Description shown in tooltips")]
        [TextArea(2, 4)]
        public string description;
    }

    public enum AppType
    {
        NotImplemented,
        Remote,
        Browser,
        Media,
        Files,
        Settings,
        Quit
    }
    #endregion

    #region Configuration
    [Header("Available Apps")]
    [Tooltip("List of all available apps")]
    public List<AppDefinition> apps = new List<AppDefinition>();

    [Header("Constraints")]
    [Tooltip("Maximum number of apps that can be open simultaneously")]
    [Range(1, 5)]
    public int maxOpenApps = 3;

    [Tooltip("Default app to open (app ID)")]
    public string defaultAppId = "remote";

    [Header("Menu Layout")]
    [Tooltip("Number of columns in menu grid")]
    [Range(2, 6)]
    public int menuColumns = 3;

    [Tooltip("Spacing between menu items")]
    public float menuSpacing = 30f;

    [Tooltip("Button aspect ratio (width/height)")]
    public float buttonAspectRatio = 1.2f;
    #endregion

    #region Query Methods
    /// <summary>
    /// Get app definition by ID
    /// </summary>
    public AppDefinition GetApp(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return apps.Find(a => a.id == id);
    }

    /// <summary>
    /// Get all enabled apps (for menu display)
    /// </summary>
    public List<AppDefinition> GetEnabledApps()
    {
        return apps.FindAll(a => a.isEnabled);
    }

    /// <summary>
    /// Get enabled apps sorted by menu position
    /// </summary>
    public List<AppDefinition> GetSortedEnabledApps()
    {
        var enabled = GetEnabledApps();
        enabled.Sort((a, b) => a.menuPosition.CompareTo(b.menuPosition));
        return enabled;
    }

    /// <summary>
    /// Get icon for an app (loads from Resources if not assigned)
    /// </summary>
    public Sprite GetIcon(string id)
    {
        var app = GetApp(id);
        if (app == null) return null;

        if (app.icon != null) return app.icon;

        // Try to load from Resources
        var loaded = Resources.Load<Sprite>($"icon_{id}");
        if (loaded != null)
        {
            app.icon = loaded; // Cache for next time
        }
        return loaded;
    }

    /// <summary>
    /// Check if an app exists and is enabled
    /// </summary>
    public bool IsAppAvailable(string id)
    {
        var app = GetApp(id);
        return app != null && app.isEnabled;
    }

    /// <summary>
    /// Get app type by ID
    /// </summary>
    public AppType GetAppType(string id)
    {
        var app = GetApp(id);
        return app?.appType ?? AppType.NotImplemented;
    }

    /// <summary>
    /// Get the number of enabled apps
    /// </summary>
    public int EnabledAppCount => apps.FindAll(a => a.isEnabled).Count;
    #endregion

    #region Modification Methods
    /// <summary>
    /// Enable or disable an app
    /// </summary>
    public void SetAppEnabled(string id, bool enabled)
    {
        var app = GetApp(id);
        if (app != null && !app.isSystemApp)
        {
            app.isEnabled = enabled;
        }
    }

    /// <summary>
    /// Update app position in menu
    /// </summary>
    public void SetAppPosition(string id, int position)
    {
        var app = GetApp(id);
        if (app != null)
        {
            app.menuPosition = position;
        }
    }

    /// <summary>
    /// Update app display name
    /// </summary>
    public void SetAppDisplayName(string id, string displayName)
    {
        var app = GetApp(id);
        if (app != null)
        {
            app.displayName = displayName;
        }
    }

    /// <summary>
    /// Update app icon
    /// </summary>
    public void SetAppIcon(string id, Sprite icon)
    {
        var app = GetApp(id);
        if (app != null)
        {
            app.icon = icon;
        }
    }
    #endregion

    #region Default Setup
    /// <summary>
    /// Reset to default app configuration
    /// </summary>
    [ContextMenu("Reset to Defaults")]
    public void ResetToDefaults()
    {
        apps = new List<AppDefinition>
        {
            new AppDefinition
            {
                id = "remote",
                displayName = "Remote Desktop",
                menuPosition = 0,
                useThemeColor = true,
                isEnabled = true,
                appType = AppType.Remote,
                description = "Connect to remote PC for streaming"
            },
            new AppDefinition
            {
                id = "browser",
                displayName = "Browser",
                menuPosition = 1,
                useThemeColor = false,
                customColor = new Color(0.76f, 0.36f, 1f),
                isEnabled = true,
                appType = AppType.Browser,
                description = "Web browser"
            },
            new AppDefinition
            {
                id = "media",
                displayName = "Media",
                menuPosition = 2,
                useThemeColor = true,
                isEnabled = true,
                appType = AppType.Media,
                description = "Media player for videos and music"
            },
            new AppDefinition
            {
                id = "files",
                displayName = "Files",
                menuPosition = 3,
                useThemeColor = false,
                customColor = new Color(0.76f, 0.36f, 1f),
                isEnabled = true,
                appType = AppType.Files,
                description = "File manager"
            },
            new AppDefinition
            {
                id = "settings",
                displayName = "Settings",
                menuPosition = 4,
                useThemeColor = true,
                isEnabled = true,
                appType = AppType.Settings,
                description = "App settings and configuration"
            },
            new AppDefinition
            {
                id = "quit",
                displayName = "Quit",
                menuPosition = 5,
                useThemeColor = false,
                customColor = new Color(0.76f, 0.36f, 1f),
                isEnabled = true,
                isSystemApp = true,
                appType = AppType.Quit,
                description = "Exit the application"
            }
        };

        maxOpenApps = 3;
        defaultAppId = "remote";
        menuColumns = 3;
        menuSpacing = 30f;
        buttonAspectRatio = 1.2f;
    }

    private void OnValidate()
    {
        // Ensure unique IDs
        var ids = new HashSet<string>();
        foreach (var app in apps)
        {
            if (string.IsNullOrEmpty(app.id))
            {
                Debug.LogWarning("[RTTAppRegistry] App has empty ID");
                continue;
            }
            if (ids.Contains(app.id))
            {
                Debug.LogWarning($"[RTTAppRegistry] Duplicate app ID: {app.id}");
            }
            ids.Add(app.id);
        }
    }
    #endregion
}
