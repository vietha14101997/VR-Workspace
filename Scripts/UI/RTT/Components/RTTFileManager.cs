using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;

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
    private RTTFileList _fileList;
    private RTTFilePagination _pagination;
    private RTTPopupInputable _createFolderPopup;

    // New Folder button reference (for enabling/disabling based on permissions)
    private Button _newFolderButton;
    private CanvasGroup _newFolderCanvasGroup;

    // Flag to track if initial setup is complete (used to prevent premature Show in OnEnable)
    private bool _viewReady = false;

    // Edit Mode
    private bool _isEditMode = false;
    private GameObject _editButton;
    private HashSet<string> _selectedItems = new HashSet<string>();
    private GameObject _selectAllCheckbox;
    private Image _selectAllCheckmark;

    // Edit Mode UI in Row2
    private GameObject _editControlsContainer; // Container for edit mode controls (replaces breadcrumbs)
    private TextMeshProUGUI _selectedCountText; // "X selected" text to the left of item count (shown in Edit Mode)

    // Edit Mode Action Buttons
    private Button _copyButton;
    private Button _moveButton;
    private Button _deleteButton;
    private CanvasGroup _copyButtonCG;
    private CanvasGroup _moveButtonCG;
    private CanvasGroup _deleteButtonCG;

    // Delete Confirmation Popup
    private RTTPopupMenu _deleteConfirmPopup;

    // Clipboard Mode (for Copy/Move operations)
    private bool _isClipboardMode = false;
    private ClipboardOperation _clipboardOperation;
    private List<string> _clipboardItems = new List<string>();
    private string _clipboardSourcePath;

    private enum ClipboardOperation { None, Copy, Move }

    // Close button reference (for clipboard mode icon/function swap)
    private GameObject _closeButton;
    private Image _closeButtonIcon;
    private Sprite _originalCloseIcon;
    private Sprite _originalEditIcon;

    // Conflict Dialog
    private RTTPopupMenu _conflictPopup;

    // Scan Status Text (for category filter mode - shown in Row2 next to breadcrumbs)
    private TextMeshProUGUI _scanStatusText;
    private Coroutine _dotsAnimationCoroutine;
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

        // Load saved preferences
        LoadViewPreferences();

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

    private void LoadViewPreferences()
    {
        _currentSortBy = PlayerPrefs.GetString(PREF_SORT_BY, "Name");
        _isAscending = PlayerPrefs.GetInt(PREF_SORT_ASCENDING, 1) == 1;
        _isGridView = PlayerPrefs.GetInt(PREF_IS_GRID_VIEW, 1) == 1;
        Debug.Log($"[RTTFileManager] Loaded preferences: sortBy={_currentSortBy}, ascending={_isAscending}, gridView={_isGridView}");
    }

    private void SaveViewPreferences()
    {
        PlayerPrefs.SetString(PREF_SORT_BY, _currentSortBy);
        PlayerPrefs.SetInt(PREF_SORT_ASCENDING, _isAscending ? 1 : 0);
        PlayerPrefs.SetInt(PREF_IS_GRID_VIEW, _isGridView ? 1 : 0);
        PlayerPrefs.Save();
    }
    
    public void Cleanup()
    {
        // Reset state for potential recreation
        _viewReady = false;

        // Unregister side panels from ZoomController before destroying
        var zoomController = VirtualObjectsZoomController.Instance;
        if (zoomController != null)
        {
            if (_leftFrame != null) zoomController.UnregisterSidePanel(_leftFrame);
            if (_rightFrame != null) zoomController.UnregisterSidePanel(_rightFrame);
        }

        // Destroy side panels when main app is closed
        if (_leftFrame != null) Destroy(_leftFrame.gameObject);
        if (_rightFrame != null) Destroy(_rightFrame.gameObject);
        if (_pagination != null) Destroy(_pagination.gameObject);

        // Destroy world-space popups (not parented to this object)
        if (_createFolderPopup != null) Destroy(_createFolderPopup.gameObject);
        _createFolderPopup = null;

        if (_viewOptionsPopup != null) Destroy(_viewOptionsPopup.gameObject);
        _viewOptionsPopup = null;

        if (_deleteConfirmPopup != null) Destroy(_deleteConfirmPopup.gameObject);
        _deleteConfirmPopup = null;
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

        // Hide world-space popup
        if (_viewOptionsPopup != null) _viewOptionsPopup.Hide();
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
        // Get RTTToolbar to parent pagination into
        RTTToolbar toolbar = RTTToolbar.Instance;
        if (toolbar == null)
        {
            Debug.LogWarning("[RTTFileManager] RTTToolbar not found, pagination positioning may be incorrect");
            // Fallback: create toolbar
            toolbar = RTTToolbar.Create();
        }

        GameObject pagObj = new GameObject("FilePagination");

        // Parent into RTTToolbar
        pagObj.transform.SetParent(toolbar.transform, false);

        // Set local position (above taskbar area)
        pagObj.transform.localPosition = toolbar.GetPaginationLocalPosition();
        pagObj.transform.localRotation = Quaternion.identity;

        _pagination = pagObj.AddComponent<RTTFilePagination>();
        _pagination.Initialize(_controller);
    }
    
    // Header References
    private RectTransform _headerRT;
    private RectTransform _bodyRT;
    private float _singleRowHeight;
    private float _rowSpacing; // Spacing between header rows
    private float _headerHeight2Rows; // Height with 2 rows

    
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
        
        // Logic kích thước mới: Header gốc (2 rows) = 19.8% panel (+10% from 18%).
        // Mỗi row = 9.9%. Row 3 sẽ thêm 9.9% khi hiện.
        float headerBaseHeight = panelHeight * 0.198f; // +10% (was 0.18f)
        _singleRowHeight = headerBaseHeight / 2f;

        // Ensure minimum row height to fit 75f buttons (+10% from 68f)
        if (_singleRowHeight < 88f) // +10% (was 80f)
        {
            _singleRowHeight = 88f;
        }

        // Row spacing = ~19% of row height (reduced by 25%)
        _rowSpacing = _singleRowHeight * 0.1875f;

        // Header height = 2 rows + 1 gap between them
        _headerHeight2Rows = (_singleRowHeight * 2f) + _rowSpacing;

        float bottomPadding = panelHeight * 0.02f;

        // 1. Create Body Object (Grid Container)
        GameObject bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(_menuFrame.ContentContainer, false);
        _bodyRT = bodyObj.AddComponent<RectTransform>();
        _bodyRT.anchorMin = Vector2.zero;
        _bodyRT.anchorMax = Vector2.one;
        _bodyRT.offsetMax = new Vector2(0, -_headerHeight2Rows); // Top offset
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
        _headerRT.sizeDelta = new Vector2(0, _headerHeight2Rows);

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
        float bodyHeight = panelHeight - _headerHeight2Rows - bottomPadding;
        _fileGrid.Initialize(_controller, contentSize.x, bodyHeight, _font);

        // Set interaction callbacks
        _fileGrid.SetItemCallbacks(
            onHoverEnter: (path) => _controller?.HoverFile(path),
            onHoverExit: (path) => _controller?.UnhoverFile(path),
            onClick: (path, isFolder) => OnItemClicked(path, isFolder)
        );
        _fileGrid.SetSelectionChangedCallback(OnSelectionChanged);

        // Render Order
        headerObj.transform.SetAsLastSibling();
        bodyObj.transform.SetAsFirstSibling();

        // Apply saved view mode preference
        if (!_isGridView)
        {
            // User prefers list view - create it and hide grid
            _fileGrid.gameObject.SetActive(false);
            CreateListView();
        }

        // Mark setup complete - enables OnEnable/OnDisable to manage visibility for app switching
        // NOTE: Do NOT show pagination here - it will be shown by OnEnable() when the frame is actually displayed
        // During preparation, the frame is at +1000 units but pagination is at normal position (near taskbar)
        _viewReady = true;

        Debug.Log($"[RTTFileManager] Before OnViewReady - breadcrumb container null: {_breadcrumbContainer == null}, instance: {GetInstanceID()}");

        // Apply saved sort options to controller before loading data (without triggering refresh)
        _controller?.SetSortOptionsNoRefresh(_currentSortBy, _isAscending);

        _controller.OnViewReady();

        // Initialize page size AFTER OnViewReady has loaded data (deferred to next frame for safety)
        StartCoroutine(InitializePageSizeDeferred());
    }

    private IEnumerator InitializePageSizeDeferred()
    {
        // Wait one frame to ensure grid has populated
        yield return null;

        // Initialize page size based on view mode (use actual visible rows)
        int itemsPerPage;
        if (_isGridView)
        {
            if (_fileGrid != null)
            {
                int columnsPerRow = _fileGrid.GetColumnsPerRow();
                int visibleRows = _fileGrid.GetVisibleRowsForPagination();
                itemsPerPage = _fileGrid.GetItemsPerPage(visibleRows);
                Debug.Log($"[RTTFileManager] InitializePageSizeDeferred(Grid): columnsPerRow={columnsPerRow}, visibleRows={visibleRows}, itemsPerPage={itemsPerPage}");
            }
            else
            {
                // Fallback: 2 rows x 5 columns (typical layout)
                itemsPerPage = 10;
                Debug.LogWarning($"[RTTFileManager] InitializePageSizeDeferred(Grid): fileGrid is null, using fallback itemsPerPage={itemsPerPage}");
            }
        }
        else
        {
            // List view: use actual visible rows
            if (_fileList != null)
            {
                itemsPerPage = _fileList.GetVisibleRowsForPagination();
                Debug.Log($"[RTTFileManager] InitializePageSizeDeferred(List): visibleRows={itemsPerPage}");
            }
            else
            {
                itemsPerPage = 6;
                Debug.LogWarning($"[RTTFileManager] InitializePageSizeDeferred(List): fileList is null, using fallback itemsPerPage={itemsPerPage}");
            }
        }
        _controller?.SetPageSize(itemsPerPage);
    }

    private void CreateHeaderRows(RectTransform parent)
    {
        Debug.Log($"[RTTFileManager] CreateHeaderRows started, instance: {GetInstanceID()}");

        // Row 1: Sort | Search | Edit
        CreateRow1(parent);

        // Row 2: Breadcrumbs (or Edit Controls in Edit Mode) | Item Count | Refresh
        CreateRow2(parent);

        Debug.Log($"[RTTFileManager] CreateHeaderRows completed, breadcrumb null: {_breadcrumbContainer == null}");
    }

    // View Options Popup References
    private RTTPopupMenu _viewOptionsPopup;
    private string _currentSortBy = "Name";
    private bool _isAscending = true;
    private bool _isGridView = true;
    private Image _sortArrowImg; // Reference to arrow icon
    private TextMeshProUGUI _sortTriggerText; // Reference to sort trigger text
    private TextMeshProUGUI _itemCountText; // Reference to item count label

    // PlayerPrefs keys for saving view options
    private const string PREF_SORT_BY = "FileManager_SortBy";
    private const string PREF_SORT_ASCENDING = "FileManager_SortAscending";
    private const string PREF_IS_GRID_VIEW = "FileManager_IsGridView";

    // Ellipsis Popup References (for hidden breadcrumb folders)
    private GameObject _ellipsisPopup;
    private GameObject _ellipsisButton;
    private List<(string name, string fullPath)> _hiddenFolders = new List<(string name, string fullPath)>();
    private float _ellipsisPopupWidth; // Calculated width for popup

    private float _sortTriggerWidth = 220f; // Increased 10% (was 200f)
    
    private void CreateRow1(RectTransform parent)
    {
        RectTransform rowRT = CreateRowContainer(parent, "Row1", 0);

        // Left: Close Button (BareIconButton - no background/border)
        _originalCloseIcon = Resources.Load<Sprite>("icon_close");
        _closeButton = VRButtonFactory.CreateBareIconButton(
            rowRT,
            75f,
            _originalCloseIcon,
            _primaryColor,
            OnCloseButtonClicked
        );
        // Store icon reference for clipboard mode swap (path: HitArea/Visuals/Content/Icon)
        _closeButtonIcon = _closeButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
        RectTransform closeRT = _closeButton.GetComponent<RectTransform>();
        SetupRowElement(closeRT, new Vector2(0, 0.5f), new Vector2(20, 0)); // Left align

        // View Options Trigger Button (Custom Layout: Text Left, Icon Right)
        Sprite arrowIcon = VRDropdownFactory.GetArrowSprite();

        var sortConfig = new VRButtonFactory.ButtonConfig
        {
            label = _currentSortBy,
            themeColor = _primaryColor,
            width = _sortTriggerWidth,
            height = 75f,  // +10% (was 68f)
            fontSize = 26, // +10% (was 24)
            font = _font,
            textOnly = true,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject sortTrigger = VRButtonFactory.CreateButton(rowRT, sortConfig, ToggleViewOptionsPopup);

        // Adjust Text Alignment to Center and save reference
        _sortTriggerText = sortTrigger.GetComponentInChildren<TextMeshProUGUI>();
        if (_sortTriggerText != null)
        {
            _sortTriggerText.alignment = TextAlignmentOptions.Center;
            _sortTriggerText.margin = Vector4.zero;
        }

        // Add Arrow Icon manually (Right aligned)
        GameObject iconObj = new GameObject("ArrowIcon");
        iconObj.transform.SetParent(sortTrigger.transform, false);
        _sortArrowImg = iconObj.AddComponent<Image>();
        _sortArrowImg.sprite = arrowIcon;
        _sortArrowImg.color = Color.white;
        _sortArrowImg.raycastTarget = false;

        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(1, 0.5f);
        iconRT.anchorMax = new Vector2(1, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(18f, 18f); // +10% (was 16f)
        iconRT.anchoredPosition = new Vector2(-36f, 0); // +10% (was -33f)

        RectTransform sortRT = sortTrigger.GetComponent<RectTransform>();
        // Position centered between Close button and SearchBar
        // Close button ends at: 20 + 68 = 88 from left
        // SearchBar (width 990, centered) left edge from left: containerWidth/2 - 495
        // Gap center from left = (88 + containerWidth/2 - 495) / 2 = containerWidth/4 - 203.5
        // Convert to center-anchored: gapCenter - containerWidth/2 = -containerWidth/4 - 203.5
        float gapCenterFromCenter = -_containerWidth / 4f - 203.5f;
        sortRT.anchorMin = new Vector2(0.5f, 0.5f);
        sortRT.anchorMax = new Vector2(0.5f, 0.5f);
        sortRT.pivot = new Vector2(0.5f, 0.5f);
        sortRT.anchoredPosition = new Vector2(gapCenterFromCenter, 0);

        // Create popup (using RTTPopupMenu) with Sort Button as parent
        CreateViewOptionsPopup(sortRT);

        // Right: Edit Button (Icon) - toggles edit mode
        _originalEditIcon = Resources.Load<Sprite>("icon_edit");
        var editConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Edit",
            icon = _originalEditIcon,
            themeColor = _primaryColor,
            width = 75f,  // +10% (was 68f)
            height = 75f, // +10% (was 68f)
            iconOnly = true, // Hide text
            iconSize = 39f, // +10% (was 35.2f)
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        _editButton = VRButtonFactory.CreateButton(
            rowRT,
            editConfig,
            OnEditButtonClicked
        );
        RectTransform editRT = _editButton.GetComponent<RectTransform>();
        SetupRowElement(editRT, new Vector2(1, 0.5f), new Vector2(-20, 0)); // Right align

        // New Folder Button (symmetric to sortTrigger on right side of search bar)
        Sprite folderIcon = Resources.Load<Sprite>("icon_create_folder");
        var newFolderConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Folder",
            icon = folderIcon,
            themeColor = _primaryColor,
            width = _sortTriggerWidth,
            height = 75f,  // +10% (was 68f)
            fontSize = 26, // +10% (was 24)
            font = _font,
            iconSize = 39f, // +10% (was 35.2f)
            horizontalLayout = true,  // Icon on left, text on right
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject newFolderBtn = VRButtonFactory.CreateButton(
            rowRT,
            newFolderConfig,
            ShowCreateFolderPopup
        );
        RectTransform newFolderRT = newFolderBtn.GetComponent<RectTransform>();
        // Position symmetric to sortTrigger (positive X instead of negative)
        float newFolderCenterFromCenter = _containerWidth / 4f + 203.5f;
        newFolderRT.anchorMin = new Vector2(0.5f, 0.5f);
        newFolderRT.anchorMax = new Vector2(0.5f, 0.5f);
        newFolderRT.pivot = new Vector2(0.5f, 0.5f);
        newFolderRT.anchoredPosition = new Vector2(newFolderCenterFromCenter, 0);

        // Store button reference for enabling/disabling based on folder creation permission
        // Note: VRButtonFactory creates Button on HitArea child, not on wrapper
        _newFolderButton = newFolderBtn.GetComponentInChildren<Button>();
        _newFolderCanvasGroup = newFolderBtn.AddComponent<CanvasGroup>();

        Debug.Log($"[RTTFileManager] New Folder button created, _newFolderButton null: {_newFolderButton == null}");

        // Update button state immediately based on current path permission
        UpdateNewFolderButtonState();

        // Center: Search Bar
        CreateSearchBar(rowRT);
    }
    
    private void ToggleViewOptionsPopup()
    {
        Debug.Log($"[RTTFileManager] ToggleViewOptionsPopup called, popup null: {_viewOptionsPopup == null}");
        if (_viewOptionsPopup == null) return;

        Debug.Log($"[RTTFileManager] Popup IsVisible: {_viewOptionsPopup.IsVisible}");

        if (_viewOptionsPopup.IsVisible)
        {
            _viewOptionsPopup.Hide();
        }
        else
        {
            // Rebuild content to reflect current state (in case changed by list header click)
            BuildViewOptionsPopupContent();
            _viewOptionsPopup.Show();
        }
        UpdateSortArrow();
    }

    private void OnViewOptionsPopupHide()
    {
        UpdateSortArrow();
    }

    private void UpdateSortArrow()
    {
        // Rotate arrow: up (180) when popup open, down (0) when closed
        if (_sortArrowImg != null)
        {
            float targetZ = (_viewOptionsPopup != null && _viewOptionsPopup.IsVisible) ? 180f : 0f;
            _sortArrowImg.rectTransform.localEulerAngles = new Vector3(0, 0, targetZ);
        }
    }
    
    private void CreateViewOptionsPopup(Transform parent)
    {
        Debug.Log($"[RTTFileManager] CreateViewOptionsPopup, _menuFrame null: {_menuFrame == null}");

        // Create popup config
        var config = new RTTPopupMenu.PopupConfig
        {
            width = _sortTriggerWidth * 2.5f, // Width = 2.5x Trigger (increased 25%)
            buttonHeight = 75f, // +10% (was 68f)
            sideSpacing = 11f,  // +10% (was 10f)
            rowSpacing = 11f,   // +10% (was 10f)
            fontSize = 20,      // +10% (was 18)
            iconSize = 26f,     // +10% (was 24f)
            primaryColor = _primaryColor,
            accentColor = _accentColor,
            overlayColor = new Color(0f, 0f, 0f, 0.4f), // Dark overlay
            font = _font,
            layerName = "VirtualObjects"
        };

        // Create world-space popup (like RTTPopupInputable)
        // Pass _menuFrame.transform as reference for positioning
        _viewOptionsPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);

        Debug.Log($"[RTTFileManager] ViewOptionsPopup created, null: {_viewOptionsPopup == null}");

        // Subscribe to OnHide to reset arrow when popup closes from click-outside
        _viewOptionsPopup.OnHide += OnViewOptionsPopupHide;

        // Build popup content
        BuildViewOptionsPopupContent();

        // Set position offset (relative to menu frame center, in logical pixels)
        // Position below the header row, left-aligned with sortTrigger button
        float popupWidth = config.width;

        // X: popup left edge aligned with sortTrigger button's left edge
        // sortTrigger center X = -containerWidth/4 - 203.5 (from CreateRow1)
        // sortTrigger width = _sortTriggerWidth = 220
        // sortTrigger left edge = center - width/2 = -containerWidth/4 - 203.5 - 110 = -containerWidth/4 - 313.5
        // Popup center X = sortTrigger left + popupWidth/2
        // Additional adjustment to align better visually
        float sortTriggerCenterX = -_containerWidth / 4f - 203.5f;
        float sortTriggerLeftX = sortTriggerCenterX - (_sortTriggerWidth / 2f);
        float additionalOffset = 130f;
        float offsetX = sortTriggerLeftX + (popupWidth / 2f) + additionalOffset;

        // Y: popup top below header area
        // Header takes approximately 200 logical pixels (Row1 + Row2 + spacing)
        // Frame center is at 0, top is at +containerHeight/2
        // Popup top should be at containerHeight/2 - headerHeight
        float headerHeight = 200f; // Row1 + Row2 + margins
        float estimatedPopupHeight = 450f; // DISPLAY AS + SORT BY + Ascending button
        // Popup center Y = popup top - popupHeight/2
        float offsetY = (_containerHeight / 2f) - headerHeight - (estimatedPopupHeight / 2f);

        _viewOptionsPopup.SetPositionOffset(new Vector2(offsetX, offsetY));

        Debug.Log($"[RTTFileManager] ViewOptionsPopup position offset: ({offsetX}, {offsetY})");
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
        if (_isGridView == isGrid) return; // No change

        _isGridView = isGrid;
        Debug.Log($"[RTTFileManager] Display mode: {(isGrid ? "Grid" : "List")}");

        // Close popup after selection
        _viewOptionsPopup?.Hide();

        // Switch views
        if (isGrid)
        {
            // Show Grid, Hide List
            if (_fileGrid != null) _fileGrid.gameObject.SetActive(true);
            if (_fileList != null) _fileList.gameObject.SetActive(false);
        }
        else
        {
            // Show List, Hide Grid
            if (_fileGrid != null) _fileGrid.gameObject.SetActive(false);

            // Create list view if not exists
            if (_fileList == null)
            {
                CreateListView();
            }
            else
            {
                _fileList.gameObject.SetActive(true);
            }
        }

        // Update page size based on view mode (use actual visible rows)
        // Grid: visible rows × actual columns (calculated based on viewport)
        // List: visible rows = items per page
        int itemsPerPage;
        if (isGrid)
        {
            if (_fileGrid != null)
            {
                int columnsPerRow = _fileGrid.GetColumnsPerRow();
                int visibleRows = _fileGrid.GetVisibleRowsForPagination();
                itemsPerPage = _fileGrid.GetItemsPerPage(visibleRows);
                Debug.Log($"[RTTFileManager] SetDisplayMode(Grid): columnsPerRow={columnsPerRow}, visibleRows={visibleRows}, itemsPerPage={itemsPerPage}");
            }
            else
            {
                // Fallback for grid: 2 rows x 5 columns (typical layout)
                itemsPerPage = 10;
                Debug.LogWarning("[RTTFileManager] FileGrid is null, using default 10 items per page");
            }
        }
        else
        {
            // List view: use actual visible rows
            if (_fileList != null)
            {
                itemsPerPage = _fileList.GetVisibleRowsForPagination();
                Debug.Log($"[RTTFileManager] SetDisplayMode(List): visibleRows={itemsPerPage}");
            }
            else
            {
                itemsPerPage = 6;
                Debug.LogWarning("[RTTFileManager] FileList is null, using default 6 items per page");
            }
        }
        _controller?.SetPageSize(itemsPerPage);

        // Refresh content with current data (keep page position)
        _controller?.RefreshCurrentFolderKeepPage();

        // Save preference
        SaveViewPreferences();
    }

    private void SetSortBy(string sortBy)
    {
        _currentSortBy = sortBy;
        Debug.Log($"[RTTFileManager] Sort by: {sortBy}");

        // Update sort trigger text
        if (_sortTriggerText != null)
        {
            _sortTriggerText.text = sortBy;
        }

        // Update list view sort state if visible
        if (!_isGridView && _fileList != null)
        {
            _fileList.UpdateSortState(_currentSortBy, _isAscending);
        }

        // Close popup after selection
        _viewOptionsPopup?.Hide();

        // Call controller to apply sort
        _controller?.SetSortOptions(_currentSortBy, _isAscending);

        // Save preference
        SaveViewPreferences();
    }

    private void ToggleSortOrder()
    {
        _isAscending = !_isAscending;
        Debug.Log($"[RTTFileManager] Order: {(_isAscending ? "Ascending" : "Descending")}");

        // Update list view sort state if visible
        if (!_isGridView && _fileList != null)
        {
            _fileList.UpdateSortState(_currentSortBy, _isAscending);
        }

        // Close popup after toggle
        _viewOptionsPopup?.Hide();

        // Call controller to apply sort
        _controller?.SetSortOptions(_currentSortBy, _isAscending);

        // Save preference
        SaveViewPreferences();
    }

    private void RefreshViewOptionsPopup()
    {
        if (_viewOptionsPopup != null)
        {
            BuildViewOptionsPopupContent();
            _viewOptionsPopup.Show();
        }
    }

    private void CreateListView()
    {
        if (_bodyRT == null)
        {
            Debug.LogError("[RTTFileManager] Cannot create list view - body container not ready!");
            return;
        }

        // Create list view in the same body container as grid
        GameObject listObj = new GameObject("FileList");
        listObj.transform.SetParent(_bodyRT, false);

        RectTransform listRT = listObj.AddComponent<RectTransform>();
        listRT.anchorMin = Vector2.zero;
        listRT.anchorMax = Vector2.one;
        listRT.offsetMin = Vector2.zero;
        listRT.offsetMax = Vector2.zero;

        _fileList = listObj.AddComponent<RTTFileList>();

        // Calculate size based on body
        Vector2 contentSize = _menuFrame.GetContentSize();
        float _headerHeight2Rows = (_singleRowHeight * 2f) + _rowSpacing; // 2 rows + spacing
        float bottomPadding = contentSize.y * 0.02f;
        float bodyHeight = contentSize.y - _headerHeight2Rows - bottomPadding;

        _fileList.Initialize(_controller, contentSize.x, bodyHeight, _font, _primaryColor, _accentColor, OnListHeaderColumnClicked);
        _fileList.UpdateSortState(_currentSortBy, _isAscending);

        // Set interaction callbacks
        _fileList.SetItemCallbacks(
            onHoverEnter: (path) => _controller?.HoverFile(path),
            onHoverExit: (path) => _controller?.UnhoverFile(path),
            onClick: (path, isFolder) => OnItemClicked(path, isFolder)
        );
        _fileList.SetSelectionChangedCallback(OnSelectionChanged);

        Debug.Log("[RTTFileManager] List view created");
    }

    private void OnListHeaderColumnClicked(string columnName)
    {
        // Toggle ascending/descending if clicking same column
        if (_currentSortBy == columnName)
        {
            _isAscending = !_isAscending;
        }
        else
        {
            _currentSortBy = columnName;
            _isAscending = true;
        }

        // Update sort trigger text
        if (_sortTriggerText != null)
        {
            _sortTriggerText.text = _currentSortBy;
        }

        // Update list header arrows
        if (_fileList != null)
        {
            _fileList.UpdateSortState(_currentSortBy, _isAscending);
        }

        // Apply sort
        _controller?.SetSortOptions(_currentSortBy, _isAscending);
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void CreateRow2(RectTransform parent)
    {
        Debug.Log("[RTTFileManager] CreateRow2 called");
        // Position Row2 below Row1 with spacing
        float row2Y = -(_singleRowHeight + _rowSpacing);
        RectTransform rowRT = CreateRowContainer(parent, "Row2", row2Y);

        // Right: Refresh Button (Icon)
        Sprite refreshIcon = Resources.Load<Sprite>("icon_refresh");
        var refreshConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Refresh",
            icon = refreshIcon,
            themeColor = _accentColor,
            width = 75f,  // +10% (was 68f)
            height = 75f, // +10% (was 68f)
            iconOnly = true, // Hide text
            iconSize = 39f, // +10% (was 35.2f)
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject refreshBtn = VRButtonFactory.CreateButton(
            rowRT,
            refreshConfig,
            () => {
                // Clear thumbnail cache to regenerate with current quality settings
                FileThumbnailService.Instance?.ClearAllCache();
                _controller?.RefreshCurrentFolder();
            }
        );
        RectTransform refreshRT = refreshBtn.GetComponent<RectTransform>();
        SetupRowElement(refreshRT, new Vector2(1, 0.5f), new Vector2(-20, 0));

        // Item Count Label (aligned with New Folder button in Row 1)
        // Create this first so we know its position for the selected count label
        GameObject countObj = new GameObject("ItemCountLabel");
        countObj.transform.SetParent(rowRT, false);

        _itemCountText = countObj.AddComponent<TextMeshProUGUI>();
        _itemCountText.text = "0 items";
        _itemCountText.font = _font;
        _itemCountText.fontSize = 31; // +10% (was 28)
        _itemCountText.fontStyle = FontStyles.Bold;
        _itemCountText.color = Color.white;
        _itemCountText.alignment = TextAlignmentOptions.Center;
        _itemCountText.raycastTarget = false;

        RectTransform countRT = countObj.GetComponent<RectTransform>();
        countRT.sizeDelta = new Vector2(_sortTriggerWidth, 75f); // +10% (was 68f)
        // Position symmetric to sortTrigger (same as New Folder button in Row 1)
        float countCenterFromCenter = _containerWidth / 4f + 203.5f;
        countRT.anchorMin = new Vector2(0.5f, 0.5f);
        countRT.anchorMax = new Vector2(0.5f, 0.5f);
        countRT.pivot = new Vector2(0.5f, 0.5f);
        countRT.anchoredPosition = new Vector2(countCenterFromCenter, 0);

        // Selected Count Label (right edge aligned with SearchBar right edge, shown in Edit Mode)
        // SearchBar is centered with width 990, so right edge is at +495 from center
        float searchBarRightEdge = 990f / 2f; // +495

        GameObject selectedObj = new GameObject("SelectedCountLabel");
        selectedObj.transform.SetParent(rowRT, false);

        _selectedCountText = selectedObj.AddComponent<TextMeshProUGUI>();
        _selectedCountText.text = "";
        _selectedCountText.font = _font;
        _selectedCountText.fontSize = 28;
        _selectedCountText.fontStyle = FontStyles.Bold;
        _selectedCountText.color = Color.white; // White color as requested
        _selectedCountText.alignment = TextAlignmentOptions.MidlineRight;
        _selectedCountText.raycastTarget = false;

        RectTransform selectedRT = selectedObj.GetComponent<RectTransform>();
        selectedRT.sizeDelta = new Vector2(280f, 75f); // Wide enough for "9999 items selected" on 1 line
        // Right edge aligned with SearchBar right edge
        selectedRT.anchorMin = new Vector2(0.5f, 0.5f);
        selectedRT.anchorMax = new Vector2(0.5f, 0.5f);
        selectedRT.pivot = new Vector2(1, 0.5f); // Right-aligned pivot
        selectedRT.anchoredPosition = new Vector2(searchBarRightEdge, 0);
        selectedObj.SetActive(false); // Hidden by default

        // Left Container for Breadcrumbs
        GameObject crumbContainer = new GameObject("Breadcrumbs");
        crumbContainer.transform.SetParent(rowRT, false);
        _breadcrumbContainer = crumbContainer.transform;
        Debug.Log($"[RTTFileManager] Breadcrumb container set: {_breadcrumbContainer != null}, instance: {GetInstanceID()}");

        RectTransform crumbRT = crumbContainer.AddComponent<RectTransform>();
        crumbRT.anchorMin = new Vector2(0, 0);
        crumbRT.anchorMax = new Vector2(1, 1);
        crumbRT.pivot = new Vector2(0, 0.5f);
        // Left offset 20, Right offset to leave room for scan status + item count + Refresh button
        crumbRT.offsetMin = new Vector2(20, 0);
        crumbRT.offsetMax = new Vector2(-520, 0); // More room for scan status

        // Scan Status Text (shown during category scan, positioned between breadcrumbs and item count)
        GameObject scanStatusObj = new GameObject("ScanStatusText");
        scanStatusObj.transform.SetParent(rowRT, false);

        _scanStatusText = scanStatusObj.AddComponent<TextMeshProUGUI>();
        _scanStatusText.text = "";
        _scanStatusText.font = _font;
        _scanStatusText.fontSize = 26;
        _scanStatusText.fontStyle = FontStyles.Italic;
        _scanStatusText.color = new Color(1f, 1f, 1f, 0.7f); // Semi-transparent
        _scanStatusText.alignment = TextAlignmentOptions.MidlineRight;
        _scanStatusText.raycastTarget = false;

        RectTransform scanStatusRT = scanStatusObj.GetComponent<RectTransform>();
        scanStatusRT.sizeDelta = new Vector2(200f, 75f);
        // Position to the left of item count
        float scanStatusX = countCenterFromCenter - _sortTriggerWidth / 2f - 110f;
        scanStatusRT.anchorMin = new Vector2(0.5f, 0.5f);
        scanStatusRT.anchorMax = new Vector2(0.5f, 0.5f);
        scanStatusRT.pivot = new Vector2(1, 0.5f); // Right-aligned
        scanStatusRT.anchoredPosition = new Vector2(scanStatusX, 0);

        // Edit Controls Container (hidden by default, shown in Edit Mode)
        GameObject editControlsObj = new GameObject("EditControls");
        editControlsObj.transform.SetParent(rowRT, false);
        _editControlsContainer = editControlsObj;

        RectTransform editRT = editControlsObj.AddComponent<RectTransform>();
        editRT.anchorMin = new Vector2(0, 0);
        editRT.anchorMax = new Vector2(1, 1);
        editRT.pivot = new Vector2(0, 0.5f);
        editRT.offsetMin = new Vector2(20, 0);
        editRT.offsetMax = new Vector2(-320, 0);

        Debug.Log($"[RTTFileManager] EditControls container created - parent: {rowRT.name}, sizeDelta: {editRT.sizeDelta}, rect: {editRT.rect}, instanceID: {editControlsObj.GetInstanceID()}");

        // Create Edit Controls inside the container BEFORE setting inactive
        // This ensures components initialize correctly
        CreateEditControlsInRow2(editRT);

        Debug.Log($"[RTTFileManager] EditControls child count after creation: {editControlsObj.transform.childCount}");
        Debug.Log($"[RTTFileManager] _editControlsContainer reference instanceID: {_editControlsContainer.GetInstanceID()}, same object: {_editControlsContainer == editControlsObj}");

        // Hide after creating children
        editControlsObj.SetActive(false);
        Debug.Log($"[RTTFileManager] EditControls set to inactive, activeSelf: {editControlsObj.activeSelf}");

        // NOTE: NOT using HorizontalLayoutGroup to allow independent control of:
        // - Visual position (left to right)
        // - Sibling order (reversed, so left buttons have higher index = hit first by GraphicRaycaster)
    }

    private void CreateEditControlsInRow2(RectTransform parent)
    {
        Debug.Log($"[RTTFileManager] CreateEditControlsInRow2 called, parent: {parent?.name}, parent active: {parent?.gameObject.activeInHierarchy}");

        // Select All Checkbox (at left edge)
        CreateSelectAllCheckbox(parent);

        // SearchBar dimensions for alignment (SearchBar is centered on row, width = 990)
        // EditControls container has offsetMin=(20,0), offsetMax=(-320,0)
        // Container center offset from row center = (20 + (-320)) / 2 = -150
        // So we need to compensate: searchBarLeft_in_container = searchBarLeft_from_row_center - container_offset
        float searchBarWidth = 990f;
        float searchBarLeftFromRowCenter = -searchBarWidth / 2f; // -495
        float containerCenterOffset = (20f + (-320f)) / 2f; // -150 (container center is 150px left of row center)
        float searchBarLeftInContainer = searchBarLeftFromRowCenter - containerCenterOffset; // -495 - (-150) = -345
        float btnSpacing = 15f;

        // Copy button - align with SearchBar left edge (use center anchor)
        var copyBtn = CreateEditModeActionButton(parent, "Copy", "icon_copy", OnCopyClicked);
        Debug.Log($"[RTTFileManager] Copy button created: {copyBtn?.name}");
        var copyRT = copyBtn.GetComponent<RectTransform>();
        copyRT.anchorMin = copyRT.anchorMax = new Vector2(0.5f, 0.5f);
        copyRT.pivot = new Vector2(0, 0.5f);
        copyRT.anchoredPosition = new Vector2(searchBarLeftInContainer, 0);
        _copyButton = copyBtn.GetComponent<Button>();
        _copyButtonCG = copyBtn.AddComponent<CanvasGroup>();

        // Move button
        var moveBtn = CreateEditModeActionButton(parent, "Move", "icon_move_folder", OnMoveClicked);
        var moveRT = moveBtn.GetComponent<RectTransform>();
        moveRT.anchorMin = moveRT.anchorMax = new Vector2(0.5f, 0.5f);
        moveRT.pivot = new Vector2(0, 0.5f);
        float moveX = searchBarLeftInContainer + copyRT.sizeDelta.x + btnSpacing;
        moveRT.anchoredPosition = new Vector2(moveX, 0);
        _moveButton = moveBtn.GetComponent<Button>();
        _moveButtonCG = moveBtn.AddComponent<CanvasGroup>();

        // Delete button
        var deleteBtn = CreateEditModeActionButton(parent, "Delete", "icon_trash", OnDeleteClicked);
        var deleteRT = deleteBtn.GetComponent<RectTransform>();
        deleteRT.anchorMin = deleteRT.anchorMax = new Vector2(0.5f, 0.5f);
        deleteRT.pivot = new Vector2(0, 0.5f);
        float deleteX = moveX + moveRT.sizeDelta.x + btnSpacing;
        deleteRT.anchoredPosition = new Vector2(deleteX, 0);
        _deleteButton = deleteBtn.GetComponent<Button>();
        _deleteButtonCG = deleteBtn.AddComponent<CanvasGroup>();

        // Initially disable buttons (no selection)
        UpdateActionButtonsState();

        Debug.Log($"[RTTFileManager] CreateEditControlsInRow2 completed - Copy: {_copyButton != null} (size: {copyRT.sizeDelta}), Move: {_moveButton != null} (size: {moveRT.sizeDelta}), Delete: {_deleteButton != null} (size: {deleteRT.sizeDelta})");
    }

    private void CreateSelectAllCheckbox(RectTransform parent)
    {
        float checkboxSize = 50f;
        float labelWidth = 150f; // Increased to fit "Select all" on one line
        float spacing = 10f;
        float leftPadding = 20f;

        // Container for checkbox + label
        GameObject container = new GameObject("SelectAllContainer");
        container.transform.SetParent(parent, false);
        RectTransform containerRT = container.AddComponent<RectTransform>();
        containerRT.anchorMin = containerRT.anchorMax = new Vector2(0, 0.5f);
        containerRT.pivot = new Vector2(0, 0.5f);
        containerRT.sizeDelta = new Vector2(checkboxSize + spacing + labelWidth, checkboxSize);
        containerRT.anchoredPosition = new Vector2(leftPadding, 0);

        // Checkbox button (glass pill style)
        _selectAllCheckbox = new GameObject("Checkbox");
        _selectAllCheckbox.transform.SetParent(container.transform, false);
        RectTransform checkboxRT = _selectAllCheckbox.AddComponent<RectTransform>();
        checkboxRT.anchorMin = checkboxRT.anchorMax = new Vector2(0, 0.5f);
        checkboxRT.pivot = new Vector2(0, 0.5f);
        checkboxRT.sizeDelta = new Vector2(checkboxSize, checkboxSize);
        checkboxRT.anchoredPosition = Vector2.zero;

        // Checkbox background (glass style)
        Image checkboxBg = _selectAllCheckbox.AddComponent<Image>();
        checkboxBg.raycastTarget = true;
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_Aspect", 1f);
            mat.SetFloat("_CornerRadius", 0.25f);
            mat.SetFloat("_EdgePadding", 0.02f);
            mat.SetColor("_ColorA", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.3f));
            mat.SetColor("_ColorB", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.1f));
            mat.SetFloat("_GlassAlpha", 0.2f);
            mat.SetFloat("_FresnelStrength", 0.15f);
            checkboxBg.material = mat;
            checkboxBg.color = Color.white;
        }

        // Checkmark icon (hidden by default)
        GameObject checkmarkObj = new GameObject("Checkmark");
        checkmarkObj.transform.SetParent(_selectAllCheckbox.transform, false);
        RectTransform checkmarkRT = checkmarkObj.AddComponent<RectTransform>();
        checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkmarkRT.offsetMin = checkmarkRT.offsetMax = Vector2.zero;
        _selectAllCheckmark = checkmarkObj.AddComponent<Image>();
        _selectAllCheckmark.sprite = Resources.Load<Sprite>("icon_check_mark");
        _selectAllCheckmark.color = Color.white;
        _selectAllCheckmark.preserveAspect = true;
        _selectAllCheckmark.raycastTarget = false;
        checkmarkObj.SetActive(false);

        // Button component
        Button checkboxButton = _selectAllCheckbox.AddComponent<Button>();
        checkboxButton.transition = Selectable.Transition.None;
        checkboxButton.onClick.AddListener(OnSelectAllClicked);

        // Add scale hover effect to checkbox (no color change)
        var checkboxHoverController = _selectAllCheckbox.AddComponent<HoverEffectController>();
        checkboxHoverController.TargetVisuals = _selectAllCheckbox.transform;
        var checkboxScaleEffect = new ScaleHoverEffect()
            .WithHoverScale(1.1f)
            .WithTransitionDuration(0.1f);
        checkboxHoverController.AddEffect(checkboxScaleEffect);

        // Label "Select all"
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(container.transform, false);
        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = labelRT.anchorMax = new Vector2(0, 0.5f);
        labelRT.pivot = new Vector2(0, 0.5f);
        labelRT.sizeDelta = new Vector2(labelWidth, checkboxSize);
        labelRT.anchoredPosition = new Vector2(checkboxSize + spacing, 0);

        TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = "Select all";
        labelText.font = _font;
        labelText.fontSize = 28;
        labelText.color = Color.white;
        labelText.fontStyle = FontStyles.Bold;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;
    }

    private GameObject CreateEditModeActionButton(RectTransform parent, string label, string iconName, UnityEngine.Events.UnityAction onClick)
    {
        float btnHeight = 60f;
        float iconSize = 32f;
        float textWidth = label.Length * 18f; // Approximate text width
        float padding = 25f;
        float spacing = 10f;
        float btnWidth = padding + iconSize + spacing + textWidth + padding;

        GameObject btnObj = new GameObject($"Btn_{label}");
        btnObj.transform.SetParent(parent, false);
        RectTransform btnRT = btnObj.AddComponent<RectTransform>();
        btnRT.sizeDelta = new Vector2(btnWidth, btnHeight);

        // Background (glass pill style - rounded on both sides, no border)
        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.raycastTarget = true;
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            float aspect = btnWidth / btnHeight;
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_CornerRadius", 0.48f); // Fully rounded ends
            mat.SetFloat("_EdgePadding", 0.02f);
            mat.SetColor("_ColorA", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.3f));
            mat.SetColor("_ColorB", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.1f));
            mat.SetFloat("_GlassAlpha", 0.15f);
            mat.SetFloat("_FresnelStrength", 0.15f);
            bgImage.material = mat;
            bgImage.color = Color.white;
        }
        else
        {
            Debug.LogWarning($"[RTTFileManager] CreateEditModeActionButton: Shader 'Custom/GlassGradientBackgroundWide' not found for {label}");
            // Fallback: use white background
            bgImage.color = new Color(1f, 1f, 1f, 0.2f);
        }

        // Icon (white color)
        Sprite icon = Resources.Load<Sprite>(iconName);
        Debug.Log($"[RTTFileManager] CreateEditModeActionButton: {label}, icon '{iconName}' loaded: {icon != null}");
        Image iconImg = null;
        if (icon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(btnObj.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);
            iconRT.anchoredPosition = new Vector2(padding, 0);

            iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.color = Color.white; // White icon
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
        }

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = textRT.anchorMax = new Vector2(0, 0.5f);
        textRT.pivot = new Vector2(0, 0.5f);
        textRT.sizeDelta = new Vector2(textWidth, btnHeight);
        textRT.anchoredPosition = new Vector2(padding + iconSize + spacing, 0);

        TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.font = _font;
        txt.fontSize = 26;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;

        // Button component
        Button btn = btnObj.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);

        // Add hover effects (Scale + Background Color)
        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.TargetVisuals = btnObj.transform; // Set before adding effects

        // Scale effect
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(1.05f)
            .WithTransitionDuration(0.1f);
        hoverController.AddEffect(scaleEffect);

        // Background color change on hover (keep alpha, change RGB to accent)
        Material btnMaterial = bgImage.material;
        Color normalColorA = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.3f);
        Color normalColorB = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.1f);
        Color hoverColorA = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.3f);
        Color hoverColorB = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.1f);

        hoverController.OnHoverStateChanged += (isHovered) =>
        {
            if (btnMaterial != null)
            {
                btnMaterial.SetColor("_ColorA", isHovered ? hoverColorA : normalColorA);
                btnMaterial.SetColor("_ColorB", isHovered ? hoverColorB : normalColorB);
            }
        };

        return btnObj;
    }

    #region Edit Mode
    private void ToggleEditMode()
    {
        Debug.Log($"[RTTFileManager] ToggleEditMode CALLED - _isEditMode before: {_isEditMode}");

        _isEditMode = !_isEditMode;

        Debug.Log($"[RTTFileManager] ToggleEditMode - _isEditMode after toggle: {_isEditMode}");
        Debug.Log($"[RTTFileManager] ToggleEditMode - _editControlsContainer: {(_editControlsContainer != null ? $"{_editControlsContainer.name} (ID:{_editControlsContainer.GetInstanceID()})" : "NULL")}, _breadcrumbContainer: {(_breadcrumbContainer != null ? _breadcrumbContainer.name : "NULL")}");

        UpdateEditButtonVisual();

        // Toggle between Breadcrumbs and Edit Controls in Row2
        if (_breadcrumbContainer != null)
            _breadcrumbContainer.gameObject.SetActive(!_isEditMode);
        if (_editControlsContainer != null)
        {
            // Explicitly set active state
            bool beforeState = _editControlsContainer.activeSelf;
            _editControlsContainer.SetActive(_isEditMode);
            bool afterState = _editControlsContainer.activeSelf;
            Debug.Log($"[RTTFileManager] EditControls activeSelf before: {beforeState}, after: {afterState}, expected: {_isEditMode}");

            // Force layout rebuild if entering edit mode
            if (_isEditMode)
            {
                Canvas.ForceUpdateCanvases();
                var rt = _editControlsContainer.GetComponent<RectTransform>();
                if (rt != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                }
            }

            Debug.Log($"[RTTFileManager] EditControls SetActive({_isEditMode}) called, now activeSelf: {_editControlsContainer.activeSelf}, activeInHierarchy: {_editControlsContainer.activeInHierarchy}");
            // Check parent hierarchy
            Transform parent = _editControlsContainer.transform.parent;
            while (parent != null)
            {
                Debug.Log($"[RTTFileManager] Parent: {parent.name}, activeSelf: {parent.gameObject.activeSelf}");
                parent = parent.parent;
            }
            Debug.Log($"[RTTFileManager] EditControls child count: {_editControlsContainer.transform.childCount}");
            // Log all children for debugging
            for (int i = 0; i < _editControlsContainer.transform.childCount; i++)
            {
                var child = _editControlsContainer.transform.GetChild(i);
                var childRT = child as RectTransform;
                Debug.Log($"[RTTFileManager] EditControls child[{i}]: {child.name}, active: {child.gameObject.activeSelf}, pos: {(childRT != null ? childRT.anchoredPosition.ToString() : "N/A")}");
            }
        }
        else
        {
            Debug.LogError("[RTTFileManager] _editControlsContainer is NULL in ToggleEditMode!");
        }

        // Show/hide selected count text
        if (_selectedCountText != null)
            _selectedCountText.gameObject.SetActive(_isEditMode);

        // Show/hide checkboxes on items
        _fileGrid?.SetEditMode(_isEditMode);
        _fileList?.SetEditMode(_isEditMode);

        // Clear selection when exiting edit mode
        if (!_isEditMode)
        {
            _selectedItems.Clear();
            UpdateSelectAllCheckmark();
            UpdateSelectedCountText();
        }
        else
        {
            // Update selected count when entering edit mode
            UpdateSelectedCountText();
        }

        Debug.Log($"[RTTFileManager] Edit Mode: {_isEditMode}");
    }

    private void UpdateSelectedCountText()
    {
        // Get actual selected count from grid/list
        int count = 0;
        if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
            count = _fileGrid.GetSelectedPaths().Count;
        else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
            count = _fileList.GetSelectedPaths().Count;

        // Update the text (to the left of item count)
        if (_selectedCountText != null)
            _selectedCountText.text = count > 0 ? $"{count} item selected" : "0 item selected";
    }

    private void UpdateEditButtonVisual()
    {
        if (_editButton == null) return;

        // Find icon and change sprite
        var iconImg = _editButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.sprite = Resources.Load<Sprite>(_isEditMode ? "icon_check_mark" : "icon_edit");
        }

        // Calculate target color (lerp with white like VRButtonFactory does)
        Color themeColor = _isEditMode ? _accentColor : _primaryColor;
        Color glowColor = Color.Lerp(themeColor, Color.white, 0.75f);

        // Update GlowBorderHoverEffect's saved color so it persists through hover state changes
        var hoverController = _editButton.transform.Find("HitArea")?.GetComponent<HoverEffectController>();
        if (hoverController != null)
        {
            var glowEffect = hoverController.GetEffect("glow_border") as GlowBorderHoverEffect;
            glowEffect?.UpdateSavedGlowColor(glowColor);
        }
    }

    private void OnSelectAllClicked()
    {
        if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
        {
            bool allSelected = _fileGrid.AreAllSelected();
            _fileGrid.SetAllSelected(!allSelected);
        }
        else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
        {
            bool allSelected = _fileList.AreAllSelected();
            _fileList.SetAllSelected(!allSelected);
        }
        UpdateSelectAllCheckmark();
        UpdateActionButtonsState();
        UpdateSelectedCountText();
    }

    private void UpdateSelectAllCheckmark()
    {
        if (_selectAllCheckmark == null) return;

        bool allSelected = false;
        if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
            allSelected = _fileGrid.AreAllSelected();
        else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
            allSelected = _fileList.AreAllSelected();

        _selectAllCheckmark.gameObject.SetActive(allSelected);
    }

    private void OnSelectionChanged()
    {
        UpdateSelectAllCheckmark();
        UpdateActionButtonsState();
        UpdateSelectedCountText();
    }

    private void UpdateActionButtonsState()
    {
        int selectedCount = 0;
        if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
            selectedCount = _fileGrid.GetSelectedPaths().Count;
        else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
            selectedCount = _fileList.GetSelectedPaths().Count;

        bool hasSelection = selectedCount > 0;

        // Update button interactability and visual state
        SetButtonEnabled(_copyButton, _copyButtonCG, hasSelection);
        SetButtonEnabled(_moveButton, _moveButtonCG, hasSelection);
        SetButtonEnabled(_deleteButton, _deleteButtonCG, hasSelection);
    }

    private void SetButtonEnabled(Button button, CanvasGroup canvasGroup, bool enabled)
    {
        if (button != null)
            button.interactable = enabled;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = enabled ? 1f : 0.4f;
            canvasGroup.interactable = enabled;
        }
    }

    private void OnCopyClicked()
    {
        var selectedPaths = GetCurrentSelectedPaths();
        Debug.Log("[RTTFileManager] Copy clicked - selected items: " + selectedPaths.Count);

        if (selectedPaths.Count == 0) return;

        EnterClipboardMode(ClipboardOperation.Copy, selectedPaths);
    }

    private void OnMoveClicked()
    {
        var selectedPaths = GetCurrentSelectedPaths();
        Debug.Log("[RTTFileManager] Move clicked - selected items: " + selectedPaths.Count);

        if (selectedPaths.Count == 0) return;

        EnterClipboardMode(ClipboardOperation.Move, selectedPaths);
    }

    private void OnDeleteClicked()
    {
        var selectedPaths = GetCurrentSelectedPaths();
        Debug.Log("[RTTFileManager] Delete clicked - selected items: " + selectedPaths.Count);

        if (selectedPaths.Count == 0)
        {
            Debug.LogWarning("[RTTFileManager] No items selected for deletion");
            return;
        }

        ShowDeleteConfirmPopup();
    }

    private HashSet<string> GetCurrentSelectedPaths()
    {
        if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
            return _fileGrid.GetSelectedPaths();
        else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
            return _fileList.GetSelectedPaths();
        return new HashSet<string>();
    }

    #region Clipboard Mode (Copy/Move)

    /// <summary>
    /// Handler for Close button - either closes file manager or cancels clipboard mode
    /// </summary>
    private void OnCloseButtonClicked()
    {
        if (_isClipboardMode)
        {
            ExitClipboardMode();
        }
        else
        {
            _controller?.HandleBack();
        }
    }

    /// <summary>
    /// Handler for Edit button - either toggles edit mode or performs paste
    /// </summary>
    private void OnEditButtonClicked()
    {
        if (_isClipboardMode)
        {
            OnClipboardPaste();
        }
        else
        {
            ToggleEditMode();
        }
    }

    private void EnterClipboardMode(ClipboardOperation operation, HashSet<string> items)
    {
        // 1. Store clipboard data
        _clipboardOperation = operation;
        _clipboardItems = new List<string>(items);
        _clipboardSourcePath = _controller.GetCurrentPath();
        _isClipboardMode = true;

        // 2. Exit Edit Mode (hide edit controls, checkboxes)
        if (_isEditMode)
        {
            ToggleEditMode();
        }

        // 3. Change Close button to Back icon
        Sprite backIcon = Resources.Load<Sprite>("icon_back");
        if (_closeButtonIcon != null && backIcon != null)
        {
            _closeButtonIcon.sprite = backIcon;
        }

        // 4. Change Edit button to Copy/Move icon
        string iconName = operation == ClipboardOperation.Copy ? "icon_copy" : "icon_move_folder";
        Sprite operationIcon = Resources.Load<Sprite>(iconName);
        UpdateEditButtonIcon(operationIcon);
        UpdateEditButtonColor(_accentColor); // Use accent color to indicate active state

        // 5. Update paste button state (edit button enabled/disabled)
        UpdatePasteButtonState();

        Debug.Log($"[RTTFileManager] Entered Clipboard Mode: {operation}, {items.Count} items from {_clipboardSourcePath}");
    }

    private void ExitClipboardMode()
    {
        _isClipboardMode = false;
        _clipboardOperation = ClipboardOperation.None;
        _clipboardItems.Clear();
        _clipboardSourcePath = null;

        // Restore Close button icon
        if (_closeButtonIcon != null && _originalCloseIcon != null)
        {
            _closeButtonIcon.sprite = _originalCloseIcon;
        }

        // Restore Edit button icon and color
        UpdateEditButtonIcon(_originalEditIcon);
        UpdateEditButtonColor(_primaryColor);

        // Restore Edit button interactability
        var editButtonCG = _editButton?.GetComponent<CanvasGroup>();
        if (editButtonCG != null)
        {
            editButtonCG.alpha = 1f;
            editButtonCG.interactable = true;
            editButtonCG.blocksRaycasts = true;
        }

        var editBtn = _editButton?.GetComponentInChildren<Button>();
        if (editBtn != null)
        {
            editBtn.interactable = true;
        }

        Debug.Log("[RTTFileManager] Exited Clipboard Mode");
    }

    private void UpdateEditButtonIcon(Sprite icon)
    {
        if (_editButton == null || icon == null) return;

        // Find icon in the button hierarchy: HitArea/Visuals/Content/Icon
        var iconImg = _editButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.sprite = icon;
        }
    }

    private void UpdateEditButtonColor(Color color)
    {
        if (_editButton == null) return;

        // Update glow border color
        var glowEffect = _editButton.GetComponentInChildren<GlowBorderHoverEffect>();
        if (glowEffect != null)
        {
            glowEffect.UpdateSavedGlowColor(color);
        }
    }

    private void UpdatePasteButtonState()
    {
        if (!_isClipboardMode) return;

        string currentPath = _controller.GetCurrentPath();
        bool canPaste = !string.Equals(currentPath, _clipboardSourcePath,
                                        StringComparison.OrdinalIgnoreCase);

        // Also check write permission
        canPaste = canPaste && FileSystemService.CanCreateFolderInPath(currentPath);

        // Prevent moving folder into itself or its subfolder
        if (canPaste && _clipboardOperation == ClipboardOperation.Move)
        {
            foreach (string sourcePath in _clipboardItems)
            {
                if (currentPath.StartsWith(sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    canPaste = false;
                    break;
                }
            }
        }

        // Update Edit button (now acting as Paste) visual state
        var editButtonCG = _editButton?.GetComponent<CanvasGroup>();
        if (editButtonCG == null && _editButton != null)
        {
            editButtonCG = _editButton.AddComponent<CanvasGroup>();
        }

        if (editButtonCG != null)
        {
            editButtonCG.alpha = canPaste ? 1f : 0.4f;
            editButtonCG.interactable = canPaste;
            editButtonCG.blocksRaycasts = canPaste;
        }

        var editBtn = _editButton?.GetComponentInChildren<Button>();
        if (editBtn != null)
        {
            editBtn.interactable = canPaste;
        }
    }

    private void OnClipboardCancel()
    {
        Debug.Log("[RTTFileManager] Clipboard operation cancelled");
        ExitClipboardMode();
    }

    private void OnClipboardPaste()
    {
        if (_clipboardItems.Count == 0) return;

        string destination = _controller.GetCurrentPath();

        // Check for conflicts first
        var conflicts = CheckForConflicts(destination);

        if (conflicts.Count > 0)
        {
            ShowConflictDialog(conflicts, destination);
        }
        else
        {
            ExecutePasteOperation(destination);
        }
    }

    private List<string> CheckForConflicts(string destination)
    {
        var conflicts = new List<string>();

        foreach (string sourcePath in _clipboardItems)
        {
            string fileName = System.IO.Path.GetFileName(sourcePath);
            string destPath = System.IO.Path.Combine(destination, fileName);

            if (System.IO.File.Exists(destPath) || System.IO.Directory.Exists(destPath))
            {
                conflicts.Add(fileName);
            }
        }

        return conflicts;
    }

    private void ExecutePasteOperation(string destination, bool overwrite = false)
    {
        Debug.Log($"[RTTFileManager] Executing paste: {_clipboardOperation} to {destination}, overwrite={overwrite}");

        if (_clipboardOperation == ClipboardOperation.Copy)
        {
            _controller.CopyItems(_clipboardItems, destination, overwrite);
        }
        else if (_clipboardOperation == ClipboardOperation.Move)
        {
            _controller.MoveItems(_clipboardItems, destination, overwrite);
        }

        ExitClipboardMode();
    }

    private void ExecutePasteOperationSkipConflicts(string destination)
    {
        // Filter out items that would conflict
        var nonConflictingItems = new List<string>();

        foreach (string sourcePath in _clipboardItems)
        {
            string fileName = System.IO.Path.GetFileName(sourcePath);
            string destPath = System.IO.Path.Combine(destination, fileName);

            if (!System.IO.File.Exists(destPath) && !System.IO.Directory.Exists(destPath))
            {
                nonConflictingItems.Add(sourcePath);
            }
        }

        if (nonConflictingItems.Count > 0)
        {
            if (_clipboardOperation == ClipboardOperation.Copy)
            {
                _controller.CopyItems(nonConflictingItems, destination, false);
            }
            else if (_clipboardOperation == ClipboardOperation.Move)
            {
                _controller.MoveItems(nonConflictingItems, destination, false);
            }
        }

        ExitClipboardMode();
    }

    private void CreateConflictPopup()
    {
        if (_conflictPopup != null) return;

        var config = new RTTPopupMenu.PopupConfig
        {
            width = 550f,
            buttonHeight = 60f,
            sideSpacing = 20f,
            rowSpacing = 10f,
            labelHeight = 60f,
            labelFontSize = 28,
            fontSize = 24,
            borderWidth = 0.028f,
            primaryColor = _primaryColor,
            accentColor = _accentColor,
            overlayColor = new Color(0f, 0f, 0f, 0.4f),
            font = _font,
            layerName = "VirtualObjects",
            buttonBorderWidth = 0.04f,
            buttonGlowWidth = 0.08f,
            buttonGlowIntensity = 4f,
            buttonCornerRadius = 0.12f
        };

        _conflictPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);
    }

    private void ShowConflictDialog(List<string> conflicts, string destination)
    {
        if (_conflictPopup == null)
        {
            CreateConflictPopup();
        }

        _conflictPopup.Clear();

        string title = conflicts.Count == 1
            ? $"'{conflicts[0]}' already exists"
            : $"{conflicts.Count} items already exist";

        _conflictPopup.AddSectionBlock(title, new List<RTTPopupMenu.ButtonData>());

        var replaceBtn = new RTTPopupMenu.ButtonData(
            "Replace",
            () => { _conflictPopup.Hide(); ExecutePasteOperation(destination, true); },
            null,
            false,
            _accentColor
        );

        var skipBtn = new RTTPopupMenu.ButtonData(
            "Skip",
            () => { _conflictPopup.Hide(); ExecutePasteOperationSkipConflicts(destination); },
            null,
            false,
            _primaryColor
        );

        var cancelBtn = new RTTPopupMenu.ButtonData(
            "Cancel",
            () => { _conflictPopup.Hide(); },
            null,
            false,
            _primaryColor
        );

        _conflictPopup.AddSectionBlock("", new List<RTTPopupMenu.ButtonData> { replaceBtn, skipBtn, cancelBtn }, 3);
        _conflictPopup.Build();
        _conflictPopup.Show();
    }

    #endregion

    private void CreateDeleteConfirmPopup()
    {
        if (_deleteConfirmPopup != null) return;

        // Match RTTPopupInputable styling
        var config = new RTTPopupMenu.PopupConfig
        {
            width = 500f,
            buttonHeight = 60f,
            sideSpacing = 20f,
            rowSpacing = 10f,
            labelHeight = 50f,
            labelFontSize = 32,  // Match RTTPopupInputable.titleFontSize
            fontSize = 24,       // Match RTTPopupInputable.buttonFontSize
            borderWidth = 0.028f,
            primaryColor = _primaryColor,
            accentColor = _accentColor,
            overlayColor = new Color(0f, 0f, 0f, 0.4f), // Match New Folder popup
            font = _font,
            layerName = "VirtualObjects",
            // Button styling (match RTTPopupInputable.CreateActionButton)
            buttonBorderWidth = 0.04f,
            buttonGlowWidth = 0.08f,
            buttonGlowIntensity = 4f,
            buttonCornerRadius = 0.12f
        };

        _deleteConfirmPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);
    }

    private void ShowDeleteConfirmPopup()
    {
        if (_deleteConfirmPopup == null)
        {
            CreateDeleteConfirmPopup();
        }

        // Clear previous content and rebuild
        _deleteConfirmPopup.Clear();

        // Build confirmation message
        var selectedPaths = GetCurrentSelectedPaths();
        int count = selectedPaths.Count;
        string itemText = count == 1 ? "item" : "items";
        string title = $"Delete {count} {itemText}?";

        // Add title section
        _deleteConfirmPopup.AddSectionBlock(title, new List<RTTPopupMenu.ButtonData>());

        // Add Yes/No buttons (accent for Yes, primary for No)
        var yesButton = new RTTPopupMenu.ButtonData(
            "Yes",
            OnDeleteConfirmed,
            null,
            false,
            _accentColor // Accent color (magenta/pink)
        );

        var noButton = new RTTPopupMenu.ButtonData(
            "No",
            OnDeleteCancelled,
            null,
            false,
            _primaryColor // Primary color (cyan)
        );

        _deleteConfirmPopup.AddSectionBlock("", new List<RTTPopupMenu.ButtonData> { yesButton, noButton }, 2);

        _deleteConfirmPopup.Build();
        _deleteConfirmPopup.Show();
    }

    private void OnDeleteConfirmed()
    {
        var selectedPaths = GetCurrentSelectedPaths();
        Debug.Log("[RTTFileManager] Delete confirmed - deleting " + selectedPaths.Count + " items");

        // Hide popup
        if (_deleteConfirmPopup != null)
        {
            _deleteConfirmPopup.Hide();
        }

        // Call controller to delete items
        var itemsToDelete = new List<string>(selectedPaths);
        _controller?.DeleteItems(itemsToDelete);

        // Exit edit mode (which also clears selections in grid/list)
        ToggleEditMode();
    }

    private void OnDeleteCancelled()
    {
        Debug.Log("[RTTFileManager] Delete cancelled");

        if (_deleteConfirmPopup != null)
        {
            _deleteConfirmPopup.Hide();
        }
    }
    #endregion

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
        config.width = 990f;
        config.themeColor = _primaryColor;
        // fontSize * 2.2 = height, so 34 * 2.2 = 74.8 ≈ 75
        config.inputFontSize = 34;
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
            float iconSize = 30f;
            float leftPadding = 26f; 
            
            iconRT.anchorMin = new Vector2(0, 0.5f);
            iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);
            iconRT.anchoredPosition = new Vector2(leftPadding, 0);
            
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = searchIcon;
            iconImg.color = new Color(1f, 1f, 1f, 0.8f); // Slightly transparent white
            
            // 3. Adjust Text Area Padding
            // Find "Text Area" recursively in the InputField hierarchy
            float textOffset = iconSize;
            Transform textArea = FindChildRecursive(searchBar.transform, "Text Area");

            if (textArea != null)
            {
                RectTransform textAreaRT = textArea.GetComponent<RectTransform>();
                textAreaRT.offsetMin = new Vector2(textOffset, textAreaRT.offsetMin.y);
                Debug.Log($"[RTTFileManager] Text Area found and offsetMin set to: {textAreaRT.offsetMin}");
            }
            else
            {
                // Fallback: adjust Placeholder and Text directly
                var placeholder = searchBar.GetComponentInChildren<TMP_Text>();
                if (placeholder != null)
                {
                    placeholder.margin = new Vector4(textOffset, 0, 0, 0);
                    Debug.Log($"[RTTFileManager] Set placeholder margin to: {placeholder.margin}");
                }
            }
        }
        // -------------------------------------
        
        RectTransform rt = searchBar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(config.width, 75f);
        rt.anchoredPosition = Vector2.zero;
    }
    
    /// <summary>
    /// Get base path for a side panel item ID
    /// </summary>
    private string GetBasePath(string id)
    {
        return id switch
        {
            "internal" => FileSystemService.RootPath,
            "sdcard" => FileSystemService.GetSDCardPath(),
            "downloads" => FileSystemService.GetDownloadsPath(),
            "videos" => FileSystemService.GetVideosPath(),
            "music" => FileSystemService.GetMusicPath(),
            "recent" => FileSystemService.RootPath, // Recent uses root as base
            _ => FileSystemService.RootPath
        };
    }

    // Breadcrumb Update Logic
    public void UpdateBreadcrumbs(string path)
    {
        Debug.Log($"[RTTFileManager] UpdateBreadcrumbs called with path: {path}, container null: {_breadcrumbContainer == null}, instance: {GetInstanceID()}");

        // Update New Folder button state based on folder creation permission
        UpdateNewFolderButtonState();

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

        // Convert "root" to actual absolute path
        if (path == "root")
        {
            path = FileSystemService.RootPath;
        }

        // Update side panel selection based on current path
        // Only shows selection when at exact root of a side panel item, not in subfolders
        _sidePanel?.UpdateSelectionForPath(path);

        // Get root path and create relative path for display
        string rootPath = FileSystemService.RootPath;
        string displayPath = path;

        // Create list of breadcrumb items
        var breadcrumbs = new List<(string name, string fullPath, bool isEllipsis)>();

        // Get the first breadcrumb label and path from side panel selection
        string firstBreadcrumbLabel = _sidePanel != null ? _sidePanel.GetSelectedLabel() : null;
        string selectedId = _sidePanel != null ? _sidePanel.GetSelectedId() : null;
        if (string.IsNullOrEmpty(firstBreadcrumbLabel))
        {
            firstBreadcrumbLabel = "Home"; // Fallback
        }

        // Determine the base path for the first breadcrumb based on side panel selection
        string basePath = GetBasePath(selectedId);

        // Add first breadcrumb with selected side panel label
        breadcrumbs.Add((firstBreadcrumbLabel, basePath, false));

        // If path is not base path, add subfolders
        if (path != basePath && !string.IsNullOrEmpty(path))
        {
            // Make path relative to base path for display
            if (path.StartsWith(basePath))
            {
                displayPath = path.Substring(basePath.Length).TrimStart('/', '\\');
            }
            else if (path.StartsWith(rootPath))
            {
                displayPath = path.Substring(rootPath.Length).TrimStart('/', '\\');
            }

            if (!string.IsNullOrEmpty(displayPath))
            {
                string[] parts = displayPath.Split(new char[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
                string currentPath = basePath;

                var allFolders = new List<(string name, string fullPath)>();
                foreach (string part in parts)
                {
                    currentPath = System.IO.Path.Combine(currentPath, part);
                    // Fix encoding for folder names that may have mojibake
                    string fixedName = TextEncodingHelper.FixString(part);
                    allFolders.Add((fixedName, currentPath));
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
        float btnHeight = 75f; // +10% (was 68f)
        float btnWidth = _sortTriggerWidth * 1.2f;
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

        // Update paste button state if in clipboard mode
        if (_isClipboardMode)
        {
            UpdatePasteButtonState();
        }
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
        txt.fontSize = 24; // +10% (was 22)
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

        // Block dwell click for current folder (last breadcrumb) - keep visual normal
        if (isActive && !isEllipsis)
        {
            var clickLock = btnObj.AddComponent<VRButtonClickLock>();
            clickLock.Lock(); // Lock immediately - no need to click current folder
        }

        if (!isEllipsis)
        {
            // Normal breadcrumb: navigate back to folder (restores scroll position)
            btn.onClick.AddListener(() => _controller?.NavigateBack(pathToNavigate));
        }
        else
        {
            // Ellipsis: toggle dropdown showing hidden folders
            btn.onClick.AddListener(ToggleEllipsisPopup);
        }

        // BoxCollider for all clickable buttons
        BoxCollider col = btnObj.AddComponent<BoxCollider>();

        // Calculate overlap amount
        float overlapAmount = height * 0.45f; // Uses button height
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

        float itemHeight = 55f;       // +10% (was 50f)
        float verticalPadding = 13f;   // +10% (was 12f)
        float popupHeight = (_hiddenFolders.Count * itemHeight) + (verticalPadding * 2);

        Debug.Log($"[EllipsisPopup] Creating popup: hiddenFolders={_hiddenFolders.Count}, itemHeight={itemHeight}, verticalPadding={verticalPadding}, popupHeight={popupHeight}, width={_ellipsisPopupWidth}");

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
        float btnHeightLocal = 75f; // +10% (was 68f)
        float overlapAmount = btnHeightLocal * 0.45f;
        float btnWidth = _sortTriggerWidth * 1.25f;
        float effectiveWidth = btnWidth - overlapAmount;
        float ellipsisX = effectiveWidth; // Index 1 position

        popupRT.anchoredPosition = new Vector2(ellipsisX, -btnHeightLocal / 2 - 8f);

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
        float horizontalPadding = 22f; // +10% (was 20f)

        GameObject itemObj = new GameObject($"Item_{index}_{folderName}");
        itemObj.transform.SetParent(_ellipsisPopup.transform, false);

        RectTransform itemRT = itemObj.AddComponent<RectTransform>();
        itemRT.anchorMin = new Vector2(0, 1);
        itemRT.anchorMax = new Vector2(1, 1);
        itemRT.pivot = new Vector2(0.5f, 1);
        itemRT.sizeDelta = new Vector2(0, itemHeight);

        float yPos = -verticalPadding - (index * itemHeight);
        itemRT.anchoredPosition = new Vector2(0, yPos);

        Debug.Log($"[EllipsisPopup] Item {index} '{folderName}': yPos={yPos}, itemHeight={itemHeight}");

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
        txt.fontSize = 30;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false; // Single line - prevent wrap causing vertical expansion
        txt.overflowMode = TextOverflowModes.Ellipsis; // Truncate with ... if too long
        txt.maxVisibleLines = 1;

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

        // Navigate back to selected folder (restores scroll position)
        _controller?.NavigateBack(path);
    }

    /// <summary>
    /// Called when a file or folder item is clicked
    /// </summary>
    private void OnItemClicked(string path, bool isFolder)
    {
        // In edit mode, toggle checkbox instead of opening
        if (_isEditMode)
        {
            if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
                _fileGrid.ToggleItemSelection(path);
            else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
                _fileList.ToggleItemSelection(path);
            return;
        }

        if (isFolder)
        {
            // Navigate into the folder (saves clicked folder's index for accurate back navigation)
            _controller?.NavigateToFolder(path);
        }
        else
        {
            // Select the file (could also open it in future)
            _controller?.SelectFile(path);
        }
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
        if (_isGridView && _fileGrid != null)
        {
            int visibleRows = _fileGrid.GetVisibleRowsForPagination();
            _fileGrid.ScrollToPage(page, visibleRows);
        }
        else if (!_isGridView && _fileList != null)
        {
            int visibleRows = _fileList.GetVisibleRowsForPagination();
            _fileList.ScrollToPage(page, visibleRows);
        }
    }

    /// <summary>
    /// Get current scroll position (normalized 0-1, where 1 = top)
    /// </summary>
    public float GetScrollPosition()
    {
        if (_isGridView && _fileGrid != null)
        {
            return _fileGrid.GetScrollPosition();
        }
        else if (!_isGridView && _fileList != null)
        {
            return _fileList.GetScrollPosition();
        }
        return 1f; // Default to top
    }

    /// <summary>
    /// Set scroll position (normalized 0-1, where 1 = top)
    /// </summary>
    public void SetScrollPosition(float normalizedPosition)
    {
        if (_isGridView && _fileGrid != null)
        {
            _fileGrid.SetScrollPosition(normalizedPosition);
        }
        else if (!_isGridView && _fileList != null)
        {
            _fileList.SetScrollPosition(normalizedPosition);
        }
    }

    public void UpdateGrid(System.Collections.Generic.List<MockFile> files, string selectedPath = "")
    {
        if (_isGridView)
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
        else
        {
            if (_fileList != null)
            {
                _fileList.Populate(files, selectedPath);
                _fileList.UpdateSortState(_currentSortBy, _isAscending);
            }
            else
            {
                Debug.LogWarning("[RTTFileManager] FileList not ready yet!");
            }
        }
    }

    /// <summary>
    /// Update the item count label in header
    /// </summary>
    public void UpdateItemCount(int totalItems)
    {
        if (_itemCountText != null)
        {
            _itemCountText.text = totalItems == 1 ? "1 item" : $"{totalItems} items";
        }
    }

    private void CreateSidePanels()
    {
        float mainPanelWidth = _menuFrame.PanelWidth;
        float mainPanelHeight = _menuFrame.PanelHeight;
        float sideWidth = mainPanelWidth / 3f;
        float sideHeight = mainPanelHeight;
        float gapMeters = 0.05f;

        // Left Panel (Navigation) - sphere positioning
        PlaceSidePanelOnSphere("FileNavigationPanel", -1, mainPanelWidth, sideWidth, sideHeight, gapMeters, ref _leftFrame);
        if (_leftFrame != null)
        {
            _leftFrame.SetVisible(true); // Make visible immediately for now
            // Initialize Side Panel Content
            StartCoroutine(CreateLeftPanelContent());
        }

        // Right Panel (Detail) - sphere positioning
        PlaceSidePanelOnSphere("FileDetailPanel", 1, mainPanelWidth, sideWidth, sideHeight, gapMeters, ref _rightFrame);
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

        // Calculate side panel position using arc placement (like WorldPanelClusterRig Flat Planar)
        float rotRad = rotationAngle * Mathf.Deg2Rad;
        float halfSide = sideWidth / 2f;
        float centerOffsetX = halfSide * Mathf.Cos(rotRad);
        float centerOffsetZ = -halfSide * Mathf.Sin(rotRad);

        float totalX = (mainWidth / 2f) + gap + centerOffsetX;
        float totalZ = centerOffsetZ;

        Vector3 offset = mainRight * (side * totalX) + mainForward * totalZ;
        Vector3 panelPos = mainPos + offset;

        // Flat Planar orientation: content faces toward camera (like WorldPanelClusterRig.panelsFaceCamera = true)
        // Panel's forward points AWAY from camera, so content (rendered on back) faces toward camera
        Camera cam = Camera.main;
        Quaternion panelRot;
        if (cam != null)
        {
            // Calculate direction from camera to panel (away from camera)
            Vector3 awayFromCamera = panelPos - cam.transform.position;
            awayFromCamera.y = 0; // Keep panel upright (yaw only, like faceCameraYawOnly)

            if (awayFromCamera.sqrMagnitude > 0.001f)
            {
                // Panel forward points away from camera, content faces toward camera
                panelRot = Quaternion.LookRotation(awayFromCamera.normalized, Vector3.up);
            }
            else
            {
                // Fallback: use main panel's rotation with angle offset
                panelRot = mainRot * Quaternion.Euler(0, side * rotationAngle, 0);
            }
        }
        else
        {
            // No camera, fallback to original angle-based rotation
            panelRot = mainRot * Quaternion.Euler(0, side * rotationAngle, 0);
        }

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

    /// <summary>
    /// Place a side panel on sphere surface with camera as center.
    /// Uses PRIMARY menu frame (not app frame) for positioning reference.
    /// Sphere radius = zoom distance from VirtualObjectsZoomController or distance to primary frame.
    /// Panel faces camera (vector from panel to camera is perpendicular to panel surface).
    /// </summary>
    /// <param name="side">-1 for left, +1 for right</param>
    private void PlaceSidePanelOnSphere(string name, int side, float mainWidth, float sideWidth, float sideHeight,
        float gap, ref RTTMenuFrame frameRef)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[RTTFileManager] PlaceSidePanelOnSphere: No main camera found!");
            return;
        }

        if (_menuFrame == null)
        {
            Debug.LogError("[RTTFileManager] PlaceSidePanelOnSphere: No app frame (_menuFrame) found!");
            return;
        }

        // Mathematical solution to satisfy BOTH conditions:
        // 1. Inner edge lies exactly on main panel's plane
        // 2. Panel surface is perpendicular to vector (center -> camera)
        //
        // Solution: r = sqrt(|E-C|² - w²), θ = atan2(b,a) + arcsin(w*side/|E-C|)

        // Step 1: Calculate inner edge position on main panel's plane
        Vector3 mainRight = _menuFrame.transform.right;
        float innerEdgeOffset = (mainWidth / 2f) + gap;
        Vector3 innerEdgePos = _menuFrame.transform.position + mainRight * innerEdgeOffset * side;

        // Step 2: Calculate in horizontal plane (XZ)
        float w = sideWidth / 2f; // half width
        Vector3 cameraPos = cam.transform.position;

        // Vector from camera to inner edge (horizontal only)
        float a = innerEdgePos.x - cameraPos.x;
        float b = innerEdgePos.z - cameraPos.z;
        float distSq = a * a + b * b;
        float dist = Mathf.Sqrt(distSq);

        Vector3 panelPos;
        Quaternion panelRotation;

        // Edge case: camera too close to inner edge
        if (dist < 0.001f)
        {
            panelPos = innerEdgePos + mainRight * w * side;
            panelPos.y = _menuFrame.transform.position.y;
            panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
        }
        else
        {
            // Step 3: Calculate distance from camera to panel center
            float rSq = distSq - w * w;
            if (rSq < 0.0001f) rSq = 0.0001f;
            float r = Mathf.Sqrt(rSq);

            // Step 4: Calculate direction angle θ
            // θ = atan2(b, a) - arcsin(w * side / dist)
            float alpha = Mathf.Atan2(b, a);
            float sinArg = Mathf.Clamp((w * side) / dist, -1f, 1f);
            float theta = alpha - Mathf.Asin(sinArg);

            // Step 5: Calculate panel center position
            float dx = Mathf.Cos(theta);
            float dz = Mathf.Sin(theta);
            panelPos = new Vector3(
                cameraPos.x + dx * r,
                _menuFrame.transform.position.y,
                cameraPos.z + dz * r
            );

            // Step 6: Calculate rotation to face camera from center
            Vector3 toCamera = new Vector3(cameraPos.x - panelPos.x, 0, cameraPos.z - panelPos.z);
            if (toCamera.sqrMagnitude > 0.001f)
            {
                panelRotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
            else
            {
                panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
            }
        }

        // Calculate logical width based on aspect ratio (same resolution density as main panel)
        float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

        // Create RTTMenuFrame for the side panel with unique name
        frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
        frameRef.transform.position = panelPos;
        frameRef.transform.rotation = panelRotation;
        frameRef.transform.localScale = Vector3.one;

        Debug.Log($"[RTTFileManager] PlaceSidePanel: innerEdge={innerEdgePos}, panelPos={panelPos}");

        // Configure frame appearance
        frameRef.SetContentMargins(20f, 20f, 20f, 20f);
        frameRef.SetFloatingDataEnabled(true, 5);

        // Register with ZoomController for updates on zoom change
        var zoomController = VirtualObjectsZoomController.Instance;
        if (zoomController != null)
        {
            zoomController.RegisterSidePanel(frameRef, _menuFrame.transform, side, mainWidth, sideWidth, gap);
            Debug.Log($"[RTTFileManager] Registered {name} with VirtualObjectsZoomController");
        }

        Debug.Log($"[RTTFileManager] Placed {name}: pos={panelPos}");
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
        _fileDetail.Initialize(_primaryColor, _accentColor, _font);
    }
    #endregion

    #region Create Folder Popup

    /// <summary>
    /// Update the New Folder button's interactable state based on folder creation permission.
    /// Called when navigating to a new directory.
    /// </summary>
    private void UpdateNewFolderButtonState()
    {
        if (_newFolderButton == null || _newFolderCanvasGroup == null)
        {
            Debug.Log("[RTTFileManager] UpdateNewFolderButtonState skipped - button not yet created");
            return;
        }

        bool canCreate = _controller != null && _controller.CanCreateFolderHere();

        Debug.Log($"[RTTFileManager] UpdateNewFolderButtonState: canCreate={canCreate}");

        _newFolderButton.interactable = canCreate;
        _newFolderCanvasGroup.alpha = canCreate ? 1f : 0.4f;
        _newFolderCanvasGroup.interactable = canCreate; // Block Unity UI raycast
        _newFolderCanvasGroup.blocksRaycasts = canCreate; // Block graphic raycasts

        // VRButtonFactory creates Button and BoxCollider on the same HitArea object
        // Since _newFolderButton is on HitArea, get BoxCollider from same GameObject
        var collider = _newFolderButton.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.enabled = canCreate;
            Debug.Log($"[RTTFileManager] BoxCollider.enabled set to {canCreate}");
        }
        else
        {
            Debug.LogWarning("[RTTFileManager] BoxCollider not found on Button's GameObject!");
        }

        Debug.Log($"[RTTFileManager] New Folder button {(canCreate ? "ENABLED" : "DISABLED")} - alpha={_newFolderCanvasGroup.alpha}");
    }

    private void CreateFolderPopup()
    {
        if (_createFolderPopup != null) return;

        var config = new RTTPopupInputable.PopupConfig
        {
            title = "New Folder",
            inputLabel = "Folder Name",
            inputPlaceholder = "Enter folder name",
            buttonText = "Create",
            width = 575f,        // +15% (was 500f)
            padding = 33f,       // +10% (was 30f)
            titleFontSize = 31,  // +10% (was 28)
            labelFontSize = 24,  // +10% (was 22)
            inputFontSize = 29,  // +10% (was 26)
            buttonFontSize = 26, // +10% (was 24)
            buttonHeight = 72f,  // +10% (was 65f)
            inputHeight = 72f,   // +10% (was 65f)
            titleHeight = 55f,   // +10% (was 50f)
            closeButtonSize = 50f, // +10% (was 45f)
            spacing = 22f,       // +10% (was 20f)
            primaryColor = _primaryColor,
            accentColor = _accentColor,
            overlayColor = new Color(0f, 0f, 0f, 0.4f), // Lighter overlay
            font = _font,
            layerName = "VirtualObjects" // Use VirtualObjects layer for world-space interaction
        };

        // Create popup in world-space mode - fixed position in front of RTTFileManager
        // Pass _menuFrame.transform as reference so popup positions relative to it
        _createFolderPopup = RTTPopupInputable.CreateWorldSpace(config, _menuFrame.transform);
    }

    private void ShowCreateFolderPopup()
    {
        // Double-check permission (button should already be disabled, but safeguard)
        if (_controller == null || !_controller.CanCreateFolderHere())
        {
            Debug.LogWarning("[RTTFileManager] Cannot create folder in current path");
            return;
        }

        // Create popup if not exists
        if (_createFolderPopup == null)
        {
            CreateFolderPopup();
        }

        // Show with callbacks
        _createFolderPopup.Show(
            onConfirm: OnCreateFolderConfirmed,
            onCancel: OnCreateFolderCancelled
        );
    }

    private void OnCreateFolderConfirmed(string folderName)
    {
        Debug.Log($"[RTTFileManager] Create folder: {folderName}");

        // Request controller to create the folder
        if (_controller != null)
        {
            _controller.CreateFolder(folderName);
        }
    }

    private void OnCreateFolderCancelled()
    {
        Debug.Log("[RTTFileManager] Create folder cancelled");
    }

    #endregion

    #region Scan Status (Category Filter Mode)

    /// <summary>
    /// Show or hide the scan status animated dots (displayed in Row2 next to breadcrumbs).
    /// While scanning, displays animated ".", "..", "..." dots.
    /// </summary>
    /// <param name="show">True to show animated dots, false to hide</param>
    /// <param name="message">Unused - kept for API compatibility</param>
    /// <param name="progress">Unused - kept for API compatibility</param>
    public void ShowScanningIndicator(bool show, string message, float progress)
    {
        if (_scanStatusText == null) return;

        if (show)
        {
            // Start dots animation if not already running
            if (_dotsAnimationCoroutine == null)
            {
                _dotsAnimationCoroutine = StartCoroutine(AnimateDotsCoroutine());
            }
        }
        else
        {
            // Stop animation and hide text
            if (_dotsAnimationCoroutine != null)
            {
                StopCoroutine(_dotsAnimationCoroutine);
                _dotsAnimationCoroutine = null;
            }
            _scanStatusText.text = "";
        }
    }

    /// <summary>
    /// Coroutine that animates the scan status text with cycling dots: ".", "..", "..."
    /// </summary>
    private System.Collections.IEnumerator AnimateDotsCoroutine()
    {
        int dotCount = 1;
        while (true)
        {
            _scanStatusText.text = new string('.', dotCount);
            dotCount = (dotCount % 3) + 1; // Cycle 1 -> 2 -> 3 -> 1
            yield return new WaitForSeconds(0.4f);
        }
    }

    /// <summary>
    /// Update breadcrumb for filter mode (e.g., "All Videos", "All Music").
    /// Uses existing pill button style for consistency.
    /// </summary>
    /// <param name="displayName">Name to show in breadcrumb</param>
    /// <param name="canCreateFolder">Whether folder creation is allowed (false for filter mode)</param>
    /// <param name="sidePanelId">Optional side panel item ID to select (e.g., "videos", "music")</param>
    public void UpdateBreadcrumb(string displayName, bool canCreateFolder, string sidePanelId = null)
    {
        if (_breadcrumbContainer == null) return;

        // Clear existing breadcrumbs
        foreach (Transform child in _breadcrumbContainer)
        {
            Destroy(child.gameObject);
        }

        // Use existing pill button style
        float btnHeight = 75f;
        float btnWidth = _sortTriggerWidth * 1.2f;

        // Create single pill button for filter mode (uses accent color as "active")
        GameObject btn = CreateBreadcrumbPillButton(displayName, "", true, btnWidth, btnHeight, 0, 1, false);

        RectTransform btnRT = btn.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0, 0.5f);
        btnRT.anchorMax = new Vector2(0, 0.5f);
        btnRT.pivot = new Vector2(0, 0.5f);
        btnRT.anchoredPosition = Vector2.zero;

        // Disable new folder button in filter mode
        if (_newFolderButton != null && _newFolderCanvasGroup != null)
        {
            _newFolderButton.interactable = canCreateFolder;
            _newFolderCanvasGroup.alpha = canCreateFolder ? 1f : 0.4f;
            _newFolderCanvasGroup.interactable = canCreateFolder;
            _newFolderCanvasGroup.blocksRaycasts = canCreateFolder;

            var collider = _newFolderButton.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.enabled = canCreateFolder;
            }
        }

        // Update side panel selection
        if (!string.IsNullOrEmpty(sidePanelId))
        {
            _sidePanel?.SelectById(sidePanelId);
        }
        else
        {
            _sidePanel?.ClearSelection();
        }
    }

    /// <summary>
    /// Clear all items in the file grid/list (for scan mode).
    /// </summary>
    public void ClearFileView()
    {
        if (_isGridView && _fileGrid != null)
        {
            _fileGrid.Populate(new List<MockFile>(), "");
        }
        else if (_fileList != null)
        {
            _fileList.Populate(new List<MockFile>(), "");
        }
    }

    #endregion
}
