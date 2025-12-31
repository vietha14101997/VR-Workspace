using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Menu item configuration for RTTMainMenu.
/// </summary>
[Serializable]
public class RTTMenuItem
{
    public string id;           // Unique identifier (e.g., "remote", "media")
    public string label;        // Display name
    public Sprite icon;         // Optional - auto-loads from Resources if null
    public Color color = new Color(0f, 0.9f, 1f); // Button theme color

    public RTTMenuItem() { }

    public RTTMenuItem(string id, string label, Sprite icon = null, Color? color = null)
    {
        this.id = id;
        this.label = label;
        this.icon = icon;
        this.color = color ?? new Color(0f, 0.9f, 1f);
    }
}

/// <summary>
/// RTT-based Main Menu with configurable menu items.
/// Easy to use: LoadDefaultItems() or AddItem() then BuildUI().
/// </summary>
public class RTTMainMenu : MonoBehaviour
{
    #region Configuration
    [Header("Menu Items")]
    [SerializeField] private List<RTTMenuItem> menuItems = new List<RTTMenuItem>();

    [Header("Layout")]
    [SerializeField] private int columns = 3;
    [SerializeField] private Vector2 spacing = new Vector2(50f, 75f);
    [SerializeField] private float buttonAspect = 1.4f;

    [Header("Typography")]
    [SerializeField] private int fontSize = 42;
    [SerializeField] private TMP_FontAsset customFont;
    #endregion

    #region Events
    /// <summary>
    /// Fired when a menu item is clicked. Returns the item's id.
    /// </summary>
    public event Action<string> OnMenuItemClicked;
    #endregion

    #region Private Fields
    private RectTransform _container;
    private GridLayoutGroup _gridLayout;
    private float _containerWidth;
    private float _containerHeight;
    private bool _isBuilt = false;
    #endregion

    #region Public Properties
    public List<RTTMenuItem> MenuItems => menuItems;
    public TMP_FontAsset CustomFont { get => customFont; set => customFont = value; }
    public int FontSize { get => fontSize; set => fontSize = value; }
    public int Columns { get => columns; set => columns = value; }
    public Vector2 Spacing { get => spacing; set => spacing = value; }
    public float ButtonAspect { get => buttonAspect; set => buttonAspect = value; }
    #endregion

    #region Preset Loaders
    /// <summary>
    /// Load the default 5 menu items: Remote, Media, Files, Settings, Quit.
    /// </summary>
    public void LoadDefaultItems()
    {
        menuItems.Clear();

        Color cyan = new Color(0f, 0.9f, 1f);
        Color purple = new Color(0.76f, 0.36f, 1f);

        menuItems.Add(new RTTMenuItem("remote", "Remote Desktop", null, cyan));
        menuItems.Add(new RTTMenuItem("media", "Media", null, purple));
        menuItems.Add(new RTTMenuItem("files", "Files", null, cyan));
        menuItems.Add(new RTTMenuItem("settings", "Settings", null, purple));
        menuItems.Add(new RTTMenuItem("quit", "Quit", null, cyan));
    }

    /// <summary>
    /// Add a menu item.
    /// </summary>
    public void AddItem(string id, string label, Sprite icon = null, Color? color = null)
    {
        menuItems.Add(new RTTMenuItem(id, label, icon, color));
    }

    /// <summary>
    /// Remove a menu item by id.
    /// </summary>
    public void RemoveItem(string id)
    {
        menuItems.RemoveAll(item => item.id == id);
    }

    /// <summary>
    /// Clear all menu items.
    /// </summary>
    public void ClearItems()
    {
        menuItems.Clear();
    }
    #endregion

    #region Build UI
    /// <summary>
    /// Build the menu UI inside the given container.
    /// </summary>
    public void BuildUI(Transform parent, float containerWidth, float containerHeight)
    {
        _containerWidth = containerWidth;
        _containerHeight = containerHeight;

        // Setup RectTransform
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        _container = rt;

        // Load icons for items that don't have them
        LoadMissingIcons();

        // Build the grid
        BuildGrid();

        _isBuilt = true;
    }

    private void LoadMissingIcons()
    {
        foreach (var item in menuItems)
        {
            if (item.icon == null && !string.IsNullOrEmpty(item.id))
            {
                // Try to load icon from Resources using id
                item.icon = LoadIcon(item.id);
            }
        }
    }

    private Sprite LoadIcon(string name)
    {
        // Try common icon naming patterns
        Sprite icon = Resources.Load<Sprite>($"icon_{name}");
        if (icon == null) icon = Resources.Load<Sprite>($"Icons/{name}");
        if (icon == null) icon = Resources.Load<Sprite>(name);
        return icon;
    }

    private void BuildGrid()
    {
        // Clear existing children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }

        // Remove existing GridLayoutGroup
        _gridLayout = GetComponent<GridLayoutGroup>();
        if (_gridLayout != null)
        {
            if (Application.isPlaying)
                Destroy(_gridLayout);
            else
                DestroyImmediate(_gridLayout);
        }

        // Add GridLayoutGroup
        _gridLayout = gameObject.AddComponent<GridLayoutGroup>();

        // Calculate cell size
        int rows = Mathf.CeilToInt((float)menuItems.Count / columns);
        if (rows < 1) rows = 1;

        float totalSpacingW = spacing.x * (columns - 1);
        float totalSpacingH = spacing.y * (rows - 1);

        float maxW = (_containerWidth - totalSpacingW) / columns;
        float maxH = (_containerHeight - totalSpacingH) / rows;

        float finalH = maxH;
        float finalW = finalH * buttonAspect;

        if (finalW > maxW)
        {
            finalW = maxW;
            finalH = finalW / buttonAspect;
        }

        Vector2 cellSize = new Vector2(finalW, finalH);

        _gridLayout.cellSize = cellSize;
        _gridLayout.spacing = spacing;
        _gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        _gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        _gridLayout.childAlignment = TextAnchor.MiddleCenter;
        _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _gridLayout.constraintCount = columns;

        // Create buttons for each menu item
        foreach (var item in menuItems)
        {
            CreateMenuButton(item, cellSize);
        }

        Debug.Log($"[RTTMainMenu] Built {menuItems.Count} items in {columns}x{rows} grid");
    }

    private void CreateMenuButton(RTTMenuItem item, Vector2 size)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = item.label,
            icon = item.icon,
            themeColor = item.color,
            width = size.x,
            height = size.y,
            fontSize = fontSize,
            font = customFont,
            popAmount = 0.025f
        };

        string itemId = item.id; // Capture for closure

        // Create button and capture reference
        GameObject buttonObj = VRButtonFactory.CreateButton(transform, config, null);

        // Get animation component for hover control
        VRButtonAnimation anim = buttonObj.GetComponentInChildren<VRButtonAnimation>();

        // Wire click handler with hover clear and animation wait
        var button = buttonObj.GetComponentInChildren<UnityEngine.UI.Button>();
        if (button != null)
        {
            button.onClick.AddListener(() =>
            {
                // Start coroutine to wait for hover animation before executing action
                StartCoroutine(ExecuteAfterHoverClear(anim, itemId));
            });
        }
    }

    /// <summary>
    /// Reset hover state immediately and execute action.
    /// Starts async preparation immediately, then opens after hover animation.
    /// </summary>
    private IEnumerator ExecuteAfterHoverClear(VRButtonAnimation anim, string itemId)
    {
        // Start preparing the app frame immediately (in background)
        // This hides the initialization lag during hover animation
        if (RTTManager.Instance != null)
        {
            RTTManager.Instance.PrepareApp(itemId);
        }

        // Reset hover state immediately (snap to default, no animation)
        // This ensures button is in correct state when returning to menu
        if (anim != null)
        {
            anim.ResetHoverState(immediate: true);
        }

        // Wait for hover reset animation + small delay for visual feedback
        yield return new WaitForSeconds(0.08f);

        // Now open the prepared app (will wait if still preparing)
        if (RTTManager.Instance != null)
        {
            RTTManager.Instance.OpenPreparedApp(itemId);
        }
        else
        {
            // Fallback to event if no AppManager
            OnMenuItemClicked?.Invoke(itemId);
        }
    }
    #endregion

    #region Rebuild
    /// <summary>
    /// Rebuild the menu grid (useful after adding/removing items).
    /// </summary>
    public void Rebuild()
    {
        if (!_isBuilt) return;
        LoadMissingIcons();
        BuildGrid();
    }
    #endregion
}
