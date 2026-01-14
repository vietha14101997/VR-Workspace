using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

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
        float headerHeight = panelHeight * 0.18f; // 18% Header
        float bottomPadding = panelHeight * 0.02f; // 2% Bottom Padding

        // 1. Create Body Object (Visual Container for Grid)
        // Must be created first or set as first sibling so it renders BEHIND header
        GameObject bodyObj = new GameObject("Body");
        bodyObj.transform.SetParent(_menuFrame.ContentContainer, false);
        RectTransform bodyRT = bodyObj.AddComponent<RectTransform>();
        bodyRT.anchorMin = Vector2.zero;
        bodyRT.anchorMax = Vector2.one;
        // Top offset = -headerHeight (Starts below header)
        // Bottom offset = bottomPadding (Ends above bottom edge)
        bodyRT.offsetMax = new Vector2(0, -headerHeight); 
        bodyRT.offsetMin = new Vector2(0, bottomPadding);
        
        // Add Mask to Body to strictly Clip everything inside it (Double safety)
        bodyObj.AddComponent<RectMask2D>(); 

        // 2. Create Header Object
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(_menuFrame.ContentContainer, false);
        RectTransform headerRT = headerObj.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.anchoredPosition = Vector2.zero;
        headerRT.sizeDelta = new Vector2(0, headerHeight);

        // 3. Create Search Bar inside Header
        CreateSearchBar(headerRT);

        // 4. Create Grid Inside Body
        GameObject gridObj = new GameObject("FileGrid");
        gridObj.transform.SetParent(bodyRT, false); // Parent to Body
        RectTransform gridRT = gridObj.AddComponent<RectTransform>();
        gridRT.anchorMin = Vector2.zero;
        gridRT.anchorMax = Vector2.one; 
        gridRT.offsetMin = Vector2.zero; // Fill Body
        gridRT.offsetMax = Vector2.zero; // Fill Body

        _fileGrid = gridObj.AddComponent<RTTFileGrid>();
        // Calculate size based on BODY size (which is Panel - Header - Padding)
        float bodyHeight = panelHeight - headerHeight - bottomPadding;
        _fileGrid.Initialize(_controller, contentSize.x, bodyHeight);
        
        // Render Order: Header Last (Top), Body First (Bottom)
        headerObj.transform.SetAsLastSibling();
        bodyObj.transform.SetAsFirstSibling();
        
        // Notify Controller that Grid is ready
        _controller.OnViewReady();
    }
    
    private void CreateSearchBar(Transform parent)
    {
        if (parent == null) return;
        
        // Config
        var config = new VRInputFieldFactory.InputFieldConfig();
        config.label = ""; // No label for header search
        config.placeholder = "Search files...";
        config.width = 600f; // Wide search bar
        config.themeColor = _accentColor;
        config.inputFontSize = 28;
        config.font = _font;
        config.glowIntensity = 1.5f;
        config.layerName = "UI"; // Ensure correct layer
        
        // Create
        GameObject searchBar = VRInputFieldFactory.CreateInputField(
            parent as RectTransform, 
            config, 
            OnSearchValueChanged, 
            OnSearchEndEdit
        );
        
        // Position at Middle Center of Header
        RectTransform rt = searchBar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero; // Center in Header
    }
    
    private void OnSearchValueChanged(string value)
    {
        // Optional: Real-time search
        _controller.SearchFiles(value);
    }
    
    private void OnSearchEndEdit(string value)
    {
        // Optional: Commit search
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
