using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Main View for File Manager App.
/// Orchestrates the 3-panel layout: Side Panel (Left), Grid (Center), Detail (Right).
/// </summary>
public class RTTFileManager : MonoBehaviour
{
    #region Configuration
    private RTTFileManagerController _controller;
    private float _containerWidth;
    private float _containerHeight;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;
    #endregion

    #region UI References
    private RTTMenuFrame _menuFrame; // Main Center Frame (Parent)
    private RTTMenuFrame _leftFrame;
    private RTTMenuFrame _rightFrame;

    private RTTFileSidePanel _sidePanel;
    private RTTFileGrid _fileGrid;
    private RTTFilePagination _pagination;

    // Flag to track if initial setup is complete (used to prevent premature Show in OnEnable)
    private bool _viewReady = false;
    #endregion

    #region Initialization
    public void Initialize(RTTFileManagerController controller, float w, float h, TMP_FontAsset font, Color primary, Color accent)
    {
        _controller = controller;
        _containerWidth = w;
        _containerHeight = h;
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;
        
        // Try to find parent frame
        _menuFrame = GetComponentInParent<RTTMenuFrame>();
    }

    // This method seems to be intended for the controller, not the view.
    // The view's responsibility is to display selection, not manage the selected file state directly.
    // The UpdateGrid method already takes a selectedPath to update the view's selection.
    // If a method is needed here, it would be to visually highlight a file.
    // public void SelectFile(string path)
    // {
    //      Debug.Log($"[Controller] Selected: {path}");
    //      // Find in current directory (works for both files and folders)
    //      _selectedFile = _currentDirectoryFiles.Find(f => f.Path == path);
    //      UpdateDetailView();
    // }

    public void BuildUI()
    {
        Debug.Log("[RTTFileManager] Building UI...");

        // Setup Main RectTransform
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        // Create Back Button (Standard for all apps)
        // Moved to Side Panel as per new design requirements
        // CreateBackButton();
        
        // Initialize 3-Panel Layout
        if (_menuFrame != null)
        {
            CreatePanels();
        }
        else
        {
            Debug.LogWarning("[RTTFileManager] Parent RTTMenuFrame not found, cannot create side panels correctly.");
        }
    }
    
    public void Cleanup()
    {
        // Reset state for potential recreation
        _viewReady = false;

        // Destroy side panels when main app is closed
        if (_leftFrame != null) Destroy(_leftFrame.gameObject);
        if (_rightFrame != null) Destroy(_rightFrame.gameObject);
        if (_pagination != null) Destroy(_pagination.gameObject);
    }
    
    private void OnEnable()
    {
        // Only restore external components after initial setup is complete (app switching)
        // During initial setup, CreateCenterGrid() handles showing pagination
        if (!_viewReady) return;

        if (_leftFrame != null) _leftFrame.gameObject.SetActive(true);
        if (_rightFrame != null) _rightFrame.gameObject.SetActive(true);
        if (_pagination != null) _pagination.Show();
    }

    private void OnDisable()
    {
        // Hide external components when this view is disabled (e.g. app switch)
        if (_leftFrame != null) _leftFrame.gameObject.SetActive(false);
        if (_rightFrame != null) _rightFrame.gameObject.SetActive(false);
        if (_pagination != null) _pagination.Hide();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
    #endregion

    #region Internal Helpers
    private void CreateBackButton()
    {
        // Use VRButtonFactory to create standard back button
        var btn = VRButtonFactory.CreateHorizontalIconTextButton(
            transform, 
            200f, 
            60f, 
            "BACK", 
            null, // TODO: Load Back Icon
            _primaryColor,
            () => _controller?.HandleBack(),
            32,
            _font
        );
        
        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(40f, -40f);
    }

    private void CreatePanels()
    {
        if (_menuFrame == null) return;

        // 1. Create Pagination (Phase 4) - Create first to ensure it's ready for invalidation in OnViewReady
        CreatePagination();

        // 2. Create Side Panels
        CreateSidePanels();

        // 3. Setup Center Grid (Main Frame Content)
        StartCoroutine(CreateCenterGrid());
    }
    
    private void CreatePagination()
    {
        // User Request: Attach to RTTTaskbar (outside menu)
        Transform targetTransform = null;
        if (RTTTaskbar.Instance != null)
        {
            targetTransform = RTTTaskbar.Instance.transform;
        }
        else
        {
            // Fallback to MenuFrame if Taskbar not found (e.g. testing)
            targetTransform = _menuFrame != null ? _menuFrame.transform : transform;
        }
        
        GameObject pagObj = new GameObject("FilePagination");
        
        // User Request: "Must be in VirtualObjects"
        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects != null)
        {
            pagObj.transform.SetParent(virtualObjects.transform, true);
        }
        
        // Initial pos near target
        pagObj.transform.position = targetTransform.position;
        pagObj.transform.rotation = targetTransform.rotation;
        
        _pagination = pagObj.AddComponent<RTTFilePagination>();
        _pagination.Initialize(_controller, targetTransform);
    }
    
    // Header References
    private RectTransform _headerRT;
    private RectTransform _bodyRT;
    private float _singleRowHeight;

    
    // Breadcrumb References
    private Transform _breadcrumbContainer;

    private IEnumerator CreateCenterGrid()
    {
        // Wait for ContentContainer of the main frame
         while (_menuFrame.ContentContainer == null) yield return null;

         // Placeholder text removal
         foreach(Transform child in _menuFrame.ContentContainer)
         {
             if (child.name == "PlaceholderText") Destroy(child.gameObject);
         }

        // Get Dimensions
        Vector2 contentSize = _menuFrame.GetContentSize();
        float panelHeight = contentSize.y;
        
        // Logic kích thước mới: Header gốc (2 rows) = 18% panel.
        // Mỗi row = 9%. Row 3 sẽ thêm 9% khi hiện.
        float headerOriginalHeight = panelHeight * 0.18f; 
        _singleRowHeight = headerOriginalHeight / 2f;
        
        // Ensure minimum row height to fit 68f buttons
        if (_singleRowHeight < 80f) 
        {
            _singleRowHeight = 80f;
            headerOriginalHeight = _singleRowHeight * 2f;
        }

        float bottomPadding = panelHeight * 0.02f;

        // 1. Create Body Object (Grid Container)
        GameObject bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(_menuFrame.ContentContainer, false);
        _bodyRT = bodyObj.AddComponent<RectTransform>();
        _bodyRT.anchorMin = Vector2.zero;
        _bodyRT.anchorMax = Vector2.one;
        _bodyRT.offsetMax = new Vector2(0, -headerOriginalHeight); // Top offset
        _bodyRT.offsetMin = new Vector2(0, bottomPadding); // Bottom offset
        
        bodyObj.AddComponent<RectMask2D>(); 

        // 2. Create Header Container
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(_menuFrame.ContentContainer, false);
        _headerRT = headerObj.AddComponent<RectTransform>();
        _headerRT.anchorMin = new Vector2(0, 1);
        _headerRT.anchorMax = new Vector2(1, 1);
        _headerRT.pivot = new Vector2(0.5f, 1);
        _headerRT.anchoredPosition = Vector2.zero;
        _headerRT.sizeDelta = new Vector2(0, headerOriginalHeight);

        // 3. Build Header Rows
        CreateHeaderRows(_headerRT);

        // 4. Create Grid Inside Body
        GameObject gridObj = new GameObject("FileGrid");
        gridObj.transform.SetParent(_bodyRT, false);
        RectTransform gridRT = gridObj.AddComponent<RectTransform>();
        gridRT.anchorMin = Vector2.zero;
        gridRT.anchorMax = Vector2.one; 
        gridRT.offsetMin = Vector2.zero;
        gridRT.offsetMax = Vector2.zero;

        _fileGrid = gridObj.AddComponent<RTTFileGrid>();
        // Calculate size based on BODY size
        float bodyHeight = panelHeight - headerOriginalHeight - bottomPadding;
        _fileGrid.Initialize(_controller, contentSize.x, bodyHeight);
        
        // Render Order
        headerObj.transform.SetAsLastSibling();
        bodyObj.transform.SetAsFirstSibling();

        // Mark setup complete - enables OnEnable/OnDisable to manage visibility for app switching
        // NOTE: Do NOT show pagination here - it will be shown by OnEnable() when the frame is actually displayed
        // During preparation, the frame is at +1000 units but pagination is at normal position (near taskbar)
        _viewReady = true;

        Debug.Log($"[RTTFileManager] Before OnViewReady - breadcrumb container null: {_breadcrumbContainer == null}, instance: {GetInstanceID()}");
        _controller.OnViewReady();
    }

    private void CreateHeaderRows(RectTransform parent)
    {
        Debug.Log($"[RTTFileManager] CreateHeaderRows started, instance: {GetInstanceID()}");

        // Row 1: Sort | Search | Edit
        CreateRow1(parent);

        // Row 2: Breadcrumbs | Refresh
        CreateRow2(parent);

        // Row 3: Hidden Placeholder
        CreateRow3(parent);

        Debug.Log($"[RTTFileManager] CreateHeaderRows completed, breadcrumb null: {_breadcrumbContainer == null}");
    }

    // View Options Popup References
    private RTTPopupMenu _viewOptionsPopup;
    private string _currentSortBy = "Name";
    private bool _isAscending = true;
    private bool _isGridView = true;
    private Image _sortArrowImg; // Reference to arrow icon

    // Ellipsis Popup References (for hidden breadcrumb folders)
    private GameObject _ellipsisPopup;
    private GameObject _ellipsisButton;
    private List<(string name, string fullPath)> _hiddenFolders = new List<(string name, string fullPath)>();
    private float _ellipsisPopupWidth; // Calculated width for popup

    private float _sortTriggerWidth = 200f; // Increased width (was 160f) to maintain aspect ratio with height
    
    private void CreateRow1(RectTransform parent)
    {
        RectTransform rowRT = CreateRowContainer(parent, "Row1", 0);
        
        // Left: View Options Trigger Button (Custom Layout: Text Left, Icon Right)
        Sprite arrowIcon = VRDropdownFactory.GetArrowSprite();
        
        // 1. Base Button (Text centered/left)
        // 1. Base Button (Text centered/left)
        var sortConfig = new VRButtonFactory.ButtonConfig
        {
            label = _currentSortBy,
            themeColor = _primaryColor,
            width = _sortTriggerWidth,
            height = 68f,
            fontSize = 24,
            font = _font,
            textOnly = true,
            borderWidth = 0.04f, // Custom thicker border (40%)
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject sortTrigger = VRButtonFactory.CreateButton(rowRT, sortConfig, ToggleViewOptionsPopup);
        
        // Adjust Text Alignment to Center (was Left)
        TextMeshProUGUI btnText = sortTrigger.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
        {
            btnText.alignment = TextAlignmentOptions.Center; // Centered
            btnText.margin = Vector4.zero; // Remove left padding
        }

        // 2. Add Icon manually (Right aligned)
        GameObject iconObj = new GameObject("ArrowIcon");
        iconObj.transform.SetParent(sortTrigger.transform, false);
        _sortArrowImg = iconObj.AddComponent<Image>();
        _sortArrowImg.sprite = arrowIcon;
        _sortArrowImg.color = Color.white; 
        _sortArrowImg.raycastTarget = false;

        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(1, 0.5f);
        iconRT.anchorMax = new Vector2(1, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f); // Pivot center for correct rotation
        iconRT.sizeDelta = new Vector2(16f, 16f);
        // Position: -25 (desired right gap) - 8 (half width) = -33
        iconRT.anchoredPosition = new Vector2(-33f, 0);

        RectTransform sortRT = sortTrigger.GetComponent<RectTransform>();
        SetupRowElement(sortRT, new Vector2(0, 0.5f), new Vector2(20, 0)); // Left-Center align
        
        // Create popup (using RTTPopupMenu) with Sort Button as parent
        CreateViewOptionsPopup(sortRT);

        // Right: Edit Button (Icon)
        Sprite editIcon = Resources.Load<Sprite>("icon_edit");
        var editConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Edit",
            icon = editIcon,
            themeColor = _primaryColor,
            width = 68f,
            height = 68f,
            iconOnly = true, // Hide text
            iconSize = 35.2f, // Reduced 20% (Default ~44)
            borderWidth = 0.04f, // Custom thicker border (Increased to match Sort/Search)
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject editBtn = VRButtonFactory.CreateButton(
            rowRT,
            editConfig,
            () => Debug.Log("Edit Clicked")
        );
        RectTransform editRT = editBtn.GetComponent<RectTransform>();
        SetupRowElement(editRT, new Vector2(1, 0.5f), new Vector2(-20, 0)); // Right align

        // Center: Search Bar
        CreateSearchBar(rowRT); 
    }
    
    private void ToggleViewOptionsPopup()
    {
        _viewOptionsPopup?.Toggle();
        
        // Rotate arrow based on visibility
        if (_sortArrowImg != null)
        {
            float targetZ = _viewOptionsPopup.IsVisible ? 180f : 0f;
            _sortArrowImg.rectTransform.localEulerAngles = new Vector3(0, 0, targetZ);
        }
    }
    
    private void CreateViewOptionsPopup(Transform parent)
    {
        // Parent to Row1 so coordinates are relative to the Sort Button
        // Sort Button is at (20, 0)
        
        // Create popup config
        var config = new RTTPopupMenu.PopupConfig
        {
            width = _sortTriggerWidth * 2.5f, // Width = 2.5x Trigger (increased 25%)
            buttonHeight = 68f, // Increased height for popup buttons too
            sideSpacing = 10f,  // Increased spacing
            rowSpacing = 10f,
            fontSize = 18,      // Larger text
            iconSize = 24f,     // Larger icon (approx matches Edit button)
            primaryColor = _primaryColor,
            accentColor = _accentColor,
            font = _font
        };
        
        // Create popup using RTTPopupMenu
        _viewOptionsPopup = RTTPopupMenu.Create(parent, config);
        
        // Override Anchor to Bottom-Left of parent (Sort Button)
        // Sort Button Pivot is (0,0), so (0,0) relative to it is its Bottom-Left corner.
        _viewOptionsPopup.SetAnchor(Vector2.zero, Vector2.zero, new Vector2(0, 1));
        
        // Build popup content
        BuildViewOptionsPopupContent();
        
        // Position the popup
        // X = 0 (Aligned Left)
        // Y = -Spacing - HalfButtonHeight (since Pivot is Center now, Anchor Bottom-Left might need offset correction or visual adjustment)
        // Adjusting by extra -34f to ensure it clears the button visual completely if anchor logic is behaving unexpectedly with new Pivot.
        _viewOptionsPopup.SetPosition(new Vector2(0, -34f - config.rowSpacing));
    }
    
    private void BuildViewOptionsPopupContent()
    {
        if (_viewOptionsPopup == null) return;
        
        _viewOptionsPopup.Clear();
        
        // Section 1: Display Mode
        Sprite gridIcon = Resources.Load<Sprite>("icon_grid");
        Sprite listIcon = Resources.Load<Sprite>("icon_list");
        
        _viewOptionsPopup.AddSectionBlock("DISPLAY AS", new System.Collections.Generic.List<RTTPopupMenu.ButtonData>
        {
            new RTTPopupMenu.ButtonData("Grid", () => SetDisplayMode(true), gridIcon, _isGridView),
            new RTTPopupMenu.ButtonData("List", () => SetDisplayMode(false), listIcon, !_isGridView)
        }, 2);
        
        // Section 2: Sort Options
        string[] sortOptions = { "Name", "Type", "Created", "Modified", "Duration", "Size" };
        var sortButtons = new System.Collections.Generic.List<RTTPopupMenu.ButtonData>();
        foreach (string option in sortOptions)
        {
            bool isSelected = (_currentSortBy == option);
            sortButtons.Add(new RTTPopupMenu.ButtonData(option, () => SetSortBy(option), null, isSelected));
        }
        _viewOptionsPopup.AddSectionBlock("SORT BY", sortButtons, 2);
        
        // Full-width Order button - No Separator
        Sprite orderIcon = Resources.Load<Sprite>(_isAscending ? "icon_ascending" : "icon_descending");
        string orderText = _isAscending ? "Ascending" : "Descending";
        _viewOptionsPopup.AddFullWidthButton(new RTTPopupMenu.ButtonData(orderText, ToggleSortOrder, orderIcon));
        
        // Build the popup
        _viewOptionsPopup.Build();
    }
    
    private void SetDisplayMode(bool isGrid)
    {
        _isGridView = isGrid;
        Debug.Log($"[RTTFileManager] Display mode: {(isGrid ? "Grid" : "List")}");
        RefreshViewOptionsPopup();
        // TODO: Implement Grid/List view switch
    }

    private void SetSortBy(string sortBy)
    {
        _currentSortBy = sortBy;
        Debug.Log($"[RTTFileManager] Sort by: {sortBy}");
        RefreshViewOptionsPopup();

        // Call controller to apply sort
        _controller?.SetSortOptions(_currentSortBy, _isAscending);
    }

    private void ToggleSortOrder()
    {
        _isAscending = !_isAscending;
        Debug.Log($"[RTTFileManager] Order: {(_isAscending ? "Ascending" : "Descending")}");
        RefreshViewOptionsPopup();

        // Call controller to apply sort
        _controller?.SetSortOptions(_currentSortBy, _isAscending);
    }
    
    private void RefreshViewOptionsPopup()
    {
        if (_viewOptionsPopup != null)
        {
            BuildViewOptionsPopupContent();
            _viewOptionsPopup.Show();
        }
    }
    
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void CreateRow2(RectTransform parent)
    {
        Debug.Log("[RTTFileManager] CreateRow2 called");
        RectTransform rowRT = CreateRowContainer(parent, "Row2", -_singleRowHeight);

        // Right: Refresh Button (Icon)
        Sprite refreshIcon = Resources.Load<Sprite>("icon_refresh");
        var refreshConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Refresh",
            icon = refreshIcon,
            themeColor = _accentColor,
            width = 68f,
            height = 68f,
            iconOnly = true, // Hide text
            iconSize = 35.2f, // Reduced 20% (Default ~44)
            borderWidth = 0.04f, // Custom thicker border (Increased to match Sort/Search)
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject refreshBtn = VRButtonFactory.CreateButton(
            rowRT, 
            refreshConfig, 
            () => _controller?.RefreshCurrentFolder()
        );
        RectTransform refreshRT = refreshBtn.GetComponent<RectTransform>();
        SetupRowElement(refreshRT, new Vector2(1, 0.5f), new Vector2(-20, 0));

        // Left Container for Breadcrumbs
        GameObject crumbContainer = new GameObject("Breadcrumbs");
        crumbContainer.transform.SetParent(rowRT, false);
        _breadcrumbContainer = crumbContainer.transform;
        Debug.Log($"[RTTFileManager] Breadcrumb container set: {_breadcrumbContainer != null}, instance: {GetInstanceID()}");

        RectTransform crumbRT = crumbContainer.AddComponent<RectTransform>();
        crumbRT.anchorMin = new Vector2(0, 0);
        crumbRT.anchorMax = new Vector2(1, 1);
        crumbRT.pivot = new Vector2(0, 0.5f);
        // Left offset 20, Right offset to leave room for Refresh button (approx 80)
        crumbRT.offsetMin = new Vector2(20, 0); 
        crumbRT.offsetMax = new Vector2(-90, 0);

        // NOTE: NOT using HorizontalLayoutGroup to allow independent control of:
        // - Visual position (left to right)
        // - Sibling order (reversed, so left buttons have higher index = hit first by GraphicRaycaster)
    }

    private void CreateRow3(RectTransform parent)
    {
        RectTransform rowRT = CreateRowContainer(parent, "Row3", -_singleRowHeight * 2);
        rowRT.gameObject.SetActive(false); // Hidden by default
    }

    private RectTransform CreateRowContainer(Transform parent, string name, float yPos)
    {
        GameObject rowObj = new GameObject(name);
        rowObj.transform.SetParent(parent, false);
        RectTransform rt = rowObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, _singleRowHeight);
        rt.anchoredPosition = new Vector2(0, yPos);
        return rt;
    }

    private void SetupRowElement(RectTransform rt, Vector2 anchor, Vector2 anchoredPos)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor; 
        if (anchor.x == 0.5f) rt.pivot = new Vector2(0.5f, 0.5f); 
        rt.anchoredPosition = anchoredPos;
    }
    
    private void CreateSearchBar(Transform parent)
    {
        if (parent == null) return;
        
        var config = new VRInputFieldFactory.InputFieldConfig();
        config.label = "";
        config.placeholder = "Search current folder..."; 
        // Width = 3 * CellWidth (310) + 2 * SpacingX (30) = 990f. Excluding outer spacings.
        config.width = 990f; 
        config.themeColor = _primaryColor;
        // Increase font size to 31 to achieve ~68f height (31 * 2.2 = 68.2)
        config.inputFontSize = 31; 
        config.font = _font;
        
        // Match Button Visuals (Sharper, Brighter Border)
        config.borderWidth = 0.04f;  // Increased 40% (was 0.025f)
        config.glowWidth = 0.08f;     // Match Button
        config.glowIntensity = 4f;  // Match Button
        
        config.layerName = "UI";
        
        GameObject searchBar = VRInputFieldFactory.CreateInputField(
            parent as RectTransform, 
            config, 
            OnSearchValueChanged, 
            OnSearchEndEdit
        );
        
        // --- Customization for Search Icon ---
        // 1. Load Icon
        Sprite searchIcon = Resources.Load<Sprite>("icon_search");
        
        if (searchIcon != null)
        {
            // 2. Create Icon Image
            GameObject iconObj = new GameObject("SearchIcon");
            
            // Parent to Visuals to participate in Hover/Pop animation
            Transform visuals = searchBar.transform.Find("InputBox/HitArea/Visuals");
            if (visuals != null)
            {
                iconObj.transform.SetParent(visuals, false);
            }
            else
            {
                // Fallback if structure changes
                iconObj.transform.SetParent(searchBar.transform, false);
            }

            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            
            // Layout: Left aligned, vertically centered
            float iconSize = 24f;
            float leftPadding = 20f;
            
            iconRT.anchorMin = new Vector2(0, 0.5f);
            iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);
            iconRT.anchoredPosition = new Vector2(leftPadding, 0);
            
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = searchIcon;
            iconImg.color = new Color(1f, 1f, 1f, 0.8f); // Slightly transparent white
            
            // 3. Adjust Text Area Padding
            // Find "Text Area" child (standard TMP InputField structure)
            Transform textArea = searchBar.transform.Find("Text Area");
            if (textArea != null)
            {
                RectTransform textAreaRT = textArea.GetComponent<RectTransform>();
                // Push text to right: Icon Width + Padding + Spacing
                float textOffset = leftPadding + iconSize + 10f; 
                textAreaRT.offsetMin = new Vector2(textOffset, textAreaRT.offsetMin.y);
            }
        }
        // -------------------------------------
        
        RectTransform rt = searchBar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(config.width, 68f); // Force height 68f to match buttons
        rt.anchoredPosition = Vector2.zero;
    }
    
    // Breadcrumb Update Logic
    public void UpdateBreadcrumbs(string path)
    {
        Debug.Log($"[RTTFileManager] UpdateBreadcrumbs called with path: {path}, container null: {_breadcrumbContainer == null}, instance: {GetInstanceID()}");

        // Fallback: find breadcrumb container from hierarchy if reference is lost
        if (_breadcrumbContainer == null)
        {
            Debug.LogWarning("[RTTFileManager] Breadcrumb container reference lost, searching in hierarchy...");
            var row2 = _headerRT?.Find("Row2");
            if (row2 != null)
            {
                _breadcrumbContainer = row2.Find("Breadcrumbs");
                Debug.Log($"[RTTFileManager] Found breadcrumb container from hierarchy: {_breadcrumbContainer != null}");
            }
        }

        if (_breadcrumbContainer == null)
        {
            Debug.LogWarning("[RTTFileManager] Breadcrumb container is null and could not be found!");
            return;
        }

        foreach (Transform child in _breadcrumbContainer) Destroy(child.gameObject);

        if (string.IsNullOrEmpty(path)) return;

        // Get root path and create relative path for display
        string rootPath = FileSystemService.RootPath;
        string displayPath = path;

        // Create list of breadcrumb items
        var breadcrumbs = new List<(string name, string fullPath, bool isEllipsis)>();

        // Always add "Home" as first breadcrumb
        breadcrumbs.Add(("Home", "root", false));

        // If path is not root, add subfolders
        if (path != "root" && !string.IsNullOrEmpty(path))
        {
            // Make path relative to root for display
            if (path.StartsWith(rootPath))
            {
                displayPath = path.Substring(rootPath.Length).TrimStart('/', '\\');
            }

            if (!string.IsNullOrEmpty(displayPath))
            {
                string[] parts = displayPath.Split(new char[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
                string currentPath = rootPath;

                var allFolders = new List<(string name, string fullPath)>();
                foreach (string part in parts)
                {
                    currentPath = System.IO.Path.Combine(currentPath, part);
                    allFolders.Add((part, currentPath));
                }

                // Limit: Max 6 buttons total (Home + 5 folders OR Home + ... + 4 folders)
                const int maxButtons = 6;
                const int maxFoldersWithoutEllipsis = maxButtons - 1; // 5 folders after Home

                if (allFolders.Count <= maxFoldersWithoutEllipsis)
                {
                    // No truncation needed
                    _hiddenFolders.Clear();
                    foreach (var folder in allFolders)
                    {
                        breadcrumbs.Add((folder.name, folder.fullPath, false));
                    }
                }
                else
                {
                    // Need ellipsis: Home > ... > last 4 folders
                    const int visibleFoldersAfterEllipsis = maxButtons - 2; // 4 folders after "..."

                    // Store hidden folders for dropdown
                    _hiddenFolders.Clear();
                    int hiddenCount = allFolders.Count - visibleFoldersAfterEllipsis;
                    for (int j = 0; j < hiddenCount; j++)
                    {
                        _hiddenFolders.Add((allFolders[j].name, allFolders[j].fullPath));
                    }

                    // Add "..." as second button (clickable - shows dropdown)
                    breadcrumbs.Add(("...", "", true));

                    // Add last N folders
                    int startIndex = allFolders.Count - visibleFoldersAfterEllipsis;
                    for (int j = startIndex; j < allFolders.Count; j++)
                    {
                        breadcrumbs.Add((allFolders[j].name, allFolders[j].fullPath, false));
                    }
                }
            }
        }

        // Create breadcrumb chevron buttons (connected style like reference image)
        // Strategy: All buttons are pill-shaped, left buttons overlap right buttons
        float btnHeight = 68f; // Same height as sortTrigger button
        float btnWidth = _sortTriggerWidth * 1.25f;
        int totalCount = breadcrumbs.Count;

        // Create buttons in REVERSE order (right-to-left) for GraphicRaycaster priority
        // Left buttons created LAST = higher sibling index = hit first
        float overlapAmount = btnHeight * 0.45f; // Same as old HLG spacing
        float effectiveWidth = btnWidth - overlapAmount;

        // Cleanup old ellipsis popup if exists
        if (_ellipsisPopup != null)
        {
            Destroy(_ellipsisPopup);
            _ellipsisPopup = null;
        }
        _ellipsisButton = null;

        // Calculate popup width: same as the "..." button width
        _ellipsisPopupWidth = btnWidth;

        for (int i = totalCount - 1; i >= 0; i--)
        {
            var (name, fullPath, isEllipsis) = breadcrumbs[i];
            string targetPath = fullPath;
            bool isLast = (i == totalCount - 1);

            int zIndex = i;
            GameObject btn = CreateBreadcrumbPillButton(name, targetPath, isLast, btnWidth, btnHeight, zIndex, totalCount, isEllipsis);

            // Save reference to ellipsis button for popup
            if (isEllipsis)
            {
                _ellipsisButton = btn;
                CreateEllipsisPopup(btn.GetComponent<RectTransform>());
            }

            // Manual positioning (left-to-right visual order)
            RectTransform btnRT = btn.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0, 0.5f);
            btnRT.anchorMax = new Vector2(0, 0.5f);
            btnRT.pivot = new Vector2(0, 0.5f);

            // X position: each button offset by effectiveWidth (width - overlap)
            float xPos = i * effectiveWidth;
            btnRT.anchoredPosition = new Vector2(xPos, 0);

            // Z-position for visual layering (left buttons closer to camera)
            // This is secondary to sibling order but helps with any physics-based raycast
            Vector3 pos = btnRT.localPosition;
            pos.z = zIndex * -0.5f; // Lower zIndex = closer (more positive in local space toward camera)
            btnRT.localPosition = pos;
        }

        Debug.Log($"[RTTFileManager] Created {breadcrumbs.Count} breadcrumbs for path: {path}");
    }
    
    /// <summary>
    /// Creates a chevron-shaped breadcrumb button.
    /// Right side: convex rounded (pill end)
    /// Left side: concave curved (inward arc)
    /// </summary>
    private GameObject CreateBreadcrumbPillButton(string label, string targetPath, bool isActive, float width, float height, int zIndex, int totalButtons, bool isEllipsis = false)
    {
        bool isFirstButton = (zIndex == 0);

        // First button (Home) always uses primary color, never shows "selected" state
        // Other buttons: accent if active (current folder), primary if not
        Color btnColor = isFirstButton ? _primaryColor : (isActive ? _accentColor : _primaryColor);
        string pathToNavigate = targetPath;

        // Button container
        GameObject btnObj = new GameObject($"Crumb_{label}");
        btnObj.transform.SetParent(_breadcrumbContainer, false);

        RectTransform btnRT = btnObj.AddComponent<RectTransform>();
        btnRT.sizeDelta = new Vector2(width, height);

        // NOTE: Z-position is set in UpdateBreadcrumbs after anchoredPosition is set

        // Background with appropriate shader
        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.raycastTarget = true;

        float aspect = width / height;

        // Glass background style (like MainMenu button hover state)
        float glassAlpha = 0.2f; // Semi-transparent glass effect

        if (isFirstButton)
        {
            // First button: rounded shape matching chevron curve
            Shader pillShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (pillShader != null)
            {
                Material mat = new Material(pillShader);
                mat.SetFloat("_Aspect", aspect);
                mat.SetFloat("_CornerRadius", 0.48f); // Full semicircle (halfH = 0.5 - padding)
                mat.SetFloat("_EdgePadding", 0.02f);
                // Gradient colors for glass effect
                Color colorA = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 1.5f);
                Color colorB = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 0.5f);
                mat.SetColor("_ColorA", colorA);
                mat.SetColor("_ColorB", colorB);
                mat.SetFloat("_GlassAlpha", glassAlpha);
                mat.SetFloat("_FresnelStrength", 0.15f); // Edge glow
                bgImage.material = mat;
                bgImage.color = Color.white;
            }
            else
            {
                bgImage.color = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha);
            }
        }
        else
        {
            // Subsequent buttons: chevron shape (convex right, concave left)
            Shader chevronShader = Shader.Find("Custom/ChevronBackground");
            if (chevronShader != null)
            {
                Material mat = new Material(chevronShader);
                mat.SetFloat("_Aspect", aspect);
                mat.SetFloat("_EdgePadding", 0.02f);
                mat.SetColor("_BackgroundColor", btnColor);
                mat.SetFloat("_BackgroundAlpha", glassAlpha);
                mat.SetFloat("_EdgeGlow", 0.15f); // Edge glow
                mat.SetFloat("_CenterGlow", 0.1f); // Center glow
                bgImage.material = mat;
                bgImage.color = Color.white;
            }
            else
            {
                bgImage.color = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha);
            }
        }

        // Text centered - constrained to visible area
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;

        // Text padding based on shape
        // Curve radius = halfHeight = height * 0.5 (full semicircle)
        float curveR = height * 0.5f;
        if (isFirstButton)
        {
            // Rounded rect: padding for both rounded corners
            textRT.offsetMin = new Vector2(curveR * 0.85f, 0); // Left padding
            textRT.offsetMax = new Vector2(-curveR * 0.75f, 0); // Right padding
        }
        else
        {
            // Chevron: small left (concave opens up space), normal right
            textRT.offsetMin = new Vector2(curveR * 1.1f, 0); // Less left padding
            textRT.offsetMax = new Vector2(-curveR * 0.6f, 0); // Right padding for convex
        }

        TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 22;
        txt.font = _font;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false; // Single line
        txt.overflowMode = TextOverflowModes.Ellipsis; // Show ... if too long

        // Button component
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bgImage;
        btn.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        btn.colors = colors;

        if (!isEllipsis)
        {
            // Normal breadcrumb: navigate to folder
            btn.onClick.AddListener(() => _controller?.NavigateTo(pathToNavigate));
        }
        else
        {
            // Ellipsis: toggle dropdown showing hidden folders
            btn.onClick.AddListener(ToggleEllipsisPopup);
        }

        // BoxCollider for all clickable buttons
        BoxCollider col = btnObj.AddComponent<BoxCollider>();

        // Calculate overlap amount
        float overlapAmount = 68f * 0.45f; // ~30.6f
        float safetyBuffer = 5f; // Extra buffer to ensure no overlap

        if (isFirstButton)
        {
            // First button: trim RIGHT side where next button overlaps
            float trimRight = overlapAmount + safetyBuffer;
            col.size = new Vector3(width - trimRight, height, 0.1f);
            col.center = new Vector3(-trimRight * 0.5f, 0, 0);
        }
        else
        {
            // Chevron buttons: trim LEFT side (overlap with previous) and RIGHT (overlap with next)
            float trimLeft = overlapAmount + safetyBuffer;
            float trimRight = (zIndex < totalButtons - 1) ? (overlapAmount + safetyBuffer) : 0;
            float totalTrim = trimLeft + trimRight;
            col.size = new Vector3(width - totalTrim, height, 0.1f);
            col.center = new Vector3((trimLeft - trimRight) * 0.5f, 0, 0);
        }

        // Set layer
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1)
        {
            btnObj.layer = vrLayer;
            textObj.layer = vrLayer;
        }

        // Add hover effect (change alpha like selected state) - only for non-active buttons
        if (!isActive)
        {
            AddBreadcrumbHoverEffect(btnObj, bgImage, btnColor, isFirstButton);
        }

        return btnObj;
    }

    /// <summary>
    /// Adds animated hover effect to breadcrumb button.
    /// On hover: color transitions to accent color with smooth animation.
    /// </summary>
    private void AddBreadcrumbHoverEffect(GameObject btnObj, Image bgImage, Color baseColor, bool isFirstButton)
    {
        // Hover values - same alpha as selected state, only color changes
        float normalAlpha = 0.2f;
        float hoverAlpha = 0.2f; // Same as selected, color change is enough visual feedback

        // Use BreadcrumbHoverAnimator for smooth animated transitions
        BreadcrumbHoverAnimator animator = btnObj.AddComponent<BreadcrumbHoverAnimator>();
        animator.Initialize(bgImage, baseColor, _accentColor, normalAlpha, hoverAlpha, isFirstButton);
    }

    /// <summary>
    /// Creates simple popup for ellipsis button showing hidden folders
    /// Style: No border, glass background matching button, left-aligned text only
    /// </summary>
    private void CreateEllipsisPopup(RectTransform ellipsisButtonRT)
    {
        if (_hiddenFolders.Count == 0) return;

        float itemHeight = 50f;
        float verticalPadding = 12f;
        float popupHeight = (_hiddenFolders.Count * itemHeight) + (verticalPadding * 2);

        // Create popup container
        _ellipsisPopup = new GameObject("EllipsisPopup");
        _ellipsisPopup.transform.SetParent(_breadcrumbContainer, false);

        RectTransform popupRT = _ellipsisPopup.AddComponent<RectTransform>();
        popupRT.sizeDelta = new Vector2(_ellipsisPopupWidth, popupHeight);

        // Position: anchor to left edge of ellipsis button, below it
        popupRT.anchorMin = new Vector2(0, 0.5f);
        popupRT.anchorMax = new Vector2(0, 0.5f);
        popupRT.pivot = new Vector2(0, 1); // Top-left pivot

        // Get ellipsis button position (it's at index 1, so xPos = 1 * effectiveWidth)
        float btnHeight = 68f;
        float overlapAmount = btnHeight * 0.45f;
        float btnWidth = _sortTriggerWidth * 1.25f;
        float effectiveWidth = btnWidth - overlapAmount;
        float ellipsisX = effectiveWidth; // Index 1 position

        popupRT.anchoredPosition = new Vector2(ellipsisX, -btnHeight / 2 - 8f);

        // Background matching VRDropdownFactory's dropdown panel style
        Image bgImage = _ellipsisPopup.AddComponent<Image>();
        float aspect = _ellipsisPopupWidth / popupHeight;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_CornerRadius", 0.06f);
            mat.SetFloat("_EdgePadding", 0.01f);

            // Match VRDropdownFactory's CreateViewportBackground style (increased alpha for visibility)
            float backgroundAlpha = 0.25f;
            Color col = _primaryColor;
            Color colorA = new Color(col.r * 0.8f, col.g * 0.9f, col.b, backgroundAlpha * 1.8f);
            Color colorB = new Color(col.r, col.g * 0.7f, col.b * 0.9f, backgroundAlpha * 1.5f);
            mat.SetColor("_ColorA", colorA);
            mat.SetColor("_ColorB", colorB);
            mat.SetFloat("_GradientOffset", 0f);
            mat.SetFloat("_GradientAngle", -10f);
            mat.SetFloat("_CyanRatio", 0.7f);
            mat.SetFloat("_GlassAlpha", backgroundAlpha * 1.2f);
            mat.SetFloat("_FresnelPower", 2.2f);
            mat.SetFloat("_FresnelStrength", 0.12f);

            bgImage.material = mat;
            bgImage.color = Color.white;
        }
        else
        {
            bgImage.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.3f);
        }

        // Create folder items
        for (int i = 0; i < _hiddenFolders.Count; i++)
        {
            var folder = _hiddenFolders[i];
            CreateEllipsisPopupItem(folder.name, folder.fullPath, i, itemHeight, verticalPadding);
        }

        // Set layer
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1)
        {
            SetLayerRecursively(_ellipsisPopup, vrLayer);
        }

        // Start hidden
        _ellipsisPopup.SetActive(false);
    }

    /// <summary>
    /// Creates a single item in the ellipsis popup
    /// </summary>
    private void CreateEllipsisPopupItem(string folderName, string folderPath, int index, float itemHeight, float verticalPadding)
    {
        float horizontalPadding = 20f;

        GameObject itemObj = new GameObject($"Item_{folderName}");
        itemObj.transform.SetParent(_ellipsisPopup.transform, false);

        RectTransform itemRT = itemObj.AddComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0, 1);
        itemRT.anchorMax = new Vector2(1, 1);
        itemRT.pivot = new Vector2(0.5f, 1);
        itemRT.sizeDelta = new Vector2(0, itemHeight);
        itemRT.anchoredPosition = new Vector2(0, -verticalPadding - (index * itemHeight));

        // Background for hover effect (starts transparent)
        Image btnBg = itemObj.AddComponent<Image>();
        btnBg.color = Color.clear;
        btnBg.raycastTarget = true;

        // Button with no color transition (we handle hover via EventTrigger)
        Button btn = itemObj.AddComponent<Button>();
        btn.targetGraphic = btnBg;
        btn.transition = Selectable.Transition.None;

        string path = folderPath;
        btn.onClick.AddListener(() => OnHiddenFolderSelected(path));

        // Add hover effect via EventTrigger (ColorTint doesn't work with Color.clear)
        Color hoverColor = new Color(0f, 0f, 0f, 0.27f); // Dark hover like gridItem
        EventTrigger trigger = itemObj.AddComponent<EventTrigger>();

        EventTrigger.Entry enterEntry = new EventTrigger.Entry();
        enterEntry.eventID = EventTriggerType.PointerEnter;
        enterEntry.callback.AddListener((data) => { btnBg.color = hoverColor; });
        trigger.triggers.Add(enterEntry);

        EventTrigger.Entry exitEntry = new EventTrigger.Entry();
        exitEntry.eventID = EventTriggerType.PointerExit;
        exitEntry.callback.AddListener((data) => { btnBg.color = Color.clear; });
        trigger.triggers.Add(exitEntry);

        // Text label (left aligned)
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(itemObj.transform, false);

        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(horizontalPadding, 0);
        textRT.offsetMax = new Vector2(-horizontalPadding, 0);

        TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = folderName;
        txt.font = _font;
        txt.fontSize = 26;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.MidlineLeft; // Left aligned
        txt.raycastTarget = false;

        // BoxCollider for VR raycast
        BoxCollider col = itemObj.AddComponent<BoxCollider>();
        col.size = new Vector3(_ellipsisPopupWidth, itemHeight, 0.1f);
        col.center = Vector3.zero;
    }

    /// <summary>
    /// Toggle ellipsis popup visibility
    /// </summary>
    private void ToggleEllipsisPopup()
    {
        if (_ellipsisPopup == null) return;
        _ellipsisPopup.SetActive(!_ellipsisPopup.activeSelf);
    }

    /// <summary>
    /// Handle selection of a hidden folder from ellipsis popup
    /// </summary>
    private void OnHiddenFolderSelected(string path)
    {
        // Hide popup
        if (_ellipsisPopup != null)
        {
            _ellipsisPopup.SetActive(false);
        }

        // Navigate to selected folder
        _controller?.NavigateTo(path);
    }

    private void OnSearchValueChanged(string value)
    {
        _controller.SearchFiles(value);
    }
    
    private void OnSearchEndEdit(string value)
    {
        _controller.SearchFiles(value);
    }

    public void UpdatePagination(int current, int total)
    {
         if (_pagination != null)
         {
             _pagination.SetPage(current, total);
         }
    }
    
    public void ScrollToPage(int page)
    {
        if (_fileGrid != null)
        {
             // Assume 2 rows per page
             _fileGrid.ScrollToPage(page, 2);
        }
    }

    public void UpdateGrid(System.Collections.Generic.List<MockFile> files, string selectedPath = "")
    {
        if (_fileGrid != null)
        {
            _fileGrid.Populate(files, selectedPath);
        }
        else
        {
            Debug.LogWarning("[RTTFileManager] FileGrid not ready yet!");
        }
    }

    private void CreateSidePanels()
    {
        float mainPanelWidth = _menuFrame.PanelWidth;
        float mainPanelHeight = _menuFrame.PanelHeight;
        float sideWidth = mainPanelWidth / 3f;
        float sideHeight = mainPanelHeight;
        float gapMeters = 0.05f;
        float rotationAngle = 30f;

        // Left Panel (Navigation)
        PlaceSidePanelFlat("FileNavigationPanel", -1, mainPanelWidth, sideWidth, sideHeight, gapMeters, rotationAngle, ref _leftFrame);
        if (_leftFrame != null)
        {
            _leftFrame.SetVisible(true); // Make visible immediately for now
            // Initialize Side Panel Content
            StartCoroutine(CreateLeftPanelContent());
        }

        // Right Panel (Detail)
        PlaceSidePanelFlat("FileDetailPanel", 1, mainPanelWidth, sideWidth, sideHeight, gapMeters, rotationAngle, ref _rightFrame);
        if (_rightFrame != null)
        {
            _rightFrame.SetVisible(true); // Make visible immediately for now
            // Initialize Right Panel Content (Placeholder)
            StartCoroutine(CreateRightPanelContent());
        }
    }

    private void PlaceSidePanelFlat(string name, int side, float mainWidth, float sideWidth, float sideHeight,
        float gap, float rotationAngle, ref RTTMenuFrame frameRef)
    {
        // Get main panel's world transform
        Vector3 mainPos = _menuFrame.transform.position;
        Quaternion mainRot = _menuFrame.transform.rotation;
        Vector3 mainRight = _menuFrame.transform.right;
        Vector3 mainForward = _menuFrame.transform.forward;

        float rotRad = rotationAngle * Mathf.Deg2Rad;
        float halfSide = sideWidth / 2f;
        float centerOffsetX = halfSide * Mathf.Cos(rotRad);
        float centerOffsetZ = -halfSide * Mathf.Sin(rotRad);

        float totalX = (mainWidth / 2f) + gap + centerOffsetX;
        float totalZ = centerOffsetZ;

        Vector3 offset = mainRight * (side * totalX) + mainForward * totalZ;
        Vector3 panelPos = mainPos + offset;
        Quaternion panelRot = mainRot * Quaternion.Euler(0, side * rotationAngle, 0);

        float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

        // Create RTTMenuFrame
        // Note: Creating as child of _menuFrame.transform can cause issues if parent scales/hides.
        // But RTTRemoteMenu does it this way initially, then RTTRemoteMenuController manages it.
        // Here we create it as child of main frame for hierarchy organization, 
        // but physically positioned in world space.
        frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
        frameRef.transform.position = panelPos;
        frameRef.transform.rotation = panelRot;
        frameRef.transform.localScale = Vector3.one;
        
        frameRef.SetContentMargins(20f, 20f, 20f, 20f);
        frameRef.SetFloatingDataEnabled(true, 5);
    }

    private IEnumerator CreateLeftPanelContent()
    {
        // Wait for ContentContainer
        while (_leftFrame.ContentContainer == null) yield return null;
        
        var containerSize = _leftFrame.GetContentSize();
        
        GameObject contentObj = new GameObject("SidePanelContent");
        contentObj.transform.SetParent(_leftFrame.ContentContainer, false);
        RectTransform rt = contentObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _sidePanel = contentObj.AddComponent<RTTFileSidePanel>();
        _sidePanel.Initialize(_controller, containerSize.x, containerSize.y, _font, _primaryColor, _accentColor);
    }
    
    private RTTFileDetail _fileDetail;

    public void UpdateDetail(MockFile file, bool isCurrentFolder)
    {
        if (_fileDetail != null)
        {
            _fileDetail.UpdateInfo(file, isCurrentFolder);
        }
    }

    private IEnumerator CreateRightPanelContent()
    {
        // Wait for ContentContainer
        while (_rightFrame.ContentContainer == null) yield return null;
        
        GameObject contentObj = new GameObject("DetailContent");
        contentObj.transform.SetParent(_rightFrame.ContentContainer, false);
        RectTransform rt = contentObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _fileDetail = contentObj.AddComponent<RTTFileDetail>();
        _fileDetail.Initialize(_primaryColor, _accentColor);
    }
    #endregion
}
