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

        // 1. Setup Center Grid (Main Frame Content)
        StartCoroutine(CreateCenterGrid());

        // 2. Create Side Panels
        CreateSidePanels();

        // 3. Create Pagination (Phase 4)
        CreatePagination();
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
    private GameObject _viewOptionsPopup;
    private string _currentSortBy = "Name";
    private bool _isAscending = true;
    private bool _isGridView = true;
    private TextMeshProUGUI _sortButtonText;
    
    private void CreateRow1(RectTransform parent)
    {
        RectTransform rowRT = CreateRowContainer(parent, "Row1", 0);
        
        // Left: View Options Trigger Button (replaces Dropdown)
        GameObject sortTrigger = CreateViewOptionsTrigger(rowRT);
        RectTransform sortRT = sortTrigger.GetComponent<RectTransform>();
        SetupRowElement(sortRT, Vector2.zero, new Vector2(20, 0)); // Left align

        // Right: Edit Button (Icon)
        Sprite editIcon = Resources.Load<Sprite>("icon_edit");
        GameObject editBtn = VRButtonFactory.CreateIconButton(
            rowRT, 
            50f, 
            editIcon, 
            _primaryColor,
            () => Debug.Log("Edit Clicked")
        );
        RectTransform editRT = editBtn.GetComponent<RectTransform>();
        SetupRowElement(editRT, new Vector2(1, 0.5f), new Vector2(-20, 0)); // Right align

        // Center: Search Bar
        CreateSearchBar(rowRT); 
    }
    
    private GameObject CreateViewOptionsTrigger(Transform parent)
    {
        // Create trigger using dropdown style (text with arrow indicator like VRDropdownFactory)
        GameObject trigger = new GameObject("SortTrigger");
        trigger.transform.SetParent(parent, false);
        
        RectTransform triggerRT = trigger.AddComponent<RectTransform>();
        triggerRT.sizeDelta = new Vector2(160f, 50f);
        
        // Background with glass effect
        Image bgImg = trigger.AddComponent<Image>();
        bgImg.raycastTarget = true;
        
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_CornerRadius", 0.15f);
            mat.SetFloat("_EdgePadding", 0.05f);
            mat.SetFloat("_Aspect", 160f / 50f);
            Color colorA = new Color(_primaryColor.r * 0.8f, _primaryColor.g * 0.9f, _primaryColor.b, 0.25f);
            Color colorB = new Color(_primaryColor.r, _primaryColor.g * 0.7f, _primaryColor.b * 0.9f, 0.2f);
            mat.SetColor("_ColorA", colorA);
            mat.SetColor("_ColorB", colorB);
            mat.SetFloat("_GlassAlpha", 0.25f);
            bgImg.material = mat;
            bgImg.color = Color.white;
        }
        else
        {
            bgImg.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.25f);
        }
        
        // Button component
        Button btn = trigger.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.onClick.AddListener(ToggleViewOptionsPopup);
        
        // Content container
        GameObject content = new GameObject("Content");
        content.transform.SetParent(trigger.transform, false);
        RectTransform contentRT = content.AddComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.offsetMin = new Vector2(12f, 0);
        contentRT.offsetMax = new Vector2(-12f, 0);
        
        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(content.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = new Vector2(0.75f, 1f);
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        
        _sortButtonText = textObj.AddComponent<TextMeshProUGUI>();
        _sortButtonText.text = _currentSortBy;
        _sortButtonText.fontSize = 24;
        _sortButtonText.font = _font;
        _sortButtonText.color = Color.white;
        _sortButtonText.fontStyle = FontStyles.Bold;
        _sortButtonText.alignment = TextAlignmentOptions.Left;
        _sortButtonText.verticalAlignment = VerticalAlignmentOptions.Middle;
        _sortButtonText.raycastTarget = false;
        
        // Arrow indicator (small triangle) - positioned on right
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(content.transform, false);
        RectTransform arrowRT = arrowObj.AddComponent<RectTransform>();
        arrowRT.anchorMin = new Vector2(1f, 0.5f);
        arrowRT.anchorMax = new Vector2(1f, 0.5f);
        arrowRT.pivot = new Vector2(1f, 0.5f);
        arrowRT.sizeDelta = new Vector2(16f, 16f);
        arrowRT.anchoredPosition = Vector2.zero;
        
        Image arrowImg = arrowObj.AddComponent<Image>();
        arrowImg.sprite = VRDropdownFactory.GetArrowSprite();
        arrowImg.preserveAspect = true;
        arrowImg.raycastTarget = false;
        arrowImg.color = Color.Lerp(_primaryColor, Color.white, 0.85f);
        
        // Create the popup (hidden by default)
        CreateViewOptionsPopup(trigger.transform);
        
        return trigger;
    }
    
    private void ToggleViewOptionsPopup()
    {
        if (_viewOptionsPopup != null)
        {
            _viewOptionsPopup.SetActive(!_viewOptionsPopup.activeSelf);
        }
    }
    
    private void CreateViewOptionsPopup(Transform anchor)
    {
        // Parent to ContentContainer so popup renders correctly in RTT
        Transform contentContainer = _menuFrame?.ContentContainer ?? anchor;
        
        // Popup Container
        GameObject popup = new GameObject("ViewOptionsPopup");
        popup.transform.SetParent(contentContainer, false);
        _viewOptionsPopup = popup;
        
        // Set layer to UI for RTT Camera rendering
        popup.layer = LayerMask.NameToLayer("UI");
        
        RectTransform popupRT = popup.AddComponent<RectTransform>();
        // Position at top-left, below the trigger button area
        popupRT.anchorMin = new Vector2(0, 1);
        popupRT.anchorMax = new Vector2(0, 1);
        popupRT.pivot = new Vector2(0, 1);
        popupRT.anchoredPosition = new Vector2(20, -60); // Left margin, below Row1
        popupRT.sizeDelta = new Vector2(260, 420); // Increased height to fit Order button
        
        // Background with Canvas for sorting (higher than other UI)
        Canvas popupCanvas = popup.AddComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = 200;
        popup.AddComponent<GraphicRaycaster>();
        
        // Glass Background Panel - Simple dark color (dropdown style)
        Image bgImg = popup.AddComponent<Image>();
        bgImg.sprite = VRDropdownFactory.GetPixelSprite(); 
        
        // Use shader for rounded corners but with uniform color
        Shader roundedShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (roundedShader != null)
        {
            Material mat = new Material(roundedShader);
            mat.SetFloat("_CornerRadius", 0.05f);
            mat.SetFloat("_EdgePadding", 0.01f);
            mat.SetFloat("_Aspect", 260f / 320f);
            
            // Uniform dark color for "single color" look
            Color simpleDark = new Color(0.12f, 0.12f, 0.16f, 0.96f); 
            mat.SetColor("_ColorA", simpleDark);
            mat.SetColor("_ColorB", simpleDark); // Same color = no gradient
            mat.SetFloat("_GlassAlpha", 0.96f);
            mat.SetFloat("_GradientOffset", 0f);
            
            bgImg.material = mat;
            bgImg.color = Color.white;
        }
        else
        {
            // Fallback color
            bgImg.color = new Color(0.12f, 0.12f, 0.16f, 0.96f);
        }
        
        // Content Layout - tighter spacing
        VerticalLayoutGroup vlg = popup.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 10);
        vlg.spacing = 6;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        
        // == SECTION 1: DISPLAY AS ==
        CreateSectionLabel(popup.transform, "DISPLAY AS");
        CreateDisplayModeRow(popup.transform);
        
        // == SECTION 2: SORT BY ==
        CreateSectionLabel(popup.transform, "SORT BY");
        CreateSortOptionsGrid(popup.transform);
        
        // == SECTION 3: ORDER ==
        CreateOrderToggle(popup.transform);
        
        // Set layer recursively for RTT rendering
        SetLayerRecursively(popup, LayerMask.NameToLayer("UI"));
        
        // Hide by default
        popup.SetActive(false);
    }
    
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    
    private void CreateSectionLabel(Transform parent, string text)
    {
        GameObject labelObj = new GameObject("Label_" + text);
        labelObj.transform.SetParent(parent, false);
        
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 13;
        label.font = _font;
        label.color = new Color(0.7f, 0.8f, 0.85f, 0.9f); // Lighter color to match glass theme
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Left;
        
        LayoutElement le = labelObj.AddComponent<LayoutElement>();
        le.preferredHeight = 18f; // Slightly reduced
    }
    
    private void CreateDisplayModeRow(Transform parent)
    {
        GameObject row = new GameObject("DisplayModeRow");
        row.transform.SetParent(parent, false);
        
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; // FIX: Prevent expansion
        hlg.childForceExpandHeight = false;
        
        // Alignment center to look good
        hlg.childAlignment = TextAnchor.MiddleCenter;
        
        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 40f; 
        
        // Grid Button
        Color gridColor = _isGridView ? _accentColor : _primaryColor;
        CreateModeButton(row.transform, "Grid", "icon_grid", gridColor, () => SetDisplayMode(true), _isGridView);
        
        // Line Button
        Color lineColor = !_isGridView ? _accentColor : _primaryColor;
        CreateModeButton(row.transform, "Line", "icon_line", lineColor, () => SetDisplayMode(false), !_isGridView);
    }
    
    private void CreateModeButton(Transform parent, string label, string iconName, Color color, UnityEngine.Events.UnityAction onClick, bool isSelected)
    {
        Sprite icon = Resources.Load<Sprite>(iconName);
        
        // Use Factory for safe layout construction
        // Create a horizontal button that looks standard
        GameObject btn = VRButtonFactory.CreateHorizontalIconTextButton(
            parent, 0, 40f, label, icon, color, 
            onClick, 16, _font
        );
        
        // Adjust style for selection state
        // If not selected, we might want it slightly transparent or different color
        // But the factory handles color. The isSelected logic in the previous manual code 
        // tried to adjust alpha/glass. 
        // For now, let's rely on the factory's robust rendering.
        
        // Enforce strict size constraints on the button object
        LayoutElement le = btn.GetComponent<LayoutElement>();
        if (le == null) le = btn.AddComponent<LayoutElement>();
        
        le.flexibleWidth = 0; // Disable flexibility
        le.minWidth = 110f;
        le.preferredWidth = 110f; // Fixed width
        le.preferredHeight = 36f;
    }
    
    private void CreateSortOptionsGrid(Transform parent)
    {
        GameObject grid = new GameObject("SortGrid");
        grid.transform.SetParent(parent, false);
        
        GridLayoutGroup glg = grid.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(105, 38); // Smaller cells
        glg.spacing = new Vector2(8, 6);     // Tighter spacing
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 2;
        glg.childAlignment = TextAnchor.UpperCenter;
        
        LayoutElement gridLE = grid.AddComponent<LayoutElement>();
        gridLE.preferredHeight = 130f; // Reduced height
        
        // Sort Options
        string[] options = { "Name", "Type", "Created", "Modified", "Duration", "Size" };
        foreach (string option in options)
        {
            bool isSelected = (_currentSortBy == option);
            Color btnColor = isSelected ? _accentColor : _primaryColor;
            CreateSortOptionButton(grid.transform, option, btnColor, isSelected);
        }
    }
    
    private void CreateSortOptionButton(Transform parent, string option, Color color, bool isSelected)
    {
        GameObject btn = VRButtonFactory.CreateTextButton(
            parent as RectTransform, 0, 38f, option, color, 
            () => SetSortBy(option), 15, _font
        );
    }
    
    private void CreateOrderToggle(Transform parent)
    {
        // Separator
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        Image sepImg = sep.AddComponent<Image>();
        sepImg.color = new Color(0.5f, 0.7f, 0.8f, 0.4f); // Cyan-ish to match theme
        LayoutElement sepLE = sep.AddComponent<LayoutElement>();
        sepLE.preferredHeight = 1f;
        
        // Order Row
        GameObject row = new GameObject("OrderRow");
        row.transform.SetParent(parent, false);
        
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        
        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 36f; // Reduced height
        
        // Arrow Icon - Load from Resources as requested
        Sprite arrowIcon = Resources.Load<Sprite>(_isAscending ? "icon_arrow_up" : "icon_arrow_down");
        
        // Label + Toggle
        string orderText = _isAscending ? "Ascending" : "Descending";
        
        // Use standard button factory for consistent look
        var config = new VRButtonFactory.ButtonConfig();
        config.label = orderText;
        config.icon = arrowIcon;
        config.themeColor = _primaryColor;
        config.width = 0; // flexible
        config.height = 40f;
        config.fontSize = 18;
        config.font = _font;
        config.iconSize = 20f;
        
        GameObject orderBtn = VRButtonFactory.CreateHorizontalIconTextButton(
            row.transform, 0, 40f, orderText, arrowIcon, _primaryColor,
            ToggleSortOrder, 18, _font
        );

        LayoutElement btnLE = orderBtn.AddComponent<LayoutElement>();
        btnLE.flexibleWidth = 1;
        btnLE.preferredHeight = 40f;
    }
    
    private void SetDisplayMode(bool isGrid)
    {
        _isGridView = isGrid;
        Debug.Log($"[RTTFileManager] Display mode: {(isGrid ? "Grid" : "Line")}");
        // TODO: Refresh UI with new display mode
        RefreshViewOptionsPopup();
    }
    
    private void SetSortBy(string sortBy)
    {
        _currentSortBy = sortBy;
        UpdateSortButtonText();
        Debug.Log($"[RTTFileManager] Sort by: {sortBy}");
        // TODO: Call controller to re-sort
        RefreshViewOptionsPopup();
    }
    
    private void ToggleSortOrder()
    {
        _isAscending = !_isAscending;
        Debug.Log($"[RTTFileManager] Order: {(_isAscending ? "Ascending" : "Descending")}");
        // TODO: Call controller to re-sort
        RefreshViewOptionsPopup();
    }
    
    private void UpdateSortButtonText()
    {
        if (_sortButtonText != null)
        {
            _sortButtonText.text = _currentSortBy;
        }
    }
    
    private void RefreshViewOptionsPopup()
    {
        // Destroy and recreate popup to reflect new state
        if (_viewOptionsPopup != null)
        {
            Transform anchor = _viewOptionsPopup.transform.parent;
            Destroy(_viewOptionsPopup);
            CreateViewOptionsPopup(anchor);
            _viewOptionsPopup.SetActive(true);
        }
    }

    private void CreateRow2(RectTransform parent)
    {
        RectTransform rowRT = CreateRowContainer(parent, "Row2", -_singleRowHeight);

        // Right: Refresh Button (Icon)
        Sprite refreshIcon = Resources.Load<Sprite>("icon_refresh");
        GameObject refreshBtn = VRButtonFactory.CreateIconButton(
            rowRT, 
            50f, 
            refreshIcon, 
            _accentColor,
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
        config.width = 500f; 
        config.themeColor = _accentColor;
        config.inputFontSize = 24; 
        config.font = _font;
        config.glowIntensity = 1.2f;
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
            iconObj.transform.SetParent(searchBar.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            
            // Layout: Left aligned, vertically centered
            float iconSize = 24f;
            float leftPadding = 15f;
            
            iconRT.anchorMin = new Vector2(0, 0.5f);
            iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);
            iconRT.anchoredPosition = new Vector2(leftPadding, 0);
            
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = searchIcon;
            iconImg.color = new Color(1f, 1f, 1f, 0.7f); // Slightly transparent white
            
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

    public void UpdateGrid(System.Collections.Generic.List<MockFile> files)
    {
        if (_fileGrid != null)
        {
            _fileGrid.Populate(files);
        }
        else
        {
            Debug.LogWarning("[RTTFileManager] FileGrid not ready yet!");
            // Retry not needed if we follow OnViewReady flow, but keep for safety
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
