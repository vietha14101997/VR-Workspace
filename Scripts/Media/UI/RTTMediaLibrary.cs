using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Config;

/// <summary>
/// Main View for Media Library App.
/// Orchestrates the 3-panel layout: Side Panel (Left), Grid (Center), Detail (Right).
/// Based on RTTFileManager pattern with modifications:
/// - No New Folder button
/// - Grid only (no List view)
/// - Edit mode: Rename and Delete only (no Copy/Move)
/// </summary>
public class RTTMediaLibrary : MonoBehaviour
{
    #region Configuration
    private RTTMediaLibraryController _controller;
    private float _containerWidth;
    private float _containerHeight;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;
    #endregion

    #region UI References
    private RTTMenuFrame _menuFrame;
    private RTTMenuFrame _leftFrame;
    private RTTMenuFrame _rightFrame;

    private RTTMediaSidePanel _sidePanel;
    private RTTMediaGrid _grid;
    private RTTFileDetail _detailPanel;
    private RTTFilePagination _pagination;

    // Media Action Bar (below detail panel)
    private RTTMediaActionBar _mediaActionBar;
    private MediaVideoInfo? _currentVideo;

    // Track if there's an actual selected video (not just hovered)
    private bool _hasSelectedVideo = false;

    // Flag to track if initial setup is complete
    private bool _viewReady = false;

    // Edit Mode
    private bool _isEditMode = false;
    private GameObject _editButton;
    private HashSet<string> _selectedItems = new HashSet<string>();
    private GameObject _selectAllCheckbox;
    private Image _selectAllCheckmark;

    // Edit Mode UI in Row2
    private GameObject _editControlsContainer;
    private TextMeshProUGUI _selectedCountText;

    // Edit Mode Action Buttons
    private Button _renameButton;
    private Button _deleteButton;
    private CanvasGroup _renameButtonCG;
    private CanvasGroup _deleteButtonCG;
    private HoverEffectController _renameButtonHover;
    private HoverEffectController _deleteButtonHover;

    // Close button reference
    private GameObject _closeButton;
    private Image _closeButtonIcon;
    private Sprite _originalCloseIcon;
    private Sprite _originalEditIcon;
    #endregion

    #region Properties
    public RTTMediaSidePanel SidePanel => _sidePanel;
    public RTTMediaGrid Grid => _grid;
    public RTTFileDetail DetailPanel => _detailPanel;
    #endregion

    #region Events
    public event Action<MediaVideoInfo> OnVideoPlayRequested;
    public event Action OnCloseRequested;
    #endregion

    #region Header References
    private RectTransform _headerRT;
    private RectTransform _bodyRT;
    private float _singleRowHeight;
    private float _rowSpacing;
    private float _headerHeight2Rows;

    // Breadcrumb References
    private Transform _breadcrumbContainer;
    private TextMeshProUGUI _breadcrumbText;

    // View Options
    private RTTPopupMenu _viewOptionsPopup;
    private string _currentSortBy = "Name";
    private bool _isAscending = true;
    private TextMeshProUGUI _sortTriggerText;
    private TextMeshProUGUI _itemCountText;

    private float _sortTriggerWidth = 220f;

    // Group By Options
    private RTTPopupMenu _groupOptionsPopup;
    private string _currentGroupBy = "Date Added";
    private TextMeshProUGUI _groupTriggerText;

    // Side Panel Gap
    private const float SIDE_PANEL_GAP = 0.05f;
    #endregion

    #region Initialization
    public void Initialize(RTTMediaLibraryController controller, RTTMenuFrame menuFrame,
        float w, float h, TMP_FontAsset font, Color primary, Color accent)
    {
        _controller = controller;
        _menuFrame = menuFrame;
        _containerWidth = w;
        _containerHeight = h;
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;

        BuildUI();
    }

    public void BuildUI()
    {
        // Debug.Log("[RTTMediaLibrary] Building UI...");

        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        if (_menuFrame != null)
        {
            CreatePanels();
        }
        else
        {
            Debug.LogWarning("[RTTMediaLibrary] Parent RTTMenuFrame not found, cannot create side panels correctly.");
        }
    }

    public void Cleanup()
    {
        _viewReady = false;

        var zoomController = VirtualObjectsZoomController.Instance;
        if (zoomController != null)
        {
            if (_leftFrame != null) zoomController.UnregisterSidePanel(_leftFrame);
            if (_rightFrame != null) zoomController.UnregisterSidePanel(_rightFrame);
        }

        if (_leftFrame != null)
        {
            Destroy(_leftFrame.gameObject);
            _leftFrame = null;
        }
        if (_rightFrame != null)
        {
            Destroy(_rightFrame.gameObject);
            _rightFrame = null;
        }
        if (_pagination != null)
        {
            Destroy(_pagination.gameObject);
            _pagination = null;
        }
        if (_mediaActionBar != null)
        {
            Debug.Log("[RTTMediaLibrary] Cleanup: Destroying _mediaActionBar");
            _mediaActionBar.HideImmediate();
            Destroy(_mediaActionBar.gameObject);
            _mediaActionBar = null;
        }

        if (_viewOptionsPopup != null)
        {
            Destroy(_viewOptionsPopup.gameObject);
            _viewOptionsPopup = null;
        }

        if (_groupOptionsPopup != null)
        {
            Destroy(_groupOptionsPopup.gameObject);
            _groupOptionsPopup = null;
        }
    }

    private void OnEnable()
    {
        if (!_viewReady) return;

        if (_leftFrame != null) _leftFrame.gameObject.SetActive(true);
        if (_rightFrame != null) _rightFrame.gameObject.SetActive(true);
        if (_pagination != null) _pagination.Show();

        // Show action bar when app is re-opened (if not in edit mode)
        // BUT only if user previously selected a video - prevents showing during dwell pre-loading
        if (_mediaActionBar != null && !_isEditMode && _hasSelectedVideo)
        {
            _mediaActionBar.ShowWithFade();
        }
    }

    private void OnDisable()
    {
        if (_leftFrame != null) _leftFrame.gameObject.SetActive(false);
        if (_rightFrame != null) _rightFrame.gameObject.SetActive(false);
        if (_pagination != null) _pagination.Hide();

        if (_mediaActionBar != null) _mediaActionBar.HideImmediate();

        if (_viewOptionsPopup != null) _viewOptionsPopup.Hide();
        if (_groupOptionsPopup != null) _groupOptionsPopup.Hide();
    }

    private void OnDestroy()
    {
        Cleanup();

        if (_grid != null)
        {
            _grid.OnVideoSelected -= OnGridVideoSelected;
            _grid.OnVideoDoubleClicked -= OnGridVideoDoubleClicked;
            _grid.OnVideoHoverEnter -= OnGridVideoHoverEnter;
            _grid.OnVideoHoverExit -= OnGridVideoHoverExit;
            _grid.OnPageChanged -= OnGridPageChanged;
        }

        // RTTFileDetail doesn't have events - action buttons are handled directly

        if (_sidePanel != null)
        {
            _sidePanel.OnCategorySelected -= OnCategorySelected;
        }
    }
    #endregion

    #region Panel Creation
    private void CreatePanels()
    {
        if (_menuFrame == null) return;

        // 1. Create Pagination
        CreatePagination();

        // 2. Create Side Panels
        CreateSidePanels();

        // 3. Setup Center Grid
        StartCoroutine(CreateCenterGrid());
    }

    private void CreatePagination()
    {
        RTTToolbar toolbar = RTTToolbar.Instance;
        if (toolbar == null)
        {
            Debug.LogWarning("[RTTMediaLibrary] RTTToolbar not found, pagination positioning may be incorrect");
            toolbar = RTTToolbar.Create();
        }

        GameObject pagObj = new GameObject("MediaPagination");
        pagObj.transform.SetParent(toolbar.transform, false);
        pagObj.transform.localPosition = toolbar.GetPaginationLocalPosition();
        pagObj.transform.localRotation = Quaternion.identity;

        _pagination = pagObj.AddComponent<RTTFilePagination>();
        _pagination.Initialize(_controller);
    }

    private void CreateSidePanels()
    {
        float mainPanelWidth = _menuFrame.PanelWidth;
        float mainPanelHeight = _menuFrame.PanelHeight;
        float sideWidth = mainPanelWidth / 3f;
        float sideHeight = mainPanelHeight;

        // Left Panel (Navigation) - created with alpha=0, will fade in with main frame
        PlaceSidePanelOnSphere("MediaNavigationPanel", -1, mainPanelWidth, sideWidth, sideHeight, SIDE_PANEL_GAP, ref _leftFrame);
        if (_leftFrame != null)
        {
            // Display quad enabled but transparent - ready for coordinated fade
            _leftFrame.SetVisible(true);
            SetFrameAlpha(_leftFrame, 0f);
            StartCoroutine(CreateLeftPanelContent());
        }

        // Right Panel (Detail) - created with alpha=0, will fade in with main frame
        PlaceSidePanelOnSphere("MediaDetailPanel", 1, mainPanelWidth, sideWidth, sideHeight, SIDE_PANEL_GAP, ref _rightFrame);
        if (_rightFrame != null)
        {
            // Display quad enabled but transparent - ready for coordinated fade
            _rightFrame.SetVisible(true);
            SetFrameAlpha(_rightFrame, 0f);
            StartCoroutine(CreateRightPanelContent());
        }
    }

    /// <summary>
    /// Set alpha of a frame's display quad.
    /// </summary>
    private void SetFrameAlpha(RTTMenuFrame frame, float alpha)
    {
        if (frame == null) return;
        var quad = frame.GetDisplayQuad();
        if (quad?.material != null)
            quad.material.color = new UnityEngine.Color(1f, 1f, 1f, alpha);
    }

    /// <summary>
    /// Get all frames (main + side panels) for coordinated fade animation.
    /// </summary>
    public List<RTTMenuFrame> GetAllFrames()
    {
        var frames = new List<RTTMenuFrame>();
        if (_menuFrame != null) frames.Add(_menuFrame);
        if (_leftFrame != null) frames.Add(_leftFrame);
        if (_rightFrame != null) frames.Add(_rightFrame);
        return frames;
    }

    /// <summary>
    /// Show side panels (legacy - now handled by coordinated fade in RTTAppManager).
    /// </summary>
    public void ShowSidePanels()
    {
        Debug.Log($"[RTTMediaLibrary] ShowSidePanels called - left={(_leftFrame != null)}, right={(_rightFrame != null)}");
        // Side panels now fade in with main frame via coordinated animation
        // This method kept for backward compatibility
    }

    /// <summary>
    /// Hide side panels (for app closing).
    /// </summary>
    public void HideSidePanels()
    {
        if (_leftFrame != null)
            _leftFrame.SetVisible(false);
        if (_rightFrame != null)
            _rightFrame.SetVisible(false);
    }

    private void PlaceSidePanelOnSphere(string name, int side, float mainWidth, float sideWidth, float sideHeight,
        float gap, ref RTTMenuFrame frameRef)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[RTTMediaLibrary] PlaceSidePanelOnSphere: No main camera found!");
            return;
        }

        if (_menuFrame == null)
        {
            Debug.LogError("[RTTMediaLibrary] PlaceSidePanelOnSphere: No app frame (_menuFrame) found!");
            return;
        }

        Vector3 mainRight = _menuFrame.transform.right;
        float innerEdgeOffset = (mainWidth / 2f) + gap;
        Vector3 innerEdgePos = _menuFrame.transform.position + mainRight * innerEdgeOffset * side;

        float w = sideWidth / 2f;
        Vector3 cameraPos = cam.transform.position;

        float a = innerEdgePos.x - cameraPos.x;
        float b = innerEdgePos.z - cameraPos.z;
        float distSq = a * a + b * b;
        float dist = Mathf.Sqrt(distSq);

        Vector3 panelPos;
        Quaternion panelRotation;

        if (dist < 0.001f)
        {
            panelPos = innerEdgePos + mainRight * w * side;
            panelPos.y = _menuFrame.transform.position.y;
            panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
        }
        else
        {
            float rSq = distSq - w * w;
            if (rSq < 0.0001f) rSq = 0.0001f;
            float r = Mathf.Sqrt(rSq);

            float alpha = Mathf.Atan2(b, a);
            float sinArg = Mathf.Clamp((w * side) / dist, -1f, 1f);
            float theta = alpha - Mathf.Asin(sinArg);

            float dx = Mathf.Cos(theta);
            float dz = Mathf.Sin(theta);
            panelPos = new Vector3(
                cameraPos.x + dx * r,
                _menuFrame.transform.position.y,
                cameraPos.z + dz * r
            );

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

        float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

        frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
        frameRef.transform.position = panelPos;
        frameRef.transform.rotation = panelRotation;
        frameRef.transform.localScale = Vector3.one;

        frameRef.SetContentMargins(20f, 20f, 20f, 20f);
        frameRef.SetFloatingDataEnabled(true, 5);

        var zoomController = VirtualObjectsZoomController.Instance;
        if (zoomController != null)
        {
            zoomController.RegisterSidePanel(frameRef, _menuFrame.transform, side, mainWidth, sideWidth, gap);
        }

        // Debug.Log($"[RTTMediaLibrary] Placed {name}: pos={panelPos}");
    }

    private IEnumerator CreateLeftPanelContent()
    {
        Debug.Log($"[RTTMediaLibrary] CreateLeftPanelContent started at {Time.realtimeSinceStartup:F3}s");
        while (_leftFrame.ContentContainer == null) yield return null;

        var containerSize = _leftFrame.GetContentSize();

        GameObject contentObj = new GameObject("SidePanelContent");
        contentObj.transform.SetParent(_leftFrame.ContentContainer, false);

        RectTransform rt = contentObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _sidePanel = contentObj.AddComponent<RTTMediaSidePanel>();

        // Subscribe to event BEFORE Initialize so we catch the initial selection
        _sidePanel.OnCategorySelected += OnCategorySelected;

        _sidePanel.Initialize(_controller, containerSize.x, containerSize.y, _font, _primaryColor, _accentColor);

        _leftFrame.MarkDirty();
        Debug.Log($"[RTTMediaLibrary] Left panel content created at {Time.realtimeSinceStartup:F3}s");
    }

    private IEnumerator CreateRightPanelContent()
    {
        Debug.Log($"[RTTMediaLibrary] CreateRightPanelContent started at {Time.realtimeSinceStartup:F3}s");
        while (_rightFrame.ContentContainer == null) yield return null;

        var containerSize = _rightFrame.GetContentSize();

        GameObject contentObj = new GameObject("DetailContent");
        contentObj.transform.SetParent(_rightFrame.ContentContainer, false);

        RectTransform rt = contentObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Use RTTFileDetail instead of RTTMediaDetail
        _detailPanel = contentObj.AddComponent<RTTFileDetail>();
        _detailPanel.Initialize(_primaryColor, _accentColor, _font);

        // Start with empty state - will be populated when first video is selected
        _detailPanel.ShowEmpty();

        _rightFrame.MarkDirty();

        // Create action bar below the panel (follows panel position)
        CreateMediaActionBar();

        Debug.Log($"[RTTMediaLibrary] Right panel content created at {Time.realtimeSinceStartup:F3}s");
    }

    /// <summary>
    /// Create RTTMediaActionBar that follows the detail panel.
    /// </summary>
    private void CreateMediaActionBar()
    {
        if (_rightFrame == null) return;

        // Don't create if already exists
        if (_mediaActionBar != null)
        {
            Debug.Log("[RTTMediaLibrary] ActionBar already exists, skipping creation");
            return;
        }

        // Get panel world size for positioning calculations
        Vector2 panelSize = _rightFrame.GetWorldSize();
        float targetHeight = panelSize.y;
        float panelWidth = panelSize.x;

        // Create action bar that follows the right panel
        _mediaActionBar = RTTMediaActionBar.Create(
            _rightFrame.transform,
            targetHeight,
            panelWidth,
            _primaryColor,
            _accentColor,
            _font
        );

        // Wire up events
        _mediaActionBar.OnPlayClicked += OnPlayButtonClicked;
        _mediaActionBar.OnFavouriteClicked += OnFavouriteButtonClicked;
        _mediaActionBar.OnPlaylistClicked += OnPlaylistButtonClicked;

        // Start hidden - will fade in when first video is selected
        // This creates smooth progressive loading: grid groups → items → select first → detail + actionbar
        _mediaActionBar.HideImmediate();
    }

    private void ShowActionBarAfterDelay()
    {
        if (_mediaActionBar != null && !_isEditMode)
        {
            _mediaActionBar.ShowWithFade();
            Debug.Log("[RTTMediaLibrary] Media action bar shown after delay");
        }
    }
    #endregion

    #region Center Grid Creation
    private IEnumerator CreateCenterGrid()
    {
        while (_menuFrame.ContentContainer == null) yield return null;

        foreach (Transform child in _menuFrame.ContentContainer)
        {
            if (child.name == "PlaceholderText") Destroy(child.gameObject);
        }

        Vector2 contentSize = _menuFrame.GetContentSize();
        float panelHeight = contentSize.y;

        float headerBaseHeight = panelHeight * 0.198f;
        _singleRowHeight = headerBaseHeight / 2f;

        if (_singleRowHeight < 88f)
        {
            _singleRowHeight = 88f;
        }

        _rowSpacing = _singleRowHeight * 0.1875f;
        _headerHeight2Rows = (_singleRowHeight * 2f) + _rowSpacing;

        float bottomPadding = panelHeight * 0.02f;

        // 1. Create Body Object (Grid Container)
        GameObject bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(_menuFrame.ContentContainer, false);
        _bodyRT = bodyObj.AddComponent<RectTransform>();
        _bodyRT.anchorMin = Vector2.zero;
        _bodyRT.anchorMax = Vector2.one;
        _bodyRT.offsetMax = new Vector2(0, -_headerHeight2Rows);
        _bodyRT.offsetMin = new Vector2(0, bottomPadding);

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
        Debug.Log($"[RTTMediaLibrary] After CreateHeaderRows: _headerRT={_headerRT != null}, _breadcrumbContainer={_breadcrumbContainer != null}");

        // 4. Create Grid Inside Body
        GameObject gridObj = new GameObject("MediaGrid");
        gridObj.transform.SetParent(_bodyRT, false);
        RectTransform gridRT = gridObj.AddComponent<RectTransform>();
        gridRT.anchorMin = Vector2.zero;
        gridRT.anchorMax = Vector2.one;
        gridRT.offsetMin = Vector2.zero;
        gridRT.offsetMax = Vector2.zero;

        _grid = gridObj.AddComponent<RTTMediaGrid>();
        float bodyHeight = panelHeight - _headerHeight2Rows - bottomPadding;
        _grid.Initialize(_controller, contentSize.x, bodyHeight, _font, _primaryColor, _accentColor);

        // Wire grid events
        _grid.OnVideoSelected += OnGridVideoSelected;
        _grid.OnVideoDoubleClicked += OnGridVideoDoubleClicked;
        _grid.OnVideoHoverEnter += OnGridVideoHoverEnter;
        _grid.OnVideoHoverExit += OnGridVideoHoverExit;
        _grid.OnPageChanged += OnGridPageChanged;
        _grid.SetSelectionChangedCallback(OnSelectionChanged);

        // Render Order
        headerObj.transform.SetAsLastSibling();
        bodyObj.transform.SetAsFirstSibling();

        _viewReady = true;

        // Set initial breadcrumb and category (default to Videos)
        UpdateBreadcrumbForCategory("videos");

        _controller?.OnViewReady();

        StartCoroutine(InitializePageSizeDeferred());

        Debug.Log("[RTTMediaLibrary] Center grid created");
    }

    private IEnumerator InitializePageSizeDeferred()
    {
        yield return null;

        int itemsPerPage = _grid?.ItemsPerPage ?? 6;
        _controller?.SetPageSize(itemsPerPage);
        Debug.Log($"[RTTMediaLibrary] InitializePageSizeDeferred: itemsPerPage={itemsPerPage}");
    }

    private void CreateHeaderRows(RectTransform parent)
    {
        // Row 1: Close | Sort | Search | Edit (No New Folder)
        CreateRow1(parent);

        // Row 2: Breadcrumbs (or Edit Controls) | Item Count | Refresh
        CreateRow2(parent);
    }

    private void CreateRow1(RectTransform parent)
    {
        RectTransform rowRT = CreateRowContainer(parent, "Row1", 0);

        // Left: Close Button
        _originalCloseIcon = Resources.Load<Sprite>("icon_close");
        _closeButton = VRButtonFactory.CreateBareIconButton(
            rowRT,
            75f,
            _originalCloseIcon,
            _primaryColor,
            OnCloseButtonClicked
        );
        _closeButtonIcon = _closeButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
        RectTransform closeRT = _closeButton.GetComponent<RectTransform>();
        SetupRowElement(closeRT, new Vector2(0, 0.5f), new Vector2(20, 0));

        // Sort Button
        var sortConfig = new VRButtonFactory.ButtonConfig
        {
            label = _currentSortBy,
            themeColor = _primaryColor,
            width = _sortTriggerWidth,
            height = 75f,
            fontSize = 26,
            font = _font,
            textOnly = true,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject sortTrigger = VRButtonFactory.CreateButton(rowRT, sortConfig, ToggleViewOptionsPopup);

        _sortTriggerText = sortTrigger.GetComponentInChildren<TextMeshProUGUI>();
        if (_sortTriggerText != null)
        {
            _sortTriggerText.alignment = TextAlignmentOptions.Center;
            _sortTriggerText.margin = Vector4.zero;
        }

        RectTransform sortRT = sortTrigger.GetComponent<RectTransform>();
        float gapCenterFromCenter = -_containerWidth / 4f - 203.5f;
        sortRT.anchorMin = new Vector2(0.5f, 0.5f);
        sortRT.anchorMax = new Vector2(0.5f, 0.5f);
        sortRT.pivot = new Vector2(0.5f, 0.5f);
        sortRT.anchoredPosition = new Vector2(gapCenterFromCenter, 0);

        CreateViewOptionsPopup(sortRT);

        // Group By Trigger Button (symmetric to sortTrigger on right side of search bar)
        var groupConfig = new VRButtonFactory.ButtonConfig
        {
            label = _currentGroupBy,
            themeColor = _primaryColor,
            width = _sortTriggerWidth,
            height = 75f,
            fontSize = 26,
            font = _font,
            textOnly = true,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject groupTrigger = VRButtonFactory.CreateButton(rowRT, groupConfig, ToggleGroupOptionsPopup);

        _groupTriggerText = groupTrigger.GetComponentInChildren<TextMeshProUGUI>();
        if (_groupTriggerText != null)
        {
            _groupTriggerText.alignment = TextAlignmentOptions.Center;
            _groupTriggerText.margin = Vector4.zero;
        }

        RectTransform groupRT = groupTrigger.GetComponent<RectTransform>();
        float groupCenterFromCenter = _containerWidth / 4f + 203.5f;  // Symmetric to sortTrigger (positive X)
        groupRT.anchorMin = new Vector2(0.5f, 0.5f);
        groupRT.anchorMax = new Vector2(0.5f, 0.5f);
        groupRT.pivot = new Vector2(0.5f, 0.5f);
        groupRT.anchoredPosition = new Vector2(groupCenterFromCenter, 0);

        CreateGroupOptionsPopup(groupRT);

        // Right: Edit Button
        _originalEditIcon = Resources.Load<Sprite>("icon_edit");
        var editConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Edit",
            icon = _originalEditIcon,
            themeColor = _primaryColor,
            width = 75f,
            height = 75f,
            iconOnly = true,
            iconSize = 39f,
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
        SetupRowElement(editRT, new Vector2(1, 0.5f), new Vector2(-20, 0));

        // NO New Folder button for Media Library

        // Center: Search Bar
        CreateSearchBar(rowRT);
    }

    private void CreateRow2(RectTransform parent)
    {
        float row2Y = -(_singleRowHeight + _rowSpacing);
        RectTransform rowRT = CreateRowContainer(parent, "Row2", row2Y);

        // Right: Refresh Button
        Sprite refreshIcon = Resources.Load<Sprite>("icon_refresh");
        var refreshConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Refresh",
            icon = refreshIcon,
            themeColor = _accentColor,
            width = 75f,
            height = 75f,
            iconOnly = true,
            iconSize = 39f,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject refreshBtn = VRButtonFactory.CreateButton(
            rowRT,
            refreshConfig,
            () => {
                // Smart refresh: incremental scan to detect new/removed files
                // Thumbnails for removed files are cleaned up automatically
                // Existing valid thumbnails are preserved
                _controller?.SmartRefresh();
            }
        );
        RectTransform refreshRT = refreshBtn.GetComponent<RectTransform>();
        SetupRowElement(refreshRT, new Vector2(1, 0.5f), new Vector2(-20, 0));

        // Item Count Label
        GameObject countObj = new GameObject("ItemCountLabel");
        countObj.transform.SetParent(rowRT, false);

        _itemCountText = countObj.AddComponent<TextMeshProUGUI>();
        _itemCountText.text = "0 items";
        _itemCountText.font = _font;
        _itemCountText.fontSize = 31;
        _itemCountText.fontStyle = FontStyles.Bold;
        _itemCountText.color = Color.white;
        _itemCountText.alignment = TextAlignmentOptions.Center;
        _itemCountText.raycastTarget = false;

        RectTransform countRT = countObj.GetComponent<RectTransform>();
        countRT.sizeDelta = new Vector2(_sortTriggerWidth, 75f);
        float countCenterFromCenter = _containerWidth / 4f + 203.5f;
        countRT.anchorMin = new Vector2(0.5f, 0.5f);
        countRT.anchorMax = new Vector2(0.5f, 0.5f);
        countRT.pivot = new Vector2(0.5f, 0.5f);
        countRT.anchoredPosition = new Vector2(countCenterFromCenter, 0);

        // Selected Count Label (Edit Mode)
        float searchBarRightEdge = 990f / 2f;

        GameObject selectedObj = new GameObject("SelectedCountLabel");
        selectedObj.transform.SetParent(rowRT, false);

        _selectedCountText = selectedObj.AddComponent<TextMeshProUGUI>();
        _selectedCountText.text = "";
        _selectedCountText.font = _font;
        _selectedCountText.fontSize = 28;
        _selectedCountText.fontStyle = FontStyles.Bold;
        _selectedCountText.color = Color.white;
        _selectedCountText.alignment = TextAlignmentOptions.MidlineRight;
        _selectedCountText.raycastTarget = false;

        RectTransform selectedRT = selectedObj.GetComponent<RectTransform>();
        selectedRT.sizeDelta = new Vector2(280f, 75f);
        selectedRT.anchorMin = new Vector2(0.5f, 0.5f);
        selectedRT.anchorMax = new Vector2(0.5f, 0.5f);
        selectedRT.pivot = new Vector2(1, 0.5f);
        selectedRT.anchoredPosition = new Vector2(searchBarRightEdge, 0);
        selectedObj.SetActive(false);

        // Breadcrumb Container (holds pill-shaped segments like RTTFileManager)
        GameObject crumbContainer = new GameObject("Breadcrumbs");
        crumbContainer.layer = LayerMask.NameToLayer("UI");
        crumbContainer.transform.SetParent(rowRT, false);

        // IMPORTANT: Add RectTransform FIRST, then store the reference
        // Storing transform before AddComponent<RectTransform> can cause stale reference issues
        RectTransform crumbRT = crumbContainer.AddComponent<RectTransform>();
        _breadcrumbContainer = crumbRT; // Store RectTransform as the container reference

        crumbRT.anchorMin = new Vector2(0, 0);
        crumbRT.anchorMax = new Vector2(1, 1);  // Stretch full width like RTTFileManager
        crumbRT.pivot = new Vector2(0, 0.5f);
        crumbRT.offsetMin = new Vector2(20f, 0);
        crumbRT.offsetMax = new Vector2(-520f, 0);  // Leave room for item count and refresh button

        Debug.Log($"[RTTMediaLibrary] CreateRow2: _breadcrumbContainer SET to {_breadcrumbContainer.name}, instanceID={GetInstanceID()}");

        // Edit Controls Container (hidden by default)
        CreateEditControlsInRow2(rowRT);
    }

    private void CreateEditControlsInRow2(RectTransform parent)
    {
        _editControlsContainer = new GameObject("EditControls");
        _editControlsContainer.transform.SetParent(parent, false);

        RectTransform editRT = _editControlsContainer.AddComponent<RectTransform>();
        editRT.anchorMin = new Vector2(0, 0);
        editRT.anchorMax = new Vector2(1, 1);
        editRT.pivot = new Vector2(0, 0.5f);
        editRT.offsetMin = new Vector2(20, 0);
        editRT.offsetMax = new Vector2(-320, 0);

        // Select All Checkbox
        CreateSelectAllCheckbox(_editControlsContainer.transform);

        // Positioning for buttons
        float searchBarWidth = 990f;
        float searchBarLeftFromRowCenter = -searchBarWidth / 2f;
        float containerCenterOffset = (20f + (-320f)) / 2f;
        float searchBarLeftInContainer = searchBarLeftFromRowCenter - containerCenterOffset;
        float buttonsStartX = searchBarLeftInContainer - 90f;
        float btnSpacing = 15f;

        // Rename Button
        var renameBtn = CreateEditModeActionButton(_editControlsContainer.transform, "Rename", "icon_rename", OnRenameClicked, 130f);
        var renameRT = renameBtn.GetComponent<RectTransform>();
        renameRT.anchorMin = renameRT.anchorMax = new Vector2(0.5f, 0.5f);
        renameRT.pivot = new Vector2(0, 0.5f);
        renameRT.anchoredPosition = new Vector2(buttonsStartX, 0);
        _renameButton = renameBtn.GetComponent<Button>();
        _renameButtonCG = renameBtn.AddComponent<CanvasGroup>();
        _renameButtonHover = renameBtn.GetComponent<HoverEffectController>();

        // Delete Button (NO Copy/Move for Media Library)
        var deleteBtn = CreateEditModeActionButton(_editControlsContainer.transform, "Delete", "icon_trash", OnDeleteClicked, 100f);
        var deleteRT = deleteBtn.GetComponent<RectTransform>();
        deleteRT.anchorMin = deleteRT.anchorMax = new Vector2(0.5f, 0.5f);
        deleteRT.pivot = new Vector2(0, 0.5f);
        float deleteX = buttonsStartX + renameRT.sizeDelta.x + btnSpacing;
        deleteRT.anchoredPosition = new Vector2(deleteX, 0);
        _deleteButton = deleteBtn.GetComponent<Button>();
        _deleteButtonCG = deleteBtn.AddComponent<CanvasGroup>();
        _deleteButtonHover = deleteBtn.GetComponent<HoverEffectController>();

        UpdateActionButtonsState();

        _editControlsContainer.SetActive(false);
    }

    private void CreateSelectAllCheckbox(Transform parent)
    {
        float checkboxSize = 50f;
        float labelWidth = 150f;
        float spacing = 20f;  // Doubled spacing between checkbox and label
        float leftPadding = 20f;

        GameObject container = new GameObject("SelectAllContainer");
        container.transform.SetParent(parent, false);
        RectTransform containerRT = container.AddComponent<RectTransform>();
        containerRT.anchorMin = containerRT.anchorMax = new Vector2(0, 0.5f);
        containerRT.pivot = new Vector2(0, 0.5f);
        containerRT.sizeDelta = new Vector2(checkboxSize + spacing + labelWidth, checkboxSize);
        containerRT.anchoredPosition = new Vector2(leftPadding, 0);

        // Checkbox button
        _selectAllCheckbox = new GameObject("Checkbox");
        _selectAllCheckbox.transform.SetParent(container.transform, false);
        RectTransform checkboxRT = _selectAllCheckbox.AddComponent<RectTransform>();
        checkboxRT.anchorMin = checkboxRT.anchorMax = new Vector2(0, 0.5f);
        checkboxRT.pivot = new Vector2(0, 0.5f);
        checkboxRT.sizeDelta = new Vector2(checkboxSize, checkboxSize);
        checkboxRT.anchoredPosition = Vector2.zero;

        Image checkboxBg = _selectAllCheckbox.AddComponent<Image>();
        checkboxBg.raycastTarget = true;
        checkboxBg.sprite = GetRoundedRectSprite();
        checkboxBg.type = Image.Type.Sliced;
        checkboxBg.color = new Color(0f, 0f, 0f, 0.3f);  // Same as item hover color

        // Checkmark icon
        GameObject checkmarkObj = new GameObject("Checkmark");
        checkmarkObj.transform.SetParent(_selectAllCheckbox.transform, false);
        RectTransform checkmarkRT = checkmarkObj.AddComponent<RectTransform>();
        checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
        checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
        checkmarkRT.offsetMin = Vector2.zero;
        checkmarkRT.offsetMax = Vector2.zero;

        _selectAllCheckmark = checkmarkObj.AddComponent<Image>();
        _selectAllCheckmark.sprite = Resources.Load<Sprite>("icon_check_mark");
        _selectAllCheckmark.color = Color.white;
        _selectAllCheckmark.preserveAspect = true;
        checkmarkObj.SetActive(false);

        // Button
        Button checkboxBtn = _selectAllCheckbox.AddComponent<Button>();
        checkboxBtn.transition = Selectable.Transition.None;
        checkboxBtn.onClick.AddListener(OnSelectAllClicked);

        // VR Collider
        var collider = _selectAllCheckbox.AddComponent<BoxCollider>();
        collider.size = new Vector3(checkboxSize, checkboxSize, 10);
        collider.center = new Vector3(checkboxSize / 2f, 0, -5);

        // Hover effect
        var hoverController = _selectAllCheckbox.AddComponent<HoverEffectController>();
        hoverController.TargetVisuals = _selectAllCheckbox.transform;
        var scaleEffect = new ScaleHoverEffect()
            .WithHoverScale(1.1f)
            .WithTransitionDuration(0.1f);
        hoverController.AddEffect(scaleEffect);

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(container.transform, false);
        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = labelRT.anchorMax = new Vector2(0, 0.5f);
        labelRT.pivot = new Vector2(0, 0.5f);
        labelRT.sizeDelta = new Vector2(labelWidth, checkboxSize);
        labelRT.anchoredPosition = new Vector2(checkboxSize + spacing, 0);

        TextMeshProUGUI labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
        labelTMP.text = "Select all";
        labelTMP.font = _font;
        labelTMP.fontSize = 28;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.color = Color.white;
        labelTMP.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private GameObject CreateEditModeActionButton(Transform parent, string label, string iconName, UnityEngine.Events.UnityAction onClick, float minTextWidth = 80f)
    {
        float btnHeight = 75f;
        float iconSize = 35f;
        float padding = 22f;
        float spacing = 11f;
        float textWidth = Mathf.Max(minTextWidth, label.Length * 18f);
        float btnWidth = padding + iconSize + spacing + textWidth + padding;

        GameObject btnObj = new GameObject($"Btn_{label}");
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(btnWidth, btnHeight);

        // Glass background
        Image bg = btnObj.AddComponent<Image>();
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_Aspect", btnWidth / btnHeight);
            mat.SetFloat("_CornerRadius", 0.48f);
            mat.SetFloat("_EdgePadding", 0.02f);
            mat.SetColor("_ColorA", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.25f));
            mat.SetColor("_ColorB", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.1f));
            mat.SetFloat("_GlassAlpha", 0.2f);
            mat.SetFloat("_FresnelStrength", 0.15f);
            bg.material = mat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.2f);
        }

        // Button
        Button btn = btnObj.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(onClick);

        // Hover effect
        var hoverController = btnObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.03f));

        // Icon
        Sprite iconSprite = Resources.Load<Sprite>(iconName);
        if (iconSprite != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(btnObj.transform, false);
            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0, 0.5f);
            iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);
            iconRT.anchoredPosition = new Vector2(padding, 0);

            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = iconSprite;
            iconImg.color = Color.white;
            iconImg.preserveAspect = true;
        }

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0, 0);
        textRT.anchorMax = new Vector2(1, 1);
        textRT.offsetMin = new Vector2(padding + iconSize + spacing, 0);
        textRT.offsetMax = new Vector2(-padding, 0);

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.font = _font;
        tmp.fontSize = 28;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;

        // VR Collider
        var collider = btnObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(btnWidth, btnHeight, 10);
        collider.center = new Vector3(btnWidth / 2f, 0, -5);

        return btnObj;
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
        config.placeholder = "Search media...";
        config.width = 990f;
        config.themeColor = _primaryColor;
        config.inputFontSize = 34;
        config.font = _font;
        config.borderWidth = 0.04f;
        config.glowWidth = 0.08f;
        config.glowIntensity = 4f;
        config.layerName = "UI";

        GameObject searchBar = VRInputFieldFactory.CreateInputField(
            parent as RectTransform,
            config,
            OnSearchValueChanged,
            OnSearchEndEdit
        );

        // Search Icon
        Sprite searchIcon = Resources.Load<Sprite>("icon_search");
        if (searchIcon != null)
        {
            GameObject iconObj = new GameObject("SearchIcon");
            Transform visuals = searchBar.transform.Find("InputBox/HitArea/Visuals");
            if (visuals != null)
            {
                iconObj.transform.SetParent(visuals, false);
            }
            else
            {
                iconObj.transform.SetParent(searchBar.transform, false);
            }

            RectTransform iconRT = iconObj.AddComponent<RectTransform>();
            float iconSize = 30f;
            float leftPadding = 26f;

            iconRT.anchorMin = new Vector2(0, 0.5f);
            iconRT.anchorMax = new Vector2(0, 0.5f);
            iconRT.pivot = new Vector2(0, 0.5f);
            iconRT.sizeDelta = new Vector2(iconSize, iconSize);
            iconRT.anchoredPosition = new Vector2(leftPadding, 0);

            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = searchIcon;
            iconImg.color = new Color(1f, 1f, 1f, 0.8f);

            // Adjust text area padding
            float textOffset = iconSize;
            Transform textArea = FindChildRecursive(searchBar.transform, "Text Area");
            if (textArea != null)
            {
                RectTransform textAreaRT = textArea.GetComponent<RectTransform>();
                textAreaRT.offsetMin = new Vector2(textOffset, textAreaRT.offsetMin.y);
            }
        }

        // Position centered with explicit height to match buttons
        RectTransform searchRT = searchBar.GetComponent<RectTransform>();
        searchRT.anchorMin = new Vector2(0.5f, 0.5f);
        searchRT.anchorMax = new Vector2(0.5f, 0.5f);
        searchRT.pivot = new Vector2(0.5f, 0.5f);
        searchRT.sizeDelta = new Vector2(config.width, 75f);
        searchRT.anchoredPosition = Vector2.zero;
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

    private void CreateViewOptionsPopup(Transform parent)
    {
        // Consistent popup config using UIConstants
        var config = new RTTPopupMenu.PopupConfig
        {
            width = _sortTriggerWidth * 2.5f,
            buttonHeight = 75f,
            sideSpacing = 35f,
            rowSpacing = 11f,
            fontSize = 24,
            iconSize = 26f,
            primaryColor = _primaryColor,
            accentColor = _accentColor,
            overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
            font = _font,
            layerName = UIConstants.VirtualObjectsLayer,
            // Consistent button styling across all popups
            borderWidth = UIConstants.PopupBorderWidth,
            glassAlpha = UIConstants.PopupGlassAlpha,
            buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
            buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
            buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
            buttonCornerRadius = UIConstants.PopupButtonCornerRadius
        };

        _viewOptionsPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);

        BuildViewOptionsPopupContent();

        // Position offset calculation (same approach as RTTFileManager)
        float popupWidth = config.width;

        // X: popup left edge aligned with sortTrigger button's left edge
        float sortTriggerCenterX = -_containerWidth / 4f - 203.5f;
        float sortTriggerLeftX = sortTriggerCenterX - (_sortTriggerWidth / 2f);
        float additionalOffset = 130f;
        float offsetX = sortTriggerLeftX + (popupWidth / 2f) + additionalOffset;

        // Y: popup top below header area with gap from sortTrigger button
        // Header takes approximately 200 logical pixels (Row1 + Row2 + spacing)
        // Frame center is at 0, top is at +containerHeight/2
        // Popup top should be at containerHeight/2 - headerHeight - gap
        float headerHeight = 200f;
        float gapFromButton = 15f; // Gap between sortTrigger button and popup top
        float estimatedPopupHeight = 360f; // SORT BY + Ascending (adjusted to match RTTFileManager gap)
        float offsetY = (_containerHeight / 2f) - headerHeight - gapFromButton - (estimatedPopupHeight / 2f);

        _viewOptionsPopup.SetPositionOffset(new Vector2(offsetX, offsetY));
    }

    private void BuildViewOptionsPopupContent()
    {
        if (_viewOptionsPopup == null) return;

        _viewOptionsPopup.Clear();

        // NO "DISPLAY AS" section - MediaLibrary is Grid-only

        // Section: Sort Options (2 columns like FileManager)
        // Name, Type, Created, Modified, Duration, Size (same as FileManager)
        string[] sortOptions = { "Name", "Type", "Created", "Modified", "Duration", "Size" };
        var sortButtons = new System.Collections.Generic.List<RTTPopupMenu.ButtonData>();
        foreach (string option in sortOptions)
        {
            bool isSelected = (_currentSortBy == option);
            sortButtons.Add(new RTTPopupMenu.ButtonData(option, () => SetSortBy(option), null, isSelected));
        }
        _viewOptionsPopup.AddSectionBlock("SORT BY", sortButtons, 2);

        // Full-width Order button
        Sprite orderIcon = Resources.Load<Sprite>(_isAscending ? "icon_ascending" : "icon_descending");
        string orderText = _isAscending ? "Ascending" : "Descending";
        _viewOptionsPopup.AddFullWidthButton(new RTTPopupMenu.ButtonData(orderText, ToggleSortOrder, orderIcon));

        // Build the popup
        _viewOptionsPopup.Build();
    }

    private void ToggleSortOrder()
    {
        SetAscending(!_isAscending);
    }

    private void ToggleViewOptionsPopup()
    {
        if (_viewOptionsPopup == null) return;

        if (_viewOptionsPopup.IsVisible)
        {
            _viewOptionsPopup.Hide();
        }
        else
        {
            BuildViewOptionsPopupContent();
            _viewOptionsPopup.Show();
        }
    }

    private void SetSortBy(string sortBy)
    {
        _currentSortBy = sortBy;
        if (_sortTriggerText != null) _sortTriggerText.text = sortBy;
        _controller?.SetSortBy(sortBy);
        _viewOptionsPopup?.Hide();
    }

    private void SetAscending(bool ascending)
    {
        _isAscending = ascending;
        _controller?.SetAscending(ascending);
        _viewOptionsPopup?.Hide();
    }

    // ===== GROUP BY POPUP =====

    private void ToggleGroupOptionsPopup()
    {
        if (_groupOptionsPopup == null) return;

        if (_groupOptionsPopup.IsVisible)
        {
            _groupOptionsPopup.Hide();
        }
        else
        {
            BuildGroupOptionsPopupContent();
            _groupOptionsPopup.Show();
        }
    }

    private void CreateGroupOptionsPopup(Transform parent)
    {
        // Consistent popup config using UIConstants
        var config = new RTTPopupMenu.PopupConfig
        {
            width = _sortTriggerWidth * 2.5f,
            buttonHeight = 75f,
            sideSpacing = 35f,
            rowSpacing = 11f,
            fontSize = 24,
            iconSize = 26f,
            primaryColor = _primaryColor,
            accentColor = _accentColor,
            overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
            font = _font,
            layerName = UIConstants.VirtualObjectsLayer,
            // Consistent button styling across all popups
            borderWidth = UIConstants.PopupBorderWidth,
            glassAlpha = UIConstants.PopupGlassAlpha,
            buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
            buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
            buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
            buttonCornerRadius = UIConstants.PopupButtonCornerRadius
        };

        _groupOptionsPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);

        BuildGroupOptionsPopupContent();

        // Position offset calculation (mirror of ViewOptionsPopup - align to right side)
        float popupWidth = config.width;
        float groupTriggerCenterX = _containerWidth / 4f + 203.5f;
        float groupTriggerRightX = groupTriggerCenterX + (_sortTriggerWidth / 2f);
        float offsetX = groupTriggerRightX - (popupWidth / 2f) - 130f;

        float headerHeight = 200f;
        float gapFromButton = 15f;
        float estimatedPopupHeight = 180f;  // Smaller popup, use smaller height to align top edge with SortBy
        float offsetY = (_containerHeight / 2f) - headerHeight - gapFromButton - (estimatedPopupHeight / 2f);

        _groupOptionsPopup.SetPositionOffset(new Vector2(offsetX, offsetY));
    }

    private void BuildGroupOptionsPopupContent()
    {
        if (_groupOptionsPopup == null) return;

        _groupOptionsPopup.Clear();

        // GROUP BY options (2 columns, 4 options) - removed None and Folder
        string[] groupOptions = { "Date Added", "Duration", "Resolution", "Format" };
        var groupButtons = new System.Collections.Generic.List<RTTPopupMenu.ButtonData>();

        foreach (string option in groupOptions)
        {
            bool isSelected = (_currentGroupBy == option);
            string capturedOption = option;
            groupButtons.Add(new RTTPopupMenu.ButtonData(option, () => SetGroupBy(capturedOption), null, isSelected));
        }
        _groupOptionsPopup.AddSectionBlock("GROUP BY", groupButtons, 2);

        _groupOptionsPopup.Build();
    }

    private void SetGroupBy(string groupBy)
    {
        _currentGroupBy = groupBy;
        if (_groupTriggerText != null) _groupTriggerText.text = groupBy;
        _controller?.SetGroupBy(groupBy);
        _groupOptionsPopup?.Hide();
    }
    #endregion

    #region Public Methods
    public void SetVideos(List<MediaVideoInfo> videos, List<MediaGroupInfo> groups = null)
    {
        if (_grid != null)
        {
            _grid.SetData(videos, groups);
        }
        int count = videos?.Count ?? 0;
        UpdateItemCount(count);
        UpdatePagination();
    }

    public void UpdateItemCount(int count)
    {
        if (_itemCountText != null)
        {
            _itemCountText.text = $"{count} item{(count != 1 ? "s" : "")}";
        }
    }

    public void UpdatePagination()
    {
        if (_grid == null || _pagination == null) return;
        _pagination.SetPage(_grid.CurrentPage, _grid.TotalPages);
    }

    public void SelectCategory(string categoryId)
    {
        if (_sidePanel != null)
        {
            _sidePanel.SelectItem(categoryId);
        }
    }

    /// <summary>
    /// Update the detail panel with video info.
    /// Called by controller based on hover/select state.
    /// </summary>
    /// <param name="video">Video info to display</param>
    /// <param name="forceRefresh">Force refresh even if same file path (used after data reload)</param>
    public void UpdateDetailPanel(MediaVideoInfo video, bool forceRefresh = false)
    {
        if (_detailPanel != null)
        {
            var mockFile = ConvertToMockFile(video);
            _detailPanel.UpdateInfo(mockFile, isCurrentFolder: false, forceRefresh: forceRefresh);
        }

        // Update current video for action buttons if this is the selected item (not just hovered)
        // The _currentVideo is set in OnGridVideoSelected, so we don't overwrite it here
        // This ensures the action buttons (Play, Favorite) work on the selected item

        // Update favourite icon state based on displayed video
        UpdateFavouriteButtonState(video.IsFavorite);

        // ActionBar is always visible for Media Library (when not in edit mode)
        // No need to toggle visibility here - it's managed in CreateMediaActionBar and ToggleEditMode
    }

    /// <summary>
    /// Clear the detail panel when no item is selected or hovered.
    /// Shows empty state in edit mode, otherwise keeps last item.
    /// </summary>
    public void ClearDetailPanel()
    {
        // In edit mode, show empty state
        if (_isEditMode)
        {
            _detailPanel?.ShowEmpty();
        }
        // Outside edit mode, keep showing last item (auto-select behavior)
    }

    /// <summary>
    /// Notify the view that a video was auto-selected by the controller.
    /// This is called when controller auto-selects first item on startup.
    /// </summary>
    public void NotifyVideoAutoSelected(MediaVideoInfo video)
    {
        _hasSelectedVideo = true;
        _currentVideo = video;

        // ActionBar is always visible for Media Library (managed in CreateMediaActionBar)
        // No need to toggle visibility here
    }

    public void UpdateBreadcrumb(string path)
    {
        // Fallback: find breadcrumb container from hierarchy if reference is lost
        if (_breadcrumbContainer == null)
        {
            Debug.LogWarning($"[RTTMediaLibrary] Breadcrumb container reference lost (instanceID={GetInstanceID()}), searching in hierarchy...");
            var row2 = _headerRT?.Find("Row2");
            if (row2 != null)
            {
                _breadcrumbContainer = row2.Find("Breadcrumbs");
                Debug.Log($"[RTTMediaLibrary] Found breadcrumb container from hierarchy: {_breadcrumbContainer != null}");
            }
        }

        if (_breadcrumbContainer == null)
        {
            Debug.LogWarning("[RTTMediaLibrary] UpdateBreadcrumb: _breadcrumbContainer is null and could not be found!");
            return;
        }

        // Clear existing breadcrumbs (use reverse for loop to avoid collection modification issues)
        for (int i = _breadcrumbContainer.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(_breadcrumbContainer.GetChild(i).gameObject);
        }

        // Parse path into segments (split by " > ")
        string[] segments = path.Split(new[] { " > " }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            segments = new[] { path };
        }

        Debug.Log($"[RTTMediaLibrary] Creating {segments.Length} breadcrumb segments for: {path}");

        // Breadcrumb button dimensions (match RTTFileManager)
        float btnHeight = 75f;  // Match RTTFileManager
        float btnWidth = _sortTriggerWidth * 1.2f;  // Use consistent width like RTTFileManager
        float overlap = btnHeight * 0.45f;

        // Create buttons in REVERSE order like RTTFileManager for proper GraphicRaycaster priority
        // Left buttons created LAST = higher sibling index = hit first
        float effectiveWidth = btnWidth - overlap;
        int totalCount = segments.Length;

        for (int i = totalCount - 1; i >= 0; i--)
        {
            string label = segments[i].Trim();
            bool isFirst = (i == 0);
            bool isLast = (i == totalCount - 1);

            // Create pill button
            GameObject btn = CreateBreadcrumbPill(label, btnWidth, btnHeight, isFirst, isLast, i);

            RectTransform btnRT = btn.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0, 0.5f);
            btnRT.anchorMax = new Vector2(0, 0.5f);
            btnRT.pivot = new Vector2(0, 0.5f);

            // X position: each button offset by effectiveWidth
            float xPos = i * effectiveWidth;
            btnRT.anchoredPosition = new Vector2(xPos, 0);

            // Z-position for visual layering (left buttons closer to camera)
            Vector3 pos = btnRT.localPosition;
            pos.z = i * -0.5f;
            btnRT.localPosition = pos;
        }

        Debug.Log($"[RTTMediaLibrary] Created {totalCount} breadcrumb pills for path: {path}");
    }

    /// <summary>
    /// Create a pill-shaped breadcrumb button (styled like RTTFileManager but not clickable).
    /// </summary>
    private GameObject CreateBreadcrumbPill(string label, float width, float height, bool isFirst, bool isLast, int index)
    {
        // Only use accent color for the last item if there are multiple breadcrumbs
        // When there's only one breadcrumb (isFirst && isLast), use primary color
        Color btnColor = (isLast && !isFirst) ? _accentColor : _primaryColor;

        GameObject btnObj = new GameObject($"Crumb_{label}");
        btnObj.layer = LayerMask.NameToLayer("UI");
        btnObj.transform.SetParent(_breadcrumbContainer, false);

        RectTransform btnRT = btnObj.AddComponent<RectTransform>();
        btnRT.sizeDelta = new Vector2(width, height);

        // Background image with shader
        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.raycastTarget = false; // Not clickable

        float aspect = width / height;
        float glassAlpha = 0.2f;

        if (isFirst)
        {
            // First button: rounded rect
            Shader pillShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (pillShader != null)
            {
                Material mat = new Material(pillShader);
                mat.SetFloat("_Aspect", aspect);
                mat.SetFloat("_CornerRadius", 0.48f);
                mat.SetFloat("_EdgePadding", 0.02f);
                Color colorA = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 1.5f);
                Color colorB = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 0.5f);
                mat.SetColor("_ColorA", colorA);
                mat.SetColor("_ColorB", colorB);
                mat.SetFloat("_GlassAlpha", glassAlpha);
                mat.SetFloat("_FresnelStrength", 0.15f);
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
            // Subsequent buttons: chevron shape
            Shader chevronShader = Shader.Find("Custom/ChevronBackground");
            if (chevronShader != null)
            {
                Material mat = new Material(chevronShader);
                mat.SetFloat("_Aspect", aspect);
                mat.SetFloat("_EdgePadding", 0.02f);
                mat.SetColor("_BackgroundColor", btnColor);
                mat.SetFloat("_BackgroundAlpha", glassAlpha);
                mat.SetFloat("_EdgeGlow", 0.15f);
                mat.SetFloat("_CenterGlow", 0.1f);
                bgImage.material = mat;
                bgImage.color = Color.white;
            }
            else
            {
                bgImage.color = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha);
            }
        }

        // Text label
        GameObject textObj = new GameObject("Text");
        textObj.layer = LayerMask.NameToLayer("UI");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;

        float curveR = height * 0.5f;
        if (isFirst)
        {
            textRT.offsetMin = new Vector2(curveR * 0.85f, 0);
            textRT.offsetMax = new Vector2(-curveR * 0.75f, 0);
        }
        else
        {
            textRT.offsetMin = new Vector2(curveR * 1.1f, 0);
            textRT.offsetMax = new Vector2(-curveR * 0.6f, 0);
        }

        TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 24;
        txt.font = _font;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;

        return btnObj;
    }
    #endregion

    #region Edit Mode
    private void ToggleEditMode()
    {
        _isEditMode = !_isEditMode;

        // Toggle visibility
        if (_breadcrumbContainer != null)
            _breadcrumbContainer.gameObject.SetActive(!_isEditMode);
        if (_editControlsContainer != null)
            _editControlsContainer.SetActive(_isEditMode);
        if (_itemCountText != null)
            _itemCountText.gameObject.SetActive(!_isEditMode);
        if (_selectedCountText != null)
            _selectedCountText.gameObject.SetActive(_isEditMode);

        // Update edit button icon
        UpdateEditButtonVisual();

        // Show/hide checkboxes on items
        _grid?.SetEditMode(_isEditMode);

        // Handle detail panel and action bar visibility based on edit mode
        if (_isEditMode)
        {
            // Entering edit mode: clear controller's selected video state
            _controller?.ClearSelectedVideo();

            // Clear detail panel and hide action bar
            _detailPanel?.ShowEmpty();
            _mediaActionBar?.SetVisible(false);

            // Update selected count when entering edit mode
            UpdateSelectedCountText();
        }
        else
        {
            // Exiting edit mode: clear checkbox selection
            ClearSelection();
            UpdateSelectAllCheckmark();
            UpdateSelectedCountText();

            // Restore detail panel if there was a selected video
            if (_hasSelectedVideo && _currentVideo.HasValue)
            {
                var mockFile = ConvertToMockFile(_currentVideo.Value);
                _detailPanel?.UpdateInfo(mockFile, isCurrentFolder: false);
                _mediaActionBar?.SetVisible(true);
                // Restore controller's selected video so hover/unhover works correctly
                _controller?.SelectVideo(_currentVideo.Value);
            }
            else
            {
                // Check if grid has a visually selected video
                var selectedVideo = _grid?.SelectedVideo;
                if (selectedVideo.HasValue)
                {
                    // Restore _hasSelectedVideo since there's a visually selected item
                    _hasSelectedVideo = true;
                    _currentVideo = selectedVideo.Value;

                    var mockFile = ConvertToMockFile(selectedVideo.Value);
                    _detailPanel?.UpdateInfo(mockFile, isCurrentFolder: false);
                    _mediaActionBar?.SetVisible(true);
                    // Restore controller's selected video
                    _controller?.SelectVideo(selectedVideo.Value);
                }
                else
                {
                    // No selected video, show empty detail panel
                    _detailPanel?.ShowEmpty();
                }
            }
        }
    }

    private void UpdateEditButtonVisual()
    {
        if (_editButton == null) return;

        var iconImg = _editButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.sprite = Resources.Load<Sprite>(_isEditMode ? "icon_check_mark" : "icon_edit");
        }
    }

    private void UpdateSelectedCountText()
    {
        if (_selectedCountText != null)
        {
            int count = _selectedItems.Count;
            _selectedCountText.text = count > 0 ? $"{count} selected" : "";
        }
    }

    private void UpdateActionButtonsState()
    {
        bool hasSelection = _selectedItems.Count > 0;
        bool singleSelection = _selectedItems.Count == 1;

        // Rename requires single selection
        SetButtonEnabled(_renameButton, _renameButtonCG, _renameButtonHover, singleSelection);

        // Delete requires any selection
        SetButtonEnabled(_deleteButton, _deleteButtonCG, _deleteButtonHover, hasSelection);
    }

    private void SetButtonEnabled(Button btn, CanvasGroup cg, HoverEffectController hover, bool enabled)
    {
        if (btn != null) btn.interactable = enabled;
        if (cg != null)
        {
            cg.alpha = enabled ? 1f : 0.4f;
            cg.interactable = enabled;
            cg.blocksRaycasts = enabled;
        }
        if (hover != null) hover.enabled = enabled;

        var collider = btn?.GetComponent<BoxCollider>();
        if (collider != null) collider.enabled = enabled;
    }

    private void UpdateSelectAllCheckmark()
    {
        if (_selectAllCheckmark == null) return;
        bool allSelected = _grid != null && _grid.AreAllSelected();
        _selectAllCheckmark.gameObject.SetActive(allSelected);
    }

    public void ClearSelection()
    {
        _selectedItems.Clear();
        _grid?.SetAllSelected(false);
        UpdateSelectAllCheckmark();
        UpdateSelectedCountText();
        UpdateActionButtonsState();
    }

    public void AddToSelection(string videoPath)
    {
        _selectedItems.Add(videoPath);
        UpdateSelectAllCheckmark();
        UpdateSelectedCountText();
        UpdateActionButtonsState();
    }

    public void RemoveFromSelection(string videoPath)
    {
        _selectedItems.Remove(videoPath);
        UpdateSelectAllCheckmark();
        UpdateSelectedCountText();
        UpdateActionButtonsState();
    }

    public bool IsSelected(string videoPath)
    {
        return _selectedItems.Contains(videoPath);
    }

    public HashSet<string> GetSelectedItems()
    {
        return new HashSet<string>(_selectedItems);
    }
    #endregion

    #region Event Handlers
    private void OnCategorySelected(string categoryId)
    {
        // Exit edit mode when changing category
        if (_isEditMode)
        {
            ToggleEditMode();
        }

        // Clear video selection when changing category
        _hasSelectedVideo = false;
        // ActionBar stays visible for Media Library - don't hide on category change

        _controller?.SelectCategory(categoryId);
        UpdateBreadcrumbForCategory(categoryId);
    }

    private void UpdateBreadcrumbForCategory(string categoryId)
    {
        string breadcrumbText;

        switch (categoryId)
        {
            case "all":
                breadcrumbText = "All Media";
                break;
            case "videos":
                breadcrumbText = "All Media > Videos";
                break;
            case "images":
                breadcrumbText = "All Media > Images";
                break;
            case "audio":
                breadcrumbText = "All Media > Music";
                break;
            case "recent":
                breadcrumbText = "Recent";
                break;
            case "favorites":
                breadcrumbText = "Favorites";
                break;
            case "playlists":
                breadcrumbText = "Playlists";
                break;
            default:
                breadcrumbText = categoryId;
                break;
        }

        UpdateBreadcrumb(breadcrumbText);
    }

    private void OnGridVideoSelected(MediaVideoInfo video)
    {
        // In edit mode, don't process video selection (checkboxes are handled separately)
        if (_isEditMode) return;

        _hasSelectedVideo = true;
        _currentVideo = video;

        // Notify controller of selection (controller manages detail panel via hover/select logic)
        _controller?.SelectVideo(video);

        // Update favourite icon state
        UpdateFavouriteButtonState(video.IsFavorite);

        // Show action bar with fade animation only if Media app is the current visible app
        // During dwell pre-loading, CurrentVisibleAppId is null (main menu) or another app
        // OnEnable will show ActionBar when app is actually opened/re-opened
        bool isMediaAppVisible = RTTManager.Instance?.CurrentVisibleAppId == "media";
        if (isMediaAppVisible)
        {
            _mediaActionBar?.ShowWithFade();
        }
    }

    private void OnGridVideoHoverEnter(MediaVideoInfo video)
    {
        _controller?.HoverVideo(video);
    }

    private void OnGridVideoHoverExit(MediaVideoInfo video)
    {
        _controller?.UnhoverVideo(video);
    }

    private void OnSelectionChanged()
    {
        // Sync _selectedItems with grid's selection
        _selectedItems.Clear();
        if (_grid != null)
        {
            foreach (var path in _grid.GetSelectedPaths())
            {
                _selectedItems.Add(path);
            }
        }

        UpdateSelectAllCheckmark();
        UpdateSelectedCountText();
        UpdateActionButtonsState();
    }

    /// <summary>
    /// Convert MediaVideoInfo to MockFile for RTTFileDetail compatibility.
    /// </summary>
    private MockFile ConvertToMockFile(MediaVideoInfo video)
    {
        return new MockFile
        {
            Path = video.Path,
            Name = System.IO.Path.GetFileName(video.Path),  // Full filename with extension
            Type = System.IO.Path.GetExtension(video.Path).TrimStart('.').ToUpperInvariant(),
            Size = video.FileSizeBytes,
            Modified = video.DateAdded,
            IsFolder = false,
            Width = video.Width,
            Height = video.Height,
            Duration = video.Duration
        };
    }

    private void UpdateFavouriteButtonState(bool isFavourite)
    {
        if (_mediaActionBar != null)
        {
            _mediaActionBar.UpdateFavouriteState(isFavourite);
        }
    }

    private void OnGridVideoDoubleClicked(MediaVideoInfo video)
    {
        OnVideoPlayRequested?.Invoke(video);
    }

    private void OnGridPageChanged(int currentPage, int totalPages)
    {
        UpdatePagination();
    }

    // Action Button Handlers
    private void OnPlayButtonClicked()
    {
        if (_currentVideo.HasValue)
        {
            OnVideoPlayRequested?.Invoke(_currentVideo.Value);
        }
    }

    private void OnFavouriteButtonClicked()
    {
        if (_currentVideo.HasValue)
        {
            _controller?.ToggleFavorite(_currentVideo.Value);

            // Toggle the state locally for immediate UI feedback
            var video = _currentVideo.Value;
            video.IsFavorite = !video.IsFavorite;
            _currentVideo = video;
            UpdateFavouriteButtonState(video.IsFavorite);
        }
    }

    private void OnPlaylistButtonClicked()
    {
        if (_currentVideo.HasValue)
        {
            Debug.Log($"[RTTMediaLibrary] Add to playlist requested for: {_currentVideo.Value.Title}");
            // TODO: Show playlist selection dialog
        }
    }

    private void OnSearchValueChanged(string value)
    {
        _controller?.Search(value);
    }

    private void OnSearchEndEdit(string value)
    {
        // Optional: Additional handling when search is submitted
    }

    private void OnCloseButtonClicked()
    {
        OnCloseRequested?.Invoke();
    }

    private void OnEditButtonClicked()
    {
        ToggleEditMode();
    }

    private void OnSelectAllClicked()
    {
        if (_grid == null) return;

        bool allSelected = _grid.AreAllSelected();
        _grid.SetAllSelected(!allSelected);

        // Sync _selectedItems with grid's selection
        _selectedItems.Clear();
        foreach (var path in _grid.GetSelectedPaths())
        {
            _selectedItems.Add(path);
        }

        UpdateSelectAllCheckmark();
        UpdateSelectedCountText();
        UpdateActionButtonsState();
    }

    private void OnRenameClicked()
    {
        if (_selectedItems.Count != 1)
        {
            Debug.LogWarning("[RTTMediaLibrary] Rename requires exactly one selected item");
            return;
        }

        string selectedPath = null;
        foreach (var path in _selectedItems)
        {
            selectedPath = path;
            break;
        }
        Debug.Log($"[RTTMediaLibrary] Rename requested for: {selectedPath}");
        // TODO: Show rename dialog
    }

    private void OnDeleteClicked()
    {
        if (_selectedItems.Count == 0)
        {
            Debug.LogWarning("[RTTMediaLibrary] No items selected for delete");
            return;
        }

        Debug.Log($"[RTTMediaLibrary] Delete requested for {_selectedItems.Count} items");
        // TODO: Show confirmation dialog
    }
    #endregion

    #region Helper Methods
    // Cached rounded rectangle sprite for checkboxes
    private static Sprite _cachedRoundedSprite;

    private static Sprite GetRoundedRectSprite()
    {
        if (_cachedRoundedSprite != null) return _cachedRoundedSprite;

        int size = 64;
        int radius = 6;  // Small radius for square-ish checkbox with slight rounding
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float halfSize = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - halfSize + 0.5f);
                float dy = Mathf.Abs(y - halfSize + 0.5f);

                float innerHalfX = halfSize - radius;
                float innerHalfY = halfSize - radius;

                float qx = Mathf.Max(dx - innerHalfX, 0f);
                float qy = Mathf.Max(dy - innerHalfY, 0f);
                float dist = Mathf.Sqrt(qx * qx + qy * qy) - radius;

                float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        int border = radius + 2;
        _cachedRoundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            Vector2.one * 0.5f,
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)
        );

        return _cachedRoundedSprite;
    }
    #endregion
}
