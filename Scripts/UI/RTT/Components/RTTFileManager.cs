using UnityEngine;
using UnityEngine.UI;
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
        // Destroy side panels when main app is closed
        if (_leftFrame != null) Destroy(_leftFrame.gameObject);
        if (_rightFrame != null) Destroy(_rightFrame.gameObject);
        if (_pagination != null) Destroy(_pagination.gameObject);
    }
    
    private void OnEnable()
    {
        // Restore visibility of external components when this view is re-enabled
        if (_leftFrame != null) _leftFrame.gameObject.SetActive(true);
        if (_rightFrame != null) _rightFrame.gameObject.SetActive(true);
        if (_pagination != null) _pagination.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        // Hide external components when this view is disabled (e.g. app switch)
        if (_leftFrame != null) _leftFrame.gameObject.SetActive(false);
        if (_rightFrame != null) _rightFrame.gameObject.SetActive(false);
        if (_pagination != null) _pagination.gameObject.SetActive(false);
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
        
        _controller.OnViewReady();
    }

    private void CreateHeaderRows(RectTransform parent)
    {
        // Row 1: Sort | Search | Edit
        CreateRow1(parent);

        // Row 2: Breadcrumbs | Refresh
        CreateRow2(parent);

        // Row 3: Hidden Placeholder
        CreateRow3(parent);
    }

    // View Options Popup References
    private RTTPopupMenu _viewOptionsPopup;
    private string _currentSortBy = "Name";
    private bool _isAscending = true;
    private bool _isGridView = true;
    private Image _sortArrowImg; // Reference to arrow icon

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
    }
    
    private void SetSortBy(string sortBy)
    {
        _currentSortBy = sortBy;
        Debug.Log($"[RTTFileManager] Sort by: {sortBy}");
        RefreshViewOptionsPopup();
    }
    
    private void ToggleSortOrder()
    {
        _isAscending = !_isAscending;
        Debug.Log($"[RTTFileManager] Order: {(_isAscending ? "Ascending" : "Descending")}");
        RefreshViewOptionsPopup();
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
        
        RectTransform crumbRT = crumbContainer.AddComponent<RectTransform>();
        crumbRT.anchorMin = new Vector2(0, 0);
        crumbRT.anchorMax = new Vector2(1, 1);
        crumbRT.pivot = new Vector2(0, 0.5f);
        // Left offset 20, Right offset to leave room for Refresh button (approx 80)
        crumbRT.offsetMin = new Vector2(20, 0); 
        crumbRT.offsetMax = new Vector2(-90, 0);

        HorizontalLayoutGroup hlg = crumbContainer.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlWidth = true;
        hlg.childControlHeight = false; // Buttons have fixed height
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
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
        if (_breadcrumbContainer == null) return;
        
        foreach (Transform child in _breadcrumbContainer) Destroy(child.gameObject);

        if (string.IsNullOrEmpty(path)) return;

        // Path splitting logic
        string[] parts = path.Split(new char[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
        
        // Accumulate path for click actions
        // Assuming path is relative to some root or is absolute? 
        // For now, assume parts build up the path the Controller understands.
        string currentAccumulatedPath = "";
        
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (i == 0) currentAccumulatedPath = part;
            else currentAccumulatedPath += "/" + part;
            
            string targetPath = currentAccumulatedPath; 
            
            bool isLast = (i == parts.Length - 1);
            Color btnColor = isLast ? _accentColor : _primaryColor;
            
            GameObject btn = VRButtonFactory.CreateHorizontalIconTextButton(
                _breadcrumbContainer, 
                0, 
                40f, 
                part, 
                null, 
                btnColor,
                () => _controller?.NavigateTo(targetPath),
                18,
                _font
            );
            
            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.AddComponent<LayoutElement>();
            le.preferredWidth =  (part.Length * 12f) + 40f; 
            le.preferredHeight = 40f;
            
            if (!isLast)
            {
                CreateBreadcrumbSeparator();
            }
        }
    }
    
    private void CreateBreadcrumbSeparator()
    {
        GameObject sep = new GameObject("Sep");
        sep.transform.SetParent(_breadcrumbContainer, false);
        TextMeshProUGUI txt = sep.AddComponent<TextMeshProUGUI>();
        txt.text = ">";
        txt.color = Color.gray;
        txt.fontSize = 18;
        txt.font = _font;
        txt.alignment = TextAlignmentOptions.Center;
        
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredWidth = 20f;
        le.preferredHeight = 40f;
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
