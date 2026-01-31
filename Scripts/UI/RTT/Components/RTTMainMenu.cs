using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;

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

    [Header("Button Animation")]
    private const float BUTTON_PRESS_SCALE = 0.9f;
    private const float BUTTON_PRESS_DURATION = 0.08f;  // 80ms total (40ms down + 40ms up)

    [Header("Dwell Pre-loading")]
    // Match VRGazeReticle's dwellStartDelay - start loading when dwell ring starts showing
    private const float DWELL_PRELOAD_DELAY = 0.5f;
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

    // Track hover pre-load coroutines per button (to cancel on hover exit)
    private Dictionary<string, Coroutine> _hoverPreloadCoroutines = new Dictionary<string, Coroutine>();
    // Track which apps have already been prepared (avoid redundant calls)
    private HashSet<string> _preparedApps = new HashSet<string>();
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
        menuItems.Add(new RTTMenuItem("folder", "File Manager", null, cyan));
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

        // Get hover controller for hover state control
        HoverEffectController hoverController = buttonObj.GetComponentInChildren<HoverEffectController>();

        // Get button transform for press animation
        Transform buttonTransform = buttonObj.transform;

        // NOTE: Dwell pre-loading has been disabled to prevent visual artifacts
        // Pre-loading was causing alpha flicker when frame was created before actual click
        // Apps now load only after user actually clicks the button

        // Wire click handler with hover clear and animation wait
        var button = buttonObj.GetComponentInChildren<UnityEngine.UI.Button>();
        if (button != null)
        {
            button.onClick.AddListener(() =>
            {
                // Start coroutine to play button animation and open app
                StartCoroutine(ExecuteWithButtonAnimation(hoverController, buttonTransform, itemId));
            });
        }
    }

    /// <summary>
    /// Start delayed pre-loading of app data when user hovers over menu button.
    /// Loading starts after DWELL_PRELOAD_DELAY (0.5s) to match when dwell ring appears.
    /// </summary>
    private void StartHoverPreload(string itemId)
    {
        // Cancel any existing preload for this item
        CancelHoverPreload(itemId);

        // Start new delayed preload
        _hoverPreloadCoroutines[itemId] = StartCoroutine(DelayedPreloadCoroutine(itemId));
    }

    /// <summary>
    /// Cancel pending pre-load if user looks away before delay completes.
    /// </summary>
    private void CancelHoverPreload(string itemId)
    {
        if (_hoverPreloadCoroutines.TryGetValue(itemId, out Coroutine coroutine))
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
            _hoverPreloadCoroutines.Remove(itemId);
        }
    }

    /// <summary>
    /// Wait for dwell delay then start background data loading.
    /// </summary>
    private IEnumerator DelayedPreloadCoroutine(string itemId)
    {
        // Wait for dwell start delay (matches when dwell ring starts appearing)
        yield return new WaitForSeconds(DWELL_PRELOAD_DELAY);

        // Only prepare if not already prepared
        if (!_preparedApps.Contains(itemId))
        {
            _preparedApps.Add(itemId);

            // Start background data loading
            if (RTTManager.Instance != null)
            {
                Debug.Log($"[RTTMainMenu] Dwell pre-loading app: {itemId}");
                RTTManager.Instance.PrepareApp(itemId);
            }
        }

        // Remove from tracking
        _hoverPreloadCoroutines.Remove(itemId);
    }

    /// <summary>
    /// Execute button click with press animation and smooth transition.
    /// Flow: Prepare App → Reset Hover → Press Animation → Open App with Transition
    /// App preparation starts on click, loading happens during button animation.
    /// </summary>
    private IEnumerator ExecuteWithButtonAnimation(HoverEffectController hoverController, Transform buttonTransform, string itemId)
    {
        // 1. Start app preparation - frame created invisibly during button animation
        if (RTTManager.Instance != null && !_preparedApps.Contains(itemId))
        {
            _preparedApps.Add(itemId);
            RTTManager.Instance.PrepareApp(itemId);
        }

        // 2. Reset hover state immediately (snap to default, no animation)
        if (hoverController != null)
        {
            hoverController.ResetHoverState(immediate: true);
        }

        // 3. Play button press animation (80ms)
        yield return StartCoroutine(ButtonPressAnimation(buttonTransform));

        // 4. Open the prepared app with smooth transition
        if (RTTManager.Instance != null)
        {
            RTTManager.Instance.OpenPreparedApp(itemId);
        }
        else
        {
            // Fallback to event if no AppManager
            OnMenuItemClicked?.Invoke(itemId);
        }

        // 5. Clear prepared state (allow re-preparation if user returns to menu)
        _preparedApps.Remove(itemId);
    }

    /// <summary>
    /// Play button press animation (scale down then up).
    /// Similar to RTTMobileKeyboard key press animation.
    /// </summary>
    private IEnumerator ButtonPressAnimation(Transform buttonTransform)
    {
        if (buttonTransform == null) yield break;

        Vector3 originalScale = buttonTransform.localScale;
        Vector3 pressedScale = originalScale * BUTTON_PRESS_SCALE;
        float halfDuration = BUTTON_PRESS_DURATION * 0.5f;

        // Press down phase
        float elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            // Smoothstep easing for natural feel
            t = t * t * (3f - 2f * t);
            buttonTransform.localScale = Vector3.Lerp(originalScale, pressedScale, t);
            yield return null;
        }

        // Release up phase
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            t = t * t * (3f - 2f * t);
            buttonTransform.localScale = Vector3.Lerp(pressedScale, originalScale, t);
            yield return null;
        }

        // Ensure final scale is exact
        buttonTransform.localScale = originalScale;
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

    #region Cleanup
    private void OnDestroy()
    {
        // Cancel all pending hover preload coroutines
        foreach (var kvp in _hoverPreloadCoroutines)
        {
            if (kvp.Value != null)
            {
                StopCoroutine(kvp.Value);
            }
        }
        _hoverPreloadCoroutines.Clear();
        _preparedApps.Clear();
    }
    #endregion
}
